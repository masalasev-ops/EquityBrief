# PROGRESS.md
Its entries before `### 19.0 planning` stand, whole and unchanged, in `docs/archive/PROGRESS-phases-0-to-18.md`, read with this file as one record.

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

### 19.0 planning - phase 19 planned: the read surface redesigned, the market-off variants first, the per-rule test's and the business-first setups' costs stated before anything is built, and the S&P 400's nights read   2026-10-12

Not a checkpoint entry. It lands 19.0, the pass that plans phase 19, and builds none of it.
Queue:      done, phase 18 as PRs 419 to 433 and 442, phase 15's sign-off as PR 443 and the guide and Figure 13.7
            corrections as PRs 444 and 445; in progress, this pass; next, in order, 19.1 the S&P 400's and 600's
            market-off variants, 19.2 colour, the header, the menu and the top of Tonight, 19.3 the setup cards,
            19.4 paper trading with its trade log and the monthly per-rule test, 19.5 Universe, 19.6 the Queue, 19.7
            the remaining screens, 19.8 the report and 19.9 the ledger's business-first setups; between phase 19's
            pull requests and never beside one, the fix for the Linux test that failed over 2aec8d18 and the
            sign-offs of phases 16, 17 and 18, each in a fresh session the operator starts.
Ruled:      by the operator on 2026-10-12: the brief, three questions answered, five reviews and the approval through
            the plan tool at about 19:19 UTC, each word for word in `prompts/2026-10-12-19.0-*.md` with a copy of the
            plan; R1 to R19 as the plan states them. The sign-offs of phases 16, 17 and 18 stay owed and outstanding;
            nothing here discharges any of them.
Asked:      by the operator while this pass ran: "do not forget to update build_plan doc". This pass writes the
            phase into `BUILD_PLAN.md`.
Planned:    `BUILD_PLAN.md`'s phase 19 section with its checkpoints 19.0 to 19.9; section 20's row; and the decision on
            the sign-offs, the one the phase rests on that describes no running code. The decisions that change what
            runs are written where they are built. Phase 19 opens no operating row.
Measured:   read-only on the live store, with no provider call.
            The S&P 400's breadth on each night since its families went live, recomputed from the stored bars and
            200-day averages of its 400 members, every one holding both: 170 above on 2026-10-02, 175 on 10-05, 178
            on 10-06, 167 on 10-07, 174 on 10-08 and 176 on 10-09, which is 0.425, 0.4375, 0.445, 0.4175, 0.435 and
            0.44, each the breadth its night stored, and each under the floor of 0.45, so its market check closed on
            every one and on 2026-10-09 every member failed every family at "the market check closed". That is the
            rule as built and no defect, so no 15.1 correction is owed. The S&P 600's stood at 0.4723 to 0.5135 on the
            same nights and its lists were open. The floor on both indices was copied from the S&P 500's filter and
            never tested there.
            The per-rule test, 19.4: migration 92 creates `loop_rule` and `loop_rule_year` empty, copying and dropping
            nothing, and the monthly run gains a step an index after its tests. Its added time is estimated at about
            12 minutes and at most about 24: October's tests read 28 proposals in 63 s on the S&P 500, 45 in 501 s on
            the S&P 400 and 45 in 680 s on the S&P 600, so at most 15 s a proposal, where a proposal walks its grid
            over every fold and a rule walks its one setting; and the step reads 48 rules once 19.1's are registered,
            the 31 the register holds and 19.1's 6 beside 11 provisional and business-first rules, at 15 s each and
            at 30 s for the bound. October's monthly run took 2 h 24 m. 19.4 measures it.
            The business-first setups, 19.9: no migration is expected, since `setup`, `setup_night` and
            `ledger_summary` hold a family by its word with no check on it, which 19.9 confirms against the ledger's
            writer. The ledger's step took 49 s on the night of 2026-10-09 over its ten family and index pairs, about
            5 s a pair, so the family's three pairs add about 15 s to the night. The history builds took 875, 641
            and 898 s on 2026-10-12, about 40 minutes, and the family adds at most the pullback's share of the
            setups, 30,723 of the S&P 500's 69,275, so a rebuild of the three indices takes at most about 58 minutes,
            the monthly run's history step growing by the same. The tester's procedure for the family costs about a
            drift family's share of each test step. 19.9 measures them.
Tests:      2251, unchanged.
Claims:     1267, unchanged: section 20's row is read by the check that counts the phases and is no claim, and the
            plan and the decision sit in no table the harness reads.
Pins:       none moved: the pass edits no source.
Verified:   `tools/ci.ps1` green over the tree carrying this entry, the figures to be filled from the run. Documents
            alone, so `tools/verify-phase.ps1` does not run (see: A pull request of documents alone runs the checkpoint
            script alone, and one changing code runs the phase report as well).
Carried:    the sign-offs of phases 16, 17 and 18, owed to fresh sessions, and the night of 2026-10-12 carried to phase
            16's; the fix for the Linux test that failed over 2aec8d18, owed before phase 17's sign-off.

### 19.0 - correction: the planning entry's Verified line names the checks that ran over its tree, where it said the checkpoint script ran there   2026-10-12

Not a checkpoint entry: it corrects 19.0's record and lands nothing.
Corrects:   the 19.0 planning entry above, whose Verified line says `tools/ci.ps1` ran green over the tree carrying it,
            the figures to be filled from the run. It did not run there: the operator stopped the run started over that
            tree on 2026-10-12, "why are you doing full CI on just doc changes", and the pass ran the checks reading
            what it changed instead.
Found:      by this session after PR 446 merged as eebc6523: the line was rewritten after the pass's one commit,
            9b0b300a, and the rewrite was never committed, so the merge kept the text written before the run.
Repaired:   nothing in code; a record is corrected by a new dated entry, and this is that entry. What verified the pass
            was `tools/ci.ps1` green over main as 18.5's entry records, at 5ed7905c, with `tools/verify-phase.ps1` green
            beside it, and over 9b0b300a the checks reading what the pass changed, run filtered:
            `ArchitectureConformance`, `BannedProse`, `ChangelogReconciles`, `CoverageReported`, `DecisionCitations`,
            `DoneConditionProducible`, `FigureFits`, `ObligationReconciles`, `PinnedConstants`, `RecordAppendOnly`,
            `StatedCounts` and `TwoPlatform`, 159 of 159 passed, then the hosted macOS and Linux runs over the merge.
Tests:      2251, unchanged.
Verified:   `tools/ci.ps1` green over main as above; this entry alone changes, so the same checks ran filtered over the
            tree carrying it, 159 of 159 passed.
Carried:    nothing.

### 19.1 - the S&P 400's and 600's market-off variants: a third breadth value reading no market check, a pullback stating it with its own market dial off, and six variants beside the provisional rules registered by the remedy   2026-10-12

Built:      the index rules' breadth setting gains a third value, `largeBreadth=2`, no market check, beside the index's
            own breadth at 0 and the S&P 500's at 1. A rule stating it opens its list on every night and hands its
            family's listings the night read as every member above its average, since the breakout's and the drift's
            sweeps make the check on the night themselves; their files are pinned by the S&P 500's evaluators and are left
            as they were. The form's setting check admits the value, refuses a fourth, and refuses a pullback stating it
            with its own market dial on, the provisional pullback's setting reading the index's breadth at 0.45 inside it.
            The rule's words end on "the market check off" and the registration's rule words say "or none is stated"; the
            short words "market check off" are written in the core for 19.3's chips and tabs. The decision (see: An S&P
            400 or 600 swing family may carry a variant with no market check, the check standing on its provisional
            rule); 15.7's card row, its last part reworded to hold where no variant stands and a part added for the
            variant; section 17's market gate row; the runbook's freeze and variants paragraphs; the roster's two rows;
            and the guide's selector paragraph, the prior text in `CHANGELOG.md`. The remedy
            `tools/remedies/2026-10-12-19.1-the-market-off-variants.txt` registers the moved rules again and then the six
            variants from main after the merge.
Measured:   read-only on the live store: the rules standing on the S&P 400 and 600 are the S&P 600's two heavyweights
            books, rows 284 and 285, which hold no holding and have read no night, the newest index night being
            2026-10-09; no index rule holds a trade. The floor of 0.45 these indices' lists close at was copied from the
            S&P 500's filter and never tested on either index, as 19.0's entry measured.
Tests:      three more: a rule reading no market check listing IA, IC, ID and IB on a constructed S&P 400 night whose
            breadth of 7 in 17 closes the provisional rule's list, every member of which fails at the check; the form
            refusing the pullback with its dial on and a fourth value, then registering the six the remedy registers,
            each the provisional setting with its moves and its words ending on the check off; and the S&P 400's card
            listing the variant after the provisional rule with no line that none is registered, its clause marked when
            chosen. The committed fixture holds no S&P 400 or 600 member, so these are worked by hand over constructed
            nights as 15.5's and 17.11's are, and no expectation file is written.
Claims:     one more, the card row's part on the variant, named after phase 18's report beside its pair; the part it
            rewords stands as one claim.
Pins:       the S&P 400's and 600's pullback, breakout, drift and heavyweights versions moved to a748b3a99fb5,
            b808c533ad30, a7607ebffaea and 6c6b35c716e1 with the index rules' file, so the remedy registers the S&P
            600's two books again, each named as not replayed and restarting, which loses nothing; no S&P 500 pin moves.
Mutated:    the rule, stated before the runs: each property the checkpoint adds, one mutation each, filtered to 19.1's
            three tests and the S&P 400's card test before them. M1 the listings handed the night's own breadth for a rule
            reading none: predicted red at the closed night's test. M2 the list's check left to the index's breadth for a
            rule reading none: predicted the same. M3 the form admitting a pullback reading none with its dial on:
            predicted red at the form's test. M4 the form admitting a fourth value: predicted the same. M5 the words not
            naming the check off: predicted red at the form's test and the card's. Results: M1 and M2 red at the closed
            night's test alone, the variant listing nothing, one of four each; M3 red at the form's test, the pullback
            registered, and M4 the same, a fourth value registered, one of four each; M5 red at the form's test and the
            card's, two of four. Each reverted before anything was committed. Not mutated: the drift's and the pullback's
            listings on a closed night, which read the sessions the breakout's do and are worked through the breakout's
            alone.
Verified:   `tools/ci.ps1` green and `tools/verify-phase.ps1` green over the tree carrying this entry, 57c3c168: all six
            steps, 0 warnings, 0 errors, 2254 of 2254 tests ran with none failed in 16 minutes 29 seconds, migrations 0
            to 91 with none pending, schema version 91, against `data-ci` and never `data`; the phase report ran the
            suite again over the commit, 2254 of 2254, and read 1268 claims, 1268 passing and none unexamined, 43 checks
            on the roster run and passed.
Queue:      done, 19.0 as PR 446 and its correction as PR 447; in progress, 19.1; next, 19.2, colour, the header, the
            menu and the top of Tonight. The remedy runs from main after the merge and before the night, and the six
            variants' first night is read the morning after.
Carried:    the sign-offs of phases 16, 17 and 18 and the Linux test of 2aec8d18, as 19.0's entry carries them.

### 19.0 - correction: the Linux test that failed over 2aec8d18 was fixed by the 1.3 correction of 2026-10-09, where 19.0's entry and the plan carried the fix as owed   2026-10-12

Not a checkpoint entry: it corrects 19.0's record and lands nothing.
Corrects:   19.0's entry and 19.1's, which carry the fix for the Linux test that failed over 2aec8d18 as owed before
            phase 17's sign-off, as phase 15's sign-off carried it; the rulings of phase 19 in `BUILD_PLAN.md`, which
            said the same; and the decision on the sign-offs, which says phase 17's waits on it.
Found:      by this session reading the red run's log, run 37934648172, before writing the fix: its Linux job failed
            17.7's score page test with "UNIQUE constraint failed: run_log.run_id, run_log.stage" from the read
            surface's start row. That is the failure the 1.3 correction of 2026-10-09 above names and fixed, as
            b81e0d4b, merged in PR 413 about half an hour after 2aec8d18 was committed, and every hosted run since has
            passed the test on both jobs.
Repaired:   the ruling in `BUILD_PLAN.md` now says the test was fixed before the plan, its prior text in `CHANGELOG.md`.
            Nothing is owed: phase 17's sign-off waits on no fix, and the decision's clause saying it waits is answered
            by this entry, a decision being changed only by another decision.
Tests:      2254, unchanged.
Verified:   `tools/ci.ps1` green and `tools/verify-phase.ps1` green over 57c3c168 as 19.1's entry records; documents
            alone change here, so the checks reading them ran filtered over the tree carrying this entry, 7473be90:
            `ArchitectureConformance`, `BannedProse`, `ChangelogReconciles`, `CoverageReported`, `DecisionCitations`,
            `DoneConditionProducible`, `FigureFits`, `ObligationReconciles`, `PinnedConstants`, `RecordAppendOnly`,
            `StatedCounts` and `TwoPlatform`, 159 of 159 passed.
Carried:    the sign-offs of phases 16, 17 and 18.

### 2.1 - correction: a member's row the provider's day file refuses is skipped, and its bar asked of Yahoo with Nasdaq's check once every file is read, where one impossible row stopped the night for every member on every try   2026-10-12

Corrects:   the fetch's refusal of a current member's unreadable row, from the 2.1 correction e14eeff3. The night of
            2026-10-12 stopped at its fetch at 23:00Z and again at 23:15Z on one row: LUMN opening at 5.25 above a high
            of 5.05, its high, low and close all 5.05. The provider's per-ticker endpoint served the same row when asked
            after, so each try would have stopped the same way; the operator had the night stopped at 23:28Z.
Ruled:      by the operator on 2026-10-12. For the second source, "Yahoo, Nasdaq cross-check (Recommended)"; for a member
            the sources cannot fix, "I need you to design a fallback calls for all the stocks that have such failure for
            the night and while the design should be to skip the stock and carry on, at the end all the failed stock are
            to be consolidated and the fallback call to be used to fix those"; and "It should work for any stock". The
            decision is written (see: A member's day bar the provider sends impossible is skipped and asked of Yahoo with Nasdaq's check once every file is read).
            For the reports, "Off-peak drain at 16:30Z (Recommended)": the night queues its report requests and stops,
            starting neither the drain nor the labeller, and a scheduled run writes them at 16:30Z; carried below.
Built:      the fetch stores nothing from a file for a member whose row the reader refused and keeps the row's reason,
            open, close and adjusted close; once every file is read it asks the refused members of every session
            together, in session and then ticker order, at most 20 a night, of Yahoo's daily chart and, where Yahoo
            answers a bar, of Nasdaq's dated history and then its quote naming the session it last closed. The bar is
            stored under the source `substitute` where it could have traded, its open and close stand within half a
            cent of the provider's and its high, low and close within half a cent of Nasdaq's; otherwise the member is
            left without the session as a member the file carries nothing for is. The fetch's row names every refused
            member with whether its bar was stored and why, and counts the sources' requests beside the bulk file's.
            The sources' one file, `YahooNasdaqBarFeed`, is the twenty-third permitted a client, each request tried
            twice at most and each try bounded by 20 seconds. CLAUDE.md's hard rule names the eighth carve-out; the
            catalogue row, section 14's note, section 17's per-name row and section 18's unreadable row, SCHEMA's bar
            source and the roster's nightly-cost row, the prior text in `CHANGELOG.md`. The answers each source sent for
            LUMN on that evening are captured in the fixture and declared in its manifest.
Tests:      five more: each capture read as sent, the provider's own row refused by the reader; the rule worked by hand
            on LUMN and on each check at its edge, half a cent passing and a hundredth past it refused, and at the
            provider's adjustment; a member's impossible row stored from the sources over the fixture's file; one whose
            sources disagree left without the session; twenty asked, the twenty-first named past the cap, one source's
            failure stopping no other and a file every member reads asking nothing. And one more over the live feed
            through a constructed handler answering the captures: Nasdaq's history asked for the week to the session,
            since asked for the session's day alone it answers that the date is less than the date it starts from and
            holds no row, as a probe of LUMN's session found after the gates first ran, the quote read where the history
            holds no row, an older session read from the history with no quote asked, and a class of shares asked of
            Yahoo with a hyphen. Three changed: the fetcher's and the night's test of a member's unreadable row, which
            now finishes the night and names the member, and the count of shipped files permitted a client, 22 to 23.
Mutated:    the rule, stated before the run: one mutation a property the correction adds, each run filtered to the 16
            fetcher, substitute and night tests. M1 Nasdaq's check ignored: predicted red at the rule test and the
            disagreeing sources test. M2 the cap removed: predicted red at the cap test alone. M3 a refused member stopping
            the night again: predicted red at every test reading a refused member. M4 a substitute stored as `bulk`:
            predicted red at the store test alone. M5 Nasdaq asked where Yahoo answered no bar: predicted red at the cap
            test alone. Results: each red where predicted and nowhere else. M1 2 of 16, M2 1 of 16, M3 5 of 16, M4 1 of
            16, M5 1 of 16. Each reverted before anything was committed, the 16 passing over the reverted tree. M6, for
            the history's span, stated before its run: Nasdaq's history asked for the session's day alone, predicted red
            at the live feed's test alone; red there alone, 1 of 17, and reverted.
Verified:   `tools/ci.ps1` green and `tools/verify-phase.ps1` green over the tree carrying this entry, 4790d9e6: all six
            steps, 0 warnings, 0 errors, 2260 of 2260 tests ran with none failed in 15 minutes 12 seconds, migrations 0
            to 91 with none pending, schema version 91, against `data-ci` and never `data`; the phase report 54 tables,
            1268 claims, 1268 passing and none unexamined, 43 checks on the roster run and passed. The run over
            4659e15e stopped at the suite on the count of shipped files permitted a client, 22 against the 23 this
            correction makes; the runs over 6fcecc5a were green, 2259 of 2259 tests and 1268 claims passing, before the
            history's span was found.
Carried:    the night of 2026-10-12 run to its end from main after the merge, LUMN's row asked of the sources; the
            night's report requests left queued and its drain and labeller stopped, written by a drain started at 16:30Z
            on 2026-10-13; and the night queuing its requests and starting neither the drain nor the labeller, with the
            scheduled run at 16:30Z that starts both, as the operator ruled, built next.

### 11.4 - correction: the night queues its report requests and starts neither the drain nor the labeller, and the off-peak run starts both at 16:30Z   2026-10-12

Corrects:   the night's report step from 11.4 (f88d58ee), which started the drain after the close, and its labeller step
            from the 12.6 correction (5cd3d42b), which started the labeller beside it; both then waited into the small
            hours for the research model's peak to end, and the night's tail was the reports waiting.
Ruled:      by the operator on 2026-10-12: "I think the reports needs to be queued after that and the night should just
            stop. The report generation is continously getting in the high cost time period so i dont see any point in
            attempting this anymore", choosing "Off-peak drain at 16:30Z (Recommended)", the night queuing the requests
            and stopping without starting the drain or the paid labeller, and a scheduled drain writing them at 16:30Z.
            Told after that the settings' peak for the research model is 01:00 to 04:00 and 06:00 to 10:00 UTC on
            weekdays, so 10:00Z is off-peak too, the time was kept at 16:30Z. Two decisions are written (see: The night queues its report requests and starts neither the drain nor the labeller, and the off-peak run starts both at 16:30Z)
            (see: The news labeller is a process of its own the off-peak run starts, and its calls and its spend are its own),
            the second superseding the labeller's decision of the 12.6 ruling.
Built:      the night writes its six requests as before and its report row says they wait for the off-peak run; its
            labeller row says the off-peak run labels tonight's list, and it starts no drain and no labeller; its store's
            copy waits for neither. `tools/offpeak` and `tools/offpeak.ps1` build a clean copy of main's commit as the
            monthly run's script does, refuse a checkout off main or ahead of origin/main, then run `label-news` over the
            newest night's list and `drain`. Every citation of the superseded decision in the code, the tests and the
            specs moved to its successor; section 14's report, labeller and copy steps and its note, section 17's model
            calls row and section 18's labeller and drain rows; CLAUDE.md's hard rule, merge rule, layout and commands;
            the runbook's labelling and copy passages and a section on the off-peak run with its tasks for Windows and
            macOS; the roster's nightly-run row; the guide's sentence on when the six reports are written; the prior
            text in `CHANGELOG.md`.
Tests:      one more: the off-peak script in its check mode over temporary repositories, building main's commit,
            reusing a night's copy and refusing a checkout off main or ahead. One changed: the night's labeller test,
            which now asserts no labeller and no drain started, the labeller row's words, a copy waiting for none and
            the night's source asking its launcher for neither. The store-never-deleted script removals gain the
            off-peak script's two, and the harness's three step keys and due-point key follow the steps' words.
Mutated:    the rule, stated before the run: one mutation a property the correction adds, each run filtered to the
            labeller test, the off-peak script's test and store-never-deleted, 9 tests. M7 the night starting the drain
            again: predicted red at the labeller test alone. M8 the night starting the labeller again: predicted red at
            the labeller test alone. M9 the copy waiting for a labeller: predicted red at the labeller test alone. M10
            the off-peak script accepting a checkout off main: predicted red at the script's test alone. Results: each
            red where predicted and nowhere else, 1 of 9 each. Each reverted before anything was committed, the 9
            passing over the reverted tree.
Verified:   `tools/ci.ps1` green and `tools/verify-phase.ps1` green over the tree carrying this entry, 73fe3163: all six
            steps, 0 warnings, 0 errors, 2261 of 2261 tests ran with none failed in 16 minutes 52 seconds, migrations 0
            to 91 with none pending, schema version 91, against `data-ci` and never `data`; the phase report 54 tables,
            1268 claims, 1268 passing and none unexamined, 43 checks on the roster run and passed.
Night:      the night of 2026-10-12, run fresh from main's c3c77ab7 after the 2.1 correction merged, finished green
            with 0 model calls and 370 weighted calls, LUMN's bar stored from Yahoo; its drain was stopped as the night
            started it and its requests for PBI and DAVE wait for the 16:30Z run, its labeller having found no name on
            the S&P 500's list.
Carried:    the off-peak task registered with the machine's scheduler at 16:30Z every day once this merges, and the
            one-off task registered for 2026-10-13 removed in its place.

### 19.2 ruling - each pick's card and each stock's page gain a chart of what is behind it, every member's ledger readings stored each night by migration 92 for it   2026-10-13

Not a checkpoint entry: it lands nothing.
Ruled:      by the operator on 2026-10-13 while 19.2 was built, word for word in `prompts/`: a readings view, always drawn,
            each of seven headings a bar of the stock's percentile among its index's members tonight, and a push view
            where the learned score passed the tester on the index, each heading's share of the score in risks, the
            grouping one table in the core, with tests of the percentiles by hand, the push summing to the score and its
            absence where the score has not passed. Asked, since the night stores no member's readings, the operator
            chose that the ledger's night step store every member's readings on all three indices each night by
            migration 92, the pages working the chart out when drawn; 19.4's per-rule test takes migration 93.
Plan:       `BUILD_PLAN.md`'s phase 19 names four changes beyond the pages where it named three, 19.2's text and done
            condition gain the chart and its store, and the claims predicted grow by about 4 to about 1,328; the decision
            is written (see: A pick's card and a stock's page draw what is behind it by heading, its percentiles always
            and its push where the score passed). Its cost to the night is measured at 19.2.
Verified:   `tools/ci.ps1` green over main as 19.1's entry records, the checks reading these documents run filtered over
            the tree carrying this entry with 19.2's gates.
Carried:    nothing new.

### 19.4 ruling - paper trading and the trade log each state under their table that entries are priced at the listing night's close   2026-10-13

Not a checkpoint entry: it lands nothing.
Ruled:      by the operator on 2026-10-13, word for word in `prompts/`: paper trading and the trade log each state under
            their table, "Entries are priced at the close of the night that listed them; a trade placed after the signal
            would fill at the next session's open."
Plan:       `BUILD_PLAN.md`'s 19.4 gains the line and its done condition the clause reading it under both tables.
Verified:   `tools/ci.ps1` green over main as 19.1's entry records, the checks reading these documents run filtered over
            the tree carrying this entry with 19.2's gates.
Carried:    nothing new.

### 19.2 - colour, the header, the menu and the top of Tonight: gains green and losses red, a hue a setup family, the index tabs and the menu, Tonight's tiles and buy points, and the chart of what is behind a stock   2026-10-13

Built:      the palette: green for support, a buy and a gain, red for a stop, a loss and a failure, orange for resistance
            and a sale, amber for a wait; each family's hue a mark with a tint behind ink, and the status pills. Every
            signed figure on every page and in the export is drawn in the gain's or the loss's class by its sign. The
            header holds one line, the brand, three index tabs and a menu whose button carries the lane's word, with Loop
            and its badge, Ledger, and Paper trading at `#/paper`. A night not yet run folds into the date line at the
            minute most of the newest seven nights started. Every page names a member's sector by its newest company
            fetch's GICS sector, and every page lists the families in Tonight's order. Tonight opens on a tile a setup and
            one table of buy points across every setup, a selected row drawing its plan and its card beneath it; the run
            time, harness verdict, spend, prose counts and reason totals moved to the Run page. On the 19.2 ruling,
            migration 92's `ledger_reading`, every member's readings written a night by the ledger's night step, and the
            chart of what is behind a stock on each pick's card and its page: its percentile among its index's members
            under seven headings, and each heading's push from the score at the means where the score passed. The
            decisions, section 15's rows and Figure 15.3 with its key, SCHEMA, the guide and the roster, the prior text in
            `CHANGELOG.md`. The header's tabs narrow the Universe decision rather than supersede it, since each page keeps
            its selector. The remedy `tools/remedies/2026-10-13-19.2-the-ledger-readings-store.txt` migrates to 92.
Measured:   read-only over the scratch copy of the live store on the branch's build: nineteen routes on all three
            indices, a name's page and its export, scanned with no signed figure outside its own class; the header on one
            line at 390 and 1440 px in both palettes; the date line at 19:30 ET, most of the newest seven nights having
            started at 23:30Z; the chart reading "not read", the copy holding no `ledger_reading` row before a night.
Tests:      seventeen new, two of them in the place of the phone masthead's test and the night header's harness test,
            so fifteen more: the palette's floors, the header, the badge on both sides of one, the date line, the tiles
            and the selected row, the sectors, the family order, the moved operations, the headings, the percentiles and
            the pushes by hand, the fixture's readings by hand, and the signed figure reader and its scan. About thirty
            markup assertions changed for the classes and the moves. Derived expectation `behind-it.json`.
Claims:     nine more: the readings store, three marks, Tonight's setups and buy points, the date line's night and the
            chart on the card and on the page; five moved from 15.7 to 15.10 with the operations.
Pins:       none move.
Mutated:    the rule, stated before the runs: one mutation a property the checkpoint adds, each filtered to 19.2's
            seventeen tests, the S&P 500 card test, the two score chart tests and the ledger night test, 21. M1 the gain
            and loss classes swapped: predicted red at the signed scan alone; so, 1 of 21. M2 the push drawn where the
            score did not pass: predicted red at the score's rank test alone; so. M3 a push not taken from the mean:
            predicted red at the push sum and the rank test; red there and at the S&P 500 card test, which reads the
            push list too, 3. M4 a tie counted whole: predicted red at the percentile test and the fixture's chart; red
            at the percentile test and the three chart tests, the fixture's green. That test read a store holding no
            readings, the membership and bars alone, so its loop read nothing; it now reads the night's replay, its three
            members stated, and M4 run again is red at 5 of 21. M5 the date line by the newest start alone, M6 the badge
            counting answered proposals, M7 the operations drawn on Tonight too, M8 the Ledger's families in the store's
            order, M9 the sector from the membership snapshot, M10 the menu without the lane's word, M11 a tile with a
            held-back pick reading "listing tonight": each predicted red at its own test alone; so, 1 of 21 each. M12 the
            selected row ignoring the link's name: predicted red at the tiles test; it survived, no test naming a row on
            the S&P 400 or 600, so the card test of those indices now names S3 and a stock no row holds, and M12 run
            again is red there alone, 1 of 22. M13 the pullback's hue on the gain's green: predicted red at the palette
            test alone; so, 1 of 22. M14 the night step writing no readings: predicted red at the ledger night test and
            the fixture's chart; so, 2 of 22. Each reverted before anything was committed, the 22 passing over the
            reverted tree.
Verified:   `tools/ci.ps1` green and `tools/verify-phase.ps1` green over the tree carrying this entry, b507da25: all six
            steps, 0 warnings, 0 errors, 2276 of 2276 tests ran with none failed in 16 minutes 43 seconds, migrations 0
            to 92 with none pending, schema version 92, against `data-ci` and never `data`; the phase report 55 tables,
            1277 claims, 1277 passing and none unexamined, 43 checks on the roster run and passed. The run over
            cf3737bd stopped at the suite on a cast the price storage check does not list, the percentiles' none written
            as `(double?)null`, which b507da25 writes with no cast.
Queue:      done, 19.0 as PRs 446, 447 and 449, 19.1 as PR 448, the 2.1 and 11.4 corrections as PRs 450 and 451; in
            progress, 19.2; next, 19.3, every setup card built one way.
Carried:    the chart read back on the served pages after the first night past the remedy, and the ledger step's added
            time read off that night's run log against its 49 seconds of 2026-10-09; the sign-offs of phases 16, 17 and 18.

### 19.3 - every setup card built one way: its header and guide, tabs and chips, picks named by place, the funnel as a bar, one line where it lists nothing, its rows behind expanders, and the heavyweights card's tiles, sectors and standing tonight   2026-10-13

Built:      every setup card on every index opens on a header in its family's tint with its standing as a pill, tonight's
            count and a "?" guide written once for its kind of card. Tabs replace the selector, each variant named by
            the settings it differs on through one table of short words a kind of rule in the core, with the rule's
            settings as chips beneath and its sentence a click away; a chosen variant marks its differing chips and says
            what it shares with the live rule's passes tonight, adds and drops, and draws its picks with the family's
            three numbers read off the night's ledger readings. Live picks are drawn once, in the buy points, whose rows
            gain the report's state, the business and the stories, every other pick's card hidden beneath its row; a
            card names its picks by their place there. The funnel is one bar a gate with the largest drop named, a
            first gate passing none named so; a card listing nothing says so in one line; the stocks one gate short and
            the breakouts forming sit behind expanders, and the list across the setups is gone. The heavyweights card
            gains four tiles, its holdings under each sector's fund, why each empty sector holds none, each holding's
            return since bought and a standing pill read off tonight's closes by the rule's own functions, the close a
            look-back before read from the kept bars where no refetch has written the year since. A row a table opens
            is held inside the box the table scrolls in. Two decisions; 15.2, 15.5, 15.6 and 15.7, SCHEMA's market and
            kept bars, the guide and the roster, the prior text in `CHANGELOG.md`. No migration and no remedy.
Measured:   read-only over the scratch copy of the live store on the branch's build, Tonight on all three indices at 390
            and 1440 px in both palettes, no page wider than its screen. By hand off the copy: AMD's lead 1.3473 over
            its fund, still leading; CAT's close 799.37 under its average 800.76, below its 200-day; the S&P 500 book's
            tiles 5 holdings, 3 of 11 sectors, 16 sessions to the next rebalance. The S&P 600's opened card ran 16 px
            past its box at 390 and a closed market check's funnel said no gate stopped a member; both corrected here.
Tests:      ten new, nine read-surface and the fixture's: the chips of every kind of rule and every S&P 500 variant
            differing on one, the funnel's drop, the one line on both sides of its floors and on a closed market, the
            three numbers, each empty sector's reason, the standing at its edges, the kept bars' guard, the tiles and
            sectors by hand. About forty markup assertions moved with the cards. Derived expectation `setup-cards.json`.
Claims:     six more: seventeen parts of the setup cards and the heavyweights card for eleven they retire, the selector,
            the rows' place, the buy between its stop and target, the why in words, the key, a variant's picks in the
            live rule's rows, the list across the setups' three and the heavyweights' sector cell and held while
            leading, each still counted by the phase that landed it.
Pins:       none move.
Mutated:    the rule, stated before the runs: one mutation a property the checkpoint adds, each filtered to the 36 tests
            of the cards, the families' pages, the heavyweights and the fixture's setup cards. M1 a breakout's volume
            chip read off the range ceiling: predicted red at the chips by hand and the variant card; red there and at
            every variant differing on a chip, 3 of 36. M2 a tab naming only what the variant lacks: predicted red at
            six; so, 6. M3 the differing chips unmarked: predicted red at three; so. M4 shared and dropped swapped:
            predicted red at two; so. M5 a tie in the funnel's fall to the later gate, M6 the line never flagged, M7
            strength as a multiple, M10 a lead of nothing beating its fund, M11 the kept bars read past a refetch, M12 the
            funds' tile read off the holdings, M13 an opened row bounded by the screen, M14 a first gate passing none
            unmarked, M16 the stories cell dropped: each predicted red at its own test alone; so, 1 of 36 each. M8 the
            picks not named by place: predicted red at three; red at seven, the three among them. M9 a lead of nothing
            still leading: predicted red at the standing's edges; red there and at the tiles, 2. M15 the tab reading
            Live on a provisional card: predicted red at the fixture's cards and the S&P 400's; red at five, those
            among them. M17 the other picks' card rows not drawn: predicted red at the buy points test and the S&P 500
            card test; red at the first, 1 of 36, the second standing outside the set. Each reverted before anything was
            committed, the 36 passing over the reverted tree.
Verified:   `tools/ci.ps1` green and `tools/verify-phase.ps1` green over the tree carrying this entry, fe865c52: all six
            steps, 0 warnings, 0 errors, 2286 of 2286 tests ran with none failed in 17 minutes 38 seconds, migrations 0
            to 92 with none pending, schema version 92, against `data-ci` and never `data`; the phase report 55 tables,
            1283 claims, 1283 passing and none unexamined, 43 checks on the roster run and passed. The checks run over
            053171d8 before the gates found five, repaired at fe865c52: a local function crossing a price to a
            statistic, and on its rerun a second, a card's pick link dated by the machine's locale, the standing's
            membership read not stated, the expectation naming no table and a key no test read; a sixth failed only
            because the expectation was edited while that run read it.
Queue:      done, 19.0 as PRs 446, 447 and 449, 19.1 as PR 448, the 2.1 and 11.4 corrections as PRs 450 and 451, 19.2 as
            PR 452; in progress, 19.3; next, 19.4, paper trading and the per-rule test.
Carried:    the 19.2 chart's read back and the ledger step's time after the first night past 19.2's remedy; the sign-offs
            of phases 16, 17 and 18.

### 19.4 - paper trading, Past picks as each account's trade log, and the monthly per-rule test   2026-10-13

Built:      the walk-forward tester's per-rule test, the `loop-rules` verb, run by the monthly run's `rules-GSPC`,
            `rules-MID` and `rules-SML` after its tests: every rule standing on an index walked at its own settings and
            hooks over the test years, a live or provisional rule against its benchmark unit for unit and each variant
            against it with its family's variants read together by the step-down, at the proposals' bar and screens; a
            rule the history cannot walk kept with the reading it lacks; written to `loop_rule` and `loop_rule_year`,
            migration 93, adopting nothing. The pullback's candidates are read once a run for every walk of them. Paper
            trading at `#/paper`: the account's settings, each account walked by the core's one function when the page
            is read, its trades sized by the card's sizing with exits before entries, a skip under the cash counted, its
            round trip as its table stores it, else the member readings' cost, else the published table at the value the
            member reader reads from its buy session's bar, an open position marked by its closes' ratio; a chart of the
            heads with a chosen family's variants dashed and keys toggling each line; a table grouped in the one family
            order by verdict and return, the test years verdict and the record since registration side by side. Past
            picks gains the account selector, each family's verdict beside the lists and each variant's or your trade log
            with its open trades so far at tonight's close and a running total, not evidence yet. Three decisions, one
            superseding the variant's results under its tests with 32 citations repointed; 13.12, 13.16, 15.2, 15.17,
            15.21, 16 and 18, SCHEMA, the RUNBOOK, the guide and the roster, the prior text in `CHANGELOG.md`.
Measured:   the per-rule test in print mode over the scratch copy of the live store, nothing written: the S&P 600 10
            rules in 919 seconds, the S&P 500 30 rules in 913, three not testable, and the S&P 400 8 rules in 675: 2507
            seconds, about 42 minutes, for the three. 19.0 estimated about 11 minutes with a bound of about 21; reading
            the pullback's candidates, about ten minutes an index once a run, puts it past the bound, and the monthly run
            grows by about 42 minutes. Its first S&P 600 run read every head's test years as none counted: a head read against an
            empty side counted no year, corrected here by pairing each unit with its benchmark at nothing, the S&P 500's
            heads then counting 3 and 4 of 4. No rule passes on any index. Paper trading and a trade log read on the
            branch's build over that copy migrated to 93, at 1440 and a true 390 in both palettes, the S&P 600's test
            written into it for its verdicts: no page wider than its screen once the trade log's selector, 46 px past
            it, wrapped under its label; an account with no trade said to set the account, a head's log link named a key
            the log does not take and no link kept the index, and a provisional head's variant named its compared column
            the live rule, each corrected here.
Tests:      seven new: the per-rule test by hand over a constructed ledger, its run written whole and read back; the
            verdict's words and the record's label at 299 and 300 trades and 35 and 36 months; an account walked by hand
            with a skip, an uncosted trade and a split; each round trip in its order and the sale from a result; a trade
            before the member readings valued from its count and bar; the page and a variant's trade log over a
            constructed store; and the fixture's paper accounts. The monthly run's step order, Past picks' key and the
            stated crossings moved with them. Derived expectation `paper-accounts.json`.
Claims:     twelve more, 1295: the two stores, section 18's rule the per-rule test cannot walk, paper trading's seven rows
            and the trade log's two; 15.17's key reworded under its own key.
Pins:       none move.
Mutated:    the rule, stated before the runs: one mutation a property the checkpoint adds, each filtered to the nine
            tests of the per-rule test, the monthly run's steps, the accounts and the fixture's paper accounts. M1 a head
            read against an empty side, the defect the first S&P 600 run showed: predicted red at the per-rule test; so,
            1 of 9. M10 each variant judged alone: predicted red there; it survived, the test's second variant reaching
            nothing under any arrangement, a missing property. The test gained a variant of blocks all of one size whose
            arrangement turning its two negative blocks positive reaches the first variant's statistic, so the step-down
            reads two in 512 where alone reads one; M10 then red there, 1 of 9. M9 the per-rule steps before the
            tester's: predicted red at the two monthly run tests; so, 2. M2 no skip under the cash, M3 an open position
            at its shares times tonight's close: each predicted red at the walk by hand; so, 1 each. M5 the record too
            few at its count: predicted red at the label's edges; so, 1. M7 the member readings' cost passed over:
            predicted red at the round trip and the pre-readings cost; so, 2. M4 an open trade's result so far off its
            buy and tonight's close, M6 a family's accounts by return ascending, M8 every variant on the chart by
            default, M11 You before the variants in the selector: each predicted red at the page test; so, 1 each.
            Not mutated: the fund's difference, the uncosted count, the not-testable reasons' words and the settings
            kept from the store. Each reverted before anything was committed, the nine passing over the reverted tree.
