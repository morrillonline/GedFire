using System.Text;
using System.Text.Json;
using GedCore.Policy;
using GedFire.Mcp;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace GedFire.Cli;

public sealed class ToolsCommand : ICliCommand
{
    public const string Usage = """
        Usage: gedfire tools list
               gedfire tools describe <tool>
               gedfire tools call <tool> [--input <ged>] [--args <json|->] [--out <json|->]
                   [--policy <json>] [--read-only] [--enforce-privacy]
        """;

    public string Name => "tools";

    public async Task<int> RunAsync(string[] args, CommandContext context)
    {
        if (args is ["--help"] or ["-h"])
        {
            context.Out.WriteLine(Usage);
            return 0;
        }
        if (args is ["list"])
        {
            context.Out.WriteLine(JsonSerializer.Serialize(new { tools = McpToolCollectionFactory.Describe() }, McpJsonUtilities.DefaultOptions));
            return 0;
        }
        if (args.Length < 2 || args[0] is not ("describe" or "call"))
            return context.Fail(Usage);

        string name = args[1];
        var description = McpToolCollectionFactory.Describe(name);
        if (description is null) return context.Fail($"Unknown tool: {name}");
        if (args[0] == "describe")
        {
            if (args.Length != 2) return context.Fail(Usage);
            context.Out.WriteLine(JsonSerializer.Serialize(description, McpJsonUtilities.DefaultOptions));
            return 0;
        }

        return await CallAsync(name, args[2..], context).ConfigureAwait(false);
    }

    static async Task<int> CallAsync(string name, string[] args, CommandContext context)
    {
        var options = CommandLine.Parse(args, ["--input", "--args", "--out", "--policy"], ["--read-only", "--enforce-privacy"]);
        if (options.Error is not null) return context.Fail($"{options.Error}\n{Usage}");
        if (McpToolCollectionFactory.RequiresDocument(name) && options.Value("--input") is null)
            return context.Fail($"{name} requires --input <ged>.\n{Usage}");

        try
        {
            var arguments = await ReadArgumentsAsync(options.Value("--args"), context.In).ConfigureAwait(false);
            var tool = CreateTool(name, options);
            using var outputFile = OpenOutput(options.Value("--out"));
            var result = await new ToolDispatcher([tool]).InvokeAsync(name, arguments).ConfigureAwait(false);
            return ToolCallResultWriter.Write(result, outputFile ?? context.Out, context.Error);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException
                                      or FormatException or ArgumentException or InvalidOperationException)
        {
            return context.Fail(ex.Message);
        }
    }

    static async Task<Dictionary<string, JsonElement>> ReadArgumentsAsync(string? path, TextReader input)
    {
        if (path is null) return [];
        if (path == "-") return await ToolCallArgumentsReader.ReadAsync(input).ConfigureAwait(false);
        using var stream = File.OpenRead(path);
        return await ToolCallArgumentsReader.ReadAsync(stream).ConfigureAwait(false);
    }

    static StreamWriter? OpenOutput(string? path) => path is null or "-" ? null
        : new StreamWriter(new FileStream(path, FileMode.CreateNew, FileAccess.Write), new UTF8Encoding(false));

    static McpServerTool CreateTool(string name, CommandLine options)
    {
        ChangesetPolicy? policy = null;
        if (options.Value("--policy") is { } policyPath)
        {
            using var stream = File.OpenRead(policyPath);
            policy = ChangesetPolicy.Load(stream);
        }
        if (options.Value("--input") is not { } path)
            return McpToolCollectionFactory.CreateStandalone(name);

        string absolutePath = Path.GetFullPath(path);
        var session = DocumentSession.Open(absolutePath, options.Has("--enforce-privacy"));
        return McpToolCollectionFactory.Create(name, new ToolContext(session, absolutePath)
        {
            ReadOnly = options.Has("--read-only"),
            Policy = policy,
        });
    }

}