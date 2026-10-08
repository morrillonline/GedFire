using System.Text.Json;

namespace GedFire.Mcp;

// ---------------------------------------------------------------------------
// Turns the raw "hints" argument into FindPersonHintsArgs after checking the
// shape of every known field, so a scalar where an object belongs, or a string
// where a number belongs, is rejected by field name with the accepted shape.
// Unknown property names are left in place for FindPersonTool's own validation
// to report.
// ---------------------------------------------------------------------------

public static class FindPersonHintsReader
{
    const string EventShape = "{\"year\": 1741, \"place\": \"Harwick\"}";

    public static bool TryRead(JsonElement hints, out FindPersonHintsArgs? args, out string? error)
    {
        args = null;
        error = null;
        if (hints.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null) return true;

        if (hints.ValueKind != JsonValueKind.Object)
        {
            error = $"hints must be an object such as {{\"birth\": {EventShape}}}, not {ToolArguments.Describe(hints)}.";
            return false;
        }

        foreach (var property in hints.EnumerateObject())
        {
            error = property.Name switch
            {
                "sex" => CheckSex(property.Value),
                "birth" or "death" => CheckEvent(property.Value, $"hints.{property.Name}"),
                "parents" => CheckParents(property.Value),
                "spouse" => CheckSpouse(property.Value),
                _ => null,
            };
            if (error is not null) return false;
        }

        args = hints.Deserialize<FindPersonHintsArgs>();
        return true;
    }

    static string? CheckSex(JsonElement value) =>
        value.ValueKind == JsonValueKind.String && value.GetString() is "M" or "F"
            ? null
            : $"hints.sex must be \"M\" or \"F\", not {ToolArguments.Describe(value)}.";

    static string? CheckEvent(JsonElement value, string path)
    {
        if (value.ValueKind != JsonValueKind.Object)
            return $"{path} must be an object such as {EventShape}, not {ToolArguments.Describe(value)}.";

        foreach (var property in value.EnumerateObject())
        {
            string? error = property.Name switch
            {
                "year" => CheckYear(property.Value, $"{path}.year"),
                "place" => CheckString(property.Value, $"{path}.place"),
                _ => null,
            };
            if (error is not null) return error;
        }
        return null;
    }

    static string? CheckParents(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object)
            return $"hints.parents must be an object such as {{\"father\": \"Levi Ashworth\", \"mother\": \"Hannah Wood\"}}, not {ToolArguments.Describe(value)}.";

        foreach (var property in value.EnumerateObject())
        {
            string? error = property.Name is "father" or "mother"
                ? CheckString(property.Value, $"hints.parents.{property.Name}")
                : null;
            if (error is not null) return error;
        }
        return null;
    }

    static string? CheckSpouse(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object)
            return $"hints.spouse must be an object such as {{\"name\": \"Beatrice Fenwick\", \"marriage\": {{\"year\": 1770}}}}, not {ToolArguments.Describe(value)}.";

        foreach (var property in value.EnumerateObject())
        {
            string? error = property.Name switch
            {
                "name" => CheckString(property.Value, "hints.spouse.name"),
                "marriage" => CheckEvent(property.Value, "hints.spouse.marriage"),
                _ => null,
            };
            if (error is not null) return error;
        }
        return null;
    }

    static string? CheckYear(JsonElement value, string path) =>
        value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out _)
            ? null
            : $"{path} must be an integer such as 1741, not {ToolArguments.Describe(value)}.";

    static string? CheckString(JsonElement value, string path) =>
        value.ValueKind == JsonValueKind.String
            ? null
            : $"{path} must be a string, not {ToolArguments.Describe(value)}.";
}
