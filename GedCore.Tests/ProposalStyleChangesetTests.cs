using GedCore.Apply;

namespace GedCore.Tests;

/// <summary>
/// Composite changesets modeled on the shapes actually produced by the
/// research pipeline's proposals (research/proposals/*.changes.json):
/// a source-backed enrichment batch, a new-generation extend-ancestry item
/// (createOrUpdateParent + createOrUpdateSpouse + note in one item), a merge
/// with follow-up edits bundled into the same item (mergePerson plus a vital
/// update on the survivor plus an unrelated record's correction), and a
/// citation-consolidation correction batch (including a notes-only,
/// no-GEDCOM-change annotation item). Each test asserts every op in the
/// batch actually took effect, not just that the run succeeded.
/// </summary>
public class ProposalStyleChangesetTests : ApplyTestBase
{
    [Fact]
    public void EnrichBatch_AddsCitedDeathFacts_AndConflictNote_AcrossMultipleItems()
    {
        WriteBaseFile();

        var result = RunExpectSuccess(ChangesetFixtures.Load("batch-enrich-vitals"));

        Assert.Equal(2, result.Deltas["SOUR"]);
        var doc = ReadDoc();

        // @New1@/@New2@ mint @S00002@/@S00003@, given the base fixture's one
        // existing source and the newSources group applying before items.
        Assert.Equal("@S00002@", result.MintedXrefs["@New1@"]);
        Assert.Equal("@S00003@", result.MintedXrefs["@New2@"]);

        var allen = doc.ByXref["@I00001@"];
        var allenDeat = allen.FirstChild("DEAT")!;
        Assert.Equal("09 APR 1998", allenDeat.FirstChild("DATE")!.Value);
        Assert.Equal("Elbow Lake, Minnesota", allenDeat.FirstChild("PLAC")!.Value);
        Assert.Equal(["@S00001@", "@S00002@"], allenDeat.ChildrenByTag("SOUR").Select(s => s.Value).ToList());

        var edna = doc.ByXref["@I00004@"];
        var ednaDeat = edna.FirstChild("DEAT")!;
        Assert.Equal("Elbow Lake, Minnesota", ednaDeat.FirstChild("PLAC")!.Value);
        Assert.Null(ednaDeat.FirstChild("DATE"));
        Assert.Equal("@S00003@", ednaDeat.FirstChild("SOUR")!.Value);
        Assert.Contains("Not reconciled", edna.ChildrenByTag("NOTE").Single().Value);

        Assert.Equal("Test Grave Index", doc.ByXref["@S00002@"].FirstChild("AUTH")!.Value);
        Assert.Equal("Test Grave Index", doc.ByXref["@S00003@"].FirstChild("AUTH")!.Value);
    }

    [Fact]
    public void ExtendAncestryBatch_CreatesParentsAndFamily_InOneItem()
    {
        WriteBaseFile();

        var result = RunExpectSuccess(ChangesetFixtures.Load("batch-extend-ancestry-new-parents"));

        Assert.Equal(2, result.Deltas["INDI"]);
        Assert.Equal(1, result.Deltas["FAM"]);
        // George (@New1@) before family (@New2@) before Martha (@New3@) --
        // createOrUpdateParent mints person then family, then
        // createOrUpdateSpouse mints only the still-new spouse -- and the
        // source (@New4@) mints first of all since newSources applies before items.
        Assert.Equal(
            new Dictionary<string, string>
            {
                ["@New4@"] = "@S00002@", ["@New1@"] = "@I00005@",
                ["@New2@"] = "@F00004@", ["@New3@"] = "@I00006@",
            },
            result.MintedXrefs);
        var doc = ReadDoc();

        var fam = doc.ByXref["@F00004@"];
        Assert.Equal("@I00005@", fam.FirstChild("HUSB")!.Value);
        Assert.Equal("@I00006@", fam.FirstChild("WIFE")!.Value);
        Assert.Equal("@I00002@", fam.FirstChild("CHIL")!.Value);
        // father's and mother's ops cite the same source with the same combined
        // text, so they land on one shared citation node rather than duplicating it
        Assert.Equal("@S00002@", fam.ChildrenByTag("SOUR").Single().Value);

        var harvey = doc.ByXref["@I00002@"];
        Assert.Contains("@F00004@", harvey.ChildrenByTag("FAMC").Select(c => c.Value));
        Assert.Contains(harvey.ChildrenByTag("NOTE"), n => n.Value.Contains("1900 census"));

        var george = doc.ByXref["@I00005@"];
        Assert.Equal("George /Test/", george.FirstChild("NAME")!.Value);
        Assert.Equal("@F00004@", george.ChildrenByTag("FAMS").Single().Value);

        var martha = doc.ByXref["@I00006@"];
        Assert.Equal("Martha /Test/", martha.FirstChild("NAME")!.Value);
        Assert.Equal("@F00004@", martha.ChildrenByTag("FAMS").Single().Value);
    }

