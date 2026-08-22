using GedFire.Mcp;

namespace GedCore.Tests;

public class DocumentSnapshotTests
{
    const string OnePersonGed = """
        0 @I1@ INDI
        1 NAME Frederick /Morrill/
        1 SEX M
        """;

    [Fact]
    public void Constructor_StoresModelAndMetadata()
    {
        var model = MatchTestModels.Build(OnePersonGed);
        var stamp = new DateTime(2024, 1, 2, 3, 4, 5, DateTimeKind.Utc);

        var snapshot = new DocumentSnapshot(model, "7.0", stamp, 12345);

        Assert.Same(model, snapshot.Model);
        Assert.Equal("7.0", snapshot.GedVersion);
        Assert.Equal(stamp, snapshot.LastWriteTimeUtc);
        Assert.Equal(12345, snapshot.Length);
    }

    [Fact]
    public void Constructor_BuildsMatchIndexFromTheModel()
    {
        var model = MatchTestModels.Build(OnePersonGed);

        var snapshot = new DocumentSnapshot(model, "7.0", DateTime.UtcNow, 1);

        Assert.NotNull(snapshot.MatchIndex);
        Assert.Single(snapshot.MatchIndex.Entries);
        Assert.Equal("@I1@", snapshot.MatchIndex.Entries[0].Individual.Xref);
    }

    [Fact]
    public void Constructor_AllowsNullGedVersion()
    {
        var model = MatchTestModels.Build(OnePersonGed);
        var snapshot = new DocumentSnapshot(model, null, DateTime.UtcNow, 1);
        Assert.Null(snapshot.GedVersion);
    }

    [Fact]
    public void Constructor_RejectsNullModel()
        => Assert.Throws<ArgumentNullException>(() => new DocumentSnapshot(null!, "7.0", DateTime.UtcNow, 0));
}
