using GedCore;

namespace GedFire.Cli;

public sealed class DateCalcCommand : SynchronousOptionCommand
{
    // The options each operation takes. Anything else supplied is refused.
    static readonly Dictionary<string, string[]> OperationOptions = new()
    {
        ["normalize"] = ["--date"],
        ["add"] = ["--date", "--age"],
        ["sub"] = ["--date", "--age"],
        ["diff"] = ["--from", "--to"],
    };

    static readonly string[] OperandOptions = ["--date", "--from", "--to", "--age"];

    public override string Name => "date-calc";

    protected override string Usage =>
        "Usage: gedfire date-calc --op normalize --date <d>\n" +
        "       gedfire date-calc --op add|sub   --date <d> --age <y/m/d>\n" +
        "       gedfire date-calc --op diff      --from <d> --to <d>";

    protected override IReadOnlyCollection<string> ValueOptions => ["--op", .. OperandOptions];

    protected override IReadOnlyCollection<string> RequiredOptions => ["--op"];

    protected override int Execute(CommandLine commandLine, CommandContext context)
    {
        string op = commandLine.Require("--op");
        if (!OperationOptions.TryGetValue(op, out var accepted))
            return UsageFailure(context, $"Unrecognized --op: {op} (expected normalize, add, sub, or diff)");

        string list = string.Join(" and ", accepted);
        if (OperandOptions.Any(o => !accepted.Contains(o) && commandLine.Value(o) is not null))
            return UsageFailure(context, $"--op {op} accepts only {list}");
        if (accepted.Any(o => commandLine.Value(o) is null))
            return UsageFailure(context, $"--op {op} requires {list}");

        try
        {
            context.Out.WriteLine(Calculate(op, commandLine));
            return 0;
        }
        catch (Exception ex) when (ex is FormatException or ArgumentException)
        {
            // Bad input is a usage error, not a crash.
            return context.Fail(ex.Message);
        }
    }

    int UsageFailure(CommandContext context, string message)
    {
        context.Error.WriteLine(message);
        return context.Fail(Usage);
    }

    static string Calculate(string op, CommandLine commandLine)
    {
        switch (op)
        {
            case "normalize":
                return GedDate.NormalizeDualDate(commandLine.Require("--date"));
            case "diff":
                return GedDate.Diff(
                    GedDate.ParseExactGregorianDate(commandLine.Require("--from")),
                    GedDate.ParseExactGregorianDate(commandLine.Require("--to"))).ToString();
            default:
                var date = GedDate.ParseExactGregorianDate(commandLine.Require("--date"));
                var age = GedAge.Parse(commandLine.Require("--age"));
                return GedDate.FormatExactGregorianDate(
                    op == "add" ? GedDate.AddAge(date, age) : GedDate.SubtractAge(date, age));
        }
    }
}
