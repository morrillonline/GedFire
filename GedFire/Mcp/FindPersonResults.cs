namespace GedFire.Mcp;

// ---------------------------------------------------------------------------
// find_person's three result shapes, mirroring FindPersonTool.OutputSchemaJson
// property-for-property (docs/design/mcp-server.md "Output: three shapes,
// one schema"). Serialized with System.Text.Json using FindPersonTool's
// camelCase, nulls-emitted options. Pure data — every mapping from a
// MatchOutcome to these records happens in FindPersonTool, not here.
// ---------------------------------------------------------------------------

public sealed record EventIdentity(string? Date, int? Year, string? Qualifier, string? Place);

public sealed record ParentsIdentity(string? Father, string? Mother);

public sealed record CandidateIdentity(
    string Xref,
    string Name,
    EventIdentity? Birth,
    EventIdentity? Death,
    ParentsIdentity? Parents,
    IReadOnlyList<string> Spouses);

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

public sealed record SuggestionIdentity(string Xref, string Name, string Reason);

public sealed record SingleMatchResult(string MatchType, ResolvedPersonIdentity Person);

public sealed record CandidateListResult(string MatchType, IReadOnlyList<CandidateIdentity> Candidates, bool Truncated);

public sealed record NoMatchResult(string MatchType, IReadOnlyList<SuggestionIdentity> Suggestions);
