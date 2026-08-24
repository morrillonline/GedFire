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
//   gedfire generate     --input <ged>    --output-dir <dir>  [--format html] [--media-base-url <url>]
//   gedfire export-index --input <ged>    --output <json>
//   gedfire select-targets --input <ged>  --output <wanted.json> --count <N> --surnames <list>
//   gedfire apply        --input <ged>    --changes <json> --items all|1,3 [--dry-run]
//   gedfire validate     <file> [--warnings-as-errors]
//   gedfire pack         --input <ged>    --media-dir <dir> --output <gdz>
//   gedfire unpack       --input <gdz>    --output-dir <dir>
//   gedfire mcp          --input <ged> [--read-only] [--enforce-privacy]
//   gedfire date-calc    --op normalize|add|sub|diff [--date <d>] [--from <d>] [--to <d>] [--age <y/m/d>]
//   gedfire find-person  --input <ged> --query <name> [--max-results N] [hint flags...]
//   gedfire get-record   --input <ged> --xref <@I1@>
//   gedfire get-document-stats --input <ged>
//
// find-person, get-record, and get-document-stats are one-shot CLI mirrors
// of the mcp server's find_person/get_record/get_document_stats tools: same
// engine, same JSON result shape, no protocol in between.
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
    "find-person"        => await RunFindPerson(args[1..]),
    "get-record"         => await RunGetRecord(args[1..]),
    "get-document-stats" => await RunGetDocumentStats(args[1..]),
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
    var cl = CommandLine.Parse(args, ["--input", "--output-dir", "--format", "--template", "--media-base-url"]);
    string? input        = cl.Value("--input");
    string? outputDir    = cl.Value("--output-dir");
    string? templateArg  = cl.Value("--template");
    string  mediaBaseUrl = cl.Value("--media-base-url") ?? "media/";

    if (cl.Error is not null || input is null || outputDir is null)
    {
        if (cl.Error is not null) Console.Error.WriteLine(cl.Error);
        Console.Error.WriteLine("Usage: gedfire generate --input <ged> --output-dir <dir> [--format html] [--template <file>] [--media-base-url <url>]");
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
    // Media always resolves against the GEDCOM's own directory -- every
    // FILE payload this engine writes is self-describing (MediaFileRequest.
    // NormalizePath prepends "media/" per GEDCOM 7 §2.12), so there is no
    // second media-location convention to configure here (see gedfire mcp's
    // matching default below).
    string mediaDir = Path.GetDirectoryName(Path.GetFullPath(input)) ?? ".";
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

    if (!ItemSelector.TryParse(items, changeset, out int[] itemNumbers, out string? itemsError))
    {
        Console.Error.WriteLine($"--{itemsError}");
        return 1;
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
    var cl = CommandLine.Parse(args, ["--input"], ["--read-only", "--enforce-privacy"]);
    string? input = cl.Value("--input");
    bool readOnly = cl.Has("--read-only");
    bool enforcePrivacy = cl.Has("--enforce-privacy");

    if (cl.Error is not null || input is null)
    {
        if (cl.Error is not null) Console.Error.WriteLine(cl.Error);
        Console.Error.WriteLine("Usage: gedfire mcp --input <ged> [--read-only] [--enforce-privacy]");
        return 1;
    }

    if (!File.Exists(input))
    {
        Console.Error.WriteLine($"Input file not found: {input}");
        return 1;
    }

    string absoluteInput = Path.GetFullPath(input);
    string absoluteMediaDir = Path.GetFullPath(DefaultMcpMediaDir(absoluteInput));

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
        if (enforcePrivacy)
            PrivacyFilter.Apply(model, DateTime.UtcNow.Year);
        var info = new FileInfo(absoluteInput);
        initialSnapshot = new DocumentSnapshot(model, doc.Version, File.GetLastWriteTimeUtc(absoluteInput), info.Length);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Failed to start MCP server: {ex.Message}");
        return 1;
    }

    var session = new DocumentSession(absoluteInput, initialSnapshot, enforcePrivacy);
    await using var watcher = new DocumentFileWatcher(session, absoluteInput);
    var gate = new ToolGate();
    var dateCalc = new DateCalcTool(gate);
    var findPerson = new FindPersonTool(session, gate, nicknames);
    var getDocumentStats = new GetDocumentStatsTool(session, gate);
    var getRecord = new GetRecordTool(session, gate, absoluteMediaDir);
    var validateChangeset = new ValidateChangesetTool(absoluteInput, gate);
    var applyChangeset = new ApplyChangesetTool(absoluteInput, gate, readOnly);
    var describeChangesetOps = new DescribeChangesetOpsTool(gate);

    // Listed alphabetically by name for readability; the SDK's own
    // McpServerPrimitiveCollection does not preserve this as the advertised
    // tools/list order. apply_changeset is always registered, even under
    // --read-only -- it refuses each call itself (see ApplyChangesetTool)
    // rather than disappearing from the advertised tool list, so a client
    // that cached the list before a config change still gets an explicit
    // refusal instead of an unknown-tool error.
    var toolCollection = new McpServerPrimitiveCollection<McpServerTool>
    {
        applyChangeset.ToMcpServerTool(),
        dateCalc.ToMcpServerTool(),
        describeChangesetOps.ToMcpServerTool(),
        findPerson.ToMcpServerTool(),
        getDocumentStats.ToMcpServerTool(),
        getRecord.ToMcpServerTool(),
        validateChangeset.ToMcpServerTool(),
    };

    string instructions =
        "Every xref returned by a tool on this server (an individual or family reference such as \"@I123@\" " +
        "or \"@F45@\") belongs only to the single GEDCOM document this server was started against, and is " +
        "meaningless to any other document or provider. date_calc, find_person, get_document_stats, get_record, " +
        "describe_changeset_ops, and validate_changeset never modify that file. apply_changeset is the only " +
        "tool that writes to it, and only after validation and in-memory verification both pass; call " +
        "validate_changeset first with the same arguments to preview a changeset without writing. Call " +
        "describe_changeset_ops before composing a changeset from scratch -- it returns the full op dialect, " +
        "so there is no need to know the changeset format in advance or discover it by trial and error.";
    if (readOnly)
        instructions +=
            " This server was started with --read-only: every call to apply_changeset is refused. " +
            "validate_changeset remains available to preview changesets.";
    if (enforcePrivacy)
        instructions +=
            " This server was started with --enforce-privacy: individuals with an RESN of CONFIDENTIAL or " +
            "PRIVACY, and individuals plausibly still living (no death-class fact, born within the last " +
            $"{PrivacyFilter.PlausiblyLivingAgeYears} years), are reduced to a \"{PrivacyFilter.LivingGivenName} " +
            "<Surname>\" placeholder with no dates, places, notes, or media -- the same treatment `gedfire " +
            "generate` applies before publishing a site. Do not treat a placeholder as the whole record; it is " +
            "withheld, not absent.";

    var serverOptions = new McpServerOptions
    {
        ServerInfo = new Implementation { Name = "gedfire", Version = GedFireVersion() },
        Capabilities = new ServerCapabilities { Tools = new ToolsCapability { ListChanged = false } },
        ToolCollection = toolCollection,
        // States once that returned xrefs belong only to this server's bound
        // document and which tool is the write path (and when it's
        // disabled). Tool-specific calling guidance remains on the tool
        // itself.
        ServerInstructions = instructions,
    };

    await using var transport = new StdioServerTransport(serverOptions, loggerFactory: null);
    var server = McpServer.Create(transport, serverOptions, loggerFactory: null, serviceProvider: null);
    await server.RunAsync(CancellationToken.None).ConfigureAwait(false);
    return 0;
}

