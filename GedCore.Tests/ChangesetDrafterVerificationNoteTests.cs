using System.Text;
using System.Text.Json;
using GedCore.Apply;
using GedCore.Drafting;
using GedCore.Policy;

namespace GedCore.Tests;

/// <summary>Each fact is judged on its own best citation, in the item or already in the document, before a verification note is written for it.</summary>
public class ChangesetDrafterVerificationNoteTests
{
    sealed class FakeLookup(Dictionary<string, ExistingFact[]>? facts = null, string[]? notes = null) : IDraftLookup
    {
        public IReadOnlyList<ExistingFact> FactsOn(string record, string tag) => facts?.GetValueOrDefault($"{record}|{tag}") ?? [];
        public IReadOnlyList<string> NotesOn(string record) => record == "@I1@" ? notes ?? [] : [];
        public string? FindSource(string title, string? author) => null;
        public string? PersonName(string xref) => xref == "@I1@" ? "Gladys Giedd" : null;
    }

    const string Prefix = "Needs independent verification:";

    static DraftSource Index(string name, string locator, string info = "secondary") => new(name, null, null, locator, null, "derivative", info);

    static readonly DraftSource StateRecord = new("Ohio Death Certificates", null, null, "Certificate 4411", null, "original", "primary");

    static DraftFact Fact(string tag, string? date, int quay) => new("@I1@", tag, date, null, quay, null, null, null);

    static DraftFinding Finding(string id, DraftSource source, params DraftFact[] facts) =>
        new(id, source, $"extract {id}", new DraftTarget("@I1@", "enrich", null), facts, []);

    static ExistingFact Held(string? date, int? quay, string? title = "Family Bible") =>
        new(date, null, [new ExistingCitation("@S9@", "p. 1", null, quay, title)]);

    static DraftResult Draft(FakeLookup lookup, params DraftFinding[] findings) => new ChangesetDrafter(lookup).Draft(findings);

    static List<string> Notes(DraftResult result) =>
        [.. JsonDocument.Parse(result.Changeset!.ToJsonString()).RootElement.GetProperty("items").EnumerateArray()
            .SelectMany(i => i.GetProperty("ops").EnumerateArray())
            .Where(o => o.GetProperty("op").GetString() == "createOrUpdateNote")
            .Select(o => o.GetProperty("text").GetString()!)];

    // ---- one assessment per fact ----

    [Fact]
    public void TwoLowFacts_EachGetTheirOwnNote()
    {
        var result = Draft(new FakeLookup(),
            Finding("F-1", Index("Find a Grave Index", "Memorial #1"), Fact("BIRT", "1850", 1)),
            Finding("F-2", Index("Ohio Death Index", "Entry 7"), Fact("DEAT", "1900", 1)));

        Assert.Equal(
            [
                $"{Prefix} the birth of Gladys Giedd rests on Find a Grave Index (secondary information) alone and has not yet been confirmed by a second source.",
                $"{Prefix} the death of Gladys Giedd rests on Ohio Death Index (secondary information) alone and has not yet been confirmed by a second source.",
            ],
            Notes(result));
    }

    [Fact]
    public void OnlyTheLowFactIsNoted_WhenAnotherFactOfThePersonIsStrong()
    {
        var result = Draft(new FakeLookup(),
            Finding("F-1", Index("Find a Grave Index", "Memorial #1"), Fact("BIRT", "1850", 1)),
            Finding("F-2", StateRecord, Fact("DEAT", "1900", 3)));

        Assert.Contains("the birth of Gladys Giedd", Assert.Single(Notes(result)));
    }

    // ---- the best citation counts ----

    [Fact]
    public void ADeathAlsoCitedToAStateRecordAtQuay2_GetsNoNote()
    {
        var result = Draft(new FakeLookup(),
            Finding("F-1", Index("Find a Grave Index", "Memorial #1"), Fact("DEAT", "1900", 1)),
            Finding("F-2", StateRecord, Fact("DEAT", "1900", 2)));

        Assert.Empty(Notes(result));
        Assert.Contains(result.Decisions, d => d.Kind == "verification-note" && d.Detail.Contains("DEAT is cited at QUAY 2"));
    }

    [Fact]
    public void AQuay2CitationTheTreeAlreadyHolds_LiftsTheFact()
    {
        var lookup = new FakeLookup(new() { ["@I1@|DEAT"] = [Held("1900", 2)] });

        var result = Draft(lookup, Finding("F-1", Index("Find a Grave Index", "Memorial #1"), Fact("DEAT", "1900", 1)));

        Assert.Empty(Notes(result));
    }

