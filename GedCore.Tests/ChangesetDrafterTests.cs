using System.Text;
using System.Text.Json;
using GedCore.Drafting;
using GedCore.Policy;

namespace GedCore.Tests;

public class ChangesetDrafterTests
{
    sealed class FakeLookup(
        Dictionary<string, string>? sources = null, Dictionary<string, string>? names = null,
        Dictionary<string, ExistingFact[]>? facts = null, Dictionary<string, string[]>? notes = null) : IDraftLookup
    {
        public IReadOnlyList<ExistingFact> FactsOn(string record, string tag) =>
            facts?.GetValueOrDefault($"{record}|{tag}") ?? [];
        public IReadOnlyList<string> NotesOn(string record) => notes?.GetValueOrDefault(record) ?? [];
        public string? FindSource(string title, string? author) => sources?.GetValueOrDefault(title);
        public string? PersonName(string xref) => names?.GetValueOrDefault(xref);
    }

    static DraftSource Memorial(string locator = "Memorial #1", string? url = null, string cls = "derivative", string info = "primary") =>
        new("Find a Grave", null, url, locator, null, cls, info);

    static DraftFact Fact(string tag, string? date, string? place, int quay, string record = "@I1@", string? excerpt = null) =>
        new(record, tag, date, place, quay, null, null, excerpt);

    static DraftFinding Finding(
        string id, DraftSource source, string extract, IReadOnlyList<DraftFact> facts,
        string xref = "@I1@", string? card = "enrich-death", string? name = null, IReadOnlyList<DraftNote>? notes = null) =>
        new(id, source, extract, new DraftTarget(xref, card, name), facts, notes ?? []);

    static JsonElement Json(DraftResult result) => JsonDocument.Parse(result.Changeset!.ToJsonString()).RootElement;

    static JsonElement[] Ops(DraftResult result) =>
        [.. Json(result).GetProperty("items").EnumerateArray().SelectMany(i => i.GetProperty("ops").EnumerateArray())];

    // ---- sources ----

    [Fact]
    public void ACollectionNotInTheDocument_IsMintedOnceForEveryFinding()
    {
        var findings = new[]
        {
            Finding("F-1", Memorial("Memorial #1"), "extract one", [Fact("DEAT", "1 JAN 1900", "Salem", 2)], xref: "@I1@"),
            Finding("F-2", Memorial("Memorial #2"), "extract two", [Fact("BURI", null, "Salem Cemetery", 2, "@I2@")], xref: "@I2@"),
        };

        var result = new ChangesetDrafter(new FakeLookup()).Draft(findings);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        var groups = Json(result).GetProperty("newSources");
        Assert.Equal(1, groups.GetArrayLength());
        Assert.Equal("@NewSource1@", groups[0].GetProperty("xref").GetString());
        Assert.All(Ops(result).Where(o => o.GetProperty("op").GetString() == "createOrUpdateVital"),
            o => Assert.Equal("@NewSource1@", o.GetProperty("citation").GetProperty("source").GetString()));
        Assert.Single(result.Decisions, d => d.Kind == "minted-source");
    }

    [Fact]
    public void AnExistingSource_IsCitedAndNothingIsMinted()
    {
        var lookup = new FakeLookup(sources: new() { ["Find a Grave"] = "@S00007@" });

        var result = new ChangesetDrafter(lookup).Draft(
            [Finding("F-1", Memorial(), "x", [Fact("DEAT", "1 JAN 1900", "Salem", 2)])]);

        Assert.False(Json(result).TryGetProperty("newSources", out _));
        Assert.Equal("@S00007@", Ops(result)[0].GetProperty("citation").GetProperty("source").GetString());
        Assert.Single(result.Decisions, d => d.Kind == "reused-source" && d.Detail.Contains("@S00007@"));
    }

    static DraftSource Collection(string name, string locator = "Memorial #1") =>
        new(name, null, null, locator, null, "derivative", "primary");

    [Fact]
    public void ATitleWithAPersonNameSuffix_CitesTheExistingCollection()
    {
        var lookup = new FakeLookup(sources: new() { ["Find a Grave"] = "@S00007@" });

        var result = new ChangesetDrafter(lookup).Draft(
            [Finding("F-1", Collection("Find a Grave - Harry Morrill"), "x", [Fact("DEAT", "1 JAN 1900", "Salem", 2)])]);

        Assert.False(Json(result).TryGetProperty("newSources", out _));
        Assert.Equal("@S00007@", Ops(result)[0].GetProperty("citation").GetProperty("source").GetString());
        Assert.Equal("title \"Find a Grave - Harry Morrill\" → reusing @S00007@", Assert.Single(result.Decisions, d => d.Kind == "reused-source").Detail);
    }

    [Fact]
    public void AnExactTitle_IsPreferredToTheBareCollectionItReducesTo()
    {
        var lookup = new FakeLookup(sources: new() { ["Ancestry - Family Trees"] = "@S1@", ["Ancestry"] = "@S2@" });

        var result = new ChangesetDrafter(lookup).Draft(
            [Finding("F-1", Collection("Ancestry - Family Trees"), "x", [Fact("DEAT", "1 JAN 1900", "Salem", 2)])]);

        Assert.Equal("@S1@", Ops(result)[0].GetProperty("citation").GetProperty("source").GetString());
    }

