using System.Text.RegularExpressions;

namespace GedCore.Drafting;

/// <summary>A verification note the compiler wrote earlier, taken apart: which facts it names, the sources it says they rest on, the information grade, and whether it named one source alone.</summary>
internal sealed record ParsedVerificationNote(IReadOnlyList<string> Tags, string Basis, string Grade, bool Alone = true);

/// <summary>Composes the note that names a fact as awaiting a stronger source, and takes such a note apart again so it can be brought up to date.</summary>
internal static partial class VerificationNote
{
    static readonly Dictionary<string, string> Labels = new(StringComparer.Ordinal)
    {
        ["BIRT"] = "birth", ["DEAT"] = "death", ["BURI"] = "burial", ["MARR"] = "marriage",
        ["CHR"] = "christening", ["BAPM"] = "baptism", ["CENS"] = "census entry", ["RESI"] = "residence",
    };

    // Words an author's own note may use for a fact instead of its label.
    static readonly Dictionary<string, string[]> Synonyms = new(StringComparer.Ordinal)
    {
        ["BIRT"] = ["born"], ["DEAT"] = ["died"], ["BURI"] = ["buried"], ["MARR"] = ["married"],
        ["CHR"] = ["christened"], ["BAPM"] = ["baptized", "baptised"], ["CENS"] = ["census", "enumerated"], ["RESI"] = ["resided", "lived"],
    };

    /// <summary>The note for one fact resting on <paramref name="records"/>, each named once; one record is said to stand alone.</summary>
    public static string Text(string prefix, string tag, string? personName, IReadOnlyList<string> records, IEnumerable<string> information) =>
        Compose(prefix, [tag], personName, JoinAnd([.. records.Distinct()]), string.Join("/", information.Distinct()), records.Distinct().Count() <= 1);

    /// <summary>The note with only <paramref name="tags"/> named, resting on the same sources as <paramref name="note"/> did.</summary>
    public static string Rewrite(string prefix, ParsedVerificationNote note, IEnumerable<string> tags, string? personName) =>
        Compose(prefix, tags, personName, note.Basis, note.Grade, note.Alone);

    /// <summary>Whether <paramref name="text"/> speaks of the fact <paramref name="tag"/>, by its label or a word for it.</summary>
    public static bool Mentions(string text, string tag) =>
        new[] { Labels.GetValueOrDefault(tag, tag.ToLowerInvariant()) }.Concat(Synonyms.GetValueOrDefault(tag, []))
            .Any(word => Regex.IsMatch(text, $@"\b{Regex.Escape(word)}\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));

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
        return new ParsedVerificationNote(tags, match.Groups["basis"].Value, match.Groups["grade"].Value, match.Groups["alone"].Success);
    }

    static string Compose(string prefix, IEnumerable<string> tags, string? personName, string basis, string grade, bool alone)
    {
        var labelList = tags.Distinct().Select(t => Labels.GetValueOrDefault(t, t.ToLowerInvariant())).ToList();
        string labels = JoinAnd(labelList);
        bool single = labelList.Count == 1;
        string owner = personName is null ? "" : $" of {personName}";
        string has = single ? "has" : "have";
        string caveat = alone ? $"alone and {has} not yet been confirmed by a second source" : $"and {has} not yet been confirmed by a stronger source";
        return $"{Capitalize(prefix.Trim())} the {labels}{owner} {(single ? "rests" : "rest")} on {basis} ({grade} information) {caveat}.";
    }

    static string Capitalize(string text) => char.ToUpperInvariant(text[0]) + text[1..];

    static string JoinAnd(List<string> parts) =>
        parts.Count <= 1 ? string.Join("", parts) : string.Join(", ", parts[..^1]) + " and " + parts[^1];

    [GeneratedRegex(@"^(?<labels>.+?) rests? on (?<basis>.+) \((?<grade>[^)]*) information\) (?:(?<alone>alone) and (?:has|have) not yet been confirmed by a second source|and (?:has|have) not yet been confirmed by a stronger source)\.$")]
    private static partial Regex Note();
}
