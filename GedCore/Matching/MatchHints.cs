namespace GedCore.Matching;

// ---------------------------------------------------------------------------
// Incidental detail supplied alongside a person-matching query. A plain
// domain type, not an MCP schema DTO, shared so GedCore.Apply's duplicate
// detection can build the same request shape find_person does.
// ---------------------------------------------------------------------------

public sealed record EventHint(int? Year = null, string? Place = null);

public sealed record ParentsHint(string? Father = null, string? Mother = null);

public sealed record SpouseHint(string? Name = null, EventHint? Marriage = null);

public sealed record MatchHints(
    EventHint? Birth = null,
    EventHint? Death = null,
    ParentsHint? Parents = null,
    SpouseHint? Spouse = null)
{
    public static readonly MatchHints None = new();
}