    [Fact]
    public void FindingsDifferingOnlyInTheNameSuffix_MintOneSourceWithTheBareTitle()
    {
        var result = new ChangesetDrafter(new FakeLookup()).Draft([
            Finding("F-1", Collection("Find a Grave Index - Harry Morrill"), "x", [Fact("DEAT", "1900", null, 2)]),
            Finding("F-2", Collection("Find a Grave Index - Ann Blake", "Memorial #2"), "y", [Fact("BURI", null, "Salem", 2, "@I2@")], xref: "@I2@")]);

        var groups = Json(result).GetProperty("newSources");
        Assert.Equal(1, groups.GetArrayLength());
        Assert.Equal("Find a Grave Index", groups[0].GetProperty("ops")[0].GetProperty("title").GetString());
        Assert.All(Ops(result), o => Assert.Equal("@NewSource1@", o.GetProperty("citation").GetProperty("source").GetString()));
        Assert.Equal(2, result.Decisions.Count(d => d.Kind == "minted-source" && d.Detail.Contains("→ minting new source @NewSource1@")));
    }

    [Fact]
    public void DistinctCollections_StillMintSeparately()
    {
        var result = new ChangesetDrafter(new FakeLookup()).Draft([
            Finding("F-1", Collection("U.S., Social Security Death Index, 1935-2014 - Harry Morrill"), "x", [Fact("DEAT", "1900", null, 2)]),
            Finding("F-2", Collection("U.S., Social Security Death Index, 1935-2013 - Ann Blake"), "y", [Fact("BURI", null, "Salem", 2, "@I2@")], xref: "@I2@"),
            Finding("F-3", Collection("1950 United States Federal Census"), "z", [Fact("RESI", null, "Salem", 2, "@I3@")], xref: "@I3@")]);

        Assert.Equal(3, Json(result).GetProperty("newSources").GetArrayLength());
    }

    [Fact]
    public void TheSourceUrl_IsWrittenOnlyWhenEveryFindingAgrees()
    {
        var agreeing = new ChangesetDrafter(new FakeLookup()).Draft([
            Finding("F-1", Memorial(url: "https://example.org/fg"), "x", [Fact("DEAT", "1900", null, 2)]),
            Finding("F-2", Memorial(url: "https://example.org/fg"), "y", [Fact("BURI", null, "Salem", 2, "@I2@")], xref: "@I2@")]);
        var differing = new ChangesetDrafter(new FakeLookup()).Draft([
            Finding("F-1", Memorial(url: "https://example.org/1"), "x", [Fact("DEAT", "1900", null, 2)]),
            Finding("F-2", Memorial(url: "https://example.org/2"), "y", [Fact("BURI", null, "Salem", 2, "@I2@")], xref: "@I2@")]);

        string Source(DraftResult r) => Json(r).GetProperty("newSources")[0].GetProperty("ops")[0].GetRawText();
        Assert.Contains("https://example.org/fg", Source(agreeing));
        Assert.DoesNotContain("https://example.org", Source(differing));
        Assert.Contains(differing.Decisions, d => d.Kind == "open" && d.Detail.Contains("url"));
    }

    // ---- evidence carried by the changeset ----

    [Fact]
    public void TheChangeset_CarriesATableOfItsFindingsWithTheirEvidence()
    {
        var finding = Finding("F-1", Memorial("Memorial #1", url: "https://example.org/fg"), "Died 1 Jan 1900.", [Fact("DEAT", "1 JAN 1900", "Salem", 2)]);

        var result = new ChangesetDrafter(new FakeLookup()).Draft([finding]);

        var entry = Assert.Single(Json(result).GetProperty("findings").EnumerateArray());
        Assert.Equal("F-1", entry.GetProperty("id").GetString());
        Assert.Equal(1, entry.GetProperty("item").GetInt32());
        Assert.Equal("Died 1 Jan 1900.", entry.GetProperty("extract").GetString());
        var source = entry.GetProperty("source");
        Assert.Equal("@NewSource1@", source.GetProperty("ref").GetString());
        Assert.Equal(new[] { "Find a Grave", "Memorial #1", "https://example.org/fg", "derivative", "primary" },
            new[] { "name", "locator", "url", "class", "information" }.Select(p => source.GetProperty(p).GetString()));
        var claim = Assert.Single(entry.GetProperty("claims").EnumerateArray());
        Assert.Equal(("@I1@", "DEAT", "1 JAN 1900", "Salem", 2),
            (claim.GetProperty("record").GetString(), claim.GetProperty("fact").GetString(), claim.GetProperty("date").GetString(),
             claim.GetProperty("place").GetString(), claim.GetProperty("quay").GetInt32()));
    }

    [Fact]
    public void FindingsOfDifferentTargets_AreTabledUnderTheirOwnItems()
    {
        var result = new ChangesetDrafter(new FakeLookup()).Draft([
            Finding("F-1", Memorial("Memorial #1"), "x", [Fact("DEAT", "1900", null, 2)]),
            Finding("F-2", Memorial("Memorial #2"), "y", [Fact("BURI", null, "Salem", 2, "@I2@")], xref: "@I2@")]);

        Assert.Equal([("F-1", 1), ("F-2", 2)],
            Json(result).GetProperty("findings").EnumerateArray().Select(f => (f.GetProperty("id").GetString()!, f.GetProperty("item").GetInt32())));
    }

    [Fact]
    public void EachOp_NamesTheFindingsItRestsOn()
    {
        var result = new ChangesetDrafter(new FakeLookup()).Draft([
            Finding("F-1", Memorial("Memorial #1"), "x", [Fact("DEAT", "1 JAN 1900", "Salem", 2)]),
            Finding("F-2", Memorial("Memorial #2"), "y", [Fact("DEAT", "1 JAN 1900", "Salem", 2), Fact("BURI", null, "Salem Cemetery", 2)])]);

        var byFact = Ops(result).Where(o => o.GetProperty("op").GetString() == "createOrUpdateVital")
            .ToDictionary(o => o.GetProperty("fact").GetString()!, o => o.GetProperty("findings").EnumerateArray().Select(f => f.GetString()));
        Assert.Equal(["F-1", "F-2"], byFact["DEAT"]);
        Assert.Equal(["F-2"], byFact["BURI"]);
    }

