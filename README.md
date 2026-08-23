# GedFire

[![CI](https://github.com/morrillonline/GedFire/actions/workflows/ci.yml/badge.svg)](https://github.com/morrillonline/GedFire/actions/workflows/ci.yml)
[![License: MIT](https://img.shields.io/badge/License-MIT-2e7d32.svg)](LICENSE)
[![.NET 10](https://img.shields.io/badge/.NET-10-512bd4.svg)](https://dotnet.microsoft.com/)

GedFire is a command-line tool that lets an AI agent help with your family
history research without giving it write access to your GEDCOM file. The agent
writes its findings as a JSON proposal; you review it, approve the items you
want, and GedFire applies them, keeping each citation attached to the claim it
supports. It can then generate a static family-page site from the result.

```text
agent research -> JSON proposal -> your review -> verified GEDCOM -> static HTML
```

GedFire works only with local files and makes no network requests. Your chosen
AI client controls where tool results are processed.

Why not just let the agent edit the file? GEDCOM looks like plain text, but
the level hierarchy, cross-record pointers, and continuation rules are easy to
get wrong — agents asked to rewrite raw GEDCOM tend to produce broken levels,
dangling references, or files that no longer validate. GedFire instead applies
typed operations to a parsed document and refuses to write anything invalid,
so a valid input file stays valid. As a bonus, the agent doesn't have to read
and reproduce whole GEDCOM records, which saves a lot of tokens.

**[Browse a live generated family-page site →](https://morrillonline.github.io/GedFire/)**
*Generated from the synthetic family in [`docs/demo`](docs/demo), rebuilt by
CI on every push. Facts retain their source citations, available as
hover-popover footnotes in the HTML.*

## MCP server

GedFire also runs as a Model Context Protocol server, so clients including
Claude Desktop, Claude Code, Cursor, Windsurf, Gemini CLI, and Codex can query
your GEDCOM directly, in conversation, instead of shelling out to the CLI:

```powershell
gedfire mcp --input family.ged
```

Install the [global .NET tool](#install-as-a-net-tool) before configuring a
client. The command above starts a long-running stdio server, so waiting
silently for a client connection is normal.

Most MCP clients accept the same local stdio server entry. Add this block to
the client's MCP configuration, replacing the GEDCOM path with an absolute
path:

```json
{
  "mcpServers": {
    "gedfire": {
      "command": "gedfire",
      "args": [
        "mcp",
        "--input",
        "/absolute/path/to/family.ged"
      ]
    }
  }
}
```

Use that `mcpServers` entry in the location your client supports:

| Client | Configuration |
|---|---|
| Claude Desktop | Open **Settings → Developer → Edit Config** and add it to `claude_desktop_config.json`. |
| Claude Code | Add it to `.mcp.json` in the project root, or run `claude mcp add --scope project --transport stdio gedfire -- gedfire mcp --input <absolute-path>`. |
| Cursor | Add it to `.cursor/mcp.json` for the project or the client's global `mcp.json`. |
| Windsurf | Add it to `~/.codeium/windsurf/mcp_config.json`. |
| Gemini CLI | Add it to `.gemini/settings.json` for the project or `~/.gemini/settings.json`. |

Codex uses TOML instead of the JSON wrapper above. Add this to
`.codex/config.toml` in the project or `~/.codex/config.toml`:

```toml
[mcp_servers.gedfire]
command = "gedfire"
args = ["mcp", "--input", "/absolute/path/to/family.ged"]
```

On Windows, JSON paths use escaped backslashes such as
`C:\\Users\\me\\family.ged`; forward slashes also work. The `gedfire`
command must be available on the environment `PATH` inherited by the client,
or `command` must contain the absolute path of the executable. GedFire needs
no environment variables, API keys, or other credentials.

Restart or reload the client after changing its configuration, approve the
local server if prompted, and confirm that it discovers `find_person`,
`date_calc`, `get_document_stats`, and `get_record`.

As a smoke test, ask "How many people and families are in this file?" The
client should call `get_document_stats` and report both counts.

The server binds to one document over stdio and exposes four read-only tools.
Three inspect that document; `date_calc` is a pure calculation over its own
arguments and does not read the document. The server also watches the bound
file and reloads automatically if it changes on disk — no restart needed
after editing the GEDCOM outside the client:

| Tool | What it does |
|---|---|
| `date_calc` | Normalize a dual-dated year, add or subtract a genealogical age, or calculate elapsed years/months/days. Uses exact Gregorian dates supplied in the call and never reads or changes the bound document. |
| `find_person` | Resolve a name the agent heard in conversation — "my great-grandfather Fred Morrill" — to scored candidates, a confident match when one exists, and family handoff identifiers. Optional structured hints distinguish birth from death, father from mother, and one marriage from another. Set `maxResults` to an integer from `1` through `20` (default `8`) without changing the matcher's confidence decision. |
| `get_document_stats` | Report person/family counts, the declared GEDCOM version, and the running gedfire version, for a quick orientation before other work. |
| `get_record` | Fetch the full detail of a specific person, family, or source by xref. |

Every one of these four tools also has a one-shot CLI mirror — `find-person`,
`get-record`, `get-document-stats`, and `date-calc` — that runs the same
engine and prints the same JSON without starting a server. See "Command
reference" below.

For example, an MCP client can call `find_person` with:

```json
{
  "query": "Frederick Morrill",
  "hints": {
    "birth": { "year": 1841, "place": "New Hampshire" },
    "parents": { "father": "Wyman Morrill" },
    "spouse": {
      "name": "Sarah Blake",
      "marriage": { "year": 1865, "place": "Maine" }
    }
  },
  "maxResults": 20
}
```

Every hint leaf is optional, but each supplied object must contain at least
one fact. Birth and death places are event-specific; census or otherwise
unclassified places are not hints. Parent names require a known `father` or
`mother` role. All fields under `spouse` describe one marriage and are never
combined across different marriages. Hints rank only people already recalled
by the name query, and missing candidate data is not penalized.

Every result has the same top-level fields: `matchType`,
`confidentMatchXref`, `confidentMatchScore`, `person`, `candidates`,
`suggestions`, `totalMatches`, and `truncated`. Candidates and suggestions
always include `matchScore`. Scores rank evidence within this matcher; they
are not statistical probabilities. Use `matchType` and
`confidentMatchXref` to decide whether the lookup resolved one person.
`maxResults` changes only the returned comparison-list length, never recall,
ranking, or confidence classification.

No tool in this release writes to the file. An agent that finds something
worth adding still produces a JSON changeset for you to review — the same
`apply` workflow described below, not a second, unreviewed path to your
data. GedFire itself makes no network requests and sends no telemetry; the
MCP client you choose is responsible for what it does with tool results.
Every returned xref belongs to the one GEDCOM bound by `--input`; do not
reuse it against another file.

If the server does not start, run `gedfire --version` in a new terminal to
confirm that the installed command is available, then verify the input path
exists. Check the client's MCP status or server log for startup errors. An
absolute executable path in `command` avoids `PATH` differences between a
terminal and a desktop application.

## Why should I use GedFire?

* GedFire keeps the agent's hands off your master file. The agent writes a
  numbered JSON proposal instead of editing the GEDCOM, so nothing changes
  until you've reviewed it.
* GedFire applies changes as typed operations against a parsed document
  rather than raw text edits, so it can't produce broken levels, dangling
  pointers, or a file that no longer validates.
* You can dry-run a proposal with `--dry-run` and then apply all of its
  numbered items or only the ones you agree with.
* Citations stay attached to the claims they support, from the agent's
  finding all the way into the GEDCOM and the generated pages.
* GedFire generates a static family-page site directly from the reviewed
  GEDCOM, so the published pages and the evidence never drift apart.
* Because the agent works with compact operations instead of reading and
  reproducing whole GEDCOM records, the workflow uses considerably fewer
  agent tokens.
* GedFire is also a scriptable GEDCOM 7 validator, a GEDCOM 5.5 converter,
  a GEDZIP packer, and a JSON name-index exporter, so it can anchor a
  genealogy build pipeline on its own.
* GedFire runs as an MCP server (`gedfire mcp`), so agents like Claude
  Desktop or Claude Code can look up people and records in your GEDCOM
  directly, over a typed, read-only protocol, instead of shelling out to
  the CLI.

In other words, use GedFire if you want AI help with your research but final
say over your family history.

## Installation

### Download a release

Download the archive for your platform from
[GitHub Releases](https://github.com/morrillonline/GedFire/releases), extract
it, and run the binary directly:

```powershell
.\GedFire.exe help
```

On Linux and macOS, run `./GedFire help` from the extracted directory.

### Install as a .NET tool

With the .NET 10 SDK installed:

```powershell
dotnet tool install -g gedfire
gedfire --version
```

## Demo

The repository includes a fictional Ash family and a proposed set of findings.
From the repository root, copy the GEDCOM, dry-run the proposal, apply it, and
generate the site:

```powershell
cp docs/demo/ash-whitfield.ged family.ged
gedfire apply --input family.ged --changes docs/demo/proposed-findings.json --items all --dry-run
gedfire apply --input family.ged --changes docs/demo/proposed-findings.json --items all
gedfire generate --input family.ged --output-dir site --template docs/demo/template.html
```

The dry run confirms exactly what would be accepted:

```text
validation OK (3 ops, items 1,2)
```

The real apply reports each approved operation and verifies the written file:

```text
applied: createOrUpdateSource @S00003@: created
applied: createOrUpdateVital DEAT on @I2@: updated (cited @S00003@)
applied: item 1: applied (@I2@ Mercy Whitfield -> cite her death, currently uncited)
...
verify OK: round-trip byte-stable, pointers resolve, deltas {SOUR +1}
```

Open `site/index.html` to browse the generated family pages. The input family,
records, places, and sources are entirely fictional.

## Safety model

- `--dry-run` validates selected items without touching the GEDCOM.
- You can apply all numbered items or just the ones you accept.
- If any operation fails validation, nothing is written.
- A path-based `apply` holds an exclusive file lock from its initial read
  through validation and verified write, preventing two GedFire writers from
  racing to allocate the same record identity.
- Before creating a person, validation uses the same identity matcher as
  `find_person` and rejects a high-confidence duplicate. Creating families,
  sources, and media does not use person duplicate detection.
- Multiple marriages between the same people are allowed, but creating a
  second marriage to the same person on the same exact date is rejected.
- After a successful write, the file is reparsed and checked: the round trip
  must be byte-stable, pointers must resolve, and record counts must change by
  exactly the expected amounts.
- GedFire works on local files and sends no family data or telemetry anywhere.

## How changesets work

Suppose an agent finds a death certificate for Mercy Whitfield, whose death is
already recorded but uncited. Instead of editing the GEDCOM, the agent proposes
a source and a numbered claim in JSON:

```jsonc
{
  "proposal": "mercy-whitfield-death-record",
  "newSources": [
    { "xref": "@NewSource1@", "ops": [
      { "op": "createOrUpdateSource", "xref": "@NewSource1@",
        "auth": "New Hampshire Bureau of Vital Records (fictional)",
        "title": "Death certificate, Mercy (Whitfield) Ash, 1870",
        "accessed": "2026-07-22" } ] }
  ],
  "items": [
    { "item": 1,
      "target": "@I2@ Mercy Whitfield -> cite her death, currently uncited",
      "ops": [
        { "op": "createOrUpdateVital", "record": "@I2@", "fact": "DEAT",
          "value": { "date": "19 JAN 1870", "place": "Salisbury, Hartwell County, New Hampshire" },
          "citation": { "source": "@NewSource1@", "page": "cert. 1870-0114",
                        "dataText": "Mercy Ash, widow of Nathaniel, died 19 Jan 1870, aged 75",
                        "quay": 2 } } ] }
  ]
}
```

New records use reserved `@New<token>@` placeholders rather than
caller-selected GEDCOM xrefs. The token can contain letters, digits, and
underscores. Its first creating operation fixes the record kind, and every
later use of that placeholder in the changeset resolves to the same real
xref. Here, `apply` allocates `@S00003@` for `@NewSource1@`, creates the
source once, and resolves the citation to it. Library callers receive the
complete placeholder-to-xref map in `ApplyResult.MintedXrefs`; the CLI names
the minted xrefs in its operation log.

Creating a new person is additionally checked against existing and other
planned people using the `find_person` matcher. A high-confidence match is a
validation error, so an already-applied person-creation proposal cannot
silently create another copy on retry. Ambiguous candidates do not block a
reviewed creation.

The complete demo proposal adds a second item as well. More examples covering
relationships, notes, media, deletion, and person merges live in
[`GedCore.Tests/TestData/Changesets`](GedCore.Tests/TestData/Changesets).

### Date arithmetic

`date-calc` performs genealogical calendar arithmetic without reading a
GEDCOM file:

```powershell
gedfire date-calc --op normalize --date "11 FEB 1691/2"
gedfire date-calc --op add --date "27 SEP 1777" --age "63y 4m 2d"
gedfire date-calc --op sub --date "29 JAN 1841" --age "63y 4m 2d"
gedfire date-calc --op diff --from "27 SEP 1777" --to "29 JAN 1841"
```

The commands print `11 FEB 1692`, `29 JAN 1841`, `27 SEP 1777`, and
`63y 4m 2d`, respectively. Arithmetic requires exact Gregorian dates;
qualified, partial, BCE, and non-Gregorian dates are rejected rather than
having uncertainty invented or discarded. GEDCOM reading, writing, and HTML
generation continue to preserve original date text such as `1860`, `BEF
1860`, and dual dates.

The MCP server exposes the same operations through `date_calc`. Its
`operation` is `normalize`, `add`, `sub`, or `diff`; the advertised schema
describes which fields each operation requires. For example:

```json
{
  "operation": "diff",
  "from": "27 SEP 1777",
  "to": "29 JAN 1841"
}
```

The result is `{ "operation": "diff", "date": null, "age": "63y 4m 2d" }`.
The other operations return the canonical result in `date` and set `age` to
`null`.

## Command reference

| Command | What it does |
|---|---|
| `create` | Create a seeded GEDCOM 7 document. |
| `upgrade` | Upgrade a GEDCOM 5.5 file to GEDCOM 7. |
| `downgrade` | Write a GEDCOM 7 document in GEDCOM 5.5 format. |
| `validate` | Report GEDCOM 7 conformance diagnostics. |
| `apply` | Validate and apply selected JSON changeset items. |
| `generate` | Generate static family-page HTML, with optional media staging. |
| `export-index` | Export a JSON person-name index. |
| `select-targets` | Detect research gaps for given surnames, score them, and draw a self-contained `wanted.json` pack. |
| `date-calc` | Normalize dual-dated years, add or subtract a genealogical age, or calculate elapsed years/months/days. |
| `mcp` | Start the read-only stdio MCP server bound to one GEDCOM document. Watches the file and reloads if it changes on disk. |
| `find-person` | One-shot mirror of the mcp server's `find_person` tool: resolve a name to scored candidates or a confident match. |
| `get-record` | One-shot mirror of the mcp server's `get_record` tool: fetch a person, family, or source by xref. |
| `get-document-stats` | One-shot mirror of the mcp server's `get_document_stats` tool: person/family counts, GEDCOM version, gedfire version. |
| `pack` | Create a GEDZIP archive from GEDCOM and referenced media. |
| `unpack` | Extract a GEDZIP archive. |

Run `gedfire help` for syntax, then `gedfire <command>` with the options above.

## When to use it

Use GedFire if you want an AI agent helping with family history research
without handing it the master file, if you need a scriptable GEDCOM validation
or publishing pipeline, or if you want your published pages to be reproducible
from the GEDCOM itself. It's a data-processing tool — not a graphical
family-tree editor, a research provider, or a hosted genealogy service.

## Roadmap

- **GEDCOM interoperability report** — the next patch release will add a
  read-only `gedfire audit` command. It will summarize GEDCOM 7 conformance,
  declared and undeclared extensions, unresolved pointers, and GEDZIP/media
  sharing readiness so researchers can understand a file before sending it to
  another tool or collaborator.
- **MCP write support** — the current `gedfire mcp` tools (`find_person`,
  `get_document_stats`, `get_record`) are read-only by design. `validate_changeset`/
  `apply_changeset` tools are planned next, extending the MCP server through
  the same reviewed changeset-and-approval path the CLI already enforces,
  rather than opening a second way to mutate a GEDCOM.
- **Agent skills** — companion Claude Code skills for the full research
  workflow (evidence grading, identity correlation, record harvesting, and
  driving GedFire safely) are being prepared for publication as a separate
  repository.
- **`GedCore` library package** — publish the parser/apply engine on NuGet for
  embedding in other .NET projects, if there is interest.

GedFire is newly public — if you're interested in the agent skills, watch or
star the repo to follow along.

## Building from source

GedFire requires the .NET 10 SDK.

```powershell
git clone https://github.com/morrillonline/GedFire.git
cd GedFire
dotnet build --nologo
dotnet test --nologo
.\GedFire\bin\Debug\net10.0\GedFire.exe help
```

The solution contains `GedCore` (the GEDCOM engine), `GedFire` (the CLI), and
`GedCore.Tests` (the test suite).

Versions use `4.0.<build>`, beginning with `4.0.1`. Local builds default to
build `1`; CI supplies its increasing workflow run number, and release builds
take the build number from their `v4.0.<build>` tag. To reproduce another build
locally, pass `-p:BuildNumber=<build>` to `dotnet build`, `test`, or `publish`.

## Standards

GedFire is an independent implementation designed to work with
[FamilySearch GEDCOM 7](https://gedcom.io/), the current GEDCOM specification
stewarded by FamilySearch. It is not affiliated with or endorsed by FamilySearch
or The Church of Jesus Christ of Latter-day Saints.

For specification questions, proposed standard changes, and registered
extensions, see the [official GEDCOM repository](https://github.com/FamilySearch/GEDCOM)
and [GEDCOM registries](https://github.com/FamilySearch/GEDCOM-registries).

## Contributing

Bug reports and pull requests are welcome — edge-case GEDCOM fixtures
especially. Please don't include private family data in an issue, fixture, or
pull request, and run `dotnet test --nologo` before opening a PR.

## License

GedFire is released under the [MIT License](LICENSE).