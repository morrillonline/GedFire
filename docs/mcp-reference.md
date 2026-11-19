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
`--input` except `date_calc`, `describe_changeset_ops`, `describe_schemas` and
`validate_finding`, which without `--input` checks every rule that needs no document. Tool names and JSON
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

### Records

`get_record` returns a person, family, or source. Citations include the source
xref, page, quoted `dataText`, `quay`, and the author's `commentary`; a
citation of `kind` `commentary` cites no source. `includeSources` adds the cited
source text. Source lookups include `citedBy`.

Individual events such as baptism and burial appear in `otherEvents`.
A burial without a death also supplies `approximateDeath`; `death` stays
null. Families carry their own `citations`.

`get_records` accepts up to 500 xrefs and preserves request order. With
`includeSources`, it returns one top-level `sources` array. `fields` selects
fields while retaining `recordType` and `xref`. An unknown field is an error;
a missing xref yields a `not_found` record in its slot.

## Correlation and research targets

### Whole-record matching

`match_record` compares a household, obituary, probate, or other multi-person
record against tree family subgraphs. Supply:

- `persons`: roles, names, sex, birth year or age, and birth place. Each
  person can also carry `deathYear` and `maidenName`. GedFire keeps and checks
  both but does not score them yet. A person can also carry `xref`, the
  tree person the record is already bound to. That person is reported as
  `single` on the xref with `bound: true`. Its same-name survey still lists
  the others. The binding also anchors the kin scoring of the record's other
  people, whatever spelling the record uses. An xref that is not a person is
  refused. A person the record relates to a bound person resolves to that
  person's one tree relative of the stated kind (spouse, child, parent or
  sibling) whose given name agrees, also as `single`, with `anchoredBy`
  naming the bound person. The surname is not compared, so a wife recorded
  under her married name still resolves. With no such relative, or several,
  the person is matched as usual.
- `relationships`: person-index pairs using `spouse-of`, `child-of`,
  `parent-of`, or `sibling-of`.
- `event`: the record's date, a GEDCOM 7 date with a year, and place.
- Or `findingPath` instead of the three above: a finding file whose `record`
  slot holds them. The file must be a valid finding file. The result matches
  the same record given inline.
- Optional `focus`: the person whose result you want.
- Optional `maxCandidates` (1-20, default 5): candidates returned per person;
  `candidatesOmitted` counts the rest.
- Optional `compact: true`: candidates become `{xref, name, lifespan, score,
  matchedKin}` and survey rows `{xref, name, reason}`.

The verdict, confident match, `runnerUpXref`/`runnerUpScore`,
`potentialNewPerson`, and `possibleDuplicates` are decided before the cap, and
the survey is never capped.

Results rank candidates by name, lifespan, kin overlap, and place. Each
person's `sameNameSurvey` lists the same-name candidates considered and the
reason each non-leader lost: incompatible dates, wrong spouse, impossible age,
or a lower score. The verdict is `single`, `weak`, `candidates`, or `none`:

- `single`: a lone viable candidate, or a composite score of at least 90 and
  a lead of at least 10 points, that something besides the name supports: a
  `lifespan`, `place` or `kinOverlap` subscore above 0. A bound or anchored
  person is also `single`.
- `weak`: such a lead with nothing but the name behind it, or a candidate the
  tree does not hold as the stated relative of a bound or anchored person.
  `confidentMatchXref` is null and the candidates are listed as usual.
- `candidates`: several candidates and no decisive lead.
- `none`: no viable candidate, or none whose `name` subscore reaches 70.

A blank or Unknown given name (`____`, `Unknown`, or empty) on either side
adds nothing to the name score: the surname is scored alone and capped at 60,
below the name floor, so a surname alone never makes a match. When the record
relates the person to a bound or anchored person, each candidate who is not
that person's relative of the stated kind in the tree carries a survey
reason such as `not the father of @I1@ (Hetty Dunmore) in the tree`, even as
the leader, and is never `single`.

A person matching nobody receives `potentialNewPerson` and any near-miss
`possibleDuplicates`, not a forced match. The same inputs yield the same
result. The survey covers candidates the matcher considers, not every
historical person who might bear that name.

### Research briefs

`research_brief` combines the person's full record with:

- `kin`: parents, siblings, spouses with marriage details, and children.
- `gaps`: the same targets `select_targets` reports for the person when named
  in `xrefs`, including a man's Biography card even when he has no other card.
- `suppressedCards`: the cards `select_targets` would withhold for the person,
  with the same reasons.
