using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GedCore.Apply;
using GedCore.Contract;

namespace GedCore.Tests;

public class ChangesetMigratorTests : ApplyTestBase
{
    static ChangesetMigration Migrate(string json) => ChangesetMigrator.Migrate(new MemoryStream(Encoding.UTF8.GetBytes(json)));

    const string Legacy = """
        { "proposal": "legacy",
          "items": [ { "item": 1, "target": "@I00001@", "ops": [
            { "op": "createOrUpdateVital", "record": "@I00001@", "fact": "DEAT",
              "value": { "date": "2 FEB 1990", "place": "Minnesota" },
              "citation": { "source": "@S00001@", "page": "p. 1", "quay": 2 } },
            { "op": "createOrUpdateCitation", "record": "@I00003@", "fact": "CENS", "match": { "date": "1930" },
              "citation": { "source": "@S00001@", "page": "p. 2", "quay": 2 } },
            { "op": "createOrUpdateSpouse", "person": "@I00001@",
              "spouse": { "xref": "@NewI1@", "name": "Ada /Quill/", "sex": "F",
                          "facts": [ { "fact": "BIRT", "value": { "date": "1930" }, "citation": { "source": "@S00001@", "page": "p. 3", "quay": 2 } } ] },
              "family": "@NewF1@",
              "marriage": { "date": "1 JUN 1950", "citation": { "source": "@S00001@", "page": "p. 4", "quay": 2 } },
              "citation": { "source": "@S00001@", "page": "p. 5", "quay": 2 } },
            { "op": "createOrUpdateChild", "family": "@NewF1@", "child": { "xref": "@NewI2@", "name": "Bea /Test/" },
              "citations": [ { "source": "@S00001@", "page": "p. 6", "quay": 2 } ] } ] },
            { "item": 2, "target": "@NewI1@", "ops": [
              { "op": "createOrUpdateParent", "person": "@I00004@", "role": "mother", "parent": "@NewI1@", "family": "@NewF2@" } ] } ] }
        """;

    [Fact]
    public void ALegacyChangeset_IsRewrittenIntoTheCanonicalForm()
    {
        var migration = Migrate(Legacy);

        Assert.True(migration.Success, string.Join("; ", migration.Errors));
        Assert.Empty(migration.Remaining);
        var changeset = migration.Changeset!;
        Assert.Equal(ChangesetDialect.SchemaVersion, (string?)changeset["schemaVersion"]);
        var ops = changeset["items"]![0]!["ops"]!.AsArray();
        Assert.Equal("2 FEB 1990", (string?)ops[0]!["date"]);
        Assert.Equal("p. 1", (string?)ops[0]!["citations"]![0]!["page"]);
        Assert.Equal("p. 2", (string?)ops[1]!["citations"]![0]!["page"]);
        Assert.Equal("@NewI1@", (string?)ops[2]!["spouse"]);
        Assert.Equal("p. 4", (string?)ops[2]!["marriage"]!["citations"]![0]!["page"]);
        Assert.Equal("p. 5", (string?)ops[2]!["familyCitations"]![0]!["page"]);
        Assert.Equal("p. 6", (string?)ops[3]!["familyCitations"]![0]!["page"]);
        var people = changeset["items"]![0]!["people"]!.AsArray();
        Assert.Equal(["@NewI1@", "@NewI2@"], people.Select(p => (string?)p!["xref"]));
        Assert.Equal("1930", (string?)people[0]!["facts"]![0]!["date"]);
        Assert.Equal("@NewI1@", (string?)changeset["items"]![1]!["people"]![0]!["xref"]);
    }

    [Fact]
    public void TheMigratedChangeset_AppliesExactlyAsTheLegacyOneDoes()
    {
        WriteBaseFile();
        var legacy = Run(Legacy, dryRun: true);
        var migrated = Run(Migrate(Legacy).Changeset!.ToJsonString(), dryRun: true);

        Assert.True(legacy.Success, string.Join("; ", legacy.Errors));
        Assert.Equal(legacy.Log, migrated.Log);
    }

