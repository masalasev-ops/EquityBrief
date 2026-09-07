# PROGRESS.md

The record of what was built, what was measured, and what is carried. Append only. A correction is a new dated entry naming what it corrects, never an edit to an earlier one.

**Which checkpoint the build is on is the furthest checkpoint this file records.** The one to build next is the checkpoint after it in `BUILD_PLAN.md`.

An entry that belongs to a checkpoint without saying that checkpoint has landed opens with **"Not a checkpoint entry"**. A phase-planning pass is the usual case.

## Entry format

```
### <checkpoint> - <what it built>            YYYY-MM-DD
Built:      what exists now that did not before
Measured:   figures, each naming the population it was computed over
Tests:      the count after tools/ci
Carried:    obligations created here, each naming a due point BUILD_PLAN also has
Notes:      anything a later session would otherwise have to rediscover
```

**Every figure states the population it was computed over, in the same breath.** A figure over a mixed population is not stated at all. A count over listed names only is a figure over the wrong population, because a listing row exists for every name.

**A checkpoint that amended its own done condition says so here, in those words.**

---

## Entries

### 0.0 - the repository, and eight defects the first corpus check found      2026-09-06
Built:      a git repository on `main`, the corpus committed unedited as its first commit, and
            branch `phase-0-repair` carrying the repairs. `ci.yml` moved from the root to
            `.github/workflows/ci.yml`, where Actions actually reads it. `global.json` and
            `src/Directory.Build.props` created. Checkpoint 0.0 added to `BUILD_PLAN.md`,
            `banned-prose` added to the Checks roster.
Measured:   over the 8 corpus documents, 13 decision citations, 0 unresolved and 0 resolving
            into "Previously decided". Over `DECISIONS.md`, 83 decision names, 7 of them
            previously decided, 0 duplicated, 0 carrying terminal punctuation. Over the 14
            tracked files, 0 unnamed by the layout block, 1 occurrence of the banned string
            and it is the exempt line, and 0 em dashes. Over `CHANGELOG.md`, 11 entries, 10
            naming a defect and 1 naming a decision, 0 naming neither. Over the 10 commits on
            this branch, 4 deleted a line from a spec and all 4 changed `CHANGELOG.md`.
Tests:      none. `tools/ci.*` does not exist until 0.4, so every figure above was swept by
            hand and none of it is guarded yet. A repeat of this sweep is what 0.4 automates.
Carried:    the citation placeholder in `BUILD_PLAN.md`'s carried obligations table, due at
            0.4, recorded as a row in that table.
Notes:      the workflow at the repository root was the sharpest of the eight. Actions reads
            workflows from `.github/workflows` and nowhere else, so the matrix job and the
            Linux case-sensitivity job had never run, which left done condition 5,
            `two-platform` and the instrument the Checks section claims for `path-casing` all
            unsatisfied with nothing reporting it. Neither is proved yet: no push has run the
            workflow, so 0.1 is the first checkpoint that will see either runner go green.
            `.claude/` is gitignored and untracked. It is per-machine harness state, not a
            corpus document, and the layout block does not name it.

### Correction to the 0.0 entry above - a ninth defect      2026-09-06
Corrects:   the entry above says eight defects and names the first corpus check as what found
            them. A ninth was found afterwards, by the first run of the workflow rather than by
            that check, and it is not in the count.
What:       the Merge section's "CI green before merge. That is the only condition." and done
            condition 2 are both written against a CI that exists, while `tools/ci.*` is built
            at 0.4. As stated, no checkpoint from 0.0 to 0.3 could merge or be declared done,
            so the corpus forbade the four checkpoints that build its own verification
            machinery. Both passages now name when they begin.
Measured:   the first workflow run, over 3 jobs on 3 runners, windows-latest, macos-latest and
            ubuntu-latest. All 3 ran, all 3 failed, and each failed on the missing `tools/ci.*`
            and on nothing else. `setup-dotnet` passed on all 3 before the failing step, which
            is what proves `global.json`'s 10.0.3xx band resolves on every runner and not only
            on this machine.
