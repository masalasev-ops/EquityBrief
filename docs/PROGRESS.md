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

### Repairs to phase 0, from the fourteen findings                           2026-09-07
Not a checkpoint entry. It belongs to 0.7, which has landed. This is the repair pass for the
            entry above, and it is a separate session from the one that found them. This
            session committed code and therefore may not sign any of it off.
Built:      a reach declaration on every check a placement or a verdict names, carried in the
            check itself rather than in a list beside it, and a reconciliation in the harness
            that reads those declarations in both directions and stops on either failure. A
            placement or a PASS naming a check whose declared reach does not include it stops
            the report; so does a check declaring reach over a subject nothing sends it; so
            does a placement whose table is claimed whole by a check that does not open
            `ARCHITECTURE.html`. Section 17 became a claim source. `Scope.For` keys on the
            table and the subject together. The fiat guard now refuses a PASS naming no roster
            check and a PASS whose note offers the report as its own evidence. Both outputs
            render the check behind every verdict, the instrument or due point behind every
            placement, and the reconciled count against its floor. `banned-prose` reads every
            text file git tracks. Six carried obligations recorded in `BUILD_PLAN.md`.
Fixed:      all fourteen findings. 1, the report renders `Claim.By` in the JSON and in the
            HTML and an assertion parses the written files rather than the model. 2 and 3, the
            reconciliation above, with section 17 and the two reused verdicts repaired. 4,
            `tools/run-bash.ps1` writes to `[Console]::Error` instead of `Write-Error`. 5, the
            containment assertion in `StoreWrites` is replaced by one that can fail. 9,
            `ChangelogReconciles` checks the exit code of `git show`. 12, `banned-prose`
            widened with a negative proof over planted files. 13, `SessionZones.Resolve` is
            renamed `ResolveSessionZone` and says it resolves an exchange session zone and
            refuses a slashless identifier deliberately. 14, the unused configuration binder
            reference is gone from `EquityBrief.Worker`. 6, 7, 8, 10 and 11 are carried, with
            the runtime money-precision guard, and each is a row in `BUILD_PLAN.md`'s table.
Section 17: the choice was to make it a claim source rather than to widen `pinned-constants`
            to parse it. Widening was not available: twenty-four of its twenty-nine rows are
            asserted by a component that does not exist, and the row's own "Asserted by"
            column names things like a run log duration, a fixture ladder diff and an app test
            against a fixture night of forty. A check written to claim those today would have
            been the same defect in a wider instrument. As a claim source the twenty-nine rows
            are counted, each naming the checkpoint or phase that ends it, which is what the
            set green is defined against was missing.
Expected:   stated before the run. 158 claims, 4 pass, 0 fail, 154 out of scope, 0 unexamined,
            10 reconciled. The out-of-scope figure was expected to rise by 31 and not to fall:
            29 from section 17, which was placed as covered and is not, and 2 from the read
            and write matrix rows whose verdict was borrowed from the catalogue. Removing a
            false placement moves claims into the count green is defined against, and at this
            phase every one of them is out of scope, so out of scope was always going to grow.
            Unexamined was expected to stay at 0, because every new claim has a due point.
Measured:   before, over `docs/ARCHITECTURE.html` as it stood: 129 claims, 6 pass, 0 fail, 123
            out of scope, 0 unexamined, 24 tables placed, 24 roster rows with 19 carried, 98
            tests. After, over the same document with the two rows corrected: 158 claims, 4
            pass, 0 fail, 154 out of scope, 0 unexamined, 24 tables placed of which 13 are
            claim sources and 11 make no claims, 24 roster rows with 19 carried. Every
            expected figure was met. Over the 24 placements and verdicts, 10 name an
            instrument or a due point and all 10 reconcile: 1 placement names a check, 5 name
            a due point, and 4 claims pass. The floor is 8, stated in advance, because a
            reconciliation over zero placements passes silently. Over the 4 passing claims, 4
            name a check on the roster whose declared reach includes them and 0 rest on a note
            about the report. Over the 154 out of scope, 0 name a due point `BUILD_PLAN.md`
            lacks and 0 name one `PROGRESS.md` records as landed. Over the 90 files git
            tracks, 90 are text, 0 carry an em dash, and 1 carries the banned string and it is
            the exempt line.
Tests:      113, up from 98, on Windows. `tools/ci.ps1` green end to end, all 6 steps, with
            113 passing inside it. `tools/verify-phase` green.
Carried:    six new obligations, all rows in `BUILD_PLAN.md`'s carried obligations table:
            `AbsolutePaths.LooksAbsolute` reading only the first two characters, due 1.3;
            `FixtureManifest.IsUtcInstant` resolving a zoneless instant against the machine
            zone, due 1.3; the manifest checker scanning `input.query` only, due 1.3; the hand
            maintained money column list of `price-storage-form`, due 1.2; `Shell.Run` reading
            standard output to completion before standard error, due 1.2; and the runtime
            money-precision property having no guard, due 1.2. The four created by the
            architecture and by 1.7 stand.
Due points: the `IsUtcInstant` obligation was raised against "0.6's first real fixture", and
            0.6 has landed, so that is not a due point. It is re-pointed to 1.3, and the
            manifest checker obligation is re-pointed to 1.3 from the 2.1 it was given, on the
            same reasoning. 1.3's own done condition is that the gap fixture is refused with
            the gap's date named, which is the first committed fixture input and therefore the
            first manifest. 1.8 is where `BUILD_PLAN.md` puts phase 1's expectations, which is
            a later thing than an input, and `fixture-replay` runs from 2.1 over expectations
            that exist. A manifest checker owed at 2.1 would have gone unfixed across every
            manifest written from 1.3 onward. The placement of section 19.1 in the harness
            names 1.3 for the same reason.
On 0.4:     the reproduction was attempted again after finding 4 was fixed, and the failure
            did not recur. Over 20 runs of `tools/ci.ps1` in a directory with no solution
            beside it, 12 direct and 8 through `ci-parity`'s own failing-step test, 20 exited
            non-zero and 20 printed "failed at step: restore". 0 produced the signature the
            0.4 entry records. Finding 4 stays a candidate mechanism and is not a diagnosis:
            it produces the same visible signature, a run that dies before its step name
            prints, but by a route that runs at the migrate step and not at the restore step
            where the original failed. Whether any route of that shape was reachable in the
            failing run is still not established. A fault that stops recurring after an
            unrelated fix is not a diagnosed one, so the 0.4 note is downgraded to a candidate
            rather than closed.
Notes:      `banned-prose` enforced its rule over an unstated subset from the day it was
            written at 0.0. It read the 8 corpus documents, the source and project files and
            the scripts in `tools`, and therefore not the workflow, `src/Directory.Build.props`,
            `EquityBrief.slnx` or the two files in `fixtures`. Nothing in those 9 files carried
            either pattern, so it was a check narrower than it read rather than a live fault,
            which is exactly the survivorship shape the Checks section argues about: the
            broken checks that survive are the ones that under-report. It now reads what git
            tracks, so the exclusions live in `.gitignore` and not in a second list.

            The reach declarations split into two scopes on purpose. Subjects carries the
            property and is what the reconciliation counts. Reads is context, has no floor,
            and is given force by one rule rather than by being counted: a check named as
            covering a whole table has to read `ARCHITECTURE.html`, because the rows it would
            be covering are rows nobody enumerated. A check may still reach a single row
            without reading the document, which is how `schema-columns` reaches the migration
            runner: what it asserts there is the store against `SCHEMA.md`.

            Three placements were named by an instrument that could not reach them and only
            one was in the findings. 19.1 was placed as asserted by the fixture manifest from
            0.6, a checkpoint that has landed, and is now owed at 1.3. 19.2 was placed as
            asserted by `coverage-reported`, which reads the roster and the CI scripts and
            never the architecture, and is now owed at 6.1. 19.3 is placed against
            `architecture-conformance`, which now genuinely reaches it: the three verdict
            names in that table are asserted to be the three the writer emits, and both files
            it describes are written and read back.

            `Get-Command bash` on this machine resolves to `C:\Windows\System32\bash.exe`,
            which is the Windows Subsystem for Linux stub, and there is no distribution
            installed, so every `.ps1` wrapper exits 1 with a message about WSL. The runs
            recorded above put Git for Windows ahead of it on PATH. This is a fact about the
            machine and not about the repository, and it is written down because the next
            session to run `tools/ci.ps1` here will meet it.

### Addendum to the repair pass above - two roster rows                      2026-09-07
Adds:       the entry above does not name a corpus edit made after it was written. The roster
            rows for `architecture-conformance` and `banned-prose` had both become narrower
            than the check behind them, which reads as coverage nobody has when the phase
            report enumerates checks against the roster. Both rows now say what their check
            does, the Checks section gains a paragraph stating the declaration and
            reconciliation rule, and the prior text of all three is in `CHANGELOG.md`.
Measured:   113 tests unchanged, `tools/ci.ps1` green, and the phase report unchanged at 158
            claims, 4 pass, 0 fail, 154 out of scope, 0 unexamined, 10 reconciled against a
            floor of 8. Over the 24 roster rows, 2 were widened and 0 narrowed.
Notes:      a row that says less than its check does is the same defect as one that says more,
            read from the other end. The next session writing against the row would take the
            reconciliation for something nobody had built.

### Phase 0 sign-off                                                         2026-09-07
Signed by the session that reviewed phase 0 and found the fourteen findings two entries above.
            Its only commit to this repository is that findings entry, which is a document, so
            the fresh session rule permits this. The repairs entry above was written by a
            different session, which says in its own first line that it may not sign its work
            off.
Verified:   by re-running, not by reading the repair session's report. `dotnet build` clean at
            0 warnings and 0 errors. `tools/ci.ps1` green end to end, all 6 steps, 113 tests
            passing inside it, exit 0. `tools/verify-phase` green at 158 claims, 4 pass, 0
            fail, 154 out of scope, 0 unexamined, 10 placements and verdicts reconciled
            against a floor of 8. Every figure the repairs entry states was reproduced.
Proved:     the reconciliation refuses the defect it was built for, on this corpus rather than
            on a constructed input. Section 17 was put back as a placement naming
            `pinned-constants`, and `tools/verify-phase` stopped with the placement named and
            the reason given, rather than reporting the twenty-nine claims as covered. The
            probe was reverted and the tree is clean. Separately, `tools/run-bash.ps1` was run
            through a caller setting the error preference to Stop with no bash on PATH: it
            exits 127 and the caller prints its step name, where before the fix it exited 1
            and printed neither.
Read back:  the phase report as written to disk rather than as modelled. Over the 4 passing
            claims in `artifacts/phase-report.json`, 4 carry a non-empty `by` and each names a
            check the roster carries; the HTML carries a "Reached by" column and every one of
            those 4 check names appears in it. This is the surface finding 1 was about, and it
            now carries what the roster claims for it.
Measured:   over the 90 files git tracks, 0 carry an em dash and 1 carries the banned string
            and it is the exempt line in `CLAUDE.md`. Over the 7 commits on this branch, 3
            deleted a line from a spec and all 3 changed `CHANGELOG.md` in the same commit.
            Over the 6 carried obligations added here, 6 name a due point `BUILD_PLAN.md` has
            and `PROGRESS.md` does not record as landed.
Not proved: the two runners have not seen this branch. Every figure above was measured on
            Windows, and `two-platform` asserts the workflow still declares both runners
            rather than that the suite passed on them, which is CI's own result. The merge
            condition is CI green, so the push is what settles it and nothing here anticipates
            that result.
Residual:   two observations, neither a defect and neither reopening anything. The guard that
            refuses a verdict resting on the report itself matches four phrases, so a
            self-referential note worded differently would pass it; the reach reconciliation
            is the stronger instrument standing behind it and would still refuse the claim.
            And the reconciled count is 10 against a floor of 8, which is a tighter margin
            than the other floors in the suite, though it is the scope carrying the property
            and the count only grows.
Two claims  a reader should not take from this entry. Green is a statement about the build and
not to take: never about a running system: nothing has fetched a bar, and the store this
            repository creates is empty. And the report is green because 154 of its 158 claims
            are out of scope, which is the correct answer for a phase that built no pipeline
            and is not evidence that the architecture has been verified. What changed at this
            checkpoint is that 31 claims which had been counted as covered are now counted as
            owed, each naming the point that ends it.
Outstanding: ten carried obligations. The 4 created by the architecture and by 1.7, none due
            before 1.3, and the 6 created by this review. None is due in phase 0.
Verdict:    phase 0 is signed off. The fourteen findings are repaired or carried with a due
            point, the phase report is green on everything it can assert, no claim passes
            without naming an instrument whose declared reach includes it, and the fixture is
            reported absent and never as a pass.

### Addendum to the phase 0 sign-off - the matrix saw the branch              2026-09-07
Discharges: the one item the sign-off above lists as not proved. It says the two runners have
            not seen this branch, that every figure was measured on Windows, and that the push
            is what settles it. The push has happened and this records the result rather than
            leaving the entry pointing at an answer nobody wrote down.
Measured:   over the 3 jobs of run 34144912456, being windows-latest, macos-latest and
            ubuntu-latest, all 3 green. All 6 steps ran on each and each reported 113 tests
            passing, 0 failing and 0 skipped, and each printed `ci: green`. The counts were
            read from the job logs rather than from the check status, because a step that
            passes by running nothing reports the same green as one that ran.
Notes:      the merge condition is now met and this is the only condition. What this discharges
            is the platform claim and nothing else: the two claims the sign-off says not to
            take from it still stand, and 154 of the 158 claims remain out of scope.

            The three jobs also ran once on the push before the pull request existed, as run
            34144883541, and were green there on the same commit. That is the same result
            twice rather than a second piece of evidence.

### Repairs to 0.7 - a replacement document, and what it took with it      2026-09-07
Not a checkpoint entry. It belongs to 0.7, which has landed. Phase 0 was signed off and is
            reopened here under the stopping rule, which reopens a phase when a finding breaks
            a check: `main` was red on two tests and phase 0's done condition 2 is that
            `tools/ci.*` is green. This session committed code and therefore may not sign it
            off.
Cause:      one commit, `8ac2442`, and the cause is not the two red tests. It replaced
            `docs/BUILD_PLAN.md` and `docs/CHANGELOG.md` wholesale rather than editing them,
            against a tree that had moved on, so everything the working copy did not carry was
            dropped without anything naming it. It also went straight to `main` as a linear
            commit with no merge, which is why no CI run saw it before it landed. The two
            failing tests and the twenty-three lost entries are both downstream of that one
            fact. A document change arrives as an edit to the file in the tree, never as a
            regenerated file pasted over it.
Built:      the twenty-three entries restored to `docs/CHANGELOG.md` from `8ac2442^`, verbatim
            and in date order, with the three the commit left standing kept where their dates
            put them. Two new entries, one recording the prior text of phase 0's deleted
            checkpoint detail and one for the RUNBOOK edit below. `changelog-reconciles` given
            the property that would have caught this. `docs/RUNBOOK.md`'s "Moving the
            installation" list given the SDK to install, on its own merits: it told an operator
            to clone and then run `tools/migrate` with nothing between them naming what has to
            be installed first.
Repaired:   `pinned-constants` and `stated-counts`, both by moving an assertion onto the thing
            that carries its property rather than by restoring a sentence to a document to feed
            a count. `pinned-constants` floored the mentions it found, which is a number an
            added sentence moves; it now floors the comparisons made against
            `src/Directory.Build.props` and `global.json`, which only a mention agreeing with
            the build can move, and it refuses a scan that compared nothing rather than passing
            over an empty result. `stated-counts` asserted the string `Six projects` against
            `BUILD_PLAN.md`; the count is stated in `CLAUDE.md`'s own layout block, which is the
            document that carries the block, and that is where it now reads it.
Floors:     stated with the count expected before the run and the count found.
            `pinned-constants`, framework: floor 2 comparisons, expected 2, found 2, being
            `CLAUDE.md` and the new RUNBOOK line, over 5 specs read as context with no floor.
            `pinned-constants`, band: floor 2 comparisons, expected 3, found 3, being 2 in
            `CLAUDE.md` and 1 in RUNBOOK. `changelog-reconciles`, high-water mark: floor 20,
            expected 26, found 26 at `99fc654`, against 30 entries now. Commits touching the
            changelog: floor 5, found 12, and that count is context rather than the property.
Missed:     one figure was predicted and wrong, and it is recorded rather than quietly
            corrected. The plan for this pass said the changelog held 27 entries before the
            deletion and would return to 27, having counted `grep -c '^### '` hits rather than
            entries. The template heading inside the fenced format block is one of those hits
            and is not an entry. The true figures are 26 entries before, 3 after, 25 deleted
            and 2 written for a net loss of 23, restored to 28 and standing at 30 with this
            pass's own two. The check counts a dated heading for exactly this reason and
            asserts the template is not one.
Measured:   over the 12 commits that have touched `docs/CHANGELOG.md`, the record's high-water
            mark is 26 entries, set at `99fc654`, and it holds 30 now. Over the 5 specs, 2
            framework mentions and 3 band mentions, and 0 disagree with the build files. Over
            the 30 tracked text files this pass touched, 0 carry an em dash and 0 carry the
            banned string. `tools/ci.ps1` green end to end, all 6 steps.
Tests:      117, up from 113, of which 111 were passing and 2 failing before this pass. 4 are
            new and every one is a proof a check can fail: a document stating a version the
            build does not, a scan that read two documents and compared nothing, a constructed
            record that lost an entry, and the assertion that the format block's template
            heading is not counted as an entry.
Proved:     the new property refuses the defect on the live record rather than only on
            constructed input. The restored changelog was cut back to 4 entries and
            `TheChangelogOnlyEverGrows` failed naming `99fc654` and the 26 entries to restore
            from; the cut was reverted and the tree is clean.
Carried:    nothing new. `Scope.cs` is deliberately untouched: its due points still describe
            phase 1's pre-revision order, which is 1.1's planning pass to repair, and leaving it
            here gives that pass a tree to verify against that this one did not edit.
Notes:      the two checks were right and the document revision was wrong, which is the same
            shape as the shallow-clone finding the 0.7 addendum records. Both had been reading
            a population that a document edit removed, and neither could see that its scope had
            narrowed, because a floor on how many mentions were found is satisfied by the
            mentions that remain. The repair moves each floor onto the comparison rather than
            onto the population, so the number cannot be moved by writing a sentence.

            `changelog-reconciles` asserts against the high-water mark rather than against each
            revision's predecessor. Both would have gone red at `8ac2442`, but a per-revision
            comparison stays red forever, because that commit is on `main` and history is not
            rewritten, so the only route to green would be to exempt the very commit the check
            exists to catch. Against the high-water mark it goes red when entries are lost and
            green again when they are put back, which is a check on the record as it stands.
            The first version of this check was written the per-revision way and failed on its
            first run, which is what surfaced the problem.

            Two entries in the restored file are dated 2026-09-05 and describe changes that
            landed on 2026-09-07 in `8ac2442`. They are left as written. A record is corrected
            by a new dated entry and never by editing an old one, and this note is that
            correction.

### 1.1 planning - the pass that settles what phase 1 builds against      2026-09-08
Not a checkpoint entry. It belongs to 1.1, which has not landed. `BUILD_PLAN.md` places the
            phase planning pass here, and this is it. No component is built and no migration is
            written. This session committed code and may not sign it off.
Decided:    four entries in `DECISIONS.md`. **A gap is a session the exchange traded and the
            store does not hold**, detected against the trading calendar rather than against the
            rows, because a run of stored dates is self-consistent whatever is missing from it.
            Both behaviours the corpus described are kept and they are different things: the
            fetcher refuses a series arriving with an interior session missing, and computation
            over a name whose stored series has a gap stops and names the date. Derived on read
            rather than kept in a column. **The stored series is adjusted**, one price set per
            bar, which is why `bar` has four price columns and no adjusted variant and why the
            corporate action checker has anything to do. **Bars come from EODHD, bulk nightly
            and per ticker for the backfill**, with the arithmetic that settles the route: bulk
            costs 100 weighted calls and a single-ticker historical request costs 1, so the
            night takes bulk at 100 against 500 and the backfill takes per ticker at 500 against
            25,000. And **the fixture is diffed on each stage's serialised output, of which the
            facts file is one**, superseding the entry that named the facts file alone, which
            was right about the rendered page and false for three phases because the facts file
            arrives at 4.3.
Resolved:   contradiction F, which named "the catalogue's chart renderer" and section 7 had no
            such row, so nothing could have resolved it as written. Restated against the real
            defect, section 15.5's Level chart mark and its four elements, and resolved per
            element: candles and the volume pane at 1.3, bands and moving averages at 2.4.
            Contradiction G, the theme research runner reading what it writes with only the
            write declared, both cells now R W.
Found:      three more contradictions, each given a resolve point in `BUILD_PLAN.md` because a
            finding named only in a planning conversation dies with it. H, `news_pulse`
            declared one year retained with Delete given to nobody, which is contradiction A in
            a second table and unnoticed until now, due 1.4 with A. I, the splits and dividends
            feed read by the nightly path with no source box in section 5, due 1.6. J, section
            17 naming two source lists against the record's three, due 1.7.
Built:      a tenth component. Section 15.4 says the server writes the shell and the marks, and
            the only server-side serve component was the read API, whose row says it performs
            no computation and no fetching, so the thing that turns stored values into SVG had
            no row and 1.3's done condition had nothing to be stated against. **Mark renderer**
            joins the catalogue with eleven blank matrix cells. The alternative was a decision
            redefining "computes nothing" so geometry is not computation, plus a check able to
            tell arithmetic on figures from arithmetic on coordinates by inspecting
            expressions, which gets it wrong quietly.

            And the due points are read from `BUILD_PLAN.md` rather than written a second time
            in `Scope`. This is the repair for the drift `8ac2442` caused and nothing caught.
