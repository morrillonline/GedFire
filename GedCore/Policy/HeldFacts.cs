using GedCore.Apply;
using GedCore.Drafting;

namespace GedCore.Policy;

/// <summary>
/// The instances of a fact the document already holds that a citation of it lands beside: every instance of a single-valued
/// tag, and of a repeatable tag the ones on the same date. The drafter and the policy both grade a fact by these.
/// </summary>
internal static class HeldFacts
{
    public static IEnumerable<ExistingFact> Of(IRecordHistory history, string record, string tag, string? date) =>
        history.FactsOn(record, tag).Where(e => CreateOrUpdateVitalOp.IsSingleValued(tag) || SameDate(e.Date, date));

    /// <summary>The best grade the document already gives the fact; null when none of its citations is graded.</summary>
    public static int? BestQuay(IRecordHistory history, string record, string tag, string? date) =>
        Of(history, record, tag, date).SelectMany(e => e.Citations).Select(c => c.Quay).OfType<int>()
            .Select(q => (int?)q).DefaultIfEmpty(null).Max();

    static bool SameDate(string? held, string? date) =>
        held is null || date is null
            ? held is null && date is null
            : string.Equals(NodeBuilder.PadDay(held), NodeBuilder.PadDay(date), StringComparison.OrdinalIgnoreCase);
}
