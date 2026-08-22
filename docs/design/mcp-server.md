# MCP Server — design

Status: draft, not yet implemented. Written for review before any code lands.
Covers the `gedfire mcp` verb in general and one concrete tool, `find_person`,
in full detail. Every later MCP tool should be designed as an addendum to
this document, governed by the same requirements and policies below — not
as a fresh design each time.

## Problem

GedFire is a local, file-oriented command-line tool. Every capability today
is a one-shot verb: parse arguments, read a file, do one thing, write a
file or print text, exit. That model works well for scripts and CI, and it
is wrong for a conversational agent. An agent working alongside a person
researching their family needs to:

- hold a GEDCOM open across many small questions in one sitting, without
  re-parsing it on every question;
- receive answers as data it can act on (an xref it can pass to the next
  call), not prose it has to scrape;
- let the person refer to someone by name — "my great-grandfather Fred
  Morrill" — without ever knowing or supplying an internal record id.

None of the existing CLI verbs is shaped for this. `export-index` writes a
file for a separate query tool to read later; nothing resolves a name to a
person in conversation, in one step, right now.

This document designs `gedfire mcp`: a stdio Model Context Protocol server
that exposes GedFire's existing engine to MCP-compatible agent clients
(Claude Desktop, Claude Code, and similar), and the first tool built on it,
`find_person`. Everything below — transport, session model, tool
granularity, response shape — is derived from the requirements in the next
section. Read that section first; it is the thing every later decision in
this document, and every later tool, has to trace back to.

## Requirements

These are stated once, at the top, because every design choice below is an
application of one of them. When a future tool's design seems to conflict
with a choice here, the fix is to change the tool, not to quietly violate
the requirement.

1. **Adapter, not a second mutation path.** `gedfire mcp` calls the same
   public engine the CLI verbs call. It does not duplicate parsing,
   validation, or apply logic, and it never opens a route to mutate a
   GEDCOM that bypasses the changeset-and-approval model the CLI already
   enforces.
2. **Local process, explicit trust boundary.** GedFire talks only to the
  client process that launched it, over stdio. GedFire itself makes no
  network requests and sends no telemetry. The MCP client may send tool
  arguments and results to a remotely hosted model; that behavior is
  outside GedFire's control and must be stated plainly in setup
  documentation.
3. **A human never needs to know an internal id to start a conversation.**
   Every people-facing tool must accept a name (plus whatever incidental
   detail the person happened to mention) and either resolve it or return
   enough to let the agent ask a clarifying question in plain
   conversation. No tool may require an `@I…@`/`@F…@` xref as the *only*
   way in.
4. **Tool results are structured data, read by a model, not prose read by
   a person.** Every tool returns JSON: exact xrefs, exact dates with
   their qualifiers, exact citation references. Formatting a fact into a
   sentence is the calling agent's job, done fresh each time for the
   person it is actually talking to, not baked into the tool response.
5. **Read-only first, writes through changesets later.** The initial tool
   set only inspects a GEDCOM — not as a permanent stance, but because
   reading has to work before writing is worth designing. Write support
   arrives as changeset validate/apply, the same reviewed-changeset path
   the CLI enforces (requirement 1), in its own design pass; it is not
   part of this document's first-tool scope.
6. **Every tool is fully described where it is declared.** A tool's
   `description` and its parameters' JSON Schema `description`s are the
   only channel most clients give the model for learning how to call it
   correctly. No tool ships without both.
7. **Few, well-scoped tools; not many narrow ones and not one dispatcher.**
   Each tool covers one complete, natural unit of the domain (a person, a
   family, a name lookup) — never one GEDCOM tag, never a generic
   `operation` string hiding an untyped payload.
8. **Return no more family data than the task needs.** A lookup tool returns
   enough context to identify a person and their immediate family records,
   not every fact attached to them. A later detail tool may return a fuller
   record when the user actually asks for it.
9. **Protocol behavior comes from the MCP specification and SDK.** The
   server uses the official C# SDK rather than implementing JSON-RPC framing,
   negotiation, cancellation, or error envelopes itself. Supported protocol
   revisions are named and tested; upgrading the SDK or protocol is an
   explicit compatibility change, not an incidental package update.
10. **Return stable handoff identifiers for follow-up tools.** A resolved
  person includes the local xref of their child-family, when known, and of
  every family in which they are a spouse — childless marriages included,
  because a marriage with no children is exactly the case where only the
  family record can distinguish it. A spouse-family entry adds only
  the raw marriage date and spouse name needed to distinguish multiple
  marriages. Its document-scoped xref lets later detail or research tools
  request the family directly. Local GEDCOM xrefs are never reused as
  identifiers from FamilySearch, WikiTree, or another provider.

## Non-goals for this document

- The full initial tool roster (`get_family`, ancestor/descendant
  traversal, relationship-path finding, the quality-check cluster,
  `validate_changeset`/`apply_changeset`). Each gets its own short design
  addendum once `find_person` and the server scaffold it depends on are
  built and reviewed. This document defines the scaffold and the first
  tool completely; it only names the others so their shape can be
  anticipated.
- Implementing FamilySearch, WikiTree, or any other external-service API.
  Those tools are separate, later decisions with their own network consent,
  authentication, provider identifiers, rate limits, provenance, and
  source-specific schemas. This document does define the local person and
  family xrefs those tools receive as handoff identifiers (requirement 10).
- The meta-tool-vs-one-tool-per-capability question, beyond stating the
  policy in requirement 7. It only becomes a live decision once the tool
  count is large enough to matter; it is not resolved here because it
  does not need to be yet.

## Architecture

### One binary, one more verb

`gedfire mcp --input <ged>` is a new case in
`Program.cs`'s existing
`args[0]` switch, next to `create`, `apply`, `generate`, and the rest. It
is the same executable, the same `GedCore` engine, built and shipped the
same way. Nothing about the existing verbs changes.

`--input` is required and resolved to an absolute path once, before the
protocol starts. Binding one server process to one document keeps local file
paths out of model-visible tool arguments, gives every xref one unambiguous
document scope, and avoids adding document-management tools before they are
needed (requirements 2, 7, and 8). A later multi-document design may add an
explicit opaque document handle; it must not introduce implicit connection
state or require absolute paths on every domain-tool call.

The server serves the research file as-is. `PrivacyFilter` belongs to HTML
site generation and nothing else; the MCP server never applies it. The
operator is querying their own local research file through a client they
chose, and what that client does with returned data is the trust-boundary
statement in requirement 2, made in setup documentation — not a filter
GedFire imposes between a researcher and their own data.

Media always resolves against the GEDCOM's own directory — the same
convention `generate` uses (requirement 1 — one media-location convention,
not a second one invented for this verb). Resolved once at startup,
alongside `--input`; it does not change across reloads.

