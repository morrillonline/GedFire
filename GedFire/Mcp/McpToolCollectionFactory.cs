using GedCore.Matching;
using GedCore.Policy;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace GedFire.Mcp;

// ---------------------------------------------------------------------------
// Builds every tool the server advertises. apply_changeset is always
// registered, even under --read-only: it refuses each call itself rather
// than disappearing, so a client that cached the tool list before a config
// change gets an explicit refusal instead of an unknown-tool error.
// ---------------------------------------------------------------------------

public static class McpToolCollectionFactory
{
    sealed record Registration(
        ToolDefinition Definition, Func<ToolContext, McpServerTool>? Bound = null,
        Func<ToolGate, McpServerTool>? Standalone = null)
    {
        public bool RequiresDocument => Standalone is null;

        public McpServerTool Create(ToolContext? context = null) => Standalone is { } standalone
            ? standalone(context?.Gate ?? new ToolGate())
            : Bound!(context ?? throw new ArgumentException($"{Definition.Name} requires a GEDCOM document."));
    }

    static readonly IReadOnlyList<Registration> Registrations =
    [
        new(new(ApplyChangesetTool.ToolName, ApplyChangesetTool.Description,
                ApplyChangesetTool.InputSchemaJson, ApplyChangesetTool.OutputSchemaJson, ToolBehavior.Writes),
            s => new ApplyChangesetTool(s.DocumentPath, s.Gate, s.ReadOnly, s.Policy).ToMcpServerTool()),
        new(new(CheckPlausibilityTool.ToolName, CheckPlausibilityTool.Description,
                CheckPlausibilityTool.InputSchemaJson, CheckPlausibilityTool.OutputSchemaJson, ToolBehavior.ReadOnlyIdempotent),
            s => new CheckPlausibilityTool(s.DocumentPath, s.Gate).ToMcpServerTool()),
        new(new(DateCalcTool.ToolName, DateCalcTool.Description,
                DateCalcTool.InputSchemaJson, DateCalcTool.OutputSchemaJson, ToolBehavior.ReadOnlyIdempotent),
            Standalone: gate => new DateCalcTool(gate).ToMcpServerTool()),
        new(new(DescribeChangesetOpsTool.ToolName, DescribeChangesetOpsTool.Description,
                DescribeChangesetOpsTool.InputSchemaJson, DescribeChangesetOpsTool.OutputSchemaJson, ToolBehavior.ReadOnlyIdempotent),
            Standalone: gate => new DescribeChangesetOpsTool(gate).ToMcpServerTool()),
        new(new(DraftChangesetTool.ToolName, DraftChangesetTool.Description,
                DraftChangesetTool.InputSchemaJson, DraftChangesetTool.OutputSchemaJson, ToolBehavior.CreatesFiles),
            s => new DraftChangesetTool(s.Session, s.Gate, s.Policy).ToMcpServerTool()),
        new(new(DraftPackageTool.ToolName, DraftPackageTool.Description,
                DraftPackageTool.InputSchemaJson, DraftPackageTool.OutputSchemaJson, ToolBehavior.CreatesFiles),
            s => new DraftPackageTool(s.DocumentPath, s.Gate, s.Policy).ToMcpServerTool()),
        new(new(FindFamilyTool.ToolName, FindFamilyTool.Description,
                FindFamilyTool.InputSchemaJson, FindFamilyTool.OutputSchemaJson, ToolBehavior.ReadOnlyIdempotent),
            s => new FindFamilyTool(s.Session, s.Gate, s.Nicknames).ToMcpServerTool()),
        new(new(FindPersonTool.ToolName, FindPersonTool.Description,
                FindPersonSchemas.InputSchemaJson, FindPersonSchemas.OutputSchemaJson, ToolBehavior.ReadOnlyIdempotent),
            s => new FindPersonTool(s.Session, s.Gate, s.Nicknames).ToMcpServerTool()),
        new(new(FindSourceTool.ToolName, FindSourceTool.Description,
                FindSourceTool.InputSchemaJson, FindSourceTool.OutputSchemaJson, ToolBehavior.ReadOnlyIdempotent),
            s => new FindSourceTool(s.Session, s.Gate).ToMcpServerTool()),
        new(new(GetDocumentStatsTool.ToolName, GetDocumentStatsTool.Description,
                GetDocumentStatsTool.InputSchemaJson, GetDocumentStatsTool.OutputSchemaJson, ToolBehavior.ReadOnlyIdempotent),
            s => new GetDocumentStatsTool(s.Session, s.Gate).ToMcpServerTool()),
        new(new(GetRecordTool.ToolName, GetRecordTool.Description,
                GetRecordTool.InputSchemaJson, GetRecordTool.OutputSchemaJson, ToolBehavior.ReadOnlyIdempotent),
            s => new GetRecordTool(s.Session, s.Gate, s.MediaDirectory).ToMcpServerTool()),
        new(new(GetRecordsTool.ToolName, GetRecordsTool.Description,
                GetRecordsTool.InputSchemaJson, GetRecordsTool.OutputSchemaJson, ToolBehavior.ReadOnlyIdempotent),
            s => new GetRecordsTool(s.Session, s.Gate, s.MediaDirectory).ToMcpServerTool()),
        new(new(ListPeopleTool.ToolName, ListPeopleTool.Description,
                ListPeopleTool.InputSchemaJson, ListPeopleTool.OutputSchemaJson, ToolBehavior.ReadOnlyIdempotent),
            s => new ListPeopleTool(s.Session, s.Gate).ToMcpServerTool()),
        new(new(ListUnanchoredPeopleTool.ToolName, ListUnanchoredPeopleTool.Description,
                ListUnanchoredPeopleTool.InputSchemaJson, ListUnanchoredPeopleTool.OutputSchemaJson, ToolBehavior.ReadOnlyIdempotent),
            s => new ListUnanchoredPeopleTool(s.Session, s.Gate).ToMcpServerTool()),
        new(new(MatchRecordTool.ToolName, MatchRecordTool.Description,
                MatchRecordTool.InputSchemaJson, MatchRecordTool.OutputSchemaJson, ToolBehavior.ReadOnlyIdempotent),
            s => new MatchRecordTool(s.Session, s.Gate, s.Nicknames).ToMcpServerTool()),
        new(new(ResearchBriefTool.ToolName, ResearchBriefTool.Description,
                ResearchBriefTool.InputSchemaJson, ResearchBriefTool.OutputSchemaJson, ToolBehavior.ReadOnlyIdempotent),
            s => new ResearchBriefTool(s.Session, s.Gate, s.DocumentPath, s.MediaDirectory).ToMcpServerTool()),
        new(new(SelectTargetsTool.ToolName, SelectTargetsTool.Description,
                SelectTargetsTool.InputSchemaJson, SelectTargetsTool.OutputSchemaJson, ToolBehavior.ReadOnlyNonIdempotent),
            s => new SelectTargetsTool(s.Session, s.Gate).ToMcpServerTool()),
        new(new(ValidateChangesetTool.ToolName, ValidateChangesetTool.Description,
                ValidateChangesetTool.InputSchemaJson, ValidateChangesetTool.OutputSchemaJson, ToolBehavior.ReadOnlyIdempotent),
            s => new ValidateChangesetTool(s.DocumentPath, s.Gate, s.Policy).ToMcpServerTool()),
        new(new(ValidateDocumentTool.ToolName, ValidateDocumentTool.Description,
                ValidateDocumentTool.InputSchemaJson, ValidateDocumentTool.OutputSchemaJson, ToolBehavior.ReadOnlyIdempotent),
            s => new ValidateDocumentTool(s.DocumentPath, s.Gate).ToMcpServerTool()),
    ];

