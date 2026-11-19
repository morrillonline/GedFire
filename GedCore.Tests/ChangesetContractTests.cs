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
        Check($$"""{ "items": [ { "item": 1{{(people.Length > 0 ? $", \"people\": [{people}]" : "")}}, "ops": [ {{ops}} ] } ] }""");

    const string Cite = """{ "source": "@S1@", "page": "p. 1", "quay": 2 }""";

    [Fact]
    public void TheCanonicalForm_HasNoErrors()
    {
        var report = CheckOps($$"""
            { "op": "createOrUpdateVital", "record": "@I1@", "fact": "BIRT", "date": "1 JAN 1900", "place": "Maine", "citations": [{{Cite}}] },
            { "op": "createOrUpdateVital", "record": "@I1@", "fact": "NAME", "value": "Ralph /Morrill/" },
            { "op": "createOrUpdateVital", "record": "@I1@", "fact": "OCCU", "value": "Farmer", "date": "1850", "match": { "value": "Laborer" } },
            { "op": "createOrUpdateSpouse", "person": "@I1@", "spouse": "@NewI1@", "family": "@NewF1@",
              "marriage": { "date": "1 JUN 1899", "citations": [{{Cite}}] }, "familyCitations": [{{Cite}}] }
            """, """{ "xref": "@NewI1@", "name": "Ada /Test/", "sex": "F", "facts": [ { "fact": "BIRT", "date": "1880", "citations": [] } ] }""");

        Assert.Empty(report.Errors);
    }

    // R1: citations are always an array named citations.

    [Fact]
    public void R1_ASingularCitation_IsRefusedByPathToUseCitations()
    {
        var report = CheckOps($$"""{ "op": "createOrUpdateNote", "record": "@I1@", "text": "A note.", "citation": {{Cite}} }""");

        Assert.Equal(["items[0].ops[0].citation: use \"citations\", an array"], report.Errors);
    }

    [Fact]
    public void R1_BothCitationKeysOnOneOp_RefuseTheSingularOne()
    {
        var report = CheckOps($$"""{ "op": "createOrUpdateNote", "record": "@I1@", "text": "A note.", "citation": {{Cite}}, "citations": [{{Cite}}] }""");

        Assert.Equal(["items[0].ops[0].citation: use \"citations\", an array"], report.Errors);
    }

    [Fact]
    public void R1_CitationsAsAnObject_IsRefused()
    {
        var report = CheckOps("""{ "op": "createOrUpdateNote", "record": "@I1@", "text": "A note.", "citations": {} }""");

        Assert.Equal(["items[0].ops[0].citations: must be an array"], report.Errors);
    }

    [Fact]
    public void R1_ARelationshipOpsCitations_AreRefusedToUseFamilyCitations()
    {
        var report = CheckOps($$"""{ "op": "createOrUpdateChild", "family": "@F1@", "child": "@I2@", "citations": [{{Cite}}] }""");

        Assert.Contains(report.Errors, e => e.StartsWith("items[0].ops[0].citations: use \"familyCitations\", an array"));
    }

    // R2: a key the dialect does not define, anywhere.

    [Fact]
    public void R2_UnknownEnvelopeKeys_AreRefusedByName()
    {
        var report = Check("""{ "gedcom": "tree.ged", "items": [] }""");

        Assert.Contains(report.Errors, e => e.StartsWith("gedcom: unknown field; a changeset takes proposal"));
    }

    [Fact]
    public void R2_AMisspelledOpKey_IsRefusedByItsFullPath()
    {
        var report = CheckOps($$"""{ "op": "createOrUpdateNote", "record": "@I1@", "text": "A note.", "citatons": [{{Cite}}] }""");

        Assert.Contains(report.Errors, e => e.StartsWith("items[0].ops[0].citatons: unknown field"));
    }

    [Fact]
    public void R2_AnUnknownKeyInsideMarriage_IsRefusedByItsFullPath()
    {
        var report = CheckOps($$"""{ "op": "createOrUpdateSpouse", "person": "@I1@", "spouse": "@I2@", "marriage": { "date": "1900", "citations": [{{Cite}}], "plase": "Maine" } }""");

        Assert.Contains(report.Errors, e => e.StartsWith("items[0].ops[0].marriage.plase: unknown field"));
    }

    [Fact]
    public void R2_AnUnknownKeyInsideACitation_IsRefusedByItsFullPath()
    {
        var report = CheckOps("""{ "op": "createOrUpdateNote", "record": "@I1@", "text": "A note.", "citations": [ { "source": "@S1@", "pg": "p. 1" } ] }""");

        Assert.Contains(report.Errors, e => e.StartsWith("items[0].ops[0].citations[0].pg: unknown field; a citation takes source, page, replacesPage, dataText, quay"));
    }

    // R3: the fact's tag fixes its payload.

    [Fact]
    public void R3_ADateGivenAsAnEventsValue_IsRefused()
    {
        var report = CheckOps("""{ "op": "createOrUpdateVital", "record": "@I1@", "fact": "EVEN", "value": "1 JAN 1900" }""");

        Assert.Equal(["items[0].ops[0].value: \"1 JAN 1900\" is a date; give it as date"], report.Errors);
    }

    [Theory]
    [InlineData("SSN", "004123456")]
    [InlineData("IDNO", "1234")]
    [InlineData("NCHI", "3")]
    public void R3_ANumericValue_IsNotADate(string tag, string value) =>
        Assert.Empty(CheckOps($$"""{ "op": "createOrUpdateVital", "record": "@I1@", "fact": "{{tag}}", "value": "{{value}}" }""").Errors);

    [Fact]
    public void R3_AValueOnAnEvent_IsRefusedNamingTheTag()
    {
        var report = CheckOps("""{ "op": "createOrUpdateVital", "record": "@I1@", "fact": "BIRT", "value": "Y" }""");

        Assert.Equal(["items[0].ops[0].value: BIRT is an event; give its date and place, not value"], report.Errors);
    }

    [Fact]
    public void R3_ADateOnAName_IsRefusedNamingTheTag()
    {
        var report = CheckOps("""{ "op": "createOrUpdateVital", "record": "@I1@", "fact": "NAME", "value": "Ralph /Morrill/", "date": "1900" }""");

        Assert.Equal(["items[0].ops[0].date: NAME takes a text value, not a date or place"], report.Errors);
    }

    [Fact]
    public void R3_ADateOnANameInMatch_IsRefusedByThePathInsideMatch()
    {
        var report = CheckOps("""{ "op": "deleteVital", "record": "@I1@", "fact": "NAME", "match": { "value": "Ralph /Morrill/", "date": "1900" } }""");

        Assert.Equal(["items[0].ops[0].match.date: NAME takes a text value, not a date or place"], report.Errors);
    }

    [Fact]
    public void R3_AnObjectValue_IsRefused()
    {
        var report = CheckOps("""{ "op": "createOrUpdateVital", "record": "@I1@", "fact": "BIRT", "value": { "date": "1900" } }""");

        Assert.Contains("items[0].ops[0].value: an object value is not read; give date and place as fields beside value", report.Errors);
    }

    [Fact]
    public void R3_AStringMatch_IsRefused()
    {
        var report = CheckOps("""{ "op": "deleteVital", "record": "@I1@", "fact": "OCCU", "match": "Farmer" }""");

        Assert.Equal(["items[0].ops[0].match: must be an object"], report.Errors);
    }

    [Fact]
    public void AVitalWithNoPayload_IsRefused()
    {
        var report = CheckOps("""{ "op": "createOrUpdateVital", "record": "@I1@", "fact": "BIRT" }""");

        Assert.Contains("items[0].ops[0]: createOrUpdateVital needs value, date or place", report.Errors);
    }

    // R4: each new person is declared once in its item's people and named by xref.

    [Fact]
    public void R4_AnInlineSpouse_IsRefusedNamingTheField()
    {
        var report = CheckOps("""{ "op": "createOrUpdateSpouse", "person": "@I1@", "spouse": { "xref": "@NewI1@", "name": "Ada /Test/" }, "family": "@NewF1@" }""");

        Assert.Equal(["items[0].ops[0].spouse: an inline person is not read; declare the person in the item's people and give only its xref here"], report.Errors);
    }

    [Fact]
    public void R4_APlaceholderNoPeopleEntryDeclares_IsRefused()
    {
        var report = CheckOps("""{ "op": "createOrUpdateChild", "family": "@F1@", "child": "@NewI1@" }""");

        Assert.Equal(["items[0].ops[0].child: @NewI1@ is not declared in items[0].people; declare each new person the item names"], report.Errors);
    }

    [Fact]
    public void R4_APeopleEntryNoOpNames_IsRefused()
    {
        var report = CheckOps(
            """{ "op": "createOrUpdateNote", "record": "@I1@", "text": "A note." }""",
            """{ "xref": "@NewI1@", "name": "Ada /Test/" }""");

        Assert.Equal(["items[0].people[0]: @NewI1@ is declared but no op's spouse, child or parent names it"], report.Errors);
    }

    [Fact]
    public void R4_APeopleEntryWithARealXref_IsRefused()
    {
        var report = CheckOps(
            """{ "op": "createOrUpdateChild", "family": "@F1@", "child": "@I9@" }""",
            """{ "xref": "@I9@", "name": "Ada /Test/" }""");

        Assert.Contains(report.Errors, e => e.StartsWith("items[0].people[0].xref: people declares new people"));
    }

    // A changeset carries no version; a key left over from an earlier release is an unknown field.

    [Fact]
    public void ASchemaVersion_IsRefusedAsAnUnknownField() =>
        Assert.Contains(Check("""{ "schemaVersion": "1.1", "items": [] }""").Errors, e => e.StartsWith("schemaVersion: unknown field"));

    [Fact]
    public void ANullOptionalField_IsRefused_AndANullRequiredFieldIsRefusedByType()
    {
        var optional = CheckOps("""{ "op": "deleteParent", "person": "@I1@", "role": "father", "family": null }""");
        var required = CheckOps("""{ "op": "deleteNote", "record": null, "text": "A note." }""");

        Assert.Equal(["items[0].ops[0].family: null; omit the field instead"], optional.Errors);
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
