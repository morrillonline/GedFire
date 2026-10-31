using GedCore.Drafting;

namespace GedCore.Apply;

/// <summary>Judges a written date against the one it replaces, by the same specificity rules drafting uses.</summary>
public static class DateImprovements
{
    public static DateImprovement Classify(string? prior, string written)
    {
        if (string.IsNullOrWhiteSpace(prior)) return DateImprovement.First;
        if (Specificity.SameDate(prior, written)) return DateImprovement.None;
        if (Narrows(prior, written)) return DateImprovement.Narrowed;
        if (!Specificity.Consistent(prior, written)) return DateImprovement.Corrected;
        return Refines(prior, written) ? DateImprovement.Refined : DateImprovement.None;
    }

    // The written bound is what the two bounds say together, so it excludes days the held one allowed.
    static bool Narrows(string prior, string written) =>
        Specificity.IsBound(prior) && Specificity.IsBound(written)
        && Specificity.CombineBounds(prior, written, out var combined) == Specificity.BoundOverlap.Combined
        && Specificity.SameDate(combined!, written);

    static bool Refines(string prior, string written) =>
        !Specificity.IsApproximate(written)
        && (Specificity.IsApproximate(prior) || Specificity.DateSupersedes(written, prior));
}
