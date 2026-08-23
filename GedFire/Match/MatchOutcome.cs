using GedCore.Matching;
using GedFire.Gen;

namespace GedFire.Match;

// ---------------------------------------------------------------------------
// PersonMatcher's domain result. References model objects and xrefs, not
// DTOs — mapping to the MCP JSON shapes is FindPersonTool's job, not this
// one's. PersonMatchType/SuggestionReason are GedCore.Matching's, kept as
// a single classifier rather than a duplicate per caller; this file just
// carries the GedIndividual-typed records that reference them.
// ---------------------------------------------------------------------------

/// <summary>One matched or candidate person, with the scores that placed it.</summary>
public sealed record ScoredMatch(
    GedIndividual Individual,
    double FinalScore,
    double RawScore,
    double NameOnlyScore);

/// <summary>One near-miss offered when nothing cleared the recall gate.</summary>
/// <param name="Score">The name-only score (0-100) that placed this suggestion in the 55-69 band.</param>
public sealed record Suggestion(GedIndividual Individual, SuggestionReason Reason, double Score);

public sealed record MatchOutcome
{
    public required PersonMatchType PersonMatchType { get; init; }

    // The capped, scored recall set, ordered best-first: exactly one entry
    // for PersonMatchType.Single when the recall set has
    // only one member, but consistently the capped recall set -- including
    // weaker alternatives -- when Single has a decisive winner among more
    // than one recalled candidate; the requested cap for Candidates; empty
    // for PersonMatchType.None.
    public IReadOnlyList<ScoredMatch> Matches { get; init; } = [];

    // The size of the complete recall set before capping. 0 for None, at
    // least 1 for Single, at least 2 for Candidates.
    public int TotalMatches { get; init; }

    // True when Matches.Count < TotalMatches (i.e. the cap actually removed
    // some of the recall set). Always false when no cap was requested.
    public bool Truncated { get; init; }

    // Populated only for PersonMatchType.None, at most 3, ordered best-first.
    public IReadOnlyList<Suggestion> Suggestions { get; init; } = [];
}
