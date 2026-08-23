using System.Text;
using GedCore.Matching;

namespace GedCore.Tests;

// ---------------------------------------------------------------------------
// Direct coverage of GedCore.Matching.PersonMatchCore -- the neutral engine
// PersonMatcherTests already exercises exhaustively through the
// GedFire.Match.PersonMatcher adapter. GedCore.Apply's duplicate detector
// calls this engine directly, with no GedIndividual/MatchIndex in the
// picture; these tests prove the engine itself works from the bare neutral
// PersonMatchCandidate shape a non-GedFire caller would build.
// ---------------------------------------------------------------------------

public class PersonMatchCoreTests
{
    const string EmptyNicknamesJson = """{ "male": {}, "female": {} }""";
    const string SyntheticNicknamesJson = """
        {
          "male": { "william": ["william", "bill", "billy"] },
          "female": {}
        }
        """;

    static NicknameDirectory Nicknames(string json = EmptyNicknamesJson) =>
        new(new MemoryStream(Encoding.UTF8.GetBytes(json)));

    static PersonMatchCandidate Candidate(
        string id, string surname, string given, int? birthYear = null, bool? isMale = null,
        IReadOnlyList<string>? places = null, IReadOnlyList<string>? spouses = null, IReadOnlyList<string>? parents = null) =>
        new(id, $"{given} {surname}", surname.ToUpperInvariant(), given.ToUpperInvariant(),
            isMale,
            birthYear is not null || places is { Count: > 0 }
                ? new PersonMatchEvent(birthYear, places?.FirstOrDefault())
                : null,
            null,
            parents is { Count: > 0 }
                ? new PersonMatchParents(parents.ElementAtOrDefault(0), parents.ElementAtOrDefault(1))
                : null,
            spouses?.Select(name => new PersonMatchMarriage(name, null, null)).ToList() ?? []);

    [Fact]
    public void ExactUniqueMatch_ReturnsSingle()
    {
        var candidates = new List<PersonMatchCandidate> { Candidate("id-1", "Morrill", "Frederick") };
        var outcome = new PersonMatchCore().Match(candidates, "Frederick Morrill", null, Nicknames());

        Assert.Equal(PersonMatchType.Single, outcome.PersonMatchType);
        Assert.Equal("id-1", outcome.Matches[0].Id);
        Assert.Equal(1, outcome.TotalMatches);
    }

    [Fact]
    public void ExactTie_ReturnsCandidates()
    {
        var candidates = new List<PersonMatchCandidate>
        {
            Candidate("id-1", "Morrill", "Frederick"),
            Candidate("id-2", "Morrill", "Frederick"),
        };
        var outcome = new PersonMatchCore().Match(candidates, "Frederick Morrill", null, Nicknames());

        Assert.Equal(PersonMatchType.Candidates, outcome.PersonMatchType);
        Assert.Equal(2, outcome.Matches.Count);
        Assert.Equal(2, outcome.TotalMatches);
        // Id is the final tiebreak once display name also ties.
        Assert.Equal(["id-1", "id-2"], outcome.Matches.Select(m => m.Id));
    }

    [Fact]
    public void WhollyUnrelatedQuery_ReturnsNone()
    {
        var candidates = new List<PersonMatchCandidate> { Candidate("id-1", "Morrill", "Frederick") };
        var outcome = new PersonMatchCore().Match(candidates, "Zzqxvw Bbdfgh", null, Nicknames());

        Assert.Equal(PersonMatchType.None, outcome.PersonMatchType);
        Assert.Equal(0, outcome.TotalMatches);
        Assert.Empty(outcome.Matches);
    }

    [Fact]
    public void BirthYearHint_NarrowsAnOtherwiseTiedPair()
    {
        var candidates = new List<PersonMatchCandidate>
        {
            Candidate("young", "Smith", "Xvqz", birthYear: 1900),
            Candidate("old", "Smith", "Xvqz", birthYear: 1800),
        };
        var outcome = new PersonMatchCore().Match(
            candidates, "Xvqz Smith", new MatchHints(Birth: new EventHint(Year: 1800)), Nicknames());

        Assert.Equal(PersonMatchType.Single, outcome.PersonMatchType);
        Assert.Equal("old", outcome.Matches[0].Id);
    }

