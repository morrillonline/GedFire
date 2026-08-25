# Two `createOrUpdateParent` ops against one freshly-created family — design

Status: draft, not yet implemented. Written for review before any code
lands. Fixes a known, tested limitation (`ApplyRelationshipTests.
Parent_SecondParentOpOnFreshlyCreatedFamily_FailsCleanly_NotCrash`), not a
new capability.

## Problem

A changeset that adds both parents to a person with no existing parent
family — the ordinary "add father, add mother" case — cannot express it as
two `createOrUpdateParent` ops naming the same new family:

```json
{ "op": "createOrUpdateParent", "person": "@I4@", "role": "father",
  "parent": { "xref": "@NewI1@", "name": "Cornelius /Ashworth/" },
  "family": "@NewF1@" },
{ "op": "createOrUpdateParent", "person": "@I4@", "role": "mother",
  "parent": { "xref": "@NewI2@", "name": "Beatrice /Fenwick/" },
  "family": "@NewF1@" }
```

`validate_changeset` and `--dry-run` both pass this cleanly. The real,
non-dry-run apply fails the second op with `apply-time invariant violated:
@F00004@ exists but is not a FAMC of @I00004@ (use createOrUpdateChild to
place the person in it first)`, and nothing is written (the applier's
existing all-or-nothing guarantee holds — this is a clean failure, not
corruption). The caller has to know, in advance, to route the second parent
through `createOrUpdateSpouse` naming the same family instead — a
workaround the changeset dialect does not document anywhere and validation
gives no hint is needed.

This is not hypothetical. The downstream `gedcom-editing` skill
(morrillonline/morrillonline, a GedFire consumer) has carried a hand-written
anti-pattern check for exactly this shape since W7 — a local, best-effort
guess at which changesets will hit it, run before ever calling
`validate_changeset`, because the server's own validation cannot tell the
caller in advance.

## Root cause

`GedRecord`'s two-way link for a child in a family is written in two
places, on two different schedules:

- The family's `CHIL` pointer is written **eagerly**, inside
  `Kin.EnsureChild` (`GedCore/Apply/Ops/Kin.cs`), the moment the op that
  creates the family applies.
- The person's reciprocal `FAMC` pointer is only **queued** —
  `ApplyState.PendFamc` appends to `PendingFamc`, a
  `Dictionary<string, List<string>>` — and not actually written until
  `ChangesetApplier.ApplyFamcBackLinks` runs, once, after every item's ops
  have all applied (`GedCore/Apply/ChangesetApplier.cs`).

This batching is deliberate and worth keeping: it dedupes a redundant
back-link, and it inserts each new `FAMC` node at one consistent anchor
position ("file convention: FAMC follows FAMS and precedes the trailing
UID") regardless of how many ops touched the person or in what order —
doing this per-op instead would make node placement depend on apply order.

The second `createOrUpdateParent` op's family-ownership check —
`Resolve.ParentFamily` (`GedCore/Apply/Resolution.cs`) — reads only the
person's live `FAMC` children to decide whether the named family actually
belongs to them:

```csharp
var famcs = person.ChildrenByTag("FAMC").Select(f => f.Value).ToList();
...
if (named.Family is not null && !famcs.Contains(familyXref))
    return FamilyResolution.Fail(
        $"{familyXref} exists but is not a FAMC of {person.Xref} " +
        "(use createOrUpdateChild to place the person in it first)");
```

Mid-run, after the first op but before `ApplyFamcBackLinks`, `famcs` is
still empty for a person whose only family membership came from an op in
*this same run* — even though the family, on its own side, already
correctly lists the person as `CHIL`. The check is asking the wrong
question: "does the person already know about this family" instead of "is
this family, right now, actually the person's family." Those two facts are
supposed to be kept in sync, and are — just not yet, because the sync step
is deferred.

`createOrUpdateChild`'s family resolution (`Resolve.ChildFamily`) has no
analogous ownership check, so the identical two-ops-one-new-family shape
does not fail there. Neither does `createOrUpdateSpouse`'s — which is
exactly why routing the second parent through it is the documented
workaround today.

## Requirements

1. **Two `createOrUpdateParent` ops naming the same freshly-created family,
   in one item or across items in one changeset, must succeed** — this is
   the single most common way to add two new parents to a person, and it
   should not require the caller to know an internal implementation detail
   to route around a false rejection.
2. **The pre-existing protection stays intact.** A `createOrUpdateParent`
   naming a real, already-existing family the person is *not* actually a
   child of must still fail with the current message. This check exists to
   catch a genuine mistake (a caller supplying the wrong family xref); the
   fix must not weaken it for that case.
3. **No behavior change at validation time.** `validate_changeset` and
   `--dry-run` already pass this shape; they must keep doing so, for the
   same reason they do today (`ResolutionContext.Planned` already tracks a
   same-run mint correctly across ops). Only the apply-time check changes.
4. **No change to back-link batching.** `PendingFamc`/`PendingFams` and the
   single end-of-run `ApplyFamcBackLinks`/`ApplyFamsBackLinks` pass stay
   exactly as they are — the fix must not make back-link writes eager, and
   must not require `ChangesetApplier` or `ApplyState`'s shape to change.
5. **No new changeset field, no new op.** Callers keep writing the same two
   `createOrUpdateParent` ops shown in Problem, unchanged. The dialect
   surface — `describe_changeset_ops`, `changeset-lib.js`,
   `compose-changeset.js` on the consumer side — needs no update once this
   lands; the anti-pattern check and the `createOrUpdateSpouse` workaround
   documentation become dead advice that can be deleted from the consumer
   skill in a follow-up, not part of this change.

## Fix

Make `Resolve.ParentFamily`'s ownership check (`GedCore/Apply/Resolution.cs`,
currently lines 218–227) symmetric: a family "belongs" to the person if
*either* side's pointer says so — the person's `FAMC` list (today's only
signal), *or* the named family's own `CHIL` list, which `Kin.EnsureChild`
(`GedCore/Apply/Ops/Kin.cs:143-154`) already writes immediately and
unconditionally whenever a `createOrUpdateParent`/`createOrUpdateChild` op
creates or targets that family.

