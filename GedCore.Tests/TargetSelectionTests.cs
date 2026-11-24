using System.Text.Json;
using GedCore.Ged55;
using GedFire.Gen;
using GedFire.TargetSelection;

namespace GedCore.Tests;

// ---------------------------------------------------------------------------
// ExactnessRules — exact date, exact place, era weight, geography weight
// ---------------------------------------------------------------------------

public class ExactnessRulesTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("15 MAR 1802", true)]
    [InlineData("MAR 1802", false)]
    [InlineData("1802", false)]
    [InlineData("ABT 1802", false)]
    [InlineData("BET 1850 AND 1860", false)]
    [InlineData("FROM 1850 TO 1860", false)]
    [InlineData("2 FEB 1681/82", true)]
    [InlineData("32 MAR 1802", false)]      // out-of-range day
    [InlineData("15 XYZ 1802", false)]      // not a month
    public void IsExactDate(string? date, bool expected) =>
        Assert.Equal(expected, ExactnessRules.IsExactDate(date));

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("Boston, Massachusetts", true)]
    [InlineData("Boston, Massachusetts, United States", true)]
    [InlineData("Massachusetts", false)]
    [InlineData("Boston", false)]
    [InlineData("Canada", false)]
    public void IsExactPlace(string? place, bool expected) =>
        Assert.Equal(expected, ExactnessRules.IsExactPlace(place));

    [Theory]
    [InlineData(null, 0)]
    [InlineData(0, 0)]
    [InlineData(1950, 0)]
    [InlineData(1900, 0)]
    [InlineData(1899, 1)]
    [InlineData(1800, 1)]
    [InlineData(1799, 2)]
    [InlineData(1700, 2)]
    [InlineData(1699, 3)]
    public void EraWeight(int? year, int expected) =>
        Assert.Equal(expected, ExactnessRules.EraWeight(year));

    [Theory]
    [InlineData(null, 0)]
    [InlineData("", 0)]
    [InlineData("Boston, Massachusetts", 0)]
    [InlineData("Danville, Quebec, Canada", 0)]
    [InlineData("London, England", 1)]
    [InlineData("Paris, France", 2)]
    [InlineData("Tokyo, Japan", 3)]
    public void GeographyWeight(string? place, int expected) =>
        Assert.Equal(expected, ExactnessRules.GeographyWeight(place));
}

// ---------------------------------------------------------------------------
// DifficultyScore — band and bonus from the three component weights
// ---------------------------------------------------------------------------

public class DifficultyScoreTests
{
    [Theory]
    [InlineData(0, 0, 0, DifficultyBand.Common, 0)]
    [InlineData(1, 0, 0, DifficultyBand.Uncommon, 5)]
    [InlineData(0, 2, 0, DifficultyBand.Uncommon, 5)]
    [InlineData(2, 1, 0, DifficultyBand.Rare, 10)]
    [InlineData(3, 1, 0, DifficultyBand.Rare, 10)]
    [InlineData(3, 3, 1, DifficultyBand.Legendary, 15)]
    [InlineData(3, 3, 0, DifficultyBand.Legendary, 15)]
    public void ComputeBandsAndBonus(int era, int geo, int context, DifficultyBand band, int bonus)
    {
        var score = DifficultyScore.Compute(era, geo, context);
        Assert.Equal(band, score.Band);
        Assert.Equal(bonus, score.Bonus);
        Assert.Equal(era + geo + context, score.RawDifficulty);
    }
}

// ---------------------------------------------------------------------------
// GapDetector — synthetic-GEDCOM detection per card type
// ---------------------------------------------------------------------------

public class GapDetectorTests
{
    [Theory]
    [InlineData(false, "")]
    [InlineData(true, "")]
    [InlineData(false, "ABT 1872")]
    [InlineData(true, "ABT 1872")]
    [InlineData(false, "EST 1850")]
    [InlineData(true, "EST 1850")]
    [InlineData(false, "BEF 1900")]
    [InlineData(true, "BEF 1900")]
    [InlineData(false, "1872")]
    [InlineData(true, "1872")]
    public void BareBonesSubject_OnlyOffersEnrichment(bool explicitPeople, string birthDate)
    {
        string ged = $"""
            0 @I1@ INDI
            1 NAME Ada /Ashworth/
            1 BIRT
            2 DATE {birthDate}
            """;
        var model = ModelBuilder.Build(Ged55Parser.Parse(ged));
        var suppressed = new List<SuppressedCard>();

        var targets = explicitPeople
            ? GapDetector.DetectForPeople(model.Individuals.Values, suppressed)
            : GapDetector.Detect(model, ["Ashworth"], 2026, suppressed);

        Assert.Equal("Enrich person", Assert.Single(targets).CardType);
        Assert.Equal(["New parent", "New spouse"], suppressed.Select(card => card.CardType));
        Assert.All(suppressed, card =>
        {
            Assert.Equal("@I1@", card.Xref);
            Assert.Equal("Ada Ashworth", card.Name);
            Assert.Equal("bare-bones subject \u2014 enrich first", card.Reason);
        });
    }

    [Theory]
    [InlineData("BIRT", "4 JUL 1872", "")]
    [InlineData("BIRT", "ABT 1872", "Penobscot, Maine")]
    [InlineData("BIRT", "ABT 1872", "Maine")]
    [InlineData("CHR", "5 JUL 1872", "")]
    [InlineData("BAPM", "5 JUL 1872", "")]
    [InlineData("DEAT", "4 JUL 1900", "")]
    [InlineData("BURI", "5 JUL 1900", "")]
    [InlineData("CHR", "", "Maine")]
    [InlineData("BAPM", "", "Maine")]
    [InlineData("DEAT", "", "Maine")]
    [InlineData("BURI", "", "Maine")]
    public void OwnVitalAnchor_RetainsRelationshipCards(string tag, string date, string place)
    {
        string ged = $"""
            0 @I1@ INDI
            1 NAME Ada /Ashworth/
            1 {tag}
            2 DATE {date}
            2 PLAC {place}
            """;
        var model = ModelBuilder.Build(Ged55Parser.Parse(ged));
        foreach (bool explicitPeople in new[] { false, true })
        {
            var suppressed = new List<SuppressedCard>();
            var targets = explicitPeople
                ? GapDetector.DetectForPeople(model.Individuals.Values, suppressed)
                : GapDetector.Detect(model, ["Ashworth"], 2026, suppressed);

            Assert.Contains(targets, card => card.CardType == "New parent");
            Assert.Contains(targets, card => card.CardType == "New spouse");
            Assert.Empty(suppressed);
        }
    }

    [Theory]
    [InlineData("", "", false, false)]
    [InlineData("ABT 1874", "", false, false)]
    [InlineData("1874", "", false, false)]
    [InlineData("5 NOV 1874", "", false, true)]
    [InlineData("", "Maine", false, true)]
    [InlineData("", "", true, true)]
    public void NewChild_UsesBothSpousesAndMarriageAnchors(string date, string place, bool spouseAnchored, bool expectedChild)
    {
        string ged = $"""
            0 @I1@ INDI
            1 NAME Adam /Ashworth/
            1 FAMS @F1@
            0 @I2@ INDI
            1 NAME Ada /Ashworth/
            1 BIRT
            2 PLAC {(spouseAnchored ? "Maine" : "")}
            1 FAMS @F1@
            0 @F1@ FAM
            1 HUSB @I1@
            1 WIFE @I2@
            1 MARR
            2 DATE {date}
            2 PLAC {place}
            """;
        var model = ModelBuilder.Build(Ged55Parser.Parse(ged));
        foreach (bool explicitPeople in new[] { false, true })
        {
            var suppressed = new List<SuppressedCard>();
            var targets = explicitPeople
                ? GapDetector.DetectForPeople(model.Individuals.Values, suppressed)
                : GapDetector.Detect(model, ["Ashworth"], 2026, suppressed);

            Assert.Equal(expectedChild ? 1 : 0, targets.Count(card => card.CardType == "New child"));
            Assert.Equal(expectedChild ? 0 : 1, suppressed.Count(card => card.CardType == "New child"));
            Assert.Contains(suppressed, card => card.Xref == "@I1@" && card.CardType == "New parent");
            if (!expectedChild)
                Assert.Contains("bare-bones family", Assert.Single(suppressed, card => card.CardType == "New child").Reason);
        }
    }

