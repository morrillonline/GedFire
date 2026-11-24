# GedFire

<!-- mcp-name: io.github.morrillonline/gedfire -->

[![CI](https://github.com/morrillonline/GedFire/actions/workflows/ci.yml/badge.svg)](https://github.com/morrillonline/GedFire/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-2e7d32.svg)](LICENSE)
[![.NET 10](https://img.shields.io/badge/.NET-10-512bd4.svg)](https://dotnet.microsoft.com/)

**Give an AI agent genealogy tools, not a text editor for your GEDCOM.**

GedFire is a local [Model Context Protocol](https://modelcontextprotocol.io/)
(MCP) server and command-line tool. It helps an agent find people, compare
records, draft cited changes, and validate them before writing your family
history. It also generates static family-page websites from the same data.

GedFire supports **local MCP mode only**, using stdio (standard input/output).
Your MCP client launches the installed executable as a local process. There
is no remote HTTP, Streamable HTTP or SSE transport, server URL, or listening
port. This describes the MCP connection, not where your client's AI model runs.

```text
historical records -> findings -> changesets -> review -> GEDCOM -> website
```

A *finding* is what one source says. A *changeset* is a reviewable set of typed
edits drafted from findings.

GedFire handles the mechanics. You and your reviewer judge the evidence.
The server exposes seventeen typed tools; only `apply_changeset` can modify
the bound GEDCOM. Other tools can write drafts and reports to separate files.
[Review and permission limits](#safety-and-review) matter: GedFire validates
changes, but it does not collect or authenticate your approval.

## Quick start

Install with the .NET 10 SDK:

```powershell
dotnet tool install --global GedFire
gedfire --version
```

Or [download a self-contained release](#installation) without installing .NET.

Start with a copy of your GEDCOM. Configure your
[MCP client](#connect-an-ai-client) to launch:

```powershell
gedfire mcp --input "C:\Genealogy\family.ged" --read-only
```

Replace the input path with your file's absolute path. Let the client launch
the local stdio server rather than running a second copy in a terminal; it
communicates through the process streams, not a network connection.

Ask your agent: **"How many people and families are in this file?"**
It should call `get_document_stats` and report both counts.

Keep `--read-only` until you understand the [safety limits](#safety-and-review).

## Try the fictional demo

[Browse the generated family pages](https://morrillonline.github.io/GedFire/).
CI rebuilds this site from the fictional Ash family whenever the demo or the code changes on `main`. Hover over
the footnotes to read each fact's citations.

To run the demo yourself, download or clone this repository. From its root:

```powershell
Copy-Item docs\demo\ash-whitfield.ged family.ged
gedfire apply --input family.ged --changes docs\demo\proposed-findings.json --items all --dry-run
gedfire apply --input family.ged --changes docs\demo\proposed-findings.json --items all
gedfire generate --input family.ged --output-dir site --template docs\demo\template.html
```

The first apply only previews the changes. The second writes them to the copy.
Open `site/index.html` to browse the result. All people, records, places, and
sources in the demo are fictional. On Linux or macOS, use `cp` and `/` paths.

## Why GedFire?

Online trees offer leads, not proof. Repeated claims can spread an error
without adding evidence. GedFire supports a different workflow: capture what
a record says, identify whom it concerns, then propose a cited change.

- **Typed changes, not raw text edits.** GedFire applies operations to a
  parsed GEDCOM document. It checks structure, pointers, and record-count
  changes before writing.
- **Citations travel with facts.** Findings carry verbatim extracts, source
  details, and quality grades. Drafted changes and generated pages retain them.
- **Deterministic mechanics.** Tools rank identity candidates, reconcile
  compatible findings, reuse sources, and flag disagreements. The agent
  supplies the evidence and judgment.
- **Rules you control.** A policy file can reject banned prose and demand
  evidence for a death place.
- **Local processing.** GedFire makes no network requests and sends no
  telemetry. Your MCP client controls where tool results go.
- **One publishing source.** Generate family pages directly from the reviewed
  GEDCOM rather than maintaining a separate copy of its facts.

Agents exchange compact findings and operations instead of rewriting whole
records.

GedFire is not a graphical tree editor, record provider, or hosted genealogy
service. It cannot establish that a quotation is genuine or an identification
is correct.

## Connect an AI client

Choose **local** or **stdio** if your client asks for the MCP server type.

### VS Code / GitHub Copilot

Add this to `.vscode/mcp.json` in your workspace:

```json
{
  "servers": {
    "gedfire": {
      "type": "stdio",
      "command": "gedfire",
      "args": [
        "mcp", "--input", "C:\\Genealogy\\family.ged",
        "--read-only"
      ]
    }
  }
}
```

Replace the input path, start the server from VS Code's MCP controls, and
approve trust when prompted. Enable its tools in chat. See the
[VS Code MCP guide](https://code.visualstudio.com/docs/agent-customization/mcp-servers)
for user-level configuration and tool permissions.

### Claude Desktop, Claude Code, Cursor, Windsurf, and Gemini CLI

These clients use an `mcpServers` entry:

```json
{
  "mcpServers": {
    "gedfire": {
      "command": "gedfire",
      "args": [
        "mcp", "--input", "C:\\Genealogy\\family.ged",
        "--read-only"
      ]
    }
  }
}
```

| Client | Configuration file |
|---|---|
| Claude Desktop | Open **Settings > Developer > Edit Config** to edit `claude_desktop_config.json`. |
| Claude Code | `.mcp.json` in the project root. |
| Cursor | `.cursor/mcp.json` in the project, or the client's global `mcp.json`. |
| Windsurf | `~/.codeium/windsurf/mcp_config.json`. |
| Gemini CLI | `.gemini/settings.json` in the project, or `~/.gemini/settings.json`. |

### Codex

Add this to `.codex/config.toml` in the project or `~/.codex/config.toml`:

```toml
[mcp_servers.gedfire]
command = "gedfire"
args = ["mcp", "--input", "C:\\Genealogy\\family.ged", "--read-only"]
```

### Options and troubleshooting

All examples use Windows paths. On Linux or macOS, replace the input with an
absolute path such as `/home/me/genealogy/family.ged`. JSON and TOML
double-quoted strings require escaped backslashes on Windows.

| Option | Effect |
|---|---|
| `--read-only` | Refuse every `apply_changeset` call. Leave all other tools available, including draft and report output. |
| `--policy <json>` | Enforce editorial rules during changeset validation and apply. See the [example policy](docs/policy.example.json) and [policy reference](docs/mcp-reference.md#changeset-policy-file). |

Restart or reload your client after changing its configuration. If startup
fails, check:

1. `gedfire --version` works in a new terminal.
2. The absolute GEDCOM path exists.
3. The client's MCP log shows no startup error.
4. `command` names an absolute executable path if the client cannot find
   `gedfire` on its inherited `PATH`.

GedFire needs no API keys, credentials, or environment variables.

## The tools

The server binds to one GEDCOM and reloads it when the file changes on disk.
Every returned xref (a record ID such as `@I1@`) belongs to that document only; never reuse it against a
different file or provider.

Use the advertised MCP schemas for exact parameters and the
[MCP reference](docs/mcp-reference.md) for behavior and examples.
Every tool rejects missing required parameters and unrecognized parameters.
The error names the offending fields and lists the accepted ones.

### Tools for reading

These tools never change the GEDCOM. To identify a person, use `find_person`
for a hinted name lookup or `match_record` for a household, obituary, or other
multi-person record. `match_record` calls a match `single` only when lifespan,
place, or kin support it beyond the name; its score is not a probability or a
proof.

| Tool | Purpose |
|---|---|
| `get_document_stats` | Report person and family counts, GEDCOM version, and GedFire version. |
| `find_person` | Rank name matches with optional birth, death, parent, and marriage hints. |
| `get_record` | Fetch a person, family, or source with citations and optional source text. |
| `get_records` | Fetch several records at once, optionally selecting fields. |
| `match_record` | Compare a multi-person record with tree families and survey same-name rivals. |
| `research_brief` | Summarize a person's facts, kin, gaps, notes, duplicates, and plausibility issues. |
| `validate_document` | Check the whole document for GEDCOM 7 conformance. |
| `date_calc` | Normalize dual dates, add or subtract ages, or calculate elapsed time. |
| `describe_changeset_ops` | Return the changeset contract: envelope, structures, operations, and worked examples. |
| `describe_schemas` | Return the versioned JSON Schemas of the finding-file and changeset contracts, cards, and saved reports. |

### Tools for writing

These tools turn findings into reviewed changes; only `apply_changeset`
writes the GEDCOM. The work runs in four steps:

1. **Record findings.** A finding is what one source says: a verbatim
   extract, the source and locator, its source and information classes, the
   facts it claims, and their grades. Keep conflicting findings rather than
   merging them. `validate_finding` checks saved findings
   (`gedfire schema finding-file`) before drafting.
2. **Draft a changeset.** `draft_changeset` compiles findings into typed edits
   under the changeset contract (`gedfire schema changeset`, with
   `describe_changeset_ops` as its guide). It merges facts that agree, keeps
   their citations, and leaves disagreements as `open` decisions. When
   validation finds a defect, fix the finding and draft again.
3. **Package it for review.** Run `validate_changeset` and
   `check_plausibility`, both of which accept `after`, the changesets approved
   ahead of this one. Then `draft_package` writes a numbered proposal with the
   evidence, grades, and identity arguments.
4. **Apply what review accepts.** `apply_changeset` validates and verifies the
   selected items again before writing. `gedfire generate` then publishes
   family pages showing every fact a changeset can write, with its citations.

Source and information classes cap a citation's grade; they do not prove a
claim. The [drafting reference](docs/mcp-reference.md#drafting-a-changeset)
covers QUAY ceilings, name corrections, and the full contract.

| Tool | Purpose |
|---|---|
| `validate_finding` | Check finding files for shape, readiness, and xref bindings without drafting. |
| `draft_changeset` | Compile findings into cited changesets and report unresolved decisions. |
| `draft_package` | Write a review proposal from finished changesets. |
| `validate_changeset` | Preview selected edits, optionally after earlier changesets, and enforce policy. |
| `check_plausibility` | Report chronological and biological issues the edits would introduce, optionally after earlier changesets. |
| `apply_changeset` | Validate, apply, and verify selected items, then write the GEDCOM. |

### Tools for keeping score

An AI agent making genealogical discoveries works better when it can measure
its progress and knows that some work is worth more than other work. Points
give it both: finding a missing parent is worth more than tidying a date, and
a hard, early, or overseas line is worth more than an easy one. With that
scale the agent can choose which task to take on next, and it can tell
progress from effort, noticing when a session has added little to the tree.

A research card is one unit of work on one person in the tree, with the points
it pays. Each card names a gap and the person it belongs to:

| Card | The work it asks for |
|---|---|
| New parent | Find a parent the tree does not record. |
| New spouse | Find a spouse for a person with none recorded. |
| New child | Find a child of a couple with none recorded. |
| Enrich person | Make a birth, marriage, or death date or place exact, or cite it. |
| Biography | Add cited facts beyond birth, marriage, and death, or cited biographical prose. Repeatable; dealt only for men, but pays for anyone. |

A card pays its points plus a difficulty bonus for how hard the person is to
research, judged from the era and place of their records and, for a new
relative, how exact the person's birth is. An Enrich person card pays its points for each listed gap a changeset
fills, with the bonus added once; a Biography card pays at most once per person
per changeset. `select_targets` deals cards for a few people at a time, and
`validate_changeset` with `cards` shows which cards a changeset closes and what
they earn.

These tools deal research cards and tally the points a changeset earns.

| Tool | Purpose |
|---|---|
| `select_targets` | Draw people with research gaps for given surnames, or score named people, and return every card that pays for each. |
| `validate_changeset` | With `cards`, preview which cards the selected edits close and the points they earn, priced as `select_targets` dealt them. |

## Safety and review

### What GedFire enforces

- Dry-run and apply use the same validation. If an operation fails validation,
  GedFire writes nothing.
- Apply takes a machine-wide write lock on the GEDCOM from the initial read
  through the write, so competing GedFire writers run one at a time and each
  builds on the one before it. The new file is written to a temporary copy
  and swapped in whole, so an interrupted write leaves the GEDCOM exactly as
  it was. A writer that cannot get the lock fails and writes nothing. The
  lock leaves no file behind.
- Before creating a person, validation rejects high-confidence duplicates
  among existing and planned people. Ambiguous candidates do not block creation.
- A new parent, spouse, or child is refused when the person it attaches to
  already has a parent in that role, or a spouse or child of an agreeing name,
  unless the relation lists that relative in `notDuplicateOf`.
- Before writing, GedFire checks a byte-stable reparse/format round trip,
  pointer resolution, and expected record-count changes.
- `--read-only` refuses apply calls even if the client approves them.
- A draft, report, index, or archive is never written over the GEDCOM itself.
  An output path that names it is refused, with or without `--read-only`.
- A configured policy can reject a changeset at both preview and apply.

Keep backups or version history. Structural validation cannot catch every
genealogical mistake.

### What you and your client control

**GedFire does not authenticate reviewer approval.** `apply_changeset` accepts
the caller's selected items; it does not verify that a human or independent
reviewer approved them. A proposal supports consent but does not enforce it.

Use your MCP client's tool permissions to control apply access. For a hard
GEDCOM write block, keep `--read-only`. Remove it only when you intend to allow
writes. Decide explicitly whether a human must approve each change or whether
you authorize an automated review workflow. GedFire does not require a human
gate; an independent model can review every item.

Validation checks the edits, not the truth of the evidence. Review quotations,
identity arguments, source independence, and unresolved conflicts yourself or
through a reviewer you trust.

### Privacy

GedFire makes no network requests and sends no telemetry. Your chosen client
may send tool results to a model provider; local processing does not make the
whole conversation local.

MCP tools work on the whole GEDCOM, living people included, with two
exceptions. A `select_targets` surname draw skips anyone born within the last
100 years; naming a person by xref, or asking for a `research_brief`, is never
limited. And the family pages `gedfire generate` writes mask living people.
There,
individuals with `RESN CONFIDENTIAL` or `RESN PRIVACY`, plus plausibly living
people (no death-class fact and a birth within the last 100 years), become
`Living <Surname>` placeholders without dates, places, notes, or media, and
the facts of their families are withheld.

The filter uses recorded facts, not verified living status. Placeholders still
reveal surnames. Without a birth year or explicit restriction, the age
heuristic does not mask a person. Inspect your data and your client's privacy
settings before sharing results.

## Installation

### Install as a .NET tool

Requires the .NET 10 SDK:

```powershell
dotnet tool install --global GedFire
gedfire --version
```

To update: `dotnet tool update --global GedFire`.

### Download a release

Download your platform's archive from
[GitHub Releases](https://github.com/morrillonline/GedFire/releases) and
extract it. These self-contained binaries do not require a separate .NET install.

On Windows, run `.\GedFire.exe help`. On Linux or macOS, run `./GedFire help`.
To use a release with an MCP client, set `command` to the full path of the
extracted `GedFire` executable.

## The command line

The CLI uses the same engine as the MCP server.

| Command | Purpose |
|---|---|
| `create` | Create a seeded GEDCOM 7 document. |
| `upgrade` / `downgrade` | Convert between GEDCOM 5.5 and 7. |
| `validate` | Check GEDCOM 7 conformance. |
| `apply` | Preview or apply selected changeset items; use `--dry-run` to preview. |
| `generate` | Generate static family pages, copying referenced media from the GEDCOM's folder; `--template` sets the page template. |
| `export-index` | Export a JSON person-name index. |
| `select-targets` | Draw `--count` people with research gaps among those who bear, or married someone who bears, one of `--surnames`, and write every card that pays for each to `--output`; `--study-surnames` limits the cards to a surname study's scope. |
| `date-calc` | Normalize dual dates or calculate dates and ages. |
| `find-person` / `get-record` / `get-document-stats` | Run one lookup and print JSON without starting a server. |
| `schema` | Print a versioned JSON Schema of a GedFire file contract (`finding`, `finding-file`, `record`, `argument`, `changeset`, `cards`, `changeset-report`, `draft-report`). |
| `pack` / `unpack` | Create or extract a GEDZIP archive. |
| `mcp` | Start the local-only stdio MCP server; no HTTP or remote transport. |
| `tools list` / `tools describe` / `tools call` | Discover or invoke any MCP tool directly using the same JSON schemas and handlers. |

Run `gedfire help` for command syntax. See the [MCP reference](docs/mcp-reference.md)
for changeset semantics and date-arithmetic examples.

### Use tools from scripts

Every MCP tool is also available as a one-shot command, without a server or AI client:

```text
gedfire tools list
gedfire tools describe find_person
gedfire tools call find_person --input family.ged --args request.json --out result.json
```

The arguments file contains exactly the MCP tool's arguments object, for example:

```json
{"query":"Mara Example","maxResults":8}
```

Use `--args -` to read JSON from stdin, or omit `--args` for `{}`. Results are
JSON on stdout unless `--out` names a new file; existing output files are
refused. Diagnostics go to stderr. The exit code is 0 on success and 1 on
errors or failed validation/plausibility checks.

`--policy` and `--read-only` work as they do on `mcp`.
Discovery needs no GEDCOM. Calls to `date_calc`, `describe_changeset_ops` and
`describe_schemas` need no `--input`. Without `--input`, `validate_finding`
checks every rule that needs no document. See the
[script interface](docs/mcp-reference.md#command-line-tool-interface) for output details.

## Building from source

Requires the .NET 10 SDK:

```powershell
git clone https://github.com/morrillonline/GedFire.git
cd GedFire
dotnet build --nologo
dotnet test --nologo
.\GedFire\bin\Debug\net10.0\GedFire.exe help
```

The solution contains `GedCore` (engine), `GedFire` (CLI, MCP server, and site
generator), and `GedCore.Tests`.

Versions follow `VersionMajor.VersionMinor.BuildNumber`.
[`Directory.Build.props`](Directory.Build.props) defines major/minor
(currently `4.1`). Local builds default to build number `1`; CI supplies its
workflow run number, and release builds use the final segment of a
`v<major>.<minor>.<build>` tag. Set `-p:BuildNumber=<build>` on `dotnet build`,
`test`, or `publish` to reproduce a build number locally.

## Standards and contributing

GedFire independently implements
[FamilySearch GEDCOM 7](https://gedcom.io/). It has no affiliation with or
endorsement from FamilySearch or The Church of Jesus Christ of Latter-day Saints.
See the [official specification repository](https://github.com/FamilySearch/GEDCOM)
and [registries](https://github.com/FamilySearch/GEDCOM-registries) for standards
and extensions.

Bug reports, pull requests, and synthetic edge-case fixtures are welcome.
Never include private family data in an issue, fixture, or pull request.
Run `dotnet test --nologo` before opening a PR.

## License

[MIT](LICENSE).
