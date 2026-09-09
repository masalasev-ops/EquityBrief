# CHANGELOG.md

The prior text of every clean edit to a spec. `changelog-reconciles` reads the git history and fails any commit that deleted a line from a spec without changing this file.

Specs are `CLAUDE.md`, `ARCHITECTURE.html`, `SCHEMA.md`, `BUILD_PLAN.md` and `RUNBOOK.md`. Records correct themselves with new dated entries and do not appear here.

Nothing in the corpus is struck through. A spec is edited cleanly and what it said before is written down here, with the decision that authorised the change or the defect it repairs.

## Entry format

```
### YYYY-MM-DD - <file> - <what changed>
Authorised by: <a decision name, cited exactly>   OR
Corrects: <the defect, in one line, and how it was found>
Was:
> the prior text, verbatim
Now:
> the replacing text, or a note that the passage was removed
Why: one or two sentences
```

An entry names one or the other and never neither. A change that alters what the corpus decides cites the decision that authorises it; a change that repairs a spec which was wrong when written names the defect, because there was no decision to make and inventing one to fill the field would make the field unreadable.

---

## Entries

### 2026-09-09 - CLAUDE.md, BUILD_PLAN.md - schema-columns reads the index it always claimed
Corrects: `schema-columns`'s roster row said less than the check now does. Migration 6 replaced
membership's primary key with a unique expression index, and nothing in the suite asserted it:
the check reads tables and columns and does not read indexes, and no test named
`membership_span`. The phase 2 sign-off proved three refusals and the control by hand from
outside the repository and recorded that a key changed under time pressure was examined once
and never again.
Was:
> | `schema-columns` | every CI run | Every table in a migrated store has the columns and
> storage types `SCHEMA.md` declares for it, in that order, and every table in the store is
> one the file describes |
>
> and the obligations table carried `The membership uniqueness asserted by a permanent test`
> as due at 3.1 and open
Now:
> the row adds that where the file declares a uniqueness the columns cannot carry, the built
> index is asserted to have the form the file states and the duplicates it forbids are refused
> against a migrated store, with the domain it must still admit accepted; and the obligation
> reads `3.1, discharged`
Why: a row that says less than its check does is the same defect as one that says more, read
from the other end, and the next session writing against the row would take the index
assertion for something nobody had built.

### 2026-09-09 - BUILD_PLAN.md - the weighted-call stop is discharged
Authorised by: The night's cost is counted in weighted calls against the stated daily allowance
Was:
> | **The weighted-call stop asserted where `nightly-cost` reaches it** | 2.7 sign-off | 3.1 |
> 3.1 constructs a night at the allowance, which is what makes the stop observable rather
> than read |
Now:
> the same row reading `3.1, discharged`, with the producer cell adding that all three clauses
> of the note now sit in the carrier and that deleting the stop turns `nightly-cost` red
Why: the obligation is discharged ahead of the checkpoint that owes it, which is legitimate
and which the reconciliation permits: a discharged row is exempt from the rule that an open
row's due point must not have landed, because work committed early is the case that exemption
exists for.

### 2026-09-09 - CLAUDE.md, BUILD_PLAN.md - a deferral names what produces the evidence
Corrects: a deferral could name a point that produces nothing it waits on, and nothing said
otherwise. Found by the phase 2 sign-off, which swept the holes table, the contradictions
table, the carried obligations and every deferral in `ARCHITECTURE.html` and `DECISIONS.md`:
12 deferrals, 9 of the 11 open ones naming a point that does not produce the evidence, and 4
naming a point that produces none at all. The truncation rule at 2.3 was the same defect
already caught once, naming phase 5 when the evidence arrived on the first live night.
Was:
> the carried obligations table carried three columns, `Obligation`, `Created at` and
> `Due at`, with unnamed rows and no statement of what produces anything. Its opening line
> read "Recorded when created, not remembered. An obligation names a due point this document
> has." Nine rows named a point that does not produce their evidence: the source lists at
> 6.0, the bulk fundamentals probe at 6.1 and the news window at 5.5 all had their evidence
> in hand already; the refetch atomicity and the fixture expectation sweep at 3.1 waited on
> no evidence at all; the posting hour stood at 3.7, which is the phase 3 report; the six
> reason thresholds were settled at 5.0 and revisited from 5.6's data, and the reason records
> sat in phase 7, neither of which accumulates anything. `CLAUDE.md` stated no rule about
> deferrals and carried no check over the table
Now:
> `CLAUDE.md` carries the rule under Conventions and the citation convention beside the
> decision one, and the roster carries `obligation-reconciles`. Every row is named, and the
> table carries a fourth column stating what produces the evidence. Each row is one of two
> forms, read from its cells rather than from what it calls itself: a checkpoint that
> produces the evidence and cites the obligation back, or the literal `operating` with a
> numeric trigger opening the producer cell, the surface named after the words "read on the",
> and the checkpoint that builds that surface. A row carrying a checkpoint due point and a
> trigger with a surface is neither form and fails. Four rows were added, being the two the
> phase 2 sign-off created and could not write, the citation residue and phase 3's own
> expectations; one row was added for the research lane boundary, which was a deferral in
> `DECISIONS.md` with no row anywhere
Why: a deferral to a phase is a guess about when evidence appears and it fails in both
directions, early where the evidence is already in hand and never where the named point is a
report or a page rather than the thing that measures. The marker is what makes the reverse
direction assertable: without one, a checkpoint announcing an obligation cannot be told from
prose that happens to use the same words.

### 2026-09-09 - BUILD_PLAN.md - the absolute-path obligation is marked discharged
Corrects: the carried obligations table gave `Absolute path matching anywhere in a value, not
position zero` a due point of 1.3 with no discharge marker, while 1.3's own text in the same
document says it was discharged there and every other discharged row carries the word. Found
by the phase 2 sign-off, which recorded it rather than repairing it because a sign-off does
not edit a spec.
Was:
> | Absolute path matching anywhere in a value, not position zero | 0.7 review | 1.3 |
Now:
> the row reads `1.3, discharged` and states that 1.3 is where exception text first reaches
> `run_log.detail`, which is what produces the evidence
Why: a record inconsistency rather than an undone obligation, and the only reading available
to a later session was that an obligation had been forgotten.

### 2026-09-09 - tools/nightly, BUILD_PLAN.md - the fixture argument becomes a setting
Authorised by: A feed is unavailable when it does not answer, and wrong when it answers with something else
Was:
> `tools/nightly` took a fixture folder as its first argument, defaulting to
> `fixtures/membership-2026-09-05`, with a note saying the argument was there because the live
> feeds were not built yet and would become optional when they landed; and the obligations table
> carried the posting hour as due at 2.6
Now:
> the script passes its arguments through and takes none of its own, with the source read from
> `EquityBrief:Providers:Source` and a fixture folder from `EquityBrief:Providers:Fixture`; and the
> posting hour is carried to 3.7 with both bounds recorded
Why: a scheduled night's source should not be a property of a shell script. The flags remain for a
run by hand and giving both is refused. The posting hour needs observations across several
evenings, which a running installation accumulates and a checkpoint cannot, and nothing waits on
it because a night run before the close is already refused by the fetch.


### 2026-09-09 - BUILD_PLAN.md - one day of news is wider than one request
Corrects: nothing. An obligation recorded when it was created rather than remembered.
Was:
> the obligations table carried nothing about the news window
Now:
> | One day of news exceeds one request at the provider's limit, so the pulse count needs a window
> or a page | 2.5 | 5.5 |
Why: the first dated request with no ticker came back holding exactly the provider's limit of
1,000 articles, so a single day of market-wide news does not fit in one request. What that means
for the pulse count cannot be settled until 5.5 counts articles per name, and an obligation filed
now is the difference between a later session finding it and a later session inheriting it.


### 2026-09-09 - ARCHITECTURE.html, SCHEMA.md, CLAUDE.md, BUILD_PLAN.md - what a live index taught the two rules
Corrects: two rules written against a five-name fixture and refuted by the first night over five
hundred, found by running rather than by reading.
Was:
> SCHEMA declared `membership.joined` as "date" with a primary key of `index_code`, `ticker`,
> `joined`; and section 18 carried the row "A feed answers with fewer names than the index holds",
> which refused a payload carrying nothing for any current member
Now:
> `joined` is "date, null when the provider carries none", unique on the three columns with the
> unknown folded to a value by an expression index rather than a primary key; and the row is
> "A feed answers with none of the index in it", which refuses only a payload carrying nothing for
> every member and stores one short of some for the rest
Why: the provider carries 822 spans for this index and 145 have no start date, two of them current
members, so a non-null column drops two real names or writes a date nobody has. And two of 503
current members are absent from an ordinary day's bulk file, so refusing on any absence refuses
every night. Both figures are measurements from the live payloads rather than estimates, and both
rules were written when the only evidence available was a fixture of five names.


### 2026-09-09 - ARCHITECTURE.html, CLAUDE.md, BUILD_PLAN.md - the two rows for an answer that arrives
Authorised by: A feed is unavailable when it does not answer, and wrong when it answers with something else
Was:
> section 18's unavailable row read "keeps last night's bars, marks every name stale, still
> serves the app" against "a banner giving the data date, and tonight's list absent rather than
> wrong", with no row anywhere for a payload that arrives and is wrong; and `nightly-run`'s
> roster row claimed only that the night runs the steps in order and that a failure names the
> step and exits non-zero
Now:
> the unavailable row names the run log and the banner as the two surfaces it promises and says
> what unavailable covers; two rows follow it for a payload carrying another session and a
> payload short of a current member; and the roster row claims the deadline and both refusals
Why: the definition settled at 2.0 splits a feed that did not answer from a feed that answered
with something else, and section 18 carried a row for the first and nothing for the second. The
unavailable row is read per surface because its banner is a phase 5 surface and its run log is
not, which is the shape the gap row took at 1.5. The roster row is widened because a check whose
declared reach grows past its description is a property nobody wrote down.


### 2026-09-09 - ARCHITECTURE.html, BUILD_PLAN.md - the timeout and the night's deadline
Authorised by: A feed is tried three times with a doubling backoff, and the night has a deadline it cannot move
Was:
> section 17 had no row for either bound, so the only statement of the retry policy was the
> decision, and 2.2's text named the policy without naming the row that would carry it
Now:
> section 17 carries **Per-request timeout and the night's deadline** with the three attempts,
> the doubling wait and both bounds, and 2.2 names that row so the claim's due point derives from
> the plan rather than being written into the harness
Why: the decision settles the figures and the limits table is where a figure the harness asserts
lives. Written only in `DECISIONS.md` the numbers would have had no instrument: the row is what
`nightly-run` now reaches, and the test reads the row against the code's own policy so the two
cannot drift.


### 2026-09-09 - BUILD_PLAN.md - the posting hour is bounded rather than measured
Corrects: an obligation written at 2.0 as though one fetch could answer it.
Was:
> | The provider's posting hour for the day's bulk file, measured from live fetches | 2.0 | 2.1 |
Now:
> | The provider's posting hour for the day's bulk file, measured from live fetches | 2.0 | 2.1 bounded at 06:13 UTC the following day; the hour itself carried to 2.6 |
Why: a single fetch gives an upper bound and not an hour. Measuring when the file first appears
means asking repeatedly across an evening, which is a live night's work rather than a
checkpoint's. The bound is recorded in `PROGRESS.md` with the instant it was taken.


