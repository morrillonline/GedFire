using System.Text;
using System.Text.Json;
using GedCore.Apply;
using GedCore.Drafting;
using GedCore.Policy;

namespace GedCore.Tests;

/// <summary>Findings that give different amounts of one single-valued fact are reconciled; ones that disagree are left open.</summary>
public class ChangesetDrafterReconcileTests
{
    sealed class FakeLookup(Dictionary<string, ExistingFact[]>? facts = null) : IDraftLookup
    {
        public IReadOnlyList<ExistingFact> FactsOn(string record, string tag) => facts?.GetValueOrDefault($"{record}|{tag}") ?? [];
        public IReadOnlyList<string> NotesOn(string record) => [];
        public string? FindSource(string title, string? author) => null;
        public string? PersonName(string xref) => null;
    }

    static DraftFinding Finding(string id, string? date, string? place, int quay = 2, string info = "primary", string tag = "BIRT") =>
        new(id, new DraftSource("Find a Grave", null, null, $"Memorial #{id}", null, "derivative", info), $"extract {id}",
            new DraftTarget("@I1@", "enrich", null),
            [new DraftFact("@I1@", tag, date, place, quay, null, null, null)], []);

    static FakeLookup Holding(string date, string place, params ExistingCitation[] citations) =>
        new(new() { ["@I1@|BIRT"] = [new ExistingFact(date, place, citations)] });

    static JsonElement[] Ops(DraftResult result) =>
        [.. JsonDocument.Parse(result.Changeset!.ToJsonString()).RootElement
            .GetProperty("items").EnumerateArray().SelectMany(i => i.GetProperty("ops").EnumerateArray())];

    static IEnumerable<JsonElement> Vitals(DraftResult result) => Ops(result).Where(o => o.GetProperty("op").GetString() == "createOrUpdateVital");

    static int Citations(JsonElement op) =>
        op.TryGetProperty("citations", out var many) ? many.GetArrayLength() : op.TryGetProperty("citation", out _) ? 1 : 0;

    static DraftResult Draft(params DraftFinding[] findings) => new ChangesetDrafter(new FakeLookup()).Draft(findings);

    static DraftDecision Single(DraftResult result, string kind) => Assert.Single(result.Decisions, d => d.Kind == kind);

    // ---- findings that refine one another ----

    [Fact]
    public void FindingsThatRefineOneAnother_AreDraftedOnceWithEveryCitation()
    {
        var result = Draft(
            Finding("F-1", "14 APR 1877", "Maine", 1, "secondary"),
            Finding("F-2", "14 APR 1877", "Oakland, Maine"),
            Finding("F-3", "14 APR 1877", "Oakland, Kennebec, Maine"),
            Finding("F-4", "1877", null));

        var vital = Assert.Single(Vitals(result));
        Assert.Equal("14 APR 1877", vital.GetProperty("value").GetProperty("date").GetString());
        Assert.Equal("Oakland, Kennebec, Maine", vital.GetProperty("value").GetProperty("place").GetString());
        Assert.Equal(4, Citations(vital));
        Assert.DoesNotContain(result.Decisions, d => d.Kind == "open" && d.Detail.Contains("different values"));
        var resolved = Single(result, "resolved");
        Assert.Contains("BIRT on @I1@", resolved.Detail);
        Assert.Contains("F-2 for the date", resolved.Detail);
        Assert.Contains("F-3 for the place", resolved.Detail);
        Assert.Equal(["F-1", "F-2", "F-3", "F-4"], resolved.Findings.Order());
    }

    [Fact]
    public void TheDateCanComeFromOneFindingAndThePlaceFromAnother()
    {
        var result = Draft(
            Finding("F-1", "14 APR 1877", "Maine"),
            Finding("F-2", "1877", "Oakland, Maine"));

        var vital = Assert.Single(Vitals(result));
        Assert.Equal("14 APR 1877", vital.GetProperty("value").GetProperty("date").GetString());
        Assert.Equal("Oakland, Maine", vital.GetProperty("value").GetProperty("place").GetString());
        Assert.Equal(2, Citations(vital));
        var detail = Single(result, "resolved").Detail;
        Assert.Contains("F-1 for the date (QUAY 2)", detail);
        Assert.Contains("F-2 for the place (QUAY 2)", detail);
    }

