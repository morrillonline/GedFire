using GedCore.Apply;
using GedCore.Ged70;

namespace GedCore.Tests;

/// <summary>
/// Coverage for the path-based ChangesetApplier.Run overload specifically:
/// it must hold one exclusive file handle from the initial read through
/// verified replacement, so a second concurrent path-based apply against
/// the same file cannot mint from the same stale snapshot. ApplyTestBase
/// and every other Apply*Tests suite exercise the byte-array overload
/// exclusively; this file is the only coverage of the path-based one.
/// </summary>
public class ApplyFileLockingTests : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("gedfire-apply-locking-").FullName;

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    static readonly string[] BaseLines =
    [
        "0 HEAD",
        "1 GEDC",
        "2 VERS 7.0",
        "0 @I00001@ INDI",
        "1 NAME Allen /Test/",
        "1 SEX M",
        "0 TRLR",
    ];

    string WriteFile()
    {
        string path = Path.Combine(_dir, "test.ged");
        var document = Ged70Parser.Parse(string.Join("\r\n", BaseLines) + "\r\n");
        using var stream = File.Create(path);
        Ged70Formatter.Write(document, stream);
        return path;
    }

    const string CreateNoteChangeset = """
        { "items": [ { "item": 1, "ops": [
          { "op": "createOrUpdateNote", "record": "@I00001@", "text": "A note." } ] } ] }
        """;

    [Fact]
    public void Run_AppliesAndWritesToTheRealFile()
    {
        string path = WriteFile();
        var changeset = Changeset.Parse(CreateNoteChangeset);

        var result = ChangesetApplier.Run(path, changeset, [1], dryRun: false);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        var doc = Ged70Parser.ReadFile(path);
        Assert.Equal("A note.", doc.ByXref["@I00001@"].ChildrenByTag("NOTE").Single().Value);
    }

    [Fact]
    public void Run_DryRun_LeavesFileByteIdentical()
    {
        string path = WriteFile();
        byte[] before = File.ReadAllBytes(path);
        var changeset = Changeset.Parse(CreateNoteChangeset);

        var result = ChangesetApplier.Run(path, changeset, [1], dryRun: true);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Equal(before, File.ReadAllBytes(path));
    }

    /// <summary>
    /// The core locking guarantee: while another handle already holds the
    /// file exclusively (FileShare.None, exactly as a concurrent apply
    /// invocation would), a second path-based Run cannot even begin --
    /// it fails to open the file rather than reading a possibly-stale
    /// snapshot out from under the first invocation.
    /// </summary>
    [Fact]
    public void Run_WhenFileAlreadyExclusivelyLocked_ThrowsRatherThanReadingAStaleSnapshot()
    {
        string path = WriteFile();
        var changeset = Changeset.Parse(CreateNoteChangeset);

        using var externalLock = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        Assert.ThrowsAny<IOException>(() => ChangesetApplier.Run(path, changeset, [1], dryRun: false));
    }

    /// <summary>
    /// The flip side: once Run has returned (successfully or not), the lock
    /// is released and an ordinary caller can open the file again
    /// immediately -- the exclusivity does not leak past one invocation.
    /// </summary>
    [Fact]
    public void Run_ReleasesTheLockOnReturn()
    {
        string path = WriteFile();
        var changeset = Changeset.Parse(CreateNoteChangeset);

        ChangesetApplier.Run(path, changeset, [1], dryRun: false);

        using var afterwards = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.True(afterwards.CanRead);
    }

    [Fact]
    public void Run_ValidationFailure_ReleasesTheLockWithoutWriting()
    {
        string path = WriteFile();
        byte[] before = File.ReadAllBytes(path);
        var changeset = Changeset.Parse("""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateVital", "record": "@I09999@", "fact": "DEAT",
                "value": { "date": "1900" },
                "citation": { "source": "@S00001@", "page": "x", "dataText": "y", "quay": 1 } } ] } ] }
            """);

        var result = ChangesetApplier.Run(path, changeset, [1], dryRun: false);

        Assert.False(result.Success);
        Assert.Equal(before, File.ReadAllBytes(path));
        // The lock released even though the run failed.
        using var afterwards = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        Assert.True(afterwards.CanRead);
    }

    [Fact]
    public void Run_MintsAndReportsMintedXrefs_ThroughThePathOverload()
    {
        string path = WriteFile();
        var changeset = Changeset.Parse("""
            { "items": [ { "item": 1, "ops": [
              { "op": "createOrUpdateSpouse", "person": "@I00001@",
                "spouse": { "xref": "@NewI1@", "name": "New /Wife/", "sex": "F" },
                "family": "@NewF1@" } ] } ] }
            """);

        var result = ChangesetApplier.Run(path, changeset, [1], dryRun: false);

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.Equal("@I00002@", result.MintedXrefs["@NewI1@"]);
        Assert.Equal("@F00001@", result.MintedXrefs["@NewF1@"]);
        var doc = Ged70Parser.ReadFile(path);
        Assert.True(doc.ByXref.ContainsKey("@I00002@"));
        Assert.True(doc.ByXref.ContainsKey("@F00001@"));
    }
}
