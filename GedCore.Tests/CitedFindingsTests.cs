using GedCore.Drafting;
using GedFire.Mcp;

namespace GedCore.Tests;

public class CitedFindingsTests
{
    static readonly DraftSource Register = new("Register", null, null, "p. 1", null, "original", "primary");

    static DraftFinding Finding(string id, params string[] cites) =>
        new(id, Register, "x", new DraftTarget("@I1@", "enrich", null), [],
            cites.Length == 0 ? [] : [new DraftNote("@I1@", "A note.") { Cites = [.. cites.Select(c => new DraftNoteCitation(c, 1, null))] }]);

    [Fact]
    public void OnlyTheFindingsTheNotesCite_FollowTheFinding()
    {
        var cited = new CitedFindings(Finding("F-1", "F-2"), [Finding("F-2"), Finding("F-3"), Finding("F-2")]);

        Assert.Equal(["F-1", "F-2"], cited.Findings.Select(f => f.Id));
    }

    [Fact]
    public void TheCheckedFindingsErrorsAreRelabelled_AndACitedFindingsOwnAreLeftOut()
    {
        var cited = new CitedFindings(Finding("F-1", "F-2"), [Finding("F-2")]);

        var errors = cited.OwnErrors(
            ["findings[0].notes[0].cites[0].excerpt is not part of the extract of findings[1]", "findings[1].facts[0].quay is 3", "a batch error"],
            "F-1.json (findings[0])");

        Assert.Equal(["F-1.json (findings[0]).notes[0].cites[0].excerpt is not part of the extract of finding \"F-2\"", "a batch error"], errors);
    }
}
