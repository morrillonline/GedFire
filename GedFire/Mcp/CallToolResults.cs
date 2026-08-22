using System.Text.Json;
using ModelContextProtocol.Protocol;

namespace GedFire.Mcp;

// ---------------------------------------------------------------------------
// Shared CallToolResult construction for every tool on this server
// (docs/design/mcp-server.md "Tool registration and server guidance" /
// "Errors"): a successful result carries structuredContent plus the
// identical compact JSON as a text fallback; an error result carries one
// text block and isError: true. FindPersonTool and GetDocumentStatsTool both
// build their results this way rather than duplicating the shape.
// ---------------------------------------------------------------------------

public static class CallToolResults
{
    // camelCase names, nulls emitted (never ignored), no indentation in the
    // text content block (docs/design/mcp-server.md "Tool registration").
    // Shared by every tool's result and text-fallback serialization.
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    public static CallToolResult Success(object payload, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(payload);
        string json = JsonSerializer.Serialize(payload, payload.GetType(), options);
        return new CallToolResult
        {
            StructuredContent = JsonSerializer.SerializeToElement(payload, payload.GetType(), options),
            Content = [new TextContentBlock { Text = json }],
            IsError = false,
        };
    }

    public static CallToolResult Error(string message) => new()
    {
        Content = [new TextContentBlock { Text = message }],
        IsError = true,
    };
}
