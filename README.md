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

Everything runs on local files. No family data leaves your machine.

Why not just let the agent edit the file? GEDCOM looks like plain text, but
the level hierarchy, cross-record pointers, and continuation rules are easy to
get wrong — agents asked to rewrite raw GEDCOM tend to produce broken levels,
dangling references, or files that no longer validate. GedFire instead applies
typed operations to a parsed document and refuses to write anything invalid,
so a valid input file stays valid. As a bonus, the agent doesn't have to read
and reproduce whole GEDCOM records, which saves a lot of tokens.

![A generated family page: sourced facts with hover-popover footnotes, biography
prose, and linked children](docs/family-page.png)

*Generated from the synthetic family in [`docs/demo`](docs/demo). Facts retain
their source citations, available as hover-popover footnotes in the HTML.*

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
applied: createOrUpdateSource @S3@: created
applied: createOrUpdateVital DEAT on @I2@: updated (cited @S3@)
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
    { "xref": "@S3@", "ops": [
      { "op": "createOrUpdateSource", "xref": "@S3@",
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
          "citation": { "source": "@S3@", "page": "cert. 1870-0114",
                        "dataText": "Mercy Ash, widow of Nathaniel, died 19 Jan 1870, aged 75",
                        "quay": 2 } } ] }
  ]
}
```

The complete demo proposal adds a second item as well. More examples covering
relationships, notes, media, deletion, and person merges live in
[`GedCore.Tests/TestData/Changesets`](GedCore.Tests/TestData/Changesets).

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
- **MCP server** — a `gedfire mcp` verb exposing the same engine as typed
  Model Context Protocol tools over stdio, for agent environments without shell
  access (Claude Desktop and friends). Planned after the interoperability report.
- **Agent skills** — companion Claude Code skills for the full research
  workflow (evidence grading, identity correlation, record harvesting, and
  driving GedFire safely) are being prepared for publication as a separate
  repository.
- **`GedCore` library package** — publish the parser/apply engine on NuGet for
  embedding in other .NET projects, if there is interest.

GedFire is newly public — if you're interested in the MCP server or the agent
skills, watch or star the repo to follow along.

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