# CLAUDE.md

Rules for any session working in this repository. Read this file first, every session, before touching anything.

---

## What this repo is

A nightly research tool over the S&P 500. It computes support and resistance levels for every name in the index each evening, decides which names are sitting at a price their own chart has made significant, and produces a full research report on any name the operator opens. It does not trade and holds no position.

**EquityBrief.** Solution, projects, namespaces and the root config section all use that name in full, with no abbreviation anywhere in code. A shortened form in one place and the full form in another is the kind of inconsistency that survives for years and then bites during a rename.

**.NET with C#, SQLite for the store.** One solution, one store file under the configured data root, no server to install on either machine.

The design source of truth is `docs/ARCHITECTURE.html`. It is the only place the system is described as a whole. If code and architecture disagree, that is a finding, not a licence to change either one silently.

## Where the build is right now

Nothing is built. What the build has reached is recorded below rather than stated here.

**Which checkpoint the build is on is the furthest checkpoint `docs/PROGRESS.md` records,** and the one to build next is the checkpoint after it in `docs/BUILD_PLAN.md`. That is stated as a pointer rather than as a number, because a number here is a second place the same fact lives and it goes stale the moment a checkpoint lands.

Anything a checkpoint has not built yet does not exist, however completely `docs/ARCHITECTURE.html` describes it. The architecture describes the finished system; PROGRESS says what exists.

## Read order for a fresh session

1. This file.
2. `docs/BUILD_PLAN.md`, the checkpoint you are on and its done condition.
3. `docs/SCHEMA.md`, if you will touch a store.
4. `docs/ARCHITECTURE.html`, the sections covering the components in your checkpoint.
5. `docs/DECISIONS.md`, the entries cited by the above.

Do not read the whole corpus. It is small on purpose and it is still larger than any single checkpoint needs.

## Repository layout

```
/src
  Directory.Build.props   the target framework, nullable, and warnings as errors
  EquityBrief.Core        domain, clock, config
  EquityBrief.Data        stores, migrations
  EquityBrief.Worker      the nightly run and the overnight queue, sole writer
  EquityBrief.Api         read surface
  EquityBrief.Web         the single page app
  EquityBrief.Tests       the suite
/docs             ARCHITECTURE.html  SCHEMA.md  BUILD_PLAN.md
                  DECISIONS.md  PROGRESS.md  CHANGELOG.md  RUNBOOK.md
/tools            ci.ps1  ci.sh          every CI step in order, against a dropped store
                  verify-phase  verify-phase.ps1   the phase report
                  migrate  migrate.ps1   apply migrations
                  nightly  nightly.ps1   what the scheduler calls, not run by CI
                  run-bash.ps1   the one place a .ps1 hands its work to a bash script
                  wrapper-probe  wrapper-probe.ps1   a script that prints on both streams
                                 and fails, so the suite can prove a wrapper returns both
/fixtures         README.md      the folder's shape, not a corpus document
                  manifest.schema.json   what a fixture manifest must carry; the suite
                                 reads its required fields from here rather than restating them
                  one folder per fixture name and date: the committed inputs, and
                  expectations/ holding what the rules in ARCHITECTURE produce over them
/artifacts        gitignored. the phase report, written by verify-phase
/prompts          gitignored. spent build prompts, kept locally
/data             gitignored. the store lives here
CLAUDE.md         these rules, read first every session
EquityBrief.slnx  the six projects, at the root
global.json       pins the SDK to the 10.0.3xx feature band
.github/workflows/ci.yml   the two-platform matrix and the Linux case-sensitivity job.
                  Actions reads workflows from this path and no other
.gitattributes    line endings, normalised to LF in the repository
.gitignore        the store, the prompts archive, the harness output, the secrets
                  files and the local harness settings
```

`EquityBrief.Tests` sits alongside the projects it tests rather than in a sibling tree. One consequence worth stating, because a check depends on it: `api-isolation` asserts that `EquityBrief.Api` has no transitive reference to `EquityBrief.Worker`, read from the compiled dependency file rather than the project file, and the test project is exempt because it references everything by design. That exemption is named here so a later session does not find it and assume the check is broken.

