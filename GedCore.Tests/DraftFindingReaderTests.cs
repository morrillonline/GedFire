using System.Text.Json;
using GedCore.Drafting;

namespace GedCore.Tests;

public class DraftFindingReaderTests
{
    const string Valid = """
        { "id": "F-1", "extract": "text",
          "source": { "name": "Find a Grave", "locator": "Memorial #1", "class": "derivative", "information": "primary" },
          "target": { "xref": "@I1@", "card": "enrich-death", "name": "Ann Test" },
          "facts": [ { "record": "@I1@", "fact": "deat", "date": "1 Jan 1900", "quay": 2 } ],
          "notes": [ { "record": "@I1@", "text": "A note." } ] }
        """;

    static (IReadOnlyList<DraftFinding> Findings, IReadOnlyList<string> Errors) Read(string findingsJson) =>
        DraftFindingReader.Read(JsonDocument.Parse(findingsJson).RootElement);

    [Fact]
    public void AValidFinding_IsReadWithTheTagUppercased()
    {
        var (findings, errors) = Read($"[{Valid}]");

        Assert.Empty(errors);
        var finding = Assert.Single(findings);
        Assert.Equal("F-1", finding.Id);
        var fact = Assert.Single(finding.Facts);
        Assert.Equal("DEAT", fact.Tag);
        Assert.Equal("1 Jan 1900", fact.Date);
        Assert.Equal("A note.", Assert.Single(finding.Notes).Text);
    }

    [Fact]
    public void ANonArray_IsRejected() =>
        Assert.Contains("non-empty array", Assert.Single(Read("{}").Errors));

    [Fact]
    public void AnEmptyArray_IsRejected() =>
        Assert.Contains("non-empty array", Assert.Single(Read("[]").Errors));

