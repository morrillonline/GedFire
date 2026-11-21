using System.Text.Json;
using System.Text.RegularExpressions;
using GedCore.Apply;

namespace GedCore.Contract;

/// <summary>What the contract check found: every fault that refuses the changeset, by path.</summary>
public sealed record ContractReport(IReadOnlyList<string> Errors)
{
    public bool IsValid => Errors.Count == 0;
}

/// <summary>
/// Checks a changeset against <see cref="ChangesetDialect"/>, naming the path and the fix of every fault. Anything the
/// dialect does not define is refused: an unknown field, a singular citation, an
/// object value, a payload the fact's tag does not take, an inline person, an undeclared or unnamed new person,
/// a date that is not a GEDCOM 7 date, and a value outside the limits the dialect declares for its field.
/// </summary>
public static class ChangesetContract
{
    static readonly string[] PersonFields = ["spouse", "child", "parent"];

    public static ContractReport Check(JsonElement root)
    {
        var check = new Run();
        check.Envelope(root);
        return new ContractReport(check.Errors);
    }

    /// <summary>The fact payload fields an object gives.</summary>
    sealed record PayloadFields(string? Value, bool HasDate, bool HasPlace);

    sealed class Run
    {
        public List<string> Errors { get; } = [];

        public void Envelope(JsonElement root)
        {
            if (root.ValueKind != JsonValueKind.Object)
            {
                Errors.Add("a changeset must be a JSON object");
                return;
            }
            Shape(root, ChangesetDialect.Shape(ChangesetDialect.Envelope), "");
        }

        void Shape(JsonElement node, DialectShape shape, string path)
        {
            if (node.ValueKind != JsonValueKind.Object)
            {
                Errors.Add($"{Label(path)}: must be an object");
                return;
            }

            foreach (var property in node.EnumerateObject())
            {
                string at = Join(path, property.Name);
                if (shape.Field(property.Name) is not { } field) Errors.Add(UnknownField(shape, property.Name, at));
                else if (MisshapenValue(shape, field, property.Value) is { } fix) Errors.Add($"{at}: {fix}");
                else Value(property.Value, field, at);
            }
            foreach (var field in shape.Fields.Where(f => f.Required && !node.TryGetProperty(f.Name, out _)))
                Errors.Add($"{Label(path)}: required field \"{field.Name}\" is missing");

            Semantics(node, shape, path);
            if (shape.Name == "item") People(node, path);
        }

        // R1 and R2: a field the shape does not define, with the right key when it is a known misspelling of one.
        static string UnknownField(DialectShape shape, string name, string at)
        {
            if (name is "citation" && shape.Field("citations") is not null)
                return $"{at}: use \"citations\", an array";
            if (name is "citation" or "citations" && shape.Field("familyCitations") is not null)
                return $"{at}: use \"familyCitations\", an array; a relationship op's own citations cite the family record";
            return $"{at}: unknown field; {Describe(shape)} takes {string.Join(", ", shape.Fields.Select(f => f.Name))}";
        }

        // R3 and R4: the object forms a string field once took, refused with the form that replaces them.
        static string? MisshapenValue(DialectShape shape, DialectField field, JsonElement value)
        {
            if (value.ValueKind != JsonValueKind.Object || field.Type != DialectType.String) return null;
            if (field.Name == "value" && shape.Field("date") is not null)
                return "an object value is not read; give date and place as fields beside value";
            if (PersonFields.Contains(field.Name) && IsRelationshipOp(shape))
                return "an inline person is not read; declare the person in the item's people and give only its xref here";
            return null;
        }

