using GedCore.Matching;
using GedFire.Mcp;
using ModelContextProtocol.Protocol;

namespace GedFire.Cli;

public sealed class FindPersonCommand : ToolMirrorCommand<FindPersonRequest>
{
    public override string Name => "find-person";

    protected override string Usage =>
        "Usage: gedfire find-person --input <ged> --query <name> [--max-results N] [--sex M|F]\n" +
        "       [--birth-year Y] [--birth-place P] [--death-year Y] [--death-place P]\n" +
        "       [--father NAME] [--mother NAME]\n" +
        "       [--spouse-name NAME] [--marriage-year Y] [--marriage-place P]";

    protected override IReadOnlyCollection<string> ValueOptions => FindPersonRequestReader.Options;

    protected override IReadOnlyCollection<string> AdditionalRequiredOptions => ["--query"];

    protected override FindPersonRequest? TryBuildRequest(CommandLine commandLine, CommandContext context)
    {
        if (FindPersonRequestReader.TryRead(commandLine, out var request, out string? error)) return request;

        context.Fail(error!);
        return null;
    }

    protected override Task<CallToolResult> CallToolAsync(
        FindPersonRequest request, DocumentSession session, string mediaDirectory)
    {
        var tool = new FindPersonTool(session, new ToolGate(), NicknameDirectory.LoadEmbedded());
        return tool.HandleAsync(request.Query, request.Hints, CancellationToken.None, request.MaxResults);
    }
}
