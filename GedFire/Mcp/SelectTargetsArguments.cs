using System.Text.Json;

namespace GedFire.Mcp;

/// <summary>select_targets' arguments as they arrived, unvalidated, so each wrong shape is reported by field name.</summary>
public sealed record SelectTargetsArguments
{
    public JsonElement People { get; init; }
    public JsonElement Surnames { get; init; }
    public JsonElement Xrefs { get; init; }
    public JsonElement MaxMotherAgeAtMarriage { get; init; }
    public JsonElement MinMarriageYears { get; init; }
    public JsonElement IncludeSuppressed { get; init; }
}
