using GedFire.Mcp;
using ModelContextProtocol.Protocol;

namespace GedFire.Cli;

public sealed class GetRecordCommand : ToolMirrorCommand<string>
{
    public override string Name => "get-record";

    protected override string Usage => "Usage: gedfire get-record --input <ged> --xref <@I1@>";

    protected override IReadOnlyCollection<string> ValueOptions => ["--input", "--xref"];

    protected override IReadOnlyCollection<string> AdditionalRequiredOptions => ["--xref"];

    protected override string? TryBuildRequest(CommandLine commandLine, CommandContext context) =>
        commandLine.Require("--xref");

    protected override Task<CallToolResult> CallToolAsync(string xref, DocumentSession session, string mediaDirectory) =>
        new GetRecordTool(session, new ToolGate(), mediaDirectory).HandleAsync(xref, CancellationToken.None);
}
