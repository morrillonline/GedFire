using GedFire.Mcp;

namespace GedCore.Tests;

// DocumentSession's whole job is watching a real file's OS-level metadata,
// so — unlike the rest of this suite — these tests use real temporary files
// rather than pure in-memory units.
public class DocumentSessionTests : IDisposable
{
    readonly string _dir;

    public DocumentSessionTests()
    {
        _dir = Directory.CreateTempSubdirectory("gedfire-mcp-tests-").FullName;
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    string WriteGed(string name, string personName, DateTime? stampUtc = null)
    {
        string path = Path.Combine(_dir, name);
        File.WriteAllText(path, $"""
            0 @I1@ INDI
            1 NAME {personName} /Test/
            1 SEX M

            """);
        if (stampUtc is { } stamp)
            File.SetLastWriteTimeUtc(path, stamp);
        return path;
    }

    static DocumentSnapshot InitialSnapshot(string path)
    {
        var info = new FileInfo(path);
        var model = MatchTestModels.Build(File.ReadAllText(path));
        return new DocumentSnapshot(model, "7.0", File.GetLastWriteTimeUtc(path), info.Length);
    }

    static string GivenNameOf(DocumentSnapshot snapshot) =>
        snapshot.MatchIndex.Entries.Single().NormalizedGiven;

    [Fact]
    public async Task GetSnapshotAsync_ReturnsTheInitialSnapshot_WhenFileUnchanged()
    {
        string path = WriteGed("a.ged", "Alpha");
        var initial = InitialSnapshot(path);
        var session = new DocumentSession(path, initial);

        var snapshot = await session.GetSnapshotAsync(CancellationToken.None);

        Assert.Same(initial, snapshot);
    }

    [Fact]
    public async Task GetSnapshotAsync_ReloadsWhenLengthChanges()
    {
        string path = WriteGed("a.ged", "Alpha");
        var initial = InitialSnapshot(path);
        var session = new DocumentSession(path, initial);

        // A different length guarantees staleness regardless of mtime
        // resolution, even on a filesystem with coarse timestamp granularity.
        File.WriteAllText(path, """
            0 @I1@ INDI
            1 NAME Bravo /Test/
            1 SEX M

            """);
        File.SetLastWriteTimeUtc(path, initial.LastWriteTimeUtc.AddSeconds(5));

        var snapshot = await session.GetSnapshotAsync(CancellationToken.None);

        Assert.NotSame(initial, snapshot);
        Assert.Equal("BRAVO", GivenNameOf(snapshot));
    }

    [Fact]
    public async Task GetSnapshotAsync_ReloadsWhenLastWriteTimeChanges_EvenAtSameLength()
    {
        string path = WriteGed("a.ged", "Alph"); // same length as "Beta"
        var initial = InitialSnapshot(path);
        var session = new DocumentSession(path, initial);

        File.WriteAllText(path, """
            0 @I1@ INDI
            1 NAME Beta /Test/
            1 SEX M

            """);
        File.SetLastWriteTimeUtc(path, initial.LastWriteTimeUtc.AddSeconds(5));

        var snapshot = await session.GetSnapshotAsync(CancellationToken.None);

        Assert.NotSame(initial, snapshot);
        Assert.Equal("BETA", GivenNameOf(snapshot));
    }

    [Fact]
    public async Task GetSnapshotAsync_SubsequentCallsShareTheReloadedSnapshot()
    {
        string path = WriteGed("a.ged", "Alpha");
        var initial = InitialSnapshot(path);
        var session = new DocumentSession(path, initial);

        File.WriteAllText(path, """
            0 @I1@ INDI
            1 NAME Bravo /Test/
            1 SEX M

            """);
        File.SetLastWriteTimeUtc(path, initial.LastWriteTimeUtc.AddSeconds(5));

        var first = await session.GetSnapshotAsync(CancellationToken.None);
        var second = await session.GetSnapshotAsync(CancellationToken.None);

        Assert.Same(first, second);
    }

    [Fact]
    public async Task GetSnapshotAsync_MissingFile_ThrowsActionableError()
    {
        string path = Path.Combine(_dir, "missing.ged");
        var initial = new DocumentSnapshot(MatchTestModels.Build("0 @I1@ INDI\n1 NAME X /Y/\n"), "7.0", DateTime.UtcNow, 1);
        var session = new DocumentSession(path, initial);

        var ex = await Assert.ThrowsAsync<DocumentReloadException>(() => session.GetSnapshotAsync(CancellationToken.None));
        Assert.Contains(path, ex.Message);
    }

    [Fact]
    public async Task GetSnapshotAsync_UnparsableReplacement_ThrowsActionableError()
    {
        string path = WriteGed("a.ged", "Alpha");
        var initial = InitialSnapshot(path);
        var session = new DocumentSession(path, initial);

        // Not valid GEDCOM at all: no HEAD/INDI/FAM records the parser can
        // make sense of as level-0 structure.
        File.WriteAllText(path, "this is not a gedcom file at all\n\x00\x01 garbage");
        File.SetLastWriteTimeUtc(path, initial.LastWriteTimeUtc.AddSeconds(5));

        var ex = await Assert.ThrowsAsync<DocumentReloadException>(() => session.GetSnapshotAsync(CancellationToken.None));
        Assert.Contains(path, ex.Message);
    }

    [Fact]
    public void Constructor_RejectsNullInitialSnapshot()
        => Assert.Throws<ArgumentNullException>(() => new DocumentSession("x.ged", null!));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Constructor_RejectsBlankPath(string? path)
    {
        var snapshot = new DocumentSnapshot(MatchTestModels.Build("0 @I1@ INDI\n1 NAME X /Y/\n"), "7.0", DateTime.UtcNow, 1);
        Assert.Throws<ArgumentException>(() => new DocumentSession(path!, snapshot));
    }

    [Fact]
    public async Task GetSnapshotAsync_ConcurrentCallsNeverObserveAPartialModel()
    {
        string path = WriteGed("a.ged", "Alpha");
        var initial = InitialSnapshot(path);
        var session = new DocumentSession(path, initial);

        File.WriteAllText(path, """
            0 @I1@ INDI
            1 NAME Bravo /Test/
            1 SEX M

            """);
        File.SetLastWriteTimeUtc(path, initial.LastWriteTimeUtc.AddSeconds(5));

        var results = await Task.WhenAll(Enumerable.Range(0, 8)
            .Select(_ => session.GetSnapshotAsync(CancellationToken.None)));

        // Every concurrent caller sees a fully-built, consistent snapshot —
        // and since a reload is one atomic swap, they all see the same one.
        Assert.All(results, r => Assert.Equal("BRAVO", GivenNameOf(r)));
        Assert.All(results, r => Assert.Same(results[0], r));
    }
}
