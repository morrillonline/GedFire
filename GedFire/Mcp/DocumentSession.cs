using GedCore;
using GedFire.Gen;

namespace GedFire.Mcp;

// ---------------------------------------------------------------------------
// Owns the current DocumentSnapshot and the source path; implements the
// entire reload policy from docs/design/mcp-server.md "Resident document and
// reload policy": mtime+length staleness check, one per-document async lock,
// before/after metadata capture with a single retry, atomic reference swap,
// actionable errors. The only code that ever replaces the snapshot.
// ---------------------------------------------------------------------------

public sealed class DocumentSession
{
    readonly string _path;
    readonly SemaphoreSlim _lock = new(1, 1);
    DocumentSnapshot _snapshot;

    public DocumentSession(string path, DocumentSnapshot initialSnapshot)
    {
        if (string.IsNullOrEmpty(path)) throw new ArgumentException("Path must not be empty.", nameof(path));
        _path = path;
        _snapshot = initialSnapshot ?? throw new ArgumentNullException(nameof(initialSnapshot));
    }

    /// <summary>
    /// The current snapshot, reloading first if the source file's metadata
    /// has moved since the snapshot was taken. Concurrent callers share one
    /// reload; none ever observes a half-built model.
    /// </summary>
    public async Task<DocumentSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var current = ReadMetadata();
            if (IsSameMetadata(current, _snapshot))
                return _snapshot;

            _snapshot = await ReloadAsync(cancellationToken).ConfigureAwait(false);
            return _snapshot;
        }
        finally
        {
            _lock.Release();
        }
    }

    async Task<DocumentSnapshot> ReloadAsync(CancellationToken cancellationToken)
    {
        // Two passes at most: a parse failure is always terminal immediately:
        // metadata instability during an otherwise-successful read is the
        // only condition that earns the single retry (a writer was active).
        for (int attempt = 0; attempt < 2; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var before = ReadMetadata();

            GedModel model;
            string? gedVersion;
            try
            {
                (model, gedVersion) = await Task.Run(() =>
                {
                    var doc = GedReader.ReadFile(_path);
                    return (ModelBuilder.Build(doc), doc.Version);
                }, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new DocumentReloadException($"Failed to parse '{_path}': {ex.Message}", ex);
            }

            var after = ReadMetadata();
            if (IsSameMetadata(before, after))
                return new DocumentSnapshot(model, gedVersion, after.LastWriteTimeUtc, after.Length);

            // The file's metadata moved while it was being read; a writer
            // was active. Retry once before giving up.
        }

        throw new DocumentReloadException(
            $"'{_path}' kept changing while being read; try the request again.");
    }

    (DateTime LastWriteTimeUtc, long Length) ReadMetadata()
    {
        var info = new FileInfo(_path);
        if (!info.Exists)
            throw new DocumentReloadException($"Input file not found: {_path}");
        return (File.GetLastWriteTimeUtc(_path), info.Length);
    }

    static bool IsSameMetadata((DateTime LastWriteTimeUtc, long Length) a, DocumentSnapshot b) =>
        a.LastWriteTimeUtc == b.LastWriteTimeUtc && a.Length == b.Length;

    static bool IsSameMetadata((DateTime LastWriteTimeUtc, long Length) a, (DateTime LastWriteTimeUtc, long Length) b) =>
        a.LastWriteTimeUtc == b.LastWriteTimeUtc && a.Length == b.Length;
}
