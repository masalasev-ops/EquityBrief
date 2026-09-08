# BUILD_PLAN.md

Checkpoints, their deliverables and their done conditions, for every phase.

The seven general done conditions in `CLAUDE.md` apply to every checkpoint. What follows adds the deliverable and any condition particular to it.

---

## How to read this, and what changed

This document previously carried checkpoint detail for phases 0 and 1 only, under a rule that later phases get their detail at the previous phase's sign-off, because writing a late checkpoint early means writing it against assumptions earlier phases have not tested.

That rule was too broad, and it cost a planning round and a review round per phase for work that could have been written once. It is now narrowed. Three things are knowable the day the corpus is written and belong here: **the checkpoint list and its order**, **which document contradictions each phase has to resolve**, and **which specification holes each phase has to settle**. Only the last kind of detail waits: exact column names, exact test names, and whether a checkpoint needs splitting once its size is visible.

So each phase below carries its checkpoints, its visible output, its contradictions and its holes. A phase's planning pass confirms and refines rather than discovers.

**Every phase opens with something to look at, and that means the first checkpoint.** Not the third and not the sixth. Where a phase's natural dependency order would bury the visible output, the order is wrong and gets changed, which is what happened to phase 1.

---

## Specification holes, and where each is settled

Each is a place the architecture names something without saying what it is. They are listed here so a phase's planning pass confirms a decision rather than discovering a gap. Each becomes a `DECISIONS.md` entry at the checkpoint named.

| Hole | Why it matters | Settled at |
|---|---|---|
| **The calendar store has no producer and no declaration** | Four components declare they read `calendar`; SCHEMA declares no such table and the matrix has no such column. The earnings date is needed nightly by the ladder builder and the shortlist builder, and the only component that could fetch it runs on demand in phase 5 | 3.0, and it needs a component, a table and a matrix column, not only a table |
| Is the stored series adjusted | SCHEMA's bar row carries four price columns and no adjusted variant; an unadjusted series never diverges, which would make the corporate action checker pointless | 1.1 |
| What a gap is | The catalogue says a series with a gap is refused; the failure table says nothing is computed across it. Those are different behaviours | 1.1 |
| Which bar provider, as a decision | Named in the architecture and the runbook, absent from `DECISIONS.md`, and the per-ticker against bulk cost arithmetic is a choice a later session could reasonably reverse | 1.1 |
| The volume profile's window | SCHEMA gives the profile an as-of date and price bands and never says over how many sessions volume is accumulated. If it differs from the level window the two disagree about the same chart | 2.0 |
| Which two swings the retracements are drawn between | "the retracements of the last two swings" is ambiguous between the last high and the last low, and the last two swings of any kind | 2.0 |
| Does a volume shelf create a band or only rank one | Section 9.1 lists heavy volume shelves as one of four candidate sources that create bands. A price range holding heavy volume with no swing, average or retracement is a band under that reading and invisible under one where shelves only rank. The two produce different band sets, so the level builder at 2.4 is a different component depending which is meant, and the profile built at 2.3 feeds it either way | 2.0 |
| The calendar holds events that are not earnings | The calendar being invented at 3.0 is keyed to the provider's earnings calendar. A report needs scheduled events that are not prints: a product event, an investor day, a leadership change. Those are news-derived rather than fetched, so either the table carries two origins with their provenance, or a researched event is a separate thing the ladder reads separately. Whichever is taken, a book keyed to one date cannot carry two, so the earnings trade is renamed the event trade in the same pass | 3.0 |
| The trend classifier's rule | Stated as "from the averages and the last two swings", which is a description rather than a rule, and it selects which ladder shape applies | 3.0 |
| The tranche condition, and which applies when | A fixed list of four patterns is named and nothing says which one a given tranche gets | 3.0 |
| A tranche condition that depends on a researched fact | A tranche can reasonably be conditional on something no compute component can see, such as a guide not implying a revenue decline. The ladder builder's matrix row gives it levels, indicators and the calendar, and a researched fact reaches it through none of those. The shape that fits the design is that the ladder emits the tranche with its price condition and a research pass may attach a fundamental precondition carrying its own source, which the claim checker treats like any other claim. Decide it at 3.0 rather than inventing a fifth condition kind at 3.2 | 3.0 |
| The share of size per tranche | The plan places a position and never sizes one, yet the tranches carry shares that fall with distance, and no rule produces them | 3.0 |
| The earnings setups' triggers | Three setups are named, each said to carry a trigger, an entry, a stop and a target; none of the four is specified | 3.0 |
| The base rate's population and window | Stated as the universe figure for the same window, without saying whether it is every name-night, every index member, or every listing | 4.0 |
| The six reason thresholds | Stated as proposals and known to flood, with calibration deliberately left to the run page's own record | 4.0, revisited from 4.6's data |
| Which local model, and the section-to-lane assignment | The lane split is configuration by decision, and nothing says what the configuration's shape is | 5.0 |
| How a research pass is recorded for a fixture | A pass must be reproducible without a network, which needs a recorded endpoint with a defined record format | 5.0 |

