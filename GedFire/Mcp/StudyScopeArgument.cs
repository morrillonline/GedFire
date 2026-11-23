using System.Text.Json;
using GedFire.TargetSelection;

namespace GedFire.Mcp;

/// <summary>Reads studySurnames, the argument by which select_targets and validate_changeset take a study's scope.</summary>
static class StudyScopeArgument
{
    public const string Name = "studySurnames";

    /// <summary>True with <paramref name="scope"/> null when the argument is absent; false with an error naming the field.</summary>
    public static bool TryRead(JsonElement element, out StudyScope? scope, out string? error)
    {
        scope = null;
        if (!ToolArguments.TryReadOptionalStringList(element, Name, out var surnames, out error)) return false;
        if (element.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null) return true;
        if (surnames.Count == 0)
        {
            error = $"{Name} must list at least one surname such as [\"Ashworth\"]; omit it to deal with no study scope.";
            return false;
        }

        scope = new StudyScope(surnames);
        return true;
    }
}
