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
    string? File, string? Id, bool Valid, IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings, bool ReadyToDraft,
    IReadOnlyList<string> DraftGaps, IReadOnlyList<FindingBinding> Bindings, string SchemaVersion);

/// <summary>Validates findings against <paramref name="model"/> when given, or their shape alone when null.</summary>
public sealed class FindingValidator(GedModel? model, ChangesetPolicy? policy, bool requireDraft)
{
    /// <summary>A finding file, read by <see cref="FindingFileReader"/>.</summary>
    public FindingValidation Validate(FindingFile file)
    {
        var errors = file.Errors.ToList();
        if (requireDraft && file.NotReadyReason is { } reason) errors.Insert(0, reason);
        bool ready = errors.Count == 0 && file.NotReadyReason is null;
        if (ready && file.Draft is { } draft)
            errors.AddRange(FindingFileLoader.NameFiles(DraftErrors(DraftNoteJoiner.Join([draft])), [file.Name]));

        return new FindingValidation(
            file.Name, file.Id, errors.Count == 0, errors, file.Warnings, ready && errors.Count == 0, file.Gaps,
            Bindings(file.Draft, "draft."), SchemaCatalog.FindingFile.Version);
    }

    /// <summary>The <paramref name="index"/>-th entry of a findings array, as draft_changeset's findings or inputPath take it.</summary>
    public FindingValidation Validate(JsonElement entry, int index)
    {
        string path = $"findings[{index}]";
        using var single = JsonDocument.Parse("[" + entry.GetRawText() + "]");
        var (read, readErrors) = DraftFindingReader.Read(single.RootElement);
        var errors = readErrors.Select(e => e.Replace("findings[0]", path)).ToList();
        var draft = errors.Count == 0 ? read[0] : null;
        if (draft is not null) errors.AddRange(DraftErrors([draft]).Select(e => e.Replace("findings[0]", path)));

        string? id = entry.ValueKind == JsonValueKind.Object && entry.TryGetProperty("id", out var given)
                     && given.ValueKind == JsonValueKind.String ? given.GetString() : null;
        return new FindingValidation(
            null, id, errors.Count == 0, errors, [], errors.Count == 0, [], Bindings(draft, path + "."), SchemaCatalog.Finding.Version);
    }

    IReadOnlyList<string> DraftErrors(IReadOnlyList<DraftFinding> findings) =>
        model is null ? [] : new ChangesetDrafter(new ModelDraftLookup(model), policy).Draft(findings).Errors;

    IReadOnlyList<FindingBinding> Bindings(DraftFinding? draft, string prefix) =>
        draft is null ? [] : [.. DraftReferences.Of(draft).Select(r => Bind(r, prefix + r.Path))];

    FindingBinding Bind(DraftReference reference, string at)
    {
        var binding = new FindingBinding(reference.Xref, at);
        if (reference.IsPlaceholder) return binding with { Declared = reference.Declared };
        if (model is null) return binding;
        if (model.Individuals.TryGetValue(reference.Xref, out var person))
            return binding with { Exists = true, Name = PersonDisplay.FullName(person), Lifespan = PersonDisplay.Lifespan(person) };
        if (model.Sources.TryGetValue(reference.Xref, out var source))
            return binding with { Exists = true, Name = source.Title };
        return binding with { Exists = model.Families.ContainsKey(reference.Xref) };
    }
}
