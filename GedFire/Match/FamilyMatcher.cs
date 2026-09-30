using GedCore.Matching;
using GedFire.Gen;

namespace GedFire.Match;

// ---------------------------------------------------------------------------
// Finds people through a named relative: "the person whose spouse is Beatrice
// Fenwick", "the children of Joseph Ashworth", "the parents of Levi Ashworth".
// The relative is found with PersonMatcher's name matching; the people sought
// are then read off the relative's families. Optional birth/death hints rank
// the people sought, never the relative.
// ---------------------------------------------------------------------------

/// <summary>How the name the caller knows is related to the person being sought.</summary>
public enum FamilyRelation
{
    /// <summary>The known name is the sought person's spouse.</summary>
    Spouse,
    /// <summary>The known name is the sought person's child.</summary>
    Child,
    /// <summary>The known name is the sought person's parent.</summary>
    Parent,
}

public sealed record FamilyRelative(GedIndividual Person, double MatchScore);

public sealed record FamilyCandidate(GedIndividual Person, GedFamily Family, FamilyRelative Relative, double RankScore);

public sealed record FamilySearchOutcome(IReadOnlyList<FamilyCandidate> Candidates, int TotalMatches, bool Truncated);

public sealed class FamilyMatcher
{
    const int MaxRelatives = 50;

    readonly PersonMatcher _matcher;

    public FamilyMatcher(NicknameDirectory nicknames) => _matcher = new PersonMatcher(nicknames);

    public FamilySearchOutcome Find(
        MatchIndex index, FamilyRelation relation, string name, MatchHints? hints, int maxResults)
    {
        ArgumentNullException.ThrowIfNull(index);
        bool ranksByHints = hints is { Birth: not null } or { Death: not null };
        var entries = ranksByHints
            ? index.Entries.ToDictionary(e => e.Individual.Xref, StringComparer.Ordinal)
            : null;

        var found = new Dictionary<(string Person, string Family), FamilyCandidate>();
        foreach (var relative in _matcher.Match(index, name, null, MaxRelatives).Matches)
        {
            foreach (var (person, family) in Sought(relation, relative.Individual))
            {
                double rank = ranksByHints
                    ? (relative.FinalScore + _matcher.ScoreAgainstHints(entries![person.Xref], hints!)) / 2
                    : relative.FinalScore;
                var candidate = new FamilyCandidate(
                    person, family, new FamilyRelative(relative.Individual, relative.FinalScore), rank);

                var key = (person.Xref, family.Xref);
                if (!found.TryGetValue(key, out var existing) || existing.RankScore < rank)
                    found[key] = candidate;
            }
        }

        var ordered = found.Values
            .OrderByDescending(c => c.RankScore)
            .ThenBy(c => c.Person.Xref, StringComparer.Ordinal)
            .ThenBy(c => c.Family.Xref, StringComparer.Ordinal)
            .ToList();
        return new FamilySearchOutcome([.. ordered.Take(maxResults)], ordered.Count, ordered.Count > maxResults);
    }

    static IEnumerable<(GedIndividual Person, GedFamily Family)> Sought(FamilyRelation relation, GedIndividual relative)
    {
        switch (relation)
        {
            case FamilyRelation.Spouse:
                foreach (var family in relative.FamSpouse)
                    if (family.SpouseOf(relative) is { } spouse) yield return (spouse, family);
                break;
            case FamilyRelation.Child:
                if (relative.FamChild is { } parents)
                {
                    if (parents.Husband is { } father) yield return (father, parents);
                    if (parents.Wife is { } mother) yield return (mother, parents);
                }
                break;
            case FamilyRelation.Parent:
                foreach (var family in relative.FamSpouse)
                    foreach (var child in family.Children) yield return (child, family);
                break;
        }
    }
}
