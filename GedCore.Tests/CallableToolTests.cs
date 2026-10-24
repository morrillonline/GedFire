using GedFire.Mcp;
using ModelContextProtocol.Protocol;

namespace GedCore.Tests;

public class CallableToolTests
{
    static CallableTool Tool(Delegate handler) => Assert.IsType<CallableTool>(new ToolDefinition(
        "test", "Synthetic test tool.", "{\"type\":\"object\",\"properties\":{}}", "{}", ToolBehavior.ReadOnlyIdempotent)
        .CreateTool(handler));

    [Fact]
    public async Task InvocationPreservesTheHandlersResult()
    {
        var expected = CallToolResults.Error("Synthetic failure.");
        Func<Task<CallToolResult>> handler = () => Task.FromResult(expected);

        var actual = await Tool(handler).InvokeAsync(arguments: null);

        Assert.Same(expected, actual);
    }

    [Fact]
    public async Task InvalidHandlerResultIsReportedInsteadOfAnInvalidCast()
    {
        Func<Task<string>> handler = () => Task.FromResult("not a tool result");

        var result = await Tool(handler).InvokeAsync(arguments: null);

        Assert.True(result.IsError);
        Assert.Contains("handler did not return a tool result", Assert.IsType<TextContentBlock>(result.Content[0]).Text);
    }
}