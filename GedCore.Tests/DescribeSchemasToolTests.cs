using System.Text.Json;
using GedFire.Mcp;
using GedFire.Schemas;
using ModelContextProtocol.Protocol;

namespace GedCore.Tests;

public class DescribeSchemasToolTests
{
    static async Task<CallToolResult> Call(string? name = null) =>
        await new DescribeSchemasTool(new ToolGate()).HandleAsync(
            name is null ? default : JsonDocument.Parse(JsonSerializer.Serialize(name)).RootElement.Clone(), CancellationToken.None);

    [Fact]
    public async Task WithoutAName_EverySchemaIsReturned()
    {
        var schemas = (await Call()).StructuredContent!.Value.GetProperty("schemas");

        Assert.Equal(SchemaCatalog.Names, schemas.EnumerateArray().Select(s => s.GetProperty("name").GetString()));
    }

    [Fact]
    public async Task AName_ReturnsTheSameDocumentTheCliPrints()
    {
        var schema = Assert.Single((await Call("finding-file")).StructuredContent!.Value.GetProperty("schemas").EnumerateArray());

        Assert.Equal("1.0", schema.GetProperty("version").GetString());
        Assert.Equal(SchemaCatalog.FindingFile.Id, schema.GetProperty("id").GetString());
        using var expected = JsonDocument.Parse(SchemaCatalog.FindingFile.Document().ToJsonString());
        Assert.True(JsonElement.DeepEquals(expected.RootElement, schema.GetProperty("schema")));
    }

    [Fact]
    public async Task AnUnknownName_IsRefused()
    {
        var result = await Call("persons");

        Assert.True(result.IsError);
        Assert.Contains("name must be one of finding, finding-file, record, argument", ((TextContentBlock)result.Content[0]).Text);
    }
}
