using GedFire.Mcp;
using GedFire.Cli;

namespace GedCore.Tests;

public class ToolContextTests
{
    static DocumentSession Session() => new("synthetic.ged",
        new DocumentSnapshot(MatchTestModels.Build("0 @I1@ INDI\n1 NAME Mara /Example/\n"), "7.0", DateTime.UtcNow, 0));

    [Fact]
    public void ContextKeepsTheBoundSessionAndDerivesTheMediaDirectory()
    {
        var session = Session();

        var context = new ToolContext(session, "synthetic.ged");

        Assert.Same(session, context.Session);
        Assert.Equal("synthetic.ged", context.DocumentPath);
        Assert.Equal(MediaDirectory.For("synthetic.ged"), context.MediaDirectory);
        Assert.NotNull(context.Gate);
        Assert.NotNull(context.Nicknames);
    }

    [Fact]
    public void ContextRequiresABoundSession() =>
        Assert.Throws<ArgumentNullException>(() => new ToolContext(null!, "synthetic.ged"));

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void ContextRequiresADocumentPath(string path) =>
        Assert.Throws<ArgumentException>(() => new ToolContext(Session(), path));
}