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
        var result = await Tool().HandleAsync(Arg(Findings), Arg(null), CancellationToken.None);

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
        var result = await Tool().HandleAsync(Arg(Findings), Arg(null), CancellationToken.None);

        var parsed = GedCore.Apply.Changeset.Parse(Structured(result).GetProperty("changeset").GetRawText());
        Assert.Single(parsed.Items);
    }

    [Fact]
    public async Task OutPath_WritesTheChangesetToANewFile()
    {
        string path = Path.Combine(_dir, "out.json");

        var result = await Tool().HandleAsync(Arg(Findings), Arg(JsonSerializer.Serialize(path)), CancellationToken.None);

        Assert.Equal(path, Structured(result).GetProperty("writtenTo").GetString());
        Assert.Contains("createOrUpdateVital", File.ReadAllText(path));
    }

    [Fact]
    public async Task OutPath_NeverOverwritesAnExistingFile()
    {
        string path = Path.Combine(_dir, "existing.json");
        File.WriteAllText(path, "keep me");

        var result = await Tool().HandleAsync(Arg(Findings), Arg(JsonSerializer.Serialize(path)), CancellationToken.None);

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

        var result = await Tool().HandleAsync(Arg(tooHigh), Arg(JsonSerializer.Serialize(path)), CancellationToken.None);

        Assert.False(Structured(result).GetProperty("success").GetBoolean());
        Assert.False(File.Exists(path));
    }

    [Fact]
    public async Task AMalformedFinding_IsRejectedByFieldPath()
    {
        var result = await Tool().HandleAsync(Arg("""[ { "id": "F-1" } ]"""), Arg(null), CancellationToken.None);

        var errors = Structured(result).GetProperty("errors").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Contains("findings[0].extract is required.", errors);
        Assert.Equal(JsonValueKind.Null, Structured(result).GetProperty("changeset").ValueKind);
    }

    [Fact]
    public async Task ATargetNameThatDiffersFromTheDocument_IsRefused()
    {
        string wrong = Findings.Replace("Gladys Giedd", "Gladis Giedd");

        var result = await Tool().HandleAsync(Arg(wrong), Arg(null), CancellationToken.None);

        Assert.Contains("findings[0].target.name", Structured(result).GetProperty("errors")[0].GetString());
    }

    [Fact]
    public async Task TheServersPolicy_IsAppliedToTheDraft()
    {
        var policy = ChangesetPolicy.Load(new MemoryStream(Encoding.UTF8.GetBytes(
            """{ "verificationNote": { "maxQuay": 2, "notePrefix": "needs independent verification:" } }""")));

        var result = await Tool(policy).HandleAsync(Arg(Findings), Arg(null), CancellationToken.None);

        var ops = Structured(result).GetProperty("changeset").GetProperty("items")[0].GetProperty("ops");
        Assert.Equal("createOrUpdateNote", ops[1].GetProperty("op").GetString());
        Assert.Contains("Gladys Giedd", ops[1].GetProperty("text").GetString());
    }

    [Fact]
    public async Task ABlankOutPath_IsRefused()
    {
        var result = await Tool().HandleAsync(Arg(Findings), Arg("\"  \""), CancellationToken.None);

        Assert.True(result.IsError);
    }
}