    [Fact]
    public void MergeBatch_FoldsDuplicate_AndAppliesFollowUpEdits_InTheSameItem()
    {
        WriteBaseFile();
        // seed a duplicate pair descending from Harvey, as ApplyMergeTests does,
        // standing in for "the same man entered twice"; @New1@/@New2@ mint
        // @F00004@/@I00005@, @New3@/@New4@ mint @F00005@/@I00006@ (person
        // before family within each op, base fixture's existing max is
        // @I00004@/@F00003@).
        RunExpectSuccess("""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateChild", "family": "@New1@", "husb": "@I00002@",
                "child": { "xref": "@New2@", "name": "John Lorenzo Dow /Test/", "sex": "M",
                           "facts": [ { "fact": "BIRT", "value": { "date": "14 JUL 1846" },
                                        "citation": { "source": "@S00001@", "page": "a",
                                                      "dataText": "born 1846", "quay": 1 } } ] } },
              { "op": "createOrUpdateChild", "family": "@New3@", "husb": "@I00002@",
                "child": { "xref": "@New4@", "name": "Lorenzo D. /Test/", "sex": "M",
                           "facts": [ { "fact": "BIRT", "value": { "date": "1849" },
                                        "citation": { "source": "@S00001@", "page": "b",
                                                      "dataText": "born abt 1849", "quay": 1 } } ] } } ] } ] }
            """);

        var result = RunExpectSuccess(ChangesetFixtures.Load("batch-merge-with-followup-edits"));

        Assert.Equal(-1, result.Deltas["INDI"]);
        var doc = ReadDoc();
        Assert.False(doc.ByXref.ContainsKey("@I00006@"));

        // merge: explicit NAME resolution, BIRT kept from survivor, merge note attached
        var survivor = doc.ByXref["@I00005@"];
        Assert.Equal("John Lorenzo Dow /Test/", survivor.FirstChild("NAME")!.Value);
        Assert.Equal("14 JUL 1846", survivor.FirstChild("BIRT")!.FirstChild("DATE")!.Value);
        Assert.Contains(survivor.ChildrenByTag("NOTE"), n => n.Value.Contains("Merged duplicate"));

        // follow-up on the survivor, in the same item as the merge
        var deat = survivor.FirstChild("DEAT")!;
        Assert.Equal("08 NOV 1998", deat.FirstChild("DATE")!.Value);
        Assert.Equal("Deering, New Hampshire", deat.FirstChild("PLAC")!.Value);

        // unrelated record's correction, also bundled into the same item
        Assert.Equal("Nellie /Corrected/", doc.ByXref["@I00003@"].FirstChild("NAME")!.Value);
    }

    [Fact]
    public void CorrectionsBatch_ConsolidatesCitation_AddsNote_DropsSuperseded_WithNoOpAnnotationItem()
    {
        WriteBaseFile();

        var result = RunExpectSuccess(ChangesetFixtures.Load("batch-corrections-citations-and-notes"));

        Assert.Contains(result.Log, l => l.Contains("item 1: applied"));   // the empty-ops annotation item still logs
        var doc = ReadDoc();

        // @New1@ mints @S00002@ given the base fixture's one existing source.
        Assert.Equal("@S00002@", result.MintedXrefs["@New1@"]);

        // citation consolidated onto BIRT, then the superseded one dropped
        var birt = doc.ByXref["@I00001@"].FirstChild("BIRT")!;
        Assert.Equal("@S00002@", birt.ChildrenByTag("SOUR").Single().Value);

        Assert.Contains(doc.ByXref["@I00003@"].ChildrenByTag("NOTE"),
            n => n.Value.Contains("Identity anchor for Nellie"));

        Assert.Equal(1, result.Deltas["SOUR"]);
    }

