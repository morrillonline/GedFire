namespace GedCore;

// ---------------------------------------------------------------------------
// Shared GEDCOM NAME payload tokenizer ("Given Middle /Surname/"): the one
// place the given/surname split around the slash delimiters is implemented,
// used by GedFire.Gen.ModelBuilder (building GedIndividual.FirstName/
// MiddleName/LastNameRaw from the parsed document) and GedCore.Apply's
// person-duplicate-detection candidate index (deriving the same fields
// directly from a raw GedRecord, with no GedModel available). Pure text
// splitting only -- it has no opinion on how to interpret a payload with no
// slash at all (Surname comes back null for that case); each of those two
// callers already made its own, different, pre-existing choice about that
// edge case and keeps it in a thin wrapper around this function.
// ---------------------------------------------------------------------------

public static class GedNamePayload
{
    /// <summary>
    /// Split <paramref name="rawName"/> at its first and second slash.
    /// <see cref="ValueTuple{T1, T2}.Item2"/> (the surname) is null when no
    /// slash was present at all; otherwise it is the (possibly empty) text
    /// between the first and second slash, or between the first slash and
    /// the end of the string when there is no second slash. Both parts are
    /// trimmed. Never throws; a null or empty input is the no-slash case.
    /// </summary>
    public static (string GivenMiddle, string? Surname) Split(string? rawName)
    {
        if (string.IsNullOrEmpty(rawName)) return ("", null);
        int slash1 = rawName.IndexOf('/');
        if (slash1 < 0) return (rawName.Trim(), null);

        string givenMiddle = rawName[..slash1].Trim();
        string rest = rawName[(slash1 + 1)..];
        int slash2 = rest.IndexOf('/');
        string surname = (slash2 >= 0 ? rest[..slash2] : rest).Trim();
        return (givenMiddle, surname);
    }
}