    [Fact]
    public void AMissingQuay_NamesTheFieldByPath()
    {
        var (findings, errors) = Read($"[{Valid.Replace(", \"quay\": 2", "")}]");

        Assert.Empty(findings);
        Assert.Contains("findings[0].facts[0].quay is required", Assert.Single(errors));
    }

    [Fact]
    public void AnUnrecognizedField_IsRejectedByPath()
    {
        var (_, errors) = Read($"[{Valid.Replace("\"id\": \"F-1\",", "\"id\": \"F-1\", \"wave\": \"W7\",")}]");

        Assert.Contains("findings[0].wave is not a recognized field", Assert.Single(errors));
    }

    [Fact]
    public void ABadSourceClass_ListsTheAcceptedValues()
    {
        var (_, errors) = Read($"[{Valid.Replace("derivative", "hearsay")}]");

        Assert.Contains("findings[0].source.class must be one of original, image copy, derivative, authored, user-contributed", Assert.Single(errors));
    }

    [Fact]
    public void AFactWithNeitherDateNorPlace_IsRejected()
    {
        var (_, errors) = Read($"[{Valid.Replace("\"date\": \"1 Jan 1900\", ", "")}]");

        Assert.Contains("findings[0].facts[0] needs a date, a place or a value", Assert.Single(errors));
    }

    [Fact]
    public void AFindingWithNothingToAssert_IsReadAndLeftToTheDraftChecks()
    {
        const string nothing = """
            { "id": "F-1", "extract": "text",
              "source": { "name": "Find a Grave", "locator": "Memorial #1", "class": "derivative", "information": "primary" },
              "target": { "xref": "@I1@" } }
            """;

        var (findings, errors) = Read($"[{nothing}]");

        Assert.Empty(errors);
        Assert.Empty(Assert.Single(findings).Facts);
    }

    [Fact]
    public void ABadTargetXref_IsRejected()
    {
        var (_, errors) = Read($"[{Valid.Replace("\"xref\": \"@I1@\"", "\"xref\": \"I1\"")}]");

        Assert.Contains("findings[0].target.xref must be an xref", Assert.Single(errors));
    }

    [Fact]
    public void ErrorsAcrossFindings_AreAllReported()
    {
        var (_, errors) = Read($"[{Valid.Replace("\"id\": \"F-1\",", "")}, {Valid.Replace("\"extract\": \"text\",", "")}]");

        Assert.Equal(2, errors.Count);
        Assert.Contains("findings[0].id is required.", errors);
        Assert.Contains("findings[1].extract is required.", errors);
    }

    const string WithRelations = """
        { "id": "F-2", "extract": "text",
          "source": { "name": "Census", "locator": "p. 1", "class": "original", "information": "primary" },
          "target": { "xref": "@I1@" },
          "people": [ { "xref": "@NewI1@", "name": "Mary /Test/", "sex": "f" } ],
          "relations": [ { "kind": "spouse", "person": "@I1@", "spouse": "@NewI1@", "family": "@NewF1@", "quay": 2 } ] }
        """;

    [Fact]
    public void PeopleAndRelations_AreRead()
    {
        var (findings, errors) = Read($"[{WithRelations}]");

        Assert.Empty(errors);
        Assert.Equal(new DraftPerson("@NewI1@", "Mary /Test/", "F"), Assert.Single(findings[0].People));
        var relation = Assert.Single(findings[0].Relations);
        Assert.Equal(("spouse", "@I1@", "@NewI1@", "@NewF1@", 2), (relation.Kind, relation.Person, relation.Other, relation.Family, relation.Quay));
        Assert.Empty(relation.NotDuplicateOf);
    }

    [Fact]
    public void ARelationWithoutAQuay_NamesTheFieldByPath()
    {
        var (_, errors) = Read($"[{WithRelations.Replace(", \"quay\": 2", "")}]");

        Assert.Contains("findings[0].relations[0].quay is required", Assert.Single(errors));
    }

    [Fact]
    public void AFieldThatDoesNotApplyToTheKind_IsRejected()
    {
        var (_, errors) = Read($"[{WithRelations.Replace("\"family\": \"@NewF1@\"", "\"family\": \"@NewF1@\", \"husb\": \"@I1@\"")}]");

        Assert.Contains("findings[0].relations[0].husb does not apply to a spouse relation", Assert.Single(errors));
    }

    [Fact]
    public void AChildRelation_NeedsAFamilyAndNoPerson()
    {
        var (findings, errors) = Read($"[{WithRelations.Replace("\"kind\": \"spouse\", \"person\": \"@I1@\", \"spouse\": \"@NewI1@\"", "\"kind\": \"child\", \"child\": \"@NewI1@\"")}]");

        Assert.Empty(errors);
        Assert.Equal("child", findings[0].Relations[0].Kind);
    }

    [Fact]
    public void AChildRelationWithFrom_ReadsTheFamilyTheChildLeaves()
    {
        var (findings, errors) = Read($"[{WithRelations.Replace("\"kind\": \"spouse\", \"person\": \"@I1@\", \"spouse\": \"@NewI1@\"", "\"kind\": \"child\", \"child\": \"@I5@\", \"from\": \"@F1@\"")}]");

        Assert.Empty(errors);
        Assert.Equal("@F1@", findings[0].Relations[0].From);
    }

    [Fact]
    public void FromOnASpouseRelation_IsRejected()
    {
        var (_, errors) = Read($"[{WithRelations.Replace("\"family\": \"@NewF1@\"", "\"family\": \"@NewF1@\", \"from\": \"@F1@\"")}]");

        Assert.Contains("findings[0].relations[0].from does not apply to a spouse relation", Assert.Single(errors));
    }

    [Fact]
    public void AnUnlinkChild_NeedsNoQuayAndNamesTheFamily()
    {
        var (findings, errors) = Read($"[{WithRelations.Replace("\"kind\": \"spouse\", \"person\": \"@I1@\", \"spouse\": \"@NewI1@\", \"family\": \"@NewF1@\", \"quay\": 2", "\"kind\": \"Unlink-Child\", \"child\": \"@I5@\", \"family\": \"@F1@\"")}]");

        Assert.Empty(errors);
        var relation = Assert.Single(findings[0].Relations);
        Assert.Equal((DraftRelation.UnlinkChild, "@I5@", "@F1@"), (relation.Kind, relation.Other, relation.Family));
        Assert.True(relation.IsRemoval);
    }

    [Fact]
    public void AnUnlinkParent_NeedsAPersonAndARoleButNoFamily()
    {
        var (findings, errors) = Read($"[{WithRelations.Replace("\"kind\": \"spouse\", \"person\": \"@I1@\", \"spouse\": \"@NewI1@\", \"family\": \"@NewF1@\", \"quay\": 2", "\"kind\": \"unlink-parent\", \"person\": \"@I1@\", \"role\": \"mother\"")}]");

        Assert.Empty(errors);
        var relation = Assert.Single(findings[0].Relations);
        Assert.Equal((DraftRelation.UnlinkParent, "@I1@", "mother", null), (relation.Kind, relation.Person, relation.Role, relation.Family));
    }

    [Theory]
    [InlineData("quay")]
    [InlineData("excerpt")]
    public void ARemovalTakesNoCitationFields(string field)
    {
        string value = field == "quay" ? "2" : "\"x\"";
        var (_, errors) = Read($"[{WithRelations.Replace("\"kind\": \"spouse\", \"person\": \"@I1@\", \"spouse\": \"@NewI1@\", \"family\": \"@NewF1@\", \"quay\": 2", $"\"kind\": \"unlink-child\", \"child\": \"@I5@\", \"family\": \"@F1@\", \"{field}\": {value}")}]");

        Assert.Contains($"findings[0].relations[0].{field} does not apply to a unlink-child relation; its fields are kind, child, family.", errors);
    }

    [Fact]
    public void AParentRelation_NeedsARole()
    {
        var (_, errors) = Read($"[{WithRelations.Replace("\"kind\": \"spouse\", \"person\": \"@I1@\", \"spouse\": \"@NewI1@\", \"family\": \"@NewF1@\"", "\"kind\": \"parent\", \"person\": \"@I1@\", \"parent\": \"@NewI1@\"")}]");

        Assert.Contains("findings[0].relations[0].role is required.", errors);
    }

    [Fact]
    public void ARelationOnlyFinding_NeedsNoFacts()
    {
        var (findings, errors) = Read($"[{WithRelations}]");

        Assert.Empty(errors);
        Assert.Empty(findings[0].Facts);
    }

    [Fact]
    public void ATextFactValue_IsReadAndCannotBeCombinedWithADate()
    {
        var valued = Valid.Replace("\"date\": \"1 Jan 1900\"", "\"value\": \"Ralph /Morrill/\"");
        var (findings, errors) = Read($"[{valued}]");
        Assert.Empty(errors);
        Assert.Equal("Ralph /Morrill/", findings[0].Facts[0].Value);

        var (_, both) = Read($"[{Valid.Replace("\"date\": \"1 Jan 1900\"", "\"date\": \"1900\", \"value\": \"x\"")}]");
        Assert.Contains("findings[0].facts[0].value is a text fact's payload", Assert.Single(both));
    }

    [Fact]
    public void NotDuplicateOf_IsReadOnASpouseAndRejectedOnAParent()
    {
        var (findings, errors) = Read($"[{WithRelations.Replace("\"quay\": 2 }", "\"quay\": 2, \"notDuplicateOf\": [\"@I5@\"] }")}]");
        Assert.Empty(errors);
        Assert.Equal(["@I5@"], findings[0].Relations[0].NotDuplicateOf);

        var parent = WithRelations.Replace("\"kind\": \"spouse\", \"person\": \"@I1@\", \"spouse\": \"@NewI1@\", \"family\": \"@NewF1@\"",
            "\"kind\": \"parent\", \"person\": \"@I1@\", \"parent\": \"@NewI1@\", \"role\": \"father\", \"notDuplicateOf\": [\"@I5@\"]");
        Assert.Contains("notDuplicateOf does not apply to a parent relation", Assert.Single(Read($"[{parent}]").Errors));
    }

    [Fact]
    public void FactSubstructuresReplacedCitationsAndCiteOnly_AreRead()
    {
        var fact = Valid.Replace("\"date\": \"1 Jan 1900\", \"quay\": 2",
            "\"date\": \"1 Jan 1900\", \"quay\": 2, \"substructures\": [ { \"tag\": \"type\", \"value\": \"Pension\" } ], \"replacedCitations\": \"moveToNote\"");
        var (findings, errors) = Read($"[{fact}]");
        Assert.Empty(errors);
        var read = findings[0].Facts[0];
        Assert.Equal(new DraftSubstructure("TYPE", "Pension"), Assert.Single(read.Substructures));
        Assert.Equal("moveToNote", read.ReplacedCitations);

        var citeOnly = Valid.Replace("\"date\": \"1 Jan 1900\", \"quay\": 2", "\"quay\": 2, \"citeOnly\": true");
        var (cited, citeErrors) = Read($"[{citeOnly}]");
        Assert.Empty(citeErrors);
        Assert.True(cited[0].Facts[0].CiteOnly);
    }

    [Fact]
    public void CiteOnlyBesideAValue_IsRejected()
    {
        var (_, errors) = Read($"[{Valid.Replace("\"quay\": 2", "\"quay\": 2, \"citeOnly\": true, \"value\": \"x\"")}]");

        Assert.Contains("citeOnly adds a citation to an existing fact", Assert.Single(errors));
    }

    [Fact]
    public void ANoteMatchAndCites_AreRead()
    {
        var note = Valid.Replace("\"text\": \"A note.\"", "\"text\": \"A note.\", \"match\": \"Old.\", \"cites\": [ { \"finding\": \"F-9\", \"quay\": 1, \"excerpt\": \"x\" }, { \"quay\": 2 } ]");
        var (findings, errors) = Read($"[{note}]");

        Assert.Empty(errors);
        var read = findings[0].Notes[0];
        Assert.Equal("Old.", read.Match);
        Assert.Equal([new DraftNoteCitation("F-9", 1, "x"), new DraftNoteCitation(null, 2, null)], read.Cites);
    }

    [Fact]
    public void ANoteCitationWithoutAQuay_NamesTheFieldByPath()
    {
        var note = Valid.Replace("\"text\": \"A note.\"", "\"text\": \"A note.\", \"cites\": [ { \"finding\": \"F-9\" } ]");

        Assert.Contains("findings[0].notes[0].cites[0].quay is required", Assert.Single(Read($"[{note}]").Errors));
    }

    [Fact]
    public void ARelationExcerpt_IsRead()
    {
        var (findings, errors) = Read($"[{WithRelations.Replace("\"quay\": 2 }", "\"quay\": 2, \"excerpt\": \"Mary 35\" }")}]");

        Assert.Empty(errors);
        Assert.Equal("Mary 35", findings[0].Relations[0].Excerpt);
    }

    [Fact]
    public void ANotDuplicateOfThatIsNotAnArrayOfStrings_IsRejectedByPath()
    {
        var (_, notArray) = Read($"[{WithRelations.Replace("\"quay\": 2 }", "\"quay\": 2, \"notDuplicateOf\": \"@I5@\" }")}]");
        var (_, badItem) = Read($"[{WithRelations.Replace("\"quay\": 2 }", "\"quay\": 2, \"notDuplicateOf\": [ 5 ] }")}]");

        Assert.Contains("findings[0].relations[0].notDuplicateOf must be an array of strings", Assert.Single(notArray));
        Assert.Contains("findings[0].relations[0].notDuplicateOf[0] must be a non-blank string", Assert.Single(badItem));
    }
}
