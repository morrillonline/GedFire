namespace GedCore.Apply;

/// <summary>
/// Kinship helpers shared by the relationship ops (Spouse, Child, Parent):
/// inline-person materialization, HUSB/WIFE role assignment, partner and
/// child link maintenance with two-way back-link bookkeeping, and the
/// empty-family cleanup that follows link deletion.
/// </summary>
internal static class Kin
{
    /// <summary>Create the inline person if their xref is not in the file yet.</summary>
    public static void EnsurePerson(ApplyState state, PersonRef p, List<string> log)
    {
        if (state.Doc.ByXref.ContainsKey(p.Xref)) return;
        var rec = new GedRecord(0, p.Xref, "INDI", "");
        NodeBuilder.Attach(rec, NodeBuilder.NewNode(1, "NAME", p.Name!));
        if (p.Sex is not null) NodeBuilder.Attach(rec, NodeBuilder.NewNode(1, "SEX", p.Sex));
        foreach (var f in p.Facts)
            NodeBuilder.Attach(rec, NodeBuilder.EventNode(1, f.Fact, f.Value, f.Citations));
        NodeBuilder.Attach(rec, NodeBuilder.NewNode(1, "UID", Guid.NewGuid().ToString()));
        state.AddRecord("INDI", rec);
        log.Add($"created person {p.Xref} ({p.Name})");
    }

    /// <summary>Create an empty FAM record (UID only) so link helpers can fill it.</summary>
    public static GedRecord CreateFamily(ApplyState state, string xref)
    {
        var rec = new GedRecord(0, xref, "FAM", "");
        NodeBuilder.Attach(rec, NodeBuilder.NewNode(1, "UID", Guid.NewGuid().ToString()));
        state.AddRecord("FAM", rec);
        return rec;
    }

    public static string? SexOf(ResolutionContext ctx, string xref, PersonRef? inline) =>
        ctx.Existing(xref)?.FirstChild("SEX")?.Value
        ?? (inline?.Xref == xref ? inline.Sex : null);

    /// <summary>
    /// Assign HUSB/WIFE roles from the sexes of the two partners. One known
    /// sex determines both roles; missing both, or equal sexes, fails (the
    /// caller reports the ambiguity as a validation error).
    /// </summary>
    public static bool TryRoles(string? personSex, string? spouseSex,
                                out string personTag, out string spouseTag)
    {
        personTag = spouseTag = "";
        if (personSex is not null && personSex == spouseSex) return false;
        string? effective = personSex
            ?? spouseSex switch { "M" => "F", "F" => "M", _ => null };
        if (effective is not ("M" or "F")) return false;
        (personTag, spouseTag) = effective == "M" ? ("HUSB", "WIFE") : ("WIFE", "HUSB");
        return true;
    }

    private static string? PartnerTagOf(GedRecord fam, string xref) =>
        fam.Children.FirstOrDefault(c => c.Tag is "HUSB" or "WIFE" && c.Value == xref)?.Tag;

    private static string OtherTag(string tag) => tag == "HUSB" ? "WIFE" : "HUSB";

    /// <summary>
    /// Can HUSB/WIFE roles be assigned for this pair? Existing placement in
    /// the family wins; otherwise the sexes must determine the roles.
    /// </summary>
    public static bool RolesResolvable(ResolutionContext ctx, GedRecord? fam,
                                       string person, PersonRef spouse)
    {
        if (fam is not null)
        {
            string? pt = PartnerTagOf(fam, person);
            string? st = PartnerTagOf(fam, spouse.Xref);
            if (pt is not null && st is not null) return pt != st;
            if (pt is not null || st is not null) return true;
        }
        return TryRoles(SexOf(ctx, person, null), SexOf(ctx, spouse.Xref, spouse), out _, out _);
    }

    /// <summary>Apply-time role assignment; validation guarantees resolvability.</summary>
    public static (string PersonTag, string SpouseTag) PartnerTags(
        ResolutionContext ctx, GedRecord fam, string person, PersonRef spouse)
    {
        string? pt = PartnerTagOf(fam, person);
        string? st = PartnerTagOf(fam, spouse.Xref);
        if (pt is not null && st is not null && pt != st) return (pt, st);
        if (pt is not null && st is null) return (pt, OtherTag(pt));
        if (st is not null && pt is null) return (OtherTag(st), st);
        if (!TryRoles(SexOf(ctx, person, null), SexOf(ctx, spouse.Xref, spouse),
                      out var personTag, out var spouseTag))
            throw new InvalidOperationException(
                $"cannot assign HUSB/WIFE roles for {person} and {spouse.Xref} in {fam.Xref}");
        return (personTag, spouseTag);
    }

