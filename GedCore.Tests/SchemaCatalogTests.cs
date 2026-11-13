using System.Text;
using System.Text.Json;
using GedFire.Mcp;
using GedFire.Schemas;
using Json.Schema;

namespace GedCore.Tests;

public class SchemaCatalogTests
{
    static readonly JsonSchema FindingFile = Compile(SchemaCatalog.FindingFile);

    static JsonSchema Compile(PublishedSchema schema) =>
        JsonSchema.FromText(schema.Document().ToJsonString(), new BuildOptions { SchemaRegistry = new SchemaRegistry() });

    static bool SchemaAccepts(string json)
    {
        using var document = JsonDocument.Parse(json);
        return FindingFile.Evaluate(document.RootElement).IsValid;
    }

    // In this release an unknown key is only a warning; the schema already refuses it.
    static bool ReaderAccepts(string json, string name)
    {
        var file = FindingFileReader.Read(new MemoryStream(Encoding.UTF8.GetBytes(json)), name);
        return file.IsValid && file.Warnings.Count == 0;
    }

    public static IEnumerable<object[]> Fixtures() => FindingFixtures.Names.Select(name => new object[] { name });

    [Theory]
    [MemberData(nameof(Fixtures))]
    public void TheFindingFileSchemaAcceptsEveryFixture_AsTheReaderDoes(string name)
    {
        string json = FindingFixtures.Read(name);

        Assert.True(SchemaAccepts(json));
        Assert.True(ReaderAccepts(json, name));
    }

    [Theory]
    [InlineData("\"persons\": []")]
    [InlineData("\"correlation\": { \"kind\": \"park\" }")]
    [InlineData("\"status\": \"new\"")]
    [InlineData("\"meta\": \"new\"")]
    [InlineData("\"draftGaps\": [1]")]
    [InlineData("\"record\": { \"persons\": [] }")]
    [InlineData("\"record\": { \"persons\": [{ \"name\": \"Ada\", \"focus\": 0 }] }")]
    [InlineData("\"argument\": { \"difficulty\": \"easy\" }")]
    public void TheSchemaRefusesWhatTheReaderRefusesOrWarnsAbout(string member)
    {
        string json = "{ " + member + " }";

        Assert.False(SchemaAccepts(json));
        Assert.False(ReaderAccepts(json, "F-1.json"));
    }

    [Fact]
    public void ADraftTheReaderRejects_IsRefusedByTheSchema()
    {
        string json = FindingFixtures.Read("F-100.json").Replace("\"quay\": 3", "\"quay\": 9");

        Assert.False(SchemaAccepts(json));
        Assert.False(ReaderAccepts(json, "F-100.json"));
    }

    [Fact]
    public void EverySchemaIsAStandaloneVersionedDocument()
    {
        foreach (var schema in SchemaCatalog.All)
        {
            var document = schema.Document();
            Assert.Equal("https://json-schema.org/draft/2020-12/schema", (string?)document["$schema"]);
            Assert.Equal($"{SchemaCatalog.BaseUri}{schema.Name}/1", (string?)document["$id"]);
            Assert.Matches(@"^\d+\.\d+$", (string?)document["x-schemaVersion"]);
            Compile(schema);
        }
    }

    [Fact]
    public void TheRecordSchemaAcceptsTheRecordSlotOfAFindingFile()
    {
        var record = Compile(SchemaCatalog.Record);
        using var file = JsonDocument.Parse(FindingFixtures.Read("F-101.json"));

        Assert.True(record.Evaluate(file.RootElement.GetProperty("record")).IsValid);
    }
}
