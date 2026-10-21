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
            ? GapDetector.DetectForPeople(model.Individuals.Values, 2026, suppressed)
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
                ? GapDetector.DetectForPeople(model.Individuals.Values, 2026, suppressed)
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
                ? GapDetector.DetectForPeople(model.Individuals.Values, 2026, suppressed)
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
        Assert.Equal("Enrich person", Assert.Single(GapDetector.DetectForPeople([person], 2026)).CardType);

        person.Birth = new GedEvent { Date = "4 JUL 1872" };

        var suppressed = new List<SuppressedCard>();
        Assert.Equal(3, GapDetector.DetectForPeople([person], 2026, suppressed).Count);
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
            ? GapDetector.DetectForPeople([model.Individuals["@I1@"]], 2026, suppressed)
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
    public void EnrichPerson_InexactUncitedBirth_HasNoCitationPoint()
    {
        const string ged = """
            0 @I1@ INDI
            1 NAME William /Morrill/
            1 SEX M
            1 BIRT
            2 DATE ABT 1780
            """;
        var target = Assert.Single(Detect(ged, "Morrill"), t => t.CardType == "Enrich person");
        Assert.False(Assert.Single(target.Enrichments!, e => e.Fact == "Birth").MissingCitation);
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
        // birth: inexact date and no place (2); death: no place, exact date uncited (2); marriage: no date, exact place uncited (2)
        Assert.Equal(6, card.NominalPoints);
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
}

// ---------------------------------------------------------------------------
// TargetDrawer — uniform draw, Legendary cap, determinism
// ---------------------------------------------------------------------------

public class TargetDrawerTests
{
    static SelectionTarget MakeTarget(string xref, DifficultyBand band) => new()
    {
        Xref = xref,
        Name = xref,
        Surname = "Test",
        CardType = "New parent",
        NominalPoints = 10,
        Difficulty = new DifficultyEntry { Band = band, EraWeight = 0, GeoWeight = 0, ContextAdjustment = 0 },
        Score = 10,
    };

    [Fact]
    public void Draw_TakesRequestedCount_WhenPoolLargeEnough()
    {
        var pool = Enumerable.Range(0, 10).Select(i => MakeTarget($"@I{i}@", DifficultyBand.Common)).ToList();
        var result = TargetDrawer.Draw(pool, count: 5, seed: 42);
        Assert.Equal(5, result.Targets.Count);
        Assert.Equal(result.Targets.Select(t => t.Xref).Distinct().Count(), result.Targets.Count);
    }

    [Fact]
    public void Draw_CapsAtPoolSize_WhenCountExceedsPool()
    {
        var pool = Enumerable.Range(0, 3).Select(i => MakeTarget($"@I{i}@", DifficultyBand.Common)).ToList();
        var result = TargetDrawer.Draw(pool, count: 10, seed: 1);
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
        var result = TargetDrawer.Draw(pool, count: 2, seed: 7);

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
        var result = TargetDrawer.Draw(pool, count: 3, seed: 3);

        Assert.Equal(3, result.Targets.Count);
        Assert.Empty(result.LegendaryDiscards);
        Assert.Single(result.Targets, t => t.Difficulty.Band == DifficultyBand.Legendary);
    }

    [Fact]
    public void Draw_SameSeedAndPool_IsDeterministic()
    {
        var pool = Enumerable.Range(0, 20).Select(i => MakeTarget($"@I{i}@", DifficultyBand.Common)).ToList();
        var first = TargetDrawer.Draw(pool, count: 6, seed: 12345);
        var second = TargetDrawer.Draw(pool, count: 6, seed: 12345);

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