    [Fact]
    public void DeathHint_DoesNotMatchBirthEvidence()
    {
        var candidates = new List<PersonMatchCandidate>
        {
            CandidateWithEvidence("birth-only", birth: new PersonMatchEvent(1850, "BOSTON")),
            CandidateWithEvidence("death-match", death: new PersonMatchEvent(1850, "BOSTON")),
        };

        var outcome = new PersonMatchCore().Match(
            candidates,
            "Jane Doe",
            new MatchHints(Death: new EventHint(1850, "Boston")),
            Nicknames());

        Assert.Equal(PersonMatchType.Candidates, outcome.PersonMatchType);
        Assert.Equal("death-match", outcome.Matches[0].Id);
        Assert.Equal(100.0, outcome.Matches.Single(m => m.Id == "birth-only").FinalScore, 6);
        Assert.Equal(60.0, outcome.Matches.Single(m => m.Id == "birth-only").RawScore, 6);
        Assert.Equal(90.0, outcome.Matches.Single(m => m.Id == "death-match").RawScore, 6);
    }

    [Fact]
    public void FatherHint_DoesNotMatchMotherEvidence()
    {
        var candidates = new List<PersonMatchCandidate>
        {
            CandidateWithEvidence("father-match", parents: new PersonMatchParents("ALEX SMITH", "BETH SMITH")),
            CandidateWithEvidence("mother-only", parents: new PersonMatchParents("CARL JONES", "ALEX SMITH")),
        };

        var outcome = new PersonMatchCore().Match(
            candidates,
            "Jane Doe",
            new MatchHints(Parents: new ParentsHint(Father: "Alex Smith")),
            Nicknames());

        Assert.Equal(PersonMatchType.Single, outcome.PersonMatchType);
        Assert.Equal("father-match", outcome.Matches[0].Id);
        Assert.Equal(75.0, outcome.Matches.Single(m => m.Id == "mother-only").FinalScore, 6);
    }

    [Theory]
    [InlineData(1900, 70.0, 100.0)]
    [InlineData(1901, 66.666666666667, 95.238095238095)]
    [InlineData(1902, 63.333333333333, 90.476190476190)]
    [InlineData(1903, 60.0, 85.714285714286)]
    public void MarriageYearHint_UsesProportionalTenPointWeight(
        int hintYear, double expectedRaw, double expectedFinal)
    {
        var candidates = new List<PersonMatchCandidate>
        {
            CandidateWithEvidence("id-1", marriages: [new PersonMatchMarriage(null, 1900, null)]),
        };

        var outcome = new PersonMatchCore().Match(
            candidates,
            "Jane Doe",
            new MatchHints(Spouse: new SpouseHint(Marriage: new EventHint(Year: hintYear))),
            Nicknames());

        Assert.Equal(expectedRaw, outcome.Matches[0].RawScore, 6);
        Assert.Equal(expectedFinal, outcome.Matches[0].FinalScore, 6);
    }

    [Fact]
    public void SpouseHint_DoesNotCombineLeavesAcrossMarriages()
    {
        var candidates = new List<PersonMatchCandidate>
        {
            CandidateWithEvidence("split", marriages:
            [
                new PersonMatchMarriage("ALEX SMITH", 1900, "BOSTON"),
                new PersonMatchMarriage("BETH JONES", 1920, "PORTLAND"),
            ]),
            CandidateWithEvidence("correlated", marriages:
            [
                new PersonMatchMarriage("ALEX SMITH", 1920, "PORTLAND"),
            ]),
        };
        var hints = new MatchHints(Spouse: new SpouseHint(
            "Alex Smith", new EventHint(1920, "Portland")));

        var outcome = new PersonMatchCore().Match(candidates, "Jane Doe", hints, Nicknames());

        Assert.Equal(PersonMatchType.Single, outcome.PersonMatchType);
        Assert.Equal("correlated", outcome.Matches[0].Id);
        Assert.Equal(80.0, outcome.Matches.Single(m => m.Id == "split").FinalScore, 6);
    }

