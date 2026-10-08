using System.Text.Json;
using GedFire.Mcp;
using ModelContextProtocol.Protocol;

namespace GedCore.Tests;

public class FindSourceToolTests : IDisposable
{
    readonly TempDocumentSessions _sessions = new();

    public void Dispose() => _sessions.Dispose();

    const string Ged = """
        0 HEAD
        1 GEDC
        2 VERS 5.5.1
        0 @S2@ SOUR
        1 AUTH Harwick Parish
        1 TITL Burial Register 1780-1810
        1 NOTE Burial Register 1780-1810, online at https://archive.example.org/harwick/burials (accessed 3 MAR 2024).
        0 @S1@ SOUR
        1 AUTH Ann Fenwick
        1 TITL Fenwick Family Letters
        0 @S3@ SOUR
        1 TITL Harwick Census 1850
        1 NOTE Harwick Census 1850, online at https://archive.example.org/harwick/census.
        """;

    FindSourceTool Tool() => new(_sessions.Open(Ged), new ToolGate());

    static JsonElement Arg(string? json) => json is null ? default : JsonDocument.Parse(json).RootElement.Clone();

    Task<CallToolResult> Call(string? title = null, string? author = null, string? url = null) =>
        Tool().HandleAsync(Arg(title), Arg(author), Arg(url), CancellationToken.None);

    static string[] Xrefs(CallToolResult result) =>
        [.. result.StructuredContent!.Value.GetProperty("sources").EnumerateArray().Select(s => s.GetProperty("xref").GetString()!)];

    static string TextOf(CallToolResult result) => Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;

    [Fact]
    public async Task Title_MatchesAsACaseInsensitiveSubstring()
    {
        var result = await Call(title: "\"register\"");

        Assert.NotEqual(true, result.IsError);
        Assert.Equal(["@S2@"], Xrefs(result));
    }

    [Fact]
    public async Task Author_MatchesAsACaseInsensitiveSubstring() =>
        Assert.Equal(["@S1@"], Xrefs(await Call(author: "\"FENWICK\"")));

    [Fact]
    public async Task Url_MatchesTheAddressRecordedInTheNote() =>
        Assert.Equal(["@S3@"], Xrefs(await Call(url: "\"archive.example.org/harwick/census\"")));

    [Fact]
    public async Task SeveralCriteria_MustAllMatch()
    {
        Assert.Equal(["@S2@"], Xrefs(await Call(title: "\"register\"", author: "\"parish\"")));
        Assert.Empty(Xrefs(await Call(title: "\"register\"", author: "\"fenwick\"")));
    }

    [Fact]
    public async Task Matches_AreOrderedByTitleThenXref() =>
        Assert.Equal(["@S2@", "@S3@"], Xrefs(await Call(url: "\"archive.example.org\"")));

    [Fact]
    public async Task NoMatch_ReturnsAnEmptyArray()
    {
        var result = await Call(title: "\"Nonexistent\"");

        Assert.NotEqual(true, result.IsError);
        Assert.Empty(Xrefs(result));
    }

    [Fact]
    public async Task EachSource_CarriesTitleAuthorAndUrlWithNullsEmitted()
    {
        var sources = (await Call(title: "\"Fenwick\"")).StructuredContent!.Value.GetProperty("sources");
        var letters = Assert.Single(sources.EnumerateArray());

        Assert.Equal("Fenwick Family Letters", letters.GetProperty("title").GetString());
        Assert.Equal("Ann Fenwick", letters.GetProperty("author").GetString());
        Assert.Equal(JsonValueKind.Null, letters.GetProperty("url").ValueKind);

        var census = Assert.Single((await Call(title: "\"Census\"")).StructuredContent!.Value.GetProperty("sources").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, census.GetProperty("author").ValueKind);
        Assert.Equal("https://archive.example.org/harwick/census", census.GetProperty("url").GetString());
    }

    [Fact]
    public async Task NoCriteria_IsAnError()
    {
        var result = await Call();

        Assert.True(result.IsError);
        Assert.Contains("at least one of title, author, or url", TextOf(result));
    }

    [Theory]
    [InlineData("\"   \"", "title must not be blank")]
    [InlineData("5", "title must be a string")]
    public async Task BadTitle_IsRejectedByFieldName(string title, string expected)
    {
        var result = await Call(title: title);

        Assert.True(result.IsError);
        Assert.Contains(expected, TextOf(result));
    }
}
