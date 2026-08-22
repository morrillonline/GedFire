namespace GedFire.Mcp;

// ---------------------------------------------------------------------------
// The two admission bounds from docs/design/mcp-server.md "Lifecycle":
// a SemaphoreSlim(4) for concurrency and a 120-per-minute sliding window.
// Wraps every tool invocation; throws ToolRateLimitExceededException on rate
// rejection. Knows nothing about any specific tool.
// ---------------------------------------------------------------------------

public sealed class ToolGate
{
    public const int MaxConcurrency = 4;
    public const int MaxCallsPerMinute = 120;

    static readonly TimeSpan Window = TimeSpan.FromMinutes(1);

    readonly SemaphoreSlim _concurrency = new(MaxConcurrency, MaxConcurrency);
    readonly object _rateLock = new();
    readonly Queue<DateTime> _recentCallsUtc = new();
    readonly Func<DateTime> _utcNow;

    public ToolGate() : this(() => DateTime.UtcNow) { }

    /// <summary>Test seam: supply a controllable clock instead of the real one.</summary>
    public ToolGate(Func<DateTime> utcNow) =>
        _utcNow = utcNow ?? throw new ArgumentNullException(nameof(utcNow));

    /// <summary>
    /// Run <paramref name="action"/> under both admission bounds: reject
    /// immediately (without occupying a concurrency slot) if the sliding
    /// window is full; otherwise wait for a concurrency slot — cancellably —
    /// and run. Never leaves a slot held after the action completes, faults,
    /// or is cancelled.
    /// </summary>
    public async Task<T> RunAsync<T>(Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (!TryAdmitByRate())
            throw new ToolRateLimitExceededException(
                $"Rate limit exceeded: at most {MaxCallsPerMinute} tool calls per minute. Try again shortly.");

        await _concurrency.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await action(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _concurrency.Release();
        }
    }

    bool TryAdmitByRate()
    {
        lock (_rateLock)
        {
            DateTime now = _utcNow();
            while (_recentCallsUtc.Count > 0 && now - _recentCallsUtc.Peek() >= Window)
                _recentCallsUtc.Dequeue();

            if (_recentCallsUtc.Count >= MaxCallsPerMinute)
                return false;

            _recentCallsUtc.Enqueue(now);
            return true;
        }
    }
}