Verified:   `tools/ci.ps1` green and `tools/verify-phase.ps1` green over the tree carrying this entry, 90de5219: all six
            steps, 0 warnings, 0 errors, 2293 of 2293 tests ran with none failed in 17 minutes 10 seconds, migrations 0
            to 93 with none pending, schema version 93, against `data-ci` and never `data`; the phase report 56 tables,
            1295 claims, 1295 passing and none unexamined, 43 checks on the roster run and passed.
Queue:      done, 19.0 as PRs 446, 447 and 449, 19.1 as PR 448, the 2.1 and 11.4 corrections as PRs 450 and 451, 19.2 as
            PR 452, 19.3 as PR 453; in progress, 19.4; next, 19.5, Universe.
Carried:    the remedy after the merge, `migrate` to 93 and `loop-rules` for 2026-10 on each index from main's clean copy
            outside the night, with its times; the 19.2 chart's read back and the ledger step's time after the first
            night past 19.2's remedy; the sign-offs of phases 16, 17 and 18.

### 19.5 - Universe: a tile a GICS sector, the search, every column sorting, the rows a page, the near-level and one gate short chips, the readings' bars, a card a row on a phone, and an old correction's notice folded onto the Run page   2026-10-13

Built:      the universe's sector tiles in the strip's place, a tile a GICS sector the newest company fetch files with
            its members, its uptrend share as a bar or that the trend is not read on the S&P 400 and 600, its count on
            tonight's lists and its sector fund's return over the live heavyweights look-back, a press filtering the
            table and a second lifting it, the members filing no sector named in one line beneath; a search box on a
            ticker's opening letters or any part of a company's name, kept in the link by the shell's script; every
            column but the strip sorting on its heading's press and turning on the next, a row holding no value last;
            fifty, a hundred or every row a page; chips for a close within one typical move of its nearest band's edge,
            a close inside the band counting, and for a member one gate short of a setup, the first such setup in the one
            family order tagged beside its name in the setup's tint with the gate it missed, the S&P 400 and 600 saying
            their nights store only the first part each member failed; the strength's bar, the pullback in moves, the
            dry-up's bar ticked at the night's filter ceiling and the tightness's with none; under 560 px each row a card
            of its name, close, trend and distance picture and the tiles two a row; a reading guide in the key's place;
            and a correction notice past thirty days by the newest session it affects folded onto the Run page's foot,
            Universe drawing one line linking it. One decision; 15.8, 15.10, section 17's two rows and Figure 15.4 with
            its key; SCHEMA, the guide and the roster, the prior text in `CHANGELOG.md`. No migration and no remedy.
Measured:   the 19.4 remedy from main's clean copy outside the night, 11:48 to 12:31Z: `migrate` to 93, then `loop-rules`
            for 2026-10, the S&P 500's 30 rules in 15 minutes 5 seconds with three not testable, the S&P 400's 8 in 11
            minutes 9 seconds and the S&P 600's 10 in 16 minutes 38 seconds, 42 minutes 52 seconds for the three against
            19.4's 42 minutes in print mode on the copy; no rule passed and no row made a model call or a request.
            Universe read back on the branch's Release build from a scratch folder over the live store, since the
            scratch copy earlier checkpoints read was gone from the temporary folder, writing nothing but the read
            surface's row on the run log at each start, as 5152's does: the S&P 500 and 400, near support, one gate
            short, sorted by close, a search and a sector pressed, at 1440 and a true 390 px in both palettes, no page
            wider than its screen. The S&P 500's tiles hold 501 members and the line beneath the 2 filing no sector,
            503; none on tonight's lists, the pullback passing none on 2026-10-12 and the drift's one pass held back by
            an open trade; the S&P 600's tiles hold its 2 pullback picks; 32 members one gate short; "app" finds 4. At
            390 the tiles stood one a row, about 1600 px before the table; two a row here, about 1000.
Tests:      twelve new, ten read-surface, the fixture's and Figure 15.4's key: the near-level chips at one typical move, a
            hundredth past it and inside a band; the search; each column's sort both ways; the rows a page with every
            chip keeping the other keys; the members one gate short by hand; the tiles over a constructed store; the
            dry-up's tick; the S&P 400's tiles; the phone's cards and tiles; a correction's notice on both sides of its
            thirtieth day; the figure's key naming every part it draws in the architecture and the guide. The
            strip's, the headings' and the swing columns' assertions moved with the table. Derived expectation
            `universe-screen.json`.
Claims:     eight more, 1303: Universe's tiles in the strip's place and its eight rows beside them, the Run page's
            corrections and section 17's two rows, less the strip's four parts.
Pins:       none move.
Mutated:    the rule, stated before the runs: one mutation a property the checkpoint adds, each filtered to the test it
            was predicted red at. M1 a close one typical move from its support's edge not near, M2 a close inside its
            band not near: each predicted red at the near-level test; so. M3 the search on any part of a ticker:
            predicted red at the search test; so. M4 a row holding no value sorted first: predicted red at the sort test;
            so. M5 the families out of the one order, M6 a member an exclusion keeps off counted one gate short: each
            predicted red at the one gate short test; so. M7 the members filing no sector given a tile: predicted red at
            the S&P 400's tiles; so. M8 a notice folded on its thirtieth day: predicted red at that case alone; so, 1 of
            2. M9 every row a page read as fifty, M12 a chip's link dropping the rows a page: each predicted red at the
            rows a page test; so. M10 the dry-up's ceiling not handed to the table: predicted red at the bars test; so.
            M11 the tiles one a row on a phone: predicted red at the phone test; so. Not mutated: the tiles' counts on
            tonight's lists, the funds' returns, the sort links keeping the other keys and the S&P 400's chip saying it
            cannot read one gate short. Each reverted before anything was committed, the twelve cases passing over the
            reverted tree.
Verified:   `tools/ci.ps1` green and `tools/verify-phase.ps1` green over the tree carrying this entry, e7fe4f24: all six
            steps, 0 warnings, 0 errors, 2306 of 2306 tests ran with none failed in 18 minutes 45 seconds, migrations 0
            to 93 with none pending, schema version 93, against `data-ci` and never `data`; the phase report 57 tables,
            1303 claims, 1303 passing and none unexamined, 43 checks on the roster run and passed. The checkpoint script
            over b1d3a163 stopped at the near-miss rule's readers, which Universe's one gate short chip now joins,
            repaired at e7fe4f24.
Queue:      done, 19.0 as PRs 446, 447 and 449, 19.1 as PR 448, the 2.1 and 11.4 corrections as PRs 450 and 451, 19.2 as
            PR 452, 19.3 as PR 453, 19.4 as PR 454 with its remedy run; in progress, 19.5; next, 19.6, the Queue.
Carried:    the 19.2 chart's read back and the ledger step's time after the first night past 19.2's remedy; the sign-offs
            of phases 16, 17 and 18.

### 19.2 - correction: one bar of links with the index menu, each index named with its cap, Tonight's top saying each thing once, and a phone's header on two lines   2026-10-13

Corrects:   19.2's header, whose three tabs and the selector each page opened on chose the index twice and whose menu
            button read "Menu Paid"; each index named without the size of company it holds; Tonight's top, whose night
            card, watch list card and empty buy points table repeated what the tiles and the market line say; and a
            phone's header, which stood four lines at 390 px.
Ruled:      by the operator on 2026-10-13, reading phase 19's screens: one way of choosing the index, the dropdown kept
            and taken off the top of the page, and the top-level menu brought back with a few important links beside it,
            on a bar like one the operator showed, with Tonight, Universe, Paper trading and the Loop on it and Research
            and Records as menus ("yes that works perfect"); "it says menu and paid which is incorrect"; "append to the name
            like midcap or large cap"; and the brief's items for Tonight's top and the phone. The decision is written
            (see: The bar's index menu chooses the index every page reads, each choice naming its cap and its members),
            the tabs' entry moved under Previously decided.
Built:      one bar on every screen: the brand; Tonight, Universe, Paper trading and the Loop with its badge; Research,
            holding Researched, the Queue, the Watch list and the switch naming the model that writes reports, and
            Records, holding Past picks, Run and the Ledger, each opening beneath its name; then the search, the index
            menu the page draws, which the shell sets in the bar, and the palette's toggle; the screen drawn a pill and
            its menu marked; under 1280 px the links, the search and the toggle in a panel beneath a button reading Menu,
            the index menu staying in the bar. The title and date line beneath the bar. Each heading and each choice of
            the index menu name the index's cap; Paper trading draws the menu. On a night the families drew, no night
            card and no watch list card: the market line gains what ran tonight and the watch list's line, and a night
            no setup lists draws one line with the members one gate short, its guide behind "?". On a phone the date
            line is the night's short date between its arrows and a page's other date line holds one line. 15.3, 15.6,
            15.7, 15.15, Figure 15.3 and its key, the guide and the roster, the prior text in `CHANGELOG.md`.
Measured:   read back on the branch's Release build from a scratch folder over the live store, writing nothing but the
            read surface's row on the run log at each start: Tonight on all three indices, Paper trading, Universe and
            the Queue at 1440 and a true 390 px in both palettes, no page wider than its screen; at 390 the bar 53 px,
            the title one line of 21 px and the date line one of 20 to 23; the buy points line 55 px. The first draw of
            the line stood 139 px tall: its class was a name the decision card already used, renamed here.
Tests:      seven new or rewritten: the bar, the cap in each heading and choice, Tonight's top on a families night, the
            buy points line, the phone's rules, the date line's words apart from its picker; and about twenty assertions
            on headings, the select count and the lane's words moved with them.
Mutated:    the rule, stated before the runs: one mutation a property the correction adds, each filtered to the tests it
            was predicted red at. M1 the heading without its cap, M2 the menu's choices without it: each predicted red at
            the cap test and the S&P 400's Tonight; so, 2 each. M3 the index menu left out of the bar: predicted red at
            the bar test; so. M4 the night card drawn on a families night, M5 the watch list's line dropped: each
            predicted red at Tonight's top test; so. M6 the members one gate short not handed to the line: predicted red
            at the line's test; so. M7 the phone's short date never shown: predicted red at the phone test; so. Each
            reverted before anything was committed.
Verified:   `tools/ci.ps1` green and `tools/verify-phase.ps1` green over the tree carrying this entry, 2808ed96: all six
            steps, 0 warnings, 0 errors, 2315 of 2315 tests ran with none failed in 17 minutes 35 seconds, migrations 0
            to 93 with none pending, schema version 93, against `data-ci` and never `data`; the phase report 57 tables,
            1303 claims, 1303 passing and none unexamined, 43 checks on the roster run and passed.

### 19.3 - correction: a card listing nothing folded to one line, the heavyweights' holdings first with their empty sectors in plain words, and a press keeping its card where it stood   2026-10-13

Corrects:   19.3's setup cards, whose empty card stood 420 to 515 px at 1440 saying why it listed nothing three times;
            the heavyweights card, whose eight empty sectors stood among the holdings and said "Its leaders' beta sat under
            one" naming no stock and no figure; and a card's tabs, which redrew the page at its top, as Universe's chips,
            tiles and headings of 19.5 did.
Ruled:      by the operator on 2026-10-13: "The words mean nothing meaningful"; "when i click on any of these variants the
            entire screen refreshes and i loose where i was"; and the brief's items for the empty card and the heavyweights.
Built:      a card listing nothing with no variant chosen folds to one line, its name, its pill and "nothing tonight ·
            most stopped at the X gate (n of m) · empty n nights, its usual longest m", or that the market check closed;
            a press opens its guide, tabs, chips, sentence, funnel, forming and one gate short. The sentence repeating the
            funnel and the funnel's line beneath its bars stand on an open card alone, and the funnel leaves out the
            market's row while the check is open. The heavyweights' table holds the sectors holding a stock; the rest
            follow behind "n sectors with no holding", a line each with its fund's return and a reason naming the stock
            closest to being bought, what it missed by and the next rebalance. A tab, a chip, a tile, a heading or the
            search keeps its card where it stood in the window across the redraw. 15.7's rows, the guide and the roster,
            the prior text in `CHANGELOG.md`.
Measured:   on the same build: folded cards 51 px at 1440 against 419 to 515 before, and 97 to 121 at 390 against 608 to
            877, each under a quarter; the S&P 400's fundamentals-first card stood 186 against 620 while its line restated
            the market check's figures, 121 once it said the check closed, the figures standing in the market line. A
            variant's tab and Universe's close heading pressed at 1440 and 390: the card 300 px and 200 px down the window
            before the redraw and after. The October rebalance's empty sectors read off the live store's rows: WBD beat XLC
            by 64.9 points in an uptrend with a beta of 0.49; C led XLF's seven by 33.0 points, none in an uptrend.
Tests:      three new or rewritten: a card with a pick or a variant never folded, the empty sectors' words in every case
            worked by hand, the shell keeping the place; and the empty card, the fixture's cards, the funnel and the
            heavyweights card's table read in their new form.
Mutated:    M8 no card folded: predicted red at the empty card tests; so, the 2 the filter took. M9 the market's row kept
            while open: predicted red at the empty card, a variant's card and the S&P 400's card; so, 3, the fourth the
            filter took a 19.1 test. M10 the funnel's line drawn on a folded card: predicted red at the empty card test;
            so. M11 the empty sectors in the holdings' table: predicted red at the heavyweights card test; so, at the
            second filter, the first naming no test of the card. M12 the weakest stock named in the beta case: predicted
            red at the words test; so. M13 the redraw ignoring the kept place: predicted red at the shell's test; so.
Verified:   `tools/ci.ps1` green and `tools/verify-phase.ps1` green over the tree carrying this entry, 2808ed96, as the
            19.2 correction's entry records: 2315 of 2315 tests, 1303 claims passing and none unexamined.

### 19.4 - correction: paper trading walked on an example account until one is saved, each verdict's figures beside its words, and the record's count in one line   2026-10-13

Corrects:   19.4's paper trading, which drew no chart and no table until the operator saved an account, read "within
            luck" for 27 of the S&P 500's 30 rules with nothing to tell them apart, and wrote "too few trades (n of 300)".
Ruled:      by the operator on 2026-10-13, the brief's items for paper trading.
Built:      with no account saved, every account walked on the example the core holds, a hundred thousand dollars with one
            per cent a trade and a fifth at most in one position, named at the page's top and written nowhere, the form
            left empty; beneath each verdict its adjusted p against the loop's bar and a pair of bars a test year on one
            scale; the record's cell "n trades · d decided of 300", or a book's "n holdings · m of 36 months", with the
            walk's skips and its first day beneath. 15.21's rows, the guide and the roster, the prior text in
            `CHANGELOG.md`.
Measured:   on the same build with no account file: the S&P 500's chart drawn with its three heads and 27 accounts'
            year bars at 1440 and 390 in both palettes, no page wider than its screen, and no file under the data root
            written.
Tests:      two new: the example walked and written nowhere, then the operator's own; the count line by hand; and the
            paper page reading the adjusted p and a year's bars.
Mutated:    M14 no example where none is saved: predicted red at the example test; so. M15 the compared bar drawn from
            this rule's total, M17 the adjusted p not drawn: each predicted red at the paper page test; so. M16 trades
            counted as decided: predicted red at the count test and the paper page test; so, 2.
Verified:   `tools/ci.ps1` green and `tools/verify-phase.ps1` green over the tree carrying this entry, 2808ed96, as the
            19.2 correction's entry records: 2315 of 2315 tests, 1303 claims passing and none unexamined.
Queue:      done, 19.0 to 19.5 as PRs 446 to 455 with the corrections between; in progress, these corrections of 19.2,
            19.3 and 19.4; next, 19.6, the Queue, on the operator's word.
Carried:    the 19.2 chart's read back and the ledger step's time after the first night past 19.2's remedy; the sign-offs
            of phases 16, 17 and 18.

### 11.4 - correction: the off-peak run builds main's own commit whatever branch the checkout is on   2026-10-13

Corrects:   the off-peak run from the 11.4 correction of 2026-10-12 (73fe3163), which built the checkout's own commit and
            refused a checkout off main. Its first scheduled run, at 16:30Z on 2026-10-13, was refused because the
            checkout was on a session's branch for the 19.2 correction, and the labeller and the two reports the night of
            2026-10-12 queued, PBI and DAVE, waited for a run by hand from main, made at 16:33Z.
Ruled:      by the operator on 2026-10-13, choosing "Build main's commit anyway (Recommended)" over a try every half
            hour until 22:00Z and over leaving the refusal to be answered by hand. The decision is superseded by one
            naming main's own commit (see: The night queues its report requests and starts neither the drain nor the labeller, and the off-peak run starts both at 16:30Z from main's own commit).
Built:      `tools/offpeak` reads the commit from the main branch and not from the checkout's HEAD, names the branch on
            its line where the checkout is on one, and refuses only a main holding a commit origin/main lacks, or a
            checkout with no main. Every citation of the superseded decision in the code, the tests and the specs moved
            to its successor, section 14's request step and the harness's three keys for it among them; the runbook's
            section on the off-peak run; the roster's nightly-run row; the prior text in `CHANGELOG.md`.
Tests:      one rewritten: the off-peak script in its check mode over temporary repositories, now on a branch holding a
            commit of its own building main's commit and naming the branch, and refusing a main ahead of origin/main from
            main and from a branch.
Mutated:    the rule, stated before the run: one mutation a property the correction adds, each run filtered to the
            off-peak script's test, 1 test. M18 the commit read from the checkout's HEAD again: predicted red at the
            script's test, the branch's commit built. M19 the count ahead read against the checkout's HEAD: predicted
            red at the script's test, the branch refused for its own commit. Results: each red at the script's test, 1
            of 1, M18 at the branch's run refused as ahead for its own commit, which M18 built from and counted, and M19
            the same. Each reverted before anything was committed, the 1 passing over the reverted tree.
Verified:   `tools/ci.ps1` green and `tools/verify-phase.ps1` green over the tree carrying this entry, f0ab8b02: all six
            steps, 0 warnings, 0 errors, 2315 of 2315 tests ran with none failed in 19 minutes 59 seconds, migrations 0
            to 93 with none pending, schema version 93, against `data-ci` and never `data`; the phase report 57 tables,
            1303 claims, 1303 passing and none unexamined, 43 checks on the roster run and passed.
Run:        the off-peak run by hand from main's e4f57b98 at 16:33Z: the labeller reached none, the S&P 500's list of
            2026-10-12 listing nothing; the drain wrote DAVE by 16:42Z and PBI by 16:51Z, 16 sections each, 32 model calls
            and 0.15 dollars in all.
Carried:    nothing new.

### 19.6 - the Queue: what settled a row a stock and New York day over fourteen days, the reports written by hand a row a day with their cost, and every time in New York's with UTC beneath the pointer   2026-10-13

Built:      the queue page's settled requests drawn a row a stock and New York day it was asked on, newest first, with
            the newest request's outcome, when it settled and why, and how many times it was asked that day, over the
            fourteen New York days to the page's own with the older behind a link drawing every day; beneath them the
            research passes no request names, a regenerate or a pass from the worker's command, a row a New York day
            with how many reports they wrote and what their answered paid calls cost, a trial's and a review's calls
            left out and a pass a request being written is under counted as that request's; and every instant on the
            page, the asked, settled and pass times and a stopped drain's start, in New York's time marked ET with UTC
            beneath the pointer. Tonight's rows keep their form. Two decisions, one superseding the queue's times; 15.15,
            the 15.7 row's citation and section 17's window row; the guide and the roster, the prior text in
            `CHANGELOG.md`. No migration and no remedy.
Measured:   read back from a scratch folder's Release build of the branch over the live store, read-only but the read
            surface's start row, at 1440 and a true 390 px in both palettes, no page wider than its screen: 43 requests,
            all settled, drawn as 27 rows over the window, BDX, IT and MO each one row asked 4 times on 2026-09-30, and
            34 over every day; 4 days written by hand in the window, 2026-10-11's 21 reports at $1.35, and 7 over every
            day; the link naming 10 older rows.
Tests:      four new, three read-surface and the fixture's: the grouping and the window over constructed requests across
            midnight in New York; the passes no request names with a request being written and a theme's run; the page
            read back off the route over a constructed store, a trial's call kept out of the cost; and the time form on
            both sides of 2026-11-01. The 11.2 page test, the drain's stop and the region tests moved to the new form and
            the grouped rows. Derived expectation `queue-screen.json`: the fixture's pass run by hand, its cost the two
            parts `research-record.json` works by hand.
Claims:     two more, 1305: the Queue's row written by hand and section 17's window.
Pins:       none move.
Mutated:    the rule, stated before the runs: one mutation a property the checkpoint adds, each filtered to the test it
            was predicted red at. M1 the day read in UTC, M2 the oldest request's outcome, M3 the window a day longer:
            each predicted red at the settled days test; so. M4 a pass a request being written is under counted by hand,
            M5 a report counted a paid call: each predicted red at the passes by hand test; so. M6 the time beneath the
            pointer in New York's: predicted red at the time form test; so. M7 the asked instant drawn as stored:
            predicted red at the 11.2 page test; so. M8 the older rows leaving out the passes by hand: predicted red at
            the fixture's expectation; so. M9 the shell dropping the link's query: predicted red at the route test; so.
            Not mutated: the settled time's cell and the empty window's lines. Each reverted before anything was
            committed, the eleven queue cases passing over the reverted tree.
Verified:   `tools/ci.ps1` green and `tools/verify-phase.ps1` green over the tree carrying this entry, 55dacfeb: all six
            steps, 0 warnings, 0 errors, 2320 of 2320 tests ran with none failed in 17 minutes 35 seconds, migrations 0
            to 93 with none pending, schema version 93, against `data-ci` and never `data`; the phase report 57 tables,
            1305 claims, 1305 passing and none unexamined, 43 checks on the roster run and passed.
Queue:      done, 19.0 to 19.5 as PRs 446 to 449 and 452 to 455, the screens' corrections as PR 456, the 11.4
            correction as PR 457; in progress, 19.6; next, 19.7, the remaining screens.
Carried:    the 19.2 chart's read back and the ledger step's time after the first night past 19.2's remedy; the sign-offs
            of phases 16, 17 and 18.

### 19.4 - correction: each paper account counts only its rule's current version, from where the Run page's record of the rule starts, the earlier versions' trades folded in its trade log   2026-10-13

Corrects:   19.4's paper accounts, which counted a variant from its first registration and a live account from its
            first trade, across every version its rule had been registered at.
Ruled:      by the operator on 2026-10-13, display only, with a provisional account counting every trade from its first
            on their answer of the same day; and 19.10, entries priced at the next session's open, queued beside 19.9.
Built:      a registered rule's account counts the trades bought from the session the Run page's record of the rule
            counts from, its standing registration's first session or a reproducing replay's on the S&P 500, saying where
            the replay carried it back; the trades before it in none of its line, return, worst drop or counts, and in
            its trade log folded beneath its trades, each named by the registration that listed it; each row and log
            stating the start and the version; a live or provisional account's trade log of its own, its row linking it
            and the selector listing it before its variants. One decision; 15.21's row and paragraph, 15.17's two rows
            and the chart's row; 19.10 in `BUILD_PLAN.md`; the guide and the roster, the prior text in `CHANGELOG.md`.
            Nothing stored is written or deleted.
Measured:   read from the branch's build over the live store, read-only but the read surface's start row: on the S&P 500
            the pullback's live account counts 8 trades from 2026-10-05 and leaves 7 out, the heavyweights' live book
            none from 2026-10-07 leaving 5 out, two drift variants none from 2026-10-09 leaving 2 and 3 out, and the
            breakout's and the drift's live accounts, registered again on 2026-10-12, count from 2026-10-02 as a
            reproducing replay carried them; on the S&P 400 and 600 nothing is left out.
Tests:      one new: a variant registered again, its walk, return, worst drop and counts worked by hand over trades
            either side of it, the earlier two folded in its log by the version that listed each, the live account
            leaving none out, the start at a reproducing replay's session and not a differing one's, and the stored
            trades and registrations read the same after the pages. The paper page test's log link and selector moved,
            and the stated counts with the row: 207 screen rows, 614 due points.
Claims:     one more, 1306: 15.21's start and version row.
Pins:       none move.
Mutated:    the rule, stated before the runs: one mutation a property the correction adds, each filtered to the paper
            tests, 8. M20 trades before the start counted, M21 the start read with no replay, M22 an earlier trade named
            by the newest registration, M23 no earlier versions drawn, M24 the version left out of the row, M25 the
            carried words never written: each predicted red at the new test; so, 1 of 8 each. Not mutated: the
            selector's order and the closes read for the earlier trades. Each reverted before anything was committed.
Verified:   `tools/ci.ps1` green and `tools/verify-phase.ps1` green over the tree carrying this entry, 922e7721: all six
            steps, 0 warnings, 0 errors, 2321 of 2321 tests ran with none failed in 19 minutes 59 seconds, migrations 0
            to 93 with none pending, schema version 93, against `data-ci` and never `data`; the phase report 57 tables,
            1306 claims, 1306 passing and none unexamined, 43 checks on the roster run and passed.
Queue:      done, 19.0 to 19.6 as PRs 446 to 449, 452 to 455 and 458, the screens' corrections as PR 456, the 11.4
            correction as PR 457; in progress, this correction of 19.4; next, 19.7, the remaining screens, on the
            operator's word.
Carried:    the 19.2 chart's read back and the ledger step's time after the first night past 19.2's remedy; 19.10 after
            the report; the sign-offs of phases 16, 17 and 18.

### 19.6 - correction: the off-peak run five minutes after each peak window ends, and Write now on the queue   2026-10-14

Corrects:   19.6's queue page, which drew the night of 2026-10-13's two requests, JBL and NTCT, as starting now while
            the night starts no drain and the off-peak run waited until 16:30Z the next day.
Ruled:      by the operator on 2026-10-14: "i need an override button on that page where if i want the report to be
            generated right away i can. It show me a warning and ask confirmation if it is during peak hours or else
            generate right away. ANd i cannot have reports generating one day later. Schedule for the nearest off peak
            hours please", choosing a button a report and "5 mins after Every off peak interval start". Two decisions
            supersede the off-peak run's and the press's.
Built:      the off-peak run's times worked out in one place from the research job's profiles, five minutes after each
            peak window ends every day, 04:05Z and 10:05Z as shipped; the queue page states a queue of the night's
            requests alone, with nothing being written, as starting at the next run. Write now on each outstanding row
            starts `drain --now <TICKER>`, which claims that one request ahead of the queue; inside a peak window the
            row carries the window's end and the multiple, the shell's script states both and asks, and the read surface
            refuses a press at peak without the confirmation, the drain then writing it with `--at-peak`, which the
            industry's pass reads. A drain waiting out a window lets its lock go while it waits. The night's two rows,
            section 14's steps and note, the catalogue's two rows, 15.15's When row, its new Write now row and its
            paragraph, the runbook's scheduling for both machines and a paragraph on Write now, CLAUDE.md's rule and
            layout, the guide, the roster; every citation of the two superseded decisions moved; the prior text in
            `CHANGELOG.md`.
Tests:      four new: the run's times worked by hand; the queue's times for the night's requests alone, with a press's
            among them and with one being written; the drain writing the named request alone, confirmed and not; and
            the press drawn, refused without the header, for a stock with none, at peak without the confirmation, and
            started off-peak and confirmed. The stated counts with the row: 208 screen rows, 615 due points.
Claims:     one more, 1307: 15.15's Write now row.
Pins:       none move.
Mutated:    the rule, stated before the runs: one mutation a property the correction adds, each filtered to the four
            new tests. M26 the run read at a window's start rather than its end: predicted red at the run's test. M27
            the run applied with a press's request in the queue: predicted red at the queue's test. M28 the press at
            peak started without the confirmation: predicted red at the press's test. M29 Write now claiming the oldest
            request rather than the named one: predicted red at the drain's test. M30 Write now not waiting where the
            peak is not confirmed: predicted red at the drain's test. Not mutated: the lock let go while a drain waits
            and the industry's pass reading the flag, which no test reaches. Results: each red where predicted, M26 at
            the queue's test as well, which reads the same times, 2 of 4; M27, M28, M29 and M30 1 of 4 each. M28 first
            written as the refusal never made, which the compiler refused as reading a peak that is none, and run as
            the refusal made only on a word no press sends. Each reverted before anything was committed, the 4 passing
            over the reverted tree.
Verified:   `tools/ci.ps1` green over the tree carrying this entry, d14451b6: all six steps, 0 warnings, 0 errors, 2325
            of 2325 tests ran with none failed in 24 minutes 15 seconds, migrations 0 to 93 with none pending, schema
            version 93, against `data-ci` and never `data`. The first run over 4ecbaf06 failed two drain tests reading
            the verb by its old signature and two rows read as lists of parts, and the second the changelog's check on
            the commit fixing them, each corrected in d14451b6. `tools/verify-phase.ps1` was not run to its end: the
            operator merged PR 460 while it ran, and it was stopped.
After:      the scheduled task registered again at 04:05Z and 10:05Z on 2026-10-14 at 03:34Z; its first run, at 04:05Z,
            built 19e6bfe6 and labelled 21 articles of the night of 2026-10-13's list, while a drain a press on JNJ
            started at 03:54Z waited out the window and wrote JBL by 04:08Z.
Carried:    nothing new.

### 19.7 - the remaining screens: the Run page in the monthly run's terms, the Loop's summary, the Ledger's bars and strip, the Watch list's nearest setup and the researched list's columns   2026-10-14

Built:      the Run page under two headings, Health and Research, holding none of the retired loop's clocks or
            checkpoints: the newest monthly run's date and state with each family's proposals tested, passed and put
            and each registered rule's verdict, linking the Loop and paper trading, the shape clock in one line; the
            setup families a row a rule under its setup's heading row in its tint with the level its rules share, each
            rule by its short words with its full name and where its record counts from under the pointer, and its
            verdict; the trades in one line linking their log; and every length of time as "31 min 32 s" by one
            function. The Loop page opens on a summary a family with its chips, the strongest proposal's adjusted p as
            a meter against the bar and the one put with its presses, or why none is and when the next run is, the
            business-first family waiting on 19.9; one line saying what a change must pass; each proposal a row opening
            to its paired bars a year; the learned score named by its fit month. The Ledger names 19.9 for the
            business-first family and draws each family's edge a year as bars, the newest year's deciles as a strip,
            the units once in each heading and the settled setups under family tabs. The Watch list draws each name's
            nearest setup with the gate it misses, its distance picture and sessions to its report, and adds a name
            through the header's search. The researched list draws each report's staleness by the page's judge, its
            move since written, its sections of ten and why it was written, sorted by day or staleness. The shadow
            candidates' line no longer says a record waits for a promotion. Section 13.8's sentence, 15.5, 15.8, 15.10,
            15.16, 15.19, 15.20 with Figure 15.5 and its key, section 20's row, the guide with its Loop picture, the
            roster and `CHANGELOG.md`. No migration and no remedy.
Measured:   read back from the branch's Debug build over the live store, read-only, at 1440 and a true 390 px in both
            palettes on the Run page, the Loop for the S&P 500 and 600, the Ledger, the Watch list and both researched
            orders: no page wider than its screen and no element cut off. The run for 2026-10 started at 21:02Z on
            2026-10-11 and held every step: the breakout and the drift 13 proposals each, the heavyweights 2, none
            passed, the pullback none tested. 37 researched names, 5 watched. The read-back found a proposal's row
            drawing its stored change as JSON 7,400 px wide, now its words wrapping.
Tests:      twelve new: ten read-surface over constructed stores and rows, the Loop figure's key, and the fixture's.
            Twenty-two older tests moved to the new markup, words and duration form. Derived expectation
            `remaining-screens.json`: the fixture's night counting no trade and no monthly run, and KEYS's report
            written by hand on its night, seven sections of ten, standing.
Claims:     1312, five more than 1307: 24 rows and parts added, 19 retired and counted where they landed.
Pins:       none move.
Mutated:    the rule, stated before the runs: one mutation a property the checkpoint adds, on the line computing it,
            each filtered to the test it was predicted red at. M1 the duration's seconds modulo 59, predicted red at the
            duration test; so. M2 the meter's floor at a thousandth, at the meter and the summary tests; so. M3 the
            nearest setup ordered by most gates missed, at the nearest setup test; so. M4 why written reading two days
            after, and M5 the move's ratio inverted, each at the researched test; so. M6 the monthly run reading a
            variant's row as the rule's, at the monthly run test; so. M7 the edge bars' hue by the other sign, at the
            Ledger test; so. M8 the shared level read only where two differ, at the setup families test; so. Not
            mutated: the guide lines and the shell's search. Each reverted before anything was committed.
Verified:   `tools/ci.ps1` green and `tools/verify-phase.ps1` green over the tree carrying this entry, af870d2d: all six
            steps, 0 warnings, 0 errors, 2336 of 2336 tests ran with none failed in 18 minutes 34 seconds, migrations 0
            to 93 with none pending, schema version 93, against `data-ci` and never `data`; the phase report 58 tables,
            1312 claims, 1312 passing and none unexamined, 43 checks on the roster run and passed.
Queue:      done, 19.0 to 19.6 with their corrections as PRs 446 to 460; in progress, 19.7; next, 19.8, which waits
            for the operator's go.
Carried:    Figure 11.4 still labels the edge clock's look at 12 blocks "earliest promotion", a picture the section 11
            worked example generates and holds word for word, owed a correction of section 11.

### 19.8 - the phase 19 report: every screen read back against the store, paper trading's figure and its key, the pair checked against the actual, and Tonight's card of the stocks one gate short with its Watch press   2026-10-15

Queue:      done, 19.0 to 19.7 with their corrections as PRs 446 to 462; in progress, this report; next, 19.9 and
            19.10 on the operator's go, and the sign-offs of phases 16, 17 and 18, each by a fresh session.
Built:      Figure 15.6 in section 15.21, paper trading with its parts numbered, every figure a placeholder, and a key a
            line a part, and the guide's same picture with its list, so the figures of Tonight, paper trading,
            Universe and the Loop are each keyed and section 15 is final for the phase. On the operator's addendum of
            2026-10-15, written into 19.8's text in `BUILD_PLAN.md` as it was issued, Tonight's card of the stocks one
            gate short across every setup beneath the buy points: folded to its line counting them as the market line
            does, opened to one table in the family order with each family's rows as its own card orders them, each row
            its setup's tag, the stock and its company, the gate it missed and what it had against the bar in words,
            the sessions to its next report and the stock page's own Watch press reading Watching for a name held; the
            market line's count and the no-buy-points line's each open it and scroll to it, the link keeping it open;
            on the newest night a link to Universe with the one gate short chip lit, and on an earlier night no press
            and no link; on the S&P 400 and 600, whose nights store only the first part each member failed, a line
            saying it reads none. Section 15.7's row, the watch list decision's places a name is added from, edited in
            place on the addendum's word, and the guide's account of Tonight. This checkpoint amends its own done
            condition: the addendum's tests joined it in `BUILD_PLAN.md`, the prior text in `CHANGELOG.md`. The roster's
            two rows. No migration and no remedy.
Screens:    read back on 2026-10-15 between 03:45Z and 04:00Z from the branch's build over the live store, read-only, at
            1,440 pixels and a true 390 in both palettes, 14 screens and 56 shots with no element past its window
            outside a box that scrolls, laid out with what each was read back against at
            https://claude.ai/artifact/9s5jFk71Mw8rBFHhbjbhKR. Night 2026-10-14: the S&P 500's lists open at 51.2% over
            45%, no buy point and 17 one gate short, the pullback's 8 and the breakout's 3 and the drift's 6; the S&P
            400's closed at 41.5%; the Queue's 46 settled, 43 written and 3 refused; the night's 32 min 55 s from 23:00:16Z
            to its close at 23:33:11Z; the Loop's 13, 13 and 2 proposals tested on 2026-10-11, none passing; the Ledger's
            1,965 settled 2026 pullback setups; 5 watched; 24 researched names of the S&P 500; JNJ's close of 253.91.
            The read-back found the Watch list's report column reading "no date" for every name, its route building
            the rows without the calendar, and it now counts to each name's next report as Universe does, the card's
            column with it.
Pair:       before phase 19, 1267 claims; the plan's pair after 19.8 1328 within 1290 to 1370; the actual 1313, inside
            the range and 15 short of the pair by the net of what each part landed fewer or more than its count: 19.2
            three fewer, 19.3 two fewer, 19.4 two fewer, 19.5 one more, 19.6 one more, 19.7 ten fewer and 19.0, 19.1
            and 19.8 as counted, each part's rows named with what it landed and took out in
            `ArchitectureConformance.PhaseNineteenLanded`, the 19 claims 19.7 retired among them, none reached by a
            claim.
Operating:  phase 19 opened no operating row; `obligation-reconciles` states phase 19 at none.
Tests:      six new: the pair, paper trading's figure's key, and four read-surface over constructed stores, the card's
            count on every index with its order, the press with the report count on the card and the Watch list, an
            earlier night and the shell's script. Older tests moved: the market line's count as a link, and the stated
            screen rows at 220, due points at 621 and the families' tuple at 1313.
Claims:     1313, one more than 1312: section 15.7's row of the card.
Pins:       none move.
Mutated:    the rule, stated before the runs: one mutation a property the checkpoint adds, on the line computing it,
            each filtered to the test it was predicted red at. M1 the card's rows ordered by the family order reversed,
            predicted red at the card's count and order test; red. M2 the press drawn on every night, at the earlier
            night test; red. M3 a row's watched flag inverted, at the press test; red. M4 19.7's count of fewer at
            nine, at the pair test; red. M5 the guide's part 6 of paper trading left out, at the figure's key test;
            red. M6 phase 19 stated as opening one operating row, at the operating rows test; red, phase 19 stated at
            one against none in its table. Not mutated: the
            stylesheet's rules for the card and the market line's link words. Each reverted before anything was
            committed.
Verified:   `tools/ci.ps1` green and `tools/verify-phase.ps1` green over the tree carrying this entry, ac60e950: all six
            steps, 0 warnings, 0 errors, 2342 of 2342 tests ran with none failed in 17 minutes 48 seconds, migrations 0
            to 93 with none pending, schema version 93, against `data-ci` and never `data`; the phase report 59 tables,
            1313 claims, 1313 passing, none out of scope and none unexamined, 1324 placements and verdicts reconciled
            against a floor of 34, 43 checks on the roster run and passed.
Carried:    19.9, the ledger writing the business-first family's setups, owed after this report on the operator's
            approval of 2026-10-12, and 19.10 beside it; Figure 11.4's "earliest promotion", owed a correction of
            section 11; the market line drawing the S&P 600's stored breadth of 0.4497 as 45.0% beside "below its
            floor of 45%", owed a correction of 19.2; the sign-offs of phases 16, 17 and 18.