    [Fact]
    public void BothCitationKeys_AreRefusedAndNothingIsRewritten()
    {
        var migration = Migrate("""
            { "items": [ { "item": 1, "ops": [ { "op": "createOrUpdateNote", "record": "@I1@", "text": "A note.",
              "citation": { "source": "@S1@", "page": "a" }, "citations": [ { "source": "@S1@", "page": "b" } ] } ] } ] }
            """);

        Assert.False(migration.Success);
        Assert.Null(migration.Changeset);
        Assert.Contains("items[0].ops[0]: carries both citation and citations; give every citation in citations", migration.Errors);
    }

    [Fact]
    public void OnePersonDescribedTwoWays_IsRefused()
    {
        var migration = Migrate("""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateSpouse", "person": "@I1@", "spouse": { "xref": "@NewI1@", "name": "Ada /Test/" }, "family": "@NewF1@" },
              { "op": "createOrUpdateChild", "family": "@F2@", "child": { "xref": "@NewI1@", "name": "Ada /Other/" } } ] } ] }
            """);

        Assert.False(migration.Success);
        Assert.Contains("items[0].ops[1].child: describes @NewI1@ differently from items[0].ops[0].spouse; reconcile them before migrating", migration.Errors);
    }

    [Fact]
    public void ALaterDescriptionThatAddsASex_IsRefusedRatherThanChosen()
    {
        var migration = Migrate("""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateChild", "family": "@F1@", "child": { "xref": "@NewI1@", "name": "Ann /X/" } },
              { "op": "createOrUpdateSpouse", "person": "@I2@", "spouse": { "xref": "@NewI1@", "name": "Ann /X/", "sex": "F" }, "family": "@NewF9@" } ] } ] }
            """);

        Assert.False(migration.Success);
        Assert.Contains("items[0].ops[1].spouse: describes @NewI1@ differently from items[0].ops[0].child; reconcile them before migrating", migration.Errors);
    }

    [Fact]
    public void ANullOptionalField_IsRemoved()
    {
        var migration = Migrate("""
            { "items": [ { "item": 1, "target": null, "ops": [ { "op": "createOrUpdateNote", "record": "@I1@", "text": "A note.", "match": null,
              "citation": { "source": "@S1@", "page": null } } ] } ] }
            """);

        Assert.True(migration.Success, string.Join("; ", migration.Errors));
        Assert.Empty(migration.Remaining);
        Assert.Contains("items[0].target: null removed", migration.Changes);
        Assert.Contains("items[0].ops[0].citations[0].page: null removed", migration.Changes);
    }

    [Fact]
    public void WhatNoRuleCanFix_IsLeftAndReported()
    {
        var migration = Migrate("""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateVital", "record": "@I1@", "fact": "EVEN", "value": "1 JAN 1900", "match": "x", "citatons": [] } ] } ] }
            """);

        Assert.True(migration.Success);
        var op = migration.Changeset!["items"]![0]!["ops"]![0]!;
        Assert.Equal("x", (string?)op["match"]!["value"]);
        Assert.Contains(migration.Remaining, w => w.StartsWith("items[0].ops[0].citatons: unknown field"));
        Assert.Contains("items[0].ops[0].value: \"1 JAN 1900\" is a date; give it as date", migration.Remaining);
    }

    [Fact]
    public void ACanonicalChangeset_IsLeftUnchanged()
    {
        string canonical = Migrate(Legacy).Changeset!.ToJsonString();

        var again = Migrate(canonical);

        Assert.Empty(again.Changes);
        Assert.True(JsonNode.DeepEquals(JsonNode.Parse(canonical), again.Changeset));
    }

    [Fact]
    public void AnUnreadableChangeset_IsRefusedWithTheContractsErrors()
    {
        var migration = Migrate("""{ "schemaVersion": "9.0", "items": [] }""");

        Assert.False(migration.Success);
        Assert.Equal(["schemaVersion: version 9.0 is not supported; this GedFire reads changeset version 1.x"], migration.Errors);
    }
}