static string GedFireVersion() => GedFire.Mcp.ServerVersion.Current;

// gedfire mcp's media resolution: always the GEDCOM's own directory -- the
// same convention `generate` uses, and not independently configurable.
// Every FILE payload CreateOrUpdateMediaOp writes is self-describing
// (MediaFileRequest.NormalizePath prepends "media/" per GEDCOM 7 §2.12's
// recommendation), so resolution needs no overriding here: a payload of
// "media/photo.jpg" already names its own subfolder relative to this
// directory. A record written before that normalization existed, with a
// bare filename and no "media/" segment, will not resolve under this
// default -- that is a data gap in the existing file to fix at the source
// (resubmit its media op), not something to paper over with a second,
// configurable media-location convention.
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
        // year 1-9999 range) is a usage error, not a crash.
        Console.Error.WriteLine(ex.Message);
        return 1;
    }
}

// -----------------------------------------------------------------------
// One-shot CLI mirrors of the mcp server's read-only tools: find-person,
// get-record, get-document-stats. Each bootstraps its own single-use
// DocumentSession/ToolGate exactly like RunMcp does, calls the same tool
// class's HandleAsync, and prints the identical JSON result (or error text)
// -- the CLI and MCP surfaces run the same code, not two implementations of
// the same idea.
// -----------------------------------------------------------------------

