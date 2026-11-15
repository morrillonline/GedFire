namespace GedCore.Apply;

/// <summary>A person already in the tree: the xref and the GEDCOM name.</summary>
public sealed record Relative(string Xref, string Name);

/// <summary>
/// The family structure a tree already holds, enough to find the relatives a person has. Every member answers for an
/// existing record only; a placeholder or an unknown xref has no relatives.
/// </summary>
public interface IExistingRelatives
{
    Relative? Person(string xref) => null;

    IReadOnlyList<string> FamiliesAsChild(string person) => [];

    IReadOnlyList<string> FamiliesAsPartner(string person) => [];

    /// <summary>The partner a family records under <paramref name="tag"/>, HUSB or WIFE.</summary>
    Relative? Partner(string family, string tag) => null;

    IReadOnlyList<Relative> Children(string family) => [];
}