Notes:      this is the value of moving the workflow, arriving within the hour. Nothing about
            the merge rule was visible until a run existed to be red.

### 0.1 - the solution                                                       2026-09-06
Built:      `EquityBrief.sln` and the six projects the layout block names. `EquityBrief.Core`,
            `EquityBrief.Data` and `EquityBrief.Web` are libraries with no code yet;
            `EquityBrief.Api` is the one web application, mapping nothing, because the read
            surface is 1.5 and its done condition is that the API computes and fetches nothing;
            `EquityBrief.Worker` is a console command rather than a service, because scheduling
            lives outside the application. Three checks: `api-isolation`,
            `build-properties-central` and `pinned-constants`, the last covering the framework
            version and the SDK feature band, which 0.0 introduced into the specs and nothing
            guarded.
Measured:   over the 6 project files, 0 declare a target framework or a warning setting of
            their own, and `src/Directory.Build.props` declares the framework, nullable and
            warnings as errors. `EquityBrief.Api`'s compiled dependency file lists 4 libraries
            and `EquityBrief.Worker` is not among them; `EquityBrief.Tests`'s lists 6 and it is,
            which is what proves the check can fail rather than a string written to be caught.
            Over the 33 tracked files, 0 are unnamed by the layout block, the banned string
            appears once and it is the exempt line, and there are 0 em dashes. Over the 14
            decision citations in the 8 corpus documents and the 6 code and project files, 0
            fail to resolve apart from the placeholder already carried to 0.4. `dotnet build`
            over the solution reports 0 warnings and 0 errors.
Tests:      11, all passing, on Windows only. `tools/ci.*` does not exist until 0.4, so this
            was `dotnet test EquityBrief.sln` run by hand and nothing guards it yet. 5 of the 11
            exist to prove the other checks can fail: 1 for `api-isolation`, 1 for
            `build-properties-central`, and 3 for `pinned-constants` including one asserting
            that a version which cannot be read throws rather than defaulting.
Carried:    the suite unrun on macOS, due at 0.4. 0.1's checks unseen by the harness, due at
            0.7. The phase report named twice, due at 0.5. All three are rows in
            `BUILD_PLAN.md`'s carried obligations table.
Amended:    this checkpoint amends its own done condition. Done condition 5, the suite passing
            on both runners, is asserted by the matrix, the matrix calls `tools/ci.*`, and those
            arrive at 0.4, so no checkpoint from 0.1 to 0.3 could satisfy it. It now names 0.4
            as where it begins, which is the repair 0.0 made to the Merge section and to done
            condition 2 and missed here. The amendment and the checkpoint it lets through are in
            the same pull request, which is the thing this line exists to mark.
Notes:      the .NET 10 SDK writes `.slnx` by default and the corpus names `EquityBrief.sln`, so
            the solution was created with `--format sln` rather than the corpus amended to suit
            a tool default. `EquityBrief.Api` answers 404 from a host with no endpoints mapped,
            which is what was taken as evidence it runs. The component catalogue in
            `ARCHITECTURE.html` has the verification harness write `verify.html` and
            `verify.json` while `CLAUDE.md` and `BUILD_PLAN.md` say `artifacts/phase-report.*`;
            that is the third carried row and it is a corpus defect, not a 0.1 one.

### Correction to the 0.1 entry above - the solution file format      2026-09-06
Corrects:   the Notes line saying the solution was created with `--format sln` rather than the
            corpus amended to suit a tool default. The operator ruled the other way. The
            solution is `EquityBrief.slnx`, the corpus moved, and the reasoning is now a
            decision rather than a note in a record.
What:       `EquityBrief.sln` migrated to `EquityBrief.slnx` with `dotnet sln migrate`, the two
            passages in `CLAUDE.md` that name it amended, and `EquityBrief.Tests` pointed at the
            new name in the two places it reads it. `DECISIONS.md` carries the new entry, The
            solution file takes the SDK's current format, in Process.
