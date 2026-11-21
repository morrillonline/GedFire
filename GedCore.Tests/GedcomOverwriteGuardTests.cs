using GedFire;

namespace GedCore.Tests;

public class GedcomOverwriteGuardTests
{
    static readonly string Folder = Path.Combine(Path.GetTempPath(), "gedfire-guard");
    static readonly string Gedcom = Path.Combine(Folder, "family.ged");

    [Fact]
    public void TheGedcomItself_IsRefusedNamingTheOptionAndThePath()
    {
        string? refusal = GedcomOverwriteGuard.Refusal(Gedcom, "--output", Gedcom);

        Assert.NotNull(refusal);
        Assert.StartsWith("--output " + Gedcom, refusal);
    }

    [Fact]
    public void TheGedcomSpelledAnotherWay_IsRefused()
    {
        string indirect = Path.Combine(Folder, "sub", "..", "FAMILY.GED");

        Assert.NotNull(GedcomOverwriteGuard.Refusal(indirect, "reportPath", Gedcom));
    }

    [Fact]
    public void AnotherFileBesideTheGedcom_IsAllowed()
    {
        string report = Path.Combine(Folder, "family.ged.json");

        Assert.Null(GedcomOverwriteGuard.Refusal(report, "reportPath", Gedcom));
    }
}
