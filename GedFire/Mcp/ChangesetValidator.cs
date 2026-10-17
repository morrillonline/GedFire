using System.Text.Json;
using GedCore.Apply;
using GedCore.Policy;

namespace GedFire.Mcp;

// ---------------------------------------------------------------------------
// Checks a finished changeset the way validate_changeset does: it must parse,
// satisfy the policy, and dry-run cleanly against the bound document. Every
// error names the file, so a caller presenting several changesets can tell
// which one to send back to drafting.
// ---------------------------------------------------------------------------

sealed class ChangesetValidator(string documentPath, ChangesetPolicy? policy)
{
    public IReadOnlyList<string> Check(string name, string json, CancellationToken cancellationToken)
    {
        Changeset changeset;
        try
        {
            changeset = Changeset.Parse(json);
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or InvalidOperationException or FormatException)
        {
            return [$"{name} is not a readable changeset: {ex.Message}"];
        }

        var errors = (policy?.Check(json) ?? []).ToList();
        try
        {
            var result = ChangesetApplier.Run(
                documentPath, changeset, [.. changeset.Items.Select(i => i.Number)], dryRun: true, cancellationToken: cancellationToken);
            errors.AddRange(result.Errors);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            errors.Add($"Could not open '{documentPath}': {ex.Message}");
        }
        return [.. errors.Select(e => $"{name}: {e}")];
    }
}
