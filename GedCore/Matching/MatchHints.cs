namespace GedCore.Matching;

// ---------------------------------------------------------------------------
// Incidental detail supplied alongside a person-matching query. A plain
// domain type, not an MCP schema DTO, shared so GedCore.Apply's duplicate
// detection can build the same request shape find_person does.
// ---------------------------------------------------------------------------

public sealed record MatchHints(int? BirthYear, string? Place, string? SpouseName, string? ParentName)
{
    public static readonly MatchHints None = new(null, null, null, null);
}
