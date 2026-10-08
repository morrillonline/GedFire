using GedFire;
using GedFire.Cli;

namespace GedCore.Tests;

public class OptionCommandTests
{
    sealed class SampleCommand : OptionCommand
    {
        public CommandLine? Received { get; private set; }

        public override string Name => "sample";
        protected override string Usage => "Usage: gedfire sample --input <x> [--flag]";
        protected override IReadOnlyCollection<string> ValueOptions => ["--input", "--label"];
        protected override IReadOnlyCollection<string> RequiredOptions => ["--input"];
        protected override IReadOnlyCollection<string> Switches => ["--flag"];

        protected override Task<int> ExecuteAsync(CommandLine commandLine, CommandContext context)
        {
            Received = commandLine;
            return Task.FromResult(5);
        }
    }

    static async Task<(int Exit, string Error, SampleCommand Command)> Run(params string[] args)
    {
        var error = new StringWriter();
        var command = new SampleCommand();
        int exit = await command.RunAsync(args, new CommandContext(new StringWriter(), error));
        return (exit, error.ToString(), command);
    }

    [Fact]
    public async Task ACompleteCommandLine_IsPassedToTheCommand()
    {
        var (exit, error, command) = await Run("--input", "a.ged", "--flag");

        Assert.Equal(5, exit);
        Assert.Empty(error);
        Assert.Equal("a.ged", command.Received!.Value("--input"));
        Assert.True(command.Received.Has("--flag"));
    }

    [Fact]
    public async Task AMissingRequiredOption_PrintsTheUsageOnly()
    {
        var (exit, error, command) = await Run("--label", "x");

        Assert.Equal(1, exit);
        Assert.Equal("Usage: gedfire sample --input <x> [--flag]" + Environment.NewLine, error);
        Assert.Null(command.Received);
    }

    [Fact]
    public async Task AParseError_PrintsTheErrorThenTheUsage()
    {
        var (exit, error, command) = await Run("--input", "a.ged", "--bogus");

        Assert.Equal(1, exit);
        Assert.StartsWith("unknown option: --bogus", error);
        Assert.Contains("Usage: gedfire sample", error);
        Assert.Null(command.Received);
    }
}
