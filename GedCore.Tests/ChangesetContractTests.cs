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

    // A date is written to the file as given, so it must be a GEDCOM 7 date.

    static string Birth(string date) =>
        $$"""{ "op": "createOrUpdateVital", "record": "@I1@", "fact": "BIRT", "date": "{{date}}" }""";

    [Theory]
    [InlineData("March 5, 1806")]
    [InlineData("1806-03-05")]
    [InlineData("5/3/1806")]
    [InlineData("30 FEB 1806")]
    [InlineData("1699/00")]
    [InlineData("about 1806")]
    public void ADateThatIsNotGedcom_IsRefusedByPath(string date)
    {
        var report = CheckOps(Birth(date));

        Assert.StartsWith($"items[0].ops[0].date: \"{date}\" is not a GEDCOM date", Assert.Single(report.Errors));
    }

    [Theory]
    [InlineData("5 MAR 1806")]
    [InlineData("05 MAR 1806")]
    [InlineData("MAR 1806")]
    [InlineData("1806")]
    [InlineData("ABT 1806")]
    [InlineData("BEF 1806")]
    [InlineData("BET 1806 AND 1810")]
    [InlineData("FROM 1806 TO 1810")]
    [InlineData("JULIAN 5 MAR 1700")]
    public void AGedcomDate_IsAccepted(string date) =>
        Assert.Empty(CheckOps(Birth(date)).Errors);

    [Fact]
    public void ALowerCaseDate_IsRefusedWithTheUpperCaseForm() =>
        Assert.Equal(["items[0].ops[0].date: write the date in upper case: \"ABT 5 MAR 1806\""], CheckOps(Birth("abt 5 Mar 1806")).Errors);

    [Fact]
    public void ADateIsCheckedWhereverAFactIsWritten()
    {
        var report = CheckOps($$"""
            { "op": "createOrUpdateSpouse", "person": "@I1@", "spouse": "@NewI1@", "family": "@NewF1@",
              "marriage": { "date": "June 1899", "citations": [{{Cite}}] } },
            { "op": "mergePerson", "survivor": "@I1@", "duplicate": "@I2@", "facts": [ { "fact": "BIRT", "date": "c. 1880" } ] }
            """, """{ "xref": "@NewI1@", "name": "Ada /Test/", "facts": [ { "fact": "BIRT", "date": "1880?" } ] }""");

        Assert.Equal(
            ["items[0].people[0].facts[0].date", "items[0].ops[0].marriage.date", "items[0].ops[1].facts[0].date"],
            report.Errors.Select(e => e[..e.IndexOf(':')]));
    }

    // A match names what the file holds, which may be the very date the op is there to correct.
    [Fact]
    public void AMatchDate_IsNotHeldToGedcomSyntax() =>
        Assert.Empty(CheckOps("""
            { "op": "createOrUpdateVital", "record": "@I1@", "fact": "CENS", "date": "5 MAR 1806", "match": { "date": "March 5, 1806" } }
            """).Errors);

    // The limits the dialect declares are refused here, before any op is read.

    [Theory]
    [InlineData("""{ "op": "createOrUpdateVital", "record": "@I1@", "fact": "BIRT", "date": "1806", "citations": [ { "source": "@S1@", "quay": 7 } ] }""",
        "items[0].ops[0].citations[0].quay: must be 0 to 3, not 7")]
    [InlineData("""{ "op": "createOrUpdateVital", "record": "@I1@", "fact": "CENS", "date": "1806", "mode": "append" }""",
        "items[0].ops[0].mode: must be \"upsert\" or \"add\", not \"append\"")]
    [InlineData("""{ "op": "mergePerson", "survivor": "@I1@", "duplicate": "@I2@", "facts": [ { "fact": "BIRT", "keep": "surviver" } ] }""",
        "items[0].ops[0].facts[0].keep: must be \"survivor\" or \"duplicate\" or \"value\", not \"surviver\"")]
    [InlineData("""{ "op": "deleteNote", "record": "I1", "text": "A note." }""",
        "items[0].ops[0].record: must be an xref such as \"@I1@\", not \"I1\"")]
    [InlineData("""{ "op": "createOrUpdateMedia", "files": [] }""",
        "items[0].ops[0].files: needs at least 1 entry")]
    [InlineData("""{ "op": "createOrUpdateVital", "record": "@I1@", "fact": "BIRT", "date": "2 APR 1928", "replacedCitations": "moveToNote" }""",
        "items[0].ops[0].replacedCitations: must be \"keep\" or \"drop\", not \"moveToNote\"")]
    [InlineData("""{ "op": "createOrUpdateParent", "person": "@I1@", "role": "uncle", "parent": "@I2@" }""",
        "items[0].ops[0].role: must be \"father\" or \"mother\", not \"uncle\"")]
    public void AValueOutsideTheDialectsLimits_IsRefusedByPath(string op, string error) =>
        Assert.Equal([error], CheckOps(op).Errors);

    [Fact]
    public void ANewPersonsSex_MustBeOneOfTheFourValues()
    {
        var report = CheckOps(
            """{ "op": "createOrUpdateChild", "family": "@F1@", "child": "@NewI1@" }""",
            """{ "xref": "@NewI1@", "name": "Ada /Test/", "sex": "Female" }""");

        Assert.Equal(["items[0].people[0].sex: must be \"M\" or \"F\" or \"U\" or \"X\", not \"Female\""], report.Errors);
    }
}
