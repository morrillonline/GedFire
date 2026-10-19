using GedCore.Apply;
using GedCore.Points;

namespace GedCore.Tests;

public class CardScorerTests
{
    static readonly IReadOnlyDictionary<string, NameChange> NoNames = new Dictionary<string, NameChange>();

    static TreeChanges Facts(params FactWrite[] facts) =>
        new(facts, NoNames, new Dictionary<string, FamilyChange>());

    static TreeChanges WithFamily(string xref, FamilyChange family) =>
        new([], NoNames, new Dictionary<string, FamilyChange> { [xref] = family });

    static PointsCard Enrich(string gap, string xref = "@I1@", string? name = null) =>
        new(xref, "enrich-person", gap, name, 7);

    [Fact]
    public void EnrichDeath_ClosesWhenDeathIsWrittenOnTheSubject()
    {
        var preview = CardScorer.Preview([Enrich("enrich-death")], Facts(new FactWrite("@I1@", "DEAT", "1 JAN 1900", null)));

        var closed = Assert.Single(preview.Closed);
        Assert.Equal("@I1@|enrich-person|enrich-death", closed.CardKey);
        Assert.Equal(7, closed.Points);
        Assert.Equal("DEAT @I1@", closed.Wrote);
        Assert.Equal(7, preview.Total);
    }

    [Fact]
    public void EnrichDeathPlace_IsNotClosedByABurialPlace()
    {
        var preview = CardScorer.Preview(
            [Enrich("enrich-death-place")], Facts(new FactWrite("@I1@", "BURI", null, "Salem Cemetery")));

        var open = Assert.Single(preview.NotClosed);
        Assert.Equal("no DEAT place written on the subject (wrote BURI)", open.Reason);
        Assert.Empty(preview.Closed);
        Assert.Equal(0, preview.Total);
    }

    [Fact]
    public void EnrichDeathPlace_NeedsAPlaceNotJustADate()
    {
        var preview = CardScorer.Preview(
            [Enrich("enrich-death-place")], Facts(new FactWrite("@I1@", "DEAT", "1 JAN 1900", null)));

        Assert.Single(preview.NotClosed);
    }

    [Fact]
    public void EnrichCard_IsNotClosedByAFactOnSomeoneElse()
    {
        var preview = CardScorer.Preview([Enrich("enrich-birth")], Facts(new FactWrite("@I2@", "BIRT", "1900", null)));

        Assert.Equal("no BIRT written on the subject", Assert.Single(preview.NotClosed).Reason);
    }

    [Fact]
    public void EnrichMarriage_ClosesOnAMarriageOfAFamilyTheSubjectIsPartnerIn()
    {
        var changes = new TreeChanges(
            [new FactWrite("@F1@", "MARR", "1900", null)], NoNames,
            new Dictionary<string, FamilyChange> { ["@F1@"] = new(["@I1@", "@I2@"], [], [], []) });

        var preview = CardScorer.Preview([Enrich("enrich-marriage")], changes);

        Assert.Equal("MARR @F1@", Assert.Single(preview.Closed).Wrote);
    }

    [Fact]
    public void EnrichCardWithAnUnrecognizedGap_ReportsWhy()
    {
        var preview = CardScorer.Preview([Enrich("enrich-occupation")], Facts());

        Assert.Contains("does not name a birth, death, marriage, burial or name fact", Assert.Single(preview.NotClosed).Reason);
    }

    [Theory]
    [InlineData("enrich-fact")]
    [InlineData(null)]
    public void AGenericEnrichCard_ClosesOnAnyFactAndPricesEachImprovement(string? gap)
    {
        var card = new PointsCard("@I1@", "enrich-person", gap, null, 1);
        var changes = Facts(
            new FactWrite("@I1@", "DEAT", "1 JAN 1900", "Salem"),
            new FactWrite("@I1@", "BIRT", null, "Duluth", DateImproved: false, PlaceImproved: true),
            new FactWrite("@I2@", "DEAT", "1901", null));

        var preview = CardScorer.Preview([card], changes);

        var closed = Assert.Single(preview.Closed);
        Assert.Equal(3, closed.Points);
        Assert.Equal("DEAT @I1@ (date and place); BIRT @I1@ (place)", closed.Wrote);
        Assert.Equal(3, preview.Total);
    }

