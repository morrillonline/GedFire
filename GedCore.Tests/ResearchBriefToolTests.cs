using System.Text.Json;
using GedFire.Mcp;
using ModelContextProtocol.Protocol;

namespace GedCore.Tests;

public class ResearchBriefToolTests : IDisposable
{
    readonly TempDocumentSessions _sessions = new();

    public void Dispose() => _sessions.Dispose();

    // @I1@ has kin, a note, a family note, a missing death, an identical twin
    // @I20@, and a death recorded before his birth (a standing GEN101 finding).
    const string Ged = """
        0 HEAD
        1 GEDC
        2 VERS 5.5.1
        0 @I1@ INDI
        1 NAME John /Ashworth/
        1 SEX M
        1 BIRT
        2 DATE 1848
        2 PLAC Boone County, Iowa
        1 DEAT
        2 DATE 1840
        1 NOTE Check the 1885 state census next.
        1 FAMS @F1@
        0 @I2@ INDI
        1 NAME Mary /Ashworth/
        1 SEX F
        1 FAMS @F1@
        0 @I3@ INDI
        1 NAME Thomas /Ashworth/
        1 SEX M
        1 BIRT
        2 DATE 1872
        1 FAMC @F1@
        0 @I20@ INDI
        1 NAME John /Ashworth/
        1 SEX M
        1 BIRT
        2 DATE 1848
        2 PLAC Boone County, Iowa
        1 DEAT
        2 DATE 1840
        0 @F1@ FAM
        1 HUSB @I1@
        1 WIFE @I2@
        1 CHIL @I3@
        1 MARR
        2 DATE 1870
        1 NOTE The witness is worth tracing.
        """;

    ResearchBriefTool Tool()
    {
        var session = _sessions.Open(Ged, out string path);
        return new ResearchBriefTool(session, new ToolGate(), path, Path.GetDirectoryName(path)!);
    }

    static string ErrorText(CallToolResult result) => ((TextContentBlock)result.Content[0]).Text;

    [Fact]
    public void Constructor_RejectsNullOrEmptyDependencies()
    {
        var session = _sessions.Open(Ged, out string path);
        Assert.Throws<ArgumentNullException>(() => new ResearchBriefTool(null!, new ToolGate(), path, "media"));
        Assert.Throws<ArgumentNullException>(() => new ResearchBriefTool(session, null!, path, "media"));
        Assert.Throws<ArgumentException>(() => new ResearchBriefTool(session, new ToolGate(), "", "media"));
        Assert.Throws<ArgumentException>(() => new ResearchBriefTool(session, new ToolGate(), path, ""));
    }

    [Fact]
    public async Task ResearchBrief_CarriesEverySectionInOneResponse()
    {
        var result = await Tool().HandleAsync("@I1@", null, CancellationToken.None);

        Assert.NotEqual(true, result.IsError);
        var root = result.StructuredContent!.Value;
        Assert.Equal("@I1@", root.GetProperty("xref").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("cardType").ValueKind);

        Assert.Equal("John Ashworth", root.GetProperty("person").GetProperty("name").GetString());

        var kin = root.GetProperty("kin");
        Assert.Equal(0, kin.GetProperty("parents").GetArrayLength());
        Assert.Equal("@I2@", kin.GetProperty("spouses")[0].GetProperty("spouse").GetProperty("xref").GetString());
        Assert.Equal("1870", kin.GetProperty("spouses")[0].GetProperty("marriageDate").GetString());
        Assert.Equal("@I3@", kin.GetProperty("children")[0].GetProperty("xref").GetString());

        Assert.True(root.GetProperty("gaps").GetArrayLength() > 0);

        Assert.Equal("@I20@", root.GetProperty("possibleDuplicateOf")[0].GetProperty("xref").GetString());
        Assert.Equal(1, root.GetProperty("possibleDuplicateCount").GetInt32());

        // The death recorded before the birth is a standing GEN101 finding.
        var plausibility = root.GetProperty("plausibility");
        Assert.Contains(plausibility.EnumerateArray(),
            f => f.GetProperty("code").GetString() == "GEN101" && f.GetProperty("xref").GetString() == "@I1@");

        var notes = root.GetProperty("notes");
        Assert.Equal("Check the 1885 state census next.", notes.GetProperty("person")[0].GetString());
        Assert.Equal("@F1@", notes.GetProperty("families")[0].GetProperty("xref").GetString());

        Assert.Equal(JsonValueKind.Null, root.GetProperty("emphasis").ValueKind);
    }

    [Fact]
    public async Task ResearchBrief_CardTypeIsNormalizedAndAddsEmphasis()
    {
        var result = await Tool().HandleAsync("@I1@", "Enrich Death", CancellationToken.None);

        Assert.NotEqual(true, result.IsError);
        var root = result.StructuredContent!.Value;
        Assert.Equal("enrich-death", root.GetProperty("cardType").GetString());
        Assert.NotEqual(JsonValueKind.Null, root.GetProperty("emphasis").ValueKind);
    }

    [Fact]
    public async Task ResearchBrief_UnknownCardType_IsAnErrorListingTheAcceptedOnes()
    {
        var result = await Tool().HandleAsync("@I1@", "enrich-burial", CancellationToken.None);

        Assert.Equal(true, result.IsError);
        Assert.StartsWith("cardType must be one of new-parent, new-spouse, new-child", ErrorText(result));
        Assert.Contains("\"enrich-burial\"", ErrorText(result));
    }

    [Fact]
    public async Task ResearchBrief_UnknownXref_IsAnErrorNamingTheXref()
    {
        var result = await Tool().HandleAsync("@I99@", null, CancellationToken.None);

        Assert.Equal(true, result.IsError);
        Assert.Equal("xref: @I99@ is not a person in this document.", ErrorText(result));
    }

    [Fact]
    public async Task ResearchBrief_FamilyXref_IsAnError()
    {
        var result = await Tool().HandleAsync("@F1@", null, CancellationToken.None);

        Assert.Equal(true, result.IsError);
        Assert.Equal("xref: @F1@ is not a person in this document.", ErrorText(result));
    }

    [Fact]
    public async Task ResearchBrief_BlankXref_IsAnError()
    {
        var result = await Tool().HandleAsync("  ", null, CancellationToken.None);

        Assert.Equal(true, result.IsError);
        Assert.StartsWith("xref must be a non-blank person xref", ErrorText(result));
    }

    [Fact]
    public async Task ResearchBrief_IsDeterministic()
    {
        var tool = Tool();

        var first = await tool.HandleAsync("@I1@", "enrich-person", CancellationToken.None);
        var second = await tool.HandleAsync("@I1@", "enrich-person", CancellationToken.None);

        Assert.Equal(
            first.StructuredContent!.Value.GetRawText(),
            second.StructuredContent!.Value.GetRawText());
    }

    [Fact]
    public void ToMcpServerTool_IsAdvertisedReadOnlyAndIdempotent()
    {
        var tool = Tool().ToMcpServerTool();

        Assert.Equal("research_brief", tool.ProtocolTool.Name);
        Assert.True(tool.ProtocolTool.Annotations!.ReadOnlyHint);
        Assert.False(tool.ProtocolTool.Annotations.DestructiveHint);
        Assert.True(tool.ProtocolTool.Annotations.IdempotentHint);
    }
}
