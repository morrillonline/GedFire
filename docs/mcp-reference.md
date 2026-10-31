# MCP reference

This reference explains GedFire's tool behavior and changeset semantics.
Start with the [README](../README.md) for installation, client configuration,
and the safety model. Use the MCP schemas for exact parameter names and types;
call `describe_changeset_ops` for the operation dialect.

All xrefs belong to the server's bound GEDCOM. Never carry them into another
document. Only `apply_changeset` modifies that GEDCOM. Draft and report tools
can write separate files, including when the server uses `--read-only`.

## Connection mode

GedFire supports local MCP mode only, over stdio. The MCP client launches
`gedfire mcp` as a local child process and communicates through its standard
input/output. Select the client's local/stdio server type and provide a
command and arguments, not a URL. GedFire does not expose an HTTP, Streamable
HTTP or SSE transport and does not listen on a network port. The client may
still use a cloud-hosted AI model; local mode describes the MCP transport.

## Command-line tool interface

All MCP tools can also run as one-shot CLI calls. The names, input/output
schemas, argument validation, handlers and document safety checks are shared:

```text
gedfire tools list
gedfire tools describe match_record
gedfire tools call match_record --input family.ged --args request.json --out result.json
```

`list` returns a JSON object with a `tools` array containing MCP tool metadata,
including input/output schemas and behavior annotations. `describe` returns
one tool's metadata. Neither opens a GEDCOM file.

`call` accepts the tool's unchanged MCP arguments object from `--args <file>`
or stdin with `--args -`; omitting `--args` supplies `{}`. All tools require
`--input` except `date_calc` and `describe_changeset_ops`. Tool names and JSON
field names are case-sensitive. Relative paths, including paths inside the
arguments object, resolve against the process's working directory.

`--policy <file>`, `--read-only` and `--enforce-privacy` use the same policy,
apply gate and privacy filtering as the MCP server. Read-only does not prevent
separate draft, proposal or report files. No resident MCP server, protocol
handshake or network connection is needed.

Successful invocations return the MCP `structuredContent` payload as JSON,
without the protocol envelope or duplicate text fallback. Tool errors return
the MCP error result with `isError` and `content`. JSON goes to stdout, or to
a UTF-8 file without a BOM when `--out <file>` is supplied; `--out -` explicitly
selects stdout. Existing output files are refused before the tool executes.
CLI setup, argument-file or file-access failures may produce no JSON result.
Diagnostics go to stderr.

Exit code 0 means success. Exit code 1 means a CLI/tool error or a result with
`success: false` or `passed: false`; failed-check results remain available as
JSON. Warnings alone do not change the exit code unless the tool's own options
make them fail the check.

CLI `--out` saves the returned result, not the changeset or proposal itself.
Tool arguments such as `outPath`, `reportPath`, `overwrite` and `verbose` retain
their MCP meanings. For example, when a tool writes a report and returns a
digest, CLI `--out` receives that digest; request `verbose: true` when the tool
supports it to return the complete result too.

## Lookups

### Hinted lookups

Call `find_person` with a name and optional structured hints:

```json
{
  "query": "Frederick Ash",
  "hints": {
    "birth": { "year": 1841, "place": "New Hampshire" },
    "parents": { "father": "Wyman Ash" },
    "spouse": {
      "name": "Sarah Whitfield",
      "marriage": { "year": 1865, "place": "Maine" }
    }
  },
  "maxResults": 20
}
```

- Each supplied hint object needs at least one fact. Birth and death places
  describe those events, not census or unclassified locations.
- Parent names need a known father or mother role. All spouse hints describe
  one marriage; the matcher never combines different marriages.
- `sex` (`M` or `F`) lowers the rank of a differing recorded sex. Missing sex
  neither helps nor penalizes a candidate. Neither case removes a candidate.
- Hints rank people the name query recalls. Missing candidate data incurs no
  penalty. `Unknown` name parts do not require a literal match to that word.
- `maxResults` accepts 1-20, default 8. It changes the returned list length,
  not recall, ranking, or the confidence decision.

Results carry `matchType`, `confidentMatchXref`, `confidentMatchScore`, `person`,
`candidates`, `suggestions`, `totalMatches`, and `truncated`. Candidates and
suggestions include `matchScore`. Scores are not statistical probabilities.
Use `matchType` and `confidentMatchXref` to inspect whether the tool resolved
one person. `single` requires a `matchScore` of at least 90, including when
only one candidate is recalled. With multiple candidates, the leader must
also score at least 10 points above the runner-up. Otherwise the result is
`candidates`, with the recalled matches retained and `confidentMatchXref`,
`confidentMatchScore`, and `person` all null. Classification uses the full
recall set before `maxResults` is applied.