- `possibleDuplicateOf`: up to three scored candidates, plus the full count.
- `plausibility`: standing issues against the person or their families.
- `notes`: person and family note texts.

Optional `cardType` adds tailored `emphasis`. `new-spouse` highlights children
with an unknown other parent. Enrichment types highlight dated facts that
bound a missing vital. Missing kin, notes, or duplicates produce empty arrays.

### Target selection

`select_targets` draws `people` people at random from those with at least one
gap card for study `surnames`, and returns every card that pays for each of
them, kept together: one card per eligible gap (a married person gets no New
spouse card, a person with both parents no New parent card), then, for a man,
his Biography card. A pack holds at most one person with Legendary-band cards,
who may hold several; later such people are discarded whole, so a pack can
hold fewer people than asked. A family's New child card and marriage
enrichment sit on one spouse only. The result is `wanted.json`-shaped, without
the GEDCOM path; `count` in it is the number of cards, and `totalCandidates`
the gap cards before the draw. The clock seeds the draw. The CLI's
`select-targets --count` is the same number of people.
Alternatively, pass `xrefs` instead of people and surnames to score specified
people the same way, deterministically. A named man also gets his Biography
card when he has no other gap. `xrefsWithoutGaps` names the people who get no
card at all, such as a woman with no gap or anyone inside the privacy floor,
and `draw.seed` is null. An unknown xref is an error.

- A man outside the privacy floor gets one `Biography` card: in the draw, only
  beside another of his cards; with `xrefs`, always. Women get none. It
  stays open for people with complete vitals or existing prose, and pays again
  for each later changeset that adds new information: a new citation, or a
  biography note whose text none of the person's notes carried. `nominalPoints` is 3, and
  `score` adds the enrichment difficulty bonus for the person's birth era and
  region, without the anchor adjustment (3, 8, 13, or 18).
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
- New-child cards are also withheld when every known child of the wife predates
  this marriage (its date, or else the first census recording the couple):
  born before it by the child's birth date, or, when the child has no usable
  birth date, enumerated in the child's own census before it. A child with
  neither signal keeps the card.
- New-child cards are also withheld for a couple who almost certainly had no
  children together, judged on every reading of the dates (`ABT`, `CAL` and
  `EST` read as two years either side; `BET`, `BEF` and `AFT` as their bounds):
  - the wife's youngest possible age at the marriage is at least
    `maxMotherAgeAtMarriage` (default 50); or
  - the marriage's longest possible length, to the first of either spouse's
    death or burial or a divorce, is under `minMarriageYears` (default 1; 0
    turns the rule off).

  The card is never withheld by this rule when the marriage date is unknown,
  when the wife's birth date is unknown (for the age test), or when a child of
  either spouse may have been born after the marriage.
- `suppressedCards` lists `{xref, name, cardType, reason}` for otherwise eligible
  cards withheld by these gates, once per childless family for New child. These
  use the same subject xref, name, and card-type labels as the withheld card.
  Personal reasons are `bare-bones subject — enrich first`; family reasons are
  `bare-bones family (both spouses and marriage lack anchors) — enrich first`,
  `the wife's known children were all born before this marriage`, or a reason
  naming the rule and its values, such as `wife aged about 64 at marriage, at
  least 61 (born ABT 1878, married 17 MAR 1942), at or past
  maxMotherAgeAtMarriage 50` or `marriage lasted at most 6 months (married
  17 MAR 1942, wife died 17 SEP 1942), under minMarriageYears 1`.
  The list is empty when nothing was suppressed and is excluded from
  `totalCandidates`. Enrich-person cards, points, privacy, died-young, and surname
  rules are unchanged. No state is kept: landing an anchor restores relationship
  cards on the next selection, without restricting extra-credit collection.
- With `xrefs`, `includeSuppressed: true` draws every withheld card, so the
  owner can force one; `suppressedCards` is then empty. It is an error without
  `xrefs`.
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
| `findingPaths` | Finding files, each with a complete entry under `draft`. |

The compiler rejects unrecognized fields by path. File-based errors name the
file. A missing `draft` or a non-empty `draftGaps` prevents drafting. A
finding that names a record the document does not hold, such as an absent
`target.xref`, is refused by path.

### Finding files

GedFire owns the finding-file format. A finding file is one JSON object with
these top-level keys:

