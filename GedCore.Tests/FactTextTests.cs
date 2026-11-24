using GedFire.Gen;

namespace GedCore.Tests;

public class FactTextTests
{
    static GedEvent Fact(string tag, params (string Tag, string Value)[] details)
    {
        var fact = new GedEvent { Tag = tag };
        fact.Details.AddRange(details.Select(d => new GedEventDetail(d.Tag, d.Value)));
        return fact;
    }

    [Theory]
    [InlineData("OCCU", "Occupation")]
    [InlineData("CENS", "Census")]
    [InlineData("DIVF", "Divorce filed")]
    [InlineData("_MILT", "Fact")]
    public void Heading_NamesTheFact(string tag, string heading) =>
        Assert.Equal(heading, FactText.Heading(Fact(tag)));

    [Fact]
    public void ATypedFact_IsHeadedAndNamedByItsType_WhichIsNotRepeatedAsADetail()
    {
        var fact = Fact("EVEN", ("TYPE", "Election"), ("AGE", "40y"));

        Assert.Equal("Election", FactText.Heading(fact));
        Assert.Equal("Election", FactText.Word(fact));
        Assert.Equal(["age: 40 years"], FactText.Details(fact));
    }

    [Fact]
    public void AnUntypedEven_FallsBackToItsTagsWord() =>
        Assert.Equal("Event", FactText.Heading(Fact("EVEN")));

    [Fact]
    public void Details_ReadInDocumentOrder_EachLabelled()
    {
        var fact = Fact("MARR", ("TYPE", "Civil"), ("HUSB", "25y"), ("WIFE", "1y"), ("CAUS", "Mutual consent"),
            ("AGNC", "Town clerk"), ("RELI", "Quaker"), ("ADDR", "1 Mill St\nBrackenford"), ("NOTE", "By licence."));

        Assert.Equal(
            ["type: Civil", "husband's age: 25 years", "wife's age: 1 year", "cause: Mutual consent", "agency: Town clerk",
             "religion: Quaker", "address: 1 Mill St, Brackenford", "By licence."],
            FactText.Details(fact));
    }

    [Theory]
    [InlineData("49y", "49 years")]
    [InlineData("> 2y 3m", "more than 2 years 3 months")]
    [InlineData("<1w 1d", "less than 1 week 1 day")]
    [InlineData("INFANT", "INFANT")]
    public void Age_ReadsInWords_OrAsWritten(string age, string words) =>
        Assert.Equal(words, FactText.Age(age));
}
