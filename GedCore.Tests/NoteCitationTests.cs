namespace GedCore.Tests;

/// <summary>
/// createOrUpdateCitation / deleteCitation with "note": they address the
/// citations under one NOTE of a record, found by the note's exact text.
/// </summary>
public class NoteCitationTests : ApplyTestBase
{
    private const string NoteText = "Allen farmed near Fergus Falls per @S00001@.";

    // The note's text and its citation's quoted text both carry ids, as notes
    // written before the prose check did.
    private void WriteFileWithCitedNote(params string[] extraCitationLines) =>
        WriteFile([.. BaseLines.SelectMany(l => l == "1 FAMC @F00001@"
            ? new[] { l, $"1 NOTE {NoteText}", "2 SOUR @S00001@", "3 PAGE p. 1", "3 DATA", "4 TEXT matches @I00002@ exactly" }
                .Concat(extraCitationLines)
            : new[] { l })]);

    private static GedRecord Note(GedDocument doc) => doc.ByXref["@I00001@"].ChildrenByTag("NOTE").Single();

    [Fact]
    public void Create_CorrectsTheQuotedTextOfANoteCitation()
    {
        WriteFileWithCitedNote();

        var result = RunExpectSuccess($$"""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateCitation", "record": "@I00001@", "note": "{{NoteText}}",
                "citation": { "source": "@S00001@", "page": "p. 1", "dataText": "matches Harvey Test exactly" } } ] } ] }
            """);

        var citation = Note(ReadDoc()).ChildrenByTag("SOUR").Single();
        Assert.Equal("matches Harvey Test exactly", citation.FirstChild("DATA")!.FirstChild("TEXT")!.Value);
        Assert.Contains(result.Log, l => l.StartsWith("createOrUpdateCitation on @I00001@ (note \"Allen farmed near"));
    }

    [Fact]
    public void Create_RerunIsANoOp()
    {
        WriteFileWithCitedNote();
        string json = $$"""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateCitation", "record": "@I00001@", "note": "{{NoteText}}",
                "citation": { "source": "@S00001@", "page": "p. 1", "dataText": "matches Harvey Test exactly" } } ] } ] }
            """;
        RunExpectSuccess(json);
        byte[] afterFirst = ReadBytes();

        var second = RunExpectSuccess(json);

        Assert.Contains(second.Log, l => l.Contains("no-op (already cited identically)"));
        Assert.Equal(afterFirst, ReadBytes());
    }

    [Fact]
    public void Create_StillRefusesAnIdInTheCorrectedText()
    {
        WriteFileWithCitedNote();

        var result = Run($$"""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateCitation", "record": "@I00001@", "note": "{{NoteText}}",
                "citation": { "source": "@S00001@", "page": "p. 1", "dataText": "see @I00003@" } } ] } ] }
            """);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("internal id @I00003@"));
    }

    [Fact]
    public void Create_ForANoteThatIsNotThere_IsRefused()
    {
        WriteFileWithCitedNote();

        var result = Run("""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateCitation", "record": "@I00001@", "note": "No such note.",
                "citation": { "source": "@S00001@", "page": "p. 1" } } ] } ] }
            """);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("no note has that text"));
    }

    [Fact]
    public void FactAndNoteTogether_AreRefused()
    {
        WriteFileWithCitedNote();

        var result = Run($$"""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateCitation", "record": "@I00001@", "fact": "BIRT", "note": "{{NoteText}}",
                "citation": { "source": "@S00001@", "page": "p. 1" } } ] } ] }
            """);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("either \"fact\" or \"note\""));
    }

    [Fact]
    public void MatchWithNote_IsRefused()
    {
        WriteFileWithCitedNote();

        var result = Run($$"""
            { "items": [ { "item": 1, "ops": [
              { "op": "deleteCitation", "record": "@I00001@", "note": "{{NoteText}}", "match": { "date": "1900" },
                "source": "@S00001@" } ] } ] }
            """);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("\"match\" selects a fact"));
    }

    [Fact]
    public void Delete_RemovesTheNoteCitationAndKeepsTheNote()
    {
        WriteFileWithCitedNote();

        var result = RunExpectSuccess($$"""
            { "items": [ { "item": 1, "ops": [
              { "op": "deleteCitation", "record": "@I00001@", "note": "{{NoteText}}", "source": "@S00001@" } ] } ] }
            """);

        var note = Note(ReadDoc());
        Assert.Empty(note.ChildrenByTag("SOUR"));
        Assert.Equal(NoteText, note.Value);
        Assert.Contains(result.Log, l => l.Contains("(note \"") && l.EndsWith("removed citation @S00001@"));
    }

    [Fact]
    public void Delete_WhenTheNoteIsAbsent_IsANoOp()
    {
        WriteFileWithCitedNote();

        var result = RunExpectSuccess("""
            { "items": [ { "item": 1, "ops": [
              { "op": "deleteCitation", "record": "@I00001@", "note": "No such note.", "source": "@S00001@" } ] } ] }
            """);

        Assert.Contains(result.Log, l => l.Contains("no-op (@S00001@ not cited)"));
    }

    [Fact]
    public void Delete_WithSeveralPagesNeedsThePage()
    {
        WriteFileWithCitedNote("2 SOUR @S00001@", "3 PAGE p. 2");

        var ambiguous = Run($$"""
            { "items": [ { "item": 1, "ops": [
              { "op": "deleteCitation", "record": "@I00001@", "note": "{{NoteText}}", "source": "@S00001@" } ] } ] }
            """);
        Assert.False(ambiguous.Success);
        Assert.Contains(ambiguous.Errors, e => e.Contains("add \"page\""));

        RunExpectSuccess($$"""
            { "items": [ { "item": 1, "ops": [
              { "op": "deleteCitation", "record": "@I00001@", "note": "{{NoteText}}", "source": "@S00001@",
                "page": "p. 2" } ] } ] }
            """);
        Assert.Equal("p. 1", Note(ReadDoc()).ChildrenByTag("SOUR").Single().FirstChild("PAGE")!.Value);
    }
}