---

## Corpus contradictions, and where each is resolved

Each is two documents disagreeing, which `CLAUDE.md` calls a finding rather than a licence to change either one. Prior text to `CHANGELOG.md` in every case, with the entry naming the defect.

| # | Contradiction | Resolve at |
|---|---|---|
| A | SCHEMA's bar note says the fetcher drops sessions older than the retention window; its ownership table gives Delete to the corporate action checker alone and says nothing else may delete a bar | 1.4 |
| B | The limits table says the nightly run makes zero per-name network calls; the run order backfills a new joiner per ticker. **Resolved at 1.2**, the limit carved rather than deleted | 1.2 |
| C | The failure table names a suspect state for a name whose corporate action check failed; no store column holds it | 1.6 |
| D | `Scope.Screens` keys on the table heading, so a phase 5 export claim is forced to be asserted at the chart checkpoint. This is the 0.7 repair of `Scope.For` failing to sweep, not a new contradiction. **Resolved at 1.3**, keyed on the table and the row together, with all 37 rows of section 15 reconciled against the document in both directions | 1.3 |
| E | The catalogue gives four components a `calendar` read that SCHEMA does not declare and no component writes | 3.0 |
| F | Section 15.5's Level chart mark names four elements, candles, bands, moving averages and a volume pane, and two of them cannot exist until phase 2, while the phase table puts a chart in phase 1. **Resolved at 1.3**, the row read as four claims in the harness rather than split into four rows in the document, with each element asserted to appear in the row's own description | 1.3 |
| G | The theme research runner's catalogue row declares it reads the theme store and source documents; its matrix row carries W in both columns and no R. **Resolved at 1.1**, both cells now R W | 1.1 |
| H | `news_pulse` is declared one year retained and its ownership row gives Delete to nobody, which is contradiction A in a second table and unnoticed until now | 1.4, with A |
| I | The splits and dividends feed is a read in the corporate action checker's catalogue row and is not one of section 5's source boxes, so the nightly path reads a feed the system overview does not carry | 1.6 |
| J | Section 17's source lists row names two lists, company-news and industry; `DECISIONS.md` carries **Three source lists, not one, each with a review date** | 1.7 |

---

## Phase 0: a skeleton that can already fail

**Signed off.** Eight checkpoints, 0.0 to 0.7. The verification machinery exists and reports before there is anything to verify, which is why its first report was entirely unexamined and out of scope.

Recorded in `PROGRESS.md`. Ten obligations carried out of it, listed at the foot of this document.

---

## Phase 1: a chart on the screen

**Visible output at 1.3**, which is as early as stored bars can exist.

### 1.1 The membership loader
The phase planning pass belongs here, as its own `PROGRESS.md` entry opening "Not a checkpoint entry".

Three decisions written before any code depends on them: the stored series is adjusted, what a gap is, and the bar provider with its per-ticker against bulk cost arithmetic. A provider abstraction taking its key from the secrets file, with a recorded-response double so no test touches the network. Migration creating `membership`. The membership loader with its access declaration. A query answering which names were members on a past date.

The component access mechanism lands here: each component declares the stores it reads and writes, and a new check reconciles those declarations against the catalogue row and the matrix row in both directions. This is the largest piece of verification work in the phase and most of the phase's claims run through it. The obligation to widen `writer-ownership` to what its roster row claims is discharged by the same work, and is forced to be: two of its tests assert over a population of zero and turn red the moment the first component lands.

Two carried obligations re-pointed here from 1.5 and discharged: the manifest checker scanning the captured response and checking the named file exists, and the zoneless instant refused with the clock check widened to catch the implicit machine-zone read. Both concern the first committed manifest, which arrives with this checkpoint's recorded-response double rather than at 1.5.