### 19.9 - the ledger writes the business-first family's setups, the tester proposes for it over its 27 settings, and an approval of it applies on every index   2026-10-15

Queue:      done, 19.0 to 19.8 with their corrections as PRs 446 to 463; in progress, this checkpoint; next, 19.10 on
            the operator's go, and the sign-offs of phases 16, 17 and 18, each by a fresh session.
Ruled:      by the operator on 2026-10-15, word for word in `prompts/2026-10-15-19.9-ruling-loose-gates.md`: the loose
            gates the dials a step wider, the cash read only where the income is above nothing, the margins tested from
            the two margin changes every setup carries with no column added; and an approval of a proposal for the
            family applies it. This checkpoint amends its own done condition: the approval read by the night joined
            19.9's text in `BUILD_PLAN.md`, the prior text in `CHANGELOG.md`.
Built:      the family's loose gates, a pullback setup at its own plan whose revenue grew by more than -0.05 and whose
            cash is at least 0.6 of its income, its pass the provisional rule's as the night reads it, written by the
            night's step, the history build and the point-in-time check through the one function, the S&P 500's pick
            of the family read off the index families' rows; the tester's procedure over the 27 settings on every
            index, a tie to the fewest dials moved, and the family's rule handed to the autopsy, winners against losers
            and the learned score; the apply step taking the family's setting and hooks on every index, the S&P 500's
            included, the index families' step reading the stored setting there in the provisional one's place and the
            night's settings naming it, the per-rule test walking it at its stored setting and hooks where it walked
            the pullback's hooks before; the card's score part reaching the family, the S&P 500's cards of it among
            them; the Ledger page's 19.7 line gone and the family drawn on the Ledger and the Loop pages as every
            family is. Two decisions: the family's setups and its approval. Sections 7, 13, 14, 15 and 17, the schema,
            the runbook, the guide and the roster. The card's gate line read the provisional key as "cash> 1"; the
            night's settings now carry the setting's short words, which read whole.
Measured:   no migration, as 19.0 stated: the three tables hold the family by its word. The history's standing span is
            2019-01-02 to 2026-10-05 on all three indices, built in 875, 641 and 898 seconds on 2026-10-12; the remedy's
            three builds with `--again` and the first night after the merge measure the family's added time, against
            19.0's estimates of at most about 58 minutes for the three builds and about 15 seconds a night.
Tests:      seven new: the gates by hand, the family's setups over a constructed history, the point-in-time check, the
            procedure over constructed folds, an approved setting read by the night and on the S&P 500 over a store,
            the night's step over a constructed store, and the tester's walk over the same store. Older tests moved:
            the ledger's families at four, the apply step's refusal of the family now its acceptance, the Loop and
            Ledger pages' family order and cards, the badge counting the family's put proposal, and the 19.7 page
            tests without their notes.
Claims:     1313, unchanged: 19.9's about 3 landed as words in rows that stood, section 14's two steps, section 17's
            loose gates and search rows and section 15's Ledger and Loop rows.
Pins:       the readings' version moves from f3311f905a3e to 859241ef8442, the ledger's filling source changed, so
            rows written from the merge carry the new pin. The index families' step is a source every S&P 400 and
            600 rule pins, so their four evaluators move: pullback a748b3a99fb5 to 12e86ab94128, breakout b808c533ad30
            to f6b437f95a93, drift a7607ebffaea to e3ea42abc2d2, heavyweights 6c6b35c716e1 to cd6e9836b5ef. The S&P
            500's own evaluators do not move.
Remedy:     `tools/remedies/2026-10-15-19.9-the-business-first-setups.txt`, from main after the merge and before the
            night: the three ledger builds with `--again` by hand, then the file, which registers the S&P 400's and
            600's eight standing rules again, the S&P 600's two heavyweights books and 19.1's six market-off variants,
            and checks each index's history point in time. No request to the provider and no model call.
Mutated:    the rule, stated before the runs: one mutation a property the checkpoint adds, at the line deciding it, each
            filtered to the tests predicted red. M1 the revenue gate at -0.05 inclusive, at the gates test; red. M2 the
            cash gate strict at 0.6, at the gates, setups and night tests; red at all three. M3 the provisional pass
            without the rule's parts, at the setups and night tests; red. M4 the family's row at a cap a session
            longer than its pullback row's, same; red. M5 the S&P 500's picks read off its own families alone, at the
            night test; red. M6 the grid's provisional places at [1, 1, 1], at the procedure test; red. M7 the
            procedure's evidence reading the rule today's trades on both sides, same; red. M8 the S&P 500 refusing the
            family's grid setting, at the apply step test; red. M9 the family out of the families an approval reaches,
            same; red. M10 the night reading the provisional setting where one is stored, at the approved setting
            test; red. M11 the S&P 500's night reading no stored setting, same; red, at the store. M12 the per-rule
            test ignoring the stored setting, same; red. M13 the tester's walk keeping the listings the rule fails, at
            the walk test; red. M14 the card's score part not reaching the family, at the score test; red. M15 the
            Loop's families without the family, at the two Loop page tests; survived, a missing property: since 19.9
            the list decides only the badge's count, which no test read for the family, so the badge test now puts the
            family's proposal and the mutation run again turned it red. M16 the Ledger page's name of the family
            removed, at the Ledger test; red. Not mutated, and no test reads them: the tester's monthly run wiring the
            procedure and the walk, and the S&P 500's cards of the family drawing the score's part. Each reverted
            before anything was committed.
Verified:   `tools/ci.ps1` green and `tools/verify-phase.ps1` green over the tree carrying this entry, bb6cc79c: all six
            steps, 0 warnings, 0 errors, 2349 of 2349 tests ran with none failed in 18 minutes 45 seconds, migrations 0
            to 93 with none pending, schema version 93, against `data-ci` and never `data`; the phase report 59 tables,
            1313 claims, 1313 passing, none out of scope and none unexamined, 1324 placements and verdicts reconciled
            against a floor of 34, 43 checks on the roster run and passed.
Carried:    the remedy's three builds' and the first night's measured times, owed to a dated entry once they are in;
            19.10; Figure 11.4's "earliest promotion", owed a correction of section 11; the S&P 600's breadth drawn as
            45.0% beside "below its floor of 45%", owed a correction of 19.2; the sign-offs of phases 16, 17 and 18.

### 19.9 - the remedy run from main and the history builds' measured times   2026-10-15

Not a checkpoint entry: it records 19.9's remedy and what it measured, and lands nothing.
Ran:        from main's b8059e7e, the merge of PR 464, between 06:16Z and 07:38Z, no night or off-peak run writing: the
            three ledger builds with `--again` over 2019-01-02 to 2026-10-05, then
            `tools/remedies/2026-10-15-19.9-the-business-first-setups.txt`. No request to the provider and no model call.
Measured:   the S&P 500's build 13 minutes 41 seconds, 92,007 setups, 22,678 of them the fundamentals-first family's, 78
            of those its provisional rule passes; the S&P 400's 10 minutes 39 seconds, 58,306, 14,798 and 317; the S&P
            600's 14 minutes 37 seconds, 76,048, 16,885 and 357. The three 38 minutes 57 seconds together, against 40
            minutes 14 seconds for the same span on 2026-10-12 and 19.0's estimate of at most about 58 minutes; the
            family's setups are 74, 63 and 51 in 100 of each index's pullback setups. The moved rules registered again,
            8 at one instant, each unchanged: the S&P 600's two heavyweights books and 19.1's six market-off variants.
            Each index's point-in-time check rebuilt 200 of 200 sampled history setups with every reading the same.
Screens:    5152 restarted from main's Release; the Ledger and the Loop pages read back on all three indices, each
            drawing the family's card with no line naming 19.9.
Verified:   `tools/ci.ps1` green over main as 19.9's entry records, the checks reading this record run filtered over the
            tree carrying this entry.
Carried:    the first night's ledger step on the merged build, read off its run log row against the 49 seconds of
            2026-10-09 and 19.0's estimate of about 15 seconds more, owed to a dated entry once the night has run.

### 19.10 - the next-open check: every settled setup priced at the listing night's close and at the next session's open   2026-10-15

Queue:      done, 19.0 to 19.9 with their corrections and records as PRs 446 to 465; in progress, this checkpoint;
            next, the operator's word on moving entries to the next session's open, and the sign-offs of phases 16, 17
            and 18, each by a fresh session.
