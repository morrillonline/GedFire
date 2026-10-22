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

```text
historical records -> findings -> changesets -> review -> GEDCOM -> website
```

GedFire handles the mechanics. You and your reviewer judge the evidence.
The server exposes nineteen typed tools; only `apply_changeset` can modify
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
gedfire mcp --input "C:\Genealogy\family.ged" --read-only --enforce-privacy
```

Replace the input path with your file's absolute path. The server uses stdio;
it waits silently for a client connection. Let the client launch it rather
than running a second copy in a terminal.

Ask your agent: **"How many people and families are in this file?"**
It should call `get_document_stats` and report both counts.

`--read-only` blocks GEDCOM changes, not drafting or report files.
`--enforce-privacy` masks protected and plausibly living individuals in tool
output. Keep both flags until you understand the
[safety and privacy limits](#safety-and-review).

## Try the fictional demo

[Browse the generated family pages](https://morrillonline.github.io/GedFire/).
CI rebuilds this site from the fictional Ash family on every push. Hover over
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
- **Rules you control.** A policy file can reject banned prose, require
  verification notes, and demand evidence for a death place.
- **Local processing.** GedFire makes no network requests and sends no
  telemetry. Your MCP client controls where tool results go.
- **One publishing source.** Generate family pages directly from the reviewed
  GEDCOM rather than maintaining a separate copy of its facts.

Agents exchange compact findings and operations instead of rewriting whole
records. Token use depends on the client and task; this README makes no
quantified savings claim.

GedFire is not a graphical tree editor, record provider, or hosted genealogy
service. It cannot establish that a quotation is genuine or an identification
is correct.

## Connect an AI client

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
        "--read-only", "--enforce-privacy"
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
        "--read-only", "--enforce-privacy"
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
args = ["mcp", "--input", "C:\\Genealogy\\family.ged", "--read-only", "--enforce-privacy"]
```

### Options and troubleshooting

All examples use Windows paths. On Linux or macOS, replace the input with an
absolute path such as `/home/me/genealogy/family.ged`. JSON and TOML
double-quoted strings require escaped backslashes on Windows.

