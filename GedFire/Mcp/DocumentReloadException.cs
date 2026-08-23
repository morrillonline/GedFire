namespace GedFire.Mcp;

/// <summary>
/// A resident document could not be kept in sync with its source file: the
/// file is missing, kept changing while being read, or failed to parse.
/// FindPersonTool's last-chance handler turns this into an isError tool
/// result.
/// </summary>
public sealed class DocumentReloadException : Exception
{
    public DocumentReloadException(string message) : base(message) { }
    public DocumentReloadException(string message, Exception innerException) : base(message, innerException) { }
}
