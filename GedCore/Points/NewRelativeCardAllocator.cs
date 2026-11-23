using GedCore.Apply;

namespace GedCore.Points;

/// <summary>How one new-relative card fared: closed with what the changeset <c>Wrote</c> and the person it <c>Added</c>, or open with a reason.</summary>
internal sealed record NewRelativeOutcome(string? Wrote, string? Reason, IReadOnlyList<string> Added);

/// <summary>
/// Allocates the people a changeset added to the new-relative cards (new-child, new-parent,
/// new-spouse), one-to-one, keyed by card position: an exact subject-name match claims its
/// minted person first, the remaining cards claim an unclaimed person in anchor-xref order,
/// and a card left only with already-claimed people stays open, naming the collision.
/// A New child card belongs to the one family the deal dealt it for: only a child joining a family that
/// existed, childless, with the subject in it closes the card, and a family's card is paid once.
/// A new-spouse card no spouse closes is closed by a newly recorded never-married answer.
/// </summary>
internal static class NewRelativeCardAllocator
{
    const string NewChild = "new-child";
    const string NewParent = "new-parent";
    const string NewSpouse = "new-spouse";

    // A person the changeset added to a family; NotDealt says why that family's New child card is not the card being priced.
    sealed record Candidate(string Family, string Person, string? NotDealt = null);

    sealed record Entry(PointsCard Card, int Index, string Class, IReadOnlyList<Candidate> Candidates)
    {
        public IEnumerable<Candidate> Dealt => Candidates.Where(c => c.NotDealt is null);
    }

    // The card each added person closed, and the card each family's New child card was paid to.
    sealed class Claims
    {
        readonly Dictionary<string, PointsCard> _people = new(StringComparer.Ordinal);
        readonly Dictionary<string, (PointsCard Card, string Child)> _families = new(StringComparer.Ordinal);

        public bool IsFree(Entry entry, Candidate candidate) =>
            !_people.ContainsKey(candidate.Person) && !(entry.Class == NewChild && _families.ContainsKey(candidate.Family));

        public void Take(Entry entry, Candidate candidate)
        {
            _people[candidate.Person] = entry.Card;
            if (entry.Class == NewChild) _families[candidate.Family] = (entry.Card, candidate.Person);
        }

        public string Collision(Entry entry, Candidate taken) =>
            entry.Class == NewChild && _families.TryGetValue(taken.Family, out var paid)
                ? $"not closed by this apply: {taken.Family}'s New child card is already closed by card {paid.Card.Key} (CHIL {paid.Child}) — a family's New child card is paid once"
                : $"not closed by this apply: {taken.Person} already closed card {_people[taken.Person].Key} — one applied person closes at most one card";
    }

    public static Dictionary<int, NewRelativeOutcome> Allocate(IReadOnlyList<PointsCard> cards, TreeChanges changes, ICardDeal deal)
    {
        var entries = cards
            .Select((card, index) => (Card: card, Index: index, Class: CardText.Normalize(card.Class)))
            .Where(e => e.Class is (NewChild or NewParent or NewSpouse) && deal.WithheldReason(e.Card, e.Card.Family) is null)
            .Select(e => new Entry(e.Card, e.Index, e.Class, CandidatesFor(e.Class, e.Card, changes, deal)))
            .OrderBy(e => e.Card.Xref, StringComparer.Ordinal)
            .ToList();

        var result = new Dictionary<int, NewRelativeOutcome>();
        var claims = new Claims();

        foreach (var entry in entries.Where(e => e.Card.Name is not null))
        {
            var match = entry.Dealt.FirstOrDefault(c =>
                claims.IsFree(entry, c)
                && MintedName(changes, c.Person) is { } minted && CardText.Fold(minted) == CardText.Fold(entry.Card.Name!));
            if (match is null) continue;
            claims.Take(entry, match);
            result[entry.Index] = Closes(entry, match);
        }

        foreach (var entry in entries.Where(e => !result.ContainsKey(e.Index)))
            result[entry.Index] = WithoutNameMatch(entry, claims, changes, deal);
        return result;
    }

