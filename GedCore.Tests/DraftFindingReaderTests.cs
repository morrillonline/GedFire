using System.Text.Json;
using GedCore.Drafting;

namespace GedCore.Tests;

public class DraftFindingReaderTests
{
    const string Valid = """
        { "id": "F-1", "extract": "text",
          "source": { "name": "Find a Grave", "locator": "Memorial #1", "class": "derivative", "information": "primary" },
          "target": { "xref": "@I1@", "card": "enrich-death", "name": "Ann Test" },
          "facts": [ { "record": "@I1@", "fact": "deat", "date": "1 Jan 1900", "quay": 2 } ],
          "notes": [ { "record": "@I1@", "text": "A note." } ] }
        """;

    static (IReadOnlyList<DraftFinding> Findings, IReadOnlyList<string> Errors) Read(string findingsJson) =>
        DraftFindingReader.Read(JsonDocument.Parse(findingsJson).RootElement);

    [Fact]
    public void AValidFinding_IsReadWithTheTagUppercased()
    {
        var (findings, errors) = Read($"[{Valid}]");

        Assert.Empty(errors);
        var finding = Assert.Single(findings);
        Assert.Equal("F-1", finding.Id);
        var fact = Assert.Single(finding.Facts);
        Assert.Equal("DEAT", fact.Tag);
        Assert.Equal("1 Jan 1900", fact.Date);
        Assert.Equal("A note.", Assert.Single(finding.Notes).Text);
    }

    [Fact]
    public void ANonArray_IsRejected() =>
        Assert.Contains("non-empty array", Assert.Single(Read("{}").Errors));

    [Fact]
    public void AnEmptyArray_IsRejected() =>
        Assert.Contains("non-empty array", Assert.Single(Read("[]").Errors));

    [Fact]
    public void AMissingQuay_NamesTheFieldByPath()
    {
        var (findings, errors) = Read($"[{Valid.Replace(", \"quay\": 2", "")}]");

        Assert.Empty(findings);
        Assert.Contains("findings[0].facts[0].quay is required", Assert.Single(errors));
    }

    [Fact]
    public void AnUnrecognizedField_IsRejectedByPath()
    {
        var (_, errors) = Read($"[{Valid.Replace("\"id\": \"F-1\",", "\"id\": \"F-1\", \"wave\": \"W7\",")}]");

        Assert.Contains("findings[0].wave is not a recognized field", Assert.Single(errors));
    }

    [Fact]
    public void ABadSourceClass_ListsTheAcceptedValues()
    {
        var (_, errors) = Read($"[{Valid.Replace("derivative", "hearsay")}]");

        Assert.Contains("findings[0].source.class must be one of original, image copy, derivative, authored, user-contributed", Assert.Single(errors));
    }

    [Fact]
    public void AFactWithNeitherDateNorPlace_IsRejected()
    {
        var (_, errors) = Read($"[{Valid.Replace("\"date\": \"1 Jan 1900\", ", "")}]");

        Assert.Contains("findings[0].facts[0] needs a date or a place", Assert.Single(errors));
    }

    [Fact]
    public void AFindingWithNothingToAssert_IsRejected()
    {
        const string nothing = """
            { "id": "F-1", "extract": "text",
              "source": { "name": "Find a Grave", "locator": "Memorial #1", "class": "derivative", "information": "primary" },
              "target": { "xref": "@I1@" } }
            """;

        var (_, errors) = Read($"[{nothing}]");

        Assert.Contains("findings[0].facts must hold at least one fact", string.Join(";", errors));
    }

    [Fact]
    public void ABadTargetXref_IsRejected()
    {
        var (_, errors) = Read($"[{Valid.Replace("\"xref\": \"@I1@\"", "\"xref\": \"I1\"")}]");

        Assert.Contains("findings[0].target.xref must be an xref", Assert.Single(errors));
    }

    [Fact]
    public void ErrorsAcrossFindings_AreAllReported()
    {
        var (_, errors) = Read($"[{Valid.Replace("\"id\": \"F-1\",", "")}, {Valid.Replace("\"extract\": \"text\",", "")}]");

        Assert.Equal(2, errors.Count);
        Assert.Contains("findings[0].id is required.", errors);
        Assert.Contains("findings[1].extract is required.", errors);
    }
}
