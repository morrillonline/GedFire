using System.Text;
using System.Text.Json;
using GedCore.Apply;
using GedCore.Drafting;

namespace GedCore.Tests;

public class ChangesetDrafterMoveTests
{
    sealed class FamilyLookup : IDraftLookup
    {
        public IReadOnlyList<ExistingFact> FactsOn(string record, string tag) => [];
        public IReadOnlyList<string> NotesOn(string record) => [];
        public string? FindSource(string title, string? author) => null;
        public string? PersonName(string xref) => null;

        public bool? IsChildOf(string child, string family) => (child, family) switch
        {
            ("@I4@", "@F1@") => true,
            ("@I4@", "@F2@") => false,
            _ => null,
        };
    }

    // George (@I1@) with Josephine (@I2@) in @F1@, which lists his son (@I4@); Helen (@I3@) is the son's real mother.
    const string Document = """
        0 HEAD
        1 GEDC
        2 VERS 7.0
        0 @I1@ INDI
        1 NAME George /Test/
        1 SEX M
        1 FAMS @F1@
        0 @I2@ INDI
        1 NAME Josephine /Other/
        1 SEX F
        1 FAMS @F1@
        0 @I3@ INDI
        1 NAME Helen /Isham/
        1 SEX F
        0 @I4@ INDI
        1 NAME George /Test/
        1 SEX M
        1 FAMC @F1@
        0 @F1@ FAM
        1 HUSB @I1@
        1 WIFE @I2@
        1 CHIL @I4@
        0 TRLR
        """;

    static readonly DraftSource MarriageRecord = new("Marriage record", null, null, "p. 2", null, "original", "primary");

    static DraftFinding Finding(params DraftRelation[] relations) =>
        new("F-1", MarriageRecord, "Son of George and Helen", new DraftTarget("@I4@", null, null), [], [])
        {
            Relations = relations,
            People = [],
        };

    static DraftRelation Move(string to, string from, string? husb = "@I1@", string? wife = "@I3@") =>
        new(DraftRelation.Child, "", "@I4@", to, null, husb, wife, 2) { From = from };

    static DraftRelation UnlinkChild(string family) =>
        new(DraftRelation.UnlinkChild, "", "@I4@", family, null, null, null, 0);

    static DraftRelation UnlinkParent(string role, string? family = null) =>
        new(DraftRelation.UnlinkParent, "@I4@", "", family, role, null, null, 0);

    static JsonElement[] Ops(DraftResult result) =>
        [.. JsonDocument.Parse(result.Changeset!.ToJsonString()).RootElement
            .GetProperty("items").EnumerateArray().SelectMany(i => i.GetProperty("ops").EnumerateArray())];

    static string Verb(JsonElement op) => op.GetProperty("op").GetString()!;

    static ApplyResult Apply(DraftResult result) =>
        ChangesetApplier.Run(Encoding.UTF8.GetBytes(Document), Changeset.Parse(result.Changeset!.ToJsonString()), [1], dryRun: false);

    static string Applied(ApplyResult result) => Encoding.UTF8.GetString(result.OutputBytes!);

    // The lines of one level-0 record of a GEDCOM text.
    static string Record(string ged, string xref) =>
        ged.Replace("\r\n", "\n").Split("\n0 ").Single(r => r.StartsWith(xref + " ") || r.StartsWith("0 " + xref + " "));

    [Fact]
    public void AMove_JoinsTheNewFamilyBeforeLeavingTheOldOne()
    {
        var result = new ChangesetDrafter(new FamilyLookup()).Draft([Finding(Move("@NewF1@", "@F1@"))]);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        var ops = Ops(result);
        Assert.Equal(["createOrUpdateChild", "deleteChild"], ops.Select(Verb));
        Assert.Equal("@NewF1@", ops[0].GetProperty("family").GetString());
        Assert.Equal("@F1@", ops[1].GetProperty("family").GetString());
        Assert.Equal("@I4@", ops[1].GetProperty("child").GetString());
    }

    [Fact]
    public void AMove_IsCitedOnTheFamilyTheChildJoins()
    {
        var result = new ChangesetDrafter(new FamilyLookup()).Draft([Finding(Move("@NewF1@", "@F1@"))]);

        var citation = Ops(result)[0].GetProperty("citation");
        Assert.Equal("p. 2", citation.GetProperty("page").GetString());
        Assert.Equal(2, citation.GetProperty("quay").GetInt32());
    }

    [Fact]
    public void AMove_LeavesTheChildInTheNewFamilyOnly()
    {
        var result = new ChangesetDrafter(new FamilyLookup()).Draft([Finding(Move("@NewF1@", "@F1@"))]);

        var applied = Apply(result);

        Assert.True(applied.Success, string.Join("; ", applied.Errors));
        Assert.DoesNotContain("CHIL @I4@", Record(Applied(applied), "@F1@"));
        Assert.DoesNotContain("FAMC @F1@", Record(Applied(applied), "@I4@"));
        Assert.Contains("FAMC @", Record(Applied(applied), "@I4@"));
    }

