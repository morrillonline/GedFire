using GedCore;

namespace GedFire.Cli;

// ---------------------------------------------------------------------------
// The writers a command reports through, plus the reporting steps every
// command shares. Commands write here, never to Console, so they can run
// in-process against string writers.
// ---------------------------------------------------------------------------

public sealed class CommandContext(TextWriter output, TextWriter error)
{
    public TextWriter Out { get; } = output;
    public TextWriter Error { get; } = error;

    /// <summary>Writes <paramref name="message"/> to the error writer and returns the failure exit code.</summary>
    public int Fail(string message)
    {
        Error.WriteLine(message);
        return 1;
    }

    /// <summary>Reports and returns true when <paramref name="path"/> is not an existing file.</summary>
    public bool FileMissing(string path, string label = "Input file")
    {
        if (File.Exists(path)) return false;
        Fail($"{label} not found: {path}");
        return true;
    }

    /// <summary>Reads a GEDCOM file, reporting the path and the number of level-0 records.</summary>
    public GedDocument ReadDocument(string path, Func<string, GedDocument>? reader = null)
    {
        Out.WriteLine($"Reading  {path}");
        var document = (reader ?? GedReader.ReadFile)(path);
        Out.WriteLine($"  {document.Records.Count:N0} level-0 records");
        return document;
    }
}
