using System.Text.RegularExpressions;
using GedCore.Apply;

namespace GedCore.Points;

/// <summary>
/// Decides which cards a changeset closes by what it wrote to the tree. An enrich card pays for a birth, death or
/// marriage only: a card naming one of them closes only when that fact was written on its subject; a card naming no
/// single fact is the deal's Enrich person card and pays its points for each gap the deal listed that the
/// changeset closed; a new-parent, new-spouse or new-child card closes only when the family gained that
/// relative, allocated one-to-one by <see cref="NewRelativeCardAllocator"/>; a biography card closes when its subject's
/// biography gained new information (a biography note's new citation or text, or a cited fact beyond birth, death and
/// marriage added, improved or corrected), at most once per person; a changeset that renames a
/// card's subject closes nothing for it, and a card the deal withholds is never closed.
/// </summary>
public static class CardScorer
{
    public const string BiographyClass = "biography";

    /// <summary>How one card fared: closed with what it <c>Wrote</c> (and its <c>Points</c>, when they differ from the card's), or open with a reason.</summary>
    sealed record Closure(string? Wrote, string? Reason, IReadOnlyList<string> Added, int? Points = null)
    {
        public static Closure Closes(string wrote, IReadOnlyList<string>? added = null, int? points = null) => new(wrote, null, added ?? [], points);
        public static Closure Open(string reason) => new(null, reason, []);
    }

    // Without the deal no gap is known to have closed and no card is known to be withheld.
    sealed class UnknownDeal : ICardDeal
    {
        public IReadOnlyList<EnrichGap> ClosedEnrichGaps(string xref) => [];
        public bool RecordedNeverMarried(string xref) => false;
        public string? WithheldReason(PointsCard card, string? family) => null;
    }

    static readonly (Regex Gap, string Tag)[] GapTags =
    [
        (new Regex("birth|born", RegexOptions.Compiled), "BIRT"),
        (new Regex("death|died", RegexOptions.Compiled), "DEAT"),
        (new Regex("marriage|married", RegexOptions.Compiled), "MARR"),
    ];

    /// <param name="deal">The deal on the tree before and after the changeset; without it no Enrich person gap is known to have closed.</param>
    public static PointsPreview Preview(IEnumerable<PointsCard> cards, TreeChanges changes, ICardDeal? deal = null)
    {
        deal ??= new UnknownDeal();
        var list = cards.ToList();
        var relatives = NewRelativeCardAllocator.Allocate(list, changes, deal);
        var biographyAwards = new Dictionary<string, string>(StringComparer.Ordinal);
        var paidGaps = new Dictionary<EnrichGap, string>();
        var closed = new List<ClosedCard>();
        var notClosed = new List<UnclosedCard>();
        var mismatches = new List<SubjectMismatch>();

        void Open(PointsCard card, string reason) => notClosed.Add(new UnclosedCard(card.Key, reason) { Family = card.Family });

        for (int index = 0; index < list.Count; index++)
        {
            var card = list[index];
            if (deal.WithheldReason(card, card.Family) is { } withheld)
            {
                Open(card, withheld);
                continue;
            }

            if (CardText.Normalize(card.Class) == "enrich-person" && RenamedAway(card, changes) is { } appliedName)
            {
                mismatches.Add(new SubjectMismatch(card.Key, card.Name!, appliedName));
                Open(card, $"the changeset renames the subject to \"{appliedName}\"");
                continue;
            }

            var closure = relatives.TryGetValue(index, out var allocated) ? ToClosure(allocated) : Close(card, changes, deal, paidGaps);
            if (closure.Wrote is not null && MintedOtherName(card, changes, closure.Added) is { } minted)
            {
                mismatches.Add(new SubjectMismatch(card.Key, card.Name!, minted));
                Open(card, $"the changeset mints \"{minted}\", not the card's \"{card.Name}\"");
            }
            else if (closure.Wrote is not null && IsBiography(card) && !biographyAwards.TryAdd(card.Xref, card.Key))
                Open(card, $"card {biographyAwards[card.Xref]} already takes this changeset's one Biography award for {card.Xref}");
            else if (closure.Wrote is not null)
                closed.Add(new ClosedCard(card.Key, closure.Points ?? card.Points, closure.Wrote) { Family = card.Family });
            else Open(card, closure.Reason!);
        }
        return new PointsPreview(closed, notClosed, mismatches, closed.Sum(c => c.Points));
    }

