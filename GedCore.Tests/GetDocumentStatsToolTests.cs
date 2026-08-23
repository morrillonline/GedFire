using System.Text.Json;
using GedCore;
using GedFire.Gen;
using GedFire.Mcp;
using ModelContextProtocol.Protocol;

namespace GedCore.Tests;

public class GetDocumentStatsToolTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("gedfire-docstats-tests-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    static JsonElement StructuredContent(CallToolResult result) => result.StructuredContent!.Value;

    static string TextOf(CallToolResult result) => Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;

    // Goes through the real GedReader.ReadFile/ModelBuilder.Build path (the
    // same one DocumentSession uses) rather than the Ged55-only test helper,
    // because gedVersion accuracy depends on the real version-dispatch logic.
    GetDocumentStatsTool ToolOver(string gedText)
    {
        string path = Path.Combine(_dir, Guid.NewGuid() + ".ged");
        File.WriteAllText(path, gedText);
        var doc = GedReader.ReadFile(path);
        var model = ModelBuilder.Build(doc);
        var info = new FileInfo(path);
        var snapshot = new DocumentSnapshot(model, doc.Version, File.GetLastWriteTimeUtc(path), info.Length);
        var session = new DocumentSession(path, snapshot);
        return new GetDocumentStatsTool(session, new ToolGate());
    }

    const string ThreePersonsTwoFamiliesGed70 = """
        0 HEAD
        1 GEDC
        2 VERS 7.0
        0 @I1@ INDI
        1 NAME Frederick /Morrill/
        1 SEX M
        1 FAMS @F1@
        0 @I2@ INDI
        1 NAME Sarah /Blake/
        1 SEX F
        1 FAMS @F1@
        1 FAMC @F2@
        0 @I3@ INDI
        1 NAME Wyman /Morrill/
        1 SEX M
        1 FAMS @F2@
        0 @F1@ FAM
        1 HUSB @I1@
        1 WIFE @I2@
        0 @F2@ FAM
        1 HUSB @I3@
        0 TRLR

        """;

    [Fact]
    public async Task HandleAsync_ReturnsPersonAndFamilyCounts()
    {
        var tool = ToolOver(ThreePersonsTwoFamiliesGed70);
        var result = await tool.HandleAsync(CancellationToken.None);

        Assert.False(result.IsError);
        var root = StructuredContent(result);
        Assert.Equal(3, root.GetProperty("personCount").GetInt32());
        Assert.Equal(2, root.GetProperty("familyCount").GetInt32());
    }

    [Fact]
    public async Task HandleAsync_ReturnsDeclaredVersion_ForGedcom70()
    {
        var tool = ToolOver(ThreePersonsTwoFamiliesGed70);
        var result = await tool.HandleAsync(CancellationToken.None);

        Assert.Equal("7.0", StructuredContent(result).GetProperty("gedVersion").GetString());
    }

    [Fact]
    public async Task HandleAsync_ReturnsGedFireVersion()
    {
        var tool = ToolOver(ThreePersonsTwoFamiliesGed70);
        var result = await tool.HandleAsync(CancellationToken.None);

        string version = StructuredContent(result).GetProperty("gedFireVersion").GetString()!;
        Assert.False(string.IsNullOrWhiteSpace(version));
    }

    [Fact]
    public async Task HandleAsync_ReturnsDeclaredVersion_ForGedcom55()
    {
        const string ged55 = """
            0 HEAD
            1 GEDC
            2 VERS 5.5.1
            2 FORM LINEAGE-LINKED
            1 CHAR ANSI
            0 @I1@ INDI
            1 NAME Solo /Person/
            1 SEX M
            0 TRLR

            """;
        var tool = ToolOver(ged55);
        var result = await tool.HandleAsync(CancellationToken.None);

        var root = StructuredContent(result);
        Assert.Equal("5.5.1", root.GetProperty("gedVersion").GetString());
        Assert.Equal(1, root.GetProperty("personCount").GetInt32());
        Assert.Equal(0, root.GetProperty("familyCount").GetInt32());
    }

    [Fact]
    public async Task HandleAsync_ReturnsNullVersion_WhenHeaderDeclaresNone()
    {
        const string headerlessGed = """
            0 HEAD
            1 CHAR ANSI
            0 @I1@ INDI
            1 NAME Solo /Person/
            1 SEX M
            0 TRLR

            """;
        var tool = ToolOver(headerlessGed);
        var result = await tool.HandleAsync(CancellationToken.None);

        var root = StructuredContent(result);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("gedVersion").ValueKind);
        Assert.Equal(1, root.GetProperty("personCount").GetInt32());
    }

    [Fact]
    public async Task HandleAsync_EmptyDocument_ReturnsZeroCounts()
    {
        const string empty = """
            0 HEAD
            1 GEDC
            2 VERS 7.0
            0 TRLR

            """;
        var tool = ToolOver(empty);
        var result = await tool.HandleAsync(CancellationToken.None);

        var root = StructuredContent(result);
        Assert.Equal(0, root.GetProperty("personCount").GetInt32());
        Assert.Equal(0, root.GetProperty("familyCount").GetInt32());
    }

    [Fact]
    public async Task HandleAsync_TextFallback_MatchesStructuredContentCompactly()
    {
        var tool = ToolOver(ThreePersonsTwoFamiliesGed70);
        var result = await tool.HandleAsync(CancellationToken.None);

        string text = TextOf(result);
        Assert.Equal(
            JsonSerializer.Serialize(StructuredContent(result), CallToolResults.JsonOptions),
            text);
        Assert.DoesNotContain("\n", text);
    }

    [Fact]
    public async Task HandleAsync_ReloadFailure_ReturnsIsErrorWithExceptionDetails()
    {
        string path = Path.Combine(_dir, "gone.ged");
        File.WriteAllText(path, ThreePersonsTwoFamiliesGed70);
        var doc = GedReader.ReadFile(path);
        var model = ModelBuilder.Build(doc);
        var info = new FileInfo(path);
        var snapshot = new DocumentSnapshot(model, doc.Version, File.GetLastWriteTimeUtc(path), info.Length);
        var session = new DocumentSession(path, snapshot);
        var tool = new GetDocumentStatsTool(session, new ToolGate());
        File.Delete(path);

        var result = await tool.HandleAsync(CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Null(result.StructuredContent);
        Assert.Contains(nameof(DocumentReloadException), TextOf(result));
        Assert.Contains(path, TextOf(result));
    }

    [Fact]
    public async Task HandleAsync_RateLimitRejection_ReturnsIsErrorRatherThanThrowing()
    {
        var now = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        string path = Path.Combine(_dir, "gate.ged");
        File.WriteAllText(path, ThreePersonsTwoFamiliesGed70);
        var doc = GedReader.ReadFile(path);
        var model = ModelBuilder.Build(doc);
        var info = new FileInfo(path);
        var snapshot = new DocumentSnapshot(model, doc.Version, File.GetLastWriteTimeUtc(path), info.Length);
        var session = new DocumentSession(path, snapshot);
        var gate = new ToolGate(() => now);
        var tool = new GetDocumentStatsTool(session, gate);

        for (int i = 0; i < ToolGate.MaxCallsPerMinute; i++)
            await tool.HandleAsync(CancellationToken.None);

        var result = await tool.HandleAsync(CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains(nameof(ToolRateLimitExceededException), TextOf(result));
    }

    [Fact]
    public async Task HandleAsync_AlreadyCancelledToken_PropagatesRatherThanReturningIsError()
    {
        var tool = ToolOver(ThreePersonsTwoFamiliesGed70);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => tool.HandleAsync(cts.Token));
    }

    [Fact]
    public void ToMcpServerTool_DeclaresNameDescriptionSchemasAndAnnotations()
    {
        var tool = ToolOver(ThreePersonsTwoFamiliesGed70).ToMcpServerTool();

        Assert.Equal(GetDocumentStatsTool.ToolName, tool.ProtocolTool.Name);
        Assert.Equal(GetDocumentStatsTool.Description, tool.ProtocolTool.Description);
        Assert.True(tool.ProtocolTool.Annotations!.ReadOnlyHint);
        Assert.False(tool.ProtocolTool.Annotations!.DestructiveHint);
        Assert.True(tool.ProtocolTool.Annotations!.IdempotentHint);

        using var expectedInput = JsonDocument.Parse(GetDocumentStatsTool.InputSchemaJson);
        Assert.Equal(
            JsonSerializer.Serialize(expectedInput.RootElement),
            JsonSerializer.Serialize(tool.ProtocolTool.InputSchema));

        using var expectedOutput = JsonDocument.Parse(GetDocumentStatsTool.OutputSchemaJson);
        Assert.Equal(
            JsonSerializer.Serialize(expectedOutput.RootElement),
            JsonSerializer.Serialize(tool.ProtocolTool.OutputSchema!.Value));
    }
}