### 2026-09-09 - BUILD_PLAN.md - the feed rulings, and what they do to 2.1, 2.2 and 2.3
Authorised by: A feed is unavailable when it does not answer, and wrong when it answers with something else
Was:
> 2.3 read "section 18 given the rows it lacks" without saying how many or which, 2.2 named the
> policy settled at 2.0 without citing it, and 2.1 did not say where the provider's posting hour
> comes from
Now:
> 2.3 names two rows and says why a rejected rate and a missed deadline add none, 2.2 cites the
> retry decision, and 2.1 measures the posting hour from live fetches with the obligation
> recorded in the table at the foot of this document
Why: 2.0 settled what unavailable means, and the definition decides how many rows section 18 is
short of. Written before the ruling, 2.3 could only say "the rows it lacks"; written after it,
the count is a consequence rather than a guess. The two rows that remain are refused in different
components, which is why they are two.


### 2026-09-09 - BUILD_PLAN.md, ARCHITECTURE.html - phase 2 written into the gap
Corrects: contradiction M, and nothing else. The rest is an addition: the remap of the same date
left a gap at phase 2 deliberately and this fills it.
Was:
> section 14's closing note ended "It is scheduled with Task Scheduler after the US close", and
> section 20's phase table ran 0, 1, 3, 4, 5, 6, 7 with no row at 2, and BUILD_PLAN's 3.1 opened
> "Migration creating `indicator`."
Now:
> the section 14 note ends with the schedule as a UTC instant set after the provider posts the
> day's bulk file, naming no scheduler and citing the decision that requires UTC; section 20
> carries a phase 2 row; and 3.1 opens by repairing contradiction K in its own commit
Why: the sentence named a Windows-only mechanism against **Nothing is written against one
operating system** and a local time against **Queued work runs off-peak, and every schedule is
written in UTC**. Two hard rules in one sentence, in the section that specifies the nightly run.
The rest of the change writes the phase the remap left room for, with its holes, its
contradictions and its checkpoints.


### 2026-09-09 - ARCHITECTURE.html - the remap's second sweep
Corrects: two defects in the remap of the same date, found by sweeping every tracked text file
rather than the four the first pass listed by hand.
Was:
> section 20's phase table ended its levels row "all phase-2 rows PASS across four names" and
> its plan row "all phase-3 rows PASS", and section 23's dated rows read "Phase 5 gains an
> obligation to store each listing's plan" and "a phase 6 expectation that the boundary is
> measured against the reference"
Now:
> the two section 20 cells read phase-3 and phase-4, matching the rows they end; the two
> section 23 rows are reverted to the phase numbers their own dates were written under
Why: the first sweep matched a phase word followed by a space, so the two hyphenated cells were
left describing the phases they used to belong to. Section 23 is the dated record of what each
version of this document said, and a record is corrected by a new dated entry rather than by an
edit, which is the rule `PROGRESS.md` was already being held to. The finding and the rule are in
that record's entry of the same date.


### 2026-09-09 - BUILD_PLAN.md, CLAUDE.md, ARCHITECTURE.html - the phase remap
Corrects: nothing that was wrong. This is a reordering rather than a repair, and it is here
because a renumbering deletes a line from three specs and `changelog-reconciles` reads the
history rather than the intent.
Was:
> phases 2 through 6 were levels on the chart, the plan, tonight's list, research on demand and
> the improvement loop, with their checkpoints numbered to match, and the roster's checkpoint
> rows read from 2.1, from 4.1, from 5.1 and from 6.1
Now:
> the same five phases at 3 through 7 with their checkpoints moved with them, the roster rows at
> from 3.1, from 5.1, from 6.1 and from 7.1, and phase 2 left empty for the feed work
Why: no feed reaches the network, so the level work would be built and calibrated against a
fixture no provider produced. The reasoning is in `PROGRESS.md`'s entry of the same date,
including why the feed work is a phase rather than a checkpoint and why renumbering was taken
over an inserted phase 1A.


### 2026-09-08 - CLAUDE.md - four claims in the corrected verify-phase passage
Corrects: the passage rewritten by the entry below, checked once and wrong in four places.
Found by an adversarial review of that same commit before it merged. Green was said to mean
every claim was checked and held, which is false for the 143 of 180 claims that are out of
scope and that green never looks at, and it contradicts the separation rule in this file and
phase 0's own sign-off. The committed expectations were called frozen, where both files in the
one committed fixture declare in their first field that they are derived from the rules and not
frozen from a run, and where this file twice uses frozen as the disqualifying kind. The map and
the PASS-by-fiat were dated from 0.5, where the record shows 0.5 and 0.6 reporting 0 pass, 120
unexamined and exit 1, with the map arriving at 0.7. And a report stamped with its generation
instant on both surfaces was called byte-identical across two runs.
Was:
> It runs the suite, which is what replays the pipeline over the committed fixture and diffs
> every stage's output against the frozen expectations, and it writes what each check did. Then
> it parses `docs/ARCHITECTURE.html`'s tables and gives every claim a verdict by reading that
> result
>
> A phase is not done until that report is green, and green means no claim failed and none is
> unexamined.
>
> From 0.5 to 1.8 the tool ran no check at all
>
> A tree with five failing tests produced a byte-identical green report. What a green report
> says is that every claim was checked and held; what it does not say is anything about a
> running system, which is the separate rule below.
Now:
> the stages that exist rather than the pipeline, expectations derived from the rules rather
> than frozen ones, every claim in scope rather than every claim, the two further conditions
> green now carries, the span split at 0.7 where the map arrived, an identical verdict block
> and the same green exit rather than a byte-identical report, and a green report saying that
> no claim in scope failed and none went unexamined.
Why: a corrected passage that is still wrong is worse than the original, because it has been
checked once and reads as settled. Three of the four are the same conflation the repair beneath
exists to remove, which is the report's scope being read as the whole of what it looked at.

### 2026-09-08 - CLAUDE.md - the layout block did not name the suite result
Corrects: `tools/verify-phase` now writes `artifacts/suite.trx` and `artifacts/suite.log` beside
the two report files, and the block describing that folder named only the report. The block is
the map of the tree, so a file it does not name is a file no document places.
Was:
> /artifacts        gitignored. the phase report, written by verify-phase
Now:
> /artifacts        gitignored. the phase report and the suite result it reads, written by verify-phase
Why: the same commit that put two new files in that folder left the line describing it alone.
Nothing leaks, because the folder is gitignored whole, so this is a map defect rather than a
hazard.


### 2026-09-08 - CLAUDE.md - what tools/verify-phase actually did
Corrects: the tool ran no check. Every verdict came from a map naming the instrument that
reaches a claim, `Verdict.Fail` was assigned nowhere in the report path, and the "fail 0" line
was structural rather than measured. Found at the phase 1 sign-off by deleting the membership
filter from the bar fetcher: five tests failed, and the report printed an identical block and
still called the claim about storing bars for current members PASS. The claim went unchecked
from 0.5, where the harness first read the architecture, through 1.8.
Was:
> **`tools/verify-phase` is what a phase signs off against.** It runs the pipeline over the
> committed fixture, diffs every stage's output against frozen expectations, parses
> `docs/ARCHITECTURE.html`'s tables and asserts each claim against the code, and writes
> `artifacts/phase-report.html` for the operator and `artifacts/phase-report.json` for a build
> session. A phase is not done until that report is green, and green includes that nothing is
> listed as unexamined.
Now:
> the same paragraph rewritten to say that the tool runs the suite and reads its result, with
> the three verdicts of section 19.3 stated against what the run did, plus a second paragraph
> naming the defect and the span it covered.
Why: three of the four things the passage claimed were not true of the tool. It did not run the
pipeline, it did not diff stage output against expectations, and it did not assert claims
against the code; it parsed the tables and printed a stored verdict. The repair makes the first
two true by running the suite, which is what replays the fixture and diffs the stages, and makes
the third true by reading that run's result. The passage is corrected rather than narrowed,
because the description was right about what a sign-off needs and wrong about what the tool did.


### 2026-09-05 - ARCHITECTURE.html - decisions moved out of the document
Authorised by: Facts are declared once and cited by descriptive name

Was: sections 21 and 22 of the architecture held the decisions and the previously-decided list inline, seventy-nine entries in all.

Now: both sections are replaced by a pointer to `DECISIONS.md`, which holds them as the record.

Why: the corpus keeps one place per fact. A decision stated in the architecture and again in a register is two documents holding one fact, and one of them always loses. The architecture cites decisions by name and `decision-resolves` asserts every citation against this record.

### 2026-09-05 - BUILD_PLAN.md - every phase now carries its checkpoints
Corrects: phases 2 to 6 were named with a deliverable and a visible output and no checkpoints, under a convention that deferred all later detail. The convention was defended with an argument true of only a narrow kind of detail, and it cost a planning round and a review round per phase for work that was knowable when the corpus was written. Found when the phase 1 planning round produced four document contradictions, five tripwires and three specification holes, none of which needed phase 0 to have run.

Was:
> **Later phases get checkpoint detail at the previous phase's sign-off, not before.** Writing 4.6 today means writing it against assumptions phases 1 through 3 have not tested. Phases 2 onward are named here with their deliverable and their visible output, and nothing finer.

Now:
> the convention narrowed to defer only exact column names, exact test names, and whether a checkpoint needs splitting; with the checkpoint list, the contradictions each phase resolves and the specification holes each phase settles all written now

Why: a rule that defers what is knowable turns every phase boundary into a discovery exercise the operator sits through twice.

### 2026-09-05 - BUILD_PLAN.md - phase 1 reordered so its first checkpoint renders
Corrects: the checkpoint list put the chart at 1.6, so six of eight checkpoints produced nothing to look at, while the document's own preamble and the decision named Every phase opens with something to look at both say the first checkpoint renders a page. The preamble and the list were in the same document, two lines apart.

Was:
> 1.1 membership loader, 1.2 backfill, 1.3 bar fetcher and gap refusal, 1.4 corporate action checker, 1.5 read surface, 1.6 chart, 1.7 coverage measurement, 1.8 report

Now:
> 1.1 membership loader, 1.2 backfill, 1.3 read surface and chart, 1.4 bar fetcher and nightly script, 1.5 gap refusal, 1.6 corporate action checker, 1.7 coverage measurement, 1.8 report

Why: the chart needs only stored bars, which exist after the backfill, so nothing in the dependency order forced it to sixth. The reorder also splits the checkpoint that carried the fetcher, the nightly script, gap refusal, retention, two check changes, the first fixture and four obligations into one commit.

### 2026-09-06 - CHANGELOG.md - an entry may name a defect instead of a decision
Corrects: the entry format required every entry to name an authorising decision, and a defect correction has none. Found while preparing this pass, six of whose eight entries are defect corrections and would all have had to cite one catch-all decision, after which the field would have stopped meaning anything.
Was:
> Nothing in the corpus is struck through. A spec is edited cleanly and what it said before is written down here, with the decision that authorised the change.

> Authorised by: <the decision name, cited exactly>
Now:
> Nothing in the corpus is struck through. A spec is edited cleanly and what it said before is written down here, with the decision that authorised the change or the defect it repairs.

