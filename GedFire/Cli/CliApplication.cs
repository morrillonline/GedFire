using GedFire.Mcp;

namespace GedFire.Cli;

// ---------------------------------------------------------------------------
// Routes the first argument to the command of that name and answers the
// help, version, and unknown-command cases itself.
// ---------------------------------------------------------------------------

public sealed class CliApplication(IEnumerable<ICliCommand> commands, CommandContext context)
{
    readonly Dictionary<string, ICliCommand> _commands =
        commands.ToDictionary(c => c.Name, StringComparer.Ordinal);

    public static CliApplication CreateDefault(CommandContext context) => new(
    [
        new CreateCommand(), new UpgradeCommand(), new DowngradeCommand(), new GenerateCommand(),
        new ExportIndexCommand(), new SelectTargetsCommand(), new ApplyCommand(), new ValidateCommand(),
        new PackCommand(), new UnpackCommand(), new McpCommand(), new DateCalcCommand(),
        new FindPersonCommand(), new GetRecordCommand(), new GetDocumentStatsCommand(),
    ], context);

    public async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0)
        {
            context.Out.WriteLine(HelpText.Text);
            return 1;
        }

        string name = args[0].ToLowerInvariant();
        if (_commands.TryGetValue(name, out var command))
            return await command.RunAsync(args[1..], context).ConfigureAwait(false);

        switch (name)
        {
            case "--help" or "-h" or "help":
                context.Out.WriteLine(HelpText.Text);
                return 0;
            case "--version" or "-v" or "version":
                context.Out.WriteLine($"GedFire {ServerVersion.Current}");
                return 0;
            default:
                return context.Fail($"Unknown command: {args[0]}");
        }
    }
}
