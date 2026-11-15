using GedCore.Apply;
using GedCore.Drafting;

namespace GedCore.Tests;

/// <summary>The drafter refuses a new relative the tree person already has, as validation would, and passes it once the finding lists that relative in notDuplicateOf.</summary>
public class ChangesetDrafterRelativeTests
{
    sealed class FakeLookup : IDraftLookup
    {
        public IReadOnlyList<ExistingFact> FactsOn(string record, string tag) => [];
        public IReadOnlyList<string> NotesOn(string record) => [];
        public string? FindSource(string title, string? author) => null;
        public string? PersonName(string xref) => xref switch { "@I1@" => "Abel /Morrill/", "@I2@" => "Nathaniel /Morrill/", _ => null };
        public Relative? Person(string xref) => PersonName(xref) is { } name ? new Relative(xref, name) : null;
        public IReadOnlyList<string> FamiliesAsChild(string person) => person == "@I1@" ? ["@F1@"] : [];
        public Relative? Partner(string family, string tag) => family == "@F1@" && tag == "HUSB" ? Person("@I2@") : null;
    }

    static DraftFinding Finding(string fatherName, params string[] notDuplicateOf) =>
        new("F-1", new DraftSource("County Register", null, null, "Entry 41", null, "original", "primary"), "son of Seth",
            new DraftTarget("@I1@", "new-parent", null), [], [])
        {
            People = [new DraftPerson("@NewI1@", fatherName, "M")],
            Relations = [new DraftRelation(DraftRelation.Parent, "@I1@", "@NewI1@", null, "father", null, null, 2) { NotDuplicateOf = notDuplicateOf }],
        };

    [Fact]
    public void ANewFatherForAPersonWhoHasOne_IsRefused()
    {
        var result = new ChangesetDrafter(new FakeLookup()).Draft([Finding("Seth /Morrill/")]);

        Assert.False(result.Success);
        Assert.Contains(result.Errors, e => e.Contains("already has the father Nathaniel Morrill (@I2@)"));
    }

    [Fact]
    public void TheFatherListedInNotDuplicateOf_IsDrafted()
    {
        var result = new ChangesetDrafter(new FakeLookup()).Draft([Finding("Seth /Morrill/", "@I2@")]);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Contains("\"notDuplicateOf\":[\"@I2@\"]", result.Changeset!.ToJsonString());
    }
}
