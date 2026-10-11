using System.Text.Json;

namespace GedCore.Policy;

/// <summary>Readers over the raw op JSON that policy rules share.</summary>
internal static class OpJson
{
    public static string? String(JsonElement node, string name) =>
        node.ValueKind == JsonValueKind.Object && node.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() : null;

    public static JsonElement? Property(JsonElement node, string name) =>
        node.ValueKind == JsonValueKind.Object && node.TryGetProperty(name, out var v) ? v : null;

    /// <summary>The citation or citations directly under <paramref name="node"/>.</summary>
    public static IEnumerable<JsonElement> CitationsOf(JsonElement node)
    {
        if (Property(node, "citation") is { ValueKind: JsonValueKind.Object } single) yield return single;
        if (Property(node, "citations") is { ValueKind: JsonValueKind.Array } many)
            foreach (var c in many.EnumerateArray()) yield return c;
    }

    /// <summary>Every object at any depth, the node itself first.</summary>
    public static IEnumerable<JsonElement> Objects(JsonElement node)
    {
        if (node.ValueKind == JsonValueKind.Object)
        {
            yield return node;
            foreach (var property in node.EnumerateObject())
                foreach (var inner in Objects(property.Value)) yield return inner;
        }
        else if (node.ValueKind == JsonValueKind.Array)
        {
            foreach (var element in node.EnumerateArray())
                foreach (var inner in Objects(element)) yield return inner;
        }
    }

    public static int? Quay(JsonElement citation) =>
        Property(citation, "quay") is { ValueKind: JsonValueKind.Number } q && q.TryGetInt32(out int grade) ? grade : null;
}