Measured:   over the solution, `dotnet build` reports 0 warnings and 0 errors and the suite is
            11 passing, unchanged from the 0.1 entry, on Windows only. The 2 references to the
            old name that remain in this file and the 2 in `CHANGELOG.md` are prior text in
            records and are left as they were written.
Notes:      the earlier note is not struck through and the 0.1 entry is not edited. This is what
            correcting a record looks like.

### 0.2 - the store and the migration runner                                 2026-09-07
Built:      the SQLite store under the configured data root, a migration runner, and the first
            migration creating `run_log` and no other table. `tools/migrate` with its `.ps1`
            wrapper, and a `migrate` verb on `EquityBrief.Worker`, which is the executable the
            corpus already has rather than a seventh project. Three checks: `schema-columns`,
            new on the roster, and `price-storage-form` and `store-portability`, both already on
            it and neither implemented until now.
Measured:   `tools/migrate` applied 1 migration against an empty directory and 0 against the
            applied store, exit 0 each time, from both the PowerShell wrapper and the bash
            script. Over the 10 columns `SCHEMA.md` declares for `run_log`, all 10 are present
            in the created table, in that order, with the declared storage types. Over the 1
            migration, 10 column declarations of which 1 is a money column, 0 declared `REAL`.
            Over the migrated store, 1 table and 0 rows and 0 absolute paths, which is context
            and not a pass; over a store the suite populates with 3 rows, 2 absolute paths are
            found, and that is the assertion carrying the property.
Tests:      27, up from 11, all passing, on Windows only. `tools/ci.*` does not exist until 0.4.
Carried:    `tools/migrate.ps1` written without its proofs, due at 0.4 and a row in
            `BUILD_PLAN.md`'s table. The three carried at 0.1 stand.
Notes:      `STRICT` does not enforce the money rule. The first version of this checkpoint
            claimed it did and a test written to prove it failed: SQLite renders a double as
            text and stores it, because that conversion is lossless. The claim was wrong, not
            the test. `SCHEMA.md` now states the limit and the suite pins the coercion, so the
            money rule rests where it always did, on `price-storage-form` reading the migration
            text and on prices being `decimal` in code.

            The applied version is SQLite's `user_version` pragma rather than a table, because
            0.2 is told to create `run_log` and no other, and a version a table holds is a table
            `SCHEMA.md` would have to declare and own.

            The migration runner is in the component catalogue, and its row in the read and
            write matrix is blank in every store column. That is the claim, not an omission: it
            writes schema and touches no store's rows. It cannot write `run_log` yet in any
            case, because a run row needs UTC instants and the clock is 0.3.

            `data/` is gitignored, so the store this session created exists on this machine
            only, which is what the rule that nothing in the harness reaches `data/` requires.

### 0.3 - the clock                                                          2026-09-07
Built:      `IClock` with `SystemClock` and `FixedClock` behind it, resolving session zones from
            IANA identifiers only and refusing a Windows one. The date derivations are written
            once on the interface, so a fixed clock in a test exercises the same code a night
            takes rather than a second implementation. `clock-usage` implemented, which was on
            the roster from the start and had nothing behind it until now.
Measured:   over the 29 source files in `src`, 1 read of the machine clock and it is in
            `SystemClock.cs`, which is the file allowed to have it. The reader is shown finding
            a read, finding a local-time expression, and passing over code that goes through the
            clock, so it is not a reader that flags everything or nothing. Over the 7 build
            files, 0 set `InvariantGlobalization` to true, and the running process reports the
            invariant switch off. The session zone's offset is -5 hours in January and -4 in
            July, read from the resolved zone, which is the assertion a missing timezone
            database would fail.
Tests:      41, up from 27, all passing, on Windows only. `tools/ci.*` does not exist until 0.4.
Carried:    nothing new. The four carried at 0.1 and 0.2 stand, one of them reworded at this
            checkpoint to name the property rather than 0.1's three checks.
