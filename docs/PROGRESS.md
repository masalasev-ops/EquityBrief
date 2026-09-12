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

### 3.1 - the weighted-call stop asserted where nightly-cost reaches it      2026-09-09
Not a checkpoint entry. It belongs to 3.1, which has not landed. The work is committed ahead
            of the checkpoint that owes it, which `CLAUDE.md` permits and which is why the
            commit subject names 3.1 rather than 3.0. Nothing here records 3.1 as landed: a
            `.0` is not a due point and a checkpoint is landed by its own entry, not by an
            obligation discharged early. This session committed code and may not sign it off.
Built:      all three clauses of the **Weighted-call budget** note asserted inside
            `NightlyCost`, which is `nightly-cost`'s carrier and therefore the only class
            whose tests can back that claim. The third clause is new: a night is constructed
            already at the allowance, with a bulk feed reporting the requests an earlier run
            spent and throwing if it is ever asked for rows, and the night is asserted to exit
            non-zero, to name the step it stopped before, to state what it had spent against
            what it was allowed, to have asked the feed for nothing, and to have written no
            run log row at all.
Moved:      the first two clauses, rather than copied. They were asserted in `LiveFeedTests`
            and `NewsFeedTests`, where they ran, passed and backed no verdict. Copying would
            have left a number asserted in two places, which is the two-statements defect this
            corpus refuses everywhere else, so each site now carries a comment saying where
            the assertion went and why. The news leg is folded into the weighted-total
            assertion, which now exercises all five feed roles at once: six requests and 316
            weighted calls, up from five and 311, because news was counted in a separate class.
Proved:     the mutation the phase 2 sign-off ran, repeated against this tree. Deleting the
            stop in `Nightly.cs` now fails `nightly-cost` at
            `ANightAlreadyAtItsAllowanceStopsBeforeItsNextStepAndSaysWhy`, 10 of 11 passing,
            where at the sign-off it left the check green at 8 of 8 and the suite green at 312
            of 312. The mutation was reverted and `Nightly.cs` is byte identical to the commit
            before this one.
Found:      warnings as errors refused the first form of the mutation. Replacing the condition
            with a constant false produced CS0162 for unreachable code, so the branch was
            deleted outright, which is what the sign-off did and is the sharper mutation in any
            case. Worth writing down because it means this class of mutation cannot be made by
            disabling a condition in this tree and has to be made by removing the code.
            And the stop fires before `migrate`, not before `membership`. The first version of
            the test asserted the wrong step name and failed, which is the test finding the
            night's real first step rather than the step section 14 opens with. The assertion
            names `migrate` and says why: the property is that the stop precedes every step,
            and the empty run log is the other half of it.
Measured:   over the 42 PASS claims, the Weighted-call budget note's 3 clauses are now 3 of 3
            reached by the carrier, against 0 of 3 before. Tests 325, unchanged, being 3 added
            to `NightlyCost` and 3 removed from the two classes they backed nothing from.
            `nightly-cost` carries 11 tests, up from 8.
Carried:    the four rows 3.0's sweep opened stand, all due 3.1. This discharges the fifth.

### 3.1 - the membership index constraints asserted                          2026-09-09
Not a checkpoint entry. It belongs to 3.1, which has not landed, and the work is committed
            ahead of the checkpoint that owes it. This session committed code and may not
            sign it off.
Built:      five tests in `SchemaColumns`, which is `schema-columns`'s carrier and therefore
            the only class whose tests back the `Stores :: Membership` claim its reach already
            declared. Four are the sign-off's own proofs made permanent: a second span for a
            constituent with no join date refused, a second span for a dated one refused, two
            rows for one ticker both carrying no join date refused, and the control, three
            distinct spans for one ticker accepted. The fifth asserts the index's form off
            `sqlite_master` in the built store, that it is UNIQUE and folds the unknown, and
            against the sentence `SCHEMA.md` states.
            The inserts are direct rather than through `MembershipLoader`, because the loader's
            statement carries `ON CONFLICT` against the same expression, so a legitimate re-run
            upserts and never reaches the refusal. What is under test is the constraint.
Found:      the first version asserted `membership_span` in the refusal message, and the two
            mutations then failed the same three tests for two different reasons. SQLite names
            the index in the message for an expression index and names the columns for a plain
            one, so a message assertion made every refusal test fail the moment the index
            changed form, whether or not the row was still refused. Three tests that all go red
            for one reason are three tests saying one thing. The refusals now assert the codes,
            19 and 2067, and the form is asserted once on its own.
Proved:     two mutations against a migrated store, each reverted, the tree clean afterwards.
            Dropping `UNIQUE` fails 4 of 12: all three refusals and the form. Dropping only the
            `IFNULL` fold, keeping the index unique, fails 3 of 12: the two cases whose join
            date is unknown, and the form. The dated case stays green under that mutation,
            which is correct and is what shows the fold's own contribution rather than the
            index's. The control stayed green under both, which is what stops a refusal that
            refuses everything reading as a proof.
Not guarded: the empty-string edge, deliberately. A literal empty string in `joined` collides
            with the unknown and was confirmed to at the sign-off. `MembershipLoader` is the
            sole declared writer, and what it binds comes from `IndexConstituent.Joined`, a
            `DateOnly?` whose present values either parse as yyyy-MM-dd or throw, so the bound
            value is DBNull or exactly ten characters and no path produces an empty string. A
            guard would be code for a case the writer cannot reach. The reasoning rests
            entirely on that type, so the type is asserted rather than described: the day it
            becomes a string the test fails and the reader is told to reconsider the guard,
            instead of finding the collision in a store. The declared-writer half is asserted
            against SCHEMA's ownership row in the same test.
Measured:   `schema-columns` carries 12 tests, up from 6. Tests 331, up from 325. Over the
            migrated store, 1 index on `membership` and it is `membership_span`.
Widened:    `schema-columns`'s roster row, which said columns and types and now says what the
            check does, with prior text in `CHANGELOG.md`. A row saying less than its check
            does is the same defect as one saying more, read from the other end.
Carried:    the four rows 3.0's sweep opened stand, all due 3.1. This discharges the sixth of
            the seven obligations that were due there.

### 3.0 planning - the holes settled and phase 3's detail confirmed          2026-09-09
Not a checkpoint entry. It belongs to 3.0, which has not landed. The last commit of the
            planning pass. This session committed code and may not sign it off.
Decided:    three entries in `DECISIONS.md`, one per hole the holes table assigns to 3.0.
            **A heavy volume shelf creates a band of its own and also strengthens one it
            coincides with**, which is what figure 9.1 says in its first step and its last step
            separately, and which the four-sources decision already required: a source of
            levels that could only rank would be three sources and a modifier. It is also the
            reading the volume argument needs, because a range where almost nothing traded is
            a statement only a band existing on volume alone can carry.
            **The volume profile accumulates over the same sixty sessions as the level window**,
            one window and not two, because a shelf drawn from a different span puts a band on
            volume no other member of the level table can see and its share of the period
            becomes a share of a different period. The moving averages stay the stated
            exception, reading the full year, because an average is a price rather than a share.
            **A retracement is drawn between the last swing high and the last swing low**. The
            phrase "the retracements of the last two swings" reads equally as two swings of any
            kind, which allows a retracement between two highs, and a fraction of the distance
            between two highs retraces nothing. A window holding no swing of one kind produces
            no retracements rather than retracements drawn from a substitute end.
Citation pass: discharged, and the 1.8 judgement behind it was wrong. That entry said the 52
            uncited decisions were mostly things the architecture states no rule about. Reading
            the remaining sections found the opposite: nearly every one had a rule to sit at.
Measured:   111 citations in `ARCHITECTURE.html` covering 85 of the 90 current decisions, from
            39 covering 29 of 81 at 1.8. Placed across sections 1, 2, 4, 6, 9, 10, 11, 12, 13,
            14, 15, 17, 18, 19 and 20. Five stand uncited and each is named in the obligations
            table with the reason: the seam for a second universe, which is a data model
            `SCHEMA.md` carries; the outward-request scan's carve-out, which is about a check's
            internals; and three about how the build is run. A citation cannot be placed at a
            rule that does not exist, and writing spec text to give a citation a home would be
            moving the count rather than the coverage.
Found:      the citation reader could not carry a decision name containing an ampersand from
            `ARCHITECTURE.html` at all. Written properly as an entity it resolved to nothing;
            written raw it would have put invalid markup in the document to satisfy a reader.
            One decision has such a name and it had never been cited, which is why nothing had
            met this. The document form is now decoded before matching, which is a no-op for
            every name carrying no entity. This is the same class as the placeholder exemption
            deleted at the first commit of this pass: a reader that cannot express what the
            corpus contains, met by writing around it rather than by fixing the reader.
Carried:    two rows discharged here, the citation pass and its residue. Four stand at 3.1 from
            the sweep, plus the atomicity test, the fixture expectation sweep and 3.0's own
            expectations. 24 rows in the table, 8 of them open.
Tests:      331, unchanged. This commit settles rules and places citations and adds no test;
            the reader repair is covered by `decision-resolves`, which would have gone red on
            the first ampersand citation and did.
Phase 3:    the checkpoint split from 3.1 to 3.7 is confirmed against the size the work turns
            out to be and is unchanged. Nothing in the three rulings moves work between
            checkpoints: the shelf ruling makes the level builder at 3.4 collect four candidate
            kinds rather than three, which was already its shape; the window ruling ties 3.3's
            profile to a number 3.4 already reads; and the retracement ruling is a rule 3.4
            applies to swings 3.2 produces.
Predicted:  phase 3's new claims, named rather than counted, for the reason 2.7 gives. No new
            row in section 17: the level window row now states that the profile shares the
            window rather than a second row stating a second window, so the phase adds no
            limits claim. No new section 18 row: none of the three rulings describes a failure
            the document does not already carry. The phase's claims are the catalogue and
            matrix rows of the four components it builds, being the indicator engine, the swing
            finder, the volume profile builder and the level builder, which is 8 claims, plus
            the two level chart elements 1.3 left absent, the moving averages at 3.1 and the
            bands at 3.4. 10 claims, and the count is expected to stay at 185.

### 3.1 - contradiction K, and the ninth row it did not name                 2026-09-09
Not a checkpoint entry. It belongs to 3.1, which has not landed. `BUILD_PLAN.md` requires this
            repair in its own commit before any component declares a write, because the moment
            `IndicatorEngine` declares one, `component-access` produces a write nobody declared
            and a declaration with no cell behind it, which is two faults for one displaced
            letter. This session committed code and may not sign it off.
Built:      nothing. This is a document repair.
Repaired:   nine rows of section 16's read and write matrix, each now carrying the cells its
            catalogue row names. Indicator engine, Swing finder, Volume profile builder and
            Move annotator move their write from Listings to Computed tables; Shortlist builder
            from Forward returns to Listings; Facts assembler from Fundamentals to Facts;
            Forward return filler from Facts to Forward returns; News pulse counter from
            Research and theme to News pulse; and Research runner from Series state to Sources.
Found:      two things, and both because the repair reconciled every row against its catalogue
            row rather than repairing the eight the contradiction named.
            A ninth displaced row. The Research runner's write sat in **Series state** where its
            catalogue says source documents, one column to the right, which is the same
            displacement and was not in the contradiction's list.
            And a displaced read. On the Facts assembler the shift moved a read as well as a
            write: **News pulse** carried an R where the catalogue says fundamentals. A repair
            confined to writes, which is what the contradiction described, would have left that
            row failing on a read the moment the component landed at 5.3.
            A repair that fixes the list it was handed cannot find what the list left out. That
            is the reason to reconcile rather than to patch, and it is the same shape as the
            sweep two commits ago, where measuring the class was what showed the known instance
            was not the only one.
Measured:   over the 27 rows of the matrix, 23 are reconcilable against a catalogue phrase and
            all 23 now agree in both directions, cell by cell with the blanks included. The
            4 excluded each name a reason: the Read API reads every store and has no phrase to
            match; the Trend classifier writes none and returns its label to another component;
            the Shortlist builder's reads are contradiction L and are settled at 5.4; and the
            Ladder builder is contradiction E, below. Before the repair, 9 of the 23 disagreed.
Carried:    nothing new, and one contradiction widened rather than created. The Ladder builder
            carries a **Fundamentals** read that appears in no catalogue phrase, while its
            catalogue names a calendar the matrix has no column for. The likeliest reading is
            that the calendar read was written into the nearest column there was, which would
            make it a third face of contradiction E rather than a tenth displacement. That is a
            guess and it is recorded as one: E now carries it, and 4.0 settles it along with
            whether the other three calendar readers carry the same substitution. It is not
            repaired here because repairing a cell on a guess is how the displacement got in.
Tests:      331, unchanged. No check reaches these rows yet: `component-access` matches a row
            only when its component exists, and the four this phase builds land at 3.1 to 3.4.
            That is what makes this repair invisible to the suite and is exactly why the corpus
            requires it before the code rather than with it.

### 3.1 - the indicator engine and the averages on the chart                 2026-09-09
Built:      migration 7 creating `indicator`, the **Indicator engine**, and the level chart's
            third element. The ten values SCHEMA's name column enumerates, per name per
            session, each row carrying the bar count it was computed from: the three averages,
            the RSI, the three MACD readings, the ATR and the two volume averages. The
            arithmetic is in `IndicatorSeries` as pure functions of a session-ordered series,
            separate from the component that reads and writes, so the numbers can be asserted
            against arithmetic done by hand with no store in the way. Every definition is
            written out rather than named, because RSI names a family whose members disagree
            and the seeding is the whole difference.
            `Statistic`, the second money-boundary helper. `Money` is the storage crossing and
            this is the crossing from a price to a statistic, one way only: a statistic never
            becomes a price again, because an average of closes is not a price anything can be
            bought at. The rule asked for a helper named for the crossing and there had not
            been one, because nothing had needed it.
            The chart from 1.3 extended in place with the average lines, and the engine on the
            night as its own step. It is on the night because phase 3's visible output is the
            averages on the chart and a chart draws what a night computed.
Measured:   over the committed fixture, 7,560 indicator rows for 3 names over 252 sessions and
            10 indicators, of which 1,362 are not available, being 454 per name. The last
            session's 20-day average for AAPL is 313.14 by arithmetic done outside this
            repository over the captured adjusted closes, and the engine agrees to within a
            billionth. `sma200` carries a value on 53 of the 252 sessions per name, being the
            252 less the 199 the window has not filled. Tests 346, up from 331. The phase report
            is 186 claims, 47 pass, 0 fail, 139 out of scope, 0 unexamined, 53 reconciled
            against a floor of 34, and 33 checks on the roster of which 30 are carried.
Discharged: six of the seven obligations due here, and one re-pointed.
            The refetch's atomicity, as a property. The first version fell into the trap the
            obligation names: it dated its constructed bars outside the window the refetch asks
            for, the feed filtered them all away, and the empty-refetch guard refused upstream
            of the delete. It passed with the transaction removed, which is how it was caught,
            and the assertion that now keeps it where it has to be is that the refusal
            reason names the unique constraint rather than the empty year. With the bars inside the window the
            insert fails partway, and the stored series is asserted to be the old one entire,
            session by session and price by price. Removing the transaction turns it red and
            leaves the other eight corporate-action tests green, which is exactly what the
            phase 1 sign-off recorded.
            The fixture expectation sweep, per key rather than per file. 44 top-level keys over
            4 files, 3 unread and each named. It found something sharper than itself, below.
            The four carrier findings from 3.0's sweep. Retention and the retry policy figures
            moved into `NightlyCost` and `NightlyRun`, the carriers whose tests back those
            claims, rather than copied: the same assertion in two classes is the two-statements
            defect one layer along. The series state note stops claiming a marking
            `schema-columns` does not assert, which is the other resolution the obligation
            allowed. The corporate action checker's catalogue and matrix claims moved to
            `component-access`, with the reach declarations moved with them.
Found:      three things.
            Contradiction K was nine rows and not eight, and on one of them a read was
            displaced as well as a write. That is the previous commit and it is recorded there.
            The expectation sweep's own finding, which is sharper than the sweep. `closures` in
            the bars expectation is read, so a per-key sweep reports it covered, and it is read
            only through a count: the derived session figure is the weekdays in the window less
            the closures, and any weekday substituted for another leaves that arithmetic
            unmoved. That is why renaming a traded session as a market closure left the suite
            green at the phase 1 sign-off. The named dates are now tied to the store, each
            closure having no stored bar and every other weekday having one, and repeating the
            sign-off's mutation now turns it red. A coverage report cannot see this: the key
            was covered and the values were not.
            And a due point of this pass's own was wrong. **Phase 3's expectations owed for
            3.0's rulings** was written at 3.1 by the pass that landed the rule requiring a due
            point to name what produces the evidence, and 3.1 produces none of it: the
            indicator engine reads no profile, no swing and no shelf, so none of 3.0's three
            rulings is assertable here. Re-pointed to 3.4, cited by 3.4's text. The rule
            catching its own author within a day is the evidence that it works on something
            other than old text.
Corrects:   the claim prediction in the 3.0 entry two above, in both halves.
            It named 10 new claims for phase 3, being 8 catalogue and matrix rows for four
            components and the 2 level chart elements 1.3 left absent, and omitted the four
            fixture rows section 19.1 carries for indicators, swings, volume profile and levels.
            The figure is 14 and 5 of them landed here.
            And it said the total would stay at 185. It is 186, because the two-hundred-bar
            failure row was decomposed into two elements and a decomposition turns one claim
            into two. That was not foreseeable when the prediction was written and it is
            recorded rather than absorbed: the prediction was about claims the phase adds by
            building, and this one was added by reading a row more finely.
Amended:    nothing. This checkpoint amends no done condition.
Tests:      346. `tools/ci` green end to end, all 6 steps. `tools/verify-phase` green.
Carried:    nothing new. Two obligations stand for phase 3: the expectations at 3.4 and the
            volume shelf threshold at 3.6.
Notes:      section 14's fourth item lists nine things and one exists, so its claim stays out
            of scope until phase 5. The night's step is named `indicators` rather than for that
            item, because a step named "for every name: indicators, swings, volume profile,
            levels, trend state, ladder, moves, list reasons, facts file" that did one of them
            would be a step a reader counts as run.

            `value` is REAL and every other computed number in this store is TEXT. That is
            SCHEMA's declaration and it is the one place a number computed from prices is not
            money: an average of a price is a statistic about prices rather than a price,
            nothing quotes it as money, and `price-storage-form`'s money column list does not
            name it.

            A row is written for every name, session and indicator whether or not there is a
            value, so an absence is a stored fact rather than a missing row a reader has to
            interpret. That is what makes 1,362 a figure the run log carries and a jump in it
            visible.

### Addendum to 3.1 - what writing the entry before the run found      2026-09-09
Adds:       four things the entry above does not name, each surfaced by done condition 8. The
            suite was green before the entry was written and red after it, which is the whole
            of what that condition is for: the record is what the reconciliation reads, and a
            run against a tree whose entry is missing is a run that never asked the question.
Found:      the banned string, written by this session into the entry itself and caught by
            `banned-prose` on the next run.
            Eight claims due at "phase 3" rather than at a checkpoint. `HasLanded` reads a
            phase as landed from any checkpoint in it, so recording 3.1 made all eight name a
            point the record shows as reached. Each now names a checkpoint: the volume profile
            mark at 3.3, the momentum panel at 3.5, the distance row at 5.1, the six-table
            store row at 5.2 where the last of its tables arrives, the level window and band
            merge distance at 3.4, and the swing lookback at 3.2.
            The two-hundred-bar failure row, which this checkpoint half satisfies. It names a
            stored behaviour and a string on a page, and the page does not exist, so it is
            decomposed the way the gap row and the unavailable row were: the stored half passes
            at 3.1 and the displayed half is owed at 3.5. That is the third row read per
            surface and the element count moves from eight to ten.
            `fixture-replay`, rostered from 3.1 and implemented here because 3.1 landing is
            what made it due. It asserts the direction nothing else does: every table the
            replay of the whole pipeline populates is named by an expectation. On its first run
            it reported `series_state`, which the corporate action checker has written since
            1.6 with no expectation naming it, so the expectation was written and derived from
            the two captured action files intersected with the current members.
Carried:    two obligations, both due at 4.0 and both cited by its text.
            Section 19.1's fixture table lists ten expected files and the fixture holds three it
            does not name, being membership, fetch and series state. Adding a row to a
            claim-bearing table changes the claim count, which belongs in a planning pass.
            And 66 claims still name a phase rather than a checkpoint. Phase 3's eight failed
            the moment 3.1 landed; the other four phases hold 5, 20, 29 and 12, and each set
            fails on its own phase's first checkpoint in exactly the same way. It is due at 4.0
            because that is the last pass before phase 4's first checkpoint lands.
Measured:   over the 5 expectation files, 51 top-level keys and 4 unread, each named. The
            series state expectation added two keys when it landed and the per-key sweep
            reported both on the same run, which is the sweep working on something written
            after it rather than on the corpus it was built against.
Notes:      the entry above is left as it was written apart from its figures, which were filled
            in from the run that verified it as done condition 8 requires. What it says about
            the work is what was true when it was written.

### 3.2 - the swing finder                                                  2026-09-09
Built:      `SwingSeries` in Core, the arithmetic as a pure function of a session-ordered
            series; migration 8 creating `swing`; `SwingFinder` in the Worker, declaring the
            bar store it reads, the swings it inserts and updates and the run log it appends
            to; and `StoredSwings.AsOf` in Data, which is the reading half and is the reason
            this checkpoint has a second done condition.
Prices:     decimal throughout, and this component crosses no money boundary at all. A swing
            is a price, read as decimal and written as TEXT, so `Statistic.FromPrice` has no
            place here. That is the difference from the indicator engine next door, which
            computes statistics about prices from the same bars and stores them REAL.
Derived:    the expectation, computed from the committed bar files outside this repository and
            without running the finder. Each session's high and low adjusted by the factor the
            stored series carries, then the sessions whose high is above all six neighbours or
            whose low is below all six. 131 swings over three names: AAPL 44, being 23 highs
            and 21 lows; KEYS 41, being 20 and 21; MSFT 46, being 24 and 22. The whole diff is
            in the file rather than a count and a sample, because section 17's row names a
            fixture swing diff and a count agrees with a finder that marked the wrong days in
            the right number.
Asserted:   the lookback rather than agreed with it. Every diff above passes unchanged against
            a finder comparing two bars each side or four, because the expectation and the code
            would be wrong together. So the expectation names AAPL on 2025-09-16, whose high is
            above the four nearest neighbours and below the seventh session in its window: a
            two-bar lookback marks it and a three-bar lookback does not, and the test asserts
            both halves off the window and then asserts no row exists. A session the two
            answers disagree about is the only kind that can tell them apart.
Withheld:   a swing is not knowable on its own day, so `StoredSwings.AsOf` filters on
            `confirmed_on` and not on `session_date`. As of 2026-08-22, 43 of AAPL's 44 are
            visible and the one withheld is dated 2026-08-20 and confirmed 2026-08-25. It is
            in the store, it is dated before the as-of date, and a reader filtering on the
            session returns it: that is a peak nobody could have seen, and it is what a
            backtest walking a year forward would find an edge in. The test asserts the row is
            stored, that the reader does not return it, and that a later as-of date returns
            everything, so the filter is withholding by date rather than by anything else.
Reader:     written before anything reads it, which is unusual here and deliberate. Writing a
            swing is arithmetic over seven bars with one way to get it wrong; reading one is a
            filter, and the obvious filter is the wrong one. The level builder at 3.4 and the
            trend classifier in phase 4 both read swings as of a date, and neither would fail
            visibly if it read them the other way.
Notes:      `swing` has an inserter and an updater and no deleter, which SCHEMA declares and
            which is sound rather than an oversight. A swing at one session is decided by seven
            bars, all fixed once the third after it has closed, and bars are append-only, so a
            confirmed swing never stops being one. The single case that moves it is a corporate
            action, which rescales every price in the year by one factor, and a uniform
            positive factor cannot reorder seven prices: the swing survives with a new price,
            which is what the update covers.

            The strictness of the comparison is the whole of the tie rule. Two adjacent
            sessions sharing a high are a plateau rather than a peak, and marking both would
            put two swings at one price three days apart while marking one would make the
            answer depend on which end the scan started from.

            No session in this fixture is a swing high and a swing low at once. An outside day
            is both, SCHEMA's primary key carries the direction so the two rows sit side by
            side, and the count is stated in the expectation at zero so a fourth name that has
            one is a change somebody reads.
Reached:    four claims move to PASS. Section 19.1's swings row and section 17's swing lookback
            by `fixture-expectations`, and the swing finder's catalogue and matrix rows by
            `component-access`. The two residual due points 3.1 re-pointed here were removed in
            the same pass, so nothing names 3.2 as a point still to come.
Amended:    nothing. This checkpoint amends no done condition.
Tests:      352. `tools/ci` green end to end, all 6 steps. `tools/verify-phase` green.
Carried:    nothing new. Two obligations stand for the rest of phase 3: the expectations owed
            for 3.0's rulings at 3.4, and the volume shelf threshold at 3.6.

### Addendum to 3.2 - the two mutations                                     2026-09-09
Adds:       the mutation evidence for the two assertions this checkpoint turns on, run after
            the commit above and in an isolated worktree, so the tree that was verified is the
            tree that was committed.
Proved:     the lookback. With `SwingSeries.Lookback` set to 2, five of the six swing tests go
            red, 347 of 352 passing. The one that stays green is the second-run test, which is
            about the upsert and not about the lookback, so the split is the shape it should
            be rather than everything failing at once.
            The reader's filter. With `confirmed_on <= $as_of` changed to
            `session_date <= $as_of` in `StoredSwings`, exactly one test goes red, 351 of 352
            passing, and it is the as-of test. Nothing else in the suite can see that change,
            which is the whole argument for having written it: a filter on the wrong column
            returns rows, the arithmetic downstream runs, and the number looks like a number.
Notes:      the worktree was removed after each run and the working tree was never mutated.

### 3.3 - the volume profile builder and the profile mark                   2026-09-09
Found:      a hole the plan did not name, before anything could be built. `BUILD_PLAN.md`
            sends 3.3 to "the window settled at 3.0" and nothing in the corpus says how many
            price bands the profile is divided into. Section 17's shelf threshold is stated as
            "at least twice its even share of the period's volume", which is a fifth with five
            bands and a fortieth with forty, so the threshold rested on a number no document
            held. 3.0 settled the window and did not notice that the bands were the other half
            of the same question.
Decided:    two entries in `DECISIONS.md`, both cited from the code and from the document at
            the point each rule is stated.
            **The volume profile is twenty bands across the window's own range.** A fixed count
            rather than a fixed price width, because the threshold is one number for all five
            hundred names and a width in money puts three bands on a forty dollar name and
            three hundred on a four thousand dollar one. Twenty because section 15.5 already
            describes a support band holding a fifth of the period's volume in a twentieth of
            its price range, so the figure was read out of the document rather than invented
            beside it.
            **A name with fewer than sixty sessions gets no volume profile.** `share_of_period`
            carries no denominator on its row, so a profile over twenty sessions sits in the
            same table as one over sixty and reads identically. That is a figure over a mixed
            population, which `CLAUDE.md` already forbids stating at all.
Stated:     the band count in section 17's shelf threshold row rather than in a row of its own.
            A new limits row is a new claim, the count is read by nothing except this
            threshold, and a threshold written as a multiple of an even share means nothing
            without the number of shares it is even over. That is the move 3.0 made when it put
            the profile's window into the level window row instead of writing a second one.
            Prior text of both passages to `CHANGELOG.md`.
Built:      `VolumeProfileSeries` in Core, migration 9 creating `volume_profile`,
            `VolumeProfileBuilder` in the Worker, and the profile mark in `MarkRenderer`.
Spread:     each day's volume across the bands its own range covers, in proportion to the
            overlap, rather than placed at that day's close. A day that opened at 300 and
            closed at 320 did not trade every share at 320, and the level builder is looking
            for the prices holders actually paid. The close version is what most charting
            packages ship and it is the wrong figure for this use.
Apportioned: rather than rounded. The done condition is that the shares in the bands sum to the
            window's total volume, and flooring twenty fractions loses up to nineteen shares in
            a way nothing would show. The floors are taken and the shares left over go to the
            bands with the largest fractions, ties to the lower band so the answer does not
            depend on a sort being stable. The test reads the total off the bar table over the
            window rather than out of the expectation, so the profile is checked against the
            store it was computed from.
Derived:    the expectation outside this repository from the rules, in a second implementation
            over the committed bar files. It agrees with the builder exactly: sixty bands over
            three names, every band edge, every share count and every share of the period to
            nine places. Two implementations in two languages agreeing on an apportionment is a
            stronger statement than one agreeing with itself.
Axis:       the mark is drawn against the price axis the chart computed, which is 3.3's third
            done condition and the part that could have been drawn correctly and been wrong.
            `AxisFor` computes the scale once and both marks are given it. The test asserts
            three things: the two marks declare the same axis in their markup, the topmost band
            sits where the chart puts that price rather than at the top of its own pane, and
            the same bands drawn against a different axis move. Without the second, a profile
            that recomputed its own scale would pass the first.
Reached:    four claims move to PASS. Section 19.1's volume profile row by
            `fixture-expectations`, section 15.5's volume profile mark by `read-surface`, and
            the builder's catalogue and matrix rows by `component-access`.
Measured:   131 swings and 60 profile bands over three names. Every one of the three has a band
            holding at or above a tenth of the period's volume, which is twice an even share,
            so the shelf threshold has something to be true about in this fixture rather than
            waiting for 3.6 to give it one.
Corrects:   the residual due points 3.2 removed. `Scope.FixtureRows["swings"]` and
            `LimitDuePoints["Swing lookback"]` were deleted when their claims were reached, and
            the established shape keeps them: `indicators` at 3.1 and the two nightly-cost
            limits at 1.4 all stand in their maps with their checkpoints landed, because
            `Reached` answers first and the map is the record of where a row would be owed. The
            screens map made the same point loudly, refusing the run until section 15.5's
            volume profile row had an entry of its own. Both were restored in this pass.
Amended:    nothing. This checkpoint amends no done condition.
Tests:      362, and 363 after the addendum below. `tools/ci` green end to end, all 6 steps. `tools/verify-phase` green.
Carried:    nothing new. Two obligations stand for the rest of phase 3: the expectations owed
            for 3.0's rulings at 3.4, and the volume shelf threshold at 3.6.

### Addendum to 3.3 - three mutations, and the test one of them found wanting  2026-09-09
Adds:       the mutation evidence for 3.3, run after the commit above in an isolated worktree,
            and one test written because a mutation found a hole rather than confirmed a
            guard. The suite is 363 rather than 362 for that reason.
Proved:     the axis. With the profile mark computing its own low and high from its own bands,
            one test goes red, 361 of 362 passing, and it is the axis test. Nothing else in the
            suite can see that change, which is what a picture drawn correctly against the
            wrong scale looks like from the inside.
            The apportionment. With the largest remainder loop deleted so the shares are the
            floors alone, two go red: the sum test and the diff.