**`/prompts` is a local scratch archive.** Name files `YYYY-MM-DD-<checkpoint>-<short-description>.md` so the folder sorts chronologically. It is gitignored because superseded prompt text is noise in a diff. That is safe only while prompts stay scratch copies: anything inside a prompt that the corpus will later refer to, a decision, a rule, a threshold, a done condition, is written into the proper document at the time it is issued.

## Commands

| Purpose | Windows | macOS | Shell |
|---|---|---|---|
| Build | `dotnet build EquityBrief.slnx` | same | either |
| Run the suite | `dotnet test src/EquityBrief.Tests` | same | either |
| Run one test | `dotnet test --filter FullyQualifiedName~<name>` | same | either |
| **Verify a checkpoint** | `tools/ci.ps1` | `tools/ci.sh` | PowerShell on Windows, bash on macOS |
| **Verify a phase** | `tools/verify-phase.ps1` | `tools/verify-phase` | PowerShell on Windows, bash on macOS |
| Apply migrations | `tools/migrate.ps1` | `tools/migrate` | PowerShell on Windows, bash on macOS |
| Run a night by hand | `tools/nightly.ps1` | `tools/nightly` | PowerShell on Windows, bash on macOS |

**The target framework is `net10.0`, pinned in one place.** `global.json` at the root holds the SDK to the 10.0.3xx feature band and rolls forward to the latest installed, and `src/Directory.Build.props` carries the framework, nullable reference types and warnings as errors for all six projects. Before this the workflow was the only statement of the version anywhere, which left the two machines free to build against something CI never sees.

**The Shell column is there because a cell naming a script does not say what can run it, and the wrong shell fails quietly in one direction.** Calling an extensionless bash script by name from PowerShell produces no output, leaves `$LASTEXITCODE` unset and leaves `$?` true, so a gate that never executed is indistinguishable from one that passed. Every bash entry point in this repository therefore ships with a `.ps1` wrapper that finds a bash, hands the work to the one script rather than reimplementing it, and exits with a named message where the machine has none. A wrapper must return both the script's output and its exit code; a PowerShell function's return value is its output stream, so returning `$LASTEXITCODE` from a function swallows everything the script printed.

**`tools/verify-phase` is what a phase signs off against.** It runs the pipeline over the committed fixture, diffs every stage's output against frozen expectations, parses `docs/ARCHITECTURE.html`'s tables and asserts each claim against the code, and writes `artifacts/phase-report.html` for the operator and `artifacts/phase-report.json` for a build session. A phase is not done until that report is green, and green includes that nothing is listed as unexamined.

**`tools/ci.*` is not a wrapper around `dotnet test`.** It runs every step of the CI workflow in order against a dropped store, exiting non-zero on the first failure. A green `dotnet test` does not satisfy done condition 2.

The PowerShell and shell versions are not translations of each other. `&&` is a parse error in Windows PowerShell, so the two files differ in syntax by necessity. `ci-parity` asserts they run the same steps in the same order, not that they contain the same text.

Checkpoint 0.4 is what makes this table true. Until it lands, these are the contract rather than a description.

**Secrets.** `appsettings.Secrets.json` sits beside `appsettings.json` in each project that needs one. Gitignored, plaintext, never committed, and registered before environment variables so an environment variable still wins.

## Hard rules

Named, and cited by name. A violation is a defect regardless of what else is true.

**The nightly run makes no model call and no per-name network request.** Bars arrive in one bulk request and news in one feed request, so a night costs the same whether the universe is fifty names or five hundred. Any component that adds a per-name call to the nightly path is a defect, not a feature. (see: The nightly run is arithmetic only)

**Code owns every number.** Every figure in a report is computed from stored data or copied from a provider payload with the filing date it came from. No model is ever asked for a number, a date or an estimate. (see: Code owns every number)

**Every number in written prose must exist in the facts file.** Enforced by the claim checker, with one retry and then the section is omitted. (see: Every number in written prose must exist in the facts file)

