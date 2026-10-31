using System.Text.Json;
using GedCore.Matching;
using GedFire.Mcp;
using ModelContextProtocol.Protocol;

namespace GedCore.Tests;

public class MatchRecordToolTests : IDisposable
{
    readonly TempDocumentSessions _sessions = new();

    public void Dispose() => _sessions.Dispose();

    const string Ged = """
        0 HEAD
        1 GEDC
        2 VERS 5.5.1
        0 @I1@ INDI
        1 NAME John /Ashworth/
        1 SEX M
        1 BIRT
        2 DATE 1848
        2 PLAC Boone County, Iowa
        1 FAMS @F1@
        0 @I2@ INDI
        1 NAME Mary /Ashworth/
        1 SEX F
        1 FAMS @F1@
        0 @I10@ INDI
        1 NAME John /Ashworth/
        1 SEX M
        1 BIRT
        2 DATE 1846
        0 @F1@ FAM
        1 HUSB @I1@
        1 WIFE @I2@
        """;

    MatchRecordTool Tool() => new(_sessions.Open(Ged), new ToolGate(), NicknameDirectory.LoadEmbedded());

    static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();

    static async Task<CallToolResult> Call(
        MatchRecordTool tool, string persons, string? relationships = null, string? recordEvent = null, string? focus = null) =>
        await tool.HandleAsync(
            Json(persons),
            relationships is null ? default : Json(relationships),
            recordEvent is null ? default : Json(recordEvent),
            focus is null ? default : Json(focus),
            CancellationToken.None);

    static string ErrorText(CallToolResult result) => ((TextContentBlock)result.Content[0]).Text;

    const string HouseholdPersons = """
        [
          { "role": "head", "name": "John Ashworth", "sex": "M", "birthYear": 1848 },
          { "role": "wife", "name": "Mary Ashworth", "sex": "F" }
        ]
        """;

    const string HouseholdRelationships = """[{ "type": "spouse-of", "from": 1, "to": 0 }]""";
    const string HouseholdEvent = """{ "date": "1880", "place": "Boone County, Iowa", "type": "census" }""";

    [Fact]
    public void Constructor_RejectsNullArguments()
    {
        var session = _sessions.Open(Ged);
        var nicknames = NicknameDirectory.LoadEmbedded();
        Assert.Throws<ArgumentNullException>(() => new MatchRecordTool(null!, new ToolGate(), nicknames));
        Assert.Throws<ArgumentNullException>(() => new MatchRecordTool(session, null!, nicknames));
        Assert.Throws<ArgumentNullException>(() => new MatchRecordTool(session, new ToolGate(), null!));
    }

    [Fact]
    public async Task MatchRecord_ReturnsAVerdictPerRecordPerson()
    {
        var result = await Call(Tool(), HouseholdPersons, HouseholdRelationships, HouseholdEvent);

        Assert.NotEqual(true, result.IsError);
        var persons = result.StructuredContent!.Value.GetProperty("persons");
        Assert.Equal(2, persons.GetArrayLength());

        var head = persons[0];
        Assert.Equal(0, head.GetProperty("index").GetInt32());
        Assert.Equal("head", head.GetProperty("role").GetString());
        Assert.Equal("single", head.GetProperty("verdict").GetString());
        Assert.Equal("@I1@", head.GetProperty("confidentMatchXref").GetString());
        Assert.False(head.GetProperty("potentialNewPerson").GetBoolean());

        var winner = head.GetProperty("candidates")[0];
        Assert.Equal("@I1@", winner.GetProperty("xref").GetString());
        Assert.True(winner.GetProperty("score").GetDouble() >= 90.0);
        var subscores = winner.GetProperty("subscores");
        Assert.Equal(100.0, subscores.GetProperty("name").GetDouble());
        Assert.Equal(100.0, subscores.GetProperty("kinOverlap").GetDouble());

        // The survey is closed: the 1846 decoy is named, with its reason.
        var survey = head.GetProperty("sameNameSurvey");
        Assert.Equal(2, survey.GetArrayLength());
        var decoy = survey.EnumerateArray().Single(e => e.GetProperty("xref").GetString() == "@I10@");
        Assert.Equal("candidate", decoy.GetProperty("outcome").GetString());
        Assert.False(string.IsNullOrEmpty(decoy.GetProperty("reason").GetString()));
    }

    [Fact]
    public async Task MatchRecord_FocusLimitsTheResultToOnePerson()
    {
        var result = await Call(Tool(), HouseholdPersons, HouseholdRelationships, HouseholdEvent, focus: "1");

        Assert.NotEqual(true, result.IsError);
        var persons = result.StructuredContent!.Value.GetProperty("persons");
        Assert.Equal(1, persons.GetArrayLength());
        Assert.Equal(1, persons[0].GetProperty("index").GetInt32());
        Assert.Equal("@I2@", persons[0].GetProperty("confidentMatchXref").GetString());
    }