    [Theory]
    [InlineData("1877", "ABT 1877")]
    [InlineData("1877", "BEF 1880")]
    [InlineData("MAR 1877", "AFT 1870")]
    [InlineData("14 APR 1877", "BET 1875 AND 1880")]
    [InlineData("14 APR 1877", "EST 1877")]
    public void AnApproximateDate_IsAlwaysSupersededByASpecificOne(string specific, string approximate)
    {
        var result = Draft(Finding("F-1", approximate, null), Finding("F-2", specific, null));

        var vital = Assert.Single(Vitals(result));
        Assert.Equal(specific, vital.GetProperty("value").GetProperty("date").GetString());
        Assert.Equal(2, Citations(vital));
    }

    [Fact]
    public void AnApproximateDateThatDoesNotFitTheChosenOne_IsSupersededButItsCitationIsNotAttached()
    {
        var result = Draft(Finding("F-1", "ABT 1860", null), Finding("F-2", "1 DEC 1855", null));

        var vital = Assert.Single(Vitals(result));
        Assert.Equal("1 DEC 1855", vital.GetProperty("value").GetProperty("date").GetString());
        Assert.Equal(1, Citations(vital));
        var open = Single(result, "open");
        Assert.Contains("F-1's ABT 1860", open.Detail);
        Assert.Equal(["F-1"], open.Findings);
    }

    [Fact]
    public void AFactNotAttached_DoesNotForceAVerificationNote()
    {
        var result = Draft(Finding("F-1", "ABT 1860", null, 1, "secondary"), Finding("F-2", "1 DEC 1855", null));

        Assert.DoesNotContain(Ops(result), o => o.GetProperty("op").GetString() == "createOrUpdateNote");
    }

    [Fact]
    public void AnAttachedLowGradedFact_StillGetsItsVerificationNote()
    {
        var result = Draft(Finding("F-1", "1855", null, 1, "secondary"), Finding("F-2", "1 DEC 1855", null));

        Assert.Contains(Ops(result), o => o.GetProperty("op").GetString() == "createOrUpdateNote");
    }

    [Fact]
    public void TheSameApproximateDateTwice_IsOneFact()
    {
        var result = Draft(Finding("F-1", "ABT 1855", null), Finding("F-2", "ABT 1855", null));

        Assert.Equal(2, Citations(Assert.Single(Vitals(result))));
        Assert.DoesNotContain(result.Decisions, d => d.Kind == "open");
    }

    [Fact]
    public void PaddingAndCaseDoNotMakeDatesDiffer()
    {
        var result = Draft(Finding("F-1", "05 Apr 1877", null), Finding("F-2", "5 APR 1877", null));

        Assert.Equal(2, Citations(Assert.Single(Vitals(result))));
        Assert.DoesNotContain(result.Decisions, d => d.Kind == "open");
    }

    // ---- findings that disagree ----

    [Fact]
    public void InconsistentDates_AreLeftOpenNamingTheRecommendedWinnerAndEveryValue()
    {
        var result = Draft(
            Finding("F-1", "15 OCT 1987", null, 1, "secondary"),
            Finding("F-2", "24 OCT 1987", null, 2),
            Finding("F-3", "31 OCT 1987", null, 1, "secondary"));

        Assert.Empty(Vitals(result));
        var open = Single(result, "open");
        Assert.Contains("3 different values", open.Detail);
        Assert.Contains("15 OCT 1987 (F-1; QUAY 1)", open.Detail);
        Assert.Contains("24 OCT 1987 (F-2; QUAY 2)", open.Detail);
        Assert.Contains("31 OCT 1987 (F-3; QUAY 1)", open.Detail);
        Assert.Contains("recommended F-2 (24 OCT 1987, QUAY 2)", open.Detail);
    }

