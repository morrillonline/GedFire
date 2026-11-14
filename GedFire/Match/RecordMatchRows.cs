using GedFire.Gen;

namespace GedFire.Match;

// ---------------------------------------------------------------------------
// The rows match_record returns for one record person: a candidate with its
// subscores, and the closed same-name survey with a reason for everyone who
// is not the match.
// ---------------------------------------------------------------------------

public static class RecordMatchRows
{
    public static RecordCandidateMatch Candidate(RecordCandidateScore s) => new()
    {
        Xref = s.Individual.Xref,
        Name = PersonDisplay.FullName(s.Individual),
        Born = EventDisplay(s.Individual.Birth),
        Died = EventDisplay(s.Individual.DeathOrBurial),
        Lifespan = PersonDisplay.Lifespan(s.Individual),
        Score = s.Composite,
        Subscores = new RecordSubscores
        {
            Name = s.NameScore,
            Lifespan = s.LifespanScore,
            KinOverlap = s.KinOverlapScore,
            Place = s.PlaceScore,
        },
        MatchedKin = s.MatchedKin,
    };

    /// <param name="ranked">The viable candidates, best first.</param>
    /// <param name="reasons">A reason that overrides the computed one, by xref, even for the leader.</param>
    public static IReadOnlyList<RecordSurveyEntry> Survey(
        IReadOnlyList<RecordCandidateScore> ranked, IReadOnlyList<(GedIndividual Individual, string Reason)> excluded, StatedKin kin,
        IReadOnlyDictionary<string, string> reasons)
    {
        var entries = new List<RecordSurveyEntry>(ranked.Count + excluded.Count);
        for (int rank = 0; rank < ranked.Count; rank++)
        {
            var s = ranked[rank];
            entries.Add(Entry(s.Individual, rank == 0 ? "leading" : "candidate",
                reasons.GetValueOrDefault(s.Individual.Xref) ?? (rank == 0 ? null : NonLeaderReason(s, ranked[0], kin))));
        }
        entries.AddRange(excluded.Select(e => Entry(e.Individual, "excluded", e.Reason)));
        return entries;
    }

    static RecordSurveyEntry Entry(GedIndividual individual, string outcome, string? reason) => new()
    {
        Xref = individual.Xref,
        Name = PersonDisplay.FullName(individual),
        Born = EventDisplay(individual.Birth),
        Died = EventDisplay(individual.DeathOrBurial),
        Outcome = outcome,
        Reason = reason,
    };

    // The most concrete disagreement available; failing one, the honest comparative score.
    static string NonLeaderReason(RecordCandidateScore s, RecordCandidateScore top, StatedKin kin)
    {
        if (kin.Spouses.Count > 0 && HasRecordedSpouse(s.Individual) &&
            !s.MatchedKin.Any(k => k.Relation == "spouse"))
            return $"no recorded spouse matches {Names(kin.Spouses)}";
        if (kin.Parents.Count > 0 && HasRecordedParent(s.Individual) &&
            !s.MatchedKin.Any(k => k.Relation == "parent"))
            return $"recorded parents do not match {Names(kin.Parents)}";
        return $"scored {s.Composite} against the leading candidate's {top.Composite}";
    }

    static bool HasRecordedSpouse(GedIndividual indi) =>
        indi.FamSpouse.Any(f => f.SpouseOf(indi) != null);

    static bool HasRecordedParent(GedIndividual indi) =>
        indi.FamChild is { } fam && (fam.Husband != null || fam.Wife != null);

    static string Names(IReadOnlyList<RecordPersonDescription> persons) =>
        string.Join(", ", persons.Select(p => p.Name));

    static string? EventDisplay(GedEvent? ev)
    {
        if (ev is null) return null;
        string date = ev.Date;
        string place = ev.Place;
        if (date.Length == 0 && place.Length == 0) return null;
        if (date.Length == 0) return place;
        if (place.Length == 0) return date;
        return $"{date}, {place}";
    }
}
