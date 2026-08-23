using System.Reflection;

namespace GedFire.Mcp;

// The gedfire assembly's own informational version, shared by the CLI's
// --version output, gedfire mcp's ServerInfo, and get_document_stats's
// gedFireVersion field, so all three report the same string.
public static class ServerVersion
{
    public static string Current { get; } =
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "unknown";
}
