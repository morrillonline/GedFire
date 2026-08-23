using System.Text;
using GedCore.Matching;
using GedFire.Match;

namespace GedCore.Tests;

public class PersonMatcherTests
{
    // -------------------------------------------------------------------
    // Query splitting
    // -------------------------------------------------------------------

    [Theory]
    [InlineData("Mary Jane Smith", "SMITH", "MARY JANE", false)]
    [InlineData("Frederick Morrill", "MORRILL", "FREDERICK", false)]
    [InlineData("Fred", "FRED", "FRED", true)]
    [InlineData("", "", "", true)]
    [InlineData("   ", "", "", true)]
    // A hyphenated surname stays one token: it is never mistaken for a
    // separate given-name token.
    [InlineData("Smith-Jones", "SMITH-JONES", "SMITH-JONES", true)]
    [InlineData("Mary Smith-Jones", "SMITH-JONES", "MARY", false)]
    public void SplitQuery_MatchesSpec(string query, string surname, string given, bool oneToken)
    {
        var (actualSurname, actualGiven, actualOneToken) = PersonMatcher.SplitQuery(query);
        Assert.Equal(surname, actualSurname);
        Assert.Equal(given, actualGiven);
        Assert.Equal(oneToken, actualOneToken);
    }

    // -------------------------------------------------------------------
    // Fixtures
    // -------------------------------------------------------------------

    // Two Frederick Morrills distinguishable only by parents/spouse, so an
    // exact-name query is a genuine tie that only a hint can break.
    const string MorrillGed = """
        0 @I1@ INDI
        1 NAME Frederick /Morrill/
        1 SEX M
        1 BIRT
        2 DATE 12 MAR 1841
        2 PLAC Gorham, Maine
        1 DEAT
        2 DATE 3 JUN 1919
        2 PLAC Portland, Maine
        1 FAMC @F1@
        1 FAMS @F2@
        0 @I2@ INDI
        1 NAME Wyman /Morrill/
        1 SEX M
        1 FAMS @F1@
        0 @I3@ INDI
        1 NAME Mary /Peaslee/
        1 SEX F
        1 FAMS @F1@
        0 @I4@ INDI
        1 NAME Sarah /Blake/
        1 SEX F
        1 FAMS @F2@
        0 @I5@ INDI
        1 NAME Frederick /Morrill/
        1 SEX M
        1 BIRT
        2 DATE ABT 1868
        2 PLAC Lewiston, Maine
        1 FAMC @F3@
        0 @I6@ INDI
        1 NAME Xavier /Duquette/
        1 SEX M
        1 FAMS @F3@
        0 @I7@ INDI
        1 NAME Odile /Fontaine/
        1 SEX F
        1 FAMS @F3@
        0 @F1@ FAM
        1 HUSB @I2@
        1 WIFE @I3@
        1 CHIL @I1@
        0 @F2@ FAM
        1 HUSB @I1@
        1 WIFE @I4@
        1 MARR
        2 DATE 9 SEP 1863
        0 @F3@ FAM
        1 HUSB @I6@
        1 WIFE @I7@
        1 CHIL @I5@
        """;

    // I5 gets a spouse of its own, distinct in every letter from I1's
    // "Sarah Blake", so a spouse hint can distinguish the two Fredericks.
    const string MorrillWithRivalSpouseGed = """
        0 @F4@ FAM
        1 HUSB @I5@
        1 WIFE @I8@
        0 @I8@ INDI
        1 NAME Zenobia /Winterbourne/
        1 SEX F
        1 FAMS @F4@
        """;

    static readonly string MorrillGedWithRivalSpouse =
        MorrillGed.Replace("0 @I6@ INDI", "1 FAMS @F4@\n0 @I6@ INDI") + "\n" + MorrillWithRivalSpouseGed;

    const string SyntheticNicknamesJson = """
        {
          "male": { "william": ["william", "bill", "billy"], "frederick": ["frederick", "fred"] },
          "female": {}
        }
        """;

    const string EmptyNicknamesJson = """{ "male": {}, "female": {} }""";

    static NicknameDirectory Nicknames(string json) =>
        new(new MemoryStream(Encoding.UTF8.GetBytes(json)));

    static PersonMatcher Matcher(string nicknamesJson = SyntheticNicknamesJson) =>
        new(Nicknames(nicknamesJson));

