using System.Text;
using System.Text.Json;
using GedCore.Apply;
using GedCore.Drafting;
using GedCore.Policy;

namespace GedCore.Tests;

/// <summary>A verification note the compiler wrote earlier is brought up to date when the draft grades one of its facts higher.</summary>
public class ChangesetDrafterStaleNoteTests
{
    sealed class FakeLookup(
        string[]? notes = null, Dictionary<string, ExistingFact[]>? facts = null, string? name = "Gladys Giedd") : IDraftLookup
    {
        public IReadOnlyList<ExistingFact> FactsOn(string record, string tag) => facts?.GetValueOrDefault($"{record}|{tag}") ?? [];
        public IReadOnlyList<string> NotesOn(string record) => record == "@I1@" ? notes ?? [] : [];
        public string? FindSource(string title, string? author) => null;
        public string? PersonName(string xref) => xref == "@I1@" ? name : null;
    }

    const string BirthNote =
        "Needs independent verification: the birth of Gladys Giedd rests on Find a Grave (secondary information) alone and has not yet been confirmed by a second source.";

    const string BirthAndDeathNote =
        "Needs independent verification: the birth and death of Gladys Giedd rest on Find a Grave and Old Obit (primary/secondary information) alone and have not yet been confirmed by a second source.";

    static DraftFinding Finding(string tag, string date, int quay, string id = "F-1", string info = "primary", string? card = "enrich") =>
        new(id, new DraftSource("County Register", null, null, $"p. {id}", null, "original", info), $"extract {id}",
            new DraftTarget("@I1@", card, null),
            [new DraftFact("@I1@", tag, date, null, quay, null, null, null)], []);

    static JsonElement[] Ops(DraftResult result) =>
        [.. JsonDocument.Parse(result.Changeset!.ToJsonString()).RootElement
            .GetProperty("items").EnumerateArray().SelectMany(i => i.GetProperty("ops").EnumerateArray())];

    static IEnumerable<JsonElement> OpsNamed(DraftResult result, string op) => Ops(result).Where(o => o.GetProperty("op").GetString() == op);

    static DraftResult Draft(FakeLookup lookup, params DraftFinding[] findings) => new ChangesetDrafter(lookup).Draft(findings);

    [Fact]
    public void ANoteWhoseOnlyFactIsNowGradedHigher_IsRemoved()
    {
        var result = Draft(new FakeLookup([BirthNote]), Finding("BIRT", "1 JAN 1850", 2));

        var delete = Assert.Single(OpsNamed(result, "deleteNote"));
        Assert.Equal("@I1@", delete.GetProperty("record").GetString());
        Assert.Equal(BirthNote, delete.GetProperty("text").GetString());
        Assert.Empty(OpsNamed(result, "createOrUpdateNote"));
        Assert.DoesNotContain(result.Decisions, d => d.Kind == "open");
        Assert.Contains(result.Decisions, d => d.Kind == "verification-note" && d.Detail.Contains("no longer applies"));
    }

    [Fact]
    public void ANoteNamingTwoFacts_IsRewrittenWithoutTheOneNowGradedHigher()
    {
        var result = Draft(new FakeLookup([BirthAndDeathNote]), Finding("BIRT", "1 JAN 1850", 2));

        var rewrite = Assert.Single(OpsNamed(result, "createOrUpdateNote"));
        Assert.Equal(BirthAndDeathNote, rewrite.GetProperty("match").GetString());
        Assert.Equal(
            "Needs independent verification: the death of Gladys Giedd rests on Find a Grave and Old Obit (primary/secondary information) alone and has not yet been confirmed by a second source.",
            rewrite.GetProperty("text").GetString());
        Assert.Empty(OpsNamed(result, "deleteNote"));
        Assert.DoesNotContain(result.Decisions, d => d.Kind == "open");
    }

    [Fact]
    public void AFactTheRecordAlreadyGradesHigher_AlsoMakesTheNoteStale()
    {
        var held = new Dictionary<string, ExistingFact[]>
        {
            ["@I1@|BIRT"] = [new ExistingFact("1 JAN 1850", null, [new ExistingCitation("@S1@", "p. 1", null, 2)])],
        };

        var result = Draft(new FakeLookup([BirthNote], held), Finding("DEAT", "1 JAN 1900", 2));

        Assert.Single(OpsNamed(result, "deleteNote"));
    }

    [Fact]
    public void ANoteWhoseFactsAreStillLow_IsKeptAndNoWarningIsRaised()
    {
        var held = new Dictionary<string, ExistingFact[]>
        {
            ["@I1@|BIRT"] = [new ExistingFact("1 JAN 1850", null, [new ExistingCitation("@S1@", "p. 1", null, 1)])],
        };

        var result = Draft(new FakeLookup([BirthNote], held), Finding("DEAT", "1 JAN 1900", 1, info: "secondary"));

        Assert.Empty(OpsNamed(result, "deleteNote"));
        var added = Assert.Single(OpsNamed(result, "createOrUpdateNote"));
        Assert.False(added.TryGetProperty("match", out _));
        Assert.Contains("the death of Gladys Giedd", added.GetProperty("text").GetString());
        Assert.DoesNotContain(result.Decisions, d => d.Kind == "open");
        Assert.Contains(result.Decisions, d => d.Kind == "verification-note" && d.Detail.Contains("keeps its verification note"));
    }

