# BUILD_PLAN.md

Checkpoints and their done conditions. The seven general done conditions in `CLAUDE.md` apply to every checkpoint; what follows adds the deliverable and any condition particular to it.

**Later phases get checkpoint detail at the previous phase's sign-off, not before.** Writing 4.6 today means writing it against assumptions phases 1 through 3 have not tested. Phases 2 onward are named here with their deliverable and their visible output, and nothing finer.

**Every phase opens with something to look at.** The first checkpoint of each phase renders a page. A report about row counts is not visible output.

---

## Phase 0: a skeleton that can already fail

The point of this phase is that the verification machinery exists and reports accurately before there is anything to verify. Its visible output is a page that is entirely red or entirely unexamined, which is the correct first result.

### 0.0 The repository
A git repository with `main` as the default branch, the corpus committed unedited as its first commit, and a remote if one is wanted. Nothing owned this: the Merge section requires a branch and a pull request, `changelog-reconciles` reads the history, and `RUNBOOK.md` opens with "clone the repository", none of which is possible before it exists.
**Done when** the corpus is committed, `git log` shows the initial commit, and the working tree is clean.

### 0.1 The solution
Six projects as `CLAUDE.md` lays out, all targeting `net10.0` and taking warnings as errors from `src/Directory.Build.props`, with `EquityBrief.Api` carrying no reference to `EquityBrief.Worker`.
**Done when** `dotnet build` is clean with nothing suppressed, no project file states a target framework or a warning setting of its own so both come from `src/Directory.Build.props`, and `api-isolation` passes reading the compiled dependency file.

### 0.2 The store and the migration runner
The SQLite store under the configured data root, a migration runner, and the first migration creating `run_log`. No other table yet.
**Done when** `tools/migrate` applies against an empty directory and again against an applied store without error, and `store-portability` passes over the result.

### 0.3 The clock
The clock abstraction resolving session dates through IANA identifiers, with `InvariantGlobalization` false.
**Done when** two tests pass through the clock's own public surface: one that fails loudly if no identifier resolves, and one asserting the session zone is behind UTC by at most a day with both bounds read from the clock. `clock-usage` passes. A green suite is not evidence this path ran; the tests must call it.

### 0.4 The CI scripts
`tools/ci.ps1` and `tools/ci.sh` running every workflow step in order against a dropped store, exiting non-zero on the first failure, plus the `.ps1` wrappers for every bash entry point.
**Done when** `ci-parity` passes, each wrapper returns both the script's output and its exit code proved by a test running a deliberately failing probe, and a wrapper on a machine with no bash exits with a named message rather than zero.

### 0.5 The harness reading the architecture
`tools/verify-phase` parsing `docs/ARCHITECTURE.html`'s tables by their headings, enumerating every claim, and writing `artifacts/phase-report.html` and `.json`.
**Done when** the report opens and lists every claim as UNEXAMINED, the parse guard fails when a table is missing rather than reporting zero claims, and no claim reads PASS.

### 0.6 The fixture folder
`/fixtures` with its layout and a manifest naming the endpoint, query and instant of every captured input. No inputs and no expectations yet.
**Done when** the folder exists, the manifest schema is asserted by a test, and the harness reports the fixture as absent rather than as passing.

### 0.7 Phase 0 report
**Done when** the phase report is green on everything it can assert, zero claims read PASS by fiat, and `PROGRESS.md` carries the phase 0 entry with its test count.

---

## Phase 1: a chart on the screen

Visible output: a candlestick chart for one name, in the browser, from stored bars.

### 1.1 The membership loader
Fetches index constituents with their join and leave dates and writes `membership`.
**Done when** a name that left the index carries its leave date, and a query asking which names were members on a past date answers with that date's set rather than today's.

### 1.2 The one-year backfill
One request per ticker against the historical endpoint, once per name, never repeated for a name that already holds its year.
**Done when** every current index member holds a full year of bars with no gaps, a second run backfills nothing, and the run log's request count matches the number of names lacking history. The per-ticker route is used rather than replaying past sessions through the bulk endpoint, which costs fifty times as much for the same data.

### 1.3 The bar fetcher and gap refusal
One bulk request per night. A series with a gap is refused rather than stored, and retention drops sessions older than one year.
**Done when** the gap fixture is refused with the gap's date named, `bar-append-only` passes, and `nightly-cost` reports zero model calls and zero per-name network requests over a recorded run.

### 1.4 The corporate action checker
Detects splits and dividends and refetches the affected name's full year.
**Done when** an action in the fixture triggers a refetch, the refetched series replaces the old one in one transaction, and a failure of the check itself marks the name suspect rather than passing silently.

### 1.5 The read surface
`EquityBrief.Api` serving bars for a name and date range, read only.
**Done when** the API performs no computation and no fetching, asserted over the shipped source, and `api-isolation` still passes.

### 1.6 The chart
`EquityBrief.Web` rendering candles and a volume pane for one name from the API.
**Done when** the page opens and draws the fixture's sessions, with the count of drawn candles asserted against the stored row count rather than eyeballed.

