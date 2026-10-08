using GedCore;
using GedFire.Gen;
using GedFire.Mcp;
using ModelContextProtocol.Protocol;

namespace GedFire.Cli;

// ---------------------------------------------------------------------------
// A one-shot CLI mirror of a read-only MCP tool: loads the GEDCOM into a
// single-use session, calls the same tool class the server runs, and prints
// its JSON result (or error text) so the CLI and MCP surfaces share one
// implementation.
// ---------------------------------------------------------------------------

public abstract class ToolMirrorCommand<TRequest> : OptionCommand
    where TRequest : class
{
    protected sealed override IReadOnlyCollection<string> RequiredOptions => ["--input", .. AdditionalRequiredOptions];

    protected virtual IReadOnlyCollection<string> AdditionalRequiredOptions => [];

    protected sealed override async Task<int> ExecuteAsync(CommandLine commandLine, CommandContext context)
    {
        string input = commandLine.Require("--input");
        if (context.FileMissing(input)) return 1;

        var request = TryBuildRequest(commandLine, context);
        if (request is null) return 1;

        var session = LoadSession(input, context);
        if (session is null) return 1;

        var result = await CallToolAsync(request, session, MediaDirectory.For(input)).ConfigureAwait(false);
        return Write(result, context);
    }

    /// <summary>Reads the tool's arguments from the command line; null after reporting why they are invalid.</summary>
    protected abstract TRequest? TryBuildRequest(CommandLine commandLine, CommandContext context);

    protected abstract Task<CallToolResult> CallToolAsync(TRequest request, DocumentSession session, string mediaDirectory);

    static DocumentSession? LoadSession(string input, CommandContext context)
    {
        string absoluteInput = Path.GetFullPath(input);
        try
        {
            var document = GedReader.ReadFile(absoluteInput);
            var model = ModelBuilder.Build(document);
            var snapshot = new DocumentSnapshot(
                model, document.Version, File.GetLastWriteTimeUtc(absoluteInput), new FileInfo(absoluteInput).Length);
            return new DocumentSession(absoluteInput, snapshot);
        }
        catch (Exception ex)
        {
            context.Fail($"Failed to read {input}: {ex.Message}");
            return null;
        }
    }

    // The tool's own compact JSON goes to stdout with exit 0; error text goes to stderr with exit 1.
    static int Write(CallToolResult result, CommandContext context)
    {
        string text = ((TextContentBlock)result.Content[0]).Text;
        if (result.IsError is true) return context.Fail(text);

        context.Out.WriteLine(text);
        return 0;
    }
}
