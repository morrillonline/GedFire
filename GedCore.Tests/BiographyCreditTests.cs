using GedCore.Apply;

namespace GedCore.Tests;

/// <summary>
/// Biography credit through createOrUpdateNote with purpose "biography": which citations a run adds to a
/// person's cited notes that none of them carried before, as reported on <see cref="ApplyResult.Changes"/>.
/// </summary>
public class BiographyCreditTests : ApplyTestBase
{
    const string Service = "Allen served aboard the destroyer Kidd from 1944 to 1946.";
    static readonly DateTime Clock = new(2026, 11, 12, 12, 0, 0, DateTimeKind.Utc);

    void WriteFileWithNotesOnAllen(params string[] noteLines) =>
        WriteFile([.. BaseLines.SelectMany(l => l switch
        {
            "1 FAMC @F00001@" => new[] { l }.Concat(noteLines),
            "0 TRLR" => ["0 @S00002@ SOUR", "1 TITL Navy pension files", l],
            _ => new[] { l },
        })]);

    static string NoteOp(string text, string citations, string? purpose = "biography", string record = "@I00001@") => $$"""
        { "items": [ { "item": 1, "ops": [
          { "op": "createOrUpdateNote", "record": "{{record}}", "text": "{{text}}"{{(purpose is null ? "" : $", \"purpose\": \"{purpose}\"")}},
            "citations": [ {{citations}} ] } ] } ] }
        """;

    const string MusterRoll = """{ "source": "@S00001@", "page": "Muster roll, p. 12", "dataText": "Allen Test, seaman", "quay": 2 }""";
    const string PensionFile = """{ "source": "@S00002@", "page": "File 4471", "quay": 2 }""";

    [Fact]
    public void ANewBiographyNote_EarnsItsCitation()
    {
        WriteFileWithNotesOnAllen();

        var result = RunExpectSuccess(NoteOp(Service, MusterRoll));

        var write = Assert.Single(result.Changes.Biographies);
        Assert.Equal("@I00001@", write.Record);
        Assert.Equal([new BiographyCitation("@S00001@", "Muster roll, p. 12")], write.NewCitations);
    }

    [Fact]
    public void TheBiographyPurpose_WritesExactlyThePlainCitedNote()
    {
        WriteFileWithNotesOnAllen();
        RunExpectSuccess(NoteOp(Service, MusterRoll, purpose: null), utcNow: Clock);
        byte[] plain = ReadBytes();

        WriteFileWithNotesOnAllen();
        RunExpectSuccess(NoteOp(Service, MusterRoll), utcNow: Clock);

        Assert.Equal(plain, ReadBytes());
    }

    [Fact]
    public void ACitedNoteWithoutPurpose_EarnsNothing()
    {
        WriteFileWithNotesOnAllen();

        var result = RunExpectSuccess(NoteOp(Service, MusterRoll, purpose: null));

        Assert.Empty(result.Changes.Biographies);
    }

    [Fact]
    public void AnIdenticalReplay_ChangesNothingAndEarnsNothing()
    {
        WriteFileWithNotesOnAllen();
        RunExpectSuccess(NoteOp(Service, MusterRoll));
        byte[] afterFirst = ReadBytes();

        var replay = RunExpectSuccess(NoteOp(Service, MusterRoll));

        Assert.Empty(replay.Changes.Biographies);
        Assert.Equal(afterFirst, ReadBytes());
    }

    [Fact]
    public void ANewCitationStrengtheningExistingProse_EarnsOnlyThatCitation()
    {
        WriteFileWithNotesOnAllen($"1 NOTE {Service}", "2 SOUR @S00001@", "3 PAGE Muster roll, p. 12");

        var result = RunExpectSuccess(NoteOp(Service, $"{MusterRoll}, {PensionFile}"));

        Assert.Equal([new BiographyCitation("@S00002@", "File 4471")], Assert.Single(result.Changes.Biographies).NewCitations);
    }