Predicted:  stated before the pass and checked here. 158 claims before, 163 after: 160 now, plus
            three when 15.5's Level chart row decomposes per element at 1.3. The mark renderer
            supplies two of the five, one catalogue claim and one matrix claim, and both landed.
            Contradiction G's two amended matrix cells and the membership loader's amended cell
            change no count, which is stated rather than left silent, because a prediction that
            does not account for the changes made beside it cannot be missed.
Measured:   160 claims, 4 pass, 0 fail, 156 out of scope, 0 unexamined, 25 tables, 11
            placements and verdicts reconciled against a floor of 8. Over the 132 distinct
            out-of-scope subjects, 49 take their due point from the plan, 52 from the residual
            maps, and 31 from a table heading, those last being the screens tables that
            contradiction D moves to table and subject at 1.3.
Missed:     the derivation's reach was predicted before it was run and the prediction was wrong
            in the useful direction. Expected 20 to 30 derived and 70 to 80 residual over
            roughly 100 subjects; found 49 derived and 52 residual over 132. The floor is set at
            40, below the measured figure rather than at it.
Proved:     the first run of the derivation was wrong and the run is what showed it. Reading a
            due point from any checkpoint whose text names the subject put the trend classifier,
            the ladder builder, the shortlist builder and the fundamentals fetcher at 3.0 and
            the base rate at 4.0, because a planning checkpoint names what it settles and builds
            none of it. Every one of those is earlier than the code and would have failed the
            moment 3.0 landed. Planning checkpoints are now excluded, which is a rule about what
            a planning checkpoint is rather than a patch, and the seventeen hand corrections
            made in the same pass are what the derivation was checked against.
Carried:    nothing new. Three obligations were re-pointed off 1.5, two to 1.1 and one to 1.3,
            because all three concern the first committed manifest and the first stored value
            and the first manifest arrives with 1.1's recorded-response double. That is the
            reasoning the 0.7 repair session used when it moved two of them from 2.1, applied
            one checkpoint further.
Tests:      122, up from 119 at the start of the pass. `tools/ci.ps1` green end to end, all 6
            steps. `tools/verify-phase` green.
Notes:      two exceptions to the derivation are declared with their reasons, and the reasons
            are the point. `Forward returns` derives 6.1 because 4.5 writes the table name in
            snake case; that is later than the truth rather than earlier, so it is safe and
            still wrong. `Source lists` derives 1.7 because 1.7 produces the first draft, while
            the limits row is about a search returning only listed sites and its own Asserted by
            column names a fixture search; deriving 1.7 would fail that claim the moment 1.7
            lands, which is the one direction that is never safe.

            `changelog-reconciles` caught this pass's own commit. A sentence added to 1.3
            rewrote a line, which counts as a deletion, and the entry recording it had gone into
            the commit before. The check was right and the commit was amended.

            The screens tables still resolve by table heading alone, which is contradiction D
            and is 1.3's to fix rather than this pass's. It is named here so the 31 subjects it
            covers are not read as an oversight in the derivation.

### Addendum to the 1.1 planning pass - three corrections carried into the build   2026-09-08
Not a checkpoint entry. It belongs to 1.1, which has not landed. It corrects the entry above
            rather than editing it.
Split:      the two derivation exceptions were in one list and are not the same kind of thing.
            `Forward returns` derives 6.1 where the table is created at 4.5, which is later than
            the truth and can only delay a claim. `Source lists` derives 1.7 where the limit is
            about a phase 5 fixture search, which is earlier than the truth and fails the day
            1.7 lands. They are now two lists, each stating its direction. A later-than-truth
            exception is declared and passes quietly; an earlier-than-truth one is declared,
            passes, and reports itself on the phase report every run, because a known unsafe
            derivation visible only in a source comment is one nobody sees again.

            The direction is asserted rather than trusted. For every declared exception the
            derived point is compared against the declared one, and a late exception that is
            actually early fails. That is the one failure the split cannot otherwise catch: an
            unsafe derivation filed under the safe heading passes every other assertion and then
            fails the day its checkpoint lands. Proved by moving `Source lists` into the late
            list and watching it fail, then reverting.
Floors:     stated with what each produced and what was expected before the run. The derived
            count: floor 40, expected 49, found 49. Declared exceptions: floor 2, expected 2,
            found 2. Both new floors sit below their measured value rather than at it.

            **Why 40 and not 49.** This floor sits on a population that moves in both
            directions, which is the case the corpus has been bitten by twice from the other
            side. Contradiction D is resolved at 1.3 and re-keys the screens tables on table and
            subject, which moves 31 subjects out of resolving by table heading and into being
            subjects like any other. Some will derive and some will become residual, so both
            counts change at 1.3 for a correct change, and a floor anchored at today's 49 would
            go red for it. Written down now so 1.3 re-anchors against a number stated in advance
            rather than against whatever that run produces: after 1.3 the expectation is 132
            subjects becoming about 163 keyed subjects, with derived rising above 49 and the
            residue rising above 52, and the floor moving to 60 if those hold.
Message:    `changelog-reconciles` names what a deletion is, in the failure itself. git counts a
            rewritten line as one deletion and one addition, so adding a sentence to the middle
            of an existing paragraph deletes the line it replaced. That is not visible from the
            check's name, and the commits that trip it are usually not the ones that removed
            anything, so the next person to hit it reads the name, sees a commit that only added
            prose, and looks for the bug in the check.
Happened:   it tripped this pass. A sentence added to 1.3 naming the mark renderer rewrote a
            line, and the entry recording that edit had gone into the commit before it. The
            check was right, the commit was amended, and the message now says why.
Tests:      125, up from 122. Four are new: the direction assertion, the surface assertion over
            the written report rather than the model, the ordering proof beneath both, and the
            floor on declared exceptions.
Notes:      the surface assertion reads the generated files rather than the model, which is
            finding 1 of the phase 0 review applied before it could repeat. Its first version
            searched the whole page for the safe exception's absence and failed, because
            `Forward returns` is also a claim subject in the read and write matrix and appears
            in the claims table. The assertion was wrong and the code was right; it now reads
            the unsafe section alone.

### 1.1 - the membership loader and the component access mechanism      2026-09-08
Built:      `IComponent` in `EquityBrief.Core`, an interface with a static abstract member, and
            the `Store`, `Feed` and `Touch` vocabulary behind it. `MembershipLoader` in
            `EquityBrief.Worker`, with its access declaration, its upsert, its past-date query
            and its own run log row. Migration 2 creating `membership`, with `left` quoted.
            `IIndexMembershipFeed` and `RecordedIndexMembershipFeed`, which answers from a
            captured payload and reaches no network by construction because it holds no HTTP
            client to misconfigure. `ProviderCredentials`, which names the secrets key once and
            refuses a blank. The first captured fixture, `membership-2026-09-05`, with its
            manifest. And `component-access`, the twenty-fifth check on the roster.
Declared:   the declaration lives in the component, discovered by its type rather than by the
            spelling of a property. That is the one thing it does differently from `CheckReach`
            inside the suite, which is found by the literal string "Reach" and would empty its
            own population on a rename. A component declaration crosses an assembly boundary
            and is read reflectively from outside, so a type is the safer key.
Measured:   142 tests, up from 125 at the start of this checkpoint. 160 claims, 8 pass, 0 fail,
            152 out of scope, 0 unexamined, 25 tables, 15 placements and verdicts reconciled
            against a floor of 12.
            The fixture is PRESENT with 1 captured, which is the first time it has not read
            ABSENT. Over the 27 catalogue rows, 27 are compared against a matrix row and every
            term in every Reads and Writes cell resolves except 4, all of them the `calendar`
            read that is contradiction E and settled at 3.0. Over the 11 matrix columns, 11 map
            to a store and every store but the candidate register has a column. Over the 18
            tables SCHEMA declares, 18 have an enum member and 18 enum members have a table.
            Over `MembershipLoader`'s matrix row, 11 cells asserted against the declaration of
            which 9 are blanks.
Floors:     each with the count it produced and the count expected before the run. Passing
            claims: floor raised 4 to 6, expected 7, found 8, the eighth being the membership
            store claim that `schema-columns` reached once the table existed. Reconciled
            placements and verdicts: floor raised 8 to 12, expected 14, found 15. Declared owner entries with a
            class: floor 2, expected 3, found 2, and the floor was corrected down to the
            measured value rather than the guess. Writes found against a declared table: floor
            2, expected 3, found 3. Matrix cells compared: floor 11, expected 11, found 11, of
            which 9 blanks against a floor of 9. Shipped assemblies scanned: floor 5, found 5.
            Catalogue rows: floor 25, expected 27, found 27.
Discharged: three obligations, and the first was forced rather than merely due.
            `writer-ownership` is now both directions its roster row has always claimed: two of
            its tests asserted over a population of zero and turned red the moment the first
            component landed, which is the mechanism that put the widening in the same commit
            instead of leaving it to be remembered. The zoneless instant is refused rather than
            resolved against the machine zone. The manifest checker opens every captured
            response and checks the file it names exists.
Amended:    this checkpoint amends its own done condition. `BUILD_PLAN.md` put the fixture-absent
            test's inversion at 1.5 with the gap fixture; the first captured input arrives here,
            with the recorded-response double this checkpoint was already told to build, so the
            inversion fell due here and was done here. The absence half is kept rather than
            deleted: absence still reports as absence over a root with no fixtures.
Found:      four defects in the suite's own instruments, three of them in checks that had been
            green for a phase.

            The statement reader behind `writer-ownership` and `bar-append-only` read prose. It
            matched a verb and a table name anywhere in one semicolon-delimited span over raw
            file text, so a comment saying "Insert, Update and Delete are the three operations
            SCHEMA declares" beside the word membership read as three writes. Stripping comments
            was not enough on its own, because the `Store` enum has members named `Bar` and
            `Membership` and the `Touch` enum has `Insert`, `Update` and `Delete`, and an enum
            body carries no semicolon, so the whole declaration read as one statement naming
            every table and every operation. It now matches the shape of SQL rather than words
            that appear in it, and both false positives are kept as cases.

            `clock-usage` had the same defect and it fired on a comment explaining the zoneless
            instant repair. It now strips comments before scanning, with a test proving a real
            read is still found and the same words in a comment are not.

            `store-portability` hardcoded the number of tables in a migrated store. It now
            derives that from the migrations, which is an independent source from the store the
            scan opens.

            And the loader's first test asserted that a second run reuses its run id, which the
            run log's primary key refuses. The test was wrong: the grain is one row per run per
            stage and SCHEMA gives the table no updater and no deleter, so a second run is a
            second run. Idempotency is a claim about the stored data and the run log is a log.
            Section 19.1's placement was owed at 1.1 and had to move the moment this entry was
            written, which is the mechanism `BUILD_PLAN.md` predicted for 1.5 arriving four
            checkpoints early. It had been owed at 0.6, then 1.3, then 1.1, each time on the
            reasoning that the first manifest arrives with the first captured input. The
            manifest does arrive here and is checked here, but the table's rows are the bars,
            the fundamentals, the news and the expectations, and a fixture holding one captured
            input holds almost none of that. Owing it where the first manifest lands confused
            one row for the table. It is now owed at 1.8, where phase 1's expectations land.
Carried:    nothing new. Contradiction D still keys the screens tables on a heading alone,
            covering 31 subjects, and is 1.3's to resolve.
Notes:      the run log is the one store every component may append to, and SCHEMA says so in
            words rather than by naming an owner. Both checks read that exemption out of the
            document rather than hardcoding it, so it disappears the day the cell names a
            component. The catalogue states the same fact once, in the Run log row, and no
            component's Writes cell lists it, so `component-access` takes the run log claim from
            the matrix column and not from the Writes cell.

            Eight files were converted from LF to CRLF by a scripted edit and converted back.
            `.gitattributes` normalises to LF in the repository, so nothing reached a commit,
            but two checks failed in the meantime by reading a line ending rather than a defect.

### Addendum to 1.1 - four defects the checkpoint shipped, and a claim it falsified   2026-09-08
Not a checkpoint entry. It belongs to 1.1, which is what authorises it: these are defects in the
            code `b67ba67` shipped and a document claim that commit made false. They land in the
            same pull request so 1.1 closes correct rather than closing with three known defects
            and a repair trailing it.
Fixed:      `network_requests` was written as the literal 1, one line below `rows_written` being
            measured from the store with a comment quoting SCHEMA's reason that a stage's own
            count is the stage's opinion. It was the same defect on the number that carries more
            weight, and the measurement already existed on the recorded feed. The count is now
            read off the feed, and `Requests` sits on `IIndexMembershipFeed` rather than on the
            double, so the live feed has to answer the same question and the cost limit is
            asserted against something measured on both paths. A literal there could not go
            wrong today and could not go right on the night a feed starts paging.

            A garbled leave date read as a current member. The parser returned null when a
            property was absent, when it was not a string, and when the parse failed, and for
            `EndDate` null means the name is still in the index. So a name that had left,
            carrying an end date the parser could not read, would be stored with no leave date
            and read as present, with nothing failing anywhere. That is the one thing membership
            exists to prevent. The three outcomes are now separate: absent is null and means a
            current member, present and unparseable throws, present and not a string throws, and
            both throws name the ticker, because a format failure over five hundred constituents
            that does not say which one is useless.

            The date parse was machine-dependent. `DateOnly.TryParse` with no culture resolves
            against the machine's locale, so the same payload is a March date on one machine and
            a refusal on another. This is the checkpoint that refused a zoneless instant for
            resolving against the machine zone and then left a date string resolving against the
            machine locale, in shipped code rather than in the suite. It is now an exact
            invariant parse, which is the parse that was meant rather than a tightening.

            The idempotency test asserted over four of the row's five columns and its name
            claimed the whole state. `observed_at` moves on every run by design, and the test
            left it out of the select. It now asserts that the instant is present and that the
            rows within one run share it, so the exclusion is a claim rather than an omission.

            And one comment read backwards: it said the comparison is strict on the left edge
            and not on the right, where `joined` uses `<=` and `left` uses `>`, and the column is
            named `left`, so the sentence read two ways at once. It now names the columns.
Widened:    `clock-usage` covers the machine's locale as well as its clock, since a date parsed
            without a culture is the same property: nothing may depend on how this machine
            happens to be configured for time. The roster row was widened in the same commit
            rather than a phase later, which is the defect two rows carried out of 0.7.
Narrowed:   section 14 claimed a night run twice produces identical stored state. 1.1's own code
            made that false: every membership row carries the instant of its fetch and the run
            log gains a row per stage per run. The claim now says the run is idempotent in what
            it records about the market, and that the observation instant and the run log are
            what record that the night ran twice. A run leaving no trace of having repeated
            would be the defect rather than the property.
Measured:   153 tests, up from 142. 11 are new and 9 of those are the three parse outcomes and
            the locale case, each a separate refusal rather than one test asserting four things.
Notes:      the culture check's own permanent proof is what caught its first version. The
            pattern was written through a scripted edit, its escapes did not survive, and the
            reader matched nothing at all: the assertion over the corpus passed, and the proof
            that the check can fail is what went red. A check with no negative proof would have
            been committed green and covering nothing.

            Four defects in one checkpoint's code, found by reading it rather than by running
            it, and three of the four are a falsy value or a stated number standing where a
            measurement or an absence belongs.

            A fifth was found by the runners rather than by reading.
            `TheUnsafeExceptionsAreOnTheSurfaceAPersonReads` read `artifacts/phase-report.json`
            and `.html` from the repository root. Those are gitignored and exist only once
            `tools/verify-phase` has been run, and verify-phase is deliberately not a CI step,
            so the test passed here on a leftover file and failed on all three runners. It now
            generates the report into a temporary directory and reads that, which is the pattern
            the test beside it already used. The property is unchanged and still asserts the
            written surface rather than the model; what changed is that it no longer reports the
            state of a working copy. Reproduced locally by deleting `artifacts/` before running
            the suite.

### 1.2 - the one-year backfill                                          2026-09-08
Built:      migration 3 creating `bar`, prices TEXT and decimal in code. `IHistoricalBarFeed`
            and `RecordedHistoricalBarFeed`, one captured file per ticker. `Backfill`, which
            fetches one year for any member holding no history, one request per name, and never
            again for a name that already holds its year. `Money` in `EquityBrief.Data`, the one
            way a price reaches a store. Three fixture files, a year of daily bars each.
Measured:   over the fixture's 4 constituents, of which 3 are current members and 1 has left:
            3 names owed a backfill, 3 requests, 783 rows written, and 0 of each on the second
            run. Per name, 261 sessions from 2025-09-05 to 2026-09-04. The population is stated
            because it is 3 names and not 500: a claim about the live index is not something
            this suite can assert, and the done condition's "every index member" is 4.1's to
            carry.

            261 rather than the 262 the captured file holds, and the difference is the window
            rather than a defect. The backfill asks for the year ending on the session date,
            which at the fixture instant is 2026-09-05, so it starts on 2025-09-05 and the
            capture's first bar falls outside it. The test asserts both edges rather than only
            the count, so that reads as a window rather than as a missing bar.
Discharged: three obligations, all created by the 0.7 review.

            The money column list is read from SCHEMA's Notes cell, where a money column is
            marked by the word "decimal", rather than kept as an array beside the check. One
            table, `theme_section`, is described as a difference from another and has no column
            table of its own; it is named and asserted to be the only one, so a second such
            table is a failure rather than a silent exclusion from the money check.

            `Shell.Run` reads both streams at once. Reading one to completion first blocks until
            the child closes it while the child blocks writing to a full error pipe, and neither
            moves again. `tools/flood-probe` writes about 400 kB to each stream and the test is
            bounded by a timeout, because the failure is a hang and a deadlocked test reports
            nothing. Proved by reverting the fix and watching it time out.

            The runtime money guard refuses anything that is not a decimal at the point a price
            is bound. `STRICT` does not do this: SQLite renders a double as text and stores it,
            which 0.2 recorded after a test written to prove otherwise failed. The test asserts
            both halves, that the guard refuses the double and that the store would have taken
            it.
Resolved:   contradiction B. The per-name limit read as a flat zero, which forbids the step the
            run order carries at position two, so a check reading it would have failed on a rule
            nobody meant. The limit is carved rather than deleted: the property it protects is
            about the steady-state night, and the backfill is bounded rather than nightly.
Found:      the backfill had no component row. Section 14 carries it at position two, section 17
            gives it a limits row, and section 7 had nothing, so the class doing the work had no
            catalogue name and the matrix asserted nothing about what it touches. **Backfill**
            is now a component, and SCHEMA gives `bar` a third inserter with the exception
            paragraph saying which. The alternative was folding it into the bar fetcher, which
            arrives at 1.4 and does a different job on a different endpoint with the opposite
            cost shape.

            And `rows_written` was measured by counting rows carrying this run's observation
            instant, which two runs sharing an instant would each attribute to the other. A
            fixed clock produces that in a test and a fast machine can produce it for real. It
            is a delta over the table now, which is still measured from the store and does not
            rest on the instant being unique.
Floors:     stated with what each produced and what was expected. Money columns SCHEMA declares:
            floor 8, expected 11, found 11. Money columns in migrations: floor 5, expected 5,
            found 5, being `bar`'s four prices and `run_log`'s spend. Passing claims: 11 against
            a floor of 6. Reconciled: 18 against a floor of 12, both left where 1.1 set them
            because contradiction D still moves them at 1.3.
Tests:      166, up from 159 at the start of this checkpoint and 153 at the end of 1.1.
Notes:      the backfill declares `Feed.HistoricalPrice` and the bar fetcher will declare
            `Feed.BulkPrice`. They are two endpoints with opposite pricing, which is the whole
            argument for the backfill running per ticker, and giving them one enum member would
            have hidden that in the one place a reader checks it.

            Two claims derived 1.2 and had to be excepted once this entry was written, which is
            the third time the reconciliation has refused a due point on the checkpoint that
            landed. The catalogue's Run log row derives 1.2 because that is where the backfill's
            request count first reaches the log, and the row is not about one stage: its Reads
            cell says "every component appends", so it is a claim about every component and the
            last of them lands in phase 6. The limits table's Backfill row derives 1.2 because
            1.2 builds it, and the row is the limit rather than the component: its own Asserted
            by column names the run log's request count against the names lacking history, which
            is `nightly-cost` reading a recorded run at 1.4. Both are declared as derived earlier
            than the truth, so both report themselves on the phase report every run.

            The process error that surfaced them is worth naming. The suite was run after the
            spec edits and before the PROGRESS entry, and `HasLanded` reads PROGRESS, so the
            failure could not appear locally and CI found it. A checkpoint's own record is the
            last thing written and the first thing that changes what the reconciliation
            refuses, so the suite belongs after it.

            `Money.FromStorage` refuses a group separator, and the test that found this is the
            reason. `NumberStyles.Number`, the convenient default, allows one, and under the
            invariant culture the group separator is a comma, so "12,34" written by a
            comma-decimal machine parses cleanly as 1234. A hundredfold error on a price, read
            back with nothing refusing it.

### 1.2 - the fixture is captured, and the parser it was hiding                2026-09-08
Corrects:   the 1.2 entry above, whose measured figures were taken over generated bars. A
            seeded random walk emits a bar for every weekday, so it tests the parser against
            its own generator: a wrong field name, a different date format, an adjusted close
            under another key, a null where a number is expected and a whole number rendered
            with no decimal part all survive it. Two of those were present and one was fatal.
Built:      nothing. Three captured bar files replacing the generated ones, a captured
            constituents file replacing a hand-built one, one parser corrected, and every
            expectation 1.2 wrote re-derived rather than translated.
Spent:      3 requests at weight 1 on the per-ticker historical endpoint, stated before the
            capture and equal to it, which is the backfill's own cost claim measured on a live
            call rather than on a double. 1 further request at weight 10 on the index
            fundamentals endpoint, which was outside the capture as asked for and is named here
            because it was spent: the manifest was about to assert a fetch instant for a file
            nobody had fetched, and the shape could not be checked without asking for it.
