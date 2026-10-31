namespace GedFire.Match;

/// <summary>How much of a match_record result to return; the verdict is decided before either option applies.</summary>
public sealed record RecordMatchView(int MaxCandidates = RecordMatchView.DefaultMaxCandidates, bool Compact = false)
{
    public const int DefaultMaxCandidates = 5;
    public const int MaxCandidatesLimit = 20;

    /// <summary>The result capped to <see cref="MaxCandidates"/> per person, as a <see cref="RecordMatchResult"/> or, when compact, a <see cref="CompactRecordMatchResult"/>.</summary>
    public object Present(RecordMatchResult result) =>
        Compact
            ? new CompactRecordMatchResult([.. result.Persons.Select(p => ToCompact(Cap(p)))])
            : new RecordMatchResult([.. result.Persons.Select(Cap)]);

    RecordPersonMatch Cap(RecordPersonMatch person) =>
        person.Candidates.Count <= MaxCandidates
            ? person
            : person with
            {
                Candidates = [.. person.Candidates.Take(MaxCandidates)],
                CandidatesOmitted = person.CandidatesOmitted + person.Candidates.Count - MaxCandidates,
            };

    static CompactRecordPersonMatch ToCompact(RecordPersonMatch person) => new()
    {
        Index = person.Index,
        Role = person.Role,
        Name = person.Name,
        Verdict = person.Verdict,
        ConfidentMatchXref = person.ConfidentMatchXref,
        ConfidentMatchScore = person.ConfidentMatchScore,
        RunnerUpXref = person.RunnerUpXref,
        RunnerUpScore = person.RunnerUpScore,
        Candidates = [.. person.Candidates.Select(c => new CompactRecordCandidate(c.Xref, c.Name, c.Lifespan, c.Score, c.MatchedKin))],
        CandidatesOmitted = person.CandidatesOmitted,
        SameNameSurvey = [.. person.SameNameSurvey.Select(s => new CompactSurveyRow(s.Xref, s.Name, s.Reason))],
        PotentialNewPerson = person.PotentialNewPerson,
        PossibleDuplicates = person.PossibleDuplicates,
    };
}
