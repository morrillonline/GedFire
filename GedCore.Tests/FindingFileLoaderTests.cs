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

    static string Finding(string tail = "") => "{ " + Draft + tail + " }";

    [Fact]
    public void AnArgumentBesideTheDraft_IsCarriedOnTheFinding()
    {
        var loaded = FindingFileLoader.Read(Utf8("{ \"argument\": { \"anchors\": [\"Born in Salem.\"], \"difficulty\": \"high\" }, " + Draft + " }"), "F-1.json");

        Assert.True(loaded.Finding is not null, string.Join("; ", loaded.Errors));
        Assert.Equal("""{"anchors":["Born in Salem."],"difficulty":"high"}""", loaded.Finding!.Argument!.ToJsonString());
    }

    [Fact]
    public void ACompleteFinding_YieldsItsDraftEntry()
    {
        bool ok = FindingFileLoader.TryRead(Utf8(Finding(", \"draftGaps\": []")), "F-1.json", out var finding, out var errors);

        Assert.True(ok, string.Join("; ", errors));
        Assert.Equal("enrich-death", finding!.Target.Card);
        Assert.Equal("DEAT", Assert.Single(finding.Facts).Tag);
    }

    [Fact]
    public void ANonEmptyDraftGaps_IsAnErrorForDraftingWithEachGap()
    {
        bool ok = FindingFileLoader.TryRead(Utf8(Finding(", \"draftGaps\": [\"needsSex: Mary\", \"unplacedKin: John\"]")), "F-1.json", out _, out var errors);

        Assert.False(ok);
        Assert.StartsWith("F-1.json has draftGaps: needsSex: Mary; unplacedKin: John", Assert.Single(errors));
    }

    [Fact]
    public void AFileWithNoDraft_IsAnErrorForDrafting()
    {
        bool ok = FindingFileLoader.TryRead(Utf8("""{ "meta": {} }"""), "F-1.json", out _, out var errors);

        Assert.False(ok);
        Assert.Equal("F-1.json has no draft; correlate the finding to write one.", Assert.Single(errors));
    }

    [Fact]
    public void AFileWithAnUnknownKey_IsNotDrafted()
    {
        bool ok = FindingFileLoader.TryRead(Utf8(Finding(", \"id\": \"F-1\"")), "F-1.json", out _, out var errors);

        Assert.False(ok);
        Assert.Contains("\"id\" is not a finding-file key", Assert.Single(errors));
    }

    [Fact]
    public void ADraftTheStrictReaderRejects_IsReportedWithTheFileAndFieldPath()
    {
        bool ok = FindingFileLoader.TryRead(Utf8(Finding().Replace("\"quay\": 2", "\"quay\": 9")), "F-1.json", out _, out var errors);

        Assert.False(ok);
        Assert.StartsWith("F-1.json: draft.facts[0].quay", Assert.Single(errors));
    }

    [Fact]
    public void DraftChecksAreRewrittenToNameTheFile()
    {
        var named = FindingFileLoader.NameFiles(["findings[1] has no facts", "findings[7] odd", "other"], ["a.json", "b.json"]);

        Assert.Equal(["b.json (findings[1]) has no facts", "findings[7] odd", "other"], named);
    }
}