Found:      the spreading, and this is the one worth reading. Deleting the spread outright, so
            each session's whole volume lands in the band holding its low, turned the diff and
            the shelf test red and left
            `ASessionsVolumeIsSpreadAcrossTheBandsItsRangeCovers` green. That test asserts the
            expectation's stated overlaps against the band edges the builder wrote and asserts
            they sum to the session's range, and both are facts about the edges rather than
            about what the builder did with them. The test named for the spreading could not
            fail on the spreading being gone.
            So a second test was written where the answer is computable by hand: sixty sessions
            from 100 to 120 at a thousand shares each, twenty bands one wide, every session
            covering all of them, so every band holds exactly three thousand shares and exactly
            an even share of the period. A builder placing the day at one price puts all sixty
            thousand in one band. It carries the counter-case for the shelf threshold as well,
            a name whose volume is evenly spread having no shelf at all, which is what the
            four-sources decision says such a name loses nothing by.
Notes:      warnings as errors refused the first form of the spreading mutation, exactly as it
            did at 3.1: replacing the guard with a constant produced CS0162 for unreachable
            code, so the loop was deleted outright, which is the sharper mutation anyway.
            The worktree was removed after each run and the working tree was never mutated.

### 3.4 - the level builder and the bands                                   2026-09-09
Built:      `LevelSeries` in Core, migration 10 creating `level`, `LevelBuilder` in the Worker,
            and the bands drawn behind the candles in `MarkRenderer`. Figure 9.1's five steps
            in order: collect the four candidate sources over the last sixty sessions, merge
            what is closer than half a typical day's move, add every session that reached a
            band, assign roles against the close, and score.
Discharged: the obligation 3.4 carries (owes: Phase 3's expectations owed for 3.0's rulings).
            All three rulings are in the levels expectation and one test reads them. The
            profile window against the level window, which is observable rather than stated for
            the first time because a band resting on a shelf holds a member that came from the
            profile. The five retracements landing together on one band, which is what a set
            drawn between one high and one low looks like. And MSFT's band at 386.6219, whose
            only anchor is a volume shelf: it is a band on volume alone, it exists under the
            ruling 3.0 took, and it does not exist under the one 3.0 rejected. A discharge that
            pointed at the checkpoint rather than at that band would be one nobody could check.
Crossed:    the money boundary back, for the first time. `Statistic` said in its own comment
            that a statistic never becomes a price again, which was true of everything that
            existed at 3.1 and stopped being true the moment a moving average had to be a band
            edge. `CLAUDE.md`'s rule is that no conversion is implicit and that a helper
            crossing the boundary is named for it, in either direction; the restriction to one
            direction was that file's own. `Statistic.ToPrice` rounds to the store's four
            places rather than casting, which is the whole safety argument: a double holding an
            average may render as 287.33999999999997 and a price column holds four places, so
            the rounding is what makes the crossing lossless in the direction that matters.
Consolidated: the price form. `ProviderBarReader` had a private helper that rounded to four
            places and trimmed trailing zeros, and the level builder needed the same rule for a
            retracement and an average. It is now `PriceForm` in Core and the reader uses it,
            because two implementations of one fact is the defect this corpus refuses
            everywhere else. The trimming is not cosmetic: `low_edge` is a primary key column,
            and 327.5740 and 327.574 are one price and two rows.
Decided:    two entries in `DECISIONS.md`, both needed to build and both cited at the rule.
            **A volume shelf enters the merge as one price, the midpoint of its band.** Section
            9.1 collects a price band as a candidate and its key says a candidate is a single
            price; SCHEMA settles it by giving a member one price. Not academic: two of the
            three fixture names have a shelf narrower than half a typical day's move, where the
            two readings agree, and MSFT has one twice that, where they do not.
            **A member's date is the session its evidence occurred on, and a figure recomputed
            nightly has none of its own.** The strength score adds a point for a member from
            the last twenty sessions, and an average dated as-of is always from the last twenty
            sessions, so awarding it on date alone would hand the point to every band holding an
            average. A point every band scores ranks nothing.
Derived:    two rules rather than decided, and both are worth naming because the derivation is
            the cheaper move. A band whose low edge is below the close is support even where it
            contains the close, which the tranche eligibility decision already settles with the
            worked example's own numbers. And a retracement is measured from the end the move
            finished at, which is what the retracement decision's "in whichever order they
            occurred" means; the fractions are not symmetric, so the two ends produce different
            sets, and the test exercises both directions over constructed prices.
Removed:    a duplicated filter before it became one. The builder first read swings with its
            own `confirmed_on <= as_of` clause, which is a second copy of the rule 3.2 built
            with no test behind it: while the as-of date is the name's own last session the
            filter can exclude nothing, because a swing exists only where three sessions follow
            it. It now reads through `StoredSwings.AsOf`, so the rule lives in one place and
            3.2's test is what fails on it.
Measured:   20 bands over three names, 5, 4 and 11, with 177 members between them. AAPL's
            immediate support runs 299.7415 to 320.28 and scores 57: 54 members and all three
            bonuses. Its members are 39 touches, 6 swings, 5 retracements, 2 shelves and 2
            averages, which is the consequence worth watching: touches are members and they
            dominate the score, so the widest band is the strongest. That is the literal
            reading of the document and it is defensible, a band the price has visited forty
            times being genuinely well evidenced, but it is a ranking that four names will test
            better than three and 3.6 is where that happens.
Reached:    six claims move to PASS. Section 19.1's levels row, section 17's level window and
            band merge distance by `fixture-expectations`, section 15.5's level bands by
            `read-surface`, and the builder's catalogue and matrix rows by `component-access`.
            The level chart now has all four of its elements drawn.
Amended:    nothing. This checkpoint amends no done condition.
Tests:      374, and 375 after the addendum below. `tools/ci` green end to end, all 6 steps. `tools/verify-phase` green.
Carried:    nothing new. One obligation stands for the rest of phase 3, the volume shelf
            threshold at 3.6.

### Addendum to 3.4 - four mutations, and a tautology one of them found        2026-09-09
Adds:       the mutation evidence for 3.4, run after the commit above in an isolated worktree,
            and one test written because a mutation found a hole. The suite is 375 rather than
            374 for that reason.
Proved:     the merge distance. With half a typical day's move replaced by a whole one, five
            tests go red, 369 of 374 passing, which is the spread a band set changing shape
            produces.
            The role rule. With the role assigned on the high edge rather than the low, two go
            red, and one of them is the worked band: it contains the close, so it is the row
            the two readings disagree about, and it flips from support to resistance.
            The recency point. With the point awarded on date alone, so an average dated as-of
            wins it, two go red. That is the decision this checkpoint took, and it is the one
            that would otherwise have been invisible: every band holding an average would have
            scored a point every night and nothing would have said so.
Found:      a tautology in my own test. Widening a band to the range of all its members,
            touches included, left the whole suite green at 374 of 374. The reason is that a
            touch is only added where its price is already inside the anchors' range, so the
            two ranges are equal by construction and `ATouchSitsInsideItsBandAndTheEdgesAreThe
            AnchorsAlone` cannot fail on that line. The statement is true and the assertion is
            empty, which is what a test looks like when the property it guards is held by the
            shape of the code rather than by the line under test.
            The rule that is not a tautology is which sessions count. The document says any
            session high or low that reached a band, so a session that traded from 80 to 120
            through a band at 100 to 101 did not reach it, and one whose low landed at 100.5
            did. Both are choices a later session could take the other way, and the new test
            asserts both over constructed input.
Notes:      this is the second time in two checkpoints that a mutation strengthened a test
            rather than confirming one, and both were the same shape: an assertion written
            against the expectation and the stored row when the property lives in neither. The
            first was the volume spreading at 3.3. Worth carrying into the sign-off as a
            pattern rather than as two incidents.
            The worktree was removed after each run and the working tree was never mutated.

### 3.5 - the level summary table and the momentum panel                    2026-09-09
Built:      `MomentumPanel` and `LevelSummary` in `MarkRenderer`, and `ReadApi.LevelsAsync`
            beneath them. The panel draws each momentum reading on its own small axis with its
            neutral rule across it; the table names every band with its members and their
            dates.
Ruled:      the neutral rules, in Core beside the arithmetic rather than in the mark that draws
            them. An RSI's rule is 50, because section 5 says near 50 is balanced and above 70
            is stretched, so 50 is the rule and the other two are the context around it. The
            MACD family's is zero for all three, the gap closing being the event. They are
            facts about the readings and not about the picture: the report quotes an RSI
            against the same rule the panel draws, and two places holding one number is how
            they come to disagree. Asking for an average's neutral rule is refused rather than
            answered with a zero, which would draw a line through the middle of a price.
Bounded:    the RSI axis to 0 and 100 and the MACD axis to its own values. An RSI runs to a
            hundred whatever the stock does, so a quiet week reads as a quiet week rather than
            being stretched to fill the pane, and the test asserts the rule sits halfway down
            its own axis for that reason. A MACD is in the stock's money and has no bounds but
            its own. Each reading is scaled on its own axis, because one shared scale would
            flatten whichever of them has the smaller numbers into a straight line.
Surfaced:   the other half of section 18's two hundred bar row, which has been owed since 3.1.
            An average with no value cannot be a band member, so the summary table would have
            omitted it with nothing saying why, which is exactly the absence that row is about.
            The table names it in a row of its own reading "not available, nn bars", with the
            bar count that explains it, and the row is absent where nothing is absent rather
            than standing as a permanent caveat. That is the third failure row read per surface
            and the last of the three to be completed.
Notes:      the level summary table is not a mark. Section 15.5 states seven marks and this is
            not one of them; it is a region of the name screen listed in 15.9 beside the chart.
            It is written in the renderer because the marks and the regions that read them are
            drawn by the same server, and a second renderer is what the marks decision exists
            to prevent.

            A band of one price is written as one price rather than as a range from a number to
            itself, because the second reads as a mistake rather than as a level.
Reached:    two claims move to PASS, both by `read-surface`: section 15.5's momentum panel, and
            the displayed half of the two hundred bar failure row. Section 15.5 now has five of
            its seven marks drawn, the two remaining being the plan column at 4.4 and the marks
            phase 5 builds.
Amended:    nothing. This checkpoint amends no done condition.
Tests:      379. `tools/ci` green end to end, all 6 steps. `tools/verify-phase` green.
Carried:    nothing new. One obligation stands for the rest of phase 3, the volume shelf
            threshold at 3.6.

### 3.6 - the fixture widens to four names                                  2026-09-09
Captured:  three live requests on the operator's key, authorised for this checkpoint before any
            was made. The fourth name's year, the index constituents, and the 2026-09-08 bulk
            session asked for by date. The constituents and the bulk file were recaptured whole
            and re-trimmed rather than having a row spliced into the earlier ones, because a
            file holding rows from two captures is a file whose provenance is per row.
Chose:      NFLX, for character rather than for size. Its year holds a ten for one split on
            2025-11-17, so every bar before it is adjusted by a factor of a tenth and the
            adjustment path is exercised over a whole series rather than over the fractions of
            a percent a dividend moves. It also holds four sessions that moved more than seven
            percent from the close before them, which is the gapping character the phase table
            asks for, and after adjustment it trades between 65 and 127 against ranges of 245
            to 345, 350 to 520 and 155 to 375. The four names now differ in price scale, in
            range width and in whether the provider restated them.
Found:      a defect the widening exposed and nothing else would have. The volume profile's
            band edges were rounded with `decimal.Round` rather than through `PriceForm`, and
            those differ: `decimal.Round` trims a scale longer than four places and leaves a
            shorter one alone, so an edge landing on three places is written "66.006" and one
            needing four is written "382.3910", from the same expression. `band_low` is a
            primary key column. The three existing names all produced four place edges and the
            fourth, whose range is a fifth as wide, produced edges on both sides of that line.
            That is exactly the argument `PriceForm` was written for at 3.4, applied to a
            component built at 3.3 that predates it, and it was invisible until a name with a
            different price scale arrived.
Measured:   the populations the fixture's own tests had written down. Widening from three names
            to four turned 28 tests red, and repairing them touched twenty-five assertion sites
            across six test classes plus the line in `fixtures/README.md` that states the two
            counts. One of the twenty-five was a named constant covering six assertions, which
            is the shape the other twenty-four should have had. It is recorded rather than
            fixed here, because turning twenty-four literals into readings of the membership
            expectation is a change to how the suite is written and not to what this checkpoint
            builds, and it belongs to a pass that can make it whole.
Discharged: the obligation this checkpoint exists for (owes: Volume shelf threshold checked
            against four names). Section 17 set the threshold at twice an even share from one
            chart, and the obligation was that one chart cannot fix a threshold, so a discharge
            saying the fixture is now four names wide would not have done the thing. The figure
            is asserted against the candidates either side of it instead, and they fail in
            opposite directions.
            At one even share, seven to ten of every name's twenty bands are shelves, holding
            55 to 85 percent of the period between them. That names most of the chart and
            discriminates nothing.
            At three, three of the four names have no shelf at all, so the level builder loses
            its fourth source for them and the range where almost nothing traded becomes
            something the report cannot state.
            At two, every one of the four has a shelf, it is two to four bands of twenty, and
            those bands hold between a fifth and a half of the period's volume. That is a
            cluster. The figure derived from one chart survives four, and it survives because
            its neighbours were measured rather than because it was asserted alone.
Re-derived: every expectation for four names. Membership, backfill, fetch, series state,
            indicators, swings, volume profile and levels. The four computed ones were
            regenerated by the same second implementation outside this repository that produced
            them for three, and every figure agrees: 171 swings, 80 profile bands and 29 level
            bands with 232 members between them.
Reached:    one claim moves to PASS: section 17's volume shelf threshold, by
            `fixture-expectations`. It was found by writing this entry first, which is what
            done condition 8 is for: the row derives its due point from the plan, so recording
            3.6 made it name a point the record shows as reached, and the run went red on a
            question it had not been able to ask before.
Amended:    nothing. This checkpoint amends no done condition.
Tests:      380. `tools/ci` green end to end, all 6 steps. `tools/verify-phase` green,
            and its fixture line now reads 6 constituents and 4 names.
Carried:    nothing new. No obligation remains open for phase 3.

### 3.7 - the phase 3 report                                                2026-09-09
Built:      nothing. This checkpoint reports, and its done condition is that every phase 3
            claim is PASS across four fixture names and that the predicted claim total is
            checked against the actual.
Claims:     186, 64 pass, 122 out of scope, 0 fail, 0 unexamined, from 185 and 42 at the phase
            2 sign-off. 70 placements and verdicts reconciled against a floor of 34. All four
            names, since the fixture widened at 3.6 and every expectation was re-derived over
            it.
Predicted:  14 new claims, and 22 arrived. The prediction is checked here rather than quietly
            superseded, and the eight it missed are one shape rather than eight surprises.
            It named 8 catalogue and matrix rows for the four components phase 3 builds, the 2
            level chart elements 1.3 left absent, and, after 3.1's correction, the 4 fixture
            rows section 19.1 carries. Every one of those 14 arrived.
            The 8 it missed are: four section 17 limit rows, being the swing lookback, the
            level window, the band merge distance and the volume shelf threshold; two more
            marks in section 15.5, being the volume profile and the momentum panel; and the two
            elements of section 18's two hundred bar row.
Found:      the shape of that miss, which is worth more than the number. The prediction reasoned
            about what phase 3 adds to the document: it said in so many words that there would
            be no new row in section 17 and no new row in section 18, and both were true. What
            it did not follow is that a phase makes rows assertable that the document already
            had. Every one of the eight is a row that existed before phase 3 opened, and became
            a PASS because the component it constrains or the surface it describes was built.
            So the rule for 4.0 is: a claim prediction is over the rows that become assertable,
            not over the rows that get written. The second is a prediction about editing and
            the first is a prediction about building, and only one of them is what a phase does.
Total:      186, and the prediction of 185 was corrected to 186 at 3.1 when the two hundred bar
            failure row was decomposed into two elements. The phase added no claim by writing
            one: every one of the 22 was a row the document already carried.
Measured:   over the phase. 68 new tests, 312 to 380. Four migrations, 6 to 10, creating
            `indicator`, `swing`, `volume_profile` and `level`. Four components, and every one
            of them declares its access and is reconciled against its catalogue row, its matrix
            row and SCHEMA in both directions. Four new decisions. Five expectation files to
            eight. The fixture from three names to four and from five constituents to six. One
            new check, `obligation-reconciles`, and one that became due and was implemented,
            `fixture-replay`.
Verified:   every claim in scope, by 30 carried checks, none of which failed or went unrun. The
            marks: section 15.5 now has five of its seven drawn, the two outstanding being the
            plan column at 4.4 and the marks phase 5 builds. The level chart has all four of
            its elements.
Noted:      a pattern rather than a defect, twice in two checkpoints and found the same way
            both times. At 3.3 the test named for the volume spreading asserted the
            expectation's stated overlaps against the stored band edges, and deleting the
            spreading left it green. At 3.4 the test named for touches not widening a band
            asserted the edges against the anchors, and widening the band to all its members
            left it green. Both were true statements and empty assertions, and in both cases
            the property lived in the shape of the code rather than in the line under test.
            Both were repaired in their own checkpoints by a test that computes the answer by
            hand over constructed input, so no hole stands open. It is recorded here because a
            pattern that arrives twice arrives again, and because both were found by mutation
            and neither by reading.
Carried:    three obligations to 4.0, each cited by its text. Section 19.1's fixture table
            reconciled against the expectations that exist, and the claims still due at a phase
            rather than at a checkpoint, both from 3.1. And new from 3.6, the suite's fixture
            populations read from the expectation rather than written into each test: widening
            to four names turned 28 tests red and touched twenty-five assertion sites, one of
            which was a named constant covering six of them, which is the shape the other
            twenty-four should have had.
Amended:    nothing. This checkpoint amends no done condition.
Tests:      380. `tools/ci` green end to end, all 6 steps. `tools/verify-phase` green.
Notes:      this session has committed code to phase 3 and may not sign it off. The sign-off is
            owed on the phase as a whole, by a fresh session, before phase 4's plan.

### Phase 3 sign-off                                                         2026-09-09
Signed by a session that has committed no code to this repository. Its only commit is this
            entry, which is a document, so the fresh session rule permits it. The building
            session committed code at 3.2, 3.3, 3.4, 3.5 and 3.6, and the 3.7 entry says in its
            own last line that it does not sign the phase off.
Verified:   by re-running both gates on `main` rather than by reading the 3.7 entry.
            `tools/ci.ps1` green end to end, all 6 steps, 0 warnings, 380 tests passing inside
            it, migrations 0 to 10, exit 0. `tools/verify-phase.ps1` green at 186 claims, 64
            PASS, 0 FAIL, 122 out of scope, 0 unexamined, 25 tables, 70 placements and verdicts
            reconciled against a floor of 34, fixture PRESENT with 1 captured over 6
            constituents and 4 names, 33 checks on the roster and 30 carried, 30 ran and passed
            and none did not run. Windows PowerShell on this machine, at commit 74b66b9. Every
            figure the 3.7 entry states reproduces exactly.
Matrix:     read from the run that pushed 74b66b9 rather than from the machine at hand, because
            done condition 5 is about both runners. All three jobs green: matrix
            windows-latest, matrix macos-latest and case-sensitivity. The same holds for every
            one of the ten phase 3 merges, 28 through 37.
Plan:       all eight checkpoints, 3.0 through 3.7, are in `BUILD_PLAN.md` and all eight are
            recorded above. No open pull request and no unmerged branch. The carried obligations
            table holds 27 rows, 17 discharged and 10 open, and the earliest open one is due at
            4.0, so nothing is owed inside phase 3. Every one of the eight expectation files
            states an independent derivation and none is frozen from a run, which is done
            condition 7 read off the files rather than off the entries.

Swept:      the tautology pattern, which 3.7 recorded as a pattern rather than as two incidents
            and predicted would arrive again. It arrived again. Every mutation was made in an
            isolated worktree at 74b66b9 and reverted, the worktree was removed, and the main
            tree was not modified at any point.
            34 mutation runs over 32 distinct mutations, of which 4 were controls, run to prove
            the harness detects: the sixty session guard, the swing confirmation date, the short
            window average and the role rule each turned red in the test named for them. Of the
            27 distinct candidate mutations, 14 turned a test red and 13 left the whole suite
            green at 380 of 380.
            The 13 sort into three groups and only the first two are defects of the shape 3.7
            named.
Group one:  a test names the property and cannot fail on it. Two.
            `EveryMomentumReadingDrawsItsNeutralRule`, which is 3.5's second done condition. The
            panel is rendered from `IndicatorSeries.Momentum` and the test asserts the drawn
            names back against `IndicatorSeries.Momentum`, so both sides are the same constant.
            Dropping `macd_hist` from that set draws three readings instead of four and leaves
            the suite green. The hole is asymmetric and that is what identifies it: adding
            `atr14` to the set does turn the test red, because `NeutralOf` throws on a reading
            that has no neutral rule, so the test is sensitive to the set only through an
            exception and never through membership. The document enumerates the four nowhere, so
            there is no independent statement for a test to read; section 15 says only that the
            momentum readings sit on their own small axes.
            `SupportAndResistanceAreTheOnlyTwoHuesAndTheImmediateBandIsStronger`. It asserts that
            three fills carry exactly two distinct hues and that one contains the support token
            and one the resistance token. It never asserts which band got which. Swapping the two
            constants draws every support band in the resistance hue and leaves the suite green,
            because both tokens still appear.
Group two:  a property the code states and no test names. Two, and both matter downstream.
            `has_non_average_anchor` is computed over the anchors rather than over the whole
            band. Computing it over the whole band instead, so that a touch counts as a
            non-average anchor, leaves the suite green: every band in this fixture anchored on an
            average alone happens to carry no touch. SCHEMA's own note says the case that would
            separate them is common, that a short moving average follows the price and sits at it
            about half the time. This is the column 4.2 reads to decide that a band anchored only
            by an average carries no tranche, so the value decides whether a tranche exists.
            `LevelsForName` serves the latest as-of date only, and deleting the clause that binds
            `as_of` to the maximum leaves the suite green. The fixture holds one as-of date per
            name, so the two queries cannot differ over it. The comment states the reason the
            clause is there, that a page holding two nights of bands is a page holding two maps,
            and that is a fault which first appears on the second night rather than in any
            fixture.
Group three: a boundary or a population the committed fixture does not hold. Nine. The swing
            plateau rule and the outside day that is a peak and a trough at once, both in 3.2;
            the merge distance boundary and the role boundary at the close, both in 3.4; the
            retracement zero span guard; and four in 3.3, being the largest remainder tie order,
            the one price session, the collapsing edge guard, and the top edge taking the
            window's own high. Each is documented at length in the source and none is reachable
            from four names of committed bars. A fixture cannot hold every boundary and this is
            not the same defect as the first two groups. The repair where one is wanted is the
            one 3.3 and 3.4 already used, a test over constructed input rather than a wider
            fixture, and the swing outside day is the case that says so plainly: the test
            covering it asserts a stored count of zero, and its own comment says the count is
            stated so that a name which has one is a change somebody reads.
Not a reopen: under the stopping rules none of the 13 reopens the phase. No check broke and no
            done condition fails. In every case the shipped code is correct and what is absent is
            the assertion, which is the same class as the weighted-call stop carried out of the
            phase 2 sign-off, and it is carried the same way.
How to look: the sweep found nothing by reading that it had not already found by mutating, which
            is now the third time that has been true. It also found where the corpus was not
            looking. `PROGRESS.md` records mutation evidence for 3.2, 3.3 and 3.4 and none for
            3.5 or 3.6, and both group one findings and one of the two group two findings are in
            3.5. The pattern is not that some checkpoints write weaker tests. It is that the
            checkpoints which mutated their own work found their own holes, and the two that did
            not, did not.

Retention:  section 16's data stores matrix, and not section 12, is where the retention cell
            sits. It states one year, recomputed nightly and kept for the harness, over
            indicators, swings, volume profile, levels, ladders and moves, and `SCHEMA.md` gives
            Delete to nobody for any of the six. This is contradiction A and contradiction H a
            third time, and `SCHEMA.md` says so about itself twice already: the note under `bar`
            records the three-way form resolved at 1.4, and the note under the second table
            records that a retention window nobody owns is a table which grows forever while
            that file says it does not.
            It fails no done condition and breaks no check, and nothing stored today is wrong.
            Two things make it a ruling rather than a note. Its due point is 5.2, which is where
            the last of the six tables arrives and not where a deleter is built, so it is a
            deferral naming a point that produces no evidence, which is the defect 3.0 swept the
            corpus for. And the four tables that exist split in two on grain, which decides which
            of them the window is actually about. `indicator` keys on ticker, session date and
            name, and `swing` on ticker, session date and direction, so both replace and grow
            only with the series. `volume_profile` keys on ticker, as-of date and band low, and
            `level` on ticker, as-of date and low edge, so both write a new set every night and
            replace nothing. At twenty bands a name, and the 7.25 bands a name this fixture
            averages, five hundred names put about 10,000 profile rows and about 3,600 level rows
            into the store every night, which is of the order of three and a half million rows a
            year that nothing is declared able to remove.

Strength:   the strength score is dominated by touches, which 3.4 recorded as a consequence to
            watch on the one name it was written over. It now has four names of different
            character and the reading holds on all four. Measured over every band of every name
            in the levels expectation.
            The strongest band is the most touched band for all four names. Touches are 72, 73,
            79 and 78 per cent of the members of each name's strongest band. Anchor counts are
            bunched, at most 15 for AAPL and KEYS and at most 4 for MSFT and NFLX, while touch
            counts run from 0 to 41, so the whole dynamic range of the score comes from touches
            and almost none of it from the evidence that makes a level a level.
            MSFT is the case that shows it changing an answer and not only a number. Its top band
            by strength, at 492.1166, and the band at 369.6983 carry the same 4 anchors, and the
            first wins on 15 touches against 9. Ranking by anchors alone puts a different band
            first.
            Width buys the score twice, because chain merging lets a band grow past the merge
            distance and a wider band is reached by more sessions. AAPL's immediate support band
            is 20.54 wide against a merge distance of 3.82, and KEYS's is 31.91 against 5.64,
            which is 5.4 and 5.7 times the distance that is supposed to bound a band. 65 per cent
            of AAPL's sixty sessions and 68 per cent of KEYS's reached the band their own name
            calls strongest, which is to say that two thirds of the quarter traded through it.
            What makes this due rather than interesting: section 15 orders tonight's list by how
            many reasons fired and then by band strength, so strength is the tiebreaker deciding
            what the operator reads first, over at most twenty drawn rows. That surface is built
            at 5.4. The builder implements figure 9.1 literally and correctly and this is not a
            phase 3 defect. It is a question about the rule, and it should be settled before the
            surface that reads it exists.

As-of:      the level builder's as-of swing filter is unexercised through the builder, and it is
            not a defect. While the as-of date is a name's own last session the filter can
            exclude nothing, because a swing exists only where three sessions follow it. 3.4 read
            it through `StoredSwings.AsOf` rather than through a second copy of the WHERE clause,
            which is the right construction and puts the rule where 3.2's test can fail on it.
            That test is not itself a tautology: it asserts the withheld set is not empty over
            rows dated at or before the as-of date and confirmed after it, which is the
            population that makes the filter observable, and it carries the counter-reading in
            the same test. The mutation confirms it, since dating the confirmation on the swing's
            own day turns that test red. What is absent is any exercise on the path the level
            builder takes, and nothing can supply one until something asks for levels as of a
            date that is not the name's last session.

Prediction: 3.7's rule is right and it has nowhere to be applied. The rule it derived, that a
            claim prediction is over the rows which become assertable and not over the rows which
            get written, is stated in the 3.7 entry and in no spec. `BUILD_PLAN.md` states the
            practice for phase 1 at 1.8 and for phase 2 at 2.7, and 3.7's own done condition
            carries it. Phase 4 carries it nowhere: 4.0's text makes no prediction, and 4.7's
            done condition is that every phase 4 claim is PASS across four fixture names and the
            plan section renders whole, with no clause checking a predicted total against the
            actual. So the lesson of the eight missed claims has no phase to be tested against
            unless 4.0 puts the practice back. Recorded rather than repaired, because a sign-off
            does not edit a spec and the pass that plans phase 4 is 4.0's own.

Record:     `PROGRESS.md` is declared append only in `CLAUDE.md`'s document lifecycle table, and
            corrections are new dated entries. PR #37 deleted 70 lines from it, being the 3.7
            handover addendum, at the operator's direction, and CI stayed green. Nothing guards
            the rule: `changelog-reconciles` reads the five specs and `PROGRESS.md` is a record,
            so no check covers a deletion from a record. The removal was deliberate and is the
            operator's call on the operator's own record, and this entry does not restore it.
            What is recorded here is that the file no longer shows the addendum was written or
            withdrawn, that the reasoning survives only in the commit message of 11708c9, and
            that the append only rule is a stated property with no instrument behind it, which is
            the same shape as everything in the sweep above.

Carried:    six obligations created here, for 4.0 to enter in the table rather than by this
            entry, which edits no spec. Four are tests and two are rulings.
            The momentum panel's reading set asserted independently of the constant it is drawn
            from, due at 4.0. The four readings stated somewhere a test can read them, or named
            literally in the test, so that dropping one turns it red.
            The band hue mapping asserted, and not only the two hues, due at 4.0. A support band
            is drawn in the support hue and a resistance band in the resistance hue.
            `has_non_average_anchor` asserted over a band anchored on an average alone that a
            session reached, due at 4.2, which is the checkpoint reading the column to decide
            tranche eligibility. Constructed input rather than a wider fixture, since the fixture
            holds no such band.
            The level read surface asserted over two stored as-of dates, due at 4.0. One name,
            two nights, and the page holds the later night's bands alone.
            A ruling on the six retention rows, due at 4.0 rather than at 5.2. Either a deleter
            is declared for the four tables that exist and the two that do not, or section 16's
            retention cell is changed to say what is true, and the due point moves to the
            checkpoint producing the evidence rather than to the one completing the set.
            A ruling on the strength score against four names, due at 5.4, which builds the list
            that orders on it. The evidence is in this entry and needs no further checkpoint to
            produce it.
Noted:      three worktrees from a phase 1 workflow run remain registered under the repository's
            local worktree directory, all at f48d9d6. They are outside the tree the nightly runs
            from and they touch nothing, and they are recorded rather than removed because a
            sign-off changes no state it is measuring.
Tests:      380, unchanged from 3.7. Windows for this run; the matrix carries macOS and the
            Linux case-sensitivity job, and all three were green on 74b66b9.
Signed:     phase 3 is signed off at 74b66b9. Phase 4's plan is not opened by this session.

### 4.0 planning - the rulings, the calendar, and nine steps where there was one   2026-09-09
Not a checkpoint entry. It belongs to 4.0, which has not landed. This session has committed
            code and may not sign it off.
Built:      no component. Five commits: the done conditions and the record guard, the rulings and
            the calendar, the nightly step decomposition, the re-point and the replan, and the
            populations with five constructed cases.