    [Fact]
    public void AnArgument_IsCarriedIntoTheFindingsTableAndTheOpsAreUntouched()
    {
        var plain = Finding("F-1", Memorial(), "x", [Fact("DEAT", "1 JAN 1900", "Salem", 2)]);
        var argument = System.Text.Json.Nodes.JsonNode.Parse("""{ "anchors": ["Born in Salem."], "identity": "Surveyed elsewhere.", "difficulty": "high" }""")!.AsObject();

        var without = new ChangesetDrafter(new FakeLookup()).Draft([plain]);
        var with = new ChangesetDrafter(new FakeLookup()).Draft([plain with { Argument = argument }]);

        Assert.Equal(without.Changeset!["items"]!.ToJsonString(), with.Changeset!["items"]!.ToJsonString());
        Assert.Equal(without.Changeset["newSources"]!.ToJsonString(), with.Changeset["newSources"]!.ToJsonString());
        Assert.Equal(without.Decisions.Select(d => (d.Kind, d.Detail)), with.Decisions.Select(d => (d.Kind, d.Detail)));
        Assert.Equal(argument.ToJsonString(), Json(with).GetProperty("findings")[0].GetProperty("argument").GetRawText());
        Assert.False(Json(without).GetProperty("findings")[0].TryGetProperty("argument", out _));
    }

    // ---- citations ----

    [Fact]
    public void TheLocator_IsCopiedVerbatimIntoThePage()
    {
        string locator = "Memorial #108658590, Knox Lutheran Cemetery, Knox, Benson County, North Dakota (created by contributor 'High Plains')";

        var result = new ChangesetDrafter(new FakeLookup()).Draft(
            [Finding("F-1", Memorial(locator), "x", [Fact("DEAT", "26 MAR 2013", null, 2)])]);

        Assert.Equal(locator, Ops(result)[0].GetProperty("citation").GetProperty("page").GetString());
    }

    [Fact]
    public void FactsCitingTheSamePageAndGrade_ShareOneMergedDataText()
    {
        var result = new ChangesetDrafter(new FakeLookup()).Draft([
            Finding("F-1", Memorial(), "whole extract", [
                Fact("DEAT", "1 JAN 1900", null, 2, excerpt: "DEATH 1 Jan 1900"),
                Fact("BURI", null, "Salem Cemetery", 2, excerpt: "BURIAL Salem Cemetery")])]);

        var citations = Ops(result).Select(o => o.GetProperty("citation").GetProperty("dataText").GetString()).ToList();
        Assert.Equal(2, citations.Count);
        Assert.All(citations, t => Assert.Equal("DEATH 1 Jan 1900 … BURIAL Salem Cemetery", t));
        Assert.Single(result.Decisions, d => d.Kind == "merged-citation");
    }

    [Fact]
    public void FactsAtDifferentGrades_KeepSeparateCitations()
    {
        var result = new ChangesetDrafter(new FakeLookup()).Draft([
            Finding("F-1", Memorial(), "whole extract", [
                Fact("DEAT", "1 JAN 1900", null, 2, excerpt: "DEATH 1 Jan 1900"),
                Fact("BIRT", "1 JAN 1850", null, 1, excerpt: "BIRTH 1 Jan 1850")])]);

        var vitals = Ops(result).Where(o => o.GetProperty("op").GetString() == "createOrUpdateVital").ToList();
        Assert.Equal(["DEATH 1 Jan 1900", "BIRTH 1 Jan 1850"],
            vitals.Select(v => v.GetProperty("citation").GetProperty("dataText").GetString()));
    }

    [Fact]
    public void DatesAreWrittenInUppercaseGedcomForm()
    {
        var result = new ChangesetDrafter(new FakeLookup()).Draft(
            [Finding("F-1", Memorial(), "x", [Fact("DEAT", "26 Mar 2013", null, 2)])]);

        Assert.Equal("26 MAR 2013", Ops(result)[0].GetProperty("value").GetProperty("date").GetString());
    }

    [Fact]
    public void TheSameFactFromTwoFindings_BecomesOneOpWithBothCitations()
    {
        var result = new ChangesetDrafter(new FakeLookup()).Draft([
            Finding("F-1", Memorial("Memorial #1"), "x", [Fact("DEAT", "1 JAN 1900", "Salem", 2)]),
            Finding("F-2", new DraftSource("County Register", null, null, "p. 4", null, "original", "primary"), "y",
                [Fact("DEAT", "1 JAN 1900", "Salem", 3)])]);

        var vital = Assert.Single(Ops(result), o => o.GetProperty("op").GetString() == "createOrUpdateVital");
        Assert.Equal(2, vital.GetProperty("citations").GetArrayLength());
    }

    [Fact]
    public void ConflictingValuesForOneFact_AreLeftOpenNotGuessed()
    {
        var result = new ChangesetDrafter(new FakeLookup()).Draft([
            Finding("F-1", Memorial("Memorial #1"), "x", [Fact("DEAT", "1 JAN 1900", null, 2)]),
            Finding("F-2", Memorial("Memorial #2"), "y", [Fact("DEAT", "2 JAN 1900", null, 2)])]);

        Assert.DoesNotContain(Ops(result), o => o.GetProperty("op").GetString() == "createOrUpdateVital");
        Assert.Single(result.Decisions, d => d.Kind == "open" && d.Detail.Contains("2 different values"));
    }

