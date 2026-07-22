namespace GedFire;

/// <summary>
/// Resolves the page template for the generate command.  An explicit
/// --template path always wins (even if missing — the caller reports that);
/// otherwise well-known locations relative to the input GED are probed, and
/// null means "no template found, use the built-in default".
/// </summary>
public static class TemplateLocator
{
    /// <summary>Fallback template used when no template file can be located.</summary>
    public const string DefaultTemplateHtml =
        "<html><head><title><insert title></title><insert stylesheet></head>"
        + "<body><insert body></body></html>";

    /// <summary>
    /// Return the template path to use for <paramref name="inputGedPath"/>,
    /// or null when nothing was found.  Probe order: the modern template under
    /// content/template, then generic fallback locations kept for compatibility
    /// with older checkouts.
    /// </summary>
    public static string? Locate(string inputGedPath, string? explicitPath)
    {
        if (explicitPath is not null) return explicitPath;

        string dir  = Path.GetDirectoryName(Path.GetFullPath(inputGedPath)) ?? ".";
        string root = Path.GetFullPath(Path.Combine(dir, ".."));

        string[] candidates =
        [
            Path.Combine(root, "content", "template", "GedfireModernTemplate.html"),
            Path.Combine(root, "gedfire", "GedfireTemplate.html"),
            Path.Combine(dir, "GedfireTemplate.html"),
        ];
        return candidates.FirstOrDefault(File.Exists);
    }
}