> Authorised by: <a decision name, cited exactly>   OR
> Corrects: <the defect, in one line, and how it was found>

with the paragraph beneath the format block saying an entry names one or the other and never neither.
Why: a field every entry fills with the same placeholder is a field nobody reads. Separating the two kinds keeps a decision citation meaning that a decision was made, which is what `decision-resolves` and `no-superseded-citation` are built to check.

### 2026-09-06 - BUILD_PLAN.md - checkpoint 0.0, the repository, inserted before 0.1
Corrects: the plan opened at 0.1 and no checkpoint owned creating the git repository. Found by the first corpus check, which looked for `.git` because `changelog-reconciles` reads the history and found no repository at all.
Was:
> the phase 0 checkpoint list began at 0.1, The solution.
Now:
> 0.0, The repository, precedes it, with the corpus committed unedited as the first commit.
Why: three rules the corpus already states depend on a repository existing, and a dependency nothing owns is one every session assumes somebody else did.

### 2026-09-06 - CLAUDE.md - the layout block names every file that is not gitignored
Corrects: `ci.yml` sat at the repository root, where Actions never reads it, so the matrix job and the Linux case-sensitivity job never ran and done condition 5, `two-platform` and the instrument the Checks section claims for `path-casing` were all unsatisfied with nothing reporting it. The layout block named neither that file nor `CLAUDE.md`, `EquityBrief.sln`, `.gitignore` or `/artifacts`, so four delivered files and the harness output folder had no stated home. Found by listing the tree against the block.
Was:
> /fixtures         one folder per fixture name and date: the committed inputs, and
>                   expectations/ holding what the rules in ARCHITECTURE produce over them
> /prompts          gitignored. spent build prompts, kept locally
> /data             gitignored. the store lives here
> .gitattributes    line endings, normalised to LF in the repository
Now: the same lines with `/artifacts` above `/prompts`, and `CLAUDE.md`, `EquityBrief.sln`, `.github/workflows/ci.yml` and `.gitignore` named beneath `/data`. `ci.yml` moved from the root to `.github/workflows/ci.yml` in the same commit.
Why: a workflow at the wrong path is the failure this corpus argues against by name, a green run that ran nothing. The block is the map of the tree, so a file it does not name is a file no document places.

### 2026-09-06 - CLAUDE.md - banned-prose added to the Checks roster
Corrects: the Prose convention said a grep enforces the banned word, and the Checks roster, which states that it lists every check that runs, contained no such row. The corpus promised an instrument it did not have. Found by reading the roster against the conventions.
Was:
> the roster ran from `stated-counts` straight to `coverage-reported`, with no row for the banned string and none for the em dash rule the same convention states.
Now:
> a `banned-prose` row between them, every CI run, asserting that no file in the corpus or the shipped source carries the banned string or any form of it and no file carries an em dash, with the sentence in the Prose convention that names the string as the single exemption.
Why: `coverage-reported` reconciles the roster against the implemented checks, so a rule enforced by a grep that the roster does not list is a check nothing would notice had stopped running.

### 2026-09-06 - CLAUDE.md - the banned string's exemption named where the rule is stated
Corrects: the exemption existed only in the fact that the sentence naming the string had to be skipped, so the first implementation of the grep would have had to invent it, and the corpus would have carried a rule its own text violates. Found by running the grep by hand: it returned one line, the line that states the rule.
Was:
> One word is banned outright across the corpus and in chat, and a grep enforces it: the operator does not want it, and a claim of candour is exactly the kind of virtue-assertion this rule already rejects. The banned string is <elided> and every form of it.
Now:
> One word is banned outright across the corpus and in chat, and a grep enforces it, exempting only this sentence, which has to contain the string in order to name it: the banned string is <elided> and every form of it. The operator does not want it, and a claim of candour is exactly the kind of virtue-assertion this rule already rejects.
Why: the clause and the naming now sit in one sentence, so the exemption a checker matches on is the sentence that actually carries the string. The prior text is quoted with the string elided, because `banned-prose` allows exactly one occurrence in the corpus and it is the exempt line.

### 2026-09-06 - CLAUDE.md - the architecture's version removed from "Where the build is right now"
Corrects: the sentence held two facts that live elsewhere. The version lives in `ARCHITECTURE.html`, and whether a build session has read it goes stale the moment 0.1 starts, with nothing owning its removal. This is the objection the paragraph immediately below it makes about checkpoint numbers, applied to the sentence above it. Found by reading the section against its own next paragraph.
Was:
> Nothing is built. `docs/ARCHITECTURE.html` is at v0.3 and has never been read by a build session.
Now:
> Nothing is built. What the build has reached is recorded below rather than stated here.
Why: one place per fact. The section now points at the record instead of restating it, which is what the rest of the section already does.

### 2026-09-06 - CLAUDE.md - the target framework stated, and two more files given a home
Corrects: no document named the target framework. The workflow's `dotnet-version: '10.0.x'` was the only statement of it anywhere, so the two development machines could build against an SDK CI never installs and nothing would report the difference. Found by grepping the corpus for a framework, a `global.json` or a warnings setting and getting one hit, in the workflow.
Was:
> the Commands section ran from its table straight to the paragraph beginning "The Shell column is there because", and the layout block named neither `global.json` nor `src/Directory.Build.props`, neither of which existed.
Now:
> a paragraph beneath the table stating that the target framework is net10.0, that `global.json` holds the SDK to the 10.0.3xx feature band and rolls forward to the latest installed, and that `src/Directory.Build.props` carries the framework, nullable reference types and warnings as errors for all six projects. Both files are named in the layout block.
Why: 0.1's done condition requires a clean build under warnings as errors, and a condition about a property needs somewhere the property is set.

### 2026-09-06 - BUILD_PLAN.md - 0.1's done condition names the framework and the props file
Corrects: the condition said "building clean under warnings-as-errors" without saying where that property lives, so six project files each setting it would have satisfied it, and the seventh project added later would not have been caught. Found while writing `src/Directory.Build.props`, which had no done condition to serve.
Was:
> Six projects as `CLAUDE.md` lays out, building clean under warnings-as-errors, with `EquityBrief.Api` carrying no reference to `EquityBrief.Worker`.
> **Done when** `dotnet build` is clean and `api-isolation` passes reading the compiled dependency file.
Now:
> Six projects as `CLAUDE.md` lays out, all targeting `net10.0` and taking warnings as errors from `src/Directory.Build.props`, with `EquityBrief.Api` carrying no reference to `EquityBrief.Worker`.
> **Done when** `dotnet build` is clean with nothing suppressed, no project file states a target framework or a warning setting of its own so both come from `src/Directory.Build.props`, and `api-isolation` passes reading the compiled dependency file.
Why: a done condition written against a property with no stated home is satisfied by any arrangement that happens to build, including the one that drifts.

### 2026-09-06 - CLAUDE.md - fixtures/README.md placed outside the eight
Corrects: the Document lifecycle caps the corpus at eight and `stated-counts` asserts the table, while `fixtures/README.md` sat in the tree as a ninth markdown file with nothing saying which side of the cap it was on. Found by counting markdown files in the tree against the lifecycle table.
Was:
> the section ended at the paragraph placing a screens document outside the eight.
Now:
> a second paragraph places `fixtures/README.md` outside them too, on the ground that it describes a folder's shape and carries no rule and no decision.
Why: the cap only works if every document in the tree is either inside it or excluded by name. One unplaced file makes the count arguable, and an arguable count is one `stated-counts` cannot assert.

### 2026-09-06 - CLAUDE.md - the layout block names fixtures/README.md
Corrects: the block described `/fixtures` as one folder per fixture name and date, which `README.md` is not, so the one delivered file inside it had no home in the map even after the Document lifecycle placed it outside the eight. Found by the tree-against-block sweep run for this pass, which returned exactly one unnamed file.
Was:
> /fixtures         one folder per fixture name and date: the committed inputs, and
Now:
> /fixtures         README.md      the folder's shape, not a corpus document
>                   one folder per fixture name and date: the committed inputs, and
Why: the sweep is only worth running if it can return zero, and a known exception carried in a reviewer's head is the thing that stops a sweep being an instrument.

### 2026-09-06 - BUILD_PLAN.md - the citation placeholder recorded as a carried obligation
Corrects: the carried obligations table writes the citation form out in full, so the parenthesised placeholder inside it will be read as a citation by `decision-resolves` and fail on a name that does not exist. Found by running that check by hand over the corpus, which returned 13 real citations and this one placeholder.
Was:
> the table opened with the volume shelf threshold row.
Now:
> a row above it, created at 0.0 and due at 0.4, holding that either the checker exempts the table by name or the row is reworded.
Why: the check is written at 0.4 and would fail on its first run against text nobody would think to look at. Recorded when created rather than remembered, which is what the table is for.

### 2026-09-06 - CLAUDE.md - the merge condition and done condition 2 name when they start
Corrects: both are written against a CI that exists, while `tools/ci.*` is built at 0.4. As stated, no checkpoint from 0.0 to 0.3 could merge or be declared done, so the corpus forbade the four checkpoints that build its own verification machinery. Found by pushing the workflow for the first time: all three jobs ran and all three failed on the missing script, which is the correct first result and which the Merge section had no way to say.
Was:
> **CI green before merge. That is the only condition.**

> 2. `tools/ci.*` is green, with the test count recorded in PROGRESS.
Now: the same two passages, each followed by a statement of when it begins. The merge condition binds from 0.4, before which a workflow failing on a missing script is not a block and after which a red run blocks with no override. Done condition 2 is met before 0.4 by a verification run by hand, with PROGRESS recording the figures and stating that nothing guards them yet.
Why: the Checks roster already solves this with its Runs column, where every check names the checkpoint it starts at. A rule with no stated start is either broken on day one or quietly ignored, and quietly ignored is the worse of the two.

### 2026-09-06 - CLAUDE.md - done condition 5 names when it starts
Corrects: the same defect repaired at 0.0 in the Merge section and in done condition 2, in the one place the repair missed. The suite passing on both runners is asserted by the matrix, the matrix calls `tools/ci.*`, and those arrive at 0.4, so no checkpoint from 0.1 to 0.3 could satisfy it. Found by building 0.1 and reaching the condition with nothing able to meet it.
Was:
> 5. The suite passes on both runners.
Now:
> 5. The suite passes on both runners. Until 0.4 makes the matrix able to run, the suite is run on the machine at hand, PROGRESS names which platform that was, and the other runner is carried to 0.4.
Why: the 0.0 repair named the two passages a corpus read would find and missed the one only a build would reach. Three statements of one rule is why it was missed, and the three now agree.

### 2026-09-06 - CLAUDE.md - the solution file is EquityBrief.slnx
Authorised by: The solution file takes the SDK's current format
Was:
> EquityBrief.sln   the six projects, at the root

> | Build | `dotnet build EquityBrief.sln` | same | either |
Now:
> EquityBrief.slnx  the six projects, at the root

> | Build | `dotnet build EquityBrief.slnx` | same | either |
Why: 0.1 created the solution with an explicit flag to get the older format, on the reasoning that the corpus named it and a spec should not bend to a tool default. The operator ruled the other way: the default is the format to carry, and the corpus is what moves. `EquityBrief.Tests` finds the checkout by looking for the file, so the code moved with it.

