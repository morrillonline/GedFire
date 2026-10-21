using GedCore.Validate;

namespace GedCore.Tests;

/// <summary>
/// End-to-end: a createOrUpdateVital that replaces a DATE or PLAC with a less
/// specific form of the held value surfaces a GEN201 Warning through
/// ChangesetApplier.Run's NewDiagnostics (and so through check_plausibility
/// and the validate/apply conformance-note log lines). Advisory only: the
/// apply still succeeds, and a conflicting or more detailed replacement is
/// never flagged.
/// </summary>
public class SpecificityLossAdvisoryTests : ApplyTestBase
{
    private byte[] WriteFileWithSpecificBirth()
    {
        var lines = BaseLines.ToList();
        lines[lines.IndexOf("2 DATE 1928")] = "2 DATE 05 NOV 1874";
        lines[lines.IndexOf("2 PLAC Minnesota")] = "2 PLAC Pembroke, Yarmouth, Nova Scotia, Canada";
        return WriteFile(lines);
    }

    private static string BirthChangeset(string valueJson) => $$"""
        {
          "proposal": "test",
          "items": [
            { "item": 1, "target": "@I00001@", "ops": [
              { "op": "createOrUpdateVital", "record": "@I00001@", "fact": "BIRT",
                "value": {{valueJson}} } ] }
          ]
        }
        """;

    [Fact]
    public void DryRun_PlaceReplacedWithSuffixOfHeldHierarchy_SurfacesGen201Warning()
    {
        WriteFileWithSpecificBirth();
        var result = Run(BirthChangeset("""{ "place": "Canada" }"""), dryRun: true);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        var finding = Assert.Single(result.NewDiagnostics.Where(d => d.Code == "GEN201"));
        Assert.Equal(GedDiagnosticSeverity.Warning, finding.Severity);
        Assert.Equal("@I00001@", finding.Xref);
        Assert.Equal("BIRT", finding.Tag);
        Assert.Contains("'Pembroke, Yarmouth, Nova Scotia, Canada'", finding.Message);
        Assert.Contains("'Canada'", finding.Message);
        Assert.Contains("losing specificity is not recommended", finding.Message);
        Assert.Contains(result.Log, l => l.StartsWith("conformance note: GEN201"));
        Assert.Null(result.OutputBytes);
    }

    [Fact]
    public void RealApply_DateReplacedWithItsYear_WarnsButStillApplies()
    {
        WriteFileWithSpecificBirth();
        var result = Run(BirthChangeset("""{ "date": "1874" }"""));

        Assert.True(result.Success, string.Join("; ", result.Errors));
        var finding = Assert.Single(result.NewDiagnostics.Where(d => d.Code == "GEN201"));
        Assert.Contains("BIRT DATE '05 NOV 1874' replaced with less specific '1874'", finding.Message);

        var birth = ReadDoc().ByXref["@I00001@"].ChildrenByTag("BIRT").Single();
        Assert.Equal("1874", birth.FirstChild("DATE")!.Value);
    }

    [Fact]
    public void LossyDateAndPlaceInOneOp_RaiseOneAdvisoryEach()
    {
        WriteFileWithSpecificBirth();
        var result = Run(BirthChangeset("""{ "date": "NOV 1874", "place": "Nova Scotia, Canada" }"""), dryRun: true);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Equal(2, result.NewDiagnostics.Count(d => d.Code == "GEN201"));
    }

    [Fact]
    public void ConflictingReplacement_IsNotFlagged()
    {
        WriteFileWithSpecificBirth();
        var result = Run(BirthChangeset("""{ "date": "04 DEC 1874", "place": "Boston, Massachusetts" }"""), dryRun: true);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.DoesNotContain(result.NewDiagnostics, d => d.Code == "GEN201");
    }

    [Fact]
    public void MoreDetailedReplacement_IsNotFlagged()
    {
        WriteBaseFile();   // Allen holds BIRT 1928, Minnesota
        var result = Run(BirthChangeset("""{ "date": "05 NOV 1928", "place": "Duluth, St. Louis, Minnesota" }"""), dryRun: true);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.DoesNotContain(result.NewDiagnostics, d => d.Code == "GEN201");
    }

    [Fact]
    public void ReRunOfAnAppliedLossyChangeset_NoOpsWithoutAdvisory()
    {
        WriteFileWithSpecificBirth();
        var changeset = BirthChangeset("""{ "date": "1874" }""");
        RunExpectSuccess(changeset);
        var reRun = Run(changeset, dryRun: true);

        Assert.True(reRun.Success, string.Join("; ", reRun.Errors));
        Assert.DoesNotContain(reRun.NewDiagnostics, d => d.Code == "GEN201");
    }
}
