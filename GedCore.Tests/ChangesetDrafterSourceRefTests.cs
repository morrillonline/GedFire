using System.Text;
using System.Text.Json;
using GedCore.Apply;
using GedCore.Drafting;
using GedCore.Ged70;

namespace GedCore.Tests;

/// <summary>Findings that name their source by xref, cite a held citation that has no page, or add only a comment citing no source.</summary>
public class ChangesetDrafterSourceRefTests
{
    const string Title = "Town Vital Records";

    // Two sources share a title; the birth cites the second without a page, the death cites the first on a page.
    const string Document = """
        0 HEAD
        1 GEDC
        2 VERS 7.0
        0 @I1@ INDI
        1 NAME Hiram /Test/
        1 SEX M
        1 BIRT
        2 DATE 1 JUN 1800
        2 SOUR @S2@
        3 DATA
        4 TEXT Hiram born 1 June 1800.
        1 DEAT
        2 DATE 1873
        2 SOUR @S1@
        3 PAGE p. 9
        0 @S1@ SOUR
        1 TITL Town Vital Records
        0 @S2@ SOUR
        1 TITL Town Vital Records
        0 TRLR
        """;

    sealed class DocumentLookup : IDraftLookup
    {
        public IReadOnlyList<ExistingFact> FactsOn(string record, string tag) => (record, tag) switch
        {
            ("@I1@", "BIRT") => [new ExistingFact("1 JUN 1800", null, [new ExistingCitation("@S2@", null, "Hiram born 1 June 1800.", null)])],
            ("@I1@", "DEAT") => [new ExistingFact("1873", null, [new ExistingCitation("@S1@", "p. 9", null, null)])],
            _ => [],
        };

        public IReadOnlyList<string> NotesOn(string record) => [];
        public string? FindSource(string title, string? author) => SourceTitles.SameCollection(title, Title) ? "@S1@" : null;
        public ExistingSource? SourceRecord(string xref) => xref is "@S1@" or "@S2@" ? new ExistingSource(Title, null) : null;
        public string? PersonName(string xref) => xref == "@I1@" ? "Hiram /Test/" : null;
        public bool? HasRecord(string xref) => xref is "@I1@" or "@S1@" or "@S2@";
    }

    static DraftFinding Finding(string? sourceRef, string? locator, params DraftFact[] facts) =>
        new("F-1", new DraftSource(Title, null, null, locator, null, "original", "primary") { Ref = sourceRef },
            "Hiram born 1 June 1800.", new DraftTarget("@I1@", "enrich", null), facts, []);

    static DraftFact CiteOnly(string tag, string date, string? commentary = null) =>
        new("@I1@", tag, date, null, 2, null, null, null) { CiteOnly = true, Commentary = commentary };

    static DraftResult Draft(DraftFinding finding) => new ChangesetDrafter(new DocumentLookup()).Draft([finding]);

    static JsonElement SingleOp(DraftResult result) =>
        JsonDocument.Parse(result.Changeset!.ToJsonString()).RootElement
            .GetProperty("items").EnumerateArray().SelectMany(i => i.GetProperty("ops").EnumerateArray()).Single();

    static GedRecord AppliedFact(DraftResult result, string tag)
    {
        Assert.True(result.Success, string.Join("; ", result.Errors));
        var applied = ChangesetApplier.Run(Encoding.UTF8.GetBytes(Document), Changeset.Parse(result.Changeset!.ToJsonString()), [1], dryRun: false);
        Assert.True(applied.Success, string.Join("; ", applied.Errors));
        return Ged70Parser.Read(new MemoryStream(applied.OutputBytes!)).ByXref["@I1@"].FirstChild(tag)!;
    }

    // ---- source.ref ----

    [Fact]
    public void ASourceRef_CitesTheNamedSource_NotTheSameTitledSourceTheTitleWouldFind()
    {
        var result = Draft(Finding("@S2@", "entry 12", CiteOnly("DEAT", "1873")));

        Assert.Equal("@S2@", SingleOp(result).GetProperty("citations")[0].GetProperty("source").GetString());
        Assert.Contains(result.Decisions, d => d.Kind == DraftDecisionKinds.ReusedSource && d.Detail.Contains("citing @S2@, the source the finding names"));
        Assert.Equal(["@S1@", "@S2@"], AppliedFact(result, "DEAT").ChildrenByTag("SOUR").Select(s => s.Value));
    }