    // ---- verification notes ----

    [Fact]
    public void ALowGradedFact_GetsAVerificationNoteNamingIt()
    {
        var lookup = new FakeLookup(names: new() { ["@I1@"] = "Gladys Giedd" });

        var result = new ChangesetDrafter(lookup).Draft(
            [Finding("F-1", Memorial(info: "secondary"), "x", [Fact("BIRT", "1924", null, 1)])]);

        var note = Assert.Single(Ops(result), o => o.GetProperty("op").GetString() == "createOrUpdateNote");
        Assert.Equal("@I1@", note.GetProperty("record").GetString());
        Assert.Equal(
            "Needs independent verification: the birth of Gladys Giedd rests on Find a Grave (secondary information) alone and has not yet been confirmed by a second source.",
            note.GetProperty("text").GetString());
    }

    [Fact]
    public void AHighGradedFact_GetsNoNote()
    {
        var result = new ChangesetDrafter(new FakeLookup()).Draft(
            [Finding("F-1", Memorial(), "x", [Fact("DEAT", "1900", null, 2)])]);

        Assert.DoesNotContain(Ops(result), o => o.GetProperty("op").GetString() == "createOrUpdateNote");
    }

    [Fact]
    public void AnAuthorsOwnVerificationNote_IsNotDuplicated()
    {
        var notes = new[] { new DraftNote("@I1@", "Needs independent verification: the birth is from one index.") };

        var result = new ChangesetDrafter(new FakeLookup()).Draft(
            [Finding("F-1", Memorial(info: "secondary"), "x", [Fact("BIRT", "1924", null, 1)], notes: notes)]);

        Assert.Single(Ops(result), o => o.GetProperty("op").GetString() == "createOrUpdateNote");
    }

    [Fact]
    public void ThePoliciesCeilingAndPrefix_GovernTheNote()
    {
        var policy = ChangesetPolicy.Load(new MemoryStream(Encoding.UTF8.GetBytes(
            """{ "verificationNote": { "maxQuay": 2, "notePrefix": "unconfirmed:" } }""")));

        var result = new ChangesetDrafter(new FakeLookup(), policy).Draft(
            [Finding("F-1", Memorial(), "x", [Fact("DEAT", "1900", null, 2)])]);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        var note = Assert.Single(Ops(result), o => o.GetProperty("op").GetString() == "createOrUpdateNote");
        Assert.StartsWith("Unconfirmed: the death", note.GetProperty("text").GetString());
    }

    // ---- refusals ----

    [Fact]
    public void AQuayAboveWhatTheEvidenceSupports_IsRefusedNamingTheField()
    {
        var result = new ChangesetDrafter(new FakeLookup()).Draft(
            [Finding("F-1", Memorial(info: "undetermined"), "x", [Fact("BIRT", "1924", null, 3)])]);

        Assert.False(result.Success);
        Assert.Null(result.Changeset);
        Assert.Contains("findings[0].facts[0].quay is 3", Assert.Single(result.Errors));
    }

    [Theory]
    [InlineData("original", "primary", 3, true)]
    [InlineData("original", "secondary", 3, false)]
    [InlineData("image copy", "undetermined", 2, true)]
    [InlineData("derivative", "primary", 2, true)]
    [InlineData("derivative", "primary", 3, false)]
    [InlineData("derivative", "secondary", 2, false)]
    [InlineData("authored", "primary", 1, true)]
    [InlineData("authored", "primary", 2, false)]
    [InlineData("user-contributed", "primary", 0, true)]
    [InlineData("user-contributed", "primary", 1, false)]
    public void TheQuayCeiling_FollowsSourceAndInformationClass(string cls, string info, int quay, bool accepted)
    {
        var result = new ChangesetDrafter(new FakeLookup()).Draft(
            [Finding("F-1", Memorial(cls: cls, info: info), "x", [Fact("BIRT", "1924", null, quay)])]);

        Assert.Equal(accepted, result.Success);
    }

    [Fact]
    public void AFactLevelClassOverride_RaisesTheCeilingForThatFactOnly()
    {
        var overridden = Fact("DEAT", "1900", null, 2) with { Information = "primary" };

        var result = new ChangesetDrafter(new FakeLookup()).Draft(
            [Finding("F-1", Memorial(info: "secondary"), "x", [overridden, Fact("BIRT", "1850", null, 2)])]);

        Assert.Equal(["findings[0].facts[1].quay is 2, but a derivative source with secondary information supports at most 1."], result.Errors);
    }

    [Fact]
    public void ATargetNameThatDiffersFromTheRecordedName_IsRefused()
    {
        var lookup = new FakeLookup(names: new() { ["@I1@"] = "Albert Augustus Beattie" });

        var result = new ChangesetDrafter(lookup).Draft(
            [Finding("F-1", Memorial(), "x", [Fact("DEAT", "1900", null, 2)], name: "Albert Agustus Beattie")]);

        Assert.Contains("findings[0].target.name", Assert.Single(result.Errors));
    }

    [Fact]
    public void ATargetNameThatMatchesIgnoringSlashesAndCase_IsAccepted()
    {
        var lookup = new FakeLookup(names: new() { ["@I1@"] = "Albert Beattie" });

        var result = new ChangesetDrafter(lookup).Draft(
            [Finding("F-1", Memorial(), "x", [Fact("DEAT", "1900", null, 2)], name: "albert /Beattie/")]);

        Assert.True(result.Success, string.Join("; ", result.Errors));
    }