    [Fact]
    public async Task MatchRecord_UnknownPerson_IsAPotentialNewPerson()
    {
        var result = await Call(Tool(), """[{ "name": "Zebulon Moxley" }]""");

        Assert.NotEqual(true, result.IsError);
        var person = result.StructuredContent!.Value.GetProperty("persons")[0];
        Assert.Equal("none", person.GetProperty("verdict").GetString());
        Assert.True(person.GetProperty("potentialNewPerson").GetBoolean());
        Assert.Equal(0, person.GetProperty("candidates").GetArrayLength());
    }

    [Fact]
    public async Task MatchRecord_IsDeterministic()
    {
        var tool = Tool();

        var first = await Call(tool, HouseholdPersons, HouseholdRelationships, HouseholdEvent);
        var second = await Call(tool, HouseholdPersons, HouseholdRelationships, HouseholdEvent);

        Assert.Equal(
            first.StructuredContent!.Value.GetRawText(),
            second.StructuredContent!.Value.GetRawText());
    }

    [Fact]
    public async Task MatchRecord_MissingPersons_IsAnError()
    {
        var result = await Tool().HandleAsync(default, default, default, default, CancellationToken.None);

        Assert.Equal(true, result.IsError);
        Assert.StartsWith("persons is required", ErrorText(result));
    }

    [Fact]
    public async Task MatchRecord_InvalidArguments_ReportTheOffendingField()
    {
        var result = await Call(
            Tool(),
            """[{ "name": "John Ashworth", "birthYear": 1848, "age": 32 }]""",
            recordEvent: """{ "date": "1880" }""");

        Assert.Equal(true, result.IsError);
        Assert.Equal("persons[0]: give birthYear or age, not both.", ErrorText(result));
    }

    async Task<JsonElement> Head(string? maxCandidates, string? compact)
    {
        var result = await Tool().HandleAsync(
            Json(HouseholdPersons), Json(HouseholdRelationships), Json(HouseholdEvent), Json("0"),
            maxCandidates is null ? default : Json(maxCandidates), compact is null ? default : Json(compact), CancellationToken.None);
        Assert.NotEqual(true, result.IsError);
        return result.StructuredContent!.Value.GetProperty("persons")[0];
    }

    [Fact]
    public async Task MatchRecord_MaxCandidates_KeepsTheVerdictAndReportsWhatItDropped()
    {
        var uncapped = await Head(null, null);
        var capped = await Head("1", null);

        Assert.Equal(1, capped.GetProperty("candidates").GetArrayLength());
        Assert.Equal(1, capped.GetProperty("candidatesOmitted").GetInt32());
        Assert.Equal(uncapped.GetProperty("verdict").GetString(), capped.GetProperty("verdict").GetString());
        Assert.Equal(uncapped.GetProperty("confidentMatchXref").GetString(), capped.GetProperty("confidentMatchXref").GetString());
        Assert.Equal("@I10@", capped.GetProperty("runnerUpXref").GetString());
        Assert.Equal(2, capped.GetProperty("sameNameSurvey").GetArrayLength());
    }

    [Fact]
    public async Task MatchRecord_Compact_DropsSubscoresAndKeepsSurveyReasons()
    {
        var head = await Head(null, "true");

        var winner = head.GetProperty("candidates")[0];
        Assert.Equal("1848-?", winner.GetProperty("lifespan").GetString());
        Assert.False(winner.TryGetProperty("subscores", out _));
        var decoy = head.GetProperty("sameNameSurvey").EnumerateArray().Single(e => e.GetProperty("xref").GetString() == "@I10@");
        Assert.False(decoy.TryGetProperty("outcome", out _));
        Assert.False(string.IsNullOrEmpty(decoy.GetProperty("reason").GetString()));
    }

    [Theory]
    [InlineData("0", null)]
    [InlineData("21", null)]
    [InlineData(null, "\"yes\"")]
    public async Task MatchRecord_InvalidViewOptions_AreErrors(string? maxCandidates, string? compact)
    {
        var result = await Tool().HandleAsync(
            Json(HouseholdPersons), default, default, default,
            maxCandidates is null ? default : Json(maxCandidates), compact is null ? default : Json(compact), CancellationToken.None);

        Assert.Equal(true, result.IsError);
        Assert.Contains(maxCandidates is null ? "compact" : "maxCandidates", ErrorText(result));
    }

    [Fact]
    public void ToMcpServerTool_IsAdvertisedReadOnlyAndIdempotent()
    {
        var tool = Tool().ToMcpServerTool();

        Assert.Equal("match_record", tool.ProtocolTool.Name);
        Assert.Equal(true, tool.ProtocolTool.Annotations!.ReadOnlyHint);
        Assert.Equal(false, tool.ProtocolTool.Annotations.DestructiveHint);
        Assert.Equal(true, tool.ProtocolTool.Annotations.IdempotentHint);
    }
}
