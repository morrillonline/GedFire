using GedCore;
using GedCore.Ged55;
using GedFire.Gen;

namespace GedCore.Tests;

// ---------------------------------------------------------------------------
// Shared-note resolution — NOTE @X@ (5.5) and SNOTE @X@ (7.0) pointers
// ---------------------------------------------------------------------------

public class SharedNoteTests
{
    [Fact]
    public void NotePointer_55Style_ResolvesToRecordText()
    {
        var model = ModelBuilder.Build(Ged55Parser.Parse("""
            0 @I1@ INDI
            1 NAME John /Smith/
            1 NOTE @N1@
            0 @N1@ NOTE Shared note text.
            1 CONT Second line.
            """));
        Assert.Equal("Shared note text.\nSecond line.",
            Assert.Single(model.Individuals["@I1@"].NarrativeNotes).Text);
    }

    [Fact]
    public void SnotePointer_70Style_ResolvesToRecordText()
    {
        var model = ModelBuilder.Build(Ged55Parser.Parse("""
            0 @I1@ INDI
            1 NAME John /Smith/
            1 SNOTE @N1@
            0 @N1@ SNOTE Shared note text.
            """));
        Assert.Equal("Shared note text.", Assert.Single(model.Individuals["@I1@"].NarrativeNotes).Text);
    }

    [Fact]
    public void SnotePointer_HtmlMime_IsPreserved()
    {
        var model = ModelBuilder.Build(Ged55Parser.Parse("""
            0 @I1@ INDI
            1 NAME John /Smith/
            1 SNOTE @N1@
            0 @N1@ SNOTE Named after <i>The Odyssey</i>.
            1 MIME text/html
            """));
        var note = Assert.Single(model.Individuals["@I1@"].NarrativeNotes);
        Assert.Equal("text/html", note.Mime);
    }

    [Fact]
    public void DanglingNotePointer_KeepsLiteralValue()
    {
        var model = ModelBuilder.Build(Ged55Parser.Parse("""
            0 @I1@ INDI
            1 NAME John /Smith/
            1 NOTE @N9@
            """));
        Assert.Equal("@N9@", Assert.Single(model.Individuals["@I1@"].NarrativeNotes).Text);
    }

    [Fact]
    public void PlainNote_IsNotTreatedAsPointer()
    {
        var model = ModelBuilder.Build(Ged55Parser.Parse("""
            0 @I1@ INDI
            1 NAME John /Smith/
            1 NOTE Contact me at name@example.com about this line.
            """));
        Assert.Equal("Contact me at name@example.com about this line.",
            Assert.Single(model.Individuals["@I1@"].NarrativeNotes).Text);
    }

    [Fact]
    public void PersonNotes_RetainAllNotesInGedcomOrder()
    {
        var model = ModelBuilder.Build(Ged55Parser.Parse("""
            0 @S1@ SOUR
            1 TITL Family papers
            0 @I1@ INDI
            1 NAME John /Smith/
            1 NOTE First biography entry.
            1 NOTE Second biography entry.
            2 SOUR @S1@
            1 NOTE Third biography entry.
            """));

        var notes = model.Individuals["@I1@"].NarrativeNotes;
        Assert.Equal(["First biography entry.", "Second biography entry.", "Third biography entry."],
            notes.Select(note => note.Text));
        Assert.Empty(notes[0].Sources);
        Assert.Single(notes[1].Sources);
        Assert.Empty(notes[2].Sources);
    }
}

// ---------------------------------------------------------------------------
// GedDate — pure parsing helpers
// ---------------------------------------------------------------------------

public class GedDateTests
{
    [Theory]
    [InlineData(null,       0)]
    [InlineData("",         0)]
    [InlineData("1800",     1800)]
    [InlineData("ABT 1800", 1800)]
    [InlineData("15 JUN 1800", 1800)]
    [InlineData("1745/46",  1745)]   // double date: ParseYear takes the left (old-style) year
    public void ParseYear(string? input, int expected)
        => Assert.Equal(expected, GedDate.ParseYear(input));

    [Fact] public void Parse_Null_ReturnsNull()  => Assert.Null(GedDate.Parse(null));
    [Fact] public void Parse_Empty_ReturnsNull() => Assert.Null(GedDate.Parse(""));