static bool TryLoadOneShotSession(string input, out DocumentSession? session, out string absoluteInput)
{
    absoluteInput = Path.GetFullPath(input);
    try
    {
        var doc = GedReader.ReadFile(absoluteInput);
        var model = ModelBuilder.Build(doc);
        var info = new FileInfo(absoluteInput);
        var snapshot = new DocumentSnapshot(model, doc.Version, File.GetLastWriteTimeUtc(absoluteInput), info.Length);
        session = new DocumentSession(absoluteInput, snapshot);
        return true;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Failed to read {input}: {ex.Message}");
        session = null;
        return false;
    }
}

// Prints a tool's CallToolResult the way every other CLI command reports
// success or failure: informational output (here, the tool's own compact
// JSON) to stdout with exit 0, error text to stderr with exit 1.
static int WriteToolResult(CallToolResult result)
{
    string text = ((TextContentBlock)result.Content[0]).Text;
    if (result.IsError is true)
    {
        Console.Error.WriteLine(text);
        return 1;
    }
    Console.WriteLine(text);
    return 0;
}

static bool TryParseOptionalYear(string? raw, string optionName, out int? value)
{
    if (raw is null) { value = null; return true; }
    if (!int.TryParse(raw, out int parsed))
    {
        Console.Error.WriteLine($"{optionName} must be an integer, got: {raw}");
        value = null;
        return false;
    }
    value = parsed;
    return true;
}

static async Task<int> RunFindPerson(string[] args)
{
    var cl = CommandLine.Parse(args, [
        "--input", "--query", "--max-results",
        "--birth-year", "--birth-place", "--death-year", "--death-place",
        "--father", "--mother",
        "--spouse-name", "--marriage-year", "--marriage-place",
    ]);
    string? input = cl.Value("--input");
    string? query = cl.Value("--query");

    const string usage =
        "Usage: gedfire find-person --input <ged> --query <name> [--max-results N]\n" +
        "       [--birth-year Y] [--birth-place P] [--death-year Y] [--death-place P]\n" +
        "       [--father NAME] [--mother NAME]\n" +
        "       [--spouse-name NAME] [--marriage-year Y] [--marriage-place P]";

    if (cl.Error is not null || input is null || query is null)
    {
        if (cl.Error is not null) Console.Error.WriteLine(cl.Error);
        Console.Error.WriteLine(usage);
        return 1;
    }

    if (!File.Exists(input))
    {
        Console.Error.WriteLine($"Input file not found: {input}");
        return 1;
    }

    int maxResults = 8;
    string? maxResultsArg = cl.Value("--max-results");
    if (maxResultsArg is not null && !int.TryParse(maxResultsArg, out maxResults))
    {
        Console.Error.WriteLine($"--max-results must be an integer, got: {maxResultsArg}");
        return 1;
    }

    if (!TryParseOptionalYear(cl.Value("--birth-year"), "--birth-year", out int? birthYear)) return 1;
    if (!TryParseOptionalYear(cl.Value("--death-year"), "--death-year", out int? deathYear)) return 1;
    if (!TryParseOptionalYear(cl.Value("--marriage-year"), "--marriage-year", out int? marriageYear)) return 1;

    string? birthPlace = cl.Value("--birth-place");
    string? deathPlace = cl.Value("--death-place");
    string? father = cl.Value("--father");
    string? mother = cl.Value("--mother");
    string? spouseName = cl.Value("--spouse-name");
    string? marriagePlace = cl.Value("--marriage-place");

    FindPersonEventHintArgs? birth = birthYear is not null || birthPlace is not null
        ? new FindPersonEventHintArgs { Year = birthYear, Place = birthPlace } : null;
    FindPersonEventHintArgs? death = deathYear is not null || deathPlace is not null
        ? new FindPersonEventHintArgs { Year = deathYear, Place = deathPlace } : null;
    FindPersonParentsHintArgs? parents = father is not null || mother is not null
        ? new FindPersonParentsHintArgs { Father = father, Mother = mother } : null;
    FindPersonEventHintArgs? marriage = marriageYear is not null || marriagePlace is not null
        ? new FindPersonEventHintArgs { Year = marriageYear, Place = marriagePlace } : null;
    FindPersonSpouseHintArgs? spouse = spouseName is not null || marriage is not null
        ? new FindPersonSpouseHintArgs { Name = spouseName, Marriage = marriage } : null;

    FindPersonHintsArgs? hints = birth is null && death is null && parents is null && spouse is null
        ? null
        : new FindPersonHintsArgs { Birth = birth, Death = death, Parents = parents, Spouse = spouse };

    if (!TryLoadOneShotSession(input, out var session, out _)) return 1;

    var gate = new ToolGate();
    var nicknames = NicknameDirectory.LoadEmbedded();
    var tool = new FindPersonTool(session!, gate, nicknames);
    var result = await tool.HandleAsync(query, hints, CancellationToken.None, maxResults).ConfigureAwait(false);
    return WriteToolResult(result);
}