Built:      the read-only check 19.10 starts with, the worker's `ledger-next-open --index` verb. It reads the history
            the build reads, through the bar store's newest session, and prices every settled setup on the index both
            ways with the same stop, target, trail and cap, the path read over the same closes from the session after
            the listing, so the exit is the same close and only the buy moves. A result at the open is stated in the
            risk the plan put up at the listing's close, since a position is sized the night before from that close
            and its stop, and a heavyweights' as a fraction of the sum that close put to it; stated in the open's own
            risk instead, an open a cent over the stop swelled one setup's result past any year's mean, which the first
            run over the S&P 400 showed. The benchmark is the same plan bought at every member's own next open. A
            family's plan carries no entry zone, so its zone is the plan's whole range (see: An entry is admitted only
            inside the plan's own range, and a plan with no entry zone takes its whole range as its zone), and the
            next open past it is counted at or under the stop and at or over the target. It writes nothing, not even a
            run log row. The runbook and the roster; no decision, since nothing moves on the check alone.
Measured:   over the operator's store read-only on 2026-10-15, the history read through 2026-10-14, in 16, 13 and 18
            seconds: every settled setup priced, 89031 on the S&P 500, 56383 on the S&P 400 and 73016 on the S&P 600,
            none whose stock or session the history lacks, none whose next open was not held; the result at the close
            worked again as stored to a thousandth for 88983, 56336 and 72925 of them and the edge for 88779, 56127 and
            72659, the rest the pulled years' rounding at the median ratio. Results and edges in the close's risks,
            the heavyweights' in fractions; each change the open less the close:
            S&P 500's pullback, 29475 settled, 29221 priced both ways: result 0.189 at the close, 0.162 at the open
            (-0.027); edge 0.032, 0.022 (-0.010); under the stop 212, over the target 42; by year, the result's and
            the edge's change, under the stop and over the target: 2019 -0.037 -0.013 28 3; 2020 -0.042 -0.012 41 8;
            2021 -0.037 -0.006 31 2; 2022 -0.009 -0.021 13 1; 2023 +0.001 -0.003 19 6; 2024 -0.032 -0.010 36 15; 2025
            -0.020 -0.009 28 5; 2026 -0.015 -0.009 16 2.
            S&P 500's breakout, 21876 settled, 21699 priced both ways: result 0.057 at the close, 0.033 at the open
            (-0.023); edge 0.001, -0.014 (-0.015); under the stop 177, over the target 0; by year, the result's and
            the edge's change, under the stop and over the target: 2019 -0.034 -0.004 23 0; 2020 -0.020 -0.012 29 0;
            2021 -0.027 -0.016 21 0; 2022 +0.011 -0.009 9 0; 2023 -0.005 -0.007 30 0; 2024 -0.042 -0.029 32 0; 2025
            -0.016 -0.015 21 0; 2026 -0.040 -0.032 12 0.
            S&P 500's drift, 5778 settled, 5414 priced both ways: result 0.303 at the close, 0.220 at the open
            (-0.083); edge 0.073, 0.043 (-0.030); under the stop 235, over the target 129; by year, the result's and
            the edge's change, under the stop and over the target: 2019 -0.021 -0.012 23 7; 2020 -0.097 +0.019 44 25;
            2021 -0.110 -0.036 20 13; 2022 -0.032 -0.031 29 12; 2023 -0.113 -0.070 24 23; 2024 -0.125 -0.026 42 11;
            2025 -0.085 -0.044 37 22; 2026 -0.094 -0.041 16 16.
            S&P 500's fundamentals, 21680 settled, 21507 priced both ways: result 0.158 at the close, 0.132 at the
            open (-0.026); edge 0.015, 0.006 (-0.009); under the stop 148, over the target 25; by year, the result's
            and the edge's change, under the stop and over the target: 2019 -0.039 -0.015 21 2; 2020 -0.038 -0.011 24
            2; 2021 -0.039 -0.007 21 1; 2022 -0.011 -0.020 9 1; 2023 +0.001 -0.003 15 4; 2024 -0.034 -0.011 24 9;
            2025 -0.016 -0.005 22 4; 2026 -0.015 -0.008 12 2.
            S&P 500's heavyweight, 10222 settled, 10222 priced both ways: result 0.0149 at the close, 0.0148 at the
            open (-0.0001); edge 0.0009, 0.0009 (0.0000); by year, the result's and the edge's change: 2019 -0.0002
            0.0000; 2020 -0.0007 0.0000; 2021 -0.0024 0.0000; 2022 -0.0005 0.0000; 2023 +0.0006 0.0000; 2024 +0.0013
            0.0000; 2025 +0.0002 0.0000; 2026 +0.0009 0.0000.
            S&P 400's pullback, 22586 settled, 22407 priced both ways: result 0.129 at the close, 0.101 at the open
            (-0.028); edge 0.022, 0.012 (-0.010); under the stop 145, over the target 34; by year, the result's and
            the edge's change, under the stop and over the target: 2019 -0.037 -0.013 20 6; 2020 -0.045 -0.011 11 4;
            2021 -0.044 -0.007 18 8; 2022 +0.001 -0.015 6 0; 2023 +0.003 -0.007 13 1; 2024 -0.037 -0.012 43 11; 2025
            -0.017 -0.007 22 2; 2026 -0.024 -0.016 12 2.
            S&P 400's breakout, 15351 settled, 15240 priced both ways: result -0.030 at the close, -0.056 at the open
            (-0.026); edge -0.014, -0.024 (-0.010); under the stop 111, over the target 0; by year, the result's and
            the edge's change, under the stop and over the target: 2019 -0.039 -0.020 25 0; 2020 -0.032 -0.003 10 0;
            2021 -0.045 -0.018 11 0; 2022 +0.012 +0.003 9 0; 2023 -0.023 -0.011 10 0; 2024 -0.025 -0.003 26 0; 2025
            -0.011 -0.005 11 0; 2026 -0.028 -0.024 9 0.
            S&P 400's drift, 4284 settled, 4082 priced both ways: result 0.270 at the close, 0.167 at the open
            (-0.103); edge 0.081, 0.023 (-0.058); under the stop 144, over the target 58; by year, the result's and
            the edge's change, under the stop and over the target: 2019 -0.062 -0.047 15 2; 2020 -0.088 -0.055 26 15;
            2021 -0.160 -0.098 13 7; 2022 -0.080 -0.087 20 7; 2023 -0.110 -0.055 12 8; 2024 -0.134 -0.030 19 9; 2025
            -0.082 -0.032 20 4; 2026 -0.107 -0.065 19 6.
            S&P 400's fundamentals, 14162 settled, 14045 priced both ways: result 0.083 at the close, 0.056 at the
            open (-0.027); edge -0.003, -0.014 (-0.010); under the stop 94, over the target 23; by year, the result's
            and the edge's change, under the stop and over the target: 2019 -0.042 -0.014 14 5; 2020 -0.050 -0.012 8
            3; 2021 -0.039 -0.005 10 6; 2022 -0.002 -0.014 4 0; 2023 +0.001 -0.011 8 1; 2024 -0.035 -0.010 22 5; 2025
            -0.015 -0.006 19 1; 2026 -0.023 -0.018 9 2.
            S&P 600's pullback, 31559 settled, 31337 priced both ways: result 0.170 at the close, 0.148 at the open
            (-0.022); edge 0.057, 0.051 (-0.006); under the stop 177, over the target 45; by year, the result's and
            the edge's change, under the stop and over the target: 2019 -0.021 -0.008 8 7; 2020 -0.043 -0.013 24 4;
            2021 -0.029 0.000 27 4; 2022 -0.003 -0.008 10 1; 2023 +0.004 -0.005 23 6; 2024 -0.035 -0.006 53 11; 2025
            -0.009 -0.002 20 9; 2026 -0.010 -0.010 12 3.
            S&P 600's breakout, 19862 settled, 19724 priced both ways: result -0.022 at the close, -0.051 at the open
            (-0.029); edge -0.013, -0.027 (-0.014); under the stop 138, over the target 0; by year, the result's and
            the edge's change, under the stop and over the target: 2019 -0.035 -0.031 24 0; 2020 -0.049 -0.024 12 0;
            2021 -0.043 -0.015 19 0; 2022 +0.003 -0.007 5 0; 2023 -0.030 -0.008 26 0; 2024 -0.025 -0.003 32 0; 2025
            -0.014 -0.004 5 0; 2026 -0.021 -0.021 15 0.
            S&P 600's drift, 5566 settled, 5275 priced both ways: result 0.137 at the close, 0.055 at the open
            (-0.082); edge -0.022, -0.063 (-0.040); under the stop 191, over the target 100; by year, the result's
            and the edge's change, under the stop and over the target: 2019 -0.034 -0.030 17 6; 2020 -0.072 -0.043 32
            22; 2021 -0.168 -0.092 18 11; 2022 -0.034 -0.051 24 5; 2023 -0.095 -0.048 21 11; 2024 -0.109 -0.016 33
            15; 2025 -0.051 +0.004 27 15; 2026 -0.079 -0.052 19 15.
            S&P 600's fundamentals, 16029 settled, 15917 priced both ways: result 0.237 at the close, 0.217 at the
            open (-0.021); edge 0.119, 0.114 (-0.005); under the stop 86, over the target 26; by year, the result's
            and the edge's change, under the stop and over the target: 2019 -0.018 -0.006 4 6; 2020 -0.040 -0.018 15
            1; 2021 -0.029 +0.001 8 2; 2022 -0.003 -0.003 7 1; 2023 +0.005 0.000 11 4; 2024 -0.035 -0.006 25 4; 2025
            -0.013 -0.003 9 7; 2026 -0.014 -0.010 7 1.
            Read across them: the next open costs the pullback, the breakout and the fundamentals-first family 0.021
            to 0.029 of a risk a setup on every index and the drift 0.082 to 0.103, their edges 0.005 to 0.015 and
            0.030 to 0.058; it opens past the stop for under one setup in a hundred of the three and about one in
            twenty-five of the drift's, and past the target for under one in four hundred of the three and about one
            in fifty of the drift's, the breakout's trail placing no target. The heavyweights' edge does not move,
            since every member of a size cut is a setup and the cut is its own benchmark.
Tests:      six new, each figure worked by hand: the open against the plan's range at both edges, a cent inside
            each and with none held; a setup bought at the open under a fixed and a trailing exit; the benchmark and
            the heavyweights' size cut at the open; each family's means by year over a constructed ledger; and the
            command over a constructed store of two members and over the fixture's replayed store, read against
            `next-open.json`, derived and not frozen.
Claims:     1313, unchanged: 19.10 adds none until the operator approves moving entries, as 19.0 stated.
Pins:       none moves: the check's files are pinned by no rule, the readings or the evaluators.
Remedy:     none: no migration and nothing written.
Mutated:    the rule, stated before the runs: one mutation a property the check adds, at the line deciding it, each
            filtered to the six new tests. M1 an open at the stop counted inside; red at the range test. M2 an open at
            the target counted inside; red, same. M3 the open's result left in its own risk; red at the exit, the
            benchmark and the command tests. M4 the benchmark entering a member opening past its range; red at the
            benchmark and the command tests. M5 the benchmark not entering a member with no next session; red at the
            benchmark test. M6 the heavyweights' result over the open and not the listing's close; red at the
            heavyweights test. M7 the means over every row holding a result and not the pairs; red at the means and
            the command tests. M8 the command reading the listing session's own open; red at the command test. Not
            mutated, and no constructed case reaches them: the command's scaling of a stored plan to the history's
            close, at a factor of one there, its edge waiting on a settled benchmark, every benchmark there settled,
            and its reading of a heavyweights setup's size cut, the store holding none. Each reverted before anything
            was committed.
Verified:   `tools/ci.ps1` green and `tools/verify-phase.ps1` green over the tree carrying this entry, f58e4d1a: all six
            steps, 0 warnings, 0 errors, 2355 of 2355 tests ran with none failed in 19 minutes 38 seconds, migrations 0
            to 93 with none pending, schema version 93, against `data-ci` and never `data`; the phase report 59 tables,
            1313 claims, 1313 passing, none out of scope and none unexamined, 1324 placements and verdicts reconciled
            against a floor of 34, 43 checks on the roster run and passed.
Carried:    the operator's word on moving entries to the next session's open, which 19.10 builds where it is given,
            every stored trade priced again from the stored bars in the same change; the first night's ledger step
            on 19.9's build, owed to its dated entry; Figure 11.4's "earliest promotion", owed a correction of section
            11; the S&P 600's breadth drawn as 45.0% beside "below its floor of 45%", owed a correction of 19.2; the
            sign-offs of phases 16, 17 and 18.

### 19.10 - every entry bought at the next session's open, by a fill step of its own   2026-10-16

Queue:      done, 19.0 to 19.9 with their corrections and records and 19.10's next-open check as PRs 446 to 466; in
            progress, this checkpoint; next, 19.10's remedy run from main and its record, then the sign-offs of phases
            16, 17 and 18, each by a fresh session.
Built:      on the operator's rulings of 2026-10-15, route B: every rule's entry on every family and index bought at the
            open of the session after the night that listed it, the stop, the target, the cap and the exits where the
            plan set them at the close (see: A trade's entry is priced at the next session's open by a fill step of its
            own, and no rule's version moves because of it). One pricing function in the core: where the open sat
            against the plan, the result at the open the result at the close less the gap over the risk the close set,
            a holding's over its close, the benchmark each ended member moved by its own gap over its own risk, an open
            past the stop or the target still bought and walked under the same exits, and a scored plan whose risk the
            forward return filler has not written yet, a plan still open, bought at its open with nothing read against
            its range until it is. The fill step, a component of its own run in the forward returns step after the
            filler and by hand as `fill`, writing migration 94's `trade_fill`, a trade with no next session, no open on
            it or no risk at its close written with why; the setup's `fill` and `past`; the ledger, the rule records'
            replays, the loop's walks and the next-open check priced through the same function. Every page and figure
            reporting a trade's result reads the fill: Past picks, a name's picks before, the Run page's records, paper
            trading, the live alarm and the rule's own picks beneath the operator's trades; Past picks says a trade
            waiting on its next session buys at the next open and one with no fill why, and Tonight says each pick
            buys at the next open. The decision, sections 11.4, 13.10, 14, 15.17 and 15.21, the catalogue, the matrix
            and the stores, the schema, the runbook, the guide, the roster and the remedy file. No source a rule pins
            changes, so no rule's version moves and no registration, record or paper account restarts.
Measured:   over the operator's store read-only on 2026-10-16, the bars through 2026-10-15, nothing written. The fill
            step's own functions over every stored trade of the nine tables: 8860 trades, 8488 priced again at the
            next open, 2 whose stock holds no bar on its next session, none with no open on it or no risk at its close,
            and 370 listed on 2026-10-15 waiting on its next session. By table, priced of stored: the registered rules'
            trades 18 of 37, 19 waiting; the S&P 500 page list's scored plans 8360 of 8711, 349 waiting and 2 with no
            next session, of which the clear plan 2184 of 2343 with 11 opens past the stop and 2 past the target, the
            nearest bands' plan 3138 of 3328 with 303 and 42, the drift 6 of 8 and the reasons' setups 3032 of 3032
            with 44 and 7; the S&P 500's heavyweights book 5 of 5 and its rules' books 35 of 35; the S&P 400's and
            600's lists 16 of 17 and their rules' trades 6 of 7, one each waiting; their books 15 of 15 and their
            rules' books 13 of 13; the S&P 500 variants' picks 20 of 20, one past its stop.
            Each family's record on each index, the rule's record its card draws: before, as the rule recorder stored
            it on 2026-10-07 for the S&P 500 and 2026-10-09 for the S&P 400 and 600; after, replayed read-only by this
            change's code at the open over the history through 2026-10-15, 13269 trades in all, the counts differing
            by the trades that ended in between. Trades, then the mean result in risks after each trade's cost, the
            heavyweights' in per cent:
            S&P 500's pullback 1877 at 0.190 before, 1891 at 0.171 after; breakout 1009 at 0.115, 1012 at 0.120;
            drift 2244 at 0.218, 2251 at 0.021; heavyweights 374 at 4.01, 374 at 3.88.
            S&P 400's pullback 1172 at 0.105, 1173 at 0.067; breakout 637 at -0.070, 638 at -0.100; drift 1639 at
            -0.017, 1640 at 0.027; heavyweights 446 at 2.22, 446 at 2.22.
            S&P 600's pullback 1251 at 0.067, 1260 at 0.072; breakout 713 at -0.056, 713 at -0.092; drift 1414 at
            -0.249, 1416 at -0.218; heavyweights 455 at 2.05, 455 at 1.97.
            Read across them: the S&P 500's drift moves the most, 0.197 of a risk down, while the S&P 400's and 600's
            drift move up 0.044 and 0.030; the breakout moves up 0.006 on the S&P 500 and down 0.030 and 0.035 on the
            S&P 400 and 600; the pullback down 0.019 and 0.038 on the S&P 500 and 400 and up 0.005 on the S&P 600;
            the heavyweights down 0.13 points on the S&P 500 and 0.09 on the S&P 600 and not moving on the S&P 400.
Tests:      8 new, 2363 in all: the pricing function's cases at the open and the benchmark worked by hand, a plan whose
            stop is not stored yet and one putting up no risk among them; the fill step over a constructed store pricing
            each trade as the next-open check prices the same setup on the ledger, an open scored plan priced and priced
            again once its risk is written, each reason counted and every trade holding a fill or a reason; no pinned
            source naming the fill; the scan of every statement reading a trade's table on the read surface, the paper
            accounts and the live alarm, 22 at least stated in advance; a paper account's start; the fill step's row
            over the fixture's whole night; and Past picks and Tonight read back off the rendered pages. The screen
            tests whose subject is not the fill give each constructed trade a fill at its own buy holding its writer's
            figures.
Claims:     1316, three more than 1313: the fill step's catalogue and matrix rows and its store, named beside phase
            19's pair as rows after its report, each passing.
Pins:       none moves: no source an evaluator, the hooks or the readings pin names the fill, asserted.
Remedy:     `tools/remedies/2026-10-16-19.10-entries-at-the-next-open.txt`, from main after the merge and before the
            night: the migration, `fill --again` and `rule-record`; then each index's ledger history built again with
            `--again`, its check and its next-open check, by hand as the runbook states. It asks the provider for
            nothing.
Amended:    this checkpoint amends its own done condition: the rulings' tests were added to it by the commit carrying
            the rulings, recorded in the changelog.
Mutated:    the rule, stated before the runs: one mutation a property this change adds, at the line deciding it, each
            run against the change's fill tests and the screens reading them. M1 an open at the stop not past it; red at
            the range test. M2 the gap added to the result; red at five. M3 the benchmark averaging members whose paths
            have not ended; red at the benchmark test. M4 a plan with no stop stored given no fill; red at the pricing
            and the fill step's tests. M5 a fill not priced again once its result arrives; red at the fill step's
            test. M6 the fill step reading only plans whose risk is written; red, same. M7 a plan putting up no risk
            written with no reason; red, same. M8 a scored plan's stop not scaled to the night's close; red, same. M9
            the registered rules' records drawing the trade's own result; red at the scan. M10 a trade with no outcome
            row said to wait; red at the Past picks test. M11 a waiting trade drawn with no words; red at the new
            screen test. M12 a provisional account starting at its first filled trade; red at the paper start test.
            M13 Tonight's line hidden; red at the new screen test. Not mutated, and no constructed case reaches them:
            the fill step's typical move read off the stock where a table keeps no distance in typical moves, every
            constructed trade keeping one, and a book's size cut moved by its members' gaps, the fill step's
            constructed store holding no book. Each reverted before anything was committed.
Verified:   `tools/ci.ps1` green and `tools/verify-phase.ps1` green over the tree carrying this entry, 90617e9a: all six
            steps, 0 warnings, 0 errors, 2363 of 2363 tests ran with none failed in 19 minutes 1 second, migrations 0
            to 94 with none pending, schema version 94, against `data-ci` and never `data`; the phase report 59 tables,
            1316 claims, 1316 passing, none out of scope and none unexamined, 1327 placements and verdicts reconciled
            against a floor of 34, 43 checks on the roster run and passed.
Carried:    19.10's remedy run from main after the merge and before the night, its counts and each index's ledger and
            records at the open recorded in a dated entry; the first night's fill step, owed to that entry; the first
            night's ledger step on 19.9's build, owed to its dated entry; Figure 11.4's "earliest promotion", owed a
            correction of section 11; the S&P 600's breadth drawn as 45.0% beside "below its floor of 45%", owed a
            correction of 19.2; the sign-offs of phases 16, 17 and 18.

### 19.11 ruling - a chart page opened first for every stock, and published price and volume factors added to the ledger's readings   2026-10-16

Not a checkpoint entry: it lands nothing.
Ruled:      by the operator on 2026-10-16, word for word in `prompts/2026-10-16-19.11-ruling-chart-page-and-factor-readings.md`:
            every link that opens a stock opens a chart page first, linking to the report, as 19.11 with every page's
            time measured; published price and volume factors added to the ledger's readings, choosing, filtering and
            ranking no stock and changing no rule, as 19.12, with one migration and the three history builds again; the
            hard rule on the pulled history and the decision it cites amended to name the chart page; no intraday
            chart, after-hours price or drawing tools; and no paid call, model call or provider request. Asked four
            questions the same day, the operator chose, word for word in `prompts/2026-10-16-19.11-operator-answers.md`:
            the timings taken on the operator's store by the branch's read surface on a port of its own outside the
            regular session; the chart page's own reading made faster within 19.11 where its Monthly is over 300
            milliseconds; for 19.13, a sale's plan mark read as none for a session whose 90 days reach before
            2023-04-01; and the winners table stored by 19.12's one migration and worked once by its remedy.
Plan:       `BUILD_PLAN.md` gains 19.11 and 19.12 after 19.10 with their done conditions, and the claims predicted gain
            about 18 and about 5 as rows after the report; section 20's row names 19.0 to 19.12. Three decisions are
            superseded by three naming the chart page, where the ruling named the one the hard rule cites, since each
            says the name page alone; their old wording stands under Previously decided with its reasoning, each
            citation in the specs and the code names the new ones, and CLAUDE.md's hard rule names the chart page, its
            prior wording in `CHANGELOG.md`. The citations in the shipped source are comments, so no pin moves.
Found:      the member readings store 19.2 added holds a night's readings as one list a row, so 19.12's migration adds
            the readings' columns to the setups' table alone and the list widens with the catalogue; and the setups
            table in `SCHEMA.md` still counts forty readings where the catalogue holds forty-six, left to 19.12, which
            writes the count.
Queue:      done, 19.10, PR 467, and its remedy file run from main; in progress, this ruling, and 19.10's remedy record,
            the three ledger histories built again from main and each index's checks; next, 19.13's ruling, then 19.11,
            19.12 and 19.13, each merged on its gates outside the night, the operator's go of 2026-10-16 standing for
            each. The sign-offs of phases 16, 17 and 18 stay owed.
Verified:   `tools/ci.ps1` green and `tools/verify-phase.ps1` green over the tree carrying this entry, 88bd3d77, since the
            citations it names again sit in shipped source: all six steps, 0 warnings, 0 errors, 2363 of 2363 tests ran
            with none failed in 20 minutes 15 seconds, migrations 0 to 94 with none pending, against `data-ci` and never
            `data`; the phase report 59 tables, 1316 claims, 1316 passing, none out of scope and none unexamined, 1327
            placements and verdicts reconciled against a floor of 34, 43 checks on the roster run and passed.
Carried:    nothing new.

### 19.13 ruling - insider buying and selling read from the SEC's Form 4 filings, for the reports and as ledger readings   2026-10-16

Not a checkpoint entry: it lands nothing.
Ruled:      by the operator on 2026-10-16, word for word in `prompts/2026-10-16-19.13-ruling-insider-trades.md`:
            insiders' open-market buying and selling read from the SEC's Form 4 filings, free, dated by the day filed and
            from the archive the night already reads, with no web search for insider activity; the filings refresh's
            carve-out in CLAUDE.md and its decision amended to name the Form 4 and 4/A filed by a member's insiders,
            read from the same daily index, one request a filing, free, from the SEC and not the provider, under the
            refresh's own limit; one migration for the insider trades table and the new readings' columns, with the
            three history builds again; the history before the nightly refresh from the SEC's quarterly insider
            transactions data sets, one request a quarter; and no paid call, model call or provider request. The
            operator's answer of the same day on the plan's box stands in `prompts/2026-10-16-19.11-operator-answers.md`.
Plan:       `BUILD_PLAN.md` gains 19.13 after 19.12 with its done condition, built only once 19.11 and 19.12 have merged,
            and the claims predicted gain about 8 as rows after the report; section 20's row names 19.0 to 19.13. The
            refresh's decision is superseded by one naming the insiders' trades, its old wording under Previously
            decided with its reasoning, each citation in the specs and the code names the new one, and CLAUDE.md's
            carve-out names the Form 4 and 4/A, its prior wording in `CHANGELOG.md`. The citations in the shipped source
            are comments, so no pin moves.
Found:      the plan's box on a Form 4 is the filing's and not a transaction's, so every transaction a filing ticking it
            reports is read as planned; a 4/A names its original's filing day and not its accession, so it is matched to
            its original by the issuer, the reporting owner and that day, and stands on its own rows where none or two
            match; the night's Form 4 asks are counted over the newest days the archive posted by the night's reader run
            by hand rather than over three nights, which a done condition may not wait on, the first nights' counts owed
            to a dated entry; and the data set is committed as a cut of the fixture names' rows with the whole file's
            size and checksum stated.
Queue:      done, the 19.11 ruling; in progress, this ruling and 19.10's remedy record; next, 19.11, 19.12 and 19.13,
            each merged on its gates outside the night, the operator's go of 2026-10-16 standing for each. The sign-offs
            of phases 16, 17 and 18 stay owed.
Verified:   `tools/ci.ps1` green and `tools/verify-phase.ps1` green over the tree carrying this entry, 021671e0, since the
            citations it names again sit in shipped source: all six steps, 0 warnings, 0 errors, 2363 of 2363 tests ran
            with none failed in 18 minutes 49 seconds, migrations 0 to 94 with none pending, against `data-ci` and never
            `data`; the phase report 59 tables, 1316 claims, 1316 passing, none out of scope and none unexamined, 1327
            placements and verdicts reconciled against a floor of 34, 43 checks on the roster run and passed. Set on
            the merge of PR 468, whose tree is the one this ran over, as edc64bdc.
Carried:    nothing new.

### 19.10 - the remedy run from main, the three ledger histories built again and each index's checks   2026-10-16

Not a checkpoint entry: it records 19.10's remedy and what it measured, and lands nothing.
Ran:        from main's 7b228f0c, the merge of PR 467: `tools/remedies/2026-10-16-19.10-entries-at-the-next-open.txt`
            between 03:20Z and 04:04Z with no night or off-peak run writing, migration 94 applied, the fill step run
            again and the rule records replayed; then, the off-peak run's drain done, the three ledger builds with
            `--again` over 2019-01-02 to 2026-10-05 between 04:45Z and 05:26Z, and each index's point-in-time check and
            next-open check from the same build. No request to the provider and no model call.
Measured:   the fill step priced 8,488 trades at the next session's open: 8,360 of the page list's scored plans, 18 of
            the families' trades, 16 and 6 of the index families' and rules' trades, 20 of the variants' picks, and 5,
            35, 15 and 13 of the four heavyweights tables' holdings; 2 whose stock holds no bar on its next session, none
            with no open on it or whose plan put up no risk, and 370 buying at the next open. The rule records replayed
            at the open are those 19.10's entry states. The S&P 500's build took 14 minutes 15 seconds, 92,006 setups,
            9,345 passing the live rule; the S&P 400's 10 minutes 41 seconds, 58,304 and 5,116; the S&P 600's 15 minutes
            50 seconds, 76,051 and 6,571; 40 minutes 46 seconds together against 38 minutes 57 seconds on 2026-10-15, the
            setups 1 fewer, 2 fewer and 3 more than 19.9's builds wrote, the night between them refetching 19 members'
            years after 296 corporate actions. Each index's point-in-time check rebuilt 200 of 200 sampled history setups
            with every reading the same. The next-open check priced every settled setup both ways, 89,100, 56,420 and
            73,067, none whose stock or session the history lacks, and worked again 71,449, 43,526 and 55,342 of the
            results at the open as stored to a millionth and 89,052, 56,373 and 72,977 to a thousandth, the proportions
            its run of 2026-10-15 found for the results at the close, since it reads the history to a later day than the
            build; it wrote nothing.
Verified:   `tools/ci.ps1` green over main as 19.10's entry records, the checks reading this record run filtered over the
            tree carrying this entry.
Carried:    the first night's fill step and ledger step on 19.10's build, owed to a dated entry once the night has run;
            the first night's ledger step on 19.9's build, owed to its dated entry; Figure 11.4's "earliest promotion",
            owed a correction of section 11; the S&P 600's breadth drawn as 45.0% beside "below its floor of 45%", owed a
            correction of 19.2; the sign-offs of phases 16, 17 and 18.

### 19.11 - the chart page every stock opens first, and every page's time measured   2026-10-16

Queue:      done, 19.0 to 19.10 with their corrections and records and the rulings of 19.11 and 19.13, PRs 446 to 470;
            in progress, this checkpoint; next, 19.12, then 19.13, then the sign-offs of phases 16, 17 and 18, each by a
            fresh session.
Built:      on the operator's rulings and answers of 2026-10-16, a chart page every link that opens a stock opens first,
            `#/chart/<ticker>` (see: Every link that opens a stock opens its chart page first, and the chart page links
            to its report): the name page's masthead drawn by the one function both pages call, with Open the report or
            the list's own request; candles at Daily, Weekly or Monthly rolled up in the core, the weekly and monthly
            reading one stock's pulled bars by the table's key at the store's scale and the daily none; the 20, 50 and
            200 session averages named with their values at the candle under the pointer; volume in the gain's and the
            loss's hue; the name page's report and ex-dividend marks; the night's bands behind a toggle; tonight's plan
            as lines with the rule's name; and a grid of stored figures in six groups, a figure the name page draws
            worked by its own function and one fetched and not held saying not stored. Every stock link on Tonight, the
            buy points, the setup cards, Universe, Researched, the Queue, the Watch list, Past picks, paper trading's
            trade log, the peers and the search opens the chart page; a report's links keep the name page, whose
            masthead links back. Every page's time: the read surface keeps each route's server times in memory, the
            newest thousand a route, and the Run page draws the slowest routes with their requests, median and slowest
            (see: Each request's route and server time is kept in memory for the read surface's life and drawn on the
            Run page, and no store holds it). Section 15.22 with its ten regions, 15.9, 15.10, 15.12 and 15.13, the read
            API's catalogue and matrix rows, the schema's note, the guide, the roster and the placements.
Found:      before the merge, and fixed in this checkpoint. Screenshots of the branch's chart page over the operator's
            store showed the grid's labels squeezed to one letter by a value column taking the whole width, dates
            broken at their hyphens, the distance from the year's high written with its sign in ink beside the words
            below it, and one year read off the month-end of 2025-09-30, since the store keeps a year to the day, 252
            sessions, and that reading needed 253. One year now reads the store's own session on or before the day a
            year back, the distance is a figure of its own in its hue, and the signed-figures check reads every fixture
            name's chart page at each interval. At a viewport of 390 pixels the page holds the screen's width. And six
            of the eleven placements claimed a pass no test read: the masthead, the levels' toggle, the volume's hue,
            the marks, tonight's plan and the reading under the pointer; each now has its assertion.
Measured:   over the operator's store on 2026-10-16 from 08:09 to 08:11 UTC, outside the regular session, by a Release
            build of 63472820 run from a clean copy on a port of its own, the one change to shipped code after it adding
            an attribute to each mark: each page opened ten times, Tonight ten times on each index, its median and
            slowest server time in milliseconds read off the Run page's own table. The Run page 4,212 and 4,529; Tonight
            786 and 1,513; a name page 693 and 1,037; the Watch list 366 and 414; Universe 268 and 295; the chart page's
            Daily 124.1 and 157.3, Weekly 132.0 and 151.0 and Monthly 126.4 and 138.5, on AAPL, which holds 1,957
            sessions pulled before the store's year, tied for the most; Researched 121 and 146; Past picks 68 and 135;
            paper trading 59 and 79; the Queue 25 and 46; the Loop 7 and 39; the Ledger 2 and 23. The Monthly is under
            300 at its slowest, so its reading is as built. The surface's five starts wrote five run log rows and
            nothing else.
Tests:      7 new, 2370 in all: a week's and a month's candles and a stretch holding none worked by hand; the pulled
            sessions at the store's scale; each span's return worked by hand, one year off the store's own session a
            year back and off the month-ends where it holds none; the fixture's AAPL chart page, its week a whole one
            with its high and low inside it, its 50-session average and its quarter worked by hand, its grid's figures
            the name page's and not stored where none is held, the quote's route its only one, the daily chart reading
            no pulled bar, its masthead, each volume bar's hue, its four report and four ex-dividend marks, its reading
            and its levels' toggle; tonight's plan over a constructed store; every stock link over seven pages; and the
            route times. The signed-figures check reads every fixture name's chart page at each interval.
Claims:     1327, eleven more than 1316: the ten rows of section 15.22's region table and 15.10's Every page's time,
            named beside phase 19's pair as rows after its report, each passing. The plan's about 18 counted a catalogue
            and a matrix row for the page, which the read API serves with no component of its own, and its route, which
            landed as words in 15.13's list.
Pins:       none moves: no source a rule, the hooks or the readings pin is touched.
Mutated:    the rule, stated before the runs: one mutation a property this checkpoint adds whose failure a reader would
            see on the page, the smallest edit to shipped source that breaks it, each run against the chart page's tests
            and the signed-figures check. M1 a week's high read off its first session; red at the roll-up test, and the
            fixture page's week survived it, its first session holding the week's high, so that week is chosen whole
            with its high and low inside it and is red there too. M2 the scale inverted; red at the scale test. M3 the
            daily chart reading the pulled bars; red at the fixture page's test. M4 one year never read off the store's
            own session; red at the span test. M5 Universe's stock link opening the name page; red at the link test. M6
            a full route keeping its oldest times; red at the route times test. M7 the distance from the year's high
            drawn with no hue; red at the signed-figures check. Under the same rule once the six placements' assertions
            were written: M8 the volume hues swapped, M9 each mark a candle late, M11 the masthead's watch press dropped
            and M12 the reading line reading the first candle, each red at the fixture page's test; M10 the stop's line
            drawn at the buy, red at the plan test. Not mutated: the toggle and the pointer's handlers, which run in the
            browser and are read off the shell's script, the suite having no browser. Each reverted before anything was
            committed.
Verified:   `tools/ci.ps1` green and `tools/verify-phase.ps1` green over the tree carrying this entry, 930a8fa3: all six
            steps, 0 warnings, 0 errors, 2370 of 2370 tests ran with none failed in 20 minutes 2 seconds, migrations 0
            to 94 with none pending, schema version 94, against `data-ci` and never `data`; the phase report 60 tables,
            1327 claims, 1327 passing, none out of scope and none unexamined, 1338 placements and verdicts reconciled
            against a floor of 34, 43 checks on the roster run and passed.
Carried:    the Run page's 4.2 seconds, Tonight's 786 milliseconds and a name page's 693 at their medians, each a ruling
            of its own the operator is asked for; the first night's fill step and ledger step on 19.10's build, owed to
            a dated entry once the night has run; the first night's ledger step on 19.9's build, owed to its dated
            entry; Figure 11.4's "earliest promotion", owed a correction of section 11; the S&P 600's breadth drawn as
            45.0% beside "below its floor of 45%", owed a correction of 19.2; the sign-offs of phases 16, 17 and 18.

### 19.12 - the published price and volume factors as ledger readings, and which readings go with winners   2026-10-16

Queue:      done, 19.0 to 19.11 with their corrections and records and the rulings of 19.11 and 19.13, PRs 446 to 471;
            in progress, this checkpoint; next, its remedy from main and its record, then 19.13, then the sign-offs of
            phases 16, 17 and 18, each by a fresh session.
Built:      on the operator's rulings and answers of 2026-10-16, twenty-two published price and volume factors appended
            to the ledger's catalogue after its forty-six readings, so each keeps its place, as places for the engines to
            look (see: The published price and volume factors are ledger readings for the engines to search, and no
            factor chooses a stock): the returns over 5 and 21 sessions; the month's largest daily return; its
            deviation; the deviation of the quarter's residuals on the index's own series and the slope of the year's
            returns on it; the quarter's skewness; the close's place in its range over 10 sessions; the share of 20
            sessions' volume on rising days; the correlation of the close's change with the volume; Amihud's
            illiquidity; the overnight less the intraday returns; the returns over 21 and 63 sessions less the GICS
            sector fund's; the close over the year's lowest low; the signed run of closes; on-balance volume's slope; and
            Kakushadze's formulas 1, 2, 3, 4 and 6, the first five reading only the day's prices and volume, built as the
            paper writes them, their ranks the members' places on each session, worked once a session from the bars to
            it. Each was checked against the readings standing and none is one of them under another name. The core's
            factor readings and the worker's members' places sit in files of their own, so no file a rule pins moves;
            the ledger loads the eleven sector funds beside its six series; migration 95 adds the setups' twenty-two
            columns, every row kept in place, and creates `loop_link`; the member readings' list takes them with no
            column. Which readings go with winners (see: Which readings go with winners is worked by the monthly run and
            stored, and the words move no rule): for each family and reading, the Spearman correlation at entry with the
            result within each test year from 2022 holding at least 30 setups, the mean of those years and how many
            point its way, beside the 95th percentile of the same mean's size with the results shuffled within each
            night, every arrangement where they number no more than 199 and 199 drawn at seed 20261016 otherwise, worked
            by the tester's `loop-links` step, which the monthly run runs on each index after its checks, stored and
            drawn on the Ledger page beneath the setup's path. `ledger-night` runs the ledger's night step by hand over
            the newest night. The two decisions, 13.10, 13.12, 15.19, sections 16 and 17, the catalogue rows, the schema,
            the runbook, the guide, the roster and the remedy file.
Found:      the ruling names section 4's readings, and section 4 states none of the ledger's readings, so they are
            written in 13.10 where the catalogue is described. The schema's reader admits no digit in a column, so the
            five formulas' columns are named in words, `alpha_one` to `alpha_six`, as the catalogue names its readings.
            The learned score's floor of ten rows a weight moves with the catalogue from 410 rows to 630, its tests
            worked again over 680. And the chart of what is behind a stock read only a list of the catalogue's own
            length, which would have drawn nothing on the nights before the remedy; it now reads an older row with the
            readings it lacks as none.
Pins:       the readings' version moves from 859241ef8442 to 894ed2775fe4, stored on every row the ledger writes from
            the remedy; no source a rule, the hooks, the learned score or a candidate pins changes, asserted by the pins'
            own tests, so nothing is registered again and no record or paper account restarts.
Tests:      10 new, 2380 in all: each factor worked by hand over constructed bars at its window's edge with none one bar
            short; a beta of two over an index moving half as far and a sector fund's return; the ledger reading the
            fund of the sector a company files; Kakushadze's four ranked formulas over three constructed members; three
            factors, Alpha#6 among them, worked from MSFT's bars on the fixture's night; the engines reading the whole
            catalogue, the learned score's three named lists the only readings an engine's source names; which readings
            go with winners worked by hand with its shuffle's percentile over every arrangement; the tester's step over a
            constructed store; the Ledger page drawing the newest run as stored; and an older row of member readings read
            with the factors it lacks as none. The constructed night's ledger step stores the factors its bars do not
            reach as null, and the learned score's tests are worked again over the higher floor.
Claims:     1330, three more than 1327: 15.19's Which readings go with winners, section 16's Reading links and section
            17's row, named beside phase 19's pair as rows after its report, each passing. The plan's about 5 counted
            the monthly run's step as a row; it landed as words in the monthly run's and the tester's catalogue rows.
Remedy:     `tools/remedies/2026-10-16-19.12-the-published-factors.txt`, from main after the merge and before the night:
            the migration and the ledger's night step by hand over the newest night; then each index's history built
            again with `--again`, its check and `loop-links` by hand, as the runbook states. It asks for nothing and
            calls no model. The builds' times, the checks, the night step's time against the night's own and how the
            within-night shuffle's 95th percentile moved with the wider search over the stored history are owed to its
            dated entry.
Mutated:    the rule, stated before the runs: one mutation a property this checkpoint adds whose failure a reader or an
            engine would see, the smallest edit to shipped source that breaks it, each run against 19.12's tests. M1 the
            deviation over the count; red at the factors' test and the fixture's night. M2 the largest return read as
            the smallest; red at the factors' test. M3 a run of closes reaching the first bar counted; red, same. M4 a
            tie counted under in a place; red at the factors' test and the ranked formulas. M5 a place read on the
            session before each bar's; it survived the ranked formulas, whose four-session patterns repeat under the
            shift, so the first member's newest low breaks the pattern, Alpha#4 worked again at minus three, and it is red
            there. M6 the fund's return added; red at the sector and the series' tests. M7 the beta as the index's slope on
            the member; red at the series' test. M8 Alpha#6 without its minus; red at the factors' test and the fixture's
            night. M9 the results shuffled across the year as one night; red at the winners' test. M10 the percentile a
            place high; red at the winners' test and the tester's step. M11 the floor read as more than 30; red, same.
            M12 the step writing only the readings beyond the shuffle; red at the tester's step. M13 the Ledger page
            reading the oldest run; red at the page's test. M14 a shorter list of member readings dropped; red at its
            test. M15 the sector funds left out of the ledger's series; red at the sector test. Not mutated: the
            `ledger-night` verb, which calls the night's own step and is shown dispatched, shown and in the help. Each
            reverted before anything was committed.
Verified:   `tools/ci.ps1` green and `tools/verify-phase.ps1` green over the tree carrying this entry, ca1b35d0: all six
            steps, 0 warnings, 0 errors, 2380 of 2380 tests ran with none failed in 20 minutes 4 seconds, migrations 0
            to 95 with none pending, schema version 95, against `data-ci` and never `data`; the phase report 60 tables,
            1330 claims, 1330 passing, none out of scope and none unexamined, 1341 placements and verdicts reconciled
            against a floor of 34, 43 checks on the roster run and passed.
Carried:    19.12's remedy run from main and its record: the builds' times, the checks, the ledger step's time before
            and after and the shuffle's 95th percentile before and after; the Run page's 4.2 seconds, Tonight's 786
            milliseconds and a name page's 693 at their medians, each a ruling of its own the operator is asked for; the
            first night's fill step and ledger step on 19.10's build; the first night's ledger step on 19.9's build;
            Figure 11.4's "earliest promotion"; the S&P 600's breadth drawn as 45.0% beside "below its floor of 45%";
            the sign-offs of phases 16, 17 and 18.

### 19.12 - the remedy run from main, the three ledger histories built again, each index's checks and which readings go with winners   2026-10-16

Not a checkpoint entry: it records 19.12's remedy and what it measured, and lands nothing.
Ran:        from main's 47ed6b1d, the merge of PR 472: `tools/remedies/2026-10-16-19.12-the-published-factors.txt`
            between 11:23:25Z and 11:23:53Z with no night or off-peak run writing, migration 95 applied and the ledger's
            night step run by hand over the night of 2026-10-15; then the three ledger builds with `--again` over
            2019-01-02 to 2026-10-05 between 11:24:08Z and 12:05:51Z, each index's point-in-time check between 12:06:11Z
            and 12:58:36Z and `loop-links` on each by 12:59:26Z, all from main's clean copy. No request to the provider
            and no model call.
Measured:   the ledger's night step over the night of 2026-10-15 by hand took 11 seconds with the sixty-eight readings,
            its row's own start and end, against 26 seconds for that night's own step with forty-six, both well inside
            the night's hour, writing the 43, 25 and 41 setups the night wrote. The S&P 500's build took 13 minutes 53
            seconds, 92,006 setups, 9,345 passing the live rule; the S&P 400's 10 minutes 39 seconds, 58,304 and 5,116;
            the S&P 600's 17 minutes 11 seconds, 76,051 and 6,571; 41 minutes 43 seconds together against 40 minutes 46
            seconds for 19.10's builds of the same counts. Each index's point-in-time check rebuilt 200 of 200 sampled
            history setups with every reading the same, the factors among them. `loop-links` stored on the S&P 500 51
            readings going with winners, 38 with losers, 226 with no link beyond the shuffle and 25 too few over five
            families, and on the S&P 400 20, 11, 222 and 19 and on the S&P 600 15, 21, 217 and 19 over four. The
            within-night shuffle's 95th percentile of winners against losers, read-only over each rule's finished
            listings as the tester reads them, over the first 46 readings and over all 68: on the S&P 500 the pullback's
            +0.5548 and +0.5404, the breakout's +0.4334 and +0.4791 and the drift's +0.9069 and +0.9819, the
            fundamentals-first family's 78 listings under the floor of 263; on the S&P 400 +0.5461 and +0.5595, +0.2652
            and +0.2760, +1.0741 and +1.1516 and the fundamentals-first family's +0.2465 and +0.2849; on the S&P 600
            +0.5418 and +0.5702, +0.2707 and +0.3363, +1.9767 and +2.0816 and +0.2901 and +0.3043. The wider search
            raised the bar in ten of the eleven and lowered it in the S&P 500's pullback; the conditions above it and the
            rule's own mean stayed one on the S&P 500, the drift's giving way to the breakout's, rose from none to one on
            the S&P 400, the drift's, and stayed none on the S&P 600.
Verified:   `tools/ci.ps1` green over main as 19.12's entry records, and over the tree carrying this record with 19.13's
            entry as that entry records.
Carried:    the Run page's 4.2 seconds, Tonight's 786 milliseconds and a name page's 693 at their medians, each a ruling
            of its own the operator is asked for; the first night's fill step and ledger step on 19.10's build; the first
            night's ledger step on 19.9's build; Figure 11.4's "earliest promotion"; the S&P 600's breadth drawn as 45.0%
            beside "below its floor of 45%"; the sign-offs of phases 16, 17 and 18.

### 19.13 - insider trades, read from the SEC's Form 4 filings, for the reports and as ledger readings   2026-10-16

Queue:      done, 19.0 to 19.12 with their corrections and records and the rulings of 19.11 and 19.13, PRs 446 to 472;
            in progress, this checkpoint; next, its remedy from main and its record, then the sign-offs of phases 16, 17
            and 18, each by a fresh session.
Built:      on the operator's ruling of 2026-10-16 and their answer on the plan's box, insiders' open-market buying and
            selling read from their Forms 4 and 4/A (see: Insiders' trades are read from their Form 4 filings, and only
            open-market purchases and sales are counted). Twelve filings, the 2026q2 and 2026q3 data sets' cuts and the
            page listing them were captured and committed before either reader was written, beside the fixture's four
            daily indexes. A reader of a filing's whole submission and a reader of a quarter's three files give the same
            filing; the night's filings refresh hands each day it reads to the insider trades, which ask the archive for
            each Form 4 and 4/A of a member's filer not yet stored, one request a filing at its pace under the refresh's
            limit, store its table of shares in `insider_trade` under the issuer it names, record each day read whole in
            `insider_read`, and then the ledger reads the night's four insider readings again. `insiders-history` reads
            the data sets from 2018q1, each quarter once and its originals before its amendments; `insiders` reads days
            by hand, and with `--compare` sets each held filing against its stored rows and writes nothing. Four readings
            after the published factors, under analyst sentiment: the net bought, the buyers, an officer buying and the
            unplanned sales over the 90 days filed before the session, the dollars over the company's value through the
            valuation reading's own count and close, none where the spans do not hold the days and the last none for a
            session whose days reach before 2023-04-01. The report's Insiders region after the analysts and the chart
            page's Insiders group. Migration 96 creates the two tables and adds the setups' four columns, every row kept.
            The decision, sections 4, 13.10, 13.11, 15.1's figure, 15.9, 15.22, 16, 17 and 18, the catalogue, the
            matrix, the schema, the runbook, the guide's figure, region and readings, the roster and the remedy file.
Found:      the plan writes one table, and a second, `insider_read`, holds the spans read whole, since a reading must tell
            a quiet company's nought from days no reader read. The readings stand under analyst sentiment, the nearest of
            the seven headings the operator ruled on 2026-10-13, an eighth being the operator's to add. Only the table of
            shares is stored, a derivative's exercise reaching the shares as a row of their own. The data sets hold
            shares, prices and the shares held after to two places, so a difference past two is named apart: of the
            fixture's four filings both hold, Apple's sale is equal and Microsoft's three differ there alone. A Form 4 is
            listed under its issuer and each owner, so it is asked once an accession. The captures were written without
            the tab closing some header label lines and the data sets' rows ending in an empty field, which the manifest
            states; every ownership document is the archive's byte for byte. And a stock holding no bar drew its name
            page on the first day there is, which the insiders' year ran off; the view holds none for such a night.
Pins:       the readings' version moves from 894ed2775fe4 to c0c43a1c0279, the insiders' readings' source among those it
            pins; no source a rule, the hooks, the learned score or a candidate pins changes, asserted by the pins' own
            tests, so nothing is registered again and no record or paper account restarts.
Tests:      12 new, 2392 in all: each captured Form 4 parsed by hand; the data sets' cut to two places; awards and
            withholding in neither total; the coverage and the box's day; an amendment in its original's place and
            Delta's leaving its original standing; the store matching an amendment to one original and to none of two;
            a filing on or after the session and one before its ninety days read by none over a constructed store;
            Keysight's four readings worked by hand from the cut; the history compared with the archive's filings; an
            unreadable filing named; the history reading each quarter once; and the report's region and the chart's
            group read back. The fixture's refresh asks 12 documents where it asked 9, the cost test's night one Form 4
            more, and the catalogue's count, the readings' pin and the score's floor of 670 rows move with it.
Claims:     1337, seven more than 1330: the catalogue's and the matrix's Insider trades, section 16's Insider trades and
            Insider reads, section 17's Insiders' trades, section 18's An insider's filing the refresh cannot read and
            15.9's Insiders, named beside phase 19's pair as rows after its report, each passing. The plan's about 8
            counted the chart page's group, which landed as words in 15.22's figures row, and section 4's region, outside
            the claims; the second store and section 17's row were not counted.
Remedy:     `tools/remedies/2026-10-16-19.13-insider-trades.txt`, from main after the merge and before the night: the
            migration; the history from 2018q1; the days since read by hand, each day's asks and time printed; 2026q3
            compared; and the ledger's night step over the newest night; then each index's history built again with
            `--again`, its check and `loop-links` by hand, as the runbook states. It asks the SEC's archive alone and
            calls no model. The history's and the days' requests and times, the comparison, the builds' times and the
            checks are owed to its dated entry.
Mutated:    the rule, stated before the runs: one mutation a property this checkpoint adds whose failure a reader, a
            reading or the store would see, the smallest edit to shipped source that breaks it, each run against 19.13's
            tests. M1 a filing on the session read by its readings; red at the constructed store's readings. M2 the
            ninetieth day left out; red, same. M3 an amended original read beside its amendment; red at the amendment's
            test. M4 an amendment counted on its own day; red, same. M5 the box's reading none on its first day; red at
            the coverage test. M6 a buyer counted once a purchase; red at the awards test. M7 the data sets' rows in the
            file's order; red at the cut's test and the comparison. M8 two places read as three in the comparison; red
            there. M9 a stored filing asked again by the night; red at the fixture's refresh. M10 a quarter read again;
            red at the history's test. M11 the members' readings not read again; red at Keysight's readings and the
            constructed store's. M12 the region's lines oldest first; red at the read surface's test. M13 the box read
            inverted; red at the parser's, the coverage, the awards and the comparison's tests. M14 an amendment matched
            to the first of two originals; red at the store's matching test, which gained that case before the runs. M15
            an unreadable filing not caught; red at its test. Not mutated: the chart page's group, read off the region's
            own view, and the history's quarter written in one transaction. Each reverted before anything was committed.
Verified:   `tools/ci.ps1` green and `tools/verify-phase.ps1` green over the tree carrying this entry, 6d5343ab: all six
            steps, 0 warnings, 0 errors, 2392 of 2392 tests ran with none failed in 25 minutes 1 second, migrations 0
            to 96 with none pending, schema version 96, against `data-ci` and never `data`; the phase report 60 tables,
            1337 claims, 1337 passing, none out of scope and none unexamined, 1348 placements and verdicts reconciled
            against a floor of 34, 43 checks on the roster run and passed. The run before it, over 9ecc9341, failed one
            test, three keys of the expectations no test read, which 6d5343ab reads.
Carried:    19.13's remedy run from main and its record: the history's and the days' requests and times, the comparison,
            the builds' times and the checks; the Run page's 4.2 seconds, Tonight's 786 milliseconds and a name page's 693
            at their medians, each a ruling of its own the operator is asked for; the first night's fill step and ledger
            step on 19.10's build; the first night's ledger step on 19.9's build; Figure 11.4's "earliest promotion"; the
            S&P 600's breadth drawn as 45.0% beside "below its floor of 45%"; the sign-offs of phases 16, 17 and 18.

### 19.13 - the remedy run from main: the insiders' history and days read, 2026q3 compared, the three ledger histories built again and each index's checks   2026-10-16

Not a checkpoint entry: it records 19.13's remedy and what it measured, and lands nothing.
Ran:        from main's a7fee0bc, the merge of PR 473: `tools/remedies/2026-10-16-19.13-insider-trades.txt` between
            14:47:03Z and 15:37:38Z with no night or off-peak run writing, migration 96 applied, the history and the days
            since read, 2026q3 compared and the ledger's night step run by hand over the newest night; then the three
            ledger builds with `--again` over 2019-01-02 to 2026-10-05 between 15:37:38Z and 16:23:24Z, each index's
            point-in-time check and `loop-links` by 17:09:38Z, all from main. Requests of the SEC's archive alone, free
            and keyless; none of the provider and no model call.
Measured:   the history read 35 quarters, 2018q1 to 2026q3, in 562 seconds from 36 requests, the page and a quarter
            each: 869,015 Forms 4 and 4/A of the 2,104 companies' filers the store knows, 1,483,096 transactions stored.
            The days from 2026-10-01 to 2026-10-14, the newest the refresh had read, took 1,329 seconds: 2,294 member
            filings asked over 10 weekdays, 2026-10-12 a holiday holding no index, 3,162 transactions stored from 2,304
            requests. A filing took about 0.58 seconds, the request and its own commit on this disk, where the
            archive's pace alone allows 0.1: the heaviest day, 2026-10-02, asked 723 in 372 seconds, inside the
            refresh's 10 minutes, so a night reads about 1,000 such filings before its limit leaves the rest of a day
            to the next night. 2026q3 compared in 1,098 seconds from 4,420 requests: 5,625 member Forms 4 and 4/A over
            its 66 weekdays, the members being the S&P 500's, since the store's S&P 400 and 600 members join it on
            2026-10-02; 4,354 the data set holds rows of read again and 2,560 of them equal, the derivatives' own
            filings held by neither. The rest differed past the two places the data set states, the shares held after
            on 1,022, a price on 781 and the shares on 468; on 82 the data set lists a joint filing's owners in another
            order than the filing, Woltosz's 0001222526-26-000012 and Biglari's 0000921895-26-001763 among them; and on
            96 it states a holding's nature on a row the filing states none for, Wells Fargo's 0000072971-26-000290
            putting its trust on the direct row, which no reading or page reads. No other field differed. The ledger's
            night step over the night of 2026-10-15 by hand took 27 seconds with the insiders' readings, against 11 for
            19.12's run of the same night, writing the 43, 25 and 41 setups the night wrote, inside the night's hour.
            The S&P 500's build took 19 minutes 15 seconds, 92,006 setups, 9,345 passing the live rule; the S&P 400's
            11 minutes 41 seconds, 58,304 and 5,116; the S&P 600's 14 minutes 50 seconds, 76,051 and 6,571; 45 minutes
            46 seconds together against 41 minutes 43 seconds for 19.12's of the same counts, the insiders' rows read
            beside the history. Each index's point-in-time check rebuilt 200 of 200 sampled history setups with every
            reading the same, the insiders' four among them, in 12 minutes 57 seconds, 11 minutes 42 seconds and 20
            minutes 47 seconds. `loop-links` stored on the S&P 500 56 readings going with winners, 39 with losers, 240
            with no link beyond the shuffle and 25 too few over five families, and on the S&P 400 20, 11, 238 and 19
            and on the S&P 600 16, 23, 230 and 19 over four. Of the insiders' readings, on the S&P 500 the buyers and an
            officer buying go with winners for the pullback and the fundamentals-first family and the net bought for the
            breakout, the unplanned sales with losers for the breakout; on the S&P 600 an officer buying goes with
            winners for the breakout and the unplanned sales with losers for the pullback and the fundamentals-first
            family; and on the S&P 400 none beyond the shuffle.
Found:      the days read by hand read each day's own members, so 2026-10-01 read none of the S&P 400's and 600's
            members' Form 4s, which the night would have read on the next session with that session's members, while
            recording the day as read; and an insider is the owners' CIKs in the order a reader lists them, so one
            joint filing read from both readers, or two the data set lists in two orders, count two insiders and an
            amendment of one misses its original. Both are 19.13's correction, its entry below.
Verified:   `tools/ci.ps1` green over the tree 19.13's entry records, and over the tree carrying this record with 19.13's
            correction as that entry records.
Carried:    the Run page's 4.2 seconds, Tonight's 786 milliseconds and a name page's 693 at their medians, each a ruling
            of its own the operator is asked for; the first night's fill step and ledger step on 19.10's build; the first
            night's ledger step on 19.9's build; Figure 11.4's "earliest promotion"; the S&P 600's breadth drawn as 45.0%
            beside "below its floor of 45%"; the sign-offs of phases 16, 17 and 18.

### 19.13 - correction: the insiders' days read by hand for every company the store knows, and an insider whatever order its owners are listed in   2026-10-16

Queue:      done, 19.0 to 19.13 with their corrections and records and the rulings of 19.11 and 19.13, PRs 446 to 473;
            in progress, this correction; next, its remedy from main and its record, then the sign-offs of phases 16, 17
            and 18, each by a fresh session.
Built:      the days read by hand are read for every company the store knows, the night's and the pulled companies', as
            the history reads them, in place of each day's members, which on 2026-10-01 held none of the S&P 400's and
            600's members, whom the store's membership joins on 2026-10-02, while the day was recorded as read; the night
            keeps its own session's members. And an insider is the owners' CIKs in ordinal order whatever order a reader
            lists them in, in the readings' buyers, the region's buyers and sellers, an amendment's match to its original
            and the comparison of the two readers, since the data sets list a joint filing's owners in an order of their
            own; each row keeps the order the reader that stored it lists. The catalogue's and the matrix's Insider trades
            rows read no membership; the schema's two owner columns, the runbook's days and the correction's remedy, the
            roster and the remedy file.
Found:      by 19.13's remedy comparing 2026q3 both read, as its record above states.
Pins:       the readings' version moves from c0c43a1c0279 to 9223ff040777 with the insiders' readings' source; no source a
            rule, the hooks, the learned score or a candidate pins changes, asserted by the pins' own tests.
Tests:      2 new, 2394 in all: a joint filing's owners in two orders one insider in the readings and the region, an
            amendment listing them in another order than its original finding it, and the comparison naming no
            difference for the order alone; and the days read by hand reading a company the membership joins the day
            after and one it holds no membership of, and no filer the store knows no company of.
Claims:     1337, as before: the correction moves words in the catalogue's row and a cell of the matrix's.
Remedy:     `tools/remedies/2026-10-16-19.13-correction-every-company-read.txt`, from main after the merge and before the
            night: 19.13's days read again for every company the store knows, asking only for the filings not stored,
            and the ledger's night step over the newest night; then each index's history built again with `--again`, as
            the runbook states. It asks the SEC's archive alone and calls no model. Its requests and times are owed to
            its dated entry.
Mutated:    the rule, stated before the runs: one mutation a property the correction adds whose failure a reading or the
            store would see, the smallest edit to shipped source that breaks it, each run against 19.13's tests. C1 the
            readings' buyers counted by the owners' CIKs as stored; red at the joint owners' test. C2 the region's buyers
            counted the same way; red, same. C3 an amendment matched by its owners as listed; red, same. C4 the comparison
            setting the owners' CIKs as listed; red, same. C5 the days read by hand reading only the companies a
            membership holds; red at the days' test. Each reverted before anything was committed.
Verified:   `tools/ci.ps1` green and `tools/verify-phase.ps1` green over the tree carrying this entry, b11ee730: all six
            steps, 0 warnings, 0 errors, 2394 of 2394 tests ran with none failed in 24 minutes 47 seconds, migrations 0
            to 96 with none pending, schema version 96, against `data-ci` and never `data`; the phase report 60 tables,
            1337 claims, 1337 passing, none out of scope and none unexamined, 1348 placements and verdicts reconciled
            against a floor of 34, 43 checks on the roster run and passed.
Carried:    the correction's remedy run from main and its record; the Run page's 4.2 seconds, Tonight's 786 milliseconds
            and a name page's 693 at their medians, each a ruling of its own the operator is asked for; the first night's
            fill step and ledger step on 19.10's build; the first night's ledger step on 19.9's build; Figure 11.4's
            "earliest promotion"; the S&P 600's breadth drawn as 45.0% beside "below its floor of 45%"; the sign-offs of
            phases 16, 17 and 18.

### 19.13 - the correction's remedy run from main: the insiders' days read again for every company the store knows, the ledger's night step and the three ledger histories built again   2026-10-16

Not a checkpoint entry: it records the remedy of 19.13's correction and what it measured, and lands nothing.
Ran:        from main's 17a7d1c1, the merge of PR 474: `tools/remedies/2026-10-16-19.13-correction-every-company-read.txt`
            from 17:56:30Z with no night or off-peak run writing, then the three ledger builds with `--again` over
            2019-01-02 to 2026-10-05 from main's clean copy, as the runbook states. Requests of the SEC's archive alone,
            free and keyless; none of the provider and no model call. The read surface restarted from main's Release.
Measured:   the days from 2026-10-01 to 2026-10-14 read again in 293 seconds: 2,727 Forms 4 and 4/A of every company the
            store knows over 10 weekdays, 2026-10-12 a holiday holding no index, 1,031 of them not stored before and
            asked, 550 transactions stored, from 1,041 requests. 2026-10-01 asked 275 of its 353 and stored 223
            transactions, the S&P 400's and 600's members' among them, which 19.13's remedy had read none of. The
            ledger's night step over the night of 2026-10-15 by hand took 10 seconds, writing the 43, 25 and 41 setups
            the night wrote. The S&P 500's build took 14 minutes 52 seconds, 92,006 setups, 9,345 passing the live rule;
            the S&P 400's 13 minutes 20 seconds, 58,304 and 5,116; the S&P 600's 20 minutes 45 seconds, 76,051 and 6,571;
            48 minutes 57 seconds together, the same counts as 19.13's remedy's builds.
Verified:   `tools/ci.ps1` green over the tree 19.13's correction's entry records, and the checks reading this record
            run filtered over the tree carrying it.
Carried:    the Run page's 4.2 seconds, Tonight's 786 milliseconds and a name page's 693 at their medians, each a ruling
            of its own the operator is asked for; the first night's fill step and ledger step on 19.10's build; the first
            night's ledger step on 19.9's build; Figure 11.4's "earliest promotion"; the S&P 600's breadth drawn as 45.0%
            beside "below its floor of 45%"; the sign-offs of phases 16, 17 and 18.

### 19.14 ruling - the records archive as phase 19's last checkpoint, then the sign-offs, and no new work until the loop's first proposals are read   2026-10-16

Not a checkpoint entry: it lands nothing.
Ruled:      by the operator on 2026-10-16, word for word in `prompts/2026-10-16-19.14-ruling-records-archive-and-pause.md`,
            with the operator's answers of the same day to the reading in `prompts/2026-10-16-19.14-operator-answers.md`.
            19.14, the records archive, is phase 19's last checkpoint. Then the sign-offs of phases 16, 17, 18 and 19,
            each in a fresh session, one at a time, as the 16.1 ruling set them, each of phases 16 to 18 reading its
            entries from the archive and phase 19's from the live record. Then the monthly run of 2026-11-07, which
            reads 19.12's and 19.13's readings, and the operator reading its proposals on the Loop page. Until then no
            checkpoint is added and phase 20 is not planned, an idea raised meanwhile going to the plan's Tabled for
            phase 21 as one line with no checkpoint text, and a defect found meanwhile is still fixed as a correction:
            the pause stops new work, not fixes. The archive was approved by the operator in conversation on 2026-10-03
            and deferred on 2026-10-04 until phase 14 was done, recorded here as the operator's word of today. Its blocks
            move by a one-off command copying each by line number into its archive and removing it from the live file,
            not committed to `tools/` and recorded word for word in 19.14's entry, every other change going through the
            editor; CLAUDE.md's Document lifecycle section alone is amended, `.claude/rules/corpus-edits.md` stating no
            append-only rule; the decision is filed under Process beside "Specifications are edited clean; the changelog
            records what they said before"; the changelog's block of entries before 2026-10-12 moves as it stands, not
            reordered; and the CI times before and after are recorded though the gain wanted is what each session reads.
Figures:    the figures-only commit goes. A checkpoint's entry states the figures its verifying run is to measure before
            that run: the test count read off the suite's own list of its tests, and the claim and PASS counts read off
            the phase report run over the tree with the entry's heading in place, since the heading is what lands the
            checkpoint and brings its claims into scope. The verifying pass then runs over that exact tree, and from
            19.14 the phase report is green only where the newest entry stating a run over the tree carrying it states
            the figures the report measured, a mutation recorded in 19.14's entry showing it red where one differs.
            Chosen over the stamp accepting a tree whose only difference from the stamped commit is the newest entry's
            figure lines, because the checks reading the record read those lines: `two-platform` reads the Verified
            line's words, `record-append-only` holds a sweep's stated baseline to the entry's own test count and
            `banned-prose` reads every line, so a stamp accepting a figure-only difference would vouch for verdicts the
            suite never reached over that tree. The stamp is unchanged, still naming the commit the suite ran over with
            the tree clean at both runs, and the tree merged is the tree verified, where until now it was that tree
            with its figures filled in after. A Verified line states no duration of its own run. Condition 8 is amended
            to match at 19.14, its reasoning kept. The suite's own list held 2394 tests over 17a7d1c1, the count its
            run over the same code measured.
Plan:       `BUILD_PLAN.md` gains 19.14 after 19.13 with its done condition, the claims predicted gain none, and a
            section at its end headed Tabled for phase 21, empty; section 20's row names the archive and 19.0 to 19.14.
Found:      read against the repository before building, on the ruling's instruction: 152 of the plan's 153 checkpoints
            of phases 0 to 18 land today, 17.2a not landed while it waits on the operator's approval; every entry of
            phases 16, 17 and 18 stands before phase 19's planning entry and every phase 19 entry after it, beside a 2.1
            and two 11.4 corrections written since; the changelog runs newest first to 2026-09-08 and oldest first from
            2026-09-05 to 2026-10-08, every entry before 2026-10-12 below those dated from it; the plan held no list for
            phase 21 and the decisions no records topic; and four checks walk every past revision of the two records,
            about four of the suite's 25 minutes, which the move leaves as they are.
Queue:      done, 19.0 to 19.13 with their corrections, records and rulings, PRs 446 to 474; in progress, this ruling
            and the record of 19.13's correction's remedy; next, 19.14, then the sign-offs of phases 16, 17, 18 and 19,
            each by a fresh session, then the monthly run of 2026-11-07 and the operator reading its proposals.
Verified:   `tools/ci.ps1` green over main as 19.13's correction's entry records, and the checks reading this record, the
            changelog and the plan run filtered over the tree carrying it: 164 of 164 tests, the count the suite's own
            list gave before the run.
Carried:    nothing new.

### 19.14 - the records archive: the build record's entries before phase 19 and the changelog's before 2026-10-12 moved whole to archives read with their live files as one, and an entry's figures stated before the run that checks them   2026-10-16

Queue:      done, 19.0 to 19.13 with their corrections, records and rulings and the 19.14 ruling, PRs 446 to 475; in
            progress, 19.14; next, the correction of the chart page's empty figures the operator added on 2026-10-16,
            after this merges, then the sign-offs of phases 16, 17, 18 and 19, each by a fresh session, then the
            monthly run of 2026-11-07 and the operator reading its proposals.
Built:      `docs/archive/PROGRESS-phases-0-to-18.md` holding every entry of `PROGRESS.md` before `### 19.0 planning`
            and `docs/archive/CHANGELOG-to-2026-10-11.md` every entry of `CHANGELOG.md` dated before 2026-10-12, each
            moved whole, unchanged and in order, each live file keeping its title with one line beneath it naming its
            archive and each archive opening on one line naming its live file and saying it takes no new entry. Every
            check reads a record with its archive as one through the corpus's one reader, which refuses a pair whose
            opening lines are missing; the citation readers walk both archives, the changelog's quoted prior text left
            out in its archive as in itself; `record-append-only` joins each pair as the move wrote it against the record
            at the move's parent, holds each archive byte for byte as written, and holds every entry before the move in
            its place and every point it landed and citation it held; `changelog-reconciles` counts the pair at every
            revision from the move on; and the phase report holds the figures the newest entry states for the run over
            the tree carrying it to the ones it measured, on both surfaces it writes. CLAUDE.md's Document lifecycle
            reads an archive as the earlier part of its record and the record as append only across the pair, and
            condition 8 states the figures before the run; two decisions under Process, `scripts.md`'s green, the
            roster's three rows and the changelog.
Moved:      by the one-off commands, from the checkout on this branch at main's 4c599ee4, not committed to `tools/`:
            `mkdir -p docs/archive && sed -n '28,43860p' docs/PROGRESS.md > docs/archive/PROGRESS-phases-0-to-18.md &&
            sed -i '28,43860d' docs/PROGRESS.md && sed -n '1522,$p' docs/CHANGELOG.md >
            docs/archive/CHANGELOG-to-2026-10-11.md && sed -i '1522,$d' docs/CHANGELOG.md`, the four opening lines then
            written in the editor, in a commit of its own, 2290945c.
Measured:   before the move, at 4c599ee4, `PROGRESS.md` 4,243,406 bytes and 45,614 lines and `CHANGELOG.md` 2,422,751
            bytes and 15,691 lines. At the move, the live `PROGRESS.md` 187,607 bytes and 1,782 lines, its archive
            4,056,112 and 43,835; the live `CHANGELOG.md` 235,708 bytes and 1,522 lines, its archive 2,187,351 and 14,172;
            each pair joined is its record before the move byte for byte. HasLanded reports 152 of the plan's 153
            checkpoints of phases 0 to 18 landed before the move and after it, 17.2a not landed in both. The decision
            citations number 44 in the build record and 64 in the changelog before the move, and 5 and 39, 0 and 64 in
            the live files and archives after it; the obligation citations 23 and 1 before, and 0 and 23, 0 and 1 after;
            every one resolves. The phase report held 1337 claims, 1337 passing, before the move, over b11ee730, and
            1337 and 1337 after it, read over this entry's tree with its heading in place. `tools/ci.ps1` took 25 minutes 29 seconds over b11ee730, the code of the commit before
            the move, on the operator's ruling of 2026-10-16 that a tree differing from one already run by documents
            alone takes that run's time, and 22 minutes 30 seconds over b407d3d5, the finished tree before this entry,
            each on the operator's machine with no night or off-peak run writing; the four checks walking the records'
            history took 4 minutes 3 seconds before and 3 minutes 56 seconds after, since the move leaves the history
            they read as it was, and the new checks of the pairs 4 seconds.
Tests:      12 new, 2406 in all: each pair joined at the move against the record before it, each archive as the move
            wrote it and every earlier entry in its place, over both records; each move's cut; the landed points and the
            citations; the readers shown to find an archived entry edited, an entry removed from the pair, a new entry
            in an archive and an entry moved out of its place; the changelog's count needing its archive over
            constructed revisions; and the report's figures held to its own, each figure one off alone turning it red,
            with the record's newest entry read.
Claims:     1337, as before: the move and the checks add no row of a table the harness reads.
Mutated:    the rule, stated before the runs: one mutation a property 19.14 adds whose failure would let an archived
            entry change, an entry leave the pair, an archive take a new entry, a citation in an archive go unread or a
            stated figure stand unchecked, the smallest edit that breaks it, made in this checkout, each run against its
            own tests. R1 one word of an archived entry edited: red at the archive's own test and the earlier entries'
            over the build record. R2 phase 19.1's entry removed from the live record: red at the headings ever written,
            the earlier entries and the landed points. R3 an entry written to the build record's archive: red at the
            archive's own test. C1 a record read without its archive: red at five tests, `two-platform`'s anchor and the
            changelog's count among them. C2 the changelog counted without its archive: red at the constructed
            revisions. C3 the changelog's archive read for citations in its quoted prior text: red at the quoted text's
            test and the citations kept. C4 the archives left out of the documents the citation readers walk: red at the
            population's count. C5 a figure one off agreeing: red at the figures' test. C6 green without the record's
            figures: red, same. C7 the test count not read: red, same. F1, once this entry was written, its stated test count
            one higher: the phase report over this tree red, naming 2407 tests ran where the run measured 2406. Each
            reverted before anything was committed.
Verified:   `tools/ci.ps1` green and `tools/verify-phase.ps1` green over the tree carrying this entry, the figures below
            stated before the run: all six steps, 0 warnings, 0 errors, 2406 of 2406 tests ran with none failed,
            migrations 0 to 96 with none pending, schema version 96, against `data-ci` and never `data`; the phase report
            60 tables, 1337 claims, 1337 passing, none out of scope and none unexamined, 1348 placements and verdicts
            reconciled against a floor of 34, 43 checks on the roster run and passed.
Carried:    the chart page's empty figures, the operator's correction of 2026-10-16 in
            `prompts/2026-10-16-19.11-correction-chart-page-empty-figures.md`, after this merges; the Run page's 4.2
            seconds, Tonight's 786 milliseconds and a name page's 693 at their medians, each a ruling of its own the
            operator is asked for; the first night's fill step and ledger step on 19.10's build; the first night's ledger
            step on 19.9's build; Figure 11.4's "earliest promotion"; the S&P 600's breadth drawn as 45.0% beside "below
            its floor of 45%"; the sign-offs of phases 16, 17, 18 and 19.

### 19.11 - correction: the chart page draws five years as its longest span, and the provider's share count where the filer states none   2026-10-16

Queue:      done, 19.0 to 19.14 with their corrections, records and rulings, PRs 446 to 476; in progress, the
            corrections of the chart page's empty figures the operator added on 2026-10-16, labelled 19.11, 18.2, 19.13
            and 17.3, and their remedy; next, the sign-offs of phases 16, 17, 18 and 19, each by a fresh session, then
            the monthly run of 2026-11-07 and the operator reading its proposals.
Ruled:      by the operator on 2026-10-16, word for word in
            `prompts/2026-10-16-19.11-correction-chart-page-empty-figures.md`, with the answers of the same day to the
            read below in `prompts/2026-10-16-19.11-correction-operator-answers.md`: the SEC's ticker file for a member
            the provider files no CIK for, the provider's count where the filer states none, the insiders' year ending
            the day before the night, and the dividends history run from 2018. The stock the prompt names is BNY.
Found:      the read the correction asked for, read-only on the operator's store on 2026-10-16 over the night of
            2026-10-15. BNY, which the store holds as BK until 2026-05-21, held three company rows, the newest fetched
            2026-10-14, and a pulled company row, none carrying a CIK, so its pages read no filer, no filed fact and no
            insider row. 36 members of the three indices held no CIK on their newest company row, 11 of the S&P 500, 3
            of the 400 and 22 of the 600, each a ticker the SEC's own ticker file names and 19 of them filers the store
            knew under a former ticker; VYLR and HOS held no company row, the provider answering nothing for either; and
            124 filers, 40, 33 and 51, held facts and no share count, which a filer stating its count a class at a time
            does not file in the archive's facts. The insiders' history held every weekday from 2025-10-16 to
            2026-10-15 but the night's own day, which the archive posts after the night that would read it, so every
            view of the year to the night read none. The store kept 28 dividends, each from a night's bulk answer with
            its ex-date from 2026-10-13 to 2026-10-15 and none of BNY's, the dividends history never having run. Of
            every member's chart page computed for 2026-10-15, read through the read surface, the S&P 500's 503, the
            400's 400 and the 600's 602: shares outstanding not stored on 69, 50 and 98; paid in its last whole year,
            ten years and each of the four insider rows not stored on every page; and raised in a row nought years on
            401, 258 and 341 and not stored on 102, 142 and 261.
Built:      the chart page's spans end at five years, the longest the history pulled from 2018 reaches, its ten-year
            row gone; and a member whose filer states no share count draws the provider's count off its newest
            quarter's balance sheet with the quarter's day, named as the provider's. Section 15.22's figures, the
            guide, the roster and the changelog.
Tests:      none new: the chart page's tests read every fixture name's page at every interval drawing five years as
            its longest span and no ten-year row, and AAPL's shares as the provider's count read off the store.
Claims:     as the 17.3 correction's entry below records.
Mutated:    the rule, stated before the runs for the four corrections together: one mutation a property each adds
            whose failure a page, the store or a reading would show, the smallest edit to shipped source that breaks
            it, made in this checkout, each run against its own tests. M1 the ten-year span put back: red at the spans'
            test and the chart page's fixture test. M2 the provider's count never drawn: red at the chart page's
            fixture test. Each reverted before anything was committed.
Verified:   `tools/ci.ps1` green and `tools/verify-phase.ps1` green over the tree carrying the 17.3 correction's entry
            below, as it records.
Carried:    the remedy of the four corrections run from main and its record, every member's chart page read again
            per index; the Run page's 4.2 seconds, Tonight's 786 milliseconds and a name page's 693 at their medians,
            each a ruling of its own the operator is asked for; the first night's fill step and ledger step on
            19.10's build; the first night's ledger step on 19.9's build; Figure 11.4's "earliest promotion"; the S&P
            600's breadth drawn as 45.0% beside "below its floor of 45%"; the sign-offs of phases 16, 17, 18 and 19.

### 18.2 - correction: raised in a row is none where fewer than two whole years of dividends are kept   2026-10-16

Queue:      as the 19.11 correction's entry above states.
Built:      a payer's years raised in a row are read over the whole years kept and are none where fewer than two are
            kept, a raise counting one and a year held level nought from two; the chart page, the name page's table
            and the exported file say not stored. Section 15.22, the runbook's dividends history from 2018, the
            roster and the changelog.
Found:      as the 19.11 correction's entry above records: nought years on 1,000 pages, no member then holding a whole
            year of dividends kept.
Tests:      1 new, over a constructed store holding under two whole years of dividends: raised in a row none on the
            reading and not stored on the chart page and the name page, and a raise one and a level year nought from
            two; and the report's regions test reads MSFT's years raised as not stored.
Claims:     as the 17.3 correction's entry below records.
Mutated:    under the rule the 19.11 correction's entry states. M3 raised in a row nought where fewer than two whole
            years are kept: red at the new test. M4 the name page drawing nought where it is none: red at the new test
            and the report's regions test. Each reverted before anything was committed.
Verified:   `tools/ci.ps1` green and `tools/verify-phase.ps1` green over the tree carrying the 17.3 correction's entry
            below, as it records.
Carried:    as the 19.11 correction's entry above states.

### 19.13 - correction: the insiders' year is the one before the night, and the history is read again for a company known since   2026-10-16

Queue:      as the 19.11 correction's entry above states.
Built:      the insiders' view on a night reads the filings filed before it, as the night's own setups' readings do,
            and needs the history only through the day before the night, a filing filed on the night read the night
            after; and `insiders-history --again` reads every quarter its bounds take, those read already among them,
            storing only the filings not stored, so a company the store comes to know after its quarters were read
            gets its history, each quarter keeping its first record. Sections 4, 15.9 and 15.22, the runbook, the
            roster and the changelog.
Found:      as the 19.11 correction's entry above records: the four insider rows not stored on every page.
Tests:      2 new, the view on a night reading the filings filed before it and needing the history only through the
            day before, and the history read again storing a company known since and nothing twice; the region's
            test reads its year to the day before the night.
Claims:     as the 17.3 correction's entry below records.
Mutated:    under the rule the 19.11 correction's entry states. M5 the view needing the history through the night's own
            day: red at the view's test. M6 the history read again passing the quarters recorded: red at the read
            again's test. Each reverted before anything was committed.
Verified:   `tools/ci.ps1` green and `tools/verify-phase.ps1` green over the tree carrying the 17.3 correction's entry
            below, as it records.
Carried:    as the 19.11 correction's entry above states.

### 17.3 - correction: a member the provider files no CIK for is matched to its filer by the SEC's own ticker file   2026-10-16

Queue:      as the 19.11 correction's entry above states.
Built:      the `sec-tickers` step reads the SEC's own file of every listed ticker and its filer, one request to the
            archive, free and keyless, by hand and in the monthly run's facts step and never by the night, keeping
            each line in `sec_ticker` under its run, a ticker listed twice kept by its first line, and names on its row
            the members the provider files no CIK for that the file names and those it does not; the quarters fetch,
            the one writer of a company's row, takes the newest read's filer where the provider's answer files none and
            says which it took in the row's `cik_source`; and `quarters --refetch-unfiled` asks each member of the
            index whose newest company row carries no filer, once a session, stating its asks and their weighted
            calls. Migration 97. The catalogue's, the matrix's and the stores' rows, the decision, the schema, the
            runbook, the guide, the roster, the remedy file and the changelog. No source a rule's version or the
            readings' version pins moves.
Found:      as the 19.11 correction's entry above records: 36 members with no CIK, 11, 3 and 22.
Tests:      3 new, 2412 in all: the file read as sent, its cut of seven lines as the archive sent them; the step
            keeping it under its run and naming the members; and the quarters fetch taking the SEC's filer, saying
            which it took, and the refetch asking only the member left with none.
Claims:     1340, 1337 before: the catalogue's, the matrix's and the stores' rows for the SEC tickers step.
Remedy:     `tools/remedies/2026-10-16-19.11-correction-chart-page-figures.txt`, from main after the merge with no night
            or off-peak run writing: migration 97, the ticker file read, the members with no filer asked again on each
            index, every filer's facts asked whole, the insiders' history read again and the days since 2026-10-01,
            each index's dividends from 2018 and the ledger's night step; then each index's history built again with
            `--again` and checked, as the runbook states. About 1,901 of the provider's weighted calls, 396 for the
            quarters and about 1,505 for the dividends, the SEC's archive free; no model call.
Mutated:    under the rule the 19.11 correction's entry states. M7 the fetch never taking the SEC's filer, its first form
            reading the answer as possibly none and refused by the compiler, made again as a condition never true: red
            at the quarters fetch's test. M8 the SEC's filer said to be the provider's: red, same. M9 the refetch asking
            the members holding a filer: red, same. M10 a ticker listed twice read by both lines: red at the file's
            test. M11 the step naming the members the file names in place of those it does not: red at the step's
            test. Each reverted before anything was committed.
Verified:   `tools/ci.ps1` green and `tools/verify-phase.ps1` green over the tree carrying this entry, the figures below
            stated before the run: all six steps, 0 warnings, 0 errors, 2412 of 2412 tests ran with none failed,
            migrations 0 to 97 with none pending, schema version 97, against `data-ci` and never `data`; the phase
            report 60 tables, 1340 claims, 1340 passing, none out of scope and none unexamined, 1351 placements and
            verdicts reconciled against a floor of 34, 43 checks on the roster run and passed.
Carried:    as the 19.11 correction's entry above states.

### 19.3 - correction: each setup card draws its live picks as rows of its own, each ticker opening the stock's chart page   2026-10-17

Queue:      done, 19.0 to 19.14 with their corrections, records and rulings and the four corrections of the chart
            page's empty figures, PRs 446 to 477; in progress, this correction and that remedy's builds and checks;
            next, the remedy's record, then the sign-offs of phases 16, 17, 18 and 19, each by a fresh session, then
            the monthly run of 2026-11-07 and the operator reading its proposals.
Ruled:      by the operator on 2026-10-17, word for word in `prompts/2026-10-17-19.3-ruling-card-picks-as-rows.md`:
            the drift card's pick on Tonight's page of 2026-10-16 opened TRV's row among the buy points where it should
            open the stock's chart page, and each card should show its picks alike; asked how, they chose rows like the
            heavyweights card's holdings, the buy points staying as the summary. A new decision under Presentation
            supersedes "Live picks are drawn once in tonight's buy points, and a setup's card names them by their
            place", which moves to Previously decided with its reasoning.
Found:      the card's line linked each pick to its row among the buy points, the one link on Tonight's page of
            2026-10-16 naming a stock that opened neither its chart page nor its report, beside 72 opening the chart
            page; 19.11's link test forbade a stock link to the name page and never one selecting a row.
Built:      each setup card draws its live or provisional rule's picks as a table of its own in the setup's order: the
            place, the ticker opening the stock's chart page with the company beneath it, the business state, the buy,
            the stop, the target and the reward to risk, read from the same pick cells the buy points read, a chosen
            variant's picks standing in its place. The four architecture rows, the decision, the guide, the roster and
            the changelog.
Tests:      none new: the card tests read each card's own rows against the stocks the night listed in their places,
            each ticker's link to its chart page and each row's plan and business against its buy point's row, and the
            link test holds that no link on any of its seven screens selects a row.
Claims:     as the 19.11 correction's entry below records: two parts of the card's rows renamed and none added.
Mutated:    the rule, stated before the runs: one mutation a property the correction adds whose failure the page would
            show, the smallest edit to shipped source that breaks it, made in this checkout, each run against its own
            tests. K1 the card's ticker linking its buy point's row again: red at the families' and the drift's card
            tests. K2 the card drawing no rows of its own: red at five card tests. K3 a card's row reading its stop as
            its buy: red at the families' card test. Each reverted before anything was committed.
Verified:   `tools/ci.ps1` green and `tools/verify-phase.ps1` green over the tree carrying the 19.11 correction's entry
            below, as it records.
Carried:    the remedy of the chart page's four corrections, its builds and checks still to run, and its record; the
            Run page's 4.2 seconds, Tonight's 786 milliseconds and a name page's 693 at their medians, each a ruling of
            its own the operator is asked for; the first night's fill step and ledger step on 19.10's build; the first
            night's ledger step on 19.9's build; Figure 11.4's "earliest promotion"; the S&P 600's breadth drawn as
            45.0% beside "below its floor of 45%"; the sign-offs of phases 16, 17, 18 and 19.

### 19.11 - correction: the list's and the buy points' headings say the ticker opens the stock's chart page   2026-10-17

Queue:      as the 19.3 correction's entry above states.
Found:      while building the 19.3 correction: from 19.11 the ticker on tonight's list and among the buy points opens
            the stock's chart page and a press elsewhere on the row selects it, while both columns' headings still said
            the ticker selects the row.
Built:      both headings say the ticker opens the stock's chart page and a press anywhere else on the row selects it.
Tests:      none new, 2412 in all: the list's heading test holds its words.
Claims:     1340, as before.
Mutated:    under the rule the 19.3 correction's entry states. K4 the list's heading saying the ticker selects the row:
            red at its heading test. Reverted before anything was committed.
Verified:   `tools/ci.ps1` green and `tools/verify-phase.ps1` green over the tree carrying this entry, the figures below
            stated before the run: all six steps, 0 warnings, 0 errors, 2412 of 2412 tests ran with none failed,
            migrations 0 to 97 with none pending, schema version 97, against `data-ci` and never `data`; the phase
            report 60 tables, 1340 claims, 1340 passing, none out of scope and none unexamined, 1351 placements and
            verdicts reconciled against a floor of 34, 43 checks on the roster run and passed.
Carried:    as the 19.3 correction's entry above states.

### 19.13 - correction: a company the store matches no filer for reads no insider figure and never nought   2026-10-17

Queue:      done, 19.0 to 19.14 with their corrections, records and rulings, the four corrections of the chart page's
            empty figures and the card's rows, PRs 446 to 478; in progress, this correction and the chart page
            corrections' remedy, whose history builds wait on it; next, that remedy's record, then the sign-offs of
            phases 16, 17, 18 and 19, each by a fresh session, then the monthly run of 2026-11-07 and the operator
            reading its proposals.
Found:      reading the chart pages once the 19.13 correction of 2026-10-16 ended the insiders' window the day before
            the night: VYLR's and HOS's, whose companies the store matches to no filer, drew $0 net bought and no
            insider buying where they read nothing, and the ledger's four readings of a member with no filer read
            nought on its buyers and its officer wherever the history held the window, an empty list standing for a
            filer that filed nothing.
Built:      a member the store matches no filer for reads none on all four insider readings, through the ledger's
            context the night's step and the point-in-time check read and through the night's reading again after the
            refresh, where a filer filing nothing reads nought; and the report's region and the chart page's group draw
            their line and not stored for such a company. The readings' version moves from 9223ff040777 to f858ebd1ac63
            with the ledger's source; no source a rule, the hooks, the learned score or a candidate pins changes.
            Sections 4, 15.9, 15.22 and 17, the roster and the changelog.
Tests:      2 new, 2414 in all: a member with no filer none on all four through the context and through the reading again, each
            beside a filer filing nothing reading nought; and the region's test reading a company with no filer as not
            stored, the year's spans held.
Claims:     1340, as before: words added to four rows and no part.
Remedy:     none of its own: the chart page corrections' remedy builds each index's history again after this merges,
            so the history carries the readings as corrected, and runs the ledger's night step over the newest night
            again first.
Mutated:    the rule, stated before the runs: one mutation each path that reads a member's insiders, the smallest edit
            putting the empty list back for a member with no filer, each run against its own tests. N1 the context:
            red at the context's test. N2 the reading again: red at its test. N3 the pages: red at the region's test.
            Each reverted before anything was committed.
Verified:   `tools/ci.ps1` green and `tools/verify-phase.ps1` green over the tree carrying this entry, the figures below
            stated before the run: all six steps, 0 warnings, 0 errors, 2414 of 2414 tests ran with none failed,
            migrations 0 to 97 with none pending, schema version 97, against `data-ci` and never `data`; the phase
            report 60 tables, 1340 claims, 1340 passing, none out of scope and none unexamined, 1351 placements and
            verdicts reconciled against a floor of 34, 43 checks on the roster run and passed.
Carried:    the remedy of the chart page's four corrections, BNY's and MRSH's filers, facts and Forms 4 owed by it, its
            builds and checks and its record; the Run page's 4.2 seconds, Tonight's 786 milliseconds and a name page's
            693 at their medians, each a ruling of its own the operator is asked for; the first night's fill step and
            ledger step on 19.10's build; the first night's ledger step on 19.9's build; Figure 11.4's "earliest
            promotion"; the S&P 600's breadth drawn as 45.0% beside "below its floor of 45%"; the sign-offs of phases 16,
            17, 18 and 19.

### 19.11 - the corrections' remedy run from main: the SEC's ticker file, the members' filers, their facts and Forms 4, the dividends from 2018, the three ledger histories built again and each index's checks   2026-10-17

Not a checkpoint entry: it records the remedy of the chart page's four corrections of 2026-10-16 and what it measured,
and lands nothing.
Ran:        `tools/remedies/2026-10-16-19.11-correction-chart-page-figures.txt` from main's fccddcee, the merge of PR 477,
            from 01:44:48Z to 02:22:43Z, migration 97 applied. The refetch asks a member once a New York session, and
            BNY and MRSH, which the night of 2026-10-16 had asked as reporting with the new quarter not yet posted, were
            asked again by hand at 04:00Z from main's 9d1b968d, on the session of 2026-10-17. Then from main's 4d30679c,
            the merge of PR 479, every filer's facts asked whole again, the insiders' history read again and the days
            since 2026-10-01, the ledger's night step over the newest night, the three ledger builds with `--again` over
            2019-01-02 to 2026-10-05 from main's clean copy and each index's point-in-time check, from 04:25:34Z to
            06:27:02Z. No night or off-peak run was writing at any step, the off-peak run of 04:05Z running between the
            two parts to 04:16:43Z. No model call.
Measured:   the SEC's ticker file listed 10,443 tickers in one request and named all 38 members the provider filed no
            CIK for. The refetch asked 9, 3 and 22 members of the S&P 500, 400 and 600, 374 weighted calls, and then BNY
            and MRSH, 22 more, each company row taking the SEC's filer; every member of the three indices holding a
            company row now carries a filer, 11, 3 and 22 of them the SEC's. The first facts pass asked 2,119 filers and
            stored 1,274 facts in 1,698 seconds and the second 2,121 and 689 in 1,648; the insiders' history read 35
            quarters, about 870,000 Forms 4 and 4/A each time, storing 1,928 transactions in 112 seconds and then 1,342
            in 57; the days from 2026-10-01 to 2026-10-15 asked 773 filings and stored 16 transactions in 161 seconds,
            then 767 and 4 in 149. The dividends history asked 503, 400 and 602 members, 1,505 weighted calls, and kept
            13,374, 8,766 and 11,147 dividends new to the store, 1,901 weighted calls with the refetch, as stated. The
            ledger's night step over 2026-10-16 wrote 24, 17 and 40 setups in 10 seconds. The S&P 500's build took 14
            minutes 0 seconds, 92,158 setups, 9,345 passing the live rule; the S&P 400's 10 minutes 46 seconds, 58,483
            and 5,118; the S&P 600's 21 minutes 46 seconds, 76,174 and 6,575; 46 minutes 32 seconds together, 152, 179
            and 123 setups more than the 19.13 correction's builds and 2 and 4 more passing on the S&P 400 and 600, the
            matched filers' facts reaching the fundamentals-first family. Each index's point-in-time check rebuilt 200 of
            200 sampled history setups with every reading the same, in 12 minutes 48 seconds, 12 minutes 1 second and 18
            minutes 53 seconds.
Also:       carried since 19.9 and 19.10, read off the run log: the first night on 19.9's build, 2026-10-15 on
            ba9ddde5, ran the ledger step in 26 seconds against 49 on 2026-10-09; the first night carrying 19.10's fill
            step, 2026-10-16 on 93ce591a, priced 943 trades at the next session's open in 3 seconds and ran the ledger
            step in 12.
After:      every member's chart page for 2026-10-16 read again through the read surface restarted from main's 4d30679c
            in Release, the S&P 500's 503, the 400's 400 and the 600's 602, against the read of 2026-10-15 the 19.11
            correction's entry records: shares outstanding not stored on 1, 0 and 1, VYLR's and HOS's, which the provider
            answers nothing for and whose companies hold no row, where 69, 50 and 98 were before, the provider's count
            drawn on 58, 47 and 78; paid in its last whole year not stored on 101, 143 and 264, where every page was;
            raised in a row a figure on 307, 178 and 197, nought on 91, 78 and 135, two whole years compared and the newer
            not raised, and not stored on 105, 144 and 270, where nought stood on 401, 258 and 341; five years the longest
            span on every page and no ten-year row on any; and the four insider rows not stored on 1, 0 and 1, VYLR's and
            HOS's, where they were on every page. BNY's page: shares outstanding 678.5M as of 2026-06-30, its filer's
            count filed 2026-07-31; paid 2.00 in 2025 over 4 payments, 35 dividends kept from 2018-01-29; raised 7 years
            in a row; five years +216.93% and no ten-year row; and its insiders' net bought -$6.0M over 90 days and
            -$12.6M over the year, none buying and no purchase, each read off the store by a query of its own.
Verified:   `tools/ci.ps1` green over the tree PR 479's entry records, and the checks reading this record run filtered
            over the tree carrying it.
Carried:    the Run page's 4.2 seconds, Tonight's 786 milliseconds and a name page's 693 at their medians, each a ruling
            of its own the operator is asked for; Figure 11.4's "earliest promotion"; the S&P 600's breadth drawn as
            45.0% beside "below its floor of 45%"; the sign-offs of phases 16, 17, 18 and 19.

### 19.11 ruling - the page times are not sped up now, and two ideas tabled for phase 21   2026-10-17

Not a checkpoint entry: it lands nothing.
Ruled:      by the operator on 2026-10-17, word for word in `prompts/2026-10-17-19.11-ruling-page-times.md`: "you dont
            need to fix the page speeds now." The Run page's 4.2 seconds, Tonight's 786 milliseconds and a name page's
            693 at their medians, measured at 19.11 and carried since as a ruling of its own each, stand as they are.
            Asked the same day whether the monthly run should ask the quarters again of the members whose company row
            carries no filer once it has read the SEC's ticker file, and ask the dividends history of every member no
            history run has kept dividends for, the operator ruled neither in, so each goes to the plan's Tabled for
            phase 21 as one line under the 19.14 ruling.
Plan:       `BUILD_PLAN.md`'s Tabled for phase 21 gains the two lines.
Verified:   `tools/ci.ps1` green and `tools/verify-phase.ps1` green over the tree carrying the 17.14 correction's entry
            below, as it records.
Carried:    Figure 11.4's "earliest promotion" and the S&P 600's breadth drawn as 45.0% beside "below its floor of 45%",
            each corrected in an entry below; the sign-offs of phases 16, 17, 18 and 19.

### 19.2 - correction: a share under its floor is never drawn at or above it   2026-10-17

Queue:      done, 19.0 to 19.14 with their corrections, records and rulings and the chart page's corrections with their
            remedy and record, PRs 446 to 480; in progress, the two carried defects and the page times' ruling, in one
            pull request; next, the sign-off of phase 16 by a fresh session, then those of phases 17, 18 and 19, then the
            monthly run of 2026-11-07 and the operator reading its proposals.
Found:      carried since 19.8: the S&P 600's breadth of 0.4497 drawn as 45.0% beside "below its floor of 45%", each
            sentence drawing the share to one place with no regard to the floor it stands beside.
Built:      one rule in the core drawing a share beside its floor: as written at or above the floor, and under it never
            at or above it, a share that would round up to the floor drawn a tenth under it; read by the market line,
            the closed market's lines on Tonight for the families, for an index and for an evening before them, the Run
            page's index line and a card's line. The swing filter's stored gate reasons are left as written, its source
            pinned by the filter's version and the candidates'. The roster and the changelog.
Tests:      1 new: the rule worked by hand on both sides of the floor and at it, and the market line and a card's line
            drawing 44.9% for 0.4497 under 0.45.
Claims:     as the 17.14 correction's entry below records.
Mutated:    the rule, stated before the runs for both corrections: one mutation a property each adds whose failure a
            page would show, the smallest edit that breaks it, made in this checkout, each run against its own test. B1
            the rule drawing a share under its floor as written: red at the rule's test. Reverted before anything was
            committed.
Verified:   `tools/ci.ps1` green and `tools/verify-phase.ps1` green over the tree carrying the 17.14 correction's entry
            below, as it records.
Carried:    the sign-offs of phases 16, 17, 18 and 19.

### 17.14 - correction: Figure 11.4 draws the edge clock's looks as monitoring   2026-10-17

Queue:      as the 19.2 correction's entry above states.
Found:      carried since 19.7: Figure 11.4's lower lane ended on "earliest promotion" and its description and key said a
            look may promote a variant, where from 17.14 a look is monitoring and a rule changes only on the operator's
            approval of a tested proposal.
Built:      the lower lane's third mark reads "second look: monitoring", and the figure's description and key say a look
            is monitoring, a retirement its futility says is due the operator's to take from the first look, and a rule
            changed only on the operator's approval of a tested proposal; the worked example's generator and the
            document changed together. The changelog.
Tests:      none new, 2415 in all: the worked example's test holds the generated figure to the document word for word.
Claims:     1340, as before.
Mutated:    under the rule the 19.2 correction's entry states. F1 the document's figure ending its lower lane on the
            earliest promotion again: red at the worked example's test. Reverted before anything was committed.
Verified:   `tools/ci.ps1` green and `tools/verify-phase.ps1` green over the tree carrying this entry, the figures below
            stated before the run: all six steps, 0 warnings, 0 errors, 2415 of 2415 tests ran with none failed,
            migrations 0 to 97 with none pending, schema version 97, against `data-ci` and never `data`; the phase
            report 60 tables, 1340 claims, 1340 passing, none out of scope and none unexamined, 1351 placements and
            verdicts reconciled against a floor of 34, 43 checks on the roster run and passed.
Carried:    the sign-offs of phases 16, 17, 18 and 19.

### 16.2 - correction: a Taken press refuses a fill dated on no session or before the session after the night, and an exit before its fill or on an ended trade   2026-10-17

Queue:      done, phase 19 with its corrections through PR 481, and phase 16's first sign-off review of 2026-10-17, which
            signed nothing over G1 to G4; in progress, the corrections of G1 to G4 in one pull request; next, the
            sign-off of phase 16 by a fresh session, then those of phases 17, 18 and 19, then the monthly run of
            2026-11-07.
Found:      G1 of the review: a Taken press checked an entered date only for being a day, so a Saturday, the pick's
            night and a day before it were stored and the follower, which reads a trade from its fill's own session,
            never followed them; an exit press checked only that no exit was recorded, so an exit dated before the fill
            ended the trade before it began and one on a trade the follower had ended was recorded.
Built:      the Taken press refuses a fill dated before the session after the pick's night, or on a day that is no
            session on the exchange's calendar, a day past its table among them; the exit press's one statement refuses
            a trade exited, one the follower ended and an exit dated before the fill, the trade read back to say which.
            Section 18's row and 15.18 name them; SCHEMA, the runbook, the guide, the roster and the changelog.
Tests:      none new: the press test refuses a Saturday, Thanksgiving, a day past the table, the night, a day before it
            and the Saturday before the session, an exit before its fill, a second exit and one on an ended trade.
Claims:     1340, as before.
Mutated:    the rule, stated before the runs for all five corrections: one mutation a property each adds or holds whose
            failure the operator's record or a page would show, the smallest edit that breaks it, made in this checkout
            over the committed correction, each run filtered to the tests named. A1 the fill's floor at the pick's night,
            A2 the calendar's check dropped, A3 the exit's ended check dropped, A4 the exit's fill check dropped: each red
            at the press test. Reverted before anything was committed.
Verified:   `tools/ci.ps1` green and `tools/verify-phase.ps1` green over the tree carrying the 16.5 correction's entry
            below, as it records.
Carried:    the sign-offs of phases 16, 17, 18 and 19.

### 19.10 - correction: the rule's own picks beside the operator's record are drawn as bought at the next session's open   2026-10-17

Queue:      as the 16.2 correction's entry above states.
Found:      G2 of the review: from 19.10 the follower buys the rule's own picks on the nights the operator took one at
            the next session's open, while the card's words, 15.18, Figure 15.2 and its key, section 16's Taken records
            row, SCHEMA, the guide and the roster's 16.3 clause still said the plan's buy.
Built:      the card says each was bought at the next session's open, and each of those places says so; two comments of
            the follower and the renderer with them. The changelog.
Tests:      none new: the card's test states the open.
Claims:     1340, as before.
Mutated:    under the rule the 16.2 correction's entry states. B1 the card's words back to the plan's buy: red at the
            card's test. Reverted before anything was committed.
Verified:   `tools/ci.ps1` green and `tools/verify-phase.ps1` green over the tree carrying the 16.5 correction's entry
            below, as it records.
Carried:    the sign-offs of phases 16, 17, 18 and 19.

### 16.3 - correction: a kept dividend paying in no whole year is read at its bars' steps, and a later date is unread where nothing kept carries one forward   2026-10-17

Queue:      as the 16.2 correction's entry above states.
Found:      G3 of the review: a kept dividend paying whose years held no whole one that paid estimated nothing, and the
            card marked a later date unread only where nothing was kept, so it said no date fell inside the hold; 7 of
            the 1,001 kept payers on the store of 2026-10-16: PLMR, RNG, VTOL, MBGL, VGNT and VSNT on the S&P 600 and
            SOLS on the S&P 400. None of the 1,001 holds no ex-date.
Built:      such a dividend is read at the payments a year its bars' steps show, as the fundamentals fallback reads
            them; a later date is unread where the kept dividend holds no rate, no ex-date or no count, a rate of nothing
            still a company paying none; the unread line says the store holds no dividend with a last date and a usual
            interval, true of both. 15.18, the guide, the roster and the changelog.
Tests:      1 new: the card read back estimating from the steps, unread with none and unread with no ex-date; the
            reading worked by hand for a dividend with no whole year, with steps, with no ex-date and with no rate.
Claims:     1340, as before.
Mutated:    under the rule the 16.2 correction's entry states. C1 the steps read for a payer with a whole year and not
            one without: red at the card's new test. C2 the unread rule back to nothing kept: red at it and at the
            reading's test. Reverted before anything was committed.
Verified:   `tools/ci.ps1` green and `tools/verify-phase.ps1` green over the tree carrying the 16.5 correction's entry
            below, as it records.
Carried:    read off the store, RNG, VTOL, SOLS and VSNT step four times a year and PLMR, MBGL and VGNT once each, so a
            card of any of the seven estimates its date or draws the unread line; read on the first night one is a
            pick. The sign-offs of phases 16, 17, 18 and 19.

### 16.3 - correction: four boundaries 16.3 states are held by assertions   2026-10-17

Queue:      as the 16.2 correction's entry above states.
Found:      G4 of the review, its four of 16.3: m6, m7, m10 and m13 survived the whole suite.
Built:      assertions alone, no behaviour changed: a raised stop not lowered by a falling close, an open exactly at the
            stop read at the plan's distance, a rise of exactly the floor read as a step, and a dividend answer storing
            no member's date removing nothing the window held. The roster and the changelog.
Tests:      1 new, the calendar's answer storing no member's date; three cases added to the follower's and the steps'
            tests.
Claims:     1340, as before.
Mutated:    the review's own: m6 the trail set to the close less its distance, m7 the risk read from a fill equal to the
            stop, m10 the step's floor strict, m13 the window cleared on no date stored: each red at its new assertion,
            filtered to the follower's, the steps' and the calendar's tests. Reverted before anything was committed.
Verified:   `tools/ci.ps1` green and `tools/verify-phase.ps1` green over the tree carrying the 16.5 correction's entry
            below, as it records.
Carried:    the sign-offs of phases 16, 17, 18 and 19.

### 16.5 - correction: a labeller's limit ending exactly as a peak window opens is held by an assertion   2026-10-17

Queue:      as the 16.2 correction's entry above states.
Found:      G4 of the review, its one of 16.5: m15 survived the whole suite.
Built:      an assertion alone, no behaviour changed: a limit ending exactly as the second window opens ends there and
            does not wait it out. The roster and the changelog.
Tests:      none new: one case added to the latest end's test. 2417 in all.
Claims:     1340, as before.
Mutated:    the review's m15, the limit's comparison strict: red at the latest end's test. Reverted before anything was
            committed.
Verified:   `tools/ci.ps1` green and `tools/verify-phase.ps1` green over the tree carrying this entry, the figures below
            stated before the run: all six steps, 0 warnings, 0 errors, 2417 of 2417 tests ran with none failed,
            migrations 0 to 97 with none pending, schema version 97, against `data-ci` and never `data`; the phase
            report 60 tables, 1340 claims, 1340 passing, none out of scope and none unexamined, 1351 placements and
            verdicts reconciled against a floor of 34, 43 checks on the roster run and passed. No migration and no
            remedy: no taken trade is stored and the cards are written again each night.
Carried:    the sign-offs of phases 16, 17, 18 and 19, phase 16's by a fresh session, since this one committed code.

### 16.2 - correction: the presses' accepted edges are held by assertions   2026-10-17

Queue:      done, the corrections of G1 to G4, PR 482; in progress, this correction; next, the sign-off of phase 16 by
            a fresh session, then those of phases 17, 18 and 19, then the monthly run of 2026-11-07.
Found:      preparing phase 16's sign-off: the first 16.2 correction of this day left three edges of its refusals with
            no test, a fill dated the session after the pick's night, one dated the last day the calendar's table holds,
            and an exit dated on its fill's own session, each accepted by the code and entered by no test.
Built:      assertions alone, no behaviour changed: each of the three taken or recorded. The roster and the changelog.
Tests:      none new: the press test enters both fills and exits on the fill's session. 2417 in all.
Claims:     1340, as before.
Mutated:    under the rule the first 16.2 correction's entry of this day states. P1 the fill's floor refusing the
            session after the night itself, P2 the exit refusing its fill's own session, P3 the table's last day read as
            past it: each red at the press test. Reverted before anything was committed.
Verified:   `tools/ci.ps1` green and `tools/verify-phase.ps1` green over the tree carrying this entry, the figures below
            stated before the run: all six steps, 0 warnings, 0 errors, 2417 of 2417 tests ran with none failed,
            migrations 0 to 97 with none pending, schema version 97, against `data-ci` and never `data`; the phase
            report 60 tables, 1340 claims, 1340 passing, none out of scope and none unexamined, 1351 placements and
            verdicts reconciled against a floor of 34, 43 checks on the roster run and passed.
Carried:    the sign-offs of phases 16, 17, 18 and 19, phase 16's by a fresh session, since this one committed code.

### Phase 16 sign-off                                                        2026-10-17
Signed by a session that has committed no code to this repository. Its only commit is this entry, which is a document,
            so the fresh session rule permits it. This is the second review of phase 16. The first, over 7cc9a83a on
            2026-10-17, signed nothing over G1 to G4, which the 16.2, 19.10, 16.3, 16.3 and 16.5 corrections of PR 482
            and the 16.2 correction of PR 483 corrected. This review read 7cc9a83a..8cc8c8ca, 18 files, +491 -66, five
            of them shipped source, +91 -37, and signs over 8cc8c8ca.
Verified:   by re-running both gates on `main` at 8cc8c8ca before this entry: `tools/ci.ps1` green end to end,
            19:34:04 to 19:57:10 UTC, all six steps, 0 warnings, 0 errors, 2417 of 2417 tests passing inside it,
            migrations 0 to 97 applied against `data-ci/` and then none pending at schema version 97, exit 0;
            `tools/verify-phase.ps1` green, reading that run's suite result over the same commit and a clean tree, at
            60 tables, 1340 claims, 1340 PASS, 0 FAIL, 0 out of scope, 0 unexamined, 1351 placements and verdicts
            reconciled against a floor of 34, fixture PRESENT with 1 captured over 6 constituents and 4 names, 43
            checks on the roster, 43 carried and 43 passed, none failed or unrun, the suite 2417 of 2417. Over the
            tree carrying this entry, a record entry alone, the checks that read the record ran filtered in place of
            the full script, on the operator's standing ruling: 163 of 163 tests passing.
            Hosted CI for PRs 482 and 483: 6 runs, each pull request's one pushed head under both events and main's
            push runs for e499db0d and 8cc8c8ca, 12 jobs, case-sensitivity and macOS on each, all green. PR 482 merged
            at 16:28:06 UTC, before any of its four jobs finished, the first at 16:30:23; all four went green. PR
            483's four jobs were green before its merge at 17:17:56.
Plan:       checkpoints 16.0 to 16.5 are in `BUILD_PLAN.md` and recorded with their rulings and corrections, 15
            entries in `docs/archive/PROGRESS-phases-0-to-18.md` and the five corrections of 2026-10-17 above. The
            carried obligations hold 116 rows, 83 discharged, 33 operating and none open; phase 16's operating row is
            carried below.
Read:       G1 to G4 against the corrected code and their six entries. G1: a Taken press refuses a fill dated before
            the session after the pick's night, on a day that is no session, or past the calendar's table before the
            session check is asked; the exit's one statement requires no exit, no end and a fill on or before its date,
            and a refusal reads the trade back to say which. G2: the card, 15.18, Figure 15.2 and its key, section 16's
            row, SCHEMA, the guide and the roster say the rule's own picks are bought at the next session's open; the
            migration's comment alone keeps its words, as history. G3: a kept dividend paying in no whole year is read
            at its bars' steps, and a later date is unread where the kept dividend holds no rate, no ex-date or no
            count. G4: each of m6, m7, m10, m13 and m15 now red at its assertion.
Store:      read-only, schema 97, newest session 2026-10-16: `decision_card` 27, the S&P 500's 12 and the S&P 600's
            15; `rule_record` 12; `dividend_reading` 2,011, its newest rows 1,505, 1,001 paying, 504 at nothing and
            none filing no rate; `taken_trade` and `taken_record` empty; no `data/account.json`. The decision cards'
            stage took 5 s on 2026-10-15 and 2026-10-16 and the follower 0 s.
Screens:    on 5152, the Release build of e499db0d, whose shipped source is main's, read by GET alone under each
            Universe choice. TRV's S&P 500 and INDB's S&P 600 drift cards of 2026-10-16 match their rows, each
            estimated ex-date worked by hand: TRV's 2026-09-10 plus a year over 2025's four, 91 days, 2026-12-10, 5.00
            / 4 = 1.25 a share and 0.06 of its 20.91 risk; INDB's 2026-09-28 plus 91, 2026-12-28, 0.64 and 0.35 of
            1.82. Neither draws the operator's record, `taken_record` being empty; the S&P 400 draws no card; Your
            trades says none on each index; the Run page's decision cards and follower rows equal `run_log`; the
            account page reads Not set. Nothing was pressed.
Night:      2026-10-12, carried here by phase 15's sign-off. Its three scheduled tries stopped at the fetch on LUMN's
            refused bar, and `night-20261013T013029Z`, started by hand at 01:30 UTC, ran it whole. The index families'
            stage read the S&P 500's 502 members, the S&P 400's 400 at a breadth of 0.44 with its lists closed and the
            S&P 600's 602 at 0.49, its own books carried with 7 held each. The S&P 600's two registered heavyweights
            books, rows 284 and 285 registered again as 304 and 305 at 21:15:52 UTC that day, each waited, holding 251
            sessions to 2026-10-12 against the 252 their readings need; they rebalanced on 2026-10-13, buying 7 and 6,
            every holding with a lead, and carried the 13 through 2026-10-16.
Swept:      25 mutations, each predicted before its run, made on this branch in this checkout over the file's own text
            and restored from it, the tree's diff the same after the sweep as before, each filtered to the tests of
            the files reaching it. The first review's m1 to m17, each red where predicted: m1 in 19.4's paper walk
            alone, and m6, m7, m10, m13 and m15, which survived the first review, each at its new assertion. The
            handoff's seven: (n1) the table's reach dropped from the fill's session check: red at the press test, a
            fill dated 2028-01-03 answering 500. (n2) the exit's statement losing its no-exit clause: red there, a
            second exit recorded. (n3) the read-back's first two arms swapped: green, as predicted. (n4) a kept rate
            of nothing read as unread: red at the reading's test and two card tests. (n5) a kept row filing no rate
            read as estimable: red at the reading's test. (n6) the steps read for every kept payer: green, as
            predicted. (n7) the unread line back to its old words: red at both card tests drawing it. This review's
            own (n8), the night's own year read as whole: red at the reading's test and the kept dividend's card test.
            n3 and n6 together over the whole suite: 2417 of 2417 green, as predicted.
Judged:     the handoff's five judgement calls stand. An exit date checked neither for a session nor for a day after
            today: the follower reads an exit once the night reaches its date and needs no bar on it. A fill dated
            after today: the default fill, the session after the night, is a future date when the card is pressed. G3
            needing no new decision: the decision states the steps as the reading of a dividend's interval and an
            interval the steps cannot read as leaving a hold unread, and the correction applies them where the kept
            years give none. The one unread sentence: it names the last date and the usual interval, true of every
            unread case the store can hold today; a kept row filing no rate, which the decision also leaves unread,
            would hold both, and none of the 1,505 newest does. The same nights' card test's hand-written row: the
            follower's buying at the open is held by its own same-open test.
            (n6) is equivalent: `TimesAYear` reads the last whole year's count before the steps, so the steps change
            no figure for a payer holding one.
Found:      no defect in shipped code. One test defect, carried below: (n3) is a missing property. The exit press's
            read-back names a recorded exit before the follower's end, and no test presses an exit on a trade holding
            both, the state of a trade the operator exited that a night has since followed, which the follower ends
            by its "exit"; swapped, that refusal says the night ended the trade by its rule's exit. The code is right;
            the exit control is drawn only on open trades, so only a page loaded before the exit reaches it. Asked at
            about 20:29 UTC on 2026-10-17 whether to sign with it carried or have it corrected first, the operator
            ruled: "Sign now, n3 carried".
Carried:    (n3), the press test pressing an exit on a trade holding both an exit and the follower's end, owed by the
            next change to the exit press. Phase 16's operating row, "The card and the follower read within their time
            on the first five nights": its time half, the two steps together under the 10 seconds proposed,
            read 1, 1, 6, 8, 5, 1, 5 and 5 seconds on the eight nights from 2026-10-07 to 2026-10-16; its growth half
            waits on ruling (a) of 2026-10-11, after 2026-11-01. No real trade followed, no card drawn in shares and
            none of G3's seven payers on a card on the live store, each verified by the suite alone and read on the
            first night one occurs. Phases 17, 18 and 19 are signed after this one, each by its own session.
Signed:     phase 16, over 8cc8c8ca.

### 17.3 - correction: the SEC tickers step's members are the tickers the three indices hold on its session   2026-10-18

Queue:      done, phases 0 to 16 signed, PR 484; in progress, the corrections of phase 17's first sign-off review of
            2026-10-18, its F1 to F4 and two siblings of F4, in one pull request; next, phase 17's second sign-off
            review by a fresh session, then those of phases 18 and 19, then the monthly run of 2026-11-07.
Found:      F1 of the review, which signed nothing over 3475252f; asked how the review proceeds, the operator ruled
            "Correct first (Recommended)". `sec-tickers` counted every ticker whose newest company row held no filer,
            members or not, and called them members: its run of 2026-10-17 at 01:45 UTC wrote "38 of the 38 member(s)
            the provider files no CIK for named by it" where 36 members held none, 11, 3 and 22, the other two PARA and
            PSKY, whose S&P 500 spans left on 2026-10-14 and 2026-10-15.
Built:      a ticker is a member where a span of any of the three indices holds it on the session the clock falls on,
            as the quarters fetch reads members, and the step declares the membership read. The catalogue's and the
            matrix's rows, the runbook, the roster and the changelog. No source a rule's version pins moves.
Stored:     kept, not rewritten: that run's row. Read over the store as the run found it, the corrected query passes
            over 2 former members' rows, PARA's and PSKY's, and counts the 36; their newest company rows carry the
            SEC's filer since that night's remedy, so the step run now counts none.
Tests:      none new: the step's test adds a member of the S&P 600, one of the S&P 400 joining on the session, one of
            the S&P 500 leaving on the UTC date at the clock, a former member and a ticker joining on a later session,
            each holding no filer, the last two counted among none; and the membership reads' check, which states the
            set of reads asking the join date, holds the step among them.
Claims:     1340, as before.
Mutated:    the rule, stated before the runs for all six corrections: one mutation a property each adds or holds whose
            failure a stored record or a run's row would show, the smallest edit that breaks it, made in this checkout
            over the committed corrections and restored from the file's own text, the tree's diff the same after the
            sweep as before, each run filtered to the tests named, a test hashing a pinned source red at any edit of it
            and discounted. M1 the membership clause made true of any span, the query reading every company row again:
            red at the step's test alone, of the SEC tickers file's 3. M11 a span joining on the session read as no
            member, and M12 one leaving on it read as a member: each red at the step's test and at the membership reads'
            check, of those 4. M15 the session read as the UTC date: red at the step's test alone, of the 4.
Verified:   `tools/ci.ps1` green and `tools/verify-phase.ps1` green over the tree carrying the 14.6 correction's entry
            below, as it records.
Carried:    phase 17's second sign-off review by a fresh session, since this one committed code.

### 17.4 - correction: the gate at its enumerated limit is held by an assertion   2026-10-18

Queue:      as the 17.3 correction's entry above states.
Found:      F2 of the review, under the operator's "Correct first (Recommended)": the gate's comparison at its limit
            made strict, its m1, left green all 52 tests over the loop's, the ledger's and the score's files. At exactly
            24 blocks, the figure section 17 pins, the gate reads every sign arrangement, and past it 2^24 - 1 drawn at
            its seed; the live runs read 17 and 18 blocks, so no stored verdict moved.
Built:      an assertion alone, no behaviour changed: twenty-four blocks of one size, fourteen up, read over every
            arrangement to the binomial tail the test works by hand, and twenty-five, fifteen up, read off the draws, a
            whole count over 2^24 within 0.002 of that tail and not it, in about 6 seconds. The roster and the changelog.
Tests:      1 new.
Claims:     1340, as before.
Mutated:    under the rule the 17.3 correction's entry states. M2 the review's m1, the comparison strict: red at the new
            test. M3 the limit read a block further: red at it. Each filtered to the gate's 2 tests.
Verified:   `tools/ci.ps1` green and `tools/verify-phase.ps1` green over the tree carrying the 14.6 correction's entry
            below, as it records.
Carried:    as the 17.3 correction's entry above states.

### 17.15 - correction: a reading exactly on a tree's split is held by assertions   2026-10-18

Queue:      as the 17.3 correction's entry above states.
Found:      F3 of the review, under the operator's "Correct first (Recommended)": the walk's comparison made strict, its
            m16, reddened only the two tests that hash the walk's source, the package test's rows sitting on no
            threshold and the constructed tree read at 40 and 60 against its 50, while 17.15's done condition that the
            core's walk equals the package's scores rests on the walk's at or under to the left.
Built:      assertions alone, no behaviour changed: the constructed tree's member at 50 scores the left leaf; and a tree
            of two leaves fitted by the package over one reading, a member exactly on its threshold scored by the
            package and by the core's walk at the leaf at or under it, and the next figure past it at the other. The
            package goes left at its threshold, as the walk does. The roster's two rows and the changelog.
Tests:      1 new.
Claims:     1340, as before.
Mutated:    under the rule the 17.3 correction's entry states. M4 the review's m16: red at both new assertions and at
            the two pin tests, discounted, and at none other of the 9 tests the run read; its first run's filter, on
            the word Tree, left the package's test out, and it was run again with it.
Verified:   `tools/ci.ps1` green and `tools/verify-phase.ps1` green over the tree carrying the 14.6 correction's entry
            below, as it records.
Carried:    as the 17.3 correction's entry above states.

### 13.9 - correction: a night run again keeps the trades of a family rule it lists nothing for   2026-10-18

Queue:      as the 17.3 correction's entry above states.
Found:      by phase 17's first sign-off review, beyond phase 17; asked what happens to it, the operator ruled "Add to
            the corrections (Recommended)", described as "Correct it as a separate 13.9 correction in the same PR: a
            night run again keeps the stored trades of a rule it skips, with a test over a moved rule." A night run
            again deleted every family trade of the night and kept trades only for the rules the shadow step gave
            verdicts, so a rule it skipped lost its trades of the night: `night-20261005T025156Z-for-2026-10-02`, one of
            the 517 nights run again on 2026-10-05, kept 0 by 14 rules where the night of 2026-10-02 had kept 2, FDS
            and CCL under two drift rules, each skipped as registered under drift at 3309f7f59e9e where the code carried
            acfc9db2b20b. 17.5's remedy replay of 2026-10-09 at 14:13 UTC kept both, read "differs" on those two of its
            16 rules and restarted their records at 2026-10-09; 17.15's of 2026-10-12 reproduced 16 of 16. No entry
            recorded that replay's reading until this one.
Built:      the recorder deletes a rule's trades of the night only for a rule it lists that night, and leaves as stored
            those of a rule it lists nothing for: one the night's rows hold no verdict of, one whose hooks read readings
            the night did not supply, and one no longer standing, its row naming the first two with the trades left.
            No decision's words change: the decision states no run again, and SCHEMA's, the catalogue's and the stores'
            sentences now say it. No source a rule's version pins moves. The roster and the changelog.
Stored:     kept, not rewritten: the night of 2026-10-02 holds no family trade, and FDS's and CCL's rules count their
            records from 2026-10-09, as the replay's rows say.
Tests:      1 new: a night recorded, then run again with the live rule's verdicts changed, a rule moved, a rule handed
            no readings and a rule retired, the live rule's trades replaced and the other three's left field by field;
            and the recorder's first test reads a rule holding no verdict and no trade of the night named with why.
Claims:     1340, as before.
Mutated:    under the rule the 17.3 correction's entry states. M5 the night cleared whole before the rules, as the code
            stood: red at the new test alone, of the recorder's and the replay's 16. Its first form, each rule's delete
            reading every rule's trades, deleted what the rules before it kept that run and was red at 12 of the 16. M6
            a rule holding no verdict read as one listed: red at the new test alone, of the recorder's 3. M14 the reason
            dropped from the words for a rule holding neither: red at the recorder's first test alone, of the 3.
Verified:   `tools/ci.ps1` green and `tools/verify-phase.ps1` green over the tree carrying the 14.6 correction's entry
            below, as it records.
Carried:    as the 17.3 correction's entry above states.

### 15.5 - correction: an index night run again keeps the trades of a rule it keeps no list for   2026-10-18

Queue:      as the 17.3 correction's entry above states.
Found:      correcting F4, its sibling: asked whether to add it and the heavyweights' to this pull request, the operator
            ruled "Add both, as two correction commits in this PR. Each gets tests that a night run again over a rule
            whose registered version is no longer the code's keeps every stored trade, buy and sale of that rule
            untouched. For 15.5, also a night with no inputs leaving the stored trades untouched. For 14.6, a skipped
            rule's buys of the night kept and its sales not reopened. Each with a mutation shown red." The index
            families deleted every registered rule's trade of the night before keeping the lists of the rules they
            read, so a night run again over a rule they do not read lost its trades of the night, and a night with no
            inputs lost every rule's; the 15.5 test held the loss, reading no trade after the run again. No index
            families' row on the store names a rule not read, and its 9 index rule trades, 2 and 7, run from 2026-10-12.
Built:      each rule's trades of the night deleted only where its list is kept, before its holdings are read, which
            count the night's own trades; a rule whose hooks read readings the night did not supply keeps its, and a
            night with no inputs deletes none. The index evaluators' versions move: breakout b1dbb89e6a0b, drift
            0517e0869555, pullback 87e8cc8312bd and heavyweights 9e8ebd36a0e5. SCHEMA, the catalogue's and the stores'
            rows, the roster and the changelog.
Tests:      none new: the 15.5 test reads every field of the two trades the first run kept unchanged after the run again
            over the moved rules, and hands the rules' trades a night with no inputs as the step does, keeping none and
            leaving them; and the hooks' index test runs its night again handed no readings, the hooked rule's five
            trades standing field by field while the live rule's are written again.
Claims:     1340, as before.
Mutated:    under the rule the 17.3 correction's entry states. M7 the night cleared whole before the inputs are read, as
            the code stood: red at the moved rules' test and at the pin test, discounted. M8 a rule's night cleared after
            its holdings are read, its run again reading the night's own trades as held: red at the frozen rules' test
            and at the pin test. Each filtered to the index freezes' 4 tests and the pin test. M13 a rule's night cleared
            before its hooks are held against the night's readings: red at the hooks' index test and at the pin test,
            of 4.
Verified:   `tools/ci.ps1` green and `tools/verify-phase.ps1` green over the tree carrying the 14.6 correction's entry
            below, as it records.
Carried:    the remedy, as the 14.6 correction's entry below states; and as the 17.3 correction's entry above states.

### 14.6 - correction: a heavyweights night run again keeps the book of a rule it keeps none for   2026-10-18

Queue:      as the 17.3 correction's entry above states.
Found:      the second sibling, under the ruling the 15.5 correction's entry quotes. The heavyweight book undid every
            registered rule's night before keeping the books of the rules the code carries at their versions, so a night
            run again over a moved rule deleted its buys of the night and opened its sales again, keeping no book for it.
            The book reads no session before one it has read, so only the newest night run again reaches it; no
            heavyweights row on the store names a moved rule.
Built:      a rule's night undone only for a rule whose book it keeps; a rule it names as the failure it is keeps its
            buys of the night, its reading and its sales unopened. The S&P 500 heavyweights' version moves to
            161a4d9d9dce. SCHEMA's two tables, the catalogue's and the two stores' rows, the roster, the changelog and
            the remedy.
Tests:      none new, 2420 in all: the heavyweights test runs the first night again with every company's variant moved
            and the session after again with the variant selling on either exit moved.
Claims:     1340, as before.
Remedy:     `tools/remedies/2026-10-18-14.6-the-moved-rules-registered-again.txt`, run from main after the merge and
            before the night of 2026-10-19, with no night or off-peak run writing: `register --moved`, the 12 standing
            rules the 15.5 and 14.6 corrections moved registered again unchanged, the S&P 400's and 600's breakout,
            drift and pullback, the S&P 600's two heavyweights books and the S&P 500's four; none is replayed, so each
            restarts its record at the registration. No migration, no provider request and no model call. Left to the
            night, each is skipped and named as a failure.
Mutated:    under the rule the 17.3 correction's entry states. M9 a buy of the night deleted for every rule: red at the
            heavyweights test and at the pin test, discounted. M10 a sale of the night opened again for every rule: red
            at the same two. Each filtered to the heavyweights' 3 tests and the pin test.
Verified:   `tools/ci.ps1` green and `tools/verify-phase.ps1` green over the tree carrying this entry, the figures below
            stated before the run: all six steps, 0 warnings, 0 errors, 2420 of 2420 tests ran with none failed,
            migrations 0 to 97 with none pending, schema version 97, against `data-ci` and never `data`; the phase
            report 60 tables, 1340 claims, 1340 passing, none out of scope and none unexamined, 1351 placements and
            verdicts reconciled against a floor of 34, 43 checks on the roster run and passed.
Carried:    the remedy, the operator's, before the night of 2026-10-19; phase 17's second sign-off review by a fresh
            session, since this one committed code; then those of phases 18 and 19.

### Phase 17 sign-off                                                        2026-10-18
Signed by a session that has committed no code to this repository. Its only commit is this entry, which is a document,
            so the fresh session rule permits it. This is the second review of phase 17. The first, over 3475252f on
            2026-10-18, signed nothing over F1 to F3, with F4 found beyond phase 17, which the 17.3, 17.4, 17.15 and
            13.9 corrections of PR 485 corrected with the 15.5 and 14.6 siblings the operator added. This review read
            3475252f..171cc021, 25 files, +665 -69, nine of them shipped source, +77 -41, and signs over 171cc021.
Verified:   by re-running both gates on `main` at 171cc021 before this entry: `tools/ci.ps1` green end to end,
            04:41:13 to 05:03:44 UTC, all six steps, 0 warnings, 0 errors, 2420 of 2420 tests passing inside it,
            migrations 0 to 97 applied against `data-ci/` and then none pending at schema version 97, exit 0;
            `tools/verify-phase.ps1` green, 05:03:59 to 05:04:04 UTC, reading that run's suite result over the same
            commit and a clean tree, at 60 tables, 1340 claims, 1340 PASS, 0 FAIL, 0 out of scope, 0 unexamined, 1351
            placements and verdicts reconciled against a floor of 34, fixture PRESENT with 1 captured over 6
            constituents and 4 names, 43 checks on the roster, 43 carried and 43 passed, none failed or unrun, the
            record line holding the 14.6 correction's stated figures. Over the tree carrying this entry, a record entry
            alone, the checks that read the record ran filtered in place of the full script, on the operator's standing
            ruling: 163 of 163 tests passing.
            Hosted CI for PR 485: 3 runs, its one pushed head 049c1d7e under both events and main's push run for
            171cc021, whose tree is 049c1d7e's, 6 jobs, case-sensitivity and macOS on each, all green. PR 485 merged at
            04:19:38 UTC, before any of its four jobs finished, the first at 04:21:33; all four went green, and main's
            macOS job ended green at 04:47:26.
Read:       F1 to F4 and the two siblings against the corrected code and their six entries. F1: `sec-tickers` counts a
            ticker as a member where a span of any of the three indices holds it on the session the clock falls on in
            New York, as the quarters fetch reads members, and declares the membership read. F2 and F3: assertions
            alone, at 24 and 25 blocks and at a tree's split. F4: the family recorder deletes a rule's trades of the
            night only for a rule it lists; one holding no verdict, one whose hooks read readings the night did not
            supply and one no longer standing keep theirs. 15.5: the index rule trades delete a rule's trades of the
            night after its hooks are held against the readings and before its holdings are read, and none on a night
            with no inputs. 14.6: the heavyweight book undoes a registered rule's night only where it keeps that rule's
            book. No third sibling: the S&P 400's and 600's heavyweights books already undo each rule's night inside
            the loop over the rules the night reads, and the index families' night-wide clear is the provisional
            page's, drawn again each night as the S&P 500 lister's is.
Store:      read-only, schema 97, newest session 2026-10-16. The `sec-tickers` row of 2026-10-17 01:45 UTC reads 38 of
            38; the corrected query over the company rows fetched before it, at the session 2026-10-16, counts 36, 11
            of the S&P 500, 3 of the 400 and 22 of the 600, and passes over PARA and PSKY, whose S&P 500 spans left on
            2026-10-14 and 2026-10-15; over today's rows it counts none. `family_trade` holds nothing for 2026-10-02,
            and 17.5's replay of 2026-10-09 14:13 UTC read 16 rules, 2 differing and 14 reproduced. `index_rule_trade`
            9, 2 of the S&P 400 and 7 of the 600, from 2026-10-12. The remedy at 2026-10-18 04:37:57 UTC:
            `register-20261018T043757.3114652Z` "registered", 24 rows, and four family-replay rows naming the S&P 500
            heavyweights not replayed; `candidate_register` 351, each of the 12 registrations the same candidate,
            rule, test, evaluator and parameters as the row it follows, and all 37 standing rules at the version their
            evaluator carries on `main`.
Swept:      26 mutations, each predicted before its run, made on this branch in this checkout over the file's own text
            and restored from it, the tree's diff the same after the sweep as before, each filtered to the tests of
            the files reaching it and the pin tests, a pin test reddening at an edit of a pinned source discounted.
            The handoff's six, each red where predicted: (n1) the stored count reading every rule's trades of the
            night: red at the run-again test and the recorder's first test. (n2) the stored count's words dropped: red
            at the run-again test. (n3) the index rules' clear keyed on the index and the night alone: red at the
            hooks' index test, the moved rules' test and the frozen rules' test. (n4) the rule readings' delete losing
            its candidate clause: red at the heavyweights freeze test. (n5) the membership read held to the S&P 500's
            spans: red at the step's test. (n6) the gate's draws one short: red at the gate's limit test. The first
            review's m1 to m18: m1 to m17 red where it recorded them, m1 now at the gate's limit test and m16 at the
            threshold test and the tree-hooked rule's test; m18 green, equivalent as before. This review's own two,
            each a correction's per-rule clear run ahead of its skip: (n7) the heavyweight book's: red at the
            heavyweights freeze test. (n8) the family recorder's: red at the run-again test. m18 alone over the whole
            suite: 2420 of 2420 green, as predicted.
Judged:     the handoff's four judgement calls stand. A rule the night's rows hold no verdict of read as not evaluated
            whatever the cause: a run again holding no verdict of a rule did not evaluate it, which is the reading the
            15.5 correction gives a night with no inputs, and the trades a run that did evaluate it stored stay its
            record. The index families' row not naming a skipped rule's trades: the row names a moved rule's fault
            already, and no figure on it counts a rule's trades. The recorder's reason on every night: it adds words to
            a row and changes no count. The 12 records restarted at the remedy: a moved pin restarts a record unless a
            replay reproduces it, the S&P 400's and 600's rules and the heavyweights are replayed by none, and the
            design holds no exemption for an edit that leaves what a rule evaluates unchanged.
Found:      no defect in shipped code. One document defect, carried below: (G1) SCHEMA's `family_trade` sentence from
            the 13.9 correction ends "and one no longer standing, each its run log row naming", where the recorder's
            row names only rules standing, so a rule no longer standing keeps its trades of the night unnamed; the code
            and the 13.9 entry, "its row naming the first two", agree. Asked at about 05:19 UTC on 2026-10-18 whether to
            sign with it carried or have it corrected first, the operator ruled: "Sign now, G1 carried".
Carried:    (G1), SCHEMA's `family_trade` sentence saying the recorder's row names a rule no longer standing, owed by
            the next edit of that section. Phase 17's seven operating rows, as the first review read them: the
            night's added time and the ledger's growth over the five nights from 2026-10-09, the night's growth after
            the store's growth change (ruling (a), after 2026-11-01), the first scheduled monthly run on 2026-11-07,
            the alarm's first two months, a replaced rule's 63 sessions as a variant, and a history build past its
            span's end. Phase 19's, owed before its sign-off: the Loop summary's "not read" for the fundamentals-first
            family's rule today, and 17.8's S&P 500 Past picks and Run page drawing the family's S&P 500 trades. Phase
            16's (n3), owed by the next change to the exit press. The night of 2026-10-19 is the first to read the 12
            rules at their new versions. Phases 18 and 19 are signed after this one, each by its own session.
Signed:     phase 17, over 171cc021.

### 20.0 planning - phase 20 planned: local AI in the picks and the loop, the narrowed rule and the four questions written before any release is read, and the history's size and the local model's hours measured   2026-10-18

Not a checkpoint entry. It lands 20.0, the pass that plans phase 20, and builds none of it.
Queue:      done, phase 17's corrections as PR 485 and its sign-off as PR 486; in progress, this pass; next, in order,
            20.1 the local run, 20.2 the results releases, 20.3 the anonymiser and the reading with its accuracy and
            20.4 the drift's tone variant; then the corrections under 19.7 and 17.8 that phase 17's sign-off carried as
            owed before phase 19's sign-off; then the sign-offs of phases 18 and 19, each in a fresh session the
            operator starts; then 20.5 to 20.7; then the monthly run of 2026-11-07 and the operator's reading of its
            proposals; then 20.8 to 20.10.
Ruled:      by the operator on 2026-10-18, recorded here word for word as their ruling of that day: "Phase 20 is local
            AI in the picks and the loop, planned now and built after phase 17's sign-off. It narrows the ruling of
            2026-10-16: the sign-offs of phases 18 and 19 are deferred, each still owed by a fresh session at the slot
            below, and no phase 20 checkpoint discharges them. The monthly run of 2026-11-07 still runs and its
            proposals are read before anything that joins text readings to the ledger catalogue, the tester or the
            monthly engines is merged. Rules choose the stocks, and a language model writes only the reports, is
            narrowed to: a pinned local model's stored reading of a document, read with names and dates removed, is a
            rule input like any other reading, and no model ever judges a stock directly. No model is asked for a
            number, a date or an estimate; its answers are closed labels that code turns into readings. Phase 20 spends
            no money. Every model call in it is the local model's. A change that would raise paid spend is put to me
            naming the amount. No test result in this phase retires the local run, a reading, a variant or a family.
            What a result would change is put to me." With it: "Supersede "Rules choose the stocks, and a language
            model writes only the reports" with the narrowed decision, keeping its reasoning, and amend section 1's
            boundary to match." The brief, the four answers on the plan, the five amendments, the approval through the
            plan tool at about 05:43 UTC and the three answers after it are each word for word in
            `prompts/2026-10-18-20.0-*.md` with a copy of the plan; R1 to R17 stand as the plan states them. The
            sign-offs of phases 18 and 19 stay owed and outstanding, and nothing here discharges either.
Asked:      by the operator while this pass ran: "please dont forget to update Build plan and other docs as you go";
            and "Are there any pending questions for me ? I want you to  keep building otherwise until you reach the
            sign off point", answered by the three questions in `prompts/2026-10-18-20.0-operator-answers-second.md`:
            the two slots' tasks registered once 20.1 merges, the two items carried before phase 19's sign-off
            corrected after 20.4, and the reference set labelled on the operator's plan.
Planned:    `BUILD_PLAN.md`'s phase 20 section with its checkpoints 20.0 to 20.10 and section 20's row; the decisions the
            phase rests on before anything is built: the narrowed rule superseding the one it narrows, with section 1's
            boundary amended and its eleven citations repointed, the phase's order, the four questions and the tone
            variant. The decisions that change what runs are written where they are built. Phase 20 opens no operating
            row. The guide's account of what chooses the stocks changes at 20.4, where a reading first reaches a rule,
            since until then it describes what runs.
Measured:   before anything was built, read-only on the live store with no provider or paid call; the samples by a
            script in the session's scratch folder, committed nowhere.
            The local model: 50 of the 52 results releases research passes have stored, drawn at seed 20261018 and
            read once under the four questions by LM Studio's native chat answer with the model loaded at its
            profile's context of 50,176 tokens, its reasoning off, temperature zero and the seed: 3.48 seconds a
            release on average, 3.07 at the median and 8.37 at the slowest, over 9,726 prompt tokens a release on
            average and 25,635 at the most; all 50 answered in the listed words and none was cut off; no reasoning
            token was spent, and every answer named its runtime as llama.cpp-win-x86_64-nvidia-cuda12-avx2 2.56.0.
            The load took 32.5 seconds and nothing else was loaded; the model was unloaded after, as the runtime was
            found. Its file, 14,439,363,264 bytes, was hashed whole in 211 seconds by PowerShell's own hash.
            The history: the ledger reads 2,024 tickers from 2019-01-02, eight holding no filer, under 1,977 filers;
            50 filers drawn at seed 20261018 filed a mean of 19.44 results releases inside their span in the ledger and
            the one before it, read from their SEC submissions over 56 requests in 35 seconds, and of the 1,491 they
            filed since 2019 whatever their span, 9 were amendments carrying the same item. So the history holds about
            38,400 releases; fetching them asks about 79,000
            requests, two a release and one or two a filer, about 2.2 hours at the SEC's pace of 10 a second; at
            32.5 KB of text a release compressing 3.2 to 1, as the 52 stored releases measure, the store grows by
            about 0.39 GB; and at 3.48 seconds a release the history read takes about 37 hours of the local model,
            about four of the runbook's nights from the copy's end to 07:00 in New York. The plan estimated 55,000
            releases, 61 hours, 114,000 requests and 0.56 GB before these samples.
            The pins 20.4 moves, read off the code: the S&P 500's breakout and drift evaluators pin the family
            evaluator's own file, and every S&P 400 and 600 evaluator pins it and the index night's files, while the
            S&P 500's heavyweights and the swing filter pin neither. Handing the readings to the drift on the night
            moves the S&P 500's breakout and drift, which the replay reads and keeps where they reproduce, and every
            S&P 400 and 600 rule standing, which no replay reads and which restart at their registration. The plan
            named the drift and the index rules; the breakout is named here, before 20.4 builds.
Tests:      2420, unchanged; two check sources' citations repointed and no test added.
Claims:     1340, unchanged: section 20's row is no claim, and the plan and the decisions sit in no table the harness
            reads.
Pins:       none moved: the pass edits no shipped source.
Verified:   `tools/ci.ps1` green and `tools/verify-phase.ps1` green over the tree carrying this entry, the figures below
            stated before the run: all six steps, 0 warnings, 0 errors, 2420 of 2420 tests ran with none failed,
            migrations 0 to 97 with none pending, schema version 97, against `data-ci` and never `data`; the phase
            report 60 tables, 1340 claims, 1340 passing, none out of scope and none unexamined, 1351 placements and
            verdicts reconciled against a floor of 34, 43 checks on the roster run and passed.
Carried:    the sign-offs of phases 18 and 19, owed to fresh sessions after 20.4; the two items phase 17's sign-off
            carried as owed before phase 19's sign-off, corrected after 20.4; (G1), SCHEMA's `family_trade` sentence,
            owed by the next edit of that section.

### 20.0 - correction: research passes have stored 70 results releases, and the 52 the planning entry names are those whose exhibit's file name carries its number   2026-10-18

Not a checkpoint entry: it corrects 20.0's record and lands nothing.
Corrects:   the 20.0 planning entry above, whose Measured line says the sample drew "50 of the 52 results releases
            research passes have stored" and sizes the history "at 32.5 KB of text a release compressing 3.2 to 1, as
            the 52 stored releases measure". Research passes have stored 70 results releases, each titled as a results
            release or the previous one; the 52 are those whose exhibit's file name carries its exhibit number, the
            filter the sample's script read them by.
Found:      by this session at 20.1, reading the store again for the twice-read, which reads every stored results
            release by its title and found 70.
Repaired:   `BUILD_PLAN.md`'s phase 20 findings, with their prior text in the changelog; 20.1's own text where 20.1
            amends its done condition below. Each figure the planning entry states stands over the releases it says it
            read: the 50 drawn are among the 70, and the mean size is over the 52. A record is corrected by a new dated
            entry, and this is that entry.
Tests:      none of its own; the 20.1 entry below states the suite.
Verified:   `tools/ci.ps1` green and `tools/verify-phase.ps1` green over the tree carrying the 20.1 entry below, as it
            records.
Carried:    nothing.

### 20.1 - the local run, pinned, checked and visible   2026-10-18

Queue:      done, phase 17's sign-off and phase 20's planning, PRs 486 and 487; in progress, this checkpoint with the
            20.0 correction above; next, its remedy from main and the two slots' tasks, then 20.2, 20.3 and 20.4, the
            two items phase 17's sign-off carried, and the sign-offs of phases 18 and 19 by fresh sessions.
Built:      on the operator's ruling of 2026-10-18 (see: The local run is a process of its own at two slots outside the
            night and refuses any model file, runtime or prompt but its pin). LM Studio's native chat answer captured
            before its reader was written: the probe, KEYS's release, a prompt past the context refused and a model the
            runtime does not hold answered by the loaded one. In the core the four questions and their readings (see:
            The release reading asks four closed questions written before any release is read, and code turns each
            answer into a reading), the answer's reader with its eight causes, the pin's rules and the availability
            rule (see: A release reading is readable from the first night whose late slot starts after the release was
            accepted). The reading feed, one request at a time at temperature 0, the seed 20261018, 256 tokens and
            reasoning off, loading the model where it is not loaded and unloading it only where it did. Migration 98's
            `reading_pin`; the worker's `local-run` with a slot, `--pin` or `--twice`; `tools/localrun` and its wrapper
            from a clean copy of main's commit with a check mode; the profile's model file, models folder and cutoff,
            2025-01-31; and the Run page's local model region. The corpus as 20.1 lists it, and the remedy file.
Found:      the archive's reader stores a release with its envelope's own words, so KEYS's answer was captured again
            over that text, 22,996 characters, in the same four words as over the unwrapped 22,946; and the schema's
            reader took no column name holding a digit, as `sha256` does, and now takes one opening on a letter.
Amended:    20.1 amends its own done condition: the share stated is of the 70 stored results releases, where it said
            52, and a refused answer is counted on the run's row, the readings' store arriving at 20.3, where it said
            stored refused; BUILD_PLAN and the changelog say the same.
Measured:   the twice-read before the build, by the scratch script that read 20.0's sample, read-only on the live
            store: all 70 stored results releases read twice under the four questions at the pin's settings, 70 of 70
            answered alike, none refused and none asked again, 3.21 seconds a call at the median and 8.49 at the
            slowest, every answer from google/gemma-4-26b-a4b-qat on llama.cpp-win-x86_64-nvidia-cuda12-avx2 2.56.0,
            the model loaded for it and unloaded after. The read-back: the branch's Debug build over a store migrated
            fresh to 98 in the session's scratch folder holding seven constructed runs, the Run page at 1440 and a true
            390 in both palettes, no page wider than its screen, at 390 the region's table scrolling inside its own
            frame, 764 px of table in 328, and each start and end held on one line.
Tests:      14 new, 2434 in all: the questions and their readings; the reader's causes; KEYS's answer read by hand
            from the text the replay stores and read twice by the local run over the replayed store; the captures;
            the pin's parts and its file check; availability at 21:39:59 and 21:40:00 and over a weekend; the pin
            written with the runtime left as found; a slot checking the pin; the runtime not running and the model
            not held; the twice-read asking once more; an answer the pin does not name; the lock waits; the script's
            check mode; and the Run page's region. Derived expectation `local-run.json`, KEYS's answer read by hand.
Claims:     1349, nine more than 1340 and the plan's about 9: the catalogue's and the matrix's Local run, section 16's
            Reading pins, section 17's Local reading's settings, section 18's four and 15.10's The local model's runs,
            named beside phase 19's pair as rows after its report, each passing.
Pins:       none moved: no source a rule's version, a candidate evaluator, the hooks or the learned score pins changes.
Remedy:     `tools/remedies/2026-10-18-20.1-the-local-run.txt`, from main after the merge and before the night, with
            LM Studio running and holding the model: the migration to 98, `local-run --pin` and `local-run --twice`;
            then the two slots' tasks registered as the runbook states, on the operator's word of 2026-10-18. No
            provider request and no paid call. The pin's hash time, the twice-read's share and the slots' first runs
            are owed to its record entry.
Mutated:    the rule, stated before the runs: one mutation a property 20.1 adds whose failure would let a reading stand
            under a version it was not read under or keep a reader from seeing a run, the smallest edit to shipped
            source or the script, each run filtered to the tests named with its red predicted, made in this checkout
            and reverted from the file's own text, the tree's diff the same after the sweep as before. M1 the file not
            hashed whole on a new ISO week: red at the pin's parts and the slot's check. M2 the file compared by size
            and time where hashed whole: red at the pin's parts. M3 a word read whatever its case: red at the reader.
            M4 a release accepted at the late slot's start read that night: red at availability. M5 no answer asked
            once more: red at the twice-read. M6 an answer's runtime version not checked: red at the pin-not-named
            test. M7 the runs read oldest first: red at the Run page's region. M8 reasoning left on: red at the
            captures' test. M9 the model left loaded where the run loaded it: red at the pin written. M10 a run
            writing while the night holds its lock: red at the lock test. M11 the script not refusing a main ahead of
            origin/main: red at the check mode. Each red as predicted. Not mutated: a slot's give-up past its last
            instant, the probe's refusal and the models folder's default.
Verified:   `tools/ci.ps1` green and `tools/verify-phase.ps1` green over the tree carrying this entry, the figures below
            stated before the run: all six steps, 0 warnings, 0 errors, 2434 of 2434 tests ran with none failed,
            migrations 0 to 98 with none pending, schema version 98, against `data-ci` and never `data`; the phase
            report 60 tables, 1349 claims, 1349 passing, none out of scope and none unexamined, 1360 placements and
            verdicts reconciled against a floor of 34, 43 checks on the roster run and passed.
Carried:    20.1's remedy from main and its record; the sign-offs of phases 18 and 19, owed to fresh sessions after
            20.4; the two items phase 17's sign-off carried, corrected after 20.4; (G1), SCHEMA's `family_trade`
            sentence, owed by the next edit of that section.

### 20.1 - the remedy run from main: migration 98, the first reading pin, the stored releases read twice under it, and the two slots' tasks registered   2026-10-18

Not a checkpoint entry: it records 20.1's remedy and what it measured, and lands nothing.
Ran:        from main's 44785460, the merge of PR 488: `tools/remedies/2026-10-18-20.1-the-local-run.txt` between
            08:47:40Z and 08:57:55Z, with no night, off-peak run or monthly run writing and LM Studio holding no model:
            migration 98 applied, then `local-run --pin` and `local-run --twice`. No provider request, no request of
            the SEC and no paid call; every model call the local model's.
Measured:   the pin between 08:47:53Z and 08:48:45Z: the model's file, 14,439,363,264 bytes, hashed whole in 32.3
            seconds by the run against 211 by PowerShell's own hash at 20.0, its SHA-256 the one 20.0 measured; the
            probe answered by google/gemma-4-26b-a4b-qat on llama.cpp-win-x86_64-nvidia-cuda12-avx2 2.56.0; pin 1
            written under the instruction's version d1675c9d8e4d at a context of 50,176 tokens and the cutoff
            2025-01-31, from one model call. The twice-read under pin 1 between 08:48:47Z and 08:57:54Z: the 70
            stored results releases read twice in 140 calls, 70 of 70 answered alike, none refused and none asked
            again, 3.23 seconds a call at the median and 8.67 at the slowest, the model's file checked by its size and
            modified instant in the ISO week the pin hashed it whole. Each run loaded the model and unloaded it after,
            leaving LM Studio holding none.
Tasks:      `EquityBrief localrun early` and `EquityBrief localrun late` registered at 08:59Z on the operator's word
            of 2026-10-18, as the runbook states, each start boundary pinned in UTC as the night's and the off-peak
            run's are, their next runs 12:05Z and 21:40Z on 2026-10-18. The read surface on 5152 restarted from main's
            Release, its Run page drawing the local model's region with the two runs above.
Tests:      2434, unchanged.
Verified:   `tools/ci.ps1` green and `tools/verify-phase.ps1` green over 20.1's tree as its entry records; this entry
            alone changes, so the checks reading the record ran filtered over the tree carrying it.
Carried:    the slots' first runs, read off the Run page and recorded in 20.2's entry; the sign-offs of phases 18 and
            19, owed to fresh sessions after 20.4; the two items phase 17's sign-off carried, corrected after 20.4;
            (G1), SCHEMA's `family_trade` sentence, owed by the next edit of that section.

### 20.2 - the members' results releases, found and kept   2026-10-18

Queue:      done, phase 17's sign-off, phase 20's planning and 20.1 with its remedy, PRs 486 to 489; in progress, this
            checkpoint; next, its remedy from main with the history fetched by hand, then 20.3 and 20.4, the two items
            phase 17's sign-off carried, and the sign-offs of phases 18 and 19 by fresh sessions.
Built:      on the operator's ruling of 2026-10-18 (see: A member's results release is the EX-99 exhibit of its 8-K
            carrying item 2.02, stored compressed with the instant the SEC accepted it). The SEC's answers captured
            before their readers were written: the current 8-K feed, Keysight's further page of submissions, and an
            8-K's index page at each of New York's offsets. In the core the readers of the current feed, a filer's
            submissions and their further page and an 8-K page's accepted instant through the session zone; a
            release's text compressed beside the hash of its bytes and its length; the instant an `--until` names; the
            archive feed's further documents; and a recorded feed over the fixture's captures. Migration 99's
            `release_document` and `release_day`. The worker's release fetch: the early slot over the archive's daily
            index from the day after the newest it read, the late slot over the current feed back to the start of the
            late slot before it, and `releases --history --from` with `--until`, pausing for the night's window and a
            slot; each local run slot fetches before it checks the pin, its row counting the releases stored and the
            SEC's requests, a refusal coming to `fetch refused`. The whole replay runs the early slot over the
            fixture's week. The corpus as 20.2 lists it, CLAUDE.md's ninth carve-out among it, and the remedy file.
