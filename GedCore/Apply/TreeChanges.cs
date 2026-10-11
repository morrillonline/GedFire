namespace GedCore.Apply;

/// <summary>A dated or placed fact present after a changeset that was not present before.</summary>
public sealed record FactWrite(string Record, string Tag, string? Date, string? Place);

/// <summary>A person's primary name before and after; <see cref="Before"/> is null for a new person.</summary>
public sealed record NameChange(string? Before, string? After);

/// <summary>A family's partners and children after the changeset, and which of them it added.</summary>
public sealed record FamilyChange(
    IReadOnlyList<string> Partners, IReadOnlyList<string> Children,
    IReadOnlyList<string> AddedPartners, IReadOnlyList<string> AddedChildren);

/// <summary>
/// What a changeset actually changed in the tree, found by comparing the touched
/// records before and after: a citation-only edit writes no fact, and a re-stated
/// identical fact is not a write.
/// </summary>
public sealed class TreeChanges
{
    public static readonly TreeChanges None = new([], new Dictionary<string, NameChange>(), new Dictionary<string, FamilyChange>());

    public IReadOnlyList<FactWrite> Facts { get; }
    public IReadOnlyDictionary<string, NameChange> Names { get; }
    public IReadOnlyDictionary<string, FamilyChange> Families { get; }

    /// <summary>Every person or family record the changeset touched, whether or not it wrote a fact there (a citation or note touches).</summary>
    public IReadOnlySet<string> Touched { get; }

    public TreeChanges(
        IReadOnlyList<FactWrite> facts, IReadOnlyDictionary<string, NameChange> names,
        IReadOnlyDictionary<string, FamilyChange> families, IReadOnlySet<string>? touched = null)
    {
        Facts = facts;
        Names = names;
        Families = families;
        Touched = touched ?? new HashSet<string>();
    }

    public static TreeChanges Between(GedDocument before, GedDocument after, IEnumerable<GedRecord> touchedRoots)
    {
        var facts = new List<FactWrite>();
        var names = new Dictionary<string, NameChange>(StringComparer.Ordinal);
        var families = new Dictionary<string, FamilyChange>(StringComparer.Ordinal);

        var touched = new HashSet<string>(StringComparer.Ordinal);
        foreach (var root in touchedRoots.Where(r => r.Xref is not null && r.Tag is "INDI" or "FAM"))
        {
            touched.Add(root.Xref!);
            string xref = root.Xref!;
            before.ByXref.TryGetValue(xref, out var prior);
            facts.AddRange(WrittenFacts(xref, prior, root));
            if (root.Tag == "INDI") AddNameChange(names, xref, prior, root);
            else families[xref] = FamilyDiff(prior, root);
        }
        return new TreeChanges(facts, names, families, touched);
    }

    static IEnumerable<FactWrite> WrittenFacts(string xref, GedRecord? prior, GedRecord current)
    {
        var remaining = prior is null ? new List<(string Tag, string? Date, string? Place)>() : FactSignatures(prior).ToList();
        foreach (var fact in FactSignatures(current))
        {
            if (remaining.Remove(fact)) continue;
            yield return new FactWrite(xref, fact.Tag, fact.Date, fact.Place);
        }
    }

    static IEnumerable<(string Tag, string? Date, string? Place)> FactSignatures(GedRecord record) =>
        record.Children
            .Where(c => c.Tag is not ("CHAN" or "CREA"))
            .Where(c => c.FirstChild("DATE") is not null || c.FirstChild("PLAC") is not null)
            .Select(c => (c.Tag, Payload(c.FirstChild("DATE")), Payload(c.FirstChild("PLAC"))));

    static string? Payload(GedRecord? node) => node?.FullValue().Trim() is { Length: > 0 } text ? text : null;

    static void AddNameChange(Dictionary<string, NameChange> names, string xref, GedRecord? prior, GedRecord current)
    {
        string? after = DisplayName(current);
        string? previous = prior is null ? null : DisplayName(prior);
        if (after != previous) names[xref] = new NameChange(previous, after);
    }

    static string? DisplayName(GedRecord person)
    {
        string? raw = person.FirstChild("NAME")?.FullValue();
        return raw is null ? null : string.Join(' ', raw.Replace("/", " ").Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    static FamilyChange FamilyDiff(GedRecord? prior, GedRecord current)
    {
        var partners = Pointers(current, "HUSB", "WIFE");
        var children = Pointers(current, "CHIL");
        var oldPartners = prior is null ? new List<string>() : Pointers(prior, "HUSB", "WIFE");
        var oldChildren = prior is null ? new List<string>() : Pointers(prior, "CHIL");
        return new FamilyChange(partners, children, [.. partners.Except(oldPartners)], [.. children.Except(oldChildren)]);
    }

    static List<string> Pointers(GedRecord family, params string[] tags) =>
        [.. family.Children.Where(c => tags.Contains(c.Tag) && c.IsPointerValue).Select(c => c.Value)];
}