    [Fact]
    public void BareBonesGate_ReevaluatesAfterAnAnchorLands()
    {
        var person = new GedIndividual { Xref = "@I1@", FirstName = "Ada", LastNameRaw = "Ashworth" };
        Assert.Equal("Enrich person", Assert.Single(GapDetector.DetectForPeople([person])).CardType);

        person.Birth = new GedEvent { Date = "4 JUL 1872" };

        var suppressed = new List<SuppressedCard>();
        Assert.Equal(3, GapDetector.DetectForPeople([person], suppressed).Count);
        Assert.Empty(suppressed);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NameOnlyChild_WithBothParents_OnlyOffersEnrichment(bool explicitPeople)
    {
        const string ged = """
            0 @I1@ INDI
            1 NAME Ada /Ashworth/
            1 FAMC @F1@
            0 @I2@ INDI
            1 NAME Adam /Ashworth/
            0 @I3@ INDI
            1 NAME Amy /Other/
            0 @F1@ FAM
            1 HUSB @I2@
            1 WIFE @I3@
            1 CHIL @I1@
            """;
        var model = ModelBuilder.Build(Ged55Parser.Parse(ged));
        var suppressed = new List<SuppressedCard>();

        var targets = explicitPeople
            ? GapDetector.DetectForPeople([model.Individuals["@I1@"]], suppressed)
            : GapDetector.Detect(model, ["Ashworth"], 2026, suppressed).Where(card => card.Xref == "@I1@");

        Assert.Equal("Enrich person", Assert.Single(targets).CardType);
        Assert.Equal("New spouse", Assert.Single(suppressed, card => card.Xref == "@I1@").CardType);
    }

    [Fact]
    public void BareBonesGate_DoesNotReportIneligibleCardsOrUseRelativesAndOtherFactsAsAnchors()
    {
        const string ged = """
            0 @I1@ INDI
            1 NAME Adam /Ashworth/
            1 BIRT
            2 DATE ABT 1850
            1 DEAT
            2 DATE BEF 1860
            1 NOTE Known household
            1 CENS
            2 DATE 1 JUN 1855
            2 PLAC Maine
            1 FAMC @F1@
            0 @I2@ INDI
            1 NAME Ada /Ashworth/
            1 BIRT
            2 DATE 4 JUL 1820
            0 @I3@ INDI
            1 NAME Amy /Ashworth/
            1 BIRT
            2 DATE ABT 2000
            0 @I4@ INDI
            1 NAME Ann /Other/
            0 @F1@ FAM
            1 WIFE @I2@
            1 CHIL @I1@
            """;
        var model = ModelBuilder.Build(Ged55Parser.Parse(ged));
        var suppressed = new List<SuppressedCard>();

        GapDetector.Detect(model, ["Ashworth"], 2026, suppressed);

        var card = Assert.Single(suppressed);
        Assert.Equal("@I1@", card.Xref);
        Assert.Equal("New parent", card.CardType);
    }

    static List<SelectionTarget> Detect(string gedText, params string[] surnames) =>
        GapDetector.Detect(ModelBuilder.Build(Ged55Parser.Parse(gedText)), surnames);

    static List<SelectionTarget> Detect(string gedText, int currentYear, params string[] surnames) =>
        GapDetector.Detect(ModelBuilder.Build(Ged55Parser.Parse(gedText)), surnames, currentYear);

    [Fact]
    public void NewParent_NoRecordedParents_Detected()
    {
        const string ged = """
            0 @I1@ INDI
            1 NAME William /Morrill/
            1 SEX M
            1 BIRT
            2 DATE 1852
            2 PLAC Missouri
            """;
        var targets = Detect(ged, "Morrill");
        var target = Assert.Single(targets, t => t.CardType == "New parent");
        Assert.Equal(10, target.NominalPoints);
        Assert.Null(target.KnownParent);
    }

    [Fact]
    public void NewParent_OneRecordedParent_CarriesKnownParent()
    {
        const string ged = """
            0 @I1@ INDI
            1 NAME John /Morrill/
            1 SEX M
            1 BIRT
            2 PLAC Maine
            1 FAMC @F1@
            0 @I2@ INDI
            1 NAME Mary /Smith/
            1 SEX F
            0 @F1@ FAM
            1 WIFE @I2@
            1 CHIL @I1@
            """;
        var targets = Detect(ged, "Morrill");
        var target = Assert.Single(targets, t => t.CardType == "New parent");
        Assert.NotNull(target.KnownParent);
        Assert.Equal("@I2@", target.KnownParent!.Xref);
    }

    [Fact]
    public void NewParent_TwoRecordedParents_NotDetected()
    {
        const string ged = """
            0 @I1@ INDI
            1 NAME John /Morrill/
            1 SEX M
            1 FAMC @F1@
            0 @I2@ INDI
            1 NAME Robert /Morrill/
            1 SEX M
            0 @I3@ INDI
            1 NAME Mary /Smith/
            1 SEX F
            0 @F1@ FAM
            1 HUSB @I2@
            1 WIFE @I3@
            1 CHIL @I1@
            """;
        var targets = Detect(ged, "Morrill");
        Assert.DoesNotContain(targets, t => t.CardType == "New parent" && t.Xref == "@I1@");
    }

    [Fact]
    public void NewSpouse_NoFamilies_Detected()
    {
        const string ged = """
            0 @I1@ INDI
            1 NAME Abigail /Morrill/
            1 SEX F
            1 BIRT
            2 DATE 1852
            2 PLAC Maine
            """;
        var targets = Detect(ged, "Morrill");
        var target = Assert.Single(targets, t => t.CardType == "New spouse");
        Assert.Equal(5, target.NominalPoints);
    }

    [Fact]
    public void NewSpouse_DiedYoung_NotDetected()
    {
        const string ged = """
            0 @I1@ INDI
            1 NAME Abigail /Morrill/
            1 SEX F
            1 BIRT
            2 DATE 1852
            1 DEAT
            2 DATE 1857
            """;
        var targets = Detect(ged, "Morrill");
        Assert.DoesNotContain(targets, t => t.CardType == "New spouse");
    }

    [Fact]
    public void NewSpouse_DiedOlderThanSeventeen_StillDetected()
    {
        const string ged = """
            0 @I1@ INDI
            1 NAME Abigail /Morrill/
            1 SEX F
            1 BIRT
            2 DATE 1852
            2 PLAC Maine
            1 DEAT
            2 DATE 1900
            """;
        var targets = Detect(ged, "Morrill");
        Assert.Contains(targets, t => t.CardType == "New spouse");
    }

    [Fact]
    public void NewChild_FamilyWithNoChildren_DetectedOncePerFamily()
    {
        const string ged = """
            0 @I1@ INDI
            1 NAME John /Morrill/
            1 SEX M
            1 FAMS @F1@
            0 @I2@ INDI
            1 NAME Mary /Morrill/
            1 SEX F
            1 FAMS @F1@
            0 @F1@ FAM
            1 HUSB @I1@
            1 WIFE @I2@
            1 MARR
            2 DATE 5 NOV 1820
            """;
        // Both spouses carry the target surname, so both pass the filter —
        // the family-level gap must still surface exactly once.
        var targets = Detect(ged, "Morrill");
        var childTargets = targets.Where(t => t.CardType == "New child").ToList();
        Assert.Single(childTargets);
        Assert.Equal("@F1@", childTargets[0].SpouseFamily!.FamilyXref);
    }

    [Fact]
    public void NewChild_NotOfferedWhenTheHusbandDoesNotBearTheSurname()
    {
        const string ged = """
            0 @I1@ INDI
            1 NAME John /Smith/
            1 SEX M
            1 FAMS @F1@
            0 @I2@ INDI
            1 NAME Mary /Morrill/
            1 SEX F
            1 FAMS @F1@
            0 @F1@ FAM
            1 HUSB @I1@
            1 WIFE @I2@
            """;
        Assert.DoesNotContain(Detect(ged, "Morrill"), t => t.CardType == "New child");
    }

    [Fact]
    public void NewChild_NotOfferedWhenTheFamilyHasNoHusband()
    {
        const string ged = """
            0 @I2@ INDI
            1 NAME Mary /Morrill/
            1 SEX F
            1 FAMS @F1@
            0 @F1@ FAM
            1 WIFE @I2@
            """;
        Assert.DoesNotContain(Detect(ged, "Morrill"), t => t.CardType == "New child");
    }

    [Fact]
    public void NewChild_StillOfferedForAMorrillHusbandWithAMarriedInWife()
    {
        const string ged = """
            0 @I1@ INDI
            1 NAME John /Morrill/
            1 SEX M
            1 BIRT
            2 PLAC Maine
            1 FAMS @F1@
            0 @I2@ INDI
            1 NAME Mary /Smith/
            1 SEX F
            1 FAMS @F1@
            0 @F1@ FAM
            1 HUSB @I1@
            1 WIFE @I2@
            """;
        Assert.Single(Detect(ged, "Morrill"), t => t.CardType == "New child");
    }

    [Fact]
    public void NewChild_FamilyWithChildren_NotDetected()
    {
        const string ged = """
            0 @I1@ INDI
            1 NAME John /Morrill/
            1 SEX M
            1 FAMS @F1@
            0 @I2@ INDI
            1 NAME Mary /Morrill/
            1 SEX F
            1 FAMS @F1@
            0 @I3@ INDI
            1 NAME Child /Morrill/
            1 SEX M
            0 @F1@ FAM
            1 HUSB @I1@
            1 WIFE @I2@
            1 CHIL @I3@
            """;
        var targets = Detect(ged, "Morrill");
        Assert.DoesNotContain(targets, t => t.CardType == "New child");
    }

    // Mary's daughter Ruth is from her earlier Smith marriage; F1 is her childless marriage to John Morrill.
    static string RemarriedWife(string childBirth, string marriage = "2 DATE 1912") => $"""
        0 @I1@ INDI
        1 NAME John /Morrill/
        1 SEX M
        1 BIRT
        2 PLAC Duluth, Minnesota
        1 CENS
        2 DATE 1920
        1 FAMS @F1@
        0 @I2@ INDI
        1 NAME Mary /Smith/
        1 SEX F
        1 CENS
        2 DATE 1920
        1 FAMS @F1@
        1 FAMS @F2@
        0 @I3@ INDI
        1 NAME Ruth /Smith/
        1 SEX F
        {childBirth}
        1 FAMC @F2@
        0 @F1@ FAM
        1 HUSB @I1@
        1 WIFE @I2@
        1 MARR
        {marriage}
        0 @F2@ FAM
        1 WIFE @I2@
        1 CHIL @I3@
        """;

    static (List<SelectionTarget> Targets, List<SuppressedCard> Suppressed) DetectWithSuppressed(string ged)
    {
        var suppressed = new List<SuppressedCard>();
        var targets = GapDetector.Detect(ModelBuilder.Build(Ged55Parser.Parse(ged)), ["Morrill"], 2026, suppressed);
        return (targets, suppressed);
    }

    [Theory]
    [InlineData("2 DATE 1912")]
    [InlineData("2 PLAC Duluth, Minnesota")]
    public void NewChild_SuppressedWhenTheWifesOnlyChildPredatesTheMarriage(string marriage)
    {
        var (targets, suppressed) = DetectWithSuppressed(RemarriedWife("1 BIRT\n2 DATE ABT 1909", marriage));

        Assert.DoesNotContain(targets, t => t.CardType == "New child");
        var card = Assert.Single(suppressed, s => s.CardType == "New child");
        Assert.Equal("the wife's known children were all born before this marriage", card.Reason);
    }

    [Fact]
    public void NewChild_KeptWhenAChildWasBornAfterTheMarriage()
    {
        var (targets, suppressed) = DetectWithSuppressed(RemarriedWife("1 BIRT\n2 DATE 1914"));

        Assert.Single(targets, t => t.CardType == "New child");
        Assert.DoesNotContain(suppressed, s => s.CardType == "New child");
    }

    [Fact]
    public void NewChild_KeptWhenTheChildCannotBeDated()
    {
        var (targets, _) = DetectWithSuppressed(RemarriedWife("1 BIRT\n2 PLAC Duluth, Minnesota"));

        Assert.Single(targets, t => t.CardType == "New child");
    }

    [Fact]
    public void NewChild_SuppressedWhenAnUndatedChildWasEnumeratedBeforeTheMarriage()
    {
        var (targets, suppressed) = DetectWithSuppressed(RemarriedWife("1 CENS\n2 DATE 1905"));

        Assert.DoesNotContain(targets, t => t.CardType == "New child");
        var card = Assert.Single(suppressed, s => s.CardType == "New child");
        Assert.Equal("the wife's known children were all born before this marriage", card.Reason);
    }

    [Fact]
    public void NewChild_KeptWhenAnUndatedChildsOnlyCensusFollowsTheMarriage()
    {
        var (targets, suppressed) = DetectWithSuppressed(RemarriedWife("1 CENS\n2 DATE 1920"));

        Assert.Single(targets, t => t.CardType == "New child");
        Assert.DoesNotContain(suppressed, s => s.CardType == "New child");
    }

    [Fact]
    public void NewChild_KeptWhenTheCoupleCannotBeDated()
    {
        var (targets, _) = DetectWithSuppressed(RemarriedWife("1 BIRT\n2 DATE 1909", marriage: "2 PLAC Duluth, Minnesota")
            .Replace("1 CENS", "1 NOTE"));

        Assert.Single(targets, t => t.CardType == "New child");
    }

    // John Morrill and Mary Smith's family F1; each argument is GEDCOM placed under her record or the family's.
    static string LateCouple(string wife, string family = "", string marriage = "2 DATE 17 MAR 1942") => $"""
        0 @I1@ INDI
        1 NAME John /Morrill/
        1 SEX M
        1 BIRT
        2 PLAC Duluth, Minnesota
        1 FAMS @F1@
        0 @I2@ INDI
        1 NAME Mary /Smith/
        1 SEX F
        {wife}
        1 FAMS @F1@
        0 @F1@ FAM
        1 HUSB @I1@
        1 WIFE @I2@
        1 MARR
        {marriage}
        {family}
        """;

    static (List<SelectionTarget> Targets, List<SuppressedCard> Suppressed) DetectPeople(string ged, GapRules? rules = null)
    {
        var suppressed = new List<SuppressedCard>();
        var model = ModelBuilder.Build(Ged55Parser.Parse(ged));
        return (GapDetector.DetectForPeople([model.Individuals["@I1@"]], suppressed, rules), suppressed);
    }

    [Fact]
    public void NewChild_WithheldWhenTheWifeWasPastChildbearingAgeAtTheMarriage()
    {
        var (targets, suppressed) = DetectWithSuppressed(LateCouple("1 BIRT\n2 DATE 3 MAY 1877"));

        Assert.DoesNotContain(targets, t => t.CardType == "New child");
        var card = Assert.Single(suppressed, s => s.CardType == "New child");
        Assert.Equal("@I1@", card.Xref);
        Assert.Equal("wife aged 64 at marriage (born 3 MAY 1877, married 17 MAR 1942), at or past maxMotherAgeAtMarriage 50", card.Reason);
    }

    [Fact]
    public void NewChild_WithheldReasonGivesTheYoungestAgeAnEstimatedBirthAllows()
    {
        var (_, suppressed) = DetectWithSuppressed(LateCouple("1 BIRT\n2 DATE ABT 1878"));

        Assert.Equal("wife aged about 64 at marriage, at least 61 (born ABT 1878, married 17 MAR 1942), at or past maxMotherAgeAtMarriage 50",
            Assert.Single(suppressed, s => s.CardType == "New child").Reason);
    }

    [Theory]
    [InlineData("1 BIRT\n2 DATE 1 JAN 1898")]
    [InlineData("1 BIRT\n2 DATE BET 1880 AND 1892")]
    [InlineData("1 BIRT\n2 DATE ABT 1894")]
    [InlineData("1 BIRT\n2 DATE AFT 1880")]
    [InlineData("1 BIRT\n2 PLAC Duluth, Minnesota")]
    [InlineData("1 NOTE no birth recorded")]
    public void NewChild_DrawnWhenSomeReadingLeavesTheWifeUnderTheLimit(string wife)
    {
        var (targets, suppressed) = DetectWithSuppressed(LateCouple(wife));

        Assert.Single(targets, t => t.CardType == "New child");
        Assert.DoesNotContain(suppressed, s => s.CardType == "New child");
    }

    [Fact]
    public void NewChild_DrawnWhenTheMarriageDateIsUnknown()
    {
        var (targets, _) = DetectWithSuppressed(LateCouple("1 BIRT\n2 DATE 1877", marriage: "2 PLAC Duluth, Minnesota"));

        Assert.Single(targets, t => t.CardType == "New child");
    }

    [Fact]
    public void NewChild_MaxMotherAgeAtMarriageMovesTheLimit()
    {
        string ged = LateCouple("1 BIRT\n2 DATE 1 JAN 1898");

        var (targets, suppressed) = DetectPeople(ged, new GapRules { NewChild = new NewChildLimits(MaxMotherAgeAtMarriage: 44) });

        Assert.DoesNotContain(targets, t => t.CardType == "New child");
        Assert.EndsWith("at or past maxMotherAgeAtMarriage 44", Assert.Single(suppressed, s => s.CardType == "New child").Reason);
    }

    [Theory]
    [InlineData("1 DEAT\n2 DATE 17 SEP 1942", "", "marriage lasted at most 6 months (married 17 MAR 1942, wife died 17 SEP 1942), under minMarriageYears 1")]
    [InlineData("1 BURI\n2 DATE 1 OCT 1942", "", "marriage lasted at most 6 months (married 17 MAR 1942, wife buried 1 OCT 1942), under minMarriageYears 1")]
    [InlineData("1 DEAT\n2 DATE 1942", "", "marriage lasted at most 9 months (married 17 MAR 1942, wife died 1942), under minMarriageYears 1")]
    [InlineData("", "1 DIV\n2 DATE 2 APR 1942", "marriage lasted less than a month (married 17 MAR 1942, divorced 2 APR 1942), under minMarriageYears 1")]
    public void NewChild_WithheldWhenTheMarriageEndedWithinAYear(string wifeEnd, string family, string reason)
    {
        var (targets, suppressed) = DetectWithSuppressed(LateCouple("1 BIRT\n2 DATE 1910\n" + wifeEnd, family));

        Assert.DoesNotContain(targets, t => t.CardType == "New child");
        Assert.Equal(reason, Assert.Single(suppressed, s => s.CardType == "New child").Reason);
    }

    [Theory]
    [InlineData("1 DEAT\n2 DATE 17 MAR 1945")]
    [InlineData("1 DEAT\n2 DATE BEF 1944")]
    [InlineData("1 DEAT\n2 DATE AFT 1 APR 1942")]
    [InlineData("1 DEAT\n2 DATE 1 JAN 1940")]
    public void NewChild_DrawnWhenNoEndingProvesTheMarriageShort(string wifeEnd)
    {
        var (targets, suppressed) = DetectWithSuppressed(LateCouple("1 BIRT\n2 DATE 1910\n" + wifeEnd));

        Assert.Single(targets, t => t.CardType == "New child");
        Assert.DoesNotContain(suppressed, s => s.CardType == "New child");
    }

    [Fact]
    public void NewChild_NotOfferedWhenTheFamilyAlreadyHasAChild()
    {
        string ged = LateCouple("1 BIRT\n2 DATE 1877", "1 CHIL @I3@") + "\n0 @I3@ INDI\n1 NAME Ruth /Morrill/\n1 FAMC @F1@";

        var (targets, suppressed) = DetectWithSuppressed(ged);

        Assert.DoesNotContain(targets, t => t.CardType == "New child");
        Assert.DoesNotContain(suppressed, s => s.CardType == "New child");
    }

    [Theory]
    [InlineData("1943")]
    [InlineData("AFT 1930")]
    public void NewChild_DrawnWhenAChildOfEitherSpouseMayHaveBeenBornAfterTheMarriage(string childBirth)
    {
        string ged = LateCouple("1 BIRT\n2 DATE 1877").ReplaceLineEndings("\n")
            .Replace("1 FAMS @F1@\n0 @I2@", "1 FAMS @F1@\n1 FAMS @F2@\n0 @I2@") + $"""

            0 @I3@ INDI
            1 NAME Ruth /Morrill/
            1 BIRT
            2 DATE {childBirth}
            1 FAMC @F2@
            0 @F2@ FAM
            1 HUSB @I1@
            1 CHIL @I3@
            """;

        var (targets, suppressed) = DetectWithSuppressed(ged);

        Assert.Single(targets, t => t.CardType == "New child");
        Assert.DoesNotContain(suppressed, s => s.CardType == "New child");
    }

    [Fact]
    public void NewChild_IncludeSuppressedDrawsAWithheldCard()
    {
        string ged = LateCouple("1 BIRT\n2 DATE 1877");

        var (targets, suppressed) = DetectPeople(ged, new GapRules { IncludeSuppressed = true });

        Assert.Single(targets, t => t.CardType == "New child");
        Assert.Empty(suppressed);
    }

    [Fact]
    public void EnrichPerson_InexactBirth_ReportsMissingDateAndPlace()
    {
        const string ged = """
            0 @I1@ INDI
            1 NAME William /Morrill/
            1 SEX M
            1 BIRT
            2 DATE ABT 1780
            """;
        var targets = Detect(ged, "Morrill");
        var target = Assert.Single(targets, t => t.CardType == "Enrich person");
        var birth = Assert.Single(target.Enrichments!, e => e.Fact == "Birth");
        Assert.True(birth.MissingDate);
        Assert.True(birth.MissingPlace);
    }

    [Fact]
    public void EnrichPerson_ExactBirth_NotDetected()
    {
        const string ged = """
            0 @I1@ INDI
            1 NAME William /Morrill/
            1 SEX M
            1 BIRT
            2 DATE 15 MAR 1802
            2 PLAC Boston, Massachusetts
            2 SOUR @S1@
            """;
        var targets = Detect(ged, "Morrill");
        var target = Assert.Single(targets, t => t.CardType == "Enrich person");
        Assert.DoesNotContain(target.Enrichments!, e => e.Fact == "Birth");
    }

    [Fact]
    public void EnrichPerson_DeathPlaceMissingOnly_ListsOnlyThePlace()
    {
        const string ged = """
            0 @I1@ INDI
            1 NAME William /Morrill/
            1 SEX M
            1 DEAT
            2 DATE 10 MAR 1854
            """;
        var targets = Detect(ged, "Morrill");
        var target = Assert.Single(targets, t => t.CardType == "Enrich person");
        var death = Assert.Single(target.Enrichments!, e => e.Fact == "Death");
        Assert.False(death.MissingDate);
        Assert.True(death.MissingPlace);
    }

    [Fact]
    public void EnrichPerson_ExactUncitedBirth_ReportsMissingCitationOnly()
    {
        const string ged = """
            0 @I1@ INDI
            1 NAME William /Morrill/
            1 SEX M
            1 BIRT
            2 DATE 15 MAR 1802
            2 PLAC Boston, Massachusetts
            """;
        var target = Assert.Single(Detect(ged, "Morrill"), t => t.CardType == "Enrich person");
        var birth = Assert.Single(target.Enrichments!, e => e.Fact == "Birth");
        Assert.False(birth.MissingDate);
        Assert.False(birth.MissingPlace);
        Assert.True(birth.MissingCitation);
    }

    [Fact]
    public void EnrichPerson_ABirthWithOnlyCommentary_StillMissesItsCitation()
    {
        const string ged = """
            0 @I1@ INDI
            1 NAME William /Morrill/
            1 SEX M
            1 BIRT
            2 DATE 15 MAR 1802
            2 PLAC Boston, Massachusetts
            2 SOUR @VOID@
            3 NOTE The family Bible is lost.
            """;
        var target = Assert.Single(Detect(ged, "Morrill"), t => t.CardType == "Enrich person");
        Assert.True(Assert.Single(target.Enrichments!, e => e.Fact == "Birth").MissingCitation);
    }

    [Fact]
    public void EnrichPerson_ExactDateOnlyUncited_AlsoReportsMissingCitation()
    {
        const string ged = """
            0 @I1@ INDI
            1 NAME William /Morrill/
            1 SEX M
            1 BIRT
            2 DATE 15 MAR 1802
            """;
        var target = Assert.Single(Detect(ged, "Morrill"), t => t.CardType == "Enrich person");
        var birth = Assert.Single(target.Enrichments!, e => e.Fact == "Birth");
        Assert.True(birth.MissingPlace);
        Assert.True(birth.MissingCitation);
    }

    [Fact]
    public void EnrichPerson_InexactUncitedBirth_AlsoMissesItsCitation()
    {
        const string ged = """
            0 @I1@ INDI
            1 NAME William /Morrill/
            1 SEX M
            1 BIRT
            2 DATE ABT 1780
            """;
        var target = Assert.Single(Detect(ged, "Morrill"), t => t.CardType == "Enrich person");
        Assert.True(Assert.Single(target.Enrichments!, e => e.Fact == "Birth").MissingCitation);
    }

    [Fact]
    public void EnrichPerson_CitationOnTheDateOrThePlace_CountsAsCited()
    {
        const string ged = """
            0 @I1@ INDI
            1 NAME William /Morrill/
            1 SEX M
            1 BIRT
            2 DATE 15 MAR 1802
            3 SOUR @S1@
            2 PLAC Boston, Massachusetts
            """;
        var target = Assert.Single(Detect(ged, "Morrill"), t => t.CardType == "Enrich person");
        Assert.DoesNotContain(target.Enrichments!, e => e.Fact == "Birth");
    }

    [Fact]
    public void EnrichPerson_PersonalNoteSource_DoesNotCiteTheFact()
    {
        const string ged = """
            0 @I1@ INDI
            1 NAME William /Morrill/
            1 SEX M
            1 BIRT
            2 DATE 15 MAR 1802
            2 PLAC Boston, Massachusetts
            2 SOUR @S1@
            3 DATA
            4 TEXT NOCITATION: TRUE|A family recollection.
            """;
        var target = Assert.Single(Detect(ged, "Morrill"), t => t.CardType == "Enrich person");
        Assert.True(Assert.Single(target.Enrichments!, e => e.Fact == "Birth").MissingCitation);
    }

    [Fact]
    public void EnrichPerson_UncitedDeathSatisfiedByAnExactBurial_IsNotAGap()
    {
        const string ged = """
            0 @I1@ INDI
            1 NAME William /Morrill/
            1 SEX M
            1 BIRT
            2 DATE 1 JAN 1850
            2 PLAC Boston, Massachusetts
            2 SOUR @S1@
            1 DEAT
            2 DATE 8 FEB 1937
            2 PLAC Boston, Massachusetts
            1 BURI
            2 DATE 8 FEB 1937
            2 PLAC Boston, Massachusetts
            """;
        Assert.DoesNotContain(Detect(ged, "Morrill"), t => t.CardType == "Enrich person");
    }

    [Fact]
    public void EnrichPerson_ExactBurialWithNoDeath_ClosesTheDeathGap()
    {
        const string ged = """
            0 @I1@ INDI
            1 NAME William /Morrill/
            1 SEX M
            1 BIRT
            2 DATE 1 JAN 1850
            2 PLAC Boston, Massachusetts
            2 SOUR @S1@
            1 BURI
            2 DATE 8 FEB 1937
            2 PLAC Boston, Massachusetts
            """;
        var targets = Detect(ged, "Morrill");
        Assert.DoesNotContain(targets, t => t.CardType == "Enrich person");
    }

    [Fact]
    public void EnrichPerson_ExactBurialBesideAnInexactDeath_ClosesTheDeathGap()
    {
        const string ged = """
            0 @I1@ INDI
            1 NAME William /Morrill/
            1 SEX M
            1 BIRT
            2 DATE 1 JAN 1850
            2 PLAC Boston, Massachusetts
            2 SOUR @S1@
            1 DEAT
            2 DATE 1937
            1 BURI
            2 DATE 8 FEB 1937
            2 PLAC Boston, Massachusetts
            """;
        var targets = Detect(ged, "Morrill");
        Assert.DoesNotContain(targets, t => t.CardType == "Enrich person");
    }

    [Fact]
    public void EnrichPerson_InexactBurial_LeavesTheDeathGapOpen()
    {
        const string ged = """
            0 @I1@ INDI
            1 NAME William /Morrill/
            1 SEX M
            1 BIRT
            2 DATE 1 JAN 1850
            2 PLAC Boston, Massachusetts
            2 SOUR @S1@
            1 BURI
            2 DATE 1937
            """;
        var target = Assert.Single(Detect(ged, "Morrill"), t => t.CardType == "Enrich person");
        Assert.Single(target.Enrichments!, e => e.Fact == "Death");
    }

    static string DeathOn(string date, int quay) => $"""
        0 @I1@ INDI
        1 NAME William /Morrill/
        1 SEX M
        1 BIRT
        2 DATE 1 JAN 1750
        2 PLAC Boston, Massachusetts
        2 SOUR @S1@
        1 DEAT
        2 DATE {date}
        2 PLAC Boston, Massachusetts
        2 SOUR @S1@
        3 QUAY {quay}
        """;

    static bool DeathDateIsAGap(string ged, HonestRange? honestRange = null)
    {
        var targets = GapDetector.Detect(ModelBuilder.Build(Ged55Parser.Parse(ged)), ["Morrill"], 2026,
            rules: new GapRules { HonestRange = honestRange ?? HonestRange.Default });
        return targets.Any(t => t.Enrichments?.Any(e => e.Fact == "Death" && e.MissingDate) == true);
    }

    [Theory]
    [InlineData("BET 18 AUG 1796 AND 27 SEP 1796")]
    [InlineData("BET 1 JAN 1796 AND 1 JAN 1797")]
    [InlineData("BET JAN 1796 AND DEC 1796")]
    public void EnrichPerson_ACitedRangeWithinTheSpan_IsNotADateGap(string date) =>
        Assert.False(DeathDateIsAGap(DeathOn(date, quay: 2)));

    [Theory]
    [InlineData("BET 1 JAN 1796 AND 2 JAN 1797")]
    [InlineData("BET 1796 AND 1797")]
    [InlineData("BEF 27 SEP 1796")]
    [InlineData("AFT 18 AUG 1796")]
    [InlineData("ABT 1796")]
    [InlineData("CAL 1796")]
    [InlineData("EST 1796")]
    public void EnrichPerson_AWiderRangeOrASoftOrOpenDate_IsStillADateGap(string date) =>
        Assert.True(DeathDateIsAGap(DeathOn(date, quay: 3)));

    [Fact]
    public void EnrichPerson_ANarrowRangeCitedBelowTheQuay_IsStillADateGap() =>
        Assert.True(DeathDateIsAGap(DeathOn("BET 18 AUG 1796 AND 27 SEP 1796", quay: 1)));

    [Fact]
    public void EnrichPerson_TheRangeSpanAndQuayAreConfigurable()
    {
        string ged = DeathOn("BET 1796 AND 1797", quay: 1);

        Assert.True(DeathDateIsAGap(ged));
        Assert.False(DeathDateIsAGap(ged, new HonestRange(MaxSpanMonths: 24, MinQuay: 1)));
    }

    [Fact]
    public void Died_FallsBackToTheBurialWhenThereIsNoDeath()
    {
        const string ged = """
            0 @I1@ INDI
            1 NAME William /Morrill/
            1 SEX M
            1 BURI
            2 DATE 8 FEB 1937
            2 PLAC Boston, Massachusetts
            """;
        var target = Assert.Single(Detect(ged, "Morrill"), t => t.CardType == "New parent");
        Assert.Equal("8 FEB 1937, Boston, Massachusetts", target.Died);
    }

    [Fact]
    public void Died_PrefersTheDeathOverTheBurial()
    {
        const string ged = """
            0 @I1@ INDI
            1 NAME William /Morrill/
            1 SEX M
            1 DEAT
            2 DATE 6 FEB 1937
            1 BURI
            2 DATE 8 FEB 1937
            """;
        var target = Assert.Single(Detect(ged, "Morrill"), t => t.CardType == "New parent");
        Assert.Equal("6 FEB 1937", target.Died);
    }

    [Fact]
    public void NewSpouse_NotOfferedWhenTheBurialShowsAnEarlyDeath()
    {
        const string ged = """
            0 @I1@ INDI
            1 NAME Ann /Morrill/
            1 SEX F
            1 BIRT
            2 DATE 1 JAN 1850
            1 BURI
            2 DATE 8 FEB 1855
            """;
        Assert.DoesNotContain(Detect(ged, "Morrill"), t => t.CardType == "New spouse");
    }

    [Fact]
    public void EnrichPerson_InexactMarriage_DetectedOncePerFamily()
    {
        const string ged = """
            0 @I1@ INDI
            1 NAME John /Morrill/
            1 SEX M
            1 FAMS @F1@
            0 @I2@ INDI
            1 NAME Mary /Morrill/
            1 SEX F
            1 FAMS @F1@
            0 @F1@ FAM
            1 HUSB @I1@
            1 WIFE @I2@
            1 MARR
            2 DATE ABT 1820
            """;
        var targets = Detect(ged, "Morrill");
        var marriages = targets.Where(t => t.CardType == "Enrich person").SelectMany(t => t.Enrichments!).Where(e => e.Fact == "Marriage");
        Assert.Single(marriages);
    }

    [Fact]
    public void EnrichPerson_IsOneCardPerPersonWithOnePointPerMissingDatePlaceOrCitation()
    {
        const string ged = """
            0 @I1@ INDI
            1 NAME William /Morrill/
            1 SEX M
            1 BIRT
            2 DATE ABT 1780
            1 DEAT
            2 DATE 10 MAR 1854
            1 FAMS @F1@
            0 @F1@ FAM
            1 HUSB @I1@
            1 MARR
            2 PLAC Boston, Massachusetts
            """;
        var targets = Detect(ged, "Morrill");

        var card = Assert.Single(targets, t => t.CardType == "Enrich person");
        Assert.Equal(["Birth", "Death", "Marriage"], card.Enrichments!.Select(e => e.Fact));
        // birth: inexact date, no place, uncited (3); death: no place, uncited (2); marriage: no date, uncited (2)
        Assert.Equal(7, card.NominalPoints);
        Assert.True(card.Score >= card.NominalPoints, "the difficulty bonus is added once, never subtracted");
    }

    [Fact]
    public void SurnameFilter_MarriedInSpouse_IncludedForNonAncestryGaps()
    {
        const string ged = """
            0 @I1@ INDI
            1 NAME John /Morrill/
            1 SEX M
            1 FAMS @F1@
            0 @I2@ INDI
            1 NAME Mary /Smith/
            1 SEX F
            1 FAMS @F1@
            1 BIRT
            2 DATE ABT 1850
            0 @F1@ FAM
            1 HUSB @I1@
            1 WIFE @I2@
            """;
        // Mary Smith isn't a Morrill herself, but she's married to one, so
        // gaps that stay within the surname family — enriching her own
        // birth record — must still surface for her.
        var targets = Detect(ged, "Morrill");
        Assert.Contains(targets, t => t.Xref == "@I2@" && t.CardType == "Enrich person");
    }

    [Fact]
    public void SurnameFilter_MarriedInSpouse_NewParentNotTargeted()
    {
        const string ged = """
            0 @I1@ INDI
            1 NAME John /Morrill/
            1 SEX M
            1 BIRT
            2 PLAC Maine
            1 FAMS @F1@
            0 @I2@ INDI
            1 NAME Mary /Smith/
            1 SEX F
            1 FAMS @F1@
            0 @F1@ FAM
            1 HUSB @I1@
            1 WIFE @I2@
            """;
        // Mary Smith has no recorded parents either, but chasing her own
        // parents would head into an unrelated Smith family line — only an
        // actual surname-bearer (John) should get a New parent card.
        var targets = Detect(ged, "Morrill");
        Assert.DoesNotContain(targets, t => t.Xref == "@I2@" && t.CardType == "New parent");
        Assert.Contains(targets, t => t.Xref == "@I1@" && t.CardType == "New parent");
    }

    [Fact]
    public void SurnameFilter_UnrelatedPerson_Excluded()
    {
        const string ged = """
            0 @I1@ INDI
            1 NAME Someone /Unrelated/
            1 SEX M
            """;
        var targets = Detect(ged, "Morrill");
        Assert.Empty(targets);
    }

    [Fact]
    public void PrivacyFloor_BornWithinLastHundredYears_ExcludedFromEveryCardType()
    {
        const string ged = """
            0 @I1@ INDI
            1 NAME William /Morrill/
            1 SEX M
            1 BIRT
            2 DATE 1950
            """;
        var targets = Detect(ged, currentYear: 2026, "Morrill"); // cutoff year 1926; born 1950 is inside it
        Assert.Empty(targets);
    }

    [Fact]
    public void PrivacyFloor_BornExactlyAtCutoff_ExcludedInclusively()
    {
        const string ged = """
            0 @I1@ INDI
            1 NAME William /Morrill/
            1 SEX M
            1 BIRT
            2 DATE 1926
            """;
        var targets = Detect(ged, currentYear: 2026, "Morrill"); // cutoff year is exactly 1926
        Assert.Empty(targets);
    }

    [Fact]
    public void PrivacyFloor_BornBeforeCutoff_StillDetected()
    {
        const string ged = """
            0 @I1@ INDI
            1 NAME William /Morrill/
            1 SEX M
            1 BIRT
            2 DATE 1925
            """;
        var targets = Detect(ged, currentYear: 2026, "Morrill");
        Assert.NotEmpty(targets);
    }

    [Fact]
    public void PrivacyFloor_UnknownBirthYear_NotGated()
    {
        const string ged = """
            0 @I1@ INDI
            1 NAME William /Morrill/
            1 SEX M
            """;
        var targets = Detect(ged, currentYear: 2026, "Morrill");
        Assert.NotEmpty(targets);
    }

    [Fact]
    public void PrivacyFloor_DoesNotHoldBackAPersonTheOwnerNames()
    {
        var person = new GedIndividual
        {
            Xref = "@I1@", FirstName = "William", LastNameRaw = "Morrill", IsMale = true,
            Birth = new GedEvent { Tag = "BIRT", Date = "1990" },
        };

        var gaps = GapDetector.DetectForPeople([person]);
        var cards = GapDetector.WithNamedBiographyCards([person], gaps);

        Assert.Contains(gaps, c => c.CardType == "Enrich person");
        Assert.Contains(cards, c => c.CardType == "Biography");
    }

    [Theory]
    [InlineData("4 JUL 1950", "Boston, Massachusetts", false, 0, 0)]       // within the privacy floor
    [InlineData("4 JUL 1910", "Boston, Massachusetts", true, 0, 3)]        // Common: complete vitals still earn a card
    [InlineData("ABT 1750", "Paris, France", true, 4, 13)]                  // Rare: era 2 + region 2, no anchor adjustment
    [InlineData(null, "Tokyo, Japan", true, 3, 13)]                         // undated birth falls back to the death's era
    public void BiographyCards_PriceThreePointsPlusTheEnrichmentDifficultyBonus(
        string? birthDate, string place, bool expected, int rawDifficulty, int score)
    {
        var person = new GedIndividual
        {
            Xref = "@I1@", FirstName = "Adam", LastNameRaw = "Ashworth", IsMale = true,
            Birth = new GedEvent { Date = birthDate ?? "", Place = place },
            Death = new GedEvent { Date = "1 MAR 1960" },
        };

        var cards = GapDetector.BiographyCards([person], 2026);

        Assert.Equal(expected, cards.Count == 1);
        if (!expected) return;
        Assert.Equal("Biography", cards[0].CardType);
        Assert.Equal(3, cards[0].NominalPoints);
        Assert.Equal(0, cards[0].Difficulty.ContextAdjustment);
        Assert.Equal(rawDifficulty, cards[0].Difficulty.EraWeight + cards[0].Difficulty.GeoWeight);
        Assert.Equal(score, cards[0].Score);
    }

    [Fact]
    public void WithBiographyCards_AddsBiographyOnlyForAManWithAnotherCard_AfterHisCards()
    {
        static GedIndividual Person(string xref, bool male) => new()
        {
            Xref = xref, FirstName = xref, LastNameRaw = "Ashworth", IsMale = male, Birth = new GedEvent { Date = "1850" },
        };
        var man = Person("@I1@", true);
        var manWithoutGaps = Person("@I2@", true);
        var woman = Person("@I3@", false);
        var gaps = GapDetector.DetectForPeople([man, woman]);

        var pack = GapDetector.WithBiographyCards([man, manWithoutGaps, woman], gaps);

        Assert.Equal("Biography", pack.Last(t => t.Xref == "@I1@").CardType);
        Assert.Single(pack, t => t.CardType == "Biography");
        Assert.DoesNotContain(pack, t => t.Xref == "@I2@");
        Assert.Equal(gaps.Count + 1, pack.Count);
    }

    [Fact]
    public void EnrichPerson_AFactTheRecordLacks_MissesItsDatePlaceAndCitation()
    {
        const string ged = """
            0 @I1@ INDI
            1 NAME Amos /Ashworth/
            1 SEX M
            1 BIRT
            2 DATE 3 JUN 1850
            2 PLAC Salem, Oregon
            2 SOUR @S1@
            """;

        var card = Assert.Single(Detect(ged, "Ashworth"), t => t.CardType == "Enrich person");

        var death = Assert.Single(card.Enrichments!);
        Assert.Equal(("Death", true, true, true), (death.Fact, death.MissingDate, death.MissingPlace, death.MissingCitation));
        Assert.Equal(3, card.NominalPoints);
    }

    // ---- a proven never-married answer ----

    static string Bachelor(string answer) => $"""
        0 @I1@ INDI
        1 NAME Amos /Ashworth/
        1 SEX M
        1 BIRT
        2 PLAC Salem, Oregon
        {answer}
        """;

    [Fact]
    public void NewSpouse_WithheldFromSomeoneWithACitedNeverMarriedAnswer()
    {
        var suppressed = new List<SuppressedCard>();

        var targets = GapDetector.Detect(ModelBuilder.Build(Ged55Parser.Parse(Bachelor("1 NMR 0\n2 SOUR @S1@"))), ["Ashworth"], 2026, suppressed);

        Assert.DoesNotContain(targets, t => t.CardType == "New spouse");
        var card = Assert.Single(suppressed);
        Assert.Equal(("@I1@", "New spouse", "never married: a cited NMR 0 records the answer"), (card.Xref, card.CardType, card.Reason));
    }

    [Theory]
    [InlineData("1 NMR 0")]                 // uncited: not proven
    [InlineData("1 NMR 1\n2 SOUR @S1@")]    // married once: the spouse is still to find
    [InlineData("1 OCCU Cooper")]
    public void NewSpouse_StillDealtWithoutACitedZero(string answer) =>
        Assert.Contains(Detect(Bachelor(answer), "Ashworth"), t => t.CardType == "New spouse");

    [Fact]
    public void NewSpouse_IncludeSuppressedDrawsTheCardDespiteTheAnswer()
    {
        var person = ModelBuilder.Build(Ged55Parser.Parse(Bachelor("1 NMR 0\n2 SOUR @S1@"))).Individuals["@I1@"];

        var targets = GapDetector.DetectForPeople([person], rules: new GapRules { IncludeSuppressed = true });

        Assert.Contains(targets, t => t.CardType == "New spouse");
    }

    // ---- study scope ----

    // A bearer husband and his married-in wife (@F1@); a bearer daughter and her husband of another surname (@F2@); a stranger.
    const string StudyGed = """
        0 @I1@ INDI
        1 NAME Amos /Ashworth/
        1 SEX M
        1 BIRT
        2 PLAC Salem, Oregon
        1 FAMS @F1@
        0 @I2@ INDI
        1 NAME Ruth /Holt/
        1 SEX F
        1 BIRT
        2 PLAC Salem, Oregon
        1 FAMS @F1@
        0 @I3@ INDI
        1 NAME Ada /Ashworth/
        1 SEX F
        1 BIRT
        2 PLAC Salem, Oregon
        1 FAMS @F2@
        0 @I4@ INDI
        1 NAME Eli /Pike/
        1 SEX M
        1 BIRT
        2 PLAC Salem, Oregon
        1 FAMS @F2@
        0 @I5@ INDI
        1 NAME Saul /Marsh/
        1 SEX M
        1 BIRT
        2 PLAC Salem, Oregon
        0 @F1@ FAM
        1 HUSB @I1@
        1 WIFE @I2@
        0 @F2@ FAM
        1 HUSB @I4@
        1 WIFE @I3@
        """;

    static readonly GapRules AshworthStudy = new() { Scope = new StudyScope(["Ashworth"]) };

    static (List<SelectionTarget> Targets, List<SuppressedCard> Suppressed) Named(GapRules rules, params string[] xrefs)
    {
        var model = ModelBuilder.Build(Ged55Parser.Parse(StudyGed));
        var people = xrefs.Select(x => model.Individuals[x]).ToList();
        var suppressed = new List<SuppressedCard>();
        var gaps = GapDetector.DetectForPeople(people, suppressed, rules);
        return (GapDetector.WithNamedBiographyCards(people, gaps, suppressed, rules), suppressed);
    }

    static IEnumerable<string> CardsOf(IEnumerable<SelectionTarget> targets, string xref) =>
        targets.Where(t => t.Xref == xref).Select(t => t.CardType);

    [Fact]
    public void StudyScope_ABearerGetsEveryCard_AndTheSpouseWhoDoesNotBearItOnlyEnrichment()
    {
        var (targets, suppressed) = Named(AshworthStudy, "@I1@", "@I2@");

        Assert.Equal(["New parent", "New child", "Enrich person", "Biography"], CardsOf(targets, "@I1@"));
        Assert.Equal(["Enrich person"], CardsOf(targets, "@I2@"));
        var withheld = Assert.Single(suppressed);
        Assert.Equal(("@I2@", "New parent", true), (withheld.Xref, withheld.CardType, withheld.OutOfScope));
        Assert.StartsWith("out of study scope: bears no study surname; the spouse of someone who does", withheld.Reason);
    }

    [Fact]
    public void StudyScope_WithholdsTheNewChildCardOfAFamilyWhoseHusbandDoesNotBearTheSurname_AndHisBiography()
    {
        var (targets, suppressed) = Named(AshworthStudy, "@I3@", "@I4@");

        Assert.Equal(["New parent", "Enrich person"], CardsOf(targets, "@I3@"));
        Assert.Equal(["Enrich person"], CardsOf(targets, "@I4@"));
        Assert.Equal(
            [("@I3@", "New child"), ("@I4@", "New parent"), ("@I4@", "Biography")],
            suppressed.Select(c => (c.Xref, c.CardType)));
        Assert.Equal("out of study scope: @F2@ has no husband who bears a study surname, so its children would not bear one", suppressed[0].Reason);
    }

    [Fact]
    public void StudyScope_DealsTheMarriedInWifeTheNewChildCardOfHerHusbandsFamily()
    {
        var (targets, _) = Named(AshworthStudy, "@I2@");

        Assert.Equal(["New child", "Enrich person"], CardsOf(targets, "@I2@"));
    }

    [Fact]
    public void StudyScope_DealsNothingToAnyoneElse_AndListsEachCardWithheld()
    {
        var (targets, suppressed) = Named(AshworthStudy, "@I5@");

        Assert.Empty(targets);
        Assert.Equal(["New parent", "New spouse", "Enrich person", "Biography"], suppressed.Select(c => c.CardType));
        Assert.All(suppressed, c => Assert.Equal("out of study scope: bears no study surname and is not married to someone who does", c.Reason));
    }

    [Fact]
    public void StudyScope_IsNotLiftedByIncludeSuppressed()
    {
        var (targets, suppressed) = Named(AshworthStudy with { IncludeSuppressed = true }, "@I4@", "@I5@");

        Assert.Equal(["Enrich person"], targets.Select(t => t.CardType));
        Assert.All(suppressed, c => Assert.True(c.OutOfScope));
    }

    [Fact]
    public void WithoutAStudyScope_ANamedPersonGetsEveryCard()
    {
        var (targets, suppressed) = Named(GapRules.Default, "@I4@", "@I5@");

        Assert.Equal(["New parent", "New child", "Enrich person", "Biography"], CardsOf(targets, "@I4@"));
        Assert.Equal(["New parent", "New spouse", "Enrich person", "Biography"], CardsOf(targets, "@I5@"));
        Assert.Empty(suppressed);
    }

    [Fact]
    public void StudyScope_APersonOutsideIt_TakesNoMarriageFromASpouseInsideIt()
    {
        // Ruth is in scope by her first marriage; her second husband is not, and is named before her.
        string ged = StudyGed.ReplaceLineEndings("\n").Replace("1 FAMS @F1@\n0 @I3@", "1 FAMS @F1@\n1 FAMS @F3@\n0 @I3@")
            + "\n0 @I6@ INDI\n1 NAME Noah /Pike/\n1 SEX M\n1 FAMS @F3@\n0 @F3@ FAM\n1 HUSB @I6@\n1 WIFE @I2@\n";
        var model = ModelBuilder.Build(Ged55Parser.Parse(ged));

        var targets = GapDetector.DetectForPeople([model.Individuals["@I6@"], model.Individuals["@I2@"]], rules: AshworthStudy);

        var card = Assert.Single(targets, t => t.CardType == "Enrich person");
        Assert.Equal("@I2@", card.Xref);
        Assert.Equal(2, card.Enrichments!.Count(e => e.Fact == "Marriage"));
    }

    [Fact]
    public void StudyScope_InADraw_ReportsWhatTheSurnameFilterUsedToSkipSilently()
    {
        var model = ModelBuilder.Build(Ged55Parser.Parse(StudyGed));
        var silent = new List<SuppressedCard>();
        var reported = new List<SuppressedCard>();

        var unscoped = GapDetector.Detect(model, ["Ashworth"], 2026, silent);
        var scoped = GapDetector.Detect(model, ["Ashworth"], 2026, reported, AshworthStudy);

        Assert.Equal(unscoped.Select(t => (t.Xref, t.CardType)), scoped.Select(t => (t.Xref, t.CardType)));
        Assert.Empty(silent);
        Assert.Equal(
            [("@I2@", "New parent"), ("@I3@", "New child"), ("@I4@", "New parent")],
            reported.Select(c => (c.Xref, c.CardType)));
    }
}

// ---------------------------------------------------------------------------
// TargetDrawer — uniform draw, Legendary cap, determinism
// ---------------------------------------------------------------------------

public class TargetDrawerTests
{
    static SelectionTarget MakeTarget(string xref, DifficultyBand band, string cardType = "New parent") => new()
    {
        Xref = xref,
        Name = xref,
        Surname = "Test",
        CardType = cardType,
        NominalPoints = 10,
        Difficulty = new DifficultyEntry { Band = band, EraWeight = 0, GeoWeight = 0, ContextAdjustment = 0 },
        Score = 10,
    };

