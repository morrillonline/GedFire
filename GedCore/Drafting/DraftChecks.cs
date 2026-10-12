using GedCore.Apply;

namespace GedCore.Drafting;

/// <summary>The refusals a draft makes before compiling: a QUAY the evidence cannot support, a target name that is not the person's, and new people or families that are not declared consistently.</summary>
internal static class DraftChecks
{
    public static IEnumerable<string> Check(IReadOnlyList<DraftFinding> findings, IPersonNames names)
    {
        for (int f = 0; f < findings.Count; f++)
        {
            var finding = findings[f];
            foreach (var error in TargetNameErrors(finding, $"findings[{f}]", names)) yield return error;
            for (int i = 0; i < finding.Facts.Count; i++)
                if (QuayError(finding, finding.Facts[i].Quay, finding.Facts[i].Class, finding.Facts[i].Information, $"findings[{f}].facts[{i}].quay") is { } error)
                    yield return error;
        }

        foreach (var error in NewRecordErrors(findings).Concat(NoteCitationErrors(findings)).Concat(EmptyFindingErrors(findings))) yield return error;
    }

    /// <summary>The highest QUAY the source class and information class can support.</summary>
    public static int Ceiling(string sourceClass, string information) => sourceClass switch
    {
        "original" or "image copy" => information == "primary" ? 3 : 2,
        "derivative" => information == "primary" ? 2 : 1,
        "authored" => 1,
        _ => 0,
    };

    static string? QuayError(DraftFinding finding, int quay, string? factClass, string? factInformation, string path)
    {
        string cls = factClass ?? finding.Source.Class;
        string info = factInformation ?? finding.Source.Information;
        int ceiling = Ceiling(cls, info);
        return quay <= ceiling ? null
            : $"{path} is {quay}, but a {cls} source with {info} information supports at most {ceiling}.";
    }

    static IEnumerable<string> TargetNameErrors(DraftFinding finding, string path, IPersonNames names)
    {
        if (finding.Target.Name is not { } named || names.PersonName(finding.Target.Xref) is not { } actual) yield break;
        if (!string.Equals(Fold(named), Fold(actual), StringComparison.OrdinalIgnoreCase))
            yield return $"{path}.target.name \"{named}\" differs from the name \"{actual}\" recorded for {finding.Target.Xref}.";
    }

    static string Fold(string name) => string.Join(' ', name.Replace("/", " ").Split(' ', StringSplitOptions.RemoveEmptyEntries));

    // A finding may exist only to be cited by another finding's note.
    static IEnumerable<string> EmptyFindingErrors(IReadOnlyList<DraftFinding> findings)
    {
        var cited = findings.SelectMany(f => f.Notes.SelectMany(n => n.Cites.Select(c => c.Finding))).ToHashSet();
        for (int f = 0; f < findings.Count; f++)
        {
            var finding = findings[f];
            if (finding.Facts.Count == 0 && finding.Notes.Count == 0 && finding.Relations.Count == 0 && !cited.Contains(finding.Id))
                yield return $"findings[{f}] has no facts, relations or notes, and no note cites it.";
        }
    }

    static IEnumerable<string> NoteCitationErrors(IReadOnlyList<DraftFinding> findings)
    {
        for (int f = 0; f < findings.Count; f++)
            for (int n = 0; n < findings[f].Notes.Count; n++)
                for (int c = 0; c < findings[f].Notes[n].Cites.Count; c++)
                {
                    var cite = findings[f].Notes[n].Cites[c];
                    string path = $"findings[{f}].notes[{n}].cites[{c}]";
                    var cited = cite.Finding is null ? findings[f] : findings.FirstOrDefault(x => x.Id == cite.Finding);
                    if (cited is null)
                        yield return $"{path}.finding \"{cite.Finding}\" is not a finding of this draft.";
                    else if ((cited.Target.Xref, cited.Target.Card) != (findings[f].Target.Xref, findings[f].Target.Card))
                        yield return $"{path}.finding \"{cite.Finding}\" is aimed at a different target, so it lands in another item; a note cites findings of its own item.";
                    else if (QuayError(cited, cite.Quay, null, null, $"{path}.quay") is { } error)
                        yield return error;
                }
    }

    static IEnumerable<string> NewRecordErrors(IReadOnlyList<DraftFinding> findings)
    {
        var declared = new Dictionary<string, DraftPerson>(StringComparer.Ordinal);
        var referenced = new HashSet<string>(StringComparer.Ordinal);

        for (int f = 0; f < findings.Count; f++)
        {
            for (int p = 0; p < findings[f].People.Count; p++)
            {
                var person = findings[f].People[p];
                string path = $"findings[{f}].people[{p}]";
                if (!Placeholder.IsPlaceholder(person.Xref))
                    yield return $"{path}.xref must be a new-record placeholder such as \"@NewI1@\", got \"{person.Xref}\".";
                else if (declared.TryGetValue(person.Xref, out var first) && first != person)
                    yield return $"{path}.xref {person.Xref} is declared with a different name or sex elsewhere in this draft.";
                declared[person.Xref] = person;
            }
        }

        for (int f = 0; f < findings.Count; f++)
        {
            var finding = findings[f];
            for (int r = 0; r < finding.Relations.Count; r++)
            {
                var relation = finding.Relations[r];
                string path = $"findings[{f}].relations[{r}]";
                foreach (string xref in PeopleOf(relation))
                {
                    referenced.Add(xref);
                    if (Placeholder.IsPlaceholder(xref) && !declared.ContainsKey(xref))
                        yield return $"{path} names {xref}, which is not declared in any finding's people.";
                }
                if (relation.Kind == DraftRelation.Spouse && relation.Family is null && PeopleOf(relation).Any(declared.ContainsKey))
                    yield return $"{path}.family is required when a partner is new: give a new family xref such as \"@NewF1@\".";
                if (QuayError(finding, relation.Quay, null, null, $"{path}.quay") is { } error) yield return error;
            }

            for (int i = 0; i < finding.Facts.Count; i++)
            {
                string record = finding.Facts[i].Record;
                if (Placeholder.IsPlaceholder(record) && !declared.ContainsKey(record) && !AnyFamilyRef(findings, record))
                    yield return $"findings[{f}].facts[{i}].record {record} is neither a declared new person nor a family named by a relation.";
            }
        }

        foreach (string xref in declared.Keys.Where(x => !referenced.Contains(x)))
            yield return $"{xref} is declared in people but no relation links it to the tree.";
    }

    static IEnumerable<string> PeopleOf(DraftRelation relation) =>
        new[] { relation.Person, relation.Other, relation.Husb, relation.Wife }.OfType<string>().Where(x => x.Length > 0);

    static bool AnyFamilyRef(IReadOnlyList<DraftFinding> findings, string family) =>
        findings.SelectMany(f => f.Relations).Any(r => r.Family == family);
}