**Done when** a name that left the index carries its leave date, a membership query for a past date answers with that date's set rather than today's, and `component-access` reconciles in both directions with its scope stated and floored.

### 1.2 The one-year backfill
Migration creating `bar`, prices TEXT and decimal in code. Per-ticker historical fetch, once per name, skipped for a name already holding its year. The run log carrying the request count.

Contradiction B resolved: the zero-per-name-call limit governs the steady-state night and the backfill is carved out of it, stated in the limits row so `nightly-cost` does not fail on a rule nobody meant.

Three carried obligations discharged: reconcile the money column list against SCHEMA, fix the stream ordering that can deadlock, and add the runtime guard refusing a non-decimal into a money column with a test that inserts a double and asserts the refusal.

**Done when** the fixture name holds a full year with no gaps, a second run backfills nothing, and the request count matches the number of names lacking history. The population is stated explicitly, because a claim about roughly five hundred live names is not something the harness can assert.

### 1.3 The read surface and the chart
The read API serving bars for a name and a date range, computing nothing and fetching nothing, asserted over the shipped source. The mark renderer as its own component, declaring an empty access across all eleven stores, so the seam section 15.4 describes is a claim the harness asserts rather than a definition it trusts. The chart drawn as server-rendered SVG: candles and a volume pane on a shared time axis, at the hash route for a name.

**This is the level chart mark with two of its four elements absent**, written in the file that mark will live in for the life of the project and extended in place at 2.4. It is not a temporary chart, because a temporary chart becomes the second renderer the marks decision exists to prevent. The checkpoint entry states which elements are present and which are absent.

Candles are neutral ink, hollow for a close above the open and filled for below. Green and orange stay reserved for support and resistance on every screen.

Contradiction D resolved: `Scope.Screens` keyed on table and subject as `Scope.For` now is, and the harness swept for any other place a verdict, placement or reach is keyed on a heading alone, with the count found stated in advance.

Contradiction F resolved per element rather than per mark, and each element named at the checkpoint that draws it: candles and the volume pane are asserted at 1.3, the moving averages at 2.1 and the bands at 2.4. The whole mark waiting for phase 2 would leave what exists unasserted for a phase and a half.

One carried obligation re-pointed here from 1.5 and discharged: absolute path matching anywhere in a value rather than at position zero, which matters because `run_log.detail` is where exception text lands and exception text carries absolute paths mid-string.

**Done when** the page draws the fixture's sessions, the drawn candle count is asserted against the stored row count rather than eyeballed, and the API is proved to compute nothing.

### 1.4 The bar fetcher and the nightly script
One bulk request per night, storing bars for current members only. Retention dropping sessions older than a year, with contradiction A resolved in SCHEMA first so `writer-ownership` does not fail on a delete nobody declared.

`tools/nightly` and its wrapper, running only the steps that exist, through the same wrapper mechanism as everything else. This closes a promise the commands table, the layout block and the runbook all make and nothing keeps.

`nightly-cost` implemented over both the shipped source and a recorded run, promoted on the roster, with the pending floor lowered in the same commit. `bar-append-only` given its owner exemption, with the negative proof asserting an undeclared file still fails.

The `two-platform` widening falls due here, since this is the checkpoint that first depends on both shells behaving the same.

**Done when** a night runs end to end over the fixture, the cost check reports zero model calls and zero per-name requests, and the nightly script exits non-zero with a named step on any failure.

### 1.5 Gap refusal
Refusal to the definition settled at 1.1: per name per night, that name's bar not stored, its stored series left as it was, and the gap's date named. The first fixture folder holds the gap fixture, which is a constructed series with an interior session removed, because a clean provider series will not produce the failure the done condition requires be induced.

The fixture-absent test inverted to assert present with the manifest checked, keeping a test that absence is still never a pass. The placement whose due point is this checkpoint converted to a check naming an instrument with declared reach, in this commit, because once `PROGRESS.md` records this checkpoint the reconciliation refuses a due point that has landed.

One carried obligation discharged: the news feed probed for whether it is queryable by date without a ticker. The three manifest and path obligations that stood here are re-pointed to 1.1 and 1.3, because the first captured fixture arrives with 1.1's recorded-response double and an obligation owed at 1.5 would go unfixed across every manifest written from 1.1 onward.

