using GedCore.Ged70;

namespace GedCore.Tests;

public class Ged70DocumentFactoryTests
{
    [Fact]
    public void CreateSeeded_WritesCanonicalDocumentWithNamedPerson()
    {
        var document = Ged70DocumentFactory.CreateSeeded("Ada /Example/", sex: "F");

        var output = new MemoryStream();
        Ged70Formatter.Write(document, output);
        byte[] bytes = output.ToArray();
        var reparsed = Ged70Parser.Read(new MemoryStream(bytes));

        Assert.Equal(["HEAD", "INDI", "TRLR"], reparsed.Records.Select(record => record.Tag));
        var person = reparsed.ByXref["@I00001@"];
        Assert.Equal("Ada /Example/", person.FirstChild("NAME")!.Value);
        Assert.Equal("F", person.FirstChild("SEX")!.Value);
        Assert.NotNull(person.FirstChild("UID"));

        var rewritten = new MemoryStream();
        Ged70Formatter.Write(reparsed, rewritten);
        Assert.Equal(bytes, rewritten.ToArray());
    }

    [Theory]
    [InlineData("", "@I00001@", null)]
    [InlineData("Ada /Example/", "I00001", null)]
    [InlineData("Ada /Example/", "@I00001@", "Q")]
    public void CreateSeeded_RejectsInvalidSeed(string name, string xref, string? sex)
    {
        Assert.Throws<ArgumentException>(() => Ged70DocumentFactory.CreateSeeded(name, xref, sex));
    }
}