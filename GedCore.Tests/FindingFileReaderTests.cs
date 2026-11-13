using System.Text;
using GedFire.Mcp;

namespace GedCore.Tests;

public class FindingFileReaderTests
{
    static FindingFile Read(string json, string name = "F-100.json") => FindingFileReader.Read(new MemoryStream(Encoding.UTF8.GetBytes(json)), name);

    static string WithKey(string fixture, string key) =>
        FindingFixtures.Read(fixture).TrimEnd().TrimEnd('}') + $", {key} }}";

    [Fact]
    public void ADraftWithArgumentAndMeta_IsReady()
    {
        var file = FindingFileReader.Read(FindingFixtures.Open("F-100.json"), "F-100.json");

        Assert.True(file.ReadyToDraft, string.Join("; ", file.Errors));
        Assert.Equal("low", (string?)file.Draft!.Argument!["difficulty"]);
    }

    [Theory]
    [InlineData("\"persons\": []", "persons")]
    [InlineData("\"correlation\": { \"kind\": \"add\" }", "correlation")]
    [InlineData("\"status\": \"new\"", "status")]
    public void AnUnknownTopLevelKey_IsRefusedNamingTheFileAndKey(string member, string key)
    {
        var file = Read(WithKey("F-100.json", member));

        Assert.False(file.ReadyToDraft);
        Assert.Equal(
            $"F-100.json: \"{key}\" is not a finding-file key; accepted keys: draft, argument, draftGaps, record, meta.",
            Assert.Single(file.Errors));
    }

    [Fact]
    public void AParkVerdictInTheCallersOwnField_IsAnUnknownKeyNotAParkedFinding()
    {
        var file = Read("""{ "correlation": { "kind": "park" }, "meta": {} }""");

        Assert.Contains("\"correlation\" is not a finding-file key", Assert.Single(file.Errors));
        Assert.Equal("F-100.json has no draft; correlate the finding to write one.", file.NotReadyReason);
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
    public void ADraftIdThatDiffersFromTheFileName_IsRefused()
    {
        var file = FindingFileReader.Read(FindingFixtures.Open("F-100.json"), "other.json");

        Assert.False(file.ReadyToDraft);
        Assert.Equal("other.json: draft.id \"F-100\" differs from the file name; name the file F-100.json.", Assert.Single(file.Errors));
    }

    [Theory]
    [InlineData("""{ "meta": "new" }""", "F-100.json: meta must be an object.")]
    [InlineData("""{ "draftGaps": ["a", 2] }""", "F-100.json: draftGaps[1] must be a string.")]
    [InlineData("""{ "record": { "persons": [{ "name": "A" }], "focus": 0 } }""", "F-100.json: record.focus is not a recognized field; accepted fields: persons, relationships, event.")]
    [InlineData("""{ "record": { "persons": [{ "name": "A", "deathYear": "x" }] } }""", "F-100.json: record.persons[0].deathYear must be an integer between 1 and 9999, not a string.")]
    [InlineData("""{ "record": { "persons": [{ "name": "A", "xref": "I2" }] } }""", "F-100.json: record.persons[0].xref must be an xref such as \"@I1@\", got \"I2\".")]
    [InlineData("""{ "record": { "relationships": [] } }""", "F-100.json: record.persons is required: an array of 1 to 50 record persons, each with at least a name.")]
    [InlineData("""{ "argument": { "difficulty": "easy" } }""", "F-100.json: argument.difficulty must be one of low, medium, high.")]
    public void AMalformedSlot_IsAnErrorNamingTheFileAndField(string json, string error) =>
        Assert.Equal(error, Assert.Single(Read(json).Errors));

    [Fact]
    public void ARecordEventDateThatIsNotAGedcomDate_IsRefused()
    {
        var file = Read("""{ "record": { "persons": [{ "name": "A" }], "event": { "date": "accessed 2026-07-06" } } }""");

        Assert.StartsWith("F-100.json: record.event.date \"accessed 2026-07-06\" is not a GEDCOM date", Assert.Single(file.Errors));
    }

    [Fact]
    public void MetaIsNeverReadBeyondItsSlot()
    {
        var file = Read(FindingFixtures.Read("F-100.json").Replace("\"meta\": { \"status\": \"new\", \"anything\": [1, 2] }", "\"meta\": { \"draft\": 7 }"));

        Assert.True(file.ReadyToDraft);
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
