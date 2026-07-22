using GedCore;
using GedCore.Apply;
using GedCore.Ged55;
using GedCore.Ged70;
using GedCore.Gedzip;
using GedCore.Validate;
using GedFire;
using GedFire.Export;
using GedFire.Gen;
using System.Reflection;

// ---------------------------------------------------------------------------
// GedFire CLI
//
// Usage:
//   gedfire create       --output <ged70> --name <gedcom-name> [--xref @I00001@] [--sex M|F|X|U]
//   gedfire upgrade      --input <ged55>  --output <ged70>
//   gedfire downgrade    --input <ged70>  --output <ged55>
//   gedfire generate     --input <ged>    --output-dir <dir>  [--format html] [--media-dir <dir>] [--media-base-url <url>]
//   gedfire export-index --input <ged>    --output <json>
//   gedfire apply        --input <ged>    --changes <json> --items all|1,3 [--dry-run]
//   gedfire validate     <file> [--warnings-as-errors]
//   gedfire pack         --input <ged>    --media-dir <dir> --output <gdz>
//   gedfire unpack       --input <gdz>    --output-dir <dir>
// ---------------------------------------------------------------------------

if (args.Length == 0)
{
    PrintHelp();
    return 1;
}

return args[0].ToLowerInvariant() switch
{
    "create"       => RunCreate(args[1..]),
    "upgrade"      => RunUpgrade(args[1..]),
    "downgrade"    => RunDowngrade(args[1..]),
    "generate"     => RunGenerate(args[1..]),
    "export-index" => RunExportIndex(args[1..]),
    "apply"        => RunApply(args[1..]),
    "validate"     => RunValidate(args[1..]),
    "pack"         => RunPack(args[1..]),
    "unpack"       => RunUnpack(args[1..]),
    "--help" or "-h" or "help" => Help(),
    "--version" or "-v" or "version" => ShowVersion(),
    _ => Unknown(args[0]),
};

// ---------------------------------------------------------------------------

static int RunCreate(string[] args)
{
    var cl = CommandLine.Parse(args, ["--output", "--name", "--xref", "--sex"]);
    string? output = cl.Value("--output");
    string? name = cl.Value("--name");
    string xref = cl.Value("--xref") ?? "@I00001@";
    string? sex = cl.Value("--sex");

    if (cl.Error is not null || output is null || name is null)
    {
        if (cl.Error is not null) Console.Error.WriteLine(cl.Error);
        Console.Error.WriteLine("Usage: gedfire create --output <ged70> --name <gedcom-name> [--xref @I00001@] [--sex M|F|X|U]");
        return 1;
    }

    if (File.Exists(output))
    {
        Console.Error.WriteLine($"Output file already exists: {output}");
        return 1;
    }

    try
    {
        var document = Ged70DocumentFactory.CreateSeeded(name, xref, sex);
        Ged70Formatter.WriteFile(document, output);
        Console.WriteLine($"Created {output} with seed person {xref} ({name})");
        return 0;
    }
    catch (ArgumentException ex)
    {
        Console.Error.WriteLine(ex.Message);
        return 1;
    }
}


static int RunDowngrade(string[] args)
{
    var cl = CommandLine.Parse(args, ["--input", "--output"]);
    string? input = cl.Value("--input");
    string? output = cl.Value("--output");

    if (cl.Error is not null || input is null || output is null)
    {
        if (cl.Error is not null) Console.Error.WriteLine(cl.Error);
        Console.Error.WriteLine("Usage: gedfire downgrade --input <ged70> --output <ged55>");
        return 1;
    }

    if (!File.Exists(input))
    {
        Console.Error.WriteLine($"Input file not found: {input}");
        return 1;
    }

    Console.WriteLine($"Reading  {input}");
    var document = Ged70Parser.ReadFile(input);
    Console.WriteLine($"  {document.Records.Count:N0} level-0 records");

    Ged55Formatter.WriteFile(document, output);
    Console.WriteLine($"Wrote    {output}");
    return 0;
}

// ---------------------------------------------------------------------------
// ---------------------------------------------------------------------------