**Every researched claim names a stored source document, and that document passed admissibility.** Having a source and having a believable source are different tests and both run. A document that fails admissibility is not stored, so a claim resting on it cannot be written. (see: A stored source is not automatically an admissible one)

**One writer per store per operation.** Declared in `SCHEMA.md`, asserted by `writer-ownership` in both directions: every declared writer exists in code, and every writer in code is declared.

**Bars are append-only and never interpolated.** Never delete or update a stored bar. A gap stops computation for that name and is reported as a gap; a corporate action arrives as a full refetch of that name's year. `bar-append-only` greps for delete and update statements against bar tables. (see: Bars are never interpolated)

**Prices are decimal in code and TEXT in storage. Statistics are double.** Never `REAL` for a price or a money value, and no implicit conversion between the two worlds. A helper that crosses the boundary does so explicitly and is named for it. This rule can be satisfied in code while still writing a `REAL` column, which is why the storage form is stated here rather than only in SCHEMA.

**Time is UTC in storage.** Session boundaries resolve through the clock abstraction using IANA identifiers only. Direct `DateTime.Now`, `DateTime.UtcNow` and `DateTimeOffset.UtcNow` outside the clock are banned and grepped. Every schedule is expressed in UTC, because the peak and off-peak windows of the research provider are fixed in UTC and a schedule written in local time moves into peak when daylight saving changes. (see: Queued work runs off-peak, and every schedule is written in UTC)

**The code runs unmodified on Windows and macOS.** No drive letters, no backslash separators, no registry, no Windows timezone identifiers, no shelling out to a platform-specific binary. Paths are composed through the platform API from one configured data root. Scheduling lives outside the application. `InvariantGlobalization` stays false, because it is the setting that silently breaks IANA timezone lookup. (see: Nothing is written against one operating system)

**No absolute path is written into a store row.** The store must remain a file that can be copied to another machine. (see: The whole system is a checkout and one database file)

**A listings row is written for every name in the index every night,** whether or not a reason fired. A shadow candidate has to be evaluated on the nights it would have fired, and most of those are nights no live reason surfaced that name. (see: Candidate conditions are registered before they are scored, and scored in shadow before they are shown)

**The candidate register is append-only.** No update and no delete. A retirement is a new dated row naming what it retires. A register that can be edited after results are in is not a pre-registration.

**Nothing expires on a timer.** Research is rewritten when a filing appears, an earnings date passes, a name's news volume jumps above its own baseline, or the operator asks. (see: Nothing expires on a timer)

**The spend cap is a hard stop, denominated in money.** At the cap, research pauses and the page says so. The overnight queue never makes a paid call at all. (see: The spend cap is a stop, not an allowance)

**An unresolved setup is never a win,** and an unexamined claim is never a pass. The same rule, one about trading outcomes and one about verification. (see: An unresolved setup is never a win)

**A reason's measured record is shown beside the reason and never beside the ticker.** Below the minimum, only the resolved count against the minimum is shown. (see: A reason's record is displayed, beside the reason and never beside the name)

## Checks

Executable, named, run by `tools/ci.*`. Each is a property that should hold at every moment, not a guideline anyone remembers.

