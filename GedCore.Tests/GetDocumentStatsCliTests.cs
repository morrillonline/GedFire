using System.Text.Json;

namespace GedCore.Tests;

// ---------------------------------------------------------------------------
// End-to-end coverage for the `gedfire get-document-stats` CLI verb: a
// one-shot mirror of the mcp server's get_document_stats tool over the real
// process. GetDocumentStatsToolTests already covers the tool's own mapping
// behavior in depth; these tests exist to prove the CLI wiring.
// ---------------------------------------------------------------------------

public class GetDocumentStatsCliTests : IDisposable
{
    static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

    readonly string _dir = Directory.CreateTempSubdirectory("gedfire-get-document-stats-cli-tests-").FullName;

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
        McpStdioTestClient.RunToCompletionAsync(Timeout, ["get-document-stats", .. args]);

    [Fact]
    public async Task ReturnsCountsVersionAndGedFireVersion()
    {
        var (exitCode, stdout, _) = await Run("--input", WriteGed());

        Assert.Equal(0, exitCode);
        using var doc = JsonDocument.Parse(stdout);
        Assert.Equal(1, doc.RootElement.GetProperty("personCount").GetInt32());
        Assert.Equal(0, doc.RootElement.GetProperty("familyCount").GetInt32());
        Assert.Equal("7.0", doc.RootElement.GetProperty("gedVersion").GetString());
        Assert.False(string.IsNullOrWhiteSpace(doc.RootElement.GetProperty("gedFireVersion").GetString()));
    }

    [Fact]
    public async Task MissingInputFile_IsUsageError()
    {
        var (exitCode, _, stderr) = await Run("--input", Path.Combine(_dir, "missing.ged"));

        Assert.Equal(1, exitCode);
        Assert.Contains("Input file not found", stderr);
    }

    [Fact]
    public async Task MissingInputFlag_IsUsageError()
    {
        var (exitCode, _, stderr) = await Run();

        Assert.Equal(1, exitCode);
        Assert.Contains("Usage: gedfire get-document-stats", stderr);
    }
}
