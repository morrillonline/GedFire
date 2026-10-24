using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace GedFire.Mcp;

public sealed class ToolDispatcher
{
    readonly IReadOnlyDictionary<string, McpServerTool> _tools;
    readonly ToolArgumentGuard _argumentGuard;

    public ToolDispatcher(IEnumerable<McpServerTool> tools)
    {
        _tools = tools.ToDictionary(t => t.ProtocolTool.Name, StringComparer.Ordinal);
        _argumentGuard = new ToolArgumentGuard(_tools.Values);
    }

    public async Task<CallToolResult> InvokeAsync(
        string name, IDictionary<string, JsonElement>? arguments, CancellationToken cancellationToken = default)
    {
        if (!_tools.TryGetValue(name, out var tool))
            return CallToolResults.Error($"Unknown tool: {name}");

        if (_argumentGuard.Check(name, arguments) is { } error)
            return CallToolResults.Error(error);

        if (tool is not ILocalTool callable)
            return CallToolResults.Error($"{name} does not support local invocation.");

        return await callable.InvokeAsync(arguments, cancellationToken).ConfigureAwait(false);
    }
}