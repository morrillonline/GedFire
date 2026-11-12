namespace GedCore.Drafting;

/// <summary>
/// Joins the notes several findings of one item wrote on the same record into a single note, so a record
/// holds one note per item and purpose however many findings contributed. Each finding's own citation of that
/// note is kept, now naming the finding explicitly because the joined note is carried by only one of them.
/// </summary>
public static class DraftNoteJoiner
{
    public static IReadOnlyList<DraftFinding> Join(IReadOnlyList<DraftFinding> findings)
    {
        var joined = findings.ToList();
        foreach (var item in findings.Select((f, i) => (Finding: f, Index: i)).GroupBy(x => (x.Finding.Target.Xref, x.Finding.Target.Card)))
            JoinWithinItem([.. item.Select(x => x.Index)], joined);
        return joined;
    }

    static void JoinWithinItem(IReadOnlyList<int> members, List<DraftFinding> findings)
    {
        var contributions = members
            .SelectMany(m => findings[m].Notes.Where(n => n.Match is null).Select(n => (Member: m, Note: n)))
            .GroupBy(c => (c.Note.Record, c.Note.Purpose))
            .Where(g => g.Count() > 1);

        foreach (var group in contributions)
        {
            int owner = group.First().Member;
            var one = new DraftNote(group.Key.Record, string.Join(' ', group.Select(c => c.Note.Text).Distinct()))
            {
                Cites = [.. group.SelectMany(c => c.Note.Cites.Select(cite => cite with { Finding = cite.Finding ?? findings[c.Member].Id })).Distinct()],
                Purpose = group.Key.Purpose,
            };

            foreach (int member in group.Select(c => c.Member).Distinct())
                findings[member] = findings[member] with { Notes = [.. Rewritten(findings[member].Notes, one, member == owner)] };
        }
    }

    // The first plain note of the joined note's record and purpose is replaced by it (or dropped, for a finding that does not carry it); the others are dropped.
    static IEnumerable<DraftNote> Rewritten(IReadOnlyList<DraftNote> notes, DraftNote joined, bool carries)
    {
        bool placed = false;
        foreach (var note in notes)
        {
            if (note.Record != joined.Record || note.Purpose != joined.Purpose || note.Match is not null) yield return note;
            else if (carries && !placed)
            {
                placed = true;
                yield return joined;
            }
        }
    }
}
