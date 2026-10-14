using System.Text;
using System.Text.Json;
using GedCore.Policy;
using GedFire.Mcp;
using ModelContextProtocol.Protocol;

namespace GedCore.Tests;

public class DraftChangesetToolTests : IDisposable
{
    readonly TempDocumentSessions _sessions = new();
    readonly string _dir = Directory.CreateTempSubdirectory("gedfire-draft-tests-").FullName;

    public void Dispose()
    {
        _sessions.Dispose();
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    const string Ged = """
        0 HEAD
        1 GEDC
        2 VERS 7.0
        0 @I1@ INDI
        1 NAME Gladys /Giedd/
        1 SEX F
        0 @S1@ SOUR
        1 TITL Find a Grave
        0 TRLR
        """;

    const string Findings = """
        [ { "id": "F-1", "extract": "DEATH 26 Mar 2013",
            "source": { "name": "Find a Grave", "locator": "Memorial #1", "class": "derivative", "information": "primary" },
            "target": { "xref": "@I1@", "card": "enrich-death", "name": "Gladys Giedd" },
            "facts": [ { "record": "@I1@", "fact": "DEAT", "date": "26 Mar 2013", "place": "Rugby", "quay": 2 } ] } ]
        """;

    DraftChangesetTool Tool(ChangesetPolicy? policy = null) => new(_sessions.Open(Ged), new ToolGate(), policy);

    static JsonElement Arg(string? json) => json is null ? default : JsonDocument.Parse(json).RootElement.Clone();

    static JsonElement Structured(CallToolResult result) => result.StructuredContent!.Value;

    [Fact]
    public async Task ADraft_ReusesTheExistingSourceAndReturnsTheChangeset()
    {
        var result = await Tool().HandleAsync(Arg(Findings), Arg(null), default, CancellationToken.None);

        var structured = Structured(result);
        Assert.True(structured.GetProperty("success").GetBoolean());
        var op = structured.GetProperty("changeset").GetProperty("items")[0].GetProperty("ops")[0];
        Assert.Equal("@S1@", op.GetProperty("citation").GetProperty("source").GetString());
        Assert.Equal("26 MAR 2013", op.GetProperty("value").GetProperty("date").GetString());
        Assert.Null(structured.GetProperty("writtenTo").GetString());
    }

    [Fact]
    public async Task ADraft_IsAcceptedByTheDialectsReader()
    {
        var result = await Tool().HandleAsync(Arg(Findings), Arg(null), default, CancellationToken.None);

        var parsed = GedCore.Apply.Changeset.Parse(Structured(result).GetProperty("changeset").GetRawText());
        Assert.Single(parsed.Items);
    }

    [Fact]
    public async Task OutPath_WritesTheChangesetToANewFile()
    {
        string path = Path.Combine(_dir, "out.json");

        var result = await Tool().HandleAsync(Arg(Findings), Arg(JsonSerializer.Serialize(path)), default, CancellationToken.None);

        Assert.Equal(path, Structured(result).GetProperty("writtenTo").GetString());
        Assert.Contains("createOrUpdateVital", File.ReadAllText(path));
    }

    [Fact]
    public async Task OutPath_NeverOverwritesAnExistingFile()
    {
        string path = Path.Combine(_dir, "existing.json");
        File.WriteAllText(path, "keep me");

        var result = await Tool().HandleAsync(Arg(Findings), Arg(JsonSerializer.Serialize(path)), default, CancellationToken.None);

        var structured = Structured(result);
        Assert.False(structured.GetProperty("success").GetBoolean());
        Assert.Contains("already exists", structured.GetProperty("errors")[0].GetString());
        Assert.Equal("keep me", File.ReadAllText(path));
    }

    [Fact]
    public async Task ADraftWithErrors_IsNotWritten()
    {
        string path = Path.Combine(_dir, "bad.json");
        string tooHigh = Findings.Replace("\"quay\": 2", "\"quay\": 3");

        var result = await Tool().HandleAsync(Arg(tooHigh), Arg(JsonSerializer.Serialize(path)), default, CancellationToken.None);

        Assert.False(Structured(result).GetProperty("success").GetBoolean());
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task AMalformedFinding_IsRejectedByFieldPath()
    {
        var result = await Tool().HandleAsync(Arg("""[ { "id": "F-1" } ]"""), Arg(null), default, CancellationToken.None);

        var errors = Structured(result).GetProperty("errors").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Contains("findings[0].extract is required.", errors);
        Assert.Equal(JsonValueKind.Null, Structured(result).GetProperty("changeset").ValueKind);
    }

    [Fact]
    public async Task ATargetNameThatDiffersFromTheDocument_IsRefused()
    {
        string wrong = Findings.Replace("Gladys Giedd", "Gladis Giedd");

        var result = await Tool().HandleAsync(Arg(wrong), Arg(null), default, CancellationToken.None);

        Assert.Contains("findings[0].target.name", Structured(result).GetProperty("errors")[0].GetString());
    }

    [Fact]
    public async Task TheServersPolicy_IsAppliedToTheDraft()
    {
        var policy = ChangesetPolicy.Load(new MemoryStream(Encoding.UTF8.GetBytes(
            """{ "verificationNote": { "maxQuay": 2, "notePrefix": "needs independent verification:" } }""")));

        var result = await Tool(policy).HandleAsync(Arg(Findings), Arg(null), default, CancellationToken.None);

        var ops = Structured(result).GetProperty("changeset").GetProperty("items")[0].GetProperty("ops");
        Assert.Equal("createOrUpdateNote", ops[1].GetProperty("op").GetString());
        Assert.Contains("Gladys Giedd", ops[1].GetProperty("text").GetString());
    }

    [Fact]
    public async Task InputPath_DraftsTheFindingsSavedInAFile()
    {
        string input = Path.Combine(_dir, "draft-input.json");
        File.WriteAllText(input, "{ \"findings\": " + Findings + " }");

        var result = await Tool().HandleAsync(default, Arg(JsonSerializer.Serialize(input)), default, default, CancellationToken.None);

        var structured = Structured(result);
        Assert.True(structured.GetProperty("success").GetBoolean());
        Assert.Equal("@S1@", structured.GetProperty("changeset").GetProperty("items")[0].GetProperty("ops")[0]
            .GetProperty("citation").GetProperty("source").GetString());
    }

    [Fact]
    public async Task FindingsAndInputPathTogether_AreRefused()
    {
        var result = await Tool().HandleAsync(Arg(Findings), Arg("\"draft-input.json\""), default, default, CancellationToken.None);

        Assert.True(result.IsError);
    }

    [Fact]
    public async Task AnInputPathFileWithoutFindings_IsRefusedByName()
    {
        string input = Path.Combine(_dir, "bad-input.json");
        File.WriteAllText(input, "{ \"cards\": [] }");

        var result = await Tool().HandleAsync(default, Arg(JsonSerializer.Serialize(input)), default, default, CancellationToken.None);

        Assert.True(result.IsError);
    }

    [Fact]
    public async Task AMoveFromAFamilyThatDoesNotListTheChild_IsRefusedAgainstTheDocument()
    {
        const string family = """
            0 HEAD
            1 GEDC
            2 VERS 7.0
            0 @I1@ INDI
            1 NAME Gladys /Giedd/
            1 FAMC @F1@
            0 @I2@ INDI
            1 NAME Hiram /Giedd/
            1 FAMS @F1@
            0 @F1@ FAM
            1 HUSB @I2@
            1 CHIL @I1@
            0 @F2@ FAM
            1 HUSB @I2@
            0 @F3@ FAM
            1 HUSB @I2@
            0 TRLR
            """;
        string finding(string from) => $$"""
            [ { "id": "F-1", "extract": "daughter of Hiram",
                "source": { "name": "Find a Grave", "locator": "Memorial #1", "class": "derivative", "information": "primary" },
                "target": { "xref": "@I1@" },
                "relations": [ { "kind": "child", "family": "@F2@", "child": "@I1@", "from": "{{from}}", "quay": 2 } ] } ]
            """;
        var tool = new DraftChangesetTool(_sessions.Open(family), new ToolGate());

        var moved = Structured(await tool.HandleAsync(Arg(finding("@F1@")), Arg(null), default, CancellationToken.None));
        var refused = Structured(await tool.HandleAsync(Arg(finding("@F3@")), Arg(null), default, CancellationToken.None));

        Assert.True(moved.GetProperty("success").GetBoolean());
        Assert.False(refused.GetProperty("success").GetBoolean());
        Assert.Contains("is not a child of @F3@", refused.GetProperty("errors")[0].GetString());
    }

    [Fact]
    public async Task ABlankOutPath_IsRefused()
    {
        var result = await Tool().HandleAsync(Arg(Findings), Arg("\"  \""), default, CancellationToken.None);

        Assert.True(result.IsError);
    }

    [Fact]
    public async Task OutPath_ReplacesAnExistingFileWhenOverwriteIsTrue()
    {
        string path = Path.Combine(_dir, "stale.json");
        File.WriteAllText(path, "stale");

        var result = await Tool().HandleAsync(Arg(Findings), Arg(JsonSerializer.Serialize(path)), Arg("true"), CancellationToken.None);

        Assert.True(Structured(result).GetProperty("success").GetBoolean());
        Assert.Contains("createOrUpdateVital", File.ReadAllText(path));
    }

    [Fact]
    public async Task ANonBooleanOverwrite_IsRefused()
    {
        var result = await Tool().HandleAsync(Arg(Findings), Arg(null), Arg("\"yes\""), CancellationToken.None);

        Assert.True(result.IsError);
    }

    [Fact]
    public void TheOutputSchemaListsEveryDecisionKind()
    {
        using var schema = JsonDocument.Parse(DraftChangesetTool.OutputSchemaJson);

        var listed = schema.RootElement.GetProperty("properties").GetProperty("decisions").GetProperty("items")
            .GetProperty("properties").GetProperty("kind").GetProperty("enum").EnumerateArray().Select(k => k.GetString());

        Assert.Equal(GedCore.Drafting.DraftDecisionKinds.All.Order(), listed.Order());
    }

    [Fact]
    public async Task ADraftThatRestatesAnExistingCitation_ReturnsItsKeptCitationDecision()
    {
        string cited = Ged.Replace("1 SEX F", "1 SEX F\n1 DEAT\n2 DATE 1 JAN 2000\n2 SOUR @S1@\n3 PAGE Memorial #0");
        var tool = new DraftChangesetTool(_sessions.Open(cited), new ToolGate());

        var result = await tool.HandleAsync(Arg(Findings), Arg(null), default, CancellationToken.None);

        var kinds = Structured(result).GetProperty("decisions").EnumerateArray().Select(d => d.GetProperty("kind").GetString());
        Assert.Contains("kept-citation", kinds);
    }

    const string RichFindings = """
        [ { "id": "F-1", "extract": "Gladys Giedd, wife Mary, child Ann.",
            "source": { "name": "Find a Grave", "locator": "Memorial #1", "class": "derivative", "information": "primary" },
            "target": { "xref": "@I1@", "name": "Gladys Giedd" },
            "people": [ { "xref": "@NewI1@", "name": "Mary /Giedd/", "sex": "M" }, { "xref": "@NewI2@", "name": "Ann /Giedd/" } ],
            "relations": [
              { "kind": "spouse", "person": "@I1@", "spouse": "@NewI1@", "family": "@NewF1@", "quay": 2, "excerpt": "wife Mary", "notDuplicateOf": [ "@I1@" ] },
              { "kind": "child", "family": "@NewF1@", "child": "@NewI2@", "quay": 2, "excerpt": "child Ann" } ],
            "facts": [
              { "record": "@NewF1@", "fact": "MARR", "date": "1900", "quay": 2, "excerpt": "wife Mary" },
              { "record": "@I1@", "fact": "NAME", "value": "Gladys /Giedd/", "quay": 2 },
              { "record": "@I1@", "fact": "EVEN", "date": "1950", "quay": 2, "substructures": [ { "tag": "TYPE", "value": "Pension" } ] },
              { "record": "@I1@", "fact": "DEAT", "quay": 2, "citeOnly": true } ],
            "notes": [ { "record": "@I1@", "text": "A cited note.", "cites": [ { "quay": 2 } ] } ] } ]
        """;

    [Fact]
    public async Task EveryNewInputField_ReachesTheChangesetThroughTheTool()
    {
        var result = await Tool().HandleAsync(Arg(RichFindings), Arg(null), default, CancellationToken.None);

        var structured = Structured(result);
        Assert.True(structured.GetProperty("success").GetBoolean(), structured.GetProperty("errors").GetRawText());
        var ops = structured.GetProperty("changeset").GetProperty("items")[0].GetProperty("ops").EnumerateArray().ToList();
        var verbs = ops.Select(o => o.GetProperty("op").GetString()).ToList();
        Assert.Contains("createOrUpdateSpouse", verbs);
        Assert.Contains("createOrUpdateChild", verbs);
        Assert.Contains("createOrUpdateCitation", verbs);
        Assert.Contains(ops, o => o.TryGetProperty("substructures", out _));
        Assert.Contains(ops, o => o.TryGetProperty("notDuplicateOf", out _));
        Assert.Contains(ops, o => o.TryGetProperty("value", out var v) && v.ValueKind == JsonValueKind.String);
        var parsed = GedCore.Apply.Changeset.Parse(structured.GetProperty("changeset").GetRawText());
        Assert.Single(parsed.Items);
    }
}
