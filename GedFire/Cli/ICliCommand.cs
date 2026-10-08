namespace GedFire.Cli;

public interface ICliCommand
{
    /// <summary>The word that selects this command on the command line.</summary>
    string Name { get; }

    /// <summary>Runs the command against the arguments that follow its name and returns the process exit code.</summary>
    Task<int> RunAsync(string[] args, CommandContext context);
}