    static MatchIndex IndexOf(string gedText) => new(MatchTestModels.Build(gedText));

    static string XrefOf(ScoredMatch m) => m.Individual.Xref;

    // -------------------------------------------------------------------
    // Recall gate and the three output shapes
    // -------------------------------------------------------------------

    [Fact]
    public void ExactUniqueMatch_ReturnsSingle()
    {
        var outcome = Matcher().Match(IndexOf(MorrillGed), "Sarah Blake");
        Assert.Equal(PersonMatchType.Single, outcome.PersonMatchType);
        Assert.Equal("@I4@", outcome.Matches[0].Individual.Xref);
    }

    [Fact]
    public void ExactTie_WithNoDisambiguatingHint_ReturnsCandidateList()
    {
        var outcome = Matcher().Match(IndexOf(MorrillGed), "Frederick Morrill");

        Assert.Equal(PersonMatchType.Candidates, outcome.PersonMatchType);
        Assert.Equal(2, outcome.Matches.Count);
        Assert.False(outcome.Truncated);
        // Both candidates tie on every score; the xref ordinal tiebreak
        // resolves the order deterministically.
        Assert.Equal(["@I1@", "@I5@"], outcome.Matches.Select(XrefOf));
        Assert.All(outcome.Matches, m => Assert.Equal(100.0, m.FinalScore, 6));
    }

    [Fact]
    public void WhollyUnrelatedQuery_ReturnsNoMatchWithNoSuggestions()
    {
        var outcome = Matcher().Match(IndexOf(MorrillGed), "Zzqxvw Bbdfgh");
        Assert.Equal(PersonMatchType.None, outcome.PersonMatchType);
        Assert.Empty(outcome.Suggestions);
    }

    // -------------------------------------------------------------------
    // Suggestion band (55-69) and reason classification
    // -------------------------------------------------------------------

    const string SmithGed = """
        0 @I1@ INDI
        1 NAME John /Smith/
        1 SEX M
        """;

    [Fact]
    public void SurnameOnlyMatch_LandsInSuggestionBand_WithCloseSpellingReason()
    {
        // Exact surname (35 pts) + zero-overlap given name (0 pts) against a
        // 60-point two-token weight: nameOnly = 35*100/60 = 58.33, inside
        // [55,69]. The decisive field (surname) matched at 1.0 >= 0.85.
        var outcome = Matcher().Match(IndexOf(SmithGed), "Xvqz Smith");

        Assert.Equal(PersonMatchType.None, outcome.PersonMatchType);
        var suggestion = Assert.Single(outcome.Suggestions);
        Assert.Equal("@I1@", suggestion.Individual.Xref);
        Assert.Equal(SuggestionReason.CloseSpelling, suggestion.Reason);
    }

    [Fact]
    public void SuggestionList_CapsAtThree_OrderedByNameOnlyThenName()
    {
        const string ged = """
            0 @I1@ INDI
            1 NAME Donna /Smith/
            1 SEX F
            0 @I2@ INDI
            1 NAME Harold /Smith/
            1 SEX M
            0 @I3@ INDI
            1 NAME John /Smith/
            1 SEX M
            0 @I4@ INDI
            1 NAME Mary /Smith/
            1 SEX F
            0 @I5@ INDI
            1 NAME Peter /Smith/
            1 SEX M
            """;

        // "Xvqz" shares no characters with any of the five given names above,
        // so every candidate ties at nameOnly = 58.33 and the cap-and-order
        // rule (name-only desc, then display name) governs which three win.
        var outcome = Matcher().Match(IndexOf(ged), "Xvqz Smith");

        Assert.Equal(PersonMatchType.None, outcome.PersonMatchType);
        Assert.Equal(3, outcome.Suggestions.Count);
        Assert.Equal(
            ["Donna Smith", "Harold Smith", "John Smith"],
            outcome.Suggestions.Select(s => GedFire.Match.PersonDisplay.FullName(s.Individual)));
    }

    // -------------------------------------------------------------------
    // One-token queries: broad recall, per-candidate best-field selection
    // -------------------------------------------------------------------

