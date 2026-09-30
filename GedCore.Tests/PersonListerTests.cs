using GedFire.Match;

namespace GedCore.Tests;

public class PersonListerTests
{
    const string Ged = """
        0 HEAD
        1 GEDC
        2 VERS 5.5.1
        0 @I3@ INDI
        1 NAME Beatrice /Fenwick/
        1 BIRT
        2 DATE 12 MAR 1745
        0 @I2@ INDI
        1 NAME Cornelius /Ashworth/
        1 BIRT
        2 DATE 1741
        1 DEAT
        2 DATE ABT 1809
        0 @I1@ INDI
        1 NAME Alfred /Ashworth/
        0 @I4@ INDI
        1 NAME Dorothea /Ashwirth/
        0 @I5@ INDI
        1 NAME Edmund /Fenwick/
        """;

    static MatchIndex Index() => new(MatchTestModels.Build(Ged));

    static PersonListPage List(MatchIndex index, string[]? surnames = null, string? cursor = null, int pageSize = 100)
    {
        Assert.True(PersonLister.TryList(index, surnames, cursor, pageSize, out var page, out var error), error);
        return page;
    }

    [Fact]
    public void List_NoFilter_ReturnsEveryoneOrderedBySurnameGivenXref()
    {
        var page = List(Index());

        Assert.Equal(["@I4@", "@I1@", "@I2@", "@I3@", "@I5@"], page.People.Select(p => p.Xref));
        Assert.Equal(5, page.TotalMatches);
        Assert.Null(page.NextCursor);
    }

    [Fact]
    public void List_ReturnsNameSurnameAndYears()
    {
        var page = List(Index());
        var cornelius = page.People.Single(p => p.Xref == "@I2@");

        Assert.Equal("Cornelius Ashworth", cornelius.Name);
        Assert.Equal("Ashworth", cornelius.Surname);
        Assert.Equal(1741, cornelius.BirthYear);
        Assert.Equal(1809, cornelius.DeathYear);
        Assert.Null(page.People.Single(p => p.Xref == "@I1@").BirthYear);
    }

    [Fact]
    public void List_SurnameFilter_ReturnsOnlyListedVariantsIgnoringCase()
    {
        var page = List(Index(), ["ashworth", "ASHWIRTH"]);

        Assert.Equal(["@I4@", "@I1@", "@I2@"], page.People.Select(p => p.Xref));
        Assert.Equal(3, page.TotalMatches);
    }

    [Fact]
    public void List_SurnameFilterWithoutMatch_IsEmpty()
    {
        var page = List(Index(), ["Nobody"]);

        Assert.Empty(page.People);
        Assert.Equal(0, page.TotalMatches);
        Assert.Null(page.NextCursor);
    }

    [Fact]
    public void List_Paging_CoversEveryoneOnceAndEndsWithNullCursor()
    {
        var index = Index();
        var seen = new List<string>();
        string? cursor = null;
        int pages = 0;
        do
        {
            var page = List(index, cursor: cursor, pageSize: 2);
            seen.AddRange(page.People.Select(p => p.Xref));
            Assert.Equal(5, page.TotalMatches);
            cursor = page.NextCursor;
            pages++;
        } while (cursor is not null);

        Assert.Equal(3, pages);
        Assert.Equal(["@I4@", "@I1@", "@I2@", "@I3@", "@I5@"], seen);
    }

    [Fact]
    public void List_ExactFitLastPage_HasNoNextCursor()
    {
        var page = List(Index(), pageSize: 5);

        Assert.Equal(5, page.People.Count);
        Assert.Null(page.NextCursor);
    }

    [Fact]
    public void List_SameInputTwice_ReturnsIdenticalPages()
    {
        var index = Index();

        Assert.Equal(
            List(index, pageSize: 2).People.Select(p => p.Xref),
            List(index, pageSize: 2).People.Select(p => p.Xref));
    }

    [Fact]
    public void List_CursorNotFromAPreviousCall_IsRejectedWithGuidance()
    {
        bool ok = PersonLister.TryList(Index(), null, "not-a-cursor!", 10, out _, out var error);

        Assert.False(ok);
        Assert.Contains("cursor is not a value returned by a previous list_people call", error);
    }
}
