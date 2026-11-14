using System.Text;
using System.Text.Json;
using GedCore.Apply;
using GedCore.Drafting;

namespace GedCore.Tests;

public class ChangesetDrafterRelationTests
{
    sealed class FakeLookup(Dictionary<string, string>? names = null, Dictionary<string, string[]>? couples = null) : IDraftLookup
    {
        public IReadOnlyList<ExistingFact> FactsOn(string record, string tag) => [];
        public IReadOnlyList<string> NotesOn(string record) => [];
        public string? FindSource(string title, string? author) => null;
        public string? PersonName(string xref) => names?.GetValueOrDefault(xref);
        public IReadOnlyList<string> FamiliesOfCouple(string partner, string otherPartner) =>
            couples?.GetValueOrDefault($"{partner}+{otherPartner}") ?? couples?.GetValueOrDefault($"{otherPartner}+{partner}") ?? [];
    }

    const string Document = """
        0 HEAD
        1 GEDC
        2 VERS 7.0
        0 @I1@ INDI
        1 NAME Hiram /Test/
        1 SEX M
        0 TRLR
        """;

    static readonly DraftSource Census = new("1850 Census", null, null, "p. 4, line 9", null, "original", "primary");

    static DraftPerson Person(string xref, string name, string? sex = null) => new(xref, name, sex);

    static DraftRelation Spouse(string person, string spouse, string family, int quay = 2) =>
        new(DraftRelation.Spouse, person, spouse, family, null, null, null, quay);

    static DraftRelation Child(string family, string child, string? husb = null, string? wife = null, int quay = 2) =>
        new(DraftRelation.Child, "", child, family, null, husb, wife, quay);

    static DraftRelation Parent(string person, string role, string parent, string? family = null, int quay = 2) =>
        new(DraftRelation.Parent, person, parent, family, role, null, null, quay);

    static DraftFact Fact(string record, string tag, string? date, string? place, int quay = 2) =>
        new(record, tag, date, place, quay, null, null, null);

    static DraftFinding Finding(
        IReadOnlyList<DraftPerson> people, IReadOnlyList<DraftRelation> relations, IReadOnlyList<DraftFact>? facts = null,
        string id = "F-1") =>
        new(id, Census, "Hiram Test 40, Mary 35, Ann 8", new DraftTarget("@I1@", "new-spouse", null), facts ?? [], [])
        { People = people, Relations = relations };

    static JsonElement[] Ops(DraftResult result) =>
        [.. JsonDocument.Parse(result.Changeset!.ToJsonString()).RootElement
            .GetProperty("items").EnumerateArray().SelectMany(i => i.GetProperty("ops").EnumerateArray())];

    static string Verb(JsonElement op) => op.GetProperty("op").GetString()!;

    static ApplyResult DryRun(DraftResult result) =>
        ChangesetApplier.Run(Encoding.UTF8.GetBytes(Document), Changeset.Parse(result.Changeset!.ToJsonString()), [1], dryRun: true);

    [Fact]
    public void ANewWifeAndChildren_AreCompiledToOpsTheApplierAccepts()
    {
        var finding = Finding(
            [Person("@NewI1@", "Mary /Test/", "F"), Person("@NewI2@", "Ann /Test/"), Person("@NewI3@", "John /Test/")],
            [Spouse("@I1@", "@NewI1@", "@NewF1@"),
             Child("@NewF1@", "@NewI2@", husb: "@I1@", wife: "@NewI1@"),
             Child("@NewF1@", "@NewI3@", husb: "@I1@", wife: "@NewI1@")],
            [Fact("@NewF1@", "MARR", "1842", null), Fact("@NewI2@", "BIRT", "ABT 1842", "Maine", quay: 2)]);

        var result = new ChangesetDrafter(new FakeLookup()).Draft([finding]);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        var applied = DryRun(result);
        Assert.True(applied.Success, string.Join("; ", applied.Errors));
    }

    [Fact]
    public void ASexCorrection_PrecedesRelationshipsThatNeedTheCorrectedValue()
    {
        var finding = Finding(
            [Person("@NewI1@", "Mary /Test/", "F"), Person("@NewI2@", "Ann /Test/")],
            [Spouse("@I1@", "@NewI1@", "@NewF1@"),
             Child("@NewF1@", "@NewI2@", husb: "@I1@", wife: "@NewI1@")],
            [Fact("@I1@", "SEX", null, null) with { Value = "M" }]);

        var result = new ChangesetDrafter(new FakeLookup()).Draft([finding]);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        var applied = ChangesetApplier.Run(
            Encoding.UTF8.GetBytes(Document.Replace("1 SEX M", "1 SEX F")),
            Changeset.Parse(result.Changeset!.ToJsonString()), [1], dryRun: true);
        Assert.True(applied.Success, string.Join("; ", applied.Errors));
        Assert.Equal("SEX", Ops(result)[0].GetProperty("fact").GetString());
    }