Decided:    ten entries in `DECISIONS.md` and one supersession, covering every hole this
            document's holes table assigns to 4.0 and the four the operator settled first.
            **A ladder row is written for every index member every night**, carrying the trend
            state and a plan that states why it is empty. The trend-changed condition compares
            tonight's label to last night's, so under any other shape a name with no eligible
            band has no yesterday and the reason cannot fire on the transition into or out of a
            downtrend, which is the transition that changes the whole plan.
            **The trend state is read from the averages and the last two swings, and a name that
            cannot be classified says so**, with a fourth `trend_state` value. A name with fewer
            than 200 bars or fewer than two swings of the kind the rule reads is not classified,
            with the reason, rather than defaulted to a range. The label decides whether a plan
            exists, and a label over inputs nobody had is the same lie as an average of sixty
            bars called 200-day.
            **The trend classifier returns its label to the ladder builder**, not to a component
            built a phase after the screen that draws it.
            **A calendar event is fetched once for the whole index, and the calendar holds
            provider events only**, with `kind` carrying provider event kinds only.
            **The second book is keyed to a dated event, and an earnings print is the only kind
            on file**, superseding **The earnings trade is a second book**, whose body was about
            carrying a momentum entry through a date and whose name said earnings.
            **A tranche's condition is the pattern the price has made at its own band**, the four
            patterns tested in a stated order so exactly one matches, asserted in both directions.
            **Each traded exit sells an equal fraction of what is held, and the top of the ladder
            trails**.
            **A researched precondition attaches to a tranche and never creates one**, so no
            nightly component reads research.
            **The event setups' triggers are proposals until resolved setups can score them**,
            with the twelve figures stated as proposals on the page.
            **Every computed table's writer is its own deleter**, implemented at 4.2.
Amended:    the holes table's row on the share of size per tranche, rather than superseding a
            decision. The shares that fall with distance came from the hand-made report the
            worked example is drawn from, where a person decided how much to commit, and that is
            what **The plan places a position and never sizes one** reserves to the reader.
Found:      three components have shipped since phase 3 and no night has ever run one. The swing
            finder, the volume profile builder and the level builder are called only from the
            suite, so a production store holds no swing, no profile and no band, and the served
            route draws candles and averages alone. Nothing failed and nothing was going to:
            section 14 carried the nine per-name computations as one step whose due point was a
            phase, and a claim due at a phase cannot fail until that phase's first checkpoint
            lands. The step is now nine, six owed at 4.1 and three at their own phase 5
            checkpoints. This is what the phase-due claims were hiding, and it is the argument
            for the obligation rather than a coincidence beside it.
Also found: `BUILD_PLAN.md` said "the seven general done conditions" where `CLAUDE.md` said "All
            eight", and only the second is inside what `stated-counts` reads. Repaired with the
            ninth.
Counted:    the phase-due claims before anything moved. The obligation says 66, being 5, 20, 29
            and 12; the tree carried 65 at the commit that wrote the row, being 5, 20, 28 and 12,
            so the phase 6 cell was one out and the total with it. 64 stood when the re-point
            began, the difference being the step that had become nine. 63 were re-pointed and one
            was decomposed: "Earnings date missing" names the calendar saying the date is not on
            file and the earnings reason not firing, which are a phase apart.
Conditions: a ninth. At least one assertion a checkpoint added is mutated and shown to go red,
            chosen before the run by a stated rule rather than after by which assertion looks
            weakest, made in an isolated worktree and reverted, and recorded here. It binds from
            4.1. Three phases of evidence produced it: every checkpoint that mutated its own work
            found its own holes, and of the two that recorded none, one carries two of the three
            group one findings from the phase 3 sign-off and one of the two group two findings.
            It raises the cost of every checkpoint and it is the only verification here that has
            found a defect in the checkpoint performing it.
Guarded:    `PROGRESS.md`'s append-only rule, which was a stated property with no instrument.
            `record-append-only` reads the history as a high-water mark over the set of entry
            headings, so it names the entry that went rather than reporting that one did. The one
            removal this repository has made is named inside the check with its commit and its
            reason, and the exemption is asserted in both directions.
Mutated:    five, each in an isolated worktree at the commit it tests, reverted, the worktree
            removed, and the main tree untouched throughout. Removing an entry from `PROGRESS.md`
            turned `record-append-only` red naming the lost heading and the commit that wrote it.
            Removing the `calendar` alias turned the catalogue term check red, where the
            exemption it replaced would have let it pass. Widening the changelog reader rule to
            every file turned its own proof red. Re-pointing the levels step to a checkpoint that
            has landed turned the out-of-scope reconciliation red, which no claim could do while
            the nine were one step due at a phase. Widening `has_non_average_anchor` to the whole
            band and dropping the `as_of` clause from the level query turned exactly the two new
            tests red and nothing else, both having been green before this pass.
Repaired:   two readers, rather than working around either. The catalogue term check permitted
            between one and six unresolved calendar reads until 4.0 settled it, and that
            exemption went with the contradiction rather than outliving it. And a quoted line in
            `CHANGELOG.md` is prior text, which that file's own format declares it to be, so a
            citation inside one is being reported rather than made: without that, superseding a
            decision whose citation the changelog has to quote leaves only paraphrasing text the
            format requires verbatim or editing an append-only record.
Discharged: nine obligations. The fixture table reconciled, the phase-due claims re-pointed, the
            populations read from the expectation, the momentum reading set, the band hue
            mapping, `has_non_average_anchor`, the level surface over two as-of dates, the four
            volume profile boundaries, and the retention ruling.
Carried:    the obligations table holds 37 rows, 26 discharged, 3 operating and 8 open. The
            earliest open ones are the swing boundaries at 4.1 and the level boundaries at 4.4.
Replanned:  phase 4 to ten checkpoints, 4.0 through 4.9. 4.1 as written carried a new feed, a new
            table, a contradiction, a matrix column, a new component and a new nightly chain at
            once. Retention is its own checkpoint at 4.2 at the operator's direction, because a
            deleter built where the tables are first populated is a delete path whose first real
            exercise is a night nobody has watched.
Predicted:  phase 4's claims, named rather than counted, in two disjoint lists. A row this phase
            writes and then asserts is one movement and is named once.
            A, the rows that exist today and turn PASS in phase 4: the Trend classifier and
            Ladder builder catalogue rows and matrix rows, the Plan column mark, the Name
            screen's plan region, the Tranches and exits, Tranche eligibility and Earnings
            horizon limits, the no-eligible-band failure row, the gap row's level and plan
            element, and the `ladder` fixture row. Twelve.
            B, the rows this phase writes: the Calendar fetcher's catalogue and matrix rows, the
            Calendar store row, the nine elements replacing section 14's step 5, the calendar
            step, the not-classified failure row, the two elements replacing the
            earnings-date-missing row, and four fixture rows. Twenty written, two replaced.
            Seventeen of the twenty turn PASS in phase 4; the three that do not are the moves,
            list reasons and facts elements of step 5, owed at phase 5 checkpoints and named as
            out of scope rather than left inside the total.
            The arithmetic 4.9 checks: 186 less 2 replaced plus 20 written is 204, PASS 64 plus
            12 plus 17 is 93, and out of scope 111. 204 and 111 sum to the total.
            A is the list most likely to be short. That is where 3.7 was wrong by eight: it
            reasoned about what the phase adds to the document and missed that a phase makes
            assertable the rows the document already had.
Measured:   204 claims from 186, 67 PASS from 64, 137 out of scope from 122, 0 fail and 0
            unexamined throughout. 34 roster rows from 33 and 31 carried from 30. One new check.
            Ten decisions and one supersession. 37 obligation rows from 28.
            The claim total is already at the 204 the prediction names, because 4.0 is the pass
            that writes rows and the checkpoints after it assert them.
Tests:      387, from 380. `tools/ci` green end to end, all 6 steps. `tools/verify-phase` green.
Notes:      Windows for this run. The matrix carries macOS and the Linux case-sensitivity job.

### 4.1 - the nightly chain, the trend state and the ladder row          2026-09-09
Built:      the trend classifier, the ladder builder, migration 11 creating `ladder`, and the
            three phase 3 components wired into the night. The name page composing the chart
            region section 15.9 names.
Repaired:   what 4.0 found. The swing finder, the volume profile builder and the level builder
            had shipped in phase 3 and no night ran one: their only callers were in the suite, so
            a store this code wrote held no swing, no profile and no band while every phase 3
            assertion held over stores the tests built themselves. All three are now steps in the
            order section 14 states, and a test drives the night's own entry point and reads the
            tables back rather than assembling them.
            The same for the page. Every mark the chart region names has existed since 3.5 and
            the app served none of them: the route drew candles and averages. The region is one
            request rather than four because the profile is drawn against the chart's own price
            axis, and the composition lives in `EquityBrief.Web` rather than in the route so the
            suite asserts the shipped path rather than a copy of it.
Decided:    nothing new. Two things 4.0 got wrong about a component that writes nothing, both
            found by building it. The trend state and the ladder were two steps in section 14 and
            are one, because a classifier that writes nothing cannot be a stage of its own: it
            would either compute the label twice or carry it between steps in nothing. And the
            read and write matrix gave the classifier a run log write while its catalogue row
            says it writes nothing, which is contradiction K's shape in a row nothing could reach
            until the class existed. The cell is blank now, and the classifier is the one
            component in the catalogue that writes nothing at all.
Derived:    the trend states from the rule, outside this repository and outside the shipped code,
            over the committed bars and before the code was run against them. AAPL, KEYS and MSFT
            uptrend; NFLX range. The expectation states the averages and the last two swings of
            each kind beside each answer, so a disagreement is traceable to an input rather than
            to the verdict.
            NFLX is the case the rule is shaped for. Its close is above the 50-day average and
            the 50 is below the 200, so neither branch holds. Reading the close against one
            average alone would call it an uptrend and reading the swings alone would call it a
            downtrend, since its last swing low is below the one before it.
            KEYS is the nearest of the four to changing its answer, its close about a point above
            its 50-day average.
Mutated:    two, both in an isolated worktree at 8bd9cb3, reverted, and the worktree removed.
            The rule for choosing was stated first: mutate the assertion carrying the property
            this checkpoint exists to add, which is that the night runs the chain and writes a row
            for every member.
            Removing the swings step from the night turned the new night test red and nothing
            else, which is the fault 4.0 found made assertable.
            Narrowing the ladder's population from the index's members to the members with stored
            bars left the whole suite green. That is the finding below.
Found:      the every-name rule was asserted against a fixture that cannot tell it from a
            narrower one. Every current member of this fixture holds a year, so a row for every
            member and a row for every member with bars are the same four rows, and the rule the
            ladder row exists for had no test that could fail on it. The same shape as the band
            anchored on an average alone with no touch: not a gap in the fixture, a population
            unrepresentative in the one way that matters, which is worse because it looks like
            coverage.
            Repaired in this checkpoint rather than carried. A current member with no stored bars
            is a name that joined the index tonight before the backfill has run for it, which is
            the case the backfill exists for. It gets a row, its state is not classified, and its
            plan says no stored bars. With the test in place the same mutation turns it red at 5
            rows against 4.
Measured:   203 claims, from 204. The total falls by one because merging the two steps into one
            removed a claim, which is checked against 4.0's prediction at 4.9 rather than
            quietly absorbed. 79 PASS from 67, being the five nightly steps, the trend
            classifier's and the ladder builder's catalogue and matrix rows, the `ladder` fixture
            row, the not-classified failure row and the name screen's chart region. 124 out of
            scope from 137. 0 fail and 0 unexamined.
Owed:       the swing boundaries the committed fixture cannot reach are due here and are not
            discharged. The plateau rule and the outside day that is a peak and a trough at once
            are both unreachable from four names of committed bars, and the trend classifier
            reads swings without reaching either. Carried to 4.4, where the tranche rules read
            band edges and the other constructed-input tests over the level arithmetic are
            written, so the two sets land together rather than one checkpoint writing one of
            them.
Amended:    nothing. This checkpoint amends no done condition.
Tests:      393, from 390. `tools/ci` green end to end, all 6 steps. `tools/verify-phase` green.
Notes:      Windows for this run. The matrix carries macOS and the Linux case-sensitivity job.
            This session has committed code and may not sign the phase off.

### 4.2 - retention on the computed tables                              2026-09-09
Built:      the retention drop for `indicator`, `swing`, `volume_profile`, `level` and `ladder`,
            each in its own writer, declared in `SCHEMA.md`'s ownership table and in each
            component's access. `move` waits for 5.2, because `MoveAnnotator` does not exist.
Why here:   at the operator's direction, and the reason held. A deleter built in the checkpoint
            that first populates the tables is a delete path whose first real exercise is a night
            nobody has watched, over the tables whose growth is the argument for the ruling. 4.1
            wrote the rows and this asserts the drop against them.
Shape:      the `DELETE` is in each component's own file rather than in a shared helper, because
            a write is attributed to the file it appears in: a helper holding the statement would
            be a file that deletes and is declared nowhere, which is the same reading
            `bar-append-only` uses to permit a delete only in a declared deleter's file.
            The boundary is one year back from the newest stored session, read from the store
            rather than passed in, so a component run on its own drops what a night would. A
            store with no bars has no boundary and drops nothing, which is a different thing from
            a boundary of today.
Asserted:   per table rather than in one loop over a mixed population. Two of the five key on a
            session and three on an as-of date, and a figure over both would be a figure over the
            wrong population, which is the rule this corpus applies to every other count.
            Both halves for each table: the row below the boundary goes, and the row at the
            boundary stays. Without the second, a drop that emptied the table would read as a
            pass, and the boundary is exactly where an off-by-one lands.
Mutated:    the level table's boundary moved by one session, from below the date to at or below
            it, in an isolated worktree at the commit under test, reverted, the worktree removed.
            The rule for choosing was stated first: mutate the assertion carrying the property
            this checkpoint exists to add, which is that a row at the boundary survives. It
            turned the retention test red and nothing else.
Found:      two stale due points, by the mechanism done condition 8 exists for. Writing this
            entry made 4.2 a checkpoint the record shows as landed, and the calendar's fixture row
            and its nightly step were still pointed at 4.2 from before phase 4 was replanned to
            ten checkpoints. Both moved to 4.3, which is where the calendar fetcher now is. Had
            the entry been written after the run, the run would have been green on a question it
            never asked.
Found also: nothing new in the code. The mutation run turned `changelog-reconciles` red on
            this checkpoint's own commit, because the SCHEMA edit had no changelog entry yet.
            That is the check working and it is recorded because the same thing reached CI at
            4.1: a suite run before a commit exists cannot see that commit, so the check that
            reads history is one a local run always reads a commit behind. The habit that follows
            is to write the changelog entry with the edit rather than with the record.
Measured:   203 claims, 79 PASS, 0 fail, 0 unexamined, 124 out of scope, unchanged. This
            checkpoint adds no claim: section 16's retention row covers six tables and the last
            of them arrives at 5.2, so the row stays out of scope until then and what is
            assertable now is the declaration, which `writer-ownership` reconciles in both
            directions.
Amended:    nothing. This checkpoint amends no done condition.
Tests:      394, from 393. `tools/ci` green end to end, all 6 steps. `tools/verify-phase` green.
Notes:      Windows for this run. The matrix carries macOS and the Linux case-sensitivity job.

### 4.3 - the calendar fetcher and the earnings date                    2026-09-09
Built:      `IEarningsCalendarFeed` with its live implementation and its recorded double, the
            calendar fetcher, migration 12 creating `calendar`, the fetcher as a nightly step, and
            the next dated event on the name page's fact strip. Contradiction E's table now
            exists, so the store four components declare a read against is one something writes.
Captured:   before the parser was written, which is the rule 1.6 and 1.7 set and which this
            endpoint justified inside one request.
Found:      the provider files no confirmed-or-estimated flag. 4.0 gave this table a `status`
            column carrying `confirmed` or `estimated`, on the reasoning that a booked print and
            an unconfirmed one are different things. They are, and the payload has no such field.
            What it does carry is whether the report lands before the session or after it, which
            decides which bar prices the print, so `timing` replaces `status` and earns the column
            for the same reason the status was thought to.
            This is 1.2's membership parser one endpoint later. That one read a field the provider
            does not send and the fixture agreed with it for two checkpoints because the same
            session wrote both. Here nothing was written against the invented field at all,
            because the capture came first.
Also found: three things a parser written from the endpoint's name would have got wrong, each now
            asserted against the captured bytes. The rows sit under an `earnings` key rather than
            at the top level, where the bulk price file and both action feeds put theirs. `code`
            is a ticker and an exchange, `AAPL.US`, and the store holds `AAPL`. And there are two
            dates: `report_date` is when the report lands and `date` is the fiscal period it
            covers. On this capture AAPL reports on 2026-10-29 for a period ending 2026-09-30, a
            month apart, so a reader taking the wrong one puts every print early.
Decided:    the window is a quarter and not the twenty-session horizon, measured rather than
            assumed. Every name reports once a quarter, so ninety days holds every member's next
            print. The fixture's names report six and seven weeks out, which is outside a
            horizon-sized window: a window equal to the horizon would have made the earnings-soon
            condition fire on the day the provider published the date rather than on the name
            approaching it.
            And the request is by date range and never by symbol. The endpoint filters by symbol
            and a request carrying five hundred of them keeps the count at one while making the
            request itself grow with the index, which is the page-count defect wearing a different
            hat.
Measured:   the endpoint's weight, against the account's own request counter rather than from
            documentation, which is what the done condition asks and where the other four figures
            came from. One request over ninety days returned 22,526 rows worldwide and moved the
            counter by one, so the weight is 1 for a whole window. Recorded in section 17's row
            and in the runbook's weights paragraph together.
Fixture:    the capture is trimmed the way every other capture in this folder is, to the six
            constituents plus four the index does not hold, being a US listing, a Frankfurt one,
            a Stuttgart one and a Singapore one. That gives the fixture a population worth having:
            two names with a print, two with none, and a departed constituent whose print is
            inside the window and must not be stored. Three refusals are told apart rather than
            one standing for the others: outside the window, not a member, and no row at all.
            Every figure in the expectation was read off the captured file by hand before the
            fetcher was run against it.
Mutated:    the parser reading `date` where it reads `report_date`, in an isolated worktree at
            0aa6b4b, reverted, the worktree removed. The rule for choosing was stated first:
            mutate the assertion carrying the property this checkpoint exists to add, which is
            that the event date is the report and not the period. It turned both calendar tests
            red and nothing else.
Measured:   203 claims, 85 PASS from 79, 0 fail, 0 unexamined, 118 out of scope. The six are the
            Calendar store row, the calendar fetcher's catalogue and matrix rows, its nightly
            step, the `calendar` fixture row and the earnings-date-missing row's calendar half.
Amended:    nothing. This checkpoint amends no done condition.
Tests:      397, from 394. `tools/ci` green end to end, all 6 steps. `tools/verify-phase` green.
Notes:      Windows for this run. The matrix carries macOS and the Linux case-sensitivity job.
            Four live requests were made against the operator's key for the capture and the
            measurement: one probe over a month, one over the fixture names, one capture over the
            window, and two account reads that cost nothing.

### 4.4 - tranches, stops and the invalidation                          2026-09-09
Built:      the tranche arithmetic in `LadderSeries`, read by the ladder builder. Tranches on
            support bands whose low edge is below the close, nearest first, at most three,
            skipping any band with no anchor other than a moving average. Each stop a daily close
            below the low edge of the next support band beneath it. The invalidation the lowest of
            those stops, or a tranche's own low edge where no band sits beneath it.
Derived:    every figure from the rules, outside this repository and outside the shipped code,
            over the committed bars and the levels expectation, before the builder was run against
            them. AAPL and KEYS take two tranches, MSFT and NFLX three. The expectation states the
            band each stop comes from, so a disagreement is traceable to a band rather than to the
            answer.
Found:      the eligibility rule and the stop rule do different things with the same band, and
            AAPL is where it shows. Its 283.439 band is its 200-day average, so it carries no
            tranche, and it is the first tranche's stop: a band that cannot be bought is still a
            level the price has to break. A rule that only skipped such a band would move every
            stop below it down to the next tradeable one, which the mutation below confirms.
Conditions: the four patterns tested in the stated order so exactly one applies, which is the
            prefix shape the verification rules name as having arrived four times. The order is
            asserted where two conditions can both hold rather than left to whichever branch is
            written first: with the close inside the band and a session that closed below it, both
            available now and a failed breakdown are true, and the first wins.
            `ReachesTheZone` is the absence of any of the four rather than a fifth pattern. A
            tranche where nothing has happened at its band is not actionable yet, and saying so is
            stating the absence rather than inventing a pattern that did not occur.
Carried:    the trend-dependent stop, to 4.5. Section 10 says the stop trails the last higher low
            in an uptrend and sits at the next band down in a range, and 4.4 places every stop at
            the next band down. Three of the four fixture names are in an uptrend, and the literal
            trailing rule puts their stop inside the band the first tranche sits on: AAPL's last
            higher low is 300.5700 and its first tranche runs 299.7415 to 320.28. A stop inside
            the band being bought is not a stop, so this is a checkpoint's work rather than a line,
            and it lands with the trailing machinery at 4.5 (owes: The trend-dependent stop, which
            trails in an uptrend rather than sitting at the next band).
Discharged: the level boundaries and the swing boundaries the committed fixture cannot reach, both
            over constructed input: the merge distance boundary, the role boundary at the close
            and the retracement zero span with the tranche rules that read them, and the plateau
            rule and the outside day that is a peak and a trough at once.
Also found: the ladder builder reads the bar store and the catalogue said levels, indicators and
            the calendar. The close it places tranches against and the sessions the conditions are
            read over are both bars. Contradiction K's shape in a row nothing could reach until
            the component existed, and it was found by declaring what the component touches and
            watching `component-access` refuse the row.
Rewritten:  4.1's test that every plan is empty, rather than deleted. It now asserts the pairing:
            a plan with no tranche has a reason, one with tranches has an invalidation and a
            condition on every tranche, and the count of plans that placed something is stated so
            a builder that placed nothing fails. Left as it was it would have gone green on
            exactly that.
Mutated:    the stop taken from the next band that carries a tranche rather than from the next
            band, in an isolated worktree at aa249de, reverted, the worktree removed. The rule for
            choosing was stated first: mutate the assertion carrying the property this checkpoint
            exists to add, which is where a stop sits. It turned the fixture diff and the
            constructed band test red and nothing else.
Found also: two stale due points, by done condition 8's mechanism again. Writing this entry made
            4.4 a checkpoint the record shows as landed, and the plan column mark and the gap
            row's plan section were still pointed at it from before the replan. Both moved to 4.6,
            which is where the plan column and its tables are built. That is the second checkpoint
            in a row the ordering has caught, and the pattern is the replan rather than
            carelessness: every due point written as 4.x before phase 4 became ten checkpoints is
            a candidate, and each surfaces on the checkpoint whose number it names.
Measured:   203 claims, 87 PASS from 85, 0 fail, 0 unexamined, 116 out of scope. The two are the
            tranche eligibility limit and the no-eligible-band failure row.
Amended:    nothing. This checkpoint amends no done condition.
Tests:      402, from 397. `tools/ci` green end to end, all 6 steps. `tools/verify-phase` green.
Notes:      Windows for this run. The matrix carries macOS and the Linux case-sensitivity job.

### 4.5 - exits, the near-exit skip and the trailing stop                2026-09-09
Built:      one exit per resistance band above the price, nearest first, at most five. An exit
            closer than two typical days' moves to the blended entry is listed and not traded,
            with its reason on the row. Each traded exit sells an equal fraction of what is held.
            The highest traded exit is a trailing rule rather than a price.
Blended:    the entry the skip is measured from is the mean of the first two tranche zones'
            midpoints, or the first alone where there is only one. That is what section 17's row
            names and it is the price the position is carried at once the plan has staged what it
            can, rather than the first tranche's midpoint, which would measure the skip against a
            price the reader may never pay alone.
Discharged: the trend-dependent stop, carried out of 4.4. Read literally, the trailing rule puts
            the stop wherever the last swing low happens to be. On a name that has run a long way
            that is far below the band beneath, which is looser protection than the range rule
            gives, and a trailing stop that can sit below the range floor is not trailing
            anything. So the stop is the higher of the band beneath and the most recent swing low
            beneath the tranche: it rises as the structure makes higher lows and is never looser
            than the range rule.
Measured:   where the rule bites, stated rather than left to look like a rule that works. Of the
            ten tranches in this fixture the trailing rule moves exactly one stop: KEYS's first,
            from the band at 288.5134 to the swing low at 293.55. AAPL's and KEYS's lowest
            tranches get a stop where the range rule gives none, because each is the lowest
            support band its name has. MSFT's three all take the band, and its third is the case
            that argues for taking the higher of the two: its last swing low beneath 476.2534 is
            376.6809, a hundred points down.
Found:      MSFT trades none of its exits. Its only resistance band sits 16.90 above the blended
            entry against a bar of 23.65, so every exit it has is listed and not traded and it has
            no top of the ladder at all. A plan that says take nothing here is a different object
            from one with a trailing rule attached to nothing, and both are asserted rather than
            the first standing for the second.
Also found: the third checkpoint in a row where `changelog-reconciles` caught a spec edit with no
            entry, and the pattern is now legible rather than three incidents. Every checkpoint
            that discharges an obligation edits `BUILD_PLAN.md`, which is a spec, so every one of
            them owes a changelog entry in the same commit. It was found here by the mutation run
            rather than by CI, which is the earlier of the two places and the one a local suite
            can reach, because the mutation worktree carries the commit under test.
Mutated:    the skipped exit omitted rather than listed, in an isolated worktree at the commit
            under test, reverted, the worktree removed. The rule for choosing was stated first:
            mutate the assertion carrying the property this checkpoint exists to add, which is
            that an exit inside the noise is named and not acted on. It turned the exit diff red
            and nothing else.
Measured:   203 claims, 88 PASS from 87, 0 fail, 0 unexamined, 115 out of scope. The one is
            section 17's tranches and exits row.
Amended:    nothing. This checkpoint amends no done condition.
Tests:      404, from 402. `tools/ci` green end to end, all 6 steps. `tools/verify-phase` green.
Notes:      Windows for this run. The matrix carries macOS and the Linux case-sensitivity job.

### 4.6 - the plan column mark and the tables                            2026-09-09
Built:      the plan column, one vertical price axis with the close marked in it, and the tranche
            and exit tables beside it, all three on the name page. Everything above the marker is
            a sale and everything below is a purchase, which is legible without a caption, and it
            is one column rather than two facing sides because a reader should not have to learn a
            convention before reading it.
Asserted:   the containment property applied to pictures, in both directions. Every price the
            figure draws is one the stored plan carries, and every price the plan carries is
            drawn. The first is the done condition; the second is the half a figure that quietly
            omitted a stop would pass, and it is what the mutation below turned red.
            Read off the mark's own data attributes and matched against the store rather than by
            eye, so an invented figure fails rather than looking plausible.
Found:      the invalidation was drawing a second rule on top of the lowest stop. It is the lowest
            stop, so a row of its own put two horizontal rules on one line saying two things where
            section 15.5 says the figure says one. The stop at that price is relabelled instead,
            and a row is added only where no stop sits there, which is a structure whose lowest
            tranche has no band beneath it. Found by the test asserting a single row at the lowest
            price rather than by reading the picture.
Mutated:    the stop rows dropped from the figure, in an isolated worktree at the commit under
            test, reverted, the worktree removed. The rule for choosing was stated first: mutate
            the assertion carrying the property this checkpoint exists to add, which is that the
            figure draws what the ladder carries and nothing else. It turned the containment test
            red and nothing else, and it is the direction a one-way assertion would have missed:
            a figure that draws fewer prices than the plan holds contains nothing it should not.
Closed:     section 18's gap row, whose other element waited for these sections to exist. A name
            whose series was refused holds no bars, so it has no bands and no plan, and both
            sections state the absence rather than drawing nothing. The counter-reading runs over
            the same store: a name whose series was not refused carries both, without which this
            would pass over a pipeline that computed nothing for anybody.
Measured:   203 claims, 91 PASS from 88, 0 fail, 0 unexamined, 112 out of scope. The three are the
            plan column mark, the name screen's plan region and the gap row's level and plan
            element.
Amended:    nothing. This checkpoint amends no done condition.
Tests:      406, from 404. `tools/ci` green end to end, all 6 steps. `tools/verify-phase` green.
Notes:      Windows for this run. The matrix carries macOS and the Linux case-sensitivity job.

### Correction to 4.6 - the deadline test raced again, one order of magnitude up   2026-09-09
Corrects:   nothing in 4.6's figures. The suite was green on this machine and on both other CI
            legs, and red on one windows runner at the push, on
            `ANightThatPassesItsDeadlineStopsAndSaysSo`. That runner took seven and a half
            minutes over a suite the other windows leg ran in four.
Found:      the same defect the 2.7 correction repaired, at ten times the scale. 2.7 gave the test
            a quarter-second deadline and a cold runner beat it; the correction warmed the store
            and raised the bound to three seconds, calling it generous. A runner twice as slow,
            over a suite half again as large, beat three seconds too.
            The shape is what matters rather than the figure. A bound written as an absolute
            number assumes a machine speed, and this corpus states the rule already: a test whose
            answer depends on how fast the machine is has no answer. Raising the number is the
            habit; it passed for four checkpoints and then did what it did before.
Repaired:   the deadline is measured rather than stated. The warm night is timed, and the bound is
            twice that plus two seconds, so a runner ten times slower produces a warm night ten
            times longer and a deadline ten times larger. The arrangement scales with the machine
            instead of assuming one.
            And the arrangement is now asserted rather than assumed: the feed's own request count
            says whether the night reached the fetch at all, so a deadline that fired on an
            earlier step fails with a message naming the arrangement rather than reporting on a
            step the test did not mean to name.
Why here:   `two-platform` made it visible again, which is the second time it has caught this and
            the reason the matrix exists. Both windows legs ran the same commit and disagreed.
            Under the stopping rules a finding that breaks a check is repaired rather than
            carried, and this broke one.
Tests:      407, unchanged. The repair changes an arrangement and asserts one more thing about it;
            what is claimed is unchanged.

### 4.7 - the event book                                                 2026-09-09
Built:      three setups keyed to the print, each with a trigger, an entry, a stop and a target,
            kept as a second book that never merges with the position book. Every figure is a
            proposal and the page says so once for the book and once on every row, because a
            figure on a screen gets acted on and one that looks measured and is not is the failure
            that statement exists to prevent.
Population: two of the four fixture names have a dated event and two do not, so the rule that a
            name with no date produces no setups and says why is reached from the fixture rather
            than constructed. That is the capture at 4.3 earning its trimming: a window holding
            every name would have made this rule unassertable.
Found:      three arrangements of these rules that would have passed against an expectation frozen
            from the code, all three by deriving the figures first.
            A stop at a zero-width band low edge is the entry. Most bands in this fixture are one
            price, so the setup could never win and would stop out on the session that triggered
            it. The stop is a typical day's move below the band, which is the rule the position
            book's stops already follow and for the same reason.
            A target taken as the next band can sit below a gap-up entry, because the entry is
            above the band it cleared. NFLX's gap up entered at 81.7058 and would have targeted
            81.16. The target is the first band above the entry.
            And the flush entered at the band's high edge and targeted the close. AAPL's close
            sits inside its immediate support band, so that setup had a reward of minus 0.31. It
            enters at the low edge, where a flush lands, and targets the price it flushed from.
Also found: one assertion of mine was wrong rather than the code. A setup sharing a price with a
            tranche is not the books merging: both read the same band, and AAPL's flush enters at
            the low edge its first tranche sits on. What separates the books is structural, a
            setup carrying its own stop and a target where a tranche has neither, and that is what
            is asserted now.
Mutated:    the setup stop moved back to the band edge, in an isolated worktree at the commit under
            test, reverted, the worktree removed. The rule for choosing was stated first: mutate
            the assertion carrying the property this checkpoint exists to add, which is that a
            setup can be won. It turned the fixture diff and the constructed reward test red and
            nothing else.
Measured:   203 claims, 92 PASS from 91, 0 fail, 0 unexamined, 111 out of scope. The one is
            section 17's earnings horizon row.
