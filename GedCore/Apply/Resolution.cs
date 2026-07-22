namespace GedCore.Apply;

/// <summary>
/// Lookup context shared by validation and application. During validation
/// nothing is created, so ops that will create records register the xref in
/// <see cref="Planned"/> as they are validated (in application order); later
/// ops can then validate references to not-yet-existing records. During
/// application the created records are in the live document, so
/// <see cref="Planned"/> stays empty and lookups hit <see cref="Existing"/>.
/// </summary>
internal sealed class ResolutionContext(GedDocument doc)
{
    /// <summary>Xrefs that earlier ops in this run will create.</summary>
    public HashSet<string> Planned { get; } = new(StringComparer.Ordinal);

    public bool Known(string xref) => doc.ByXref.ContainsKey(xref) || Planned.Contains(xref);

    public GedRecord? Existing(string xref) =>
        doc.ByXref.TryGetValue(xref, out var rec) ? rec : null;

    public IEnumerable<GedRecord> RecordsOfType(string tag) =>
        doc.Records.Where(r => r.Tag == tag);
}

/// <summary>
/// Outcome of resolving a fact selector on a record. <see cref="Fact"/> is
/// non-null iff exactly one instance matched. <see cref="TagCount"/> counts
/// all same-tag instances; <see cref="MatchCount"/> counts those passing the
/// match selector (all of them when no selector was given).
/// </summary>
internal sealed record FactResolution(GedRecord? Fact, int TagCount, int MatchCount)
{
    public bool Ambiguous => MatchCount > 1;
}

/// <summary>Outcome of resolving a PersonRef.</summary>
internal enum PersonHit { Link, Create }
internal sealed record PersonResolution(PersonHit Hit, GedRecord? Record, string? Error)
{
    public static PersonResolution Fail(string error) => new(PersonHit.Link, null, error);
}

/// <summary>
/// Outcome of resolving which FAM a relationship op targets: an existing
/// record, a new record to create under <see cref="CreateXref"/>, a record
/// an earlier op in this run creates (<see cref="Planned"/> — inspectable
/// only at apply time), or an error.
/// </summary>
internal sealed record FamilyResolution(GedRecord? Family, string? CreateXref, bool Planned, string? Error)
{
    public static FamilyResolution Fail(string error) => new(null, null, false, error);

    /// <summary>
    /// The resolved family for the non-create branch of an Apply method.
    /// Validate should already have rejected any <see cref="Error"/> case,
    /// so a null <see cref="Family"/> reaching here means Apply's re-resolution
    /// disagreed with Validate's — surface that loudly and specifically instead
    /// of null-dereferencing three frames deeper in <c>Kin.SetPartner</c>.
    /// </summary>
    public GedRecord RequireFamily(string context) =>
        Family ?? throw new InvalidOperationException(
            $"{context}: family did not resolve at apply time" +
            (Error is not null ? $" ({Error})" : " (no error recorded)"));
}

/// <summary>
/// Read-only resolvers shared by op validation and op application, so both
/// phases derive create-vs-update-vs-no-op decisions from the same logic.
/// </summary>
internal static class Resolve
{
    /// <summary>
    /// Find the fact instance(s) with <paramref name="tag"/> on
    /// <paramref name="record"/> that pass <paramref name="match"/>.
    /// Date comparison is zero-padding-insensitive (the file zero-pads days).
    /// </summary>
    public static FactResolution Fact(GedRecord record, string tag, FactMatch? match)
    {
        var instances = record.ChildrenByTag(tag).ToList();
        var matched = instances.Where(f => Matches(f, match)).ToList();
        return new FactResolution(
            matched.Count == 1 ? matched[0] : null, instances.Count, matched.Count);
    }

    private static bool Matches(GedRecord fact, FactMatch? match)
    {
        if (match is null) return true;
        if (match.Text is not null && fact.Value != match.Text) return false;
        if (match.Date is not null &&
            !fact.ChildrenByTag("DATE").Any(d => d.Value == NodeBuilder.PadDay(match.Date)))
            return false;
        if (match.Place is not null &&
            !fact.ChildrenByTag("PLAC").Any(p => p.Value == match.Place))
            return false;
        return true;
    }

