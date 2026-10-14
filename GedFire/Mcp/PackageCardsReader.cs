using System.Text.Json;
using GedCore.Points;

namespace GedFire.Mcp;

// ---------------------------------------------------------------------------
// Reads a package's cards file: one JSON object mapping each anchor's xref to
// the array of research cards that anchor's item serves, each card in
// validate_changeset's "cards" shape. Every rejection names the anchor and
// field.
// ---------------------------------------------------------------------------

public static class PackageCardsReader
{
    public static bool TryRead(
        Stream stream, string source, out IReadOnlyDictionary<string, IReadOnlyList<PointsCard>> cards, out string? error)
    {
        var result = new Dictionary<string, IReadOnlyList<PointsCard>>(StringComparer.Ordinal);
        cards = result;
        error = null;

        JsonElement root;
        try
        {
            using var document = JsonDocument.Parse(stream);
            root = document.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            error = $"{source} is not valid JSON: {ex.Message}";
            return false;
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            error = $"{source} must hold an object mapping each anchor xref to its cards array, not {ToolArguments.Describe(root)}.";
            return false;
        }

        foreach (var anchor in root.EnumerateObject())
        {
            if (anchor.Value.ValueKind != JsonValueKind.Array)
            {
                error = $"{source}: the cards for {anchor.Name} must be an array, not {ToolArguments.Describe(anchor.Value)}.";
                return false;
            }
            if (!PointsCardReader.TryRead(anchor.Value, out var read, out string? cardError))
            {
                error = $"{source}: {anchor.Name}: {cardError}";
                return false;
            }
            result[anchor.Name] = read ?? [];
        }
        return true;
    }
}
