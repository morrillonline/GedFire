using System.Text;
using System.Text.Json;
using GedCore.Apply;
using GedCore.Drafting;

namespace GedCore.Tests;

public class ChangesetDrafterNoteAndCitationTests
{
    sealed class FakeLookup : IDraftLookup
    {
        public IReadOnlyList<ExistingFact> FactsOn(string record, string tag) => [];
        public IReadOnlyList<string> NotesOn(string record) => [];
        public string? FindSource(string title, string? author) => null;
        public string? PersonName(string xref) => null;
    }

    const string Document = """
        0 HEAD
        1 GEDC
        2 VERS 7.0
        0 @I1@ INDI
        1 NAME Hiram /Test/
        1 SEX M
        1 DEAT
        2 DATE 1873
        2 SOUR @S1@
        3 PAGE old page
        1 NOTE Old note text.
        0 @S1@ SOUR
        1 TITL Memorial Index
        0 TRLR
        """;

    static DraftSource Source(string name, string locator, string cls = "original", string info = "primary") =>
        new(name, null, null, locator, null, cls, info);

    static DraftFinding Finding(
        string id, DraftSource source, string extract, IReadOnlyList<DraftFact>? facts = null, IReadOnlyList<DraftNote>? notes = null,
        string xref = "@I1@") =>
        new(id, source, extract, new DraftTarget(xref, "enrich", null), facts ?? [], notes ?? []);

    static DraftFact Fact(string tag, string? date, string? place, int quay = 2) =>
        new("@I1@", tag, date, place, quay, null, null, null);

    static JsonElement[] Ops(DraftResult result) =>
        [.. JsonDocument.Parse(result.Changeset!.ToJsonString()).RootElement
            .GetProperty("items").EnumerateArray().SelectMany(i => i.GetProperty("ops").EnumerateArray())];

    static ApplyResult DryRun(DraftResult result) =>
        ChangesetApplier.Run(Encoding.UTF8.GetBytes(Document), Changeset.Parse(result.Changeset!.ToJsonString()), [1], dryRun: true);

    static DraftResult Draft(params DraftFinding[] findings) => new ChangesetDrafter(new FakeLookup()).Draft(findings);

    static void AssertApplies(DraftResult result)
    {
        Assert.True(result.Success, string.Join("; ", result.Errors));
        var applied = DryRun(result);
        Assert.True(applied.Success, string.Join("; ", applied.Errors));
    }

    // ---- notes ----

    [Fact]
    public void ABiographyNoteWithNoCites_IsRefused_AndPassesOnceCited()
    {
        DraftFinding With(IReadOnlyList<DraftCite> cites) => Finding("F-1", Source("Register", "p. 2"), "Hiram, farmer.",
            notes: [new DraftNote("@I1@", "Hiram farmed.") { Purpose = CreateOrUpdateNoteOp.BiographyPurpose, Cites = cites }]);

        Assert.Equal("findings[0].notes[0] is a biography note with no cites; a biography note cites the findings that support it.",
            Assert.Single(Draft(With([])).Errors));
        AssertApplies(Draft(With([new DraftCite(null, 1, null)])));
    }

    [Fact]
    public void ANoteCitingTwoFindings_CarriesBothCitationsAtTheirOwnGrades()
    {
        var obituary = Finding("F-1", Source("Obituaries", "Star obituary"), "Hiram died 1873.", [Fact("BIRT", "1800", null, quay: 1)]);
        var register = Finding("F-2", Source("Register", "Register p. 2"), "Hiram, born 1800.",
            notes: [new DraftNote("@I1@", "He appears in two sources.") { Cites = [new DraftCite("F-1", 1, null), new DraftCite(null, 2, null)] }]);

        var result = Draft(obituary, register);

        var note = Ops(result).Single(o => o.TryGetProperty("text", out var text) && text.GetString() == "He appears in two sources.");
        var cites = note.GetProperty("citations").EnumerateArray().ToList();
        Assert.Equal(["Star obituary", "Register p. 2"], cites.Select(c => c.GetProperty("page").GetString()));
        Assert.Equal([1, 2], cites.Select(c => c.GetProperty("quay").GetInt32()));
        Assert.Equal("Hiram died 1873.", cites[0].GetProperty("dataText").GetString());
        AssertApplies(result);
    }

    [Fact]
    public void ABiographyNote_CompilesToABiographyNoteOpThatEarnsItsCitation()
    {
        var pension = Finding("F-1", Source("Pension Files", "File 4471"), "Hiram served in the 3rd Maine.",
            notes: [new DraftNote("@I1@", "Hiram served in the 3rd Maine Infantry.") { Purpose = "biography", Cites = [new DraftCite(null, 2, null)] }]);

        var result = Draft(pension);

        var note = Ops(result).Single(o => o.GetProperty("op").GetString() == "createOrUpdateNote");
        Assert.Equal("biography", note.GetProperty("purpose").GetString());
        var applied = DryRun(result);
        Assert.True(applied.Success, string.Join("; ", applied.Errors));
        Assert.Equal("File 4471", Assert.Single(Assert.Single(applied.Changes.Biographies).NewCitations).Page);
    }

