using GedCore.Matching;
using GedCore.Policy;
using GedFire.Mcp;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace GedFire.Cli;

// A resident stdio server: it stays running until stdin closes.
public sealed class McpCommand : OptionCommand
{
    public override string Name => "mcp";

    protected override string Usage => "Usage: gedfire mcp --input <ged> [--policy <json>] [--read-only] [--enforce-privacy]";

    protected override IReadOnlyCollection<string> ValueOptions => ["--input", "--policy"];

    protected override IReadOnlyCollection<string> RequiredOptions => ["--input"];

    protected override IReadOnlyCollection<string> Switches => ["--read-only", "--enforce-privacy"];

    protected override async Task<int> ExecuteAsync(CommandLine commandLine, CommandContext context)
    {
        string input = commandLine.Require("--input");
        bool readOnly = commandLine.Has("--read-only");
        bool enforcePrivacy = commandLine.Has("--enforce-privacy");
        if (context.FileMissing(input)) return 1;
        string? policyPath = commandLine.Value("--policy");
        if (policyPath is not null && context.FileMissing(policyPath)) return 1;

        string absoluteInput = Path.GetFullPath(input);
        NicknameDirectory nicknames;
        DocumentSession session;
        try
        {
            nicknames = NicknameDirectory.LoadEmbedded();
            session = DocumentSession.Open(absoluteInput, enforcePrivacy);
        }
        catch (Exception ex)
        {
            return context.Fail($"Failed to start MCP server: {ex.Message}");
        }

        ChangesetPolicy? policy = null;
        if (policyPath is not null)
        {
            try
            {
                using var policyStream = File.OpenRead(policyPath);
                policy = ChangesetPolicy.Load(policyStream);
            }
            catch (Exception ex) when (ex is FormatException or IOException or UnauthorizedAccessException)
            {
                return context.Fail($"Failed to load policy {policyPath}: {ex.Message}");
            }
        }

        await using var watcher = new DocumentFileWatcher(session, absoluteInput);
        var tools = McpToolCollectionFactory.Create(new ToolContext(session, absoluteInput)
        {
            Nicknames = nicknames,
            ReadOnly = readOnly,
            Policy = policy,
        });

        await RunServerAsync(tools, McpServerInstructions.Build(readOnly, enforcePrivacy, policy is not null)).ConfigureAwait(false);
        return 0;
    }

    static async Task RunServerAsync(McpServerPrimitiveCollection<McpServerTool> tools, string instructions)
    {
        var options = new McpServerOptions
        {
            ServerInfo = new Implementation { Name = "gedfire", Version = ServerVersion.Current },
            Capabilities = new ServerCapabilities { Tools = new ToolsCapability { ListChanged = false } },
            ToolCollection = tools,
            ServerInstructions = instructions,
        };
        options.Filters.Request.CallToolFilters.Add(new ToolArgumentGuard(tools).AsFilter());

        await using var transport = new StdioServerTransport(options, loggerFactory: null);
        var server = McpServer.Create(transport, options, loggerFactory: null, serviceProvider: null);
        await server.RunAsync(CancellationToken.None).ConfigureAwait(false);
    }
}