| Key | Required | Content |
|---|---|---|
| `draft` | Once the finding can be drafted | One `draft_changeset` findings entry. `draft.id` names the file, as `<id>.json`. |
| `argument` | No | The identity argument; overrides one inside `draft`. |
| `draftGaps` | No | Strings. A non-empty list blocks drafting. |
| `record` | No | The record exactly as `match_record` takes it: `persons`, `relationships`, `event`. |
| `meta` | No | Any object. It belongs to the caller; GedFire never reads, validates, or rewrites its content. |

`meta` and `record` never reach the changeset. Any other top-level key (such
as `persons`, `correlation` or `status`) is refused, naming the key. So is a
`draft.id` that differs from the file name. `draft_changeset`, `match_record`
and `validate_finding` all refuse such a file. A file with no `draft` reports
"has no draft"; GedFire reads nothing of the caller's to explain why.

`validate_finding` checks finding files (`findingPaths`), or draft entries
(`findings` or `inputPath`), without drafting. It is read-only. For each
finding it reports:

- `valid` and `errors`
- `readyToDraft`
- `draftGaps`
- `bindings`: each xref the draft or a record person names, and where. A
  document record carries `exists`, plus a person's `name` and `lifespan` or
  a source's title. A `@New?@` placeholder carries `declared`. A spouse
  relation between two people who already share a family is bound to that
  family, e.g. `{"ref": "@F1@", "at": "draft.relations[0].family", "exists": true}`,
  in place of the new family it named.
- `schemaVersion`

These rules need only the finding, so they are checked with or without a
document. They are also checked while `draftGaps` keep the finding from
drafting, so a harvest is checked before correlation settles its gaps:

- Field shapes and the finding-file keys.
- GEDCOM 7 date syntax on each fact `date` and on `record.event.date`. An ISO
  date, free text, or a date with a place appended is refused. Every GEDCOM 7
  form is accepted, case ignored: `1 JUN 1840`, `ABT`, `CAL`, `EST`, `BEF`,
  `AFT`, `BET ? AND ?`, `FROM ? TO ?`, calendars and `BCE`.
- Fact tags. Each `fact` must be a tag the dialect writes; `describe_changeset_ops`
  lists them under `createOrUpdateVital`.
- The QUAY ceiling of each fact, relation and note citation, using the item's
  own `class` and `information`, else the source's.
- Placeholder declarations. Every `@New?@` person a fact, note or relation
  names is declared in `people`, and every new family is named by a relation.
- Sex on a new person a relation places as `spouse`, `parent`, `husb` or
  `wife`, so the role can be typed.
- Verbatim excerpts. An `excerpt` on a fact, relation or note citation must
  be part of the cited finding's `extract`, word for word.
- One grade per quoted text. Two claims that quote the same text of a
  finding, such as the whole extract, must carry the same `quay`. Give a
  differently graded claim its own `excerpt`.
- Cited notes. A note with `purpose: "biography"` needs at least one entry in
  `cites`; with none, or an empty array, it is refused as `notes[0] is a
  biography note with no cites`.
- Cross-finding cites. A note's or fact's `cites[].finding` is resolved
  against the other findings checked with it (the other `findingPaths` files,
  or the other `findings` entries) and against the file `<id>.json` beside
  the citing file. The cited finding may aim at any target. An `excerpt` must
  be in its `extract` word for word. A cited id found nowhere stays an error.
  The cited finding is not drafted with the one that cites it, and an error
  of its own is reported when that finding is checked, not against the cite.

Two checks are about completeness rather than validity: a person declared
in `people` that no relation links to the tree, and a finding with no facts,
relations or notes that no note cites. While `draftGaps` is non-empty they
only make the finding not ready. Once the gaps close they are errors, worded
as `draft_changeset` words them.

`draft_changeset` refuses the same faults in the same words, and refuses any
file with `draftGaps`. Without a
document (`gedfire tools call validate_finding` with no `--input`), these are
the only checks, and their text is the document mode's. With a document,
xrefs are also resolved. A record person's `xref` must name a person. A
finding that is ready is compiled, with the findings its notes cite, by
`draft_changeset`'s checks, so its errors match `draft_changeset`'s word for
word, and `readyToDraft` is true exactly when `draft_changeset` compiles that
file (with them) without error. A file
with no draft or with `draftGaps` is not ready; it is an error only with
`requireDraft`.

### Schemas and versions