| Check | Runs | Asserts |
|---|---|---|
| `writer-ownership` | every CI run | Every store has exactly one declared writer per operation, verified in both directions against SCHEMA |
| `component-access` | every CI run | Every component declares the stores it reads and writes, and the declaration is reconciled against its catalogue row, its read and write matrix row cell by cell with the blanks included, SCHEMA's ownership, and the statements in its own source, in both directions |
| `architecture-conformance` | every CI run | Every claim ARCHITECTURE.html makes, in a table or in the nightly run's ordered list, has a verdict: pass, fail, out of scope for this phase, or unexamined; every table in the document is placed so none can go unread; a claim that passes names the check that reached it, on both surfaces the report writes; and every placement and every pass is reconciled against what the check it names declares it reaches, in both directions |
| `decision-resolves` | every CI run | Every decision name cited in code or docs matches a bold decision name in DECISIONS.md exactly, and no two decisions share a name |
| `no-superseded-citation` | every CI run | No cited name resolves to a decision under "Previously decided" |
| `changelog-reconciles` | every CI run | Every commit that deleted a line from a spec also changed `CHANGELOG.md`, read from the history |
| `pinned-constants` | every CI run | Numeric constants stated in docs match the code constant they describe |
| `stated-counts` | every CI run | Every count a spec states about itself matches the derived count. Record entries are dated measurements and are exempt |
| `banned-prose` | every CI run | No text file the repository tracks contains the banned string or any form of it, and none contains an em dash. The line in CLAUDE.md's Prose convention that names the string is the single exemption, matched on the sentence that states the rule |
| `coverage-reported` | every CI run | Every check the roster says runs is implemented, is invoked by `tools/ci.*`, states its own scope in numbers, and left a coverage record in the run the phase report reads |
| `clock-usage` | every CI run | Nothing outside the clock reads the machine clock, no schedule is expressed in local time, and no date is parsed against the machine's locale. Comments are stripped first, because a sentence naming a pattern is not a use of it |
| `path-casing` | every CI run | Every file path appearing as a string literal in source matches the on-disk path exactly, byte for byte |
| `store-portability` | every CI run | No row in a populated store carries an absolute path |
| `schema-columns` | every CI run | Every table in a migrated store has the columns and storage types `SCHEMA.md` declares for it, in that order, and every table in the store is one the file describes |
| `price-storage-form` | every CI run | No migration declares a price or money column `REAL` |
| `build-properties-central` | every CI run | No project file states a target framework or a warning setting of its own, and `src/Directory.Build.props` states both |
| `api-isolation` | every CI run | `EquityBrief.Api` has no transitive reference to `EquityBrief.Worker`, read from the compiled dependency file |
| `bar-append-only` | every CI run | Nothing in the shipped source deletes or updates a bar table, and no migration deletes, updates or drops one |
| `bar-bounds` | every CI run | Every stored bar has its low at or below its open and its close and its high at or above both, and carries the raw close its adjustment factor came from |
| `ci-parity` | every CI run | `tools/ci.ps1` and `tools/ci.sh` run the same steps in the same order, and a step that fails fails the script it runs in |
| `two-platform` | the matrix | The suite passes on both windows and macos runners |
| `nightly-cost` | from 1.4 | The nightly path makes zero model calls and zero per-name network requests, asserted over the shipped source and over a recorded run |
| `fixture-replay` | from 2.1 | The pipeline over the committed fixture matches every expectation, with every figure it produces named by one |
| `listings-coverage` | from 4.1 | A listings row exists for every index member on every night the run completed |
| `claim-admissibility` | from 5.1 | A poisoned paragraph, an unsourced claim, and each inadmissible document class are refused, and nothing resting on them is written |
| `register-append-only` | from 6.1 | The candidate register refuses updates and deletes, and the correction divisor matches the rows registered before the window opened |

**The table lists every check that runs, not only the properties this file argues for.** A check that runs as a CI step and is not declared here is a property nobody wrote down, and the phase report enumerates checks by name, so the two would disagree with nothing to reconcile them.

**The Runs column is what makes the table a roster rather than a wish.** Every check either runs on every CI run, runs as the matrix, or names the checkpoint that starts it. `coverage-reported` asserts each of those three against the corpus: an "every CI run" row has to be implemented and invoked by `tools/ci.*`, and a checkpoint row has to name a checkpoint `PROGRESS.md` does not yet record, in a phase `BUILD_PLAN.md` has. The phase rather than the checkpoint, because later phases get checkpoint detail at the previous phase's sign-off, so requiring the checkpoint by name would forbid the roster from naming anything past the phase in hand.

**`coverage-reported` is the one that matters most and is easiest to lose.** Under-reporting is survivorship: a check that errors loudly gets fixed because it blocks, while a check that silently narrows its own scope keeps passing. So the only broken checks that survive in verification code are the ones that under-report. Green means "nothing I ran failed", never "nothing is wrong".

