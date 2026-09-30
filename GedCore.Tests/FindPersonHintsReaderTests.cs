using System.Text.Json;
using GedFire.Mcp;

namespace GedCore.Tests;

public class FindPersonHintsReaderTests
{
    static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();

    [Fact]
    public void Absent_Or_Null_IsAcceptedAsNoHints()
    {
        Assert.True(FindPersonHintsReader.TryRead(default, out var none, out var error));
        Assert.Null(none);
        Assert.Null(error);
        Assert.True(FindPersonHintsReader.TryRead(Json("null"), out var nulled, out _));
        Assert.Null(nulled);
    }

    [Fact]
    public void WellFormedHints_AreDeserialized()
    {
        bool ok = FindPersonHintsReader.TryRead(Json("""
            {"birth":{"year":1741,"place":"Harwick"},
             "death":{"year":1809},
             "parents":{"father":"Levi Ashworth","mother":"Hannah Wood"},
             "spouse":{"name":"Beatrice Fenwick","marriage":{"year":1770,"place":"Harwick"}}}
            """), out var args, out var error);

        Assert.True(ok, error);
        Assert.Equal(1741, args!.Birth!.Year);
        Assert.Equal("Harwick", args.Birth.Place);
        Assert.Equal("Hannah Wood", args.Parents!.Mother);
        Assert.Equal(1770, args.Spouse!.Marriage!.Year);
    }

    [Theory]
    [InlineData("\"birth 1741\"", "hints must be an object", "a string")]
    [InlineData("{\"birth\":\"1741\"}", "hints.birth must be an object such as", "a string")]
    [InlineData("{\"death\":[1809]}", "hints.death must be an object such as", "an array")]
    [InlineData("{\"birth\":{\"year\":\"1741\"}}", "hints.birth.year must be an integer such as 1741", "a string")]
    [InlineData("{\"birth\":{\"year\":1741.5}}", "hints.birth.year must be an integer", "a number")]
    [InlineData("{\"birth\":{\"place\":1741}}", "hints.birth.place must be a string", "a number")]
    [InlineData("{\"parents\":\"Levi Ashworth\"}", "hints.parents must be an object such as", "a string")]
    [InlineData("{\"parents\":{\"father\":5}}", "hints.parents.father must be a string", "a number")]
    [InlineData("{\"spouse\":\"Beatrice\"}", "hints.spouse must be an object such as", "a string")]
    [InlineData("{\"spouse\":{\"name\":true}}", "hints.spouse.name must be a string", "a boolean")]
    [InlineData("{\"spouse\":{\"marriage\":\"1770\"}}", "hints.spouse.marriage must be an object such as", "a string")]
    [InlineData("{\"spouse\":{\"marriage\":{\"year\":\"1770\"}}}", "hints.spouse.marriage.year must be an integer", "a string")]
    public void WrongShape_IsRejectedByFieldPathWithTheAcceptedShape(string json, string expectedStart, string expectedDetail)
    {
        bool ok = FindPersonHintsReader.TryRead(Json(json), out var args, out var error);

        Assert.False(ok);
        Assert.Null(args);
        Assert.StartsWith(expectedStart, error);
        Assert.Contains(expectedDetail, error);
    }

    [Fact]
    public void UnknownProperties_PassThroughForTheToolsOwnValidationToName()
    {
        bool ok = FindPersonHintsReader.TryRead(Json("{\"birthYear\":1741}"), out var args, out _);

        Assert.True(ok);
        Assert.True(args!.AdditionalProperties!.ContainsKey("birthYear"));
    }
}
