using System.Text.Json;
using GedFire.Mcp;
using ModelContextProtocol.Protocol;

namespace GedCore.Tests;

public class CallToolResultsTests
{
    sealed record Payload(int Number, string? Text);

    [Fact]
    public void Success_SetsStructuredContentAndTextFallback_NotAnError()
    {
        var result = CallToolResults.Success(new Payload(42, "hello"), CallToolResults.JsonOptions);

        Assert.False(result.IsError);
        Assert.NotNull(result.StructuredContent);
        Assert.Equal(42, result.StructuredContent!.Value.GetProperty("number").GetInt32());
        Assert.Equal("hello", result.StructuredContent!.Value.GetProperty("text").GetString());

        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.Equal(
            JsonSerializer.Serialize(result.StructuredContent!.Value, CallToolResults.JsonOptions),
            text);
    }

    [Fact]
    public void Success_EmitsNullPropertiesRatherThanOmittingThem()
    {
        var result = CallToolResults.Success(new Payload(1, null), CallToolResults.JsonOptions);

        Assert.Equal(JsonValueKind.Null, result.StructuredContent!.Value.GetProperty("text").ValueKind);
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.Contains("\"text\":null", text);
    }

    [Fact]
    public void Success_TextFallback_HasNoIndentation()
    {
        var result = CallToolResults.Success(new Payload(1, "x"), CallToolResults.JsonOptions);
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.DoesNotContain("\n", text);
        Assert.DoesNotContain("  ", text);
    }

    [Fact]
    public void Success_UsesTheRuntimeTypeWhenPayloadIsBoxed()
    {
        object payload = new Payload(7, "boxed");
        var result = CallToolResults.Success(payload, CallToolResults.JsonOptions);
        Assert.Equal(7, result.StructuredContent!.Value.GetProperty("number").GetInt32());
    }

    [Fact]
    public void Success_NullPayload_Throws()
        => Assert.Throws<ArgumentNullException>(() => CallToolResults.Success(null!, CallToolResults.JsonOptions));

    [Fact]
    public void Error_SetsIsErrorWithOneTextBlock_NoStructuredContent()
    {
        var result = CallToolResults.Error("something went wrong");

        Assert.True(result.IsError);
        Assert.Null(result.StructuredContent);
        var text = Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;
        Assert.Equal("something went wrong", text);
    }
}
