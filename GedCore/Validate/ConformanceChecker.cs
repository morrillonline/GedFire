using System.Text.RegularExpressions;

namespace GedCore.Validate;

/// <summary>How serious a <see cref="GedDiagnostic"/> is.</summary>
public enum GedDiagnosticSeverity { Error, Warning, Info }

/// <summary>One conformance finding from <see cref="ConformanceChecker.Check"/>.</summary>
public sealed record GedDiagnostic(
    GedDiagnosticSeverity Severity,
    string Code,          // stable, e.g. "GED001"
    string Message,       // human text incl. offending value
    string? Xref,         // owning level-0 record, if any
    string Tag);          // offending tag

/// <summary>
/// GEDCOM 7.0.18 conformance checks the codebase otherwise performs nowhere.
/// Each rule is implemented by its own private <c>Check&lt;RuleName&gt;</c>
/// method so the GED001-GED014 rules stay independently testable.
/// </summary>
public static class ConformanceChecker
{
    private static readonly Regex StandardTagCharset  = new(@"^[A-Z0-9]+$",   RegexOptions.Compiled);
    private static readonly Regex ExtensionTagCharset  = new(@"^_[A-Z0-9_]+$", RegexOptions.Compiled);

    private static readonly Dictionary<string, string> PointerTargetTag = new()
    {
        ["FAMS"] = "FAM", ["FAMC"] = "FAM",
        ["HUSB"] = "INDI", ["WIFE"] = "INDI", ["CHIL"] = "INDI", ["ALIA"] = "INDI", ["ASSO"] = "INDI",
        ["SOUR"] = "SOUR",
        ["SNOTE"] = "SNOTE",
        ["REPO"] = "REPO",
        ["OBJE"] = "OBJE",
        ["SUBM"] = "SUBM",
    };

    private static readonly HashSet<string> ValidSexValues = new(StringComparer.Ordinal) { "M", "F", "X", "U" };
    private static readonly HashSet<string> ValidQuayValues = new(StringComparer.Ordinal) { "0", "1", "2", "3" };

    /// <summary>
    /// Run every rule against <paramref name="doc"/>, sorted by severity then
    /// code. <paramref name="cancellationToken"/> is checked between rules --
    /// each is its own flat O(n) pass, so this is fine-grained enough for a
    /// cancelled MCP request to actually stop the work instead of running to
    /// completion regardless.
    /// </summary>
    public static IReadOnlyList<GedDiagnostic> Check(GedDocument doc, CancellationToken cancellationToken = default)
    {
        var diags = new List<GedDiagnostic>();

        cancellationToken.ThrowIfCancellationRequested();
        CheckTagCharset(doc, diags);
        cancellationToken.ThrowIfCancellationRequested();
        CheckLevelIncrements(doc, diags);
        cancellationToken.ThrowIfCancellationRequested();
        CheckContOrdering(doc, diags);
        cancellationToken.ThrowIfCancellationRequested();
        CheckPointerResolutionAndType(doc, diags);
        cancellationToken.ThrowIfCancellationRequested();
        CheckSelfReferentialAlia(doc, diags);
        cancellationToken.ThrowIfCancellationRequested();
        CheckExidWithoutType(doc, diags);
        cancellationToken.ThrowIfCancellationRequested();
        CheckDeprecatedAddressLines(doc, diags);
        cancellationToken.ThrowIfCancellationRequested();
        CheckDuplicateFamilyLinks(doc, diags);
        cancellationToken.ThrowIfCancellationRequested();
        CheckUndeclaredExtensionTags(doc, diags);
        cancellationToken.ThrowIfCancellationRequested();
        CheckRemoved70Structures(doc, diags);
        cancellationToken.ThrowIfCancellationRequested();
        CheckSexAndQuayRanges(doc, diags);
        cancellationToken.ThrowIfCancellationRequested();
        CheckObjeFileForm(doc, diags);
        cancellationToken.ThrowIfCancellationRequested();
        CheckSourceObjeCycle(doc, diags);

        return [.. diags.OrderBy(d => d.Severity).ThenBy(d => d.Code, StringComparer.Ordinal)];
    }

