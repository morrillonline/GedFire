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
    private const string FullChangesetJson = """
    {
      "proposal": "test",
      "newSources": [
        { "xref": "@S00002@", "ops": [
          { "op": "createOrUpdateSource", "xref": "@S00002@", "auth": "Test Funeral Home",
            "title": "Test obituary", "url": "https://example.org/obit",
            "accessed": "2026-07-04" } ] }
      ],
      "items": [
        { "item": 1, "target": "@I00001@", "ops": [
          { "op": "createOrUpdateVital", "record": "@I00001@", "fact": "NAME",
            "value": "Albin H. /Test/", "match": "Allen /Test/",
            "substructures": [ { "tag": "NICK", "value": "Babe" } ],
            "citation": { "source": "@S00002@", "page": "p. 1",
                          "dataText": "Albin H. Test", "quay": 2 } },
          { "op": "createOrUpdateNote", "record": "@I00001@",
            "text": "Name corrected from census form." } ] },
        { "item": 2, "target": "@I00001@", "ops": [
          { "op": "createOrUpdateVital", "record": "@I00001@", "fact": "BIRT",
            "value": { "date": "2 APR 1928", "place": "Fergus Falls, Minnesota" },
            "citation": { "source": "@S00002@", "page": "p. 1",
                          "dataText": "born April 2, 1928", "quay": 2 } },
          { "op": "createOrUpdateVital", "record": "@I00001@", "fact": "DEAT",
            "value": { "date": "9 APR 2009", "place": "Elbow Lake, Minnesota" },
            "citation": { "source": "@S00002@", "page": "p. 1",
                          "dataText": "passed away April 9, 2009", "quay": 3 } } ] },
        { "item": 3, "target": "new @I00005@ / @F00004@", "ops": [
          { "op": "createOrUpdateSpouse", "person": "@I00001@",
            "spouse": { "xref": "@I00005@", "name": "Edith /Spouse/", "sex": "F",
                        "facts": [ { "fact": "DEAT", "value": { "date": "JAN 1971" },
                                     "citation": { "source": "@S00002@", "page": "p. 1",
                                                   "dataText": "Edith died", "quay": 2 } } ] },
            "family": "@F00004@",
            "marriage": { "date": "2 JUN 1949", "place": "Underwood, Minnesota",
                          "citations": [
                            { "source": "@S00001@", "page": "a", "dataText": "married", "quay": 2 },
                            { "source": "@S00002@", "page": "b", "dataText": "married too", "quay": 2 } ] },
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

    [Fact]
    public void Apply_SameChangesetTwice_SecondRunIsAllNoOps_AndByteIdentical()
    {
        WriteBaseFile();
        RunExpectSuccess(FullChangesetJson);
        byte[] afterFirst = ReadBytes();

        var second = RunExpectSuccess(FullChangesetJson);

        Assert.Empty(second.Deltas);
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
            "spouse": { "xref": "@I00050@", "name": "New /Wife/", "sex": "F" } } ] } ] }
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
