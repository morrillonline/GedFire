using System.Text;
using GedCore.Apply;
using GedCore.Matching;

namespace GedCore.Validate;

/// <summary>
/// Genealogical plausibility checks (GEN1xx date/age/order, GEN3xx
/// identity/topology, GEN4xx name-format) — see docs/design/plausibility-checker.md. Distinct
/// from <see cref="ConformanceChecker"/>: that proves a file is
/// syntactically well-formed, this flags facts that don't make
/// chronological or biological sense. Same shape as
/// <see cref="ConformanceChecker"/> deliberately — one private
/// <c>Check&lt;RuleName&gt;</c> method per rule, sharing <see cref="GedDiagnostic"/>
/// and <see cref="GedDiagnosticSeverity"/> — but a separate class, since the
/// two check different things even though they share a result shape and a
/// gate (see <see cref="ConformanceChecker"/>'s own summary).
///
/// Every rule here is a <see cref="GedDiagnosticSeverity.Warning"/> except
/// GEN302 (ancestor cycle): a chronological or age outlier sometimes is
/// correct and this checker cannot see the source behind the fact, but a
/// person being their own ancestor is not a judgment call — it is
/// logically impossible by construction, the same bar GED-family
/// conformance errors already use.
/// </summary>
public static class PlausibilityChecker
{
    /// <summary>
    /// Run every rule against <paramref name="doc"/>, sorted by severity then
    /// code. <paramref name="duplicateCheckScope"/> restricts GEN301
    /// (possible duplicate) to only the given people as the query side of the
    /// match, still scored against their full same-surname bucket -- pass
    /// null (the default) for the unrestricted whole-document sweep every
    /// other rule already does. See <see cref="CheckPossibleDuplicates"/> for
    /// why that rule alone needs this. <paramref name="cancellationToken"/>
    /// is checked between rules (each of the others is its own flat O(n)
    /// pass) and, additionally, inside CheckPossibleDuplicates's own loop --
    /// that's the one rule whose single call can still be a large enough
    /// scope (a big batch changeset) for a between-rules check alone to be
    /// too coarse.
    /// </summary>
    public static IReadOnlyList<GedDiagnostic> Check(
        GedDocument doc, IReadOnlySet<string>? duplicateCheckScope = null, CancellationToken cancellationToken = default)
    {
        var diags = new List<GedDiagnostic>();

        cancellationToken.ThrowIfCancellationRequested();
        CheckCanonicalEventOrder(doc, diags);
        cancellationToken.ThrowIfCancellationRequested();
        CheckParentAgeAtBirth(doc, diags);
        cancellationToken.ThrowIfCancellationRequested();
        CheckMarriageAge(doc, diags);
        cancellationToken.ThrowIfCancellationRequested();
        CheckLargeSpousalAgeGap(doc, diags);
        cancellationToken.ThrowIfCancellationRequested();
        CheckLargeChildrenSpan(doc, diags);
        cancellationToken.ThrowIfCancellationRequested();
        CheckChildrenOrMarriageMismatch(doc, diags);
        cancellationToken.ThrowIfCancellationRequested();
        CheckImplausibleAgeAtDeath(doc, diags);
        cancellationToken.ThrowIfCancellationRequested();
        CheckDiedTooYoungForFacts(doc, diags);
        cancellationToken.ThrowIfCancellationRequested();
        CheckPossibleDuplicates(doc, diags, duplicateCheckScope, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        CheckAncestorCycles(doc, diags);
        cancellationToken.ThrowIfCancellationRequested();
        CheckTooManyChildren(doc, diags);
        cancellationToken.ThrowIfCancellationRequested();
        CheckMultipleParentFamilies(doc, diags);
        cancellationToken.ThrowIfCancellationRequested();
        CheckManySpouses(doc, diags);
        cancellationToken.ThrowIfCancellationRequested();
        CheckDisconnectedIndividual(doc, diags);
        cancellationToken.ThrowIfCancellationRequested();
        CheckInvalidNameCharacters(doc, diags);
        cancellationToken.ThrowIfCancellationRequested();
        CheckMissingSex(doc, diags);
        cancellationToken.ThrowIfCancellationRequested();
        CheckHusbandWifeSexMismatch(doc, diags);

        return [.. diags.OrderBy(d => d.Severity).ThenBy(d => d.Code, StringComparer.Ordinal)];
    }

    // -------------------------------------------------------------------
    // GEN101 — canonical event order
    // -------------------------------------------------------------------

    // One generic rule driven by a canonical per-person tag sequence
    // (BIRT -> BAPT/CHR -> MARR (via FAMS) -> DEAT -> BURI/PROB) rather than
    // one hand-written rule per event pair — see design doc for why. Plus
    // one cross-person check a per-person sequence can't see on its own: a
    // family's MARR recorded after either spouse's own DEAT.

    private static void CheckCanonicalEventOrder(GedDocument doc, List<GedDiagnostic> diags)
    {
        foreach (var indi in doc.Records.Where(r => r.Tag == "INDI"))
        {
            if (indi.Xref is null) continue;

            int? birth = YearOf(indi.ChildrenByTag("BIRT").LastOrDefault());
            int? bapt = YearOf(indi.ChildrenByTag("BAPT").LastOrDefault())
                        ?? YearOf(indi.ChildrenByTag("CHR").LastOrDefault());
            int? death = YearOf(indi.ChildrenByTag("DEAT").LastOrDefault());
            int? buri = YearOf(indi.ChildrenByTag("BURI").LastOrDefault())
                        ?? YearOf(indi.ChildrenByTag("PROB").LastOrDefault());

            FlagIfOutOfOrder(diags, indi.Xref, birth, "BIRT", bapt, "BAPT/CHR");
            FlagIfOutOfOrder(diags, indi.Xref, birth, "BIRT", death, "DEAT");
            FlagIfOutOfOrder(diags, indi.Xref, bapt, "BAPT/CHR", death, "DEAT");
            FlagIfOutOfOrder(diags, indi.Xref, death, "DEAT", buri, "BURI/PROB");
            FlagIfOutOfOrder(diags, indi.Xref, birth, "BIRT", buri, "BURI/PROB");

            foreach (var famsLink in indi.ChildrenByTag("FAMS"))
            {
                if (famsLink.Value == GedRecord.VoidPointer) continue;
                if (!doc.ByXref.TryGetValue(famsLink.Value, out var fam) || fam.Tag != "FAM") continue;

                int? marr = YearOf(fam.ChildrenByTag("MARR").LastOrDefault());
                FlagIfOutOfOrder(diags, indi.Xref, birth, "BIRT", marr, "MARR");
                FlagIfOutOfOrder(diags, indi.Xref, marr, "MARR", death, "DEAT");
            }
        }

        // Cross-spouse: a family's MARR recorded after the OTHER spouse's own
        // DEAT — not visible from either spouse's own canonical sequence alone.
        foreach (var fam in doc.Records.Where(r => r.Tag == "FAM"))
        {
            int? marr = YearOf(fam.ChildrenByTag("MARR").LastOrDefault());
            if (marr is null) continue;

            foreach (var roleTag in new[] { "HUSB", "WIFE" })
            {
                string? spouseXref = fam.FirstChild(roleTag)?.Value;
                if (spouseXref is null || spouseXref == GedRecord.VoidPointer) continue;
                if (!doc.ByXref.TryGetValue(spouseXref, out var spouse) || spouse.Tag != "INDI") continue;

                int? spouseDeath = YearOf(spouse.ChildrenByTag("DEAT").LastOrDefault());
                if (spouseDeath is int sd && marr.Value > sd)
                    diags.Add(new GedDiagnostic(GedDiagnosticSeverity.Warning, "GEN101",
                        $"MARR ({marr}) on {fam.Xref} recorded after {spouseXref}'s own DEAT ({sd})",
                        fam.Xref, "MARR"));
            }
        }
    }

    private static void FlagIfOutOfOrder(
        List<GedDiagnostic> diags, string xref,
        int? earlierYear, string earlierTag, int? laterYear, string laterTag)
    {
        if (earlierYear is int ey && laterYear is int ly && ly < ey)
            diags.Add(new GedDiagnostic(GedDiagnosticSeverity.Warning, "GEN101",
                $"{laterTag} ({ly}) recorded before {earlierTag} ({ey}) on {xref}", xref, laterTag));
    }

    // -------------------------------------------------------------------
    // GEN102 — parent age at child's birth
    // -------------------------------------------------------------------

    private static void CheckParentAgeAtBirth(GedDocument doc, List<GedDiagnostic> diags)
    {
        var indiByXref = doc.Records
            .Where(r => r.Tag == "INDI" && r.Xref is not null)
            .ToDictionary(r => r.Xref!, StringComparer.Ordinal);

        foreach (var fam in doc.Records.Where(r => r.Tag == "FAM"))
        {
            string? husbXref = fam.FirstChild("HUSB")?.Value;
            string? wifeXref = fam.FirstChild("WIFE")?.Value;
            int? fatherBirth = ParentBirthYear(indiByXref, husbXref);
            int? motherBirth = ParentBirthYear(indiByXref, wifeXref);

            foreach (var chilLink in fam.ChildrenByTag("CHIL"))
            {
                if (chilLink.Value == GedRecord.VoidPointer) continue;
                if (!indiByXref.TryGetValue(chilLink.Value, out var child)) continue;
                int? childBirth = YearOf(child.ChildrenByTag("BIRT").LastOrDefault());
                if (childBirth is null) continue;

                CheckParentAge(diags, fam, chilLink.Value, childBirth.Value, motherBirth, "mother", wifeXref, 17, 50);
                CheckParentAge(diags, fam, chilLink.Value, childBirth.Value, fatherBirth, "father", husbXref, 18, 65);
            }
        }
    }

    private static void CheckParentAge(
        List<GedDiagnostic> diags, GedRecord fam, string childXref, int childBirthYear,
        int? parentBirthYear, string role, string? parentXref, int min, int max)
    {
        if (parentBirthYear is not int pb || parentXref is null || parentXref == GedRecord.VoidPointer) return;
        int age = childBirthYear - pb;
        if (age < min || age > max)
            diags.Add(new GedDiagnostic(GedDiagnosticSeverity.Warning, "GEN102",
                $"{role} {parentXref} was {age} at {childXref}'s birth ({childBirthYear}), outside {min}-{max}",
                fam.Xref, "CHIL"));
    }

    private static int? ParentBirthYear(Dictionary<string, GedRecord> indiByXref, string? xref) =>
        xref is not null && xref != GedRecord.VoidPointer && indiByXref.TryGetValue(xref, out var rec)
            ? YearOf(rec.ChildrenByTag("BIRT").LastOrDefault())
            : null;

    // -------------------------------------------------------------------
    // GEN103 — marriage-event age under 12
    // -------------------------------------------------------------------

    private static void CheckMarriageAge(GedDocument doc, List<GedDiagnostic> diags)
    {
        foreach (var fam in doc.Records.Where(r => r.Tag == "FAM"))
        {
            int? marr = YearOf(fam.ChildrenByTag("MARR").LastOrDefault());
            if (marr is null) continue;

            foreach (var roleTag in new[] { "HUSB", "WIFE" })
            {
                string? spouseXref = fam.FirstChild(roleTag)?.Value;
                if (spouseXref is null || spouseXref == GedRecord.VoidPointer) continue;
                if (!doc.ByXref.TryGetValue(spouseXref, out var spouse) || spouse.Tag != "INDI") continue;

                int? spouseBirth = YearOf(spouse.ChildrenByTag("BIRT").LastOrDefault());
                if (spouseBirth is not int sb) continue;

                int age = marr.Value - sb;
                if (age < 12)
                    diags.Add(new GedDiagnostic(GedDiagnosticSeverity.Warning, "GEN103",
                        $"{spouseXref} was {age} at MARR ({marr}) on {fam.Xref}", fam.Xref, "MARR"));
            }
        }
    }

    // -------------------------------------------------------------------
    // GEN104 — large spousal age gap
    // -------------------------------------------------------------------

    // Gramps LargeAgeGapFamily's own default (30y), unchanged — no FamilySearch
    // equivalent to prefer instead (see Prior art), and unlike GEN102's OldParent
    // default this isn't tied to a biological ceiling FamilySearch's own numbers
    // narrow, so there's nothing to widen it against.
    private const int MaxPlausibleSpousalAgeGap = 30;

    private static void CheckLargeSpousalAgeGap(GedDocument doc, List<GedDiagnostic> diags)
    {
        foreach (var fam in doc.Records.Where(r => r.Tag == "FAM"))
        {
            int? husbBirth = SpouseBirthYear(doc, fam.FirstChild("HUSB")?.Value);
            int? wifeBirth = SpouseBirthYear(doc, fam.FirstChild("WIFE")?.Value);
            if (husbBirth is not int hb || wifeBirth is not int wb) continue;

            int gap = Math.Abs(hb - wb);
            if (gap > MaxPlausibleSpousalAgeGap)
                diags.Add(new GedDiagnostic(GedDiagnosticSeverity.Warning, "GEN104",
                    $"{fam.Xref} spouses are {gap} years apart in age (born {hb} and {wb})", fam.Xref, "FAM"));
        }
    }

    private static int? SpouseBirthYear(GedDocument doc, string? xref) =>
        xref is not null && xref != GedRecord.VoidPointer && doc.ByXref.TryGetValue(xref, out var indi) && indi.Tag == "INDI"
            ? YearOf(indi.ChildrenByTag("BIRT").LastOrDefault())
            : null;

    // -------------------------------------------------------------------
    // GEN105 — large span between first and last child
    // -------------------------------------------------------------------

    // Gramps LargeChildrenSpan's own default (25y), unchanged — same
    // reasoning as GEN104: no FamilySearch equivalent to prefer instead.
    private const int MaxPlausibleChildrenSpan = 25;

    private static void CheckLargeChildrenSpan(GedDocument doc, List<GedDiagnostic> diags)
    {
        foreach (var fam in doc.Records.Where(r => r.Tag == "FAM"))
        {
            var childYears = fam.ChildrenByTag("CHIL")
                .Where(c => c.Value != GedRecord.VoidPointer)
                .Select(c => doc.ByXref.GetValueOrDefault(c.Value))
                .Where(child => child is { Tag: "INDI" })
                .Select(child => YearOf(child!.ChildrenByTag("BIRT").LastOrDefault()))
                .Where(y => y is not null)
                .Select(y => y!.Value)
                .ToList();
            if (childYears.Count < 2) continue;

            int span = childYears.Max() - childYears.Min();
            if (span > MaxPlausibleChildrenSpan)
                diags.Add(new GedDiagnostic(GedDiagnosticSeverity.Warning, "GEN105",
                    $"{fam.Xref} children span {span} years, from {childYears.Min()} to {childYears.Max()}",
                    fam.Xref, "CHIL"));
        }
    }

    // -------------------------------------------------------------------
    // GEN111 — children/marriage mismatch
    // -------------------------------------------------------------------

    // GEDCOM's structured way to assert a *negative* result is NCHI 0 (a
    // family's or person's own stated child count) or a never-married fact.
    // There is no single standard tag for the latter; this reads the common
    // "_MSTAT" extension tag (GEDCOM 7's underscore-prefixed convention for
    // an undeclared extension) with a NO/never-married value, per the design
    // doc's own hedge that this is a study-level convention, not a GEDCOM one.
    private static void CheckChildrenOrMarriageMismatch(GedDocument doc, List<GedDiagnostic> diags)
    {
        foreach (var fam in doc.Records.Where(r => r.Tag == "FAM"))
        {
            var nchi = fam.FirstChild("NCHI");
            if (nchi is null || nchi.Value != "0") continue;
            if (fam.ChildrenByTag("CHIL").Any(c => c.Value != GedRecord.VoidPointer))
                diags.Add(new GedDiagnostic(GedDiagnosticSeverity.Warning, "GEN111",
                    $"{fam.Xref} carries NCHI 0 but has one or more CHIL links", fam.Xref, "NCHI"));
        }

        foreach (var indi in doc.Records.Where(r => r.Tag == "INDI"))
        {
            if (indi.Xref is null) continue;

            var nchi = indi.FirstChild("NCHI");
            if (nchi is not null && nchi.Value == "0")
            {
                bool hasChild = indi.ChildrenByTag("FAMS")
                    .Where(f => f.Value != GedRecord.VoidPointer)
                    .Select(f => doc.ByXref.GetValueOrDefault(f.Value))
                    .Any(fam => fam is { Tag: "FAM" } && fam.ChildrenByTag("CHIL").Any(c => c.Value != GedRecord.VoidPointer));
                if (hasChild)
                    diags.Add(new GedDiagnostic(GedDiagnosticSeverity.Warning, "GEN111",
                        $"{indi.Xref} carries NCHI 0 but has a child through a linked family", indi.Xref, "NCHI"));
            }

            bool neverMarried = indi.ChildrenByTag("_MSTAT")
                .Any(m => m.FullValue().Contains("NO", StringComparison.OrdinalIgnoreCase)
                          || m.FullValue().Contains("NEVER MARRIED", StringComparison.OrdinalIgnoreCase));
            if (neverMarried && indi.ChildrenByTag("FAMS").Any(f => f.Value != GedRecord.VoidPointer))
                diags.Add(new GedDiagnostic(GedDiagnosticSeverity.Warning, "GEN111",
                    $"{indi.Xref} carries a never-married fact but has a FAMS link", indi.Xref, "_MSTAT"));
        }
    }

    // -------------------------------------------------------------------
    // GEN115 — implausible age at death or burial
    // -------------------------------------------------------------------

    // Only fires when there IS a death-adjacent record (DEAT, or BURI when
    // no DEAT is recorded) to compute an age from. An earlier version of
    // this rule (GEN113) also flagged a person with *no* DEAT at all whose
    // birth year alone would make them implausibly old today -- retired: an
    // unresearched death date is not itself a data problem in a tree with
    // many not-yet-researched deaths, and flagging it on birth year alone
    // drowned out real findings. Same 120y bound as before: the oldest
    // reliably documented human lifespan sits around 122 years, so 120
    // stays a near-zero-false-positive floor.
    private const int MaxPlausibleAgeAtDeath = 120;

    private static void CheckImplausibleAgeAtDeath(GedDocument doc, List<GedDiagnostic> diags)
    {
        foreach (var indi in doc.Records.Where(r => r.Tag == "INDI"))
        {
            if (indi.Xref is null) continue;
            int? birth = YearOf(indi.ChildrenByTag("BIRT").LastOrDefault());
            if (birth is not int by) continue;

            int? deathOrBurialYear = YearOf(indi.ChildrenByTag("DEAT").LastOrDefault());
            string sourceTag = "DEAT";
            if (deathOrBurialYear is null)
            {
                deathOrBurialYear = YearOf(indi.ChildrenByTag("BURI").LastOrDefault());
                sourceTag = "BURI";
            }
            if (deathOrBurialYear is not int ey) continue;

            int age = ey - by;
            if (age > MaxPlausibleAgeAtDeath)
                diags.Add(new GedDiagnostic(GedDiagnosticSeverity.Warning, "GEN115",
                    $"{indi.Xref} would be {age} at {sourceTag} ({by}-{ey})", indi.Xref, sourceTag));
        }
    }

    // -------------------------------------------------------------------
    // GEN114 — died too young for the facts attached
    // -------------------------------------------------------------------

    private const int MaxDeathAgeForMarriageOrChildren = 8;

    private static void CheckDiedTooYoungForFacts(GedDocument doc, List<GedDiagnostic> diags)
    {
        foreach (var indi in doc.Records.Where(r => r.Tag == "INDI"))
        {
            if (indi.Xref is null) continue;
            int? birth = YearOf(indi.ChildrenByTag("BIRT").LastOrDefault());
            int? death = YearOf(indi.ChildrenByTag("DEAT").LastOrDefault());
            if (birth is not int by || death is not int dy) continue;

            int age = dy - by;
            if (age > MaxDeathAgeForMarriageOrChildren) continue;

            bool hasMarriageOrChild = indi.ChildrenByTag("FAMS")
                .Where(f => f.Value != GedRecord.VoidPointer)
                .Select(f => doc.ByXref.GetValueOrDefault(f.Value))
                .Any(fam => fam is { Tag: "FAM" } &&
                    (fam.FirstChild("MARR") is not null || fam.ChildrenByTag("CHIL").Any(c => c.Value != GedRecord.VoidPointer)));

            if (hasMarriageOrChild)
                diags.Add(new GedDiagnostic(GedDiagnosticSeverity.Warning, "GEN114",
                    $"{indi.Xref} died at age {age} ({dy}) but has a marriage or child recorded", indi.Xref, "DEAT"));
        }
    }

    // -------------------------------------------------------------------
    // GEN301 — possible duplicate INDI
    // -------------------------------------------------------------------

    // Reuses PersonRecordIndex.Build + PersonMatchCore.Match, the same
    // scoring find_person and the changeset-time duplicate detector use.
    // Bucketed by normalized surname first (cheap, O(n)) so scoring only
    // runs within same-surname groups rather than all pairs in the whole
    // document — see design doc's Integration section for why that matters.
    //
    // That still leaves an all-pairs sweep *within* a bucket: every member
    // scored as the query ("self") against every other member. Fine for a
    // small bucket, but a real family study's own most common surname can
    // run to thousands of members, and O(bucket^2) there is minutes of work
    // -- run twice (before and after) on every check_plausibility/
    // validate_changeset/apply_changeset call, regardless of what the
    // changeset touches. duplicateCheckScope, when supplied, runs only
    // members in scope as "self" -- exactly the one-call-per-person query
    // find_person already does, still scored against the bucket's full
    // membership, so it finds every pair a full sweep would that involves a
    // scoped person. A pair where neither member is in scope is necessarily
    // one this changeset didn't touch, so it can't be a *new* finding for
    // ChangesetApplier's before/after diff either way -- omitting it from
    // the scoped sweep costs that diff nothing.
    private const double DuplicateWarningFloor = 70.0;

    static readonly Lazy<NicknameDirectory> Nicknames = new(NicknameDirectory.LoadEmbedded);
    static readonly PersonMatchCore MatchCore = new();

    private static void CheckPossibleDuplicates(
        GedDocument doc, List<GedDiagnostic> diags, IReadOnlySet<string>? duplicateCheckScope,
        CancellationToken cancellationToken)
    {
        var candidates = PersonRecordIndex.Build(doc);
        if (candidates.Count < 2) return;

        var flaggedPairs = new HashSet<(string, string)>();
        foreach (var bucket in candidates.GroupBy(c => c.NormalizedSurname, StringComparer.Ordinal))
        {
            var members = bucket.ToList();
            if (members.Count < 2) continue;

            var selves = duplicateCheckScope is null
                ? members
                : members.Where(c => duplicateCheckScope.Contains(c.Id)).ToList();

            foreach (var self in selves)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var others = members.Where(c => c.Id != self.Id).ToList();
                var hints = HintsFor(self);
                var outcome = MatchCore.Match(others, self.DisplayName, hints, Nicknames.Value, maxResults: 1);
                // Single requires FinalScore >= 90 with a 10-point margin over the
                // runner-up (PersonMatchCore's own hard-match bar) -- gating on it
                // here would make DuplicateWarningFloor dead code, since nothing
                // between 70 and 90 would ever classify as Single. Candidates (an
                // ambiguous recall set) still carries a real top score in Matches;
                // only None (nothing cleared the recall gate at all) has none.
                if (outcome.PersonMatchType == PersonMatchType.None) continue;

                var match = outcome.Matches[0];
                if (match.FinalScore < DuplicateWarningFloor) continue;

                var pairKey = string.CompareOrdinal(self.Id, match.Id) < 0
                    ? (self.Id, match.Id) : (match.Id, self.Id);
                if (!flaggedPairs.Add(pairKey)) continue;

                diags.Add(new GedDiagnostic(GedDiagnosticSeverity.Warning, "GEN301",
                    $"{pairKey.Item1} and {pairKey.Item2} score as a probable identity match ({match.FinalScore:0.0})",
                    pairKey.Item1, "INDI"));
            }
        }
    }

