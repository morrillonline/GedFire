using GedCore.Matching;

namespace GedFire.Match;

// ---------------------------------------------------------------------------
// Thin GedIndividual-facing adapter over GedCore.Matching.PersonMatchCore.
// The pure scoring/classification core lives in GedCore so GedCore.Apply's
// new-person duplicate detector and this class call the same implementation.
// Builds neutral PersonMatchCandidate values from a MatchIndex's
// PersonIndexEntry list, delegates to the core, and maps the neutral
// PersonMatchOutcome back to this namespace's GedIndividual-carrying
// MatchOutcome/ScoredMatch/Suggestion types. No matching or scoring logic
// lives in this class.
// ---------------------------------------------------------------------------

public sealed class PersonMatcher
{
    readonly NicknameDirectory _nicknames;
    readonly PersonMatchCore _core = new();

    public PersonMatcher(NicknameDirectory nicknames) =>
        _nicknames = nicknames ?? throw new ArgumentNullException(nameof(nicknames));

    /// <summary>
    /// Resolve <paramref name="query"/> against <paramref name="index"/>.
    /// <paramref name="maxResults"/> caps how many scored recall candidates
    /// come back in <see cref="MatchOutcome.Matches"/> (default 8); pass
    /// null for no cap at all.
    /// </summary>
    public MatchOutcome Match(MatchIndex index, string query, MatchHints? hints = null, int? maxResults = 8)
    {
        ArgumentNullException.ThrowIfNull(index);

        var byId = new Dictionary<string, PersonIndexEntry>(index.Entries.Count, StringComparer.Ordinal);
        var candidates = new List<PersonMatchCandidate>(index.Entries.Count);
        foreach (var entry in index.Entries)
        {
            byId[entry.Individual.Xref] = entry;
            candidates.Add(ToCandidate(entry));
        }

        var outcome = _core.Match(candidates, query, hints, _nicknames, maxResults);

        return new MatchOutcome
        {
            PersonMatchType = outcome.PersonMatchType,
            Matches = [.. outcome.Matches.Select(m => ToScoredMatch(m, byId))],
            TotalMatches = outcome.TotalMatches,
            Truncated = outcome.Truncated,
            Suggestions = [.. outcome.Suggestions.Select(s => ToSuggestion(s, byId))],
        };
    }

    // Query splitting is a pure string operation with no GedIndividual
    // involvement; PersonMatcherTests exercises it directly through this
    // pass-through, keeping the one implementation in GedCore.Matching.
    public static (string Surname, string Given, bool OneToken) SplitQuery(string? query) =>
        PersonMatchCore.SplitQuery(query);

    static PersonMatchCandidate ToCandidate(PersonIndexEntry e) => new(
        e.Individual.Xref,
        PersonDisplay.FullName(e.Individual),
        e.NormalizedSurname,
        e.NormalizedGiven,
        e.BirthYear,
        e.IsMale,
        e.NormalizedPlaces,
        e.NormalizedSpouseNames,
        e.NormalizedParentNames);

    static ScoredMatch ToScoredMatch(PersonMatchScore m, Dictionary<string, PersonIndexEntry> byId) =>
        new(byId[m.Id].Individual, m.FinalScore, m.RawScore, m.NameOnlyScore);

    static Suggestion ToSuggestion(PersonMatchSuggestion s, Dictionary<string, PersonIndexEntry> byId) =>
        new(byId[s.Id].Individual, s.Reason, s.Score);
}