    /// <summary>
    /// CreateOrUpdate one partner slot of a family. Already correct → null
    /// (the FAMS back-link is still queued for repair). Empty → fill.
    /// Holding a different person → replace, removing the replaced person's
    /// FAMS back-link. Returns the log fragment for a change.
    /// </summary>
    public static string? SetPartner(ApplyState state, GedRecord fam, string tag, string xref)
    {
        var slot = fam.FirstChild(tag);
        state.PendFams(xref, fam.Xref!);
        if (slot is not null && slot.Value == xref) return null;

        if (slot is null)
        {
            // file convention: HUSB first, WIFE directly after HUSB
            int at = 0;
            if (tag == "WIFE" && fam.FirstChild("HUSB") is GedRecord husb)
                at = fam.Children.IndexOf(husb) + 1;
            NodeBuilder.Attach(fam, NodeBuilder.NewNode(1, tag, xref), at);
            state.Mutated();
            state.Touch(fam);
            return $"{tag} {xref} added";
        }

        string replaced = slot.Value;
        slot.SetValue(xref);
        state.Mutated();
        state.Touch(fam);
        RemoveFamsBackLink(state, replaced, fam.Xref!);
        return $"{tag} {replaced} → {xref} (replaced; {replaced} FAMS removed)";
    }

    /// <summary>
    /// CreateOrUpdate a CHIL link. Present → null (FAMC back-link still
    /// queued for repair); absent → append after the partner/child block.
    /// </summary>
    public static string? EnsureChild(ApplyState state, GedRecord fam, string childXref)
    {
        state.PendFamc(childXref, fam.Xref!);
        if (fam.Children.Any(c => c.Tag == "CHIL" && c.Value == childXref)) return null;

        int anchor = fam.Children.FindIndex(c => c.Tag is not ("HUSB" or "WIFE" or "CHIL"));
        NodeBuilder.Attach(fam, NodeBuilder.NewNode(1, "CHIL", childXref),
                           at: anchor < 0 ? null : anchor);
        state.Mutated();
        state.Touch(fam);
        return $"CHIL {childXref} added";
    }

    public static void RemoveFamsBackLink(ApplyState state, string indiXref, string famXref) =>
        RemoveBackLink(state, indiXref, "FAMS", famXref);

    public static void RemoveFamcBackLink(ApplyState state, string indiXref, string famXref) =>
        RemoveBackLink(state, indiXref, "FAMC", famXref);

    private static void RemoveBackLink(ApplyState state, string indiXref, string tag, string famXref)
    {
        if (!state.Doc.ByXref.TryGetValue(indiXref, out var indi)) return;
        var link = indi.Children.FirstOrDefault(c => c.Tag == tag && c.Value == famXref);
        if (link is null) return;
        indi.Children.Remove(link);
        state.Mutated();
        state.Touch(indi);
    }

    /// <summary>All FAMs in which both people appear as partners.</summary>
    public static List<GedRecord> SharedFamilies(ResolutionContext ctx, string a, string b) =>
        [.. ctx.RecordsOfType("FAM").Where(f => Resolve.IsPartner(f, a) && Resolve.IsPartner(f, b))];

    /// <summary>
    /// A family left with no HUSB, no WIFE, and no CHIL after a link deletion
    /// is removed entirely, along with any back-links still pointing at it.
    /// </summary>
    public static void CleanupFamilyIfEmpty(ApplyState state, GedRecord fam, List<string> log)
    {
        if (fam.Children.Any(c => c.Tag is "HUSB" or "WIFE" or "CHIL")) return;

        foreach (var indi in state.Doc.Records.Where(r => r.Tag == "INDI"))
            foreach (var link in indi.Children
                         .Where(c => c.Tag is "FAMS" or "FAMC" && c.Value == fam.Xref).ToList())
            {
                indi.Children.Remove(link);
                state.Mutated();
                state.Touch(indi);
            }
        state.RemoveRecord(fam);
        log.Add($"family {fam.Xref} emptied; record deleted");
    }

    /// <summary>Add a NOTE to a record (before its UID) unless the exact text is present.</summary>
    public static string? UpsertNote(ApplyState state, GedRecord rec, string text)
    {
        if (rec.ChildrenByTag("NOTE").Any(n => n.Value == text)) return null;
        int anchor = rec.Children.FindIndex(c => c.Tag == "UID");
        NodeBuilder.Attach(rec, NodeBuilder.NewNode(1, "NOTE", text),
                           at: anchor < 0 ? null : anchor);
        state.Mutated();
        state.Touch(rec);
        return "note added";
    }
}
