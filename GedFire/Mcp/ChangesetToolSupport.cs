using GedCore.Apply;
using ModelContextProtocol.Protocol;

namespace GedFire.Mcp;

// ---------------------------------------------------------------------------
// Shared schema and execution path for validate_changeset and
// apply_changeset: both take the same two arguments and differ only in
// dryRun (and apply_changeset's --read-only gate, applied by its own class
// before this runs). Both call ChangesetApplier.Run directly against the
// server's bound path rather than going through DocumentSession, for two
// reasons, not because DocumentSession's own snapshot could be stale (its
// lazy mtime/length check keeps it current as of any call):
//   1. wrong representation -- DocumentSnapshot.Model is a GedModel, the
//      denormalized model ModelBuilder builds for display/matching.
//      ChangesetApplier needs the raw GedDocument/GedRecord parse tree
//      (from Ged70Parser) to preserve exact structure and formatting for
//      its byte-stable round-trip check; there is no conversion between
//      the two.
//   2. ChangesetApplier holds one exclusive file handle (FileShare.None)
//      from its initial read straight through the verified write, so
//      nothing else can touch the file in between. Fetching a snapshot
//      first and writing separately later would reopen exactly the
//      time-of-check-to-time-of-use gap that locking exists to close.
// A successful apply_changeset write is picked up by DocumentSession's own
// staleness check (and the file watcher) on the next tool call, exactly
// like any other external edit.
// ---------------------------------------------------------------------------

static class ChangesetToolSupport
{
    public const string InputSchemaJson = """
        {
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "changesetPath": {
              "type": "string",
              "minLength": 1,
              "pattern": "\\S",
              "description": "Path to the changeset JSON file -- the machine-applicable side of a research proposal, in the v2 op dialect (call describe_changeset_ops for its full shape). Absolute, or relative to the server process's working directory."
            },
            "items": {
              "type": "string",
              "minLength": 1,
              "pattern": "^(all|[0-9]+(,[0-9]+)*)$",
              "description": "Which changeset items to include: the literal \"all\", or a comma-separated list of item numbers such as \"1,3\". Excluding an item also excludes any newSources[] group only that item cites."
            }
          },
          "required": ["changesetPath", "items"]
        }
        """;

    public const string OutputSchemaJson = """
        {
          "$schema": "https://json-schema.org/draft/2020-12/schema",
          "type": "object",
          "additionalProperties": false,
          "properties": {
            "success": {
              "type": "boolean",
              "description": "Whether validation (and, for apply_changeset, the write) succeeded. false means errors explains why, and the file -- if any -- is untouched."
            },
            "log": {
              "type": "array",
              "items": { "type": "string" },
              "description": "Human-readable trace of what was checked or applied, in order."
            },
            "errors": {
              "type": "array",
              "items": { "type": "string" },
              "description": "Validation or verification failures. Non-empty only when success is false."
            },
            "deltas": {
              "type": "object",
              "additionalProperties": { "type": "integer" },
              "description": "Signed per-record-tag count changes actually written, e.g. {\"INDI\": 1}. Empty for validate_changeset, and for an apply_changeset run whose ops were all no-ops."
            },
            "mintedXrefs": {
              "type": "object",
              "additionalProperties": { "type": "string" },
              "description": "Placeholder token to real minted xref for every new record this run created. Empty for validate_changeset, and for an apply_changeset run whose ops were all no-ops."
            }
          },
          "required": ["success", "log", "errors", "deltas", "mintedXrefs"]
        }
        """;

    public static CallToolResult Execute(
        string absoluteGedcomPath, string changesetPath, string items, bool dryRun,
        CancellationToken cancellationToken = default)
    {
        string trimmedPath = (changesetPath ?? "").Trim();
        if (trimmedPath.Length == 0)
            return CallToolResults.Error("changesetPath must not be blank.");

        string resolvedChangesetPath = Path.GetFullPath(trimmedPath);
        if (!File.Exists(resolvedChangesetPath))
            return CallToolResults.Error($"Changeset file not found: {changesetPath}");

        var changeset = Changeset.LoadFile(resolvedChangesetPath);

        if (!ItemSelector.TryParse(items ?? "", changeset, out int[] itemNumbers, out string? itemsError))
            return CallToolResults.Error(itemsError!);

        ApplyResult result;
        try
        {
            result = ChangesetApplier.Run(absoluteGedcomPath, changeset, itemNumbers, dryRun,
                cancellationToken: cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return CallToolResults.Error($"Could not open '{absoluteGedcomPath}': {ex.Message}");
        }

        return CallToolResults.Success(Map(result), CallToolResults.JsonOptions);
    }

    static ChangesetResult Map(ApplyResult result) =>
        new(result.Success, result.Log, result.Errors, result.Deltas, result.MintedXrefs);
}
