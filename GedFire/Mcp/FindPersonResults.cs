namespace GedFire.Mcp;

// ---------------------------------------------------------------------------
// find_person's unified result shape, mirroring FindPersonTool.OutputSchemaJson
// property-for-property. Serialized with System.Text.Json using
// FindPersonTool's camelCase, nulls-emitted options. Pure data — every
// mapping from a MatchOutcome to these records happens in FindPersonTool,
// not here.
// ---------------------------------------------------------------------------

public sealed record EventIdentity(string? Date, int? Year, string? Qualifier, string? Place);

public sealed record ParentsIdentity(string? Father, string? Mother);

public sealed record CandidateIdentity(
    string Xref,
    string Name,
    EventIdentity? Birth,
    EventIdentity? Death,
    ParentsIdentity? Parents,
    IReadOnlyList<string> Spouses,
    double MatchScore);

public sealed record SpouseFamilyIdentity(string Xref, string? MarriageDate, string? SpouseName);

public sealed record FamiliesIdentity(
    IReadOnlyList<string> AsChild,
    IReadOnlyList<SpouseFamilyIdentity> AsParent);

public sealed record ResolvedPersonIdentity(
    string Xref,
    string Name,
    EventIdentity? Birth,
    EventIdentity? Death,
    FamiliesIdentity Families);

public sealed record SuggestionIdentity(string Xref, string Name, string Reason, double MatchScore);

/// <summary>
/// find_person's one unified response shape. Every call returns every
/// field; unused fields for a given matchType are null or empty per the
/// invariants documented on FindPersonTool.OutputSchemaJson.
/// </summary>
public sealed record FindPersonResult(
    string MatchType,
    string? ConfidentMatchXref,
    double? ConfidentMatchScore,
    ResolvedPersonIdentity? Person,
    IReadOnlyList<CandidateIdentity> Candidates,
    IReadOnlyList<SuggestionIdentity> Suggestions,
    int TotalMatches,
    bool Truncated);
