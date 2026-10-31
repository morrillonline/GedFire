using System.Text;
using System.Text.Json;
using GedCore.Ged70;
using GedCore.Policy;
using GedFire.Mcp;
using ModelContextProtocol.Protocol;

namespace GedCore.Tests;

/// <summary>validate_changeset and apply_changeset with a server policy, and validate_changeset's points preview.</summary>
public class ValidateChangesetPointsTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("gedfire-points-tests-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    string WriteGed()
    {
        string path = Path.Combine(_dir, "test.ged");
        var document = Ged70Parser.Parse(string.Join("\r\n",
        [
            "0 HEAD", "1 GEDC", "2 VERS 7.0",
            "0 @I00001@ INDI", "1 NAME Allen /Test/", "1 SEX M",
            "0 @S00001@ SOUR", "1 TITL Existing source",
            "0 TRLR",
        ]) + "\r\n");
        using var stream = File.Create(path);
        Ged70Formatter.Write(document, stream);
        return path;
    }

    string WriteChangeset(string json)
    {
        string path = Path.Combine(_dir, "changeset.json");
        File.WriteAllText(path, json);
        return path;
    }

    static ChangesetPolicy Policy(string json) => ChangesetPolicy.Load(new MemoryStream(Encoding.UTF8.GetBytes(json)));

    static JsonElement Cards(string json) => JsonDocument.Parse(json).RootElement.Clone();

    static JsonElement Structured(CallToolResult result) => result.StructuredContent!.Value;

    const string DeathChangeset = """
        { "items": [ { "item": 1, "ops": [
          { "op": "createOrUpdateVital", "record": "@I00001@", "fact": "DEAT",
            "value": { "date": "1 JAN 1900", "place": "Salem" },
            "citation": { "source": "@S00001@", "page": "p. 1", "quay": 1 } } ] } ] }
        """;

    // ---- points preview ----

    [Fact]
    public async Task WithoutCards_TheResultHasNoPointsPreview()
    {
        var tool = new ValidateChangesetTool(WriteGed(), new ToolGate());

        var result = await tool.HandleAsync(WriteChangeset(DeathChangeset), "all", CancellationToken.None);

        Assert.False(Structured(result).TryGetProperty("pointsPreview", out _));
    }

    [Fact]
    public async Task ACardWhoseFactTheChangesetWrites_IsClosed()
    {
        var tool = new ValidateChangesetTool(WriteGed(), new ToolGate());
        var cards = Cards("""[ { "xref": "@I00001@", "class": "enrich-person", "gap": "enrich-death", "points": 7 } ]""");

        var result = await tool.HandleAsync(WriteChangeset(DeathChangeset), "all", cards, CancellationToken.None);

        var preview = Structured(result).GetProperty("pointsPreview");
        Assert.Equal(7, preview.GetProperty("total").GetInt32());
        var closed = Assert.Single(preview.GetProperty("closed").EnumerateArray());
        Assert.Equal("@I00001@|enrich-person|enrich-death", closed.GetProperty("cardKey").GetString());
        Assert.Equal("DEAT @I00001@ (first date and place and citation)", closed.GetProperty("wrote").GetString());
    }

    [Fact]
    public async Task ACardWhoseFactIsOnlyCited_IsNotClosed()
    {
        string path = WriteGed();
        var tool = new ValidateChangesetTool(path, new ToolGate());
        var cards = Cards("""[ { "xref": "@I00001@", "class": "enrich-person", "gap": "enrich-birth", "points": 7 } ]""");

        var result = await tool.HandleAsync(WriteChangeset(DeathChangeset), "all", cards, CancellationToken.None);

        var preview = Structured(result).GetProperty("pointsPreview");
        Assert.Equal(0, preview.GetProperty("total").GetInt32());
        Assert.Equal("no BIRT written on the subject (wrote DEAT)",
            Assert.Single(preview.GetProperty("notClosed").EnumerateArray()).GetProperty("reason").GetString());
    }

    [Fact]
    public async Task ARenamedSubject_IsReportedAsASubjectMismatch()
    {
        var tool = new ValidateChangesetTool(WriteGed(), new ToolGate());
        var cards = Cards("""[ { "xref": "@I00001@", "class": "enrich-person", "gap": "enrich-death", "name": "Allen Test", "points": 7 } ]""");
        string changeset = """
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateVital", "record": "@I00001@", "fact": "NAME", "value": "Alan /Test/" },
              { "op": "createOrUpdateVital", "record": "@I00001@", "fact": "DEAT", "value": { "date": "1 JAN 1900" } } ] } ] }
            """;

        var result = await tool.HandleAsync(WriteChangeset(changeset), "all", cards, CancellationToken.None);

        var mismatch = Assert.Single(Structured(result).GetProperty("pointsPreview").GetProperty("subjectMismatch").EnumerateArray());
        Assert.Equal("Allen Test", mismatch.GetProperty("cardSubject").GetString());
        Assert.Equal("Alan Test", mismatch.GetProperty("appliedName").GetString());
    }

    [Fact]
    public async Task CardsPath_ReadsTheCardsFromAFile()
    {
        var tool = new ValidateChangesetTool(WriteGed(), new ToolGate());
        string cardsFile = Path.Combine(_dir, "cards.json");
        File.WriteAllText(cardsFile, """{ "cards": [ { "xref": "@I00001@", "class": "enrich-person", "gap": "enrich-death", "points": 7 } ] }""");

        var result = await tool.HandleAsync(WriteChangeset(DeathChangeset), "all", default, Cards(JsonSerializer.Serialize(cardsFile)), CancellationToken.None);

        Assert.Equal(7, Structured(result).GetProperty("pointsPreview").GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task CardsAndCardsPathTogether_AreRefused()
    {
        var tool = new ValidateChangesetTool(WriteGed(), new ToolGate());

        var result = await tool.HandleAsync(WriteChangeset(DeathChangeset), "all", Cards("[]"), Cards("\"cards.json\""), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("give cards or cardsPath, not both.", Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text);
    }

    [Fact]
    public async Task AMalformedCard_IsRefusedNamingTheField()
    {
        var tool = new ValidateChangesetTool(WriteGed(), new ToolGate());

        var result = await tool.HandleAsync(WriteChangeset(DeathChangeset), "all",
            Cards("""[ { "xref": "@I00001@", "class": "enrich-person" } ]"""), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("cards[0].points is required", Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text);
    }

    [Fact]
    public void TheAdvertisedSchemas_DescribeCardsAndThePreview()
    {
        Assert.Contains("\"cards\"", ValidateChangesetTool.InputSchemaJson);
        Assert.Contains("\"pointsPreview\"", ValidateChangesetTool.OutputSchemaJson);
        Assert.DoesNotContain("\"cards\"", ApplyChangesetTool.InputSchemaJson);
        Assert.DoesNotContain("\"pointsPreview\"", ApplyChangesetTool.OutputSchemaJson);
    }

    // ---- policy ----

    [Fact]
    public async Task DryRun_ReportsEveryPolicyViolationAsAnError()
    {
        var policy = Policy("""{ "verificationNote": { "maxQuay": 1, "notePrefix": "needs independent verification:" } }""");
        var tool = new ValidateChangesetTool(WriteGed(), new ToolGate(), policy);

        var result = await tool.HandleAsync(WriteChangeset(DeathChangeset), "all", CancellationToken.None);

        var structured = Structured(result);
        Assert.False(structured.GetProperty("success").GetBoolean());
        Assert.Contains("policy: item 1", Assert.Single(structured.GetProperty("errors").EnumerateArray()).GetString());
    }

    [Fact]
    public async Task DryRun_ShowsPolicyViolationsBesideTheDocumentsOwnErrors()
    {
        var policy = Policy("""{ "bannedPhrases": ["not added"] }""");
        var tool = new ValidateChangesetTool(WriteGed(), new ToolGate(), policy);
        string changeset = """
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateNote", "record": "@I09999@", "text": "not added" } ] } ] }
            """;

        var result = await tool.HandleAsync(WriteChangeset(changeset), "all", CancellationToken.None);

        var errors = Structured(result).GetProperty("errors").EnumerateArray().Select(e => e.GetString()!).ToList();
        Assert.Equal(2, errors.Count);
        Assert.StartsWith("policy: ", errors[0]);
    }

    [Fact]
    public async Task ACompliantChangeset_PassesThePolicy()
    {
        var policy = Policy("""{ "bannedPhrases": ["not added"] }""");
        var tool = new ValidateChangesetTool(WriteGed(), new ToolGate(), policy);

        var result = await tool.HandleAsync(WriteChangeset(DeathChangeset), "all", CancellationToken.None);

        Assert.True(Structured(result).GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task Apply_RefusesAPolicyViolationAndLeavesTheFileUntouched()
    {
        string path = WriteGed();
        byte[] before = File.ReadAllBytes(path);
        var policy = Policy("""{ "verificationNote": { "maxQuay": 1, "notePrefix": "needs independent verification:" } }""");
        var tool = new ApplyChangesetTool(path, new ToolGate(), readOnly: false, policy);

        var result = await tool.HandleAsync(WriteChangeset(DeathChangeset), "all", CancellationToken.None);

        Assert.False(Structured(result).GetProperty("success").GetBoolean());
        Assert.Contains("policy:", Structured(result).GetProperty("errors")[0].GetString());
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Fact]
    public async Task Apply_WithoutAPolicy_StillWrites()
    {
        string path = WriteGed();
        var tool = new ApplyChangesetTool(path, new ToolGate(), readOnly: false);

        var result = await tool.HandleAsync(WriteChangeset(DeathChangeset), "all", CancellationToken.None);

        Assert.True(Structured(result).GetProperty("success").GetBoolean());
        Assert.Contains("DEAT", File.ReadAllText(path));
    }
}