    [Fact]
    public void AGenericEnrichCard_PricesANewCitationAsOneImprovement()
    {
        var card = new PointsCard("@I1@", "enrich-person", "enrich-fact", null, 1);
        var changes = Facts(new FactWrite("@I1@", "BIRT", "1850", "Duluth", DateImproved: false, PlaceImproved: false, CitationImproved: true));

        var closed = Assert.Single(CardScorer.Preview([card], changes).Closed);

        Assert.Equal(1, closed.Points);
        Assert.Equal("BIRT @I1@ (citation)", closed.Wrote);
    }

    [Fact]
    public void ANamedDatePlaceGap_IsNotClosedByACitationAlone()
    {
        var changes = Facts(new FactWrite("@I1@", "BIRT", "1850", "Duluth", DateImproved: false, PlaceImproved: false, CitationImproved: true));

        Assert.Single(CardScorer.Preview([Enrich("enrich-birth-date")], changes).NotClosed);
        Assert.Single(CardScorer.Preview([Enrich("enrich-birth-place")], changes).NotClosed);
        Assert.Single(CardScorer.Preview([Enrich("enrich-birth")], changes).Closed);
    }

    [Fact]
    public void AGenericEnrichCard_AddsItsDifficultyBonusOnceNotPerImprovement()
    {
        var card = new PointsCard("@I1@", "enrich-person", "enrich-fact", null, 1, Bonus: 4);
        var changes = Facts(new FactWrite("@I1@", "DEAT", "1 JAN 1900", "Salem"), new FactWrite("@I1@", "BIRT", "1850", "Duluth"));

        Assert.Equal(8, Assert.Single(CardScorer.Preview([card], changes).Closed).Points);
    }

    [Fact]
    public void AGenericEnrichCard_EarnsNoBonusWhenNothingIsWritten()
    {
        var card = new PointsCard("@I1@", "enrich-person", "enrich-fact", null, 1, Bonus: 4);

        var preview = CardScorer.Preview([card], Facts());

        Assert.Empty(preview.Closed);
        Assert.Equal(0, preview.Total);
    }

    [Fact]
    public void ANamedGapCard_IgnoresTheBonusBecauseItsPointsAlreadyIncludeIt()
    {
        var card = new PointsCard("@I1@", "enrich-person", "enrich-death", null, 7, Bonus: 4);

        var preview = CardScorer.Preview([card], Facts(new FactWrite("@I1@", "DEAT", "1900", null)));

        Assert.Equal(7, Assert.Single(preview.Closed).Points);
    }

    [Fact]
    public void AGenericEnrichCard_StaysOpenWhenNoFactIsWrittenOnTheSubject()
    {
        var preview = CardScorer.Preview([Enrich("enrich-fact")], Facts(new FactWrite("@I2@", "DEAT", "1901", null)));

        Assert.Equal("no fact written on the subject", Assert.Single(preview.NotClosed).Reason);
    }

    [Fact]
    public void EnrichBurial_ClosesOnABurialWrite()
    {
        var preview = CardScorer.Preview([Enrich("enrich-burial-place")], Facts(new FactWrite("@I1@", "BURI", null, "Salem Cemetery")));

        Assert.Equal("BURI @I1@", Assert.Single(preview.Closed).Wrote);
    }

    [Fact]
    public void EnrichName_ClosesOnARenameAndIsNotAMismatch()
    {
        var changes = new TreeChanges([], new Dictionary<string, NameChange> { ["@I1@"] = new("Al Test", "Allen Test") },
            new Dictionary<string, FamilyChange>());

        var preview = CardScorer.Preview([Enrich("enrich-name", name: "Al Test")], changes);

        Assert.Single(preview.Closed);
        Assert.Empty(preview.SubjectMismatch);
    }

    [Theory]
    [InlineData("enrich-citation")]
    [InlineData("verify-quay")]
    [InlineData("resolve-conflict")]
    public void ACitationStyleGap_ClosesOnAnyTouchOfTheSubject(string gap)
    {
        var touched = new TreeChanges([], NoNames, new Dictionary<string, FamilyChange>(), new HashSet<string> { "@I1@" });

        Assert.Single(CardScorer.Preview([Enrich(gap)], touched).Closed);
        Assert.Single(CardScorer.Preview([Enrich(gap)], TreeChanges.None).NotClosed);
    }

