using System.Text.Json;

namespace GedCore.Apply;

/// <summary>
/// CreateOrUpdate for the Spouse noun: ensure a family links the two people
/// as partners, with the marriage fact and provenance. The spouse may be an
/// existing person (xref) or described inline (created here). Creating a
/// family always requires an explicit new family xref; with no family named,
/// the two people must already share exactly one.
///
/// Relationship provenance ("citations") attaches at the FAM record level:
/// GEDCOM 7 allows SOURCE_CITATION on the FAM record but only PHRASE under
/// HUSB/WIFE/CHIL. Marriage citations attach on MARR, which is an event.
/// </summary>
public sealed class CreateOrUpdateSpouseOp : ChangeOp
{
    public override string Kind => "createOrUpdateSpouse";

    public required string Person { get; init; }
    public required PersonRef Spouse { get; init; }
    public string? Family { get; init; }
    public EventValue? Marriage { get; init; }
    public IReadOnlyList<Citation> MarriageCitations { get; init; } = [];
    public string? Note { get; init; }
    public IReadOnlyList<Citation> Citations { get; init; } = [];

    internal override IEnumerable<string> CitedSources =>
        Citations.Concat(MarriageCitations).Concat(Spouse.Facts.SelectMany(f => f.Citations))
                 .Select(c => c.Source);

    internal static CreateOrUpdateSpouseOp Read(JsonElement el)
    {
        EventValue? marriage = null;
        IReadOnlyList<Citation> marriageCitations = [];
        if (el.TryGetProperty("marriage", out var m))
        {
            marriage = new EventValue(null, JsonRead.Str(m, "date"), JsonRead.Str(m, "place"));
            marriageCitations = Citation.ReadAll(m);
        }
        return new CreateOrUpdateSpouseOp
        {
            Person = JsonRead.Req(el, "person", "createOrUpdateSpouse"),
            Spouse = PersonRef.Read(el, "spouse")
                     ?? throw new JsonException("createOrUpdateSpouse: required field 'spouse' missing"),
            Family = JsonRead.Str(el, "family"),
            Marriage = marriage,
            MarriageCitations = marriageCitations,
            Note = JsonRead.Str(el, "note"),
            Citations = Citation.ReadAll(el),
        };
    }

    private string Context => $"{Kind} {Person} + {Spouse.Xref}";

    internal override void Validate(ResolutionContext ctx, List<string> errors)
    {
        if (OpChecks.RejectVoid(Context, Person, errors)) return;
        if (!ctx.Known(Person)) { errors.Add($"{Context}: person not in file"); return; }

        var person = Resolve.Person(ctx, Spouse);
        if (person.Error is not null) { errors.Add($"{Context}: {person.Error}"); return; }

        var family = Resolve.SpouseFamily(ctx, Person, Spouse, Family);
        if (family.Error is not null) { errors.Add($"{Context}: {family.Error}"); return; }

        bool bothAlreadyPlaced = family.Family is not null
            && Resolve.IsPartner(family.Family, Person)
            && Resolve.IsPartner(family.Family, Spouse.Xref);
        if (!bothAlreadyPlaced && !family.Planned
            && !Kin.RolesResolvable(ctx, family.Family, Person, Spouse))
            errors.Add($"{Context}: cannot assign HUSB/WIFE roles — " +
                       "the partners' SEX values are missing or equal");

        if (Marriage is not null)
        {
            OpChecks.CitationsRequired($"{Context}: MARR", MarriageCitations, errors);
            OpChecks.CitationsValid(ctx, $"{Context}: MARR", MarriageCitations, errors);
        }
        OpChecks.CitationsValid(ctx, Context, Citations, errors);
        OpChecks.InlineFactsValid(ctx, Context, Spouse, errors);

        if (person.Hit == PersonHit.Create) ctx.Planned.Add(Spouse.Xref);
        if (family.CreateXref is not null) ctx.Planned.Add(family.CreateXref);
    }

