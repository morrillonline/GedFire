using System.Text;
using System.Text.Json;
using GedCore.Apply;
using GedCore.Drafting;
using GedCore.Ged70;
using GedFire.Gen;
using GedFire.Mcp;

namespace GedCore.Tests;

/// <summary>A relation's commentary, its cites of other findings, and a comment alone on a link the tree holds.</summary>
public class ChangesetDrafterRelationCommentaryTests
{
    const string Document = """
        0 HEAD
        1 GEDC
        2 VERS 7.0
        0 @I1@ INDI
        1 NAME Walter /Pryce/
        1 SEX M
        1 FAMS @F1@
        0 @I2@ INDI
        1 NAME Edith /Pryce/
        1 SEX F
        1 FAMS @F1@
        0 @I3@ INDI
        1 NAME Owen /Pryce/
        1 SEX M
        1 FAMC @F1@
        0 @I4@ INDI
        1 NAME Ruth /Pryce/
        1 SEX F
        0 @F1@ FAM
        1 HUSB @I1@
        1 WIFE @I2@
        1 CHIL @I3@
        0 TRLR
        """;

    static readonly DraftSource Census = new("1950 Census", null, null, "ED 12-4, sheet 7", null, "original", "primary");

    static ChangesetDrafter Drafter() => new(new ModelDraftLookup(ModelBuilder.Build(Ged70Parser.Parse(Document))));

    static DraftFinding Finding(string id, DraftRelation relation, DraftSource? source = null) =>
        new(id, source ?? Census, "Walter Pryce 52, Edith 30, Owen 12", new DraftTarget("@I1@", "family", null), [], [])
        { Relations = [relation] };

    static DraftRelation Spouse(int quay = 2) => new(DraftRelation.Spouse, "@I1@", "@I2@", "@F1@", null, null, null, quay);

    static DraftRelation Child(string child = "@I3@", int quay = 2) => new(DraftRelation.Child, "", child, "@F1@", null, null, null, quay);

    static JsonElement[] Ops(DraftResult result)
    {
        Assert.True(result.Success, string.Join("; ", result.Errors));
        return [.. JsonDocument.Parse(result.Changeset!.ToJsonString()).RootElement
            .GetProperty("items").EnumerateArray().SelectMany(i => i.GetProperty("ops").EnumerateArray())];
    }

    static GedRecord AppliedFamily(DraftResult result)
    {
        var applied = ChangesetApplier.Run(Encoding.UTF8.GetBytes(Document), Changeset.Parse(result.Changeset!.ToJsonString()), [1], dryRun: false);
        Assert.True(applied.Success, string.Join("; ", applied.Errors));
        return Ged70Parser.Read(new MemoryStream(applied.OutputBytes!)).ByXref["@F1@"];
    }

    [Fact]
    public void AChildRelationsCommentary_IsWrittenOnItsFamilyCitation_AndAppliedAsThatCitationsNote()
    {
        const string Commentary = "The death index reads the father as Watkin; the certificate image shows Walter.";

        var result = Drafter().Draft([Finding("F-1", Child() with { Commentary = Commentary })]);

        var op = Assert.Single(Ops(result));
        Assert.Equal("createOrUpdateChild", op.GetProperty("op").GetString());
        Assert.Equal(Commentary, op.GetProperty("familyCitations")[0].GetProperty("commentary").GetString());
        Assert.Equal(Commentary, Assert.Single(AppliedFamily(result).ChildrenByTag("SOUR")).FirstChild("NOTE")!.FullValue());
    }