    [Fact]
    public void ARelation_IsCitedWithTheFindingsSourceAtItsOwnQuay()
    {
        var result = new ChangesetDrafter(new FakeLookup()).Draft(
            [Finding([Person("@NewI1@", "Mary /Test/", "F")], [Spouse("@I1@", "@NewI1@", "@NewF1@", quay: 1)])]);

        var citation = Ops(result)[0].GetProperty("citation");
        Assert.Equal("@NewSource1@", citation.GetProperty("source").GetString());
        Assert.Equal("p. 4, line 9", citation.GetProperty("page").GetString());
        Assert.Equal(1, citation.GetProperty("quay").GetInt32());
    }

    [Fact]
    public void ARelationExcerpt_NarrowsTheCitationsDataText()
    {
        var finding = Finding([Person("@NewI1@", "Mary /Test/", "F")], [Spouse("@I1@", "@NewI1@", "@NewF1@") with { Excerpt = "Mary 35" }]);

        var citation = Ops(new ChangesetDrafter(new FakeLookup()).Draft([finding]))[0].GetProperty("citation");

        Assert.Equal("Mary 35", citation.GetProperty("dataText").GetString());
    }

    [Fact]
    public void ARelationAndAFactSharingAnExcerpt_ProduceNoMergedOverQuote()
    {
        var finding = Finding(
            [Person("@NewI1@", "Mary /Test/", "F")],
            [Spouse("@I1@", "@NewI1@", "@NewF1@") with { Excerpt = "Mary 35" }],
            [Fact("@NewI1@", "BIRT", "1807", "Maine") with { Excerpt = "Mary 35" }]);

        var ops = Ops(new ChangesetDrafter(new FakeLookup()).Draft([finding]));

        Assert.All(ops, o => Assert.Equal("Mary 35", o.GetProperty("citation").GetProperty("dataText").GetString()));
    }

    [Fact]
    public void TwoFindingsStatingTheSameLink_ShareOneOpCitingBoth()
    {
        var first = Finding([Person("@NewI1@", "Mary /Test/", "F")], [Spouse("@I1@", "@NewI1@", "@NewF1@")]);
        var second = Finding([Person("@NewI1@", "Mary /Test/", "F")], [Spouse("@I1@", "@NewI1@", "@NewF1@", quay: 1)], id: "F-2") with
        {
            Source = new DraftSource("1860 Census", null, null, "p. 9", null, "original", "primary"),
        };

        var ops = Ops(new ChangesetDrafter(new FakeLookup()).Draft([first, second]));

        var op = Assert.Single(ops, o => Verb(o) == "createOrUpdateSpouse");
        Assert.Equal(2, op.GetProperty("citations").GetArrayLength());
    }

    [Fact]
    public void ANewPerson_IsDescribedInlineWhereTheRelationCreatesThem()
    {
        var result = new ChangesetDrafter(new FakeLookup()).Draft([Finding(
            [Person("@NewI1@", "Mary /Test/", "F"), Person("@NewI2@", "Ann /Test/")],
            [Spouse("@I1@", "@NewI1@", "@NewF1@"), Child("@NewF1@", "@NewI2@", husb: "@I1@", wife: "@NewI1@")])]);

        var ops = Ops(result);
        Assert.Equal("Mary /Test/", ops[0].GetProperty("spouse").GetProperty("name").GetString());
        Assert.Equal("F", ops[0].GetProperty("spouse").GetProperty("sex").GetString());
        Assert.Equal("Ann /Test/", ops[1].GetProperty("child").GetProperty("name").GetString());
        Assert.False(ops[1].GetProperty("child").TryGetProperty("sex", out _));
        Assert.False(ops[1].TryGetProperty("wife", out _));
    }

    [Fact]
    public void TheFamilySeed_IsGivenOnlyToTheOpThatCreatesTheFamily()
    {
        var result = new ChangesetDrafter(new FakeLookup()).Draft([Finding(
            [Person("@NewI1@", "Ann /Test/"), Person("@NewI2@", "John /Test/")],
            [Child("@NewF1@", "@NewI1@", husb: "@I1@"), Child("@NewF1@", "@NewI2@", husb: "@I1@")])]);

        var ops = Ops(result);
        Assert.Equal("@I1@", ops[0].GetProperty("husb").GetString());
        Assert.False(ops[1].TryGetProperty("husb", out _));
        Assert.Single(result.Decisions, d => d.Kind == DraftDecisionKinds.DroppedSeed);
        Assert.True(DryRun(result).Success);
    }