    private static MatchHints HintsFor(PersonMatchCandidate c)
    {
        EventHint? birth = c.Birth is { } b ? new EventHint(b.Year, b.NormalizedPlace) : null;
        ParentsHint? parents = c.Parents is { } p ? new ParentsHint(p.NormalizedFatherName, p.NormalizedMotherName) : null;
        SpouseHint? spouse = c.Marriages.Count > 0 ? new SpouseHint(c.Marriages[0].NormalizedSpouseName) : null;
        return new MatchHints(Birth: birth, Parents: parents, Spouse: spouse);
    }

    // -------------------------------------------------------------------
    // GEN302 — ancestor cycle (Error: logically impossible, not a judgment call)
    // -------------------------------------------------------------------

    private static void CheckAncestorCycles(GedDocument doc, List<GedDiagnostic> diags)
    {
        // Cycle-free subtrees are memoized across the whole run so a shared
        // ancestor reached from many descendants (ordinary pedigree
        // collapse, not a cycle) is walked once, not once per descendant.
        var confirmedAcyclic = new HashSet<string>(StringComparer.Ordinal);
        foreach (var indi in doc.Records.Where(r => r.Tag == "INDI"))
        {
            if (indi.Xref is null || confirmedAcyclic.Contains(indi.Xref)) continue;
            var path = new HashSet<string>(StringComparer.Ordinal);
            string? revisited = FindAncestorCycle(doc, indi.Xref, path, confirmedAcyclic);
            if (revisited is not null)
                diags.Add(new GedDiagnostic(GedDiagnosticSeverity.Error, "GEN302",
                    $"{indi.Xref} is its own ancestor: {revisited} reappears in its FAMC parent chain",
                    indi.Xref, "FAMC"));
        }
    }