Notes:      the clock is not added to the component catalogue. That table's contract is what a
            component reads and writes, store by store, and its rule is that a component
            touching a store not listed fails its row. The clock touches no store, so it has no
            row to fill and no claim the harness could assert there. This is a judgment 0.5 can
            reverse when the harness reads the catalogue for real.

            The session zone is a constant in `EquityBrief.Core` rather than configuration. It
            is a fact about the S&P 500 and not about the machine, and no document states the
            identifier, so there is no constant to pin and no dead configuration key to carry.

            The migration runner still writes no `run_log` row, so its blank line in the read
            and write matrix stands. The clock existing removes the reason it could not, which
            is a change of options rather than a change of behaviour, and the matrix says what
            the code does.

### 0.4 - the CI scripts                                                     2026-09-07
Built:      `tools/ci.ps1` and `tools/ci.sh`, two implementations rather than a script and a
            wrapper, because Windows PowerShell cannot parse `&&` and the two differ in syntax
            by necessity. `tools/run-bash.ps1`, the one place a PowerShell entry point finds a
            bash and hands over, so `tools/migrate.ps1` is two lines and every later wrapper
            will be. `tools/wrapper-probe` and its `.ps1`, permanent because a break and revert
            done by hand once proves nothing after the day it was done. `ci-parity`
            implemented.
Measured:   6 steps in each CI script, the same names in the same order. Both scripts green end
            to end on Windows, 41 tests passing inside them at the time, migrating an empty
            directory and then a migrated one. Each script, copied somewhere with no solution
            beside it, exits non-zero and names `restore` as the step that failed, which is the
            behavioural half of `ci-parity` and needs nothing broken in the repository to show
            it. The wrapper returns exit code 3 and both of the probe's streams. With a path
            holding no bash, the wrapper exits non-zero and names bash, rather than the zero
            that would make a gate that never ran look like one that passed. Over the 3 bash
            entry points in `tools`, 0 lack a `.ps1` beside them.
Tests:      49, up from 41, on Windows. The two runners are what this checkpoint makes possible
            and the next push is the first time either has run the suite.
Carried:    the wrapper proofs carried from 0.2 are discharged here. The macOS runner, carried
            from 0.1 and due here, is discharged only when the matrix goes green.
Notes:      one run of the suite failed once, in `ci-parity`'s failing-step test, and did not
            fail again in eight further runs including two that rebuilt first. I could not
            reproduce it and therefore could not diagnose it. Rather than call it nothing, both
            assertions in that test now carry the whole transcript and the exit code in their
            failure message, so a second occurrence arrives with its own diagnosis instead of
            just a mismatch. This is recorded because an unreproducible failure that is written
            down is a different thing from one that is not.

### Addendum to 0.4 - the matrix ran                                         2026-09-07
Measured:   the first CI run in which the scripts existed. 3 jobs, windows-latest,
            macos-latest and ubuntu-latest, all green. All 6 steps ran on each, and each
            reported 49 tests passing, 0 failing, 0 skipped. `ci: green` on all three.
What it     the macOS runner, carried from 0.1, is discharged: the suite has now run there.
discharges: `two-platform` is a real claim rather than a contract for the first time. The Linux
            job, which exists as an instrument for one class of fault, opened every file the
            pipeline touches on a case-sensitive filesystem and found none miscased, so
            `path-casing` has an instrument behind it even though the check itself is not yet
            written.
Notes:      the wrapper assertions are carried by the Windows runner. On a machine with no
            PowerShell they assert only the split, that such a machine is not Windows, and the
            two GitHub runners both carry pwsh so all three ran them for real. An operator's
            Mac without pwsh would assert the split alone, which is the correct population to
            state rather than claiming the wrapper is proved everywhere.

### 0.5 - the harness reading the architecture                               2026-09-07
Built:      `tools/verify-phase` with its wrapper, and the harness behind it: a reader for
            `ARCHITECTURE.html`'s tables, a placement for every table in the document, a claim
            per row of every claim-bearing table, and `artifacts/phase-report.html` for the
            operator with `artifacts/phase-report.json` beside it. `architecture-conformance`
            implemented.
