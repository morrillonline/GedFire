namespace GedFire.Gen;

/// <summary>
/// Where to find media files on disk and how to reference them in generated
/// HTML. <see cref="MediaDir"/> is null when the generator was never told
/// where the files live (relative media is then treated as missing — see
/// <c>SiteGenerator.ResolveMediaSrc</c>); the CLI's <c>generate</c> verb
/// always supplies one (explicit <c>--media-dir</c>, defaulting to the GED
/// file's own directory).
/// </summary>
public sealed record MediaOptions(string? MediaDir, string MediaBaseUrl)
{
    public static readonly MediaOptions None = new(null, "media/");
}

/// <summary>
/// Copies referenced media files from <c>--media-dir</c> into the generated
/// site's own <c>media/</c> folder, preserving each payload's relative path.
/// Only files the generator actually rendered are copied — the caller
/// supplies the exact set (<see cref="SiteGenerator.Warnings"/> already
/// vetted each one for existence and for escaping <c>mediaDir</c>); anything
/// else under <c>--media-dir</c> is left untouched.
/// </summary>
public static class MediaStager
{
    public static void Stage(string mediaDir, string outputDir,
                              IReadOnlyCollection<string> relativePaths)
    {
        if (relativePaths.Count == 0) return;

        string mediaRoot = Path.GetFullPath(mediaDir);
        foreach (var rel in relativePaths)
        {
            string relFs = rel.Replace('/', Path.DirectorySeparatorChar);
            string src   = Path.Combine(mediaRoot, relFs);
            string dest  = Path.Combine(outputDir, "media", relFs);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(src, dest, overwrite: true);
        }
    }
}
