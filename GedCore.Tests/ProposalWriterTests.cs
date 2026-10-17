using GedCore.Drafting;

namespace GedCore.Tests;

public class ProposalWriterTests
{
    const string Finding = """
        { "id": "F-1", "item": 1, "extract": "Died 1 Jan 1900.\nBuried at Salem.",
          "source": { "ref": "@S1@", "name": "Find a Grave", "author": "Volunteers", "url": "https://example.org/fg", "locator": "Memorial #1",
                      "accessed": "2 FEB 2026", "class": "derivative", "information": "primary" },
          "claims": [ { "record": "@I1@", "fact": "DEAT", "date": "1 JAN 1900", "place": "Salem", "quay": 2, "class": "original" },
                      { "relation": "child", "person": "@I1@", "other": "@I2@", "family": "@F1@", "quay": 1 } ] }
        """;

    const string DeathOp = """
        { "op": "createOrUpdateVital", "record": "@I1@", "fact": "DEAT", "value": { "date": "1 JAN 1900", "place": "Salem" },
          "citation": { "source": "@S1@", "page": "Memorial #1", "dataText": "Died 1 Jan 1900.", "quay": 2 }, "findings": ["F-1"] }
        """;

    static string Changeset(string ops = DeathOp, string findings = Finding) => $$"""
        { "items": [ { "item": 1, "target": "@I1@ Gladys enrich-death", "ops": [ {{ops}} ] } ], "findings": [ {{findings}} ] }
        """;

    static ProposalChangeset Presented(string name = "a.changes.json", string? json = null, string sha = "abc123") =>
        new(name, sha, json ?? Changeset());

    static string Write(params ProposalChangeset[] changesets)
    {
        var document = ProposalWriter.Write("W67-test", changesets);
        Assert.True(document.Success, string.Join("; ", document.Errors));
        return document.Markdown!;
    }

    [Fact]
    public void EachChangeset_GetsANumberedSectionNamingItsFileAndHash()
    {
        string markdown = Write(Presented("one.changes.json", sha: "aaa"), Presented("two.changes.json", sha: "bbb"));

        Assert.Contains("# Proposal: W67-test", markdown);
        Assert.Contains("## Changeset 1: one.changes.json", markdown);
        Assert.Contains("## Changeset 2: two.changes.json", markdown);
        Assert.Contains("- File: `one.changes.json`", markdown);
        Assert.Contains("- SHA-256: `bbb`", markdown);
        Assert.True(markdown.IndexOf("Changeset 1:", StringComparison.Ordinal) < markdown.IndexOf("Changeset 2:", StringComparison.Ordinal));
    }

    [Fact]
    public void AnItem_ListsItsOperationsWithTheirGradesAndTheFindingsTheyCite()
    {
        string markdown = Write(Presented());

        Assert.Contains("### Item 1: @I1@ Gladys enrich-death", markdown);
        Assert.Contains("1. `createOrUpdateVital`: Set DEAT on @I1@: 1 JAN 1900 at Salem (QUAY 2), evidence F-1", markdown);
    }

    [Fact]
    public void AnOpWithNoFindings_IsListedWithoutEvidence()
    {
        string note = """{ "op": "createOrUpdateNote", "record": "@I1@", "text": "needs independent verification:\nDEAT" }""";

        string markdown = Write(Presented(json: Changeset(ops: $"{DeathOp}, {note}")));

        Assert.Contains("2. `createOrUpdateNote`: Add a note on @I1@: \"needs independent verification: DEAT\"", markdown);
        Assert.DoesNotContain("DEAT\", evidence", markdown);
    }

