using System.Text.Json;
using GedCore;
using GedFire.Match;

namespace GedFire.Mcp;

// ---------------------------------------------------------------------------
// Parses and validates match_record's raw arguments into a RecordDescription,
// naming the offending field in every rejection so a wrong shape is never
// reduced to a generic "error occurred".
// ---------------------------------------------------------------------------

public static class MatchRecordRequestReader
{
    public const int MaxPersons = 50;

    public static bool TryRead(
        JsonElement persons, JsonElement relationships, JsonElement recordEvent, JsonElement focus,
        out RecordDescription? record, out string? error)
    {
        record = null;
        if (!TryReadEvent(recordEvent, out var eventDescription, out error)) return false;
        if (!TryReadPersons(persons, eventDescription?.Year, out var personList, out error)) return false;
        if (!TryReadRelationships(relationships, personList.Count, out var relationshipList, out error)) return false;
        if (!TryReadFocus(focus, personList.Count, out int? focusIndex, out error)) return false;

        record = new RecordDescription
        {
            Persons = personList,
            Relationships = relationshipList,
            Event = eventDescription,
            Focus = focusIndex,
        };
        return true;
    }

    /// <summary>Reads the optional maxCandidates (1-20, default 5) and compact flag.</summary>
    public static bool TryReadView(JsonElement maxCandidates, JsonElement compact, out RecordMatchView? view, out string? error)
    {
        view = null;
        if (!ToolArguments.TryReadOptionalInt(maxCandidates, "maxCandidates", 1, RecordMatchView.MaxCandidatesLimit,
                RecordMatchView.DefaultMaxCandidates, out int max, out error)) return false;
        if (!ToolArguments.TryReadOptionalBool(compact, "compact", out bool isCompact, out error)) return false;
        view = new RecordMatchView(max, isCompact);
        return true;
    }

    static bool TryReadEvent(JsonElement element, out RecordEventDescription? recordEvent, out string? error)
    {
        recordEvent = null;
        error = null;
        if (IsAbsent(element)) return true;
        if (element.ValueKind != JsonValueKind.Object)
        {
            error = $"event must be an object with optional date, place, and type, not {ToolArguments.Describe(element)}.";
            return false;
        }

        string? date = null, place = null, type = null;
        foreach (var property in element.EnumerateObject())
        {
            switch (property.Name)
            {
                case "date": if (!ReadString(property.Value, "event.date", out date, out error)) return false; break;
                case "place": if (!ReadString(property.Value, "event.place", out place, out error)) return false; break;
                case "type": if (!ReadString(property.Value, "event.type", out type, out error)) return false; break;
                default:
                    error = $"event has an unrecognized property \"{property.Name}\". Accepted properties: date, place, type.";
                    return false;
            }
        }

        int year = GedDate.ParseYear(date);
        if (date != null && year == 0)
        {
            error = $"event.date must contain a year, got \"{date}\".";
            return false;
        }

        recordEvent = new RecordEventDescription { Year = year != 0 ? year : null, Place = place, Type = type };
        return true;
    }

    static bool TryReadPersons(
        JsonElement element, int? eventYear, out List<RecordPersonDescription> persons, out string? error)
    {
        persons = [];
        if (IsAbsent(element))
        {
            error = "persons is required: an array of 1 to 50 record persons, each with at least a name.";
            return false;
        }
        if (element.ValueKind != JsonValueKind.Array)
        {
            error = $"persons must be an array of objects, not {ToolArguments.Describe(element)}.";
            return false;
        }
        int count = element.GetArrayLength();
        if (count < 1 || count > MaxPersons)
        {
            error = $"persons must contain 1 to {MaxPersons} entries, got {count}.";
            return false;
        }

        int index = 0;
        foreach (var item in element.EnumerateArray())
        {
            if (!TryReadPerson(item, index, eventYear, out var person, out error)) return false;
            persons.Add(person!);
            index++;
        }
        error = null;
        return true;
    }

    static bool TryReadPerson(
        JsonElement element, int index, int? eventYear, out RecordPersonDescription? person, out string? error)
    {
        person = null;
        string field = $"persons[{index}]";
        if (element.ValueKind != JsonValueKind.Object)
        {
            error = $"{field} must be an object with at least a name, not {ToolArguments.Describe(element)}.";
            return false;
        }

        string? role = null, name = null, birthPlace = null;
        bool? isMale = null;
        int? birthYear = null, age = null;
        foreach (var property in element.EnumerateObject())
        {
            switch (property.Name)
            {
                case "role": if (!ReadString(property.Value, $"{field}.role", out role, out error)) return false; break;
                case "name": if (!ReadString(property.Value, $"{field}.name", out name, out error)) return false; break;
                case "birthPlace": if (!ReadString(property.Value, $"{field}.birthPlace", out birthPlace, out error)) return false; break;
                case "sex": if (!ReadSex(property.Value, $"{field}.sex", out isMale, out error)) return false; break;
                case "birthYear": if (!ReadInt(property.Value, $"{field}.birthYear", 1, 9999, out birthYear, out error)) return false; break;
                case "age": if (!ReadInt(property.Value, $"{field}.age", 0, 120, out age, out error)) return false; break;
                default:
                    error = $"{field} has an unrecognized property \"{property.Name}\". Accepted properties: role, name, sex, birthYear, age, birthPlace.";
                    return false;
            }
        }

        if (name is null)
        {
            error = $"{field}.name is required: the name as the record spells it.";
            return false;
        }
        if (birthYear != null && age != null)
        {
            error = $"{field}: give birthYear or age, not both.";
            return false;
        }
        if (age is int statedAge)
        {
            if (eventYear is not int year)
            {
                error = $"{field}.age needs event.date with a year to compute a birth year.";
                return false;
            }
            birthYear = year - statedAge;
        }

        person = new RecordPersonDescription
        {
            Name = name,
            Role = role,
            IsMale = isMale,
            BirthYear = birthYear,
            BirthPlace = birthPlace,
        };
        error = null;
        return true;
    }

