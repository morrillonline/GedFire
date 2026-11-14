using GedFire.Gen;
using GedFire.Match;

namespace GedCore.Tests;

public class RecordVerdictTests
{
    static RecordCandidateScore Score(string xref, double composite, double name = 95, double? lifespan = null, double? kin = null, double? place = null) =>
        new(new GedIndividual { Xref = xref }, composite, name, lifespan, kin, place, []);

    static readonly Dictionary<string, string> NoneRuledOut = [];

    [Fact]
    public void ALoneCandidateTheLifespanSupports_IsSingle() =>
        Assert.Equal(RecordVerdict.Single, RecordVerdict.Decide([Score("@I1@", 80, lifespan: 100)], NoneRuledOut));

    [Fact]
    public void ALoneCandidateWithOnlyTheName_IsWeak() =>
        Assert.Equal(RecordVerdict.Weak, RecordVerdict.Decide([Score("@I1@", 95, kin: 0)], NoneRuledOut));

    [Fact]
    public void ASupportedLeaderRuledOutAsAKnownPersonsRelative_IsWeak() =>
        Assert.Equal(RecordVerdict.Weak, RecordVerdict.Decide([Score("@I1@", 95, place: 100)], new Dictionary<string, string> { ["@I1@"] = "not the father" }));

    [Fact]
    public void NoCandidateReachingTheNameFloor_IsNone() =>
        Assert.Equal(RecordVerdict.None, RecordVerdict.Decide([Score("@I1@", 80, name: 60, lifespan: 100)], NoneRuledOut));

    [Fact]
    public void TwoCloseCandidates_AreCandidates() =>
        Assert.Equal(RecordVerdict.Candidates, RecordVerdict.Decide([Score("@I1@", 95, lifespan: 100), Score("@I2@", 90, lifespan: 100)], NoneRuledOut));

    [Fact]
    public void ADecisiveSupportedLead_IsSingle() =>
        Assert.Equal(RecordVerdict.Single, RecordVerdict.Decide([Score("@I1@", 95, kin: 100), Score("@I2@", 70, kin: 0)], NoneRuledOut));
}
