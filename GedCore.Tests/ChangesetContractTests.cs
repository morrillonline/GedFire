using System.Text.Json;
using GedCore.Contract;

namespace GedCore.Tests;

public class ChangesetContractTests
{
    static ContractReport Check(string json)
    {
        using var document = JsonDocument.Parse(json);
        return ChangesetContract.Check(document.RootElement);
    }

    // One item holding the given ops, at the current contract version.
    static ContractReport CheckOps(string ops, string people = "") =>
        Check($$"""{ "schemaVersion": "1.0", "items": [ { "item": 1{{(people.Length > 0 ? $", \"people\": [{people}]" : "")}}, "ops": [ {{ops}} ] } ] }""");

    const string Cite = """{ "source": "@S1@", "page": "p. 1", "quay": 2 }""";

    [Fact]
    public void TheCanonicalForm_HasNoErrorsOrWarnings()
    {
        var report = CheckOps($$"""
            { "op": "createOrUpdateVital", "record": "@I1@", "fact": "BIRT", "date": "1 JAN 1900", "place": "Maine", "citations": [{{Cite}}] },
            { "op": "createOrUpdateVital", "record": "@I1@", "fact": "NAME", "value": "Ralph /Morrill/" },
            { "op": "createOrUpdateVital", "record": "@I1@", "fact": "OCCU", "value": "Farmer", "date": "1850", "match": { "value": "Laborer" } },
            { "op": "createOrUpdateSpouse", "person": "@I1@", "spouse": "@NewI1@", "family": "@NewF1@",
              "marriage": { "date": "1 JUN 1899", "citations": [{{Cite}}] }, "familyCitations": [{{Cite}}] }
            """, """{ "xref": "@NewI1@", "name": "Ada /Test/", "sex": "F", "facts": [ { "fact": "BIRT", "date": "1880", "citations": [] } ] }""");

        Assert.Empty(report.Errors);
        Assert.Empty(report.Warnings);
    }

    [Fact]
    public void ASingularCitation_IsWarnedByPathToUseCitations()
    {
        var report = CheckOps($$"""{ "op": "createOrUpdateNote", "record": "@I1@", "text": "A note.", "citation": {{Cite}} }""");

        Assert.Empty(report.Errors);
        Assert.Contains("items[0].ops[0].citation: use citations, an array, for every citation", report.Warnings);
    }

    [Fact]
    public void BothCitationKeys_AreRefused()
    {
        var report = CheckOps($$"""{ "op": "createOrUpdateNote", "record": "@I1@", "text": "A note.", "citation": {{Cite}}, "citations": [{{Cite}}] }""");

        Assert.Contains("items[0].ops[0]: carries both citation and citations; give every citation in citations", report.Errors);
    }

    [Fact]
    public void CitationsAsAnObject_IsRefused()
    {
        var report = CheckOps($$"""{ "op": "createOrUpdateNote", "record": "@I1@", "text": "A note.", "citations": {{Cite}} }""");

        Assert.Contains("items[0].ops[0].citations: must be an array", report.Errors);
    }

    [Fact]
    public void ARelationshipOpsCitations_AreWarnedToBeRenamedFamilyCitations()
    {
        var report = CheckOps($$"""{ "op": "createOrUpdateChild", "family": "@F1@", "child": "@I2@", "citations": [{{Cite}}] }""");

        Assert.Empty(report.Errors);
        Assert.Contains(report.Warnings, w => w.StartsWith("items[0].ops[0].citations: on createOrUpdateChild the op-level citations cite the family record; rename it familyCitations"));
    }

    [Fact]
    public void FamilyCitationsBesideOpLevelCitations_AreRefused()
    {
        var report = CheckOps($$"""{ "op": "createOrUpdateSpouse", "person": "@I1@", "spouse": "@I2@", "familyCitations": [{{Cite}}], "citations": [{{Cite}}] }""");

        Assert.Contains(report.Errors, e => e.StartsWith("items[0].ops[0]: carries familyCitations and citations"));
    }

