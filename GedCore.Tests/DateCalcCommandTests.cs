using GedFire.Cli;

namespace GedCore.Tests;

public class DateCalcCommandTests
{
    static async Task<(int Exit, string Out, string Error)> Run(params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        int exit = await new DateCalcCommand().RunAsync(args, new CommandContext(output, error));
        return (exit, output.ToString().Trim(), error.ToString());
    }

    [Fact]
    public async Task Normalize_ResolvesADualDate()
    {
        var (exit, output, _) = await Run("--op", "normalize", "--date", "11 FEB 1691/2");

        Assert.Equal(0, exit);
        Assert.Equal("11 FEB 1692", output);
    }

    [Theory]
    [InlineData("add", "29 JAN 1841")]
    [InlineData("sub", "27 SEP 1777")]
    public async Task AddAndSub_MoveTheDateByTheAge(string op, string expected)
    {
        string date = op == "add" ? "27 SEP 1777" : "29 JAN 1841";
        var (exit, output, _) = await Run("--op", op, "--date", date, "--age", "63y 4m 2d");

        Assert.Equal(0, exit);
        Assert.Equal(expected, output);
    }

    [Fact]
    public async Task Diff_ReportsTheElapsedTime()
    {
        var (exit, output, _) = await Run("--op", "diff", "--from", "27 SEP 1777", "--to", "29 JAN 1841");

        Assert.Equal(0, exit);
        Assert.Equal("63y 4m 2d", output);
    }

    [Theory]
    [InlineData("normalize --date 1 JAN 1900 --age 1y", "--op normalize accepts only --date")]
    [InlineData("add --date 1 JAN 1900 --age 1y --to 2 JAN 1900", "--op add accepts only --date and --age")]
    [InlineData("diff --from 1 JAN 1900 --to 2 JAN 1900 --date 1 JAN 1900", "--op diff accepts only --from and --to")]
    public async Task AnOptionTheOperationDoesNotTake_IsRefused(string commandLine, string expected)
    {
        var (exit, _, error) = await Run(Split(commandLine));

        Assert.Equal(1, exit);
        Assert.StartsWith(expected, error);
        Assert.Contains("Usage: gedfire date-calc", error);
    }

    [Theory]
    [InlineData("normalize", "--op normalize requires --date")]
    [InlineData("add --date 1JAN1900", "--op add requires --date and --age")]
    [InlineData("diff --from 1JAN1900", "--op diff requires --from and --to")]
    public async Task AMissingOperand_IsRefused(string commandLine, string expected)
    {
        var (exit, _, error) = await Run(Split(commandLine));

        Assert.Equal(1, exit);
        Assert.StartsWith(expected, error);
    }

    [Fact]
    public async Task AnUnrecognizedOperation_IsRefusedWithTheUsage()
    {
        var (exit, _, error) = await Run("--op", "multiply");

        Assert.Equal(1, exit);
        Assert.StartsWith("Unrecognized --op: multiply", error);
        Assert.Contains("Usage: gedfire date-calc", error);
    }

    [Fact]
    public async Task AMalformedDate_IsAUsageErrorNotACrash()
    {
        var (exit, _, error) = await Run("--op", "normalize", "--date", "not a date");

        Assert.Equal(1, exit);
        Assert.NotEmpty(error);
    }

    [Fact]
    public async Task WithoutAnOperation_PrintsTheUsage()
    {
        var (exit, _, error) = await Run();

        Assert.Equal(1, exit);
        Assert.StartsWith("Usage: gedfire date-calc", error);
    }

    // Dates here are written without spaces, so splitting on spaces keeps each option's value whole.
    static string[] Split(string commandLine)
    {
        var parts = commandLine.Split(' ');
        var args = new List<string> { "--op", parts[0] };
        for (int i = 1; i < parts.Length;)
        {
            int next = Array.FindIndex(parts, i + 1, p => p.StartsWith("--", StringComparison.Ordinal));
            if (next < 0) next = parts.Length;
            args.Add(parts[i]);
            args.Add(string.Join(' ', parts[(i + 1)..next]));
            i = next;
        }
        return [.. args];
    }
}