    /// <summary>
    /// Subproject D2's post-apply gate: diagnostics present in <paramref name="after"/>
    /// whose (Code, Xref, Tag, Message) occurrence count exceeds their count in
    /// <paramref name="before"/> — i.e. genuinely new findings a changeset introduced,
    /// not ones it merely left alone. Decreased or unchanged counts (an improvement,
    /// or a pre-existing Warning the baseline already carried) are never flagged.
    /// </summary>
    internal static List<GedDiagnostic> DiffDiagnostics(
        IReadOnlyList<GedDiagnostic> before, IReadOnlyList<GedDiagnostic> after)
    {
        var beforeCounts = new Dictionary<(string Code, string? Xref, string Tag, string Message), int>();
        foreach (var d in before)
        {
            var key = (d.Code, d.Xref, d.Tag, d.Message);
            beforeCounts[key] = beforeCounts.GetValueOrDefault(key) + 1;
        }

        var seenInAfter = new Dictionary<(string Code, string? Xref, string Tag, string Message), int>();
        var increased = new List<GedDiagnostic>();
        foreach (var d in after)
        {
            var key = (d.Code, d.Xref, d.Tag, d.Message);
            int seen = seenInAfter[key] = seenInAfter.GetValueOrDefault(key) + 1;
            if (seen > beforeCounts.GetValueOrDefault(key))
                increased.Add(d);
        }
        return increased;
    }

    // -------------------------------------------------------------------
    // GED001 — tag charset
    // -------------------------------------------------------------------

    private static void CheckTagCharset(GedDocument doc, List<GedDiagnostic> diags)
    {
        foreach (var rec in AllRecords(doc))
        {
            bool valid = rec.Tag.StartsWith('_')
                ? ExtensionTagCharset.IsMatch(rec.Tag)
                : StandardTagCharset.IsMatch(rec.Tag);
            if (!valid)
                diags.Add(new GedDiagnostic(GedDiagnosticSeverity.Error, "GED001",
                    $"tag '{rec.Tag}' does not match the required charset ([A-Z0-9]+, or _[A-Z0-9_]+ for an extension tag)",
                    OwningXref(rec), rec.Tag));
        }
    }

    // -------------------------------------------------------------------
    // GED002 — level increments
    // -------------------------------------------------------------------

    private static void CheckLevelIncrements(GedDocument doc, List<GedDiagnostic> diags)
    {
        foreach (var rec in AllRecords(doc))
        {
            if (rec.Parent is null)
            {
                if (rec.Level != 0)
                    diags.Add(new GedDiagnostic(GedDiagnosticSeverity.Error, "GED002",
                        $"orphaned {rec.Tag} at level {rec.Level}: no record found at level {rec.Level - 1}",
                        OwningXref(rec), rec.Tag));
            }
            else if (rec.Level > rec.Parent.Level + 1)
            {
                diags.Add(new GedDiagnostic(GedDiagnosticSeverity.Error, "GED002",
                    $"{rec.Tag} at level {rec.Level} exceeds its parent's level {rec.Parent.Level} + 1",
                    OwningXref(rec), rec.Tag));
            }
        }
    }

    // -------------------------------------------------------------------
    // GED003 — CONT ordering
    // -------------------------------------------------------------------

    private static void CheckContOrdering(GedDocument doc, List<GedDiagnostic> diags)
    {
        foreach (var parent in AllRecords(doc))
        {
            bool seenSubstructure = false;
            foreach (var child in parent.Children)
            {
                if (child.Tag is "CONT" or "CONC")
                {
                    if (seenSubstructure && child.Tag == "CONT")
                        diags.Add(new GedDiagnostic(GedDiagnosticSeverity.Error, "GED003",
                            "CONT follows a substructure sibling; CONT must immediately continue its head line's value",
                            OwningXref(child), "CONT"));
                }
                else
                {
                    seenSubstructure = true;
                }
            }
        }
    }

