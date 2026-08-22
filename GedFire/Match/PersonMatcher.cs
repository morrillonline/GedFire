namespace GedFire.Match;

// ---------------------------------------------------------------------------
// The whole find_person algorithm (docs/design/mcp-server.md "Matching and
// ranking"): query splitting, the name-only recall gate, weighted evidence
// with availability normalization, classification thresholds, ordering, and
// suggestions. Pure and deterministic — no I/O, no MCP types, no JSON.
// ---------------------------------------------------------------------------

public sealed class PersonMatcher
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
    const int CandidateCap = 8;
    const int SuggestionCap = 3;

    readonly NicknameDirectory _nicknames;

    public PersonMatcher(NicknameDirectory nicknames) =>
        _nicknames = nicknames ?? throw new ArgumentNullException(nameof(nicknames));

    public MatchOutcome Match(MatchIndex index, string query, MatchHints? hints = null)
    {
        ArgumentNullException.ThrowIfNull(index);
        hints ??= MatchHints.None;

        var (querySurname, queryGiven, oneToken) = SplitQuery(query);

        var nameScored = new List<(PersonIndexEntry Entry, NameOnlyScore Score)>(index.Entries.Count);
        foreach (var entry in index.Entries)
            nameScored.Add((entry, ScoreNameOnly(entry, querySurname, queryGiven, oneToken)));

        var recall = nameScored.Where(x => x.Score.Value >= RecallThreshold).ToList();

        if (recall.Count == 0)
            return NoMatchOutcome(nameScored);

        var scored = recall
            .Select(x => new CandidateScore(x.Entry, x.Score, ApplyHints(x.Score, x.Entry, hints)))
            .ToList();

        var ordered = Order(scored);

        if (ordered.Count == 1)
            return new MatchOutcome { PersonMatchType = PersonMatchType.Single, Matches = [ToScoredMatch(ordered[0])] };

        var top = ordered[0];
        var runnerUp = ordered[1];
        if (top.Hinted.FinalScore >= SingleMatchMinScore &&
            top.Hinted.FinalScore - runnerUp.Hinted.FinalScore >= SingleMatchMargin)
        {
            return new MatchOutcome { PersonMatchType = PersonMatchType.Single, Matches = [ToScoredMatch(top)] };
        }

        bool truncated = ordered.Count > CandidateCap;
        return new MatchOutcome
        {
            PersonMatchType = PersonMatchType.Candidates,
            Matches = [.. ordered.Take(CandidateCap).Select(ToScoredMatch)],
            Truncated = truncated,
        };
    }

    // -------------------------------------------------------------------
    // Query splitting ("Nickname dictionary" / evidence-weight table notes)
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

    NameOnlyScore ScoreNameOnly(PersonIndexEntry entry, string querySurname, string queryGiven, bool oneToken)
    {
        double surnameSim = JaroWinkler.Similarity(querySurname, entry.NormalizedSurname);
        double givenSim = JaroWinkler.Similarity(queryGiven, entry.NormalizedGiven);
        bool nicknameEquivalent = _nicknames.AreEquivalent(FirstToken(queryGiven), FirstToken(entry.NormalizedGiven), entry.IsMale);
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

    HintedScore ApplyHints(NameOnlyScore nameOnly, PersonIndexEntry entry, MatchHints hints)
    {
        double raw = nameOnly.Points;
        double available = nameOnly.Weight;

        if (hints.BirthYear is int hintYear && entry.BirthYear is int candidateYear)
        {
            available += BirthYearWeight;
            int diff = Math.Abs(hintYear - candidateYear);
            raw += diff switch { 0 => 15.0, 1 => 10.0, 2 => 5.0, _ => 0.0 };
        }

        if (!string.IsNullOrWhiteSpace(hints.Place) && entry.NormalizedPlaces.Count > 0)
        {
            available += PlaceWeight;
            string hintPlace = PersonNameNormalizer.Normalize(hints.Place);
            if (hintPlace.Length > 0 && entry.NormalizedPlaces.Any(p =>
                    p.Contains(hintPlace, StringComparison.Ordinal) ||
                    hintPlace.Contains(p, StringComparison.Ordinal)))
            {
                raw += PlaceWeight;
            }
        }

        if (!string.IsNullOrWhiteSpace(hints.SpouseName) && entry.NormalizedSpouseNames.Count > 0)
        {
            available += SpouseWeight;
            string hintSpouse = PersonNameNormalizer.Normalize(hints.SpouseName);
            if (hintSpouse.Length > 0 && entry.NormalizedSpouseNames.Any(n =>
                    JaroWinkler.Similarity(hintSpouse, n) >= RelationalHintThreshold))
            {
                raw += SpouseWeight;
            }
        }

        if (!string.IsNullOrWhiteSpace(hints.ParentName) && entry.NormalizedParentNames.Count > 0)
        {
            available += ParentWeight;
            string hintParent = PersonNameNormalizer.Normalize(hints.ParentName);
            if (hintParent.Length > 0 && entry.NormalizedParentNames.Any(n =>
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

    readonly record struct CandidateScore(PersonIndexEntry Entry, NameOnlyScore NameOnly, HintedScore Hinted);

    static List<CandidateScore> Order(List<CandidateScore> scored) =>
    [
        .. scored
            .OrderByDescending(s => s.Hinted.FinalScore)
            .ThenByDescending(s => s.Hinted.Raw)
            .ThenByDescending(s => s.NameOnly.Value)
            .ThenBy(s => PersonDisplay.FullName(s.Entry.Individual), StringComparer.Ordinal)
            .ThenBy(s => s.Entry.Individual.Xref, StringComparer.Ordinal),
    ];

    static ScoredMatch ToScoredMatch(CandidateScore s) =>
        new(s.Entry.Individual, s.Hinted.FinalScore, s.Hinted.Raw, s.NameOnly.Value);

    static MatchOutcome NoMatchOutcome(List<(PersonIndexEntry Entry, NameOnlyScore Score)> nameScored)
    {
        var suggestions = nameScored
            .Where(x => x.Score.Value >= SuggestionLow && x.Score.Value <= SuggestionHigh)
            .OrderByDescending(x => x.Score.Value)
            .ThenBy(x => PersonDisplay.FullName(x.Entry.Individual), StringComparer.Ordinal)
            .ThenBy(x => x.Entry.Individual.Xref, StringComparer.Ordinal)
            .Take(SuggestionCap)
            .Select(x => new Suggestion(
                x.Entry.Individual,
                x.Score.DecisiveSimilarity >= CloseSpellingThreshold
                    ? SuggestionReason.CloseSpelling
                    : SuggestionReason.PartialName))
            .ToList();

        return new MatchOutcome { PersonMatchType = PersonMatchType.None, Suggestions = suggestions };
    }
}
