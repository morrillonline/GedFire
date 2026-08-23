using System.Globalization;
using System.Text.RegularExpressions;
using GedCore.Ged70;
using GedCore.Validate;

namespace GedCore.Apply;

/// <summary>
/// Applies an approved proposal changeset (dialect v2, see
/// <see cref="ChangeOp"/>) to a GEDCOM 7 master file.
///
/// Safety model:
///   1. every op validates against the parsed document before anything is
///      modified — target xrefs resolve, fact selectors are unambiguous,
///      cited sources resolve, provenance requirements hold; ops that will
///      create records register their xrefs so later ops validate against
///      the planned state;
///   2. application happens on the in-memory tree; every replacement is
///      logged with the value it replaced, every no-op with why — the
///      dry-run log is the review artifact for the unconditional-update
///      semantics;
///   3. a run whose ops were all no-ops leaves the file untouched (re-runs
///      are byte-identical by construction);
///   4. otherwise the result is serialized and verified in memory (parse →
///      re-serialize byte-stable, pointer resolution, signed record-count
///      deltas, no newly created source left uncited) and the file on disk is
///      only written after every check passes — there is no window where a
///      bad state exists on disk;
///   5. a newSources[] entry is only created when some selected item actually
///      cites it (or nothing in the whole changeset cites it, i.e. it's
///      prepared ahead of citing it) — excluding an item via --items also
///      excludes the sources that item alone needed, rather than leaving them
///      as uncited orphans.
///
/// File conventions are preserved by construction: Ged70Formatter emits
/// UTF-8 BOM + CRLF; new records go at the end of their record-type block;
/// FAMS is inserted before FAMC; days of month are zero-padded to match the
/// file's style; new INDI/FAM records carry a UID.
/// </summary>
public static class ChangesetApplier
{
    private static readonly Regex PointerPayload = new(@"^@[A-Za-z0-9_]+@$", RegexOptions.Compiled);

    /// <summary>
    /// Apply a changeset to a file on disk, holding one exclusive file handle
    /// from the initial read through verified replacement. FileShare.None
    /// denies every other handle for the whole call, so a concurrent
    /// path-based apply against the same file throws IOException immediately
    /// instead of racing this one -- callers should treat that as a normal
    /// apply failure, not let it propagate unhandled. A dry run only needs
    /// read access since nothing is written.
    /// </summary>
    public static ApplyResult Run(string gedcomPath, Changeset changeset,
                                  IReadOnlyCollection<int> itemNumbers, bool dryRun, DateTime? utcNow = null)
    {
        using var handle = new FileStream(gedcomPath, FileMode.Open,
            dryRun ? FileAccess.Read : FileAccess.ReadWrite, FileShare.None);

        byte[] originalBytes = new byte[handle.Length];
        handle.ReadExactly(originalBytes);

        var result = Run(originalBytes, changeset, itemNumbers, dryRun, utcNow);
        if (!result.Success || result.OutputBytes is null) return result;

        handle.Seek(0, SeekOrigin.Begin);
        handle.Write(result.OutputBytes);
        handle.SetLength(result.OutputBytes.Length);
        handle.Flush(flushToDisk: true);

        handle.Seek(0, SeekOrigin.Begin);
        byte[] writtenBytes = new byte[result.OutputBytes.Length];
        handle.ReadExactly(writtenBytes);
        if (!writtenBytes.SequenceEqual(result.OutputBytes))
        {
            result.Errors.Add("re-read after write does not match written bytes");
            handle.Seek(0, SeekOrigin.Begin);
            handle.Write(originalBytes);
            handle.SetLength(originalBytes.Length);
            handle.Flush(flushToDisk: true);
            result.Success = false;
        }
        return result;
    }