    [Fact]
    public void ANoteThatIsNotTheCompilersOwn_IsStillLeftToTheAuthor()
    {
        var result = Draft(
            new FakeLookup(["Needs independent verification: the parents are unproven."]),
            Finding("BIRT", "1 JAN 1850", 1, info: "secondary"));

        Assert.Single(result.Decisions, d => d.Kind == "open" && d.Detail.Contains("already has a verification note"));
        Assert.Empty(OpsNamed(result, "deleteNote"));
    }

    [Fact]
    public void ANoteForAnotherNamesOwner_IsNotJudged()
    {
        var result = Draft(
            new FakeLookup([BirthNote.Replace("Gladys Giedd", "Someone Else")]),
            Finding("DEAT", "1 JAN 1900", 1, info: "secondary"));

        Assert.Single(result.Decisions, d => d.Kind == "open");
        Assert.Empty(OpsNamed(result, "deleteNote"));
    }

    [Fact]
    public void ANoteTheAuthorRewritesThemselves_IsNotTouched()
    {
        var finding = Finding("BIRT", "1 JAN 1850", 2) with
        {
            Notes = [new DraftNote("@I1@", "Needs independent verification: the birth is now documented.") { Match = BirthNote }],
        };

        var result = Draft(new FakeLookup([BirthNote]), finding);

        Assert.Empty(OpsNamed(result, "deleteNote"));
        Assert.Single(OpsNamed(result, "createOrUpdateNote"));
    }

    [Fact]
    public void ANoteIsLeftAloneWhenTheDraftSaysNothingAboutItsFacts()
    {
        var result = Draft(new FakeLookup([BirthNote]), Finding("DEAT", "1 JAN 1900", 2));

        Assert.Empty(OpsNamed(result, "deleteNote"));
        Assert.Empty(OpsNamed(result, "createOrUpdateNote"));
        Assert.DoesNotContain(result.Decisions, d => d.Kind is "open" or "verification-note");
    }

    [Fact]
    public void ANoteWithNoUnknownFactLabels_IsNotJudged()
    {
        string odd = BirthNote.Replace("the birth of", "the horoscope of");

        var result = Draft(new FakeLookup([odd]), Finding("BIRT", "1 JAN 1850", 1, info: "secondary"));

        Assert.Single(result.Decisions, d => d.Kind == "open");
        Assert.Empty(OpsNamed(result, "deleteNote"));
    }

    // ---- accepted by the applier and the policy ----

    static string Document(string note) => $"""
        0 HEAD
        1 GEDC
        2 VERS 7.0
        0 @I1@ INDI
        1 NAME Gladys /Giedd/
        1 SEX F
        1 NOTE {note}
        0 TRLR
        """;

    static ApplyResult DryRun(DraftResult result, string note) =>
        ChangesetApplier.Run(Encoding.UTF8.GetBytes(Document(note)), Changeset.Parse(result.Changeset!.ToJsonString()), [1], dryRun: true);

    [Fact]
    public void ARemovedNote_IsAcceptedByTheApplier()
    {
        var result = Draft(new FakeLookup([BirthNote]), Finding("BIRT", "1 JAN 1850", 2));

        var applied = DryRun(result, BirthNote);

        Assert.True(applied.Success, string.Join("; ", applied.Errors));
    }

    [Fact]
    public void ARewrittenNote_IsAcceptedByTheApplier()
    {
        var result = Draft(new FakeLookup([BirthAndDeathNote]), Finding("BIRT", "1 JAN 1850", 2));

        var applied = DryRun(result, BirthAndDeathNote);

        Assert.True(applied.Success, string.Join("; ", applied.Errors));
    }

    [Fact]
    public void TheVerificationPolicy_AcceptsTheUpToDateNotes()
    {
        var policy = ChangesetPolicy.Load(new MemoryStream(Encoding.UTF8.GetBytes(
            """{ "verificationNote": { "maxQuay": 1, "notePrefix": "needs independent verification:" } }""")));
        var held = new Dictionary<string, ExistingFact[]>
        {
            ["@I1@|BIRT"] = [new ExistingFact("1 JAN 1850", null, [new ExistingCitation("@S1@", "p. 1", null, 1)])],
        };

        var result = new ChangesetDrafter(new FakeLookup([BirthAndDeathNote], held), policy).Draft(
            [Finding("BIRT", "1 JAN 1850", 2), Finding("BURI", "5 JAN 1900", 1, "F-2", "secondary")]);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Single(OpsNamed(result, "createOrUpdateNote"), o => o.TryGetProperty("match", out _));
        Assert.Single(OpsNamed(result, "createOrUpdateNote"), o => !o.TryGetProperty("match", out _));
    }
}