When the query or candidate has an unknown surname, `matchScore` is capped at
89 unless a birth, death, or marriage year/place hint agrees with recorded
data. Names and sex alone cannot produce `single`; missing event data and
disagreeing event hints do not lift the cap.

`find_family` searches through a named relative. `relation` is `spouse`
(find the named person's partner), `child` (find their parents), or `parent`
(find their children). Optional birth and death hints describe the person
sought. Results include the connecting family xref and matched relative.

`find_source` accepts case-insensitive title, author, and URL substrings.
Supply at least one; all supplied filters must match. An empty `sources`
array means no match. A matching source returns `xref`, `title`, `author`,
and `url` (null when absent).

### Records and lists

`get_record` returns a person, family, or source. Citations include the source
xref, page, quoted `dataText`, and `quay`. `includeSources` adds the cited
source text. Source lookups include `citedBy`.

Individual events such as baptism and burial appear in `otherEvents`.
A burial without a death also supplies `approximateDeath`; `death` stays
null. Families carry their own `citations`.

`get_records` accepts up to 500 xrefs and preserves request order. With
`includeSources`, it returns one top-level `sources` array. `fields` selects
fields while retaining `recordType` and `xref`. An unknown field is an error;
a missing xref yields a `not_found` record in its slot.

`list_people` sorts by surname, given name, and xref. It supports surname
filters and `pageSize` 1-2000 (default 500). Pass `nextCursor` back as `cursor`
to continue. Death summaries use burial year when no death exists.

`list_unanchored_people` finds people without a dated event, place, or placed
parent, spouse, or child. Ties only to unplaced people do not count as anchors.
It reports fact counts and unplaced relatives, and omits privacy-hidden people.

## Correlation and research targets

### Whole-record matching

`match_record` compares a household, obituary, probate, or other multi-person
record against tree family subgraphs. Supply:

- `persons`: roles, names, sex, birth year or age, and birth place.
- `relationships`: person-index pairs using `spouse-of`, `child-of`,
  `parent-of`, or `sibling-of`.
- `event`: the record's date and place.
- Optional `focus`: the person whose result you want.

Results rank candidates by name, lifespan, kin overlap, and place. Each
person's `sameNameSurvey` lists the same-name candidates considered and the
reason each non-leader lost: incompatible dates, wrong spouse, impossible age,
or a lower score. The verdict is `single`, `candidates`, or `none`.
Unlike `find_person`, `match_record` still returns `single` for a lone viable
candidate; with multiple viable candidates it requires a composite score of
at least 90 and a lead of at least 10 points.

A person matching nobody receives `potentialNewPerson` and any near-miss
`possibleDuplicates`, not a forced match. The same inputs yield the same
result. The survey covers candidates the matcher considers, not every
historical person who might bear that name.

### Research briefs

`research_brief` combines the person's full record with:

- `kin`: parents, siblings, spouses with marriage details, and children.
- `gaps`: the same gaps `select_targets` reports.
- `possibleDuplicateOf`: up to three scored candidates, plus the full count.
- `plausibility`: standing issues against the person or their families.
- `notes`: person and family note texts.

Optional `cardType` adds tailored `emphasis`. `new-spouse` highlights children
with an unknown other parent. Enrichment types highlight dated facts that
bound a missing vital. Missing kin, notes, or duplicates produce empty arrays.

### Target selection

`select_targets` draws `count` gaps for study `surnames`, returning a
`wanted.json`-shaped result without the GEDCOM path. The clock seeds the draw.
Alternatively, pass `xrefs` instead of count and surnames to score specified
people deterministically. This returns every eligible gap, names people without cards
in `xrefsWithoutGaps`, and sets `draw.seed` to null. An unknown xref is an error.

- An `enrich-person` card covers all inexact birth, marriage, and death dates
  and places for one person, plus any such fact with an exact date or place but
  no citation. `nominalPoints` counts missing dates, places, and citations;
  `score` adds a difficulty bonus once.
- A `BET ... AND ...` date no wider than 12 months, cited at QUAY 2 or higher,
  is not a missing date. Open `BEF`/`AFT` dates and `ABT`, `CAL`, and `EST`
  remain gaps.
- An exact burial date and place close a death gap when no exact death exists.
  The summary's `died` falls back to burial.
- New-parent and new-spouse cards are withheld for bare-bones people: none of
  their own birth, christening/baptism, death, or burial events has an exact date
  (per `ExactnessRules.IsExactDate`) or a non-empty place. Qualified years such
  as `ABT 1872`, unqualified years, notes, citations, and relatives do not anchor
  them; any non-empty place does, even if it still needs enrichment.
- New-child cards are withheld only if both spouses are bare-bones and the
  marriage has neither an exact date nor a place. In surname mode they also
  require a husband with a study surname; xrefs mode has no surname requirement.
- `suppressedCards` lists `{xref, name, cardType, reason}` for otherwise eligible
  cards withheld by this gate, once per childless family for New child. These
  use the same subject xref, name, and card-type labels as the withheld card.
  Personal reasons are `bare-bones subject — enrich first`; family reasons are
  `bare-bones family (both spouses and marriage lack anchors) — enrich first`.
  The list is empty when nothing was suppressed and is excluded from
  `totalCandidates`. Enrich-person cards, points, privacy, died-young, and surname
  rules are unchanged. No state is kept: landing an anchor restores relationship
  cards on the next selection, without restricting extra-credit collection.
- Duplicate hints use the GEN301 plausibility test. Results include up to
  three candidates plus `possibleDuplicateCount`; duplicates do not prevent a draw.
- An `Unknown` name part needs an agreeing parent or spouse name for duplicate
  pairing. An agreeing relative can support the match without dates; the
  person's own name and sex alone cannot.
- GEN301 never pairs two people whose recorded births (or recorded deaths) sit
  more than twenty years apart, or whose given names share no token, initial,
  or documented nickname.

To price an enrich-person card during validation, use `gap: "enrich-fact"`,
`points: 1`, and `bonus` equal to `score - nominalPoints`.

## Drafting a changeset

`draft_changeset` compiles findings. Supply exactly one input form:

| Input | Content |
|---|---|
| `findings` | An array of draft finding entries. |
| `inputPath` | A JSON file containing `{"findings": [...]}`. |
| `findingPaths` | Correlated finding files, each with a complete entry under `draft`. |

The compiler rejects unrecognized fields by path. File-based errors name the
file. A missing `draft`, parked finding, or non-empty `draftGaps` prevents
drafting.

### Finding fields

Each entry carries `id`, a verbatim `extract`, `source`, `target`, and `facts`.
It may also carry `people`, `relations`, `notes`, and an identity `argument`.

| Field | Content |
|---|---|
| `source` | `name`, `locator`, `class`, `information`; optional `author`, `url`, `accessed`. |
| `target` | `xref`; optional `card` and `name`. |
| Each fact | `record`, `fact`, per-fact `quay`; date/place or text `value`; optional `excerpt`, `class`, `information`. |

Use the tool schema for the full contract. The compiler returns the changeset,
`decisions`, and `errors`. It rejects a target name that differs from the
recorded name and prose that violates policy. Inspect `open` decisions before
proceeding.

### Sources and quality grades

Findings for one target become one item. The compiler reuses existing sources
or creates one per collection and author. It merges extracts by source, page,
and grade into citation `dataText`, and copies the locator into `page`.

Source-title matching ignores case and punctuation. It also drops a trailing
` - <person name>` segment without digits. Keep record-specific details in
the locator; different year ranges and record types remain separate sources.
The `decisions` list reports source reuse and creation.

The compiler rejects a citation grade above these ceilings:

| Source class | Primary information | Other information |
|---|---|---|
| Original or image copy | 3 | 2 |
| Derivative | 2 | 1 |
| Authored | 1 | 1 |
| User-contributed | 0 | 0 |

These are ceilings, not automatic grades or proof of accuracy. The author
assigns each fact's grade. Policy can require verification notes for low grades.

### Reconciliation

- Repeatable events such as `CENS` and `RESI` retain separate values. A date
  matching an existing event updates that instance; another date adds an event.
- Single-valued facts such as `BIRT`, `DEAT`, `BURI`, and `MARR` reconcile date
  and place separately. Agreeing findings all contribute citations.
- A more precise compatible date or place wins. A specific date supersedes an
  approximate date; the approximate citation stays only if its year fits.
  Otherwise an `open` decision explains its omission.
- Hard bounds (`BEF`, `AFT`, `BET … AND`) narrow one another: `AFT 1795` and
  `BEF 27 SEP 1796` draft `BET 1795 AND 27 SEP 1796`, whether both come from
  findings or one is already on the record.
- A less specific finding adds a citation, not a downgrade. The compiler
  reports a `resolved` decision with `no improvement`.
- Precision decides only between dates that can both be true. When a bound
  excludes the record's date (or the record's bound excludes the finding's
  date), the record's value gives way if it is uncited or graded below the
  finding: the bound is written and the old citations move to a note. Otherwise
  nothing is drafted for the fact and an `open` decision asks the author to
  settle it.
