using System.Text.Json;
using System.Text.Json.Serialization;
using GedCore.Drafting;
using GedCore.Policy;
using GedFire.Gen;
using GedFire.Match;
using GedFire.Schemas;

namespace GedFire.Mcp;

// ---------------------------------------------------------------------------
// Checks one finding the way draft_changeset would take it. The shape checks
// are always made; with a document, a finding that is ready is also compiled
// on its own by the same drafter draft_changeset uses, so validate_finding's
// errors are draft_changeset's errors, word for word, and a finding is ready
// to draft exactly when draft_changeset would compile it.
// ---------------------------------------------------------------------------

/// <summary>One xref a finding names: <see cref="Exists"/> (with the record's name) for a document record, <see cref="Declared"/> for a placeholder.</summary>
public sealed record FindingBinding(string Ref, string At)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? Exists { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? Declared { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Name { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Lifespan { get; init; }
}

public sealed record FindingValidation(
    string? File, string? Id, bool Valid, IReadOnlyList<string> Errors, bool ReadyToDraft,
    IReadOnlyList<string> DraftGaps, IReadOnlyList<FindingBinding> Bindings, string SchemaVersion);

/// <summary>Validates findings against <paramref name="model"/> when given, or without a document when null.</summary>
public sealed class FindingValidator(GedModel? model, ChangesetPolicy? policy, bool requireDraft)
{
    /// <summary>A finding file, read by <see cref="FindingFileReader"/>. Its draft is checked even while draftGaps keep it from drafting.</summary>
    public FindingValidation Validate(FindingFile file)
    {
        var errors = file.Errors.ToList();
        if (requireDraft && file.NotReadyReason is { } reason) errors.Insert(0, reason);
        bool compiles = errors.Count == 0 && file.NotReadyReason is null;
        if (file.Draft is { } draft)
            errors.AddRange(FindingFileLoader.NameFiles(RuleErrors(DraftNoteJoiner.Join([draft]), compiles), [file.Name]));
        var recordBindings = RecordBindings(file.Record);
        errors.AddRange(recordBindings.Where(b => b.Exists == false).Select(b => $"{file.Name}: {b.At} {b.Ref} is not a person in the document."));

        return new FindingValidation(
            file.Name, file.Id, errors.Count == 0, errors, compiles && errors.Count == 0, file.Gaps,
            [.. Bindings(file.Draft, "draft."), .. recordBindings], SchemaCatalog.FindingFile.Version);
    }

    /// <summary>The <paramref name="index"/>-th entry of a findings array, as draft_changeset's findings or inputPath take it.</summary>
    public FindingValidation Validate(JsonElement entry, int index)
    {
        string path = $"findings[{index}]";
        using var single = JsonDocument.Parse("[" + entry.GetRawText() + "]");
        var (read, readErrors) = DraftFindingReader.Read(single.RootElement);
        var errors = readErrors.Select(e => e.Replace("findings[0]", path)).ToList();
        var draft = errors.Count == 0 ? read[0] : null;
        if (draft is not null) errors.AddRange(RuleErrors([draft], compiles: true).Select(e => e.Replace("findings[0]", path)));

        string? id = entry.ValueKind == JsonValueKind.Object && entry.TryGetProperty("id", out var given)
                     && given.ValueKind == JsonValueKind.String ? given.GetString() : null;
        return new FindingValidation(
            null, id, errors.Count == 0, errors, errors.Count == 0, [], Bindings(draft, path + "."), SchemaCatalog.Finding.Version);
    }

    // Without a document, the rules the finding alone decides; with one, the drafter's own refusals, compiling when the finding could be drafted.
    IReadOnlyList<string> RuleErrors(IReadOnlyList<DraftFinding> findings, bool compiles)
    {
        if (model is null) return ChangesetDrafter.CheckWithoutDocument(findings);
        var drafter = new ChangesetDrafter(new ModelDraftLookup(model), policy);
        return compiles ? drafter.Draft(findings).Errors : drafter.Check(findings);
    }

    IReadOnlyList<FindingBinding> Bindings(DraftFinding? draft, string prefix) =>
        draft is null ? [] : [.. DraftReferences.Of(draft).Select(r => Bind(r, prefix + r.Path))];

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

    // A record person can be bound only to a person.
    IReadOnlyList<FindingBinding> RecordBindings(JsonElement? record)
    {
        if (record is not { } slot || !slot.TryGetProperty("persons", out var persons) || persons.ValueKind != JsonValueKind.Array) return [];
        var bindings = new List<FindingBinding>();
        int index = 0;
        foreach (var person in persons.EnumerateArray())
        {
            if (person.TryGetProperty("xref", out var xref) && xref.GetString() is { } given)
            {
                var binding = new FindingBinding(given.Trim(), $"record.persons[{index}].xref");
                bindings.Add(model is null ? binding
                    : model.Individuals.TryGetValue(binding.Ref, out var individual) ? BindPerson(binding, individual)
                    : binding with { Exists = false });
            }
            index++;
        }
        return bindings;
    }

    static FindingBinding BindPerson(FindingBinding binding, GedIndividual person) =>
        binding with { Exists = true, Name = PersonDisplay.FullName(person), Lifespan = PersonDisplay.Lifespan(person) };
}
