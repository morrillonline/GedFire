using GedFire.Gen;

namespace GedFire.Match;

// ---------------------------------------------------------------------------
// Finds people the tree cannot place: nothing dated, nothing located, and no
// family tie to anyone who is placed. A person who has no dates or places is
// unanchored unless a parent, spouse, or child of theirs is anchored, which
// is settled by repeatedly dropping anyone who has such a tie until no more
// change. A person hidden by the privacy filter is unknown, not unplaced: it
// is never reported and counts as anchored for its relatives.
// ---------------------------------------------------------------------------

public sealed record UnanchoredPerson(
    GedIndividual Person, int FactCount, IReadOnlyList<string> TiedOnlyToUnanchored);

public static class UnanchoredPeopleFinder
{
    public static IReadOnlyList<UnanchoredPerson> Find(GedModel model)
    {
        ArgumentNullException.ThrowIfNull(model);

        var unanchored = model.Individuals.Values
            .Where(p => !IsHidden(p) && !HasDateOrPlace(p))
            .ToHashSet();

        bool changed;
        do
        {
            changed = unanchored.RemoveWhere(p => Relatives(p).Any(r => !unanchored.Contains(r))) > 0;
        } while (changed);

        return [.. unanchored
            .OrderBy(p => p.LastName, StringComparer.Ordinal)
            .ThenBy(p => p.FirstMiddle(), StringComparer.Ordinal)
            .ThenBy(p => p.Xref, StringComparer.Ordinal)
            .Select(p => new UnanchoredPerson(
                p, FactCount(p),
                [.. Relatives(p).Select(r => r.Xref).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)]))];
    }

    static bool IsHidden(GedIndividual person) => person.FirstName == PrivacyFilter.LivingGivenName;

    static IEnumerable<GedEvent> Events(GedIndividual person) =>
        new[] { person.Birth, person.Death, person.Will, person.Probate }.OfType<GedEvent>().Concat(person.Census);

    static bool HasDateOrPlace(GedIndividual person) =>
        Events(person).Any(e => e.Date.Length > 0 || e.Place.Length > 0);

    static IEnumerable<GedIndividual> Relatives(GedIndividual person)
    {
        if (person.FamChild is { } parents)
        {
            if (parents.Husband is { } father) yield return father;
            if (parents.Wife is { } mother) yield return mother;
        }
        foreach (var family in person.FamSpouse)
        {
            if (family.SpouseOf(person) is { } spouse) yield return spouse;
            foreach (var child in family.Children) yield return child;
        }
    }

    // Every event, note, media link, and citation recorded on the person.
    static int FactCount(GedIndividual person) =>
        Events(person).Count() + person.NarrativeNotes.Count + person.Media.Count +
        person.NameSources.Count +
        Events(person).Sum(e => e.Sources.Count) +
        person.NarrativeNotes.Sum(n => n.Sources.Count);
}
