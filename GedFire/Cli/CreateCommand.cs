using GedCore.Ged70;

namespace GedFire.Cli;

public sealed class CreateCommand : SynchronousOptionCommand
{
    public override string Name => "create";

    protected override string Usage =>
        "Usage: gedfire create --output <ged70> --name <gedcom-name> [--xref @I00001@] [--sex M|F|X|U]";

    protected override IReadOnlyCollection<string> ValueOptions => ["--output", "--name", "--xref", "--sex"];

    protected override IReadOnlyCollection<string> RequiredOptions => ["--output", "--name"];

    protected override int Execute(CommandLine commandLine, CommandContext context)
    {
        string output = commandLine.Require("--output");
        string name = commandLine.Require("--name");
        string xref = commandLine.Value("--xref") ?? "@I00001@";

        if (File.Exists(output))
            return context.Fail($"Output file already exists: {output}");

        try
        {
            var document = Ged70DocumentFactory.CreateSeeded(name, xref, commandLine.Value("--sex"));
            Ged70Formatter.WriteFile(document, output);
            context.Out.WriteLine($"Created {output} with seed person {xref} ({name})");
            return 0;
        }
        catch (ArgumentException ex)
        {
            return context.Fail(ex.Message);
        }
    }
}
