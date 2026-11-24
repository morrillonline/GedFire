using GedCore.Ged70;
using GedFire.Gen;

namespace GedCore.Tests;

/// <summary>Every fact a changeset can write reaches the family page, each in the same row or phrase form.</summary>
public class SiteGeneratorFactTests
{
    const string Template = "<html><head><title><insert title></title></head><body><insert body></body></html>";

    // Ezra and Ruth have two children. Amos is childless, so has no page of his own; his wife Bea has her own card
    // on her father's page. Lydia's childless husband Silas appears only on her card, with his own childless second
    // marriage, to Kate. Ezra's second marriage, to Joan, is childless; Ruth's other marriage, to Hugh, has a child.
    const string Ged = """
        0 HEAD
        1 GEDC
        2 VERS 7.0
        0 @S1@ SOUR
        1 AUTH Town Clerk
        1 TITL Town Records
        1 NOTE Town Records, online at https://example.org.
        0 @I1@ INDI
        1 NAME Ezra /Vale/
        1 SEX M
        1 BIRT
        2 DATE 1801
        1 OCCU Miller
        2 DATE 1850
        2 PLAC Salem, Massachusetts
        2 SOUR @S1@
        3 PAGE p. 4
        3 DATA
        4 TEXT Ezra Vale, miller
        3 NOTE The clerk spelled it Vail.
        1 CENS
        2 DATE 1860
        1 RESI Mill Street
        2 DATE 1845
        1 DEAT
        2 DATE 1870
        2 PLAC Salem, Massachusetts
        2 CAUS Pneumonia
        2 AGE 68y
        1 FAMS @F1@
        1 FAMS @F2@
        0 @I2@ INDI
        1 NAME Ruth /Hale/
        1 SEX F
        1 WILL
        2 DATE 1879
        1 PROB
        2 DATE 1880
        1 FAMS @F1@
        1 FAMS @F4@
        0 @I3@ INDI
        1 NAME Amos /Vale/
        1 SEX M
        1 OCCU Millwright
        1 PROB
        2 DATE 1890
        1 FAMC @F1@
        1 FAMS @F6@
        0 @I4@ INDI
        1 NAME Lydia /Vale/
        1 SEX F
        1 FAMC @F1@
        1 FAMS @F3@
        0 @I5@ INDI
        1 NAME Silas /Penn/
        1 SEX M
        1 OCCU Peddler
        1 NOTE Silas sold tinware.
        1 DEAT
        2 DATE 1890
        2 CAUS Fever
        1 FAMS @F3@
        1 FAMS @F5@
        0 @I6@ INDI
        1 NAME Joan /Reed/
        1 SEX F
        1 BIRT
        2 DATE 1815
        1 NOTE Joan kept a shop.
        1 FAMS @F2@
        0 @F1@ FAM
        1 HUSB @I1@
        1 WIFE @I2@
        1 ENGA
        2 DATE 1827
        1 MARR
        2 DATE 1828
        2 AGNC First Parish
        1 FACT Held the tavern licence
        2 TYPE Licence
        1 NOTE The couple kept the mill house.
        1 NOTE The family's pew was No. 12.
        2 SOUR @S1@
        3 PAGE p. 7
        1 CHIL @I3@
        1 CHIL @I4@
        0 @F2@ FAM
        1 HUSB @I1@
        1 WIFE @I6@
        1 ENGA
        2 DATE 1839
        1 MARR
        2 DATE 1840
        1 DIV
        2 DATE 1842
        1 NOTE They parted quickly.
        1 SOUR @S1@
        2 PAGE p. 9
        2 NOTE The clerk noted the separation.
        0 @F3@ FAM
        1 HUSB @I5@
        1 WIFE @I4@
        1 ENGA
        2 DATE 1854
        1 DIV
        2 DATE 1862
        1 NOTE They lived apart.
        0 @I7@ INDI
        1 NAME Hugh /Moss/
        1 SEX M
        1 FAMS @F4@
        0 @I8@ INDI
        1 NAME Pip /Moss/
        1 SEX M
        1 FAMC @F4@
        0 @I9@ INDI
        1 NAME Kate /Lowe/
        1 SEX F
        1 FAMS @F5@
        0 @I10@ INDI
        1 NAME Bea /Ward/
        1 SEX F
        1 BIRT
        2 DATE 1830
        1 OCCU Weaver
        1 FAMC @F7@
        1 FAMS @F6@
        0 @I11@ INDI
        1 NAME Old /Ward/
        1 SEX M
        1 FAMS @F7@
        0 @F4@ FAM
        1 HUSB @I7@
        1 WIFE @I2@
        1 CHIL @I8@
        0 @F5@ FAM
        1 HUSB @I5@
        1 WIFE @I9@
        1 MARR
        2 DATE 1865
        1 DIV
        2 DATE 1866
        1 NOTE Silas and Kate parted.
        0 @F6@ FAM
        1 HUSB @I3@
        1 WIFE @I10@
        1 MARR
        2 DATE 1860
        0 @F7@ FAM
        1 HUSB @I11@
        1 CHIL @I10@
        0 TRLR
        """;

