using System.Text;
using GedCore.Ged55;

namespace GedCore.Tests;

/// <summary>
/// Encoding auto-detection for GEDCOM 5.5 input: byte-order marks are
/// decisive; otherwise the header CHAR tag decides (previewed well beyond
/// the old 512-byte window); Windows-1252 is the fallback.
/// </summary>
public class EncodingDetectionTests
{
    static EncodingDetectionTests() =>
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    static GedDocument Read(byte[] bytes) => Ged55Parser.Read(new MemoryStream(bytes));

    static string NameOfFirstIndi(GedDocument doc) =>
        doc.Records.First(r => r.Tag == "INDI").FirstChild("NAME")!.Value;

    [Fact]
    public void Utf8Bom_IsDecisive_EvenWithoutCharTag()
    {
        string ged = "0 HEAD\r\n1 GEDC\r\n2 VERS 5.5\r\n" +
                     "0 @I1@ INDI\r\n1 NAME José /Test/\r\n0 TRLR\r\n";
        byte[] bytes = [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes(ged)];

        Assert.Equal("José /Test/", NameOfFirstIndi(Read(bytes)));
    }

    [Fact]
    public void CharAnsi_DecodesWindows1252()
    {
        string ged = "0 HEAD\r\n1 CHAR ANSI\r\n" +
                     "0 @I1@ INDI\r\n1 NAME José /Test/\r\n0 TRLR\r\n";
        byte[] bytes = Encoding.GetEncoding(1252).GetBytes(ged);

        Assert.Equal("José /Test/", NameOfFirstIndi(Read(bytes)));
    }

    [Fact]
    public void CharTagBeyond512Bytes_IsStillDetected()
    {
        // Pad the header so the CHAR line sits past the old 512-byte preview
        // window but within the widened one.
        var sb = new StringBuilder("0 HEAD\r\n");
        for (int i = 0; i < 12; i++)
            sb.Append("1 NOTE " + new string('x', 60) + "\r\n");
        sb.Append("1 CHAR UTF-8\r\n");
        sb.Append("0 @I1@ INDI\r\n1 NAME José /Test/\r\n0 TRLR\r\n");

        byte[] bytes = Encoding.UTF8.GetBytes(sb.ToString());
        Assert.True(bytes.Length > 512, "test setup: CHAR must sit beyond 512 bytes");

        Assert.Equal("José /Test/", NameOfFirstIndi(Read(bytes)));
    }

    [Fact]
    public void NoCharTagNoBom_DefaultsToWindows1252()
    {
        string ged = "0 HEAD\r\n1 GEDC\r\n" +
                     "0 @I1@ INDI\r\n1 NAME José /Test/\r\n0 TRLR\r\n";
        byte[] bytes = Encoding.GetEncoding(1252).GetBytes(ged);

        Assert.Equal("José /Test/", NameOfFirstIndi(Read(bytes)));
    }
}
