using System.Text.Json;
using GedFire.Gen;
using GedFire.Mcp;

namespace GedCore.Tests;

public class FindingValidatorTests : IDisposable
{
    readonly TempDocumentSessions _sessions = new();

    public void Dispose() => _sessions.Dispose();

    const string Ged = """
        0 HEAD
        1 GEDC
        2 VERS 7.0
        0 @I1@ INDI
        1 NAME Ezra /Quillfeather/
        1 SEX M
        1 BIRT
        2 DATE 1801
        1 DEAT
        2 DATE 1877
        0 @S1@ SOUR
        1 TITL Fictional County Register
        0 TRLR
        """;

    async Task<GedModel> Model() => (await _sessions.Open(Ged).GetSnapshotAsync(CancellationToken.None)).Model;

    static FindingFile Fixture(string name) => FindingFileReader.Read(FindingFixtures.Open(name), name);

    [Fact]
    public async Task AReadyFinding_IsValidWithEveryBindingNamed()
    {
        var result = new FindingValidator(await Model(), null, requireDraft: false).Validate(Fixture("F-100.json"));

        Assert.True(result.Valid, string.Join("; ", result.Errors));
        Assert.True(result.ReadyToDraft);
        Assert.Equal("F-100", result.Id);
        Assert.Equal(GedFire.Schemas.SchemaCatalog.FindingFile.Version, result.SchemaVersion);
        var target = result.Bindings[0];
        Assert.Equal(new FindingBinding("@I1@", "draft.target.xref") { Exists = true, Name = "Ezra Quillfeather", Lifespan = "1801-1877" }, target);
        Assert.Equal("draft.facts[0].record", result.Bindings[1].At);
    }

    [Fact]
    public async Task AnUndeclaredPlaceholder_IsDraftChangesetsErrorAndAnUndeclaredBinding()
    {
        var result = new FindingValidator(await Model(), null, requireDraft: false).Validate(Fixture("F-103.json"));

        Assert.False(result.Valid);
        Assert.False(result.ReadyToDraft);
        Assert.Contains("F-103.json (findings[0]).relations[0] names @NewI_x@, which is not declared in any finding's people.", result.Errors);
        Assert.Contains(new FindingBinding("@NewI_x@", "draft.relations[0].spouse") { Declared = false }, result.Bindings);
        Assert.Contains(new FindingBinding("@NewF1@", "draft.relations[0].family") { Declared = true }, result.Bindings);
    }

    [Fact]
    public async Task ATargetTheDocumentLacks_IsAnErrorAndAnAbsentBinding()
    {
        var result = new FindingValidator(await Model(), null, requireDraft: false).Validate(Fixture("F-104.json"));

        Assert.Contains("F-104.json (findings[0]).target.xref @I99@ is not a record in the document.", result.Errors);
        Assert.Equal(new FindingBinding("@I99@", "draft.target.xref") { Exists = false }, result.Bindings[0]);
        Assert.False(result.ReadyToDraft);
    }

    [Fact]
    public async Task DraftGaps_AreValidButNotReady_UnlessADraftIsRequired()
    {
        var model = await Model();

        var lenient = new FindingValidator(model, null, requireDraft: false).Validate(Fixture("F-102.json"));
        var strict = new FindingValidator(model, null, requireDraft: true).Validate(Fixture("F-102.json"));

        Assert.True(lenient.Valid);
        Assert.False(lenient.ReadyToDraft);
        Assert.Equal(["spouse unbound"], lenient.DraftGaps);
        Assert.False(strict.Valid);
        Assert.StartsWith("F-102.json has draftGaps: spouse unbound", Assert.Single(strict.Errors));
    }

    [Fact]
    public async Task ARecordOnlyFile_IsValidWithNothingToBind()
    {
        var result = new FindingValidator(await Model(), null, requireDraft: false).Validate(Fixture("F-101.json"));

        Assert.True(result.Valid);
        Assert.False(result.ReadyToDraft);
        Assert.Null(result.Id);
        Assert.Empty(result.Bindings);
    }

    [Fact]
    public void WithoutADocument_OnlyTheShapeIsChecked()
    {
        var result = new FindingValidator(null, null, requireDraft: false).Validate(Fixture("F-104.json"));

        Assert.True(result.ReadyToDraft);
        Assert.Equal(new FindingBinding("@I99@", "draft.target.xref"), result.Bindings[0]);
    }