**Done when** the gap fixture is refused with its date named, the clean fixture is unaffected, and the induced failure produces what the failure table promises.

### 1.6 The corporate action checker
Splits and dividends feed, full-year refetch replacing the old series in one transaction. Contradiction C resolved: a column for the suspect state with its grain and owner declared in SCHEMA, so a failed check marks the name rather than passing silently.

**The feed is captured before the parser is written, not after.** One request settles the payload's shape, and the checkpoint opens by spending it. 1.2 found a membership parser reading a field the provider does not send, which the fixture had agreed with for two checkpoints because the same session wrote both. A parser written first and given a fixture afterwards is a parser whose fixture is a transcript of what it already expects.

**Done when** an action in the fixture triggers a refetch, the replacement is atomic, a failure of the check itself marks the name rather than passing, and the action is read from a captured provider response rather than a constructed one.

### 1.7 The coverage measurement
Two weeks of news across thirty names spread deliberately across the market-capitalisation range and not chosen from a watch list. Distinct publishers counted by frequency, each checked for whether its text can be retrieved.

**The feed is captured before the parser is written, for the reason 1.6 states.** The measurement itself needs live responses, so this checkpoint spends the request first in any case; what it must not do is write the parser against a payload composed to suit it and keep the captured responses only as measurement input.

**Done when** the measurement is recorded with its sample named, the first draft of the company-news and industry source lists exists with a review date, and the news parser's fixture is a captured provider response rather than a constructed one. This is a measurement rather than an assumption: a large-company index guarantees coverage at the top and much less further down, and tuning the lists on the largest names alone starves the rest of the index invisibly.

### 1.8 Phase 1 report
**Done when** every claim the phase owes is PASS naming an instrument whose declared reach includes it, unexamined is zero, and the fixture holds phase 1 expectations with at least one derived independently from the rules rather than frozen from a run.

The claim total is predicted before 1.1 and checked here: the expected total after the phase, the new claims by source, and the out-of-scope figure that follows. A figure predicted in advance and missed is a finding; a figure computed afterwards is bookkeeping.

---

## Phase 2: levels on the chart

**Visible output at 2.1.** The moving averages are the cheapest thing that draws, so they come first.

### 2.0 Planning
Settles every hole this document's holes table assigns to 2.0, and takes a decision for each. Confirms the checkpoint split below against the size the work turns out to be.

The holes are named there rather than repeated here. A checkpoint listing its own subset is a second statement of the table's contents, and it is the statement that goes stale: this one named two of the three it is now assigned, so a hole filed against 2.0 would have been settled at 2.0 only if somebody happened to read both.

### 2.1 The indicator engine and the averages on the chart
Migration creating `indicator`. The averages, the momentum readings, the typical daily move and the volume ratios, each row carrying the bar count it was computed from.

The chart from 1.3 extended in place with the average lines drawn.

**Done when** the indicator values match the fixture, a name with fewer than two hundred bars records its long average as not available with its bar count rather than as a number, and the chart draws the averages.

### 2.2 The swing finder
Migration creating `swing`. Days whose high or low is the most extreme within three bars either side, each carrying the date its lookback completed.

**Done when** the swings match the fixture, and a component reading swings as of a date cannot see one confirmed later. That second condition deserves its own test: a swing is not knowable on its own day, and a reader that ignores the confirmation date sees the future.

### 2.3 The volume profile builder and the profile mark
Migration creating `volume_profile`. Each day's volume spread across that day's range into fixed price bands, over the window settled at 2.0. The profile drawn horizontally against the same price axis as the chart.

**Done when** the profile matches the fixture, the shares in the bands sum to the window's total volume, and the mark renders against the chart's price axis rather than its own.

### 2.4 The level builder and the bands
Migration creating `level`. The four candidate sources merged into bands within half a typical day's move, touches added, roles and strength assigned, and the non-average-anchor flag recorded because the ladder reads it on every band.

The bands drawn behind the candles, completing the level chart mark.

**Done when** the bands match the fixture with their members and dates, a band anchored only by a moving average is flagged as such, and the chart draws the bands behind the price rather than over it.

### 2.5 The level summary table and the momentum panel
The level table with each band's members and dates, and the momentum readings on their own small axes with their neutral rules drawn.

**Done when** every band in the table names its members and every momentum reading draws its neutral rule, because a momentum number without the band it sits in means nothing.