Measured:   over the fixture's 4 constituents, of which 3 are current members and 1 has left:
            3 owed, 3 requests, 756 rows written, 0 of each on the second run. Per name, 252
            sessions from 2025-09-05 to 2026-09-04, against the 261 the generated fixture held.

            252 is derived rather than counted off the run, which is what done condition 7 asks
            of at least one expectation. 261 weekdays in the window less 9 days the exchange did
            not trade, each named in the test: Thanksgiving, Christmas, New Year, Martin Luther
            King, Washington's Birthday, Good Friday, Memorial, Juneteenth and the observed
            Independence Day. The derivation is asserted against its own arithmetic before it is
            used, and the stored session list is compared to it date by date rather than by
            count, so a series holding the right number of the wrong days fails.

            The generated fixture had no holidays in it at all. A gap rule reading "a weekday
            with no bar" would have passed it and raised nine false gaps on the first real
            night, which is 1.5's whole subject arriving as a fixture fact rather than as a
            surprise (see: A gap is a session the exchange traded and the store does not hold).
Found:      the membership parser could not read the provider's payload, and this is the
            finding that justifies the pass on its own. The recorded feed's parse read StartDate
            and EndDate out of the Components object. The provider does send Components, and it
            carries Code, Exchange, Name, Sector, Industry and Weight, with no dates on any
            entry and no row at all for a name that has left. The spans are in
            HistoricalTickerComponents, a different object in the same payload. Against the real
            response the parser threw on the first constituent.

            It failed loudly rather than quietly, which is why this is a carried defect and not
            an interrupt, but it was total: the loader could not have completed a single live
            night. The hand-built fixture satisfied it because the fixture had been written with
            the dates in the object the parser was reading, which is one sentence read twice.
            Made permanent by `TheSnapshotObjectIsRefusedRatherThanReadAsTheIndex`, which feeds
            a real snapshot payload and asserts the refusal names the object holding the spans,
            and by an assertion that the captured fixture carries both objects, so the refusal
            is about which one is read and not about which one is present.

            XRAY's leave date is 2024-04-03, not the 2026-03-21 the hand-built fixture invented,
            and the three dates the past-date query is asserted at were all chosen around the
            invented one.

            The adjusted close is now load-bearing in the fixture. The generator set
            adjusted_close equal to close on every row, so a parser taking the wrong field
            passed the fixture and failed only a payload written for the test. The capture
            differs on 232 of AAPL's 252 window rows and 240 of MSFT's, and on none of KEYS,
            which pays no dividend and did not split in the window. Both cases are in one
            fixture, and the comparison reads the raw close straight from the file rather than
            asking the parser for the field it is being tested for not taking.

            Two number shapes reached storage that never had before. The provider sends JSON
            numbers rather than strings, renders a whole number with no decimal part at all, and
            carries four decimal places on an adjusted close where close carries two. AAPL's
            first stored session is all three at once: an open of 240 stores as `240`, and an
            adjusted close of 238.8078 stores with its fourth decimal held, which a route
            through double would have rounded away.

            `TemporaryStore.Dispose` called `SqliteConnection.ClearAllPools`, which is
            process-wide, while xUnit runs test classes in parallel. A store disposing pulled
            pooled connections out from under tests still reading in other classes. Measured at
            1 failure in 15 runs before the change and 0 in 20 after, always in the test that
            makes four sequential queries, which is the widest window rather than a second
            fault. It now clears this store's pool alone. An intermittent red that goes green on
            a re-run is worse than a reliable one, because it teaches a reader to press the
            button rather than read the result, and on the matrix it would have arrived as one
            runner in fifteen going red for no reason anyone could reproduce.

            `tools/ci.ps1` and `tools/verify-phase.ps1` had never run from a PowerShell prompt
            on this machine, and both are documented commands. `tools/run-bash.ps1` took the
            first `bash` on PATH. On a Windows machine with the optional WSL feature enabled
            that is the launcher in System32, which cannot open a Windows path and answers with
            an advertisement for installing a distribution. Meanwhile a working bash was
            present and unreachable: a default Git for Windows install puts `git.exe` in `cmd\`
            and `bash.exe` in `bin\`, and only the first goes on PATH.

            The wrapper now collects every bash on PATH plus the ones beside `git`, and picks
            the first that can read the script it was asked to run. Chosen by the property
            rather than by ruling out paths that look like WSL, because the property is what
            matters and it does not go stale when a launcher moves. The suite's own lookup
            matches it, and one test was reaching for `Shell.Locate("bash")` directly rather
            than `Shell.Bash()`.

            This was a loud failure and not a silent pass, which is why it is recorded as a
            defect found rather than as an interrupt. But it is exactly what `run-bash.ps1`
            exists to prevent: the file already turns "no bash" into a named message, and the
            wrong bash is the commoner case on Windows and produced no message of its own.
Verified:   `tools/ci.ps1` green end to end from a PowerShell prompt with no bash on PATH, six
            steps, 175 tests passing, exit 0. `tools/verify-phase.ps1` green: 162 claims, 11
            pass, 0 fail, 151 out of scope, 0 unexamined, 18 placements and verdicts reconciled
            against a floor of 12, 25 tables, fixture PRESENT with 1 captured, 25 checks on the
            roster with 20 carried. No floor was raised on this pass. The suite was run 15 times
            before the pool change and 20 times after, which is the population the 1-in-15 and
            0-in-20 figures above are over.
Amended:    this checkpoint amends its own done condition, in those words, and the amendment is
            the pass's second deliverable rather than an escape it authorises. The definition of
            done goes from seven conditions to eight, the eighth being that the PROGRESS entry
            is written before the run that verifies the checkpoint. `HasLanded` reads PROGRESS,
            so a run against a tree whose entry is missing is a run against a corpus in which
            the checkpoint has not landed, and it is green on a question it never asked. It had
            hidden a failure three times: at 1.1, at 1.2, and on this pass. It was recorded in
            the 1.2 entry above as advice to a future session, which is a record telling a
            reader what to do rather than a rule the corpus holds.

            This entry was written before the verification run, which is the first application
            of the condition it adds.

            `stated-counts` now reads the expected number of conditions out of the sentence that
            states it rather than repeating it as a literal, and asserts the merge section
            spells it the same way. Adding condition 8 meant editing the rules, an assertion and
            an anchor string, and only the first of the three was the fact. That is the defect
            the check exists to find, in the check itself.
Also:       RUNBOOK's secrets section told the operator to write the secrets file by hand and
            never said what to put in it. The key written by hand was consequently one name and
            the code looks for another. The section now names the keys, the nested file shape
            and the environment-variable spelling, and `ProviderCredentialsTests` asserts the
            runbook against the code's own constant in both forms, because that is one fact in
            two places. The local file was rewritten to the canonical path with its value kept.

            The fixture manifest asserted a fetch instant for the constituents file that no
            fetch had produced. All four inputs now carry real instants. The constituents file
            is a trimmed capture, 4 names of the 503 in Components and the 822 in
            HistoricalTickerComponents, and the manifest schema gains an optional `trimmedTo` so
            a subset says it is one. Without it the file reads as the whole response and a later
            session re-captures it to fix an absence that was deliberate. The provider's keys are
            kept as sent, so the gaps in them show where the cut was made, and nothing about the
            shape is trimmed.
Carried:    the bar fixture and the constituents fixture are captured. No other feed in section
            5 has been exercised against a real payload, and the same defect class is available
            in each: the splits and dividends feed at 1.6 and the news feed at 1.7 both arrive
            with a parser and a double, and neither has seen a provider response. Each captures
            before it asserts.

### 1.2 - what the past-date assertions were testing, and the class behind it       2026-09-08
Corrects:   the entry above, which recorded XRAY's leave date moving from an invented
            2026-03-21 to the provider's 2024-04-03 and did not say what that did to the three
            past-date assertions chosen around the invented value. Re-pointing them was not
            enough. They were positioned either side of a boundary, and the boundary moved two
            years, so what each one distinguished had to be worked out again rather than
            assumed to have travelled with it.
Found:      one of the three had collapsed. With XRAY as the only departed name, every date
            from its leave to the fixture instant returns the same set, because no membership
            event falls between them. So "after a leave and before tonight" was "tonight", and
            a query that answered with tonight's set for any recent past date would have passed
            it. The assertion had been made against a date two years from the leave rather than
            one day after it, which hid this: the old date sat five months before the invented
            leave and the new one sits on the real leave, and only the second makes the
            question visible.

            That was true before this pass as well. The invented leave was 2026-03-21 and the
            fixture instant is 2026-09-05, so the same stretch existed and was five months long
            instead of two years. The collapse is not something the real date introduced; it is
            something the real date made possible to notice.
Built:      a fifth constituent, AAL, joined 2015-03-23 and left 2024-09-23, re-trimmed from
            the `fundamentals/GSPC.INDX` response already captured on this pass. No new
            request. It splits the stretch between XRAY's leave and the fixture instant in two,
            which is what makes the third distinction a distinction.

            The fixture is now 5 constituents, 3 current members and 2 departed, and the
            backfill is unaffected: a departed name is not owed history, so 3 owed, 3 requests
            and 756 rows all stand. The membership figures move: 5 rows written for the
            membership stage rather than 4, and the departed names are asserted as a set rather
            than through Assert.Single. A fixture with one departed name makes every statement
            about departure a statement about one row.
Measured:   seven dates, each named for the distinction it draws. Over the fixture's 5
            constituents:

            1982-11-29, before any name joined, answers with nothing. A query ignoring the date
            would answer with the whole table here and pass every other assertion below.

            2010-01-04, where two of the five had not joined, answers AAPL, MSFT, XRAY. This is
            the join half of the span and the one a query keyed on the leave date alone misses.

            2018-11-05 and 2018-11-06, either side of one day, are the join edge. The leave
            edge was asserted this way and the join edge was not, which left `joined <` and
            `joined <=` indistinguishable. Added on this pass.

            2024-04-02, between a join and a leave, answers with all five including both names
            that have since gone.

            2024-04-03, XRAY's leave date, answers AAL, AAPL, KEYS, MSFT. Two properties at
            once: the leave edge is strict, and the answer differs from tonight's, which is the
            third distinction and the one that had collapsed.

            2026-09-05, the fixture instant, answers AAPL, KEYS, MSFT.

            Seven dates and six distinct answers, derived in the test from the seven results
            rather than stated in a comment. The first draft of this entry and of the comment
            beside it both said six dates over a test that asks seven questions, which is why
            the figure is now counted rather than written down. One pair agrees deliberately
            and is asserted to agree: 2018-11-06 and 2024-04-02 are the same region, because
            the first is there as an edge against 2018-11-05 and not as a region of its own.

            Proved by removing AAL from the fixture and watching the test go red, then
            restoring it. The permanent proof is the assertion rather than that exercise: a
            fixture that loses its second departed name fails on the inequality.
Also:       three synthetic payloads in the parser tests carried 2026-03-21 and its day-first
            and integer spellings. They belong to no fixture and never did, but a synthetic
            payload carrying a real name's date invites a reader to connect the two. They now
            read 2021-07-04, and the day-first case is 04/07/2021, which is ambiguous rather
            than merely slashed: the fourth of July read one way and the seventh of April read
            the other, so a lenient parse succeeds under both cultures and returns a different
            date under each. A day-first string with a day above twelve is refused by an
            invariant parse anyway and tests the culture far less.
Checked:    the pass was swept by four independent readers and each reading was then given to a
            second reader told to refute it. Three defects in this pass's own work came back,
            and all three are the kind that pass a green suite.

            The comment introducing the past-date dates said six over a test that asks seven
            questions, and the entry above said the same. Both are now derived: the test
            collects its seven answers and asserts six distinct ones, so the figure is counted
            rather than written down. This is the defect `stated-counts` exists for, committed
            in the same pass that argues for deriving counts.

            `TheDateParseDoesNotDependOnTheMachinesLocale` set no locale. It asserted an exact
            parse, which implies locale independence without demonstrating it, and would have
            passed with the `CultureInfo.InvariantCulture` argument removed from the parse it is
            named for. It now runs the parse under en-GB, en-US and de-DE and asserts the same
            answer under each, and it demonstrates the ambiguity the refusal rests on rather
            than asserting it in a comment: 04/07/2021 parses to the fourth of July under en-GB
            and the seventh of April under en-US, both succeeding, which is what an exact parse
            is refusing. A refused string nothing would have misread proves only that a slash is
            not a hyphen.

            The parse test asserted a count of five and two Contains clauses, covering two rows
            of five. A regression dropping AAL's end date leaves the count at five and both
            clauses true. It asserts the whole ticker and leave-date projection now.
Class:      the general form, stated because the instance is the third of its kind and naming
            the instance again would not stop the fourth.

            A fixture written by the session writing the parser proves that the parser agrees
            with the fixture. It proves nothing about the provider. The two artefacts have one
            author and one set of assumptions, so the agreement between them is a restatement,
            and every check reading it reports green over a population of one opinion held
            twice.

            This is the same shape as a constant pinned document against document, which Pass A
            repaired at 0.7. `pinned-constants` floored the mentions it found across two specs,
            and a mention is a number an added sentence moves, so the check could be satisfied
            by writing prose. The repair was to move the floor onto the comparisons made against
            `src/Directory.Build.props` and `global.json`, which only a mention agreeing with
            the build can move. The assertion stopped resting on what the corpus says about
            itself and started resting on the artefact the corpus describes.

            The repair here is the same move and the artefact is the provider's own response. A
            parser is checked against a captured payload or it is checked against itself, and
            there is no third option that a fixture written alongside it can provide. Two
            parsers are in that state now, the splits and dividends feed and the news feed, and
            both are carried with the defect named rather than the task: a row reading "capture
            the news feed" is a chore that gets done late, and one reading "the parser is
            checked against itself" says something is wrong now.
            `fixtures/README.md` now says that a fixture counts two different things. Phase 2
            widens the fixture "to four names" in `BUILD_PLAN.md` and in three places in the
            architecture, and this fixture now holds five constituents, which reads as already
            past four. Names are the tickers with a captured price series and constituents are
            the membership rows; a departed name has a row and no bars, so this fixture holds
            5 constituents and 3 names. The distinction goes in the file whose subject is the
            folder's shape rather than in the four documents that say four, because it is one
            fact and those would be four statements of it.
Carried:    unchanged from the entry above, with the two parser rows now in `BUILD_PLAN.md`'s
            obligations table at 1.6 and 1.7 rather than only in that entry's prose. Each
            checkpoint's done condition states that its fixture is a captured response, and
            each section says the request is spent when the checkpoint opens rather than after
            the parser is written. A fixture written after the parser is a transcript of what
            the parser already expects, whichever session writes it.

### 1.2 - the two populations counted rather than described                        2026-09-08
Corrects:   the entry above, which settled the distinction between constituents and names in
            `fixtures/README.md` and left it there. One fact in one place was the right call
            and half the work. A distinction that lives only in prose is not asserted, and this
            one is positioned to be read past: phase 2's sentence says the fixture widens to
            four names and this fixture holds five constituents, so a reader who has not been
            told they are different populations sees an obligation already met. The sentence
            also carried two numbers that nothing checked, which is the drift this corpus
            polices everywhere else, created by the act of writing it.
Built:      `Fixtures.Populations`, which counts both from the folder. Names are the tickers
            with a captured series, read from the `bars-` files. Constituents are the rows the
            membership payload holds, read through the shipped parser rather than by a second
            reading of the same JSON, so a parser that stopped reading a constituent moves this
            figure rather than leaving it agreeing with itself.

            The phase report carries both, on all three surfaces and never as a sum: the
            console line reads "5 constituents and 3 names", the JSON carries them as separate
            fields with a per-folder breakdown, and the HTML gains a table naming the departed
            constituents and the ones with no series. A total would read as one population of
            eight, which is the reading the whole distinction exists to prevent.
Measured:   over the one committed fixture: 5 constituents, 3 names, 2 departed, and the
            constituents with no series are exactly the departed ones.

            That last equality is the property rather than the count. A constituent with no
            captured series is a name the backfill would not fetch, meaning one that has left;
            a current member with no series is a fixture fault rather than a smaller
            population, because the backfill refuses a current member it holds no capture for.
            So the two populations differ in the direction the rule predicts, and the counts
            cannot be made equal by adding a series for a name that is not in the index. The
            other direction is asserted too: a `bars-` file naming no constituent is an orphan
            that would inflate the smaller population against nothing.

            Proved against planted folders outside the repository, not only against the
            committed fixture, which has one shape and would let a counter that returned the
            same number twice pass. Three constituents with a series for two counts 3 and 2;
            giving the departed name a series as well makes the counts agree, which is the
            counter-test that stops the difference assertion from being satisfied by a check
            that always reports one; an orphan series counts 3 and 4; and a folder with no
            membership payload counts 0 constituents with its names still counting, which is
            the gap fixture's shape at 1.5.

            The README's two numbers are asserted against the derived ones. Proved by changing
            the 5 to a 4 and watching the test go red, then restoring it. The permanent proof
            is the assertion: the sentence cannot drift from the folder without failing.
Found:      why the collapse in the entry above matters beyond the assertion that now covers
            it. It was dead from the day it was written. The invented leave date did not create
            the dead stretch, it shortened it, and that is the worse of the two failures.

            A wrong value that widens a dead range makes the assertion visibly vacuous, because
            the date ends up an absurd distance from the boundary it claims to test. One that
            narrows it leaves the assertion looking like it is testing an edge. The invented
            leave put the dead stretch at five months and the assertion one day inside it,
            which reads exactly like a date chosen to fall just after a boundary. The real date
            puts the same stretch at two years and five months. Neither was testing anything,
            and it took moving the date onto the boundary to show it.
Verified:   `tools/ci.ps1` green, 178 tests, up from 175. `tools/verify-phase.ps1` green: 162
            claims, 11 pass, 0 fail, 151 out of scope, 0 unexamined, 18 reconciled against a
            floor of 12. No floor was raised. This entry was written before that run.

### 1.3 - the read surface, the mark renderer and the chart                  2026-09-08
Built:      `ReadApi` in `EquityBrief.Api`, serving a name's bars over a date range, ordered by
            session and otherwise untouched. `MarkRenderer` in `EquityBrief.Web`, the level
            chart mark as a server-rendered SVG string. `SinglePageApp`, the shell that routes
            on the hash and draws nothing itself. Three routes: the shell at `/`, the mark at
            `/marks/level-chart/{ticker}`, and one run log row when the surface starts.

            Migration 4 adding `bar.raw_close`, and the bar adjustment that made it necessary:
            open, high and low are scaled by `adjusted_close / close` so all four prices are one
            adjusted set. `bar-bounds` on the roster, and `read-surface` with it.

Elements:   this is the level chart mark with two of its four elements absent, which the
            checkpoint requires be stated. Present: the candles and the volume pane, on a shared
            time axis. Absent: the moving averages, which arrive at 2.1 with the indicator
            engine, and the level bands, which arrive at 2.4 with the level builder. Both are
            drawn into this same file rather than into a second one, which is the whole reason
            this is not a temporary chart.

Measured:   over the fixture's 3 names holding a series, 756 stored bars. For AAPL, 253 stored
            sessions, 253 candles drawn and 253 volume bars drawn, matched session by session
            against the store rather than counted. Every value the API returns rendered back
            into the storage form and compared string for string against the rows a second
            query read: 253 of 253 identical.

            The report: 165 claims, 20 pass, 0 fail, 145 out of scope, 0 unexamined, 25 tables,
            27 placements and verdicts reconciled against a floor of 20. 202 tests. `tools/ci`
            green on Windows PowerShell and on bash on this machine; the macOS runner is the
            matrix and is carried as it has been since 0.4.

Predicted:  stated before the run. 18 passing claims expected and 20 measured. The two extra are
            the single page app's catalogue and matrix rows, whose due point in `Scope` still
            said 1.6, left over from the ordering `8ac2442` replaced: 1.6 is the corporate action
            checker and has nothing to do with a page. It is one of the fifteen stale due points
            Pass B set out to correct and the one that was not corrected, and it survived because
            it is a written residual rather than a derived one, so nothing compares it to
            anything. Corrected here and the claims reached rather than re-dated.

Floors:     two raised, each with the count expected before the run and the count produced.
            `Reconciliation.Floor` from 12, expected to reconcile about 25 and measured 27, set
            to 20. The passing-claims floor from 6, expected 18 and measured 20, set to 14. Both
            sit below the measured value rather than at it, because a floor set to what the run
            produced is a floor that can never fail.

Derived:    the third derivation of the same figures, and only this one is a defect in stored
            values. The first changed what the fixture was, replacing a seeded walk with captured
            bars. The second changed it again, trimming the captured constituents. This one
            changed what the code did to the fixture: 1.2 stored the provider's adjusted close
            beside its unadjusted open, high and low, so 96 of 756 stored bars carried a close
            outside their own low and high. The first two moved a number because the input moved,
            which is a fixture being corrected. This one moved a number because the arithmetic was
            wrong, which is a defect in what the store held, and it was found by needing to draw a
            candle from a bar rather than by any check asking.

            That is the finding rather than the mixed price set. A bar could be internally
            impossible and no instrument looked, so the repair is `bar-bounds` over every stored
            bar on every CI run, with a planted violating bar as its permanent proof, rather than
            a test beside the arithmetic that happens to produce it.

Resolved:   contradiction D, `Scope.Screens` keyed on the table and the row together, with all 37
            rows of section 15 reconciled against the document in both directions. The sweep it
            promised found the same defect in the measurement: the derived count asked whether a
            subject was absent from the residual list and whether the plan could name it, which is
            a question about capability rather than about which branch ran, and 15 of its 45 were
            section 15 rows answered by a table heading while the plan's prose happened to contain
            their words. Measured by origin the same tree gives 30, so the floor of 40 could not be
            carried and was replaced by a partition: the four origins sum to the claims out of
            scope, and the plan's share is floored at 20 against 50 measured.

            Contradiction F, per element rather than per mark. The row is read as four claims in
            the harness rather than split into four rows in the document, because 15.5 opens by
            stating seven marks over seven rows and a split would leave the document disagreeing
            with itself. Each element is read back out of the row's own description, so an element
            renamed there or invented here fails, and `stated-counts` now reads the opening
            sentence against the table so the split fails too.

Class:      prefix matching, named as a shape to sweep for rather than a defect to fix one
            instance at a time. A matcher keyed on a prefix answers about everything sharing that
            prefix, and it has now arrived four times: the nightly step keys, a subject matched
            without its table, a phase read as landed from any heading beginning with its number,
            and a roster row retired by a heading whose entry said it was not a checkpoint. The
            third was live with no symptom, which is the state that carries a defect past the
            point where it bites: a PROGRESS entry headed `### 2.0 planning` would have read as
            phase 2 having landed and failed every claim still owed at it, and `### 1.1 planning`
            already answered it true for phase 1 with nothing noticing, because nothing is due at
            bare "phase 1". The rule is now in `CLAUDE.md`'s Verification list rather than here.

