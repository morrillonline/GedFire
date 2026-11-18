namespace GedCore.Packages;

/// <summary>
/// The rules that span a package's items, which no single changeset can be checked for on its own: one QUAY per source,
/// page and extract, and one name per new person.
/// </summary>
public static class PackageChecks
{
    public static IReadOnlyList<string> Check(IReadOnlyList<PackageChangeset> changesets, ISourceRecords sources, PackageOptions options)
    {
        var package = PackageContents.Read(changesets, sources);
        return
        [
            .. OneGradePerExtractRule.Check(package),
            .. OneNamePerPersonRule.Check(package, options.SameNamePeople),
        ];
    }
}
