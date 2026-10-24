using System.Text.Json;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace GedFire.Mcp;

public sealed class CallableTool(McpServerTool tool, AIFunction function) : DelegatingMcpServerTool(tool), ILocalTool
{
    public async Task<CallToolResult> InvokeAsync(
        IDictionary<string, JsonElement>? arguments, CancellationToken cancellationToken = default)
    {
        var values = new AIFunctionArguments(arguments?.ToDictionary(p => p.Key, p => (object?)p.Value)
            ?? new Dictionary<string, object?>());
        var result = await function.InvokeAsync(values, cancellationToken).ConfigureAwait(false);
        return result is CallToolResult callResult
            ? callResult
            : CallToolResults.Error($"{ProtocolTool.Name}: the handler did not return a tool result.");
    }
}