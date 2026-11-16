namespace GedCore.Drafting;

/// <summary>
/// The findings a draft's cites may name: the findings it drafts, then findings that are only cited, never drafted (such
/// as a finding about another person, read from the file beside the one that cites it). A drafted finding wins an id.
/// </summary>
internal sealed class CitableFindings
{
    readonly Dictionary<string, DraftFinding> _byId = new(StringComparer.Ordinal);

    public CitableFindings(IReadOnlyList<DraftFinding> drafted, IReadOnlyList<DraftFinding> citedOnly)
    {
        foreach (var finding in drafted) _byId.TryAdd(finding.Id, finding);
        CitedOnly = [.. citedOnly.Where(f => _byId.TryAdd(f.Id, f))];
    }

    /// <summary>The cited findings that are not drafted, without duplicates.</summary>
    public IReadOnlyList<DraftFinding> CitedOnly { get; }

    public DraftFinding? Find(string id) => _byId.GetValueOrDefault(id);

    /// <summary>The finding a cite names: <paramref name="owner"/> itself when the cite names none.</summary>
    public DraftFinding? Cited(DraftFinding owner, DraftCite cite) => cite.Finding is null ? owner : Find(cite.Finding);
}
