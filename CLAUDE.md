# CLAUDE.md

Rules for any session working in this repository. Read this file first, every session, before touching anything.

---

## What this repo is

A nightly research tool over the S&P 500. It computes support and resistance levels for every name in the index each evening, decides which names are sitting at a price their own chart has made significant, and produces a full research report on any name the operator opens. It does not trade and holds no position.

**EquityBrief.** Solution, projects, namespaces and the root config section all use that name in full, with no abbreviation anywhere in code. A shortened form in one place and the full form in another is the kind of inconsistency that survives for years and then bites during a rename.

**.NET with C#, SQLite for the store.** One solution, one store file under the configured data root, no server to install on either machine.

The design source of truth is `docs/ARCHITECTURE.html`. It is the only place the system is described as a whole. If code and architecture disagree, that is a finding, not a licence to change either one silently.

## Where the build is right now

What the build has reached is recorded below rather than stated here.

**Which checkpoint the build is on is the furthest checkpoint `docs/PROGRESS.md` records,** and the one to build next is the checkpoint after it in `docs/BUILD_PLAN.md`. That is stated as a pointer rather than as a number, because a number here is a second place the same fact lives and it goes stale the moment a checkpoint lands.

Anything a checkpoint has not built yet does not exist, however completely `docs/ARCHITECTURE.html` describes it. The architecture describes the finished system; PROGRESS says what exists.

## Read order for a fresh session

1. This file.
2. `docs/BUILD_PLAN.md`, the checkpoint you are on and its done condition.
3. `docs/SCHEMA.md`, if you will touch a store.
4. `docs/ARCHITECTURE.html`, the sections covering the components in your checkpoint.
5. `docs/DECISIONS.md`, the entries cited by the above.

Do not read the whole corpus. It is small on purpose and it is still larger than any single checkpoint needs.

**The reference material sits in `.claude/rules/`, scoped by path.** Each file below opens with a `paths` front matter block, and a path-scoped rule loads when a session reads a file matching it and not before, so a session that needs one of these before opening anything under its paths has to know it exists and open it by hand.

| Rules file | Loads for | What it covers |
|---|---|---|
| `.claude/rules/checks.md` | `tools/**`, `src/EquityBrief.Tests/**` | the checks roster, every check that runs and what each asserts, and what a roster row, a floor and a declared reach mean |
| `.claude/rules/writing-tests.md` | `src/EquityBrief.Tests/**` | how an assertion is written: populations, floors, surfaces, mutation classes, and the two rules specific to this tool |
| `.claude/rules/corpus-edits.md` | `docs/**` | how the corpus is edited: named decisions, named obligations, deferrals, calendar time, and clean edits |
| `.claude/rules/scripts.md` | `tools/**` | what the scripts do and what a green report says: the wrapper contract, `tools/ci.*`, `tools/verify-phase` and the store it drops |

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
/artifacts        gitignored. the phase report and the suite result it reads, written by verify-phase
/prompts          gitignored. spent build prompts, kept locally
/data             gitignored. the store lives here, and nothing that verifies reaches it
/data-ci          gitignored. the store `tools/ci.*` creates and drops, which is not the one above
CLAUDE.md         these rules, read first every session
.claude/rules     four path-scoped rules files, tracked, holding this file's own
                  reference material where it loads for the session that needs it
source-lists.json the two open-web lists a research search may return, with their review date
EquityBrief.slnx  the six projects, at the root
global.json       pins the SDK to the 10.0.3xx feature band
.github/workflows/ci.yml   the macOS job and the Linux case-sensitivity job. Windows is
                  verified on the operator's machine rather than here.
                  Actions reads workflows from this path and no other
.gitattributes    line endings, normalised to LF in the repository
.gitignore        the store, the prompts archive, the harness output, the secrets
                  files and the local harness settings, less `.claude/rules`,
                  which is tracked
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

The PowerShell and shell versions are not translations of each other. `&&` is a parse error in Windows PowerShell, so the two files differ in syntax by necessity. `ci-parity` asserts they run the same steps in the same order, not that they contain the same text.

**Secrets.** `appsettings.Secrets.json` sits beside `appsettings.json` in each project that needs one. Gitignored, plaintext, never committed, and registered before environment variables so an environment variable still wins.

