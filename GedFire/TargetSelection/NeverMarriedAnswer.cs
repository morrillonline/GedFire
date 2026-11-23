using GedFire.Gen;

namespace GedFire.TargetSelection;

/// <summary>
/// A proven never-married answer: an INDI.NMR of 0 that carries a citation. It answers the New spouse card,
/// so the deal withholds that card and the points preview closes it on the changeset that records the answer.
/// </summary>
public static class NeverMarriedAnswer
{
    public const string Reason = "never married: a cited NMR 0 records the answer";

    public static bool IsRecorded(GedIndividual person) => EnrichRule.IsCited(person.NeverMarried);
}
