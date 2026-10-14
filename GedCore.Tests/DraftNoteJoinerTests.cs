using GedCore.Drafting;

namespace GedCore.Tests;

public class DraftNoteJoinerTests
{
    static readonly DraftSource Source = new("Obituary", null, null, "p. 1", null, "derivative", "primary");

    static DraftFinding Finding(string id, string xref, string? card, params DraftNote[] notes) =>
        new(id, Source, "text", new DraftTarget(xref, card, null), [], notes);

    static DraftNote Note(string record, string text, string? citedFinding = null, int quay = 2) =>
        new(record, text) { Cites = [new DraftNoteCitation(citedFinding, quay, null)] };

    [Fact]
    public void NotesOnTheSameRecordInOneItem_BecomeOneNoteCarriedByTheFirstFinding()
    {
        var joined = DraftNoteJoiner.Join([
            Finding("F-1", "@I1@", "enrich", Note("@I1@", "Died in Salem.")),
            Finding("F-2", "@I1@", "enrich", Note("@I1@", "Buried in Salem.", quay: 1))]);

        var note = Assert.Single(joined[0].Notes);
        Assert.Equal("Died in Salem. Buried in Salem.", note.Text);
        Assert.Empty(joined[1].Notes);
        Assert.Equal([("F-1", 2), ("F-2", 1)], note.Cites.Select(c => (c.Finding!, c.Quay)));
    }

    [Fact]
    public void ACitationThatNamedAFinding_KeepsItAndIdenticalCitationsCollapse()
    {
        var joined = DraftNoteJoiner.Join([
            Finding("F-1", "@I1@", null, Note("@I1@", "One.", "F-2")),
            Finding("F-2", "@I1@", null, Note("@I1@", "Two."))]);

        Assert.Equal(["F-2"], Assert.Single(joined[0].Notes).Cites.Select(c => c.Finding));
    }

    [Fact]
    public void TheSameSentenceTwice_IsKeptOnce()
    {
        var joined = DraftNoteJoiner.Join([
            Finding("F-1", "@I1@", null, Note("@I1@", "Died in Salem.")),
            Finding("F-2", "@I1@", null, Note("@I1@", "Died in Salem."))]);

        Assert.Equal("Died in Salem.", Assert.Single(joined[0].Notes).Text);
    }

    [Fact]
    public void NotesOnDifferentRecords_AreLeftAlone()
    {
        var joined = DraftNoteJoiner.Join([
            Finding("F-1", "@I1@", null, Note("@I1@", "One.")),
            Finding("F-2", "@I1@", null, Note("@I2@", "Two."))]);

        Assert.Equal("One.", Assert.Single(joined[0].Notes).Text);
        Assert.Equal("Two.", Assert.Single(joined[1].Notes).Text);
        Assert.Null(joined[0].Notes[0].Cites[0].Finding);
    }

    [Fact]
    public void NotesInDifferentItems_AreNotJoined()
    {
        var joined = DraftNoteJoiner.Join([
            Finding("F-1", "@I1@", "enrich", Note("@I1@", "One.")),
            Finding("F-2", "@I1@", "new-spouse", Note("@I1@", "Two."))]);

        Assert.Single(joined[0].Notes);
        Assert.Single(joined[1].Notes);
    }

    [Fact]
    public void ARewritingNote_IsNeverJoined()
    {
        var rewrite = Note("@I1@", "New wording.") with { Match = "Old wording." };

        var joined = DraftNoteJoiner.Join([
            Finding("F-1", "@I1@", null, rewrite),
            Finding("F-2", "@I1@", null, Note("@I1@", "Added."))]);

        Assert.Equal("Old wording.", Assert.Single(joined[0].Notes).Match);
        Assert.Equal("Added.", Assert.Single(joined[1].Notes).Text);
    }

    [Fact]
    public void AFindingWithOtherNotes_KeepsThemInOrder()
    {
        var joined = DraftNoteJoiner.Join([
            Finding("F-1", "@I1@", null, Note("@I2@", "Before."), Note("@I1@", "Shared one."), Note("@I3@", "After.")),
            Finding("F-2", "@I1@", null, Note("@I1@", "Shared two."))]);

        Assert.Equal(["Before.", "Shared one. Shared two.", "After."], joined[0].Notes.Select(n => n.Text));
    }

    [Fact]
    public void TheInputList_IsNotChanged()
    {
        var input = new[]
        {
            Finding("F-1", "@I1@", null, Note("@I1@", "One.")),
            Finding("F-2", "@I1@", null, Note("@I1@", "Two.")),
        };

        DraftNoteJoiner.Join(input);

        Assert.Single(input[1].Notes);
    }
}
