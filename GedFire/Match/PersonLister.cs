using System.Text;
using GedCore;
using GedCore.Matching;
using GedFire.Gen;

namespace GedFire.Match;

// ---------------------------------------------------------------------------
// Lists every person in a MatchIndex in a fixed order, optionally limited to a
// set of surnames, one page at a time. The order is (surname, given, xref) so
// the same tree always pages the same way; a cursor records the last entry
// returned rather than an offset.
// ---------------------------------------------------------------------------

public sealed record PersonListEntry(string Xref, string Name, string Surname, int? BirthYear, int? DeathYear);

public sealed record PersonListPage(IReadOnlyList<PersonListEntry> People, int TotalMatches, string? NextCursor);

public static class PersonLister
{
    const char Separator = '\u0001';

    public static bool TryList(
        MatchIndex index, IReadOnlyCollection<string>? surnames, string? cursor, int pageSize,
        out PersonListPage page, out string? error)
    {
        page = new PersonListPage([], 0, null);
        if (!TryDecodeCursor(cursor, out var after, out error)) return false;

        var wanted = surnames is null || surnames.Count == 0
            ? null
            : surnames.Select(PersonNameNormalizer.Normalize).ToHashSet(StringComparer.Ordinal);

        var ordered = index.Entries
            .Where(e => wanted is null || wanted.Contains(e.NormalizedSurname))
            .OrderBy(e => e.NormalizedSurname, StringComparer.Ordinal)
            .ThenBy(e => e.NormalizedGiven, StringComparer.Ordinal)
            .ThenBy(e => e.Individual.Xref, StringComparer.Ordinal)
            .ToList();

        var remaining = after is null ? ordered : ordered.Where(e => IsAfter(e, after.Value)).ToList();
        var pageEntries = remaining.Take(pageSize).ToList();
        string? next = remaining.Count > pageSize ? EncodeCursor(pageEntries[^1]) : null;

        page = new PersonListPage([.. pageEntries.Select(ToListEntry)], ordered.Count, next);
        return true;
    }

    static PersonListEntry ToListEntry(PersonIndexEntry entry)
    {
        var indi = entry.Individual;
        return new PersonListEntry(
            indi.Xref, PersonDisplay.FullName(indi), indi.LastName,
            YearOrNull(indi.Birth), YearOrNull(indi.Death));
    }

    static int? YearOrNull(GedEvent? ev)
    {
        int year = GedDate.ParseYear(ev?.Date);
        return year != 0 ? year : null;
    }

    static bool IsAfter(PersonIndexEntry entry, (string Surname, string Given, string Xref) after)
    {
        int c = string.CompareOrdinal(entry.NormalizedSurname, after.Surname);
        if (c == 0) c = string.CompareOrdinal(entry.NormalizedGiven, after.Given);
        if (c == 0) c = string.CompareOrdinal(entry.Individual.Xref, after.Xref);
        return c > 0;
    }

    static string EncodeCursor(PersonIndexEntry entry) => Convert.ToBase64String(
        Encoding.UTF8.GetBytes($"{entry.NormalizedSurname}{Separator}{entry.NormalizedGiven}{Separator}{entry.Individual.Xref}"));

    static bool TryDecodeCursor(string? cursor, out (string Surname, string Given, string Xref)? after, out string? error)
    {
        after = null;
        error = null;
        if (string.IsNullOrEmpty(cursor)) return true;
        try
        {
            var parts = Encoding.UTF8.GetString(Convert.FromBase64String(cursor)).Split(Separator);
            if (parts.Length == 3)
            {
                after = (parts[0], parts[1], parts[2]);
                return true;
            }
        }
        catch (FormatException) { }
        error = "cursor is not a value returned by a previous list_people call; omit it to start at the first page.";
        return false;
    }
}
