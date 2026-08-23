using GedFire.Gen;
using GedFire.Match;

namespace GedFire.Mcp;

// ---------------------------------------------------------------------------
// A construct-once carrier for one resident-document snapshot: the
// GedModel, the MatchIndex built from it, the document's declared GEDCOM
// version, and the source file's UTC last-write time and byte length used
// for the staleness check. No behavior beyond construction — DocumentSession
// owns the reload policy.
// ---------------------------------------------------------------------------

public sealed class DocumentSnapshot
{
    public GedModel Model { get; }
    public MatchIndex MatchIndex { get; }
    public string? GedVersion { get; }
    public DateTime LastWriteTimeUtc { get; }
    public long Length { get; }

    public DocumentSnapshot(GedModel model, string? gedVersion, DateTime lastWriteTimeUtc, long length)
    {
        Model = model ?? throw new ArgumentNullException(nameof(model));
        MatchIndex = new MatchIndex(model);
        GedVersion = gedVersion;
        LastWriteTimeUtc = lastWriteTimeUtc;
        Length = length;
    }
}