### 2026-09-07 - BUILD_PLAN.md - the harness obligation covers phase 0, not 0.1
Corrects: the row named 0.1's three checks by name, so every checkpoint that adds a check has to remember to edit it, and the first one that forgets leaves the row understating what the phase report owes. Found at 0.3, the second checkpoint in a row that added checks the row did not mention.
Was:
> | 0.1's checks unseen by the harness | 0.1 | 0.7 | `api-isolation`, `build-properties-central` and `pinned-constants` are asserted by the suite and by nothing the phase report reads. 0.1 produces no pipeline output, so it has no fixture expectations to carry, only checks the report must enumerate |
Now:
> | Phase 0's checks unseen by the harness | 0.1 | 0.7 | every check the suite carries is asserted by nothing the phase report reads. Phase 0 produces no pipeline output, so it has no fixture expectations to carry, only checks the report must enumerate by name |
Why: an obligation that has to be re-edited to stay true is one that goes stale quietly. Naming the property rather than today's instances of it makes the row correct for every checkpoint in the phase.

### 2026-09-07 - BUILD_PLAN.md - two carried obligations discharged at 0.4
Corrects: nothing. Both rows came due at 0.4 and were met, and a table of carried obligations that keeps rows after they are discharged stops being a list of what is owed.
Was:
> | The suite unrun on macOS | 0.1 | 0.4 | 0.1 was verified on Windows only, because the matrix cannot run until `tools/ci.*` exists. No macOS runner has executed the suite |

> | `tools/migrate.ps1` written without its proofs | 0.2 | 0.4 | 0.2 needed the wrapper to run migrate on Windows at all, so it exists. 0.4's done condition owes the proofs: that a wrapper returns both the script's output and its exit code, shown by a deliberately failing probe, and that a machine with no bash exits with a named message rather than zero |
Now: both rows removed. The measurements that discharged them are in the 0.4 entry and its addendum in `PROGRESS.md`, which is where a dated figure belongs.
Why: the table answers what is still owed. A discharged row makes it answer something else, and the record of the discharge belongs in the record rather than in the spec.

### 2026-09-07 - ARCHITECTURE.html - the phase report is named once
Corrects: the component catalogue had the verification harness write `verify.html` and `verify.json`, while `CLAUDE.md` and `BUILD_PLAN.md` both say `artifacts/phase-report.html` and `artifacts/phase-report.json`. One artefact, two names, and `architecture-conformance` reads the catalogue. Found at 0.1 by reading the catalogue against the two files that name the same output, and carried to 0.5, where the harness that writes it was built.
Was:
> <td>verify.html, verify.json</td>

> <td><code>verify.html</code> with every row UNEXAMINED</td>
Now:
> <td>artifacts/phase-report.html, artifacts/phase-report.json</td>

> <td><code>artifacts/phase-report.html</code> with every row UNEXAMINED</td>
Why: two documents said one thing and one said another, and the two are the ones a build session reads first. The harness now writes the name they use.

### 2026-09-07 - CLAUDE.md - architecture-conformance covers the nightly run's list
Corrects: the row said "every claim a table makes", and the component catalogue names sections 7, 14, 15, 16 and 18 as the claim scope. Section 14 carries an ordered list rather than a table, so a check written to the row's words would have taken no claims from the nightly run and said nothing about having skipped it. Found at 0.5 by counting the document's tables against the sections the catalogue names, and carried to 0.7.
Was:
> | `architecture-conformance` | every CI run | Every claim a table in ARCHITECTURE.html makes has a verdict: pass, fail, out of scope for this phase, or unexamined, and every table in the document is placed so none can go unread |
Now:
> | `architecture-conformance` | every CI run | Every claim ARCHITECTURE.html makes, in a table or in the nightly run's ordered list, has a verdict: pass, fail, out of scope for this phase, or unexamined; every table in the document is placed so none can go unread; and a claim that passes names the check that reached it |
Why: the instrument was widened to cover what the architecture said it covered, rather than the architecture narrowed to what the instrument happened to read. The clause about naming the check is added because the harness now has claims that pass, and a pass by fiat is the thing the phase report exists to refuse.

### 2026-09-07 - CLAUDE.md - a checkpoint row names a phase the plan has
Corrects: `coverage-reported` was written to require that a checkpoint row name a checkpoint `BUILD_PLAN.md` has, and the plan states that later phases get checkpoint detail at the previous phase's sign-off. Four of the five checkpoint rows name 2.1, 4.1, 5.1 and 6.1, none of which the plan can carry yet, so the rule forbade the roster from naming anything past the phase in hand. Found at 0.7 while implementing the check.
Was:
> and a checkpoint row has to name one `BUILD_PLAN.md` has and `PROGRESS.md` does not yet record.
Now:
> and a checkpoint row has to name a checkpoint `PROGRESS.md` does not yet record, in a phase `BUILD_PLAN.md` has. The phase rather than the checkpoint, because later phases get checkpoint detail at the previous phase's sign-off, so requiring the checkpoint by name would forbid the roster from naming anything past the phase in hand.
Why: the two rules were written against each other and one of them had to give. The one that gave is the one that could not be satisfied without abandoning the plan's own policy on when checkpoints are written.

### 2026-09-07 - BUILD_PLAN.md - the section 14 obligation discharged at 0.7
Corrects: nothing. The row came due at 0.7 and was met by widening the harness rather than the document: section 14's ordered list is now read as nine claims.
Was:
> | Section 14 is named as a claim source and carries no table | 0.5 | 0.7 | the component catalogue says the harness reports a verdict for every claim in sections 7, 14, 15, 16 and 18. Section 14 contains no table, so a harness that reads tables can take no claims from it. Either 14 gains a table or the stated scope drops it, and the report cannot be trusted to cover the nightly run until one of those happens |
Now: the row is removed. The measurement that discharged it is in the phase 0 entry in `PROGRESS.md`.
Why: the table answers what is still owed, and a discharged row makes it answer something else.

### 2026-09-07 - BUILD_PLAN.md - the harness coverage obligation discharged at 0.7
Corrects: nothing. The row came due at 0.7 and was met. The phase report now carries a coverage record naming every check the roster says runs and what carries it, so a roster row with nothing behind it is visible on the page a person reads rather than only inside a run that passed.
Was:
> | Phase 0's checks unseen by the harness | 0.1 | 0.7 | every check the suite carries is asserted by nothing the phase report reads. Phase 0 produces no pipeline output, so it has no fixture expectations to carry, only checks the report must enumerate by name |
Now: the row is removed. The figures are in the phase 0 entry in `PROGRESS.md`.
Why: the table answers what is still owed, and a discharged row makes it answer something else.

### 2026-09-07 - BUILD_PLAN.md - two more obligations discharged at 0.7
Corrects: nothing. Both are met. The citation placeholder, due at 0.4 and discharged late here, is exempted by `decision-resolves` on its exact text rather than by loosening the pattern, which would have exempted real mistakes too. The architecture now cites six of its rules by decision name, so `decision-resolves` has the design document to assert and not only `CLAUDE.md`.
Was:
> | The citation placeholder in this table is not a citation | 0.0 | 0.4 | ... Either the checker exempts this table by name or the row is reworded |

> | Architecture cites its decisions by name | 0.5 | 0.7 | ... A pass over the architecture adding a citation at each rule that rests on a decision is owed before the phase 0 report claims that check runs |
Now: both rows removed, and the carried obligations table holds only the five the architecture and phase 1 created.
Why: the second was the one gating the phase 0 report's claim that `decision-resolves` runs, and it is the reason the pass happened at 0.7 rather than being carried further. The first was due at 0.4 and was not discharged then, which the phase 0 entry records rather than passes over.

### 2026-09-07 - ARCHITECTURE.html - the verification harness reads no live store
Corrects: the read and write matrix gave the verification harness R against all eleven stores, and `CLAUDE.md` says nothing in the harness reaches `data/`. Two documents in the corpus disagreed and the harness reported PASS on one side of the disagreement. The component catalogue's own row carried the same words. Found by the phase 0 review, finding 3, as the third of three findings bearing on sign-off.
Was:
> `  <tr><td>Verification harness</td><td><span class="r">R</span></td>` and the same cell repeated for all eleven stores

> `<td>this document, the code, the fixture, every store</td>` in the component catalogue's Verification harness row

> The run log is a store rather than a component and so has no row of its own.
Now:
> the matrix row carries eleven blank cells, the catalogue row reads `this document, the code, and the fixture, including the fixture's own store; never a store under the data root`, and the Key paragraph gains: The verification harness reads none of the eleven stores: it reads this document, the code and the fixture, including the fixture's own store, and never a store under the data root. The distinction is written here because it is the one a later reader would otherwise widen back, and a check that reads the live store is a check whose result depends on last night.
Why: the harness reads the fixture's store and the schema declarations and never the live store, so the matrix row was the wrong one of the two. A blank cell in that matrix is a claim as much as a filled one, which makes eleven blanks the correct statement rather than a deletion. The sentence in the Key is there because the row itself cannot carry a reason, and a bare set of blanks invites the next reader to fill them in again.

### 2026-09-07 - ARCHITECTURE.html - the stated claim scope names section 17
Corrects: the component catalogue gave the verification harness a claim scope of sections 7, 14, 15, 16 and 18, while section 17's own note says each of its rows is a claim about the code and that the harness parses the table. The two passages disagreed, and the harness followed the shorter list: section 17 was placed as asserted by `pinned-constants`, which reads `CLAUDE.md`, `BUILD_PLAN.md`, `global.json`, `src/Directory.Build.props` and the workflow and never this document, so twenty-nine claims were removed from the count by a placement naming an instrument that could not reach them. Found by the phase 0 review, finding 2.
Was:
> PASS, FAIL or UNEXAMINED for every claim in sections 7, 14, 15, 16 and 18
Now:
> PASS, FAIL or UNEXAMINED for every claim in sections 7, 14, 15, 16, 17 and 18
Why: the scope was widened to what the document already said of itself in two places rather than section 17's note narrowed to the catalogue's list, which is the same repair 0.5 made for section 14. Section 17 is now a claim source and its twenty-nine rows are out of scope, each naming the checkpoint or phase that ends it, instead of being counted as covered.

### 2026-09-07 - CLAUDE.md - two roster rows widened to what their checks now do
Corrects: nothing in the rows was wrong; both had become narrower than the check behind them, which is the direction that reads as coverage nobody has. `architecture-conformance` now reconciles every placement and every pass against what the check it names declares it reaches, and renders the reaching check on both surfaces the report writes. `banned-prose` now reads every text file git tracks rather than the eight documents, the source and project files and the scripts in `tools`. Found by the phase 0 review, findings 1, 2, 3 and 12, and repaired in the same pass.
Was:
> | `architecture-conformance` | every CI run | Every claim ARCHITECTURE.html makes, in a table or in the nightly run's ordered list, has a verdict: pass, fail, out of scope for this phase, or unexamined; every table in the document is placed so none can go unread; and a claim that passes names the check that reached it |

> | `banned-prose` | every CI run | No file in the corpus or the shipped source contains the banned string or any form of it, and no file contains an em dash. The line in CLAUDE.md's Prose convention that names the string is the single exemption, matched on the sentence that states the rule |
Now:
> | `architecture-conformance` | every CI run | Every claim ARCHITECTURE.html makes, in a table or in the nightly run's ordered list, has a verdict: pass, fail, out of scope for this phase, or unexamined; every table in the document is placed so none can go unread; a claim that passes names the check that reached it, on both surfaces the report writes; and every placement and every pass is reconciled against what the check it names declares it reaches, in both directions |

