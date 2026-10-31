using System.Text;
using System.Text.Json;
using GedCore.Apply;
using GedCore.Drafting;

namespace GedCore.Tests;

/// <summary>
/// A finding's date against the date the record holds: precision decides only between dates that can both be true;
/// hard bounds narrow one another; a contradiction is settled by the evidence or left open; the author can replace or withdraw.
/// </summary>
public class ChangesetDrafterHeldDateTests
{
    sealed class FakeLookup(Dictionary<string, ExistingFact[]> facts) : IDraftLookup
    {
        public IReadOnlyList<ExistingFact> FactsOn(string record, string tag) => facts.GetValueOrDefault($"{record}|{tag}") ?? [];
        public IReadOnlyList<string> NotesOn(string record) => [];
        public string? FindSource(string title, string? author) => null;
        public string? PersonName(string xref) => null;
    }

    static DraftFinding Finding(string id, string? date, int quay = 2, string tag = "DEAT", string? replaceHeld = null, string? withdraw = null, string? place = null) =>
        new(id, new DraftSource("Probate Records", null, null, $"Case {id}", null, "original", "primary"), $"extract {id}",
            new DraftTarget("@I1@", "enrich", null),
            [new DraftFact("@I1@", tag, date, place, quay, null, null, null) { ReplaceHeld = replaceHeld, Withdraw = withdraw }], []);

    static FakeLookup Holding(string? date, params int?[] citationGrades) => HoldingTag("DEAT", date, citationGrades);

    static FakeLookup HoldingTag(string tag, string? date, params int?[] citationGrades) =>
        new(new() { [$"@I1@|{tag}"] = [new ExistingFact(date, null, [.. citationGrades.Select((q, i) => new ExistingCitation($"@S{i + 1}@", $"p. {i}", null, q))])] });

    static DraftResult Draft(IDraftLookup lookup, params DraftFinding[] findings) => new ChangesetDrafter(lookup).Draft(findings);

    static JsonElement[] Ops(DraftResult result) =>
        [.. JsonDocument.Parse(result.Changeset!.ToJsonString()).RootElement
            .GetProperty("items").EnumerateArray().SelectMany(i => i.GetProperty("ops").EnumerateArray())];

    static JsonElement[] OpsOf(DraftResult result, string kind) => [.. Ops(result).Where(o => o.GetProperty("op").GetString() == kind)];

    static string? DateOf(JsonElement vital) =>
        vital.GetProperty("value").TryGetProperty("date", out var date) ? date.GetString() : null;

    static string? Str(JsonElement op, string name) => op.TryGetProperty(name, out var value) ? value.GetString() : null;

    // ---- a bound that excludes the record's date ----

    [Fact]
    public void ABoundThatExcludesAnUncitedRecordDate_ReplacesItAndKeepsItsCitationsInANote()
    {
        var result = Draft(Holding("17 OCT 1810"), Finding("F-1", "BEF 27 SEP 1796"));

        var vital = Assert.Single(OpsOf(result, "createOrUpdateVital"));
        Assert.Equal("BEF 27 SEP 1796", DateOf(vital));
        Assert.Equal("moveToNote", Str(vital, "replacedCitations"));
        var resolved = Assert.Single(result.Decisions, d => d.Kind == "resolved");
        Assert.Contains("17 OCT 1810 does not fit BEF 27 SEP 1796 and is uncited", resolved.Detail);
        Assert.Equal(["F-1"], resolved.Findings);
    }

    [Fact]
    public void ABoundThatExcludesALowerGradedRecordDate_ReplacesIt()
    {
        var result = Draft(Holding("17 OCT 1810", 1), Finding("F-1", "BEF 27 SEP 1796", quay: 2));

        Assert.Equal("BEF 27 SEP 1796", DateOf(Assert.Single(OpsOf(result, "createOrUpdateVital"))));
        Assert.Contains(result.Decisions, d => d.Kind == "resolved" && d.Detail.Contains("cited at QUAY 1, below the finding's QUAY 2"));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(null)]
    public void ABoundThatExcludesAnEquallyOrBetterOrUngradedCitedRecordDate_IsLeftOpen(int? grade)
    {
        var result = Draft(Holding("17 OCT 1810", grade), Finding("F-1", "BEF 27 SEP 1796", quay: 2));

        Assert.Empty(OpsOf(result, "createOrUpdateVital"));
        Assert.Empty(OpsOf(result, "createOrUpdateCitation"));
        var open = Assert.Single(result.Decisions, d => d.Kind == "open");
        Assert.Contains("does not fit BEF 27 SEP 1796", open.Detail);
        Assert.Contains("replaceHeld", open.Detail);
    }

    [Fact]
    public void ABoundTheRecordDateFits_OnlyAddsItsCitation()
    {
        var result = Draft(Holding("3 MAR 1795"), Finding("F-1", "BEF 27 SEP 1796"));

        Assert.Empty(OpsOf(result, "createOrUpdateVital"));
        Assert.Single(OpsOf(result, "createOrUpdateCitation"));
        Assert.Contains(result.Decisions, d => d.Kind == "resolved" && d.Detail.Contains("no improvement"));
    }

