using GedFire.Gen;
using GedFire.Match;

namespace GedCore.Tests;

public class UnanchoredPeopleFinderTests
{
    const string Ged = """
        0 HEAD
        1 GEDC
        2 VERS 5.5.1
        0 @S1@ SOUR
        1 TITL Parish Register
        0 @I1@ INDI
        1 NAME Alone /Loner/
        0 @I2@ INDI
        1 NAME Dated /Placed/
        1 BIRT
        2 DATE 1800
        1 FAMS @F1@
        0 @I3@ INDI
        1 NAME Spouse /Dateless/
        1 FAMS @F1@
        0 @F1@ FAM
        1 HUSB @I2@
        1 WIFE @I3@
        0 @I4@ INDI
        1 NAME Joseph /Bare/
        1 FAMS @F2@
        0 @I5@ INDI
        1 NAME Child /Bare/
        1 FAMC @F2@
        0 @F2@ FAM
        1 HUSB @I4@
        1 CHIL @I5@
        0 @I6@ INDI
        1 NAME Top /Chain/
        1 FAMS @F3@
        0 @I7@ INDI
        1 NAME Middle /Chain/
        1 FAMC @F3@
        0 @I8@ INDI
        1 NAME Placed /Chain/
        1 BIRT
        2 PLAC Harwick
        1 FAMC @F3@
        0 @F3@ FAM
        1 HUSB @I6@
        1 CHIL @I7@
        1 CHIL @I8@
        0 @I9@ INDI
        1 NAME Noted /Facts/
        1 BIRT
        2 SOUR @S1@
        1 NOTE Some prose about the person.
        0 @I10@ INDI
        1 NAME Hidden /Private/
        1 RESN CONFIDENTIAL
        1 FAMS @F4@
        0 @I11@ INDI
        1 NAME Neighbor /Private/
        1 FAMC @F4@
        0 @F4@ FAM
        1 HUSB @I10@
        1 CHIL @I11@
        """;

    static GedModel Model() => MatchTestModels.Build(Ged);

    static IReadOnlyList<string> Xrefs(GedModel model) =>
        [.. UnanchoredPeopleFinder.Find(model).Select(u => u.Person.Xref)];

    [Fact]
    public void PersonWithNoFactsAndNoFamilyTies_IsListedWithNoTies()
    {
        var loner = UnanchoredPeopleFinder.Find(Model()).Single(u => u.Person.Xref == "@I1@");

        Assert.Equal(0, loner.FactCount);
        Assert.Empty(loner.TiedOnlyToUnanchored);
    }

    [Fact]
    public void PersonWithADate_IsNotListed()
    {
        Assert.DoesNotContain("@I2@", Xrefs(Model()));
    }

    [Fact]
    public void DatelessPersonMarriedToAPlacedPerson_IsNotListed()
    {
        Assert.DoesNotContain("@I3@", Xrefs(Model()));
    }

    [Fact]
    public void TwoDatelessPeopleTiedOnlyToEachOther_AreBothListedNamingEachOther()
    {
        var people = UnanchoredPeopleFinder.Find(Model());

        Assert.Equal(["@I5@"], people.Single(u => u.Person.Xref == "@I4@").TiedOnlyToUnanchored);
        Assert.Equal(["@I4@"], people.Single(u => u.Person.Xref == "@I5@").TiedOnlyToUnanchored);
    }

    [Fact]
    public void ATieToAPlacedRelative_AnchorsEveryoneConnectedOnlyThroughIt()
    {
        var xrefs = Xrefs(Model());

        Assert.DoesNotContain("@I6@", xrefs);
        Assert.DoesNotContain("@I7@", xrefs);
        Assert.DoesNotContain("@I8@", xrefs);
    }

    [Fact]
    public void FactCount_CountsEventsNotesMediaAndCitations()
    {
        var noted = UnanchoredPeopleFinder.Find(Model()).Single(u => u.Person.Xref == "@I9@");

        // the undated birth, its citation, and the note
        Assert.Equal(3, noted.FactCount);
    }

    [Fact]
    public void PeopleHiddenByPrivacyEnforcement_AreNeverListedAndAnchorTheirRelatives()
    {
        var model = Model();
        Assert.Contains("@I10@", Xrefs(model));
        Assert.Contains("@I11@", Xrefs(model));

        PrivacyFilter.Apply(model, 2026);

        var xrefs = Xrefs(model);
        Assert.DoesNotContain("@I10@", xrefs);
        Assert.DoesNotContain("@I11@", xrefs);
    }

    [Fact]
    public void ResultIsOrderedBySurnameGivenNameAndXref()
    {
        var model = MatchTestModels.Build("""
            0 @I2@ INDI
            1 NAME Zed /Zulu/
            0 @I1@ INDI
            1 NAME Amy /Zulu/
            0 @I3@ INDI
            1 NAME Bob /Alpha/
            """);

        Assert.Equal(["@I3@", "@I1@", "@I2@"], Xrefs(model));
    }

    [Fact]
    public void EmptyModel_ListsNobody()
    {
        Assert.Empty(UnanchoredPeopleFinder.Find(MatchTestModels.Build("0 HEAD\n")));
    }
}
