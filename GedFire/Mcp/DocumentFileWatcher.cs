namespace GedFire.Mcp;

// ---------------------------------------------------------------------------
// Watches the bound GEDCOM file for on-disk changes and proactively reloads
// DocumentSession's snapshot when they settle, so a long-running `gedfire
// mcp` server picks up an external edit without waiting for the next tool
// call to trigger DocumentSession's own lazy staleness check. Debounces
// rapid-fire filesystem events (many editors emit several Changed events, or
// a delete+recreate, for one logical save) behind a quiet period before
// reloading. Status is logged to stderr, never stdout, since stdout is
// reserved for MCP JSON-RPC traffic (McpServerIntegrationTests asserts this).
// ---------------------------------------------------------------------------

public sealed class DocumentFileWatcher : IAsyncDisposable
{
    static readonly TimeSpan QuietPeriod = TimeSpan.FromMilliseconds(300);

    readonly DocumentSession _session;
    readonly FileSystemWatcher _watcher;
    readonly CancellationTokenSource _lifetime = new();
    readonly object _debounceLock = new();
    CancellationTokenSource? _debounce;

    // Incremented after every reload this watcher itself triggered (as
    // opposed to one a tool call's own lazy check triggered) — the only
    // externally observable signal that the watcher, not just
    // DocumentSession's existing pull-based check, did the work.
    public int ReloadCount { get; private set; }

    public DocumentFileWatcher(DocumentSession session, string absolutePath)
    {
        _session = session ?? throw new ArgumentNullException(nameof(session));
        if (string.IsNullOrEmpty(absolutePath)) throw new ArgumentException("Path must not be empty.", nameof(absolutePath));

        string? dir = Path.GetDirectoryName(absolutePath);
        if (string.IsNullOrEmpty(dir))
            throw new ArgumentException("Path must have a directory.", nameof(absolutePath));

        _watcher = new FileSystemWatcher(dir, Path.GetFileName(absolutePath))
        {
            NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName,
        };
        _watcher.Changed += OnFileEvent;
        _watcher.Created += OnFileEvent;
        _watcher.Renamed += OnFileEvent;
        _watcher.Error += OnError;
        _watcher.EnableRaisingEvents = true;
    }

    void OnFileEvent(object sender, FileSystemEventArgs e) => Debounce();

    void OnError(object sender, ErrorEventArgs e) =>
        Console.Error.WriteLine($"File watcher error: {e.GetException().Message}");

    // Collapses a burst of events for one logical save into a single reload:
    // each new event cancels and replaces the pending delay rather than
    // queuing another one.
    void Debounce()
    {
        lock (_debounceLock)
        {
            _debounce?.Cancel();
            _debounce?.Dispose();
            var cts = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
            _debounce = cts;
            _ = ReloadAfterQuietPeriodAsync(cts.Token);
        }
    }

    async Task ReloadAfterQuietPeriodAsync(CancellationToken token)
    {
        try
        {
            await Task.Delay(QuietPeriod, token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return; // superseded by a later event, or disposed
        }

        try
        {
            await _session.GetSnapshotAsync(token).ConfigureAwait(false);
            ReloadCount++;
            Console.Error.WriteLine("gedfire: input file changed on disk, reloaded.");
        }
        catch (OperationCanceledException)
        {
            // disposed mid-reload
        }
        catch (Exception ex)
        {
            // A transient mid-write race: DocumentSession's own retry already
            // covers one unstable read: this is a second failure surfaced,
            // not swallowed, since the next successful reload (this watcher's
            // or a subsequent tool call's) will recover on its own.
            Console.Error.WriteLine($"gedfire: input file changed on disk but reload failed: {ex.Message}");
        }
    }

    public async ValueTask DisposeAsync()
    {
        _watcher.EnableRaisingEvents = false;
        _watcher.Changed -= OnFileEvent;
        _watcher.Created -= OnFileEvent;
        _watcher.Renamed -= OnFileEvent;
        _watcher.Error -= OnError;
        _watcher.Dispose();

        await _lifetime.CancelAsync().ConfigureAwait(false);
        lock (_debounceLock) { _debounce?.Dispose(); }
        _lifetime.Dispose();
    }
}
