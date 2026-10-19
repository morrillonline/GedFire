using System.Text.RegularExpressions;
using GedCore.Apply;

namespace GedCore.Points;

/// <summary>
/// Decides which cards a changeset closes by what it wrote to the tree. An enrich
/// card closes only when the fact it names was written on its subject (a card naming no
/// single fact closes on any improved fact, at its points per improvement); a new-parent,
/// new-spouse or new-child card closes only when the family gained that relative, allocated
/// one-to-one by <see cref="NewRelativeCardAllocator"/>; a changeset that renames a card's
/// subject closes nothing for it.
/// </summary>
public static class CardScorer
{
    /// <summary>How one card fared: closed with what it <c>Wrote</c> (and its <c>Points</c>, when they differ from the card's), or open with a reason.</summary>
    sealed record Closure(string? Wrote, string? Reason, IReadOnlyList<string> Added, int? Points = null)
    {
        public static Closure Closes(string wrote, IReadOnlyList<string>? added = null, int? points = null) => new(wrote, null, added ?? [], points);
        public static Closure Open(string reason) => new(null, reason, []);
    }

    // Gaps asking for a citation, a grade or a ruling rather than a new fact: any touch of the subject closes them.
    static readonly Regex NonFactGap = new("citation|verif|quay|classif|conflict|uncited", RegexOptions.Compiled);

    static readonly (Regex Gap, string Tag)[] GapTags =
    [
        (new Regex("birth|born", RegexOptions.Compiled), "BIRT"),
        (new Regex("death|died", RegexOptions.Compiled), "DEAT"),
        (new Regex("marriage|married", RegexOptions.Compiled), "MARR"),
        (new Regex("burial|buri", RegexOptions.Compiled), "BURI"),
        (new Regex("name", RegexOptions.Compiled), "NAME"),
    ];

    public static PointsPreview Preview(IEnumerable<PointsCard> cards, TreeChanges changes)
    {
        var list = cards.ToList();
        var relatives = NewRelativeCardAllocator.Allocate(list, changes);
        var closed = new List<ClosedCard>();
        var notClosed = new List<UnclosedCard>();
        var mismatches = new List<SubjectMismatch>();

        for (int index = 0; index < list.Count; index++)
        {
            var card = list[index];
            if (CardText.Normalize(card.Class) == "enrich-person" && !NamesTheSubject(card) && RenamedAway(card, changes) is { } appliedName)
            {
                mismatches.Add(new SubjectMismatch(card.Key, card.Name!, appliedName));
                notClosed.Add(new UnclosedCard(card.Key, $"the changeset renames the subject to \"{appliedName}\""));
                continue;
            }

            var closure = relatives.TryGetValue(index, out var allocated) ? ToClosure(allocated) : Close(card, changes);
            if (closure.Wrote is not null && MintedOtherName(card, changes, closure.Added) is { } minted)
            {
                mismatches.Add(new SubjectMismatch(card.Key, card.Name!, minted));
                notClosed.Add(new UnclosedCard(card.Key, $"the changeset mints \"{minted}\", not the card's \"{card.Name}\""));
            }
            else if (closure.Wrote is not null) closed.Add(new ClosedCard(card.Key, closure.Points ?? card.Points, closure.Wrote));
            else notClosed.Add(new UnclosedCard(card.Key, closure.Reason!));
        }
        return new PointsPreview(closed, notClosed, mismatches, closed.Sum(c => c.Points));
    }

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

    static Closure Close(PointsCard card, TreeChanges changes) =>
        CardText.Normalize(card.Class) switch
        {
            "enrich-person" => CloseEnrich(card, changes),
            _ => Closure.Open($"unknown card class \"{card.Class}\""),
        };

    static Closure ToClosure(NewRelativeOutcome outcome) =>
        outcome.Wrote is not null ? Closure.Closes(outcome.Wrote, outcome.Added) : Closure.Open(outcome.Reason!);

    static bool NamesTheSubject(PointsCard card) => card.Gap is not null && ReadTag(CardText.Normalize(card.Gap)) == "NAME";

    static Closure CloseEnrich(PointsCard card, TreeChanges changes)
    {
        string gap = CardText.Normalize(card.Gap ?? "");
        if (gap.Length > 0 && NonFactGap.IsMatch(gap))
            return WroteOnSubject(card, changes) ? Closure.Closes($"touched {card.Xref}") : Closure.Open("the changeset does not touch the subject");

        if (IsGenericGap(gap)) return CloseAnyFact(card, changes);

        if (!TryReadGap(card.Gap, out string tag, out string? part))
            return Closure.Open($"the card's gap \"{card.Gap}\" does not name a birth, death, marriage, burial or name fact");

        if (tag == "NAME")
            return changes.Names.TryGetValue(card.Xref, out var renamed) && renamed.After is not null
                ? Closure.Closes($"NAME {card.Xref}")
                : Closure.Open("no NAME written on the subject");

        var onSubject = changes.Facts.Where(f => WrittenOnSubject(f, card.Xref, changes)).ToList();
        var match = onSubject.FirstOrDefault(f => f.Tag == tag && Carries(f, part));
        if (match is not null) return Closure.Closes($"{tag} {match.Record}");

        string others = string.Join(", ", onSubject.Select(f => f.Tag).Distinct());
        return Closure.Open(others.Length == 0
            ? $"no {tag}{PartSuffix(part)} written on the subject"
            : $"no {tag}{PartSuffix(part)} written on the subject (wrote {others})");
    }

    // A gapless card, or the queue gap "enrich-fact", names no single fact: its points are per improvement.
    static bool IsGenericGap(string gap) => gap is "" or "enrich" or "enrich-fact" or "enrich-facts" or "fact" or "facts";

    // Closes on any improved fact written on the subject: the card's points for each improved date, place and citation, plus its difficulty bonus once.
    static Closure CloseAnyFact(PointsCard card, TreeChanges changes)
    {
        var written = changes.Facts.Where(f => WrittenOnSubject(f, card.Xref, changes) && f.Improvements > 0).ToList();
        if (written.Count == 0) return Closure.Open("no fact written on the subject");

        int improvements = written.Sum(f => f.Improvements);
        string wrote = string.Join("; ", written.Select(f => $"{f.Tag} {f.Record} ({Improved(f)})"));
        return Closure.Closes(wrote, points: card.Points * improvements + card.Bonus);
    }

    static string Improved(FactWrite fact) =>
        string.Join(" and ", new[]
        {
            fact.DateImproved ? "date" : null, fact.PlaceImproved ? "place" : null, fact.CitationImproved ? "citation" : null,
        }.OfType<string>());

    static bool WroteOnSubject(PointsCard card, TreeChanges changes) =>
        changes.Touched.Contains(card.Xref)
        || changes.Families.Any(f => f.Value.Partners.Contains(card.Xref) && changes.Touched.Contains(f.Key));

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
