using GedCore.Apply;

namespace GedCore.Tests;

// SharedSourceCopier is internal, so copy-on-update is exercised through
// ChangesetApplier. The census source @S00002@ is cited on three structures
// (Allen's birth and both of Nellie's census facts); @S00003@ on one.
public class SharedSourceCopyTests : ApplyTestBase
{
    static List<string> Lines()
    {
        string text = string.Join("\n", BaseLines)
            .Replace("1 BIRT\n2 DATE 1928\n2 PLAC Minnesota", "1 BIRT\n2 DATE 1928\n2 PLAC Minnesota\n2 SOUR @S00002@\n3 PAGE p. 1")
            .Replace("1 CENS\n2 DATE 1930\n2 PLAC Minnesota", "1 CENS\n2 DATE 1930\n2 PLAC Minnesota\n2 SOUR @S00002@\n3 PAGE p. 5")
            .Replace("1 CENS\n2 DATE 1940\n2 PLAC Minnesota", "1 CENS\n2 DATE 1940\n2 PLAC Minnesota\n2 SOUR @S00002@\n3 PAGE p. 6")
            .Replace("1 NAME Harvey /Test/\n1 SEX M", "1 NAME Harvey /Test/\n1 SEX M\n1 DEAT\n2 DATE 1950\n2 SOUR @S00003@\n3 PAGE p. 2")
            .Replace("0 TRLR",
                "0 @S00002@ SOUR\n1 AUTH US Census Bureau\n1 TITL 1850 Census\n" +
                "0 @S00003@ SOUR\n1 TITL Family Bible\n0 TRLR");
        return [.. text.Split('\n')];
    }

    const string RetitleCensus =
        """{ "op": "createOrUpdateSource", "xref": "@S00002@", "title": "1850 Census, Northvale County" }""";

    const string CiteCensusOnAllensBirth =
        """{ "op": "createOrUpdateCitation", "record": "@I00001@", "fact": "BIRT", "citation": { "source": "@S00002@", "page": "p. 1" } }""";

    static string Item(params string[] ops) =>
        "{ \"items\": [ { \"item\": 1, \"ops\": [ " + string.Join(", ", ops) + " ] } ] }";

    static string Dump(GedRecord node) =>
        node.Tag + "=" + node.Value + "(" + string.Join(",", node.Children.Select(Dump)) + ")";

    string RecordText(string xref) => Dump(ReadDoc().ByXref[xref]);

    static IEnumerable<string?> CitedSources(GedRecord fact) => fact.ChildrenByTag("SOUR").Select(s => s.Value);

    GedRecord Birth(string xref) => ReadDoc().ByXref[xref].ChildrenByTag("BIRT").Single();

    [Fact]
    public void UpdatingASharedSource_AppliesToACopyCitedOnlyByTheFactBeingEdited()
    {
        WriteFile(Lines());
        string census = RecordText("@S00002@");
        string nellie = RecordText("@I00003@");

        var result = RunExpectSuccess(Item(RetitleCensus, CiteCensusOnAllensBirth));

        Assert.Equal("@S00004@", result.CopiedSources["@S00002@"]);
        Assert.Equal(1, result.Deltas["SOUR"]);
        Assert.Equal(census, RecordText("@S00002@"));
        Assert.Equal(nellie, RecordText("@I00003@"));

        var copy = ReadDoc().ByXref["@S00004@"];
        Assert.Equal("1850 Census, Northvale County", copy.FirstChild("TITL")!.Value);
        Assert.Equal("US Census Bureau", copy.FirstChild("AUTH")!.Value);

        var swapped = Assert.Single(Birth("@I00001@").ChildrenByTag("SOUR"));
        Assert.Equal("@S00004@", swapped.Value);
        Assert.Equal("p. 1", swapped.FirstChild("PAGE")!.Value);
        Assert.Contains(result.Log, l => l.Contains("shared by 3 structures") && l.Contains("copied to @S00004@"));
    }