Measured:   23 tables in the document, all 23 placed. 11 are claim sources, which is the scope
            the catalogue states for itself, sections 7, 15, 16 and 18. The other 12 are placed
            with the reason they make no claims and, where one exists, the instrument that
            covers them instead. 120 claims, of which 0 pass, 0 fail, 0 are out of scope and
            120 are unexamined, which is the correct first result and the reason
            `tools/verify-phase` exits 1 from both entry points.
Tests:      55, up from 49, on Windows before the push.
Carried:    the phase report named twice, carried from 0.1 and due here, is discharged: the
            catalogue and the phase table in `ARCHITECTURE.html` now say
            `artifacts/phase-report.html` and `.json`, which is what the other two specs said
            all along. One new obligation replaces it, below.
Notes:      the harness lives in `EquityBrief.Tests` and the suite carries its own entry point,
            so `dotnet test` runs the checks and `dotnet run` writes the report from one
            assembly. This keeps the project count at six. It also puts the harness where the
            checks already are, which is what it has to read.

            `tools/verify-phase` is not a CI step and is not called by `tools/ci.*`. It is green
            only when nothing is unexamined, which is a gate on a phase rather than on a commit,
            and making it a CI step would stop every checkpoint in a phase merging until the
            phase was finished. `architecture-conformance` is the part that does run every CI
            run: it asserts every table is placed and every claim carries a verdict, not that
            the verdicts are good enough to sign anything off.

            A claim that reads PASS has to name the check that reached it. Nothing passes yet,
            so that assertion is vacuous today and the test says so rather than letting a zero
            read as a result.

            The finding this checkpoint produced: the catalogue names sections 7, 14, 15, 16 and
            18 as the claim scope, and section 14 carries no table at all, so a harness that
            reads tables can take no claims from the nightly run. Recorded as a carried
            obligation due at 0.7.

### 0.6 - the fixture folder                                                 2026-09-07
Built:      `fixtures/manifest.schema.json`, which declares what a manifest carries, and a
            checker that reads its required field names out of that file rather than restating
            them. The harness now reports the fixture's state, and `fixtures/README.md` says
            where the shape is declared. No inputs and no expectations, which is what 0.6 asks
            for.
Measured:   4 required fields at the top level and 4 on every captured input, read from the
            schema and asserted against it. 8 rejections asserted, each a separate rule: a
            missing required field, a date that is not a date, an instant that is not UTC, a
            credential in a query, an absolute file path, a manifest asserting it carries
            credentials, a fixture with no inputs, and a file that is not JSON. A valid manifest
            has 0 faults, which is what stops those 8 passing over a checker that rejects
            everything. The harness reports the fixture ABSENT with 0 captured.
Tests:      67, up from 55, on Windows before the push.
Carried:    nothing new. Section 14 having no table, carried from 0.5, is still due at 0.7.
Notes:      the fixture is reported as absent and never as passing, and the note the report
            carries says so in those words. Absent is out of scope until 1.8, which is where
            `BUILD_PLAN.md` puts the first expectations, and out of scope is shown beside
            unexamined rather than added to it.

            One test walks every captured fixture and checks its manifest. It is vacuous today,
            over zero fixtures, and the test above it states that by asserting the count is
            zero. It is written now so the first fixture that lands is checked on the day it
            lands rather than on the day somebody remembers to write the check.

            A credential in a captured query would be published, because a fixture is committed.
            The manifest asserts there is none and the checker scans for seven markers, which is
            the pattern of asserting a thing and then testing the assertion rather than trusting
            it.

### 0.7 - the phase 0 report                                                 2026-09-07
Built:      the ten roster checks that had nothing behind them: `decision-resolves`,
            `no-superseded-citation`, `changelog-reconciles`, `stated-counts`, `banned-prose`,
            `path-casing`, `bar-append-only`, `writer-ownership`, `two-platform` and
            `coverage-reported`. A scope for every claim in the architecture, a reader for
            section 14's ordered list, and a coverage record in the phase report naming every
            check the roster carries and what implements it.
