using System.Text.Json;
using GedCore.Points;
using GedFire.Mcp;

namespace GedCore.Tests;

public class PointsCardReaderTests
{
    static bool TryRead(string? json, out IReadOnlyList<PointsCard>? cards, out string? error) =>
        PointsCardReader.TryRead(json is null ? default : JsonDocument.Parse(json).RootElement, out cards, out error);

    [Fact]
    public void Absent_IsAcceptedWithNoCards()
    {
        Assert.True(TryRead(null, out var cards, out var error));
        Assert.Null(cards);
        Assert.Null(error);
    }

    [Fact]
    public void AValidCard_IsRead()
    {
        Assert.True(TryRead("""[ { "xref": " @I1@ ", "class": "new-child", "gap": "g", "name": "Ann Test", "points": 5 } ]""", out var cards, out _));

        var card = Assert.Single(cards!);
        Assert.Equal(new PointsCard("@I1@", "new-child", "g", "Ann Test", 5), card);
        Assert.Equal("@I1@|new-child|g", card.Key);
    }

    [Fact]
    public void ABonus_IsReadAndDefaultsToZero()
    {
        Assert.True(TryRead("""[ { "xref": "@I1@", "class": "enrich-person", "points": 1, "bonus": 4 }, { "xref": "@I2@", "class": "enrich-person", "points": 1 } ]""", out var cards, out _));

        Assert.Equal([4, 0], cards!.Select(c => c.Bonus));
    }

    [Theory]
    [InlineData("{}", "cards must be an array")]
    [InlineData("[ 3 ]", "cards[0] must be an object")]
    [InlineData("""[ { "class": "x", "points": 1 } ]""", "cards[0].xref is required")]
    [InlineData("""[ { "xref": "@I1@", "points": 1 } ]""", "cards[0].class is required")]
    [InlineData("""[ { "xref": "@I1@", "class": "x" } ]""", "cards[0].points is required")]
    [InlineData("""[ { "xref": "@I1@", "class": "x", "points": "five" } ]""", "cards[0].points must be an integer")]
    [InlineData("""[ { "xref": "@I1@", "class": 4, "points": 1 } ]""", "cards[0].class must be a string")]
    public void AMalformedCard_IsRejectedNamingTheField(string json, string expected)
    {
        Assert.False(TryRead(json, out _, out var error));
        Assert.StartsWith(expected, error);
    }
}