## Hard rules

Named, and cited by name. A violation is a defect regardless of what else is true.

**The nightly run makes no model call and no per-name network request.** Bars arrive in one bulk request and news in one feed request, so a night costs the same whether the universe is fifty names or five hundred. Any component that adds a per-name call to the nightly path is a defect, not a feature. Two carve-outs are named rather than left to be discovered, and neither grows with the index: a new member's one-year backfill, once per name ever, and the corporate action refetch, which asks for a year again for a name an action landed on, on each of the five nights after a failed check and once a week after that until a refetch succeeds or the name leaves the index. (see: The nightly run is arithmetic only) (see: Adjusted history is re-fetched after a corporate action) (see: A suspect name is asked for again on the five nights after it is marked and weekly after that, and its own page, its row on tonight's list and the run page say so until a refetch succeeds)

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

## Conventions

**A commit subject is `Phase {phase} / {checkpoint} - {what changed}`.** The checkpoint is never omitted, including on a commit that builds nothing: a ruling, a document pass, a correction and a sign-off addendum all belong to a checkpoint. Where work is done ahead of the checkpoint that owes it, the subject names that checkpoint rather than the one being worked on now.

**A commit belongs to the checkpoint that authorises the work, never to the phase whose subject matter the edited text happens to describe.** A wording repair to the screens section during phase 1 is phase 1 work. The clause above is about an obligation: a checkpoint owes something and the work discharging it arrives early. It is not about what the text is about, and reading it that way puts a later phase's number on a commit that phase did not authorise and does not advance. That is worse than an untidy log, because a checkpoint from an unbuilt phase reaching `PROGRESS.md` makes the reconciliation refuse every claim still owed at it: out of scope means a point that has not been reached, and `HasLanded` reads this record to decide.

**The pass that plans a phase belongs to the phase it plans, at that phase's opening checkpoint.** `Phase 2 / 2.0` for the pass that writes phase 2's section, not `Phase 1 / 1.8`. A PROGRESS entry for such a pass is headed with that checkpoint and the word planning, as `### 2.0 planning - ...`, and opens with **"Not a checkpoint entry"**, because it lands that checkpoint and never its phase: planning a phase builds none of it. An entry opening that way under any other heading, a ruling among them, lands nothing. Code such a pass or a ruling carries into shipped source meets the done conditions a checkpoint's code meets, and a correction to it is labelled for the checkpoint the pass belongs to (see: Code a pass that lands no checkpoint carries into shipped source meets the done conditions a checkpoint's code meets).

**Anything issued in conversation that will later be cited must land in the repo when it is issued,** not afterwards. A citation to something that lives only in a chat transcript is a hole in the record.

**Prose.** Standard keyboard punctuation, no em dashes. State the mechanism rather than asserting a virtue: write "every number in the prose exists in the facts file", not "the reports are truthful". One word is banned outright across the corpus and in chat, and a grep enforces it, exempting only this sentence, which has to contain the string in order to name it: the banned string is `honest` and every form of it. The operator does not want it, and a claim of candour is exactly the kind of virtue-assertion this rule already rejects.

## Definition of done for a checkpoint

All nine, or it is not done:

