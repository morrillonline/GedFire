using GedCore.Apply;

namespace GedCore.Points;

/// <summary>How one new-relative card fared: closed with what the changeset <c>Wrote</c> and the person it <c>Added</c>, or open with a reason.</summary>
internal sealed record NewRelativeOutcome(string? Wrote, string? Reason, IReadOnlyList<string> Added);

/// <summary>
/// Allocates the people a changeset added to the new-relative cards (new-child, new-parent,
/// new-spouse), one-to-one, keyed by card position: an exact subject-name match claims its
/// minted person first, the remaining cards claim an unclaimed person in anchor-xref order,
/// and a card left only with already-claimed people stays open, naming the collision.
/// </summary>
internal static class NewRelativeCardAllocator
{
    sealed record Candidate(string Family, string Person);

    public static Dictionary<int, NewRelativeOutcome> Allocate(IReadOnlyList<PointsCard> cards, TreeChanges changes)
    {
        var entries = cards
            .Select((card, index) => (Card: card, Index: index, Class: CardText.Normalize(card.Class)))
            .Where(e => e.Class is "new-child" or "new-parent" or "new-spouse")
            .Select(e => (e.Card, e.Index, e.Class, Candidates: CandidatesFor(e.Class, e.Card, changes)))
            .OrderBy(e => e.Card.Xref, StringComparer.Ordinal)
            .ToList();

        var result = new Dictionary<int, NewRelativeOutcome>();
        var claimedBy = new Dictionary<string, PointsCard>(StringComparer.Ordinal);

        foreach (var entry in entries.Where(e => e.Card.Name is not null))
        {
            var match = entry.Candidates.FirstOrDefault(c =>
                !claimedBy.ContainsKey(c.Person)
                && MintedName(changes, c.Person) is { } minted && CardText.Fold(minted) == CardText.Fold(entry.Card.Name!));
            if (match is null) continue;
            claimedBy[match.Person] = entry.Card;
            result[entry.Index] = Closes(entry.Class, entry.Card, match);
        }

        foreach (var entry in entries.Where(e => !result.ContainsKey(e.Index)))
        {
            if (entry.Candidates.Count == 0)
            {
                result[entry.Index] = SubjectLinkedAsSpouse(entry.Class, entry.Card, changes)
                    ? new NewRelativeOutcome($"spouse in {SpouseFamily(entry.Card, changes)}", null, [])
                    : Open(NoRelativeReason(entry.Class));
                continue;
            }

            var open = entry.Candidates.FirstOrDefault(c => !claimedBy.ContainsKey(c.Person));
            if (open is not null)
            {
                claimedBy[open.Person] = entry.Card;
                result[entry.Index] = Closes(entry.Class, entry.Card, open);
            }
            else
            {
                var taken = entry.Candidates[0];
                result[entry.Index] = Open(
                    $"not closed by this apply: {taken.Person} already closed card {claimedBy[taken.Person].Key} — one applied person closes at most one card");
            }
        }
        return result;
    }

    static List<Candidate> CandidatesFor(string cardClass, PointsCard card, TreeChanges changes)
    {
        var found = new List<Candidate>();
        foreach (var (xref, family) in changes.Families)
        {
            IReadOnlyList<string> people = cardClass switch
            {
                "new-child" when family.Partners.Contains(card.Xref) => family.AddedChildren,
                "new-parent" when family.Children.Contains(card.Xref) => family.AddedPartners,
                "new-spouse" when family.Partners.Contains(card.Xref) && family.Partners.Count >= 2
                    => [.. family.AddedPartners.Where(p => p != card.Xref)],
                _ => [],
            };
            found.AddRange(people.Select(p => new Candidate(xref, p)));
        }
        return found;
    }

    static string? MintedName(TreeChanges changes, string person) =>
        changes.Names.TryGetValue(person, out var name) && name.Before is null ? name.After : null;

    static NewRelativeOutcome Closes(string cardClass, PointsCard card, Candidate claimed) => cardClass switch
    {
        "new-child" => new($"CHIL {claimed.Person} in {claimed.Family}", null, [claimed.Person]),
        "new-parent" => new($"parent {claimed.Person} of {card.Xref} in {claimed.Family}", null, [claimed.Person]),
        _ => new($"spouse in {claimed.Family}", null, [claimed.Person]),
    };

    static NewRelativeOutcome Open(string reason) => new(null, reason, []);

    // A new-spouse card also closes when the subject itself was the partner the changeset
    // linked into a two-partner family; no added person is claimed, so nothing can collide.
    static bool SubjectLinkedAsSpouse(string cardClass, PointsCard card, TreeChanges changes) =>
        cardClass == "new-spouse" && SpouseFamily(card, changes) is not null;

    static string? SpouseFamily(PointsCard card, TreeChanges changes) =>
        changes.Families.FirstOrDefault(f =>
            f.Value.Partners.Contains(card.Xref) && f.Value.Partners.Count >= 2 && f.Value.AddedPartners.Count > 0).Key;

    static string NoRelativeReason(string cardClass) => cardClass switch
    {
        "new-child" => "no child added to a family the subject is a partner in",
        "new-parent" => "no parent added to a family the subject is a child in",
        _ => "no spouse added to a family the subject is a partner in",
    };
}
