using System.Text;
using GedCore.Parsing;

namespace GedCore.Ged55;

/// <summary>Parses a GEDCOM 5.5 / 5.5.1 file or stream into a version-agnostic GedDocument.</summary>
public static class Ged55Parser
{
    // -------------------------------------------------------------------------
    // Public API — file convenience wrapper at the top, stream core below
    // -------------------------------------------------------------------------

    /// <summary>Open a file and parse it as GEDCOM 5.5.</summary>
    public static GedDocument ReadFile(string path)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read,
                                      FileShare.Read, bufferSize: 65536);
        return Read(fs);
    }

    /// <summary>
    /// Parse a GEDCOM 5.5 stream.  If <paramref name="encoding"/> is null the
    /// encoding is auto-detected from the CHAR tag in the header, defaulting to
    /// Windows-1252 (ANSI) when absent or unrecognised.
    /// The caller retains ownership of <paramref name="stream"/>.
    /// </summary>
    public static GedDocument Read(Stream stream, Encoding? encoding = null)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var bytes = ReadFully(stream);
        encoding ??= DetectEncoding(bytes);
        int bomLength = ByteOrderMarkLength(bytes);
        return Parse(encoding.GetString(bytes, bomLength, bytes.Length - bomLength));
    }

    /// <summary>Parse a GEDCOM 5.5 document from an already-decoded string.</summary>
    public static GedDocument Parse(string text) =>
        GedCommonParser.BuildDocument(text);   // no tag remapping for 5.5

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private static byte[] ReadFully(Stream stream)
    {
        using var buf = new MemoryStream();
        stream.CopyTo(buf);
        return buf.ToArray();
    }

    /// <summary>Length of any leading byte-order mark, so decoding skips it.</summary>
    private static int ByteOrderMarkLength(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return 3;
        if (bytes.Length >= 2 &&
            ((bytes[0] == 0xFF && bytes[1] == 0xFE) || (bytes[0] == 0xFE && bytes[1] == 0xFF)))
            return 2;
        return 0;
    }

    private static Encoding DetectEncoding(byte[] bytes)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

        // A byte-order mark is decisive regardless of the CHAR tag.
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8;
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return Encoding.Unicode;
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return Encoding.BigEndianUnicode;

        // Peek at the header as ANSI to locate the CHAR tag. 2048 bytes leaves
        // room for headers with long NOTE/SOUR structures before CHAR.
        int previewLen = Math.Min(2048, bytes.Length);
        var preview = Encoding.GetEncoding(1252).GetString(bytes, 0, previewLen);

        foreach (var line in preview.Split('\n'))
        {
            var trimmed = line.TrimEnd('\r');
            if (trimmed.StartsWith("1 CHAR ", StringComparison.Ordinal))
            {
                var charValue = trimmed["1 CHAR ".Length..].Trim().ToUpperInvariant();
                return charValue switch
                {
                    "ANSI"    => Encoding.GetEncoding(1252),
                    "ASCII"   => Encoding.ASCII,
                    "UTF-8"   => Encoding.UTF8,
                    "UNICODE" => Encoding.Unicode,
                    _         => Encoding.GetEncoding(1252),
                };
            }
        }
        return Encoding.GetEncoding(1252);
    }
}