    [Fact]
    public void TheRecommendationBreaksAQuayTieByTheBetterSource()
    {
        var derivative = Finding("F-1", "15 OCT 1987", null, 1);
        var original = Finding("F-2", "24 OCT 1987", null, 1) with
        {
            Source = new DraftSource("Register", null, null, "p. 4", null, "original", "primary"),
        };

        var result = Draft(derivative, original);

        Assert.Contains("recommended F-2", Single(result, "open").Detail);
    }

    [Fact]
    public void InconsistentPlaces_AreLeftOpenEvenWhenTheDatesAgree()
    {
        var result = Draft(Finding("F-1", "14 APR 1877", "Salem, Essex"), Finding("F-2", "14 APR 1877", "Lynn, Essex"));

        Assert.Empty(Vitals(result));
        Assert.Contains("different values", Single(result, "open").Detail);
    }

    [Fact]
    public void TwoDifferentApproximateDates_AreLeftOpen()
    {
        var result = Draft(Finding("F-1", "ABT 1855", null), Finding("F-2", "BEF 1860", null));

        Assert.Empty(Vitals(result));
        Single(result, "open");
    }

    [Fact]
    public void ANameWithAnExtraGivenName_IsNotReconciledBySpecificity()
    {
        var first = Finding("F-1", null, null, tag: "NAME") with { Facts = [new DraftFact("@I1@", "NAME", null, null, 2, null, null, null, "Barbara /Morrill/")] };
        var second = Finding("F-2", null, null, tag: "NAME") with { Facts = [new DraftFact("@I1@", "NAME", null, null, 2, null, null, null, "Barbara N. /Morrill/")] };

        var result = Draft(first, second);

        Assert.Empty(Vitals(result));
        Assert.Contains("2 different values", Single(result, "open").Detail);
    }

    [Fact]
    public void RepeatableFactsAreNotReconciled()
    {
        var result = Draft(Finding("F-1", "1900", "Salem", tag: "CENS"), Finding("F-2", "1910", "Salem", tag: "CENS"));

        Assert.Equal(2, Vitals(result).Count());
        Assert.DoesNotContain(result.Decisions, d => d.Kind == "resolved");
    }

    // ---- the value the tree already holds ----

    [Fact]
    public void ATreeValueThatSaysMore_IsKeptAndOnlyTheCitationIsAdded()
    {
        var lookup = Holding("14 APR 1877", "Oakland, Kennebec, Maine");

        var result = new ChangesetDrafter(lookup).Draft([Finding("F-1", "1877", "Maine")]);

        Assert.Empty(Vitals(result));
        var citation = Assert.Single(Ops(result), o => o.GetProperty("op").GetString() == "createOrUpdateCitation");
        Assert.Equal("14 APR 1877", citation.GetProperty("match").GetProperty("date").GetString());
        Assert.Equal("Oakland, Kennebec, Maine", citation.GetProperty("match").GetProperty("place").GetString());
        Assert.Contains("no improvement", Single(result, "resolved").Detail);
    }

    [Fact]
    public void ATreeValueThatSaysLess_IsReplacedByTheMoreSpecificFinding()
    {
        var result = new ChangesetDrafter(Holding("1877", "Maine")).Draft([Finding("F-1", "14 APR 1877", "Oakland, Maine")]);

        var vital = Assert.Single(Vitals(result));
        Assert.Equal("14 APR 1877", vital.GetProperty("value").GetProperty("date").GetString());
        Assert.Equal("Oakland, Maine", vital.GetProperty("value").GetProperty("place").GetString());
        Assert.DoesNotContain(result.Decisions, d => d.Kind == "resolved");
    }

