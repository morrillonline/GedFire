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
    public void NewTextBackedOnlyByACitationAnotherCitedNoteCarries_EarnsNothing()
    {
        WriteFileWithNotesOnAllen($"1 NOTE {Service}", "2 SOUR @S00001@", "3 PAGE muster  roll, P. 12", "3 QUAY 1");

        var result = RunExpectSuccess(NoteOp("Allen was rated seaman first class in 1945.", MusterRoll));

        Assert.Contains(result.Log, l => l.StartsWith("createOrUpdateNote on @I00001@: created"));
        Assert.Empty(result.Changes.Biographies);
    }

    [Fact]
    public void ABiographyOpOnAnAlreadyCitedNote_EarnsNothingForTheCitationsItHad()
    {
        WriteFileWithNotesOnAllen($"1 NOTE {Service}", "2 SOUR @S00001@", "3 PAGE Muster roll, p. 12");

        var result = RunExpectSuccess(NoteOp(Service, MusterRoll));

        Assert.Empty(result.Changes.Biographies);
    }

    [Fact]
    public void ANoteABiographyOpWrote_NeverReceivesCitationsMovedOffAReplacedFact()
    {
        WriteFileWithNotesOnAllen();
        RunExpectSuccess("""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateCitation", "record": "@I00001@", "fact": "BIRT",
                "citation": { "source": "@S00001@", "page": "p. 1", "quay": 1 } } ] } ] }
            """);

        var result = RunExpectSuccess($$"""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateNote", "record": "@I00001@", "text": "{{Service}}", "purpose": "biography",
                "citations": [ {{PensionFile}} ] },
              { "op": "createOrUpdateVital", "record": "@I00001@", "fact": "BIRT",
                "value": { "date": "2 APR 1928", "place": "Fergus Falls, Minnesota" }, "replacedCitations": "moveToNote",
                "citation": { "source": "@S00002@", "page": "Birth affidavit", "quay": 2 } } ] } ] }
            """);

        var notes = ReadDoc().ByXref["@I00001@"].ChildrenByTag("NOTE").ToList();
        Assert.Equal(["@S00002@"], notes.Single(n => n.Value == Service).ChildrenByTag("SOUR").Select(s => s.Value));
        Assert.Equal(["@S00001@"], notes.Single(n => n.Value != Service).ChildrenByTag("SOUR").Select(s => s.Value));
        Assert.Equal([new BiographyCitation("@S00002@", "File 4471")], Assert.Single(result.Changes.Biographies).NewCitations);
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
}