- `ABT`, `CAL` and `EST` have no defined tolerance, so they never replace a
  specific date that is more than a year away; the fact is left `open`. A
  specific finding still replaces such an estimate on the record, with an
  `open` decision asking that both describe the same event.
- Genuine disagreements leave the fact undrafted. An `open` decision lists
  values and findings, recommending by QUAY and then source quality.
- Name reconciliation only expands documented nicknames with matching surname,
  suffix, and given-name count. Other disagreements stay `open`.

The compiler maintains its own standard verification notes, one per fact. A
fact gets a note only when its best citation, in the item or already in the
document, is at or below the policy ceiling; the note names every record the
fact rests on. No note is added when a note on the record already says the fact
awaits verification, however worded. Once stronger
citations lift a named fact above the policy ceiling, it removes that fact
from the note or deletes the note when no low-grade facts remain. It preserves
sources. It leaves other verification-note wording to the author and raises an
`open` decision. An author's explicit note rewrite with `match` takes precedence.

### People, relationships, and notes

New people use `people` entries with placeholder `xref`, `name`, and `sex`
(needed for a partner). Use placeholders such as `@NewI1@` in their facts.
Relations cite the finding at their own `quay`:

| `kind` | Fields |
|---|---|
| `spouse` | `person`, `spouse`, `family`, `quay` |
| `child` | `family`, `child`, `husb`, `wife`, `quay` |
| `parent` | `person`, `role`, `parent`, `quay` |

