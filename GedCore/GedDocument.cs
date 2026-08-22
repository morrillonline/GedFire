namespace GedCore;

/// <summary>
/// A parsed GEDCOM document.  Version-agnostic: produced by any parser,
/// consumed by any formatter.
/// </summary>
public sealed class GedDocument
{
    private readonly List<GedRecord> _records;

    /// <summary>
    /// Level-0 records in source order (HEAD, SUBM, INDI, FAM, SOUR, TRLR, …).
    /// Each carries its full subtree of children.
    /// </summary>
    public IReadOnlyList<GedRecord> Records => _records;

    /// <summary>
    /// Mutable view of <see cref="Records"/> for upgrade transforms that add
    /// or remove level-0 records.  Callers must keep <see cref="ByXref"/>
    /// consistent (see RebuildXrefIndex).
    /// </summary>
    internal List<GedRecord> MutableRecords => _records;

    /// <summary>Level-0 records indexed by xref ID (e.g. "@I00001@").</summary>
    public IReadOnlyDictionary<string, GedRecord> ByXref { get; internal set; } =
        new Dictionary<string, GedRecord>();

    /// <summary>
    /// The document's declared GEDCOM version (HEAD.GEDC.VERS, e.g. "7.0" or
    /// "5.5.1"), or null if the header or that structure is absent. Mirrors
    /// the lookup ConformanceChecker's GED011 check already performs.
    /// </summary>
    public string? Version =>
        Records.FirstOrDefault(r => r.Tag == "HEAD")?.FirstChild("GEDC")?.FirstChild("VERS")?.Value;

    internal GedDocument(List<GedRecord> records) => _records = records;

    /// <summary>Regenerate <see cref="ByXref"/> after level-0 mutations.</summary>
    internal void RebuildXrefIndex() =>
        ByXref = Records.Where(r => r.Xref is not null)
                        .ToDictionary(r => r.Xref!);
}
