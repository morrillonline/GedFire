using System.Text.Json;
using System.Text.Json.Serialization;

namespace GedFire.Mcp;

/// <summary>
/// Wire shape of the "hints" argument object. Binding-only concern — the
/// domain type PersonMatcher actually consumes is GedFire.Match.MatchHints.
/// </summary>
public sealed class FindPersonHintsArgs
{
    [JsonPropertyName("sex")]
    public string? Sex { get; init; }

    [JsonPropertyName("birth")]
    public FindPersonEventHintArgs? Birth { get; init; }

    [JsonPropertyName("death")]
    public FindPersonEventHintArgs? Death { get; init; }

    [JsonPropertyName("parents")]
    public FindPersonParentsHintArgs? Parents { get; init; }

    [JsonPropertyName("spouse")]
    public FindPersonSpouseHintArgs? Spouse { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; init; }
}

public sealed class FindPersonEventHintArgs
{
    [JsonPropertyName("year")]
    public int? Year { get; init; }

    [JsonPropertyName("place")]
    public string? Place { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; init; }
}

public sealed class FindPersonParentsHintArgs
{
    [JsonPropertyName("father")]
    public string? Father { get; init; }

    [JsonPropertyName("mother")]
    public string? Mother { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; init; }
}

public sealed class FindPersonSpouseHintArgs
{
    [JsonPropertyName("name")]
    public string? Name { get; init; }

    [JsonPropertyName("marriage")]
    public FindPersonEventHintArgs? Marriage { get; init; }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? AdditionalProperties { get; init; }
}
