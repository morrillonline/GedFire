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

    /// <summary>The selected items of a changeset that satisfies the contract; a contract error is the refusal's message.</summary>
    public static bool TryRead(string changesetPath, string items, out ChangesetRequest? request, out string? error)
    {
        request = null;
        if (!ChangesetFile.TryRead(changesetPath, out var file, out error)) return false;
        if (file!.Reading.Changeset is null)
        {
            error = $"{changesetPath} does not satisfy the changeset contract: {string.Join("; ", file.Reading.Errors)}";
            return false;
        }
        return TrySelect(file, items, out request, out error);
    }

    public static bool TrySelect(ChangesetFile file, string items, out ChangesetRequest? request, out string? error)
    {
        request = null;
        var changeset = file.Reading.Changeset!;
        if (!ItemSelector.TryParse(items ?? "", changeset, out int[] itemNumbers, out error)) return false;
        request = new ChangesetRequest(file.Json, changeset, itemNumbers);
        return true;
    }
}

/// <summary>A changeset file as read against the contract; its changeset is null when the contract refuses it.</summary>
sealed record ChangesetFile(string Json, ChangesetReading Reading)
{
    /// <summary>False only when the path is blank or names no file; a file that does not satisfy the contract is still read.</summary>
    public static bool TryRead(string changesetPath, out ChangesetFile? file, out string? error)
    {
        file = null;
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
        file = new ChangesetFile(json, Changeset.Read(json));
        error = null;
        return true;
    }
}
