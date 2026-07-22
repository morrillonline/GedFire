using System.IO.Compression;
using GedCore.Apply;
using GedCore.Ged70;

namespace GedCore.Gedzip;

/// <summary>
/// Writes a GEDZIP (<c>.gdz</c>) archive: <c>gedcom.ged</c> (GEDCOM 7.0 format)
/// at the root plus every file named by a relative <c>FILE</c> payload,
/// resolved against <c>mediaDir</c> and stored at its unescaped relative path.
/// Absolute-URL payloads are never bundled (nothing to fetch or store).
/// </summary>
public static class GedzipWriter
{
    private const string GedcomEntryName = "gedcom.ged";

    /// <summary>
    /// Write <paramref name="doc"/> and its referenced media to
    /// <paramref name="gdzPath"/>, overwriting any existing file there.
    /// Throws <see cref="FileNotFoundException"/> naming the OBJE xref and
    /// path when a referenced relative-path file is missing from
    /// <paramref name="mediaDir"/>.
    /// </summary>
    public static void Write(GedDocument doc, string mediaDir, string gdzPath)
    {
        string mediaRoot = Path.GetFullPath(mediaDir);

        if (File.Exists(gdzPath))
            File.Delete(gdzPath);

        using var zip = ZipFile.Open(gdzPath, ZipArchiveMode.Create);

        var gedEntry = zip.CreateEntry(GedcomEntryName);
        using (var entryStream = gedEntry.Open())
            Ged70Formatter.Write(doc, entryStream);

        var written = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rec in doc.Records.Where(r => r.Tag == "OBJE"))
        {
            foreach (var file in rec.ChildrenByTag("FILE"))
            {
                string payload = file.FullValue();
                if (MediaPaths.IsAbsoluteUrl(payload)) continue;

                string archivePath = MediaPaths.UnescapeFilePath(payload);
                if (!written.Add(archivePath)) continue; // already bundled

                string sourcePath = Path.Combine(mediaRoot, archivePath.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(sourcePath))
                    throw new FileNotFoundException(
                        $"OBJE {rec.Xref}: media file missing for {archivePath}", sourcePath);

                var mediaEntry = zip.CreateEntry(archivePath);
                using var mediaEntryStream = mediaEntry.Open();
                using var sourceStream = File.OpenRead(sourcePath);
                sourceStream.CopyTo(mediaEntryStream);
            }
        }
    }
}
