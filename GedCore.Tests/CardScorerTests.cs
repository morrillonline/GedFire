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

    /// <summary>The deal as a test states it: the gaps a changeset closed, the never-married answers it recorded, the cards withheld.</summary>
    sealed class FakeDeal : ICardDeal
    {
        public Dictionary<string, EnrichGap[]> Closed { get; } = [];
        public HashSet<string> NeverMarried { get; } = [];
        public Func<PointsCard, string?, string?> Withheld { get; init; } = (_, _) => null;

        public IReadOnlyList<EnrichGap> ClosedEnrichGaps(string xref) => Closed.GetValueOrDefault(xref) ?? [];
        public bool RecordedNeverMarried(string xref) => NeverMarried.Contains(xref);
        public string? WithheldReason(PointsCard card, string? family) => Withheld(card, family);
    }

    static PointsCard EnrichPerson(string xref = "@I1@", int bonus = 0, string? gap = "enrich-fact") =>
        new(xref, "enrich-person", gap, null, 1, bonus);

    [Fact]
    public void EnrichDeath_ClosesWhenDeathIsWrittenOnTheSubject()
    {
        var preview = CardScorer.Preview([Enrich("enrich-death")], Facts(new FactWrite("@I1@", "DEAT", "1 JAN 1900", null)));

        var closed = Assert.Single(preview.Closed);
        Assert.Equal("@I1@|enrich-person|enrich-death", closed.CardKey);
        Assert.Equal(7, closed.Points);
        Assert.Equal("DEAT @I1@ (first date)", closed.Wrote);
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

        Assert.Equal("MARR @F1@ (first date)", Assert.Single(preview.Closed).Wrote);
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
    public void AnEnrichPersonCard_IsPaidForEachGapTheDealListedThatTheChangesetClosed(string? gap)
    {
        var deal = new FakeDeal
        {
            Closed =
            {
                ["@I1@"] = [new("BIRT", "@I1@", "date"), new("BIRT", "@I1@", "citation"), new("MARR", "@F1@", "place")],
                ["@I2@"] = [new("DEAT", "@I2@", "date")],
            },
        };

        var preview = CardScorer.Preview([EnrichPerson(gap: gap)], Facts(), deal);

        var closed = Assert.Single(preview.Closed);
        Assert.Equal(3, closed.Points);
        Assert.Equal("BIRT @I1@ (date and citation); MARR @F1@ (place)", closed.Wrote);
        Assert.Equal(3, preview.Total);
    }

    [Fact]
    public void AnEnrichPersonCard_AddsItsDifficultyBonusOnceNotPerGap()
    {
        var deal = new FakeDeal { Closed = { ["@I1@"] = [new("DEAT", "@I1@", "date"), new("DEAT", "@I1@", "place")] } };

        Assert.Equal(6, Assert.Single(CardScorer.Preview([EnrichPerson(bonus: 4)], Facts(), deal).Closed).Points);
    }

    [Fact]
    public void AnEnrichPersonCard_IsPaidNothingForAWriteTheDealDidNotList()
    {
        var changes = Facts(
            new FactWrite("@I1@", "RESI", "1 JUN 1880", "Salem, Oregon") { CitationImproved = true },
            new FactWrite("@I1@", "BURI", null, "Salem Cemetery, Salem, Oregon") { CitationImproved = true },
            new FactWrite("@I1@", "BIRT", "ABT 1850", null));

        var preview = CardScorer.Preview([EnrichPerson(bonus: 4)], changes, new FakeDeal());

        Assert.Equal(0, preview.Total);
        Assert.Equal(
            "no gap the deal lists for the subject is closed: a birth, death or marriage date or place made exact, or its first citation (wrote RESI, BURI, BIRT)",
            Assert.Single(preview.NotClosed).Reason);
    }

    [Fact]
    public void AnEnrichPersonCard_WithoutTheDeal_ClosesNothing()
    {
        var preview = CardScorer.Preview([EnrichPerson()], Facts(new FactWrite("@I1@", "DEAT", "1 JAN 1900", "Salem, Oregon")));

        Assert.Empty(preview.Closed);
    }

    [Fact]
    public void AMarriagesGaps_ArePaidToOneSpousesCard()
    {
        var marriage = new EnrichGap("MARR", "@F1@", "date");
        var deal = new FakeDeal { Closed = { ["@I1@"] = [marriage], ["@I2@"] = [marriage, new("BIRT", "@I2@", "place")] } };

        var preview = CardScorer.Preview([EnrichPerson("@I1@"), EnrichPerson("@I2@"), EnrichPerson("@I1@", gap: null)], Facts(), deal);

        Assert.Equal(
            [("@I1@|enrich-person|enrich-fact", 1, "MARR @F1@ (date)"), ("@I2@|enrich-person|enrich-fact", 1, "BIRT @I2@ (place)")],
            preview.Closed.Select(c => (c.CardKey, c.Points, c.Wrote)));
        Assert.Equal("the gaps this changeset closes are already paid to card @I1@|enrich-person|enrich-fact", Assert.Single(preview.NotClosed).Reason);
    }

    [Fact]
    public void ANamedDatePlaceGap_IsNotClosedByACitationAlone()
    {
        var changes = Facts(new FactWrite("@I1@", "BIRT", "1850", "Duluth", DateImprovement.None, PlaceImproved: false, CitationImproved: true));

        Assert.Single(CardScorer.Preview([Enrich("enrich-birth-date")], changes).NotClosed);
        Assert.Single(CardScorer.Preview([Enrich("enrich-birth-place")], changes).NotClosed);
        Assert.Single(CardScorer.Preview([Enrich("enrich-birth")], changes).Closed);
    }

    [Fact]
    public void ANamedGapCard_IgnoresTheBonusBecauseItsPointsAlreadyIncludeIt()
    {
        var card = new PointsCard("@I1@", "enrich-person", "enrich-death", null, 7, Bonus: 4);

        var preview = CardScorer.Preview([card], Facts(new FactWrite("@I1@", "DEAT", "1900", null)));

        Assert.Equal(7, Assert.Single(preview.Closed).Points);
    }

    [Fact]
    public void EnrichBurial_ClosesOnABurialWrite()
    {
        var preview = CardScorer.Preview([Enrich("enrich-burial-place")], Facts(new FactWrite("@I1@", "BURI", null, "Salem Cemetery")));

        Assert.Equal("BURI @I1@ (place)", Assert.Single(preview.Closed).Wrote);
    }

    [Theory]
    [InlineData(DateImprovement.First, "DEAT @I1@ (first date)")]
    [InlineData(DateImprovement.Refined, "DEAT @I1@ (refined date)")]
    [InlineData(DateImprovement.Narrowed, "DEAT @I1@ (narrowed date)")]
    [InlineData(DateImprovement.Corrected, "DEAT @I1@ (corrected date)")]
    public void AClosedCard_NamesTheKindOfDateImprovementThatPaid(DateImprovement kind, string wrote)
    {
        var changes = Facts(new FactWrite("@I1@", "DEAT", "BEF 1797", null, kind, PlaceImproved: false));

        Assert.Equal(wrote, Assert.Single(CardScorer.Preview([Enrich("enrich-death-date")], changes).Closed).Wrote);
    }

    [Fact]
    public void ADateThatDoesNotImprove_LeavesADateCardOpen()
    {
        var changes = Facts(new FactWrite("@I1@", "DEAT", "1877", null, DateImprovement.None, PlaceImproved: false, CitationImproved: true));

        Assert.Single(CardScorer.Preview([Enrich("enrich-death-date")], changes).NotClosed);
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
    public void NewSpouse_ClosesOnANewlyRecordedNeverMarriedAnswer_AndOnlyForItsSubject()
    {
        var deal = new FakeDeal { NeverMarried = { "@I1@" } };
        var cards = new[] { new PointsCard("@I1@", "new-spouse", null, null, 10), new PointsCard("@I2@", "new-spouse", null, null, 10) };

        var preview = CardScorer.Preview(cards, Facts(), deal);

        var closed = Assert.Single(preview.Closed);
        Assert.Equal("@I1@|new-spouse", closed.CardKey);
        Assert.Equal("NMR 0 on @I1@ (never married, cited)", closed.Wrote);
        Assert.Equal(
            "no spouse added to a family the subject is a partner in, and no cited never-married answer (NMR 0) recorded",
            Assert.Single(preview.NotClosed).Reason);
    }

    [Fact]
    public void ACardTheDealWithholds_IsNeverClosed()
    {
        var deal = new FakeDeal
        {
            Closed = { ["@I1@"] = [new("BIRT", "@I1@", "date")] },
            Withheld = (card, _) => card.Xref == "@I1@" ? "out of study scope: bears no study surname" : null,
        };
        var changes = WithFamily("@F1@", new FamilyChange(["@I5@"], ["@I1@", "@I2@"], ["@I5@"], []));
        var cards = new[]
        {
            EnrichPerson("@I1@"),
            new PointsCard("@I1@", "new-parent", null, null, 10),
            new PointsCard("@I2@", "new-parent", null, null, 10),
        };

        var preview = CardScorer.Preview(cards, changes, deal);

        Assert.Equal("@I2@|new-parent", Assert.Single(preview.Closed).CardKey);
        Assert.Equal(["@I1@|enrich-person|enrich-fact", "@I1@|new-parent"], preview.NotClosed.Select(c => c.CardKey));
        Assert.All(preview.NotClosed, c => Assert.Equal("out of study scope: bears no study surname", c.Reason));
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

    static TreeChanges Biography(string xref, params BiographyCitation[] citations) =>
        new([], NoNames, new Dictionary<string, FamilyChange>(), biographies: [new BiographyWrite(xref, citations)]);

    [Fact]
    public void BiographyCard_ClosesAtItsPointsWhenTheSubjectsBiographyGainedACitation()
    {
        var preview = CardScorer.Preview(
            [new PointsCard("@I1@", "Biography", null, null, 8, Bonus: 5)],
            Biography("@I1@", new BiographyCitation("@S1@", "File 4471")));

        var closed = Assert.Single(preview.Closed);
        Assert.Equal("@I1@|Biography", closed.CardKey);
        Assert.Equal(8, closed.Points);
        Assert.Equal("biography note on @I1@ citing @S1@ \"File 4471\"", closed.Wrote);
    }

    [Fact]
    public void BiographyCard_StaysOpenWhenOnlySomeoneElsesBiographyGainedACitation()
    {
        var preview = CardScorer.Preview(
            [new PointsCard("@I1@", "biography", null, null, 3)], Biography("@I2@", new BiographyCitation("@S1@", null)));

        Assert.Contains("no biography note on the subject added text or a citation", Assert.Single(preview.NotClosed).Reason);
    }

    [Fact]
    public void BiographyCards_PayOncePerPersonPerChangeset_AlongsideFactCards()
    {
        var changes = new TreeChanges(
            [new FactWrite("@I1@", "DEAT", "1 JAN 1900", null)], NoNames, new Dictionary<string, FamilyChange>(),
            biographies: [new BiographyWrite("@I1@", [new BiographyCitation("@S1@", "p. 1"), new BiographyCitation("@S2@", "p. 2")])]);

        var preview = CardScorer.Preview(
            [new PointsCard("@I1@", "biography", null, null, 8), new PointsCard("@I1@", "biography", "again", null, 8), Enrich("enrich-death")],
            changes);

        Assert.Equal(["@I1@|biography", "@I1@|enrich-person|enrich-death"], preview.Closed.Select(c => c.CardKey));
        Assert.Equal(15, preview.Total);
        Assert.Contains("already takes this changeset's one Biography award for @I1@", Assert.Single(preview.NotClosed).Reason);
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
    public void TwoSpousesNewChildCards_TwoChildrenOfTheirFamily_CloseExactlyOne()
    {
        var changes = new TreeChanges([],
            new Dictionary<string, NameChange> { ["@I8@"] = new(null, "Ann /Test/"), ["@I9@"] = new(null, "Ruth /Test/") },
            new Dictionary<string, FamilyChange> { ["@F1@"] = new(["@I1@", "@I2@"], ["@I8@", "@I9@"], [], ["@I8@", "@I9@"]) });
        var cards = new[]
        {
            new PointsCard("@I2@", "new-child", "children", null, 10),
            new PointsCard("@I1@", "new-child", "children", null, 5),
        };

        var preview = CardScorer.Preview(cards, changes);

        var closed = Assert.Single(preview.Closed);
        Assert.Equal("@I1@|new-child|children", closed.CardKey);
        Assert.Equal("CHIL @I8@ in @F1@", closed.Wrote);
        Assert.Equal(5, preview.Total);
        Assert.Equal(
            "not closed by this apply: @F1@'s New child card is already closed by card @I1@|new-child|children (CHIL @I8@) — a family's New child card is paid once",
            Assert.Single(preview.NotClosed).Reason);
    }

    static TreeChanges ChildIn(string family, FamilyChange change) =>
        new([], new Dictionary<string, NameChange> { ["@I9@"] = new(null, "Ruth /Test/") },
            new Dictionary<string, FamilyChange> { [family] = change });

    [Fact]
    public void ANewChildCard_IsNotClosedByAChildOfAFamilyTheChangesetCreates()
    {
        var changes = ChildIn("@F2@", new FamilyChange(["@I5@", "@I1@"], ["@I9@"], ["@I5@", "@I1@"], ["@I9@"]) { Created = true });

        var preview = CardScorer.Preview([new PointsCard("@I1@", "new-child", "children", null, 10)], changes);

        Assert.Empty(preview.Closed);
        Assert.Equal(
            "@I9@ joined @F2@, a family this changeset creates; a New child card is dealt for a family that was childless before it",
            Assert.Single(preview.NotClosed).Reason);
    }

    [Fact]
    public void ANewChildCard_IsNotClosedByAChildJoiningAFamilyThatAlreadyHadOne()
    {
        var changes = ChildIn("@F1@", new FamilyChange(["@I1@", "@I2@"], ["@I7@", "@I9@"], [], ["@I9@"]) { HadChildren = true });

        var preview = CardScorer.Preview([new PointsCard("@I1@", "new-child", null, null, 10)], changes);

        Assert.Equal("@I9@ joined @F1@, which already had a child, so it had no New child card", Assert.Single(preview.NotClosed).Reason);
    }

    [Fact]
    public void ANewChildCardNamingItsFamily_ClosesOnlyOnAChildJoiningThatFamily_AndRepeatsIt()
    {
        var changes = ChildIn("@F2@", new FamilyChange(["@I1@", "@I3@"], ["@I9@"], [], ["@I9@"]));
        var cards = new[]
        {
            new PointsCard("@I1@", "new-child", "children", null, 10) { Family = "@F1@" },
            new PointsCard("@I1@", "new-child", "children", null, 10) { Family = "@F2@" },
        };

        var preview = CardScorer.Preview(cards, changes);

        var closed = Assert.Single(preview.Closed);
        Assert.Equal(("@F2@", "CHIL @I9@ in @F2@"), (closed.Family, closed.Wrote));
        var open = Assert.Single(preview.NotClosed);
        Assert.Equal(("@F1@", "@I9@ joined @F2@, not the card's family @F1@"), (open.Family, open.Reason));
    }

    [Fact]
    public void ANewChildCard_IsNotClosedWhenTheDealWithholdsTheFamilysCard()
    {
        var deal = new FakeDeal { Withheld = (_, family) => family == "@F1@" ? "out of study scope: @F1@ has no husband who bears a study surname" : null };
        var changes = ChildIn("@F1@", new FamilyChange(["@I1@", "@I2@"], ["@I9@"], [], ["@I9@"]));

        var preview = CardScorer.Preview([new PointsCard("@I1@", "new-child", null, null, 10)], changes, deal);

        Assert.Equal(
            "@I9@ joined @F1@, whose New child card is withheld (out of study scope: @F1@ has no husband who bears a study surname)",
            Assert.Single(preview.NotClosed).Reason);
    }
}