Proved:     four checks caught defects in this checkpoint's own code before any of it was
            committed. `clock-usage` found a `DateOnly.ParseExact` with a null format provider,
            which parses against the machine's locale. `decision-resolves` found three cited
            decision names that do not exist, all three of them plausible paraphrases of names
            that do. And the reconciliation refused `read-surface` as a verdict's instrument
            until it was a rostered check with a declared reach, which is the fiat guard working
            on the session that wrote it.

Carried:    the fixture's expectations for this checkpoint, to 1.8, where the corpus already
            places phase 1's expectations and where `19.1 What a fixture holds` is owed. What 1.3
            contributes to that obligation is the derived half rather than a frozen figure: the
            drawn candle count is derived from the stored rows on every run rather than compared
            against a number written down once, so the expectation moves when the fixture moves.
            It lives in the suite until 1.8 gives the fixture a place to hold it.

            The macOS runner, as before. And the run log grain question, which this checkpoint
            answered narrowly: the read surface appends one row per process start, stage
            `read-api`, because SCHEMA's grain is one row per run per stage and a row per served
            request would break the key and would grow the operational record by something that is
            not an operation. A later checkpoint that wants a per-request record needs a different
            table, not a looser grain.

### 1.4 - the bar fetcher and the nightly script                             2026-09-08
Built:      `IBulkPriceFeed` and `RecordedBulkPriceFeed`. `BarFetcher`, one bulk request a night,
            stored for current members only, then the sessions that fell out of the retention
            window dropped. `Nightly`, the night's steps in section 14's order, and the `nightly`
            verb behind `tools/nightly` and `tools/nightly.ps1`. `ProviderBarReader`, extracted so
            the two bar feeds share one adjustment rather than carrying two copies of it.
            `nightly-cost` and `nightly-run` on the roster, `bar-append-only` given its owner
            exemption, `two-platform` widened.

Captured:   one request against `eod-bulk-last-day/US`, weight 100 against a 100,000 daily
            allowance, spent before the parser was written rather than after. Stated in advance
            and then made. It returned 10,670 rows for 2026-09-08, and the shape is not the
            historical endpoint's: the name is under `code`, and every row carries the exchange
            it came from. A parser written first would have looked for a ticker field.

            Trimmed to seven rows, chosen so the membership filter has each thing it must reject.
            Three current members are stored. Two constituents that have left, AAL and XRAY, are
            in the file because they still trade, which is what proves the filter reads membership
            rather than the file. Two, A and AA, were never in this index. The session is the next
            trading day after the stored year ends, the Monday between being a holiday, so a night
            appends to a contiguous series.

Measured:   over the fixture's 5 constituents, of which 3 are current members: 3 rows written,
            1 request, and 4 rows in the file not stored. A second night writes 0. Retention with
            the boundary at the fetched session less a year is 2025-09-08, and 0 rows remain below
            it while none inside it is gone. A night by hand: membership 5, backfill 753 over 3
            requests, fetch 3 for 3 members over 1 request, 0 model calls.

            The report: 165 claims, 29 pass, 0 fail, 136 out of scope, 0 unexamined, 36
            placements and verdicts reconciled against a floor of 28. 223 tests. `tools/ci` green
            on Windows PowerShell and on bash on this machine; the macOS runner is the matrix.

Predicted:  stated before the run. 29 passing claims, being 1.3's 20 plus nine of the ten owed at
            1.4. Measured 29. The tenth is the bulk-feed failure row, re-dated rather than reached:
            its "What you see" cell promises a banner giving the data date and tonight's list
            absent rather than wrong, and both are phase 4 surfaces. A claim that something is
            visible is a claim about a surface, which is the same correction "A name leaves the
            index" took at 1.1, found again at the next row that makes one.

Resolved:   contradictions A and H. A was three-way rather than two-way, which is why it survived
            a review: the `bar` note said the fetcher drops old sessions, the ownership row gave
            Delete to the corporate action checker alone, and the exception paragraph said twice
            that a refetch was the only sanctioned removal. Any two of the three read as agreeing.

            The resolution is not that the rule was wrong but that it was about a third thing.
            Retention removes every session below a date boundary for every name at once and
            leaves a contiguous series; a refetch replaces one name's year inside a transaction.
            What the append-only rule forbids is a bar being deleted or edited from inside a
            series that still stands, and that is untouched. `bar-append-only` now reads the
            declared deleters out of SCHEMA rather than carrying a list, so the exemption moves
            with the declaration, and its negative proof plants the same DELETE in a file SCHEMA
            does not name and asserts it still fails. H is the same defect in `news_pulse` and
            took the same resolution.

Widened:    `two-platform`, discharging the obligation the 0.7 review carried here. It asserted
            that the workflow names two runners, which would hold over a workflow whose macOS leg
            was skipped or whose failure was swallowed. It now asserts that no leg can report
            green without running the suite: no `continue-on-error`, no swallowed failure, every
            leg invoking a CI script rather than a bare `dotnet test`, the Linux instrument
            outside the matrix so "both" still means two, and the history fetched on every leg
            that reads it. That last is not decoration: `changelog-reconciles` reads the history,
            and a shallow clone gives it one commit, which is under-reporting on a runner and
            invisible from here.

Proved:     the suite found two defects in this checkpoint's own code. `bar-append-only` failed
            the moment the retention delete was written, which is the check doing exactly what
            the checkpoint text predicted it would. And a second night collided on the run log's
            primary key: the run id was keyed on the date, so re-running a night that failed
            halfway failed on its first step instead, which is precisely what a re-run is for.
            The id now carries the instant, and a caller may name its own.

Carried:    no feed reaches the network. Every provider implementation in the tree is a recorded
            double, so `tools/nightly` takes a fixture folder and a live night cannot run;
            captures have been made by hand at 1.1, 1.2 and 1.4. That is a hole rather than a
            defect in any checkpoint, and it is now in `BUILD_PLAN.md` rather than named only
            here: the HTTP feeds and the path from configuration to request have no checkpoint
            that builds them.

            The fixture's expectations, to 1.8, as at 1.3. The macOS runner, as before.

### 1.5 - gap refusal                                                        2026-09-08
Built:      `TradingCalendar` in `EquityBrief.Core`, which answers which sessions the exchange
            traded and which of them one name is missing. The refusal in `Backfill`: a name whose
            series arrives with an interior session missing is not stored, its stored series is
            left as it was, and the gap's date is named in the run log. `gap-refusal` on the
            roster. The gap fixture, `gap-KEYS.json`.

Measured:   the gap fixture is the KEYS capture with one interior session removed, 2026-03-06,
            which AAPL and MSFT both hold. 253 sessions become 252. Over the three names, one
            name refused and two stored, 0 rows for KEYS and more than 250 each for the other
            two. Over the clean fixture, none refused and all three stored, which is the
            counter-test: without it every assertion above would hold over a backfill that
            refused everything.

            The report: 166 claims, 30 pass, 0 fail, 136 out of scope, 0 unexamined, 37
            placements and verdicts reconciled against a floor of 28. 231 tests. `tools/ci` green
            on Windows PowerShell and on bash on this machine.

Detection:  against the calendar and never against the rows, which is the decision's own
            reasoning and is the part that would have been easy to get wrong. A run of stored
            dates is self-consistent whatever is missing from it: four days of a five-day week
            look exactly like four days of a four-day week, so a series cannot be asked whether
            it is complete. The calendar is observed rather than fetched, being the union of
            session dates across the names in hand, so no call is added to a night to get it.

            One consequence is stated rather than left implicit. Over a single series the union
            is that series, so nothing can be found missing from it. `CanDetect` returns false
            there rather than returning a clean answer, because a clean answer would mean
            "nothing was compared" and would read as "there is no gap".

            And interior only. A name that joined the index in March has no February bars and
            that is a shorter history; a name whose last session is Friday has no Monday until
            Monday's night runs. A gap is a hole with stored sessions on both sides of it.

Decomposed: the failure row rather than re-dated, and the reasoning is worth recording because it
            went the other way first. Its "What you see" cell names two surfaces: the name's
            chart, which exists from 1.3, and its level and plan sections, which arrive at 2.5
            and 3.4. Read as one claim the row is owed at 3.4 and the half that works sits
            unasserted for two phases, which is exactly the argument that resolved contradiction
            F. So it is read as two claims, `chart` at 1.5 and `level and plan sections` at 3.4.

            This was nearly the third failure row re-dated whole, after "A name leaves the index"
            at 1.1 and "Bulk price feed unavailable" at 1.4. Those two are correct because
            neither names a surface that exists. This one does, and re-dating it would have been
            the habit rather than the rule. The distinction: a failure row is owed where the last
            surface it promises exists, and where its surfaces exist at different points it is
            read per surface.

            The element check now reads every cell of the row rather than the description column,
            because the two decomposing tables put their elements in different columns and
            reading one index would have been a rule about column order. Six elements over two
            rows, stated in advance.

Discharged: the news feed obligation, by spending one request. The feed is queryable by date with
            no ticker: a date range with no ticker returns 200 with articles for the whole
            market, every row carries a `symbols` array naming the tickers it is about, and the
            response shape is identical either way. So a night makes one request and attributes
            the rows in code, and the per-name cost of news is zero rather than one call per
            name. The row also carries the article text, so the document a claim rests on arrives
            with the row. Recorded as a decision rather than only as a discharged row.

            The fixture-absent inversion this checkpoint's text also names was already discharged
            at 1.1, where the first input was captured. It is noted rather than redone.

Predicted:  30 passing claims, being 1.4's 29 plus the chart half of the gap row. Measured 30.
            The claim total moves from 165 to 166 for the same reason, which 1.8 has to account
            for when it checks the figure Pass B predicted: the decomposition count is now five
            rather than three, four on the level chart and one added here.

Carried:    the fixture's expectations, to 1.8. The macOS runner. The live feeds, as filed at 1.4.

### 1.6 - the corporate action checker                                       2026-09-08
Built:      `ICorporateActionFeed` and `RecordedCorporateActionFeed`. `CorporateActionChecker`,
            one bulk request per kind a night and a full-year refetch of any current member whose
            adjusted prices an action moved, each name in its own transaction. Migration 5
            creating `series_state`. `corporate-actions` on the roster. The action step added to
            the night, which now runs five of section 14's nine.

Captured:   three probe requests before the parser was written, then two captures. Stated and
            then made. Two of the three shapes would have been written wrong from the endpoint's
            name alone: a split's value is a ratio in a string, `4.000000/1.000000`, not a
            number, and the exchange is under `exchange` where the price bulk file calls the same
            thing `exchange_short_name`, so one reader for both would have silently dropped every
            row of one of them. Four of the dividend row's ten fields arrive as JSON null rather
            than being absent.

            The action is a real one. AAPL's own dividend history was read first to find an
            ex-dividend date inside the stored year, and the bulk file for that session was
            captured whole and trimmed. A day picked at random holds no action for any fixture
            name, which is why the done condition's "captured rather than constructed" needed
            two requests rather than one.

Measured:   over the fixture, 4 dividend rows and 3 split rows, of which 1 is a current member.
            One name refetched, 0 suspect, 2 requests. The refetched year is the window the
            action night defines rather than the stored year, and it is smaller, so a store that
            had added rather than replaced would hold more than either: that is asserted as the
            difference rather than as a count.

            The report: 167 claims, 35 pass, 0 fail, 132 out of scope, 0 unexamined, 42
            placements and verdicts reconciled against a floor of 34. 238 tests. A night by hand
            runs five steps: migrate, membership, backfill 753 over 3 requests, fetch 3 over 1,
            actions 0 over 2. 0 model calls, 6 network requests.

Resolved:   contradiction C, with a table rather than a column. The plan had said a column with
            its grain declared, and that cannot be satisfied as written because grain is a
            property of a table. `series_state` is one row per ticker, which is the grain the
            statement is actually about: whether this name's stored series can be trusted is not
            a fact about a session and not a fact about index membership. One row per ticker
            rather than one per check, because when it happened is in the run log and a second
            history here would be one fact in two places.

            Contradiction I, the source box added. And the per-name limit gained its second
            carve-out, found the same way contradiction B was: the refetch makes one request per
            affected name, which the rule as written forbade. Carved rather than loosened,
            because a night that refetched every name would satisfy a loosened rule and defeat
            the whole design.

Found:      the catalogue row was incomplete, and `component-access` refused the declaration on
            the day the component was written. The row named the actions feed and the bar store
            and named neither the historical feed the refetch fetches from, nor the membership it
            asks which names are members, nor the state contradiction C's own resolution requires
            it to write. That is three omissions in one row, and the refetch cannot happen
            without the first, the filter without the second, or C's resolution without the
            third. Amended with prior text recorded, which is a finding rather than a document
            edited to suit code.

Discharged: the splits and dividends parser obligation 1.2 filed here, by capturing before
            writing. The two shape surprises above are what that obligation was for.

Predicted:  35 passing claims, being 1.5's 30 plus the five owed at 1.6, one of which is the
            store row this checkpoint adds. Measured 35. The claim total moves 166 to 167 for
            that row.

Carried:    the fixture's expectations, to 1.8. The macOS runner. The live feeds, as filed at 1.4.

### 1.7 - the coverage measurement                                           2026-09-08
Built:      `RecordedNewsFeed` and `NewsArticle`, written against a captured response.
            `news-parse` on the roster. `source-lists.json` at the root, the first draft of the
            two open-web lists with a review date. Contradiction J resolved.

Sample:     30 of the 503 S&P 500 constituents, taken at even intervals down the index's own
            weight ranking so the sample spans it rather than being drawn from the top: ranks 1,
            18, 36, 53 and so on to 503, weights from 8.4 per cent to 0.01 per cent. Not chosen
            from a watch list and not chosen by hand. Two weeks, 2026-08-25 to 2026-09-08. 31
            requests, one for the index and one per name.

Measured:   1,253 articles over 30 names in 14 days. Coverage falls with size, and the figures
            are stated per third of the ranked sample rather than as one average, because an
            average over this population would be a statement about the largest names:

              top third,    ranks 1 to 168:    883 articles, median 52 per name, none with zero
              middle third, ranks 169 to 336:  195 articles, median 15 per name, none with zero
              bottom third, ranks 337 to 503:  175 articles, median 13 per name, one with zero

            So the assumption this measurement replaced was right to be doubted: a name in the
            bottom third gets about a quarter of the coverage of one in the top, and one name in
            thirty had no article at all in a fortnight. Tuning anything on the largest names
            would starve the rest of the index invisibly, which is what the checkpoint existed
            to find out.

            Every one of the 1,253 articles carried retrievable text. That is the second half of
            what was asked, and it is a stronger answer than expected: the document a claim rests
            on arrives with the row and needs no second fetch.

Found:      three distinct domains across 1,253 articles, and 1,173 of them are one domain.
            That is not what a publisher count was expected to show and it changes what a source
            list can do. The domain in the payload is the aggregator that carried the article,
            not the outlet that wrote it: the writing outlet appears inside the article text
            where it appears at all, as Business Wire, Globe Newswire or Bloomberg. A domain list
            over this feed would hold three entries and would filter nothing, and filtering by
            domain would discard the whole feed or none of it.

            So the field is named `Channel` in code rather than `Publisher`, and the lists are
            declared to govern the search tool rather than the feed. Naming it `Publisher` would
            have been a wrong statement that every later reader inherits.

Resolved:   contradiction J. Section 17 named two lists and `DECISIONS.md` carried an entry
            called **Three source lists, not one, each with a review date** whose body described
            two and said filings need none. The name was the defect. Superseded rather than
            edited, with the old entry moved to "Previously decided" with its reasoning intact,
            and the new entry carries what the measurement added.

Drafted:    two lists, 13 company-news sites and 12 industry sites, reviewed 2026-09-08 and due
            2027-03-08. Gated sites are kept on the lists and marked, rather than dropped, so a
            later review measures whether they still refuse an automated fetch instead of
            rediscovering the question. The industry list is named as the weaker of the two: the
            licensed feed returned no article from any site on it, so nothing here measured it.

Discharged: the news parser obligation 1.2 filed against this checkpoint, by capturing before
            writing. Two shape facts came out of it: the published instant carries an offset and
            is kept as one, because rounding it to a date at the parse would put evening
            publications on the wrong session, and `symbols` arrives suffixed as AAPL.US where
            every store here keys on the ticker alone.

Asserted:   the parser and not the measurement. The measurement is an observation about one
            provider over one fortnight, not a property of this code, and a test asserting it
            would fail when the news does. It is recorded here with its sample named, which is
            what the done condition asks.

Report:     167 claims, 35 pass, 0 fail, 132 out of scope, 0 unexamined. 244 tests. `tools/ci`
            green on Windows PowerShell and on bash on this machine. No architecture claim falls
            due at 1.7, and that is stated rather than left to look like an omission: this
            checkpoint's deliverable is a measurement and two drafts, and the news feed's own
            catalogue claims arrive with the components that read it in phase 5.

Carried:    the fixture's expectations, to 1.8. The macOS runner. The live feeds, as filed at 1.4.
            The source lists' own review, to 5.0, which the obligations table already carries.

### 1.8 - phase 1 report                                                     2026-09-08
Built:      the fixture's expectations and `fixture-expectations` on the roster. Section 19.1
            read as a claim source rather than placed whole. The citation pass owed since 0.5.

Done:       every claim phase 1 owes is PASS naming an instrument whose declared reach includes
            it, and unexamined is zero. No claim and no placement is owed at any 1.x checkpoint.

Expectations: two files, `expectations/bars.json` and `expectations/membership.json`, each stating
            which of the two kinds it is. That statement is asserted, because a frozen figure and
            a derived one look identical in a JSON file and the difference is the whole point.

            The derived one is the session count. It is the weekdays in the backfill's window
            less the nine named United States market closures that fall inside it: 261 weekdays,
            9 closures, 252 sessions, 756 rows over three names. The file states the rule and the
            check recomputes it, so the two are two derivations of one calendar rather than a
            number and a copy of it. A count frozen from the run would agree with a backfill that
            stored the wrong window, because it would have been taken from that backfill.

            The three properties the bars expectation names in words are asserted rather than
            left as prose: every stored bar could have traded, every one carries its raw close,
            and at least one was actually adjusted, that last so the one-price-set rule is
            exercised rather than trivially true over a year with no action.

Read:       section 19.1 as a claim source. Placed whole and owed at 1.8, it would have been
            asserted with eleven of its thirteen rows describing artefacts that do not exist:
            seven expected outputs across phases 2 to 4, three rejections at phase 5, and a
            fundamentals input at 5.1. That is the same defect as reading a failure row whole and
            it takes the same repair. `bars` and `news` pass; the rest are owed where the
            artefact they describe arrives.

            One thing came out of doing it. The table groups its rows under Inputs, Expected
            outputs and Expected rejections, and each heading is a row spanning the table. A
            claim needs a subject and something said about it, so a row with one cell is a
            heading; the harness now says so and refuses if a second table starts doing it,
            because one table with that shape is known and two is a shape nobody has read.

Predicted:  the claim total, checked here as 1.8's own done condition requires. Pass B predicted
            163 from a base of 160. The measured total is 180. The chain, stated in full rather
            than as a difference:

              160  Pass B's measurement
              162  after 1.1 and 1.2, recorded in 1.2's entry and not accounted for there
              165  the level chart decomposed per element, contradiction F, +3 as predicted
              166  the gap failure row decomposed per surface, 1.5, +1
              167  `series_state` added to the stores table, contradiction C, 1.6, +1
              180  section 19.1 read as a claim source, 1.8, +13

            So the prediction was right about the only movement it foresaw and missed four. Three
            of the four are decompositions taken after it, each with its own reasoning recorded
            at the checkpoint that took it, and one is a store the corpus did not have when the
            prediction was made. The prediction was made before phase 1 was built and could not
            have foreseen a table the harness had not yet been asked to read.

            The +2 at 1.2 is the one movement in the chain nobody accounted for. It is recorded
            here as a gap in that entry rather than reconstructed now, because reconstructing it
            from the diff would be a figure computed afterwards, which is the thing this done
            condition exists to refuse.

Measured:   180 claims, 37 pass, 0 fail, 143 out of scope, 0 unexamined, 26 tables, 43 placements
            and verdicts reconciled against a floor of 34. 251 tests. 32 checks on the roster, 28
            carried. `tools/ci` green on Windows PowerShell and on bash on this machine.

Cited:      39 citations in `ARCHITECTURE.html` against 6 before, covering 29 of the 81 current
            decisions. The obligation created at 0.5 is discharged for the rules phase 1 reaches
            and the remainder is carried to 2.0 with the figure recorded. The 52 uncited are not
            one omission: most settle process, phase order, scope or how the build is run, and a
            citation cannot be placed at a rule the document does not state. What is owed is a
            pass that decides, decision by decision, whether a rule rests on it, and that belongs
            where the sections are being worked on rather than in a sweep at a phase boundary.