    [Fact]
    public void ANewParent_IsCompiledWithItsRole()
    {
        var result = new ChangesetDrafter(new FakeLookup()).Draft([Finding(
            [Person("@NewI1@", "Obed /Test/", "M")], [Parent("@I1@", "father", "@NewI1@", family: "@NewF1@")])]);

        var op = Ops(result)[0];
        Assert.Equal("createOrUpdateParent", Verb(op));
        Assert.Equal("father", op.GetProperty("role").GetString());
        Assert.Equal("@NewF1@", op.GetProperty("family").GetString());
        Assert.True(DryRun(result).Success);
    }

    [Fact]
    public void RelationOps_PrecedeTheFactsAboutThePeopleTheyCreate()
    {
        var result = new ChangesetDrafter(new FakeLookup()).Draft([Finding(
            [Person("@NewI1@", "Mary /Test/", "F")], [Spouse("@I1@", "@NewI1@", "@NewF1@")],
            [Fact("@NewI1@", "BIRT", "1807", "Maine")])]);

        Assert.Equal(["createOrUpdateSpouse", "createOrUpdateVital"], Ops(result).Select(Verb));
    }

    [Fact]
    public void ASexFactOnANewPerson_FollowsTheRelationThatCreatesThem()
    {
        var result = new ChangesetDrafter(new FakeLookup()).Draft([Finding(
            [Person("@NewI1@", "Mary /Test/", "F")], [Spouse("@I1@", "@NewI1@", "@NewF1@")],
            [Fact("@NewI1@", "SEX", null, null) with { Value = "F" }])]);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Equal(["createOrUpdateSpouse", "createOrUpdateVital"], Ops(result).Select(Verb));
        var applied = DryRun(result);
        Assert.True(applied.Success, string.Join("; ", applied.Errors));
    }

    [Fact]
    public void ALowGradedFactOnANewPerson_GetsAVerificationNoteNamingThem()
    {
        var result = new ChangesetDrafter(new FakeLookup()).Draft([Finding(
            [Person("@NewI1@", "Mary /Test/", "F")], [Spouse("@I1@", "@NewI1@", "@NewF1@", quay: 1)],
            [Fact("@NewI1@", "BIRT", "1807", "Maine", quay: 1)])]);

        var note = Ops(result).Single(o => Verb(o) == "createOrUpdateNote");
        Assert.Equal("@NewI1@", note.GetProperty("record").GetString());
        Assert.Contains("Mary Test", note.GetProperty("text").GetString());
    }

    [Fact]
    public void ANewPersonIsReportedAsADecision() =>
        Assert.Single(
            new ChangesetDrafter(new FakeLookup()).Draft([Finding(
                [Person("@NewI1@", "Mary /Test/", "F")], [Spouse("@I1@", "@NewI1@", "@NewF1@")])]).Decisions,
            d => d.Kind == DraftDecisionKinds.MintedPerson && d.Detail.Contains("Mary /Test/"));

    [Fact]
    public void ARelationQuayAboveTheSourcesCeiling_IsRefused()
    {
        var derivative = Finding([Person("@NewI1@", "Mary /Test/", "F")], [Spouse("@I1@", "@NewI1@", "@NewF1@", quay: 3)]) with
        {
            Source = Census with { Class = "derivative", Information = "secondary" },
        };

        var result = new ChangesetDrafter(new FakeLookup()).Draft([derivative]);

        Assert.Contains("findings[0].relations[0].quay is 3", Assert.Single(result.Errors));
    }

    [Fact]
    public void APlaceholderNobodyDeclared_IsRefused()
    {
        var result = new ChangesetDrafter(new FakeLookup()).Draft([Finding([], [Spouse("@I1@", "@NewI9@", "@NewF1@")])]);

        Assert.Contains("@NewI9@, which is not declared", Assert.Single(result.Errors));
    }

    [Fact]
    public void ADeclaredPersonNoRelationLinks_IsRefused()
    {
        var result = new ChangesetDrafter(new FakeLookup()).Draft(
            [Finding([Person("@NewI1@", "Mary /Test/", "F")], [], [Fact("@I1@", "DEAT", "1900", null)])]);

        Assert.Contains("@NewI1@ is declared in people but no relation links it", Assert.Single(result.Errors));
    }

