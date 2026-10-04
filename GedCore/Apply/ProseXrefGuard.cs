using System.Text.RegularExpressions;

namespace GedCore.Apply;

/// <summary>
/// Refuses internal GEDCOM identifiers (@S00223@, @I12@, @NewI1@, @VOID@) in
/// prose a changeset writes: readers of a note or citation text see names and
/// titles, never record ids.
/// </summary>
internal static partial class ProseXrefGuard
{
    // The lookarounds skip an escaped "@@" and an @ inside a longer word, so an
    // email address or a literal "@home" is not mistaken for an id.
    [GeneratedRegex(@"(?<![@\w])@[A-Za-z0-9_]+@(?![@\w])")]
    private static partial Regex Pointer();

    public static void Check(string context, string field, string? text, List<string> errors)
    {
        if (text is null) return;
        var match = Pointer().Match(text);
        if (match.Success)
            errors.Add($"{context}: {field} contains the internal id {match.Value}; " +
                       "refer to the person or source by name so the text reads without the file");
    }
}
