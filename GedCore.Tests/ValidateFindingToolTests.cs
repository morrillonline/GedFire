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