    static readonly Lazy<string> Page = new(() =>
    {
        var model = ModelBuilder.Build(Ged70Parser.Parse(Ged));
        string dir = Directory.CreateTempSubdirectory("gedfire-gen-fact-tests-").FullName;
        try
        {
            new SiteGenerator(model, Template).Generate(dir);
            return File.ReadAllText(Path.Combine(dir, "ValeEzra-1801-1870.html"));
        }
        finally { Directory.Delete(dir, recursive: true); }
    });

    // The page with its footnote markers removed, so assertions read as the text does.
    static string Text => System.Text.RegularExpressions.Regex.Replace(Page.Value, "<sup>.*?</sup>", "");

    [Fact]
    public void AnAttribute_IsARowOfItsValueDateAndPlace() =>
        Assert.Contains("<dt>Occupation</dt><dd>Miller, 1850 <span class=\"place\">at Salem, Mass.</span></dd>", Text);

    [Fact]
    public void ACensusAndAResidence_AreEachNamed_AndAResidenceSaysWhere()
    {
        Assert.Contains("<dt>Census</dt><dd>1860</dd>", Text);
        Assert.Contains("<dt>Resided</dt><dd>Mill Street, 1845</dd>", Text);
    }

    [Fact]
    public void ALifesFacts_AreInDateOrder_BetweenTheBirthAndTheDeath()
    {
        int[] rows = [.. new[] { "<dt>Born</dt><dd>1801", "<dt>Resided</dt>", "<dt>Occupation</dt><dd>Miller",
                                 "<dt>Census</dt>", "<dt>Died</dt><dd>1870" }.Select(r => Text.IndexOf(r))];

        Assert.True(rows.All(r => r >= 0), "a row is missing");
        Assert.Equal(rows.Order(), rows);
    }

    [Fact]
    public void AFactsDetails_FollowItsPlace() =>
        Assert.Contains("<dt>Died</dt><dd>1870 <span class=\"place\">at Salem, Mass.; cause: Pneumonia; age: 68 years</span></dd>", Text);

    [Fact]
    public void TheCouplesFacts_AreRowsOnTheHusbandsCard_InDateOrder()
    {
        int engaged = Text.IndexOf("<dt>Engaged</dt><dd>1827</dd>");
        int married = Text.IndexOf("<dt>Married</dt><dd>1828");

        Assert.True(engaged >= 0 && married > engaged, "the engagement does not precede the marriage");
    }

    [Fact]
    public void TheCouplesNotes_AreOnTheHusbandsCard_WithTheCouplesFacts()
    {
        int note = Text.IndexOf("<p class=\"bio-inline\">NOTE: The couple kept the mill house.</p>");

        Assert.True(note > Text.IndexOf("class=\"role\">Husband<") && note < Text.IndexOf("class=\"role\">Wife<"),
            "the couple's note is not on the husband's card");
    }

    [Fact]
    public void ACitedFamilyNote_IsAParagraphWithItsFootnote() =>
        Assert.Matches("<p class=\"bio-inline\">The family's pew was No. 12.<sup>.*?</sup></p>", Page.Value);

    [Fact]
    public void AFamilyAttribute_IsHeadedByItsType_AndAFamilyEventShowsItsDetails()
    {
        Assert.Contains("<dt>Licence</dt><dd>Held the tavern licence</dd>", Text);
        Assert.Contains("<dt>Married</dt><dd>1828 <span class=\"place\">agency: First Parish</span></dd>", Text);
    }

    [Fact]
    public void AWillAndItsProbate_ShareOneRow() =>
        Assert.Contains("<dt>Will</dt><dd>1879, proved 1880</dd>", Text);

    [Fact]
    public void AProbateWithNoWill_IsItsOwnRow() =>
        Assert.Contains("<dt>Probate</dt><dd>1890</dd>", Text);

    [Fact]
    public void AChildWithNoPageOfTheirOwn_HasEveryFactOnTheirCard() =>
        Assert.Contains("<dt>Occupation</dt><dd>Millwright</dd>", Text);

