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
            birthYear, isMale, places ?? [], spouses ?? [], parents ?? []);

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
            candidates, "Xvqz Smith", new MatchHints(BirthYear: 1800, null, null, null), Nicknames());

        Assert.Equal(PersonMatchType.Single, outcome.PersonMatchType);
        Assert.Equal("old", outcome.Matches[0].Id);
    }

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
    public void ZeroOrNegativeMaxResults_Throws()
    {
        var candidates = new List<PersonMatchCandidate> { Candidate("id-1", "Doe", "Jane") };
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new PersonMatchCore().Match(candidates, "Jane Doe", null, Nicknames(), maxResults: 0));
    }
}
