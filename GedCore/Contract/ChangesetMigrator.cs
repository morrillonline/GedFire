using System.Text.Json;
using System.Text.Json.Nodes;
using GedCore.Apply;

namespace GedCore.Contract;

/// <summary>
/// A legacy changeset rewritten into the canonical form: what was changed, and what the contract still reports on the
/// rewritten file because no rule can decide it (a misspelled field, a date given as an event's value). A changeset
/// that does not satisfy the contract at all, or that is ambiguous, is refused with no rewrite.
/// </summary>
public sealed record ChangesetMigration(
    JsonObject? Changeset, IReadOnlyList<string> Changes, IReadOnlyList<string> Remaining, IReadOnlyList<string> Errors)
{
    public bool Success => Errors.Count == 0;
}

/// <summary>
/// Rewrites a changeset in the legacy dialect into the canonical one: a singular citation becomes citations, a
/// relationship op's citations become familyCitations, an object value becomes date and place, a string match becomes
/// {value}, and each inline new person moves into its item's people. Never chooses between two citation keys.
/// </summary>
public static class ChangesetMigrator
{
    static readonly string[] Relationships = ["createOrUpdateSpouse", "createOrUpdateChild", "createOrUpdateParent"];
    static readonly string[] PersonFields = ["spouse", "child", "parent"];
    static readonly string[] FactCiting = ["createOrUpdateVital", "createOrUpdateCitation", "createOrUpdateNote"];
    static readonly string[] FactSelecting = ["createOrUpdateVital", "deleteVital", "createOrUpdateCitation", "deleteCitation"];

    public static ChangesetMigration Migrate(Stream json)
    {
        JsonNode? root;
        try { root = JsonNode.Parse(json); }
        catch (JsonException ex) { return Refused([$"not valid JSON: {ex.Message}"]); }
        if (root is not JsonObject changeset) return Refused(["a changeset must be a JSON object"]);

        var before = Check(changeset);
        if (!before.IsValid) return Refused(before.Errors);

        var run = new Run();
        run.Changeset(changeset);
        if (run.Errors.Count > 0) return Refused(run.Errors);

        var after = Check(changeset);
        return after.IsValid ? new ChangesetMigration(changeset, run.Changes, after.Warnings, []) : Refused(after.Errors);
    }

    static ChangesetMigration Refused(IReadOnlyList<string> errors) => new(null, [], [], errors);

    static ContractReport Check(JsonObject changeset)
    {
        using var document = JsonDocument.Parse(changeset.ToJsonString());
        return ChangesetContract.Check(document.RootElement);
    }

    // The people one item declares, keyed by xref, with where each declaration came from.
    sealed class ItemPeople(JsonObject item, string path)
    {
        public JsonObject Item { get; } = item;
        public string Path { get; } = path;
        public List<JsonObject> Entries { get; } = [];
        public Dictionary<string, string> Origins { get; } = new(StringComparer.Ordinal);
    }

    sealed class Run
    {
        public List<string> Changes { get; } = [];
        public List<string> Errors { get; } = [];

        // The first description of each new person anywhere in the changeset, for an item that names it without one.
        readonly Dictionary<string, (JsonObject Person, string Path)> _described = new(StringComparer.Ordinal);

        public void Changeset(JsonObject root)
        {
            if (!root.ContainsKey("schemaVersion"))
            {
                root.Insert(0, "schemaVersion", ChangesetDialect.SchemaVersion);
                Changes.Add($"schemaVersion: set to \"{ChangesetDialect.SchemaVersion}\"");
            }

            foreach (var (group, i) in Objects(root["newSources"]))
                foreach (var (op, j) in Objects(group["ops"]))
                    Op(op, $"newSources[{i}].ops[{j}]", null);

            var items = Objects(root["items"]).Select(entry => Item(entry.Node, $"items[{entry.Index}]")).ToList();
            foreach (var people in items)
            {
                DeclareNamedPeople(people);
                WritePeople(people);
            }
            RemoveNulls(root, "");
        }

        // The contract reads a null optional field as absent, so the canonical form omits it.
        void RemoveNulls(JsonNode? node, string path)
        {
            if (node is JsonArray array)
                for (int i = 0; i < array.Count; i++) RemoveNulls(array[i], $"{path}[{i}]");
            if (node is not JsonObject obj) return;
            foreach (var key in obj.Where(p => p.Value is null).Select(p => p.Key).ToList())
            {
                obj.Remove(key);
                Changes.Add($"{Join(path, key)}: null removed");
            }
            foreach (var (key, value) in obj) RemoveNulls(value, Join(path, key));
        }

        static string Join(string path, string key) => path.Length == 0 ? key : $"{path}.{key}";

        ItemPeople Item(JsonObject item, string path)
        {
            var people = new ItemPeople(item, path);
            foreach (var (person, i) in Objects(item["people"]))
            {
                foreach (var (fact, k) in Objects(person["facts"])) Fact(fact, $"{path}.people[{i}].facts[{k}]");
                people.Entries.Add(person);
                if ((string?)person["xref"] is { } xref) people.Origins[xref] = $"{path}.people[{i}]";
            }
            foreach (var (op, j) in Objects(item["ops"]))
                Op(op, $"{path}.ops[{j}]", people);
            return people;
        }