    /// <summary>
    /// Apply a changeset wholly in memory. A successful mutating application
    /// supplies verified replacement bytes through <see cref="ApplyResult.OutputBytes"/>.
    /// </summary>
    public static ApplyResult Run(byte[] originalBytes, Changeset changeset,
                                  IReadOnlyCollection<int> itemNumbers, bool dryRun, DateTime? utcNow = null)
    {
        var result = new ApplyResult();
        var doc = Ged70Parser.Read(new MemoryStream(originalBytes));
        var beforeCounts = CountRecords(doc);
        var beforeDiags = ConformanceChecker.Check(doc);

        var known = changeset.Items.Select(i => i.Number).ToHashSet();
        var unknown = itemNumbers.Where(n => !known.Contains(n)).ToList();
        if (unknown.Count > 0)
        {
            result.Errors.Add($"item(s) not in changeset: {string.Join(", ", unknown)}");
            return result;
        }

        var selectedItems = changeset.Items.Where(i => itemNumbers.Contains(i.Number)).ToList();

        // A newSources[] group is cited by an item when one of that item's ops names
        // its xref in a citation (see ChangeOp.CitedSources). A group nothing in the
        // WHOLE changeset cites is a source prepared ahead of citing it and stays
        // always-applied; a group some item does cite is only applied when a selected
        // item is among its citers — otherwise it would land in the file uncited, an
        // orphan `SOUR` record `--items` was supposed to have excluded entirely.
        var citedByAnyItem = changeset.Items
            .SelectMany(i => i.Ops.SelectMany(op => op.CitedSources))
            .ToHashSet(StringComparer.Ordinal);
        var citedBySelectedItems = selectedItems
            .SelectMany(i => i.Ops.SelectMany(op => op.CitedSources))
            .ToHashSet(StringComparer.Ordinal);
        var usedSources = changeset.NewSources
            .Where(g => !citedByAnyItem.Contains(g.Xref) || citedBySelectedItems.Contains(g.Xref))
            .ToList();
        foreach (var skipped in changeset.NewSources.Except(usedSources))
            result.Log.Add($"newSources {skipped.Xref}: skipped (cited only by excluded item(s))");

        var selectedOps = usedSources.SelectMany(g => g.Ops)
            .Concat(selectedItems.SelectMany(i => i.Ops))
            .ToList();

        var ctx = new ResolutionContext(doc);
        foreach (var op in selectedOps)
            op.Validate(ctx, result.Errors);
        if (result.Errors.Count > 0) return result;

        // New-person duplicate detection runs after every op's own Validate,
        // once ResolutionContext.Placeholders is fully populated and every
        // selected op is available to gather evidence from — a per-op check
        // couldn't see operations after the creating PersonRef.
        PersonDuplicateDetector.Check(doc, ctx, selectedOps, result.Errors);
        if (result.Errors.Count > 0) return result;

        result.Log.Add($"validation OK ({selectedOps.Count} ops, items {string.Join(",", itemNumbers.OrderBy(n => n))})");
        if (dryRun) { result.Success = true; return result; }

        var state = utcNow is { } fixedClock ? new ApplyState(doc) { UtcNow = fixedClock } : new ApplyState(doc);
        try
        {
            foreach (var op in usedSources.SelectMany(g => g.Ops))
                op.Apply(state, result.Log);
            foreach (var item in selectedItems)
            {
                state.BeginItem();
                foreach (var op in item.Ops)
                    op.Apply(state, result.Log);
                result.Log.Add($"item {item.Number}: applied ({item.Target})");
            }
            ApplyFamsBackLinks(state, result.Log);
            ApplyFamcBackLinks(state, result.Log);
        }
        catch (InvalidOperationException ex)
        {
            // An op's own assumptions about the document state were violated at
            // apply time (e.g. Validate and Apply resolved a target differently) —
            // report it through the normal failure path rather than an unhandled
            // exception. Nothing has been written yet, so the file is untouched.
            result.Errors.Add($"apply-time invariant violated: {ex.Message}");
            return result;
        }

        if (state.Mutations == 0)
        {
            result.Log.Add("no changes; file untouched");
            result.Success = true;
            return result;
        }

        StampChangeDates(state, result.Log);

        // Post-apply conformance gate (Subproject D2): the mutation is already on
        // doc in memory, before serialization — no need to reparse to check it.
        // Only *new* findings (occurrence count higher than the baseline) count;
        // a pre-existing Warning the file already carried is not this changeset's
        // problem, and a changeset that removes a diagnostic is always welcome.
        var afterDiags = ConformanceChecker.Check(doc);
        var newDiags = ConformanceChecker.DiffDiagnostics(beforeDiags, afterDiags);
        var newErrors = newDiags.Where(d => d.Severity == GedDiagnosticSeverity.Error).ToList();
        if (newErrors.Count > 0)
        {
            foreach (var d in newErrors)
                result.Errors.Add($"conformance regression: {d.Code} {d.Xref} {d.Tag}: {d.Message}");
            return result;
        }
        foreach (var d in newDiags.Where(d => d.Severity != GedDiagnosticSeverity.Error))
            result.Log.Add($"conformance note: {d.Code} {d.Xref} {d.Tag}: {d.Message}");

        var ms = new MemoryStream();
        Ged70Formatter.Write(doc, ms);
        byte[] newBytes = ms.ToArray();

        // citedByAnyItem was gathered from the ops' own fields before apply
        // ran, so a placeholder-keyed entry is still placeholder text; resolve
        // it to the real minted xref (state.CreatedSourceXrefs already holds
        // real xrefs) before checking which created sources are the ones
        // some item actually meant to cite.
        var citedByAnyItemResolved = citedByAnyItem.Select(state.Resolve).ToHashSet(StringComparer.Ordinal);
        Verify(newBytes, beforeCounts, state.Deltas,
               state.CreatedSourceXrefs.Intersect(citedByAnyItemResolved), result);
        if (result.Errors.Count > 0) return result;

        result.Deltas = state.Deltas;
        result.MintedXrefs = state.MintedXrefs;
        result.OutputBytes = newBytes;
        result.Success = true;
        return result;
    }

