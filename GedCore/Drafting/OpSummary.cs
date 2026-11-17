using System.Text.Json;

namespace GedCore.Drafting;

/// <summary>Describes one changeset op in a line of prose, from the op's JSON as the drafter writes it.</summary>
internal static class OpSummary
{
    /// <param name="people">The op's item's "people" declarations, which name the new people its ops create.</param>
    public static string Describe(JsonElement op, JsonElement people = default)
    {
        string kind = Text(op, "op") ?? "unknown op";
        return kind switch
        {
            "createOrUpdateVital" => $"{(Text(op, "mode") == "add" ? "Add" : "Set")} {Text(op, "fact")} on {Text(op, "record")}: {ValueOf(op)}",
            "createOrUpdateCitation" => $"Cite {Text(op, "fact")} on {Text(op, "record")}{MatchOf(op)}",
            "createOrUpdateSpouse" => $"Link {Text(op, "person")} to spouse {PersonRef(op, "spouse", people)}{FamilyOf(op)}",
            "createOrUpdateChild" => $"Add child {PersonRef(op, "child", people)} to {Text(op, "family")}",
            "createOrUpdateParent" => $"Add {Text(op, "role")} {PersonRef(op, "parent", people)} to {Text(op, "person")}",
            "createOrUpdateNote" => $"{(Has(op, "match") ? "Rewrite" : "Add")} a {(Has(op, "purpose") ? "biography " : "")}note on {Text(op, "record")}: \"{OneLine(Text(op, "text"))}\"",
            "deleteChild" => $"Remove child {Text(op, "child")} from {Text(op, "family")}",
            "deleteParent" => $"Remove the {Text(op, "role")} of {Text(op, "person")}",
            "deleteVital" => $"Withdraw {Text(op, "fact")} on {Text(op, "record")}{MatchOf(op)}",
            "deleteNote" => $"Delete a note on {Text(op, "record")}: \"{OneLine(Text(op, "text"))}\"",
            _ => $"{kind} on {Text(op, "record") ?? Text(op, "person") ?? Text(op, "xref") ?? "the document"}",
        };
    }

    /// <summary>The distinct grades of the citations an op carries, lowest first.</summary>
    public static IReadOnlyList<int> Quays(JsonElement op) =>
        [.. Citations(op).Select(c => c.TryGetProperty("quay", out var q) && q.TryGetInt32(out int grade) ? grade : (int?)null)
            .OfType<int>().Distinct().Order()];

    // The citations of the fact or note the op writes, and of the family a relationship op links.
    static IEnumerable<JsonElement> Citations(JsonElement op) =>
        new[] { "citations", "familyCitations" }
            .Where(key => op.TryGetProperty(key, out var many) && many.ValueKind == JsonValueKind.Array)
            .SelectMany(key => op.GetProperty(key).EnumerateArray());

    static string ValueOf(JsonElement op)
    {
        if (Text(op, "value") is { } value) return value;
        string? date = Text(op, "date");
        string? place = Text(op, "place");
        return date is not null && place is not null ? $"{date} at {place}" : date ?? place ?? "no value";
    }

    static string MatchOf(JsonElement op)
    {
        if (!op.TryGetProperty("match", out var match) || match.ValueKind != JsonValueKind.Object) return "";
        var parts = new[] { Text(match, "date"), Text(match, "place") }.OfType<string>().ToList();
        return parts.Count == 0 ? "" : $" ({string.Join(" at ", parts)})";
    }

    static string FamilyOf(JsonElement op) => Text(op, "family") is { } family ? $" in {family}" : "";

    // A new person is named by xref and declared in the item's people.
    static string PersonRef(JsonElement op, string field, JsonElement people)
    {
        if (Text(op, field) is not { } xref) return "unknown";
        var declared = people.ValueKind == JsonValueKind.Array
            ? people.EnumerateArray().FirstOrDefault(p => Text(p, "xref") == xref)
            : default;
        return Text(declared, "name") is { } declaredName ? $"{declaredName} ({xref})" : xref;
    }

    static bool Has(JsonElement op, string field) => op.TryGetProperty(field, out _);

    static string OneLine(string? text) => string.Join(' ', (text ?? "").Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries));

    static string? Text(JsonElement node, string field) =>
        node.ValueKind == JsonValueKind.Object && node.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
