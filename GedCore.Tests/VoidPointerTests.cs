using GedCore.Ged55;
using GedCore.Ged70;
using GedFire.Gen;

namespace GedCore.Tests;

/// <summary>
/// GEDCOM 7's reserved @VOID@ pointer means "no record exists in this
/// document". It must never resolve
/// to a real record, and tooling must not create a record named @VOID@.
/// Covers Apply-layer validation, the generator's ModelBuilder, and raw
/// round-trip stability of a file containing @VOID@ pointers.
/// </summary>
public class VoidPointerTests : ApplyTestBase
{
    // -------------------------------------------------------------------
    // Apply validation
    // -------------------------------------------------------------------

    [Fact]
    public void CreateOrUpdateVital_TargetingVoid_FailsValidation_DocumentUnmodified()
    {
        WriteBaseFile();
        byte[] before = ReadBytes();

        var result = Run("""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateVital", "record": "@VOID@", "fact": "DEAT",
                "value": { "date": "9 APR 2009" },
                "citations": [ { "source": "@S00001@" } ] } ] } ] }
            """, dryRun: true);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("@VOID@ is not an addressable record"));
        Assert.Equal(before, ReadBytes());
        Assert.Null(result.OutputBytes);
    }

    [Fact]
    public void CreateOrUpdateSpouse_SpouseIsVoid_FailsValidation()
    {
        WriteBaseFile();

        var result = Run("""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateSpouse", "person": "@I00001@",
                "spouse": { "xref": "@VOID@" }, "family": "@F00099@" } ] } ] }
            """, dryRun: true);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("@VOID@ is not an addressable record"));
    }

    [Fact]
    public void CreateOrUpdateCitation_SourceIsVoid_FailsValidation()
    {
        WriteBaseFile();

        var result = Run("""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateCitation", "record": "@I00001@", "fact": "BIRT",
                "citations": [ { "source": "@VOID@" } ] } ] } ] }
            """, dryRun: true);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("@VOID@ is not an addressable record"));
    }

    [Fact]
    public void CreateOrUpdateSource_XrefIsVoid_FailsValidation()
    {
        WriteBaseFile();

        var result = Run("""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateSource", "xref": "@VOID@", "title": "Bogus" } ] } ] }
            """, dryRun: true);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("@VOID@ is not an addressable record"));
    }

    // -------------------------------------------------------------------
    // ModelBuilder
    // -------------------------------------------------------------------

    [Fact]
    public void VoidSnote_ProducesNoNote()
    {
        var model = ModelBuilder.Build(Ged55Parser.Parse("""
            0 @I1@ INDI
            1 NAME John /Smith/
            1 SNOTE @VOID@
            """));
        Assert.Empty(model.Individuals["@I1@"].NarrativeNotes);
    }

    [Fact]
    public void EscapedNoteTextShapedLikeAPointer_StaysNoteText()
    {
        // Pointer-ness must be decided on the raw payload: "@@VOID@" and
        // "@@N1@" are ESCAPED TEXT whose logical values ("@VOID@", "@N1@")
        // merely look pointer-shaped after un-escaping.
        var model = ModelBuilder.Build(Ged55Parser.Parse("""
            0 @I1@ INDI
            1 NAME John /Smith/
            1 NOTE @@VOID@
            0 @I2@ INDI
            1 NAME Jane /Smith/
            1 NOTE @@N1@
            """));

        Assert.Equal("@VOID@", Assert.Single(model.Individuals["@I1@"].NarrativeNotes).Text);
        Assert.Equal("@N1@", Assert.Single(model.Individuals["@I2@"].NarrativeNotes).Text);
    }

    [Fact]
    public void VoidChil_BuildsWithRemainingChildren_DoesNotThrow()
    {
        var model = ModelBuilder.Build(Ged55Parser.Parse("""
            0 @I1@ INDI
            1 NAME Parent /Test/
            1 FAMS @F1@
            0 @I2@ INDI
            1 NAME Kid /Test/
            1 FAMC @F1@
            0 @F1@ FAM
            1 HUSB @I1@
            1 CHIL @VOID@
            1 CHIL @I2@
            """));

        var fam = model.Families["@F1@"];
        Assert.Single(fam.Children);
        Assert.Equal("@I2@", fam.Children[0].Xref);
    }

    [Fact]
    public void VoidHusbAndWife_LeaveFamilyMembersAbsent()
    {
        var model = ModelBuilder.Build(Ged55Parser.Parse("""
            0 @F1@ FAM
            1 HUSB @VOID@
            1 WIFE @VOID@
            """));

        var fam = model.Families["@F1@"];
        Assert.Null(fam.Husband);
        Assert.Null(fam.Wife);
    }

    // -------------------------------------------------------------------
    // Round-trip stability
    // -------------------------------------------------------------------

    [Fact]
    public void FileContainingVoidPointers_ReformatsByteIdentically()
    {
        string text = string.Join("\r\n",
        [
            "0 HEAD",
            "1 GEDC",
            "2 VERS 7.0",
            "0 @F1@ FAM",
            "1 HUSB @VOID@",
            "1 CHIL @VOID@",
            "1 CHIL @I1@",
            "0 @I1@ INDI",
            "1 NAME Kid /Test/",
            "1 SNOTE @VOID@",
            "0 TRLR",
        ]) + "\r\n";

        var doc = Ged70Parser.Parse(text);

        var first = new MemoryStream();
        Ged70Formatter.Write(doc, first);

        var reparsed = Ged70Parser.Read(new MemoryStream(first.ToArray()));
        var second = new MemoryStream();
        Ged70Formatter.Write(reparsed, second);

        Assert.Equal(first.ToArray(), second.ToArray());
    }
}