Carried:    the live feeds, filed at 1.4 and unchanged: every provider implementation in the tree
            is a recorded double, so a live night cannot run and every capture has been made by
            hand. The macOS runner. The remainder of the citation pass, to 2.0. The source lists'
            review, to 5.0.

Not:        this entry does not sign phase 1 off. Sign-off is owed on the phase as a whole before
            phase 2's plan, by a session that has committed no code to it, and this session has
            committed code to every checkpoint in it.

### Phase 1 sign-off                                                         2026-09-08
Signed by a session that has committed no code to this repository. Its only commit is this
            entry, which is a document, so the fresh session rule permits it. Every checkpoint
            from 1.1 to 1.8 was committed by other sessions, and the 1.8 entry says in its own
            last line that it does not sign the phase off.
Verified:   by re-running rather than by reading the 1.8 entry. `tools/ci.ps1` green end to
            end, all 6 steps, 251 tests passing inside it, exit 0. `tools/verify-phase.ps1`
            green at 180 claims, 37 pass, 0 fail, 143 out of scope, 0 unexamined, 25 tables,
            43 placements and verdicts reconciled against a floor of 34, fixture PRESENT with
            1 captured over 5 constituents and 3 names, 32 checks on the roster and 28
            carried. Windows PowerShell on this machine, at commit f48d9d6.
Corrects:   the 1.8 entry's Measured line, which states 26 tables. The run at that same commit
            reports 25, every entry before it reports 25, and every other figure on that line
            reproduces exactly. The figure is a transcription error and the count is 25.
Counts:     by verdict, over the 180 claims in `artifacts/phase-report.json` at f48d9d6: 37
            PASS, 0 FAIL, 143 OUT OF SCOPE, 0 unexamined. The 37 by the instrument that
            reached them: `component-access` 13, `schema-columns` 5, `corporate-actions` 4,
            `nightly-cost` 4, `read-surface` 3, `nightly-run` 3, `architecture-conformance` 2,
            `fixture-expectations` 2, `gap-refusal` 1.

Reconstructed: the +2 at 1.2, which the 1.8 entry records as the one movement nobody accounted
            for and declines to reconstruct. It is the Backfill becoming a component: one row
            in section 7's component catalogue and one row in the read and write matrix, both
            added by a68a504. Established three ways that agree. The diff of
            `docs/ARCHITECTURE.html` across that commit is 4 insertions and 2 deletions, and
            the two deletions are rewrites of an existing note and an existing limits row,
            leaving those two rows as the only new claim-bearing lines. A re-implementation of
            the harness's own counting rules over every phase 1 commit gives 160 before and 162
            after, with 2 added and 0 removed, by multiset difference so a removal could not
            hide inside a net figure. And the 1.2 entry already records the cause under Found,
            that the backfill had no catalogue row and was made a component there. The shape
            was accounted for once already: at 1.1 planning the mark renderer supplied one
            catalogue claim and one matrix claim, and that entry said so.
            The reason the 1.8 entry gives for leaving it does not hold. Done condition 8
            refuses a prediction written after the run it predicts, because a forecast made
            afterwards is not a forecast. It does not refuse explaining where a figure already
            measured came from, which is what every other step of that chain is. The movement
            was measured at the time and only its cause was missing, so recovering the cause
            adds nothing to the prediction and closes the chain.
Also:       the pass movement at 1.2, 8 to 11, which no entry accounts for either. Two of the
            three are the Backfill rows and the third is the bar store claim turning from out
            of scope to PASS as migration 3 landed.

Ratio:      37 of 180 passing, against 4 of 158 at phase 0 close. Passing claims added per
            checkpoint, from each entry's own Measured line: 1.1 +4, 1.2 +3, 1.3 +9, 1.4 +9,
            1.5 +1, 1.6 +5, 1.7 +0, 1.8 +2, over a claim total moving 160, 162, 165, 165, 166,
            167, 167, 180.
            Of the 33 new passes, 28 came from code a check could then reach, 1 from the
            harness reading more on its own, and 4 from both together. Of the 20 new claims,
            17 came from the harness reading more. The two columns answer differently and both
            answers hold: code is what turns a claim green, reading is what finds claims, and
            in this phase reading found them faster than code turned them.
            The figure that settles it is neither percentage. Out of scope moved 154 to 143, a
            fall of 11, while the build retired 27 pre-existing out-of-scope claims and the
            harness added 16 new ones. 16 of the 27 was given back. Held on the 158 that
            existed when the phase opened, out of scope is 127 of 158, so the growing
            denominator flatters the headline by about one point and in the opposite direction
            to the one suspected. The convergence is real; the quantity of unasserted
            architecture is what barely moved.
Spot check: ten of the 143 out-of-scope claims drawn at random, seeded on the date so the draw
            is reproducible, each read against its named due point and that point's text in
            `BUILD_PLAN.md`. 5 of the 10 name a point that does the work.
            The split is the finding rather than the score. All 4 that name a checkpoint are
            right, clause for clause: Listings at 4.4, News pulse at 4.5, Staleness judge at
            5.7, Volume profile builder at 2.3. Of the 6 that name only a phase, 1 lands right
            and does so by coincidence, the nightly wall clock at phase 4 where 4.1 happens to
            be the checkpoint that measures it. The other 5 resolve earlier than the work:
            Dates and sources is 5.5, Reason record display is 6.5, the register row refusal is
            6.3, no eligible band is 3.2, and the per-name nightly step is not complete until
            4.4.
            Early is the direction the corpus names as never safe. `HasLanded` resolves a
            phase-only due point against any non-planning checkpoint in that phase, so a claim
            saying phase 6 falls due at 6.1 and fails there. 74 of the 143 name a phase rather
            than a checkpoint: 8 at phase 2, 5 at phase 3, 20 at phase 4, 29 at phase 5, 12 at
            phase 6. `CLAUDE.md` says an out-of-scope claim names the checkpoint that ends it,
            and `Scope.cs` accepts either, which is two documents disagreeing rather than an
            oversight in any one map entry.
            The nearest instance is dated. All 8 phase 2 claims fall due at 2.1, and read
            against phase 2's checkpoints, 1 of the 8 is 2.1 work, the failure row for fewer
            than 200 bars, which is 2.1's own done condition. The other 7 are owed later: the
            swing lookback at 2.2, the volume profile mark at 2.3, the level window and the
            band merge distance at 2.4, the momentum panel at 2.5, the six-store row not
            complete until `move` at 4.2, and the distance row whose surfaces are the Universe
            and Tonight screens. Recorded here and repaired nowhere: nothing in phase 2's plan
            is touched by this entry.

Broke:      three passing claims, each reached by a check whose declared reach was written
            during this phase and none of them already carrying a permanent negative proof.
            Every mutation was made in an isolated worktree at f48d9d6 and reverted, and the
            main tree was not modified at any point.
            One went red. Deleting the membership filter from `BarFetcher` falsifies the
            section 14 step "store the bars for current members", and `nightly-run` fails at
            its own assertion with 5 of 251 tests red. That PASS is load bearing.
            Two did not.
            Removing the transaction from the corporate action refetch, so the year is dropped
            and reinserted in autocommit, leaves 251 of 251 passing. The two tests that claim
            atomicity both induce their failure upstream of the only destructive statement, an
            empty history feed that throws before the delete runs, so neither can observe a
            half-replaced series. 1.6's done condition says the replacement is atomic, and the
            code is atomic; what is absent is any assertion that would notice if it stopped
            being.
            Changing the bars expectation to name a traded session a market closure and to drop
            a real one, 2026-07-06 in place of 2026-07-03, leaves 251 of 251 passing. The
            derivation recomputes weekdays and sessions from a window and a closure list it
            reads out of the file it is checking, then pins the results to the literals 261 and
            252 and the closure count to 9. So a wrong total or a wrong window goes red, and a
            wrong set of nine dates does not. All three captured series hold a bar for
            2026-07-06 and none for 2026-07-03, so the fixture already carries the refutation
            and nothing reads it. 1.8's done condition asks for an expectation derived rather
            than frozen; the count is derived and the calendar it derives from is checked
            against nothing.

Found:      the finding that outranks those three, and it is about the instrument this entry is
            written against. `tools/verify-phase` runs no check. It is one `dotnet run` of the
            report generator, which parses `ARCHITECTURE.html`, reads section 14's list and the
            fixture folder, and assembles the report from verdict literals in `Harness/Scope.cs`.
            `Verdict.Fail` is assigned nowhere in that path, so the "fail 0" line is structural
            rather than measured and cannot take another value, and green means nothing is
            unexamined rather than that anything held.
            Shown rather than argued. With the membership filter deleted and 5 tests failing,
            `tools/verify-phase` printed the identical block, 180 claims, 37 pass, 0 fail, 143
            out of scope, 0 unexamined, exit 0, and the claim about storing bars for current
            members still read PASS by `nightly-run` with its note about current members
            intact.
            This is not a hole in the property. The suite asserts, `architecture-conformance`
            reconciles that a passing claim names a check whose declared reach covers it, and
            `tools/ci` is red the moment a property breaks, so the two instruments run together
            do give the guarantee. What is wrong is the description. `CLAUDE.md` says
            verify-phase runs the pipeline over the committed fixture, diffs every stage's
            output against frozen expectations, and asserts each claim against the code, and it
            does none of those three. A green phase report is a statement about scope and
            attribution and never about behaviour, and neither surface it writes says so on its
            face.

Feed gap:   no feed reaches the network. All five provider implementations are recorded doubles
            reading a file, `Nightly.RunAsync` constructs them inline and takes a fixture folder
            as a required argument, and `tools/nightly` passes one positionally with a default.
            There is no configuration switch and no injection seam. Every capture in `fixtures/`
            was made by hand, at 1.1, 1.2, 1.4, 1.6 and 1.7, which is two more checkpoints than
            the obligation paragraph in `BUILD_PLAN.md` records.
            What a live feed owes beyond the double, each verified against the tree.
            The HTTP client and a live class per feed, of which there is not one line: no
            `System.Net.Http`, no client, no base address and no URL construction in any
            shipped file.
            `INewsFeed`, which does not exist, so news is the one feed whose request count is
            not forced by an interface the way the other four force it.
            The credential bind. `ProviderCredentials` names the key once, refuses a blank and
            withholds itself from a log, and is constructed nowhere outside its own tests; both
            configuration call sites read only the data root. `RUNBOOK.md` already states that
            a blank or missing key is refused by name at startup, and no startup path reads the
            key at all.
            Retry, backoff, a per-request timeout and a night-level deadline, none of which
            exists and none of which any document names, with no cancellation source to thread
            because `Nightly.RunAsync` takes no token while every feed interface accepts one.
            A definition of unavailable, which the failure table promises a behaviour for under
            "Bulk price feed unavailable" and never defines, together with the rows it has none
            of: a rejected rate, a slow answer, a truncated payload, and a response carrying the
            wrong session. The last of those is the failure that reports green, since a night
            answered with yesterday's bulk file logs one request and no error.
            The `nightly-cost` carve-out. Its coverage half bans `System.Net.Http`,
            `HttpClient`, `WebClient`, `HttpRequestMessage` and the socket constructor across
            every shipped file outside the test project, so the first live feed turns a green
            check red on the day it is written. The carve-out has to name what may hold a
            client, or scope the scan to the components section 14 lists, rather than delete the
            patterns, and it belongs in its own commit ahead of the first feed rather than
            beside it.
            Paging, which falsifies the limits row wording "bars arrive in one bulk file and
            news in one feed request" the day a news request pages, and forces that row to be
            restated as a limit that does not grow with the universe rather than as one request.
            The weighted-call budget, stated in `RUNBOOK.md` as 100,000 with per-endpoint
            weights, read by no code and pinned by no check, beside a `run_log.spend` column no
            component writes.
            Idempotency under a retry. `RUNBOOK.md` and section 14 both promise it, and today it
            is a claim over a file that is always fully present. Over a wire it becomes a claim
            about a retry landing inside the delete-and-reinsert refetch path SCHEMA declares,
            which is the one place a partial write can cost stored bars.
            And the schedule as a UTC instant. Section 14 ends by saying the run is scheduled
            with Task Scheduler after the US close, naming a Windows-only mechanism and a
            local-relative time against two hard rules, where `RUNBOOK.md` says the platform's
            scheduler in UTC. Neither states the instant the provider posts the bulk file, which
            live is what decides whether a night fetches tonight's session or last night's.
Belongs:    phase 2 can be built without it, and that is why it has survived this long: phase 2
            computes over stored bars and does not care where they came from. So phase 2 is the
            cheapest place to put it and not the place that forces it.
            The first checkpoint that cannot be built without it is 3.1, whose deliverable is
            itself a live nightly fetcher for the calendar, against an endpoint never probed,
            with no interface, no double, no capture, no `Feed` enum member and no step in
            section 14. 4.1 is where the wall clock and the weighted-call budget stop being
            decorative. 4.7, a week of unattended nights, is the done condition no number of
            hand captures can satisfy, and 4.4's deferred "Bulk price feed unavailable" row
            already rests on work no checkpoint owns.
            The recommendation is that it is settled at 3.0 at the latest and built at the head
            of phase 3 ahead of the calendar fetcher, because a calendar fetcher cannot be
            specified before it is settled what a fetcher is, and 3.0 already owns contradiction
            E and the missing calendar component. Doing it inside phase 2 instead is a
            defensible scheduling choice and buys the same thing earlier, while nothing depends
            on it. Either way it wants two rows rather than one: a carried obligation created at
            1.4, and a specification hole, the second because the holes table is what forces a
            `DECISIONS.md` entry where the obligations table forces only a date. Named here and
            written into `BUILD_PLAN.md` by the pass that plans the phase that takes it, since
            neither table is this entry's to edit.

Green:      what it does not mean, for this phase. A night has run end to end and no night has
            run against a provider: every feed is a recorded double reading a file a person
            saved by hand, and `tools/nightly` will not start without being told which folder to
            read. The store this repository creates holds 3 names and 5 constituents, against a
            system specified for about 500. Every figure in the phase report is about the build,
            computed from the corpus and the fixture and never from a store a night wrote, by a
            tool that runs no check and cannot print a failure. The suite is what asserts, and
            it stayed green through two of the three properties this entry removed by hand. What
            phase 1 has established is that a chart can be drawn from stored bars, and that the
            corpus and the code agree about what exists. It has established nothing about a
            night nobody watched.

Carried:    out of phase 1, each with the point that ends it.
            The live feeds and the credential path, created at 1.4, due at 3.0. The largest by
            some distance, and the only one that is a hole rather than a repair.
            The atomicity of the corporate action refetch, asserted by nothing, created here,
            due at 2.0 as a suite repair rather than a phase 3 item, because the code is correct
            today and only the assertion guarding it is missing.
            The closure list in the bars expectation, checked against nothing, created here, due
            at 2.0. The captured series already carry the answer and the check does not read
            them against it.
            The description of `tools/verify-phase` in `CLAUDE.md`, which claims three things
            the tool does not do, created here, due at 2.0. Either the tool runs the suite first
            or the sentence is corrected and both report surfaces state that they presuppose a
            green one.
            74 out-of-scope claims naming a phase rather than a checkpoint, of which 8 fall due
            at 2.1 and 7 of those 8 are owed later than 2.1, created here, due at 2.0.
            Unchanged from the 1.8 entry: the remainder of the citation pass to 2.0, the macOS
            runner, the source lists' review to 5.0, and the volume shelf threshold to 2.6.

Verdict:    phase 1 is signed off. Eight checkpoints landed, `tools/ci` is green at 251 tests,
            the phase report is green on everything it can assert, unexamined is zero, and no
            claim passes without naming an instrument whose declared reach includes it. The
            findings above are carried with due points rather than repaired here, because none
            of them falsifies shipped behaviour: the refetch is atomic, the session count is
            derived, and the night does what section 14 says it does. What each of them
            falsifies is a claim about how well that is known, and keeping those two apart is
            the thing this corpus exists to do.

### Repairs to 0.7 - verify-phase had no failing branch                      2026-09-08
Not a checkpoint entry. It belongs to 0.7, which built the harness and has landed. This is a
            defect in phase 0's instrument found at phase 1's sign-off, and it lands on its own
            branch before phase 2's planning pass opens rather than waiting for 2.0, because
            2.0's own verification would otherwise be signed off by an instrument known to have
            no failing branch.
Defect:     `Verdict.Fail` was assigned nowhere in the report path. Every verdict came from
            `Harness/Scope.cs`, a map naming the instrument that reaches each claim, and the
            report printed PASS from the naming without asking whether that instrument had run.
            So the "fail 0" line was structural rather than measured and could not take another
            value, and green meant no claim was unexamined rather than that any claim held.
            Unchecked from 0.5, where the harness first read the architecture, through 1.8.
Proof:      on record from the sign-off two entries above rather than constructed for this
            entry. Deleting the membership filter from the bar fetcher left five tests failing,
            and `tools/verify-phase` printed an identical block, 180 claims, 37 pass, 0 fail,
            143 out of scope, 0 unexamined, exit 0, with the claim about storing bars for
            current members still reading PASS by `nightly-run` and its note about current
            members intact.
Repaired:   three changes, in the order the defect needs them.

            The tool reads a run. `tools/verify-phase` now runs the suite first with a trx
            logger and hands the result to the report, and the report gives a claim PASS only
            where the check reaching it ran and held, FAIL where that check ran and did not
            hold, and UNEXAMINED where it did not run. That is section 19.3's own table, which
            has said exactly this since it was written; what was missing was any code reading
            it. The result file is deleted before the suite runs rather than overwritten by it,
            because a result left from an earlier run is worse than none: the report would read
            it, find every check passing, and print green over a tree whose suite had failed to
            build.

            The outcome is applied after the reconciliation and not before. Whether a
            declaration is used by a verdict is a question about the map and has to answer the
            same on a run where every check failed as on one where every check passed. Applying
            outcomes first made the reconciliation refuse every declaration the moment a check
            went red, which is the first version of this repair failing on its own first test.

            And the not-green line names both counts. It said a phase is not done while anything
            is unexamined, which was the only reason the report could be red while fail could
            not be above zero. A dead branch made a wrong message harmless.
Proved:     five new tests, and the two that carry the property are asserted over the written
            file rather than over the model, because the surface was the defect the last time
            this class appeared. A constructed run in which one check fails writes a report
            whose JSON reads fail equal to the number of claims that check reaches, green false,
            and pass reduced by the same number. A run that produced no result writes pass 0,
            fail 0, unexamined above zero and green false, with out of scope unmoved, since out
            of scope is a statement about the plan rather than about a run. The trx reader is
            asserted over a constructed file for all three outcomes. The carrier of every check
            is asserted to own its tests and no others, in both directions, because the match is
            on a type's full name and a prefix key answers about everything sharing it: on the
            class name alone "Store" would answer for "StoreWrites" and two checks would share
            one result. And every check a passing claim names is one the result can answer for.
Measured:   over the 180 claims of the report at this commit, before the repair and after it.
            Stated before the run: 37 read PASS before. 37 read PASS after, and no verdict
            changed. That is the outcome to expect rather than a disappointment: every carried
            check passes on this tree, so a report that reads the run agrees with one that
            assumed it. The repair is not visible in the verdicts and is visible in what happens
            when a check fails.
            The new line the report prints: 28 checks passed, 0 failed, 0 did not run, over the
            28 the roster carries of 32.
            Against a constructed run with `nightly-run` failing: 34 pass, 3 fail, 143 out of
            scope, 0 unexamined, green false, exit 1, each FAIL row naming the failing test.
            Before the repair the same input produced 37 pass and 0 fail.
Tests:      256, up from 251. `tools/ci.ps1` green end to end, all 6 steps.
Sign-offs:  what this does to the two already written, which is less than it looks and worth
            stating rather than leaving to be worked out. It reverses neither. The suite carried
            the assertions in both cases and the suite was green in both, so the properties held
            when each was signed and hold now. What it does mean is that every phase report's
            fail count was uninformative, phase 0's and phase 1's alike, and that a reader who
            took "0 fail" as a measurement was reading a constant. Both sign-offs carry a
            paragraph on what green does not mean, and both were true for a reason neither of
            them gave: phase 0's said the report was green because 154 of 158 claims were out of
            scope, and phase 1's said every figure was computed from the corpus and the fixture
            rather than from a store. Neither said the fail count could not have been anything
            else.
Notes:      the table this repair satisfies was placed against `architecture-conformance` from
            0.5, and that check asserted the three verdict names matched the document and never
            that the harness could produce the second of them. A table can be covered by an
            instrument that reads its vocabulary and not its content, and this is what that
            looks like.

### Addendum to the repairs above - the first version of the repair was defeated   2026-09-08
Not a checkpoint entry. It belongs to 0.7 with the entry above, which it corrects rather than
            edits. An adversarial review was run against `7372d8b` before it merged, and it
            reproduced the original defect against the repair. Recorded because a repair to
            something that could not fail is the repair that can silently not work, the entry
            above says so in those words, and it was true of the entry above.
Defeated:   two ways, both reproduced end to end rather than argued.

            A skipped test. `SuiteOutcomes.ResultOf` asked whether any result said "Failed" and
            took a passing sibling as the answer otherwise, so every other outcome the format
            carries read as a pass: `NotExecuted`, `Error`, `Timeout` and `Aborted` alike. With
            the membership filter deleted from the bar fetcher and two carrier tests marked
            `[Fact(Skip = ...)]`, `tools/verify-phase` printed 37 pass, 0 fail, green and exit
            0, with the claim about storing bars for current members reading PASS over code
            storing every ticker. Two attributes were the whole distance between the repair
            working and not working, and `dotnet test` exits zero on a skip as well.

            A failing test in a class carrying no check. Green was computed from the claim
            counts alone, and 83 of the suite's 257 tests sit in the eleven classes that test
            components rather than carry a roster check. Changing `Money.FromStorage` to accept
            a group separator, which is the hundredfold price error the comment above that line
            warns about, left one test red and the report green at exit 0.