    [Fact]
    public void Parse_YearOnly_ReturnsJan1()
    {
        var d = GedDate.Parse("1800");
        Assert.NotNull(d);
        Assert.Equal(new DateTime(1800, 1, 1), d!.Value);
    }

    [Fact]
    public void Parse_FullDate()
    {
        var d = GedDate.Parse("15 JUN 1800");
        Assert.NotNull(d);
        Assert.Equal(new DateTime(1800, 6, 15), d!.Value);
    }

    [Fact]
    public void Parse_MonthYear_Day1()
    {
        var d = GedDate.Parse("JUN 1800");
        Assert.NotNull(d);
        Assert.Equal(new DateTime(1800, 6, 1), d!.Value);
    }

    [Fact]
    public void Parse_Approximate_SkipsAbtToken()
    {
        // "ABT" starts with "AB", so the token is skipped; the year still parses.
        var d = GedDate.Parse("ABT 1800");
        Assert.NotNull(d);
        Assert.Equal(1800, d!.Value.Year);
    }

    [Fact]
    public void Parse_Private_ReturnsNull() => Assert.Null(GedDate.Parse("PRIVATE 1800"));

    [Fact]
    public void Parse_DoubleDate_NormalisesToGregorianYear()
    {
        // "1745/46" means Julian 1745 = Gregorian 1746. Parse computes 1746.
        // This intentionally differs from ParseYear, which returns the left side (1745).
        var d = GedDate.Parse("1745/46");
        Assert.NotNull(d);
        Assert.Equal(1746, d!.Value.Year);
    }
}

// ---------------------------------------------------------------------------
// ParsePropertyList — pipe-delimited property extraction
// ---------------------------------------------------------------------------

public class ParsePropertyListTests
{
    [Fact]
    public void NoPipes_ReturnsTextUnchanged()
    {
        string result = FtmCitationText.ParsePropertyList("some text",
            out bool inl, out bool noCit, out string sc);
        Assert.Equal("some text", result);
        Assert.False(inl);
        Assert.False(noCit);
        Assert.Equal("", sc);
    }

    [Fact]
    public void Empty_ReturnsEmpty()
        => Assert.Equal("", FtmCitationText.ParsePropertyList("", out _, out _, out _));

    [Fact]
    public void InlineTrue_SetsFlag()
    {
        string result = FtmCitationText.ParsePropertyList("INLINE:TRUE|note text",
            out bool inl, out _, out _);
        Assert.True(inl);
        Assert.Equal("note text", result);
    }

    [Fact]
    public void NoCitationTrue_SetsFlag()
    {
        string result = FtmCitationText.ParsePropertyList("NOCITATION:TRUE|note",
            out _, out bool noCit, out _);
        Assert.True(noCit);
        Assert.Equal("note", result);
    }

    [Fact]
    public void ShortCitation_Extracted()
    {
        string result = FtmCitationText.ParsePropertyList("SHORTCITATION:Short Cite|note",
            out _, out _, out string sc);
        Assert.Equal("Short Cite", sc);
        Assert.Equal("note", result);
    }

    [Fact]
    public void MultipleProperties_AllExtracted()
    {
        string result = FtmCitationText.ParsePropertyList(
            "INLINE:TRUE|SHORTCITATION:Short|NOCITATION:TRUE|final text",
            out bool inl, out bool noCit, out string sc);
        Assert.True(inl);
        Assert.True(noCit);
        Assert.Equal("Short", sc);
        Assert.Equal("final text", result);
    }

    [Fact]
    public void UnrecognizedLeadingSegment_StopsParsing_AndPreservesText()
    {
        string result = FtmCitationText.ParsePropertyList("NOCOLON|note",
            out bool inl, out _, out _);
        Assert.Equal("NOCOLON|note", result);
        Assert.False(inl);
    }

    [Fact]
    public void MidTextMarkers_AreInert_AndTextIsPreserved()
    {
        // FTM source NOTE shape: citation text first, marker line after.
        // The citation text must survive — the 2010 regression was greedy
        // stripping that reduced these notes to "." fragments.
        string text = "Smith, A Book (Boston: Press, 1900), Source Medium: Book\n" +
                      "SHORTCITATION: Smith|\n.";
        string result = FtmCitationText.ParsePropertyList(text,
            out bool inl, out bool noCit, out string sc);
        Assert.Equal(text, result);
        Assert.False(inl);
        Assert.False(noCit);
        Assert.Equal("", sc);
    }

