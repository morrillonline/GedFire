namespace GedFire.Match;

// ---------------------------------------------------------------------------
// Incidental detail supplied alongside a find_person query
// (docs/design/mcp-server.md "Input" / "Evidence weights"). A plain domain
// type, not an MCP schema DTO — PersonMatcher takes no MCP types.
// ---------------------------------------------------------------------------

public sealed record MatchHints(int? BirthYear, string? Place, string? SpouseName, string? ParentName)
{
    public static readonly MatchHints None = new(null, null, null, null);
}
