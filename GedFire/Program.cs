using GedCore;
using GedCore.Apply;
using GedCore.Ged55;
using GedCore.Ged70;
using GedCore.Gedzip;
using GedCore.Matching;
using GedCore.Validate;
using GedFire;
using GedFire.Export;
using GedFire.Gen;
using GedFire.Match;
using GedFire.Mcp;
using GedFire.TargetSelection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
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
//   gedfire select-targets --input <ged>  --output <wanted.json> --count <N> --surnames <list>
//   gedfire apply        --input <ged>    --changes <json> --items all|1,3 [--dry-run]
//   gedfire validate     <file> [--warnings-as-errors]
//   gedfire pack         --input <ged>    --media-dir <dir> --output <gdz>
//   gedfire unpack       --input <gdz>    --output-dir <dir>
//   gedfire mcp          --input <ged>    [--media-dir <dir>]
//   gedfire date-calc    --op normalize|add|sub|diff [--date <d>] [--from <d>] [--to <d>] [--age <y/m/d>]
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
    "select-targets" => RunSelectTargets(args[1..]),
    "apply"        => RunApply(args[1..]),
    "validate"     => RunValidate(args[1..]),
    "pack"         => RunPack(args[1..]),
    "unpack"       => RunUnpack(args[1..]),
    "mcp"          => await RunMcp(args[1..]),
    "date-calc"    => RunDateCalc(args[1..]),
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

