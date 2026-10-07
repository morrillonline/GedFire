using GedCore.Matching;

namespace GedCore.Tests;

public class PersonMatchCoreUnknownTests
{
    static readonly PersonMatchCore Core = new();
    static readonly NicknameDirectory NoNicknames =
        new(new MemoryStream(System.Text.Encoding.UTF8.GetBytes("""{"male":{},"female":{}}""")));

    static PersonMatchCandidate Person(
        string id, string given, string surname, int? birthYear = null, string? father = null, bool? isMale = null) => new(
        id, $"{given} {surname}", PersonNameNormalizer.Normalize(surname), PersonNameNormalizer.Normalize(given), isMale,
        birthYear is null ? null : new PersonMatchEvent(birthYear, null), null,
        father is null ? null : new PersonMatchParents(PersonNameNormalizer.Normalize(father), null), []);

    static IReadOnlyList<string> Ids(PersonMatchOutcome outcome) => [.. outcome.Matches.Select(m => m.Id)];

    [Fact]
    public void Query_UnknownGiven_MatchesEveryoneWithThatSurname()
    {
        var people = new[] { Person("@I1@", "Mary", "Pike"), Person("@I2@", "John", "Pike"), Person("@I3@", "Mary", "Bell") };

        var outcome = Core.Match(people, "Unknown Pike", null, NoNicknames);

        Assert.Equal(["@I1@", "@I2@"], Ids(outcome).Order());
    }

    [Fact]
    public void Query_UnknownSurname_MatchesEveryoneWithThatGivenName()
    {
        var people = new[] { Person("@I1@", "Mary", "Pike"), Person("@I2@", "John", "Pike"), Person("@I3@", "Mary", "Bell") };

        var outcome = Core.Match(people, "Mary /Unknown/", null, NoNicknames);

        Assert.Equal(["@I1@", "@I3@"], Ids(outcome).Order());
    }

    [Fact]
    public void Query_UnknownInBothParts_HasNoNameEvidenceAndMatchesNobody()
    {
        var outcome = Core.Match([Person("@I1@", "Mary", "Pike")], "Unknown Unknown", null, NoNicknames);

        Assert.Equal(PersonMatchType.None, outcome.PersonMatchType);
    }

    [Fact]
    public void Candidate_UnknownSurname_IsRecalledForAKnownSurnameQuery()
    {
        var outcome = Core.Match([Person("@I1@", "Mary", "Unknown")], "Mary Smith", null, NoNicknames);

        Assert.Equal(["@I1@"], Ids(outcome));
        Assert.True(outcome.Matches[0].Wildcard);
    }

    [Fact]
    public void Candidate_UnknownSurname_RanksBelowAnExactNameEvenWithBetterHints()
    {
        var people = new[] { Person("@I1@", "Mary", "Unknown", birthYear: 1850), Person("@I2@", "Mary", "Smith", birthYear: 1900) };
        var hints = new MatchHints(Birth: new EventHint(1850, null));

        var outcome = Core.Match(people, "Mary Smith", hints, NoNicknames);

        Assert.Equal(["@I2@", "@I1@"], Ids(outcome));
    }

    [Fact]
    public void Candidate_UnknownSurname_HintsDecideAmongWildcardCandidates()
    {
        var people = new[] { Person("@I1@", "Mary", "Unknown", birthYear: 1900), Person("@I2@", "Mary", "Unknown", birthYear: 1850) };

        var outcome = Core.Match(people, "Mary Smith", new MatchHints(Birth: new EventHint(1850, null)), NoNicknames);

        Assert.Equal(["@I2@", "@I1@"], Ids(outcome));
    }

    [Fact]
    public void DuplicateDetection_UnknownPartWithoutAgreeingRelative_IsNotAMatch()
    {
        var people = new[] { Person("@I1@", "Mary", "Smith", 1850, father: "John Smith") };
        var hints = new MatchHints(Birth: new EventHint(1850, null), Parents: new ParentsHint("John Brown", null));

        var outcome = Core.Match(people, "Mary Unknown", hints, NoNicknames, forDuplicateDetection: true);

        Assert.Equal(PersonMatchType.None, outcome.PersonMatchType);
    }

    [Fact]
    public void DuplicateDetection_UnknownPartWithAgreeingRelative_IsAWildcardMatch()
    {
        var people = new[] { Person("@I1@", "Mary", "Smith", 1850, father: "John Smith") };
        var hints = new MatchHints(Birth: new EventHint(1850, null), Parents: new ParentsHint("John Smith", null));

        var outcome = Core.Match(people, "Mary Unknown", hints, NoNicknames, forDuplicateDetection: true);

        Assert.Equal(["@I1@"], Ids(outcome));
        Assert.True(outcome.Matches[0].Wildcard);
    }

    [Fact]
    public void KnownNames_AreNotWildcards()
    {
        var outcome = Core.Match([Person("@I1@", "Mary", "Smith")], "Mary Smith", null, NoNicknames);

        Assert.False(outcome.Matches[0].Wildcard);
    }

    [Fact]
    public void Candidate_SurnameOfOnlyPunctuation_IsAWildcardLikeUnknown()
    {
        var outcome = Core.Match([Person("@I1@", "Mary", "----")], "Mary Smith", null, NoNicknames);

        Assert.Equal(["@I1@"], Ids(outcome));
        Assert.True(outcome.Matches[0].Wildcard);
    }
}
