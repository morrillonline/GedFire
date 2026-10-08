using GedCore.Matching;
using ModelContextProtocol.Server;

namespace GedFire.Mcp;

// ---------------------------------------------------------------------------
// Builds every tool the server advertises. apply_changeset is always
// registered, even under --read-only: it refuses each call itself rather
// than disappearing, so a client that cached the tool list before a config
// change gets an explicit refusal instead of an unknown-tool error.
// ---------------------------------------------------------------------------

public static class McpToolCollectionFactory
{
    public static McpServerPrimitiveCollection<McpServerTool> Create(
        DocumentSession session, ToolGate gate, NicknameDirectory nicknames,
        string documentPath, string mediaDirectory, bool readOnly) =>
    [
        new ApplyChangesetTool(documentPath, gate, readOnly).ToMcpServerTool(),
        new CheckPlausibilityTool(documentPath, gate).ToMcpServerTool(),
        new DateCalcTool(gate).ToMcpServerTool(),
        new DescribeChangesetOpsTool(gate).ToMcpServerTool(),
        new FindFamilyTool(session, gate, nicknames).ToMcpServerTool(),
        new FindPersonTool(session, gate, nicknames).ToMcpServerTool(),
        new FindSourceTool(session, gate).ToMcpServerTool(),
        new GetDocumentStatsTool(session, gate).ToMcpServerTool(),
        new GetRecordTool(session, gate, mediaDirectory).ToMcpServerTool(),
        new GetRecordsTool(session, gate, mediaDirectory).ToMcpServerTool(),
        new ListPeopleTool(session, gate).ToMcpServerTool(),
        new ListUnanchoredPeopleTool(session, gate).ToMcpServerTool(),
        new SelectTargetsTool(session, gate).ToMcpServerTool(),
        new ValidateChangesetTool(documentPath, gate).ToMcpServerTool(),
        new ValidateDocumentTool(documentPath, gate).ToMcpServerTool(),
    ];
}
