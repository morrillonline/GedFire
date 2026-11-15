using System.Text;
using System.Text.Json;
using GedFire.Mcp;
using GedFire.Schemas;
using Json.Schema;

namespace GedCore.Tests;

public class SchemaCatalogTests
{
    static readonly JsonSchema FindingFile = Compile(SchemaCatalog.FindingFile);

    internal static JsonSchema Compile(PublishedSchema schema) =>
        JsonSchema.FromText(schema.Document().ToJsonString(), new BuildOptions { SchemaRegistry = new SchemaRegistry() });

    static bool SchemaAccepts(string json)
    {
        using var document = JsonDocument.Parse(json);
        return FindingFile.Evaluate(document.RootElement).IsValid;
    }

    static bool ReaderAccepts(string json, string name)
    {
        var file = FindingFileReader.Read(new MemoryStream(Encoding.UTF8.GetBytes(json)), name);
        return file.IsValid;
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
    public void TheSchemaRefusesWhatTheReaderRefuses(string member)
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
    public void ABiographyNoteWithNoCites_IsRefusedByTheSchema()
    {
        string json = FindingFixtures.Read("F-106.json")
            .Replace("\"cites\": [ { \"quay\": 3 }, { \"finding\": \"F-105\", \"quay\": 2, \"excerpt\": \"Ezra Quillfeather, 49, farmer\" } ]", "\"cites\": []");

        Assert.False(SchemaAccepts(json));
    }

    [Fact]
    public void EverySchemaIsAStandaloneVersionedDocument()
    {
        foreach (var schema in SchemaCatalog.All)
        {
            var document = schema.Document();
            Assert.Equal("https://json-schema.org/draft/2020-12/schema", (string?)document["$schema"]);
            Assert.Equal($"{SchemaCatalog.BaseUri}{schema.Name}/{schema.Version.Split('.')[0]}", (string?)document["$id"]);
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

    static readonly JsonSchema Changeset = Compile(SchemaCatalog.Changeset);

    public static IEnumerable<object[]> ChangesetFixtureNames() =>
        typeof(ChangesetFixtures).Assembly.GetManifestResourceNames()
            .Where(n => n.StartsWith("GedCore.Tests.TestData.Changesets.", StringComparison.Ordinal))
            .Select(n => new object[] { n["GedCore.Tests.TestData.Changesets.".Length..^".json".Length] });

    [Theory]
    [MemberData(nameof(ChangesetFixtureNames))]
    public void EveryChangesetFixture_IsCanonical_AndTheChangesetSchemaAcceptsIt(string name)
    {
        string json = ChangesetFixtures.Read(name);
        using var document = JsonDocument.Parse(json);

        Assert.Empty(GedCore.Apply.Changeset.Read(json).Warnings);
        Assert.True(Changeset.Evaluate(document.RootElement).IsValid);
    }

    [Theory]
    [InlineData("\"citation\": { \"source\": \"@S1@\" }")]
    [InlineData("\"citations\": { \"source\": \"@S1@\" }")]
    [InlineData("\"citatons\": []")]
    [InlineData("\"value\": { \"date\": \"1900\" }")]
    [InlineData("\"value\": \"1 JAN 1900\"")]
    public void TheChangesetSchemaRefusesWhatTheContractReports(string member)
    {
        string json = $$"""
            { "schemaVersion": "1.0", "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateVital", "record": "@I1@", "fact": "BIRT", "date": "1900", {{member}} } ] } ] }
            """;
        using var document = JsonDocument.Parse(json);

        var reading = GedCore.Apply.Changeset.Read(json);
        Assert.NotEmpty(reading.Errors.Concat(reading.Warnings));
        Assert.False(Changeset.Evaluate(document.RootElement).IsValid);
    }

    [Theory]
    [InlineData("\"schemaVersion\": \"2.0\",")]
    [InlineData("")]
    public void TheChangesetSchemaRequiresTheSupportedMajorVersion(string version)
    {
        using var document = JsonDocument.Parse($$"""{ {{version}} "items": [] }""");

        Assert.False(Changeset.Evaluate(document.RootElement).IsValid);
    }

    [Fact]
    public void TheCardsSchemaAcceptsACardsFile()
    {
        using var document = JsonDocument.Parse("""{ "cards": [ { "xref": "@I1@", "class": "enrich-person", "gap": "enrich-death", "points": 7 } ] }""");

        Assert.True(Compile(SchemaCatalog.Cards).Evaluate(document.RootElement).IsValid);
    }
}
