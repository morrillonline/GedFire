using System.Text;

namespace GedCore.Ged70;

/// <summary>
/// Serializes a GedDocument to GEDCOM 7.0 format.
///
/// Encoding: UTF-8 with BOM (U+FEFF) as recommended by the 7.0 specification.
/// The BOM is transparent to round-tripping: .NET's UTF-8 decoder strips it on
/// read, so the recovered text is identical to what was written.
///
/// Key transformations applied during output (all reversible for round-trip):
///
///   CONC tag          → _CONC   (CONC removed in 7.0; _CONC is a documented
///                                local extension that preserves line-split
///                                positions so Ged55Formatter can reconstruct
///                                byte-identical 5.5 output)
///
///   HEAD/GEDC/VERS    → "7.0"  (was "5.5"; restored to "5.5" by Ged55Formatter)
///
///   HEAD/CHAR value   → "UTF-8" (CHAR is removed in the 7.0 spec; we keep the
///                                record and update its value so encoding is
///                                self-describing and Ged55Formatter can restore
///                                "ANSI" without injecting a new record)
///
/// Everything else passes through unchanged.
/// </summary>
public static class Ged70Formatter
{
    /// <summary>Write a GedDocument to a file in GEDCOM 7.0 format (UTF-8 with BOM).</summary>
    public static void WriteFile(GedDocument doc, string path)
    {
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write,
                                      FileShare.None, bufferSize: 65536);
        Write(doc, fs);
    }

    /// <summary>Write a GedDocument to any stream in GEDCOM 7.0 format.</summary>
    public static void Write(GedDocument doc, Stream stream)
    {
        // UTF-8 with BOM (encoderShouldEmitUTF8Identifier = true)
        var utf8Bom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

        using var writer = new StreamWriter(stream, utf8Bom, bufferSize: 65536,
                                            leaveOpen: true);
        writer.NewLine = "\r\n";

        foreach (var root in doc.Records)
            WriteRecord(writer, root);
    }

    private static void WriteRecord(StreamWriter writer, GedRecord rec)
    {
        // Tag override: CONC is not valid in GEDCOM 7.0; write as _CONC extension.
        string? tagOverride = rec.Tag == "CONC" ? "_CONC" : null;

        // Value overrides for header fields that change between versions.
        string? valueOverride = null;
        if      (rec.Tag == "VERS" && rec.Parent?.Tag == "GEDC") valueOverride = "7.0";
        else if (rec.Tag == "CHAR" && rec.Parent?.Tag == "HEAD") valueOverride = "UTF-8";

        writer.WriteLine(rec.FormatLine(tagOverride, valueOverride));

        foreach (var child in rec.Children)
            WriteRecord(writer, child);
    }
}