> | `banned-prose` | every CI run | No text file the repository tracks contains the banned string or any form of it, and none contains an em dash. The line in CLAUDE.md's Prose convention that names the string is the single exemption, matched on the sentence that states the rule |
Why: the roster is where a reader learns what a check asserts, and the phase report enumerates checks by name against it. A row that says less than the check does is the same defect as one that says more, read from the other end: the next session writing against the row would take the reconciliation for something nobody had built. The Checks section gains a paragraph stating the declaration rule, because the rule is what makes a placement naming an instrument refutable rather than decorative.

### 2026-09-07 - BUILD_PLAN.md - phase 0's checkpoint detail replaced by a summary
Corrects: the prior text was removed with no entry recording it. The commit that removed it wrote the two entries above, covering the convention change and the phase 1 reorder, and neither names the eight checkpoint blocks that went with them. Found by the two tests it turned red, which were asserting over strings that lived only in the deleted text.

Was:
> eight blocks, `### 0.0 The repository` through `### 0.7 Phase 0 report`, each with its deliverable and its own done condition, including "Six projects as `CLAUDE.md` lays out, all targeting `net10.0`" at 0.1

Now:
> **Signed off.** Eight checkpoints, 0.0 to 0.7. The verification machinery exists and reports before there is anything to verify, which is why its first report was entirely unexamined and out of scope.

Why: replacing a signed-off phase's checkpoint detail with a summary is defensible on its own, because the detail is history and `PROGRESS.md` is where history lives. Recording no prior text for it is not. Two checks were reading that text as their population, `pinned-constants` for the second `net10.0` mention and `stated-counts` for the string `Six projects`, and both lost their scope silently until the suite ran.

### 2026-09-07 - RUNBOOK.md - the SDK to install named in Moving the installation
Corrects: the list told an operator to clone the repository and then run `tools/migrate`, with nothing between them naming what has to be installed first. A machine with no SDK in the pinned band fails at restore with a message that names neither the band nor `global.json`.

Was:
> 1. Clone the repository on the new machine.
> 2. Copy `data/equitybrief.db` into the configured data root.

Now:
> 1. Clone the repository on the new machine.
> 2. Install a .NET SDK in the `10.0.3xx` band, which is what `global.json` pins and what the six projects need to build against `net10.0`. Without one the run fails at step 5 with a restore error that names neither.
> 3. Copy `data/equitybrief.db` into the configured data root.

Why: moving the installation is the one procedure a person follows on a machine where nothing works yet, so the step that has to come first belongs in it. The remaining steps are renumbered.

### 2026-09-08 - ARCHITECTURE.html - what each lane writes, set out beside the figure
Corrects: the key explained where the line falls and never listed what each lane writes, so a reader asking what the two sides do had to read the figure's box text. Found while planning phase 1, reading 12.2 for what the local lane owns.

Was:
> the key's four paragraphs, ending "The picture is the same either way; only the boundary moves.", and then section 13

Now:
> the same four paragraphs, then a subsection "What each lane actually writes" carrying a seven-row table of section, lane, what goes in and why it sits there, then the paragraph on what each lane holds in memory, then section 13

Why: nothing is removed. The figure already names these inside its boxes, which is a picture's worth of information a reader cannot cite, search or check a claim against. The table is placed in the harness as a table that makes no claims yet, owed at 5.0, which is where `BUILD_PLAN.md` settles the section-to-lane assignment.

### 2026-09-08 - ARCHITECTURE.html, CLAUDE.md, RUNBOOK.md - the plan no longer predates the price
Corrects: the clause was repaired once for provenance and left implying the plan predated the price. Section 1 said the system answers which stocks have reached a price where the plan already says something. That was corrected at 1.1's precursor to add that the plan is recomputed nightly rather than authored in advance, and the correction added a clause in front of which the original phrase was left standing, so the sentence still says a plan existed before the price arrived. It did not: the levels and the ladder are both computed tonight from data that includes tonight's bar. The operator read the sentence twice and read it as wrong both times. Swept for after the three known occurrences were repaired, over the whole tracked tree on "the plan says", "the plan already says", "where the plan" and four adjacent shapes; the sweep found the same three and nothing else.

Was:
> ARCHITECTURE.html section 1: "which stocks have reached a price where the plan already says something" and "Four of the six conditions detect arrival at a price the plan has an instruction for, one detects a break on unusual volume, and one is a calendar fact. That plan is not authored in advance and is not carried over from a previous night: it is recomputed from the chart every evening"
> ARCHITECTURE.html 15.7: "which names reached a price where the plan says something, and how busy the evening was"
> CLAUDE.md: "decides which names reached a price where the plan says something"
> RUNBOOK.md: "The list says a name reached a price where the plan says something, and the plan is arithmetic over levels the chart has visited"

Now:
> ARCHITECTURE.html section 1: "which stocks are sitting at a price their own chart has made significant" and "Four of the six conditions detect arrival at a price the evening's arithmetic has an instruction for, one detects a break on unusual volume, and one is a calendar fact. That arithmetic is not authored in advance and is not carried over from a previous night: the levels and the plan are recomputed from the chart every evening"
> ARCHITECTURE.html 15.7: "which names are sitting at a price their own chart has made significant, and how busy the evening was"
> CLAUDE.md: "decides which names are sitting at a price their own chart has made significant"
> RUNBOOK.md: "The list says a name is sitting at a price its own chart has made significant, and the levels and the plan are both arithmetic recomputed from that chart the same evening"

Why: three documents said the same wrong thing, and repairing one would have left two saying it, which is the drift a single place per fact exists to prevent. RUNBOOK's sentence corrected itself in its next clause, which is a better argument for rewriting it than for leaving it: a reader who stops at the first half has been told the wrong thing. The occurrence in section 23's own changelog table is prior text in a record and is left as written. The stated count survives the change and is now asserted, so section 1's breakdown cannot drift from section 11's table.

### 2026-09-08 - CLAUDE.md - a commit belongs to the checkpoint that authorises it
Corrects: the commit convention said work done ahead of the checkpoint that owes it names that checkpoint, and said nothing about work whose subject matter belongs to a later phase. Found by writing a document repair to section 12.2 under the subject `Phase 5 / 5.0`, on the reasoning that 12.2 describes the research lanes and the lane assignment is settled at 5.0, when the edit was authorised by 1.1's planning pass and discharged nothing 5.0 owes.

Was:
> **A commit subject is `Phase {phase} / {checkpoint} - {what changed}`.** The checkpoint is never omitted, including on a commit that builds nothing: a ruling, a document pass, a correction and a sign-off addendum all belong to a checkpoint. Where work is done ahead of the checkpoint that owes it, the subject names that checkpoint rather than the one being worked on now.

Now:
> the same paragraph, followed by one stating that a commit belongs to the checkpoint that authorises the work and never to the phase whose subject matter the edited text describes, with the reason: a checkpoint from an unbuilt phase reaching `PROGRESS.md` makes the reconciliation refuse every claim still owed at it

Why: the existing clause is about an obligation arriving early, and it reads as being about topic if nothing says otherwise. The cost is not an untidy log. `DuePoints.HasLanded` reads `PROGRESS.md`, and out of scope means a point that has not been reached, so a phase 5 checkpoint recorded while phases 2 to 4 are unbuilt would fail every claim owed at it.

### 2026-09-08 - ARCHITECTURE.html - the mark renderer, and two rows that disagreed with themselves
Corrects: three defects in sections 7 and 16, all found while planning 1.1 against them. Section 15.4 says the server writes the shell and the marks, and the only server-side serve component was the read API, whose row says it performs no computation and no fetching, so the component that turns stored values into SVG had no row and 1.3's done condition could not be stated against anything. The theme research runner's catalogue row declares it reads the theme store and source documents while its matrix row carries W in both columns and no R, which `component-access` fails on the day it is written. The membership loader's matrix row carries W and no R while 1.1's done condition requires a membership query for a past date, which is a read.

Was:
> section 7 carried nine components and no renderer, with the read API, the single page app and the report exporter as the whole serve layer
> the matrix row: Theme research runner, W in Research and theme, W in Sources
> the matrix row: Membership loader, W in Membership
> the catalogue row: Membership loader reads "index membership feed"

Now:
> section 7 carries a tenth, Mark renderer, in the serve layer, reading the read API and writing none, described as turning stored values into the seven marks as SVG strings server side so the app and the exported report carry the same pictures from the same numbers
> the matrix row: Theme research runner, R W in Research and theme, R W in Sources
> the matrix row: Membership loader, R W in Membership
> the catalogue row: Membership loader reads "index membership feed, membership" and answers which names were members on a past date

Why: the alternative to a renderer row was a decision redefining "computes nothing" so that geometry is not computation, plus a check able to tell arithmetic on figures from arithmetic on coordinates by inspecting expressions. A check that has to make that distinction gets it wrong quietly, and the read API's done condition is that it is proved to compute nothing. A row makes the seam a claim the harness asserts rather than a definition it trusts, keeps the read API's row literally true, and gives the marks decision a component enforcing one renderer for both surfaces. Its access declaration is empty across all eleven stores, which is a claim and not an omission. The two disagreeing rows are the same defect read from opposite ends: a component that reads what it writes, with only the write declared.

### 2026-09-08 - BUILD_PLAN.md - contradiction F named the wrong thing, and three more were found
Corrects: F was written against "the catalogue's chart renderer", and section 7 carried no such row, so the contradictions table named a component that does not exist. The real defect of that shape is section 15.5's Level chart mark. Three further contradictions were found while planning 1.1 and had no home, and a finding named only in a planning conversation dies with it. Separately, three carried obligations were due at 1.5 and concern the first committed manifest, which arrives at 1.1.

Was:
> | F | The catalogue's chart renderer reads levels and indicators, which do not exist until phase 2, while the phase table puts a chart in phase 1 | 1.3 |
> Contradiction F resolved: the catalogue's chart renderer read set says what it reads at each phase, or the claim is scoped to 2.4, whichever the reconciliation permits.
> | Absolute path matching anywhere in a value, not position zero | 0.7 review | 1.5 |
> | Zoneless instant refused, clock check widened for the implicit machine-zone read | 0.7 review | 1.5 |
> | Manifest checker scans the captured response and checks the named file exists | 0.7 review | 1.5 |
> Four carried obligations discharged: absolute path matching anywhere in a value rather than at position zero; the zoneless instant refused, with the clock check widened to catch the implicit machine-zone read; the manifest checker scanning the captured response and checking the named file exists; and the news feed probed for whether it is queryable by date without a ticker.

Now:
> F names section 15.5's Level chart mark and its four elements, resolved per element rather than per mark: candles and the volume pane asserted at 1.3, bands and moving averages out of scope until 2.4
> G, the theme research runner reading what it writes with only the write declared, resolved at 1.1
> H, `news_pulse` declared one year retained with Delete given to nobody, due 1.4 with A
> I, the splits and dividends feed read by the nightly path with no source box in section 5, due 1.6
> J, section 17 naming two source lists against the record's three, due 1.7
> the three obligations re-pointed to 1.1 and 1.3, with 1.5 keeping only the news feed probe

