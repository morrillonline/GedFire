using GedCore.Apply;

namespace GedCore.Drafting;

/// <summary>
/// The refusals a draft makes before compiling: a QUAY the evidence cannot support, a target name that is not the person's,
/// new people or families that are not declared consistently, and quotations that are not the record's. Without a lookup
/// only the rules the findings alone decide are checked, in the same words.
/// </summary>
internal static class DraftChecks
{
    /// <param name="complete">False for a finding still open (draftGaps): a new person nothing links yet, or a finding with nothing to compile, is then not yet complete rather than wrong.</param>
    public static IEnumerable<string> Check(IReadOnlyList<DraftFinding> findings, IDraftLookup? lookup, bool complete = true)
    {
        for (int f = 0; f < findings.Count; f++)
        {
            var finding = findings[f];
            if (lookup is not null)
                foreach (var error in TargetNameErrors(finding, $"findings[{f}]", lookup)) yield return error;
            for (int i = 0; i < finding.Facts.Count; i++)
                if (QuayError(finding, finding.Facts[i].Quay, finding.Facts[i].Class, finding.Facts[i].Information, $"findings[{f}].facts[{i}].quay") is { } error)
                    yield return error;
        }

        foreach (var error in NewRecordErrors(findings, complete).Concat(RemovalErrors(findings, lookup))
                     .Concat(NoteCitationErrors(findings)).Concat(complete ? EmptyFindingErrors(findings) : [])
                     .Concat(NameErrors(findings, lookup)).Concat(MissingRecordErrors(findings, lookup))
                     .Concat(ExcerptErrors(findings)).Concat(GradeErrors(findings)))
            yield return error;
    }

    // existingPeople and NAME fact records are checked, in person terms, by NameErrors.
    static IEnumerable<string> MissingRecordErrors(IReadOnlyList<DraftFinding> findings, IDraftLookup? lookup)
    {
        for (int f = 0; f < findings.Count; f++)
        {
            var nameRecords = findings[f].Facts.Select((fact, i) => (fact, i)).Where(x => x.fact.Tag == "NAME")
                .Select(x => $"facts[{x.i}].record").ToHashSet(StringComparer.Ordinal);
            foreach (var reference in DraftReferences.Of(findings[f]))
            {
                if (reference.IsPlaceholder || reference.Path.StartsWith("existingPeople[", StringComparison.Ordinal)
                    || nameRecords.Contains(reference.Path)) continue;
                if (lookup?.HasRecord(reference.Xref) == false)
                    yield return $"findings[{f}].{reference.Path} {reference.Xref} is not a record in the document.";
            }
        }
    }