### 2.6 The fixture widens to four names
A mega-cap in a tight range, a mid-cap in a wide one, and a name that gapped, beside the existing name.

**Done when** every phase 2 expectation holds for all four names, and the carried obligation on the volume shelf threshold is discharged: the threshold is whatever makes all four charts agree with where their volume visibly clusters, rather than the figure derived from one chart.

### 2.7 Phase 2 report
**Done when** every phase 2 claim is PASS across four fixture names, and the predicted claim total is checked against the actual.

---

## Phase 3: the plan

**Visible output at 3.1.** The trend state is one word on a page that already exists.

### 3.0 Planning
The heaviest planning pass in the project, because more holes settle here than at any other point and one of them is a missing component.

**Contradiction E and the calendar.** Four components read a store that nothing declares and nothing writes. The resolution needs a calendar fetcher on the nightly path, a `calendar` table in SCHEMA with its grain and owner, a column in the read and write matrix, and a catalogue row. The earnings date is needed nightly by the ladder builder and the shortlist builder, so it cannot wait for the on-demand fundamentals fetcher in phase 5.

Settles every hole this document's holes table assigns to 3.0, and takes a decision for each. None of them is inferable from what is written, and each decides what the plan section says. Two of them arrive together: the second book is keyed to dated events rather than to prints alone, and a tranche can carry a precondition no compute component can see, so the pass that settles where non-earnings events come from is the pass that settles how a researched fact reaches a tranche.

The holes are named in the table rather than counted or repeated here. This checkpoint said "four holes settle here" and then listed four, which was two statements of one fact and both went stale the day two more were filed against it.

### 3.1 The calendar fetcher and the trend state
Migration creating `calendar`. The fetcher on the nightly path, one request for the calendar rather than one per name. The trend classifier to the rule settled at 3.0, with the state shown on the name page.

**Done when** the calendar table holds dated events for the fixture names, the trend state matches the fixture, and the classifier's rule is a stated rule rather than a description.

### 3.2 Tranches and stops
Migration creating `ladder`. Tranches placed on support bands whose low edge is below the price, skipping any band anchored only by a moving average. Each stop a daily close below the low edge of the next band beneath it.

**Done when** the tranches and stops match the fixture, a band anchored only by an average carries no tranche, a band straddling the price keeps its full width, and a name with no eligible band produces no ladder and says why.

### 3.3 Exits, the invalidation and the near-exit skip
Exits on the resistance bands above price, an exit closer than two typical days' moves listed but not traded, the top of the ladder a trailing rule rather than a price, and the invalidation at the lowest band the structure depends on.

**Done when** the exits match the fixture and the skipped exit is listed with its reason rather than omitted.

### 3.4 The plan column mark and the tables
One vertical price axis with the current price marked in it, stops as horizontal rules, the invalidation the lowest. The tranche table with conditions and stops, the exit table with actions.

**Done when** the figure renders from the ladder with no value drawn that the ladder does not carry, which is the containment property applied to pictures.

### 3.5 The earnings trade
The three setups to the triggers settled at 3.0, each with its entry, stop and target, kept as a second book that never merges with the position book.

**Done when** the setups match the fixture, and a name with no earnings date on file produces no setups and says so rather than producing them from a guessed date.

### 3.6 The arithmetic and the earnings rule
Risk per tranche at the zone midpoint, reward to risk from the first tranche and from the blended first two, the worked sizing example from a risk budget, and the statement of the last prints' one-day moves against the stop distance.

**Done when** every figure is derived rather than stored, the reward-to-risk arithmetic is asserted, and the earnings rule fires inside the horizon and not outside it.

### 3.7 Phase 3 report
**Done when** every phase 3 claim is PASS across four fixture names, and the plan section renders whole.

---

## Phase 4: tonight's list, over the whole index

**Visible output at 4.1.** The universe screen renders the moment five hundred names have bars.

### 4.0 Planning
Settles the base rate's population and window. Confirms the six reason thresholds as the proposals they are, and states that 4.6's own record is what calibrates them, so nothing is tuned in advance.

### 4.1 The full universe and the universe screen
Membership and backfill over the whole index. The universe screen: every name, paged, sorted by distance to the nearest level, with the sector strip and the filters.

**Done when** every index member holds its year, the nightly wall clock is inside its limit at index size, and the screen reaches every name rather than the first page of them.

