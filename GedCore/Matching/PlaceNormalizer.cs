using System.Text.RegularExpressions;

namespace GedCore.Matching;

/// <summary>
/// Reads a place as the jurisdictions it names, ignoring spelling noise that does not change the place: case, hyphens,
/// runs of whitespace, trailing periods, and a trailing County, Co. or Parish. Abbreviations are left alone.
/// </summary>
public static partial class PlaceNormalizer
{
    public static IReadOnlyList<string> Jurisdictions(string? place) =>
        string.IsNullOrWhiteSpace(place) ? [] : [.. place.Split(',').Select(Jurisdiction).Where(j => j.Length > 0)];

    /// <summary>The jurisdictions as one name-normalized string, for substring matching.</summary>
    public static string ForMatching(string? place) => PersonNameNormalizer.Normalize(string.Join(' ', Jurisdictions(place)));

    static string Jurisdiction(string text)
    {
        string spaced = WhiteSpace().Replace(text.Replace('-', ' '), " ").Trim().TrimEnd('.').TrimEnd().ToUpperInvariant();
        return CountyWord().Replace(spaced, "").TrimEnd('.');
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhiteSpace();

    // Only after a name, so a jurisdiction that is just "County" is kept.
    [GeneratedRegex(@"(?<=\S)\s+(COUNTY|CO|PARISH)$")]
    private static partial Regex CountyWord();
}
