using GedFire.Gen;

namespace GedFire.Match;

// ---------------------------------------------------------------------------
// PersonMatcher's domain result (docs/design/mcp-server.md "Output: three
// shapes, one schema" / "Matching and ranking"). References model objects
// and xrefs, not DTOs — mapping to the MCP JSON shapes is FindPersonTool's
// job, not this one's.
// ---------------------------------------------------------------------------

public enum PersonMatchType { Single, Candidates, None }

public enum SuggestionReason { CloseSpelling, PartialName }

/// <summary>One matched or candidate person, with the scores that placed it.</summary>
public sealed record ScoredMatch(
    GedIndividual Individual,
    double FinalScore,
    double RawScore,
    double NameOnlyScore);

/// <summary>One near-miss offered when nothing cleared the recall gate.</summary>
public sealed record Suggestion(GedIndividual Individual, SuggestionReason Reason);

public sealed record MatchOutcome
{
    public required PersonMatchType PersonMatchType { get; init; }

    // Exactly one entry for PersonMatchType.Single; two or more (capped) for
    // PersonMatchType.Candidates, ordered best-first; empty for PersonMatchType.None.
    public IReadOnlyList<ScoredMatch> Matches { get; init; } = [];

    // True only for PersonMatchType.Candidates when the candidate set was capped.
    public bool Truncated { get; init; }

    // Populated only for PersonMatchType.None, at most 3, ordered best-first.
    public IReadOnlyList<Suggestion> Suggestions { get; init; } = [];
}