    /// <summary>Record tags whose changes are worth a CHAN change-date stamp.</summary>
    private static readonly HashSet<string> ChanEligibleTags =
        new(StringComparer.Ordinal) { "INDI", "FAM", "SOUR", "SNOTE", "REPO", "OBJE" };

    /// <summary>
    /// Subproject F: upsert <c>CHAN / DATE / TIME</c> on every level-0 record this
    /// run touched in place, using <see cref="ApplyState.UtcNow"/>. Runs only on
    /// the mutating path (never validate/dry-run — the caller returns before this
    /// point otherwise) and only for records still present in the document — a
    /// record removed by this run has no CHAN left to stamp. The CHAN upsert
    /// itself is not tracked as a mutation or touch, so it cannot perpetuate
    /// itself across re-runs.
    /// </summary>
    private static void StampChangeDates(ApplyState state, List<string> log)
    {
        string date = NodeBuilder.PadDay(
            state.UtcNow.ToString("d MMM yyyy", CultureInfo.InvariantCulture).ToUpperInvariant());
        string time = state.UtcNow.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + "Z";

        // Iterate in document order (TouchedRoots is a hash set) so stamping and
        // its log lines are deterministic; a record removed by this run is no
        // longer in Records and is skipped by construction.
        foreach (var root in state.Doc.Records)
        {
            if (!state.TouchedRoots.Contains(root)) continue;
            if (!ChanEligibleTags.Contains(root.Tag)) continue;

            var chan = root.FirstChild("CHAN");
            if (chan is null)
            {
                chan = NodeBuilder.NewNode(1, "CHAN", "");
                NodeBuilder.Attach(root, chan);
            }

            var dateNode = chan.FirstChild("DATE");
            if (dateNode is null)
            {
                dateNode = NodeBuilder.NewNode(2, "DATE", date);
                NodeBuilder.Attach(chan, dateNode);
            }
            else
            {
                dateNode.SetValue(date);
            }

            var timeNode = dateNode.FirstChild("TIME");
            if (timeNode is null)
                NodeBuilder.Attach(dateNode, NodeBuilder.NewNode(3, "TIME", time));
            else
                timeNode.SetValue(time);

            log.Add($"stamped CHAN on {root.Xref}");
        }
    }

    /// <summary>Two-way linking: every partner placed by a relationship op gets a FAMS.</summary>
    private static void ApplyFamsBackLinks(ApplyState state, List<string> log)
    {
        foreach (var (indiXref, fams) in state.PendingFams)
        {
            var indi = state.Doc.ByXref[indiXref];
            var missing = fams
                .Where(f => !indi.Children.Any(c => c.Tag == "FAMS" && c.Value == f))
                .ToList();
            if (missing.Count == 0) continue;

            // file convention: FAMS before FAMC; keep UID last on new records
            int anchor = indi.Children.FindIndex(c => c.Tag is "FAMC" or "UID");
            foreach (var (fam, i) in missing.Select((f, i) => (f, i)))
                NodeBuilder.Attach(indi, NodeBuilder.NewNode(1, "FAMS", fam),
                                   at: anchor < 0 ? null : anchor + i);
            state.Mutated();
            state.Touch(indi);
            log.Add($"FAMS back-link(s) {string.Join(", ", missing)} on {indiXref}");
        }
    }

