namespace GedCore.Apply;

/// <summary>
/// Parses the "all|1,3" --items syntax shared by the CLI's `apply` command
/// and the MCP server's validate_changeset/apply_changeset tools into
/// concrete item numbers. One parser so both surfaces reject the same
/// malformed input the same way.
/// </summary>
public static class ItemSelector
{
    public static bool TryParse(string items, Changeset changeset, out int[] itemNumbers, out string? error)
    {
        if (items == "all")
        {
            itemNumbers = [.. changeset.Items.Select(i => i.Number)];
            error = null;
            return true;
        }

        try
        {
            itemNumbers = [.. items.Split(',').Select(int.Parse)];
            error = null;
            return true;
        }
        catch (FormatException)
        {
            itemNumbers = [];
            error = $"items must be 'all' or comma-separated numbers, got: {items}";
            return false;
        }
    }
}
