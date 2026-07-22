using GedCore.Apply;

namespace GedCore.Tests;

public class ChangesetFixtureTests : ApplyTestBase
{
    private static readonly IReadOnlyDictionary<string, string> Fixtures =
        new Dictionary<string, string>
        {
            ["create-or-update-vital"] = "createOrUpdateVital",
            ["create-or-update-spouse"] = "createOrUpdateSpouse",
            ["create-or-update-child"] = "createOrUpdateChild",
            ["create-or-update-parent"] = "createOrUpdateParent",
            ["create-or-update-source"] = "createOrUpdateSource",
            ["create-or-update-citation"] = "createOrUpdateCitation",
            ["create-or-update-note"] = "createOrUpdateNote",
            ["create-or-update-media"] = "createOrUpdateMedia",
            ["delete-vital"] = "deleteVital",
            ["delete-spouse"] = "deleteSpouse",
            ["delete-child"] = "deleteChild",
            ["delete-parent"] = "deleteParent",
            ["delete-source"] = "deleteSource",
            ["delete-citation"] = "deleteCitation",
            ["delete-note"] = "deleteNote",
            ["delete-media"] = "deleteMedia",
            ["merge-person"] = "mergePerson",
        };

    [Fact]
    public void Catalogue_ContainsOneExampleForEachSupportedOperation()
    {
        var foundKinds = Fixtures.Select(pair =>
        {
            var changeset = ChangesetFixtures.Load(pair.Key);
            var op = changeset.SourceOps.Concat(changeset.Items.SelectMany(item => item.Ops)).Single();
            Assert.Equal(pair.Value, op.Kind);
            return op.Kind;
        }).ToHashSet(StringComparer.Ordinal);

        Assert.Equal(17, foundKinds.Count);
    }

    [Theory]
    [MemberData(nameof(ExampleNames))]
    public void Example_DryRunValidatesAgainstSyntheticGed(string name)
    {
        WriteBaseFile();
        if (name == "merge-person")
            RunExpectSuccess(ChangesetFixtures.Read("seed-duplicate-pair"));
        if (name == "delete-media")
            RunExpectSuccess(ChangesetFixtures.Load("create-or-update-media"));

        var result = Run(ChangesetFixtures.Load(name), dryRun: true);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Contains(result.Log, entry => entry.StartsWith("validation OK"));
    }

    public static IEnumerable<object[]> ExampleNames() =>
        Fixtures.Keys.Select(name => new object[] { name });
}