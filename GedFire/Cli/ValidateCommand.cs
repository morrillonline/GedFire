using GedCore;
using GedCore.Validate;

namespace GedFire.Cli;

// The file is positional, so this command parses its own arguments.
public sealed class ValidateCommand : ICliCommand
{
    const string Usage = "Usage: gedfire validate <file> [--warnings-as-errors]";

    public string Name => "validate";

    public Task<int> RunAsync(string[] args, CommandContext context) => Task.FromResult(Run(args, context));

    static int Run(string[] args, CommandContext context)
    {
        if (args.Length == 0 || args[0].StartsWith("--", StringComparison.Ordinal))
            return context.Fail(Usage);
        string input = args[0];

        var commandLine = CommandLine.Parse(args[1..], [], ["--warnings-as-errors"]);
        if (commandLine.Error is not null)
        {
            context.Error.WriteLine(commandLine.Error);
            return context.Fail(Usage);
        }

        if (context.FileMissing(input)) return 1;

        var diagnostics = ConformanceChecker.Check(GedReader.ReadFile(input));
        foreach (var d in diagnostics)
            context.Out.WriteLine($"{d.Severity} {d.Code} {d.Xref ?? "-"} {d.Tag}: {d.Message}");

        context.Out.WriteLine(Summarize(diagnostics));
        return HasBlockingDiagnostic(diagnostics, commandLine.Has("--warnings-as-errors")) ? 1 : 0;
    }

    static bool HasBlockingDiagnostic(IReadOnlyList<GedDiagnostic> diagnostics, bool warningsAsErrors) =>
        diagnostics.Any(d =>
            d.Severity == GedDiagnosticSeverity.Error ||
            (warningsAsErrors && d.Severity == GedDiagnosticSeverity.Warning));

    static string Summarize(IReadOnlyList<GedDiagnostic> diagnostics) =>
        $"{diagnostics.Count} diagnostic(s): " +
        $"{CountOf(diagnostics, GedDiagnosticSeverity.Error)} error(s), " +
        $"{CountOf(diagnostics, GedDiagnosticSeverity.Warning)} warning(s), " +
        $"{CountOf(diagnostics, GedDiagnosticSeverity.Info)} info";

    static int CountOf(IReadOnlyList<GedDiagnostic> diagnostics, GedDiagnosticSeverity severity) =>
        diagnostics.Count(d => d.Severity == severity);
}