static async Task<int> RunGetRecord(string[] args)
{
    var cl = CommandLine.Parse(args, ["--input", "--xref"]);
    string? input = cl.Value("--input");
    string? xref = cl.Value("--xref");

    if (cl.Error is not null || input is null || xref is null)
    {
        if (cl.Error is not null) Console.Error.WriteLine(cl.Error);
        Console.Error.WriteLine("Usage: gedfire get-record --input <ged> --xref <@I1@>");
        return 1;
    }

    if (!File.Exists(input))
    {
        Console.Error.WriteLine($"Input file not found: {input}");
        return 1;
    }

    if (!TryLoadOneShotSession(input, out var session, out string absoluteInput)) return 1;

    var gate = new ToolGate();
    var tool = new GetRecordTool(session!, gate, DefaultMcpMediaDir(absoluteInput));
    var result = await tool.HandleAsync(xref, CancellationToken.None).ConfigureAwait(false);
    return WriteToolResult(result);
}

static async Task<int> RunGetDocumentStats(string[] args)
{
    var cl = CommandLine.Parse(args, ["--input"]);
    string? input = cl.Value("--input");

    if (cl.Error is not null || input is null)
    {
        if (cl.Error is not null) Console.Error.WriteLine(cl.Error);
        Console.Error.WriteLine("Usage: gedfire get-document-stats --input <ged>");
        return 1;
    }

    if (!File.Exists(input))
    {
        Console.Error.WriteLine($"Input file not found: {input}");
        return 1;
    }

    if (!TryLoadOneShotSession(input, out var session, out _)) return 1;

    var gate = new ToolGate();
    var tool = new GetDocumentStatsTool(session!, gate);
    var result = await tool.HandleAsync(CancellationToken.None).ConfigureAwait(false);
    return WriteToolResult(result);
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

          mcp       --input <ged> [--read-only] [--enforce-privacy]
                        Start a stdio Model Context Protocol server exposing
                        this GEDCOM to MCP-compatible agent clients. Resident
                        process: stays running until stdin closes. Six tools:
                        date_calc, find_person, get_document_stats,
                        get_record, and validate_changeset never modify the
                        file; apply_changeset is the only one that writes,
                        after validation and in-memory verification pass.
                        Watches the input file and reloads automatically if
                        it changes on disk.
                          --read-only        Disable apply_changeset: every
                                              call to it is refused.
                                              validate_changeset and every
                                              read-only tool stay available.
                          --enforce-privacy   Apply the same privacy filter
                                              `generate` uses before
                                              publishing a site: RESN
                                              CONFIDENTIAL/PRIVACY and
                                              plausibly-living individuals
                                              are reduced to a placeholder
                                              in every tool's output.

          date-calc --op normalize|add|sub|diff
                        Genealogical date arithmetic using GedCore's GEDCOM
                        date grammar -- no GEDCOM file read or required.
                          normalize --date <d>            resolve a dual-dated year
                          add|sub   --date <d> --age <y/m/d>   date +/- age
                          diff      --from <d> --to <d>    elapsed y/m/d between two dates
                        Dates are exact Gregorian "D MON YYYY"; --age is
                        e.g. "63y 4m 2d". See README/AGENTS.md for details.

          find-person --input <ged> --query <name>
                        One-shot mirror of the mcp server's find_person tool:
                        same matcher, same JSON result, no protocol needed.
                          [--max-results N]                 1-20, default 8
                          [--birth-year Y] [--birth-place P]
                          [--death-year Y] [--death-place P]
                          [--father NAME] [--mother NAME]
                          [--spouse-name NAME] [--marriage-year Y] [--marriage-place P]

          get-record --input <ged> --xref <@I1@>
                        One-shot mirror of the mcp server's get_record tool.

          get-document-stats --input <ged>
                        One-shot mirror of the mcp server's get_document_stats
                        tool: person/family counts, GEDCOM version, and the
                        running gedfire version.

        Options:
                    --help, -h       Show this help message.
                    --version, -v    Show the GedFire version.
        """);
}