**A check that stops running is the sharpest form of that.** `dotnet test --filter` exits zero when the filter matches no test, so a renamed check leaves a CI step that passes by running nothing at all. `coverage-reported` reconciles the roster against the implemented checks and the CI steps, and the phase report requires a coverage record from every check the roster says runs.

**A check that a placement or a verdict names declares what it reaches, and the harness reconciles the two.** The declaration lives in the check itself, naming the corpus files it opens and the claim subjects or tables it can reach a verdict on. `architecture-conformance` refuses a placement or a pass naming a check whose declared reach does not include that claim, a check declaring reach over a subject no placement sends it, and a whole table claimed by a check that does not open the document carrying its rows. A declaration written beside a check rather than inside it is a second statement of one fact, and nothing keeps the two together.

**A check states a floor under each scope it names, and a run is measured scope by scope.** A scope whose size is a fact about the corpus rather than about the property, files read or literals scanned, is either left without a floor and marked as context, or given one far enough below its value that ordinary growth never moves it. It is never summed with the scope that carries the property. Write the check so the scope carrying the property is the one with a floor on it, and say which that is.

**Unexamined and out of scope are counted separately, and only one of them is a defect.** Unexamined means a claim this phase should have been able to assert and could not. Out of scope means the corpus places it at a checkpoint that has not landed, or exempts it by name and says why. `tools/verify-phase` is green only with zero unexamined; out of scope is shown beside it and never added to it. An out-of-scope claim names the checkpoint that ends it, and that checkpoint has to exist in `BUILD_PLAN.md` and not yet be recorded in `PROGRESS.md`.

**`path-casing` targets a bug neither of the operator's machines can see.** Case sensitivity is a property of the filesystem, not the operating system. Windows and macOS are both insensitive by default and Linux is not, so a path written with the wrong case works on both development machines and fails on a runner. The workflow carries a Linux job that runs `tools/ci.sh` so the pipeline opens its files on a case-sensitive filesystem on every push. That job is an instrument for one class of fault; Linux is not a platform this tool supports, and `two-platform` still claims exactly what it says.

## Conventions

**Decisions are named, not numbered.** A decision belongs in `DECISIONS.md` when a later session could reasonably choose differently **and** the wrong choice would be invisible. Mechanisms with one obvious implementation, and anything a test already enforces, do not.

A decision is identified by its bold name in `DECISIONS.md`. Cite the exact name. In code: `// see: Code owns every number`. In a document: `(see: Code owns every number)`. Same string either way, so one checker covers both. A number tells a reader nothing and forces a lookup; a name tells them the thing directly. A misremembered name fails to resolve, where a misremembered number resolves to the wrong decision and nobody notices. Decision names carry no terminal punctuation, because a name ending in a period is awkward to cite and invites the paraphrase `decision-resolves` exists to reject.

**A decision is changed only by another decision.** No finding, progress entry, checkpoint note or conversation supersedes one. Work that changes a decision writes a new one, names what it supersedes, and moves the old entry to "Previously decided" in the same commit, reasoning intact.

**Nothing in the corpus is struck through.** A spec is edited cleanly and its prior text goes to `CHANGELOG.md`. A record is corrected by a new dated entry naming what it corrects. Strikethrough leaves a document that is half history and half current state, and the reader has to work out which is which on every line.

**Components are named, not coded.** The catalogue lives in `ARCHITECTURE.html` under "Component catalogue", and a new component is added there in the same commit that introduces it.

**Headings in ARCHITECTURE.html carry numbers and everything else does not.** That document is read section by section by a person and by the conformance check, and its numbers are navigation. Cross-document references cite heading text, not numbers, because a misremembered number resolves to the wrong place and nothing notices. Checkpoint identifiers keep their numbers, because they name work in a sequence where the sequence is the point.

**A commit subject is `Phase {phase} / {checkpoint} - {what changed}`.** The checkpoint is never omitted, including on a commit that builds nothing: a ruling, a document pass, a correction and a sign-off addendum all belong to a checkpoint. Where work is done ahead of the checkpoint that owes it, the subject names that checkpoint rather than the one being worked on now.

