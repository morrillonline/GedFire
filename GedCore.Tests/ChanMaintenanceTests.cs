using GedCore.Apply;
using GedCore.Ged70;

namespace GedCore.Tests;

/// <summary>
/// Subproject F — CHAN (change date) maintenance in the Apply layer: every
/// level-0 record a run mutates in place gains/updates a CHAN/DATE/TIME
/// stamp; untouched records are left alone; the stamp itself never
/// perpetuates (no-op changesets stamp nothing, and validate/dry-run never
/// reach this phase).
/// </summary>
public class ChanMaintenanceTests : ApplyTestBase
{
    private static byte[] Seed(params string[] lines)
    {
        var doc = Ged70Parser.Parse(string.Join("\r\n", lines) + "\r\n");
        var ms = new MemoryStream();
        Ged70Formatter.Write(doc, ms);
        return ms.ToArray();
    }

    private static readonly DateTime FixedClock =
        new(2026, 7, 18, 14, 5, 9, DateTimeKind.Utc);

    private const string VitalUpdateChangesetJson = """
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

    [Fact]
    public void Apply_WithFixedClock_StampsChanOnlyOnTouchedRecord()
    {
        WriteBaseFile();

        var result = RunExpectSuccess(VitalUpdateChangesetJson, utcNow: FixedClock);
        Assert.Contains(result.Log, l => l.Contains("stamped CHAN on @I00001@"));

        var doc = ReadDoc();

        var touched = doc.ByXref["@I00001@"];
        var chan = touched.FirstChild("CHAN");
        Assert.NotNull(chan);
        Assert.Equal("18 JUL 2026", chan!.FirstChild("DATE")!.Value);
        Assert.Equal("14:05:09Z", chan.FirstChild("DATE")!.FirstChild("TIME")!.Value);
        Assert.Same(chan, touched.ChildrenByTag("CHAN").Single());   // exactly one CHAN

        // Harvey (@I00002@) is untouched by this changeset and gets no CHAN at all.
        var untouched = doc.ByXref["@I00002@"];
        Assert.Null(untouched.FirstChild("CHAN"));
    }

    [Fact]
    public void Apply_RecordWithExistingChan_ReplacesValue_NeverDuplicates()
    {
        var bytes = Seed(
            "0 HEAD",
            "1 GEDC",
            "2 VERS 7.0",
            "0 @I00001@ INDI",
            "1 NAME Allen /Test/",
            "1 SEX M",
            "1 CHAN",
            "2 DATE 01 JAN 2020",
            "3 TIME 00:00:00Z",
            "0 @S00001@ SOUR",
            "1 TITL Existing source",
            "0 TRLR");

        var result = ChangesetApplier.Run(
            bytes, Changeset.Parse(VitalUpdateChangesetJson), [1], dryRun: false, FixedClock);

        Assert.True(result.Success, string.Join("; ", result.Errors));

        var doc = Ged70Parser.Read(new MemoryStream(result.OutputBytes!));
        var indi = doc.ByXref["@I00001@"];
        Assert.Single(indi.ChildrenByTag("CHAN"));   // no second CHAN added
        var chan = indi.FirstChild("CHAN")!;
        Assert.Equal("18 JUL 2026", chan.FirstChild("DATE")!.Value);
        Assert.Equal("14:05:09Z", chan.FirstChild("DATE")!.FirstChild("TIME")!.Value);
    }

    [Fact]
    public void Apply_NoOpChangeset_StampsNoChanAnywhere()
    {
        WriteBaseFile();
        RunExpectSuccess(VitalUpdateChangesetJson, utcNow: FixedClock);   // first run: real mutation

        // Re-apply the same changeset: every op is now a no-op.
        var result = RunExpectSuccess(VitalUpdateChangesetJson, utcNow: FixedClock.AddDays(1));

        Assert.Contains(result.Log, l => l.Contains("no changes; file untouched"));
        Assert.DoesNotContain(result.Log, l => l.Contains("stamped CHAN"));

        var doc = ReadDoc();
        // Only the CHAN from the first (mutating) run is present; the second,
        // all-no-op run left it exactly as-is (not bumped to the later clock).
        var chan = doc.ByXref["@I00001@"].FirstChild("CHAN")!;
        Assert.Equal("18 JUL 2026", chan.FirstChild("DATE")!.Value);
    }

    [Fact]
    public void DryRun_LeavesDocumentUnmodified_NoChanAnywhere()
    {
        WriteBaseFile();

        var result = Run(VitalUpdateChangesetJson, items: [1], dryRun: true, utcNow: FixedClock);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Null(result.OutputBytes);
        Assert.DoesNotContain(result.Log, l => l.Contains("stamped CHAN"));

        // The base file on disk (unaffected by the dry run) has no CHAN records at all.
        var doc = ReadDoc();
        Assert.DoesNotContain(doc.Records, r => r.FirstChild("CHAN") is not null);
    }
}
