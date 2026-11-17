using GedCore.Ged70;
using GedFire.Cli;

namespace GedCore.Tests;

public class ApplyCommandTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("gedfire-apply-command-tests-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    string WriteGed()
    {
        string path = Path.Combine(_dir, "test.ged");
        var document = Ged70Parser.Parse("0 HEAD\r\n1 GEDC\r\n2 VERS 7.0\r\n0 @I1@ INDI\r\n1 NAME Allen /Test/\r\n0 TRLR\r\n");
        using var stream = File.Create(path);
        Ged70Formatter.Write(document, stream);
        return path;
    }

    async Task<(int Exit, string Output, string Error)> Apply(string changesetJson, string ged)
    {
        string changeset = Path.Combine(_dir, "changeset.json");
        File.WriteAllText(changeset, changesetJson);
        var output = new StringWriter();
        var error = new StringWriter();
        int exit = await new ApplyCommand().RunAsync(
            ["--input", ged, "--changes", changeset, "--items", "all"], new CommandContext(output, error));
        return (exit, output.ToString(), error.ToString());
    }

    [Fact]
    public async Task ACanonicalChangeset_IsAppliedSilently()
    {
        string ged = WriteGed();

        var result = await Apply("""
            { "items": [ { "item": 1, "ops": [ { "op": "createOrUpdateVital", "record": "@I1@", "fact": "BIRT", "date": "1900" } ] } ] }
            """, ged);

        Assert.Equal(0, result.Exit);
        Assert.Equal("", result.Error);
        Assert.Contains("2 DATE 1900", File.ReadAllText(ged));
    }

    [Theory]
    [InlineData("""{ "schemaVersion": "1.1", "items": [] }""", "schemaVersion: unknown field")]
    [InlineData("""{ "items": [ { "item": 1, "ops": [ { "op": "createOrUpdateVital", "record": "@I1@", "fact": "BIRT", "value": { "date": "1900" } } ] } ] }""",
        "items[0].ops[0].value: an object value is not read")]
    public async Task AChangesetTheContractRefuses_FailsWithoutTouchingTheFile(string changeset, string error)
    {
        string ged = WriteGed();
        byte[] before = File.ReadAllBytes(ged);

        var result = await Apply(changeset, ged);

        Assert.Equal(1, result.Exit);
        Assert.Contains(error, result.Error);
        Assert.Equal(before, File.ReadAllBytes(ged));
    }
}