**A commit belongs to the checkpoint that authorises the work, never to the phase whose subject matter the edited text happens to describe.** A wording repair to the screens section during phase 1 is phase 1 work. The clause above is about an obligation: a checkpoint owes something and the work discharging it arrives early. It is not about what the text is about, and reading it that way puts a later phase's number on a commit that phase did not authorise and does not advance. That is worse than an untidy log, because a checkpoint from an unbuilt phase reaching `PROGRESS.md` makes the reconciliation refuse every claim still owed at it: out of scope means a point that has not been reached, and `HasLanded` reads this record to decide.

**The pass that plans a phase belongs to the phase it plans, at that phase's opening checkpoint.** `Phase 2 / 2.0` for the pass that writes phase 2's section, not `Phase 1 / 1.8`. A PROGRESS entry for such a pass opens with **"Not a checkpoint entry"** so it says which checkpoint it belongs to without saying that checkpoint has landed.

**Anything issued in conversation that will later be cited must land in the repo when it is issued,** not afterwards. A citation to something that lives only in a chat transcript is a hole in the record.

**Prose.** Standard keyboard punctuation, no em dashes. State the mechanism rather than asserting a virtue: write "every number in the prose exists in the facts file", not "the reports are truthful". One word is banned outright across the corpus and in chat, and a grep enforces it, exempting only this sentence, which has to contain the string in order to name it: the banned string is `honest` and every form of it. The operator does not want it, and a claim of candour is exactly the kind of virtue-assertion this rule already rejects.

## Verification

Rules that exist before anything has gone wrong, taken from what has gone wrong elsewhere:

- Greps over markdown must be whitespace-tolerant, and markup-tolerant over the span they match. A phrase written with emphasis where the writer wanted emphasis defeats a pattern built on a literal space.
- A sweep expecting a non-zero count states that count in advance. "Returns nothing" is self-validating; "returns 17" is not.
- A test proving a check works must be permanent, not a break-and-revert done by hand once.
- An assertion must fail when the thing it guards is removed, and the proof of that is permanent. A source scan that finds a pattern is not evidence the behaviour exists; a behavioural test that exercises the path is. Where both are cheap, write both and let the scan report coverage while the test carries the claim.
- A figure states the population it was computed over, in the same breath, and a figure over a mixed population is not stated at all. Population is the rows, the filter and the source together.
- A claim that something is visible is a claim about a surface. Where a property is asserted to be stated, recorded on every row, or shown, the assertion names the surface a person reads it on and checks that surface.
- A green report is a statement about the build and never about the running system. Where a property is about the running system, being what the store holds or what the night produced, it is asserted by a guard the code carries so the fault refuses instead of passing, and by a figure a person reads on the morning it happens.
- A guard over a population states which population, and where a check has two paths, they are one loop or the split is the thing asserted.

**Two of these are specific to this tool and worth naming separately.** A verification figure computed over listed names only is a figure over the wrong population, because a listings row exists for every name. And a check that reads the live store is a check whose result depends on last night, which is a different instrument from the one this corpus builds; nothing in the harness reaches `data/`.

## Definition of done for a checkpoint

All eight, or it is not done:

1. The checkpoint's stated deliverable exists and runs.
2. `tools/ci.*` is green, with the test count recorded in PROGRESS. Until 0.4 builds those scripts, the checkpoint's own verification is run by hand and PROGRESS records the figures it produced and states that nothing guards them yet.
3. Every new store write is declared in SCHEMA and passes `writer-ownership`.
4. Any new numeric constant stated in a doc is pinned, and every decision name cited in new code or docs resolves.
5. The suite passes on both runners. Until 0.4 makes the matrix able to run, the suite is run on the machine at hand, PROGRESS names which platform that was, and the other runner is carried to 0.4.
6. A PROGRESS entry naming what was built, what was measured, and any carried obligation.
7. The checkpoint's expectations are added to the fixture, so `tools/verify-phase` covers it from now on, and at least one of them is derived independently rather than frozen from a run. A checkpoint that adds behaviour and no expectation has widened the unexamined set; one that adds only frozen expectations has added regression detection and called it verification. Where the fixture does not exist yet, expectations are carried to the checkpoint that first can, and the carried obligation is recorded in `BUILD_PLAN.md` when it is created rather than remembered.
8. The PROGRESS entry of condition 6 is written **before** the run that verifies the checkpoint, not after it, and the figures conditions 2 and 5 record are filled in from that run. The record is what the reconciliation reads: `HasLanded` decides out of scope by asking `PROGRESS.md` which checkpoints have landed, so a suite run against a tree whose entry is missing is a run against a corpus where this checkpoint has not landed, and every claim the entry is about to make due is still out of scope and cannot fail. That run is green on a question it never asked. Written the other way round it is the same run in the same order, and the only difference is whether the last thing changed is the one thing nothing after it re-reads.

