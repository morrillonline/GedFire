using GedFire.Export;
using GedFire.Gen;

namespace GedFire.Cli;

public sealed class ExportIndexCommand : SynchronousOptionCommand
{
    public override string Name => "export-index";

    protected override string Usage => "Usage: gedfire export-index --input <ged> --output <json>";

    protected override IReadOnlyCollection<string> ValueOptions => ["--input", "--output"];

    protected override IReadOnlyCollection<string> RequiredOptions => ["--input", "--output"];

    protected override int Execute(CommandLine commandLine, CommandContext context)
    {
        string input = commandLine.Require("--input");
        string output = commandLine.Require("--output");
        if (context.FileMissing(input)) return 1;

        var document = context.ReadDocument(input);
        context.Out.WriteLine("Building model...");
        var model = ModelBuilder.Build(document);
        context.Out.WriteLine($"  {model.Individuals.Count:N0} individuals, {model.Families.Count:N0} families");

        PersonIndexExporter.WriteFile(model, input, output);
        context.Out.WriteLine($"Wrote    {output} ({model.Individuals.Count:N0} persons)");
        return 0;
    }
}