    internal override void Apply(ApplyState state, List<string> log)
    {
        int before = state.Mutations;
        Kin.EnsurePerson(state, Spouse, log);

        var ctx = new ResolutionContext(state.Doc);
        var resolution = Resolve.SpouseFamily(ctx, Person, Spouse, Family);
        GedRecord fam;
        if (resolution.CreateXref is not null)
        {
            fam = Kin.CreateFamily(state, resolution.CreateXref);
            log.Add($"created family {resolution.CreateXref}");
        }
        else
            fam = resolution.RequireFamily(Context);

        var changes = new List<string>();
        var (personTag, spouseTag) = Kin.PartnerTags(ctx, fam, Person, Spouse);
        if (Kin.SetPartner(state, fam, personTag, Person) is string p) changes.Add(p);
        if (Kin.SetPartner(state, fam, spouseTag, Spouse.Xref) is string s) changes.Add(s);

        if (Marriage is not null)
            new CreateOrUpdateVitalOp
            {
                Record = fam.Xref!, Fact = "MARR",
                Value = Marriage, Citations = MarriageCitations,
            }.Apply(state, log);

        foreach (var cit in Citations)
            if (NodeBuilder.UpsertCitation(state, fam, cit) is string c) changes.Add(c);
        if (Note is not null && Kin.UpsertNote(state, fam, Note) is string n) changes.Add(n);

        log.Add(changes.Count > 0
            ? $"{Context} on {fam.Xref}: {string.Join("; ", changes)}"
            : state.Mutations > before
                ? $"{Context} on {fam.Xref}: updated"
                : $"{Context} on {fam.Xref}: no-op (already matches)");
    }
}

/// <summary>
/// CreateOrUpdate for the Child noun: ensure a person is a CHIL of a family.
/// The family xref is mandatory — an existing FAM is updated; an unused xref
/// creates the family, seeded with the optional husb/wife partners.
/// Relationship provenance attaches at the FAM record level (see
/// <see cref="CreateOrUpdateSpouseOp"/> for the GEDCOM 7 rationale).
/// </summary>
public sealed class CreateOrUpdateChildOp : ChangeOp
{
    public override string Kind => "createOrUpdateChild";

    public required string Family { get; init; }
    public required PersonRef Child { get; init; }
    public string? Husb { get; init; }
    public string? Wife { get; init; }
    public IReadOnlyList<Citation> Citations { get; init; } = [];

    internal override IEnumerable<string> CitedSources =>
        Citations.Concat(Child.Facts.SelectMany(f => f.Citations)).Select(c => c.Source);

    internal static CreateOrUpdateChildOp Read(JsonElement el) => new()
    {
        Family = JsonRead.Req(el, "family", "createOrUpdateChild"),
        Child = PersonRef.Read(el, "child")
                ?? throw new JsonException("createOrUpdateChild: required field 'child' missing"),
        Husb = JsonRead.Str(el, "husb"),
        Wife = JsonRead.Str(el, "wife"),
        Citations = Citation.ReadAll(el),
    };

    private string Context => $"{Kind} {Child.Xref} in {Family}";

    internal override void Validate(ResolutionContext ctx, List<string> errors)
    {
        var child = Resolve.Person(ctx, Child);
        if (child.Error is not null) { errors.Add($"{Context}: {child.Error}"); return; }

        var family = Resolve.ChildFamily(ctx, Family);
        if (family.Error is not null) { errors.Add($"{Context}: {family.Error}"); return; }

        if (family.CreateXref is not null)
        {
            foreach (var (role, xref) in new[] { ("husb", Husb), ("wife", Wife) })
            {
                if (xref is null) continue;
                if (OpChecks.RejectVoid(Context, xref, errors)) continue;
                if (!ctx.Known(xref))
                    errors.Add($"{Context}: {role} {xref} unknown");
            }
        }
        else if (Husb is not null || Wife is not null)
            errors.Add($"{Context}: husb/wife are creation seeds; {Family} already exists " +
                       "(use createOrUpdateSpouse to change partners)");

        OpChecks.CitationsValid(ctx, Context, Citations, errors);
        OpChecks.InlineFactsValid(ctx, Context, Child, errors);

        if (child.Hit == PersonHit.Create) ctx.Planned.Add(Child.Xref);
        if (family.CreateXref is not null) ctx.Planned.Add(family.CreateXref);
    }

