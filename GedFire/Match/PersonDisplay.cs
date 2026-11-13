using GedCore;
using GedFire.Gen;

namespace GedFire.Match;

// ---------------------------------------------------------------------------
// The one canonical display name for a person: "First Middle LastName",
// matching PersonIndexExporter's existing "name" field convention. Shared by
// MatchIndex (spouse/parent name comparisons) and the MCP result mapping
// (person/spouse/parent name fields) so both draw from one definition of
// "this person's name" (requirement 1) instead of duplicating the join.
// ---------------------------------------------------------------------------

public static class PersonDisplay
{
    public static string FullName(GedIndividual individual)
    {
        ArgumentNullException.ThrowIfNull(individual);
        return (individual.FirstMiddle() + " " + individual.LastName).Trim();
    }

    /// <summary>Birth year to death (or burial) year, with ? for an unknown end; null when neither is known.</summary>
    public static string? Lifespan(GedIndividual individual)
    {
        ArgumentNullException.ThrowIfNull(individual);
        int born = GedDate.ParseYear(individual.Birth?.Date), died = GedDate.ParseYear(individual.DeathOrBurial?.Date);
        if (born == 0 && died == 0) return null;
        return $"{(born == 0 ? "?" : born.ToString())}-{(died == 0 ? "?" : died.ToString())}";
    }
}