    [Fact]
    public void KeysAreCaseInsensitive()
    {
        FtmCitationText.ParsePropertyList("inline:true|note", out bool inl, out _, out _);
        Assert.True(inl);
    }
}

// ---------------------------------------------------------------------------
// ParseSourceNote — FTM source-record NOTE convention
// ---------------------------------------------------------------------------

public class ParseSourceNoteTests
{
    [Fact]
    public void CitationLine_WithShortCitationDirective_KeepsCitation()
    {
        string result = FtmCitationText.ParseSourceNote(
            "[Anonymous], Vital Records of Roxbury (Salem: Essex Institute, 1925), Source Medium: Book\n" +
            "SHORTCITATION: Roxbury VR (published)|\n.",
            out bool noCit, out string sc);
        Assert.Equal(
            "[Anonymous], Vital Records of Roxbury (Salem: Essex Institute, 1925), Source Medium: Book",
            result);
        Assert.False(noCit);
        Assert.Equal("Roxbury VR (published)", sc);
    }

    [Fact]
    public void NoCitationDirective_IsHonored_ButInlineIsInert()
    {
        // The "Personal note" pseudo-source @S00257@ — its INLINE marker must
        // stay inert so its 2,261 citations keep rendering as footnotes.
        string result = FtmCitationText.ParseSourceNote(
            "Personal note, Source Medium: Book\nINLINE: true | NOCITATION: true|\n.",
            out bool noCit, out string sc);
        Assert.Equal("Personal note, Source Medium: Book", result);
        Assert.True(noCit);
        Assert.Equal("", sc);
    }

    [Fact]
    public void TerminatorLine_IsDropped()
    {
        string result = FtmCitationText.ParseSourceNote(
            "A citation, Source Medium: Newspaper\n.",
            out _, out _);
        Assert.Equal("A citation, Source Medium: Newspaper", result);
    }

    [Fact]
    public void ProseLine_EndingWithPipe_IsNotADirective()
    {
        // @S00163@ shape: a comment line that happens to end with "|".
        string result = FtmCitationText.ParseSourceNote(
            "Merrill, History of Amesbury (1880), Source Medium: Book\n" +
            "Contains numerous references to Abraham Example.|\n.",
            out bool noCit, out _);
        Assert.Equal(
            "Merrill, History of Amesbury (1880), Source Medium: Book\n" +
            "Contains numerous references to Abraham Example.|",
            result);
        Assert.False(noCit);
    }

    [Fact]
    public void LeadingMarkers_AreStripped_FromFirstLine()
    {
        string result = FtmCitationText.ParseSourceNote(
            "NOCITATION:TRUE|actual note text",
            out bool noCit, out _);
        Assert.Equal("actual note text", result);
        Assert.True(noCit);
    }
}

// ---------------------------------------------------------------------------
// GedIndividual / GedFamily helpers
// ---------------------------------------------------------------------------

public class GedModelHelperTests
{
    [Fact]
    public void Husbandname_Basic()
        => Assert.Equal("John Smith",
            new GedIndividual { FirstName = "John", LastNameRaw = "Smith" }.Husbandname());

    [Fact]
    public void Husbandname_WithMiddleName()
        => Assert.Equal("John Robert Smith",
            new GedIndividual { FirstName = "John", MiddleName = "Robert", LastNameRaw = "Smith" }.Husbandname());

    [Fact]
    public void Husbandname_WithTitle()
        => Assert.Equal("Dr. John Smith",
            new GedIndividual { FirstName = "John", LastNameRaw = "Smith", Title = "Dr." }.Husbandname());

    [Fact]
    public void LastName_UnknownNormalisesToPlaceholder()
    {
        Assert.Equal(GedIndividual.UnknownString,
            new GedIndividual { LastNameRaw = "unknown" }.LastName);
        Assert.Equal(GedIndividual.UnknownString,
            new GedIndividual { LastNameRaw = "UNKNOWN" }.LastName);
    }

    [Fact]
    public void FirstMiddle_UnknownNormalisesToPlaceholder()
        => Assert.Equal(GedIndividual.UnknownString,
            new GedIndividual { FirstName = "unknown" }.FirstMiddle());

