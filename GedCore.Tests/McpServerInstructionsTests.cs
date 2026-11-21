using GedFire.Mcp;

namespace GedCore.Tests;

public class McpServerInstructionsTests
{
    [Fact]
    public void Default_StatesXrefScopeAndTheWritePathOnly()
    {
        string text = McpServerInstructions.Build(readOnly: false);

        Assert.Contains("belongs only to the single GEDCOM document", text);
        Assert.Contains("apply_changeset is the only tool that writes", text);
        Assert.DoesNotContain("--read-only", text);
    }

    [Fact]
    public void ReadOnly_AddsTheRefusalNotice()
    {
        string text = McpServerInstructions.Build(readOnly: true);

        Assert.Contains("--read-only: every call to apply_changeset is refused", text);
    }

    [Fact]
    public void Policy_AddsTheEnforcementNotice()
    {
        string withPolicy = McpServerInstructions.Build(readOnly: false, hasPolicy: true);
        string without = McpServerInstructions.Build(readOnly: false);

        Assert.Contains("policy file", withPolicy);
        Assert.DoesNotContain("policy file", without);
    }

    [Fact]
    public void EveryAdvertisedReadOnlyToolIsNamed()
    {
        string text = McpServerInstructions.Build(readOnly: false);

        foreach (string tool in new[]
        {
            "date_calc", "find_person", "get_document_stats", "get_record",
            "get_records", "match_record", "research_brief", "select_targets",
            "describe_changeset_ops", "describe_schemas", "draft_changeset", "draft_package", "validate_changeset", "validate_document",
            "validate_finding", "check_plausibility",
        })
            Assert.Contains(tool, text);
    }
}
