using GedCore.Apply;
using GedCore.Ged70;
using GedCore.Validate;

namespace GedCore.Tests;

/// <summary>
/// Subproject D2 — the post-apply conformance gate: an apply that introduces
/// a new Error-severity <see cref="GedDiagnostic"/> is refused (file
/// untouched); pre-existing Warnings and improvements never block; the gate
/// is invisible to well-behaved changesets.
/// </summary>
public class PostApplyGateTests : ApplyTestBase
{
    private static byte[] Seed(params string[] lines)
    {
        var doc = Ged70Parser.Parse(string.Join("\r\n", lines) + "\r\n");
        var ms = new MemoryStream();
        Ged70Formatter.Write(doc, ms);
        return ms.ToArray();
    }

    private static GedDiagnostic Diag(GedDiagnosticSeverity severity, string code, string xref, string tag,
                                       string message = "m") => new(severity, code, message, xref, tag);

    // -------------------------------------------------------------------
    // DiffDiagnostics — the multiset-increase mechanism itself
    // -------------------------------------------------------------------

    [Fact]
    public void DiffDiagnostics_FlagsOnlyIncreasedKeys()
    {
        var before = new[]
        {
            Diag(GedDiagnosticSeverity.Error, "GED004", "@I1@", "FAMC"),
            Diag(GedDiagnosticSeverity.Warning, "GED010", "@I2@", "_FOO"),
        };
        var after = new[]
        {
            Diag(GedDiagnosticSeverity.Error, "GED004", "@I1@", "FAMC"),   // unchanged
            Diag(GedDiagnosticSeverity.Error, "GED004", "@I3@", "FAMC"),   // new
            Diag(GedDiagnosticSeverity.Warning, "GED010", "@I2@", "_FOO"), // unchanged
            Diag(GedDiagnosticSeverity.Warning, "GED010", "@I4@", "_BAR"), // new
        };

        var increased = ConformanceChecker.DiffDiagnostics(before, after);

        Assert.Equal(2, increased.Count);
        Assert.Contains(increased, d => d.Code == "GED004" && d.Xref == "@I3@");
        Assert.Contains(increased, d => d.Code == "GED010" && d.Xref == "@I4@");
    }

    [Fact]
    public void DiffDiagnostics_DecreasedOrRemovedCounts_AreNeverFlagged()
    {
        var before = new[]
        {
            Diag(GedDiagnosticSeverity.Error, "GED004", "@I1@", "FAMC"),
            Diag(GedDiagnosticSeverity.Error, "GED004", "@I2@", "FAMC"),
        };
        var after = new[] { Diag(GedDiagnosticSeverity.Error, "GED004", "@I1@", "FAMC") };

        Assert.Empty(ConformanceChecker.DiffDiagnostics(before, after));
        Assert.Empty(ConformanceChecker.DiffDiagnostics(before, []));
    }

    // -------------------------------------------------------------------
    // End-to-end via ChangesetApplier.Run
    // -------------------------------------------------------------------

    [Fact]
    public void RegressionBlocked_CitationNamingAnIndiAsSource_IsRefusedByTheGate()
    {
        // Op validation checks only that a citation's source xref EXISTS
        // (OpChecks.CitationsValid → ctx.Known), not that it names a SOUR
        // record. Citing @I00002@ (an INDI — a realistic slip: a person xref
        // pasted into the source field) therefore passes validation and
        // apply, and only the post-apply gate's GED005 target-type check
        // stands between it and the file.
        //
        // If op-level validation ever learns to reject wrong-type citation
        // sources, this test will fail on the dry-run assertion below —
        // update it then to whatever hole remains, or retire it in favor of
        // the DiffDiagnostics unit tests above.
        WriteBaseFile();
        var before = ReadBytes();
        Assert.Empty(ConformanceChecker.Check(ReadDoc()));

        const string changesetJson = """
        {
          "proposal": "test",
          "items": [
            { "item": 1, "target": "@I00001@", "ops": [
              { "op": "createOrUpdateVital", "record": "@I00001@", "fact": "DEAT",
                "value": { "date": "5 MAY 1999", "place": "Minnesota" },
                "citation": { "source": "@I00002@", "page": "p. 1", "quay": 2 } } ] }
          ]
        }
        """;

        // Dry-run passes: op validation cannot see the problem.
        var dryRun = Run(changesetJson, dryRun: true);
        Assert.True(dryRun.Success, string.Join("; ", dryRun.Errors));

        // Real apply is refused by the gate, and the file is untouched.
        var result = Run(changesetJson);
        Assert.False(result.Success);
        Assert.Contains(result.Errors, e =>
            e.StartsWith("conformance regression: GED005") && e.Contains("@I00002@"));
        Assert.Null(result.OutputBytes);
        Assert.Equal(before, ReadBytes());
        Assert.Empty(ConformanceChecker.Check(ReadDoc()));
    }

