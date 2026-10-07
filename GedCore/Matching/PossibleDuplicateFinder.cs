namespace GedCore.Matching;

/// <summary>One pair of people the duplicate test scores as probably the same person.</summary>
public sealed record PossibleDuplicatePair(string FirstId, string SecondId, double Score);

/// <summary>
/// The duplicate test behind GEN301: each person is matched, with their own
/// birth, parents and spouse as hints, against the others sharing their
/// surname, and the best match at or above <see cref="ScoreFloor"/> forms a pair.
/// </summary>
public static class PossibleDuplicateFinder
{
    public const double ScoreFloor = 70.0;

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
        var pairs = new List<PossibleDuplicatePair>();
        var seen = new HashSet<(string, string)>();
        foreach (var bucket in candidates.GroupBy(c => c.NormalizedSurname, StringComparer.Ordinal))
        {
            var members = bucket.ToList();
            if (members.Count < 2) continue;

            var selves = scope is null ? members : members.Where(c => scope.Contains(c.Id)).ToList();
            foreach (var self in selves)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (BestMatch(self, members) is not { } match) continue;

                var key = string.CompareOrdinal(self.Id, match.Id) < 0 ? (self.Id, match.Id) : (match.Id, self.Id);
                if (seen.Add(key))
                    pairs.Add(new PossibleDuplicatePair(key.Item1, key.Item2, match.FinalScore));
            }
        }
        return pairs;
    }

    // Single requires a 90 score with a 10-point margin, so only None means "no match here";
    // Candidates still carries a real top score.
    static PersonMatchScore? BestMatch(PersonMatchCandidate self, List<PersonMatchCandidate> members)
    {
        var others = members.Where(c => c.Id != self.Id).ToList();
        var outcome = MatchCore.Match(
            others, self.DisplayName, HintsFor(self), Nicknames.Value, maxResults: 1, forDuplicateDetection: true);
        if (outcome.PersonMatchType == PersonMatchType.None) return null;

        var match = outcome.Matches[0];
        return match.FinalScore >= ScoreFloor ? match : null;
    }

    static MatchHints HintsFor(PersonMatchCandidate c)
    {
        EventHint? birth = c.Birth is { } b ? new EventHint(b.Year, b.NormalizedPlace) : null;
        ParentsHint? parents = c.Parents is { } p ? new ParentsHint(p.NormalizedFatherName, p.NormalizedMotherName) : null;
        SpouseHint? spouse = c.Marriages.Count > 0 ? new SpouseHint(c.Marriages[0].NormalizedSpouseName) : null;
        return new MatchHints(Birth: birth, Parents: parents, Spouse: spouse, IsMale: c.IsMale);
    }
}
