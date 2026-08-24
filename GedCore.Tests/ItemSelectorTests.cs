using GedCore.Apply;

namespace GedCore.Tests;

public class ItemSelectorTests
{
    static readonly Changeset ThreeItemChangeset = Changeset.Parse("""
        { "items": [
          { "item": 1, "ops": [] },
          { "item": 2, "ops": [] },
          { "item": 5, "ops": [] }
        ] }
        """);

    [Fact]
    public void TryParse_All_ReturnsEveryItemNumberInChangesetOrder()
    {
        bool ok = ItemSelector.TryParse("all", ThreeItemChangeset, out int[] numbers, out string? error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal([1, 2, 5], numbers);
    }

    [Fact]
    public void TryParse_CommaSeparatedList_ReturnsThoseNumbers()
    {
        bool ok = ItemSelector.TryParse("1,5", ThreeItemChangeset, out int[] numbers, out string? error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal([1, 5], numbers);
    }

    [Fact]
    public void TryParse_SingleNumber_ReturnsOneEntry()
    {
        bool ok = ItemSelector.TryParse("2", ThreeItemChangeset, out int[] numbers, out _);

        Assert.True(ok);
        Assert.Equal([2], numbers);
    }

    [Theory]
    [InlineData("")]
    [InlineData("all,1")]
    [InlineData("1,two")]
    [InlineData("1;2")]
    public void TryParse_Malformed_FailsWithAnActionableError(string items)
    {
        bool ok = ItemSelector.TryParse(items, ThreeItemChangeset, out int[] numbers, out string? error);

        Assert.False(ok);
        Assert.Empty(numbers);
        Assert.Contains(items, error);
    }
}