        void Value(JsonElement value, DialectField field, string path)
        {
            if (value.ValueKind == JsonValueKind.Null && !field.Required)
            {
                Errors.Add($"{path}: null; omit the field instead");
                return;
            }
            switch (field.Type)
            {
                case DialectType.String when value.ValueKind != JsonValueKind.String:
                    Errors.Add($"{path}: must be a string");
                    break;
                case DialectType.String:
                    if (StringLimit(value.GetString()!, field, path) is { } textFault) Errors.Add(textFault);
                    break;
                case DialectType.Integer when value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out _):
                    Errors.Add($"{path}: must be an integer");
                    break;
                case DialectType.Integer:
                    int number = value.GetInt32();
                    if (number < (field.Minimum ?? int.MinValue) || number > (field.Maximum ?? int.MaxValue))
                        Errors.Add($"{path}: must be {Range(field)}, not {number}");
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
                    if (value.GetArrayLength() < (field.MinItems ?? 0))
                        Errors.Add($"{path}: needs at least {field.MinItems} {(field.MinItems == 1 ? "entry" : "entries")}");
                    foreach (var (element, i) in value.EnumerateArray().Select((e, i) => (e, i)))
                        if (field.Shape == ChangesetDialect.Op) Op(element, $"{path}[{i}]");
                        else Shape(element, ChangesetDialect.Shape(field.Shape!), $"{path}[{i}]");
                    break;
            }
        }

        // The limits the dialect declares for a string: its allowed values, its pattern, and GEDCOM date syntax.
        static string? StringLimit(string text, DialectField field, string path)
        {
            if (field.Values.Count > 0 && !field.Values.Contains(text))
                return $"{path}: must be {string.Join(" or ", field.Values.Select(v => $"\"{v}\""))}, not \"{text}\"";
            if (field.Pattern is { } pattern && !Regex.IsMatch(text, pattern))
                return pattern == ChangesetDialect.XrefPattern
                    ? $"{path}: must be an xref such as \"@I1@\", not \"{text}\""
                    : $"{path}: \"{text}\" does not match {pattern}";
            if (field.GedcomDate) return DateFault(text, path);
            return null;
        }

        // The applier writes the date as given, so it must already be in the upper case GEDCOM 7 requires.
        static string? DateFault(string date, string path)
        {
            if (GedDateSyntax.Check($"{path}:", date) is { } fault) return fault;
            return date == date.ToUpperInvariant() ? null : $"{path}: write the date in upper case: \"{date.ToUpperInvariant()}\"";
        }

        static string Range(DialectField field) => (field.Minimum, field.Maximum) switch
        {
            ({ } min, { } max) => $"{min} to {max}",
            ({ } min, null) => $"at least {min}",
            (null, { } max) => $"at most {max}",
            _ => "an integer",
        };

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

        static bool IsRelationshipOp(DialectShape shape) => shape.Name.StartsWith("createOrUpdate", StringComparison.Ordinal);

        static bool Has(JsonElement node, string name) =>
            node.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null;

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
            if (shape.Field("match") is { Shape: "factMatch" } && node.TryGetProperty("match", out var match) && match.ValueKind == JsonValueKind.Object)
                PayloadRule(Text(node, "fact"), Payload(match), Join(path, "match"));
        }

        void PayloadRule(string? tag, PayloadFields given, string path)
        {
            switch (FactPayloads.Of(tag))
            {
                case FactPayload.Event when given.Value is not null:
                    Errors.Add($"{Join(path, "value")}: {tag} is an event; give its date and place, not value");
                    break;
                case FactPayload.Text when given.HasDate || given.HasPlace:
                    Errors.Add($"{Join(path, given.HasDate ? "date" : "place")}: {tag} takes a text value, not a date or place");
                    break;
                case FactPayload.Described when !given.HasDate && FactPayloads.LooksLikeMisplacedDate(tag!, given.Value):
                    Errors.Add($"{Join(path, "value")}: \"{given.Value}\" is a date; give it as date");
                    break;
            }
        }

        static PayloadFields Payload(JsonElement node) => new(Text(node, "value"), Has(node, "date"), Has(node, "place"));

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
                || Text(op, field) is not { } xref || !Placeholder.IsPlaceholder(xref)) return;
            named.Add(xref);
            if (!declared.ContainsKey(xref))
                Errors.Add($"{Join(opPath, field)}: {xref} is not declared in {itemPath}.people; declare each new person the item names");
        }

        static string? Text(JsonElement node, string name) =>
            node.ValueKind == JsonValueKind.Object && node.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() : null;

        static string Describe(DialectShape shape) => ChangesetDialect.FindOp(shape.Name) is not null ? shape.Name : $"a {shape.Name}";

        static string Join(string path, string name) => path.Length == 0 ? name : $"{path}.{name}";

        static string Label(string path) => path.Length == 0 ? "changeset" : path;
    }
}