    [Fact]
    public void Draw_CountsPeople_AndReturnsEveryCardOfEachTogether()
    {
        var pool = new List<SelectionTarget>
        {
            MakeTarget("@I1@", DifficultyBand.Common), MakeTarget("@I2@", DifficultyBand.Common),
            MakeTarget("@I1@", DifficultyBand.Common, "New spouse"), MakeTarget("@I3@", DifficultyBand.Common),
            MakeTarget("@I2@", DifficultyBand.Common, "Enrich person"),
        };

        var result = TargetDrawer.Draw(pool, people: 3, seed: 9);

        Assert.Equal(5, result.Targets.Count);
        var order = result.Targets.Select(t => t.Xref).ToList();
        Assert.Equal(3, order.Distinct().Count());
        Assert.All(order.Distinct(), xref => Assert.Equal(
            order.LastIndexOf(xref) - order.IndexOf(xref) + 1, order.Count(x => x == xref)));
        Assert.Equal(["New parent", "New spouse"], result.Targets.Where(t => t.Xref == "@I1@").Select(t => t.CardType));
    }

    [Fact]
    public void DrawPack_ListsTheCardsAStudyScopeWithholdsForTheDrawnPeopleOnly()
    {
        // Two bearer wives with husbands of another surname, and a bearer with no anchor at all.
        const string ged = """
            0 @I1@ INDI
            1 NAME Ada /Ashworth/
            1 SEX F
            1 FAMS @F1@
            0 @I2@ INDI
            1 NAME Eli /Pike/
            1 SEX M
            1 FAMS @F1@
            0 @I3@ INDI
            1 NAME Ann /Ashworth/
            1 SEX F
            1 FAMS @F2@
            0 @I4@ INDI
            1 NAME Noah /Marsh/
            1 SEX M
            1 FAMS @F2@
            0 @I5@ INDI
            1 NAME Amos /Ashworth/
            1 SEX M
            0 @F1@ FAM
            1 HUSB @I2@
            1 WIFE @I1@
            1 MARR
            2 PLAC Salem, Oregon
            0 @F2@ FAM
            1 HUSB @I4@
            1 WIFE @I3@
            1 MARR
            2 PLAC Salem, Oregon
            """;
        var model = ModelBuilder.Build(Ged55Parser.Parse(ged));
        var rules = new GapRules { Scope = new StudyScope(["Ashworth"]) };
        var suppressed = new List<SuppressedCard>();
        var husbands = GapDetector.Detect(model, ["Ashworth"], 2026, suppressed, rules).Where(t => t.Xref is "@I2@" or "@I4@").ToList();

        var pack = TargetDrawer.DrawPack(model, husbands, people: 1, seed: 3, suppressed, rules);

        string drawn = Assert.Single(pack.Targets).Xref;
        Assert.Equal("Enrich person", pack.Targets[0].CardType);
        var outOfScope = pack.SuppressedCards.Where(c => c.OutOfScope).ToList();
        Assert.All(outOfScope, c => Assert.Equal(drawn, c.Xref));
        Assert.Contains(outOfScope, c => c.CardType == "Biography");
        Assert.Contains(outOfScope, c => c.CardType == "New parent");
        Assert.Contains(pack.SuppressedCards, c => c.Xref == "@I5@" && c.Reason.StartsWith("bare-bones subject"));
    }