Done conditions are written against **what the file will say after the edit**, not as statements of intent. A done condition narrower than its clause is the most common defect in this class of corpus.

**A checkpoint that amends its own done condition says so in its PROGRESS entry, in those words.** Amending one is legitimate and sometimes right. What is not legitimate is the amendment and the escape it authorises landing in the same commit with nothing outside that session's own prose marking it.

## Stopping rules

**Interrupt a phase only if a finding blocks a checkpoint from being built, or would put a silently wrong result into shipped code.** Everything else becomes a carried obligation and waits for sign-off. A reply that holds this rule is short, carries no fenced block, and ends by saying go build.

**A finding reopens a phase only if it fails a done condition or breaks a check.**

**Three passes each finding less is the signal to stop,** not to run a fourth. A report that repeats a previous report's defect has nothing new in it.

**Fresh session rule, narrowed on purpose:** a session that has committed **code** to this repository must not sign that code off. A session whose only commits are documents may. The protection being bought is that a session does not review its own code, and the wider wording costs a session per phase for nothing.

## Merge

**CI green before merge. That is the only condition.** Sign-off is a separate activity with its own record, owed on the phase as a whole before the next phase's plan, and it does not gate the merge. A phase held open waiting on something that is not code keeps a branch open, and the nightly job runs from that checkout for the whole of it.

**The condition binds from 0.4, which is where `tools/ci.*` first exists.** Before then the workflow fails on a missing script, which is phase 0 behaving as `BUILD_PLAN.md` describes it rather than a fault, and it does not block a merge. From 0.4 onward a red run blocks, with no exception and no override. This is written down because the rule above it was stated against a CI that exists, and the checkpoints that build the verification machinery come before it.

**A checkpoint lands as its own commit** and satisfies all eight done conditions on its own, and a session that has committed code still may not sign it off.

**Every change reaches `main` through a branch and a pull request, and none is committed to `main` directly.** That includes a document pass, a correction, a ruling and a sign-off. The branch is deleted after the merge and the working tree is returned to `main`, because the tree the nightly runs from is this repository's production checkout and a branch left checked out is a live hazard.

## Document lifecycle

Five specs and three records. A ninth document requires retiring one or writing down why not.

| Document | Kind | Rule |
|---|---|---|
| `CLAUDE.md` | spec | clean edits, prior text to CHANGELOG |
| `ARCHITECTURE.html` | spec | clean edits, citation at the point of change |
| `SCHEMA.md` | spec | clean edits. **The only place data ownership is declared** |
| `BUILD_PLAN.md` | spec | checkpoints and their done conditions |
| `RUNBOOK.md` | spec | clean edits |
| `DECISIONS.md` | record | grouped by topic, superseded entries move to "Previously decided" keeping their reasoning |
| `PROGRESS.md` | record | append only, corrections are new dated entries |
| `CHANGELOG.md` | record | prior text of every clean spec edit |

A corpus of the same shape grew past twenty documents on a previous project and the documentation tax stopped scaling with the size of the work. Eight is the cap, and the ninth costs a retirement.

**A screens document is not one of the eight.** Section 15 of `ARCHITECTURE.html` specifies what the operator sees. A mockup file and a built page are two answers to one question, and the day the two disagree nothing says which is the specification.

**`fixtures/README.md` is not one of the eight.** It describes a folder's shape, as `.gitignore` describes exclusions, and it carries no rule and no decision.