    internal override void Apply(ApplyState state, List<string> log)
    {
        Kin.EnsurePerson(state, Child, log);

        var ctx = new ResolutionContext(state.Doc);
        var resolution = Resolve.ChildFamily(ctx, Family);
        GedRecord fam;
        var changes = new List<string>();
        if (resolution.CreateXref is not null)
        {
            fam = Kin.CreateFamily(state, resolution.CreateXref);
            log.Add($"created family {resolution.CreateXref}");
            if (Husb is not null && Kin.SetPartner(state, fam, "HUSB", Husb) is string h) changes.Add(h);
            if (Wife is not null && Kin.SetPartner(state, fam, "WIFE", Wife) is string w) changes.Add(w);
        }
        else
            fam = resolution.RequireFamily(Context);

        if (Kin.EnsureChild(state, fam, Child.Xref) is string c) changes.Add(c);
        foreach (var cit in Citations)
            if (NodeBuilder.UpsertCitation(state, fam, cit) is string cc) changes.Add(cc);

        log.Add(changes.Count > 0
            ? $"{Context}: {string.Join("; ", changes)}"
            : $"{Context}: no-op (already a child)");
    }
}

/// <summary>
/// CreateOrUpdate for the Parent noun: ensure a person's parent family has
/// the given parent in the given role (father → HUSB, mother → WIFE). With
/// one FAMC the target family is implicit; several require the family xref;
/// none requires a new family xref (the person becomes its CHIL). Replacing
/// a different parent rewrites the pointer and removes the replaced parent's
/// FAMS back-link. Provenance attaches at the FAM record level.
/// </summary>
public sealed class CreateOrUpdateParentOp : ChangeOp
{
    public override string Kind => "createOrUpdateParent";

    public required string Person { get; init; }
    public required string Role { get; init; }
    public required PersonRef Parent { get; init; }
    public string? Family { get; init; }
    public IReadOnlyList<Citation> Citations { get; init; } = [];

    internal override IEnumerable<string> CitedSources =>
        Citations.Concat(Parent.Facts.SelectMany(f => f.Citations)).Select(c => c.Source);

    internal static CreateOrUpdateParentOp Read(JsonElement el) => new()
    {
        Person = JsonRead.Req(el, "person", "createOrUpdateParent"),
        Role = JsonRead.Req(el, "role", "createOrUpdateParent"),
        Parent = PersonRef.Read(el, "parent")
                 ?? throw new JsonException("createOrUpdateParent: required field 'parent' missing"),
        Family = JsonRead.Str(el, "family"),
        Citations = Citation.ReadAll(el),
    };

    private string Context => $"{Kind} {Role} {Parent.Xref} of {Person}";

    internal override void Validate(ResolutionContext ctx, List<string> errors)
    {
        if (OpChecks.RejectVoid(Context, Person, errors)) return;
        if (!ctx.Known(Person)) { errors.Add($"{Context}: person not in file"); return; }
        if (Role is not ("father" or "mother"))
        { errors.Add($"{Context}: role must be father or mother"); return; }

        var parent = Resolve.Person(ctx, Parent);
        if (parent.Error is not null) { errors.Add($"{Context}: {parent.Error}"); return; }

        var personRec = ctx.Existing(Person);
        if (personRec is null)
        {
            // person is created by an earlier op in this run; nothing to inspect
            if (Family is null)
                errors.Add($"{Context}: person is created in this run; supply the family xref");
        }
        else
        {
            var family = Resolve.ParentFamily(ctx, personRec, Family);
            if (family.Error is not null) { errors.Add($"{Context}: {family.Error}"); return; }
            if (family.CreateXref is not null) ctx.Planned.Add(family.CreateXref);
        }

        OpChecks.CitationsValid(ctx, Context, Citations, errors);
        OpChecks.InlineFactsValid(ctx, Context, Parent, errors);
        if (parent.Hit == PersonHit.Create) ctx.Planned.Add(Parent.Xref);
    }

