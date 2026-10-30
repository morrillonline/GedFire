using GedFire.Gen;
using GedFire.Mcp;

namespace GedCore.Tests;

public class ModelDraftLookupTests
{
    static ModelDraftLookup Lookup()
    {
        var model = new GedModel();
        model.Sources["@S1@"] = new GedSourceRecord { Xref = "@S1@", Title = "Find a Grave", Author = "" };
        model.Sources["@S2@"] = new GedSourceRecord { Xref = "@S2@", Title = "County Register", Author = "Clerk" };
        model.Individuals["@I1@"] = new GedIndividual { Xref = "@I1@", Fullname = "Gladys Giedd" };
        model.Individuals["@I2@"] = new GedIndividual { Xref = "@I2@", Fullname = "" };
        var death = new GedEvent { Tag = "DEAT" };
        death.Sources.Add(new GedSourceRef { GlobalSource = model.Sources["@S1@"], Page = "Memorial #0", DataText = "", Quay = 2 });
        model.Individuals["@I1@"].Death = death;
        model.Individuals["@I1@"].NarrativeNotes.Add(new GedNarrativeNote { Text = "A note." });
        return new ModelDraftLookup(model);
    }

    [Fact]
    public void FindSource_MatchesTitleExactlyIgnoringCase() =>
        Assert.Equal("@S1@", Lookup().FindSource("find a grave ", null));

    [Fact]
    public void FindSource_IgnoresPunctuationNoise() =>
        Assert.Equal("@S1@", Lookup().FindSource("Find a Grave.", null));

    [Fact]
    public void FindSource_DistinguishesYearRanges()
    {
        var model = new GedModel();
        model.Sources["@S1@"] = new GedSourceRecord { Xref = "@S1@", Title = "U.S., Social Security Death Index, 1935-2014", Author = "" };

        Assert.Equal("@S1@", new ModelDraftLookup(model).FindSource("U.S., Social Security Death Index, 1935-2014", null));
        Assert.Null(new ModelDraftLookup(model).FindSource("U.S., Social Security Death Index, 1935-2013", null));
    }

    [Fact]
    public void FindSource_IsNotASubstringSearch() =>
        Assert.Null(Lookup().FindSource("Find a", null));

    [Fact]
    public void FindSource_RequiresTheAuthorWhenGiven()
    {
        Assert.Equal("@S2@", Lookup().FindSource("County Register", "clerk"));
        Assert.Null(Lookup().FindSource("County Register", "Someone Else"));
    }

    [Fact]
    public void PersonName_ReturnsTheRecordedNameOrNull()
    {
        Assert.Equal("Gladys Giedd", Lookup().PersonName("@I1@"));
        Assert.Null(Lookup().PersonName("@I2@"));
        Assert.Null(Lookup().PersonName("@I9@"));
    }

    [Fact]
    public void FactsOn_ReturnsEachInstanceWithItsDatePlaceAndCitations()
    {
        var lookup = Lookup();

        var death = Assert.Single(lookup.FactsOn("@I1@", "DEAT"));
        Assert.Null(death.Date);
        var citation = Assert.Single(death.Citations);
        Assert.Equal(new GedCore.Drafting.ExistingCitation("@S1@", "Memorial #0", null, 2, "Find a Grave"), citation);
        Assert.Empty(lookup.FactsOn("@I1@", "BIRT"));
        Assert.Empty(lookup.FactsOn("@I9@", "DEAT"));
    }

    [Fact]
    public void FactsOn_ListsEveryInstanceOfARepeatableTag()
    {
        var model = new GedModel();
        model.Individuals["@I1@"] = new GedIndividual { Xref = "@I1@" };
        model.Individuals["@I1@"].Census.Add(new GedEvent { Tag = "CENS", Date = "1900", Place = "Salem" });
        model.Individuals["@I1@"].Census.Add(new GedEvent { Tag = "CENS", Date = "1910", Place = "" });

        var facts = new ModelDraftLookup(model).FactsOn("@I1@", "CENS");

        Assert.Equal([("1900", "Salem"), ("1910", null)], facts.Select(f => (f.Date, f.Place)));
    }

    [Fact]
    public void NotesOn_ReturnsAPersonsNoteTexts()
    {
        Assert.Equal(["A note."], Lookup().NotesOn("@I1@"));
        Assert.Empty(Lookup().NotesOn("@I2@"));
    }
}
