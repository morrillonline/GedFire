using System.Text.Json;
using GedFire.Mcp;

namespace GedCore.Tests;

public class MatchRecordRequestReaderTests
{
    static JsonElement Json(string text) => JsonDocument.Parse(text).RootElement.Clone();

    static (bool Ok, GedFire.Match.RecordDescription? Record, string? Error) Read(
        string? persons = null, string? relationships = null, string? recordEvent = null, string? focus = null)
    {
        bool ok = MatchRecordRequestReader.TryRead(
            persons is null ? default : Json(persons),
            relationships is null ? default : Json(relationships),
            recordEvent is null ? default : Json(recordEvent),
            focus is null ? default : Json(focus),
            out var record, out string? error);
        return (ok, record, error);
    }

    [Fact]
    public void MinimalRecord_OnePersonWithAName_Reads()
    {
        var (ok, record, error) = Read(persons: """[{ "name": "John Ashworth" }]""");

        Assert.True(ok, error);
        var person = Assert.Single(record!.Persons);
        Assert.Equal("John Ashworth", person.Name);
        Assert.Null(person.Role);
        Assert.Null(person.IsMale);
        Assert.Null(person.BirthYear);
        Assert.Null(record.Event);
        Assert.Null(record.Focus);
        Assert.Empty(record.Relationships);
    }

    [Fact]
    public void FullRecord_ReadsEveryField()
    {
        var (ok, record, error) = Read(
            persons: """
                [
                  { "role": "head", "name": "John Ashworth", "sex": "M", "birthYear": 1848, "birthPlace": "Iowa" },
                  { "role": "wife", "name": "Mary Ashworth", "sex": "F" }
                ]
                """,
            relationships: """[{ "type": "spouse-of", "from": 1, "to": 0 }]""",
            recordEvent: """{ "date": "12 JUN 1880", "place": "Boone County, Iowa", "type": "census" }""",
            focus: "0");

        Assert.True(ok, error);
        Assert.Equal(2, record!.Persons.Count);
        Assert.Equal("head", record.Persons[0].Role);
        Assert.True(record.Persons[0].IsMale);
        Assert.Equal(1848, record.Persons[0].BirthYear);
        Assert.Equal("Iowa", record.Persons[0].BirthPlace);
        Assert.False(record.Persons[1].IsMale);
        var relationship = Assert.Single(record.Relationships);
        Assert.Equal((GedFire.Match.RecordRelationshipKind.SpouseOf, 1, 0), (relationship.Kind, relationship.From, relationship.To));
        Assert.Equal(1880, record.Event!.Year);
        Assert.Equal("Boone County, Iowa", record.Event.Place);
        Assert.Equal("census", record.Event.Type);
        Assert.Equal(0, record.Focus);
    }

    [Fact]
    public void Age_WithAnEventYear_BecomesABirthYear()
    {
        var (ok, record, error) = Read(
            persons: """[{ "name": "John Ashworth", "age": 32 }]""",
            recordEvent: """{ "date": "1880" }""");

        Assert.True(ok, error);
        Assert.Equal(1848, record!.Persons[0].BirthYear);
    }

    [Fact]
    public void MissingPersons_IsAnError()
    {
        var (ok, _, error) = Read();
        Assert.False(ok);
        Assert.StartsWith("persons is required", error);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("\"John\"")]
    public void EmptyOrNonArrayPersons_IsAnError(string persons)
    {
        var (ok, _, error) = Read(persons: persons);
        Assert.False(ok);
        Assert.StartsWith("persons must", error);
    }

    [Fact]
    public void PersonWithoutAName_IsAnErrorNamingTheIndex()
    {
        var (ok, _, error) = Read(persons: """[{ "name": "John" }, { "role": "wife" }]""");
        Assert.False(ok);
        Assert.Equal("persons[1].name is required: the name as the record spells it.", error);
    }

    [Fact]
    public void PersonWithAnUnknownProperty_IsAnError()
    {
        var (ok, _, error) = Read(persons: """[{ "name": "John", "surname": "Ashworth" }]""");
        Assert.False(ok);
        Assert.Contains("persons[0] has an unrecognized property \"surname\"", error);
    }

    [Fact]
    public void BirthYearAndAgeTogether_IsAnError()
    {
        var (ok, _, error) = Read(
            persons: """[{ "name": "John", "birthYear": 1848, "age": 32 }]""",
            recordEvent: """{ "date": "1880" }""");
        Assert.False(ok);
        Assert.Equal("persons[0]: give birthYear or age, not both.", error);
    }

    [Fact]
    public void AgeWithoutAnEventYear_IsAnError()
    {
        var (ok, _, error) = Read(persons: """[{ "name": "John", "age": 32 }]""");
        Assert.False(ok);
        Assert.Equal("persons[0].age needs event.date with a year to compute a birth year.", error);
    }

    [Fact]
    public void BadSex_IsAnError()
    {
        var (ok, _, error) = Read(persons: """[{ "name": "John", "sex": "male" }]""");
        Assert.False(ok);
        Assert.Equal("persons[0].sex must be \"M\" or \"F\", not \"male\".", error);
    }

    [Fact]
    public void UnknownRelationshipType_IsAnError()
    {
        var (ok, _, error) = Read(
            persons: """[{ "name": "John" }, { "name": "Mary" }]""",
            relationships: """[{ "type": "married-to", "from": 0, "to": 1 }]""");
        Assert.False(ok);
        Assert.Equal("relationships[0].type must be spouse-of, child-of, parent-of, or sibling-of, got \"married-to\".", error);
    }

    [Fact]
    public void RelationshipIndexOutOfRange_IsAnError()
    {
        var (ok, _, error) = Read(
            persons: """[{ "name": "John" }, { "name": "Mary" }]""",
            relationships: """[{ "type": "spouse-of", "from": 0, "to": 2 }]""");
        Assert.False(ok);
        Assert.StartsWith("relationships[0].to must be an integer between 0 and 1", error);
    }

    [Fact]
    public void RelationshipFromSelfToSelf_IsAnError()
    {
        var (ok, _, error) = Read(
            persons: """[{ "name": "John" }, { "name": "Mary" }]""",
            relationships: """[{ "type": "spouse-of", "from": 1, "to": 1 }]""");
        Assert.False(ok);
        Assert.Equal("relationships[0]: from and to must name two different persons, both are 1.", error);
    }

    [Fact]
    public void EventDateWithoutAYear_IsAnError()
    {
        var (ok, _, error) = Read(
            persons: """[{ "name": "John" }]""",
            recordEvent: """{ "date": "sometime" }""");
        Assert.False(ok);
        Assert.Equal("event.date must contain a year, got \"sometime\".", error);
    }

    [Fact]
    public void FocusOutOfRange_IsAnError()
    {
        var (ok, _, error) = Read(persons: """[{ "name": "John" }]""", focus: "1");
        Assert.False(ok);
        Assert.StartsWith("focus must be an integer between 0 and 0", error);
    }
}