    [Fact]
    public void ANewNoteBesideExistingProse_EarnsItsNewCitation()
    {
        WriteFileWithNotesOnAllen($"1 NOTE {Service}", "2 SOUR @S00001@", "3 PAGE Muster roll, p. 12", "1 NOTE Allen grew up on a farm.");

        var result = RunExpectSuccess(NoteOp("Allen drew a disability pension from 1947.", PensionFile));

        Assert.Equal([new BiographyCitation("@S00002@", "File 4471")], Assert.Single(result.Changes.Biographies).NewCitations);
    }

    [Fact]
    public void NewTextBackedOnlyByACitationAnotherCitedNoteCarries_EarnsCreditForTheText()
    {
        WriteFileWithNotesOnAllen($"1 NOTE {Service}", "2 SOUR @S00001@", "3 PAGE muster  roll, P. 12", "3 QUAY 1");

        var result = RunExpectSuccess(NoteOp("Allen was rated seaman first class in 1945.", MusterRoll));

        Assert.Contains(result.Log, l => l.StartsWith("createOrUpdateNote on @I00001@: created"));
        var write = Assert.Single(result.Changes.Biographies);
        Assert.Empty(write.NewCitations);
        Assert.True(write.NewText);
    }

    [Theory]
    [InlineData(Service)]
    [InlineData("  ALLEN served aboard the destroyer, Kidd, from 1944 to 1946  ")]
    public void ReplayingTheSameTextFromTheSameRecord_EarnsNothing(string text)
    {
        WriteFileWithNotesOnAllen($"1 NOTE {Service}", "2 SOUR @S00001@", "3 PAGE Muster roll, p. 12");

        var result = RunExpectSuccess(NoteOp(text, MusterRoll));

        Assert.Empty(result.Changes.Biographies);
    }

    [Fact]
    public void TextAlreadyInAnUncitedNote_IsNotNewInformation()
    {
        WriteFileWithNotesOnAllen("1 NOTE Allen was rated seaman first class in 1945.");

        var result = RunExpectSuccess(NoteOp("Allen was rated seaman first class in 1945.", MusterRoll));

        Assert.False(Assert.Single(result.Changes.Biographies).NewText);
    }

    [Fact]
    public void NewTextFromAnAlreadyCitedRecord_ClosesTheBiographyCard()
    {
        WriteFileWithNotesOnAllen($"1 NOTE {Service}", "2 SOUR @S00001@", "3 PAGE Muster roll, p. 12");

        var result = RunExpectSuccess(NoteOp("Allen was rated seaman first class in 1945.", MusterRoll));

        var preview = GedCore.Points.CardScorer.Preview([new GedCore.Points.PointsCard("@I00001@", "biography", null, null, 3)], result.Changes);
        Assert.Contains("with text none of its notes carried", Assert.Single(preview.Closed).Wrote);
    }

    [Fact]
    public void ABiographyOpOnAnAlreadyCitedNote_EarnsNothingForTheCitationsItHad()
    {
        WriteFileWithNotesOnAllen($"1 NOTE {Service}", "2 SOUR @S00001@", "3 PAGE Muster roll, p. 12");

        var result = RunExpectSuccess(NoteOp(Service, MusterRoll));

        Assert.Empty(result.Changes.Biographies);
    }

