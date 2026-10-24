using System.Text.Json;
using GedFire.Mcp;

namespace GedCore.Tests;

public class McpToolCollectionFactoryTests
{
    [Theory]
    [InlineData("date_calc")]
    [InlineData("describe_changeset_ops")]
    public void StandaloneToolsNeedNoDocument(string name)
    {
        Assert.False(McpToolCollectionFactory.RequiresDocument(name));
        Assert.Equal(name, McpToolCollectionFactory.CreateStandalone(name).ProtocolTool.Name);
    }

    [Fact]
    public void DocumentToolsCannotBeConstructedWithoutAContext()
    {
        Assert.True(McpToolCollectionFactory.RequiresDocument("get_record"));
        Assert.Throws<ArgumentException>(() => McpToolCollectionFactory.CreateStandalone("get_record"));
    }

    [Fact]
    public void SingleToolConstructionDoesNotConstructOtherTools()
    {
        var snapshot = new DocumentSnapshot(MatchTestModels.Build("0 @I1@ INDI\n1 NAME Mara /Example/\n"), "7.0", DateTime.UtcNow, 0);
        var session = new DocumentSession("synthetic.ged", snapshot);
        var context = new ToolContext(session, "synthetic.ged") { MediaDirectory = "" };

        var tool = McpToolCollectionFactory.Create("get_document_stats", context);

        Assert.Equal("get_document_stats", tool.ProtocolTool.Name);
        Assert.Throws<ArgumentException>(() => McpToolCollectionFactory.Create("get_record", context));
    }

    [Fact]
    public void UnknownNamesHaveAnExplicitFailure()
    {
        Assert.Null(McpToolCollectionFactory.Describe("unknown"));
        Assert.Contains("Unknown tool", Assert.Throws<ArgumentException>(() => McpToolCollectionFactory.CreateStandalone("unknown")).Message);
    }

    [Fact]
    public void DiscoveryMatchesEveryRegisteredToolsContract()
    {
        var snapshot = new DocumentSnapshot(MatchTestModels.Build("0 @I1@ INDI\n1 NAME Mara /Example/\n"), "7.0", DateTime.UtcNow, 0);
        var context = new ToolContext(new DocumentSession("synthetic.ged", snapshot), "synthetic.ged");
        var tools = McpToolCollectionFactory.Create(context);
        var descriptions = McpToolCollectionFactory.Describe().ToDictionary(t => t.Name);

        Assert.Equal(tools.Count, descriptions.Count);
        foreach (var tool in tools)
        {
            var actual = tool.ProtocolTool;
            var described = descriptions[actual.Name];
            Assert.Equal(actual.Description, described.Description);
            Assert.Equal(actual.InputSchema.ToString(), described.InputSchema.ToString());
            Assert.Equal(actual.OutputSchema.ToString(), described.OutputSchema.ToString());
            Assert.Equal(JsonSerializer.Serialize(actual.Annotations), JsonSerializer.Serialize(described.Annotations));
        }
    }
}