This was not the first default tried. A real research file's `OBJE.FILE`
payloads turned out to be bare filenames with no `media/` prefix, which
only resolve under a `media` subfolder next to the GEDCOM — so `mcp`
briefly defaulted to `<gedcom-dir>/media` specifically to make that file
resolve. That default was wrong, and reverted, once the real defect surfaced:
`CreateOrUpdateMediaOp` (`GedCore/Apply/Ops/MediaOps.cs`) never applied
GEDCOM 7 §2.12's recommended `media/` prefix when writing a new `FILE`
payload, so every media record it ever wrote depended on whatever base
directory a later reader happened to guess, rather than describing its
own location. `MediaFileRequest.NormalizePath` now applies that prefix
once, at changeset-parse time, so every `FILE` payload this engine writes
going forward is self-describing (`media/photo.jpg`, resolved against the
GEDCOM's own directory — no guessing required). Defaulting `mcp` to
`<gedcom-dir>/media` on top of that fix would have double-nested every new
record's path (`media/media/photo.jpg`); matching `generate`'s existing,
unmodified default is what's actually consistent with self-describing
paths. A record written before this fix existed, whose payload still lacks
the prefix, will not resolve under this default — that is a gap in the
existing data to close at the source (resubmit its `createOrUpdateMedia`
op so the normalization rewrites it), not something `mcp` should
special-case a different base directory to paper over.

**Addendum, post-4.0.5:** this section originally specified an optional
`--media-dir <dir>` override on both `mcp` and `generate`, defaulting to
the GEDCOM's own directory but letting a caller point elsewhere. Once
self-describing `media/`-prefixed paths (above) became the only supported
convention, that override had no legitimate use left — every file this
engine writes resolves correctly under the one default, and a file that
doesn't is a data gap to fix at the source, not a base directory to
special-case. The flag was removed from both verbs' CLI surface; media
resolution is no longer independently configurable on either. `pack`
keeps its own `--media-dir <dir>`, which is unrelated: it names the
loose-media source directory to bundle *into* a GEDZIP archive, not a
resolution override for already-referenced paths, so there is no default
to fall back to.

### Lifecycle: resident process, not one-shot

Every other verb parses arguments, does one thing, and exits. `gedfire mcp`
instead validates and parses its input, starts the SDK's stdio server, and
stays resident. It exits promptly and with code 0 when stdin closes or reaches
EOF, the portable graceful-shutdown signal for stdio MCP. Startup validation
or parsing failures are written to stderr and exit with code 1 before any
protocol output is written. There is no invented `shutdown` JSON-RPC method
(requirements 1 and 9).

Each tool receives and honors the SDK cancellation token. Cancellation stops
the work as soon as practical and does not emit a late response after the SDK
has accepted the cancellation. The server permits at most four tool calls to
execute concurrently; additional calls wait asynchronously and remain
cancellable. A per-process sliding window admits at most 120 tool calls per
minute; excess calls return a retryable tool execution error. These bounds
satisfy the protocol's local resource-control requirement without changing
successful tool results.

### Transport discipline

- **stdin** carries the SDK's UTF-8, newline-delimited MCP messages from the
  client. Messages contain no embedded newlines.
- **stdout** is reserved exclusively for JSON-RPC responses and
  notifications. No code reachable from the `mcp` verb may write
  human-readable progress text to stdout the way `RunExportIndex` or
  `RunApply` do today — that would corrupt the protocol stream mid-message.
- **stderr** is free for diagnostics and logging, exactly as a well-behaved
  stdio server should use it.

GedFire uses the official `ModelContextProtocol` C# package, pinned to
**2.0.0** — the release that brings the SDK into stable alignment with the
`2026-07-28` specification (`server/discover` with fallback to legacy
`initialize`) while its `1.0`-era `2025-11-25` support remains reachable
through the same negotiation path. The initial compatibility target is
dual-era: current MCP `2026-07-28` clients using per-request metadata and
`server/discover`, plus initialization-based clients through `2025-11-25`.
The SDK owns framing, version negotiation, capability metadata, cancellation,
and response envelopes. If the selected SDK cannot demonstrate both eras in
tests, implementation stops for a design update rather than silently dropping
a named client (requirement 9).

### Resident document and reload policy

A resident process must not re-parse the whole GEDCOM on every tool call —
that was free when each CLI verb ran once and exited; it is wasteful once
one process serves many calls in one sitting.

- Startup reads the document through `GedReader`, builds it through
  `ModelBuilder`, and stores one snapshot for tool calls (requirement 1).
  The snapshot is read-only by discipline, not by type: the model classes
  stay mutable because the planned write workflow (changeset
  validate/apply, a later addendum) will need them to be, applied through
  the changeset engine with a reload following the file change — never by
  a tool mutating the resident snapshot in place.
- The snapshot records the source file's UTC last-write time and byte length.
  Before each call, the server compares both values with current metadata.
  This deliberately misses a same-length in-place write inside the
  timestamp's granularity; hashing the file on every call would reread it and
  defeat the resident model. Save-and-replace editors, the normal way a local
  research file changes, always alter the timestamp.
- When either value changes, one per-document async lock performs the reload.
  Other calls await that reload; they never observe a half-built model.
- The reloader captures metadata before and after reading. If the values
  differ, it retries once because a writer was active. If they change again,
  or parsing fails, the call returns an actionable tool execution error. It
  never serves the stale snapshot as if it were current.
- A successful parse and model build are completed before one atomic
  snapshot replacement. Tool calls then share that snapshot without
  mutation (requirements 1 and 5).

### Tool registration and server guidance

- Each tool declares `name`, a full-sentence `description` covering not
  just what it does but when to call it, and an `inputSchema` whose every
  property carries its own `description` (format, units, examples where
  the shape is not obvious — e.g. what an xref string looks like).
- Each tool that returns structured data declares an `outputSchema`. A
  successful call puts the schema-conforming value in `structuredContent`
  and also puts its compact JSON serialization in a text content block for
  clients that do not yet consume structured content (requirements 4 and 6).
- Each tool carries annotations (`readOnlyHint`, `destructiveHint`,
  `idempotentHint`) that are literally true for that tool, so a client
  can gate confirmation UI correctly without reading prose. Every tool in
  this document's initial scope is `readOnlyHint: true`.
- The SDK's server guidance mechanism for the negotiated protocol revision
  states once that returned xrefs belong only to this server's bound document
  and that no initial-release tool mutates the file. Tool-specific calling
  guidance remains on the tool.
- `Program.RunMcp` calls `ToMcpServerTool()` for each tool in alphabetical
  name order, for the same reason source files list members alphabetically
  — a reader can find a registration without scanning the whole block. This
  is a source-code convention only: the SDK's `McpServerPrimitiveCollection`
  does not preserve that order in the advertised `tools/list` (confirmed by
  running it, not assumed — see the subprocess tests), and this design does
  not depend on the wire order matching registration order. What the tests
  do assert is that `tools/list` is stable across repeated calls within one
  process and lists every registered tool exactly once. The server
  advertises the tools capability without `listChanged`, because the set
  cannot change during the process lifetime.

### Decision traceability

| Implementation decision | Governing requirements |
| --- | --- |
| Reuse `GedReader` and `ModelBuilder` | 1 |
| Stdio only; no GedFire network client or telemetry | 2 |
| One startup-bound document; no path in tool arguments | 2, 7, 8 |
| Name-first lookup with explicit ambiguity | 3 |
| Weighted evidence normalized by available weight; no hard-blocking fields | 3, 8 |
| Embedded nickname dictionary; fixed override below exact-match score | 3 |
| `outputSchema` plus `structuredContent` | 4, 6, 9 |
| Read-only snapshot discipline and truthful annotations | 5 |
| `find_person` performs lookup, not full record retrieval | 7, 8 |
| `get_document_stats` takes no arguments and reports only snapshot facts already in hand | 2, 4, 5, 6, 7, 8 |
| `get_record` resolves person/family/source by xref with shallow references only, no name fallback | 1, 4, 5, 6, 7, 8, 10 |
| A resolved person includes immediate family xrefs with bounded spouse-family context | 3, 4, 8, 10 |
| Official SDK, named protocol revisions, compatibility tests | 9 |

### Implementation map: classes and responsibilities

All new code lives in the `GedFire` project, following the existing
folder-per-namespace convention (`GedFire.Gen`, `GedFire.Export`,
`GedFire.TargetSelection`): matching classes in a new `GedFire/Match`
folder (namespace `GedFire.Match`), server classes in a new `GedFire/Mcp`
folder (namespace `GedFire.Mcp`). Nothing moves to `GedCore`. Each class
has one job; a class listed here must not absorb a neighbor's.

| Class | Location | Responsibility |
| --- | --- | --- |
| `Program.RunMcp` | `GedFire/Program.cs` | New switch case. Parse arguments with `CommandLine.Parse`, resolve `--input` to an absolute path, perform the initial load by constructing `DocumentSession`, register every tool with the SDK stdio server (source listed alphabetically by name; the SDK does not preserve that as the advertised `tools/list` order), await it, return the exit code. No domain logic, no protocol logic. |
| `DocumentSession` | `GedFire/Mcp/DocumentSession.cs` | Owns the current `DocumentSnapshot` reference and the source path. Implements the entire reload policy: mtime+length staleness check, one per-document async lock, before/after metadata capture with the single retry, atomic reference swap, actionable errors. The only code that ever replaces the snapshot. Exposes one method: get the current (reloading if stale) snapshot. |
| `DocumentSnapshot` | `GedFire/Mcp/DocumentSnapshot.cs` | A construct-once carrier: the `GedModel`, the `MatchIndex` built from it, the document's declared GEDCOM version (`GedDocument.Version`, captured once so `get_document_stats` never re-parses either), and the source file's UTC last-write time and byte length. No behavior beyond construction. |
| `ToolGate` | `GedFire/Mcp/ToolGate.cs` | The two admission bounds: a `SemaphoreSlim(4)` for concurrency and the 120-per-minute sliding window. Wraps every tool invocation; returns the retryable tool execution error on rate rejection. Knows nothing about any specific tool. |
| `FindPersonTool` | `GedFire/Mcp/FindPersonTool.cs` | The MCP handler: declares the tool metadata, schemas, and annotations from this document; trims and validates the query; obtains the snapshot from `DocumentSession`; calls `PersonMatcher`; maps the `MatchOutcome` to the result records. The last-chance `Exception` handler lives at this boundary. No matching or scoring logic. |
| `FindPersonResults` | `GedFire/Mcp/FindPersonResults.cs` | Sealed records mirroring the output schema property-for-property (`SingleMatchResult`, `CandidateListResult`, `NoMatchResult`, and the identity records). Serialized with `System.Text.Json`, camelCase names, null properties emitted (never ignored), no indentation in the text block. |
| `GetDocumentStatsTool` | `GedFire/Mcp/GetDocumentStatsTool.cs` | The MCP handler for `get_document_stats`: declares the tool metadata and schemas from that addendum; obtains the snapshot from `DocumentSession`; reads `personCount`/`familyCount`/`gedVersion` straight off it. No arguments to validate. Same last-chance `Exception` handler pattern as `FindPersonTool`. |
| `DocumentStatsResult` | `GedFire/Mcp/DocumentStatsResult.cs` | The one sealed record mirroring `get_document_stats`'s flat output schema. Same serialization conventions as `FindPersonResults`. |
| `GetRecordTool` | `GedFire/Mcp/GetRecordTool.cs` | The MCP handler for `get_record`: declares the tool metadata and schemas from that addendum; trims and validates `xref`; obtains the snapshot from `DocumentSession`; looks the xref up across `Individuals`/`Families`/`Sources` and maps whichever resolves. Takes the resolved media directory (always the GEDCOM's own directory — see the post-4.0.5 addendum above; no longer independently configurable) by constructor alongside `DocumentSession`/`ToolGate`, and resolves each `MediaFile.path` against it (`MediaPaths`, the same helpers `SiteGenerator.ResolveMediaSrc` uses). No matching or scoring logic — there is none to have, only a dictionary lookup. |
| `GetRecordResults` | `GedFire/Mcp/GetRecordResults.cs` | Sealed records mirroring `get_record`'s output schema property-for-property (`PersonRecord`, `FamilyRecord`, `SourceRecord`, `NotFoundRecord`, and the shared identity/detail records). Same serialization conventions as `FindPersonResults`. |
| `PersonMatcher` | `GedFire/Match/PersonMatcher.cs` | The whole algorithm in "Matching and ranking": query splitting, recall gate, evidence weights, availability normalization, classification thresholds, ordering, caps, and suggestions. Constructed with a `NicknameDirectory`; takes a `MatchIndex`, query, and hints; returns a `MatchOutcome`. Pure and deterministic — no I/O, no MCP types, no JSON. |
| `MatchOutcome` | `GedFire/Match/MatchOutcome.cs` | The matcher's domain result: which of the three shapes, the ordered matched people with their family data, the truncated flag, suggestions with reasons. References model objects and xrefs, not DTOs. |
| `MatchHints` | `GedFire/Match/MatchHints.cs` | The plain domain record `PersonMatcher.Match` takes for the four optional hints (`BirthYear`, `Place`, `SpouseName`, `ParentName`). Not an MCP type — `FindPersonTool` maps its own wire-shape `FindPersonHintsArgs` to this before calling the matcher, keeping JSON binding concerns out of `GedFire.Match` entirely. |
| `MatchIndex` | `GedFire/Match/MatchIndex.cs` | Built once per snapshot from a `GedModel`: for every individual, the normalized surname and given name, normalized spouse and parent full names, birth year, sex (see `GedIndividual.SexRecorded` below), and normalized birth/death/census places. Exists so no tool call re-normalizes the tree. |
| `PersonNameNormalizer` | `GedFire/Match/PersonNameNormalizer.cs` | The normalization rules in "Shared normalization", extracted from `PersonIndexExporter.Normalize`; the exporter delegates to it. Static, one public method. |
| `JaroWinkler` | `GedFire/Match/JaroWinkler.cs` | The similarity routine in "Name similarity". Static, one public method, no dependencies. |
| `NicknameDirectory` | `GedFire/Match/NicknameDirectory.cs` | Loads the embedded `nicknames.json` once, normalizes entries, answers one question: are two given names documented equivalents, given the candidate's recorded sex (union of maps when unrecorded). |
| `PersonDisplay` | `GedFire/Match/PersonDisplay.cs` | One static method, `FullName(GedIndividual)`: the canonical "First Middle LastName" display string, matching `PersonIndexExporter`'s existing `name` field convention. Shared by `MatchIndex` (spouse/parent name comparisons) and `FindPersonTool`'s result mapping (person/spouse/parent name fields) so both draw from one definition of "this person's name" instead of duplicating the join (requirement 1). Not named in the original implementation map; added during implementation once the duplication became concrete. |

This implementation map undersold one thing: `GedModel` does gain one new member after all. `GedIndividual.IsMale` cannot distinguish "recorded female" from "sex never stated" — both leave it `false` — and `NicknameDirectory`'s sex-map selection needs that distinction to consult the right map (or both, when genuinely unrecorded). `GedIndividual.SexRecorded` (a plain `bool`, set alongside `IsMale` in `ModelBuilder.ParseIndi`'s `SEX` case) closes that gap. It is additive and non-breaking — nothing but `MatchIndex` reads it — so it was made directly rather than treated as a blocking design question.

Construction order in `RunMcp`: load the nickname directory, build the
first `DocumentSnapshot` (any failure here is the startup-error path),
construct `DocumentSession`, `ToolGate`, and the tool instances, then hand
the tools to the SDK. Tools receive `DocumentSession` and `ToolGate` by
constructor; nothing uses a service locator or static mutable state.

The output schema is one handwritten JSON document kept as a string
constant beside `FindPersonTool` and declared to the SDK verbatim — it is
the contract, and the records conform to it, not the reverse. A unit test
serializes every result shape and validates it against that schema, which
is what keeps the two from drifting.


## Tool granularity policy (governs this and every later tool)

Per requirement 7: a tool's scope is one complete, natural unit a person
would recognize as "one thing" — a person, a family, a name lookup — never
narrower (one fact type) and never wider (a generic dispatcher keyed by an
`operation` argument). The test for a new tool proposal: can its whole
argument shape be described in one JSON Schema without an internal
sub-schema keyed by a string enum? If not, it is really several tools
wearing one name, and should be split.

## The first tool: `find_person`

### Why this one first

Every other people-facing tool (`get_person`, `get_family`, ancestor and
descendant traversal, relationship-path finding) needs an xref as its
starting point. Nothing today produces one from what a person actually
says in conversation. `find_person` is the front door every later tool
walks through, which is why it is built — and specified — before any of
them.

### Requirement recap specific to this tool

- Accepts a name and optional incidental detail, never requires an xref.
- Never blocks waiting for a human answer inside the call. It returns
  data; the calling agent decides, from that data, whether to proceed or
  to ask a clarifying question in ordinary conversation.
- Reuses the project's existing normalization rule rather than copying it.
  Implementation extracts the private `PersonIndexExporter.Normalize`
  routine into a public `PersonNameNormalizer` used by both export and
  lookup (requirement 1). `PersonNameNormalizer` and the matching engine
  live in the `GedFire` project beside `GedModel`, which they depend on;
  they do not move to `GedCore`.
- Returns only identity and disambiguation fields. Detailed facts, notes,
  media, and citations belong to the later `get_person` tool (requirements
  7 and 8).
- A resolved match includes the xref of every local family in which the
  person is a child or a spouse, childless marriages included.
  Spouse-family entries add only marriage date
  and spouse name; they do not embed children or other family details
  (requirements 8 and 10).
- Returns exact, structured fields — never a formatted sentence.

### Input

```json
{
  "name": "find_person",
  "description": "Resolve a name the user mentioned to a person in this server's GEDCOM. Call this whenever a person is known by name but not by xref. A single result includes the person's identity, their child-family xref, and the xref of every marriage — childless marriages included — with marriage date and spouse name; pass those xrefs to future family detail or research tools when needed. When candidates are returned, ask the user which person they mean and call again with any new birth, place, spouse, or parent hint.",
  "inputSchema": {
    "type": "object",
    "additionalProperties": false,
    "properties": {
      "query": {
        "type": "string",
        "minLength": 1,
        "description": "The name as the user said it: a full name, given name, shortened prefix such as Fred for Frederick, a documented nickname such as Bill for William, or a close spelling. Pass it unchanged; the tool normalizes it."
      },
      "hints": {
        "type": "object",
        "additionalProperties": false,
        "description": "Any incidental detail the user mentioned in the same breath, used only to rank and narrow candidates. All fields optional; omit any not mentioned.",
        "properties": {
          "birthYear": {
            "type": "integer",
            "minimum": 1,
            "maximum": 9999,
            "description": "An approximate or exact birth year, if mentioned."
          },
          "place": {
            "type": "string",
            "minLength": 1,
            "description": "A place associated with the person (birth, death, residence), as free text."
          },
          "spouseName": {
            "type": "string",
            "minLength": 1,
            "description": "A spouse's name, if the user mentioned one."
          },
          "parentName": {
            "type": "string",
            "minLength": 1,
            "description": "A parent's name, if the user mentioned one."
          }
        }
      }
    },
    "required": ["query"]
  },
  "annotations": {
    "readOnlyHint": true,
    "destructiveHint": false,
    "idempotentHint": true
  }
}
```

JSON Schema's `minLength` does not reject a string containing only spaces, so
the implementation also trims solely for validation and returns a tool
execution error for a whitespace-only query. It does not alter the query
before passing it to `PersonNameNormalizer`.

### Output: three shapes, one schema

The response always has a `matchType` field naming which of the three
shapes follows. This is deliberate: an agent should not have to infer from
array length whether it got one confident answer or a disambiguation list.
The following object is the tool's `outputSchema`; it is part of the
`tools/list` declaration, not merely documentation (requirements 4 and 6).

```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "type": "object",
  "oneOf": [
    { "$ref": "#/$defs/SingleMatch" },
    { "$ref": "#/$defs/CandidateList" },
    { "$ref": "#/$defs/NoMatch" }
  ],
  "$defs": {
    "EventIdentity": {
      "type": ["object", "null"],
      "additionalProperties": false,
      "properties": {
        "date": { "type": ["string", "null"] },
        "year": { "type": ["integer", "null"] },
        "qualifier": { "type": ["string", "null"] },
        "place": { "type": ["string", "null"] }
      },
      "required": ["date", "year", "qualifier", "place"]
    },
    "ParentsIdentity": {
      "type": ["object", "null"],
      "additionalProperties": false,
      "properties": {
        "father": { "type": ["string", "null"] },
        "mother": { "type": ["string", "null"] }
      },
      "required": ["father", "mother"]
    },
    "CandidateIdentity": {
      "type": "object",
      "additionalProperties": false,
      "properties": {
        "xref": { "type": "string", "pattern": "^@[^@]+@$" },
        "name": { "type": "string" },
        "birth": { "$ref": "#/$defs/EventIdentity" },
        "death": { "$ref": "#/$defs/EventIdentity" },
        "parents": { "$ref": "#/$defs/ParentsIdentity" },
        "spouses": { "type": "array", "items": { "type": "string" } }
      },
      "required": ["xref", "name", "birth", "death", "parents", "spouses"]
    },
    "SpouseFamilyIdentity": {
      "type": "object",
      "additionalProperties": false,
      "properties": {
        "xref": { "type": "string", "pattern": "^@[^@]+@$" },
        "marriageDate": { "type": ["string", "null"] },
        "spouseName": { "type": ["string", "null"] }
      },
      "required": ["xref", "marriageDate", "spouseName"]
    },
    "FamiliesIdentity": {
      "type": "object",
      "additionalProperties": false,
      "properties": {
        "asChild": {
          "type": "array",
          "items": { "type": "string", "pattern": "^@[^@]+@$" }
        },
        "asParent": {
          "type": "array",
          "items": { "$ref": "#/$defs/SpouseFamilyIdentity" }
        }
      },
      "required": ["asChild", "asParent"]
    },
    "ResolvedPersonIdentity": {
      "type": "object",
      "additionalProperties": false,
      "properties": {
        "xref": { "type": "string", "pattern": "^@[^@]+@$" },
        "name": { "type": "string" },
        "birth": { "$ref": "#/$defs/EventIdentity" },
        "death": { "$ref": "#/$defs/EventIdentity" },
        "families": { "$ref": "#/$defs/FamiliesIdentity" }
      },
      "required": ["xref", "name", "birth", "death", "families"]
    },
    "Suggestion": {
      "type": "object",
      "additionalProperties": false,
      "properties": {
        "xref": { "type": "string", "pattern": "^@[^@]+@$" },
        "name": { "type": "string" },
        "reason": {
          "type": "string",
          "enum": ["close spelling", "partial name"]
        }
      },
      "required": ["xref", "name", "reason"]
    },
    "SingleMatch": {
      "type": "object",
      "additionalProperties": false,
      "properties": {
        "matchType": { "const": "single" },
        "person": { "$ref": "#/$defs/ResolvedPersonIdentity" }
      },
      "required": ["matchType", "person"]
    },
    "CandidateList": {
      "type": "object",
      "additionalProperties": false,
      "properties": {
        "matchType": { "const": "candidates" },
        "candidates": {
          "type": "array",
          "minItems": 2,
          "maxItems": 8,
          "items": { "$ref": "#/$defs/CandidateIdentity" }
        },
        "truncated": { "type": "boolean" }
      },
      "required": ["matchType", "candidates", "truncated"]
    },
    "NoMatch": {
      "type": "object",
      "additionalProperties": false,
      "properties": {
        "matchType": { "const": "none" },
        "suggestions": {
          "type": "array",
          "maxItems": 3,
          "items": { "$ref": "#/$defs/Suggestion" }
        }
      },
      "required": ["matchType", "suggestions"]
    }
  }
}
```

Nullable identity fields are present as JSON `null` rather than omitted, so
clients do not have to distinguish “unknown” from “serializer happened to
omit it.” Lists are present and empty when there are no values.

The top-level `oneOf` with `$defs` and `$ref` is valid under the current
protocol revision, where `structuredContent` may be any JSON value; every
branch is itself an object, so handshake-era clients always receive a plain
JSON object. Compatibility tests must show that clients of both target eras
accept this schema shape from `tools/list` and validate a result against it.
If the selected SDK or a named client cannot, the fallback is one envelope
object — `matchType` plus three optional properties — adopted as a design
update to this document, not as an ad hoc change during implementation.

The top-level `"type": "object"` alongside `oneOf` is required, not
decorative: the selected SDK (`ModelContextProtocol` 2.0.0) auto-wraps any
declared `outputSchema` that lacks an explicit top-level object type in a
synthetic `{"type":"object","properties":{"result":...}}` envelope before
advertising it, which would silently mismatch the bare shape `find_person`
actually returns in `structuredContent`. Every branch already satisfies
`"type": "object"`; stating it once at the root is what stops the SDK from
adding one on its own.

#### `SingleMatch` — one strong match found

Returned when exactly one person clears the confidence bar (see
"Matching and ranking" below). The result contains enough context to verify
the person's identity, their child-family xref, and a compact identity for
each of their marriages. It intentionally excludes children,
other family facts, notes, media, and citations. A later `get_person` or
`get_family` call is needed only when the user's actual question asks for
those details.

```json
{
  "matchType": "single",
  "person": {
    "xref": "@I1234@",
    "name": "Frederick A. Morrill",
    "birth": { "date": "12 MAR 1841", "year": 1841, "qualifier": null, "place": "Gorham, Maine" },
    "death": { "date": "3 JUN 1919", "year": 1919, "qualifier": null, "place": "Portland, Maine" },
    "families": {
      "asChild": ["@F200@"],
      "asParent": [
        {
          "xref": "@F300@",
          "marriageDate": "9 SEP 1863",
          "spouseName": "Sarah J. Blake"
        }
      ]
    }
  }
}
```

The object above is placed in `structuredContent`. The SDK also places its
compact JSON serialization in one text content block and sets `isError` to
false. The SDK wraps those fields in the response shape required by the
negotiated protocol revision; GedFire does not hand-build that envelope.

#### `CandidateList` — more than one plausible match

Returned when two or more people clear the *lower* recall bar but no
single one clears the confidence bar alone. Each candidate carries just
enough to let a human recognize themselves in it. Family xrefs are returned
only after one person is resolved; they would not help distinguish the N-1
candidates the person did not mean.

```json
{
  "matchType": "candidates",
  "candidates": [
    {
      "xref": "@I1234@",
      "name": "Frederick A. Morrill",
      "birth": { "date": "12 MAR 1841", "year": 1841, "qualifier": null, "place": "Gorham, Maine" },
      "death": { "date": "3 JUN 1919", "year": 1919, "qualifier": null, "place": "Portland, Maine" },
      "parents": { "father": "Ansel Morrill", "mother": "Mary Peaslee" },
      "spouses": ["Sarah J. Blake"]
    },
    {
      "xref": "@I5678@",
      "name": "Frederick W. Morrill",
      "birth": { "date": "ABT 1868", "year": 1868, "qualifier": "ABT", "place": "Lewiston, Maine" },
      "death": null,
      "parents": { "father": "Charles Morrill", "mother": "Ellen Sawyer" },
      "spouses": []
    }
  ],
  "truncated": false
}
```

`truncated: true` signals the candidate set was capped (see limits below)
and narrower hints would help — the agent should say so rather than
present a partial list as if it were complete.

### Family xref semantics

The family fields are handoff identifiers with only enough spouse-family
context to distinguish multiple marriages (requirements 8 and 10).

- `families.asChild` is empty when `GedIndividual.FamChild` is null; otherwise
  it contains that family's xref.
- `families.asParent` contains one object for every family in
  `GedIndividual.FamSpouse`, preserving model order. Childless marriages are
  included: a marriage with no children is exactly the case where the date and
  spouse name are needed to tell marriages apart, and excluding it would leave
  no handoff xref for researching that family at all. `xref` is the family
  handoff identifier; `marriageDate` is the exact raw
  `GedFamily.Marriage.Date`, or null when the marriage event or date is
  absent; and `spouseName` is the canonical display name of
  `GedFamily.SpouseOf(person)`, or null when no spouse resolves.
- `CandidateIdentity.spouses` draws from the same source: the canonical
  display name of `GedFamily.SpouseOf(person)` for every family in
  `FamSpouse`, in model order, skipping families where no spouse resolves.
  The two fields therefore always describe the same set of marriages.
- No child, parent, sibling, other family event, pedigree classification, or
  provider identity is included. A future family-details or research tool
  accepts an xref and returns the data appropriate to that request.
- Every xref is local to the server's bound GEDCOM. A future source API
  returns provider identities in separate provider-specific fields and never
  places one in an `xref` field or silently treats it as equivalent.

#### `NoMatch` — nothing found

Returned when nothing clears even the recall bar. Includes near-miss
suggestions from the same normalized-name space, so a typo or an
unfamiliar spelling doesn't read as "this person isn't in the tree at all"
when they are, under a different spelling.

```json
{
  "matchType": "none",
  "suggestions": [
     { "xref": "@I9012@", "name": "Frederick A. Morrell", "reason": "close spelling" }
  ]
}
```

### Matching and ranking

  Identity resolution adapts the weighted-evidence model of Fellegi–Sunter
  record linkage — the algorithm family behind master-patient-index (MPI)
  matching in healthcare — to genealogy lookup. Every field contributes a
  weighted similarity, the total is normalized by the weight of the evidence
  that was actually available to compare, and fixed thresholds classify the
  normalized score. The implementation is pure C#: a public `PersonMatcher`
  class and a public `JaroWinkler` similarity routine in the `GedFire`
  project, with no external dependency, deterministic for a given snapshot
  and query. Two properties are non-negotiable: hints can rank a plausible
  name but can never admit an unrelated name into the recall set
  (requirement 3), and no field is hard-blocking — not even birth year.
  Genealogical records are imperfect; a mistranscribed date or a moved
  family must lower a candidate's rank, never remove the right person from
  the results. There are no hard exclusions of any kind.

  #### Shared normalization

  `PersonNameNormalizer.Normalize` changes one rule from the private
  routine it replaces: a hyphen is kept as a literal character within a
  name rather than collapsed to a token boundary, so a hyphenated surname
  stays one token instead of splitting into two. This is a deliberate
  behavior change, not a preservation — it is what makes the query-splitting
  rule below work for hyphenated surnames — and it changes
  `PersonIndexExporter`'s own `surname`/`given` output for any hyphenated
  name: `O'Brien-Smith` now normalizes to `OBRIEN-SMITH`, not
  `OBRIEN SMITH`. `PersonIndexExporterTests.Names_AreNormalizedForMatching`
  asserts the old `"OBRIEN SMITH"` value and must be updated to
  `"OBRIEN-SMITH"` as part of this work; that is an intended output change
  to a currently-tested behavior, not a regression to guard against.

  1. Keep Unicode letters, Unicode digits, `_`, and `-`; uppercase letters
     invariantly.
  2. Convert spaces to a token boundary. A hyphen is never a boundary.
  3. Remove all other punctuation.
  4. Collapse repeated space boundaries and trim them; collapse repeated
     hyphens to one and trim a token's leading or trailing hyphens (a
     hyphen carries no meaning at a token edge).

  The exporter's normalized `given` and `surname` fields and lookup's query,
  person, place, spouse, and parent comparisons all call this one public
  routine. A candidate's compared fields are `Normalize(LastName)` (surname)
  and `Normalize(FirstMiddle())` (given). Alternate `NAME` structures are
  not represented by today's `GedModel` and are explicitly deferred rather
  than guessed.

  #### Name similarity

  `JaroWinkler.Similarity` returns 0.0–1.0: standard Jaro similarity with
  the Winkler prefix bonus (scaling factor 0.1, common prefix capped at
  four characters), computed on normalized strings, symmetric, and 0.0 when
  either input is empty. The prefix bonus is what makes a shortened form
  score high without any lookup table: `FRED` vs `FREDERICK` scores about
  0.89. `BILL` vs `WILLIAM` scores only about 0.73 — documented nicknames
  are not a spelling phenomenon, so they are handled by the dictionary
  below, not by similarity.

  #### Nickname dictionary

  The researcher's genealogy nickname reference data —
  `.claude/skills/genealogy-identity-correlation/references/nicknames.json`
  in the maintainer's private `morrillonline` repository — has been copied
  verbatim into this solution as `GedFire/Resources/nicknames.json` (196
  `male` groups, 164 `female`, byte-identical to the source). It contains
  only historical given-name conventions, no family data. The file in this
  repository is now the authoritative copy; implementation still needs to
  mark it `<EmbeddedResource>` in `GedFire.csproj` (the same mechanism
  `GedCore.Tests` uses for changeset fixtures) so the shipped tool has no
  loose data file to locate or lose. Each map is keyed by a formal name
  whose list contains that formal name and its documented equivalents.

  A public `NicknameDirectory` class loads the resource once at startup,
  normalizing every entry through `PersonNameNormalizer` so lookups share
  the exporter's rules. Two given names are *documented equivalents* when
  any group's list contains both — the lists include the formal name, so
  membership covers formal↔nickname and nickname↔nickname (`PEGGY` and
  `MEG` via `MARGARET`) without special cases. The candidate's recorded sex
  selects the map; when sex is unrecorded, both maps are consulted. A few
  group labels are naming conventions rather than names (suffix-derived
  groups such as `trey`); they are loaded as-is and participate only if a
  user actually says them, which is harmless.

  Equivalence is checked between the first given-name token of the query
  and the first given-name token of the candidate; remaining given tokens
  still contribute through similarity. Following the MPI pattern, a
  documented equivalence overrides a weak similarity with a fixed strong
  score — 20 of the 25 given-name weight — so a nickname is decisive
  evidence but never outranks an exact or near-exact spelling. Nickname
  equivalence applies only to the query's given-name comparison; spouse and
  parent hint comparisons use similarity alone in this release.

  The query splits on normalized token boundaries — spaces only, since a
  hyphen is preserved inside a token by normalization rather than treated
  as one. With two or more space-separated tokens, the last token is
  compared as the surname and the remaining tokens, joined by one space,
  as the given name; a hyphenated surname such as `Smith-Jones` now
  survives as that one last token intact instead of being torn into two.
  With one token, the query is compared against both fields and the better
  single-field result stands alone, with only that field's weight
  available — a one-token query therefore recalls broadly (every strong
  surname match) and relies on the candidate cap, hints, or a follow-up
  call to narrow.

  A surname that is itself multiple space-separated words with no hyphen
  ("Van Der Berg", "De La Cruz") is not solved by this rule: the
  last-token heuristic still takes only "Berg" or "Cruz" as the surname.
  This is the same class of gap as the deferred alternate `NAME`
  structures above, and for the same reason — `GedModel` has no
  structured given/surname split to fall back on, and guessing where the
  boundary falls without one would trade a known limitation for a silent
  wrong answer. It is not fixed in this release.

  #### Evidence weights

  | Weight | Evidence | Counts toward available weight when |
  | ---: | --- | --- |
  | 35 | Surname: similarity × 35. | Always (two-plus-token query). |
  | 25 | Given name: the greater of similarity × 25 and, when the query and candidate given names are documented nickname equivalents, a fixed 20. | Always (two-plus-token query). |
  | 15 | Birth year: 15, 10, or 5 points when the hint differs from the candidate's birth year by 0, 1, or 2 years; 0 beyond that. | Hint supplied and the candidate has a birth year. |
  | 15 | Place: 15 points when the normalized place hint is a substring of, or contains, a non-empty normalized birth, death, or census place. | Hint supplied and the candidate has at least one such place. |
  | 20 | Spouse name: 20 points when the normalized hint scores at least 0.85 against any spouse's normalized full name. | Hint supplied and the candidate has at least one resolvable spouse name. |
  | 20 | Parent name: 20 points when the normalized hint scores at least 0.85 against either parent's normalized full name. | Hint supplied and the candidate has at least one parent name. |

  `AvailableWeight` is the sum of the weights whose condition in the last
  column holds: 60 for a plain two-token name query (35 or 25 for a
  one-token query), up to 130 with every hint present and comparable. The
  final score is `raw × 100 / AvailableWeight`. A hint the user never gave,
  or a datum the tree never recorded, neither penalizes nor rewards —
  missing data is not evidence. This availability normalization is the
  heart of the MPI model and replaces additive bonus points: the score
  always reads as "percent of the comparable evidence that matched."

  #### Recall gate, classification, and ordering

  1. The recall gate uses names alone: a candidate enters the recall set
    when its name-only normalized score (name points × 100 / name weight)
    is at least 70. Hints are scored only for recall candidates, so they
    can reorder the set but never expand it (requirement 3).
  2. No recall candidates produces `NoMatch`. Suggestions are at most the
    three best candidates whose name-only score is 55–69. Reason is
    `close spelling` when the decisive field's similarity is at least
    0.85, otherwise `partial name`. For a two-plus-token query the
    decisive field is always the surname. For a one-token query, which
    never has a separate surname comparison, the decisive field is
    whichever single field — given or surname — that query was actually
    compared against under the one-token rule above; using the same
    field that produced the candidate's name-only score keeps the
    classification meaningful instead of undefined.
  3. Exactly one recall candidate produces `SingleMatch`.
  4. With two or more, the top candidate produces `SingleMatch` only when
    its final score is at least 90 and leads the runner-up by at least 10
    points. Otherwise the result is `CandidateList`.
  5. Ordering is final score descending, then raw score descending (more
    actually-matched evidence outranks an equal score built on less), then
    name-only score descending, then display name ordinally, then xref
    ordinally. Scores are IEEE-754 doubles computed in one fixed evaluation
    order, so output is byte-stable for an unchanged model.

  One matched relational hint breaks an exact-name tie when both candidates
  have that relation recorded: the candidate whose spouse matches scores
  100 while the mismatch drops to 75, clearing the 10-point margin. When
  one candidate simply lacks the datum, the two can tie at 100; the margin
  rule then returns `CandidateList` rather than guessing. That is
  deliberate conservatism: "no spouse recorded" is not evidence that the
  spouse hint is wrong, and it is not evidence that it is right, either.

