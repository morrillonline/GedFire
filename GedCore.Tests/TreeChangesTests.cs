using GedCore.Apply;
using GedCore.Points;

namespace GedCore.Tests;

public class TreeChangesTests : ApplyTestBase
{
    const string Ruth = """{ "xref": "@NewI1@", "name": "Ruth /Test/", "sex": "F" }""";

    TreeChanges DryRunChanges(string opsJson, string people = "")
    {
        WriteBaseFile();
        var result = Run($$"""{ "items": [ { "item": 1, "people": [ {{people}} ], "ops": [ {{opsJson}} ] } ] }""", dryRun: true);
        Assert.True(result.Success, string.Join("; ", result.Errors));
        return result.Changes;
    }

    [Fact]
    public void NewFact_IsReportedAsWrittenOnItsRecord()
    {
        var changes = DryRunChanges("""
            { "op": "createOrUpdateVital", "record": "@I00002@", "fact": "DEAT",
              "date": "3 MAR 1950", "place": "Duluth, Minnesota" }
            """);

        var write = Assert.Single(changes.Facts);
        Assert.Equal(new FactWrite("@I00002@", "DEAT", "03 MAR 1950", "Duluth, Minnesota"), write);
    }

    [Fact]
    public void CitationOnAnExistingFact_ImprovesOnlyTheCitation()
    {
        var changes = DryRunChanges("""
            { "op": "createOrUpdateCitation", "record": "@I00001@", "fact": "BIRT",
              "citations": [{ "source": "@S00001@", "page": "p. 1", "quay": 2 }] }
            """);

        var write = Assert.Single(changes.Facts);
        Assert.Equal("BIRT", write.Tag);
        Assert.False(write.DateImproved);
        Assert.False(write.PlaceImproved);
        Assert.True(write.CitationImproved);
        Assert.Equal(1, write.Improvements);
    }

    [Fact]
    public void CommentaryAloneOnAnUncitedFact_IsNoCitation()
    {
        var changes = DryRunChanges("""
            { "op": "createOrUpdateCitation", "record": "@I00001@", "fact": "BIRT",
              "citations": [{ "source": "@VOID@", "commentary": "His age varies by census." }] }
            """);

        Assert.Empty(changes.Facts);
    }

    [Fact]
    public void RestatingAnIdenticalFact_WritesNothing()
    {
        var changes = DryRunChanges("""
            { "op": "createOrUpdateVital", "record": "@I00001@", "fact": "BIRT",
              "date": "1928", "place": "Minnesota" }
            """);

        Assert.Empty(changes.Facts);
    }

    [Fact]
    public void ChangedPlace_IsAWrite()
    {
        var changes = DryRunChanges("""
            { "op": "createOrUpdateVital", "record": "@I00001@", "fact": "BIRT",
              "date": "1928", "place": "Duluth, Minnesota" }
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
              "child": "@NewI1@" }
            """, Ruth);

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
              "child": "@NewI1@" }
            """, Ruth);

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
                "date": "1928", "place": "Duluth, Minnesota" } ] } ] }
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
              "date": "3 MAR 1950", "place": "Duluth, Minnesota" }
            """).Facts);

        Assert.Equal(2, write.Improvements);
    }

    static string BirthDate(string date) =>
        $$"""{ "op": "createOrUpdateVital", "record": "@I00001@", "fact": "BIRT", "date": "{{date}}", "place": "Minnesota" }""";

    [Fact]
    public void AMoreSpecificBirthDate_IsARefinedDate()
    {
        var write = Assert.Single(DryRunChanges(BirthDate("5 NOV 1928")).Facts);

        Assert.Equal(DateImprovement.Refined, write.DateImprovement);
        Assert.Equal(1, write.Improvements);
    }

    [Fact]
    public void ALessSpecificBirthDate_IsNotAWrite()
    {
        Assert.Empty(DryRunChanges(BirthDate("ABT 1928")).Facts);
    }

    [Fact]
    public void APlaceDifferingOnlyByCounty_IsNotAnImprovedPlace()
    {
        var lines = BaseLines.ToList();
        lines[lines.IndexOf("2 PLAC Minnesota")] = "2 PLAC Duluth, St. Louis County, Minnesota";
        WriteFile(lines);

        var result = Run("""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateVital", "record": "@I00001@", "fact": "BIRT",
                "date": "1928", "place": "Duluth, St. Louis, Minnesota",
                "citations": [{ "source": "@S00001@", "page": "p. 2", "quay": 2 }] } ] } ] }
            """, dryRun: true);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        var write = Assert.Single(result.Changes.Facts);
        Assert.False(write.PlaceImproved);
        Assert.True(write.CitationImproved);
        var preview = CardScorer.Preview([new PointsCard("@I00001@", "enrich-person", "enrich-birth-place", null, 1)], result.Changes);
        Assert.Empty(preview.Closed);
    }

    [Fact]
    public void ACorrectionPays_WhileTheSpecificityLossAdvisoryStillFires()
    {
        var lines = BaseLines.ToList();
        lines[lines.IndexOf("2 DATE 1928")] = "2 DATE 17 OCT 1810";
        lines[lines.IndexOf("2 PLAC Minnesota")] = "2 PLAC Pembroke, Yarmouth, Nova Scotia, Canada";
        WriteFile(lines);

        var result = Run("""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateVital", "record": "@I00001@", "fact": "BIRT",
                "date": "BEF 27 SEP 1796", "place": "Canada" } ] } ] }
            """, dryRun: true);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Contains(result.NewDiagnostics, d => d.Code == "GEN201");
        Assert.Equal(DateImprovement.Corrected, Assert.Single(result.Changes.Facts).DateImprovement);
    }
}