    static bool IsBiography(PointsCard card) => CardText.Normalize(card.Class) == BiographyClass;

    static string? RenamedAway(PointsCard card, TreeChanges changes) =>
        card.Name is not null
        && changes.Names.TryGetValue(card.Xref, out var change)
        && change is { Before: not null, After: not null }
        && !string.Equals(CardText.Fold(change.After), CardText.Fold(card.Name), StringComparison.OrdinalIgnoreCase)
            ? change.After
            : null;

    // A new-relative card's name is the person to mint: a person the changeset creates under it must carry that name exactly.
    static string? MintedOtherName(PointsCard card, TreeChanges changes, IReadOnlyList<string> added)
    {
        if (card.Name is null) return null;
        var minted = added
            .Where(x => changes.Names.TryGetValue(x, out var n) && n.Before is null && n.After is not null)
            .Select(x => changes.Names[x].After!)
            .ToList();
        return minted.Count > 0 && !minted.Any(n => CardText.Fold(n) == CardText.Fold(card.Name)) ? minted[0] : null;
    }

    static Closure Close(PointsCard card, TreeChanges changes, ICardDeal deal, Dictionary<EnrichGap, string> paidGaps) =>
        CardText.Normalize(card.Class) switch
        {
            "enrich-person" => CloseEnrich(card, changes, deal, paidGaps),
            BiographyClass => CloseBiography(card, changes, deal),
            _ => Closure.Open($"unknown card class \"{card.Class}\""),
        };

    // An exact burial that stands for the death is paid by the Enrich person card as the death, so it is not biography too.
    static Closure CloseBiography(PointsCard card, TreeChanges changes, ICardDeal deal)
    {
        var write = changes.Biographies.FirstOrDefault(b => b.Record == card.Xref);
        if (write is not null && write.Facts.Any(f => f.Tag == "BURI") && deal.ClosedEnrichGaps(card.Xref).Any(g => g.Tag == "DEAT"))
            write = write with { Facts = [.. write.Facts.Where(f => f.Tag != "BURI")] };
        return write is not null && (write.NewCitations.Count > 0 || write.NewText || write.Facts.Count > 0)
            ? Closure.Closes(string.Join("; ", BiographyWrote(card.Xref, write)))
            : Closure.Open("no biography note on the subject added text or a citation (source and page) its notes did not already carry, " +
                           "and no cited fact beyond birth, death and marriage was added, improved or corrected on the subject or their family");
    }

    static IEnumerable<string> BiographyWrote(string xref, BiographyWrite write)
    {
        if (write.NewCitations.Count > 0) yield return $"biography note on {xref} citing {string.Join("; ", write.NewCitations)}";
        else if (write.NewText) yield return $"biography note on {xref} with text none of its notes carried";
        if (write.Facts.Count > 0) yield return $"cited {string.Join(", ", write.Facts)}";
    }

    static Closure ToClosure(NewRelativeOutcome outcome) =>
        outcome.Wrote is not null ? Closure.Closes(outcome.Wrote, outcome.Added) : Closure.Open(outcome.Reason!);

