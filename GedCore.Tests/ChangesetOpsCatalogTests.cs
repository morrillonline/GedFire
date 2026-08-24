using System.Text.Json;
using GedCore.Apply;
using GedFire.Mcp;

namespace GedCore.Tests;

/// <summary>
/// Guards ChangesetOpsCatalog (describe_changeset_ops's static reference
/// data) against drifting from the actual GedCore.Apply parser: every op's
/// "example" must parse via the real ChangeOp.ReadOp used by
/// validate_changeset/apply_changeset, not just look plausible. A field-name
/// typo or a field the real op no longer accepts fails here, not silently in
/// an agent's hands.
/// </summary>
public class ChangesetOpsCatalogTests
{
    // One line per case in ChangeOp.ReadOp's switch (GedCore/Apply/ChangeOp.cs)
    // -- keep this list and that switch in sync by hand; ContainsExactlyTheDialectsOpKinds
    // below only catches the two drifting apart, not which one is wrong.
    static readonly string[] ExpectedOpKinds =
    [
        "createOrUpdateVital", "createOrUpdateSpouse", "createOrUpdateChild", "createOrUpdateParent",
        "createOrUpdateSource", "createOrUpdateCitation", "createOrUpdateNote", "createOrUpdateMedia",
        "deleteVital", "deleteSpouse", "deleteChild", "deleteParent",
        "deleteSource", "deleteCitation", "deleteNote", "deleteMedia",
        "mergePerson",
    ];

    [Fact]
    public void ContainsExactlyTheDialectsOpKinds()
    {
        Assert.Equal(
            ExpectedOpKinds.OrderBy(k => k, StringComparer.Ordinal),
            ChangesetOpsCatalog.Ops.Select(o => o.Op).OrderBy(k => k, StringComparer.Ordinal));
    }

    public static IEnumerable<object[]> OpDescriptors() =>
        ChangesetOpsCatalog.Ops.Select(op => new object[] { op.Op });

    [Theory]
    [MemberData(nameof(OpDescriptors))]
    public void EveryOpsExample_ParsesAsThatExactOp(string opKind)
    {
        var descriptor = ChangesetOpsCatalog.Ops.Single(o => o.Op == opKind);
        string exampleJson = descriptor.Example.GetRawText();
        string changesetJson = $$"""{ "items": [ { "item": 1, "ops": [ {{exampleJson}} ] } ] }""";

        var changeset = Changeset.Parse(changesetJson);

        var op = Assert.Single(changeset.Items.Single().Ops);
        Assert.Equal(opKind, op.Kind);
    }

    [Fact]
    public void EveryField_IsDocumentedWithANonEmptyDescription()
    {
        foreach (var op in ChangesetOpsCatalog.Ops)
            foreach (var field in op.Fields)
            {
                Assert.False(string.IsNullOrWhiteSpace(field.Name), $"{op.Op}: field with a blank name");
                Assert.False(string.IsNullOrWhiteSpace(field.Type), $"{op.Op}.{field.Name}: blank type");
                Assert.False(string.IsNullOrWhiteSpace(field.Description), $"{op.Op}.{field.Name}: blank description");
            }
    }

    [Fact]
    public void EnvelopeExample_ParsesAsAWholeChangesetWithAtLeastOneItem()
    {
        var changeset = Changeset.Parse(ChangesetOpsCatalog.Envelope.Example.GetRawText());

        Assert.NotEmpty(changeset.Items);
        Assert.All(changeset.Items, item => Assert.NotEmpty(item.Ops));
    }

    [Fact]
    public async Task DescribeChangesetOpsTool_ReturnsTheCatalogAsStructuredContent()
    {
        var tool = new DescribeChangesetOpsTool(new ToolGate());

        var result = await tool.HandleAsync(CancellationToken.None);

        Assert.False(result.IsError);
        var structured = result.StructuredContent!.Value;
        Assert.Equal(ChangesetOpsCatalog.Ops.Count, structured.GetProperty("ops").GetArrayLength());
        Assert.True(structured.GetProperty("envelope").TryGetProperty("example", out _));

        // Every op's own example survives the round trip through JSON
        // serialization intact enough to still parse as that op.
        foreach (var opEl in structured.GetProperty("ops").EnumerateArray())
        {
            string kind = opEl.GetProperty("op").GetString()!;
            string exampleJson = opEl.GetProperty("example").GetRawText();
            var changeset = Changeset.Parse($$"""{ "items": [ { "item": 1, "ops": [ {{exampleJson}} ] } ] }""");
            Assert.Equal(kind, changeset.Items.Single().Ops.Single().Kind);
        }
    }
}
