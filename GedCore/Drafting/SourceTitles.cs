using System.Text;
using System.Text.RegularExpressions;

namespace GedCore.Drafting;

/// <summary>
/// Compares source titles by collection identity: case and punctuation do not matter, and a harvested
/// title's trailing " - Person Name" belongs to the record, not to the collection.
/// </summary>
public static partial class SourceTitles
{
    // A name segment has no digits, so a year range or a numbered record type is never mistaken for one.
    [GeneratedRegex(@"^(?<collection>.+)\s[-\u2013\u2014]\s(?<name>\D+)$")]
    private static partial Regex NameSuffix();

    /// <summary>The title lower-cased with every run of punctuation and whitespace reduced to one space.</summary>
    public static string Identity(string title)
    {
        var text = new StringBuilder(title.Length);
        foreach (char c in title.ToLowerInvariant())
            text.Append(char.IsLetterOrDigit(c) ? c : ' ');
        return string.Join(' ', text.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>The title without a trailing " - Person Name" segment; the title itself when it has none.</summary>
    public static string Collection(string title)
    {
        var match = NameSuffix().Match(title.Trim());
        return match.Success && Identity(match.Groups["name"].Value).Length > 0
            ? match.Groups["collection"].Value.Trim()
            : title.Trim();
    }

    public static bool SameCollection(string a, string b) => Identity(a) == Identity(b);
}
