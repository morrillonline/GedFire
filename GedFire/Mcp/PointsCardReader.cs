using System.Text.Json;
using GedCore.Points;

namespace GedFire.Mcp;

// ---------------------------------------------------------------------------
// Reads validate_changeset's optional "cards" argument, naming the field of
// any card that is the wrong shape.
// ---------------------------------------------------------------------------

public static class PointsCardReader
{
    /// <summary>True with <paramref name="cards"/> null when the argument is absent; false with an error naming the offending field.</summary>
    public static bool TryRead(JsonElement element, out IReadOnlyList<PointsCard>? cards, out string? error)
    {
        cards = null;
        error = null;
        if (element.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null) return true;
        if (element.ValueKind != JsonValueKind.Array)
        {
            error = $"cards must be an array of card objects, not {ToolArguments.Describe(element)}.";
            return false;
        }

        var result = new List<PointsCard>();
        int index = 0;
        foreach (var card in element.EnumerateArray())
        {
            if (!TryReadCard(card, $"cards[{index++}]", out var read, out error)) return false;
            result.Add(read!);
        }
        cards = result;
        return true;
    }

    static bool TryReadCard(JsonElement card, string path, out PointsCard? read, out string? error)
    {
        read = null;
        if (card.ValueKind != JsonValueKind.Object)
        {
            error = $"{path} must be an object with xref, class and points.";
            return false;
        }

        string? xref = null, cls = null, gap = null, name = null;
        if (!ToolArguments.TryReadOptionalString(Property(card, "xref"), $"{path}.xref", out xref, out error)
            || !ToolArguments.TryReadOptionalString(Property(card, "class"), $"{path}.class", out cls, out error)
            || !ToolArguments.TryReadOptionalString(Property(card, "gap"), $"{path}.gap", out gap, out error)
            || !ToolArguments.TryReadOptionalString(Property(card, "name"), $"{path}.name", out name, out error)
            || !ToolArguments.TryReadOptionalInt(Property(card, "points"), $"{path}.points", 0, int.MaxValue, -1, out int points, out error)
            || !ToolArguments.TryReadOptionalInt(Property(card, "bonus"), $"{path}.bonus", 0, int.MaxValue, 0, out int bonus, out error))
            return false;

        if (string.IsNullOrWhiteSpace(xref)) error = $"{path}.xref is required: the card subject's xref such as \"@I00006@\".";
        else if (string.IsNullOrWhiteSpace(cls)) error = $"{path}.class is required: enrich-person, new-parent, new-spouse or new-child.";
        else if (points < 0) error = $"{path}.points is required: a non-negative integer.";
        if (error is not null) return false;

        read = new PointsCard(xref!.Trim(), cls!.Trim(), gap?.Trim(), name?.Trim(), points, bonus);
        return true;
    }

    static JsonElement Property(JsonElement card, string name) =>
        card.TryGetProperty(name, out var value) ? value : default;
}
