using GedCore;
using GedCore.Matching;
using GedFire.Gen;
using GedFire.Match;

namespace GedFire.Mcp;

// ---------------------------------------------------------------------------
// Maps a matcher outcome to find_person's result records.
// ---------------------------------------------------------------------------

public static class FindPersonResultMapper
{
    public static FindPersonResult Map(MatchOutcome outcome)
    {
        bool isSingle = outcome.PersonMatchType == PersonMatchType.Single;
        string matchType = outcome.PersonMatchType switch
        {
            PersonMatchType.Single => "single",
            PersonMatchType.Candidates => "candidates",
            PersonMatchType.None => "none",
            _ => throw new InvalidOperationException($"Unknown match type: {outcome.PersonMatchType}"),
        };

        return new FindPersonResult(
            matchType,
            isSingle ? outcome.Matches[0].Individual.Xref : null,
            isSingle ? outcome.Matches[0].FinalScore : null,
            isSingle ? MapResolvedPerson(outcome.Matches[0].Individual) : null,
            [.. outcome.Matches.Select(MapCandidate)],
            [.. outcome.Suggestions.Select(MapSuggestion)],
            outcome.TotalMatches,
            outcome.Truncated);
    }

    static ResolvedPersonIdentity MapResolvedPerson(GedIndividual indi) => new(
        indi.Xref,
        PersonDisplay.FullName(indi),
        MapEvent(indi.Birth),
        MapEvent(indi.Death),
        new FamiliesIdentity(MapAsChild(indi), MapAsParent(indi)),
        indi.SurnameUnknown);

    static CandidateIdentity MapCandidate(ScoredMatch match)
    {
        var indi = match.Individual;
        return new(
            indi.Xref,
            PersonDisplay.FullName(indi),
            MapEvent(indi.Birth),
            MapEvent(indi.Death),
            MapParents(indi.FamChild),
            MapSpouseNames(indi),
            match.FinalScore,
            indi.SurnameUnknown);
    }

    static SuggestionIdentity MapSuggestion(Suggestion s) => new(
        s.Individual.Xref,
        PersonDisplay.FullName(s.Individual),
        s.Reason == SuggestionReason.CloseSpelling ? "close spelling" : "partial name",
        s.Score);

    static EventIdentity? MapEvent(GedEvent? ev)
    {
        if (ev is null) return null;
        string? date = ev.Date.Length > 0 ? ev.Date : null;
        string? place = ev.Place.Length > 0 ? ev.Place : null;
        if (date is null && place is null) return null;

        int year = GedDate.ParseYear(ev.Date);
        return new EventIdentity(date, year != 0 ? year : null, GedDate.Qualifier(ev.Date), place);
    }

    static ParentsIdentity? MapParents(GedFamily? famChild)
    {
        if (famChild is null) return null;
        return new ParentsIdentity(
            famChild.Husband != null ? PersonDisplay.FullName(famChild.Husband) : null,
            famChild.Wife != null ? PersonDisplay.FullName(famChild.Wife) : null);
    }

    static List<string> MapSpouseNames(GedIndividual indi) =>
        [.. indi.FamSpouse
            .Select(f => f.SpouseOf(indi))
            .Where(spouse => spouse != null)
            .Select(spouse => PersonDisplay.FullName(spouse!))];

    static List<string> MapAsChild(GedIndividual indi) =>
        indi.FamChild != null ? [indi.FamChild.Xref] : [];

    static List<SpouseFamilyIdentity> MapAsParent(GedIndividual indi) =>
        [.. indi.FamSpouse.Select(f => new SpouseFamilyIdentity(
            f.Xref,
            f.Marriage != null && f.Marriage.Date.Length > 0 ? f.Marriage.Date : null,
            f.SpouseOf(indi) is { } spouse ? PersonDisplay.FullName(spouse) : null))];
}
