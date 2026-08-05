using System.Text.Json;

namespace GedCore.Apply;

/// <summary>
/// One changeset operation (dialect v2): an orthogonal verb × noun pair.
/// Verbs: createOrUpdate (idempotent — matching state is a no-op, absent
/// state is created, differing state is replaced unconditionally with the
/// replacement logged), delete (absent target is a no-op), and merge
/// (fold a duplicate person into a survivor; idempotent by consumption —
/// once the duplicate is gone, a re-run is a no-op). Nouns: Vital, Spouse,
/// Child, Parent, Source, Citation, Note, Media, and — for merge only —
/// Person. Media's delete is the one deliberate exception to "absent target
/// is a no-op": a missing xref fails validation (see <c>MediaOps.cs</c>).
///
/// Each op owns its validation (read-only, against the parsed document plus
/// the set of xrefs earlier ops plan to create) and its application (against
/// the live in-memory tree). Both phases share the resolvers in
/// <see cref="Resolve"/> so validation's create/update/no-op prediction
/// cannot drift from apply's behavior.
/// </summary>
public abstract class ChangeOp
{
    public abstract string Kind { get; }

    internal abstract void Validate(ResolutionContext ctx, List<string> errors);
    internal abstract void Apply(ApplyState state, List<string> log);

    /// <summary>
    /// Source xrefs this op attaches as a citation, including any carried by
    /// inline facts on a person it describes. <see cref="ChangesetApplier"/>
    /// uses this, gathered over an item's ops, to decide whether a
    /// <c>newSources[]</c> entry is still needed once <c>--items</c> excludes
    /// the item(s) that cited it. Ops that don't attach citations report none.
    /// </summary>
    internal virtual IEnumerable<string> CitedSources => [];

    internal static ChangeOp ReadOp(JsonElement el)
    {
        string kind = el.GetProperty("op").GetString()
                      ?? throw new JsonException("op kind missing");
        return kind switch
        {
            "createOrUpdateVital"    => CreateOrUpdateVitalOp.Read(el),
            "createOrUpdateSpouse"   => CreateOrUpdateSpouseOp.Read(el),
            "createOrUpdateChild"    => CreateOrUpdateChildOp.Read(el),
            "createOrUpdateParent"   => CreateOrUpdateParentOp.Read(el),
            "createOrUpdateSource"   => CreateOrUpdateSourceOp.Read(el),
            "createOrUpdateCitation" => CreateOrUpdateCitationOp.Read(el),
            "createOrUpdateNote"     => CreateOrUpdateNoteOp.Read(el),
            "createOrUpdateMedia"    => CreateOrUpdateMediaOp.Read(el),
            "deleteVital"            => DeleteVitalOp.Read(el),
            "deleteSpouse"           => DeleteSpouseOp.Read(el),
            "deleteChild"            => DeleteChildOp.Read(el),
            "deleteParent"           => DeleteParentOp.Read(el),
            "deleteSource"           => DeleteSourceOp.Read(el),
            "deleteCitation"         => DeleteCitationOp.Read(el),
            "deleteNote"             => DeleteNoteOp.Read(el),
            "deleteMedia"            => DeleteMediaOp.Read(el),
            "mergePerson"            => MergePersonOp.Read(el),
            _ => throw new JsonException(
                $"unsupported op '{kind}' — dialect v2 ops are createOrUpdate/delete × " +
                "Vital|Spouse|Child|Parent|Source|Citation|Note|Media, plus mergePerson " +
                "(see the gedcom-editing skill)"),
        };
    }
}