### Limits

- The candidate list is capped at 8. Beyond that, `truncated:
  true` is set and the response asks for a narrower hint rather than
  dumping an unfilterable list on the conversation.
- Suggestions are capped at 3. `find_person` has no result-size setting; the
  fixed limits are part of its public contract and output schema.
- The family arrays are not capped or truncated. Each spouse-family object is
  fixed at three fields, so payload size is bounded by the resolved person's
  own marriage count rather than connected-family size.
- `find_person` never traverses relationships or resolves a second person
  from a `hints` name (e.g. `spouseName`) — it only matches that string
  against the candidate's own indexed family fields. Resolving
  "the person whose spouse is named X" as its own lookup is future scope,
  not this tool's job.

### Errors

- Missing `--input`, a missing input file, or an initial parse failure is a
  CLI startup error: write a specific diagnostic to stderr, write nothing to
  stdout, and exit 1.
- A malformed `tools/call` or arguments that fail `inputSchema` are protocol
  errors produced by the SDK.
- A whitespace-only query, a cancelled reload, or a changed file that cannot
  be read or parsed is a tool execution error. Return `isError: true` with one
  actionable text content block and no `structuredContent`.
- Every tool body runs inside a last-chance handler that catches
  `Exception`. An unexpected exception never kills the server and never
  goes unanswered: the call returns a tool execution error — `isError:
  true` with one text content block containing the exception type, its
  message, and the full call stack — and the server keeps serving
  subsequent calls. The stack trace is deliberately included; this is a
  local tool run by the researcher against their own data, and a stack
  the agent can read and relay is worth more than a sanitized apology.
  The same detail is also written to stderr.