    [Fact]
    public void NewChild_ClosesWhenAFamilyOfTheSubjectGainsAChild()
    {
        var changes = WithFamily("@F1@", new FamilyChange(["@I1@", "@I2@"], ["@I9@"], [], ["@I9@"]));

        var preview = CardScorer.Preview([new PointsCard("@I1@", "New child", null, null, 5)], changes);

        Assert.Equal("CHIL @I9@ in @F1@", Assert.Single(preview.Closed).Wrote);
    }

    [Fact]
    public void NewParent_ClosesWhenTheSubjectsFamilyGainsAPartner()
    {
        var changes = WithFamily("@F1@", new FamilyChange(["@I5@"], ["@I1@"], ["@I5@"], []));

        var preview = CardScorer.Preview([new PointsCard("@I1@", "new-parent", null, null, 10)], changes);

        Assert.Equal("parent @I5@ of @I1@ in @F1@", Assert.Single(preview.Closed).Wrote);
    }

    [Fact]
    public void NewSpouse_NeedsTheOtherPartnerAndAnAddedPartner()
    {
        var alone = WithFamily("@F1@", new FamilyChange(["@I1@"], [], ["@I1@"], []));
        var paired = WithFamily("@F1@", new FamilyChange(["@I1@", "@I2@"], [], ["@I2@"], []));
        var card = new PointsCard("@I1@", "new-spouse", null, null, 5);

        Assert.Single(CardScorer.Preview([card], alone).NotClosed);
        Assert.Single(CardScorer.Preview([card], paired).Closed);
    }

    [Fact]
    public void RenamingTheSubject_ReportsAMismatchAndClosesNothing()
    {
        var changes = new TreeChanges(
            [new FactWrite("@I1@", "BIRT", "1900", null)],
            new Dictionary<string, NameChange> { ["@I1@"] = new("Albert Augustus Beattie", "Albert Agustus Beattie") },
            new Dictionary<string, FamilyChange>());

        var preview = CardScorer.Preview([Enrich("enrich-birth", name: "Albert Augustus Beattie")], changes);

        var mismatch = Assert.Single(preview.SubjectMismatch);
        Assert.Equal("Albert Augustus Beattie", mismatch.CardSubject);
        Assert.Equal("Albert Agustus Beattie", mismatch.AppliedName);
        Assert.Empty(preview.Closed);
    }

    [Fact]
    public void RenamingWithTheSameWordsDifferentlyFormatted_IsNotAMismatch()
    {
        var changes = new TreeChanges(
            [new FactWrite("@I1@", "BIRT", "1900", null)],
            new Dictionary<string, NameChange> { ["@I1@"] = new("Al Beattie", "Albert  Beattie") },
            new Dictionary<string, FamilyChange>());

        var preview = CardScorer.Preview([Enrich("enrich-birth", name: "albert /Beattie/")], changes);

        Assert.Empty(preview.SubjectMismatch);
        Assert.Single(preview.Closed);
    }

    [Fact]
    public void UnknownClass_IsReportedNotClosed()
    {
        var preview = CardScorer.Preview([new PointsCard("@I1@", "mystery", null, null, 3)], Facts());

        Assert.Contains("unknown card class", Assert.Single(preview.NotClosed).Reason);
    }

    [Fact]
    public void Total_SumsOnlyClosedCards()
    {
        var preview = CardScorer.Preview(
            [Enrich("enrich-birth"), Enrich("enrich-death")], Facts(new FactWrite("@I1@", "BIRT", "1900", null)));

        Assert.Equal(7, preview.Total);
        Assert.Single(preview.NotClosed);
    }

    static TreeChanges MintedChild(string name) =>
        new([], new Dictionary<string, NameChange> { ["@I9@"] = new(null, name) },
            new Dictionary<string, FamilyChange> { ["@F1@"] = new(["@I1@", "@I2@"], ["@I9@"], [], ["@I9@"]) });

    [Fact]
    public void NewChildCard_ClosesWhenTheMintedNameIsTheCardsName()
    {
        var preview = CardScorer.Preview([new PointsCard("@I1@", "new-child", null, "Ruth Test", 10)], MintedChild("Ruth /Test/"));

        Assert.Single(preview.Closed);
        Assert.Empty(preview.SubjectMismatch);
    }