Replace this block:

```csharp
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
    ...
```

with:

```csharp
public static FamilyResolution ParentFamily(
    ResolutionContext ctx, GedRecord person, string? familyXref)
{
    var famcs = person.ChildrenByTag("FAMC").Select(f => f.Value).ToList();
    if (familyXref is not null)
    {
        var named = NamedFamily(ctx, familyXref);
        bool ownsViaFamc = famcs.Contains(familyXref);
        bool ownsViaChil = named.Family?.Children.Any(
            c => c.Tag == "CHIL" && c.Value == person.Xref) ?? false;
        if (named.Family is not null && !ownsViaFamc && !ownsViaChil)
            return FamilyResolution.Fail(
                $"{familyXref} exists but is not a FAMC of {person.Xref} " +
                "(use createOrUpdateChild to place the person in it first)");
        return named;
    }
    ...
```

Only the `familyXref is not null` branch changes. The `famcs.Count switch`
branch below it (no family named, resolve the person's single/ambiguous/
absent FAMC) is untouched — that branch never sees a same-run family at
all, since a person with zero pre-existing FAMC always falls to its `0 =>`
case, which is exactly what routes the caller to name a new family xref in
the first place.

This needs no new parameter threaded through `ParentFamily`'s signature and
no access to `ApplyState`: `named.Family`, when non-null, is already the
live, current-run `GedRecord` for the family — at apply time (both call
sites, `RelationshipOps.cs:311` during `Validate` and `:336` during
`Apply`) it reflects every mutation applied so far in this run, including
the first op's eager `CHIL` write, because `NamedFamily` resolves it via
`ctx.Existing(familyXref)` against the live `GedDocument` (`state.Doc` at
apply time), not a snapshot; at validation time (before any mutation), it
is either null (the planned-but-not-yet-created case — `named.Family` is
null because `NamedFamily` returns `Planned: true` with no record, so
`ownsViaChil` is trivially false and the check behaves exactly as today)
or the real pre-existing family record, whose `CHIL` list is already
accurate on disk — so requirement 2's rejection case is unaffected either
way.

Note the two call sites build the `ResolutionContext` differently:
`Validate` reuses the changeset-wide `ctx` (so `ctx.Planned`/`Placeholders`
carry state across ops), while `Apply` (`RelationshipOps.cs:334`)
constructs a fresh `new ResolutionContext(state.Doc)` per op — this is
existing behavior, not something this fix touches, and it's why the fix
must not rely on `ctx.Planned` to see the first op's family: by the second
op's apply, the family is no longer "planned", it's already a real
`GedRecord` in `state.Doc`, reachable through `ctx.Existing` inside
`NamedFamily` regardless of which `ResolutionContext` instance is asking.

## Non-goals

- Making `FAMC`/`FAMS` back-link writes eager instead of batched. Rejected
  in Requirements (4); the batching earns its keep on node-ordering and
  dedup, and the ownership check is the actual defect, not the batching.
