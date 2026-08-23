namespace GedCore.Tests;

// ---------------------------------------------------------------------------
// End-to-end coverage for the `gedfire date-calc` CLI verb: Program.RunDateCalc
// is a thin argument-and-output adapter over GedCore.GedDate/GedAge
// (GedDateArithmeticTests proves the engine itself); these tests exercise the
// actual process, its flag validation, output formatting, and exit codes. No
// GEDCOM file is touched or required by any of them.
// ---------------------------------------------------------------------------

public class DateCalcCliTests
{
    static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    static Task<(int ExitCode, string Stdout, string Stderr)> Run(params string[] args) =>
        McpStdioTestClient.RunToCompletionAsync(Timeout, ["date-calc", .. args]);

    // -------------------------------------------------------------------
    // The design document's own four worked examples, verbatim.
    // -------------------------------------------------------------------

    [Fact]
    public async Task Normalize_WorkedExample()
    {
        var (exitCode, stdout, _) = await Run("--op", "normalize", "--date", "11 FEB 1691/2");
        Assert.Equal(0, exitCode);
        Assert.Equal("11 FEB 1692", stdout.Trim());
    }

    [Fact]
    public async Task Add_WorkedExample()
    {
        var (exitCode, stdout, _) = await Run("--op", "add", "--date", "27 SEP 1777", "--age", "63y 4m 2d");
        Assert.Equal(0, exitCode);
        Assert.Equal("29 JAN 1841", stdout.Trim());
    }

    [Fact]
    public async Task Sub_WorkedExample()
    {
        var (exitCode, stdout, _) = await Run("--op", "sub", "--date", "29 JAN 1841", "--age", "63y 4m 2d");
        Assert.Equal(0, exitCode);
        Assert.Equal("27 SEP 1777", stdout.Trim());
    }

    [Fact]
    public async Task Diff_WorkedExample_RoundTripsIntoAddsAgeArgument()
    {
        var (exitCode, stdout, _) = await Run("--op", "diff", "--from", "27 SEP 1777", "--to", "29 JAN 1841");
        Assert.Equal(0, exitCode);
        Assert.Equal("63y 4m 2d", stdout.Trim());
    }

    // -------------------------------------------------------------------
    // Usage errors: exit 1, message on stderr, nothing on stdout
    // -------------------------------------------------------------------

    [Fact]
    public async Task MissingOp_IsUsageError()
    {
        var (exitCode, stdout, stderr) = await Run("--date", "1 JAN 2000");
        Assert.Equal(1, exitCode);
        Assert.Empty(stdout.Trim());
        Assert.Contains("Usage:", stderr);
    }

    [Fact]
    public async Task UnrecognizedOp_IsUsageError()
    {
        var (exitCode, _, stderr) = await Run("--op", "frobnicate");
        Assert.Equal(1, exitCode);
        Assert.Contains("frobnicate", stderr);
    }

    [Fact]
    public async Task Diff_WithAgeFlag_IsUsageError()
    {
        // The design's own example of a disallowed flag/op combination.
        var (exitCode, _, stderr) = await Run("--op", "diff", "--from", "1 JAN 2000", "--to", "1 JAN 2001", "--age", "1y");
        Assert.Equal(1, exitCode);
        Assert.Contains("--age", stderr);
    }

    [Fact]
    public async Task Add_MissingAge_IsUsageError()
    {
        var (exitCode, _, stderr) = await Run("--op", "add", "--date", "1 JAN 2000");
        Assert.Equal(1, exitCode);
        Assert.Contains("--age", stderr);
    }

    [Fact]
    public async Task Normalize_WithFromTo_IsUsageError()
    {
        var (exitCode, _, stderr) = await Run("--op", "normalize", "--from", "1 JAN 2000", "--to", "1 JAN 2001");
        Assert.Equal(1, exitCode);
        Assert.Contains("--date", stderr);
    }

    // -------------------------------------------------------------------
    // Input-grammar errors surface as exit 1 with the engine's own message
    // -------------------------------------------------------------------

    [Fact]
    public async Task Add_QualifiedDate_IsRejected()
    {
        var (exitCode, _, stderr) = await Run("--op", "add", "--date", "ABT 1780", "--age", "1y");
        Assert.Equal(1, exitCode);
        Assert.NotEmpty(stderr);
    }

    [Fact]
    public async Task Diff_ReversedDates_IsRejected()
    {
        var (exitCode, _, stderr) = await Run("--op", "diff", "--from", "1 JAN 2001", "--to", "1 JAN 2000");
        Assert.Equal(1, exitCode);
        Assert.NotEmpty(stderr);
    }

    [Fact]
    public async Task Add_MalformedAge_IsRejected()
    {
        var (exitCode, _, stderr) = await Run("--op", "add", "--date", "1 JAN 2000", "--age", "1 year");
        Assert.Equal(1, exitCode);
        Assert.NotEmpty(stderr);
    }

    [Fact]
    public async Task NoInputFileIsReadOrRequired()
    {
        // A pure computation over its own arguments: no --input flag exists
        // for this verb at all, and the process must succeed with none.
        var (exitCode, stdout, _) = await Run("--op", "diff", "--from", "1 JAN 2000", "--to", "1 JAN 2000");
        Assert.Equal(0, exitCode);
        Assert.Equal("0y 0m 0d", stdout.Trim());
    }
}
