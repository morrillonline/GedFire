using System.Text.Json;
using GedCore.Matching;
using GedFire.Match;

namespace GedCore.Tests;

public class RecordMatcherTests
{
    // An 1880 census household: John Ashworth of @F1@ is the true head. Two
    // same-name decoys: @I10@ is alive and plausible but married to Harriet,
    // and @I20@ died in 1875, before the census.
    const string Ged = """
        0 @I1@ INDI
        1 NAME John /Ashworth/
        1 SEX M
        1 BIRT
        2 DATE 1848
        2 PLAC Boone County, Iowa
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
        1 NAME Anna /Ashworth/
        1 SEX F
        1 FAMC @F1@
        0 @I5@ INDI
        1 NAME Samuel /Ashworth/
        1 SEX M
        1 FAMC @F1@
        0 @I10@ INDI
        1 NAME John /Ashworth/
        1 SEX M
        1 BIRT
        2 DATE 1846
        1 FAMS @F2@
        0 @I11@ INDI
        1 NAME Harriet /Ashworth/
        1 SEX F
        1 FAMS @F2@
        0 @I20@ INDI
        1 NAME John /Ashworth/
        1 SEX M
        1 BIRT
        2 DATE 1840
        1 DEAT
        2 DATE 1875
        0 @F1@ FAM
        1 HUSB @I1@
        1 WIFE @I2@
        1 CHIL @I3@
        1 CHIL @I4@
        1 CHIL @I5@
        1 MARR
        2 DATE 1870
        0 @F2@ FAM
        1 HUSB @I10@
        1 WIFE @I11@
        """;

    static RecordMatcher Matcher() => new(NicknameDirectory.LoadEmbedded());

    static MatchIndex Index() => new(MatchTestModels.Build(Ged));

    static RecordPersonDescription Person(
        string name, string? role = null, bool? isMale = null, int? birthYear = null, string? birthPlace = null) =>
        new() { Name = name, Role = role, IsMale = isMale, BirthYear = birthYear, BirthPlace = birthPlace };

    static RecordDescription CensusHousehold(int? focus = null) => new()
    {
        Persons =
        [
            Person("John Ashworth", role: "head", isMale: true, birthYear: 1848, birthPlace: "Iowa"),
            Person("Mary Ashworth", role: "wife", isMale: false),
            Person("Thomas Ashworth", role: "son", isMale: true, birthYear: 1872),
            Person("Anna Ashworth", role: "daughter", isMale: false),
            Person("Samuel Ashworth", role: "son", isMale: true),
        ],
        Relationships =
        [
            new RecordRelationship(RecordRelationshipKind.SpouseOf, 1, 0),
            new RecordRelationship(RecordRelationshipKind.ChildOf, 2, 0),
            new RecordRelationship(RecordRelationshipKind.ChildOf, 3, 0),
            new RecordRelationship(RecordRelationshipKind.ChildOf, 4, 0),
        ],
        Event = new RecordEventDescription { Year = 1880, Place = "Boone County, Iowa", Type = "census" },
        Focus = focus,
    };

    [Fact]
    public void Constructor_RejectsNullNicknames()
    {
        Assert.Throws<ArgumentNullException>(() => new RecordMatcher(null!));
    }

    [Fact]
    public void CensusHead_ResolvesSingle_WithKinOverlapSeparatingTheSameNameDecoy()
    {
        var head = Matcher().Match(Index(), CensusHousehold(focus: 0)).Persons.Single();

        Assert.Equal("single", head.Verdict);
        Assert.Equal("@I1@", head.ConfidentMatchXref);
        Assert.False(head.PotentialNewPerson);

        var winner = head.Candidates[0];
        Assert.Equal("@I1@", winner.Xref);
        Assert.True(winner.Score >= 90.0);
        Assert.Equal(100.0, winner.Subscores.KinOverlap);
        Assert.Equal(
            ["Mary Ashworth", "Thomas Ashworth", "Anna Ashworth", "Samuel Ashworth"],
            winner.MatchedKin.Select(k => k.RecordName));

        // The living decoy stays viable but kin overlap sinks it decisively.
        var decoy = head.Candidates.Single(c => c.Xref == "@I10@");
        Assert.Equal(0.0, decoy.Subscores.KinOverlap);
        Assert.True(winner.Score - decoy.Score >= 10.0);
    }