    [Fact]
    public void ASourceRefTheDocumentDoesNotHold_IsRefused() =>
        Assert.Equal("findings[0].source.ref @S9@ is not a source in the document.",
            Assert.Single(Draft(Finding("@S9@", "entry 12", CiteOnly("BIRT", "1 JUN 1800"))).Errors));

    [Fact]
    public void ASourceRefTitledOtherwiseThanTheFinding_IsRefused()
    {
        var finding = Finding("@S2@", "entry 12", CiteOnly("BIRT", "1 JUN 1800"));
        finding = finding with { Source = finding.Source with { Name = "County Deeds" } };

        Assert.Contains("findings[0].source.ref @S2@ is titled \"Town Vital Records\", not \"County Deeds\"", Assert.Single(Draft(finding).Errors));
    }

    // ---- no locator ----

    [Fact]
    public void WithoutALocator_ACiteOnlyFactUpdatesTheHeldCitationThatHasNoPage()
    {
        var result = Draft(Finding("@S2@", null, CiteOnly("BIRT", "1 JUN 1800", "The town clerk copied the entry from the family record.")));

        Assert.False(SingleOp(result).GetProperty("citations")[0].TryGetProperty("page", out _));
        var cited = Assert.Single(AppliedFact(result, "BIRT").ChildrenByTag("SOUR"));
        Assert.Null(cited.FirstChild("PAGE"));
        Assert.Equal("The town clerk copied the entry from the family record.", cited.FirstChild("NOTE")!.FullValue());
    }

    [Fact]
    public void WithoutALocator_AHeldFactCitingTheSourceOnlyOnAPage_IsRefused() =>
        Assert.Equal(
            "finding \"F-1\" has no locator, but the DEAT on @I1@ it cites holds no citation of @S1@ without a page; a citation without a page only adds to such a citation, so give source.locator.",
            Assert.Single(Draft(Finding("@S1@", null, CiteOnly("DEAT", "1873"))).Errors));

    [Fact]
    public void WithoutALocator_AFactThatIsNotCiteOnly_IsRefusedWithoutTheDocument()
    {
        var finding = Finding("@S2@", null, new DraftFact("@I1@", "BURI", "1873", null, 2, null, null, null));

        Assert.Equal(
            "findings[0].facts[0] cites its finding, which has no locator; a finding without source.locator can only add to a held citation that has no page, through a citeOnly fact.",
            Assert.Single(ChangesetDrafter.CheckWithoutDocument([finding])));
    }

    // ---- commentary only ----

    [Fact]
    public void ACommentaryOnlyFact_AddsACommentCitingNoSourceToTheHeldFact_AndCitesNothingOfTheFinding()
    {
        var comment = new DraftFact("@I1@", "DEAT", "1873", null, 0, null, null, null)
        {
            CommentaryOnly = true, Commentary = "No death record survives; the year is from his gravestone.",
        };

        var result = Draft(Finding(null, "entry 12", comment));

        var op = SingleOp(result);
        Assert.Equal("1873", op.GetProperty("match").GetProperty("date").GetString());
        var citation = Assert.Single(op.GetProperty("citations").EnumerateArray());
        Assert.Equal(DraftReferences.Void, citation.GetProperty("source").GetString());
        Assert.DoesNotContain(result.Decisions, d => d.Kind is DraftDecisionKinds.ReusedSource or DraftDecisionKinds.MintedSource);
        var death = AppliedFact(result, "DEAT");
        Assert.Equal(["@S1@", DraftReferences.Void], death.ChildrenByTag("SOUR").Select(s => s.Value));
        Assert.Equal("No death record survives; the year is from his gravestone.", death.ChildrenByTag("SOUR").Last().FirstChild("NOTE")!.FullValue());
    }
}
