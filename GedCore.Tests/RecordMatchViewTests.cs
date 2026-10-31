using GedFire.Match;

namespace GedCore.Tests;

public class RecordMatchViewTests
{
    static RecordCandidateMatch Candidate(int n) => new()
    {
        Xref = $"@I{n}@",
        Name = "John Ashworth",
        Born = "1848, Iowa",
        Lifespan = "1848-?",
        Score = 90 - n,
        Subscores = new RecordSubscores { Name = 100 },
        MatchedKin = [],
    };

    static RecordPersonMatch Person(int candidates) => new()
    {
        Index = 0,
        Name = "John Ashworth",
        Verdict = "candidates",
        RunnerUpXref = "@I2@",
        RunnerUpScore = 88,
        Candidates = [.. Enumerable.Range(1, candidates).Select(Candidate)],
        SameNameSurvey =
        [
            new RecordSurveyEntry { Xref = "@I1@", Name = "John Ashworth", Outcome = "leading" },
            new RecordSurveyEntry { Xref = "@I2@", Name = "John Ashworth", Outcome = "candidate", Reason = "scored 88 against the leading candidate's 89" },
            new RecordSurveyEntry { Xref = "@I99@", Name = "John Ashworth", Outcome = "excluded", Reason = "died before the event" },
        ],
        PotentialNewPerson = false,
        PossibleDuplicates = [],
    };

    static RecordMatchResult Result(int candidates) => new([Person(candidates)]);

    [Fact]
    public void TheDefault_CapsAtFiveAndCountsTheRest()
    {
        var person = ((RecordMatchResult)new RecordMatchView().Present(Result(8))).Persons[0];

        Assert.Equal(5, person.Candidates.Count);
        Assert.Equal(3, person.CandidatesOmitted);
    }

    [Fact]
    public void ACap_KeepsTheVerdictAndRunnerUp()
    {
        var uncapped = Person(8);
        var capped = ((RecordMatchResult)new RecordMatchView(MaxCandidates: 1).Present(Result(8))).Persons[0];

        Assert.Equal("@I1@", Assert.Single(capped.Candidates).Xref);
        Assert.Equal(uncapped.Verdict, capped.Verdict);
        Assert.Equal(uncapped.RunnerUpXref, capped.RunnerUpXref);
        Assert.Equal(uncapped.RunnerUpScore, capped.RunnerUpScore);
        Assert.Equal(7, capped.CandidatesOmitted);
    }

    [Fact]
    public void FewerCandidatesThanTheCap_OmitNothing()
    {
        var person = ((RecordMatchResult)new RecordMatchView().Present(Result(2))).Persons[0];

        Assert.Equal(2, person.Candidates.Count);
        Assert.Equal(0, person.CandidatesOmitted);
    }

    [Fact]
    public void Compact_ShortensRowsButKeepsEverySurveyReason()
    {
        var person = ((CompactRecordMatchResult)new RecordMatchView(MaxCandidates: 2, Compact: true).Present(Result(4))).Persons[0];

        var first = person.Candidates[0];
        Assert.Equal(("@I1@", "John Ashworth", "1848-?", 89.0), (first.Xref, first.Name, first.Lifespan, first.Score));
        Assert.Equal(2, person.CandidatesOmitted);
        Assert.Equal("@I2@", person.RunnerUpXref);
        Assert.Equal(
            [new CompactSurveyRow("@I1@", "John Ashworth", null),
             new CompactSurveyRow("@I2@", "John Ashworth", "scored 88 against the leading candidate's 89"),
             new CompactSurveyRow("@I99@", "John Ashworth", "died before the event")],
            person.SameNameSurvey);
    }
}
