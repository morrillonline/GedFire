using System.Text.Json;
using GedCore.Validate;
using GedFire.Gen;
using GedFire.Match;
using GedFire.Mcp;
using GedFire.TargetSelection;

namespace GedCore.Tests;

public class ResearchBriefBuilderTests
{
    // John Ashworth @I1@: parents and a sibling in @F3@, a married family @F1@
    // with two children, a second family @F2@ with no recorded spouse and one
    // child, a note of his own and one on @F1@, a missing death (a gap), and
    // an identical twin @I20@ (a guaranteed GEN301 duplicate pair).
    const string Ged = """
        0 @I1@ INDI
        1 NAME John /Ashworth/
        1 SEX M
        1 BIRT
        2 DATE 1848
        2 PLAC Boone County, Iowa
        1 CENS
        2 DATE 1880
        2 PLAC Boone County, Iowa
        1 NOTE Check the 1885 state census next.
        1 FAMC @F3@
        1 FAMS @F1@
        1 FAMS @F2@
        0 @I2@ INDI
        1 NAME Mary /Ashworth/
        1 SEX F
        1 BIRT
        2 DATE 1850
        1 FAMS @F1@
        0 @I3@ INDI
        1 NAME Thomas /Ashworth/
        1 SEX M
        1 BIRT
        2 DATE 1872
        1 FAMC @F1@
        0 @I4@ INDI
        1 NAME Anna /Ashworth/
        1 SEX F
        1 FAMC @F1@
        0 @I5@ INDI
        1 NAME Samuel /Ashworth/
        1 SEX M
        1 FAMC @F2@
        0 @I6@ INDI
        1 NAME Robert /Ashworth/
        1 SEX M
        1 DEAT
        2 DATE 1860
        1 FAMS @F3@
        0 @I7@ INDI
        1 NAME Jane /Ashworth/
        1 SEX F
        1 FAMS @F3@
        0 @I8@ INDI
        1 NAME Peter /Ashworth/
        1 SEX M
        1 FAMC @F3@
        0 @I20@ INDI
        1 NAME John /Ashworth/
        1 SEX M
        1 BIRT
        2 DATE 1848
        2 PLAC Boone County, Iowa
        0 @F1@ FAM
        1 HUSB @I1@
        1 WIFE @I2@
        1 CHIL @I3@
        1 CHIL @I4@
        1 MARR
        2 DATE 1870
        2 PLAC Boone County, Iowa
        1 NOTE The marriage record names a witness worth tracing.
        0 @F2@ FAM
        1 HUSB @I1@
        1 CHIL @I5@
        0 @F3@ FAM
        1 HUSB @I6@
        1 WIFE @I7@
        1 CHIL @I1@
        1 CHIL @I8@
        """;

    static DocumentSnapshot Snapshot(string ged = Ged)
    {
        var model = MatchTestModels.Build(ged);
        return new DocumentSnapshot(model, "5.5.1", DateTime.UtcNow, 0);
    }

    static ResearchBriefBuilder Builder() => new("media");

    static ResearchBrief Build(
        DocumentSnapshot snapshot, string xref = "@I1@", string? cardType = null,
        IReadOnlyList<GedDiagnostic>? findings = null) =>
        Builder().Build(snapshot, snapshot.Model.Individuals[xref], cardType, findings ?? []);

    [Fact]
    public void Person_IsTheFullGetRecordDetail()
    {
        var brief = Build(Snapshot());

        Assert.Equal("@I1@", brief.Xref);
        Assert.Null(brief.CardType);
        Assert.Equal("John Ashworth", brief.Person.Name);
        Assert.Equal("1848", brief.Person.Birth!.Date);
        Assert.Equal("Check the 1885 state census next.", Assert.Single(brief.Person.Notes).Text);
    }

    [Fact]
    public void Kin_SummarizesParentsSiblingsSpousesAndChildrenToOneLineEach()
    {
        var kin = Build(Snapshot()).Kin;

        Assert.Equal(["@I6@", "@I7@"], kin.Parents.Select(p => p.Xref));
        Assert.Equal("Robert Ashworth", kin.Parents[0].Name);
        Assert.Equal("1860", kin.Parents[0].Died);
        Assert.Equal(["@I8@"], kin.Siblings.Select(s => s.Xref));

        Assert.Equal(2, kin.Spouses.Count);
        var married = kin.Spouses.Single(s => s.FamilyXref == "@F1@");
        Assert.Equal("@I2@", married.Spouse!.Xref);
        Assert.Equal("Mary Ashworth", married.Spouse.Name);
        Assert.Equal("1870", married.MarriageDate);
        Assert.Equal("Boone County, Iowa", married.MarriagePlace);
        var unknownSpouse = kin.Spouses.Single(s => s.FamilyXref == "@F2@");
        Assert.Null(unknownSpouse.Spouse);

        Assert.Equal(["@I3@", "@I4@", "@I5@"], kin.Children.Select(c => c.Xref));
        Assert.Equal("1872", kin.Children[0].Born);
    }