    [Fact]
    public void AuthorProseTheServersPolicyBans_IsRefused()
    {
        var policy = ChangesetPolicy.Load(new MemoryStream(Encoding.UTF8.GetBytes("""{ "bannedPhrases": ["Both readings are kept"] }""")));
        var notes = new[] { new DraftNote("@I1@", "Both readings are kept in the record.") };

        var result = new ChangesetDrafter(new FakeLookup(), policy).Draft(
            [Finding("F-1", Memorial(), "x", [Fact("DEAT", "1900", null, 2)], notes: notes)]);

        Assert.False(result.Success);
        Assert.NotNull(result.Changeset);
        Assert.Contains("Both readings are kept", Assert.Single(result.Errors));
    }

    // ---- items ----

    [Fact]
    public void FindingsForTheSameTarget_BecomeOneItem_AndOthersGetTheirOwn()
    {
        var result = new ChangesetDrafter(new FakeLookup()).Draft([
            Finding("F-1", Memorial("Memorial #1"), "x", [Fact("DEAT", "1900", null, 2)], xref: "@I1@"),
            Finding("F-2", Memorial("Memorial #2"), "y", [Fact("BURI", null, "Salem", 2)], xref: "@I1@"),
            Finding("F-3", Memorial("Memorial #3"), "z", [Fact("DEAT", "1901", null, 2, "@I2@")], xref: "@I2@", name: null)]);

        var items = Json(result).GetProperty("items");
        Assert.Equal(2, items.GetArrayLength());
        Assert.Equal(1, items[0].GetProperty("item").GetInt32());
        Assert.Equal(2, items[1].GetProperty("item").GetInt32());
        Assert.Equal("@I1@ enrich-death", items[0].GetProperty("target").GetString());
    }

    // ---- what the document already holds ----

    [Fact]
    public void ANewPageForASourceTheFactAlreadyCites_StatesTheOldCitationAgain()
    {
        var lookup = new FakeLookup(
            sources: new() { ["Find a Grave"] = "@S1@" },
            facts: new() { ["@I1@|DEAT"] = [new ExistingFact(null, null, [new ExistingCitation("@S1@", "Memorial #0", "old text", 2)])] });

        var result = new ChangesetDrafter(lookup).Draft(
            [Finding("F-1", Memorial("Memorial #1"), "x", [Fact("DEAT", "1 JAN 1900", null, 2)])]);

        var citations = Ops(result)[0].GetProperty("citations");
        Assert.Equal(["Memorial #1", "Memorial #0"], citations.EnumerateArray().Select(c => c.GetProperty("page").GetString()));
        Assert.Equal("old text", citations[1].GetProperty("dataText").GetString());
        Assert.Single(result.Decisions, d => d.Kind == "kept-citation");
    }

    [Fact]
    public void TheSamePageAsTheExistingCitation_IsLeftToUpdateIt()
    {
        var lookup = new FakeLookup(
            sources: new() { ["Find a Grave"] = "@S1@" },
            facts: new() { ["@I1@|DEAT"] = [new ExistingFact(null, null, [new ExistingCitation("@S1@", "Memorial #1", null, 2)])] });

        var result = new ChangesetDrafter(lookup).Draft(
            [Finding("F-1", Memorial("Memorial #1"), "x", [Fact("DEAT", "1 JAN 1900", null, 2)])]);

        Assert.True(Ops(result)[0].TryGetProperty("citation", out _));
        Assert.DoesNotContain(result.Decisions, d => d.Kind == "kept-citation");
    }

    [Fact]
    public void AnExistingVerificationNote_IsFlaggedOpenBesideTheNewOne()
    {
        var lookup = new FakeLookup(notes: new() { ["@I1@"] = ["Needs independent verification: the parents are unproven."] });

        var result = new ChangesetDrafter(lookup).Draft(
            [Finding("F-1", Memorial(info: "secondary"), "x", [Fact("BIRT", "1924", null, 1)])]);

        Assert.Single(result.Decisions, d => d.Kind == "open" && d.Detail.Contains("already has a verification note"));
    }

    [Fact]
    public void TheSameCitationGradedDifferentlyInTwoItems_IsRefused()
    {
        var result = new ChangesetDrafter(new FakeLookup()).Draft([
            Finding("F-1", Memorial("Memorial #1"), "same", [Fact("DEAT", "1900", null, 2)], xref: "@I1@"),
            Finding("F-2", Memorial("Memorial #1", info: "secondary"), "same", [Fact("BIRT", "1850", null, 1, "@I2@")], xref: "@I2@")]);

        Assert.Contains("graded differently in different items", Assert.Single(result.Errors));
    }

    // ---- text facts and repeatable tags ----

    static DraftFact Text(string tag, string value, int quay, string record = "@I1@") =>
        new(record, tag, null, null, quay, null, null, null, value);

    static ExistingFact Held(string? date, string? place = null) => new(date, place, []);

    [Fact]
    public void ANameChange_IsCompiledAsATextValue()
    {
        var result = new ChangesetDrafter(new FakeLookup()).Draft(
            [Finding("F-1", Memorial(), "x", [Text("NAME", "Ralph /Morrill/", 2)])]);

        var op = Ops(result)[0];
        Assert.Equal("NAME", op.GetProperty("fact").GetString());
        Assert.Equal("Ralph /Morrill/", op.GetProperty("value").GetString());
    }

    const string RenameDocument = """
        0 HEAD
        1 GEDC
        2 VERS 7.0
        0 @I10@ INDI
        1 NAME George /Wadleigh/
        1 SEX M
        1 FAMS @F1@
        0 @I20@ INDI
        1 NAME Betty /Morrill/
        1 SEX F
        1 FAMS @F1@
        0 @F1@ FAM
        1 HUSB @I10@
        1 WIFE @I20@
        1 MARR
        2 DATE 22 OCT 1779
        0 TRLR
        """;