    static IEnumerable<string> NameErrors(IReadOnlyList<DraftFinding> findings, IDraftLookup? lookup)
    {
        var referenced = findings.Select(f => f.Target.Xref)
            .Concat(findings.SelectMany(f => f.ExistingPeople))
            .Concat(findings.SelectMany(f => f.Relations).SelectMany(PeopleOf))
            .Concat(findings.SelectMany(f => f.People).Select(p => p.Xref))
            .ToHashSet(StringComparer.Ordinal);
        for (int f = 0; f < findings.Count; f++)
        {
            for (int p = 0; p < findings[f].ExistingPeople.Count; p++)
            {
                string xref = findings[f].ExistingPeople[p];
                if (Placeholder.IsPlaceholder(xref) || lookup?.HasPerson(xref) == false)
                    yield return $"findings[{f}].existingPeople[{p}] {xref} is not an existing person in the document.";
            }
            for (int i = 0; i < findings[f].Facts.Count; i++)
            {
                var fact = findings[f].Facts[i];
                if (fact.Tag != "NAME") continue;
                string path = $"findings[{f}].facts[{i}]";
                if (!referenced.Contains(fact.Record))
                    yield return $"{path}.record {fact.Record} is not referenced by the input's targets, existingPeople, people or relations.";
                else if (fact.Record != findings[f].Target.Xref && !Placeholder.IsPlaceholder(fact.Record)
                         && lookup?.HasPerson(fact.Record) == false)
                    yield return $"{path}.record {fact.Record} is not an existing person in the document.";
                if (fact.Value is null || fact.CiteOnly || fact.Withdraw is not null) continue;
                if (fact.ReplacedCitations is "keep" or "drop")
                    yield return $"{path}.replacedCitations must be moveToNote for NAME corrections; the previous name's citations must be preserved.";
                string? heldSurname = lookup?.PersonSurname(fact.Record);
                string? newSurname = GedNamePayload.Split(fact.Value).Surname;
                if (heldSurname is not null && !string.Equals(Fold(heldSurname), Fold(newSurname ?? ""), StringComparison.OrdinalIgnoreCase)
                    && !fact.AllowSurnameChange)
                    yield return $"{path}.value changes the surname from \"{heldSurname}\" to \"{newSurname ?? ""}\"; set allowSurnameChange to true only for an intentional surname correction.";
            }
        }
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

    static IEnumerable<string> NewRecordErrors(IReadOnlyList<DraftFinding> findings, bool complete)
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
                foreach (var (xref, role) in PartnersOf(relation))
                    if (declared.TryGetValue(xref, out var partner) && partner.Sex is null)
                        yield return $"{path} places {xref} as {role}, so its people entry needs sex (M or F) to type the role.";
                if (!relation.IsRemoval && QuayError(finding, relation.Quay, null, null, $"{path}.quay") is { } error) yield return error;
            }

