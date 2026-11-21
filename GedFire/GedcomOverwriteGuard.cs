namespace GedFire;

/// <summary>Keeps the drafts, reports and exports a command or tool writes off the GEDCOM it is working on.</summary>
public static class GedcomOverwriteGuard
{
    /// <summary>Why <paramref name="outputPath"/> cannot be written, naming <paramref name="option"/>; null when it is not the GEDCOM.</summary>
    public static string? Refusal(string outputPath, string option, string gedcomPath) =>
        IsSameFile(outputPath, gedcomPath)
            ? $"{option} {Path.GetFullPath(outputPath)} is the GEDCOM file itself and would be overwritten; choose another file."
            : null;

    // Case is ignored on every platform: a wrongly refused path costs a rename, a wrongly allowed one costs the GEDCOM.
    static bool IsSameFile(string path, string other) =>
        string.Equals(Path.GetFullPath(path), Path.GetFullPath(other), StringComparison.OrdinalIgnoreCase);
}
