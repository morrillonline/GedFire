namespace GedFire.Match;

// ---------------------------------------------------------------------------
// match_record's result shape, one entry per record person. Pure data,
// serialized as-is; RecordMatcher builds it.
// ---------------------------------------------------------------------------

public sealed record RecordMatchResult(IReadOnlyList<RecordPersonMatch> Persons);

public sealed record RecordPersonMatch
{
    public required int Index { get; init; }
    public string? Role { get; init; }
    public required string Name { get; init; }

    /// <summary>"single", "candidates", or "none" — the same confidence floors as find_person.</summary>
    public required string Verdict { get; init; }

    public string? ConfidentMatchXref { get; init; }
    public double? ConfidentMatchScore { get; init; }

    /// <summary>Viable candidates, best first, each with its composite score and subscores.</summary>
    public required IReadOnlyList<RecordCandidateMatch> Candidates { get; init; }

    /// <summary>Every same-name person considered — nobody is silently dropped. Each non-leader carries its reason.</summary>
    public required IReadOnlyList<RecordSurveyEntry> SameNameSurvey { get; init; }

    /// <summary>True when no tree person remains viable: the record person may need to be created.</summary>
    public required bool PotentialNewPerson { get; init; }

    /// <summary>Near-miss names a potential new person might duplicate; empty otherwise.</summary>
    public required IReadOnlyList<RecordPossibleDuplicate> PossibleDuplicates { get; init; }
}

public sealed record RecordCandidateMatch
{
    public required string Xref { get; init; }
    public required string Name { get; init; }
    public string? Born { get; init; }
    public string? Died { get; init; }
    public required double Score { get; init; }
    public required RecordSubscores Subscores { get; init; }

    /// <summary>Each stated kin found in this candidate's own family records.</summary>
    public required IReadOnlyList<RecordKinMatch> MatchedKin { get; init; }
}

/// <summary>Each 0–100; null when the record supplies no evidence for that dimension.</summary>
public sealed record RecordSubscores
{
    public required double Name { get; init; }
    public double? Lifespan { get; init; }
    public double? KinOverlap { get; init; }
    public double? Place { get; init; }
}

public sealed record RecordKinMatch(string Relation, string RecordName, string TreeXref, string TreeName);

public sealed record RecordSurveyEntry
{
    public required string Xref { get; init; }
    public required string Name { get; init; }
    public string? Born { get; init; }
    public string? Died { get; init; }

    /// <summary>"leading", "candidate", or "excluded".</summary>
    public required string Outcome { get; init; }

    /// <summary>Why this person is not the leading candidate; null only for the leader.</summary>
    public string? Reason { get; init; }
}

public sealed record RecordPossibleDuplicate(string Xref, string Name, string Reason, double Score);
