# BUILD_PLAN.md

Checkpoints, their deliverables and their done conditions, for every phase.

The nine general done conditions in `CLAUDE.md` apply to every checkpoint. What follows adds the deliverable and any condition particular to it.

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
| **The calendar store has no producer and no declaration** | Four components declare they read `calendar`; SCHEMA declares no such table and the matrix has no such column. The earnings date is needed nightly by the ladder builder and the shortlist builder, and the only component that could fetch it runs on demand in phase 6 | 4.0, and it needs a component, a table and a matrix column, not only a table |
| Is the stored series adjusted | SCHEMA's bar row carries four price columns and no adjusted variant; an unadjusted series never diverges, which would make the corporate action checker pointless | 1.1 |
| What a gap is | The catalogue says a series with a gap is refused; the failure table says nothing is computed across it. Those are different behaviours | 1.1 |
| Which bar provider, as a decision | Named in the architecture and the runbook, absent from `DECISIONS.md`, and the per-ticker against bulk cost arithmetic is a choice a later session could reasonably reverse | 1.1 |
| **What "unavailable" means** | Section 18's first row promises a behaviour for a feed that is unavailable and nothing anywhere says what unavailable is. A refusal, a socket that never answers, a rejected request rate and a payload that parses to nothing are four events with four right answers, and a row treating them as one is a row no test can induce | 2.0 |
| The retry policy, the backoff schedule, the per-request timeout and the night's deadline | Every feed interface accepts a cancellation token and nothing supplies one. There is no stated timeout, no retry and no deadline for the night as a whole, so a night that hangs on a socket hangs until someone looks | 2.0 |
| The night's schedule, as an instant | Section 14 schedules the run with Task Scheduler after the US close, which names one platform's mechanism and a local time. The provider posts the bulk file at a fixed UTC hour, and a schedule written in local time moves against it twice a year with nothing to announce it | 2.0, with contradiction M |
| What a paged payload does to the request count | Every feed counts its own requests and none says what happens when a payload arrives paged. A feed that starts paging keeps the one-request claim true in the document and false in the run, and the count would report the truth while the limits row read as a lie | 2.0 |
| The weighted-call budget | `RUNBOOK.md` states a daily allowance of 100,000 weighted calls and the weight of each endpoint. No code reads either figure, so nothing stops a night spending a month's allowance and nothing would say it had | 2.0 |
| The `nightly-cost` carve-out | The check's coverage half bans every outward-request type across every shipped file and reports zero. The first live feed turns a green check red, so what may hold a client is named before one exists. It is a decision because the wrong shape, deleting the patterns, leaves a check that reports the absence of a scan as the absence of a client | 2.0, applied at 2.1 |
| The volume profile's window | SCHEMA gives the profile an as-of date and price bands and never says over how many sessions volume is accumulated. If it differs from the level window the two disagree about the same chart | 3.0 |
| Which two swings the retracements are drawn between | "the retracements of the last two swings" is ambiguous between the last high and the last low, and the last two swings of any kind | 3.0 |
| Does a volume shelf create a band or only rank one | Section 9.1 lists heavy volume shelves as one of four candidate sources that create bands. A price range holding heavy volume with no swing, average or retracement is a band under that reading and invisible under one where shelves only rank. The two produce different band sets, so the level builder at 3.4 is a different component depending which is meant, and the profile built at 3.3 feeds it either way | 3.0 |
| The calendar holds events that are not earnings | The calendar being invented at 4.0 is keyed to the provider's earnings calendar. A report needs scheduled events that are not prints: a product event, an investor day, a leadership change. Those are news-derived rather than fetched, so either the table carries two origins with their provenance, or a researched event is a separate thing the ladder reads separately. Whichever is taken, a book keyed to one date cannot carry two, so the earnings trade is renamed the event trade in the same pass | 4.0 |
| The trend classifier's rule | Stated as "from the averages and the last two swings", which is a description rather than a rule, and it selects which ladder shape applies | 4.0 |
| The tranche condition, and which applies when | A fixed list of four patterns is named and nothing says which one a given tranche gets | 4.0 |
| A tranche condition that depends on a researched fact | A tranche can reasonably be conditional on something no compute component can see, such as a guide not implying a revenue decline. The ladder builder's matrix row gives it levels, indicators and the calendar, and a researched fact reaches it through none of those. The shape that fits the design is that the ladder emits the tranche with its price condition and a research pass may attach a fundamental precondition carrying its own source, which the claim checker treats like any other claim. Decide it at 4.0 rather than inventing a fifth condition kind at 4.2 | 4.0 |
| The share of size per tranche | There is no hole. The shares that fall with distance came from the hand-made report the worked example is drawn from, where a person decided how much to commit, and that is exactly what **The plan places a position and never sizes one** reserves to the reader. A share of an intended position is a sizing rule written as a fraction, and calling it a display convention does not change what a reader does with it. Exits are a different thing and stand: a fraction of what is already held is scaling out of a position that exists. **Settled at 4.0**, by amending this row rather than by superseding the decision | 4.0, settled |
| The earnings setups' triggers | Three setups are named, each said to carry a trigger, an entry, a stop and a target; none of the four is specified | 4.0 |
| The base rate's population and window | Stated as the universe figure for the same window, without saying whether it is every name-night, every index member, or every listing. **Settled at 5.0**: every name-night, being every `listing` row for the horizon and not the rows where a reason fired, on `CLAUDE.md`'s own rule that a figure over listed names only is a figure over the wrong population. Two sub-questions settled beside it rather than folded into it: the `setup` horizon has no universe figure of the same kind and the column is null for it, and the figure repeats down the table by design because the row is what the run page reads | 5.0, settled |
| The six reason thresholds | Stated as proposals and known to flood, with calibration deliberately left to the run page's own record | **Confirmed at 5.0** as the proposals section 11's own callout already calls them, with nothing tuned in advance. The calibration is an operating obligation and not a checkpoint's (owes: The six reason thresholds calibrated from the nights they fired on), because 5.6 displays a record that nights accumulate and no checkpoint accumulates nights |
| **What one dated news query actually returns** | Section 14 counts today's articles from one news feed request and section 17 prices news at one request weighing 5. 2.5 measured one dated request for a single session coming back at exactly 1,000 articles, which is the provider's cap, over 3,232 distinct symbols, so one request does not carry a day and a count taken from it is a count over whatever the cap included. Filed at 5.0 rather than left to 5.5, because 5.5 would otherwise discover at build time whether the phase's cost rule can be met at all. **Settled at 5.0**: the query is paged until the day is covered, every page is counted, a day reaching a stated maximum refuses rather than truncating, and the page count follows the day's news volume rather than the size of the universe, which is what keeps the zero-per-name rule true. Sections 14 and 17 are restated rather than satisfied | 5.0, settled |
| Which local model, and the section-to-lane assignment | The lane split is configuration by decision, and nothing says what the configuration's shape is | 6.0 |
| How a research pass is recorded for a fixture | A pass must be reproducible without a network, which needs a recorded endpoint with a defined record format | 6.0 |

---

## Corpus contradictions, and where each is resolved

Each is two documents disagreeing, which `CLAUDE.md` calls a finding rather than a licence to change either one. Prior text to `CHANGELOG.md` in every case, with the entry naming the defect.

| # | Contradiction | Resolve at |
|---|---|---|
| A | SCHEMA's bar note says the fetcher drops sessions older than the retention window; its ownership table gives Delete to the corporate action checker alone and says nothing else may delete a bar. **Resolved at 1.4**, three-way rather than two-way: the fetcher is declared a deleter, two removals are sanctioned and named, and `bar-append-only` permits a delete only in a declared deleter's file | 1.4 |
| B | The limits table says the nightly run makes zero per-name network calls; the run order backfills a new joiner per ticker. **Resolved at 1.2**, the limit carved rather than deleted | 1.2 |
| C | The failure table names a suspect state for a name whose corporate action check failed; no store column holds it. **Resolved at 1.6** with a table rather than a column, because grain is a property of a table: `series_state`, one row per ticker, owned by the checker | 1.6 |
| D | `Scope.Screens` keys on the table heading, so a phase 6 export claim is forced to be asserted at the chart checkpoint. This is the 0.7 repair of `Scope.For` failing to sweep, not a new contradiction. **Resolved at 1.3**, keyed on the table and the row together, with all 37 rows of section 15 reconciled against the document in both directions | 1.3 |
| E | The catalogue gives four components a `calendar` read that SCHEMA does not declare and no component writes. 3.1's reconciliation of the whole matrix adds a second half to it: the Ladder builder carries a **Fundamentals** read that appears in no catalogue phrase, and its catalogue names the calendar the matrix has no column for, so the likeliest reading is that the calendar read was written into the nearest column there was. That is a guess and 4.0 settles it, along with whether the other three calendar readers carry the same substitution | 4.0 |
| F | Section 15.5's Level chart mark names four elements, candles, bands, moving averages and a volume pane, and two of them cannot exist until phase 3, while the phase table puts a chart in phase 1. **Resolved at 1.3**, the row read as four claims in the harness rather than split into four rows in the document, with each element asserted to appear in the row's own description | 1.3 |
| G | The theme research runner's catalogue row declares it reads the theme store and source documents; its matrix row carries W in both columns and no R. **Resolved at 1.1**, both cells now R W | 1.1 |
| H | `news_pulse` is declared one year retained and its ownership row gives Delete to nobody, which is contradiction A in a second table and unnoticed until now. **Resolved at 1.4**, with A and by the same reasoning: the counter is declared its deleter | 1.4, with A |
| I | The splits and dividends feed is a read in the corporate action checker's catalogue row and is not one of section 5's source boxes, so the nightly path reads a feed the system overview does not carry. **Resolved at 1.6**, the box added | 1.6 |
| J | Section 17's source lists row names two lists, company-news and industry; `DECISIONS.md` carries **Three source lists, not one, each with a review date**. **Resolved at 1.7**: the decision's body always described two and its name said three, so the name was the defect. Superseded by a decision that also says where the lists apply | 1.7 |
| K | Section 16's read and write matrix puts nine components' write one column to the right of the store their catalogue row names. Indicator engine, Swing finder, Volume profile builder and Move annotator wrote **Listings** where their rows say indicators, swings, volume profile and moves; Shortlist builder wrote **Forward returns** where its row says listings; Facts assembler wrote **Fundamentals** where its row says facts; Forward return filler wrote **Facts** where its row says forward returns; News pulse counter wrote **Research and theme** where its row says news pulse; and Research runner wrote **Series state** where its row says source documents. Level builder and Fundamentals fetcher are correct, which is what makes it a displacement rather than a convention. `component-access` matches cell by cell including blanks and reaches a row only when its component exists, so every one was invisible until the code landed and then produced two faults each. **Resolved at 3.1**, all nine together, by reconciling every row of the matrix against its catalogue row rather than by repairing the rows this contradiction happened to name. That is what found the ninth, and it found one other thing: on the Facts assembler the displacement moved a read as well as a write, putting **News pulse** where the catalogue says fundamentals, so a repair confined to writes would have left that row failing on a read | 3.1, all nine together |
| L | The shortlist builder's matrix row disagrees with its catalogue row in its reads as well. The row reads **Fundamentals** and **News pulse**; the catalogue names levels, indicators, ladders, calendar and facts. Facts has a column and is not read, and neither named store is in the catalogue phrase. This is separate from K because K has one answer per row and this one does not: what the component reads is decided by section 11's six reasons | **Ruled at 5.0** from section 11's six reasons and section 1's statement that the shortlist selects on chart state alone with no fundamentals and no model in the decision, which puts the matrix's Fundamentals read on the wrong side. What it reads is levels, indicators, ladders, the calendar, the facts file and the bar store, being tonight's close and today's volume, which four of the six reasons need and neither side names. Repaired at 5.4, where the component is built and `component-access` first reaches the row |
| M | Section 14 closes by scheduling the night with Task Scheduler after the US close. That names a Windows-only mechanism, against **Nothing is written against one operating system**, and a local time, against **Queued work runs off-peak, and every schedule is written in UTC**. Two hard rules, in one sentence, in the section the nightly run is specified in | 2.0 |

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
The read API serving bars for a name and a date range, computing nothing and fetching nothing, asserted over the shipped source. The mark renderer as its own component, declaring an empty access across every store in the matrix, so the seam section 15.4 describes is a claim the harness asserts rather than a definition it trusts. The chart drawn as server-rendered SVG: candles and a volume pane on a shared time axis, at the hash route for a name.

