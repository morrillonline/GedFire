using System.Text.Json;

namespace GedCore.Drafting;

/// <summary>Strict reader for the findings a draft is compiled from; every rejection names the field by path.</summary>
public sealed class DraftFindingReader
{
    static readonly string[] SourceClasses = ["original", "image copy", "derivative", "authored", "user-contributed"];
    static readonly string[] InformationClasses = ["primary", "secondary", "undetermined"];
    static readonly string[] RelationKinds =
        [DraftRelation.Spouse, DraftRelation.Child, DraftRelation.Parent, DraftRelation.UnlinkChild, DraftRelation.UnlinkParent];

    readonly List<string> _errors = [];

    public static (IReadOnlyList<DraftFinding> Findings, IReadOnlyList<string> Errors) Read(JsonElement findings)
    {
        var reader = new DraftFindingReader();
        var result = reader.ReadAll(findings);
        return (result, reader._errors);
    }

    List<DraftFinding> ReadAll(JsonElement findings)
    {
        var result = new List<DraftFinding>();
        if (findings.ValueKind != JsonValueKind.Array || findings.GetArrayLength() == 0)
        {
            _errors.Add("findings must be a non-empty array of finding objects.");
            return result;
        }

        int index = 0;
        foreach (var element in findings.EnumerateArray())
        {
            if (ReadFinding(element, $"findings[{index++}]") is { } finding) result.Add(finding);
        }
        return result;
    }

    DraftFinding? ReadFinding(JsonElement el, string path)
    {
        if (!IsObject(el, path, "id", "extract", "source", "target", "facts", "notes", "people", "relations", "argument")) return null;
        int before = _errors.Count;

        string? id = Text(el, "id", path, required: true);
        string? extract = Text(el, "extract", path, required: true);
        var source = ReadSource(Child(el, "source", path), $"{path}.source");
        var target = ReadTarget(Child(el, "target", path), $"{path}.target");
        var facts = ReadList(el, "facts", path, ReadFact);
        var notes = ReadList(el, "notes", path, ReadNote);
        var people = ReadList(el, "people", path, ReadPerson);
        var relations = ReadList(el, "relations", path, ReadRelation);
        var argument = el.TryGetProperty("argument", out var given) && given.ValueKind != JsonValueKind.Null
            ? FindingArgumentReader.Read(given, $"{path}.argument", _errors)
            : null;

        if (_errors.Count > before || id is null || extract is null || source is null || target is null) return null;
        return new DraftFinding(id, source, extract, target, facts, notes) { People = people, Relations = relations, Argument = argument };
    }

    DraftSource? ReadSource(JsonElement? el, string path)
    {
        if (el is not { } source
            || !IsObject(source, path, "name", "author", "url", "locator", "accessed", "class", "information")) return null;
        string? name = Text(source, "name", path, required: true);
        string? locator = Text(source, "locator", path, required: true);
        string? cls = Choice(source, "class", path, SourceClasses, required: true);
        string? info = Choice(source, "information", path, InformationClasses, required: true);
        if (name is null || locator is null || cls is null || info is null) return null;
        return new DraftSource(name, Text(source, "author", path), Text(source, "url", path), locator, Text(source, "accessed", path), cls, info);
    }

    DraftTarget? ReadTarget(JsonElement? el, string path)
    {
        if (el is not { } target || !IsObject(target, path, "xref", "card", "name")) return null;
        string? xref = Text(target, "xref", path, required: true);
        if (xref is null) return null;
        if (!xref.StartsWith('@') || !xref.EndsWith('@')) _errors.Add($"{path}.xref must be an xref such as \"@I1@\", got \"{xref}\".");
        return new DraftTarget(xref, Text(target, "card", path), Text(target, "name", path));
    }