Amended:    nothing. This checkpoint amends no done condition.
Tests:      410, from 407. `tools/ci` green end to end, all 6 steps. `tools/verify-phase` green.
Notes:      Windows for this run. The matrix carries macOS and the Linux case-sensitivity job.

### 4.8 - the arithmetic and the earnings rule                           2026-09-09
Built:      risk per tranche at the zone midpoint, reward to risk from the first tranche and from
            the blended first two, the break-even each plan demands of itself, the worked sizing
            paragraph, and the last two prints one-day moves stated against the stop distance.
Derived:    every figure from the plan rather than stored beside it. The ladder holds prices and
            the arithmetic turns them into the figures the report states, so the report and the
            score cannot become two implementations of one arithmetic.
            The target is the first traded exit and not the first exit, because a skipped one is
            named and not acted on, and measuring reward to it would be measuring a trade the plan
            says not to take.
            The break-even is asserted against cases computed by hand rather than against the
            code's own output, including the worked example section 13 already carries: risking 65
            to make 137 breaks even at about 32 per cent.
Widened:    the calendar window, to a year behind as well as a quarter ahead. The earnings rule
            needs the dates the last two prints happened on, and the endpoint answers with
            historical and upcoming events over whatever range it is asked, so one request carries
            both. A year and no further, because the moves are read off bars kept for a year: a
            calendar reaching further back would name a print whose session the store does not
            hold. One more live request, and the capture retaken over the wider window.
Found:      the timing column earning its place, which 4.3 could argue for and not demonstrate. A
            report before the open moves that day's bar and one after the close moves the next, so
            the same date with the two timings reads two different sessions. Every print in this
            fixture lands after the close, so each is read off the session following its date.
Also found: the stop distance and the arithmetic are independent, and conflating them cost a
            reading. MSFT trades no exit, so it has no reward to measure and its arithmetic is
            absent; it still has a stop and two prints to measure against it. My first derivation
            took the stop distance from the arithmetic and left MSFT's share empty, which the hand
            derivation showed before the code was compared to it.
Reading:    NFLX is the name this rule exists for. Its last two prints moved it 3.91 and 2.02
            times the distance to its own stop, so a position carried through the date is one the
            plan's stop cannot protect. MSFT's moved it 1.40 and 5.09 times. AAPL's moved it 0.33
            and 0.92, which is a name whose print the plan can carry.
            AAPL's first tranche is the other reading worth stating: its reward to risk is 0.9292,
            so the plan demands being right 51.83 per cent of the time. A plan that has to be right
            more than half is one the reader should see stated rather than inferred.
Mutated:    the timing no longer deciding the session, in an isolated worktree at 96b81b7,
            reverted, the worktree removed. The rule for choosing was stated first: mutate the
            assertion carrying the property this checkpoint exists to add, which is that a print
            after the close moves the next session. It turned the fixture diff and the constructed
            timing test red and nothing else.
Measured:   203 claims, 92 PASS, 0 fail, 0 unexamined, 111 out of scope, unchanged. This checkpoint
            adds no claim: the arithmetic is the plan section's content rather than a row of any
            claim-bearing table, and section 15.9's plan region passed at 4.6.
Amended:    nothing. This checkpoint amends no done condition.
Tests:      415, from 410. `tools/ci` green end to end, all 6 steps. `tools/verify-phase` green.
Notes:      Windows for this run. The matrix carries macOS and the Linux case-sensitivity job.

### 4.9 - the phase 4 report                                            2026-09-09
Built:      nothing. This checkpoint reports, and its done condition is that every phase 4 claim
            is PASS across four fixture names, that the plan section renders whole, and that the
            claims 4.0 named are checked against the actual.
Claims:     203, 92 pass, 111 out of scope, 0 fail, 0 unexamined, from 186 and 64 at the phase 3
            sign-off. 98 placements and verdicts reconciled against a floor of 34. All four names,
            over the fixture 3.6 widened and the calendar capture 4.3 added to it.
Predicted:  204 claims, 93 PASS and 111 out of scope. The actual is 203, 92 and 111.
            One claim short in both totals, and it is the same claim: 4.0 predicted section 14's
            step 5 becoming nine elements, and 4.1 merged two of them back into one. A classifier
            that writes nothing cannot be a stage of its own, so the trend state and the ladder are
            one step rather than two, and the nine became eight.
            The prediction's composition held everywhere else. Twelve rows that existed turned
            PASS and sixteen of the nineteen the phase wrote did, with the three that did not being
            the moves, list reasons and facts elements owed at phase 5 checkpoints and named as out
            of scope rather than left inside the total.
Prediction: 3.7's rule held and it was the right rule. It said a prediction is over the rows that
            become assertable rather than the rows that get written, and phase 4 wrote 19 rows and
            asserted 28. What 4.0 could not have known is that building a component would change
            the document: the merged step is a row the phase removed, and no prediction made before
            the code exists can see that.
            The lesson for 5.0 is narrow and worth stating: predict the rows a phase writes as a
            range rather than a figure where the phase invents a component, because a component
            that turns out to write nothing changes the shape of the section that describes it.
Measured:   over the phase. 35 new tests, 380 to 415. Two migrations, 10 to 12, creating `ladder`
            and `calendar`. Three components, being the calendar fetcher, the trend classifier and
            the ladder builder, and every one declares its access and is reconciled against its
            catalogue row, its matrix row and SCHEMA in both directions. Ten decisions and one
            supersession. The obligations table went from 27 rows to 38 and from 17 discharged to
            29, so eleven were entered and twelve were discharged. One new check,
            `record-append-only`. One new feed, one new store, one new mark, and the fixture gained
            a calendar capture and a ladder expectation.
Verified:   every claim in scope, by 31 carried checks, none of which failed or went unrun. The
            marks: section 15.5 now has six of its seven drawn, the outstanding one being the
            distance row phase 5 builds. Section 14's list runs end to end for every step phase 4
            owes.
Found:      nine things by building, and they are the phase's real output beside the code.
            Three components had shipped in phase 3 and no night ran one, which one step with a
            phase-due point had hidden.
            The provider files no confirmed-or-estimated flag, which 4.0 had given the calendar a
            column for, caught by capturing before the parser was written.
            The trend classifier's matrix row gave it a run log write and its catalogue row says it
            writes nothing.
            The ladder builder reads the bar store and its catalogue row did not say so.
            The every-name ladder rule was asserted against a fixture that could not tell it from a
            narrower one, found by mutation.
            Three arrangements of the event setups would have passed against an expectation frozen
            from the code: a stop at a zero-width band's edge is the entry, a target taken as the
            next band can sit below a gap-up entry, and a flush entering at the band's high edge
            targeted a price below its own entry.
            The invalidation was drawing a second rule on top of the lowest stop.
            The stop distance and the arithmetic are independent, and conflating them left a name
            with no reward to measure showing no prints either.
            And the deadline test raced again, one order of magnitude up from where 2.7 repaired
            it, which `two-platform` caught for the second time.
Pattern:    seven of the nine were found by deriving figures by hand before running the code
            against them, or by mutating an assertion. Two were found by declaring what a component
            touches and watching a check refuse the row. None was found by reading.
            Done condition 9 was added at 4.0 on three phases of evidence and it earned itself in
            this one: the every-name rule and the near-exit skip were both mutations that went
            green first.
Carried:    six obligations, none owed inside phase 4. The earliest are the strength score at 5.4
            and the news request at 5.5. Three operating rows stand, being the six reason
            thresholds, the three reason records and the event setups' triggers, each with a
            numeric trigger and the surface it is read on.
Amended:    nothing. This checkpoint amends no done condition.
Tests:      415. `tools/ci` green end to end, all 6 steps. `tools/verify-phase` green.
Notes:      this session has committed code to phase 4 and may not sign it off. The sign-off is
            owed on the phase as a whole, by a fresh session, before phase 5's plan.

### Phase 4 sign-off                                                         2026-09-09
Signed by a session that has committed no code to this repository. Its only commit is this
            entry, which is a document, so the fresh session rule permits it. The building
            session committed code at every checkpoint of phase 4 and says so in its handover.
Verified:   by re-running both gates on `main` rather than by reading the 4.9 entry.
            `tools/ci.ps1` green end to end, all 6 steps, 0 warnings, 415 tests passing inside
            it, migrations 0 to 12, exit 0. `tools/verify-phase.ps1` green at 203 claims, 92
            PASS, 0 FAIL, 111 out of scope, 0 unexamined, 25 tables, 98 placements and verdicts
            reconciled against a floor of 34, fixture PRESENT with 1 captured over 6
            constituents and 4 names, 34 checks on the roster and 31 carried, 31 ran and passed
            and none did not run. Windows PowerShell on this machine, at commit 5d669d9. Every
            figure the 4.9 entry states reproduces exactly. The build was run again cold with
            `--no-incremental`, so the 0 warnings is over a full compile and not a cached one.
Matrix:     read from the runs that pushed each merge rather than from the machine at hand,
            because done condition 5 is about both runners. Jobs were enumerated rather than
            run conclusions read, since a skipped job leaves a run green and the two are
            indistinguishable from the conclusion alone. Ten merges, PRs 39 through 48, carry
            20 runs of 3 jobs each: matrix windows-latest, matrix macos-latest and
            case-sensitivity. All 60 jobs succeeded and none was skipped or cancelled.
Plan:       all ten checkpoints, 4.0 through 4.9, are in `BUILD_PLAN.md` and all ten are
            recorded above. No open pull request and no unmerged branch. The carried
            obligations table holds 38 rows, 29 discharged, 3 operating and 6 open, and the
            earliest open one is due at 5.4, so nothing is owed inside phase 4. All six open
            due points are in the plan and none is recorded as landed. Every one of the ten
            expectation files states an independent derivation and none is frozen from a run,
            which is done condition 7 read off the files rather than off the entries. No due
            point in `Scope.cs` is a phase: all 36 distinct due points are checkpoints, which
            is the positive half of a sweep whose negative half returns nothing and is
            therefore self-validating on its own. Mutation evidence is recorded at every
            checkpoint from 4.1 to 4.8; 4.9 builds nothing and records that it amends no done
            condition, so condition 9 has no assertion to bind to there.
Swept:      38 mutation runs over 36 distinct mutations, all made in an isolated worktree at
            5d669d9 and reverted, the worktree removed, and the main tree modified at no point.
            The rule was stated before the sweep and not after: every mutation targets a site
            where phase 4 code asserts a shape rather than a value, being a branch order, a
            reference identity, a relabelling or a fall-through guard, because a wrong shape
            returns a well-formed answer that no eye catches. The four the handover named were
            taken first, then the phase 4 source was swept for every other site carrying those
            same four shapes, so the set is found rather than chosen.
            One of the 36 was invalid and is not counted below. The projection meant to break
            `ExitsFor`'s reference identity was applied before `traded` derived from it, so
            identity survived and the mutation was a no-op. Corrected, it turns a test red.
            Of the 35 valid mutations, 23 turned a test red, 1 failed to compile because a test
            asserts against the constant directly and the analyzer refuses the result, and 11
            left the whole suite green at 415 of 415. The 23 reds are the evidence that the
            harness detects, in place of separately run controls.
Group A:    equivalent under an invariant the suite does not state. Two, and neither is a
            defect in shipped code.
            `ExitsFor`'s `ReferenceEquals(band, top)` replaced by value equality stays green,
            because two resistance bands cannot be value-equal once merging has run. The
            handover's actual worry, that a rewrite projecting the bands would break identity
            silently, is not borne out: projected so that `top` is a different instance, a test
            goes red. That worry is answered here rather than carried.
            `PlanRows`'s `FindIndex` replaced by `FindLastIndex` stays green, because stops are
            strictly decreasing and at most one stop row can sit at the invalidation price. The
            relabel cannot lose a stop, and it cannot by an argument that nothing asserts. Each
            stop is at or above the low edge of the band beneath its tranche, and the next
            tranche's stop is below that tranche's own low edge, so no two tranches share a
            stop. That is the property the relabel rests on and it is the one to write down.
Group B:    a property the code states and no test names. Four.
            `EarningsRuleFor`'s two fall-through guards. Removing either leaves the suite green,
            which is the path the handover said it had not tested, and both produce a number
            rather than an absence. Without the `before` guard a print older than the stored
            bars reports a one-day move equal to the price itself, since the missing prior bar
            reads as a close of zero. Without the `at` guard a print after the last stored bar
            reports a zero move dated 0001-01-01. Both would reach the earnings rule on the
            page as figures, and the rule they break is the one that says code owns every
            number (see: Code owns every number).
            The blended entry taken from the first tranche alone rather than the mean of the
            first two stays green, so the figure section 17 names as what the near-exit skip is
            measured from is unasserted.
            The invalidation falling back to a tranche's own low edge where no band sits beneath
            it stays green, because no tranche in this fixture has an absent stop: the three
            uptrend names take one from the trailing rule and the range name has a band beneath
            all three of its tranches. The ladder expectation says as much in its own
            invalidation note, so the fixture documents the gap it leaves.
Group C:    the shock condition, unasserted in its parts. Two.
            The shock multiple changed from 3 to 1 stays green, because the constructed case
            uses a move large enough for either figure and the negative case a move small
            enough for either. The lookback beside it does turn that same test red, so the pair
            is asymmetric: one figure is reached and the other is not, and the two are stated
            in one sentence in `DECISIONS.md` as proposals on the same terms.
            The shock's next-day hold requirement removed stays green, since the constructed
            window holds its low and no case is written where it does not.
Group D:    a boundary or a population the committed fixture cannot reach. Three, and this is
            the class phase 3's sign-off named as its group three rather than a defect of the
            first two kinds.
            Tranche eligibility admitting a band whose low edge equals the close exactly.
            The near-exit skip at exactly two typical days' moves.
            The window slice for a name with more than ten recent sessions, which no test
            enters at all: taking the first ten instead of the last ten stays green, so the
            branch that trims the window is never run.
Judged:     the three the handover put up for rejection, taken one at a time.
            The trailing stop taking the higher of the band beneath and the last swing low is
            the right rule and is recorded in the wrong place. It is right because a stop below
            the range floor is looser than the range rule gives and trails nothing, and the
            fixture holds the case that shows it: MSFT's third tranche has its last swing low a
            hundred points under the band, and the literal reading would put the stop there. It
            changes exactly one stop in this fixture, which the 4.5 entry states and the ladder
            expectation derives independently. What is wrong is that no spec says it. Figure
            10.1 still reads "the stop trails the last higher low" and the decision it rests on
            says the same (see: The stop rule depends on the trend state). The phrase "higher
            of" appears in no spec in this repository, only in the two records, a source
            comment and an expectation note. `CLAUDE.md` says a decision is changed only by
            another decision and that no progress entry or checkpoint note supersedes one, so
            the rule the code runs is a rule the corpus does not hold.
            The lookback of 10 and the shock multiple of 3, and every figure in the three event
            setups, are accepted as proposals. They are stated as proposals in both section 10
            and `DECISIONS.md`, they name the obligation that will calibrate them, and none is
            presented as measured. What is owed is only the asymmetry group C records.
            `ReachesTheZone` as the absence of a pattern is the right modelling and is named in
            no spec. A tranche whose band the price has not reached needs a label and the
            alternatives are worse. But figure 10.1 calls its list fixed and names four, the
            decision enumerates the same four, and the fifth reaches both the store and the
            page, where it reads "buy on the price reaching the zone". The string appears
            nowhere in the corpus. Beside it, `NameScreen`'s condition-to-words mapping ends in
            a catch-all arm, so a sixth condition, a typo or an unset value would render as
            that same sentence with nothing failing, and no test in the suite asserts any plan
            sentence at all.
Found:      two further things, neither of them code.
            Section 10 is not a claim source. The 203 claims come from sections 7, 14, 15, 16,
            17, 18 and 19.1 and the read and write matrix, so figure 10.1's rows are reached by
            nothing. That is the class that produced the trailing stop drift, and it is the
            more useful half of that finding: the ladder's rules can move away from the code
            with no instrument asking.
            `CHANGELOG.md`'s entry for phase 4 becoming ten checkpoints states that the
            obligations table held 28 rows and holds 43. It held 27 before that commit and 37
            after it, which the phase 3 sign-off and the 4.0 entry both record correctly by
            measurement. The 43 is 28 plus the fifteen obligations the entry names, and the
            fifteen became ten rows because phase 3's nine group-three cases were consolidated
            into three rows by component. All nine are covered and all six named obligations
            are present, so nothing was lost; the figure was predicted and never counted.
            Records are exempt from `stated-counts`, which is how it survived.
Not a reopen: under the stopping rules none of the above reopens phase 4. No check broke, both
            gates are green, and no done condition fails. In every case the shipped code is
            correct and what is absent is either an assertion or a sentence in a spec, which is
            the class phase 2 and phase 3 each carried out of their own sign-offs.
Carried:    eleven obligations created here, for 5.0 to enter in the table rather than by this
            entry, which edits no spec. Two are spec corrections, one is a placement question
            and eight are tests. The due points are proposals for 5.0 to place against the
            checkpoints it writes, since phase 5's detail is not this session's to settle.
            The trailing stop rule written as a decision that supersedes the one it changes,
            with figure 10.1 restated to match, due at 5.0, which is the pass that reads the
            corpus before phase 5 builds on it.
            `ReachesTheZone` named in section 10 and in the decision that enumerates the
            conditions, due at 5.0, for the same reason.
            Section 10's figure rows placed as claims, or the corpus stating why they are not,
            due at 5.0, since it is a placement question and not a test.
            The two earnings guards asserted over a print older than the stored bars and a
            print after the last one, due at 5.1.
            The blended entry asserted over a plan with two tranches, due at 5.1.
            The invalidation asserted where a tranche has no band beneath it, due at 5.1.
            The stop distinctness the relabel rests on asserted, due at 5.1.
            The shock multiple's value asserted, and the next-day hold asserted over a window
            that does not hold its low, due at 5.1.
            The condition window asserted for a name with more than ten recent sessions, due
            at 5.1.
            The tranche eligibility and near-exit boundaries asserted over constructed input,
            due at 5.1.
            The plan column's condition sentences asserted on the surface a person reads, with
            the catch-all arm made to fail rather than to render, due at 5.4, which is the
            checkpoint that builds the surface those sentences sit beside.
Noted:      the three worktrees the phase 3 sign-off recorded under the repository's local
            worktree directory are still registered and still at f48d9d6, untouched. They are
            recorded again rather than removed, for the reason that entry gave. The worktree
            this sweep used was created under the session scratchpad, outside the repository,
            and was removed; `git worktree list` holds the same four entries it held before.
            The five live calendar requests the 4.3 and 4.8 entries record are taken as
            recorded, being measurements against a key this session did not use.
Tests:      415, unchanged from 4.9. Windows for this run; the matrix carries macOS and the
            Linux case-sensitivity job, and all three were green on 7eee922, the head of the
            last phase 4 merge.
Signed:     phase 4 is signed off at 5d669d9. Phase 5's plan is not opened by this session.

### 5.0 planning - the addendum's three, four groups renamed, and the figures nothing read   2026-09-10
Not a checkpoint entry. It belongs to 5.0, which has not landed. This session has committed
            code and may not sign it off.
Built:      no component. Six commits, each carrying one kind of work so a reader can check one
            kind at a time: the verification rules and the absolute bound, the seven assertions
            with their mutations, the eleven obligations and two spec discharges, the figures,
            the holes and faults, and the prediction with the checkpoint text.
Addendum:   the operator's three, and two of the three figures it states do not reproduce.
            The survivors are eleven and the sign-off had already sorted them into four groups
            lettered A to D, not three. The three-group scheme is the phase 3 sign-off's, and
            phase 4's Group D back-references it by name. Re-sorted against phase 3's three,
            the survivors that fit none of them are Group A and there are two of them, not six.
            The class is real and the addendum's definition and the sign-off's are one fault
            seen from opposite sides: equivalent under an invariant the suite does not state,
            from the mutation's side, and asserting something the payload cannot produce, from
            the assertion's side. So CLAUDE.md names the class rather than a fourth beside a
            scheme that already had one, and this entry records two instances rather than six.
            The elapsed-time instances are two, not three, and both are the same test,
            ANightThatPassesItsDeadlineStopsAndSaysSo, at 250 milliseconds and then at three
            seconds. two-platform's other two catches are a shallow clone and a leftover
            artifact, neither of them a bound on time. The suite held one absolute bound and one
            self-calibrating one, so the self-calibrating form tied rather than outnumbered.
            The ConditionFor fork resolves to its first branch. 4.4 mutated the stop, not the
            condition, and its rule was stated first and named where a stop sits, so condition 9
            worked and the choice of assertion was the gap. Condition 9 is not amended; what
            CLAUDE.md gains is that a mutation's rule names the property it is trying to break
            rather than the line it edits, and that a checkpoint names the properties it added
            and did not mutate.
            The tautology the addendum points at is real and sits at the exactly-one assertion,
            whose two sides both come from production: five ConditionFor calls against the enum
            ConditionFor returns from. Swap two kinds and the set over those inputs is unchanged.
            It is redundant rather than load-bearing, because the five assertions above it
            compare against literals and each goes red, so it stays with that written on it.
            What it never asserted is the exclusivity it advertises, and one of the three overlap
            pairs had no case anywhere: a window carrying both a close below the band and a shock
            whose low held. Added in both directions. The pair the plan expected to be missing
            was already asserted at two multiples.
Decided:    three holes settled and one of them filed by this pass.
            **The base rate is over every name-night, and never over the listed ones**, with two
            sub-questions answered beside it rather than folded in: the setup horizon has no
            universe figure of the same kind and the column is null there, and the figure repeats
            down the table by design because the row is what the run page reads.
            **News is one dated query, paged to cover the day, and attributed to names locally**,
            superseding the decision whose name said one request. 2.5's measurement falsifies two
            documented limits and both are restated rather than satisfied. What saves the cost
            rule is the measurement: one request already attributed 3,232 distinct symbols
            against an index of 503, so the feed is market-wide and adding names adds no
            articles, which is what makes a paged query something other than a per-name call.
            **The trailing stop is the higher of the band beneath and the last swing low**,
            superseding **The stop rule depends on the trend state**, which the code has run
            since 4.5 against a corpus that said something else.
            The six reason thresholds are confirmed as proposals with nothing tuned in advance.
Found:      no figure in the document was read by anything, which is why figure 10.1's rows were
            reached by nothing. The table reader matches table elements, every figure is a div,
            and the placement check asserted that every table the reader returned was placed, so
            its completeness was defined by the thing it was checking. Four figures and 59 boxes
            were invisible and no check could say so. That is the shrinking-population defect in
            its third form, after a floor set to what a run produced and a reader whose
            population was its own output, so the new reader states its population as the
            document's own count of figure openings and refuses if the parse returns fewer.
            The first run of the system diagram's own placement found the diagram naming a store
            called "list reasons" where section 16 carries Listings.
            And the sign-off's description of one Group B survivor does not reproduce. It says
            that without the later-session guard a print after the last stored bar reports a zero
            move dated 0001-01-01. Removing that guard alone changes nothing, because the session
            it leaves at the default has no session before it and the earlier-session guard
            catches it. The guard is real and is not independently observable through the
            function's output, so the mutation that reaches it removes both.
Measured:   11 mutation runs over 10 distinct mutations, all in a worktree under the session
            scratchpad outside the repository, reverted, and the worktree removed. The worktree
            list holds the same four entries it held before, being this tree and the three the
            phase 3 sign-off recorded. The rule was stated before the sweep: each mutation names
            the property the assertion it targets was written to close, taken from the sign-off's
            own survivor list where one exists.
            Nine turned a test red, one was invalid and is described above, and one is the
            control. Red: the earlier-session guard, both earnings guards together, the blended
            entry taken from the first tranche alone, the invalidation without its fallback, the
            band beneath taken as the lowest rather than the nearest, the shock multiple at 1,
            the next-day hold removed, the condition window taking the first ten, and tranche
            eligibility admitting the close. Green: the relabel's first match replaced by its
            last, which is the Group A survivor and is expected to stay green, because stops are
            strictly decreasing and the data cannot produce two at the invalidation price. That
            is the control the property assertion was written for rather than against.
            Claims 234, from 203. PASS 107, from 92. Out of scope 127, unexamined 0, fail 0. The
            population is every claim the harness reads from the document, which is now every
            table, every figure and the nightly run's ordered list.
            The 31 new claims: 23 from the figures, being 6 for figure 9.1, 9 for figure 10.1 and
            8 for figure 12.1; 2 for the change detector's catalogue and matrix rows; 3 for the
            section 15 rows decomposed per surface; and 3 for the expectation files section 19.1
            did not name. 15 of the 31 pass now.
            Section 15 carries 37 rows and 43 claim subjects, from 40, because three rows were
            decomposed. The decomposed elements are 18 over 7 rows, from 12 over 4.
            The obligations table holds 49 rows, from 38, with 11 entered here.
Predicted:  234 claims and 162 PASS after phase 5, with 72 out of scope. The two lists and the
            arithmetic between them are in BUILD_PLAN.md at 5.0, and 5.7 checks the pair rather
            than the components.
Tests:      426, from 415.
Carried:    nothing new. Ten of the eleven obligations entered here are discharged here and the
            eleventh is due at 5.4, which its own text cites back.
Notes:      no-superseded-citation now reads the specs and the code and excludes the three
            records by name. Superseding the trailing stop rule turned it red on the phase 4
            sign-off's own citation of that rule, and PROGRESS.md is append only: a record is
            corrected by a new dated entry rather than by editing the old one, so a check that
            refused such a line would forbid the corpus from ever superseding a decision a record
            has cited. A second test asserts the exclusion removes at least one citation and that
            every one it removes sits in a record, because a filter that matches nothing reads as
            a rule and behaves as a comment.
            The sector was ruled here and is declared and migrated at 5.1. schema-columns refused
            the column when this pass declared it ahead of the migration, which is the same
            direction writer-ownership refused a deleter at 4.0.
            Two of this pass's own constructed windows were wrong on their first run, and both
            were the class the pass had just finished naming: a bar whose close sat outside its
            own range, and a window whose ordinary bars were themselves shocks. Repaired rather
            than worked around.

### 5.1 - the full universe and the universe screen                          2026-09-10
Built:      the sector on the membership row, migration 13, and the universe screen's three
            regions with the distance row mark beneath them. `UniverseScreen`, a projection
            beside `NameScreen` and in the seam that file names.
Decided:    nothing new. 5.0 ruled the sector's home and what a departed name carries, and this
            checkpoint declares the column and writes it, because a column declared before the
            migration creates it is a declaration with nothing behind it.
Measured:   the sector comes from the `Components` object of the constituents response the
            loader already fetches, at no extra request. 4 of the fixture's 6 constituents carry
            one, being the 4 current members, and the 2 departed carry none. The population is
            the membership rows in the committed capture, and the two counts are different
            populations: a constituent is a membership row and a current member is a row with no
            leave date.
            The universe answers with 4 rows against 6 constituents, which is the index rather
            than every name the store has ever held.
Found:      two holes in this checkpoint's own work, both by its own mutation sweep, and both
            are classes 5.0 had just finished naming.
            Sorting a name with no distance first left the suite green. The partition assertion
            had nothing on the other side of it: every name in the committed fixture has bands
            and bars, so there is no absent distance to sort and the comparison was vacuous.
            That is the unreachable boundary. The case is constructed now, and the constructed
            input also pins the name whose typical daily move is zero, which has no distance
            rather than an infinite one.
            Assigning the incoming sector rather than coalescing it left the suite green. The
            fixture's two departed names were never in the snapshot object, so they have no
            sector to lose and the two readings cannot be told apart by any payload this fixture
            holds. That is the unproducible shape, whose remedy is the invariant written down
            and asserted rather than a stronger assertion at the site. A name is now loaded
            while it is a member and again after it has left, and what it was last seen with
            survives.
            And two limits were owed at this checkpoint and neither could be produced here. The
            row coverage claim is about a listings row for every name every night and `listing`
            arrives at 5.4, which is the same fault as the `listings-coverage` roster row 5.0
            moved and which was missed beside it. The wall clock at index size needs a night that
            ran on a schedule; what this checkpoint produces is one night run by hand, which is
            an observation and not a bound. Both moved, to 5.4 and 5.7, and 5.7's done condition
            already produces the week of nights a distribution needs.
            Writing this entry before the run found two more of the same family, which is what
            done condition 8 is for. Recording 5.1 turned 15 tests red at once, all of them the
            reconciliation refusing a due point the record now shows as landed. Section 11's
            table placement was owed at 5.1 and its own reason names the fixture's listings
            expectations, which arrive at 5.4. And section 16's Listings store row derived to
            5.1 from the plan, because 5.1's text names the store in order to say it is absent
            there and the derivation reads the first checkpoint whose text carries the word.
            That is the prefix shape the verification rules name as having arrived four times,
            and it is declared as a late derivation rather than written into the map, which is
            the form that dictionary exists for. Four rows of one family, then: the roster row
            and the row coverage limit 5.0 moved, and these two.
Mutated:    four mutations, in a worktree under the session scratchpad outside the repository,
            reverted, and the worktree removed. The rule was stated before the sweep and follows
            5.0's own: each mutation names the property it is trying to break, not the line it
            edits, and the properties this checkpoint added are named below whether they were
            mutated or not.
            The properties added: the universe is every current index member and no other name;
            a name with no distance sorts last; the sector is read from the snapshot object of
            the payload the loader already fetches; and a name that leaves keeps the sector it
            was last seen with.
            All four were mutated and all four now turn a test red. Two were green on the first
            sweep and are the holes recorded above; the two tests that close them were written
            before the second sweep and the mutations were re-run against them.
            None of the four went unmutated. The mark's geometry was not mutated and is named
            here as not mutated: a scale drawn wrong is a picture that looks wrong, and the
            assertion over it reads the drawn attributes back rather than the shape.
Tests:      433, from 426. `tools/ci.sh` green end to end, all 6 steps, migrations 0 to 13, exit
            0. `tools/verify-phase` green at 234 claims, 112 PASS, 0 FAIL, 122 out of scope, 0
            unexamined, 119 placements and verdicts reconciled against a floor of 34. Windows on
            this machine; the matrix carries macOS and the Linux case-sensitivity job.
Carried:    nothing new.
Notes:      the read API still derives nothing, which `read-surface` asserts over the shipped
            source, so the distance the screen sorts on is worked out in the projection. That is
            the one place the universe screen differs from the name screen and the file says so:
            the name page draws stored columns, and a distance to a band edge in typical days is
            stored nowhere and changes with every session.
            The sector column is last in `SCHEMA.md`'s column list because `ALTER TABLE` appends
            and the file states the order the store has rather than the order that reads best.
            `schema-columns` asserts that order and refused the declaration when 5.0 wrote it
            ahead of the migration.
            The regions the listings store feeds are absent and say so rather than being drawn
            as zero: how many of a sector are on tonight's list, the evening a name was last on
            it, and the listing strip. A zero there would read as nothing having fired.

### 5.2 - the move annotator                                                 2026-09-10
Built:      migration 14 and the `move` table, `MoveSeries`, `MoveAnnotator`, section 14's step
            11, and the how-it-got-here table on the name page.
Decided:    the spans a move is measured over, being one session and one trading week. Two rather
            than a range, because a set of spans is a threshold and nothing has measured one: a
            third would be a figure nobody could later tell from a measured one, which is the
            rule the tranche lookback and the shock multiple are already stated under. It is in
            code rather than in a document, so it pins nothing and states nothing.
