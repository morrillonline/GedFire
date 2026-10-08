using System.Text.Json;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace GedFire.Mcp;

// ---------------------------------------------------------------------------
// What a tool advertises: its name, description, schemas, and behavior. The
// SDK binds arguments to and invokes the delegate; the advertised description,
// schemas, and annotations are then overwritten with the hand-written
// constants, so they are the contract rather than a byproduct of reflection
// over the delegate's parameter types.
// ---------------------------------------------------------------------------

public sealed record ToolDefinition(
    string Name, string Description, string InputSchemaJson, string OutputSchemaJson, ToolBehavior Behavior)
{
    public McpServerTool CreateTool(Delegate invoke)
    {
        var tool = McpServerTool.Create(invoke, new McpServerToolCreateOptions
        {
            Name = Name,
            Description = Description,
            ReadOnly = Behavior.ReadOnly,
            Destructive = Behavior.Destructive,
            Idempotent = Behavior.Idempotent,
        });
        tool.ProtocolTool.Description = Description;
        tool.ProtocolTool.InputSchema = JsonDocument.Parse(InputSchemaJson).RootElement.Clone();
        tool.ProtocolTool.OutputSchema = JsonDocument.Parse(OutputSchemaJson).RootElement.Clone();
        tool.ProtocolTool.Annotations = new ToolAnnotations
        {
            ReadOnlyHint = Behavior.ReadOnly,
            DestructiveHint = Behavior.Destructive,
            IdempotentHint = Behavior.Idempotent,
        };
        return tool;
    }
}