### 4.2 The move annotator
Migration creating `move`. The largest single-day and multi-day moves of the stored year, which become the rows of the how-it-got-here table with the cause column empty until phase 5.

**Done when** the moves match the fixture and the table renders with its cause column explicitly absent rather than blank.

### 4.3 The facts assembler and the change detector
Migration creating `facts`. Every number the computed sections may use, each with its source, and the hash. The change detector writing only the material-change list, on disjoint columns of the same row.

**Done when** the facts file matches the fixture byte for byte, and the per-operation split is proved: a facts re-run must not blank the change list, asserted rather than true by construction.

### 4.4 The shortlist builder and tonight's list
Migration creating `listing`. **A row for every index member every night**, whether or not a reason fired, carrying the reasons with their values and the plan as it stood that night.

Tonight's list: the header with the true fired count, the watch list above it, twenty rows drawn, the reasons on each row.

**Done when** the listing row count equals the index size on every completed night, the fired count in the header matches the reasons, and a night with more than twenty fired names draws twenty and states the true count.

The plan-at-listing column is the one thing in this phase that cannot be added later. Bars can be replayed and the plan cannot, because by the time a verdict is possible the rules may have changed and recomputing would score old listings under new ones.

### 4.5 The forward return filler and the news pulse counter
Migration creating `forward_return` and `news_pulse`. The five and twenty-one session outcomes filled as they mature, the base rate computed to the definition settled at 4.0, and the article count per name from one feed request.

**Done when** an immature row reads as not yet matured rather than as a blank or a zero, and the news pulse costs one request rather than one per name.

### 4.6 The run page
The operational header, and each reason's record with the base rate pinned as the first row.

**Done when** no forward-return figure is shown without the base rate beside it, and a reason below the minimum shows a dashed outline carrying its count rather than a rate.

### 4.7 Phase 4 report
**Done when** every phase 4 claim is PASS, and a week of unattended nights has run with the list current each morning.

---

## Phase 5: research on demand

**Visible output at 5.1.** The numbers section fills from real filings.

### 5.0 Planning
Settles which local model, the section-to-lane configuration shape, and how a research pass is recorded so a fixture can replay it without a network. Discharges the source-lists obligation from 1.7 against the coverage measurement.

### 5.1 The fundamentals fetcher and the numbers section
Migration creating `fundamentals`, one row per filing date, never updated. The quarters, balance sheet, segments and guidance, each with the filing date it came from.

**Done when** the numbers section renders, a stored copy predating a filing triggers a fetch and one that does not triggers nothing, and the bulk fundamentals probe is recorded either way.

### 5.2 The source store and admissibility
Migration creating `source_document`. The admissibility test running after the fetch and before storage: denied categories, a publish date inside the window, and a preference for primary sources. A document that fails is kept as a row with its refusal reason and no body.

**Done when** each denied category is refused by a fixture document, a document whose text cannot be retrieved is discarded rather than cited, and the refusal is visible rather than silent.

### 5.3 The claim checker
Migration creating `research_section` and `theme_section`, one row per section per version. Numbers checked against the facts file, claims checked for a stored admissible source, one retry then the section omitted.

**Done when** the poisoned paragraph and the unsourced claim are both rejected, a section failing twice is absent with its reason rather than guessed, and nothing rejected is written.

### 5.4 The prose writer
The local model writing the short version and the figure keys from numbers, at no cost, through the claim checker like anything else.

**Done when** a pass runs against the recorded endpoint with no network, and the footer states which model wrote each section.

### 5.5 The research runner
Filings and news read by ticker, the theme record read, the narrative sections written, every document stored as it is fetched.

**Done when** every claim in a written section names a stored source, and a pass over the fixture reproduces byte for byte from the recorded endpoint.

### 5.6 The theme research runner
Industry research once per theme, shared by every name in it, with the search tool called as one more fetching tool and its results stored like any other document. The theme query names the industry rather than a ticker, carries a date range, is restricted to the industry list, and requests full page text.

**Done when** a theme refresh serves every name in its industry from one pass, and a search returning snippets rather than text produces no stored document.

### 5.7 The staleness judge
The four questions answered from data the nightly run already computed: a new filing, the earnings date passing, the news pulse above its baseline, or a manual refresh.

**Done when** each trigger fires its own test, a second open of an unchanged name spends nothing, and deciding not to spend costs nothing.