    /// <summary>Two-way linking: every child placed by a relationship op gets a FAMC.</summary>
    private static void ApplyFamcBackLinks(ApplyState state, List<string> log)
    {
        foreach (var (indiXref, famcs) in state.PendingFamc)
        {
            var indi = state.Doc.ByXref[indiXref];
            var missing = famcs
                .Where(f => !indi.Children.Any(c => c.Tag == "FAMC" && c.Value == f))
                .ToList();
            if (missing.Count == 0) continue;

            // file convention: FAMC follows FAMS and precedes the trailing UID
            int anchor = indi.Children.FindIndex(c => c.Tag == "UID");
            foreach (var (fam, i) in missing.Select((f, i) => (f, i)))
                NodeBuilder.Attach(indi, NodeBuilder.NewNode(1, "FAMC", fam),
                                   at: anchor < 0 ? null : anchor + i);
            state.Mutated();
            state.Touch(indi);
            log.Add($"FAMC back-link(s) {string.Join(", ", missing)} on {indiXref}");
        }
    }

    // -------------------------------------------------------------------------
    // Verification — runs on the serialized bytes before anything hits disk
    // -------------------------------------------------------------------------

    private static void Verify(byte[] newBytes, Dictionary<string, int> beforeCounts,
                               Dictionary<string, int> expectedDeltas,
                               IEnumerable<string> createdCitedSourceXrefs, ApplyResult result)
    {
        var reparsed = Ged70Parser.Read(new MemoryStream(newBytes));

        var ms = new MemoryStream();
        Ged70Formatter.Write(reparsed, ms);
        if (!ms.ToArray().SequenceEqual(newBytes))
            result.Errors.Add("round-trip serialization is not byte-stable");

        var defined = new HashSet<string>(reparsed.ByXref.Keys, StringComparer.Ordinal) { "@VOID@" };
        foreach (var root in reparsed.Records)
            VerifyPointers(root, defined, result.Errors);

        var afterCounts = CountRecords(reparsed);
        foreach (var tag in beforeCounts.Keys.Union(afterCounts.Keys))
        {
            int delta = afterCounts.GetValueOrDefault(tag) - beforeCounts.GetValueOrDefault(tag);
            int expected = expectedDeltas.GetValueOrDefault(tag);
            if (delta != expected)
                result.Errors.Add($"record count {tag}: expected {expected:+0;-0;+0}, got {delta:+0;-0;+0}");
        }

        // Defense-in-depth backstop behind the newSources[] citation filtering in Run:
        // a SOUR record this run created, that some item in the changeset intended to
        // cite, should actually be cited somewhere. Only sources some item cites are
        // checked — one nothing in the changeset cites is deliberately created ahead of
        // citing it (see Run) and is not this check's concern. Likewise this only checks
        // records the run itself created, not pre-existing ones — an orphan the file
        // already carried is not this changeset's problem to fail on.
        foreach (var xref in createdCitedSourceXrefs)
        {
            if (!reparsed.ByXref.ContainsKey(xref)) continue;   // deleted later in the same run
            int citations = SourceCitations.Count(reparsed.Records, xref);
            if (citations == 0)
                result.Errors.Add($"orphan source {xref}: created with no citation anywhere in the file");
        }
    }

    private static void VerifyPointers(GedRecord rec, HashSet<string> defined, List<string> errors)
    {
        if (rec.Level > 0 && PointerPayload.IsMatch(rec.Value) && !defined.Contains(rec.Value))
            errors.Add($"dangling pointer {rec.Value} under {rec.Tag}");
        foreach (var child in rec.Children)
            VerifyPointers(child, defined, errors);
    }

    private static Dictionary<string, int> CountRecords(GedDocument doc)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var rec in doc.Records)
            counts[rec.Tag] = counts.GetValueOrDefault(rec.Tag) + 1;
        return counts;
    }
}

/// <summary>Outcome of a changeset application: log, errors, record-count deltas.</summary>
public sealed class ApplyResult
{
    public bool Success { get; internal set; }
    public List<string> Log { get; } = [];
    public List<string> Errors { get; } = [];
    public Dictionary<string, int> Deltas { get; internal set; } = [];
    public byte[]? OutputBytes { get; internal set; }

    /// <summary>
    /// Placeholder token → real minted xref for every new record this run
    /// created. Empty for a dry run, a validation failure, or a run whose
    /// ops were all no-ops.
    /// </summary>
    public Dictionary<string, string> MintedXrefs { get; internal set; } = new(StringComparer.Ordinal);
}
