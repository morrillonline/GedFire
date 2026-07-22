using GedCore.Ged55;
using GedCore.Ged70;
using GedFire.Gen;

namespace GedCore.Tests;

/// <summary>
/// Generic upgrade tests that cover core 5.5 -> 7.0 normalization behavior
/// without depending on private genealogy fixtures.
/// </summary>
public class UpgradeTests
{
    [Fact]
    public void Upgrade_NormalizesLegacy55Constructs()
    {
        var doc = Ged55Parser.Parse(Legacy55Fixture);

        var summary = Ged70Upgrader.UpgradeInPlace(doc);

        Assert.True(summary.ConcLinesFolded > 0);
        Assert.True(summary.HeaderRecordsRemoved > 0);
        Assert.True(summary.NoteRecordsConverted > 0);
        Assert.True(summary.FreeTextCitationsConverted > 0);
        Assert.True(summary.AliasesConverted > 0);
        Assert.True(summary.EmptyContactLinesRemoved > 0);
        Assert.True(summary.SubmitterRecordsRemoved > 0);

        Assert.DoesNotContain(doc.Records, r => r.Tag == "NOTE");
        Assert.Contains(doc.Records, r => r.Tag == "SNOTE");

        var head = doc.Records.First(r => r.Tag == "HEAD");
        Assert.DoesNotContain(head.Children, c => c.Tag is "CHAR" or "FILE" or "DEST" or "SUBM");
        Assert.Null(head.FirstChild("GEDC")?.FirstChild("FORM"));

        Assert.Equal(0, CountDescendants(doc, r => r.Tag == "ALIA" && r.Value.Length > 0 && r.Value[0] != '@'));
        Assert.Equal(0, CountDescendants(doc, r => r.Tag == "SOUR" && r.Level > 0 && r.Value.Length > 0 && r.Value[0] != '@' && r.Parent?.Tag != "HEAD"));

        foreach (var root in doc.Records)
            AssertNoConc(root);
    }

    [Fact]
    public void Upgrade_OutputRoundTripsThroughGed70()
    {
        var doc = Ged55Parser.Parse(Legacy55Fixture);
        Ged70Upgrader.UpgradeInPlace(doc);

        var first = new MemoryStream();
        Ged70Formatter.Write(doc, first);

        var reparsed = Ged70Parser.Read(new MemoryStream(first.ToArray()));
        var second = new MemoryStream();
        Ged70Formatter.Write(reparsed, second);

        Assert.Equal(first.ToArray(), second.ToArray());

        var gedc = reparsed.Records.First(r => r.Tag == "HEAD").FirstChild("GEDC");
        Assert.Equal("7.0", gedc?.FirstChild("VERS")?.Value);
    }

    [Fact]
    public void Upgrade_ExampleFixture_PreservesCoreShape()
    {
        byte[] original = ReadResource("Example-5.5.ged");

        var doc = Ged55Parser.Read(new MemoryStream(original));
        Ged70Upgrader.UpgradeInPlace(doc);

        Assert.Equal(4, doc.Records.Count(r => r.Tag == "INDI"));
        Assert.Equal(2, doc.Records.Count(r => r.Tag == "FAM"));

        var model = ModelBuilder.Build(doc);
        Assert.Equal(4, model.Individuals.Count);
        Assert.Equal(2, model.Families.Count);

        foreach (var root in doc.Records)
            AssertNoConc(root);
    }

    [Fact]
    public void GedReader_DetectsVersion_AndParsesBothFormats()
    {
        byte[] legacy = ReadResource("Example-5.5.ged");
        var doc55 = GedReader.Read(legacy);
        Assert.Equal(4, doc55.Records.Count(r => r.Tag == "INDI"));

        byte[] modern = ReadResource("Example-7.0.ged");
        var doc70 = GedReader.Read(modern);
        Assert.Equal(4, doc70.Records.Count(r => r.Tag == "INDI"));
    }

    // -------------------------------------------------------------------
    // Subproject E — HEAD.SCHMA declarations for emitted extension tags
    // -------------------------------------------------------------------