Why: F named a component that does not exist, so nothing could have resolved it as written and a session reading the table would have gone looking for a row. Resolving the real defect per element rather than per mark keeps what exists asserted at 1.3 instead of leaving the whole mark unexamined for a phase and a half. The obligations were re-pointed on the reasoning the 0.7 repair session used when it moved them from 2.1: an obligation about manifests owed at 1.5 goes unfixed across every manifest written from 1.1 onward.

### 2026-09-08 - BUILD_PLAN.md - 1.3 names the mark renderer it builds
Corrects: 1.3's text named the read API and the chart and not the component between them, so the mark renderer added to section 7 in the same pass had no checkpoint naming it. That is a gap in the plan on its own, and it also left the component's due point unavailable to the derivation, which reads due points from the checkpoint whose text names the subject.

Was:
> The read API serving bars for a name and a date range, computing nothing and fetching nothing, asserted over the shipped source. The chart drawn as server-rendered SVG: candles and a volume pane on a shared time axis, at the hash route for a name.

Now:
> the same sentence with "The mark renderer as its own component, declaring an empty access across all eleven stores, so the seam section 15.4 describes is a claim the harness asserts rather than a definition it trusts." between the two clauses

Why: a checkpoint that builds a component and does not name it cannot be found by anything reading the plan, by a person or by the harness. The other half of this pass makes that concrete: due points are now read from the checkpoint whose text names the subject, so a component the plan does not name falls to a hand-written entry that nothing keeps current.

### 2026-09-08 - CLAUDE.md, BUILD_PLAN.md - component-access on the roster, three obligations discharged
Corrects: nothing. This records a clean edit rather than a defect, and it names no authorising decision because the corpus already required both changes: the Checks roster lists every check that runs, and the carried obligations table records a due point rather than remembering one.

Was:
> the Checks roster carried 24 rows and none for `component-access`
> the carried obligations table carried three rows due at 1.1, being `writer-ownership` widened to what its roster row claims, the zoneless instant refused with the clock check widened, and the manifest checker scanning the captured response

Now:
> 25 roster rows, the new one reading: Every component declares the stores it reads and writes, and the declaration is reconciled against its catalogue row, its read and write matrix row cell by cell with the blanks included, SCHEMA's ownership, and the statements in its own source, in both directions
> the three rows removed, with a paragraph beneath the table naming what discharged each and why the first was forced rather than merely due

Why: a check that runs as a CI step and is not declared on the roster is a property nobody wrote down, and the phase report enumerates checks by name against that table. The three obligations were discharged by the first component landing rather than by anyone remembering them, which is what an obligation with a due point is for.

### 2026-09-08 - ARCHITECTURE.html - the night is idempotent in what it records, not byte for byte
Corrects: section 14 claimed that running a night twice produces identical stored state, and 1.1's own code is what made that false. Every membership row carries `observed_at`, the instant of the fetch, and the run log gains a row per stage per run. Found by reading the loader against the claim while correcting a test that had asserted over four of the row's five columns and called it the state.

Was:
> No model is called and no per-name network request is made. The run is idempotent, so running it twice on one night produces identical stored state. It is scheduled with Task Scheduler after the US close.

Now:
> the same note, with the claim narrowed to say the run is idempotent in what it records about the market, that it does not produce a byte-identical store and is not meant to, and that the observation instant and the run log are what record that the night ran twice

Why: the original reads as a claim about the bytes and is a claim about the market data. Left as written, the first person to diff two runs finds it false and has to guess which half was meant. The observation instant is stored data and it is supposed to move; a run that left no trace of having repeated would be the defect, not the property.

### 2026-09-08 - CLAUDE.md - clock-usage covers the machine's locale as well as its clock
Corrects: the roster row named the machine clock and local schedules, and the check now also refuses a date parsed without a culture. A row narrower than its check reads as coverage nobody has, which is the same defect two rows carried at 0.7 and is being repaired here in the same commit that widens the check rather than a phase later.

Was:
> | `clock-usage` | every CI run | Nothing outside the clock reads the machine clock, and no schedule is expressed in local time |

Now:
> | `clock-usage` | every CI run | Nothing outside the clock reads the machine clock, no schedule is expressed in local time, and no date is parsed against the machine's locale. Comments are stripped first, because a sentence naming a pattern is not a use of it |

Why: a date parsed with no culture resolves against the machine's locale, so the same payload is a March date on one machine and a refusal on another. That is the same property the check already owns, being that nothing depends on how this machine happens to be configured for time, and it was found in shipped code rather than in the suite.

### 2026-09-08 - ARCHITECTURE.html, SCHEMA.md - the backfill is a component, and the limit that forbade it
Corrects: two defects, both found by building the backfill against the documents. Section 14's run order carries the backfill at position two, section 17 gives it a limits row, and section 7 had no component for it, so the class that does the work had no catalogue row and the matrix asserted nothing about what it touches. And the per-name limit read as a flat zero, which forbids the step the run order carries: a check reading it would have failed on a rule nobody meant.

Was:
> section 7 carried no row between Membership loader and Bar fetcher, and the matrix likewise
> | Per-name network calls in the nightly run | 0 | bars arrive in one bulk file and news in one feed request, so the run does not grow with the universe | run log |
> | `bar` | BarFetcher, CorporateActionChecker | none | CorporateActionChecker |
> **`bar` has two inserters and one deleter, and that is the one exception this file argues for.** BarFetcher inserts the day's bars.

Now:
> section 7 and the matrix carry **Backfill**, in the compute layer, reading the historical price feed, membership and the bar store, writing the bar store and the run log
> | Per-name network calls in the nightly run | 0 in the steady state, and the backfill is carved out of it | ... it makes one request per name holding no history, which is every name on the first run and a new joiner afterwards, and never again for a name that already holds its year ... | run log, on the steady-state stages |
> | `bar` | Backfill, BarFetcher, CorporateActionChecker | none | CorporateActionChecker |
> **`bar` has three inserters and one deleter** ... Backfill inserts a name's first year, once, on the run that finds it holding none.

Why: the alternative to a component row was folding the backfill into the bar fetcher, which arrives at 1.4 and does a different job on a different endpoint with the opposite cost shape. A row makes what it touches a claim the harness asserts. The limit is carved rather than deleted, because the property it protects is real and is about the steady-state night: what was wrong was stating it as a flat zero over a run whose second step is per name by design.

### 2026-09-08 - CLAUDE.md - the record is written before the run that signs the checkpoint off
Corrects: a suite run before the PROGRESS entry exists is a run against a corpus in which the checkpoint has not landed. `HasLanded` reads `PROGRESS.md` to decide what is out of scope, so every claim the entry is about to make due is still out of scope and cannot fail. It happened three times: at 1.1, where a claim placement whose due point had arrived reached CI unexamined; at 1.2, where `Run log` and `Backfill` derived the checkpoint that was landing in the same commit; and at 1.2 again on the capture pass. Each was green on the machine and red on all three runners, which is the signature of the fault rather than a coincidence.

Was:
> All seven, or it is not done:
> ...
> 7. The checkpoint's expectations are added to the fixture, so `tools/verify-phase` covers it from now on, and at least one of them is derived independently rather than frozen from a run.

and, under Merge:

> **A checkpoint lands as its own commit** and satisfies all seven done conditions on its own, and a session that has committed code still may not sign it off.

Now: the list opens `All eight`, the merge sentence says `all eight done conditions`, and an eighth condition is added:

> 8. The PROGRESS entry of condition 6 is written **before** the run that verifies the checkpoint, not after it, and the figures conditions 2 and 5 record are filled in from that run. The record is what the reconciliation reads ... That run is green on a question it never asked.

Why: it was recorded in the 1.2 entry as a note to a future session, which is a record telling a reader what to do rather than a rule the corpus holds. The ordering is not a habit, it is a property of the harness: the last thing written is the one thing nothing after it re-reads. Numbered rather than added as a paragraph so it is ticked with the others, and `stated-counts` now reads the expected count out of the sentence rather than repeating it as a literal, so the next condition added needs only this file edited.

### 2026-09-08 - RUNBOOK.md - the secrets section names the keys it tells the operator to write
Corrects: the section said to write `appsettings.Secrets.json` by hand and never said what to put in it. The first file written by hand consequently used a name of its own, `Secrets:EodhdApiToken`, and the code looked for `EquityBrief:Providers:Eodhd:ApiKey` and found nothing. Found on the pass that captured the fixture, which was the first work in this repository to need a live credential.

Was:
> Moving to a new machine: copy the checkout, copy the store file, write the secrets file by hand. The secrets file is the one part of the move that is a human act and cannot be scripted.

Now: that paragraph is unchanged and is followed by the key names, the nested file shape they take, a table naming the provider and the projects that need it, and the environment-variable spelling.

Why: an instruction to write a file by hand that does not say what the file contains is an instruction that can only be followed by guessing. The name now lives in a document and in code, which is two places for one fact, so `ProviderCredentialsTests` asserts the runbook against `ProviderCredentials.ApiKeyName` in both the path form and the nested form.

### 2026-09-08 - RUNBOOK.md - installing a bash is a step, because the tools need one
Corrects: "Moving the installation" named the SDK and said what fails without it, and said nothing about bash, which every `.ps1` in `/tools` hands its work to. On the operator's own machine `tools/ci.ps1` and `tools/verify-phase.ps1` had never run from a PowerShell prompt: Git for Windows puts `git.exe` in `cmd\` and `bash.exe` in `bin\` and only the first is on `PATH`, so the only `bash` reachable by name was the WSL launcher, which cannot open a Windows path. Found on this pass by running the documented command.

Was:
> 2. Install a .NET SDK in the `10.0.3xx` band ... Without one the run fails at step 5 with a restore error that names neither.
> 3. Copy `data/equitybrief.db` into the configured data root.

Now: a new step 3 names Git for Windows, says the wrapper looks beside `git` as well as on `PATH`, and says the WSL launcher does not count and why. The steps after it are renumbered and step 2's cross-reference moves from step 5 to step 6.

Why: the list is what an operator follows on a new machine, and it named the one dependency whose absence produces a legible error while omitting the one whose absence produces an advertisement for installing a Linux distribution. `tools/run-bash.ps1` now chooses a bash by asking each candidate whether it can read the script rather than taking the first on `PATH`, so the property is enforced as well as documented, and the suite's own lookup matches it.

### 2026-09-08 - BUILD_PLAN.md - two parsers checked against themselves, named as a defect and given due points
Corrects: the splits and dividends feed and the news feed each ship a parser and a recorded double, and neither has seen a provider response. That is the state the membership parser was in until 1.2, when the first captured constituents payload showed it had been reading dates out of an object the provider does not put them in. It had been green for two checkpoints because the same session wrote the parser and the fixture it was checked against. The carried obligations table had no row for either feed, and the 1.6 and 1.7 done conditions asked for a fixture without saying where it comes from.

Was: the obligations table ran from "Architecture cites its decisions by name at each rule" straight to "`two-platform` widened to what its roster row claims", and the two done conditions read:

> **Done when** an action in the fixture triggers a refetch, the replacement is atomic, and a failure of the check itself marks the name rather than passing.

> **Done when** the measurement is recorded with its sample named, and the first draft of the company-news and industry source lists exists with a review date.

