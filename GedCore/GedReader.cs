using System.Text;
using GedCore.Ged55;
using GedCore.Ged70;

namespace GedCore;

/// <summary>
/// Version-detecting GEDCOM reader: inspects HEAD.GEDC.VERS and dispatches to
/// Ged70Parser for 7.x files, Ged55Parser for everything else (5.5 / 5.5.1).
/// </summary>
public static class GedReader
{
    static readonly TimeSpan BusyWait = TimeSpan.FromSeconds(1);
    static readonly TimeSpan BusyRetry = TimeSpan.FromMilliseconds(20);

    /// <summary>Read a GEDCOM file of either supported version.</summary>
    public static GedDocument ReadFile(string path) => Read(ReadAllBytes(path));

    /// <summary>
    /// Reads a GEDCOM file's bytes, waiting out the moment another program has it locked, such as a writer
    /// putting a new version in place. A file still locked after the wait throws <see cref="IOException"/>.
    /// </summary>
    public static byte[] ReadAllBytes(string path)
    {
        DateTime deadline = DateTime.UtcNow + BusyWait;
        while (true)
        {
            try
            {
                return File.ReadAllBytes(path);
            }
            catch (IOException ex) when (IsLocked(ex) && DateTime.UtcNow < deadline)
            {
                Thread.Sleep(BusyRetry);
            }
        }
    }

    static bool IsLocked(IOException ex) =>
        ex is not (FileNotFoundException or DirectoryNotFoundException or PathTooLongException or DriveNotFoundException);

    /// <summary>Parse GEDCOM bytes of either supported version.</summary>
    public static GedDocument Read(byte[] bytes) =>
        IsGed70(bytes) ? Ged70Parser.Read(new MemoryStream(bytes))
                       : Ged55Parser.Read(new MemoryStream(bytes));

    /// <summary>
    /// Detect GEDCOM 7.x by finding "2 VERS 7…" after "1 GEDC" in the header.
    /// The header tags are ASCII in every supported encoding (ANSI and UTF-8
    /// are both ASCII-compatible), so decoding the leading bytes as ASCII is
    /// safe for detection regardless of the file's actual encoding.
    /// </summary>
    private static bool IsGed70(byte[] bytes)
    {
        string head = Encoding.ASCII.GetString(bytes, 0, Math.Min(bytes.Length, 2048));

        int gedc = head.IndexOf("1 GEDC", StringComparison.Ordinal);
        if (gedc < 0) return false;

        int vers = head.IndexOf("2 VERS ", gedc, StringComparison.Ordinal);
        if (vers < 0) return false;

        int versionStart = vers + "2 VERS ".Length;
        return versionStart < head.Length && head[versionStart] == '7';
    }
}
