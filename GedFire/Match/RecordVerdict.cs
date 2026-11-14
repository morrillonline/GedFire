namespace GedFire.Match;

// ---------------------------------------------------------------------------
// match_record's verdict for one record person, from the ranked candidates:
// find_person's confidence floors pick a lone survivor or a decisive lead,
// and that lead is single only when something besides the name supports it
// and it is not ruled out as a known person's relative.
// ---------------------------------------------------------------------------

public static class RecordVerdict
{
    public const string Single = "single";
    public const string Weak = "weak";
    public const string Candidates = "candidates";
    public const string None = "none";

    /// <param name="ranked">The viable candidates, best first.</param>
    /// <param name="notRelated">Candidates the tree does not hold as a known person's relative of the stated kind.</param>
    public static string Decide(IReadOnlyList<RecordCandidateScore> ranked, IReadOnlyDictionary<string, string> notRelated)
    {
        if (ranked.All(s => s.NameScore < RecordCandidateScorer.NameFloor)) return None;
        if (!HasDecisiveLead(ranked)) return Candidates;
        var top = ranked[0];
        return IsSupportedBeyondName(top) && top.NameScore >= RecordCandidateScorer.NameFloor && !notRelated.ContainsKey(top.Individual.Xref)
            ? Single
            : Weak;
    }

    static bool HasDecisiveLead(IReadOnlyList<RecordCandidateScore> ranked) =>
        ranked.Count == 1 ||
        (ranked[0].Composite >= GedCore.Matching.PersonMatchCore.SingleMatchMinScore &&
         ranked[0].Composite - ranked[1].Composite >= GedCore.Matching.PersonMatchCore.SingleMatchMargin);

    static bool IsSupportedBeyondName(RecordCandidateScore score) =>
        score.LifespanScore > 0 || score.PlaceScore > 0 || score.KinOverlapScore > 0;
}
