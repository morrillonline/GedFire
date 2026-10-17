using System.Text.Json;
using GedCore.Drafting;

namespace GedCore.Tests;

public class FindingArgumentReaderTests
{
    const string Full = """
        { "anchors": ["Born in Salem in 1850.", "Married Ann Blake in 1875."],
          "identity": "Searched for Harry Morrill born 1850: three candidates, the runner-up @I9@ at 0.41.",
          "exclusions": [ { "name": "Harry Morrill", "xref": "@I9@", "fact": "a death in 1801", "finding": "F-2" } ],
          "cautions": "The census gives 1851.",
          "difficulty": "medium" }
        """;

    static (System.Text.Json.Nodes.JsonObject? Argument, List<string> Errors) Read(string json)
    {
        var errors = new List<string>();
        var argument = FindingArgumentReader.Read(JsonDocument.Parse(json).RootElement, "argument", errors);
        return (argument, errors);
    }

    [Fact]
    public void AValidArgument_IsReturnedExactlyAsGiven()
    {
        var (argument, errors) = Read(Full);

        Assert.Empty(errors);
        Assert.True(JsonElement.DeepEquals(JsonDocument.Parse(Full).RootElement, JsonDocument.Parse(argument!.ToJsonString()).RootElement));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{ "identity": "Surveyed outside find_person." }""")]
    [InlineData("""{ "identity": "One match.", "difficulty": "HIGH" }""")]
    [InlineData("""{ "cautions": null, "identity": null }""")]
    public void EveryFieldIsOptional(string json)
    {
        var (argument, errors) = Read(json);

        Assert.Empty(errors);
        Assert.NotNull(argument);
    }

    [Theory]
    [InlineData("""{ "anchors": "one" }""", "argument.anchors must be an array.")]
    [InlineData("""{ "anchors": [1] }""", "argument.anchors[0] must be a string.")]
    [InlineData("""{ "difficulty": "easy" }""", "argument.difficulty must be one of low, medium, high.")]
    [InlineData("""{ "cautions": ["a"] }""", "argument.cautions must be a string.")]
    [InlineData("""{ "identity": 5 }""", "argument.identity must be a string.")]
    [InlineData("""{ "identity": { "query": "Harry" } }""", "argument.identity must be a string.")]
    [InlineData("""{ "exclusions": [ "Ann" ] }""", "argument.exclusions[0] must be an object.")]
    [InlineData("""{ "exclusions": [ { "name": "Ann", "why": "x" } ] }""", "argument.exclusions[0].why is not a recognized field")]
    [InlineData("""{ "mood": "x" }""", "argument.mood is not a recognized field")]
    public void AWrongShape_IsRejectedNamingTheField(string json, string expected)
    {
        var (argument, errors) = Read(json);

        Assert.Null(argument);
        Assert.Contains(expected, Assert.Single(errors));
    }

    [Fact]
    public void ANonObject_IsRejected() =>
        Assert.Contains("argument must be an object", Assert.Single(Read("[]").Errors));
}
