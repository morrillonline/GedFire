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
        return children.Count > 0 && children.All(c => GedDate.ParseYear(c.Birth?.Date) is var born && born > 0 && born < start);
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
