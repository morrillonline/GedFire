using System.Text.Json;

namespace GedCore.Tests;

// ---------------------------------------------------------------------------
// End-to-end coverage for the `gedfire find-person` CLI verb: a one-shot
// mirror of the mcp server's find_person tool over the real process, its
// flag validation, and exit codes. FindPersonToolTests already covers the
// tool's own behavior in depth; these tests exist to prove the CLI wiring
// -- argument parsing, hint-flag assembly, JSON output -- not to re-derive
// matching behavior.
// ---------------------------------------------------------------------------

public class FindPersonCliTests : IDisposable
{
    static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    readonly string _dir = Directory.CreateTempSubdirectory("gedfire-find-person-cli-tests-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    string WriteGed()
    {
        string path = Path.Combine(_dir, "family.ged");
        File.WriteAllText(path, """
            0 HEAD
            1 GEDC
            2 VERS 7.0
            0 @I1@ INDI
            1 NAME Frederick /Morrill/
            1 SEX M
            1 BIRT
            2 DATE 12 MAR 1841
            2 PLAC Gorham, Maine
            0 TRLR

            """);
        return path;
    }

    static Task<(int ExitCode, string Stdout, string Stderr)> Run(params string[] args) =>
        McpStdioTestClient.RunToCompletionAsync(Timeout, ["find-person", .. args]);

    [Fact]
    public async Task SingleMatch_PrintsStructuredJsonAndExitsZero()
    {
        var (exitCode, stdout, _) = await Run("--input", WriteGed(), "--query", "Frederick Morrill");

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(stdout);
        Assert.Equal("single", doc.RootElement.GetProperty("matchType").GetString());
        Assert.Equal("@I1@", doc.RootElement.GetProperty("confidentMatchXref").GetString());
    }

    [Fact]
    public async Task BirthHintFlags_KeepAConfidentMatchAtFullScore()
    {
        var (exitCode, stdout, _) = await Run(
            "--input", WriteGed(), "--query", "Frederick Morrill",
            "--birth-year", "1841", "--birth-place", "Maine");

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(stdout);
        Assert.Equal(100.0, doc.RootElement.GetProperty("confidentMatchScore").GetDouble(), 3);
    }

    [Fact]
    public async Task MaxResultsFlag_IsPassedThrough()
    {
        var (exitCode, stdout, _) = await Run(
            "--input", WriteGed(), "--query", "Frederick Morrill", "--max-results", "1");

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(stdout);
        Assert.Single(doc.RootElement.GetProperty("candidates").EnumerateArray());
    }

    [Fact]
    public async Task MissingQuery_IsUsageError()
    {
        var (exitCode, _, stderr) = await Run("--input", WriteGed());

        Assert.Equal(1, exitCode);
        Assert.Contains("Usage: gedfire find-person", stderr);
    }

    [Fact]
    public async Task MissingInputFile_IsUsageError()
    {
        var (exitCode, _, stderr) = await Run(
            "--input", Path.Combine(_dir, "missing.ged"), "--query", "Frederick Morrill");

        Assert.Equal(1, exitCode);
        Assert.Contains("Input file not found", stderr);
    }

    [Fact]
    public async Task InvalidMaxResults_IsUsageError()
    {
        var (exitCode, _, stderr) = await Run(
            "--input", WriteGed(), "--query", "Frederick Morrill", "--max-results", "bogus");

        Assert.Equal(1, exitCode);
        Assert.Contains("--max-results must be an integer", stderr);
    }
}