    [Fact]
    public void Wifename_NoMarriages_ReturnsMaidenName()
    {
        var w = new GedIndividual { FirstName = "Mary", LastNameRaw = "Jones", IsMale = false };
        Assert.Equal("Mary Jones", w.Wifename(null));
    }

    [Fact]
    public void Wifename_FirstMarriage_ReturnsMaidenName()
    {
        var w = new GedIndividual { FirstName = "Mary", LastNameRaw = "Jones", IsMale = false };
        var h = new GedIndividual { FirstName = "John", LastNameRaw = "Smith" };
        var fam = new GedFamily { Husband = h, Wife = w };
        w.FamSpouse.Add(fam);
        // First marriage: maiden name with no prior married name
        Assert.Equal("Mary Jones", w.Wifename(h));
    }

    [Fact]
    public void Wifename_SecondMarriage_ShowsMaidenInParens()
    {
        var w  = new GedIndividual { FirstName = "Mary", LastNameRaw = "Jones", IsMale = false };
        var h1 = new GedIndividual { FirstName = "First",  LastNameRaw = "Brown" };
        var h2 = new GedIndividual { FirstName = "Second", LastNameRaw = "Smith" };
        var fam1 = new GedFamily { Husband = h1, Wife = w };
        var fam2 = new GedFamily { Husband = h2, Wife = w };
        w.FamSpouse.Add(fam1);
        w.FamSpouse.Add(fam2);
        // Name relative to second husband: "Mary (Jones) Brown"
        string name = w.Wifename(h2);
        Assert.Contains("(Jones)", name);
        Assert.Contains("Brown", name);
    }

    [Fact]
    public void IsChildless_NoFamilies_True()
        => Assert.True(new GedIndividual().IsChildless());

    [Fact]
    public void IsChildless_FamilyWithNoChildren_True()
    {
        var i = new GedIndividual();
        i.FamSpouse.Add(new GedFamily());
        Assert.True(i.IsChildless());
    }

    [Fact]
    public void IsChildless_FamilyWithChild_False()
    {
        var i   = new GedIndividual();
        var fam = new GedFamily();
        fam.Children.Add(new GedIndividual());
        i.FamSpouse.Add(fam);
        Assert.False(i.IsChildless());
    }

    [Fact]
    public void HasNoEvents_EmptyPerson_True()
        => Assert.True(new GedIndividual().HasNoEvents());

    [Fact]
    public void HasNoEvents_WithBirth_False()
        => Assert.False(new GedIndividual { Birth = new GedEvent { Tag = "BIRT" } }.HasNoEvents());

    [Fact]
    public void HasNoEvents_TwoMarriages_False()
    {
        var i = new GedIndividual();
        i.FamSpouse.Add(new GedFamily());
        i.FamSpouse.Add(new GedFamily());
        Assert.False(i.HasNoEvents());
    }

    [Fact]
    public void SpouseOf_ReturnsOtherSpouse()
    {
        var h   = new GedIndividual();
        var w   = new GedIndividual();
        var fam = new GedFamily { Husband = h, Wife = w };
        Assert.Equal(w, fam.SpouseOf(h));
        Assert.Equal(h, fam.SpouseOf(w));
    }

    [Fact]
    public void SpouseOf_NonMember_ReturnsNull()
    {
        var fam = new GedFamily
            { Husband = new GedIndividual(), Wife = new GedIndividual() };
        Assert.Null(fam.SpouseOf(new GedIndividual()));
    }

    [Fact]
    public void FamilyDescription_HusbandAndWife()
    {
        var h   = new GedIndividual { FirstName = "John", LastNameRaw = "Smith",
                                      Fullname = "John Smith" };
        var w   = new GedIndividual { FirstName = "Mary", LastNameRaw = "Jones", IsMale = false };
        var fam = new GedFamily { Husband = h, Wife = w };
        w.FamSpouse.Add(fam);
        string desc = fam.Description();
        Assert.Contains("John Smith", desc);
        Assert.Contains("Mary", desc);
    }

    [Fact]
    public void FamilyDescription_HusbandOnly()
    {
        var h   = new GedIndividual { Fullname = "John Smith" };
        var fam = new GedFamily { Husband = h };
        Assert.Equal("John Smith", fam.Description());
    }
}