Measured:   129 claims. 6 pass, 0 fail, 123 out of scope, 0 unexamined, and
            `tools/verify-phase` exits 0. Every one of the 6 that passes names the check that
            reached it; every one of the 123 out of scope names the checkpoint or phase that
            ends it. 24 placements, being the 23 tables and section 14's list. 24 roster rows,
            19 carried by an implementation and 5 naming a checkpoint that has not landed.
Tests:      98, up from 67. `tools/ci.sh` green end to end locally before the push.
Carried:    all four obligations that were due in phase 0 are discharged. Four remain, created
            by the architecture and by 1.7, none due before 1.3.
Notes:      three findings, each fixed by widening an instrument rather than narrowing a
            document. Section 14 is named as a claim source and carries an ordered list, so the
            harness reads lists as well as tables. `coverage-reported` was written to require a
            checkpoint row name a checkpoint the plan has, while the plan states that later
            phases get checkpoint detail at the previous phase's sign-off, so four of the five
            rows named checkpoints that cannot exist yet and the rule now asks for the phase.
            `architecture-conformance` said "every claim a table makes" and now says what the
            catalogue always said it covered.

            The citation placeholder obligation was due at 0.4 and was discharged here, three
            checkpoints late. Nothing was blocked by it, because the check it concerned was not
            written until 0.7, but the due point was wrong rather than the work.

            Two checks are narrower than their roster row reads and the difference is stated
            here rather than left to be discovered. `writer-ownership` asserts that every write
            in the shipped source is declared; the other direction, that every declared writer
            exists, cannot hold until the components are built, so the declared writers are
            counted and not asserted. `two-platform` asserts that the workflow still declares
            both runners and hands each the CI script, because whether the suite passed on both
            is CI's own result and cannot be asserted from inside one run.

### Phase 0 - a skeleton that can already fail                               2026-09-07
Built:      eight checkpoints, 0.0 to 0.7. A git repository and the corpus committed unedited.
            Six projects building clean under warnings as errors. A SQLite store, a migration
            runner and `run_log`. A clock resolving session dates through IANA identifiers. Two
            CI implementations and one wrapper mechanism. A harness that reads the architecture
            and writes the phase report. A fixture folder with its manifest schema. Nineteen
            checks.
Measured:   98 tests. CI green on windows-latest, macos-latest and ubuntu-latest. The phase
            report green: 129 claims, 6 pass, 0 fail, 123 out of scope, 0 unexamined, and no
            claim passing without naming the check that reached it. The fixture is reported
            ABSENT with 0 captured, which is what 0.6 built and never a pass.
Notes:      the phase found nine defects in the corpus it was building from, and every one was
            repaired by a named edit with its prior text in `CHANGELOG.md`. Four of them were
            rules that could not be satisfied as written: the merge condition, done condition 2,
            done condition 5, and `coverage-reported`'s checkpoint clause. Each was written
            against a system that already existed, and phase 0 is the phase that builds it.

            Two claims a reader should not take from this entry. Green is a statement about the
            build and never about a running system: nothing has fetched a bar, and the store
            this repository can create is empty. And the phase report is green because 123 of
            its 129 claims are out of scope, which is the correct answer for a phase that built
            no pipeline, not evidence that the architecture has been verified.

            **Not a sign-off.** This session committed code, so under the fresh session rule it
            must not sign that code off. Phase 0's sign-off is owed on the phase as a whole,
            before phase 1's plan, by a session that has not written any of it.

### Addendum to 0.7 - the check that could not see its population      2026-09-07
What:       `changelog-reconciles` passed locally and failed on all three runners on its first
            push. `actions/checkout` clones shallow by default, so `git log` returned 1 commit
            where the working machine has 20, and the check reads the history.
Measured:   1 commit visible on the runners against a floor of 5. The workflow now fetches the
            full history and all 3 jobs are green.
Notes:      the check was right and the workflow was wrong. This is the case the corpus argues
            about at length: a check whose population is invisible must fail rather than assert
            over what it can see, because the version that quietly asserts over 1 commit passes
            forever and reports a scope it never had. The floor stated in advance is what turned
            an invisible narrowing into a red run, and it is the reason this was found on the
            first push rather than at some later sign-off.

