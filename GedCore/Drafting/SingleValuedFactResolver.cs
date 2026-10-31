namespace GedCore.Drafting;

/// <summary>One finding's statement of a single-valued fact, reduced to what reconciling needs.</summary>
internal sealed record FactClaim(string FindingId, string? Date, string? Place, int Quay, int Rank, string? Value = null);

/// <summary>
/// What reconciling a fact's claims decided. When <see cref="Conflict"/> is false, <see cref="DateFrom"/>, <see cref="PlaceFrom"/>
/// and <see cref="ValueFrom"/> index the claims that supply the date, place and name to write (null when no claim gave one), and
/// <see cref="Excluded"/> indexes claims whose approximate date does not fit the chosen one, so their citations are not attached.
/// When it is true, <see cref="Recommended"/> indexes the claim a reviewer would most likely keep.
/// </summary>
internal sealed record FactResolution(bool Conflict, int? DateFrom, int? PlaceFrom, int? ValueFrom, IReadOnlyList<int> Excluded, int Recommended)
{
    /// <summary>A date no single claim gives: the intersection of the claims' hard bounds (each claim then supports it).</summary>
    public string? CombinedDate { get; init; }
}

/// <summary>
/// Reconciles what several findings say about one single-valued fact. Date, place and name are settled separately: each takes
/// the most specific value that every other value of its kind is consistent with, so the date may come from one finding
/// and the place from another. A name is consistent with another when it is the same name with a given name in its formal
/// form. Values that are not consistent with one another are a conflict, left to the reviewer.
/// </summary>
internal static class SingleValuedFactResolver
{
    public static FactResolution Resolve(IReadOnlyList<FactClaim> claims)
    {
        int recommended = Best(Enumerable.Range(0, claims.Count), claims);
        int? dateFrom = Winner(claims, c => c.Date, Specificity.DateSupersedes, out bool dateConflict);
        int? placeFrom = Winner(claims, c => c.Place, Specificity.PlaceSupersedes, out bool placeConflict);
        int? valueFrom = Winner(claims, c => c.Value, Specificity.NameSupersedes, out bool valueConflict);
        string? combined = dateConflict ? CombinedBounds(claims) : null;
        if (combined is not null) dateConflict = false;
        if (dateConflict || placeConflict || valueConflict) return new FactResolution(true, null, null, null, [], recommended);

        return new FactResolution(false, dateFrom, placeFrom, valueFrom, Unfitting(claims, dateFrom), recommended) { CombinedDate = combined };
    }

    // Dates that are all hard bounds narrow one another: AFT 1795 and BEF 1797 together say BET 1795 AND 1797.
    static string? CombinedBounds(IReadOnlyList<FactClaim> claims)
    {
        var dates = claims.Select(c => c.Date).OfType<string>().ToList();
        if (dates.Count < 2 || !dates.All(Specificity.IsBound)) return null;

        string combined = dates[0];
        foreach (string date in dates.Skip(1))
        {
            if (Specificity.CombineBounds(combined, date, out var narrower) != Specificity.BoundOverlap.Combined) return null;
            combined = narrower!;
        }
        return combined;
    }

    // The claim holding the value that supersedes every other value (the best graded when several say the same), or null with a conflict when none does.
    static int? Winner(
        IReadOnlyList<FactClaim> claims, Func<FactClaim, string?> read, Func<string, string, bool> supersedes, out bool conflict)
    {
        conflict = false;
        var given = Enumerable.Range(0, claims.Count).Where(i => read(claims[i]) is not null).ToList();
        if (given.Count == 0) return null;

        var winners = given.Where(i => given.All(j => supersedes(read(claims[i])!, read(claims[j])!))).ToList();
        if (winners.Count == 0)
        {
            conflict = true;
            return null;
        }
        return Best(winners, claims);
    }

    // Highest QUAY, then the better source and information class, then the first given.
    static int Best(IEnumerable<int> indexes, IReadOnlyList<FactClaim> claims) =>
        indexes.OrderByDescending(i => claims[i].Quay).ThenByDescending(i => claims[i].Rank).ThenBy(i => i).First();

    static List<int> Unfitting(IReadOnlyList<FactClaim> claims, int? dateFrom)
    {
        if (dateFrom is not { } from || claims[from].Date is not { } chosen || Specificity.IsApproximate(chosen)) return [];
        return [.. Enumerable.Range(0, claims.Count)
            .Where(i => claims[i].Date is { } date && Specificity.IsApproximate(date) && !Specificity.Fits(date, chosen))];
    }
}
