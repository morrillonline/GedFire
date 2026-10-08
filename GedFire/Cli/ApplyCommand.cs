using GedCore.Apply;

namespace GedFire.Cli;

public sealed class ApplyCommand : SynchronousOptionCommand
{
    public override string Name => "apply";

    protected override string Usage => "Usage: gedfire apply --input <ged> --changes <json> --items all|1,3 [--dry-run]";

    protected override IReadOnlyCollection<string> ValueOptions => ["--input", "--changes", "--items"];

    protected override IReadOnlyCollection<string> RequiredOptions => ["--input", "--changes", "--items"];

    protected override IReadOnlyCollection<string> Switches => ["--dry-run"];

    protected override int Execute(CommandLine commandLine, CommandContext context)
    {
        string input = commandLine.Require("--input");
        string changesPath = commandLine.Require("--changes");
        bool dryRun = commandLine.Has("--dry-run");

        if (context.FileMissing(input, "File") || context.FileMissing(changesPath, "File")) return 1;

        var changeset = Changeset.LoadFile(changesPath);
        if (!ItemSelector.TryParse(commandLine.Require("--items"), changeset, out int[] itemNumbers, out string? itemsError))
            return context.Fail($"--{itemsError}");

        ApplyResult result;
        try
        {
            result = ChangesetApplier.Run(input, changeset, itemNumbers, dryRun);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return context.Fail($"APPLY FAILED — could not open {input}: {ex.Message}");
        }

        foreach (var entry in result.Log)
            context.Out.WriteLine(dryRun ? entry : $"applied: {entry}");

        if (!result.Success)
            return ReportFailure(result, context);

        if (!dryRun)
        {
            string deltas = string.Join(", ", result.Deltas.Select(d => $"{d.Key} +{d.Value}"));
            context.Out.WriteLine($"verify OK: round-trip byte-stable, pointers resolve, deltas {{{deltas}}}");
        }
        return 0;
    }

    static int ReportFailure(ApplyResult result, CommandContext context)
    {
        context.Error.WriteLine("APPLY FAILED — file not modified:");
        foreach (var error in result.Errors)
            context.Error.WriteLine($"  - {error}");
        return 1;
    }
}
