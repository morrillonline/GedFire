namespace GedCore.Tests;

/// <summary>
/// Per-op tests for the relationship nouns (Spouse, Child, Parent): family
/// resolution, inline-person materialization, partner replacement with
/// back-link cleanup, FAM-level provenance, and empty-family deletion.
/// </summary>
public class ApplyRelationshipTests : ApplyTestBase
{
    // -------------------------------------------------------------------------
    // Spouse
    // -------------------------------------------------------------------------

    [Fact]
    public void Spouse_ExistingSharedFamily_AddsMarriageOnly()
    {
        WriteBaseFile();
        var changeset = ChangesetFixtures.Load("create-or-update-spouse");

        var result = RunExpectSuccess(changeset);

        Assert.Empty(result.Deltas);   // no records created — F00001 already links them
        var fam = ReadDoc().ByXref["@F00001@"];
        var marr = fam.FirstChild("MARR")!;
        Assert.Equal("12 JUN 1922", marr.FirstChild("DATE")!.Value);
        Assert.Equal("@S00001@", marr.FirstChild("SOUR")!.Value);

        var second = RunExpectSuccess(changeset);
        Assert.Contains(second.Log, l => l.Contains("no changes; file untouched"));
    }

    [Fact]
    public void Spouse_ReplacesDifferentSpouse_InNamedFamily_CleansOldFams()
    {
        WriteBaseFile();

        var result = RunExpectSuccess("""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateSpouse", "person": "@I00002@", "spouse": "@I00004@",
                "family": "@F00001@" } ] } ] }
            """);

        var doc = ReadDoc();
        Assert.Equal("@I00004@", doc.ByXref["@F00001@"].FirstChild("WIFE")!.Value);
        // replaced wife loses her FAMS back-link; new wife gains one
        Assert.Empty(doc.ByXref["@I00003@"].ChildrenByTag("FAMS"));
        Assert.Contains("@F00001@", doc.ByXref["@I00004@"].ChildrenByTag("FAMS").Select(c => c.Value));
        Assert.Contains(result.Log, l => l.Contains("@I00003@ → @I00004@"));
    }

    [Fact]
    public void DeleteSpouse_RemovesLinkAndFamsBackLink_FamilySurvivesWithOthers()
    {
        WriteBaseFile();

        RunExpectSuccess("""
            { "items": [ { "item": 1, "ops": [
              { "op": "deleteSpouse", "person": "@I00002@", "spouse": "@I00003@" } ] } ] }
            """);

        var doc = ReadDoc();
        var fam = doc.ByXref["@F00001@"];                 // survives: HUSB + CHIL remain
        Assert.Null(fam.FirstChild("WIFE"));
        Assert.Equal("@I00002@", fam.FirstChild("HUSB")!.Value);
        Assert.Empty(doc.ByXref["@I00003@"].ChildrenByTag("FAMS"));
    }

    [Fact]
    public void DeleteSpouse_EmptiedFamily_IsDeleted_WithNegativeDelta()
    {
        WriteBaseFile();

        var result = RunExpectSuccess("""
            { "items": [ { "item": 1, "ops": [
              { "op": "deleteSpouse", "person": "@I00001@", "spouse": "@I00004@",
                "family": "@F00003@" },
              { "op": "deleteSpouse", "person": "@I00004@", "spouse": "@I00001@",
                "family": "@F00003@" } ] } ] }
            """);

        Assert.Equal(-1, result.Deltas["FAM"]);
        var doc = ReadDoc();
        Assert.False(doc.ByXref.ContainsKey("@F00003@"));
        // both ex-partners keep only their @F00002@ FAMS
        Assert.Equal(["@F00002@"], doc.ByXref["@I00001@"].ChildrenByTag("FAMS").Select(c => c.Value));
        Assert.Equal(["@F00002@"], doc.ByXref["@I00004@"].ChildrenByTag("FAMS").Select(c => c.Value));
    }

    // -------------------------------------------------------------------------
    // Child
    // -------------------------------------------------------------------------