    /// <summary>
    /// Resolve a PersonRef: link to an existing INDI, create the inline
    /// person, or fail. An inline ref whose xref already exists with the
    /// identical NAME degrades to a link — this is what makes whole-changeset
    /// re-runs no-ops; a different NAME is refused.
    /// </summary>
    public static PersonResolution Person(ResolutionContext ctx, PersonRef p)
    {
        if (p.Xref == GedRecord.VoidPointer)
            return PersonResolution.Fail($"{GedRecord.VoidPointer} is not an addressable record");
        var existing = ctx.Existing(p.Xref);
        if (existing is null)
        {
            if (ctx.Planned.Contains(p.Xref))
                return new PersonResolution(PersonHit.Link, null, null);
            if (!p.IsInline)
                return PersonResolution.Fail($"person {p.Xref} not in file (give name/sex to create inline)");
            return new PersonResolution(PersonHit.Create, null, null);
        }
        if (existing.Tag != "INDI")
            return PersonResolution.Fail($"{p.Xref} is not an INDI record");
        if (p.IsInline && existing.FirstChild("NAME")?.Value != p.Name)
            return PersonResolution.Fail(
                $"{p.Xref} exists with a different name " +
                $"('{existing.FirstChild("NAME")?.Value}' vs '{p.Name}'); refusing inline create");
        return new PersonResolution(PersonHit.Link, existing, null);
    }

    /// <summary>
    /// Resolve the FAM a spouse op targets. A named existing FAM is used
    /// as-is; a named unused xref means create; with no family named, the two
    /// people must share exactly one existing FAM (creating a family always
    /// requires an explicit new xref so changesets stay reviewable).
    /// </summary>
    public static FamilyResolution SpouseFamily(
        ResolutionContext ctx, string person, PersonRef spouse, string? familyXref)
    {
        if (familyXref is not null) return NamedFamily(ctx, familyXref);

        var spouseExists = ctx.Existing(spouse.Xref) is not null;
        if (!spouseExists)
            return FamilyResolution.Fail(
                $"{spouse.Xref} is new; supply a new family xref to create the marriage");

        var shared = ctx.RecordsOfType("FAM")
            .Where(f => IsPartner(f, person) && IsPartner(f, spouse.Xref))
            .ToList();
        return shared.Count switch
        {
            1 => new FamilyResolution(shared[0], null, false, null),
            0 => FamilyResolution.Fail(
                $"{person} and {spouse.Xref} share no family; supply a family xref to create one"),
            _ => FamilyResolution.Fail(
                $"{person} and {spouse.Xref} share {shared.Count} families; supply the family xref"),
        };
    }

    /// <summary>Resolve the FAM a child op targets (family xref is mandatory).</summary>
    public static FamilyResolution ChildFamily(ResolutionContext ctx, string familyXref) =>
        NamedFamily(ctx, familyXref);

    /// <summary>
    /// Resolve the FAM a parent op targets: the person's single FAMC family,
    /// or the named one of several, or a new FAM (named unused xref) when the
    /// person has none.
    /// </summary>
    public static FamilyResolution ParentFamily(
        ResolutionContext ctx, GedRecord person, string? familyXref)
    {
        var famcs = person.ChildrenByTag("FAMC").Select(f => f.Value).ToList();
        if (familyXref is not null)
        {
            var named = NamedFamily(ctx, familyXref);
            if (named.Family is not null && !famcs.Contains(familyXref))
                return FamilyResolution.Fail(
                    $"{familyXref} exists but is not a FAMC of {person.Xref} " +
                    "(use createOrUpdateChild to place the person in it first)");
            return named;
        }
        return famcs.Count switch
        {
            1 => ctx.Existing(famcs[0]) is { Tag: "FAM" } fam
                ? new FamilyResolution(fam, null, false, null)
                : FamilyResolution.Fail($"FAMC {famcs[0]} of {person.Xref} does not resolve to a FAM"),
            0 => FamilyResolution.Fail(
                $"{person.Xref} has no parent family; supply a new family xref to create one"),
            _ => FamilyResolution.Fail(
                $"{person.Xref} has {famcs.Count} parent families; supply the family xref"),
        };
    }

    private static FamilyResolution NamedFamily(ResolutionContext ctx, string familyXref)
    {
        if (familyXref == GedRecord.VoidPointer)
            return FamilyResolution.Fail($"{GedRecord.VoidPointer} is not an addressable record");
        var existing = ctx.Existing(familyXref);
        if (existing is not null)
            return existing.Tag == "FAM"
                ? new FamilyResolution(existing, null, false, null)
                : FamilyResolution.Fail($"{familyXref} is not a FAM record");
        if (ctx.Planned.Contains(familyXref))
            return new FamilyResolution(null, null, true, null);
        return new FamilyResolution(null, familyXref, false, null);
    }

    public static bool IsPartner(GedRecord fam, string xref) =>
        fam.Children.Any(c => c.Tag is "HUSB" or "WIFE" && c.Value == xref);

    /// <summary>The existing citation of <paramref name="source"/> on a structure, if any.</summary>
    public static GedRecord? CitationOnStructure(GedRecord structure, string source) =>
        structure.ChildrenByTag("SOUR").FirstOrDefault(s => s.Value == source);
}
