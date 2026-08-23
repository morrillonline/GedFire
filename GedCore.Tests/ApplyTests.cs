using System.Text.Json;
using GedCore.Apply;

namespace GedCore.Tests;

/// <summary>
/// Pipeline tests for the changeset applier (gedfire apply, dialect v2):
/// full-changeset application, the idempotence guarantees (re-run = all
/// no-ops, byte-identical file), item filtering, dry-run, validation
/// failures with the no-write guarantee, and changeset parsing.
/// </summary>
public class ApplyTests : ApplyTestBase
{
    // The three new-record identities this changeset asks apply to mint --
    // @NewS1@, @NewI1@, @NewF1@ -- happen to mint to @S00002@/@I00005@/@F00004@
    // given the base fixture's existing @S00001@/@I0000[1-4]@/@F0000[1-3]@
    // records (sources apply before items; person before family within one
    // op), which is exactly what this changeset named directly before
    // placeholders existed -- so every other assertion below is unchanged.
    private const string FullChangesetJson = """
    {
      "proposal": "test",
      "newSources": [
        { "xref": "@NewS1@", "ops": [
          { "op": "createOrUpdateSource", "xref": "@NewS1@", "auth": "Test Funeral Home",
            "title": "Test obituary", "url": "https://example.org/obit",
            "accessed": "2026-07-04" } ] }
      ],
      "items": [
        { "item": 1, "target": "@I00001@", "ops": [
          { "op": "createOrUpdateVital", "record": "@I00001@", "fact": "NAME",
            "value": "Albin H. /Test/", "match": "Allen /Test/",
            "substructures": [ { "tag": "NICK", "value": "Babe" } ],
            "citation": { "source": "@NewS1@", "page": "p. 1",
                          "dataText": "Albin H. Test", "quay": 2 } },
          { "op": "createOrUpdateNote", "record": "@I00001@",
            "text": "Name corrected from census form." } ] },
        { "item": 2, "target": "@I00001@", "ops": [
          { "op": "createOrUpdateVital", "record": "@I00001@", "fact": "BIRT",
            "value": { "date": "2 APR 1928", "place": "Fergus Falls, Minnesota" },
            "citation": { "source": "@NewS1@", "page": "p. 1",
                          "dataText": "born April 2, 1928", "quay": 2 } },
          { "op": "createOrUpdateVital", "record": "@I00001@", "fact": "DEAT",
            "value": { "date": "9 APR 2009", "place": "Elbow Lake, Minnesota" },
            "citation": { "source": "@NewS1@", "page": "p. 1",
                          "dataText": "passed away April 9, 2009", "quay": 3 } } ] },
        { "item": 3, "target": "new @NewI1@ / @NewF1@", "ops": [
          { "op": "createOrUpdateSpouse", "person": "@I00001@",
            "spouse": { "xref": "@NewI1@", "name": "Edith /Spouse/", "sex": "F",
                        "facts": [ { "fact": "DEAT", "value": { "date": "JAN 1971" },
                                     "citation": { "source": "@NewS1@", "page": "p. 1",
                                                   "dataText": "Edith died", "quay": 2 } } ] },
            "family": "@NewF1@",
            "marriage": { "date": "2 JUN 1949", "place": "Underwood, Minnesota",
                          "citations": [
                            { "source": "@S00001@", "page": "a", "dataText": "married", "quay": 2 },
                            { "source": "@NewS1@", "page": "b", "dataText": "married too", "quay": 2 } ] },
            "note": "No children recorded." } ] }
      ]
    }
    """;