    [Fact]
    public void Child_InlinePerson_AddedToExistingFamily_WithFamLevelCitation()
    {
        WriteBaseFile();
        const string json = """
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateChild", "family": "@F00002@",
                "child": { "xref": "@NewI1@", "name": "Junior /Test/", "sex": "M",
                           "facts": [ { "fact": "BIRT", "value": { "date": "1951" },
                                        "citation": { "source": "@S00001@", "page": "p. 5",
                                                      "dataText": "son, born 1951", "quay": 3 } } ] },
                "citation": { "source": "@S00001@", "page": "p. 5",
                              "dataText": "surviving children include Junior", "quay": 3 } } ] } ] }
            """;

        var result = RunExpectSuccess(json);

        Assert.Equal(1, result.Deltas["INDI"]);
        Assert.Equal("@I00005@", result.MintedXrefs["@NewI1@"]);
        var doc = ReadDoc();
        var fam = doc.ByXref["@F00002@"];
        Assert.Equal("@I00005@", fam.ChildrenByTag("CHIL").Single().Value);
        Assert.Equal("@S00001@", fam.FirstChild("SOUR")!.Value);   // FAM-level provenance
        var junior = doc.ByXref["@I00005@"];
        Assert.Equal("@F00002@", junior.ChildrenByTag("FAMC").Single().Value);
        Assert.Equal("1951", junior.FirstChild("BIRT")!.FirstChild("DATE")!.Value);
        Assert.NotNull(junior.FirstChild("UID"));
    }

    [Fact]
    public void Child_InlinePerson_AllowsUncitedVital()
    {
        WriteBaseFile();

        RunExpectSuccess("""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateChild", "family": "@F00002@",
                "child": { "xref": "@NewI1@", "name": "Junior /Test/", "sex": "M",
                           "facts": [ { "fact": "BIRT", "value": { "date": "1951" } } ] },
                "citation": { "source": "@S00001@", "page": "p. 5",
                              "dataText": "surviving children include Junior", "quay": 3 } } ] } ] }
            """);

        var birth = ReadDoc().ByXref["@I00005@"].FirstChild("BIRT")!;
        Assert.Equal("1951", birth.FirstChild("DATE")!.Value);
        Assert.Null(birth.FirstChild("SOUR"));
    }

    [Fact]
    public void Child_NewFamilyXref_CreatesFamilyWithSeedPartners()
    {
        WriteBaseFile();

        var result = RunExpectSuccess("""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateChild", "family": "@NewF1@",
                "husb": "@I00002@", "wife": "@I00004@",
                "child": { "xref": "@NewI1@", "name": "Kid /Test/", "sex": "F" } } ] } ] }
            """);

        Assert.Equal(1, result.Deltas["INDI"]);
        Assert.Equal(1, result.Deltas["FAM"]);
        Assert.Equal("@F00004@", result.MintedXrefs["@NewF1@"]);
        Assert.Equal("@I00005@", result.MintedXrefs["@NewI1@"]);
        var doc = ReadDoc();
        var fam = doc.ByXref["@F00004@"];
        Assert.Equal("@I00002@", fam.FirstChild("HUSB")!.Value);
        Assert.Equal("@I00004@", fam.FirstChild("WIFE")!.Value);
        Assert.Equal("@I00005@", fam.ChildrenByTag("CHIL").Single().Value);
        Assert.NotNull(fam.FirstChild("UID"));
        Assert.Contains("@F00004@", doc.ByXref["@I00002@"].ChildrenByTag("FAMS").Select(c => c.Value));
        Assert.Contains("@F00004@", doc.ByXref["@I00004@"].ChildrenByTag("FAMS").Select(c => c.Value));
        Assert.Equal("@F00004@", doc.ByXref["@I00005@"].ChildrenByTag("FAMC").Single().Value);
    }

    [Fact]
    public void DeleteChild_RemovesChilAndFamcBackLink()
    {
        WriteBaseFile();

        RunExpectSuccess("""
            { "items": [ { "item": 1, "ops": [
              { "op": "deleteChild", "family": "@F00001@", "child": "@I00001@" } ] } ] }
            """);

        var doc = ReadDoc();
        Assert.Empty(doc.ByXref["@F00001@"].ChildrenByTag("CHIL"));
        Assert.Empty(doc.ByXref["@I00001@"].ChildrenByTag("FAMC"));
        Assert.True(doc.ByXref.ContainsKey("@F00001@"));   // partners remain
    }

