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

    static PersonMatchCandidate MarriedWoman(string id, string surname, string spouse, int birthYear) => new(
        id, $"Mary {surname}", PersonNameNormalizer.Normalize(surname), "MARY", false,
        new PersonMatchEvent(birthYear, null), null, null,
        [new PersonMatchMarriage(PersonNameNormalizer.Normalize(spouse), null, null)]);

    [Fact]
    public void Find_UnknownSurnameWhoseSpouseHasADifferentGivenName_IsNotPaired()
    {
        var pairs = PossibleDuplicateFinder.Find(
            [MarriedWoman("@I1@", "Unknown", "Jabez Morrill", 1759), MarriedWoman("@I2@", "Clapp", "James Morrill", 1759)]);

        Assert.Empty(pairs);
    }

    [Fact]
    public void Find_UnknownSurnameWhoseSpouseIsTheSameMan_IsPaired()
    {
        var pairs = PossibleDuplicateFinder.Find(
            [MarriedWoman("@I1@", "Unknown", "James Morrill", 1759), MarriedWoman("@I2@", "Clapp", "James Morrill", 1759)]);

        Assert.Single(pairs);
    }

    // ---- GEN301 precision: birth decades and given-name tokens temper name similarity ----

    [Fact]
    public void Find_SameNameBornMoreThanAGenerationApart_IsNotPaired()
    {
        // A father/son namesake pair: identical names, births 35 years apart.
        Assert.Empty(PossibleDuplicateFinder.Find(
            [Person("@I1@", "William", "Ashworth", birthYear: 1850), Person("@I2@", "William", "Ashworth", birthYear: 1885)]));
    }

    [Fact]
    public void Find_SameNameBornThreeYearsApart_StillWarns()
    {
        Assert.Single(PossibleDuplicateFinder.Find(
            [Person("@I1@", "William", "Ashworth", birthYear: 1850), Person("@I2@", "William", "Ashworth", birthYear: 1853)]));
    }

    [Fact]
    public void Find_BirthsExactlyAtTheGapLimit_StillCompare()
    {
        Assert.Single(PossibleDuplicateFinder.Find(
            [Person("@I1@", "William", "Ashworth", birthYear: 1850),
             Person("@I2@", "William", "Ashworth", birthYear: 1850 + PossibleDuplicateFinder.MaxVitalYearGap)]));
    }

    static PersonMatchCandidate Died(string id, string given, int deathYear, int? birthYear = null) => new(
        id, $"{given} Ashworth", PersonNameNormalizer.Normalize("Ashworth"), PersonNameNormalizer.Normalize(given), true,
        birthYear is null ? null : new PersonMatchEvent(birthYear, null),
        new PersonMatchEvent(deathYear, PersonNameNormalizer.Normalize("Missouri")),
        new PersonMatchParents(PersonNameNormalizer.Normalize("Moses Ashworth"), null), []);

    [Fact]
    public void Find_DeathsMoreThanAGenerationApart_AreNotPaired()
    {
        // Neither carries a birth, so only the recorded deaths can separate them.
        Assert.Empty(PossibleDuplicateFinder.Find([Died("@I1@", "Barney", 1869), Died("@I2@", "Barnes", 1815)]));
    }

    [Fact]
    public void Find_DeathsCloseTogether_StillCompare()
    {
        Assert.Single(PossibleDuplicateFinder.Find([Died("@I1@", "Barney", 1869), Died("@I2@", "Barnes", 1866)]));
    }

    [Fact]
    public void Find_GivenNamesSharingNoTokenOrInitial_AreNotPaired()
    {
        // Ella and Bella score high on letters alone yet share no token, initial or documented nickname.
        Assert.Empty(PossibleDuplicateFinder.Find(
            [Person("@I1@", "Ella", "Ashworth", isMale: false), Person("@I2@", "Bella", "Ashworth", isMale: false)]));
    }

    [Fact]
    public void Find_GivenNamesSharingABareInitial_AreStillComparable()
    {
        // "Ella M" carries the bare initial M that "Mabel Ella"'s given names share.
        Assert.Single(PossibleDuplicateFinder.Find(
            [Person("@I1@", "Ella M", "Ashworth", isMale: false), Person("@I2@", "Ella Mabel", "Ashworth", isMale: false)]));
    }
}
