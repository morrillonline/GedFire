namespace GedFire.Gen;

// ---------------------------------------------------------------------------
// FamilyUrlResolver — assigns the stable page URL for every child-producing
// marriage and resolves which page an individual appears on.  Extracted from
// SiteGenerator so URL policy (which DEPLOY.md's URL-stability pre-flight
// depends on) is separate from HTML rendering.
//
// URL policy mirrors original Gedfire VB (Family.GetURL + MakePageName):
// primary spouse's name + birth/death years, plus the secondary spouse's name
// when the primary has several marriages; collisions get a letter suffix in
// first-come order, so results depend on call order — the generator resolves
// families in GEDCOM record order.
// ---------------------------------------------------------------------------

public sealed class FamilyUrlResolver
{
    private readonly HashSet<string> _urlRegistry = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<GedFamily, string> _urlCache = new();

    /// <summary>
    /// The URL of the page where <paramref name="indi"/> appears: their own
    /// family page if a marriage produced children, else their parents' page,
    /// else a page reachable through a spouse; "" when nothing qualifies.
    /// </summary>
    public string IndividualUrl(GedIndividual indi)
    {
        bool hasSpouses = indi.FamSpouse.Count > 0;
        bool hasParents = indi.FamChild != null;

        if (hasParents && !hasSpouses)
            return FamilyUrl(indi.FamChild!);

        if (hasParents && hasSpouses)
        {
            foreach (var f in indi.FamSpouse)
                if (f.Children.Count > 0) return FamilyUrl(f);
            return FamilyUrl(indi.FamChild!);
        }

        if (!hasParents && hasSpouses)
        {
            foreach (var f in indi.FamSpouse)
                if (f.Children.Count > 0) return FamilyUrl(f);

            if (!indi.IsMale && indi.FamSpouse.Count > 0)
                return FamilyUrl(indi.FamSpouse[0]);

            foreach (var f in indi.FamSpouse)
            {
                var spouse = f.SpouseOf(indi);
                if (spouse?.FamChild != null) return FamilyUrl(spouse.FamChild);
            }
            foreach (var f in indi.FamSpouse)
            {
                var spouse = f.SpouseOf(indi);
                if (spouse == null) continue;
                foreach (var f2 in spouse.FamSpouse)
                    if (f2.Children.Count > 0) return FamilyUrl(f2);
            }
        }

        return "";
    }

    /// <summary>The page URL for a family (cached; registered against collisions).</summary>
    public string FamilyUrl(GedFamily fam)
    {
        if (_urlCache.TryGetValue(fam, out var cached)) return cached;

        var primary   = fam.Husband ?? fam.Wife;
        var secondary = primary == fam.Husband ? fam.Wife : fam.Husband;

        if (primary == null) return StoreUrl(fam, "blank.html");
        if (primary == secondary) secondary = null;

        bool stepChildren = primary.FamSpouse.Any(f => f != fam && f.Children.Count > 0);
        var  otherFam     = primary.FamSpouse.FirstOrDefault(f => f != fam && f.Children.Count > 0);

        if (fam.Children.Count == 0 && !stepChildren && primary.FamChild != null)
            return StoreUrl(fam, FamilyUrl(primary.FamChild));
        else if (primary.FamSpouse.Count < 2)
            secondary = null;
        else if (fam.Children.Count > 0 && !stepChildren)
            secondary = null;
        else if (fam.Children.Count == 0 && stepChildren)
            return StoreUrl(fam, otherFam != null ? FamilyUrl(otherFam) : "");

        string pagename = MakePageName(primary, brief: false);
        if (secondary != null)
            pagename += "-" + MakePageName(secondary, brief: true);

        pagename = StripSpecialChars(pagename);
        if (pagename.Length == 0 || pagename.Contains("Family"))
            pagename = "blank";

        if (_urlRegistry.Contains(pagename))
        {
            char ch = 'A';
            while (_urlRegistry.Contains(pagename + ch)) ch++;
            pagename += ch;
        }

        _urlRegistry.Add(pagename);
        return StoreUrl(fam, pagename + ".html");
    }

    private string StoreUrl(GedFamily fam, string url) { _urlCache[fam] = url; return url; }

    private static string MakePageName(GedIndividual indi, bool brief)
    {
        string name = indi.LastName + indi.FirstMiddle();
        name = name.Replace(" ", "").Replace(".", "").Replace(",", "")
                   .Replace("-", "").Replace("/", "").Replace("(", "")
                   .Replace(")", "").Replace("\"", "").Replace("?", "").Replace("'", "");
        if (brief) return name;

        string by = GedDate.ParseYear(indi.Birth?.Date) is var byr && byr > 0
            ? byr.ToString() : "x";
        string dy = GedDate.ParseYear(indi.Death?.Date) is var dyr && dyr > 0
            ? dyr.ToString() : "x";
        return name + "-" + by + "-" + dy;
    }

    private static string StripSpecialChars(string s) =>
        s.Replace("*", "").Replace("/", "").Replace("\\", "").Replace(" ", "");
}
