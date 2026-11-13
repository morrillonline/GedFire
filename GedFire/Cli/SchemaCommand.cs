using System.Text;
using System.Text.Json;
using GedFire.Schemas;

namespace GedFire.Cli;

// ---------------------------------------------------------------------------
// Prints one published JSON Schema of the finding-file contract, or writes it
// to a new file, so a caller can validate its files with any validator.
// ---------------------------------------------------------------------------

public sealed class SchemaCommand : ICliCommand
{
    static readonly JsonSerializerOptions Indented = new() { WriteIndented = true };

    public static string Usage =>
        $"Usage: gedfire schema <{string.Join("|", SchemaCatalog.Names)}> [--out <json>]";

    public string Name => "schema";

    public Task<int> RunAsync(string[] args, CommandContext context) => Task.FromResult(Run(args, context));

    static int Run(string[] args, CommandContext context)
    {
        if (args is ["--help"] or ["-h"])
        {
            context.Out.WriteLine(Usage);
            return 0;
        }
        if (args.Length == 0) return context.Fail(Usage);
        if (SchemaCatalog.Find(args[0]) is not { } schema)
            return context.Fail($"Unknown schema: {args[0]}\n{Usage}");

        var options = CommandLine.Parse(args[1..], ["--out"]);
        if (options.Error is not null) return context.Fail($"{options.Error}\n{Usage}");

        string json = schema.Document().ToJsonString(Indented);
        string? path = options.Value("--out");
        if (path is null or "-")
        {
            context.Out.WriteLine(json);
            return 0;
        }
        try
        {
            using var file = new FileStream(path, FileMode.CreateNew, FileAccess.Write);
            using var writer = new StreamWriter(file, new UTF8Encoding(false));
            writer.WriteLine(json);
            return 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return context.Fail(ex.Message);
        }
    }
}
