using GedCore.Apply;

namespace GedCore.Drafting;

/// <summary>
/// A spouse relation between two people the document already holds as partners compiles onto the family they share
/// (<paramref name="Family"/>), in place of the new family the finding <paramref name="Named"/> or the none it gave.
/// <paramref name="Finding"/> and <paramref name="Relation"/> index the findings and that finding's relations.
/// </summary>
public sealed record FamilySubstitution(int Finding, int Relation, string Person, string Spouse, string? Named, string Family)
{
    /// <summary>The relation's family field, e.g. relations[0].family.</summary>
    public string Path => $"relations[{Relation}].family";
}

/// <summary>Keeps a spouse relation between existing partners on their family, so a draft never duplicates a marriage.</summary>
internal static class SpouseFamilies
{
    sealed record Couple(int Finding, int Relation, DraftRelation Link, IReadOnlyList<string> Shared);

    public static IEnumerable<string> Errors(IReadOnlyList<DraftFinding> findings, IFamilyRecords? families) =>
        Couples(findings, families).Select(Error).OfType<string>();

    // A family the relation names must be the one the couple share; with none named, they must share only one.
    static string? Error(Couple couple)
    {
        string path = $"findings[{couple.Finding}].relations[{couple.Relation}]";
        var (person, spouse, named) = (couple.Link.Person, couple.Link.Other, couple.Link.Family);
        string shared = string.Join(", ", couple.Shared);
        bool namesExisting = named is not null && !Placeholder.IsPlaceholder(named);
        if (namesExisting && !couple.Shared.Contains(named!))
            return $"{path}.family {named} is not the family of {person} and {spouse}, who are already partners in {shared}; name that family or leave family out.";
        if (!namesExisting && couple.Shared.Count > 1)
            return $"{path}: {person} and {spouse} are already partners in {couple.Shared.Count} families ({shared}); give the family this relation belongs to.";
        return null;
    }
    /// <summary>The relations that name a new family, or none, for partners who already share exactly one.</summary>
    public static IReadOnlyList<FamilySubstitution> Find(IReadOnlyList<DraftFinding> findings, IFamilyRecords? families) =>
        [.. Couples(findings, families)
            .Where(c => c.Shared.Count == 1 && (c.Link.Family is null || Placeholder.IsPlaceholder(c.Link.Family)))
            .Select(c => new FamilySubstitution(c.Finding, c.Relation, c.Link.Person, c.Link.Other, c.Link.Family, c.Shared[0]))];

    /// <summary>The findings with each substituted family in place: a named placeholder is replaced wherever any finding uses it.</summary>
    public static IReadOnlyList<DraftFinding> Apply(IReadOnlyList<DraftFinding> findings, IReadOnlyList<FamilySubstitution> substitutions)
    {
        var result = findings.ToList();
        foreach (var substitution in substitutions)
        {
            if (substitution.Named is { } placeholder)
                for (int f = 0; f < result.Count; f++) result[f] = Renamed(result[f], placeholder, substitution.Family);
            else
                result[substitution.Finding] = WithFamily(result[substitution.Finding], substitution);
        }
        return result;
    }

    /// <summary>A resolved decision for each substitution, so the reviewer sees the family a relation was compiled onto.</summary>
    public static IEnumerable<DraftDecision> Decisions(IReadOnlyList<DraftFinding> findings, IReadOnlyList<FamilySubstitution> substitutions) =>
        substitutions.Select(s => new DraftDecision(DraftDecisionKinds.Resolved,
            $"{s.Person} and {s.Spouse} are already partners in {s.Family}, so {s.Path} compiles onto it" + (s.Named is { } named ? $" instead of creating {named}" : ""),
            [findings[s.Finding].Id]));

    static IEnumerable<Couple> Couples(IReadOnlyList<DraftFinding> findings, IFamilyRecords? families)
    {
        if (families is null) yield break;
        for (int f = 0; f < findings.Count; f++)
            for (int r = 0; r < findings[f].Relations.Count; r++)
            {
                var link = findings[f].Relations[r];
                if (link.Kind != DraftRelation.Spouse || Placeholder.IsPlaceholder(link.Person) || Placeholder.IsPlaceholder(link.Other)) continue;
                var shared = families.FamiliesOfCouple(link.Person, link.Other);
                if (shared.Count > 0) yield return new Couple(f, r, link, shared);
            }
    }

    static DraftFinding WithFamily(DraftFinding finding, FamilySubstitution substitution)
    {
        var relations = finding.Relations.ToList();
        relations[substitution.Relation] = relations[substitution.Relation] with { Family = substitution.Family };
        return finding with { Relations = relations };
    }

    static DraftFinding Renamed(DraftFinding finding, string from, string to)
    {
        string Swap(string xref) => xref == from ? to : xref;
        string? SwapOptional(string? xref) => xref is null ? null : Swap(xref);
        return finding with
        {
            Facts = [.. finding.Facts.Select(f => f with { Record = Swap(f.Record) })],
            Notes = [.. finding.Notes.Select(n => n with { Record = Swap(n.Record) })],
            Relations = [.. finding.Relations.Select(r => r with { Family = SwapOptional(r.Family), From = SwapOptional(r.From) })],
        };
    }
}
