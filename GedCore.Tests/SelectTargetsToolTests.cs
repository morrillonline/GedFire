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
        await tool.HandleAsync(Json(count), Json(surnames), default, CancellationToken.None);

    static async Task<CallToolResult> CallXrefs(SelectTargetsTool tool, string xrefs) =>
        await tool.HandleAsync(default, default, Json(xrefs), CancellationToken.None);

    static string ErrorText(CallToolResult result) => ((TextContentBlock)result.Content[0]).Text;

    [Theory]
    [InlineData(false, "ABT 1872", "", 1, 2)]
    [InlineData(true, "ABT 1872", "", 1, 2)]
    [InlineData(false, "ABT 1872", "Penobscot, Maine", 3, 0)]
    [InlineData(true, "ABT 1872", "Penobscot, Maine", 3, 0)]
    [InlineData(false, "4 JUL 1872", "", 3, 0)]
    [InlineData(true, "4 JUL 1872", "", 3, 0)]
    [InlineData(false, "", "", 1, 2)]
    [InlineData(true, "", "", 1, 2)]
    public async Task BareBonesGate_ReportsSuppressedCards(bool explicitPeople, string date, string place, int count, int suppressedCount)
    {
        string ged = $"""
            0 HEAD
            1 GEDC
            2 VERS 5.5.1
            0 @I1@ INDI
            1 NAME Ada /Ashworth/
            1 BIRT
            2 DATE {date}
            2 PLAC {place}
            """;
        var tool = new SelectTargetsTool(_sessions.Open(ged), new ToolGate());

        var result = explicitPeople ? await CallXrefs(tool, "[\"@I1@\"]") : await Call(tool, "50", "[\"Ashworth\"]");

        Assert.NotEqual(true, result.IsError);
        var root = result.StructuredContent!.Value;
        Assert.Equal(count, root.GetProperty("count").GetInt32());
        Assert.Equal(count, root.GetProperty("totalCandidates").GetInt32());
        var suppressed = root.GetProperty("suppressedCards").EnumerateArray().ToList();
        Assert.Equal(suppressedCount, suppressed.Count);
        Assert.All(suppressed, card =>
        {
            Assert.Equal("@I1@", card.GetProperty("xref").GetString());
            Assert.Equal("Ada Ashworth", card.GetProperty("name").GetString());
            Assert.Contains(card.GetProperty("cardType").GetString(), new[] { "New parent", "New spouse" });
            Assert.Equal("bare-bones subject \u2014 enrich first", card.GetProperty("reason").GetString());
        });
    }

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
        Assert.Empty(root.GetProperty("suppressedCards").EnumerateArray());
    }

    [Theory]
    [InlineData(false, "", "", false)]
    [InlineData(true, "", "", false)]
    [InlineData(false, "5 NOV 1874", "", true)]
    [InlineData(true, "5 NOV 1874", "", true)]
    [InlineData(false, "", "Maine", true)]
    [InlineData(true, "", "Maine", true)]
    public async Task BareBonesFamily_ReportsSuppressionUnlessMarriageAnchored(bool explicitPeople, string date, string place, bool childExpected)
    {
        string ged = $"""
            0 HEAD
            1 GEDC
            2 VERS 5.5.1
            0 @I1@ INDI
            1 NAME Adam /Ashworth/
            1 FAMS @F1@
            0 @I2@ INDI
            1 NAME Ada /Ashworth/
            1 FAMS @F1@
            0 @F1@ FAM
            1 HUSB @I1@
            1 WIFE @I2@
            1 MARR
            2 DATE {date}
            2 PLAC {place}
            """;
        var tool = new SelectTargetsTool(_sessions.Open(ged), new ToolGate());

        var result = explicitPeople
            ? await CallXrefs(tool, "[\"@I1@\", \"@I2@\"]")
            : await Call(tool, "50", "[\"Ashworth\"]");

        Assert.NotEqual(true, result.IsError);
        var root = result.StructuredContent!.Value;
        Assert.Equal(childExpected ? 1 : 0, root.GetProperty("targets").EnumerateArray()
            .Count(card => card.GetProperty("cardType").GetString() == "New child"));
        var suppressedChildren = root.GetProperty("suppressedCards").EnumerateArray()
            .Where(card => card.GetProperty("cardType").GetString() == "New child").ToList();
        Assert.Equal(childExpected ? 0 : 1, suppressedChildren.Count);
        if (!childExpected)
        {
            var card = Assert.Single(suppressedChildren);
            Assert.Equal("@I1@", card.GetProperty("xref").GetString());
            Assert.Equal("Adam Ashworth", card.GetProperty("name").GetString());
            Assert.Equal("bare-bones family (both spouses and marriage lack anchors) \u2014 enrich first", card.GetProperty("reason").GetString());
        }
    }

    [Fact]
    public async Task SelectTargets_EveryTargetCarriesPossibleDuplicateOfAndNoBirthPlace()
    {
        var result = await Call(Tool(), "50", "[\"Ashworth\"]");

        foreach (var target in result.StructuredContent!.Value.GetProperty("targets").EnumerateArray())
        {
            Assert.Equal(JsonValueKind.Array, target.GetProperty("possibleDuplicateOf").ValueKind);
            Assert.False(target.TryGetProperty("birthPlace", out _));
        }
    }

    [Fact]
    public async Task SelectTargets_TargetWithTwin_IsStillDrawnAndNamesTheTwin()
    {
        const string twinGed = Ged + """

            0 @I4@ INDI
            1 NAME William /Ashworth/
            1 SEX M
            1 BIRT
            2 DATE 1852
            2 PLAC Missouri
            """;
        var tool = new SelectTargetsTool(_sessions.Open(twinGed), new ToolGate());

        var result = await Call(tool, "50", "[\"Ashworth\"]");

        var targets = result.StructuredContent!.Value.GetProperty("targets").EnumerateArray().ToList();
        var twinned = targets.Where(t => t.GetProperty("xref").GetString() == "@I1@").ToList();
        Assert.NotEmpty(twinned);
        foreach (var target in twinned)
        {
            var duplicate = Assert.Single(target.GetProperty("possibleDuplicateOf").EnumerateArray());
            Assert.Equal("@I4@", duplicate.GetProperty("xref").GetString());
            Assert.True(duplicate.GetProperty("score").GetDouble() >= 70);
        }
        var others = targets.Where(t => t.GetProperty("xref").GetString() == "@I2@").ToList();
        Assert.NotEmpty(others);
        Assert.All(others, t => Assert.Equal(0, t.GetProperty("possibleDuplicateOf").GetArrayLength()));
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

    // -------------------------------------------------------------------
    // Explicit xrefs: no draw, deterministic scoring of exactly the given people
    // -------------------------------------------------------------------

    const string XrefGed = """
        0 HEAD
        1 GEDC
        2 VERS 5.5.1
        0 @I10@ INDI
        1 NAME Robert /Ashworth/
        1 SEX M
        1 BIRT
        2 DATE 15 MAR 1820
        2 PLAC Boston, Massachusetts
        2 SOUR @S1@
        1 DEAT
        2 DATE 2 FEB 1890
        2 PLAC Boston, Massachusetts
        2 SOUR @S1@
        1 FAMC @F1@
        1 FAMS @F2@
        0 @I11@ INDI
        1 NAME Henry /Ashworth/
        1 SEX M
        1 FAMS @F1@
        0 @I12@ INDI
        1 NAME Sarah /Price/
        1 SEX F
        1 FAMS @F1@
        0 @I13@ INDI
        1 NAME Jane /Smith/
        1 SEX F
        1 FAMS @F2@
        0 @I14@ INDI
        1 NAME Thomas /Ashworth/
        1 SEX M
        1 BIRT
        2 DATE 1850
        2 PLAC Maine
        1 FAMC @F2@
        0 @F1@ FAM
        1 HUSB @I11@
        1 WIFE @I12@
        1 CHIL @I10@
        0 @F2@ FAM
        1 HUSB @I10@
        1 WIFE @I13@
        1 CHIL @I14@
        1 MARR
        2 DATE 20 JUN 1845
        2 PLAC Boston, Massachusetts
        2 SOUR @S1@
        """;

    SelectTargetsTool XrefTool() => new(_sessions.Open(XrefGed), new ToolGate());

    [Fact]
    public async Task Xrefs_CalledTwiceWithTheSameList_ReturnsIdenticalResults()
    {
        var tool = XrefTool();

        var first = await CallXrefs(tool, "[\"@I14@\", \"@I13@\"]");
        var second = await CallXrefs(tool, "[\"@I14@\", \"@I13@\"]");

        Assert.NotEqual(true, first.IsError);
        Assert.Equal(first.StructuredContent!.Value.GetRawText(), second.StructuredContent!.Value.GetRawText());
    }

    [Fact]
    public async Task Xrefs_EnvelopeKeepsGeneratedCountAndTargets_AndSeedIsNull()
    {
        var result = await CallXrefs(XrefTool(), "[\"@I14@\"]");

        Assert.NotEqual(true, result.IsError);
        var root = result.StructuredContent!.Value;
        Assert.True(root.TryGetProperty("generated", out _));
        Assert.Equal(root.GetProperty("count").GetInt32(), root.GetProperty("targets").GetArrayLength());
        Assert.Equal(0, root.GetProperty("surnames").GetArrayLength());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("draw").GetProperty("seed").ValueKind);
    }

    [Fact]
    public async Task Xrefs_PersonWithEveryFactExactAndFullFamily_ProducesNoCardAndIsNamed()
    {
        var result = await CallXrefs(XrefTool(), "[\"@I10@\"]");

        var root = result.StructuredContent!.Value;
        Assert.Equal(0, root.GetProperty("count").GetInt32());
        Assert.Equal(0, root.GetProperty("targets").GetArrayLength());
        var withoutGaps = root.GetProperty("xrefsWithoutGaps").EnumerateArray().Select(x => x.GetString()!);
        Assert.Equal(["@I10@"], withoutGaps);
    }

    [Fact]
    public async Task Xrefs_PersonWithSeveralGaps_YieldsOneTargetPerGap()
    {
        // @I14@ has both parents recorded, an inexact birth, and no spouse family.
        var result = await CallXrefs(XrefTool(), "[\"@I14@\"]");

        var root = result.StructuredContent!.Value;
        var cardTypes = root.GetProperty("targets").EnumerateArray()
            .Select(t => t.GetProperty("cardType").GetString()!).ToList();
        Assert.Contains("New spouse", cardTypes);
        Assert.Contains("Enrich person", cardTypes);
        Assert.All(root.GetProperty("targets").EnumerateArray(),
            t => Assert.Equal("@I14@", t.GetProperty("xref").GetString()));
        Assert.Equal(0, root.GetProperty("xrefsWithoutGaps").GetArrayLength());
    }

    [Fact]
    public async Task Xrefs_TargetRecords_CarryTheSameShapeAsTheRandomDraw()
    {
        var result = await CallXrefs(XrefTool(), "[\"@I14@\"]");

        foreach (var target in result.StructuredContent!.Value.GetProperty("targets").EnumerateArray())
        {
            Assert.True(target.TryGetProperty("name", out _));
            Assert.True(target.TryGetProperty("surname", out _));
            Assert.True(target.TryGetProperty("nominalPoints", out _));
            Assert.True(target.TryGetProperty("score", out _));
            Assert.True(target.GetProperty("difficulty").TryGetProperty("band", out _));
            Assert.Equal(JsonValueKind.Array, target.GetProperty("possibleDuplicateOf").ValueKind);
            Assert.True(target.TryGetProperty("possibleDuplicateCount", out _));
        }
    }

    [Fact]
    public async Task Xrefs_UnknownXref_IsAnErrorNamingIt()
    {
        var result = await CallXrefs(XrefTool(), "[\"@I10@\", \"@I99@\"]");

        Assert.True(result.IsError);
        Assert.Contains("@I99@ is not a person in this document", ErrorText(result));
    }

    [Fact]
    public async Task Xrefs_CombinedWithCountOrSurnames_IsRejected()
    {
        var result = await XrefTool().HandleAsync(Json("5"), default, Json("[\"@I10@\"]"), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("xrefs cannot be combined with count or surnames", ErrorText(result));
    }

    [Theory]
    [InlineData("[]", "xrefs must contain 1 to")]
    [InlineData("\"@I10@\"", "xrefs must be an array")]
    [InlineData("[\"I10\"]", "xrefs[0] must be an xref")]
    public async Task Xrefs_BadShape_IsRejectedByFieldName(string xrefs, string expected)
    {
        var result = await CallXrefs(XrefTool(), xrefs);

        Assert.True(result.IsError);
        Assert.Contains(expected, ErrorText(result));
    }

    [Fact]
    public void Constructor_RejectsNullDependencies()
    {
        Assert.Throws<ArgumentNullException>(() => new SelectTargetsTool(null!, new ToolGate()));
        Assert.Throws<ArgumentNullException>(() => new SelectTargetsTool(_sessions.Open(Ged), null!));
    }
}