    [Fact]
    public void OneTokenQuery_RecallsEverySurnameMatch_TiesBrokenByNameThenXref()
    {
        // "Morrill" matches three people by surname alone (Wyman, and both
        // Fredericks); with no hints every one ties at final score 100, so
        // display name orders the two Fredericks before Wyman, and the xref
        // tiebreak orders the two identically-named Fredericks between
        // themselves.
        var outcome = Matcher().Match(IndexOf(MorrillGed), "Morrill");

        Assert.Equal(PersonMatchType.Candidates, outcome.PersonMatchType);
        Assert.Equal(["@I1@", "@I5@", "@I2@"], outcome.Matches.Select(XrefOf));
    }

    [Fact]
    public void OneTokenQuery_OnUniqueGiven_MatchesViaGivenField()
    {
        // "Zenobia" only matches by given name (no one is surnamed Zenobia);
        // that candidate must still be found and scored on the given field.
        var outcome = Matcher().Match(IndexOf(MorrillGedWithRivalSpouse), "Zenobia");
        Assert.Equal(PersonMatchType.Single, outcome.PersonMatchType);
        Assert.Equal("@I8@", outcome.Matches[0].Individual.Xref);
    }

    // -------------------------------------------------------------------
    // Relational hints: tie at 100 when a candidate simply lacks the datum,
    // versus a clear 100-vs-75 split when a candidate's datum disagrees.
    // -------------------------------------------------------------------

    [Fact]
    public void SpouseHint_CandidateWithNoSpouseRecorded_StillTiesAtHundred()
    {
        // I5 has no spouse in the base fixture at all: the spouse hint's
        // weight is simply not available for I5, so both candidates can
        // still tie at 100 rather than one being penalized for silence.
        var outcome = Matcher().Match(IndexOf(MorrillGed), "Frederick Morrill",
            new MatchHints(Spouse: new SpouseHint(Name: "Sarah Blake")));

        Assert.Equal(PersonMatchType.Candidates, outcome.PersonMatchType);
        Assert.Equal(2, outcome.Matches.Count);
        Assert.All(outcome.Matches, m => Assert.Equal(100.0, m.FinalScore, 6));

        // Final scores tie (100 vs 100), but I1's raw score (80: 60 name +
        // 20 matched spouse) exceeds I5's (60: name only, no spouse datum
        // to score) — the raw-score tiebreak orders I1 first.
        Assert.Equal(80.0, outcome.Matches.Single(m => m.Individual.Xref == "@I1@").RawScore, 6);
        Assert.Equal(60.0, outcome.Matches.Single(m => m.Individual.Xref == "@I5@").RawScore, 6);
        Assert.Equal(["@I1@", "@I5@"], outcome.Matches.Select(XrefOf));
    }

    [Fact]
    public void SpouseHint_MismatchedRivalSpouse_DropsToSeventyFiveAndResolvesSingle()
    {
        // I5 now has a spouse of its own with no letters in common with
        // "Sarah Blake": the datum is present but disagrees, dropping I5 to
        // exactly 75 (60 name pts / 80 available) while I1 reaches 100 (80/80).
        var outcome = Matcher().Match(IndexOf(MorrillGedWithRivalSpouse), "Frederick Morrill",
            new MatchHints(Spouse: new SpouseHint(Name: "Sarah Blake")));

        Assert.Equal(PersonMatchType.Single, outcome.PersonMatchType);
        var winner = outcome.Matches[0];
        Assert.Equal("@I1@", winner.Individual.Xref);
        Assert.Equal(100.0, winner.FinalScore, 6);
    }

    [Fact]
    public void ParentHint_DisambiguatesExactNameTie()
    {
        var outcome = Matcher().Match(IndexOf(MorrillGed), "Frederick Morrill",
            new MatchHints(Parents: new ParentsHint(Father: "Wyman Morrill")));

        Assert.Equal(PersonMatchType.Single, outcome.PersonMatchType);
        Assert.Equal("@I1@", outcome.Matches[0].Individual.Xref);
    }

    [Fact]
    public void PlaceHint_MatchesWhenHintIsSubstringOfPlace()
    {
        var outcome = Matcher().Match(IndexOf(MorrillGed), "Frederick Morrill",
            new MatchHints(Birth: new EventHint(Place: "Gorham")));

        Assert.Equal(PersonMatchType.Single, outcome.PersonMatchType);
        Assert.Equal("@I1@", outcome.Matches[0].Individual.Xref);
    }

