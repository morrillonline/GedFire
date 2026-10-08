using ModelContextProtocol.Protocol;

namespace GedFire.Mcp;

// ---------------------------------------------------------------------------
// Runs a tool's work through the ToolGate and turns every failure into an
// isError result, so a tool never throws at the protocol layer. An
// already-requested cancellation is left to propagate as
// OperationCanceledException so no late response is emitted.
// ---------------------------------------------------------------------------

public static class GatedToolRunner
{
    public static async Task<CallToolResult> RunAsync(
        ToolGate gate, Func<CancellationToken, Task<CallToolResult>> work, CancellationToken cancellationToken)
    {
        try
        {
            return await gate.RunAsync(work, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Includes a rate-limit rejection, thrown before the work starts. The stack trace is
            // deliberately included: this is a local tool run by the researcher against their own data.
            return CallToolResults.Error($"{ex.GetType().FullName}: {ex.Message}\n{ex.StackTrace}");
        }
    }
}
