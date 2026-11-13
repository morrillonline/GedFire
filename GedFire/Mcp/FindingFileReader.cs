using System.Text.Json;
using System.Text.Json.Nodes;
using GedCore.Drafting;

namespace GedFire.Mcp;

// ---------------------------------------------------------------------------
// Reads one finding file against the finding-file contract (gedfire schema
// finding-file): draft, argument, draftGaps, record and the caller's meta.
// Shape faults are errors; what only blocks drafting (no draft, draftGaps)
// is left to the caller to treat as an error or as "not ready". A top-level
// key outside the contract, and a draft.id that differs from the file name,
// are warnings for now and will be refused in a later release.
// ---------------------------------------------------------------------------

/// <summary>One finding file as read against the finding-file contract.</summary>
public sealed record FindingFile(string Name)
{
    /// <summary>draft.id as written, even when the draft is otherwise invalid; null when there is none.</summary>
    public string? Id { get; init; }

    /// <summary>The draft entry with any argument beside it applied; null when absent or invalid.</summary>
    public DraftFinding? Draft { get; init; }

    public bool HasDraft { get; init; }

    /// <summary>Whether the file held a JSON object at all; readiness is judged only then.</summary>
    public bool Parsed { get; init; }

    /// <summary>The record slot, already checked as match_record input; null when absent.</summary>
    public JsonElement? Record { get; init; }

    public IReadOnlyList<string> Gaps { get; init; } = [];
    public bool Parked { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];

    public bool IsValid => Errors.Count == 0;

    /// <summary>Why the file cannot be drafted although its shape is valid, or null when it can.</summary>
    public string? NotReadyReason =>
        !Parsed ? null
        : Gaps.Count > 0 ? $"{Name} has draftGaps: {string.Join("; ", Gaps)}. Supply them at the stage that owns them and re-run correlation with --force."
        : !HasDraft ? (Parked ? $"{Name} is parked; a park verdict writes no draft." : $"{Name} has no draft; correlate the finding to write one.")
        : null;

    public bool ReadyToDraft => IsValid && NotReadyReason is null;
}

public static class FindingFileReader
{
    public static readonly IReadOnlyList<string> Keys = ["draft", "argument", "draftGaps", "record", "meta"];

    static readonly string[] RecordKeys = ["persons", "relationships", "event"];