`husb` and `wife` seed a new family only. Later relations omit those seeds;
the compiler reports `dropped-seed` when it removes them. Marriage facts use
the family placeholder as `record`.

An existing person's `SEX` fact uses a string `value`, such as `"M"` or `"F"`.
Within each item, the compiler places these corrections before relationships,
so role validation uses the corrected sex. Facts on new people follow the
relationships that create them.

Same-sex spouses are supported without changing either person's recorded
`SEX`. Existing `HUSB`/`WIFE` placements are preserved; those legacy tags
identify partner slots, not constraints on the couple's sexes. `GEN403`
warns when a marriage links two people with the same recorded `M` or `F`,
with additional historical-review wording for dates before 2001, the year
of the [first national legalization](https://en.wikipedia.org/wiki/Same-sex_marriage_in_the_Netherlands)
of same-sex civil marriage (Netherlands; effective 1 April 2001). This is a
historical milestone, not a universal legalization date: local law varies.
The warning requests source review, is not an error, and does not block application.

- `spouse` and `child` relations accept `notDuplicateOf`: existing people
  the author has confirmed are not the new person.
- A child relation with `from` moves the child from that existing family into
  `family`. `unlink-child` takes `family` and `child`; `unlink-parent` takes
  `person`, `role`, and optional `family`.
- Removal ops carry no citation, `quay`, or `excerpt`. The draft raises an
  `open` decision requesting a cited explanatory note. It refuses to remove a
  child the family does not list.
- A fact can carry `substructures`, such as an `EVEN` with a `TYPE`.
  Such an event adds an instance rather than matching by date.
- `replacedCitations` chooses `keep`, `drop`, or `moveToNote` for citations on
  a replaced value.
- `citeOnly: true` cites an existing fact; date/place select its instance.
- `replaceHeld: "<reason>"` writes a single-valued fact's date and place over
  the record's, even a more specific or better-cited one. A contradicted value's
  citations move to a note unless `replacedCitations` says otherwise.
- `withdraw: "<reason>"` deletes the record's fact with nothing in its place;
  date/place select the instance, and its citations move to a note. Like a
  removal, it raises an `open` decision unless the finding adds a cited note.
  The reason is shown to the reviewer in the changeset's findings table.
- A note's `match` names the exact text to rewrite. `cites` lists
  `{"finding", "quay", "excerpt"}` entries; omitting `finding` uses its own.
  The compiler joins notes on the same record within a changeset and retains
  every finding's citations.

### Identity arguments and output

An optional `argument` records the author's identity reasoning:

```json
{
  "anchors": ["Born in Oakland in 1850."],
  "identity": "One candidate matches the recorded spouse and parents.",
  "exclusions": [
    {"name": "Harry Ash", "xref": "@I9@", "fact": "died in 1801", "finding": "F-2"}
  ],
  "cautions": "The census gives a birth year of 1851.",
  "difficulty": "medium"
}
```

All argument fields are optional. In correlated finding files, `argument`
sits beside `draft`; in direct entries it sits inside the finding.
GedFire checks the shape and copies the argument, but does not write or
verify its reasoning.

`outPath` writes the changeset to a new file; `overwrite: true` permits
replacement. The changeset embeds evidence under top-level `findings`, and
each operation names its supporting findings. The apply engine ignores this
evidence metadata; the proposal renderer uses it.

## Drafting a package

First compile with `draft_changeset`, then run `validate_changeset` and
`check_plausibility`. Fix inputs and redraft when either check exposes a defect.

Call `draft_package` with:

- `changesetPaths`: finished changesets in the desired numbering order.
- `folder`: an existing output folder.
- `proposal`: the filename prefix.
- Optional `overwrite`: allow replacement.

The tool writes `<folder>/<proposal>.md` and reports its path and changesets.
It validates changesets against the bound document and fails on invalid input.
It does not repair changesets or replace the upstream plausibility check.

Each numbered item presents its cited findings, extracts, source and
information classes, grades, and supplied identity arguments:
*Why him*, *Why not others*, *Conflicts and cautions*, and *Difficulty*.
Difficulty uses the hardest finding; an unspecified difficulty counts as low.
A missing argument renders `(none recorded)` and does not block output.
Package creation therefore does not certify evidentiary completeness or approval.

## Changeset policy file

Pass `--policy <json>` when starting the server. Every key is optional;
unknown keys stop startup. See the [complete example](policy.example.json).

| Key | Rule |
|---|---|
| `bannedPhrases` | Reject case-insensitive phrases in note text or citation pages. |
| `bannedPatterns` | Reject regular-expression matches in note text or citation pages. |
| `verificationNote` | Require a note with `notePrefix` for records receiving facts whose strongest citation is at or below `maxQuay`. |
| `deathPlaceEvidence` | Require a death-place citation whose page or `dataText` matches this regular expression. |

For example, `{"maxQuay": 1, "notePrefix": "needs independent verification:"}`
requires a note identifying the affected record. It can sit on the record,
name the person's given name, or mention marriage for a marriage fact.

Only selected items undergo policy checks. Violations appear as
`policy: item 2: ...`; validation reports errors and apply refuses the changes.
Policy matches text and grades, not the underlying truth of a source.

## Points preview

Pass `cards` to `validate_changeset`, or `cardsPath` pointing to
`{"cards": [...]}`. Each card supplies `xref`, `class`, and `points`, with
optional `gap`, `name`, and `bonus`.

Classes are `enrich-person`, `new-parent`, `new-spouse`, and `new-child`.
Enrichment gaps are `enrich-birth`, `enrich-death`, or `enrich-marriage`,
optionally ending in `-date` or `-place`.

With no gap, or with `enrich-fact`, the card covers any improved fact on the
subject. Each improved date, place, or newly added citation earns `points`;
`bonus` pays once.
A date improves only when it is the first, refines the held date (`1877` to
`14 APR 1877`, `ABT 1877` to `1877`), narrows its bound (`BEF 1800` to
`BEF 1797`), or corrects a date it contradicts. A less specific date or the same
date respelled (`BEFORE 1796` to `BEF 1796`) earns nothing.
`pointsPreview` compares the touched records before and after:

- `closed`: cards with a written improvement or new family relationship.
  `wrote` identifies the fact and improved components, naming a date as
  `first`, `refined`, `narrowed`, or `corrected`.
- `notClosed`: remaining cards with reasons. An identical restatement earns no
  closure.
- `subjectMismatch`: cards left open by a name mismatch. An enrich card detects
  a subject rename; a new-relative card requires the minted person's name to
  match `name` exactly, including case and spelling.
- `total`: points from closed cards.

Card-to-write matching is one-to-one: an applied person closes at most one
new-relative card. An exact `name` match claims the minted person first, the
rest claim by anchor xref, and a card left only with already-claimed people
appears in `notClosed` with the collision named.

Points measure changes, not evidentiary reliability.

## Changeset application

`validate_changeset(changesetPath, items)` previews the selected operations.
`apply_changeset` validates and verifies them before writing. Use
`check_plausibility` alongside validation to inspect introduced chronological,
biological, name, and duplicate issues. It also reports GEN201 when an
operation replaces a date or place with a less specific form of the value the
file holds (a place that is a suffix of the held hierarchy; a year, or a month
and year, for a fuller date inside it) — a warning only, since losing
specificity is not recommended except when correcting an error. A conflicting
or more detailed replacement is not flagged, and nothing is blocked.
`validate_document` checks whole-file
GEDCOM 7 conformance separately; `warningsAsErrors` promotes warnings.

New records use reserved `@New<token>@` placeholders. Tokens contain letters,
digits, and underscores. The first creation fixes the record kind; later
references resolve to the same allocated xref. Library callers receive
`ApplyResult.MintedXrefs`; the CLI logs allocations.

- Person creation rejects high-confidence duplicates, including planned people.
  Family, source, and media creation do not use the person duplicate detector.
- Multiple marriages between the same people are valid; a duplicate marriage
  on the same exact date is not.
- Source creation reuses a matching title and author with no publication,
  ignoring case and extra spaces. This apply-time rule differs from the
  draft compiler's broader title normalization.
- Updating a shared source makes a copy (or reuses an exact updated source).
  Only facts the same item cites point to the copy. The item must cite the fact
  needing the change. `ApplyResult.CopiedSources` reports the mapping.
- A citation page belongs to the citation, not the source. A matching page
  updates; another page adds a citation. With only one citation for that source
  on a fact, the update corrects its page. `deleteCitation` can select a page.
- Relationship provenance never corrects a page: a child, spouse, or parent
  link citing a page the family does not already cite adds a citation beside
  the existing ones, so a link can never overwrite another record's provenance.
- Omit `fact` to edit a family-level citation with `createOrUpdateCitation`
  or `deleteCitation`. Use exact `note` text to address note citations.
- Validation rejects internal ids in stored prose: note text, citation pages
  and extracts, source titles and authors, and merge notes.

See the [synthetic changeset fixtures](../GedCore.Tests/TestData/Changesets)
for examples of relationships, notes, media, deletion, and merges.
Calling apply does not prove that anyone approved the selected items; see
[review permissions](../README.md#what-you-and-your-client-control).

## Result reports

`draft_changeset`, `validate_changeset`, and `apply_changeset` accept
`reportPath`. It saves the full result as UTF-8 indented JSON, including
`reportWrittenTo`, and quiets the tool response to a digest: a draft reports
each item's number, target, and op count plus its errors and decisions counted
by kind; a validation or apply reports the selected items, pass/fail, error and
log counts, deltas, the minted-xref count, and — with cards — the points
preview total. Scripts read the file for the full result; `verbose: true`
returns it inline as well.

The parent folder must exist. Existing files require `overwrite: true`.
A draft's `outPath` and `reportPath` must differ. GedFire checks these paths
before work, including before apply can modify the GEDCOM.

Reports cover successful and failed work, including policy refusal. Bad
arguments or read-only apply refusal return errors without writing a report.
If report writing fails after work finishes, the result records that failure
in `log` (or an `open` draft decision) and sets `reportWrittenTo` to null.
That failure does not undo a successful apply; inspect the returned outcome.

## Date arithmetic

`date_calc` never reads the bound document. Its `operation` is `normalize`,
`add`, `sub`, or `diff`. For example:

```json
{
  "operation": "diff",
  "from": "27 SEP 1777",
  "to": "29 JAN 1841"
}
```

The result is `{"operation": "diff", "date": null, "age": "63y 4m 2d"}`.
Other operations return `date` and set `age` to null. CLI equivalents:

```powershell
gedfire date-calc --op normalize --date "11 FEB 1691/2"
gedfire date-calc --op add --date "27 SEP 1777" --age "63y 4m 2d"
```

These return `11 FEB 1692` and `29 JAN 1841`. Arithmetic accepts exact
Gregorian dates only; it rejects qualified, partial, BCE, and non-Gregorian
dates rather than discarding uncertainty. GEDCOM reading, writing, and site
generation still preserve original date text.
