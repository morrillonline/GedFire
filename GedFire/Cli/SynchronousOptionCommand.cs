namespace GedFire.Cli;

/// <summary>An <see cref="OptionCommand"/> whose work needs no awaiting.</summary>
public abstract class SynchronousOptionCommand : OptionCommand
{
    protected sealed override Task<int> ExecuteAsync(CommandLine commandLine, CommandContext context) =>
        Task.FromResult(Execute(commandLine, context));

    protected abstract int Execute(CommandLine commandLine, CommandContext context);
}