    /// <summary>
    /// Depth-first walk up <paramref name="xref"/>'s FAMC parent chain.
    /// Returns the xref revisited within the current path (a cycle), or
    /// null when this xref's whole ancestor subtree is acyclic. A person
    /// reachable via two different, non-overlapping branches (cousin
    /// marriage) is never flagged — <paramref name="path"/> only tracks the
    /// current branch's own ancestors, backtracked on return.
    /// </summary>
    private static string? FindAncestorCycle(
        GedDocument doc, string xref, HashSet<string> path, HashSet<string> confirmedAcyclic)
    {
        if (!path.Add(xref)) return xref;

        if (!confirmedAcyclic.Contains(xref) &&
            doc.ByXref.TryGetValue(xref, out var indi) && indi.Tag == "INDI")
        {
            foreach (var famcLink in indi.ChildrenByTag("FAMC"))
            {
                if (famcLink.Value == GedRecord.VoidPointer) continue;
                if (!doc.ByXref.TryGetValue(famcLink.Value, out var fam) || fam.Tag != "FAM") continue;

                foreach (var parentXref in new[] { fam.FirstChild("HUSB")?.Value, fam.FirstChild("WIFE")?.Value })
                {
                    if (parentXref is null || parentXref == GedRecord.VoidPointer) continue;
                    string? cycle = FindAncestorCycle(doc, parentXref, path, confirmedAcyclic);
                    if (cycle is not null) { path.Remove(xref); return cycle; }
                }
            }
            confirmedAcyclic.Add(xref);
        }

        path.Remove(xref);
        return null;
    }

