using GedCore.Ged55;
using GedCore.Ged70;

namespace GedFire.Cli;

public sealed class DowngradeCommand : SynchronousOptionCommand
{
    public override string Name => "downgrade";

    protected override string Usage => "Usage: gedfire downgrade --input <ged70> --output <ged55>";

    protected override IReadOnlyCollection<string> ValueOptions => ["--input", "--output"];

    protected override IReadOnlyCollection<string> RequiredOptions => ["--input", "--output"];

    protected override int Execute(CommandLine commandLine, CommandContext context)
    {
        string input = commandLine.Require("--input");
        string output = commandLine.Require("--output");
        if (context.FileMissing(input)) return 1;

        var document = context.ReadDocument(input, Ged70Parser.ReadFile);
        Ged55Formatter.WriteFile(document, output);
        context.Out.WriteLine($"Wrote    {output}");
        return 0;
    }
}
