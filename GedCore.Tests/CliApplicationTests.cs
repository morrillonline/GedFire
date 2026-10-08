using GedFire.Cli;

namespace GedCore.Tests;

public class CliApplicationTests
{
    sealed class RecordingCommand(string name, int exitCode) : ICliCommand
    {
        public string Name { get; } = name;
        public string[]? ReceivedArgs { get; private set; }

        public Task<int> RunAsync(string[] args, CommandContext context)
        {
            ReceivedArgs = args;
            return Task.FromResult(exitCode);
        }
    }

    static (CliApplication App, StringWriter Out, StringWriter Error) Build(params ICliCommand[] commands)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        return (new CliApplication(commands, new CommandContext(output, error)), output, error);
    }

    [Fact]
    public async Task NoArguments_PrintsHelpAndFails()
    {
        var (app, output, _) = Build();

        Assert.Equal(1, await app.RunAsync([]));
        Assert.Contains("Commands:", output.ToString());
    }

    [Theory]
    [InlineData("--help")]
    [InlineData("-h")]
    [InlineData("help")]
    public async Task HelpFlags_PrintHelpAndSucceed(string flag)
    {
        var (app, output, _) = Build();

        Assert.Equal(0, await app.RunAsync([flag]));
        Assert.Contains("Commands:", output.ToString());
    }

    [Theory]
    [InlineData("--version")]
    [InlineData("-v")]
    [InlineData("version")]
    public async Task VersionFlags_PrintTheVersion(string flag)
    {
        var (app, output, _) = Build();

        Assert.Equal(0, await app.RunAsync([flag]));
        Assert.StartsWith("GedFire ", output.ToString());
    }

    [Fact]
    public async Task UnknownCommand_IsReportedAndFails()
    {
        var (app, _, error) = Build();

        Assert.Equal(1, await app.RunAsync(["frobnicate"]));
        Assert.Contains("Unknown command: frobnicate", error.ToString());
    }

    [Fact]
    public async Task KnownCommand_ReceivesTheRemainingArgumentsAndReturnsItsExitCode()
    {
        var command = new RecordingCommand("record", 7);
        var (app, _, _) = Build(command);

        Assert.Equal(7, await app.RunAsync(["RECORD", "--input", "x"]));
        Assert.Equal(["--input", "x"], command.ReceivedArgs);
    }

    [Theory]
    [InlineData("create")]
    [InlineData("upgrade")]
    [InlineData("downgrade")]
    [InlineData("generate")]
    [InlineData("export-index")]
    [InlineData("select-targets")]
    [InlineData("apply")]
    [InlineData("validate")]
    [InlineData("pack")]
    [InlineData("unpack")]
    [InlineData("mcp")]
    [InlineData("date-calc")]
    [InlineData("find-person")]
    [InlineData("get-record")]
    [InlineData("get-document-stats")]
    public async Task Default_RoutesEveryDocumentedCommandToItsUsage(string name)
    {
        var error = new StringWriter();
        var app = CliApplication.CreateDefault(new CommandContext(new StringWriter(), error));

        Assert.Equal(1, await app.RunAsync([name, "--no-such-option"]));
        Assert.DoesNotContain("Unknown command", error.ToString());
        Assert.Contains("Usage: gedfire " + name, error.ToString());
        Assert.Contains(name, HelpText.Text);
    }
}