    [Fact]
    public void ASpouseRelationsCite_AddsTheCitedFindingsCitation_WithItsPageAndCommentary()
    {
        var memorial = new DraftFinding("F-2", new DraftSource("Find a Grave", null, null, "Memorial 77", null, "derivative", "primary"),
            "Edith Pryce, wife of Walter", new DraftTarget("@I2@", "enrich", null), [], []);
        var spouse = Spouse() with { Cites = [new DraftCite("F-2", 1, null, "Her memorial names him as her husband.")] };

        var result = Drafter().Draft([Finding("F-1", spouse)], cited: [memorial]);

        var op = Assert.Single(Ops(result));
        var citations = op.GetProperty("familyCitations").EnumerateArray().ToList();
        Assert.Equal(2, citations.Count);
        Assert.Equal(("Memorial 77", 1, "Her memorial names him as her husband."),
            (citations[1].GetProperty("page").GetString(), citations[1].GetProperty("quay").GetInt32(), citations[1].GetProperty("commentary").GetString()));
        Assert.Equal(["F-1", "F-2"], op.GetProperty("findings").EnumerateArray().Select(f => f.GetString()));
        Assert.Equal(2, AppliedFamily(result).ChildrenByTag("SOUR").Count());
    }

    [Fact]
    public void ACommentaryOnlyRelation_AddsACommentCitingNoSourceToTheFamilyOfTheHeldLink()
    {
        var comment = Spouse(quay: 0) with { CommentaryOnly = true, Commentary = "The census indexes her age as 30; the image reads 50." };

        var result = Drafter().Draft([Finding("F-1", comment)]);

        var op = Assert.Single(Ops(result));
        var citation = Assert.Single(op.GetProperty("familyCitations").EnumerateArray());
        Assert.Equal(DraftReferences.Void, citation.GetProperty("source").GetString());
        Assert.DoesNotContain(result.Decisions, d => d.Kind is DraftDecisionKinds.ReusedSource or DraftDecisionKinds.MintedSource);
        var sour = Assert.Single(AppliedFamily(result).ChildrenByTag("SOUR"));
        Assert.True(sour.IsVoidPointer);
        Assert.Equal("The census indexes her age as 30; the image reads 50.", sour.FirstChild("NOTE")!.FullValue());
    }

    [Theory]
    [InlineData(DraftRelation.Child, "the tree holds no link of @I4@ as a child of @F1@")]
    [InlineData(DraftRelation.Parent, "the tree holds no link of @I1@ as the father of @I4@")]
    [InlineData(DraftRelation.Spouse, "the tree holds no family of @I1@ and @I4@ as partners")]
    public void ACommentaryOnlyRelationOnALinkTheTreeDoesNotHold_IsRefused(string kind, string expected)
    {
        var relation = kind switch
        {
            DraftRelation.Child => Child("@I4@", quay: 0),
            DraftRelation.Parent => new DraftRelation(DraftRelation.Parent, "@I4@", "@I1@", null, "father", null, null, 0),
            _ => new DraftRelation(DraftRelation.Spouse, "@I1@", "@I4@", null, null, null, null, 0),
        };

        var result = Drafter().Draft([Finding("F-1", relation with { CommentaryOnly = true, Commentary = "A comment." })]);

        Assert.Equal($"findings[0].relations[0].commentaryOnly: {expected}; a comment citing no source goes only on a link the tree holds.",
            Assert.Single(result.Errors));
    }

    [Fact]
    public void CommentaryTwoFindingsGiveOneSourceAndPageOfAFamily_IsJoinedOnEveryCopy()
    {
        var spouse = Finding("F-1", Spouse() with { Commentary = "Her age is indexed as 30." });
        var child = Finding("F-2", Child() with { Commentary = "Owen is listed as a stepson." });

        var result = Drafter().Draft([spouse, child]);

        const string Joined = "Her age is indexed as 30. Owen is listed as a stepson.";
        Assert.All(Ops(result), op => Assert.Equal(Joined, op.GetProperty("familyCitations")[0].GetProperty("commentary").GetString()));
        Assert.Equal(Joined, Assert.Single(AppliedFamily(result).ChildrenByTag("SOUR")).FirstChild("NOTE")!.FullValue());
    }
}
