using System.Text.Json;
using GedFire.Mcp;
using ModelContextProtocol.Protocol;

namespace GedCore.Tests;

public class ValidateFindingToolTests : IDisposable
{
    readonly TempDocumentSessions _sessions = new();
    readonly string _dir = Directory.CreateTempSubdirectory("gedfire-validate-finding-tests-").FullName;

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
        1 NAME Ezra /Quillfeather/
        1 SEX M
        0 @S1@ SOUR
        1 TITL Fictional County Register
        0 TRLR
        """;

    static JsonElement Arg(string json) => JsonDocument.Parse(json).RootElement.Clone();

    static JsonElement Structured(CallToolResult result) => result.StructuredContent!.Value;

    string WriteFixture(string name)
    {
        string path = Path.Combine(_dir, name);
        File.WriteAllText(path, FindingFixtures.Read(name));
        return path;
    }

    static ValidateFindingArguments Paths(bool requireDraft, params string[] paths) =>
        new(Arg(JsonSerializer.Serialize(paths)), default, default, Arg(requireDraft ? "true" : "false"));

    public static IEnumerable<object[]> Fixtures() => FindingFixtures.Names.Select(name => new object[] { name });

    [Theory]
    [MemberData(nameof(Fixtures))]
    public async Task ReadyToDraft_IsExactlyWhenDraftChangesetCompilesTheFile_WithTheSameErrors(string name)
    {
        var session = _sessions.Open(Ged);
        string path = WriteFixture(name);

        var validated = Structured(await new ValidateFindingTool(session, new ToolGate()).HandleAsync(Paths(true, path), CancellationToken.None));
        var drafted = Structured(await new DraftChangesetTool(session, new ToolGate()).HandleAsync(
            new DraftChangesetArguments(default, default, Arg(JsonSerializer.Serialize(new[] { path })), default, default), CancellationToken.None));

        var finding = validated.GetProperty("findings")[0];
        Assert.Equal(drafted.GetProperty("success").GetBoolean(), finding.GetProperty("readyToDraft").GetBoolean());
        Assert.Equal(
            drafted.GetProperty("errors").EnumerateArray().Select(e => e.GetString()),
            finding.GetProperty("errors").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public async Task EachFileIsReported_AndSuccessNeedsEveryOneValid()
    {
        var tool = new ValidateFindingTool(_sessions.Open(Ged), new ToolGate());

        var result = Structured(await tool.HandleAsync(Paths(false, WriteFixture("F-100.json"), WriteFixture("F-103.json")), CancellationToken.None));

        Assert.False(result.GetProperty("success").GetBoolean());
        Assert.True(result.GetProperty("documentChecked").GetBoolean());
        var findings = result.GetProperty("findings");
        Assert.Equal(2, findings.GetArrayLength());
        Assert.EndsWith("F-100.json", findings[0].GetProperty("file").GetString());
        Assert.True(findings[0].GetProperty("valid").GetBoolean());
        Assert.False(findings[1].GetProperty("valid").GetBoolean());
    }

    [Fact]
    public async Task WithoutADocument_TheShapeAloneIsChecked()
    {
        var result = Structured(await new ValidateFindingTool(null, new ToolGate()).HandleAsync(Paths(false, WriteFixture("F-104.json")), CancellationToken.None));

        Assert.True(result.GetProperty("success").GetBoolean());
        Assert.False(result.GetProperty("documentChecked").GetBoolean());
        Assert.False(result.GetProperty("findings")[0].GetProperty("bindings")[0].TryGetProperty("exists", out _));
    }

    [Fact]
    public async Task FindingsEntries_AreCheckedLikeDraftChangesetsFindings()
    {
        using var file = JsonDocument.Parse(FindingFixtures.Read("F-104.json"));
        string entries = "[" + file.RootElement.GetProperty("draft").GetRawText() + "]";

        var result = Structured(await new ValidateFindingTool(_sessions.Open(Ged), new ToolGate()).HandleAsync(
            new ValidateFindingArguments(default, Arg(entries), default, default), CancellationToken.None));

        var finding = result.GetProperty("findings")[0];
        Assert.Equal(JsonValueKind.Null, finding.GetProperty("file").ValueKind);
        Assert.Equal("findings[0].target.xref @I99@ is not a record in the document.", finding.GetProperty("errors")[0].GetString());
    }

    [Fact]
    public async Task ABatchWhoseNoteCitesAnotherFileOfIt_IsValid_AsDraftChangesetCompilesIt()
    {
        var session = _sessions.Open(Ged);
        string[] paths = [WriteFixture("F-105.json"), WriteFixture("F-106.json")];

        var validated = Structured(await new ValidateFindingTool(session, new ToolGate()).HandleAsync(Paths(true, paths), CancellationToken.None));
        var drafted = Structured(await new DraftChangesetTool(session, new ToolGate()).HandleAsync(
            new DraftChangesetArguments(default, default, Arg(JsonSerializer.Serialize(paths)), default, default), CancellationToken.None));

        Assert.True(drafted.GetProperty("success").GetBoolean());
        Assert.True(validated.GetProperty("success").GetBoolean(), validated.ToString());
        Assert.All(validated.GetProperty("findings").EnumerateArray(), f => Assert.True(f.GetProperty("readyToDraft").GetBoolean()));
    }

    [Fact]
    public async Task ASingleFileWhoseNoteCitesTheFileBesideIt_IsValid()
    {
        WriteFixture("F-105.json");

        var result = Structured(await new ValidateFindingTool(null, new ToolGate()).HandleAsync(Paths(true, WriteFixture("F-106.json")), CancellationToken.None));

        Assert.True(result.GetProperty("success").GetBoolean(), result.ToString());
    }

    [Fact]
    public async Task FindingsEntriesMayCiteEachOther()
    {
        string entries = "[" + string.Join(",", new[] { "F-105.json", "F-106.json" }.Select(name =>
            JsonDocument.Parse(FindingFixtures.Read(name)).RootElement.GetProperty("draft").GetRawText())) + "]";

        var result = Structured(await new ValidateFindingTool(_sessions.Open(Ged), new ToolGate()).HandleAsync(
            new ValidateFindingArguments(default, Arg(entries), default, default), CancellationToken.None));

        Assert.True(result.GetProperty("success").GetBoolean(), result.ToString());
    }

    [Fact]
    public async Task GarbledTextInAFinding_IsRefusedAtItsPath_WhileForeignTextPasses()
    {
        const string entries = """
            [ { "id": "F-1", "extract": "Ezra Quillfeather, born in Łódź; Événement à Århus Ã¢â‚¬Â¦ José Núñez …",
                "source": { "name": "Fictional County Register", "locator": "p. 9", "class": "original", "information": "primary" },
                "target": { "xref": "@I1@" },
                "facts": [ { "record": "@I1@", "fact": "BIRT", "date": "4 May 1801", "place": "Łódź, Polska", "quay": 3 } ] } ]
            """;

        var result = Structured(await new ValidateFindingTool(_sessions.Open(Ged), new ToolGate()).HandleAsync(
            new ValidateFindingArguments(default, Arg(entries), default, default), CancellationToken.None));

        Assert.Equal(
            "findings[0].extract has garbled characters \"Ã¢â‚¬Â¦\" (UTF-8 text misread as Windows-1252); it should read \"…\".",
            Assert.Single(result.GetProperty("findings")[0].GetProperty("errors").EnumerateArray()).GetString());
    }

    [Fact]
    public async Task AFamilyEventOnAPerson_IsRefused_NamingThePath()
    {
        const string entries = """
            [ { "id": "F-1", "extract": "Married 4 May 1901 at Elmford",
                "source": { "name": "Fictional County Register", "locator": "p. 9", "class": "original", "information": "primary" },
                "target": { "xref": "@I1@" },
                "facts": [ { "record": "@I1@", "fact": "MARR", "date": "4 May 1901", "quay": 3 } ] } ]
            """;

        var result = Structured(await new ValidateFindingTool(_sessions.Open(Ged), new ToolGate()).HandleAsync(
            new ValidateFindingArguments(default, Arg(entries), default, default), CancellationToken.None));

        Assert.False(result.GetProperty("success").GetBoolean());
        Assert.Contains(result.GetProperty("findings")[0].GetProperty("errors").EnumerateArray(), e =>
            e.GetString()!.StartsWith("findings[0].facts[0].record @I1@ is a person, but MARR is a family event"));
    }

    [Fact]
    public async Task FindingPathsWithFindings_IsRefused()
    {
        var result = await new ValidateFindingTool(null, new ToolGate()).HandleAsync(
            new ValidateFindingArguments(Arg("[\"a.json\"]"), Arg("[{}]"), default, default), CancellationToken.None);

        Assert.True(result.IsError);
    }

    [Fact]
    public async Task NoFindings_IsRefused()
    {
        var result = await new ValidateFindingTool(null, new ToolGate()).HandleAsync(
            new ValidateFindingArguments(default, default, default, default), CancellationToken.None);

        Assert.True(result.IsError);
    }

    [Fact]
    public async Task ValidatingNeverWritesTheDocument()
    {
        var session = _sessions.Open(Ged, out string gedPath);
        byte[] before = File.ReadAllBytes(gedPath);

        await new ValidateFindingTool(session, new ToolGate()).HandleAsync(Paths(false, WriteFixture("F-100.json")), CancellationToken.None);

        Assert.Equal(before, File.ReadAllBytes(gedPath));
    }
}
