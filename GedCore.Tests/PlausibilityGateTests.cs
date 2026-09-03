using GedCore.Apply;
using GedCore.Validate;

namespace GedCore.Tests;

/// <summary>
/// End-to-end: PlausibilityChecker findings surface through
/// ChangesetApplier.Run's before/after diff gate, on both the dry-run path
/// (the plausibility-checker design's prerequisite fix) and the real-apply
/// path — including the combination case (a changeset's new fact plus the
/// target's pre-existing, untouched fact). See docs/design/plausibility-checker.md.
/// </summary>
public class PlausibilityGateTests : ApplyTestBase
{
    // @I00001@ (Allen) has a pre-existing BIRT 1928 in ApplyTestBase.BaseLines
    // that this changeset never touches — the DEAT it adds only becomes
    // implausible in combination with that untouched fact.
    private const string DeathBeforeBirthChangeset = """
        {
          "proposal": "test",
          "items": [
            { "item": 1, "target": "@I00001@", "ops": [
              { "op": "createOrUpdateVital", "record": "@I00001@", "fact": "DEAT",
                "value": { "date": "1 JAN 1900" } } ] }
          ]
        }
        """;

    [Fact]
    public void DryRun_CombinationOfNewFactAndPreExistingFact_SurfacesGen101()
    {
        WriteBaseFile();
        var result = Run(DeathBeforeBirthChangeset, dryRun: true);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Contains(result.NewDiagnostics, d => d.Code == "GEN101" && d.Xref == "@I00001@");
        Assert.Contains(result.Log, l => l.StartsWith("conformance note: GEN101"));
        Assert.Null(result.OutputBytes);
    }

    [Fact]
    public void RealApply_SameCombination_SurfacesTheIdenticalGen101Finding()
    {
        WriteBaseFile();
        var dryRun = Run(DeathBeforeBirthChangeset, dryRun: true);
        var real = Run(DeathBeforeBirthChangeset);

        Assert.True(real.Success, string.Join("; ", real.Errors));
        var dryFinding = Assert.Single(dryRun.NewDiagnostics.Where(d => d.Code == "GEN101"));
        var realFinding = Assert.Single(real.NewDiagnostics.Where(d => d.Code == "GEN101"));
        Assert.Equal(dryFinding.Message, realFinding.Message);
    }

    [Fact]
    public void PreExistingPlausibilityWarning_TheFileAlreadyCarried_IsNotFlaggedAsNew()
    {
        // Seed a file that already has Nellie married before her own recorded
        // birth (a pre-existing GEN101 the baseline already carries), then
        // apply an unrelated changeset. DiffDiagnostics only counts findings
        // whose occurrence count increased — the baseline's own warning is
        // not this changeset's problem.
        var lines = BaseLines.ToList();
        int idx = lines.IndexOf("1 NAME Nellie /Test/");
        lines.Insert(idx + 1, "1 BIRT");
        lines.Insert(idx + 2, "2 DATE 1960");
        lines.Insert(idx + 3, "1 DEAT");
        lines.Insert(idx + 4, "2 DATE 1900");   // DEAT before her own BIRT — a pre-existing GEN101
        WriteFile(lines);

        Assert.NotEmpty(PlausibilityChecker.Check(ReadDoc()).Where(d => d.Code == "GEN101" && d.Xref == "@I00003@"));

        const string unrelatedChangeset = """
            {
              "proposal": "test",
              "items": [
                { "item": 1, "target": "@I00004@", "ops": [
                  { "op": "createOrUpdateVital", "record": "@I00004@", "fact": "BIRT",
                    "value": { "date": "1 JAN 1930" } } ] }
              ]
            }
            """;
        var result = RunExpectSuccess(unrelatedChangeset);
        Assert.DoesNotContain(result.NewDiagnostics, d => d.Xref == "@I00003@");
    }

    // "Allan /Test/" born 1930 scores 89.3 against the existing "Allen
    // /Test/" born 1928 (@I00001@) -- close enough to recall, not close
    // enough for PersonMatchCore's own Single/hard-match classification
    // (needs >=90 with a 10-point margin), so the changeset is not blocked
    // by PersonDuplicateDetector's pre-apply hard block (that's a different,
    // stricter check on new-vs-existing evidence only). GEN301 is the rule
    // that has to catch a match in that gap -- "worth a look", not a reason
    // to refuse the changeset.
    private const string NewSimilarChildChangeset = """
        {
          "proposal": "test",
          "items": [
            { "item": 1, "target": "@F00001@", "ops": [
              { "op": "createOrUpdateChild", "family": "@F00001@",
                "child": { "xref": "@NewI1@", "name": "Allan /Test/", "sex": "M",
                           "facts": [ { "fact": "BIRT", "value": { "date": "1930" } } ] } } ] }
          ]
        }
        """;

    [Fact]
    public void NewPersonScoringAsProbableDuplicate_SurfacesGen301_WithoutBlockingTheChangeset()
    {
        WriteBaseFile();
        var result = Run(NewSimilarChildChangeset, dryRun: true);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Contains(result.NewDiagnostics, d =>
            d.Code == "GEN301" && d.Severity == GedDiagnosticSeverity.Warning &&
            d.Message.Contains("@I00001@") && d.Message.Contains("@I00005@"));
        Assert.Contains(result.Log, l => l.StartsWith("conformance note: GEN301"));
    }

    [Fact]
    public void NewPersonScoringAsProbableDuplicate_RealApply_SurfacesTheIdenticalFinding()
    {
        WriteBaseFile();
        var dryRun = Run(NewSimilarChildChangeset, dryRun: true);
        var real = Run(NewSimilarChildChangeset);

        Assert.True(real.Success, string.Join("; ", real.Errors));
        var dryFinding = Assert.Single(dryRun.NewDiagnostics.Where(d => d.Code == "GEN301"));
        var realFinding = Assert.Single(real.NewDiagnostics.Where(d => d.Code == "GEN301"));
        Assert.Equal(dryFinding.Message, realFinding.Message);
    }
}
