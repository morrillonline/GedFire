namespace GedCore.Matching;

// ---------------------------------------------------------------------------
// The person-matching algorithm's pure scoring/classification core, shared
// so GedCore.Apply's new-person duplicate detector and
// GedFire.Match.PersonMatcher call one implementation instead of two.
// Operates entirely on the neutral
// PersonMatchCandidate shape below -- no GedIndividual, no GedRecord, no
// MCP types. GedFire.Match.PersonMatcher adapts a MatchIndex's
// GedIndividual-backed entries into candidates and maps the neutral result
// back to its own GedIndividual-carrying types; GedCore.Apply's duplicate
// detector adapts structured GedRecord data the same way. Query splitting,
// the name-only recall gate, weighted evidence with availability
// normalization, classification thresholds, ordering, and suggestions are
// all defined exactly once, here.
// ---------------------------------------------------------------------------

public enum PersonMatchType { Single, Candidates, None }

public enum SuggestionReason { CloseSpelling, PartialName }

/// <summary>
/// One candidate's pre-normalized, comparable fields, keyed by an opaque
/// caller-chosen <see cref="Id"/> (a real xref for find_person, a real xref
/// or a provisional placeholder token for apply-time duplicate detection).
/// <see cref="DisplayName"/> only breaks ties in ordering/suggestion
/// selection; it plays no role in scoring.
/// </summary>
public sealed record PersonMatchCandidate(
    string Id,
    string DisplayName,
    string NormalizedSurname,
    string NormalizedGiven,
    int? BirthYear,
    bool? IsMale,
    IReadOnlyList<string> NormalizedPlaces,
    IReadOnlyList<string> NormalizedSpouseNames,
    IReadOnlyList<string> NormalizedParentNames);

/// <summary>One matched or candidate person's id, with the scores that placed it.</summary>
public sealed record PersonMatchScore(string Id, double FinalScore, double RawScore, double NameOnlyScore);

/// <summary>One near-miss offered when nothing cleared the recall gate.</summary>
public sealed record PersonMatchSuggestion(string Id, SuggestionReason Reason, double Score);

public sealed record PersonMatchOutcome
{
    public required PersonMatchType PersonMatchType { get; init; }

    // The capped, scored recall set, ordered best-first: exactly one entry
    // for Single when the recall set has only one member, but consistently
    // the capped recall set -- including weaker alternatives -- when Single
    // has a decisive winner among more than one recalled candidate; the
    // requested cap for Candidates; empty for None.
    public IReadOnlyList<PersonMatchScore> Matches { get; init; } = [];

    // The size of the complete recall set before capping.
    public int TotalMatches { get; init; }

    // True when Matches.Count < TotalMatches.
    public bool Truncated { get; init; }

    // Populated only for None, at most 3, ordered best-first.
    public IReadOnlyList<PersonMatchSuggestion> Suggestions { get; init; } = [];
}

public sealed class PersonMatchCore
{
    // Evidence weights ("Evidence weights" table).
    const double SurnameWeight = 35.0;
    const double GivenWeight = 25.0;
    const double GivenNicknameFixedPoints = 20.0;
    const double BirthYearWeight = 15.0;
    const double PlaceWeight = 15.0;
    const double SpouseWeight = 20.0;
    const double ParentWeight = 20.0;

    // Recall gate, classification, and suggestion thresholds ("Recall gate,
    // classification, and ordering" / "Limits").
    const double RecallThreshold = 70.0;
    const double SuggestionLow = 55.0;
    const double SuggestionHigh = 69.0;
    const double CloseSpellingThreshold = 0.85;
    const double RelationalHintThreshold = 0.85;
    const double SingleMatchMinScore = 90.0;
    const double SingleMatchMargin = 10.0;
    const int DefaultCandidateCap = 8;
    const int SuggestionCap = 3;