        void Op(JsonObject op, string path, ItemPeople? people)
        {
            string? kind = (string?)op["op"];
            if (Relationships.Contains(kind))
            {
                FamilyCitations(op, path);
                if (op["marriage"] is JsonObject marriage) Citations(marriage, $"{path}.marriage");
                if (people is not null)
                    foreach (string field in PersonFields) MovePerson(op, field, path, people);
            }
            else if (FactCiting.Contains(kind)) Citations(op, path);

            if (kind == "createOrUpdateVital") Value(op, path);
            if (FactSelecting.Contains(kind)) Match(op, path);
            if (kind == "mergePerson")
                foreach (var (fact, i) in Objects(op["facts"])) Fact(fact, $"{path}.facts[{i}]");
        }

        void Fact(JsonObject fact, string path)
        {
            Citations(fact, path);
            Value(fact, path);
        }

        void Citations(JsonObject node, string path)
        {
            if (!node.ContainsKey("citation")) return;
            Rename(node, "citation", "citations", value => new JsonArray(value));
            Changes.Add($"{path}.citation: now citations, an array");
        }

        void FamilyCitations(JsonObject op, string path)
        {
            string? key = new[] { "citation", "citations" }.FirstOrDefault(op.ContainsKey);
            if (key is null) return;
            Rename(op, key, "familyCitations", value => value as JsonArray ?? new JsonArray(value));
            Changes.Add($"{path}.{key}: renamed familyCitations, the family record's citations");
        }

        void Value(JsonObject node, string path)
        {
            if (node["value"] is not JsonObject value) return;
            int at = node.IndexOf("value");
            node.Remove("value");
            foreach (string field in new[] { "place", "date" })
                if (value[field] is { } given)
                {
                    value.Remove(field);
                    node.Insert(at, field, given);
                }
            Changes.Add($"{path}.value: date and place moved beside it");
        }

        void Match(JsonObject op, string path)
        {
            if (op["match"] is not JsonValue match || match.GetValueKind() != JsonValueKind.String) return;
            op["match"] = new JsonObject { ["value"] = match.GetValue<string>() };
            Changes.Add($"{path}.match: now {{\"value\": ...}}");
        }

        void MovePerson(JsonObject op, string field, string path, ItemPeople people)
        {
            if (op[field] is not JsonObject person || (string?)person["xref"] is not { } xref) return;
            string at = $"{path}.{field}";
            op[field] = xref;
            if (!person.ContainsKey("name"))
            {
                Changes.Add($"{at}: a link to {xref}, now its xref");
                return;
            }
            foreach (var (fact, i) in Objects(person["facts"])) Fact(fact, $"{at}.facts[{i}]");
            _described.TryAdd(xref, (person, at));

            if (people.Entries.FirstOrDefault(p => (string?)p["xref"] == xref) is { } declared)
            {
                if (!Compatible(declared, person))
                    Errors.Add($"{at}: describes {xref} differently from {people.Origins[xref]}; reconcile them before migrating");
                else
                    Changes.Add($"{at}: inline person {xref} replaced by its xref, declared in {people.Path}.people");
                return;
            }
            people.Entries.Add((JsonObject)person.DeepClone());
            people.Origins[xref] = at;
            Changes.Add($"{at}: inline person {xref} moved to {people.Path}.people");
        }

        // An item that names a new person described only in another item declares it too, from that description.
        void DeclareNamedPeople(ItemPeople people)
        {
            foreach (var (op, _) in Objects(people.Item["ops"]).Where(o => Relationships.Contains((string?)o.Node["op"])))
                foreach (string field in PersonFields)
                {
                    if (op[field] is not JsonValue named || named.GetValueKind() != JsonValueKind.String) continue;
                    string xref = named.GetValue<string>();
                    if (!Placeholder.IsPlaceholder(xref) || people.Origins.ContainsKey(xref)
                        || !_described.TryGetValue(xref, out var described)) continue;
                    people.Entries.Add((JsonObject)described.Person.DeepClone());
                    people.Origins[xref] = described.Path;
                    Changes.Add($"{people.Path}.people: declares {xref}, as described at {described.Path}");
                }
        }

        static void WritePeople(ItemPeople people)
        {
            if (people.Entries.Count == 0) return;
            var array = new JsonArray([.. people.Entries.Select(e => e.Parent is null ? e : e.DeepClone())]);
            var item = people.Item;
            if (item.ContainsKey("people")) item["people"] = array;
            else item.Insert(item.ContainsKey("ops") ? item.IndexOf("ops") : item.Count, "people", array);
        }

        // A later description agrees with the first when it adds nothing the first lacks: the same name, sex and facts.
        static bool Compatible(JsonObject first, JsonObject other) =>
            (string?)first["name"] == (string?)other["name"]
            && (other["sex"] is null || (string?)first["sex"] == (string?)other["sex"])
            && (other["facts"] is not JsonArray { Count: > 0 } facts || JsonNode.DeepEquals(first["facts"], facts));

        // Renames a key where it stands, so the rewritten file reads in the same order.
        static void Rename(JsonObject node, string oldKey, string newKey, Func<JsonNode?, JsonNode> convert)
        {
            int at = node.IndexOf(oldKey);
            var value = node[oldKey];
            node.Remove(oldKey);
            node.Insert(at, newKey, convert(value));
        }
    }

    static List<(JsonObject Node, int Index)> Objects(JsonNode? array) =>
        array is JsonArray list
            ? [.. list.Select((node, i) => (node, i)).Where(e => e.node is JsonObject).Select(e => ((JsonObject)e.node!, e.i))]
            : [];
}
