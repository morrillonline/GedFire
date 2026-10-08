using GedFire.Mcp;

namespace GedCore.Tests;

public class McpServerInstructionsTests
{
    [Fact]
    public void Default_StatesXrefScopeAndTheWritePathOnly()
    {
        string text = McpServerInstructions.Build(readOnly: false, enforcePrivacy: false);

        Assert.Contains("belongs only to the single GEDCOM document", text);
        Assert.Contains("apply_changeset is the only tool that writes", text);
        Assert.DoesNotContain("--read-only", text);
        Assert.DoesNotContain("--enforce-privacy", text);
    }

    [Fact]
    public void ReadOnly_AddsTheRefusalNotice()
    {
        string text = McpServerInstructions.Build(readOnly: true, enforcePrivacy: false);

        Assert.Contains("--read-only: every call to apply_changeset is refused", text);
        Assert.DoesNotContain("--enforce-privacy", text);
    }

    [Fact]
    public void EnforcePrivacy_AddsThePlaceholderNotice()
    {
        string text = McpServerInstructions.Build(readOnly: false, enforcePrivacy: true);

        Assert.Contains("--enforce-privacy", text);
        Assert.Contains("withheld, not absent", text);
    }

    [Fact]
    public void EveryAdvertisedReadOnlyToolIsNamed()
    {
        string text = McpServerInstructions.Build(readOnly: false, enforcePrivacy: false);

        foreach (string tool in new[]
        {
            "date_calc", "find_family", "find_person", "find_source", "get_document_stats", "get_record",
            "get_records", "list_people", "list_unanchored_people", "select_targets",
            "describe_changeset_ops", "validate_changeset", "validate_document", "check_plausibility",
        })
            Assert.Contains(tool, text);
    }
}
