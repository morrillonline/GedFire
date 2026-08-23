using System.Text.Json;
using GedCore.Matching;
using GedFire.Mcp;
using GedFire.Match;
using ModelContextProtocol.Protocol;

namespace GedCore.Tests;

public class FindPersonToolTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("gedfire-findperson-tests-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    // -------------------------------------------------------------------
    // Fixtures and helpers
    // -------------------------------------------------------------------

    const string RichFamilyGed = """
        0 @I1@ INDI
        1 NAME Frederick /Morrill/
        1 SEX M
        1 BIRT
        2 DATE 12 MAR 1841
        2 PLAC Gorham, Maine
        1 DEAT
        2 DATE 3 JUN 1919
        2 PLAC Portland, Maine
        1 FAMC @F1@
        1 FAMS @F2@
        1 FAMS @F3@
        1 FAMS @F4@
        0 @I2@ INDI
        1 NAME Ansel /Morrill/
        1 SEX M
        1 FAMS @F1@
        0 @I3@ INDI
        1 NAME Mary /Peaslee/
        1 SEX F
        1 FAMS @F1@
        0 @I4@ INDI
        1 NAME Sarah /Blake/
        1 SEX F
        1 FAMS @F2@
        0 @I5@ INDI
        1 NAME Second /Wife/
        1 SEX F
        1 FAMS @F3@
        0 @F1@ FAM
        1 HUSB @I2@
        1 WIFE @I3@
        1 CHIL @I1@
        0 @F2@ FAM
        1 HUSB @I1@
        1 WIFE @I4@
        1 MARR
        2 DATE 9 SEP 1863
        0 @F3@ FAM
        1 HUSB @I1@
        1 WIFE @I5@
        0 @F4@ FAM
        1 HUSB @I1@
        """;

    const string SparsePersonGed = """
        0 @I1@ INDI
        1 NAME Alone /Nobody/
        1 SEX M
        """;

    static FindPersonTool ToolOver(string gedText, out string path, string dir, NicknameDirectory? nicknames = null)
    {
        path = Path.Combine(dir, Guid.NewGuid() + ".ged");
        File.WriteAllText(path, gedText);
        var model = MatchTestModels.Build(gedText);
        var info = new FileInfo(path);
        var snapshot = new DocumentSnapshot(model, "7.0", File.GetLastWriteTimeUtc(path), info.Length);
        var session = new DocumentSession(path, snapshot);
        return new FindPersonTool(session, new ToolGate(), nicknames ?? EmptyNicknames());
    }

    static NicknameDirectory EmptyNicknames() =>
        new(new MemoryStream(System.Text.Encoding.UTF8.GetBytes("""{"male":{},"female":{}}""")));

    static JsonElement StructuredContent(CallToolResult result) => result.StructuredContent!.Value;

    static string TextOf(CallToolResult result) => Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text;

    static JsonElement Json(string literal)
    {
        using var doc = JsonDocument.Parse(literal);
        return doc.RootElement.Clone();
    }

    // -------------------------------------------------------------------
    // Success: single match, full field mapping
    // -------------------------------------------------------------------

    [Fact]
    public async Task HandleAsync_SingleMatch_IsNotError_AndStructuredContentMatchesTextFallback()
    {
        var tool = ToolOver(RichFamilyGed, out _, _dir);
        var result = await tool.HandleAsync("Frederick Morrill", null, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.NotNull(result.StructuredContent);
        var text = TextOf(result);
        // The text block is the compact JSON serialization of the same value.
        Assert.Equal(
            JsonSerializer.Serialize(StructuredContent(result), CallToolResults.JsonOptions),
            text);
        Assert.DoesNotContain("\n", text); // no indentation in the text block
    }

    [Fact]
    public async Task HandleAsync_SingleMatch_MapsIdentityAndBirthDeath()
    {
        var tool = ToolOver(RichFamilyGed, out _, _dir);
        var result = await tool.HandleAsync("Frederick Morrill", null, CancellationToken.None);

        var root = StructuredContent(result);
        Assert.Equal("single", root.GetProperty("matchType").GetString());

        var person = root.GetProperty("person");
        Assert.Equal("@I1@", person.GetProperty("xref").GetString());
        Assert.Equal("Frederick Morrill", person.GetProperty("name").GetString());

        var birth = person.GetProperty("birth");
        Assert.Equal("12 MAR 1841", birth.GetProperty("date").GetString());
        Assert.Equal(1841, birth.GetProperty("year").GetInt32());
        Assert.Equal(JsonValueKind.Null, birth.GetProperty("qualifier").ValueKind);
        Assert.Equal("Gorham, Maine", birth.GetProperty("place").GetString());

        var death = person.GetProperty("death");
        Assert.Equal("3 JUN 1919", death.GetProperty("date").GetString());
        Assert.Equal("Portland, Maine", death.GetProperty("place").GetString());
    }

    [Fact]
    public async Task HandleAsync_SingleMatch_MapsFamiliesIncludingChildlessAndSpouselessMarriages()
    {
        var tool = ToolOver(RichFamilyGed, out _, _dir);
        var result = await tool.HandleAsync("Frederick Morrill", null, CancellationToken.None);

        var families = StructuredContent(result).GetProperty("person").GetProperty("families");

        var asChild = families.GetProperty("asChild").EnumerateArray().Select(e => e.GetString()).ToList();
        Assert.Equal(["@F1@"], asChild);

        var asParent = families.GetProperty("asParent").EnumerateArray().ToList();
        Assert.Equal(3, asParent.Count);

        var f2 = asParent.Single(f => f.GetProperty("xref").GetString() == "@F2@");
        Assert.Equal("9 SEP 1863", f2.GetProperty("marriageDate").GetString());
        Assert.Equal("Sarah Blake", f2.GetProperty("spouseName").GetString());

        // Childless marriage: no MARR date, but the family xref and spouse
        // name are still present — childless marriages are included, not
        // skipped.
        var f3 = asParent.Single(f => f.GetProperty("xref").GetString() == "@F3@");
        Assert.Equal(JsonValueKind.Null, f3.GetProperty("marriageDate").ValueKind);
        Assert.Equal("Second Wife", f3.GetProperty("spouseName").GetString());

        // A family with no resolvable spouse at all: xref present, both
        // marriageDate and spouseName null.
        var f4 = asParent.Single(f => f.GetProperty("xref").GetString() == "@F4@");
        Assert.Equal(JsonValueKind.Null, f4.GetProperty("marriageDate").ValueKind);
        Assert.Equal(JsonValueKind.Null, f4.GetProperty("spouseName").ValueKind);
    }

    [Fact]
    public async Task HandleAsync_PersonWithNoEventsOrFamilies_MapsNullsAndEmptyArrays()
    {
        var tool = ToolOver(SparsePersonGed, out _, _dir);
        var result = await tool.HandleAsync("Alone Nobody", null, CancellationToken.None);

        var person = StructuredContent(result).GetProperty("person");
        Assert.Equal(JsonValueKind.Null, person.GetProperty("birth").ValueKind);
        Assert.Equal(JsonValueKind.Null, person.GetProperty("death").ValueKind);
        Assert.Empty(person.GetProperty("families").GetProperty("asChild").EnumerateArray());
        Assert.Empty(person.GetProperty("families").GetProperty("asParent").EnumerateArray());
    }

    // -------------------------------------------------------------------
    // Success: candidate list mapping
    // -------------------------------------------------------------------

    const string TwoCandidatesGed = """
        0 @I1@ INDI
        1 NAME Jane /Doe/
        1 SEX F
        1 BIRT
        2 DATE 1900
        1 FAMC @F1@
        1 FAMS @F2@
        0 @I2@ INDI
        1 NAME Jane /Doe/
        1 SEX F
        0 @I3@ INDI
        1 NAME Robert /Fields/
        1 SEX M
        1 FAMS @F1@
        0 @I4@ INDI
        1 NAME John /Smith/
        1 SEX M
        1 FAMS @F2@
        0 @F1@ FAM
        1 HUSB @I3@
        1 CHIL @I1@
        0 @F2@ FAM
        1 HUSB @I4@
        1 WIFE @I1@
        """;

    [Fact]
    public async Task HandleAsync_CandidateList_MapsParentsAndSpousesPerCandidate()
    {
        var tool = ToolOver(TwoCandidatesGed, out _, _dir);
        var result = await tool.HandleAsync("Jane Doe", null, CancellationToken.None);

        var root = StructuredContent(result);
        Assert.Equal("candidates", root.GetProperty("matchType").GetString());
        Assert.Equal(JsonValueKind.False, root.GetProperty("truncated").ValueKind);

        var candidates = root.GetProperty("candidates").EnumerateArray().ToList();
        Assert.Equal(2, candidates.Count);

        var withFamily = candidates.Single(c => c.GetProperty("xref").GetString() == "@I1@");
        Assert.Equal(1900, withFamily.GetProperty("birth").GetProperty("year").GetInt32());
        Assert.Equal("Robert Fields", withFamily.GetProperty("parents").GetProperty("father").GetString());
        Assert.Equal(JsonValueKind.Null, withFamily.GetProperty("parents").GetProperty("mother").ValueKind);
        Assert.Equal(["John Smith"], withFamily.GetProperty("spouses").EnumerateArray().Select(e => e.GetString()));

        var bare = candidates.Single(c => c.GetProperty("xref").GetString() == "@I2@");
        Assert.Equal(JsonValueKind.Null, bare.GetProperty("birth").ValueKind);
        Assert.Equal(JsonValueKind.Null, bare.GetProperty("parents").ValueKind);
        Assert.Empty(bare.GetProperty("spouses").EnumerateArray());

        // Candidates never carry family xrefs — only a resolved single match
        // does.
        Assert.False(withFamily.TryGetProperty("families", out _));
    }

    // -------------------------------------------------------------------
    // Success: no match, suggestions with reasons
    // -------------------------------------------------------------------

    [Fact]
    public async Task HandleAsync_NoMatch_MapsSuggestionReasonsAsSchemaStrings()
    {
        var tool = ToolOver(SparsePersonGed, out _, _dir);
        // Exact surname match ("Nobody") but a zero-overlap given name lands
        // in the 55-69 suggestion band with a close-spelling reason (the
        // same construction PersonMatcherTests uses).
        var result = await tool.HandleAsync("Zzqxvw Nobody", null, CancellationToken.None);

        var root = StructuredContent(result);
        Assert.Equal("none", root.GetProperty("matchType").GetString());
        var suggestion = Assert.Single(root.GetProperty("suggestions").EnumerateArray());
        Assert.Equal("@I1@", suggestion.GetProperty("xref").GetString());
        Assert.Equal("close spelling", suggestion.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task HandleAsync_WhollyUnrelatedQuery_ReturnsEmptySuggestions()
    {
        var tool = ToolOver(SparsePersonGed, out _, _dir);
        var result = await tool.HandleAsync("Zzqxvw Bbdfghj", null, CancellationToken.None);

        var root = StructuredContent(result);
        Assert.Equal("none", root.GetProperty("matchType").GetString());
        Assert.Empty(root.GetProperty("suggestions").EnumerateArray());
    }

    // I1 tying with a same-named rival (distinguishable only by a birth-year
    // hint) so the same person's spouse list can be observed through both
    // output shapes: as a CandidateIdentity (no hint) and as the resolved
    // person's families.asParent (with the disambiguating hint).
    const string RichFamilyGedWithRival = RichFamilyGed + """

        0 @I6@ INDI
        1 NAME Frederick /Morrill/
        1 SEX M
        1 BIRT
        2 DATE 1750

        """;

    [Fact]
    public async Task CandidateSpousesList_AgreesWithSingleMatchAsParentSpouseNames()
    {
        var tool = ToolOver(RichFamilyGedWithRival, out _, _dir);

        var candidateResult = await tool.HandleAsync("Frederick Morrill", null, CancellationToken.None);
        var candidateRoot = StructuredContent(candidateResult);
        Assert.Equal("candidates", candidateRoot.GetProperty("matchType").GetString());
        var candidate = candidateRoot.GetProperty("candidates").EnumerateArray()
            .Single(c => c.GetProperty("xref").GetString() == "@I1@");
        var candidateSpouses = candidate.GetProperty("spouses").EnumerateArray()
            .Select(e => e.GetString()).ToList();

        var singleResult = await tool.HandleAsync(
            "Frederick Morrill", new FindPersonHintsArgs { BirthYear = 1841 }, CancellationToken.None);
        var singleRoot = StructuredContent(singleResult);
        Assert.Equal("single", singleRoot.GetProperty("matchType").GetString());
        Assert.Equal("@I1@", singleRoot.GetProperty("person").GetProperty("xref").GetString());
        var asParentSpouseNames = singleRoot.GetProperty("person").GetProperty("families").GetProperty("asParent")
            .EnumerateArray()
            .Select(f => f.GetProperty("spouseName"))
            .Where(v => v.ValueKind != JsonValueKind.Null)
            .Select(v => v.GetString())
            .ToList();

        // CandidateIdentity.spouses and a resolved person's
        // families.asParent[].spouseName are computed by two separate
        // mapping functions over the same FamSpouse/SpouseOf data; they
        // must describe the same set of marriages, in the same order.
        Assert.Equal(["Sarah Blake", "Second Wife"], candidateSpouses);
        Assert.Equal(asParentSpouseNames, candidateSpouses);
    }

    // -------------------------------------------------------------------
    // Hints flow through to the matcher
    // -------------------------------------------------------------------

    [Fact]
    public async Task HandleAsync_HintsArePassedThroughToTheMatcher()
    {
        const string ged = """
            0 @I1@ INDI
            1 NAME Frederick /Morrill/
            1 SEX M
            1 BIRT
            2 DATE 12 MAR 1841
            0 @I2@ INDI
            1 NAME Frederick /Morrill/
            1 SEX M
            1 BIRT
            2 DATE 12 MAR 1900
            """;
        var tool = ToolOver(ged, out _, _dir);

        var result = await tool.HandleAsync(
            "Frederick Morrill",
            new FindPersonHintsArgs { BirthYear = 1841 },
            CancellationToken.None);

        var root = StructuredContent(result);
        Assert.Equal("single", root.GetProperty("matchType").GetString());
        Assert.Equal("@I1@", root.GetProperty("person").GetProperty("xref").GetString());
    }

    // -------------------------------------------------------------------
    // Errors
    // -------------------------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task HandleAsync_BlankQuery_ReturnsIsErrorWithoutStructuredContent(string query)
    {
        var tool = ToolOver(SparsePersonGed, out _, _dir);
        var result = await tool.HandleAsync(query, null, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Null(result.StructuredContent);
        Assert.Contains("blank", TextOf(result));
    }

    [Fact]
    public async Task HandleAsync_ReloadFailure_ReturnsIsErrorWithExceptionDetails()
    {
        var tool = ToolOver(SparsePersonGed, out string path, _dir);
        File.Delete(path);

        var result = await tool.HandleAsync("Alone Nobody", null, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Null(result.StructuredContent);
        string text = TextOf(result);
        Assert.Contains(nameof(DocumentReloadException), text);
        Assert.Contains(path, text);
    }

    [Fact]
    public async Task HandleAsync_RateLimitRejection_ReturnsIsErrorRatherThanThrowing()
    {
        var now = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var path = Path.Combine(_dir, "gate.ged");
        File.WriteAllText(path, SparsePersonGed);
        var model = MatchTestModels.Build(SparsePersonGed);
        var info = new FileInfo(path);
        var snapshot = new DocumentSnapshot(model, "7.0", File.GetLastWriteTimeUtc(path), info.Length);
        var session = new DocumentSession(path, snapshot);
        var gate = new ToolGate(() => now);
        var tool = new FindPersonTool(session, gate, EmptyNicknames());

        for (int i = 0; i < ToolGate.MaxCallsPerMinute; i++)
            await tool.HandleAsync("Alone Nobody", null, CancellationToken.None);

        var result = await tool.HandleAsync("Alone Nobody", null, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains(nameof(ToolRateLimitExceededException), TextOf(result));
    }

    [Fact]
    public async Task HandleAsync_AlreadyCancelledToken_PropagatesRatherThanReturningIsError()
    {
        var tool = ToolOver(SparsePersonGed, out _, _dir);
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => tool.HandleAsync("Alone Nobody", null, cts.Token));
    }

    // -------------------------------------------------------------------
    // Tool metadata
    // -------------------------------------------------------------------

    [Fact]
    public void ToMcpServerTool_DeclaresNameDescriptionSchemasAndAnnotations()
    {
        var tool = ToolOver(SparsePersonGed, out _, _dir).ToMcpServerTool();

        Assert.Equal(FindPersonTool.ToolName, tool.ProtocolTool.Name);
        Assert.Equal(FindPersonTool.Description, tool.ProtocolTool.Description);
        Assert.True(tool.ProtocolTool.Annotations!.ReadOnlyHint);
        Assert.False(tool.ProtocolTool.Annotations!.DestructiveHint);
        Assert.True(tool.ProtocolTool.Annotations!.IdempotentHint);

        // The declared schemas are exactly the hand-written constants.
        using var expectedInput = JsonDocument.Parse(FindPersonTool.InputSchemaJson);
        Assert.Equal(
            JsonSerializer.Serialize(expectedInput.RootElement),
            JsonSerializer.Serialize(tool.ProtocolTool.InputSchema));

        using var expectedOutput = JsonDocument.Parse(FindPersonTool.OutputSchemaJson);
        Assert.Equal(
            JsonSerializer.Serialize(expectedOutput.RootElement),
            JsonSerializer.Serialize(tool.ProtocolTool.OutputSchema!.Value));
    }

    // -------------------------------------------------------------------
    // Unified output shape: confidentMatchXref/Score, totalMatches,
    // truncated, and per-candidate/suggestion matchScore are present in
    // every scenario.
    // -------------------------------------------------------------------

    [Fact]
    public async Task HandleAsync_SingleMatch_SetsConfidentFieldsAndIncludesCandidatesArray()
    {
        var tool = ToolOver(RichFamilyGed, out _, _dir);
        var result = await tool.HandleAsync("Frederick Morrill", null, CancellationToken.None);

        var root = StructuredContent(result);
        Assert.Equal("single", root.GetProperty("matchType").GetString());
        Assert.Equal("@I1@", root.GetProperty("confidentMatchXref").GetString());
        Assert.True(root.GetProperty("confidentMatchScore").GetDouble() > 0);
        // A decisive single winner can still have weaker same-name
        // alternatives in the full recall set (Ansel Morrill scores above
        // the 70-point recall gate here on surname alone); totalMatches
        // reports that full set, not just the winner.
        int totalMatches = root.GetProperty("totalMatches").GetInt32();
        Assert.True(totalMatches >= 1);

        // The unified schema also carries a scored candidates array for a
        // single result, with the confident match first.
        var candidates = root.GetProperty("candidates").EnumerateArray().ToList();
        Assert.NotEmpty(candidates);
        Assert.Equal("@I1@", candidates[0].GetProperty("xref").GetString());
        Assert.True(candidates[0].GetProperty("matchScore").GetDouble() > 0);
        Assert.Equal(
            candidates.Count < totalMatches,
            root.GetProperty("truncated").GetBoolean());

        Assert.Empty(root.GetProperty("suggestions").EnumerateArray());
    }

    [Fact]
    public async Task HandleAsync_CandidateList_HasNullConfidentFieldsAndNullPerson_AndPerCandidateMatchScore()
    {
        var tool = ToolOver(TwoCandidatesGed, out _, _dir);
        var result = await tool.HandleAsync("Jane Doe", null, CancellationToken.None);

        var root = StructuredContent(result);
        Assert.Equal("candidates", root.GetProperty("matchType").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("confidentMatchXref").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("confidentMatchScore").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("person").ValueKind);
        Assert.Equal(2, root.GetProperty("totalMatches").GetInt32());

        foreach (var candidate in root.GetProperty("candidates").EnumerateArray())
            Assert.True(candidate.GetProperty("matchScore").GetDouble() > 0);
    }

    [Fact]
    public async Task HandleAsync_NoMatch_HasNullConfidentFieldsAndNullPerson_AndZeroTotalMatches()
    {
        var tool = ToolOver(SparsePersonGed, out _, _dir);
        var result = await tool.HandleAsync("Zzqxvw Bbdfghj", null, CancellationToken.None);

        var root = StructuredContent(result);
        Assert.Equal("none", root.GetProperty("matchType").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("confidentMatchXref").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("confidentMatchScore").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("person").ValueKind);
        Assert.Equal(0, root.GetProperty("totalMatches").GetInt32());
        Assert.False(root.GetProperty("truncated").GetBoolean());
        Assert.Empty(root.GetProperty("candidates").EnumerateArray());
    }

    [Fact]
    public async Task HandleAsync_NoMatch_SuggestionsCarryMatchScore()
    {
        var tool = ToolOver(SparsePersonGed, out _, _dir);
        var result = await tool.HandleAsync("Zzqxvw Nobody", null, CancellationToken.None);

        var suggestion = Assert.Single(StructuredContent(result).GetProperty("suggestions").EnumerateArray());
        double score = suggestion.GetProperty("matchScore").GetDouble();
        Assert.InRange(score, 55.0, 69.0);
    }

    // -------------------------------------------------------------------
    // maxResults
    // -------------------------------------------------------------------

    [Fact]
    public async Task HandleAsync_MaxResults_Integer_CapsCandidatesAndSetsTruncated()
    {
        var tool = ToolOver(TwoCandidatesGed, out _, _dir);
        var result = await tool.HandleAsync("Jane Doe", null, CancellationToken.None, Json("1"));

        var root = StructuredContent(result);
        // Still classified as candidates -- capping never fabricates a
        // confident single classification.
        Assert.Equal("candidates", root.GetProperty("matchType").GetString());
        Assert.Single(root.GetProperty("candidates").EnumerateArray());
        Assert.Equal(2, root.GetProperty("totalMatches").GetInt32());
        Assert.True(root.GetProperty("truncated").GetBoolean());
    }

    [Fact]
    public async Task HandleAsync_MaxResults_All_ReturnsWholeRecallSetUntruncated()
    {
        var tool = ToolOver(TwoCandidatesGed, out _, _dir);
        var result = await tool.HandleAsync("Jane Doe", null, CancellationToken.None, Json("\"all\""));

        var root = StructuredContent(result);
        Assert.Equal(2, root.GetProperty("candidates").EnumerateArray().Count());
        Assert.Equal(2, root.GetProperty("totalMatches").GetInt32());
        Assert.False(root.GetProperty("truncated").GetBoolean());
    }

    [Fact]
    public async Task HandleAsync_MaxResults_Omitted_DefaultsToEightLikeBefore()
    {
        var tool = ToolOver(TwoCandidatesGed, out _, _dir);
        var withDefault = await tool.HandleAsync("Jane Doe", null, CancellationToken.None);
        var withExplicitEight = await tool.HandleAsync("Jane Doe", null, CancellationToken.None, Json("8"));

        Assert.Equal(
            JsonSerializer.Serialize(StructuredContent(withDefault)),
            JsonSerializer.Serialize(StructuredContent(withExplicitEight)));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("\"bogus\"")]
    public async Task HandleAsync_MaxResults_Invalid_ReturnsIsErrorWithoutMutatingMatching(string invalidJson)
    {
        var tool = ToolOver(TwoCandidatesGed, out _, _dir);
        var result = await tool.HandleAsync("Jane Doe", null, CancellationToken.None, Json(invalidJson));

        Assert.True(result.IsError);
        Assert.Null(result.StructuredContent);
        Assert.Contains("maxResults", TextOf(result));
    }

    [Fact]
    public void ToolMetadata_InputSchema_DeclaresMaxResults()
    {
        var tool = ToolOver(SparsePersonGed, out _, _dir).ToMcpServerTool();
        var properties = tool.ProtocolTool.InputSchema.GetProperty("properties");
        Assert.True(properties.TryGetProperty("maxResults", out var maxResults));
        Assert.Equal(8, maxResults.GetProperty("default").GetInt32());
    }

    [Fact]
    public void OutputSchemaJson_EveryResultShape_ValidatesStructurally()
    {
        // A lightweight structural check in lieu of a full JSON Schema
        // validator dependency: every $ref in the document resolves to a
        // $defs entry that actually exists, and every $defs entry the oneOf
        // branches (transitively) reach is present.
        using var doc = JsonDocument.Parse(FindPersonTool.OutputSchemaJson);
        var defs = doc.RootElement.GetProperty("$defs");
        var defNames = defs.EnumerateObject().Select(p => p.Name).ToHashSet();

        void CheckRefs(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                if (element.TryGetProperty("$ref", out var refProp))
                {
                    string ptr = refProp.GetString()!;
                    Assert.StartsWith("#/$defs/", ptr);
                    Assert.Contains(ptr["#/$defs/".Length..], defNames);
                }
                foreach (var prop in element.EnumerateObject())
                    CheckRefs(prop.Value);
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in element.EnumerateArray())
                    CheckRefs(item);
            }
        }

        CheckRefs(doc.RootElement);
    }
}
