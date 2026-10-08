using GedCore.Gedzip;

namespace GedFire.Cli;

public sealed class PackCommand : SynchronousOptionCommand
{
    public override string Name => "pack";

    protected override string Usage => "Usage: gedfire pack --input <ged> --media-dir <dir> --output <gdz>";

    protected override IReadOnlyCollection<string> ValueOptions => ["--input", "--media-dir", "--output"];

    protected override IReadOnlyCollection<string> RequiredOptions => ["--input", "--media-dir", "--output"];

    protected override int Execute(CommandLine commandLine, CommandContext context)
    {
        string input = commandLine.Require("--input");
        string output = commandLine.Require("--output");
        if (context.FileMissing(input)) return 1;

        var document = context.ReadDocument(input);
        try
        {
            GedzipWriter.Write(document, commandLine.Require("--media-dir"), output);
        }
        catch (FileNotFoundException ex)
        {
            return context.Fail(ex.Message);
        }

        context.Out.WriteLine($"Wrote    {output}");
        return 0;
    }
}
