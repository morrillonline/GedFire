using System.Text.Json;
using GedFire.Mcp;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace GedCore.Tests;

public class ToolDispatcherTests
{
    sealed class StubLocalTool(McpServerTool tool, CallToolResult result) : DelegatingMcpServerTool(tool), ILocalTool
    {
        public Task<CallToolResult> InvokeAsync(
            IDictionary<string, JsonElement>? arguments, CancellationToken cancellationToken = default) => Task.FromResult(result);
    }

    static Dictionary<string, JsonElement> Args(string json) =>
        JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(json)!;

    static ToolDispatcher Dispatcher() => new([new DateCalcTool(new ToolGate()).ToMcpServerTool()]);

    [Fact]
    public async Task InvokeAsync_UsesTheRegisteredHandlerAndResult()
    {
        var arguments = Args("""{"operation":"normalize","date":"1 JAN 1800"}""");
        var expected = await new DateCalcTool(new ToolGate()).HandleAsync("normalize", "1 JAN 1800", null, null, null, CancellationToken.None);

        var actual = await Dispatcher().InvokeAsync("date_calc", arguments);

        Assert.Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(actual));
    }

    [Fact]
    public async Task InvokeAsync_UsesTheSchemaArgumentGuard()
    {
        var result = await Dispatcher().InvokeAsync("date_calc", Args("""{"unknown":true}"""));

        Assert.True(result.IsError);
        Assert.Contains("missing required parameter", JsonSerializer.Serialize(result));
        Assert.Contains("unrecognized parameter", JsonSerializer.Serialize(result));
    }

    [Fact]
    public async Task InvokeAsync_UnknownToolIsAnError()
    {
        var result = await Dispatcher().InvokeAsync("unknown", null);

        Assert.True(result.IsError);
    }

    [Fact]
    public async Task InvokeAsync_UnsupportedToolReturnsAnErrorInsteadOfAnInvalidCast()
    {
        var tool = McpServerTool.Create(() => "example", new McpServerToolCreateOptions { Name = "external" });

        var result = await new ToolDispatcher([tool]).InvokeAsync("external", null);

        Assert.True(result.IsError);
        Assert.Contains("does not support local invocation", JsonSerializer.Serialize(result));
    }

    [Fact]
    public async Task InvokeAsync_AcceptsAlternativeLocalToolImplementations()
    {
        var expected = CallToolResults.Error("Synthetic handler result.");
        var inner = McpServerTool.Create(() => "example", new McpServerToolCreateOptions { Name = "alternate" });
        var tool = new StubLocalTool(inner, expected);

        var actual = await new ToolDispatcher([tool]).InvokeAsync("alternate", null);

        Assert.Same(expected, actual);
    }
}