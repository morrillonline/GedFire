using System.Text.Json;

namespace GedFire.Mcp;

// ---------------------------------------------------------------------------
// Reads a tool argument that was saved to a JSON file instead of being passed
// inline: the file holds one object whose single property is the argument,
// e.g. {"findings": [...]} for draft_changeset's inputPath. Lets an author
// re-run a tool on a saved input without pasting it back through the model.
// ---------------------------------------------------------------------------

public static class ArgumentFile
{
    /// <summary>
    /// Resolves an argument that may arrive inline or by file. True with <paramref name="value"/> default when
    /// neither was given; false with an error when both were, or the file cannot be used.
    /// </summary>
    public static bool TryResolve(
        JsonElement inline, string inlineField, JsonElement pathElement, string pathField,
        out JsonElement value, out string? error)
    {
        value = default;
        if (!ToolArguments.TryReadOptionalString(pathElement, pathField, out string? path, out error)) return false;
        if (path is null)
        {
            value = inline;
            return true;
        }

        if (inline.ValueKind is not (JsonValueKind.Undefined or JsonValueKind.Null))
        {
            error = $"give {inlineField} or {pathField}, not both.";
            return false;
        }
        if (string.IsNullOrWhiteSpace(path))
        {
            error = $"{pathField} must not be blank.";
            return false;
        }
        return TryReadFile(path.Trim(), inlineField, pathField, out value, out error);
    }

    /// <summary>Reads <paramref name="property"/> from the JSON object in <paramref name="stream"/>; the object may hold nothing else.</summary>
    public static bool TryRead(Stream stream, string property, string source, out JsonElement value, out string? error)
    {
        value = default;
        try
        {
            using var document = JsonDocument.Parse(stream);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                error = $"{source} must hold a JSON object with a \"{property}\" property, not {ToolArguments.Describe(root)}.";
                return false;
            }

            foreach (var member in root.EnumerateObject().Where(m => m.Name != property))
            {
                error = $"{source}.{member.Name} is not recognized; the file holds only {{\"{property}\": ...}}.";
                return false;
            }
            if (!root.TryGetProperty(property, out var found))
            {
                error = $"{source} has no \"{property}\" property.";
                return false;
            }

            value = found.Clone();
            error = null;
            return true;
        }
        catch (JsonException ex)
        {
            error = $"{source} is not valid JSON: {ex.Message}";
            return false;
        }
    }

    static bool TryReadFile(string path, string property, string pathField, out JsonElement value, out string? error)
    {
        value = default;
        string fullPath = Path.GetFullPath(path);
        try
        {
            using var stream = File.OpenRead(fullPath);
            return TryRead(stream, property, $"{pathField} {fullPath}", out value, out error);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            error = $"{pathField} {fullPath} could not be read: {ex.Message}";
            return false;
        }
    }
}
