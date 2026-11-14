using GedCore.Apply;
using ModelContextProtocol.Protocol;

namespace GedFire.Mcp;

// ---------------------------------------------------------------------------
// A changeset file and item selection as the changeset tools receive them,
// loaded once: the raw JSON (for policy checks), the parsed changeset, and
// the selected item numbers. Every rejection is the tool's error result.
// ---------------------------------------------------------------------------

sealed record ChangesetRequest(string Json, Changeset Changeset, int[] ItemNumbers)
{
    public static bool TryLoad(string changesetPath, string items, out ChangesetRequest? request, out CallToolResult? error)
    {
        bool loaded = TryRead(changesetPath, items, out request, out string? message);
        error = loaded ? null : CallToolResults.Error(message!);
        return loaded;
    }

    public static bool TryRead(string changesetPath, string items, out ChangesetRequest? request, out string? error)
    {
        request = null;
        string trimmedPath = (changesetPath ?? "").Trim();
        if (trimmedPath.Length == 0)
        {
            error = "changesetPath must not be blank.";
            return false;
        }

        string resolvedPath = Path.GetFullPath(trimmedPath);
        if (!File.Exists(resolvedPath))
        {
            error = $"Changeset file not found: {changesetPath}";
            return false;
        }

        string json = File.ReadAllText(resolvedPath);
        var changeset = Changeset.Parse(json);
        if (!ItemSelector.TryParse(items ?? "", changeset, out int[] itemNumbers, out string? itemsError))
        {
            error = itemsError!;
            return false;
        }

        request = new ChangesetRequest(json, changeset, itemNumbers);
        error = null;
        return true;
    }
}