    [Fact]
    public void TheOriginalSourceIsNeverStampedAsChanged()
    {
        WriteFile(Lines());

        RunExpectSuccess(Item(RetitleCensus, CiteCensusOnAllensBirth));

        Assert.Null(ReadDoc().ByXref["@S00002@"].FirstChild("CHAN"));
    }

    [Fact]
    public void TheOrderOfOpsWithinTheItemDoesNotMatter()
    {
        WriteFile(Lines());
        byte[] start = ReadBytes();

        var citeFirst = Run(Item(CiteCensusOnAllensBirth, RetitleCensus), utcNow: new DateTime(2026, 9, 30, 0, 0, 0, DateTimeKind.Utc));
        byte[] citeFirstBytes = citeFirst.OutputBytes!;
        WriteFile(Lines());
        var sourceFirst = Run(Item(RetitleCensus, CiteCensusOnAllensBirth), utcNow: new DateTime(2026, 9, 30, 0, 0, 0, DateTimeKind.Utc));

        Assert.True(citeFirst.Success, string.Join("; ", citeFirst.Errors));
        Assert.True(sourceFirst.Success, string.Join("; ", sourceFirst.Errors));
        Assert.Equal(citeFirstBytes, sourceFirst.OutputBytes);
        Assert.NotEqual(start, citeFirstBytes);
    }

    [Fact]
    public void CitingTheEditedSourceOnAnotherFact_LeavesExistingCitersOnTheOriginal()
    {
        WriteFile(Lines());

        RunExpectSuccess(Item(RetitleCensus,
            """{ "op": "createOrUpdateCitation", "record": "@I00002@", "fact": "DEAT", "citation": { "source": "@S00002@", "page": "p. 9" } }"""));

        var harvey = ReadDoc().ByXref["@I00002@"].ChildrenByTag("DEAT").Single();
        Assert.Equal(["@S00003@", "@S00004@"], CitedSources(harvey));
        Assert.Equal("@S00002@", Birth("@I00001@").ChildrenByTag("SOUR").Single().Value);
    }

    [Fact]
    public void UpdatingASharedSourceWithoutCitingItInTheItem_IsRejectedAndWritesNothing()
    {
        WriteFile(Lines());
        byte[] before = ReadBytes();

        var result = Run(Item(RetitleCensus));

        Assert.False(result.Success);
        string errors = string.Join("; ", result.Errors);
        Assert.Contains("cited by 3 structures", errors);
        Assert.Contains("applied to a copy", errors);
        Assert.Equal(before, ReadBytes());
    }

    [Fact]
    public void UpdatingASourceCitedByOneStructure_IsDoneInPlace()
    {
        WriteFile(Lines());

        var result = RunExpectSuccess(Item(
            """{ "op": "createOrUpdateSource", "xref": "@S00003@", "title": "Family Bible of the Test family" }"""));

        Assert.Empty(result.CopiedSources);
        Assert.False(result.Deltas.ContainsKey("SOUR"));
        Assert.Equal("Family Bible of the Test family", ReadDoc().ByXref["@S00003@"].FirstChild("TITL")!.Value);
    }

    [Fact]
    public void UpdateToAnExistingExactSource_UsesThatSourceInsteadOfCreatingACopy()
    {
        var lines = Lines();
        lines.InsertRange(lines.IndexOf("0 TRLR"), ["0 @S00005@ SOUR", "1 AUTH US Census Bureau", "1 TITL 1850 Census, Northvale County"]);
        WriteFile(lines);
        string target = RecordText("@S00005@");

        var result = RunExpectSuccess(Item(RetitleCensus, CiteCensusOnAllensBirth));

        Assert.Equal("@S00005@", result.CopiedSources["@S00002@"]);
        Assert.False(result.Deltas.ContainsKey("SOUR"));
        Assert.Equal(target, RecordText("@S00005@"));
        Assert.Equal("@S00005@", Assert.Single(Birth("@I00001@").ChildrenByTag("SOUR")).Value);
    }

