namespace GedCore.Parsing;

/// <summary>
/// Shared parsing infrastructure used by both Ged55Parser and Ged70Parser.
/// </summary>
internal static class GedCommonParser
{
    static readonly System.Buffers.SearchValues<char> LineBreaks = System.Buffers.SearchValues.Create("\r\n");

    /// <summary>
    /// Split a decoded GEDCOM text into GedRecord objects (no tree structure yet).
    /// <paramref name="mapTag"/> is called on every parsed tag so callers can remap
    /// version-specific tags (e.g. Ged70Parser maps "_CONC" → "CONC").
    /// </summary>
    internal static IEnumerable<GedRecord> SplitLines(string text,
                                                       Func<string, string>? mapTag = null)
    {
        int start      = 0;
        int len        = text.Length;
        int lineNumber = 0;

        while (start < len)
        {
            // One search for either break: searching for each separately rescans to the end of a file that lacks one.
            int end = text.AsSpan(start).IndexOfAny(LineBreaks);
            if (end >= 0) end += start;
            int next;
            if (end < 0)
            {
                end  = len;
                next = len;
            }
            else
            {
                next = text[end] == '\r' && end + 1 < len && text[end + 1] == '\n' ? end + 2 : end + 1;  // eat CRLF pair or bare CR/LF
            }

            lineNumber++;
            int lineLen = end - start;
            if (lineLen > 0)
            {
                GedRecord rec;
                try
                {
                    rec = GedRecord.ParseLine(text.AsSpan(start, lineLen));
                }
                catch (Exception ex) when (ex is FormatException or OverflowException)
                {
                    string content = text.Substring(start, Math.Min(lineLen, 80));
                    throw new FormatException(
                        $"GEDCOM line {lineNumber}: cannot parse '{content}'", ex);
                }
                if (mapTag is not null)
                {
                    string mapped = mapTag(rec.Tag);
                    if (!ReferenceEquals(mapped, rec.Tag))
                        rec = new GedRecord(rec.Level, rec.Xref, mapped, rec.Value);
                }
                yield return rec;
            }

            start = next;
        }
    }

    /// <summary>Build the parent-child tree from a flat sequence of GedRecords.</summary>
    internal static List<GedRecord> BuildTree(IEnumerable<GedRecord> flat)
    {
        var roots = new List<GedRecord>();
        var stack = new GedRecord?[100]; // stack[level] = most recent record at that level

        foreach (var rec in flat)
        {
            int level = rec.Level;
            if (level >= stack.Length)
                throw new FormatException(
                    $"GEDCOM record '{rec.Tag}': nesting level {level} exceeds the " +
                    $"supported maximum of {stack.Length - 1}");
            for (int i = level + 1; i < stack.Length; i++) stack[i] = null;

            if (level == 0)
            {
                roots.Add(rec);
            }
            else
            {
                var parent = stack[level - 1];
                if (parent is not null)
                {
                    parent.Children.Add(rec);
                    rec.Parent = parent;
                }
                else
                {
                    roots.Add(rec);  // orphan — shouldn't happen in a valid file
                }
            }
            stack[level] = rec;
        }
        return roots;
    }

    /// <summary>Index level-0 records by their xref ID.</summary>
    internal static Dictionary<string, GedRecord> BuildXrefIndex(List<GedRecord> roots)
    {
        var index = new Dictionary<string, GedRecord>(roots.Count, StringComparer.Ordinal);
        foreach (var rec in roots)
            if (rec.Xref is not null)
                index[rec.Xref] = rec;
        return index;
    }

    /// <summary>Assemble a GedDocument from the three components above.</summary>
    internal static GedDocument BuildDocument(string text, Func<string, string>? mapTag = null)
    {
        var records = BuildTree(SplitLines(text, mapTag));
        return new GedDocument(records) { ByXref = BuildXrefIndex(records) };
    }
}
