namespace GedFire.Mcp;

/// <summary>The behavior a tool advertises to clients through its MCP annotations.</summary>
public sealed record ToolBehavior(bool ReadOnly, bool Destructive, bool Idempotent)
{
    /// <summary>Reads the document and returns the same answer for the same input.</summary>
    public static readonly ToolBehavior ReadOnlyIdempotent = new(ReadOnly: true, Destructive: false, Idempotent: true);

    /// <summary>Reads the document, but a repeated call may answer differently.</summary>
    public static readonly ToolBehavior ReadOnlyNonIdempotent = new(ReadOnly: true, Destructive: false, Idempotent: false);

    /// <summary>Reads the document and may create a new file elsewhere, never overwriting one.</summary>
    public static readonly ToolBehavior CreatesFiles = new(ReadOnly: false, Destructive: false, Idempotent: false);

    /// <summary>Modifies the document.</summary>
    public static readonly ToolBehavior Writes = new(ReadOnly: false, Destructive: true, Idempotent: false);
}
