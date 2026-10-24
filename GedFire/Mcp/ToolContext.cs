using GedCore.Matching;
using GedCore.Policy;

namespace GedFire.Mcp;

public sealed class ToolContext(DocumentSession session, string documentPath)
{
    public DocumentSession Session { get; } = session ?? throw new ArgumentNullException(nameof(session));
    public string DocumentPath { get; } = string.IsNullOrWhiteSpace(documentPath)
        ? throw new ArgumentException("Document path must not be empty.", nameof(documentPath))
        : documentPath;
    public ToolGate Gate { get; init; } = new();
    public NicknameDirectory Nicknames { get; init; } = NicknameDirectory.LoadEmbedded();
    public string MediaDirectory { get; init; } = Path.GetDirectoryName(Path.GetFullPath(documentPath)) ?? ".";
    public bool ReadOnly { get; init; }
    public ChangesetPolicy? Policy { get; init; }
}