    // -------------------------------------------------------------------
    // GED004 / GED005 — pointer resolution and target type
    // -------------------------------------------------------------------

    private static void CheckPointerResolutionAndType(GedDocument doc, List<GedDiagnostic> diags)
    {
        foreach (var rec in AllRecords(doc))
        {
            if (!rec.IsPointerValue || rec.Value == GedRecord.VoidPointer) continue;

            if (!doc.ByXref.TryGetValue(rec.Value, out var target))
            {
                diags.Add(new GedDiagnostic(GedDiagnosticSeverity.Error, "GED004",
                    $"{rec.Tag} pointer {rec.Value} does not resolve to any record", OwningXref(rec), rec.Tag));
                continue;
            }

            if (PointerTargetTag.TryGetValue(rec.Tag, out var expected) && target.Tag != expected)
                diags.Add(new GedDiagnostic(GedDiagnosticSeverity.Error, "GED005",
                    $"{rec.Tag} pointer {rec.Value} targets a {target.Tag} record, expected {expected}",
                    OwningXref(rec), rec.Tag));
        }
    }

    // -------------------------------------------------------------------
    // GED006 — self-referential ALIA
    // -------------------------------------------------------------------

    private static void CheckSelfReferentialAlia(GedDocument doc, List<GedDiagnostic> diags)
    {
        foreach (var rec in AllRecords(doc))
        {
            if (rec.Tag != "ALIA" || !rec.IsPointerValue) continue;
            string? owner = OwningXref(rec);
            if (owner is not null && owner == rec.Value)
                diags.Add(new GedDiagnostic(GedDiagnosticSeverity.Error, "GED006",
                    $"ALIA {rec.Value} is self-referential", owner, rec.Tag));
        }
    }

    // -------------------------------------------------------------------
    // GED007 — EXID without TYPE
    // -------------------------------------------------------------------

    private static void CheckExidWithoutType(GedDocument doc, List<GedDiagnostic> diags)
    {
        foreach (var rec in AllRecords(doc))
        {
            if (rec.Tag == "EXID" && rec.FirstChild("TYPE") is null)
                diags.Add(new GedDiagnostic(GedDiagnosticSeverity.Warning, "GED007",
                    "EXID without a TYPE substructure (deprecated since 7.0.6)", OwningXref(rec), rec.Tag));
        }
    }

    // -------------------------------------------------------------------
    // GED008 — deprecated address lines
    // -------------------------------------------------------------------

    private static void CheckDeprecatedAddressLines(GedDocument doc, List<GedDiagnostic> diags)
    {
        foreach (var rec in AllRecords(doc))
        {
            if (rec.Tag is "ADR1" or "ADR2" or "ADR3")
                diags.Add(new GedDiagnostic(GedDiagnosticSeverity.Warning, "GED008",
                    $"{rec.Tag} is deprecated (since 7.0.13)", OwningXref(rec), rec.Tag));
        }
    }

    // -------------------------------------------------------------------
    // GED009 — duplicate FAMC / CHIL links
    // -------------------------------------------------------------------

    private static void CheckDuplicateFamilyLinks(GedDocument doc, List<GedDiagnostic> diags)
    {
        foreach (var rec in doc.Records)
        {
            // @VOID@ is exempt: several "1 CHIL @VOID@" lines legitimately
            // record several placeholder children (it names no record, so no
            // link is duplicated).
            if (rec.Tag == "INDI")
            {
                foreach (var group in rec.ChildrenByTag("FAMC").GroupBy(c => c.Value)
                             .Where(g => g.Count() > 1 && g.Key != GedRecord.VoidPointer))
                    diags.Add(new GedDiagnostic(GedDiagnosticSeverity.Warning, "GED009",
                        $"duplicate FAMC {group.Key} on {rec.Xref}", rec.Xref, "FAMC"));
            }
            else if (rec.Tag == "FAM")
            {
                foreach (var group in rec.ChildrenByTag("CHIL").GroupBy(c => c.Value)
                             .Where(g => g.Count() > 1 && g.Key != GedRecord.VoidPointer))
                    diags.Add(new GedDiagnostic(GedDiagnosticSeverity.Warning, "GED009",
                        $"duplicate CHIL {group.Key} on {rec.Xref}", rec.Xref, "CHIL"));
            }
        }
    }

