using System.IO.Compression;

namespace GedCore.Gedzip;

/// <summary>
/// An open GEDZIP (<c>.gdz</c>) archive: the parsed dataset plus access to its
/// bundled media entries. Archive paths are stored exactly as they appear in
/// the zip (forward-slash separated, <b>not</b> percent-escaped — conversion
/// to/from the percent-escaped <c>FILE</c> payload form happens at the
/// <see cref="Apply.MediaPaths"/> boundary, not here).
///
/// Owns the underlying <see cref="ZipArchive"/>; dispose to release the file.
/// </summary>
public sealed class GedzipPackage : IDisposable
{
    private readonly ZipArchive _zip;

    /// <summary>The dataset parsed from the archive's <c>gedcom.ged</c> entry.</summary>
    public GedDocument Document { get; }

    /// <summary>Archive paths of every entry other than <c>gedcom.ged</c>, in archive order.</summary>
    public IReadOnlyList<string> MediaPaths { get; }

    internal GedzipPackage(ZipArchive zip, GedDocument document, IReadOnlyList<string> mediaPaths)
    {
        _zip = zip;
        Document = document;
        MediaPaths = mediaPaths;
    }

    /// <summary>
    /// Extract every media entry into <paramref name="destinationDir"/>,
    /// preserving each entry's relative path. Rejects (throws) any entry
    /// whose path would resolve outside <paramref name="destinationDir"/>
    /// (zip-slip) before extracting anything from that entry.
    /// </summary>
    public void ExtractMedia(string destinationDir)
    {
        string destRoot = Path.GetFullPath(destinationDir);

        foreach (string archivePath in MediaPaths)
        {
            string destPath = ResolveDestination(destRoot, archivePath);
            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);

            var entry = _zip.GetEntry(archivePath)
                ?? throw new FileNotFoundException($"media entry missing from archive: {archivePath}");
            entry.ExtractToFile(destPath, overwrite: true);
        }
    }

    /// <summary>Open a stream over one media entry's bytes. Caller disposes the stream.</summary>
    public Stream OpenMedia(string archivePath)
    {
        var entry = _zip.GetEntry(archivePath)
            ?? throw new FileNotFoundException($"media entry not found in archive: {archivePath}");
        return entry.Open();
    }

    /// <summary>
    /// Resolve an archive-relative path against a destination root, rejecting
    /// any path that would escape it (OWASP path-traversal / zip-slip guard).
    /// </summary>
    private static string ResolveDestination(string destRoot, string archivePath)
    {
        string relativeFs = archivePath.Replace('/', Path.DirectorySeparatorChar);
        string destPath = Path.GetFullPath(Path.Combine(destRoot, relativeFs));

        bool escapes = destPath != destRoot &&
            !destPath.StartsWith(destRoot + Path.DirectorySeparatorChar, StringComparison.Ordinal);
        if (escapes)
            throw new IOException($"zip-slip: archive entry escapes destination directory: {archivePath}");

        return destPath;
    }

    public void Dispose() => _zip.Dispose();
}