| Option | Effect |
|---|---|
| `--read-only` | Refuse every `apply_changeset` call. Leave all other tools available, including draft and report output. |
| `--enforce-privacy` | Mask protected and plausibly living people in tool output. See [privacy limits](#privacy). |
| `--policy <json>` | Enforce editorial rules during changeset validation and apply. See the [example policy](docs/policy.example.json) and [policy reference](docs/mcp-reference.md#changeset-policy-file). |

Restart or reload your client after changing its configuration. If startup
fails, check:

1. `gedfire --version` works in a new terminal.
2. The absolute GEDCOM path exists.
3. The client's MCP log shows no startup error.
4. `command` names an absolute executable path if the client cannot find
   `gedfire` on its inherited `PATH`.

GedFire needs no API keys, credentials, or environment variables.

## Research workflow

**A finding records what a source says.** Capture a verbatim extract, source
and locator, source and information classes, claimed facts, and quality grades.
Keep conflicting findings instead of flattening them into one conclusion.

**Correlation identifies the person.** Use `find_person` for a hinted name
lookup or `match_record` for a household, obituary, or other multi-person
record. Inspect the candidates and exclusions. A deterministic match score is
not a probability or a substitute for a proof argument. `find_person` returns
`single` only at a score of at least 90, even for a lone candidate, and requires
a lead of at least 10 points when alternatives exist. Lower-scoring matches
remain available as `candidates`.

**A changeset specifies the edits.** `draft_changeset` compiles findings into
typed operations. It reconciles compatible facts, preserves their citations,
and leaves genuine disagreements as `open` decisions. Fix the findings and
draft again when validation exposes a defect.

**A proposal presents the case for review.** After `validate_changeset` and
`check_plausibility`, call `draft_package`. It writes a numbered proposal
from the finished changesets, including evidence, grades, and any identity
arguments the author supplied.

**Apply only what your review process accepts.** Select the approved items
with `apply_changeset`. It validates and verifies them again before writing.
Generate the site when you are ready to publish.

Source and information classes bound citation quality; they do not prove a
claim. A death certificate may offer primary information about death but
secondary information about birth. Read the [grading and drafting
reference](docs/mcp-reference.md#drafting-a-changeset) for the QUAY ceilings.

## The tools

The server binds to one GEDCOM and reloads it when the file changes on disk.
Every returned xref belongs to that document only; never reuse it against a
different file or provider.

| Tool | Purpose |
|---|---|
| `get_document_stats` | Report person and family counts, GEDCOM version, and GedFire version. |
| `find_person` | Rank name matches with optional birth, death, parent, and marriage hints. |
| `find_family` | Find a person through a named spouse, parent, or child. |
| `find_source` | Find existing sources by title, author, or URL. |
| `get_record` | Fetch a person, family, or source with citations and optional source text. |
| `get_records` | Fetch several records at once, optionally selecting fields. |
| `list_people` | Page through people in stable surname/name/xref order. |
| `list_unanchored_people` | Find people with no dated or placed anchors of their own or through immediate kin. |
| `match_record` | Compare a multi-person record with tree families and survey same-name rivals. |
| `research_brief` | Summarize a person's facts, kin, gaps, notes, duplicates, and plausibility issues. |
| `select_targets` | Score research gaps and draw targets, or score specified people. |
| `date_calc` | Normalize dual dates, add or subtract ages, or calculate elapsed time. |
| `describe_changeset_ops` | Return the changeset envelope, operation schemas, and worked examples. |
| `draft_changeset` | Compile findings into cited changesets and report unresolved decisions. |
| `draft_package` | Write a review proposal from finished changesets. |
| `validate_changeset` | Preview selected edits, enforce policy, and optionally score gap closures. |
| `check_plausibility` | Report chronological and biological issues the edits would introduce. |
| `validate_document` | Check the whole document for GEDCOM 7 conformance. |
| `apply_changeset` | Validate, apply, and verify selected items, then write the GEDCOM. |

Use the advertised MCP schemas for exact parameters and the
[MCP reference](docs/mcp-reference.md) for behavior and examples.
Every tool rejects missing required parameters and unrecognized parameters.
The error names the offending fields and lists the accepted ones.

## Safety and review

### What GedFire enforces

- Dry-run and apply use the same validation. If an operation fails validation,
  GedFire writes nothing.
- Path-based apply holds an exclusive file lock from the initial read through
  the verified write to prevent competing GedFire writers from racing.
- Before creating a person, validation rejects high-confidence duplicates
  among existing and planned people. Ambiguous candidates do not block creation.
- Before writing, GedFire checks a byte-stable reparse/format round trip,
  pointer resolution, and expected record-count changes.
- `--read-only` refuses apply calls even if the client approves them.
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

`--enforce-privacy` uses the site's privacy filter. It masks individuals with
`RESN CONFIDENTIAL` or `RESN PRIVACY`, plus plausibly living people: no
death-class fact and a birth within the last 100 years. Their tool views become
`Living <Surname>` placeholders without dates, places, notes, or media.

The filter uses recorded facts, not verified living status. Placeholders still
reveal surnames. Without a birth year or explicit restriction, the age
heuristic does not mask a person. The flag does not promise to redact every
mention in other records or user-supplied text. Inspect your data and your
client's privacy settings before sharing results.

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

## The command line

The CLI uses the same engine as the MCP server.

| Command | Purpose |
|---|---|
| `create` | Create a seeded GEDCOM 7 document. |
| `upgrade` / `downgrade` | Convert between GEDCOM 5.5 and 7. |
| `validate` | Check GEDCOM 7 conformance. |
| `apply` | Preview or apply selected changeset items; use `--dry-run` to preview. |
| `generate` | Generate static family pages, with optional media staging. |
| `export-index` | Export a JSON person-name index. |
| `select-targets` | Detect, score, and draw research gaps for study surnames. |
| `date-calc` | Normalize dual dates or calculate dates and ages. |
| `find-person` / `get-record` / `get-document-stats` | Run one lookup and print JSON without starting a server. |
| `pack` / `unpack` | Create or extract a GEDZIP archive. |
| `mcp` | Start the stdio MCP server. |

Run `gedfire help` for command syntax. See the [MCP reference](docs/mcp-reference.md)
for changeset semantics and date-arithmetic examples.

Target selection withholds New parent and New spouse cards until the person's
own birth, christening/baptism, death, or burial has an exact date or a non-empty
place. New child cards need an anchor on either spouse or the marriage.
Enrich person cards are unchanged; `suppressedCards` reports withheld cards and
reasons in both CLI and MCP results. Enrichment re-enables cards on the next draw.

## Roadmap

- **Interoperability audit:** a planned read-only `gedfire audit` command to
  report conformance, extensions, unresolved pointers, and media readiness.
- **Companion research skills:** the working skills need packaging,
  portability, and documentation cleanup before a standalone release.
  GedFire does not require them; they are not yet a published companion package.
- **`GedCore` on NuGet:** a possible parser/apply library package, if there is
  interest.

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