    [Fact]
    public void Apply_FullChangeset_Succeeds_WithExpectedStructures()
    {
        WriteBaseFile();

        var result = RunExpectSuccess(FullChangesetJson);
        Assert.Equal(1, result.Deltas["INDI"]);
        Assert.Equal(1, result.Deltas["FAM"]);
        Assert.Equal(1, result.Deltas["SOUR"]);
        Assert.Equal(
            new Dictionary<string, string> { ["@NewS1@"] = "@S00002@", ["@NewI1@"] = "@I00005@", ["@NewF1@"] = "@F00004@" },
            result.MintedXrefs);

        var doc = ReadDoc();

        // NAME updated via match on the old value; NICK first child, citation attached
        var indi = doc.ByXref["@I00001@"];
        var name = indi.FirstChild("NAME")!;
        Assert.Equal("Albin H. /Test/", name.Value);
        Assert.Equal("NICK", name.Children[0].Tag);
        Assert.Equal("Babe", name.Children[0].Value);
        var nameSour = name.FirstChild("SOUR")!;
        Assert.Equal("@S00002@", nameSour.Value);
        Assert.Equal("Albin H. Test", nameSour.FirstChild("DATA")!.FirstChild("TEXT")!.Value);

        // BIRT refined (day zero-padded), citation appended inside BIRT
        var birt = indi.FirstChild("BIRT")!;
        Assert.Equal("02 APR 1928", birt.FirstChild("DATE")!.Value);
        Assert.Equal("Fergus Falls, Minnesota", birt.FirstChild("PLAC")!.Value);
        Assert.NotNull(birt.FirstChild("SOUR"));

        // DEAT created before FAMS/FAMC; new FAMS back-link present
        var tags = indi.Children.Select(c => c.Tag).ToList();
        Assert.True(tags.IndexOf("DEAT") < tags.IndexOf("FAMS"));
        Assert.True(tags.LastIndexOf("FAMS") < tags.IndexOf("FAMC"));
        Assert.Contains("@F00004@", indi.ChildrenByTag("FAMS").Select(c => c.Value));
        Assert.Equal("Name corrected from census form.",
            indi.ChildrenByTag("NOTE").Single().Value);

        // new person: NAME/SEX/facts/FAMS/UID, two-way linked
        var edith = doc.ByXref["@I00005@"];
        Assert.Equal("JAN 1971", edith.FirstChild("DEAT")!.FirstChild("DATE")!.Value);
        Assert.Equal("@F00004@", edith.ChildrenByTag("FAMS").Single().Value);
        Assert.NotNull(edith.FirstChild("UID"));

        // new family: links, padded MARR date, both citations, note, UID
        var fam = doc.ByXref["@F00004@"];
        Assert.Equal("@I00001@", fam.FirstChild("HUSB")!.Value);
        Assert.Equal("@I00005@", fam.FirstChild("WIFE")!.Value);
        var marr = fam.FirstChild("MARR")!;
        Assert.Equal("02 JUN 1949", marr.FirstChild("DATE")!.Value);
        Assert.Equal(2, marr.ChildrenByTag("SOUR").Count());
        Assert.Equal("No children recorded.", fam.FirstChild("NOTE")!.Value);
        Assert.NotNull(fam.FirstChild("UID"));

        // new source after the existing one, composed NOTE
        var sour = doc.ByXref["@S00002@"];
        Assert.Equal("Test Funeral Home", sour.FirstChild("AUTH")!.Value);
        Assert.Equal(
            "Test obituary, online at https://example.org/obit (accessed 2026-07-04).",
            sour.FirstChild("NOTE")!.Value);

        // record grouping preserved: INDIs, then FAMs, then SOURs, then TRLR
        Assert.Equal(
            ["HEAD", "INDI", "INDI", "INDI", "INDI", "INDI",
             "FAM", "FAM", "FAM", "FAM", "SOUR", "SOUR", "TRLR"],
            doc.Records.Select(r => r.Tag).ToList());
    }

    /// <summary>
    /// Re-submitting the identical placeholder-bearing changeset is caught,
    /// not silently duplicated: @NewI1@ ("Edith /Spouse/", married to the
    /// same @I00001@) reconstructs a high-confidence match against the
    /// person the first run already created, and person duplicate detection
    /// rejects the whole changeset before any mutation. Source and family
    /// creation in the same changeset are consequently rejected too — not
    /// because they carry their own duplicate protection (they don't), but
    /// because validation never lets any op in a duplicate-flagged changeset
    /// reach mutation.
    /// </summary>
    [Fact]
    public void Apply_SameChangesetTwice_WithPlaceholders_SecondRunIsRejectedAsADuplicate()
    {
        WriteBaseFile();
        RunExpectSuccess(FullChangesetJson);
        byte[] afterFirst = ReadBytes();

        var second = Run(FullChangesetJson, dryRun: false);

        Assert.False(second.Success);
        Assert.Contains(second.Errors, e => e.Contains("@NewI1@") && e.Contains("high-confidence match"));
        Assert.Equal(afterFirst, ReadBytes());
        Assert.Equal(2, ReadDoc().Records.Count(r => r.Tag == "SOUR"));   // unchanged: @S00001@ + the first run's @S00002@
    }

