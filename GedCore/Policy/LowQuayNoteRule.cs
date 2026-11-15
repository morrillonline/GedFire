using System.Text.Json;
using GedCore.Drafting;

namespace GedCore.Policy;

/// <summary>
/// Every record that receives a fact whose strongest citation, in the item or already in the document (see
/// <see cref="HeldFacts"/>), is graded at or below the QUAY ceiling must be named by a verification note: one on that
/// record naming the fact (see <see cref="VerificationNoteText.Names"/>), in the item or already in the document,
/// or for a new person one naming the person's given name, or for a marriage one mentioning marriage.
/// </summary>
internal sealed class LowQuayNoteRule(int maxQuay, string notePrefix) : IPolicyRule
{
    sealed record Owner(string Label, string? Xref = null, string? Tag = null, string? GivenName = null, bool Marriage = false);

    public IEnumerable<string> Check(IReadOnlyList<PolicyItem> items, IRecordHistory document) =>
        items.SelectMany(item => ItemViolations(item, document));

    IEnumerable<string> ItemViolations(PolicyItem item, IRecordHistory document)
    {
        var owners = item.Ops.SelectMany(op => Owners(op, document)).Concat(item.People.SelectMany(PersonFacts)).ToList();
        if (owners.Count == 0) yield break;

        var notes = item.Ops
            .Where(op => OpJson.String(op, "op") == "createOrUpdateNote" && Prefixed(OpJson.String(op, "text")))
            .ToList();
        foreach (var owner in owners.Where(o => !notes.Any(n => Names(n, o)) && !HeldNoteNames(item, o, document)))
            yield return $"item {item.Number}: the {owner.Label} is graded {maxQuay} or lower but no note containing \"{notePrefix}\" names it — put one on that record, or name it in an existing one";
    }

    bool Prefixed(string? text) => text is not null && text.Contains(notePrefix, StringComparison.OrdinalIgnoreCase);

    bool Names(JsonElement note, Owner owner)
    {
        string text = OpJson.String(note, "text")!;
        if (owner.Xref is not null) return OpJson.String(note, "record") == owner.Xref && VerificationNoteText.Names(text, notePrefix, owner.Tag);
        if (owner.Marriage) return text.Contains("marriage", StringComparison.OrdinalIgnoreCase);
        return owner.GivenName is { Length: > 0 } && text.Contains(owner.GivenName, StringComparison.OrdinalIgnoreCase);
    }

    // A note the record already holds counts unless this item rewrites or deletes it.
    bool HeldNoteNames(PolicyItem item, Owner owner, IRecordHistory document)
    {
        if (owner.Xref is null) return false;
        var replaced = item.Ops
            .Where(op => OpJson.String(op, "record") == owner.Xref)
            .Select(op => OpJson.String(op, "op") switch
            {
                "createOrUpdateNote" => OpJson.String(op, "match"),
                "deleteNote" => OpJson.String(op, "text"),
                _ => null,
            })
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);
        return document.NotesOn(owner.Xref).Any(text => !replaced.Contains(text) && VerificationNoteText.Names(text, notePrefix, owner.Tag));
    }

    IEnumerable<Owner> Owners(JsonElement op, IRecordHistory document)
    {
        switch (OpJson.String(op, "op"))
        {
            case "createOrUpdateVital" or "createOrUpdateCitation":
                if (OpJson.String(op, "record") is { } record && LowGrade(op, HeldBest(op, record, document)))
                    yield return new Owner($"{OpJson.String(op, "fact") ?? "fact"} on {record}", Xref: record, Tag: OpJson.String(op, "fact"));
                break;
            case "createOrUpdateSpouse":
                foreach (var owner in InlineFacts(op, "spouse")) yield return owner;
                if (OpJson.Property(op, "marriage") is { } marriage && LowGrade(marriage))
                    yield return new Owner($"marriage (family {OpJson.String(op, "family")})", Marriage: true);
                break;
            case "createOrUpdateChild":
                foreach (var owner in InlineFacts(op, "child")) yield return owner;
                break;
            case "createOrUpdateParent":
                foreach (var owner in InlineFacts(op, "parent")) yield return owner;
                break;
        }
    }

    // A new person's facts: declared in the item's people, or described inline by a legacy op.
    IEnumerable<Owner> InlineFacts(JsonElement op, string key) =>
        OpJson.Property(op, key) is { ValueKind: JsonValueKind.Object } person ? PersonFacts(person) : [];

    IEnumerable<Owner> PersonFacts(JsonElement person)
    {
        if (OpJson.Property(person, "facts") is not { ValueKind: JsonValueKind.Array } facts) yield break;

        string name = OpJson.String(person, "name") ?? "";
        string given = name.Replace("/", " ").Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        foreach (var fact in facts.EnumerateArray().Where(f => LowGrade(f)))
            yield return new Owner($"{OpJson.String(fact, "fact")} of {name}", GivenName: given);
    }

    // A stronger citation beside a weak one lifts the fact, whether in the item or already held; ungraded citations say
    // nothing either way, and a fact the item does not grade is not judged.
    bool LowGrade(JsonElement node, int? heldBest = null)
    {
        var graded = OpJson.CitationsOf(node).Where(c => OpJson.String(c, "source") is not null).Select(OpJson.Quay).OfType<int>().ToList();
        return graded.Count > 0 && Math.Max(graded.Max(), heldBest ?? int.MinValue) <= maxQuay;
    }

    // The fact's date picks the held instance of a repeatable tag: the instance the op selects, or the one it writes.
    static int? HeldBest(JsonElement op, string record, IRecordHistory document) =>
        OpJson.String(op, "fact") is { } tag
            ? HeldFacts.BestQuay(document, record, tag,
                OpJson.String(OpJson.Property(op, "match") ?? default, "date")
                ?? OpJson.String(op, "date")
                ?? OpJson.String(OpJson.Property(op, "value") ?? default, "date"))
            : null;
}
