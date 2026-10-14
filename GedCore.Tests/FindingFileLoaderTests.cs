using System.Text;
using GedFire.Mcp;

namespace GedCore.Tests;

public class FindingFileLoaderTests
{
    static MemoryStream Utf8(string json) => new(Encoding.UTF8.GetBytes(json));

    const string Draft = """
        "draft": { "id": "F-1", "extract": "Died 3 Jan 1850.",
          "source": { "name": "Obituary", "locator": "p. 1", "class": "derivative", "information": "primary" },
          "target": { "xref": "@I1@", "card": "enrich-death" },
          "facts": [ { "record": "@I1@", "fact": "DEAT", "date": "3 JAN 1850", "quay": 2 } ],
          "people": [], "relations": [], "notes": [] }
        """;

    static string Finding(string tail = "") => "{ \"id\": \"F-1\", " + Draft + tail + " }";

    [Fact]
    public void TheAnchorComesFromTheCorrelationTarget()
    {
        var loaded = FindingFileLoader.Read(Utf8("{ \"correlation\": { \"kind\": \"add\", \"target\": \"@I7@\" }, " + Draft + " }"), "f.json");

        Assert.Equal("@I7@", loaded.Anchor);
        Assert.True(loaded.Finding is not null, string.Join("; ", loaded.Errors));
    }

    [Fact]
    public void WithoutACorrelationTarget_AReadyFindingsAnchorIsItsDraftTarget()
    {
        Assert.Equal("@I1@", FindingFileLoader.Read(Utf8(Finding()), "f.json").Anchor);
    }

    [Fact]
    public void AnUnreadyFileKeepsItsAnchorSoItsItemCanReportIt()
    {
        string unready = """{ "id": "F-1", "correlation": { "kind": "enrich", "target": "@I7@" } }""";

        var loaded = FindingFileLoader.Read(Utf8(unready), "f.json");

        Assert.Null(loaded.Finding);
        Assert.Equal("@I7@", loaded.Anchor);
    }

    [Fact]
    public void AFileThatIsNotJson_HasNoAnchor()
    {
        Assert.Null(FindingFileLoader.Read(Utf8("{ nope"), "f.json").Anchor);
    }

    [Fact]
    public void DraftChecksAreRewrittenToNameTheFile()
    {
        var named = FindingFileLoader.NameFiles(["findings[1] has no facts", "findings[7] odd", "other"], ["a.json", "b.json"]);

        Assert.Equal(["b.json (findings[1]) has no facts", "findings[7] odd", "other"], named);
    }

    [Fact]
    public void ACompleteFinding_YieldsItsDraftEntry()
    {
        bool ok = FindingFileLoader.TryRead(Utf8(Finding(", \"draftGaps\": []")), "f.json", out var finding, out var errors);

        Assert.True(ok, string.Join("; ", errors));
        Assert.Equal("F-1", finding!.Id);
        Assert.Equal("@I1@", finding.Target.Xref);
        Assert.Equal("enrich-death", finding.Target.Card);
        Assert.Equal("DEAT", Assert.Single(finding.Facts).Tag);
    }

    [Fact]
    public void ANonEmptyDraftGaps_IsReportedByFileNameWithEachGap()
    {
        bool ok = FindingFileLoader.TryRead(
            Utf8(Finding(", \"draftGaps\": [\"needsSex: Mary\", \"unplacedKin: John\"]")), "W67-x.json", out _, out var errors);

        Assert.False(ok);
        var error = Assert.Single(errors);
        Assert.Contains("W67-x.json has draftGaps: needsSex: Mary; unplacedKin: John", error);
        Assert.Contains("--force", error);
    }

    [Fact]
    public void AFindingWithNoDraft_IsReportedByFileName()
    {
        bool ok = FindingFileLoader.TryRead(Utf8("""{ "id": "F-1" }"""), "f.json", out _, out var errors);

        Assert.False(ok);
        Assert.Equal("f.json has no draft; correlate the finding to write one.", Assert.Single(errors));
    }

    [Fact]
    public void AParkedFinding_SaysTheVerdictWritesNoDraft()
    {
        bool ok = FindingFileLoader.TryRead(Utf8("""{ "id": "F-1", "correlation": { "kind": "park" } }"""), "f.json", out _, out var errors);

        Assert.False(ok);
        Assert.Equal("f.json is parked; a park verdict writes no draft.", Assert.Single(errors));
    }

    [Fact]
    public void ADraftTheStrictReaderRejects_IsReportedWithTheFileAndFieldPath()
    {
        bool ok = FindingFileLoader.TryRead(Utf8(Finding().Replace("\"quay\": 2", "\"quay\": 9")), "f.json", out _, out var errors);

        Assert.False(ok);
        Assert.StartsWith("f.json: draft.facts[0].quay", Assert.Single(errors));
    }

    [Fact]
    public void ADraftWithAnUnrecognizedField_IsRejectedNamingIt()
    {
        bool ok = FindingFileLoader.TryRead(Utf8(Finding().Replace("\"people\": []", "\"people\": [], \"flags\": []")), "f.json", out _, out var errors);

        Assert.False(ok);
        Assert.Contains("f.json: draft.flags is not a recognized field", Assert.Single(errors));
    }

    [Theory]
    [InlineData("[1]")]
    [InlineData("""{ "id": "F-1", "draft": [] }""")]
    public void AFileThatIsNotAFindingObject_IsRefused(string json)
    {
        Assert.False(FindingFileLoader.TryRead(Utf8(json), "f.json", out _, out var errors));
        Assert.StartsWith("f.json", Assert.Single(errors));
    }

    [Fact]
    public void AFileThatIsNotJson_IsRefused()
    {
        Assert.False(FindingFileLoader.TryRead(Utf8("{ nope"), "f.json", out _, out var errors));
        Assert.Contains("f.json is not valid JSON", Assert.Single(errors));
    }
}