    [Fact]
    public void ATreeDateThatSaysMore_IsKeptWhileTheFindingsBetterPlaceIsWritten()
    {
        var result = new ChangesetDrafter(Holding("14 APR 1877", "Maine")).Draft([Finding("F-1", "1877", "Oakland, Maine")]);

        var vital = Assert.Single(Vitals(result));
        Assert.False(vital.GetProperty("value").TryGetProperty("date", out _));
        Assert.Equal("Oakland, Maine", vital.GetProperty("value").GetProperty("place").GetString());
        Assert.Contains("a more specific date", Single(result, "resolved").Detail);
    }

    [Fact]
    public void AFindingThatContradictsTheTree_StillWritesItsValue()
    {
        var result = new ChangesetDrafter(Holding("15 APR 1877", "Maine")).Draft([Finding("F-1", "14 APR 1877", "Maine")]);

        Assert.Equal("14 APR 1877", Assert.Single(Vitals(result)).GetProperty("value").GetProperty("date").GetString());
    }

    [Fact]
    public void AnApproximateFindingNeverReplacesASpecificTreeDate()
    {
        var result = new ChangesetDrafter(Holding("14 APR 1877", "Maine")).Draft([Finding("F-1", "ABT 1877", null)]);

        Assert.Empty(Vitals(result));
        Assert.Single(Ops(result), o => o.GetProperty("op").GetString() == "createOrUpdateCitation");
    }

    [Fact]
    public void ASpecificFindingReplacesAnApproximateTreeDate()
    {
        var result = new ChangesetDrafter(Holding("ABT 1877", "Maine")).Draft([Finding("F-1", "1877", null)]);

        Assert.Equal("1877", Assert.Single(Vitals(result)).GetProperty("value").GetProperty("date").GetString());
    }

    [Fact]
    public void AFindingEqualToTheTree_IsStillAnOrdinaryUpdate()
    {
        var result = new ChangesetDrafter(Holding("14 APR 1877", "Maine")).Draft([Finding("F-1", "14 APR 1877", "Maine")]);

        Assert.Single(Vitals(result));
        Assert.DoesNotContain(result.Decisions, d => d.Kind == "resolved");
    }

    [Fact]
    public void TheCitationsTheTreeAlreadyHolds_AreRestatedOnACitationOnlyOp()
    {
        var lookup = Holding("14 APR 1877", "Maine", new ExistingCitation("@S1@", "Memorial #0", null, 2));

        var result = new ChangesetDrafter(lookup).Draft([Finding("F-1", "1877", null)]);

        var citation = Assert.Single(Ops(result), o => o.GetProperty("op").GetString() == "createOrUpdateCitation");
        Assert.Equal(1, citation.TryGetProperty("citations", out var many) ? many.GetArrayLength() : 1);
    }

    // ---- names ----

    static DraftFinding Named(string id, string name, int quay = 2) =>
        Finding(id, null, null, quay, "primary", "NAME") with
        {
            Facts = [new DraftFact("@I1@", "NAME", null, null, quay, null, null, null, name)],
        };

    [Theory]
    [InlineData("William /Morrill/", "Bill /Morrill/", "William /Morrill/")]
    [InlineData("Bill /Morrill/", "William /Morrill/", "William /Morrill/")]
    [InlineData("William H. /Morrill/", "Bill H. /Morrill/", "William H. /Morrill/")]
    [InlineData("Bill /MORRILL/", "william /Morrill/", "william /Morrill/")]
    public void AFormalName_ReplacesItsKnownNickname(string first, string second, string expected)
    {
        var result = Draft(Named("F-1", first), Named("F-2", second));

        var vital = Assert.Single(Vitals(result));
        Assert.Equal(expected, vital.GetProperty("value").GetString());
        Assert.Equal(2, Citations(vital));
        Assert.Contains("for the name", Single(result, "resolved").Detail);
        Assert.DoesNotContain(result.Decisions, d => d.Kind == "open");
    }