    static DraftFinding MarriageFinding(string id) =>
        new(id, new DraftSource($"Fictional register {id}", null, null, "page 1", null, "original", "primary"),
            "Joseph Wadleigh married Betty Morrill on 27 October 1779.",
            new DraftTarget("@I20@", null, "Betty Morrill"),
            [Text("NAME", "Joseph /Wadleigh/", 3, "@I10@"), Fact("MARR", "27 OCT 1779", null, 3, "@F1@")], [])
        {
            Relations = [new DraftRelation(DraftRelation.Spouse, "@I20@", "@I10@", "@F1@", null, null, null, 3)],
        };

    static DraftResult DraftMarriage(string document = RenameDocument, params DraftFinding[] findings)
    {
        var parsed = GedCore.Ged70.Ged70Parser.Read(new MemoryStream(Encoding.UTF8.GetBytes(document)));
        var lookup = new GedFire.Mcp.ModelDraftLookup(GedFire.Gen.ModelBuilder.Build(parsed));
        return new ChangesetDrafter(lookup).Draft(findings.Length == 0 ? [MarriageFinding("A"), MarriageFinding("B")] : findings);
    }

    [Fact]
    public void ABoundHusbandsName_IsUpdatedWithTheMarriageAndItsPreviousNameIsPreserved()
    {
        var drafted = DraftMarriage();
        Assert.True(drafted.Success, string.Join("; ", drafted.Errors));
        var changeset = GedCore.Apply.Changeset.Parse(drafted.Changeset!.ToJsonString());
        var original = Encoding.UTF8.GetBytes(RenameDocument);
        var validated = GedCore.Apply.ChangesetApplier.Run(original, changeset, [1], dryRun: true);
        Assert.True(validated.Success, string.Join("; ", validated.Errors));
        Assert.Contains(validated.Log, l => l.Contains("'George /Wadleigh/' → 'Joseph /Wadleigh/'"));
        var applied = GedCore.Apply.ChangesetApplier.Run(original, changeset, [1], dryRun: false);
        Assert.True(applied.Success, string.Join("; ", applied.Errors));
        var document = GedCore.Ged70.Ged70Parser.Read(new MemoryStream(applied.OutputBytes!));
        var husband = document.ByXref["@I10@"];
        Assert.Equal("Joseph /Wadleigh/", husband.FirstChild("NAME")!.PayloadValue);
        Assert.Equal(2, husband.FirstChild("NAME")!.ChildrenByTag("SOUR").Count());
        Assert.Equal("27 OCT 1779", document.ByXref["@F1@"].FirstChild("MARR")!.FirstChild("DATE")!.Value);
        Assert.Contains(husband.ChildrenByTag("NOTE"), n => n.FullValue().Contains("George") && n.FullValue().Contains("uncited"));
        var repeated = GedCore.Apply.ChangesetApplier.Run(applied.OutputBytes!, changeset, [1], dryRun: false);
        Assert.True(repeated.Success, string.Join("; ", repeated.Errors));
        Assert.Null(repeated.OutputBytes);
    }

    [Fact]
    public void AnExplicitExistingPersonBinding_DoesNotRequireARelationshipOp()
    {
        var finding = MarriageFinding("A") with { Relations = [], ExistingPeople = ["@I10@"] };
        var drafted = DraftMarriage(RenameDocument, finding);
        Assert.True(drafted.Success, string.Join("; ", drafted.Errors));
        Assert.DoesNotContain(Ops(drafted), o => o.GetProperty("op").GetString() == "createOrUpdateSpouse");
        Assert.Contains(Ops(drafted), o => o.GetProperty("record").GetString() == "@I10@" && o.GetProperty("fact").GetString() == "NAME");
    }

    [Fact]
    public void ANameFactDoesNotBindItsOwnRecord()
    {
        var finding = MarriageFinding("A") with { Relations = [] };
        var drafted = DraftMarriage(RenameDocument, finding);
        Assert.False(drafted.Success);
        Assert.Null(drafted.Changeset);
        Assert.Contains(drafted.Errors, e => e.Contains("facts[0].record @I10@ is not referenced"));
    }

    [Fact]
    public void AnUnknownExistingPersonBinding_IsRefused()
    {
        var finding = MarriageFinding("A") with { Relations = [], ExistingPeople = ["@I99@"] };
        var drafted = DraftMarriage(RenameDocument, finding);
        Assert.Contains(drafted.Errors, e => e.Contains("existingPeople[0] @I99@ is not"));
    }

    [Fact]
    public void ANameCanBeAddedToABoundPersonWithoutAnExistingName()
    {
        string document = RenameDocument.Replace("1 NAME George /Wadleigh/", "");
        var finding = MarriageFinding("A") with { Relations = [], ExistingPeople = ["@I10@"] };
        var drafted = DraftMarriage(document, finding);
        Assert.True(drafted.Success, string.Join("; ", drafted.Errors));
    }

    [Fact]
    public void AnUnchangedName_DoesNotCreateAPreviousNameNote()
    {
        var finding = MarriageFinding("A") with { Facts = [Text("NAME", "George /Wadleigh/", 3, "@I10@")] };
        var drafted = DraftMarriage(RenameDocument, finding);
        var applied = GedCore.Apply.ChangesetApplier.Run(Encoding.UTF8.GetBytes(RenameDocument),
            GedCore.Apply.Changeset.Parse(drafted.Changeset!.ToJsonString()), [1], dryRun: false);
        Assert.True(applied.Success, string.Join("; ", applied.Errors));
        var person = GedCore.Ged70.Ged70Parser.Read(new MemoryStream(applied.OutputBytes!)).ByXref["@I10@"];
        Assert.Empty(person.ChildrenByTag("NOTE"));
    }