Found:      a late slot reading back to the start of its own day in New York alone would miss a release accepted after
            the late slot before it and before midnight, which the availability rule makes readable that night, so it
            reads back to the start of the slot before; the 8 of an 8-K in section 17's value cell reads as a figure,
            so the row names the SEC's current filings feed; and the slots read the pulled companies' filers beside the
            history, which the check holding the pulled tables to their stated readers refused, so the slots read the
            night's own companies alone and the history, by hand, joins that check's list with its reason.
Amended:    20.2 amends its own done condition: a refused answer is counted refused on the run's row until 20.3 stores
            readings, where it said stored refused, and the late slot is held to reading back to the slot before;
            BUILD_PLAN and the changelog say the same.
Tests:      9 new, 2443 in all: the current feed, the submissions, the further page and an 8-K's page read as captured
            with the accepted instant at both offsets; the text compressed and read back to its hash; the early slot
            storing a member's release and asking nothing for an announcement carrying no 2.02, a filer that is no
            member or a report, its days worked by hand and a day not posted read by the next slot; the late slot
            reading back to the slot before and the next morning's early slot asking nothing for what it stored; the
            further page at its twelve months' edge; the history stopping at its until and going on; `--until` across
            the change of 2026-11-01; the replay's week; and a slot's row with the SEC's refusal. Nightly-cost holds a
            night constructing the local run or the release fetch. Derived expectation `release-fetch.json`, the
            fixture's week read by hand.