    static FindingFile Variant(string fixture, string from, string to) =>
        FindingFileReader.Read(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(FindingFixtures.Read(fixture).Replace(from, to))), fixture);

    [Theory]
    [InlineData("F-103.json", null, null)]
    [InlineData("F-100.json", "\"class\": \"original\"", "\"class\": \"derivative\"")]
    [InlineData("F-100.json", "\"quay\": 3 } ]", "\"quay\": 3 }, { \"record\": \"@I1@\", \"fact\": \"DEAT\", \"date\": \"1877\", \"quay\": 2 } ]")]
    public async Task WithoutADocument_EveryRuleTheFindingDecidesIsReportedInTheDocumentsWords(string fixture, string? from, string? to)
    {
        var file = from is null ? Fixture(fixture) : Variant(fixture, from, to!);

        var shape = new FindingValidator(null, null, requireDraft: false).Validate(file);
        var bound = new FindingValidator(await Model(), null, requireDraft: false).Validate(file);

        Assert.False(shape.Valid);
        Assert.Equal(bound.Errors, shape.Errors);
    }

    static FindingFile Harvest(string draftBody, bool gaps) => FindingFileReader.Read(new MemoryStream(System.Text.Encoding.UTF8.GetBytes($$"""
        { "draft": { "id": "F-200", "extract": "Ezra Quillfeather, 49; Hannah, 12.",
            "source": { "name": "Fictional Census", "locator": "p. 3", "class": "original", "information": "secondary" },
            "target": { "xref": "@I1@", "card": "new-child" }{{draftBody}} }
          {{(gaps ? ", \"draftGaps\": [\"child unbound: Hannah\"]" : "")}} }
        """)), "F-200.json");

    const string UnlinkedHannah = """, "people": [{ "xref": "@NewI_hannah@", "name": "Hannah /Quillfeather/", "sex": "F" }]""";

    [Theory]
    [InlineData(UnlinkedHannah, "@NewI_hannah@ is declared in people but no relation links it to the tree.")]
    [InlineData("", "F-200.json (findings[0]) has no facts, relations or notes, and no note cites it.")]
    public async Task ACompletenessRule_IsNotReadyWhileGapsAreOpenAndAnErrorOnceTheyClose(string draftBody, string error)
    {
        foreach (var model in new[] { null, await Model() })
        {
            var open = new FindingValidator(model, null, requireDraft: false).Validate(Harvest(draftBody, gaps: true));
            var closed = new FindingValidator(model, null, requireDraft: false).Validate(Harvest(draftBody, gaps: false));

            Assert.True(open.Valid, string.Join("; ", open.Errors));
            Assert.False(open.ReadyToDraft);
            Assert.False(closed.Valid);
            Assert.Contains(error, closed.Errors);
        }
    }

    [Fact]
    public void WithGapsOpen_AQuayAboveItsCeilingIsStillAnError()
    {
        var file = Harvest(UnlinkedHannah + """, "facts": [{ "record": "@I1@", "fact": "BIRT", "date": "1801", "quay": 3 }]""", gaps: true);

        var result = new FindingValidator(null, null, requireDraft: false).Validate(file);

        Assert.False(result.Valid);
        Assert.StartsWith("F-200.json (findings[0]).facts[0].quay is 3, but a original source with secondary information supports at most 2", Assert.Single(result.Errors));
    }

    [Fact]
    public void AFindingWithDraftGaps_IsStillCheckedByTheRules()
    {
        var file = Variant("F-102.json", "\"class\": \"original\"", "\"class\": \"derivative\"");

        var result = new FindingValidator(null, null, requireDraft: false).Validate(file);

        Assert.False(result.Valid);
        Assert.False(result.ReadyToDraft);
        Assert.StartsWith("F-102.json (findings[0]).facts[0].quay is 3, but a derivative source", Assert.Single(result.Errors));
    }

    [Fact]
    public async Task ARecordPersonBoundToAPerson_IsValidAndNamed()
    {
        var file = Variant("F-101.json", "\"name\": \"Ezra Quillfeather\",", "\"name\": \"Ezra Quillfeather\", \"xref\": \"@I1@\",");

        var result = new FindingValidator(await Model(), null, requireDraft: false).Validate(file);

        Assert.True(result.Valid, string.Join("; ", result.Errors));
        Assert.Equal(new FindingBinding("@I1@", "record.persons[0].xref") { Exists = true, Name = "Ezra Quillfeather", Lifespan = "1801-1877" }, Assert.Single(result.Bindings));
    }

    [Fact]
    public async Task ARecordPersonBoundToNobody_IsRefusedAgainstTheTree()
    {
        var file = Variant("F-101.json", "\"name\": \"Ezra Quillfeather\",", "\"name\": \"Ezra Quillfeather\", \"xref\": \"@S1@\",");

        var bound = new FindingValidator(await Model(), null, requireDraft: false).Validate(file);
        var shape = new FindingValidator(null, null, requireDraft: false).Validate(file);

        Assert.Equal("F-101.json: record.persons[0].xref @S1@ is not a person in the document.", Assert.Single(bound.Errors));
        Assert.True(shape.Valid);
    }

    [Fact]
    public async Task AFindingsEntry_IsNamedByItsIndex()
    {
        using var file = JsonDocument.Parse(FindingFixtures.Read("F-104.json"));

        var result = new FindingValidator(await Model(), null, requireDraft: false).Validate(file.RootElement.GetProperty("draft"), 2);

        Assert.Null(result.File);
        Assert.Contains("findings[2].target.xref @I99@ is not a record in the document.", result.Errors);
        Assert.Equal("findings[2].target.xref", result.Bindings[0].At);
    }
}
