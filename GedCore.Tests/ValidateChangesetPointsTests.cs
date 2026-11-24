using System.Text;
using System.Text.Json;
using GedCore.Ged70;
using GedCore.Policy;
using GedFire.Mcp;
using ModelContextProtocol.Protocol;

namespace GedCore.Tests;

/// <summary>validate_changeset and apply_changeset with a server policy, and validate_changeset's points preview.</summary>
public class ValidateChangesetPointsTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("gedfire-points-tests-").FullName;
    readonly TempDocumentSessions _sessions = new();

    public void Dispose()
    {
        _sessions.Dispose();
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    string WriteGed()
    {
        string path = Path.Combine(_dir, "test.ged");
        var document = Ged70Parser.Parse(string.Join("\r\n",
        [
            "0 HEAD", "1 GEDC", "2 VERS 7.0",
            "0 @I00001@ INDI", "1 NAME Allen /Test/", "1 SEX M",
            "0 @S00001@ SOUR", "1 TITL Existing source",
            "0 TRLR",
        ]) + "\r\n");
        using var stream = File.Create(path);
        Ged70Formatter.Write(document, stream);
        return path;
    }

    string WriteChangeset(string json)
    {
        string path = Path.Combine(_dir, "changeset.json");
        File.WriteAllText(path, json);
        return path;
    }

    static ChangesetPolicy Policy(string json) => ChangesetPolicy.Load(new MemoryStream(Encoding.UTF8.GetBytes(json)));

    static JsonElement Cards(string json) => JsonDocument.Parse(json).RootElement.Clone();

    static JsonElement Structured(CallToolResult result) => result.StructuredContent!.Value;

    const string DeathChangeset = """
        { "items": [ { "item": 1, "ops": [
          { "op": "createOrUpdateVital", "record": "@I00001@", "fact": "DEAT",
            "date": "1 JAN 1900", "place": "Salem",
            "citations": [{ "source": "@S00001@", "page": "p. 1", "quay": 1 }] } ] } ] }
        """;

    // ---- points preview ----

    [Fact]
    public async Task WithoutCards_TheResultHasNoPointsPreview()
    {
        var tool = new ValidateChangesetTool(WriteGed(), new ToolGate());

        var result = await tool.HandleAsync(WriteChangeset(DeathChangeset), "all", CancellationToken.None);

        Assert.False(Structured(result).TryGetProperty("pointsPreview", out _));
    }

    [Fact]
    public async Task ACardWhoseFactTheChangesetWrites_IsClosed()
    {
        var tool = new ValidateChangesetTool(WriteGed(), new ToolGate());
        var cards = Cards("""[ { "xref": "@I00001@", "class": "enrich-person", "gap": "enrich-death", "points": 7 } ]""");

        var result = await tool.HandleAsync(WriteChangeset(DeathChangeset), "all", cards, CancellationToken.None);

        var preview = Structured(result).GetProperty("pointsPreview");
        Assert.Equal(7, preview.GetProperty("total").GetInt32());
        var closed = Assert.Single(preview.GetProperty("closed").EnumerateArray());
        Assert.Equal("@I00001@|enrich-person|enrich-death", closed.GetProperty("cardKey").GetString());
        Assert.Equal("DEAT @I00001@ (first date and place and citation)", closed.GetProperty("wrote").GetString());
    }

    [Fact]
    public async Task ABiographyCard_IsClosedByABiographyNoteThatAddsACitation()
    {
        var tool = new ValidateChangesetTool(WriteGed(), new ToolGate());
        var cards = Cards("""[ { "xref": "@I00001@", "class": "Biography", "points": 8 } ]""");
        string changeset = WriteChangeset("""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateNote", "record": "@I00001@", "purpose": "biography",
                "text": "Allen kept a dry-goods store in Salem from 1890.",
                "citations": [{ "source": "@S00001@", "page": "Business directory, 1890", "quay": 2 }] } ] } ] }
            """);

        var result = await tool.HandleAsync(changeset, "all", cards, CancellationToken.None);

        var closed = Assert.Single(Structured(result).GetProperty("pointsPreview").GetProperty("closed").EnumerateArray());
        Assert.Equal(8, closed.GetProperty("points").GetInt32());
        Assert.Equal("biography note on @I00001@ citing @S00001@ \"Business directory, 1890\"", closed.GetProperty("wrote").GetString());
    }

    [Fact]
    public async Task AWomansBiographyCard_PaysLikeAMans_ThoughTheDealNeverOffersOne()
    {
        var tool = new ValidateChangesetTool(WriteGed("""
            0 @I1@ INDI
            1 NAME Nellie /Test/
            1 SEX F
            """), new ToolGate());
        var cards = Cards("""[ { "xref": "@I1@", "class": "Biography", "points": 3 } ]""");
        string changeset = WriteChangeset("""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateVital", "record": "@I1@", "fact": "OCCU", "value": "Schoolmistress",
                "citations": [{ "source": "@S1@", "page": "p. 4", "quay": 2 }] } ] } ] }
            """);

        var result = await tool.HandleAsync(changeset, "all", cards, CancellationToken.None);

        var closed = Assert.Single(Structured(result).GetProperty("pointsPreview").GetProperty("closed").EnumerateArray());
        Assert.Equal(3, closed.GetProperty("points").GetInt32());
        Assert.Equal("cited OCCU @I1@", closed.GetProperty("wrote").GetString());
    }

    [Fact]
    public async Task ACardWhoseFactIsOnlyCited_IsNotClosed()
    {
        string path = WriteGed();
        var tool = new ValidateChangesetTool(path, new ToolGate());
        var cards = Cards("""[ { "xref": "@I00001@", "class": "enrich-person", "gap": "enrich-birth", "points": 7 } ]""");

        var result = await tool.HandleAsync(WriteChangeset(DeathChangeset), "all", cards, CancellationToken.None);

        var preview = Structured(result).GetProperty("pointsPreview");
        Assert.Equal(0, preview.GetProperty("total").GetInt32());
        Assert.Equal("no BIRT written on the subject (wrote DEAT)",
            Assert.Single(preview.GetProperty("notClosed").EnumerateArray()).GetProperty("reason").GetString());
    }

    [Fact]
    public async Task ARenamedSubject_IsReportedAsASubjectMismatch()
    {
        var tool = new ValidateChangesetTool(WriteGed(), new ToolGate());
        var cards = Cards("""[ { "xref": "@I00001@", "class": "enrich-person", "gap": "enrich-death", "name": "Allen Test", "points": 7 } ]""");
        string changeset = """
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateVital", "record": "@I00001@", "fact": "NAME", "value": "Alan /Test/" },
              { "op": "createOrUpdateVital", "record": "@I00001@", "fact": "DEAT", "date": "1 JAN 1900" } ] } ] }
            """;

        var result = await tool.HandleAsync(WriteChangeset(changeset), "all", cards, CancellationToken.None);

        var mismatch = Assert.Single(Structured(result).GetProperty("pointsPreview").GetProperty("subjectMismatch").EnumerateArray());
        Assert.Equal("Allen Test", mismatch.GetProperty("cardSubject").GetString());
        Assert.Equal("Alan Test", mismatch.GetProperty("appliedName").GetString());
    }

    [Fact]
    public async Task CardsPath_ReadsTheCardsFromAFile()
    {
        var tool = new ValidateChangesetTool(WriteGed(), new ToolGate());
        string cardsFile = Path.Combine(_dir, "cards.json");
        File.WriteAllText(cardsFile, """{ "cards": [ { "xref": "@I00001@", "class": "enrich-person", "gap": "enrich-death", "points": 7 } ] }""");

        var result = await tool.HandleAsync(WriteChangeset(DeathChangeset), "all", default, Cards(JsonSerializer.Serialize(cardsFile)), CancellationToken.None);

        Assert.Equal(7, Structured(result).GetProperty("pointsPreview").GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task CardsAndCardsPathTogether_AreRefused()
    {
        var tool = new ValidateChangesetTool(WriteGed(), new ToolGate());

        var result = await tool.HandleAsync(WriteChangeset(DeathChangeset), "all", Cards("[]"), Cards("\"cards.json\""), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("give cards or cardsPath, not both.", Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text);
    }

    [Fact]
    public async Task AMalformedCard_IsRefusedNamingTheField()
    {
        var tool = new ValidateChangesetTool(WriteGed(), new ToolGate());

        var result = await tool.HandleAsync(WriteChangeset(DeathChangeset), "all",
            Cards("""[ { "xref": "@I00001@", "class": "enrich-person" } ]"""), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("cards[0].points is required", Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text);
    }

    [Fact]
    public void TheAdvertisedSchemas_DescribeCardsAndThePreview()
    {
        Assert.Contains("\"cards\"", ValidateChangesetTool.InputSchemaJson);
        Assert.Contains("\"pointsPreview\"", ValidateChangesetTool.OutputSchemaJson);
        Assert.DoesNotContain("\"cards\"", ApplyChangesetTool.InputSchemaJson);
        Assert.DoesNotContain("\"pointsPreview\"", ApplyChangesetTool.OutputSchemaJson);
    }

    // ---- the preview and the deal agree ----

    string WriteGed(string records)
    {
        string path = Path.Combine(_dir, Guid.NewGuid() + ".ged");
        string text = "0 HEAD\n1 GEDC\n2 VERS 7.0\n" + records.ReplaceLineEndings("\n").Trim() + "\n0 @S1@ SOUR\n1 TITL Town register\n0 TRLR\n";
        using var stream = File.Create(path);
        Ged70Formatter.Write(Ged70Parser.Parse(text), stream);
        return path;
    }

    // select_targets for one person of the file as it now stands.
    async Task<JsonElement> Deal(string path, string xref, string? studySurnames = null)
    {
        var tool = new SelectTargetsTool(_sessions.Open(File.ReadAllText(path)), new ToolGate());
        var arguments = new SelectTargetsArguments
        {
            Xrefs = Cards($"[\"{xref}\"]"),
            StudySurnames = studySurnames is null ? default : Cards(studySurnames),
        };
        var result = await tool.HandleAsync(arguments, CancellationToken.None);
        Assert.NotEqual(true, result.IsError);
        return Structured(result);
    }

    static JsonElement? Card(JsonElement deal, string cardType) =>
        deal.GetProperty("targets").EnumerateArray().Cast<JsonElement?>()
            .FirstOrDefault(t => t!.Value.GetProperty("cardType").GetString() == cardType);

    async Task<JsonElement> Preview(string path, string changeset, string cards, string? studySurnames = null)
    {
        var arguments = new ValidateChangesetArguments(WriteChangeset(changeset), "all", Cards(cards))
        {
            StudySurnames = studySurnames is null ? default : Cards(studySurnames),
        };
        var result = await new ValidateChangesetTool(path, new ToolGate()).HandleAsync(arguments, CancellationToken.None);
        Assert.NotEqual(true, result.IsError);
        var structured = Structured(result);
        Assert.True(structured.GetProperty("success").GetBoolean(), structured.GetProperty("errors").ToString());
        return structured.GetProperty("pointsPreview");
    }

    async Task Apply(string path, string changeset)
    {
        var result = await new ApplyChangesetTool(path, new ToolGate(), readOnly: false).HandleAsync(WriteChangeset(changeset), "all", CancellationToken.None);
        Assert.True(Structured(result).GetProperty("success").GetBoolean(), Structured(result).GetProperty("errors").ToString());
    }

    static List<string> Keys(JsonElement preview, string list) =>
        [.. preview.GetProperty(list).EnumerateArray().Select(c => c.GetProperty("cardKey").GetString()!)];

    const string ChildlessCouple = """
        0 @I1@ INDI
        1 NAME Amos /Ashworth/
        1 SEX M
        1 BIRT
        2 PLAC Salem, Oregon
        1 FAMS @F1@
        0 @I2@ INDI
        1 NAME Ruth /Holt/
        1 SEX F
        1 BIRT
        2 PLAC Salem, Oregon
        1 FAMS @F1@
        0 @F1@ FAM
        1 HUSB @I1@
        1 WIFE @I2@
        1 MARR
        2 DATE 4 MAY 1874
        """;

    const string Cited = """[{ "source": "@S1@", "page": "p. 3", "quay": 2 }]""";

    [Fact]
    public async Task AChildOfANewFamily_DoesNotCloseTheNewChildCardOfTheFamilyTheDealNamed()
    {
        string path = WriteGed(ChildlessCouple);
        string changeset = $$"""
            { "items": [ { "item": 1,
              "people": [ { "xref": "@NewI1@", "name": "Eli /Pike/", "sex": "M" }, { "xref": "@NewI2@", "name": "Ann /Pike/", "sex": "F" } ],
              "ops": [
                { "op": "createOrUpdateSpouse", "person": "@I2@", "spouse": "@NewI1@", "family": "@NewF1@", "familyCitations": {{Cited}} },
                { "op": "createOrUpdateChild", "family": "@NewF1@", "child": "@NewI2@", "familyCitations": {{Cited}} } ] } ] }
            """;
        Assert.Equal("@F1@", Card(await Deal(path, "@I2@"), "New child")!.Value.GetProperty("spouseFamily").GetProperty("familyXref").GetString());

        var preview = await Preview(path, changeset, """
            [ { "xref": "@I2@", "class": "new-child", "gap": "children", "points": 10 },
              { "xref": "@I2@", "class": "new-child", "gap": "children", "points": 10, "family": "@F1@" } ]
            """);

        Assert.Empty(preview.GetProperty("closed").EnumerateArray());
        var open = preview.GetProperty("notClosed").EnumerateArray().ToList();
        Assert.Equal(["@I2@|new-child|children", "@I2@|new-child|children"], open.Select(c => c.GetProperty("cardKey").GetString()));
        Assert.Matches(@"^@I\d+@ joined @F0+2@, a family this changeset creates", open[0].GetProperty("reason").GetString());
        Assert.False(open[0].TryGetProperty("family", out _));
        Assert.Matches(@"^@I\d+@ joined @F0+2@, not the card's family @F1@$", open[1].GetProperty("reason").GetString());
        Assert.Equal("@F1@", open[1].GetProperty("family").GetString());

        await Apply(path, changeset);
        Assert.Equal("@F1@", Card(await Deal(path, "@I2@"), "New child")!.Value.GetProperty("spouseFamily").GetProperty("familyXref").GetString());
    }

    [Fact]
    public async Task TwoChildrenOfOneFamily_CloseExactlyOneNewChildCard_AndTheDealStopsDealingIt()
    {
        string path = WriteGed(ChildlessCouple);
        string changeset = $$"""
            { "items": [ { "item": 1,
              "people": [ { "xref": "@NewI1@", "name": "Ann /Ashworth/", "sex": "F" }, { "xref": "@NewI2@", "name": "Eli /Ashworth/", "sex": "M" } ],
              "ops": [
                { "op": "createOrUpdateChild", "family": "@F1@", "child": "@NewI1@", "familyCitations": {{Cited}} },
                { "op": "createOrUpdateChild", "family": "@F1@", "child": "@NewI2@" } ] } ] }
            """;

        var preview = await Preview(path, changeset, """
            [ { "xref": "@I2@", "class": "new-child", "gap": "children", "points": 10 },
              { "xref": "@I1@", "class": "new-child", "gap": "children", "points": 5 } ]
            """);

        var closed = Assert.Single(preview.GetProperty("closed").EnumerateArray());
        Assert.Matches("^CHIL @I\\d+@ in @F1@$", closed.GetProperty("wrote").GetString());
        Assert.Equal(closed.GetProperty("points").GetInt32(), preview.GetProperty("total").GetInt32());
        Assert.Contains("a family's New child card is paid once", Assert.Single(preview.GetProperty("notClosed").EnumerateArray()).GetProperty("reason").GetString());

        await Apply(path, changeset);
        Assert.Null(Card(await Deal(path, "@I1@"), "New child"));
        Assert.Null(Card(await Deal(path, "@I2@"), "New child"));
    }

    const string Sparse = """
        0 @I1@ INDI
        1 NAME Amos /Ashworth/
        1 SEX M
        """;

    static string EnrichCard(JsonElement dealt) =>
        $$"""[ { "xref": "@I1@", "class": "Enrich person", "gap": "enrich-fact", "points": 1, "bonus": {{dealt.GetProperty("score").GetInt32() - dealt.GetProperty("nominalPoints").GetInt32()}} } ]""";

    [Fact]
    public async Task FillingExactlyTheGapsTheDealListed_PaysTheDealtScore()
    {
        string path = WriteGed(Sparse);
        var dealt = Card(await Deal(path, "@I1@"), "Enrich person")!.Value;
        Assert.Equal(6, dealt.GetProperty("nominalPoints").GetInt32());

        var preview = await Preview(path, $$"""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateVital", "record": "@I1@", "fact": "BIRT", "date": "3 JUN 1850", "place": "Salem, Oregon", "citations": {{Cited}} },
              { "op": "createOrUpdateVital", "record": "@I1@", "fact": "DEAT", "date": "9 OCT 1911", "place": "Salem, Oregon", "citations": {{Cited}} } ] } ] }
            """, EnrichCard(dealt));

        Assert.Equal(dealt.GetProperty("score").GetInt32(), preview.GetProperty("total").GetInt32());
        Assert.Equal(
            "BIRT @I1@ (date and place and citation); DEAT @I1@ (date and place and citation)",
            Assert.Single(preview.GetProperty("closed").EnumerateArray()).GetProperty("wrote").GetString());
    }

    [Fact]
    public async Task AWriteTheDealDidNotList_PaysNothing_AndTheCardIsDealtAgainAtWhatIsLeft()
    {
        string path = WriteGed(Sparse);
        var dealt = Card(await Deal(path, "@I1@"), "Enrich person")!.Value;
        string changeset = $$"""
            { "items": [ { "item": 1, "people": [ { "xref": "@NewI1@", "name": "Ruth /Holt/", "sex": "F" } ], "ops": [
              { "op": "createOrUpdateSpouse", "person": "@I1@", "spouse": "@NewI1@", "family": "@NewF1@",
                "marriage": { "date": "4 MAY 1874", "place": "Salem, Oregon", "citations": {{Cited}} } },
              { "op": "createOrUpdateVital", "record": "@I1@", "fact": "DEAT", "date": "9 OCT 1911", "place": "Salem, Oregon", "citations": {{Cited}} },
              { "op": "createOrUpdateVital", "record": "@I1@", "fact": "BIRT", "date": "3 JUN 1850", "citations": {{Cited}} },
              { "op": "createOrUpdateVital", "record": "@I1@", "fact": "BURI", "place": "Salem Cemetery, Salem, Oregon", "citations": {{Cited}} },
              { "op": "createOrUpdateVital", "record": "@I1@", "fact": "RESI", "date": "1 JUN 1880", "place": "Salem, Oregon", "citations": {{Cited}} } ] } ] }
            """;

        var preview = await Preview(path, changeset, EnrichCard(dealt));

        var closed = Assert.Single(preview.GetProperty("closed").EnumerateArray());
        Assert.Equal("BIRT @I1@ (date and citation); DEAT @I1@ (date and place and citation)", closed.GetProperty("wrote").GetString());
        int paid = preview.GetProperty("total").GetInt32();
        Assert.Equal(5, paid);

        await Apply(path, changeset);
        var left = Card(await Deal(path, "@I1@"), "Enrich person")!.Value;
        Assert.Equal(dealt.GetProperty("nominalPoints").GetInt32() - paid, left.GetProperty("nominalPoints").GetInt32());
        var birth = Assert.Single(left.GetProperty("enrichments").EnumerateArray());
        Assert.Equal(("Birth", true), (birth.GetProperty("fact").GetString(), birth.GetProperty("missingPlace").GetBoolean()));
    }

    const string Bachelor = """
        0 @I1@ INDI
        1 NAME Amos /Ashworth/
        1 SEX M
        1 BIRT
        2 PLAC Salem, Oregon
        """;

    [Fact]
    public async Task ACitedNeverMarriedAnswer_ClosesTheNewSpouseCard_AndTheDealWithholdsItAfterTheApply()
    {
        string path = WriteGed(Bachelor);
        Assert.NotNull(Card(await Deal(path, "@I1@"), "New spouse"));
        string changeset = $$"""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateVital", "record": "@I1@", "fact": "NMR", "value": "0", "citations": {{Cited}} } ] } ] }
            """;
        const string cards = """[ { "xref": "@I1@", "class": "new-spouse", "gap": "spouse", "points": 10 } ]""";

        var preview = await Preview(path, changeset, cards);

        var closed = Assert.Single(preview.GetProperty("closed").EnumerateArray());
        Assert.Equal(("@I1@|new-spouse|spouse", 10, "NMR 0 on @I1@ (never married, cited)"),
            (closed.GetProperty("cardKey").GetString(), closed.GetProperty("points").GetInt32(), closed.GetProperty("wrote").GetString()));

        await Apply(path, changeset);
        Assert.Contains("1 NMR 0\r\n2 SOUR @S1@", File.ReadAllText(path));
        var deal = await Deal(path, "@I1@");
        Assert.Null(Card(deal, "New spouse"));
        var withheld = Assert.Single(deal.GetProperty("suppressedCards").EnumerateArray());
        Assert.Equal(("New spouse", "never married: a cited NMR 0 records the answer"),
            (withheld.GetProperty("cardType").GetString(), withheld.GetProperty("reason").GetString()));
        Assert.Equal(["@I1@|new-spouse|spouse"], Keys(await Preview(path, changeset, cards), "notClosed"));
    }

    [Fact]
    public async Task ANoteSayingSomeoneNeverMarried_DoesNotCloseTheNewSpouseCard()
    {
        var preview = await Preview(WriteGed(Bachelor), $$"""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateNote", "record": "@I1@", "text": "He never married.", "citations": {{Cited}} } ] } ] }
            """, """[ { "xref": "@I1@", "class": "new-spouse", "points": 10 } ]""");

        Assert.Equal(
            "no spouse added to a family the subject is a partner in, and no cited never-married answer (NMR 0) recorded",
            Assert.Single(preview.GetProperty("notClosed").EnumerateArray()).GetProperty("reason").GetString());
    }

    // A bearer husband and his married-in wife (@F1@), and a bearer daughter with a husband of another surname (@F2@).
    const string Study = ChildlessCouple + """

        0 @I3@ INDI
        1 NAME Ada /Ashworth/
        1 SEX F
        1 BIRT
        2 PLAC Salem, Oregon
        1 FAMS @F2@
        0 @I4@ INDI
        1 NAME Eli /Pike/
        1 SEX M
        1 BIRT
        2 PLAC Salem, Oregon
        1 FAMS @F2@
        0 @F2@ FAM
        1 HUSB @I4@
        1 WIFE @I3@
        """;

    [Fact]
    public async Task WithStudySurnames_ThePreviewClosesNoCardTheDealWithholds()
    {
        string path = WriteGed(Study);
        string changeset = $$"""
            { "items": [ { "item": 1,
              "people": [ { "xref": "@NewI1@", "name": "Ann /Pike/", "sex": "F" }, { "xref": "@NewI2@", "name": "Saul /Holt/", "sex": "M" } ],
              "ops": [
                { "op": "createOrUpdateChild", "family": "@F2@", "child": "@NewI1@", "familyCitations": {{Cited}} },
                { "op": "createOrUpdateParent", "person": "@I2@", "role": "father", "parent": "@NewI2@", "family": "@NewF1@", "familyCitations": {{Cited}} },
                { "op": "createOrUpdateNote", "record": "@I4@", "purpose": "biography", "text": "Eli kept a ferry at Salem.", "citations": {{Cited}} } ] } ] }
            """;
        const string cards = """
            [ { "xref": "@I3@", "class": "new-child", "gap": "children", "points": 10, "family": "@F2@" },
              { "xref": "@I2@", "class": "new-parent", "gap": "parents", "points": 10 },
              { "xref": "@I4@", "class": "biography", "points": 3 } ]
            """;
        const string study = """["Ashworth"]""";
        foreach (var (xref, cardType) in new[] { ("@I3@", "New child"), ("@I2@", "New parent"), ("@I4@", "Biography") })
        {
            var deal = await Deal(path, xref, study);
            Assert.Null(Card(deal, cardType));
            Assert.Contains(deal.GetProperty("suppressedCards").EnumerateArray(), c =>
                c.GetProperty("cardType").GetString() == cardType && c.GetProperty("reason").GetString()!.StartsWith("out of study scope: "));
        }

        var unscoped = await Preview(path, changeset, cards);
        var scoped = await Preview(path, changeset, cards, study);

        Assert.Equal(3, unscoped.GetProperty("closed").GetArrayLength());
        Assert.Empty(scoped.GetProperty("closed").EnumerateArray());
        Assert.Equal(0, scoped.GetProperty("total").GetInt32());
        Assert.Equal(["@I3@|new-child|children", "@I2@|new-parent|parents", "@I4@|biography"], Keys(scoped, "notClosed"));
        Assert.All(scoped.GetProperty("notClosed").EnumerateArray(), c => Assert.StartsWith("out of study scope: ", c.GetProperty("reason").GetString()));
    }

    [Fact]
    public async Task StudySurnamesWithoutCards_IsRefused()
    {
        var arguments = new ValidateChangesetArguments(WriteChangeset(DeathChangeset), "all") { StudySurnames = Cards("""["Ashworth"]""") };

        var result = await new ValidateChangesetTool(WriteGed(), new ToolGate()).HandleAsync(arguments, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Contains("studySurnames needs cards or cardsPath", Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text);
    }

    // ---- policy ----

    [Fact]
    public async Task DryRun_ReportsEveryPolicyViolationAsAnError()
    {
        var policy = Policy("""{ "deathPlaceEvidence": "\\b(died|death)\\b" }""");
        var tool = new ValidateChangesetTool(WriteGed(), new ToolGate(), policy);

        var result = await tool.HandleAsync(WriteChangeset(DeathChangeset), "all", CancellationToken.None);

        var structured = Structured(result);
        Assert.False(structured.GetProperty("success").GetBoolean());
        Assert.Contains("policy: item 1", Assert.Single(structured.GetProperty("errors").EnumerateArray()).GetString());
    }

    [Fact]
    public async Task DryRun_ShowsPolicyViolationsBesideTheDocumentsOwnErrors()
    {
        var policy = Policy("""{ "bannedPhrases": ["not added"] }""");
        var tool = new ValidateChangesetTool(WriteGed(), new ToolGate(), policy);
        string changeset = """
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateNote", "record": "@I09999@", "text": "not added" } ] } ] }
            """;

        var result = await tool.HandleAsync(WriteChangeset(changeset), "all", CancellationToken.None);

        var errors = Structured(result).GetProperty("errors").EnumerateArray().Select(e => e.GetString()!).ToList();
        Assert.Equal(2, errors.Count);
        Assert.StartsWith("policy: ", errors[0]);
    }

    [Fact]
    public async Task ACompliantChangeset_PassesThePolicy()
    {
        var policy = Policy("""{ "bannedPhrases": ["not added"] }""");
        var tool = new ValidateChangesetTool(WriteGed(), new ToolGate(), policy);

        var result = await tool.HandleAsync(WriteChangeset(DeathChangeset), "all", CancellationToken.None);

        Assert.True(Structured(result).GetProperty("success").GetBoolean());
    }

    [Fact]
    public async Task Apply_RefusesAPolicyViolationAndLeavesTheFileUntouched()
    {
        string path = WriteGed();
        byte[] before = File.ReadAllBytes(path);
        var policy = Policy("""{ "deathPlaceEvidence": "\\b(died|death)\\b" }""");
        var tool = new ApplyChangesetTool(path, new ToolGate(), readOnly: false, policy);

        var result = await tool.HandleAsync(WriteChangeset(DeathChangeset), "all", CancellationToken.None);

        Assert.False(Structured(result).GetProperty("success").GetBoolean());
        Assert.Contains("policy:", Structured(result).GetProperty("errors")[0].GetString());
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    [Fact]
    public async Task Apply_WithoutAPolicy_StillWrites()
    {
        string path = WriteGed();
        var tool = new ApplyChangesetTool(path, new ToolGate(), readOnly: false);

        var result = await tool.HandleAsync(WriteChangeset(DeathChangeset), "all", CancellationToken.None);

        Assert.True(Structured(result).GetProperty("success").GetBoolean());
        Assert.Contains("DEAT", File.ReadAllText(path));
    }
}
