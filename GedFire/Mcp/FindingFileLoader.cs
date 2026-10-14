using System.Text.Json;
using System.Text.RegularExpressions;
using GedCore.Drafting;

namespace GedFire.Mcp;

// ---------------------------------------------------------------------------
// Reads the lake's correlated finding files for draft_changeset's findingPaths
// and draft_package. Each file carries the finding's complete draft_changeset
// entry under "draft" and what it could not place under "draftGaps"; a file
// that is not ready (no draft, or draftGaps) is reported by name so the stage
// that owns the gap can fix it, rather than compiling a draft with a hole in it.
// ---------------------------------------------------------------------------

/// <summary>One finding file as read: its draft when ready, otherwise why not, and the anchor it was correlated to when that could be read.</summary>
public sealed record LoadedFindingFile(string Name, string? Anchor, DraftFinding? Finding, IReadOnlyList<string> Errors);

public static partial class FindingFileLoader
{
    /// <summary>Reads every finding file named in <paramref name="paths"/>; false with every error, each naming its file. <paramref name="files"/> holds each finding's file, in order.</summary>
    public static bool TryLoad(JsonElement paths, out IReadOnlyList<DraftFinding> findings, out IReadOnlyList<string> files, out List<string> errors)
    {
        findings = [];
        files = [];
        errors = [];
        if (!ToolArguments.TryReadOptionalStringList(paths, "findingPaths", out var names, out string? error))
        {
            errors.Add(error!);
            return false;
        }
        if (names.Count == 0)
        {
            errors.Add("findingPaths must name at least one finding file.");
            return false;
        }

        var loaded = LoadAll(names);
        errors.AddRange(loaded.SelectMany(f => f.Errors));
        if (errors.Count > 0) return false;

        findings = DraftNoteJoiner.Join([.. loaded.Select(f => f.Finding!)]);
        files = [.. loaded.Select(f => f.Name)];
        return true;
    }

    /// <summary>Reads each named file, in order; a file that cannot be read comes back with its error.</summary>
    public static IReadOnlyList<LoadedFindingFile> LoadAll(IEnumerable<string> names) =>
        [.. names.Select(name => Path.GetFullPath(name)).Select(ReadFile)];

    static LoadedFindingFile ReadFile(string fullPath)
    {
        try
        {
            using var stream = File.OpenRead(fullPath);
            return Read(stream, fullPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return new LoadedFindingFile(fullPath, null, null, [$"{fullPath} could not be read: {ex.Message}"]);
        }
    }

    /// <summary>Reads one finding file's stream; <paramref name="name"/> labels its errors.</summary>
    public static LoadedFindingFile Read(Stream stream, string name)
    {
        JsonElement root;
        try
        {
            using var document = JsonDocument.Parse(stream);
            root = document.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            return Failed(name, null, $"{name} is not valid JSON: {ex.Message}");
        }

        if (root.ValueKind != JsonValueKind.Object)
            return Failed(name, null, $"{name} must hold a finding object, not {ToolArguments.Describe(root)}.");

        string? anchor = AnchorOf(root);
        var errors = new List<string>();
        var gaps = GapsOf(root, name, errors);
        if (errors.Count > 0) return new LoadedFindingFile(name, anchor, null, errors);
        if (gaps.Count > 0)
            return Failed(name, anchor, $"{name} has draftGaps: {string.Join("; ", gaps)}. Supply them at the stage that owns them and re-run correlation with --force.");

        if (!root.TryGetProperty("draft", out var draft) || draft.ValueKind == JsonValueKind.Null)
            return Failed(name, anchor, IsParked(root)
                ? $"{name} is parked; a park verdict writes no draft."
                : $"{name} has no draft; correlate the finding to write one.");

        using var single = JsonDocument.Parse("[" + draft.GetRawText() + "]");
        var (read, readErrors) = DraftFindingReader.Read(single.RootElement);
        if (readErrors.Count > 0)
            return new LoadedFindingFile(name, anchor, null, [.. readErrors.Select(e => $"{name}: {e.Replace("findings[0]", "draft")}")]);

        return new LoadedFindingFile(name, anchor ?? read[0].Target.Xref, read[0], []);
    }

    /// <summary>Reads the draft entry of one finding file's stream; <paramref name="name"/> labels its errors.</summary>
    public static bool TryRead(Stream stream, string name, out DraftFinding? finding, out List<string> errors)
    {
        var loaded = Read(stream, name);
        finding = loaded.Finding;
        errors = [.. loaded.Errors];
        return finding is not null;
    }

    /// <summary>Rewrites the <c>findings[i]</c> a draft check names to the file the i-th finding came from.</summary>
    public static IReadOnlyList<string> NameFiles(IReadOnlyList<string> errors, IReadOnlyList<string> files) =>
        [.. errors.Select(e => FindingIndex().Replace(e, m =>
            int.Parse(m.Groups[1].Value) is var i && i < files.Count ? $"{files[i]} (findings[{i}])" : m.Value))];

    [GeneratedRegex(@"findings\[(\d+)\]")]
    private static partial Regex FindingIndex();

    static LoadedFindingFile Failed(string name, string? anchor, string error) => new(name, anchor, null, [error]);

    static string? AnchorOf(JsonElement root) =>
        root.TryGetProperty("correlation", out var correlation) && correlation.ValueKind == JsonValueKind.Object
        && correlation.TryGetProperty("target", out var target) && target.ValueKind == JsonValueKind.String
        && !string.IsNullOrWhiteSpace(target.GetString())
            ? target.GetString()!.Trim()
            : null;

    static List<string> GapsOf(JsonElement root, string name, List<string> errors)
    {
        var gaps = new List<string>();
        if (!root.TryGetProperty("draftGaps", out var list) || list.ValueKind == JsonValueKind.Null) return gaps;
        if (list.ValueKind != JsonValueKind.Array)
        {
            errors.Add($"{name}: draftGaps must be an array of strings.");
            return gaps;
        }

        gaps.AddRange(list.EnumerateArray().Select(g => g.ValueKind == JsonValueKind.String ? g.GetString()! : g.GetRawText()));
        return gaps;
    }

    static bool IsParked(JsonElement root) =>
        root.TryGetProperty("correlation", out var correlation)
        && correlation.ValueKind == JsonValueKind.Object
        && correlation.TryGetProperty("kind", out var kind)
        && kind.ValueKind == JsonValueKind.String
        && kind.GetString() == "park";
}
