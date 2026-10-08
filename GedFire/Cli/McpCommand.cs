using GedCore;
using GedCore.Matching;
using GedFire.Gen;
using GedFire.Mcp;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace GedFire.Cli;

// A resident stdio server: it stays running until stdin closes.
public sealed class McpCommand : OptionCommand
{
    public override string Name => "mcp";

    protected override string Usage => "Usage: gedfire mcp --input <ged> [--read-only] [--enforce-privacy]";

    protected override IReadOnlyCollection<string> ValueOptions => ["--input"];

    protected override IReadOnlyCollection<string> RequiredOptions => ["--input"];

    protected override IReadOnlyCollection<string> Switches => ["--read-only", "--enforce-privacy"];

    protected override async Task<int> ExecuteAsync(CommandLine commandLine, CommandContext context)
    {
        string input = commandLine.Require("--input");
        bool readOnly = commandLine.Has("--read-only");
        bool enforcePrivacy = commandLine.Has("--enforce-privacy");
        if (context.FileMissing(input)) return 1;

        string absoluteInput = Path.GetFullPath(input);
        NicknameDirectory nicknames;
        DocumentSnapshot initialSnapshot;
        try
        {
            nicknames = NicknameDirectory.LoadEmbedded();
            initialSnapshot = LoadSnapshot(absoluteInput, enforcePrivacy);
        }
        catch (Exception ex)
        {
            return context.Fail($"Failed to start MCP server: {ex.Message}");
        }

        var session = new DocumentSession(absoluteInput, initialSnapshot, enforcePrivacy);
        await using var watcher = new DocumentFileWatcher(session, absoluteInput);
        var tools = McpToolCollectionFactory.Create(
            session, new ToolGate(), nicknames, absoluteInput, MediaDirectory.For(absoluteInput), readOnly);

        await RunServerAsync(tools, McpServerInstructions.Build(readOnly, enforcePrivacy)).ConfigureAwait(false);
        return 0;
    }

    static DocumentSnapshot LoadSnapshot(string absoluteInput, bool enforcePrivacy)
    {
        var document = GedReader.ReadFile(absoluteInput);
        var model = ModelBuilder.Build(document);
        if (enforcePrivacy)
            PrivacyFilter.Apply(model, DateTime.UtcNow.Year);
        return new DocumentSnapshot(
            model, document.Version, File.GetLastWriteTimeUtc(absoluteInput), new FileInfo(absoluteInput).Length);
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