static int RunUpgrade(string[] args)
{
    var cl = CommandLine.Parse(args, ["--input", "--output"]);
    string? input  = cl.Value("--input");
    string? output = cl.Value("--output");

    if (cl.Error is not null || input is null || output is null)
    {
        if (cl.Error is not null) Console.Error.WriteLine(cl.Error);
        Console.Error.WriteLine("Usage: gedfire upgrade --input <ged55> --output <ged70>");
        return 1;
    }

    if (!File.Exists(input))
    {
        Console.Error.WriteLine($"Input file not found: {input}");
        return 1;
    }

    Console.WriteLine($"Reading  {input}");
    var doc = Ged55Parser.ReadFile(input);
    Console.WriteLine($"  {doc.Records.Count:N0} level-0 records");

    Console.WriteLine("Upgrading to GEDCOM 7.0");
    var summary = Ged70Upgrader.UpgradeInPlace(doc);
    Console.WriteLine($"  {summary.ConcLinesFolded:N0} CONC continuation lines folded");
    Console.WriteLine($"  {summary.HeaderRecordsRemoved:N0} obsolete header records removed (CHAR, FILE, DEST, GEDC.FORM, bare SUBM pointer)");
    Console.WriteLine($"  {summary.InlineNotesConverted:N0} \"Inline: TRUE\" narrative citations converted to NOTE structures");
    Console.WriteLine($"  {summary.NoteRecordsConverted:N0} NOTE records converted to SNOTE");
    Console.WriteLine($"  {summary.FreeTextCitationsConverted:N0} free-text source citations converted to pointer citations");
    Console.WriteLine($"  {summary.AliasesConverted:N0} text-payload ALIA converted to NAME.NICK");
    Console.WriteLine($"  {summary.EmptyContactLinesRemoved:N0} empty contact lines removed");
    Console.WriteLine($"  {summary.SubmitterRecordsRemoved:N0} bare submitter records removed");
    Console.WriteLine($"  {summary.SchemaTagsDeclared:N0} SCHMA TAG declarations added");

    Ged70Formatter.WriteFile(doc, output);
    Console.WriteLine($"Wrote    {output}");
    return 0;
}

static int RunGenerate(string[] args)
{
    var cl = CommandLine.Parse(args, ["--input", "--output-dir", "--format", "--template", "--media-dir", "--media-base-url"]);
    string? input        = cl.Value("--input");
    string? outputDir    = cl.Value("--output-dir");
    string? templateArg  = cl.Value("--template");
    string? mediaDirArg  = cl.Value("--media-dir");
    string  mediaBaseUrl = cl.Value("--media-base-url") ?? "media/";

    if (cl.Error is not null || input is null || outputDir is null)
    {
        if (cl.Error is not null) Console.Error.WriteLine(cl.Error);
        Console.Error.WriteLine("Usage: gedfire generate --input <ged> --output-dir <dir> [--format html] [--template <file>] [--media-dir <dir>] [--media-base-url <url>]");
        return 1;
    }

    if (!File.Exists(input))
    {
        Console.Error.WriteLine($"Input file not found: {input}");
        return 1;
    }

    Console.WriteLine($"Reading  {input}");
    var doc = GedReader.ReadFile(input);
    Console.WriteLine($"  {doc.Records.Count:N0} level-0 records");

    Console.WriteLine("Building model...");
    var model = ModelBuilder.Build(doc);
    Console.WriteLine($"  {model.Individuals.Count:N0} individuals, {model.Families.Count:N0} families, {model.Sources.Count:N0} sources");

    int privatized = PrivacyFilter.Apply(model, DateTime.UtcNow.Year);
    if (privatized > 0)
        Console.WriteLine($"  {privatized:N0} plausibly-living individuals privatized (\"{PrivacyFilter.LivingGivenName}\" placeholder)");

    const string defaultTemplate = TemplateLocator.DefaultTemplateHtml;

    // Load template: explicit --template wins; otherwise probe well-known
    // locations relative to the input GED.
    string template = defaultTemplate;
    string? templatePath = TemplateLocator.Locate(input, templateArg);
    if (templatePath != null && File.Exists(templatePath))
    {
        template = File.ReadAllText(templatePath);
        Console.WriteLine($"Using template: {templatePath}");
    }

    Console.WriteLine($"Generating HTML to {outputDir} ...");
    string mediaDir = mediaDirArg ?? Path.GetDirectoryName(Path.GetFullPath(input)) ?? ".";
    var generator = new SiteGenerator(model, template, new MediaOptions(mediaDir, mediaBaseUrl));
    generator.Generate(outputDir);
    foreach (var warning in generator.Warnings)
        Console.WriteLine($"  Warning: {warning}");
    Console.WriteLine("Done.");
    return 0;
}

