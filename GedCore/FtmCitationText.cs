namespace GedCore;

/// <summary>
/// Parses the Gedfire/FTM property-list convention used inside citation TEXT
/// (and NOTE) payloads in the legacy 5.5 data: a payload may start with one or
/// more "Key: Value|" markers, e.g. "Inline: TRUE|Actual narrative text…".
/// Keys are case-insensitive and only ASCII-32 spaces are trimmed.
///
/// Recognized keys: INLINE (narrative prose rendered as a bio paragraph),
/// NOCITATION (suppress the bibliographic citation), SHORTCITATION (override
/// citation text).  The GEDCOM 7 upgrade converts INLINE-marked citation text
/// into real NOTE structures, so in 7.0 data these markers should no longer
/// occur inside citation TEXT payloads.
///
/// Markers are only honored where the convention puts them; text that merely
/// contains a "|" is never treated as a marker (mid-text markers are inert,
/// matching the original VB Gedfire and the 2010 site output).
/// </summary>
public static class FtmCitationText
{
    /// <summary>
    /// Strip leading "Key: Value|" markers from <paramref name="text"/>,
    /// returning the remaining payload and reporting the recognized markers.
    /// Stripping stops at the first segment that is not a recognized marker,
    /// so ordinary text containing "|" passes through unchanged.
    /// </summary>
    public static string ParsePropertyList(string text,
        out bool isInlineable, out bool noCitation,
        out string shortCitation)
    {
        isInlineable  = false;
        noCitation    = false;
        shortCitation = "";

        while (TryStripLeadingMarker(ref text, out string key, out string value))
            Apply(key, value, ref isInlineable, ref noCitation, ref shortCitation);

        return text;
    }

    /// <summary>
    /// Parse a global source (SOUR record) NOTE written in the FTM export
    /// convention: the first line is the pre-formatted bibliographic citation,
    /// optionally followed by comment lines and/or directive lines made up
    /// entirely of "Key: Value|" markers, and closed by a lone "." terminator
    /// line.  Returns the note text with directive lines and the terminator
    /// removed.
    ///
    /// NOCITATION and SHORTCITATION directives are honored wherever they
    /// appear as a marker line.  INLINE is deliberately ignored here: it never
    /// took effect in the original VB pipeline (the "Personal note" source
    /// @S00257@ carries a record-level INLINE marker, yet its 2,261 citations
    /// have always rendered as numbered footnotes), and honoring it now would
    /// silently turn those footnotes into body prose.
    /// </summary>
    public static string ParseSourceNote(string text,
        out bool noCitation, out string shortCitation)
    {
        noCitation    = false;
        shortCitation = "";

        var kept = new List<string>();
        foreach (string rawLine in text.Split('\n'))
        {
            string line = rawLine;
            bool inlineIgnored = false;   // INLINE is inert in source notes — see doc comment
            while (TryStripLeadingMarker(ref line, out string key, out string value))
                Apply(key, value, ref inlineIgnored, ref noCitation, ref shortCitation);

            if (line.Trim(' ').Length > 0)
                kept.Add(line);
        }

        // FTM closes the note payload with a lone "." line — not content.
        if (kept.Count > 0 && kept[^1].Trim(' ') == ".")
            kept.RemoveAt(kept.Count - 1);

        return string.Join("\n", kept);
    }

    /// <summary>
    /// If <paramref name="text"/> begins with a recognized "Key: Value|"
    /// marker, remove it and report the key (upper-cased) and value.
    /// </summary>
    static bool TryStripLeadingMarker(ref string text, out string key, out string value)
    {
        key   = "";
        value = "";

        int p = text.IndexOf('|');
        if (p < 0) return false;

        string before = text[..p];
        int colon = before.IndexOf(':');
        if (colon < 0) return false;

        string k = before[..colon].Trim(' ').ToUpperInvariant();
        if (k is not ("INLINE" or "NOCITATION" or "SHORTCITATION")) return false;

        key   = k;
        value = before[(colon + 1)..].Trim(' ');
        text  = text[(p + 1)..];
        return true;
    }

    static void Apply(string key, string value,
        ref bool isInlineable, ref bool noCitation, ref string shortCitation)
    {
        switch (key)
        {
            case "SHORTCITATION": shortCitation = value; break;
            case "NOCITATION":    noCitation    = value.Equals("TRUE", StringComparison.OrdinalIgnoreCase); break;
            case "INLINE":        isInlineable  = value.Equals("TRUE", StringComparison.OrdinalIgnoreCase); break;
        }
    }
}
