using GedCore;
using GedCore.Matching;
using GedFire.Gen;

namespace GedFire.Match;

// ---------------------------------------------------------------------------
// Everything PersonMatcher needs to compare a query against a person,
// pre-normalized once per snapshot. Built once from a GedModel; no
// re-normalization happens per tool call.
// ---------------------------------------------------------------------------

/// <summary>One individual's pre-normalized, comparable fields.</summary>
public sealed record PersonIndexEntry(
    GedIndividual Individual,
    string NormalizedSurname,
    string NormalizedGiven,
    bool? IsMale,
    PersonMatchEvent? Birth,
    PersonMatchEvent? Death,
    PersonMatchParents? Parents,
    IReadOnlyList<PersonMatchMarriage> Marriages);

public sealed class MatchIndex
{
    public IReadOnlyList<PersonIndexEntry> Entries { get; }

    public MatchIndex(GedModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        Entries = [.. model.Individuals.Values.Select(BuildEntry)];
    }

    static PersonIndexEntry BuildEntry(GedIndividual indi) => new(
        Individual: indi,
        NormalizedSurname: PersonNameNormalizer.Normalize(indi.LastName),
        NormalizedGiven: PersonNameNormalizer.Normalize(indi.FirstMiddle()),
        IsMale: indi.SexRecorded ? indi.IsMale : null,
        Birth: EventOf(indi.Birth),
        Death: EventOf(indi.Death),
        Parents: ParentsOf(indi.FamChild),
        Marriages: MarriagesOf(indi));

    static PersonMatchEvent? EventOf(GedEvent? gedEvent)
    {
        if (gedEvent is null) return null;
        int parsedYear = GedDate.ParseYear(gedEvent.Date);
        int? year = parsedYear != 0 ? parsedYear : null;
        string? place = NormalizeOrNull(gedEvent.Place);
        return year is null && place is null ? null : new PersonMatchEvent(year, place);
    }

    static PersonMatchParents? ParentsOf(GedFamily? family)
    {
        if (family is null) return null;
        string? father = family.Husband is { } husband
            ? NormalizeOrNull(PersonDisplay.FullName(husband))
            : null;
        string? mother = family.Wife is { } wife
            ? NormalizeOrNull(PersonDisplay.FullName(wife))
            : null;
        return father is null && mother is null ? null : new PersonMatchParents(father, mother);
    }

    static List<PersonMatchMarriage> MarriagesOf(GedIndividual individual)
    {
        var marriages = new List<PersonMatchMarriage>(individual.FamSpouse.Count);
        foreach (var family in individual.FamSpouse)
        {
            string? spouseName = family.SpouseOf(individual) is { } spouse
                ? NormalizeOrNull(PersonDisplay.FullName(spouse))
                : null;
            int parsedYear = GedDate.ParseYear(family.Marriage?.Date);
            int? year = parsedYear != 0 ? parsedYear : null;
            string? place = NormalizeOrNull(family.Marriage?.Place);
            marriages.Add(new PersonMatchMarriage(spouseName, year, place));
        }
        return marriages;
    }

    static string? NormalizeOrNull(string? value)
    {
        string normalized = PersonNameNormalizer.Normalize(value);
        return normalized.Length > 0 ? normalized : null;
    }
}
