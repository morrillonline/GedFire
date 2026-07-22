using GedCore.Ged55;

namespace GedCore.Tests;

/// <summary>
/// Malformed input should fail with an actionable message (line number and
/// offending content), not a bare FormatException from int.Parse deep inside
/// the line parser.
/// </summary>
public class ParserDiagnosticsTests
{
    [Fact]
    public void MalformedFirstLine_ReportsLineNumberAndContent()
    {
        var ex = Assert.Throws<FormatException>(() => Ged55Parser.Parse("garbage line"));
        Assert.Contains("line 1", ex.Message);
        Assert.Contains("garbage line", ex.Message);
    }

    [Fact]
    public void MalformedLaterLine_ReportsCorrectLineNumber()
    {
        var ex = Assert.Throws<FormatException>(
            () => Ged55Parser.Parse("0 HEAD\r\n1 GEDC\r\nnot a gedcom line\r\n"));
        Assert.Contains("line 3", ex.Message);
    }

    [Fact]
    public void ExcessiveNestingLevel_ReportsInformativeError()
    {
        var ex = Assert.Throws<FormatException>(
            () => Ged55Parser.Parse("0 HEAD\r\n100 DEEP\r\n"));
        Assert.Contains("level", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ValidFile_StillParses()
    {
        var doc = Ged55Parser.Parse("0 HEAD\r\n1 GEDC\r\n2 VERS 5.5\r\n0 TRLR\r\n");
        Assert.Equal(2, doc.Records.Count);
    }
}
