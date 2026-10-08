using System.Text.RegularExpressions;

namespace GedCore;

/// <summary>
/// Reads the web address out of a source's note text, where the createOrUpdateSource op writes it
/// as "online at <c>URL</c>".
/// </summary>
public static partial class SourceNoteUrl
{
    [GeneratedRegex(@"https?://[^\s<>""]+", RegexOptions.IgnoreCase)]
    private static partial Regex Address();

    /// <summary>The first web address in <paramref name="text"/>, without trailing sentence punctuation; null when there is none.</summary>
    public static string? Find(string? text)
    {
        if (string.IsNullOrEmpty(text)) return null;

        var match = Address().Match(text);
        if (!match.Success) return null;

        string url = match.Value.TrimEnd('.', ',', ';', ':', ')');
        return url.Length > "https://".Length ? url : null;
    }
}
