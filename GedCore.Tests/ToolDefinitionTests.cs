using GedFire.Mcp;

namespace GedCore.Tests;

public class ToolDefinitionTests
{
    const string Input = """{"type":"object","properties":{"name":{"type":"string"}},"required":["name"]}""";
    const string Output = """{"type":"object","properties":{"ok":{"type":"boolean"}}}""";

    static Task<string> Echo(string name, CancellationToken cancellationToken = default) => Task.FromResult(name);

    static ToolDefinition Define(ToolBehavior behavior) => new("echo", "Echo the name.", Input, Output, behavior);

    [Fact]
    public void CreateTool_AdvertisesTheNameDescriptionAndSchemasVerbatim()
    {
        var tool = Define(ToolBehavior.ReadOnlyIdempotent).CreateTool(Echo).ProtocolTool;

        Assert.Equal("echo", tool.Name);
        Assert.Equal("Echo the name.", tool.Description);
        Assert.Equal("object", tool.InputSchema.GetProperty("type").GetString());
        Assert.Equal("name", tool.InputSchema.GetProperty("required")[0].GetString());
        Assert.NotNull(tool.OutputSchema);
        Assert.True(tool.OutputSchema!.Value.GetProperty("properties").TryGetProperty("ok", out _));
    }

    [Theory]
    [MemberData(nameof(Behaviors))]
    public void CreateTool_AdvertisesTheBehaviorAsAnnotations(ToolBehavior behavior)
    {
        var annotations = Define(behavior).CreateTool(Echo).ProtocolTool.Annotations!;

        Assert.Equal(behavior.ReadOnly, annotations.ReadOnlyHint);
        Assert.Equal(behavior.Destructive, annotations.DestructiveHint);
        Assert.Equal(behavior.Idempotent, annotations.IdempotentHint);
    }

    public static IEnumerable<object[]> Behaviors() =>
    [
        [ToolBehavior.ReadOnlyIdempotent],
        [ToolBehavior.ReadOnlyNonIdempotent],
        [ToolBehavior.Writes],
    ];

    [Fact]
    public void Behaviors_DescribeWhatTheyAreNamedFor()
    {
        Assert.Equal(new ToolBehavior(true, false, true), ToolBehavior.ReadOnlyIdempotent);
        Assert.Equal(new ToolBehavior(true, false, false), ToolBehavior.ReadOnlyNonIdempotent);
        Assert.Equal(new ToolBehavior(false, true, false), ToolBehavior.Writes);
    }
}
