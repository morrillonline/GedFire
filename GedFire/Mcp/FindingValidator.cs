using System.Text.Json;
using System.Text.Json.Serialization;
using GedCore.Drafting;
using GedCore.Policy;
using GedFire.Gen;
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
    readonly FindingBinder _binder = new(model);

    /// <inheritdoc cref="Validate(FindingFile, IReadOnlyList{DraftFinding})"/>
    public FindingValidation Validate(FindingFile file) => Validate(file, []);

    /// <summary>
    /// A finding file, read by <see cref="FindingFileReader"/>. Its draft is checked even while draftGaps keep it from drafting;
    /// what only a complete finding needs (every new person linked, something to compile) then shows as not ready, not as an error.
    /// </summary>
    /// <param name="others">The findings a note of this one may cite: the rest of the batch, and the files beside a single file.</param>
    public FindingValidation Validate(FindingFile file, IReadOnlyList<DraftFinding> others)
    {
        var errors = file.Errors.ToList();
        if (requireDraft && file.NotReadyReason is { } reason) errors.Insert(0, reason);
        bool compiles = errors.Count == 0 && file.NotReadyReason is null;
        if (file.Draft is { } draft)
        {
            // draft_changeset joins a file's notes before compiling, so the check sees the notes it compiles.
            var cited = new CitedFindings(DraftNoteJoiner.Join([draft])[0], others);
            var ruleErrors = RuleErrors(cited, compiles, complete: file.Gaps.Count == 0);
            errors.AddRange(CitedFindings.Relabel(ruleErrors, $"{file.Name} (findings[0])"));
        }
        var recordBindings = _binder.Record(file.Record);
        errors.AddRange(recordBindings.Where(b => b.Exists == false).Select(b => $"{file.Name}: {b.At} {b.Ref} is not a person in the document."));

        return new FindingValidation(
            file.Name, file.Id, errors.Count == 0, errors, compiles && errors.Count == 0, file.Gaps,
            [.. _binder.Draft(file.Draft, "draft."), .. recordBindings], SchemaCatalog.FindingFile.Version);
    }

    /// <summary>Every entry of a findings array, as draft_changeset's findings or inputPath take it; a note may cite another entry.</summary>
    public IReadOnlyList<FindingValidation> ValidateEntries(JsonElement entries)
    {
        var list = entries.EnumerateArray().ToList();
        var drafts = list.Select(ReadEntry).Select(r => r.Finding).OfType<DraftFinding>().ToList();
        return [.. list.Select((entry, index) => Validate(entry, index, drafts))];
    }

    /// <summary>The <paramref name="index"/>-th entry of a findings array, as draft_changeset's findings or inputPath take it.</summary>
    public FindingValidation Validate(JsonElement entry, int index) => Validate(entry, index, []);

    FindingValidation Validate(JsonElement entry, int index, IReadOnlyList<DraftFinding> others)
    {
        string path = $"findings[{index}]";
        var (draft, readErrors) = ReadEntry(entry);
        var errors = readErrors.Select(e => e.Replace("findings[0]", path)).ToList();
        if (draft is not null)
            errors.AddRange(CitedFindings.Relabel(RuleErrors(new CitedFindings(draft, others), compiles: true, complete: true), path));

        string? id = entry.ValueKind == JsonValueKind.Object && entry.TryGetProperty("id", out var given)
                     && given.ValueKind == JsonValueKind.String ? given.GetString() : null;
        return new FindingValidation(
            null, id, errors.Count == 0, errors, errors.Count == 0, [], _binder.Draft(draft, path + "."), SchemaCatalog.Finding.Version);
    }

    static (DraftFinding? Finding, IReadOnlyList<string> Errors) ReadEntry(JsonElement entry)
    {
        using var single = JsonDocument.Parse("[" + entry.GetRawText() + "]");
        var (read, errors) = DraftFindingReader.Read(single.RootElement);
        return (errors.Count == 0 ? read[0] : null, errors);
    }

    // Without a document, the rules the finding alone decides; with one, the drafter's own refusals, compiling when the finding
    // could be drafted. Only the checked finding is drafted; the findings it cites lend their records.
    IReadOnlyList<string> RuleErrors(CitedFindings finding, bool compiles, bool complete)
    {
        IReadOnlyList<DraftFinding> drafted = [finding.Finding];
        if (model is null) return ChangesetDrafter.CheckWithoutDocument(drafted, complete, finding.Cited);
        var drafter = new ChangesetDrafter(new ModelDraftLookup(model), policy);
        return compiles ? drafter.Draft(drafted, finding.Cited).Errors : drafter.Check(drafted, complete, finding.Cited);
    }
}