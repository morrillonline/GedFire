using System.Text.Json;
using Microsoft.Extensions.AI;
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
    public Tool Describe() => new()
    {
        Name = Name,
        Description = Description,
        InputSchema = ReadSchema(InputSchemaJson),
        OutputSchema = ReadSchema(OutputSchemaJson),
        Annotations = new ToolAnnotations
        {
            ReadOnlyHint = Behavior.ReadOnly,
            DestructiveHint = Behavior.Destructive,
            IdempotentHint = Behavior.Idempotent,
        },
    };

    public McpServerTool CreateTool(Delegate invoke)
    {
        var function = AIFunctionFactory.Create(invoke, new AIFunctionFactoryOptions
        {
            MarshalResult = (result, resultType, cancellationToken) => ValueTask.FromResult(result),
        });
        var tool = McpServerTool.Create(function, new McpServerToolCreateOptions
        {
            Name = Name,
            Description = Description,
            ReadOnly = Behavior.ReadOnly,
            Destructive = Behavior.Destructive,
            Idempotent = Behavior.Idempotent,
        });
        var description = Describe();
        tool.ProtocolTool.Description = description.Description;
        tool.ProtocolTool.InputSchema = description.InputSchema;
        tool.ProtocolTool.OutputSchema = description.OutputSchema;
        tool.ProtocolTool.Annotations = description.Annotations;
        return new CallableTool(tool, function);
    }

    static JsonElement ReadSchema(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}
