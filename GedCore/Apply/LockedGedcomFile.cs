using System.Security.Cryptography;
using System.Text;

namespace GedCore.Apply;

/// <summary>
/// One writer's hold on a GEDCOM file. Every GedFire writer on the machine takes the same named system lock for
/// the file, so they run one at a time, each reading what the one before it wrote. The replacement is written to
/// a temporary file and renamed over the GEDCOM, so the GEDCOM is always either the complete old file or the
/// complete new one. The lock belongs to the thread that opened it: open and dispose on the same thread.
/// </summary>
public sealed class LockedGedcomFile : IDisposable
{
    public static readonly TimeSpan DefaultLockWait = TimeSpan.FromSeconds(10);

    static readonly TimeSpan SwapWait = TimeSpan.FromSeconds(5);
    static readonly TimeSpan RetryInterval = TimeSpan.FromMilliseconds(20);

    readonly string _path;
    readonly Mutex _lock;
    byte[]? _read;

    LockedGedcomFile(string path, Mutex writeLock)
    {
        _path = path;
        _lock = writeLock;
    }

    /// <summary>
    /// Takes the write lock on <paramref name="gedcomPath"/>, waiting up to <paramref name="lockWait"/> for another
    /// writer to finish. Throws <see cref="IOException"/> when the wait runs out.
    /// </summary>
    public static LockedGedcomFile Open(string gedcomPath, TimeSpan lockWait, CancellationToken cancellationToken = default)
    {
        string path = RealPath(gedcomPath);
        if (!File.Exists(path)) throw new FileNotFoundException($"GEDCOM file not found: {path}", path);
        return new LockedGedcomFile(path, TakeLock(path, lockWait, cancellationToken));
    }

    public static LockedGedcomFile Open(string gedcomPath, CancellationToken cancellationToken = default) =>
        Open(gedcomPath, DefaultLockWait, cancellationToken);

    /// <summary>The GEDCOM as it stands while the lock is held; <see cref="Replace"/> refuses if it has changed since.</summary>
    public byte[] ReadAllBytes() => _read = GedReader.ReadAllBytes(_path);

    /// <summary>
    /// Replaces the GEDCOM with <paramref name="bytes"/> in one step. Throws <see cref="IOException"/> or
    /// <see cref="UnauthorizedAccessException"/> when it cannot; the GEDCOM is then exactly as it was.
    /// </summary>
    public void Replace(byte[] bytes)
    {
        string temporary = _path + ".tmp";
        try
        {
            WriteVerified(temporary, bytes);
            RefuseIfChangedSinceRead();
            Swap(temporary);
        }
        catch
        {
            TryDelete(temporary);
            throw;
        }
    }

    public void Dispose()
    {
        _lock.ReleaseMutex();
        _lock.Dispose();
    }

    // A link is followed so the file it names is replaced, not the link itself.
    static string RealPath(string gedcomPath)
    {
        string full = Path.GetFullPath(gedcomPath);
        return File.Exists(full) && File.ResolveLinkTarget(full, returnFinalTarget: true) is { } target ? target.FullName : full;
    }

    static Mutex TakeLock(string path, TimeSpan lockWait, CancellationToken cancellationToken)
    {
        var mutex = new Mutex(initiallyOwned: false, LockName(path));
        try
        {
            if (Acquire(mutex, lockWait, cancellationToken)) return mutex;
        }
        catch
        {
            mutex.Dispose();
            throw;
        }

        mutex.Dispose();
        throw new IOException(
            $"another GedFire writer held the write lock on '{path}' for more than {lockWait.TotalSeconds:0.#} s; " +
            "nothing was written. Try again.");
    }

    static bool Acquire(Mutex mutex, TimeSpan lockWait, CancellationToken cancellationToken)
    {
        DateTime deadline = DateTime.UtcNow + lockWait;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (mutex.WaitOne(RetryInterval)) return true;
            }
            catch (AbandonedMutexException)
            {
                // A writer died holding the lock. It is ours now, and the file is whole: a write lands in one step or not at all.
                return true;
            }
            if (DateTime.UtcNow >= deadline) return false;
        }
    }

    // One name per file however the path is spelled; Windows paths differ only by case.
    static string LockName(string path)
    {
        string key = OperatingSystem.IsWindows() ? path.ToUpperInvariant() : path;
        return @"Global\GedFire.write." + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)));
    }

    static void WriteVerified(string temporary, byte[] bytes)
    {
        using var stream = new FileStream(temporary, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        stream.Write(bytes);
        stream.Flush(flushToDisk: true);

        stream.Position = 0;
        byte[] written = new byte[bytes.Length];
        stream.ReadExactly(written);
        if (stream.Length != bytes.Length || !written.AsSpan().SequenceEqual(bytes))
            throw new IOException($"re-read of '{temporary}' does not match the bytes written; nothing was written to the GEDCOM.");
    }

    // The lock only binds GedFire writers; another program may have saved the file since it was read.
    void RefuseIfChangedSinceRead()
    {
        if (_read is not null && !GedReader.ReadAllBytes(_path).AsSpan().SequenceEqual(_read))
            throw new IOException($"'{_path}' was changed by another program during the write; nothing was written. Try again.");
    }

    // A rename over the GEDCOM never leaves its name absent. While any program has the GEDCOM open, Windows
    // refuses the rename, reporting it as an access failure, so both kinds are retried before giving up.
    void Swap(string temporary)
    {
        DateTime deadline = DateTime.UtcNow + SwapWait;
        while (true)
        {
            try
            {
                File.Move(temporary, _path, overwrite: true);
                return;
            }
            catch (Exception ex) when (IsBusy(ex) && DateTime.UtcNow < deadline)
            {
                Thread.Sleep(RetryInterval);
            }
            catch (Exception ex) when (IsBusy(ex))
            {
                throw new IOException(
                    $"'{_path}' could not be replaced within {SwapWait.TotalSeconds:0.#} s: it is open in another program, " +
                    $"or cannot be written ({ex.Message}). Nothing was written.", ex);
            }
        }
    }

    static bool IsBusy(Exception ex) =>
        ex is UnauthorizedAccessException || ex is IOException io && IsHeldElsewhere(io);

    static bool IsHeldElsewhere(IOException ex) =>
        ex is not (FileNotFoundException or DirectoryNotFoundException or PathTooLongException or DriveNotFoundException);

    static void TryDelete(string path)
    {
        try { File.Delete(path); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
