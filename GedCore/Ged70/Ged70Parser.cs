using System.Text;
using GedCore.Parsing;

namespace GedCore.Ged70;

/// <summary>
/// Parses a GEDCOM 7.0 file or stream into a version-agnostic GedDocument.
///
/// Encoding: 7.0 mandates UTF-8 (with optional BOM); .NET's StreamReader
/// strips the BOM automatically when detectEncodingFromByteOrderMarks is true.
///
/// Tag mapping: the _CONC local extension tag written by Ged70Formatter is
/// mapped back to the model tag CONC so that Ged55Formatter can reconstruct
/// byte-identical 5.5 output.
/// </summary>
public static class Ged70Parser
{
    // -------------------------------------------------------------------------
    // Public API — file convenience wrapper at the top, stream core below
    // -------------------------------------------------------------------------

    /// <summary>Open a file and parse it as GEDCOM 7.0.</summary>
    public static GedDocument ReadFile(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read,
                                      FileShare.Read, bufferSize: 65536);
        return Read(fs);
    }

    /// <summary>
    /// Parse a GEDCOM 7.0 stream.  UTF-8 BOM is handled automatically.
    /// The caller retains ownership of <paramref name="stream"/>.
    /// </summary>
    public static GedDocument Read(Stream stream)
    {
        // detectEncodingFromByteOrderMarks: true → strips the optional UTF-8 BOM
        using var reader = new StreamReader(stream, Encoding.UTF8,
                                            detectEncodingFromByteOrderMarks: true,
                                            bufferSize: 65536, leaveOpen: true);
        return Parse(reader.ReadToEnd());
    }

    /// <summary>Parse a GEDCOM 7.0 document from an already-decoded string.</summary>
    public static GedDocument Parse(string text) =>
        GedCommonParser.BuildDocument(text, MapTag);

    // -------------------------------------------------------------------------

    /// <summary>
    /// Remap tags written by Ged70Formatter back to their model equivalents.
    ///   _CONC → CONC  (local extension used to preserve 5.5 line-split positions)
    /// All other tags are returned unchanged.
    /// </summary>
    private static string MapTag(string tag) => tag switch
    {
        "_CONC" => "CONC",
        _       => tag,
    };
}
