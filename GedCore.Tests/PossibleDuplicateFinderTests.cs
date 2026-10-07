using GedCore.Matching;

namespace GedCore.Tests;

public class PossibleDuplicateFinderTests
{
    static PersonMatchCandidate Person(
        string id, string given, string surname, int? birthYear = 1852, bool? isMale = true, string? father = null) => new(
        id, $"{given} {surname}", PersonNameNormalizer.Normalize(surname), PersonNameNormalizer.Normalize(given), isMale,
        birthYear is null ? null : new PersonMatchEvent(birthYear, PersonNameNormalizer.Normalize("Missouri")), null,
        father is null ? null : new PersonMatchParents(PersonNameNormalizer.Normalize(father), null), []);

    [Fact]
    public void Find_SameNameAndBirth_ReportsOnePairInIdOrder()
    {
        var pairs = PossibleDuplicateFinder.Find([Person("@I2@", "William", "Ashworth"), Person("@I1@", "William", "Ashworth")]);

        var pair = Assert.Single(pairs);
        Assert.Equal(("@I1@", "@I2@"), (pair.FirstId, pair.SecondId));
        Assert.True(pair.Score >= PossibleDuplicateFinder.ScoreFloor);
    }

    [Fact]
    public void Find_DifferentSurnames_ReportsNothing()
    {
        Assert.Empty(PossibleDuplicateFinder.Find([Person("@I1@", "William", "Ashworth"), Person("@I2@", "William", "Bell")]));
    }

    [Fact]
    public void Find_SexMismatch_ReportsNothing()
    {
        Assert.Empty(PossibleDuplicateFinder.Find(
            [Person("@I1@", "Alex", "Ashworth", isMale: true), Person("@I2@", "Alex", "Ashworth", isMale: false)]));
    }

    [Fact]
    public void Find_ScopeNamingNeitherMember_ReportsNothing()
    {
        var people = new[] { Person("@I1@", "William", "Ashworth"), Person("@I2@", "William", "Ashworth"), Person("@I3@", "Mary", "Bell") };

        Assert.Empty(PossibleDuplicateFinder.Find(people, new HashSet<string> { "@I3@" }));
    }

    [Fact]
    public void Find_ScopeNamingOneMember_ReportsThePair()
    {
        var people = new[] { Person("@I1@", "William", "Ashworth"), Person("@I2@", "William", "Ashworth") };

        Assert.Single(PossibleDuplicateFinder.Find(people, new HashSet<string> { "@I2@" }));
    }

    [Fact]
    public void Find_CancelledToken_Throws()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var people = new[] { Person("@I1@", "William", "Ashworth"), Person("@I2@", "William", "Ashworth") };

        Assert.Throws<OperationCanceledException>(() => PossibleDuplicateFinder.Find(people, null, cts.Token));
    }

    [Fact]
    public void Find_TwoUnknownSurnamesWithNoRelativesRecorded_AreNotDuplicates()
    {
        Assert.Empty(PossibleDuplicateFinder.Find(
            [Person("@I1@", "Mary", "Unknown", isMale: false), Person("@I2@", "Mary", "Unknown", isMale: false)]));
    }

    [Fact]
    public void Find_UnknownSurnameWithNoRelative_IsNotPairedWithAKnownNameThatMatches()
    {
        Assert.Empty(PossibleDuplicateFinder.Find(
            [Person("@I1@", "Mary", "Unknown", isMale: false), Person("@I2@", "Mary", "Smith", isMale: false, father: "John Smith")]));
    }

    [Fact]
    public void Find_UnknownSurnameWithAgreeingFather_IsPairedWithTheKnownNameAcrossSurnameGroups()
    {
        var pairs = PossibleDuplicateFinder.Find(
            [Person("@I1@", "Mary", "Unknown", isMale: false, father: "John Smith"),
             Person("@I2@", "Mary", "Smith", isMale: false, father: "John Smith")]);

        var pair = Assert.Single(pairs);
        Assert.Equal(("@I1@", "@I2@"), (pair.FirstId, pair.SecondId));
        Assert.True(pair.Score >= PossibleDuplicateFinder.WildcardScoreFloor);
    }

    [Fact]
    public void Find_KnownSurnameQueriedFirst_StillFindsTheUnknownSurnamePerson()
    {
        var people = new[]
        {
            Person("@I1@", "Mary", "Unknown", isMale: false, father: "John Smith"),
            Person("@I2@", "Mary", "Smith", isMale: false, father: "John Smith"),
        };

        Assert.Single(PossibleDuplicateFinder.Find(people, new HashSet<string> { "@I2@" }));
    }

    [Fact]
    public void Find_UnknownInBothParts_IsNeverPaired()
    {
        Assert.Empty(PossibleDuplicateFinder.Find(
            [Person("@I1@", "Unknown", "Unknown", father: "John Smith"), Person("@I2@", "Unknown", "Unknown", father: "John Smith")]));
    }
}