    static bool TryReadRelationships(
        JsonElement element, int personCount, out List<RecordRelationship> relationships, out string? error)
    {
        relationships = [];
        error = null;
        if (IsAbsent(element)) return true;
        if (element.ValueKind != JsonValueKind.Array)
        {
            error = $"relationships must be an array of objects, not {ToolArguments.Describe(element)}.";
            return false;
        }

        int index = 0;
        foreach (var item in element.EnumerateArray())
        {
            if (!TryReadRelationship(item, index, personCount, out var relationship, out error)) return false;
            relationships.Add(relationship!);
            index++;
        }
        return true;
    }

    static bool TryReadRelationship(
        JsonElement element, int index, int personCount, out RecordRelationship? relationship, out string? error)
    {
        relationship = null;
        string field = $"relationships[{index}]";
        if (element.ValueKind != JsonValueKind.Object)
        {
            error = $"{field} must be an object with type, from, and to, not {ToolArguments.Describe(element)}.";
            return false;
        }

        string? type = null;
        int? from = null, to = null;
        foreach (var property in element.EnumerateObject())
        {
            switch (property.Name)
            {
                case "type": if (!ReadString(property.Value, $"{field}.type", out type, out error)) return false; break;
                case "from": if (!ReadInt(property.Value, $"{field}.from", 0, personCount - 1, out from, out error)) return false; break;
                case "to": if (!ReadInt(property.Value, $"{field}.to", 0, personCount - 1, out to, out error)) return false; break;
                default:
                    error = $"{field} has an unrecognized property \"{property.Name}\". Accepted properties: type, from, to.";
                    return false;
            }
        }

        if (type is null || from is null || to is null)
        {
            error = $"{field} must carry type, from, and to.";
            return false;
        }
        if (from == to)
        {
            error = $"{field}: from and to must name two different persons, both are {from}.";
            return false;
        }
        RecordRelationshipKind? kind = type switch
        {
            "spouse-of" => RecordRelationshipKind.SpouseOf,
            "child-of" => RecordRelationshipKind.ChildOf,
            "parent-of" => RecordRelationshipKind.ParentOf,
            "sibling-of" => RecordRelationshipKind.SiblingOf,
            _ => null,
        };
        if (kind is null)
        {
            error = $"{field}.type must be spouse-of, child-of, parent-of, or sibling-of, got \"{type}\".";
            return false;
        }

        relationship = new RecordRelationship(kind.Value, from.Value, to.Value);
        error = null;
        return true;
    }

    static bool TryReadFocus(JsonElement element, int personCount, out int? focus, out string? error)
    {
        focus = null;
        error = null;
        if (IsAbsent(element)) return true;
        if (!ReadInt(element, "focus", 0, personCount - 1, out focus, out error)) return false;
        return true;
    }

    static bool ReadString(JsonElement element, string field, out string? value, out string? error)
    {
        value = null;
        error = null;
        string? text = element.ValueKind == JsonValueKind.String ? element.GetString()!.Trim() : null;
        if (string.IsNullOrEmpty(text))
        {
            error = $"{field} must be a non-blank string, not {(element.ValueKind == JsonValueKind.String ? "a blank string" : ToolArguments.Describe(element))}.";
            return false;
        }
        value = text;
        return true;
    }

    static bool ReadSex(JsonElement element, string field, out bool? isMale, out string? error)
    {
        isMale = null;
        error = null;
        string? text = element.ValueKind == JsonValueKind.String ? element.GetString()!.Trim().ToUpperInvariant() : null;
        if (text is "M" or "F")
        {
            isMale = text == "M";
            return true;
        }
        error = $"{field} must be \"M\" or \"F\", not {(element.ValueKind == JsonValueKind.String ? $"\"{element.GetString()}\"" : ToolArguments.Describe(element))}.";
        return false;
    }

    static bool ReadInt(JsonElement element, string field, int min, int max, out int? value, out string? error)
    {
        value = null;
        error = null;
        if (element.ValueKind != JsonValueKind.Number || !element.TryGetInt32(out int parsed) || parsed < min || parsed > max)
        {
            error = $"{field} must be an integer between {min} and {max}, not {ToolArguments.Describe(element)}.";
            return false;
        }
        value = parsed;
        return true;
    }

    static bool IsAbsent(JsonElement element) =>
        element.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null;
}