    [Fact]
    public void ANoteCitingOneFinding_UsesTheCitationsArray()
    {
        var finding = Finding("F-1", Source("Register", "Register p. 2"), "Hiram, narrower. Hiram.",
            notes: [new DraftNote("@I1@", "A cited note.") { Cites = [new DraftCite(null, 2, "Hiram, narrower.")] }]);

        var note = Ops(Draft(finding))[0];

        Assert.Equal("Hiram, narrower.", Assert.Single(note.GetProperty("citations").EnumerateArray()).GetProperty("dataText").GetString());
        Assert.False(note.TryGetProperty("citation", out _));
    }

    [Fact]
    public void ANoteMatch_RewritesTheExistingNote()
    {
        var finding = Finding("F-1", Source("Register", "p. 2"), "x",
            notes: [new DraftNote("@I1@", "New note text.") { Match = "Old note text." }]);

        var result = Draft(finding);

        Assert.Equal("Old note text.", Ops(result)[0].GetProperty("match").GetString());
        AssertApplies(result);
    }

    [Fact]
    public void ANoteCitingAnUnknownFinding_IsRefused()
    {
        var finding = Finding("F-1", Source("Register", "p. 2"), "x",
            notes: [new DraftNote("@I1@", "N.") { Cites = [new DraftCite("F-9", 1, null)] }]);

        Assert.Contains("findings[0].notes[0].cites[0].finding \"F-9\" is not a finding of this draft", Assert.Single(Draft(finding).Errors));
    }

    [Fact]
    public void ANoteCitingAFindingAboutAnotherPerson_IsDraftedAsACitationOfItsRecordOnly()
    {
        var marriage = Finding("F-2", Source("Marriage Returns", "Return 88"), "Hiram Test married Ada Vale, 1842.",
            [Fact("BIRT", "1800", null) with { Record = "@I2@" }], xref: "@I2@");
        var finding = Finding("F-1", Source("Register", "p. 2"), "x",
            notes: [new DraftNote("@I1@", "His marriage return gives a later age.") { Cites = [new DraftCite("F-2", 2, null)] }]);

        var result = new ChangesetDrafter(new FakeLookup()).Draft([finding], cited: [marriage]);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        var note = Assert.Single(Ops(result));
        Assert.Equal("createOrUpdateNote", note.GetProperty("op").GetString());
        var citation = Assert.Single(note.GetProperty("citations").EnumerateArray());
        Assert.Equal("Return 88", citation.GetProperty("page").GetString());
        Assert.Equal("Hiram Test married Ada Vale, 1842.", citation.GetProperty("dataText").GetString());
        Assert.Equal(["F-2"], note.GetProperty("findings").EnumerateArray().Select(f => f.GetString()));
        var table = JsonDocument.Parse(result.Changeset!.ToJsonString()).RootElement.GetProperty("findings").EnumerateArray().ToList();
        var cited = Assert.Single(table, f => f.GetProperty("id").GetString() == "F-2");
        Assert.False(cited.TryGetProperty("item", out _));
        Assert.False(cited.TryGetProperty("claims", out _));
        Assert.Empty(Changeset.Read(result.Changeset!.ToJsonString()).Warnings);
        using var document = JsonDocument.Parse(result.Changeset!.ToJsonString());
        Assert.True(SchemaCatalogTests.Compile(GedFire.Schemas.SchemaCatalog.Changeset).Evaluate(document.RootElement).IsValid);
        AssertApplies(result);
    }

    [Fact]
    public void ANoteCitingAFindingAboutAnotherPersonInTheSameDraft_CitesItAndDraftsItInItsOwnItem()
    {
        var other = Finding("F-2", Source("Register", "p. 3"), "y", [Fact("BIRT", "1800", null) with { Record = "@I2@" }], xref: "@I2@");
        var finding = Finding("F-1", Source("Register", "p. 2"), "x",
            notes: [new DraftNote("@I1@", "N.") { Cites = [new DraftCite("F-2", 2, null)] }]);

        var result = Draft(finding, other);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        var items = JsonDocument.Parse(result.Changeset!.ToJsonString()).RootElement.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal(2, items.Count);
        Assert.Equal("p. 3", items[0].GetProperty("ops")[0].GetProperty("citations")[0].GetProperty("page").GetString());
    }

