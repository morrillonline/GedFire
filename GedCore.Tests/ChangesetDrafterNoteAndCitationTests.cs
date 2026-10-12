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
    public void ANoteCitingTwoFindings_CarriesBothCitationsAtTheirOwnGrades()
    {
        var obituary = Finding("F-1", Source("Obituaries", "Star obituary"), "Hiram died 1873.", [Fact("BIRT", "1800", null, quay: 1)]);
        var register = Finding("F-2", Source("Register", "Register p. 2"), "Hiram, born 1800.",
            notes: [new DraftNote("@I1@", "He appears in two sources.") { Cites = [new DraftNoteCitation("F-1", 1, null), new DraftNoteCitation(null, 2, null)] }]);

        var result = Draft(obituary, register);

        var note = Ops(result).Single(o => o.TryGetProperty("text", out var text) && text.GetString() == "He appears in two sources.");
        var cites = note.GetProperty("citations").EnumerateArray().ToList();
        Assert.Equal(["Star obituary", "Register p. 2"], cites.Select(c => c.GetProperty("page").GetString()));
        Assert.Equal([1, 2], cites.Select(c => c.GetProperty("quay").GetInt32()));
        Assert.Equal("Hiram died 1873.", cites[0].GetProperty("dataText").GetString());
        AssertApplies(result);
    }

    [Fact]
    public void ANoteCitingOneFinding_UsesTheSingularCitationKey()
    {
        var finding = Finding("F-1", Source("Register", "Register p. 2"), "Hiram.",
            notes: [new DraftNote("@I1@", "A cited note.") { Cites = [new DraftNoteCitation(null, 2, "Hiram, narrower.")] }]);

        var note = Ops(Draft(finding))[0];

        Assert.Equal("Hiram, narrower.", note.GetProperty("citation").GetProperty("dataText").GetString());
        Assert.False(note.TryGetProperty("citations", out _));
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
            notes: [new DraftNote("@I1@", "N.") { Cites = [new DraftNoteCitation("F-9", 1, null)] }]);

        Assert.Contains("findings[0].notes[0].cites[0].finding \"F-9\" is not a finding of this draft", Assert.Single(Draft(finding).Errors));
    }

    [Fact]
    public void ANoteCitingAFindingOfAnotherItem_IsRefused()
    {
        var other = Finding("F-2", Source("Register", "p. 3"), "y", [Fact("BIRT", "1800", null)] , xref: "@I2@");
        var finding = Finding("F-1", Source("Register", "p. 2"), "x",
            notes: [new DraftNote("@I1@", "N.") { Cites = [new DraftNoteCitation("F-2", 1, null)] }]);

        Assert.Contains("aimed at a different target", Assert.Single(Draft(finding, other).Errors));
    }

    [Fact]
    public void ANoteCitationAboveTheCitedSourcesCeiling_IsRefused()
    {
        var finding = Finding("F-1", Source("Index", "p. 2", cls: "derivative", info: "secondary"), "x",
            notes: [new DraftNote("@I1@", "N.") { Cites = [new DraftNoteCitation(null, 2, null)] }]);

        Assert.Contains("findings[0].notes[0].cites[0].quay is 2", Assert.Single(Draft(finding).Errors));
    }

    [Fact]
    public void AFindingWithNothingOfItsOwn_IsAllowedWhenANoteCitesIt()
    {
        var supporting = Finding("F-1", Source("Obituaries", "Star obituary"), "Hiram died 1873.");
        var author = Finding("F-2", Source("Register", "Register p. 2"), "Hiram.",
            [Fact("BIRT", "1800", null)], [new DraftNote("@I1@", "Two sources.") { Cites = [new DraftNoteCitation("F-1", 2, null)] }]);

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
        Assert.Equal("memorial 1", op.GetProperty("citation").GetProperty("page").GetString());
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
