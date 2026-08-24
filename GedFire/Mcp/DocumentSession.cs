using GedCore;
using GedFire.Gen;

namespace GedFire.Mcp;

// ---------------------------------------------------------------------------
// Owns the current DocumentSnapshot and the source path; implements the
// entire reload policy: mtime+length staleness check, one per-document
// async lock, before/after metadata capture with a single retry, a patient
// retry around the file actually being open elsewhere, atomic reference
// swap, actionable errors. The only code that ever replaces the snapshot.
//
// Two different races land here, and only one of them is transient:
//   - the file is *locked* right now -- apply_changeset's own
//     ChangesetApplier.Run holds one exclusive (FileShare.None) handle for
//     its whole read-validate-write-verify cycle, and many external editors
//     briefly lock a file while saving. This is expected, says nothing
//     about the file's eventual contents, and is worth waiting out (see
//     ReadAndParseAsync's retry budget) rather than surfacing a spurious
//     "failed to parse" for a file that will read fine a moment later.
//   - the file's *metadata moved* between an otherwise-successful read's
//     before and after snapshots -- a writer using shared access modified
//     it mid-read. That earns the existing single whole-read retry below;
//     it is a different failure mode from a lock and is not more patient
//     about it, since a second full read is comparatively expensive.
// A parse failure that is neither of those (genuinely malformed content) is
// still terminal immediately, on the first attempt, exactly as before.
//
// When enforcePrivacy is set, every reload runs the model through the same
// PrivacyFilter `gedfire generate` applies before publishing a site: RESN
// CONFIDENTIAL/PRIVACY individuals and plausibly-living ones collapse to a
// placeholder before any tool -- and the MatchIndex built from it -- ever
// sees the real data. The caller applies the filter to the *initial*
// snapshot itself before construction; this class only owns it from the
// first reload onward.
// ---------------------------------------------------------------------------

public sealed class DocumentSession
{
    // How long ReadAndParseAsync will keep retrying a locked file before
    // giving up: comfortably longer than any apply_changeset run on a
    // realistic GEDCOM should ever take, short enough that a genuinely
    // stuck lock still surfaces a bounded, actionable failure rather than
    // hanging a tool call indefinitely.
    static readonly TimeSpan LockRetryBudget = TimeSpan.FromSeconds(5);
    static readonly TimeSpan LockRetryInterval = TimeSpan.FromMilliseconds(100);

    readonly string _path;
    readonly bool _enforcePrivacy;
    readonly SemaphoreSlim _lock = new(1, 1);
    DocumentSnapshot _snapshot;

    public DocumentSession(string path, DocumentSnapshot initialSnapshot, bool enforcePrivacy = false)
    {
        if (string.IsNullOrEmpty(path)) throw new ArgumentException("Path must not be empty.", nameof(path));
        _path = path;
        _enforcePrivacy = enforcePrivacy;
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
        // Two whole-read passes at most: metadata instability between an
        // otherwise-successful read's before and after snapshot is the only
        // condition that earns this retry (a writer using shared access was
        // active). Being locked out entirely is a different, more patient
        // retry inside ReadAndParseAsync itself.
        for (int attempt = 0; attempt < 2; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var before = ReadMetadata();

            var (model, gedVersion) = await ReadAndParseAsync(cancellationToken).ConfigureAwait(false);

            var after = ReadMetadata();
            if (IsSameMetadata(before, after))
            {
                if (_enforcePrivacy)
                    PrivacyFilter.Apply(model, DateTime.UtcNow.Year);
                return new DocumentSnapshot(model, gedVersion, after.LastWriteTimeUtc, after.Length);
            }

            // The file's metadata moved while it was being read; a writer
            // was active. Retry once before giving up.
        }

        throw new DocumentReloadException(
            $"'{_path}' kept changing while being read; try the request again.");
    }

    /// <summary>
    /// Read and parse the bound file, retrying patiently (up to
    /// <see cref="LockRetryBudget"/>) while it is held open elsewhere --
    /// e.g. apply_changeset's own exclusive lock on this same path, or an
    /// external editor's brief lock while saving -- and failing immediately,
    /// on the first attempt, for any other error (a genuinely malformed
    /// file is never going to parse no matter how long this waits).
    /// </summary>
    async Task<(GedModel Model, string? GedVersion)> ReadAndParseAsync(CancellationToken cancellationToken)
    {
        DateTime deadline = DateTime.UtcNow + LockRetryBudget;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return await Task.Run(() =>
                {
                    var doc = GedReader.ReadFile(_path);
                    return (ModelBuilder.Build(doc), doc.Version);
                }, cancellationToken).ConfigureAwait(false);
            }
            catch (IOException) when (DateTime.UtcNow + LockRetryInterval < deadline)
            {
                // Retried below; only the final failure (if the budget runs
                // out) is ever reported.
                await Task.Delay(LockRetryInterval, cancellationToken).ConfigureAwait(false);
            }
            catch (IOException ex)
            {
                throw new DocumentReloadException(
                    $"'{_path}' is locked by another process and did not become available within " +
                    $"{LockRetryBudget.TotalSeconds:0}s: {ex.Message}", ex);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                throw new DocumentReloadException($"Failed to parse '{_path}': {ex.Message}", ex);
            }
        }
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