**This is the level chart mark with two of its four elements absent**, written in the file that mark will live in for the life of the project and extended in place at 3.4. It is not a temporary chart, because a temporary chart becomes the second renderer the marks decision exists to prevent. The checkpoint entry states which elements are present and which are absent.

Candles are neutral ink, hollow for a close above the open and filled for below. Green and orange stay reserved for support and resistance on every screen.

Contradiction D resolved: `Scope.Screens` keyed on table and subject as `Scope.For` now is, and the harness swept for any other place a verdict, placement or reach is keyed on a heading alone, with the count found stated in advance.

Contradiction F resolved per element rather than per mark, and each element named at the checkpoint that draws it: candles and the volume pane are asserted at 1.3, the moving averages at 3.1 and the bands at 3.4. The whole mark waiting for phase 3 would leave what exists unasserted for a phase and a half.

One carried obligation re-pointed here from 1.5 and discharged (owes: Absolute path matching anywhere in a value, not position zero), which matters because `run_log.detail` is where exception text lands and exception text carries absolute paths mid-string.

**Done when** the page draws the fixture's sessions, the drawn candle count is asserted against the stored row count rather than eyeballed, and the API is proved to compute nothing.

### 1.4 The bar fetcher and the nightly script
One bulk request per night, storing bars for current members only. Retention dropping sessions older than a year, with contradiction A resolved in SCHEMA first so `writer-ownership` does not fail on a delete nobody declared.

`tools/nightly` and its wrapper, running only the steps that exist, through the same wrapper mechanism as everything else. This closes a promise the commands table, the layout block and the runbook all make and nothing keeps.

`nightly-cost` implemented over both the shipped source and a recorded run, promoted on the roster, with the pending floor lowered in the same commit. `bar-append-only` given its owner exemption, with the negative proof asserting an undeclared file still fails.

The `two-platform` widening falls due here (owes: `two-platform` widened to what its roster row claims), since this is the checkpoint that first depends on both shells behaving the same.

**Done when** a night runs end to end over the fixture, the cost check reports zero model calls and zero per-name requests, and the nightly script exits non-zero with a named step on any failure.

### 1.5 Gap refusal
Refusal to the definition settled at 1.1: per name per night, that name's bar not stored, its stored series left as it was, and the gap's date named. The first fixture folder holds the gap fixture, which is a constructed series with an interior session removed, because a clean provider series will not produce the failure the done condition requires be induced.

The fixture-absent test inverted to assert present with the manifest checked, keeping a test that absence is still never a pass. The placement whose due point is this checkpoint converted to a check naming an instrument with declared reach, in this commit, because once `PROGRESS.md` records this checkpoint the reconciliation refuses a due point that has landed.

One carried obligation discharged (owes: News feed queryable by date without a ticker): the news feed probed for whether it answers by date at all. The three manifest and path obligations that stood here are re-pointed to 1.1 and 1.3, because the first captured fixture arrives with 1.1's recorded-response double and an obligation owed at 1.5 would go unfixed across every manifest written from 1.1 onward.

**Done when** the gap fixture is refused with its date named, the clean fixture is unaffected, and the induced failure produces what the failure table promises.

### 1.6 The corporate action checker
Splits and dividends feed, full-year refetch replacing the old series in one transaction. Contradiction C resolved: a column for the suspect state with its grain and owner declared in SCHEMA, so a failed check marks the name rather than passing silently.

**The feed is captured before the parser is written, not after** (owes: Splits and dividends parser checked against itself). One request settles the payload's shape, and the checkpoint opens by spending it. 1.2 found a membership parser reading a field the provider does not send, which the fixture had agreed with for two checkpoints because the same session wrote both. A parser written first and given a fixture afterwards is a parser whose fixture is a transcript of what it already expects.

**Done when** an action in the fixture triggers a refetch, the replacement is atomic, a failure of the check itself marks the name rather than passing, and the action is read from a captured provider response rather than a constructed one.

### 1.7 The coverage measurement
Two weeks of news across thirty names spread deliberately across the market-capitalisation range and not chosen from a watch list. Distinct publishers counted by frequency, each checked for whether its text can be retrieved.

**The feed is captured before the parser is written, for the reason 1.6 states** (owes: News parser checked against itself). The measurement itself needs live responses, so this checkpoint spends the request first in any case; what it must not do is write the parser against a payload composed to suit it and keep the captured responses only as measurement input.

**Done when** the measurement is recorded with its sample named, the first draft of the company-news and industry source lists exists with a review date, and the news parser's fixture is a captured provider response rather than a constructed one. This is a measurement rather than an assumption: a large-company index guarantees coverage at the top and much less further down, and tuning the lists on the largest names alone starves the rest of the index invisibly.

### 1.8 Phase 1 report
**Done when** every claim the phase owes is PASS naming an instrument whose declared reach includes it, unexamined is zero, and the fixture holds phase 1 expectations with at least one derived independently from the rules rather than frozen from a run.

The claim total is predicted before 1.1 and checked here: the expected total after the phase, the new claims by source, and the out-of-scope figure that follows. A figure predicted in advance and missed is a finding; a figure computed afterwards is bookkeeping.

---

## Phase 2: the feeds reach the network

**Visible output at 2.1**, which is the chart from 1.3 drawing a bar the provider sent tonight rather than one a capture holds.

The phase exists because no feed reaches the network. Every provider implementation in the tree is a recorded double, `tools/nightly` takes a fixture folder, and a live night cannot run. That was carried out of 1.4 as a hole rather than as a defect in any checkpoint, and it is a phase rather than a checkpoint for the reason the level work cannot go first: building four compute stages against a fixture no provider produced means deriving every indicator, swing, profile and level expectation from it and then calibrating the shelf threshold, the merge distance and the profile window over it. 1.2 found a membership parser reading a field the provider does not send, which the fixture had agreed with for two checkpoints because the same session wrote both. That failure is available again here, one layer down and across four stages at once.

### 2.0 Planning
The phase planning pass belongs here, as its own `PROGRESS.md` entry opening "Not a checkpoint entry".

Settles every hole this document's holes table assigns to 2.0, and takes a decision for each. Six of them, and the first is the one the others hang off: section 18 promises a behaviour for a feed that is unavailable and never says what unavailable is. A refusal, a socket that never answers, a rejected request rate and a payload that parses to nothing are four events, and a row that treats them as one is a row no test can induce.

The holes are named in the holes table rather than repeated here, for the reason 3.0 states.

Contradiction M resolved: section 14's closing sentence schedules the night with Task Scheduler after the US close, which is a Windows mechanism and a local time, and both are against a hard rule. The schedule becomes a UTC instant stated against the hour the provider posts the bulk file, and the mechanism leaves the document, because scheduling lives outside the application and naming one platform's scheduler inside the design is what put it there.

Also settles the shape of the `nightly-cost` carve-out, which is a decision about a check rather than about cost. Its coverage half scans every shipped file for `System.Net.Http`, `HttpClient`, `WebClient`, `HttpRequestMessage` and the socket constructor, and reports zero. The first live feed turns that green check red, so the carve-out has to name what may hold a client, or scope the scan to the components section 14 lists, before one exists. What it never does is delete the patterns: the rule being asserted is that the nightly path makes no per-name request, and a scan that stopped looking would report the absence of a scan as the absence of a client.

### 2.1 The credential path and the bulk price feed
**The `nightly-cost` carve-out lands first, in its own commit, before any live feed.** Done condition 8 exists to stop a commit whose last change is the one nothing after it re-reads, and a commit that removes a guard and adds the thing the guard forbade is the same shape: whichever half is read first, the other looks authorised.

Then `ProviderCredentials` bound from configuration, and the startup refusal `RUNBOOK.md` already promises and no startup path performs. The class refuses a blank key today and nothing constructs it, so the promise is a constructor nobody calls.

