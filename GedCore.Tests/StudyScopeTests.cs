using GedFire.Gen;
using GedFire.TargetSelection;

namespace GedCore.Tests;

public class StudyScopeTests
{
    static readonly StudyScope Scope = new(["Ashworth", " Ashwirth "]);

    static GedIndividual Person(string xref, string surname, bool male = true) =>
        new() { Xref = xref, FirstName = "Test", LastNameRaw = surname, IsMale = male };

    static GedFamily Marry(string xref, GedIndividual? husband, GedIndividual? wife)
    {
        var family = new GedFamily { Xref = xref, Husband = husband, Wife = wife };
        husband?.FamSpouse.Add(family);
        wife?.FamSpouse.Add(family);
        return family;
    }

    [Theory]
    [InlineData("Ashworth")]
    [InlineData("ASHWIRTH")]
    public void SomeoneWhoBearsAStudySurname_GetsEveryCard(string surname)
    {
        var bearer = Person("@I1@", surname);

        Assert.All(Enum.GetValues<CardType>(), type => Assert.Null(Scope.WithheldReason(bearer, type)));
    }

    [Fact]
    public void ASpouseWhoDoesNotBearOne_GetsOnlyEnrichPerson()
    {
        var wife = Person("@I2@", "Holt", male: false);
        Marry("@F1@", Person("@I1@", "Ashworth"), wife);

        Assert.Null(Scope.WithheldReason(wife, CardType.EnrichPerson));
        Assert.All([CardType.NewParent, CardType.NewSpouse, CardType.Biography], type => Assert.Equal(
            "out of study scope: bears no study surname; the spouse of someone who does is dealt only Enrich person, and New child for a family whose husband bears one",
            Scope.WithheldReason(wife, type)));
    }

    [Fact]
    public void AnyoneElse_GetsNoCard()
    {
        var stranger = Person("@I1@", "Holt");
        var family = Marry("@F1@", stranger, Person("@I2@", "Pike", male: false));

        Assert.All(Enum.GetValues<CardType>(), type => Assert.Equal(
            "out of study scope: bears no study surname and is not married to someone who does",
            Scope.WithheldReason(stranger, type, family)));
    }

    [Fact]
    public void ANewChildCard_IsDealtToEitherSpouseWhenTheHusbandBearsAStudySurname()
    {
        var husband = Person("@I1@", "Ashworth");
        var wife = Person("@I2@", "Holt", male: false);
        var family = Marry("@F1@", husband, wife);

        Assert.Null(Scope.WithheldReason(husband, CardType.NewChild, family));
        Assert.Null(Scope.WithheldReason(wife, CardType.NewChild, family));
    }

    [Fact]
    public void ANewChildCard_IsWithheldFromBothSpousesWhenTheHusbandDoesNotBearOne()
    {
        var husband = Person("@I1@", "Holt");
        var wife = Person("@I2@", "Ashworth", male: false);
        var family = Marry("@F1@", husband, wife);
        const string reason = "out of study scope: @F1@ has no husband who bears a study surname, so its children would not bear one";

        Assert.Equal(reason, Scope.WithheldReason(wife, CardType.NewChild, family));
        Assert.Equal(reason, Scope.WithheldReason(husband, CardType.NewChild, family));
    }

    [Fact]
    public void ANewChildCard_IsWithheldWhenTheFamilyRecordsNoHusband()
    {
        var wife = Person("@I2@", "Ashworth", male: false);

        Assert.StartsWith(StudyScope.ReasonPrefix, Scope.WithheldReason(wife, CardType.NewChild, Marry("@F1@", null, wife)));
    }

    [Fact]
    public void ANewChildCardWithNoFamilyNamed_IsJudgedOnThePersonAlone()
    {
        var wife = Person("@I2@", "Ashworth", male: false);
        Marry("@F1@", Person("@I1@", "Holt"), wife);

        Assert.Null(Scope.WithheldReason(wife, CardType.NewChild));
    }
}
