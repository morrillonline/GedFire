using GedFire.Cli;

namespace GedCore.Tests;

public class PackCommandTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("gedfire-pack-tests-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    [Fact]
    public async Task AnOutputThatIsTheInput_IsRefusedAndTheGedcomIsUntouched()
    {
        string ged = Path.Combine(_dir, "family.ged");
        File.WriteAllText(ged, "0 HEAD\n1 GEDC\n2 VERS 7.0\n0 @I1@ INDI\n1 NAME Allen /Test/\n0 TRLR\n");
        byte[] before = File.ReadAllBytes(ged);
        var error = new StringWriter();

        int exit = await new PackCommand().RunAsync(
            ["--input", ged, "--media-dir", _dir, "--output", ged], new CommandContext(new StringWriter(), error));

        Assert.Equal(1, exit);
        Assert.Contains("is the GEDCOM file itself", error.ToString());
        Assert.Equal(before, File.ReadAllBytes(ged));
    }
}
