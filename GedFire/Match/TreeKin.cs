using GedFire.Gen;

namespace GedFire.Match;

/// <summary>A tree person's immediate kin as the document records them.</summary>
public static class TreeKin
{
    public static List<GedIndividual> Spouses(GedIndividual indi) =>
        [.. indi.FamSpouse.Select(f => f.SpouseOf(indi)).OfType<GedIndividual>()];

    public static List<GedIndividual> Children(GedIndividual indi) =>
        [.. indi.FamSpouse.SelectMany(f => f.Children)];

    public static List<GedIndividual> Parents(GedIndividual indi)
    {
        var parents = new List<GedIndividual>(2);
        if (indi.FamChild?.Husband is { } husband) parents.Add(husband);
        if (indi.FamChild?.Wife is { } wife) parents.Add(wife);
        return parents;
    }

    public static List<GedIndividual> Siblings(GedIndividual indi) =>
        indi.FamChild is { } fam ? [.. fam.Children.Where(c => !ReferenceEquals(c, indi))] : [];
}