static int RunExportIndex(string[] args)
{
    var cl = CommandLine.Parse(args, ["--input", "--output"]);
    string? input  = cl.Value("--input");
    string? output = cl.Value("--output");

    if (cl.Error is not null || input is null || output is null)
    {
        if (cl.Error is not null) Console.Error.WriteLine(cl.Error);
        Console.Error.WriteLine("Usage: gedfire export-index --input <ged> --output <json>");
        return 1;
    }

    if (!File.Exists(input))
    {
        Console.Error.WriteLine($"Input file not found: {input}");
        return 1;
    }

    Console.WriteLine($"Reading  {input}");
    var doc = GedReader.ReadFile(input);
    Console.WriteLine($"  {doc.Records.Count:N0} level-0 records");

    Console.WriteLine("Building model...");
    var model = ModelBuilder.Build(doc);
    Console.WriteLine($"  {model.Individuals.Count:N0} individuals, {model.Families.Count:N0} families");

    PersonIndexExporter.WriteFile(model, input, output);
    Console.WriteLine($"Wrote    {output} ({model.Individuals.Count:N0} persons)");
    return 0;
}

static int RunApply(string[] args)
{
    var cl = CommandLine.Parse(args, ["--input", "--changes", "--items"], ["--dry-run"]);
    string? input   = cl.Value("--input");
    string? changes = cl.Value("--changes");
    string? items   = cl.Value("--items");
    bool dryRun     = cl.Has("--dry-run");

    if (cl.Error is not null || input is null || changes is null || items is null)
    {
        if (cl.Error is not null) Console.Error.WriteLine(cl.Error);
        Console.Error.WriteLine("Usage: gedfire apply --input <ged> --changes <json> --items all|1,3 [--dry-run]");
        return 1;
    }

    foreach (var path in new[] { input, changes })
        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"File not found: {path}");
            return 1;
        }

    var changeset = Changeset.LoadFile(changes);

    int[] itemNumbers;
    if (items == "all")
    {
        itemNumbers = [.. changeset.Items.Select(i => i.Number)];
    }
    else
    {
        try
        {
            itemNumbers = [.. items.Split(',').Select(int.Parse)];
        }
        catch (FormatException)
        {
            Console.Error.WriteLine($"--items must be 'all' or comma-separated numbers, got: {items}");
            return 1;
        }
    }

    var result = ChangesetApplier.Run(input, changeset, itemNumbers, dryRun);

    foreach (var entry in result.Log)
        Console.WriteLine(dryRun ? entry : $"applied: {entry}");

    if (!result.Success)
    {
        Console.Error.WriteLine("APPLY FAILED — file not modified:");
        foreach (var error in result.Errors)
            Console.Error.WriteLine($"  - {error}");
        return 1;
    }

    if (!dryRun)
    {
        string deltas = string.Join(", ", result.Deltas.Select(d => $"{d.Key} +{d.Value}"));
        Console.WriteLine($"verify OK: round-trip byte-stable, pointers resolve, deltas {{{deltas}}}");
    }
    return 0;
}

static int RunValidate(string[] args)
{
    if (args.Length == 0 || args[0].StartsWith("--", StringComparison.Ordinal))
    {
        Console.Error.WriteLine("Usage: gedfire validate <file> [--warnings-as-errors]");
        return 1;
    }
    string input = args[0];

    var cl = CommandLine.Parse(args[1..], [], ["--warnings-as-errors"]);
    if (cl.Error is not null)
    {
        Console.Error.WriteLine(cl.Error);
        Console.Error.WriteLine("Usage: gedfire validate <file> [--warnings-as-errors]");
        return 1;
    }
    bool warningsAsErrors = cl.Has("--warnings-as-errors");

    if (!File.Exists(input))
    {
        Console.Error.WriteLine($"Input file not found: {input}");
        return 1;
    }

    var doc = GedReader.ReadFile(input);
    var diagnostics = ConformanceChecker.Check(doc);

    foreach (var d in diagnostics)
        Console.WriteLine($"{d.Severity} {d.Code} {d.Xref ?? "-"} {d.Tag}: {d.Message}");

    bool hasError = diagnostics.Any(d =>
        d.Severity == GedDiagnosticSeverity.Error ||
        (warningsAsErrors && d.Severity == GedDiagnosticSeverity.Warning));

    Console.WriteLine($"{diagnostics.Count} diagnostic(s): " +
        $"{diagnostics.Count(d => d.Severity == GedDiagnosticSeverity.Error)} error(s), " +
        $"{diagnostics.Count(d => d.Severity == GedDiagnosticSeverity.Warning)} warning(s), " +
        $"{diagnostics.Count(d => d.Severity == GedDiagnosticSeverity.Info)} info");

    return hasError ? 1 : 0;
}