    [Fact]
    public void WellBehavedChangeset_IsInvisibleToTheGate()
    {
        WriteBaseFile();
        Assert.Empty(ConformanceChecker.Check(ReadDoc()));

        var result = RunExpectSuccess("""
        {
          "proposal": "test",
          "items": [
            { "item": 1, "target": "@I00001@", "ops": [
              { "op": "createOrUpdateVital", "record": "@I00001@", "fact": "NAME",
                "value": "Albin H. /Test/", "match": "Allen /Test/",
                "citation": { "source": "@S00001@", "page": "p. 1",
                              "dataText": "Albin H. Test", "quay": 2 } } ] }
          ]
        }
        """);

        Assert.DoesNotContain(result.Errors, e => e.Contains("conformance"));
        Assert.DoesNotContain(result.Log, l => l.Contains("conformance note"));
        Assert.Empty(ConformanceChecker.Check(ReadDoc()));
    }

    [Fact]
    public void BaselineTolerance_PreExistingWarningNeitherBlocksNorLogsAsANote()
    {
        var bytes = Seed(
            "0 HEAD",
            "1 GEDC",
            "2 VERS 7.0",
            "0 @I00001@ INDI",
            "1 NAME Allen /Test/",
            "1 SEX M",
            "1 _FOOT Some legacy footnote",
            "0 @S00001@ SOUR",
            "1 TITL Existing source",
            "0 TRLR");

        var before = ConformanceChecker.Check(Ged70Parser.Read(new MemoryStream(bytes)));
        Assert.Contains(before, d => d.Code == "GED010");

        const string changesetJson = """
        {
          "proposal": "test",
          "items": [
            { "item": 1, "target": "@I00001@", "ops": [
              { "op": "createOrUpdateVital", "record": "@I00001@", "fact": "NAME",
                "value": "Albin H. /Test/", "match": "Allen /Test/",
                "citation": { "source": "@S00001@", "page": "p. 1",
                              "dataText": "Albin H. Test", "quay": 2 } } ] }
          ]
        }
        """;

        var result = ChangesetApplier.Run(bytes, Changeset.Parse(changesetJson), [1], dryRun: false);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.DoesNotContain(result.Errors, e => e.Contains("conformance"));
        Assert.DoesNotContain(result.Log, l => l.Contains("conformance note"));

        var after = ConformanceChecker.Check(Ged70Parser.Read(new MemoryStream(result.OutputBytes!)));
        Assert.Single(after.Where(d => d.Code == "GED010"));
    }

    [Fact]
    public void ImprovementAllowed_MergeRemovingDuplicateWithWrongTypeFamc_SucceedsWithFewerDiagnostics()
    {
        // Two duplicate INDI records both erroneously point FAMC at a SOUR record
        // (resolves fine, so the applier's own pointer-existence invariant in
        // Verify() doesn't block it — only ConformanceChecker's GED005 target-type
        // check catches this) — a realistic shape for the record a merge cleans up.
        var bytes = Seed(
            "0 HEAD",
            "1 GEDC",
            "2 VERS 7.0",
            "0 @S00001@ SOUR",
            "1 TITL Existing source",
            "0 @I00001@ INDI",
            "1 NAME Allen /Test/",
            "1 SEX M",
            "1 FAMC @S00001@",
            "0 @I00002@ INDI",
            "1 NAME Allen /Test/",
            "1 SEX M",
            "1 FAMC @S00001@",
            "0 TRLR");

        var before = ConformanceChecker.Check(Ged70Parser.Read(new MemoryStream(bytes)));
        Assert.Equal(2, before.Count(d => d.Code == "GED005"));

        const string changesetJson = """
        {
          "proposal": "test",
          "items": [
            { "item": 1, "target": "@I00001@", "ops": [
              { "op": "mergePerson", "survivor": "@I00001@", "duplicate": "@I00002@" } ] }
          ]
        }
        """;

        var result = ChangesetApplier.Run(bytes, Changeset.Parse(changesetJson), [1], dryRun: false);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.DoesNotContain(result.Errors, e => e.Contains("conformance regression"));

        var after = ConformanceChecker.Check(Ged70Parser.Read(new MemoryStream(result.OutputBytes!)));
        Assert.Equal(1, after.Count(d => d.Code == "GED005"));
        Assert.True(after.Count < before.Count);
    }
}
