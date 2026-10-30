using System.Text.Json;

namespace GedCore.Policy;

/// <summary>
/// Every record that receives a fact whose strongest citation is graded at or below the
/// QUAY ceiling must be named by a note containing the verification prefix: on that record,
/// naming the person's given name, or (for a marriage) mentioning marriage.
/// </summary>
internal sealed class LowQuayNoteRule(int maxQuay, string notePrefix) : IPolicyRule
{
    sealed record Owner(string Label, string? Xref = null, string? GivenName = null, bool Marriage = false);

    public IEnumerable<string> Check(IReadOnlyList<PolicyItem> items) => items.SelectMany(ItemViolations);

    IEnumerable<string> ItemViolations(PolicyItem item)
    {
        var owners = item.Ops.SelectMany(Owners).ToList();
        if (owners.Count == 0) yield break;

        var notes = item.Ops
            .Where(op => OpJson.String(op, "op") == "createOrUpdateNote" && Prefixed(OpJson.String(op, "text")))
            .ToList();
        foreach (var owner in owners.Where(o => !notes.Any(n => Names(n, o))))
            yield return $"item {item.Number}: the {owner.Label} is graded {maxQuay} or lower but no note containing \"{notePrefix}\" names it — put one on that record, or name it in an existing one";
    }

    bool Prefixed(string? text) => text is not null && text.Contains(notePrefix, StringComparison.OrdinalIgnoreCase);

    static bool Names(JsonElement note, Owner owner)
    {
        if (owner.Xref is not null && OpJson.String(note, "record") == owner.Xref) return true;
        string text = OpJson.String(note, "text")!.ToLowerInvariant();
        if (owner.Marriage) return text.Contains("marriage");
        return owner.GivenName is { Length: > 0 } && text.Contains(owner.GivenName.ToLowerInvariant());
    }

    IEnumerable<Owner> Owners(JsonElement op)
    {
        switch (OpJson.String(op, "op"))
        {
            case "createOrUpdateVital" or "createOrUpdateCitation":
                if (LowGrade(op))
                    yield return new Owner($"{OpJson.String(op, "fact") ?? "fact"} on {OpJson.String(op, "record")}", Xref: OpJson.String(op, "record"));
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

    IEnumerable<Owner> InlineFacts(JsonElement op, string key)
    {
        if (OpJson.Property(op, key) is not { ValueKind: JsonValueKind.Object } person
            || OpJson.Property(person, "facts") is not { ValueKind: JsonValueKind.Array } facts) yield break;

        string name = OpJson.String(person, "name") ?? "";
        string given = name.Replace("/", " ").Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        foreach (var fact in facts.EnumerateArray().Where(LowGrade))
            yield return new Owner($"{OpJson.String(fact, "fact")} of {name}", GivenName: given);
    }

    // A stronger citation beside a weak one lifts the fact; ungraded citations say nothing either way.
    bool LowGrade(JsonElement node) =>
        OpJson.CitationsOf(node).Where(c => OpJson.String(c, "source") is not null).Select(OpJson.Quay).OfType<int>()
            .DefaultIfEmpty(int.MaxValue).Max() <= maxQuay;
}
