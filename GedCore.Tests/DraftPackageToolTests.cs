using System.Security.Cryptography;
using System.Text.Json;
using GedFire.Mcp;
using ModelContextProtocol.Protocol;

namespace GedCore.Tests;

public class DraftPackageToolTests : IDisposable
{
    readonly TempDocumentSessions _sessions = new();
    readonly string _dir = Directory.CreateTempSubdirectory("gedfire-package-tests-").FullName;
    readonly string _out;
    readonly DraftChangesetTool _drafter;
    readonly DraftPackageTool _tool;

    public DraftPackageToolTests()
    {
        _out = Path.Combine(_dir, "out");
        Directory.CreateDirectory(_out);
        var session = _sessions.Open(Ged, out string path);
        _drafter = new DraftChangesetTool(session, new ToolGate());
        _tool = new DraftPackageTool(path, new ToolGate());
    }

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
        0 @I2@ INDI
        1 NAME Hiram /Giedd/
        1 SEX M
        0 TRLR
        """;

    static string Finding(string id, string anchor, string fact, string date) => $$"""
        [ { "id": "{{id}}", "extract": "extract of {{id}}",
            "source": { "name": "Find a Grave", "locator": "Memorial #{{id}}", "class": "derivative", "information": "primary" },
            "target": { "xref": "{{anchor}}", "card": "enrich" },
            "facts": [ { "record": "{{anchor}}", "fact": "{{fact}}", "date": "{{date}}", "place": "Rugby", "quay": 2 } ] } ]
        """;

    static JsonElement Json(object value) => JsonSerializer.SerializeToElement(value);

    // A changeset written by draft_changeset, as a finished one would arrive.
    async Task<string> Drafted(string name, string findings)
    {
        string path = Path.Combine(_dir, name);
        var result = await _drafter.HandleAsync(
            JsonDocument.Parse(findings).RootElement.Clone(), Json(path), default, CancellationToken.None);
        Assert.True(Structured(result).GetProperty("success").GetBoolean(), Structured(result).GetRawText());
        return path;
    }

    string Write(string name, string content)
    {
        string path = Path.Combine(_dir, name);
        File.WriteAllText(path, content);
        return path;
    }

    Task<CallToolResult> Call(string[] changesets, bool overwrite = false, string proposal = "W67-test") =>
        _tool.HandleAsync(
            new DraftPackageArguments(Json(changesets), Json(_out), Json(proposal), overwrite ? Json(true) : default), CancellationToken.None);

    static JsonElement Structured(CallToolResult result) => result.StructuredContent!.Value;

    static string Text(CallToolResult result) => Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;

    [Fact]
    public async Task FinishedChangesets_AreWrittenAsOneNumberedProposalAndLeftUntouched()
    {
        string a = await Drafted("a.changes.json", Finding("F-1", "@I1@", "DEAT", "26 Mar 2013"));
        string b = await Drafted("b.changes.json", Finding("F-2", "@I2@", "BIRT", "1 Jan 1900"));
        byte[] aBytes = File.ReadAllBytes(a);

        var structured = Structured(await Call([a, b]));

        Assert.True(structured.GetProperty("success").GetBoolean(), structured.GetRawText());
        string proposal = structured.GetProperty("proposalPath").GetString()!;
        Assert.Equal(Path.Combine(_out, "W67-test.md"), proposal);
        Assert.Equal(proposal, Assert.Single(Directory.GetFiles(_out)));
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(File.ReadAllBytes(proposal))), structured.GetProperty("proposalSha256").GetString());
        var changesets = structured.GetProperty("changesets").EnumerateArray().ToList();
        Assert.Equal([(1, a), (2, b)], changesets.Select(c => (c.GetProperty("number").GetInt32(), c.GetProperty("path").GetString()!)));
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(aBytes)), changesets[0].GetProperty("sha256").GetString());
        Assert.Equal(aBytes, File.ReadAllBytes(a));

        string markdown = File.ReadAllText(proposal);
        Assert.Contains("## Changeset 1: a.changes.json", markdown);
        Assert.Contains("## Changeset 2: b.changes.json", markdown);
        Assert.Contains("> extract of F-1", markdown);
        Assert.Contains("> extract of F-2", markdown);
        Assert.Contains("Source class: derivative", markdown);
        Assert.Contains("QUAY 2", markdown);
    }

    [Fact]
    public async Task TheArgumentACorrelatedFindingCarries_ReachesTheProposalFromTheChangesetAlone()
    {
        string findings = Finding("F-1", "@I1@", "DEAT", "26 Mar 2013").Replace(
            "\"id\": \"F-1\",",
            "\"id\": \"F-1\", \"argument\": { \"anchors\": [\"Wife of Hiram.\"], \"exclusions\": [ { \"name\": \"Gladys Giedd\", \"xref\": \"@I9@\", \"fact\": \"a death in 1801\", \"finding\": \"F-7\" } ], \"cautions\": \"Spelling varies.\", \"difficulty\": \"high\" },");
        string changeset = await Drafted("a.changes.json", findings);

        string proposal = Structured(await Call([changeset])).GetProperty("proposalPath").GetString()!;

        string markdown = File.ReadAllText(proposal).ReplaceLineEndings("\n");
        Assert.Contains("#### Why him\n\n- Wife of Hiram.\n", markdown);
        Assert.Contains("- Gladys Giedd (@I9@) is excluded by a death in 1801 (finding F-7).", markdown);
        Assert.Contains("#### Conflicts and cautions\n\n- Spelling varies.\n", markdown);
        Assert.Contains("#### Difficulty\n\nhigh\n", markdown);
    }

    [Fact]
    public async Task AChangesetThatCarriesArguments_StillValidatesAgainstTheDocument()
    {
        string findings = Finding("F-1", "@I1@", "DEAT", "26 Mar 2013").Replace(
            "\"id\": \"F-1\",", "\"id\": \"F-1\", \"argument\": { \"anchors\": [\"Wife of Hiram.\"] },");
        string changeset = await Drafted("a.changes.json", findings);

        var result = Structured(await Call([changeset]));

        Assert.True(result.GetProperty("success").GetBoolean(), result.GetRawText());
    }

    [Fact]
    public async Task AChangesetTheDocumentRejects_FailsTheCallNamingItAndWritesNothing()
    {
        string good = await Drafted("good.changes.json", Finding("F-1", "@I1@", "DEAT", "26 Mar 2013"));
        string missingPerson = Write("bad.changes.json", """
            { "items": [ { "item": 1, "ops": [ { "op": "createOrUpdateVital", "record": "@I99@", "fact": "DEAT", "value": { "date": "1900" }, "findings": ["F-9"] } ] } ],
              "findings": [ { "id": "F-9", "item": 1, "extract": "x", "source": { "name": "S", "locator": "p", "class": "derivative", "information": "primary" }, "claims": [] } ] }
            """);

        var structured = Structured(await Call([good, missingPerson]));

        Assert.False(structured.GetProperty("success").GetBoolean());
        Assert.Equal(JsonValueKind.Null, structured.GetProperty("proposalPath").ValueKind);
        var changesets = structured.GetProperty("changesets").EnumerateArray().ToList();
        Assert.Equal([true, false], changesets.Select(c => c.GetProperty("success").GetBoolean()));
        Assert.All(changesets[1].GetProperty("errors").EnumerateArray(), e => Assert.StartsWith(missingPerson, e.GetString()));
        Assert.NotEmpty(changesets[1].GetProperty("errors").EnumerateArray());
        Assert.Empty(Directory.GetFiles(_out));
    }

    [Fact]
    public async Task AChangesetThatCarriesNoFindings_IsNotPresented()
    {
        string bare = Write("bare.changes.json", """
            { "items": [ { "item": 1, "ops": [ { "op": "createOrUpdateVital", "record": "@I1@", "fact": "DEAT", "value": { "date": "1900" } } ] } ] }
            """);

        var structured = Structured(await Call([bare]));

        Assert.False(structured.GetProperty("success").GetBoolean());
        Assert.Contains("bare.changes.json carries no findings", Assert.Single(structured.GetProperty("errors").EnumerateArray()).GetString());
        Assert.Empty(Directory.GetFiles(_out));
    }

    [Fact]
    public async Task AChangesetThatIsNotJson_FailsNamingTheFile()
    {
        string broken = Write("broken.changes.json", "{ nope");

        var changeset = Structured(await Call([broken])).GetProperty("changesets")[0];

        Assert.False(changeset.GetProperty("success").GetBoolean());
        Assert.Contains("broken.changes.json is not a readable changeset", changeset.GetProperty("errors")[0].GetString());
    }

    [Fact]
    public async Task AMissingChangesetFile_FailsNamingItWithNoHash()
    {
        string missing = Path.Combine(_dir, "missing.changes.json");

        var changeset = Structured(await Call([missing])).GetProperty("changesets")[0];

        Assert.False(changeset.GetProperty("success").GetBoolean());
        Assert.Equal(JsonValueKind.Null, changeset.GetProperty("sha256").ValueKind);
        Assert.Contains("could not be read", changeset.GetProperty("errors")[0].GetString());
    }

    [Fact]
    public async Task AnExistingProposal_IsKeptUnlessOverwriteIsTrue()
    {
        string a = await Drafted("a.changes.json", Finding("F-1", "@I1@", "DEAT", "26 Mar 2013"));
        string target = Path.Combine(_out, "W67-test.md");
        File.WriteAllText(target, "keep me");

        var refused = Structured(await Call([a]));
        Assert.False(refused.GetProperty("success").GetBoolean());
        Assert.Contains("already exists", Assert.Single(refused.GetProperty("errors").EnumerateArray()).GetString());
        Assert.Equal("keep me", File.ReadAllText(target));

        var replaced = Structured(await Call([a], overwrite: true));
        Assert.True(replaced.GetProperty("success").GetBoolean());
        Assert.Contains("# Proposal: W67-test", File.ReadAllText(target));
    }

    [Fact]
    public async Task TheSameChangesetsTwice_GiveTheSameProposalHash()
    {
        string a = await Drafted("a.changes.json", Finding("F-1", "@I1@", "DEAT", "26 Mar 2013"));

        string first = Structured(await Call([a])).GetProperty("proposalSha256").GetString()!;
        string second = Structured(await Call([a], overwrite: true)).GetProperty("proposalSha256").GetString()!;

        Assert.Equal(first, second);
    }

    [Fact]
    public async Task ABadRequest_IsRefusedNamingTheField()
    {
        string a = await Drafted("a.changes.json", Finding("F-1", "@I1@", "DEAT", "26 Mar 2013"));

        var noFolder = await _tool.HandleAsync(new DraftPackageArguments(Json(new[] { a }), default, Json("p"), default), CancellationToken.None);
        var missingFolder = await _tool.HandleAsync(new DraftPackageArguments(Json(new[] { a }), Json(Path.Combine(_dir, "nope")), Json("p"), default), CancellationToken.None);
        var badName = await Call([a], proposal: "a/b");
        var noPaths = await _tool.HandleAsync(new DraftPackageArguments(default, Json(_out), Json("p"), default), CancellationToken.None);
        var emptyPaths = await Call([]);
        var twice = await Call([a, a]);

        Assert.All(new[] { noFolder, missingFolder, badName, noPaths, emptyPaths, twice }, r => Assert.True(r.IsError));
        Assert.Contains("folder is required", Text(noFolder));
        Assert.Contains("does not exist", Text(missingFolder));
        Assert.Contains("proposal", Text(badName));
        Assert.Contains("changesetPaths is required", Text(noPaths));
        Assert.Contains("at least one changeset", Text(emptyPaths));
        Assert.Contains("more than once", Text(twice));
        Assert.Empty(Directory.GetFiles(_out));
    }

    [Fact]
    public void TheInputSchema_TakesChangesetsNotFindings()
    {
        using var schema = JsonDocument.Parse(DraftPackageTool.InputSchemaJson);

        string[] properties = [.. schema.RootElement.GetProperty("properties").EnumerateObject().Select(p => p.Name)];
        Assert.Equal(["changesetPaths", "folder", "proposal", "overwrite"], properties);
    }

    [Fact]
    public void TheOutputSchema_NamesTheFieldsTheResultCarries()
    {
        using var schema = JsonDocument.Parse(DraftPackageTool.OutputSchemaJson);

        string[] required = [.. schema.RootElement.GetProperty("required").EnumerateArray().Select(r => r.GetString()!)];
        Assert.Equal(["success", "proposalPath", "proposalSha256", "changesets", "errors"], required);
        var changeset = schema.RootElement.GetProperty("properties").GetProperty("changesets").GetProperty("items");
        Assert.Contains("sha256", changeset.GetProperty("required").EnumerateArray().Select(r => r.GetString()));
    }
}
