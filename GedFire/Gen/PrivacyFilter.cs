using GedCore;

namespace GedFire.Gen;

// ---------------------------------------------------------------------------
// Publication privacy filter.
//
// The GED is the private research archive and may contain plausibly-living
// people; the generated site must not show them. A person with any
// death-class fact (DEAT, WILL, PROB) is publishable regardless of dates; a
// person without one, born fewer than 100 years before generation, is
// plausibly living and is reduced to a "Living <Surname>" placeholder with no
// dates, places, facts, or sources. The filter mutates the model so every
// render path — parent cards, child cards, spouse lines, index rows — is
// covered without per-site checks. Marriage events of a family with a living
// spouse are suppressed too: a dated marriage can identify the living party.
// So is any family-level media (a wedding photo is exactly as identifying as
// a marriage date) — individual-level media is already gone via Privatize.
//
// A person with no birth year and no death-class fact is treated as historic,
// not living: the tree's undated people are overwhelmingly colonial-era
// spouses, and flagging them would blank ~1,500 long-dead individuals.
//
// An explicit GEDCOM `RESN` (restriction notice) of CONFIDENTIAL or PRIVACY
// always wins over the heuristic above, even for a person the heuristic
// would otherwise call publishable (e.g. a death fact on record). Absence of
// RESN never un-privatizes someone the living heuristic catches — the two
// triggers are OR'ed. RESN LOCKED is an editing restriction, not a display
// one, and is ignored here (it matters to the Apply layer, not the
// generator).
// ---------------------------------------------------------------------------

public static class PrivacyFilter
{
    public const int    PlausiblyLivingAgeYears = 100;
    public const string LivingGivenName         = "Living";

    // Returns the number of individuals privatized.
    public static int Apply(GedModel model, int currentYear)
    {
        int cutoffYear = currentYear - PlausiblyLivingAgeYears;

        var living = model.Individuals.Values
            .Where(i => IsRestricted(i) || IsPlausiblyLiving(i, cutoffYear))
            .ToHashSet();
        if (living.Count == 0) return 0;

        foreach (var indi in living)
            Privatize(indi);

        foreach (var fam in model.Families.Values)
            if ((fam.Husband != null && living.Contains(fam.Husband)) ||
                (fam.Wife    != null && living.Contains(fam.Wife)))
            {
                fam.Marriage = null;
                fam.Media.Clear();
            }

        // Placeholder given names change index sort order.
        ModelBuilder.SortForIndex(model);
        return living.Count;
    }

    static bool IsPlausiblyLiving(GedIndividual indi, int cutoffYear)
    {
        if (indi.Death != null || indi.Will != null || indi.Probate != null)
            return false;
        return GedDate.ParseYear(indi.Birth?.Date) > cutoffYear;
    }

    static bool IsRestricted(GedIndividual indi)
    {
        if (string.IsNullOrEmpty(indi.Restriction)) return false;
        return indi.Restriction
            .Split(',')
            .Select(s => s.Trim())
            .Any(s => s is "CONFIDENTIAL" or "PRIVACY");
    }

    static void Privatize(GedIndividual indi)
    {
        indi.FirstName  = LivingGivenName;
        indi.MiddleName = "";
        indi.Title      = "";
        indi.Fullname   = (LivingGivenName + " " + indi.LastName).Trim();
        indi.NarrativeNotes.Clear();
        indi.Birth      = null;
        indi.Death      = null;
        indi.Will       = null;
        indi.Probate    = null;
        indi.Census.Clear();
        indi.NameSources.Clear();
        indi.Media.Clear();
    }
}
