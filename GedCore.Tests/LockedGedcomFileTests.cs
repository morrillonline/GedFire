using System.Text;
using GedCore.Apply;

namespace GedCore.Tests;

public class LockedGedcomFileTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("gedfire-locked-file-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    static byte[] Bytes(string text) => Encoding.UTF8.GetBytes(text);

    string WriteGedcom(string content = "old")
    {
        string path = Path.Combine(_dir, "family.ged");
        File.WriteAllText(path, content);
        return path;
    }

    // The lock belongs to a thread, so a competing writer is one on its own thread.
    sealed class OtherWriter : IDisposable
    {
        readonly ManualResetEventSlim _holding = new();
        readonly ManualResetEventSlim _finish = new();
        readonly Thread _thread;

        public OtherWriter(string path, byte[]? writes = null)
        {
            _thread = new Thread(() =>
            {
                using var file = LockedGedcomFile.Open(path);
                _holding.Set();
                _finish.Wait();
                if (writes is not null) file.Replace(writes);
            });
            _thread.Start();
            _holding.Wait();
        }

        public void Dispose()
        {
            _finish.Set();
            _thread.Join();
        }
    }

    [Fact]
    public void Replace_SwapsInTheNewContentAndLeavesNothingBesideTheGedcom()
    {
        string path = WriteGedcom();

        using (var file = LockedGedcomFile.Open(path))
        {
            Assert.Equal(Bytes("old"), file.ReadAllBytes());
            file.Replace(Bytes("new"));
        }

        Assert.Equal("new", File.ReadAllText(path));
        Assert.Equal([path], Directory.GetFiles(_dir));
    }

    [Fact]
    public void Open_WhileAnotherWriterHoldsTheLock_FailsAfterTheWaitWithTheFileUntouched()
    {
        string path = WriteGedcom();
        using var other = new OtherWriter(path);

        var ex = Assert.ThrowsAny<IOException>(() => LockedGedcomFile.Open(path, TimeSpan.FromMilliseconds(150)));

        Assert.Contains("write lock", ex.Message);
        Assert.Equal("old", File.ReadAllText(path));
    }

    [Fact]
    public void Open_WaitsForTheOtherWriterAndThenReadsWhatItWrote()
    {
        string path = WriteGedcom();
        var other = new OtherWriter(path, Bytes("written by the first writer"));
        var finishing = Task.Run(() =>
        {
            Thread.Sleep(200);
            other.Dispose();
        });

        using var second = LockedGedcomFile.Open(path, TimeSpan.FromSeconds(30));

        Assert.Equal(Bytes("written by the first writer"), second.ReadAllBytes());
        finishing.Wait();
    }

    [Fact]
    public void Replace_ThatFailsPartWay_LeavesTheGedcomUntouchedAndReleasesTheLock()
    {
        string path = WriteGedcom();
        // A folder where the temporary file belongs makes the write fail before the swap.
        Directory.CreateDirectory(path + ".tmp");

        using (var file = LockedGedcomFile.Open(path))
        {
            var ex = Record.Exception(() => file.Replace(Bytes("new")));
            Assert.True(ex is IOException or UnauthorizedAccessException, ex?.ToString());
        }

        Assert.Equal("old", File.ReadAllText(path));
        using var again = LockedGedcomFile.Open(path, TimeSpan.Zero);
        Assert.Equal(Bytes("old"), again.ReadAllBytes());
    }

    [Fact]
    public void Replace_AfterAnotherProgramChangedTheFile_IsRefusedAndKeepsThatProgramsContent()
    {
        string path = WriteGedcom();
        using var file = LockedGedcomFile.Open(path);
        file.ReadAllBytes();
        File.WriteAllText(path, "saved by an editor meanwhile");

        var ex = Assert.ThrowsAny<IOException>(() => file.Replace(Bytes("new")));

        Assert.Contains("changed by another program", ex.Message);
        Assert.Equal("saved by an editor meanwhile", File.ReadAllText(path));
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void Replace_OverwritesATemporaryFileLeftByAnInterruptedWrite()
    {
        string path = WriteGedcom();
        File.WriteAllText(path + ".tmp", "half-written leftovers that are longer than the new content");

        using (var file = LockedGedcomFile.Open(path))
            file.Replace(Bytes("new"));

        Assert.Equal("new", File.ReadAllText(path));
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void Open_ThroughALink_ReplacesTheFileTheLinkNames()
    {
        string path = WriteGedcom();
        string link = Path.Combine(_dir, "link.ged");
        try
        {
            File.CreateSymbolicLink(link, path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return; // Windows without the privilege to create links: nothing to check here.
        }

        using (var file = LockedGedcomFile.Open(link))
            file.Replace(Bytes("new"));

        Assert.Equal("new", File.ReadAllText(path));
        Assert.NotNull(new FileInfo(link).LinkTarget);
    }
}