    // -------------------------------------------------------------------
    // GEN303 — too many children
    // -------------------------------------------------------------------

    // Gramps TooManyChildren's own thresholds (12 mother / 15 father),
    // unchanged. Counted per parent across every FAM they're a spouse in
    // (deduped by child xref), not per family — Gramps' own rule is about a
    // person's total recorded children, not one marriage's. Requires a
    // recorded SEX (M or F): GEDCOM 7 decouples the HUSB/WIFE role tags from
    // sex (see the design doc's Not-adopted section on FemaleHusband/MaleWife),
    // so the role tag alone isn't a safe stand-in for "mother" or "father" here.
    private const int MaxPlausibleChildrenForMother = 12;
    private const int MaxPlausibleChildrenForFather = 15;

    private static void CheckTooManyChildren(GedDocument doc, List<GedDiagnostic> diags)
    {
        foreach (var indi in doc.Records.Where(r => r.Tag == "INDI"))
        {
            if (indi.Xref is null) continue;
            string? sex = indi.FirstChild("SEX")?.Value;
            if (sex is not ("M" or "F")) continue;
            int threshold = sex == "F" ? MaxPlausibleChildrenForMother : MaxPlausibleChildrenForFather;

            var childXrefs = new HashSet<string>(StringComparer.Ordinal);
            foreach (var famsLink in indi.ChildrenByTag("FAMS"))
            {
                if (famsLink.Value == GedRecord.VoidPointer) continue;
                if (!doc.ByXref.TryGetValue(famsLink.Value, out var fam) || fam.Tag != "FAM") continue;
                foreach (var chil in fam.ChildrenByTag("CHIL"))
                    if (chil.Value != GedRecord.VoidPointer) childXrefs.Add(chil.Value);
            }

            if (childXrefs.Count > threshold)
                diags.Add(new GedDiagnostic(GedDiagnosticSeverity.Warning, "GEN303",
                    $"{indi.Xref} has {childXrefs.Count} children recorded across linked families, exceeds {threshold}",
                    indi.Xref, "FAMS"));
        }
    }

