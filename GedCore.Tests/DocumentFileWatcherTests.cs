using GedCore;
using GedFire.Gen;
using GedFire.Mcp;

namespace GedCore.Tests;

// ---------------------------------------------------------------------------
// Coverage for DocumentFileWatcher: proactive reload on an on-disk change,
// debouncing a burst of rapid-fire events into one reload, and clean
// shutdown. Timing-based by nature (a real FileSystemWatcher + a real
// debounce delay), so waits poll with a generous timeout rather than
// asserting on a fixed clock.
// ---------------------------------------------------------------------------

public class DocumentFileWatcherTests : IDisposable
{
    static readonly TimeSpan ReloadTimeout = TimeSpan.FromSeconds(5);

    readonly string _dir = Directory.CreateTempSubdirectory("gedfire-filewatcher-tests-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    const string OnePersonGed = """
        0 HEAD
        1 GEDC
        2 VERS 7.0
        0 @I1@ INDI
        1 NAME Frederick /Morrill/
        1 SEX M
        0 TRLR

        """;

    const string TwoPersonGed = """
        0 HEAD
        1 GEDC
        2 VERS 7.0
        0 @I1@ INDI
        1 NAME Frederick /Morrill/
        1 SEX M
        0 @I2@ INDI
        1 NAME Sarah /Blake/
        1 SEX F
        0 TRLR

        """;

    (DocumentSession Session, string Path) NewSession(string gedText)
    {
        string path = Path.Combine(_dir, "family.ged");
        File.WriteAllText(path, gedText);
        var doc = GedReader.ReadFile(path);
        var model = ModelBuilder.Build(doc);
        var info = new FileInfo(path);
        var snapshot = new DocumentSnapshot(model, doc.Version, File.GetLastWriteTimeUtc(path), info.Length);
        return (new DocumentSession(path, snapshot), path);
    }

    static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException("Condition was not met within the timeout.");
            await Task.Delay(25);
        }
    }

    [Fact]
    public async Task FileChangedOnDisk_TriggersAProactiveReload()
    {
        var (session, path) = NewSession(OnePersonGed);
        await using var watcher = new DocumentFileWatcher(session, path);
        await Task.Delay(50); // let the watcher finish arming

        File.WriteAllText(path, TwoPersonGed);

        await WaitUntilAsync(() => watcher.ReloadCount >= 1, ReloadTimeout);
        var snapshot = await session.GetSnapshotAsync(CancellationToken.None);
        Assert.Equal(2, snapshot.Model.Individuals.Count);
    }

    [Fact]
    public async Task BurstOfRapidWrites_DebouncesIntoOneReload()
    {
        var (session, path) = NewSession(OnePersonGed);
        await using var watcher = new DocumentFileWatcher(session, path);
        await Task.Delay(50);

        for (int i = 0; i < 5; i++)
        {
            File.WriteAllText(path, TwoPersonGed);
            await Task.Delay(20); // well inside the watcher's quiet period
        }

        await WaitUntilAsync(() => watcher.ReloadCount >= 1, ReloadTimeout);
        await Task.Delay(500); // let any over-eager extra reload show up

        Assert.Equal(1, watcher.ReloadCount);
    }

    [Fact]
    public async Task DisposeAsync_StopsWatching_NoFurtherReloads()
    {
        var (session, path) = NewSession(OnePersonGed);
        var watcher = new DocumentFileWatcher(session, path);
        await Task.Delay(50);
        await watcher.DisposeAsync();

        File.WriteAllText(path, TwoPersonGed);
        await Task.Delay(700); // longer than the quiet period; nothing should fire

        Assert.Equal(0, watcher.ReloadCount);
    }
}
