using System.Text.Json;
using GedFire.Mcp;
using ModelContextProtocol.Protocol;

namespace GedCore.Tests;

public class ListPeopleToolTests : IDisposable
{
    readonly TempDocumentSessions _sessions = new();

    public void Dispose() => _sessions.Dispose();

    const string Ged = """
        0 HEAD
        1 GEDC
        2 VERS 5.5.1
        0 @I1@ INDI
        1 NAME Alfred /Ashworth/
        0 @I2@ INDI
        1 NAME Cornelius /Ashworth/
        1 BIRT
        2 DATE 1741
        0 @I3@ INDI
        1 NAME Beatrice /Fenwick/
        """;

    ListPeopleTool Tool() => new(_sessions.Open(Ged), new ToolGate());

    static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();

    static async Task<CallToolResult> Call(
        ListPeopleTool tool, string surnames = "null", string cursor = "null", string pageSize = "null") =>
        await tool.HandleAsync(Json(surnames), Json(cursor), Json(pageSize), CancellationToken.None);

    static string ErrorText(CallToolResult result) => ((TextContentBlock)result.Content[0]).Text;

    [Fact]
    public async Task List_NoArguments_ReturnsEveryoneInStableOrder()
    {
        var result = await Call(Tool());

        Assert.NotEqual(true, result.IsError);
        var root = result.StructuredContent!.Value;
        Assert.Equal(3, root.GetProperty("totalMatches").GetInt32());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("nextCursor").ValueKind);
        Assert.Equal(
            ["@I1@", "@I2@", "@I3@"],
            root.GetProperty("people").EnumerateArray().Select(p => p.GetProperty("xref").GetString()!));
    }

    [Fact]
    public async Task List_SurnameFilter_LimitsToThoseSurnames()
    {
        var result = await Call(Tool(), surnames: "[\"Fenwick\"]");

        var people = result.StructuredContent!.Value.GetProperty("people");
        Assert.Equal("@I3@", Assert.Single(people.EnumerateArray()).GetProperty("xref").GetString());
    }

    [Fact]
    public async Task List_PersonEntry_CarriesNameSurnameAndYearsWithNullsEmitted()
    {
        var result = await Call(Tool());

        var cornelius = result.StructuredContent!.Value.GetProperty("people").EnumerateArray()
            .Single(p => p.GetProperty("xref").GetString() == "@I2@");
        Assert.Equal("Cornelius Ashworth", cornelius.GetProperty("name").GetString());
        Assert.Equal("Ashworth", cornelius.GetProperty("surname").GetString());
        Assert.Equal(1741, cornelius.GetProperty("birthYear").GetInt32());
        Assert.Equal(JsonValueKind.Null, cornelius.GetProperty("deathYear").ValueKind);
    }

    [Fact]
    public async Task List_PagingThroughCursor_ReturnsEveryoneOnce()
    {
        var tool = Tool();
        var first = (await Call(tool, pageSize: "2")).StructuredContent!.Value;
        string cursor = first.GetProperty("nextCursor").GetString()!;
        var second = (await Call(tool, cursor: JsonSerializer.Serialize(cursor), pageSize: "2")).StructuredContent!.Value;

        Assert.Equal(2, first.GetProperty("people").GetArrayLength());
        Assert.Equal("@I3@", Assert.Single(second.GetProperty("people").EnumerateArray()).GetProperty("xref").GetString());
        Assert.Equal(JsonValueKind.Null, second.GetProperty("nextCursor").ValueKind);
    }

    [Theory]
    [InlineData("\"Ashworth\"", "null", "null", "surnames must be an array")]
    [InlineData("null", "null", "\"10\"", "pageSize must be an integer between 1 and 2000")]
    [InlineData("null", "null", "5000", "pageSize must be an integer between 1 and 2000")]
    [InlineData("null", "7", "null", "cursor must be a string")]
    [InlineData("null", "\"garbage!\"", "null", "cursor is not a value returned by a previous list_people call")]
    public async Task List_BadArguments_AreRejectedByFieldName(string surnames, string cursor, string pageSize, string expected)
    {
        var result = await Call(Tool(), surnames, cursor, pageSize);

        Assert.True(result.IsError);
        Assert.Contains(expected, ErrorText(result));
    }

    [Fact]
    public void Tool_DeclaresAReadOnlyIdempotentSchemaMatchingItsConstants()
    {
        var tool = Tool().ToMcpServerTool();

        Assert.Equal("list_people", tool.ProtocolTool.Name);
        Assert.True(tool.ProtocolTool.Annotations!.ReadOnlyHint);
        Assert.True(tool.ProtocolTool.Annotations.IdempotentHint);
        Assert.Equal(ListPeopleTool.Description, tool.ProtocolTool.Description);
    }

    [Fact]
    public void Constructor_RejectsNullDependencies()
    {
        Assert.Throws<ArgumentNullException>(() => new ListPeopleTool(null!, new ToolGate()));
        Assert.Throws<ArgumentNullException>(() => new ListPeopleTool(_sessions.Open(Ged), null!));
    }
}