    // -------------------------------------------------------------------
    // GEN304 — belongs to more than one parent family
    // -------------------------------------------------------------------

    // Gramps MultipleParents, unchanged (>1 FAMC). A second FAMC is often
    // legitimate (adoption, step-parentage) -- flagged as a Warning worth a
    // look, same as Gramps treats it, not a claim that it's wrong.
    private static void CheckMultipleParentFamilies(GedDocument doc, List<GedDiagnostic> diags)
    {
        foreach (var indi in doc.Records.Where(r => r.Tag == "INDI"))
        {
            if (indi.Xref is null) continue;
            var famcXrefs = indi.ChildrenByTag("FAMC")
                .Where(f => f.Value != GedRecord.VoidPointer)
                .Select(f => f.Value)
                .ToHashSet(StringComparer.Ordinal);
            if (famcXrefs.Count > 1)
                diags.Add(new GedDiagnostic(GedDiagnosticSeverity.Warning, "GEN304",
                    $"{indi.Xref} belongs to {famcXrefs.Count} parent families (FAMC): {string.Join(", ", famcXrefs.OrderBy(x => x, StringComparer.Ordinal))}",
                    indi.Xref, "FAMC"));
        }
    }

    // -------------------------------------------------------------------
    // GEN305 — many spouses
    // -------------------------------------------------------------------