    /// <summary>
    /// Resolve <paramref name="query"/> against <paramref name="candidates"/>.
    /// <paramref name="maxResults"/> caps how many scored recall candidates
    /// come back in <see cref="PersonMatchOutcome.Matches"/> (default 8);
    /// pass null for no cap. The cap never changes recall admission,
    /// scoring, ordering, or single/candidates/none classification.
    /// </summary>
    public PersonMatchOutcome Match(
        IReadOnlyList<PersonMatchCandidate> candidates, string query, MatchHints? hints,
        NicknameDirectory nicknames, int? maxResults = DefaultCandidateCap)
    {
        ArgumentNullException.ThrowIfNull(candidates);
        ArgumentNullException.ThrowIfNull(nicknames);
        if (maxResults is < 1)
            throw new ArgumentOutOfRangeException(nameof(maxResults), maxResults, "maxResults must be at least 1, or null for no cap.");
        hints ??= MatchHints.None;

        var (querySurname, queryGiven, oneToken) = SplitQuery(query);

        var nameScored = new List<(PersonMatchCandidate Candidate, NameOnlyScore Score)>(candidates.Count);
        foreach (var candidate in candidates)
            nameScored.Add((candidate, ScoreNameOnly(candidate, querySurname, queryGiven, oneToken, nicknames)));

        var recall = nameScored.Where(x => x.Score.Value >= RecallThreshold).ToList();

        if (recall.Count == 0)
            return NoMatchOutcome(nameScored);

        var scored = recall
            .Select(x => new CandidateScore(x.Candidate, x.Score, ApplyHints(x.Score, x.Candidate, hints)))
            .ToList();

        var ordered = Order(scored);
        int totalMatches = ordered.Count;

        bool isSingle;
        if (ordered.Count == 1)
        {
            isSingle = true;
        }
        else
        {
            var top = ordered[0];
            var runnerUp = ordered[1];
            isSingle = top.Hinted.FinalScore >= SingleMatchMinScore &&
                       top.Hinted.FinalScore - runnerUp.Hinted.FinalScore >= SingleMatchMargin;
        }

        var capped = maxResults.HasValue ? ordered.Take(maxResults.Value).ToList() : ordered;
        bool truncated = capped.Count < totalMatches;

        return new PersonMatchOutcome
        {
            PersonMatchType = isSingle ? PersonMatchType.Single : PersonMatchType.Candidates,
            Matches = [.. capped.Select(ToScore)],
            TotalMatches = totalMatches,
            Truncated = truncated,
        };
    }

    // -------------------------------------------------------------------
    // Query splitting
    // -------------------------------------------------------------------

    public static (string Surname, string Given, bool OneToken) SplitQuery(string? query)
    {
        string normalized = PersonNameNormalizer.Normalize(query);
        var tokens = normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries);

