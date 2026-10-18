using GedCore.Apply;

namespace GedCore.Tests;

public class TreeChangesTests : ApplyTestBase
{
    TreeChanges DryRunChanges(string opsJson)
    {
        WriteBaseFile();
        var result = Run($$"""{ "items": [ { "item": 1, "ops": [ {{opsJson}} ] } ] }""", dryRun: true);
        Assert.True(result.Success, string.Join("; ", result.Errors));
        return result.Changes;
    }

    [Fact]
    public void NewFact_IsReportedAsWrittenOnItsRecord()
    {
        var changes = DryRunChanges("""
            { "op": "createOrUpdateVital", "record": "@I00002@", "fact": "DEAT",
              "value": { "date": "3 MAR 1950", "place": "Duluth, Minnesota" } }
            """);

        var write = Assert.Single(changes.Facts);
        Assert.Equal(new FactWrite("@I00002@", "DEAT", "03 MAR 1950", "Duluth, Minnesota"), write);
    }

    [Fact]
    public void CitationOnAnExistingFact_ImprovesOnlyTheCitation()
    {
        var changes = DryRunChanges("""
            { "op": "createOrUpdateCitation", "record": "@I00001@", "fact": "BIRT",
              "citation": { "source": "@S00001@", "page": "p. 1", "quay": 2 } }
            """);

        var write = Assert.Single(changes.Facts);
        Assert.Equal("BIRT", write.Tag);
        Assert.False(write.DateImproved);
        Assert.False(write.PlaceImproved);
        Assert.True(write.CitationImproved);
        Assert.Equal(1, write.Improvements);
    }

    [Fact]
    public void RestatingAnIdenticalFact_WritesNothing()
    {
        var changes = DryRunChanges("""
            { "op": "createOrUpdateVital", "record": "@I00001@", "fact": "BIRT",
              "value": { "date": "1928", "place": "Minnesota" } }
            """);

        Assert.Empty(changes.Facts);
    }

    [Fact]
    public void ChangedPlace_IsAWrite()
    {
        var changes = DryRunChanges("""
            { "op": "createOrUpdateVital", "record": "@I00001@", "fact": "BIRT",
              "value": { "date": "1928", "place": "Duluth, Minnesota" } }
            """);

        Assert.Equal("Duluth, Minnesota", Assert.Single(changes.Facts).Place);
    }

    [Fact]
    public void RenamingAPerson_RecordsTheNameBeforeAndAfter()
    {
        var changes = DryRunChanges("""
            { "op": "createOrUpdateVital", "record": "@I00002@", "fact": "NAME", "value": "Harvey Paul /Test/" }
            """);

        Assert.Equal(new NameChange("Harvey Test", "Harvey Paul Test"), changes.Names["@I00002@"]);
    }

    [Fact]
    public void AddingAChild_ListsItAmongTheFamilysAddedChildren()
    {
        var changes = DryRunChanges("""
            { "op": "createOrUpdateChild", "family": "@F00001@",
              "child": { "xref": "@NewI1@", "name": "Ruth /Test/", "sex": "F" } }
            """);

        var family = changes.Families["@F00001@"];
        Assert.Single(family.AddedChildren);
        Assert.Empty(family.AddedPartners);
        Assert.Contains("@I00001@", family.Children);
        Assert.Equal(new[] { "@I00002@", "@I00003@" }, family.Partners);
    }

    [Fact]
    public void NewPerson_AppearsInNamesWithNoPriorName()
    {
        var changes = DryRunChanges("""
            { "op": "createOrUpdateChild", "family": "@F00001@",
              "child": { "xref": "@NewI1@", "name": "Ruth /Test/", "sex": "F" } }
            """);

        var created = Assert.Single(changes.Names);
        Assert.Null(created.Value.Before);
        Assert.Equal("Ruth Test", created.Value.After);
    }

    [Fact]
    public void RealApply_DoesNotCountTheChangeStampAsAFact()
    {
        WriteBaseFile();
        var result = Run("""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateNote", "record": "@I00002@", "text": "A note." } ] } ] }
            """);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Empty(result.Changes.Facts);
    }

    [Fact]
    public void AddingOnlyAPlaceToAnExistingFact_ImprovesThePlaceNotTheDate()
    {
        WriteBaseFile();
        var result = Run("""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateVital", "record": "@I00001@", "fact": "BIRT",
                "value": { "date": "1928", "place": "Duluth, Minnesota" } } ] } ] }
            """, dryRun: true);

        var write = Assert.Single(result.Changes.Facts);
        Assert.False(write.DateImproved);
        Assert.True(write.PlaceImproved);
        Assert.Equal(1, write.Improvements);
    }

    [Fact]
    public void ANewFact_ImprovesBothItsDateAndItsPlace()
    {
        var write = Assert.Single(DryRunChanges("""
            { "op": "createOrUpdateVital", "record": "@I00002@", "fact": "DEAT",
              "value": { "date": "3 MAR 1950", "place": "Duluth, Minnesota" } }
            """).Facts);

        Assert.Equal(2, write.Improvements);
    }
}