    /// <summary>
    /// Idempotent re-application is still available: address the same edit
    /// by the real xrefs the first run's MintedXrefs reported, not by
    /// resubmitting the placeholders. createOrUpdateSpouse's inline-person
    /// degrade-to-link rule (identical NAME) and createOrUpdate's ordinary
    /// unconditional-update-to-no-op rule both still apply unchanged once a
    /// real xref is named.
    /// </summary>
    [Fact]
    public void Apply_SameChangesetTwice_AddressedByRealMintedXrefs_SecondRunIsAllNoOps()
    {
        WriteBaseFile();
        var first = RunExpectSuccess(FullChangesetJson);
        byte[] afterFirst = ReadBytes();

        string realized = FullChangesetJson
            .Replace("@NewS1@", first.MintedXrefs["@NewS1@"])
            .Replace("@NewI1@", first.MintedXrefs["@NewI1@"])
            .Replace("@NewF1@", first.MintedXrefs["@NewF1@"]);
        var second = RunExpectSuccess(realized);

        Assert.Empty(second.Deltas);
        Assert.Empty(second.MintedXrefs);
        Assert.Contains(second.Log, l => l.Contains("no changes; file untouched"));
        Assert.DoesNotContain(second.Log,
            l => l.Contains("created") || l.Contains("updated") || l.Contains("→"));
        Assert.Equal(afterFirst, ReadBytes());
    }

    [Fact]
    public void Apply_AllNoOps_SkipsWrite()
    {
        byte[] original = WriteBaseFile();
        // every op targets absent state — all deletes degrade to no-ops
        var result = RunExpectSuccess("""
            { "items": [ { "item": 1, "ops": [
              { "op": "deleteVital", "record": "@I00001@", "fact": "DEAT" },
              { "op": "deleteCitation", "record": "@I00001@", "fact": "BIRT", "source": "@S00001@" },
              { "op": "deleteNote", "record": "@I00001@", "text": "No such note." },
              { "op": "deleteSpouse", "person": "@I00002@", "spouse": "@I00004@" },
              { "op": "deleteChild", "family": "@F00002@", "child": "@I00001@" },
              { "op": "deleteSource", "xref": "@S00099@" } ] } ] }
            """);

        Assert.Contains(result.Log, l => l.Contains("no changes; file untouched"));
        Assert.All(result.Log.Where(l => l.StartsWith("delete")),
                   l => Assert.Contains("no-op", l));
        Assert.Equal(original, ReadBytes());
    }

    [Fact]
    public void Apply_Result_IsByteStableOnReserialization()
    {
        WriteBaseFile();
        RunExpectSuccess(FullChangesetJson);

        byte[] written = ReadBytes();
        var ms = new MemoryStream();
        Ged70.Ged70Formatter.Write(Ged70.Ged70Parser.Read(new MemoryStream(written)), ms);
        Assert.Equal(written, ms.ToArray());
    }

    [Fact]
    public void Apply_ItemFilter_AppliesOnlySelectedItems()
    {
        WriteBaseFile();

        RunExpectSuccess(FullChangesetJson, items: [1]);

        var doc = ReadDoc();
        Assert.Equal("Albin H. /Test/", doc.ByXref["@I00001@"].FirstChild("NAME")!.Value);
        Assert.Null(doc.ByXref["@I00001@"].FirstChild("DEAT"));      // item 2 not applied
        Assert.False(doc.ByXref.ContainsKey("@I00005@"));            // item 3 not applied
        Assert.True(doc.ByXref.ContainsKey("@S00002@"));             // sources always applied
    }