    [Fact]
    public void AMoveIntoAnExistingFamily_NeedsNoSeed()
    {
        var result = new ChangesetDrafter(new FamilyLookup()).Draft([Finding(Move("@F2@", "@F1@", husb: null, wife: null))]);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Equal("@F2@", Ops(result)[0].GetProperty("family").GetString());
    }

    [Fact]
    public void AMoveFromAFamilyThatDoesNotListTheChild_IsRefused()
    {
        var result = new ChangesetDrafter(new FamilyLookup()).Draft([Finding(Move("@NewF1@", "@F2@"))]);

        Assert.Contains("findings[0].relations[0].from: @I4@ is not a child of @F2@", Assert.Single(result.Errors));
    }

    [Fact]
    public void AMoveIntoTheSameFamily_IsRefused()
    {
        var result = new ChangesetDrafter(new FamilyLookup()).Draft([Finding(Move("@F1@", "@F1@", husb: null, wife: null))]);

        Assert.Contains("a move needs two different families", Assert.Single(result.Errors));
    }

    [Fact]
    public void AMoveFromAFamilyTheDraftCreates_IsRefused()
    {
        var result = new ChangesetDrafter(new FamilyLookup()).Draft([Finding(Move("@F2@", "@NewF9@", husb: null, wife: null))]);

        Assert.Contains("is a family this draft creates", Assert.Single(result.Errors));
    }

    [Fact]
    public void AMoveOfAChildTheLookupCannotJudge_IsNotRefused()
    {
        var unknown = new DraftRelation(DraftRelation.Child, "", "@I9@", "@NewF1@", null, null, null, 2) { From = "@F7@" };

        var result = new ChangesetDrafter(new FamilyLookup()).Draft([Finding(unknown)]);

        Assert.True(result.Success, string.Join("; ", result.Errors));
    }

    [Fact]
    public void UnlinkChild_IsCompiledToADeleteWithAnUncitedDecision()
    {
        var result = new ChangesetDrafter(new FamilyLookup()).Draft([Finding(UnlinkChild("@F1@"))]);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        var op = Assert.Single(Ops(result));
        Assert.Equal("deleteChild", Verb(op));
        Assert.False(op.TryGetProperty("citation", out _));
        Assert.Contains(result.Decisions, d => d.Kind == DraftDecisionKinds.Open && d.Detail.Contains("removing @I4@ from @F1@ is not cited"));
        Assert.True(Apply(result).Success);
    }

    [Fact]
    public void UnlinkParent_IsCompiledToADeleteParent()
    {
        var result = new ChangesetDrafter(new FamilyLookup()).Draft([Finding(UnlinkParent("mother"))]);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        var op = Assert.Single(Ops(result));
        Assert.Equal("deleteParent", Verb(op));
        Assert.Equal("@I4@", op.GetProperty("person").GetString());
        Assert.Equal("mother", op.GetProperty("role").GetString());
        Assert.False(op.TryGetProperty("family", out _));
        var applied = Apply(result);
        Assert.True(applied.Success, string.Join("; ", applied.Errors));
        Assert.DoesNotContain("WIFE @I2@", Record(Applied(applied), "@F1@"));
    }

    [Fact]
    public void UnlinkParent_NamesTheFamilyWhenGiven()
    {
        var result = new ChangesetDrafter(new FamilyLookup()).Draft([Finding(UnlinkParent("father", "@F1@"))]);

        Assert.Equal("@F1@", Ops(result)[0].GetProperty("family").GetString());
    }

    [Fact]
    public void UnlinkChildFromAFamilyThatDoesNotListTheChild_IsRefused()
    {
        var result = new ChangesetDrafter(new FamilyLookup()).Draft([Finding(UnlinkChild("@F2@"))]);

        Assert.Contains("findings[0].relations[0].family: @I4@ is not a child of @F2@", Assert.Single(result.Errors));
    }

    [Fact]
    public void TheSameRemovalStatedTwice_IsOneOp()
    {
        var result = new ChangesetDrafter(new FamilyLookup()).Draft([Finding(UnlinkChild("@F1@"), UnlinkChild("@F1@"))]);

        Assert.Single(Ops(result));
    }

    [Fact]
    public void ARemovalOnlyFinding_IsNotTakenForAnEmptyOne()
    {
        var result = new ChangesetDrafter(new FamilyLookup()).Draft([Finding(UnlinkChild("@F1@"))]);

        Assert.DoesNotContain(result.Errors, e => e.Contains("has no facts, relations or notes"));
    }
}