    // Gramps MarriedOften's own default (>3 distinct spouses), unchanged.
    private const int MaxPlausibleSpouseCount = 3;

    private static void CheckManySpouses(GedDocument doc, List<GedDiagnostic> diags)
    {
        foreach (var indi in doc.Records.Where(r => r.Tag == "INDI"))
        {
            if (indi.Xref is null) continue;
            var spouseXrefs = new HashSet<string>(StringComparer.Ordinal);
            foreach (var famsLink in indi.ChildrenByTag("FAMS"))
            {
                if (famsLink.Value == GedRecord.VoidPointer) continue;
                if (!doc.ByXref.TryGetValue(famsLink.Value, out var fam) || fam.Tag != "FAM") continue;
                string? husb = fam.FirstChild("HUSB")?.Value;
                string? wife = fam.FirstChild("WIFE")?.Value;
                string? spouse = husb == indi.Xref ? wife : wife == indi.Xref ? husb : null;
                if (spouse is not null && spouse != GedRecord.VoidPointer) spouseXrefs.Add(spouse);
            }

            if (spouseXrefs.Count > MaxPlausibleSpouseCount)
                diags.Add(new GedDiagnostic(GedDiagnosticSeverity.Warning, "GEN305",
                    $"{indi.Xref} has {spouseXrefs.Count} distinct spouses recorded, exceeds {MaxPlausibleSpouseCount}",
                    indi.Xref, "FAMS"));
        }
    }