static int RunSelectTargets(string[] args)
{
    var cl = CommandLine.Parse(args, ["--input", "--output", "--count", "--surnames"]);
    string? input        = cl.Value("--input");
    string? output       = cl.Value("--output");
    string? countArg     = cl.Value("--count");
    string? surnamesArg  = cl.Value("--surnames");

    if (cl.Error is not null || input is null || output is null || countArg is null || surnamesArg is null)
    {
        if (cl.Error is not null) Console.Error.WriteLine(cl.Error);
        Console.Error.WriteLine("Usage: gedfire select-targets --input <ged> --output <wanted.json> --count <N> --surnames <list>");
        return 1;
    }

    if (!int.TryParse(countArg, out int count) || count <= 0)
    {
        Console.Error.WriteLine($"--count must be a positive integer, got: {countArg}");
        return 1;
    }

    var surnames = surnamesArg
        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .ToList();
    if (surnames.Count == 0)
    {
        Console.Error.WriteLine("--surnames must list at least one surname");
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

    var model = ModelBuilder.Build(doc);
    var candidates = GapDetector.Detect(model, surnames);
    Console.WriteLine($"  {candidates.Count:N0} candidate gap(s) detected for {string.Join(", ", surnames)}");
    foreach (var group in candidates.GroupBy(c => c.CardType).OrderByDescending(g => g.Count()))
        Console.WriteLine($"    {group.Count(),6:N0}  {group.Key}");

    long seed = DateTime.UtcNow.Ticks;
    var draw = TargetDrawer.Draw(candidates, count, seed);
    if (draw.LegendaryDiscards.Count > 0)
        Console.WriteLine($"  {draw.LegendaryDiscards.Count:N0} extra Legendary-band candidate(s) discarded (one-per-pack cap)");

    WantedFileWriter.WriteFile(input, surnames, candidates.Count, draw, output);
    Console.WriteLine($"Wrote    {output} ({draw.Targets.Count:N0} target(s), seed {seed})");
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

    ApplyResult result;
    try
    {
        result = ChangesetApplier.Run(input, changeset, itemNumbers, dryRun);
    }
    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
    {
        Console.Error.WriteLine($"APPLY FAILED — could not open {input}: {ex.Message}");
        return 1;
    }

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

static async Task<int> RunMcp(string[] args)
{
    var cl = CommandLine.Parse(args, ["--input", "--media-dir"]);
    string? input = cl.Value("--input");
    string? mediaDirArg = cl.Value("--media-dir");

    if (cl.Error is not null || input is null)
    {
        if (cl.Error is not null) Console.Error.WriteLine(cl.Error);
        Console.Error.WriteLine("Usage: gedfire mcp --input <ged> [--media-dir <dir>]");
        return 1;
    }

    if (!File.Exists(input))
    {
        Console.Error.WriteLine($"Input file not found: {input}");
        return 1;
    }

    string absoluteInput = Path.GetFullPath(input);
    string absoluteMediaDir = Path.GetFullPath(mediaDirArg ?? DefaultMcpMediaDir(absoluteInput));

    // Startup: load the nickname directory and build the first snapshot.
    // Any failure here is the startup-error path -- stderr, exit 1, no
    // protocol output written.
    NicknameDirectory nicknames;
    DocumentSnapshot initialSnapshot;
    try
    {
        nicknames = NicknameDirectory.LoadEmbedded();

        var doc = GedReader.ReadFile(absoluteInput);
        var model = ModelBuilder.Build(doc);
        var info = new FileInfo(absoluteInput);
        initialSnapshot = new DocumentSnapshot(model, doc.Version, File.GetLastWriteTimeUtc(absoluteInput), info.Length);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Failed to start MCP server: {ex.Message}");
        return 1;
    }

    var session = new DocumentSession(absoluteInput, initialSnapshot);
    var gate = new ToolGate();
    var findPerson = new FindPersonTool(session, gate, nicknames);
    var getDocumentStats = new GetDocumentStatsTool(session, gate);
    var getRecord = new GetRecordTool(session, gate, absoluteMediaDir);

    // Listed alphabetically by name for readability; the SDK's own
    // McpServerPrimitiveCollection does not preserve this as the advertised
    // tools/list order.
    var toolCollection = new McpServerPrimitiveCollection<McpServerTool>
    {
        findPerson.ToMcpServerTool(),
        getDocumentStats.ToMcpServerTool(),
        getRecord.ToMcpServerTool(),
    };

    var serverOptions = new McpServerOptions
    {
        ServerInfo = new Implementation { Name = "gedfire", Version = GedFireVersion() },
        Capabilities = new ServerCapabilities { Tools = new ToolsCapability { ListChanged = false } },
        ToolCollection = toolCollection,
        // States once that returned xrefs belong only to this server's bound
        // document and that no initial-release tool mutates the file.
        // Tool-specific calling guidance remains on the tool itself.
        ServerInstructions =
            "Every xref returned by a tool on this server (an individual or family reference such as \"@I123@\" " +
            "or \"@F45@\") belongs only to the single GEDCOM document this server was started against, and is " +
            "meaningless to any other document or provider. No tool in this release mutates that file; every tool " +
            "is read-only.",
    };

    await using var transport = new StdioServerTransport(serverOptions, loggerFactory: null);
    var server = McpServer.Create(transport, serverOptions, loggerFactory: null, serviceProvider: null);
    await server.RunAsync(CancellationToken.None).ConfigureAwait(false);
    return 0;
}

static string GedFireVersion() =>
    Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "unknown";

// gedfire mcp's --media-dir default: the GEDCOM's own directory -- the same
// default `generate` has always used. Every FILE payload CreateOrUpdateMediaOp
// writes is now self-describing (MediaFileRequest.NormalizePath prepends
// "media/" per GEDCOM 7 §2.12's recommendation), so resolution needs no
// special-casing here: a payload of "media/photo.jpg" already names its
// own subfolder relative to this directory. A record written before that
// normalization existed, with a bare filename and no "media/" segment,
// will not resolve under this default -- that is a data gap in the
// existing file to fix at the source (resubmit its media op), not
// something this tool should paper over by guessing a different base.
static string DefaultMcpMediaDir(string absoluteInput) =>
    Path.GetDirectoryName(absoluteInput) ?? ".";

static int RunDateCalc(string[] args)
{
    var cl = CommandLine.Parse(args, ["--op", "--date", "--from", "--to", "--age"]);
    string? op   = cl.Value("--op");
    string? date = cl.Value("--date");
    string? from = cl.Value("--from");
    string? to   = cl.Value("--to");
    string? age  = cl.Value("--age");

    const string usage =
        "Usage: gedfire date-calc --op normalize --date <d>\n" +
        "       gedfire date-calc --op add|sub   --date <d> --age <y/m/d>\n" +
        "       gedfire date-calc --op diff      --from <d> --to <d>";

    if (cl.Error is not null || op is null)
    {
        if (cl.Error is not null) Console.Error.WriteLine(cl.Error);
        Console.Error.WriteLine(usage);
        return 1;
    }

    try
    {
        switch (op)
        {
            case "normalize":
                if (from is not null || to is not null || age is not null)
                {
                    Console.Error.WriteLine("--op normalize accepts only --date");
                    Console.Error.WriteLine(usage);
                    return 1;
                }
                if (date is null)
                {
                    Console.Error.WriteLine("--op normalize requires --date");
                    Console.Error.WriteLine(usage);
                    return 1;
                }
                Console.WriteLine(GedDate.NormalizeDualDate(date));
                return 0;

            case "add":
            case "sub":
                if (from is not null || to is not null)
                {
                    Console.Error.WriteLine($"--op {op} accepts only --date and --age");
                    Console.Error.WriteLine(usage);
                    return 1;
                }
                if (date is null || age is null)
                {
                    Console.Error.WriteLine($"--op {op} requires --date and --age");
                    Console.Error.WriteLine(usage);
                    return 1;
                }
                var baseDate = GedDate.ParseExactGregorianDate(date);
                var ageValue = GedAge.Parse(age);
                var result = op == "add"
                    ? GedDate.AddAge(baseDate, ageValue)
                    : GedDate.SubtractAge(baseDate, ageValue);
                Console.WriteLine(GedDate.FormatExactGregorianDate(result));
                return 0;

            case "diff":
                if (date is not null || age is not null)
                {
                    Console.Error.WriteLine("--op diff accepts only --from and --to");
                    Console.Error.WriteLine(usage);
                    return 1;
                }
                if (from is null || to is null)
                {
                    Console.Error.WriteLine("--op diff requires --from and --to");
                    Console.Error.WriteLine(usage);
                    return 1;
                }
                var fromDate = GedDate.ParseExactGregorianDate(from);
                var toDate = GedDate.ParseExactGregorianDate(to);
                Console.WriteLine(GedDate.Diff(fromDate, toDate).ToString());
                return 0;

            default:
                Console.Error.WriteLine($"Unrecognized --op: {op} (expected normalize, add, sub, or diff)");
                Console.Error.WriteLine(usage);
                return 1;
        }
    }
    catch (Exception ex) when (ex is FormatException or ArgumentException)
    {
        // Bad input (grammar violation, dates out of the exact-precision
        // scope, reversed --from/--to, or arithmetic leaving the supported
        // year 1-9999 range): a usage error, not a crash (AGENTS.md "To add
        // a CLI command").
        Console.Error.WriteLine(ex.Message);
        return 1;
    }
}

static int Help() { PrintHelp(); return 0; }
static int ShowVersion()
{
    Console.WriteLine($"GedFire {GedFireVersion()}");
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

          select-targets --input <ged> --output <wanted.json> --count <N> --surnames <list>
                        Detect every research gap for the given surnames
                        (New parent/spouse/child, Enrich person), score each
                        by nominal points and GED-only difficulty, and draw
                        <N> of them uniformly at random (one Legendary-band
                        cap per pack) into a self-contained wanted.json.

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

          mcp       --input <ged>
                        Start a stdio Model Context Protocol server exposing
                        this GEDCOM to MCP-compatible agent clients. Resident
                        process: stays running until stdin closes. Read-only;
                        no tool in this release mutates the file.

          date-calc --op normalize|add|sub|diff
                        Genealogical date arithmetic using GedCore's GEDCOM
                        date grammar -- no GEDCOM file read or required.
                          normalize --date <d>            resolve a dual-dated year
                          add|sub   --date <d> --age <y/m/d>   date +/- age
                          diff      --from <d> --to <d>    elapsed y/m/d between two dates
                        Dates are exact Gregorian "D MON YYYY"; --age is
                        e.g. "63y 4m 2d". See README/AGENTS.md for details.

        Options:
                    --help, -h       Show this help message.
                    --version, -v    Show the GedFire version.
        """);
}
