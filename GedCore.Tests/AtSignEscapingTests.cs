using GedCore.Ged55;
using GedCore.Ged70;

namespace GedCore.Tests;

/// <summary>
/// GEDCOM 7 requires a text payload whose first character is '@' to be
/// written as '@@'; readers must un-escape.
/// Covers the Escape/Unescape helpers, parse/reformat of an escaped payload,
/// the upgrader's free-text-citation detection regression, and idempotent
/// round-tripping through the Apply layer.
/// </summary>
public class AtSignEscapingTests : ApplyTestBase
{
    // -------------------------------------------------------------------
    // EscapeAtSign / UnescapeAtSign unit matrix
    // -------------------------------------------------------------------

    [Theory]
    [InlineData("", "")]
    [InlineData("a", "a")]
    [InlineData("@a", "@@a")]
    [InlineData("@@a", "@@a")]      // already escaped-looking — idempotent, not double-escaped
    [InlineData("@X@", "@@X@")]
    public void EscapeAtSign_MatchesExpected(string input, string expected) =>
        Assert.Equal(expected, GedRecord.EscapeAtSign(input));

    [Theory]
    [InlineData("", "")]
    [InlineData("a", "a")]
    [InlineData("@a", "@a")]        // no leading "@@" — nothing to strip
    [InlineData("@@a", "@a")]
    [InlineData("@@X@", "@X@")]
    public void UnescapeAtSign_MatchesExpected(string input, string expected) =>
        Assert.Equal(expected, GedRecord.UnescapeAtSign(input));

    [Theory]
    [InlineData("")]
    [InlineData("a")]
    [InlineData("@a")]
    [InlineData("@@a")]
    [InlineData("@X@")]
    public void EscapeAtSign_IsIdempotent(string input) =>
        Assert.Equal(GedRecord.EscapeAtSign(input), GedRecord.EscapeAtSign(GedRecord.EscapeAtSign(input)));

    [Theory]
    [InlineData("@a")]
    [InlineData("@X@")]
    public void EscapeThenUnescape_RoundTripsGenuinelyUnescapedText(string logical)
    {
        string escaped = GedRecord.EscapeAtSign(logical);
        Assert.Equal(logical, GedRecord.UnescapeAtSign(escaped));
    }

    // -------------------------------------------------------------------
    // Parse / reformat of an escaped payload
    // -------------------------------------------------------------------

    [Fact]
    public void Parse_EscapedNotePayload_UnescapesLogicallyAndReformatsStably()
    {
        string text = string.Join("\r\n",
        [
            "0 HEAD",
            "1 GEDC",
            "2 VERS 7.0",
            "0 @I00001@ INDI",
            "1 NAME Ada /Example/",
            "1 NOTE @@handle",
            "0 TRLR",
        ]) + "\r\n";

        var doc = Ged70Parser.Parse(text);
        var note = doc.ByXref["@I00001@"].FirstChild("NOTE")!;

        Assert.Equal("@@handle", note.Value);
        Assert.Equal("@handle", note.PayloadValue);
        Assert.Equal("@handle", note.FullValue());

        var first = new MemoryStream();
        Ged70Formatter.Write(doc, first);

        var reparsed = Ged70Parser.Read(new MemoryStream(first.ToArray()));
        var second = new MemoryStream();
        Ged70Formatter.Write(reparsed, second);

        Assert.Equal(first.ToArray(), second.ToArray());
        Assert.Equal("@@handle", reparsed.ByXref["@I00001@"].FirstChild("NOTE")!.Value);
    }

    [Theory]
    [InlineData("@S1@", true)]      // real pointer
    [InlineData("@@Some text", false)]
    [InlineData("Plain text", false)]
    public void IsPointerValue_DistinguishesPointersFromEscapedText(string value, bool expected)
    {
        var rec = Ged70Parser.Parse(string.Join("\r\n",
        [
            "0 HEAD",
            "1 GEDC",
            "2 VERS 7.0",
            "0 @I00001@ INDI",
            $"1 SOUR {value}",
            "0 TRLR",
        ]) + "\r\n").ByXref["@I00001@"].FirstChild("SOUR")!;

        Assert.Equal(expected, rec.IsPointerValue);
    }

    // -------------------------------------------------------------------
    // Upgrader regression: an "@@"-escaped free-text citation must still be
    // collected and converted, not silently classified as a pointer (defect 3).
    // -------------------------------------------------------------------

    [Fact]
    public void Upgrade_EscapedFreeTextCitation_IsConverted_WhilePointerCitationIsUntouched()
    {
        const string fixture = """
            0 HEAD
            1 CHAR ANSI
            1 GEDC
            2 VERS 5.5
            2 FORM LINEAGE-LINKED
            0 @I1@ INDI
            1 NAME Test /Person/
            1 SOUR @@Some text
            1 SOUR @S1@
            0 @S1@ SOUR
            1 TITL Existing Source
            0 TRLR
            """;

        var doc = Ged55Parser.Parse(fixture);
        var summary = Ged70Upgrader.UpgradeInPlace(doc);

        Assert.True(summary.FreeTextCitationsConverted > 0);

        var indi = doc.ByXref["@I1@"];
        var citations = indi.ChildrenByTag("SOUR").ToList();
        Assert.Equal(2, citations.Count);

        // The pre-existing pointer citation is untouched.
        Assert.Contains(citations, c => c.Value == "@S1@");

        // The escaped free-text citation is converted to a pointer at a new SOUR record.
        var converted = citations.Single(c => c.Value != "@S1@");
        Assert.True(converted.IsPointerValue);

        var newSource = doc.ByXref[converted.Value!];
        var note = newSource.FirstChild("NOTE")!;
        Assert.Equal("@@Some text", note.Value);      // still correctly escaped on disk
        Assert.Equal("@Some text", note.PayloadValue); // logical text preserved
    }

    // -------------------------------------------------------------------
    // Apply layer: a note whose text starts with '@' round-trips through
    // escaping and stays idempotent on re-apply.
    // -------------------------------------------------------------------

    [Fact]
    public void ApplyNote_TextStartingWithAtSign_WritesEscapedForm_AndReapplyIsNoOp()
    {
        WriteBaseFile();
        const string changeset = """
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateNote", "record": "@I00001@",
                "text": "@Home in Fergus Falls" } ] } ] }
            """;

        RunExpectSuccess(changeset);

        var note = ReadDoc().ByXref["@I00001@"].ChildrenByTag("NOTE")
            .Single(n => n.PayloadValue == "@Home in Fergus Falls");
        Assert.Equal("@@Home in Fergus Falls", note.Value);

        byte[] afterFirstApply = ReadBytes();

        var result = RunExpectSuccess(changeset);
        Assert.Contains(result.Log, l => l.Contains("no changes; file untouched"));
        Assert.Equal(afterFirstApply, ReadBytes());
    }
}