    // -------------------------------------------------------------------
    // GED010 — undeclared extension tags
    // -------------------------------------------------------------------

    private static void CheckUndeclaredExtensionTags(GedDocument doc, List<GedDiagnostic> diags)
    {
        var declared = new HashSet<string>(StringComparer.Ordinal);
        var head = doc.Records.FirstOrDefault(r => r.Tag == "HEAD");
        var schma = head?.FirstChild("SCHMA");
        if (schma is not null)
        {
            foreach (var tagDecl in schma.ChildrenByTag("TAG"))
            {
                string payload = tagDecl.FullValue();
                int sp = payload.IndexOf(' ');
                declared.Add(sp > 0 ? payload[..sp] : payload);
            }
        }

        foreach (var rec in AllRecords(doc))
        {
            if (rec.Tag.StartsWith('_') && !declared.Contains(rec.Tag))
                diags.Add(new GedDiagnostic(GedDiagnosticSeverity.Warning, "GED010",
                    $"extension tag {rec.Tag} used but not declared in HEAD.SCHMA", OwningXref(rec), rec.Tag));
        }
    }

    // -------------------------------------------------------------------
    // GED011 — structures removed in 7.0 (7.x files only)
    // -------------------------------------------------------------------

    private static void CheckRemoved70Structures(GedDocument doc, List<GedDiagnostic> diags)
    {
        var head = doc.Records.FirstOrDefault(r => r.Tag == "HEAD");
        string? vers = head?.FirstChild("GEDC")?.FirstChild("VERS")?.Value;
        if (vers is null || !vers.StartsWith('7')) return;   // a 5.5 document is not diagnosed

        foreach (var rec in AllRecords(doc))
        {
            if (rec.Tag == "CONC")
                diags.Add(new GedDiagnostic(GedDiagnosticSeverity.Warning, "GED011",
                    "CONC is removed in GEDCOM 7.0 (use CONT or a longer line)", OwningXref(rec), rec.Tag));

            // HEAD.SOUR identifies the originating product (e.g. "1 SOUR FTM") and is
            // always free text — a distinct structure from a citation SOUR pointer.
            if (rec.Tag == "SOUR" && rec.Value.Length > 0 && !rec.IsPointerValue && rec.Parent?.Tag != "HEAD")
                diags.Add(new GedDiagnostic(GedDiagnosticSeverity.Warning, "GED011",
                    "free-text SOUR citation payload is removed in GEDCOM 7.0 (SOUR must point to a SOUR record)",
                    OwningXref(rec), rec.Tag));

            if (rec.Tag == "NOTE" && rec.Level == 0 && rec.Xref is not null)
                diags.Add(new GedDiagnostic(GedDiagnosticSeverity.Warning, "GED011",
                    "level-0 NOTE with an xref is removed in GEDCOM 7.0 (use SNOTE)", rec.Xref, rec.Tag));
        }

        if (head is null) return;
        if (head.FirstChild("CHAR") is not null)
            diags.Add(new GedDiagnostic(GedDiagnosticSeverity.Warning, "GED011",
                "HEAD.CHAR is removed in GEDCOM 7.0 (encoding is always UTF-8)", head.Xref, "CHAR"));
        if (head.FirstChild("FILE") is not null)
            diags.Add(new GedDiagnostic(GedDiagnosticSeverity.Warning, "GED011",
                "HEAD.FILE is removed in GEDCOM 7.0", head.Xref, "FILE"));
        if (head.FirstChild("DEST") is not null)
            diags.Add(new GedDiagnostic(GedDiagnosticSeverity.Warning, "GED011",
                "HEAD.DEST is removed in GEDCOM 7.0", head.Xref, "DEST"));
        if (head.FirstChild("GEDC")?.FirstChild("FORM") is not null)
            diags.Add(new GedDiagnostic(GedDiagnosticSeverity.Warning, "GED011",
                "HEAD.GEDC.FORM is removed in GEDCOM 7.0", head.Xref, "FORM"));
    }