    [Fact]
    public void ACitedFinding_IsPresentedWithItsVerbatimExtractSourceClassesAndGrades()
    {
        string markdown = Write(Presented());

        Assert.Contains("##### F-1: Find a Grave", markdown);
        Assert.Contains("- Source: Find a Grave, by Volunteers", markdown);
        Assert.Contains("- Locator: Memorial #1", markdown);
        Assert.Contains("- URL: https://example.org/fg", markdown);
        Assert.Contains("- Accessed: 2 FEB 2026", markdown);
        Assert.Contains("- Source class: derivative", markdown);
        Assert.Contains("- Information: primary", markdown);
        Assert.Contains("  - DEAT on @I1@: 1 JAN 1900 at Salem: QUAY 2 (class original for this claim)", markdown);
        Assert.Contains("  - child @I1@ with @I2@: QUAY 1", markdown);
        Assert.Contains("> Died 1 Jan 1900.\n> Buried at Salem.", markdown.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void AFindingOfTheItemNoOpCites_IsStillPresentedAsEvidence()
    {
        string uncited = Finding.Replace("\"F-1\"", "\"F-2\"");

        string markdown = Write(Presented(json: Changeset(findings: $"{Finding}, {uncited}")));

        Assert.Contains("##### F-1:", markdown);
        Assert.Contains("##### F-2:", markdown);
    }

    [Fact]
    public void ADescribedOp_ReadsAsProse()
    {
        string ops = """
            { "op": "createOrUpdateSpouse", "person": "@I1@", "spouse": { "xref": "@NewI1@", "name": "Hiram /Giedd/" }, "family": "@NewF1@" },
            { "op": "createOrUpdateChild", "family": "@F1@", "child": "@I3@" },
            { "op": "createOrUpdateParent", "person": "@I3@", "role": "father", "parent": "@I2@" },
            { "op": "createOrUpdateCitation", "record": "@I1@", "fact": "BIRT", "match": { "date": "1870" } },
            { "op": "createOrUpdateNote", "record": "@I1@", "match": "old", "text": "new" },
            { "op": "deleteChild", "family": "@F1@", "child": "@I4@" },
            { "op": "deleteParent", "person": "@I3@", "role": "mother" },
            { "op": "createOrUpdateVital", "record": "@I1@", "fact": "CENS", "mode": "add", "value": { "date": "1900" } },
            { "op": "createOrUpdateVital", "record": "@I1@", "fact": "NAME", "value": "Ruth /Giedd/" }
            """;

        string markdown = Write(Presented(json: Changeset(ops)));

        Assert.Contains("Link @I1@ to spouse Hiram /Giedd/ (@NewI1@) in @NewF1@", markdown);
        Assert.Contains("Add child @I3@ to @F1@", markdown);
        Assert.Contains("Add father @I2@ to @I3@", markdown);
        Assert.Contains("Cite BIRT on @I1@ (1870)", markdown);
        Assert.Contains("Rewrite a note on @I1@: \"new\"", markdown);
        Assert.Contains("Remove child @I4@ from @F1@", markdown);
        Assert.Contains("Remove the mother of @I3@", markdown);
        Assert.Contains("Add CENS on @I1@: 1900", markdown);
        Assert.Contains("Set NAME on @I1@: Ruth /Giedd/", markdown);
    }

    [Fact]
    public void AnOpCitingAFindingTheChangesetDoesNotCarry_FailsNamingTheFile()
    {
        var document = ProposalWriter.Write("p", [Presented("a.changes.json", Changeset(findings: "{ \"id\": \"F-9\", \"item\": 1 }"))]);

        Assert.False(document.Success);
        Assert.Null(document.Markdown);
        Assert.Equal("a.changes.json: item 1, operation 1 cites finding F-1, which the changeset does not carry.", Assert.Single(document.Errors));
    }

    [Fact]
    public void AChangesetWithNoFindings_IsRefusedNotPresentedBare()
    {
        var document = ProposalWriter.Write("p", [Presented("a.changes.json", """{ "items": [ { "item": 1, "ops": [] } ] }""")]);

        Assert.Contains("a.changes.json carries no findings", Assert.Single(document.Errors));
    }

    [Theory]
    [InlineData("{ nope", "is not valid JSON")]
    [InlineData("{ \"findings\": [] }", "has no items to present")]
    public void AChangesetThatCannotBeRead_FailsNamingTheFile(string json, string expected)
    {
        var document = ProposalWriter.Write("p", [Presented("a.changes.json", json)]);

        Assert.Null(document.Markdown);
        Assert.Contains(expected, Assert.Single(document.Errors));
    }

    [Fact]
    public void OneBadChangeset_FailsTheWholeProposalAndEveryBadOneIsReported()
    {
        var document = ProposalWriter.Write("p", [Presented("good.json"), Presented("bad1.json", "{ nope"), Presented("bad2.json", "{}")]);

        Assert.Null(document.Markdown);
        Assert.Equal(2, document.Errors.Count);
        Assert.Contains("bad1.json", document.Errors[0]);
        Assert.Contains("bad2.json", document.Errors[1]);
    }

    [Fact]
    public void TheSameChangesets_GiveTheSameProposal() =>
        Assert.Equal(Write(Presented(), Presented("b.json")), Write(Presented(), Presented("b.json")));
}
