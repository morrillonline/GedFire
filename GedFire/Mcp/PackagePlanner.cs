namespace GedFire.Mcp;

// ---------------------------------------------------------------------------
// Groups a package's finding files into items, one per anchor (the tree person
// the correlated findings are about). Items are numbered by the first
// appearance of each anchor in the order the files were given, so the numbers
// stay stable however many files fail. A file whose anchor could not be read
// belongs to no item.
// ---------------------------------------------------------------------------

public sealed record PackageItemPlan(int Number, string Anchor, IReadOnlyList<LoadedFindingFile> Files);

public sealed record PackagePlan(IReadOnlyList<PackageItemPlan> Items, IReadOnlyList<LoadedFindingFile> Unassigned);

public static class PackagePlanner
{
    public static PackagePlan Plan(IReadOnlyList<LoadedFindingFile> files)
    {
        var items = files
            .Where(f => f.Anchor is not null)
            .GroupBy(f => f.Anchor!, StringComparer.Ordinal)
            .Select((group, index) => new PackageItemPlan(index + 1, group.Key, [.. group]))
            .ToList();
        return new PackagePlan(items, [.. files.Where(f => f.Anchor is null)]);
    }
}