    [Fact]
    public void Upgrade_DeclaresSchemaForExtensionTag_ButNotForFoldedConc()
    {
        var doc = Ged55Parser.Parse("""
            0 HEAD
            1 GEDC
            2 VERS 5.5
            0 @I1@ INDI
            1 NAME John /Example/
            1 NOTE Some note text that
            2 CONC continues here
            1 _FOOT A legacy footnote
            0 TRLR
            """);

        var summary = Ged70Upgrader.UpgradeInPlace(doc);

        // The CONC line was folded into the NOTE value, so the written file
        // contains no _CONC — declaring it would name a tag the file never uses.
        Assert.True(summary.ConcLinesFolded > 0);

        var head = doc.Records.First(r => r.Tag == "HEAD");
        var schma = head.FirstChild("SCHMA");
        Assert.NotNull(schma);
        var tagLines = schma!.ChildrenByTag("TAG").Select(t => t.FullValue()).ToList();
        var tagLine = Assert.Single(tagLines);
        Assert.StartsWith("_FOOT ", tagLine);
        Assert.Equal(1, summary.SchemaTagsDeclared);
    }

    [Fact]
    public void Upgrade_ExistingSchemaDeclaration_IsNotDuplicatedOrRewritten()
    {
        var doc = Ged55Parser.Parse("""
            0 HEAD
            1 GEDC
            2 VERS 5.5
            1 SCHMA
            2 TAG _FOOT https://existing.example/_FOOT
            0 @I1@ INDI
            1 NAME John /Example/
            1 NOTE Some note text that
            2 CONC continues here
            1 _FOOT A legacy footnote
            0 TRLR
            """);

        var summary = Ged70Upgrader.UpgradeInPlace(doc);

        var head = doc.Records.First(r => r.Tag == "HEAD");
        var schma = head.FirstChild("SCHMA")!;
        var tagLines = schma.ChildrenByTag("TAG").Select(t => t.FullValue()).ToList();
        var tagLine = Assert.Single(tagLines);
        Assert.Equal("_FOOT https://existing.example/_FOOT", tagLine);   // untouched
        Assert.Equal(0, summary.SchemaTagsDeclared);
    }

    [Fact]
    public void Upgrade_NoExtensionTagsOrConc_AddsNoSchema()
    {
        var doc = Ged55Parser.Parse("""
            0 HEAD
            1 GEDC
            2 VERS 5.5
            0 @I1@ INDI
            1 NAME John /Example/
            0 TRLR
            """);

        var summary = Ged70Upgrader.UpgradeInPlace(doc);

        var head = doc.Records.First(r => r.Tag == "HEAD");
        Assert.Null(head.FirstChild("SCHMA"));
        Assert.Equal(0, summary.SchemaTagsDeclared);
    }

    private static int CountDescendants(GedDocument doc, Func<GedRecord, bool> match)
    {
        int Count(GedRecord rec) => (match(rec) ? 1 : 0) + rec.Children.Sum(Count);
        return doc.Records.Sum(Count);
    }

    private static void AssertNoConc(GedRecord rec)
    {
        Assert.NotEqual("CONC", rec.Tag);
        foreach (var child in rec.Children)
            AssertNoConc(child);
    }

    private static byte[] ReadResource(string fileName)
    {
        var stream = typeof(UpgradeTests).Assembly
            .GetManifestResourceStream($"GedCore.Tests.TestData.{fileName}")
            ?? throw new InvalidOperationException(
                $"Embedded resource GedCore.Tests.TestData.{fileName} not found.");
        using var buf = new MemoryStream();
        stream.CopyTo(buf);
        return buf.ToArray();
    }

    private const string Legacy55Fixture = """
        0 HEAD
        1 SOUR LegacyFixture
        1 DEST OldDest
        1 FILE legacy.ged
        1 CHAR ANSI
        1 GEDC
        2 VERS 5.5
        2 FORM LINEAGE-LINKED
        1 SUBM @SUB1@
        0 @SUB1@ SUBM
        0 @I1@ INDI
        1 NAME John /Example/
        1 ALIA Jack Example
        1 NOTE @N1@
        1 EMAIL
        1 BIRT
        2 DATE 1 JAN 1900
        2 SOUR @S1@
        3 DATA
        4 TEXT INLINE:TRUE|Narrative line
        1 DEAT
        2 DATE 2 FEB 1980
        1 SOUR Book citation text without pointer
        0 @N1@ NOTE Shared note line
        1 CONC continued segment
        0 @S1@ SOUR
        1 TITL Source One
        0 TRLR
        """;
}