    [Fact]
    public void PlaceHint_MatchesWhenPlaceIsSubstringOfHint()
    {
        // The hint is longer than the recorded place and contains it.
        var outcome = Matcher().Match(IndexOf(MorrillGed), "Frederick Morrill",
            new MatchHints(Birth: new EventHint(Place: "Lewiston, Maine, USA")));

        Assert.Equal(PersonMatchType.Single, outcome.PersonMatchType);
        Assert.Equal("@I5@", outcome.Matches[0].Individual.Xref);
    }

    [Fact]
    public void PlaceHint_NoContainmentEitherWay_AddsAvailableWeightButNoPoints()
    {
        var outcome = Matcher().Match(IndexOf(MorrillGed), "Frederick Morrill",
            new MatchHints(Birth: new EventHint(Place: "Boston, Massachusetts")));

        // Neither candidate's place matches, but both have a place recorded,
        // so the 15-point place weight counts toward availability for both
        // without being earned: raw 60 / available 75 = 80 for each. A
        // mismatched-but-present datum lowers the score (never excludes),
        // and it lowers it identically here, so the tie still survives.
        Assert.Equal(PersonMatchType.Candidates, outcome.PersonMatchType);
        Assert.All(outcome.Matches, m => Assert.Equal(80.0, m.FinalScore, 6));
    }

    [Fact]
    public void PlaceHint_CandidateWithNoPlaceRecorded_HintNotAvailable()
    {
        const string ged = """
            0 @I1@ INDI
            1 NAME Frederick /Morrill/
            1 SEX M
            """;
        var outcome = Matcher().Match(IndexOf(ged), "Frederick Morrill",
            new MatchHints(Birth: new EventHint(Place: "Gorham, Maine")));

        Assert.Equal(PersonMatchType.Single, outcome.PersonMatchType);
        var match = outcome.Matches[0];
        // No birth/death/census place recorded at all: the hint contributes
        // nothing, and the final score reduces to the plain name-only score.
        Assert.Equal(60.0, match.RawScore, 6);
        Assert.Equal(100.0, match.FinalScore, 6);
    }

    [Fact]
    public void ParentHint_CandidateWithNoParentRecorded_HintNotAvailable()
    {
        const string ged = """
            0 @I1@ INDI
            1 NAME Frederick /Morrill/
            1 SEX M
            """;
        var outcome = Matcher().Match(IndexOf(ged), "Frederick Morrill",
            new MatchHints(Parents: new ParentsHint(Father: "Wyman Morrill")));

        Assert.Equal(PersonMatchType.Single, outcome.PersonMatchType);
        var match = outcome.Matches[0];
        // No FamChild at all, so no parent name to compare against: the
        // hint contributes nothing, and the final score reduces to the
        // plain name-only score.
        Assert.Equal(60.0, match.RawScore, 6);
        Assert.Equal(100.0, match.FinalScore, 6);
    }

    // -------------------------------------------------------------------
    // Birth-year graded points (0/1/2/>2 year difference) and no hard
    // exclusion — even a wildly wrong year keeps the person in the result.
    // -------------------------------------------------------------------

    const string SingleBirthGed = """
        0 @I1@ INDI
        1 NAME Frederick /Morrill/
        1 SEX M
        1 BIRT
        2 DATE 12 MAR 1841
        """;

    [Theory]
    [InlineData(1841, 75.0, 100.0)]              // exact year: +15 of 15 available
    [InlineData(1842, 70.0, 93.333333333333)]     // 1 year off: +10
    [InlineData(1843, 65.0, 86.666666666666)]     // 2 years off: +5
    [InlineData(1900, 60.0, 80.0)]                // far off: +0, but never excluded
    public void BirthYearHint_AwardsGradedPoints(int hintYear, double expectedRaw, double expectedFinal)
    {
        var outcome = Matcher().Match(IndexOf(SingleBirthGed), "Frederick Morrill",
            new MatchHints(Birth: new EventHint(Year: hintYear)));

        Assert.Equal(PersonMatchType.Single, outcome.PersonMatchType);
        var match = outcome.Matches[0];
        // Name weight 60 + birth-year weight 15 = 75 available throughout;
        // only the awarded points vary with the year difference.
        Assert.Equal(expectedRaw, match.RawScore, 6);
        Assert.Equal(expectedFinal, match.FinalScore, 6);
    }