### 1.7 The coverage measurement
Two weeks of news pulled across a sample of thirty names spread deliberately across the market-capitalisation range and not chosen from the watch list. Distinct publishers counted by frequency, each checked for whether its text can be retrieved.
**Done when** the measurement is recorded in `PROGRESS.md` with its sample named, and the first draft of the company-news and industry source lists exists with a review date. This is a measurement rather than an assumption: a large-company index guarantees coverage at the top and much less further down, and tuning the lists on the largest names alone starves the rest of the index invisibly.

### 1.8 Phase 1 report
**Done when** every phase-1 claim is PASS, the fixture holds phase 1's expectations with at least one derived independently, and the phase report carries zero unexamined.

---

## Phase 2: levels on the chart

Indicator engine, swing finder, volume profile builder, level builder. The bands drawn on the chart, the level summary table, and the volume histogram beside it. The fixture widens from one name to four of different character: a mega-cap in a tight range, a mid-cap in a wide one, and a name that gapped.

Visible output: the same page with bands, the histogram and the summary table.

**Carried into this phase:** the volume shelf threshold in `ARCHITECTURE.html` is set from one chart. Phase 2 checks it against all four fixture names before anything depends on it.

## Phase 3: the plan

Trend classifier, ladder builder, the plan figure, the tranche and exit tables, the earnings setups.

Visible output: the plan figure and tables, as section 4 of the architecture specifies them.

## Phase 4: tonight's list, over the whole index

The full universe, shortlist builder, facts assembler, forward return filler, news pulse counter, and the list, universe and run pages.

Visible output: tonight's list over five hundred names, with the true fired count in the header.

**Carried into this phase:** every listing stores the plan as it stood that night. Without that column, phase 6 becomes a wait rather than a feature, and the evidence cannot be recreated afterwards.

## Phase 5: research on demand

Fundamentals fetcher, staleness judge, theme and name research runners, claim checker, prose writer, the research store with versions and sources, and the overnight queue.

Visible output: a name opening with computed sections instantly and research streaming in; the same name opening instantly the next time with per-section dates; and, the following morning, listed names already carrying their overnight draft sections.

## Phase 6: the improvement loop

Setup resolution, the break-even score, the candidate register, the shadow column, and the condition verdicts on the run page. Rule versions scored counterfactually. The model's own plan proposal stored beside the computed one and scored the same way.

Visible output: the run page showing each reason against the bar its own setups demanded, with the resolved count and the correction divisor beside it.

---

## Carried obligations

Recorded when created, not remembered. A `PROGRESS.md` entry naming a carried obligation must name a due point this table also has.

| Obligation | Created at | Due at | What it holds |
|---|---|---|---|
| `tools/migrate.ps1` written without its proofs | 0.2 | 0.4 | 0.2 needed the wrapper to run migrate on Windows at all, so it exists. 0.4's done condition owes the proofs: that a wrapper returns both the script's output and its exit code, shown by a deliberately failing probe, and that a machine with no bash exits with a named message rather than zero |
| The suite unrun on macOS | 0.1 | 0.4 | 0.1 was verified on Windows only, because the matrix cannot run until `tools/ci.*` exists. No macOS runner has executed the suite |
| 0.1's checks unseen by the harness | 0.1 | 0.7 | `api-isolation`, `build-properties-central` and `pinned-constants` are asserted by the suite and by nothing the phase report reads. 0.1 produces no pipeline output, so it has no fixture expectations to carry, only checks the report must enumerate |
| The phase report is named twice | 0.1 | 0.5 | the component catalogue in `ARCHITECTURE.html` has the verification harness write `verify.html` and `verify.json`, while `CLAUDE.md` and this file say `artifacts/phase-report.html` and `.json`. One artefact, two names, and `architecture-conformance` reads the catalogue |
| The citation placeholder in this table is not a citation | 0.0 | 0.4 | the row below writes the citation form out in full, so a `decision-resolves` built on the parenthesised pattern reads the placeholder inside it as a citation and fails on a name that does not exist. Either the checker exempts this table by name or the row is reworded |
| Volume shelf threshold checked against four names | authored with the architecture | 2.1 | the threshold is derived from one chart and nothing has tested it |
| Source lists reviewed against measured coverage | 1.7 | 5.1 | the lists are a first draft from a two-week sample |
| Bulk fundamentals endpoint probed on the operator's key | authored with the architecture | 5.1 | if it responds, one nightly call replaces the on-demand fundamentals fetcher |
| News feed queryable by date without a ticker | authored with the architecture | 1.3 | if it is not, the nightly pulse costs a hundred times more, which is affordable but should be known |
| Architecture cites its decisions by name | 0.5 | 0.7 | the decisions moved to `DECISIONS.md` and the architecture states its rules in prose without citing them, so `decision-resolves` has one citation to assert. A pass over the architecture adding `(see: <name>)` at each rule that rests on a decision is owed before the phase 0 report claims that check runs |
