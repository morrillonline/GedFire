using GedCore.Ged70;
using GedCore.Points;
using GedFire.TargetSelection;

namespace GedCore.Tests;

public class DealComparisonTests
{
    const string Head = "0 HEAD\n1 GEDC\n2 VERS 7.0\n";
    const string Tail = "0 @S1@ SOUR\n1 TITL Town register\n0 TRLR\n";

    static GedDocument Document(string records) => Ged70Parser.Parse(Head + records.ReplaceLineEndings("\n").Trim() + "\n" + Tail);

    static DealComparison Between(string before, string after, StudyScope? scope = null) =>
        new(Document(before), Document(after), scope);

    static readonly string SparseCouple = """
        0 @I1@ INDI
        1 NAME Amos /Ashworth/
        1 SEX M
        1 BIRT
        2 DATE ABT 1850
        1 FAMS @F1@
        0 @I2@ INDI
        1 NAME Ruth /Holt/
        1 SEX F
        1 FAMS @F1@
        0 @F1@ FAM
        1 HUSB @I1@
        1 WIFE @I2@
        """.ReplaceLineEndings("\n");

    [Fact]
    public void ClosedEnrichGaps_AreThePartsTheDealListedBeforeAndNoLongerListsAfter()
    {
        var deal = Between(SparseCouple, """
            0 @I1@ INDI
            1 NAME Amos /Ashworth/
            1 SEX M
            1 BIRT
            2 DATE 3 JUN 1850
            2 SOUR @S1@
            1 DEAT
            2 DATE 9 OCT 1911
            2 PLAC Salem, Oregon
            2 SOUR @S1@
            1 RESI
            2 DATE 1 JUN 1880
            2 PLAC Salem, Oregon
            2 SOUR @S1@
            1 FAMS @F1@
            0 @I2@ INDI
            1 NAME Ruth /Holt/
            1 SEX F
            1 FAMS @F1@
            0 @F1@ FAM
            1 HUSB @I1@
            1 WIFE @I2@
            1 MARR
            2 DATE 4 MAY 1874
            """);

        Assert.Equal(
            [
                new EnrichGap("BIRT", "@I1@", "date"), new EnrichGap("BIRT", "@I1@", "citation"),
                new EnrichGap("DEAT", "@I1@", "date"), new EnrichGap("DEAT", "@I1@", "place"), new EnrichGap("DEAT", "@I1@", "citation"),
                new EnrichGap("MARR", "@F1@", "date"),
            ],
            deal.ClosedEnrichGaps("@I1@"));
        Assert.Equal([new EnrichGap("MARR", "@F1@", "date")], deal.ClosedEnrichGaps("@I2@"));
    }

    [Fact]
    public void ADateOrPlaceThatIsStillNotExact_ClosesNoGap()
    {
        var deal = Between(SparseCouple, SparseCouple.Replace("2 DATE ABT 1850", "2 DATE 1850\n2 PLAC Oregon"));

        Assert.Empty(deal.ClosedEnrichGaps("@I1@"));
    }

    [Fact]
    public void AnExactBurial_ClosesTheDeathGapsItStandsFor()
    {
        var deal = Between(SparseCouple, SparseCouple.Replace(
            "1 FAMS @F1@\n0 @I2@", "1 BURI\n2 DATE 12 OCT 1911\n2 PLAC Salem, Oregon\n1 FAMS @F1@\n0 @I2@"));

        Assert.Equal(["date", "place", "citation"], deal.ClosedEnrichGaps("@I1@").Where(g => g.Tag == "DEAT").Select(g => g.Part));
    }

    [Fact]
    public void AMarriageThePersonIsNoLongerAPartnerIn_ClosesNothing()
    {
        string unlinked = SparseCouple.Replace("1 FAMS @F1@\n0 @I2@", "0 @I2@").Replace("1 HUSB @I1@\n", "");
        Assert.DoesNotContain("@I1@\n1 WIFE", unlinked);
        Assert.Equal(1, unlinked.Split("FAMS @F1@").Length - 1);

        Assert.Empty(Between(SparseCouple, unlinked).ClosedEnrichGaps("@I1@"));
    }

    [Fact]
    public void APersonTheChangesetCreates_HadNoGapsToClose()
    {
        var deal = Between(SparseCouple, SparseCouple + "\n0 @I3@ INDI\n1 NAME Eli /Ashworth/\n1 BIRT\n2 DATE 3 JUN 1875\n2 PLAC Salem, Oregon\n2 SOUR @S1@");

        Assert.Empty(deal.ClosedEnrichGaps("@I3@"));
    }

    const string Bachelor = "0 @I1@ INDI\n1 NAME Amos /Ashworth/\n1 SEX M";

    [Theory]
    [InlineData("", "\n1 NMR 0\n2 SOUR @S1@", true)]
    [InlineData("", "\n1 NMR 0", false)]                                    // uncited, so not proven
    [InlineData("", "\n1 NMR 1\n2 SOUR @S1@", false)]                       // married once
    [InlineData("\n1 NMR 0\n2 SOUR @S1@", "\n1 NMR 0\n2 SOUR @S1@", false)] // already recorded, so this changeset did not record it
    public void RecordedNeverMarried_NeedsACitedZeroTheTreeDidNotHold(string before, string after, bool recorded) =>
        Assert.Equal(recorded, Between(Bachelor + before, Bachelor + after).RecordedNeverMarried("@I1@"));

    [Fact]
    public void WithheldReason_JudgesTheStudyScopeOnTheTreeTheCardWasDealtFrom()
    {
        var deal = Between(SparseCouple, SparseCouple.Replace("Ruth /Holt/", "Ruth /Ashworth/"), new StudyScope(["Ashworth"]));

        Assert.StartsWith(StudyScope.ReasonPrefix, deal.WithheldReason(new PointsCard("@I2@", "New parent", null, null, 10), null));
        Assert.Null(deal.WithheldReason(new PointsCard("@I2@", "enrich-person", "enrich-fact", null, 1), null));
        Assert.Null(deal.WithheldReason(new PointsCard("@I2@", "new-child", null, null, 5), "@F1@"));
        Assert.Null(deal.WithheldReason(new PointsCard("@I1@", "biography", null, null, 3), null));
        Assert.Null(deal.WithheldReason(new PointsCard("@I9@", "new-parent", null, null, 10), null));
    }

    [Fact]
    public void WithheldReason_WithNoScope_WithholdsNothing() =>
        Assert.Null(Between(SparseCouple, SparseCouple).WithheldReason(new PointsCard("@I2@", "new-parent", null, null, 10), null));
}
