using System.Text.Json;
using GedCore.Apply;

namespace GedCore.Contract;

/// <summary>What the contract check found: errors that refuse the changeset, and warnings about legacy shapes.</summary>
public sealed record ContractReport(IReadOnlyList<string> Errors, IReadOnlyList<string> Warnings)
{
    public bool IsValid => Errors.Count == 0;
}

/// <summary>
/// Checks a changeset against <see cref="ChangesetDialect"/>, naming the path of every fault. A shape the reader cannot
/// read, an unsupported version, or a fault in the canonical form is an error. A legacy shape (a singular citation, a
/// relationship op's citations, an object value, a string match, an inline person, a field the dialect does not
/// define) is still read, and is reported as a warning until the contract is enforced.
/// </summary>
public static class ChangesetContract
{
    static readonly string[] PersonFields = ["spouse", "child", "parent"];

    public static ContractReport Check(JsonElement root)
    {
        var check = new Run();
        check.Envelope(root);
        return new ContractReport(check.Errors, check.Warnings);
    }

    /// <summary>The fact payload fields an object gives, with an object value read as the legacy event form.</summary>
    sealed record PayloadFields(string? Value, bool HasDate, bool HasPlace);

    sealed class Run
    {
        public List<string> Errors { get; } = [];
        public List<string> Warnings { get; } = [];

        public void Envelope(JsonElement root)
        {
            if (root.ValueKind != JsonValueKind.Object)
            {
                Errors.Add("a changeset must be a JSON object");
                return;
            }
            if (!VersionSupported(root)) return;
            Shape(root, ChangesetDialect.Shape(ChangesetDialect.Envelope), "");
        }

        bool VersionSupported(JsonElement root)
        {
            if (!root.TryGetProperty("schemaVersion", out var version))
            {
                Warnings.Add($"schemaVersion: missing; a changeset names the contract version it is written to, \"{ChangesetDialect.SchemaVersion}\"");
                return true;
            }
            string? text = version.ValueKind == JsonValueKind.String ? version.GetString() : null;
            string[] parts = text?.Split('.') ?? [];
            if (parts.Length != 2 || !parts.All(p => p.Length > 0 && p.All(char.IsAsciiDigit)))
            {
                Errors.Add($"schemaVersion: must be a \"<major>.<minor>\" string such as \"{ChangesetDialect.SchemaVersion}\"");
                return false;
            }
            if (int.Parse(parts[0]) != ChangesetDialect.SupportedMajor)
            {
                Errors.Add($"schemaVersion: version {text} is not supported; this GedFire reads changeset version {ChangesetDialect.SupportedMajor}.x");
                return false;
            }
            return true;
        }

        void Shape(JsonElement node, DialectShape shape, string path)
        {
            if (node.ValueKind != JsonValueKind.Object)
            {
                Errors.Add($"{Label(path)}: must be an object");
                return;
            }

            var handled = new HashSet<string>(StringComparer.Ordinal);
            Legacy(node, shape, path, handled);
            foreach (var property in node.EnumerateObject().Where(p => !handled.Contains(p.Name)))
            {
                string at = Join(path, property.Name);
                if (shape.Field(property.Name) is { } field) Value(property.Value, field, at);
                else Warnings.Add($"{at}: unknown field; {Describe(shape)} takes {string.Join(", ", shape.Fields.Select(f => f.Name))}");
            }
            foreach (var field in shape.Fields.Where(f => f.Required && !node.TryGetProperty(f.Name, out _) && !SatisfiedByLegacy(f, handled)))
                Errors.Add($"{Label(path)}: required field \"{field.Name}\" is missing");

            Semantics(node, shape, path);
            if (shape.Name == "item") People(node, path);
        }