    [Fact]
    public void AnUngradedTreeCitation_DoesNotLiftTheFact()
    {
        var lookup = new FakeLookup(new() { ["@I1@|DEAT"] = [Held("1900", null)] });

        var result = Draft(lookup, Finding("F-1", Index("Find a Grave Index", "Memorial #1"), Fact("DEAT", "1900", 1)));

        Assert.Single(Notes(result));
    }

    [Fact]
    public void AStrongCitationOnAnotherInstanceOfARepeatableTag_DoesNotLiftThisOne()
    {
        var lookup = new FakeLookup(new() { ["@I1@|CENS"] = [Held("1900", 3)] });

        var result = Draft(lookup,
            Finding("F-1", Index("Census Index", "Line 4"), Fact("CENS", "1900", 1)),
            Finding("F-2", Index("Census Index", "Line 9"), Fact("CENS", "1910", 1)));

        Assert.Equal(
            [$"{Prefix} the census entry of Gladys Giedd rests on Census Index (secondary information) alone and has not yet been confirmed by a second source."],
            Notes(result));
    }

    // ---- every record is named ----

    [Fact]
    public void AFactCitedToSeveralLowRecords_NamesEveryOne()
    {
        var lookup = new FakeLookup(new() { ["@I1@|DEAT"] = [Held("1900", 1)] });

        var result = Draft(lookup,
            Finding("F-1", Index("Find a Grave Index", "Memorial #1"), Fact("DEAT", "1900", 1)),
            Finding("F-2", Index("Ohio Death Index", "Entry 7"), Fact("DEAT", "1900", 1)));

        Assert.Equal(
            [$"{Prefix} the death of Gladys Giedd rests on Find a Grave Index, Ohio Death Index and Family Bible (secondary information) and has not yet been confirmed by a stronger source."],
            Notes(result));
    }

    [Fact]
    public void TwoPagesOfOneCollection_AreNamedAsTwoRecords()
    {
        var result = Draft(new FakeLookup(),
            Finding("F-1", Index("Find a Grave Index", "Memorial #1"), Fact("DEAT", "1900", 1)),
            Finding("F-2", Index("Find a Grave Index", "Memorial #2"), Fact("DEAT", "1900", 1)));

        Assert.Equal(
            [$"{Prefix} the death of Gladys Giedd rests on Find a Grave Index (Memorial #1) and Find a Grave Index (Memorial #2) (secondary information) and has not yet been confirmed by a stronger source."],
            Notes(result));
    }

    [Fact]
    public void ATreeCitationWithoutATitle_IsStillCountedWithoutNamingItsXref()
    {
        var lookup = new FakeLookup(new() { ["@I1@|DEAT"] = [Held("1900", 0, title: null)] });

        var note = Assert.Single(Notes(Draft(lookup, Finding("F-1", Index("Find a Grave Index", "Memorial #1"), Fact("DEAT", "1900", 1)))));

        Assert.Contains("rests on Find a Grave Index and a source the record already cites", note);
        Assert.DoesNotContain("@S9@", note);
    }

    [Fact]
    public void TheSameRecordInTheItemAndTheTree_IsNamedOnce()
    {
        var lookup = new FakeLookup(new() { ["@I1@|DEAT"] = [new ExistingFact("1900", null, [new ExistingCitation("@NewSource1@", "Memorial #1", null, 1, "Find a Grave Index")])] });

        var note = Assert.Single(Notes(Draft(lookup, Finding("F-1", Index("Find a Grave Index", "Memorial #1"), Fact("DEAT", "1900", 1)))));

        Assert.Contains("rests on Find a Grave Index (secondary information) alone", note);
    }

    // ---- notes already on the record ----

    [Fact]
    public void ADifferentlyWordedNoteOnASolidlyCitedBirth_GetsNoNoteBesideIt()
    {
        var lookup = new FakeLookup(
            new() { ["@I1@|BIRT"] = [Held("1850", 2)] },
            [$"{Prefix} Gladys was born in 1850 according to one index."]);

        var result = Draft(lookup, Finding("F-1", Index("Find a Grave Index", "Memorial #1"), Fact("BIRT", "1850", 1)));

        Assert.Empty(Notes(result));
        Assert.DoesNotContain(result.Decisions, d => d.Kind == "open");
    }