Repaired:   passing is now the only outcome that counts as passing, and everything else is read
            against it rather than listed, so an outcome the format adds later fails safe. Not
            run and run badly are kept apart: a carrier whose rows are all `NotExecuted` did not
            run, one carrying an `Error` or a `Timeout` ran and did not hold, and any test of a
            carrier skipped makes that check not run even beside a passing sibling.

            And the report reads the run's own tally from the trx's `Counters` element beside
            the carried checks, because they are two populations and neither subsumes the other:
            a check can fail with no claim attached, and a claim can go unchecked in a run that
            reported no failure. Green is now four conditions rather than two, and it is stated
            once on the model where it had been written out twice, in the command returning the
            exit code and in the writer stamping the artifact.
Surfaced:   the run is on both artifacts rather than only in the scrollback. The JSON carries a
            `suite` object with the four counters and the count of carried checks not passing;
            the page carries a paragraph saying what the run did. A claim that something is
            visible is a claim about a surface, and terminal output is not one.
Widened:    `TheCarrierOfEveryCheckOwnsItsTestsAndNoOthers` said both directions and asserted
            one. It built its list of tests by reflecting over the carriers, so both loops asked
            only about tests the carriers already declared, and the question that matters,
            whether a test is owned at all, could not be reached. It now reads every test in the
            suite, counts the unowned against a floor of 40 rather than assuming zero, and
            asserts at most one owner rather than exactly one. This is the guard whose scope was
            narrower than its own prose, which is the defect this corpus calls the most common
            in its class, found in the commit that repaired the same shape one level up.
Corrected:  four claims in the CLAUDE.md passage the entry above rewrote, each wrong and each
            checked once, which is worse than not checked. Green was said to mean every claim
            was checked and held, false for the 143 of 180 that are out of scope. The committed
            expectations were called frozen, where both files declare in their first field that
            they are derived from the rules and where the same document twice uses frozen as the
            disqualifying kind. The map and the PASS-by-fiat were dated from 0.5, where the
            record shows 0.5 and 0.6 reporting 0 pass, 120 unexamined and exit 1, so the tool
            ran no check from 0.5 and printed PASS from a name only from 0.7. And a report
            stamped with its generation instant on both surfaces was called byte-identical.
            Prior text to `CHANGELOG.md` in two entries.
Measured:   over the 180 claims at this commit. 37 read PASS, unchanged from before either
            version of the repair, and no verdict moved: every carried check passes on this tree
            and the run is clean, so a report that reads the run agrees with one that assumed
            it. What changed is what happens when it is not.
            Against the two runs that defeated the first version: the skipped-test run now gives
            3 unexamined, 1 carried check not run, notExecuted 1 and exit 1; the failing test
            outside every carrier gives exit 1 where it gave 0.
            Over the suite, 257 tests of which 173 sit in a carrier class and 83 do not.
Tests:      257, up from 256 at the first version of this repair and 251 before it. Six new in
            total. `tools/ci.ps1` green end to end, all 6 steps.
Carried:    one, and it is section 19.3's own words. That table says a failure shows the diff
            beside it, and what the report shows is the failing test's name and its message. For
            most checks here that message is an `Assert.True` sentence carrying no expected and
            no actual, only the first failing test of a carrier is named, and the message renders
            in a plain cell. Either the report renders expected against actual, or 19.3 is
            amended to say the failing test and its message with prior text to `CHANGELOG.md`.
            Due at 2.0, with the rest of the citation pass. It is filed here rather than left in
            a review, because the table is placed as wholly asserted by `architecture-conformance`
            and this is one clause of it that no assertion reaches.
Notes:      the review that found all of this was run against the repair before it merged, by
            agents given the commit and told to break it. The two defeats were reproduced in
            worktrees rather than reasoned about, which is the only reason they are in this
            entry rather than in a later one.

### 2.0 planning - the phase remap                                           2026-09-09
Not a checkpoint entry. It belongs to 2.0, which has not landed. `BUILD_PLAN.md` places a phase
            planning pass at that phase's opening checkpoint, and this is the first half of it:
            the renumbering alone, so the substantive pass that follows is readable as a diff
            rather than buried inside one.
Remapped:   the feed work becomes phase 2, and levels, the plan, tonight's list, research and the
            loop become phases 3 through 7. Every checkpoint moves with its phase: what was 2.1
            is 3.1 and what was 6.8 is 7.8.
Why:        no feed reaches the network. Every provider implementation is a recorded double, and
            three of the fixture's names were captured by hand from endpoints no code has called.
            Building the level work first means deriving every indicator, swing, profile and
            level expectation from that fixture, and then calibrating the shelf threshold, the
            merge distance and the profile window over it. 1.2 found a membership parser reading
            a field the provider does not send, which the fixture had agreed with for two
            checkpoints because the same session wrote both. That failure is available again
            here, one layer down and across four stages at once.
Why not 1A: `Reconciliation.Order` parses a phase with `int.Parse`, so a phase has to be an
            integer and "1A" throws. `DuePoints.Built` recognises a landed checkpoint by a
            leading digit, dot, digit, which never matches a heading beginning "1A.1", so such a
            checkpoint could not register as landed and every claim due at it would stay out of
            scope forever. Admitting it means changing `Order`, `PhaseOf`, `Compare` and `Built`,
            which are the primitives deciding what landed and before mean, to accommodate a name.
            Renumbering keeps every phase an integer and the order total, and its failures are
            loud: `InThePlan` refuses a due point the plan does not have, `HasLanded` refuses one
            already recorded, and the reconciliation runs both directions.
Stale:      every forward reference in an entry above this one was stale rather than wrong when
            written. Each was correct under the numbering in force on its own date, and this
            remap is what made it point elsewhere. So a reference to phase 2 in an entry dated
            before today means the level work, and the same words in an entry dated after today
            mean the feeds. An entry's own date is what says which side of the remap it came
            from, and nothing else does.
Moved:      89 checkpoint tokens and every phase word in `BUILD_PLAN.md`. 40 due points, every
            phase word and one prose range in `Scope.cs`. Four roster rows in `CLAUDE.md`. Two
            checkpoint references and five phase rows in `ARCHITECTURE.html`.
Not moved:  the eight tokens in `ARCHITECTURE.html` that look like checkpoints and are not.
            Figure 5.1 is named twice and sections 6.1 through 6.6 are headings, so a blanket
            shift would have renumbered all eight silently and left the document referring to
            sections that do not exist. They were found by reading every token in context rather
            than by trusting the pattern, which is the only reason they survived. Ten tokens
            matched the shape and two of them were the ones meant.
            And `CLAUDE.md`'s commit-subject example, which reads `Phase 2 / 2.0` for the pass
            that writes phase 2's section. It is an illustration rather than a due point and it
            is still true, because that is the subject this commit carries.
Gap:        `BUILD_PLAN.md` and section 20 now run 0, 1, 3, 4, 5, 6, 7 with nothing at 2. The
            commit after this one writes phase 2 into that gap. Nothing is due at a bare phase 2
            today, so no check asks for it in between.
Tests:      257, unchanged by a pass that renames due points and builds nothing.

### Correction to the remap entry above - the sweep was scoped by hand         2026-09-09
Not a checkpoint entry. It belongs to 2.0, like the entry it corrects, and it is written as a
            new dated entry rather than as an edit because `PROGRESS.md` is append only.
Corrects:   two sentences in the entry above. "Moved" lists four files and the remap touched
            four files, which was true and was not the same thing as complete. And "its failures
            are loud: `InThePlan` refuses a due point the plan does not have" is wrong as a
            general claim, for the reason below. The rest of that entry stands.
Found:      46 stale references across 18 files, none of them in the four the first pass swept.
            The first pass chose its files by reading the remap and listing what it thought
            carried numbers. This one derived them: every tracked text file except the two
            append-only records, scanned for a checkpoint token or a phase word, 349 matches in
            26 files read in context. The difference between the two counts is the whole
            finding, and it is the same shape as the shape this corpus has now met five times:
            a population chosen by hand is a population whose gaps are invisible.
Quiet:      five of the misses were live due points in `PhaseReport.Placed`, which is where a
            table too broad to be one claim is placed with the checkpoint that ends it. Section
            11 was owed at 4.1, the lane table at 5.0, and sections 13.2, 13.3 and 19.2 at 6.1.
            Every one of those still exists after the remap and none of them has landed, so
            `InThePlan` had nothing to refuse and `HasLanded` had nothing to catch: the report
            stayed green at 143 out of scope while five claims pointed at checkpoints doing
            different work. A renumber goes loud only where it produces a due point the plan
            does not have. Where the plan has a checkpoint at every number, it goes quiet, and
            that is every renumber of a contiguous range. They are now 5.1, 6.0 and 7.1.
Not moved:  a dated record keeps the numbering in force on its date. That was applied to
            `PROGRESS.md` in the entry above and not to the two other places the corpus keeps
            one, so the first pass rewrote two rows of `ARCHITECTURE.html`'s section 23, which
            is the dated record of what each version of that document said. A row dated
            2026-09-05 now says something that version did not say. Both are reverted, and the
            same rule leaves the superseded entry in `DECISIONS.md` under "Previously decided"
            alone: it carries the date it was superseded, which is what tells a reader which
            numbering it used. Live text shifts, a dated record does not.
Also:       `docs/DECISIONS.md`, `fixtures/README.md` and `source-lists.json` are none of them
            specs, so none is in `CHANGELOG.md`, and each carried a reference nothing else
            would have caught. `source-lists.json`'s was a review owed at 5.0, which is now
            6.0, and it sits in a JSON string that no grep over the documents would reach.
Constructed: the fabricated `PROGRESS.md` fragments inside `ArchitectureConformance` were moved
            too, though their numbers are the test's own data and no assertion depends on which
            they are. One of them read "### 2.1 - the indicator engine and the averages on the
            chart", which is a statement about the plan whatever the code around it does with
            it, and a reader has no way to tell a constructed number from a cited one.
Claims:     180, 37 pass, 143 out of scope, 0 unexamined, all unchanged. Predicted before the
            run and for a stated reason: every due point this repair moved went from an unlanded
            checkpoint to another unlanded checkpoint, so no claim crosses the scope boundary in
            either direction. A change in the figures would have meant the repair moved
            something it was not meant to.
Tests:      257, unchanged.

### 2.0 planning - phase 2 written into the gap                              2026-09-09
Not a checkpoint entry. It belongs to 2.0, which has not landed. This is the second half of the
            planning pass: the remap left `BUILD_PLAN.md` and section 20 running 0, 1, 3, 4, 5,
            6, 7 with nothing at 2, and this writes phase 2 into that gap.
Built:      eight checkpoints, 2.0 to 2.7, in `BUILD_PLAN.md`. Six holes added to the holes
            table, all settled at 2.0. Three contradictions added, K, L and M. A phase row in
            section 20. Contradiction M resolved in the same pass, since it is one sentence.
Visible:    2.1. The chart from 1.3 drawing a bar the provider sent tonight rather than one a
            capture holds. Every phase opens with something to look at and this one can, because
            the surface already exists and only the source of the bar changes.
K:          section 16's matrix puts eight components' write one column to the right of the store
            their catalogue row names. The plan that opened this pass said three rows and named
            the indicator engine, the swing finder and the volume profile builder. Parsing the
            table against the catalogue rather than reading it found eight: those three plus the
            move annotator, the shortlist builder, the facts assembler, the forward return filler
            and the news pulse counter. Level builder, ladder builder and fundamentals fetcher
            are correct, which is what makes it a displacement rather than a convention somebody
            meant. It is invisible today because `component-access` reaches a matrix row only
            when its component exists in code, and none of the eight does.
            All eight are repaired at 3.1 rather than three there and five in phase 5. One defect
            with one cause, and the alternative leaves four cells known wrong in a spec across
            two phases while the check that would catch them grows toward them.
L:          found with K and kept apart from it. The shortlist builder's row disagrees with its
            catalogue row in its reads as well as its write: the row reads fundamentals and news
            pulse, the catalogue names levels, indicators, ladders, calendar and facts. K has one
            answer per row, read straight off the Writes cell. This one does not, because what
            the component reads is decided by section 11's six reasons, so it waits for 5.4.
M:          section 14 closed by scheduling the night with Task Scheduler after the US close.
            That is a Windows-only mechanism against one hard rule and a local time against
            another, in one sentence, in the section the nightly run is specified in. The
            sentence now states a UTC instant set after the provider posts the day's bulk file
            and names no scheduler, because scheduling lives outside the application.
Shadowed:   the suite refused the first version of this pass, which is the outcome it exists for.
            Writing a checkpoint whose text names "Bulk price feed unavailable" gave the plan a
            derivation for a subject `Scope.cs` also answered by hand, and
            `EveryDuePointThePlanSuppliesIsReadFromThePlan` failed on the shadowing. The row is
            now a declared exception under `DerivedIsEarly`, carrying the same 5.4 and the same
            reason: the plan derives 2.3 because 2.3 decomposes the row, and the claim is owed at
            5.4 because the row's "What you see" cell promises a banner and tonight's list.
Prediction: 187 claims and 44 PASS at the end of phase 2, from 180 and 37 today. It is derived
            rather than estimated, because section 18's gap row already shows what a
            decomposition does to the count: 19 rows in that table produce 20 claims, the gap row
            being two.
            Seven new claims, named rather than counted, because a prediction of "seven" cannot
            be missed legibly. An eighth found at 2.0 reads as a number that drifted and so does
            one of the seven turning out unnecessary; named, both are visible as what they are.
            Four new rows in section 18, in two kinds. Absence: the provider refuses the request
            rate, and a feed answers past the night's deadline. Wrong content: a feed answers
            with a truncated payload, and a feed answers with a session other than the one asked
            for. The second kind is the one that reports green today, since a night answered with
            yesterday's bulk file logs one request and no error.
            One more from decomposing "Bulk price feed unavailable" per surface, exactly as the
            gap row was decomposed at 1.5: the behaviour half passes at 2.3 and the banner half
            stays out of scope until 5.4.
            Two new rows in section 17: the per-request timeout with the night's deadline, and
            the weighted-call budget, which `RUNBOOK.md` states as 100,000 and no code reads.
            Six of the seven pass in the phase that creates them. The seventh is the banner half.
Range:      185 to 188, and the rulings pass reports which end and why rather than restating the
            number. Upward is a failure mode the sign-off inventory did not catch: that inventory
            was built by reading the feed surface and section 18 together, and a mode that
            appears only once the retry policy and the deadline are actually specified would
            surface at 2.0 and not before. A credential rejected mid-night, and a partial answer
            that parses cleanly and holds fewer names than the index, are the shapes to look for.
            Downward is the two absence rows collapsing into one, or into the decomposed
            unavailable row, because they share a behaviour: a refused rate and an answer past
            the deadline both end as the feed did not answer, and section 18 already promises
            what the system does then. If the definition of unavailable covers both, they are one
            row or none. The two wrong-content rows cannot collapse the same way, because an
            answer that arrives and is wrong is refused rather than treated as an absence, which
            is a different behaviour and the whole reason the wrong-session row exists.
Not built:  no code. This pass writes a plan and repairs one sentence. The rulings phase 2 needs
            are the other half of 2.0 and land in the commit after this one, so a reader can see
            the plan and the decisions as two diffs rather than one.
Claims:     180, 37 pass, 143 out of scope, 0 unexamined, unchanged. Predicted before the run:
            section 20 is placed as a whole rather than claimed row by row, so the phase row adds
            none, and section 14's amended sentence sits in a note rather than in the ordered
            list. Nothing is due at phase 2 yet, so no claim moved.
Tests:      257, unchanged.

### 2.0 planning - the feed rulings                                          2026-09-09
Not a checkpoint entry. It belongs to 2.0, and it is the other half of it. The commit before this
            one wrote the plan; this settles the six holes that plan assigns to 2.0, each as a
            `DECISIONS.md` entry under a new heading, Feeds and the wire.
Settled:    what unavailable means, and what wrong means beside it. The retry count, the backoff,
            the per-request timeout and the night's deadline. The schedule as a UTC instant. What
            a paged answer does to the request count. The weighted-call budget. And the shape of
            the `nightly-cost` carve-out.
Range:      the prediction stated 185 to 188 and named what would move it. It is 185, the bottom,
            and it is the downward cause named in advance rather than a different one.
            Two of the four failure rows collapse. A rejected request rate and an answer past the
            deadline are both unavailable under the definition settled here, and section 18
            already carries a row promising what the system does when a feed is unavailable:
            keep last night's bars, mark every name stale, still serve the app, banner with the
            data date. Two more rows would have repeated that row in all four of its cells, which
            is the two-statements defect this corpus refuses everywhere else. They are induced as
            two of that row's cases instead, so nothing goes untested and nothing is said twice.
            The two wrong-content rows do not collapse, and the reason is sharper than the one
            the prediction gave. It is not only that a wrong answer is refused rather than
            treated as an absence. It is that the two are refused in different places: only the
            feed can see the session date a payload declares, and only the fetcher knows how many
            members the index holds. One is refused before parsing and the other after it, which
            is two behaviours in two components and cannot be one row.
Count:      185 claims and 42 PASS at the end of phase 2, from 180 and 37. Two new section 18
            rows, one more from decomposing the unavailable row per surface, two new section 17
            rows. 42 plus 143 out of scope is 185, which is the arithmetic checked at 2.7.
Hour:       the schedule decision fixes the form and not the hour. A UTC instant, registered with
            whatever scheduler the machine has, set at the provider's posting hour plus a margin.
            The hour itself is measured at 2.1 from live fetches rather than taken from
            documentation, and it is recorded as an obligation rather than left in this entry,
            because a number nobody has measured written into a decision is the shape that gets
            cited later as though it were settled.
Carve-out:  the `nightly-cost` decision is about a check rather than about cost, and it is the
            one of the six most likely to be got wrong quietly. The check scans every shipped
            file for the outward-request types and reports zero, which is true only while no feed
            reaches the network. Deleting the patterns would leave a check reporting the absence
            of a scan as the absence of a client, inside the check that carries the nightly
            path's own claim. The exemption is named instead, one file per live feed, and the
            list is asserted to hold exactly the feed implementations.
Not built:  still no code. 2.0 settles and builds none of it, which is why a planning checkpoint
            can never be the answer to a due point.
Claims:     180, 37 pass, 143 out of scope, 0 unexamined, unchanged. Predicted before the run:
            a decision record carries no claim, and the plan amendments name checkpoints that
            already existed. The 185 above is what phase 2 ends at, not what it stands at now.
Tests:      257, unchanged.

### 2.1 The credential path and the bulk price feed                          2026-09-09
Built:      the first feed in this tree that reaches a provider. `EodhdBulkPriceFeed`, the
            credential path from configuration with the startup refusal `RUNBOOK.md` promised and
            no startup path performed, and `NightFeeds`, which is the seam the live-against-
            fixture choice will go through at 2.6.
Two commits: the `nightly-cost` carve-out landed first and alone, before any feed existed. A
            commit that removes a guard and adds the thing the guard forbade authorises itself
            whichever half is read first, which is done condition 8's ordering applied to a guard
            rather than to a record. `BUILD_PLAN.md` asks for it in those words, and this entry
            says it happened because "a checkpoint lands as its own commit" is the rule it bends.
Carve-out:  the scan is now written over its inputs rather than over the checkout, so the
            exemption can be exercised on constructed sources. It was proved in both directions
            while it still carried nothing, which was the point of landing it early: an assertion
            over an empty list is one that has never run. Verified against the real tree as well:
            a client field added to `Backfill.cs` fails the scan naming the file by its path, and
            the same file added to the exemption passes the scan and fails the other test,
            because a file earns the exemption by being a feed. Both edits reverted.
Redaction:  the key travels in this provider's query string, so it is one substring away from
            every message a failed request produces. The scrub takes the address as well as the
            key, and the second half is the one that matters: a message quoting a URL this
            process did not build would carry a live key past a scrub that only looked for the
            value it happens to hold. The inner exception is not attached at all, so a logger
            expanding the chain cannot reach a message this code never scrubbed. The stack trace
            of the transport is what that costs.
Live:       a night ran against the provider at 06:16:17 UTC on 2026-09-09, over the fixture for
            everything except the fetch. One request. 44,362 rows, 6,676,343 bytes, 4.21 seconds
            to fetch. Three bars stored for the three current members, AAPL at 316.22, MSFT at
            493.95 and KEYS at 333.42, each matching the provider's payload. The run log records
            the fetch stage as 0 model calls and 1 network request, and the whole store was
            scanned afterwards for anything holding a scheme separator, the parameter name or the
            provider's domain: none.
Found:      the live run failed on its first attempt, and it failed on real data the fixture
            could not hold. `ProviderBarReader.Read` refuses a row whose close is not positive,
            because the adjustment factor divides by it, and 62 of the 44,362 rows carry one.
            The night stopped on DEWM. The bulk file is the whole exchange rather than the index,
            so it carries symbols that are listed and did not trade, and refusing the file for
            one of them lets a penny stock stop the night for five hundred names.
            This is the failure 1.2 found one layer up, arriving exactly where 2.0's plan said it
            would. All seven rows of the captured bulk file were hand-picked at 1.4 and every one
            of them traded, so no suite that read only the fixture could have found it. What
            fixed it is that the run was live rather than that the code was read again.
Skipped:    a row with no positive close is now skipped and named rather than throwing, and the
            count reaches the operator on the fetch step's own line. Named rather than counted,
            because the same skip at scale is a night that stores almost nothing and reports
            success. Three guards hold the shape: a row from another exchange is dropped without
            being counted, since it is not this night's at all; a row missing a field still
            throws, since a payload that cannot be read is not a payload with fewer bars; and a
            file where every row is a symbol that did not trade is refused outright, since
            answering with no bars would be stored as an exchange that did not trade.
