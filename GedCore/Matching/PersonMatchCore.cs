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
public sealed record PersonMatchEvent(int? Year, string? NormalizedPlace);

public sealed record PersonMatchParents(
    string? NormalizedFatherName,
    string? NormalizedMotherName);

public sealed record PersonMatchMarriage(
    string? NormalizedSpouseName,
    int? Year,
    string? NormalizedPlace);

public sealed record PersonMatchCandidate(
    string Id,
    string DisplayName,
    string NormalizedSurname,
    string NormalizedGiven,
    bool? IsMale,
    PersonMatchEvent? Birth,
    PersonMatchEvent? Death,
    PersonMatchParents? Parents,
    IReadOnlyList<PersonMatchMarriage> Marriages);

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
    const double EventYearWeight = 15.0;
    const double EventPlaceWeight = 15.0;
    const double ParentNameWeight = 20.0;
    const double SpouseNameWeight = 20.0;
    const double MarriageYearWeight = 10.0;
    const double MarriagePlaceWeight = 10.0;

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

    // Floor on each name field itself for two-token queries, on top of the
    // weighted-sum RecallThreshold. Without it, one strong field alone
    // clears RecallThreshold regardless of how unrelated the other field is:
    // e.g. SurnameWeight (35) is already 58% of the 60-point two-token pool,
    // so any givenSim >= 0.28 pushes the sum over 70 -- and Jaro-Winkler
    // routinely scores unrelated given names above that (the symmetric case,
    // an exact given name riding along a merely-plausible surname, is milder
    // but real too). Both floors are necessary conditions, not an alternate
    // score: they never admit a candidate the weighted sum would otherwise
    // reject, they only stop one field's strength from admitting a
    // coincidentally-similar match on the other field alone.
    //
    // The surname floor reuses CloseSpellingThreshold: below it, two
    // surnames share only a coincidental few letters, not a plausible
    // spelling of each other -- e.g. "Moore" vs "Morrill" is 0.74.
    //
    // The given-name floor is deliberately lower, because
    // WithoutDocumentedNickname_FallsBackToSimilarityAlone already relies on
    // raw similarity recalling a genuine nickname that isn't in the
    // directory: "Bill" vs "William" is 0.73, below CloseSpellingThreshold
    // but a real shortening, not a coincidence. GivenNameAdmissionFloor
    // sits just under that so the fallback keeps working, while still
    // excluding a merely-coincidental overlap like "Eunice" vs "Ezekiel"
    // (0.68).
    const double GivenNameAdmissionFloor = 0.70;

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

        var recall = nameScored.Where(x => x.Score.Value >= RecallThreshold && x.Score.FieldFloorsMet).ToList();

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

    readonly record struct NameOnlyScore(double Points, double Weight, double Value, double DecisiveSimilarity, bool FieldFloorsMet);

    static NameOnlyScore ScoreNameOnly(
        PersonMatchCandidate candidate, string querySurname, string queryGiven, bool oneToken, NicknameDirectory nicknames)
    {
        double surnameSim = JaroWinkler.Similarity(querySurname, candidate.NormalizedSurname);
        double givenSim = JaroWinkler.Similarity(queryGiven, candidate.NormalizedGiven);
        bool nicknameEquivalent = nicknames.AreEquivalent(FirstToken(queryGiven), FirstToken(candidate.NormalizedGiven), candidate.IsMale);
        double givenPoints = Math.Max(givenSim * GivenWeight, nicknameEquivalent ? GivenNicknameFixedPoints : 0.0);

        double points, weight, decisive;
        bool fieldFloorsMet;
        if (!oneToken)
        {
            points = surnameSim * SurnameWeight + givenPoints;
            weight = SurnameWeight + GivenWeight;
            decisive = surnameSim;
            // Only a two-token query lets one field's own strength admit a
            // candidate on the weighted sum alone -- gate that admission on
            // both fields clearing their own floor (a documented nickname
            // stands in for the given-name floor).
            fieldFloorsMet = (givenSim >= GivenNameAdmissionFloor || nicknameEquivalent) &&
                             surnameSim >= CloseSpellingThreshold;
        }
        else
        {
            // One-token query: compare against both fields independently and
            // let the better-scoring field stand alone, with only that
            // field's weight available. No second field rides along, so no
            // floor applies.
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
            fieldFloorsMet = true;
        }

        double value = weight > 0 ? points * 100.0 / weight : 0.0;
        return new NameOnlyScore(points, weight, value, decisive, fieldFloorsMet);
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

        AddEventHint(hints.Birth, candidate.Birth, ref raw, ref available);
        AddEventHint(hints.Death, candidate.Death, ref raw, ref available);

        if (hints.Parents is { } parentHints && candidate.Parents is { } parents)
        {
            AddNameHint(parentHints.Father, parents.NormalizedFatherName, ParentNameWeight, ref raw, ref available);
            AddNameHint(parentHints.Mother, parents.NormalizedMotherName, ParentNameWeight, ref raw, ref available);
        }

        AddSpouseHint(hints.Spouse, candidate.Marriages, ref raw, ref available);

        double finalScore = available > 0 ? raw * 100.0 / available : 0.0;
        return new HintedScore(raw, available, finalScore);
    }

    static void AddEventHint(
        EventHint? hint, PersonMatchEvent? candidate, ref double raw, ref double available)
    {
        if (hint is null || candidate is null) return;

        AddYearHint(hint.Year, candidate.Year, EventYearWeight, ref raw, ref available);
        AddPlaceHint(hint.Place, candidate.NormalizedPlace, EventPlaceWeight, ref raw, ref available);
    }

    static void AddSpouseHint(
        SpouseHint? hint, IReadOnlyList<PersonMatchMarriage> marriages, ref double raw, ref double available)
    {
        if (hint is null || marriages.Count == 0) return;

        double bestRaw = 0.0;
        double bestAvailable = 0.0;
        double bestNormalized = 0.0;
        bool found = false;

        foreach (var marriage in marriages)
        {
            double marriageRaw = 0.0;
            double marriageAvailable = 0.0;

            AddNameHint(
                hint.Name, marriage.NormalizedSpouseName, SpouseNameWeight,
                ref marriageRaw, ref marriageAvailable);
            AddYearHint(
                hint.Marriage?.Year, marriage.Year, MarriageYearWeight,
                ref marriageRaw, ref marriageAvailable);
            AddPlaceHint(
                hint.Marriage?.Place, marriage.NormalizedPlace, MarriagePlaceWeight,
                ref marriageRaw, ref marriageAvailable);

            double normalized = marriageAvailable > 0
                ? marriageRaw * 100.0 / marriageAvailable
                : 0.0;
            if (!found || normalized > bestNormalized ||
                (normalized == bestNormalized && marriageRaw > bestRaw))
            {
                found = true;
                bestRaw = marriageRaw;
                bestAvailable = marriageAvailable;
                bestNormalized = normalized;
            }
        }

        raw += bestRaw;
        available += bestAvailable;
    }

    static void AddYearHint(
        int? hintYear, int? candidateYear, double weight, ref double raw, ref double available)
    {
        if (hintYear is not int expected || candidateYear is not int actual) return;

        available += weight;
        int difference = Math.Abs(expected - actual);
        raw += difference switch
        {
            0 => weight,
            1 => weight * 2.0 / 3.0,
            2 => weight / 3.0,
            _ => 0.0,
        };
    }

    static void AddPlaceHint(
        string? hint, string? candidate, double weight, ref double raw, ref double available)
    {
        if (string.IsNullOrWhiteSpace(hint) || string.IsNullOrEmpty(candidate)) return;

        string normalizedHint = PersonNameNormalizer.Normalize(hint);
        if (normalizedHint.Length == 0) return;

        available += weight;
        if (candidate.Contains(normalizedHint, StringComparison.Ordinal) ||
            normalizedHint.Contains(candidate, StringComparison.Ordinal))
        {
            raw += weight;
        }
    }

    static void AddNameHint(
        string? hint, string? candidate, double weight, ref double raw, ref double available)
    {
        if (string.IsNullOrWhiteSpace(hint) || string.IsNullOrEmpty(candidate)) return;

        string normalizedHint = PersonNameNormalizer.Normalize(hint);
        if (normalizedHint.Length == 0) return;

        available += weight;
        if (JaroWinkler.Similarity(normalizedHint, candidate) >= RelationalHintThreshold)
            raw += weight;
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