    [Fact]
    public void ADifferentlyWordedNoteStatingTheSameCaveat_IsNotDuplicated()
    {
        var lookup = new FakeLookup(notes: [$"{Prefix} her birth year is from a single index."]);

        var result = Draft(lookup, Finding("F-1", Index("Find a Grave Index", "Memorial #1"), Fact("BIRT", "1850", 1)));

        Assert.Empty(Notes(result));
        Assert.Contains(result.Decisions, d => d.Kind == "verification-note" && d.Detail.Contains("already says the BIRT awaits verification"));
        Assert.DoesNotContain(result.Decisions, d => d.Kind == "open");
    }

    [Fact]
    public void ACompilerNoteStillNamingTheFact_IsNotDuplicated()
    {
        var lookup = new FakeLookup(notes:
            [$"{Prefix} the birth of Gladys Giedd rests on Old Register (secondary information) alone and has not yet been confirmed by a second source."]);

        var result = Draft(lookup, Finding("F-1", Index("Find a Grave Index", "Memorial #1"), Fact("BIRT", "1850", 1)));

        Assert.Empty(Notes(result));
    }

    [Fact]
    public void ANoteAboutAnotherFact_DoesNotSuppressThisOne()
    {
        var lookup = new FakeLookup(notes: [$"{Prefix} her birth year is from a single index."]);

        var result = Draft(lookup, Finding("F-1", Index("Find a Grave Index", "Memorial #1"), Fact("DEAT", "1900", 1)));

        Assert.Contains("the death of Gladys Giedd", Assert.Single(Notes(result)));
    }

    [Fact]
    public void ANoteNamingSeveralRecords_IsRemovedOnceTheFactIsLifted()
    {
        string held = $"{Prefix} the death of Gladys Giedd rests on Find a Grave Index and Ohio Death Index (secondary information) and has not yet been confirmed by a stronger source.";

        var result = Draft(new FakeLookup(notes: [held]), Finding("F-1", StateRecord, Fact("DEAT", "1900", 2)));

        var ops = JsonDocument.Parse(result.Changeset!.ToJsonString()).RootElement.GetProperty("items")[0].GetProperty("ops");
        Assert.Contains(ops.EnumerateArray(), o => o.GetProperty("op").GetString() == "deleteNote" && o.GetProperty("text").GetString() == held);
    }

    [Fact]
    public void AnAuthorsVerificationNote_StillReplacesTheCompilersForEveryFact()
    {
        var finding = Finding("F-1", Index("Find a Grave Index", "Memorial #1"), Fact("BIRT", "1850", 1), Fact("DEAT", "1900", 1)) with
        {
            Notes = [new DraftNote("@I1@", $"{Prefix} both dates come from one memorial.")],
        };

        var result = Draft(new FakeLookup(), finding);

        Assert.Equal([$"{Prefix} both dates come from one memorial."], Notes(result));
    }

    // ---- accepted downstream ----

    [Fact]
    public void AFactLiftedByTheItem_PassesTheVerificationPolicy()
    {
        var policy = ChangesetPolicy.Load(new MemoryStream(Encoding.UTF8.GetBytes(
            """{ "verificationNote": { "maxQuay": 1, "notePrefix": "needs independent verification:" } }""")));

        var result = new ChangesetDrafter(new FakeLookup(), policy).Draft([
            Finding("F-1", Index("Find a Grave Index", "Memorial #1"), Fact("DEAT", "1900", 1)),
            Finding("F-2", StateRecord, Fact("DEAT", "1900", 2))]);

        Assert.True(result.Success, string.Join("; ", result.Errors));
    }

    [Fact]
    public void ANoteNamingSeveralRecords_IsAcceptedByTheApplier()
    {
        var result = Draft(new FakeLookup(),
            Finding("F-1", Index("Find a Grave Index", "Memorial #1"), Fact("DEAT", "1900", 1)),
            Finding("F-2", Index("Ohio Death Index", "Entry 7"), Fact("DEAT", "1900", 1)));
        const string document = """
            0 HEAD
            1 GEDC
            2 VERS 7.0
            0 @I1@ INDI
            1 NAME Gladys /Giedd/
            0 TRLR
            """;

        var applied = ChangesetApplier.Run(Encoding.UTF8.GetBytes(document), Changeset.Parse(result.Changeset!.ToJsonString()), [1], dryRun: true);

        Assert.True(applied.Success, string.Join("; ", applied.Errors));
    }
}
