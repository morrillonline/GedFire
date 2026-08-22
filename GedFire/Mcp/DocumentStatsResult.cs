namespace GedFire.Mcp;

// ---------------------------------------------------------------------------
// get_document_stats's one flat result shape, mirroring
// GetDocumentStatsTool.OutputSchemaJson property-for-property
// (docs/design/mcp-server.md "The second tool: get_document_stats").
// Serialized with FindPersonTool's shared camelCase, nulls-emitted options.
// ---------------------------------------------------------------------------

public sealed record DocumentStatsResult(int PersonCount, int FamilyCount, string? GedVersion);
