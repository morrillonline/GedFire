using System.Text;
using System.Text.Json;
using GedFire.Cli;

namespace GedCore.Tests;

public class ToolCallArgumentsReaderTests
{
    [Fact]
    public async Task StreamReaderKeepsArgumentTypesAndLeavesTheStreamOpen()
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes("""{"query":"Mara Example","maxResults":8,"hints":{"birth":{"year":1800}}}"""));

        var arguments = await ToolCallArgumentsReader.ReadAsync(stream);

        Assert.Equal("Mara Example", arguments["query"].GetString());
        Assert.Equal(8, arguments["maxResults"].GetInt32());
        Assert.Equal(1800, arguments["hints"].GetProperty("birth").GetProperty("year").GetInt32());
        Assert.True(stream.CanRead);
    }

    [Fact]
    public async Task TextAndStreamReadersHaveTheSameContract()
    {
        const string json = """{"xrefs":["@I1@"],"includeSources":true}""";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));
        using var reader = new StringReader(json);

        var fromStream = await ToolCallArgumentsReader.ReadAsync(stream);
        var fromText = await ToolCallArgumentsReader.ReadAsync(reader);

        Assert.Equal(JsonSerializer.Serialize(fromStream), JsonSerializer.Serialize(fromText));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("{")]
    [InlineData("{\"query\":\"first\",\"query\":\"second\"}")]
    public async Task InvalidOrAmbiguousObjectsAreRejected(string json)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(json));

        await Assert.ThrowsAnyAsync<JsonException>(() => ToolCallArgumentsReader.ReadAsync(stream));
    }
}