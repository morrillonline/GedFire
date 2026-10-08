using GedCore.Ged70;
using GedCore.Gedzip;

namespace GedFire.Cli;

public sealed class UnpackCommand : SynchronousOptionCommand
{
    public override string Name => "unpack";

    protected override string Usage => "Usage: gedfire unpack --input <gdz> --output-dir <dir>";

    protected override IReadOnlyCollection<string> ValueOptions => ["--input", "--output-dir"];

    protected override IReadOnlyCollection<string> RequiredOptions => ["--input", "--output-dir"];

    protected override int Execute(CommandLine commandLine, CommandContext context)
    {
        string input = commandLine.Require("--input");
        string outputDir = commandLine.Require("--output-dir");
        if (context.FileMissing(input)) return 1;

        try
        {
            using var package = GedzipReader.Open(input);
            context.Out.WriteLine($"Read     {input}");
            context.Out.WriteLine($"  {package.Document.Records.Count:N0} level-0 records, {package.MediaPaths.Count:N0} media file(s)");

            Directory.CreateDirectory(outputDir);
            Ged70Formatter.WriteFile(package.Document, Path.Combine(outputDir, "gedcom.ged"));
            package.ExtractMedia(outputDir);
        }
        catch (Exception ex) when (ex is FormatException or IOException)
        {
            return context.Fail(ex.Message);
        }

        context.Out.WriteLine($"Wrote    {outputDir}");
        return 0;
    }
}
