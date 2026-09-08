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