Then one live feed end to end: the bulk price feed, because it is the nightly path's own and the one the zero-per-name rule is shaped around. The capture-before-parse rule of 1.6 and 1.7 does not apply to it, because the parser exists and was written against a captured response at 1.4; what is new here is the transport, not the shape.

The provider's posting hour for the day's bulk file is measured here from live fetches rather than taken from documentation, which is the one figure the schedule decision leaves open and the first thing a live feed can be asked (see: The night runs at a fixed UTC instant set after the provider posts the day's bulk file).

**Done when** a night fetches the day's bars from the provider in one request, the run log's `network_requests` is measured off the live feed rather than off a double, the key is refused by name at startup when it is blank, and no request URL reaches a log line or a store row. The last is not incidental: a URL carries the key in a query string on this provider, and the run log is a store this repository copies between machines.

### 2.2 Retry, backoff and the night's deadline
To the policy settled at 2.0 (see: A feed is tried three times with a doubling backoff, and the night has a deadline it cannot move). A cancellation source threaded into `Nightly.RunAsync`, which takes none today while every feed interface accepts one, so a night that hangs on a socket has no deadline and nothing to cancel it.

Section 17 gains the row **Per-request timeout and the night's deadline**, carrying the three attempts, the doubling wait, the thirty-second bound on an attempt and the fifteen-minute bound on the night. The figures are read off that row and asserted against the code's own policy, because a limit stated in a document and again in code is two places holding one fact.

**Done when** a transient refusal is retried to the stated policy and a persistent one is not, a night exceeding its deadline stops with the step named and exits non-zero, and the retry is proved not to double-write over the delete-and-reinsert refetch path SCHEMA declares. That last condition is why this checkpoint is not folded into 2.1: a retry over a non-idempotent write is a defect that only appears once both exist.

### 2.3 Failure behaviour at the wire
"Unavailable" implemented to the definition settled at 2.0 (see: A feed is unavailable when it does not answer, and wrong when it answers with something else), and section 18 given the two rows that definition leaves it short of. Both are answers that arrive: **A feed answers with a session other than the one asked for**, and **A feed answers with none of the index in it**. They are two rows rather than one because they are refused in different places. Only the feed can see the session the payload declares, against the session it asked for; only the fetcher knows how many members the index has. One is refused inside the feed and the other after it, which also settles a question 2.1 left open: the feed is told which session to fetch rather than asked for the last day, because a request with no date has nothing to compare its answer against.

A rejected request rate and an answer past the night's deadline add no row. The definition settled at 2.0 makes both of them unavailable, and the row that promises what the system does when a feed is unavailable already exists; a second row saying the same thing in all four cells is the two-statements defect this corpus refuses everywhere else. They are induced as two of that row's cases instead.

The existing "Bulk price feed unavailable" row is decomposed per surface the way the gap row was at 1.5: its behaviour half is assertable here and its banner half needs tonight's list, so the two are asserted at different checkpoints rather than the whole row waiting for the later one.

**Done when** each new row's failure is induced against the fixture and produces what the row promises, and a response carrying a session other than the one asked for is refused rather than stored. A night answered with yesterday's bulk file logs one request and no error, which is the failure that reports green, and it is the reason the wrong-session row exists at all.

### 2.4 The remaining price and membership feeds
Live implementations of `IIndexMembershipFeed`, `IHistoricalBarFeed` and `ICorporateActionFeed`, each answering the `Requests` member its interface already mandates rather than a count the caller states.

Section 17 gains the row **Weighted-call budget**, which is the figure `RUNBOOK.md` has stated since the architecture was written and no code has read. Each endpoint's weight is known here because this is the checkpoint at which four different endpoints exist, and a night counted in requests alone says four where the provider says two hundred and twelve.

**Done when** a night loads membership, backfills a new joiner and checks actions against the provider, the per-name limit still holds over the live path with both carve-outs measured rather than asserted, and each feed's captured response and its live response are read by the same parser.

### 2.5 The news feed
`INewsFeed` invented. News is the only feed with no interface, so it is the only one whose request count no contract forces, and the 1.7 measurement was run through a class the nightly path does not reach. The live implementation behind it.

**Done when** one dated feed request is fanned out to names in code rather than one request being made per name, and the request count is measured on the interface so the live feed answers the same question the double does.

### 2.6 The selection point and a live night
Live against fixture chosen by configuration rather than by a required positional argument. `Nightly.RunAsync` stops constructing the doubles inline, which is where the choice lives today and is why there is no choice.

Both directions are kept and both are asserted. A mistyped fixture path cannot silently reach the network, and a live run cannot silently fall back to a capture. Either one alone is a night that ran against something other than what the operator asked for and said nothing.

**Done when** a night runs end to end against the provider with no fixture folder given, a night given a fixture folder makes no request, and a fixture folder that does not exist is refused rather than resolved to the live path.

### 2.7 Phase 2 report
**Done when** every claim the phase owes is PASS naming an instrument whose declared reach includes it, unexamined is zero, and the fixture holds phase 2 expectations with at least one derived independently rather than frozen from a run.

The claim total is predicted at 2.0 by naming each new claim rather than counting them, and checked here. A prediction of "seven" cannot be missed legibly: a row found later reads as a number that drifted, and a row that turns out unnecessary reads the same way. Named, both are visible as what they are.

---

## Phase 3: levels on the chart

**Visible output at 3.1.** The moving averages are the cheapest thing that draws, so they come first.

### 3.0 Planning
Settles every hole this document's holes table assigns to 3.0, and takes a decision for each. Confirms the checkpoint split below against the size the work turns out to be.

The holes are named there rather than repeated here. A checkpoint listing its own subset is a second statement of the table's contents, and it is the statement that goes stale: this one named two of the three it is now assigned, so a hole filed against 3.0 would have been settled at 3.0 only if somebody happened to read both.

Carries out the citation pass re-pointed here from 1.8 (owes: Architecture cites its decisions by name at each rule), reading the remaining sections and deciding, decision by decision, whether the document states a rule that rests on each. Whatever residue stands afterwards is an open state rather than a closed one and is recorded as such (owes: The decisions the architecture states no rule about, named as such), because a figure carried from 0.5 to 1.8 to here without ever becoming a row is a figure nothing reads back.

Also settles the three carries and the record inconsistency the phase 2 sign-off left, which is where the rule above this table came from: a deferral names what produces the evidence, and the reconciliation that asserts it in both directions.

### 3.1 The indicator engine and the averages on the chart
Contradiction K repaired in `ARCHITECTURE.html` first, in its own commit, for all nine rows rather than the three this checkpoint would otherwise force. The moment `IndicatorEngine` declares its write, `component-access` produces a write nobody declared and a declaration with no cell behind it, which is two faults for one displaced letter.

The repair reconciles every row of the matrix against its catalogue row rather than repairing the rows the contradiction names, which is what found a ninth row and a displaced read the contradiction had not seen. A repair that fixes the list it was handed cannot find what the list left out.

Migration creating `indicator`. The averages, the momentum readings, the typical daily move and the volume ratios, each row carrying the bar count it was computed from.

The chart from 1.3 extended in place with the average lines drawn.

Five obligations fall due here, and they fall due together because this is the first checkpoint after 2.7 that writes code and the first that writes fixture expectations. Two carried out of the phase 1 sign-off (owes: The refetch's atomicity asserted as a property rather than as a construct) and (owes: Every fixture expectation swept for whether a test reads it). Two carried out of the phase 2 sign-off (owes: The weighted-call stop asserted where `nightly-cost` reaches it) and (owes: The membership uniqueness asserted by a permanent test). The expectations 3.0's rulings owe the fixture were written here and are not: none of the three is assertable against a stage this checkpoint builds, so the row moved to 3.4 and the reasoning is in its producer cell. That the rule landed at 3.0 and its own author put a due point one checkpoint too early is the case the reconciliation exists for.

Four more come from 3.0's sweep of the claim notes, and they are one shape rather than four: a clause asserted by a test outside the check's carrier class backs no verdict, because a check's tests are the ones its carrier declares. Each is repaired by moving the assertion into the carrier or by naming the check that already holds it, and the second is sometimes the right answer. (owes: The retention clause asserted where `nightly-cost` reaches it), (owes: The retry policy figures asserted where `nightly-run` reaches them), (owes: The suspect marking asserted where `schema-columns` reaches it) and (owes: The corporate action checker's declaration claims named for the check that reaches them).

**Done when** the indicator values match the fixture, a name with fewer than two hundred bars records its long average as not available with its bar count rather than as a number, and the chart draws the averages.

### 3.2 The swing finder
Migration creating `swing`. Days whose high or low is the most extreme within three bars either side, each carrying the date its lookback completed.

**Done when** the swings match the fixture, and a component reading swings as of a date cannot see one confirmed later. That second condition deserves its own test: a swing is not knowable on its own day, and a reader that ignores the confirmation date sees the future.

### 3.3 The volume profile builder and the profile mark
Migration creating `volume_profile`. Each day's volume spread across that day's range into fixed price bands, over the window settled at 3.0. The profile drawn horizontally against the same price axis as the chart.

**Done when** the profile matches the fixture, the shares in the bands sum to the window's total volume, and the mark renders against the chart's price axis rather than its own.

### 3.4 The level builder and the bands
Migration creating `level`. The four candidate sources merged into bands within half a typical day's move, touches added, roles and strength assigned, and the non-average-anchor flag recorded because the ladder reads it on every band.

The bands drawn behind the candles, completing the level chart mark.

The three rulings 3.0 took become fixture expectations here (owes: Phase 3's expectations owed for 3.0's rulings), because this is the first checkpoint at which all three are assertable: the shelf ruling decides what the builder collects, the retracement ruling decides what it draws between, and the profile window ruling is only observable where a band rests on a shelf.

**Done when** the bands match the fixture with their members and dates, a band anchored only by a moving average is flagged as such, and the chart draws the bands behind the price rather than over it.

### 3.5 The level summary table and the momentum panel
The level table with each band's members and dates, and the momentum readings on their own small axes with their neutral rules drawn.

**Done when** every band in the table names its members and every momentum reading draws its neutral rule, because a momentum number without the band it sits in means nothing.

### 3.6 The fixture widens to four names
A mega-cap in a tight range, a mid-cap in a wide one, and a name that gapped, beside the existing name.

**Done when** every phase 3 expectation holds for all four names, and the carried obligation on the volume shelf threshold is discharged (owes: Volume shelf threshold checked against four names): the threshold is whatever makes all four charts agree with where their volume visibly clusters, rather than the figure derived from one chart.

### 3.7 Phase 3 report
**Done when** every phase 3 claim is PASS across four fixture names, and the predicted claim total is checked against the actual.

---

## Phase 4: the plan

**Visible output at 4.1.** The trend state is one word on a page that already exists, drawn from a store the night wrote.

Nine build checkpoints rather than seven, decided at 4.0 against the size the work turns out to be. 4.1 as this document first wrote it carried a new feed, a new table, a contradiction, a matrix column, a new component and a new nightly chain at once, and done condition 9 raises the cost of every checkpoint, which argues for smaller ones. The phase 3 evidence is that the checkpoints which did not mutate their own work are where the holes were found a phase later.

### 4.0 Planning
The heaviest planning pass in the project, because more holes settle here than at any other point and one of them is a missing component.

Reconciles section 19.1's fixture table against the expectation files the fixture actually holds (owes: Section 19.1's fixture table reconciled against the expectations that exist). It lists ten and the fixture holds three it does not name. This belongs in a planning pass rather than beside a component, because adding a row to a claim-bearing table changes the claim count and the out-of-scope set with it.