### 5.8 The overnight queue
The local model writing the sections in the local lane for listed names whose research is missing or stale, in priority order, until the configured time limit, holding the machine awake, making no paid call.

**Done when** the queue completes with zero spend, a busy night leaves names for the next night rather than running past its limit, and a night where the machine slept is reported rather than silent.

### 5.9 Phase 5 report
**Done when** every section the report specifies is produced on the current hardware with the synthesis sections written by the paid model on demand, and the spend cap stops a pass rather than warning about one.

---

## Phase 6: the improvement loop

**Visible output at 6.1.** The resolution counts appear on a run page that already exists.

### 6.0 Planning
Confirms the guardrail values against the evidence that has accumulated. Nothing here is tuned to what the data turned out to be; a threshold changed after seeing results starts a new window and the old one is kept.

### 6.1 Setup resolution
The setup horizon on forward returns: target reached, stop closed through, or the time cap expired. The resolution counts on the run page.

**Done when** each of the three outcomes has its own test, a timed-out setup is counted in its own column and never in a rate, and the counts render.

### 6.2 The break-even score
The hit rate each plan demanded, computed from its own entry, stop and target, and the share of setups that beat it.

**Done when** the arithmetic is asserted against worked cases, and a verdict below the minimum is withheld with its count shown against the minimum.

### 6.3 The candidate register
Migration creating `candidate_register`, append only. Registration before scoring, with the rule, the test and the date, and a stated maximum family size.

**Done when** an update and a delete are both refused at the store, a retirement is a new row naming what it retires, and the correction divisor matches the rows registered before the window opened.

### 6.4 The shadow column
Registered candidates evaluated nightly on every name-night exactly as live reasons are, written to the shadow column and shown nowhere.

**Done when** a shadow candidate is evaluated on nights no live reason fired, which is what the every-name listing row exists for, and nothing shadow reaches any screen.

### 6.5 Reason verdicts on the run page
Each reason against the bar its own setups demanded, with the resolved count and the divisor beside it, and the base rate pinned.

**Done when** no verdict appears below its minimum, every verdict names its divisor, and the three display states are each proved.

### 6.6 Rule versions scored counterfactually
Each ladder rule a named version, every night scored under every version from stored bars.

**Done when** a version change opens a new window and the previous one is kept, and a rule is not changed while a window measuring it is open.

### 6.7 The model's proposal
The model's own entry and exit proposal stored beside the computed one and scored on the same break-even rule, never overriding it.

**Done when** the proposal is stored, scored and displayed as a second opinion, and no computed number is sourced from it.

### 6.8 Phase 6 report
**Done when** every guardrail has its own test, and the loop has changed nothing on the strength of evidence below its stated minimum.

---

## Carried obligations

Recorded when created, not remembered. An obligation names a due point this document has.

| Obligation | Created at | Due at |
|---|---|---|
| Architecture cites its decisions by name at each rule | 0.5 | 1.8 |
| Splits and dividends parser checked against itself, its only fixture written by the session writing the parser | 1.2 | 1.6 |
| News parser checked against itself, its only fixture written by the session writing the parser | 1.2 | 1.7 |
| `two-platform` widened to what its roster row claims | 0.7 review | 1.4 |
| Absolute path matching anywhere in a value, not position zero | 0.7 review | 1.3 |
| News feed queryable by date without a ticker | authored with the architecture | 1.5 |
| Volume shelf threshold checked against four names | authored with the architecture | 2.6 |
| Source lists reviewed against measured coverage | 1.7 | 5.0 |
| Bulk fundamentals endpoint probed on the operator's key | authored with the architecture | 5.1 |

**Discharged at 1.2.** The money column list, now read from SCHEMA's own Notes cell rather than kept beside the check. The stream ordering that can deadlock, with a probe that fills both pipes and a test bounded by a timeout, since the failure is a hang rather than a wrong answer. And the runtime money guard, which refuses anything that is not a decimal at the point a price is bound.

**Discharged at 1.1.** `writer-ownership` widened to both directions its roster row claims, which building the first component forced rather than allowed: two of its tests asserted over a population of zero and turned red the moment `MembershipLoader` landed. The zoneless instant refused, with `clock-usage` widened to read code rather than prose. The manifest checker opening every captured response and checking the file it names exists, which the first committed fixture made assertable.