Measured:   the payload was fetched once more, on its own, so the design was made against the
            real distribution rather than against the one row that happened to fail first. 62
            rows carry no usable close. 27,540 of 44,362 carry zero volume, which is a session
            with no trades rather than a fault and is stored as one. Zero rows produce an
            adjusted bar that could not have traded, so 1.2's guard finds nothing to refuse on
            live data. Every row carries every field the parser reads.
Note:       the manifest records the 1.4 capture as seven of 10,670 rows. Today's file holds
            44,362. The manifest is a dated record of what that capture held and is left as it
            is; the two figures are four days and one endpoint apart, and which of them is the
            steady state is not something one fetch each can say.
Hour:       the obligation created at 2.0 is bounded rather than discharged. The file for the
            2026-09-08 session was already being served at 06:13:42 UTC on 2026-09-09, which is
            an upper bound on the posting hour and not the hour. Measuring the hour means asking
            repeatedly across an evening, which is a live night's work rather than a checkpoint's,
            so the remainder is re-pointed to 2.6 and the bound is recorded here.
Selection:  `--live` is a flag on the command line and not yet a setting. That is the state 2.6
            improves, and it is deliberate: a half-built configuration path is what lets a
            mistyped fixture folder resolve to the network, so the choice stays loud until the
            checkpoint that asserts both directions of it.
Fixture:    `expectations/fetch.json`, derived from the two committed inputs rather than frozen
            from a run: the membership filter applied to the captured bulk file gives three bars
            stored, two rows for departed constituents and two for names the index does not hold.
            It states `notSessionsExpected` as 0 and says why the zero is a property of the
            capture rather than of the exchange, so the figure reads as a gap in the fixture and
            not as a fact about the world.
Claims:     180, 37 pass, 143 out of scope, 0 unexamined. Predicted before the run: 2.1 adds no
            row to any table this harness parses. The two limits rows and the two failure rows
            phase 2 owes arrive at 2.2 and 2.3, so the count moves there and not here.
Tests:      275, from 257. Four with the carve-out and fourteen with the feed.
Platform:   Windows for the by-hand live run. The suite runs on both runners through the matrix,
            and the live run is not part of it: no runner holds a key, and one that did would
            spend the operator's allowance on every push.

### 2.2 Retry, backoff and the night's deadline                              2026-09-09
Built:      `RetryPolicy`, `ProviderRequest` and a cancellation source the night owns. Three
            attempts per request, waiting two seconds and then four, each attempt bounded by
            thirty seconds, inside a night bounded by fifteen minutes.
Why here:   every feed interface has accepted a cancellation token since 1.1 and nothing supplied
            one, so a night that hung on a socket hung until somebody looked. `BarFetcher` and
            `CorporateActionChecker` took no token at all; both do now, and every step of the
            night runs under the same source.
Shape:      the retry knows nothing about HTTP. A feed converts what its transport did into a
            `ProviderRefusal` carrying whether asking again would help, and `ProviderRequest`
            decides how many times. That seam is not tidiness: naming a transport exception in
            the retry would put a file in `nightly-cost`'s exemption list that is not a feed, and
            the carve-out would have to widen to cover code that holds no client.
Told apart: a per-request timeout and the night's deadline arrive as the same exception type and
            only the token says which. The deadline is checked first and never retried, because
            retrying it is this class overruling the caller's decision that there is no time
            left, which is how a night with a fifteen-minute bound runs for forty-five.
Counted:    one request costs one request however many attempts it took. The limit that figure is
            read against is one request for the night whatever the universe is, and three
            attempts at one request is still one request: what grows with retries is time, and
            the deadline is what bounds that. Attempts are reported separately.
Waited:     the wait is injected and the schedule is read off what the request asked for rather
            than off a clock. A backoff proved by waiting six seconds is a backoff nobody runs
            twice, and a test nobody runs twice is one that gets a Skip attribute the first time
            it is inconvenient.
Refetch:    the third done condition, and it is a real risk rather than a formality. The refetch
            is the only non-idempotent write on the nightly path, deleting a name's year and
            reinserting it, so a second attempt made after the delete would leave two years or
            half of one. A feed that refuses its first attempt is run through the real retry into
            a real refetch, and the stored series is compared against a clean refetch rather than
            against the count before it: the refetch asks for the year ending on the action night
            rather than on the backfill date, so the count legitimately moves and the first
            version of this test failed on the answer to a question it had not meant to ask.
            The retry is asserted to have happened, because a flaky feed that never refused would
            leave every other assertion true and the property untested.
Limits:     section 17 gains **Per-request timeout and the night's deadline**. The figures are
            read off that row and asserted against `RetryPolicy.Standard`, so a number changed in
            either place fails: a limit stated in a document and again in code is two places
            holding one fact.
Derived:    thirty seconds because the bulk file for a whole exchange is the largest thing the
            night fetches and arrived in 4.21 seconds at 6,676,343 bytes on 2.1's live run, which
            is far outside a healthy fetch and far inside the night. Fifteen minutes because it
            is three times the wall clock section 17 already states for five hundred names, so
            the deadline stops a night that has hung rather than one that is merely slow. Three
            attempts because the failure a retry is for is a transient one and a fourth attempt
            is a slower way of learning what the third said. None of the three is a round number
            chosen for looking like one.
Claims:     181, 38 pass, 143 out of scope, 0 unexamined, from 180 and 37. Predicted before the
            run: one new limits row, passing in the checkpoint that creates it, and no claim
            leaves the out-of-scope set because nothing was owed at 2.2.
Tests:      286, from 275.

### 2.3 Failure behaviour at the wire                                        2026-09-09
Built:      unavailable implemented to the definition settled at 2.0, and section 18 given the
            two rows that definition leaves it short of.
Rows:       **A feed answers with a session other than the one asked for** and **A feed answers
            with fewer names than the index holds**. Both are answers that arrive, which is the
            half of the definition that had no row at all: section 18 promised what the system
            does when a feed is unavailable and said nothing about a feed that answered.
Two, not one: they are refused in different places, and that is the reason rather than a
            preference. Only the feed knows the session it asked for, and only the fetcher knows
            how many names the index holds. One is refused inside the feed and the other after
            it, so folding them would put one of the two checks somewhere it cannot see what it
            needs.
Asked for:  the bulk feed is now told which session to fetch rather than asked for the last day.
            That was left open at 2.1 and this checkpoint closes it, because a request with no
            date has nothing to compare its answer against: yesterday's file arrives making one
            request, logging no error, and storing bars the store already holds. The live feed
            sends the date and the recorded feed checks it, and the recorded feed checks rather
            than filters, because a double that quietly returned only the matching rows could
            never answer with the wrong session and the fixture would be structurally unable to
            induce the failure this checkpoint exists to induce.
Short:      a current member that appears neither as a bar nor as a symbol that did not trade is
            a member the file does not carry. The bulk file is the whole exchange and every
            member of a US index is listed on it, so that is a truncation rather than a quiet
            night. The refusal names how many of how many, and the first five by ticker.
            Refused before the transaction opens, so the names it did carry are not stored
            either. That is deliberate and it is the strict reading: a partial store leaves most
            of the index silently stale beside names that look current. The behaviour for an
            unusable feed already exists and is safe, being to keep last night's bars and say so.
            If it turns out to fire on ordinary nights that is a measurement phase 5 will have,
            and the row can be revisited with data rather than with a guess now.
No threshold: neither refusal carries a fraction, a proportion or a tolerance. The first compares
            two dates and the second compares a set against a set, and both are derived from the
            structure of the data rather than from a number somebody chose. That was the point of
            looking: a truncation rule written as "fewer than ninety per cent of members" would
            have been a round number with nothing behind it.
Decomposed: **Bulk price feed unavailable** is read per surface the way the gap row was at 1.5.
            Its run log half is asserted here, being that a night whose feed does not answer
            stores nothing, leaves the bars it held exactly as they were, names the step and
            exits non-zero. Its banner half is a phase 5 surface and stays out of scope until
            5.4. The row's own text was amended to name both, because a decomposition the
            document does not carry is a second statement of the row's content and the check
            reads each element back out of the row.
Retention:  the boundary was read back out of the rows the provider sent and is now the session
            the night asked for. A payload for an older session would have moved it backwards and
            kept sessions the night should have dropped, which is a second defect the dated
            request removes rather than a third thing to check.
Induced:    every one of the three is induced against the fixture rather than described, and each
            runs a clean night first so there is a stored series for the refusal to leave alone.
            A test that induced these against an empty store would prove that nothing was written
            where nothing could have been.
Roster:     `nightly-run`'s row in `CLAUDE.md` is widened to what it now asserts. A check whose
            declared reach grows past its roster description is a property nobody wrote down.
Claims:     184, 41 pass, 143 out of scope, 0 unexamined, from 181 and 38. Predicted before the
            run: two new rows and one more claim from the decomposition, all three passing in the
            checkpoint that creates them, and the banner half staying out of scope.
Tests:      289, from 286.

### 2.4 The remaining price and membership feeds                             2026-09-09
Built:      `EodhdIndexMembershipFeed`, `EodhdHistoricalBarFeed` and `EodhdCorporateActionFeed`,
            each reading its answer through the parser the double already uses. The weighted-call
            budget, which `RUNBOOK.md` has stated since the architecture was written and no code
            had read. `--session`, so a night can be run by hand for a session the operator names.
Live:       a night ran end to end against the provider for the 2026-09-08 session. 822 membership
            rows, 125,742 bars over 503 tickers backfilled at one request per name, 501 bars for
            the session, 182 corporate actions of which 7 fell on current members and each
            triggered a full-year refetch. 11 network requests, 317 weighted calls of 100,000, 0
            model calls. The store was scanned afterwards: no URL and no key anywhere in it.
Found:      the live payload broke the membership parser on its first attempt, and the fixture
            could not have shown it. `IndexConstituent.Joined` was not nullable and the parser
            threw on a constituent with no StartDate. The provider carries 822 spans and 145 have
            none, two of those being current members: IR and WAB are in tonight's snapshot of 503
            and the provider will not say since when. The fixture's five constituents all carried
            one.
            Dropping such a name takes a real member out of the index and out of everything
            computed from it. Writing a date nobody has is the guess this corpus refuses
            everywhere else. So the column admits null, which needed migration 6.
Rebuilt:    the unknown cannot sit in a primary key. SQLite treats nulls as distinct, so a second
            night would insert a second row for the same name rather than conflicting with the
            first, and the uniqueness moved to an expression index that folds the unknown to a
            value. That is the one place a sentinel belongs: inside the index that enforces
            uniqueness, and never in the column a query reads. A row whose join date is unknown
            answers no to a past-date query, because a comparison against null is null, and yes
            to members now, which is `left IS NULL`. Both are true rather than convenient.
Rebuild:    `writer-ownership` reported the migration's `DROP TABLE membership` as a write nobody
            declared, which it was reading correctly: SCHEMA gives membership no deleter. The
            drop is half of a rename rather than a removal, and the permission is narrow in three
            ways, being only the migration runner, only a drop, and only where the same migration
            renames something back to the name it dropped. Its proof runs both directions over
            the real migrations rather than over a description.
Corrected:  2.3's truncation rule, by the first live night over five hundred names. It refused a
            payload carrying nothing for any current member, and two of 503 are absent from an
            ordinary day's file: EQR and PSTG are not in the 2026-09-08 bulk file at all, neither
            as a bar nor as a symbol that did not trade. A rule that refused on any absence would
            refuse every night.
            The 2.3 entry said that if it fired on ordinary nights the row could be revisited with
            data rather than with a guess, and phase 5 would have the data. Phase 5 was three
            checkpoints too late: the data arrived the moment a night ran over the whole index.
            The row is now **A feed answers with none of the index in it**, which is the wrong
            file or a session the exchange has not traded and is not the same as a file with no
            rows: a night run before the close produced exactly that, a payload full of symbols
            this index does not hold and carrying nothing for any of its five hundred members.
            A file short of some members but not all is stored for the rest, the names it carried
            nothing for leave the stage as a count, and the fetch line reports them. The count is
            what stops the correction from becoming silence: a rise from two to two hundred is a
            fact about the provider that nothing else would show.
Session:    a night run this morning asked the provider for 2026-09-09, which the exchange has not
            traded, and every one of 503 members came back unaccounted for. That is the schedule
            decision doing its work rather than a defect, and it is why `--session` exists: the
            run RUNBOOK asks for by hand, and the catch-up night after a machine was off. It
            resolves to a fixed instant in that session's evening so the same derivation runs as
            on any other night. Its run id carries the real instant as well, because a clock fixed
            to a session gives the same id every time and the second by-hand run for one session
            collided on the run log's key.
Weights:    the budget is composed from the feeds' roles rather than declared on each feed, and
            every figure is read back out of `RUNBOOK.md` rather than repeated in code. A request
            is not a request: the live night made 11 and spent 317. The local stop exists because
            the provider's own stop is a rejected rate, which arrives as an unavailable feed and
            loses the reason.
Claims:     185, 42 pass, 143 out of scope, 0 unexamined, from 184 and 41. That is the figure
            2.0 predicted for the end of the phase, reached at 2.4: the prediction named seven new
            claims and all seven now exist. 2.5 and 2.6 add none, which 2.7 checks.
Tests:      298, from 289.

### 2.5 The news feed                                                        2026-09-09
Built:      `INewsFeed`, which news has never had, and `EodhdNewsFeed` behind it.
            `RecordedNewsFeed` now implements the same interface, and `NewsAttribution.ByName` is
            the fan-out done in code.
Why it mattered: news was the only feed without an interface, which made it the only one whose
            request count no contract forced. The 1.7 measurement ran through a class the nightly
            path does not reach, so a live implementation could have made one request per name and
            nothing in the harness would have said so. `Requests` is now on the interface for the
            same reason it is on the other four, and the test reads it through the interface rather
            than off either implementation.
Live:       one dated request with no ticker, for the 2026-09-08 window. 1,000 articles, 4,753,520
            bytes, and 3,232 distinct symbols attributed from that single request. The fan-out is
            not a design intention: it is what the payload carries, and one request reached three
            thousand names.
Found:      the request came back holding exactly the limit. The provider caps one request at
            1,000 articles and a single day of market-wide news reaches it, so one dated request
            does not carry a whole day. The decision that news arrives in one dated feed request
            still holds for the shape of the cost, and what it does not yet settle is the window.
            Recorded as an obligation due at 5.5, which is the checkpoint that counts articles per
            name and the first that can measure what a night actually needs.
            The limit is asked for in full and never paged around, which is why this was visible at
            all. A feed that paged quietly would have turned one request into ten and reported the
            truth in a count nobody was reading, and the paging decision at 2.0 is what says the
            count includes every page rather than the feed hiding them.
Not built:  nothing calls the news feed on the nightly path yet. The news pulse counter is a phase
            5 component and section 14's step seven is one of the five that do not exist, so the
            feed is built, asserted and unused, which is the same state every other feed was in at
            the checkpoint that built it.
Compared:   the live feed and the double are asserted to read one payload the same way, field by
            field rather than as records. `NewsArticle` carries its attribution as a list and a
            record compares a list by reference, so two parses of one payload are never equal
            however identical their contents. Asserting the records would have been an assertion
            that could not hold, which is a different failure from one that does not.
Claims:     185, 42 pass, 143 out of scope, 0 unexamined, unchanged. Predicted before the run:
            2.5 adds no row to any table the harness parses, and the seven claims 2.0 named all
            arrived by 2.4.
Tests:      304, from 298.

### 2.6 The selection point and a live night                                 2026-09-09
Built:      `NightFeeds.Resolve`, which decides where tonight's feeds come from, and
            `EquityBrief:Providers:Source` with `EquityBrief:Providers:Fixture` beside it.
            `tools/nightly` takes no fixture argument of its own any more, which is what its own
            text has promised since 1.4: the argument was there because the live feeds did not
            exist, and it said it would become optional when they did.
Why a setting: until now the choice lived in whichever overload the caller happened to call, so a
            scheduled night's source was a property of a shell script. The flags remain for the
            run RUNBOOK asks for by hand, and giving both is refused, because a command that said
            live and fixture at once has no right answer and picking one would be this code
            deciding what the operator meant.
Neither falls back: that is the property, not a courtesy, and both directions are failures that
            look like successes. A fixture folder that does not exist is refused rather than
            resolved to the provider, because a mistyped path would otherwise spend the allowance
            and store live bars where a replay was meant. A live source with no key is refused
            rather than falling back to a capture, because a night that quietly replayed yesterday
            would look exactly like a night that ran. Each is asserted with the other side present
            and working, so a fall-back would have succeeded: the key is valid when the path is
            wrong, and the capture is readable when the key is blank.
Live:        a night ran end to end against the provider for the 2026-09-08 session with no
            fixture folder given and no source set. 822 membership rows, 126,235 bars over 503
            tickers, 501 bars for the session, 182 actions with 7 refetches. 514 network requests,
            820 weighted calls of 100,000, 0 model calls, green.
Fixture:     the same night over the capture makes no request: 5 membership rows, 753 bars, and
            feeds that hold no client at all.
Refused:     three refusals run by hand, each exiting non-zero. A fixture path that does not
            exist, both flags at once, and a live source with a blank key.
Found:       the night's own summary said "network request(s)" over a capture, on a night that
            touched no network. A recorded feed counts the calls a live one would have made, which
            is deliberate and is how a replay measures the cost shape, but printing it that way on
            a fixture night is a figure that means one thing and reads as another. The line now
            says which source the night ran against, read off the feeds rather than off the
            setting that chose them, so a fall-back that got past both refusals would still be
            visible on the line the operator reads.
Reaches:     `NightFeeds.ReachesTheNetwork` asks the objects rather than the setting. The recorded
            doubles are named, so a sixth feed added live and forgotten there reads as one that
            can reach the network rather than one that cannot, which is the direction that is safe
            to be wrong in.
Hour:        the posting-hour obligation is bounded twice and discharged neither time. The file for
            2026-09-08 was being served at 06:13 UTC on the 9th and again at 12:03, which are two
            upper bounds and not an hour. Measuring when it first appears needs observations across
            several evenings, which a running installation accumulates and a checkpoint cannot, so
            it is re-pointed to 3.7 with both bounds recorded. A night run before the close is
            already refused loudly by the fetch, so nothing waits on this figure.
Claims:      185, 42 pass, 143 out of scope, 0 unexamined, unchanged and predicted. 2.6 changes
            where the feeds come from and adds no row to any table the harness parses.
Tests:       312, from 304.

### 2.7 Phase 2 report                                                       2026-09-09
Report:     185 claims, 42 pass, 0 fail, 143 out of scope, 0 unexamined, 48 placements and
            verdicts reconciled against a floor of 34. 32 checks on the roster, 28 carried, 28 ran
            and passed and none did not run. 312 of 312 tests ran, 0 failed and 0 did not run.
Prediction: 2.0 predicted 185 claims and 42 pass, with a range of 185 to 188 and a named cause at
            each end. It is 185 and 42, which is the bottom of the range, and it is there for the
            downward cause the prediction named rather than a different one.
            Named rather than counted, which is what made the check possible. Four section 18 rows
            were predicted and two exist: a rejected request rate and an answer past the deadline
            both collapsed into the unavailable row, because the definition settled at 2.0 makes
            both of them unavailable and section 18 already promised what the system does then.
            Two more would have repeated that row in all four of its cells. The two wrong-content
            rows survived, and the reason turned out to be sharper than the prediction gave: they
            are refused in different components, not merely for different reasons.
            A prediction of seven could not have reported this. It would have read as a number
            that drifted by two, where what actually happened is that two named rows turned out to
            be one row's cases and two others were confirmed for a better reason than the one
            written down.
Claims by source: two section 17 rows, being the per-request timeout with the night's deadline at
            2.2 and the weighted-call budget at 2.4. Two section 18 rows at 2.3. One more from
            decomposing the unavailable row per surface, whose run log half passes at 2.3 and
            whose banner half is out of scope until 5.4. Five new claims, five new passes, and the
            out-of-scope count unchanged at 143 because nothing was owed at phase 2 before it.
Instruments: every passing claim names a check whose declared reach includes it, reconciled in
            both directions. The phase's own claims are carried by `nightly-run` and
            `nightly-cost`, and `nightly-run`'s roster row in `CLAUDE.md` was widened at 2.3 to
            what it now asserts rather than left describing a narrower check.
Fixture:    `expectations/fetch.json`, added at 2.1 and derived from the two committed inputs
            rather than frozen from a run: the membership filter applied to the captured bulk file
            gives three bars stored, two rows for departed constituents and two for names the
            index does not hold. It states `notSessionsExpected` as 0 and says why that zero is a
            property of the capture rather than of the exchange, which is the sentence that made
            the 2.1 defect legible after the live run found it.
Live:       every feed reached the provider during this phase and each run is recorded at its own
            checkpoint. The last of them, at 2.6, ran end to end with nothing on the command line
            but a session: 822 membership rows, 126,235 bars over 503 tickers, 501 bars for the
            session, 182 corporate actions with 7 refetches, 514 requests, 820 weighted calls of
            100,000, 0 model calls.
Found live: three defects the fixture could not have shown, each found by running rather than by
            reading. A row with no positive close stopped the night, and 62 of 44,362 rows carry
            one. A constituent with no join date threw in the parser, and 145 of 822 spans have
            none with two of those current members. A truncation rule that refused any absence
            would have refused every night, because two of 503 members are absent from an ordinary
            day's file. All three were written against a fixture of five names and all three were
            refuted within hours of the first live run.
What green does not mean: it does not mean a night will run tonight. Every figure above is a
            statement about the build and about runs made by hand today, and the schedule that
            would make them nightly is a setting on a machine rather than anything this report
            reads. It does not mean the feeds are complete: the news feed is built, asserted and
            called by nothing, because the component that would call it is a phase 5 one. It does
            not mean the numbers are right, only that they are the numbers the rules produce over
            one captured fixture of three names and whatever the provider sent today. And 143 of
            the 185 claims are out of scope, which is to say unchecked: the report says nothing
            about them and is not meant to.
