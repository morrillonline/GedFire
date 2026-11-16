using GedCore.Drafting;

namespace GedFire.Mcp;

// ---------------------------------------------------------------------------
// The findings staged beside a finding file: a cite of a finding not given
// with it is read from <id>.json in the citing file's folder, whatever that
// finding's target or status, so it can be cited without being drafted.
// ---------------------------------------------------------------------------

public static class StagedFindings
{
    /// <summary>
    /// The drafts of the files beside the given finding files, named <c>&lt;id&gt;.json</c>, that their notes and facts cite and
    /// that are not themselves among the given findings. A file with no draft entry is not a finding that can be cited.
    /// </summary>
    public static IReadOnlyList<DraftFinding> CitedBeside(IEnumerable<(string File, DraftFinding Draft)> findings)
    {
        var given = findings.ToList();
        var known = given.Select(g => g.Draft.Id).ToHashSet(StringComparer.Ordinal);
        var found = new List<DraftFinding>();
        foreach (var (file, draft) in given)
        {
            if (Path.GetDirectoryName(file) is not { } folder) continue;
            foreach (string id in draft.CitedIds.Where(id => !known.Contains(id) && IsFileName(id)))
            {
                string path = Path.Combine(folder, id + ".json");
                if (File.Exists(path) && FindingFileReader.ReadPath(path).Draft is { } cited && cited.Id == id && known.Add(id))
                    found.Add(cited);
            }
        }
        return found;
    }

    // A cited id names a file only when it cannot reach outside the folder.
    static bool IsFileName(string id) => id.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && !id.StartsWith('.');
}