    [Fact]
    public void AFactCitingAnotherFinding_AddsThatRecordsCitationAndDraftsNothingFromIt()
    {
        var marriage = Finding("F-2", Source("Marriage Returns", "Return 88"), "Hiram Test, aged 42, married Ada Vale.",
            [Fact("BIRT", "1790", null) with { Record = "@I2@" }, Fact("DEAT", "1850", null) with { Record = "@I2@" }], xref: "@I2@");
        var finding = Finding("F-1", Source("Register", "p. 2"), "Hiram born 1800.",
            [Fact("BIRT", "ABT 1800", null) with { Cites = [new DraftCite("F-2", 2, "aged 42")] }]);

        var result = new ChangesetDrafter(new FakeLookup()).Draft([finding], cited: [marriage]);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        var birth = Assert.Single(Ops(result));
        Assert.Equal("@I1@", birth.GetProperty("record").GetString());
        Assert.Equal(["p. 2", "Return 88"], birth.GetProperty("citations").EnumerateArray().Select(c => c.GetProperty("page").GetString()));
        Assert.Equal("aged 42", birth.GetProperty("citations")[1].GetProperty("dataText").GetString());
        Assert.Equal(["F-1", "F-2"], birth.GetProperty("findings").EnumerateArray().Select(f => f.GetString()));
        AssertApplies(result);
    }

    [Fact]
    public void AFactCiteOfItsOwnFindingOrOfNoFinding_IsRefused()
    {
        var own = Finding("F-1", Source("Register", "p. 2"), "x", [Fact("BIRT", "1800", null) with { Cites = [new DraftCite("F-1", 2, null)] }]);
        var missing = Finding("F-1", Source("Register", "p. 2"), "x", [Fact("BIRT", "1800", null) with { Cites = [new DraftCite("F-9", 2, null)] }]);

        Assert.StartsWith("findings[0].facts[0].cites[0].finding \"F-1\" is the fact's own finding", Assert.Single(Draft(own).Errors));
        Assert.Equal("findings[0].facts[0].cites[0].finding \"F-9\" is not a finding of this draft, nor a finding file beside it (F-9.json).",
            Assert.Single(Draft(missing).Errors));
    }

    [Fact]
    public void ACiteGradingACitedFindingsTextDifferentlyFromItsOwnClaim_IsRefusedUntilGivenAnExcerpt()
    {
        var marriage = Finding("F-2", Source("Marriage Returns", "Return 88"), "Hiram Test, aged 42, married Ada Vale.",
            [Fact("MARR", "1842", null) with { Record = "@F1@" }], xref: "@I2@");
        DraftFinding Citing(string? excerpt) => Finding("F-1", Source("Register", "p. 2"), "x",
            notes: [new DraftNote("@I1@", "N.") { Cites = [new DraftCite("F-2", 1, excerpt)] }]);
        var drafter = new ChangesetDrafter(new FakeLookup());

        Assert.Equal(
            "findings[0].notes[0].cites[0].quay is 1, but finding \"F-2\".facts[0] cites the same text of finding \"F-2\" at 2; one quoted text carries one grade, so give the differently graded claim its own excerpt.",
            Assert.Single(drafter.Draft([Citing(null)], cited: [marriage]).Errors));
        Assert.Empty(drafter.Draft([Citing("aged 42")], cited: [marriage]).Errors);
    }

    [Fact]
    public void ACitedFindingNoNoteOrFactCites_IsLeftOut()
    {
        var unused = Finding("F-2", Source("Marriage Returns", "Return 88"), "y", xref: "@I2@");

        var result = new ChangesetDrafter(new FakeLookup()).Draft([Finding("F-1", Source("Register", "p. 2"), "x", [Fact("BIRT", "1800", null)])], cited: [unused]);

        var changeset = JsonDocument.Parse(result.Changeset!.ToJsonString()).RootElement;
        Assert.DoesNotContain(changeset.GetProperty("findings").EnumerateArray(), f => f.GetProperty("id").GetString() == "F-2");
        Assert.Single(changeset.GetProperty("newSources").EnumerateArray());
    }

    [Fact]
    public void ANoteCitationAboveTheCitedSourcesCeiling_IsRefused()
    {
        var finding = Finding("F-1", Source("Index", "p. 2", cls: "derivative", info: "secondary"), "x",
            notes: [new DraftNote("@I1@", "N.") { Cites = [new DraftCite(null, 2, null)] }]);

        Assert.Contains("findings[0].notes[0].cites[0].quay is 2", Assert.Single(Draft(finding).Errors));
    }

    [Fact]
    public void AFindingWithNothingOfItsOwn_IsAllowedWhenANoteCitesIt()
    {
        var supporting = Finding("F-1", Source("Obituaries", "Star obituary"), "Hiram died 1873.");
        var author = Finding("F-2", Source("Register", "Register p. 2"), "Hiram.",
            [Fact("BIRT", "1800", null)], [new DraftNote("@I1@", "Two sources.") { Cites = [new DraftCite("F-1", 2, null)] }]);

        var result = Draft(supporting, author);

        Assert.True(result.Success, string.Join("; ", result.Errors));
    }