            for (int i = 0; i < finding.Facts.Count; i++)
            {
                string record = finding.Facts[i].Record;
                if (Placeholder.IsPlaceholder(record) && !declared.ContainsKey(record) && !AnyFamilyRef(findings, record))
                    yield return $"findings[{f}].facts[{i}].record {record} is neither a declared new person nor a family named by a relation.";
            }
        }

        if (!complete) yield break;
        foreach (string xref in declared.Keys.Where(x => !referenced.Contains(x)))
            yield return $"{xref} is declared in people but no relation links it to the tree.";
    }

    // A link can only be removed from a family that already exists and lists the child; otherwise the removal would silently do nothing.
    static IEnumerable<string> RemovalErrors(IReadOnlyList<DraftFinding> findings, IDraftLookup? lookup)
    {
        for (int f = 0; f < findings.Count; f++)
            for (int r = 0; r < findings[f].Relations.Count; r++)
            {
                var relation = findings[f].Relations[r];
                string path = $"findings[{f}].relations[{r}]";
                if (relation.Kind == DraftRelation.UnlinkChild)
                    foreach (var error in LeavesFamilyErrors(relation, relation.Family!, $"{path}.family", lookup)) yield return error;
                if (relation.Kind == DraftRelation.Child && relation.From is { } from)
                {
                    if (from == relation.Family) yield return $"{path}.from is the family the child joins; a move needs two different families.";
                    else foreach (var error in LeavesFamilyErrors(relation, from, $"{path}.from", lookup)) yield return error;
                }
            }
    }

    static IEnumerable<string> LeavesFamilyErrors(DraftRelation relation, string family, string path, IDraftLookup? lookup)
    {
        if (Placeholder.IsPlaceholder(family))
            yield return $"{path} {family} is a family this draft creates, so there is no existing link to remove.";
        else if (Placeholder.IsPlaceholder(relation.Other))
            yield return $"{path}: {relation.Other} is a new person, so there is no existing link to remove.";
        else if (lookup?.IsChildOf(relation.Other, family) == false)
            yield return $"{path}: {relation.Other} is not a child of {family}, so nothing would be removed.";
    }

    static IEnumerable<string> PeopleOf(DraftRelation relation) =>
        new[] { relation.Person, relation.Other, relation.Husb, relation.Wife }.OfType<string>().Where(x => x.Length > 0);

    // The people a relation gives a partner's or parent's role, which only their sex can type.
    static IEnumerable<(string Xref, string Role)> PartnersOf(DraftRelation relation)
    {
        if (relation.Kind == DraftRelation.Spouse) yield return (relation.Other, "spouse");
        if (relation.Kind == DraftRelation.Parent) yield return (relation.Other, "parent");
        if (relation.Kind == DraftRelation.Child)
        {
            if (relation.Husb is { } husb) yield return (husb, "husb");
            if (relation.Wife is { } wife) yield return (wife, "wife");
        }
    }

    // A citation quotes the record: an excerpt must be the finding's extract, or part of it, word for word.
    static IEnumerable<string> ExcerptErrors(IReadOnlyList<DraftFinding> findings)
    {
        for (int f = 0; f < findings.Count; f++)
        {
            var finding = findings[f];
            string path = $"findings[{f}]";
            for (int i = 0; i < finding.Facts.Count; i++)
                if (NotQuoted(finding.Facts[i].Excerpt, finding)) yield return $"{path}.facts[{i}].excerpt is not part of the finding's extract; an excerpt quotes the extract word for word.";
            for (int r = 0; r < finding.Relations.Count; r++)
                if (NotQuoted(finding.Relations[r].Excerpt, finding)) yield return $"{path}.relations[{r}].excerpt is not part of the finding's extract; an excerpt quotes the extract word for word.";
            for (int n = 0; n < finding.Notes.Count; n++)
                for (int c = 0; c < finding.Notes[n].Cites.Count; c++)
                {
                    var cite = finding.Notes[n].Cites[c];
                    var cited = cite.Finding is null ? finding : findings.FirstOrDefault(x => x.Id == cite.Finding);
                    if (cited is not null && NotQuoted(cite.Excerpt, cited))
                        yield return $"{path}.notes[{n}].cites[{c}].excerpt is not part of the extract of finding \"{cited.Id}\"; an excerpt quotes the extract word for word.";
                }
        }
    }

    static bool NotQuoted(string? excerpt, DraftFinding finding) =>
        excerpt is not null && !finding.Extract.Contains(excerpt, StringComparison.Ordinal);

    // The same quoted text becomes one citation, which carries one QUAY.
    static IEnumerable<string> GradeErrors(IReadOnlyList<DraftFinding> findings)
    {
        var claims = new List<(string Finding, string Text, int Quay, string Path)>();
        for (int f = 0; f < findings.Count; f++)
        {
            var finding = findings[f];
            string path = $"findings[{f}]";
            for (int i = 0; i < finding.Facts.Count; i++)
                if (finding.Facts[i].Withdraw is null)
                    claims.Add((finding.Id, finding.Facts[i].Excerpt ?? finding.Extract, finding.Facts[i].Quay, $"{path}.facts[{i}]"));
            for (int r = 0; r < finding.Relations.Count; r++)
                if (!finding.Relations[r].IsRemoval)
                    claims.Add((finding.Id, finding.Relations[r].Excerpt ?? finding.Extract, finding.Relations[r].Quay, $"{path}.relations[{r}]"));
            for (int n = 0; n < finding.Notes.Count; n++)
                for (int c = 0; c < finding.Notes[n].Cites.Count; c++)
                {
                    var cite = finding.Notes[n].Cites[c];
                    var cited = cite.Finding is null ? finding : findings.FirstOrDefault(x => x.Id == cite.Finding);
                    if (cited is not null) claims.Add((cited.Id, cite.Excerpt ?? cited.Extract, cite.Quay, $"{path}.notes[{n}].cites[{c}]"));
                }
        }

        foreach (var quoted in claims.GroupBy(c => (c.Finding, c.Text)))
        {
            var first = quoted.First();
            foreach (var other in quoted.Where(c => c.Quay != first.Quay))
                yield return $"{other.Path}.quay is {other.Quay}, but {first.Path} cites the same text of finding \"{first.Finding}\" at {first.Quay}; one quoted text carries one grade, so give the differently graded claim its own excerpt.";
        }
    }

    static bool AnyFamilyRef(IReadOnlyList<DraftFinding> findings, string family) =>
        findings.SelectMany(f => f.Relations).Any(r => r.Family == family);
}
