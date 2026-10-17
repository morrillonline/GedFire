using System.Text;
using System.Text.Json;

namespace GedCore.Drafting;

/// <summary>
/// Renders the identity argument of the findings an item cites: why this person, why not the same-named rivals,
/// what to be cautious of, and how hard the identification was. Reads only the findings as the changeset carries them.
/// </summary>
internal static class ArgumentSection
{
    const string NoneRecorded = "(none recorded)";

    static readonly string[] Difficulties = ["low", "medium", "high"];

    public static void Write(IReadOnlyList<JsonElement> findings, StringBuilder markdown)
    {
        var arguments = findings.Select(f => f.TryGetProperty("argument", out var a) && a.ValueKind == JsonValueKind.Object ? a : (JsonElement?)null)
            .OfType<JsonElement>().ToList();

        Section(markdown, "Why him", arguments.SelectMany(a => Strings(a, "anchors")));
        Section(markdown, "Why not others", arguments.SelectMany(Rivals));
        Section(markdown, "Conflicts and cautions", arguments.SelectMany(a => Strings(a, "cautions")));
        markdown.AppendLine("#### Difficulty").AppendLine().AppendLine(HardestOf(arguments)).AppendLine();
    }

    static void Section(StringBuilder markdown, string heading, IEnumerable<string> lines)
    {
        var distinct = lines.Distinct(StringComparer.Ordinal).ToList();
        markdown.AppendLine($"#### {heading}").AppendLine();
        if (distinct.Count == 0) markdown.AppendLine(NoneRecorded);
        foreach (string line in distinct) markdown.AppendLine($"- {line}");
        markdown.AppendLine();
    }

    // The hardest difficulty any finding states; a finding that states none counts as low.
    static string HardestOf(List<JsonElement> arguments) =>
        Difficulties[arguments.Select(a => Array.IndexOf(Difficulties, Text(a, "difficulty")?.ToLowerInvariant())).DefaultIfEmpty(0).Max(rank => Math.Max(0, rank))];

    static IEnumerable<string> Rivals(JsonElement argument)
    {
        foreach (string identity in Strings(argument, "identity")) yield return identity;
        if (!argument.TryGetProperty("exclusions", out var exclusions) || exclusions.ValueKind != JsonValueKind.Array) yield break;
        foreach (var exclusion in exclusions.EnumerateArray()) yield return Exclusion(exclusion);
    }

    static string Exclusion(JsonElement exclusion)
    {
        string by = Text(exclusion, "fact") is { } fact ? $" by {fact}" : "";
        string finding = Text(exclusion, "finding") is { } id ? $" (finding {id})" : "";
        return $"{Who(exclusion) ?? "A rival"} is excluded{by}{finding}.";
    }

    static string? Who(JsonElement node) => (Text(node, "name"), Text(node, "xref")) switch
    {
        ({ } name, { } xref) => $"{name} ({xref})",
        ({ } name, null) => name,
        (null, { } xref) => xref,
        _ => null,
    };

    static IEnumerable<string> Strings(JsonElement argument, string field)
    {
        if (!argument.TryGetProperty(field, out var value)) yield break;
        if (value.ValueKind == JsonValueKind.String) yield return value.GetString()!;
        else if (value.ValueKind == JsonValueKind.Array)
            foreach (var item in value.EnumerateArray().Where(i => i.ValueKind == JsonValueKind.String)) yield return item.GetString()!;
    }

    static string? Text(JsonElement node, string field) =>
        node.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;
}
