using System.Text;
using System.Text.Json;
using GedCore.Apply;
using GedCore.Drafting;

namespace GedCore.Tests;

/// <summary>A finding's note with withdraw and match drafts a deleteNote for the held note of exactly that text.</summary>
public class ChangesetDrafterNoteWithdrawalTests
{
    const string Kept = "Married 22 Jul 1911 at the register office, per the register entry.";
    const string Duplicate = "Married 7 Jul 1911 at the register office, per the register entry.";

    sealed class FakeLookup(string[] familyNotes, ExistingFact? marriage = null) : IDraftLookup
    {
        public FakeLookup(params string[] familyNotes) : this(familyNotes, null) { }

        public IReadOnlyList<ExistingFact> FactsOn(string record, string tag) =>
            record == "@F1@" && tag == "MARR" && marriage is not null ? [marriage] : [];
        public IReadOnlyList<string> NotesOn(string record) => record == "@F1@" ? familyNotes : [];
        public string? FindSource(string title, string? author) => null;
        public string? PersonName(string xref) => xref == "@I1@" ? "Walter /Hartley/" : null;
    }

    static DraftFinding Finding(string id, params DraftNote[] notes) =>
        new(id, new DraftSource("County Register", null, null, "Entry 41", null, "original", "primary"), "extract",
            new DraftTarget("@I1@", "enrich", null), [], notes);

    static DraftNote Withdraw(string match, string reason = "a duplicate of the 22 Jul entry") =>
        DraftNote.Withdrawal("@F1@", match, reason);

    static DraftResult Draft(FakeLookup lookup, params DraftFinding[] findings) => new ChangesetDrafter(lookup).Draft(findings);

    static JsonElement[] Ops(DraftResult result) =>
        [.. JsonDocument.Parse(result.Changeset!.ToJsonString()).RootElement
            .GetProperty("items").EnumerateArray().SelectMany(i => i.GetProperty("ops").EnumerateArray())];

    [Fact]
    public void AHeldNote_IsDeletedAndTheReasonReported()
    {
        var result = Draft(new FakeLookup(Kept, Duplicate), Finding("F-1", Withdraw(Duplicate)));

        Assert.Empty(result.Errors);
        var op = Assert.Single(Ops(result));
        Assert.Equal("deleteNote", op.GetProperty("op").GetString());
        Assert.Equal("@F1@", op.GetProperty("record").GetString());
        Assert.Equal(Duplicate, op.GetProperty("text").GetString());
        Assert.Equal(["F-1"], op.GetProperty("findings").EnumerateArray().Select(f => f.GetString()));
        Assert.Contains(result.Decisions, d => d.Kind == "resolved" && d.Detail.Contains("a duplicate of the 22 Jul entry"));
    }

    [Fact]
    public void AFindingThatOnlyWithdraws_MintsNoSource()
    {
        var result = Draft(new FakeLookup(Duplicate), Finding("F-1", Withdraw(Duplicate)));

        var changeset = JsonDocument.Parse(result.Changeset!.ToJsonString()).RootElement;
        Assert.False(changeset.TryGetProperty("newSources", out _));
        Assert.False(changeset.GetProperty("findings")[0].GetProperty("source").TryGetProperty("ref", out _));
        Assert.DoesNotContain(result.Decisions, d => d.Kind is "minted-source" or "reused-source");
    }

    [Fact]
    public void TwoFindingsWithdrawingOneNote_ShareOneOp()
    {
        var result = Draft(new FakeLookup(Duplicate), Finding("F-1", Withdraw(Duplicate)), Finding("F-2", Withdraw(Duplicate, "repeated")));

        var op = Assert.Single(Ops(result));
        Assert.Equal(["F-1", "F-2"], op.GetProperty("findings").EnumerateArray().Select(f => f.GetString()));
    }

    [Fact]
    public void AMatchTheRecordDoesNotHold_DraftsNothingAndIsLeftOpen()
    {
        var result = Draft(new FakeLookup(Kept), Finding("F-1", Withdraw("Married 7 Jul 1911.")));

        Assert.Empty(Ops(result));
        Assert.Contains("@F1@ holds no note \"Married 7 Jul 1911.\" to withdraw", Assert.Single(result.Decisions, d => d.Kind == "open").Detail);
    }

    [Fact]
    public void TheDraftedDeletion_RemovesOnlyThatNoteWhenApplied()
    {
        const string document = $"""
            0 HEAD
            1 GEDC
            2 VERS 7.0
            0 @I1@ INDI
            1 NAME Walter /Hartley/
            1 FAMS @F1@
            0 @F1@ FAM
            1 HUSB @I1@
            1 NOTE {Kept}
            1 NOTE {Duplicate}
            0 TRLR
            """;
        var draft = Draft(new FakeLookup(Kept, Duplicate), Finding("F-1", Withdraw(Duplicate)));

        var applied = ChangesetApplier.Run(Encoding.UTF8.GetBytes(document), Changeset.Parse(draft.Changeset!.ToJsonString()), [1], dryRun: true);

        Assert.True(applied.Success, string.Join("; ", applied.Errors));
        Assert.Contains(applied.Log, line => line.Contains("deleteNote on @F1@: deleted"));
    }
}
