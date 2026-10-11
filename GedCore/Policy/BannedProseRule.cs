using System.Text.RegularExpressions;

namespace GedCore.Policy;

/// <summary>Published prose (note text, citation pages) must not contain banned phrases or patterns.</summary>
internal sealed class BannedProseRule(IReadOnlyList<string> phrases, IReadOnlyList<Regex> patterns) : IPolicyRule
{
    public IEnumerable<string> Check(IReadOnlyList<PolicyItem> items) =>
        items.SelectMany(item => ProseFields(item).SelectMany(field => Violations(item.Number, field)));

    IEnumerable<string> Violations(int item, (string Kind, string Text) field)
    {
        foreach (string phrase in phrases)
            if (field.Text.Contains(phrase, StringComparison.OrdinalIgnoreCase))
                yield return $"item {item}: {field.Kind} contains banned phrase \"{phrase.Trim()}\": \"{Excerpt(field.Text)}\"";

        foreach (var pattern in patterns)
            if (pattern.Match(field.Text) is { Success: true } hit)
                yield return $"item {item}: {field.Kind} contains \"{hit.Value}\", which matches banned pattern /{pattern}/";
    }

    static string Excerpt(string text) => text.Length <= 70 ? text : text[..70] + "…";

    static IEnumerable<(string Kind, string Text)> ProseFields(PolicyItem item)
    {
        foreach (var node in item.Ops.SelectMany(OpJson.Objects))
        {
            if (OpJson.String(node, "op") == "createOrUpdateNote" && OpJson.String(node, "text") is { } text)
                yield return ("note text", text);
            if (OpJson.String(node, "note") is { } note)
                yield return ("note text", note);
            if (OpJson.String(node, "source") is not null && OpJson.String(node, "page") is { } page)
                yield return ("citation page", page);
        }
    }
}