    [Fact]
    public void MediaBatch_CreatesObjects_EscapesPaths_AttachesPortraitsAndFamilyLink_AllocatesOmittedXref()
    {
        WriteBaseFile();

        var result = RunExpectSuccess(ChangesetFixtures.Load("batch-add-family-photos"));

        Assert.Equal(3, result.Deltas["OBJE"]);
        // @New1@/@New2@ mint @M00001@/@M00002@ (the base fixture carries no
        // OBJE at all); the omitted-xref tintype op is content-addressed,
        // unaffected by placeholders, and allocates the next free @M...@
        // once those two already exist: @M00003@.
        Assert.Equal("@M00001@", result.MintedXrefs["@New1@"]);
        Assert.Equal("@M00002@", result.MintedXrefs["@New2@"]);
        var doc = ReadDoc();

        // @M00001@ — title, two files; the space-containing path is percent-escaped
        // on write, and the composer's bare "photos/..." path gains the GEDCOM 7
        // §2.12-recommended "media/" prefix it omitted (MediaFileRequest.NormalizePath).
        var wedding = doc.ByXref["@M00001@"];
        Assert.Equal("Allen and Edna's wedding, 1902", wedding.FirstChild("TITL")!.Value);
        var files = wedding.ChildrenByTag("FILE").ToList();
        Assert.Equal(2, files.Count);
        Assert.Equal("media/photos/allen-edna-wedding.jpg", files[0].Value);
        Assert.Equal("image/jpeg", files[0].FirstChild("FORM")!.Value);
        Assert.Equal("PHOTO", files[0].FirstChild("FORM")!.FirstChild("MEDI")!.Value);
        Assert.Equal("Wedding portrait", files[0].FirstChild("TITL")!.Value);
        Assert.Equal("media/documents/wedding%20program%201902.pdf", files[1].Value);

        // @M00002@ — absolute URL stored verbatim, never escaped, never prefixed
        Assert.Equal("https://example.org/photos/edna-1990.jpg",
            doc.ByXref["@M00002@"].ChildrenByTag("FILE").Single().Value);

        // omitted xref — content-addressed op allocated the next free @M…@
        var tintype = doc.ByXref["@M00003@"];
        Assert.Equal("media/photos/harvey-tintype.jpg", tintype.ChildrenByTag("FILE").Single().Value);

        // portrait attach = first OBJE link on the person
        Assert.Equal("@M00001@", doc.ByXref["@I00001@"].ChildrenByTag("OBJE").First().Value);
        Assert.Equal("@M00002@", doc.ByXref["@I00004@"].ChildrenByTag("OBJE").First().Value);
        Assert.Equal("@M00003@", doc.ByXref["@I00002@"].ChildrenByTag("OBJE").Single().Value);

        // family link carries its per-link title override
        var famLink = doc.ByXref["@F00002@"].ChildrenByTag("OBJE").Single();
        Assert.Equal("@M00001@", famLink.Value);
        Assert.Equal("Wedding day", famLink.FirstChild("TITL")!.Value);

        // The omitted-xref (content-addressed) op is still fully idempotent —
        // re-submitting just that one op resolves to the same @M00003@ record.
        var tintypeRerun = RunExpectSuccess("""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateMedia",
                "files": [ { "path": "photos/harvey-tintype.jpg", "mediaType": "image/jpeg", "medium": "PHOTO" } ],
                "attachTo": [ { "person": "@I00002@" } ] } ] } ] }
            """);
        Assert.Equal(0, tintypeRerun.Deltas.GetValueOrDefault("OBJE"));
        Assert.Contains(tintypeRerun.Log, l => l.Contains("no-op (already matches)"));

        // The explicit-placeholder-xref media in newSources is not idempotent
        // across separate apply invocations (rerun identity is scoped to
        // person duplicate detection): re-running the whole batch mints a
        // second wedding photo and a second later-years photo, distinct from
        // the first run's @M00001@/@M00002@.
        var rerun = RunExpectSuccess(ChangesetFixtures.Load("batch-add-family-photos"));
        Assert.Equal("@M00004@", rerun.MintedXrefs["@New1@"]);
        Assert.Equal("@M00005@", rerun.MintedXrefs["@New2@"]);
    }
}