    [Fact]
    public void BirthYearHint_CandidateWithNoBirthRecorded_HintNotAvailable()
    {
        const string ged = """
            0 @I1@ INDI
            1 NAME Frederick /Morrill/
            1 SEX M
            """;
        var outcome = Matcher().Match(IndexOf(ged), "Frederick Morrill",
            new MatchHints(Birth: new EventHint(Year: 1841)));

        Assert.Equal(PersonMatchType.Single, outcome.PersonMatchType);
        var match = outcome.Matches[0];
        // No birth year to compare against: the hint contributes nothing,
        // and the final score reduces to the plain name-only score.
        Assert.Equal(60.0, match.RawScore, 6);
        Assert.Equal(100.0, match.FinalScore, 6);
    }

    // -------------------------------------------------------------------
    // Documented nicknames: decisive when similarity alone is weak, but
    // never outranking an exact or near-exact spelling.
    // -------------------------------------------------------------------

    const string WilliamGed = """
        0 @I1@ INDI
        1 NAME William /Morrill/
        1 SEX M
        """;

    [Fact]
    public void NicknameOverride_AppliesWhenSimilarityAloneIsWeak()
    {
        var outcome = Matcher(SyntheticNicknamesJson).Match(IndexOf(WilliamGed), "Bill Morrill");

        Assert.Equal(PersonMatchType.Single, outcome.PersonMatchType);
        // 35 (exact surname) + 20 (fixed nickname bonus, since Bill/William's
        // raw similarity * 25 is below 20) = 55 raw, out of 60 available.
        Assert.Equal(55.0, outcome.Matches[0].RawScore, 6);
        Assert.Equal(55.0 * 100.0 / 60.0, outcome.Matches[0].FinalScore, 6);
    }

    [Fact]
    public void WithoutDocumentedNickname_FallsBackToSimilarityAlone()
    {
        var outcome = Matcher(EmptyNicknamesJson).Match(IndexOf(WilliamGed), "Bill Morrill");

        double billWilliamSim = JaroWinkler.Similarity("BILL", "WILLIAM");
        double expectedRaw = 35.0 + billWilliamSim * 25.0;

        Assert.Equal(PersonMatchType.Single, outcome.PersonMatchType);
        Assert.Equal(expectedRaw, outcome.Matches[0].RawScore, 6);
    }

    [Fact]
    public void ExactSpelling_NeverOutrankedByNicknameBonus()
    {
        // Even with "frederick"/"fred" documented as equivalents, an exact
        // spelling match (25 pts) always beats the fixed 20-point bonus.
        var outcome = Matcher(SyntheticNicknamesJson).Match(IndexOf(MorrillGed), "Frederick Morrill");
        Assert.All(outcome.Matches, m => Assert.Equal(60.0, m.RawScore, 6));
    }

    [Fact]
    public void RelationalHints_DoNotConsultTheNicknameDictionary()
    {
        // "Bill Blake" as a spouse hint against an actual spouse "William
        // Blake": Bill/William is documented, but spouse-hint comparisons
        // use similarity alone, so the +20 only applies if the combined
        // strings genuinely clear the 0.85 threshold on their own.
        const string ged = """
            0 @I1@ INDI
            1 NAME Frederick /Morrill/
            1 SEX M
            1 FAMS @F1@
            0 @I2@ INDI
            1 NAME William /Blake/
            1 SEX M
            1 FAMS @F1@
            0 @F1@ FAM
            1 HUSB @I1@
            1 WIFE @I2@
            """;
        var outcome = Matcher(SyntheticNicknamesJson).Match(IndexOf(ged), "Frederick Morrill",
            new MatchHints(Spouse: new SpouseHint(Name: "Bill Blake")));

        double hintSim = JaroWinkler.Similarity("BILL BLAKE", "WILLIAM BLAKE");
        double expectedRaw = 60.0 + (hintSim >= 0.85 ? 20.0 : 0.0);

        Assert.Equal(PersonMatchType.Single, outcome.PersonMatchType);
        Assert.Equal(expectedRaw, outcome.Matches[0].RawScore, 6);
    }

