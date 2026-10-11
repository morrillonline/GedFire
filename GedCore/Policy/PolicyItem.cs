using System.Text.Json;

namespace GedCore.Policy;

/// <summary>One selected changeset item as raw JSON ops, the form every policy rule reads.</summary>
internal sealed record PolicyItem(int Number, IReadOnlyList<JsonElement> Ops)
{
    public static IReadOnlyList<PolicyItem> Read(JsonElement changeset, IReadOnlySet<int>? selected)
    {
        if (!changeset.TryGetProperty("items", out var items) || items.ValueKind != JsonValueKind.Array) return [];
        return [.. items.EnumerateArray()
            .Where(i => i.TryGetProperty("item", out var n) && n.TryGetInt32(out int number) && (selected is null || selected.Contains(number)))
            .Select(i => new PolicyItem(
                i.GetProperty("item").GetInt32(),
                i.TryGetProperty("ops", out var ops) ? [.. ops.EnumerateArray()] : []))];
    }
}
