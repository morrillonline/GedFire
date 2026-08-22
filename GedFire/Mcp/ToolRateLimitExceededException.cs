namespace GedFire.Mcp;

/// <summary>
/// A tool call was rejected by ToolGate's sliding-window rate limit. Always
/// retryable: FindPersonTool's last-chance handler turns this into an
/// isError tool result (docs/design/mcp-server.md "Lifecycle").
/// </summary>
public sealed class ToolRateLimitExceededException : Exception
{
    public ToolRateLimitExceededException(string message) : base(message) { }
}
