using System.Text.Json;

namespace GedCore.Drafting;

/// <summary>Strict reader for the findings a draft is compiled from; every rejection names the field by path.</summary>
public sealed class DraftFindingReader
{
    static readonly string[] SourceClasses = ["original", "image copy", "derivative", "authored", "user-contributed"];
    static readonly string[] InformationClasses = ["primary", "secondary", "undetermined"];

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
        if (!IsObject(el, path, "id", "extract", "source", "target", "facts", "notes")) return null;
        int before = _errors.Count;

        string? id = Text(el, "id", path, required: true);
        string? extract = Text(el, "extract", path, required: true);
        var source = ReadSource(Child(el, "source", path), $"{path}.source");
        var target = ReadTarget(Child(el, "target", path), $"{path}.target");
        var facts = ReadList(el, "facts", path, ReadFact);
        var notes = ReadList(el, "notes", path, ReadNote);

        if (facts.Count == 0 && notes.Count == 0) _errors.Add($"{path}.facts must hold at least one fact (or {path}.notes at least one note).");
        if (_errors.Count > before || id is null || extract is null || source is null || target is null) return null;
        return new DraftFinding(id, source, extract, target, facts, notes);
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
        if (!IsObject(el, path, "record", "fact", "date", "place", "quay", "class", "information", "excerpt")) return null;
        int before = _errors.Count;
        string? record = Text(el, "record", path, required: true);
        string? tag = Text(el, "fact", path, required: true);
        string? date = Text(el, "date", path);
        string? place = Text(el, "place", path);
        string? cls = Choice(el, "class", path, SourceClasses, required: false);
        string? info = Choice(el, "information", path, InformationClasses, required: false);

        int quay = 0;
        if (!el.TryGetProperty("quay", out var q) || q.ValueKind != JsonValueKind.Number || !q.TryGetInt32(out quay) || quay is < 0 or > 3)
            _errors.Add($"{path}.quay is required: an integer 0-3, graded for this fact (not inherited from the finding).");
        if (date is null && place is null) _errors.Add($"{path} needs a date or a place.");

        if (_errors.Count > before || record is null || tag is null) return null;
        return new DraftFact(record, tag.ToUpperInvariant(), date, place, quay, cls, info, Text(el, "excerpt", path));
    }

    DraftNote? ReadNote(JsonElement el, string path)
    {
        if (!IsObject(el, path, "record", "text")) return null;
        string? record = Text(el, "record", path, required: true);
        string? text = Text(el, "text", path, required: true);
        return record is null || text is null ? null : new DraftNote(record, text);
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
