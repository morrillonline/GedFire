using GedCore.Ged55;
using GedFire.Gen;
using GedFire.Match;
using GedFire.TargetSelection;

namespace GedCore.Tests;

public class DuplicateAnnotatorTests
{
    const string Ged = """
        0 HEAD
        1 GEDC
        2 VERS 5.5.1
        0 @I1@ INDI
        1 NAME William /Ashworth/
        1 SEX M
        1 BIRT
        2 DATE 1852
        2 PLAC Missouri
        0 @I2@ INDI
        1 NAME William /Ashworth/
        1 SEX M
        1 BIRT
        2 DATE 1852
        2 PLAC Missouri
        0 @I3@ INDI
        1 NAME Harriet /Bell/
        1 SEX F
        1 BIRT
        2 DATE 1855
        """;

    static MatchIndex Index() => new(ModelBuilder.Build(Ged55Parser.Parse(Ged)));

    static SelectionTarget Target(string xref) => new()
    {
        Xref = xref,
        Name = "Someone",
        Surname = "Ashworth",
        CardType = "New parent",
        NominalPoints = 5,
        Difficulty = new DifficultyEntry { Band = DifficultyBand.Common, EraWeight = 0, GeoWeight = 0, ContextAdjustment = 0 },
        Score = 5,
    };

    static DrawResult Draw(params string[] xrefs) =>
        new() { Targets = [.. xrefs.Select(Target)], Seed = 1, LegendaryDiscards = [] };

    [Fact]
    public void Annotate_TargetWithTwin_ListsTheOtherPersonWithScore()
    {
        var result = DuplicateAnnotator.Annotate(Draw("@I1@"), Index());

        var entry = Assert.Single(Assert.Single(result.Targets).PossibleDuplicateOf);
        Assert.Equal("@I2@", entry.Xref);
        Assert.True(entry.Score >= 70);
    }

    [Fact]
    public void Annotate_BothTwinsDrawn_EachListsTheOther()
    {
        var result = DuplicateAnnotator.Annotate(Draw("@I1@", "@I2@"), Index());

        Assert.Equal("@I2@", Assert.Single(result.Targets[0].PossibleDuplicateOf).Xref);
        Assert.Equal("@I1@", Assert.Single(result.Targets[1].PossibleDuplicateOf).Xref);
    }

    [Fact]
    public void Annotate_TargetWithoutTwin_HasEmptyList()
    {
        var result = DuplicateAnnotator.Annotate(Draw("@I3@"), Index());

        Assert.Empty(Assert.Single(result.Targets).PossibleDuplicateOf);
    }

    [Fact]
    public void Annotate_KeepsSeedAndDiscards()
    {
        var result = DuplicateAnnotator.Annotate(Draw("@I1@"), Index());

        Assert.Equal(1, result.Seed);
        Assert.Empty(result.LegendaryDiscards);
    }
}
