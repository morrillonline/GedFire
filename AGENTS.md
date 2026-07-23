# GedFire Agent Instructions

GedFire is an open-source .NET command-line tool for AI-assisted family-history research, developed publicly on GitHub and released under the MIT License. It keeps AI-generated findings in reviewable JSON changesets and applies approved operations through a validated GEDCOM document model.

## Repository Layout

- `GedCore/` contains the GEDCOM document model, parsers, formatters, conformance checks, GEDZIP support, and changeset application engine.
- `GedFire/` contains the `gedfire` CLI, static-site generator, exporters, and template lookup.
- `GedCore.Tests/` contains the xUnit test suite and synthetic GEDCOM and changeset fixtures.
- `docs/demo/` contains the fictional data used by the README walkthrough.

Keep domain behavior in `GedCore` when it does not depend on command-line or site-generation concerns. Keep argument parsing and user-facing command behavior in `GedFire`.

## Privacy And Test Data

- Never add real or private family-history data to source control, tests, issues, logs, or generated artifacts.
- Use synthetic people, places, sources, media, and identifiers in fixtures and examples.
- Do not introduce network transmission of user data without an explicit, documented product decision, because GedFire is a local-file tool that sends no family data or telemetry.

## Build And Test

Use the .NET 10 SDK, and run commands from the repository root.

```powershell
dotnet build --nologo
dotnet test --nologo
```

During iteration, run the narrowest relevant tests, for example:

```powershell
dotnet test --nologo --filter FullyQualifiedName~ApplyTests
```

Before completing a change, run the affected tests and then the full suite when the change touches shared parsing, formatting, validation, changeset application, or command-line behavior. When changing the CLI, also exercise the affected command against synthetic input. Prefer `--dry-run` for changeset scenarios.

- Name tests `<Action>_<Condition>_<Expected>` and keep each test class focused on one domain.
- Test only through `public` types and members of the assembly under test. `InternalsVisibleTo` is strictly prohibited; if a behavior cannot be reached from the public surface, then you should wonder whether it has any purpose at all.
- Build apply tests on `ApplyTestBase` and `ChangesetFixtures`. Changeset fixtures are embedded resources under `GedCore.Tests/TestData/Changesets/`, not loose files. Put reusable synthetic GEDCOM fixtures under `GedCore.Tests/TestData/`.
- Any change to parsers, formatters, or `Ged70Upgrader` must pass `RoundTripTests`, which require byte-stable round trips including the 5.5 → 7.0 → 5.5 cycle.

## Engineering Conventions

- Prefer existing parsers, document APIs, and operation types over editing GEDCOM or changeset JSON as raw text.
- Read GEDCOM input through `GedReader`, which detects the version and dispatches to the 5.5 or 7.0 parser. Do not call a version-specific parser directly on input of unknown version.
- Call `GedDocument.RebuildXrefIndex()` after inserting or removing level-0 records.
- Mutate record structure through `NodeBuilder` helpers rather than raw string edits. Internal text uses `\n` (via `NodeBuilder.NormalizeText`); formatters emit CRLF. `CONT`/`CONC` lines are preserved as child records, so naive value edits break byte-stable round trips.
- Never change formatter output conventions: `Ged70Formatter` writes UTF-8 with BOM, `Ged55Formatter` writes the detected legacy encoding (Windows-1252 default), both with CRLF line endings.
- Treat `@VOID@` as a valid, intentionally unresolvable pointer. It is distinct from a dangling xref, which is an error.
- `CHAN` stamps are applied only by `ChangesetApplier`, only to level-0 records actually mutated, and only after all operations succeed. Do not stamp elsewhere.
- To add a changeset operation: create a `ChangeOp` subclass in `GedCore/Apply/Ops/` with `Validate`, `Apply`, and a static `Read` factory; register it in the `ChangeOp.ReadOp()` switch (registration is explicit, not reflective); update the unsupported-op error message; and add a JSON fixture plus tests. Classify any new fact type as single-valued or repeatable.
- To add a CLI command: add a `Run<Command>` method and a case in the `Program.cs` switch, parse arguments with `CommandLine.Parse`, return exit code 0 on success and 1 on any failure, write errors to standard error, and write informational output to standard output.
- Product version numbers live in `Directory.Build.props`, not in individual project files.

## Compatibility And Safety

- Refer to the [FamilySearch GEDCOM 7 specification](https://gedcom.io/specifications/FamilySearchGEDCOMv7.html) for format rules. Account for earlier GEDCOM versions where relevant.
- Preserve unknown extension tags and supported encodings unless the operation explicitly changes them.
- Preserve GEDCOM record xrefs (such as `@I1@`) across all edits. Never renumber or reuse them; remove them only through an approved delete or merge.
- Keep command-line syntax and changeset JSON backward compatible unless a breaking change is explicitly requested and documented, because they are public interfaces.
- Keep changeset application transactional by leaving the GEDCOM unchanged when validation fails.
- Preserve post-apply checks for round-trip stability, pointer resolution, and expected record-count deltas.
- Treat GEDCOM cross-record pointers, level hierarchy, continuation records, at-sign escaping, media paths, and GEDZIP paths as structured data with security and integrity implications.

## Documentation

Update `README.md`, command help, examples, and fixtures when user-visible commands or changeset behavior changes. Keep examples cross-platform where practical and ensure all example family data remains fictional.

## Git And Pull Requests

- Do not discard or rewrite changes made by other contributors.
- Inspect `git status` and the intended diff before proposing a commit.
- Do not commit, push, merge, or create branches unless explicitly requested.
- Explain the behavior change, compatibility impact, and validation performed in pull requests.
- Ensure CI passes before merging.

## Maintainer Check-In Procedure

Run this procedure only when explicitly requested by the repository maintainer.

1. Review `git status` and the intended diff. Propose a commit message and wait for explicit approval of that exact message before committing.
2. Create and check out a descriptive working branch before committing, because `main` is protected. Never commit directly on local `main`:
   `git checkout -b <branch>`
3. Commit the approved changes, then push with upstream tracking:
   `git push -u origin <branch>`
4. If the push is rejected because the remote advanced, fetch and rebase the working branch on its remote base. Never force-push.
5. Open the pull request with GitHub CLI:
   `gh pr create --base main --title "<title>" --body "<summary>"`
6. Unless another merge method is requested, merge with a merge commit and remove the remote branch:
   `gh pr merge <number> --merge --delete-branch`
7. Sync and clean up the local repository:
   `git checkout main`
   `git pull --ff-only`
   `git branch -d <branch>`
   `git fetch --prune`