- An exception that escapes the SDK's run loop itself (not a tool body) is
  written to stderr with its full stack and the process exits nonzero. A
  stdio server dies loudly rather than limping with corrupted state; the
  client reports the dead process and the stderr text.

## The second tool: `get_document_stats`

### Why this one, and why this small

Before an agent does anything else with a bound document, it is often worth
a quick orientation: how big is this file, and what format is it in. That
is a different, and much smaller, job than `find_person` — no name to
resolve, no ambiguity to report, no arguments at all.

The field list was deliberately cut down from a larger candidate set
(source count, media count, earliest/latest recorded year, distinct
surname count — see the design conversation this addendum comes from) to
exactly three: `personCount`, `familyCount`, `gedVersion`. Requirement 8
("return no more than the task needs") applies to tool scope, not only to
a single response's field list — a stats tool that tries to anticipate
every question up front stops being "one complete, natural unit" (the
granularity policy above) and starts being a dashboard. The larger set
remains available for a later addendum if it turns out to be wanted; cutting
it now is a scope decision, not an oversight.

### Requirement recap specific to this tool

- No arguments, because there is nothing to disambiguate: the tool reports
  on the one document this server is bound to (requirements 2, 7, 8).
- Structured, fully-described output, same as `find_person` (requirements
  4, 6).
