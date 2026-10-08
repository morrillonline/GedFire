using GedCore.Matching;
using GedFire.Gen;
using GedFire.Match;
using GedFire.Mcp;

namespace GedCore.Tests;

public class FindPersonResultMapperTests
{
    static GedIndividual Person(string xref, string given, string surname, string? birthDate = null, string? birthPlace = null)
    {
        var person = new GedIndividual { Xref = xref, FirstName = given, LastNameRaw = surname };
        if (birthDate is not null)
            person.Birth = new GedEvent { Tag = "BIRT", Date = birthDate, Place = birthPlace ?? "" };
        return person;
    }

    [Fact]
    public void None_CarriesSuggestionsAndNoCandidates()
    {
        var result = FindPersonResultMapper.Map(new MatchOutcome
        {
            PersonMatchType = PersonMatchType.None,
            Suggestions = [new Suggestion(Person("@I1@", "Mary", "Wood"), SuggestionReason.CloseSpelling, 62.5)],
        });

        Assert.Equal("none", result.MatchType);
        Assert.Null(result.ConfidentMatchXref);
        Assert.Null(result.ConfidentMatchScore);
        Assert.Null(result.Person);
        Assert.Empty(result.Candidates);
        var suggestion = Assert.Single(result.Suggestions);
        Assert.Equal("@I1@", suggestion.Xref);
        Assert.Equal("close spelling", suggestion.Reason);
        Assert.Equal(62.5, suggestion.MatchScore);
    }

    [Fact]
    public void PartialNameSuggestions_AreLabelledAsSuch()
    {
        var result = FindPersonResultMapper.Map(new MatchOutcome
        {
            PersonMatchType = PersonMatchType.None,
            Suggestions = [new Suggestion(Person("@I1@", "Mary", "Wood"), SuggestionReason.PartialName, 60)],
        });

        Assert.Equal("partial name", Assert.Single(result.Suggestions).Reason);
    }

    [Fact]
    public void Single_ExpandsTheWinnerAndKeepsTheRecallCounts()
    {
        var winner = Person("@I2@", "Mary", "Wood", "12 MAR 1741", "Harwick");
        var result = FindPersonResultMapper.Map(new MatchOutcome
        {
            PersonMatchType = PersonMatchType.Single,
            Matches = [new ScoredMatch(winner, 97.5, 80, 100)],
            TotalMatches = 3,
            Truncated = true,
        });

        Assert.Equal("single", result.MatchType);
        Assert.Equal("@I2@", result.ConfidentMatchXref);
        Assert.Equal(97.5, result.ConfidentMatchScore);
        Assert.Equal("@I2@", result.Person!.Xref);
        Assert.Equal("Mary Wood", result.Person.Name);
        Assert.Equal(1741, result.Person.Birth!.Year);
        Assert.Equal("Harwick", result.Person.Birth.Place);
        Assert.Null(result.Person.Death);
        Assert.Empty(result.Person.Families.AsChild);
        Assert.Empty(result.Person.Families.AsParent);
        Assert.Equal(3, result.TotalMatches);
        Assert.True(result.Truncated);
    }

    [Fact]
    public void Candidates_ListEachWithItsScoreAndNoConfidentMatch()
    {
        var result = FindPersonResultMapper.Map(new MatchOutcome
        {
            PersonMatchType = PersonMatchType.Candidates,
            Matches =
            [
                new ScoredMatch(Person("@I1@", "Mary", "Wood"), 88, 70, 100),
                new ScoredMatch(Person("@I2@", "Mary", "Wood"), 80, 60, 100),
            ],
            TotalMatches = 2,
        });

        Assert.Equal("candidates", result.MatchType);
        Assert.Null(result.ConfidentMatchXref);
        Assert.Null(result.Person);
        Assert.Equal(["@I1@", "@I2@"], result.Candidates.Select(c => c.Xref));
        Assert.Equal([88d, 80d], result.Candidates.Select(c => c.MatchScore));
        Assert.All(result.Candidates, c => Assert.False(c.SurnameUnknown));
    }

    [Fact]
    public void ABirthWithNeitherDateNorPlace_IsReportedAsAbsent()
    {
        var person = Person("@I1@", "Mary", "Wood");
        person.Birth = new GedEvent { Tag = "BIRT" };

        var result = FindPersonResultMapper.Map(new MatchOutcome
        {
            PersonMatchType = PersonMatchType.Candidates,
            Matches = [new ScoredMatch(person, 90, 80, 100)],
            TotalMatches = 1,
        });

        Assert.Null(Assert.Single(result.Candidates).Birth);
    }
}
