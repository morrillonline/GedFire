using GedFire.Mcp;
using ModelContextProtocol.Protocol;

namespace GedFire.Cli;

public sealed class GetDocumentStatsCommand : ToolMirrorCommand<object>
{
    static readonly object NoArguments = new();

    public override string Name => "get-document-stats";

    protected override string Usage => "Usage: gedfire get-document-stats --input <ged>";

    protected override IReadOnlyCollection<string> ValueOptions => ["--input"];

    protected override object? TryBuildRequest(CommandLine commandLine, CommandContext context) => NoArguments;

    protected override Task<CallToolResult> CallToolAsync(object request, DocumentSession session, string mediaDirectory) =>
        new GetDocumentStatsTool(session, new ToolGate()).HandleAsync(CancellationToken.None);
}