    [Theory]
    [InlineData("William /Morrill/", "Bob /Morrill/")]
    [InlineData("William /Morrill/", "Bill /Smith/")]
    [InlineData("William H. /Morrill/", "Bill /Morrill/")]
    [InlineData("William /Morrill/ Jr", "Bill /Morrill/ Sr")]
    [InlineData("William /Morrill/", "Wilbur /Morrill/")]
    [InlineData("Barbara /Morrill/", "Barbara N. /Morrill/")]
    public void NamesThatAreNotANicknamePair_AreLeftOpen(string first, string second)
    {
        var result = Draft(Named("F-1", first), Named("F-2", second));

        Assert.Empty(Vitals(result));
        Assert.Contains("2 different values", Single(result, "open").Detail);
    }

    [Fact]
    public void TheSameNameFromTwoFindings_IsOneFactWithBothCitations()
    {
        var result = Draft(Named("F-1", "Barbara N. /Morrill/"), Named("F-2", "Barbara N /Morrill/"));

        Assert.Equal(2, Citations(Assert.Single(Vitals(result))));
        Assert.DoesNotContain(result.Decisions, d => d.Kind == "open");
    }

    [Fact]
    public void ANicknameConflictRecommendsTheBetterGradedName()
    {
        var result = Draft(Named("F-1", "William /Morrill/", 1), Named("F-2", "Bob /Morrill/", 2));

        Assert.Contains("recommended F-2 (Bob /Morrill/, QUAY 2)", Single(result, "open").Detail);
    }

    [Fact]
    public void ANameWithoutASlashedSurname_IsOnlyEverTheSameOrDifferent()
    {
        var result = Draft(Named("F-1", "William Morrill"), Named("F-2", "Bill Morrill"));

        Assert.Empty(Vitals(result));
        Single(result, "open");
    }

    [Fact]
    public void AReconciledNameDraft_IsAcceptedByTheApplier()
    {
        var result = Draft(Named("F-1", "William /Test/"), Named("F-2", "Bill /Test/"));

        var applied = DryRun(result);

        Assert.True(applied.Success, string.Join("; ", applied.Errors));
    }

    // ---- accepted by the applier ----

    const string Document = """
        0 HEAD
        1 GEDC
        2 VERS 7.0
        0 @I1@ INDI
        1 NAME Hiram /Test/
        1 SEX M
        1 BIRT
        2 DATE 14 APR 1877
        2 PLAC Oakland, Kennebec, Maine
        0 TRLR
        """;

    static ApplyResult DryRun(DraftResult result) =>
        ChangesetApplier.Run(Encoding.UTF8.GetBytes(Document), Changeset.Parse(result.Changeset!.ToJsonString()), [1], dryRun: true);

    [Fact]
    public void ACitationOnlyResolution_IsAcceptedByTheApplier()
    {
        var result = new ChangesetDrafter(Holding("14 APR 1877", "Oakland, Kennebec, Maine")).Draft([Finding("F-1", "1877", "Maine")]);

        var applied = DryRun(result);

        Assert.True(applied.Success, string.Join("; ", applied.Errors));
    }

    [Fact]
    public void AReconciledSetOfFindings_IsAcceptedByTheApplier()
    {
        var result = new ChangesetDrafter(Holding("14 APR 1877", "Oakland, Kennebec, Maine")).Draft([
            Finding("F-1", "1877", "Maine"), Finding("F-2", "ABT 1877", null), Finding("F-3", "14 APR 1877", "Oakland, Kennebec, Maine")]);

        var applied = DryRun(result);

        Assert.True(applied.Success, string.Join("; ", applied.Errors));
    }

    [Fact]
    public void TheVerificationPolicy_StillAcceptsAReconciledDraft()
    {
        var policy = ChangesetPolicy.Load(new MemoryStream(Encoding.UTF8.GetBytes(
            """{ "verificationNote": { "maxQuay": 1, "notePrefix": "needs independent verification:" } }""")));

        var result = new ChangesetDrafter(new FakeLookup(), policy).Draft([
            Finding("F-1", "1877", "Maine", 1, "secondary"), Finding("F-2", "14 APR 1877", "Oakland, Maine")]);

        Assert.True(result.Success, string.Join("; ", result.Errors));
    }
}
