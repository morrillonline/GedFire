using GedFire.Mcp;
using ModelContextProtocol.Protocol;

namespace GedCore.Tests;

public class GatedToolRunnerTests
{
    static string TextOf(CallToolResult result) => Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;

    [Fact]
    public async Task ASuccessfulResult_IsReturnedUnchanged()
    {
        var expected = CallToolResults.Error("sentinel");

        var result = await GatedToolRunner.RunAsync(new ToolGate(), _ => Task.FromResult(expected), CancellationToken.None);

        Assert.Same(expected, result);
    }

    [Fact]
    public async Task AnException_BecomesAnIsErrorResultWithTypeMessageAndStackTrace()
    {
        var result = await GatedToolRunner.RunAsync(
            new ToolGate(), _ => throw new InvalidOperationException("kaput"), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.StartsWith("System.InvalidOperationException: kaput", TextOf(result));
        Assert.Contains("GatedToolRunnerTests", TextOf(result));
    }

    [Fact]
    public async Task ARateLimitRejection_BecomesAnIsErrorResultAndTheWorkNeverRuns()
    {
        var now = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var gate = new ToolGate(() => now);
        for (int i = 0; i < ToolGate.MaxCallsPerMinute; i++)
            await gate.RunAsync(_ => Task.FromResult(0), CancellationToken.None);

        bool ran = false;
        var result = await GatedToolRunner.RunAsync(gate, _ => { ran = true; return Task.FromResult(CallToolResults.Error("x")); }, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("Rate limit exceeded", TextOf(result));
        Assert.False(ran);
    }

    [Fact]
    public async Task ARequestedCancellation_PropagatesInsteadOfBecomingAResult()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => GatedToolRunner.RunAsync(
            new ToolGate(), _ => Task.FromResult(CallToolResults.Error("x")), cancellation.Token));
    }
}
