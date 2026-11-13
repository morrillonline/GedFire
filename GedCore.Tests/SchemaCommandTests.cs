using System.Text.Json;
using GedFire.Cli;
using GedFire.Schemas;

namespace GedCore.Tests;

public class SchemaCommandTests
{
    static async Task<(int Exit, string Output, string Error)> Run(params string[] args)
    {
        var output = new StringWriter();
        var error = new StringWriter();
        int exit = await new SchemaCommand().RunAsync(args, new CommandContext(output, error));
        return (exit, output.ToString(), error.ToString());
    }

    [Fact]
    public async Task ASchemaName_PrintsItsVersionedDocument()
    {
        var result = await Run("finding-file");

        Assert.Equal(0, result.Exit);
        using var json = JsonDocument.Parse(result.Output);
        Assert.Equal(SchemaCatalog.FindingFile.Id, json.RootElement.GetProperty("$id").GetString());
        Assert.Equal(SchemaCatalog.FindingFile.Version, json.RootElement.GetProperty("x-schemaVersion").GetString());
    }

    [Theory]
    [InlineData]
    [InlineData("persons")]
    [InlineData("finding", "--bogus")]
    public async Task AMissingOrUnknownSchemaOrOption_Fails(params string[] args)
    {
        var result = await Run(args);

        Assert.Equal(1, result.Exit);
        Assert.Contains("Usage: gedfire schema <finding|finding-file|record|argument>", result.Error);
    }

    [Fact]
    public async Task Out_WritesANewFileAndRefusesAnExistingOne()
    {
        string path = Path.Combine(Path.GetTempPath(), $"gedfire-schema-{Guid.NewGuid()}.json");
        try
        {
            var written = await Run("record", "--out", path);
            var refused = await Run("record", "--out", path);

            Assert.Equal(0, written.Exit);
            Assert.Empty(written.Output);
            using (var json = JsonDocument.Parse(File.ReadAllText(path)))
                Assert.Equal(SchemaCatalog.Record.Id, json.RootElement.GetProperty("$id").GetString());
            Assert.Equal(1, refused.Exit);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
