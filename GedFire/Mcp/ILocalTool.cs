using System.Text.Json;
using ModelContextProtocol.Protocol;

namespace GedFire.Mcp;

public interface ILocalTool
{
    Task<CallToolResult> InvokeAsync(
        IDictionary<string, JsonElement>? arguments, CancellationToken cancellationToken = default);
}