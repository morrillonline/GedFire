using System.Collections.Concurrent;
using System.Text.RegularExpressions;

namespace GedCore.Apply;

// ---------------------------------------------------------------------------
// Deterministic numeric xref allocation: the allocator considers only xrefs
// matching ^@<prefix>(\d+)@$ and chooses one more than the largest numeric
// suffix, padded to the greater of five digits or the widest matching
// suffix already present, naturally expanding when the next value exceeds
// that width. An empty kind starts at 00001; mixed @I1@/@I00009@ input
// produces @I00010@; no existing xref is renumbered or reused.
// ---------------------------------------------------------------------------

internal static class XrefMinter
{
    const int MinWidth = 5;

    // One compiled pattern per prefix ("I", "F", "S", "M"), built once and reused.
    private static readonly ConcurrentDictionary<string, Regex> Patterns = new(StringComparer.Ordinal);

    /// <summary>
    /// Mint the next free xref for <paramref name="prefix"/> (one of "I",
    /// "F", "S", "M") from the numeric xrefs already present in
    /// <paramref name="existingXrefs"/> -- the live document's own records of
    /// the matching kind, which by construction already includes every xref
    /// minted earlier in the same apply run (each is inserted into the
    /// document immediately after minting).
    /// </summary>
    public static string MintNext(IEnumerable<string> existingXrefs, string prefix)
    {
        var pattern = Patterns.GetOrAdd(prefix,
            p => new Regex($@"^@{Regex.Escape(p)}(\d+)@$", RegexOptions.Compiled));

        long maxValue = 0;
        int maxWidth = MinWidth;
        bool any = false;
        foreach (var xref in existingXrefs)
        {
            var m = pattern.Match(xref);
            if (!m.Success) continue;
            string digits = m.Groups[1].Value;
            // A suffix with more digits than long can represent cannot be
            // the current maximum in any realistic document; skip it rather
            // than overflow. (No practical GEDCOM file approaches this.)
            if (digits.Length > 18) continue;
            any = true;
            long value = long.Parse(digits);
            if (value > maxValue) maxValue = value;
            if (digits.Length > maxWidth) maxWidth = digits.Length;
        }

        long next = any ? maxValue + 1L : 1L;
        string padded = next.ToString().PadLeft(maxWidth, '0');
        return $"@{prefix}{padded}@";
    }
}