### Review of phase 0 - fourteen findings                                    2026-09-07
Not a checkpoint entry. It belongs to 0.7, which has landed. This is the independent read the
            phase 0 entry says is owed, recorded at the time it was issued because seven of
            the fourteen become carried obligations, and an obligation citing a conversation
            rather than a document is a hole in the record. The reviewing session had
            committed no code when it found these and has committed none since.
Read:       the 8 corpus documents, the 9 shipped source files, the 9 scripts in `tools`, the
            workflow, and the 43 files in `EquityBrief.Tests`. Over that population the suite
            ran at 98 passing and `tools/verify-phase` at 129 claims, 6 pass, 0 fail, 123 out
            of scope, 0 unexamined, which reproduces what the 0.7 entry records.
Bearing on  three of the fourteen, being 1, 2 and 3 below. Each concerns the accuracy of the
sign-off:   report a sign-off reads, and 2 and 3 both make green mean less than it claims.
            The other eleven are ordinary defects that hold no phase open.

            1. The phase report never names the check that reached a PASS.
            `Claim.By` is populated at `Harness/Scope.cs:114-132` and asserted non-empty by
            `NoClaimPassesByFiat`, and neither output renders it:
            `Harness/PhaseReportWriter.cs:60-66` projects the JSON claim as table, subject,
            verdict and note, and the HTML claims table at `PhaseReportWriter.cs:137` carries
            the same four columns. Confirmed against the generated artifact. The roster says a
            passing claim names the check that reached it, which is a claim about a surface,
            and the corpus rule is that such a claim is checked on the surface a person reads.
            Asserting the model is what let this hold in the model and be absent from both
            surfaces.

            2. Section 17's thirty rows are placed as covered by an instrument that never
            opens the file. `Harness/PhaseReport.cs:67` places section 17, on limits, spend
            and the numbers the harness asserts, as asserted by `pinned-constants`.
            `Checks/PinnedConstants.cs` reads CLAUDE.md, BUILD_PLAN.md, global.json,
            `src/Directory.Build.props` and the workflow, never `ARCHITECTURE.html`, and
            covers two constants, the framework version and the SDK feature band. Section 17
            says of itself that each row is a claim about the code and that the harness parses
            the table. Thirty claims are removed from the count by a placement naming a check
            that does not reach them, which understates the set green is defined against.

            3. Two of the six passing claims are reached by a check that does not cover them.
            `Scope.For` at `Harness/Scope.cs:110-142` keys on subject alone for all but one
            case, so a catalogue verdict is reused verbatim in the read and write matrix. In
            that matrix `Migration runner` at `docs/ARCHITECTURE.html:910` is twelve blank
            cells, and the matrix's own contract is that a blank cell is asserted as much as a
            filled one; `schema-columns` asserts the columns and types of `run_log` and says
            nothing about whether the runner writes store rows. `Verification harness` at
            `docs/ARCHITECTURE.html:934` claims R against all eleven stores, which contradicts
            the rule in CLAUDE.md that nothing in the harness reaches `data/`, and it passes
            on the note that this report is the thing the claim describes. Two documents in
            the corpus disagree and the harness reports PASS on one side of the disagreement.

            4. `tools/run-bash.ps1` loses both its exit code and the failing step's name when
            called from `tools/ci.ps1`. `ci.ps1:7` sets the error action preference to Stop,
            which propagates into the called script, so `Write-Error` at `run-bash.ps1:20`
            becomes a terminating error and `exit 127` is never reached. Measured by running
            it: the run dies at exit 1 with the message and with no failed-at-step line. The
            safety property survives and the documented 127 does not.
            `AWrapperOnAMachineWithNoBashExitsWithANamedMessage` cannot see this, because it
            invokes `migrate.ps1` directly under the default preference.

            5. `TheDeclaredWritersThatDoNotExistYetAreCounted` at
            `Checks/StoreWrites.cs:103-109` cannot fail. `built` is a filter of `owners`, so
            asserting that every element of `built` is contained in `owners` is true by
            construction. Only the floor above it asserts anything. A permanently passing test
            reports coverage it does not have.

            6. `AbsolutePaths.LooksAbsolute` at `Checks/AbsolutePaths.cs:22-35` inspects only
            the first two characters, so an absolute path anywhere but the start of a value is
            not seen. `run_log.detail` is where exception text will land and exception text
            carries absolute paths mid-string.

            7. `FixtureManifest.IsUtcInstant` at `Harness/FixtureManifest.cs:129-135` resolves
            a zoneless instant against the machine's own zone, so the same manifest passes on
            a UTC runner and fails on the operator's machine. It is also an implicit read of
            the machine zone that `clock-usage` does not match. The existing test uses an
            explicit offset, so it does not cover the case.

            8. The manifest checker scans `input.query` only, at
            `Harness/FixtureManifest.cs:104-115`. It never opens the captured response and
            never checks that the file named by `input.file` exists, while
            `fixtures/manifest.schema.json` and `fixtures/README.md` both say no credential
            appears in a captured response and that the check scans as well.

            9. `ChangelogReconciles` at `Checks/ChangelogReconciles.cs:36` does not check the
            exit code of `git show`. A failed invocation returns empty output, the commit
            reads as one that deleted nothing, and the run stays green. Same shape as the
            shallow clone fault the addendum above records, which the floor caught only
            because it was total.

            10. The money column list of `price-storage-form` at
            `Checks/PriceStorageForm.cs:13-17` is hand maintained and nothing reconciles it
            against `SCHEMA.md`, which `StoreSchema.Declared` already parses. A money column
            added under a new name is unchecked and nothing says so.

            11. `Shell.Run` at `Shell.cs:70-72` reads standard output to completion before
            reading standard error, so a child that fills the error pipe's buffer deadlocks
            both. Latent at the output sizes the suite's children produce today.

            12. `banned-prose` enforces its rule over an unstated subset and has done so from
            the day it was written at 0.0. `Checks/BannedProse.cs:22` scans the 8 corpus
            documents, the source and project files and the scripts in `tools`, and therefore
            not the workflow, `src/Directory.Build.props`, `EquityBrief.slnx` or the two files
            in `fixtures`. Over those 9 unscanned files, 0 carry the banned string and 0 carry
            an em dash today, so this is a check narrower than it reads rather than a live
            fault. That is the survivorship shape the Checks section argues about: a check
            that silently narrows its own scope keeps passing. It also carries no negative
            proof over a file, only over a string in memory.

            13. `SessionZones.Resolve` at `src/EquityBrief.Core/Time/SessionZones.cs:11-36`
            refuses any identifier carrying no separator. That is right for an exchange
            session zone, which is every zone this system resolves, and wrong for the general
            case the method name and its message claim, because UTC and several other IANA
            identifiers carry no separator. The repair is to narrow the claim to what the code
            does rather than to widen the code for a case nothing asks for.

            14. `Microsoft.Extensions.Configuration.Binder` is referenced by
            `src/EquityBrief.Worker/EquityBrief.Worker.csproj` and nothing binds. An unused
            reference is a dependency somebody later cites as one.
On 0.4:     no supported explanation was found for the unreproducible `ci-parity` failure the
            0.4 entry records. The hypothesis tested was that Windows PowerShell turns a
            native command's error stream into a terminating error under Stop when the streams
            are redirected. It was run twice on a machine carrying Windows PowerShell and no
            `pwsh`, once through `ci.ps1` itself and once in isolation, and did not fire
            either time. Finding 4 is a candidate mechanism rather than a diagnosis: it
            produces the same visible signature, a run that dies at exit 1 before its step
            name prints, by a different route. Whether that route was reachable in the failing
            run is not established, and a fault that stops recurring after an unrelated fix is
            not a diagnosed one.
Notes:      this entry is the whole of what the reviewing session committed. The repairs are a
            separate session's work, which is what keeps the fresh session rule satisfied at
            sign-off.