    [Fact]
    public void ATargetSurnameChange_AlsoRequiresExplicitPermission()
    {
        var finding = MarriageFinding("A") with { Facts = [Text("NAME", "Betty /Other/", 3, "@I20@")] };
        Assert.Contains(DraftMarriage(RenameDocument, finding).Errors, e => e.Contains("allowSurnameChange"));
    }

    [Theory]
    [InlineData("Joseph /Other/")]
    [InlineData("Joseph")]
    [InlineData("Joseph //")]
    public void AChangedOrRemovedSurname_RequiresExplicitPermission(string name)
    {
        var finding = MarriageFinding("A");
        var fact = finding.Facts[0] with { Value = name };
        var refused = DraftMarriage(RenameDocument, finding with { Facts = [fact] });
        Assert.False(refused.Success);
        Assert.Contains(refused.Errors, e => e.Contains("allowSurnameChange"));
        var allowed = DraftMarriage(RenameDocument, finding with { Facts = [fact with { AllowSurnameChange = true }] });
        Assert.True(allowed.Success, string.Join("; ", allowed.Errors));
        Assert.Equal(name, Ops(allowed).Single(o => o.GetProperty("op").GetString() == "createOrUpdateVital").GetProperty("value").GetString());
    }

    [Theory]
    [InlineData("keep")]
    [InlineData("drop")]
    public void ANameCorrection_CannotLoseOrMisattributeItsPreviousCitations(string disposition)
    {
        var finding = MarriageFinding("A");
        var drafted = DraftMarriage(RenameDocument, finding with { Facts = [finding.Facts[0] with { ReplacedCitations = disposition }] });
        Assert.False(drafted.Success);
        Assert.Contains(drafted.Errors, e => e.Contains("previous name's citations must be preserved"));
    }

    [Fact]
    public void ATargetPersonNameCorrection_StillCompilesAndApplies()
    {
        var finding = MarriageFinding("A") with
        {
            Relations = [],
            Facts = [Text("NAME", "Elizabeth /Morrill/", 3, "@I20@")],
        };
        var drafted = DraftMarriage(RenameDocument, finding);
        Assert.True(drafted.Success, string.Join("; ", drafted.Errors));
        var applied = GedCore.Apply.ChangesetApplier.Run(Encoding.UTF8.GetBytes(RenameDocument),
            GedCore.Apply.Changeset.Parse(drafted.Changeset!.ToJsonString()), [1], dryRun: false);
        Assert.True(applied.Success, string.Join("; ", applied.Errors));
        var person = GedCore.Ged70.Ged70Parser.Read(new MemoryStream(applied.OutputBytes!)).ByXref["@I20@"];
        Assert.Equal("Elizabeth /Morrill/", person.FirstChild("NAME")!.PayloadValue);
        Assert.Contains(person.ChildrenByTag("NOTE"), n => n.FullValue() == "The previous name was Betty Morrill, which was uncited.");
    }

    [Fact]
    public void APreviouslyCitedName_KeepsItsCompleteCitationTrailInItsOwnNote()
    {
        string document = RenameDocument.Replace("1 NAME George /Wadleigh/", """
            1 NAME George /Wadleigh/
            2 SOUR @S1@
            3 PAGE old page
            3 DATA
            4 TEXT George in an earlier fictional register.
            3 QUAY 1
            3 NOTE Original citation annotation.
            """).Replace("0 TRLR", "0 @S1@ SOUR\n1 TITL Earlier fictional register\n0 TRLR");
        var finding = MarriageFinding("A") with { Notes = [new DraftNote("@I10@", "An unrelated research note.")] };
        var drafted = DraftMarriage(document, finding);
        Assert.True(drafted.Success, string.Join("; ", drafted.Errors));
        drafted.Changeset!["items"]![0]!["ops"]!.AsArray().Insert(0, new System.Text.Json.Nodes.JsonObject
        {
            ["op"] = "createOrUpdateNote", ["record"] = "@I10@", ["text"] = "An earlier unrelated note.",
        });
        var applied = GedCore.Apply.ChangesetApplier.Run(Encoding.UTF8.GetBytes(document),
            GedCore.Apply.Changeset.Parse(drafted.Changeset!.ToJsonString()), [1], dryRun: false);
        Assert.True(applied.Success, string.Join("; ", applied.Errors));
        var person = GedCore.Ged70.Ged70Parser.Read(new MemoryStream(applied.OutputBytes!)).ByXref["@I10@"];
        var note = person.ChildrenByTag("NOTE").Single(n => n.FullValue() == "The previous name was George Wadleigh, which was cited.");
        Assert.DoesNotContain("@", note.FullValue());
        var citation = Assert.Single(note.ChildrenByTag("SOUR"));
        Assert.Equal("@S1@", citation.Value);
        Assert.Equal("old page", citation.FirstChild("PAGE")!.Value);
        Assert.Equal("George in an earlier fictional register.", citation.FirstChild("DATA")!.FirstChild("TEXT")!.FullValue());
        Assert.Equal("1", citation.FirstChild("QUAY")!.Value);
        Assert.Equal("Original citation annotation.", citation.FirstChild("NOTE")!.FullValue());
        Assert.DoesNotContain(person.FirstChild("NAME")!.ChildrenByTag("SOUR"), c => c.Value == "@S1@");
        Assert.Empty(person.ChildrenByTag("NOTE").Single(n => n.FullValue() == "An earlier unrelated note.").ChildrenByTag("SOUR"));
        var repeated = GedCore.Apply.ChangesetApplier.Run(applied.OutputBytes!,
            GedCore.Apply.Changeset.Parse(drafted.Changeset!.ToJsonString()), [1], dryRun: false);
        Assert.True(repeated.Success, string.Join("; ", repeated.Errors));
        Assert.Null(repeated.OutputBytes);
    }

