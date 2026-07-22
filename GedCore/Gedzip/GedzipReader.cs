using System.IO.Compression;

namespace GedCore.Gedzip;

/// <summary>
/// Opens a GEDZIP (<c>.gdz</c>) archive: a ZIP containing the dataset as
/// <c>gedcom.ged</c> at the root plus every file named by a relative
/// <c>FILE</c> payload, stored at its unescaped relative path.
/// </summary>
public static class GedzipReader
{
    private const string GedcomEntryName = "gedcom.ged";

    /// <summary>
    /// Open <paramref name="path"/> as a GEDZIP archive. Throws
    /// <see cref="FormatException"/> when the archive has no
    /// <c>gedcom.ged</c> entry.
    /// </summary>
    public static GedzipPackage Open(string path)
    {
        var zip = ZipFile.OpenRead(path);
        try
        {
            var gedEntry = zip.GetEntry(GedcomEntryName)
                ?? throw new FormatException($"not a GEDZIP: no {GedcomEntryName} entry in {path}");

            byte[] gedBytes;
            using (var entryStream = gedEntry.Open())
            using (var buffer = new MemoryStream())
            {
                entryStream.CopyTo(buffer);
                gedBytes = buffer.ToArray();
            }

            var document = GedReader.Read(gedBytes);

            // Directory entries (FullName ending '/') are zip bookkeeping,
            // not media — tools that add explicit folder entries would
            // otherwise make ExtractMedia try to extract a directory.
            var mediaPaths = zip.Entries
                .Where(e => e.FullName != GedcomEntryName && !e.FullName.EndsWith('/'))
                .Select(e => e.FullName)
                .ToList();

            return new GedzipPackage(zip, document, mediaPaths);
        }
        catch
        {
            zip.Dispose();
            throw;
        }
    }
}