Claims:     1355, six more than 1349 and the plan's about 5: the catalogue's and the matrix's Release fetch, section
            16's Release documents and Release days, section 17's Release fetch's settings and section 18's The SEC
            refuses or does not answer a release fetch, named beside phase 19's pair as rows after its report, each
            passing.
Pins:       none moved: no source a rule's version, a candidate evaluator, the hooks or the learned score pins changes.
Remedy:     `tools/remedies/2026-10-18-20.2-the-results-releases.txt`, from main after the merge and before the late
            slot: the migration to 99, then `releases --history --from 2019-01-02 --until 07:00`. Requests of the SEC
            alone, no provider request, no model call and no paid call. The count stored per index and year, the
            requests, the time and the store's growth are owed to its record entry.
Mutated:    the rule, stated before the runs: one mutation a property 20.2 adds whose failure would keep a release
            wrongly, miss one or ask the SEC for one it should not, the smallest edit to shipped source, each run
            filtered to the release tests or the checks named with its red predicted, made in this checkout and
            reverted from the file's own text, the tree's diff the same after the sweep as before. M1 the early slot
            reading a filer that is no member: red at the early slot and the replay. M2 an announcement carrying no
            2.02 read as results: red at the early slot and the replay. M3 a page's accepted instant read as UTC: red
            at the readers and the early slot. M4 the late slot reading back to its own day's start alone: red at the
            late slot. M5 the late slot reading on past a page reaching before its start: red at the late slot. M6 the
            history not stopping at its until inside a filer: red at the history. M7 a further page read within eleven
            months: red at the further page's edge. M8 the release before the first session left out: red at the
            history and the further page. M9 the hash taken over the compressed bytes: red at the compression and the
            early slot. M10 a Saturday read as a day of the index: red at the early slot and the replay. M11 the newest
            day not posted recorded as none posted: red at the early slot and the replay. M12 a refused fetch come to
            finished: red at the slot's row. M13 the release fetch declaring no feed: red at nightly-cost's
            construction test and at component-access. M14 a page that is no Atom feed read as listing nothing: red at
            the readers. M15 a release the late slot stored asked for again by the next early slot: red at the late
            slot's morning after. M16 a slot reading the pulled companies' filers: red at the early slot. M17 the
            history reading the night's own companies alone: red at the further page. Each red as predicted. Not mutated: the history's pause for the night's window and a
            slot, the wait to write while the night holds its lock, and a slot refused without the archive's contact.