- Read-only, same annotation profile as `find_person` (requirement 5).
- Reuses data the resident snapshot already holds — `personCount` and
  `familyCount` are direct counts off the `GedModel` already built for
  `find_person`; `gedVersion` reuses the same `HEAD.GEDC.VERS` lookup
  `ConformanceChecker`'s GED011 check already performs, now exposed as
  `GedDocument.Version` in `GedCore` rather than re-implemented (requirement
  1). No tool call re-parses or re-walks the tree.

### Input

```json
{
  "name": "get_document_stats",
  "description": "Report basic size and format facts about this server's bound GEDCOM document: how many people and families it contains, and its declared GEDCOM version. Call this for a quick orientation before other work, or when the user asks how large their file is or what format it's in. Takes no arguments — there is only one bound document.",
  "inputSchema": {
    "type": "object",
    "additionalProperties": false,
    "properties": {},
    "required": []
  },
  "annotations": {
    "readOnlyHint": true,
    "destructiveHint": false,
    "idempotentHint": true
  }
}
```

### Output

```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "type": "object",
  "additionalProperties": false,
  "properties": {
    "personCount": {
      "type": "integer",
      "minimum": 0,
      "description": "Total number of individual (INDI) records in the document."
    },
    "familyCount": {
      "type": "integer",
      "minimum": 0,
      "description": "Total number of family (FAM) records in the document."
    },
    "gedVersion": {
      "type": ["string", "null"],
      "description": "The document's declared GEDCOM version (HEAD.GEDC.VERS, e.g. \"7.0\" or \"5.5.1\"), or null if the header does not declare one."
    }
  },
  "required": ["personCount", "familyCount", "gedVersion"]
}
```