1. The checkpoint's stated deliverable exists and runs.
2. `tools/ci.*` is green, with the test count recorded in PROGRESS.
3. Every new store write is declared in SCHEMA and passes `writer-ownership`.
4. Any new numeric constant stated in a doc is pinned, and every decision name cited in new code or docs resolves.
5. The suite passes on both platforms: on macOS by the hosted runner, and on Windows by `tools/ci.ps1` on the operator's machine, which the PROGRESS entry records in the words `tools/ci.ps1` green, because no hosted runner checks Windows (see: Windows is verified on the operator's machine, and the hosted runners are macOS and Linux). The workflow carried a hosted Windows leg until the 7.2 ruling removed it, which is why this reads as one hosted runner and one machine rather than a matrix of two.
6. A PROGRESS entry naming what was built, what was measured, and any carried obligation.
7. The checkpoint's expectations are added to the fixture, so `tools/verify-phase` covers it from now on, and at least one of them is derived independently rather than frozen from a run. A checkpoint that adds behaviour and no expectation has widened the unexamined set; one that adds only frozen expectations has added regression detection and called it verification. Where the fixture does not exist yet, expectations are carried to the checkpoint that first can, and the carried obligation is recorded in `BUILD_PLAN.md` when it is created rather than remembered.
8. The PROGRESS entry of condition 6 is written **before** the run that verifies the checkpoint, not after it, and the figures conditions 2 and 5 record are filled in from that run. The record is what the reconciliation reads: `HasLanded` decides out of scope by asking `PROGRESS.md` which checkpoints have landed, so a suite run against a tree whose entry is missing is a run against a corpus where this checkpoint has not landed, and every claim the entry is about to make due is still out of scope and cannot fail. That run is green on a question it never asked. Written the other way round it is the same run in the same order, and the only difference is whether the last thing changed is the one thing nothing after it re-reads.
9. At least one assertion the checkpoint added is mutated and shown to go red, with the mutation and its result recorded in the PROGRESS entry. The mutation is chosen before the run by a stated rule, not after by which assertion looks weakest, and it is made in an isolated worktree and reverted. A checkpoint recording no mutation has not shown that anything it wrote can fail.

**Condition 9 is the cheapest verification in the project and three phases of evidence say so.** Every checkpoint that mutated its own work found its own holes, and the two that recorded no mutation are where a later session found them instead: 3.5 carries two of the three group one findings in the phase 3 sign-off and one of the two group two findings, and 3.5 is one of the two checkpoints with no mutation evidence in its entry. It raises the cost of every checkpoint, and it is the only form of verification here that has found a defect in the checkpoint performing it. It binds from 4.1.

Done conditions are written against **what the file will say after the edit**, not as statements of intent. A done condition narrower than its clause is the most common defect in this class of corpus.

**A checkpoint that amends its own done condition says so in its PROGRESS entry, in those words.** Amending one is legitimate and sometimes right. What is not legitimate is the amendment and the escape it authorises landing in the same commit with nothing outside that session's own prose marking it.

## Stopping rules

**Interrupt a phase only if a finding blocks a checkpoint from being built, or would put a silently wrong result into shipped code.** Everything else becomes a carried obligation and waits for sign-off. A reply that holds this rule is short, carries no fenced block, and ends by saying go build.

**A finding reopens a phase only if it fails a done condition or breaks a check.**

**Three passes each finding less is the signal to stop,** not to run a fourth. A report that repeats a previous report's defect has nothing new in it.

**Fresh session rule, narrowed on purpose:** a session that has committed **code** to this repository must not sign that code off. A session whose only commits are documents may. The protection being bought is that a session does not review its own code, and the wider wording costs a session per phase for nothing.

## Merge

**CI green before merge, and `tools/ci.ps1` green on the operator's machine over the tree being merged. Those are the only conditions.** Sign-off is a separate activity with its own record, owed on the phase as a whole before the next phase's plan, and it does not gate the merge. A phase held open waiting on something that is not code keeps a branch open, and the nightly job runs from that checkout for the whole of it.

**The condition binds from 0.4, which is where `tools/ci.*` first exists.** Before then the workflow fails on a missing script, which is phase 0 behaving as `BUILD_PLAN.md` describes it rather than a fault, and it does not block a merge. From 0.4 onward a red run blocks, with no exception and no override. This is written down because the rule above it was stated against a CI that exists, and the checkpoints that build the verification machinery come before it.

**A checkpoint lands as its own commit** and satisfies all nine done conditions on its own, and a session that has committed code still may not sign it off.

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

**`.claude/rules/` is not a ninth document.** It holds no decision, no design fact and no rule this corpus does not already state. It is the same rules, placed where they load for the session that needs them: a path-scoped file costs nothing to a session working elsewhere, which is what lets the reference material keep every word of its reasoning. The eight stand unchanged, and the four rules files are read by the corpus checks exactly as this file is, which is what stops them becoming a place a rule can hide. A rule appearing in both this file and a rules file is the defect the placement exists to avoid, because two copies of one rule drift and nothing says which is current, so a rule lives in exactly one of them.
