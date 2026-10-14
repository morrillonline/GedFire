using GedFire.Mcp;

namespace GedCore.Tests;

public class PackagePlannerTests
{
    static LoadedFindingFile File(string name, string? anchor, params string[] errors) =>
        new(name, anchor, null, errors);

    [Fact]
    public void Items_AreNumberedByTheFirstAppearanceOfEachAnchor()
    {
        var plan = PackagePlanner.Plan([File("a", "@I2@"), File("b", "@I1@"), File("c", "@I2@")]);

        Assert.Equal([(1, "@I2@", 2), (2, "@I1@", 1)], plan.Items.Select(i => (i.Number, i.Anchor, i.Files.Count)));
        Assert.Equal(["a", "c"], plan.Items[0].Files.Select(f => f.Name));
    }

    [Fact]
    public void AFailedFileStillHoldsItsAnchorsNumber()
    {
        var plan = PackagePlanner.Plan([File("bad", "@I1@", "no draft"), File("ok", "@I2@")]);

        Assert.Equal([(1, "@I1@"), (2, "@I2@")], plan.Items.Select(i => (i.Number, i.Anchor)));
    }

    [Fact]
    public void AFileWithNoAnchor_IsUnassignedAndTakesNoNumber()
    {
        var plan = PackagePlanner.Plan([File("lost", null, "not json"), File("ok", "@I1@")]);

        Assert.Equal("lost", Assert.Single(plan.Unassigned).Name);
        Assert.Equal(1, Assert.Single(plan.Items).Number);
    }

    [Fact]
    public void NoFiles_PlanNothing()
    {
        var plan = PackagePlanner.Plan([]);

        Assert.Empty(plan.Items);
        Assert.Empty(plan.Unassigned);
    }
}