static int RunPack(string[] args)
{
    var cl = CommandLine.Parse(args, ["--input", "--media-dir", "--output"]);
    string? input    = cl.Value("--input");
    string? mediaDir = cl.Value("--media-dir");
    string? output   = cl.Value("--output");

    if (cl.Error is not null || input is null || mediaDir is null || output is null)
    {
        if (cl.Error is not null) Console.Error.WriteLine(cl.Error);
        Console.Error.WriteLine("Usage: gedfire pack --input <ged> --media-dir <dir> --output <gdz>");
        return 1;
    }

    if (!File.Exists(input))
    {
        Console.Error.WriteLine($"Input file not found: {input}");
        return 1;
    }

    Console.WriteLine($"Reading  {input}");
    var doc = GedReader.ReadFile(input);
    Console.WriteLine($"  {doc.Records.Count:N0} level-0 records");

    try
    {
        GedzipWriter.Write(doc, mediaDir, output);
    }
    catch (FileNotFoundException ex)
    {
        Console.Error.WriteLine(ex.Message);
        return 1;
    }

    Console.WriteLine($"Wrote    {output}");
    return 0;
}

static int RunUnpack(string[] args)
{
    var cl = CommandLine.Parse(args, ["--input", "--output-dir"]);
    string? input     = cl.Value("--input");
    string? outputDir = cl.Value("--output-dir");

    if (cl.Error is not null || input is null || outputDir is null)
    {
        if (cl.Error is not null) Console.Error.WriteLine(cl.Error);
        Console.Error.WriteLine("Usage: gedfire unpack --input <gdz> --output-dir <dir>");
        return 1;
    }

    if (!File.Exists(input))
    {
        Console.Error.WriteLine($"Input file not found: {input}");
        return 1;
    }

    try
    {
        using var package = GedzipReader.Open(input);
        Console.WriteLine($"Read     {input}");
        Console.WriteLine($"  {package.Document.Records.Count:N0} level-0 records, {package.MediaPaths.Count:N0} media file(s)");

        Directory.CreateDirectory(outputDir);
        string gedPath = Path.Combine(outputDir, "gedcom.ged");
        Ged70Formatter.WriteFile(package.Document, gedPath);
        package.ExtractMedia(outputDir);
    }
    catch (Exception ex) when (ex is FormatException or IOException)
    {
        Console.Error.WriteLine(ex.Message);
        return 1;
    }

    Console.WriteLine($"Wrote    {outputDir}");
    return 0;
}

static int Help() { PrintHelp(); return 0; }
static int ShowVersion()
{
    string version = Assembly.GetExecutingAssembly()
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
        .InformationalVersion ?? "unknown";
    Console.WriteLine($"GedFire {version}");
    return 0;
}
static int Unknown(string cmd) { Console.Error.WriteLine($"Unknown command: {cmd}"); return 1; }

static void PrintHelp()
{
    Console.WriteLine("""
        GedFire -- GEDCOM processor and site generator

        Commands:
                    create    --output <ged70> --name <gedcom-name> [--xref @I00001@] [--sex M|F|X|U]
                                                Create a new GEDCOM 7 document seeded with one named person.

          upgrade   --input <ged55> --output <ged70>
                        Upgrade a GEDCOM 5.5 file to GEDCOM 7.0.

          downgrade --input <ged70> --output <ged55>
                        Write a GEDCOM 7.0 file in GEDCOM 5.5 format.

          generate  --input <ged> --output-dir <dir> [--format html]
                        Generate the family pages and index from a GEDCOM file.

          export-index  --input <ged> --output <json>
                        Export the research person index (one JSON entry per
                        individual: xref, normalized name, birth/death, parents,
                        marriages with spouse and children xrefs).

          apply     --input <ged> --changes <json> --items all|1,3 [--dry-run]
                        Apply approved research-proposal changeset items to the
                        GEDCOM. Validates every op first; writes the file only
                        after in-memory verification (byte-stable round-trip,
                        pointer resolution, record-count deltas) passes.

          validate  <file> [--warnings-as-errors]
                        Run GEDCOM 7 conformance checks (GED001-GED014) and
                        print one diagnostic per line, sorted by severity then
                        code. Exits 1 if any Error is present (or any Warning
                        too, with --warnings-as-errors); 0 otherwise.

          pack      --input <ged> --media-dir <dir> --output <gdz>
                        Bundle a GEDCOM file and its referenced media into a
                        GEDZIP (.gdz) archive.

          unpack    --input <gdz> --output-dir <dir>
                        Extract a GEDZIP archive's gedcom.ged and media files
                        into a directory.

        Options:
                    --help, -h       Show this help message.
                    --version, -v    Show the GedFire version.
        """);
}