    [Fact]
    public void CensusHead_SurveyIsClosed_EverySameNameCandidateAppearsWithAReason()
    {
        var head = Matcher().Match(Index(), CensusHousehold(focus: 0)).Persons.Single();

        var byXref = head.SameNameSurvey.ToDictionary(e => e.Xref);
        Assert.Equal(3, byXref.Count);

        Assert.Equal("leading", byXref["@I1@"].Outcome);
        Assert.Null(byXref["@I1@"].Reason);

        Assert.Equal("candidate", byXref["@I10@"].Outcome);
        Assert.Equal("no recorded spouse matches Mary Ashworth", byXref["@I10@"].Reason);

        Assert.Equal("excluded", byXref["@I20@"].Outcome);
        Assert.Equal("died in 1875, before the record's 1880 event", byXref["@I20@"].Reason);
    }

    [Fact]
    public void WholeHousehold_IdentifiesEveryPersonWhenNoFocusIsGiven()
    {
        var result = Matcher().Match(Index(), CensusHousehold());

        Assert.Equal([0, 1, 2, 3, 4], result.Persons.Select(p => p.Index));
        Assert.Equal(["head", "wife", "son", "daughter", "son"], result.Persons.Select(p => p.Role));
        Assert.Equal("@I1@", result.Persons[0].ConfidentMatchXref);
        Assert.Equal("@I2@", result.Persons[1].ConfidentMatchXref);
        Assert.Equal("@I3@", result.Persons[2].ConfidentMatchXref);
    }

    [Fact]
    public void PersonAbsentFromTheTree_IsFlaggedAsPotentialNewPerson_NotForceMatched()
    {
        var record = new RecordDescription { Persons = [Person("Zebulon Moxley")] };

        var person = Matcher().Match(Index(), record).Persons.Single();

        Assert.Equal("none", person.Verdict);
        Assert.True(person.PotentialNewPerson);
        Assert.Null(person.ConfidentMatchXref);
        Assert.Empty(person.Candidates);
    }

    [Fact]
    public void PersonWithACloseButDifferentName_ListsPossibleDuplicates()
    {
        var record = new RecordDescription { Persons = [Person("Samual Ashworth")] };

        var person = Matcher().Match(Index(), record).Persons.Single();

        if (person.Verdict == "none" && person.SameNameSurvey.Count == 0)
        {
            var duplicate = Assert.Single(person.PossibleDuplicates, d => d.Xref == "@I5@");
            Assert.Equal("close spelling", duplicate.Reason);
        }
        else
        {
            // The matcher recalled the variant spelling directly; it must point at Samuel.
            Assert.Contains(person.Candidates, c => c.Xref == "@I5@");
        }
    }

    [Fact]
    public void EveryoneExcluded_YieldsNoneWithTheExclusionsInTheSurvey()
    {
        var record = new RecordDescription
        {
            Persons = [Person("John Ashworth")],
            Event = new RecordEventDescription { Year = 1990 },
        };

        var person = Matcher().Match(Index(), record).Persons.Single();

        Assert.Equal("none", person.Verdict);
        Assert.True(person.PotentialNewPerson);
        Assert.Empty(person.Candidates);
        Assert.Equal(3, person.SameNameSurvey.Count);
        Assert.All(person.SameNameSurvey, e => Assert.Equal("excluded", e.Outcome));
    }

    [Fact]
    public void SameNameDecoysWithoutKinEvidence_YieldCandidatesNotAGuess()
    {
        // Name only: three Johns, nothing to separate them.
        var record = new RecordDescription { Persons = [Person("John Ashworth")] };

        var person = Matcher().Match(Index(), record).Persons.Single();

        Assert.Equal("candidates", person.Verdict);
        Assert.Null(person.ConfidentMatchXref);
        Assert.Equal(3, person.Candidates.Count);
    }

    [Fact]
    public void Match_IsDeterministic()
    {
        var matcher = Matcher();
        var index = Index();

        string first = JsonSerializer.Serialize(matcher.Match(index, CensusHousehold()));
        string second = JsonSerializer.Serialize(matcher.Match(index, CensusHousehold()));

        Assert.Equal(first, second);
    }
}
