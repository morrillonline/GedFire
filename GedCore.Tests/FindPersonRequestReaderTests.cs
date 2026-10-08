using GedFire;
using GedFire.Cli;

namespace GedCore.Tests;

public class FindPersonRequestReaderTests
{
    static CommandLine Parse(params string[] args) =>
        CommandLine.Parse(["--query", "Mary Wood", .. args], FindPersonRequestReader.Options);

    [Fact]
    public void QueryAlone_HasNoHintsAndTheDefaultMaximum()
    {
        Assert.True(FindPersonRequestReader.TryRead(Parse(), out var request, out _));

        Assert.Equal("Mary Wood", request!.Query);
        Assert.Null(request.Hints);
        Assert.Equal(8, request.MaxResults);
    }

    [Fact]
    public void SuppliedFlags_BecomeTheirHintGroups()
    {
        Assert.True(FindPersonRequestReader.TryRead(Parse(
            "--max-results", "3", "--sex", "f", "--birth-year", "1741", "--birth-place", "Harwick",
            "--father", "Levi Ashworth", "--spouse-name", "John Fenwick", "--marriage-year", "1770"),
            out var request, out _));

        var hints = request!.Hints!;
        Assert.Equal(3, request.MaxResults);
        Assert.Equal("F", hints.Sex);
        Assert.Equal(1741, hints.Birth!.Year);
        Assert.Equal("Harwick", hints.Birth.Place);
        Assert.Null(hints.Death);
        Assert.Equal("Levi Ashworth", hints.Parents!.Father);
        Assert.Null(hints.Parents.Mother);
        Assert.Equal("John Fenwick", hints.Spouse!.Name);
        Assert.Equal(1770, hints.Spouse.Marriage!.Year);
    }

    [Fact]
    public void AMarriageWithoutASpouseName_StillFormsTheSpouseHint()
    {
        Assert.True(FindPersonRequestReader.TryRead(Parse("--marriage-place", "Harwick"), out var request, out _));

        Assert.Null(request!.Hints!.Spouse!.Name);
        Assert.Equal("Harwick", request.Hints.Spouse.Marriage!.Place);
    }

    [Theory]
    [InlineData("--max-results", "many", "--max-results must be an integer, got: many")]
    [InlineData("--birth-year", "x", "--birth-year must be an integer, got: x")]
    [InlineData("--death-year", "1.5", "--death-year must be an integer, got: 1.5")]
    [InlineData("--marriage-year", "-", "--marriage-year must be an integer, got: -")]
    [InlineData("--sex", "X", "--sex must be M or F, got: X")]
    public void AnInvalidValue_IsReportedByFlag(string flag, string value, string expected)
    {
        Assert.False(FindPersonRequestReader.TryRead(Parse(flag, value), out var request, out string? error));

        Assert.Null(request);
        Assert.Equal(expected, error);
    }
}
