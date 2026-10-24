using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace GedFire.Cli;

public static class ToolCallResultWriter
{
    public static int Write(CallToolResult result, TextWriter output, TextWriter error)
    {
        if (result.IsError == true)
        {
            output.WriteLine(JsonSerializer.Serialize(result, McpJsonUtilities.DefaultOptions));
            foreach (var text in result.Content.OfType<TextContentBlock>()) error.WriteLine(text.Text);
            return 1;
        }

        output.WriteLine(JsonSerializer.Serialize(result.StructuredContent));
        if (!FailedCheck(result.StructuredContent)) return 0;

        error.WriteLine("The tool reported a failed check; see the JSON result.");
        return 1;
    }

    static bool FailedCheck(JsonElement? payload) => payload is { ValueKind: JsonValueKind.Object } value
        && (IsFalse(value, "success") || IsFalse(value, "passed"));

    static bool IsFalse(JsonElement value, string property) =>
        value.TryGetProperty(property, out var flag) && flag.ValueKind == JsonValueKind.False;
}