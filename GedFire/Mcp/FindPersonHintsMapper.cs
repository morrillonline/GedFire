using GedCore.Matching;

namespace GedFire.Mcp;

public static class FindPersonHintsMapper
{
    public static MatchHints ToMatchHints(FindPersonHintsArgs? hints) => hints is null
      ? MatchHints.None
      : new MatchHints(
        ToEventHint(hints.Birth),
        ToEventHint(hints.Death),
        hints.Parents is { } parents ? new ParentsHint(parents.Father, parents.Mother) : null,
        hints.Spouse is { } spouse
          ? new SpouseHint(spouse.Name, ToEventHint(spouse.Marriage))
          : null,
        hints.Sex is { } sex ? sex == "M" : null);

    static EventHint? ToEventHint(FindPersonEventHintArgs? hint) =>
      hint is null ? null : new EventHint(hint.Year, hint.Place);
}
