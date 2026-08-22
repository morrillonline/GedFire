using System.Text;

namespace GedFire.Match;

// ---------------------------------------------------------------------------
// Shared name normalization (docs/design/mcp-server.md "Shared normalization").
// Extracted from the private routine PersonIndexExporter used to own, with
// one deliberate behavior change: a hyphen is kept as a literal character
// within a name instead of being collapsed to a token boundary, so a
// hyphenated surname ("Smith-Jones") stays one token. PersonIndexExporter
// now delegates here instead of normalizing independently (requirement 1).
// ---------------------------------------------------------------------------

public static class PersonNameNormalizer
{
    /// <summary>
    /// Normalize a name (or name-shaped free text, e.g. a place) for matching:
    /// keep Unicode letters/digits/underscore/hyphen, uppercase letters,
    /// convert spaces to a single token boundary, drop all other punctuation,
    /// collapse repeated boundaries (spaces and hyphens) and trim them.
    /// Never throws; a null or empty input normalizes to "".
    /// </summary>
    public static string Normalize(string? name)
    {
        if (string.IsNullOrEmpty(name)) return "";

        var kept = new StringBuilder(name.Length);
        foreach (char c in name)
        {
            if (c == '-') kept.Append('-');
            else if (char.IsLetter(c) || char.IsDigit(c) || c == '_') kept.Append(char.ToUpperInvariant(c));
            else if (c == ' ') kept.Append(' ');
            // All other punctuation is dropped, not converted to a boundary.
        }

        var tokens = kept.ToString()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Select(NormalizeHyphens)
            .Where(t => t.Length > 0);

        return string.Join(' ', tokens);
    }

    // Collapse repeated hyphens to one and trim a token's leading/trailing
    // hyphens, which carry no meaning at a token edge.
    static string NormalizeHyphens(string token)
    {
        var parts = token.Split('-', StringSplitOptions.RemoveEmptyEntries);
        return string.Join('-', parts);
    }
}