    // -------------------------------------------------------------------
    // Ordering: raw-score tiebreak when final scores tie
    // -------------------------------------------------------------------

    [Fact]
    public void Ordering_FinalScoreRanksAboveARawButUnavailableAdvantage()
    {
        // A sanity check that final score (not raw point totals) governs
        // ranking in the ordinary case: an exact name+year match must beat
        // an exact name match with a wrong year, even though both had the
        // same evidence available to earn.
        const string ged = """
            0 @I1@ INDI
            1 NAME Xvqz /Smith/
            1 SEX M
            1 BIRT
            2 DATE 1900
            0 @I2@ INDI
            1 NAME Xvqz /Smith/
            1 SEX M
            1 BIRT
            2 DATE 1950
            """;
        var outcome = Matcher().Match(IndexOf(ged), "Xvqz Smith",
            new MatchHints(Birth: new EventHint(Year: 1900)));

        Assert.Equal(PersonMatchType.Single, outcome.PersonMatchType);
        Assert.Equal("@I1@", outcome.Matches[0].Individual.Xref);
    }

    [Fact]
    public void Ordering_IsDeterministic_AcrossRepeatedRuns()
    {
        var index = IndexOf(MorrillGed);
        var first = Matcher().Match(index, "Morrill");
        var second = Matcher().Match(index, "Morrill");
        Assert.Equal(first.Matches.Select(XrefOf), second.Matches.Select(XrefOf));
    }

    // -------------------------------------------------------------------
    // Candidate cap and truncation
    // -------------------------------------------------------------------

    [Fact]
    public void CandidateList_CapsAtEightAndSetsTruncated()
    {
        var ged = new StringBuilder();
        for (int i = 1; i <= 10; i++)
        {
            ged.AppendLine($"0 @I{i:D2}@ INDI");
            ged.AppendLine("1 NAME Jane /Doe/");
            ged.AppendLine("1 SEX F");
        }

        var outcome = Matcher().Match(IndexOf(ged.ToString()), "Jane Doe");

        Assert.Equal(PersonMatchType.Candidates, outcome.PersonMatchType);
        Assert.Equal(8, outcome.Matches.Count);
        Assert.True(outcome.Truncated);
        Assert.Equal(10, outcome.TotalMatches);
        Assert.Equal(
            ["@I01@", "@I02@", "@I03@", "@I04@", "@I05@", "@I06@", "@I07@", "@I08@"],
            outcome.Matches.Select(XrefOf));
    }

    // -------------------------------------------------------------------
    // maxResults: the recall set, its classification, and TotalMatches
    // never move; only how much of it comes back does.
    // -------------------------------------------------------------------

    static string TenJaneDoesGed()
    {
        var ged = new StringBuilder();
        for (int i = 1; i <= 10; i++)
        {
            ged.AppendLine($"0 @I{i:D2}@ INDI");
            ged.AppendLine("1 NAME Jane /Doe/");
            ged.AppendLine("1 SEX F");
        }
        return ged.ToString();
    }

    [Fact]
    public void MaxResults_Null_ReturnsWholeRecallSetUncapped()
    {
        var outcome = Matcher().Match(IndexOf(TenJaneDoesGed()), "Jane Doe", maxResults: null);

        Assert.Equal(PersonMatchType.Candidates, outcome.PersonMatchType);
        Assert.Equal(10, outcome.Matches.Count);
        Assert.Equal(10, outcome.TotalMatches);
        Assert.False(outcome.Truncated);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(10)]
    public void MaxResults_Integer_CapsListWithoutChangingTotalOrClassification(int cap)
    {
        var outcome = Matcher().Match(IndexOf(TenJaneDoesGed()), "Jane Doe", maxResults: cap);

        Assert.Equal(PersonMatchType.Candidates, outcome.PersonMatchType);
        Assert.Equal(Math.Min(cap, 10), outcome.Matches.Count);
        Assert.Equal(10, outcome.TotalMatches);
        Assert.Equal(cap < 10, outcome.Truncated);
    }

    [Fact]
    public void MaxResults_Integer_LargerThanRecallSet_IsNotTruncated()
    {
        var outcome = Matcher().Match(IndexOf(TenJaneDoesGed()), "Jane Doe", maxResults: 100);

        Assert.Equal(10, outcome.Matches.Count);
        Assert.Equal(10, outcome.TotalMatches);
        Assert.False(outcome.Truncated);
    }

