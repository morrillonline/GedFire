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
    public void CitationsOn_ReturnsTheFactsCitationsWithEmptyFieldsAsNull()
    {
        var citation = Assert.Single(Lookup().CitationsOn("@I1@", "DEAT"));

        Assert.Equal(new GedCore.Drafting.ExistingCitation("@S1@", "Memorial #0", null, 2), citation);
        Assert.Empty(Lookup().CitationsOn("@I1@", "BIRT"));
        Assert.Empty(Lookup().CitationsOn("@I9@", "DEAT"));
    }

    [Fact]
    public void NotesOn_ReturnsAPersonsNoteTexts()
    {
        Assert.Equal(["A note."], Lookup().NotesOn("@I1@"));
        Assert.Empty(Lookup().NotesOn("@I2@"));
    }
}
