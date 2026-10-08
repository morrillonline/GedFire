using GedFire.Gen;
using GedFire.Match;

namespace GedCore.Tests;

public class SourceFinderTests
{
    static GedModel Model()
    {
        var model = new GedModel();
        model.Sources["@S1@"] = new GedSourceRecord { Xref = "@S1@", Title = "Burial Register", Author = "Harwick Parish", NoteRaw = "Register, online at https://archive.example.org/b." };
        model.Sources["@S2@"] = new GedSourceRecord { Xref = "@S2@", Title = "Burial Register", Publication = "Posted at https://other.example.org/b" };
        model.Sources["@S3@"] = new GedSourceRecord { Xref = "@S3@", Author = "Ann Fenwick" };
        return model;
    }

    static string[] Xrefs(SourceQuery query) => [.. SourceFinder.Find(Model(), query).Select(s => s.Xref)];

    [Fact]
    public void Title_FoldsCaseAndMatchesSubstrings() =>
        Assert.Equal(["@S1@", "@S2@"], Xrefs(new SourceQuery(Title: "REGISTER")));

    [Fact]
    public void SourceWithoutTheFieldNeverMatchesACriterionOnIt() =>
        Assert.DoesNotContain("@S3@", Xrefs(new SourceQuery(Title: "a")));

    [Fact]
    public void Url_ComesFromTheNoteThenThePublication()
    {
        Assert.Equal(["@S1@"], Xrefs(new SourceQuery(Url: "archive.example.org")));
        Assert.Equal(["@S2@"], Xrefs(new SourceQuery(Url: "other.example.org")));
    }

    [Fact]
    public void Criteria_AreCombinedWithAnd() =>
        Assert.Equal(["@S1@"], Xrefs(new SourceQuery(Title: "register", Author: "parish")));

    [Fact]
    public void Result_ReportsNullForMissingFields()
    {
        var fenwick = Assert.Single(SourceFinder.Find(Model(), new SourceQuery(Author: "fenwick")));

        Assert.Null(fenwick.Title);
        Assert.Null(fenwick.Url);
        Assert.Equal("Ann Fenwick", fenwick.Author);
    }

    [Fact]
    public void Matches_AreOrderedByTitleThenXref() =>
        Assert.Equal(["@S3@", "@S1@", "@S2@"], Xrefs(new SourceQuery()));
}
