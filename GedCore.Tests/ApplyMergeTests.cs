namespace GedCore.Tests;

/// <summary>
/// Tests for mergePerson: the third verb (alongside CreateOrUpdate and
/// Delete). Each test creates its own throwaway duplicate pair via
/// createOrUpdateChild/Parent so the shared base fixture (relied on by
/// record-count/order assertions elsewhere) stays untouched.
/// </summary>
public class ApplyMergeTests : ApplyTestBase
{
    /// <summary>
    /// Seeds two stub people who both descend from @I00002@ (Harvey) via
    /// separate throwaway families, standing in for "the same man entered
    /// twice": @I00005@ (kept vitals) and @I00006@ (kept relationship link).
    /// The placeholders mint deterministically given the base fixture's
    /// existing @I0000[1-4]@/@F0000[1-3]@ records: person before family
    /// within each op (createOrUpdateChild.Apply mints the child first), so
    /// @NewI1@→@I00005@, @NewF1@→@F00004@, @NewI2@→@I00006@, @NewF2@→@F00005@.
    /// </summary>
    private void SeedDuplicatePair()
    {
        WriteBaseFile();
        RunExpectSuccess("""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateChild", "family": "@NewF1@", "husb": "@I00002@",
                "child": { "xref": "@NewI1@", "name": "Duplicate /Person/", "sex": "M",
                           "facts": [ { "fact": "BIRT", "value": { "date": "1846" },
                                        "citation": { "source": "@S00001@", "page": "a",
                                                      "dataText": "born 1846", "quay": 1 } } ] } },
              { "op": "createOrUpdateChild", "family": "@NewF2@", "husb": "@I00002@",
                "child": { "xref": "@NewI2@", "name": "Duplicate /Twin/", "sex": "M",
                           "facts": [ { "fact": "BIRT", "value": { "date": "1849" },
                                        "citation": { "source": "@S00001@", "page": "b",
                                                      "dataText": "born abt 1849", "quay": 1 } } ] } } ] } ] }
            """);
    }

    [Fact]
    public void Merge_UnionsNonConflictingFacts_RelocatesFamc_RedirectsPointer_DeletesDuplicate()
    {
        SeedDuplicatePair();
        // give @I00006@ a fact @I00005@ lacks, so union is exercised
        RunExpectSuccess("""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateVital", "record": "@I00006@", "fact": "DEAT",
                "value": { "date": "1900" },
                "citation": { "source": "@S00001@", "page": "c", "dataText": "died 1900", "quay": 1 } } ] } ] }
            """);

        var result = RunExpectSuccess("""
            { "items": [ { "item": 1, "ops": [
              { "op": "mergePerson", "survivor": "@I00005@", "duplicate": "@I00006@",
                "facts": [ { "fact": "BIRT", "keep": "survivor" },
                           { "fact": "NAME", "keep": "survivor" } ],
                "note": "Merged per test evidence review." } ] } ] }
            """);

        Assert.Equal(-1, result.Deltas["INDI"]);
        var doc = ReadDoc();
        Assert.False(doc.ByXref.ContainsKey("@I00006@"));

        var survivor = doc.ByXref["@I00005@"];
        Assert.Equal("1846", survivor.FirstChild("BIRT")!.FirstChild("DATE")!.Value);   // kept, per resolution
        Assert.Equal("1900", survivor.FirstChild("DEAT")!.FirstChild("DATE")!.Value);   // unioned from duplicate
        Assert.Contains("Merged per test evidence review.", survivor.ChildrenByTag("NOTE").Select(n => n.Value));

        // duplicate's FAMC relocated onto survivor (alongside its own @F00004@); FAM's CHIL redirected
        Assert.Contains("@F00005@", survivor.ChildrenByTag("FAMC").Select(c => c.Value));
        var fam11 = doc.ByXref["@F00005@"];
        Assert.Equal("@I00005@", fam11.ChildrenByTag("CHIL").Single().Value);
    }

