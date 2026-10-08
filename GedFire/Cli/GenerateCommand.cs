using GedFire.Gen;

namespace GedFire.Cli;

public sealed class GenerateCommand : SynchronousOptionCommand
{
    public override string Name => "generate";

    protected override string Usage =>
        "Usage: gedfire generate --input <ged> --output-dir <dir> [--format html] [--template <file>] [--media-base-url <url>]";

    protected override IReadOnlyCollection<string> ValueOptions =>
        ["--input", "--output-dir", "--format", "--template", "--media-base-url"];

    protected override IReadOnlyCollection<string> RequiredOptions => ["--input", "--output-dir"];

    protected override int Execute(CommandLine commandLine, CommandContext context)
    {
        string input = commandLine.Require("--input");
        string outputDir = commandLine.Require("--output-dir");
        string mediaBaseUrl = commandLine.Value("--media-base-url") ?? "media/";
        if (context.FileMissing(input)) return 1;

        var document = context.ReadDocument(input);
        var model = BuildModel(document, context.Out);
        string template = LoadTemplate(input, commandLine.Value("--template"), context.Out);

        context.Out.WriteLine($"Generating HTML to {outputDir} ...");
        var generator = new SiteGenerator(model, template, new MediaOptions(MediaDirectory.For(input), mediaBaseUrl));
        generator.Generate(outputDir);
        foreach (var warning in generator.Warnings)
            context.Out.WriteLine($"  Warning: {warning}");
        context.Out.WriteLine("Done.");
        return 0;
    }

    static GedModel BuildModel(GedCore.GedDocument document, TextWriter output)
    {
        output.WriteLine("Building model...");
        var model = ModelBuilder.Build(document);
        output.WriteLine($"  {model.Individuals.Count:N0} individuals, {model.Families.Count:N0} families, {model.Sources.Count:N0} sources");

        int privatized = PrivacyFilter.Apply(model, DateTime.UtcNow.Year);
        if (privatized > 0)
            output.WriteLine($"  {privatized:N0} plausibly-living individuals privatized (\"{PrivacyFilter.LivingGivenName}\" placeholder)");
        return model;
    }

    // An explicit --template wins; otherwise well-known locations beside the input are probed.
    static string LoadTemplate(string input, string? templateArg, TextWriter output)
    {
        string? path = TemplateLocator.Locate(input, templateArg);
        if (path is null || !File.Exists(path)) return TemplateLocator.DefaultTemplateHtml;

        output.WriteLine($"Using template: {path}");
        return File.ReadAllText(path);
    }
}
