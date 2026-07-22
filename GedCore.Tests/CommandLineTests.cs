using GedFire;

namespace GedCore.Tests;

public class CommandLineTests
{
    static readonly string[] InOut   = ["--input", "--output"];
    static readonly string[] DryRun  = ["--dry-run"];

    [Fact]
    public void ValueOptions_AreParsed()
    {
        var cl = CommandLine.Parse(["--input", "a.ged", "--output", "b.ged"], InOut);
        Assert.Null(cl.Error);
        Assert.Equal("a.ged", cl.Value("--input"));
        Assert.Equal("b.ged", cl.Value("--output"));
    }

    [Fact]
    public void SwitchInLastPosition_IsSeen()
    {
        // Regression: the old per-command loops iterated to args.Length - 1
        // and never saw a flag in the final position.
        var cl = CommandLine.Parse(["--input", "a.ged", "--dry-run"], InOut, DryRun);
        Assert.Null(cl.Error);
        Assert.True(cl.Has("--dry-run"));
    }

    [Fact]
    public void ValueOptionWithoutValue_Fails()
    {
        var cl = CommandLine.Parse(["--input"], InOut);
        Assert.NotNull(cl.Error);
        Assert.Contains("--input", cl.Error);
    }

    [Fact]
    public void ValueOptionFollowedByAnotherOption_Fails()
    {
        // Regression: "--input --output x" used to set input to "--output".
        var cl = CommandLine.Parse(["--input", "--output", "x.ged"], InOut);
        Assert.NotNull(cl.Error);
        Assert.Contains("--input", cl.Error);
    }

    [Fact]
    public void UnknownOption_Fails()
    {
        var cl = CommandLine.Parse(["--bogus"], InOut, DryRun);
        Assert.NotNull(cl.Error);
        Assert.Contains("--bogus", cl.Error);
    }

    [Fact]
    public void RepeatedValueOption_LastWins()
    {
        var cl = CommandLine.Parse(["--input", "a", "--input", "b"], InOut);
        Assert.Null(cl.Error);
        Assert.Equal("b", cl.Value("--input"));
    }

    [Fact]
    public void MissingOption_ReturnsNullValueAndFalseSwitch()
    {
        var cl = CommandLine.Parse([], InOut, DryRun);
        Assert.Null(cl.Error);
        Assert.Null(cl.Value("--input"));
        Assert.False(cl.Has("--dry-run"));
    }
}
