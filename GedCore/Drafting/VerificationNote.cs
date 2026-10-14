using System.Text.RegularExpressions;

namespace GedCore.Drafting;

/// <summary>A verification note the compiler wrote earlier, taken apart: which facts it names, the sources it says they rest on, and the information grade.</summary>
internal sealed record ParsedVerificationNote(IReadOnlyList<string> Tags, string Basis, string Grade);

/// <summary>Composes the note that names a record's facts as awaiting a second source, and takes such a note apart again so it can be brought up to date.</summary>
internal static partial class VerificationNote
{
    static readonly Dictionary<string, string> Labels = new(StringComparer.Ordinal)
    {
        ["BIRT"] = "birth", ["DEAT"] = "death", ["BURI"] = "burial", ["MARR"] = "marriage",
        ["CHR"] = "christening", ["BAPM"] = "baptism", ["CENS"] = "census entry", ["RESI"] = "residence",
    };

    public static string Text(string prefix, IEnumerable<string> tags, string? personName, IEnumerable<string> sources, IEnumerable<string> information) =>
        Compose(prefix, tags, personName, JoinAnd(sources.Distinct().ToList()), string.Join("/", information.Distinct()));

    /// <summary>The note with only <paramref name="tags"/> named, resting on the same sources as <paramref name="note"/> did.</summary>
    public static string Rewrite(string prefix, ParsedVerificationNote note, IEnumerable<string> tags, string? personName) =>
        Compose(prefix, tags, personName, note.Basis, note.Grade);

    /// <summary>
    /// Takes apart a note in the form <see cref="Text"/> writes, for the person named <paramref name="personName"/>; null for any
    /// other note, which only its author can judge.
    /// </summary>
    public static ParsedVerificationNote? TryParse(string text, string prefix, string? personName)
    {
        string head = Capitalize(prefix.Trim()) + " the ";
        if (!text.StartsWith(head, StringComparison.Ordinal)) return null;
        if (Note().Match(text[head.Length..]) is not { Success: true } match) return null;

        string labelText = match.Groups["labels"].Value;
        if (personName is not null)
        {
            string owner = $" of {personName}";
            if (!labelText.EndsWith(owner, StringComparison.Ordinal)) return null;
            labelText = labelText[..^owner.Length];
        }

        var tags = new List<string>();
        foreach (string label in labelText.Replace(" and ", ", ").Split(", "))
        {
            if (Labels.FirstOrDefault(l => l.Value == label).Key is not { } tag) return null;
            tags.Add(tag);
        }
        return new ParsedVerificationNote(tags, match.Groups["basis"].Value, match.Groups["grade"].Value);
    }

    static string Compose(string prefix, IEnumerable<string> tags, string? personName, string basis, string grade)
    {
        var labelList = tags.Distinct().Select(t => Labels.GetValueOrDefault(t, t.ToLowerInvariant())).ToList();
        string labels = JoinAnd(labelList);
        bool single = labelList.Count == 1;
        string owner = personName is null ? "" : $" of {personName}";
        return $"{Capitalize(prefix.Trim())} the {labels}{owner} {(single ? "rests" : "rest")} on {basis} ({grade} information) alone and {(single ? "has" : "have")} not yet been confirmed by a second source.";
    }

    static string Capitalize(string text) => char.ToUpperInvariant(text[0]) + text[1..];

    static string JoinAnd(List<string> parts) =>
        parts.Count <= 1 ? string.Join("", parts) : string.Join(", ", parts[..^1]) + " and " + parts[^1];

    [GeneratedRegex(@"^(?<labels>.+?) rests? on (?<basis>.+) \((?<grade>[^)]*) information\) alone and (?:has|have) not yet been confirmed by a second source\.$")]
    private static partial Regex Note();
}