    DraftFact? ReadFact(JsonElement el, string path)
    {
        if (!IsObject(el, path, "record", "fact", "date", "place", "value", "quay", "class", "information", "excerpt", "substructures", "replacedCitations", "citeOnly", "replaceHeld", "withdraw")) return null;
        int before = _errors.Count;
        string? record = Text(el, "record", path, required: true);
        string? tag = Text(el, "fact", path, required: true);
        string? date = Text(el, "date", path);
        string? place = Text(el, "place", path);
        string? value = Text(el, "value", path);
        string? cls = Choice(el, "class", path, SourceClasses, required: false);
        string? info = Choice(el, "information", path, InformationClasses, required: false);
        string? replaced = Choice(el, "replacedCitations", path, ["keep", "drop", "movetonote"], required: false);
        var substructures = ReadList(el, "substructures", path, ReadSubstructure);
        bool citeOnly = ReadFlag(el, "citeOnly", path);
        string? replaceHeld = Text(el, "replaceHeld", path);
        string? withdraw = Text(el, "withdraw", path);
        int quay = ReadQuay(el, path, "this fact (not inherited from the finding)");

        if (withdraw is not null)
        {
            if (value is not null || substructures.Count > 0 || replaced is not null || citeOnly || replaceHeld is not null)
                _errors.Add($"{path}.withdraw deletes the fact the record holds; value, substructures, replacedCitations, citeOnly and replaceHeld do not apply.");
        }
        else if (citeOnly)
        {
            if (value is not null || substructures.Count > 0 || replaced is not null || replaceHeld is not null)
                _errors.Add($"{path}.citeOnly adds a citation to an existing fact; value, substructures, replacedCitations and replaceHeld do not apply.");
        }
        else if (value is not null && (date is not null || place is not null))
            _errors.Add($"{path}.value is a text fact's payload and cannot be combined with a date or place.");
        else if (value is null && date is null && place is null)
            _errors.Add($"{path} needs a date, a place or a value.");

        if (_errors.Count > before || record is null || tag is null) return null;
        return new DraftFact(record, tag.ToUpperInvariant(), date, place, quay, cls, info, Text(el, "excerpt", path), value)
        {
            Substructures = substructures,
            ReplacedCitations = replaced is null ? null : replaced == "movetonote" ? "moveToNote" : replaced,
            CiteOnly = citeOnly,
            ReplaceHeld = replaceHeld,
            Withdraw = withdraw,
        };
    }

    DraftSubstructure? ReadSubstructure(JsonElement el, string path)
    {
        if (!IsObject(el, path, "tag", "value")) return null;
        string? tag = Text(el, "tag", path, required: true);
        string? value = Text(el, "value", path, required: true);
        return tag is null || value is null ? null : new DraftSubstructure(tag.ToUpperInvariant(), value);
    }

    bool ReadFlag(JsonElement el, string name, string path)
    {
        if (!el.TryGetProperty(name, out var flag) || flag.ValueKind == JsonValueKind.Null) return false;
        if (flag.ValueKind is JsonValueKind.True or JsonValueKind.False) return flag.GetBoolean();
        _errors.Add($"{path}.{name} must be true or false.");
        return false;
    }

    int ReadQuay(JsonElement el, string path, string graded)
    {
        int quay = 0;
        if (!el.TryGetProperty("quay", out var q) || q.ValueKind != JsonValueKind.Number || !q.TryGetInt32(out quay) || quay is < 0 or > 3)
            _errors.Add($"{path}.quay is required: an integer 0-3, graded for {graded}.");
        return quay;
    }

    DraftPerson? ReadPerson(JsonElement el, string path)
    {
        if (!IsObject(el, path, "xref", "name", "sex")) return null;
        string? xref = Text(el, "xref", path, required: true);
        string? name = Text(el, "name", path, required: true);
        string? sex = Choice(el, "sex", path, ["m", "f"], required: false)?.ToUpperInvariant();
        return xref is null || name is null ? null : new DraftPerson(xref, name, sex);
    }

    DraftRelation? ReadRelation(JsonElement el, string path)
    {
        if (!IsObject(el, path, "kind", "person", "spouse", "child", "parent", "role", "family", "from", "husb", "wife", "notDuplicateOf", "excerpt", "quay")) return null;
        int before = _errors.Count;
        string? kind = Choice(el, "kind", path, RelationKinds, required: true);
        if (kind is null) return null;

        bool removal = kind is DraftRelation.UnlinkChild or DraftRelation.UnlinkParent;
        bool aboutAParent = kind is DraftRelation.Parent or DraftRelation.UnlinkParent;
        string? person = Text(el, "person", path, required: kind is not (DraftRelation.Child or DraftRelation.UnlinkChild));
        string? other = kind == DraftRelation.UnlinkParent ? "" : Text(el, kind == DraftRelation.UnlinkChild ? "child" : kind, path, required: true);
        string? family = Text(el, "family", path, required: !aboutAParent);
        string? role = Choice(el, "role", path, ["father", "mother"], required: aboutAParent);
        string? husb = Text(el, "husb", path);
        string? wife = Text(el, "wife", path);
        string? from = Text(el, "from", path);
        var notDuplicateOf = ReadStrings(el, "notDuplicateOf", path);
        RejectForeignFields(el, path, kind);

        int quay = removal ? 0 : ReadQuay(el, path, "this relationship");

        if (_errors.Count > before || other is null) return null;
        return new DraftRelation(kind, person ?? "", other, family, role, husb, wife, quay)
        {
            NotDuplicateOf = notDuplicateOf, Excerpt = Text(el, "excerpt", path), From = from,
        };
    }

