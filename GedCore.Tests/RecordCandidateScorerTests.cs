using GedCore.Matching;
using GedFire.Gen;
using GedFire.Match;

namespace GedCore.Tests;

public class RecordCandidateScorerTests
{
    const string Ged = """
        0 @I1@ INDI
        1 NAME John /Ashworth/
        1 SEX M
        1 BIRT
        2 DATE 1848
        2 PLAC Iowa
        1 FAMS @F1@
        0 @I2@ INDI
        1 NAME Mary /Ashworth/
        1 SEX F
        1 FAMS @F1@
        0 @I3@ INDI
        1 NAME Thomas /Ashworth/
        1 SEX M
        1 BIRT
        2 DATE 1872
        1 FAMC @F1@
        0 @I4@ INDI
        1 NAME William /Ashworth/
        1 SEX M
        1 DEAT
        2 DATE 1875
        0 @I5@ INDI
        1 NAME Margaret /Ashworth/
        1 SEX F
        0 @F1@ FAM
        1 HUSB @I1@
        1 WIFE @I2@
        1 CHIL @I3@
        1 MARR
        2 DATE 1870
        2 PLAC Boone County, Iowa
        """;

    static readonly GedModel Model = MatchTestModels.Build(Ged);

    static RecordCandidateScorer Scorer() => new(NicknameDirectory.LoadEmbedded());

    static RecordPersonDescription Person(
        string name = "John Ashworth", bool? isMale = null, int? birthYear = null, string? birthPlace = null) =>
        new() { Name = name, IsMale = isMale, BirthYear = birthYear, BirthPlace = birthPlace };

    static ScoredMatch Match(string xref, double nameOnlyScore = 100.0)
    {
        var indi = MatchTestModels.Person(Model, xref);
        return new ScoredMatch(indi, nameOnlyScore, nameOnlyScore, nameOnlyScore);
    }

    static readonly StatedKin NoKin = new([], [], [], []);

    [Fact]
    public void Constructor_RejectsNullNicknames()
    {
        Assert.Throws<ArgumentNullException>(() => new RecordCandidateScorer(null!));
    }

    [Fact]
    public void ExclusionReason_DiedBeforeTheEvent()
    {
        string? reason = RecordCandidateScorer.ExclusionReason(
            MatchTestModels.Person(Model, "@I4@"), Person(), eventYear: 1880);
        Assert.Equal("died in 1875, before the record's 1880 event", reason);
    }

    [Fact]
    public void ExclusionReason_BornAfterTheEvent()
    {
        string? reason = RecordCandidateScorer.ExclusionReason(
            MatchTestModels.Person(Model, "@I3@"), Person(), eventYear: 1870);
        Assert.Equal("born in 1872, after the record's 1870 event", reason);
    }

    [Fact]
    public void ExclusionReason_ImpossibleAgeAtTheEvent()
    {
        string? reason = RecordCandidateScorer.ExclusionReason(
            MatchTestModels.Person(Model, "@I1@"), Person(), eventYear: 1848 + 121);
        Assert.Equal("would be 121 at the record's 1969 event", reason);
    }

    [Fact]
    public void ExclusionReason_BirthYearTooFarFromTheRecords()
    {
        string? reason = RecordCandidateScorer.ExclusionReason(
            MatchTestModels.Person(Model, "@I1@"), Person(birthYear: 1830), eventYear: null);
        Assert.Equal("born in 1848, more than 10 years from the record's 1830", reason);
    }

    [Fact]
    public void ExclusionReason_SexMismatch()
    {
        string? reason = RecordCandidateScorer.ExclusionReason(
            MatchTestModels.Person(Model, "@I1@"), Person(isMale: false), eventYear: null);
        Assert.Equal("recorded as male, the record says female", reason);
    }

    [Fact]
    public void ExclusionReason_NullWhenNothingDisagrees()
    {
        Assert.Null(RecordCandidateScorer.ExclusionReason(
            MatchTestModels.Person(Model, "@I1@"), Person(isMale: true, birthYear: 1848), eventYear: 1880));
    }

    [Fact]
    public void ExclusionReason_NoDatesMeansNoExclusion()
    {
        Assert.Null(RecordCandidateScorer.ExclusionReason(
            MatchTestModels.Person(Model, "@I5@"), Person(birthYear: 1700), eventYear: 1880));
    }

    [Fact]
    public void Score_WithOnlyAName_ScoresNameAloneAndLeavesOtherSubscoresNull()
    {
        var score = Scorer().Score(Match("@I1@"), Person(), NoKin, recordEvent: null);

        Assert.Equal(100.0, score.NameScore);
        Assert.Null(score.LifespanScore);
        Assert.Null(score.KinOverlapScore);
        Assert.Null(score.PlaceScore);
        Assert.Equal(100.0, score.Composite);
        Assert.Empty(score.MatchedKin);
    }