    // -------------------------------------------------------------------
    // GED012 — SEX / QUAY value ranges
    // -------------------------------------------------------------------

    private static void CheckSexAndQuayRanges(GedDocument doc, List<GedDiagnostic> diags)
    {
        foreach (var rec in AllRecords(doc))
        {
            if (rec.Tag == "SEX" && !ValidSexValues.Contains(rec.Value))
                diags.Add(new GedDiagnostic(GedDiagnosticSeverity.Info, "GED012",
                    $"SEX payload '{rec.Value}' is outside M/F/X/U", OwningXref(rec), rec.Tag));

            if (rec.Tag == "QUAY" && !ValidQuayValues.Contains(rec.Value))
                diags.Add(new GedDiagnostic(GedDiagnosticSeverity.Info, "GED012",
                    $"QUAY payload '{rec.Value}' is outside 0-3", OwningXref(rec), rec.Tag));
        }
    }

    // -------------------------------------------------------------------
    // GED013 — OBJE requires FILE, FILE requires FORM
    // -------------------------------------------------------------------

    private static void CheckObjeFileForm(GedDocument doc, List<GedDiagnostic> diags)
    {
        foreach (var rec in doc.Records.Where(r => r.Tag == "OBJE"))
        {
            var files = rec.ChildrenByTag("FILE").ToList();
            if (files.Count == 0)
            {
                diags.Add(new GedDiagnostic(GedDiagnosticSeverity.Error, "GED013",
                    "OBJE record has no FILE substructure", rec.Xref, "OBJE"));
                continue;
            }

            foreach (var file in files)
            {
                if (file.FirstChild("FORM") is null)
                    diags.Add(new GedDiagnostic(GedDiagnosticSeverity.Error, "GED013",
                        "OBJE.FILE has no FORM substructure", rec.Xref, "FILE"));
            }
        }
    }

    // -------------------------------------------------------------------
    // GED014 — SOUR <-> OBJE pointer cycle
    // -------------------------------------------------------------------

    private static void CheckSourceObjeCycle(GedDocument doc, List<GedDiagnostic> diags)
    {
        foreach (var objeRec in doc.Records.Where(r => r.Tag == "OBJE"))
        {
            foreach (var sourRef in objeRec.ChildrenByTag("SOUR"))
            {
                if (!sourRef.IsPointerValue) continue;
                if (!doc.ByXref.TryGetValue(sourRef.Value, out var sourceRec) || sourceRec.Tag != "SOUR") continue;

                foreach (var objeRef in sourceRec.ChildrenByTag("OBJE"))
                {
                    if (objeRef.IsPointerValue && objeRef.Value == objeRec.Xref)
                        diags.Add(new GedDiagnostic(GedDiagnosticSeverity.Error, "GED014",
                            $"OBJE {objeRec.Xref} <-> SOUR {sourceRec.Xref} pointer cycle (prohibited, 7.0.17)",
                            objeRec.Xref, "OBJE"));
                }
            }
        }
    }

    // -------------------------------------------------------------------
    // Shared helpers
    // -------------------------------------------------------------------

    /// <summary>Every record in the document, at every nesting level, in document order.</summary>
    private static IEnumerable<GedRecord> AllRecords(GedDocument doc)
    {
        foreach (var root in doc.Records)
            foreach (var rec in Walk(root))
                yield return rec;
    }

    private static IEnumerable<GedRecord> Walk(GedRecord rec)
    {
        yield return rec;
        foreach (var child in rec.Children)
            foreach (var r in Walk(child))
                yield return r;
    }

    /// <summary>The xref of the level-0 (or orphaned-root) ancestor of <paramref name="rec"/>, if any.</summary>
    private static string? OwningXref(GedRecord rec)
    {
        var cur = rec;
        while (cur.Parent is not null) cur = cur.Parent;
        return cur.Xref;
    }
}
