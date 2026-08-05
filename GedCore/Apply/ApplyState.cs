namespace GedCore.Apply;

/// <summary>
/// Mutable state threaded through op application: the live document, signed
/// record-count deltas (record creation and deletion are conditional in
/// dialect v2, so deltas are counted as they happen), a mutation counter
/// driving the all-no-op short-circuit, and the deferred back-link maps.
/// </summary>
internal sealed class ApplyState(GedDocument doc)
{
    public GedDocument Doc { get; } = doc;

    /// <summary>Signed record-count deltas by record type (INDI/FAM/SOUR).</summary>
    public Dictionary<string, int> Deltas { get; } = new(StringComparer.Ordinal);

    /// <summary>Count of actual tree mutations; zero means the file is not rewritten.</summary>
    public int Mutations { get; private set; }

    /// <summary>Xrefs of SOUR records this run created, for the post-apply orphan-source
    /// check in <see cref="ChangesetApplier"/> — a defense-in-depth backstop behind the
    /// newSources[] citation filtering, in case some other path creates an uncited one.</summary>
    public HashSet<string> CreatedSourceXrefs { get; } = new(StringComparer.Ordinal);

    /// <summary>
    /// Level-0 records touched by this run's mutations, for the post-apply
    /// CHAN (change date) stamping phase (Subproject F). A record is touched
    /// only when it survives in place — a record removed outright has no
    /// CHAN left to stamp, so <see cref="RemoveRecord"/> does not touch.
    /// </summary>
    public HashSet<GedRecord> TouchedRoots { get; } = [];

    /// <summary>Injectable clock for CHAN stamping; defaults to the real time, fixed for tests.</summary>
    public DateTime UtcNow { get; init; } = DateTime.UtcNow;

    /// <summary>Mark the level-0 record containing <paramref name="node"/> as touched this run.</summary>
    public void Touch(GedRecord node)
    {
        var root = node;
        while (root.Parent is not null) root = root.Parent;
        TouchedRoots.Add(root);
    }

    /// <summary>INDI xref → FAM xrefs needing a FAMS back-link.</summary>
    public Dictionary<string, List<string>> PendingFams { get; } = [];

    /// <summary>INDI xref → FAM xrefs needing a FAMC back-link.</summary>
    public Dictionary<string, List<string>> PendingFamc { get; } = [];

    /// <summary>
    /// Record xref → NOTE node added (or matched) by a note op earlier in the
    /// item currently being applied. Lets a moveToNote disposition in the same
    /// item find "the note this proposal added", as opposed to any unrelated
    /// NOTE the record already carried. Reset at the start of each item.
    /// </summary>
    public Dictionary<string, GedRecord> NotesAddedThisItem { get; } = [];

    /// <summary>
    /// SOUR citation node → the field values (PAGE/DATA.TEXT/QUAY) written to
    /// it so far in this run. One citation per source per structure holds by
    /// construction, so two ops that both cite the same source on the same
    /// structure (e.g. a father-only and a mother-only "Parents" extract both
    /// cited on one FAM record) land on the same node; without this guard the
    /// second op's <see cref="NodeBuilder.UpsertCitation"/> would silently
    /// overwrite the first op's DATA.TEXT. Spans the whole run, not one item,
    /// because relationship provenance for father and mother is composed as
    /// two separate items against the same family.
    /// </summary>
    private readonly Dictionary<GedRecord, Dictionary<string, string>> _citationFieldWrites = new();

    public void BeginItem() => NotesAddedThisItem.Clear();

    /// <summary>
    /// Record a citation field write and reject a conflicting one: if a prior
    /// op this run already wrote a different value to the same field of the
    /// same citation node, throw so the run fails cleanly with the file
    /// untouched rather than silently discarding the earlier attestation. The
    /// composer must combine the two attestations into one citation.
    /// </summary>
    public void RecordCitationField(GedRecord citation, string source, string field, string value)
    {
        if (!_citationFieldWrites.TryGetValue(citation, out var fields))
            _citationFieldWrites[citation] = fields = new(StringComparer.Ordinal);
        if (fields.TryGetValue(field, out var prior) && prior != value)
            throw new InvalidOperationException(
                $"citation {source}: {field} written twice with different values in one " +
                $"changeset ('{prior}' vs '{value}') — the same source cited on one structure " +
                "is a single citation; combine the attestations into one citation's text");
        fields[field] = value;
    }

    public void Mutated() => Mutations++;

    private void Bump(string tag, int delta)
    {
        int value = Deltas.GetValueOrDefault(tag) + delta;
        if (value == 0) Deltas.Remove(tag);
        else Deltas[tag] = value;
    }

    public void AddRecord(string tag, GedRecord rec)
    {
        NodeBuilder.InsertAfterLastOfKind(Doc, tag, rec);
        Bump(tag, +1);
        Mutated();
        Touch(rec);   // newly created level-0 record
        if (tag == "SOUR" && rec.Xref is not null) CreatedSourceXrefs.Add(rec.Xref);
        Doc.RebuildXrefIndex();   // later ops in this run can resolve it
    }

    public void RemoveRecord(GedRecord rec)
    {
        Doc.MutableRecords.Remove(rec);
        Bump(rec.Tag, -1);
        Mutated();
        Doc.RebuildXrefIndex();
    }

    public void PendFams(string indiXref, string famXref) => Pend(PendingFams, indiXref, famXref);
    public void PendFamc(string indiXref, string famXref) => Pend(PendingFamc, indiXref, famXref);

    private static void Pend(Dictionary<string, List<string>> map, string indiXref, string famXref)
    {
        if (!map.TryGetValue(indiXref, out var fams)) map[indiXref] = fams = [];
        if (!fams.Contains(famXref)) fams.Add(famXref);
    }
}