    static PersonMatchCandidate CandidateWithEvidence(
        string id,
        PersonMatchEvent? birth = null,
        PersonMatchEvent? death = null,
        PersonMatchParents? parents = null,
        IReadOnlyList<PersonMatchMarriage>? marriages = null) =>
        new(id, "Jane Doe", "DOE", "JANE", false, birth, death, parents, marriages ?? []);

    [Fact]
    public void DocumentedNickname_MatchesWhenSimilarityAloneIsWeak()
    {
        var candidates = new List<PersonMatchCandidate> { Candidate("id-1", "Morrill", "William") };
        var outcome = new PersonMatchCore().Match(
            candidates, "Bill Morrill", null, Nicknames(SyntheticNicknamesJson));

        Assert.Equal(PersonMatchType.Single, outcome.PersonMatchType);
        Assert.Equal("id-1", outcome.Matches[0].Id);
    }

    [Fact]
    public void MaxResults_CapsListWithoutChangingTotalOrClassification()
    {
        var candidates = Enumerable.Range(1, 10)
            .Select(i => Candidate($"id-{i:D2}", "Doe", "Jane"))
            .ToList();
        var outcome = new PersonMatchCore().Match(candidates, "Jane Doe", null, Nicknames(), maxResults: 3);

        Assert.Equal(PersonMatchType.Candidates, outcome.PersonMatchType);
        Assert.Equal(3, outcome.Matches.Count);
        Assert.Equal(10, outcome.TotalMatches);
        Assert.True(outcome.Truncated);
    }

    [Fact]
    public void MaxResults_Null_ReturnsWholeRecallSet()
    {
        var candidates = Enumerable.Range(1, 10)
            .Select(i => Candidate($"id-{i:D2}", "Doe", "Jane"))
            .ToList();
        var outcome = new PersonMatchCore().Match(candidates, "Jane Doe", null, Nicknames(), maxResults: null);

        Assert.Equal(10, outcome.Matches.Count);
        Assert.False(outcome.Truncated);
    }

    [Fact]
    public void SuggestionBand_OffersCloseSpellingReason()
    {
        var candidates = new List<PersonMatchCandidate> { Candidate("id-1", "Smith", "John") };
        var outcome = new PersonMatchCore().Match(candidates, "Xvqz Smith", null, Nicknames());

        Assert.Equal(PersonMatchType.None, outcome.PersonMatchType);
        var suggestion = Assert.Single(outcome.Suggestions);
        Assert.Equal("id-1", suggestion.Id);
        Assert.Equal(SuggestionReason.CloseSpelling, suggestion.Reason);
        Assert.InRange(suggestion.Score, 55.0, 69.0);
    }

    [Fact]
    public void CommonSurname_UnrelatedGivenName_DoesNotRideAlongOnSurnameStrength()
    {
        // Regression test: SurnameWeight (35) is 58% of the 60-point
        // two-token pool, so an exact surname match alone used to clear
        // RecallThreshold (70) against any given name whose similarity was
        // as low as 0.28 -- a bar unrelated given names routinely clear.
        // "Ezekiel"/"Charles" (0.36) is one such coincidental case: on a
        // large tree it recalled every same-surname person regardless of
        // given name.
        var candidates = new List<PersonMatchCandidate>
        {
            Candidate("id-1", "Morrill", "Ezekiel"),
            Candidate("id-2", "Morrill", "Charles"),
        };
        var outcome = new PersonMatchCore().Match(candidates, "Ezekiel Morrill", null, Nicknames());

        Assert.Equal(PersonMatchType.Single, outcome.PersonMatchType);
        Assert.Equal("id-1", outcome.Matches[0].Id);
        Assert.Equal(1, outcome.TotalMatches);
    }

    [Fact]
    public void ZeroOrNegativeMaxResults_Throws()
    {
        var candidates = new List<PersonMatchCandidate> { Candidate("id-1", "Doe", "Jane") };
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new PersonMatchCore().Match(candidates, "Jane Doe", null, Nicknames(), maxResults: 0));
    }
}
