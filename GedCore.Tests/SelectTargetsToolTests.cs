using System.Text.Json;
using GedFire.Mcp;
using ModelContextProtocol.Protocol;

namespace GedCore.Tests;

public class SelectTargetsToolTests : IDisposable
{
    readonly TempDocumentSessions _sessions = new();

    public void Dispose() => _sessions.Dispose();

    const string Ged = """
        0 HEAD
        1 GEDC
        2 VERS 5.5.1
        0 @I1@ INDI
        1 NAME William /Ashworth/
        1 SEX M
        1 BIRT
        2 DATE 1852
        2 PLAC Missouri
        0 @I2@ INDI
        1 NAME Harriet /Ashworth/
        1 SEX F
        1 BIRT
        2 DATE 1855
        2 PLAC Missouri
        0 @I3@ INDI
        1 NAME Someone /Else/
        1 SEX M
        1 BIRT
        2 DATE 1850
        """;

    SelectTargetsTool Tool() => new(_sessions.Open(Ged), new ToolGate());

    static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();

    static async Task<CallToolResult> Call(SelectTargetsTool tool, string count, string surnames) =>
        await tool.HandleAsync(Json(count), Json(surnames), CancellationToken.None);

    static string ErrorText(CallToolResult result) => ((TextContentBlock)result.Content[0]).Text;

    [Fact]
    public async Task SelectTargets_ReturnsTheWantedDocumentWithoutTheGedcomPath()
    {
        var result = await Call(Tool(), "5", "[\"Ashworth\"]");

        Assert.NotEqual(true, result.IsError);
        var root = result.StructuredContent!.Value;
        Assert.False(root.TryGetProperty("source", out _));
        Assert.Equal(["Ashworth"], root.GetProperty("surnames").EnumerateArray().Select(s => s.GetString()!));
        Assert.True(root.GetProperty("totalCandidates").GetInt32() >= 2);
        Assert.Equal(root.GetProperty("count").GetInt32(), root.GetProperty("targets").GetArrayLength());
        Assert.True(root.GetProperty("draw").TryGetProperty("seed", out _));
    }

    [Fact]
    public async Task SelectTargets_OnlyDrawsPeopleWhoMatchTheSurnames()
    {
        var result = await Call(Tool(), "50", "[\"Ashworth\"]");

        var xrefs = result.StructuredContent!.Value.GetProperty("targets").EnumerateArray()
            .Select(t => t.GetProperty("xref").GetString()!).ToHashSet();
        Assert.DoesNotContain("@I3@", xrefs);
        Assert.NotEmpty(xrefs);
    }

    [Fact]
    public async Task SelectTargets_CountBelowCandidates_ReturnsExactlyThatMany()
    {
        var result = await Call(Tool(), "1", "[\"Ashworth\"]");

        Assert.Equal(1, result.StructuredContent!.Value.GetProperty("count").GetInt32());
    }

    [Fact]
    public async Task SelectTargets_MatchesTheCommandsDrawForTheSameSeed()
    {
        var session = _sessions.Open(Ged);
        var snapshot = await session.GetSnapshotAsync(CancellationToken.None);
        var candidates = GedFire.TargetSelection.GapDetector.Detect(snapshot.Model, ["Ashworth"]);

        var result = await Call(new SelectTargetsTool(session, new ToolGate()), "50", "[\"Ashworth\"]");

        var root = result.StructuredContent!.Value;
        long seed = root.GetProperty("draw").GetProperty("seed").GetInt64();
        var expected = GedFire.TargetSelection.TargetDrawer.Draw(candidates, 50, seed).Targets.Select(t => t.Xref);
        Assert.Equal(expected, root.GetProperty("targets").EnumerateArray().Select(t => t.GetProperty("xref").GetString()!));
    }

    [Theory]
    [InlineData("null", "[\"Ashworth\"]", "count is required")]
    [InlineData("\"5\"", "[\"Ashworth\"]", "count must be an integer between 1 and")]
    [InlineData("0", "[\"Ashworth\"]", "count must be an integer between 1 and")]
    [InlineData("5", "null", "surnames is required")]
    [InlineData("5", "[]", "surnames is required")]
    [InlineData("5", "\"Ashworth\"", "surnames must be an array")]
    [InlineData("5", "[\"Ashworth\", 2]", "surnames[1] must be a non-blank string")]
    public async Task SelectTargets_BadArguments_AreRejectedByFieldName(string count, string surnames, string expected)
    {
        var result = await Call(Tool(), count, surnames);

        Assert.True(result.IsError);
        Assert.Contains(expected, ErrorText(result));
    }

    [Fact]
    public void Tool_IsReadOnlyButNotIdempotentBecauseEachDrawIsClockSeeded()
    {
        var tool = Tool().ToMcpServerTool();

        Assert.Equal("select_targets", tool.ProtocolTool.Name);
        Assert.True(tool.ProtocolTool.Annotations!.ReadOnlyHint);
        Assert.False(tool.ProtocolTool.Annotations.IdempotentHint);
    }

    [Fact]
    public void Constructor_RejectsNullDependencies()
    {
        Assert.Throws<ArgumentNullException>(() => new SelectTargetsTool(null!, new ToolGate()));
        Assert.Throws<ArgumentNullException>(() => new SelectTargetsTool(_sessions.Open(Ged), null!));
    }
}