    [Fact]
    public void NewChildCard_StaysOpenWhenTheMintedNameDriftsByALetter()
    {
        var preview = CardScorer.Preview([new PointsCard("@I1@", "new-child", null, "Albert Augustus Beattie", 10)], MintedChild("Albert Agustus Beattie"));

        var mismatch = Assert.Single(preview.SubjectMismatch);
        Assert.Equal("Albert Augustus Beattie", mismatch.CardSubject);
        Assert.Equal("Albert Agustus Beattie", mismatch.AppliedName);
        Assert.Empty(preview.Closed);
    }

    [Fact]
    public void NewChildCard_MatchesTheMintedNameLiterally()
    {
        var preview = CardScorer.Preview([new PointsCard("@I1@", "new-child", null, "ruth test", 10)], MintedChild("Ruth /Test/"));

        Assert.Single(preview.SubjectMismatch);
    }

    [Fact]
    public void NewChildCard_LinkingAnExistingPerson_IsNotNameChecked()
    {
        var changes = new TreeChanges([], NoNames,
            new Dictionary<string, FamilyChange> { ["@F1@"] = new(["@I1@"], ["@I8@"], [], ["@I8@"]) });

        Assert.Single(CardScorer.Preview([new PointsCard("@I1@", "new-child", null, "Ruth Test", 10)], changes).Closed);
    }

    [Fact]
    public void NewSpouseCard_ComparesTheMintedSpousesNameNotTheSubjects()
    {
        var changes = new TreeChanges([], new Dictionary<string, NameChange> { ["@I2@"] = new(null, "Edna /Test/") },
            new Dictionary<string, FamilyChange> { ["@F1@"] = new(["@I1@", "@I2@"], [], ["@I1@", "@I2@"], []) });

        Assert.Single(CardScorer.Preview([new PointsCard("@I1@", "new-spouse", null, "Edna Test", 5)], changes).Closed);
        Assert.Single(CardScorer.Preview([new PointsCard("@I1@", "new-spouse", null, "Edna Tast", 5)], changes).SubjectMismatch);
    }

    // ---- one applied person closes at most one new-relative card ----

    [Fact]
    public void TwoNewChildCards_OneMintedChild_CloseOnlyOneAndNameTheCollision()
    {
        var changes = MintedChild("Ruth /Test/");
        var cards = new[]
        {
            new PointsCard("@I1@", "new-child", null, null, 10),
            new PointsCard("@I2@", "new-child", null, null, 10),
        };

        var preview = CardScorer.Preview(cards, changes);

        var closed = Assert.Single(preview.Closed);
        Assert.Equal("@I1@|new-child", closed.CardKey);
        var open = Assert.Single(preview.NotClosed);
        Assert.Equal("@I2@|new-child", open.CardKey);
        Assert.Contains("@I9@", open.Reason);
        Assert.Contains("@I1@|new-child", open.Reason);
        Assert.Equal(10, preview.Total);
    }

    [Fact]
    public void TwoNewChildCards_TheExactNameMatchClaimsTheMintedChild()
    {
        var changes = MintedChild("Ruth /Test/");
        var cards = new[]
        {
            new PointsCard("@I1@", "new-child", null, "Someone Else", 10),
            new PointsCard("@I2@", "new-child", null, "Ruth Test", 5),
        };

        var preview = CardScorer.Preview(cards, changes);

        var closed = Assert.Single(preview.Closed);
        Assert.Equal("@I2@|new-child", closed.CardKey);
        var open = Assert.Single(preview.NotClosed);
        Assert.Equal("@I1@|new-child", open.CardKey);
        Assert.Contains("@I9@", open.Reason);
    }

    [Fact]
    public void TwoNewChildCards_TwoMintedChildren_EachCloseOnTheirOwnChild()
    {
        var changes = new TreeChanges([],
            new Dictionary<string, NameChange> { ["@I8@"] = new(null, "Ann /Test/"), ["@I9@"] = new(null, "Ruth /Test/") },
            new Dictionary<string, FamilyChange> { ["@F1@"] = new(["@I1@", "@I2@"], ["@I8@", "@I9@"], [], ["@I8@", "@I9@"]) });
        var cards = new[]
        {
            new PointsCard("@I1@", "new-child", null, "Ruth Test", 10),
            new PointsCard("@I2@", "new-child", null, "Ann Test", 5),
        };

        var preview = CardScorer.Preview(cards, changes);

        Assert.Equal(2, preview.Closed.Count);
        Assert.Equal(15, preview.Total);
        Assert.Contains("@I9@", preview.Closed[0].Wrote);
        Assert.Contains("@I8@", preview.Closed[1].Wrote);
    }
}
