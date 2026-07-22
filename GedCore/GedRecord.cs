namespace GedCore;

/// <summary>
/// One record (node) in the GEDCOM tree.  This is the version-agnostic model:
/// parsers (Ged55Parser, Ged70Parser) produce it; formatters (Ged55Formatter,
/// Ged70Formatter) consume it.
///
/// CONC and CONT lines are preserved as child records so that the 5.5 formatter
/// can reproduce byte-identical output.  Call FullValue() to get the logical
/// concatenated text when the split position doesn't matter.
/// </summary>
public sealed class GedRecord
{
    public int Level { get; }
    public string? Xref { get; }   // record ID, e.g. "@I00001@"; null if not present
    public string Tag { get; private set; }
    public string Value { get; private set; }   // value exactly as it appeared on the source line

    public GedRecord? Parent { get; internal set; }
    public List<GedRecord> Children { get; } = [];

    internal GedRecord(int level, string? xref, string tag, string value)
    {
        Level = level;
        Xref  = xref;
        Tag   = tag;
        Value = value;
    }

    /// <summary>
    /// Encode a logical text payload for the file: GEDCOM 7 requires a payload
    /// whose first character is '@' to be written as '@@' so a reader never
    /// mistakes it for the start of a pointer. Idempotent: a value already
    /// shaped like an escape ('@@…') is left alone.
    /// </summary>
    public static string EscapeAtSign(string text) =>
        text.Length > 0 && text[0] == '@' && !(text.Length > 1 && text[1] == '@')
            ? "@" + text
            : text;

    /// <summary>Reverse of <see cref="EscapeAtSign"/>: strip one leading '@' from an '@@…' payload.</summary>
    public static string UnescapeAtSign(string text) =>
        text.Length > 1 && text[0] == '@' && text[1] == '@'
            ? text[1..]
            : text;

    /// <summary>
    /// The reserved GEDCOM 7 pointer meaning "no record exists in this
    /// document" (§ VOID_POINTER). It is never resolvable and no record may
    /// be created with this xref.
    /// </summary>
    public const string VoidPointer = "@VOID@";

    /// <summary>True when this record's <see cref="Value"/> is the reserved <see cref="VoidPointer"/>.</summary>
    public bool IsVoidPointer => Value == VoidPointer;

    // -------------------------------------------------------------------------
    // Line-level parsing and formatting (the GEDCOM line grammar is the same
    // across versions 5.5 and 7.0, so these live on the shared model class).
    // -------------------------------------------------------------------------

    /// <summary>Parse one raw GEDCOM line (no line terminator).</summary>
    internal static GedRecord ParseLine(ReadOnlySpan<char> raw)
    {
        int pos = 0;

        // level digits
        int lvStart = pos;
        while (pos < raw.Length && raw[pos] >= '0' && raw[pos] <= '9') pos++;
        int level = int.Parse(raw[lvStart..pos]);

        // mandatory space
        if (pos < raw.Length && raw[pos] == ' ') pos++;

        // optional xref_ID: "@word@" before the tag
        string? xref = null;
        if (pos < raw.Length && raw[pos] == '@')
        {
            int xStart = pos;
            pos++;                                            // skip opening '@'
            while (pos < raw.Length && raw[pos] != '@') pos++;
            if (pos < raw.Length) pos++;                     // skip closing '@'
            xref = new string(raw[xStart..pos]);
            if (pos < raw.Length && raw[pos] == ' ') pos++;  // skip separator
        }

        // tag
        int tStart = pos;
        while (pos < raw.Length && raw[pos] != ' ') pos++;
        string tag = new string(raw[tStart..pos]);

        // optional " value" — preserve exactly, including any trailing spaces
        string value = "";
        if (pos < raw.Length && raw[pos] == ' ')
        {
            pos++;
            value = new string(raw[pos..]);
        }

        return new GedRecord(level, xref, tag, value);
    }

    /// <summary>Format this record as a single GEDCOM line (no line terminator).</summary>
    public string FormatLine() => FormatLine(null, null);

    /// <summary>
    /// Format as a GEDCOM line, optionally overriding the tag and/or value.
    /// Used by versioned formatters that need to rewrite specific header fields
    /// (e.g. VERS value, CHAR value, or CONC → _CONC tag mapping).
    /// </summary>
    public string FormatLine(string? tagOverride, string? valueOverride)
    {
        string t = tagOverride   ?? Tag;
        string v = valueOverride ?? Value;
        if (Xref is not null)
            return v.Length > 0 ? $"{Level} {Xref} {t} {v}" : $"{Level} {Xref} {t}";
        return v.Length > 0 ? $"{Level} {t} {v}" : $"{Level} {t}";
    }

    /// <summary>
    /// Append text to this record's value.  Used by upgrade transforms that
    /// fold CONC continuation lines into the line they continue.
    /// </summary>
    internal void AppendValue(string text) => Value += text;

    /// <summary>
    /// Rename this record's tag in place.  Used by upgrade transforms that
    /// re-tag structures whose tag changed between versions (NOTE → SNOTE).
    /// </summary>
    internal void Retag(string tag) => Tag = tag;

    /// <summary>
    /// Replace this record's value.  Used by upgrade transforms that rewrite
    /// payloads (a free-text source citation becoming a pointer).
    /// </summary>
    internal void SetValue(string value) => Value = value;

    // -------------------------------------------------------------------------
    // Semantic helpers used by the generator
    // -------------------------------------------------------------------------

    /// <summary>The logical, un-escaped single-line payload (see <see cref="UnescapeAtSign"/>). <see cref="Value"/> itself stays the raw on-disk payload.</summary>
    public string PayloadValue => UnescapeAtSign(Value);

    /// <summary>
    /// True when this record's raw <see cref="Value"/> has the shape of a
    /// pointer ("@XREF@") rather than a plain or escaped text payload. An
    /// escaped text payload ("@@…") is never mistaken for a pointer here.
    /// </summary>
    public bool IsPointerValue =>
        Value.Length > 2 && Value[0] == '@' && Value[1] != '@' && Value[^1] == '@';

    /// <summary>
    /// Logical text value: folds any CONC/CONT direct children into the value.
    /// CONC appends directly; CONT appends with a newline. The head line is
    /// un-escaped; CONT/CONC continuation lines are never escaped mid-payload.
    /// </summary>
    public string FullValue()
    {
        if (Children.Count == 0) return PayloadValue;
        var sb = new System.Text.StringBuilder(PayloadValue);
        foreach (var child in Children)
        {
            if (child.Tag == "CONC")      sb.Append(child.Value);
            else if (child.Tag == "CONT") sb.Append('\n').Append(child.Value);
        }
        return sb.ToString();
    }

    /// <summary>Return all immediate children with the given tag.</summary>
    public IEnumerable<GedRecord> ChildrenByTag(string tag) =>
        Children.Where(c => c.Tag == tag);

    /// <summary>Return the first immediate child with the given tag, or null.</summary>
    public GedRecord? FirstChild(string tag) =>
        Children.FirstOrDefault(c => c.Tag == tag);
}
