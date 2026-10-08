using GedFire.Gen;
using GedFire.Match;
using GedFire.TargetSelection;

namespace GedFire.Cli;

public sealed class SelectTargetsCommand : SynchronousOptionCommand
{
    public override string Name => "select-targets";

    protected override string Usage =>
        "Usage: gedfire select-targets --input <ged> --output <wanted.json> --count <N> --surnames <list>";

    protected override IReadOnlyCollection<string> ValueOptions => ["--input", "--output", "--count", "--surnames"];

    protected override IReadOnlyCollection<string> RequiredOptions => ["--input", "--output", "--count", "--surnames"];

    protected override int Execute(CommandLine commandLine, CommandContext context)
    {
        string input = commandLine.Require("--input");
        string output = commandLine.Require("--output");
        string countArg = commandLine.Require("--count");

        if (!int.TryParse(countArg, out int count) || count <= 0)
            return context.Fail($"--count must be a positive integer, got: {countArg}");

        var surnames = commandLine.Require("--surnames")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
        if (surnames.Count == 0)
            return context.Fail("--surnames must list at least one surname");

        if (context.FileMissing(input)) return 1;

        var model = ModelBuilder.Build(context.ReadDocument(input));
        var candidates = GapDetector.Detect(model, surnames);
        ReportCandidates(candidates, surnames, context.Out);

        long seed = DateTime.UtcNow.Ticks;
        var draw = DuplicateAnnotator.Annotate(TargetDrawer.Draw(candidates, count, seed), new MatchIndex(model));
        if (draw.LegendaryDiscards.Count > 0)
            context.Out.WriteLine($"  {draw.LegendaryDiscards.Count:N0} extra Legendary-band candidate(s) discarded (one-per-pack cap)");

        WantedFileWriter.WriteFile(input, surnames, candidates.Count, draw, output);
        context.Out.WriteLine($"Wrote    {output} ({draw.Targets.Count:N0} target(s), seed {seed})");
        return 0;
    }

    static void ReportCandidates(IReadOnlyList<SelectionTarget> candidates, List<string> surnames, TextWriter output)
    {
        output.WriteLine($"  {candidates.Count:N0} candidate gap(s) detected for {string.Join(", ", surnames)}");
        foreach (var group in candidates.GroupBy(c => c.CardType).OrderByDescending(g => g.Count()))
            output.WriteLine($"    {group.Count(),6:N0}  {group.Key}");
    }
}
