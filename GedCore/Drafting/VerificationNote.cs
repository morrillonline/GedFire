namespace GedCore.Drafting;

/// <summary>Composes the note that names a record's facts as awaiting a second source.</summary>
internal static class VerificationNote
{
    static readonly Dictionary<string, string> Labels = new(StringComparer.Ordinal)
    {
        ["BIRT"] = "birth", ["DEAT"] = "death", ["BURI"] = "burial", ["MARR"] = "marriage",
        ["CHR"] = "christening", ["BAPM"] = "baptism", ["CENS"] = "census entry", ["RESI"] = "residence",
    };

    public static string Text(string prefix, IEnumerable<string> tags, string? personName, IEnumerable<string> sources, IEnumerable<string> information)
    {
        var labelList = tags.Distinct().Select(t => Labels.GetValueOrDefault(t, t.ToLowerInvariant())).ToList();
        string labels = JoinAnd(labelList);
        bool single = labelList.Count == 1;
        string owner = personName is null ? "" : $" of {personName}";
        string basis = JoinAnd(sources.Distinct().ToList());
        string grade = string.Join("/", information.Distinct());
        return $"{Capitalize(prefix.Trim())} the {labels}{owner} {(single ? "rests" : "rest")} on {basis} ({grade} information) alone and {(single ? "has" : "have")} not yet been confirmed by a second source.";
    }

    static string Capitalize(string text) => char.ToUpperInvariant(text[0]) + text[1..];

    static string JoinAnd(List<string> parts) =>
        parts.Count <= 1 ? string.Join("", parts) : string.Join(", ", parts[..^1]) + " and " + parts[^1];
}
