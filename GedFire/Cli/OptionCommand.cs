namespace GedFire.Cli;

// ---------------------------------------------------------------------------
// A command driven by "--option value" arguments. Parses them against the
// declared options and answers a bad or incomplete command line with the
// parse error and the usage text, so a subclass sees only a complete one.
// ---------------------------------------------------------------------------

public abstract class OptionCommand : ICliCommand
{
    public abstract string Name { get; }

    protected abstract string Usage { get; }

    protected abstract IReadOnlyCollection<string> ValueOptions { get; }

    protected virtual IReadOnlyCollection<string> RequiredOptions => [];

    protected virtual IReadOnlyCollection<string> Switches => [];

    public async Task<int> RunAsync(string[] args, CommandContext context)
    {
        var commandLine = CommandLine.Parse(args, ValueOptions, Switches);
        if (commandLine.Error is not null)
            context.Error.WriteLine(commandLine.Error);

        if (commandLine.Error is not null || RequiredOptions.Any(option => commandLine.Value(option) is null))
            return context.Fail(Usage);

        return await ExecuteAsync(commandLine, context).ConfigureAwait(false);
    }

    protected abstract Task<int> ExecuteAsync(CommandLine commandLine, CommandContext context);
}