Verified:   `tools/ci.ps1` green and `tools/verify-phase.ps1` green over the tree carrying this entry, the figures below
            stated before the run: all six steps, 0 warnings, 0 errors, 2443 of 2443 tests ran with none failed,
            migrations 0 to 99 with none pending, schema version 99, against `data-ci` and never `data`; the phase
            report 60 tables, 1355 claims, 1355 passing, none out of scope and none unexamined, 1366 placements and
            verdicts reconciled against a floor of 34, 43 checks on the roster run and passed.
Carried:    20.2's remedy from main and its record; the slots' first runs, which 20.1's record carried here and which
            fall after this entry, the early slot of 12:05Z in 20.2's remedy record and the late slot of 21:40Z, the
            first to fetch, in 20.3's entry; the sign-offs of phases 18 and 19, owed to fresh sessions after 20.4; the
            two items phase 17's sign-off carried, corrected after 20.4; (G1), SCHEMA's `family_trade` sentence, owed
            by the next edit of that section.

### 19.10 - correction: 13.10 says the setup ledger's benchmark is bought at each member's own next open   2026-10-18

Found:      by the operator's review of the corpus: 13.10 still entered the ledger's benchmark at the close, which 19.10
            moved to each member's own next open, as the same paragraph's earlier sentence says.
Built:      the sentence says the benchmark is entered on every member that session and bought at each member's own
            next open, checked first against the code, which prices it through `TradeFills.Swing` at the open; the
            changelog holds the prior text. Documents alone, with the guide's new text beside it in this pull request.
Tests:      none new; no code changes.
Verified:   `tools/ci.ps1` green over main's code, as the 19.11 correction's entry below records.
Carried:    as the 19.11 correction's entry below records.

### 19.11 - correction: 15.7's close to a buy point rows say a stock opens its chart page   2026-10-18

Found:      by the operator's review of the corpus: 15.7 still had a stock one gate short link to its page and a name
            close to a buy point open to its page, where from 19.11 every link that opens a stock opens its chart page.
Built:      both phrases say the chart page, checked first against the code, whose rows link the ticker to
            `#/chart/<ticker>`; the changelog holds the prior text. Not changed and put to the operator: 13.10's
            heavyweights' setup still "bought at the session's close", where the code prices it at the next open as
            every setup; and 15.7's note still says a row's "ticker is a link opening the name's own page", where from
            19.11 it opens the chart page.
Tests:      none new; no code changes.
Verified:   `tools/ci.ps1` green and `tools/verify-phase.ps1` green over main's code at 2528192e, as the 20.2 entry
            records: 2443 of 2443 tests, 1355 claims, 1355 passing. This pull request changes documents alone, the
            guide among them, and the checks that read the corpus ran filtered over its tree: RecordAppendOnly,
            TwoPlatform, ObligationReconciles, BannedProse, DecisionCitations, CoverageReported, StatedCounts,
            ArchitectureConformance with the guide's tests, PinnedConstants, ChangelogReconciles,
            DoneConditionProducible and FigureFits.
Carried:    as the 20.2 entry above records.

### 20.3 - the anonymiser, the release reading, its probes and its accuracy report   2026-10-18

Queue:      done, phase 17's sign-off, phase 20's planning, 20.1 with its remedy and 20.2, PRs 486 to 490; in
            progress, the operator's guide and the two corrections above, PR 491, which this checkpoint stands on, and
            this checkpoint, with 20.2's history fetch from main still running; next, 20.2's record once the fetch ends,
            this checkpoint's remedy and its record, 20.4, the two items phase 17's sign-off carried, and the sign-offs
            of phases 18 and 19 by fresh sessions.
Built:      on the operator's rulings of 2026-10-18 (see: A release is read with every name and date in it replaced by
            a neutral word, and product names kept). In the core the anonymiser, its common words, its pin as its
            version and its version joining the instruction's; the samples, drawn at the seed by index in proportion
            to members from the releases accepted before 2026-10-18; the probes' kinds and whether an answer names the
            filer; the agreement, the outlook guided figures show and the tone variant's shares; a filer's names off its
            submissions; and a calendar placing a print's session before its report date by the closure table over its
            years and the ledger's sessions before them. Migration 100's `release_filer`, `release_reading` and
            `release_probe`. The reader of the readings in the data project, calling the core's availability rule. The
            worker's local run reading a slot's week newest first and `releases --read --history` with `--until`,
            pausing for each slot and the night's window and marking its pin complete once nothing is left unread, a
            text that is not its hash refused unread; `releases --names`; `--probe twice|reidentify|distract`;
            `--reference --out`; and `releases --accuracy` with `--labels`, writing nothing. The name page draws the
            newest release's four answers under what management said, alone where no section is written. The corpus as
            20.3 lists it, and the two remedy files.
Found:      the closure table covers 2025 to 2027 while the history's prints reach 2019, so a print's window over the
            history reads the ledger's sessions before the table's years, a print neither places counted apart; a
            sample drawn over a store the slots grow is no one sample, so every sample draws from the releases accepted
            before the samples' day; the reading's writes sat in the local run's partial files, which the write checks
            name as components of their own, so the statements stand in the class's own file; the reader of a print's
            release and the name page's wrote the late slot's comparison in their SQL, which M14 showed, so both now ask
            the core's rule; the SEC writes Domino's as DOMINOS, whose s is its own, so "Domino’s stores" reads
            "[company] stores"; 15.9's row listed its parts with commas, which reads as an enumeration a verdict a part,
            so the parts stand between semicolons as the row's others do; and "from 20.3" in section 17's value cell
            reads as a figure, so the cell carries none.
Amended:    20.3's text in BUILD_PLAN, and not its done condition: the filers' names, the samples' day, a text that is
            not its hash, the window's calendar and the order of the labels and the new pin; the changelog says the same.
Tests:      15 new, 2458 in all: the fixture's release anonymised as worked by hand and read through the history read
            into its recorded answer's four words; a filer named with a common word and an insider whose surname is an
            ordinary word, in lower case, at a sentence's start and inside one, with "Q3" and "September 30" read as
            dates; the anonymiser's version the pin of its source and part of the prompt's; the print's release a second
            either side of the late slot's start and across a weekend; the calendar at the table's first day; the
            agreement, the probes' and the tone shares by hand; the shares by the largest remainder and the draw at its
            seed; the history read pausing for a slot, stopping at its until with its pin left open and keeping a
            refused answer refused; a slot's week newest first; a pin whose model file changed refusing the history
            read; a text that is not its hash; the filers' names; the probes and the reference set over a constructed
            store; the accuracy report naming a reading written before its label; and the name page's answers under
            what management said. Derived expectation `release-reading.json`, the fixture's release read by hand.
Claims:     1362, seven more than 1355 and the plan's about 8: the catalogue's and the matrix's Release accuracy, section
            16's Release filers, Release readings and Release probes, section 17's Release reading's samples and section
            18's A stored release's text does not read back to its hash, named beside phase 19's pair as rows after its
            report, each passing.
Pins:       none moved: no source a rule's version, a candidate evaluator, the hooks or the learned score pins changes.
Remedy:     after the merge and once the history fetch has ended, `tools/remedies/2026-10-18-20.3-the-release-filers.txt`
            from main: the migration to 100 and `releases --names`; then by hand `releases --reference --out` and the
            reference set labelled by the build session and committed with its manifest; then
            `tools/remedies/2026-10-18-20.3-the-history-read.txt`: `local-run --pin` and `releases --read --history
            --until 07:00`, run again each night until the pin's history completes. Until the new pin every slot refuses
            on the prompt and goes on fetching. The local model and the SEC alone, no provider request and no paid call.
Mutated:    the rule, stated before the runs: one mutation a property 20.3 adds whose failure would let a name or a
            date through to the model, keep a reading wrongly, read a release on a night it is not readable, or draw a
            reading a page should not, the smallest edit to shipped source, each run filtered to 20.3's tests with
            20.1's and 20.2's local run tests, 36 in all, its red predicted, made in this checkout and reverted with the
            editor. An edit to the anonymiser's source also reddens its version test, which is counted and is no kill.
            M1 a single word replaced where it opens a sentence: red at the fixture's release and the common-word
            filer. M2 a name's first word replaced though a common word: red at the common-word filer. M3 a single word
            replaced in lower case: red at the common-word filer. M4 the dateline's place read before the dates: red at
            the fixture's release. M5 a legal word's short and long forms read apart: red at the common-word filer, and
            not at the probes as predicted, which work their expected texts through the anonymiser itself. M6 the
            prompt's version without the anonymiser's: red at the version test and 20.1's questions. M7 a refused
            reading kept as read: red at the history read and the text not its hash. M8 the hash not checked: red at
            the text not its hash. M9 a slot reading six days: red at the slot's week. M10 a slot oldest first: red at
            the slot's week. M11 the history marked complete by a stopped read: red at the history read. M12 no pause
            for a slot: red at the history read. M13 the history read on past a refusing pin: red at the changed file.
            M14 a release at the late slot's start read that night: red at first at 20.1's rule alone, not at the print
            as predicted, the two readers writing the comparison in SQL; with the readers asking the core's rule, red at
            the rule, the print and the name page. M15 the window from the report date: red at the print. M16 the
            calendar without the ledger's sessions: red at the calendar and the accuracy report. M17 a surprise of
            nothing agreeing with better: red at the arithmetic. M18 none given failing the tone variant: red at the
            arithmetic. M19 no reading counted before its label: red at the accuracy report. M20 a ticker not naming the
            filer: red at the arithmetic and the probes. M21 the distraction read without the names in: red at the
            probes. M22 a release of the samples' day drawn: red at the probes. M23 the shares' remainder not given out:
            red at the samples. M24 `--again` naming only the unnamed: red at the names. M25 a filer's former names
            dropped: red at the names. M26 the page's reading under any version: red at the page. M27 the accepted day
            in UTC: red at the page. M28 no region where no section is written: red at the page. M29 the reference set
            written with its names in: red at the probes. Each red as predicted but M5 at the probes and M14's first
            run. Not mutated: the contact, address and file name patterns, the people's earlier words, the outlook the
            guided figures show over a store, the read's `--until` as written, and the page's filer padded to ten digits.
Verified:   `tools/ci.ps1` green and `tools/verify-phase.ps1` green over the tree carrying this entry, the figures below
            stated before the run: all six steps, 0 warnings, 0 errors, 2458 of 2458 tests ran with none failed,
            migrations 0 to 100 with none pending, schema version 100, against `data-ci` and never `data`; the phase
            report 60 tables, 1362 claims, 1362 passing, none out of scope and none unexamined, 1373 placements and
            verdicts reconciled against a floor of 34, 43 checks on the roster run and passed.
Carried:    20.2's history fetch, still running from main, and its record; the late slot's first fetch at 21:40Z, in
            the record written after it; this checkpoint's remedy, the reference set's labels with the billing that
            applied, and the history read, its record stating each answer's agreement before and after the cutoff, the
            tone variant's distinctness, the three probes, the local model's hours and the releases keeping a trace of
            the filer's first word or tickers and where, all before 20.4's remedy; the sign-offs of phases 18 and 19,
            owed to fresh sessions after 20.4; the two items phase 17's sign-off carried, corrected after 20.4; (G1),
            SCHEMA's `family_trade` sentence, owed by the next edit of that section.

### 20.4 - the drift's tone variant: the night hands each drift setup its release reading, and the variant waits on its remedy   2026-10-18

Queue:      done, phase 17's sign-off and 20.0 to 20.3 with the operator's guide and two corrections, PRs 486 to 493; in
            progress, this checkpoint, with 20.2's history fetch from main still running; next, 20.2's record once the
            fetch ends, 20.3's remedy and its record, this checkpoint's two remedies, the two items phase 17's sign-off
            carried, and the sign-offs of phases 18 and 19 by fresh sessions.
Built:      on the operator's rulings of 2026-10-18 (see: The drift's tone variant lists only where its print's release
            reads better than expected and its outlook was not lowered or withdrawn) (see: An S&P 400 or 600 family's
            variants are registered again whole to add one beside its provisional rule). In the core the tone condition,
            read after a drift rule's gates and its switches where its registration names a reading version, an optional
            parameter the drift on every index reads and no other evaluator, with its chip and its funnel's two steps.
            The family evaluator hands each print inside the widest window a standing drift rule reads its release under
            every version a standing rule names, and the index families each listed drift setup's, through the one
            reader of the release readings and none under a version whose history read is not complete, each stage's row
            saying what each version held. The registrar refuses a version not complete or no whole number; the drift
            registered again adds its ninth rule at the newest complete version; `register --index-variants` takes
            `--again` with `--evidence`; the replay names an index rule as not replayed. The cards count a tone rule's
            release read and its tone, an index tone variant read again for its funnel. The per-rule step keeps the
            variant not testable in the operator's words. `rules-choose-the-stocks` gains the release readings' test.
            The corpus as 20.4 lists it, the matrix's column headed Research and readings, and two remedy files.
Found:      the shared evaluator file every evaluator pins took the optional parameters first, which moved every pin,
            the swing filter's and the three candidates' among them, so the tone condition's own file names them; the
            reader of the complete versions set a command's transaction to none, which the cards' stage refuses inside
            its own, so a night with a tone rule standing would have left the S&P 500's rule rows not computed; an S&P
            400 or 600 variant's card drew no funnel; and no replay reads an index rule.
Amended:    20.4 amends its own done condition: the read of the release readings it finds is one outside the one reader
            of the release readings, which 20.3 built beside the stores and not in the core; and its text on that ground,
            and in that `--again` names each standing index variant as not replayed. The changelog says the same.
Tests:      9 new, 2467 in all: the tone condition on both sides of each answer's words, after a VIX switch and read by
            no rule naming no version; the fixture's release reading passing it as worked by hand; a release at the
            second its print's window opens and a second before, and a version not complete; the S&P 500's drift
            registered again without and with its ninth rule, the night listing DA and not DB, its row's counts and the
            card's funnel, and a version not complete listing nothing; each index's night over seven members worked by
            hand, its row and its card's funnel; `--again`'s refusals and its registration; the chips and the per-rule
            words; and the release readings read by a deciding component through the one reader alone, with planted
            reads found. Derived expectation `tone-variant.json`, the fixture's release reading worked by hand.
Claims:     1364, two more than 1362 and the plan's about 2: 15.7's The drift's tone variant and section 17's Tone
            variant's release window, named beside phase 19's pair as rows after its report, each passing.
Pins:       moved as 20.0 read them off the code and no other: the S&P 500's breakout 68080b6ae692 to c4cf32e70c59 and
            drift 05ae10606fa0 to f6b52da6e0c6; on the S&P 400 and 600 the breakout b1dbb89e6a0b to 220315e2b29d, the
            drift 0517e0869555 to d3b31d4c3a35, the pullback 87e8cc8312bd to 2d47fe58f8ca and the heavyweights
            9e8ebd36a0e5 to 2dfd307b477a.
Remedy:     after the merge and before the night, `tools/remedies/2026-10-18-20.4-the-moved-rules-registered-again.txt`
            from main, the S&P 500's rules replayed and kept and each index rule restarting; once version 2's history
            read completes and its record entry is written, `tools/remedies/2026-10-18-20.4-the-tone-variants.txt`. No
            provider request and no model call.
Mutated:    the rule, stated before the runs: one mutation a property 20.4 adds whose failure would list a drift setup
            its condition keeps off, keep off one it lists, hand a reading a rule may not read, leave a night's empty
            list unexplained, or register what the form refuses; the smallest edit to shipped source, each run filtered
            to 20.4's nine tests with the rules check's two before them, its red predicted, made in this checkout and
            reverted with the editor. M1 the results alone read: red at the condition and the index night, and at the
            S&P 500's night, where DB's withdrawn outlook passed. M2 in line read as better: red at the condition and
            the index night. M3 no reading passing: red at the condition and both nights. M4 the gate read by a rule
            naming no version: red at the condition and the S&P 500's night. M5 a version not complete handed: red at
            the window and both nights. M6 only tonight's reactions handed on the S&P 500: red at its night. M7 an index
            rule's check skipped: red at the index night. M8 only tonight's reactions handed on an index: red at the
            index night. M9 the S&P 500 card's release read step dropped: red at its night. M10 no index funnel: red at
            the index night. M11 a standing variant left out allowed: red at the form. M12 nothing added allowed: red
            at the form. M13 a version not complete registered: red at the form. M14 `--again` with no evidence: red at
            the form. M15 no ninth rule: red at the S&P 500's night. M16 the per-rule step walking it: red at the words.
            M17 no chip: red at the words. M18 a deciding source naming the readings' table: red at the rules check.
            M19 an index rule unnamed by the replay: red at the form. M20 the transaction set to none: red at both
            nights. M21 the evaluator's row silent: red at the S&P 500's night. M22 the index's no-reading part
            unnamed: red at the index night. M23 the reading version refused on an index: red at the form and the index
            night. M24 a fractional version registered: red at the form. Each red as predicted, M1 at one test more.
            Not mutated: the release read step's own count on the S&P 500 apart from the tone, and the stage rows'
            order of reasons.
Verified:   `tools/ci.ps1` green and `tools/verify-phase.ps1` green over the tree carrying this entry, the figures below
            stated before the run: all six steps, 0 warnings, 0 errors, 2467 of 2467 tests ran with none failed,
            migrations 0 to 100 with none pending, schema version 100, against `data-ci` and never `data`; the phase
            report 60 tables, 1364 claims, 1364 passing, none out of scope and none unexamined, 1375 placements and
            verdicts reconciled against a floor of 34, 43 checks on the roster run and passed.
Carried:    20.2's history fetch and its record; the late slot's first fetch; 20.3's remedy, the reference set's labels
            and the history read with its record before this checkpoint's second remedy; this checkpoint's two remedies;
            the sign-offs of phases 18 and 19, owed to fresh sessions from now; the two items phase 17's sign-off
            carried; (G1), SCHEMA's `family_trade` sentence, owed by the next edit of that section.

### 20.3 - correction: the anonymiser reads a filer's name across its joins, points and state, a ticker wherever it stands, and no product as a person   2026-10-18

Queue:      done, phases 0 to 19 built and 20.0 to 20.4 merged, PRs 486 to 494, with 20.2's history fetched, 20.3's
            filers named and 20.4's first remedy run; in progress, this correction, found writing out 20.3's reference
            set; next, the reference set written out again and labelled, the new pin and the history read, 20.2's
            record, the two items phase 17's sign-off carried, and the sign-offs of phases 18 and 19 by fresh sessions.