A flat object, not a `oneOf` — there is only one shape, because there is
nothing to disambiguate and nothing that can fail short of the shared
reload/exception paths below. `gedVersion` is nullable rather than assumed
present: GEDCOM requires a header, but this codebase already treats a
missing or malformed one as data to report rather than a reason to fail
(`ConformanceChecker`'s own `vers is null` handling), and a stats tool
should be at least as tolerant as the validator.

```json
{ "personCount": 4218, "familyCount": 1533, "gedVersion": "7.0" }
```

### Errors and limits

No user-supplied input exists to validate, so this tool inherits exactly
the shared paths already specified for `find_person` and nothing new: a
reload failure (missing, unstable, or unparsable source file) is a tool
execution error, and the last-chance `Exception` handler covers everything
else. There is no result-size limit to specify — the shape is always
exactly these three fields.

## The third tool: `get_record`

### Why this one, and why one tool for three record types

`find_person` deliberately withholds almost everything about a person —
children, notes, media, citations, every family fact beyond the bare
minimum needed to tell two candidates apart (requirements 7 and 8). That
was not an oversight; it was designed so a later tool would answer "tell
me everything about this specific record" completely, once the person is
no longer ambiguous. This is that tool.

It resolves three record types — person, family, and source — through one
argument, because a GEDCOM xref is unique across the entire document
regardless of record type (`GedDocument`/`GedModel` never assign the same
xref to an individual and a family). The tool looks up whichever of
`GedModel.Individuals`, `.Families`, or `.Sources` the xref belongs to and
returns that record's full detail. This is not the "generic dispatcher"
the granularity policy warns against: the *input* shape is one field with
no operation string or type enum to key off, exactly the test that policy
sets ("can its whole argument shape be described in one JSON Schema
without an internal sub-schema keyed by a string enum?"). The branching
happens on the *output*, the same pattern `find_person`'s own three-shape
`oneOf` already establishes as acceptable — this tool has four output
shapes instead of three for the same reason: a resolved question can turn
out one of several ways without the question itself being ambiguous.

Splitting this into three separate tools (`get_person`, `get_family`,
`get_source`) was considered and rejected: a caller holding an xref from a
`find_person` result or another `get_record` call has no reason to already
know which of the three it names, and forcing it to pick the right tool
name would just move the branching from this tool's output to the
caller's tool-selection logic — no simpler, and one more failure mode
(picking the wrong one) added for nothing.

### Requirement recap specific to this tool, including one apparent tension

- Requirement 3 says a human never needs to know an internal id "to start
  a conversation" and that no tool may require an xref "as the only way
  in." `get_record` *only* takes an xref, with no name-based fallback —
  read carelessly, that looks like a violation. It is not: requirement 3
  governs the conversation's entry point, which is `find_person`. Nothing
  about a person's name is knowable once the conversation is already
  holding a specific family's or source's xref — a source has no "name" to
  fall back to at all. Requirement 10 is the one that actually governs
  this tool: it exists specifically so "later detail or research tools"
  have something to accept, and `get_record` is that tool. A name-based
  fallback here would not satisfy requirement 3 more thoroughly; it would
  duplicate `find_person`'s job inside a second tool, which requirement 7
  forbids outright.
- Requirement 1: reuses `GedModel`'s existing dictionaries (no new lookup
  structure), `PersonDisplay.FullName` for every display name, `GedDate`
  for date parsing, and `FtmCitationText.ParseSourceNote` for the one
  piece of text that needs it — the source record's bibliographic note,
  the exact cleanup `ModelBuilder`'s own pointer resolution already
  performs on the same field.
- Requirements 4, 5, 6: structured output, read-only, fully described —
  same as every tool so far.
- Requirement 8: every shallow-reference field (`familyAsChild`,
  `familiesAsSpouse[].spouseName`, a family's `husband`/`wife`) is an
  xref-plus-name pair, not the nested record — a caller who wants a
  parent's own full detail calls `get_record` again with that xref. This
  keeps every response's size bounded by "one record's own facts," never
  by how connected that record happens to be. Citations follow the same
  rule at the researcher's explicit direction: every event, the name, and
  every note carries a `sources` array of bare source xrefs — no page, no
  citation text — resolvable through this same tool if wanted, never
  inlined.
- Requirement 9: `type: "object"` sits alongside this tool's `oneOf` for
  the same reason it does on `find_person`'s output schema — see that
  section's note on the SDK's auto-wrapping behavior. Getting this right
  from the start this time is itself the payoff of having hit it once
  already.
- Requirement 10: this tool is the direct fulfillment of the promise
  requirement 10 makes when `find_person` hands back family xrefs, and it
  extends the same promise forward — every reference it returns
  (`familyAsChild`, each `familiesAsSpouse` entry, each child, each source
  xref, and this record's own xref right back) is itself a valid input to
  another `get_record` call.

### A pre-existing model limitation, stated plainly rather than papered over

`GedIndividual` only ever distinguishes "recorded male" from "everything
else," because `ModelBuilder` has stored `IsMale` that way since before
this MCP work existed (`IsMale = child.Value.Equals("M", ...)`). A GEDCOM 7
`SEX` value of `X` or `U` is indistinguishable from `F` in the model as it
stands today. `get_record`'s `sex` field is therefore `"M"`, `"F"`, or
`null` (never recorded) — not the full GEDCOM 7 value space. Widening
`GedIndividual` to carry the actual recorded value is a reasonable future
change; it is not part of this addendum, and this field does not pretend
otherwise.

### Input

```json
{
  "name": "get_record",
  "description": "Return everything this document records about one person, family, or source, identified by its local xref — the identity, every event with its citations and attached media, notes, and every related record's xref for a follow-up call. Call this once a specific person or family is no longer ambiguous (after find_person returns a single match, or a candidate the user picked) or when the user's question needs detail find_person deliberately omits: children, notes, media, or citations. Pass any xref this server has returned — from find_person's person or family fields, or from an earlier get_record's own references — never one the user typed from memory. Every media file's \"resolved\" flag tells you whether its \"path\" is ready to use as-is: true means open or display it directly (a local absolute path, or an external URL to render as a link or image — never fetch or search for it, this server does not make network requests); false means the file could not be located, so do not guess at where it might be.",
  "inputSchema": {
    "type": "object",
    "additionalProperties": false,
    "properties": {
      "xref": {
        "type": "string",
        "pattern": "^@[^@]+@$",
        "minLength": 3,
        "description": "A local xref returned by this server, e.g. \"@I00006@\", \"@F00012@\", or \"@S00042@\". Not a value the user would know to type themselves."
      }
    },
    "required": ["xref"]
  },
  "annotations": {
    "readOnlyHint": true,
    "destructiveHint": false,
    "idempotentHint": true
  }
}
```

An xref that does not resolve to a person, family, or source in this
document — dangling, mistyped, or naming a record type this tool does not
cover (a media object or a shared note both have their own xref space but
are out of scope here) — is not a schema violation; `NotFound` covers it
without a protocol-level error, the same "structured absence, not a
crash" stance `find_person`'s `NoMatch` already takes.

### Output: four shapes, one schema

```json
{
  "$schema": "https://json-schema.org/draft/2020-12/schema",
  "type": "object",
  "oneOf": [
    { "$ref": "#/$defs/PersonRecord" },
    { "$ref": "#/$defs/FamilyRecord" },
    { "$ref": "#/$defs/SourceRecord" },
    { "$ref": "#/$defs/NotFoundRecord" }
  ],
  "$defs": {
    "EventDetail": {
      "type": ["object", "null"],
      "additionalProperties": false,
      "properties": {
        "date": { "type": ["string", "null"] },
        "year": { "type": ["integer", "null"] },
        "qualifier": { "type": ["string", "null"] },
        "place": { "type": ["string", "null"] },
        "sources": { "type": "array", "items": { "type": "string" } },
        "media": { "type": "array", "items": { "$ref": "#/$defs/MediaDetail" } }
      },
      "required": ["date", "year", "qualifier", "place", "sources", "media"]
    },
    "NoteDetail": {
      "type": "object",
      "additionalProperties": false,
      "properties": {
        "text": { "type": "string" },
        "mime": { "type": ["string", "null"] },
        "sources": { "type": "array", "items": { "type": "string" } }
      },
      "required": ["text", "mime", "sources"]
    },
    "MediaFile": {
      "type": "object",
      "additionalProperties": false,
      "properties": {
        "path": { "type": "string" },
        "mediaType": { "type": "string" },
        "medium": { "type": ["string", "null"] },
        "title": { "type": ["string", "null"] },
        "resolved": { "type": "boolean" }
      },
      "required": ["path", "mediaType", "medium", "title", "resolved"]
    },
    "Crop": {
      "type": ["object", "null"],
      "additionalProperties": false,
      "properties": {
        "top": { "type": ["integer", "null"] },
        "left": { "type": ["integer", "null"] },
        "height": { "type": ["integer", "null"] },
        "width": { "type": ["integer", "null"] }
      },
      "required": ["top", "left", "height", "width"]
    },
    "MediaDetail": {
      "type": "object",
      "additionalProperties": false,
      "properties": {
        "xref": { "type": "string", "pattern": "^@[^@]+@$" },
        "title": { "type": ["string", "null"] },
        "crop": { "$ref": "#/$defs/Crop" },
        "files": { "type": "array", "items": { "$ref": "#/$defs/MediaFile" } }
      },
      "required": ["xref", "title", "crop", "files"]
    },
    "ChildIdentity": {
      "type": "object",
      "additionalProperties": false,
      "properties": {
        "xref": { "type": "string", "pattern": "^@[^@]+@$" },
        "name": { "type": "string" },
        "birthYear": { "type": ["integer", "null"] }
      },
      "required": ["xref", "name", "birthYear"]
    },
    "ParentFamilyReference": {
      "type": ["object", "null"],
      "additionalProperties": false,
      "properties": {
        "xref": { "type": "string", "pattern": "^@[^@]+@$" },
        "fatherName": { "type": ["string", "null"] },
        "motherName": { "type": ["string", "null"] }
      },
      "required": ["xref", "fatherName", "motherName"]
    },
    "SpouseFamilyDetail": {
      "type": "object",
      "additionalProperties": false,
      "properties": {
        "xref": { "type": "string", "pattern": "^@[^@]+@$" },
        "spouseName": { "type": ["string", "null"] },
        "marriage": { "$ref": "#/$defs/EventDetail" },
        "children": { "type": "array", "items": { "$ref": "#/$defs/ChildIdentity" } }
      },
      "required": ["xref", "spouseName", "marriage", "children"]
    },
    "SpouseReference": {
      "type": ["object", "null"],
      "additionalProperties": false,
      "properties": {
        "xref": { "type": "string", "pattern": "^@[^@]+@$" },
        "name": { "type": "string" }
      },
      "required": ["xref", "name"]
    },
    "PersonRecord": {
      "type": "object",
      "additionalProperties": false,
      "properties": {
        "recordType": { "const": "person" },
        "xref": { "type": "string", "pattern": "^@[^@]+@$" },
        "name": { "type": "string" },
        "title": { "type": ["string", "null"] },
        "sex": { "type": ["string", "null"], "enum": ["M", "F", null] },
        "birth": { "$ref": "#/$defs/EventDetail" },
        "death": { "$ref": "#/$defs/EventDetail" },
        "will": { "$ref": "#/$defs/EventDetail" },
        "probate": { "$ref": "#/$defs/EventDetail" },
        "census": { "type": "array", "items": { "$ref": "#/$defs/EventDetail" } },
        "nameSources": { "type": "array", "items": { "type": "string" } },
        "notes": { "type": "array", "items": { "$ref": "#/$defs/NoteDetail" } },
        "restriction": { "type": ["string", "null"] },
        "media": { "type": "array", "items": { "$ref": "#/$defs/MediaDetail" } },
        "familyAsChild": { "$ref": "#/$defs/ParentFamilyReference" },
        "familiesAsSpouse": { "type": "array", "items": { "$ref": "#/$defs/SpouseFamilyDetail" } }
      },
      "required": [
        "recordType", "xref", "name", "title", "sex", "birth", "death", "will",
        "probate", "census", "nameSources", "notes", "restriction", "media",
        "familyAsChild", "familiesAsSpouse"
      ]
    },
    "FamilyRecord": {
      "type": "object",
      "additionalProperties": false,
      "properties": {
        "recordType": { "const": "family" },
        "xref": { "type": "string", "pattern": "^@[^@]+@$" },
        "husband": { "$ref": "#/$defs/SpouseReference" },
        "wife": { "$ref": "#/$defs/SpouseReference" },
        "marriage": { "$ref": "#/$defs/EventDetail" },
        "children": { "type": "array", "items": { "$ref": "#/$defs/ChildIdentity" } },
        "media": { "type": "array", "items": { "$ref": "#/$defs/MediaDetail" } }
      },
      "required": ["recordType", "xref", "husband", "wife", "marriage", "children", "media"]
    },
    "SourceRecord": {
      "type": "object",
      "additionalProperties": false,
      "properties": {
        "recordType": { "const": "source" },
        "xref": { "type": "string", "pattern": "^@[^@]+@$" },
        "author": { "type": ["string", "null"] },
        "title": { "type": ["string", "null"] },
        "publication": { "type": ["string", "null"] },
        "note": { "type": ["string", "null"] }
      },
      "required": ["recordType", "xref", "author", "title", "publication", "note"]
    },
    "NotFoundRecord": {
      "type": "object",
      "additionalProperties": false,
      "properties": {
        "recordType": { "const": "not_found" },
        "xref": { "type": "string" }
      },
      "required": ["recordType", "xref"]
    }
  }
}
```

`husband`/`wife`/parents-of-child are xref-plus-name pairs
(`SpouseReference`, and the two name fields on `ParentFamilyReference`),
never the embedded record — the shallow-reference rule from requirement 8
above. `sources` arrays hold bare xref strings and nothing else, per the
researcher's explicit direction: a `GedSourceRef` with no resolvable
`GlobalSource` (a note-only citation with nothing to point at) contributes
no entry — there is no id to give, not a withheld one. `EventDetail` is
`null` exactly when the underlying `GedEvent` reference itself is absent
(no `BIRT`/`DEAT`/etc. tag at all) — a client never has to distinguish "no
such event" from "the serializer skipped some fields." This is one
deliberate divergence from `find_person`'s `EventIdentity`: that shape
*also* collapses to `null` when an event exists but its date and place are
both blank, because an identity summary has nothing useful to say about
such an event. `get_record` does not apply that second collapse — a sparse
event (say, a `BIRT` tag with a source citation or a photo but no `DATE`)
still carries real information this tool exists to surface, so its
`EventDetail` is non-null whenever the tag itself is present, holding
whatever combination of `null` fields, sources, and media actually exist.

`MediaFile.path` and `.resolved` exist because a raw GEDCOM `FILE` payload
by itself is not something a caller can act on: it is either an absolute
URL or a path relative to the media directory (always the GEDCOM's own
directory — see "Architecture"), and nothing in the payload says which, or whether the
file the path names actually exists. Leaving that resolution to the
calling agent is what caused the problem this field exists to prevent: a
bare relative filename with no directory context, handed to an agent with
no local-filesystem access of its own, reads as something to search the
web for — which is slow, sometimes hangs, and is exactly the outcome
requirement 2 (no network calls, explicit trust boundary) means this
server itself must never cause even indirectly. `get_record` resolves the
question itself, the same way `SiteGenerator.ResolveMediaSrc` already does
for HTML generation (same `MediaPaths` helpers, same escape-the-media-root
rejection, same existence check) — reused, not reimplemented:

- An absolute URL passes through unchanged with `resolved: true` — it is
  already a usable reference, and this server does not fetch it to verify
  anything (still no network calls). The tool description tells the
  calling agent to render it directly (e.g. as a markdown image
  reference) rather than fetching or searching for it first.
- A relative path that resolves to an existing file under the media
  directory becomes that file's absolute local path, with `resolved: true`
  — ready to open directly, no search, no follow-up call.
- A relative path that is missing, or would resolve outside the media
  directory (the same path-traversal rejection `ResolveMediaSrc` already applies),
  keeps the raw payload exactly as recorded, with `resolved: false` — a
  clear "do not try to locate this yourself" signal, rather than silently
  handing back something that looks actionable but is not.

#### `PersonRecord`

```json
{
  "recordType": "person",
  "xref": "@I00006@",
  "name": "Abraham Morrill",
  "title": null,
  "sex": "M",
  "birth": {
    "date": "14 NOV 1652", "year": 1652, "qualifier": null,
    "place": "Salisbury, Massachusetts", "sources": ["@S00042@"], "media": []
  },
  "death": {
    "date": "1698", "year": 1698, "qualifier": null,
    "place": "Salisbury, Massachusetts", "sources": [], "media": []
  },
  "will": null,
  "probate": null,
  "census": [],
  "nameSources": [],
  "notes": [],
  "restriction": null,
  "media": [],
  "familyAsChild": { "xref": "@F00001@", "fatherName": "Abraham Morrill", "motherName": "Sarah Clements" },
  "familiesAsSpouse": [
    {
      "xref": "@F00003@",
      "spouseName": "Sarah Bradbury",
      "marriage": { "date": "1675", "year": 1675, "qualifier": null, "place": null, "sources": [], "media": [] },
      "children": [
        { "xref": "@I00022@", "name": "Abraham Morrill", "birthYear": 1671 }
      ]
    }
  ]
}
```

#### `FamilyRecord`

```json
{
  "recordType": "family",
  "xref": "@F00003@",
  "husband": { "xref": "@I00006@", "name": "Abraham Morrill" },
  "wife": { "xref": "@I00019@", "name": "Sarah Bradbury" },
  "marriage": { "date": "1675", "year": 1675, "qualifier": null, "place": null, "sources": [], "media": [] },
  "children": [
    { "xref": "@I00022@", "name": "Abraham Morrill", "birthYear": 1671 }
  ],
  "media": []
}
```

#### `SourceRecord`

```json
{
  "recordType": "source",
  "xref": "@S00042@",
  "author": null,
  "title": "Vital Records of Salisbury, Massachusetts",
  "publication": "Essex Institute, 1915",
  "note": "Salisbury VR, p. 214"
}
```

#### `NotFoundRecord`

```json
{ "recordType": "not_found", "xref": "@I99999@" }
```

### Errors and limits

- A blank or whitespace-only `xref` is a tool execution error (`isError:
  true`), the same trim-and-validate treatment `find_person` gives a blank
  `query` — JSON Schema's `minLength` does not catch a whitespace-only
  string either.
- An xref that is syntactically fine but does not resolve to a person,
  family, or source produces `NotFoundRecord`, not an error — a wrong or
  stale xref is an ordinary outcome, not a failure.
- No result-size limit applies to any shape here — unlike `find_person`'s
  candidate cap, a single record's own event, note, media, and reference
  lists are bounded by how much this one record actually carries, not by
  how many records matched a query.
- Reload failures and the last-chance `Exception` handler are the same
  shared paths specified for `find_person`.

## Testing approach

Consistent with this repo's conventions, matching is tested through public
methods against synthetic in-memory `GedModel` instances. One test class per
public production class covers:

- normalization, punctuation, hyphen preservation (including repeated and
  leading/trailing hyphens), underscores, Unicode letters, and short tokens;
- query splitting on a hyphenated surname, confirming it stays one token
  and is compared as the surname rather than being torn apart;
- the `JaroWinkler` routine itself: identical strings, empty strings,
  transpositions, the prefix-bonus cap, symmetry, and the documented
  `FRED`/`FREDERICK` and `BILL`/`WILLIAM` reference values;
- query splitting for one-token and multi-token queries, and the name-only
  recall gate at its 70 and 55 boundaries;
- the `NicknameDirectory`: loading from the embedded resource, group
  equivalence in both directions (formal↔nickname and nickname↔nickname),
  sex-map selection and the unrecorded-sex union, the fixed 20-point
  override, and an exact given name still outranking a nickname match;
- every evidence weight and its availability condition — hint absent,
  candidate datum absent, and both present — including the graded birth-year
  steps and the absence of any hard exclusion;
- the 90 threshold and 10-point margin, same-name ties both broken and
  unbroken by hints, and deterministic ordering including the raw-score
  tiebreak;
- single, candidate, truncated, suggestion, and no-match shapes;
- absent and present `FamChild` links mapping to zero or one `asChild` xref;
- every `FamSpouse` link — childless marriages included — mapping to
  `asParent` in model order, with exact family xref, raw marriage date, and
  canonical spouse name;
- null marriage dates and spouse names, agreement between `asParent` and
  candidate `spouses`, and empty arrays rather than omitted properties; and
- output from every successful branch validating against `outputSchema`.

File tests remain at the highest level. Subprocess integration tests launch
the packed command against a synthetic GEDCOM and verify:

- missing/invalid startup input exits 1 with stdout empty;
- modern `server/discover`/per-request metadata and legacy `initialize`
  clients can each list and call the tool;
- `tools/list` is deterministic and advertises the schemas and truthful
  annotations declared here, and both client eras accept and validate against
  the top-level `oneOf` output schema;
- success uses `structuredContent` plus the JSON text fallback, while
  recoverable input and reload failures use `isError: true`;
- diagnostics never contaminate stdout, each stdout line is valid JSON-RPC,
  and closing stdin causes a prompt exit 0;
- changing the GEDCOM triggers one atomic reload, concurrent calls never see
  a partial model, and an unstable or invalid replacement is reported rather
  than serving stale data; and
- an injected tool-body exception returns `isError: true` with the
  exception type and call stack in the text content block, and the server
  answers subsequent calls normally; and
- cancellation reaches a waiting or reloading call without a late response.

`get_document_stats` needs far less: no argument-shaped test surface, and
its only branch is the nullable `gedVersion`. Coverage is `personCount`/
`familyCount` against a known synthetic document, a GEDCOM 7.0 and a 5.5
fixture producing the expected `gedVersion` string, a header-less or
`VERS`-less fixture producing `null` rather than an error, and — at the
subprocess level — that `tools/list` now advertises both tools (by
membership; see the note above on why order is not asserted) and a
`tools/call` round-trips the flat object shape through `structuredContent`.

`get_record` needs the fullest coverage of the three tools after
`find_person` itself, because it has the most branches per record: a
person with every event type populated, a person with none, a childless
family, a family with no marriage event, media with and without a crop
and with multiple files, a note-only citation contributing no entry to a
`sources` array, `familyAsChild` present and absent, `familiesAsSpouse`
with more than one marriage, and a source whose note carries FTM directive
markers (`SHORTCITATION`/`NOCITATION`) to prove `FtmCitationText
.ParseSourceNote` is actually being reused and not reimplemented. Also:
resolving all three record types from the same xref-lookup path, a
dangling/mistyped xref producing `NotFoundRecord` rather than an error, a
blank xref as a tool execution error, and — at the subprocess level — all
three tools listed together and one `tools/call` round-trip per record
type through `structuredContent`. Media path resolution gets its own
cases: a self-describing `media/...` relative path that resolves to a real
file under the default media directory (the GEDCOM's own directory;
`resolved: true`, `path` rewritten to the absolute path), one that is
missing, one that would escape the media directory via `../`, and an
absolute URL passed through unchanged.

All fixtures use synthetic people (requirements 2 and 9).

## Open questions for later addenda

- Whether a client that supports MCP's elicitation primitive should get a
  variant behavior — the server prompting the human directly for a pick
  from `CandidateList` rather than returning it for the agent to phrase.
  Not pursued now because the initialization-based compatibility target does
  not support the modern multi-round-trip result uniformly. Returning data
  works in both eras and keeps identity selection with the calling agent.
- Whether multi-document research justifies an `open_document` tool and
  opaque document handles. The first release deliberately binds one document
  at startup; any expansion must preserve path privacy, bounded lifetime, and
  explicit document scope.
- Whether alternate `NAME` structures should be added to `GedModel` and the
  recall algorithm. They are not inferred from the primary name in this
  release.
- Whether spouse and parent hint comparisons should also consult the
  nickname dictionary, which this release applies only to the query's
  given-name comparison.
- The full initial tool roster named in "Non-goals" above, each as its own
  short addendum once this document and `find_person` are implemented and
  reviewed.
