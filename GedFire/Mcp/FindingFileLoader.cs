using System.Text.Json;
using System.Text.RegularExpressions;
using GedCore.Drafting;

namespace GedFire.Mcp;

// ---------------------------------------------------------------------------
// Loads finding files for draft_changeset's findingPaths. A file that is not
// ready (no draft, or draftGaps) is reported by name so the stage that owns
// the gap can fix it, rather than compiling a draft with a hole in it.
// ---------------------------------------------------------------------------

/// <summary>One finding file as loaded for drafting: its draft when ready, otherwise why not.</summary>
public sealed record LoadedFindingFile(string Name, DraftFinding? Finding, IReadOnlyList<string> Errors);

/// <summary>The drafts of a set of finding files; <see cref="Files"/> holds each finding's file, in order.</summary>
public sealed record LoadedFindings(IReadOnlyList<DraftFinding> Findings, IReadOnlyList<string> Files, IReadOnlyList<string> Errors);

public static partial class FindingFileLoader
{
    /// <summary>Reads every finding file named in <paramref name="paths"/>; the findings are empty when any file has an error, each naming its file.</summary>
    public static LoadedFindings Load(JsonElement paths)
    {
        if (!ToolArguments.TryReadOptionalStringList(paths, "findingPaths", out var names, out string? error))
            return new LoadedFindings([], [], [error!]);
        if (names.Count == 0)
            return new LoadedFindings([], [], ["findingPaths must name at least one finding file."]);

        var loaded = LoadAll(names);
        var errors = loaded.SelectMany(f => f.Errors).ToList();
        if (errors.Count > 0) return new LoadedFindings([], [], errors);

        return new LoadedFindings(DraftNoteJoiner.Join([.. loaded.Select(f => f.Finding!)]), [.. loaded.Select(f => f.Name)], []);
    }

    /// <summary>Reads each named file, in order; a file that cannot be read comes back with its error.</summary>
    public static IReadOnlyList<LoadedFindingFile> LoadAll(IEnumerable<string> names) =>
        [.. names.Select(name => Path.GetFullPath(name)).Select(path => ForDrafting(FindingFileReader.ReadPath(path)))];

    /// <summary>Reads one finding file's stream; <paramref name="name"/> labels its errors.</summary>
    public static LoadedFindingFile Read(Stream stream, string name) => ForDrafting(FindingFileReader.Read(stream, name));

    /// <summary>Reads the draft entry of one finding file's stream; <paramref name="name"/> labels its errors.</summary>
    public static bool TryRead(Stream stream, string name, out DraftFinding? finding, out List<string> errors)
    {
        var loaded = Read(stream, name);
        finding = loaded.Finding;
        errors = [.. loaded.Errors];
        return finding is not null;
    }

    /// <summary>The file as draft_changeset takes it: anything that keeps it from drafting is an error.</summary>
    public static LoadedFindingFile ForDrafting(FindingFile file)
    {
        IReadOnlyList<string> errors = file.NotReadyReason is { } reason ? [reason, .. file.Errors] : file.Errors;
        return new LoadedFindingFile(file.Name, errors.Count == 0 ? file.Draft : null, errors);
    }

    /// <summary>Rewrites the <c>findings[i]</c> a draft check names to the file the i-th finding came from.</summary>
    public static IReadOnlyList<string> NameFiles(IReadOnlyList<string> errors, IReadOnlyList<string> files) =>
        [.. errors.Select(e => FindingIndex().Replace(e, m =>
            int.Parse(m.Groups[1].Value) is var i && i < files.Count ? $"{files[i]} (findings[{i}])" : m.Value))];

    [GeneratedRegex(@"findings\[(\d+)\]")]
    private static partial Regex FindingIndex();
}
