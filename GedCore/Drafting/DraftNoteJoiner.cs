namespace GedCore.Drafting;

/// <summary>
/// Joins the notes several findings of one item wrote on the same record into a single note, so a record
/// holds one note per item however many findings contributed. Each finding's own citation of that note is
/// kept, now naming the finding explicitly because the joined note is carried by only one of them.
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
            .GroupBy(c => c.Note.Record)
            .Where(g => g.Count() > 1);

        foreach (var record in contributions)
        {
            int owner = record.First().Member;
            var one = new DraftNote(record.Key, string.Join(' ', record.Select(c => c.Note.Text).Distinct()))
            {
                Cites = [.. record.SelectMany(c => c.Note.Cites.Select(cite => cite with { Finding = cite.Finding ?? findings[c.Member].Id })).Distinct()],
            };

            foreach (int member in record.Select(c => c.Member).Distinct())
                findings[member] = findings[member] with { Notes = [.. Rewritten(findings[member].Notes, record.Key, member == owner ? one : null)] };
        }
    }

    // The first plain note on the record is replaced by the joined one (or dropped, for a finding that does not carry it); the others are dropped.
    static IEnumerable<DraftNote> Rewritten(IReadOnlyList<DraftNote> notes, string record, DraftNote? replacement)
    {
        bool placed = false;
        foreach (var note in notes)
        {
            if (note.Record != record || note.Match is not null) yield return note;
            else if (replacement is not null && !placed)
            {
                placed = true;
                yield return replacement;
            }
        }
    }
}
