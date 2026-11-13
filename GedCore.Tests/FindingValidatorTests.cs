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
        Assert.Equal("1.0", result.SchemaVersion);
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