    // -------------------------------------------------------------------
    // GEN306 — disconnected individual
    // -------------------------------------------------------------------

    // Gramps Disconnected: no FAMC and no FAMS anywhere in the document. A
    // legitimate state for a deliberately-added standalone research
    // candidate or a tree's own root ancestor, same as Gramps treats it --
    // worth a look, not a claim that it's wrong.
    private static void CheckDisconnectedIndividual(GedDocument doc, List<GedDiagnostic> diags)
    {
        foreach (var indi in doc.Records.Where(r => r.Tag == "INDI"))
        {
            if (indi.Xref is null) continue;
            bool hasFamc = indi.ChildrenByTag("FAMC").Any(f => f.Value != GedRecord.VoidPointer);
            bool hasFams = indi.ChildrenByTag("FAMS").Any(f => f.Value != GedRecord.VoidPointer);
            if (!hasFamc && !hasFams)
                diags.Add(new GedDiagnostic(GedDiagnosticSeverity.Warning, "GEN306",
                    $"{indi.Xref} has no FAMC or FAMS link to any family", indi.Xref, "INDI"));
        }
    }

    // -------------------------------------------------------------------
    // GEN401 — invalid characters in a name
    // -------------------------------------------------------------------

    // Not a FamilySearch/Gramps rule -- both sources' own "invalid
    // characters" checks key off proprietary standardization data no parsed
    // GedDocument carries (see design doc's Not-adopted section). This is a
    // self-specified policy instead: a name's payload (both NAME.FullValue()
    // and its structural "/" surname delimiters stripped out) may contain
    // Unicode letters -- any script, diacritics included -- plus whitespace,
    // a hyphen, an apostrophe, and a period (a middle-initial or suffix
    // abbreviation, e.g. "Albin H. /Test/" or "/Test/ Jr."). Anything else
    // (digits, emoji, other punctuation/symbols) is flagged. Runs per-Rune,
    // not per-char, so a surrogate-pair emoji is reported once, not as two
    // orphaned halves.
    private static void CheckInvalidNameCharacters(GedDocument doc, List<GedDiagnostic> diags)
    {
        foreach (var indi in doc.Records.Where(r => r.Tag == "INDI"))
        {
            if (indi.Xref is null) continue;
            foreach (var nameRec in indi.ChildrenByTag("NAME"))
            {
                string raw = nameRec.FullValue();
                string nameOnly = raw.Replace("/", "");
                var invalid = nameOnly.EnumerateRunes()
                    .Where(r => !IsAllowedNameRune(r))
                    .Select(r => r.ToString())
                    .Distinct()
                    .ToList();
                if (invalid.Count == 0) continue;

                diags.Add(new GedDiagnostic(GedDiagnosticSeverity.Warning, "GEN401",
                    $"{indi.Xref} NAME \"{raw}\" contains disallowed character(s): {string.Join(" ", invalid)}",
                    indi.Xref, "NAME"));
            }
        }
    }

