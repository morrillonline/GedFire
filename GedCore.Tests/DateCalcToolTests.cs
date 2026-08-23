using System.Text.Json;
using GedFire.Mcp;
using ModelContextProtocol.Protocol;

namespace GedCore.Tests;

public class DateCalcToolTests
{
    static JsonElement StructuredContent(CallToolResult result) => result.StructuredContent!.Value;

    static string TextOf(CallToolResult result) =>
        Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;

    static DateCalcTool Tool() => new(new ToolGate());

    [Theory]
    [InlineData("normalize", "11 FEB 1691/2", null, null, null, "11 FEB 1692", null)]
    [InlineData("add", "27 SEP 1777", "63y 4m 2d", null, null, "29 JAN 1841", null)]
    [InlineData("sub", "29 JAN 1841", "63y 4m 2d", null, null, "27 SEP 1777", null)]
    [InlineData("diff", null, null, "27 SEP 1777", "29 JAN 1841", null, "63y 4m 2d")]
    public async Task HandleAsync_WorkedExamples_ReturnCanonicalResult(
        string operation,
        string? date,
        string? age,
        string? from,
        string? to,
        string? expectedDate,
        string? expectedAge)
    {
        var result = await Tool().HandleAsync(operation, date, age, from, to, CancellationToken.None);

        Assert.False(result.IsError);
        var root = StructuredContent(result);
        Assert.Equal(operation, root.GetProperty("operation").GetString());
        Assert.Equal(expectedDate, root.GetProperty("date").GetString());
        Assert.Equal(expectedAge, root.GetProperty("age").GetString());
        Assert.Equal(JsonSerializer.Serialize(root), TextOf(result));
    }

    [Theory]
    [InlineData("normalize", "11 FEB 1691/2", "1y", null, null, "accepts only")]
    [InlineData("add", "1 JAN 2000", null, null, null, "requires date and age")]
    [InlineData("diff", null, "1y", "1 JAN 2000", "1 JAN 2001", "accepts only")]
    [InlineData("unknown", null, null, null, null, "operation must be")]
    public async Task HandleAsync_InvalidOperationShape_ReturnsIsError(
        string operation,
        string? date,
        string? age,
        string? from,
        string? to,
        string expectedMessage)
    {
        var result = await Tool().HandleAsync(operation, date, age, from, to, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Null(result.StructuredContent);
        Assert.Contains(expectedMessage, TextOf(result));
    }

    [Theory]
    [InlineData("add", "ABT 1780", "1y", null, null)]
    [InlineData("add", "1 JAN 2000", "1 year", null, null)]
    [InlineData("diff", null, null, "1 JAN 2001", "1 JAN 2000")]
    public async Task HandleAsync_InvalidDateOrAge_ReturnsIsError(
        string operation, string? date, string? age, string? from, string? to)
    {
        var result = await Tool().HandleAsync(operation, date, age, from, to, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Null(result.StructuredContent);
        Assert.NotEmpty(TextOf(result));
    }

    [Fact]
    public async Task HandleAsync_AlreadyCancelledToken_Propagates()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Tool().HandleAsync("normalize", "11 FEB 1691/2", null, null, null, cts.Token));
    }

    [Fact]
    public void ToMcpServerTool_DeclaresDocumentedSchemasAndAnnotations()
    {
        var tool = Tool().ToMcpServerTool();

        Assert.Equal(DateCalcTool.ToolName, tool.ProtocolTool.Name);
        Assert.Equal(DateCalcTool.Description, tool.ProtocolTool.Description);
        Assert.True(tool.ProtocolTool.Annotations!.ReadOnlyHint);
        Assert.False(tool.ProtocolTool.Annotations.DestructiveHint);
        Assert.True(tool.ProtocolTool.Annotations.IdempotentHint);

        using var expectedInput = JsonDocument.Parse(DateCalcTool.InputSchemaJson);
        using var expectedOutput = JsonDocument.Parse(DateCalcTool.OutputSchemaJson);
        Assert.Equal(
            JsonSerializer.Serialize(expectedInput.RootElement),
            JsonSerializer.Serialize(tool.ProtocolTool.InputSchema));
        Assert.Equal(
            JsonSerializer.Serialize(expectedOutput.RootElement),
            JsonSerializer.Serialize(tool.ProtocolTool.OutputSchema!.Value));

        foreach (var property in expectedInput.RootElement.GetProperty("properties").EnumerateObject())
            Assert.False(string.IsNullOrWhiteSpace(property.Value.GetProperty("description").GetString()));
        foreach (var property in expectedOutput.RootElement.GetProperty("properties").EnumerateObject())
            Assert.False(string.IsNullOrWhiteSpace(property.Value.GetProperty("description").GetString()));
    }
}