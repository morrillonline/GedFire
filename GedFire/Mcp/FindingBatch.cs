using GedCore.Drafting;

namespace GedFire.Mcp;

// ---------------------------------------------------------------------------
// The finding files validate_finding checks together. A note or fact may cite
// any other finding of the batch, or a finding staged beside the citing file,
// <id>.json, so a write-time check passes before the batch exists.
// ---------------------------------------------------------------------------

public sealed class FindingBatch
{
    public IReadOnlyList<FindingFile> Files { get; }

    /// <summary>Every draft a note or fact of the batch may cite.</summary>
    public IReadOnlyList<DraftFinding> Citable { get; }

    FindingBatch(IReadOnlyList<FindingFile> files, IReadOnlyList<DraftFinding> citable) => (Files, Citable) = (files, citable);

    /// <summary>Reads the named files, in order.</summary>
    public static FindingBatch Read(IEnumerable<string> names) =>
        Of([.. names.Select(name => FindingFileReader.ReadPath(Path.GetFullPath(name)))]);

    public static FindingBatch Of(IReadOnlyList<FindingFile> files)
    {
        var drafts = files.Where(f => f.Draft is not null).Select(f => (f.Name, Draft: f.Draft!)).ToList();
        return new FindingBatch(files, [.. drafts.Select(d => d.Draft), .. StagedFindings.CitedBeside(drafts)]);
    }
}
