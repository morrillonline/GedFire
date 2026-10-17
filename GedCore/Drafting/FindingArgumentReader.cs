using System.Text.Json;
using System.Text.Json.Nodes;

namespace GedCore.Drafting;

/// <summary>
/// Reads the identity argument a finding carries: why this person and not a same-named rival. It checks only the
/// shape of each field and judges nothing; the argument is returned exactly as given so it can be carried into the changeset.
/// </summary>
public static class FindingArgumentReader
{
    delegate IEnumerable<string> Shape(JsonElement value, string path);

    static readonly Shape Text = (value, path) =>
        value.ValueKind == JsonValueKind.String ? [] : [$"{path} must be a string."];

    static readonly Shape Argument = Fields(
        ("anchors", ListOf(Text)),
        ("identity", Text),
        ("exclusions", ListOf(Fields(("name", Text), ("xref", Text), ("fact", Text), ("finding", Text)))),
        ("cautions", Text),
        ("difficulty", OneOf("low", "medium", "high")));

    /// <summary>The argument as given, or null when <paramref name="argument"/> is absent or invalid; each problem is added to <paramref name="errors"/> under <paramref name="path"/>.</summary>
    public static JsonObject? Read(JsonElement argument, string path, List<string> errors)
    {
        var problems = Argument(argument, path).ToList();
        errors.AddRange(problems);
        return problems.Count == 0 ? JsonNode.Parse(argument.GetRawText())!.AsObject() : null;
    }

    static Shape Fields(params (string Name, Shape Shape)[] accepted) => (value, path) =>
    {
        if (value.ValueKind != JsonValueKind.Object) return [$"{path} must be an object."];
        var names = accepted.Select(a => a.Name).ToList();
        var unknown = value.EnumerateObject().Where(p => !names.Contains(p.Name))
            .Select(p => $"{path}.{p.Name} is not a recognized field; accepted fields: {string.Join(", ", names)}.");
        var invalid = accepted.Where(a => value.TryGetProperty(a.Name, out var field) && field.ValueKind != JsonValueKind.Null)
            .SelectMany(a => a.Shape(value.GetProperty(a.Name), $"{path}.{a.Name}"));
        return unknown.Concat(invalid);
    };

    static Shape ListOf(Shape item) => (value, path) =>
        value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray().SelectMany((element, index) => item(element, $"{path}[{index}]"))
            : [$"{path} must be an array."];

    static Shape OneOf(params string[] allowed) => (value, path) =>
        value.ValueKind == JsonValueKind.String && allowed.Contains(value.GetString()!.ToLowerInvariant())
            ? []
            : [$"{path} must be one of {string.Join(", ", allowed)}."];
}