    [Fact]
    public void Draw_DiscardsASecondLegendaryPersonWhole()
    {
        var pool = new List<SelectionTarget>
        {
            MakeTarget("@L1@", DifficultyBand.Legendary),
            MakeTarget("@L2@", DifficultyBand.Common), MakeTarget("@L2@", DifficultyBand.Legendary, "New spouse"),
        };

        var result = TargetDrawer.Draw(pool, people: 2, seed: 7);

        var kept = Assert.Single(result.Targets.Select(t => t.Xref).Distinct());
        Assert.Equal(pool.Count(c => c.Xref == kept), result.Targets.Count);
        Assert.Equal(pool.Count(c => c.Xref != kept), result.LegendaryDiscards.Count);
        Assert.All(result.LegendaryDiscards, d => Assert.NotEqual(kept, d.Xref));
    }

    [Fact]
    public void Draw_TakesRequestedCount_WhenPoolLargeEnough()
    {
        var pool = Enumerable.Range(0, 10).Select(i => MakeTarget($"@I{i}@", DifficultyBand.Common)).ToList();
        var result = TargetDrawer.Draw(pool, people: 5, seed: 42);
        Assert.Equal(5, result.Targets.Count);
        Assert.Equal(result.Targets.Select(t => t.Xref).Distinct().Count(), result.Targets.Count);
    }