Measured:   32 move rows over 4 names, 8 per name, which is the cap `MoveSeries` states. The
            population is the names with a captured price series, not the index: a name that left
            keeps its stored history and a report opened on it reads what it read last night.
            The expectation was computed outside this repository from the captured payloads,
            applying the window the backfill asks for, so a component that stored the wrong set
            does not agree with it. AAPL's largest is 12.17 per cent over five sessions ending
            2026-07-02; KEYS 28.62 over five ending 2026-02-24; MSFT 25.33 over five ending
            2026-08-03; NFLX 27.72 over five ending 2026-03-02.
            235 claims from 234, 118 PASS from 112, 117 out of scope, 0 unexamined. The extra
            claim is unpredicted and is named here so 5.7 can place it: section 15.9's
            how-it-got-here row decomposed into two, its table of moves at 5.2 and its cause
            column at 6.5.
Found:      two holes in this checkpoint's own work, both by its own mutation sweep.
            The retention assertion was vacuous. Every move the committed bars produce is already
            inside the window, so a drop that did nothing satisfied both halves of it: no row
            below the boundary, and rows above it untouched. That is the unreachable boundary,
            and the row is constructed now.
            The constructed row was then removed by the wrong statement. Given a rank past the
            kept count it was taken by the fallen-out drop rather than by the retention drop, so
            the assertion still passed with the retention statement disabled. Given rank 1 the
            only statement that can reach it is the one under test. Two greens for one property,
            and the second was the sharper of them: the first was a case the fixture could not
            reach and the second was an assertion reaching the wrong code.
Mutated:    four mutations, in a worktree under the session scratchpad outside the repository,
            reverted, and the worktree removed. The rule was stated before the sweep: each names
            the property it is trying to break rather than the line it edits.
            The properties added: a move is ranked by absolute size so a fall counts as much as a
            rise; where two spans end on one session the longer wins; a move does not outlive the
            bar it sits on; and the cause column is stated as absent rather than drawn empty.
            All four were mutated and all four turn a test red. Two were green on the first sweep
            and are the holes above. None of the four went unmutated. The span set itself was not
            mutated and is named here as not mutated: it is a proposal rather than a property, and
            a different set is a different reading of the same rule rather than a broken one.
Tests:      437, from 433. `tools/ci.sh` green end to end, all 6 steps, migrations 0 to 14, exit
            0. `tools/verify-phase` green at 235 claims, 118 PASS, 0 FAIL, 117 out of scope, 0
            unexamined, 125 placements and verdicts reconciled against a floor of 34. Windows on
            this machine; the matrix carries macOS and the Linux case-sensitivity job.
Carried:    nothing new.
Notes:      `move` gains its deleter here, which is the last of the six computed tables and
            completes 4.0's ruling that every computed table's writer is its own deleter. The
            store row covering all six is owed at this checkpoint for that reason rather than
            where the first of them arrived.
            The annotator carries two delete statements and they are different things. One drops
            everything below the retention boundary for every name at once, which is the shape
            the other five computed tables have. The other drops the rows a name held above
            tonight's rank count, which is a session that was a biggest move last night and is
            not one now. The second is what makes the table a set rather than a union of every
            night's sets, and it is why the first needed isolating before it could be asserted.
            Section 15.9's how-it-got-here row is decomposed per surface, its table at 5.2 and
            its cause at 6.5, for the reason 5.0 decomposed the universe screen's two rows and
            the run page's one.

### 5.3 - the facts assembler and the change detector                        2026-09-10
Built:      migration 15 and the `facts` table, `FactsFile`, `FactsAssembler`, `ChangeDetector`,
            and section 14's step 13. The file and the change list are one nightly stage rather
            than two, because the detector compares tonight's payload against the last stored one
            and there is nothing for it to read until the assembler has written tonight's.
Measured:   26 facts per name over 4 names. The population is the names with a captured price
            series, not the index: the assembler reads the last stored session and a name with no
            bars gets no row, which is an absence rather than a file of nulls.
            The set is 4 from the bar, 10 from the indicators, 6 from the two immediate bands, 1
            from the ladder, 3 from the largest move, 1 from the swings and 1 from the calendar.
            It is written from the rules rather than frozen from a run, and every value is
            asserted against the store it was read from rather than against a copy of the
            payload.
            235 claims, 125 PASS from 118, 110 out of scope, 0 unexamined.
Found:      four cells filled ahead of the code they claim, and one statement that was an update
            wearing an insert's name.
            `writer-ownership` reads the statement rather than the declaration, and the
            assembler's first upsert was an update on a table whose Update belongs to the change
            detector. A table may never have two owners for one operation, so the assembler
            inserts and ignores a conflict. A night run twice writes the same file and leaves the
            change list where it is, which is the same shape `news_pulse` takes.
            The facts assembler's matrix row read Listings and Fundamentals and the change
            detector's read Listings, while `listing` is created at 5.4 and `fundamentals` at
            6.1. `component-access` refused all three the moment the components landed. Each cell
            now sits at the checkpoint that creates the store it names, and the payload retention
            moves to 5.4 with the read it needs. That is the same direction `writer-ownership`
            refused a deleter at 4.0 and `schema-columns` refused a column at 5.0, which makes
            three instances of one rule: a declaration before the thing exists is refused, and
            the cell belongs where the code is.
Mutated:    six mutations, in a worktree under the session scratchpad outside the repository,
            reverted, and the worktree removed. The rule was stated before the sweep: each names
            the property it is trying to break rather than the line it edits.
            The properties added: a facts re-run does not blank the change list; the payload is
            written in name order so two runs over one store produce the same bytes; a material
            change is a fact that appeared, went, or took a different value; a first night has no
            changes rather than an unknown number of them; every fact names the stage that
            computed it; and the detector touches its own column and neither of the assembler's.
            All six were mutated and all six turn a test red on the first sweep, which is the
            first checkpoint of this phase where that happened. None went unmutated. The hash
            itself was not mutated and is named here as not mutated: it is a function of the
            payload beside it, so a mutation of one is a mutation of the other and the assertion
            over the payload already carries it.
Tests:      441, from 437. `tools/ci.sh` green end to end, all 6 steps, migrations 0 to 15, exit
            0. `tools/verify-phase` green at 235 claims, 125 PASS, 0 FAIL, 110 out of scope, 0
            unexamined. Windows on this machine; the matrix carries macOS and the Linux
            case-sensitivity job.
Carried:    nothing new.
Notes:      the change list is an empty list rather than a null where there is nothing to compare
            against, because a name whose first night this is has no changes rather than an
            unknown number of them, and the two read differently on a page.
            The comparison is on the fact's name rather than on its position, so a fact added
            between two others is one change rather than every fact after it changing. The
            constructed second night is what asserts it: the committed fixture holds one night
            per name, so a comparison against a previous night is unreachable from it.

### 5.4 - the shortlist builder and tonight's list                           2026-09-10
Built:      migration 16 and the `listing` table, `ShortlistSeries`, `ShortlistBuilder`, section
            14's step 12, and the `listings-coverage` check promoted to every CI run. Tonight's
            list with its night header, watch list, list and selected name; the universe screen's
            two listing halves, deferred from 5.1; the name page's why-it-is-here and walk; and
            section 18's banner. The change detector's payload retention, with the listings read
            5.3 could not declare.
Decided:    nothing new. The six reasons are section 11's and their thresholds are the proposals
            5.0 confirmed, with nothing tuned in advance.
Measured:   4 listing rows over 4 current members, one per member per night, and the fired and
            quiet rows partition the whole. The population is the index rather than the names with
            bars, which is the every-name grain the shadow column rests on.
            235 claims, 143 PASS from 126, 92 out of scope, 0 unexamined. Seventeen claims became
            PASS at once, which is the largest single move of the phase, and the out-of-scope
            floor in `architecture-conformance` fell from 100 to 80 with the reason written beside
            it: that number is a fact about how far the build has got and not about the property.
Discharged: the strength score obligation. The list orders on how many reasons fired and then on
            band strength, and the phase 3 sign-off's measurement is what settles what that score
            is dominated by: touches are 72 to 79 per cent of the members of each name's strongest
            band, anchor counts are bunched at 4 to 15 while touch counts run 0 to 41, and MSFT's
            top two bands carry the same 4 anchors with the ranking decided by 15 touches against
            9. A tiebreaker dominated by touches is one about how often a price has come back to a
            level, which is what the ordering is for. The tiebreaker is asserted over constructed
            rows whose fired counts are equal, because four names of real bars are not guaranteed
            to tie.
            And the eleventh obligation the phase 4 sign-off created. `NameScreen`'s
            condition-to-words mapping ended in a catch-all arm, so a sixth condition, a typo or
            an unset value rendered as the sentence for reaching the zone with nothing failing,
            and no test in the suite asserted any plan sentence at all. Every condition has its
            own arm now and anything else throws. Every sentence is asserted one at a time rather
            than as a set, because a mapping with two arms swapped produces the same set of
            sentences over the same plans, which is the tautology 5.0 recorded in a different
            place.
Found:      contradiction L, refused by `component-access` the moment the component landed. The
            matrix gave the builder reads of Fundamentals and News pulse, which the catalogue
            names neither of, and blanked Membership, Bars and Facts, which four of the six
            reasons need. A blank cell is a claim as much as a filled one, so the row made five
            wrong statements and none of them was reachable until the class existed.
            The facts read is there for a reason rather than for a value, and it is worth stating
            because a reader will look for the value: the builder compares the session the facts
            file is dated with the session its bars end on and stops the night if they differ. A
            listing written against a different session from the facts beside it is a row two
            later readers disagree over.
            Writing this entry before the run found three more things, which is what done condition
            8 is for. Section 11's table placement was owed at 5.4 and its own reason names the
            fixture's listings expectations, which now exist, so it is converted from a due point
            to a check naming the instrument that reaches it, which is what 1.5 did with the gap
            row. Covering a table means covering rows nobody enumerated, so that check now opens
            the document and reads the six reason names and the two figures the cells state,
            rather than restating them beside it. And section 16's Fundamentals store row derived
            to 5.4 from the plan, because 5.4's text names the fundamentals in the sentence saying
            the shortlist reads none of them. That is the prefix shape again and is declared as a
            late derivation, which makes three of those across the phase.
Mutated:    nine mutation runs over eight distinct mutations, in a worktree under the session
            scratchpad outside the repository, reverted, and the worktree removed. The rule was
            stated before the sweep: each names the property it is trying to break rather than the
            line it edits. One was invalid, being a pattern that matched nothing, and was
            corrected and re-run.
            The properties added: unusual volume is above twice the fifty-day average and not
            merely above it; earnings soon fires inside the horizon and not outside it; a name
            with no ladder row last night has not changed; a row is written for every index member
            and not only the ones with bars; band strength is the tiebreaker where fired counts
            are equal; an unknown condition fails rather than rendering a default; the plan at
            listing carries the entry, the stop and the first traded target; and at most twenty
            rows are drawn with the true count stated.
            All eight turn a test red. None went unmutated. The six reasons' own thresholds were
            mutated in two of the eight and the other four reasons were not, and they are named
            here as not mutated: each is recomputed in the suite from the tables it reads, so a
            rule changed in the builder disagrees with the recomputation rather than with a
            frozen set, and that is the assertion carrying them.
Tests:      456, from 444. `tools/ci.sh` green end to end, all 6 steps, migrations 0 to 16, exit
            0. `tools/verify-phase` green at 235 claims, 143 PASS, 0 FAIL, 92 out of scope, 0
            unexamined, 150 placements and verdicts reconciled against a floor of 34. Windows on
            this machine; the matrix carries macOS and the Linux case-sensitivity job.
Carried:    nothing new.
Notes:      the trend-changed reason cannot fire over the committed fixture, because it holds one
            ladder row per name and the reason needs a yesterday. Read from the fixture alone the
            rule is indistinguishable from a reason that never fires, so it is asserted over
            constructed inputs at all four cases: a label that changed, one that did not, no row
            last night, and no row tonight.
            The watch list has no store anywhere in the corpus and none is invented. The region
            states that rather than being absent, because a region a reader cannot find is
            indistinguishable from one that is empty.
            `listings-coverage` reads the same replay the fixture expectations read rather than a
            second one. Two replays of one pipeline disagree eventually, and the two checks would
            be the pair.

### 5.5 - the forward return filler, the news pulse counter and the night's close   2026-09-10
Built:      migrations 17 and 18 with the `forward_return` and `news_pulse` tables,
            `ForwardReturnSeries`, `ForwardReturnFiller`, `NewsPulseCounter`, `NightClose`, the
            live news feed's paging, and section 14's steps 14, 15 and 16.
Decided:    nothing new. The paging is 5.0's ruling implemented, and the base rate's population is
            5.0's ruling computed.
Measured:   12 forward return rows over 4 listings, three horizons each, and every one of them not
            yet matured. That is the state the committed fixture is in rather than a defect: its
            listings sit on the last stored session, so nothing after them exists. 4 news pulse
            rows over 4 current members, and the capture attributes more symbols than the index
            holds, which is what makes the filter observable.
            237 claims, 156 PASS from 143, 81 out of scope, 0 unexamined.
            Two floors in `architecture-conformance` fell with the phase, each with the reason
            written beside it: the out-of-scope count from 100 to 80 at 5.4, and the plan-derived
            share from 20 to 12 here. Both are facts about how far the build has got rather than
            about the property, and the property in each case is the split the assertion makes.
Found:      three things, two of them by this checkpoint's own mutations.
            The setup's branch order cannot be observed at all. The test is on the close, and one
            close cannot be both below the stop and at or above the target while the stop is
            beneath the target, so swapping the two branches changes nothing any series can show.
            That is the unproducible shape, and the remedy for that class is the invariant written
            down and asserted rather than a stronger assertion at the site. A plan whose stop sits
            at or above its target now refuses rather than being scored by whichever branch ran
            first, and the invariant is asserted over every plan the store carries as well as over
            constructed input.
            Nothing exercised the paging path, which is what 5.0's whole news ruling rests on. The
            captured payload is one page under the cap, so a feed that stopped after one page
            passed the suite. Three pages are constructed, two at the cap and one short of it, and
            a day that reaches the page cap is asserted to refuse rather than to store a truncated
            count.
            And the closing row's counts were unasserted: it could report zero names on the list
            and the suite stayed green. Every figure is now recomputed from the tables the stage
            counts over.
Mutated:    thirteen mutation runs over nine distinct mutations, in a worktree under the session
            scratchpad outside the repository, reverted, and the worktree removed. Two were
            invalid, being a form that did not compile, and both were corrected and re-run. The
            rule was stated before the sweep: each names the property it is trying to break rather
            than the line it edits.
            The properties added: an unresolved setup is a value of its own and never a win or a
            loss; an immature horizon reads as not yet matured rather than as a blank or a zero;
            an unresolved outcome is in neither half of the base rate; a window with nothing
            matured has no rate rather than a rate of zero; a member the day wrote nothing about
            carries a row with zero; the count is kept over the index rather than over every
            symbol the market wrote about; the query is paged until the day is covered; every
            count on the closing row is taken off the store; and a plan whose stop sits at or
            above its target refuses.
            Eight turn a test red. One stays green and is the control rather than a hole: the
            setup's branch order, which no series can distinguish, and whose invariant is what is
            asserted in its place. That is the second time this phase has answered a green
            mutation with an invariant rather than with an assertion at the site.
            None of the properties added went unmutated.
Tests:      463, from 456. `tools/ci.sh` green end to end, all 6 steps, migrations 0 to 18, exit
            0. `tools/verify-phase` green at 237 claims, 156 PASS, 0 FAIL, 81 out of scope, 0
            unexamined, 163 placements and verdicts reconciled against a floor of 34. Windows on
            this machine; the matrix carries macOS and the Linux case-sensitivity job.