        void Value(JsonElement value, DialectField field, string path)
        {
            if (value.ValueKind == JsonValueKind.Null && Nullable(field))
            {
                Warnings.Add($"{path}: null; omit the field instead");
                return;
            }
            switch (field.Type)
            {
                case DialectType.String when value.ValueKind != JsonValueKind.String:
                    Errors.Add($"{path}: must be a string");
                    break;
                case DialectType.Integer when value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out _):
                    Errors.Add($"{path}: must be an integer");
                    break;
                case DialectType.Boolean when value.ValueKind is not (JsonValueKind.True or JsonValueKind.False):
                    Errors.Add($"{path}: must be true or false");
                    break;
                case DialectType.StringArray:
                    if (value.ValueKind != JsonValueKind.Array) Errors.Add($"{path}: must be an array of strings");
                    else foreach (var (element, i) in value.EnumerateArray().Select((e, i) => (e, i)))
                            if (element.ValueKind != JsonValueKind.String) Errors.Add($"{path}[{i}]: must be a string");
                    break;
                case DialectType.Object:
                    Shape(value, ChangesetDialect.Shape(field.Shape!), path);
                    break;
                case DialectType.ObjectArray:
                    if (value.ValueKind != JsonValueKind.Array)
                    {
                        Errors.Add($"{path}: must be an array");
                        break;
                    }
                    foreach (var (element, i) in value.EnumerateArray().Select((e, i) => (e, i)))
                        if (field.Shape == ChangesetDialect.Op) Op(element, $"{path}[{i}]");
                        else Shape(element, ChangesetDialect.Shape(field.Shape!), $"{path}[{i}]");
                    break;
            }
        }

        void Op(JsonElement op, string path)
        {
            if (op.ValueKind != JsonValueKind.Object)
            {
                Errors.Add($"{path}: must be an op object");
                return;
            }
            if (!op.TryGetProperty("op", out var kind) || kind.ValueKind != JsonValueKind.String)
            {
                Errors.Add($"{path}.op: required, the op's kind");
                return;
            }
            if (ChangesetDialect.FindOp(kind.GetString()) is not { } shape)
            {
                Errors.Add($"{path}.op: unknown op \"{kind.GetString()}\"; the dialect's ops are {string.Join(", ", ChangesetDialect.Ops.Select(o => o.Name))}");
                return;
            }
            Shape(op, shape, path);
        }

        // The legacy shapes the reader still accepts: each is read, warned about, and kept out of the unknown-field check.
        void Legacy(JsonElement node, DialectShape shape, string path, HashSet<string> handled)
        {
            if (shape.Field("familyCitations") is not null) RelationshipCitations(node, shape, path, handled);
            else if (shape.Field("citations") is not null) SingularCitation(node, path, handled);
            if (shape.Field("value") is { Type: DialectType.String } && shape.Field("date") is not null) ObjectValue(node, path, handled);
            if (shape.Field("match") is { Shape: "factMatch" }) StringMatch(node, path, handled);
            foreach (string field in PersonFields)
                if (shape.Name.StartsWith("createOrUpdate", StringComparison.Ordinal) && shape.Field(field) is not null)
                    InlinePerson(node, field, path, handled);
        }

        void SingularCitation(JsonElement node, string path, HashSet<string> handled)
        {
            if (!node.TryGetProperty("citation", out var citation)) return;
            handled.Add("citation");
            if (node.TryGetProperty("citations", out _))
            {
                Errors.Add($"{Label(path)}: carries both citation and citations; give every citation in citations");
                return;
            }
            Warnings.Add($"{Join(path, "citation")}: use citations, an array, for every citation");
            Shape(citation, ChangesetDialect.Shape("citation"), Join(path, "citation"));
        }

        void RelationshipCitations(JsonElement node, DialectShape shape, string path, HashSet<string> handled)
        {
            string[] given = [.. new[] { "citation", "citations" }.Where(k => node.TryGetProperty(k, out _))];
            if (given.Length == 0) return;
            handled.UnionWith(given);
            if (given.Length > 1 || node.TryGetProperty("familyCitations", out _))
            {
                string keys = string.Join(" and ", node.TryGetProperty("familyCitations", out _) ? ["familyCitations", .. given] : given);
                Errors.Add($"{Label(path)}: carries {keys}; give the family record's citations in familyCitations only");
                return;
            }
            string at = Join(path, given[0]);
            Warnings.Add($"{at}: on {shape.Name} the op-level citations cite the family record; rename it familyCitations, an array");
            var citation = ChangesetDialect.Shape("citation");
            var value = node.GetProperty(given[0]);
            if (given[0] == "citation") Shape(value, citation, at);
            else if (value.ValueKind != JsonValueKind.Array) Errors.Add($"{at}: must be an array");
            else foreach (var (element, i) in value.EnumerateArray().Select((e, i) => (e, i))) Shape(element, citation, $"{at}[{i}]");
        }