    [Fact]
    public void Draw_CapsAtPoolSize_WhenCountExceedsPool()
    {
        var pool = Enumerable.Range(0, 3).Select(i => MakeTarget($"@I{i}@", DifficultyBand.Common)).ToList();
        var result = TargetDrawer.Draw(pool, people: 10, seed: 1);
        Assert.Equal(3, result.Targets.Count);
    }

    [Fact]
    public void Draw_DiscardsSecondLegendary_WithNoBackfill()
    {
        var pool = new List<SelectionTarget>
        {
            MakeTarget("@L1@", DifficultyBand.Legendary),
            MakeTarget("@L2@", DifficultyBand.Legendary),
        };
        var result = TargetDrawer.Draw(pool, people: 2, seed: 7);

        Assert.Single(result.Targets);
        Assert.Equal(DifficultyBand.Legendary, result.Targets[0].Difficulty.Band);
        Assert.Single(result.LegendaryDiscards);
    }

    [Fact]
    public void Draw_AllowsOneLegendaryAlongsideOthers()
    {
        var pool = new List<SelectionTarget>
        {
            MakeTarget("@L1@", DifficultyBand.Legendary),
            MakeTarget("@C1@", DifficultyBand.Common),
            MakeTarget("@C2@", DifficultyBand.Common),
        };
        var result = TargetDrawer.Draw(pool, people: 3, seed: 3);

        Assert.Equal(3, result.Targets.Count);
        Assert.Empty(result.LegendaryDiscards);
        Assert.Single(result.Targets, t => t.Difficulty.Band == DifficultyBand.Legendary);
    }

