using GedCore.Drafting;

namespace GedFire.Mcp;

// ---------------------------------------------------------------------------
// The finding files validate_finding checks together. A note may cite any
// other finding of the batch; a file checked alone may also cite the file
// beside it, <id>.json, so a write-time check passes before the batch exists.
// ---------------------------------------------------------------------------

public sealed class FindingBatch
{
    public IReadOnlyList<FindingFile> Files { get; }

    /// <summary>Every draft a note of the batch may cite.</summary>
    public IReadOnlyList<DraftFinding> Citable { get; }

    FindingBatch(IReadOnlyList<FindingFile> files, IReadOnlyList<DraftFinding> citable) => (Files, Citable) = (files, citable);

    /// <summary>Reads the named files, in order.</summary>
    public static FindingBatch Read(IEnumerable<string> names) =>
        Of([.. names.Select(name => FindingFileReader.ReadPath(Path.GetFullPath(name)))]);

    public static FindingBatch Of(IReadOnlyList<FindingFile> files)
    {
        List<DraftFinding> citable = [.. files.Select(f => f.Draft).OfType<DraftFinding>()];
        if (files.Count == 1) citable.AddRange(CitedSiblings(files[0]));
        return new FindingBatch(files, citable);
    }

    // The drafts of the files beside the file, named <id>.json, that its notes cite.
    static IEnumerable<DraftFinding> CitedSiblings(FindingFile file)
    {
        if (file.Draft is not { } draft || Path.GetDirectoryName(file.Name) is not { } folder) return [];
        return draft.Notes.SelectMany(n => n.Cites).Select(c => c.Finding).OfType<string>()
            .Where(id => id != draft.Id && IsFileName(id))
            .Distinct(StringComparer.Ordinal)
            .Select(id => Path.Combine(folder, id + ".json"))
            .Where(File.Exists)
            .Select(path => FindingFileReader.ReadPath(path).Draft)
            .OfType<DraftFinding>();
    }

    // A cited id names a file only when it cannot reach outside the folder.
    static bool IsFileName(string id) => id.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && !id.StartsWith('.');
}
