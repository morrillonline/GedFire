using System.Text;
using System.Text.Json;

namespace GedCore.Drafting;

/// <summary>Renders one changeset as a numbered section of a proposal: each item's operations, then the evidence behind them.</summary>
internal static class ChangesetSection
{
    public static void Write(int number, ProposalChangeset changeset, StringBuilder markdown, List<string> errors)
    {
        JsonDocument document;
        try { document = JsonDocument.Parse(changeset.Json); }
        catch (JsonException ex)
        {
            errors.Add($"{changeset.Name} is not valid JSON: {ex.Message}");
            return;
        }

        using (document)
        {
            var root = document.RootElement;
            if (!TryArray(root, "items", out var items))
                errors.Add($"{changeset.Name} has no items to present.");
            else if (!TryArray(root, "findings", out var table))
                errors.Add($"{changeset.Name} carries no findings; only a changeset drafted by draft_changeset can be presented.");
            else
                WriteSection(number, changeset, items, table, markdown, errors);
        }
    }

    static void WriteSection(
        int number, ProposalChangeset changeset, JsonElement items, JsonElement table, StringBuilder markdown, List<string> errors)
    {
        var findings = table.EnumerateArray().Where(f => Text(f, "id") is not null).DistinctBy(f => Text(f, "id")).ToList();
        markdown.AppendLine($"## Changeset {number}: {Path.GetFileName(changeset.Name)}").AppendLine();
        markdown.AppendLine($"- File: `{changeset.Name}`");
        markdown.AppendLine($"- SHA-256: `{changeset.Sha256}`");
        markdown.AppendLine($"- Items: {items.GetArrayLength()}").AppendLine();

        foreach (var item in items.EnumerateArray())
            WriteItem(changeset.Name, item, findings, markdown, errors);
    }

    static void WriteItem(string name, JsonElement item, List<JsonElement> findings, StringBuilder markdown, List<string> errors)
    {
        int number = item.TryGetProperty("item", out var n) && n.TryGetInt32(out int value) ? value : 0;
        markdown.AppendLine($"### Item {number}: {Text(item, "target")}").AppendLine();
        markdown.AppendLine("#### Operations").AppendLine();

        var cited = new List<JsonElement>();
        int index = 0;
        foreach (var op in TryArray(item, "ops", out var ops) ? ops.EnumerateArray() : [])
        {
            index++;
            var ids = Strings(op, "findings");
            foreach (string id in ids.Where(id => findings.All(f => Text(f, "id") != id)))
                errors.Add($"{name}: item {number}, operation {index} cites finding {id}, which the changeset does not carry.");
            cited.AddRange(ids.SelectMany(id => findings.Where(f => Text(f, "id") == id)));
            markdown.AppendLine($"{index}. {OpLine(op, ids)}");
        }
        markdown.AppendLine();

        ArgumentSection.Write([.. cited.DistinctBy(f => Text(f, "id"))], markdown);

        var shown = cited.Concat(findings.Where(f => f.TryGetProperty("item", out var i) && i.TryGetInt32(out int fi) && fi == number))
            .DistinctBy(f => Text(f, "id")).ToList();
        if (shown.Count == 0) return;

        markdown.AppendLine("#### Evidence").AppendLine();
        foreach (var finding in shown) WriteFinding(finding, markdown);
    }

    static string OpLine(JsonElement op, IReadOnlyList<string> findingIds)
    {
        var line = new StringBuilder($"`{Text(op, "op")}`: {OpSummary.Describe(op)}");
        var quays = OpSummary.Quays(op);
        if (quays.Count > 0) line.Append($" (QUAY {string.Join(", ", quays)})");
        if (findingIds.Count > 0) line.Append($", evidence {string.Join(", ", findingIds)}");
        return line.ToString();
    }

    static void WriteFinding(JsonElement finding, StringBuilder markdown)
    {
        var source = finding.TryGetProperty("source", out var s) ? s : default;
        markdown.AppendLine($"##### {Text(finding, "id")}: {Text(source, "name")}").AppendLine();
        markdown.AppendLine($"- Source: {Text(source, "name")}{(Text(source, "author") is { } author ? $", by {author}" : "")}");
        markdown.AppendLine($"- Locator: {Text(source, "locator")}");
        if (Text(source, "url") is { } url) markdown.AppendLine($"- URL: {url}");
        if (Text(source, "accessed") is { } accessed) markdown.AppendLine($"- Accessed: {accessed}");
        markdown.AppendLine($"- Source class: {Text(source, "class")}");
        markdown.AppendLine($"- Information: {Text(source, "information")}");

        var claims = TryArray(finding, "claims", out var list) ? list.EnumerateArray().ToList() : [];
        if (claims.Count > 0)
        {
            markdown.AppendLine("- Quality grades:");
            foreach (var claim in claims) markdown.AppendLine($"  - {ClaimLine(claim)}");
        }

        markdown.AppendLine().AppendLine("Verbatim extract:").AppendLine();
        foreach (string line in (Text(finding, "extract") ?? "").ReplaceLineEndings("\n").Split('\n'))
            markdown.AppendLine($"> {line}".TrimEnd());
        markdown.AppendLine();
    }

    static string ClaimLine(JsonElement claim)
    {
        string what = Text(claim, "relation") is { } relation
            ? $"{relation} {Text(claim, "person")} with {Text(claim, "other")}"
            : $"{Text(claim, "fact")} on {Text(claim, "record")}: {ClaimValue(claim)}";
        var overrides = new[] { Text(claim, "class") is { } c ? $"class {c}" : null, Text(claim, "information") is { } i ? $"information {i}" : null }
            .OfType<string>().ToList();
        string grade = claim.TryGetProperty("quay", out var q) && q.TryGetInt32(out int quay) ? $"QUAY {quay}" : "ungraded";
        return $"{what}: {grade}{(overrides.Count > 0 ? $" ({string.Join(", ", overrides)} for this claim)" : "")}";
    }

    static string ClaimValue(JsonElement claim)
    {
        if (Text(claim, "value") is { } value) return value;
        string? date = Text(claim, "date");
        string? place = Text(claim, "place");
        return date is not null && place is not null ? $"{date} at {place}" : date ?? place ?? "no value";
    }

    static bool TryArray(JsonElement node, string field, out JsonElement array)
    {
        array = default;
        return node.ValueKind == JsonValueKind.Object && node.TryGetProperty(field, out array) && array.ValueKind == JsonValueKind.Array;
    }

    static List<string> Strings(JsonElement node, string field) =>
        TryArray(node, field, out var array)
            ? [.. array.EnumerateArray().Where(v => v.ValueKind == JsonValueKind.String).Select(v => v.GetString()!)]
            : [];

    static string? Text(JsonElement node, string field) =>
        node.ValueKind == JsonValueKind.Object && node.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