    [Theory]
    [InlineData("@F00002@", "biography", "a biography note belongs on a person")]
    [InlineData("@I00001@", "obituary", "purpose must be \"biography\"")]
    public void APurposeThatCannotApply_IsRefused(string record, string purpose, string error)
    {
        WriteFileWithNotesOnAllen();

        var result = Run(NoteOp(Service, MusterRoll, purpose, record));

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains(error));
    }

    // -------------------------------------------------------------------
    // Facts beyond birth, death and marriage
    // -------------------------------------------------------------------

    static string Ops(params string[] ops) => $$"""{ "items": [ { "item": 1, "ops": [ {{string.Join(", ", ops)}} ] } ] }""";

    static string VitalOp(string record, string fact, string fields, string? citations = MusterRoll) =>
        $$"""{ "op": "createOrUpdateVital", "record": "{{record}}", "fact": "{{fact}}", {{fields}}, "citations": [ {{citations}} ] }""";

    static IReadOnlyList<string> CreditedFacts(ApplyResult result, string person) =>
        [.. result.Changes.Biographies.Where(b => b.Record == person).SelectMany(b => b.Facts).Select(f => f.ToString())];

    [Fact]
    public void ACitedAttribute_EarnsBiographyCredit()
    {
        WriteFileWithNotesOnAllen();

        var result = RunExpectSuccess(Ops(VitalOp("@I00001@", "OCCU", "\"value\": \"Seaman\", \"date\": \"1945\"")));

        Assert.Equal(["OCCU @I00001@"], CreditedFacts(result, "@I00001@"));
    }

    [Fact]
    public void AnUncitedFact_EarnsNothing()
    {
        WriteFileWithNotesOnAllen();

        var result = RunExpectSuccess(Ops(VitalOp("@I00001@", "OCCU", "\"value\": \"Seaman\"", citations: "")));

        Assert.Empty(result.Changes.Biographies);
    }

    [Theory]
    [InlineData("BIRT", "\"date\": \"4 MAR 1928\"")]
    [InlineData("DEAT", "\"date\": \"1990\"")]
    public void ABirthOrDeath_IsTheEnrichCardsAndEarnsNoBiographyCredit(string fact, string fields)
    {
        WriteFileWithNotesOnAllen();

        var result = RunExpectSuccess(Ops(VitalOp("@I00001@", fact, fields)));

        Assert.Empty(result.Changes.Biographies);
    }

    [Fact]
    public void CorrectingACitedFactsValue_EarnsCredit_AndAnIdenticalReplayEarnsNothing()
    {
        WriteFileWithNotesOnAllen("1 OCCU Seeman", "2 SOUR @S00001@", "3 PAGE Muster roll, p. 12");

        var corrected = RunExpectSuccess(Ops(VitalOp("@I00001@", "OCCU", "\"value\": \"Seaman\", \"match\": { \"value\": \"Seeman\" }")));
        var replay = RunExpectSuccess(Ops(VitalOp("@I00001@", "OCCU", "\"value\": \"Seaman\", \"match\": { \"value\": \"Seaman\" }")));

        Assert.Equal(["OCCU @I00001@"], CreditedFacts(corrected, "@I00001@"));
        Assert.Empty(replay.Changes.Biographies);
    }

    [Fact]
    public void ANewCitationOnAnExistingCensus_EarnsCredit_ButNewQuotationTextDoesNot()
    {
        WriteFileWithNotesOnAllen();

        var cited = RunExpectSuccess(Ops("""
            { "op": "createOrUpdateCitation", "record": "@I00003@", "fact": "CENS", "match": { "date": "1930" },
              "citations": [ { "source": "@S00001@", "page": "Sheet 4" } ] }
            """));
        var quoted = RunExpectSuccess(Ops("""
            { "op": "createOrUpdateCitation", "record": "@I00003@", "fact": "CENS", "match": { "date": "1930" },
              "citations": [ { "source": "@S00001@", "page": "Sheet 4", "dataText": "Nellie Test, 32", "quay": 3 } ] }
            """));

        Assert.Equal(["CENS @I00003@"], CreditedFacts(cited, "@I00003@"));
        Assert.Empty(quoted.Changes.Biographies);
    }

    [Fact]
    public void ACitedFamilyFact_CreditsBothPartners_ButAMarriageIsTheEnrichCards()
    {
        WriteFileWithNotesOnAllen();

        var result = RunExpectSuccess(Ops(
            VitalOp("@F00002@", "DIV", "\"date\": \"1960\""),
            VitalOp("@F00003@", "MARR", "\"date\": \"1965\"")));

        Assert.Equal(["DIV @F00002@"], CreditedFacts(result, "@I00001@"));
        Assert.Equal(["DIV @F00002@"], CreditedFacts(result, "@I00004@"));
    }

    [Fact]
    public void ACitedFact_ClosesTheBiographyCard_NamingWhatItWrote()
    {
        WriteFileWithNotesOnAllen();

        var result = RunExpectSuccess(Ops(VitalOp("@I00001@", "IMMI", "\"date\": \"1950\", \"place\": \"New York\"")));

        var preview = GedCore.Points.CardScorer.Preview([new GedCore.Points.PointsCard("@I00001@", "biography", null, null, 3)], result.Changes);
        Assert.Equal("cited IMMI @I00001@", Assert.Single(preview.Closed).Wrote);
    }
}
