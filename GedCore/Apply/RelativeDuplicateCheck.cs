using GedCore.Matching;

namespace GedCore.Apply;

/// <summary>
/// Refuses a new person a changeset creates as the relative of someone already in the tree, when that person already has
/// the relative: a parent in the same role (by any name: one of the same name is a duplicate, another name a conflicting
/// second parent), a spouse, or a child (of the family, or of an existing partner of a family the changeset creates)
/// whose name agrees by <see cref="NameAgreement.SharesAGivenName"/>. An existing person the op lists in notDuplicateOf
/// is passed over.
/// </summary>
public static class RelativeDuplicateCheck
{
    public static IReadOnlyList<string> Check(IEnumerable<ChangeOp> ops, IExistingRelatives tree)
    {
        var list = ops.ToList();
        var newFamilyPartners = NewFamilyPartners(list, tree);
        var errors = new List<string>();
        foreach (var op in list)
        {
            switch (op)
            {
                case CreateOrUpdateParentOp parent when IsNew(parent.Parent) && tree.Person(parent.Person) is { } child:
                    errors.AddRange(ParentErrors(parent, child, tree));
                    break;
                case CreateOrUpdateSpouseOp spouse when IsNew(spouse.Spouse) && tree.Person(spouse.Person) is { } partner:
                    errors.AddRange(Spouses(partner.Xref, tree)
                        .Where(s => Agrees(s, spouse.Spouse, spouse.NotDuplicateOf))
                        .Select(s => Duplicate(spouse.Spouse, $"{Describe(partner)} already has the spouse", s)));
                    break;
                case CreateOrUpdateChildOp child when IsNew(child.Child):
                    errors.AddRange(ChildErrors(child, newFamilyPartners, tree));
                    break;
            }
        }
        return errors;
    }

    static IEnumerable<string> ParentErrors(CreateOrUpdateParentOp op, Relative child, IExistingRelatives tree)
    {
        string tag = op.Role == "father" ? "HUSB" : "WIFE";
        IReadOnlyList<string> families = op.Family is { } family && !Placeholder.IsPlaceholder(family) ? [family] : tree.FamiliesAsChild(child.Xref);
        var held = families.Select(f => tree.Partner(f, tag)).OfType<Relative>().DistinctBy(p => p.Xref)
            .Where(p => !op.NotDuplicateOf.Contains(p.Xref));
        foreach (var parent in held)
        {
            string has = $"{Describe(child)} already has the {op.Role}";
            yield return NameAgreement.SharesAGivenName(parent.Name, op.Parent.Name)
                ? Duplicate(op.Parent, has, parent)
                : $"{Describe(op.Parent)}: {has} {Describe(parent)}, so a new {op.Role} would be a second, conflicting one; " +
                  $"name {parent.Xref} in place of {op.Parent.Xref} if they are one person, or, if {op.Parent.Xref} is truly " +
                  $"another person, list {parent.Xref} in the relation's notDuplicateOf";
        }
    }

    static IEnumerable<string> ChildErrors(
        CreateOrUpdateChildOp op, Dictionary<string, List<Relative>> newFamilyPartners, IExistingRelatives tree)
    {
        if (!Placeholder.IsPlaceholder(op.Family))
            return tree.Children(op.Family)
                .Where(c => Agrees(c, op.Child, op.NotDuplicateOf))
                .Select(c => Duplicate(op.Child, $"family {op.Family} already has the child", c));

        return newFamilyPartners.GetValueOrDefault(op.Family, []).SelectMany(partner =>
            ChildrenOf(partner.Xref, tree)
                .Where(c => Agrees(c, op.Child, op.NotDuplicateOf))
                .Select(c => Duplicate(op.Child, $"{Describe(partner)}, a partner in the new family {op.Family}, already has the child", c)));
    }

    // The existing people each family the changeset creates is built around, from the ops that seed or join it.
    static Dictionary<string, List<Relative>> NewFamilyPartners(List<ChangeOp> ops, IExistingRelatives tree)
    {
        var partners = new Dictionary<string, List<Relative>>(StringComparer.Ordinal);
        void Add(string? family, string? person)
        {
            if (family is null || !Placeholder.IsPlaceholder(family) || person is null || tree.Person(person) is not { } relative) return;
            if (!partners.TryGetValue(family, out var list)) partners[family] = list = [];
            if (list.All(p => p.Xref != relative.Xref)) list.Add(relative);
        }

        foreach (var op in ops)
        {
            switch (op)
            {
                case CreateOrUpdateSpouseOp spouse:
                    Add(spouse.Family, spouse.Person);
                    Add(spouse.Family, spouse.Spouse.Xref);
                    break;
                case CreateOrUpdateChildOp child:
                    Add(child.Family, child.Husb);
                    Add(child.Family, child.Wife);
                    break;
                case CreateOrUpdateParentOp parent:
                    Add(parent.Family, parent.Parent.Xref);
                    break;
            }
        }
        return partners;
    }

    static IEnumerable<Relative> Spouses(string person, IExistingRelatives tree) =>
        tree.FamiliesAsPartner(person)
            .SelectMany(f => new[] { tree.Partner(f, "HUSB"), tree.Partner(f, "WIFE") })
            .OfType<Relative>()
            .Where(p => p.Xref != person)
            .DistinctBy(p => p.Xref);

    static IEnumerable<Relative> ChildrenOf(string person, IExistingRelatives tree) =>
        tree.FamiliesAsPartner(person).SelectMany(tree.Children).DistinctBy(c => c.Xref);

    static bool IsNew(PersonRef person) => person.IsInline && Placeholder.IsPlaceholder(person.Xref);

    static bool Agrees(Relative held, PersonRef created, IReadOnlyList<string> notDuplicateOf) =>
        !notDuplicateOf.Contains(held.Xref) && NameAgreement.SharesAGivenName(held.Name, created.Name);

    static string Duplicate(PersonRef created, string has, Relative held) =>
        $"{Describe(created)}: {has} {Describe(held)}, the same name; name {held.Xref} in place of {created.Xref}, or, " +
        $"if {created.Xref} is truly another person, list {held.Xref} in the relation's notDuplicateOf";

    static string Describe(Relative person) => $"{NameAgreement.Display(person.Name)} ({person.Xref})";

    static string Describe(PersonRef person) => $"{NameAgreement.Display(person.Name)} ({person.Xref})";
}
