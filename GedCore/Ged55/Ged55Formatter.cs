using System.Text;

namespace GedCore.Ged55;

/// <summary>
/// Serializes a GedDocument to GEDCOM 5.5 format (Windows-1252 / ANSI encoding, CRLF).
///
/// Header overrides: this formatter always writes the 5.5-correct values for
/// GEDC/VERS ("5.5") and HEAD/CHAR ("ANSI"), regardless of what the model
/// holds.  This makes the 5.5 formatter idempotent when the model was produced
/// by a round-trip through the 7.0 formatter.
/// </summary>
public static class Ged55Formatter
{
    /// <summary>Write a GedDocument to a file in GEDCOM 5.5 format.</summary>
    public static void WriteFile(GedDocument doc, string path)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var ansi = Encoding.GetEncoding(1252);

        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write,
                                      FileShare.None, bufferSize: 65536);
        Write(doc, fs, ansi);
    }

    /// <summary>Write a GedDocument to any stream in GEDCOM 5.5 format.</summary>
    public static void Write(GedDocument doc, Stream stream, Encoding? encoding = null)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        encoding ??= Encoding.GetEncoding(1252);

        using var writer = new StreamWriter(stream, encoding, bufferSize: 65536,
                                            leaveOpen: true);
        writer.NewLine = "\r\n";

        foreach (var root in doc.Records)
            WriteRecord(writer, root);
    }

    private static void WriteRecord(StreamWriter writer, GedRecord rec)
    {
        // Restore the 5.5-correct values for tags the 7.0 formatter rewrites.
        string? valueOverride = null;
        if (rec.Tag == "VERS" && rec.Parent?.Tag == "GEDC")
            valueOverride = "5.5";
        else if (rec.Tag == "CHAR" && rec.Parent?.Tag == "HEAD")
            valueOverride = "ANSI";

        writer.WriteLine(rec.FormatLine(null, valueOverride));

        foreach (var child in rec.Children)
            WriteRecord(writer, child);
    }
}