Re-points every claim whose due point is a phase rather than a checkpoint (owes: Claims due at a phase rather than at a checkpoint), before phase 4's first checkpoint lands and makes the phase 4 ones fail the way phase 3's did at 3.1.

Turns the fixture's populations into readings of the membership expectation rather than literals in each test (owes: The suite's fixture populations read from the expectation rather than written into each test). Phase 4 adds components whose tests will state the same counts again, so the repair is cheaper before them than after.

Constructs the band the fixture does not hold and asserts the column that reads it (owes: `has_non_average_anchor` asserted over a band anchored on an average alone that a session reached). A band anchored only by a moving average and carrying touches is the case `SCHEMA.md` calls common and the committed fixture has none of, which is worse than a gap because it looks like coverage. It is done here rather than at 4.4, because 4.4's tranche eligibility rests on that column and cannot be built over a test that cannot see the case it exists to decide.

Discharges the four other rows the phase 3 sign-off left at this checkpoint (owes: The momentum panel's reading set asserted independently of the constant it is drawn from), (owes: The band hue mapping asserted, and not only the two hues), (owes: The level read surface asserted over two stored as-of dates) and (owes: The volume profile boundaries the committed fixture cannot reach). The last is four constructed-input tests whose evidence is in hand and which no later checkpoint produces, since nothing after phase 3 reads the profile.

Takes the retention ruling (owes: A deleter for the computed tables, or a retention cell that says what is true), which section 16 states over six tables and `SCHEMA.md` gives to nobody.

**Contradiction E and the calendar.** Four components read a store that nothing declares and nothing writes. The resolution needs a calendar fetcher on the nightly path, a `calendar` table in SCHEMA with its grain and owner, a column in the read and write matrix, and a catalogue row. The earnings date is needed nightly by the ladder builder and the shortlist builder, so it cannot wait for the on-demand fundamentals fetcher in phase 6.

Settles every hole this document's holes table assigns to 4.0, and takes a decision for each. None of them is inferable from what is written, and each decides what the plan section says.

The holes are named in the table rather than counted or repeated here. This checkpoint said "four holes settle here" and then listed four, which was two statements of one fact and both went stale the day two more were filed against it.

**Predicts the phase's claims by naming them,** in two disjoint lists with the arithmetic between them, and 4.9 checks the sum. 3.7 derived the rule this restores: a prediction is over the rows that become assertable, not over the rows that get written, and phase 4 carried the practice nowhere until this pass put it back.

### 4.1 The nightly chain and the trend state
The swing finder, the volume profile builder and the level builder join the night as steps in the order section 14 states. They have shipped since phase 3 and no night has ever run one, so a production store holds no swing, no profile and no band; their only callers are in the suite.

The trend classifier to the rule settled at 4.0, reading swings as of the date through the reader the level builder already uses rather than a second copy of the clause. Migration creating `ladder`, one row per index member per night carrying the trend state and a plan that states why it is empty. The name page composing the chart region it has marks for and drawing the trend state, served from the store the night wrote rather than one a test built.

Nothing is deleted here. This is the first checkpoint at which three of the five computed tables are written by a night at all, and their row counts are the population 4.2's deleter is built against.

**Done when** the ladder row count equals the index size on a completed night, the trend state matches the fixture across four names, a name whose inputs are absent is recorded as not classified with its reason rather than as a range, the page draws the label and the chart region from the night's own store, and each of the five computed tables' row counts after a completed night matches what its own grain predicts, stated per table rather than in one figure over a mixed population.

### 4.2 Retention on the computed tables
The ruling settled at 4.0, implemented for `indicator`, `swing`, `volume_profile`, `level` and `ladder` together: each builder drops the rows that fall out of the window on the night they fall out, declared in `SCHEMA.md`'s ownership table and in each component's access. `move` waits for 5.2, because its component does not exist until then.

Its own checkpoint rather than folded into the calendar, because a deleter built in the checkpoint that first populates the tables is a delete path whose first real exercise is a night nobody has watched, over the tables whose growth is the argument for the ruling.

**Done when** rows past the window are gone from all five tables and rows inside it are untouched, asserted per table rather than in one loop over a mixed population, `writer-ownership` reconciles five new Delete owners in both directions, and the mutation is the window boundary moved by one session.

### 4.3 The calendar fetcher and the earnings date
**The endpoint is probed and captured before the parser is written**, for the reason 1.6 and 1.7 state, and because nothing has yet asked whether this provider answers the calendar over a date range without a ticker. `IEarningsCalendarFeed` with its live implementation and its recorded double, a slot on `NightFeeds`, and the `nightly-cost` carve-out list extended by exactly one file and still asserted to hold exactly the feed implementations.

Migration creating `calendar`. The fetcher on the nightly path, one request for the horizon whatever the universe size, with the endpoint's weight measured and written into section 17's weighted-call row and `RUNBOOK.md` together. The next earnings date on the fact strip.

**Done when** the calendar holds dated events for the fixture names read from a captured provider response, a night fetches them in one request measured off the live feed, a name with no date on file shows an explicit blank, and `component-access` reconciles the four calendar readers in both directions.

### 4.4 Tranches, stops and the invalidation
Tranches on support bands whose low edge is below the close, nearest first, at most three, skipping any band without a non-average anchor. Each stop a daily close below the low edge of the next band beneath it. The invalidation at the lowest band the structure depends on. The condition attached by the rule settled at 4.0. The constructed-input tests for the band and swing cases the fixture cannot reach land here, since this is what reads band edges and roles (owes: The level boundaries the committed fixture cannot reach) (owes: The swing boundaries the committed fixture cannot reach).

**Done when** the tranches and stops match the fixture across four names, a band anchored only by an average carries no tranche asserted against the band 4.0 constructed, a band straddling the close keeps its full width, exactly one condition matches each tranche asserted in both directions, and a name with no eligible band produces a ladder row whose plan states why.

### 4.5 Exits and the near-exit skip
Exits on the resistance bands above the close, at most five, one closer than two typical days' moves from the blended entry listed and not traded, equal fractions per traded exit, the top of the ladder a trailing rule rather than a price. The trailing machinery is what the trend-dependent stop needs, so it lands here too (owes: The trend-dependent stop, which trails in an uptrend rather than sitting at the next band).

**Done when** the exits match the fixture, the skipped exit is listed with its reason rather than omitted, and the fractions sum to the whole of what is held.

### 4.6 The plan column mark and the tables
One vertical price axis with the current price marked in it, stops as horizontal rules, the invalidation the lowest. The tranche table with conditions and stops, the exit table with actions, drawn into the marks file beside the others and served by the app.

**Done when** the figure renders from the ladder with no value drawn that the ladder does not carry, which is the containment property applied to pictures, asserted by reading the mark's own data attributes back against the store rather than by eye.

### 4.7 The event book
The three setups to the proposals settled at 4.0, each with its trigger, entry, stop and target, keyed to a dated event from the calendar and never merging with the position book (owes: The event setups' triggers calibrated from resolved setups).

**Done when** the setups match the fixture, a name with no date on file produces none and says so rather than producing them from a guessed date, and the page states that the triggers are proposals and names the record that will calibrate them.

### 4.8 The arithmetic and the earnings rule
Risk per tranche at the zone midpoint, reward to risk from the first tranche and from the blended first two, the worked sizing example from a risk budget the reader chooses, and the statement of the last prints' one-day moves against the stop distance.

**Done when** every figure is derived rather than stored, the reward-to-risk arithmetic is asserted against cases computed by hand rather than against the code's own output, and the earnings rule fires inside the twenty-session horizon and not outside it.

### 4.9 Phase 4 report
**Done when** every phase 4 claim is PASS across four fixture names naming an instrument whose declared reach includes it, unexamined is zero, the plan section renders whole, and the claims 4.0 named are checked against the actual with every claim that arrived unpredicted named and placed in the list it belonged in.

---

## Phase 5: tonight's list, over the whole index

**Visible output at 5.1.** The universe screen renders the moment five hundred names have bars.

### 5.0 Planning
The phase planning pass belongs here, as its own `PROGRESS.md` entry opening "Not a checkpoint entry".

Settles the base rate's population and window. Confirms the six reason thresholds as the proposals they are, and states that 5.6's own record is what calibrates them, so nothing is tuned in advance.

**Enters the eleven obligations the phase 4 sign-off created and left for this pass to place,** since that entry edits no spec and its due points are proposals rather than placements. Ten fall due here and one at 5.4.

Two are spec corrections and both are the same defect, being a rule the code runs that the corpus does not hold (owes: The trailing stop rule written as a decision that supersedes the one it changes) and (owes: `ReachesTheZone` named in section 10 and in the decision that enumerates the conditions). One is a placement question rather than a test (owes: Section 10's figure rows placed as claims or the corpus stating why they are not).

Seven are tests over phase 4 code, taken here rather than at 5.1 for the reason 4.0 took five of them in its own planning pass: the evidence is in hand, the code already ships, and no phase 5 checkpoint reads it. (owes: The two earnings guards asserted over a print outside the stored bars), (owes: The blended entry asserted over a plan with two tranches), (owes: The invalidation asserted where a tranche has no band beneath it), (owes: The stop distinctness the relabel rests on), (owes: The shock multiple's value and its next-day hold each asserted), (owes: The condition window asserted for a name with more than ten recent sessions) and (owes: The tranche eligibility and near-exit boundaries asserted over constructed input).

Each of the seven carries its own mutation. They are assertions written to close mutations that survived, and a test written to catch a tautology and never mutated is the next tautology.

**Settles every hole this document's holes table assigns to 5.0, and takes a decision for each.** Three of them, and one was filed by this pass: the news feed's paging, which 5.5 would otherwise have discovered at build time. The holes are named in the table rather than counted or repeated here, for the reason 3.0 and 4.0 both state.

**Resolves fourteen faults the phase 4 corpus carried,** each of which would have failed a check or produced a wrong reading during phase 5. Four are worth naming here because a later checkpoint reads them. `listings-coverage` was rostered from 5.1 and `listing` is created at 5.4, so the row would have failed `coverage-reported` the moment 5.1 landed. The universe screen reads listings and 5.1 builds it three checkpoints before that store exists, so its sector strip and its table are decomposed per region as 15.5's level chart is. The run page's reason record is decomposed the same way, its counts at 5.6 and its verdicts at 7.5, because three operating obligations name 5.6 as the surface their trigger is read on and the harness had the whole row at 7.5. And the `ChangeDetector` had a declared column set in `SCHEMA.md` and no row in the catalogue or the matrix, which `writer-ownership` and `component-access` refuse in that direction.

**States the wall clock as a measurement plus headroom rather than as the measurement.** Section 17's row reads under five minutes and is marked proposed, and its own preamble says a proposed value is settled before the phase that depends on it. It cannot be settled before 5.1 runs, so what 5.0 settles is the form: 5.1 records the measured night per step with its conditions, the limit is that measurement plus stated headroom with the headroom's reason beside it, and the night's deadline follows at three times the limit in the document and in the retry policy together. A limit set to the figure that measured it is passed by construction by the night that set it.

**Predicts the phase's claims by naming them,** in two disjoint lists with the arithmetic between them and the pair the sum makes, and 5.7 checks that pair. A prediction given as components is checked by re-deriving it, which is not the same as checking it.

**List A, the 55 claims already placed at a 5.x checkpoint** and waiting for it: 7 at 5.1, 5 at 5.2, 7 at 5.3, 17 at 5.4, 12 at 5.5 and 7 at 5.6. Every one becomes PASS when its checkpoint lands or the phase is not signed off, since unexamined is zero at 5.7.

**List B, the 31 claims this pass itself added,** named rather than counted. Twenty-three came from reading the figures, being figure 9.1's six boxes, figure 10.1's nine and figure 12.1's eight. Two are the change detector's catalogue and matrix rows. Three are the section 15 rows decomposed per surface, the universe screen's sector strip and table each splitting at the listing store and the run page's reason record splitting between its counts and its verdicts. Three are the expectation files section 19.1 did not name, being moves, forward returns and news pulse, which phase 5 produces and the table listed neither.

Fifteen of the 31 pass already, being the two flow figures whose stages have shipped since phases 3 and 4. Sixteen do not: figure 12.1's eight are phase 6, the reason record's verdict half is 7.5, and the remaining seven land inside phase 5 at 5.2, 5.3, 5.4 and 5.5.

**The pair. 234 claims and 162 PASS after phase 5**, with 72 out of scope, 0 unexamined and 0 fail. The arithmetic: 203 claims and 92 PASS at the phase 4 sign-off, plus the 31 of list B and the 15 of them that pass now gives 234 and 107, which is what this pass leaves; plus list A's 55 gives 162, and 162 with the 72 that remain at 6.x and 7.x is 234.

### 5.1 The full universe and the universe screen
Membership and backfill over the whole index. The universe screen: every name, paged, sorted by distance to the nearest level, with the sector strip and the filters.

**The backfill has already run at scale, and the new risk is the computed chain.** 2.6 ran a live night with no fixture folder given: 822 membership rows and 126,235 bars over 503 tickers, which 2.7 restates. What has never run at index size is what comes after the bars, being the indicators, the swings, the volume profile, the levels and the ladder, over 503 names where every night so far has run them over four. That is a different failure mode from a backfill's, and this checkpoint watches the right thing only because it says so.

The `sector` column, declared in `SCHEMA.md` and migrated here rather than at 5.0, because a column declared before its migration is a declaration with nothing behind it. It is read from the `Components` object of the constituents response the loader already fetches, at no extra request, and the permanent test refusing that object as the index stands: it is read for the sector and never for the membership.

The screen draws what exists before `listing` does. Its sector strip counts the sectors and how many names are in an uptrend; its table draws every name with its distance to the nearest level. The regions that count how many are on tonight's list, and the columns carrying the evening a name was last on it and the listing strip, are 5.4's, which is where the store they read is created.

**The wall clock is a live measurement rather than a suite assertion**, because it is a property of the running system, and such a property is asserted by a guard the code carries and by a figure a person reads. The guard exists as the night's deadline from 2.2, and nothing in the harness reaches `data/`. A 500-name store generated for the suite is explicitly not built: it would be an assertion over a payload no provider produced, which is the class 5.0 has just named.

**Two limits move off this checkpoint, because it produces neither's evidence.** Section 17's row coverage claim is about a listings row for every name every night, and `listing` is created at 5.4; it is the same fault as the `listings-coverage` roster row and it moves to the same place. And the wall clock at index size needs a night that ran on a schedule, which 5.1 does not produce: what 5.1 produces is a night run by hand on the machine at hand, which is one observation and not a limit. It moves to 5.7, whose done condition is already a week of unattended nights and which already carries the posting hour for exactly that reason. A deferral names what produces the evidence, and this checkpoint produces one night rather than a distribution.

What 5.1 does carry is the guard: every stage records the instant it started and the instant it ended, and the night's own deadline stops a run that has hung. The measurement 5.1 records is one night with its conditions, stated as an observation rather than as a bound.

**Done when** every index member holds its year, the computed chain completes for every member, the screen reaches every name rather than the first page of them, every stage of a night records the instant it started and the instant it ended, and a night over the whole index is recorded with the elapsed time per step, so a night landing inside its limit by one step doing nothing is legible rather than hidden in a total; with the run's own conditions, being a cold or warm store, which network, and whether the provider was slow that evening, since a single figure with no conditions is one observation read as a bound; and with what it does not establish stated in the same entry, being one night on one machine at one moment. Section 17's limit is set at 5.7 from the week of nights that produces a distribution, as that measurement plus stated headroom with the headroom's reason, and the deadline follows at three times it.

### 5.2 The move annotator
Migration creating `move`. The largest single-day and multi-day moves of the stored year, which become the rows of the how-it-got-here table with the cause column empty until phase 5.

`MoveAnnotator` declared its own deleter in `SCHEMA.md`'s ownership table and in its access, which is the last of the six computed tables and discharges 4.0's retention ruling in full (see: Every computed table's writer is its own deleter). Section 16's store row covering all six is owed here for that reason. The `sessions` column 5.0 added is what makes the catalogue row true, since a table keyed on one session with no span could carry only the single-day half of what the annotator selects.

**Done when** the moves match the fixture with the span of each, the table renders with its cause column explicitly absent rather than blank, and rows past the window are gone with rows inside it untouched.

### 5.3 The facts assembler and the change detector
Migration creating `facts`. Every number the computed sections may use, each with its source, and the hash. The change detector writing only the material-change list, on disjoint columns of the same row.

The change detector's catalogue and matrix rows landed at 5.0, so the component has a row to be reconciled against before it exists. Its listings read is what the retention needs: a facts row is kept whole for a night the name fired and every other night keeps the hash and the material changes, with the payload emptied by the detector, which already owns Update on that table.

**Done when** the facts file matches the fixture byte for byte, the per-operation split is proved rather than true by construction, since a facts re-run must not blank the change list, and a payload from a night the name did not fire is emptied while its hash stands.

### 5.4 The shortlist builder and tonight's list
The plan column's condition sentences fall due here, on the surface a person reads them on (owes: The plan column's condition sentences asserted on the surface a person reads). `NameScreen`'s condition-to-words mapping ends in a catch-all arm, so a sixth condition, a typo or an unset value renders as the same sentence with nothing failing, and no test in the suite asserts any plan sentence at all. The catch-all is made to fail rather than to render, and 5.4 is the checkpoint that builds the surface those sentences sit beside.

Migration creating `listing`. **A row for every index member every night**, whether or not a reason fired, carrying the reasons with their values and the plan as it stood that night.

Tonight's list: the header with the true fired count, the watch list above it, twenty rows drawn, the reasons on each row. The list orders on band strength as its tiebreaker, so the ruling on what that score is dominated by falls due here (owes: The strength score read against four names).

Contradiction L repaired, to 5.0's ruling: the shortlist reads levels, indicators, ladders, the calendar, the facts file and the bar store, and reads no fundamentals, because it selects on chart state alone with no fundamentals and no model in the decision. `listings-coverage` is implemented and promoted on the roster here rather than at 5.1, following the shape `LadderBuilder` already set: the population is the index read from membership and not the names with bars.

Section 17's list display row is asserted against a constructed night of forty names, which is a constructed store as 4.0's average-anchor band was and not a constructed provider payload. The universe screen's listing-dependent regions land here, deferred from 5.1.

**Done when** the listing row count equals the index size on every completed night, the fired count in the header matches the reasons, a night with more than twenty fired names draws twenty and states the true count, every stored listing carries its entry, stop and first traded target, and the plan column's condition sentences are asserted on the surface a person reads them on, with the catch-all arm made to fail rather than to render.

The plan-at-listing column is the one thing in this phase that cannot be added later. Bars can be replayed and the plan cannot, because by the time a verdict is possible the rules may have changed and recomputing would score old listings under new ones.

### 5.5 The forward return filler and the news pulse counter
Migration creating `forward_return` and `news_pulse`. The five and twenty-one session outcomes filled as they mature, the base rate computed to the definition settled at 5.0, and the article count per name from one feed request.

The pulse count answers the obligation 2.5 created (owes: One day of news exceeds one request at the provider's limit), whose evidence is already in hand: one live dated request returned a thousand articles over more than three thousand symbols and reached the provider's cap, so a count taken from one request is a count over whatever the cap happened to include. The counter carries a window or a page.

The counter is built to 5.0's paging ruling: one dated query paged until the day is covered, every page counted, a day reaching the stated maximum refusing rather than truncating, and the count fanned out to names in code. Its matrix row is blank in every column today and attributing articles to names needs a membership read, which is filled here.

The base rate is computed to 5.0's definition, over every name-night in the window rather than over the rows where a reason fired, null for the `setup` horizon, and stored beside the return it is shown against.

**Done when** an immature row reads as not yet matured rather than as a blank or a zero, a whole day is covered rather than a capped sample of it, the page count is measured over two universe sizes and shown not to grow with the population, and the base rate names the population it was computed over on the surface it is read on.

### 5.6 The run page
The operational header, and each reason's record with the base rate pinned as the first row.

This checkpoint builds the surface three operating obligations are read on (owes: The six reason thresholds calibrated from the nights they fired on), (owes: The three reason records that need resolved setups) and (owes: The event setups' triggers calibrated from resolved setups). None is due at a checkpoint, because no checkpoint accumulates nights or resolved setups; what 5.6 owes is that each trigger is legible on the page when it fires, which is the count of nights beside the reason record, the resolved count against its minimum, and the same count for the event book's own setups.

15.10's reason record row is decomposed per surface, and this checkpoint builds the half it can: the resolved count, the count of nights beside it and the base rate pinned as the first row, all drawn in the not-yet-measured state (see: Not yet measured is drawn as a dashed outline, never as a pale value). The verdicts are 7.5's, because they need resolved setups and no checkpoint accumulates those.

**Done when** no forward-return figure is shown without the base rate beside it, and a reason below the minimum shows a dashed outline carrying its count rather than a rate.

### 5.7 Phase 5 report
The week of unattended nights is what produces the posting hour (owes: The provider's posting hour for the day's bulk file, measured from live fetches). It stood at 3.7, which is the phase 3 report and produces no evenings; a week of nights that ran on a schedule is a week of observations of when the file actually appeared, and it is the first thing in the plan that yields several.

The week of nights is also what sets the nightly wall clock at index size, which stood at 5.1 and which 5.1 cannot produce: one night run by hand is an observation and a limit needs a distribution. The limit is that week's measurement plus stated headroom with the headroom's reason, and the night's deadline follows at three times it, in section 17 and in the retry policy together.

**Done when** every phase 5 claim is PASS naming an instrument whose declared reach includes it, unexamined is zero, a week of unattended nights has run with the list current each morning, the posting hour is recorded from those nights rather than from documentation, the wall clock limit is set from those nights with its headroom stated, and the pair 5.0 predicted is checked against the actual pair, with every claim that arrived unpredicted named and placed in the list it belonged in and every predicted claim that did not arrive named as such.

---

## Phase 6: research on demand

**Visible output at 6.1.** The numbers section fills from real filings.

### 6.0 Planning
Settles which local model, the section-to-lane configuration shape, and how a research pass is recorded so a fixture can replay it without a network. Discharges the source-lists obligation from 1.7 against the coverage measurement (owes: Source lists reviewed against measured coverage), whose evidence has been in hand since 1.7 and which sits here because 6.0 is where the lists are next used rather than because anything is waited on.

### 6.1 The fundamentals fetcher and the numbers section
Migration creating `fundamentals`, one row per filing date, never updated. The quarters, balance sheet, segments and guidance, each with the filing date it came from.

**Done when** the numbers section renders, a stored copy predating a filing triggers a fetch and one that does not triggers nothing, and the bulk fundamentals probe is recorded either way (owes: Bulk fundamentals endpoint probed on the operator's key). The probe is one live call on a key the credential path has held since 2.1, so nothing here waits on evidence.

### 6.2 The source store and admissibility
Migration creating `source_document`. The admissibility test running after the fetch and before storage: denied categories, a publish date inside the window, and a preference for primary sources. A document that fails is kept as a row with its refusal reason and no body.

**Done when** each denied category is refused by a fixture document, a document whose text cannot be retrieved is discarded rather than cited, and the refusal is visible rather than silent.

### 6.3 The claim checker
Migration creating `research_section` and `theme_section`, one row per section per version. Numbers checked against the facts file, claims checked for a stored admissible source, one retry then the section omitted.

**Done when** the poisoned paragraph and the unsourced claim are both rejected, a section failing twice is absent with its reason rather than guessed, and nothing rejected is written.

### 6.4 The prose writer
The local model writing the short version and the figure keys from numbers, at no cost, through the claim checker like anything else.

**Done when** a pass runs against the recorded endpoint with no network, and the footer states which model wrote each section.

### 6.5 The research runner
Filings and news read by ticker, the theme record read, the narrative sections written, every document stored as it is fetched.

A pass that reproduces byte for byte from a recording is what makes the lane boundary measurable rather than asserted (owes: The research lane boundary measured against the fixture both ways), so each section can be run on both models against the same evidence and compared.

**Done when** every claim in a written section names a stored source, and a pass over the fixture reproduces byte for byte from the recorded endpoint.

### 6.6 The theme research runner
Industry research once per theme, shared by every name in it, with the search tool called as one more fetching tool and its results stored like any other document. The theme query names the industry rather than a ticker, carries a date range, is restricted to the industry list, and requests full page text.

**Done when** a theme refresh serves every name in its industry from one pass, and a search returning snippets rather than text produces no stored document.

### 6.7 The staleness judge
The four questions answered from data the nightly run already computed: a new filing, the earnings date passing, the news pulse above its baseline, or a manual refresh.

**Done when** each trigger fires its own test, a second open of an unchanged name spends nothing, and deciding not to spend costs nothing.

### 6.8 The overnight queue
The local model writing the sections in the local lane for listed names whose research is missing or stale, in priority order, until the configured time limit, holding the machine awake, making no paid call.

**Done when** the queue completes with zero spend, a busy night leaves names for the next night rather than running past its limit, and a night where the machine slept is reported rather than silent.

### 6.9 Phase 6 report
**Done when** every section the report specifies is produced on the current hardware with the synthesis sections written by the paid model on demand, and the spend cap stops a pass rather than warning about one.

---

## Phase 7: the improvement loop

**Visible output at 7.1.** The resolution counts appear on a run page that already exists.

### 7.0 Planning
Confirms the guardrail values against the evidence that has accumulated. Nothing here is tuned to what the data turned out to be; a threshold changed after seeing results starts a new window and the old one is kept.

### 7.1 Setup resolution
The setup horizon on forward returns: target reached, stop closed through, or the time cap expired. The resolution counts on the run page.

**Done when** each of the three outcomes has its own test, a timed-out setup is counted in its own column and never in a rate, and the counts render.

### 7.2 The break-even score
The hit rate each plan demanded, computed from its own entry, stop and target, and the share of setups that beat it.

**Done when** the arithmetic is asserted against worked cases, and a verdict below the minimum is withheld with its count shown against the minimum.

### 7.3 The candidate register
Migration creating `candidate_register`, append only. Registration before scoring, with the rule, the test and the date, and a stated maximum family size.

**Done when** an update and a delete are both refused at the store, a retirement is a new row naming what it retires, and the correction divisor matches the rows registered before the window opened.

### 7.4 The shadow column
Registered candidates evaluated nightly on every name-night exactly as live reasons are, written to the shadow column and shown nowhere.

**Done when** a shadow candidate is evaluated on nights no live reason fired, which is what the every-name listing row exists for, and nothing shadow reaches any screen.

### 7.5 Reason verdicts on the run page
Each reason against the bar its own setups demanded, with the resolved count and the divisor beside it, and the base rate pinned.

**Done when** no verdict appears below its minimum, every verdict names its divisor, and the three display states are each proved.

### 7.6 Rule versions scored counterfactually
Each ladder rule a named version, every night scored under every version from stored bars.

**Done when** a version change opens a new window and the previous one is kept, and a rule is not changed while a window measuring it is open.

### 7.7 The model's proposal
The model's own entry and exit proposal stored beside the computed one and scored on the same break-even rule, never overriding it.

**Done when** the proposal is stored, scored and displayed as a second opinion, and no computed number is sourced from it.

### 7.8 Phase 7 report
**Done when** every guardrail has its own test, and the loop has changed nothing on the strength of evidence below its stated minimum.

---

## Carried obligations

Recorded when created, not remembered. Every row is named, and the name is what a document or a comment cites, in the form `owes:` followed by the exact name and nothing else.

**Each row is one of two forms and never neither, and `obligation-reconciles` reads the form off the cells rather than off what the row calls itself.** A checkpoint row names in `Due at` a checkpoint that produces the evidence, and that checkpoint's own text cites the obligation back. An operating row carries the literal `operating` and states in the last column a numeric trigger, the surface the trigger is read on, and the checkpoint that builds that surface, whose text carries the citation. Its last column opens with the trigger and names the surface after the words `read on the`, so the check reads the trigger from a position rather than guessing which of several numbers in a sentence is the one that fires. A row carrying a checkpoint due point and a trigger with a surface is neither form: it reads as tracked from either end and is chased from neither.

| Obligation | Created at | Due at | What produces the evidence |
|---|---|---|---|
| **Architecture cites its decisions by name at each rule** | 0.5 | 3.0, discharged | 3.0 read the remaining sections and decided, decision by decision, whether the document states a rule that rests on each. 111 citations covering 85 of the 90 current decisions, from 39 covering 29 of 81. Discharged at 1.8 for the rules phase 1 reaches and here for the rest |
| **Splits and dividends parser checked against itself** | 1.2 | 1.6, discharged | 1.6 captures the feed before the parser is written, which is what makes a fixture the parser did not author |
| **News parser checked against itself** | 1.2 | 1.7, discharged | 1.7 captures the feed before the parser is written, for the reason 1.6 states |
| **`two-platform` widened to what its roster row claims** | 0.7 review | 1.4, discharged | 1.4 is the first checkpoint that depends on both shells behaving the same |
| **Absolute path matching anywhere in a value, not position zero** | 0.7 review | 1.3, discharged | 1.3 is where exception text first reaches `run_log.detail`, and exception text carries absolute paths mid-string |
| **News feed queryable by date without a ticker** | authored with the architecture | 1.5, discharged | 1.5 puts the question to the feed |
| **Volume shelf threshold checked against four names** | authored with the architecture | 3.6, discharged | 3.6 widened the fixture to four names of different character, and the widening is the evidence. The figure holds: at twice an even share every one of the four has a shelf and it is two to four bands of twenty, holding a fifth to a half of the period's volume. The candidates either side fail in opposite directions, which is what makes this a calibration rather than a number that happens to work. At one even share, seven to ten bands of twenty are shelves, which names most of the chart. At three, three of the four names have no shelf at all and the level builder loses its fourth source for them |
| **Source lists reviewed against measured coverage** | 1.7 | 6.0 | evidence in hand: the coverage measurement taken at 1.7 across thirty names spread over the capitalisation range. Nothing waits on evidence; 6.0 is where the lists are next used |
| **Bulk fundamentals endpoint probed on the operator's key** | authored with the architecture | 6.1 | one live call on the key the credential path has held since 2.1. Nothing waits on evidence, and 6.1's done condition records the probe either way |
| **The refetch's atomicity asserted as a property rather than as a construct** | 1.8 | 3.1, discharged | a refetch is interrupted after the delete and partway through the inserts, and the stored series is asserted to be the old one entire, session by session and price by price. Removing the transaction turns it red and leaves the other eight tests green, which is what the sign-off found |
| **Every fixture expectation swept for whether a test reads it** | 1.8 | 3.1, discharged | swept per key rather than per file, because a file-level sweep reports four of four and misses the defect it was filed for. 44 keys over 4 files, 3 unread and each named. The sweep found something sharper than itself, below |
| **The provider's posting hour for the day's bulk file, measured from live fetches** | 2.0 | 5.7 | 5.7's done condition is a week of unattended nights, which is what produces evenings of observation. Bounded at 2.1 and again at 2.6. It stood at 3.7, which is the phase 3 report and produces no evenings |
| **One day of news exceeds one request at the provider's limit** | 2.5 | 5.5 | evidence in hand: 2.5's live request returned 1,000 articles over 3,232 symbols and reached the provider's cap. 5.5 builds the counter that has to answer it |
| **The weighted-call stop asserted where `nightly-cost` reaches it** | 2.7 sign-off | 3.1, discharged | 3.1 constructs a night at the allowance, which is what makes the stop observable rather than read. All three clauses of the note now sit in the carrier, and deleting the stop turns `nightly-cost` red |
| **The membership uniqueness asserted by a permanent test** | 2.7 sign-off | 3.1, discharged | 3.1 puts the three refusals and the control against a store the shipped runner migrated, and asserts the index's form off the built store, so dropping the uniqueness and dropping the fold fail different sets |
| **The decisions the architecture states no rule about, named as such** | 3.0 | 3.0, discharged | closed on the reading. Five stand uncited and each is a decision the document states no rule about: the seam for a second universe, which is a data model SCHEMA carries and no rule here rests on; the outward-request scan's carve-out, which is about a check's internals; and three about how the build is run, being clean spec edits, a rule never blocking a fix, and the solution file format. A citation cannot be placed at a rule that does not exist, and adding one to create a home for a citation would be writing spec text to satisfy a count |
| **Phase 3's expectations owed for 3.0's rulings** | 3.0 | 3.4, discharged | 3.4 builds the level builder, which is the first point at which all three of 3.0's rulings are assertable against the fixture: the shelf ruling decides what the builder collects, the retracement ruling decides what it draws between, and the profile window ruling is only observable where a band is built from a shelf. It was written as 3.1 by the pass that landed the rule, and 3.1 can assert none of the three: the indicator engine reads no profile, no swing and no shelf. All three are in the levels expectation and one test reads them: the profile window against the level window, the five retracements landing together on one band, and MSFT's band at 386.6219 whose only anchor is a shelf, which is a band on volume alone and is the band the other reading of the shelf ruling would not produce |
| **The retention clause asserted where `nightly-cost` reaches it** | 3.0 sweep | 3.1, discharged | moved into `NightlyCost` rather than copied, and widened: the row states two things and the second, that nothing inside the window went, was not asserted where the first was |
| **The retry policy figures asserted where `nightly-run` reaches them** | 3.0 sweep | 3.1, discharged | moved into `NightlyRun` rather than copied. A limit stated in a document and again in code is two places holding one fact, and the same assertion in two test classes is a third |
| **The suspect marking asserted where `schema-columns` reaches it** | 3.0 sweep | 3.1, discharged | resolved the other way the obligation permits: the claim stops saying what its check does not do. `schema-columns` asserts the columns and types, and the marking is the corporate action checker's, claimed by its own failure row |
| **The corporate action checker's declaration claims named for the check that reaches them** | 3.0 sweep | 3.1, discharged | both claims moved to `component-access`, with the reach declarations moved with them, because the notes describe a reconciliation only that check performs |
| **Claims due at a phase rather than at a checkpoint** | 3.1 | 4.0, discharged | 4.0 is the next planning pass and the one before phase 4's first checkpoint lands, which is when the next of these fails. A claim whose due point is a phase becomes invalid the moment that phase's first checkpoint is recorded, because `HasLanded` reads a phase as landed from any checkpoint in it. Phase 3's eight were repaired at 3.1 because 3.1 landing is what made them fail; 66 remain, being 5 at phase 4, 20 at phase 5, 29 at phase 6 and 12 at phase 7, and every one of them will fail on its phase's first checkpoint exactly as these did |
| **The suite's fixture populations read from the expectation rather than written into each test** | 3.6 | 4.0, discharged | evidence in hand: 3.6 measured it. Widening the fixture from three names to four turned 28 tests red, and repairing them touched twenty-five assertion sites across six test classes. One of the twenty-five was a named constant covering six assertions, which is the shape the other twenty-four should have had. 4.0 is the pass before phase 4 writes more of them, and turning a literal into a reading of the membership expectation is a change to how the suite is written rather than to what any one checkpoint builds, so it belongs to a pass that can make it across the whole suite at once |
| **Section 19.1's fixture table reconciled against the expectations that exist** | 3.1 | 4.0, discharged | 4.0 is the next planning pass, which is where a change to a claim-bearing table can be made with its effect on the claim count understood. The table lists ten expected files and the fixture holds three it does not name, being membership, fetch and series state, each written by a checkpoint that needed one. `fixture-replay` found the third by reporting a populated table nothing expected; the first two have been unlisted since 1.1 and 2.1 |
| **The research lane boundary measured against the fixture both ways** | authored with the architecture | 6.5 | 6.5 is where a pass over the fixture reproduces byte for byte from the recorded endpoint, which is what lets each section be run both ways and compared. The decision named phase 6 and this names the checkpoint inside it that produces the recording, which sharpens the point rather than moving it |
| **The six reason thresholds calibrated from the nights they fired on** | 5.0 | operating | 60 nights of listings, read on the run page, which 5.6 builds. 60 because it is the quarter of trading the level window already uses, long enough that a distribution of fired counts is not one week's weather. No checkpoint accumulates nights, so 5.6 makes the trigger readable rather than producing it |
| **The momentum panel's reading set asserted independently of the constant it is drawn from** | 3.7 sign-off | 4.0, discharged | 4.0 states the four readings where a test can read them rather than asserting the drawn names back against the set they were drawn from. The hole was asymmetric, which is what identified it: dropping `macd_hist` drew three readings and left the suite green, while adding `atr14` turned it red only because `NeutralOf` throws on a reading with no neutral rule, so the test was sensitive to the set through an exception and never through membership |
| **The band hue mapping asserted, and not only the two hues** | 3.7 sign-off | 4.0, discharged | 4.0 asserts which band got which hue. The test asserted that three fills carry exactly two distinct hues and that one contains the support token and one the resistance token, and never which was which, so swapping the two constants drew every support band in the resistance hue and left the suite green |
| **The trend-dependent stop, which trails in an uptrend rather than sitting at the next band** | 4.4 | 4.5, discharged | 4.5 builds the trailing rule at the top of the ladder, which is the same machinery: a stop that follows a swing low rather than naming a price. 4.4 places every stop at the low edge of the next band beneath, which is the rule section 10 states for a range, and **The stop rule depends on the trend state** says an uptrend trails the last higher low instead. Three of the four fixture names are in an uptrend, and the literal rule puts their trailing stop inside the band the first tranche sits on, which is why it is a checkpoint's work rather than a line |
| **`has_non_average_anchor` asserted over a band anchored on an average alone that a session reached** | 3.7 sign-off | 4.0, discharged | 4.0 constructs the band, because the committed fixture holds none: every band in it anchored on an average alone happens to carry no touch, so computing the column over the whole band rather than over the anchors leaves the suite green. `SCHEMA.md` says the case is common. Moved here from 4.4 by the operator, because 4.4 reads this column to decide whether a tranche exists and cannot be built over a test that cannot see the case it exists to decide |
| **The level read surface asserted over two stored as-of dates** | 3.7 sign-off | 4.0, discharged | 4.0 stores one name on two nights and asserts the page holds the later night's bands alone. Deleting the clause that binds `as_of` to the maximum leaves the suite green, because the fixture holds one as-of date per name and the two queries cannot differ over it. The constructed store is also what 4.2's retention test is asserted against |
| **The volume profile boundaries the committed fixture cannot reach** | 3.7 sign-off | 4.0, discharged | 4.0 writes four constructed-input tests, for the largest remainder tie order, the one price session, the collapsing edge guard and the top edge taking the window's own high. Nothing after phase 3 reads the profile, so no later checkpoint produces this evidence and a due point naming one would name a point that produces nothing |
| **The swing boundaries the committed fixture cannot reach** | 3.7 sign-off | 4.4, discharged | 4.4 writes the constructed-input tests over the level arithmetic, and the swing cases land with them rather than one checkpoint writing one of the two sets. It stood at 4.1, where the trend classifier reads swings and reaches neither case: the plateau rule and the outside day that is a peak and a trough at once are unreachable from four names of committed bars and from a classifier that only asks which swing is later |
| **The level boundaries the committed fixture cannot reach** | 3.7 sign-off | 4.4, discharged | 4.4 places tranches on band edges and roles, which is what the merge distance boundary, the role boundary at the close and the retracement zero span guard are about, and it is the next checkpoint to write constructed-input tests over them |
| **A deleter for the computed tables, or a retention cell that says what is true** | 3.7 sign-off | 4.0, discharged | 4.0 rules that every computed table's writer is its own deleter, and 4.2 implements it. The row stood at 5.2, which is where the last of the six tables arrives rather than where a deleter is built, so it named a point that produces no evidence. `SCHEMA.md`'s ownership rows stay as they are until 4.2, because a deleter declared before the component deletes is a declaration with nothing behind it |
| **The strength score read against four names** | 3.7 sign-off | 5.4 | 5.4 builds tonight's list, which orders on band strength as its tiebreaker over at most twenty drawn rows. The evidence is in the phase 3 sign-off and needs no checkpoint to produce it: touches are 72 to 79 per cent of the members of each name's strongest band, anchor counts are bunched at 4 to 15 while touch counts run 0 to 41, and MSFT's top two bands carry the same 4 anchors with the ranking decided by 15 touches against 9 |
| **The event setups' triggers calibrated from resolved setups** | 4.0 | operating | 250 resolved event-book setups, read on the run page, which 5.6 builds and 7.5 fills with verdicts. 250 because it is the minimum section 17 already states before a verdict is reported at all, and the same clustering argument applies: event setups fire around prints, so they cluster by date harder than the reasons do. No checkpoint accumulates resolved setups, and 4.0 has no basis to correct a figure nothing has scored, so every trigger, entry, stop and target in the three setups is stated on the page as a proposal until this fires |
| **The three reason records that need resolved setups** | authored with the architecture | operating | 250 resolved setups, which is the minimum section 17 already states, read on the run page, which 5.6 builds and 7.5 fills with verdicts. Phase 7 arriving does not supply months of accumulation; phase 5's storage obligation is the mitigation |
| **The trailing stop rule written as a decision that supersedes the one it changes** | 4.9 sign-off | 5.0, discharged | 5.0 is the pass that reads the corpus before phase 5 builds on it. The stop is the higher of the band beneath and the last swing low beneath the tranche, which the code has run since 4.5 and no spec says: figure 10.1 read "the stop trails the last higher low", the decision said the same, and the phrase "higher of" appeared in two records, a source comment and an expectation note. A decision is changed only by another decision, so the rule the code runs was a rule the corpus did not hold |
| **`ReachesTheZone` named in section 10 and in the decision that enumerates the conditions** | 4.9 sign-off | 5.0, discharged | the same defect in a second place. Figure 10.1 calls its list fixed and names four patterns, the decision enumerates the same four, and the fifth reaches the store and the page, where it reads "buy on the price reaching the zone". The string appeared nowhere in the corpus |
| **Section 10's figure rows placed as claims or the corpus stating why they are not** | 4.9 sign-off | 5.0, discharged | placed as claims, and the repair is wider than the row asked for. Nothing read any figure: the reader matched table elements, every figure is a div, and the placement check asserted that every table the reader returned was placed, so its completeness was defined by the thing it was checking. Four figures and 59 boxes were unread. Figures 9.1 and 10.1 are claim sources reached by `fixture-expectations`, figure 12.1's eight boxes are out of scope until phase 6, and figure 5.1 is placed as the system diagram with every box asserted to name a component or store sections 7 and 16 carry. 226 claims from 203, 107 PASS from 92 |
| **The two earnings guards asserted over a print outside the stored bars** | 4.9 sign-off | 5.0, discharged | both guards produce a number rather than an absence when they are gone. Without the earlier-session guard a print older than the stored bars reports a one-day move equal to the price itself, since the missing prior bar reads as a close of zero; the later-session guard is not independently observable, which the 5.0 entry records as a correction to the sign-off's own description. Both would reach the earnings rule on the page as figures (see: Code owns every number) |
| **The blended entry asserted over a plan with two tranches** | 4.9 sign-off | 5.0, discharged | the figure section 17 names as what the near-exit skip is measured from. Taking the first tranche alone left the suite green, because no fixture name has two tranches far enough apart for the two readings to differ. Asserted over midpoints of 99 and 89, so the mean is 94 and the first alone is 99, with the two resistance bands either side of exactly two typical days' moves from 94 |
| **The invalidation asserted where a tranche has no band beneath it** | 4.9 sign-off | 5.0, discharged | the fallback to a tranche's own low edge, which no fixture name reaches: the three uptrend names take a stop from the trailing rule and the range name has a band beneath all three of its tranches, and the ladder expectation says as much in its own invalidation note |
| **The stop distinctness the relabel rests on** | 4.9 sign-off | 5.0, discharged | the group A survivor's own remedy, which is the invariant written down rather than a stronger assertion at the site the mutation touched. Each stop is at or above the low edge of the band beneath its tranche and below its own tranche's low edge, so the stops are strictly decreasing and at most one can sit at the invalidation price. Asserted over constructed bands, over the four fixture names, and on the surface the relabel it buys is read on |
| **The shock multiple's value and its next-day hold each asserted** | 4.9 sign-off | 5.0, discharged | the pair was asymmetric: the lookback beside the multiple turned a test red and the multiple itself did not, because the constructed case used a move large enough for either figure and the negative case a move small enough for either. Asserted at exactly three typical days' moves and one hundredth past it, and over a window that does not hold its low (see: The event setups' triggers are proposals until resolved setups can score them) |
| **The condition window asserted for a name with more than ten recent sessions** | 4.9 sign-off | 5.0, discharged | the branch no test entered at all, because every constructed window in the suite was exactly ten sessions long and a name with more than ten stored sessions is every name. Asserted over fifteen sessions with the breakdown outside the window and inside it, and at the boundary either side |
| **The tranche eligibility and near-exit boundaries asserted over constructed input** | 4.9 sign-off | 5.0, discharged | two boundaries the committed fixture cannot reach: a band whose low edge equals the close exactly, and an exit at exactly two typical days' moves from the blended entry. The second is asserted beside the blended entry, which is the arrangement that pins both |
| **The plan column's condition sentences asserted on the surface a person reads** | 4.9 sign-off | 5.4 | 5.4 builds the surface those sentences sit beside. `NameScreen`'s condition-to-words mapping ends in a catch-all arm, so a sixth condition, a typo or an unset value renders as that same sentence with nothing failing, and no test in the suite asserts any plan sentence at all. The catch-all is made to fail rather than to render |

**Carried out of the phase 1 sign-off.** Two defects found by breaking a passing claim and
watching the suite stay green. Neither falsifies shipped behaviour, so under the stopping rules
both are carried rather than reopening the phase, and both fall due at 3.1 because that is the
first checkpoint that adds expectations to the fixture: the sweep and the atomicity test land
where the next expectations are written rather than after them.

The refetch's atomicity is claimed and untested. Removing the transaction from the corporate
action refetch leaves the whole suite passing, because both tests that claim atomicity induce
their failure upstream of the only destructive statement and neither can observe a half-replaced
series. What is owed asserts the property rather than the construct: interrupt a refetch partway
and confirm the stored series is the old one entire rather than a mixture. A scan for the keyword
reports the construct, and reporting the construct is what let this pass.

An expectation file that nothing reads. Renaming a traded session as a market closure in the bars
expectation leaves the whole suite passing, because the fixture refutes it and no instrument
consults it. What is owed is a sweep of every file under `fixtures/` named as an expectation,
reporting which are read by a test and which are not, with the count stated before looking. An
expectation nobody reads is the same object as a check that runs nothing.

**Discharged at 1.4.** `two-platform`, widened from asserting that the workflow names two runners to asserting that no leg can report green without running the suite: no `continue-on-error`, no swallowed failure, every leg invoking a CI script rather than a bare `dotnet test`, the Linux instrument outside the matrix so "both" still means two, and the full history fetched on every leg that reads it.

**Carried out of 1.8.** The remainder of the citation pass. 39 citations now stand in `ARCHITECTURE.html` against 6 before, covering 29 of the 81 current decisions. The 52 uncited are not one omission: most settle things the architecture states no rule about, being about process, phase order, scope or how the build is run, and a citation cannot be placed at a rule that does not exist. What is owed is a pass that reads the remaining sections and decides, decision by decision, whether the document states a rule that rests on it. That belongs where the sections it would read are being worked on rather than in one sweep at a phase boundary, so it is re-pointed to 3.0 with the figure recorded here.

**Carried out of 1.4.** No feed reaches the network. Every provider implementation in the tree is a recorded double, so `tools/nightly` takes a fixture folder and a live night cannot run. Captures have been made by hand at 1.1, 1.2 and 1.4. This is a hole rather than a defect in any checkpoint, and it is filed here rather than named only in a commit: the HTTP feeds and the credential path from configuration to request have no checkpoint that builds them.

**Discharged at 1.2.** The money column list, now read from SCHEMA's own Notes cell rather than kept beside the check. The stream ordering that can deadlock, with a probe that fills both pipes and a test bounded by a timeout, since the failure is a hang rather than a wrong answer. And the runtime money guard, which refuses anything that is not a decimal at the point a price is bound.

**Discharged at 1.1.** `writer-ownership` widened to both directions its roster row claims, which building the first component forced rather than allowed: two of its tests asserted over a population of zero and turned red the moment `MembershipLoader` landed. The zoneless instant refused, with `clock-usage` widened to read code rather than prose. The manifest checker opening every captured response and checking the file it names exists, which the first committed fixture made assertable.
