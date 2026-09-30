using System.Text.Json;
using System.Text.RegularExpressions;

namespace GedFire.Mcp;

// ---------------------------------------------------------------------------
// Shape checks for tool arguments bound as raw JsonElement. Every rejection
// names the field and says what would have been accepted, so a wrong shape is
// never reduced to a generic "error occurred" by the SDK's argument binder.
// ---------------------------------------------------------------------------

public static partial class ToolArguments
{
    [GeneratedRegex(@"^@[^@]+@$")]
    private static partial Regex XrefPattern();

    public static bool TryReadXrefList(
        JsonElement element, string field, int maxItems, out List<string> xrefs, out string? error)
    {
        xrefs = [];
        if (element.ValueKind == JsonValueKind.Undefined || element.ValueKind == JsonValueKind.Null)
        {
            error = $"{field} is required: an array of 1 to {maxItems} xrefs such as [\"@I00006@\"].";
            return false;
        }
        if (element.ValueKind != JsonValueKind.Array)
        {
            error = $"{field} must be an array of xref strings such as [\"@I00006@\"], not {Describe(element)}.";
            return false;
        }

        int count = element.GetArrayLength();
        if (count < 1 || count > maxItems)
        {
            error = $"{field} must contain 1 to {maxItems} xrefs, got {count}.";
            return false;
        }

        int index = 0;
        foreach (var item in element.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.String)
            {
                error = $"{field}[{index}] must be an xref string such as \"@I00006@\", not {Describe(item)}.";
                return false;
            }
            string xref = item.GetString()!.Trim();
            if (!XrefPattern().IsMatch(xref))
            {
                error = $"{field}[{index}] must be an xref such as \"@I00006@\", got \"{xref}\".";
                return false;
            }
            xrefs.Add(xref);
            index++;
        }

        error = null;
        return true;
    }

    public static bool TryReadOptionalStringList(
        JsonElement element, string field, out List<string> values, out string? error)
    {
        values = [];
        error = null;
        if (IsAbsent(element)) return true;
        if (element.ValueKind != JsonValueKind.Array)
        {
            error = $"{field} must be an array of non-blank strings such as [\"Smith\"], not {Describe(element)}.";
            return false;
        }

        int index = 0;
        foreach (var item in element.EnumerateArray())
        {
            string? text = item.ValueKind == JsonValueKind.String ? item.GetString()!.Trim() : null;
            if (string.IsNullOrEmpty(text))
            {
                error = $"{field}[{index}] must be a non-blank string, not {(item.ValueKind == JsonValueKind.String ? "a blank string" : Describe(item))}.";
                return false;
            }
            values.Add(text);
            index++;
        }
        return true;
    }

    public static bool TryReadOptionalString(JsonElement element, string field, out string? value, out string? error)
    {
        value = null;
        error = null;
        if (IsAbsent(element)) return true;
        if (element.ValueKind != JsonValueKind.String)
        {
            error = $"{field} must be a string, not {Describe(element)}.";
            return false;
        }
        value = element.GetString();
        return true;
    }

    public static bool TryReadOptionalBool(JsonElement element, string field, out bool value, out string? error)
    {
        value = false;
        error = null;
        if (IsAbsent(element)) return true;
        if (element.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            error = $"{field} must be true or false, not {Describe(element)}.";
            return false;
        }
        value = element.GetBoolean();
        return true;
    }

    public static bool TryReadOptionalInt(
        JsonElement element, string field, int min, int max, int defaultValue, out int value, out string? error)
    {
        value = defaultValue;
        error = null;
        if (IsAbsent(element)) return true;
        if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt32(out int parsed) || parsed < min || parsed > max)
        {
            error = $"{field} must be an integer between {min} and {max}, not {Describe(element)}.";
            return false;
        }
        value = parsed;
        return true;
    }

    static bool IsAbsent(JsonElement element) =>
        element.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null;

    public static string Describe(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => "a string",
        JsonValueKind.Number => "a number",
        JsonValueKind.True or JsonValueKind.False => "a boolean",
        JsonValueKind.Object => "an object",
        JsonValueKind.Array => "an array",
        _ => "null",
    };
}
