using GedCore;
using GedFire.Gen;

namespace GedFire.TargetSelection;

/// <summary>
/// A childless family whose wife's only known children were all born before this couple was together:
/// those children belong to an earlier marriage, so a New child card for this one is unlikely to close.
/// </summary>
static class EarlierMarriageChildren
{
    public const string Reason = "the wife's known children were all born before this marriage";

    public static bool AllPredate(GedFamily family)
    {
        if (family.Wife is not { } wife || CoupleStartYear(family) is not { } start) return false;
        var children = wife.FamSpouse.Where(f => f != family).SelectMany(f => f.Children).Distinct().ToList();
        return children.Count > 0 && children.All(c => BornBefore(c, start));
    }

    // A child without a usable birth date is still bounded by their own first
    // census: anyone enumerated before the couple was together was born earlier.
    static bool BornBefore(GedIndividual child, int coupleStart)
    {
        int born = GedDate.ParseYear(child.Birth?.Date);
        if (born > 0) return born < coupleStart;
        var censusYears = CensusYears(child).ToList();
        return censusYears.Count > 0 && censusYears.Min() < coupleStart;
    }

    // The marriage year, else the first census year that records both partners.
    static int? CoupleStartYear(GedFamily family)
    {
        int married = GedDate.ParseYear(family.Marriage?.Date);
        if (married > 0) return married;
        if (family.Husband is null || family.Wife is null) return null;

        var wifeYears = CensusYears(family.Wife).ToHashSet();
        var together = CensusYears(family.Husband).Where(wifeYears.Contains).ToList();
        return together.Count > 0 ? together.Min() : null;
    }

    static IEnumerable<int> CensusYears(GedIndividual person) =>
        person.Census.Select(c => GedDate.ParseYear(c.Date)).Where(y => y > 0);
}
