using System.Text;
using GedFire.Mcp;

namespace GedCore.Tests;

public class FindingFileReaderTests
{
    static FindingFile Read(string json, string name = "F-100.json") => FindingFileReader.Read(new MemoryStream(Encoding.UTF8.GetBytes(json)), name);

    static string WithKey(string fixture, string key) =>
        FindingFixtures.Read(fixture).TrimEnd().TrimEnd('}') + $", {key} }}";

    [Fact]
    public void ADraftWithArgumentAndMeta_IsReadyWithNoWarnings()
    {
        var file = FindingFileReader.Read(FindingFixtures.Open("F-100.json"), "F-100.json");

        Assert.True(file.ReadyToDraft, string.Join("; ", file.Errors));
        Assert.Empty(file.Warnings);
        Assert.Equal("low", (string?)file.Draft!.Argument!["difficulty"]);
    }

    [Theory]
    [InlineData("\"persons\": []", "persons")]
    [InlineData("\"correlation\": { \"kind\": \"add\" }", "correlation")]
    [InlineData("\"status\": \"new\"", "status")]
    public void AnUnknownTopLevelKey_IsAWarningNamingTheFileAndKey(string member, string key)
    {
        var file = Read(WithKey("F-100.json", member));

        Assert.True(file.ReadyToDraft);
        var warning = Assert.Single(file.Warnings);
        Assert.StartsWith($"F-100.json: \"{key}\" is not a finding-file key", warning);
    }

    [Fact]
    public void ARecordAndMetaOnly_IsValidButHasNoDraft()
    {
        var file = FindingFileReader.Read(FindingFixtures.Open("F-101.json"), "F-101.json");

        Assert.True(file.IsValid, string.Join("; ", file.Errors));
        Assert.NotNull(file.Record);
        Assert.False(file.ReadyToDraft);
        Assert.Equal("F-101.json has no draft; correlate the finding to write one.", file.NotReadyReason);
    }

    [Fact]
    public void DraftGaps_LeaveAValidFileNotReady()
    {
        var file = FindingFileReader.Read(FindingFixtures.Open("F-102.json"), "F-102.json");

        Assert.True(file.IsValid);
        Assert.Equal(["spouse unbound"], file.Gaps);
        Assert.StartsWith("F-102.json has draftGaps: spouse unbound", file.NotReadyReason);
    }

    [Fact]
    public void ADraftIdThatDiffersFromTheFileName_IsAWarning()
    {
        var file = FindingFileReader.Read(FindingFixtures.Open("F-100.json"), "other.json");

        Assert.True(file.ReadyToDraft);
        Assert.Contains("draft.id \"F-100\" differs from the file name", Assert.Single(file.Warnings));
    }

    [Theory]
    [InlineData("""{ "meta": "new" }""", "F-100.json: meta must be an object.")]
    [InlineData("""{ "draftGaps": ["a", 2] }""", "F-100.json: draftGaps[1] must be a string.")]
    [InlineData("""{ "record": { "persons": [{ "name": "A" }], "focus": 0 } }""", "F-100.json: record.focus is not a recognized field; accepted fields: persons, relationships, event.")]
    [InlineData("""{ "record": { "persons": [{ "name": "A", "deathYear": "x" }] } }""", "F-100.json: record.persons[0].deathYear must be an integer between 1 and 9999, not a string.")]
    [InlineData("""{ "record": { "relationships": [] } }""", "F-100.json: record.persons is required: an array of 1 to 50 record persons, each with at least a name.")]
    [InlineData("""{ "argument": { "difficulty": "easy" } }""", "F-100.json: argument.difficulty must be one of low, medium, high.")]
    public void AMalformedSlot_IsAnErrorNamingTheFileAndField(string json, string error) =>
        Assert.Equal(error, Assert.Single(Read(json).Errors));

    [Fact]
    public void MetaIsNeverReadBeyondItsSlot()
    {
        var file = Read(FindingFixtures.Read("F-100.json").Replace("\"meta\": { \"status\": \"new\", \"anything\": [1, 2] }", "\"meta\": { \"draft\": 7 }"));

        Assert.True(file.ReadyToDraft);
        Assert.Empty(file.Warnings);
        Assert.Equal("F-100", file.Draft!.Id);
    }

    [Fact]
    public void AnUnreadableFile_IsNotJudgedForReadiness()
    {
        var file = Read("{ nope");

        Assert.Contains("is not valid JSON", Assert.Single(file.Errors));
        Assert.Null(file.NotReadyReason);
    }
}
