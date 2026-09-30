using System.Text.RegularExpressions;

namespace GedCore.Apply;

/// <summary>
/// Finds an existing source record that is the same document as a described
/// one: title, author, and publication all equal once case and runs of
/// whitespace are folded. All three must match; a missed duplicate is easy to
/// notice and merge later, while two different sources sharing one record is
/// hard to undo.
/// </summary>
internal static partial class SourceMatcher
{
    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    public static GedRecord? FindExact(GedDocument doc, string title, string? author, string? publication, GedRecord? except = null) =>
        doc.Records.FirstOrDefault(r =>
            r.Tag == "SOUR" && !ReferenceEquals(r, except) &&
            Same(Field(r, "TITL"), title) && Same(Field(r, "AUTH"), author) && Same(Field(r, "PUBL"), publication));

    public static GedRecord? FindExactMatchOf(GedDocument doc, GedRecord described, GedRecord? except = null) =>
        FindExact(doc, Field(described, "TITL"), Field(described, "AUTH"), Field(described, "PUBL"), except);

    static string Field(GedRecord source, string tag) => source.FirstChild(tag)?.FullValue() ?? "";

    static bool Same(string a, string? b) => Normalize(a) == Normalize(b);

    static string Normalize(string? value) =>
        Whitespace().Replace((value ?? "").Trim(), " ").ToLowerInvariant();
}
