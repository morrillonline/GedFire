using System.Text.Json;

namespace GedCore.Tests;

// ---------------------------------------------------------------------------
// End-to-end coverage for the `gedfire get-record` CLI verb: a one-shot
// mirror of the mcp server's get_record tool over the real process.
// GetRecordToolTests already covers the tool's own mapping behavior in
// depth; these tests exist to prove the CLI wiring, not re-derive it.
// ---------------------------------------------------------------------------

public class GetRecordCliTests : IDisposable
{
    static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    readonly string _dir = Directory.CreateTempSubdirectory("gedfire-get-record-cli-tests-").FullName;

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
            0 TRLR

            """);
        return path;
    }

    static Task<(int ExitCode, string Stdout, string Stderr)> Run(params string[] args) =>
        McpStdioTestClient.RunToCompletionAsync(Timeout, ["get-record", .. args]);

    [Fact]
    public async Task ResolvesAPersonXref()
    {
        var (exitCode, stdout, _) = await Run("--input", WriteGed(), "--xref", "@I1@");

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(stdout);
        Assert.Equal("@I1@", doc.RootElement.GetProperty("xref").GetString());
        Assert.Equal("person", doc.RootElement.GetProperty("recordType").GetString());
    }

    [Fact]
    public async Task UnresolvableXref_ReturnsNotFoundButExitsZero()
    {
        var (exitCode, stdout, _) = await Run("--input", WriteGed(), "--xref", "@I404@");

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(stdout);
        Assert.Equal("not_found", doc.RootElement.GetProperty("recordType").GetString());
    }

    [Fact]
    public async Task MissingXref_IsUsageError()
    {
        var (exitCode, _, stderr) = await Run("--input", WriteGed());

        Assert.Equal(1, exitCode);
        Assert.Contains("Usage: gedfire get-record", stderr);
    }

    [Fact]
    public async Task MissingInputFile_IsUsageError()
    {
        var (exitCode, _, stderr) = await Run(
            "--input", Path.Combine(_dir, "missing.ged"), "--xref", "@I1@");

        Assert.Equal(1, exitCode);
        Assert.Contains("Input file not found", stderr);
    }
}