    [Fact]
    public void ASpouseWithACardOfTheirOwn_IsSummarizedByTheirVitals_NotEveryFact()
    {
        Assert.Contains("Bea Ward</a> — m. 1860<br><span class=\"sub\">Bea b. 1830.</span>", Text);
        Assert.DoesNotContain("Weaver", Text);
    }

    [Fact]
    public void ASpouseToldOnlyInline_HasTheirFactsAndNotes_AndTheMarriageItsDivorceAndNotes()
    {
        Assert.Contains("Silas Penn — engagement 1854; divorce 1862<br>", Text);
        Assert.Contains("NOTE: They lived apart.", Text);
        Assert.Contains("NOTE: Silas sold tinware.", Text);
    }

    [Fact]
    public void ASpouseToldOnlyInline_HasTheirVitalsDetails_AndTheirOtherMarriageInFull()
    {
        Assert.Contains("Silas occupation Peddler; d. 1890 (cause: Fever); m. (2) Kate Lowe — m. 1865; divorce 1866.", Text);
        Assert.Contains("NOTE: Silas and Kate parted.", Text);
    }

    [Fact]
    public void ASummarizedSpouseWithNothingToSummarize_GetsNoEmptyLine()
    {
        // Sue's husband Pa has his own card on his father's page, and only an occupation, which a summary leaves out.
        var model = ModelBuilder.Build(Ged70Parser.Parse("""
            0 HEAD
            1 GEDC
            2 VERS 7.0
            0 @I1@ INDI
            1 NAME Ned /Hill/
            1 SEX M
            1 FAMS @F1@
            0 @I2@ INDI
            1 NAME Sue /Hill/
            1 SEX F
            1 FAMC @F1@
            1 FAMS @F2@
            0 @I3@ INDI
            1 NAME Pa /Moss/
            1 SEX M
            1 OCCU Smith
            1 FAMC @F3@
            1 FAMS @F2@
            0 @I4@ INDI
            1 NAME Old /Moss/
            1 SEX M
            1 FAMS @F3@
            0 @F1@ FAM
            1 HUSB @I1@
            1 CHIL @I2@
            0 @F2@ FAM
            1 HUSB @I3@
            1 WIFE @I2@
            1 MARR
            2 DATE 1880
            0 @F3@ FAM
            1 HUSB @I4@
            1 CHIL @I3@
            0 TRLR
            """));
        string dir = Directory.CreateTempSubdirectory("gedfire-gen-fact-tests-").FullName;
        try
        {
            new SiteGenerator(model, Template).Generate(dir);
            string page = File.ReadAllText(Directory.EnumerateFiles(dir, "HillNed*.html").Single());

            Assert.Contains("Pa Moss</a> — m. 1880</p>", page);
            Assert.DoesNotContain("<span class=\"sub\"></span>", page);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void AWifesOtherMarriageWithChildren_NamesHerHusband_AndLinksTheirPage() =>
        Assert.Matches("<span class=\"ord\">\\(2\\)</span> <a href=\"[^\"]+\">Hugh Moss</a> <a class=\"tofam\"", Text);

    [Fact]
    public void AChildlessEarlierOrLaterMarriage_IsToldInFullInTheAlsoMarriedRow()
    {
        Assert.Contains("<dt>Also m.</dt><dd>\r\n<p class=\"marr\"><span class=\"ord\">(2)</span> Joan Reed — engagement 1839; m. 1840; divorce 1842<br>", Text);
        Assert.Contains("<span class=\"sub\">Joan b. 1815.</span>", Text);
        Assert.Contains("NOTE: They parted quickly.", Text);
        Assert.Contains("NOTE: Joan kept a shop.", Text);
    }

    [Fact]
    public void AChildlessCouplesOwnCitation_IsFootnotedOnTheSpousesName() =>
        Assert.Matches("Joan Reed<sup>.*?</sup> — engagement 1839", Page.Value);

    [Fact]
    public void AChildlessCouplesOwnCitation_KeepsItsCommentary() =>
        Assert.Contains("Town Clerk, Town Records, online at https://example.org, 9. The clerk noted the separation.", Page.Value);

    [Fact]
    public void AFootnoteFromASourceNoteThatOmitsTheAuthor_NamesTheAuthor_AndSetsTheCommentaryOffItsExtract() =>
        Assert.Contains("Town Clerk, Town Records, online at https://example.org, 4. Ezra Vale, miller. The clerk spelled it Vail.", Page.Value);
}