    internal override void Apply(ApplyState state, List<string> log)
    {
        Kin.EnsurePerson(state, Parent, log);

        var ctx = new ResolutionContext(state.Doc);
        var personRec = state.Doc.ByXref[Person];
        var resolution = Resolve.ParentFamily(ctx, personRec, Family);
        GedRecord fam;
        var changes = new List<string>();
        if (resolution.CreateXref is not null)
        {
            fam = Kin.CreateFamily(state, resolution.CreateXref);
            log.Add($"created family {resolution.CreateXref}");
            if (Kin.EnsureChild(state, fam, Person) is string c) changes.Add(c);
        }
        else
            fam = resolution.RequireFamily(Context);

        string roleTag = Role == "father" ? "HUSB" : "WIFE";
        if (Kin.SetPartner(state, fam, roleTag, Parent.Xref) is string p) changes.Add(p);
        foreach (var cit in Citations)
            if (NodeBuilder.UpsertCitation(state, fam, cit) is string cc) changes.Add(cc);

        log.Add(changes.Count > 0
            ? $"{Context} on {fam.Xref}: {string.Join("; ", changes)}"
            : $"{Context} on {fam.Xref}: no-op (already the {Role})");
    }
}

/// <summary>
/// Delete for the Spouse noun: remove one partner's link (and FAMS back-link)
/// from the family the two people share. No shared family, or the spouse not
/// a partner → no-op. A family left empty is deleted.
/// </summary>
public sealed class DeleteSpouseOp : ChangeOp
{
    public override string Kind => "deleteSpouse";

    public required string Person { get; init; }
    public required string Spouse { get; init; }
    public string? Family { get; init; }

    internal static DeleteSpouseOp Read(JsonElement el) => new()
    {
        Person = JsonRead.Req(el, "person", "deleteSpouse"),
        Spouse = JsonRead.Req(el, "spouse", "deleteSpouse"),
        Family = JsonRead.Str(el, "family"),
    };

    internal override void Validate(ResolutionContext ctx, List<string> errors)
    {
        if (OpChecks.RejectVoid($"{Kind} {Spouse} of {Person}", Person, errors)) return;
        if (!ctx.Known(Person)) { errors.Add($"{Kind} {Spouse} of {Person}: person not in file"); return; }
        if (Family is not null)
        {
            if (ctx.Existing(Family) is { } fam && fam.Tag != "FAM")
                errors.Add($"{Kind} {Spouse} of {Person}: {Family} is not a FAM record");
            return;
        }
        if (Kin.SharedFamilies(ctx, Person, Spouse).Count > 1)
            errors.Add($"{Kind} {Spouse} of {Person}: multiple shared families; supply the family xref");
    }

    internal override void Apply(ApplyState state, List<string> log)
    {
        var ctx = new ResolutionContext(state.Doc);
        var fam = Family is not null
            ? ctx.Existing(Family)
            : Kin.SharedFamilies(ctx, Person, Spouse).SingleOrDefault();
        var link = fam?.Children.FirstOrDefault(
            c => c.Tag is "HUSB" or "WIFE" && c.Value == Spouse);
        if (fam is null || link is null)
        {
            log.Add($"{Kind} {Spouse} of {Person}: no-op (no such spouse link)");
            return;
        }

        fam.Children.Remove(link);
        state.Mutated();
        state.Touch(fam);
        Kin.RemoveFamsBackLink(state, Spouse, fam.Xref!);
        log.Add($"{Kind} {Spouse} of {Person}: removed {link.Tag} from {fam.Xref}");
        Kin.CleanupFamilyIfEmpty(state, fam, log);
    }
}