    [Fact]
    public void UnknownEnvelopeKeys_AreReportedByName()
    {
        var report = Check("""{ "schemaVersion": "1.0", "gedcom": "tree.ged", "recordingRule": "x", "items": [] }""");

        Assert.Contains(report.Warnings, w => w.StartsWith("gedcom: unknown field"));
        Assert.Contains(report.Warnings, w => w.StartsWith("recordingRule: unknown field"));
    }

    [Fact]
    public void AMisspelledOpKey_IsReportedByItsFullPath()
    {
        var report = CheckOps($$"""{ "op": "createOrUpdateNote", "record": "@I1@", "text": "A note.", "citatons": [{{Cite}}] }""");

        Assert.Contains(report.Warnings, w => w.StartsWith("items[0].ops[0].citatons: unknown field"));
    }

    [Fact]
    public void AnUnknownKeyInsideMarriage_IsReportedByItsFullPath()
    {
        var report = CheckOps($$"""{ "op": "createOrUpdateSpouse", "person": "@I1@", "spouse": "@I2@", "marriage": { "date": "1900", "citations": [{{Cite}}], "plase": "Maine" } }""");

        Assert.Contains(report.Warnings, w => w.StartsWith("items[0].ops[0].marriage.plase: unknown field"));
    }

    [Fact]
    public void ADateGivenAsAnEventsValue_IsReported()
    {
        var report = CheckOps("""{ "op": "createOrUpdateVital", "record": "@I1@", "fact": "EVEN", "value": "1 JAN 1900" }""");

        Assert.Contains("items[0].ops[0].value: \"1 JAN 1900\" is a date; give it as date", report.Warnings);
    }

    [Fact]
    public void AValueOnAnEvent_IsReportedNamingTheTag()
    {
        var report = CheckOps("""{ "op": "createOrUpdateVital", "record": "@I1@", "fact": "BIRT", "value": "Y" }""");

        Assert.Contains("items[0].ops[0].value: BIRT is an event; give its date and place, not value", report.Warnings);
    }

    [Fact]
    public void ADateOnAName_IsReportedNamingTheTag()
    {
        var report = CheckOps("""{ "op": "createOrUpdateVital", "record": "@I1@", "fact": "NAME", "value": "Ralph /Morrill/", "date": "1900" }""");

        Assert.Contains("items[0].ops[0].date: NAME takes a text value, not a date or place", report.Warnings);
    }

    [Fact]
    public void AnObjectValue_IsWarned_AndRefusedBesideATopLevelDate()
    {
        var legacy = CheckOps("""{ "op": "createOrUpdateVital", "record": "@I1@", "fact": "BIRT", "value": { "date": "1900" } }""");
        var mixed = CheckOps("""{ "op": "createOrUpdateVital", "record": "@I1@", "fact": "BIRT", "value": { "date": "1900" }, "date": "1901" }""");

        Assert.Empty(legacy.Errors);
        Assert.Contains(legacy.Warnings, w => w.StartsWith("items[0].ops[0].value: an object value is the old event form"));
        Assert.Contains(mixed.Errors, e => e.StartsWith("items[0].ops[0].value: give date and place beside value"));
    }

    [Fact]
    public void AVitalWithNoPayload_IsRefused()
    {
        var report = CheckOps("""{ "op": "createOrUpdateVital", "record": "@I1@", "fact": "BIRT" }""");

        Assert.Contains("items[0].ops[0]: createOrUpdateVital needs value, date or place", report.Errors);
    }

    [Fact]
    public void AnInlinePerson_IsReportedNamingTheField()
    {
        var report = CheckOps("""{ "op": "createOrUpdateSpouse", "person": "@I1@", "spouse": { "xref": "@NewI1@", "name": "Ada /Test/" }, "family": "@NewF1@" }""");

        Assert.Contains(report.Warnings, w => w.StartsWith("items[0].ops[0].spouse: an inline new person; declare it in the item's people"));
    }

