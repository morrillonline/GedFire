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
                "child": { "xref": "@I00006@", "name": "Junior /Test/", "sex": "M",
                           "facts": [ { "fact": "BIRT", "value": { "date": "1951" },
                                        "citation": { "source": "@S00001@", "page": "p. 5",
                                                      "dataText": "son, born 1951", "quay": 3 } } ] },
                "citation": { "source": "@S00001@", "page": "p. 5",
                              "dataText": "surviving children include Junior", "quay": 3 } } ] } ] }
            """;

        var result = RunExpectSuccess(json);

        Assert.Equal(1, result.Deltas["INDI"]);
        var doc = ReadDoc();
        var fam = doc.ByXref["@F00002@"];
        Assert.Equal("@I00006@", fam.ChildrenByTag("CHIL").Single().Value);
        Assert.Equal("@S00001@", fam.FirstChild("SOUR")!.Value);   // FAM-level provenance
        var junior = doc.ByXref["@I00006@"];
        Assert.Equal("@F00002@", junior.ChildrenByTag("FAMC").Single().Value);
        Assert.Equal("1951", junior.FirstChild("BIRT")!.FirstChild("DATE")!.Value);
        Assert.NotNull(junior.FirstChild("UID"));

        // re-run: inline xref exists with the identical name → degrades to a no-op link
        var second = RunExpectSuccess(json);
        Assert.Contains(second.Log, l => l.Contains("no changes; file untouched"));
    }

    [Fact]
    public void Child_InlinePerson_AllowsUncitedVital()
    {
        WriteBaseFile();

        RunExpectSuccess("""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateChild", "family": "@F00002@",
                "child": { "xref": "@I00006@", "name": "Junior /Test/", "sex": "M",
                           "facts": [ { "fact": "BIRT", "value": { "date": "1951" } } ] },
                "citation": { "source": "@S00001@", "page": "p. 5",
                              "dataText": "surviving children include Junior", "quay": 3 } } ] } ] }
            """);

        var birth = ReadDoc().ByXref["@I00006@"].FirstChild("BIRT")!;
        Assert.Equal("1951", birth.FirstChild("DATE")!.Value);
        Assert.Null(birth.FirstChild("SOUR"));
    }

    [Fact]
    public void Child_NewFamilyXref_CreatesFamilyWithSeedPartners()
    {
        WriteBaseFile();

        var result = RunExpectSuccess("""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateChild", "family": "@F00005@",
                "husb": "@I00002@", "wife": "@I00004@",
                "child": { "xref": "@I00006@", "name": "Kid /Test/", "sex": "F" } } ] } ] }
            """);

        Assert.Equal(1, result.Deltas["INDI"]);
        Assert.Equal(1, result.Deltas["FAM"]);
        var doc = ReadDoc();
        var fam = doc.ByXref["@F00005@"];
        Assert.Equal("@I00002@", fam.FirstChild("HUSB")!.Value);
        Assert.Equal("@I00004@", fam.FirstChild("WIFE")!.Value);
        Assert.Equal("@I00006@", fam.ChildrenByTag("CHIL").Single().Value);
        Assert.NotNull(fam.FirstChild("UID"));
        Assert.Contains("@F00005@", doc.ByXref["@I00002@"].ChildrenByTag("FAMS").Select(c => c.Value));
        Assert.Contains("@F00005@", doc.ByXref["@I00004@"].ChildrenByTag("FAMS").Select(c => c.Value));
        Assert.Equal("@F00005@", doc.ByXref["@I00006@"].ChildrenByTag("FAMC").Single().Value);
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
                "parent": { "xref": "@I00006@", "name": "Newman /Test/", "sex": "M" },
                "citation": { "source": "@S00001@", "page": "p. 7",
                              "dataText": "son of Newman Test", "quay": 2 } } ] } ] }
            """);

        var doc = ReadDoc();
        var fam = doc.ByXref["@F00001@"];
        Assert.Equal("@I00006@", fam.FirstChild("HUSB")!.Value);
        Assert.Equal("@S00001@", fam.FirstChild("SOUR")!.Value);   // FAM-level provenance
        Assert.Empty(doc.ByXref["@I00002@"].ChildrenByTag("FAMS"));            // replaced father
        Assert.Contains("@F00001@", doc.ByXref["@I00006@"].ChildrenByTag("FAMS").Select(c => c.Value));
        Assert.Contains(result.Log, l => l.Contains("@I00002@ → @I00006@"));
    }

    [Fact]
    public void Parent_NoParentFamily_CreatesOne_WithPersonAsChild()
    {
        WriteBaseFile();

        var result = RunExpectSuccess("""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateParent", "person": "@I00004@", "role": "mother",
                "parent": { "xref": "@I00006@", "name": "Grandma /Test/", "sex": "F" },
                "family": "@F00005@" } ] } ] }
            """);

        Assert.Equal(1, result.Deltas["INDI"]);
        Assert.Equal(1, result.Deltas["FAM"]);
        var doc = ReadDoc();
        var fam = doc.ByXref["@F00005@"];
        Assert.Equal("@I00006@", fam.FirstChild("WIFE")!.Value);
        Assert.Equal("@I00004@", fam.ChildrenByTag("CHIL").Single().Value);
        Assert.Equal("@F00005@", doc.ByXref["@I00004@"].ChildrenByTag("FAMC").Single().Value);
        Assert.Contains("@F00005@", doc.ByXref["@I00006@"].ChildrenByTag("FAMS").Select(c => c.Value));
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
                "parent": { "xref": "@I00006@", "name": "Grandpa /Test/", "sex": "M" },
                "family": "@F00005@" },
              { "op": "createOrUpdateParent", "person": "@I00004@", "role": "mother",
                "parent": { "xref": "@I00007@", "name": "Grandma /Test/", "sex": "F" },
                "family": "@F00005@" } ] } ] }
            """);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e =>
            e.Contains("apply-time invariant violated") && e.Contains("@F00005@"));
        Assert.Equal(original, ReadBytes());   // untouched, not a partial write
    }

    [Fact]
    public void Parent_SecondParentOnFreshlyCreatedFamily_ViaCreateOrUpdateSpouse_Works()
    {
        WriteBaseFile();

        var result = RunExpectSuccess("""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateParent", "person": "@I00004@", "role": "father",
                "parent": { "xref": "@I00006@", "name": "Grandpa /Test/", "sex": "M" },
                "family": "@F00005@" },
              { "op": "createOrUpdateSpouse", "person": "@I00006@",
                "spouse": { "xref": "@I00007@", "name": "Grandma /Test/", "sex": "F" },
                "family": "@F00005@" } ] } ] }
            """);

        var doc = ReadDoc();
        var fam = doc.ByXref["@F00005@"];
        Assert.Equal("@I00006@", fam.FirstChild("HUSB")!.Value);
        Assert.Equal("@I00007@", fam.FirstChild("WIFE")!.Value);
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
                "parent": { "xref": "@I00006@", "name": "Grandpa /Test/", "sex": "M" },
                "family": "@F00005@",
                "citation": { "source": "@S00001@", "page": "p. 4",
                              "dataText": "father: Grandpa", "quay": 2 } },
              { "op": "createOrUpdateSpouse", "person": "@I00006@",
                "spouse": { "xref": "@I00007@", "name": "Grandma /Test/", "sex": "F" },
                "family": "@F00005@",
                "citation": { "source": "@S00001@", "page": "p. 4",
                              "dataText": "mother: Grandma", "quay": 2 } } ] } ] }
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
