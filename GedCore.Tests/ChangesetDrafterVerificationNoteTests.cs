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

    static DraftFinding Finding(string id, string card, DraftSource source, params DraftFact[] facts) =>
        new(id, source, $"extract {id}", new DraftTarget("@I1@", card, null), facts, []);

    static readonly ChangesetPolicy NotePolicy = ChangesetPolicy.Load(new MemoryStream(Encoding.UTF8.GetBytes(
        $$"""{ "verificationNote": { "maxQuay": 1, "notePrefix": "{{Prefix}}" } }""")));

    [Theory]
    [InlineData("RESI")]
    [InlineData("DEAT")]
    public void OneFactCitedFromTwoRecordsInTwoItems_IsNeverCalledSingleSource_AndThePackagePasses(string tag)
    {
        var result = new ChangesetDrafter(new FakeLookup(), NotePolicy).Draft([
            Finding("F-1", "enrich-a", Index("1920 Census Index", "Sheet 4"), Fact(tag, "1920", 1)),
            Finding("F-2", "enrich-b", Index("City Directory", "p. 88"), Fact(tag, "1920", 1)),
        ]);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        var notes = Notes(result);
        Assert.Equal(2, notes.Count);
        Assert.Equal(notes[0], notes[1]);
        Assert.DoesNotContain("alone", notes[0]);
        Assert.Contains("rests on 1920 Census Index and City Directory", notes[0]);
        Assert.Empty(GedCore.Packages.PackageChecks.Check(
            [new GedCore.Packages.PackageChangeset(1, result.Changeset!.ToJsonString())],
            new NoSources(), new GedCore.Packages.PackageOptions([], Prefix)));
    }

    sealed class NoSources : GedCore.Packages.ISourceRecords
    {
        public GedCore.Packages.SourceDescription? Source(string xref) => null;
    }

    [Fact]
    public void AFactCitedFromOneRecord_IsStillCalledSingleSource_BesideAnotherItem()
    {
        var result = Draft(new FakeLookup(),
            Finding("F-1", "enrich-a", Index("1920 Census Index", "Sheet 4"), Fact("RESI", "1920", 1)),
            Finding("F-2", "enrich-b", Index("City Directory", "p. 88"), Fact("RESI", "1930", 1)));

        Assert.All(Notes(result), note => Assert.Contains("alone and has not yet been confirmed by a second source", note));
    }

    [Theory]
    [InlineData("OCCU", "occupation")]
    [InlineData("RESI", "residence")]
    [InlineData("EDUC", "education")]
    [InlineData("NATU", "naturalization")]
    public void ANotesFieldIsWrittenInWords_NeverAsATag(string tag, string word)
    {
        var fact = new DraftFact("@I1@", tag, "1920", null, 1, null, null, null, Value: tag == "NATU" ? null : "x");
        var result = new ChangesetDrafter(new FakeLookup(), NotePolicy).Draft([Finding("F-1", Index("City Directory", "p. 88"), fact)]);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        string note = Assert.Single(Notes(result));
        Assert.Contains($"the {word} of Gladys Giedd", note);
        Assert.DoesNotMatch($@"(?i)\b{tag}\b", note);
    }

    [Fact]
    public void ANoteOnALongerNamedFact_DoesNotCoverTheShorterOne()
    {
        string held = $"{Prefix} the adult christening of Gladys Giedd rests on Parish Register (secondary information) alone and has not yet been confirmed by a second source.";

        var result = new ChangesetDrafter(new FakeLookup(notes: [held]), NotePolicy)
            .Draft([Finding("F-1", Index("Parish Register", "p. 3"), Fact("CHR", "1850", 1))]);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Contains("the christening of Gladys Giedd", Assert.Single(Notes(result)));
    }

    [Fact]
    public void ANoteAnOlderVersionWroteWithATag_StillCountsForThatFact()
    {
        string older = $"{Prefix} the occu of Gladys Giedd rests on City Directory (secondary information) alone and has not yet been confirmed by a second source.";
        var fact = new DraftFact("@I1@", "OCCU", "1920", null, 1, null, null, null, Value: "farmer");

        var result = new ChangesetDrafter(new FakeLookup(notes: [older]), NotePolicy).Draft([Finding("F-1", Index("City Directory", "p. 88"), fact)]);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Empty(Notes(result));
    }

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

    static readonly ChangesetPolicy VerificationPolicy = ChangesetPolicy.Load(new MemoryStream(Encoding.UTF8.GetBytes(
        """{ "verificationNote": { "maxQuay": 1, "notePrefix": "needs independent verification:" } }""")));

    static List<JsonElement> Ops(DraftResult result) =>
        [.. JsonDocument.Parse(result.Changeset!.ToJsonString()).RootElement.GetProperty("items")[0].GetProperty("ops").EnumerateArray()];

    [Fact]
    public void AFactTheTreeAlreadyCitesAboveTheCeiling_GetsNoNote_AndThePolicyAgrees()
    {
        var lookup = new FakeLookup(new() { ["@I1@|DEAT"] = [Held("1900", 2)] });

        var result = new ChangesetDrafter(lookup, VerificationPolicy).Draft([
            Finding("F-1", Index("Find a Grave Index", "Memorial #1"), Fact("DEAT", "1900", 1))]);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Empty(Notes(result));
    }

    [Fact]
    public void ALowFactWithNoNote_GetsOneThePolicyAccepts()
    {
        var result = new ChangesetDrafter(new FakeLookup(), VerificationPolicy).Draft([
            Finding("F-1", Index("Census Index", "Line 4"), Fact("BIRT", "1899", 1))]);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Contains("the birth of Gladys Giedd", Assert.Single(Notes(result)));
    }

    [Fact]
    public void ASoleSourceNoteOnAFactNowCitedTwice_IsRewrittenToNameBoth_AndThePolicyAccepts()
    {
        string held = $"{Prefix} the birth of Gladys Giedd rests on State Death Index (secondary information) alone and has not yet been confirmed by a second source.";
        var lookup = new FakeLookup(new() { ["@I1@|BIRT"] = [Held("29 JAN 1899", 1, "State Death Index")] }, [held]);

        var result = new ChangesetDrafter(lookup, VerificationPolicy).Draft([
            Finding("F-1", Index("1900 Census", "Sheet 3"), Fact("BIRT", "JAN 1899", 1))]);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        var note = Assert.Single(Ops(result), o => o.GetProperty("op").GetString() == "createOrUpdateNote");
        Assert.Equal(held, note.GetProperty("match").GetString());
        Assert.Equal(
            $"{Prefix} the birth of Gladys Giedd rests on 1900 Census and State Death Index (secondary information) and has not yet been confirmed by a stronger source.",
            note.GetProperty("text").GetString());
        Assert.Contains(result.Decisions, d => d.Kind == "verification-note" && d.Detail.Contains("no longer says it rests on one alone"));
    }

    [Fact]
    public void AnAuthorsVerificationNoteNamingTheFact_SatisfiesThePolicyWithoutANewNote()
    {
        var lookup = new FakeLookup(notes: [$"{Prefix} her birth year is from a single index."]);

        var result = new ChangesetDrafter(lookup, VerificationPolicy).Draft([
            Finding("F-1", Index("Census Index", "Line 4"), Fact("BIRT", "1899", 1))]);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Empty(Notes(result));
    }

    [Fact]
    public void AVerificationNoteNamingTheDeath_DoesNotCountForTheBirth()
    {
        var lookup = new FakeLookup(notes: [$"{Prefix} the death of Gladys Giedd rests on Ohio Death Index (secondary information) alone and has not yet been confirmed by a second source."]);

        var result = new ChangesetDrafter(lookup, VerificationPolicy).Draft([
            Finding("F-1", Index("Census Index", "Line 4"), Fact("BIRT", "1899", 1))]);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Contains("the birth of Gladys Giedd", Assert.Single(Notes(result)));
    }

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