Now: two rows are added to the table, due at 1.6 and 1.7, which are the checkpoints that build each parser. Both name the defect rather than the work:

> | Splits and dividends parser checked against itself, its only fixture written by the session writing the parser | 1.2 | 1.6 |
> | News parser checked against itself, its only fixture written by the session writing the parser | 1.2 | 1.7 |

Each done condition gains the clause that its fixture is a captured provider response rather than a constructed one, and each section gains a paragraph saying the feed is captured when the checkpoint opens rather than after the parser is written.

Why: the row has to name the defect because the task is the easy half. "Capture the news feed" reads as a chore and gets done late or partly; "the parser is checked against itself" says what is wrong now, so a session that writes the parser first has broken something rather than deferred something. The ordering matters for the same reason: a fixture written after the parser is a transcript of what the parser already expects, whichever session writes it. Ten weighted calls found a defect that two checkpoints of green had not.

### 2026-09-08 - SCHEMA.md - the four prices are one adjusted set, and the raw close is kept
Corrects: 1.2 stored the provider's adjusted close beside its unadjusted open, high and low. The decision named **The stored series is adjusted** says one price set per bar and it is the adjusted one; a bar holding three raw prices and one adjusted price is a mixed set. 96 of the 756 stored fixture bars carried a close outside their own low and high, and nothing looked. Found at 1.3, by needing to draw a candle from one.

Was:
> | `open`, `high`, `low`, `close` | TEXT | decimal in code |
> | `volume` | INTEGER | |
> | `source` | TEXT | which endpoint delivered it |
> | `observed_at` | TEXT | UTC instant |

Now: the price row reads "decimal in code, and one adjusted price set", a `raw_close` column is added after `observed_at`, and two paragraphs state that the other three prices are scaled by the same factor and that the raw close is kept because it is the input to that factor.

Why: the provider adjusts the close alone, so passing the other three through stores a bar that could not have traded. The raw close is kept rather than discarded because the factor is the adjusted close over the raw one, and a store holding only the output cannot recompute or audit it after a later restatement moves it; the corporate action checker's refetch is what moves it. The column sits last because migration 4 adds it to a table migration 3 created, and `bar-append-only` forbids a migration dropping a bar table to reorder its columns.

### 2026-09-08 - CLAUDE.md - bar-bounds added to the roster, and nightly-cost re-pointed to 1.4
Corrects: two defects. The roster had no check asserting that a stored bar could have traded, which is why 96 impossible bars survived a checkpoint. And `nightly-cost` was rostered "from 1.3" while `BUILD_PLAN.md` implements it at 1.4, so `coverage-reported` would have refused the 1.3 record: its rule is that a "from" row names a checkpoint `PROGRESS.md` does not yet record. Found by reading the roster against the plan before writing the 1.3 entry.

Was:
> | `nightly-cost` | from 1.3 | The nightly path makes zero model calls and zero per-name network requests, asserted over the shipped source and over a recorded run |

Now: the same row reads `from 1.4`, and a new row is added beneath `bar-append-only`:

> | `bar-bounds` | every CI run | Every stored bar has its low at or below its open and its close and its high at or above both, and carries the raw close its adjustment factor came from |

Why: 1.4 is the checkpoint that builds the nightly path, so it is the first point at which the nightly cost can be asserted over a recorded run; 1.3 was the old ordering. And the finding at 1.3 was not the mixed price set, which is one arithmetic error in one component. The finding was that a bar could be internally impossible and no instrument asked, so the repair is a property asserted over the store on every run rather than a test beside the arithmetic that happens to produce it.

### 2026-09-08 - BUILD_PLAN.md - contradiction F names the checkpoint that draws each element
Corrects: 1.3's resolution said the bands and the moving averages both stay out of scope until 2.4, and 2.1 says "The chart from 1.3 extended in place with the average lines drawn" with a done condition requiring it. The averages are drawn at 2.1 and only the bands at 2.4, so the resolution deferred one element by three checkpoints past the one that builds it. Found while reading 1.3 against phase 2.

Was:
> Contradiction F resolved per element rather than per mark: candles and the volume pane are asserted at 1.3, and the bands and the moving averages stay out of scope until 2.4.

Now:
> Contradiction F resolved per element rather than per mark, and each element named at the checkpoint that draws it: candles and the volume pane are asserted at 1.3, the moving averages at 2.1 and the bands at 2.4.

Why: the whole point of resolving F per element is that an element is asserted where it exists. Naming 2.4 for both defeats that for the averages, which draw a checkpoint into phase 2 and would have sat out of scope for three checkpoints after the code was there.

### 2026-09-08 - BUILD_PLAN.md - contradiction D resolved, and the floor it stood on replaced
Corrects: `Scope.Screens` answered on the table heading, so every row of a section shared one due point and "The exported report" was owed at the chart checkpoint because it sits in the same two-row table as "The app". Resolved at 1.3.

Was:
> | D | `Scope.Screens` keys on the table heading, so a phase 5 export claim is forced to be asserted at the chart checkpoint. This is the 0.7 repair of `Scope.For` failing to sweep, not a new contradiction | 1.3 |

Now: the same row, with **Resolved at 1.3** and the shape of the resolution, keyed on the table and the row together with all 37 rows of section 15 reconciled against the document in both directions.

Why: the sweep the 1.3 text promised found one more instance of the same defect and it was in the measurement rather than in the map. `MostDuePointsAreDerivedRatherThanWritten` counted a subject as derived from the plan when the subject was absent from the residual list and the plan could name it, which is a question about capability rather than about which branch ran. Fifteen section 15 rows are headed in ordinary English, "The table", "The chart", "Filters", "Walk", so a whole-word search of the plan's prose finds them by coincidence; all fifteen were counted as derived while a table heading supplied their due point. The floor of 40 sat under a count of 45 of which a third was miscounted, so it could not be carried across the re-key: a floor carried across a redefinition of what it counts is a floor that means something else. The count is now taken by origin, per claim rather than per distinct subject, because a subject appearing in two tables is two claims with two verdicts and counting subjects under-counted the population the partition is about. The property moved with it: the four origins are asserted to sum to the claims out of scope, so a claim answered by none cannot pass as answered, and the floor is 20 on the plan's share, far below the 50 measured, because that share falls to zero by construction as the system is built and its size is a fact about how much is built rather than about the derivation.

### 2026-09-08 - BUILD_PLAN.md - contradiction F resolved in the harness's reading, not in the table
Corrects: section 15.5's Level chart row names four elements drawn at three different points, and one verdict over the row held what exists hostage to what does not until phase 2. Resolved at 1.3.

Was:
> | F | Section 15.5's Level chart mark names four elements, candles, bands, moving averages and a volume pane, and two of them cannot exist until phase 2, while the phase table puts a chart in phase 1 | 1.3 |

Now: the same row, with **Resolved at 1.3** and the shape of the resolution, the row read as four claims in the harness rather than split into four rows in the document, with each element asserted to appear in the row's own description.

Why: splitting the row was the obvious repair and it is the wrong one. Section 15.5 opens by stating seven marks over a table of seven rows, so four rows would leave the document disagreeing with itself, and it would turn one mark into four in a vocabulary whose stated point is that a mark is defined once and every screen draws from the list. So the document keeps one row and the harness reads it as four claims, `Level chart, candles` and `Level chart, a volume pane` at 1.3, `Level chart, the moving averages` at 2.1 and `Level chart, the level bands` at 2.4.

What keeps that from being a second statement of the row's content is that each element phrase is read back out of the row's own description cell, so an element renamed in the document or invented in the harness fails, with a permanent proof over a constructed table where one of four elements is absent. And `stated-counts` now reads the opening sentence's count against the table's rows, so the repair this entry rejects fails too rather than being available to a later session as the obvious thing to do. The claim total moves from 162 to 165, which is the figure Pass B predicted for this decomposition against the base it had then.

### 2026-09-08 - BUILD_PLAN.md - three specification holes filed, and two checkpoints that were counting their own
Corrects: three holes found by reading a hand-written report for another name against the corpus, and named only in conversation. A finding named only in conversation is one that dies with it. Filed at 1.3 against the passes that own them, and nothing here is built now.

The three are added to the holes table, grouped by settling point: whether a heavy volume shelf creates a band or only ranks one, at 2.0; whether the calendar holds events that are not earnings, at 3.0; and how a tranche condition that depends on a researched fact reaches the tranche, at 3.0.

The first matters because section 9.1 lists shelves as one of four candidate sources that create bands, and a price range holding heavy volume with no swing, average or retracement is a band under that reading and invisible under one where shelves only rank. The second because a book keyed to one date cannot carry two, and non-earnings events are news-derived rather than fetched. The third because the ladder builder's matrix row gives it levels, indicators and the calendar, and a researched fact reaches it through none of those.

Was, at 2.0:
> Settles the volume profile's window, and which two swings the retracements are drawn between. Confirms the checkpoint split below against the size the work turns out to be.

Was, at 3.0:
> The heaviest planning pass in the project, because four holes settle here and one of them is a missing component.
>
> Also settles the trend classifier's rule, which tranche condition applies when, the share of size per tranche, and the earnings setups' four elements each. None of these is inferable from what is written, and all four decide what the plan section says.

Now: each points at the holes table rather than restating a subset of it, and 3.0 states no count at all.

Why: a hole is only useful if the pass that owns it reads it, and both passes were listing their own. 2.0 named two of the three it is now assigned and 3.0 named four and stated "four holes settle here", so filing a hole against either would have left the checkpoint text describing the old set. This is the same defect the due points had before Pass B derived them from the plan: one fact written in two places, where the second copy is the one that goes stale and nothing reconciles them. The repair is the same move, pointing at the one statement rather than repeating it, and it removes a stated count that `stated-counts` would otherwise have to be taught to assert.

### 2026-09-08 - ARCHITECTURE.html - two notes naming where an open question is settled
Corrects: two places the document reads as settled and is not. Both are additions, so nothing is removed and no prior text is owed; they are recorded here because a note that names a settling point is a claim about the plan and belongs in the record with the rows it points at.

Section 9.1's key gains, after the paragraph on the volume shelf:
> Whether a heavy volume shelf creates a band or only strengthens one built by the other three is settled at 2.0 and is not decided here. The two readings produce different band sets, so the level builder differs depending which is taken.

Section 10's key gains, at the end:
> The second book is keyed to dated events, of which an earnings print is one. Whether it carries events that are not prints, and where those come from, is settled at 3.0.

Why: 9.1's figure lists heavy volume shelves alongside swings, averages and retracements as the candidates that create bands, and its key then argues the shelf's importance without saying whether it creates or only strengthens. A reader building the level builder at 2.4 would take the figure literally, and that is a different component from the one a reader of the ranking reading would build. Section 10 names the second book the earnings trade throughout, which reads as a book keyed to prints rather than to dated events of which a print is one kind. Naming the settling point is the smallest edit that stops either being read as decided, and it leaves the decision where the plan already puts it.

### 2026-09-08 - CLAUDE.md - prefix matching named as a shape to sweep for
Corrects: nothing removed. A rule is added to the Verification list, which is where rules taken from what has gone wrong elsewhere live, and this one is taken from what has gone wrong here four times.