    // -------------------------------------------------------------------------
    // Parent
    // -------------------------------------------------------------------------

    [Fact]
    public void Parent_SameParent_IsNoOp()
    {
        byte[] original = WriteBaseFile();

        var result = RunExpectSuccess("""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateParent", "person": "@I00001@", "role": "father",
                "parent": "@I00002@" } ] } ] }
            """);

        Assert.Contains(result.Log, l => l.Contains("no changes; file untouched"));
        Assert.Equal(original, ReadBytes());
    }

    [Fact]
    public void Parent_ReplacesDifferentFather_CleansOldFams()
    {
        WriteBaseFile();

        var result = RunExpectSuccess("""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateParent", "person": "@I00001@", "role": "father",
                "parent": { "xref": "@NewI1@", "name": "Newman /Test/", "sex": "M" },
                "citation": { "source": "@S00001@", "page": "p. 7",
                              "dataText": "son of Newman Test", "quay": 2 } } ] } ] }
            """);

        Assert.Equal("@I00005@", result.MintedXrefs["@NewI1@"]);
        var doc = ReadDoc();
        var fam = doc.ByXref["@F00001@"];
        Assert.Equal("@I00005@", fam.FirstChild("HUSB")!.Value);
        Assert.Equal("@S00001@", fam.FirstChild("SOUR")!.Value);   // FAM-level provenance
        Assert.Empty(doc.ByXref["@I00002@"].ChildrenByTag("FAMS"));            // replaced father
        Assert.Contains("@F00001@", doc.ByXref["@I00005@"].ChildrenByTag("FAMS").Select(c => c.Value));
        Assert.Contains(result.Log, l => l.Contains("@I00002@ → @I00005@"));
    }

    [Fact]
    public void Parent_NoParentFamily_CreatesOne_WithPersonAsChild()
    {
        WriteBaseFile();

        var result = RunExpectSuccess("""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateParent", "person": "@I00004@", "role": "mother",
                "parent": { "xref": "@NewI1@", "name": "Grandma /Test/", "sex": "F" },
                "family": "@NewF1@" } ] } ] }
            """);

        Assert.Equal(1, result.Deltas["INDI"]);
        Assert.Equal(1, result.Deltas["FAM"]);
        Assert.Equal("@I00005@", result.MintedXrefs["@NewI1@"]);
        Assert.Equal("@F00004@", result.MintedXrefs["@NewF1@"]);
        var doc = ReadDoc();
        var fam = doc.ByXref["@F00004@"];
        Assert.Equal("@I00005@", fam.FirstChild("WIFE")!.Value);
        Assert.Equal("@I00004@", fam.ChildrenByTag("CHIL").Single().Value);
        Assert.Equal("@F00004@", doc.ByXref["@I00004@"].ChildrenByTag("FAMC").Single().Value);
        Assert.Contains("@F00004@", doc.ByXref["@I00005@"].ChildrenByTag("FAMS").Select(c => c.Value));
    }

    /// <summary>
    /// Known limitation, not a crash: a second createOrUpdateParent op that
    /// targets the *same brand-new family* a prior op in this run just created
    /// fails cleanly instead of segfaulting. The FAMC back-link that would make
    /// the family "belong" to the person is deferred to end-of-run, so
    /// Resolve.ParentFamily's ownership check (correctly) can't yet see it.
    /// Two new parents on one person: create the family with the first parent,
    /// then add the second with createOrUpdateSpouse naming the same family
    /// (see the sibling test below) — that path resolves the family by xref
    /// directly, without the ownership check.
    /// </summary>
    [Fact]
    public void Parent_SecondParentOpOnFreshlyCreatedFamily_FailsCleanly_NotCrash()
    {
        byte[] original = WriteBaseFile();

        var result = Run("""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateParent", "person": "@I00004@", "role": "father",
                "parent": { "xref": "@NewI1@", "name": "Cornelius /Ashworth/", "sex": "M" },
                "family": "@NewF1@" },
              { "op": "createOrUpdateParent", "person": "@I00004@", "role": "mother",
                "parent": { "xref": "@NewI2@", "name": "Beatrice /Fenwick/", "sex": "F" },
                "family": "@NewF1@" } ] } ] }
            """);

        Assert.False(result.Success);
        // @F00004@ is the real xref @NewF1@ mints during the first op — the
        // second op's apply-time re-resolution disagrees with validation's
        // plan-based one, exactly as it did under the old caller-chosen-xref
        // dialect, just reported by the now-real minted xref.
        Assert.Contains(result.Errors, e =>
            e.Contains("apply-time invariant violated") && e.Contains("@F00004@"));
        Assert.Equal(original, ReadBytes());   // untouched, not a partial write
    }