    [Fact]
    public void TwoDifferentNames_AreLeftOpen()
    {
        var result = new ChangesetDrafter(new FakeLookup()).Draft(
            [Finding("F-1", Memorial(), "x", [Text("NAME", "Ralph /Morrill/", 2), Text("NAME", "Rafe /Morrill/", 2)])]);

        Assert.Empty(Ops(result));
        Assert.Single(result.Decisions, d => d.Kind == "open" && d.Detail.Contains("NAME on @I1@"));
    }

    [Fact]
    public void SeveralCensusFactsOnOnePerson_AreEachAddedAsAnInstance()
    {
        var facts = new[] { Fact("CENS", "1900", "Salem", 2), Fact("CENS", "1910", "Salem", 2), Fact("CENS", "1920", "Lynn", 2) };

        var result = new ChangesetDrafter(new FakeLookup()).Draft([Finding("F-1", Memorial(), "x", facts)]);

        var ops = Ops(result);
        Assert.Equal(["1900", "1910", "1920"], ops.Select(o => o.GetProperty("value").GetProperty("date").GetString()));
        Assert.All(ops, o => Assert.Equal("add", o.GetProperty("mode").GetString()));
        Assert.DoesNotContain(result.Decisions, d => d.Kind == "open");
    }

    [Fact]
    public void ARepeatableFactNobodyHolds_IsAPlainUpsert()
    {
        var result = new ChangesetDrafter(new FakeLookup()).Draft(
            [Finding("F-1", Memorial(), "x", [Fact("CENS", "1900", "Salem", 2)])]);

        Assert.False(Ops(result)[0].TryGetProperty("mode", out _));
        Assert.False(Ops(result)[0].TryGetProperty("match", out _));
    }

    [Fact]
    public void ARepeatableFactBesideAnotherDate_IsAdded()
    {
        var lookup = new FakeLookup(facts: new() { ["@I1@|CENS"] = [Held("1900", "Salem")] });

        var result = new ChangesetDrafter(lookup).Draft([Finding("F-1", Memorial(), "x", [Fact("CENS", "1910", "Salem", 2)])]);

        Assert.Equal("add", Ops(result)[0].GetProperty("mode").GetString());
    }

    [Fact]
    public void ARepeatableFactOnADateTheRecordHolds_UpdatesThatInstanceByMatch()
    {
        var lookup = new FakeLookup(facts: new() { ["@I1@|CENS"] = [Held("1900"), Held("1910")] });

        var result = new ChangesetDrafter(lookup).Draft([Finding("F-1", Memorial(), "x", [Fact("CENS", "1910", "Salem", 2)])]);

        var op = Ops(result)[0];
        Assert.Equal("1910", op.GetProperty("match").GetProperty("date").GetString());
        Assert.False(op.TryGetProperty("mode", out _));
    }

    [Fact]
    public void TwoDifferentDeathFacts_AreStillLeftOpen()
    {
        var result = new ChangesetDrafter(new FakeLookup()).Draft(
            [Finding("F-1", Memorial(), "x", [Fact("DEAT", "1900", null, 2), Fact("DEAT", "1901", null, 2)])]);

        Assert.Empty(Ops(result));
        Assert.Single(result.Decisions, d => d.Kind == "open");
    }

    [Fact]
    public void EveryDecisionKindTheDrafterEmits_IsListed()
    {
        var lookup = new FakeLookup(
            sources: new() { ["Find a Grave"] = "@S1@" },
            facts: new() { ["@I1@|DEAT"] = [new ExistingFact(null, null, [new ExistingCitation("@S1@", "Memorial #0", null, 2)])] });

        var result = new ChangesetDrafter(lookup).Draft(
            [Finding("F-1", Memorial("Memorial #1"), "x", [Fact("DEAT", "1900", null, 1)])]);

        Assert.All(result.Decisions, d => Assert.Contains(d.Kind, DraftDecisionKinds.All));
    }

    [Fact]
    public void RepeatedCensusFacts_PassTheAppliersOverwriteGuard()
    {
        const string document = """
            0 HEAD
            1 GEDC
            2 VERS 7.0
            0 @I1@ INDI
            1 NAME Hiram /Test/
            1 SEX M
            1 CENS
            2 DATE 1900
            2 PLAC Salem
            0 TRLR
            """;
        var lookup = new FakeLookup(facts: new() { ["@I1@|CENS"] = [Held("1900", "Salem")] });
        var facts = new[] { Fact("CENS", "1900", "Salem, Essex", 2), Fact("CENS", "1910", "Salem", 2), Fact("CENS", "1920", "Lynn", 2) };

        var drafted = new ChangesetDrafter(lookup).Draft([Finding("F-1", Memorial(), "x", facts)]);
        var applied = GedCore.Apply.ChangesetApplier.Run(
            Encoding.UTF8.GetBytes(document), GedCore.Apply.Changeset.Parse(drafted.Changeset!.ToJsonString()), [1], dryRun: true);

        Assert.True(applied.Success, string.Join("; ", applied.Errors));
    }
}