Carried:    nothing new. (owes: One day of news exceeds one request at the provider's limit) is
            discharged: the counter carries a page rather than a window, the page count follows
            the day's news volume rather than the size of the universe, and a day past the cap
            refuses.
Notes:      the base rate limit moved from 5.5 to 5.6, whose own Asserted by cell names a run page
            test. 5.5 computes the figure and stores it beside every return it is shown against;
            the claim is that no forward-return figure is shown without it, which is a claim about
            a surface. That is the eighth due point of this family the phase has corrected, after
            the roster row and the row coverage limit at 5.0, section 11's placement and the
            Listings store row at 5.1, the Fundamentals store row and section 11's placement again
            at 5.4, and this one.
            The night close is a component and gains its catalogue and matrix rows here, because a
            component is named in the catalogue in the same commit that introduces it and
            `component-access` refused the class until it was.
### 5.6 - the run page   2026-09-10
Built:      section 15.10's five regions, section 15.5's reason track mark, and section 15.7's two
            halves that needed a reason to have a history. `RunScreen`, the run route at
            `#/run/<date>`, `RunRegion`, and five marks: the operational header, the reason records,
            the reason track, stale and failed, and the harness verdicts. The read surface gained
            the night's run log, every listing, the forward returns and the stale names.
Decided:    two things, both about where a gate is applied rather than about a new rule.
            The win and loss split is gated on the same minimum in the picture as in the column.
            15.11 gates the record column at 250 resolved setups, and a split drawn beside a reason
            with eleven of them is the same figure through a second channel, since a reader takes
            the ratio off the bar. The mark keeps its three states as 15.5 defines them and draws
            what it is given; below the minimum the page hands it the resolved setups as one
            segment. So no reason below the minimum shows a rate anywhere on the page, through
            either channel.
            And the base rate is pinned per window rather than as one figure. Section 17's row says
            every forward-return figure is shown against the base rate for the same window, the
            horizons are three, and the setup horizon has none by rule. Two pinned lines and a
            third line stating the setup horizon's absence with its reason, rather than a window
            with no line at all, which a reader cannot tell from an oversight.
Measured:   237 claims, 164 PASS from 156, 73 out of scope, 0 unexamined. The eight are the run
            page's: the reason track, tonight's reasons-per-row and reason totals, 15.10's
            operational header, resolved count, stale and failed and harness, and section 17's base
            rate limit.
            Two floors in `architecture-conformance` fell with the phase, both the out-of-scope
            floor and for the reason written beside it: from 80 to 70, because eight claims became
            PASS at once. It is a fact about how far the build has got and the claims are what
            carries the property.
            Over the committed fixture: 8 reason firings across 4 listings, 0 resolved setups, and
            both base rate windows not yet measured. That is the fixture's state rather than a
            defect, and it is the state the page is designed for: its listings sit on the last
            stored session, so nothing after them has matured.
Found:      three things, two of them by this checkpoint's own mutations.
            `NightDurationAsync` ignored the night it was given. The query took every run carrying
            a listings stage and spanned the lot, so the date in the parameter changed nothing and
            two stored nights reported one duration covering both. It has been wrong since 5.4 and
            the header has been drawing it since. The run log has no session column and cannot have
            one it would agree with, because a run that starts after the close in New York carries
            tomorrow's UTC date, so the night is decided by the clock over each row's own start.
            That is asserted with the pair of rows that makes it visible: one written at half past
            one in the morning UTC that belongs to this night, and one written the evening before
            that does not.
            The reason totals region could lose its mark and stay green. The table of counts beside
            it carried every assertion, so the picture, which is the row's whole claim, was drawn
            and read by nothing.
            And the operational header's model calls and network requests could be drawn as
            constants. The assertion was on the data attributes, which the mutation left alone: the
            counts were right in the markup a machine reads and zero in the cells a person reads.
            That is CLAUDE.md's own rule about a claim that something is shown being a claim about
            the surface, missed while writing the assertion for it. The cells are asserted now, on
            a night where one stage made a request and the rest made none, so a header printing a
            constant disagrees on the row that matters.
Mutated:    seventeen distinct mutations over thirty-nine runs, in a worktree under the session
            scratchpad outside the repository, reverted, and the worktree removed. One was invalid,
            being a form that did not compile, and it was corrected and re-run. The rule was stated
            before the sweep: each names the property it is trying to break rather than the line it
            edits.
            The properties: a setup belongs to every reason that fired on the night it was listed;
            a resolved setup for a name nobody listed belongs to no reason; a stored reason section
            11's list does not hold refuses; the win and loss split is gated on the minimum in the
            picture as well as in the column; a record below the minimum draws its count against
            the minimum and no verdict; the reason track scales on one denominator across the whole
            mark; the unresolved segment is an outline and never a third colour; no forward-return
            figure is drawn with no base rate pinned above it; the night a run log row belongs to
            is decided by the clock and not by the date the log stores; a night's duration is over
            the run that wrote that night's list; a member with no stored series at all is stale;
            out of scope is counted apart from unexamined and never in its place; a reason that
            fired on nothing draws its rule rather than nothing; the reason totals region draws the
            track and not only the table; the operational header carries each stage's model calls
            and network requests; the stages are drawn in the order they ran; and each reason on a
            row carries the values that made it true.
            Fifteen turned a test red on the first run. Two stayed green and both were holes rather
            than controls: the totals region's mark and the header's two cells, both closed above
            and both red on the re-run. This is the first sweep in the phase with no green mutation
            left standing as a control.
            None of the properties added went unmutated.
Tests:      474, from 463. `tools/ci.sh` green end to end, all 6 steps, migrations 0 to 18, exit 0.
            `tools/verify-phase` green at 237 claims, 164 PASS, 0 FAIL, 73 out of scope, 0
            unexamined, 171 placements and verdicts reconciled against a floor of 34. Windows on
            this machine; the matrix carries macOS and the Linux case-sensitivity job.
Carried:    nothing new, and nothing discharged. The three operating obligations this page is the
            surface for stay open, which is what they are: (owes: The six reason thresholds
            calibrated from the nights they fired on) needs 60 nights, (owes: The three reason
            records that need resolved setups) and (owes: The event setups' triggers calibrated
            from resolved setups) need 250 resolved setups. What this checkpoint owed them is that
            each trigger is legible when it fires, and each is now a count on the page: the nights
            the record stands on, the resolved count against its minimum on every reason row, and
            the same count beside each reason on tonight's list.
Notes:      no store, no migration and no spec edit. The page reads what phase 5 has already
            written, which is why it is the last checkpoint before the report.
            The reason record is over every night the store holds rather than over the night being
            read. A record is a property of the reason across every name it ever fired for, and a
            record over one evening would be a statement about that evening wearing the clothes of
            a verdict. That is a scan of the listings table once an evening, and it is the reason
            the read surface gained a listing query with no filter on it.
### 5.7 - the phase 5 report, and the done condition that required calendar time   2026-09-10
Built:      nothing that runs. This checkpoint reports, converts two obligations, and repairs its
            own done condition. **This checkpoint amends its own done condition**, and the
            amendment is the substance of the entry rather than an aside in it.
Claims:     237, 165 PASS, 72 out of scope, 0 FAIL, 0 unexamined, from 226 and 107 at the phase 4
            sign-off pass. 172 placements and verdicts reconciled against a floor of 34.
Predicted:  234 claims and 162 PASS, with 72 out of scope. The actual is 237, 165 and 72.
            Three claims arrived unpredicted and each is named and placed. Two are the night
            close's catalogue row and its matrix row, which arrived at 5.5 because a component is
            named in the catalogue in the same commit that introduces it, and 5.0 predicted the
            stage without predicting that the stage was a component; both are PASS and belong in
            list B. One is section 15.9's how-it-got-here row decomposed into its table of moves at
            5.2 and its cause column at 6.5, named in 5.2's own entry so this pass could place it;
            it is out of scope at 6.5 and belongs in list B.
            One claim predicted to stay out of scope is PASS instead: section 17's wall clock row,
            below. So out of scope lands on the predicted 72 by two movements that cancel rather
            than by nothing having moved, which is worth saying, because a total that matches for
            two reasons is not a total that matched.
            The arithmetic: 234 plus 3 is 237; 162 plus the two night close rows plus the wall
            clock is 165; 72 plus the cause column minus the wall clock is 72.
            Every claim list A named arrived. No predicted claim failed to arrive.
Amended:    the done condition read that a week of unattended nights has run with the list current
            each morning, that the posting hour is recorded from those nights, and that the wall
            clock limit is set from them. It now requires the pair checked, the two figures carried
            as operating rows, and the registration written as a command.
            The reason is that neither figure is produced by a checkpoint. Both are produced by the
            system running on a schedule, which is the class this plan already carries as operating
            for the reason thresholds and the reason records. `CLAUDE.md`'s deferral convention had
            already named the failure in the words "never, where the named point is a report or a
            page rather than the thing that measures", and 5.7 is the phase 5 report. The posting
            hour stood at 3.7 before it stood here, which is the phase 3 report, so it had been
            moved from one report to another and neither produces an evening.
            The second half is worse than the first. Nothing had been registered with any
            scheduler. `RUNBOOK.md` step 8 read "Register the schedule with the platform's
            scheduler, in UTC", which is an instruction and not a command, so the week the done
            condition was waiting for was not going to arrive however long the build waited: no
            night was scheduled to run. A done condition that waits on the operator having got
            around to something is a done condition with no mechanism behind it.
Found:      three things beyond the done condition itself.
            The wall clock and the deadline were two numbers held in the relationship the row
            claims by a comment saying so. `RetryPolicy` stated fifteen minutes and a comment said
            it was three times the wall clock section 17 states, and section 17 stated no figure at
            all. The deadline is now derived from the wall clock at three times it, so the limit
            cannot move in one place alone, and the relationship is asserted in the document and in
            the policy together.
            Section 17 named the wrong checkpoint in two rows. Both said the wall clock is proposed
            until 5.1 measures it, and 5.0 had moved it to 5.7, so the document named a checkpoint
            the plan no longer did. Neither produced it.
            And one assertion this checkpoint wrote was a tautology, caught while writing it rather
            than by the sweep. `Deadline` is defined as `WallClock * DeadlineMultiple`, so asserting
            the one against the other cannot fail: changing the multiple moves both sides together.
            The multiple is now the number the row's own prose states, and the document is what
            notices.
Measured:   the wall clock claim moves from out of scope to PASS, which is 5.7's only claim
            movement. What is assertable is the relationship between the two rows and the
            derivation in the policy; what the figure should be is a property of the running system
            and is carried as an operating row, read on the operational header. Five minutes stands
            as proposed with what settles it named beside it.
Mutated:    six mutations, six runs, in a worktree under the session scratchpad outside the
            repository, reverted, and the worktree removed. None was invalid. The rule was stated
            before the sweep: each names the property it is trying to break rather than the line it
            edits.
            The properties: the night's deadline is three times the wall clock the document states;
            the wall clock the code holds is the figure section 17 states; the wall clock row says
            its figure is proposed and names what settles it; an operating row states the surface
            its trigger is read on; an operating row carries no checkpoint due point beside its
            trigger; and the checkpoint that builds the surface cites the obligation back.
            All six turned a test red. The last three are `obligation-reconciles` reading the two
            converted rows, which is the check that has to hold for the conversion to be worth
            anything: a row that reads as tracked from either end is chased from neither.
            None of the properties added went unmutated.
Tests:      474, unchanged. This checkpoint added assertions to a test that existed rather than
            tests of its own, which is where the wall clock row's claim belonged: `nightly-run`
            already read the deadline row against the policy. `tools/ci.sh` green end to end, all 6
            steps, migrations 0 to 18, exit 0. `tools/verify-phase` green at 237 claims, 165 PASS,
            0 FAIL, 72 out of scope, 0 unexamined, 172 placements and verdicts reconciled against a
            floor of 34. Windows on this machine; the matrix carries macOS and the Linux
            case-sensitivity job.
Carried:    two rows converted rather than discharged, and neither is closed by this checkpoint.
            (owes: The provider's posting hour for the day's bulk file, measured from live fetches)
            is 5 nights fetched under the schedule, and what those establish is a bound on the hour
            rather than the hour: a night that found the file already posted says the file was
            posted by then and says nothing about when. Tightening it needs a probe earlier than
            the schedule rather than more nights of it, and that is written into the row so a later
            session does not read five nights as five measurements.
            (owes: The nightly wall clock at index size, measured from nights that ran on the
            schedule) is 5 scheduled nights over the whole index. It is created here and open here,
            which is the first row in this table created by the checkpoint that also converts it.
Notes:      the registration is the deliverable that matters most and it is the smallest. Two
            schedulers, both triggering in local time, each needing a different thing done about
            it: Windows takes a start boundary carrying a zone, and `launchd` has no zone field at
            all, so the macOS job runs hourly and the script decides. A schedule written in local
            time walks an hour relative to the provider when daylight saving changes, and it walks
            into the wrong side of the close twice a year.
            The source needs no setting: it defaults to live, and what a live night needs is the
            key, which refuses by name when it is absent rather than reaching the provider
            anonymously. The runbook said both machines need the source set, which was wrong when
            written and would have sent a reader looking for a setting that is not there; it now
            says to read it only because a machine that has been replaying a capture carries
            `fixture` and would go on replaying it every evening.
            The instant is 23:30 UTC and it is provisional, which the runbook says in the same
            breath as stating it. A night that runs before the file is posted refuses rather than
            storing the wrong session, so an instant set too early is visible the next morning and
            is the measurement the posting hour obligation reads.
            What this checkpoint does not establish: that a scheduled night works. Nothing here
            registered anything on the operator's machine, and the first scheduled night is the
            operator's to start. What changed is that starting it is one command rather than a
            piece of research, and that no checkpoint is waiting on it.
### 5.7 - correction: the scheduled task's logon type, and the first live night at index size   2026-09-10
Corrects:   the 5.7 entry above records that the registration is written as a command an operator
            runs. The command was run on this machine and it produces a task that runs only while
            the account is logged on, which the entry did not say because the pass that wrote it
            did not run it. `RUNBOOK.md` now states the logon type, the elevated principal that
            fixes it, and the line that reads back which of the two is registered.
Measured:   the first live night at index size since 2.6, run by hand at 12:57 UTC on the operator's
            machine against a store `tools/ci.sh` had just dropped, so it was a first-run backfill
            rather than an evening.
            822 membership rows over 1 request. 125,736 bars over 503 names and 503 requests,
            covering 2025-09-10 to 2026-09-09. The backfill is the one place a per-name request is
            allowed and it is once per name ever, which is what those 503 are.
            The night then refused, correctly, and this is the part worth keeping: the fetch step
            stopped on the bulk file for 2026-09-10 carrying nothing for any of the 503 current
            members, naming the first five. It was 12:57 UTC and the session opens at 13:30, so
            there was no file for today to carry anything. A payload holding none of the index is
            refused before anything is stored rather than read as a night that ran, which is the
            behaviour section 18 states and the first time it has been seen against a live payload
            rather than a constructed one.
            The night exits 1 on that failure through both entry points, `tools/nightly` and
            `tools/nightly.ps1`, checked separately because a scheduler records the exit code and a
            failed night that reports success is the failure mode the whole run page exists to
            prevent.
            The second run of the same night, made after the store had been dropped by the fault
            below, is timed: membership 0.7 seconds over 1 request, and the backfill 79.2 seconds
            over 503 requests for 125,736 rows. So the heaviest night this system can have, being a
            first run over the whole index, is 80 seconds against a deadline of fifteen minutes and
            a proposed wall clock of five. That is a bound on the part that fetches and stores and
            is not a measurement of a night: the computed chain over 503 names has still never run,
            which is what 5.1's text named as the risk phase 5 actually carries, and it is what the
            first scheduled night gives.
Found:      three things, all from running the thing rather than from reading it.
            The logon type above. `Register-ScheduledTask` with no principal stores
            `InteractiveToken`. A locked screen is still logged on, so it works until a sign-out or
            an update reboot nobody signs back into, and then the nights stop with the run page
            showing the night before, which reads as a night not yet run rather than as a schedule
            that is gone. The durable form is an S4U principal and it needs an elevated shell,
            because logging a task on without a session is a batch logon. This session is not
            elevated: the interactive task is what is registered, and switching it is one elevated
            command.
            Task Scheduler rewrites a `Z` boundary into the machine's own offset. The task was
            handed 2026-09-10T23:30:00Z and stores 2026-09-10T19:30:00-04:00, which is the same
            instant in the pinned form. The runbook said the `Z` is what pins it and now says what
            is stored, because an operator checking the work and finding something else has to
            decide which of the two is wrong.
            `tools/ci.*` dropped the operator's store, and this session did it to itself. The
            live night above backfilled 125,736 bars, `tools/ci.sh` was then run to verify the next
            commit, and the backfill was gone: both the verification scripts and the nightly job
            resolved to `data`. Every session that has ever verified a checkpoint has been
            resetting the store the schedule is meant to accumulate into, and the next night would
            have run a first-run backfill of the whole index with nothing anywhere saying why. It
            has been latent since 0.4 and could not fire until a schedule existed, which is today.
            Both scripts now export a data root of their own and drop `/data-ci`, so the path to
            the operator's store is not something they know rather than something they are trusted
            not to use. The corpus already held that nothing in the harness reaches `data/`, and
            that was true of the suite and false of the two scripts that run it.
            And the estimate given to the operator before the run was wrong in the direction that
            matters. It was described as one evening's provider quota, and the store had just been
            dropped by `tools/ci.sh`, so it was a first-run backfill of 503 per-name requests. The
            spend is the same one the schedule would have made unattended at 23:30 tonight, which
            is why it was run rather than stopped, but it was not the spend that was described.
            The store was backfilled twice as a result, which is 1,006 per-name requests where
            503 were needed, and the second was this session's own doing rather than the schedule's.
Notes:      the schedule is registered on this machine for 23:30 UTC daily, which is 19:30 local in
            the Eastern zone today, with wake-to-run set and a two hour execution limit. Tonight's
            night is an evening rather than a backfill: the store now holds a year for every
            current member through 2026-09-09.
            What is not established: that a scheduled night runs. Nothing has yet fired on the
            trigger. That is read on the run page tomorrow morning, and it is the first of the five
            nights both operating rows are counting (owes: The provider's posting hour for the
            day's bulk file, measured from live fetches) (owes: The nightly wall clock at index
            size, measured from nights that ran on the schedule).
### 5.7 - the night measured at index size, and the three defects that measuring it found   2026-09-10
Measured:   a whole night over 503 names, run by hand for session 2026-09-09 against the live
            provider, three times: once as it stood, once with room to finish, and once with the
            repairs below. Warm store, home network, provider answering normally, on the operator's
            Windows machine. The first run is what forced the rest.
            Run 1 was stopped by its own deadline on the `facts` step at fifteen minutes, having
            reached twelve of seventeen stages. It behaved exactly as section 18 says: it named the
            step, kept last night's bars and marked every name stale rather than half-writing a
            night. That is the first time the deadline has fired against a real run rather than a
            constructed one, and it means the schedule registered this morning would have failed
            at 23:30 tonight.
            Run 3, after the repairs, is 44.6 seconds for the same session over the same store,
            from 574.7. Per stage, and the row counts are identical either side so the comparison
            is like for like:
              changes            106.0s -> 0.0s over 503 rows
              forward-returns    334.0s -> 0.0s over 1,509 rows
              indicators           9.0s -> 18.0s over 1,257,450 rows
              whole night        574.7s -> 44.6s
            The cost rule holds at index size and this is the first live evidence of it: 17 network
            requests and 0 model calls for 503 names, 335 weighted calls of an allowance of
            100,000. The news feed paged three times to cover one day and attributed 2,038 articles
            to 343 of the 503, which is 5.5's paging working against the provider rather than
            against a capture.
            The list is not a shortlist: 477 of 503 names fired, 798 reasons. That is the flood
            section 11's callout predicted in the abstract and it now has a number
            (owes: The six reason thresholds calibrated from the nights they fired on).
            A first-run backfill is 79.2 seconds for 125,736 bars over 503 per-name requests, which
            is the one place a per-name request is allowed and it is once per name ever.
            What this does not establish: a cold store. Every figure above is a machine whose page
            cache already held the file, and the one cold run this session made was the one carrying
            the defects. It also does not establish a scheduled night, since all three were run by
            hand.
Found:      three defects, none of which the suite could have found, because the committed fixture
            has four names and every one of these is invisible below about five hundred.
            Five stages wrote without a transaction. Every row that commits on its own costs a disk
            sync and a sync costs the same whatever the row holds, so the price is per row rather
            than per byte: 503 rows at about a fifth of a second each. Eleven components already
            batched and the five that did not are the five phase 5 wrote, which is the fixture's
            shape showing through the code. `ForwardReturnFiller` is now atomic as well as fast,
            which it should have been anyway: a fill interrupted halfway left some horizons written
            and a base rate belonging to none of them.
            Four components wrote their run log outcome as the literal `"ok, {dropped} dropped"`
            with the interpolation prefix missing, so the brace reached the column and the run page
            drew it. The worse half is that `RunScreen.Failed` decides the stale-and-failed region
            by comparing the outcome against `ok`, so four stages of every night would have been
            reported as failures on the one region that exists to say what failed. The count was
            already on the detail beside it, correctly, which is why every review passed over it.
            An outcome is a closed vocabulary and not a sentence, so all four now write `ok`.
            And a replayed night froze its own measurement. `--session` resolved to a `FixedClock`,
            so every stage started and ended at the same instant and the operational header drew a
            row of zeroes that reads as a value. The session is what has to be fixed for a replay;
            the duration is what the page is for. `ReplayClock` fixes the one and lets the other
            run, and it is why the table above exists at all.
Mutated:    one mutation, one run, in a worktree under the session scratchpad outside the
            repository, reverted, and the worktree removed. The rule: reinstate the defect the new
            guard was written for, in one of the four files rather than all four, since a guard
            that only fires when every instance is present is a guard nobody will trip.
            `"ok"` back to `"ok, {dropped} dropped"` in `SwingFinder` turns two tests red, being the
            placeholder sweep and the closed-vocabulary assertion.
Carried:    one new obligation. The bulk feed refuses with an unnamed `FormatException` for a
            session that is not the most recent one: 2026-09-04 and 2026-09-08 both fail with "One
            of the identified items was in an invalid format" while 2026-09-09 fetches cleanly. The
            provider appears to serve the bulk file for the last session and something else for
            older ones, and the parser meets that something else and throws rather than refusing by
            name. Section 18 has rows for a payload that is for another session and for one holding
            none of the index, and no row for one that is not a price payload at all.
            It costs nothing tonight, because a scheduled night fetches the most recent session,
            which is the case that works. It costs the catch-up night the `--session` comment
            claims: a machine that was off for three days cannot fetch those days, and the comment
            says it can.
Tests:      476, from 474. Two guards added, both over the replayed chain rather than at the four
            sites, because the next missing prefix will be written somewhere else: no run log value
            may carry a brace around a bare identifier, and every stage of a clean run says `ok`.
            `tools/ci.sh` green end to end, migrations 0 to 18. `tools/verify-phase` green at 237
            claims, 165 PASS, 0 FAIL, 72 out of scope, 0 unexamined. Windows on this machine.
Notes:      the wall clock stays at 5 minutes and stays proposed. One by-hand run on a warm store
            is one observation, and the row is settled by nights that ran on the schedule
            (owes: The nightly wall clock at index size, measured from nights that ran on the
            schedule). What the measurement does say is that the proposed figure is the right order
            of magnitude rather than wrong by three times, which is what it looked like this
            morning before the transactions went in.

### 5.7 - correction: the amended done condition, in the four places it was not swept   2026-09-10
Found:      the phase 5 sign-off read section 20 and reported that it states an unmet phase 5 done
            condition. It does, or did: `docs/ARCHITECTURE.html:1131` still read that phase 5 is
            done when a week of unattended nights has run, which 5.7 had amended in
            `docs/BUILD_PLAN.md` earlier the same day and swept nowhere else. Reading for the rest
            found three more, two of them inside the file 5.7 edited: 5.1's prose said the wall
            clock moves to 5.7 whose done condition is already a week of unattended nights, and
            5.1's own done condition ended with a sentence setting section 17's limit from the week
            of nights that produces a distribution. A source comment in `ReadSurface.cs` carried the
            same wording. So the finding is not one stale cell but an amendment that reached one of
            four statements of the same fact, which is the class this corpus has now met from both
            ends: two places holding one fact, and the one that loses is whichever a reader opens.
Also found: every cell in section 13.2's Phase column was wrong, in the column beside the one being
            repaired. 5.0 ruled the six reason thresholds an operating obligation triggered by sixty
            nights of listings, and repaired that row from 4 to 5 rather than off a phase at all, so
            13.2 and 13.4 both went on saying phase 5 tunes the thresholds while the obligation
            table said no checkpoint produces the evidence. The other three rows read 6 where the
            register is 7.3, the rule versions 7.6 and the model's proposal 7.7, and the sentence
            immediately under the table already said phase 7. That is 5.0's incomplete sweep rather
            than 5.7's, and it is recorded here because it was found here.
Repaired:   four statements of the done condition and five cells of 13.2, as two commits, one per
            checkpoint that owed the sweep. Section 20's phase 5 cell now requires all phase-5 rows
            PASS, the stored plan on every listing, and the schedule registered as a command. No
            claim moved: 237 claims, 165 PASS, 0 FAIL, 72 out of scope, 0 unexamined, unchanged
            either side, because neither section 13 nor section 20 is a claim source. That is the
            reason nothing failed and the reason it needed reading rather than running.
Guarded:    `done-condition-producible`, rostered and implemented, which is the instrument
            `CLAUDE.md`'s calendar-time rule arrived without. It reads all 55 done conditions in
            `docs/BUILD_PLAN.md` and section 20's 8 Done when cells, and refuses a span of calendar,
            several nights or days, an unattended run, and evidence stated as accumulating. The
            column is anchored from the table's own header rather than taken as the last one, so a
            column added to the right of Done when cannot leave the check scanning the new column
            and reporting the same green.
            Shown red against the corpus as it stood, in a worktree under the session scratchpad
            outside the repository, removed after: it names `ARCHITECTURE.html:1131` and
            `BUILD_PLAN.md:426` and passes on the repaired tree. Both directions are permanent
            tests rather than that one run, because a sweep whose expected result is nothing is
            passed every time by a matcher that matches nothing at all; the second asserts that a
            producible condition is left alone, since a matcher catching every mention of a night
            would make the rule unwritable in a system whose work is nights.
            The first attempt at this was one such narrowing. Keyed on a quantity before the period
            noun it caught `a week of unattended nights` and missed `the week of nights`, so it went
            green over `BUILD_PLAN.md:426` while naming section 20 correctly. That is the prefix and
            near-miss class the verification rules already name, found here by running the check
            against the defect rather than by reading it.
            What it does not reach: prose about a done condition, which is where two of the four
            stale statements sat. It asserts that no done condition says this, not that nothing in
            the corpus describes one that did, and widening it that far would require exempting the
            convention that has to quote the forbidden text in order to forbid it.
            5.1's trailing sentence moved out of its done condition into a paragraph of its own in
            the same pass, because a clause sitting inside one reads as something the checkpoint
            waits for, which is the defect the paragraph was repaired for.
Tests:      479, from 476. `tools/ci.sh` green end to end, migrations 0 to 18. `tools/verify-phase`
            green at 237 claims, 165 PASS, 0 FAIL, 72 out of scope, 0 unexamined, 35 checks on the
            roster and 33 carried. Windows on this machine.
Notes:      this session committed the code being corrected and does not sign phase 5 off. The
            repair was made here rather than by the sign-off for that reason: a reviewer that edits
            the spec it measures against and then measures against its own edit is permitted by the
            fresh-session rule, which bars only code, and is the thing the rule exists to prevent.
            The sign-off reviews a consistent corpus instead, and the finding stands to its credit
            rather than being absorbed into the work it was reviewing.

### 5.1 - correction: the clauses the entry did not evidence, and where they were discharged   2026-09-10
Corrects:   the 5.1 entry above records a checkpoint as done against a done condition whose
            second, fifth and sixth clauses it evidences nowhere. `BUILD_PLAN.md:426`, in force
            when 5.1 landed and verified identical at f46e296, requires every index member to
            hold its year, the computed chain to complete for every member, and a night over the
            whole index recorded with the elapsed time per step, with the run's own conditions,
            being a cold or warm store, which network, and whether the provider was slow. The
            entry's Measured block is four fixture names and six constituents, and the strings
            503, elapsed, cold and warm do not appear in it at all.
Found:      by the phase 5 sign-off, and the sharp part is what 5.1's own text says. It named the
            computed chain at index size as the risk phase 5 actually carries: "What has never run
            at index size is what comes after the bars, being the indicators, the swings, the
            volume profile, the levels and the ladder, over 503 names where every night so far has
            run them over four", and then "this checkpoint watches the right thing only because it
            says so". It wrote that into its own done condition and landed without running it.
            The condition was load-bearing rather than ceremonial, and the evidence for that is
            what happened when the run finally came: at dead88d a whole night over 503 names was
            stopped by its own fifteen-minute deadline on the facts step having reached twelve of
            seventeen stages, and three defects were found that the four-name fixture cannot show.
            That is the failure 5.1 was watching for, six checkpoints late.
Discharged: by measurement already in the record rather than by anything built here, which is why
            this is a correction and not a rebuild. Clause one and clause two are answered by the
            two 5.7 entries: 125,736 bars over 503 names covering 2025-09-10 to 2026-09-09, and a
            whole night over 503 names run three times for session 2026-09-09. Clause five is the
            per-stage table in the night-speed entry, 574.7 seconds falling to 44.6 with the row
            counts identical either side. Clause six is stated there in the same breath: warm
            store, home network, provider answering normally, on the operator's Windows machine,
            with "what this does not establish: a cold store" beside it.
Notes:      nothing is owed onward. The wall clock the run produces is already carried as an
            operating row (owes: The nightly wall clock at index size, measured from nights that
            ran on the schedule), which is where 5.7 put it and is not what this corrects. What
            this corrects is the record showing a checkpoint as having met a condition it had not,
            which `done-condition-producible` cannot catch because the condition is producible and
            was simply not produced.

### 5.3 - correction: the done condition amended to what the fixture asserts   2026-09-10
Corrects:   **This entry amends 5.3's done condition**, in those words, because the checkpoint
            that built it did not. 5.3 read "the facts file matches the fixture byte for byte" and
            the fixture refuses to be that: `facts.json` states in its own words that nothing in it
            is frozen from a run and that the payload hash is not stated, because a hash of a
            payload is a function of that payload and stating it would be a regression baseline
            wearing the clothes of an expectation.
Found:      by the phase 5 sign-off, reading each phase 5 done condition against what its
            checkpoint actually asserts. The condition asked for exactly the thing done condition 7
            warns against, being a checkpoint that adds only frozen expectations and has added
            regression detection and called it verification. 5.3 built the stronger form instead
            and said why in its Measured block, "It is written from the rules rather than frozen
            from a run, and every value is asserted against the store it was read from rather than
            against a copy of the payload", and then left the condition saying something else. So
            the checkpoint amended its own done condition in substance and recorded no amendment,
            which is the one thing CLAUDE.md says is not legitimate about amending one.
Repaired:   the condition now reads that every fact and the stage that computed it match the
            fixture, that the payload is written in name order so two runs over one store produce
            the same bytes, and that the per-operation split is proved rather than true by
            construction. That is what `FixtureExpectations` has asserted since 5.3 landed. Prior
            text is in `CHANGELOG.md` and a paragraph beneath the condition says it was amended.
Notes:      no claim moved and nothing was rebuilt. The behaviour 5.3 shipped is the behaviour the
            amended condition requires, and it was the better of the two readings all along.

### 5.4 - correction: the test-count baseline   2026-09-10
Corrects:   5.4's entry records "Tests: 456, from 444". 5.3 landed at 441. The baseline should
            read 441 and the delta the checkpoint added is fifteen rather than twelve.
Measured:   the phase 5 sign-off counted test cases at every commit of the phase, taking facts
            plus inline data cases, which reproduces the suite's own count exactly at HEAD. The
            chain is 415 at the phase 4 sign-off, then 426, 433, 437, 441, 456, 463, 474, 474, 476
            and 479, and every recorded figure matches the tree that landed it. 444 is the count
            after 5.4's own first commit, 358c261, so the baseline names an intermediate state
            inside the checkpoint's own branch rather than the figure the checkpoint started from.
            It is the only break in the chain across phases 0 to 5.
Notes:      no check reaches this and none should. `stated-counts` exempts record entries by
            design, because a record entry is a dated measurement rather than a statement the
            corpus makes about itself, and the exemption is in the check's own first comment. Done
            condition 2 requires the test count recorded in PROGRESS, and 456 is recorded and is
            right; what is wrong is the figure it is stated as having come from. Corrected by this
            entry rather than by editing the one above, because the record is append only.

### 5.7 - correction: the repairs the phase 5 sign-off found   2026-09-10
Corrects:   fourteen things the sign-off found across phase 5, none of which needed anything
            rebuilt. This session committed the code and does not sign phase 5 off.
Found:      three done conditions not met as written, and each fails differently. 5.1's is above.
            5.3's is above. 5.5 required "the page count is measured over two universe sizes and
            shown not to grow with the population", and the test carrying it called
            `feed.ArticlesAsync` twice with the same date pair and asserted a counter had moved
            from one to two. No universe reaches that seam: `INewsFeed.ArticlesAsync` takes a date
            range and no member set, so the test showed that a second call increments a counter
            and its own comment said it was asserting the count "over two universe sizes". A check
            that states its own scope wrongly is the class CLAUDE.md names as the one that
            survives, and this one had been passing since 5.5.
Also found: five checks reaching half of what their roster rows claim. `ci-parity` compares step
            names and reads neither script's data root, so 5.7's repair of the two scripts had no
            instrument at all and the strings `data-ci` and `DataRoot` appeared nowhere under
            `src/`: the fault that dropped the operator's store was fixed in two files by hand and
            nothing stopped it returning. `two-platform`'s swallow guard was a blocklist of three
            literals, and a leg is skipped by any YAML condition whatever, none of which need
            contain any of the three; a skipped job does not fail its run. `clock-usage` read the
            parse direction and not the render, so every `started_at` and `ended_at` in the tree
            was written against the machine's calendar while every `session_date` beside them
            passed a culture. `store-portability` had never scanned a row a shipped component
            wrote: one scan over an empty store and two over a probe table the test creates, so
            every row it had ever read was one it had written itself. `price-storage-form` read
            the migrations and nothing of the code half of the rule it is named for.
            And three crossings between the decimal world and the double one bypassed the helper
            named for them, two of them structurally unable to reach it, because `Statistic` sat
            in `EquityBrief.Data` and `EquityBrief.Core` does not reference `EquityBrief.Data`.
            A helper the rule requires and half the tree cannot see is a helper that gets bypassed.
            The operational header drew how long each stage took and never the instant it started,
            while `BUILD_PLAN.md` says twice that it carries both and the posting hour obligation
            names it as the surface its trigger is read on. A posting hour is a clock time and
            cannot be read off a duration. `run_log` has stored `started_at` since 1.1 and it
            reached no surface.
            `tools/ci.ps1` set its data root and its working directory in the caller's own
            PowerShell process, which `tools/ci.sh` cannot do because its export dies with its
            process. So the two were not the same in effect on the one platform the operator runs
            them on, and `RUNBOOK.md` step 7 followed by step 9 sent a hand-run night into
            `data-ci`. Its cmdlet-only step also died unnamed: `Step` judged a step by
            `$LASTEXITCODE`, which a cmdlet never sets, so "drop the store" failed through a
            terminating error under the Stop preference and the failed-at-step line never printed.
            Three floors. The out-of-scope pair fell 100 to 80 at 5.4 and 80 to 70 at 5.6 against
            a value of 72, and each fall was recorded with the sentence "it is a fact about how far
            the build has got and not about the property", which is the rule's own condition for
            leaving such a scope as context rather than flooring it. Three sessions wrote the
            condition and lowered the floor anyway. The third fell 100 to 80 in the same sweep as
            the first pair and is the total claim count, which only grows, has gone 203, 226, 234,
            237 and was never near either number; it got no comment where the other two got one,
            and its failure message still said 100 while its assertion said 80.
            And the obligation nobody filed. 5.7's own entry states that `ForwardReturnFiller`'s
            transaction is a correctness fix and not only a speed one, "a fill interrupted halfway
            left some horizons written and a base rate belonging to none of them". That is the
            shape the phase 1 sign-off already ruled on for the refetch, where a scan for the
            keyword reported the construct and a behavioural test carried the claim. Nothing
            asserted it and no row was filed, and CLAUDE.md says obligations are recorded when
            created rather than remembered.
Repaired:   all fourteen. The two-universe assertion runs the counter over two membership sets
            against the same captured day. The interrupted fill is induced downstream of twelve
            real writes by a stored plan whose stop sits at or above its target, and the stage is
            asserted to leave nothing behind and to be repeatable once the bad plan is gone. The
            five checks are widened and their roster rows say so. `Statistic` moved to
            `EquityBrief.Core.Prices` beside `PriceForm` with a sibling named for the ratio
            crossing, and the set of crossing helpers is asserted as a set rather than a count.
            The header draws the instant, in UTC and labelled, with the whole instant in the
            attribute because a night that starts after the close in New York carries tomorrow's
            UTC date. `ci.ps1` restores both the root and the location in a `finally`, which runs
            on `exit` and preserves the code, and names a cmdlet-only failure. The two out-of-scope
            floors and the plan-derived one are non-vacuity guards of one now, since all three
            reach zero at phase 7 by construction, and the claim floor is back at 100 with its
            message derived from it.
            Fifty-six date renderings across twenty-five files gained a culture. Sixteen
            expectation files gained a declared `derivation`, and the check reads the field rather
            than the prose: a file frozen from a run whose `derivedFrom` said "the run of
            2026-09-10" passed unchanged before, which is the shape that check exists to reject.
Guarded:    every repair that is a property rather than a text edit. The additions are the two
            universe sizes, the interrupted fill, the header's instant, the data root in both
            scripts, the cmdlet failure path, every YAML condition, the rendering direction of
            `clock-usage`, the populated-store scan, the crossing-helper set, the declared
            derivation, the record's nesting inside its reason, and the wall clock claim read off
            its description cell rather than off the whole row.
            That last one is worth naming: `TheLimitsRowStatesTheFiguresTheCodeUses` sliced from
            the subject to `</tr>`, which spans all four cells, and two of the phrases it looks
            for appear twice inside that row, once in the description and once in the Asserted-by
            cell. So deleting the claim from the cell that carries it left the assertion green.
            5.7 wrote that assertion specifically to stop the wall clock and the deadline being
            held in their stated relationship by a comment, and it was reading the wrong span.
Mutated:    twelve distinct mutations over sixteen runs, in a worktree under the session
            scratchpad outside the repository, reverted, and the worktree removed. The worktree
            list holds the same four entries it held before, being this tree and the three the
            phase 3 sign-off recorded. The rule was stated before the sweep: each mutation
            reinstates the defect the new assertion was written to catch, named by the property it
            breaks rather than by the line it edits, and where the repair widened a check the
            mutation is the code or corpus text that check now has to refuse.
            All twelve turn a test red. Four were invalid on the first attempt and were corrected
            and re-run, which is the four extra runs.
            One of those four is worth the space, because it is the difference between an invalid
            mutation and a surviving one and they look identical from the outside. Dropping
            `await using` from `ForwardReturnFiller`'s transaction left the test green, and it
            removed nothing: the connection is disposed at the end of the same scope and disposing
            an open transaction rolls it back, so the atomicity was still there. The mutation that
            reaches the property is one transaction per listing rather than one around the stage,
            which is the shape the stage had before 5.7, and it turns the test red. A green run
            recorded as a survivor there would have said the assertion was weak when what was weak
            was the edit.
            The properties added and not mutated: the claim floor restored to 100, the two
            out-of-scope guards and the plan-derived guard, and the section 19.1 run page row.
            The three floors are not mutated because a floor is not a property and the whole point
            of the repair is that the number was standing in for one; what carries the property at
            each site is the assertion beneath it, and each of those already had a proof. The 19.1
            row is placed rather than asserted, and `architecture-conformance` refused it on the
            first run by deriving a due point of 5.1 from the plan's prose, which is the placement
            reconciliation working.
Tests:      491, from 479. `tools/ci.sh` green end to end, all 6 steps, migrations 0 to 18, exit 0.
            `tools/ci.ps1` green end to end on this machine, and the session it ran in kept its own
            data root and its own working directory. `tools/verify-phase` green at 238 claims, 166
            PASS, 0 FAIL, 72 out of scope, 0 unexamined, 173 placements and verdicts reconciled
            against a floor of 34, 35 checks on the roster and 33 carried, 33 ran and 0 did not.
            Windows on this machine; the matrix carries macOS and the Linux case-sensitivity job.
            The claim count moves by one and the PASS count with it, both from section 19.1's new
            run page row. Out of scope is unchanged at 72.
Carried:    one new row (owes: Every stage that writes in a loop opens a transaction, asserted
            rather than read), created at the sign-off and half discharged by it. The correctness
            half is asserted; what 6.0 decides is whether a source scan over the loop shape earns
            its false positives, and the argument against is already written into the row: all
            sixteen writers loop, so a scan keyed on the loop reports every one of them and a scan
            keyed on the transaction reports none.
Notes:      the matrix was verified from the runs rather than from these entries. Every one of
            phase 5's eleven merges was enumerated by job rather than by run conclusion, since a
            skipped job leaves a run green, and all three jobs, being windows-latest, macos-latest
            and the case-sensitivity leg, report success on all eleven with seven steps each.
            What is not repaired, and is named here so it is not mistaken for an oversight:
            `main` carries no branch protection and no rulesets, so the merge rule is enforced by
            convention. That is a setting on the repository rather than a change to it, and it is
            the operator's to make.

### 5.4 - correction: the second evening could not finish   2026-09-11
Corrects:   the 5.4 entry above records the shortlist builder as done, and the first night the
            schedule ran shows it refusing every evening after a store's first. **This entry amends
            5.4's done condition**, in those words, because the condition as written was satisfied
            by nights that could not complete.
Found:      by the phase 5 sign-off reading the run log of the first scheduled night, 2026-09-10,
            read-only through an immutable connection. It wrote eleven stages, every one `ok`, and
            stopped after `moves`: no `listings`, `facts`, `changes`, `forward-returns`, `news-pulse`
            or `close`, and no listing for the session. Task Scheduler recorded `LastResult 0x1`.
            The refusal was reproduced on `main` by running the shortlist builder over a copy of the
            store: "The facts file for A is dated 2026-09-09 and the bars end on 9/10/2026".
            The builder compared tonight's bars with the newest facts row for equality. Section 14
            writes the listings at step 12 and the facts at step 13, so on every evening after a
            store's first the newest facts row is last night's and the builder refused. No test
            could see it: every night the suite ran was a store's first or a re-run of one session,
            and the replay chain runs the facts before the listings where the night runs them
            after, so the builder only ever met a facts row for its own session. The by-hand nights
            at index size were three runs of 2026-09-09 and could not see it either. The guard had
            no test of its own in either direction.
            The done condition that would have caught it on the second morning was the week of
            unattended nights 5.7 amended away. The amendment was right that calendar time cannot
            be a done condition, and it dropped the half a checkpoint can produce: a night replayed
            over a store that already holds the one before it.
Repaired:   the guard refuses only facts newer than the bars, which is the listing written behind a
            facts file the store holds that it exists to refuse. Older facts are last night's file
            and equal facts are a re-run. Its message renders the date against the invariant culture
            rather than printing "9/10/2026". 5.4's done condition reads "on every night, asserted
            on a night replayed over a store that already holds the night before it rather than
            only on a store's first".
Guarded:    a second consecutive night run end to end, which fails on the old guard with the live
            night's own message and passes on the new one with a listing for every member on both
            sessions, the second session's facts written and its `close` row present; and the guard
            in both directions, refusing facts dated after the bars with both dates named and
            running over facts dated before them.
            `done-condition-producible` refused the first wording of the amended condition, "two
            consecutive sessions", as a span of calendar. It is a replay and a checkpoint produces
            it, so the matcher cannot tell a replayed span from a waited one; the condition was
            reworded to say it is a replay rather than the matcher being loosened.
Mutated:    the rule, stated before the sweep: reinstate the defect the new assertions were written
            for, being the guard comparing for equality. One mutation, one run, in a worktree under
            the session scratchpad outside the repository, reverted, and the worktree removed. It
            turned the consecutive night and the guard's own test red and left the rest green. None
            of the properties added went unmutated.
Lost:       the listing and the plan for 2026-09-10 were not written. The bars, indicators, swings,
            profile, levels, ladders and moves for that session were, and stand. Whether the listing
            can still be written depends on a by-hand night for that session reaching its fetch,
            which is recorded where the fetch is.
Tests:      493, from 491. `tools/ci.ps1` green end to end, all 6 steps, 0 warnings, migrations 0 to
            18, exit 0. `tools/verify-phase.ps1` green at 238 claims, 166 PASS, 0 FAIL, 72 out of
            scope, 0 unexamined, 173 placements and verdicts reconciled against a floor of 34, 35
            checks on the roster and 33 carried, 33 ran and 0 did not. No claim moved. Windows on
            this machine; the matrix carries macOS and the Linux case-sensitivity job.

### 5.6 - correction: a stopped night left no record on the run page   2026-09-11
Corrects:   the 5.6 entry above records the run page's stale-and-failed region as drawing what
            failed in which component, and no night could put anything there. A step that stopped
            wrote nothing to the run log.
Found:      by the phase 5 sign-off, reading the same first scheduled night as the 5.4 correction
            above. Its failed step's name and reason went to stderr, which a scheduled task
            discards, so the run page drew eleven clean stages and an empty stale-and-failed region
            for a night the scheduler recorded as a failure. Section 18's unavailable-feed row
            promises the step and the reason "on the run log", and its claim had been PASS since
            phase 2 with a verdict note that omitted those three words. The one test of the
            stale-and-failed region built its failed row by hand, a shape the night could never
            produce, which is an unproducible shape asserted in the direction that makes the region
            look reached. And the sign-off's first repair pass had removed "what failed in which
            component" from the operational header on the reading that this region delivered it,
            while `RUNBOOK.md` went on telling the operator the run page says it.
Repaired:   a stop writes its own run log row through `NightClose.RecordStopAsync`, as `failed` for
            a step that threw and `stopped` for the deadline and the allowance, under the first run
            log stage the step writes that the run does not hold yet, so a stop inside the facts
            step is recorded under `changes` rather than dropped on the conflict with `facts`. The
            detail has the data root named and any other rooted path replaced before it is stored,
            because exception text carries absolute paths. A stop that cannot be recorded still
            exits 1 and says so on stderr. Section 15.10's stale-and-failed row names the stage a
            night stopped on, and the Night close catalogue row says it records the stop.
Guarded:    the stop recorded on the run log for a feed that did not answer, read back through the
            read API and drawn by the stale-and-failed region; the deadline's stop recorded as
            `stopped`; the allowance stop recorded against the step it preceded, where the test had
            asserted the run log stayed empty, which was the absence of the record rather than the
            presence of the stop; the composite step's stop landing under the stage that stopped;
            and the detail carrying no absolute path, both through the helper with the input shown
            absolute first and through the row the recorder writes.
Mutated:    the rule, stated before the sweep: each mutation reinstates the defect a new assertion
            was written for. The thrown step's stop is not recorded; the recorder writes under its
            first candidate only; the detail is stored unscrubbed. Three mutations over four runs, in
            a worktree under the session scratchpad outside the repository, reverted, and the
            worktree removed. The first two turned a test red on the first run, the unavailable-feed
            test and the composite-step test.
            The third survived at first. The scrub had a test of its own and nothing asserted that
            the recorder applies it, which is a missing property rather than an unproducible shape:
            the stored row is what the rule is about and no test read one. A test now writes a stop
            whose detail carries the store's own path and reads the row back, and the same mutation
            turns it red.
            Added and not mutated: the allowance stop's row and the deadline's, both asserted and
            both through the recorder the first two mutations reach.
            The three were run again on this commit's own tree after it was split from the 5.4
            correction, three more runs, and each turned exactly one test red.
Tests:      496, from 493. `tools/ci.ps1` green end to end, all 6 steps, 0 warnings, migrations 0 to
            18, exit 0. `tools/verify-phase.ps1` green at 238 claims, 166 PASS, 0 FAIL, 72 out of
            scope, 0 unexamined, 173 placements and verdicts reconciled against a floor of 34. No
            claim moved: the stale-and-failed row changed what it says and not whether it is a
            claim, and the unavailable-feed claim's verdict note now states the run log half it had
            omitted. Windows on this machine; the matrix carries macOS and the Linux
            case-sensitivity job.

### 2.1 - correction: one fund's fractional volume refused the whole exchange's file   2026-09-11
Corrects:   the 2.1 entry above records the reader's row policy on the live exchange file, being a
            symbol that did not trade passed over and named and anything else refusing the file.
            The second clause refused whole nights for a row the index does not hold. And the
            record of 5.7 and the obligation row it filed, which read the refusal as the provider
            serving something other than prices for an older session and carried it to 6.0.
Found:      by the phase 5 sign-off, with two live requests on the operator's key, the first of
            them approved as a probe. The file for 2026-09-09, fetched a day later, answered 200 as
            JSON dated for that session. The file for 2026-09-08 answered the same way with 50,249
            rows, and six carry a fractional volume: FMAO, EWG, EZU, EWW, API and ALP, the largest
            being EWG at 530131.7. None is an index member. The reader took every volume through
            `GetInt64`, which throws a `FormatException` with the framework's default message, "One
            of the identified items was in an invalid format", and the parser let it refuse the
            whole file. That is the message 5.7 recorded for 2026-09-04 and 2026-09-08, and it named
            no row, no field and no ticker, which is how a reader defect was read as the provider
            sending something other than prices.
            It is not a catch-up defect. Any evening whose file carries one such row anywhere on the
            exchange stops the night at the fetch, and two of the first four days fetched at index
            size did. 2026-09-09 and 2026-09-10 happened not to.
Repaired:   the volume reader refuses a fraction by name, with the ticker and the number that
            arrived, and never rounds it. The bulk parser passes over a row that names its ticker and
            cannot be read, keeping the reason, where the caller collects such rows, and refuses as
            before where it does not. The fetcher refuses the night by name, before anything is
            stored, if a current member's row is among them, because a member with no bar tonight
            would read as a shorter history. The fetch line counts the rows outside the index the
            reader refused. Section 18 gains a row for a row the reader cannot read, placed PASS by
            `nightly-run`. The obligation is discharged here rather than at 6.0, and 6.0's text no
            longer carries it (owes: A bulk payload that is not a price payload refused by name).
Recoverable: the listing and the plan for 2026-09-10, which the 5.4 correction above records as not
            written, can still be written, and only until the next scheduled night stores
            2026-09-11. The store's bars end on 2026-09-10, so a by-hand night for that session run
            before then computes its listing from the series as it stood that evening; after it,
            the listing stage reads a later ladder. That run is the operator's to start, on the
            operator's key, and this entry does not record it as done.
Guarded:    the reader over four rows copied from the provider's own file, two whole volumes and two
            fractional, returning the two and naming the two with their numbers; the strict path
            naming the row; the fetcher storing every member past a fund's fractional volume and
            refusing by name on a member's; and a whole night doing both, with the member's refusal
            on the run log.
Mutated:    the rule, stated before the sweep: each mutation reinstates the defect a new assertion
            was written for. The volume is read through `GetInt64` again; the parser refuses the file
            on a row it cannot read; the fetcher stores past a member's refused row.
            Three mutations, three runs, in a worktree under the session scratchpad outside the
            repository, reverted, and the worktree removed, over source identical byte for byte to
            this commit's. All three turned tests red: the first and second each turned the
            reader's test, the fetcher's and the whole night's red, and the third turned the
            fetcher's and the whole night's. None was invalid. None of the properties added went
            unmutated.
Tests:      499, from 496. `tools/ci.ps1` green end to end, all 6 steps, 0 warnings, migrations 0 to
            18, exit 0. `tools/verify-phase.ps1` green at 239 claims, 167 PASS, 0 FAIL, 72 out of
            scope, 0 unexamined, 174 placements and verdicts reconciled against a floor of 34. The
            claim and the PASS each move by one, both from section 18's new row. Windows on this
            machine; the matrix carries macOS and the Linux case-sensitivity job.

### 5.7 - correction: the sign-off repair's own gaps, found by reviewing it   2026-09-11
Corrects:   the 5.7 entry recording the repairs the phase 5 sign-off found, which a session that had
            committed no code to them reviewed independently for the first time. Two of its widened
            checks missed the shape each was widened for, one of its obligation rows was labelled
            discharged while half of it is owed, and its floor sweep left one floor standing on the
            reasoning it used to convert three others.
Found:      `price-storage-form`'s code half read crossing signatures. The three inline casts the
            repair moved onto the helpers sit in methods the signature reader either never matches
            or already permits, so putting any of them back left the suite green: a mutation
            reinstating the casts in `MoveSeries` and `UniverseScreen.Distance` ran 491 of 491.
            `clock-usage`'s rendering half read `ToString` with no provider and not an
            interpolation hole carrying a date format. That is how all five provider request URLs,
            the fetch stage's run log detail, the run ids and eleven messages in the shipped source
            rendered their dates, and the entry's "fifty-six renderings" was a count over what the
            matcher could see rather than over the property. Nothing is wrong on the operator's
            machine, whose culture is en-US on the Gregorian calendar; on one that is not, the night
            would ask the provider for a year that has not happened.
            The transaction obligation read "6.0, discharged" while its own text and 6.0's paragraph
            said half of it is owed there, so the table stood at 43 discharged and 4 open where it is
            42 and 5. `obligation-reconciles` exempts a discharged row by design and cannot read
            intent from a note, so only the label could have said it.
            `obligation-reconciles` kept a floor of 3 on open rows, one below their count, beside the
            sentence the repair quoted as its reason for converting the out-of-scope floors, and a
            floor of 2 on operating rows of the same shape.
            And a figure. The night-speed entry records 17 network requests for the third by-hand
            night of 2026-09-09, and the run log sums to 8 for that run. The 17 is right and is the
            feeds' own count: the actions row records the corporate action feed's two requests and
            not the nine per-name refetches the stage made. So the store under-reports the one
            request the cost rule carves out by name, which is a defect in that stage's count
            rather than in this record and is corrected in its own entry.
Repaired:   `price-storage-form` states every explicit cast to double or to decimal in the shipped
            source as a set of sites, fifteen of three kinds, so a cast added inline is a new site.
            `clock-usage` reads an interpolated literal and passes a date hole only where the literal
            is handed to the invariant culture; the thirty-two loose holes, eleven shipped and
            twenty-one in the suite, are wrapped in `FormattableString.Invariant`. A hole with no
            format over a date value is outside what a text reader can reach and the check says so.
            Both roster rows state the halves they now read. The transaction row reads "6.0". Both
            floors in `obligation-reconciles` are non-vacuity guards of one.
Guarded:    the cast set, with the reader shown to key on the operand and to report each of the three
            inline casts as a site the set does not hold; the interpolation sweep, with the reader
            shown to report a request URL and a run id, to pass the three invariant forms, and to
            leave a number, an escaped brace and a comment alone.
Mutated:    the rule, stated before the sweep: each mutation reinstates the defect a new assertion was
            written for. The inline casts go back into `MoveSeries` and `UniverseScreen.Distance`;
            one provider request URL loses its invariant wrapper. Two mutations, two runs, in a
            worktree under the session scratchpad outside the repository, reverted, and the
            worktree removed. The first turned the cast set red, which is the same edit that ran
            491 of 491 before it; the second turned the interpolation sweep red. Each left the other
            502 green. None was invalid.
            Not mutated, and why: the obligation label and the two guards are not properties a test
            carries. The label is a statement a check exempts by design, and a guard of one is what
            the number was ever doing, for the reason the repair itself gave.
Tests:      503, from 499. `tools/ci.ps1` green end to end, all 6 steps, 0 warnings, migrations 0 to
            18, exit 0, after the Api project's intermediate folder was removed: an interrupted
            build had left a cache file of NUL bytes that failed the build step, which is a fault in
            the machine's build output and not in the tree. `tools/verify-phase.ps1` green at 239
            claims, 167 PASS, 0 FAIL, 72 out of scope, 0 unexamined, 174 placements and verdicts
            reconciled against a floor of 34. No claim moved. Windows on this machine; the matrix
            carries macOS and the Linux case-sensitivity job.

### 1.6 - correction: the actions stage counts the refetches it makes   2026-09-11
Corrects:   the 1.6 entry above records the corporate action check's cost as two requests whatever
            the universe is. It is two plus one per member an action moved, and the run log carried
            the two alone.
Found:      by the phase 5 sign-off, reconciling a figure. 5.7's night-speed entry records 17
            network requests for the third by-hand night of 2026-09-09 and the run log's rows for
            that run sum to 8. The 17 is the feeds' own count. The actions row records
            `actions.Requests`, the corporate action feed's two, and not the historical feed's
            nine per-name refetches the same stage made; the scheduled night of 2026-09-10 made
            nine at that stage and recorded two. So the per-name request the cost rule carves out by
            name was the one request the store never counted, and the operational header read the
            stage as two. The test beside it asserted the stage reported two while the fixture
            refetches AAPL, which held the undercount in place.
Repaired:   the stage's request figure is both feeds' requests made during the run, taken as deltas
            because the night shares one historical feed between the backfill and this stage, and the
            detail says how many of them were per name.
Guarded:    the fixture's one refetch counted off the feed that made it, the stage's figure being the
            action feed's two and that one together, and the run log row carrying three with the
            per-name part stated.
Mutated:    the rule, stated before the sweep: reinstate the defect, being the stage reporting the
            action feed's requests alone. One mutation, one run, in a worktree under the session
            scratchpad outside the repository, reverted, and the worktree removed. It turned the
            renamed test red and left the other 502 green. None of the properties added went
            unmutated.
Tests:      503, unchanged: the test that held the undercount is renamed and widened rather than
            added beside. `tools/ci.ps1` green end to end, all 6 steps, 0 warnings, migrations 0 to 18,
            exit 0. `tools/verify-phase.ps1` green at 239 claims, 167 PASS, 0 FAIL, 72 out of scope, 0
            unexamined. No claim moved. Windows on this machine; the matrix carries macOS and the
            Linux case-sensitivity job.

### 5.0 - correction: the superseded news decision, deleted rather than moved   2026-09-11
Corrects:   the 5.0 planning entry above, which records "News is one dated query, paged to cover the
            day, and attributed to names locally" as superseding the decision whose name said one
            request. The superseded entry was deleted in that commit, 234e5a1, and "Previously
            decided" never held it, which is the one thing `CLAUDE.md` says a supersession does in
            the same commit. Its text survived only in a `CHANGELOG.md` quotation cut short with an
            ellipsis.
Found:      by the phase 5 sign-off, comparing what the 5.0 entry says it superseded with what
            "Previously decided" holds. 5.0's other supersession in the same pass, the stop rule,
            was moved correctly. No check could notice: `no-superseded-citation` asks whether a
            citation resolves to a superseded entry, and a deleted one resolves to nothing, so it is
            invisible to the check that exists for superseded entries.
Repaired:   the entry is restored under "Previously decided" with its reasoning as it stood,
            recovered from the revision before 234e5a1, with the date, the decision that replaced
            it, what was superseded and what stands, and a sentence saying it was deleted and
            restored. `record-append-only` now holds every decision name `DECISIONS.md` has ever
            carried as a high-water mark read from the history, the same way it holds `PROGRESS.md`'s
            headings. 118 names over 14 revisions, none lost once this one is back, so the check
            carries no exemption.
Guarded:    the walk over the history, and the reader's proof that a moved name stays, a deleted
            name goes, and bold inside a line is not a name.
Mutated:    the rule, stated before the sweep: reinstate the defect, being the superseded entry
            deleted rather than moved. One mutation, one run, in a worktree under the session
            scratchpad outside the repository: the restoration committed there and a second commit
            deleting the entry again, since the check reads the history rather than the file. It
            turned the new check red naming the entry and the commit that first wrote it. The
            worktree and its two commits were removed. None of the properties added went unmutated.
Tests:      505, from 503. `tools/ci.ps1` green end to end, all 6 steps, 0 warnings, migrations 0 to
            18, exit 0. `tools/verify-phase.ps1` green at 239 claims, 167 PASS, 0 FAIL, 72 out of
            scope, 0 unexamined. No claim moved. Windows on this machine; the matrix carries macOS and
            the Linux case-sensitivity job.

### 4.1 - correction: a ladder row with no stored bar was dated by the UTC date   2026-09-11
Corrects:   the 4.1 entry above records a ladder row written for every member, with one that has no
            stored bar dated by the clock. It was dated by the clock's UTC date rather than its
            session date, which `IClock` exists to keep apart.
Found:      by the phase 5 sign-off, reading the shortlist builder's matching line and sweeping the
            shipped source for the same shape: two sites, this one and 5.4's. After eight in the
            evening in New York the UTC date is tomorrow's, so a night running then dated this row a
            day ahead of every row beside it. Nothing on the operator's machine has run that late:
            the schedule is 23:30 UTC, which is the same date in both zones, winter and summer.
Repaired:   the row is dated `clock.SessionDateAt(clock.UtcNow)`.
Guarded:    the ladder builder run at an instant after midnight in UTC and before it in New York, with
            the two dates asserted to differ first, over a member the backfill never saw.
Mutated:    the rule, stated before the sweep: reinstate the defect, being the row dated by the UTC
            date. One mutation, one run, in a worktree under the session scratchpad outside the
            repository, reverted, and the worktree removed. It turned the new test red and left the
            other 505 green. None of the properties added went unmutated.
Tests:      506, from 505. `tools/ci.ps1` green end to end, all 6 steps, 0 warnings, migrations 0 to
            18, exit 0. `tools/verify-phase.ps1` green at 239 claims, 167 PASS, 0 FAIL, 72 out of
            scope, 0 unexamined. No claim moved. Windows on this machine; the matrix carries macOS and
            the Linux case-sensitivity job.

### 5.4 - correction: a listing with no stored bar was dated by the UTC date   2026-09-11
Corrects:   the 5.4 entry above records a listing row for every member, with one that has no stored
            bar dated by the night. It was dated by the clock's UTC date rather than its session
            date, the same shape as the ladder builder's line corrected in the 4.1 entry above.
Found:      by the phase 5 sign-off, reading the builder while reproducing the first scheduled
            night's refusal. Latent on the schedule as registered, which runs at 23:30 UTC, the same
            date in both zones; a night after eight in New York dates the row a day ahead.
Repaired:   the row is dated `clock.SessionDateAt(clock.UtcNow)`.
Guarded:    the shortlist builder run at the same late instant as the 4.1 test, over the same
            member the backfill never saw.
Mutated:    the rule, stated before the sweep: reinstate the defect, being the row dated by the UTC
            date. One mutation, one run, in a worktree under the session scratchpad outside the
            repository, reverted, and the worktree removed. It turned the new test red and left the
            other 506 green. None of the properties added went unmutated.
Tests:      507, from 506. `tools/ci.ps1` green end to end, all 6 steps, 0 warnings, migrations 0 to
            18, exit 0. `tools/verify-phase.ps1` green at 239 claims, 167 PASS, 0 FAIL, 72 out of
            scope, 0 unexamined. No claim moved. Windows on this machine; the matrix carries macOS and
            the Linux case-sensitivity job.

### 5.6 - correction: the stale-and-failed claim was PASS over parts phase 6 builds   2026-09-11
Corrects:   the 5.6 entry above places section 15.10's stale-and-failed region PASS. The row names
            names carrying yesterday's bars, sections that fell back and documents refused by
            admissibility, and the last two are the claim checker's at 6.3 and admissibility's at
            6.2. The region itself draws them as absent. So the verdict was PASS over a row its check
            reached a third of, which is an unexamined claim wearing a verdict; the verdict note
            described the stale names and the failed stages, the second of which no night could
            then produce and the row did not name.
Found:      by the phase 5 sign-off, reading the region's verdict note against its row while
            checking whether the operational header's narrowing at the first repair was covered.
Repaired:   the row is decomposed the way 5.0 split the reason record, into its four parts as the
            row now words them: names carrying yesterday's bars, PASS by `read-surface`; the stage a
            night stopped on, PASS by `nightly-run`, which is the only check that runs a night; and
            sections that fell back and documents refused by admissibility, out of scope until 6.3
            and 6.2. `architecture-conformance`'s stated counts move with it, forty-four claim
            subjects to forty-seven and twenty decomposed elements to twenty-four, each with the
            reason written beside it.
Guarded:    the placement reconciliation that already exists: each element is asserted to appear in
            the row's own cell, and each PASS names a check whose declared reach includes it.
Mutated:    the rule, stated before the sweep: reinstate the defect, being a phase 6 part of the row
            placed PASS by a check that does not reach it. One mutation, one run, in a worktree
            under the session scratchpad outside the repository, reverted, and the worktree removed:
            sections that fell back placed PASS by `read-surface`. It turned fifteen of the
            conformance tests red, the report refusing to reconcile a placement no reach declaration
            covers, and left the rest green.
Tests:      507, unchanged. `tools/ci.ps1` green end to end, all 6 steps, 0 warnings, migrations 0
            to 18, exit 0. `tools/verify-phase.ps1` green at 242 claims, 168 PASS, 0 FAIL, 74 out of
            scope, 0 unexamined, 175 placements and verdicts reconciled against a floor of 34.
            Predicted before the run and matched: one claim became four, two PASS and two out of
            scope, so claims rise by three, PASS by one and out of scope by two. Windows on this
            machine; the matrix carries macOS and the Linux case-sensitivity job.

### 5.7 - the missed session caught up in bulk, and the schedule moved by refusals   2026-09-11
Built:      a table of the exchange's closures pinned in code, and the fetch fetching in bulk, one
            request each and before tonight's, every session the store is missing since the last
            night that ran, all stored in one transaction. Two decisions and one supersession, a
            section 18 row, section 14's fetch step reworded, and the posting-hour obligation
            retired for one whose trigger can answer what it names.
Decided:    **A session the night finds missing is fetched in bulk before tonight's**. A night that
            does not run leaves every name short the same session, and the calendar the store can
            observe is the union of its names' dates, so that is the one hole it cannot see: every
            indicator, move and forward return after it would count across it as one session,
            which is what "Bars are never interpolated" forbids. 5.7 had read the provider as
            unable to serve an older session, so nothing could fill one, and the 2.1 correction
            above showed that reading was a reader defect. The cost is one bulk request per missed
            session, which grows with nights missed and never with names.
            **The night runs at a fixed UTC instant, moved only when a night finds the day's file not
            yet posted**, superseding the entry that set the instant at a measured posting hour plus a
            margin, which moves to "Previously decided" with its reasoning. A scheduled fetch can only
            bound the hour from above, so five nights would fire the old trigger and settle nothing,
            which the old row's own text conceded. A refusal is the only evidence that moves the
            instant, and with the catch-up it costs a day's delay rather than a gap.
Measured:   the closure table against the operator's store, read through an immutable connection:
            its 252 sessions from 2025-09-10 to 2026-09-10 are exactly the weekdays in that span less
            the closures the table lists, none extra and none missing. The years after that span
            follow the exchange's published rules and are not observed; the table covers to the end
            of 2027 and refuses a weekday past it rather than guessing.
Guarded:    a night after one that did not run, fetching the missed session first with one request
            more, every member on both sessions and the session named on the run log; a closure
            between two nights costing no request; a missed session whose file holds none of the
            index stopping the night by name with nothing stored, not even tonight's, and the stop on
            the run log; and the table agreeing with the fixture's captured calendar over a year of
            weekdays, refusing a weekday past its range and asking nothing of a weekend.
Carried:    two rows. (owes: The night's instant moved later when a night finds the day's file not
            yet posted) is operating on a trigger of one refused night, read on the stale-and-failed
            region. And (owes: Every computation over a name whose stored series has a gap stops and
            reports the gap), due at 6.0: the decision on what a gap is has said since 1.5 that a
            computation over a gapped series stops, and only the backfill consults the calendar. The
            catch-up closes the hole every name shares; a name the day's file carried nothing for,
            two of 503 on an ordinary night, still has an interior hole the stages count across.
Mutated:    the rule, stated before the sweep: each mutation reinstates the defect a new assertion
            was written for. The fetch fetches no missed session; the table loses Labor Day 2026; a
            missed session that cannot be fetched is passed over rather than stopping the night.
            Three mutations, three runs, in a worktree under the session scratchpad outside the
            repository, reverted, and the worktree removed. All three turned tests red. The first
            turned the catch-up night and the unfillable night red; the second turned 31 red,
            because every fixture night crosses that closure and the fetch asked the provider for it;
            the third turned the unfillable night red alone. None was invalid, and none of the
            properties added went unmutated.
Tests:      511, from 507. `tools/ci.ps1` green end to end, all 6 steps, 0 warnings, migrations 0 to
            18, exit 0. `tools/verify-phase.ps1` green at 243 claims, 169 PASS, 0 FAIL, 74 out of
            scope, 0 unexamined, 176 placements and verdicts reconciled against a floor of 34. The
            claim and the PASS each move by one, from section 18's new row; section 14's fetch step
            changed its words and stayed one claim. Windows on this machine; the matrix carries macOS
            and the Linux case-sensitivity job.

### 5.7 - addendum: the first scheduled night, measured as far as it ran   2026-09-11
Measured:   the first night the schedule ran, 2026-09-10, read from its run log through an immutable
            connection. It started at 23:30:03 UTC and wrote eleven stages before it stopped at step
            12 for the reason the 5.4 correction above records: 494 seconds from membership through
            moves, against 44.6 seconds for a whole by-hand night of 2026-09-09 on a warm store.
            Indicators took 156 seconds where the by-hand night took 18, the volume profile 120 and
            the levels 156, over 505 names. It made 7 requests at the fetch and the stages before it,
            and the actions row recorded 2 of the 9 its stage made, which is the 1.6 correction.
            What it does not establish: a distribution, or a cold store as against a busy machine.
            The machine was in use by a session running builds while it ran, so the figure is one
            observation under a load nothing else will reproduce, and it is recorded for the row
            that reads nights on the schedule rather than taken as one of them (owes: The nightly
            wall clock at index size, measured from nights that ran on the schedule). It had
            already passed the proposed five minutes before reaching its last six stages, which is
            the first sign the proposal may be short and not yet evidence that it is.

### 5.7 - correction: a second run for one night added a second band set and a second profile   2026-09-11
Corrects:   the 3.3 and 3.4 entries above, which record the volume profile and the level bands
            written per as-of with upserts keyed on each band's low edge. A band's edges move whenever
            the prices under it do, so a second run for one as-of after a refetch adjusted the
            series kept the first run's rows beside the second's. Found in phase 5 and repaired
            under it, at the checkpoint the sign-off's repairs sit at.
Found:      by the phase 5 sign-off's builder, reading the by-hand night of 2026-09-10 the operator
            ran to recover that night's listing. Its corporate action step refetched seven names and
            moved their adjusted prices a hair against the scheduled night of the evening before.
            NVDA was left with two immediate support bands for one as-of and MOS with 39 profile
            bands where the profile has 20. NVDA's facts file then carried its immediate support
            facts twice, and the change detector stopped the night at step 13 on the repeated name,
            recorded as `changes` failed on the run log, which is the 5.6 correction working. The
            listing for 2026-09-10 was written before it stopped: 505 rows, 490 fired.
Repaired:   both builders delete the name's whole set for the as-of and write it again inside one
            transaction. A night writes a whole set, so a whole set is what it replaces, which is
            the retention's rule applied to the set being written. Both already own the delete on
            their table, and SCHEMA's retention paragraph states it.
Guarded:    for each, a set built, one name's year moved by one per cent as a refetch would move it,
            the chain the night runs rebuilt over it for the same as-of, and no row of the first set
            left standing: twenty profile bands, and at most one immediate band on each side for
            every name and as-of in the store.
            The level test failed on its first run with four of the first set's edges standing, and
            that was the test: the swing finder had not been run again over the moved series, so the
            bands it anchors kept their old edges. The night runs swings before levels and the test
            now does too.
Mutated:    the rule, stated before the sweep: reinstate the defect in each builder, being the set
            written without removing the one before it. Two mutations, two runs, in a worktree under
            the session scratchpad outside the repository, reverted, and the worktree removed. Each
            turned its own builder's test red and left the other 512 green. None of the properties
            added went unmutated.
Tests:      513, from 511. `tools/ci.ps1` green end to end, all 6 steps, 0 warnings, migrations 0 to
            18, exit 0. `tools/verify-phase.ps1` green at 243 claims, 169 PASS, 0 FAIL, 74 out of
            scope, 0 unexamined. No claim moved. Windows on this machine; the matrix carries macOS and
            the Linux case-sensitivity job.

### 5.7 - correction: one facts file that could not be compared stopped every name   2026-09-11
Corrects:   the 5.3 entry above records the change detector comparing each name's facts file with the
            one before it. One file that could not be read as a set of named facts stopped the stage
            for every name, and the assembler would write such a file without refusing it. Found in
            phase 5 and repaired under it.
Found:      by the phase 5 sign-off's builder, reading the by-hand night of 2026-09-10. NVDA's facts
            file named its immediate support facts twice, from the two band sets the correction above
            repairs, and `FactsFile.ByName` built a dictionary with `ToDictionary`, which threw "An
            item with the same key has already been added" and stopped the night at step 13 for all
            505 names. Reading the same method found the second shape before it fired: the retention
            empties a past night's payload, `Changed` only guarded a missing previous file and not an
            empty one, and the pairs the detector reads include every name's newest row, so the first
            name to leave the index with two stored nights would have stopped every night after it on
            a blank payload.
            The scheduled night of 2026-09-11 would have compared its facts against NVDA's file of
            2026-09-10 and stopped at the same step.
Repaired:   `FactsFile.TryChanged` answers whether a comparison can be made, and a file that is empty
            or names a fact twice is that name's own unknown: its `material_changes` is null, where an
            empty list would read as nothing changed, and the run log names it with the reason. The
            stage carries on for every other name. `ByName` refuses a repeated name by name rather
            than through a framework message. The assembler refuses to write a file naming a fact
            twice, before anything is stored, because the stage that left the rows is the defect.
            SCHEMA's column note states what null means.
Guarded:    a previous file naming a fact twice and a previous file emptied by the retention, each
            recorded as null and named with its reason while the stage writes every name and the run
            log says two were not compared; a name with nothing before it still carries an empty
            list; and the assembler refusing a file that would name a fact twice, with nothing written.
            One assertion written for this test compared a value with itself and could not fail, and
            it was removed before the run rather than counted.
Mutated:    the rule, stated before the sweep: reinstate each defect, being an uncomparable file
            stopping the stage and a file naming a fact twice written. Two mutations, two runs, in a
            worktree under the session scratchpad outside the repository, reverted, and the worktree
            removed. Each turned its own test red and left the other 514 green. None of the
            properties added went unmutated.
Tests:      515, from 513. `tools/ci.ps1` green end to end, all 6 steps, 0 warnings, migrations 0 to
            18, exit 0. `tools/verify-phase.ps1` green at 243 claims, 169 PASS, 0 FAIL, 74 out of
            scope, 0 unexamined. No claim moved. Windows on this machine; the matrix carries macOS and
            the Linux case-sensitivity job.

### 5.7 - correction: an announced index change, a joiner's backfill and a day with no session   2026-09-11
Corrects:   the entries above that record the fetch, the backfill and the listed stages reading the
            index as the rows with no leave date, the 5.7 entry above that records the missed session
            read off the newest bar in the store, and the schedule's entries, which registered a
            daily night against an exchange that trades on weekdays. Three defects found by the phase
            5 sign-off reviewer and repaired under 5.7, at the checkpoint the sign-off's repairs sit
            at.
Found:      by the phase 5 sign-off reviewer. The membership feed carried the rebalance effective
            2026-09-21 by 2026-09-10, and ten sites read a member as `left IS NULL`: in the operator's
            store BLDR, TAP and TTD, still in the index until 2026-09-21, held no bar, no ladder row and
            no listing, and BE, ILMN, P and PSTG were listed before they joined. The catch-up window
            opened at the newest bar in the whole store, and a joiner's backfill writes its year
            through tonight, so a joiner backfilled on the night after a night that did not run hid the
            missed session from every other name; the reviewer reproduced it. And a night on a Saturday
            or a holiday asked the provider for a session that does not exist and exited 1 at the
            fetch, so the scheduler would have recorded a failure every weekend.
Repaired:   a name is a member on a session when its join date is on or before it, or unknown, and
            its leave date is after it or absent. The listing, ladder, calendar, news pulse and close
            stages and the read surface's universe and stale names read that. The fetch, the backfill
            and the corporate action check read every name that has not left by the session, which
            includes an announced joiner, so a joiner is backfilled on the announcement and stored
            every night until it joins with its year whole. The catch-up opens at the newest session a
            bulk file stored, falling back to every bar only on a store no bulk file has reached. A
            night whose session the closure table says was not traded writes one run log row under the
            closing stage with an outcome of its own, fetches nothing and exits 0; the run page's
            failed region does not count it. Two decisions record the choices a later session could
            reverse unseen, section 18 gains two rows, SCHEMA's `left` note and member-tonight paragraph
            are corrected, and the runbook registers weekdays and says how to move a daily task in
            place. The operator's task was moved to weekdays at 23:30 UTC on 2026-09-11 before this
            entry, read back as `DaysOfWeek` 62.
Guarded:    a rebalance over the fixture, one member leaving and one joining the session after the
            night: before it the leaver is backfilled, stored, laddered and listed and the joiner is
            backfilled and stored and neither laddered nor listed, and on it each goes the other way.
            A joiner backfilled through tonight over a missed night, with every member holding the
            missed session afterwards. A Saturday night and a Labor Day night, each making no request,
            storing nothing, writing one no-session row and exiting 0, with the run page's word asserted
            to agree with the worker's. And a scan holding every membership read in the shipped source
            to one of the two forms, with both sets stated: four that store or answer a past date and
            seven that ask the join date too.
Mutated:    the rule, stated before the sweep: reinstate each defect at one site, being the listing
            reading `left IS NULL`, the backfill reading `left IS NULL`, the catch-up opening at the
            newest bar of any source, and the night going on to the fetch on a day with no session.
            Four mutations, four runs, in a worktree under the session scratchpad outside the
            repository, reverted, and the worktree removed. The first two each turned the rebalance
            test and the scan red, the third the joiner's catch-up test, and the fourth both cases
            of the closed-day test, each leaving the other 518 or 519 green. None of the properties
            added went unmutated.
Tests:      520, from 515. `tools/ci.ps1` green end to end, all 6 steps, 0 warnings, migrations 0 to
            18, exit 0. `tools/verify-phase.ps1` green at 245 claims, 171 PASS, 0 FAIL, 74 out of
            scope, 0 unexamined, 178 placements reconciled. The two new section 18 rows are the two
            claims added, and the fetch step's text moved under its own placement. Windows on this
            machine; the matrix carries macOS and the Linux case-sensitivity job.

### 5.7 - correction: a re-run could not replace a facts file, and said it had   2026-09-11
Corrects:   the 5.3 entry above, which records the facts assembler inserting with the conflict ignored
            and calls that keeping the night idempotent, and the 5.7 correction above that made an
            uncomparable file the name's own unknown, whose detector could replace a list it had made
            with null. Found in phase 5 and repaired under it.
Found:      by the phase 5 sign-off's builder reading the recovered store, and by the sign-off reviewer
            independently. The 2026-09-10 files in the operator's store come from the 12:39Z attempt,
            which ran over band sets a refetch had doubled and stopped at the change detector; the run
            at 13:07Z computed clean files after the band repair and every insert was dropped on the
            conflict, while the run log said 505 files written. NVDA's file still names three facts
            twice, 211.9375 and 211.9476 for one edge where the store holds one band at 211.9375. The
            reviewer also found that a second run of the detector over a night whose previous file the
            retention had emptied replaced the list it had made with null, that nothing refused a file
            naming a fact twice at the point it is written, and that "not compared" is on the page only
            as hover text; the last is the run page's and is carried to the correction that repairs
            that page.
Repaired:   the assembler compares tonight's file's hash with the stored one and, where they differ,
            deletes that row and inserts tonight's; a re-run over an unchanged store writes nothing. It
            counts what the statements did: files written, how many of them replaced a file that
            differed, and how many were unchanged. The detector keeps a list it made where the file it
            was made against has since been emptied, and counts it on the run log. `FactsFile.Serialise`
            refuses a name given twice, so no writer can produce such a file. SCHEMA gives the delete on
            `facts` to the assembler, a decision records the choice against the update a later session
            might reach for, and section 14's note says a second run's values are what the store holds.
            The existing uncomparable-file test now clears tonight's lists first, since a list an
            earlier run made is now kept where the file it was made against is emptied, and that test
            is about a row no comparison has reached.
Repair:     the store's 2026-09-10 files are repaired by running `tools/nightly.ps1 --session
            2026-09-10` once this is merged and before the night of 2026-09-11 runs, because the
            assembler writes the newest night the store holds and cannot recompute one another night
            has followed. What that run replaces is on its facts line.
Guarded:    a store in which one stored value the assembler reads has moved: one file written and
            replacing the one before it, the rest unchanged and counted so on the run log, the stored
            file tonight's with its own hash, the replaced row with no list until the detector writes
            one and the others keeping theirs. A second detector run over a night whose previous file
            the first run emptied, keeping the first run's list and naming nothing. A file naming a fact
            twice refused by `Serialise` with the name, the same facts under two names written. And
            every file the replay stores naming each fact once.
Mutated:    the rule, stated before the sweep: reinstate each defect, being a differing file left
            standing, every file counted as written, a list replaced with null when its comparison is
            gone, and a repeated name written. Four mutations, four runs, in a worktree under the
            session scratchpad outside the repository, reverted, and the worktree removed. The first
            two each turned the replacement test red, the third the second-run test and the fourth the
            write refusal, each leaving the other 522 green. None of the properties added went
            unmutated.
Tests:      523, from 520. `tools/ci.ps1` green end to end, all 6 steps, 0 warnings, migrations 0 to
            18, exit 0. `tools/verify-phase.ps1` green at 245 claims, 171 PASS, 0 FAIL, 74 out of
            scope, 0 unexamined, 178 placements reconciled. No claim moved. Windows on this machine;
            the matrix carries macOS and the Linux case-sensitivity job.

### 5.7 - correction: the run page opened on the last night that listed, and the run log lost what a night said   2026-09-11
Corrects:   the 5.6 entry above, which records the run page reading the newest night the listings
            hold, the operational header, and the stale-and-failed region; the phase 5 sign-off
            correction above that recorded a stopped step on the run log; and the fetch and action
            entries above for what their rows carry. Found in phase 5 and repaired under it.
Found:      by the phase 5 sign-off reviewer. The run page opened on the newest listing and kept that
            night's rows alone, so a scratch night stopped at the fetch on 2026-09-09 left the default
            page on 2026-09-08 saying no stage of this night failed, and the PASS for the stage a night
            stopped on was reached through a read with the date supplied rather than the page a person
            opens. A stage's own account of itself was a hover title, so the names a night could not
            compare were not readable on the page. A stop row recorded no requests. The fetch's counts
            of rows listed and not traded and of rows the reader passed over went to stdout alone, and
            the members a caught-up file carried nothing for were discarded. A refusal before the first
            step, such as a missing secrets file, left no row. A deadline passed inside the action step
            was caught per name, marked the name suspect, and was recorded under the calendar. And the
            duration spanned a night's failed run and its re-run.
Repaired:   the run page opens on the newest night the run log carries a stage for, less the read
            surface's own row and a night with no session, and falls back to the listings only on a log
            that holds no night; the choice lives in the read API so a test asks it exactly what the
            route asks. The operational header draws each stage's detail in its own cell. A stop row
            carries the requests the step made, read off the feeds. The fetch row's detail carries the
            not-traded and unreadable counts, tonight's missing members and each caught-up session's,
            each as a count and the first few names, and the fetch line counts the caught-up shortfall.
            A night refused before its first step writes a failed row under that step into a store that
            exists, and creates none. The action step lets the night's cancellation through, so the
            deadline stops the night there and marks no name. The duration is the run whose list the
            store holds. Section 15.10 states the route's default and the header's detail cell.
Guarded:    a night stopped at the fetch after a clean night: the listings still end the night before,
            the run page's night is the stopped one, its region draws the fetch, later read-api and
            no-session rows do not move it, and the stop row carries the one request made. A deadline
            passed inside a refetch, recorded under the action step as stopped with no name suspect,
            the deadline measured from a warm night on the same machine. A refusal written under the
            first step, and a refusal into a store that does not exist creating none. A caught-up file
            short of one member named on the fetch row and counted on the line, with tonight's shape
            and the passed-over counts beside it. The header's detail cells and no hover title. And a
            night run twice whose duration is the second run's alone.
Mutated:    the rule, stated before the sweep: reinstate each defect at one site, being the run page
            opening on the newest listing, the detail back in a hover, the stop row's requests back to
            none, the caught-up shortfall discarded, the refusal writing no row, the action step
            catching the cancellation, and the duration over every run that listed. Seven mutations,
            seven runs, in a worktree under the session scratchpad outside the repository, reverted,
            and the worktree removed. Each turned its own test red and left the other 527 green:
            the first and third the stopped-night page test, the second the header test, the fourth
            the caught-up shortfall test, the fifth the refusal test, the sixth the actions deadline
            test and the seventh the duration test. None of the properties added went unmutated.
Tests:      528, from 523. `tools/ci.ps1` green end to end, all 6 steps, 0 warnings, migrations 0 to
            18, exit 0. `tools/verify-phase.ps1` green at 245 claims, 171 PASS, 0 FAIL, 74 out of
            scope, 0 unexamined, 178 placements reconciled. No claim moved; two placements now name
            what they reach. Windows on this machine; the matrix carries macOS and the Linux
            case-sensitivity job.

### 5.7 - correction: a move pushed out of the top eight kept its rank, a suspect name was never retried, and a stale member was listed on its own last session   2026-09-11
Corrects:   the 5.2 entry above, which records the move annotator's fallen-out drop; the 1.6 and 1.8
            entries above, which record the suspect mark; and the 5.4 correction above that dated a
            listing with no stored bar by the session, which left a member with an old bar dated by it.
            Three defects found by the phase 5 sign-off reviewer and repaired under 5.7.
Found:      by the phase 5 sign-off reviewer. The fallen-out drop took rows ranked below tonight's count,
            so a move pushed out of a name's top eight stood at rank eight beside tonight's eighth: nine
            names in the operator's store held two moves ranked eighth. A name whose corporate action
            refetch failed was marked suspect and nothing read the mark, so it was never refetched
            again unless another action landed on it; no live case today. And a member the day's file
            carried nothing for was listed on its own last session, so EQR and PSTG had no row for
            2026-09-10 and one row each dated 2026-08-17 and 2026-04-16 that fired two and three reasons
            on those sessions' prices, and `listings-coverage` could not reach the case.
Repaired:   the fallen-out drop deletes every session for the name outside tonight's set, named by
            session rather than rank. The action check reads series state and refetches every suspect
            name that has not left the index, with or without an action of its own, until one refetch
            succeeds, counting the retries on its row; the catalogue row, the matrix cell and section 18
            say so. The shortlist builder lists a member whose newest bar is older than the newest
            session any name holds on that session, with nothing fired and a plan naming its last
            stored session, the newest session read off the store rather than the clock so a replay on
            a weekend does not make every name stale. The move retention test's constructed row moved to
            a name with no bars, because the fallen-out drop now reaches any row of a name with bars and
            left that test asserting a drop another statement performed.
Guarded:    a move made to outrank every stored one on a session outside the set: it is in, a stored one
            went, the name holds eight, and every rank of every name is held once. A suspect name
            refetched on the next night with no action for anyone and set ok, the retry on the row, and
            the night after asking nothing for it. And a member with no bar for the night listed on the
            night with nothing fired and its plan naming its last session, every member holding a row on
            the night and none dated by the stale session.
Mutated:    the rule, stated before the sweep: reinstate each defect, being the fallen-out drop keyed
            on rank, the suspect names not read, and a stale member dated by its own last session and
            evaluated over it. Three mutations, three runs, in a worktree under the session scratchpad
            outside the repository, reverted, and the worktree removed. Each turned its own test red
            and left the other 530 green: the move test, the suspect retry test and the stale-member
            coverage test. None of the properties added went unmutated.
Tests:      531, from 528. `tools/ci.ps1` green end to end, all 6 steps, 0 warnings, migrations 0 to
            18, exit 0. `tools/verify-phase.ps1` green at 245 claims, 171 PASS, 0 FAIL, 74 out of
            scope, 0 unexamined, 178 placements reconciled. No claim moved. Windows on this machine;
            the matrix carries macOS and the Linux case-sensitivity job.

### 5.7 - correction: two readers blind to forms that ship, an allowance claim its check did not reach, and a closure table with no end in sight   2026-09-11
Corrects:   the 5.7 correction above titled "the sign-off repair's own gaps, found by reviewing it",
            whose entry says the thirty-two loose holes it wrapped were "eleven shipped and twenty-one
            in the suite". They were eighteen shipped and fourteen in the suite: the commit, 236992b,
            adds `FormattableString.Invariant(` on eighteen shipped lines, none of them a comment, and
            on sixteen suite lines, of which one is a comment and one a probe string inside the check's
            own test. The phase 5 sign-off reviewer found the figure and this count confirms it. Also
            the 5.7 entries above that widened `clock-usage` and `price-storage-form`, the 5.7 entry
            that added the closure table, and the 5.7 entry that superseded the schedule decision.
Found:      by the phase 5 sign-off reviewer. `clock-usage` did not read a raw interpolated literal, and
            passed any literal with `InvariantCulture,` before it, which includes
            `string.Format(CultureInfo.InvariantCulture, $"...")`, where the literal is formatted against
            the current culture before `string.Format` receives the provider. `price-storage-form` did
            not read `(double?)`, `(decimal?)`, `Convert.ToDouble(` or `Convert.ToDecimal(`, and
            `Convert.ToDouble(value)` in the shortlist builder shipped in no stated set. The stale-and-
            failed region's PASS, placed by `nightly-run`, said an allowance stop reaches the region,
            and only `nightly-cost` made one. The closure table's dates outside the captured year had
            no test, and it ends 2027-12-31 with nothing saying so. The schedule decision did not name
            the one it superseded.
Repaired:   `clock-usage` reads raw literals, with holes opening by as many braces as the literal has
            dollar signs, passes a literal only where the call formatting it takes the provider, named
            whole, and reads a composite format with a date format and no provider as a fourth form.
            `price-storage-form` reads the nullable casts and the two `Convert` calls, and its stated
            set gains the two that ship: `(decimal?)null` in the ladder builder, which crosses nothing,
            and the volume average read back as the double it is. `nightly-run` asserts an allowance
            stop through the run page's own night and region. The closure table is checked closure by
            closure against the exchange's published rules for every year it covers, with the one
            special closure stated. The closing line names the table's end once a night is within 90
            days of it, and an operating obligation carries the extension, read on the operational
            header, which 5.6 builds and whose paragraph now cites six. The schedule decision names what
            it superseded. The two roster rows in CLAUDE.md say what the readers now read.
Guarded:    probes for each new form in both directions: a provider given to `string.Format` after the
            literal flagged, a raw literal read with one dollar sign and with two, a raw literal's
            single brace under two dollar signs left alone, the composite form flagged and its pinned
            twin passed, a number left alone; the nullable cast and both conversions producing sites
            and a conversion to an integer producing none. An allowance stop drawn in the region against
            the first step. Thirty-one closures derived from the rules equal to the table. The closing
            line empty one day outside the window and present on its first day, written on a close run
            inside it, and the obligation row stating the window the code uses.
Mutated:    the rule, stated before the sweep: reinstate each defect, being the pinned forms matched on
            any `InvariantCulture,`, raw literals unread, the composite reader reading nothing, the
            nullable cast unread, the conversion calls unread, the allowance stop recording no row, one
            closure moved by a day, and the reminder never firing. Eight mutations, eight runs, in a
            worktree under the session scratchpad outside the repository, reverted, and the worktree
            removed. Each turned red and left the rest green: the first two the interpolation probe
            test, the third the composite probe test, the fourth and fifth both the stated set and the
            probe test of `price-storage-form`, the sixth the allowance tests of both `nightly-run` and
            `nightly-cost`, the seventh the rules-derived closure test and the eighth the closing line
            test. None of the properties added went unmutated.
Tests:      536, from 531. `tools/ci.ps1` green end to end, all 6 steps, 0 warnings, migrations 0 to
            18, exit 0. `tools/verify-phase.ps1` green at 245 claims, 171 PASS, 0 FAIL, 74 out of
            scope, 0 unexamined, 178 placements reconciled. No claim moved. Carried obligations 55
            rows, the new one operating. Windows on this machine; the matrix carries macOS and the
            Linux case-sensitivity job.

### 5.7 - correction: the facts file rewrote past nights, the pages read today's index, and a stale member kept an old ladder row   2026-09-11
Corrects:   the correction above titled "a re-run could not replace a facts file, and said it had",
            whose replacement reached past nights; the correction above titled "an announced index
            change, a joiner's backfill and a day with no session", whose read surface bound the index
            to the day a page is opened; and the correction above that listed a stale member on the
            night, which left the ladder with the same shape. Three findings of the fourth phase 5
            sign-off review, the ones that put a silently wrong result into the store or onto a page.
            The rest of that review is carried at 6.0 in four named rows, by the stopping rule.
Found:      by the fourth phase 5 sign-off review. The facts assembler read every name with any bar
            and keyed the file on the name's own last bar, so for EQR, whose file last carried it on
            2026-08-17, and PSTG, on 2026-04-16, each night recomputed a past night's file over windows
            the store had moved and replaced it where the hash differed, returning an emptied payload
            whole and resetting its list. The by-hand night of 2026-09-11 for 2026-09-10 left both
            files' hashes unchanged against the operator's backup; the next night moves the retention
            boundary. The change detector compared each name's newest file, so a name with no file
            tonight had its emptied last file compared every night and its list replaced with null. The
            read surface's universe and stale names bound membership to today, so every past-night page
            read today's index and, from 2026-09-21 before that night, 2026-09-18's list would have
            drawn the first-ranked leaver's plan at a close of zero. And the ladder builder dated a
            stale member's row by its own last bar, so EQR and PSTG held one ladder row each.
Repaired:   the assembler writes files only for the names holding a bar on the newest session the
            store has, so a past night's file is left as that night wrote it. The change detector
            compares tonight's files only. The universe and stale names read the index on the night
            the page shows, and the name, tonight, universe and run routes pass that night. The ladder
            builder writes a stale member's row on the night, not classified, with a plan naming its
            last session.
Guarded:    a name with no bar tonight gets no file and its past file keeps its payload, hash and list;
            a replacement takes tonight's file and leaves an earlier night's; a name with no file
            tonight keeps the list its own night made beside an emptied file; a leaver after the night
            shown is in that night's universe with its close and a later joiner is not, where today's
            read swaps them; and a stale member's ladder row is the night's, not classified, with every
            member holding a row on the night.
Mutated:    the rule, stated before the sweep: reinstate each defect, being the assembler reading
            every name with a bar, the replacement keyed on the name alone, which is the fourth
            review's surviving mutation, the detector reading each name's newest file, the read
            surface bound to today, and the ladder dating a stale member by its own last bar. Five
            mutations, five runs, in a worktree under the session scratchpad outside the repository,
            reverted, and the worktree removed. Each turned its own test red and left the other 540
            green, the second being the replacement test this entry adds for the mutation the fourth
            review found surviving. None of the properties added went unmutated.
Tests:      541, from 536. `tools/ci.ps1` green end to end, all 6 steps, 0 warnings, migrations 0 to
            18, exit 0. `tools/verify-phase.ps1` green at 245 claims, 171 PASS, 0 FAIL, 74 out of
            scope, 0 unexamined, 178 placements reconciled. No claim moved. Carried obligations 59
            rows, four new at 6.0 and cited back by 6.0's text. Windows on this machine; the matrix
            carries macOS and the Linux case-sensitivity job.

### 5.7 - correction: a row's parts were the reader's rather than the document's   2026-09-12
Corrects:   the 5.0, 5.2, 5.4 and 5.6 entries above, whose section 15 rows were placed PASS whole
            while the reader chose which of their parts carried a verdict, and the entries above that
            decomposed four rows by hand at 3.1, 5.0 and the phase 5 sign-off. The blocking finding of
            the fifth phase 5 sign-off review, which committed nothing.
Found:      by the fifth phase 5 sign-off review. `Scope.Elements` named each decomposed row's parts,
            so a clause it left out had no verdict of its own while its row read PASS: 15.7's list
            states a day change, a trend state in a word and the distance row mark, the page draws
            name, close and reasons, and the row passed. The same shape hid the harness verdict in
            the night header, the level summary and the selection on the selected name, paging and
            sessions until earnings on the universe table, the twelve-month picture on the name page,
            and the break-even on the run page's reason record. That is an unexamined claim wearing a
            verdict, and `architecture-conformance`'s population-from-the-document promise broken one
            level below the row.
Repaired:   the parts are read off the row. An enumeration is a run of three or more comma-separated
            items, which is how these rows list what a region holds, and every part of a row any
            verdict passes must be a claim with a verdict of its own. A two-item list joined by "and"
            is outside what the reader reaches and is stated as its scope, which is why the selected
            name is decomposed by hand; the rationale items a run picks up are a stated set of eleven,
            each argued, the way `price-storage-form` states its cast sites. Fifteen rows are
            decomposed into seventy-two parts, fifty-seven of them drawn and passed naming the check
            that reads the markup back, and fifteen out of scope: nine at a new checkpoint 5.8, which
            draws them and carries the obligation row filed for them, two at 6.5, being the research
            pass's prose and spend, and four at 7.5 and 6.5, which the plan already owed.
Guarded:    the reader over constructed text, taking the list's own sentence apart into its six parts,
            dropping a leading connective, refusing a two-item list and dropping a stated rationale
            item; and the coverage assertion over the document, which names every part of a passing
            row that has no verdict.
Mutated:    the rule, stated before the sweep: reinstate the finding and each guard the repair rests
            on, being a part dropped from a row's list, a deferred part marked PASS with no check
            reaching it, the reader returning nothing, and a drawn part's reach taken off the check
            that names it. Four mutations, four runs, in a worktree under the session scratchpad
            outside the repository, reverted, and the worktree removed. The first turned the coverage
            assertion, the element reader and the due-point count red; the second and the fourth turned
            the reconciliation red in fifteen places each; the third turned the coverage assertion and
            the reader's own proof red. None of the properties added went unmutated.
Tests:      543, from 541. `tools/ci.ps1` green end to end, all 6 steps, 0 warnings, migrations 0 to
            18, exit 0. `tools/verify-phase.ps1` green at 298 claims, 211 PASS, 0 FAIL, 87 out of
            scope, 0 unexamined, 218 placements reconciled. The claims rise by 53 and the out-of-scope
            by 13 because a row's parts are claims now: 72 parts over 15 rows, 57 of them passed and
            15 owed, nine of those at the new 5.8. Windows on this machine; the matrix carries macOS
            and the Linux case-sensitivity job.

### 5.8 - the screens' stated parts   2026-09-12
Built:      the nine parts section 15 states and no page drew, and the tenth claim the ninth makes on
            a second row. Tonight's list carries a day change, a trend state in a word and the
            distance row mark beside the name, the close and the reasons. The night header carries the
            harness verdict beside the counts it already drew. The selected name carries the level
            summary beside its plan column, and it is the row the reader picked rather than the first:
            every row is a link that names itself in the hash, and `#/night/<date>?name=<TICKER>` is a
            selected view another machine opens the same way. The universe table is paged and carries
            the sessions until each name's next dated event. The name page's how-it-got-here carries
            the twelve-month picture above the table of moves. And the app's own row names selection
            beside routing and filters, which is 15.4's statement of the same part.
            (owes: The screens' parts stated in section 15 and not drawn)
Why:        the fifth phase 5 sign-off review found that `Scope.Elements` named each row's parts, so a
            clause the reader left out had no verdict of its own while its row read PASS. The repair
            at 5.7 made the parts come from the row, which turned these nine into claims with no check
            reaching them, and out of scope at a checkpoint that did not exist yet. This is that
            checkpoint. Phase 5's own done condition at 5.7 asks that every phase 5 claim be PASS, so
            the phase could not sign off while ten of its claims sat here.
Read:       every value drawn is stored or derived in the seam `UniverseScreen` already names. Two
            reads are new and both hand back columns: every name's next dated event on or after a
            date, in one query rather than five hundred, and every name's close on the newest session
            it holds before the night, which is the other half of a day change. Two figures are
            derived and both in the projection: the day change from two closes, through
            `Statistic.FromPrice` and stated in `price-storage-form`'s set of crossing helpers, and the
            sessions until an event, counted off `ExchangeClosures` rather than off stored bars,
            because the event is ahead of the night and no bar exists for a session that has not
            happened. The read surface computes nothing and the page computes nothing, which is the
            seam both of those rules leave open.
            (see: A screen reads and renders, and computes nothing) (see: Code owns every number)
Absences:   each new part states its absence rather than drawing a zero. No earlier close, no ladder
            row and no bands each say so on the row. A name with no dated event and a name whose event
            is past the end of the exchange closure table are two different absences and are stated as
            two: the second is the table ending, not a name without earnings.
            (owes: The exchange closure table extended before the nights reach its end)
Guarded:    seven assertions in `read-surface`, each reading the part back off the markup rather than
            off the model behind it, which is what this checkpoint owed. The day change is checked
            against the two closes read out of the stored bars rather than against the projection that
            drew it; the trend state against the ladder's own label; the distance marks counted one per
            drawn row and each carrying its own name; the four harness counts drawn separately, with
            the header asserted not to carry their sum; the selection asserted on a row that is not the
            first, with the fallbacks for no pick and for a name not on the list; the pages asserted to
            partition the rows in order with every link keeping the filters it was drawn under; the
            sessions count asserted over a Friday to a Monday, a day to itself, and the week holding
            Thanksgiving, and against the per-name read the fact strip uses; and the picture asserted
            to be the level chart mark over the year to the newest stored session, above the table and
            with no bands.
Mutated:    the rule, stated before the sweep: take one part from each surface the checkpoint touched,
            and take the one whose absence a count would not notice. Four mutations, four runs, in a
            worktree under the session scratchpad outside the repository, reverted, and the worktree
            removed. The selected-name region fixed back at the first row; the paging slice taking
            from the whole table rather than from the filtered rows; the sessions count subtracting
            dates instead of reading the calendar; and the twelve-month window measured from the
            machine clock rather than from the newest stored session. The first, the third and the
            fourth each turned their own assertion red on the first sweep. The second survived it, and
            that is the finding this entry carries: the route filtered and then cut, and nothing
            asserted the order between the two steps, so a page taken from the whole table and
            filtered afterwards drew a short page of the wrong names with no count on the screen
            contradicting it. The remedy is the two steps made one call the route cannot get wrong,
            `UniverseScreen.Rows`, with the order asserted on it: page two of a filter matching half
            the names is the second fifty of that half, and it is not page two of the whole table.
            Mutated again there and it goes red. The fourth also went red on `clock-usage` alone at
            first, which is a scan finding a pattern rather than a test exercising the path: the
            committed fixture's newest session is days from the machine's, so both windows hold the
            same bars. Asserted now over a constructed series that ended in 2020, where a window taken
            from the clock is empty.
Tests:      550, from 543. `tools/ci.ps1` green end to end, all 6 steps, 0 warnings, migrations 0 to
            18, exit 0. `tools/verify-phase.ps1` green at 298 claims, 221 PASS, 0 FAIL, 77 out of
            scope, 0 unexamined, 228 placements reconciled against floor 34. The ten claims this
            checkpoint owed moved from out of scope to PASS and no claim moved the other way, so no
            out-of-scope claim now names a checkpoint inside phase 5: the 77 are 58 at phase 6 and 19
            at phase 7. Windows on this machine; the matrix carries macOS and the Linux
            case-sensitivity job.
Carried:    nothing new. The closure table's end is the obligation the sessions column now reads
            against, and it was already open.
Notes:      the nine parts are ten claims, because the selection is stated on 15.7's selected-name row
            and again on 15.4's app row, and a claim is a row's part rather than a thing on a screen.
            `BUILD_PLAN.md` says which, so the count is legible rather than arithmetic a reader has to
            do. ARCHITECTURE is unchanged: no row was edited to match a page, at 5.7 or here.

### 5.8 - correction: the day change subtracted two different sessions   2026-09-12
Corrects:   the 5.8 entry above records tonight's list carrying a day change. It carried a number
            made of two closes from different sessions, and on a name with no bar on the night it
            carried exactly 0.00, which the same entry says the surface never draws.
Found:      by the sixth phase 5 sign-off review, through the read path rather than by reading the
            SQL. `ReadApi`'s universe query takes each name's close from its newest bar with no
            session bound, only the membership predicate uses the night, and 5.8 subtracted the
            night-bounded previous close from it. On 2026-09-01 over the fixture, the route drew a
            close of 319.97, which is AAPL's bar on 09-04, against a previous close of 316.85, which
            is correct for the night, and reported +0.98 where the night's own closes give +2.61. The
            second shape fires on the newest night: both legs of a stale member resolve to its last
            stored bar, so NFLX made stale on 2026-09-04 drew 0.00 from 81.72 against 81.72.
Why nothing saw it: the committed fixture holds listings on one night, so every assertion over
            tonight's list ran on the one night where the two closes coincide for a name that is not
            stale. 5.8's own absence assertion ran over a hand-built `ListingCell`, which carries
            whatever the test puts in it and proves the renderer rather than the figures reaching it.
            The same commit made every row a link carrying its night, so past-night views became
            shareable in the commit that made them wrong.
Repaired:   one read for the pair. `ClosesToTheNightAsync` hands back each name's two newest stored
            sessions at or before the night, newest first, and both the close cell and the change are
            read from it. A change is drawn only where the newer of the two is the night itself, so a
            name with no bar on the night has no close and no change rather than a figure about a day
            the page is not showing. The universe row's close is untouched and the universe screen
            still reads it; a past night's bands and plan are the same shape and stay carried.
            (owes: The phase 5 sign-off's remaining store-shape findings ruled or fixed)
            The two absences are stated as two, a name with no bar on the session and a name whose
            first stored session is the night, because drawn alike they read as one thing.
Guarded:    over the read path, which is what the 5.8 assertion was not. A past night is constructed
            by listing the same names again on an earlier session the store already holds bars for,
            since nothing else in the suite reaches a second night, and every row's close is asserted
            to be that night's and its change to be the one that night's closes make, with the figure
            the defect drew asserted absent. The count of names whose newest close differs from the
            night's is stated in advance and required to be at least one, because a sweep where the
            two readings coincide proves nothing. A stale member is constructed by removing its bar
            on the night, and its row is asserted to carry no close, no change, the no-bar sentence,
            and neither 0.00 nor 0.00%.
Mutated:    the rule, stated before the sweep: reinstate the finding and each guard the repair rests
            on. Four mutations, four runs, in a worktree under the session scratchpad outside the
            repository, reverted, and the worktree removed. The close taken from the universe row as
            5.8 took it; the change drawn without the night guard; the pair read unbounded by the
            night; and the two absences collapsed into one sentence. The first and the fourth turned
            two tests red each, the second and the third one each. None of the properties added went
            unmutated.
Notes:      `TonightScreen.DayChange` leaves `price-storage-form`'s stated set of crossing helpers,
            and the reason is the signature rather than the arithmetic: it took two decimals and
            returned a double, and it takes the name's own sessions now, so the two closes it
            subtracts never appear in its signature. It still crosses once, through
            `Statistic.FromPrice`, which is in that set and is the boundary. The check's cast half is
            what covers the inline form, and the roster row already states that limit.
Tests:      552, from 550. `tools/ci.ps1` green end to end, all 6 steps, 0 warnings, migrations 0 to
            18, exit 0. `tools/verify-phase.ps1` green at 298 claims, 221 PASS, 0 FAIL, 77 out of
            scope, 0 unexamined, 228 placements reconciled. No claim moved: the repair is to what a
            drawn part carries rather than to which parts are drawn. Windows on this machine; the
            matrix carries macOS and the Linux case-sensitivity job.

### 5.8 - correction: the entry's out-of-scope split named the wrong phases   2026-09-12
Corrects:   the 5.8 entry above states the 77 out-of-scope claims as "58 at phase 6 and 19 at phase
            7". Both surfaces the phase report writes say 61 and 16.
Found:      by the sixth phase 5 sign-off review. The figures came from a sweep of the report's notes
            that took the last checkpoint each note names, and a note naming two, being "6.5 and
            7.5", was counted at the later one. The corpus number is the report's.
Repaired:   nothing in code. A record is corrected by a new dated entry rather than by editing the
            old one, and this is that entry. The split is 61 at phase 6 and 16 at phase 7.
Notes:      `stated-counts` exempts record entries as dated measurements, which is why the suite was
            green over a figure that was wrong. That exemption is right and this is the cost of it:
            a figure in a record is checked by a reader and by nothing else. The figure this
            checkpoint turns on, that no out-of-scope claim names a checkpoint inside phase 5, is
            unaffected and the review reproduced it.