/// <summary>
/// Delete for the Child noun: remove a CHIL link and the child's FAMC
/// back-link. Absent → no-op. A family left empty is deleted.
/// </summary>
public sealed class DeleteChildOp : ChangeOp
{
    public override string Kind => "deleteChild";

    public required string Family { get; init; }
    public required string Child { get; init; }

    internal static DeleteChildOp Read(JsonElement el) => new()
    {
        Family = JsonRead.Req(el, "family", "deleteChild"),
        Child = JsonRead.Req(el, "child", "deleteChild"),
    };

    internal override void Validate(ResolutionContext ctx, List<string> errors)
    {
        if (ctx.Existing(Family) is { } fam && fam.Tag != "FAM")
            errors.Add($"{Kind} {Child} from {Family}: not a FAM record");
    }

    internal override void Apply(ApplyState state, List<string> log)
    {
        var fam = state.Doc.ByXref.GetValueOrDefault(Family);
        var link = fam?.Children.FirstOrDefault(c => c.Tag == "CHIL" && c.Value == Child);
        if (fam is null || link is null)
        {
            log.Add($"{Kind} {Child} from {Family}: no-op (no such child link)");
            return;
        }

        fam.Children.Remove(link);
        state.Mutated();
        state.Touch(fam);
        Kin.RemoveFamcBackLink(state, Child, Family);
        log.Add($"{Kind} {Child} from {Family}: removed");
        Kin.CleanupFamilyIfEmpty(state, fam, log);
    }
}

/// <summary>
/// Delete for the Parent noun: remove the HUSB/WIFE link (and FAMS back-link)
/// for the given role from the person's parent family. Absent → no-op.
/// </summary>
public sealed class DeleteParentOp : ChangeOp
{
    public override string Kind => "deleteParent";

    public required string Person { get; init; }
    public required string Role { get; init; }
    public string? Family { get; init; }

    internal static DeleteParentOp Read(JsonElement el) => new()
    {
        Person = JsonRead.Req(el, "person", "deleteParent"),
        Role = JsonRead.Req(el, "role", "deleteParent"),
        Family = JsonRead.Str(el, "family"),
    };

    private string Context => $"{Kind} {Role} of {Person}";

    internal override void Validate(ResolutionContext ctx, List<string> errors)
    {
        if (OpChecks.RejectVoid(Context, Person, errors)) return;
        if (!ctx.Known(Person)) { errors.Add($"{Context}: person not in file"); return; }
        if (Role is not ("father" or "mother"))
        { errors.Add($"{Context}: role must be father or mother"); return; }
        var personRec = ctx.Existing(Person);
        if (personRec is null) return;
        var famcs = ParentFamilies(personRec);
        if (famcs.Count > 1)
            errors.Add($"{Context}: {famcs.Count} parent families; supply the family xref");
    }

    private List<string> ParentFamilies(GedRecord person) =>
        [.. person.ChildrenByTag("FAMC").Select(f => f.Value)
                  .Where(f => Family is null || f == Family)];

    internal override void Apply(ApplyState state, List<string> log)
    {
        var personRec = state.Doc.ByXref[Person];
        var famXref = ParentFamilies(personRec).SingleOrDefault();
        var fam = famXref is null ? null : state.Doc.ByXref.GetValueOrDefault(famXref);
        string roleTag = Role == "father" ? "HUSB" : "WIFE";
        var link = fam?.FirstChild(roleTag);
        if (fam is null || link is null)
        {
            log.Add($"{Context}: no-op (no such parent link)");
            return;
        }

        string parentXref = link.Value;
        fam.Children.Remove(link);
        state.Mutated();
        state.Touch(fam);
        Kin.RemoveFamsBackLink(state, parentXref, fam.Xref!);
        log.Add($"{Context}: removed {roleTag} {parentXref} from {fam.Xref}");
        Kin.CleanupFamilyIfEmpty(state, fam, log);
    }
}
