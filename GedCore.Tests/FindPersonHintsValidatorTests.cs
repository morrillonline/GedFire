using System.Text.Json;
using GedFire.Mcp;

namespace GedCore.Tests;

public class FindPersonHintsValidatorTests
{
    static FindPersonHintsArgs Hints(string json) => JsonSerializer.Deserialize<FindPersonHintsArgs>(json)!;

    [Fact]
    public void NoHints_AreValid()
    {
        Assert.True(FindPersonHintsValidator.TryValidate(null, out var error));
        Assert.Null(error);
    }

    [Fact]
    public void WellFormedHints_AreValid()
    {
        Assert.True(FindPersonHintsValidator.TryValidate(Hints("""
            {"sex":"M","birth":{"year":1741,"place":"Harwick"},"death":{"year":1809},
             "parents":{"father":"Levi Ashworth"},
             "spouse":{"name":"Beatrice Fenwick","marriage":{"place":"Harwick"}}}
            """), out _));
    }

    [Theory]
    [InlineData("{}", "hints must contain at least one of sex, birth, death, parents, or spouse.")]
    [InlineData("""{"sex":"X"}""", "hints.sex must be \"M\" or \"F\".")]
    [InlineData("""{"sex":"m"}""", "hints.sex must be \"M\" or \"F\".")]
    [InlineData("""{"birthYear":1741}""", "hints contains unknown property 'birthYear'.")]
    [InlineData("""{"birth":{}}""", "hints.birth must contain year or place.")]
    [InlineData("""{"death":{"year":0}}""", "hints.death.year must be between 1 and 9999.")]
    [InlineData("""{"birth":{"year":10000}}""", "hints.birth.year must be between 1 and 9999.")]
    [InlineData("""{"birth":{"place":"  "}}""", "hints.birth.place must not be blank.")]
    [InlineData("""{"birth":{"era":"x","year":1}}""", "hints.birth contains unknown property 'era'.")]
    [InlineData("""{"parents":{}}""", "hints.parents must contain father or mother.")]
    [InlineData("""{"parents":{"mother":" "}}""", "hints.parents.mother must not be blank.")]
    [InlineData("""{"parents":{"guardian":"x","father":"y"}}""", "hints.parents contains unknown property 'guardian'.")]
    [InlineData("""{"spouse":{}}""", "hints.spouse must contain name or marriage.")]
    [InlineData("""{"spouse":{"name":""}}""", "hints.spouse.name must not be blank.")]
    [InlineData("""{"spouse":{"marriage":{"year":0}}}""", "hints.spouse.marriage.year must be between 1 and 9999.")]
    [InlineData("""{"spouse":{"partner":"x","name":"y"}}""", "hints.spouse contains unknown property 'partner'.")]
    public void InvalidHints_AreRejectedByPath(string json, string expected)
    {
        Assert.False(FindPersonHintsValidator.TryValidate(Hints(json), out string? error));
        Assert.Equal(expected, error);
    }
}