    static readonly IReadOnlyDictionary<string, Registration> ByName = Registrations.ToDictionary(r => r.Definition.Name, StringComparer.Ordinal);

    public static IReadOnlyList<Tool> Describe() => [.. Registrations.Select(r => r.Definition.Describe())];

    public static Tool? Describe(string name) => ByName.TryGetValue(name, out var registration) ? registration.Definition.Describe() : null;

    public static bool RequiresDocument(string name) => Find(name).RequiresDocument;

    public static McpServerTool CreateStandalone(string name)
    {
        var registration = Find(name);
        if (registration.RequiresDocument) throw new ArgumentException($"{name} requires a GEDCOM document.", nameof(name));
        return registration.Create();
    }

    public static McpServerTool Create(string name, ToolContext context) => Find(name).Create(context);

    public static McpServerPrimitiveCollection<McpServerTool> Create(ToolContext context) =>
        [.. Registrations.Select(r => r.Create(context))];

    public static McpServerPrimitiveCollection<McpServerTool> Create(
        DocumentSession session, ToolGate gate, NicknameDirectory nicknames,
        string documentPath, string mediaDirectory, bool readOnly, ChangesetPolicy? policy = null) =>
        Create(new ToolContext(session, documentPath)
        {
            Gate = gate,
            Nicknames = nicknames,
            MediaDirectory = mediaDirectory,
            ReadOnly = readOnly,
            Policy = policy,
        });

    static Registration Find(string name) => ByName.TryGetValue(name, out var registration)
        ? registration
        : throw new ArgumentException($"Unknown tool: {name}", nameof(name));
}
