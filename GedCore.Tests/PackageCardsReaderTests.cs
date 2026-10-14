using System.Text;
using GedFire.Mcp;

namespace GedCore.Tests;

public class PackageCardsReaderTests
{
    static MemoryStream Utf8(string json) => new(Encoding.UTF8.GetBytes(json));

    [Fact]
    public void EachAnchorsCards_AreReadInValidateChangesetsShape()
    {
        bool ok = PackageCardsReader.TryRead(Utf8("""
            { "@I1@": [ { "xref": "@I1@", "class": "new-spouse", "points": 10, "name": "Emily Farnum" } ],
              "@I2@": [] }
            """), "cards.json", out var cards, out var error);

        Assert.True(ok, error);
        var card = Assert.Single(cards["@I1@"]);
        Assert.Equal(("@I1@", "new-spouse", 10, "Emily Farnum"), (card.Xref, card.Class, card.Points, card.Name));
        Assert.Empty(cards["@I2@"]);
    }

    [Theory]
    [InlineData("[]", "must hold an object")]
    [InlineData("""{ "@I1@": {} }""", "the cards for @I1@ must be an array")]
    [InlineData("""{ "@I1@": [ { "xref": "@I1@", "class": "enrich-person" } ] }""", "@I1@: cards[0].points is required")]
    [InlineData("{ nope", "is not valid JSON")]
    public void AWrongShape_IsRefusedNamingTheAnchorAndField(string json, string expected)
    {
        Assert.False(PackageCardsReader.TryRead(Utf8(json), "cards.json", out _, out var error));
        Assert.Contains(expected, error);
        Assert.StartsWith("cards.json", error);
    }
}
