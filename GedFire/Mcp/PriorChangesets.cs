using System.Text.Json;
using GedCore;
using GedCore.Apply;
using GedCore.Ged70;

namespace GedFire.Mcp;

// ---------------------------------------------------------------------------
// The "after" argument of validate_changeset and check_plausibility: approved
// changesets applied in memory, in order, before the target changeset is
// dry-run, so it is checked against the tree as it will stand when its turn
// comes. Nothing is written.
// ---------------------------------------------------------------------------

sealed class PriorChangesets
{
    public const string SchemaJson = """
        {
          "type": "array",
          "description": "Changesets to apply in memory, in order, before this one is checked, so an order dependency between approved changesets shows before apply. Each names its changesetPath and its own items selection. Nothing is written. Omit to check against the document as it is.",
          "items": {
            "type": "object",
            "additionalProperties": false,
            "properties": {
              "changesetPath": { "type": "string", "minLength": 1, "pattern": "\\S", "description": "Path to the earlier changeset file." },
              "items": { "type": "string", "minLength": 1, "pattern": "^(all|[0-9]+(,[0-9]+)*)$", "description": "Which of its items to apply: \"all\" or a comma-separated list of item numbers." }
            },
            "required": ["changesetPath", "items"]
          }
        }
        """;

    public static readonly PriorChangesets None = new([]);

    // One earlier changeset as after names it, loaded.
    sealed record Prior(string Path, string Items, ChangesetRequest Request);

    readonly IReadOnlyList<Prior> _changesets;

    PriorChangesets(IReadOnlyList<Prior> changesets) => _changesets = changesets;

    /// <summary>Reads the after argument; absent or null means none. Each changeset must exist and its items must select.</summary>
    public static bool TryRead(JsonElement after, out PriorChangesets prior, out string? error)
    {
        prior = None;
        error = null;
        if (after.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null) return true;
        if (after.ValueKind != JsonValueKind.Array)
        {
            error = "after must be an array of {changesetPath, items}.";
            return false;
        }

        var changesets = new List<Prior>();
        foreach (var (entry, index) in after.EnumerateArray().Select((e, i) => (e, i)))
        {
            if (!TryReadEntry(entry, $"after[{index}]", out var loaded, out error)) return false;
            changesets.Add(loaded!);
        }
        prior = new PriorChangesets(changesets);
        return true;
    }

    static bool TryReadEntry(JsonElement entry, string at, out Prior? prior, out string? error)
    {
        prior = null;
        if (entry.ValueKind != JsonValueKind.Object || Text(entry, "changesetPath") is not { } path || Text(entry, "items") is not { } items
            || entry.EnumerateObject().Any(p => p.Name is not ("changesetPath" or "items")))
        {
            error = $"{at} must be an object with changesetPath and items strings, and nothing else.";
            return false;
        }
        if (!ChangesetRequest.TryRead(path, items, out var request, out string? loadError))
        {
            error = $"{at}: {loadError}";
            return false;
        }
        prior = new Prior(path, items, request!);
        error = null;
        return true;
    }

    static string? Text(JsonElement entry, string name) =>
        entry.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    /// <summary>
    /// Dry-runs <paramref name="target"/> against the document at <paramref name="gedcomPath"/> as it stands after these
    /// changesets. A changeset that does not apply fails the result with its errors, each naming where it is in after.
    /// </summary>
    public ApplyResult DryRun(string gedcomPath, ChangesetRequest target, CancellationToken cancellationToken)
    {
        if (_changesets.Count == 0)
            return ChangesetApplier.Run(gedcomPath, target.Changeset, target.ItemNumbers, dryRun: true, cancellationToken: cancellationToken);

        var (document, log, failed) = Applied(gedcomPath, cancellationToken);
        if (failed is not null) return failed;

        var result = ChangesetApplier.Run(document, target.Changeset, target.ItemNumbers, dryRun: true, cancellationToken: cancellationToken);
        result.Log.InsertRange(0, log);
        return result;
    }

    /// <summary>
    /// The document at <paramref name="gedcomPath"/> as it stands after these changesets, for the policy to read; the
    /// document as it is when there are none, and null when one does not apply (the dry run reports why).
    /// </summary>
    public GedDocument? DocumentAfter(string gedcomPath, CancellationToken cancellationToken)
    {
        if (_changesets.Count == 0) return GedReader.ReadFile(gedcomPath);
        var (document, _, failed) = Applied(gedcomPath, cancellationToken);
        return failed is null ? Ged70Parser.Read(new MemoryStream(document)) : null;
    }

    // Applied once per call, whether the policy or the dry run asks first.
    (byte[] Document, List<string> Log, ApplyResult? Failed)? _applied;

    (byte[] Document, List<string> Log, ApplyResult? Failed) Applied(string gedcomPath, CancellationToken cancellationToken)
    {
        if (_applied is { } applied) return applied;
        var log = new List<string>();
        var (document, failed) = ApplyInOrder(GedReader.ReadAllBytes(gedcomPath), log, cancellationToken);
        return (_applied = (document, log, failed)).Value;
    }

    // The document after each earlier changeset in turn, or the failed result of the first that does not apply.
    (byte[] Document, ApplyResult? Failed) ApplyInOrder(byte[] document, List<string> log, CancellationToken cancellationToken)
    {
        for (int i = 0; i < _changesets.Count; i++)
        {
            var prior = _changesets[i];
            var applied = ChangesetApplier.Run(document, prior.Request.Changeset, prior.Request.ItemNumbers, dryRun: false, cancellationToken: cancellationToken);
            if (!applied.Success)
            {
                var failed = new ApplyResult();
                failed.Log.AddRange(log);
                failed.Errors.AddRange(applied.Errors.Select(e => $"after[{i}] {prior.Path}: {e}"));
                return (document, failed);
            }
            document = applied.OutputBytes ?? document;
            log.Add($"after[{i}] {prior.Path} (items {prior.Items}): applied in memory");
        }
        return (document, null);
    }
}