    [Fact]
    public void Draw_SameSeedAndPool_IsDeterministic()
    {
        var pool = Enumerable.Range(0, 20).Select(i => MakeTarget($"@I{i}@", DifficultyBand.Common)).ToList();
        var first = TargetDrawer.Draw(pool, people: 6, seed: 12345);
        var second = TargetDrawer.Draw(pool, people: 6, seed: 12345);

        Assert.Equal(first.Targets.Select(t => t.Xref), second.Targets.Select(t => t.Xref));
    }
}

// ---------------------------------------------------------------------------
// WantedFileWriter — envelope shape
// ---------------------------------------------------------------------------

public class WantedFileWriterTests
{
    [Fact]
    public void ToJson_EnvelopeCarriesCountsAndDrawLog()
    {
        var target = new SelectionTarget
        {
            Xref = "@I1@",
            Name = "William Morrill",
            Surname = "Morrill",
            CardType = "New parent",
            NominalPoints = 10,
            Difficulty = new DifficultyEntry { Band = DifficultyBand.Rare, EraWeight = 2, GeoWeight = 1, ContextAdjustment = 1 },
            Score = 20,
        };
        var draw = new DrawResult
        {
            Targets = [target],
            Seed = 999,
            LegendaryDiscards = [],
            SuppressedCards = [new SuppressedCard("@I2@", "Ada Ashworth", "New spouse", "bare-bones subject \u2014 enrich first")],
        };

        string json = WantedFileWriter.ToJson("data/Test.ged", ["Morrill", "Morrell"], totalCandidates: 42, draw);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal("data/Test.ged", root.GetProperty("source").GetString());
        Assert.Equal(42, root.GetProperty("totalCandidates").GetInt32());
        Assert.Equal(1, root.GetProperty("count").GetInt32());
        Assert.Equal(new[] { "Morrill", "Morrell" },
            root.GetProperty("surnames").EnumerateArray().Select(e => e.GetString()).ToArray());

        var jsonTarget = root.GetProperty("targets").EnumerateArray().Single();
        Assert.Equal("New parent", jsonTarget.GetProperty("cardType").GetString());
        Assert.Equal("Rare", jsonTarget.GetProperty("difficulty").GetProperty("band").GetString());
        Assert.Equal(20, jsonTarget.GetProperty("score").GetInt32());
        Assert.Equal(0, jsonTarget.GetProperty("possibleDuplicateOf").GetArrayLength());
        Assert.False(jsonTarget.TryGetProperty("birthPlace", out _));

        var drawLog = root.GetProperty("draw");
        Assert.Equal(999, drawLog.GetProperty("seed").GetInt64());
        Assert.Equal(0, drawLog.GetProperty("legendaryDiscards").GetArrayLength());
        var suppressed = Assert.Single(root.GetProperty("suppressedCards").EnumerateArray());
        Assert.Equal("@I2@", suppressed.GetProperty("xref").GetString());
        Assert.Equal("New spouse", suppressed.GetProperty("cardType").GetString());
    }

    [Fact]
    public void ToJson_WithoutSourcePath_OmitsSourceAndKeepsEverythingElse()
    {
        var draw = new DrawResult { Targets = [], Seed = 7, LegendaryDiscards = [] };

        string json = WantedFileWriter.ToJson(["Ashworth"], totalCandidates: 3, draw);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.False(root.TryGetProperty("source", out _));
        Assert.Equal(3, root.GetProperty("totalCandidates").GetInt32());
        Assert.Equal(7, root.GetProperty("draw").GetProperty("seed").GetInt64());
        Assert.Equal(0, root.GetProperty("suppressedCards").GetArrayLength());
    }
}
