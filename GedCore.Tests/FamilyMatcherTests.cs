using GedCore.Matching;
using GedFire.Match;

namespace GedCore.Tests;

public class FamilyMatcherTests
{
    const string Ged = """
        0 HEAD
        1 GEDC
        2 VERS 5.5.1
        0 @I1@ INDI
        1 NAME Cornelius /Ashworth/
        1 SEX M
        1 BIRT
        2 DATE 1741
        2 PLAC Harwick, Northvale
        1 FAMS @F1@
        0 @I2@ INDI
        1 NAME Beatrice /Fenwick/
        1 SEX F
        1 BIRT
        2 DATE 1745
        1 FAMS @F1@
        0 @I3@ INDI
        1 NAME Levi /Ashworth/
        1 SEX M
        1 BIRT
        2 DATE 1770
        2 PLAC Harwick, Northvale
        1 FAMC @F1@
        1 FAMS @F2@
        0 @I4@ INDI
        1 NAME Hannah /Ashworth/
        1 SEX F
        1 BIRT
        2 DATE 1772
        2 PLAC Fairhaven, Northvale
        1 FAMC @F1@
        0 @F1@ FAM
        1 HUSB @I1@
        1 WIFE @I2@
        1 CHIL @I3@
        1 CHIL @I4@
        0 @I5@ INDI
        1 NAME Jane /Whitcombe/
        1 SEX F
        1 BIRT
        2 DATE 1775
        1 FAMS @F2@
        0 @I6@ INDI
        1 NAME Joseph /Ashworth/
        1 SEX M
        1 BIRT
        2 DATE 1800
        1 FAMC @F2@
        0 @F2@ FAM
        1 HUSB @I3@
        1 WIFE @I5@
        1 CHIL @I6@
        """;

    static readonly MatchIndex Index = new(MatchTestModels.Build(Ged));
    static readonly FamilyMatcher Matcher = new(NicknameDirectory.LoadEmbedded());

    static FamilySearchOutcome Find(
        FamilyRelation relation, string name, MatchHints? hints = null, int maxResults = 8) =>
        Matcher.Find(Index, relation, name, hints, maxResults);

    static IEnumerable<string> Sought(FamilySearchOutcome outcome) => outcome.Candidates.Select(c => c.Person.Xref);

    [Fact]
    public void Spouse_NameOfTheWife_FindsTheHusbandThroughTheirFamily()
    {
        var candidate = Assert.Single(Find(FamilyRelation.Spouse, "Beatrice Fenwick").Candidates);

        Assert.Equal("@I1@", candidate.Person.Xref);
        Assert.Equal("@F1@", candidate.Family.Xref);
        Assert.Equal("@I2@", candidate.Relative.Person.Xref);
        Assert.True(candidate.Relative.MatchScore >= 90);
    }

    [Fact]
    public void Spouse_NameOfTheHusband_FindsTheWife()
    {
        var candidate = Assert.Single(Find(FamilyRelation.Spouse, "Levi Ashworth").Candidates);

        Assert.Equal("@I5@", candidate.Person.Xref);
        Assert.Equal("@F2@", candidate.Family.Xref);
    }

    [Fact]
    public void Child_NameOfAChild_FindsBothParentsThroughTheFamilyTheyAreAChildOf()
    {
        var outcome = Find(FamilyRelation.Child, "Hannah Ashworth");

        Assert.Equal(["@I1@", "@I2@"], Sought(outcome).Order());
        Assert.All(outcome.Candidates, c => Assert.Equal("@F1@", c.Family.Xref));
        Assert.All(outcome.Candidates, c => Assert.Equal("@I4@", c.Relative.Person.Xref));
    }

    [Fact]
    public void Parent_NameOfAMother_FindsTheirChildren()
    {
        var candidate = Assert.Single(Find(FamilyRelation.Parent, "Jane Whitcombe").Candidates);

        Assert.Equal("@I6@", candidate.Person.Xref);
        Assert.Equal("@F2@", candidate.Family.Xref);
    }

    [Fact]
    public void Parent_NameOfAFather_FindsEveryChildOfEveryFamily()
    {
        var outcome = Find(FamilyRelation.Parent, "Cornelius Ashworth");

        Assert.Equal(["@I3@", "@I4@"], Sought(outcome).Order());
    }

    [Fact]
    public void BirthHints_RankThePersonSoughtNotTheRelative()
    {
        var byYear1772 = Find(FamilyRelation.Parent, "Cornelius Ashworth", new MatchHints(Birth: new EventHint(1772)));
        var byYear1770 = Find(FamilyRelation.Parent, "Cornelius Ashworth", new MatchHints(Birth: new EventHint(1770)));

        Assert.Equal("@I4@", byYear1772.Candidates[0].Person.Xref);
        Assert.Equal("@I3@", byYear1770.Candidates[0].Person.Xref);
    }

    [Fact]
    public void BirthPlaceHint_RanksThePersonWhoseBirthPlaceMatches()
    {
        var outcome = Find(FamilyRelation.Parent, "Cornelius Ashworth", new MatchHints(Birth: new EventHint(Place: "Fairhaven")));

        Assert.Equal("@I4@", outcome.Candidates[0].Person.Xref);
    }

    [Fact]
    public void UnknownName_FindsNothing()
    {
        var outcome = Find(FamilyRelation.Spouse, "Zzqxvw Bbdfghj");

        Assert.Empty(outcome.Candidates);
        Assert.Equal(0, outcome.TotalMatches);
        Assert.False(outcome.Truncated);
    }

    [Fact]
    public void MaxResults_CapsTheListAndReportsTruncation()
    {
        var outcome = Find(FamilyRelation.Child, "Hannah Ashworth", maxResults: 1);

        Assert.Single(outcome.Candidates);
        Assert.Equal(2, outcome.TotalMatches);
        Assert.True(outcome.Truncated);
    }

    [Fact]
    public void RelativeWithoutTheRequestedFamily_YieldsNoCandidates()
    {
        // Joseph has parents but no spouse family, so nobody is his spouse or child.
        Assert.Empty(Find(FamilyRelation.Spouse, "Joseph Ashworth").Candidates);
        Assert.Empty(Find(FamilyRelation.Parent, "Joseph Ashworth").Candidates);
    }
}
