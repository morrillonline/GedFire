using GedCore.Ged70;
using GedCore.Gedzip;

namespace GedCore.Tests;

/// <summary>
/// GEDZIP (.gdz) round-trip, absolute-URL passthrough, missing-file, missing
/// gedcom.ged, and zip-slip guard coverage for Subproject K.
/// </summary>
public sealed class GedzipTests : IDisposable
{
    private readonly string _mediaDir;
    private readonly string _workDir;

    // One relative FILE with a percent-escaped space (exercises escaping),
    // one absolute-URL FILE (never bundled).
    private const string Ged = """
        0 @M1@ OBJE
        1 FILE photos/husband.jpg
        2 FORM image/jpeg
        1 FILE documents/will%20with%20spaces.pdf
        2 FORM application/pdf
        0 @M2@ OBJE
        1 FILE https://example.com/photo.jpg
        2 FORM image/jpeg
        """;

    public GedzipTests()
    {
        _mediaDir = Path.Combine(Path.GetTempPath(), "gedfire-gedzip-media-" + Path.GetRandomFileName());
        _workDir = Path.Combine(Path.GetTempPath(), "gedfire-gedzip-work-" + Path.GetRandomFileName());
        Directory.CreateDirectory(_mediaDir);
        Directory.CreateDirectory(_workDir);
    }

    public void Dispose()
    {
        Directory.Delete(_mediaDir, recursive: true);
        Directory.Delete(_workDir, recursive: true);
    }

    private void WriteMediaFile(string relativePath, string content)
    {
        string full = Path.Combine(_mediaDir, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
    }

    [Fact]
    public void RoundTrip_BundlesRelativeFiles_SkipsAbsoluteUrl_ExtractedBytesMatch()
    {
        WriteMediaFile("photos/husband.jpg", "jpeg-bytes");
        WriteMediaFile("documents/will with spaces.pdf", "pdf-bytes");

        var doc = Ged70Parser.Parse(Ged);
        string gdzPath = Path.Combine(_workDir, "family.gdz");
        GedzipWriter.Write(doc, _mediaDir, gdzPath);

        using var package = GedzipReader.Open(gdzPath);

        Assert.Equal(2, package.MediaPaths.Count);
        Assert.Contains("photos/husband.jpg", package.MediaPaths);
        Assert.Contains("documents/will with spaces.pdf", package.MediaPaths);
        Assert.DoesNotContain(package.MediaPaths, p => p.Contains("example.com"));

        // The re-parsed document formats byte-identically to the original.
        using var originalBytes = new MemoryStream();
        Ged70Formatter.Write(doc, originalBytes);
        using var reparsedBytes = new MemoryStream();
        Ged70Formatter.Write(package.Document, reparsedBytes);
        Assert.Equal(originalBytes.ToArray(), reparsedBytes.ToArray());

        string extractDir = Path.Combine(_workDir, "extracted");
        package.ExtractMedia(extractDir);
        Assert.Equal("jpeg-bytes", File.ReadAllText(Path.Combine(extractDir, "photos", "husband.jpg")));
        Assert.Equal("pdf-bytes", File.ReadAllText(Path.Combine(extractDir, "documents", "will with spaces.pdf")));

        using var stream = package.OpenMedia("photos/husband.jpg");
        using var reader = new StreamReader(stream);
        Assert.Equal("jpeg-bytes", reader.ReadToEnd());
    }

    [Fact]
    public void MissingReferencedFile_ThrowsNamingXrefAndPath()
    {
        WriteMediaFile("photos/husband.jpg", "jpeg-bytes");
        // "documents/will with spaces.pdf" deliberately not written.

        var doc = Ged70Parser.Parse(Ged);
        string gdzPath = Path.Combine(_workDir, "family.gdz");

        var ex = Assert.Throws<FileNotFoundException>(() => GedzipWriter.Write(doc, _mediaDir, gdzPath));
        Assert.Contains("@M1@", ex.Message);
        Assert.Contains("documents/will with spaces.pdf", ex.Message);
    }

    [Fact]
    public void ArchiveWithoutGedcomEntry_ThrowsFormatException()
    {
        string gdzPath = Path.Combine(_workDir, "not-a-gedzip.gdz");
        using (var zip = System.IO.Compression.ZipFile.Open(gdzPath, System.IO.Compression.ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry("readme.txt");
            using var stream = entry.Open();
            using var writer = new StreamWriter(stream);
            writer.Write("not a gedcom archive");
        }

        Assert.Throws<FormatException>(() => GedzipReader.Open(gdzPath));
    }

    [Fact]
    public void ZipSlip_ExtractMedia_ThrowsAndWritesNothingOutsideDestination()
    {
        string gdzPath = Path.Combine(_workDir, "malicious.gdz");
        using (var zip = System.IO.Compression.ZipFile.Open(gdzPath, System.IO.Compression.ZipArchiveMode.Create))
        {
            var gedEntry = zip.CreateEntry("gedcom.ged");
            using (var stream = gedEntry.Open())
            using (var writer = new StreamWriter(stream))
                writer.Write("0 HEAD\n0 TRLR\n");

            var evilEntry = zip.CreateEntry("../evil.txt");
            using (var stream = evilEntry.Open())
            using (var writer = new StreamWriter(stream))
                writer.Write("escaped!");
        }

        string destDir = Path.Combine(_workDir, "extract-dest");
        using var package = GedzipReader.Open(gdzPath);

        Assert.Throws<IOException>(() => package.ExtractMedia(destDir));
        Assert.False(File.Exists(Path.Combine(_workDir, "evil.txt")));
    }

    [Fact]
    public void DirectoryEntries_AreNotMedia_AndDoNotBreakExtract()
    {
        // Some zip tools write explicit folder entries; they are bookkeeping,
        // not extractable media.
        string gdzPath = Path.Combine(_workDir, "with-folder-entry.gdz");
        using (var zip = System.IO.Compression.ZipFile.Open(gdzPath, System.IO.Compression.ZipArchiveMode.Create))
        {
            var gedEntry = zip.CreateEntry("gedcom.ged");
            using (var stream = gedEntry.Open())
            using (var writer = new StreamWriter(stream))
                writer.Write("0 HEAD\n0 TRLR\n");

            zip.CreateEntry("photos/");
            var photo = zip.CreateEntry("photos/him.jpg");
            using (var stream = photo.Open())
            using (var writer = new StreamWriter(stream))
                writer.Write("jpeg-bytes");
        }

        using var package = GedzipReader.Open(gdzPath);
        Assert.Equal(["photos/him.jpg"], package.MediaPaths);

        string destDir = Path.Combine(_workDir, "folder-entry-dest");
        package.ExtractMedia(destDir);
        Assert.True(File.Exists(Path.Combine(destDir, "photos", "him.jpg")));
    }
}