    [Fact]
    public void Parent_SecondParentOnFreshlyCreatedFamily_ViaCreateOrUpdateSpouse_Works()
    {
        WriteBaseFile();

        var result = RunExpectSuccess("""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateParent", "person": "@I00004@", "role": "father",
                "parent": { "xref": "@NewI1@", "name": "Cornelius /Ashworth/", "sex": "M" },
                "family": "@NewF1@" },
              { "op": "createOrUpdateSpouse", "person": "@NewI1@",
                "spouse": { "xref": "@NewI2@", "name": "Beatrice /Fenwick/", "sex": "F" },
                "family": "@NewF1@" } ] } ] }
            """);

        Assert.Equal("@I00005@", result.MintedXrefs["@NewI1@"]);
        Assert.Equal("@I00006@", result.MintedXrefs["@NewI2@"]);
        Assert.Equal("@F00004@", result.MintedXrefs["@NewF1@"]);
        var doc = ReadDoc();
        var fam = doc.ByXref["@F00004@"];
        Assert.Equal("@I00005@", fam.FirstChild("HUSB")!.Value);
        Assert.Equal("@I00006@", fam.FirstChild("WIFE")!.Value);
        Assert.Equal("@I00004@", fam.ChildrenByTag("CHIL").Single().Value);
        Assert.Equal(2, result.Deltas["INDI"]);
        Assert.Equal(1, result.Deltas["FAM"]);
    }

    /// <summary>
    /// Both parents added citing the same "Parents" source on the shared FAM,
    /// but with father-only vs mother-only extracts. The two ops upsert the
    /// same SOUR node on the family; the second must not silently overwrite the
    /// first's DATA.TEXT — the run fails cleanly so the composer combines the
    /// two extracts into one citation instead of losing one.
    /// </summary>
    [Fact]
    public void Parent_TwoParentsConflictingProvenanceOnFamily_FailsCleanly()
    {
        byte[] original = WriteBaseFile();

        var result = Run("""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateParent", "person": "@I00004@", "role": "father",
                "parent": { "xref": "@NewI1@", "name": "Cornelius /Ashworth/", "sex": "M" },
                "family": "@NewF1@",
                "citation": { "source": "@S00001@", "page": "p. 4",
                              "dataText": "father: Cornelius", "quay": 2 } },
              { "op": "createOrUpdateSpouse", "person": "@NewI1@",
                "spouse": { "xref": "@NewI2@", "name": "Beatrice /Fenwick/", "sex": "F" },
                "family": "@NewF1@",
                "citation": { "source": "@S00001@", "page": "p. 4",
                              "dataText": "mother: Beatrice", "quay": 2 } } ] } ] }
            """);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e =>
            e.Contains("apply-time invariant violated") && e.Contains("DATA.TEXT"));
        Assert.Equal(original, ReadBytes());
    }

    [Fact]
    public void DeleteParent_RemovesRoleLinkAndFams()
    {
        WriteBaseFile();

        RunExpectSuccess("""
            { "items": [ { "item": 1, "ops": [
              { "op": "deleteParent", "person": "@I00001@", "role": "mother" } ] } ] }
            """);

        var doc = ReadDoc();
        Assert.Null(doc.ByXref["@F00001@"].FirstChild("WIFE"));
        Assert.Empty(doc.ByXref["@I00003@"].ChildrenByTag("FAMS"));
        Assert.True(doc.ByXref.ContainsKey("@F00001@"));   // father + child remain
    }
}