    [Fact]
    public void AFindingDateOutsideTheRecordsBound_ReplacesAnUncitedBound()
    {
        var result = Draft(Holding("BEF 1796"), Finding("F-1", "17 OCT 1810"));

        var vital = Assert.Single(OpsOf(result, "createOrUpdateVital"));
        Assert.Equal("17 OCT 1810", DateOf(vital));
        Assert.Equal("moveToNote", Str(vital, "replacedCitations"));
    }

    [Fact]
    public void AFindingDateOutsideTheRecordsBetterCitedBound_IsLeftOpen()
    {
        var result = Draft(Holding("BEF 1796", 3), Finding("F-1", "17 OCT 1810"));

        Assert.Empty(OpsOf(result, "createOrUpdateVital"));
        Assert.Single(result.Decisions, d => d.Kind == "open");
    }

    // ---- bounds narrow one another ----

    [Fact]
    public void ABoundBesideTheRecordsBound_WritesTheirIntersectionAndKeepsTheCitations()
    {
        var result = Draft(Holding("AFT 1795", 2), Finding("F-1", "BEF 27 SEP 1796"));

        var vital = Assert.Single(OpsOf(result, "createOrUpdateVital"));
        Assert.Equal("BET 1795 AND 27 SEP 1796", DateOf(vital));
        Assert.Null(Str(vital, "replacedCitations"));
        Assert.Contains(result.Decisions, d => d.Kind == "resolved" && d.Detail.Contains("together give BET 1795 AND 27 SEP 1796"));
    }

    [Theory]
    [InlineData("BEF 1797", "BEF 1800")]
    [InlineData("BET 1790 AND 1797", "AFT 1785")]
    public void ABoundTheRecordAlreadyNarrows_OnlyAddsItsCitation(string held, string given)
    {
        var result = Draft(Holding(held), Finding("F-1", given));

        Assert.Empty(OpsOf(result, "createOrUpdateVital"));
        Assert.Single(OpsOf(result, "createOrUpdateCitation"));
    }

    [Fact]
    public void ANarrowerBound_ReplacesTheRecordsWiderOne()
    {
        var result = Draft(Holding("BET 1790 AND 1800"), Finding("F-1", "BEF 1795"));

        Assert.Equal("BET 1790 AND 1795", DateOf(Assert.Single(OpsOf(result, "createOrUpdateVital"))));
    }

    [Fact]
    public void BoundsFromTwoFindings_AreCombinedAndBothCited()
    {
        var result = Draft(new FakeLookup(new()), Finding("F-1", "AFT 1795"), Finding("F-2", "BEF 1797"));

        var vital = Assert.Single(OpsOf(result, "createOrUpdateVital"));
        Assert.Equal("BET 1795 AND 1797", DateOf(vital));
        Assert.Equal(2, vital.GetProperty("citations").GetArrayLength());
        Assert.DoesNotContain(result.Decisions, d => d.Kind == "open");
    }

    [Theory]
    [InlineData("AFT 1800", "BEF 1797")]
    [InlineData("AFT 1797", "BEF 1797")]
    [InlineData("AFT 1796", "BEF SEP 1796")]
    public void BoundsThatCannotBothHoldOrBeOrdered_AreLeftOpen(string first, string second)
    {
        var result = Draft(new FakeLookup(new()), Finding("F-1", first), Finding("F-2", second));

        Assert.Empty(OpsOf(result, "createOrUpdateVital"));
        Assert.Single(result.Decisions, d => d.Kind == "open");
    }

    // ---- soft approximations ----

    [Fact]
    public void AnAboutDateFarFromTheRecordsDate_DraftsNothingAndIsLeftOpen()
    {
        var result = Draft(HoldingTag("BIRT", "14 APR 1877"), Finding("F-1", "ABT 1880", tag: "BIRT"));

        Assert.Empty(Ops(result).Where(o => o.GetProperty("op").GetString() is "createOrUpdateVital" or "createOrUpdateCitation"));
        Assert.Contains("more than a year from the record's 14 APR 1877", Assert.Single(result.Decisions, d => d.Kind == "open").Detail);
    }

    [Fact]
    public void ASpecificDateFarFromTheRecordsEstimate_IsWrittenWithAWarning()
    {
        var result = Draft(HoldingTag("BIRT", "ABT 1860"), Finding("F-1", "1 DEC 1855", tag: "BIRT"));

        Assert.Equal("1 DEC 1855", DateOf(Assert.Single(OpsOf(result, "createOrUpdateVital"))));
        Assert.Contains("check that both describe the same event", Assert.Single(result.Decisions, d => d.Kind == "open").Detail);
    }

    // ---- the author replaces or withdraws ----

    [Fact]
    public void ReplaceHeld_WritesALessSpecificDateOverABetterCitedOne()
    {
        var result = Draft(HoldingTag("BIRT", "14 APR 1877", 3), Finding("F-1", "1877", tag: "BIRT", replaceHeld: "the day belongs to a cousin"));

        var vital = Assert.Single(OpsOf(result, "createOrUpdateVital"));
        Assert.Equal("1877", DateOf(vital));
        Assert.Null(Str(vital, "replacedCitations"));
        Assert.Contains(result.Decisions, d => d.Kind == "resolved" && d.Detail.Contains("as the author directs, the day belongs to a cousin"));
    }

