using GedCore.Drafting;
using GedFire.Mcp;

namespace GedCore.Tests;

public class CitedFindingsTests
{
    static readonly DraftSource Register = new("Register", null, null, "p. 1", null, "original", "primary");

    static DraftFinding Finding(string id, string[] noteCites, string[]? factCites = null) =>
        new(id, Register, "x", new DraftTarget("@I1@", "enrich", null),
            factCites is null ? [] : [new DraftFact("@I1@", "BIRT", "1850", null, 1, null, null, null) { Cites = [.. factCites.Select(c => new DraftCite(c, 1, null))] }],
            noteCites.Length == 0 ? [] : [new DraftNote("@I1@", "A note.") { Cites = [.. noteCites.Select(c => new DraftCite(c, 1, null))] }]);

    static DraftFinding Finding(string id) => Finding(id, []);

    [Fact]
    public void OnlyTheFindingsTheNotesAndFactsCite_AreCited()
    {
        var cited = new CitedFindings(Finding("F-1", ["F-2", "F-1"], ["F-4"]), [Finding("F-2"), Finding("F-3"), Finding("F-2"), Finding("F-4")]);

        Assert.Equal("F-1", cited.Finding.Id);
        Assert.Equal(["F-2", "F-4"], cited.Cited.Select(f => f.Id));
    }

    [Fact]
    public void TheCheckedFindingsErrorsAreRelabelled() =>
        Assert.Equal(
            ["F-1.json (findings[0]).notes[0].cites[0].excerpt is not part of the extract of finding \"F-2\"", "a batch error"],
            CitedFindings.Relabel(["findings[0].notes[0].cites[0].excerpt is not part of the extract of finding \"F-2\"", "a batch error"], "F-1.json (findings[0])"));
}