    [Fact]
    public void Gaps_AgreeExactlyWithWhatSelectTargetsWouldReport()
    {
        var snapshot = Snapshot();
        var brief = Build(snapshot);
        var expected = GapDetector.DetectForPeople([snapshot.Model.Individuals["@I1@"]]);

        Assert.Equal(JsonSerializer.Serialize(expected), JsonSerializer.Serialize(brief.Gaps));
        Assert.NotEmpty(brief.Gaps);
        Assert.All(brief.Gaps, t => Assert.Equal("@I1@", t.Xref));
    }

    [Fact]
    public void Duplicates_ListTheTwinWithScore_AsSelectTargetsComputesThem()
    {
        var brief = Build(Snapshot());

        var entry = Assert.Single(brief.PossibleDuplicateOf);
        Assert.Equal("@I20@", entry.Xref);
        Assert.True(entry.Score >= 70);
        Assert.Equal(1, brief.PossibleDuplicateCount);
    }

    [Fact]
    public void Plausibility_KeepsOnlyFindingsAnchoredOnThePersonOrTheirFamilies()
    {
        var findings = new GedDiagnostic[]
        {
            new(GedDiagnosticSeverity.Warning, "GEN101", "On the person.", "@I1@", "DEAT"),
            new(GedDiagnosticSeverity.Warning, "GEN103", "On the married family.", "@F1@", "MARR"),
            new(GedDiagnosticSeverity.Warning, "GEN102", "On the parent family.", "@F3@", "FAM"),
            new(GedDiagnosticSeverity.Warning, "GEN101", "On an unrelated person.", "@I99@", "BIRT"),
            new(GedDiagnosticSeverity.Warning, "GEN301", "A duplicate pair, covered elsewhere.", "@I1@", "INDI"),
        };

        var brief = Build(Snapshot(), findings: findings);

        Assert.Equal(["GEN101", "GEN103", "GEN102"], brief.Plausibility.Select(f => f.Code));
        Assert.Equal("Warning", brief.Plausibility[0].Severity);
        Assert.Equal("On the person.", brief.Plausibility[0].Message);
    }

    [Fact]
    public void Notes_CarryThePersonsTextsAndTheirFamiliesTexts()
    {
        var notes = Build(Snapshot()).Notes;

        Assert.Equal(["Check the 1885 state census next."], notes.Person);
        var family = Assert.Single(notes.Families);
        Assert.Equal("@F1@", family.Xref);
        Assert.Equal(["The marriage record names a witness worth tracing."], family.Texts);
    }

    [Fact]
    public void NewSpouseEmphasis_ListsChildrenWhoseOtherParentIsUnknown()
    {
        var brief = Build(Snapshot(), cardType: "new-spouse");

        Assert.Equal("new-spouse", brief.CardType);
        var child = Assert.Single(brief.Emphasis!.ChildrenWithUnknownOtherParent!);
        Assert.Equal("@I5@", child.Xref);
        Assert.Null(brief.Emphasis.BirthBoundingFacts);
        Assert.Null(brief.Emphasis.DeathBoundingFacts);
    }

    [Fact]
    public void EnrichDeathEmphasis_ListsTheDatedFactsThatBoundTheDeath()
    {
        var brief = Build(Snapshot(), cardType: "enrich-death");

        var census = Assert.Single(brief.Emphasis!.DeathBoundingFacts!);
        Assert.Equal(("CENS", "1880", "Boone County, Iowa"), (census.Tag, census.Date, census.Place));
        Assert.Null(brief.Emphasis.BirthBoundingFacts);
    }

    [Fact]
    public void EnrichPersonEmphasis_BoundsBothVitals()
    {
        var brief = Build(Snapshot(), cardType: "enrich-person");

        Assert.NotNull(brief.Emphasis!.BirthBoundingFacts);
        Assert.NotNull(brief.Emphasis.DeathBoundingFacts);
        // Birth is bounded by the census and the person's own marriage.
        Assert.Equal(["MARR", "CENS"], brief.Emphasis.BirthBoundingFacts!.Select(f => f.Tag));
    }

    [Fact]
    public void NoCardType_MeansNoEmphasis()
    {
        Assert.Null(Build(Snapshot()).Emphasis);
    }

    [Fact]
    public void PersonWithNoKinNoNotesAndNoDuplicates_ReturnsEmptyArraysNotErrors()
    {
        const string lone = """
            0 @I1@ INDI
            1 NAME Zebulon /Moxley/
            1 SEX M
            1 BIRT
            2 DATE 1801
            """;
        var brief = Build(Snapshot(lone));

        Assert.Empty(brief.Kin.Parents);
        Assert.Empty(brief.Kin.Siblings);
        Assert.Empty(brief.Kin.Spouses);
        Assert.Empty(brief.Kin.Children);
        Assert.Empty(brief.PossibleDuplicateOf);
        Assert.Equal(0, brief.PossibleDuplicateCount);
        Assert.Empty(brief.Plausibility);
        Assert.Empty(brief.Notes.Person);
        Assert.Empty(brief.Notes.Families);
        Assert.NotEmpty(brief.Gaps);
    }

    [Fact]
    public void Build_IsDeterministic()
    {
        var snapshot = Snapshot();

        string first = JsonSerializer.Serialize(Build(snapshot, cardType: "enrich-person"));
        string second = JsonSerializer.Serialize(Build(snapshot, cardType: "enrich-person"));

        Assert.Equal(first, second);
    }
}
