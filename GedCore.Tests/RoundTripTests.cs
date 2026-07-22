using GedCore.Ged55;
using GedCore.Ged70;

namespace GedCore.Tests;

public class RoundTripTests
{
    /// <summary>
    /// Parse the derived 5.5 fixture and write it back; result must be byte-for-byte identical.
    /// </summary>
    [Fact]
    public void RoundTrip_Ged55_IsByteIdentical()
    {
        byte[] original = ReadResource55();

        var doc = Ged55Parser.Read(new MemoryStream(original));

        var ms = new MemoryStream();
        Ged55Formatter.Write(doc, ms);

        Assert.Equal(original, ms.ToArray());
    }

    [Fact]
    public void Parse_CountsMatchFixtureContents()
    {
        var doc = Ged55Parser.Read(new MemoryStream(ReadResource55()));

        Assert.Equal(4, doc.Records.Count(r => r.Tag == "INDI"));
        Assert.Equal(2, doc.Records.Count(r => r.Tag == "FAM"));
    }

    /// <summary>
    /// 5.5 parse → 7.0 write → 7.0 parse → 5.5 write must be byte-identical to the
    /// original 5.5 file.
    /// </summary>
    [Fact]
    public void RoundTrip_Through_Ged70_IsByteIdentical()
    {
        byte[] original = ReadResource55();

        var doc55 = Ged55Parser.Read(new MemoryStream(original));

        var ms70 = new MemoryStream();
        Ged70Formatter.Write(doc55, ms70);

        ms70.Position = 0;
        var doc70 = Ged70Parser.Read(ms70);

        var ms55 = new MemoryStream();
        Ged55Formatter.Write(doc70, ms55);

        Assert.Equal(original, ms55.ToArray());
    }

    /// <summary>
    /// Canonicalize the upstream GEDCOM 7 fixture, then verify that the
    /// canonical representation is byte-stable on subsequent parse/write cycles.
    /// </summary>
    [Fact]
    public void RoundTrip_Ged70_CanonicalFormIsByteIdentical()
    {
        byte[] original = ReadResource70();

        var doc = Ged70Parser.Read(new MemoryStream(original));

        var first = new MemoryStream();
        Ged70Formatter.Write(doc, first);

        var reparsed = Ged70Parser.Read(new MemoryStream(first.ToArray()));
        var second = new MemoryStream();
        Ged70Formatter.Write(reparsed, second);

        Assert.Equal(first.ToArray(), second.ToArray());
    }

    // -------------------------------------------------------------------------

    private static byte[] ReadResource55() => ReadResource("Example-5.5.ged");
    private static byte[] ReadResource70() => ReadResource("Example-7.0.ged");

    private static byte[] ReadResource(string fileName)
    {
        var stream = typeof(RoundTripTests).Assembly
            .GetManifestResourceStream($"GedCore.Tests.TestData.{fileName}")
            ?? throw new InvalidOperationException(
                $"Embedded resource GedCore.Tests.TestData.{fileName} not found.");
        using var buf = new MemoryStream();
        stream.CopyTo(buf);
        return buf.ToArray();
    }
}
