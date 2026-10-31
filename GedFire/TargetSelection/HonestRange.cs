using GedCore;
using GedFire.Gen;

namespace GedFire.TargetSelection;

/// <summary>
/// A cited BET … AND … range no wider than <see cref="MaxSpanMonths"/> is an honest answer, not a date gap.
/// ABT, CAL, EST and open BEF or AFT dates never qualify.
/// </summary>
public sealed record HonestRange(int MaxSpanMonths = 12, int MinQuay = 2)
{
    public static readonly HonestRange Default = new();

    public bool Closes(GedEvent gedEvent) =>
        DateRange.SpansAtMost(gedEvent.Date, MaxSpanMonths)
        && gedEvent.Sources.Any(s => !s.NoCitation && s.Quay >= MinQuay);
}