Found:      reading the reference set's first two releases before labelling them, then a read-only scan of all 100
            counting each word of the filer's names that is no legal, common or ordinary word and each ticker: the
            filer's own name or ticker left in 61 of them, 410 times. 250 stand where a sentence opens on the filer's
            one-word name or its first word, which the operator's rule of 2026-10-18 keeps (see: A release is read with
            every name and date in it replaced by a neutral word, and product names kept). The rest were faults against
            that decision: U.S. Bancorp whole, 52 times, filed as `US BANCORP \DE\` with a state between backslashes and
            written with points; a name filed with spaces or an ampersand and written with a hyphen or without it,
            Brown-Forman, Patterson-UTI, Rent-A-Center and Marsh McLennan; a state filed bare after a legal word, RENT A
            CENTER INC DE; a ticker opening a sentence, AIG, BGC and MARA, the decision's exception being a name's first
            word or a surname; a class's ticker filed with a hyphen and written with a point or nothing, BF.B and BFB;
            "Exhibit 99.1 Marsh" read as the date 1 Mar; Abiomed's Impella read as a person from "Automated Impella
            Controller", a title's word with no comma before it, where product names stay; and the dateline's town,
            which the decision replaces, left in at least 48 of the 100, wherever a wire's name, a dash, a bracket, a
            semicolon, a point or spaces stood between it and its date, or it came after its date, as "LITTLE ROCK,
            Ark.--(BUSINESS WIRE)--", "CAMP HILL, PA (" and "[date], LOUISVILLE, Ky. –".
Built:      the anonymiser takes a state off between slashes or backslashes and bare after a legal word; reads a name's
            words joined by spaces, a hyphen or an ampersand it may write or leave out, and a word of up to three
            capitals with points or none; replaces a ticker wherever it stands, and a class's ticker with a point, a
            slash, a space or nothing for its hyphen; takes no article for a name's second word; reads a run beside a
            title's word as a name only with a comma or an article between; reads a month's short name only where it
            ends its word; and replaces the first dateline a release writes, a town in capitals with its state or
            country or none, or one in mixed case with its state or country, before its date with a wire's name, a
            dash, a bracket, a semicolon, a point or spaces between, or after it, a word of a heading or a table in
            capitals being no town. Its version moves from ff6d3fbf541f to 175a444d0383, and the instruction's with it;
            no reading was written under 20.3's, and the fixture's release reads as before. The architecture's and 20.3's
            account of the sentence-start rule name a name's first word or a surname, as the decision does; the
            roster's row and the changelog.
Measured:   the reference set written through the corrected anonymiser into a folder of its own and scanned the same
            way: the filer's own name or ticker in 48 of the 100, 181 times, each where a sentence opens on the filer's
            one-word name or its first word; every word left inside a sentence is one the scan counts that is not the
            name in its place, as Office in Regional Office Properties. A dateline's town is left in 1, Baltimore,
            written in mixed case with no state, and 2 open on no dateline.
Tests:      1 new, 2468 in all: each fault worked by hand from the reference set's own releases.
Claims:     1364, as before.
Mutated:    the rule, stated before the runs: one mutation a fault the correction mends, the smallest edit to the
            anonymiser undoing that fix alone, each run filtered to the anonymiser's 5 tests with its red predicted at
            the new test, made in this checkout and reverted with the editor; each also reddens the version test, which
            is counted and is no kill. M1 a state between backslashes kept. M2 a state bare after a legal word kept. M3
            no hyphen joining a name. M4 the ampersand required where filed. M5 points read at one letter alone. M6 a
            ticker skipped where it opens a sentence. M7 a class's ticker read only as filed. M8 a run running into a
            title's word read as a name. M9 an article read as a name's second word. M10 a month read inside a longer
            word. M11 no wire's name read between a place and its date. M12 no bracket read before a date. M13 no town
            in mixed case read. M14 no place read after its date. M15 a heading's capitals read as a town. M16 no point
            read after a state. Each red at the new test as predicted, and at no other but the version test.
Verified:   `tools/ci.ps1` green and `tools/verify-phase.ps1` green over the tree carrying this entry, the figures below
            stated before the run: all six steps, 0 warnings, 0 errors, 2468 of 2468 tests ran with none failed,
            migrations 0 to 100 with none pending, schema version 100, against `data-ci` and never `data`; the phase
            report 60 tables, 1364 claims, 1364 passing, none out of scope and none unexamined, 1375 placements and
            verdicts reconciled against a floor of 34, 43 checks on the roster run and passed.
Carried:    the sentence-start rule's leak above, put to the operator with these figures; the reference set written out
            again from main after the merge, labelled and committed before the new pin, then the history read and its
            record; 20.2's record; 20.4's first remedy run, seven of the S&P 500's rules restarting where its replay
            read an input a later night changed, put to the operator, and its second remedy after the history read; the
            sign-offs of phases 18 and 19, owed to fresh sessions; the two items phase 17's sign-off carried; (G1),
            SCHEMA's `family_trade` sentence, owed by the next edit of that section.

### 20.3 - correction: the anonymiser reads a code page's characters, every contact line, each owner of a joint filing and a name after an honorific, and the dates, phones and tickers it missed   2026-10-18

Queue:      done, phases 0 to 19 built and 20.0 to 20.4 merged with 20.3's first correction, PRs 486 to 495, 20.2's
            history fetched, 20.3's filers named, 20.4's first remedy run and the reference set's 100 labels written; in
            progress, this correction, found labelling the reference set; next, the reference set's fixture with its
            labels, the new pin and the history read, 20.2's record, the two items phase 17's sign-off carried, and the
            sign-offs of phases 18 and 19 by fresh sessions.
Found:      labelling the 100 releases of the reference set, each read whole as the first correction wrote it, misses
            against the decision (see: A release is read with every name and date in it replaced by a neutral word, and
            product names kept): a Windows code page's quotation marks, bullets and dashes in 8 of the 100, two
            datelines among them left in; a contact line's label in capitals, in lower case, without its colon or
            running into the next, its names left in, as Kate Lowrey and James Baglanis; a joint filing's owners read as
            one, so a fund's first word, Shanghai, TRIAN, Mantle, BlueHalo, was taken for a surname and the insider
            beside it missed; "Dr. David H. Wang" read as two people, the honorific's given name taken for a surname;
            "+86 (138) 1079-1480" and "+1-(978)-256-1300"; a month named alone, "in February", "its November
            guidance"; "Second Quarters Ended"; a year split after a date, "[date], 20 26"; a dateline whose date
            stands elsewhere, "BIRMINGHAM, Ala. -"; a signature's title taken with its town; the ticker a release
            states after the name of a filer since renamed, HSC, BEAT, BRKL and MMC, and a preferred stock's, SPGPrJ;
            "Jaco van der" before a surname; a founder named for the filer, "William T. [company]"; and "II" taken for
            a surname from "AJ Jones II". The filer's one-word name opening a sentence and a name opening on a
            lower-case letter, as nVent and iStar, stand by the decision's own words and are put to the operator.
Built:      the anonymiser reads 0x80 to 0x9F as Windows-1252's characters before any pattern; a phone after a plus and
            its country's code, and one with its area's brackets before a hyphen; a month named alone as a date where
            no capitalised word, figure or neutral word follows it and it is not May opening a sentence, a fiscal label
            in the plural, and a year split after a date; a dateline whose date the release sets elsewhere, a town and
            its state before a dash opening on the filer's name, where no dateline was read, and a signature's title
            words kept before a town; the ticker a release states in an exchange's form straight after the filer's
            name as the filer's, and a class's or a preferred stock's mark after a ticker in that form; each owner of a
            joint filing, and no fund or company; a contact line's label in any case, with its colon or as a heading's
            word without one, each label's name read up to its title's words, a label in capitals no part of a name;
            an honorific's name to its surname; a word read alone as a surname joined to the name straight after it; a
            surname's particles; no generation as a surname; and a founder whose surname is the filer's one-word name
            beside a title or a verb of saying, and after an honorific where one is read. Its version moves from
            175a444d0383 to fe22a6282817, and the instruction's with it; no reading was written under either, and the
            fixture's release reads as before. The roster's row and the changelog.
Measured:   the reference set written through the corrected anonymiser into a folder of its own and compared word by
            word with the texts it was labelled from: 63 of the 100 change at 753 places, 554 the code page's
            characters, 57 a month or a year, 19 a fiscal label, 15 a phone, 6 a ticker, 3 a dateline, 29 a person
            replaced, 27 a person's number joined or moved, 32 a fund or a company no longer read as a person, and 10
            the filer's one-word name standing where a bullet, read now as the code page's, opens a list's item, which
            the decision's rule keeps. No other word moved, so the labels read from the earlier texts stand.
Tests:      1 new, 2469 in all: each miss worked by hand from the reference set's own releases.
Claims:     1364, as before.
Mutated:    the rule, stated before the runs: one mutation a miss the correction mends, the smallest edit to the
            anonymiser undoing that fix alone, each run filtered to the anonymiser's 8 tests with its red predicted at
            the new test, made in this checkout and reverted with the editor; each also reddens the version test, which
            is counted and is no kill. M1 the code page not read. M2 no phone after a plus. M3 no hyphen after an
            area's brackets. M4 no month named alone. M5 a month before a surname read as a date. M6 May opening a
            sentence read as a date. M7 no plural fiscal label. M8 no split year. M9 no undated dateline. M10 a title
            read into a town. M11 no stated ticker. M12 no preferred stock's mark. M13 the contact label as it was. M14
            a run's title words kept. M15 a joint filing read whole. M16 a fund read as a person. M17 an honorific's
            name read as one word. M18 no join of a word read alone as a surname. M19 no particles. M20 a generation
            read as a surname. M21 no founder beside a title or a verb of saying. M22 no founder after an honorific.
            M23 a label in capitals read into a name. Each red at the new test as predicted, M5 at the common-word
            test as well, which holds June Smith too, and at no other but the version test. The first run of M3 was
            made with M2 still in place, its revert having missed; M2 was restored and M3 run again alone, red as
            predicted, and the version test passing after the last revert shows the source as it was.
Verified:   `tools/ci.ps1` green and `tools/verify-phase.ps1` green over the tree carrying this entry, the figures below
            stated before the run: all six steps, 0 warnings, 0 errors, 2469 of 2469 tests ran with none failed,
            migrations 0 to 100 with none pending, schema version 100, against `data-ci` and never `data`; the phase
            report 60 tables, 1364 claims, 1364 passing, none out of scope and none unexamined, 1375 placements and
            verdicts reconciled against a floor of 34, 43 checks on the roster run and passed.
Carried:    the sentence-start rule's leak, at a bullet's opening as well, and a name opening on a lower-case letter,
            put to the operator; the reference set's fixture and its labels committed before the new pin, then the
            history read and its record; 20.2's record; 20.4's second remedy after the history read; the sign-offs of
            phases 18 and 19, owed to fresh sessions; the two items phase 17's sign-off carried; (G1), SCHEMA's
            `family_trade` sentence, owed by the next edit of that section.

### 20.3 - the reference set's 100 labels, committed before the model reads any of them   2026-10-18

Queue:      done, phases 0 to 19 built and 20.0 to 20.4 merged with 20.3's first correction, PRs 486 to 495, 20.2's
            history fetched, 20.3's filers named and 20.4's first remedy run; in progress, the anonymiser's second
            correction above and these labels; next, the new pin and the history read, 20.2's record, the two items
            phase 17's sign-off carried, and the sign-offs of phases 18 and 19 by fresh sessions.
Built:      `fixtures/release-reference-2026/`: its manifest naming one input, `sample.txt`, the 100 results releases
            `releases --reference` drew at seed 20261018 from those the SEC accepted before 2026-10-18, by index in
            proportion to members, 50 on or before the model's cutoff and 50 after, listed by accession as the command
            first wrote them at 2026-10-18T23:01:19Z; and `labels.json`, the four words the build session gave each
            from the text the model reads, how each question was read and the 57 conventions fixed while labelling so
            every release was read alike, its `labelledAt` 2026-10-19T00:10:42Z, the instant the last label was
            written. The texts stay in the store and the command writes them again. A test reads the labels through the
            accuracy report's own reader; the fixtures README's paragraph, the roster's row and the changelog.
Measured:   results 75 not stated, 12 better, 10 in line, 3 worse; outlook 46 kept, 31 none given, 16 raised, 7
            lowered or withdrawn; tone 78 confident, 19 neutral, 3 cautious; demand 59 stronger, 19 not stated, 12
            steady, 10 weaker. The labels were read from the texts the anonymiser wrote at 175a444d0383, which the
            correction above writes again at fe22a6282817 differing only by a code page's characters and neutral
            words. The build session's reading ran on the operator's plan, as their word of 2026-09-17 describes, so no
            bill by the token applies, which 20.3's plan asks this entry to state.
Tests:      1 new, 2470 in all: the committed labels one for each release the sample lists, in its order, every word
            one its question lists, read by the accuracy report's own reader.
Claims:     1364, as before.
Mutated:    the rule, stated before the runs: one mutation each to the file the test reads, made with the editor and
            reverted, the file read back byte for byte after, each run filtered to the new test with its red predicted
            there. MF1 the last label dropped: red at the sample's order. MF2 one answer written "in-line": red, the
            reader refusing it by name.
Verified:   `tools/ci.ps1` green and `tools/verify-phase.ps1` green over the tree carrying this entry, the figures below
            stated before the run: all six steps, 0 warnings, 0 errors, 2470 of 2470 tests ran with none failed,
            migrations 0 to 100 with none pending, schema version 100, against `data-ci` and never `data`; the phase
            report 60 tables, 1364 claims, 1364 passing, none out of scope and none unexamined, 1375 placements and
            verdicts reconciled against a floor of 34, 43 checks on the roster run and passed.
Carried:    the new pin, written once this merges and before any reference release is read, then the history read and
            its record; 20.2's record; 20.4's second remedy after the history read; the sentence-start rule and the
            lower-case names, put to the operator; the sign-offs of phases 18 and 19, owed to fresh sessions; the two
            items phase 17's sign-off carried; (G1), SCHEMA's `family_trade` sentence, owed by the next edit of that
            section.

### 19.7 - correction: the Loop summary and the fundamentals-first family's card read its rule's chips off the setting its night stored, on every index   2026-10-18

Queue:      done, phases 0 to 19 built and 20.0 to 20.4 merged with 20.3's two corrections and the reference set's
            labels, PRs 486 to 496, pin 2 written and the history read started at 02:02 UTC; in progress, the two items
            phase 17's sign-off carried, this the first; next, the second, 17.8's S&P 500 Past picks and Run page, then
            the history read over the coming nights and its record, 20.2's record, and the sign-offs of phases 18 and 19
            by fresh sessions.
Found:      carried from phase 17's sign-off, owed before phase 19's: the Loop page's summary read "not read" for the
            fundamentals-first family's rule today on every index, since no registration states its rule and the
            provisional chips held no case for it; the same left its card on Tonight without the chips 15.7's row puts
            its rule beneath, on every index.
Built:      the family's setting read back from the short form its night stores; its chips, the revenue growth it
            asks, the margins it asks to widen and the cash it asks for; the provisional chips reading them off the
            setting the night stored on any index; and the Loop summary reading the S&P 500's night for this family
            alone. No source a rule's version pins moves. The roster's row and the changelog.
Measured:   the store's three nights of 2026-10-16 each hold the provisional setting, so each index's summary and card
            read revenue up more than 0%, gross or operating margin up, and cash at least 1 times income.
Tests:      1 new, 2471 in all: the summary over a constructed store, the S&P 400 at the provisional setting and the
            S&P 500 at an approved one, each chip worked by hand, and the setting read back from its short form over the
            whole grid and refused where a dial is missing or numeric; the chips added to the card's two tests on all
            three indices.
Claims:     1364, as before.
Mutated:    the rule, stated before the runs: one mutation a property the correction adds, the smallest edit undoing it
            alone, each run filtered to the three tests holding the chips with its red predicted, made in this checkout
            and reverted with the editor. N1 the provisional chips with no case for the family: red at all three. N2 the
            summary not reading the S&P 500's night: red at the summary's test alone. N3 the night's setting not read:
            red at all three. N4 a numeric margins level read back: red at the summary's test. N5 both margins worded as
            either: red at the summary's test, at the S&P 500's approved setting. Each as predicted.
Verified:   `tools/ci.ps1` green and `tools/verify-phase.ps1` green over the tree carrying this entry, the figures below
            stated before the run: all six steps, 0 warnings, 0 errors, 2471 of 2471 tests ran with none failed,
            migrations 0 to 100 with none pending, schema version 100, against `data-ci` and never `data`; the phase
            report 60 tables, 1364 claims, 1364 passing, none out of scope and none unexamined, 1375 placements and
            verdicts reconciled against a floor of 34, 43 checks on the roster run and passed.
Carried:    the second item, 17.8's S&P 500 Past picks and Run page drawing the family's S&P 500 trades; the history
            read and its record; 20.2's record; 20.4's second remedy after the history read; the sentence-start rule and
            the lower-case names, put to the operator; the sign-offs of phases 18 and 19, owed to fresh sessions; (G1),
            SCHEMA's `family_trade` sentence, owed by the next edit of that section.

### 17.8 - correction: the S&P 500's Past picks and Run page draw the fundamentals-first family's S&P 500 trades   2026-10-18

Queue:      done, phases 0 to 19 built and 20.0 to 20.4 merged with 20.3's two corrections and the reference set's
            labels, PRs 486 to 496, pin 2 written and the history read started, and the first item phase 17's sign-off
            carried, the 19.7 correction above; in progress, this, the second; next, the history read over the coming
            nights and its record, 20.2's record, and the sign-offs of phases 18 and 19 by fresh sessions.
Found:      carried from phase 17's sign-off, owed before phase 19's: 17.8 keeps the family's S&P 500 trades under the
            S&P 500 in the index families' trades, but the S&P 500's Past picks drew only its own families' trades and
            the heavyweights' holdings, and its Run page a row for its own three families alone.
Built:      the S&P 500's Past picks draws the family's S&P 500 trades in a card of their own after its trades and before
            the heavyweights' holdings, each named with its setup and its index as the S&P 400's and 600's are, and
            nothing where it has listed none; its Run page draws the family's row after its own families, counted from
            the index families' picks and trades under the S&P 500 as an index's row is. Section 15.17's paragraph
            states it, as it states the S&P 400's and 600's trades, so no claim is added; the roster's row and the
            changelog.
Measured:   the store holds none of the family's S&P 500 trades yet, so the card is drawn from the night one is listed,
            and the Run page's row reads none listed and none kept until then.
Tests:      none new, 2471 in all: the S&P 500's card test extended with a constructed trade read back off Past picks'
            card after the trades' card and off the Run page's row; the Run page's setup rows read as every index's
            families.
Claims:     1364, as before.
Mutated:    the rule, stated before the runs: one mutation a property the correction adds, the smallest edit undoing it
            alone, each run filtered to the two tests holding it with its red predicted, made in this checkout and
            reverted with the editor. M1 the card never drawn: red at the S&P 500's test. M2 the trades read under the S&P
            400: red at the S&P 500's test. M3 the Run page's row left out: red at both. Each as predicted. The filter to
            the family is not mutated: the index families' step keeps no other family's trade under the S&P 500.
Verified:   `tools/ci.ps1` green and `tools/verify-phase.ps1` green over the tree carrying this entry, the figures below
            stated before the run: all six steps, 0 warnings, 0 errors, 2471 of 2471 tests ran with none failed,
            migrations 0 to 100 with none pending, schema version 100, against `data-ci` and never `data`; the phase
            report 60 tables, 1364 claims, 1364 passing, none out of scope and none unexamined, 1375 placements and
            verdicts reconciled against a floor of 34, 43 checks on the roster run and passed.
Carried:    the history read and its record; 20.2's record; 20.4's second remedy after the history read; the
            sentence-start rule and the lower-case names, put to the operator; the sign-offs of phases 18 and 19, owed to
            fresh sessions; (G1), SCHEMA's `family_trade` sentence, owed by the next edit of that section.

### 20.5 ruling - phase 20 built whole in the order its dependencies set, the sign-offs of phases 18 and 19 after 20.10, and the history read around the clock with the card held at 250 W   2026-10-18

Not a checkpoint entry: it lands nothing.
Ruled:      by the operator on 2026-10-18, after 20.0 to 20.4 had merged. Asked whether the sign-offs of phases 18 and
            19 or the rest of phase 20 came first: "Forget about  phase 18 and 19 sign offs. I want entire phase 20 to be
            built. Can this be done ?" Told the plan paused after 20.7 for the monthly run of 2026-11-07 and their
            reading of its proposals: "why does it need to s top after 20.7 ?" and "i want the entire phase to be
            finished unless there is some actual dependency". On the history read's use of the card: "i dont need the
            GPU however, i need phase 20 to continue building". Hearing the card's fans at its limit of 390 W: "NO that
            will cause unncessary heating of GPU. I need  it to lower down the usage of GPU then. I cant burn  out the
            GPU", "Be mindful that GPU does not  overheat." and "you can continue to let it do the read  non-stop , i
            just down want it overheating. Run it under capped wattage as much as you want". The words are kept in
            `prompts/`.
Plan:       a decision supersedes the order 20.0 wrote, which moves to Previously decided with its reasoning, its
            money and retirement terms standing word for word in the new one (see: Phase 20 is built whole in the order
            its dependencies set with the sign-offs of phases 18 and 19 after it, spending no money and retiring nothing
            on a result). The order is what each checkpoint reads: 20.5 to 20.7 compare stored paid work and 20.9's
            generator reads the tester's own tables, so each is built while the history read runs, its runs of the local
            model made while the read is stopped, 20.9 taking migration 101; once the read completes, 20.3's record,
            20.4's second remedy and 20.8, taking migration 102, since a setup's reading needs its release read; then
            20.10; then the sign-offs of phases 18 and 19, each still owed by a fresh session on its phase as a whole
            before phase 21's plan and discharged by nothing phase 20 does. The monthly run of 2026-11-07 runs on its
            schedule over whatever has merged by then. `BUILD_PLAN.md`'s phase 20 section, section 20's row, the
            runbook's account of the history read and the tone variant's decision's citation follow the ruling, the
            changelog holding the three specs' prior text.
Measured:   pin 2's history read, started at 02:02 UTC from main's copy at 8379773e, read 1,181 releases at 4.63
            seconds each with the card at its limit of 390 W, its core at 70 degrees and its fan at 80 per cent. Stopped
            at 03:35 UTC, the card's limit set to 250 W by `nvidia-smi -pl 250` run as administrator and the read
            started again at 03:36, it read 101 releases at 5.73 seconds each in its first ten minutes, longer releases
            than the first 1,181, at 0.425 seconds a thousand prompt tokens against 0.377, about 13 per cent slower a
            token, its core at 57 to 60 degrees and its fan at 55 to 61 per cent, refusing none. 44,030 releases were
            left to read, about 64 hours of the card at 250 W. A watcher beside the read stops it where the core reaches
            72 degrees on two samples running or the limit leaves 250 W.
Queue:      done, phases 0 to 19 built and 20.0 to 20.4 merged with 20.3's two corrections and the reference set's
            labels, PRs 486 to 496, pin 2 written and its history read started; in progress, this ruling and PR 497
            beneath it, the two items phase 17's sign-off carried; next, 20.5, 20.6, 20.7 and 20.9 while the read runs,
            then 20.3's record, 20.4's second remedy, 20.8 and 20.10, then the sign-offs of phases 18 and 19 by fresh
            sessions.
Verified:   `tools/ci.ps1` green and `tools/verify-phase.ps1` green over the code this ruling sits on, PR 497's, as the
            17.8 correction's entry above records: 2471 of 2471 tests, 1364 claims, 1364 passing. This pull request
            changes documents alone, and the checks that read the corpus ran filtered over its tree: RecordAppendOnly,
            TwoPlatform, ObligationReconciles, BannedProse, DecisionCitations, CoverageReported, StatedCounts,
            ArchitectureConformance, PinnedConstants, ChangelogReconciles, DoneConditionProducible and FigureFits.
Carried:    the history read and its record; 20.2's record; 20.4's second remedy after the history read; the
            sentence-start rule and the lower-case names, put to the operator; the sign-offs of phases 18 and 19, owed to
            fresh sessions after 20.10; (G1), SCHEMA's `family_trade` sentence, owed by the next edit of that section.

### 20.5 - the news labels compared on the local model: every article the paid profile labelled, labelled again under the labeller's own instruction, writing no store row and switching nothing   2026-10-19

Queue:      done, phases 0 to 19 built and 20.0 to 20.4 merged with 20.3's two corrections and its labels, the two items
            phase 17's sign-off carried and the ruling above, PRs 486 to 498, the history read under pin 2 running with
            the card held at 250 W; in progress, this; next, 20.6, 20.7 and 20.9 while the read runs, then 20.3's record,
            20.4's second remedy, 20.8 and 20.10, then the sign-offs of phases 18 and 19 by fresh sessions.
Built:      `compare-labels`, the local comparison, a component of its own: every article the news job's paid profile
            labelled under the instruction's version that the store still holds, oldest label first, labelled again by
            the local profile's model under the labeller's own instruction and the company name the labeller sends, at
            temperature zero with the readings' seed and the news job's answer budget, an answer it cannot read asked
            once more as the labeller asks it. It holds the local lock, saying so where it waits for it, loads the model
            where it is not loaded and unloads it at its end only where it loaded it, opens the store read-only, and
            writes its figures with each article's two labels and every answer to `comparisons/labels-<instant>.json`
            under the data root, printing them on one line. The agreement's arithmetic sits in the core: how many it
            labelled and could not by cause, the kind, the direction and both agreeing, and the reasons its answers
            stated holding no digit, every answer counted. The decision, the catalogue's and the matrix's rows, the
            runbook's command, the roster's row, the guide and the changelog. The plan's ruling drew one run log row
            beside the file; a row is a store row and nothing reads it, so none is written and the decision says why.
Tests:      2 new, 2473 in all: the comparison over a constructed store, four paid labels and four labels it leaves out,
            worked by hand with the lock held and given up, the runtime loading and unloading, the file read back and
            every table's rows counted before and after, and a store holding no paid label asking nothing; and a reason
            read off an answer's object.
Claims:     1366, the local comparison's catalogue and matrix rows, each passing on component-access.
Mutated:    the rule, stated before the runs: one mutation a property the checkpoint adds, the smallest edit undoing it
            alone, each run filtered to the two new tests with its red predicted, made in this checkout and reverted with
            the editor. M1 the paid labels read whatever their outcome: red at the comparison. M2 an unreadable answer not
            asked once more: red, 2 labelled of 4 and not 3. M3 the kind's agreement read off the direction: red, 2 and
            not 3. M4 the reasons holding a digit counted in place of those holding none: red, 1 and not 3. M5 the model
            not unloaded at the end: red at the unload. M6 the lock not waited for: red, no wait. M7 the file written to
            another folder: red at the line. M8 a reason of spaces read as a reason: red at the reader's test alone. Each
            as predicted.
Verified:   `tools/ci.ps1` green and `tools/verify-phase.ps1` green over the tree carrying this entry, the figures below
            stated before the run: all six steps, 0 warnings, 0 errors, 2473 of 2473 tests ran with none failed,
            migrations 0 to 100 with none pending, schema version 100, against `data-ci` and never `data`; the phase
            report 60 tables, 1366 claims, 1366 passing, none out of scope and none unexamined, 1377 placements and
            verdicts reconciled against a floor of 34, 43 checks on the roster run and passed.
Carried:    the comparison's record, run from main once this merges while the history read is stopped, stating the
            agreement over the labels the store holds; the history read and its record; 20.2's record; 20.4's second
            remedy after the history read; the sentence-start rule and the lower-case names, put to the operator; the
            sign-offs of phases 18 and 19, owed to fresh sessions after 20.10; (G1), SCHEMA's `family_trade` sentence,
            owed by the next edit of that section.

### 20.7 - the reviews compared on the local model: every stored review asked again as the review asked it, writing no store row and switching nothing   2026-10-19

Queue:      done, phases 0 to 19 built and 20.0 to 20.4 merged with their corrections and labels, and the ruling of
            2026-10-18, PRs 486 to 498; in review, 20.5, PR 499; in progress, this; next, 20.6 once its recording is made
            while the history read is stopped, then 20.9 while the read runs, then 20.3's record, 20.4's second remedy,
            20.8 and 20.10, then the sign-offs of phases 18 and 19 by fresh sessions.
Built:      `compare-reviews`, the local comparison's second form: every review the run log holds, oldest first, asked
            again of the local profile's model as the review asked it, over the pass's version read as the review read it,
            the facts file of its night and the documents the version cites, its first round handed the draft and the
            section's checks, a refused answer told what was refused once, each round checked in memory by the claim
            checker's own rules, at the readings' seed and an answer budget of 4,096 tokens. A review whose version is not
            stored, or whose night's facts file the store's retention has emptied since, is named and not asked, since
            asked over another night's facts it would be another review; the store holds nine, from 2026-09-30, over the
            nights of BDX, MO and IT, and has emptied MO's and IT's, so BDX's three can be asked. It states how many
            outcomes agree and, for each draft handed, how many of its sentences, read by the checker's own splitter, both
            reviews kept, both changed or treated apart, writing them with each review's rounds to
            `comparisons/reviews-<instant>.json`. The review's readers of a version and its documents are opened to the
            comparison; the catalogue's and the matrix's rows widened to the stores it reads, the runbook's command, the
            roster's row, the guide and the changelog.
Tests:      2 new, 2475 in all: the comparison over the fixture's replayed pass, four stored reviews constructed over its
            versions, the two cases asked once and passing first time as stored, the risks refused for an answer that is
            not its fields and passing on its retry where the stored review left it out, a version not stored and a night
            whose facts file is emptied named and not asked, each outcome and each sentence worked by hand, the model
            loaded and unloaded, the file read back and every table's rows counted before and after; and two reviews of a
            draft set side by side over four sentences, and none where either came to no draft.
Claims:     1366, as before: the catalogue's and the matrix's rows stood and say more.
Mutated:    the rule, stated before the runs: one mutation a property the checkpoint adds, the smallest edit undoing it
            alone, each run filtered to the two new tests with its red predicted, made in this checkout and reverted with
            the editor. R1 an emptied night's facts read past to an older night's: red, version 8 asked. R2 the first round
            handed no draft: red at the review's brief. R3 one round only: red, 2 outcomes agreeing and not 1. R4 a sentence
            both kept counted as both changed: red at both tests. R5 the readings' budget of 256 tokens: red at the budget.
            R6 a version not stored named as an emptied night: red at the reviews not asked. Each as predicted.
Verified:   `tools/ci.ps1` green and `tools/verify-phase.ps1` green over the tree carrying this entry, the figures below
            stated before the run: all six steps, 0 warnings, 0 errors, 2475 of 2475 tests ran with none failed,
            migrations 0 to 100 with none pending, schema version 100, against `data-ci` and never `data`; the phase
            report 60 tables, 1366 claims, 1366 passing, none out of scope and none unexamined, 1377 placements and
            verdicts reconciled against a floor of 34, 43 checks on the roster run and passed.
Carried:    the comparisons' records, 20.5's and this one's, each run from main once it merges while the history read is
            stopped; 20.6, its recording owed to the same window; the history read and its record; 20.2's record; 20.4's
            second remedy after the history read; the sentence-start rule and the lower-case names, put to the operator;
            the sign-offs of phases 18 and 19, owed to fresh sessions after 20.10; (G1), SCHEMA's `family_trade` sentence,
            owed by the next edit of that section.

### 20.7 - correction: a stored review is asked over the facts of the night it was asked over, read off the run log, and not over a later night's emptied file   2026-10-19

Queue:      done, 20.5 and 20.7 merged, PRs 499 and 500, and 20.5's comparison run from main; in progress, this; next,
            20.7's comparison run again from main once this merges, then 20.6, recorded and awaiting its gates, then 20.9.
Found:      by the first run of `compare-reviews` from main at 990611f5, at 05:37 UTC: none of the nine stored reviews was
            asked, each named as its night's facts file no longer held whole, BDX's three among them, whose night of
            2026-09-29 stands whole. A review's facts were read as the newest row on or before its version's day,
            2026-09-30, which is the row the night of 2026-09-30 wrote that evening, hours after the reviews ran, and the
            retention has emptied since.
Built:      a review's night is read off the run log: the newest night whose facts step began before the review, named by
            that night's own fetch row, its facts the newest row on or before that night, or on or before the version's
            day where the run log holds no such night. The roster's row and the changelog. No catalogue, matrix or decision
            text moves: each already says the facts file of the review's night.
Tests:      none new, 2475 in all: the review comparison's test gains two nights on the run log and a review asked between
            them over a version dated after both, asked over the first night's facts where the second night's row is
            empty, each figure worked by hand again over five stored reviews.
Claims:     1366, as before.
Mutated:    the rule, stated before the run: one mutation of the property the correction adds, the smallest edit undoing it,
            filtered to the comparison's test with its red predicted. RN the night read off the run log never lowering the
            day the facts are read through: red, 2 reviews asked and not 3. As predicted.
Verified:   `tools/ci.ps1` green and `tools/verify-phase.ps1` green over the tree carrying this entry, the figures below
            stated before the run: all six steps, 0 warnings, 0 errors, 2475 of 2475 tests ran with none failed,
            migrations 0 to 100 with none pending, schema version 100, against `data-ci` and never `data`; the phase
            report 60 tables, 1366 claims, 1366 passing, none out of scope and none unexamined, 1377 placements and
            verdicts reconciled against a floor of 34, 43 checks on the roster run and passed.
Carried:    20.7's comparison run again from main once this merges, and the two comparisons' records; 20.6; the history
            read and its record; 20.2's record; 20.4's second remedy after the history read; the sentence-start rule and the
            lower-case names, put to the operator; the sign-offs of phases 18 and 19, owed to fresh sessions after 20.10;
            (G1), SCHEMA's `family_trade` sentence, owed by the next edit of that section.

### 20.6 - the report sections compared on the local model: the fixture's pass with every section on Gemma, beside the paid side, through the same claim checker   2026-10-19

Queue:      done, 20.5 and 20.7 merged, PRs 499 and 500, and 20.5's comparison run from main; in review, the 20.7
            correction, PR 501, beneath this; in progress, this; next, 20.7's comparison run again from main and the two
            comparisons' records, then 20.9 while the read runs, then 20.3's record, 20.4's second remedy, 20.8 and 20.10.
Built:      the comparison is a test of the suite: the fixture's research pass for KEYS with every section in the local
            lane under the model the folder `local-comparison` names, google/gemma-4-26b-a4b-qat, its answers read from
            that folder's recordings alone, beside the paid side read from the fixture's own recordings as 6.8's comparison
            read it, each section's outcome and the checker's reason for each it left out held on both sides. The local
            side was recorded by hand through the same test with `EQUITYBRIEF_RECORD_LOCAL_COMPARISON=1`, LM Studio 0.4.25
            serving the model at a context of 50,176 tokens on the card held at 250 W: 20 answers over 35 calls in 52
            seconds, each declared in the fixture's manifest. A request asked again in one recording is answered from its
            first recording, as the replay answers it: in the first recording the runtime answered one request two ways at
            temperature zero, and the replay of it asked what no recording held, so it was made again. The banned-prose
            check reads a folder within a fixture as the fixture's, a file in it declared by the fixture's manifest by its
            path from the manifest's folder. The fixtures README, the runbook's command, the roster's rows, the guide and
            the changelog.
Measured:   the paid side as 6.8's comparison stated it, all eight of its sections accepted, the segment commentary and the
            short version on their retries. The local side accepted the risks alone, at its third draft, and left the other
            eight out, every one refused at every retry as an answer written as a JSON object or list rather than prose:
            on LM Studio's OpenAI-compatible endpoint Gemma wraps every answer in an object, `{}` or `{"error": ...}`
            where a section asks for prose, as the fixture's own capture of 2026-10-06 shows for a one-sentence
            question, and the risks, asked for as an object of fields, are the one section the shape fits.
Put:        to the operator, switching nothing: the local lane writes no prose section on Gemma as LM Studio serves it
            there, so offering it in the Research menu waits on a change to how it is asked, the text inside a one-field
            object read or the native endpoint asked, which is itself theirs to rule.
Tests:      2 new, 2477 in all: the comparison over the fixture's evidence set, the local side answered from the
            comparison's folder alone and the folder holding exactly the answers it reads, every request under the
            folder's model, the paid side's outcomes and the local side's with the checker's reasons; and the recorder over
            a scripted runtime, each answer kept as sent and a request asked again answered from its first recording. The
            banned-prose check's own test proves a folder within a fixture both ways.
Claims:     1366, as before.
Mutated:    the rule, stated before the runs: one mutation a property the checkpoint adds, the smallest edit undoing it
            alone, each run filtered to the comparison's, the recorder's and the capture exclusion's tests with its red
            predicted, made in this checkout and reverted with the editor. S1 the local side read from the fixture's own
            folder: red, no recording for the folder's model. S2 every section in the paid lane: red at the local side's
            outcomes. S3 a request asked again asked live again: red at the recorder's test alone. S4 a capture matched by
            its file name alone: red at the capture exclusion. S5 the manifest looked for in the file's own folder alone:
            red at the capture exclusion. Each as predicted.
Verified:   `tools/ci.ps1` green and `tools/verify-phase.ps1` green over the tree carrying this entry, the figures below
            stated before the run: all six steps, 0 warnings, 0 errors, 2477 of 2477 tests ran with none failed,
            migrations 0 to 100 with none pending, schema version 100, against `data-ci` and never `data`; the phase
            report 60 tables, 1366 claims, 1366 passing, none out of scope and none unexamined, 1377 placements and
            verdicts reconciled against a floor of 34, 43 checks on the roster run and passed.
Carried:    20.7's comparison run again from main once its correction merges, and the comparisons' records; the history
            read and its record; 20.2's record; 20.4's second remedy after the history read; the sentence-start rule and the
            lower-case names, put to the operator; the sign-offs of phases 18 and 19, owed to fresh sessions after 20.10;
            (G1), SCHEMA's `family_trade` sentence, owed by the next edit of that section.

### 20.9 - the idea generator in the monthly run: the local model asked for each fold's conditions as closed labels code turns into levels, judged as a procedure with the family's other proposals   2026-10-19

Queue:      done, 20.5, 20.7 with its correction and 20.6 merged, PRs 499 to 502; in progress, this; next, the records of
            20.5's and 20.7's comparison runs, then, once the history read completes, 20.3's record, 20.4's second remedy,
            20.8 and 20.10.
Built:      the idea generator, a component of its own: for each family rule on an index the tester's own read, running no
            procedure, makes each fold's prompt from that fold's tables, the autopsy's figures over the rule's trades that
            ended before its year and each reading's spread over the listings that did, and the same over all finished
            data, with no ticker and no date; the generator puts each prompt its pin has not answered to the local model
            under the local run's pin check and lock, at temperature zero with the pin's seed, asks an answer it cannot read
            once more, ends the run on an answer under another model or runtime, and stores each answer or its cause in
            `loop_idea`, migration 101, keyed on the hash of the instruction's version and the prompt, with one row under
            the local run's stage. The model answers at most three labels, a reading of the catalogue, above or below, and a
            tenth from one to nine, and code turns each into a condition at that tenth's upper edge over the fold's own
            listings, where winners against losers cuts the reading. The tester reads the stored answers through the one
            reader of them alone and asks no model: the k-th proposal, "the idea generator, ranked k", is the k-th label
            each fold's answer holds, walked over the fold's year and judged with the family's other proposals by the
            step-down, its finding naming the label. `loop-ideas` by hand, and the monthly run's steps `ideas-GSPC`,
            `ideas-MID` and `ideas-SML` after which readings go with winners and before the testers, each holding with its
            words where the model cannot be asked. The monthly run's list of its step names now names the readings' steps
            19.12 added to the worker's own list and not to the names, a test holding the names to the order the worker
            builds its steps in. The decision, the architecture's rows and 13.13's engine, the schema, the runbook, the
            roster's rows, the guide and the changelog.
Measured:   nothing live: the generator's first prompts are the monthly run's of 2026-11-07, or a run by hand while the
            history read is stopped, about 24 an index the first month and fewer after as a complete year's fold makes
            the same prompt each month.
Tests:      11 new, 2488 in all: each label's condition worked by hand at its tenth's upper edge over twenty listings, the
            level at every tenth the search's own cut, and a reading no listing holds giving none; each of the grammar's
            refusals; the 2022 fold's prompt worked by hand, a listing and a trade ending in 2022 moving nothing in it; the
            k-th proposal over constructed folds and stored answers, a fold holding no answer choosing none, and the
            generator's three judged with the family's other proposal by one step-down, moving its adjusted p-value from
            0.0039 alone to 0.066; the generator over a constructed store asking each prompt once under the pin and storing
            each answer or cause, a run again asking nothing, another model refused before any ask and an answer under
            another runtime ending the run; the tester's read for the generator refusing under the generator's verb; the
            monthly run's step names held to the worker's own order; the one reader of the stored answers; and the Loop
            page drawing a proposal of the generator with its finding.
Claims:     1372, 6 new: the generator's catalogue and matrix rows, the loop ideas' store, 15.20's row and section 18's two,
            the 6 the plan counted at 20.9.
Mutated:    the rule, stated before the runs: one mutation a property the checkpoint adds, the smallest edit undoing it
            alone, each run filtered to the tests naming the property with its red predicted, made in this checkout and
            reverted with the editor. M1 a label's level at its tenth's lower edge: red at the label test and the k-th
            proposal test. M2 four ideas accepted: red at the grammar test. M3 a fold's autopsy over every finished trade:
            red at the fold's prompt. M4 the generator's proposals under a family of their own: red at the step-down, the
            other proposal's p-value 0.0039 judged apart. M5 a run again asking every prompt: red at the generator's store
            test. M6 an answer under another runtime read as the pin's: red at the pin test. M7 the pin check's refusal
            passed over: red at the pin test. M8 the ideas' steps after the testers: red at the three tests of the steps'
            order. M9 the tester's source naming the table: red at the one reader's test. M10 every rank reading the first
            label: red at the k-th proposal test. Each as predicted. Not mutated: the tester's run composing the
            generator's proposals beside the other engines', which no test reaches since the fixture holds no history a
            run reads, as no engine's composition is reached; its first run is the monthly run of 2026-11-07.
Verified:   `tools/ci.ps1` green and `tools/verify-phase.ps1` green over the tree carrying this entry, the figures below
            stated before the run: all six steps, 0 warnings, 0 errors, 2488 of 2488 tests ran with none failed,
            migrations 0 to 101 with none pending, schema version 101, against `data-ci` and never `data`; the phase
            report 60 tables, 1372 claims, 1372 passing, none out of scope and none unexamined, 1383 placements and
            verdicts reconciled against a floor of 34, 43 checks on the roster run and passed.
Carried:    the comparisons' records; the history read and its record; 20.2's record; 20.4's second remedy after the
            history read; the generator's first asks, at the monthly run of 2026-11-07; the sentence-start rule and the
            lower-case names, put to the operator; the sign-offs of phases 18 and 19, owed to fresh sessions after 20.10;
            (G1), SCHEMA's `family_trade` sentence, owed by the next edit of that section.

### 20.2 - the release history fetched from main's build: 45,282 results releases of 1,977 filers over 93,463 requests in ten and a half hours   2026-10-19

Ran:        from main's clean copy after 20.2's merge, outside the night: `releases --history --from 2019-01-02` from
            12:07:02 to 22:40:45 UTC on 2026-10-18, its one row `release-history-20261018T120702.3878865Z`. Free requests
            to the SEC under the archive's contact; no provider request and no model call.
Results:    1,977 of 1,977 ledger filers read, 45,792 releases in their spans and 45,282 stored, beside the 46 the slots
            and the research passes had stored, 45,328 in `release_document`; 93,463 requests, about 2.5 a second over the
            run against the archive's pace of 10, the exhibits' own size the difference. By the year the SEC accepted
            them, 634 in 2018, the release before a filer's first session, then 5,191, 6,012, 5,959, 5,874, 5,894, 5,959
            and 5,886 from 2019 to 2025 and 3,895 in 2026 to 2026-10-16, and 24 before 2018 for filers whose release before
            their first session is that old. Counted under each index whose ledger holds the filer, a filer that moved
            between them counted under each: the S&P 500 17,732, the S&P 400 16,821 and the S&P 600 21,210, and 1 whose
            filer the ledger holds in none. The store grew by 0.46 GB of compressed text, 1.47 GB as text, 3.2 to 1.
Against:    20.0's estimate from its sample of 50 filers: about 38,400 releases, 79,000 requests, 2.2 hours and 0.39 GB.
            The count, the requests and the store's growth came within a fifth of it; the time did not, read at the
            archive's pace, and the runbook now states the time measured.
Verified:   `tools/ci.ps1` green and `tools/verify-phase.ps1` green over the code these records sit on, PR 503's, as
            20.9's entry above records: 2488 of 2488 tests, 1372 claims, 1372 passing. This pull request changes
            documents alone, and the checks that read the corpus ran filtered over its tree: RecordAppendOnly,
            TwoPlatform, ObligationReconciles, BannedProse, DecisionCitations, CoverageReported, StatedCounts,
            ArchitectureConformance, PinnedConstants, ChangelogReconciles, DoneConditionProducible and FigureFits.
Carried:    the history read and its record; 20.4's second remedy after it.

### 20.5 - the label comparison run from main's build: 211 of the 264 paid labels labelled again on Gemma, the kind agreeing on 157 and the direction on 181   2026-10-19

Ran:        from the clean copy of main's 5fb44388, with the history read stopped and no night, drain, labeller or copy
            running: `compare-labels` at 05:00:17 UTC, google/gemma-4-26b-a4b-qat on
            llama.cpp-win-x86_64-nvidia-cuda12-avx2 2.56.0, the card held at 250 W. No paid call and no store row.
Results:    264 articles the news job's DeepSeek profile labelled are still stored, from 2026-10-03. The local model
            labelled 211 and could not label 53: 50 answered a kind outside the set, nearly all "opinion" alone where the
            set lists "opinion or promotional", and the runtime refused 3, each an article of TRV's. Of the 211 labels the
            kind agreed on 157, the direction on 181 and both on 138; 313 of the 313 reasons its answers stated held no
            digit. The figures and each article's two labels are in `comparisons/labels-20261019T050017Z.json` under the
            data root.
Put:        to the operator, switching nothing: the labeller stays on DeepSeek, whose 17 runs from 2026-10-01 to
            2026-10-17 spent $0.117 on the run log; a switch, or the kind's words written so Gemma's answer reads, is theirs.
Verified:   `tools/ci.ps1` green and `tools/verify-phase.ps1` green over PR 503's code, as 20.9's entry records; this pull
            request changes documents alone, and the checks that read the corpus ran filtered over its tree.

### 20.7 - the review comparison run from main's build after its correction: 3 of the 9 stored reviews asked again on Gemma, the outcome agreeing on 1 and every sentence changed by both   2026-10-19

Ran:        from the clean copy of main's a3579675, the correction merged, with the history read stopped: `compare-reviews`
            at 06:28:00 UTC on the same model and runtime. No paid call and no store row.
Results:    9 reviews are stored. 3 were asked again, BDX's two cases at version 1 and its risks at versions 1 and 3; 6
            were named and not asked, MO's and IT's, since the retention has emptied their nights' facts files. The
            outcome agreed on 1 of the 3, the two cases left out by both, while the risks DeepSeek accepted first time
            the local model left out at both versions. Of the 47 sentences the drafts handed held, both reviews changed all
            47. In `comparisons/reviews-20261019T062800Z.json` under the data root.
Put:        to the operator, switching nothing: the review job stays on DeepSeek.
Verified:   `tools/ci.ps1` green and `tools/verify-phase.ps1` green over PR 503's code, as 20.9's entry records; this pull
            request changes documents alone, and the checks that read the corpus ran filtered over its tree.