`gedfire schema <name> [--out <file>]` prints a standalone JSON Schema (draft
2020-12), and `describe_schemas` returns the same documents over MCP. The
names are `finding`, `finding-file`, `record`, `argument`, `changeset` (see
[the changeset contract](#the-changeset-contract)), `cards` (the file
`validate_changeset`'s `cardsPath` reads), `changeset-report` (the `reportPath`
file of `validate_changeset` and `apply_changeset`) and `draft-report` (the
`reportPath` file of `draft_changeset`). Each schema
carries `$id` (`https://github.com/morrillonline/GedFire/schema/<name>/<major>`)
and `"x-schemaVersion": "<major>.<minor>"`. The tool input and output schemas
that hold these shapes are built from the same definitions.

- A new optional field or a new enum value bumps the minor version.
- A removal, a rename, a new required field, or a tighter constraint bumps
  the major version and with it the `$id`.
- Release notes name every schema version change.

Current versions: `finding` 4.2 (a source may name an existing source by
`ref` and omit `locator`, and a fact may be `commentaryOnly`; 4.1 let a fact
and every cite take `commentary`; 4.0
dropped `moveToNote` from `replacedCitations`; 3.3
let a fact take `cites` and a cite name a
finding of any target; 3.2 let a parent relation take `notDuplicateOf`;
3.1 let a note `withdraw` the note its `match` names; 3.0 required `cites` on
a biography note; 2.0
tightened date, tag, excerpt, grade and sex rules), `finding-file` 4.2 (embeds
`finding` 4.2; 2.0 refused unknown keys and a mismatched `draft.id`),
`record` 1.2 (documents `match_record`'s `weak` verdict; 1.1 added optional
`persons[].xref`), `argument` 1.0, `changeset` 3.2 (a citation takes
`replacesPage`, and a new page of a cited source is added beside, never
written over a held page; 3.1 let a citation take
`commentary`, and `@VOID@` as its source for commentary alone; 3.0 dropped
`moveToNote` from `replacedCitations` and `deletedCitations` from
`deleteVital`; 2.0 refused every
legacy shape, including the `schemaVersion` key; 1.1 let `createOrUpdateParent` take
`notDuplicateOf`), `cards` 1.0, `changeset-report` 2.0 (drops `warnings`) and
`draft-report` 3.2 (embeds `changeset` 3.2; 3.0 dropped the `verification-note`
decision kind). A schema
document states structure only. `validate_finding` is the full check. In its default
strict mode, ajv rejects the `x-schemaVersion` annotation. Register it with
`ajv.addKeyword("x-schemaVersion")`, or pass `--strict=false` to `ajv-cli`.

### Finding fields

Each entry carries `id`, a verbatim `extract`, `source`, `target`, and `facts`.
It may also carry `people`, `relations`, `notes`, and an identity `argument`.

| Field | Content |
|---|---|
| `source` | `name`, `locator`, `class`, `information`; optional `ref`, `author`, `url`, `accessed`. `locator` may be omitted only as described under "Naming a source by xref". |
| `target` | `xref`; optional `card` and `name`. |
| Each fact | `record`, `fact`, per-fact `quay`; date/place or text `value`; optional `excerpt`, `class`, `information`, `commentary`. A `commentaryOnly` fact takes only `commentary` and date/place. |

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

#### Naming a source by xref

When the document holds several sources with one title and author, title
matching cites the first of them. `source.ref` names the existing source to
cite instead, such as `"ref": "@S01395@"`. The compiler refuses a `ref` that
is not a source in the document, and one whose title or author differs from
the finding's `name` and `author`, so a mistyped xref cannot move citations to
another source.

`locator` may be omitted to add to a held citation that has no page. Every
fact of such a finding is `citeOnly`, no relation or note cites it, and the
fact it selects must already cite that source without a page; otherwise the
draft is refused. The citation is drafted without a `page` and updates the held
one, so a finding cannot add a new citation that has no page.

The compiler rejects a citation grade above these ceilings:

| Source class | Primary information | Other information |
|---|---|---|
| Original or image copy | 3 | 2 |
| Derivative | 2 | 1 |
| Authored | 1 | 1 |
| User-contributed | 0 | 0 |

These are ceilings, not automatic grades or proof of accuracy. The author
assigns each fact's grade.

### Reconciliation

- Repeatable events such as `CENS`, `RESI` and `OCCU` are placed by date, so a
  drafted op still lands where it was meant to after another changeset adds
  instances of the tag. A date no existing instance has drafts
  `"mode": "add"`, even for a person with none. A date exactly one instance
  has drafts `"match": {"date": …}` on it; a bare year names any plain date in
  that year, whose fuller date is kept. A date several instances have is
  refused, naming them. A fact with no date, or with `substructures`, is
  always added.
- At apply, `"mode": "add"` creates nothing when the person already holds an
  instance with the same value, date and place: the op's citations are added
  to it beside the ones it has, and the log says `cited existing`. Two
  changesets that both add `CENS 1900, Maine` leave one census with both
  citations.
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
- Places compare by jurisdiction, ignoring case, hyphens, spacing, trailing
  periods, and a trailing `County`, `Co.`, or `Parish`. A finding that differs
  from the record only this way is cited, not written; the record's spelling is
  kept. The same rule applies to points scoring and GEN301 place matching.
- Precision decides only between dates that can both be true. When two dates
  contradict (two plain dates that disagree, a bound that excludes the
  record's date, or the record's bound that excludes the finding's date), the
  record's value gives way only if it is uncited or every citation carries a
  QUAY below the finding's: the finding's date is written and the old
  citations are removed. A citation with no QUAY, common in imported
  files, holds the record's date like a graded one. Otherwise the record's
  date is kept and an `open` decision names the record's date, its citations
  (source xref, page and grade), the findings and their conflicting date. Write
  a note on the conflict, or give the fact `replaceHeld` with a reason. A
  finding's place that says more than the record's (or a place where the
  record has none) is still added beside the kept date, with the finding's
  citation; otherwise nothing is drafted for the fact.
- `ABT`, `CAL` and `EST` have no defined tolerance, so they never replace a
  specific date that is more than a year away; the fact is left `open`. A
  specific finding still replaces such an estimate on the record, with an
  `open` decision asking that both describe the same event.
- Genuine disagreements leave the fact undrafted. An `open` decision lists
  values and findings, recommending by QUAY and then source quality.
- Name reconciliation only expands documented nicknames with matching surname,
  suffix, and given-name count. Other disagreements stay `open`.

The compiler never writes a note of its own. A person's notes are part of
their life story, and the family web page follows the Register style, so a
note holds only what a Register-style report would show: no verification
reminders, no change logs, and no record of a replaced value. A low grade is
carried by the citation's QUAY.

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

A `spouse` relation between two existing people who already share a family
compiles onto that family, never a new one. A new family placeholder it names
is replaced by the shared family wherever the findings use it (a `MARR` fact,
a child relation), a `resolved` decision says so, and `validate_finding`
binds the relation's `family` to it. A relation naming a different existing
family is refused with the family the couple shares; so is one for a couple
who share several families and does not name one of them.

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

- `spouse`, `child` and `parent` relations accept `notDuplicateOf`: existing
  people the author has confirmed are not the new person.
- A new relative is refused when the person it attaches to already has it:
  a new parent when the person already has a parent in that role (of the same
  name, a duplicate; of another name, a conflicting second parent that would
  replace the first), a new spouse when the person has a spouse of an agreeing
  name, and a new child when the family, or an existing partner of a new
  family, has a child of an agreeing name. Two names agree when they have the
  same known surname and at least one whole given name in common, after
  common abbreviations are expanded (Wm, Jas, Geo, Chas, Thos, Jno, Eliz,
  Margt, Saml, Benj, Danl, Robt, Nath, Nathl, Jos, Edw, Hy). A lone initial is
  not a given name, and a blank, `Unknown` or underscore surname never agrees.
  The error names the relative's xref: use it in place of the placeholder, or
  list it in the relation's `notDuplicateOf`. `validate_changeset`,
  `check_plausibility`, `apply_changeset` and `draft_package` refuse the same.
- A child relation with `from` moves the child from that existing family into
  `family`. `unlink-child` takes `family` and `child`; `unlink-parent` takes
  `person`, `role`, and optional `family`.
- Removal ops carry no citation, `quay`, or `excerpt`. The draft raises an
  `open` decision asking the reviewer to confirm the finding's evidence; the
  removed link is never recorded in a note. It refuses to remove a
  child the family does not list.
- A fact can carry `substructures`, such as an `EVEN` with a `TYPE`.
  Such an event adds an instance rather than matching by date.
- `replacedCitations` chooses `keep` or `drop` for citations on
  a replaced value. A value corrected by stronger evidence or `replaceHeld`
  drops its citations by default: a corrected fact has no place in the
  record, in a note or elsewhere. NAME corrections refuse `keep`, so old
  citations cannot attest the new name.
- `citeOnly: true` cites an existing fact; date/place select its instance.
- `commentaryOnly: true` with `commentary` adds only a comment citing no
  source (`SOUR @VOID@` with a `NOTE`) to an existing fact; date/place select
  its instance. It asserts nothing and does not cite the finding's source, so
  it takes no `quay`, `value`, `excerpt`, `cites` or other claim field. Use it
  when the comment concerns the fact as a whole rather than one record.
- `replaceHeld: "<reason>"` writes a single-valued fact's date and place over
  the record's, even a more specific or better-cited one. A contradicted value's
  citations are removed unless `replacedCitations` says otherwise.
- `withdraw: "<reason>"` deletes the record's fact with nothing in its place;
  date/place select the instance, and its citations are removed with it. The
  reason and the finding's evidence are shown to the reviewer in the
  changeset's findings table; nothing about the removed value goes into a note.
- A note's `match` names the exact text to rewrite. `cites` lists
  `{"finding", "quay", "excerpt"}` entries; omitting `finding` uses its own.
  The compiler joins notes on the same record within a changeset and retains
  every finding's citations.
- A fact's `cites` lists `{"finding", "quay", "excerpt"}` entries, each naming
  another finding whose record also supports the fact. The fact keeps its own
  finding's citation at its `quay` and gains one citation per entry; a cite
  of the fact's own finding is refused.
- A fact's `commentary`, and the `commentary` of any cite (on a fact or a
  note), is a sentence or two of the author's comment on that record's
  evidence: why its reading is preferred over another record's, or that a date
  is derived from an age. The compiler writes it as `commentary` on the
  citation it drafts, so it prints after the source in that footnote. A
  `citeOnly` fact's commentary lands on the existing fact. A note holds the
  life story; discussion of the evidence behind it goes in its citations'
  commentary. Commentary is one paragraph with no record ids, and a withdrawal
  takes none.
- A cite, on a note or a fact, may name any finding by id, whatever its target
  or status: another finding of the draft, or with `findingPaths` a finding
  staged as `<id>.json` beside the citing file. The cited finding lends only
  its source, `locator` and `extract` (or the cite's `excerpt`); no facts,
  people or relations are drafted from it. A staged finding that is not in
  `findingPaths` appears in the changeset's findings table with no `item` and
  no `claims`. The cite is graded against the cited finding's source and
  information classes, and one quoted text carries one grade, including the
  cited finding's own claims: a differently graded cite needs its own
  `excerpt`. A cited id found nowhere is refused.
- A note with `withdraw: "<reason>"` and `match` deletes the record's note
  with exactly that text, drafting a `deleteNote`; it takes no `text`,
  `cites`, or `purpose`. The reason is reported as a `resolved` decision. A
  `match` the record does not hold drafts nothing and raises an `open`
  decision. A finding that only withdraws or removes cites nothing, so it
  mints or reuses no source.
- A note's `purpose: "biography"` claims biography credit for the new
  information it adds to the person's cited biographical prose (service,
  occupation, migration, probate): a new citation, or new text. It must carry
  at least one `cites` entry. It compiles
  to a `createOrUpdateNote` with the same purpose and joins only other
  biography notes. The note itself is a plain cited note.

### Correcting an existing person's name

A NAME fact may address any existing person bound by a target, a person
reference in `relations`, or `existingPeople` anywhere in the input. The
fact's own `record` does not bind the person. `existingPeople` declares xrefs
without creating people or asserting new relationships; `people` remains
reserved for new-record placeholders.

For example, these fields in a finding for a fictional wife correct her
bound husband's name and their marriage:

```json
{
  "target": {"xref": "@I20@", "name": "Betty Morrill"},
  "existingPeople": ["@I10@"],
  "facts": [
    {"record": "@I10@", "fact": "NAME", "value": "Joseph /Wadleigh/", "quay": 3},
    {"record": "@F1@", "fact": "MARR", "date": "27 OCT 1779", "quay": 3}
  ]
}
```

Include the usual finding `id`, `extract`, and `source`. Each NAME op receives
that finding's citation and QUAY; agreeing findings contribute their citations
to one op. A different or removed surname is refused unless that NAME fact
sets `allowSurnameChange: true`. Keep `target.name`, when supplied, as the
currently recorded name, not the proposed correction.

Applying a different NAME replaces the old name and removes its citations. No
note records the old name. Unchanged names change nothing, and a second apply
is a no-op. Both `validate_changeset` and `check_plausibility` include the
old-name-to-new-name replacement in their `log`.

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

Each `findings` entry traces back to the finding it was compiled from. Its
`id` is the draft's `id`, and so names the finding file. `extract` and the
`source` fields are copied from the draft, with `source.ref` added. The
`argument`, beside or inside the draft, is carried unchanged and is absent
when the finding has none. `claims` is a summary of the draft's facts and
relations, not a copy of them; the finding file stays the full record.

## Drafting a package

First compile with `draft_changeset`, then run `validate_changeset` and
`check_plausibility`. Fix inputs and redraft when either check exposes a defect.

Call `draft_package` with:

- `changesetPaths`: finished changesets in the desired numbering order.
- `folder`: an existing output folder.
- `proposal`: the filename prefix.
- Optional `overwrite`: allow replacement.
- Optional `sameNamePeople`: pairs of new people, by placeholder xref, that
  share a first given name and surname but are truly different people, such
  as a father and son both named John: `[["@NewI_a@", "@NewI_b@"]]`.

The tool writes `<folder>/<proposal>.md` and reports its path and changesets.
It validates changesets against the bound document and fails on invalid input.
It does not repair changesets or replace the upstream plausibility check.

It also checks the package's items together, since no single changeset can
show these. Each violation is an `errors` entry naming both items and the
cure, and no proposal is written while one stands:

- One source, page and extract carries one QUAY in every item. Two references
  are one source when both give a web address and the addresses agree
  (ignoring query, fragment and a trailing slash), or otherwise when their
  titles agree, so two `@NewSource…@` placeholders for one source compare equal.
- Two new people whose names share the first given name and the surname are
  refused as one person named two ways, unless declared in `sameNamePeople`.
  A placeholder names a person only within its own changeset.

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
| `deathPlaceEvidence` | Require a death-place citation whose page or `dataText` matches this regular expression. |

A policy file that still holds the retired `verificationNote` key stops
startup with a message to delete it: GedFire no longer writes or requires
verification notes.

Only selected items undergo policy checks. Violations appear as
`policy: item 2: ...`; validation reports errors and apply refuses the changes.
Policy matches text and grades, not the underlying truth of a source.

## Points preview

Pass `cards` to `validate_changeset`, or `cardsPath` pointing to
`{"cards": [...]}`. Each card supplies `xref`, `class`, and `points`, with
optional `gap`, `name`, and `bonus`.

Classes are `enrich-person`, `new-parent`, `new-spouse`, `new-child`, and
`biography`.
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

A `biography` card closes at its `points` when a `createOrUpdateNote` with
`purpose: "biography"` adds new information to its subject, either of:

- a citation that none of the subject's biographical notes carried before the
  changeset; or
- note text that matches none of the subject's notes before the changeset, cited
  or not, once case, whitespace and punctuation are folded. The note's citation
  does not matter here: a second note from an already-cited record pays when
  its text is new. Rewording is not judged; a reviewer decides whether
  differently worded text says anything new.

Biographical notes are plain
person-level notes with prose and at least one source citation; GedFire writes
no marker or extension, so they are recognized from standard GEDCOM structure.
Only an op with the purpose earns credit. A citation is its source and page
(`SOUR` + `PAGE`), so quotation text, QUAY, spacing or case in the page,
citation order, and a placeholder or copied source xref do not make it new.
Existing prose never exhausts the card: a new note beside it, a new
citation strengthening it, or new text from a record it already cites pays; an
identical replay (the same text, ignoring case, spacing and punctuation, with no
new citation) or a run that changes nothing does not. A person earns at most one Biography
award per changeset, alongside any fact cards; a later changeset adding
new information earns another. A note a biography op writes never receives
citations moved off a replaced fact in the same changeset.

Points measure changes, not evidentiary reliability.

## The changeset contract

A changeset has one strict, versioned shape: the `changeset` schema
(`gedfire schema changeset`). `describe_changeset_ops` is its readable guide;
both are generated from the table the validator reads, so they cannot drift.
`draft_changeset` writes only this form.

- A changeset carries no version. The published schema is versioned instead
  (`x-schemaVersion`, now 3.1). A `schemaVersion` key left by an earlier
  release is refused as an unknown field; draft such a changeset again with
  `draft_changeset`.
- Every object takes only the fields the contract defines: the envelope,
  items, ops, and every nested object (`marriage`, `people` entries, facts,
  citations, `newSources`, the findings table).
- `citations` is always an array, wherever a citation can appear: an op, a
  `marriage`, a new person's facts, and `mergePerson` resolutions. It cites the
  fact or note the op writes. `createOrUpdateSpouse`, `createOrUpdateChild` and
  `createOrUpdateParent` cite the family record in `familyCitations`; a
  marriage's own citations sit in `marriage.citations`.
- The fact's tag fixes its payload. NAME and SEX take `value`, a string. An
  event (BIRT, DEAT, MARR, CENS, BURI, …) takes `date` and `place`. An
  attribute whose payload is text (OCCU, RESI, EVEN, TITL, …) takes `value`,
  `date` and `place`. A fact selector follows the same rule:
  `"match": {"value"}` or `{"date", "place"}`.
- A spouse, child or parent field holds only an xref: an existing person, or a
  `@New…@` placeholder declared once in the item's `people`
  (`{xref, name, sex?, facts?}`). An item that names a new person declares it,
  even when another item creates it first.

Every changeset tool refuses anything else, identically, as an error naming the
full path and the fix; nothing is read leniently and results carry no warnings:

- a singular `citation` (`items[0].ops[2].citation: use "citations", an array`),
  `citation` beside `citations`, a non-array `citations`, or a relationship op's
  `citation`/`citations` (use `familyCitations`);
- a field the contract does not define, anywhere: envelope, item, op, or a
  nested `marriage`, person, citation or `newSources` entry
  (`items[0].ops[0].citatons: unknown field; …`), and a `null` optional field;
- an object `value`, a `value` on an event, a `date` or `place` on a text fact
  such as NAME, a date given as an attribute's value, and the same in `match`;
  a string `match`;
- an inline person object in `spouse`, `child` or `parent`, a placeholder its
  item's `people` does not declare, a `people` entry no op names, a `people`
  entry with a real xref, and one person declared twice;
- a field of the wrong type, a missing required field, and an unknown op.

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

`validate_changeset` and `check_plausibility` accept an optional `after`: a
list of `{changesetPath, items}` applied in memory, in order, before the
target changeset is checked. Use it to check an approved changeset against the
tree as it will stand when its turn comes, so an order dependency between
packages (a census another package adds first, an edit to a fact another
replaced) shows before apply. Nothing is written. The log names each earlier
changeset applied; one that does not apply fails the run with its errors,
prefixed `after[<i>] <path>:`. A missing file or a bad `items` selection in
`after` is an error. Omit `after` to check against the document as it is.

New records use reserved `@New<token>@` placeholders. Tokens contain letters,
digits, and underscores. The first creation fixes the record kind; later
references resolve to the same allocated xref. Library callers receive
`ApplyResult.MintedXrefs`; the CLI logs allocations.

- Person creation rejects high-confidence duplicates, including planned people.
  Family, source, and media creation do not use the person duplicate detector.
- A new parent, spouse, or child is refused when the person it attaches to
  already has that relative (see [Drafting a changeset](#drafting-a-changeset)),
  unless the op lists the relative in `notDuplicateOf`.
- Multiple marriages between the same people are valid; a duplicate marriage
  on the same exact date is not.
- Source creation reuses a matching title and author with no publication,
  ignoring case and extra spaces. This apply-time rule differs from the
  draft compiler's broader title normalization.
- Updating a shared source makes a copy (or reuses an exact updated source).
  Only facts the same item cites point to the copy. The item must cite the fact
  needing the change. `ApplyResult.CopiedSources` reports the mapping.
- A citation page belongs to the citation, not the source: one source may be
  cited on many pages of one fact, each page its own footnote. A stated page
  the structure already cites from that source updates that citation; any
  other page adds a citation beside the others. A page is never changed unless
  the citation names the page it corrects with `replacesPage`, such as
  `{"source": "@S1@", "page": "p. 121", "replacesPage": "p. 112"}`; naming a
  page the structure does not cite is an error. `deleteCitation` can select a
  page.
- Omit `fact` to edit a family-level citation with `createOrUpdateCitation`
  or `deleteCitation`. Use exact `note` text to address note citations.
- A citation's `commentary` is a sentence or two of the author's own comment
  on that source, such as why a date is read one way. It is written as the
  citation's `NOTE` (standard GEDCOM 7) and printed after the source and its
  extract in the footnote. Stating a new commentary replaces the old.
  Discussion of a fact's evidence belongs here, not in a person's notes.
- A citation with `"source": "@VOID@"` and only `commentary` is the author's
  comment citing no source, written as `SOUR @VOID@` with a `NOTE`. Its
  footnote holds only the comment. It is never evidence: it does not satisfy a
  required citation, close a card, or hold a date against a finding. The same
  text stated again is a no-op; `deleteCitation` with `"source": "@VOID@"` and
  `commentary` removes it.
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
by kind; a validation or apply reports the selected items, pass/fail, error,
warning and log counts, deltas, the minted-xref count, and — with cards — the points
preview total. Scripts read the file for the full result; `verbose: true`
returns it inline as well. The saved files follow the `changeset-report` and
`draft-report` schemas.

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