        void ObjectValue(JsonElement node, string path, HashSet<string> handled)
        {
            if (!node.TryGetProperty("value", out var value) || value.ValueKind != JsonValueKind.Object) return;
            handled.Add("value");
            string at = Join(path, "value");
            if (Has(node, "date") || Has(node, "place"))
            {
                Errors.Add($"{at}: give date and place beside value, not inside it as well");
                return;
            }
            Warnings.Add($"{at}: an object value is the old event form; give date and place as fields beside it");
            foreach (var property in value.EnumerateObject())
                if (property.Name is not ("date" or "place"))
                    Warnings.Add($"{Join(at, property.Name)}: unknown field; an event value takes date, place");
                else if (property.Value.ValueKind == JsonValueKind.Null)
                    Warnings.Add($"{Join(at, property.Name)}: null; omit the field instead");
                else if (property.Value.ValueKind != JsonValueKind.String)
                    Errors.Add($"{Join(at, property.Name)}: must be a string");
        }

        void StringMatch(JsonElement node, string path, HashSet<string> handled)
        {
            if (!node.TryGetProperty("match", out var match) || match.ValueKind != JsonValueKind.String) return;
            handled.Add("match");
            Warnings.Add($"{Join(path, "match")}: a string match is the old form; give {{\"value\": ...}} for a text payload or {{\"date\", \"place\"}} for an event");
        }

        void InlinePerson(JsonElement node, string field, string path, HashSet<string> handled)
        {
            if (!node.TryGetProperty(field, out var person) || person.ValueKind != JsonValueKind.Object) return;
            handled.Add(field);
            string at = Join(path, field);
            if (!person.TryGetProperty("name", out _))
            {
                Warnings.Add($"{at}: an object naming only an existing person; give its xref as a string");
                if (Text(person, "xref") is null) Errors.Add($"{at}.xref: required, a string");
                foreach (var property in person.EnumerateObject().Where(p => p.Name != "xref"))
                    Warnings.Add($"{Join(at, property.Name)}: unknown field; a person link takes only xref");
                return;
            }
            Warnings.Add($"{at}: an inline new person; declare it in the item's people and give only its xref here");
            Shape(person, ChangesetDialect.Shape("person"), at);
        }

        // The legacy reader read a null optional scalar or string list as absent; a null object or op list it could not read.
        static bool Nullable(DialectField field) =>
            !field.Required && field.Type is not (DialectType.Object or DialectType.ObjectArray);

        static bool Has(JsonElement node, string name) =>
            node.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null;

        static bool SatisfiedByLegacy(DialectField field, HashSet<string> handled) =>
            handled.Contains(field.Name) || field.Name == "citations" && handled.Contains("citation");

        // R3: the tag fixes whether a fact takes value, or date and place.
        void Semantics(JsonElement node, DialectShape shape, string path)
        {
            switch (shape.Name)
            {
                case "createOrUpdateVital" or "personFact" or "mergeFact":
                    var given = Payload(node);
                    if (shape.Name == "createOrUpdateVital" && given.Value is null && !given.HasDate && !given.HasPlace && !Has(node, "value"))
                        Errors.Add($"{Label(path)}: createOrUpdateVital needs value, date or place");
                    PayloadRule(Text(node, "fact"), given, path);
                    break;
            }
            if (shape.Field("match") is { Shape: "factMatch" } && node.TryGetProperty("match", out var match))
                PayloadRule(Text(node, "fact"), MatchPayload(match), Join(path, "match"));
        }

