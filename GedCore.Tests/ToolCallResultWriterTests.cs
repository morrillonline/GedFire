using System.Text.Json;
using GedFire.Cli;
using GedFire.Mcp;

namespace GedCore.Tests;

public class ToolCallResultWriterTests
{
    [Theory]
    [InlineData("{}", 0)]
    [InlineData("{\"success\":true}", 0)]
    [InlineData("{\"passed\":true}", 0)]
    [InlineData("{\"success\":false}", 1)]
    [InlineData("{\"passed\":false}", 1)]
    [InlineData("{\"success\":true,\"passed\":false}", 1)]
    [InlineData("{\"diagnostics\":[{\"severity\":\"Warning\"}]}", 0)]
    public void StructuredResultsKeepTheirPayloadAndSetTheExitCode(string json, int expectedExit)
    {
        using var document = JsonDocument.Parse(json);
        var result = CallToolResults.Success(document.RootElement, CallToolResults.JsonOptions);
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exit = ToolCallResultWriter.Write(result, output, error);

        Assert.Equal(expectedExit, exit);
        using var actual = JsonDocument.Parse(output.ToString());
        Assert.True(JsonElement.DeepEquals(document.RootElement, actual.RootElement));
        Assert.Equal(expectedExit != 0, error.ToString().Length > 0);
    }

    [Fact]
    public void ToolErrorsKeepTheProtocolEnvelopeAndSeparateDiagnostics()
    {
        using var output = new StringWriter();
        using var error = new StringWriter();

        int exit = ToolCallResultWriter.Write(CallToolResults.Error("Synthetic failure."), output, error);

        Assert.Equal(1, exit);
        using var json = JsonDocument.Parse(output.ToString());
        Assert.True(json.RootElement.GetProperty("isError").GetBoolean());
        Assert.Equal("Synthetic failure.", json.RootElement.GetProperty("content")[0].GetProperty("text").GetString());
        Assert.Equal("Synthetic failure." + Environment.NewLine, error.ToString());
    }
}