    [Fact]
    public void APlaceholderNoPeopleEntryDeclares_IsReported()
    {
        var report = CheckOps("""{ "op": "createOrUpdateChild", "family": "@F1@", "child": "@NewI1@" }""");

        Assert.Contains(report.Warnings, w => w.StartsWith("items[0].ops[0].child: @NewI1@ is not declared in items[0].people"));
    }

    [Fact]
    public void APeopleEntryNoOpNames_IsRefused()
    {
        var report = CheckOps(
            """{ "op": "createOrUpdateNote", "record": "@I1@", "text": "A note." }""",
            """{ "xref": "@NewI1@", "name": "Ada /Test/" }""");

        Assert.Contains("items[0].people[0]: @NewI1@ is declared but no op's spouse, child or parent names it", report.Errors);
    }

    [Fact]
    public void APeopleEntryWithARealXref_IsRefused()
    {
        var report = CheckOps(
            """{ "op": "createOrUpdateChild", "family": "@F1@", "child": "@I9@" }""",
            """{ "xref": "@I9@", "name": "Ada /Test/" }""");

        Assert.Contains(report.Errors, e => e.StartsWith("items[0].people[0].xref: people declares new people"));
    }

    [Fact]
    public void AnUnsupportedMajorVersion_IsRefused()
    {
        var report = Check("""{ "schemaVersion": "2.0", "items": [] }""");

        Assert.Equal(["schemaVersion: version 2.0 is not supported; this GedFire reads changeset version 1.x"], report.Errors);
    }

    [Theory]
    [InlineData("\"1\"")]
    [InlineData("1.0")]
    [InlineData("\"v1.0\"")]
    public void AMalformedVersion_IsRefused(string version)
    {
        var report = Check($$"""{ "schemaVersion": {{version}}, "items": [] }""");

        Assert.Contains(report.Errors, e => e.StartsWith("schemaVersion: must be"));
    }

    [Fact]
    public void AMissingVersion_IsWarned()
    {
        var report = Check("""{ "items": [] }""");

        Assert.Empty(report.Errors);
        Assert.Contains(report.Warnings, w => w.StartsWith("schemaVersion: missing"));
    }

    [Fact]
    public void ANullOptionalScalar_IsWarnedAndReadAsAbsent_ButANullRequiredFieldIsRefused()
    {
        string json = """
            { "items": [ { "item": 1, "target": null, "ops": [
              { "op": "deleteParent", "person": "@I1@", "role": "father", "family": null },
              { "op": "createOrUpdateVital", "record": "@I1@", "fact": "BIRT", "value": null, "date": "1900",
                "citations": [ { "source": "@S1@", "page": null, "quay": null } ] } ] } ] }
            """;

        var reading = GedCore.Apply.Changeset.Read(json);
        var required = CheckOps("""{ "op": "deleteNote", "record": null, "text": "A note." }""");

        Assert.Empty(reading.Errors);
        Assert.Contains("items[0].ops[0].family: null; omit the field instead", reading.Warnings);
        Assert.Contains("items[0].ops[1].citations[0].page: null; omit the field instead", reading.Warnings);
        Assert.Contains("items[0].ops[0].record: must be a string", required.Errors);
    }

    [Fact]
    public void AnUnknownOp_AndAMissingRequiredField_AreRefusedByPath()
    {
        var report = CheckOps("""{ "op": "createOrUpdateThing" }, { "op": "deleteNote", "record": "@I1@" }""");

        Assert.Contains(report.Errors, e => e.StartsWith("items[0].ops[0].op: unknown op \"createOrUpdateThing\""));
        Assert.Contains("items[0].ops[1]: required field \"text\" is missing", report.Errors);
    }
}
