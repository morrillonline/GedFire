using System.Text.Json;
using GedCore.Drafting;
using GedFire.Gen;
using GedFire.Match;

namespace GedFire.Mcp;

// ---------------------------------------------------------------------------
// The xrefs a finding file names, bound to the document: each record's
// existence, a person's name and lifespan, a source's title, a placeholder's
// declaration, and the family a spouse relation compiles onto. Without a
// document only placeholders are judged.
// ---------------------------------------------------------------------------

public sealed class FindingBinder(GedModel? model)
{
    /// <summary>Every xref the draft names, at <paramref name="prefix"/> plus its path within the draft.</summary>
    public IReadOnlyList<FindingBinding> Draft(DraftFinding? draft, string prefix)
    {
        if (draft is null) return [];
        var bindings = DraftReferences.Of(draft).Select(r => Bind(r, prefix + r.Path)).ToList();
        if (model is not null) BindSharedFamilies(draft, prefix, bindings);
        return bindings;
    }

    /// <summary>Each record person's xref; a record person can be bound only to a person.</summary>
    public IReadOnlyList<FindingBinding> Record(JsonElement? record)
    {
        if (record is not { } slot || !slot.TryGetProperty("persons", out var persons) || persons.ValueKind != JsonValueKind.Array) return [];
        var bindings = new List<FindingBinding>();
        int index = 0;
        foreach (var person in persons.EnumerateArray())
        {
            if (person.TryGetProperty("xref", out var xref) && xref.GetString() is { } given)
                bindings.Add(BindRecordPerson(new FindingBinding(given.Trim(), $"record.persons[{index}].xref")));
            index++;
        }
        return bindings;
    }

    // A spouse relation between partners who already share a family is bound to that family, as draft_changeset compiles it.
    void BindSharedFamilies(DraftFinding draft, string prefix, List<FindingBinding> bindings)
    {
        foreach (var substitution in new ChangesetDrafter(new ModelDraftLookup(model!)).FamilySubstitutions([draft]))
        {
            var family = new FindingBinding(substitution.Family, prefix + substitution.Path) { Exists = true };
            int at = bindings.FindIndex(b => b.At == family.At);
            if (at >= 0) bindings[at] = family;
            else bindings.Add(family);
        }
    }

    FindingBinding Bind(DraftReference reference, string at)
    {
        var binding = new FindingBinding(reference.Xref, at);
        if (reference.IsPlaceholder) return binding with { Declared = reference.Declared };
        if (model is null) return binding;
        if (model.Individuals.TryGetValue(reference.Xref, out var person)) return BindPerson(binding, person);
        if (model.Sources.TryGetValue(reference.Xref, out var source))
            return binding with { Exists = true, Name = source.Title };
        return binding with { Exists = model.Families.ContainsKey(reference.Xref) };
    }

    FindingBinding BindRecordPerson(FindingBinding binding) =>
        model is null ? binding
        : model.Individuals.TryGetValue(binding.Ref, out var individual) ? BindPerson(binding, individual)
        : binding with { Exists = false };

    static FindingBinding BindPerson(FindingBinding binding, GedIndividual person) =>
        binding with { Exists = true, Name = PersonDisplay.FullName(person), Lifespan = PersonDisplay.Lifespan(person) };
}