        return tokens.Length switch
        {
            0 => ("", "", true),
            1 => (tokens[0], tokens[0], true),
            _ => (tokens[^1], string.Join(' ', tokens[..^1]), false),
        };
    }

    // -------------------------------------------------------------------
    // Name-only scoring and the recall gate
    // -------------------------------------------------------------------

    readonly record struct NameOnlyScore(double Points, double Weight, double Value, double DecisiveSimilarity);

    static NameOnlyScore ScoreNameOnly(
        PersonMatchCandidate candidate, string querySurname, string queryGiven, bool oneToken, NicknameDirectory nicknames)
    {
        double surnameSim = JaroWinkler.Similarity(querySurname, candidate.NormalizedSurname);
        double givenSim = JaroWinkler.Similarity(queryGiven, candidate.NormalizedGiven);
        bool nicknameEquivalent = nicknames.AreEquivalent(FirstToken(queryGiven), FirstToken(candidate.NormalizedGiven), candidate.IsMale);
        double givenPoints = Math.Max(givenSim * GivenWeight, nicknameEquivalent ? GivenNicknameFixedPoints : 0.0);

        double points, weight, decisive;
        if (!oneToken)
        {
            points = surnameSim * SurnameWeight + givenPoints;
            weight = SurnameWeight + GivenWeight;
            decisive = surnameSim;
        }
        else
        {
            // One-token query: compare against both fields independently and
            // let the better-scoring field stand alone, with only that
            // field's weight available.
            double surnameNormalized = surnameSim * 100.0;
            double givenNormalized = givenPoints / GivenWeight * 100.0;
            if (surnameNormalized >= givenNormalized)
            {
                points = surnameSim * SurnameWeight;
                weight = SurnameWeight;
                decisive = surnameSim;
            }
            else
            {
                points = givenPoints;
                weight = GivenWeight;
                decisive = givenSim;
            }
        }

        double value = weight > 0 ? points * 100.0 / weight : 0.0;
        return new NameOnlyScore(points, weight, value, decisive);
    }

    static string FirstToken(string normalized)
    {
        int space = normalized.IndexOf(' ');
        return space < 0 ? normalized : normalized[..space];
    }

    // -------------------------------------------------------------------
    // Hint-augmented scoring (recall-set candidates only)
    // -------------------------------------------------------------------

    readonly record struct HintedScore(double Raw, double AvailableWeight, double FinalScore);

    static HintedScore ApplyHints(NameOnlyScore nameOnly, PersonMatchCandidate candidate, MatchHints hints)
    {
        double raw = nameOnly.Points;
        double available = nameOnly.Weight;

        if (hints.BirthYear is int hintYear && candidate.BirthYear is int candidateYear)
        {
            available += BirthYearWeight;
            int diff = Math.Abs(hintYear - candidateYear);
            raw += diff switch { 0 => 15.0, 1 => 10.0, 2 => 5.0, _ => 0.0 };
        }

        if (!string.IsNullOrWhiteSpace(hints.Place) && candidate.NormalizedPlaces.Count > 0)
        {
            available += PlaceWeight;
            string hintPlace = PersonNameNormalizer.Normalize(hints.Place);
            if (hintPlace.Length > 0 && candidate.NormalizedPlaces.Any(p =>
                    p.Contains(hintPlace, StringComparison.Ordinal) ||
                    hintPlace.Contains(p, StringComparison.Ordinal)))
            {
                raw += PlaceWeight;
            }
        }

        if (!string.IsNullOrWhiteSpace(hints.SpouseName) && candidate.NormalizedSpouseNames.Count > 0)
        {
            available += SpouseWeight;
            string hintSpouse = PersonNameNormalizer.Normalize(hints.SpouseName);
            if (hintSpouse.Length > 0 && candidate.NormalizedSpouseNames.Any(n =>
                    JaroWinkler.Similarity(hintSpouse, n) >= RelationalHintThreshold))
            {
                raw += SpouseWeight;
            }
        }

        if (!string.IsNullOrWhiteSpace(hints.ParentName) && candidate.NormalizedParentNames.Count > 0)
        {
            available += ParentWeight;
            string hintParent = PersonNameNormalizer.Normalize(hints.ParentName);
            if (hintParent.Length > 0 && candidate.NormalizedParentNames.Any(n =>
                    JaroWinkler.Similarity(hintParent, n) >= RelationalHintThreshold))
            {
                raw += ParentWeight;
            }
        }

        double finalScore = available > 0 ? raw * 100.0 / available : 0.0;
        return new HintedScore(raw, available, finalScore);
    }

    // -------------------------------------------------------------------
    // Ordering and shape selection
    // -------------------------------------------------------------------

    readonly record struct CandidateScore(PersonMatchCandidate Candidate, NameOnlyScore NameOnly, HintedScore Hinted);

    static List<CandidateScore> Order(List<CandidateScore> scored) =>
    [
        .. scored
            .OrderByDescending(s => s.Hinted.FinalScore)
            .ThenByDescending(s => s.Hinted.Raw)
            .ThenByDescending(s => s.NameOnly.Value)
            .ThenBy(s => s.Candidate.DisplayName, StringComparer.Ordinal)
            .ThenBy(s => s.Candidate.Id, StringComparer.Ordinal),
    ];

    static PersonMatchScore ToScore(CandidateScore s) =>
        new(s.Candidate.Id, s.Hinted.FinalScore, s.Hinted.Raw, s.NameOnly.Value);

    static PersonMatchOutcome NoMatchOutcome(List<(PersonMatchCandidate Candidate, NameOnlyScore Score)> nameScored)
    {
        var suggestions = nameScored
            .Where(x => x.Score.Value >= SuggestionLow && x.Score.Value <= SuggestionHigh)
            .OrderByDescending(x => x.Score.Value)
            .ThenBy(x => x.Candidate.DisplayName, StringComparer.Ordinal)
            .ThenBy(x => x.Candidate.Id, StringComparer.Ordinal)
            .Take(SuggestionCap)
            .Select(x => new PersonMatchSuggestion(
                x.Candidate.Id,
                x.Score.DecisiveSimilarity >= CloseSpellingThreshold
                    ? SuggestionReason.CloseSpelling
                    : SuggestionReason.PartialName,
                x.Score.Value))
            .ToList();

        return new PersonMatchOutcome { PersonMatchType = PersonMatchType.None, Suggestions = suggestions };
    }
}
