using System.Text.Json;
using GedCore.Matching;
using GedFire.Mcp;
using ModelContextProtocol.Protocol;

namespace GedCore.Tests;

public class FindFamilyToolTests : IDisposable
{
    readonly TempDocumentSessions _sessions = new();

    public void Dispose() => _sessions.Dispose();

    const string Ged = """
        0 HEAD
        1 GEDC
        2 VERS 5.5.1
        0 @I1@ INDI
        1 NAME Cornelius /Ashworth/
        1 SEX M
        1 BIRT
        2 DATE 1741
        1 FAMS @F1@
        0 @I2@ INDI
        1 NAME Beatrice /Fenwick/
        1 SEX F
        1 BIRT
        2 DATE 1745
        1 FAMS @F1@
        0 @I3@ INDI
        1 NAME Levi /Ashworth/
        1 SEX M
        1 BIRT
        2 DATE 1770
        1 DEAT
        2 DATE 1840
        1 FAMC @F1@
        0 @F1@ FAM
        1 HUSB @I1@
        1 WIFE @I2@
        1 CHIL @I3@
        """;

    FindFamilyTool Tool() => new(_sessions.Open(Ged), new ToolGate(), NicknameDirectory.LoadEmbedded());

    static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();

    static async Task<CallToolResult> Call(
        FindFamilyTool tool, string relation, string name, string hints = "null", string maxResults = "null") =>
        await tool.HandleAsync(Json(relation), Json(name), Json(hints), Json(maxResults), CancellationToken.None);

    static string ErrorText(CallToolResult result) => ((TextContentBlock)result.Content[0]).Text;

    [Fact]
    public async Task FindFamily_ByChildsName_ReturnsTheParentsWithTheConnectingFamilyAndRelative()
    {
        var result = await Call(Tool(), "\"child\"", "\"Levi Ashworth\"");

        Assert.NotEqual(true, result.IsError);
        var root = result.StructuredContent!.Value;
        var candidates = root.GetProperty("candidates");
        Assert.Equal(2, candidates.GetArrayLength());
        Assert.Equal(2, root.GetProperty("totalMatches").GetInt32());
        Assert.False(root.GetProperty("truncated").GetBoolean());

        var first = candidates[0];
        Assert.Equal("@F1@", first.GetProperty("familyXref").GetString());
        Assert.Equal("@I3@", first.GetProperty("relative").GetProperty("xref").GetString());
        Assert.Equal("Levi Ashworth", first.GetProperty("relative").GetProperty("name").GetString());
        Assert.True(first.GetProperty("relative").GetProperty("nameScore").GetDouble() >= 90);
        Assert.Equal(
            ["@I1@", "@I2@"],
            candidates.EnumerateArray().Select(c => c.GetProperty("person").GetProperty("xref").GetString()!).Order());
    }

    [Fact]
    public async Task FindFamily_PersonCarriesNameAndYearsWithNullsEmitted()
    {
        var result = await Call(Tool(), "\"parent\"", "\"Cornelius Ashworth\"");

        var person = result.StructuredContent!.Value.GetProperty("candidates")[0].GetProperty("person");
        Assert.Equal("Levi Ashworth", person.GetProperty("name").GetString());
        Assert.Equal(1770, person.GetProperty("birthYear").GetInt32());
        Assert.Equal(1840, person.GetProperty("deathYear").GetInt32());
    }

    [Fact]
    public async Task FindFamily_WithBirthAndDeathHints_IsAccepted()
    {
        var result = await Call(Tool(), "\"spouse\"", "\"Beatrice Fenwick\"",
            hints: "{\"birth\":{\"year\":1741},\"death\":{\"place\":\"Harwick\"}}");

        Assert.NotEqual(true, result.IsError);
        Assert.Equal("@I1@", result.StructuredContent!.Value.GetProperty("candidates")[0].GetProperty("person").GetProperty("xref").GetString());
    }

    [Fact]
    public async Task FindFamily_UnknownName_ReturnsAnEmptyResultNotAnError()
    {
        var result = await Call(Tool(), "\"spouse\"", "\"Zzqxvw Bbdfghj\"");

        Assert.NotEqual(true, result.IsError);
        Assert.Equal(0, result.StructuredContent!.Value.GetProperty("candidates").GetArrayLength());
    }

    [Theory]
    [InlineData("null", "\"Levi\"", "null", "null", "relation is required")]
    [InlineData("\"cousin\"", "\"Levi\"", "null", "null", "relation must be \"spouse\", \"child\", or \"parent\", not \"cousin\"")]
    [InlineData("3", "\"Levi\"", "null", "null", "relation must be")]
    [InlineData("\"child\"", "null", "null", "null", "name is required")]
    [InlineData("\"child\"", "\"   \"", "null", "null", "name must be a non-blank string, not a blank string")]
    [InlineData("\"child\"", "5", "null", "null", "name must be a non-blank string, not a number")]
    [InlineData("\"child\"", "\"Levi\"", "{\"parents\":{\"father\":\"Cornelius\"}}", "null", "hints.parents is not accepted by find_family")]
    [InlineData("\"child\"", "\"Levi\"", "{\"spouse\":{\"name\":\"Jane\"}}", "null", "hints.spouse is not accepted by find_family")]
    [InlineData("\"child\"", "\"Levi\"", "{\"birth\":\"1741\"}", "null", "hints.birth must be an object")]
    [InlineData("\"child\"", "\"Levi\"", "{\"birthYear\":1741}", "null", "hints contains unknown property 'birthYear'")]
    [InlineData("\"child\"", "\"Levi\"", "null", "99", "maxResults must be an integer between 1 and 20")]
    public async Task FindFamily_BadArguments_AreRejectedByFieldName(
        string relation, string name, string hints, string maxResults, string expected)
    {
        var result = await Call(Tool(), relation, name, hints, maxResults);

        Assert.True(result.IsError);
        Assert.Contains(expected, ErrorText(result));
    }

    [Fact]
    public void Tool_IsAdvertisedReadOnlyAndIdempotent()
    {
        var tool = Tool().ToMcpServerTool();

        Assert.Equal("find_family", tool.ProtocolTool.Name);
        Assert.True(tool.ProtocolTool.Annotations!.ReadOnlyHint);
        Assert.True(tool.ProtocolTool.Annotations.IdempotentHint);
        Assert.Equal(FindFamilyTool.Description, tool.ProtocolTool.Description);
    }

    [Fact]
    public void Constructor_RejectsNullDependencies()
    {
        var session = _sessions.Open(Ged);
        var nicknames = NicknameDirectory.LoadEmbedded();
        Assert.Throws<ArgumentNullException>(() => new FindFamilyTool(null!, new ToolGate(), nicknames));
        Assert.Throws<ArgumentNullException>(() => new FindFamilyTool(session, null!, nicknames));
        Assert.Throws<ArgumentNullException>(() => new FindFamilyTool(session, new ToolGate(), null!));
    }
}