    /// <summary>Reads the file at <paramref name="fullPath"/>; a file that cannot be opened comes back with that error.</summary>
    public static FindingFile ReadPath(string fullPath)
    {
        try
        {
            using var stream = File.OpenRead(fullPath);
            return Read(stream, fullPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return new FindingFile(fullPath) { Errors = [$"{fullPath} could not be read: {ex.Message}"] };
        }
    }

    /// <summary>Reads one finding file's stream; <paramref name="name"/> labels its errors and is checked against draft.id.</summary>
    public static FindingFile Read(Stream stream, string name)
    {
        JsonElement root;
        try
        {
            using var document = JsonDocument.Parse(stream);
            root = document.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            return new FindingFile(name) { Errors = [$"{name} is not valid JSON: {ex.Message}"] };
        }
        if (root.ValueKind != JsonValueKind.Object)
            return new FindingFile(name) { Errors = [$"{name} must hold a finding object, not {ToolArguments.Describe(root)}."] };

        var errors = new List<string>();
        var warnings = UnknownKeyWarnings(root, name).ToList();
        var gaps = ReadGaps(root, name, errors);
        CheckMeta(root, name, errors);
        var record = ReadRecord(root, name, errors);
        var argument = ReadArgument(root, name, errors);
        var (id, draft, hasDraft) = ReadDraft(root, name, argument, errors);
        if (id is not null && Path.GetFileNameWithoutExtension(name) != id)
            warnings.Add($"{name}: draft.id \"{id}\" differs from the file name; name the file {id}.json. This will be refused in a later release.");

        return new FindingFile(name)
        {
            Parsed = true, Id = id, Draft = draft, HasDraft = hasDraft, Record = record, Gaps = gaps, Parked = IsParked(root),
            Errors = errors, Warnings = warnings,
        };
    }

    static IEnumerable<string> UnknownKeyWarnings(JsonElement root, string name) =>
        root.EnumerateObject().Select(p => p.Name).Where(key => !Keys.Contains(key))
            .Select(key => $"{name}: \"{key}\" is not a finding-file key; accepted keys: {string.Join(", ", Keys)}. It is ignored now and will be refused in a later release.");

    static List<string> ReadGaps(JsonElement root, string name, List<string> errors)
    {
        var gaps = new List<string>();
        if (!root.TryGetProperty("draftGaps", out var list) || list.ValueKind == JsonValueKind.Null) return gaps;
        if (list.ValueKind != JsonValueKind.Array)
        {
            errors.Add($"{name}: draftGaps must be an array of strings.");
            return gaps;
        }

        int index = 0;
        foreach (var gap in list.EnumerateArray())
        {
            if (gap.ValueKind == JsonValueKind.String) gaps.Add(gap.GetString()!);
            else errors.Add($"{name}: draftGaps[{index}] must be a string.");
            index++;
        }
        return gaps;
    }

    // meta belongs to the caller: only its slot's shape is checked, never its content.
    static void CheckMeta(JsonElement root, string name, List<string> errors)
    {
        if (root.TryGetProperty("meta", out var meta) && meta.ValueKind is not (JsonValueKind.Object or JsonValueKind.Null))
            errors.Add($"{name}: meta must be an object.");
    }

    static JsonElement? ReadRecord(JsonElement root, string name, List<string> errors)
    {
        if (!root.TryGetProperty("record", out var record) || record.ValueKind == JsonValueKind.Null) return null;
        if (record.ValueKind != JsonValueKind.Object)
        {
            errors.Add($"{name}: record must be an object with persons, and optional relationships and event.");
            return null;
        }

        int before = errors.Count;
        foreach (var key in record.EnumerateObject().Select(p => p.Name).Where(key => !RecordKeys.Contains(key)))
            errors.Add($"{name}: record.{key} is not a recognized field; accepted fields: {string.Join(", ", RecordKeys)}.");
        if (errors.Count == before && !MatchRecordRequestReader.TryRead(
                Member(record, "persons"), Member(record, "relationships"), Member(record, "event"), default, out _, out string? error))
            errors.Add($"{name}: record.{error}");
        return errors.Count == before ? record : null;
    }

    static JsonElement Member(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) ? value : default;

    static (string? Id, DraftFinding? Draft, bool HasDraft) ReadDraft(JsonElement root, string name, JsonObject? argument, List<string> errors)
    {
        if (!root.TryGetProperty("draft", out var draft) || draft.ValueKind == JsonValueKind.Null) return (null, null, false);
        string? id = draft.ValueKind == JsonValueKind.Object && draft.TryGetProperty("id", out var given)
                     && given.ValueKind == JsonValueKind.String ? given.GetString() : null;

        using var single = JsonDocument.Parse("[" + draft.GetRawText() + "]");
        var (read, readErrors) = DraftFindingReader.Read(single.RootElement);
        errors.AddRange(readErrors.Select(e => $"{name}: {e.Replace("findings[0]", "draft")}"));
        if (readErrors.Count > 0) return (id, null, true);
        return (id, argument is null ? read[0] : read[0] with { Argument = argument }, true);
    }

    static JsonObject? ReadArgument(JsonElement root, string name, List<string> errors)
    {
        if (!root.TryGetProperty("argument", out var argument) || argument.ValueKind == JsonValueKind.Null) return null;
        var argumentErrors = new List<string>();
        var carried = FindingArgumentReader.Read(argument, "argument", argumentErrors);
        errors.AddRange(argumentErrors.Select(e => $"{name}: {e}"));
        return carried;
    }

    // Deprecated: correlation is the caller's field. Removed when unknown keys are refused.
    static bool IsParked(JsonElement root) =>
        root.TryGetProperty("correlation", out var correlation)
        && correlation.ValueKind == JsonValueKind.Object
        && correlation.TryGetProperty("kind", out var kind)
        && kind.ValueKind == JsonValueKind.String
        && kind.GetString() == "park";
}