    static NewRelativeOutcome WithoutNameMatch(Entry entry, Claims claims, TreeChanges changes, ICardDeal deal)
    {
        if (entry.Dealt.FirstOrDefault(c => claims.IsFree(entry, c)) is { } free)
        {
            claims.Take(entry, free);
            return Closes(entry, free);
        }
        if (entry.Dealt.FirstOrDefault() is { } taken) return Open(claims.Collision(entry, taken));
        if (entry.Candidates.Count > 0) return Open(entry.Candidates[0].NotDealt!);

        // No added person is claimed by either of these, so nothing can collide.
        if (entry.Class == NewSpouse && SpouseFamily(entry.Card, changes) is { } family)
            return new NewRelativeOutcome($"spouse in {family}", null, []);
        if (entry.Class == NewSpouse && deal.RecordedNeverMarried(entry.Card.Xref))
            return new NewRelativeOutcome($"NMR 0 on {entry.Card.Xref} (never married, cited)", null, []);
        return Open(NoRelativeReason(entry));
    }

    static List<Candidate> CandidatesFor(string cardClass, PointsCard card, TreeChanges changes, ICardDeal deal)
    {
        var found = new List<Candidate>();
        foreach (var (xref, family) in changes.Families)
        {
            switch (cardClass)
            {
                case NewChild when family.Partners.Contains(card.Xref):
                    string? notDealt = NotDealtReason(card, xref, family, deal);
                    found.AddRange(family.AddedChildren.Select(child =>
                        new Candidate(xref, child, notDealt is null ? null : $"{child} joined {xref}, {notDealt}")));
                    break;
                case NewParent when family.Children.Contains(card.Xref):
                    found.AddRange(family.AddedPartners.Select(parent => new Candidate(xref, parent)));
                    break;
                case NewSpouse when family.Partners.Contains(card.Xref) && family.Partners.Count >= 2:
                    found.AddRange(family.AddedPartners.Where(p => p != card.Xref).Select(spouse => new Candidate(xref, spouse)));
                    break;
            }
        }
        return found;
    }

    // Why the deal holds no New child card of the subject's for this family that the priced card could be; null when it does.
    static string? NotDealtReason(PointsCard card, string xref, FamilyChange family, ICardDeal deal)
    {
        if (card.Family is not null && card.Family != xref) return $"not the card's family {card.Family}";
        if (family.Created) return "a family this changeset creates; a New child card is dealt for a family that was childless before it";
        if (family.AddedPartners.Contains(card.Xref))
            return "a family the subject joins in this changeset; a New child card is dealt for a family the subject was already a partner in";
        if (family.HadChildren) return "which already had a child, so it had no New child card";
        return deal.WithheldReason(card, xref) is { } withheld ? $"whose New child card is withheld ({withheld})" : null;
    }

    static string? MintedName(TreeChanges changes, string person) =>
        changes.Names.TryGetValue(person, out var name) && name.Before is null ? name.After : null;

    static NewRelativeOutcome Closes(Entry entry, Candidate claimed) => entry.Class switch
    {
        NewChild => new($"CHIL {claimed.Person} in {claimed.Family}", null, [claimed.Person]),
        NewParent => new($"parent {claimed.Person} of {entry.Card.Xref} in {claimed.Family}", null, [claimed.Person]),
        _ => new($"spouse in {claimed.Family}", null, [claimed.Person]),
    };

    static NewRelativeOutcome Open(string reason) => new(null, reason, []);

    // The two-partner family the changeset linked the subject itself into, when it added no other partner to claim.
    static string? SpouseFamily(PointsCard card, TreeChanges changes) =>
        changes.Families.FirstOrDefault(f =>
            f.Value.Partners.Contains(card.Xref) && f.Value.Partners.Count >= 2 && f.Value.AddedPartners.Count > 0).Key;

    static string NoRelativeReason(Entry entry) => entry.Class switch
    {
        NewChild when entry.Card.Family is { } family => $"no child added to {family}",
        NewChild => "no child added to a family the subject is a partner in",
        NewParent => "no parent added to a family the subject is a child in",
        _ => "no spouse added to a family the subject is a partner in, and no cited never-married answer (NMR 0) recorded",
    };
}
