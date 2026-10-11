using System.Text.Json;
using System.Text.RegularExpressions;

namespace GedCore.Policy;

/// <summary>A death place needs a citation whose page or extract matches the evidence pattern, so a burial record alone cannot supply it.</summary>
internal sealed class DeathPlaceEvidenceRule(Regex evidence) : IPolicyRule
{
    public IEnumerable<string> Check(IReadOnlyList<PolicyItem> items) =>
        from item in items
        from node in item.Ops.SelectMany(OpJson.Objects)
        where OpJson.String(node, "fact") == "DEAT"
        let place = OpJson.Property(node, "value") is { } value ? OpJson.String(value, "place") : null
        where !string.IsNullOrWhiteSpace(place)
        where !OpJson.CitationsOf(node).Any(Supports)
        select $"item {item.Number}: the DEAT place \"{place}\" is not supported by a citation whose page or extract matches /{evidence}/ — a burial record does not establish where a person died";

    bool Supports(JsonElement citation) =>
        (OpJson.String(citation, "dataText") is { } text && evidence.IsMatch(text))
        || (OpJson.String(citation, "page") is { } page && evidence.IsMatch(page));
}
