namespace GedFire.Cli;

// ---------------------------------------------------------------------------
// Where a GEDCOM's media is found. Always the GEDCOM's own directory: every
// FILE payload a media op writes is a path relative to it, "media/" prefix
// included, so there is no second location to configure.
// ---------------------------------------------------------------------------

public static class MediaDirectory
{
    public static string For(string gedcomPath) => Path.GetDirectoryName(Path.GetFullPath(gedcomPath)) ?? ".";
}
