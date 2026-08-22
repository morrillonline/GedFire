using GedFire.Mcp;

namespace GedCore.Tests;

public class ToolGateTests
{
    [Fact]
    public async Task RunAsync_ReturnsTheActionsResult()
    {
        var gate = new ToolGate();
        int result = await gate.RunAsync(_ => Task.FromResult(42), CancellationToken.None);
        Assert.Equal(42, result);
    }

    [Fact]
    public async Task RunAsync_PropagatesTheActionsException()
    {
        var gate = new ToolGate();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            gate.RunAsync<int>(_ => throw new InvalidOperationException("boom"), CancellationToken.None));
    }

    [Fact]
    public async Task RunAsync_LimitsConcurrencyToFour()
    {
        var gate = new ToolGate();
        var sync = new object();
        int concurrent = 0;
        int maxObserved = 0;
        var entered = new SemaphoreSlim(0);
        var release = new TaskCompletionSource();

        async Task<int> Slot(int n)
        {
            lock (sync)
            {
                concurrent++;
                maxObserved = Math.Max(maxObserved, concurrent);
            }
            entered.Release();
            await release.Task;
            lock (sync) { concurrent--; }
            return n;
        }

        var tasks = Enumerable.Range(0, 5)
            .Select(n => gate.RunAsync(_ => Slot(n), CancellationToken.None))
            .ToArray();

        // Exactly 4 calls should be able to enter their slot promptly.
        for (int i = 0; i < 4; i++)
            Assert.True(await entered.WaitAsync(TimeSpan.FromSeconds(5)), "expected 4 calls to enter promptly");

        // The 5th must still be waiting for a free concurrency slot.
        Assert.False(await entered.WaitAsync(TimeSpan.FromMilliseconds(200)),
            "a 5th concurrent call must wait for a free slot");

        release.SetResult();
        await Task.WhenAll(tasks);

        Assert.Equal(4, maxObserved);
    }

    [Fact]
    public async Task RunAsync_RejectsTheCallOverTheSlidingWindowLimit()
    {
        var now = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var gate = new ToolGate(() => now);

        for (int i = 0; i < ToolGate.MaxCallsPerMinute; i++)
            await gate.RunAsync(_ => Task.FromResult(0), CancellationToken.None);

        await Assert.ThrowsAsync<ToolRateLimitExceededException>(() =>
            gate.RunAsync(_ => Task.FromResult(0), CancellationToken.None));
    }

    [Fact]
    public async Task RunAsync_AdmitsAgainOnceOldCallsAgeOutOfTheWindow()
    {
        var now = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var gate = new ToolGate(() => now);

        for (int i = 0; i < ToolGate.MaxCallsPerMinute; i++)
            await gate.RunAsync(_ => Task.FromResult(0), CancellationToken.None);

        now = now.AddSeconds(61);

        int result = await gate.RunAsync(_ => Task.FromResult(99), CancellationToken.None);
        Assert.Equal(99, result);
    }

    [Fact]
    public async Task RunAsync_RateRejectionDoesNotConsumeAConcurrencySlot()
    {
        var now = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var gate = new ToolGate(() => now);

        for (int i = 0; i < ToolGate.MaxCallsPerMinute; i++)
            await gate.RunAsync(_ => Task.FromResult(0), CancellationToken.None);

        for (int i = 0; i < 10; i++)
            await Assert.ThrowsAsync<ToolRateLimitExceededException>(() =>
                gate.RunAsync(_ => Task.FromResult(0), CancellationToken.None));

        now = now.AddSeconds(61);
        int result = await gate.RunAsync(_ => Task.FromResult(7), CancellationToken.None);
        Assert.Equal(7, result);
    }

    [Fact]
    public async Task RunAsync_WaitingCallRemainsCancellable()
    {
        var gate = new ToolGate();
        var entered = new SemaphoreSlim(0);
        var release = new TaskCompletionSource();

        Task<int> Occupy() => gate.RunAsync(async _ =>
        {
            entered.Release();
            await release.Task;
            return 0;
        }, CancellationToken.None);

        var occupied = Enumerable.Range(0, ToolGate.MaxConcurrency).Select(_ => Occupy()).ToArray();
        for (int i = 0; i < ToolGate.MaxConcurrency; i++)
            Assert.True(await entered.WaitAsync(TimeSpan.FromSeconds(5)));

        using var cts = new CancellationTokenSource();
        var waitingTask = gate.RunAsync(_ => Task.FromResult(0), cts.Token);
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waitingTask);

        release.SetResult();
        await Task.WhenAll(occupied);
    }

    [Fact]
    public async Task RunAsync_NullAction_Throws()
    {
        var gate = new ToolGate();
        await Assert.ThrowsAsync<ArgumentNullException>(() => gate.RunAsync<int>(null!, CancellationToken.None));
    }

    [Fact]
    public void Constructor_NullClock_Throws()
        => Assert.Throws<ArgumentNullException>(() => new ToolGate(null!));
}