    [Fact]
    public void ReplaceHeld_SettlesAContradictionAndMovesTheOldCitations()
    {
        var result = Draft(Holding("17 OCT 1810", 3), Finding("F-1", "BEF 27 SEP 1796", replaceHeld: "1810 is the son's death"));

        var vital = Assert.Single(OpsOf(result, "createOrUpdateVital"));
        Assert.Equal("BEF 27 SEP 1796", DateOf(vital));
        Assert.Equal("moveToNote", Str(vital, "replacedCitations"));
        Assert.DoesNotContain(result.Decisions, d => d.Kind == "open");
    }

    [Fact]
    public void ReplaceHeldIsCarriedIntoTheFindingsTable()
    {
        var result = Draft(Holding("17 OCT 1810", 3), Finding("F-1", "BEF 27 SEP 1796", replaceHeld: "1810 is the son's death"));

        var claim = JsonDocument.Parse(result.Changeset!.ToJsonString()).RootElement
            .GetProperty("findings").EnumerateArray().Single().GetProperty("claims").EnumerateArray().Single();
        Assert.Equal("1810 is the son's death", claim.GetProperty("replaceHeld").GetString());
    }

    [Fact]
    public void Withdraw_DeletesTheSelectedFactAndAsksForACitedNote()
    {
        var result = Draft(HoldingTag("BIRT", "ABT 1755", 0),
            Finding("F-1", "ABT 1755", tag: "BIRT", withdraw: "she bore children 1735-1739"));

        var delete = Assert.Single(OpsOf(result, "deleteVital"));
        Assert.Equal("BIRT", Str(delete, "fact"));
        Assert.Equal("ABT 1755", delete.GetProperty("match").GetProperty("date").GetString());
        Assert.Equal("moveToNote", Str(delete, "deletedCitations"));
        Assert.Empty(OpsOf(result, "createOrUpdateVital"));
        Assert.Contains(result.Decisions, d => d.Kind == "resolved" && d.Detail.Contains("withdrawn: BIRT on @I1@ (ABT 1755), she bore children 1735-1739"));
        Assert.Contains(result.Decisions, d => d.Kind == "open" && d.Detail.Contains("record the evidence in a note"));
    }

    [Fact]
    public void Withdraw_WithACitedNote_RaisesNothingOpen()
    {
        var finding = Finding("F-1", null, tag: "BIRT", withdraw: "she bore children 1735-1739") with
        {
            Notes = [new DraftNote("@I1@", "Her recorded birth about 1755 cannot stand: she bore children 1735-1739.") { Cites = [new DraftNoteCitation(null, 2, null)] }],
        };

        var result = Draft(HoldingTag("BIRT", "ABT 1755"), finding);

        Assert.Single(OpsOf(result, "deleteVital"));
        Assert.DoesNotContain(result.Decisions, d => d.Kind == "open");
    }

    [Fact]
    public void WithdrawingWhatTheRecordDoesNotHold_DraftsNothing()
    {
        var result = Draft(HoldingTag("BIRT", "ABT 1755"), Finding("F-1", "1760", tag: "BIRT", withdraw: "wrong"));

        Assert.Empty(OpsOf(result, "deleteVital"));
        Assert.Contains("holds no BIRT (1760) to withdraw", Assert.Single(result.Decisions, d => d.Kind == "open").Detail);
    }

    // ---- accepted by the applier ----

    const string Document = """
        0 HEAD
        1 GEDC
        2 VERS 7.0
        0 @I1@ INDI
        1 NAME Micajah /Test/
        1 SEX M
        1 BIRT
        2 DATE ABT 1755
        2 SOUR @S1@
        3 PAGE p. 0
        1 DEAT
        2 DATE 17 OCT 1810
        0 @S1@ SOUR
        1 TITL Old Tree
        0 TRLR
        """;

    static ApplyResult Apply(DraftResult result) =>
        ChangesetApplier.Run(Encoding.UTF8.GetBytes(Document), Changeset.Parse(result.Changeset!.ToJsonString()), [1], dryRun: true);

    [Fact]
    public void AReplacedContradictedDate_IsAcceptedByTheApplier()
    {
        var applied = Apply(Draft(Holding("17 OCT 1810"), Finding("F-1", "BEF 27 SEP 1796")));

        Assert.True(applied.Success, string.Join("; ", applied.Errors));
    }

    [Fact]
    public void AWithdrawal_IsAcceptedByTheApplier()
    {
        var applied = Apply(Draft(HoldingTag("BIRT", "ABT 1755", 0), Finding("F-1", null, tag: "BIRT", withdraw: "she bore children 1735-1739")));

        Assert.True(applied.Success, string.Join("; ", applied.Errors));
        Assert.Contains(applied.Log, line => line.Contains("deleteVital BIRT on @I1@: deleted (1 citation(s) moved to note)"));
    }
}
