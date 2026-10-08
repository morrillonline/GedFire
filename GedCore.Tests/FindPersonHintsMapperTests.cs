using System.Text.Json;
using GedCore.Matching;
using GedFire.Mcp;

namespace GedCore.Tests;

public class FindPersonHintsMapperTests
{
    static MatchHints Map(string json) =>
        FindPersonHintsMapper.ToMatchHints(JsonSerializer.Deserialize<FindPersonHintsArgs>(json));

    [Fact]
    public void NoHints_MapToNone() =>
        Assert.Same(MatchHints.None, FindPersonHintsMapper.ToMatchHints(null));

    [Fact]
    public void EveryHintGroup_IsCarriedOver()
    {
        var hints = Map("""
            {"sex":"F","birth":{"year":1741,"place":"Harwick"},"death":{"year":1809},
             "parents":{"father":"Levi Ashworth","mother":"Hannah Wood"},
             "spouse":{"name":"Beatrice Fenwick","marriage":{"year":1770}}}
            """);

        Assert.Equal(new EventHint(1741, "Harwick"), hints.Birth);
        Assert.Equal(new EventHint(1809, null), hints.Death);
        Assert.Equal(new ParentsHint("Levi Ashworth", "Hannah Wood"), hints.Parents);
        Assert.Equal(new SpouseHint("Beatrice Fenwick", new EventHint(1770, null)), hints.Spouse);
        Assert.False(hints.IsMale);
    }

    [Theory]
    [InlineData("M", true)]
    [InlineData("F", false)]
    public void Sex_MapsToIsMale(string sex, bool expected) =>
        Assert.Equal(expected, Map($$"""{"sex":"{{sex}}"}""").IsMale);

    [Fact]
    public void AnAbsentGroup_StaysNull()
    {
        var hints = Map("""{"birth":{"year":1741}}""");

        Assert.Null(hints.Death);
        Assert.Null(hints.Parents);
        Assert.Null(hints.Spouse);
        Assert.Null(hints.IsMale);
    }
}
