using System.Text.Json;

namespace GedCore.Drafting;

/// <summary>Describes one changeset op in a line of prose, from the op's JSON as the drafter writes it.</summary>
internal static class OpSummary
{
    public static string Describe(JsonElement op)
    {
        string kind = Text(op, "op") ?? "unknown op";
        return kind switch
        {
            "createOrUpdateVital" => $"{(Text(op, "mode") == "add" ? "Add" : "Set")} {Text(op, "fact")} on {Text(op, "record")}: {ValueOf(op)}",
            "createOrUpdateCitation" => $"Cite {Text(op, "fact")} on {Text(op, "record")}{MatchOf(op)}",
            "createOrUpdateSpouse" => $"Link {Text(op, "person")} to spouse {PersonRef(op, "spouse")}{FamilyOf(op)}",
            "createOrUpdateChild" => $"Add child {PersonRef(op, "child")} to {Text(op, "family")}",
            "createOrUpdateParent" => $"Add {Text(op, "role")} {PersonRef(op, "parent")} to {Text(op, "person")}",
            "createOrUpdateNote" => $"{(Has(op, "match") ? "Rewrite a note" : "Add a note")} on {Text(op, "record")}: \"{OneLine(Text(op, "text"))}\"",
            "deleteChild" => $"Remove child {Text(op, "child")} from {Text(op, "family")}",
            "deleteParent" => $"Remove the {Text(op, "role")} of {Text(op, "person")}",
            "deleteNote" => $"Delete a note on {Text(op, "record")}: \"{OneLine(Text(op, "text"))}\"",
            _ => $"{kind} on {Text(op, "record") ?? Text(op, "person") ?? Text(op, "xref") ?? "the document"}",
        };
    }

    /// <summary>The distinct grades of the citations an op carries, lowest first.</summary>
    public static IReadOnlyList<int> Quays(JsonElement op) =>
        [.. Citations(op).Select(c => c.TryGetProperty("quay", out var q) && q.TryGetInt32(out int grade) ? grade : (int?)null)
            .OfType<int>().Distinct().Order()];

    static IEnumerable<JsonElement> Citations(JsonElement op)
    {
        if (op.TryGetProperty("citation", out var one) && one.ValueKind == JsonValueKind.Object) yield return one;
        if (op.TryGetProperty("citations", out var many) && many.ValueKind == JsonValueKind.Array)
            foreach (var citation in many.EnumerateArray()) yield return citation;
    }

    static string ValueOf(JsonElement op)
    {
        if (!op.TryGetProperty("value", out var value)) return "no value";
        if (value.ValueKind == JsonValueKind.String) return value.GetString() ?? "";

        string? date = Text(value, "date");
        string? place = Text(value, "place");
        return date is not null && place is not null ? $"{date} at {place}" : date ?? place ?? "no value";
    }

    static string MatchOf(JsonElement op)
    {
        if (!op.TryGetProperty("match", out var match) || match.ValueKind != JsonValueKind.Object) return "";
        var parts = new[] { Text(match, "date"), Text(match, "place") }.OfType<string>().ToList();
        return parts.Count == 0 ? "" : $" ({string.Join(" at ", parts)})";
    }

    static string FamilyOf(JsonElement op) => Text(op, "family") is { } family ? $" in {family}" : "";

    // A person the changeset creates is described inline; one it only points at is a bare xref.
    static string PersonRef(JsonElement op, string field)
    {
        if (!op.TryGetProperty(field, out var person)) return "unknown";
        if (person.ValueKind == JsonValueKind.String) return person.GetString() ?? "unknown";
        return Text(person, "name") is { } name ? $"{name} ({Text(person, "xref")})" : Text(person, "xref") ?? "unknown";
    }

    static bool Has(JsonElement op, string field) => op.TryGetProperty(field, out _);

    static string OneLine(string? text) => string.Join(' ', (text ?? "").Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries));

    static string? Text(JsonElement node, string field) =>
        node.ValueKind == JsonValueKind.Object && node.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