    [Fact]
    public void Merge_ExplicitValueResolution_ForName_StaysFirstChild()
    {
        SeedDuplicatePair();

        RunExpectSuccess("""
            { "items": [ { "item": 1, "ops": [
              { "op": "mergePerson", "survivor": "@I00005@", "duplicate": "@I00006@",
                "facts": [ { "fact": "NAME", "keep": "value", "value": "Merged /Name/" },
                           { "fact": "BIRT", "keep": "survivor" } ] } ] } ] }
            """);

        var survivor = ReadDoc().ByXref["@I00005@"];
        Assert.Equal("NAME", survivor.Children[0].Tag);
        Assert.Equal("Merged /Name/", survivor.Children[0].Value);
    }

    [Fact]
    public void Merge_ExplicitValueResolution_ReplacesBothSides_WithNewCitation()
    {
        SeedDuplicatePair();

        RunExpectSuccess("""
            { "newSources": [ { "xref": "@NewS1@", "ops": [
                { "op": "createOrUpdateSource", "xref": "@NewS1@", "title": "Death record" } ] } ],
              "items": [ { "item": 1, "ops": [
              { "op": "mergePerson", "survivor": "@I00005@", "duplicate": "@I00006@",
                "facts": [ { "fact": "BIRT", "keep": "value", "value": { "date": "14 JUL 1846" },
                             "citation": { "source": "@NewS1@", "page": "p1",
                                           "dataText": "age at death implies 1846", "quay": 2 } },
                           { "fact": "NAME", "keep": "survivor" } ] } ] } ] }
            """);

        var birt = ReadDoc().ByXref["@I00005@"].FirstChild("BIRT")!;
        Assert.Equal("14 JUL 1846", birt.FirstChild("DATE")!.Value);
        Assert.Equal("@S00002@", birt.FirstChild("SOUR")!.Value);
    }

    [Fact]
    public void Merge_KeepDuplicate_CarriesOverDuplicatesFactAndCitation()
    {
        SeedDuplicatePair();

        RunExpectSuccess("""
            { "items": [ { "item": 1, "ops": [
              { "op": "mergePerson", "survivor": "@I00005@", "duplicate": "@I00006@",
                "facts": [ { "fact": "BIRT", "keep": "duplicate" },
                           { "fact": "NAME", "keep": "survivor" } ] } ] } ] }
            """);

        var birt = ReadDoc().ByXref["@I00005@"].FirstChild("BIRT")!;
        Assert.Equal("1849", birt.FirstChild("DATE")!.Value);
        Assert.Equal("@S00001@", birt.FirstChild("SOUR")!.Value);
    }

    [Fact]
    public void Merge_ConflictingFactWithoutResolution_FailsValidation()
    {
        SeedDuplicatePair();
        byte[] before = ReadBytes();

        var result = Run("""
            { "items": [ { "item": 1, "ops": [
              { "op": "mergePerson", "survivor": "@I00005@", "duplicate": "@I00006@" } ] } ] }
            """);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("BIRT differs"));
        Assert.Equal(before, ReadBytes());
    }

    [Fact]
    public void Merge_DuplicateAlreadyGone_IsNoOp_Idempotent()
    {
        SeedDuplicatePair();
        const string json = """
            { "items": [ { "item": 1, "ops": [
              { "op": "mergePerson", "survivor": "@I00005@", "duplicate": "@I00006@",
                "facts": [ { "fact": "BIRT", "keep": "survivor" },
                           { "fact": "NAME", "keep": "survivor" } ] } ] } ] }
            """;
        RunExpectSuccess(json);
        byte[] afterFirst = ReadBytes();

        var second = RunExpectSuccess(json);
        Assert.Contains(second.Log, l => l.Contains("no-op (duplicate already gone)"));
        Assert.Equal(afterFirst, ReadBytes());
    }

    [Fact]
    public void Merge_SameXrefTwice_FailsValidation()
    {
        WriteBaseFile();
        var result = Run("""
            { "items": [ { "item": 1, "ops": [
              { "op": "mergePerson", "survivor": "@I00001@", "duplicate": "@I00001@" } ] } ] }
            """);
        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("same xref"));
    }
}
