using System.Text.RegularExpressions;
using GedCore.Apply;

namespace GedCore.Points;

/// <summary>
/// Decides which cards a changeset closes by what it wrote to the tree. An enrich
/// card closes only when the fact it names was written on its subject; a new-parent,
/// new-spouse or new-child card closes only when the family gained that relative; a
/// changeset that renames a card's subject closes nothing for it.
/// </summary>
public static class CardScorer
{
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
        var closed = new List<ClosedCard>();
        var notClosed = new List<UnclosedCard>();
        var mismatches = new List<SubjectMismatch>();

        foreach (var card in cards)
        {
            if (Normalize(card.Class) == "enrich-person" && !NamesTheSubject(card) && RenamedAway(card, changes) is { } appliedName)
            {
                mismatches.Add(new SubjectMismatch(card.Key, card.Name!, appliedName));
                notClosed.Add(new UnclosedCard(card.Key, $"the changeset renames the subject to \"{appliedName}\""));
                continue;
            }

            var (wrote, reason, added) = Close(card, changes);
            if (wrote is not null && MintedOtherName(card, changes, added) is { } minted)
            {
                mismatches.Add(new SubjectMismatch(card.Key, card.Name!, minted));
                notClosed.Add(new UnclosedCard(card.Key, $"the changeset mints \"{minted}\", not the card's \"{card.Name}\""));
            }
            else if (wrote is not null) closed.Add(new ClosedCard(card.Key, card.Points, wrote));
            else notClosed.Add(new UnclosedCard(card.Key, reason!));
        }
        return new PointsPreview(closed, notClosed, mismatches, closed.Sum(c => c.Points));
    }

    static string? RenamedAway(PointsCard card, TreeChanges changes) =>
        card.Name is not null
        && changes.Names.TryGetValue(card.Xref, out var change)
        && change is { Before: not null, After: not null }
        && !string.Equals(Fold(change.After), Fold(card.Name), StringComparison.OrdinalIgnoreCase)
            ? change.After
            : null;

    static string Fold(string name) => string.Join(' ', name.Replace("/", " ").Split(' ', StringSplitOptions.RemoveEmptyEntries));

    // A new-relative card's name is the person to mint: a person the changeset creates under it must carry that name exactly.
    static string? MintedOtherName(PointsCard card, TreeChanges changes, IReadOnlyList<string> added)
    {
        if (card.Name is null) return null;
        var minted = added
            .Where(x => changes.Names.TryGetValue(x, out var n) && n.Before is null && n.After is not null)
            .Select(x => changes.Names[x].After!)
            .ToList();
        return minted.Count > 0 && !minted.Any(n => Fold(n) == Fold(card.Name)) ? minted[0] : null;
    }

    static (string? Wrote, string? Reason, IReadOnlyList<string> Added) Close(PointsCard card, TreeChanges changes) =>
        Normalize(card.Class) switch
        {
            "enrich-person" => With(CloseEnrich(card, changes)),
            "new-child" => CloseNewChild(card, changes),
            "new-parent" => CloseNewParent(card, changes),
            "new-spouse" => CloseNewSpouse(card, changes),
            _ => (null, $"unknown card class \"{card.Class}\"", []),
        };

    static (string?, string?, IReadOnlyList<string>) With((string?, string?) closure) => (closure.Item1, closure.Item2, []);

    static string Normalize(string text) => text.Trim().ToLowerInvariant().Replace(' ', '-').Replace('_', '-');

    static bool NamesTheSubject(PointsCard card) => card.Gap is not null && ReadTag(Normalize(card.Gap)) == "NAME";

    static (string?, string?) CloseEnrich(PointsCard card, TreeChanges changes)
    {
        string gap = Normalize(card.Gap ?? "");
        if (gap.Length > 0 && NonFactGap.IsMatch(gap))
            return WroteOnSubject(card, changes) ? ($"touched {card.Xref}", null) : (null, "the changeset does not touch the subject");

        if (!TryReadGap(card.Gap, out string tag, out string? part))
            return (null, $"the card's gap \"{card.Gap}\" does not name a birth, death, marriage, burial or name fact");

        if (tag == "NAME")
            return changes.Names.TryGetValue(card.Xref, out var renamed) && renamed.After is not null
                ? ($"NAME {card.Xref}", null)
                : (null, "no NAME written on the subject");

        var onSubject = changes.Facts.Where(f => WrittenOnSubject(f, card.Xref, changes)).ToList();
        var match = onSubject.FirstOrDefault(f => f.Tag == tag && Carries(f, part));
        if (match is not null) return ($"{tag} {match.Record}", null);

        string others = string.Join(", ", onSubject.Select(f => f.Tag).Distinct());
        return (null, others.Length == 0
            ? $"no {tag}{PartSuffix(part)} written on the subject"
            : $"no {tag}{PartSuffix(part)} written on the subject (wrote {others})");
    }

    static bool WroteOnSubject(PointsCard card, TreeChanges changes) =>
        changes.Touched.Contains(card.Xref)
        || changes.Families.Any(f => f.Value.Partners.Contains(card.Xref) && changes.Touched.Contains(f.Key));

    static bool WrittenOnSubject(FactWrite fact, string subject, TreeChanges changes) =>
        fact.Record == subject
        || (changes.Families.TryGetValue(fact.Record, out var family) && family.Partners.Contains(subject));

    static bool Carries(FactWrite fact, string? part) =>
        part switch { "date" => fact.Date is not null, "place" => fact.Place is not null, _ => true };

    static string PartSuffix(string? part) => part is null ? "" : $" {part}";

    static string? ReadTag(string gap) => GapTags.FirstOrDefault(g => g.Gap.IsMatch(gap)).Tag;

    static bool TryReadGap(string? gap, out string tag, out string? part)
    {
        string normalized = Normalize(gap ?? "");
        tag = ReadTag(normalized) ?? "";
        part = normalized.Split('-', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault(s => s is "date" or "place");
        return tag.Length > 0;
    }

    static (string?, string?, IReadOnlyList<string>) CloseNewChild(PointsCard card, TreeChanges changes)
    {
        foreach (var (xref, family) in changes.Families)
            if (family.Partners.Contains(card.Xref) && family.AddedChildren.Count > 0)
                return ($"CHIL {family.AddedChildren[0]} in {xref}", null, family.AddedChildren);
        return (null, "no child added to a family the subject is a partner in", []);
    }

    static (string?, string?, IReadOnlyList<string>) CloseNewParent(PointsCard card, TreeChanges changes)
    {
        foreach (var (xref, family) in changes.Families)
            if (family.Children.Contains(card.Xref) && family.AddedPartners.Count > 0)
                return ($"parent {family.AddedPartners[0]} of {card.Xref} in {xref}", null, family.AddedPartners);
        return (null, "no parent added to a family the subject is a child in", []);
    }

    static (string?, string?, IReadOnlyList<string>) CloseNewSpouse(PointsCard card, TreeChanges changes)
    {
        foreach (var (xref, family) in changes.Families)
            if (family.Partners.Contains(card.Xref) && family.Partners.Count >= 2 && family.AddedPartners.Count > 0)
                return ($"spouse in {xref}", null, [.. family.AddedPartners.Where(p => p != card.Xref)]);
        return (null, "no spouse added to a family the subject is a partner in", []);
    }
}
