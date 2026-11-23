namespace GedCore.Drafting;

/// <summary>
/// Joins the notes several findings of one item wrote on the same record into a single note, so a record
/// holds one note per item and purpose however many findings contributed. Each finding's own citation of that
/// note is kept, now naming the finding explicitly because the joined note is carried by only one of them.
/// Notes that cite one source page at different grades are left apart: a structure cites a page once, at one grade.
/// </summary>
public static class DraftNoteJoiner
{
    // One plain note of a finding, with the source page and grade of each of its citations.
    sealed record Contribution(int Member, DraftNote Note, IReadOnlyList<(SourcePlan.SourceKey Source, string? Page, int Quay)> Grades)
    {
        public bool ClashesWith(Contribution other) =>
            Grades.Any(mine => other.Grades.Any(theirs => mine.Source == theirs.Source && mine.Page == theirs.Page && mine.Quay != theirs.Quay));
    }

    /// <param name="cited">Findings the notes may cite that are not among <paramref name="findings"/>; their source pages are compared too.</param>
    public static IReadOnlyList<DraftFinding> Join(IReadOnlyList<DraftFinding> findings, IReadOnlyList<DraftFinding>? cited = null)
    {
        var citable = new CitableFindings(findings, cited ?? []);
        var joined = findings.ToList();
        foreach (var item in findings.Select((f, i) => (Finding: f, Index: i)).GroupBy(x => (x.Finding.Target.Xref, x.Finding.Target.Card)))
            JoinWithinItem([.. item.Select(x => x.Index)], joined, citable);
        return joined;
    }

    static void JoinWithinItem(IReadOnlyList<int> members, List<DraftFinding> findings, CitableFindings citable)
    {
        var contributions = members
            .SelectMany(m => findings[m].Notes.Where(n => n.Match is null).Select(n => new Contribution(m, n, GradesOf(findings[m], n, citable))))
            .GroupBy(c => (c.Note.Record, c.Note.Purpose))
            .SelectMany(Foldable)
            .Where(g => g.Count > 1)
            .ToList();

        foreach (var group in contributions)
        {
            int owner = group[0].Member;
            var one = new DraftNote(group[0].Note.Record, string.Join(' ', group.Select(c => c.Note.Text).Distinct()))
            {
                Cites = [.. group.SelectMany(c => c.Note.Cites.Select(cite => cite with { Finding = cite.Finding ?? findings[c.Member].Id })).Distinct()],
                Purpose = group[0].Note.Purpose,
            };

            var folded = new HashSet<DraftNote>(group.Select(c => c.Note), ReferenceEqualityComparer.Instance);
            foreach (int member in group.Select(c => c.Member).Distinct())
                findings[member] = findings[member] with { Notes = [.. Rewritten(findings[member].Notes, one, member == owner, folded)] };
        }
    }

    // Each note joins the first group none of whose notes it clashes with, in the order written.
    static List<List<Contribution>> Foldable(IEnumerable<Contribution> contributions)
    {
        var groups = new List<List<Contribution>>();
        foreach (var contribution in contributions)
        {
            var home = groups.FirstOrDefault(group => !group.Any(contribution.ClashesWith));
            if (home is null) groups.Add(home = []);
            home.Add(contribution);
        }
        return groups;
    }

    static IReadOnlyList<(SourcePlan.SourceKey, string?, int)> GradesOf(DraftFinding owner, DraftNote note, CitableFindings citable) =>
        [.. note.Cites
            .Select(cite => (Cited: citable.Cited(owner, cite), cite.Quay))
            .Where(c => c.Cited is not null)
            .Select(c => (SourcePlan.KeyOf(c.Cited!), c.Cited!.Source.Locator, c.Quay))];

    // The first of the folded notes is replaced by the joined note (or dropped, for a finding that does not carry it); the others are dropped.
    static IEnumerable<DraftNote> Rewritten(IReadOnlyList<DraftNote> notes, DraftNote joined, bool carries, HashSet<DraftNote> folded)
    {
        bool placed = false;
        foreach (var note in notes)
        {
            if (!folded.Contains(note)) yield return note;
            else if (carries && !placed)
            {
                placed = true;
                yield return joined;
            }
        }
    }
}