    // Each kind names its own partner field and takes only the fields its op reads.
    // A removal is not cited, so it takes no quay or excerpt.
    void RejectForeignFields(JsonElement el, string path, string kind)
    {
        string[] allowed = kind switch
        {
            DraftRelation.Spouse => ["person", "spouse", "family", "notDuplicateOf"],
            DraftRelation.Child => ["child", "family", "from", "husb", "wife", "notDuplicateOf"],
            DraftRelation.UnlinkChild => ["child", "family"],
            DraftRelation.UnlinkParent => ["person", "role", "family"],
            _ => ["person", "parent", "role", "family"],
        };
        bool removal = kind is DraftRelation.UnlinkChild or DraftRelation.UnlinkParent;
        string[] named = removal
            ? ["person", "spouse", "child", "parent", "role", "family", "from", "husb", "wife", "notDuplicateOf", "quay", "excerpt"]
            : ["person", "spouse", "child", "parent", "role", "family", "from", "husb", "wife", "notDuplicateOf"];
        string fields = string.Join(", ", removal ? allowed : ["quay", .. allowed]);
        foreach (string name in named)
            if (!allowed.Contains(name) && el.TryGetProperty(name, out _))
                _errors.Add($"{path}.{name} does not apply to a {kind} relation; its fields are kind, {fields}.");
    }

    DraftNote? ReadNote(JsonElement el, string path)
    {
        if (!IsObject(el, path, "record", "text", "match", "cites")) return null;
        int before = _errors.Count;
        string? record = Text(el, "record", path, required: true);
        string? text = Text(el, "text", path, required: true);
        string? match = Text(el, "match", path);
        var cites = ReadList(el, "cites", path, ReadNoteCitation);
        if (_errors.Count > before || record is null || text is null) return null;
        return new DraftNote(record, text) { Match = match, Cites = cites };
    }

    DraftNoteCitation? ReadNoteCitation(JsonElement el, string path)
    {
        if (!IsObject(el, path, "finding", "quay", "excerpt")) return null;
        int before = _errors.Count;
        string? finding = Text(el, "finding", path);
        string? excerpt = Text(el, "excerpt", path);
        int quay = ReadQuay(el, path, "this note citation");
        return _errors.Count > before ? null : new DraftNoteCitation(finding, quay, excerpt);
    }

    List<T> ReadList<T>(JsonElement el, string name, string path, Func<JsonElement, string, T?> read) where T : class
    {
        var result = new List<T>();
        if (!el.TryGetProperty(name, out var list) || list.ValueKind == JsonValueKind.Null) return result;
        if (list.ValueKind != JsonValueKind.Array)
        {
            _errors.Add($"{path}.{name} must be an array.");
            return result;
        }

        int index = 0;
        foreach (var item in list.EnumerateArray())
            if (read(item, $"{path}.{name}[{index++}]") is { } value) result.Add(value);
        return result;
    }

    List<string> ReadStrings(JsonElement el, string name, string path)
    {
        var result = new List<string>();
        if (!el.TryGetProperty(name, out var list) || list.ValueKind == JsonValueKind.Null) return result;
        if (list.ValueKind != JsonValueKind.Array)
        {
            _errors.Add($"{path}.{name} must be an array of strings.");
            return result;
        }

        int index = 0;
        foreach (var item in list.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(item.GetString())) result.Add(item.GetString()!.Trim());
            else _errors.Add($"{path}.{name}[{index}] must be a non-blank string.");
            index++;
        }
        return result;
    }

    JsonElement? Child(JsonElement el, string name, string path)
    {
        if (el.TryGetProperty(name, out var child) && child.ValueKind != JsonValueKind.Null) return child;
        _errors.Add($"{path}.{name} is required.");
        return null;
    }

    bool IsObject(JsonElement el, string path, params string[] accepted)
    {
        if (el.ValueKind != JsonValueKind.Object)
        {
            _errors.Add($"{path} must be an object.");
            return false;
        }

        var unknown = el.EnumerateObject().Select(p => p.Name).Where(n => !accepted.Contains(n)).ToList();
        foreach (string name in unknown)
            _errors.Add($"{path}.{name} is not a recognized field; accepted fields: {string.Join(", ", accepted)}.");
        return unknown.Count == 0;
    }

    string? Text(JsonElement el, string name, string path, bool required = false)
    {
        if (!el.TryGetProperty(name, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            if (required) _errors.Add($"{path}.{name} is required.");
            return null;
        }
        if (value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(value.GetString()))
        {
            _errors.Add($"{path}.{name} must be a non-blank string.");
            return null;
        }
        return value.GetString()!.Trim();
    }

    string? Choice(JsonElement el, string name, string path, string[] allowed, bool required)
    {
        string? value = Text(el, name, path, required)?.ToLowerInvariant().Replace('_', ' ');
        if (value is null) return null;
        if (!allowed.Contains(value))
        {
            _errors.Add($"{path}.{name} must be one of {string.Join(", ", allowed)}, got \"{value}\".");
            return null;
        }
        return value;
    }
}
