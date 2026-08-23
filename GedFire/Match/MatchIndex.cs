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
    int? BirthYear,
    bool? IsMale,
    IReadOnlyList<string> NormalizedPlaces,
    IReadOnlyList<string> NormalizedSpouseNames,
    IReadOnlyList<string> NormalizedParentNames);

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
        BirthYear: BirthYearOf(indi),
        IsMale: indi.SexRecorded ? indi.IsMale : null,
        NormalizedPlaces: CollectPlaces(indi),
        NormalizedSpouseNames: CollectSpouseNames(indi),
        NormalizedParentNames: CollectParentNames(indi));

    static int? BirthYearOf(GedIndividual indi)
    {
        if (indi.Birth is null) return null;
        int year = GedDate.ParseYear(indi.Birth.Date);
        return year != 0 ? year : null;
    }

    static List<string> CollectPlaces(GedIndividual indi)
    {
        var places = new List<string>();
        AddPlace(places, indi.Birth?.Place);
        AddPlace(places, indi.Death?.Place);
        foreach (var census in indi.Census) AddPlace(places, census.Place);
        return places;
    }

    static void AddPlace(List<string> places, string? rawPlace)
    {
        string normalized = PersonNameNormalizer.Normalize(rawPlace);
        if (normalized.Length > 0) places.Add(normalized);
    }

    static List<string> CollectSpouseNames(GedIndividual indi)
    {
        var names = new List<string>();
        foreach (var fam in indi.FamSpouse)
        {
            var spouse = fam.SpouseOf(indi);
            if (spouse is null) continue;
            string normalized = PersonNameNormalizer.Normalize(PersonDisplay.FullName(spouse));
            if (normalized.Length > 0) names.Add(normalized);
        }
        return names;
    }

    static List<string> CollectParentNames(GedIndividual indi)
    {
        var names = new List<string>();
        var famChild = indi.FamChild;
        if (famChild?.Husband is { } father)
        {
            string normalized = PersonNameNormalizer.Normalize(PersonDisplay.FullName(father));
            if (normalized.Length > 0) names.Add(normalized);
        }
        if (famChild?.Wife is { } mother)
        {
            string normalized = PersonNameNormalizer.Normalize(PersonDisplay.FullName(mother));
            if (normalized.Length > 0) names.Add(normalized);
        }
        return names;
    }
}
