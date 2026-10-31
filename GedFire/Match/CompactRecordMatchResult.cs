namespace GedFire.Match;

/// <summary>match_record's compact result: the same decision with short candidate and survey rows.</summary>
public sealed record CompactRecordMatchResult(IReadOnlyList<CompactRecordPersonMatch> Persons);

public sealed record CompactRecordPersonMatch
{
    public required int Index { get; init; }
    public string? Role { get; init; }
    public required string Name { get; init; }
    public required string Verdict { get; init; }
    public string? ConfidentMatchXref { get; init; }
    public double? ConfidentMatchScore { get; init; }
    public string? RunnerUpXref { get; init; }
    public double? RunnerUpScore { get; init; }
    public required IReadOnlyList<CompactRecordCandidate> Candidates { get; init; }
    public required int CandidatesOmitted { get; init; }
    public required IReadOnlyList<CompactSurveyRow> SameNameSurvey { get; init; }
    public required bool PotentialNewPerson { get; init; }
    public required IReadOnlyList<RecordPossibleDuplicate> PossibleDuplicates { get; init; }
}

/// <summary>A candidate without its subscores; <see cref="Lifespan"/> is "birth-death" in years, "?" for an unknown end.</summary>
public sealed record CompactRecordCandidate(string Xref, string Name, string? Lifespan, double Score, IReadOnlyList<RecordKinMatch> MatchedKin);

public sealed record CompactSurveyRow(string Xref, string Name, string? Reason);
