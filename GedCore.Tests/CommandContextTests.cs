using GedCore.Ged70;
using GedFire.Cli;

namespace GedCore.Tests;

public class CommandContextTests
{
    static (CommandContext Context, StringWriter Out, StringWriter Error) Create()
    {
        var output = new StringWriter();
        var error = new StringWriter();
        return (new CommandContext(output, error), output, error);
    }

    [Fact]
    public void Fail_WritesTheMessageToErrorAndReturnsOne()
    {
        var (context, output, error) = Create();

        Assert.Equal(1, context.Fail("boom"));
        Assert.Equal("boom" + Environment.NewLine, error.ToString());
        Assert.Empty(output.ToString());
    }

    [Fact]
    public void FileMissing_ReportsAnAbsentFileWithTheLabel()
    {
        var (context, _, error) = Create();

        Assert.True(context.FileMissing("no/such/file.ged"));
        Assert.Equal("Input file not found: no/such/file.ged" + Environment.NewLine, error.ToString());
        Assert.True(context.FileMissing("no/such/changes.json", "File"));
        Assert.Contains("File not found: no/such/changes.json", error.ToString());
    }

    [Fact]
    public void FileMissing_AcceptsAnExistingFileSilently()
    {
        var (context, _, error) = Create();

        Assert.False(context.FileMissing(typeof(CommandContextTests).Assembly.Location));
        Assert.Empty(error.ToString());
    }

    [Fact]
    public void ReadDocument_ReportsThePathAndRecordCount()
    {
        var (context, output, _) = Create();
        var document = Ged70DocumentFactory.CreateSeeded("Test", "@I1@", null);

        var read = context.ReadDocument("seeded.ged", _ => document);

        Assert.Same(document, read);
        Assert.Equal(
            $"Reading  seeded.ged{Environment.NewLine}  {document.Records.Count:N0} level-0 records{Environment.NewLine}",
            output.ToString());
    }
}
