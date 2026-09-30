using GedFire.Mcp;
using ModelContextProtocol.Protocol;

namespace GedCore.Tests;

public class ListUnanchoredPeopleToolTests : IDisposable
{
    readonly TempDocumentSessions _sessions = new();

    public void Dispose() => _sessions.Dispose();

    const string Ged = """
        0 HEAD
        1 GEDC
        2 VERS 5.5.1
        0 @I1@ INDI
        1 NAME Cornelius /Ashworth/
        1 BIRT
        2 DATE 1741
        0 @I2@ INDI
        1 NAME Joseph /Bare/
        1 NOTE Only a note.
        0 @I3@ INDI
        1 NAME Child /Bare/
        1 FAMC @F1@
        0 @I4@ INDI
        1 NAME Father /Bare/
        1 FAMS @F1@
        0 @F1@ FAM
        1 HUSB @I4@
        1 CHIL @I3@
        """;

    ListUnanchoredPeopleTool Tool() => new(_sessions.Open(Ged), new ToolGate());

    [Fact]
    public async Task ListUnanchored_ReturnsEachUnplacedPersonWithFactCountAndTies()
    {
        var result = await Tool().HandleAsync(CancellationToken.None);

        Assert.NotEqual(true, result.IsError);
        var people = result.StructuredContent!.Value.GetProperty("people");
        Assert.Equal(
            ["@I3@", "@I4@", "@I2@"],
            people.EnumerateArray().Select(p => p.GetProperty("xref").GetString()!));

        var joseph = people.EnumerateArray().Single(p => p.GetProperty("xref").GetString() == "@I2@");
        Assert.Equal("Joseph Bare", joseph.GetProperty("name").GetString());
        Assert.Equal(1, joseph.GetProperty("factCount").GetInt32());
        Assert.Empty(joseph.GetProperty("tiedOnlyToUnanchored").EnumerateArray());

        var child = people.EnumerateArray().Single(p => p.GetProperty("xref").GetString() == "@I3@");
        Assert.Equal(["@I4@"], child.GetProperty("tiedOnlyToUnanchored").EnumerateArray().Select(e => e.GetString()));
    }

    [Fact]
    public async Task ListUnanchored_NeverListsAPersonWithADate()
    {
        var result = await Tool().HandleAsync(CancellationToken.None);

        var xrefs = result.StructuredContent!.Value.GetProperty("people").EnumerateArray()
            .Select(p => p.GetProperty("xref").GetString());
        Assert.DoesNotContain("@I1@", xrefs);
    }

    [Fact]
    public void Tool_IsAdvertisedReadOnlyIdempotentWithAnEmptyInputSchema()
    {
        var tool = Tool().ToMcpServerTool();

        Assert.Equal("list_unanchored_people", tool.ProtocolTool.Name);
        Assert.True(tool.ProtocolTool.Annotations!.ReadOnlyHint);
        Assert.True(tool.ProtocolTool.Annotations.IdempotentHint);
        Assert.Empty(tool.ProtocolTool.InputSchema.GetProperty("properties").EnumerateObject());
    }

    [Fact]
    public void Constructor_RejectsNullDependencies()
    {
        Assert.Throws<ArgumentNullException>(() => new ListUnanchoredPeopleTool(null!, new ToolGate()));
        Assert.Throws<ArgumentNullException>(() => new ListUnanchoredPeopleTool(_sessions.Open(Ged), null!));
    }
}
