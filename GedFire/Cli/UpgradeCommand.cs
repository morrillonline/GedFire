using GedCore.Ged55;
using GedCore.Ged70;

namespace GedFire.Cli;

public sealed class UpgradeCommand : SynchronousOptionCommand
{
    public override string Name => "upgrade";

    protected override string Usage => "Usage: gedfire upgrade --input <ged55> --output <ged70>";

    protected override IReadOnlyCollection<string> ValueOptions => ["--input", "--output"];

    protected override IReadOnlyCollection<string> RequiredOptions => ["--input", "--output"];

    protected override int Execute(CommandLine commandLine, CommandContext context)
    {
        string input = commandLine.Require("--input");
        string output = commandLine.Require("--output");
        if (context.FileMissing(input)) return 1;

        var document = context.ReadDocument(input, Ged55Parser.ReadFile);

        context.Out.WriteLine("Upgrading to GEDCOM 7.0");
        ReportSummary(Ged70Upgrader.UpgradeInPlace(document), context.Out);

        Ged70Formatter.WriteFile(document, output);
        context.Out.WriteLine($"Wrote    {output}");
        return 0;
    }

    static void ReportSummary(Ged70Upgrader.UpgradeSummary summary, TextWriter output)
    {
        output.WriteLine($"  {summary.ConcLinesFolded:N0} CONC continuation lines folded");
        output.WriteLine($"  {summary.HeaderRecordsRemoved:N0} obsolete header records removed (CHAR, FILE, DEST, GEDC.FORM, bare SUBM pointer)");
        output.WriteLine($"  {summary.InlineNotesConverted:N0} \"Inline: TRUE\" narrative citations converted to NOTE structures");
        output.WriteLine($"  {summary.NoteRecordsConverted:N0} NOTE records converted to SNOTE");
        output.WriteLine($"  {summary.FreeTextCitationsConverted:N0} free-text source citations converted to pointer citations");
        output.WriteLine($"  {summary.AliasesConverted:N0} text-payload ALIA converted to NAME.NICK");
        output.WriteLine($"  {summary.EmptyContactLinesRemoved:N0} empty contact lines removed");
        output.WriteLine($"  {summary.SubmitterRecordsRemoved:N0} bare submitter records removed");
        output.WriteLine($"  {summary.SchemaTagsDeclared:N0} SCHMA TAG declarations added");
    }
}
