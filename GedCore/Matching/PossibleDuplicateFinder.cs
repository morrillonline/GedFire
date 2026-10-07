namespace GedCore.Matching;

/// <summary>One pair of people the duplicate test scores as probably the same person.</summary>
public sealed record PossibleDuplicatePair(string FirstId, string SecondId, double Score);

/// <summary>
/// The duplicate test behind GEN301: each person is matched, with their own
/// birth, parents and spouse as hints, against the others sharing their
/// surname, and the best match at or above the score floor forms a pair.
/// A person whose name has an Unknown part is compared only when a parent or spouse is recorded.
/// </summary>
public static class PossibleDuplicateFinder
{
    // Below 70 a shared name plus one coincident fact is as likely a namesake as the same person.
    public const double ScoreFloor = 70.0;

    // With a part of the name Unknown, the score rests on less name evidence, so it must clear a higher bar.
    public const double WildcardScoreFloor = 85.0;

    static readonly Lazy<NicknameDirectory> Nicknames = new(NicknameDirectory.LoadEmbedded);
    static readonly PersonMatchCore MatchCore = new();

    /// <summary>
    /// Pairs found when each person in <paramref name="scope"/> (everyone when null) is the query
    /// side, still scored against that person's whole same-surname group. A pair found from both
    /// sides is reported once.
    /// </summary>
    public static IReadOnlyList<PossibleDuplicatePair> Find(
        IReadOnlyList<PersonMatchCandidate> candidates, IReadOnlySet<string>? scope = null,
        CancellationToken cancellationToken = default)
    {
        var eligible = candidates.Where(c => !HasUnknownPart(c) || HasRelativeRecorded(c)).ToList();
        var pools = new CandidatePools(eligible);

        var pairs = new List<PossibleDuplicatePair>();
        var seen = new HashSet<(string, string)>();
        foreach (var self in eligible.Where(c => scope is null || scope.Contains(c.Id)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (BestMatch(self, pools.For(self)) is not { } match) continue;

            var key = string.CompareOrdinal(self.Id, match.Id) < 0 ? (self.Id, match.Id) : (match.Id, self.Id);
            if (seen.Add(key))
                pairs.Add(new PossibleDuplicatePair(key.Item1, key.Item2, match.FinalScore));
        }
        return pairs;
    }

    // Single requires a 90 score with a 10-point margin, so only None means "no match here";
    // Candidates still carries a real top score.
    static PersonMatchScore? BestMatch(PersonMatchCandidate self, IReadOnlyList<PersonMatchCandidate> pool)
    {
        var others = pool.Where(c => c.Id != self.Id).ToList();
        if (others.Count == 0) return null;

        var outcome = MatchCore.Match(
            others, self.DisplayName, HintsFor(self), Nicknames.Value, maxResults: 1, forDuplicateDetection: true);
        if (outcome.PersonMatchType == PersonMatchType.None) return null;

        var match = outcome.Matches[0];
        double floor = match.Wildcard ? WildcardScoreFloor : ScoreFloor;
        return match.FinalScore >= floor ? match : null;
    }

    static bool HasUnknownPart(PersonMatchCandidate c) =>
        UnknownName.IsPlaceholder(c.NormalizedSurname) || UnknownName.IsPlaceholder(c.NormalizedGiven);

    static bool HasRelativeRecorded(PersonMatchCandidate c) =>
        c.Parents is not null || c.Marriages.Any(m => m.NormalizedSpouseName is not null);

    static string FirstGivenToken(PersonMatchCandidate c) => c.NormalizedGiven.Split(' ', 2)[0];

    static MatchHints HintsFor(PersonMatchCandidate c)
    {
        EventHint? birth = c.Birth is { } b ? new EventHint(b.Year, b.NormalizedPlace) : null;
        ParentsHint? parents = c.Parents is { } p ? new ParentsHint(p.NormalizedFatherName, p.NormalizedMotherName) : null;
        SpouseHint? spouse = c.Marriages.Count > 0 ? new SpouseHint(c.Marriages[0].NormalizedSpouseName) : null;
        return new MatchHints(Birth: birth, Parents: parents, Spouse: spouse, IsMale: c.IsMale);
    }

    // A person is scored against their surname group. An Unknown surname has no group, so such a
    // person is scored against everyone with the same first given name instead, and is added to
    // the pool of anyone with that given name.
    sealed class CandidatePools
    {
        readonly ILookup<string, PersonMatchCandidate> _bySurname;
        readonly ILookup<string, PersonMatchCandidate> _unknownSurnameByGiven;
        readonly ILookup<string, PersonMatchCandidate> _relativeRecordedByGiven;

        public CandidatePools(IReadOnlyList<PersonMatchCandidate> eligible)
        {
            _bySurname = eligible
                .Where(c => !UnknownName.IsPlaceholder(c.NormalizedSurname))
                .ToLookup(c => c.NormalizedSurname, StringComparer.Ordinal);
            _unknownSurnameByGiven = eligible
                .Where(c => UnknownName.IsPlaceholder(c.NormalizedSurname) && !UnknownName.IsPlaceholder(c.NormalizedGiven))
                .ToLookup(FirstGivenToken, StringComparer.Ordinal);
            _relativeRecordedByGiven = eligible
                .Where(c => HasRelativeRecorded(c) && !UnknownName.IsPlaceholder(c.NormalizedGiven))
                .ToLookup(FirstGivenToken, StringComparer.Ordinal);
        }

        public IReadOnlyList<PersonMatchCandidate> For(PersonMatchCandidate self)
        {
            if (UnknownName.IsPlaceholder(self.NormalizedSurname))
                return UnknownName.IsPlaceholder(self.NormalizedGiven) ? [] : [.. _relativeRecordedByGiven[FirstGivenToken(self)]];

            var sameSurname = _bySurname[self.NormalizedSurname];
            return UnknownName.IsPlaceholder(self.NormalizedGiven)
                ? [.. sameSurname]
                : [.. sameSurname, .. _unknownSurnameByGiven[FirstGivenToken(self)]];
        }
    }
}