    [Fact]
    public void Apply_ItemFilter_SkipsNewSourceCitedOnlyByAnExcludedItem()
    {
        WriteBaseFile();

        var result = RunExpectSuccess("""
            { "newSources": [
                { "xref": "@NewS1@", "ops": [
                  { "op": "createOrUpdateSource", "xref": "@NewS1@", "title": "Excluded source" } ] },
                { "xref": "@NewS2@", "ops": [
                  { "op": "createOrUpdateSource", "xref": "@NewS2@", "title": "Included source" } ] } ],
              "items": [
                { "item": 1, "ops": [
                  { "op": "createOrUpdateNote", "record": "@I00001@", "text": "Item one note.",
                    "citation": { "source": "@NewS1@", "page": "p. 1", "dataText": "x", "quay": 2 } } ] },
                { "item": 2, "ops": [
                  { "op": "createOrUpdateNote", "record": "@I00002@", "text": "Item two note.",
                    "citation": { "source": "@NewS2@", "page": "p. 1", "dataText": "y", "quay": 2 } } ] } ] }
            """, items: [2]);

        // item 1 (the only citer of @NewS1@) was excluded — its group is skipped
        // entirely, so @NewS1@ never mints at all; @NewS2@ is the only source
        // actually created, and mints @S00002@ (the first free slot).
        Assert.Equal(["@NewS2@"], result.MintedXrefs.Keys);
        Assert.Equal("@S00002@", result.MintedXrefs["@NewS2@"]);
        var doc = ReadDoc();
        Assert.True(doc.ByXref.ContainsKey("@S00002@"));
        Assert.Equal(2, doc.Records.Count(r => r.Tag == "SOUR"));   // @S00001@ (existing) + @S00002@ (minted)
        Assert.Contains(result.Log, l => l.Contains("newSources @NewS1@: skipped"));
        Assert.DoesNotContain(result.Log, l => l.Contains("@NewS2@: skipped"));
    }

    [Fact]
    public void Apply_ItemFilter_StillAppliesASourceNoItemCites()
    {
        WriteBaseFile();

        // A source with no citing item anywhere in the changeset is prepared ahead
        // of citing it (e.g. a follow-up changeset will add the citation) — it stays
        // always-applied regardless of which items are selected.
        var result = RunExpectSuccess("""
            { "newSources": [
                { "xref": "@NewS1@", "ops": [
                  { "op": "createOrUpdateSource", "xref": "@NewS1@", "title": "Prepared ahead" } ] } ],
              "items": [
                { "item": 1, "ops": [
                  { "op": "createOrUpdateNote", "record": "@I00001@", "text": "Unrelated note." } ] } ] }
            """, items: [1]);

        Assert.True(ReadDoc().ByXref.ContainsKey(result.MintedXrefs["@NewS1@"]));
    }

    [Fact]
    public void Apply_DryRun_ValidatesButDoesNotModify()
    {
        byte[] original = WriteBaseFile();

        var result = Run(FullChangesetJson, dryRun: true);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Equal(original, ReadBytes());
    }

