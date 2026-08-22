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
}
