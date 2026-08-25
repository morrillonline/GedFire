# GedFire Agent Instructions

GedFire is an open-source .NET command-line tool for AI-assisted family-history research, developed publicly on GitHub and released under the MIT License. It keeps AI-generated findings in reviewable JSON changesets and applies approved operations through a validated GEDCOM document model.

## Repository Layout

- `GedCore/` contains the GEDCOM document model, parsers, formatters, conformance checks, GEDZIP support, and changeset application engine.
- `GedFire/` contains the `gedfire` CLI, MCP, static-site generator, exporters, and template lookup.
- `GedCore.Tests/` contains the xUnit test suite and synthetic GEDCOM and changeset fixtures.
- `docs/demo/` contains the fictional data used by the README walkthrough.

Keep domain behavior in `GedCore` when it does not depend on command-line or site-generation concerns. Keep argument parsing and user-facing command behavior in `GedFire`.

## Privacy And Test Data

- Never add a real person or identifier to source control, tests, issues, logs, or generated artifacts.
- Use synthetic people, places, sources, media, and identifiers in fixtures and examples.
- This application should never send information from the current GEDCOM file outbound to a third party.

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

Before declaring that a change is ready to commit, run the full test suite to ensure no regressions.

## Engineering Conventions

- Prefer existing third party packages over reinventing the wheel.
- Each class should have one well defined responsibility. 
- Avoid methods with high cyclomatic complexity. Introduce component methods. Replace a long argument list with one or two objects that carry the information.
- Use unit testing to prove correctness. Adhere to the principle of one unit test class per production class, and isolate the class under test from other classes as much as possible.
- Treat `@VOID@` as a valid, intentionally unresolvable pointer. It is distinct from a dangling xref, which is an error.
- `CHAN` stamps are applied only by `ChangesetApplier`, only to level-0 records actually mutated, and only after all operations succeed. Do not stamp elsewhere.
- Avoid testing with files. Production code should expose public methods that take a stream, and tests should exercise those stream overloads; only the very highest level of the call stack takes a file path and opens the stream for reading or writing.
- Test only through `public` types and members of the assembly under test. `InternalsVisibleTo` is strictly prohibited; if a behavior cannot be reached from the public surface, then you should wonder whether it has any purpose at all.

## Code Comments

- The best comment is no comment. Strive to make code simple, straightforward, and self explanatory.
- Comments must be short, no more than a line or two, and explain things that are not self explanatory. Help the future programmer to notice relationships and constraints.
- Never reference design documents or recount the origin story of how the next line came to exist. 

## Compatibility And Safety

- GEDCOM and GEDZIP files must conform to standards. Refer to the [FamilySearch GEDCOM 7 specification](https://gedcom.io/specifications/FamilySearchGEDCOMv7.html) for the latest standard. 
- Never take risks when modifying a GEDCOM file. Corrupting a file must be avoided. Use the validate routine when a corrupted file is suspected.
- GEDCOM extensions are allowed by the standard and so should be supported by this system.
- Preserve GEDCOM record xrefs (such as `@I1@`) across all edits. Never renumber or reuse them; remove them only through an approved delete or merge.
- With fixes and enhancements, make heavy use of unit tests to ensure correctness and no regressions.
- Always respect the MCP read-only option when enabled.
- Always respect the MCP enforce-privacy option when enabled.

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

**PR merge:**
- Merging to `main` only runs CI's `test` job — no release happens automatically.
- Merge commit message stays terse: "Merge pull request #N from `<branch>`" + PR title, nothing more. Detail lives in the feature commit underneath it, not the merge commit.

**New tag / release:**
- Version = `VersionMajor.VersionMinor` (`Directory.Build.props`) + `BuildNumber`. `BuildNumber` isn't stored in the file — CI reads it from the tag's own last segment.
- Routine release: tag `vX.Y.<next-number>` on `main`, push it. No file edit needed unless deliberately bumping Major/Minor.
- Pushing a `v*` tag triggers CI's `release` job: retest, verify tag == computed project version, publish self-contained binaries (win-x64, linux-x64, osx-x64, osx-arm64), pack + push the NuGet tool, create the GitHub release with auto-generated notes.
- Never push a `v*` tag unless the maintainer explicitly requests a release.