Now, added at the end of that list:
> A matcher keyed on a prefix answers about everything sharing that prefix. Where a key is the opening of a value rather than the whole of it, the property is that exactly one key matches, asserted in both directions rather than left to the order a dictionary happens to yield. This is a shape to sweep for rather than a defect to fix one instance at a time: it has arrived four times, as the nightly step keys, as a subject matched without its table, as a phase read as landed from any heading beginning with its number, and as a roster row retired by a heading whose entry said it was not a checkpoint.

Why: the four are one defect wearing four faces, and each was found by tripping over it rather than by looking. The third was a live defect with no symptom, which is the state that carries one past the point where it bites: `HasLanded("phase 2")` matched the bare string `### 2.`, so the pass that plans phase 2 would have read as phase 2 having landed and failed every claim still owed at it, and the existing `### 1.1 planning` entry already answered that question true for phase 1 with nothing noticing, because nothing is due at bare "phase 1". Written as a rule, the next instance is found by the sweep it names rather than by the failure it causes.

### 2026-09-08 - CLAUDE.md - read-surface added to the roster
Corrects: nothing removed. A row is added to the checks table for the check 1.3 built.

Now, added above `ci-parity`:
> | `read-surface` | every CI run | The read API hands back every stored value unchanged, and the page draws one candle and one volume bar per stored session, matched session by session against the store |

Why: three claims in section 15 are claims about a surface, and a surface is not something a declaration can assert. `component-access` reaches the read API, the mark renderer and the app as components, which says what each may touch and nothing about what the page draws. So the drawn claims are reached by a check that renders the surface and reads it back, and the reconciliation refused that check's name until it was on the roster with a declared reach, which is the fiat guard working on the session that wrote it.

### 2026-09-08 - SCHEMA.md - contradictions A and H resolved, two sanctioned removals named
Corrects: a three-way contradiction, not a two-way one. The `bar` note said the fetcher drops sessions older than the retention window, the ownership row gave Delete to `CorporateActionChecker` alone, and the exception paragraph said twice that a refetch was the only sanctioned removal of a bar. Any two of the three could be read as agreeing, which is why it survived a review. `news_pulse` carried the same defect in a second table, declared one year retained with a Delete cell of "none".

Was:
> | `bar` | Backfill, BarFetcher, CorporateActionChecker | none | CorporateActionChecker |
> | `news_pulse` | NewsPulseCounter | none | none |
>
> **`bar` has three inserters and one deleter, and that is the one exception this file argues for.** ... CorporateActionChecker deletes and reinserts a name's whole year when an action changes its adjusted prices, which is the only sanctioned removal of a bar in the system. ... Nothing else may delete or update a bar, and `bar-append-only` asserts that over the shipped source and over every migration.

Now: `bar` has two deleters and `news_pulse` has one, and a paragraph states that two removals are sanctioned and that neither takes a bar out of a series it leaves standing.

Why: the resolution is not that the rule was wrong but that it was about a third thing. Retention removes every session below a date boundary for every name at once, and what remains is a contiguous series ending tonight. A refetch removes one name's whole year and writes it back in the same transaction, so the series is replaced rather than shortened. What the append-only rule forbids is a bar inside a stored series being deleted or edited while the rest stands, and that is untouched: no update to a bar by anything, and no delete by a component this file does not name. `bar-append-only` reads the deleters out of the ownership table rather than carrying a list of its own, so the exemption is a property of the declaration and moves when the declaration does, and its negative proof plants the same statement in a file SCHEMA does not name and asserts it still fails.

### 2026-09-08 - CLAUDE.md - nightly-cost promoted, nightly-run added
Corrects: nothing removed except the `nightly-cost` row's Runs cell, which named a checkpoint that has now landed.

Was:
> | `nightly-cost` | from 1.4 | The nightly path makes zero model calls and zero per-name network requests, asserted over the shipped source and over a recorded run |

Now: the same row reads `every CI run` and states that the run is measured over two universe sizes, and a `nightly-run` row is added above `read-surface`.

Why: the limit is not "one request" but "a count that does not grow with the universe", and a night measured once over one population has been measured against a number rather than against the rule. So the check runs a night over three members and again over two and asserts the request count did not move. `nightly-run` is separate because the ordering of section 14's steps is a claim about the run rather than about cost: membership before the backfill because a backfill asks which names are members, and the backfill before the fetch because a name with no year is one the fetch would leave holding a single session.

### 2026-09-08 - DECISIONS.md - news arrives in one dated feed request
Corrects: nothing removed. A decision is added under Data, taken from a probe rather than from an assumption.

Now:
> **News arrives in one dated feed request and is attributed to names locally** The feed is queryable by date with no ticker, which was probed on the operator's key at 1.5 rather than assumed ...

Why: the carried obligation asked whether the feed can be queried by date without a ticker, and 1.7 depends on the answer. One request settled it: a date range with no ticker returns 200 with articles for the whole market, every row carries a `symbols` array naming the tickers it is about, and the response shape is identical either way, so one parser serves both. A night therefore makes one request and fans the rows out in code. The alternative would put five hundred calls on the nightly path for the same articles and break the rule that a night costs the same for fifty names as for five hundred. The row also carries the article text, so the document a claim rests on arrives with the row rather than needing a second fetch.

### 2026-09-08 - BUILD_PLAN.md - the news feed obligation discharged
Corrects: an obligation that had stood since the architecture was authored.

Was:
> | News feed queryable by date without a ticker | authored with the architecture | 1.5 |

Now: the same row reads `1.5, discharged`.

Why: it was owed here because 1.7 cannot be planned without the answer, and the answer is yes. Recorded as a decision rather than only as a discharged row, because a later session could reasonably choose a request per ticker and the cost of that choice would be invisible until a night got slow.

### 2026-09-08 - SCHEMA.md and ARCHITECTURE.html - contradictions C and I, and the catalogue row the checker outgrew
Corrects: three things, all found by building the component the row describes.

**C.** The failure table said a name whose corporate action check failed is marked suspect and nothing held that, so a failed check passed silently. Resolved with a table rather than a column, because grain is a property of a table and not of a column: `series_state`, one row per ticker, holding the state, the reason when it is not ok, and the instant the check that said so ran. Not a column on `bar`, whose grain is a session, and not on `membership`, which records whether a name is in the index and not whether its bars are believable. Section 16 gains the store row and the read and write matrix gains a twelfth column.

**I.** The splits and dividends feed was a read in the checker's catalogue row and was not one of section 5's source boxes, so the nightly path read a feed the system overview did not carry. The box is added.

**The catalogue row was incomplete, which is a third finding rather than part of either.**

Was:
> <td>splits and dividends feed, bar store</td><td>bar store</td><td>refetches a name's full year when an action changes its adjusted prices, because stored history silently diverges otherwise</td>

Now: Reads gains the historical price feed and membership, Writes gains series state, and the description says the refetch replaces the year inside one transaction and that a name whose own check failed is marked suspect rather than passing.

Why: the row named neither the feed the refetch fetches from, nor the store it asks which names are members, nor the state contradiction C's own resolution requires it to write. The refetch cannot happen without the first, the filter cannot happen without the second, and C cannot be resolved without the third. This was found by `component-access` refusing the declaration on the day the component was written, which is the check working rather than a document being edited to suit code.

### 2026-09-08 - ARCHITECTURE.html - the per-name limit gains its second carve-out
Corrects: the corporate action refetch makes one request per affected name, which the steady-state limit as written forbids.

Was:
> 0 in the steady state, and the backfill is carved out of it

Now: the same row carves out the backfill and the corporate action refetch, and states that the refetch is bounded by the day's actions rather than by the universe.

Why: the same shape as contradiction B and found the same way, by building the step the rule forbade. It is carved rather than the rule loosened, because a night that refetched every name would satisfy a loosened rule and defeat the whole design. On the fixture's captured day the actions affect one name.

### 2026-09-08 - DECISIONS.md - contradiction J, and where the source lists apply
Corrects: a decision whose name disagreed with its own body, and a premise the 1.7 measurement falsified.

Was, and now under "Previously decided":
> **Three source lists, not one, each with a review date** Filings and company releases need no list at all, since they are fetched by ticker. A company-news list governs reporting about a company. A separate industry list governs theme material ...

Now: **Two source lists govern the open web, and the licensed feed is a channel rather than a list**.

Why: the body described two lists and said filings need none, and the name said three. That is the whole of contradiction J and the name was the defect. The measurement added the second half. The licensed news feed delivered 1,173 of 1,253 articles under one domain, and the domain is the aggregator that carried the article rather than the outlet that wrote it, so a domain list over the feed would hold three entries and would filter nothing: filtering by domain would discard the whole feed or none of it. The lists therefore govern the search tool, where a query can return a football club, and the feed is admitted as a delivery channel with the per-document test doing all of the work on its articles.

### 2026-09-08 - CLAUDE.md - source-lists.json added to the layout
Corrects: nothing removed. A line is added to the repository layout block for the file 1.7 drafts.

Now:
> source-lists.json the two open-web lists a research search may return, with their review date

Why: the lists are configuration a research pass reads, not a document that argues anything, so they are a file rather than a ninth corpus document. They are named in the layout block because a file at the root that the block does not describe is a file nobody knows the purpose of.

### 2026-09-08 - CLAUDE.md - banned-prose does not govern a captured response
Corrects: a rule about how this corpus is written was being applied to bytes a provider sent. Found at 1.7, when a captured news article's own text carried an em dash. The three earlier captures happened to carry neither banned pattern, so this was a rule that had not yet met the thing it cannot govern.

Was:
> | `banned-prose` | every CI run | No text file the repository tracks contains the banned string or any form of it, and none contains an em dash. The line in CLAUDE.md's Prose convention that names the string is the single exemption, matched on the sentence that states the rule |

Now: the same row, with the captured provider responses excluded and the exclusion stated.

Why: the only way to satisfy the rule over a capture would be to edit the provider's text, and the manifest schema says in so many words that what may never be trimmed is the shape. A file edited to suit a prose rule is no longer a capture, and the whole value of a capture is that the parser is checked against what the provider actually sends. The exclusion follows the manifest's declaration rather than the folder, so a json file dropped into a fixture folder and never declared is still scanned, and the manifest, the README and anything under expectations/ are written here and are scanned too.

### 2026-09-08 - ARCHITECTURE.html - the citation pass, and section 19.1 read as claims
Corrects: two things owed at 1.8.

**The citations.** The obligation created at 0.5 was that the architecture cites its decisions by name at each rule. Six citations stood; 39 stand now, covering 29 of the 81 current decisions. Nothing is removed: each is added at the end of the cell or sentence stating the rule, so a reader meets it where they are already reading rather than in a footnote. The 52 that remain uncited mostly settle things the architecture states no rule about, and the remainder of the pass is carried to 2.0 with the figure recorded, because a citation cannot be placed at a rule that does not exist.

**19.1.** The table was placed whole, owed at 1.8. It is now a claim source, so each of its 13 rows carries its own due point. Nothing in the document changed for this; what changed is the harness's reading of it.

Why the second: read whole at 1.8 the table would have been asserted with eleven of its thirteen rows describing artefacts that do not exist, seven expected outputs arriving across phases 2 to 4, three rejections at phase 5 and a fundamentals input at 5.1. That is the same defect as reading a failure row whole, and it takes the same repair. `bars` and `news` pass now; the rest are owed where the artefact they describe arrives.