    [Theory]
    [InlineData(1848, 100.0)]
    [InlineData(1849, 66.7)]
    [InlineData(1850, 33.3)]
    [InlineData(1851, 0.0)]
    public void Score_LifespanDecaysWithBirthYearDistance(int statedYear, double expected)
    {
        var score = Scorer().Score(Match("@I1@"), Person(birthYear: statedYear), NoKin, recordEvent: null);
        Assert.Equal(expected, score.LifespanScore);
    }

    [Fact]
    public void Score_LifespanIsNullWhenTheCandidateHasNoBirthYear()
    {
        var score = Scorer().Score(Match("@I5@"), Person(name: "Margaret Ashworth", birthYear: 1850), NoKin, recordEvent: null);
        Assert.Null(score.LifespanScore);
    }

    [Fact]
    public void Score_KinOverlap_FindsSpouseAndChildAndNamesThem()
    {
        var kin = new StatedKin(
            Spouses: [Person(name: "Mary")],
            Parents: [],
            Children: [Person(name: "Thomas")],
            Siblings: []);

        var score = Scorer().Score(Match("@I1@"), Person(), kin, recordEvent: null);

        Assert.Equal(100.0, score.KinOverlapScore);
        Assert.Equal(
            [("spouse", "Mary", "@I2@", "Mary Ashworth"), ("child", "Thomas", "@I3@", "Thomas Ashworth")],
            score.MatchedKin.Select(k => (k.Relation, k.RecordName, k.TreeXref, k.TreeName)));
    }

    [Fact]
    public void Score_KinOverlap_AveragesInUnmatchedStatedKinAsZero()
    {
        var kin = new StatedKin(
            Spouses: [Person(name: "Mary")],
            Parents: [],
            Children: [Person(name: "Zebulon")],
            Siblings: []);

        var score = Scorer().Score(Match("@I1@"), Person(), kin, recordEvent: null);

        Assert.Equal(50.0, score.KinOverlapScore);
        Assert.Equal(["Mary"], score.MatchedKin.Select(k => k.RecordName));
    }

    [Fact]
    public void Score_KinOverlap_AcceptsNicknamesForKin()
    {
        var kin = new StatedKin(
            Spouses: [Person(name: "Polly")], // Polly is a recorded nickname for Mary.
            Parents: [], Children: [], Siblings: []);

        var score = Scorer().Score(Match("@I1@"), Person(), kin, recordEvent: null);

        Assert.Equal("@I2@", Assert.Single(score.MatchedKin).TreeXref);
        Assert.True(score.KinOverlapScore >= 96.0);
    }

    [Fact]
    public void Score_KinOverlap_RejectsAKinWhoseBirthYearIsTooFarOff()
    {
        var kin = new StatedKin(
            Spouses: [],
            Parents: [],
            Children: [new RecordPersonDescription { Name = "Thomas", BirthYear = 1880 }],
            Siblings: []);

        var score = Scorer().Score(Match("@I1@"), Person(), kin, recordEvent: null);

        Assert.Equal(0.0, score.KinOverlapScore);
        Assert.Empty(score.MatchedKin);
    }

    [Fact]
    public void Score_Place_MatchesBirthPlaceAndEventPlaceByContainment()
    {
        var recordEvent = new RecordEventDescription { Place = "Boone County, Iowa" };
        var score = Scorer().Score(Match("@I1@"), Person(birthPlace: "Iowa"), NoKin, recordEvent);

        // Birth place "Iowa" matches, and the marriage in Boone County matches the event place.
        Assert.Equal(100.0, score.PlaceScore);
    }

    [Fact]
    public void Score_Place_ZeroOnDisagreementAndNullWithoutEvidence()
    {
        var scorer = Scorer();

        var disagreeing = scorer.Score(Match("@I1@"), Person(birthPlace: "Vermont"), NoKin, recordEvent: null);
        Assert.Equal(0.0, disagreeing.PlaceScore);

        var noEvidence = scorer.Score(Match("@I5@"), Person(name: "Margaret Ashworth", birthPlace: "Iowa"), NoKin, recordEvent: null);
        Assert.Null(noEvidence.PlaceScore);
    }

    [Fact]
    public void Score_CompositeIsWeightedOverAvailableDimensionsOnly()
    {
        // Name 100 (weight 35) and lifespan 0 (weight 15): composite 35/50 = 70.
        var score = Scorer().Score(Match("@I1@"), Person(birthYear: 1860), NoKin, recordEvent: null);
        Assert.Equal(70.0, score.Composite);
    }
}