    [Fact]
    public void AnUpdateThatChangesNothing_DoesNotCopyAndWritesNothing()
    {
        WriteFile(Lines());
        byte[] before = ReadBytes();

        var result = RunExpectSuccess(Item(
            """{ "op": "createOrUpdateSource", "xref": "@S00002@", "title": "1850 Census" }"""));

        Assert.Empty(result.CopiedSources);
        Assert.Equal(before, ReadBytes());
    }

    [Fact]
    public void WithThreeHundredFiftyCitersOfOneSource_EditingOnePage_LeavesEveryOtherPersonAndTheSourceUntouched()
    {
        var lines = BaseLines.ToList();
        int trlr = lines.IndexOf("0 TRLR");
        var people = new List<string>();
        for (int i = 1; i <= 350; i++)
            people.AddRange([$"0 @I{1000 + i}@ INDI", $"1 NAME Person{i} /Census/", "1 BIRT", "2 DATE 1850",
                             "2 SOUR @S00002@", $"3 PAGE p. {i}"]);
        people.AddRange(["0 @S00002@ SOUR", "1 AUTH US Census Bureau", "1 TITL 1850 Census"]);
        lines.InsertRange(trlr, people);
        WriteFile(lines);
        var before = Enumerable.Range(1, 350).ToDictionary(i => i, i => RecordText($"@I{1000 + i}@"));
        string source = RecordText("@S00002@");

        RunExpectSuccess(Item(
            """{ "op": "createOrUpdateCitation", "record": "@I1001@", "fact": "BIRT", "citation": { "source": "@S00002@", "page": "p. 1, line 4" } }"""));

        Assert.Contains("p. 1, line 4", RecordText("@I1001@"));
        for (int i = 2; i <= 350; i++)
            Assert.Equal(before[i], RecordText($"@I{1000 + i}@"));
        Assert.Equal(source, RecordText("@S00002@"));
    }

    [Fact]
    public void WithThreeHundredFiftyCitersOfOneSource_EditingTheSourceForOnePerson_LeavesTheOtherPeopleOnTheOriginal()
    {
        var lines = BaseLines.ToList();
        int trlr = lines.IndexOf("0 TRLR");
        var people = new List<string>();
        for (int i = 1; i <= 350; i++)
            people.AddRange([$"0 @I{1000 + i}@ INDI", $"1 NAME Person{i} /Census/", "1 BIRT", "2 DATE 1850",
                             "2 SOUR @S00002@", $"3 PAGE p. {i}"]);
        people.AddRange(["0 @S00002@ SOUR", "1 AUTH US Census Bureau", "1 TITL 1850 Census"]);
        lines.InsertRange(trlr, people);
        WriteFile(lines);
        var before = Enumerable.Range(2, 349).ToDictionary(i => i, i => RecordText($"@I{1000 + i}@"));
        string source = RecordText("@S00002@");

        var result = RunExpectSuccess(Item(
            """{ "op": "createOrUpdateSource", "xref": "@S00002@", "title": "1850 Census, Northvale County" }""",
            """{ "op": "createOrUpdateCitation", "record": "@I1001@", "fact": "BIRT", "citation": { "source": "@S00002@", "page": "p. 1" } }"""));

        Assert.Equal(1, result.Deltas["SOUR"]);
        Assert.Equal(source, RecordText("@S00002@"));
        for (int i = 2; i <= 350; i++)
            Assert.Equal(before[i], RecordText($"@I{1000 + i}@"));
        Assert.Equal(result.CopiedSources["@S00002@"],
            ReadDoc().ByXref["@I1001@"].ChildrenByTag("BIRT").Single().ChildrenByTag("SOUR").Single().Value);
    }
}