    [Fact]
    public void Apply_UnknownItemNumber_Fails()
    {
        byte[] original = WriteBaseFile();

        var result = Run(FullChangesetJson, items: [1, 99]);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("99"));
        Assert.Equal(original, ReadBytes());
    }

    // -------------------------------------------------------------------------
    // Validation failures — file must never be modified
    // -------------------------------------------------------------------------

    [Fact]
    public void Apply_AllowsUncitedVital()
    {
      WriteBaseFile();

      var result = Run("""
        { "items": [ { "item": 1, "ops": [
          { "op": "createOrUpdateVital", "record": "@I00001@", "fact": "DEAT",
          "value": { "date": "1900" } } ] } ] }
        """);

      Assert.True(result.Success, string.Join("; ", result.Errors));
      var death = ReadDoc().ByXref["@I00001@"].FirstChild("DEAT")!;
      Assert.Equal("1900", death.FirstChild("DATE")!.Value);
      Assert.Null(death.FirstChild("SOUR"));
    }

    [Theory]
    // target record missing
    [InlineData("""
        { "items": [ { "item": 1, "ops": [
          { "op": "createOrUpdateVital", "record": "@I09999@", "fact": "DEAT",
            "value": { "date": "1900" },
            "citation": { "source": "@S00001@", "page": "x", "dataText": "y", "quay": 1 } } ] } ] }
        """, "not in file")]
    // unknown citation source
    [InlineData("""
        { "items": [ { "item": 1, "ops": [
          { "op": "createOrUpdateVital", "record": "@I00001@", "fact": "DEAT",
            "value": { "date": "1900" },
            "citation": { "source": "@S09999@", "page": "x", "dataText": "y", "quay": 1 } } ] } ] }
        """, "unknown")]
    // two same-tag facts, no match selector
    [InlineData("""
        { "items": [ { "item": 1, "ops": [
          { "op": "createOrUpdateVital", "record": "@I00003@", "fact": "CENS",
            "value": { "place": "Iowa" },
            "citation": { "source": "@S00001@", "page": "x", "dataText": "y", "quay": 1 } } ] } ] }
        """, "ambiguous")]
    // match selector hits nothing (and nothing satisfies the request)
    [InlineData("""
        { "items": [ { "item": 1, "ops": [
          { "op": "createOrUpdateVital", "record": "@I00003@", "fact": "CENS",
            "match": { "date": "1950" }, "value": { "place": "Iowa" },
            "citation": { "source": "@S00001@", "page": "x", "dataText": "y", "quay": 1 } } ] } ] }
        """, "matches")]
    // a citation cannot create its fact
    [InlineData("""
        { "items": [ { "item": 1, "ops": [
          { "op": "createOrUpdateCitation", "record": "@I00001@", "fact": "DEAT",
            "citation": { "source": "@S00001@", "page": "x", "dataText": "y", "quay": 1 } } ] } ] }
        """, "cannot create its fact")]
    // one source twice on one fact
    [InlineData("""
        { "items": [ { "item": 1, "ops": [
          { "op": "createOrUpdateVital", "record": "@I00001@", "fact": "DEAT",
            "value": { "date": "1990" },
            "citations": [
              { "source": "@S00001@", "page": "a", "dataText": "x", "quay": 1 },
              { "source": "@S00001@", "page": "b", "dataText": "y", "quay": 1 } ] } ] } ] }
        """, "cited twice")]
    // inline new spouse requires an explicit new family xref
    [InlineData("""
        { "items": [ { "item": 1, "ops": [
          { "op": "createOrUpdateSpouse", "person": "@I00002@",
            "spouse": { "xref": "@NewI1@", "name": "New /Wife/", "sex": "F" } } ] } ] }
        """, "supply a new family xref")]
    // two shared families, no family xref
    [InlineData("""
        { "items": [ { "item": 1, "ops": [
          { "op": "createOrUpdateSpouse", "person": "@I00001@", "spouse": "@I00004@" } ] } ] }
        """, "share 2 families")]
    // inline xref collides with an existing person of a different name
    [InlineData("""
        { "items": [ { "item": 1, "ops": [
          { "op": "createOrUpdateChild", "family": "@F00001@",
            "child": { "xref": "@I00004@", "name": "Someone /Else/", "sex": "F" } } ] } ] }
        """, "different name")]
    // husb/wife are creation seeds only
    [InlineData("""
        { "items": [ { "item": 1, "ops": [
          { "op": "createOrUpdateChild", "family": "@F00002@", "child": "@I00002@",
            "husb": "@I00001@" } ] } ] }
        """, "creation seeds")]
    // invalid parent role
    [InlineData("""
        { "items": [ { "item": 1, "ops": [
          { "op": "createOrUpdateParent", "person": "@I00001@", "role": "uncle",
            "parent": "@I00002@" } ] } ] }
        """, "role must be father or mother")]
    public void Apply_InvalidOp_FailsValidation_AndLeavesFileUntouched(string json, string expectedError)
    {
        byte[] original = WriteBaseFile();

        var result = Run(json, items: [1]);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains(expectedError));
        Assert.Equal(original, ReadBytes());
    }

    // -------------------------------------------------------------------------
    // Post-apply orphan-source check — belt-and-suspenders behind the
    // newSources[] item-citation filtering above
    // -------------------------------------------------------------------------

    [Fact]
    public void Apply_SourceCitedBySomeItemButLeftUncited_FailsVerify_AndLeavesFileUntouched()
    {
        byte[] original = WriteBaseFile();

        // item 1 creates the source via a cited note; item 2 then removes that very
        // citation — the source is created because an item cited it, but the run's
        // net effect leaves it an orphan, which the post-apply check must catch even
        // though every individual op validated and applied cleanly on its own.
        var result = Run("""
            { "newSources": [
                { "xref": "@NewS1@", "ops": [
                  { "op": "createOrUpdateSource", "xref": "@NewS1@", "title": "Soon uncited" } ] } ],
              "items": [
                { "item": 1, "ops": [
                  { "op": "createOrUpdateNote", "record": "@I00001@", "text": "Cited note.",
                    "citation": { "source": "@NewS1@", "page": "p. 1", "dataText": "x", "quay": 2 } } ] },
                { "item": 2, "ops": [
                  { "op": "deleteCitation", "record": "@I00001@", "fact": "NOTE", "source": "@NewS1@" } ] } ] }
            """, items: [1, 2]);

        Assert.False(result.Success);
        // The orphan check runs against the reparsed post-apply document, so
        // it names @NewS1@'s minted real xref (@S00002@ given the base
        // fixture's one existing source), not the placeholder itself.
        Assert.Contains(result.Errors, e => e.Contains("orphan source @S00002@"));
        Assert.Equal(original, ReadBytes());
    }

    // -------------------------------------------------------------------------
    // Changeset parsing
    // -------------------------------------------------------------------------

    [Fact]
    public void Parse_UnknownOp_Throws_NamingTheVocabulary()
    {
        var ex = Assert.Throws<JsonException>(() => Changeset.Parse("""
            { "items": [ { "item": 1, "ops": [
              { "op": "addFact", "record": "@I00001@", "fact": "DEAT" } ] } ] }
            """));
        Assert.Contains("addFact", ex.Message);
        Assert.Contains("createOrUpdate", ex.Message);
    }

    [Fact]
    public void Parse_PersonRef_AcceptsBareXrefAndInlineObject()
    {
        var cs = Changeset.Parse("""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateSpouse", "person": "@I00001@", "spouse": "@I00004@" },
              { "op": "createOrUpdateSpouse", "person": "@I00001@",
                "spouse": { "xref": "@I00050@", "name": "New /Wife/", "sex": "F",
                            "facts": [ { "fact": "BIRT", "value": { "date": "1930" },
                                         "citation": { "source": "@S00001@", "page": "x",
                                                       "dataText": "y", "quay": 1 } } ] },
                "family": "@F00050@" } ] } ] }
            """);

        var link = (CreateOrUpdateSpouseOp)cs.Items[0].Ops[0];
        Assert.Equal("@I00004@", link.Spouse.Xref);
        Assert.False(link.Spouse.IsInline);

        var inline = (CreateOrUpdateSpouseOp)cs.Items[0].Ops[1];
        Assert.True(inline.Spouse.IsInline);
        Assert.Equal("New /Wife/", inline.Spouse.Name);
        Assert.Single(inline.Spouse.Facts);
        Assert.Single(inline.Spouse.Facts[0].Citations);
    }

    [Fact]
    public void Parse_Match_AcceptsBareTextAndDatePlaceObject()
    {
        var cs = Changeset.Parse("""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateVital", "record": "@I00001@", "fact": "NAME",
                "value": "New /Name/", "match": "Old /Name/",
                "citation": { "source": "@S00001@", "page": "x", "dataText": "y", "quay": 1 } },
              { "op": "deleteVital", "record": "@I00003@", "fact": "CENS",
                "match": { "date": "1940", "place": "Minnesota" } } ] } ] }
            """);

        var textMatch = (CreateOrUpdateVitalOp)cs.Items[0].Ops[0];
        Assert.Equal("Old /Name/", textMatch.Match!.Text);

        var eventMatch = (DeleteVitalOp)cs.Items[0].Ops[1];
        Assert.Equal("1940", eventMatch.Match!.Date);
        Assert.Equal("Minnesota", eventMatch.Match!.Place);
    }
}