Not signed off: this session committed code to phase 2, so it may not sign phase 2 off. The
            report is written here and the sign-off is a separate activity with its own record,
            owed before phase 3's plan and not gating the merge.
Tests:      312, from 257 at the start of the phase.

### Correction to 2.7 - the deadline test raced on a runner and not here        2026-09-09
Corrects:   nothing in the report's figures. The suite was green on this machine and red on the
            windows runner at the first push, on `ANightThatPassesItsDeadlineStopsAndSaysSo`.
Found:      the test gave a fresh store a 250 millisecond deadline and a feed that never answers,
            and asserted the night stopped on the fetch. On a warm machine the migrate, membership
            and backfill steps finish inside that; on a cold runner they do not, so the deadline
            fired on an earlier step and the assertion about which step was named failed.
            The property was right and the arrangement was a race. A test whose answer depends on
            how fast the machine is has no answer, and this one passed locally on every run.
Repaired:   a clean night runs first, so every step but the fetch is a no-op against a warm store,
            and the bound is three seconds rather than a quarter of one. A runner ten times slower
            still reaches the deadline inside the fetch and nowhere else. The assertion is
            unchanged, which is the point: what moved is the arrangement, not what is claimed.
Why here:   `two-platform` is the check that made this visible, and it is the reason the matrix
            exists. Both runners ran the same suite and disagreed, which is exactly the class of
            fault a single-machine green cannot see.
Tests:      312, unchanged.

### Phase 2 sign-off                                                         2026-09-09
Signed by a session that has committed no code to this repository. Its only commit is this
            entry, which is a document, so the fresh session rule permits it. Every phase 2
            checkpoint was committed by other sessions, and the 2.7 entry says in its own last
            line that it does not sign the phase off.
Verified:   by re-running rather than by reading the 2.7 entry. `tools/ci.ps1` green end to end,
            all 6 steps, 0 warnings, 312 tests passing inside it, exit 0. `tools/verify-phase.ps1`
            green at 185 claims, 42 PASS, 0 FAIL, 143 out of scope, 0 unexamined, 25 tables, 48
            placements and verdicts reconciled against a floor of 34, fixture PRESENT with 1
            captured over 5 constituents and 3 names, 32 checks on the roster and 28 carried, 28
            ran and passed and none did not run. Windows PowerShell on this machine, at commit
            71328b5. Both surfaces of the report read the same, and the HTML carries the same
            verdict block as the JSON.
Counts:     by verdict, over the 185 claims in `artifacts/phase-report.json` at 71328b5: 42 PASS,
            0 FAIL, 143 OUT OF SCOPE, 0 unexamined. The five passes phase 2 added, by the
            instrument that reaches them: `nightly-run` 4, `nightly-cost` 1.

Key:        the membership uniqueness, exercised rather than read. The index is
            `UNIQUE (index_code, ticker, IFNULL(joined, ''))`, and the fold is what makes it the
            non-naive form. Three duplicate inserts were tried against a store migrated to
            version 6 by the shipped runner, plus a control and an edge case, all five by hand
            from outside the repository so nothing was added to the tree.
            All three refused, each on `membership_span` with SQLite 19/2067. A second span for a
            null-join constituent, WAB, refused. A second span for a dated constituent, AAPL at
            1982-11-30, refused. And the case a naive expression index lets through, two rows for
            one ticker both carrying a null join and differing only in `left`, refused: the fold
            makes both keys the same triple and the second collides.
            The control ran because a refusal that refuses everything proves nothing. Three
            distinct spans for one ticker, one null-join and two dated, were all accepted. So the
            index enforces what the primary key enforced, over a domain the key could not hold.
            One edge, stated because it is the sentinel's own risk: a literal empty string in
            `joined` collides with the unknown, and it was confirmed to. It is unreachable through
            the writing path rather than guarded. `RecordedIndexMembershipFeed.Date` yields a
            nullable date, so a present StartDate either parses as yyyy-MM-dd or throws, and the
            sole declared writer cannot produce an empty string. The exposure is a future writer,
            not this one.
Not tested: nothing in the suite asserts any of the four. `schema-columns` reads tables and
            columns and does not read indexes, and no test names `membership_span`. A key changed
            under time pressure is now examined once by hand, which `CLAUDE.md` says is exactly
            what a permanent test replaces. Carried to 3.1.

Out of scope: 143 at phase 1 sign-off and 143 now, and neither of the two readings offered is
            what the numbers say. Measured by multiset difference on table and subject between
            the report at f48d9d6 and the report at 71328b5, so a retirement could not cancel an
            addition inside a net figure. The f48d9d6 report was regenerated in an isolated
            worktree and reproduces the phase 1 sign-off figures exactly.
            Retired by phase 2's build: 0. Added by wider reading: 0. The whole of the movement is
            one row splitting in two. **Bulk price feed unavailable** left the set and **Bulk
            price feed unavailable, banner** entered it, which is the same row's other half, and
            its run log half passes at 2.3. One out, one in, the same subject.
            The six other claims are all new and none touched the out-of-scope set: two section 17
            rows, two section 18 rows and the decomposition's run log half all pass in the phase
            that created them, and the banner half is the single addition above. Without the
            section 18 rows this phase created, out of scope would stand at 143 as well: the
            undecomposed row would still be there, out of scope, and the banner half would not
            exist. The figure is unchanged under either accounting.
            So the reading is neither. It is not treading water, because nothing was retired and
            nothing was added by reading. It is that phase 2's deliverable has almost no
            intersection with the out-of-scope set. The 143 are claims about levels, the plan,
            tonight's list, research and the loop, and the feeds are the layer underneath all of
            them, described by the section 17 and section 18 rows this phase wrote and passed.
            Phase 1 was not like this: it moved 154 to 143, retiring 27 and adding 16.
About the instrument: the fact worth stating at sign-off rather than at phase 6 is a different one
            from the one suspected. The report does not get closer to green by building
            infrastructure, because the out-of-scope count measures how much of the architecture
            is unasserted and the architecture does not describe infrastructure claim by claim.
            143 of 185 is 77 per cent of the document unchecked after two phases of building, and
            a phase can be built end to end, run live, and move that figure by nothing. Green at
            phase 2 is a statement about 42 claims and about no others.

Deferrals:  the truncation rule generalised. The corpus was swept for deferrals that wait on
            evidence, as distinct from deferrals that wait on a ruling or on code: the holes
            table, the contradictions table, the carried obligations, and every deferral in
            `ARCHITECTURE.html` and `DECISIONS.md`. 12 found, 1 of them the truncation rule
            already corrected at 2.4. The count expected before looking was 8 to 12 with 2 or 3
            of the shape, and the total was right while the proportion was badly wrong: 9 of the
            11 open ones name a point that does not produce the evidence.
            2 arrive when they say. The volume shelf threshold at 3.6, where the fixture widens to
            four names and the widening is what produces the evidence. The research lane boundary
            at phase 6, which needs a recorded research pass that phase 6 builds.
            5 have their evidence already, earlier than the phase named. The news window at 5.5,
            whose evidence is article counts per name and arrived at 2.5, where one live request
            returned 1,000 articles over 3,232 symbols and reached the provider's cap. The bulk
            fundamentals probe at 6.1, which is one live call on a key the credential path has
            held since 2.1. The source lists review at 6.0, whose measured coverage was taken at
            1.7. The refetch atomicity test and the fixture expectation sweep, both at 3.1,
            deferred to group them with the next expectations rather than for want of evidence.
            4 name a point that produces no evidence at all, which is the sharper half. The six
            reason thresholds are settled at 5.0 and revisited from 5.6's data, and 5.6 is the run
            page: it displays a record that weeks of nights accumulate, and no checkpoint
            accumulates weeks. **Condition thresholds are calibrated from your own nights, not
            from a backfill** says the same thing and names the same absent producer. The three
            reason records needing resolved setups sit in phase 7 against months of accumulation,
            which phase 7 arriving does not supply, though phase 5's storage obligation is the
            mitigation and it is written down. And the provider's posting hour is due at 3.7,
            which is the phase 3 report: it produces no evenings of observation, and its done
            condition does not mention the obligation, so the due point is a placeholder.
            The shape is one shape. A deferral to a phase is a guess about when evidence appears,
            and it fails in both directions: early, where the evidence is already in hand and the
            corpus keeps a guess it could have replaced, which is what the truncation rule did;
            and never, where the named point is a report or a page rather than the thing that
            measures. What a deferral should name is the producer. Recorded here and repaired
            nowhere: no hole, obligation or decision is edited by this entry.

Broke:      three passing claims, each reached by a check whose declared reach was written during
            phase 2 and none of them carrying a permanent negative proof. Every mutation was made
            in an isolated worktree at 71328b5 and reverted, and the main tree was not modified at
            any point. Baseline in that worktree was 19 of 19 green across both checks before the
            first mutation.
            Two went red. Disabling the wrong-session refusal in `RecordedBulkPriceFeed` fails
            `nightly-run` at `APayloadForAnotherSessionIsRefusedAndTheStoredBarsAreUnchanged`.
            Disabling the none-of-index refusal in `BarFetcher` fails `nightly-run` at
            `APayloadHoldingNoneOfTheIndexIsRefusedBeforeAnythingIsStored`. Both PASS verdicts are
            load bearing.
            One stayed green, and it is the third done deliberately against the other check. The
            **Weighted-call budget** claim is reached by `nightly-cost`, and deleting the stop in
            `Nightly.cs`, the branch that refuses to make a call once the night's weighted calls
            have reached the stated daily allowance, leaves `nightly-cost` green at 8 of 8 and the
            whole suite green at 312 of 312. The claim would still read PASS with the stop gone.
            What is missing is exactly one of the three things the claim's own note asserts. Every
            weight and the allowance are read back out of `RUNBOOK.md`, which `LiveFeedTests`
            holds, and the night reports its weighted total beside its request count, which the
            run log carries. The third clause, that a night already at the allowance stops before
            making a call, is asserted by nothing: no test constructs a night at the allowance.
            The behaviour is in the shipped source and runs; what is absent is the assertion, so
            this is a verification hole rather than a wrong result, and under the stopping rules
            it is carried rather than reopening the phase. It is the same class as the two carried
            out of the phase 1 sign-off, and it sits beside **The spend cap is a stop, not an
            allowance**, which is a stop of the same kind that phase 6 will owe its own proof of.
            2 of 3 load bearing, against 1 of 3 at the phase 1 sign-off.

What green does not mean, this time: a night has now run against the provider over the whole
            index, so the two earlier paragraphs cannot be repeated. What ran is this. Two nights
            end to end against the live provider, at 2.4 and 2.6, both for the 2026-09-08 session,
            both on 2026-09-09, both on Windows, both started by hand and watched. The larger of
            the two covered 503 current members: 822 membership rows, 126,235 bars, 501 bars for
            the session, 182 corporate actions with 7 refetches, 514 network requests against the
            backfill carve-out and 11 in the steady state, 820 weighted calls of 100,000, and 0
            model calls. Four of section 14's nine steps exist and ran; five do not exist. What
            that establishes is that the credential path, five feeds, the retry, the deadline, the
            refusals and the store hold against real payloads at index scale, which is more than
            any fixture could have said, and it is how three defects were found that a fixture of
            five names had agreed with. What it does not establish is anything about a night
            nobody is watching. No night has ever run for two different sessions, none has run on
            two consecutive days, none has run unattended, none has run on a schedule, and none
            has run on macOS, where only the suite has been. The one night that asked for a session
            the exchange had not traded was refused, which is the guard working and is also the
            only unhappy live path anyone has seen. Nothing downstream of the feeds exists to be
            wrong yet: no levels, no listings, no plan, no research, and a news feed that is built,
            asserted and called by nothing. And 143 of the 185 claims are out of scope, which is to
            say unchecked, and this report says nothing about them.

Carried:    two obligations created here, both due at 3.1, which is the next checkpoint that
            writes code and the point the phase 1 sign-off's own two carries fall due.
            The membership uniqueness asserted by a permanent test rather than by this entry:
            the three duplicates above and the control, run against a migrated store, so the index
            is proved to enforce what the key enforced and the proof survives the session that
            made it.
            The weighted-call stop asserted where `nightly-cost` reaches it: a night constructed
            at the allowance, confirmed to make no call and to say so, so that deleting the branch
            turns the check red.
Noted:      the carried obligations table gives `Absolute path matching anywhere in a value, not
            position zero` a due point of 1.3 and no discharge marker, while 1.3's own text in
            `BUILD_PLAN.md` says it was discharged there. Every other discharged row carries the
            word. It is a record inconsistency rather than an undone obligation, and it is
            recorded rather than repaired because a sign-off does not edit a spec.
Tests:      312, unchanged from 2.7. Windows for this run; the matrix carries macOS and the Linux
            case-sensitivity job, and no live feed runs on either.
Signed:     phase 2 is signed off at 71328b5. Phase 3's plan is not opened by this session.

### 3.0 planning - a deferral names what produces the evidence      2026-09-09
Not a checkpoint entry. It belongs to 3.0, which has not landed. `BUILD_PLAN.md` places the
            phase planning pass here and this is the first of its commits. This session
            committed code and may not sign it off.
Built:      the rule, and an instrument for it. `CLAUDE.md` gains **A deferral names what
            produces the evidence, not a phase** and the citation convention beside the
            decision one, and the roster gains `obligation-reconciles`. The carried
            obligations table gains a name on every row and a fourth column stating what
            produces the evidence, and every row is now one of two forms read from its cells:
            a checkpoint that produces the evidence and cites the obligation back, or the
            literal `operating` carrying a numeric trigger, the surface it is read on and the
            checkpoint that builds that surface.
Re-pointed: the nine the phase 2 sign-off found, each stating a producer rather than when
            somebody will look. Five had their evidence in hand and now say so and where it
            came from: the source lists against 1.7's coverage measurement, the bulk
            fundamentals probe against a key held since 2.1, the news window against 2.5's
            live request, and the refetch atomicity and fixture sweep, which wait on no
            evidence at all and are grouped with 3.1's expectations. One moved: the posting
            hour leaves 3.7, which is the phase 3 report and produces no evenings, for 5.7,
            whose done condition is a week of unattended nights and which now says so. Three
            take the operating form, because no checkpoint accumulates nights or resolved
            setups: the six reason thresholds at 60 nights of listings, the reason records at
            the 250 resolved setups section 17 already states, and the calibration decision,
            which is the same obligation and now cites it rather than naming no point at all.
            Two are confirmed rather than moved, the volume shelf threshold at 3.6 and the
            research lane boundary, which had no row anywhere and now has one at 6.5.
Measured:   over the carried obligations table, 13 rows before and 20 after, 7 added and 0
            removed. Over those 20, 20 distinct names, 0 carrying terminal punctuation, 0
            that are neither form and 0 carrying parts of both. Over the corpus, 20 obligation
            citations and 20 resolve, and every one of the 20 rows is cited back by a
            checkpoint that owes it, reconciled against a floor of 13. Over the 33 roster
            rows, 29 are carried by an implementation, up from 32 and 28. The phase report is
            unchanged at 185 claims, 42 pass, 0 fail, 143 out of scope, 0 unexamined, 48
            placements and verdicts reconciled against a floor of 34, which is the expected
            result because this pass adds no claim to the architecture.
Proved:     the reconciliation refuses the defect it was built for, on this corpus rather than
            only on constructed input, in both directions. Removing 3.6's citation of the
            volume shelf threshold failed the forward direction naming that row; adding a
            citation of a row nobody wrote failed the reverse direction naming the file and
            the line. Both mutations were reverted and the tree is clean. Six permanent
            proofs stand behind them, one per assertion that can fail, plus the control: a
            partition that refuses everything proves nothing, so a well-formed row of each
            form is asserted to produce no fault.
Found:      one defect, in the harness rather than in the corpus, and the first sentence added
            to the obligations table exposed it. `PlanCheckpoints.In` ended the last
            checkpoint's text at the end of the file, so 7.8 silently owned the carried
            obligations table and every word after it. Writing "the level window" into a
            producer cell moved that limits row's due point from 3.4 to 7.8, and only the
            shadowing assertion caught it. The same fault ran the other way at every phase
            boundary, where a checkpoint's text ran on through the next phase's opening
            paragraph. A checkpoint's text now ends at the next checkpoint or the next
            section, whichever comes first, and never at the end of the file. This is the
            prefix-matcher shape `CLAUDE.md` says to sweep for, arriving a fifth time.
Also:       two passages describing the new citation form contained one, which is the exact
            shape of the exemption this commit deletes. `DecisionCitations` had carried an
            exemption for a `<name>` placeholder that no longer exists anywhere in the corpus
            and whose filter matched nothing. Rather than write a second exemption, both
            passages now name a real obligation, so they resolve. The exemption is removed in
            this commit rather than a later one because this is the file the obligation
            citation reader is modelled on, and copying it forward would have copied the
            exemption's shape. One reader now serves both markers, with the marker passed in,
            so `Corpus.cs` still never contains either form.
Tests:      325, up from 312. 13 of the new ones are `obligation-reconciles` and its proofs;
            the rest are the reader shared with `decision-resolves`.
Carried:    nothing new. Two rows opened here fall due later in this pass: the citation
            residue at 3.0 and phase 3's expectations at 3.1.
Notes:      the stale row the sign-off recorded is repaired here with its prior text in
            `CHANGELOG.md`, which is what a sign-off could not do and 3.0 can.

            `obligation-reconciles` declares no reach. It reconciles `BUILD_PLAN.md` against
            itself and reaches no claim in the architecture, so no placement and no verdict
            names it, and a reach declaration nothing uses is a declaration nothing keeps
            current. Its floor sits on the rows, which carry the property; files opened is
            context and carries none.

### 3.0 planning - the multi-part note sweep                                 2026-09-09
Not a checkpoint entry. It belongs to 3.0, which has not landed. The second commit of the
            planning pass. This session committed code and may not sign it off.
Why first:  this runs before the weighted-call budget is repaired rather than after it.
            Repairing the budget first repairs one instance of a class whose size is unknown,
            and a sweep run afterwards can only report how much was left. The budget is the
            first and worst instance and it is repaired at the next commit, with the
            population it belongs to already measured.
Unit:       a note asserts more than one thing when it joins two or more clauses naming
            different behaviours, artefacts or files, such that a mutation could falsify one
            and leave the others true. Two attributes of one comparison, "columns and types
            asserted against SCHEMA.md", are one thing measured two ways and are not counted.
Predicted:  stated before the sweep ran and named rather than only counted. 25 multi-part
            notes, range 20 to 30, the downward cause being that the component-access family
            reads as one reconciliation stated four ways and the upward cause being that it
            does not, because the four sources are four separate files and a reconciliation
            could be dropped against one alone. And 4 notes carrying at least one clause
            unreached by a test in the check's carrier class, range 2 to 8, of which one was
            known: the weighted-call budget, whose third clause the phase 2 sign-off proved
            unreached and whose first two sit in `LiveFeedTests` and `NewsFeedTests`, outside
            the `NightlyCost` carrier. The other three were expected to be notes whose clauses
            span two checks.
Measured:   over the 42 PASS claims in `artifacts/phase-report.json` at 9f12d66, 29 notes are
            multi-part and 13 are not. That is the top of the predicted range and it is there
            for the upward cause named in advance: the component-access family is five notes
            asserting a declaration against four separate documents, and each of the four is
            separately breakable.
            6 of the 29 carry at least one clause unreached by a test in the carrier class,
            against a prediction of 4 in a range of 2 to 8. A seventh finding sits outside the
            multi-part set and is the same fault in a single-clause note.
Found:      seven, in two classes, and both have the same consequence: deleting the behaviour
            leaves the named check green and the claim reading PASS.
            The first class is a clause nothing asserts anywhere. **Weighted-call budget**,
            third clause, that a night at the allowance stops before making a call. And **A
            split or dividend not caught**, the atomicity clause, which is the obligation the
            phase 1 sign-off already carried to 3.1 and which this sweep reaches independently.
            The second class is a clause asserted outside the carrier, which runs, passes and
            backs no verdict. **Weighted-call budget**, first two clauses, in `LiveFeedTests`
            and `NewsFeedTests`. **Bar history kept**, both clauses, in `Bars/BarFetcherTests.cs`.
            **Per-request timeout and the night's deadline**, first clause, in
            `Providers/ProviderRequestTests.cs` and `Providers/EodhdBulkPriceFeedTests.cs`; its
            second clause is reached. **Series state**, second clause, which is the corporate
            action checker's property and sits in that check's carrier. And the seventh, the
            single-clause one: **Corporate action checker** in the catalogue and again in the
            read and write matrix, whose notes describe a declaration reconciled against the
            row, the matrix row, SCHEMA's ownership and its own source, which is
            `component-access`'s property and which `corporate-actions` does not perform at
            all. Its reach declares both rows, so the reconciliation is satisfied and the
            content is another check's.
Sharper than the sign-off: the sign-off proved one clause unasserted by mutation. What the
            carrier rule adds is that the budget's other two clauses back no verdict either,
            and that four more claims have the same fault. The mutation the sign-off ran
            understated the finding rather than describing it, and it understated it in the
            direction the corpus says these faults always run: under-reporting.
No instrument: this stays a measurement and does not become a check. A note is prose and
            whether a clause is reached is a judgment about what a test asserts, not a parse.
            `TheCarrierOfEveryCheckOwnsItsTestsAndNoOthers` already asserts the mechanism the
            fault runs through; what it cannot say is whether the tests a carrier owns cover
            what its notes claim. The findings become obligations instead, which is what the
            rule this pass landed is for.
Carried:    four new rows, all due 3.1 and all cited by 3.1's own text, being the four
            second-class findings. The budget is repaired at the next commit and the atomicity
            clause was already carried. 24 rows in the table, up from 20.
Tests:      325, unchanged. This commit measures and records and changes no code.