- Any change to `createOrUpdateChild` or `createOrUpdateSpouse`. Neither
  has this defect today; this document does not touch either op's
  resolution path.
- A general audit of every other op pair for a similar same-run visibility
  gap. This document fixes the one concrete, tested case on file. A
  different op pair with a similar symptom gets its own design pass when
  one is actually found.

## Testing

All new/changed cases live in `GedCore.Tests/ApplyRelationshipTests.cs`,
next to the existing Parent-noun tests (around line 227). They use the same
`WriteBaseFile()` / `RunExpectSuccess(...)` / `Run(...)` / `ReadDoc()`
helpers as the neighboring tests, against the same base fixture where
`@I00004@` is the target person and the first minted family/person in a
fresh run come out as `@F00004@`/`@I00005@` (see
`Parent_NoParentFamily_CreatesOne_WithPersonAsChild`, line 227, for the
exact minted-xref numbering this fixture produces).

- **Rename and flip**
  `Parent_SecondParentOpOnFreshlyCreatedFamily_FailsCleanly_NotCrash`
  (currently line 259) to
  `Parent_TwoParentOpsOnFreshlyCreatedFamily_BothSucceed`. Replace its body
  with `RunExpectSuccess` (not `Run`) on the same two-op JSON already in
  that test (father then mother, both naming `"family": "@NewF1@"`), and
  replace its `Assert.False(result.Success)` / error-message / untouched-
  bytes assertions with:
  - `result.MintedXrefs["@NewI1@"]` == `"@I00005@"`,
    `result.MintedXrefs["@NewI2@"]` == `"@I00006@"`,
    `result.MintedXrefs["@NewF1@"]` == `"@F00004@"`
  - `doc.ByXref["@F00004@"].FirstChild("HUSB")!.Value` ==
    `"@I00005@"`, `.FirstChild("WIFE")!.Value` == `"@I00006@"`
  - `doc.ByXref["@F00004@"].ChildrenByTag("CHIL").Single().Value` ==
    `"@I00004@"`
  - `doc.ByXref["@I00004@"].ChildrenByTag("FAMC").Single().Value` ==
    `"@F00004@"` (exactly one `FAMC`, not two — proves
    `ApplyFamcBackLinks`'s dedup still holds when both ops queue the same
    pending link)
  - `result.Deltas["INDI"]` == 2, `result.Deltas["FAM"]` == 1
  - update the doc comment above the test: it currently describes this as
    a known limitation ("Known limitation, not a crash: ..."); rewrite it
    to state what the test now proves and drop the "route through
    createOrUpdateSpouse instead" advice.
- **Cross-item case** — add
  `Parent_TwoParentOpsOnFreshlyCreatedFamily_AcrossItems_BothSucceed`: the
  identical father/mother ops as above, but split into two `item`s in the
  `items` array (`"item": 1` with the father op, `"item": 2` with the
  mother op) instead of two ops in one item's `ops` list. Same assertions
  as above. This exercises `ChangesetApplier`'s `foreach (var item in
  selectedItems) { state.BeginItem(); ... }` loop
  (`GedCore/Apply/ChangesetApplier.cs:149-152`) — `BeginItem()` resets
  per-item state but `state.Doc` (and therefore the family's `CHIL` list
  written by the first item) persists across the boundary, so this should
  pass for the same reason the single-item case does; the point of the
  test is to pin that down, not to expect different behavior.
- **Validation-time regression guard** — add a case asserting
  `validate_changeset` (or the harness's dry-run path) still reports success
  for the two-op shape, unchanged from before this fix. (Requirement 3.) If
  an existing validate/dry-run test already covers this shape, extend its
  assertions instead of adding a new test.
- Keep `Parent_SecondParentOnFreshlyCreatedFamily_ViaCreateOrUpdateSpouse_
  Works` (currently line 285) passing unchanged, verbatim — the workaround
  path must keep working even though it stops being necessary.
- **Requirement 2 regression guard** — add
  `Parent_NamedFamily_PersonNotActuallyAChild_FailsCleanly`: using the base
  fixture (`ApplyTestBase.BaseLines`), a `createOrUpdateParent` op on
  `@I00004@` (Edna: `FAMS @F00002@`/`@F00003@`, no `FAMC` at all) naming
  `"family": "@F00001@"` — the pre-existing family whose `CHIL` is
  `@I00001@` (Allen), not Edna, so Edna is unrelated to it on both sides.
  Assert `result.Success` is `false`, the error contains `"exists but is
  not a FAMC of @I00004@"`, and `ReadBytes() == original` (untouched,
  matching the existing pattern in the test being replaced above).
