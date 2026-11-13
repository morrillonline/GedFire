using GedCore.Apply;

namespace GedCore.Drafting;

/// <summary>
/// One xref a finding names, at <paramref name="Path"/> within the finding (e.g. <c>facts[0].record</c>).
/// A placeholder is <paramref name="Declared"/> when the finding's people declare it or one of its relations names it as a family.
/// </summary>
public sealed record DraftReference(string Xref, string Path, bool IsPlaceholder, bool Declared);

/// <summary>Every xref a finding binds or asserts about, in the order the finding gives them.</summary>
public static class DraftReferences
{
    public const string Void = "@VOID@";

    public static IReadOnlyList<DraftReference> Of(DraftFinding finding)
    {
        var declared = finding.People.Select(p => p.Xref)
            .Concat(finding.Relations.Where(r => !r.IsRemoval).Select(r => r.Family).OfType<string>())
            .ToHashSet(StringComparer.Ordinal);
        return [.. Named(finding)
            .Where(r => r.Xref.Length > 0 && r.Xref != Void)
            .Select(r => Placeholder.IsPlaceholder(r.Xref)
                ? new DraftReference(r.Xref, r.Path, true, declared.Contains(r.Xref))
                : new DraftReference(r.Xref, r.Path, false, false))];
    }

    static IEnumerable<(string Xref, string Path)> Named(DraftFinding finding)
    {
        yield return (finding.Target.Xref, "target.xref");
        for (int i = 0; i < finding.ExistingPeople.Count; i++) yield return (finding.ExistingPeople[i], $"existingPeople[{i}]");
        for (int i = 0; i < finding.Facts.Count; i++) yield return (finding.Facts[i].Record, $"facts[{i}].record");
        for (int i = 0; i < finding.Relations.Count; i++)
            foreach (var named in RelationRefs(finding.Relations[i], $"relations[{i}]")) yield return named;
        for (int i = 0; i < finding.Notes.Count; i++) yield return (finding.Notes[i].Record, $"notes[{i}].record");
    }

    static IEnumerable<(string Xref, string Path)> RelationRefs(DraftRelation relation, string path)
    {
        string otherField = relation.Kind switch
        {
            DraftRelation.Spouse => "spouse",
            DraftRelation.Parent => "parent",
            _ => "child",
        };
        var fields = new (string? Xref, string Field)[]
        {
            (relation.Person, "person"), (relation.Other, otherField), (relation.Husb, "husb"), (relation.Wife, "wife"),
            (relation.Family, "family"), (relation.From, "from"),
        };
        foreach (var (xref, field) in fields)
            if (xref is not null) yield return (xref, $"{path}.{field}");
        for (int i = 0; i < relation.NotDuplicateOf.Count; i++)
            yield return (relation.NotDuplicateOf[i], $"{path}.notDuplicateOf[{i}]");
    }
}