    [Fact]
    public void MaxResults_One_OnADecisiveSingleWinner_StillClassifiesAsSingle()
    {
        // I1 has a hint-earned decisive win over the other Fredericks, but
        // more than one person is in the recall set: capping to 1 must not
        // change matchType away from Single, and TotalMatches must still
        // report the full recall set the classifier actually saw.
        var outcome = Matcher().Match(IndexOf(MorrillGedWithRivalSpouse), "Frederick Morrill",
            new MatchHints(Spouse: new SpouseHint(Name: "Sarah Blake")),
            maxResults: 1);

        Assert.Equal(PersonMatchType.Single, outcome.PersonMatchType);
        Assert.Single(outcome.Matches);
        Assert.Equal("@I1@", outcome.Matches[0].Individual.Xref);
        Assert.Equal(2, outcome.TotalMatches);
        Assert.True(outcome.Truncated);
    }

    [Fact]
    public void MaxResults_One_OnUnresolvedCandidates_ReturnsOneCandidateButStaysCandidates()
    {
        // maxResults: 1 can return one scored candidate while matchType
        // stays "candidates", totalMatches is
        // greater than 1, and truncated is true -- list length never
        // fabricates a confident classification.
        var outcome = Matcher().Match(IndexOf(TenJaneDoesGed()), "Jane Doe", maxResults: 1);

        Assert.Equal(PersonMatchType.Candidates, outcome.PersonMatchType);
        Assert.Single(outcome.Matches);
        Assert.Equal(10, outcome.TotalMatches);
        Assert.True(outcome.Truncated);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void MaxResults_ZeroOrNegative_Throws(int badCap)
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => Matcher().Match(IndexOf(TenJaneDoesGed()), "Jane Doe", maxResults: badCap));
    }

    [Fact]
    public void MaxResults_Omitted_DefaultsToEight()
    {
        // No maxResults argument at all behaves exactly like the historical
        // fixed-8 cap (mirrors CandidateList_CapsAtEightAndSetsTruncated,
        // but calling the 3-arg overload to pin down the default itself).
        var outcome = Matcher().Match(IndexOf(TenJaneDoesGed()), "Jane Doe");

        Assert.Equal(8, outcome.Matches.Count);
        Assert.Equal(10, outcome.TotalMatches);
        Assert.True(outcome.Truncated);
    }

    [Fact]
    public void TotalMatches_ForNoMatch_IsZero()
    {
        var outcome = Matcher().Match(IndexOf(MorrillGed), "Zzqxvw Bbdfgh");
        Assert.Equal(PersonMatchType.None, outcome.PersonMatchType);
        Assert.Equal(0, outcome.TotalMatches);
        Assert.False(outcome.Truncated);
    }

    [Fact]
    public void TotalMatches_ForSingle_CanExceedOne_WhenADecisiveWinnerBeatsWeakerAlternatives()
    {
        var outcome = Matcher().Match(IndexOf(MorrillGedWithRivalSpouse), "Frederick Morrill",
            new MatchHints(Spouse: new SpouseHint(Name: "Sarah Blake")));

        Assert.Equal(PersonMatchType.Single, outcome.PersonMatchType);
        Assert.Equal(2, outcome.TotalMatches);
        // Matches now consistently carries the capped recall set, including
        // the weaker alternative, not just the winner.
        Assert.Equal(2, outcome.Matches.Count);
        Assert.Equal(["@I1@", "@I5@"], outcome.Matches.Select(XrefOf));
    }

    [Fact]
    public void Suggestion_CarriesTheNameOnlyScoreThatPlacedIt()
    {
        // Exact surname (35) + zero-overlap given name (0) over a 60-point
        // two-token weight = 58.333...: the same PersonMatcherTests fixture
        // as SurnameOnlyMatch_LandsInSuggestionBand_WithCloseSpellingReason.
        var outcome = Matcher().Match(IndexOf(SmithGed), "Xvqz Smith");

        var suggestion = Assert.Single(outcome.Suggestions);
        Assert.Equal(35.0 * 100.0 / 60.0, suggestion.Score, 6);
        Assert.InRange(suggestion.Score, 55.0, 69.0);
    }
}