    [Fact]
    public void TwoDeclarationsOfOnePlaceholderThatDisagree_AreRefused()
    {
        var result = new ChangesetDrafter(new FakeLookup()).Draft([
            Finding([Person("@NewI1@", "Mary /Test/", "F")], [Spouse("@I1@", "@NewI1@", "@NewF1@")]),
            Finding([Person("@NewI1@", "Marie /Test/", "F")], [Spouse("@I1@", "@NewI1@", "@NewF1@")], id: "F-2")]);

        Assert.Contains(result.Errors, e => e.Contains("declared with a different name or sex"));
    }

    [Fact]
    public void ARealXrefInPeople_IsRefused()
    {
        var result = new ChangesetDrafter(new FakeLookup()).Draft(
            [Finding([Person("@I9@", "Mary /Test/", "F")], [Spouse("@I1@", "@I9@", "@NewF1@")])]);

        Assert.Contains(result.Errors, e => e.Contains("must be a new-record placeholder"));
    }

    [Fact]
    public void ANewPartnerWithoutAFamily_IsRefused()
    {
        var spouse = new DraftRelation(DraftRelation.Spouse, "@I1@", "@NewI1@", null, null, null, null, 2);

        var result = new ChangesetDrafter(new FakeLookup()).Draft([Finding([Person("@NewI1@", "Mary /Test/", "F")], [spouse])]);

        Assert.Contains("relations[0].family is required", Assert.Single(result.Errors));
    }

    [Fact]
    public void AFactOnAnUndeclaredPlaceholderRecord_IsRefused()
    {
        var result = new ChangesetDrafter(new FakeLookup()).Draft(
            [Finding([], [], [Fact("@NewF7@", "MARR", "1842", null)])]);

        Assert.Contains("facts[0].record @NewF7@", Assert.Single(result.Errors));
    }

    // ---- partners who already share a family ----

    const string MarriedDocument = """
        0 HEAD
        1 GEDC
        2 VERS 7.0
        0 @I1@ INDI
        1 NAME Hiram /Test/
        1 SEX M
        1 FAMS @F1@
        0 @I2@ INDI
        1 NAME Mary /Test/
        1 SEX F
        1 FAMS @F1@
        0 @F1@ FAM
        1 HUSB @I1@
        1 WIFE @I2@
        0 @F2@ FAM
        0 TRLR
        """;

    static ChangesetDrafter Married(params string[] families) => new(new FakeLookup(couples: new() { ["@I1@+@I2@"] = families }));

    [Fact]
    public void ASpouseRelationBetweenExistingPartners_CompilesOntoTheirFamily_AndCreatesNone()
    {
        var finding = Finding([], [Spouse("@I1@", "@I2@", "@NewF1@")], [Fact("@NewF1@", "MARR", "1842", null)]);

        var result = Married("@F1@").Draft([finding]);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        var ops = Ops(result);
        Assert.Equal("@F1@", ops.Single(o => Verb(o) == "createOrUpdateSpouse").GetProperty("family").GetString());
        Assert.Equal("@F1@", ops.Single(o => Verb(o) == "createOrUpdateVital").GetProperty("record").GetString());
        Assert.DoesNotContain("@NewF1@", result.Changeset!.ToJsonString());
        Assert.Contains(result.Decisions, d => d.Kind == "resolved" && d.Detail == "@I1@ and @I2@ are already partners in @F1@, so relations[0].family compiles onto it instead of creating @NewF1@");
        var applied = ChangesetApplier.Run(Encoding.UTF8.GetBytes(MarriedDocument), Changeset.Parse(result.Changeset!.ToJsonString()), [1], dryRun: false);
        Assert.True(applied.Success, string.Join("; ", applied.Errors));
        Assert.Equal(2, GedReader.Read(applied.OutputBytes!).Records.Count(r => r.Tag == "FAM"));
    }

    [Fact]
    public void ASpouseRelationNamingAnotherExistingFamily_IsRefusedWithTheSharedOne()
    {
        var result = Married("@F1@").Draft([Finding([], [Spouse("@I1@", "@I2@", "@F2@")])]);

        Assert.Equal("findings[0].relations[0].family @F2@ is not the family of @I1@ and @I2@, who are already partners in @F1@; name that family or leave family out.",
            Assert.Single(result.Errors));
    }

    [Fact]
    public void ASpouseRelationForPartnersInSeveralFamilies_IsRefusedListingThem()
    {
        var result = Married("@F1@", "@F3@").Draft([Finding([], [Spouse("@I1@", "@I2@", "@NewF1@")])]);

        Assert.Equal("findings[0].relations[0]: @I1@ and @I2@ are already partners in 2 families (@F1@, @F3@); give the family this relation belongs to.",
            Assert.Single(result.Errors));
    }
}