    private static bool IsAllowedNameRune(Rune r) =>
        Rune.IsWhiteSpace(r) || Rune.IsLetter(r) || r.Value is '-' or '\'' or '.';

    // -------------------------------------------------------------------
    // GEN402 — sex not specified
    // -------------------------------------------------------------------

    // FamilySearch "Male or Female Is Required" -- but as a soft Warning, not
    // FamilySearch's own "Required"/non-dismissible framing: SEX is an
    // optional GEDCOM 7 field, so this is a completeness nudge, not a
    // re-assertion that every record must carry it. Only an absent SEX
    // structure is flagged -- an explicit "U" (undetermined) or "X"
    // (intersex/other) is specified, just not binary, and isn't this rule's
    // concern.
    private static void CheckMissingSex(GedDocument doc, List<GedDiagnostic> diags)
    {
        foreach (var indi in doc.Records.Where(r => r.Tag == "INDI"))
        {
            if (indi.Xref is null) continue;
            if (indi.FirstChild("SEX") is null)
                diags.Add(new GedDiagnostic(GedDiagnosticSeverity.Warning, "GEN402",
                    $"{indi.Xref} has no SEX recorded", indi.Xref, "INDI"));
        }
    }

    // -------------------------------------------------------------------
    // GEN403 — HUSB/WIFE role doesn't match the linked person's own SEX
    // -------------------------------------------------------------------

    // Gramps FemaleHusband/MaleWife, as one combined rule -- but as a soft
    // Warning, not a claim that the record is wrong: GEDCOM 7 explicitly
    // decouples the HUSB/WIFE role tags from sex (they're legacy
    // partner-1/partner-2 labels, kept for compatibility), so a same-sex
    // couple or a role recorded to match a study's own convention is not an
    // error. It's still worth a look -- often the sign of a swapped role or
    // a stale SEX value -- which is exactly the "flag, don't reject" posture
    // every other rule here already takes.
    private static void CheckHusbandWifeSexMismatch(GedDocument doc, List<GedDiagnostic> diags)
    {
        foreach (var fam in doc.Records.Where(r => r.Tag == "FAM"))
        {
            CheckRoleSex(doc, diags, fam, "HUSB", mismatchedSex: "F");
            CheckRoleSex(doc, diags, fam, "WIFE", mismatchedSex: "M");
        }
    }

    private static void CheckRoleSex(
        GedDocument doc, List<GedDiagnostic> diags, GedRecord fam, string roleTag, string mismatchedSex)
    {
        string? xref = fam.FirstChild(roleTag)?.Value;
        if (xref is null || xref == GedRecord.VoidPointer) return;
        if (!doc.ByXref.TryGetValue(xref, out var indi) || indi.Tag != "INDI") return;
        if (indi.FirstChild("SEX")?.Value != mismatchedSex) return;

        diags.Add(new GedDiagnostic(GedDiagnosticSeverity.Warning, "GEN403",
            $"{fam.Xref} {roleTag} {xref} has SEX {mismatchedSex}", fam.Xref, roleTag));
    }

    // -------------------------------------------------------------------
    // Shared helpers
    // -------------------------------------------------------------------

    private static int? YearOf(GedRecord? gedEvent)
    {
        if (gedEvent is null) return null;
        int y = GedDate.ParseYear(gedEvent.ChildrenByTag("DATE").LastOrDefault()?.FullValue());
        return y != 0 ? y : null;
    }
}