    [Fact]
    public void AFindingWithNothingAndNoCitingNote_IsRefused()
    {
        var result = Draft(Finding("F-1", Source("Obituaries", "Star obituary"), "Hiram died 1873."));

        Assert.Contains("findings[0] has no facts, relations or notes", Assert.Single(result.Errors));
    }

    // ---- facts ----

    [Fact]
    public void AFactWithSubstructures_IsAddedWithThem()
    {
        var pension = Fact("EVEN", "24 JUL 1890", "Wisconsin") with { Substructures = [new DraftSubstructure("TYPE", "Pension Application")] };
        var service = Fact("EVEN", "BET 1861 AND 1865", "Wisconsin") with { Substructures = [new DraftSubstructure("TYPE", "Military Service")] };

        var result = Draft(Finding("F-1", Source("Pensions", "file 1"), "x", [pension, service]));

        var ops = Ops(result);
        Assert.All(ops, o => Assert.Equal("add", o.GetProperty("mode").GetString()));
        Assert.Equal(["Pension Application", "Military Service"],
            ops.Select(o => o.GetProperty("substructures")[0].GetProperty("value").GetString()));
        AssertApplies(result);
    }

    [Fact]
    public void ReplacedCitations_IsPassedToTheOp()
    {
        var replacing = Fact("DEAT", "1874", null) with { ReplacedCitations = "moveToNote" };

        var result = Draft(Finding("F-1", Source("Register", "p. 2"), "x", [replacing]));

        Assert.Equal("moveToNote", Ops(result)[0].GetProperty("replacedCitations").GetString());
        AssertApplies(result);
    }

    [Fact]
    public void KeepIsTheDefaultDisposition_AndIsNotWritten()
    {
        var keeping = Fact("DEAT", "1874", null) with { ReplacedCitations = "keep" };

        Assert.False(Ops(Draft(Finding("F-1", Source("Register", "p. 2"), "x", [keeping])))[0].TryGetProperty("replacedCitations", out _));
    }

    [Fact]
    public void ACiteOnlyFact_BecomesACitationOpOnTheExistingFact()
    {
        var finding = Finding("F-1", Source("Find a Grave", "memorial 1"), "DEATH 1873",
            [Fact("DEAT", null, null, quay: 1) with { CiteOnly = true }]);

        var result = Draft(finding);

        var op = Ops(result)[0];
        Assert.Equal("createOrUpdateCitation", op.GetProperty("op").GetString());
        Assert.Equal("DEAT", op.GetProperty("fact").GetString());
        Assert.False(op.TryGetProperty("match", out _));
        Assert.Equal("memorial 1", op.GetProperty("citations")[0].GetProperty("page").GetString());
        AssertApplies(result);
    }

    [Fact]
    public void CiteOnlyFactsOnOneInstance_ShareOneOpAndTheDateSelectsTheInstance()
    {
        var first = Finding("F-1", Source("Find a Grave", "memorial 1"), "a", [Fact("DEAT", "1873", null, quay: 1) with { CiteOnly = true }]);
        var second = Finding("F-2", Source("Register", "p. 2"), "b", [Fact("DEAT", "1873", null) with { CiteOnly = true }]);

        var result = Draft(first, second);

        var op = Assert.Single(Ops(result), o => o.GetProperty("op").GetString() == "createOrUpdateCitation");
        Assert.Equal("1873", op.GetProperty("match").GetProperty("date").GetString());
        Assert.Equal(2, op.GetProperty("citations").GetArrayLength());
        AssertApplies(result);
    }

    // ---- relations ----

    [Fact]
    public void NotDuplicateOf_IsPassedToTheSpouseAndChildOps()
    {
        var wife = new DraftPerson("@NewI1@", "Mary /Test/", "F");
        var child = new DraftPerson("@NewI2@", "Ann /Test/", null);
        var finding = Finding("F-1", Source("Census", "p. 1"), "x") with
        {
            People = [wife, child],
            Relations =
            [
                new DraftRelation(DraftRelation.Spouse, "@I1@", "@NewI1@", "@NewF1@", null, null, null, 2) { NotDuplicateOf = ["@I1@"] },
                new DraftRelation(DraftRelation.Child, "", "@NewI2@", "@NewF1@", null, null, null, 2) { NotDuplicateOf = ["@I1@"] },
            ],
        };

        var ops = Ops(Draft(finding));

        Assert.All(ops, o => Assert.Equal("@I1@", o.GetProperty("notDuplicateOf")[0].GetString()));
    }
}
