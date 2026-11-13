using System.Text.Json;
using GedCore.Matching;
using GedFire.Mcp;
using ModelContextProtocol.Protocol;

namespace GedCore.Tests;

public class MatchRecordToolTests : IDisposable
{
    readonly TempDocumentSessions _sessions = new();
    readonly string _dir = Directory.CreateTempSubdirectory("gedfire-match-record-tests-").FullName;

    public void Dispose()
    {
        _sessions.Dispose();
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

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

    string WriteFinding(string content)
    {
        string path = Path.Combine(_dir, "F-1.json");
        File.WriteAllText(path, content);
        return path;
    }

    static MatchRecordArguments ByPath(string path, string? focus = null) =>
        new(default, default, default, focus is null ? default : Json(focus)) { FindingPath = Json(JsonSerializer.Serialize(path)) };

    [Fact]
    public async Task FindingPath_MatchesTheFilesRecordExactlyAsIfGivenInline()
    {
        var tool = Tool();
        string path = WriteFinding($$"""
            { "record": { "persons": {{HouseholdPersons}}, "relationships": {{HouseholdRelationships}}, "event": {{HouseholdEvent}} },
              "meta": { "status": "harvested" } }
            """);

        var byPath = await tool.HandleAsync(ByPath(path, focus: "1"), CancellationToken.None);
        var inline = await Call(tool, HouseholdPersons, HouseholdRelationships, HouseholdEvent, focus: "1");

        Assert.NotEqual(true, byPath.IsError);
        Assert.Equal(inline.StructuredContent!.Value.GetRawText(), byPath.StructuredContent!.Value.GetRawText());
    }

    [Fact]
    public async Task FindingPath_WithPersons_IsRefused()
    {
        string path = WriteFinding("""{ "record": { "persons": [{ "name": "John Ashworth" }] } }""");

        var result = await Tool().HandleAsync(ByPath(path) with { Persons = Json(HouseholdPersons) }, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal("give findingPath, or persons, relationships and event, not both.", ErrorText(result));
    }

    [Fact]
    public async Task FindingPath_AFileWithNoRecord_IsRefusedByName()
    {
        string path = WriteFinding("""{ "meta": {} }""");

        var result = await Tool().HandleAsync(ByPath(path), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal($"{path} has no record.", ErrorText(result));
    }

    [Fact]
    public async Task FindingPath_AFileOutsideTheContract_IsRefusedNamingTheKey()
    {
        string path = WriteFinding("""{ "record": { "persons": [{ "name": "John Ashworth" }] }, "status": "new" }""");

        var result = await Tool().HandleAsync(ByPath(path), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("\"status\" is not a finding-file key", ErrorText(result));
    }

    [Fact]
    public async Task ABoundPerson_IsSingleOnTheBindingAndTheSurveyStillListsTheOthers()
    {
        var result = await Call(Tool(), """[{ "name": "John Ashworth", "xref": "@I10@" }, { "name": "Mary Ashworth" }]""",
            HouseholdRelationships, HouseholdEvent, focus: "0");

        var person = result.StructuredContent!.Value.GetProperty("persons")[0];
        Assert.Equal("single", person.GetProperty("verdict").GetString());
        Assert.True(person.GetProperty("bound").GetBoolean());
        Assert.Equal("@I10@", person.GetProperty("confidentMatchXref").GetString());
        Assert.Equal("@I10@", person.GetProperty("candidates")[0].GetProperty("xref").GetString());
        var rival = person.GetProperty("sameNameSurvey").EnumerateArray().Single(e => e.GetProperty("xref").GetString() == "@I1@");
        Assert.Equal("the record binds John Ashworth to @I10@", rival.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task ABoundKin_AnchorsTheOtherPeopleWhateverItsSpelling()
    {
        var result = await Call(Tool(), """[{ "name": "John Ashworth", "birthYear": 1848 }, { "name": "Polly Whitcomb", "xref": "@I2@" }]""",
            HouseholdRelationships, HouseholdEvent, focus: "0");

        var leader = result.StructuredContent!.Value.GetProperty("persons")[0].GetProperty("candidates")[0];
        Assert.Equal("@I1@", leader.GetProperty("xref").GetString());
        var kin = Assert.Single(leader.GetProperty("matchedKin").EnumerateArray());
        Assert.Equal("@I2@", kin.GetProperty("treeXref").GetString());
    }

    [Fact]
    public async Task ABindingToNoPerson_IsRefused()
    {
        var result = await Call(Tool(), """[{ "name": "John Ashworth", "xref": "@I99@" }]""");

        Assert.True(result.IsError);
        Assert.Equal("persons[0].xref @I99@ is not a person in the document.", ErrorText(result));
    }

    const string MarriedNameGed = """
        0 HEAD
        1 GEDC
        2 VERS 7.0
        0 @I1@ INDI
        1 NAME Abner /Quillfeather/
        1 SEX M
        1 FAMS @F1@
        0 @I2@ INDI
        1 NAME Sarah Eastman /Nichols/
        1 SEX F
        1 FAMS @F1@
        0 @I3@ INDI
        1 NAME Sarah E. /Quillfeather/
        1 SEX F
        0 @I4@ INDI
        1 NAME Ruth /Quillfeather/
        1 SEX F
        1 FAMC @F1@
        0 @F1@ FAM
        1 HUSB @I1@
        1 WIFE @I2@
        1 CHIL @I4@
        0 TRLR
        """;

    async Task<JsonElement> MatchHousehold(string wifeName, string? childName = null)
    {
        string persons = $$"""[{ "name": "A. Quillfeather", "xref": "@I1@" }, { "name": "{{wifeName}}" }{{(childName is null ? "" : $$""", { "name": "{{childName}}" }""")}}]""";
        string relationships = childName is null
            ? """[{ "type": "spouse-of", "from": 1, "to": 0 }]"""
            : """[{ "type": "spouse-of", "from": 1, "to": 0 }, { "type": "child-of", "from": 2, "to": 0 }]""";
        var tool = new MatchRecordTool(_sessions.Open(MarriedNameGed), new ToolGate(), NicknameDirectory.LoadEmbedded());
        var result = await Call(tool, persons, relationships, """{ "date": "1880" }""");
        Assert.NotEqual(true, result.IsError);
        return result.StructuredContent!.Value.GetProperty("persons");
    }

    [Fact]
    public async Task ABoundHeadsOnlyTreeSpouse_IsTheWifeRecordedUnderHerMarriedName()
    {
        var wife = (await MatchHousehold("Sarah E. Quillfeather"))[1];

        Assert.Equal("single", wife.GetProperty("verdict").GetString());
        Assert.Equal("@I2@", wife.GetProperty("confidentMatchXref").GetString());
        Assert.Equal("@I1@", wife.GetProperty("anchoredBy").GetString());
        Assert.False(wife.GetProperty("bound").GetBoolean());
        var decoy = wife.GetProperty("sameNameSurvey").EnumerateArray().Single(e => e.GetProperty("xref").GetString() == "@I3@");
        Assert.Equal("the record binds A. Quillfeather to @I1@, whose only matching tree spouse is @I2@", decoy.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task ABoundParentsTreeChild_IsTheRecordsChild()
    {
        var child = (await MatchHousehold("Sarah E. Quillfeather", "Ruth Quillfeather"))[2];

        Assert.Equal("@I4@", child.GetProperty("confidentMatchXref").GetString());
        Assert.Equal("@I1@", child.GetProperty("anchoredBy").GetString());
    }

    [Fact]
    public async Task ASpouseWhoseGivenNameDiffers_IsNotAnchored()
    {
        var wife = (await MatchHousehold("Lucy Quillfeather"))[1];

        Assert.Equal(JsonValueKind.Null, wife.GetProperty("anchoredBy").ValueKind);
        Assert.NotEqual("@I2@", wife.GetProperty("confidentMatchXref").GetString());
    }

    [Fact]
    public async Task FindingPath_ABoundRecordPerson_IsSingleOnTheBinding()
    {
        string path = WriteFinding("""{ "record": { "persons": [{ "name": "John Ashworth" }, { "name": "Mary Ashworth", "xref": "@I2@" }] } }""");

        var result = await Tool().HandleAsync(ByPath(path, focus: "1"), CancellationToken.None);

        var person = result.StructuredContent!.Value.GetProperty("persons")[0];
        Assert.Equal("single", person.GetProperty("verdict").GetString());
        Assert.Equal("@I2@", person.GetProperty("confidentMatchXref").GetString());
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
