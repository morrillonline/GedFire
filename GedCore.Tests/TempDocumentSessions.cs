using GedFire.Mcp;

namespace GedCore.Tests;

/// <summary>
/// Test support, not a class under test: builds a DocumentSession over a GEDCOM
/// written to a temp directory, because DocumentSession stats its source path.
/// </summary>
internal sealed class TempDocumentSessions : IDisposable
{
    readonly string _dir = Directory.CreateTempSubdirectory("gedfire-session-tests-").FullName;

    public DocumentSession Open(string gedText)
    {
        string path = Path.Combine(_dir, Guid.NewGuid() + ".ged");
        File.WriteAllText(path, gedText);
        var doc = GedReader.ReadFile(path);
        var model = GedFire.Gen.ModelBuilder.Build(doc);
        var info = new FileInfo(path);
        var snapshot = new DocumentSnapshot(model, doc.Version, File.GetLastWriteTimeUtc(path), info.Length);
        return new DocumentSession(path, snapshot);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }
}
