namespace GedCore.Apply;

/// <summary>
/// Conversion between the logical (filesystem-style) path a changeset author
/// writes and the percent-escaped form GEDCOM 7 requires for a relative
/// <c>FILE</c> payload (a "File Reference" is a URI reference; characters
/// outside the unreserved URI set must be percent-encoded). Escaping is
/// segment-wise so the '/' path delimiter itself is never encoded. Absolute
/// URLs (<c>http://</c>/<c>https://</c>) pass through unchanged in both
/// directions — GEDCOM 7 treats an already-valid URI as-is.
///
/// Shared by the Media changeset op (<c>MediaOps.cs</c>) and GEDZIP
/// read/write (Subproject K), which converts between archive paths (never
/// escaped) and <c>FILE</c> payloads (always escaped) at the same boundary.
/// </summary>
public static class MediaPaths
{
    public static bool IsAbsoluteUrl(string path) =>
        path.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith("https://", StringComparison.OrdinalIgnoreCase);

    /// <summary>Logical path → GEDCOM <c>FILE</c> payload.</summary>
    public static string EscapeFilePath(string path) =>
        IsAbsoluteUrl(path)
            ? path
            : string.Join('/', path.Split('/').Select(Uri.EscapeDataString));

    /// <summary>GEDCOM <c>FILE</c> payload → logical (filesystem) path.</summary>
    public static string UnescapeFilePath(string path) =>
        IsAbsoluteUrl(path)
            ? path
            : string.Join('/', path.Split('/').Select(Uri.UnescapeDataString));
}
