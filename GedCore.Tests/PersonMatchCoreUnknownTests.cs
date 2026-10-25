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

    static PersonMatchCandidate MarriedTo(string id, string given, string surname, string spouse) => new(
        id, $"{given} {surname}", PersonNameNormalizer.Normalize(surname), PersonNameNormalizer.Normalize(given), false,
        null, null, null, [new PersonMatchMarriage(PersonNameNormalizer.Normalize(spouse), null, null)]);

    static PersonMatchOutcome DuplicateCheckWithSpouse(string candidateSpouse, string hintSpouse) =>
        Core.Match([MarriedTo("@I1@", "Mary", "Clapp", candidateSpouse)], "Mary Unknown",
            new MatchHints(Spouse: new SpouseHint(hintSpouse)), NoNicknames, forDuplicateDetection: true);

    [Theory]
    [InlineData(null)]
    [InlineData(1900)]
    public void UnknownSurname_WithoutAgreeingEvents_CannotReachSingle(int? birthYear)
    {
        var candidate = MarriedTo("@I1@", "Mary", "____", "Robert /Test/") with
        {
            Birth = birthYear is null ? null : new PersonMatchEvent(birthYear, null),
        };
        var hints = new MatchHints(Birth: new EventHint(1850), Spouse: new SpouseHint("Robert /Test/"), IsMale: false);

        var outcome = Core.Match([candidate], "Mary Smith", hints, NoNicknames);

        Assert.Equal(PersonMatchType.Candidates, outcome.PersonMatchType);
        Assert.True(Assert.Single(outcome.Matches).FinalScore < PersonMatchCore.SingleMatchMinScore);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(1850)]
    public void UnknownSurname_NameAlone_CannotReachSingle(int? birthYear)
    {
        var outcome = Core.Match([Person("@I1@", "Mary", "Unknown", birthYear)], "Mary Smith", null, NoNicknames);

        Assert.Equal(PersonMatchType.Candidates, outcome.PersonMatchType);
        Assert.True(Assert.Single(outcome.Matches).FinalScore < PersonMatchCore.SingleMatchMinScore);
    }

    [Theory]
    [InlineData("birth", true)]
    [InlineData("birth", false)]
    [InlineData("death", true)]
    [InlineData("death", false)]
    [InlineData("marriage", true)]
    [InlineData("marriage", false)]
    public void UnknownSurname_AgreeingEvent_CanReachSingle(string kind, bool useYear)
    {
        var recorded = new PersonMatchEvent(1850, PersonNameNormalizer.Normalize("Testville"));
        var hint = useYear ? new EventHint(1850) : new EventHint(Place: "Testville");
        var candidate = Person("@I1@", "Mary", "Unknown") with
        {
            Birth = kind == "birth" ? recorded : null,
            Death = kind == "death" ? recorded : null,
            Marriages = kind == "marriage" ? [new PersonMatchMarriage(null, 1850, recorded.NormalizedPlace)] : [],
        };
        var hints = new MatchHints(
            Birth: kind == "birth" ? hint : null,
            Death: kind == "death" ? hint : null,
            Spouse: kind == "marriage" ? new SpouseHint(Marriage: hint) : null);

        var outcome = Core.Match([candidate], "Mary Smith", hints, NoNicknames);

        Assert.Equal(PersonMatchType.Single, outcome.PersonMatchType);
        Assert.True(Assert.Single(outcome.Matches).FinalScore >= PersonMatchCore.SingleMatchMinScore);
    }

    [Fact]
    public void QueryUnknownSurname_WithoutAgreeingEvents_CannotReachSingle()
    {
        var outcome = Core.Match([Person("@I1@", "Mary", "Smith")], "Mary Unknown", null, NoNicknames);

        Assert.Equal(PersonMatchType.Candidates, outcome.PersonMatchType);
        Assert.True(Assert.Single(outcome.Matches).FinalScore < PersonMatchCore.SingleMatchMinScore);
    }

    [Fact]
    public void DuplicateDetection_SpouseWithSameSurnameButDifferentGivenName_IsNotAgreement()
    {
        Assert.Equal(PersonMatchType.None, DuplicateCheckWithSpouse("James Morrill", "Jabez Morrill").PersonMatchType);
    }

    [Fact]
    public void DuplicateDetection_UnknownSurnameWithAnAgreeingSpouse_IsAMatch()
    {
        Assert.Equal(["@I1@"], Ids(DuplicateCheckWithSpouse("James Morrill", "James Morrill")));
    }

    [Theory]
    [InlineData("Unknown", "McDonald", "John Smith", true)]
    [InlineData("McDonald", "Unknown", "John Smith", true)]
    [InlineData("Unknown", "Jones", "Ben Simmons", false)]
    [InlineData("Jones", "Unknown", "Ben Simmons", false)]
    public void DuplicateDetection_UnknownSurname_RequiresAgreeingSpouse(
        string querySurname, string candidateSurname, string candidateSpouse, bool shouldMatch)
    {
        var candidate = MarriedTo("@I1@", "Mary", candidateSurname, candidateSpouse);
        var hints = new MatchHints(Spouse: new SpouseHint("John Smith"), IsMale: false);

        var outcome = Core.Match([candidate], $"Mary {querySurname}", hints, NoNicknames, forDuplicateDetection: true);

        Assert.Equal(shouldMatch ? PersonMatchType.Single : PersonMatchType.None, outcome.PersonMatchType);
        Assert.Equal(shouldMatch ? ["@I1@"] : Array.Empty<string>(), Ids(outcome));
    }

    [Fact]
    public void DuplicateDetection_FatherWithSameSurnameButDifferentGivenName_IsNotAgreement()
    {
        var candidate = Person("@I1@", "Mary", "Smith", father: "John Smith");
        var hints = new MatchHints(Parents: new ParentsHint("James Smith", null));

        var outcome = Core.Match([candidate], "Mary Unknown", hints, NoNicknames, forDuplicateDetection: true);

        Assert.Equal(PersonMatchType.None, outcome.PersonMatchType);
    }
}
