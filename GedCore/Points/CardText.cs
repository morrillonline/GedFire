namespace GedCore.Points;

/// <summary>The two text foldings the card rules share: class/gap tokens and person-name comparison.</summary>
internal static class CardText
{
    /// <summary>A class or gap token in canonical form: trimmed, lowercase, hyphen-separated.</summary>
    public static string Normalize(string text) => text.Trim().ToLowerInvariant().Replace(' ', '-').Replace('_', '-');

    /// <summary>A person name reduced to its words, so "Ruth /Test/" and "Ruth  Test" compare equal.</summary>
    public static string Fold(string name) => string.Join(' ', name.Replace("/", " ").Split(' ', StringSplitOptions.RemoveEmptyEntries));
}