        void PayloadRule(string? tag, PayloadFields given, string path)
        {
            switch (FactPayloads.Of(tag))
            {
                case FactPayload.Event when given.Value is not null:
                    Warnings.Add($"{Join(path, "value")}: {tag} is an event; give its date and place, not value");
                    break;
                case FactPayload.Text when given.HasDate || given.HasPlace:
                    Warnings.Add($"{Join(path, given.HasDate ? "date" : "place")}: {tag} takes a text value, not a date or place");
                    break;
                case FactPayload.Described when !given.HasDate && FactPayloads.LooksLikeMisplacedDate(tag!, given.Value):
                    Warnings.Add($"{Join(path, "value")}: \"{given.Value}\" is a date; give it as date");
                    break;
            }
        }

        static PayloadFields Payload(JsonElement node)
        {
            bool date = Has(node, "date"), place = Has(node, "place");
            if (node.TryGetProperty("value", out var value) && value.ValueKind == JsonValueKind.Object)
                return new PayloadFields(null, date || Has(value, "date"), place || Has(value, "place"));
            return new PayloadFields(Text(node, "value"), date, place);
        }

        static PayloadFields MatchPayload(JsonElement match) => match.ValueKind == JsonValueKind.String
            ? new PayloadFields(match.GetString(), false, false)
            : match.ValueKind == JsonValueKind.Object
                ? new PayloadFields(Text(match, "value"), Has(match, "date"), Has(match, "place"))
                : new PayloadFields(null, false, false);

        // R4: each new person an item creates is declared once in its people and named by xref in a person field.
        void People(JsonElement item, string path)
        {
            var declared = new Dictionary<string, int>(StringComparer.Ordinal);
            if (item.TryGetProperty("people", out var people) && people.ValueKind == JsonValueKind.Array)
                foreach (var (entry, i) in people.EnumerateArray().Select((e, i) => (e, i)))
                {
                    if (Text(entry, "xref") is not { } xref) continue;
                    string at = $"{path}.people[{i}].xref";
                    if (!Placeholder.IsPlaceholder(xref)) Errors.Add($"{at}: people declares new people; give a @New…@ placeholder, not {xref}");
                    else if (!declared.TryAdd(xref, i)) Errors.Add($"{at}: {xref} is declared more than once");
                }

            var named = new HashSet<string>(StringComparer.Ordinal);
            if (item.TryGetProperty("ops", out var ops) && ops.ValueKind == JsonValueKind.Array)
                foreach (var (op, i) in ops.EnumerateArray().Select((o, i) => (o, i)))
                    foreach (string field in PersonFields)
                        PersonReference(op, field, $"{path}.ops[{i}]", path, declared, named);

            foreach (var (xref, i) in declared.Where(d => !named.Contains(d.Key)))
                Errors.Add($"{path}.people[{i}]: {xref} is declared but no op's spouse, child or parent names it");
        }

        void PersonReference(JsonElement op, string field, string opPath, string itemPath,
                             Dictionary<string, int> declared, HashSet<string> named)
        {
            if (op.ValueKind != JsonValueKind.Object || !(Text(op, "op")?.StartsWith("createOrUpdate", StringComparison.Ordinal) ?? false)
                || !op.TryGetProperty(field, out var person)) return;
            string at = Join(opPath, field);
            if (person.ValueKind == JsonValueKind.Object)
            {
                if (Text(person, "xref") is { } inline && declared.ContainsKey(inline))
                    Errors.Add($"{at}: {inline} is declared in {itemPath}.people and inline here; give only its xref");
                return;
            }
            if (person.ValueKind != JsonValueKind.String || !Placeholder.IsPlaceholder(person.GetString())) return;
            string xref = person.GetString()!;
            named.Add(xref);
            if (!declared.ContainsKey(xref))
                Warnings.Add($"{at}: {xref} is not declared in {itemPath}.people; declare each new person the item names");
        }

        static string? Text(JsonElement node, string name) =>
            node.ValueKind == JsonValueKind.Object && node.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() : null;

        static string Describe(DialectShape shape) => ChangesetDialect.FindOp(shape.Name) is not null ? shape.Name : $"a {shape.Name}";

        static string Join(string path, string name) => path.Length == 0 ? name : $"{path}.{name}";

        static string Label(string path) => path.Length == 0 ? "changeset" : path;
    }
}