    static Closure CloseEnrich(PointsCard card, TreeChanges changes, ICardDeal deal, Dictionary<EnrichGap, string> paidGaps)
    {
        string gap = CardText.Normalize(card.Gap ?? "");
        if (IsGenericGap(gap)) return CloseListedGaps(card, changes, deal, paidGaps);

        // An Enrich person card pays for a birth, death or marriage only; any other gap names nothing it pays for.
        if (!TryReadGap(card.Gap, out string tag, out string? part))
            return Closure.Open($"the card's gap \"{card.Gap}\" does not name a birth, death or marriage fact");

        var onSubject = changes.Facts.Where(f => WrittenOnSubject(f, card.Xref, changes)).ToList();
        var match = onSubject.FirstOrDefault(f => f.Tag == tag && Carries(f, part));
        if (match is not null) return Closure.Closes($"{tag} {match.Record} ({Improved(match)})");

        string others = string.Join(", ", onSubject.Select(f => f.Tag).Distinct());
        return Closure.Open(others.Length == 0
            ? $"no {tag}{PartSuffix(part)} written on the subject"
            : $"no {tag}{PartSuffix(part)} written on the subject (wrote {others})");
    }

    // A gapless card, or the queue gap "enrich-fact", names no single fact: it is the deal's Enrich person card.
    static bool IsGenericGap(string gap) => gap is "" or "enrich" or "enrich-fact" or "enrich-facts" or "fact" or "facts";

    // Pays the card's points for each gap the deal listed for the subject that the changeset closed, plus its difficulty
    // bonus once. A marriage's gaps sit on one spouse's card, so a gap an earlier card was paid for is not paid again.
    static Closure CloseListedGaps(PointsCard card, TreeChanges changes, ICardDeal deal, Dictionary<EnrichGap, string> paidGaps)
    {
        var closedGaps = deal.ClosedEnrichGaps(card.Xref);
        var unpaid = closedGaps.Where(g => !paidGaps.ContainsKey(g)).ToList();
        if (unpaid.Count == 0)
            return Closure.Open(closedGaps.Count > 0
                ? $"the gaps this changeset closes are already paid to card {paidGaps[closedGaps[0]]}"
                : NoGapClosedReason(card, changes));

        foreach (var gap in unpaid) paidGaps[gap] = card.Key;
        string wrote = string.Join("; ", unpaid.GroupBy(g => (g.Tag, g.Record))
            .Select(fact => $"{fact.Key.Tag} {fact.Key.Record} ({string.Join(" and ", fact.Select(g => g.Part))})"));
        return Closure.Closes(wrote, points: card.Points * unpaid.Count + card.Bonus);
    }

    static string NoGapClosedReason(PointsCard card, TreeChanges changes)
    {
        string wrote = string.Join(", ", changes.Facts.Where(f => WrittenOnSubject(f, card.Xref, changes)).Select(f => f.Tag).Distinct());
        return "no gap the deal lists for the subject is closed: a birth, death or marriage date or place made exact, or its first citation"
            + (wrote.Length == 0 ? "" : $" (wrote {wrote})");
    }

    static string Improved(FactWrite fact) =>
        string.Join(" and ", new[]
        {
            fact.DateImproved ? $"{fact.DateImprovement.ToString().ToLowerInvariant()} date" : null,
            fact.PlaceImproved ? "place" : null, fact.CitationImproved ? "citation" : null,
        }.OfType<string>());

    static bool WrittenOnSubject(FactWrite fact, string subject, TreeChanges changes) =>
        fact.Record == subject
        || (changes.Families.TryGetValue(fact.Record, out var family) && family.Partners.Contains(subject));

    static bool Carries(FactWrite fact, string? part) =>
        part switch { "date" => fact.DateImproved, "place" => fact.PlaceImproved, _ => fact.Improvements > 0 };

    static string PartSuffix(string? part) => part is null ? "" : $" {part}";

    static string? ReadTag(string gap) => GapTags.FirstOrDefault(g => g.Gap.IsMatch(gap)).Tag;

    static bool TryReadGap(string? gap, out string tag, out string? part)
    {
        string normalized = CardText.Normalize(gap ?? "");
        tag = ReadTag(normalized) ?? "";
        part = normalized.Split('-', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(s => s is "date" or "place");
        return tag.Length > 0;
    }
}
