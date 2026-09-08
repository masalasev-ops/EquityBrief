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
