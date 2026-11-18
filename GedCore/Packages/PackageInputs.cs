namespace GedCore.Packages;

/// <summary>One changeset of a package: its number in the package and its JSON.</summary>
public sealed record PackageChangeset(int Number, string Json);

/// <summary>Two new people a package creates under agreeing names, declared to be different people.</summary>
public sealed record SameNamePair(string First, string Second)
{
    public bool Covers(string a, string b) => (First == a && Second == b) || (First == b && Second == a);
}

/// <summary>What a package is checked with beyond its changesets: the declared same-named pairs.</summary>
public sealed record PackageOptions(IReadOnlyList<SameNamePair> SameNamePeople);

/// <summary>An existing source's title and web address, which tell two references to one source apart from two sources.</summary>
public sealed record SourceDescription(string? Title, string? Url);

/// <summary>The sources a document already holds.</summary>
public interface ISourceRecords
{
    SourceDescription? Source(string xref);
}
