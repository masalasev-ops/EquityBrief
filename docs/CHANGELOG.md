# CHANGELOG.md

The prior text of every clean edit to a spec. `changelog-reconciles` reads the git history and fails any commit that deleted a line from a spec without changing this file.

Specs are `CLAUDE.md`, `ARCHITECTURE.html`, `SCHEMA.md`, `BUILD_PLAN.md` and `RUNBOOK.md`, and the four files under `.claude/rules/`, which carry `CLAUDE.md`'s own text and are edited on the same terms. Records correct themselves with new dated entries and do not appear here.

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

### 2026-09-22 - ARCHITECTURE.html - section 11's flag no longer says the twenty lasts until the thresholds are calibrated
Authorised by: The page shows twenty and states the true count
Was:
> <p><b>The thresholds in this table are proposals and are known to be untested.</b> Two of them will produce too many names as written. Earnings soon fires for a large part of the index during reporting season and goes quiet between, and at entry zone fires often because the nearest support band is rarely far away. The calibration is not a backfill: the run page records how many names fired each night and which reason contributed, so the thresholds are set against your own distribution once sixty nights of listings have accumulated, which is the quarter of trading the level window already uses and long enough that a distribution of fired counts is not one week's weather (see: Condition thresholds are calibrated from your own nights, not from a backfill) (owes: The six reason thresholds calibrated from the nights they fired on). This paragraph said "after a few weeks" until the phase 5 sign-off, against an obligation stating sixty nights, which is two places holding one fact and the vaguer of the two is the one a reader meets first. Until it fires the app shows the strongest twenty and states the true fired count in the header, so a hundred-name night says a hundred. The first night at index size fired 477 of 503 on 798 reasons, and most of that was not the thresholds: earnings soon counted the stored bars after the night, which a live store never holds, so every future print read as tonight's and fired, while breakout on volume read a role set against tonight's close and could not fire at all. Both were corrected at 5.4, and the rows written before are kept as written with a line saying so where a page draws them (see: Sessions to a dated event are counted on the exchange calendar and never on stored bars) (see: Breakout on volume reads resistance at the previous session's close).</p>
Now:
> <p><b>The thresholds in this table are proposals and are known to be untested.</b> Two of them will produce too many names as written. Earnings soon fires for a large part of the index during reporting season and goes quiet between, and at entry zone fires often because the nearest support band is rarely far away. The calibration is not a backfill: the run page records how many names fired each night and which reason contributed, so the thresholds are set against your own distribution once sixty nights of listings have accumulated, which is the quarter of trading the level window already uses and long enough that a distribution of fired counts is not one week's weather (see: Condition thresholds are calibrated from your own nights, not from a backfill) (owes: The six reason thresholds calibrated from the nights they fired on). This paragraph said "after a few weeks" until the phase 5 sign-off, against an obligation stating sixty nights, which is two places holding one fact and the vaguer of the two is the one a reader meets first. The first night at index size fired 477 of 503 on 798 reasons, and most of that was not the thresholds: earnings soon counted the stored bars after the night, which a live store never holds, so every future print read as tonight's and fired, while breakout on volume read a role set against tonight's close and could not fire at all. Both were corrected at 5.4, and the rows written before are kept as written with a line saying so where a page draws them (see: Sessions to a dated event are counted on the exchange calendar and never on stored bars) (see: Breakout on volume reads resistance at the previous session's close).</p>
Why: the operator ruled on 2026-09-22 that at most twenty rows are drawn, stated once in the selection section with no end condition. Section 11.1 now states it, so the flag's sentence saying the twenty lasts until the thresholds are calibrated is removed rather than left as a second statement of one fact.

### 2026-09-22 - ARCHITECTURE.html - section 22 states that questions are open again
Authorised by: A candidate is judged by a sign-flip test over blocks of 63 sessions, with at least eight blocks
Was:
> <p class="note">No question is open. Every fork that stood here has been settled, and each is recorded below with how it settled rather than deleted. The six that stood here on 4 September were not questions: three were values with a stated way of settling them, which now sit in the limits table and the phase table; two were things already decided elsewhere in this document; and one was a recommendation written as a question. They are recorded in section 22 and the changelog rather than left here, because an open list that holds settled things makes the one real fork harder to see.</p>
Now:
> <p class="note">The questions phase 10 leaves open are listed first, each linked from the rule that rests on it. Before phase 10 no question was open. Every fork that stood here has been settled, and each is recorded below with how it settled rather than deleted. The six that stood here on 4 September were not questions: three were values with a stated way of settling them, which now sit in the limits table and the phase table; two were things already decided elsewhere in this document; and one was a recommendation written as a question. They are recorded in section 22 and the changelog rather than left here, because an open list that holds settled things makes the one real fork harder to see.</p>
Why: phase 10's rules rest on questions no source read settles and on two questions to the operator about the live family, and each rule marked not settled by research links to its question here. A note saying no question is open would contradict the table beneath it.

### 2026-09-22 - BUILD_PLAN.md - the run page's duration row says the first 5.6 correction's run could be a stop and names the second
Corrects: the row stated the duration repaired at the 5.6 correction, whose run was the last to write a listings row, which a stop recorded under the stage also writes; found by the fourth phase 8 sign-off review over 40f5a77
Was:
> The duration was not repaired here: its run was still chosen by the newest start until the 5.6 correction of 2026-09-22, which takes the run that wrote the night's listings row last, by the rowid, and `read-surface` refuses a run-log read that takes its newest row by the instant it carries, outside the two reads that state why they may.
Now:
> The duration was not repaired here: its run was still chosen by the newest start until the 5.6 correction of 2026-09-22, which took the run that wrote the night's listings row last, by the rowid. That row can be a stop the night records under the stage, where a step that failed or was stopped rolled its list back and the store holds the list of the run before it, so a second 5.6 correction of the same day takes the last run whose listings row the stage wrote itself, told from a stop by its outcome, and the last to reach the stage only where no run wrote a list. `read-surface` refuses a run-log read that takes its newest row by the instant it carries, outside the two reads that state why they may.
Why: the row read as discharging a repair that a night run again whose listings step failed still defeated, drawing the failed run's span beside the earlier run's fired count.

### 2026-09-22 - BUILD_PLAN.md - the run page's duration row says the ordering reached the default night at 6.0 and the duration only at the 5.6 correction
Corrects: the row stated the duration repaired at 6.0 where 6.0's rowid ordering reached the run page's default night alone; found by the operator's question after the 5.6 correction of 2026-09-22
Was:
> The duration is repaired rather than tested around: the newest run is ordered by the rowid, which is the write order, and not by the instant a replay stamps from 21:10Z, so a night replayed for an older session after tonight's no longer takes over the page.
Now:
> The run page's default night is repaired rather than tested around: the newest run is ordered by the rowid, which is the write order, and not by the instant a replay stamps from 21:10Z, so a night replayed for an older session after tonight's no longer takes over the page. The duration was not repaired here: its run was still chosen by the newest start until the 5.6 correction of 2026-09-22, which takes the run that wrote the night's listings row last, by the rowid, and `read-surface` refuses a run-log read that takes its newest row by the instant it carries, outside the two reads that state why they may.
Why: the row read as discharging a repair the code did not carry, which is how the duration's wrong run stayed live from 6.0 to the phase 8 sign-off's third review.

### 2026-09-21 - ARCHITECTURE.html - section 1 states the purpose as a selection of the stocks worth buying or paying attention to
Authorised by: Tonight's list selects the stocks worth buying or paying attention to that evening, and the improvement loop exists to make that selection better
Was:
> <p><b>What it is for.</b> To answer one question every evening, which stocks are sitting at a price their own chart has made significant, and then to produce a full research report on any name you choose to open. Four of the six conditions detect arrival at a price the evening's arithmetic has an instruction for, one detects a break on unusual volume, and one is a calendar fact. That arithmetic is not authored in advance and is not carried over from a previous night: the levels and the plan are recomputed from the chart every evening, so a name arriving at a level and the plan naming that level are the same night's work. Whether those arrivals are worth acting on is measured rather than assumed, and section 13 is how. The universe is the S&amp;P 500, and its membership is fetched from the provider rather than maintained by hand. (see: The universe is the S&amp;P 500, and membership is fetched, not maintained) The report it produces, specified in section 4, (see: Section 4 of the architecture defines the report, and nothing outside the corpus does) carries a narrative verdict, how the stock got here with the causes of its biggest moves, what the company sells, the numbers over five quarters, the industry cycle it sits in, the bull and bear cases side by side, a chart with levels and a volume profile, a staged entry and exit plan in two books, a risk table where every risk names the number that would confirm it, and a dated calendar.</p>
>
> <p><b>What it is not.</b> It is not a screen: the universe is a published index membership, not a filtered set, and the shortlist selects on chart state alone with no fundamentals and no model in the decision. It does not trade, does not size positions, and produces no short or options ideas. (see: The plan places a position and never sizes one) (see: Improving what surfaces a name is in scope; ranking names against each other is not)</p>
Now:
> <p><b>What it is for.</b> To select, every evening, the stocks worth buying or paying attention to as of that day, and then to produce a full research report on any name you choose to open. The selection starts from the stocks sitting at a price their own chart has made significant, and the improvement loop in section 13 exists to make it select better. (see: Tonight's list selects the stocks worth buying or paying attention to that evening, and the improvement loop exists to make that selection better) Four of the six conditions detect arrival at a price the evening's arithmetic has an instruction for, one detects a break on unusual volume, and one is a calendar fact. That arithmetic is not authored in advance and is not carried over from a previous night: the levels and the plan are recomputed from the chart every evening, so a name arriving at a level and the plan naming that level are the same night's work. Whether those arrivals are worth acting on is measured rather than assumed, and section 13 is how. The universe is the S&amp;P 500, and its membership is fetched from the provider rather than maintained by hand. (see: The universe is the S&amp;P 500, and membership is fetched, not maintained) The report it produces, specified in section 4, (see: Section 4 of the architecture defines the report, and nothing outside the corpus does) carries a narrative verdict, how the stock got here with the causes of its biggest moves, what the company sells, the numbers over five quarters, the industry cycle it sits in, the bull and bear cases side by side, a chart with levels and a volume profile, a staged entry and exit plan in two books, a risk table where every risk names the number that would confirm it, and a dated calendar.</p>
>
> <p><b>What it is not.</b> The universe is a published index membership, not a filtered set, and the shortlist selects on chart state alone with no fundamentals and no model in the decision. (see: No reading of the fundamentals fires a reason, gates a tranche or draws a panel) It does not trade, does not size positions, and produces no short or options ideas. (see: The plan places a position and never sizes one)</p>
Why: the operator ruled on 2026-09-21 that the list exists to select the stocks worth buying or paying attention to that evening. Section 1 said the tool answers which stocks sit at a price their chart has made significant and is not a screen, which is the position the ruling replaces. What it is not keeps every boundary that still holds, with the fundamentals rule cited where it had been implied.

### 2026-09-21 - ARCHITECTURE.html - section 13.5 states the boundary as what a change may use and where its result is shown
Authorised by: Tonight's list selects the stocks worth buying or paying attention to that evening, and the improvement loop exists to make that selection better
Was:
> <p>Improving which conditions surface a name for reading stays inside what section 1 says this tool is. Ranking names by expected return is a different tool with different obligations, and a scoring loop makes that line easy to cross one small step at a time. The test to apply to any future change: does it decide which chart is worth opening, or does it decide which stock is better than another. The first is this project. The second is not. (see: Improving what surfaces a name is in scope; ranking names against each other is not)</p>
Now:
> <p>Section 1 says the list selects the stocks worth buying or paying attention to that evening, so a change that makes the selection better is inside this project, including one that orders names against each other. (see: Tonight's list selects the stocks worth buying or paying attention to that evening, and the improvement loop exists to make that selection better) The boundary is what a change may use and where its result may be shown. The nightly run stays arithmetic, with no model call and no per-name request. (see: The nightly run is arithmetic only) No reading of the fundamentals fires a reason or gates a tranche. (see: No reading of the fundamentals fires a reason, gates a tranche or draws a panel) The plan places a position and never sizes one. (see: The plan places a position and never sizes one) A reason's record is shown beside the reason and never beside the ticker. (see: A reason's record is displayed, beside the reason and never beside the name) A condition is registered before it is scored and scored in shadow before it is shown, and the register is never edited. (see: Candidate conditions are registered before they are scored, and scored in shadow before they are shown)</p>
Why: the test the section stated, whether a change decides which stock is better than another, is the line the ruling removes. The boundaries that stand are restated one per sentence, each with the decision that holds it.

### 2026-09-21 - ARCHITECTURE.html - section 15.14's ordering bullet cites the decision that replaced the one it cited
Authorised by: Tonight's list selects the stocks worth buying or paying attention to that evening, and the improvement loop exists to make that selection better
Was:
> <li>No screen orders names by a judgement about the company. The universe orders by distance to a level and tonight's list by how many reasons fired and then band strength, each a fact about the chart. (see: Improving what surfaces a name is in scope; ranking names against each other is not) (see: Tonight's list orders by the reasons that fired and then band strength, which are facts about the chart)</li>
Now:
> <li>No screen orders names by a judgement about the company. The universe orders by distance to a level and tonight's list by how many reasons fired and then band strength, each a fact about the chart. (see: Tonight's list selects the stocks worth buying or paying attention to that evening, and the improvement loop exists to make that selection better) (see: Tonight's list orders by the reasons that fired and then band strength, which are facts about the chart)</li>
Why: the bullet cited a decision now under Previously decided, which `no-superseded-citation` refuses. What the bullet says stays true of the screens as built, and the order tonight's list uses is for the next phase's planning pass to rule.

### 2026-09-21 - SCHEMA.md - research_request's settled_at says when it is null, which is while a request is outstanding or being written
Corrects: the column note said the instant the state last moved off `outstanding`, null while it has not, and the declared column sets below it and the drain's own claim say a claim writes `state` alone, so a request being written carries a null `settled_at` the note said it could not. Found in the phase 9 second sign-off review by reading the note against the statement that claims a request.
Was:
> | `settled_at` | TEXT | UTC instant the state last moved off `outstanding`, null while it has not |
Now:
> | `settled_at` | TEXT | UTC instant the request settled or was withdrawn, written by RequestDrain when it moves the request to `written` or `refused` and by ReadApi when it moves it to `withdrawn`; null while it is `outstanding` or `writing`, because a claim writes `state` alone |
Why: the code is right and the note was not. The note now names the states in which the column is null, and `read-surface` reads those states off the note and asserts them against a request moved through the store, so the two cannot come apart again unnoticed.

### 2026-09-20 - ARCHITECTURE.html - the queue screen says which lane would write a report, and its outstanding region is one clause

Authorised by: The report generation the operator asked for is built as phase 9, and phase 8's sign-off is owed after it rather than before it
Was:
> <tr><td>Outstanding</td><td>every request nobody has started, oldest first, which is the order the
> worker takes them in</td></tr>
Now:
> <tr><td>Outstanding</td><td>every request nobody has started, drawn in the order the worker takes
> them in</td></tr>, and a row is added after Take it out: <tr><td>Which lane would write one</td><td>the
> two words at the head of every page, the choice that is not offered drawn beside the one that is and
> what it waits on stated here</td></tr>
Why: 9.4 states the lane on the surface, so 15.15 gains a row for it and the claim is placed there.
The outstanding row was reworded because a cell that enumerates three parts owes a verdict for each,
and what it was enumerating was one thing said twice: oldest first is the order the worker takes them
in.

### 2026-09-20 - ARCHITECTURE.html - the read and write matrix gains a research requests column

Authorised by: A request the page writes and the worker drains is what starts a pass, and the read surface writes the ask and never the research
Was:
> the header ended `<th>Series<br>state</th><th>Run log</th>`, and each of the thirty-four rows carried
> a cell for every column up to the run log and none between series state and it
Now:
> the header reads `<th>Series<br>state</th><th>Research<br>requests</th><th>Run log</th>`, and every row
> carries a cell in the new column: the read API reads and writes it, the request drain reads and writes
> it, and the other thirty-two are blank
Why: a store with no column is one the matrix asserts nothing about, in either direction, and the check
reads a blank cell as a claim as much as a filled one. The column sits before the run log rather than
after it because the run log is the last column by convention and every row's last cell is read as its
run log cell.

### 2026-09-20 - ARCHITECTURE.html - the read API starts no process, and the request drain is a component of its own

Authorised by: A request the page writes and the worker drains is what starts a pass, and the read surface writes the ask and never the research
Was:
> <td>every store</td><td>run log</td><td>read-only access for the app; performs no computation and no
> fetching. The name page's control is the one thing it starts, the worker's <code>research</code> verb for
> that name as a process of its own, and it writes nothing that pass writes (see: The name page's control
> starts the worker's research verb, and the read API writes nothing it starts)</td>
Now:
> <td>every store</td><td>run log, research requests</td><td>read-only access for the app; performs no
> computation and no fetching, and starts no process at all. A press asking for a report writes a request
> and a press on the queue screen takes back one nobody has started, which are the two writes it makes and
> the only table it writes; the research the request leads to is the worker's (see: A request the page
> writes and the worker drains is what starts a pass, and the read surface writes the ask and never the
> research)</td>
Why: the surface started the worker's verb as a process of its own until 9.2, which made the read layer a
thing that runs programs and left a press with nowhere to wait. It writes a request instead, and a
catalogue row for the request drain is added above the read API's, being the worker's half of the same
table: it takes the oldest request nobody has started and settles it under what the pass's own run says
it came to rather than under whether the verb ran.

### 2026-09-20 - RUNBOOK.md - the queue is what starts a pass, and the drain verb is shown

Authorised by: A request the page writes and the worker drains is what starts a pass, and the read surface writes the ask and never the research
Was:
> A pass writes one name's research: the sections not yet written, the ones gone stale, and the ones left
> out on an earlier day. The name page's control starts it, and so does this, from the repository root,
> which is all the control does:
>
> **What the control does.** It sends the press with a header of the page's own, and the read surface
> refuses a request without one, so another site's page open in a browser on this machine cannot start a
> pass (see: A pass is started only by a request carrying the name page's own header). The surface refuses
> a name the index does not hold, then starts the command above from the checkout it runs in, telling the
> worker the data root it reads so the pass writes the store the page shows, and returns at once. It
> writes nothing itself: the page shows what the pass wrote when it is opened again.
Now:
> the first passage names the queue's drain as what runs a pass, and the second says the surface writes a
> request and starts no process of its own. A section, *Draining the queue*, is added after it, showing
> `dotnet run --project src/EquityBrief.Worker -- drain` and what the drain settles a request under.
Why: a runbook that tells the operator the control starts the verb describes a mechanism that is no
longer there, and the one verb that now writes a queued report had no command line anywhere in it.

### 2026-09-20 - ARCHITECTURE.html - 15.1 names the five screens and where each sits

Authorised by: The report generation the operator asked for is built as phase 9, and phase 8's sign-off is owed after it rather than before it
Was:
> <p>A full report for one name is a long document. It is read in the evening by one person deciding which of a handful of names is worth the next half hour, and the sections that answer that question sit among sections that answer different ones. Every screen below replaces reading with looking. None of them replaces the prose: the researched sections are kept in full, one disclosure down, because the screen answers <b>which name</b> and the prose answers <b>why</b>.</p>
Now:
> <p>A full report for one name is a long document. It is read in the evening by one person deciding which of a handful of names is worth the next half hour, and the sections that answer that question sit among sections that answer different ones. Every screen below replaces reading with looking. None of them replaces the prose: the researched sections are kept in full, one disclosure down, because the screen answers <b>which name</b> and the prose answers <b>why</b>. There are five: tonight at 15.7, the universe at 15.8, a name at 15.9, the run at 15.10 and the queue at 15.15. The queue is numbered after the sections that follow the other four rather than among them, because the numbers here are navigation and the record already cites 15.11 to 15.14.</p>
Why: a fifth screen is added at 15.15 rather than at 15.11, because the numbers in this document are
navigation and the record already cites 15.11 to 15.14, so inserting one would repoint every entry
that named them with nothing going red. The screens are then not contiguous, so the section that says
what the screens are for names them and where each sits, and a reader finds the fifth from a list
rather than from the numbering. Section 15.7 also gains what a row says about research and what it can
ask for, and 15.15 is written whole; both are additions and neither replaces text.

### 2026-09-20 - BUILD_PLAN.md - phase 9's checkpoints planned at 9.0, from four to five

Authorised by: The report generation the operator asked for is built as phase 9, and phase 8's sign-off is owed after it rather than before it
Was:
> **Done when** section 15.7's regions name what a row says about research and what it can ask for, the request store is declared in `SCHEMA.md` with its one writer per operation, and every decision the three checkpoints below cite resolves.

> ### 9.2 The request queue
A store table the page writes a request to and the worker drains, replacing the process started beside the read surface.

**Done when** a press writes one request row and starts no process, asserted over the shipped source and over the hosted route; the worker drains the queue oldest first and writes one run per request under that request's own identifier; a second request for a name whose request is outstanding is refused and the row says so; a request for a name whose pass already wrote something that day is refused, counted over passes that wrote rather than over passes that ran; the table is declared in `SCHEMA.md` and passes `writer-ownership` in both directions; and a request whose worker never ran still reads as outstanding rather than as lost, asserted over a store where the drain did not run.
Now:
> 9.0's done condition names sections 15.1, 15.7 and 15.15 and the placement of the claims they add,
> and says that the request store and the decision the queue supersedes are 9.2's to write and not
> its own. 9.2 states that both surfaces write the same request. 9.3 is the queue screen and taking a
> report out of it. The lane checkpoint moves to 9.4.
Why: the ruling opened the phase with a first sketch of its checkpoints and 9.0 is the pass that plans
it. The operator settled two questions the ruling left open on the same day: queueing happens from
tonight's list, and the control 6.5 made reachable on every name page stays and writes the same
request, so nothing starts a process any more and the queue is the only mechanism; and the queue is
read on a screen of its own in the masthead, from which a report that has not been written yet is
withdrawn. That is a fifth checkpoint. The store declaration moved to 9.2 because `writer-ownership`
reconciles against the code and refuses a writer declared before its component exists, which this
corpus has been bitten by at 4.0 and at 5.0, and the decision the queue supersedes moved with it
because a decision retired while the code it describes is still running would fail
`no-superseded-citation` on six live citations.

### 2026-09-20 - CLAUDE.md - the operator may rule a phase's plan open before the previous phase's sign-off

Authorised by: The report generation the operator asked for is built as phase 9, and phase 8's sign-off is owed after it rather than before it
Was:
> Sign-off is a separate activity with its own record, owed on the phase as a whole before the next phase's plan, and it does not gate the merge. A phase held open waiting on something that is not code keeps a branch open, and the nightly job runs from that checkout for the whole of it.
Now:
> Sign-off is a separate activity with its own record, owed on the phase as a whole before the next phase's plan, and it does not gate the merge. The operator may rule that order reversed for one phase, and the ruling says what it defers and why: the sign-off stays owed, its entry names it as outstanding, and no checkpoint of the later phase discharges it (see: The report generation the operator asked for is built as phase 9, and phase 8's sign-off is owed after it rather than before it). The order is the only thing such a ruling moves, and a session that has committed code still may not sign that code off. A phase held open waiting on something that is not code keeps a branch open, and the nightly job runs from that checkout for the whole of it.
Why: the clause was written against a build where each phase closed before the next opened, and it read as refusing an ordering the operator wanted rather than as protecting what the sign-off is for. What it protects is that a session does not review its own code, and that is untouched and restated here: the sign-off stays owed, nothing phase 9 does discharges it, and the fresh-session rule stands. The exception is written into the rule rather than left as a ruling standing against text that still reads as forbidding it.

### 2026-09-20 - ARCHITECTURE.html - a reason's values are shown under the pointer rather than on a click

Corrects: the row was rewritten earlier the same day to say the values are drawn where the reason is opened, which made reading a row a click per reason and a second click to put it away. The operator asked for the values under the pointer, going as the pointer leaves.
Was:
> <tr><td>Reasons, per row</td><td>each reason named, with its measured record beside it under 15.11, and the values that made it true drawn where the reason is opened</td></tr>
Now:
> <tr><td>Reasons, per row</td><td>each reason named, with its measured record beside it under 15.11, and the values that made it true drawn beside it while it is under the pointer or holds focus</td></tr>
Why: scanning twenty rows of six reasons is the thing this surface is for, and a disclosure that has to be opened and closed is a worse instrument for it than a panel that follows the pointer. What the earlier change was actually for stands: the values are drawn markup rather than a title attribute, so they are on a surface that can be read back and one a touch screen can reach. Focus is `focus-visible` rather than `focus`, so a keyboard reaches the panel and a click does not pin it open.

### 2026-09-20 - .claude/rules/checks.md - `read-surface` reads a reason's values off its panel and out of its attributes

Corrects: the clause was written the same day against a disclosure that opened on a click, and named that element. It also asserted only that the values are drawn, not that they are drawn nowhere else, so the attribute they used to live in could have come back beside them.
Was:
> ... and each reason on tonight's list opens on the values the night measured it over, read back off that reason's own disclosure against the store rather than off the page as a whole, a reason the store holds no values for saying so, with a row's record read inside its reason and the column's inside the footer's, counted apart because one is the other's prefix |
Now:
> ... and each reason on tonight's list draws the values the night measured it over, read back off that reason's own panel against the store rather than off the page as a whole and asserted to sit in no attribute of it, a reason the store holds no values for saying so, with a row's record read inside its reason and the column's inside the footer's, counted apart because one is the other's prefix |
Why: a second copy in an attribute is a copy that drifts from the drawn one, and nothing would have said which a reader saw. The assertion is scoped to the reason's own cell, because the column heads carry a title that spells out the word a column is headed by, which is not a value the night measured.

### 2026-09-20 - ARCHITECTURE.html - a reason on tonight's list opens on what it was measured over

Corrects: the row said the values that made a reason true were shown on hover. They were in a title attribute, which is a native tooltip: delayed, unreachable on a touch screen, and never opened by a click. The operator clicked a reason and got nothing.
Was:
> <tr><td>Reasons, per row</td><td>each reason named, with its measured record beside it under 15.11, and the values that made it true on hover</td></tr>
Now:
> <tr><td>Reasons, per row</td><td>each reason named, with its measured record beside it under 15.11, and the values that made it true drawn where the reason is opened</td></tr>
Why: a claim that something is shown is a claim about a surface, and a tooltip was the weakest surface available for values that answer why this name is on the list. The values are drawn now, so the page states them and the surface can read them back.

### 2026-09-20 - .claude/rules/checks.md - `read-surface` reads a reason's values off its own disclosure

Corrects: the row reached the record beside a reason and never the values the reason was measured over, so the values could sit in an attribute nobody could open and every assertion stayed green. The one assertion that read them searched the whole page, which a value drawn under the wrong reason would have satisfied.
Was:
> ... with every section of a pass read back off the page for its own paragraphs joining into the prose the store holds |
Now:
> ... with every section of a pass read back off the page for its own paragraphs joining into the prose the store holds; and each reason on tonight's list opens on the values the night measured it over, read back off that reason's own disclosure against the store rather than off the page as a whole, a reason the store holds no values for saying so, with a row's record read inside its reason and the column's inside the footer's, counted apart because one is the other's prefix |
Why: reading each reason's values off its own element is what tells a value drawn under the wrong reason from one drawn under the right one. The record halves are counted apart because `record` is a prefix of `record-foot`, and the single matcher that counted both stayed green while only half of them were nested.

### 2026-09-20 - ARCHITECTURE.html - section 15.9 draws the risks one part to a risk

Corrects: the section holding the risks was drawn as one run of prose, which for the newest pass on the operator's machine is ten risks and ten confirmations in a single paragraph, so finding the fourth risk meant reading the first three. The operator asked for the section as a list.
Was:
> (no row: the sections row covered them as prose carrying its own date)
Now:
> <tr><td>The risks as parts</td><td>one part per risk where the prose says where each part ends, with what would confirm a risk set beneath the risk it confirms (see: A written section is broken into parts only where its own prose says where each part ends)</td></tr>
Why: every sample the repository can reach was measured and they are written in three shapes rather than one, so the row claims what the prose itself states rather than a shape the page hoped for. The parts are the prose cut and never edited, which is what the surface asserts over every section of a pass.

### 2026-09-20 - .claude/rules/checks.md - `read-surface` reads the risks as the parts the prose states

Authorised by: A written section is broken into parts only where its own prose says where each part ends
Was:
> ... and as the prose was written where it was not |
Now:
> ... and as the prose was written where it was not; and the risks are drawn one part to a risk where the prose says where each part ends, what would confirm a risk set beneath the risk it confirms and whatever stands before the first part opening that part, and as the prose was written where it says nowhere, with every section of a pass read back off the page for its own paragraphs joining into the prose the store holds |
Why: a cut in the wrong place would read as correct to every assertion the row held, so the property that makes it a failure is that the parts joined back up are the prose as it was stored, and the row's existing per-section reader was widened to carry it.

### 2026-09-20 - ARCHITECTURE.html - section 15.9 draws the two cases as two labelled halves

Corrects: the section holding the case for a name and the case against it was drawn as one run of prose, so a reader looking for the case against had to find where the case for stopped. The operator asked for the two to be laid out and labelled separately.
Was:
> (no row: the sections row covered them as prose carrying its own date)
Now:
> <tr><td>The case for and the case against</td><td>each case under its own label where the two cases were answered in the shape the writer was asked for and as the prose was written where they were not</td></tr>
Why: the writer is asked for two paragraphs and every answer recorded over the fixture and every section stored on the operator's machine is that shape, so the labels rest on a property three independent samples carry. The fallback is the point of the second clause: a section is the words the checker accepted, and a shape the page hoped for is no reason to draw any of it differently.

### 2026-09-20 - .claude/rules/checks.md - `read-surface` reads the two cases as two halves

Corrects: the row reached what a written section states and the date it carries, and nothing about the shape it is drawn in. A page labelling the wrong half, or dropping one, would have read as correct to every assertion here.
Was:
> ... a close inside a band stating no distance rather than the gap to one of its sides |
Now:
> ... a close inside a band stating no distance rather than the gap to one of its sides; and the two cases are drawn as two labelled halves where the writer answered in the shape it was asked for, each half one of the section's own paragraphs unchanged and in the order it was written, and as the prose was written where it was not |
Why: the halves are read back against the stored prose by a query of the test's own, so a label put over the prose rather than beside it fails, and the unrecognised shape is asserted to draw no half at all.

### 2026-09-20 - ARCHITECTURE.html - section 15.9 states how far each band sits from the close

Corrects: the level summary named each band, its role, a strength and its members, and never said how far away any of it was. Tonight's list has stated distances in typical days' moves since 5.4 and a name's own page, which is where a band is acted on, did not.
Was:
> (no row: the chart row ended at the level summary table)
Now:
> <tr><td>How far each band is</td><td>on every row of the level summary: the gap from tonight's close to the nearer edge of that band, counted in the moves the name usually makes in a session (see: Distances are stated as typical days' moves)</td></tr>
Why: a band twelve points away means one thing on a name that moves two points a session and another on a name that moves six, and the page gave the reader the subtraction to do. The measure already existed for the list; this is the same one, moved to one place both screens read.

### 2026-09-20 - .claude/rules/checks.md - `read-surface` reads how far each band is from the close

Authorised by: Distances are stated as typical days' moves
Was:
> ... numbered contiguously from where a reader starts and standing above the first region it names |
Now:
> ... numbered contiguously from where a reader starts and standing above the first region it names; and each of a name's bands states how far it sits from the stored close, counted over that name's own newest typical move and read back off the markup against a computation of the test's own, a close inside a band stating no distance rather than the gap to one of its sides |
Why: the distance is arithmetic over two stored values, so it is read back against a computation of the test's own rather than against the page's, and the case a reader meets most, a close sitting inside its immediate band, is the one a gap-to-an-edge rule gets wrong.

### 2026-09-20 - ARCHITECTURE.html - section 15.9 gains a contents at the head of a name's page

Corrects: the page draws sixteen regions on a name holding every section and offered no way to reach one but scrolling. The operator read a report of their own with a contents at its head, asked for the same here, and there was nothing in section 15 the page could be held to.
Was:
> (no row: the table opened on the suspect-prices region)
Now:
> <tr><td>Contents</td><td>at the head of the page: a numbered link to each region the page drew and to no other, in the order the page drew them</td></tr>
Why: a reader who cannot reach a section is a reader who does not read it, and a page this long needs a way in. The row is written as what the page drew rather than as a list of sections so the two cannot come apart: the check reads it in both directions.

### 2026-09-20 - .claude/rules/checks.md - `read-surface` reads the contents against the regions the page drew

Corrects: the row reached what a page draws, at what size, and what each region says, and nothing held the page to being navigable. A contents naming a region the page does not draw, or a region no entry reaches, would have read as correct to every assertion here.
Was:
> ... and every part of the page states the date it is as of on the card that holds it, with no region of its own restating them |
Now:
> ... and every part of the page states the date it is as of on the card that holds it, with no region of its own restating them; and a name's page opens with a contents read against the regions it drew in both directions, so neither a region nobody can reach nor an entry pointing at nothing passes, numbered contiguously from where a reader starts and standing above the first region it names |
Why: the contents is built from what was drawn rather than from a roster kept beside the page, and the assertion is what keeps that true as regions are added and removed.

### 2026-09-20 - .claude/rules/checks.md - `read-surface` reads what a refusal says and where a date is stated

Authorised by: A refused draft's own words are kept on the row and drawn on the evidence page, and never on the name page
Was:
> ... the panel beneath as wide as the chart's own plot and the profile beside it carrying that row's height, so a session is at one distance across the two and a price at one height |
Now:
> ... the panel beneath as wide as the chart's own plot and the profile beside it carrying that row's height, so a session is at one distance across the two and a price at one height; and a section a checker refused twice is named on the page by every rule that refused it, with what the checker extracted beside each rule and never the draft's own sentence, repeats collected under their rule and a reason carrying no second refusal drawn as it was stored, which rules carry a sentence being read off the shipped checker's own findings rather than listed beside the check, and the line read back off the page as well as off the reader, so the two fail apart; and every part of the page states the date it is as of on the card that holds it, with no region of its own restating them |
Why: the row reached what a page draws and at what size, and said nothing about what the words of a refusal are for. A line that named the rule and then quoted the paragraph the checker threw away read as correct to every assertion here, because it was the whole reason drawn. The second clause is what the provenance footer's row used to carry, moved to the cards that hold the dates.

### 2026-09-20 - ARCHITECTURE.html - section 15.9 loses the provenance footer and gains the sections left out

Authorised by: A refused draft's own words are kept on the row and drawn on the evidence page, and never on the name page
Was:
> <tr><td>Provenance footer</td><td>for every part of the page: computed tonight, fundamentals as of a filing date, research as of the date it was written</td></tr>
Now:
> <tr><td>Sections left out</td><td>one line per section the checker left out or the newest pass did not write, each naming what refused it and never the words of the draft that was refused (see: A refused draft's own words are kept on the row and drawn on the evidence page, and never on the name page)</td></tr>
Why: every line of the footer restated a date the card it named already carried: the night on each computed card's stamp, the filing on the numbers card's spine, and the day each written section states beneath its own prose. The promise it served is held there, so the region was a second copy. In its place the table claims what it had never claimed, being the lines that say which sections were left out and why, which is where a refused draft's own sentences were being drawn.

### 2026-09-19 - .claude/rules/checks.md - `read-surface` reads what a picture is drawn at and what is written over it

Corrects: the row stated that a screen is read at the width of the screen it is read on and that the column's ceiling is the widest picture the name page draws, and said nothing about the size a picture is drawn at. Two were drawn at the width of whatever held them, so each was scaled up to fill its card and the type inside it grew with it, and the chart wrote every band's name and every average's name inside its own plot, over the prices they were about.
Was:
> ... with the page's own watching read off the shell's script, the suite having no browser to run it in |
Now:
> ... with the page's own watching read off the shell's script, the suite having no browser to run it in; and no picture the five surfaces draw is stretched to the width of what holds it, each named where one is, with nothing written inside the chart's plot, the averages and the two hues named in a row above it and the nearest band's edges drawn in its hue in the column, the panel beneath as wide as the chart's own plot and the profile beside it carrying that row's height, so a session is at one distance across the two and a price at one height |
Why: the operator read the chart and could make nothing out on it. The words were written where the price is, and the check that reads this surface had no assertion about where a word may be drawn or how large a picture may be drawn.

### 2026-09-19 - .claude/rules/checks.md - `price-storage-form` gains a third half, over what a query does with a price

Authorised by: A stored price is chosen and ordered by its value and never by the text it is stored as
Was:
> The two halves fail apart: the storage half can hold while an expression casts money to a statistic inline, which is what a helper sitting in a project half the tree cannot reference produces |
Now:
> The two halves fail apart: the storage half can hold while an expression casts money to a statistic inline, which is what a helper sitting in a project half the tree cannot reference produces. A third half reads every query in the shipped source, taken from the strings the SQL is written as with the comments stripped first, and fails one that orders by, takes the least or the greatest or the total of, or compares a column SCHEMA marks as money, because the store compares a stored price character by character and adds one by reading it as a floating point number; the reader is shown to find each of the three forms, qualified by its table included, and to leave a date, a count and a column that merely ends in a price's name alone |
Why: the two halves governed the form a price is stored in and the crossings between the decimal world and the double one, and neither reached the third way the two worlds meet, which is the store being asked to compare or add a price it holds as text. Eight queries did, and four of them answer a reader.

### 2026-09-19 - ARCHITECTURE.html - section 15.12's second step says what opening a name does and does not do

Corrects: the step said the stored fundamentals are fetched when a page is opened and the numbers section fills in, which no screen does: section 15.2 says a screen fetches nothing, and the fetch is the research pass's first act.

Was:
> If the stored fundamentals predate the name's latest filing, they are fetched and the numbers section fills in.
Now:
> The numbers section renders from the filings the store holds. Opening a name fetches nothing: where a name holds none, the research pass fetches them before it writes and assembles that night's facts file again where it stored a filing the night had not seen.
Why: the step described a fetch on opening that the rule governing every screen forbids, and the page has never made one.

### 2026-09-19 - ARCHITECTURE.html - section 15.9 states what the page shows while a pass runs

Authorised by: A pass the page starts is watched until it ends and the page redraws as each section lands
Was:
> the table ran from Research paused to the provenance footer
Now:
> a row, A pass as it runs, for the line the page draws while a pass it started is running and the redraw as each section lands
Why: section 15.12's last step says progress is shown and sections stream in, and what a screen shows is stated as a row of its own table, which is what the harness reads as a claim.

### 2026-09-19 - .claude/rules/checks.md - read-surface asserts a pass watched as it runs

Authorised by: A pass the page starts is watched until it ends and the page redraws as each section lands
Was:
> the read-surface row ended at "a date it cannot read with tonight"
Now:
> "; and a pass the page started is watched by its own rows, the step named in the reader's words against the stages the worker writes, a pass with no row yet said to be starting and one carrying its own row said to have ended, read for the run the page was handed and never for an earlier pass, with the page's own watching read off the shell's script, the suite having no browser to run it in"
Why: the watching is drawn on the name page, and read-surface is the check that reads it.

### 2026-09-19 - ARCHITECTURE.html - section 15.3's routes name the one that carries a date

Authorised by: A name's page for an earlier night is what the store held that night
Was:
> the route list ran `#/researched`, `#/name/&lt;ticker&gt;`, `#/run/&lt;date&gt;`
Now:
> `#/researched`, `#/name/&lt;ticker&gt;`, `#/name/&lt;ticker&gt;/&lt;date&gt;`, `#/run/&lt;date&gt;`
Why: section 15.9 named both of the name screen's routes and 15.3's list, which is where the routes are enumerated, named one of them.

### 2026-09-19 - .claude/rules/checks.md - read-surface asserts a name's page for an earlier night

Authorised by: A name's page for an earlier night is what the store held that night
Was:
> the read-surface row ended at "named where one is not"
Now:
> "; and a name's page for an earlier night draws that night's figures and not tonight's, asserted against tonight's own page over the same store, says which evening it drew, offers no control, walks that evening's list, and answers a day the listings hold no evening on with the evening before it and a date it cannot read with tonight"
Why: the route draws a page, and read-surface is the check that reads the pages.

### 2026-09-19 - .claude/rules/checks.md - read-surface asserts that a screen fits the screen it is read on

Authorised by: A screen is read at the width of the screen it is read on
Was:
> the read-surface row ended at "and forms no rate for the name"
Now:
> "; and a screen is read at the width of the screen it is read on, the column's ceiling read off the widest picture the name page draws with the card's padding and the page's gutter around it rather than off a number kept beside the check, the column the screen's own width below it, the chart and the profile beside it each drawn at its own size with the rule that scales the pair together read, and every table the five surfaces draw read in a box of its own, named where one is not"
Why: the width a page is laid out at is a property of what the screens draw, and read-surface is the check that reads them.

### 2026-09-19 - ARCHITECTURE.html - section 15.9's listing history is a row of the page

Authorised by: A name's listing history states what followed each evening it was listed and forms no rate for the name
Was:
> <p>A tab on this screen carries the name's listing history: the listing strip over sixty sessions, then one row per evening it fired, with the close that night and what happened five and twenty-one sessions later. A row too recent to have matured says so rather than showing a blank or a zero.</p>
Now:
> <tr><td>Listing history</td><td>the listing strip over sixty sessions and one row per evening the name was listed, each with the reasons that fired and the close that night and what followed five and twenty-one sessions later beside the universe base rate or the words that it has not matured (see: A name's listing history states what followed each evening it was listed and forms no rate for the name)</td></tr>
Why: the listing history is drawn as a card after the plan, as every region of the page is, and a row of the table is what the harness reads as a claim.

### 2026-09-19 - .claude/rules/checks.md - read-surface asserts a name's listing history

Authorised by: A name's listing history states what followed each evening it was listed and forms no rate for the name
Was:
> the read-surface row ended at "a key written for another night replaced by its card naming that night"
Now:
> "; and a name's listing history states what followed each evening it was listed beside the base rate its row carries and forms no rate for the name" added at its end
Why: the name page draws the listing history, and read-surface is the check that reads the page.

### 2026-09-19 - ARCHITECTURE.html - section 4 puts the chart and the plan third and fourth

Corrects: section 4's numbered order put the company, its numbers and the two cases before the chart and the plan, where the approved screens draw the chart and the plan first
Was:
> sections 3 to 8 in the order what the company sells, the numbers, the industry cycle, the two cases, the chart, entries and exits
Now:
> sections 3 to 8 in the order the chart, entries and exits, what the company sells, the numbers, the industry cycle, the two cases, each row's words unchanged
Why: the operator chose on 2026-09-19 the approved screens' order, the chart and the plan straight after how the price got here, over section 4's.

### 2026-09-19 - ARCHITECTURE.html - section 4's key names the free sections by their new numbers

Corrects: section 4's numbered order put the company, its numbers and the two cases before the chart and the plan, where the approved screens draw the chart and the plan first
Was:
> It shows sections 2 without its cause column, 4, 7, 8 and 10,
Now:
> It shows sections 2 without its cause column, 3, 4, 6 and 10,
Why: the free sections were renumbered with the chart and the plan third and fourth.

### 2026-09-19 - ARCHITECTURE.html - section 15.9's short version is dated without its model

Authorised by: Every part of a page states where it came from and as of when, and a written section when it was written rather than which model wrote it
Was:
> <tr><td>The short version</td><td>the narrative verdict, with its own date and the model that wrote it beneath it</td></tr>
Now:
> <tr><td>The short version</td><td>the narrative verdict, with the date it was written beneath it</td></tr>
Why: the operator ruled that a page says when a section was written and not which model wrote it.

### 2026-09-19 - ARCHITECTURE.html - section 15.9's written sections carry their dates alone

Authorised by: Every part of a page states where it came from and as of when, and a written section when it was written rather than which model wrote it
Was:
> the researched and computed sections in the order section 4 specifies, each carrying its own date and model</td>
Now:
> the researched and computed sections in the order section 4 specifies, each carrying its own date</td>
Why: the operator ruled that a page says when a section was written and not which model wrote it.

### 2026-09-19 - ARCHITECTURE.html - section 15.9's provenance footer dates research without its model

Authorised by: Every part of a page states where it came from and as of when, and a written section when it was written rather than which model wrote it
Was:
> computed tonight, fundamentals as of a filing date, research as of a date and the model that wrote it</td>
Now:
> computed tonight, fundamentals as of a filing date, research as of the date it was written</td>
Why: the operator ruled that a page says when a section was written and not which model wrote it.

### 2026-09-19 - ARCHITECTURE.html - section 15.12's fourth step renders sections by their dates

Authorised by: Every part of a page states where it came from and as of when, and a written section when it was written rather than which model wrote it
Was:
> <li>Whatever sections are stored render with their own dates and the model that wrote each. A section drafted overnight by the local model says so, with the option to have the paid model rewrite it. Nothing is spent for any of this.</li>
Now:
> <li>Whatever sections are stored render with the date each was written. The page offers to have the paid model write again what the local model drafted. Nothing is spent for any of this.</li>
Why: the operator ruled that a page says when a section was written and not which model wrote it.

### 2026-09-19 - ARCHITECTURE.html - section 15's screen rule cites the provenance decision's successor

Authorised by: Every part of a page states where it came from and as of when, and a written section when it was written rather than which model wrote it
Was:
> states where it came from and as of when. (see: A screen reads and renders, and computes nothing) (see: Every part of a page states where it came from and as of when)</li>
Now:
> states where it came from and as of when. (see: A screen reads and renders, and computes nothing) (see: Every part of a page states where it came from and as of when, and a written section when it was written rather than which model wrote it)</li>
Why: the decision it cited is superseded.

### 2026-09-19 - ARCHITECTURE.html - section 18's local model row cites the queue decision's successor

Authorised by: The overnight queue writes the local lane's sections that rest on no document for every name, and the paid model is for names you get serious about
Was:
> the answer for each is the other lane (see: The overnight queue writes the local lane's sections that rest on no document, and the paid model is for names you get serious about)</td>
Now:
> the answer for each is the other lane (see: The overnight queue writes the local lane's sections that rest on no document for every name, and the paid model is for names you get serious about)</td>
Why: the decision it cited is superseded.

### 2026-09-19 - ARCHITECTURE.html - section 14's last step cites the queue decision's successor

Authorised by: The overnight queue writes the local lane's sections that rest on no document for every name, and the paid model is for names you get serious about
Was:
> and no part of the arithmetic above depends on it (see: The overnight queue writes the local lane's sections that rest on no document, and the paid model is for names you get serious about).</li>
Now:
> and no part of the arithmetic above depends on it (see: The overnight queue writes the local lane's sections that rest on no document for every name, and the paid model is for names you get serious about).</li>
Why: the decision it cited is superseded.

### 2026-09-19 - BUILD_PLAN.md - 6.10 cites the queue decision's successor

Authorised by: The overnight queue writes the local lane's sections that rest on no document for every name, and the paid model is for names you get serious about
Was:
> no part of the arithmetic depends on it (see: The overnight queue writes the local lane's sections that rest on no document, and the paid model is for names you get serious about).
Now:
> no part of the arithmetic depends on it (see: The overnight queue writes the local lane's sections that rest on no document for every name, and the paid model is for names you get serious about).
Why: the decision it cited is superseded.

### 2026-09-19 - RUNBOOK.md - the queue's paragraph cites the queue decision's successor

Authorised by: The overnight queue writes the local lane's sections that rest on no document for every name, and the paid model is for names you get serious about
Was:
> and the night fetches nothing for a name (see: The overnight queue writes the local lane's sections that rest on no document, and the paid model is for names you get serious about).
Now:
> and the night fetches nothing for a name (see: The overnight queue writes the local lane's sections that rest on no document for every name, and the paid model is for names you get serious about).
Why: the decision it cited is superseded.

### 2026-09-19 - .claude/rules/checks.md - banned-prose reads the architecture for a path

Corrects: the architecture named files of the repository by their paths and nothing read its words for one; the operator asked on 2026-09-19 for each to be named by what it is
Was:
> the banned-prose row ended at "matched on the sentence that states the rule"
Now:
> ". And the architecture's words, read as its reader sees them, name no file of the repository by its path or its file name, with the matcher shown to find each shape the document carried and to leave ordinary text alone" added at its end
Why: a defect the operator reports in the corpus lands with a check that refuses it again.

### 2026-09-19 - ARCHITECTURE.html - section 22's settled questions restored

Corrects: the 2026-09-19 removal of section 22's note and its table of settled questions, which went beyond the operator's request, that day, to remove the architecture's changelog sections
Was:
> <p class="note">No question is open.</p>
Now:
> the note and the table as they stood before that removal, with two changes: the table's heading read "Settled since the version above", and the version line it pointed at is gone, so it reads "Settled questions"; and its last row said the overnight queue writes free drafts for listed names, which the 6.10 correction of the same day made every name
Why: the operator asked for the changelog sections to go, and the settled questions are not one.

### 2026-09-19 - ARCHITECTURE.html - section 21 names the decisions record by what it is

Corrects: the architecture named a file of the repository by its path, which tells a reader who has not seen the tree nothing; the operator asked on 2026-09-19 for each to be named by what it is
Was:
> <p>The decisions and the superseded ones live in <code>docs/DECISIONS.md</code>, grouped by topic, and not here.
Now:
> <p>The decisions and the superseded ones live in the decisions record kept beside this document, grouped by topic, and not here.
Why: a reader of the design meets the name before the tree, if ever.

### 2026-09-19 - ARCHITECTURE.html - section 17's night cost row names the runbook by what it is

Corrects: the architecture named a file of the repository by its path, which tells a reader who has not seen the tree nothing; the operator asked on 2026-09-19 for each to be named by what it is
Was:
> `RUNBOOK.md` stated the allowance and every weight
Now:
> The runbook, the guide to operating the system kept beside this document, stated the allowance and every weight
Why: a reader of the design meets the name before the tree, if ever.

### 2026-09-19 - ARCHITECTURE.html - section 20's phase 0 row names the phase report by what it is

Corrects: the architecture named a file of the repository by its path, which tells a reader who has not seen the tree nothing; the operator asked on 2026-09-19 for each to be named by what it is
Was:
> <code>artifacts/phase-report.html</code> with every row
Now:
> the phase report's page with every row
Why: a reader of the design meets the name before the tree, if ever.

### 2026-09-19 - ARCHITECTURE.html - the harness's catalogue row names the phase report by what it is

Corrects: the architecture named a file of the repository by its path, which tells a reader who has not seen the tree nothing; the operator asked on 2026-09-19 for each to be named by what it is
Was:
> <td>artifacts/phase-report.html, artifacts/phase-report.json</td>
Now:
> <td>the phase report, as a page and as data</td>
Why: a reader of the design meets the name before the tree, if ever.

### 2026-09-19 - .claude/rules/checks.md - read-surface asserts the key drawn only beside its night

Authorised by: The key under each figure is dated by the night whose figures it explains, written for every name each night, and drawn only beside that night's figures
Was:
> the read-surface row ended at "and the names holding research are listed on their own route and found, with every other member, from the masthead's search"
Now:
> "; and the key under each figure is drawn only beside the night whose figures it explains, a key written for another night replaced by its card naming that night" added at its end
Why: the name page draws the key only beside the night it was written for, and read-surface is the check that reads the page.

### 2026-09-19 - ARCHITECTURE.html - the lane table writes the key for every name and dates it by its night

Authorised by: The key under each figure is dated by the night whose figures it explains, written for every name each night, and drawn only beside that night's figures
Was:
> is already computed and in the facts file, which is the night's, so the key is written for each night's facts file (see: The key under each figure is written for each night's facts file)</td>
Now:
> is already computed and in the facts file, which is the night's, so the key is written for each night's facts file, for every name, and dated by that night (see: The key under each figure is dated by the night whose figures it explains, written for every name each night, and drawn only beside that night's figures)</td>
Why: the key was written again each night for the listed names alone, so 78 of the 503 names on the operator's store drew an earlier night's key beside the night's figures, and the entry this cell cited is superseded.

### 2026-09-19 - ARCHITECTURE.html - the lane table hands the key its facts as a reader reads them

Authorised by: The key under each figure is handed its facts as a reader reads them, rounded by code
Was:
> <td>three to five sentences on what those values show, for a reader who has not seen the figure, with money rounded to millions or billions and margins written as percentages</td>
Now:
> <td>three to five sentences on what those values show, for a reader who has not seen the figure, copying each value as it is handed, named as a reader reads it and rounded by code, money in millions or billions and growth and margins as percentages (see: The key under each figure is handed its facts as a reader reads them, rounded by code)</td>
Why: asked to round the figures itself the local model cut digits off, 271.9963 written as 271.99, and otherwise copied six places onto the page.

### 2026-09-19 - ARCHITECTURE.html - section 14's last step runs the queue over every name

Authorised by: The key under each figure is dated by the night whose figures it explains, written for every name each night, and drawn only beside that night's figures
Was:
> writing the sections in the local lane that rest on no document for listed names whose research is missing or stale, in priority order, until the configured time limit
Now:
> writing the sections in the local lane that rest on no document for every name in the index whose research is missing or stale, the names on tonight's list first in order of reasons fired (see: The key under each figure is dated by the night whose figures it explains, written for every name each night, and drawn only beside that night's figures), until the configured time limit
Why: the queue reached the listed names alone, and 70 names that fired nothing on 2026-09-18 kept a key written for an earlier night.

### 2026-09-19 - ARCHITECTURE.html - section 15.8 says the queue writes the key for every name

Authorised by: The key under each figure is dated by the night whose figures it explains, written for every name each night, and drawn only beside that night's figures
Was:
> which the overnight queue writes for every listed name each night
Now:
> which the overnight queue writes for every name each night
Why: the queue now writes the key for every name in the index.

### 2026-09-19 - ARCHITECTURE.html - section 15.9 draws the key only beside the night it was written for

Authorised by: The key under each figure is dated by the night whose figures it explains, written for every name each night, and drawn only beside that night's figures
Was:
> section 15.9 said nothing of where the key under each figure is drawn or which night's it is
Now:
> <p>The key under each figure is drawn beneath the chart only where it was written for the night whose figures the page draws. A key written for another night is not drawn: its card names the night it was written for and says so, because a paragraph explaining another night's close beside these figures explains figures the page does not show (see: The key under each figure is dated by the night whose figures it explains, written for every name each night, and drawn only beside that night's figures).</p>
Why: a page drew a key written for an earlier night beside the night's figures, NVDA's quoting a close of 210.96 on a page whose close was 219.34.

### 2026-09-19 - ARCHITECTURE.html - section 17's overnight queue row runs the queue over every name

Authorised by: The key under each figure is dated by the night whose figures it explains, written for every name each night, and drawn only beside that night's figures
Was:
> for listed names whose research is missing or stale, in order of reasons fired, starting no pass once a configured number of hours has passed
Now:
> for every name in the index whose research is missing or stale, the names on tonight's list first in order of reasons fired, starting no pass once a configured number of hours has passed
Why: the queue reached the listed names alone, and the hour this row states was already set to cover every member of the index.

### 2026-09-19 - ARCHITECTURE.html - section 18 says what a page draws when the queue did not run

Authorised by: The key under each figure is dated by the night whose figures it explains, written for every name each night, and drawn only beside that night's figures
Was:
> and listed names open without a draft as normal</td>
Now:
> and each name's page names the earlier night its key under each figure was written for rather than drawing the key</td>
Why: a key written for an earlier night is no longer drawn beside the night's figures.

### 2026-09-19 - SCHEMA.md - research_section's as_of dates the key by its facts file's night

Authorised by: The key under each figure is dated by the night whose figures it explains, written for every name each night, and drawn only beside that night's figures
Was:
> | `as_of` | TEXT | date this section was written |
Now:
> | `as_of` | TEXT | date this section was written, and for the key under each figure the night of the facts file it was written from (see: The key under each figure is dated by the night whose figures it explains, written for every name each night, and drawn only beside that night's figures) |
Why: a key written in the day from the night before carried that day's date and read as the next night's key.

### 2026-09-19 - RUNBOOK.md - the schedule table's overnight queue row covers every name

Authorised by: The key under each figure is dated by the night whose figures it explains, written for every name each night, and drawn only beside that night's figures
Was:
> for listed names whose research is missing or stale, in priority order, starting no pass once the configured hours have passed
Now:
> for every name in the index whose research is missing or stale, the listed names first, starting no pass once the configured hours have passed
Why: the queue now writes the key for every name in the index.

### 2026-09-19 - RUNBOOK.md - what a pass costs, from the pass recorded again

Authorised by: The key under each figure is handed its facts as a reader reads them, rounded by code
Was:
> three on the local model and four through the spend cap for $0.0333,
Now:
> three on the local model and four through the spend cap for $0.0340,
Why: the fixture's pass was recorded again with the key handed its facts as a reader reads them, and the short version, written from the key, was refused once and written again, so the pass made eight paid calls rather than seven.

### 2026-09-19 - BUILD_PLAN.md - 6.10's queue covers every name

Authorised by: The key under each figure is dated by the night whose figures it explains, written for every name each night, and drawn only beside that night's figures
Was:
> for listed names whose research is missing or stale, in order of reasons fired, until the configured time limit
Now:
> for every name in the index whose research is missing or stale, the listed names first in order of reasons fired, until the configured time limit
Why: the 6.10 correction of 2026-09-19 widened the queue to every name, which this checkpoint's own statement of it now says.

### 2026-09-19 - CLAUDE.md - the timer rule cites the key's new decision

Authorised by: The key under each figure is dated by the night whose figures it explains, written for every name each night, and drawn only beside that night's figures
Was:
> The key under each figure, which explains the night's figures, is written for each night's facts file. (see: Nothing expires on a timer) (see: The key under each figure is written for each night's facts file)
Now:
> The key under each figure, which explains the night's figures, is written for every name for each night's facts file and drawn only beside the figures it explains. (see: Nothing expires on a timer) (see: The key under each figure is dated by the night whose figures it explains, written for every name each night, and drawn only beside that night's figures)
Why: the decision this rule cited is superseded.

### 2026-09-19 - ARCHITECTURE.html - section 15.2 states the places a figure is drawn at

Authorised by: A figure is drawn at the places it is read at, and its element carries the stored value whole
Was:
> section 15.2 ended at "It also means a screen can be rebuilt or restyled at any time without a chance of moving a number. (see: A screen reads and renders, and computes nothing)"
Now:
> a paragraph added: "A figure is drawn at the places it is read at: a price, a ratio and an amount per share to two places, an amount of money and a count of shares in its scale, a fraction as a percentage to one place and a multiple to one. The element it is drawn in carries the stored value whole, and that is what the checks read, so rounding a figure to be read never makes a value the store does not hold. Prose a model wrote is drawn as the claim checker accepted it."
Why: the screens drew stored values at their stored places, revenue as 96221000000.00 and a margin as 0.749753, and the rule every screen is held to now says how a figure is drawn.

### 2026-09-19 - ARCHITECTURE.html - the version history, the settled questions and the changelog section removed

Corrects: the architecture carried a version stamp, a table of questions already settled and a changelog section of its own, two places holding the history this file and the git log keep; the operator asked on 2026-09-19 for the changelog sections to be removed
Was, the title:
> <title>EquityBrief architecture v0.3 (5 September 2026)</title>
Was, the line above the heading:
>   <div class="sub">Architecture document, version 0.3, 5 September 2026. Replaces v0.2 of 4 September.</div>
Was, section 22 after its heading:
> <p class="note">No question is open. Every fork that stood here has been settled, and each is recorded below with how it settled rather than deleted. The six that stood here on 4 September were not questions: three were values with a stated way of settling them, which now sit in the limits table and the phase table; two were things already decided elsewhere in this document; and one was a recommendation written as a question. They are recorded in section 22 and the changelog rather than left here, because an open list that holds settled things makes the one real fork harder to see.</p>
> <h4>Settled since the version above</h4>
> <table>
>   <tr><th style="width:22%">Was asked</th><th>How it settled</th></tr>
>   <tr><td>Which of two hand-written reports was right about a moving average</td><td>Not a question once the specification moved inside this document. Every number in a report is computed from stored bars under the decision named Code owns every number, and fixture expectations are derived from the rules rather than from any report, so there is nothing left to reconcile.</td></tr>
>   <tr><td>The project's name</td><td>EquityBrief, confirmed 4 September.</td></tr>
>   <tr><td>Where transcripts come from</td><td>Nowhere that is bought. See the decision named Transcripts are opportunistic, never a dependency. The question should not have been written, because the source map already showed the press release carrying what the report needs.</td></tr>
>   <tr><td>The news-jump multiple</td><td>Set in the limits table: at least five articles in five sessions and at least three times the name's own trailing median.</td></tr>
>   <tr><td>The volume shelf threshold</td><td>Set in the limits table at twice an even share, with phase 3 widening the fixture to four names so the number is checked before anything depends on it.</td></tr>
>   <tr><td>A directional score</td><td>Settled 5 September in a narrower form than asked. Bucket-shaped conditions are registered and scored like any other candidate, so the data improves which reasons fire rather than producing a separate ranking. A reason's measured record is displayed beside the reason under section 15.5, which is the visible half of what was wanted. What is not built is a number beside each ticker, because separating small edges needs years of setups this list cannot produce and such a number would be read as a probability of making money.</td></tr>
>   <tr><td>Where theme material comes from</td><td>A search tool called inside the research loop, with a key already in hand and two trial searches run against it. See the decision named Theme material comes from a search tool, and per-name material never does, whose results are stored like any other document. This row named half of it until 6.0, and a name given in half is the paraphrase the citation rule exists to reject. It should not have stayed on this list after the key was set up.</td></tr>
>   <tr><td>Whether the watch list is pre-warmed</td><td>Settled twice. On 4 September the answer was no, because anticipation meant spending money on names that might not be opened. On 5 September that reasoning stopped applying, because research can now be written locally at no cost, so the overnight queue writes free drafts for listed names and the paid model stays strictly on demand. The superseded decision and its original reasoning are in section 22.</td></tr>
> </table>
Was, section 23 and its contents entry, "Changelog":
> <h2>23. Changelog</h2>
> <table>
>   <tr><th style="width:10%">Date</th><th style="width:8%">Version</th><th>Change, and what the previous text said</th></tr>
>   <tr><td>2026-09-02</td><td>0.1</td><td>First version, and a same-day reissue after the original was lost with its conversation. Reference output was the Micron report of 2 September; first fixture was fifty daily bars.</td></tr>
>   <tr><td>2026-09-05</td><td>0.3</td><td>Audited the vocabulary against actual use and repaired it. Six rows belonging to the limits table were found sitting in the vocabulary table, where they had four columns against vocabulary's two and rendered broken; they had landed there because both tables carry a row named Base rate and the inserts that added them were anchored on that text, which matched the vocabulary table first. They are now in the limits table. Take-or-pay was removed, since it appeared nowhere else in the document and described a fact about the reference report's company rather than anything this system does. Listing, setup, shadow and break-even were added, since between them they are used more than seventy times, almost all in section 13, and none was defined. Added section 15, which describes what the reader actually sees each evening, and replaced the previous section of the same number that described only the load order of one page. The document had specified components, stores, limits and phases without ever stating that the list draws at most twenty rows while naming the true count, or how the other four hundred and eighty names are reached; that had lived only in a superseded mockup file. Settled the directional score in a narrower form: bucket-shaped conditions go through the candidate register like anything else, so the data changes which reasons fire, and a reason's measured record is displayed beside the reason rather than a number being placed beside each ticker. Two corrections came with it. The listings store now holds a row for every name every night rather than only for listed names, without which a shadow candidate could never be evaluated on the nights it would have fired. And the minimum resolved setups were raised from 50 and 100 to 250 and 400: the earlier figures were set without doing the arithmetic and would have licensed verdicts on a tenth of the evidence such a verdict needs. Closed the last source question. Theme material comes from a search tool called inside the research loop, with the tool named and its query parameters stated as a limit; the question had stayed on the open list after the key was already in hand and two trial searches had been run against it, which is the second time an answer that existed in the conversation was left standing as a blocker. Repaired two things in the same section: a duplicated clause in its opening note, and a settled row still citing a ruling in section 19 that this pass had removed, which would have sent a reader to text that no longer exists. Moved the decisions and the previously-decided list out of this document into <code>docs/DECISIONS.md</code>, where they are the record and this document cites them by name. Keeping them here as well would be two documents holding one fact. A pass adding a citation at each rule that rests on a decision is carried in the build plan, because until it runs the check that asserts citations has one citation to assert. Rewrote section 15 as a screen specification rather than a load order, following the shape a sibling project settled on: a rule that governs every screen, a named mark vocabulary defined once, a colour section where a hue means one thing only, and then each screen stating what it answers, its route, what it reads and its regions in order. Five screens, seven marks, and a section stating what no screen does. Four decisions were added to the record with it, covering the reads-and-renders rule, marks shared between the application and the exported report, the two hues belonging to support and resistance alone, and a not-yet-measured figure drawn as a dashed outline rather than a pale value. The previous section named the views and their contents and said nothing about routes, marks, colour, the export surface, or what a screen may not do. Made the document self-contained. It had treated a hand-written report as the thing to be reproduced, which put the specification outside these pages and left the harness checking code against a document that broke two of the rules stated here. Section 4 is now the specification of the report itself. Section 19 was rewritten: the command-line invocation and its prose steps were replaced by three tables stating what a fixture holds, what the harness checks and which table each claim comes from, and what the three verdicts mean; fixture expectations are derived from the rules in sections 9 to 13 rather than copied from a report, so the two callouts recording that the report contradicted itself and broke the rules were removed as no longer describing anything. Every justification that rested on what a report happened to do was restated from first principles, and the figures that make rules legible are now cited as a worked example, defined in section 8.1, which illustrates rules and never defines them. Answered a question the document could not answer as written: where the plan comes from on the first night. It comes from the same night's arithmetic, since the ladder is recomputed from the chart every evening rather than authored in advance or carried forward, and section 1 now says so. Examining that exposed two defects in the ladder rule. A moving average was allowed to anchor a tranche, and a short average follows the price, so on the worked example's own bars the close sat within half a typical day's move of its 20-day average on seventeen of thirty-one sessions, meaning a tranche anchored there would make the at-entry-zone condition fire about half the time on nothing having happened; averages remain chart levels and no longer anchor tranches. And the rule said tranches sit on support bands below the price while the reference report's first tranche contains the price, so the rule now reads low edge below the price, with a straddling band keeping its full width. Two decisions, one limits row, one failure row and a fixture note were added. Renumbered the sections so that none carries a letter. The document had grown a section 7A and a section 11A, which existed only to avoid renumbering when they were inserted, and four consecutive sections whose titles all began with the same two words so that the contents list read as a column of "Opened up" with the subject pushed to the right; one of those four had no figure to open anything up with. Those four are now named for what they cover. Every cross-reference and figure number was moved with them. Added section 13, how the picks improve, after the observation that the document measured things without ever changing anything, and that every system built here is designed around a self-improvement loop. The score is the break-even each plan states for itself, since a forward return mostly measures market drift. Four levels of improvement are named with the phase that owns each, guardrails are written before any data exists, and figures 13.1 and 13.2 show the resolution path and the gates a candidate must pass. Section 1's opening sentence is corrected: it said the system answers which stocks are worth looking at tonight, which implies a claim about what they will do; it now says which stocks have reached a price where the plan already says something. Phase 4 gains an obligation to store each listing's plan, without which the whole of section 13 becomes impossible to add later. Six decisions and four limits rows added, plus two failure rows. Corrected the same day: the candidate register was described as a committed file, which put one fact outside the store for the sake of tamper-evidence that an append-only table provides just as well, and which matched neither the rest of the design nor this document's own convention that a record corrects itself with a new dated entry. It is now a table accepting no update and no delete. Two decisions were added so the design works on the machine it is being built on rather than only on a future one: the local lane's scope is a setting the overnight queue inherits, so on 24GB the queue writes classification and extraction while synthesis stays paid and on demand; and a research record is written and dated per section rather than as a whole, since some sections are now drafted overnight and others written days later, which a single record-level date and model name would misreport. The decision that nothing is pre-warmed was superseded and moved to section 22, after the operator specified a machine with enough memory to run a large model locally: the objection had been to spending money in anticipation, and local work in anticipation is free. In its place the local model writes a free overnight draft for listed names whose research is missing or stale, bounded by wall clock rather than by a count of names, with the paid model reserved for on-demand reading and for rewriting a draft that is not good enough. Three decisions on where the system runs were added at the same time, since the store being one database file is what makes an installation portable, and the two things that would break portability if left implicit are hardcoded paths and self-scheduling.</td></tr>
>   <tr><td>2026-09-04</td><td>0.2</td><td>Rebuilt against a different reference report and eight rulings. The reference is now the Micron report written from the 1 September close, which adds a volume-at-price histogram, a day-by-day table of the largest moves, a second book for the earnings trade, behavioural tranche conditions, fractional exits with a trailing rule, non-price exit triggers, and a plain-language key under every figure. The universe is the S&amp;P 500 where v0.1 had a hand-picked watch list. The provider is named where v0.1 left it open, and hand-entered fundamentals are superseded. Storage is one year where v0.1 proposed five. The level window is sixty sessions where v0.1 said two hundred and fifty. Rendering moved from a nightly file per report to a single-page application. Research moved from nightly narration to on demand with permanent dated records. The fixture is diffed on the facts file where v0.1 diffed a rendered page. The ladder's stop rule is now stated as the low edge of the next band down and is trend-dependent; v0.1's rule said the next band beneath the tranche with one stop for all tranches, and it did not reproduce its own reference. Components no longer carry code numbers. Amended the same day to name the one-year first-run backfill as a step, a limit and a phase-1 done condition; it had been mentioned only as something a newly joining name receives, which left the first run itself unspecified. Amended again the same day to settle six of the seven open questions. The name is EquityBrief. Transcripts are read from the filings archive where a company files one and no provider is bought for them, replacing the previous text which said they were unavailable from either source and needed a further provider. Nothing is pre-warmed. The news-jump and volume-shelf thresholds moved from proposals in the open list into the limits table with their checks named. The 200-day conflict returned to section 19, where it was already ruled. Six of those seven had answers that existed elsewhere in this document or in the conversation that produced it, and listing them as open made settled things look like blockers. Amended 5 September to name DeepSeek V4 as the research model, replacing the previous text which said only that a cloud model with web search does research. Three consequences followed. The model cannot search, so components fetch documents and hand them over, which is now a decision in its own right and which leaves theme material without a settled source. The spend cap is denominated in money rather than tokens, because peak rates are double. And queued work is scheduled off-peak in UTC. Amended again on 5 September after trial searches showed that a bare provenance rule is not enough: a stored source is now also tested for admissibility before it is stored, with denied categories, a publish-date requirement and a preference for primary sources. Corrected the same day to state that admissibility is judged per document after the fetch and never per publisher, since a search tool's domain filter cannot express a rule about article kinds, and to add the case of a source that refuses automated access. Extended the same day with three decisions covering gated sources, the split into a company-news list and an industry list each with a review date, and the rule that a publisher list is a noise filter rather than a correctness test; with a phase 1 coverage measurement across a market-capitalisation-spread sample, replacing the assumption that a large-company index guarantees coverage; and with a note in section 19 recording that the reference report itself breaks the computed-numbers rule and the stored-source rule, so the fixture takes what the rules produce rather than what the report says. Extended again with the decision that a research pass is split by section difficulty across the local and paid models, figure 12.2 showing that split, and a phase 5 expectation that the boundary is measured against the reference rather than asserted. Added section 8 with figure 8.1, after the three builders were read as possibly per-name-on-demand rather than nightly across the whole index; it states plainly that all three run for every name every night, walks one stock through all three stages with the reference report's own numbers, and says why the chain is three components rather than one.</td></tr>
> </table>
Was, the footer:
>   <p>EquityBrief architecture v0.3, 5 September 2026. Nothing in this document has been read by a build session and nothing is built. Written for the operator and for readers outside the domain; section 3 defines every specialist term used.</p>
Now:
> the title reads "EquityBrief architecture", the line above the heading is gone, section 22 reads "No question is open.", section 23 and its contents entry are gone, and the footer reads "Written for the operator and for readers outside the domain; section 3 defines every specialist term used."
Why: the document is the specification of the system as it stands, and a version stamp, a table of questions already settled and a changelog are history, which this file and the git log already keep. The footer's "nothing is built" had been false since phase 0.

### 2026-09-19 - ARCHITECTURE.html - section 15.3 names the researched route

Authorised by: A researched name is one holding an accepted section besides the key under each figure
Was:
> Views are hash routes resolved in the browser: <code>#/</code>, <code>#/night/&lt;date&gt;</code>, <code>#/universe</code>, <code>#/name/&lt;ticker&gt;</code>, <code>#/run/&lt;date&gt;</code>.
Now:
> Views are hash routes resolved in the browser: <code>#/</code>, <code>#/night/&lt;date&gt;</code>, <code>#/universe</code>, <code>#/researched</code>, <code>#/name/&lt;ticker&gt;</code>, <code>#/run/&lt;date&gt;</code>.
Why: the operator could not find a researched name without knowing its ticker, and the names holding research now have a route of their own.

### 2026-09-19 - ARCHITECTURE.html - section 15.8 reads the researched sections and lists the names holding them

Authorised by: A researched name is one holding an accepted section besides the key under each figure
Was:
> <p><b>Reads:</b> the listings, levels and indicators for the night, and membership.</p>

and the region table ended at its Filters row, with no paragraph after the note.
Now:
> <p><b>Reads:</b> the listings, levels and indicators for the night, membership, and the researched sections.</p>

with a row after Filters, "Researched: every name holding a researched section with the day its newest one was written, on <code>#/researched</code> and linked from the masthead", and a paragraph after the note stating which sections make a name researched.
Why: the list is the screen the operator asked for, and a researched name is defined where the region is so the key the queue writes nightly is not counted.

### 2026-09-19 - ARCHITECTURE.html - section 15.13 finds any name from the masthead

Authorised by: A researched name is one holding an accepted section besides the key under each figure
Was:
> the list ended at "An unknown route resolves to tonight with a line saying what was asked for, rather than to a blank page."
Now:
> a bullet added: "Any name is found from a search box in the masthead of every screen, by its ticker or its company's name, and a name found is opened on its own route. Text matching no name says so rather than opening an empty page."
Why: the operator asked for a search on the first page, and the masthead is on every screen.

### 2026-09-19 - .claude/rules/checks.md - `read-surface` asserts figures drawn to be read and the researched names

Authorised by: A figure is drawn at the places it is read at, and its element carries the stored value whole
Was:
> ...and the shell remembers the palette, defaults to the machine's and resolves an unknown route to tonight with a line naming it |
Now:
> ...and the shell remembers the palette, defaults to the machine's and resolves an unknown route to tonight with a line naming it; every figure a reader sees on the name page is drawn at the places it is read at with the stored value whole on its element, and the names holding research are listed on their own route and found, with every other member, from the masthead's search |
Why: the check gained the assertions this correction adds, and the roster states what each check asserts.

### 2026-09-19 - .claude/rules/checks.md - `read-surface` asserts the screens' design

Authorised by: Every region is a card that states where its figures came from and how to read them
Was:
> | `read-surface` | every CI run | The read API hands back every stored value unchanged, and the page draws one candle and one volume bar per stored session, matched session by session against the store |
Now:
> | `read-surface` | every CI run | The read API hands back every stored value unchanged, and the page draws one candle and one volume bar per stored session, matched session by session against the store; every screen is laid out in cards whose keys close on what to take from a figure, each of the seven marks is asserted over a full input and over the input it degrades on with the words it says in place of what it cannot draw, the name page opens with what it is for, its three refusals and its ten words, tonight's reasons stand in six columns in their set order, and the shell remembers the palette, defaults to the machine's and resolves an unknown route to tonight with a line naming it |
Why: the check carries the tests of the screens drawn to the approved design, and a roster row not saying what its check asserts is a property nobody wrote down.

### 2026-09-19 - ARCHITECTURE.html - section 15.6 states that every region is a card

Authorised by: Every region is a card that states where its figures came from and how to read them
Was:
> (no such paragraph)
Now:
> <h4>Every region is a card</h4>
> <p>Each region is a card on one paper palette. A region computed from the nightly store is ruled in slate and stamped with the night its figures are from, and a section written by research or taken from a filing is ruled in plum-grey with the day it was written or filed in a column at its left. Every picture carries a key that says how to read it and closes on what to take from it. The stylesheet the app and the exported report share opens with the rule each colour token is held to: the two hues for a level below and above the price, the dashed outline's ink for not yet measured and for nothing else, slate for what was computed tonight, plum-grey for what was written or filed, and four steps of one neutral for more or less. (see: Every region is a card that states where its figures came from and how to read them)</p>
> 
> <h3>15.7 Tonight</h3>
Why: the approved design lays every region out as a card whose rule and stamp say where its figures came from, and section 15.6 is where the colour of each is stated.

### 2026-09-19 - ARCHITECTURE.html - section 15.7's reason totals are counts

Authorised by: Tonight's reason totals are counts, and a reason's record is the run page's
Was:
> <tr><td>Reason totals</td><td>the reason track mark across tonight's fired names, which says whether the evening is one thing happening to many names or many things happening to a few</td></tr>
Now:
> <tr><td>Reason totals</td><td>each reason's count of tonight's fired names drawn as a bar out of the fired count with the count on it, which says whether the evening is one thing happening to many names or many things happening to a few</td></tr>
Why: a setup listed tonight has no outcome, so a track of outcomes drew one dashed segment per reason; the approved design draws the counts.

### 2026-09-19 - ARCHITECTURE.html - section 15.7 states the list's columns, its order and what selecting a row does

Authorised by: Selecting a row draws its plan beneath the list and is no navigation
Was:
> (no such note)
Now:
> <p class="note">The list draws each reason in a column of its own, in a set order, headed by one word with the reason's full name on it, so an evening that is one thing happening to many names reads as one stripe down one column. Each reason's record sits once at the foot of its column and inside the reason on each row, and never beside a name. The order is how many reasons fired and then band strength, which are facts about the chart (see: Tonight's list orders by the reasons that fired and then band strength, which are facts about the chart). Selecting a row draws its plan beneath the list and brings it into view (see: Selecting a row draws its plan beneath the list and is no navigation). The reason totals are tonight's counts, and a reason's record over time is the run page's (see: Tonight's reason totals are counts, and a reason's record is the run page's).</p>
> 
> <h3>15.8 Universe</h3>
Why: the approved design draws the reasons in fixed columns and the selected plan beneath the list, and three questions the design raised are ruled.

### 2026-09-19 - ARCHITECTURE.html - section 15.9 states the masthead and the page's opening

Authorised by: The masthead carries the last stored close and the session it is from
Was:
> (no such paragraph)
Now:
> <p>The page opens with the line the masthead carries, being the ticker, the company's name, the last stored close as of the session it closed on with its change on the day, and the sector and industry (see: The masthead carries the last stored close and the session it is from), and with a paragraph saying what the page is for, its three refusals, being that it does not predict where the price will go, does not rank the name against any other and does not say how much to buy, and the ten words it uses one disclosure down. The sizing arithmetic states the risk a share carries and leaves the budget to the reader, which is what keeps the third refusal true (see: The sizing arithmetic states the risk a share carries and holds no account of the reader's).</p>
> <p>A tab on this screen carries the name's listing history:
Why: the approved design opens the name page with the masthead, what the page is for, its refusals and its words, and no screen fetches a price.

### 2026-09-19 - ARCHITECTURE.html - section 15.11 says a record is drawn in two states

Authorised by: A reason's record is drawn in two states, and a bare number beside a ticker is a prohibition rather than a third
Was:
> (no such note)
Now:
> <p class="note">The last row is a prohibition rather than a state: a record is drawn in one of the first two states, with its unresolved setups as the track's own segment in either (see: A reason's record is drawn in two states, and a bare number beside a ticker is a prohibition rather than a third).</p>
> 
> <h3>15.12 Opening a name, in order</h3>
Why: the table's fourth row reads as a fourth state, and it is what no screen draws.

### 2026-09-19 - ARCHITECTURE.html - section 15.14 names the two orders a screen uses

Authorised by: Tonight's list orders by the reasons that fired and then band strength, which are facts about the chart
Was:
> <li>No screen ranks names by anything other than distance to a level, which is a fact about the chart rather than a judgement about the company. (see: Improving what surfaces a name is in scope; ranking names against each other is not)</li>
Now:
> <li>No screen orders names by a judgement about the company. The universe orders by distance to a level and tonight's list by how many reasons fired and then band strength, each a fact about the chart. (see: Improving what surfaces a name is in scope; ranking names against each other is not) (see: Tonight's list orders by the reasons that fired and then band strength, which are facts about the chart)</li>
Why: section 15.7 orders tonight's list by reasons fired and band strength and this line forbade any order but distance, so one of the two had to change.

### 2026-09-18 - .claude/rules/checks.md - `nightly-run` asserts how long a writer waits for another

Authorised by: A writer waits up to ten minutes for another, and a pass stores what it fetched in one write
Was:
> | `nightly-run` | every CI run | The night runs the steps that exist in the order section 14 states, each step doing what its own text says, and a failure names the step and exits non-zero. A night bounded by a deadline it cannot meet stops and says which step it was on, and a payload that arrives and is wrong, being for another session or holding none of the index, is refused before anything is stored. The rule version step runs after the arithmetic it replays and before the close, a live rule that moved inside an open window stops the night there naming the rule with nothing scored and no close, and the night after its windows are closed and opened again scores under the new ones; and no document, fixture, script or source names a night's step by its number except section 14's own note and the night's own step list, both held to section 14's order |
Now:
> | `nightly-run` | every CI run | The night runs the steps that exist in the order section 14 states, each step doing what its own text says, and a failure names the step and exits non-zero. A night bounded by a deadline it cannot meet stops and says which step it was on, and a payload that arrives and is wrong, being for another session or holding none of the index, is refused before anything is stored. The rule version step runs after the arithmetic it replays and before the close, a live rule that moved inside an open window stops the night there naming the rule with nothing scored and no close, and the night after its windows are closed and opened again scores under the new ones; and no document, fixture, script or source names a night's step by its number except section 14's own note and the night's own step list, both held to section 14's order; and every connection the worker opens outside a source a rule's version pins waits for another writer as long as section 17 states, read from the shipped source and shown over a write another connection holds, and a research pass and a theme pass store the documents they fetched in one write, so a store refusing one partway keeps none |
Why: the check carries the tests of section 17's new row, and a roster row not saying what its check asserts is a property nobody wrote down.

### 2026-09-18 - ARCHITECTURE.html - section 17 states how long a writer waits for another

Authorised by: A writer waits up to ten minutes for another, and a pass stores what it fetched in one write
Was:
> no row
Now:
> a row stating the 600 seconds a statement waits for another writer's write outside the sources a rule's version pins, and that a pass stores what it fetched in one write, with why and what asserts it
Why: the wait was the driver's thirty seconds and stated nowhere, and a pass storing its documents one to a write outlasted it and failed the night's queue.

### 2026-09-18 - RUNBOOK.md - a step failing on the store's lock added to the morning table

Authorised by: A writer waits up to ten minutes for another, and a pass stores what it fetched in one write
Was:
> no row
Now:
> a row for a night's step or a research pass failing with `database is locked`: what held the store, and how the night is run again for its session
Why: a pass beside the overnight queue failed the queue on the lock, and the table said nothing of it.

### 2026-09-18 - SCHEMA.md - the analysts' ratings are stored

Authorised by: The fundamentals row carries the analysts' ratings the provider files, on the newest filing alone
Was:
> The company financials endpoint supplies eight parts and the filings archive four: `segments`, `revenueTables`, `guidance` and `facts`, which that endpoint files for no name at all. The archive's four sit on the newest
Now:
> The company financials endpoint supplies ten parts, the analysts' `ratings` among them, and the filings archive five: `segments`, `revenueTables`, `tableGrowth`, `guidance` and `facts`, which that endpoint files for no name at all. The archive's five sit on the newest
Why: the analysts' ratings are stored, and the count had missed the two growth parts, `growth` from the endpoint's filings and `tableGrowth` from the archive's tables.

### 2026-09-18 - SCHEMA.md - the parts before it are fifteen

Authorised by: The fundamentals row carries the analysts' ratings the provider files, on the newest filing alone
Was:
> A thirteenth part, `periodEnd`, is the quarter's end
Now:
> A sixteenth part, `periodEnd`, is the quarter's end
Why: the parts before it are fifteen.

### 2026-09-18 - SCHEMA.md - the analysts' ratings are stored

Authorised by: The fundamentals row carries the analysts' ratings the provider files, on the newest filing alone
Was:
> ; `facts` holds the archive's own filed figures
Now:
> ; `ratings` holds the analysts' mean rating on a scale of one to five, their mean target price and how many rate the name at each of five grades from a strong buy to a strong sell, as the provider files them (see: The fundamentals row carries the analysts' ratings the provider files, on the newest filing alone); `facts` holds the archive's own filed figures
Why: the analysts' ratings are stored.

### 2026-09-18 - ARCHITECTURE.html - the fetcher stores the analysts' ratings

Authorised by: The fundamentals row carries the analysts' ratings the provider files, on the newest filing alone
Was:
> fetches the quarters and the balance sheet when the stored copy predates the name's latest filing, and reads the filings archive for the segment table and management's guidance, which the company financials endpoint files for nobody. The two providers fail apart: an archive that could not be read leaves its two parts named as unread on the row and the other eight stored, because refusing the fetch would lose eight figures to recover two (see: Fundamentals are stored with the filing date they came from).
Now:
> fetches the quarters, the balance sheet and the analysts' ratings when the stored copy predates the name's latest filing (see: The fundamentals row carries the analysts' ratings the provider files, on the newest filing alone), and reads the filings archive for the segment table, the other tables of revenue and management's guidance, which the company financials endpoint files for nobody. The two providers fail apart: an archive that could not be read leaves its five parts named as unread on the row and the other eleven stored, because refusing the fetch would lose eleven parts to recover five (see: Fundamentals are stored with the filing date they came from).
Why: the fetcher stores the analysts' ratings, and the counts had missed the archive's other tables, their growth and its facts.

### 2026-09-18 - RUNBOOK.md - what a pass over the fixture costs

Authorised by: A research pass reads a name's news from the last three months alone, inside each stored move and since the company's own filing, and hands each section the documents code picks from it
Was:
> Over the fixture's KEYS a pass wrote all eight sections it could write, three on the local model and five through the spend cap for $0.0201.
Now:
> Over the fixture's KEYS a pass wrote seven of the eight sections it could write, three on the local model and four through the spend cap for $0.0333, a sum that includes the cause of each large move, asked twice and answered with nothing both times, since a call answered with nothing is billed.
Why: the pass reads three months of news and states which way each move went, and over the fixture the model answers the cause of KEYS's one fall it can read with nothing.

### 2026-09-18 - ARCHITECTURE.html - a move's cause was handed the earliest documents inside it

Authorised by: A research pass reads a name's news from the last three months alone, inside each stored move and since the company's own filing, and hands each section the documents code picks from it
Was:
> then, for each move, the documents fetched for the name that were published inside it, at most two a move, naming the fewest companies and then the earliest (see: A research pass reads a name's news inside each stored move and since the company's own filing, and hands each section the documents code picks from it, the company's own filing first). A move with no document inside it is not put to the model, and a section with none at all is not written
Now:
> moves whose spans share a session read as one episode; then, for each episode, the documents fetched for the name that were published inside its largest move, at most two a move, a title naming the company first, then the newest, then the fewest companies named, and none older than three months before the night (see: A research pass reads a name's news from the last three months alone, inside each stored move and since the company's own filing, and hands each section the documents code picks from it). A move with no document inside it is not put to the model, and a section with none at all is not written
Why: a move's cause was handed the earliest documents inside it, which are about the session before it moved, and overlapping moves were asked for one run three times.

### 2026-09-18 - ARCHITECTURE.html - the cause is asked once an episode, and not for a move no document explains

Authorised by: A research pass reads a name's news from the last three months alone, inside each stored move and since the company's own filing, and hands each section the documents code picks from it
Was:
> <td>one sentence for each move that has a document inside it, saying what that document gives as the cause</td>
Now:
> <td>one sentence for each episode whose largest move has a document inside it, beginning with the session that move ended on and saying what that document gives as the cause, and nothing for a move no document beside it gives a cause for in the direction it went</td>
Why: the cause is asked once an episode, since overlapping moves are one run of the price, and a document about a rise is not given as the cause of a fall.

### 2026-09-18 - ARCHITECTURE.html - the six newest documents since the filing were one or two days of news

Authorised by: A research pass reads a name's news from the last three months alone, inside each stored move and since the company's own filing, and hands each section the documents code picks from it
Was:
> the facts file, the company's own filing and at most six documents published since it naming the fewest companies, and the theme record where one is stored, handed over together
Now:
> the facts file, the company's own filing and at most six documents published since it, one from each of six equal stretches of the days since, a title naming the company first and then the fewest companies named, and the theme record where one is stored, handed over together
Why: the six newest documents since the filing were one or two days of news.

### 2026-09-18 - ARCHITECTURE.html - a key written on an earlier night stated figures the page no longer showed

Authorised by: The key under each figure is written for each night's facts file
Was:
> <td>nothing new: every value the figure draws, the close, the averages, the levels, momentum, the latest quarter and the valuation, is already computed and in the facts file</td>
Now:
> <td>nothing new: every value the figure draws, the close, the averages, the levels, momentum, the latest quarter and the valuation, is already computed and in the facts file, which is the night's, so the key is written for each night's facts file (see: The key under each figure is written for each night's facts file)</td>
Why: a key written on an earlier night stated figures the page no longer showed.

### 2026-09-18 - ARCHITECTURE.html - the cycle was written from pages that only mentioned the industry

Authorised by: A theme page is handed to the model only where its text names the industry
Was:
> a search per industry rather than per name, scoped to the industry's source list, a date range and full page text, with what passes admissibility stored as the theme record every name in the industry shares
Now:
> a search per industry rather than per name, scoped to the industry's source list, a date range and full page text, with what passes admissibility stored as the theme record every name in the industry shares, and a page handed to the model only where its text names the industry (see: A theme page is handed to the model only where its text names the industry)
Why: the cycle was written from pages that only mentioned the industry.

### 2026-09-18 - ARCHITECTURE.html - the theme page floor is a limit of the row's own kind

Authorised by: A theme page is handed to the model only where its text names the industry
Was:
> its call is handed at most 10 of the pages it admitted, every site's first before any site's second, each carried as its first 30,000 characters (see: A theme search is scoped by parameter, not by hope) (see: A theme pass searches each site on the industry list alone, and hands the model a bounded set of the pages they return)</td>
Now:
> its call is handed at most 10 of the pages it admitted, those whose title and text name the industry's own words at least 5 times in every 10,000 characters, every site's first before any site's second, each carried as its first 30,000 characters (see: A theme search is scoped by parameter, not by hope) (see: A theme pass searches each site on the industry list alone, and hands the model a bounded set of the pages they return) (see: A theme page is handed to the model only where its text names the industry)</td>
Why: the theme page floor is a limit of the row's own kind.

### 2026-09-18 - RUNBOOK.md - the operator ruled research news bounded to the last three months

Authorised by: A research pass reads a name's news from the last three months alone, inside each stored move and since the company's own filing, and hands each section the documents code picks from it
Was:
> then the name's news inside each stored move and from the release's filing date to the night, overlapping spans once,
Now:
> then the name's news inside each stored move and from the release's filing date to the night, overlapping spans once and none older than three months before the night,
Why: the operator ruled research news bounded to the last three months.

### 2026-09-18 - RUNBOOK.md - the choice of documents changed

Authorised by: A research pass reads a name's news from the last three months alone, inside each stored move and since the company's own filing, and hands each section the documents code picks from it
Was:
> two a move for the cause of each move, and six since the release beside the release itself for the sections built across the evidence (see: A research pass reads a name's news inside each stored move and since the company's own filing, and hands each section the documents code picks from it, the company's own filing first)
Now:
> two a move for the cause of each move's episode, and six since the release beside the release itself for the sections built across the evidence, one from each stretch of the days since it (see: A research pass reads a name's news from the last three months alone, inside each stored move and since the company's own filing, and hands each section the documents code picks from it)
Why: the choice of documents changed.

### 2026-09-18 - CLAUDE.md - the key under each figure explains figures that move every session

Authorised by: The key under each figure is written for each night's facts file
Was:
> **Nothing expires on a timer.** Research is rewritten when a filing appears, an earnings date passes, a name's news volume jumps above its own baseline, or the operator asks. (see: Nothing expires on a timer)
Now:
> **Nothing expires on a timer.** Research is rewritten when a filing appears, an earnings date passes, a name's news volume jumps above its own baseline, or the operator asks. The key under each figure, which explains the night's figures, is written for each night's facts file. (see: Nothing expires on a timer) (see: The key under each figure is written for each night's facts file)
Why: the key under each figure explains figures that move every session, and a key from an earlier night stated figures the page no longer showed.

### 2026-09-18 - ARCHITECTURE.html - the decision it cited was superseded

Authorised by: Guidance is stored as management's own prose, and the facts file carries each figure the passage states as the claim checker reads it
Was:
> The guidance is stored as management's own passage with the exhibit and date it was filed on, and never as a figure (see: Guidance is stored as management's own prose and never parsed into a figure)
Now:
> The guidance is stored as management's own passage with the exhibit and date it was filed on, and never as a figure (see: Guidance is stored as management's own prose, and the facts file carries each figure the passage states as the claim checker reads it)
Why: the decision it cited was superseded, and the stored passage is still never a figure.

### 2026-09-18 - SCHEMA.md - the decision it cited was superseded

Authorised by: Guidance is stored as management's own prose, and the facts file carries each figure the passage states as the claim checker reads it
Was:
> `guidance` holds management's own passage with the exhibit and the date it was filed on, and never a figure struck from it (see: Guidance is stored as management's own prose and never parsed into a figure)
Now:
> `guidance` holds management's own passage with the exhibit and the date it was filed on, and never a figure struck from it (see: Guidance is stored as management's own prose, and the facts file carries each figure the passage states as the claim checker reads it)
Why: the decision it cited was superseded, and the stored passage is still never a figure.

### 2026-09-18 - ARCHITECTURE.html - the facts file carries more than the eleven facts the row counted

Authorised by: The facts file carries a quarter's growth, its earnings against the estimate and the filing's own tables with their year-earlier columns
Was:
> The fundamentals joined its reads at 6.1, which is the checkpoint that created that store, and eleven facts come off the newest filing a name holds.
Now:
> The fundamentals joined its reads at 6.1, which is the checkpoint that created that store, and the newest filing a name holds gives the quarter's figures, its growth and its earnings against the estimate, the balance sheet, the valuation, the filing's own tables with the same months a year before and each group's growth, and every figure management's located guidance states (see: The facts file carries a quarter's growth, its earnings against the estimate and the filing's own tables with their year-earlier columns) (see: Guidance is stored as management's own prose, and the facts file carries each figure the passage states as the claim checker reads it). The membership row gives the company's name (see: The membership row carries the company's name the index feed states).
Why: the facts file carries more than the eleven facts the row counted, and a count restated here goes stale with the next fact.

### 2026-09-18 - ARCHITECTURE.html - the facts assembler reads the company's name off the membership row

Authorised by: The membership row carries the company's name the index feed states
Was:
> <td>bar store, indicators, swings, volume profile, levels, ladders, moves, calendar, fundamentals</td><td>facts</td>
Now:
> <td>membership, bar store, indicators, swings, volume profile, levels, ladders, moves, calendar, fundamentals</td><td>facts</td>
Why: the facts assembler reads the company's name off the membership row.

### 2026-09-18 - ARCHITECTURE.html - the facts assembler reads membership

Authorised by: The membership row carries the company's name the index feed states
Was:
> <tr><td>Facts assembler</td><td></td><td><span class="r">R</span></td>
Now:
> <tr><td>Facts assembler</td><td><span class="r">R</span></td><td><span class="r">R</span></td>
Why: the facts assembler reads membership, in the read and write matrix.

### 2026-09-18 - SCHEMA.md - the fundamentals row carries each table group's growth

Authorised by: A group's growth in a filing's own tables is computed from the columns the table states
Was:
> `revenueTables` holds the filing's other tables of revenue by a grouping, by market, product or region, each in that shape (see: The filing's other tables of revenue by a grouping are kept beside its segment table); `guidance`
Now:
> `revenueTables` holds the filing's other tables of revenue by a grouping, by market, product or region, each in that shape (see: The filing's other tables of revenue by a grouping are kept beside its segment table); `tableGrowth` holds each of those tables' groups' change on the same months a year before, computed from the columns the table states (see: A group's growth in a filing's own tables is computed from the columns the table states); `guidance`
Why: a segment's growth is what a section and a reader of the segment table ask next, and nothing the store held carried it.

### 2026-09-18 - SCHEMA.md - the membership row carries the company's name

Authorised by: The membership row carries the company's name the index feed states
Was:
> | `industry` | TEXT | the industry the provider last named for this ticker, null where it has named none. Last because it was added by an `ALTER TABLE` at 6.9 |
Now:
> | `industry` | TEXT | the industry the provider last named for this ticker, null where it has named none. After `sector` because it was added by an `ALTER TABLE` at 6.9 |
> | `name` | TEXT | the company's name as the provider's span for this ticker states it, null where the span states none. Last because it was added by an `ALTER TABLE` at the 5.1 correction of 2026-09-18 |
> and a paragraph saying where the name comes from, that it is coalesced, and what reads it
Why: the name page had no company name to state and a research pass had none to tell an article about the company from one that mentions it.

### 2026-09-18 - SCHEMA.md - the fundamentals row carries the quarter's growth

Authorised by: A quarter's growth is computed on its own row from the filings the provider returned
Was:
> `payload` holds the quarter's figures, the balance sheet, and the margin computed from that filing's own revenue and gross profit,
Now:
> `payload` holds the quarter's figures, the balance sheet, the margin computed from that filing's own revenue and gross profit, and the quarter's growth on the same quarter a year before and on the quarter before it, computed from the earlier filings returned with it (see: A quarter's growth is computed on its own row from the filings the provider returned),
Why: a section quoting a quarter's growth was refused because nothing the store held carried the change, and the screen computes nothing.

### 2026-09-18 - SCHEMA.md - the fundamentals row carries the filing's other tables of revenue by a grouping

Authorised by: The filing's other tables of revenue by a grouping are kept beside its segment table
Was:
> The company financials endpoint supplies eight parts and the filings archive three: `segments`, `guidance` and `facts`, which that endpoint files for no name at all. The archive's three sit on the newest filing's row alone
> A twelfth part, `periodEnd`, is the quarter's end
> `segments` holds the report the figures were read from, the scale the table stated, its period columns and its groups in the order the table states them; `guidance`
Now:
> The company financials endpoint supplies eight parts and the filings archive four: `segments`, `revenueTables`, `guidance` and `facts`, which that endpoint files for no name at all. The archive's four sit on the newest filing's row alone
> A thirteenth part, `periodEnd`, is the quarter's end
> `segments` holds the report the figures were read from, the scale the table stated, its period columns and its groups in the order the table states them; `revenueTables` holds the filing's other tables of revenue by a grouping, by market, product or region, each in that shape (see: The filing's other tables of revenue by a grouping are kept beside its segment table); `guidance`
Why: the figures a results release headlines are often in a filing's tables by market or region rather than its segment table, and the first live research pass was refused for quoting two of them.

### 2026-09-18 - RUNBOOK.md - how the screens are opened

Corrects: the runbook said nowhere how to start the read surface, and started the way the worker is it stopped for want of a data root; found when the operator went to open the screens for the first time.
Was:
> nothing: no section said how to start the read surface or where it reads its store from
Now:
> "Opening the screens", after "What runs, and when": the command from the repository root, the address the launch settings give, the four routes, that its settings name the worker's data root read against the checkout, that the environment overrides it, and that it can stay open while the night runs
Why: the screens could not be opened from anything the corpus said, and the operator had never seen one.

### 2026-09-18 - SCHEMA.md - a quarter's end is the period the company's own report states

Authorised by: A quarter ends on the date the company's own filing states
Was:
> **Two providers fill one row and `source` says which filled what, part by part.** The company financials endpoint supplies eight parts and the filings archive three: `segments`, `guidance` and `facts`, which that endpoint files for no name at all. The archive's three sit on the newest filing's row alone for the reason the ratios do and one of its own: a segment table is read from one filing's report page and the guidance from one announcement's exhibit, so writing either onto a historical row would state that an older quarter's segments were this quarter's, and deriving them per filing would cost a request per row for figures nothing reads.
Now:
> **Two providers fill one row and `source` says which filled what, part by part.** The company financials endpoint supplies eight parts and the filings archive three: `segments`, `guidance` and `facts`, which that endpoint files for no name at all. The archive's three sit on the newest filing's row alone for the reason the ratios do and one of its own: a segment table is read from one filing's report page and the guidance from one announcement's exhibit, so writing either onto a historical row would state that an older quarter's segments were this quarter's, and deriving them per filing would cost a request per row for figures nothing reads. A twelfth part, `periodEnd`, is the quarter's end and comes from either provider on any row: the company financials endpoint labels a quarter with the last day of its month, and the archive's index states the period each 10-Q and 10-K covers, so a row takes the period of the periodic report ending within a week of that label and keeps the label only where the archive was not read or indexes no such report, and `source` names whose date it is (see: A quarter ends on the date the company's own filing states).
Why: the row stored the provider's month-end label as the quarter's end, so a written section stated a quarter ending on a day it did not end on, and the company's own date was refused.

### 2026-09-18 - .claude/rules/checks.md - `gap-refusal` reads a year asked for alone against the exchange calendar

Corrects: the backfill read the calendar for its refusal off the names a night asked for, so a year asked for alone was stored with its gap, which a refused name asked for again always is; found writing the 1.2 correction's schedule.
Was:
> | `gap-refusal` | every CI run | A series arriving with an interior session missing is refused, that name's stored series is left as it was, and the gap's date is named on the run log; a hole at either edge is a shorter history rather than a gap, and one series alone reports that it cannot be checked rather than that it is clean. Every computation over a name whose stored series already holds a hole stops, so the five computed tables withhold every row for it while the ladder and the listing still carry theirs with the gap's date as the reason, asserted per table over a constructed store rather than in one loop over the seven, because the two failures are opposite: a figure computed across the hole, and a member left without the row every member gets |
Now:
> | `gap-refusal` | every CI run | A series arriving with an interior session missing is refused, that name's stored series is left as it was, and the gap's date is named on the run log; a hole at either edge is a shorter history rather than a gap, and one series alone reports that it cannot be checked rather than that it is clean, so a year the backfill asks for alone is read against the exchange's closure table and refused there, which is how a refused name asked for again on its own is refused again. Every computation over a name whose stored series already holds a hole stops, so the five computed tables withhold every row for it while the ladder and the listing still carry theirs with the gap's date as the reason, asserted per table over a constructed store rather than in one loop over the seven, because the two failures are opposite: a figure computed across the hole, and a member left without the row every member gets |
Why: the row states what the check asserts, and the check now asserts a lone year refused against the closure table as well as a year refused beside others.

### 2026-09-18 - RUNBOOK.md - the morning's line for a name the backfill served no year

Authorised by: A name the backfill stored nothing for is asked for again on the five nights after and weekly after that, and its page and the run page say so until one stores its year
Was:
> (no row for a name the backfill served no year)
Now:
> | A name's page opens with a line saying no year of prices came back, and the run page's failed region names the backfill | the provider returned nothing when the backfill asked for the name's year | nothing at first: the backfill asks again on each of the next 5 nights and then on the first night 7 or more days after the session it was last asked for, until one stores its year or the name leaves the index, and the page's line says how many nights it has been asked for and when it is asked next. A ticker the membership feed lists and the price file carries under another ticker never gets a year, because the feed's listing is the index (see: A ticker the index feed stops listing leaves the index on the night it goes unlisted), so it stays without prices until the provider's two files agree |
Why: the name's page and the run page now say so on every night it holds no year, and the table is where a person reads what a line in the morning means and what to do about it.

### 2026-09-18 - .claude/rules/checks.md - `nightly-cost` asserts the backfill's schedule for a name it stored nothing for

Authorised by: A name the backfill stored nothing for is asked for again on the five nights after and weekly after that, and its page and the run page say so until one stores its year
Was:
> | `nightly-cost` | every CI run | The nightly path makes zero per-name network requests in its steady state and its arithmetic zero model calls, asserted over the shipped source and over a recorded run, with the run measured over two universe sizes so the count is shown not to grow with the population. The two carve-outs the hard rule names are asserted rather than exempted: a joiner's backfill is one request per name ever, and a suspect name costs one request on each of the five nights of its retries and one a week after them, counted over a constructed name whose refetch fails. The night reaches no lane an open reaches, read off what the components its own file constructs declare, and every model call a whole recorded night makes sits on the overnight queue's own row or on a pass that row names, with nothing spent anywhere, because the queue is carved out of the model-call rule by name and out of nothing else. A recorded night at one rule version and at the fullest register the version bound admits makes the same requests on every step and none on the version step, counted off the feeds and put on the step each was made on, with a score per version per name and the step saying how many versions of each kind it replayed, so the step's cost is shown to grow with versions and not with requests |
Now:
> | `nightly-cost` | every CI run | The nightly path makes zero per-name network requests in its steady state and its arithmetic zero model calls, asserted over the shipped source and over a recorded run, with the run measured over two universe sizes so the count is shown not to grow with the population. The two carve-outs the hard rule names are asserted rather than exempted: a joiner's backfill is one request per name ever where it stores the name's year, and a name it stores nothing for costs one request on each of the five nights after and one a week after them, counted over a constructed name the provider serves nothing, and a suspect name costs one request on each of the five nights of its retries and one a week after them, counted over a constructed name whose refetch fails. The night reaches no lane an open reaches, read off what the components its own file constructs declare, and every model call a whole recorded night makes sits on the overnight queue's own row or on a pass that row names, with nothing spent anywhere, because the queue is carved out of the model-call rule by name and out of nothing else. A recorded night at one rule version and at the fullest register the version bound admits makes the same requests on every step and none on the version step, counted off the feeds and put on the step each was made on, with a score per version per name and the step saying how many versions of each kind it replayed, so the step's cost is shown to grow with versions and not with requests |
Why: the row said the backfill is one request per name ever, which the ruling bounds rather than keeps for a name the provider serves nothing, and the check now counts that schedule night by night.

### 2026-09-18 - CLAUDE.md - the backfill's carve-out names the schedule a name it stored nothing for is asked on

Authorised by: A name the backfill stored nothing for is asked for again on the five nights after and weekly after that, and its page and the run page say so until one stores its year
Was:
> **The nightly run makes no model call and no per-name network request.** Bars arrive in one bulk request and news in one feed request, so a night costs the same whether the universe is fifty names or five hundred. Any component that adds a per-name call to the nightly path is a defect, not a feature. Two carve-outs are named rather than left to be discovered, and neither grows with the index: a new member's one-year backfill, once per name ever, and the corporate action refetch, which asks for a year again for a name an action landed on, on each of the five nights after a failed check and once a week after that until a refetch succeeds or the name leaves the index. (see: The nightly run is arithmetic only) (see: Adjusted history is re-fetched after a corporate action) (see: A suspect name is asked for again on the five nights after it is marked and weekly after that, and its own page, its row on tonight's list and the run page say so until a refetch succeeds)
Now:
> **The nightly run makes no model call and no per-name network request.** Bars arrive in one bulk request and news in one feed request, so a night costs the same whether the universe is fifty names or five hundred. Any component that adds a per-name call to the nightly path is a defect, not a feature. Two carve-outs are named rather than left to be discovered, and neither grows with the index: a new member's one-year backfill, once per name and again, for a name it stored nothing for, on each of the five nights after and once a week after that until one stores its year or the name leaves the index, and the corporate action refetch, which asks for a year again for a name an action landed on, on each of the five nights after a failed check and once a week after that until a refetch succeeds or the name leaves the index. (see: The nightly run is arithmetic only) (see: Adjusted history is re-fetched after a corporate action) (see: A suspect name is asked for again on the five nights after it is marked and weekly after that, and its own page, its row on tonight's list and the run page say so until a refetch succeeds) (see: A name the backfill stored nothing for is asked for again on the five nights after and weekly after that, and its page and the run page say so until one stores its year)
Why: the carve-out allowed a new member's backfill once, and the backfill asked for any member holding no bar on every night, so a name the provider served no year was asked for on every night; the ruling bounds it with the refetch's schedule, and the rule now says so where it is cited.

### 2026-09-18 - ARCHITECTURE.html - the backfill's schedule for a name it stored nothing for, where the catalogue, the matrix, section 17 and section 18 state the backfill

Authorised by: A name the backfill stored nothing for is asked for again on the five nights after and weekly after that, and its page and the run page say so until one stores its year
Was:
> <tr><td><b>Backfill</b></td><td><span class="layer L-compute">compute</span></td><td>nightly, once</td><td>historical price feed, membership, bar store</td><td>bar store</td><td>fetches one year for any member holding no history, one request per name, and never again for a name that already holds its year</td></tr>
>
> <tr><td>Backfill</td><td><span class="r">R</span></td><td><span class="r">R</span> <span class="w">W</span></td><td></td><td></td><td></td><td></td><td></td><td></td><td></td><td></td><td></td><td></td><td></td><td></td><td></td><td><span class="w">W</span></td></tr>
>
> The backfill is the one exception and is bounded rather than nightly: it makes one request per name holding no history, which is every name on the first run and a new joiner afterwards, and never again for a name that already holds its year.
>
> <tr><td>Backfill</td><td>one year per name, once, on the first run and on the night a name joins the index; never repeated for a name that already holds its year (see: Bars come from EODHD, bulk nightly and per ticker for the backfill)</td><td>a single day of bars computes nothing, so the store has to start full; repeating it would spend hundreds of requests to fetch bars already held</td><td>run log request count against names lacking history</td></tr>
>
> (no row for a name the provider serves no year)
Now:
> <tr><td><b>Backfill</b></td><td><span class="layer L-compute">compute</span></td><td>nightly, once</td><td>historical price feed, membership, bar store, run log</td><td>bar store</td><td>fetches one year for any member holding no history, one request per name, and never again for a name that already holds its year; a name it stored nothing for is asked for again on the refetch's schedule, read off its own earlier rows on the run log, until one stores its year or the name leaves the index (see: A name the backfill stored nothing for is asked for again on the five nights after and weekly after that, and its page and the run page say so until one stores its year)</td></tr>
>
> <tr><td>Backfill</td><td><span class="r">R</span></td><td><span class="r">R</span> <span class="w">W</span></td><td></td><td></td><td></td><td></td><td></td><td></td><td></td><td></td><td></td><td></td><td></td><td></td><td></td><td><span class="r">R</span> <span class="w">W</span></td></tr>
>
> The backfill is the one exception and is bounded rather than nightly: it makes one request per name holding no history, which is every name on the first run and a new joiner afterwards, and never again for a name that already holds its year. A name it stored nothing for is asked for again on each of the 5 nights after the first and then every 7 days until one stores its year or the name leaves the index, which is bounded by the names the provider cannot serve rather than by the universe (see: A name the backfill stored nothing for is asked for again on the five nights after and weekly after that, and its page and the run page say so until one stores its year).
>
> <tr><td>Backfill</td><td>one year per name, once, on the first run and on the night a name joins the index; never repeated for a name that already holds its year (see: Bars come from EODHD, bulk nightly and per ticker for the backfill); a name it stored nothing for asked for again on each of the 5 nights after the first and then every 7 days, until one stores its year or the name leaves the index (see: A name the backfill stored nothing for is asked for again on the five nights after and weekly after that, and its page and the run page say so until one stores its year)</td><td>a single day of bars computes nothing, so the store has to start full; repeating it would spend hundreds of requests to fetch bars already held, and asking for a name the provider cannot serve on every night it stays a member is a per-name request a night for as long as it lasts</td><td>run log request count against the names lacking history that the schedule makes due</td></tr>
>
> <tr><td>The provider serves no year for a name the backfill asks for</td><td>nothing is stored, and the name is asked for again on the refetch's schedule, on each of the nights after the first and then weekly, until a request stores its year or the name leaves the index; the backfill step ends partial on every night a member holds no year (see: A name the backfill stored nothing for is asked for again on the five nights after and weekly after that, and its page and the run page say so until one stores its year)</td><td>the run page's stale-and-failed region names the name on every night it holds no year, with the nights it was asked for and when it is asked next, and its page opens with a line saying the same</td><td>a name the provider cannot serve one night may be served the next, and asking for it on every night it stays a member is a per-name request a night on a path that carves out one; asking once would leave a new member with no prices for good after one bad answer</td></tr>
Why: the backfill now reads its own earlier rows on the run log for the schedule, which the catalogue and the matrix have to show, and section 17 bounded the backfill by a rule the code did not keep for a name the provider could not serve.

### 2026-09-18 - SCHEMA.md - the backfill's row on the run log, and the schedule that reads it back

Authorised by: A name the backfill stored nothing for is asked for again on the five nights after and weekly after that, and its page and the run page say so until one stores its year
Was:
> (no paragraph on the backfill's row)
Now:
> **The backfill's row names what it asked for and every member still holding no year, and its schedule reads those rows back.** Its `detail` is JSON carrying the session, the names it asked for, each refused year's missing session, and each member still holding no year with how many nights it has been asked for, the session it was last asked for on, and the session it is next asked for on, null where that is the next night. A night that asks for nothing and leaves no member without a year writes none. The count of nights a name was asked for is read off these rows rather than kept a second time, because the run log is the record of what each night did, and the name page reads the newest row naming a name for the line it opens with (see: A name the backfill stored nothing for is asked for again on the five nights after and weekly after that, and its page and the run page say so until one stores its year).
Why: the backfill's schedule reads its own earlier rows, so what they carry is a column's content other code depends on, and SCHEMA is where that is declared.

### 2026-09-18 - SCHEMA.md - a span the feed no longer lists leaves on the night that finds it unlisted

Authorised by: A ticker the index feed stops listing leaves the index on the night it goes unlisted
Was:
> | `left` | TEXT | the date the name stops being a member, null until a leave is announced. The provider carries a leave before it takes effect, so a name with a date here is still a member on every session before it |
>
> **A provider that re-dates a member's span leaves two open rows for one ticker, and a screen draws the one observed most recently.** The upsert is keyed on the join date, so a span whose start the provider corrects is written as a second row, and the first stays open, dated by the `observed_at` it was last seen with. The read surface draws each ticker once, from its open span with the newest `observed_at`, and the nightly stages read every open span and write one row a ticker. A span the provider stops listing without a leave date still reads as a member, which is a question about what the provider sends rather than about this table.
Now:
> | `left` | TEXT | the date the name stops being a member, null until a leave is announced or the feed stops listing the span. The provider carries a leave before it takes effect, so a name with a date here is still a member on every session before it. A span the feed no longer lists carries the session of the night that found it unlisted |
>
> **A span the feed no longer lists leaves on the session of the night that finds it unlisted, and a screen draws one span a ticker.** The upsert is keyed on the join date, so a span whose start the provider corrects is written as a second row, and the night that writes it closes the first on its session, as it closes the span of a ticker the provider renamed and lists no longer. A span the feed lists again is reopened by the upsert, which writes the feed's leave date over the one the night wrote. A night whose feed stops listing more tickers than the loader closes in one night closes none (see: A ticker the index feed stops listing leaves the index on the night it goes unlisted). The sessions before a re-dated span was closed are covered by both of its rows, so the read surface draws each ticker once, from its current span with the newest `observed_at`, and the nightly stages read every current span and write one row a ticker.
Why: the operator ruled that a ticker the feed stops listing leaves the index, which the loader now writes into this column, and the paragraph's last sentence, which left that question to the provider, is the one the ruling answers.

### 2026-09-18 - ARCHITECTURE.html - the failure table gains a ticker the index feed stops listing

Authorised by: A ticker the index feed stops listing leaves the index on the night it goes unlisted
Was:
> (no row for a ticker the feed stops listing without a leave date)
Now:
> <tr><td>A ticker the index feed stops listing</td><td>the night whose membership feed does not list a span still open on its session closes that span on the session, whether the ticker is gone from the feed or listed under another join date, and a span the feed lists again is reopened; a night whose feed stops listing more than 10 tickers in one night closes none of them, and its membership step ends partial (see: A ticker the index feed stops listing leaves the index on the night it goes unlisted)</td><td>the ticker leaves the universe and tonight's list that night and the run page's membership line names it; a night that closed none has its membership step on the stale-and-failed region with every ticker named</td><td>a provider that renames a ticker lists the new one and stops listing the old without a leave date on either, so closing only the spans the feed dates kept one company in the index under two tickers and, from a rebalance, under three. A feed missing that many names at once is a fault in the feed rather than that many departures</td></tr>
Why: the row for a name leaving the index covered a leave the feed dates and nothing covered a ticker the feed stops listing, which kept EQR in the index beside VMRK from 2026-09-17 and would have kept the joiner of 2026-09-21 in it under three tickers.

### 2026-09-18 - SCHEMA.md - a ticker holding two open spans is drawn once, from the span observed most recently

Corrects: the membership section stated one row per span and nothing about one ticker holding two open spans, which the provider produced on 2026-09-17 by re-dating VMRK's span; the universe read drew the ticker twice, and tonight's list, every name page and the report export stopped on it. Found on 2026-09-18 by serving the screens over a copy of the operator's store.
Was:
> (no paragraph on one ticker holding two open spans)
Now:
> **A provider that re-dates a member's span leaves two open rows for one ticker, and a screen draws the one observed most recently.** The upsert is keyed on the join date, so a span whose start the provider corrects is written as a second row, and the first stays open, dated by the `observed_at` it was last seen with. The read surface draws each ticker once, from its open span with the newest `observed_at`, and the nightly stages read every open span and write one row a ticker. A span the provider stops listing without a leave date still reads as a member, which is a question about what the provider sends rather than about this table.
Why: a reader that assumes one open span a ticker fails on the first ticker the provider re-dates, and the table has admitted two since it was keyed on the join date.

### 2026-09-18 - ARCHITECTURE.html - a live reason's retirement and a candidate's promotion cited where the architecture places them

Authorised by: A live reason is added or retired only by a change to section 11 and the code together, and the register holds candidates alone
Was:
> a condition whose setups fail to clear their own break-even over enough resolved cases is retired; a new one is registered and scored in shadow until it has earned promotion</td>
>
> <td>no verdict of any kind below the stated minimum; a higher minimum before a live condition may be retired</td>
>
> <td>a promotion, a retirement or a rule version change is written with the evidence that produced it and the version it replaced</td>
>
> 400 before a live condition may be retired (see: An unresolved setup is never a win) (see: A reason's share, verdict and both floors are counted over the resolved setups that set a bar)</td>
Now:
> a condition whose setups fail to clear their own break-even over enough resolved cases is retired; a new one is registered and scored in shadow until it has earned promotion (see: A live reason is added or retired only by a change to section 11 and the code together, and the register holds candidates alone)</td>
>
> <td>no verdict of any kind below the stated minimum; a higher minimum before a live condition may be retired (see: A live reason is added or retired only by a change to section 11 and the code together, and the register holds candidates alone)</td>
>
> <td>a promotion, a retirement or a rule version change is written with the evidence that produced it and the version it replaced (see: A live reason is added or retired only by a change to section 11 and the code together, and the register holds candidates alone)</td>
>
> 400 before a live condition may be retired (see: An unresolved setup is never a win) (see: A reason's share, verdict and both floors are counted over the resolved setups that set a bar) (see: A live reason is added or retired only by a change to section 11 and the code together, and the register holds candidates alone)</td>
Why: section 13.2 placed which conditions exist at the register and section 13.3 a live condition's retirement beside the verdict's minimum, and neither said a live reason is changed only in section 11 and the code, which is what the register refusing its name assumes.

### 2026-09-18 - ARCHITECTURE.html - a threshold's window cited where the guardrail and the limit state it

Authorised by: A reason's record reads only the rows written under the threshold the code carries, and the rows written under another are kept
Was:
> <td>a rule or threshold is not changed during a window it is being measured over. A change starts a new window and the old one is kept</td>
>
> a change opens a new window and the previous one is kept (see: Adding a candidate later restarts the clock)</td>
Now:
> <td>a rule or threshold is not changed during a window it is being measured over. A change starts a new window and the old one is kept (see: A reason's record reads only the rows written under the threshold the code carries, and the rows written under another are kept)</td>
>
> a change opens a new window and the previous one is kept (see: Adding a candidate later restarts the clock) (see: A reason's record reads only the rows written under the threshold the code carries, and the rows written under another are kept)</td>
Why: the guardrail named thresholds and only ladder rules had windows.

### 2026-09-18 - ARCHITECTURE.html - section 13.2's thresholds row states the trigger its obligation reads, and two of section 17's rows say what asserts them

Corrects: architecture-conformance read 13.2's rows against a list of checkpoints written in the test and read the thresholds row for two phrases of its own, and section 17's frozen windows row named "rule version stamps on every scored listing", which no listing carries. Found by the phase 8 sign-off review on 2026-09-16.
Was:
> <td>none. Phase 5 is what first records how many names fire each night, and the calibration itself waits on sixty nights of that record, which no checkpoint accumulates. It is carried as an operating obligation and read on the run page, using those nights rather than a backfill</td>
>
> <td>the run page must show the count beside every verdict and withhold the verdict below either floor, naming the one that is short</td>
>
> <td>rule version stamps on every scored listing</td>
Now:
> <td>none. Phase 5 is what first records how many names fire each night, and the calibration itself waits on 60 nights of that record, which no checkpoint accumulates. It is carried as an operating obligation and read on the run page, using those nights rather than a backfill (owes: The six reason thresholds calibrated from the nights they fired on)</td>
>
> <td>the run page must show the count beside every verdict and withhold the verdict below either floor, naming the one that is short; no page computes the retirement floor, which is read against that count</td>
>
> <td>each ladder rule's windows kept as rows the night's drift check reads, and each reason's threshold stored on every listing row it was evaluated on</td>
Why: a row read against its obligation has to state the trigger in the obligation's own form and cite it, a bound states where its end shows, and a column naming a mechanism has to name one that exists.

### 2026-09-18 - BUILD_PLAN.md - the 8.2 ruling's reason no longer rests on the guard 8.7 removed

Corrects: phase 8's opening justified filing the rules directory ruling on 8.2 with "a checkpoint appended after the phase report would be the plan's last, which no entry may record as built while work in the phase remains", which is the anchor 8.7 found forbade every phase's last checkpoint and removed. Found by the phase 8 sign-off review on 2026-09-16.
Was:
> It is a ruling on an existing checkpoint rather than a checkpoint of its own, because the loop's numbers are fixed by the claims still out of scope at 8.3 to 8.6 and a checkpoint appended after the phase report would be the plan's last, which no entry may record as built while work in the phase remains.
Now:
> It is a ruling on an existing checkpoint rather than a checkpoint of its own, because the loop's numbers are fixed by the claims still out of scope at 8.3 to 8.6, and a checkpoint appended after the phase report would build nothing the report covers.
Why: a later session reading the old sentence would refuse a checkpoint after a phase report on the strength of a guard that no longer exists.

### 2026-09-18 - BUILD_PLAN.md - 8.4 names the promotion nothing yet computes, and the obligation that owes it

Authorised by: A live reason is added or retired only by a change to section 11 and the code together, and the register holds candidates alone
Was:
> is skipped on its own row and counted on the stage's line, and is not a failure (see: Only a missing or moved evaluator fails the shadow column, and a name-night without the readings is a counted skip).
>
> (no row named A candidate's shadow record computed, and a promotion written against it)
Now:
> is skipped on its own row and counted on the stage's line, and is not a failure (see: Only a missing or moved evaluator fails the shadow column, and a name-night without the readings is a counted skip). No component this phase builds computes a candidate's record, so the region withholds a record nothing computes and no candidate can be promoted until one does (owes: A candidate's shadow record computed, and a promotion written against it).
>
> | **A candidate's shadow record computed, and a promotion written against it** | 8.7 correction | operating | 1 candidate condition registered, read on the run page's shadow candidates region, which 8.4 builds. A candidate's record cannot meet the minimum before 60 distinct listing sessions have passed after it was registered, since that is the minimum's own floor, so the plan written when this fires has at least that long. Phase 8 built the register and the shadow column and nothing that computes a candidate's record from them, so section 13's shadow before live and every change is recorded each promise a promotion nothing can yet make: the plan builds the candidate's record over the shadow column and forward returns, the minimum and the corrected threshold applied to it, and the change to section 11 and the code a promotion is, with the retirement row that takes the candidate out of the family (see: A live reason is added or retired only by a change to section 11 and the code together, and the register holds candidates alone) |
Why: 13.3 promises a promotion written with its evidence and a shadow record meeting the minimum before one, and phase 8 built no component that computes a candidate's record.

### 2026-09-18 - RUNBOOK.md - the registrar's refusals as built, and a live reason's retirement and a candidate's promotion

Authorised by: A live reason is added or retired only by a change to section 11 and the code together, and the register holds candidates alone
Was:
> The registrar refuses an evaluator the code does not carry, a parameter the evaluator does not read, a value that is not a finite number, a ninth candidate, a live reason's name, and any change to a candidate that stands registered. A live reason is retired only by changing section 11 and the code together, once its record holds 400 resolved setups. A change is a retirement and a new registration, under the same name or another, and a name registered again stands once:
Now:
> The registrar refuses an evaluator the code does not carry, a parameter the evaluator does not read, a value that is not a finite number, a ninth candidate, a registration stating no rule or no test, a retirement stating no evidence, a live reason's name, and any change to a candidate that stands registered. A live reason is retired, and a candidate promoted, only by changing section 11 and the code together (see: A live reason is added or retired only by a change to section 11 and the code together, and the register holds candidates alone). A live reason is retired once its record holds 400 resolved setups, read against the resolved count the run page draws beside it, since no page computes that floor. A candidate standing registered under a name section 11 has come to carry is retired like any other, which is how a promoted candidate leaves the family; no candidate can be promoted yet, because nothing computes a candidate's record (owes: A candidate's shadow record computed, and a promotion written against it). A change is a retirement and a new registration, under the same name or another, and a name registered again stands once:
Why: the operator reads here what a registration is refused for and how a live reason leaves, and the old paragraph named a floor with nowhere it shows.

### 2026-09-18 - .claude/rules/checks.md - architecture-conformance says what its mapping reads, and register-append-only and listings-coverage what they now assert

Corrects: the architecture-conformance row said each 13.3 clause maps to "a test whose own body exercises the code the clause is enforced by", and the check confirmed a string appeared somewhere in a body with whole-line comments removed; three clauses passed on tests that do not hold them. Found by the phase 8 sign-off review on 2026-09-16.
Was:
> Section 13.3's guardrails are mapped clause by clause, each clause quoted from its cell, to a test whose own body exercises the code the clause is enforced by, with no test holding two; and the loop is shown to have changed nothing over the register's rows and the version windows a whole recorded night ran over on evidence below the minimum |
>
> and a live reason's name is refused both as a registration and as a retirement, the refusal naming the floor section 17 sets before a live condition may be retired;
>
> named as a failure on the stage's own run log row rather than in a note.
Now:
> Section 13.3's guardrails are mapped clause by clause, and subject by subject where a cell lists several, each quoted from its cell and together covering every word of it but its joining words, to a test whose own code, read with its comments and literals set apart, names the shipped member the clause is enforced by, or whose literals name the stored or drawn name it is enforced through, or to the open carried obligation that owes it; no test holds two and no skipped test holds any; whether a mapped test's assertions hold its clause is a reading no scan can make, recorded pair by pair in the entry that last changed the mapping. Section 13.2's rows are each read against the checkpoints of the phase their Phase cell names, by checkpoint title, or against the open operating obligation the cell cites, by its trigger and the surface it is read on. The loop is shown to have changed nothing over the register's rows and the version windows a whole recorded night ran over on evidence below the minimum, the candidate evaluated on every listing row of that night and the version scored on every one |
>
> a registration stating no rule or no test and a retirement stating no evidence are refused, and a registration is stored with the rule, the test and the instant it was given; and a live reason's name is refused as a registration and, where it does not stand registered, as a retirement, the refusal naming the floor section 17 sets before a live condition may be retired, while a candidate standing registered under a name the live reasons carry is retired like any other, so a promoted candidate leaves the family;
>
> named as a failure on the stage's own run log row rather than in a note, and a candidate that fires on every member leaves every row's reasons section 11's six and its fired count theirs.
Why: a roster row that says more than its check reads is the under-reporting the roster exists to prevent, and a limit a check cannot pass is stated beside the check.

### 2026-09-18 - .claude/rules/checks.md - pinned-constants states the figures it reads and the ones it does not

Corrects: the roster row said numeric constants stated in docs match the code constant they describe, and the check read the framework version and the SDK feature band; phase 8's restated figures and seven section 17 rows' figures were read by no test. Found by the phase 8 sign-off review on 2026-09-16.
Was:
> | `pinned-constants` | every CI run | Numeric constants stated in docs match the code constant they describe |
Now:
> | `pinned-constants` | every CI run | The framework version and the SDK feature band the specs state match the build files that set them. Every figure section 17 states in digits in its Value column is held by the code constant or the arithmetic over constants its entry names, read in the order the table states them and in both directions, or is named as a kind no constant holds with the check that kind allows: a measurement the record carries, a checkpoint the record shows, a limit of zero, the index's nominal size the row says the night does not read, or a count a check reaching the row asserts. Every restatement of a figure the check lists, in a spec, a rules file or a decision that stands, agrees with the code, counted per document against a count stated in advance so a sentence reworded out of its pattern fails rather than going unread. A figure section 17 writes in words, and a figure outside section 17 the list does not name, are not read |
Why: a roster row is what `coverage-reported` holds a check to, and this one claimed every constant while its check read two.

### 2026-09-18 - ARCHITECTURE.html - the run page's route reads the register verb's rows as run by hand as well

Corrects: the run page drew the register verb's rows as the night's stages, a registration among its failed stages, found by the phase 8 sign-off review on 2026-09-16.
Was:
> and neither is a day whose only rows are the <code>version</code> verb run by hand,
Now:
> and neither is a day whose only rows are the <code>version</code> or <code>register</code> verb run by hand,
Why: the register verb's rows are a person's commands as the version verb's are, and a registration's outcome is not ok, so the page drew one as a failed stage.

### 2026-09-18 - SCHEMA.md - a register command writes under a run id of its own

Corrects: the register verb's attempts were not all rows on the run log and their run ids could collide, found by the phase 8 sign-off review on 2026-09-16.
Was:
> **A command a person runs through the `version` verb writes under a run id of the verb's name and its instant to the ten-millionth of a second.** Every open, replacement, close and backfill, refused or not, is one row under `rule-versions`, a listing writes none, and the run page draws the rows as run by hand rather than as stages of the night.
Now:
> **A command a person runs through the `version` or `register` verb writes under a run id of the verb's name and its instant to the ten-millionth of a second.** Every open, replacement, close and backfill, refused or not, is one row under `rule-versions`, a listing writes none, every registration and retirement, refused or not, is one row under `candidate-register`, and the run page draws the rows as run by hand rather than as stages of the night.
Why: the register verb's own refusals wrote no row, and its run id, taken to the second, collided with a second command in the same second.

### 2026-09-18 - RUNBOOK.md - a register attempt is one row under a run id of its own, and a command is one form

Corrects: the register verb's refusals were not rows and its forms were read in a fixed order, found by the phase 8 sign-off review on 2026-09-16.
Was:
> Each attempt, refused or not, is a row on the run log under `candidate-register`.
Now:
> Each attempt, refused or not, is one row on the run log under `candidate-register` and a run id beginning `register-`, which the run page draws as run by hand; a command giving both `--candidate` and `--retire`, a flag where a value goes, or a store behind the checkout is refused, the last writing nothing.
Why: the verb's own refusals wrote no row, a retirement given beside a registration dropped the registration without a word, and the run page drew a registration as a failed stage.

### 2026-09-18 - .claude/rules/checks.md - register-append-only holds the register verb's forms and its run log rows

Corrects: no test ran the register verb, found by the phase 8 sign-off review on 2026-09-16.
Was:
> the refusal naming the floor section 17 sets before a live condition may be retired |
Now:
> the refusal naming the floor section 17 sets before a live condition may be retired; and the register verb takes one form at a time, with every attempt, refused or not, one row on the run log under a run id no second command shares and every refusal changing nothing |
Why: the verb was not run by any test, every register test calling the registrar beneath it.

### 2026-09-18 - ARCHITECTURE.html - the rule version scorer's catalogue row names the replacement, the evidence, the New York date and the backfill's night

Authorised by: A rule version change closes the window with the evidence that produced it and opens its replacement in the same write
Was:
> opening and closing a version's window through the worker's <code>version</code> verb and never as a side effect of a night, a version only beside its rule's live window and under the parameter names that rule is replayed from; it stops the night at its own step, naming the rule, where a live rule's parameters or code have moved while a window measuring it is open, and it flags a score a backfill wrote for a night before its version opened as in sample so it counts toward no record
Now:
> opening, replacing and closing a version's window through the worker's <code>version</code> verb and never as a side effect of a night, a version only beside its rule's live window and under the parameter names that rule is replayed from, and a change or a close only with the evidence that produced it (see: A rule version change closes the window with the evidence that produced it and opens its replacement in the same write); it stops the night at its own step, naming the rule, where a live rule's parameters or code have moved while a window measuring it is open; it flags as in sample a score for a session on or before the New York date its version's window opened on, so it counts toward no record (see: A version's score counts only for a session after the New York date its window opened on); and a backfill scores only a night the store computed, at that night's own price scale, keeping every score already stored (see: A backfill scores only a night the store computed, at that night's own price scale, and never rewrites a score already stored)
Why: the row said a close named what replaced it and a backfill flagged a night before its window opened, which the 8.6 correction replaces with a change written with its evidence, a flag read off the New York date and a backfill held to the nights the store computed; each clause cites the decision it rests on, the other two being 'A version's score counts only for a session after the New York date its window opened on' and 'A backfill scores only a night the store computed, at that night's own price scale, and never rewrites a score already stored'.

### 2026-09-18 - ARCHITECTURE.html - section 14's version step flags a score by the New York date its window opened on

Authorised by: A version's score counts only for a session after the New York date its window opened on
Was:
> flagging as in sample any score written for a night before its version's window opened so it counts toward no record.
Now:
> flagging as in sample any score for a session on or before the New York date its version's window opened on so it counts toward no record (see: A version's score counts only for a session after the New York date its window opened on).
Why: the flag compared the session with the window's UTC date, so a window opened on a night's evening after it ran counted the night just read.

### 2026-09-18 - ARCHITECTURE.html - the run page's route opens on no day holding only a command run by hand

Corrects: the run page drew the version verb's rows as the night's stages, found by the phase 8 sign-off review on 2026-09-16.
Was:
> A night on a day with no session is not one it opens on.</p>
Now:
> A night on a day with no session is not one it opens on, and neither is a day whose only rows are the <code>version</code> verb run by hand, whose rows the operational header draws marked as run by hand, apart from the night's own stage time and never among the stages that failed.</p>
Why: the page read the verb's rows as stages of the night: a refusal as a failed stage, a backfill's time inside the night's, and a later command as the night the page opens on.

### 2026-09-18 - ARCHITECTURE.html - the rule versions store row carries a closed window's evidence

Authorised by: A rule version change closes the window with the evidence that produced it and opens its replacement in the same write
Was:
> when its window opened and, once closed, when and what replaced it</td>
Now:
> when its window opened and, once closed, when, the evidence it was closed on and what replaced it where a version did</td>
Why: migration 28 adds the column a close writes its evidence to, and a close with nothing replacing it names no replacement.

### 2026-09-18 - ARCHITECTURE.html - three limits rows name the arithmetic and the queue rather than numbering them

Corrects: the rows numbered the arithmetic as steps 1 to 16 and the queue as step 17 after 8.6 inserted the version step, found by the phase 8 sign-off review on 2026-09-16.
Was:
> 0 in the arithmetic, being steps 1 to 16, with the overnight queue at step 17 carved out by name
> a night's arithmetic, steps 1 to 16, bounded by 5 minutes
> the arithmetic as a whole, steps 1 to 16, bounded by 15 minutes, and the overnight queue at step 17 bounded by its own limit instead
Now:
> 0 in the arithmetic, being every step before the overnight queue, with the queue, the night's last step, carved out by name
> a night's arithmetic, every step before the overnight queue, bounded by 5 minutes
> the arithmetic as a whole, every step before the overnight queue, bounded by 15 minutes, and the queue bounded by its own limit instead
Why: the rows kept the numbering from before the version step was inserted, and a guard now holds every step named by number outside section 14 to none.

### 2026-09-18 - SCHEMA.md - a version row's close writes its evidence, and a change is one write

Authorised by: A rule version change closes the window with the evidence that produced it and opens its replacement in the same write
Was:
> A window is opened by an insert and closed by writing its `closed_at` and `replaced_by`, which is the one field a version row ever changes:
> | `replaced_by` | TEXT | for a closed window, the version that replaced it |
> A version row is refused where a value is one the replay would not apply as given or where every value is its rule's live one (see: A version of a ladder rule is refused at values its replay would not apply as given, or at its rule's live values).
Now:
> A window is opened by an insert and closed by writing its `closed_at`, `evidence` and, for a replacement, `replaced_by`, which is the one change a version row ever takes:
> | `replaced_by` | TEXT | for a window closed by a replacement, the version the same write opened |
> | `evidence` | TEXT | for a closed window, the figures or the reason that closed it, written by the close and required by it; last because SQLite appends |
> A version row is refused where a value is one the replay would not apply as given or where every value is its rule's live one (see: A version of a ladder rule is refused at values its replay would not apply as given, or at its rule's live values). A window is closed only with its evidence, and a version is changed by one write that closes the old window naming the version replacing it and opens that version, the rule's cap counted once the old one is closed (see: A rule version change closes the window with the evidence that produced it and opens its replacement in the same write).
Why: a close took an optional name of what replaced it and no evidence, where section 13 says a rule version change is written with both.

### 2026-09-18 - SCHEMA.md - a version score's flag is read off the New York date, and a backfill keeps what is stored

Authorised by: A version's score counts only for a session after the New York date its window opened on
Was:
> a re-run of a night writes that night's set again, inside the transaction that writes it, which is the update this table declares.
> a score a backfill wrote for a night before its version opened is `in_sample`
> The scorer writes the flag from the window's own `opened_at` against the night being scored, so the classification is arithmetic rather than a caller's claim about itself.
Now:
> a re-run of a night writes that night's set again, inside the transaction that writes it, which is the update this table declares. A backfill writes only the scores not stored yet and keeps the rest (see: A backfill scores only a night the store computed, at that night's own price scale, and never rewrites a score already stored).
> a score for a session on or before the New York date its window opened on is `in_sample`
> The scorer writes the flag from the date in New York the window's own `opened_at` falls on, read through the clock, against the session being scored, so the classification is arithmetic rather than a caller's claim about itself, and a window opened on a session's own evening, after that night was read, counts from the session after it (see: A version's score counts only for a session after the New York date its window opened on).
Why: the flag compared the session with the window's UTC date, and a backfill replaced the score the night had written.

### 2026-09-18 - SCHEMA.md - a command run through the version verb writes under a run id of its own and is drawn as run by hand

Corrects: the version verb's attempts were not all rows on the run log and their run ids could collide, found by the phase 8 sign-off review on 2026-09-16.
Was:
> nothing: the run log's notes named no run a person starts by hand.
Now:
> **A command a person runs through the `version` verb writes under a run id of the verb's name and its instant to the ten-millionth of a second.** Every open, replacement, close and backfill, refused or not, is one row under `rule-versions`, a listing writes none, and the run page draws the rows as run by hand rather than as stages of the night.
> 
> **The overnight queue writes one row under the night's run, and each name it gives a pass is a run of its own.**
Why: the verb's own refusals wrote no row, and its run id, taken to the second, collided with a second command in the same second.

### 2026-09-18 - SCHEMA.md - the facts file's ordering note names the steps rather than numbering them

Corrects: section 14's numbering is held only by section 14 and the night's own list, found by the phase 8 sign-off review on 2026-09-16 among the passages that kept stale numbers.
Was:
> the ordering is already right, since the shortlist is step 12 of the night and the facts file is step 13.
Now:
> the ordering is already right, since the night writes the shortlist before the facts file.
Why: a step's number goes stale the night a step is inserted before it, and a guard now holds every step named by number outside section 14 to none.

### 2026-09-18 - BUILD_PLAN.md - five passages name a night's step rather than numbering it

Corrects: four passages kept section 14's numbering from before the version step was inserted, found by the phase 8 sign-off review on 2026-09-16.
Was:
> so the first scheduled night stopped at step 12
> `docs/RUNBOOK.md` step 8 said
> That row does not reach step 17's free local pass
> and that its calls come from step 17 alone.
> the night of 2026-09-14 took 495 seconds over steps 1 to 16, its level stage
Now:
> so the first scheduled night stopped at the listings step
> `docs/RUNBOOK.md`'s installation steps said
> That row does not reach the overnight queue's free local pass
> and that its calls come from the overnight queue alone.
> the night of 2026-09-14 took 495 seconds over the steps before the close, its level stage
Why: a step's number goes stale the night a step is inserted before it, and a guard now holds every step named by number outside section 14 to none.

### 2026-09-18 - BUILD_PLAN.md - the version bound's operating row fires on nights that replayed a version of each kind

Corrects: the trigger fired on nights that replayed nothing, found by the phase 8 sign-off review on 2026-09-16.
Was:
> 5 scheduled nights at index size carrying the version scorer's step, read on the run page's operational header, which 8.6 fills with that step's own duration.
> its arithmetic is the night of 2026-09-14: 495 seconds over steps 1 to 16,
Now:
> 5 scheduled nights at index size on which the version scorer's step replayed at least one merge distance version and one version of another rule, read on the run page's operational header, which 8.6 fills with that step's own duration and the versions it replayed of each kind, apart from any row a person ran by hand.
> its arithmetic is the night of 2026-09-14: 495 seconds over the steps before the close,
Why: every night carries the step, and one that replayed no version times none, so the trigger counted nights that could not answer its question; the phase 5 sign-off retired a row for the same reason.

### 2026-09-18 - RUNBOOK.md - three passages name the queue and the migration rather than numbering them

Corrects: two passages numbered the queue as step 17 after 8.6 inserted the version step, found by the phase 8 sign-off review on 2026-09-16.
Was:
> The queue runs at step 17 of the same invocation as the night,
> Without one the run fails at step 6 with a restore error that names neither.
> the machine slept, or the night stopped before step 17 |
Now:
> The queue runs as the night's last step, in the same invocation,
> Without one, `tools/migrate` below fails with a restore error that names neither.
> the machine slept, or the night stopped before the overnight queue |
Why: a step's number goes stale the night a step is inserted before it, and a guard now holds every step named by number outside section 14 to none.

### 2026-09-18 - RUNBOOK.md - the version section shows the replacement, the close with its evidence and the backfill's refusals

Authorised by: A rule version change closes the window with the evidence that produced it and opens its replacement in the same write
Was:
> dotnet run --project src/EquityBrief.Worker -- version --rule "the near-exit skip" --close "three typical days" --replaced-by "four typical days"
> 
> A live window is closed only after the versions beside it. `version --backfill 2026-09-14` scores a past night under the windows open now, from that night's own bars and bands, and every score it writes for a night before its window opened is flagged in sample and counts toward no record. A version of the merge distance or of the zone edges is skipped over a band set stored before member sources were written, and the step's run log row counts the name-nights it skipped. Each attempt is a row on the run log under `rule-versions`, with a refusal under the outcome `refused`.
> Close the rule's versions, then its live window, and open them again: the closed rows are kept with what they were opened with.
> close every open window and open them again after the merge.
Now:
> dotnet run --project src/EquityBrief.Worker -- version --rule "the near-exit skip" --replace "three typical days" --with "four typical days" --parameters nearExitInTypicalDays=4 --evidence "the figures that produced the change"
> dotnet run --project src/EquityBrief.Worker -- version --rule "the near-exit skip" --close "four typical days" --evidence "the figures that produced the close"
> dotnet run --project src/EquityBrief.Worker -- version --backfill 2026-09-14
> 
> A live window is closed only after the versions beside it. `--list` names the open windows with each rule's count against its cap. A change of version is one command, `--replace`, which closes the old window with the evidence that produced the change and the name of the version replacing it and opens that version in the same write, so a rule at its cap can still be changed; `--close` ends a window with nothing replacing it and takes its evidence too (see: A rule version change closes the window with the evidence that produced it and opens its replacement in the same write). A command takes one form: a second form, a flag its form does not take, or a flag where a value goes is refused rather than read as what was probably meant.
> 
> `version --backfill 2026-09-14` scores a past night under the windows open now, from that night's own bars, bands and trend at that night's own price scale, and keeps every score already stored (see: A backfill scores only a night the store computed, at that night's own price scale, and never rewrites a score already stored). It is refused for a day the exchange did not trade, for a session the store holds no bar for, which is every date after the newest session it holds, and for a session no night computed, which is one fetched by a backfill or as a missed session; a name that night computed no bands or plan for is left out and counted. A score counts only for a session after the date in New York its window opened on, and every other score is flagged in sample and counts toward no record (see: A version's score counts only for a session after the New York date its window opened on). A version of the merge distance or of the zone edges is skipped over a band set stored before member sources were written. Its line says what it wrote, kept, left out, skipped and dropped.
> 
> Every open, replacement, close and backfill, refused or not, is one row on the run log under `rule-versions`, a refusal under the outcome `refused`, and `--list` writes none. The rows are under a run id beginning `version-`, and the run page draws them as run by hand, apart from the night's own stages. A command against a store behind this checkout is refused and writes nothing, the run log included: run `tools/migrate` first.
> Close the rule's versions and then its live window, each with `--close` and evidence naming the hash the night's row gives, open them again, and re-run the night, with `--session` once it is past midnight in New York: a night, a replayed one included, scores under the windows the store holds open when its step runs. The closed rows are kept with what they were opened with.
> close every open window with its evidence and open them again after the merge.
Why: a close took an optional name of what replaced it and no evidence, a backfill took any date and replaced the scores the night had written, and a replayed night read the windows open at the replayed instant.

### 2026-09-18 - RUNBOOK.md - the morning table names a store behind the checkout, and a migration comes before the screens and the verbs

Corrects: the screens and the version verb threw on a store behind the checkout, found by the phase 8 sign-off review on 2026-09-16.
Was:
> nothing: the table had no row for a store behind the checkout, and the maintenance note said a migration is never applied at start-up without saying when to run one.
Now:
> | A screen says the store is at one schema and the checkout reads another | the checkout was updated with a migration that neither `tools/migrate` nor a night has applied | run `tools/migrate`. The night applies it at its first step as well; until one has, the screens name both schema numbers and the run page draws only its run log, and the `version` verb refuses and writes nothing |
> | The run page says the queue did not run |
> and a stage failing on a missing column is how that is discovered. After updating the checkout, run `tools/migrate` before starting the read surface or running a verb; the screens and the loop's verbs name a store behind the checkout rather than migrate it.
Why: every screen and the version verb threw on a store behind the checkout, where they now name both schema numbers and refuse.

### 2026-09-18 - .claude/rules/checks.md - component-access reads a verb however the specs write it

Corrects: the verb reader read only `<code>x</code> verb` in the architecture, found by the phase 8 sign-off review on 2026-09-16.
Was:
> and every worker verb a catalogue row names is one the worker dispatches and the runbook shows, with every verb the worker dispatches named in the help it prints
Now:
> and every worker verb the architecture, the schema or the build plan names, in backticks, code markup or bare and as a verb or a command, is one the worker dispatches and the runbook shows, with every verb the worker dispatches, an arm matching several included, named in the help it prints
Why: the reader matched one markup in one document and one arm shape, so a verb written any other way went unread.

### 2026-09-18 - .claude/rules/checks.md - architecture-conformance holds a second check a row names to the row

Corrects: a check named in a row was held only to a mention in the verdict's note, found by the phase 8 sign-off review on 2026-09-16.
Was:
> and a check a row names in its own words is the check its verdict names or is named in that verdict's note.
Now:
> and a check a row names in its own words, in backticks, code markup or bare, is the check its verdict names or one that declares reach over the row, holds it with a test of its own that runs, is named in the verdict's note and fails the row where it fails, while a check-shaped name in a table that the roster does not carry fails.
Why: a mention in the note stood for the named check holding the row, and a name in code markup or one the roster lacks went unread.

### 2026-09-18 - .claude/rules/checks.md - nightly-run runs a night over a moved live rule and holds step numbers to section 14

Corrects: nightly-run passed two claims it never asserted, found by the phase 8 sign-off review on 2026-09-16.
Was:
> is refused before anything is stored |
Now:
> is refused before anything is stored. The rule version step runs after the arithmetic it replays and before the close, a live rule that moved inside an open window stops the night there naming the rule with nothing scored and no close, and the night after its windows are closed and opened again scores under the new ones; and no document, fixture, script or source names a night's step by its number except section 14's own note and the night's own step list, both held to section 14's order |
Why: the frozen windows row and the version step's stop passed by this check on no test that drifted a window through a night, and stale step numbers had no guard.

### 2026-09-18 - .claude/rules/checks.md - nightly-cost counts a night's requests off the feeds and puts each on its step

Corrects: nightly-cost compared self-reported sums, found by the phase 8 sign-off review on 2026-09-16.
Was:
> sits on step 17's own row or on a pass that row names
> makes the same requests on every step and none on the version step, with a score per version per name,
Now:
> sits on the overnight queue's own row or on a pass that row names
> makes the same requests on every step and none on the version step, counted off the feeds and put on the step each was made on, with a score per version per name and the step saying how many versions of each kind it replayed,
Why: the requests compared were the ones each step reported of itself, the version step's a literal zero.

### 2026-09-18 - .claude/rules/checks.md - rule-versions-scored holds the evidence, the New York date, the backfill's night and the verb's forms

Authorised by: A rule version change closes the window with the evidence that produced it and opens its replacement in the same write
Was:
> A version change closes the window measuring the old rule and opens a new one, with the closed row keeping every column it was opened with so the scores under it stay scores of the rule as it stood;
> with a closed window counting against nothing;
> a live rule whose parameters or code have moved inside an open window is found and one that has not is not, in both directions;
> a backfill replays a past night against that night's own bands and trend; a score written for a night before its window opened is flagged in sample beside one written for a night after it;
> and the `version` verb opens, lists, closes and backfills through the scorer with every refusal a non-zero exit and a row on the run log under its own outcome,
Now:
> A version change is one write that closes the window measuring the old rule with the evidence that produced it and the version that replaced it and opens the replacement, with the closed row keeping every column it was opened with so the scores under it stay scores of the rule as it stood, and a close with no evidence is refused;
> with a closed window counting against nothing and two opens racing for a rule's last window admitting one;
> a live rule whose parameters or code have moved inside an open window is found and one that has not is not, in both directions, over the windows the store holds open;
> a backfill is refused for a day that is not a session, a session the store holds no bar for and a session no night computed, and replays a past night against that night's own bands and trend at that night's own price scale, leaving out a name that night computed nothing for and keeping every score already stored; a score counts only for a session after the New York date its window opened on and is flagged in sample for every other, over constructed instants and the fixture's own night;
> and the `version` verb takes one form at a time, refusing a second form, a flag its form does not take and a flag where a value goes, and opens, replaces, closes and backfills through the scorer, every such attempt, refused or not, one row on the run log under a run id no second command shares, every refusal a non-zero exit with nothing changed, a listing writing no row, and a store behind the checkout refused with nothing written,
Why: the row described the version store and verb as 8.6 built them, which the 8.6 correction replaces.

### 2026-09-18 - CLAUDE.md - a ruling's commit subject and entry opening stated beside the planning pass's

Corrects: the conventions said an entry opening "Not a checkpoint entry" under a ruling's heading lands nothing, and never said that a ruling's entry opens that way or how its commit subject reads, so the 8.2 ruling's entry opened "Built:", its subjects put the word ruling in the checkpoint slot, and the record reads it as building 8.2 a second time. Found by the phase 8 sign-off review.
Was:
> nothing: the conventions stated a planning pass's heading and opening and no ruling's.
Now:
> **A ruling lands nothing, and its commit and its entry say so.** Its commit subject keeps the checkpoint slot for the checkpoint alone, as `Phase 7 / 7.2 - ruling: ...`, and its PROGRESS entry is headed with that checkpoint and the word ruling, as `### 7.2 ruling - ...`, and opens with **"Not a checkpoint entry"**, as a planning pass's does. An entry opening any other way is read as building its checkpoint, so a ruling filed under a checkpoint that has not landed would land it and make every claim owed there due.
Why: the three rulings before the 8.2 ruling took this form by precedent alone, and the opening is what the due-point reader reads to decide whether an entry lands its checkpoint.

### 2026-09-18 - .claude/rules/corpus-edits.md - the conventions CLAUDE.md holds include a ruling's subject and opening

Corrects: the paragraph listed the conventions that stayed in `CLAUDE.md` when the editing conventions moved here, so it named no convention written into `CLAUDE.md` after the move, and a session reading this file for where a convention lives would not find a ruling's. Found while correcting the 8.2 ruling's entry.
Was:
> The conventions that bind a session working anywhere stayed in CLAUDE.md: the prose
> convention, the commit subject, which checkpoint a commit belongs to, the planning pass, and
> anything issued in conversation landing in the repo when it is issued.
Now:
> The conventions that bind a session working anywhere are in CLAUDE.md: the prose
> convention, the commit subject, which checkpoint a commit belongs to, the planning pass, a
> ruling's subject and opening, and anything issued in conversation landing in the repo when it
> is issued.
Why: the paragraph is where a session in `docs/` learns which conventions it will not find in this file, so it names what `CLAUDE.md` holds rather than what stayed there on one day.

### 2026-09-18 - CLAUDE.md - the rules table loads corpus-edits.md for the code and writing-tests.md for the record

Corrects: `corpus-edits.md` loaded only for `docs/**` while it carries the citation forms code writes and the rule that a new component's catalogue row lands in the commit that adds it, and `writing-tests.md` loaded only for the test project while its mutation, survivor and population rules govern a `PROGRESS.md` entry's Mutated and Measured fields. Found by the phase 8 sign-off review.
Was:
> | `.claude/rules/writing-tests.md` | `src/EquityBrief.Tests/**` | how an assertion is written: populations, floors, surfaces, mutation classes, and the two rules specific to this tool |
> | `.claude/rules/corpus-edits.md` | `docs/**` | how the corpus is edited: named decisions, named obligations, deferrals, calendar time, and clean edits |
Now:
> | `.claude/rules/writing-tests.md` | `src/EquityBrief.Tests/**`, `docs/PROGRESS.md` | how an assertion is written: populations, floors, surfaces, mutation classes, and the two rules specific to this tool |
> | `.claude/rules/corpus-edits.md` | `docs/**`, `src/**` | how the corpus is edited: named decisions, named obligations, deferrals, calendar time, and clean edits |
Why: the table is where a session learns which file loads for what, so it states each file's paths as the file's own front matter does, and `stated-counts` holds the two to one another.

### 2026-09-18 - .claude/rules/writing-tests.md - loads for the record's entries as well as the test project

Corrects: the file's front matter scoped it to the test project alone, while its mutation, survivor and population rules govern the Mutated and Measured fields a `PROGRESS.md` entry records. Found by the phase 8 sign-off review.
Was:
> paths: src/EquityBrief.Tests/**
Now:
> paths: src/EquityBrief.Tests/**, docs/PROGRESS.md
Why: a session writing an entry's Mutated and Measured fields is a session these rules bind, and a file scoped to the test project loaded for it only when it happened to open a test first.

### 2026-09-18 - .claude/rules/corpus-edits.md - loads for the code as well as the documents

Corrects: the file's front matter scoped it to `docs/**` alone, while the decision and obligation citation forms it gives are written in code and a new component's catalogue row is owed by the commit that adds the component in `src/`. Found by the phase 8 sign-off review.
Was:
> paths: docs/**
Now:
> paths: docs/**, src/**
Why: a session writing a `// see:` or `// owes:` line or adding a component is working in `src/`, and a file scoped to `docs/**` never loaded for it.

### 2026-09-18 - .claude/rules/checks.md - stated-counts holds CLAUDE.md's rules table to the directory

Corrects: `CLAUDE.md` names four rules files in a table, with the paths each loads for, and states the count twice, and nothing compared any of it with the directory, so a fifth file or a renamed one left every check green and the table stale. Found by the phase 8 sign-off review.
Was:
> | `stated-counts` | every CI run | Every count a spec states about itself matches the derived count. Record entries are dated measurements and are exempt |
Now:
> | `stated-counts` | every CI run | Every count a spec states about itself matches the derived count, and the rules files `CLAUDE.md`'s read order names, with the paths each loads for, are the files the rules directory holds and the paths each one's front matter states. Record entries are dated measurements and are exempt |
Why: a session learns a rules file exists from that table, and a file the table does not name loads only for a session that already reads under its paths.

### 2026-09-18 - BUILD_PLAN.md - two pointers name the rules file that now holds the rule

Corrects: two passages still sent a reader to `CLAUDE.md` for rules the 8.2 ruling moved into `.claude/rules/`, where a search of `CLAUDE.md` finds nothing. Found by the phase 8 sign-off review.
Was, in the holes table's base rate row:
> on `CLAUDE.md`'s own rule that a figure over listed names only is a figure over the wrong population.
Was, in 5.7:
> `CLAUDE.md`'s deferral convention already names that failure: a deferral fails **never**, where the named point is a report or a page rather than the thing that measures.
Now:
> on the rule in `.claude/rules/writing-tests.md` that a figure over listed names only is a figure over the wrong population.
>
> The deferral convention in `.claude/rules/corpus-edits.md` already names that failure: a deferral fails **never**, where the named point is a report or a page rather than the thing that measures.
Why: a pointer to the file a rule left reads as the rule having been dropped.

### 2026-09-18 - CLAUDE.md - the layout block's rules row and .gitignore clause, recorded after the edit

Corrects: the prior text was changed with no entry recording it. The 8.2 ruling's commit e8bc531 added the `.claude/rules` row and a clause on the `.gitignore` line, and none of that commit's entries names the layout block. Found by the phase 8 sign-off review.
Was:
> CLAUDE.md         these rules, read first every session
> source-lists.json the two open-web lists a research search may return, with their review date
>
> .gitignore        the store, the prompts archive, the harness output, the secrets
>                   files and the local harness settings
Now:
> CLAUDE.md         these rules, read first every session
> .claude/rules     four path-scoped rules files, tracked, holding this file's own
>                   reference material where it loads for the session that needs it
> source-lists.json the two open-web lists a research search may return, with their review date
>
> .gitignore        the store, the prompts archive, the harness output, the secrets
>                   files and the local harness settings, less `.claude/rules`,
>                   which is tracked
Why: the layout block is where a reader learns what each root path is, and the rules directory is tracked under a carve-out from the harness settings the `.gitignore` line names.

### 2026-09-18 - SCHEMA.md - break_even and return_pct state the range an entry is admitted in

Authorised by: An entry is admitted only inside the plan's own range, and a plan with no entry zone takes its whole range as its zone
Was:
> Five of the 93 setups the operator's store had resolved on 2026-09-16 were that shape, which is why the figures computed over these rows state the population they were computed over rather than counting every resolved row.
Now:
> Five of the 93 setups the operator's store had resolved on 2026-09-16 were that shape, which is why the figures computed over these rows state the population they were computed over rather than counting every resolved row. An entry is admitted only inside the plan's own range, from its stop to the lower of its entry zone's top edge and its target, so no entry sits where the share is undefined (see: An entry is admitted only inside the plan's own range, and a plan with no entry zone takes its whole range as its zone).
Why: the paragraph states a property of every row, and it holds on every path the arithmetic takes only because the decision narrows where an entry is admitted.

### 2026-09-18 - ARCHITECTURE.html - section 17's minimum and record display rows and 15.11's below row name the set a reason is counted over

Authorised by: A reason's share, verdict and both floors are counted over the resolved setups that set a bar
Was:
> in section 17's Minimum resolved setups row: 250 resolved setups spread over at least 60 distinct listing sessions, each contributing at least one, before a verdict is reported at all; 400 before a live condition may be retired (see: An unresolved setup is never a win)
>
> in section 17's Reason record display row: below the minimum only the resolved count against the minimum is shown;
>
> in 15.11's Below the minimum row: a dashed outline carrying the resolved count against the minimum, and no rate
Now:
> the minimum reads "250 resolved setups that set a bar, spread over at least 60 distinct listing sessions, each contributing at least one", and cites the decision beside the one it already cited; the record display row reads "below the minimum only the count of resolved setups that set a bar against the minimum is shown"; and 15.11's row reads "a dashed outline carrying the count of resolved setups that set a bar against the minimum, and no rate".
Why: the verdict counted unresolved setups toward both floors while the page's gate counted losses that set no bar, so the rows a reader checks a verdict against named no set while two different sets were being counted against them.

### 2026-09-18 - BUILD_PLAN.md - 8.5's minimum no longer called proposed, and named over the set it counts

Corrects: 8.5's text called the minimum "marked proposed", where the decision that rules it at 8.0 and section 17's row state it unmarked and no carried row settles it, and it named no set for the count. Found by the phase 8 sign-off review, reading the three against each other.
Was:
> Below the minimum nothing is shown but the count against it, and the minimum is 250 resolved setups spread over at least 60 distinct listing sessions, each contributing at least one, marked proposed: sixty is the quarter of trading the bands look back over and the threshold calibration already uses, and a count of rows alone can be filled by a handful of nights of one market move.
Now:
> the minimum is "250 resolved setups that set a bar, spread over at least 60 distinct listing sessions, each contributing at least one", citing the decision that names the set, and "Both figures are ruled with the verdict rather than proposed", before the same reasoning for sixty.
Why: section 17's preamble requires a proposed value to be settled before the phase that depends on it, and the verdict decision settled both figures in the planning commit that left the marker, so the marker had nothing behind it and no row to end it.

### 2026-09-18 - BUILD_PLAN.md - the operating row for the three reason records reads the count the page draws

Authorised by: A reason's share, verdict and both floors are counted over the resolved setups that set a bar
Was:
> 250 resolved setups, which is the minimum section 17 already states, read on the run page, which 5.6 builds and 8.5 fills with verdicts.
Now:
> 250 resolved setups that set a bar, which is the minimum section 17 already states, read on the run page, which 5.6 builds and 8.5 fills with verdicts.
Why: the trigger is read off the dashed count on the run page, and that count is of the resolved setups that set a bar.

### 2026-09-17 - ARCHITECTURE.html - a figure's rows fit the column they are drawn in

Corrects: figure 5.1's outside-the-system row held eight boxes on a line the stylesheet gave no way to wrap, needing 1143px of the 1024px the column holds, and a figure is a scroll box, so the surplus was clipped rather than shown and the language models box lost most of its text where nobody could reach it. The row fit at seven boxes with 25px to spare and crossed the column when 4.0 added the earnings calendar feed; found by the operator reading section 5 on 2026-09-17.
Was:
>   .box{background:var(--boxbg);border:1px solid var(--rule);border-left:5px solid var(--faint);padding:8px 10px;min-width:135px;font-size:12.8px;line-height:1.36;flex:1}
Now:
> the same declaration at `flex:1 1 210px`, a rule wrapping a row whose boxes carry no arrow between them, and a narrow-width rule that stacks every row and turns its arrows down.
Why: a row whose boxes carry no arrow is a set of peers and nothing is lost by drawing it on two lines, so it wraps and can hold any number of boxes. A row with arrows reads in one direction and is turned on its side below the width its boxes need rather than wrapped, because wrapping a sequence runs it left to right and then starts again. `figure-fits` reads both rules and every row's width off this stylesheet, so a sequence that outgrows the column fails at the commit that adds the box.

### 2026-09-17 - ARCHITECTURE.html - the marketing marker's two counts stated in digits

Corrects: section 17's source admissibility row stated the regulatory risk warning that refuses a page and the invitation a leveraged product must sit beside in words, no constant held either, and the claim's verdict note said three of the row's numbers were read where its test read two; found by the phase 8 sign-off review on 2026-09-16.
Was:
> and a page exists to open an account where it carries one regulatory risk warning, or a leveraged-product term beside an invitation to open one.
Now:
> and a page exists to open an account where it carries at least 1 regulatory risk warning, or a leveraged-product term beside at least 1 invitation to open one.
Why: both counts are now constants the rule reads, and the row's test reads each figure off the row against its constant, so a row edited to another count fails it.

### 2026-09-17 - ARCHITECTURE.html - the weighted-call budget's example restated as the count its test makes

Corrects: section 17's weighted-call budget gave a night counted in requests as four where the provider says two hundred and twelve, written at 2.4 when four endpoints existed, where the night's record now composes six feed roles, and no test read either figure; found by the phase 8 sign-off review on 2026-09-16.
Was:
> a request is not a request, so a night counted in requests alone says four where the provider says two hundred and twelve.
Now:
> a request is not a request, so a night counted in requests alone says 7 where the provider says 317, over one request from each feed role and the corporate action feed's second.
Why: the example is now the count the weighted total test makes over every feed role, read off the row by that test, so it moves when a role joins the night.

### 2026-09-17 - CLAUDE.md - code a planning pass or a ruling carries meets a checkpoint's done conditions

Authorised by: Code a pass that lands no checkpoint carries into shipped source meets the done conditions a checkpoint's code meets
Was:
> **The pass that plans a phase belongs to the phase it plans, at that phase's opening checkpoint.** `Phase 2 / 2.0` for the pass that writes phase 2's section, not `Phase 1 / 1.8`. A PROGRESS entry for such a pass is headed with that checkpoint and the word planning, as `### 2.0 planning - ...`, and opens with **"Not a checkpoint entry"**, because it lands that checkpoint and never its phase: planning a phase builds none of it. An entry opening that way under any other heading, a ruling among them, lands nothing.
Now:
> **The pass that plans a phase belongs to the phase it plans, at that phase's opening checkpoint.** `Phase 2 / 2.0` for the pass that writes phase 2's section, not `Phase 1 / 1.8`. A PROGRESS entry for such a pass is headed with that checkpoint and the word planning, as `### 2.0 planning - ...`, and opens with **"Not a checkpoint entry"**, because it lands that checkpoint and never its phase: planning a phase builds none of it. An entry opening that way under any other heading, a ruling among them, lands nothing. Code such a pass or a ruling carries into shipped source meets the done conditions a checkpoint's code meets, and a correction to it is labelled for the checkpoint the pass belongs to (see: Code a pass that lands no checkpoint carries into shipped source meets the done conditions a checkpoint's code meets).
Why: the pass that planned phase 8 carried code whose entry owed no expectation and no Windows record any check read, and three of the phase 8 sign-off review's findings arrived through it.

### 2026-09-17 - .claude/rules/checks.md - two-platform reads every entry since the hosted Windows leg was removed

Authorised by: Every entry written since the hosted Windows leg was removed records the Windows run, whatever the entry lands
Was:
> | `two-platform` | every CI run | The suite passes on both platforms, macOS on a hosted runner and Windows on the operator's machine (see: Windows is verified on the operator's machine, and the hosted runners are macOS and Linux): the workflow runs `tools/ci.sh` on macOS and names no Windows runner, and every checkpoint entry written since the hosted Windows leg was removed says `tools/ci.ps1` ran green, read off the record by a reader shown to find an entry that does not. No leg can report green without running the suite: the workflow carries zero YAML condition keys, counted rather than blocklisted, because a leg is skipped at runtime by any condition at all and a skipped job leaves its run green |
Now:
> | `two-platform` | every CI run | The suite passes on both platforms, macOS on a hosted runner and Windows on the operator's machine (see: Windows is verified on the operator's machine, and the hosted runners are macOS and Linux): the workflow runs `tools/ci.sh` on macOS and names no Windows runner, and every entry written since the hosted Windows leg was removed, a planning pass, a ruling, a correction and a sign-off among them, says `tools/ci.ps1` ran green, read off the record through the reader `DuePoints` lands checkpoints from and shown to find an entry that does not whatever it lands (see: Every entry written since the hosted Windows leg was removed records the Windows run, whatever the entry lands). No leg can report green without running the suite: the workflow carries zero YAML condition keys, counted rather than blocklisted, because a leg is skipped at runtime by any condition at all and a skipped job leaves its run green |
Why: a merge landing no checkpoint carried code with no Windows record any check read, and the merge rule names the run for every merge.

### 2026-09-17 - BUILD_PLAN.md - phase 8's pass carries code held to a checkpoint's done conditions

Authorised by: Code a pass that lands no checkpoint carries into shipped source meets the done conditions a checkpoint's code meets
Was:
> **This phase's pass carries repairs.** 8.0 rules on what phase 6 and the phase 7 sign-off left open, and several of those rulings are a line of code rather than a sentence, so the pass that plans this phase edits shipped source (see: The pass that plans phase 8 carries the phase 7 sign-off's repairs and its rulings' code). It builds no checkpoint of this phase.
Now:
> **This phase's pass carries repairs.** 8.0 rules on what phase 6 and the phase 7 sign-off left open, and several of those rulings are a line of code rather than a sentence, so the pass that plans this phase edits shipped source (see: The pass that plans phase 8 carries the phase 7 sign-off's repairs and its rulings' code). It builds no checkpoint of this phase, and the code it carries meets the done conditions a checkpoint's code meets (see: Code a pass that lands no checkpoint carries into shipped source meets the done conditions a checkpoint's code meets).
Why: the exception said what it did not license and not what its code owed.

### 2026-09-17 - BUILD_PLAN.md - 8.0's segment ruling says what the commentary is asked and refused over a longer period

Authorised by: A segment figure held for a period longer than a quarter is asked for by that period and refused where its sentence names a period of another length
Was:
> **Rules the two things 6.11's production run found and left as they were.** A pass for a name with no facts file refreshes its industry's theme, because the theme is the industry's and every member reads it, and the name page says what the pass did instead of saying it did not run (owes: A name with no facts file refreshes its industry's theme, ruled) (see: A pass for a name with no facts file refreshes its industry's theme and says so). And a facts file carries the latest period of the segment table the archive supplies, twelve-month figures named by their months where the newest filing files no quarter, while a filing stored before the archive could be read keeps its parts unread until a newer filing arrives, which is stated as the limit it is rather than repaired by re-reading a stored filing (owes: The segment figures a facts file carries after an annual filing or an archive the fetch could not read) (see: A facts file carries the latest period of the segment table, and says which period it is).
Now:
> **Rules the two things 6.11's production run found and left as they were.** A pass for a name with no facts file refreshes its industry's theme, because the theme is the industry's and every member reads it, and the name page says what the pass did instead of saying it did not run (owes: A name with no facts file refreshes its industry's theme, ruled) (see: A pass for a name with no facts file refreshes its industry's theme and says so). And a facts file carries the latest period of the segment table the archive supplies, twelve-month figures named by their months where the newest filing files no quarter, while a filing stored before the archive could be read keeps its parts unread until a newer filing arrives, which is stated as the limit it is rather than repaired by re-reading a stored filing (owes: The segment figures a facts file carries after an annual filing or an archive the fetch could not read) (see: A facts file carries the latest period of the segment table, and says which period it is). Over such a table the segment commentary is asked for the period its figures cover, and a figure held for that period alone is refused in a sentence naming a period of another length (see: A segment figure held for a period longer than a quarter is asked for by that period and refused where its sentence names a period of another length).
Why: the ruling carried a year's figures under a prompt asking for a quarter, and nothing read the months in their names.

### 2026-09-17 - BUILD_PLAN.md - 8.0's section and its carried row count ten counting notes, and state two-platform's population and the started-phase set as they are asserted

Corrects: the section and the carried row counted nine notes stating a count of their row's parts where ten did, the section said two-platform reads the entries that land a checkpoint where a planning pass had carried code under an entry that lands none, and called the started-phase guard derived where one phase before the newest could stop being read with the suite green; found by the phase 8 sign-off review on 2026-09-16.
Was:
> **Rules how a count a verdict note states is kept to the row it describes** (owes: A count a verdict note states read off the row it describes). A verdict note states no count of the row's own parts: what it says is what the check asserts, and the count lives in the row, which is the only place that cannot go stale (see: A verdict note states no count of the row's own parts). Read over all 346 notes the harness holds, 334 in the scope map and 12 in the placements, nine state such a count and are rewritten without it, being the three matrix notes, four catalogue notes counting the stores a component declares and two store notes counting SCHEMA's columns. Sixty-one others carry a number, and those are rule figures their row itself states or populations of the fixture, which the claim's own check reads. A guard refuses the shape from here.
>
> **Carries the phase 7 sign-off's items, each closed here rather than entered.** The candidate register starts at one checkpoint, 8.3, where the migration creates it, so the roster row, the three placements, the two comments and the page's own line say 8.3 or the checkpoint that builds what they name. `two-platform` reads the record's entries through the same heading form `DuePoints.Built` lands a checkpoint from, so a heading without a dash cannot land a checkpoint and owe no Windows record. The started-phase guard is derived from the plan and the record rather than floored at five. A `--session` older than the newest session the store holds is refused at the argument, since the corporate action refetch on that path drops a due name's newer bars, which this pass traced. The two thin tests are widened: the weekly retry's midnight case pins tonight's session as well as the last asked, and a suspect name the index no longer holds is read on all three of its surfaces. The wording the phase left behind is corrected in `BUILD_PLAN.md`, `CLAUDE.md` and `RUNBOOK.md`, with the prior text in `CHANGELOG.md`.
>
> | **A count a verdict note states read off the row it describes** | 7.0 ruling | 8.0, discharged | ruled (see: A verdict note states no count of the row's own parts). A note says what the check asserts and states no count of the row's cells, stores or columns, and a guard refuses the shape. Read over all 346 notes, being 334 in the scope map and 12 in the placements, nine stated such a count and are rewritten without it: the three matrix notes, four catalogue notes counting the stores a component declares, and two store notes counting SCHEMA's columns. Sixty-one other notes carry a number, and each is a rule figure the row itself states or a population of the fixture, which the claim's own check reads. What it read before: a verdict note in the harness's placement map states counts about the row it passes, being its cells, its reads and its stores, and each count is typed into the note and read against nothing, so the phase report prints it whatever the row holds. The 7.0 ruling found the read API's matrix note at eleven reads over a row its series state read had made twelve and corrected it, and found the mark renderer's and the single page app's notes at all eleven cells blank over rows of thirteen, which 7.2 corrects. What 8.0 has is those three notes and the rows they describe, and what it rules is whether a note's count is read off the row, written without a count, or stated as the limit it is |
Now:
> **Rules how a count a verdict note states is kept to the row it describes** (owes: A count a verdict note states read off the row it describes). A verdict note states no count of the row's own parts: what it says is what the check asserts, and the count lives in the row, which is the only place that cannot go stale (see: A verdict note states no count of the row's own parts). Read over all 346 notes the harness holds, 334 in the scope map and 12 in the placements, ten state such a count and each is rewritten without it, being the three matrix notes, four catalogue notes counting the stores a component declares, one catalogue note counting the feeds its component declares and two store notes counting SCHEMA's columns. Sixty-one others carry a number, and those are rule figures their row itself states or populations of the fixture, which the claim's own check reads. A guard refuses the shape from here, a count beside feeds among it.
>
> **Carries the phase 7 sign-off's items, each closed here rather than entered.** The candidate register starts at one checkpoint, 8.3, where the migration creates it, so the roster row, the three placements, the two comments and the page's own line say 8.3 or the checkpoint that builds what they name. `two-platform` reads every entry written since the hosted Windows leg was removed, whatever it lands, split out of the record by the reader `DuePoints` lands checkpoints from, so no entry carries code that owes no Windows record (see: Every entry written since the hosted Windows leg was removed records the Windows run, whatever the entry lands). The started-phase guard is derived from the plan and the record rather than floored at five, as the exact set: every phase before the newest whose opening has landed has started and landed, and that newest one alone may be planned and not started. A `--session` older than the newest session the store holds is refused at the argument, since the corporate action refetch on that path drops a due name's newer bars, which this pass traced. The two thin tests are widened: the weekly retry's midnight case pins tonight's session as well as the last asked, and a suspect name the index no longer holds is read on all three of its surfaces. The wording the phase left behind is corrected in `BUILD_PLAN.md`, `CLAUDE.md` and `RUNBOOK.md`, with the prior text in `CHANGELOG.md`.
>
> | **A count a verdict note states read off the row it describes** | 7.0 ruling | 8.0, discharged | ruled (see: A verdict note states no count of the row's own parts). A note says what the check asserts and states no count of the row's cells, stores, feeds or columns, and a guard refuses the shape. Read over all 346 notes, being 334 in the scope map and 12 in the placements, ten stated such a count and are rewritten without it: the three matrix notes, four catalogue notes counting the stores a component declares, one catalogue note counting the feeds its component declares, and two store notes counting SCHEMA's columns. Sixty-one other notes carry a number, and each is a rule figure the row itself states or a population of the fixture, which the claim's own check reads. What it read before: a verdict note in the harness's placement map states counts about the row it passes, being its cells, its reads and its stores, and each count is typed into the note and read against nothing, so the phase report prints it whatever the row holds. The 7.0 ruling found the read API's matrix note at eleven reads over a row its series state read had made twelve and corrected it, and found the mark renderer's and the single page app's notes at all eleven cells blank over rows of thirteen, which 7.2 corrects. What 8.0 has is those three notes and the rows they describe, and what it rules is whether a note's count is read off the row, written without a count, or stated as the limit it is |
Why: a count read by the guard's own words misses what those words miss, and a population chosen by what an entry is called moves when the entry is called something else.

### 2026-09-17 - ARCHITECTURE.html - section 12.2's segment row says the latest period, what the model is told and what it is refused

Corrects: the row said code works out the latest quarter and the model writes what each unit reported for the quarter, where 8.0 carried an annual report's twelve-month figures; found by the phase 8 sign-off review on 2026-09-16.
Was:
>   <tr><td>The segment commentary</td><td>local</td><td>the latest quarter of the segment table, read out of the filing into the facts file as one figure per business unit and line item, and the filing it came from</td><td>one sentence per business unit, saying what it reported for the quarter</td><td>every figure is a segment figure the facts file holds; every sentence cites the filing</td></tr>
Now:
>   <tr><td>The segment commentary</td><td>local</td><td>the latest period of the segment table, being the latest quarter where the filing files one and otherwise its newest longer period, named by its months, read out of the filing into the facts file as one figure per business unit and line item, and the filing it came from (see: A facts file carries the latest period of the segment table, and says which period it is)</td><td>one sentence per business unit, saying what it reported for that period, and told to name a longer period by its months and never as a quarter, a half or any other period</td><td>every figure is a segment figure the facts file holds, and a figure held for a period longer than a quarter alone is refused in a sentence naming a period of another length (see: A segment figure held for a period longer than a quarter is asked for by that period and refused where its sentence names a period of another length); every sentence cites the filing</td></tr>
Why: the design source of truth described a quarter where code carried a year, and the prompt said the same word for word.

### 2026-09-17 - BUILD_PLAN.md - phase 8's section as the plan rewrite found it, quoted where the entry for that rewrite summarised it

Corrects: the entry below headed "phase 8's section written as the loop it builds, with 8.0's rulings and the model's proposal dropped" gave a summary where the format asks for the prior text verbatim, so whether a done condition narrowed when the plan was rewritten could be read only from the history; found by the phase 8 sign-off review on 2026-09-16.
Was:
> ### 8.0 Planning
> Confirms the guardrail values against the evidence that has accumulated. Nothing here is tuned to what the data turned out to be; a threshold changed after seeing results starts a new window and the old one is kept.
>
> Decides the fundamental analysis, being four readings over the twelve stored quarters and the three questions about what they are for: the computed trajectory, the guide record, earnings quality and the valuation position, each with the range it is placed in and the count of quarters behind it, and then whether the four earn a panel, whether a state transition on one of them earns a seventh reason, and whether a computed fundamental state may gate a tranche (owes: The computed fundamental panel, and whether a fundamental state may fire a reason or gate a tranche). The evidence is in hand by this pass rather than produced by it, since 6.1 stores the filings and the research checkpoints write the first reports to read a panel against, which is why 6.1 filed the question rather than answering it.
>
> Narrows what the volume profile is allowed to claim, before anything scores it (owes: The volume profile's claim narrowed to a price region at a day's resolution). Its evidence is in hand as well: the profile has been stored since 3.3 over four names of different character, and the sessions a multiple of the typical daily move sets aside are countable off the stored bars.
>
> Rules two things 6.11's production run found and left as they were, because each is a ruling rather than a repair: whether a pass for a name with no facts file refreshes its industry's theme (owes: A name with no facts file refreshes its industry's theme, ruled), and what a facts file carries of a segment table after an annual filing or an archive the fetch could not read (owes: The segment figures a facts file carries after an annual filing or an archive the fetch could not read). Their evidence is in hand rather than produced by this pass: the run's passes on the fixture's names, recorded in `PROGRESS.md`, and the code that decides both.
>
> Rules how a count a verdict note states is kept to the row it describes (owes: A count a verdict note states read off the row it describes). The read and write matrix's note for the read API said eleven reads over a row the 7.0 ruling's series state read had made twelve until that ruling corrected it, and the notes for the mark renderer and the single page app say all eleven cells are blank over rows of thirteen, which 7.2 corrects, because a count typed into a note is read against nothing and the phase report prints it whatever the row holds. The evidence is in hand rather than produced by this pass: the three notes and the rows they describe.
>
> **Decides what the momentum panel is for, rather than carrying it.** It reaches no decision, feeds no band and gates nothing, which makes it the only part of the technical half with no stated purpose. Either the corpus says it is context a reader weighs and that nothing computes with it, which is a sentence in section 15 and a claim the panel's own row can carry, or it is given a job, which is a component that reads it and a rule that says what it decides. It is decided here rather than filed as an obligation because nothing produces evidence for it: the panel has been drawn since 3.5 and the question is what the corpus intends, not what a measurement would show.
>
> **Rules the two conventions the loop's arithmetic rests on, rather than opening rows that wait on nothing.** The family's maximum size and the significance level are conventions, and no count of resolved setups measures either: the arithmetic that sets them is the one section 13 already states, where five worthless candidates clear an ordinary test about 23 per cent of the time and twenty clear it about 64. Both are decided here with that reasoning, and section 17's rows state decided values (see: The candidate family is at most eight and the threshold is divided by it) (see: A verdict tests a reason's wins against each of its setups' own break-even at a corrected threshold). The version bound is the one figure of the three a measurement settles, and it opens an operating row, because the scorer's own nights are what time it (owes: The rule version bound set from nights the version scorer ran).
>
> **States what the loop scores, from the store as it stands.** Under the entry rule 8.1 builds, the setup rows the store holds resolve as 42 wins, 51 losses and 105 never entered, against 147 wins and 51 losses as the filler wrote them, over 2,522 listings on seven sessions read on 2026-09-16. Every one of those figures is re-read at the checkpoint that uses it rather than carried from here.
>
> **Settles what the corpus says about a tranche zone's edges.** The trace this pass ran over the fired zones of 2026-09-15 found the merge rule holding: no band's neighbouring anchors sit more than half a typical day's move apart, 223 zones read and the widest gap exactly half. What it also found is that a moving average sets 36 of those zones' 446 edges, where the decision on averages says they do not decide where to buy and section 17's eligibility row says a band keeps its full width. The two are reconciled here in the decision's own words rather than by changing what the builder computes (see: A moving average may widen a band that a tranche sits on, and may not anchor one).
>
> ### 8.1 Setup resolution
> The setup horizon on forward returns: target reached, stop closed through, or the time cap expired. The resolution counts on the run page.
>
> **Done when** each of the three outcomes has its own test, a timed-out setup is counted in its own column and never in a rate, and the counts render.
>
> ### 8.2 The break-even score
> The hit rate each plan demanded, computed from its own entry, stop and target, and the share of setups that beat it.
>
> **Done when** the arithmetic is asserted against worked cases, and a verdict below the minimum is withheld with its count shown against the minimum.
>
> ### 8.3 The candidate register
> Migration creating `candidate_register`, append only. Registration before scoring, with the rule, the test and the date, and a stated maximum family size.
>
> **Done when** an update and a delete are both refused at the store, a retirement is a new row naming what it retires, and the correction divisor matches the rows registered before the window opened.
>
> ### 8.4 The shadow column
> Registered candidates evaluated nightly on every name-night exactly as live reasons are, written to the shadow column and shown nowhere.
>
> **Done when** a shadow candidate is evaluated on nights no live reason fired, which is what the every-name listing row exists for, and nothing shadow reaches any screen.
>
> ### 8.5 Reason verdicts on the run page
> Each reason against the bar its own setups demanded, with the resolved count and the divisor beside it, and the base rate pinned.
>
> **Done when** no verdict appears below its minimum, every verdict names its divisor, and the three display states are each proved.
>
> ### 8.6 Rule versions scored counterfactually
> Each ladder rule a named version, every night scored under every version from stored bars.
>
> **Done when** a version change opens a new window and the previous one is kept, and a rule is not changed while a window measuring it is open.
>
> ### 8.7 The model's proposal
> The model's own entry and exit proposal stored beside the computed one and scored on the same break-even rule, never overriding it.
>
> **Done when** the proposal is stored, scored and displayed as a second opinion, and no computed number is sourced from it.
>
> ### 8.8 Phase 8 report
> **Done when** every guardrail has its own test, and the loop has changed nothing on the strength of evidence below its stated minimum.
Now:
> phase 8's section as the entry below describes it; this entry changes no spec and quotes the text that entry summarised
Why: a summary of removed done conditions cannot be read against the conditions that replaced them.

### 2026-09-17 - ARCHITECTURE.html - section 13's figure, its description and key, and the glossary's Setup row state the entry

Corrects: figure 13.1 drew three outcomes with "Target first / counts as a win", its description said a setup resolves as a win, a loss or unresolved, and its key and the glossary's Setup row held a setup open until the target, the stop or the cap with no entry, where 8.1 made a target reached before the entry never entered and never a win. Found by the phase 8 sign-off review; 8.1's pass over the document edited the two rows its own claims sit on.
Was:
> <path d="M350 168 L350 186 L130 186 L130 194" fill="none" stroke="var(--muted)" stroke-width="1.2" marker-end="url(#ad)"/>
> <line x1="350" y1="168" x2="350" y2="194" stroke="var(--muted)" stroke-width="1.2" marker-end="url(#ad)"/>
> <path d="M350 168 L350 186 L570 186 L570 194" fill="none" stroke="var(--muted)" stroke-width="1.2" marker-end="url(#ad)"/>
>
> <rect x="30" y="196" width="200" height="64" rx="6" fill="var(--panel)" stroke="var(--compute)" stroke-width="1.2"/><rect x="30" y="196" width="5" height="64" fill="var(--compute)"/>
> <text x="130" y="220" text-anchor="middle" dominant-baseline="central" fill="var(--ink)" font-family="Segoe UI, Arial, sans-serif" font-size="14" font-weight="600">Target first</text>
> <text x="130" y="240" text-anchor="middle" dominant-baseline="central" fill="var(--muted)" font-family="Segoe UI, Arial, sans-serif" font-size="12.5">counts as a win</text>
>
> <rect x="250" y="196" width="200" height="64" rx="6" fill="var(--panel)" stroke="var(--check)" stroke-width="1.2"/><rect x="250" y="196" width="5" height="64" fill="var(--check)"/>
> <text x="350" y="220" text-anchor="middle" dominant-baseline="central" fill="var(--ink)" font-family="Segoe UI, Arial, sans-serif" font-size="14" font-weight="600">Stop first</text>
> <text x="350" y="240" text-anchor="middle" dominant-baseline="central" fill="var(--muted)" font-family="Segoe UI, Arial, sans-serif" font-size="12.5">counts as a loss</text>
>
> <rect x="470" y="196" width="200" height="64" rx="6" fill="var(--panel)" stroke="var(--faint)" stroke-width="1.2"/><rect x="470" y="196" width="5" height="64" fill="var(--faint)"/>
> <text x="570" y="220" text-anchor="middle" dominant-baseline="central" fill="var(--ink)" font-family="Segoe UI, Arial, sans-serif" font-size="14" font-weight="600">Neither, 63 sessions</text>
> <text x="570" y="240" text-anchor="middle" dominant-baseline="central" fill="var(--muted)" font-family="Segoe UI, Arial, sans-serif" font-size="12.5">never counts as a win</text>
>
> <path d="M130 260 L130 280 L350 280 L350 296" fill="none" stroke="var(--muted)" stroke-width="1.2" marker-end="url(#ad)"/>
> <line x1="350" y1="260" x2="350" y2="296" stroke="var(--muted)" stroke-width="1.2" marker-end="url(#ad)"/>
> <path d="M570 260 L570 280 L350 280 L350 296" fill="none" stroke="var(--muted)" stroke-width="1.2" marker-end="url(#ad)"/>
>
> <desc>A listing is recorded with its plan, the plan states its own break-even, the setup resolves as a win, a loss or unresolved, and the results are scored against that break-even.</desc>
>
> and the grey box at the right of the row is the case that matters most: a setup that has done nothing yet is neither a win nor a loss. Counting it as either would flatter or punish a condition for a trade that has not happened. It is held open until the target hits, the stop hits, or the time cap expires, and a timed-out setup is reported separately and never as a win.
>
> A setup is resolved when the target is reached, the stop is closed through, or the time cap expires.
Now:
> four outcome boxes, "Target first / after the entry, a win", "Stop first / counts as a loss", "Never entered / target before the entry" and "Neither, 63 sessions / never counts as a win", each joined to the scoring box; the description naming never entered; the key saying a setup starts on the first close at or below its entry zone's top edge and that a target reached before that close is never entered and never a win, with the decision cited; and the glossary's Setup row saying the same.
Why: the figure, its key and the glossary are what a reader of section 13 takes the rule from, and they described the rule 8.1 replaced.

### 2026-09-17 - BUILD_PLAN.md - 8.1's one rewrite says what keeps it one

Authorised by: An outcome once decided is never rewritten, and a setup still in play is scored with its plan scaled by its listing session's adjustment factor
Was:
> The filler rewrites the setup outcomes it has already written, once, and the checkpoint's entry states the counts before and after over the same population. It is the one rewrite: no verdict has been shown and none can be, since the minimum is 250 and the store holds 93 resolved.
Now:
> the same two sentences, the second adding that an outcome once decided is never written again, with the decision cited.
Why: the filler as 8.1 built it wrote every row on every night, so "the one rewrite" was true of no night; the 5.5 correction made it true, and the section now says what it rests on.

### 2026-09-16 - DECISIONS.md - the dated event count and breakout on volume corrected in place at their figures, their surfaces and the lower edge

Corrects: the count's decision named the tonight, run and universe routes as the surfaces that say a session was written before the correction, while the name page and the report exported from it draw the same rows; it gave its removal from earnings soon's record over six sessions after the record had moved to seven; and it said a count of 0 fires without a reason. Breakout on volume's decision gave the 2,018 rows of those six sessions. Found by the phase 8 sign-off review.
Was:
> so 0 is tonight's print, already reported by the time the night runs after the close, and it still fires.
>
> The run page's record counts each of those two reasons only off rows carrying its marker, which removed 99 wins and 23 losses from earnings soon's record and nothing from breakout on volume's, since it never fired, and the tonight, run and universe routes say so beside a session whose rows lack earnings soon's marker.
>
> from 5.4 until its correction it fired on none of the 2,018 rows the operator's store holds, and the suite's recompute read the same role and agreed.
Now:
> the count's entry gives the reason a count of 0 fires, says the calibration of the six reasons sets the lower edge, says a member evaluated over nothing is not counted, states the nights each record stands on, gives the removal over the seven sessions as the store held them after the night of 2026-09-16, being 2,306 fired with 46 won, 142 lost and 97 never entered among them, and names the five surfaces; breakout on volume's gives 2,522 rows; each ends with what it said until this correction.
Why: what each rules is unchanged, so each is corrected rather than superseded, as the caps entry was.

### 2026-09-16 - ARCHITECTURE.html - section 18's row for listings written before the 5.4 correction names the name page, its exported report and the nights each record stands on

Corrects: the row named three routes while its own reason, that a page drawing those rows without the line presents what the defect wrote as that night's reading, covers the name page and the report exported from it, which draw the newest night's rows; and the run page's line saying how many nights the record stands on counted sessions the two corrected records leave out. Found by the phase 8 sign-off review.
Was:
> the rows are kept as written, and the run page's record counts earnings soon and breakout on volume only off rows carrying the value the corrected rule writes (see: Sessions to a dated event are counted on the exchange calendar and never on stored bars)
>
> the tonight, run and universe routes for such a session carry one line saying earnings soon on those rows counted every future print as tonight's, and breakout on volume could not fire
Now:
> the second cell adds "and states the nights each of their records stands on counted the same way"; the third reads "the tonight, run, universe and name routes for such a session, and the report exported from a name page, carry one line saying earnings soon on those rows counted every future print as tonight's, and breakout on volume could not fire"
Why: the scope was narrower than the reason given for it, and a count read as the calibration's trigger was true of four records and not the other two.

### 2026-09-16 - ARCHITECTURE.html - section 17's earnings horizon row says a print dated on the night fires

Corrects: the row justified the horizon as far enough ahead to stage or trim a position before the date, and the reason fires at a count of 0, a print dated on the night, which no reason in the corpus explained. Found by the phase 8 sign-off review's judgement on that edge.
Was:
> about a month of trading, which is far enough ahead that a position can still be staged or trimmed before the date and near enough that the date is worth stating; the worked example sits 20 sessions before its print and applies the rule
Now:
> the same, with "a print dated on the night itself counts 0 and still fires, for the reason its decision gives, and where the lower edge sits is the calibration's to set" before the worked example
Why: the row's rationale read as excluding a case the rule includes, and the decision it cites now gives the reason.

### 2026-09-16 - SCHEMA.md - the listing's reasons note says what a member evaluated over nothing stores

Authorised by: A member the night evaluates over nothing keeps the dated event the calendar holds and says no count was made
Was:
> | `reasons` | TEXT | JSON: each of the six reasons with fired true or false and the values that made it so. From the 5.4 correction earnings soon's values carry `next dated event` and breakout on volume's carry `previous close`, and a row without them was written before it (see: Sessions to a dated event are counted on the exchange calendar and never on stored bars) |
Now:
> the same note, followed by what a member evaluated over nothing stores for earnings soon, with the new decision cited
Why: the path stored not on file for a date the calendar held, and the column note is where a later reader of the stored values looks for what each value means.

### 2026-09-16 - RUNBOOK.md - the row for the line on listings written before the correction names the name page and its exported report

Corrects: the row named the list, the run page and the universe screen, and the name page and its exported report draw the same rows and now carry the same line. Found by the phase 8 sign-off review.
Was:
> | A past night's list, run page or the universe screen carries a line saying its listings were written before a correction | the rows are from before the 5.4 correction, when earnings soon counted stored bars after the night and breakout on volume could not fire | nothing; the rows are kept as written, because a listing records what its night listed, and the run page's record for those two reasons already leaves them out |
Now:
> the first cell adds a name page and its exported report; the third adds that the run page's line states how many nights those two records stand on, and that a name page and its report read the newest night, so their line shows only while the newest night's rows were written before the correction
Why: a symptom the operator can meet on a surface the row did not name is a symptom with no row.

### 2026-09-16 - BUILD_PLAN.md - the calibration row counts each reason's nights over its own record and carries earnings soon's lower edge

Corrects: the trigger counted nights of listings the two corrected records leave out, and earnings soon's firing at a count of 0 was left without a reason or a place to be decided. Found by the phase 8 sign-off review.
Was:
> | **The six reason thresholds calibrated from the nights they fired on** | 5.0 | operating | 60 nights of listings, read on the run page, which 5.6 builds. 60 because it is the quarter of trading the level window already uses, long enough that a distribution of fired counts is not one week's weather. No checkpoint accumulates nights, so 5.6 makes the trigger readable rather than producing it |
Now:
> the trigger adds that each reason's count is the nights its record stands on, stated above the records where it is fewer; and the cell ends with earnings soon's lower edge, what the calibration reads before setting it, the three choices and what moves with it
Why: the review's judgement stood the edge and carried the question to the calibration, and a carried question lives in the row that owns it rather than in a sign-off's prose.

### 2026-09-16 - DECISIONS.md - the rule version caps cited to the operator's third round

Corrects: the entry ruling the rule version caps ended "Ruled at the 8.6 correction", where the caps are the operator's third-round ruling on the phase 8 plan, extended to the fourth rule, and it named neither that ruling nor the two parts the building session added. Found by the phase 8 sign-off review.
Was:
> a count of windows can be read off the verb's own list. Ruled at the 8.6 correction.
Now:
> the entry ends by naming the operator's third-round caps of 2026-09-15 (two of the merge distance, four of where the stop sits and four of the near-exit skip, live windows counted, ten at once and 673 seconds, four of every rule rejected at 969), the zone edges rule taking the ladder-only cap after that round, why three merge distance windows are left to the operating row, and the live window requirement as written at the 8.6 correction.
Why: the decision's own ruling is unchanged, so the entry is corrected rather than superseded, as the posting hour's was, and a supersession would move seven citations for nothing.

### 2026-09-16 - .claude/rules/checks.md - rule-versions-scored states the pin's reach, the refusals, the worked plans and the retention boundary

Corrects: the row said the code version is the pin of the sources a replay runs through while three files were pinned and the live merge distance sat outside them, and it stated nothing about a version's values, the plans a replay stores against a worked figure, member sources, or retention. Found by the phase 8 sign-off review.
Was:
> | `rule-versions-scored` | every CI run | A version change closes the window measuring the old rule and opens a new one, with the closed row keeping every column it was opened with so the scores under it stay scores of the rule as it stood; a version is opened only beside its rule's live window and under the parameter names the rule is replayed from, a live window carries the build's own parameters, and a live window is not closed while a version of its rule is open; the window past each rule's cap is refused and one below admitted, live windows counted, with a closed window counting against nothing; the fullest register the caps admit is projected from the night's own stage durations read off section 17's row and held inside the deadline the night is bounded by; the ladder rules' code version is the pin of the sources a replay runs through, so a live rule whose code moves is found as one whose parameters did; a live rule whose parameters or code have moved inside an open window is found and one that has not is not, in both directions; a version replays a plan the live rule does not produce; a backfill replays a past night against that night's own bands; a score written for a night before its window opened is flagged in sample beside one written for a night after it; and the `version` verb opens, lists, closes and backfills through the scorer with every refusal a non-zero exit and a row on the run log under its own outcome |
Now:
> the row states the pin over every source the live rules and their replay run through, held to the set the compiled code reaches, with exactly one declaring line left out; the replay at the live values reproducing the night's bands and plan; the refusal at unapplied and live values and the named overflow; the fixture's worked plans; the member sources skip; a zone never narrowed to a touch; the backfill's trend; the retention boundary; and the verb listing a drifted live window.
Why: a roster row is what `coverage-reported` holds a check to, and the check now asserts each of these.

### 2026-09-16 - RUNBOOK.md - a version's values, the skip over a band set without sources, and closing windows before a pinned source changes

Authorised by: The ladder rules' code version pins every source a live ladder rule or its replay runs through
Was:
> `zone edges from non-average anchors only` from `zoneEdgesFromNonAverageAnchorsOnly`, where a flag is 1 or 0.
>
> from that night's own bars and bands, and every score it writes for a night before its window opened is flagged in sample and counts toward no record.
>
> Close the rule's versions, then its live window, and open them again: the closed rows are kept with what they were opened with.
Now:
> the values each parameter takes and the refusals at the live values and at a name given twice; the skip of a merge distance or zone edges version over a band set stored before member sources, counted on the step's row; closing a version whose merge distance no price can hold; and a paragraph saying to close every window before merging an edit to a pinned source, with `version --list` naming a drifted live window.
Why: a pinned edit stops the next night with a window open, and the runbook is where the person merging reads what to do first.

### 2026-09-16 - SCHEMA.md - a member's source, a version's values, and where a score's year is counted from

Corrects: `level.members` said each member's kind, price and date while every row since 8.6 carries a source and the rows before carry none; `rule_version.parameters` said nothing about which values are refused; and `version_score`'s year was counted by the scorer from whatever night it scored. Found by the phase 8 sign-off review.
Was:
> | `members` | TEXT | JSON: each member's kind, price and date |
>
> | `parameters` | TEXT | JSON, the values this version is replayed with |
>
> One year retained, dropped by the scorer on the night the rows fall out of the window, at the order of the index times the versions open.
Now:
> the member row names the source, a paragraph says rows stored before carry none and a version that reads members is skipped over them; the parameters row says each value is one the replay applies as given, and the rule_version paragraph gains the refusal at unapplied and live values (see: A version of a ladder rule is refused at values its replay would not apply as given, or at its rule's live values); and the year is counted back from the newest stored session as the bars are, whatever night is scored.
Why: the scorer reads what the member row describes, refuses what the parameters row admits, and drops by the boundary the bars use.

### 2026-09-16 - ARCHITECTURE.html - two catalogue rows read swings, section 13 names the fourth rule and phase 8's stores, and the version bound is held against the deadline

Corrects: the Ladder builder and Rule version scorer rows read swings and did not say so; section 13.2 listed three ladder rules where four carry versions and 13.4 said phase 8 adds no nightly component and no store; and section 17's version bound rested on a 495 second night beside a wall clock row proposing 5 minutes without saying which bounds it. Found by the phase 8 sign-off review.
Was:
> <td>levels, indicators, calendar, bar store</td> and <td>bar store, indicators, levels, ladders, rule versions</td>
>
> the merge distance, where the stop sits, the near-exit skip. Each is a named rule version
>
> Phase 8 adds the scoring, the register, the shadow column and the run page's condition verdicts. Nothing else changes: no new nightly component, no new store, and no part of the reader's experience.
>
> 14 is the sum of the caps. It grows with versions times names and never with a request
Now:
> both reads cells name swings; 13.2's row names the zone edges rule; 13.4 names the rule version scorer and the three stores and says nothing a reader sees changes but the run page; and section 17's row says the night it is projected from already passes the proposed 5 minutes, which that row's own obligation settles, so the bound is held against the deadline.
Why: a declaration, a count of rules and a count of stores the document states are claims a reader acts on, and each was narrower than what phase 8 built.

### 2026-09-16 - SCHEMA.md - which shadow skips fail the listings stage
Authorised by: Only a missing or moved evaluator fails the shadow column, and a name-night without the readings is a counted skip

Was:
> **`shadow_reasons` carries two lists and not one, from 8.4.** A candidate that did not fire and a candidate nothing evaluated are opposite statements: the first is a measurement and the second is a hole in one. Folding the second into the first is how a record of having skipped a name-night stops existing, and the correction later divides by a family whose members are assumed to have been scored throughout. So a candidate the night could not evaluate, because its evaluator's version has moved or because the night computed none of the values it reads, is written into `skipped` with its reason and is named as a failure on the listings stage's run log row rather than being absent.

Now: the paragraph names as a failure only a candidate whose evaluator the code does not carry or whose version has moved, once, and counts a name with no bar, a gap or a reading not available as a skip of that name-night alone that fails nothing.

Why: a skip for a name the night holds no readings for is a fact about that name on that night and fails nothing; only a missing or moved evaluator is a fault.

### 2026-09-16 - SCHEMA.md - the column a listing row written before 8.4 carries
Corrects: the shadow_reasons cell described two lists on every row, and every row written before 8.4 carries an empty candidates list and a note; found by the phase 8 sign-off review's correction reading the operator's store immutable.

Was:
> | `shadow_reasons` | TEXT | JSON: `candidates`, each registered candidate the night evaluated with whether it fired and the values that made it so, and `skipped`, each registered candidate the night could not evaluate with the reason. Written for every member on every night exactly as `reasons` is, and drawn on no screen (see: Candidate conditions are registered before they are scored, and scored in shadow before they are shown) |

Now: the cell adds that a row written before 8.4 carries an empty `candidates` and a `note` naming the checkpoint the register was then due at, in place of `skipped`.

Why: a reader of the column across the store meets both shapes, and the cell is where it is told.

### 2026-09-16 - ARCHITECTURE.html - the shortlist builder's row names which skipped candidates fail its stage
Authorised by: Only a missing or moved evaluator fails the shadow column, and a name-night without the readings is a counted skip

Was:
> and evaluates every candidate standing registered at the instant the night started into the shadow column of the same rows, naming as a failure on its own run log row any registered candidate it could not evaluate</td></tr>

Now: the clause names as a failure only a candidate whose evaluator the code does not carry or has moved, and counts the name-nights skipped for a name with no bar, a gap or a reading not available.

Why: a skip for a name the night holds no readings for is a fact about that name on that night and fails nothing; only a missing or moved evaluator is a fault.

### 2026-09-16 - BUILD_PLAN.md - 8.4's paragraph says what a skipped name-night does
Authorised by: Only a missing or moved evaluator fails the shadow column, and a name-night without the readings is a counted skip

Was:
> the 8.4 paragraph, ending: is not a record of having skipped one.

Now: the paragraph adds that a name-night the night holds no readings for is skipped on its own row, counted on the stage's line, and not a failure.

Why: the paragraph named only the failure, and the counted skip is the other half the ruling states.

### 2026-09-16 - .claude/rules/checks.md - listings-coverage asserts the night's start over a whole night, the counted skips and the worked night
Authorised by: Only a missing or moved evaluator fails the shadow column, and a name-night without the readings is a counted skip

Was:
> | `listings-coverage` | every CI run | A listings row exists for every index member on every night the store holds, whether or not a reason fired, with the fired and the quiet rows partitioning the whole and a member the night computed nothing for still carrying one. From 8.4 it also asserts what that grain exists for: every candidate standing registered at the instant the night started is evaluated into the shadow column of every one of those rows, including the rows no live reason fired on, a candidate registered after the night started is not evaluated by it, and a candidate whose evaluator's version has moved is skipped with its reason on every row and named as a failure on the stage's own run log row rather than in a note. It was rostered from 5.1 until 5.0 moved it, because 5.4 is the checkpoint that creates `listing` and a roster row naming a checkpoint the record shows as landed fails `coverage-reported` |

Now: the row adds the instant asserted over a whole night whose listings stage starts after a registration, a stale or gapped name skipped with its readings unread, each skip counted and failing nothing, and the column over the replayed fixture matching a night worked by hand.

Why: the roster row claimed the instant over a stage that ran alone under a clock that does not move, and named a moved evaluator as the one failure without saying what the other skips do.

### 2026-09-16 - RUNBOOK.md - a registration while a night runs, the region's instant, and which skips fail
Authorised by: Only a missing or moved evaluator fails the shadow column, and a name-night without the readings is a counted skip

Was:
> From the next night every standing candidate is evaluated on every name into the shadow column and shown nowhere; the run page states how many are registered and the divisor that sets. A change to any source an evaluation runs through, which `CandidateEvaluator` lists, moves every evaluator's version, and from the next night each standing candidate is skipped and named as a failure on the listings stage's run log row until it is retired and registered again. Each attempt, refused or not, is a row on the run log under `candidate-register`.

Now: the paragraph adds that a registration made while a night runs is evaluated from the night after, that the region's figures are the register as it stands when the page is read, that a name without readings is a counted skip, and that the listings stage fails only on a missing or moved evaluator, whose remedy is a retirement and a new registration.

Why: the operator reads the listings stage's failures on the run page and needs to know which one asks for a retirement.

### 2026-09-16 - SCHEMA.md - the register's version pinned to every source an evaluation runs through
Authorised by: A registration names an evaluator the code carries, and its version is the pin of every source its evaluation runs through
Was:
> | `evaluator_version` | TEXT | that evaluator's version as the code carried it when the row was written, which is a hash of the evaluator's own source |
>
> and `evaluator_version` is a hash of that evaluator's source with line endings normalised to LF and any leading byte order mark removed, so the same evaluator hashes the same on both platforms and on a runner that checked the tree out with either ending. A changed evaluator is a new registration retiring the old one, never an edited row, and `register-append-only` fails a registered, unretired candidate whose evaluator's source has moved away from the version its row names.
Now:
> the column note and the paragraph say the version is the pin of the evaluator's source and every source its evaluation runs through, a changed evaluation is a new registration, and the check fails a standing candidate whose evaluation's sources have moved.
Why: a pin over one file left the parameter parse, the reading of the session before and the indicator arithmetic free to change a registered rule with the row still matching.

### 2026-09-16 - SCHEMA.md - a name registered again, and a row in the same second as a night's start
Authorised by: A candidate stands by the last row naming it, and a name retired and registered again stands once
Was:
> A retirement is a new dated row naming what it retires. `register-append-only` asserts the absence in both the source and a live attempt.
>
> | `registered_at` | TEXT | UTC instant |
Now:
> a name retired may be registered again, standing once by its last row, and the check's live attempt includes a replace; `registered_at` is held to the second and a row in the same second as a night's start or a window's opening is read as after it (see: A register row in the same second as the instant it is compared with is read as after it).
Why: the divisor subtracted a name registered again while the night evaluated it, and a registration a fraction of a second after a night started read as before it.

### 2026-09-16 - SCHEMA.md - no replace, refused by the table
Corrects: the register's note said no update and no delete while a replace removed a row and wrote another past both triggers. Found by the phase 8 sign-off review on 2026-09-16.
Was:
> No update, no delete. A correction is a new row.
Now:
> No update, no delete and no replace, each refused by the table. A correction is a new row.
Why: migration 27 is what refuses the replace, and the note is what a reader of the table checks first.

### 2026-09-16 - ARCHITECTURE.html - section 18's register edit row says which half records the attempt
Corrects: the row said the run log records every attempt to edit or delete a register row, while a statement against the file is refused by a trigger that rolls the statement back and can write nothing. Found by the phase 8 sign-off review on 2026-09-16.
Was:
> <td>the write is refused and the run log records the attempt</td>
Now:
> the row says an attempt through the registrar is refused with the attempt on the run log, and one against the file, a replace included, is refused by the table, which writes nothing, the run log included.
Why: a refusal at the table is a rollback, and a row promising a record from it promises something no store can hold.

### 2026-09-16 - ARCHITECTURE.html - the divisor counts candidates standing, a name registered again once, and the register refuses a replace
Authorised by: A candidate stands by the last row naming it, and a name retired and registered again stands once
Was:
> section 17's family row: the harness asserts the register accepts no update or delete, and that the divisor used matches the number of rows registered before the window opened
>
> section 19.2's register row: the register refuses updates and deletes, and the correction divisor matches the rows registered before the window opened
Now:
> both say the register refuses an update, a delete and a replace, and the divisor matches the candidates standing before the window opened, section 17's naming a name retired and registered again once.
Why: rows registered is not candidates standing once a name is registered again, and the replace is refused from migration 27.

### 2026-09-16 - BUILD_PLAN.md - 8.3's version, divisor and done condition at what the correction adds
Authorised by: A registration names an evaluator the code carries, and its version is the pin of every source its evaluation runs through
Was:
> The evaluator carries its own version, a test pins that version to a hash of the evaluator's source with line endings normalised and any leading byte order mark removed, and a candidate whose evaluator has moved on is a new registration rather than an edited row.
>
> The family is at most eight and the correction divides the threshold by the rows registered before the window opened (see: The candidate family is at most eight and the threshold is divided by it).
>
> **Done when** an update and a delete are both refused at the store, a retirement is a new row naming what it retires, the correction divisor matches the rows registered before the window opened, an evaluator whose source moved without its version fails, and `register-append-only` runs on every CI run from here.
Now:
> the version is pinned over the evaluator's source and every source its evaluation runs through; the divisor is the candidates standing before the window opened, a name retired and registered again once; and the done condition adds a replace refused at the store, counts a name registered again once, and fails an evaluator whose evaluation's sources moved.
Why: 8.3's done condition was met by a store that let a replace rewrite a row, a divisor that dropped a name registered again, and a pin over one file (see: A candidate stands by the last row naming it, and a name retired and registered again stands once).

### 2026-09-16 - .claude/rules/checks.md - register-append-only reaches a replace, a name registered again, a value that is not a number and the second a window opened in
Corrects: the row said the register refuses updates and deletes at the table itself while a replace rewrote a row past both triggers, its source half read a replace as an insert or as nothing, its divisor counted a name retired and registered again as retired, its two routes stated one rule twice over a store holding no retirement, and a value that is not a finite number registered. Found by the phase 8 sign-off review on 2026-09-16.
Was:
> | `register-append-only` | every CI run | The candidate register refuses updates and deletes, at the table itself rather than only in what writes to it, and the register still reads as it did after each is refused; a retirement is a new row naming what it retires and the row it retires still stands; the correction divisor counts the candidates registered before the window opened and not retired before it opened, over hand-worked rows and over a store by two routes; and a registered candidate whose evaluator's source has moved without its version fails rather than being evaluated under a rule the register does not name, the version being a hash of the evaluator's own source taken over line endings normalised and a leading byte order mark removed; and a live reason's name is refused both as a registration and as a retirement, the refusal naming the floor section 17 sets before a live condition may be retired |
Now:
> the row adds a replace refused at the table with recursive triggers off or on and nothing on the run log, the source half read in every form SQLite accepts, a name retired and registered again standing once in the divisor, against the maximum and on the run page, the same-second rule, two routes that state the rule differently and the fixture, and a value that is not a finite number refused.
Why: the check claimed refusal at the table and a divisor over the register, and both had a population it could not see.

### 2026-09-16 - .claude/rules/checks.md - register-append-only pins the version to every source an evaluation runs through
Authorised by: A registration names an evaluator the code carries, and its version is the pin of every source its evaluation runs through
Was:
> a registered candidate whose evaluator's source has moved without its version fails rather than being evaluated under a rule the register does not name, the version being a hash of the evaluator's own source taken over line endings normalised and a leading byte order mark removed
Now:
> a registered candidate whose evaluation's sources have moved without its version fails, the version being the pin of the evaluator's own source and every source its evaluation runs through, listed and held to the shipped files that compute a reading or run an evaluation.
Why: the rule a registration names is decided by more than the evaluator's file, and the ladder rules' pin already reaches every source a replay runs through.

### 2026-09-16 - RUNBOOK.md - the register section names the finite value refusal, a name registered again and what a moved evaluation costs
Corrects: the registrar accepted a value that is not a finite number, which the 8.3 correction refuses; the section did not say a name may be registered again, which the divisor dropped until that correction; and it did not say a change to the code an evaluation runs through stops every standing candidate being evaluated. Found by the phase 8 sign-off review on 2026-09-16.
Was:
> The registrar refuses an evaluator the code does not carry, a parameter the evaluator does not read, a ninth candidate, a live reason's name, and any change to a candidate that stands registered. A live reason is retired only by changing section 11 and the code together, once its record holds 400 resolved setups. A change is a retirement and a new registration:
>
> From the next night every standing candidate is evaluated on every name into the shadow column and shown nowhere; the run page states how many are registered and the divisor that sets.
Now:
> the refusals include a value that is not a finite number; a change is a retirement and a new registration under the same name or another, and a name registered again stands once; and a change to any source `CandidateEvaluator` lists moves every evaluator's version, after which each standing candidate is skipped and named as a failure on the listings row until retired and registered again.
Why: the operator reads what a registration is refused for and what a code change costs here, before meeting either as a failed command or a failed night.

### 2026-09-16 - SCHEMA.md - `resolved_on` names the session a row resolved on
Corrects: the note said null while unresolved, where the filler has written the cap's own session on a setup unresolved at the cap since 5.5; found while designing the 5.5 correction.

Was:
> | `resolved_on` | TEXT | date, null while unresolved |

Now: the note names the session a horizon matured or a setup resolved on, the cap's own session for a setup timed out, and null while not yet matured.

Why: unresolved at the cap is an outcome with a session, and a note reading null for it describes a row the filler never writes.

### 2026-09-16 - SCHEMA.md - a forward return written until it is decided, a plan read at its listing session's adjustment, and the raw close's one reader
Authorised by: An outcome once decided is never rewritten, and a setup still in play is scored with its plan scaled by its listing session's adjustment factor

Was:
> It is written by whichever component writes the bar and is never read by the arithmetic that draws or computes: those read the adjusted set.

Now: the bar note names the forward return filler as the one reader of `raw_close`, for the factor a stored plan is scaled by, and a paragraph after the forward return notes says a row is written until its outcome is decided and never after, `base_rate` being the one column written over a decided row and a setup in play being scored at the listing session's close over its raw close.

Why: the ruling makes a decided row final and reads the raw close for the first time, so the table that says what each column is for has to say both.

### 2026-09-16 - ARCHITECTURE.html - the filler reads the rows it has written, and section 17's setup resolution states the plan's scale and a decided outcome's finality
Authorised by: An outcome once decided is never rewritten, and a setup still in play is scored with its plan scaled by its listing session's adjustment factor

Was:
> <td>listings, bar store</td><td>forward returns</td>, in the Forward return filler's catalogue row, and <td><span class="w">W</span></td> under Forward returns in its matrix row
>
> <td>a listed setup starts on the first close at or below its entry zone's top edge and resolves when its target is reached, its stop is closed through, or 63 sessions pass from the listing; a timed-out setup and one whose price never reached that entry are each counted in their own column and never as a win (see: An unresolved setup is never a win) (see: A setup is scored from its entry, and a target reached before the entry is never a win)</td>, and <td>resolution unit tests, one per outcome</td>, in section 17's Setup resolution row

Now: the catalogue row reads listings, bar store and forward returns, and the matrix cell reads R W; the setup resolution row adds that the plan is read at the listing session's adjustment and a decided outcome is never written again, and its assertion cell adds the filler's stored rows over constructed stores restated and cut back after the listing.

Why: the filler now reads the rows it wrote to decide which it leaves alone, which its declaration, catalogue row and matrix cell have to agree on, and section 17 is where a setup's resolution is stated.

### 2026-09-16 - .claude/rules/checks.md - the guardrails held clause by clause, the loop's silence asserted over a night, and a live reason refused at the register

Corrects: `architecture-conformance` held each 13.3 guardrail to one test by name alone, and three of the eight named tests about something else; the loop's changed-nothing assertion was made over two empty lists; and nothing refused a live reason's name at the register, where section 17 says a live condition is retired only at a higher floor. Found by the building session while writing phase 8's sign-off handoff.
Was:
> | `architecture-conformance` | every CI run | Every claim ARCHITECTURE.html makes, in a table, in a figure, or in the nightly run's ordered list, has a verdict: pass, fail, out of scope for this phase, or unexamined; every table and every figure in the document is placed so none can go unread, each against a population read from the document rather than from the reader; a claim that passes names the check that reached it, on both surfaces the report writes; and every placement and every pass is reconciled against what the check it names declares it reaches, in both directions; and a check a row names in its own words is the check its verdict names or is named in that verdict's note |
>
> | `register-append-only` | every CI run | The candidate register refuses updates and deletes, at the table itself rather than only in what writes to it, and the register still reads as it did after each is refused; a retirement is a new row naming what it retires and the row it retires still stands; the correction divisor counts the candidates registered before the window opened and not retired before it opened, over hand-worked rows and over a store by two routes; and a registered candidate whose evaluator's source has moved without its version fails rather than being evaluated under a rule the register does not name, the version being a hash of the evaluator's own source taken over line endings normalised and a leading byte order mark removed |
Now:
> `architecture-conformance` also maps section 13.3's guardrails clause by clause to a test whose own body exercises the code the clause is enforced by, with no test holding two, and shows the loop changed nothing over the register's rows and version windows a whole recorded night ran over; `register-append-only` also refuses a live reason's name as a registration and as a retirement, naming the floor section 17 sets.
Why: a mapping that asks only that a name exists passes a test chosen for its name, and an assertion over lists the test itself hands in cannot come back anything but empty.

### 2026-09-16 - RUNBOOK.md - a live reason's name among the registrar's refusals

Corrects: the registration section written at the 8.6 correction listed the registrar's refusals without the live reason's name, which the 8.7 correction adds. Found while making that refusal.
Was:
> The registrar refuses an evaluator the code does not carry, a parameter the evaluator does not read, a ninth candidate, and any change to a candidate that stands registered.
Now:
> The registrar refuses an evaluator the code does not carry, a parameter the evaluator does not read, a ninth candidate, a live reason's name, and any change to a candidate that stands registered. A live reason is retired only by changing section 11 and the code together, once its record holds 400 resolved setups.
Why: the operator reads what a registration is refused for here, and a refusal the page does not list is one they meet first as a failed command.

### 2026-09-16 - ARCHITECTURE.html - the version bound stated to its end, and the verbs three rows name

Corrects: section 17's version bound admitted a night past the deadline and stated 688 seconds as though it were the worst case, and the Rule version scorer's row promised a verb the worker did not carry. Found by the building session while writing phase 8's sign-off handoff.
Was:
> at most 4 versions of each of the 4 ladder rules and 14 at once, marked proposed; a version's live rule is what the night already computes and is no extra replay (owes: The rule version bound set from nights the version scorer ran)</td><td>the arithmetic is the night of 2026-09-14: 495 seconds over the steps before the close, a level stage of 143 seconds and a ladder stage of 5 at 504 names, so a merge distance version costs 148 seconds and every other version 5, which puts 14 at 688 seconds against a deadline of 900. Four of each of four rules is sixteen, so the total is what binds first, and the per-rule cap is what stops one rule taking the whole budget and leaving the other three unversioned at the same cost to the night. It grows with versions times names and never with a request</td><td>the bound refused at the fifteenth version and at the fifth of one rule, the projection computed from the night's own stage durations, and `nightly-cost` over a recorded night at one version and at fourteen
>
> and in the Rule version scorer's row: opening and closing a version's window through its own verb and never as a side effect of a night; the Candidate registrar's row named no verb, and the Read API's row named "the worker's research verb" without marking it as one.
Now:
> the row states at most 2 windows of the merge distance and 4 of each of the other 3 rules, live windows among them and 14 at once, a version opened only beside its rule's live window, the fullest register replaying 1 merge distance version and 9 others for 193 seconds and a night of 688 against 900, and `nightly-cost` over a recorded night at one version and at the fullest register; the three catalogue rows name the <code>version</code>, <code>register</code> and <code>research</code> verbs.
Why: four windows of every rule with no live window required admitted four level replays and a night of 1,137 seconds, and a verb a row names is checked against the worker's dispatch only once the row names it as a verb.

### 2026-09-16 - BUILD_PLAN.md - 8.6's bound and its operating row restated at the caps that keep the night inside its deadline

Corrects: 8.6's text and the rule version bound's operating row stated four versions of every rule, and the row's arithmetic added 188 seconds to 495 and called it 688. Found by the building session while writing phase 8's sign-off handoff.
Was:
> The bound is at most four versions of each of the four rules and fourteen at once, marked proposed, and its arithmetic is stated: the night of 2026-09-14 took 495 seconds over steps 1 to 16, its level stage 143 seconds and its ladder stage 5 at 504 names, so a merge distance version costs a level and a ladder replay and every other version costs a ladder replay, which puts fourteen at 688 seconds against the deadline of 900. The arithmetic grows with versions times names and never with a network request, and `nightly-cost` asserts that over a recorded night at one version and at fourteen (owes: The rule version bound set from nights the version scorer ran).
>
> The bound is at most four versions of each of the four ladder rules and fourteen at once, marked proposed, and its arithmetic is the night of 2026-09-14: 495 seconds over steps 1 to 16, a level stage of 143 seconds and a ladder stage of 5 at 504 names, so fourteen versions add 188 seconds and sit at 688 against a deadline of 900.
Now:
> two windows of the merge distance and four of each other rule, each rule's live window among them, fourteen at once, a version opened only beside its rule's live window, the fullest register adding 193 seconds for a night of 688 against 900, `nightly-cost` over a recorded night at one version and at the fullest register, and windows opened and closed through the worker's `version` verb.
Why: the stated bound admitted a night of 1,137 seconds, and 495 and 188 are 683.

### 2026-09-16 - SCHEMA.md - rule_version states the live row a version stands beside

Authorised by: A ladder rule's version is measured beside that rule's live window, and both count against the bound
Was:
> nothing: the table's notes said what a window is keyed on and not which rows may be open together.
Now:
> a paragraph saying a version row stands beside an open `live` row of its rule, a `live` row carries the build's own parameters, a version row's parameters are named as its rule is replayed from, a `live` row is not closed while a version of its rule is open, and at most two rows of the merge distance and four of each other rule are open at once, `live` rows included.
Why: the decision rules what rows may stand together, and SCHEMA is where a reader of the table looks for it.

### 2026-09-16 - RUNBOOK.md - registering a candidate and versioning a ladder rule

Corrects: the runbook showed no command line for the `register` verb built at 8.3 or for any way to open a rule version, so the loop phase 8 built could not be started from the operator's manual. Found by the building session while writing phase 8's sign-off handoff.
Was:
> nothing: no section named either verb.
Now:
> a section showing the `register` and `version` command lines, the evaluators and their parameter names, the four rules and the names each is replayed from, the caps, the backfill, where each attempt is on the run log, and what to do when a night stops at the rule versions step.
Why: a decision a person takes is taken from this document, and a verb it does not show is one nobody can find.

### 2026-09-16 - .claude/rules/checks.md - four rows state what the 8.6 correction asserts

Corrects: `rule-versions-scored`'s row stated a cap of four for every rule and a projection the test forced to agree with the document; `nightly-cost`'s row did not state the recorded night section 17 said it ran; and neither `component-access` nor `architecture-conformance` stated the reconciliation that would have found the missing verb or the missing night. Found by the building session while writing phase 8's sign-off handoff.
Was:
> | `component-access` | every CI run | Every component declares the stores it reads and writes, and the declaration is reconciled against its catalogue row, its read and write matrix row cell by cell with the blanks included, SCHEMA's ownership, and the statements in its own source, in both directions |
>
> | `architecture-conformance` | every CI run | Every claim ARCHITECTURE.html makes, in a table, in a figure, or in the nightly run's ordered list, has a verdict: pass, fail, out of scope for this phase, or unexamined; every table and every figure in the document is placed so none can go unread, each against a population read from the document rather than from the reader; a claim that passes names the check that reached it, on both surfaces the report writes; and every placement and every pass is reconciled against what the check it names declares it reaches, in both directions |
>
> | `nightly-cost` | every CI run | The nightly path makes zero per-name network requests in its steady state and its arithmetic zero model calls, asserted over the shipped source and over a recorded run, with the run measured over two universe sizes so the count is shown not to grow with the population. The two carve-outs the hard rule names are asserted rather than exempted: a joiner's backfill is one request per name ever, and a suspect name costs one request on each of the five nights of its retries and one a week after them, counted over a constructed name whose refetch fails. The night reaches no lane an open reaches, read off what the components its own file constructs declare, and every model call a whole recorded night makes sits on step 17's own row or on a pass that row names, with nothing spent anywhere, because the queue is carved out of the model-call rule by name and out of nothing else |
>
> | `rule-versions-scored` | every CI run | A version change closes the window measuring the old rule and opens a new one, with the closed row keeping every column it was opened with so the scores under it stay scores of the rule as it stood; the bound is refused at the fifteenth version and at the fifth of one rule and admitted one below each, with a closed window counting against neither; the night's added seconds are projected from its own stage durations rather than from a figure written beside them; a live rule whose parameters or code have moved inside an open window is found and one that has not is not, in both directions; a version replays a plan the live rule does not produce; and a score written for a night before its window opened is flagged in sample beside one written for a night after it |
Now:
> `component-access` also reconciles the worker verbs a catalogue row names against the worker's dispatch, the runbook and the help; `architecture-conformance` also holds a check a row names to the check its verdict names or to that verdict's note; `nightly-cost` also runs a recorded night at one rule version and at the fullest register; and `rule-versions-scored` states the live window beside every version, the per-rule caps, the worst case held inside the deadline, the code version pinned to the sources a replay runs through, the backfill reading that night's bands, and the `version` verb.
Why: a roster row is what `coverage-reported` holds a check to, and each of the four now asserts something its row did not say.

### 2026-09-16 - SCHEMA.md - the rule versions and the scores written under them

Authorised by: Adding a candidate later restarts the clock
Was:
> neither table existed, and `level`'s members carried a kind, a price and a date.
Now:
> `rule_version` holds a window per rule per version with its parameters, their hash with the code version, and the instants it opened and closed; `version_score` holds a plan per name per night per rule per version with the window it belongs to and whether it counts or was written in sample; and a level member carries its own source beside its kind.
Why: the band already carried whether any member is not a moving average, which is a fact derived from the sources, and stored the derived fact while dropping the fact it came from, so nothing could read a band back and say which member was the average. The instant is in the version key because scores belong to a window: a version closed and opened again is two measurements and not one.

### 2026-09-16 - ARCHITECTURE.html - the rule version scorer, its step, its two stores and its bound

Authorised by: Adding a candidate later restarts the clock
Was:
> section 7 carried no scorer, the matrix carried fourteen store columns, section 16 carried neither store, section 14 had sixteen steps with the arithmetic being steps 1 to 16 and the queue step 17, and section 17 carried no bound on versions.
Now:
> a **Rule version scorer** catalogue row and matrix row, two store columns and two store rows, a step between the news pulse and the close with the arithmetic now steps 1 to 17 and the queue step 18, and a **Rule versions scored at once** row stating at most 4 of each of the 4 rules and 14 at once with the arithmetic that sets it.
Why: 8.6 is the checkpoint that builds the scorer, and a step the document does not carry is a step the harness reads as unexamined. The step counts move because a step was inserted before the close.

### 2026-09-16 - .claude/rules/checks.md - rule-versions-scored joins the roster

Authorised by: Adding a candidate later restarts the clock
Was:
> the roster carried 35 rows and none for the version store.
Now:
> a `rule-versions-scored` row running on every CI run, asserting the window's close keeps what it was opened with, the bound at each cap and one below, the projection from the night's own durations, the drift check in both directions, a version producing a plan the live rule does not, and a backfilled score flagged in sample.
Why: the two store rows and the bound needed a check to reach them, and the register's own check is about the register rather than about windows.


### 2026-09-16 - ARCHITECTURE.html - section 17 states the significance threshold, and the minimum gains its night floor

Authorised by: A verdict tests a reason's wins against each of its setups' own break-even at a corrected threshold
Was:
> section 17 carried no row for the threshold at all, and the minimum read "250 resolved setups before a verdict is reported at all; 400 before a live condition may be retired", asserted by the run page showing the count beside every verdict and withholding it below the minimum.
Now:
> a **Significance threshold** row states 0.05, one-sided, divided by the family the reason belongs to, with the six live reasons as one family of 6 and registered candidates as the other at most 8, and says the null makes the wins a Poisson binomial whose exact tail is computed from the stored break-evens. The minimum reads "250 resolved setups spread over at least 60 distinct listing sessions, each contributing at least one", its reasoning says why the sessions are a floor of their own, and it is asserted by the page withholding below either floor and naming the one that is short.
Why: the threshold was decided at 8.0 and stated only in the decision record, so the limits table a reader checks a verdict against did not carry the number the verdict was computed at. The night floor is the same omission one column along: a count of rows alone can be filled by a handful of nights of one market move, and the test assumes an independence that listings clustering by sector and by date do not give.

### 2026-09-16 - SCHEMA.md - the shadow column carries what the night could not evaluate as well as what it did

Authorised by: Candidate conditions are registered before they are scored, and scored in shadow before they are shown
Was:
> | `shadow_reasons` | TEXT | JSON: registered candidates, evaluated the same way, shown nowhere |
Now:
> the column holds two lists, `candidates` and `skipped`, the second carrying each registered candidate the night could not evaluate with the reason, and a paragraph below says why they are separate.
Why: a candidate that did not fire and a candidate nothing evaluated are opposite statements, and folding the second into the first is how a record of having skipped a name-night stops existing.

### 2026-09-16 - ARCHITECTURE.html - the shortlist builder reads the register, and the read API reads it too

Authorised by: Candidate conditions are registered before they are scored, and scored in shadow before they are shown
Was:
> the shortlist builder's Reads cell ended at facts and its row said it evaluates the six reasons and records which fired, for every index member and not only the listed ones; its matrix row was blank under the candidate register, as was the read API's, and the matrix key said the read API's cell there was the one store it does not read.
Now:
> the Reads cell names the candidate register and the row says it evaluates every candidate standing registered at the instant the night started into the shadow column of the same rows, naming as a failure any it could not evaluate; both matrix rows carry a read; and the key says the read API's cell filled at 8.4 with the region that states how many candidates are registered, and that what it reads there is the register and never a shadow evaluation of a name.
Why: 8.3 declared the register with nothing reading it, and 8.4 is the checkpoint where the night writes the column and the run page states the count and the divisor.

### 2026-09-16 - .claude/rules/checks.md - listings-coverage carries the shadow column

Authorised by: Candidate conditions are registered before they are scored, and scored in shadow before they are shown
Was:
> the row ended at a member the night computed nothing for still carrying one.
Now:
> it also asserts what the every-name grain exists for: every candidate standing registered when the night started evaluated into every row including the quiet ones, a candidate registered after the night started not evaluated by it, and a drifted evaluator skipped with its reason and named as a failure on the stage's run log row.
Why: the check's own comment already said the every-name grain is what the shadow mechanism rests on, and 8.4 is where that stopped being a reason and became a thing asserted.

### 2026-09-16 - SCHEMA.md - the candidate register names the evaluator that will run it

Authorised by: A registration names an evaluator the code carries, and its version is a hash of that evaluator's own source
Was:
> the table carried `id`, `candidate`, `rule`, `test`, `event`, `retires`, `registered_at` and `evidence`, with the note "No update, no delete. A correction is a new row."
Now:
> three columns sit between `test` and `event`: `evaluator`, the name of an evaluator the Core code carries, refused at the write where it carries none; `parameters`, the JSON values it is run with; and `evaluator_version`, that evaluator's version as the code carried it when the row was written. `event` is stated as constrained in the table, and a paragraph below says why the three are what make the row a registration rather than a description.
Why: a candidate naming its rule in prose alone is a row a later session re-implements from words, and what it implements is whatever it read the words to mean. The register has to say what will run.

### 2026-09-16 - ARCHITECTURE.html - the candidate registrar, and the register's column in the matrix

Authorised by: Candidate conditions are registered before they are scored, and scored in shadow before they are shown
Was:
> section 7 carried no registrar; the read and write matrix carried thirteen store columns and no candidate register among them; section 16's register row read "candidate, its rule, its test, the date registered, and for a retirement a new row naming what it retires" kept "forever; append-only, no update and no delete".
Now:
> section 7 carries a **Candidate registrar** row, running on request, reading and writing the register; the matrix carries a fourteenth column for the register, blank in every row but the registrar's, and a registrar row; section 16's row names the evaluator, the parameters and the version as well, and says the refusal is the table's and not only its writers'; and the matrix key says why the read API's cell under the new column is blank and why the registrar reads what it writes.
Why: the register had a store row and no component, so the one omission section 16's key gave no reason for was its missing column. The migration at 8.3 is what let both be put to something.

### 2026-09-16 - .claude/rules/checks.md - register-append-only runs on every CI run

Authorised by: Candidate conditions are registered before they are scored, and scored in shadow before they are shown
Was:
> | `register-append-only` | from 8.3 | The candidate register refuses updates and deletes, the correction divisor matches the rows registered before the window opened, and a registered candidate whose evaluator's source has moved without its version fails rather than being evaluated under a rule the register does not name |
Now:
> the Runs cell reads "every CI run", and the Asserts cell states each half as the check asserts it: the refusal at the table itself with the register reading afterwards as it did, the retirement as a new row with the retired one still standing, the divisor over hand-worked rows and over a store by two routes, and the version as a hash of the evaluator's source over normalised line endings and a removed byte order mark.
Why: 8.3 is the checkpoint the row named, and a roster row naming a checkpoint the record shows as landed fails `coverage-reported`. It was the last checkpoint row on the roster, so the pending population is now empty and its floor moved to a constructed proof.

### 2026-09-16 - BUILD_PLAN.md - phase 8's opening carries the ruling that moves the reference material

Corrects: the pass moving `CLAUDE.md`'s reference material had no checkpoint that owed it, and a commit subject may not name a number the plan does not carry.
Was:
> the phase opened with the paragraph on the pass that carries repairs, and nothing in the plan said where the reference material lives or why.
Now:
> a paragraph above it states the ruling taken at 8.2: what moves to `.claude/rules/`, that nothing is reworded, that the populations widen first, and why it is a ruling on an existing checkpoint rather than a checkpoint of its own.
Why: the loop's numbers are fixed by the claims still out of scope at 8.3 to 8.6 and by 8.0's landed done condition, and a checkpoint appended after the phase report would be the plan's last, which no entry may record as built while work in the phase remains. It was written as a checkpoint numbered 8.8 first and the harness refused it, which is the reason this reads as a ruling. It sits in the phase's opening rather than inside a checkpoint's text, so it supplies no due point.

### 2026-09-16 - CLAUDE.md - the build state stated directly, which the paragraph under it forbids

Corrects: the file stated a build state in the one place the paragraph beneath it says a build state must not live, and the statement was false at the checkpoint the record shows. Found by reading the section against its own next paragraph.
Was:
> Nothing is built. What the build has reached is recorded below rather than stated here.
Now:
> What the build has reached is recorded below rather than stated here.
Why: which checkpoint the build is on is what `PROGRESS.md` records, and a second place for that fact goes stale the moment a checkpoint lands, which this one had.

### 2026-09-16 - CLAUDE.md - the commands table described rather than promised

Corrects: a line conditioning the table on a checkpoint that landed long ago, so the table read as a contract rather than a description.
Was:
> Checkpoint 0.4 is what makes this table true. Until it lands, these are the contract rather than a description.
Now:
> the line is removed; the table stands as a description of what the repository holds.
Why: 0.4 landed, every script the table names exists, and a reader has no way to tell a promise from a description while the sentence stands.

### 2026-09-16 - CLAUDE.md - the two done conditions no longer conditioned on 0.4

Corrects: conditions 2 and 5 each carried a clause describing what to do before 0.4 built the scripts and the workflow, which no session can now reach.
Was, in condition 2:
> Until 0.4 builds those scripts, the checkpoint's own verification is run by hand and PROGRESS records the figures it produced and states that nothing guards them yet.
Was, in condition 5:
> Until 0.4 makes the workflow able to run, the suite is run on the machine at hand, PROGRESS names which platform that was, and the other platform is carried to 0.4.
Now:
> both clauses are removed. The rest of both conditions stands, including condition 5's decision citation and its sentence about the hosted Windows leg the 7.2 ruling removed.
Why: a done condition carrying an escape nobody can take is read by every session and applies to none of them.

### 2026-09-16 - CLAUDE.md - the checks roster moved to .claude/rules/checks.md

Corrects: no defect. The section is 18,478 bytes and a third of the file, and no session outside `tools/` or the test project reads it, while every session loaded it.
Was:
> the whole `## Checks` section: the lead sentence, the roster table of thirty-five rows, and the eight paragraphs under it, from "The table lists every check that runs" to the `path-casing` paragraph.
Now:
> the same text, word for word, in `.claude/rules/checks.md`, which loads for a session reading `tools/**` or `src/EquityBrief.Tests/**`.
Why: a path-scoped file keeps every word of its reasoning and costs nothing to a session working elsewhere. `coverage-reported` locates the roster in one place and that place is the rules file; the corpus checks read it as they read this file.

### 2026-09-16 - CLAUDE.md - the verification rules moved to .claude/rules/writing-tests.md

Corrects: no defect. They govern how an assertion is written and are read by a session writing tests.
Was:
> the whole `## Verification` section: its lead sentence, its twelve bullets and the paragraph naming the two rules specific to this tool.
Now:
> the same text, word for word, in `.claude/rules/writing-tests.md`, which loads for a session reading `src/EquityBrief.Tests/**`.
Why: as above. Nothing is dropped or reworded, and the two rules the script mechanics point at are named in that file's opening so the pointer still resolves.

### 2026-09-16 - CLAUDE.md - the corpus editing conventions moved to .claude/rules/corpus-edits.md

Corrects: no defect. They are read by a session editing a document.
Was:
> eight conventions from `## Conventions`: decisions are named not numbered and the paragraph on citing one; a deferral names what produces the evidence; a done condition may not require calendar time; obligations are named and cited; a decision is changed only by another decision; nothing in the corpus is struck through; components are named not coded; headings in ARCHITECTURE.html carry numbers.
Now:
> the same text, word for word, in `.claude/rules/corpus-edits.md`, which loads for a session reading `docs/**`.
Why: as above. Five conventions stay in `CLAUDE.md` because they bind a session working anywhere: the prose convention, on which `banned-prose` matches its single exemption in that file; the commit subject; which checkpoint a commit belongs to; the planning pass; and anything issued in conversation landing in the repo when it is issued.

### 2026-09-16 - CLAUDE.md - the script mechanics moved to .claude/rules/scripts.md

Corrects: no defect. They describe what the scripts do and what a green report says, and are read by a session running or editing one.
Was:
> five paragraphs from `## Commands`: the Shell column and the wrapper contract; what `tools/verify-phase` is and what a green report says; the report being one instrument reading another; `tools/ci.*` not being a wrapper around `dotnet test`; and the store it drops being `/data-ci` and never `/data`.
Now:
> the same text, word for word, in `.claude/rules/scripts.md`, which loads for a session reading `tools/**`.
Why: as above. The command table stays in `CLAUDE.md` with the target framework paragraph, the PowerShell parity sentence and the secrets paragraph.

### 2026-09-16 - CLAUDE.md - the read order names the rules directory, and the lifecycle exempts it

Corrects: after the four moves a session reading `CLAUDE.md` alone no longer sees the roster, the conventions, the verification rules or the script rules, and had no way to learn they exist.
Was:
> the read order ended at the five numbered documents and the sentence about not reading the whole corpus; the document lifecycle exempted the screens document and `fixtures/README.md` and named nothing else.
Now:
> the read order carries a table of the four rules files with the paths each is scoped to and what each covers, and a sentence saying a path-scoped rule loads when a session reads a matching file and not before. The lifecycle gains a paragraph in the form of the two beside it, saying the rules directory is not a ninth document and that a rule lives in exactly one of the two places.
Why: a file that loads conditionally is a file a session has to be told about, and the eight-document cap is a rule about where facts live rather than about how many files the repository holds.

### 2026-09-16 - ARCHITECTURE.html - section 17's setup resolution row counts from the entry

Authorised by: A setup is scored from its entry, and a target reached before the entry is never a win
Was:
> a listed setup resolves when its target is reached, its stop is closed through, or 63 sessions pass; a timed-out setup is counted in its own column and never as a win
Now:
> a listed setup starts on the first close at or below its entry zone's top edge and resolves when its target is reached, its stop is closed through, or 63 sessions pass from the listing; a timed-out setup and one whose price never reached that entry are each counted in their own column and never as a win
Why: the filler scored from the listing, so a name that ran to its target from above the entry zone counted as a win for a purchase the plan did not offer, which is 105 of 147 stored wins.

### 2026-09-16 - ARCHITECTURE.html - the run page's reason records row gains the never-entered count

Authorised by: A setup is scored from its entry, and a target reached before the entry is never a win
Was:
> one row per reason with the reason track mark, the resolved count, the share that reached target before stop, and the break-even those setups demanded
Now:
> the same with the never-entered count between the resolved count and the share
Why: a setup nobody entered is in neither half of a rate, so the reader needs it stated rather than left out of every column.

### 2026-09-16 - SCHEMA.md - the forward return's outcome and return notes

Authorised by: A setup is scored from its entry, and a target reached before the entry is never a win
Was:
> | `outcome` | TEXT | `win`, `loss`, `unresolved`, or null while immature |
> | `return_pct` | REAL | null for the `setup` horizon |
Now:
> the outcome adds `never entered`, the setup horizon's alone; `return_pct` for that horizon is the move from the close the setup was entered at, and null where nothing was entered or the entry and the stop fell on one session
Why: the outcome gained a fourth value and the setup horizon gained a return that means something, being the trade's own move rather than the listing's.

### 2026-09-16 - BUILD_PLAN.md - phase 8's section written as the loop it builds, with 8.0's rulings and the model's proposal dropped

Authorised by: The model writes no entry or exit proposal, and the loop scores only what code computes
Was:
> 8.0 named the questions it would decide, being the fundamental panel, the volume profile's claim, the theme for a name with no facts file, the segment figures and the count a verdict note states; 8.1 read "Setup resolution"; 8.3 stated a maximum family size without deciding it; 8.4 said nothing shadow reaches any screen; 8.5 named no test; 8.6 named three rules and no bound; 8.7 was the model's proposal and 8.8 the phase report
Now:
> 8.0 states each ruling and what it rests on; 8.1 is setup resolution from the entry; 8.3 names the register's evaluator, parameters and version; 8.4 says what the run page's region states; 8.5 names the test and the minimum's night floor; 8.6 names four rules, the enforcement and the bound with its arithmetic; and 8.7 is the phase report, the model's proposal having been dropped
Why: the operator ruled on 2026-09-15 that the loop is built to the end of phase 8, that the proposal is dropped rather than carved out of the rule that no model is asked for a number, and that the conventions the loop rests on are decided rather than carried.

### 2026-09-16 - BUILD_PLAN.md - the contradictions table gains the shadow region and the model's proposal

Authorised by: Candidate conditions are registered before they are scored, and scored in shadow before they are shown
Was:
> the table ended at M
Now:
> N, the shadow candidates region against the guardrail that says shadow conditions are shown nowhere, resolved at 8.0 on the decision's side; and O, the model's proposal against the rule that code owns every number, resolved at 8.0 by dropping the checkpoint
Why: each is two documents disagreeing, which is a finding rather than a licence to change either one, so each is recorded with where it was resolved.

### 2026-09-16 - BUILD_PLAN.md - the three prediction lines that read 7.x

Corrects: the arithmetic in phases 5 and 6 named the improvement loop by its old number, which the phase 7 sign-off carried as wording left behind.
Was:
> the 72 that remain at 6.x and 7.x; the 16 that remain at 7.x, twice
Now:
> the improvement loop's checkpoints, then numbered 7.x and now 8.x
Why: a record of what a pass predicted stays as it was, and the checkpoints it names are the ones the plan now has.

### 2026-09-16 - ARCHITECTURE.html - section 13.2 loses the model's proposal and names phase 8

Authorised by: The model writes no entry or exit proposal, and the loop scores only what code computes
Was:
> 13.2 Four things that can improve, shallowest first, with a row for the model's own proposal and three rows naming phase 7; the paragraph beneath reading "the other three" and "All four"
Now:
> 13.2 Three things that can improve, shallowest first, the rows naming phase 8, and the paragraph reading "the other two" and "All three" with a sentence saying what was dropped and why
Why: the loop is phase 8, which the 7.0 planning pass moved everywhere but these cells, and the fourth row asked a model for prices.

### 2026-09-16 - ARCHITECTURE.html - section 20's phase 8 row drops the proposal

Authorised by: The model writes no entry or exit proposal, and the loop scores only what code computes
Was:
> the improvement loop of section 13: setup resolution, the break-even score, the candidate register, the shadow column and the condition verdicts on the run page; rule versions scored counterfactually; the model's own proposal stored beside the computed one and scored the same way
Now:
> the improvement loop of section 13: setup resolution from the entry, the break-even score, the candidate register, the shadow column and the condition verdicts on the run page; rule versions scored counterfactually
Why: the row lists what the phase builds and the proposal is not built.

### 2026-09-16 - ARCHITECTURE.html - section 15.10's shadow candidates region states what it shows

Authorised by: Candidate conditions are registered before they are scored, and scored in shadow before they are shown
Was:
> the same columns for registered candidates that are not on the list, with the correction divisor stated beside the threshold
Now:
> how many candidate conditions are registered, the correction divisor that number sets, and one line saying each candidate's record is withheld until it is promoted. No evaluation of a name appears here or anywhere else
Why: the region as written showed a candidate's record before promotion, which is the thing the shadow exists to prevent, and the decision is the side that stands.

### 2026-09-16 - ARCHITECTURE.html - the momentum panel's row says its neutral rules decide nothing

Authorised by: The momentum panel is context a reader weighs, and nothing computes with it
Was:
> The momentum readings on their own small axes beneath the chart, each with its neutral rule drawn. A number like 53 means nothing without the band it sits in.
Now:
> the same, with the neutral rules named as reading conventions the panel draws and no component applies
Why: the panel was the only part of the technical half with no stated purpose, and 8.0 gave it one rather than carrying the question.

### 2026-09-16 - ARCHITECTURE.html - section 17's eligibility row says an average may set an edge

Authorised by: A moving average may widen a band that a tranche sits on, and may never anchor one
Was:
> a band straddling the close keeps its full width
Now:
> a band straddling the close keeps its full width, and an average among its members may set an edge of that width without making the band eligible
Why: an average sets 36 of the 446 edges of the zones that fired on 2026-09-15, and two sentences of the corpus disagreed about whether it may.

### 2026-09-16 - ARCHITECTURE.html - section 17's family size row states a decided value

Authorised by: The candidate family is at most eight and the threshold is divided by it
Was:
> with a stated maximum family size of 8, proposed
Now:
> with a maximum family size of 8
Why: no count of resolved setups measures a convention, so a proposed marker on it would wait on nothing.

### 2026-09-16 - CLAUDE.md - the nightly rule and the nightly-cost row name the two carve-outs

Corrects: the hard rule said the nightly run makes no per-name network request while the backfill and the corporate action refetch both make one, which the phase 7 sign-off carried as wording the weekly retry left open-ended.
Was:
> Any component that adds a per-name call to the nightly path is a defect, not a feature.
Now:
> the same, followed by the two carve-outs named: a joiner's backfill, once per name ever, and the corporate action refetch on the five nights after a failed check and once a week after that
Why: a rule with two standing exceptions that it does not name is a rule a reader has to discover the exceptions to.

### 2026-09-16 - CLAUDE.md - done condition 5 stops naming the matrix

Corrects: the condition still said "Until 0.4 makes the matrix able to run", where the 7.2 ruling removed the hosted Windows leg and left one hosted runner and one machine.
Was:
> Until 0.4 makes the matrix able to run, the suite is run on the machine at hand, PROGRESS names which platform that was, and the other runner is carried to 0.4.
Now:
> Until 0.4 makes the workflow able to run, ... and the other platform is carried to 0.4, with a sentence saying the workflow carried a hosted Windows leg until the 7.2 ruling removed it
Why: the word named a shape the workflow no longer has.

### 2026-09-16 - CLAUDE.md - the register's roster row starts at 8.3 and names the evaluator guard

Authorised by: A due point names a checkpoint that exists wherever its phase has been detailed
Was:
> | `register-append-only` | from 8.1 | The candidate register refuses updates and deletes, and the correction divisor matches the rows registered before the window opened |
Now:
> | `register-append-only` | from 8.3 | ... and a registered candidate whose evaluator's source has moved without its version fails rather than being evaluated under a rule the register does not name |
Why: 8.3 is the checkpoint whose migration creates the register, and the roster row named 8.1, where nothing creates it; the second half is 8.0's ruling on what a registered candidate is.

### 2026-09-16 - RUNBOOK.md - the suspect row says which surfaces keep their line

Authorised by: A suspect name is asked for again on the five nights after it is marked and weekly after that, and its own page, its row on tonight's list and the run page say so until a refetch succeeds
Was:
> until a refetch succeeds or the name leaves the index, when the lines go
Now:
> until a refetch succeeds or the name leaves the index. When it leaves, the run page's region stops naming it, because that region is about tonight; its name page and its exported report keep their line
Why: the row said every line goes, and two of the three are about a name whose stored prices are still the ones that may not carry the action.

### 2026-09-15 - ARCHITECTURE.html - section 11's breakout on volume row reads resistance at last night's close

Authorised by: Breakout on volume reads resistance at the previous session's close
Was:
> the close is above a resistance band on volume above the fifty-day average
Now:
> the close is above a band that sat at or above last night's close, on volume above the fifty-day average (see: Breakout on volume reads resistance at the previous session's close)
Why: the stored role is set against tonight's close, so the reason as built could never fire; the 5.4 correction reads the side a band was on last night.

### 2026-09-15 - ARCHITECTURE.html - section 11's earnings soon row says the twenty sessions are the exchange's

Authorised by: Sessions to a dated event are counted on the exchange calendar and never on stored bars
Was:
> the next earnings date is within twenty sessions
Now:
> the next earnings date is within twenty sessions, counted on the exchange's calendar (see: Sessions to a dated event are counted on the exchange calendar and never on stored bars)
Why: the builder counted stored bars after the night, which a live store never holds, so every future print read as 0 and fired.

### 2026-09-15 - ARCHITECTURE.html - section 11's flag says what the first night's 477 was

Authorised by: Sessions to a dated event are counted on the exchange calendar and never on stored bars
Was:
> The first night at index size fired 477 of 503 on 798 reasons, which is what the first sentence above predicted in the abstract and is well past the hundred this one illustrates with.
Now:
> The first night at index size fired 477 of 503 on 798 reasons, and most of that was not the thresholds: earnings soon counted the stored bars after the night, which a live store never holds, so every future print read as tonight's and fired, while breakout on volume read a role set against tonight's close and could not fire at all. Both were corrected at 5.4, and the rows written before are kept as written with a line saying so where a page draws them (see: Sessions to a dated event are counted on the exchange calendar and never on stored bars) (see: Breakout on volume reads resistance at the previous session's close).
Why: the sentence read a defect's output as evidence for the thresholds, which the correction's trace showed it was not.

### 2026-09-15 - ARCHITECTURE.html - section 17's earnings horizon row states the count's basis and the refusal past the closure table

Authorised by: Sessions to a dated event are counted on the exchange calendar and never on stored bars
Was:
> <tr><td>Earnings horizon</td><td>20 sessions</td><td>about a month of trading, which is far enough ahead that a position can still be staged or trimmed before the date and near enough that the date is worth stating; the worked example sits 20 sessions before its print and applies the rule</td><td>ladder builder unit test</td></tr>
Now:
> the value cell adds "counted on the exchange's calendar from the night to the date; a date past the last date the closure table covers is not counted, does not fire and is named on the run log", and the test cell adds "and the shortlist builder over a store holding no bar after its night at the horizon and either side of it"
Why: the horizon was stated in sessions and never said whose sessions, which is the gap the bar count fell through.

### 2026-09-15 - ARCHITECTURE.html - section 18 gains a row for listings written before the 5.4 correction

Authorised by: Sessions to a dated event are counted on the exchange calendar and never on stored bars
Was:
> no row
Now:
> Listings written before the 5.4 correction: the rows are kept as written, and the run page's record counts earnings soon and breakout on volume only off rows carrying the value the corrected rule writes; the tonight, run and universe routes for such a session carry one line saying earnings soon on those rows counted every future print as tonight's, and breakout on volume could not fire
Why: the operator's ruling on 2026-09-15 that those routes say so rather than presenting what the defect wrote as that night's reading.

### 2026-09-15 - SCHEMA.md - the listing's reasons note names the two markers, and fired_count's note states its mechanism

Authorised by: Sessions to a dated event are counted on the exchange calendar and never on stored bars
Was:
> | `reasons` | TEXT | JSON: each of the six reasons with fired true or false and the values that made it so |
> | `fired_count` | INTEGER | zero for most rows |
Now:
> the reasons note adds that from the 5.4 correction earnings soon's values carry `next dated event` and breakout on volume's carry `previous close`, and a row without them was written before it; the fired_count note reads "how many of the six reasons fired on the row, counted from `reasons`"
Why: "zero for most rows" was false on every whole-index night the store holds, and a figure about what a night produced belongs in the record rather than in a spec, where it goes stale.

### 2026-09-15 - RUNBOOK.md - two symptoms added to the morning table, the refusal past the closure table and the line on listings written before the correction

Authorised by: Sessions to a dated event are counted on the exchange calendar and never on stored bars
Was:
> no rows
Now:
> a row for a name the listings stage names as having a dated event beyond the exchange calendar, and a row for a past night whose pages say its listings were written before a correction
Why: each is a line a person reads on a surface in the morning, and each says what to do rather than leaving the line to be read as a fault.

### 2026-09-15 - CLAUDE.md - the workflow's layout line names the macOS job, and says where Windows is verified

Authorised by: Windows is verified on the operator's machine, and the hosted runners are macOS and Linux
Was:
> .github/workflows/ci.yml   the two-platform matrix and the Linux case-sensitivity job.
Now:
> .github/workflows/ci.yml   the macOS job and the Linux case-sensitivity job. Windows is
>                   verified on the operator's machine rather than here.
Why: the matrix is gone and a layout line naming it would describe a file the repository no longer has.

### 2026-09-15 - CLAUDE.md - two-platform runs on every CI run and reads Windows off the record

Authorised by: Windows is verified on the operator's machine, and the hosted runners are macOS and Linux
Was:
> | `two-platform` | the matrix | The suite passes on both windows and macos runners, and no leg can report green without running the suite: the workflow carries zero YAML condition keys, counted rather than blocklisted, because a leg is skipped at runtime by any condition at all and a skipped job leaves its run green |
Now:
> | `two-platform` | every CI run | The suite passes on both platforms, macOS on a hosted runner and Windows on the operator's machine (see: Windows is verified on the operator's machine, and the hosted runners are macOS and Linux): the workflow runs `tools/ci.sh` on macOS and names no Windows runner, and every checkpoint entry written since the hosted Windows leg was removed says `tools/ci.ps1` ran green, read off the record by a reader shown to find an entry that does not. No leg can report green without running the suite: the workflow carries zero YAML condition keys, counted rather than blocklisted, because a leg is skipped at runtime by any condition at all and a skipped job leaves its run green |
Why: with the Windows leg gone the check no longer runs as a matrix, and the half that said the suite passed on a Windows runner has to say where the Windows result now comes from and what reads it.

### 2026-09-15 - CLAUDE.md - the Runs column has two forms, the matrix being gone

Authorised by: Windows is verified on the operator's machine, and the hosted runners are macOS and Linux
Was:
> Every check either runs on every CI run, runs as the matrix, or names the checkpoint that starts it. `coverage-reported` asserts each of those three against the corpus:
Now:
> Every check either runs on every CI run or names the checkpoint that starts it. `coverage-reported` asserts both against the corpus:
Why: the one row running as the matrix now runs on every CI run, and the readers that accepted the matrix as a third form accept it no longer, so the sentence names the two forms that remain.

### 2026-09-15 - CLAUDE.md - done condition 5 names where each platform's suite run comes from

Authorised by: Windows is verified on the operator's machine, and the hosted runners are macOS and Linux
Was:
> 5. The suite passes on both runners. Until 0.4
Now:
> 5. The suite passes on both platforms: on macOS by the hosted runner, and on Windows by `tools/ci.ps1` on the operator's machine, which the PROGRESS entry records in the words `tools/ci.ps1` green, because no hosted runner checks Windows (see: Windows is verified on the operator's machine, and the hosted runners are macOS and Linux). Until 0.4
Why: no hosted runner checks Windows any more, so the condition says what stands in for it and in what words the record carries it, which is what `two-platform` reads.

### 2026-09-15 - CLAUDE.md - a merge also waits on the Windows run on the operator's machine

Authorised by: Windows is verified on the operator's machine, and the hosted runners are macOS and Linux
Was:
> **CI green before merge. That is the only condition.**
Now:
> **CI green before merge, and `tools/ci.ps1` green on the operator's machine over the tree being merged. Those are the only conditions.**
Why: CI no longer runs the suite on Windows, so green CI alone would let a change reach the checkout the nightly runs from with no Windows result behind it.

### 2026-09-15 - CLAUDE.md - a planning pass's entry lands its checkpoint and never its phase

Corrects: the due-point reader skipped every PROGRESS entry opening "Not a checkpoint entry", which is how a planning pass is recorded, so no planning checkpoint ever landed and an obligation owed at one could not be read as passed; found by the phase 6 sign-off, which probed it by moving a row's due point to 6.0
Was:
> A PROGRESS entry for such a pass opens with **"Not a checkpoint entry"** so it says which checkpoint it belongs to without saying that checkpoint has landed.
Now:
> A PROGRESS entry for such a pass is headed with that checkpoint and the word planning, as `### 2.0 planning - ...`, and opens with **"Not a checkpoint entry"**, because it lands that checkpoint and never its phase: planning a phase builds none of it. An entry opening that way under any other heading, a ruling among them, lands nothing.
Why: 7.1's reader lands a phase's opening checkpoint from that entry and never the phase, and tells it from a ruling by its heading, so the sentence says what the entry lands and how it is headed rather than that it says nothing has landed.

### 2026-09-15 - BUILD_PLAN.md - the due-point reader's repair row discharged at 7.1

Corrects: the due-point reader skipped every PROGRESS entry opening "Not a checkpoint entry", which is how a planning pass is recorded, so no planning checkpoint ever landed and an obligation owed at one could not be read as passed; found by the phase 6 sign-off, which probed it by moving a row's due point to 6.0
Was:
> | **A planning checkpoint lands with its planning entry** | 6.11 sign-off | 7.1 | the due-point reader skips every entry opening "Not a checkpoint entry", which is how the pass that plans a phase is recorded, so no planning checkpoint lands and a row still open at one passes for as long as the plan runs. The phase 6 sign-off probed it by moving the volume profile row's due point to 6.0 and leaving the check green, with 6.5 turning it red as the control, and ruled it a repair owed before the next planning pass's entry. 7.1 produces the reader that lands a planning checkpoint from its planning entry and never its phase from it, with a permanent test over a constructed plan, table and record |
Now:
> the row reads discharged at 7.1, with what the reader lands now, the proofs that hold it and what it read before.
Why: 7.1 built the reader and the permanent proofs the row was carried for.

### 2026-09-15 - CLAUDE.md - the register-append-only roster row starts at 8.1

Authorised by: Phase 6's carried items are built as phase 7, and the improvement loop is phase 8
Was:
> | `register-append-only` | from 7.1 |
Now:
> | `register-append-only` | from 8.1 |
Why: the candidate register is built by the loop's first checkpoint that the roster row names, which is 8.1 since the loop moved to phase 8.

### 2026-09-15 - ARCHITECTURE.html - section 13 names phase 8 as the improvement loop's phase

Authorised by: Phase 6's carried items are built as phase 7, and the improvement loop is phase 8
Was:
> which is why the other three sit in phase 7 and why phase 5 has an obligation described below.
>
> <p>The loop is a phase 7 feature. But it cannot be added in phase 7 unless phase 5 stores the right things,
>
> because by the time phase 7 arrives the rules may have changed
>
> the difference between phase 7 being a feature and phase 7 being a six-month wait.
>
> no checkpoint accumulates nights. Phase 7 adds the scoring, the register,
Now:
> which is why the other three sit in phase 8 and why phase 5 has an obligation described below.
>
> <p>The loop is a phase 8 feature. But it cannot be added in phase 8 unless phase 5 stores the right things,
>
> because by the time phase 8 arrives the rules may have changed
>
> the difference between phase 8 being a feature and phase 8 being a six-month wait.
>
> no checkpoint accumulates nights. Phase 8 adds the scoring, the register,
Why: the loop moved to phase 8; each sentence says which phase builds it and why phase 5 stores what it needs first, which is unchanged.

### 2026-09-15 - ARCHITECTURE.html - section 20 gains phase 7's row and the later row becomes phase 8

Authorised by: Phase 6's carried items are built as phase 7, and the improvement loop is phase 8
Was:
> <tr><td><b>7. Later</b></td>
Now:
> <tr><td><b>7. Phase 6's carried items</b></td><td>the operator's ruling on a suspect name's own surfaces with its weekly retry, and the due-point reader landing a planning checkpoint from its planning entry</td><td>a suspect name's page, its exported report and its row on tonight's list saying its prices may not reflect a dividend or split</td><td>a spent suspect name asked for weekly and neither asked for nor named once it leaves the index; an open obligation due at a planning checkpoint failing once that checkpoint's planning entry is recorded</td><td>both gates are green with every phase 7 claim PASS and none unexamined, and the pair 7.0 predicted is checked against the actual</td></tr>
> <tr><td><b>8. Later</b></td>
Why: the phase table states each phase's visible output and done condition, and phase 7 is now phase 6's carried items, so it gains that phase's row ahead of the later one, which moves to 8.

### 2026-09-15 - BUILD_PLAN.md - phase 7 written for phase 6's carried items, and the improvement loop moved to phase 8

Authorised by: Phase 6's carried items are built as phase 7, and the improvement loop is phase 8
Was:
> ## Phase 7: the improvement loop
>
> **Visible output at 7.1.** The resolution counts appear on a run page that already exists.
>
> ### 7.0 Planning
> Confirms the guardrail values against the evidence that has accumulated. Nothing here is tuned to what the data turned out to be; a threshold changed after seeing results starts a new window and the old one is kept.
>
> Decides the fundamental analysis, being four readings over the twelve stored quarters and the three questions about what they are for: the computed trajectory, the guide record, earnings quality and the valuation position, each with the range it is placed in and the count of quarters behind it, and then whether the four earn a panel, whether a state transition on one of them earns a seventh reason, and whether a computed fundamental state may gate a tranche (owes: The computed fundamental panel, and whether a fundamental state may fire a reason or gate a tranche). The evidence is in hand by this pass rather than produced by it, since 6.1 stores the filings and the research checkpoints write the first reports to read a panel against, which is why 6.1 filed the question rather than answering it.
>
> Narrows what the volume profile is allowed to claim, before anything scores it (owes: The volume profile's claim narrowed to a price region at a day's resolution). Its evidence is in hand as well: the profile has been stored since 3.3 over four names of different character, and the sessions a multiple of the typical daily move sets aside are countable off the stored bars.
>
> Rules two things 6.11's production run found and left as they were, because each is a ruling rather than a repair: whether a pass for a name with no facts file refreshes its industry's theme (owes: A name with no facts file refreshes its industry's theme, ruled), and what a facts file carries of a segment table after an annual filing or an archive the fetch could not read (owes: The segment figures a facts file carries after an annual filing or an archive the fetch could not read). Their evidence is in hand rather than produced by this pass: the run's passes on the fixture's names, recorded in `PROGRESS.md`, and the code that decides both.
>
> Rules what a suspect name's own surfaces say, which the phase 6 sign-off carried here and the operator ruled ahead of the pass: a name whose retries are spent is asked for again weekly rather than waiting for another action, and its name page, its row on tonight's list and its exported report say its prices may not reflect a dividend or split (owes: A suspect name's own surfaces say so, ruled). With it, the property the check's membership clause carries is asserted by behaviour rather than by a source scan alone, since a name asked for weekly is asked for until it leaves the index (owes: A suspect name the index no longer holds is neither asked for nor named, asserted by behaviour).
>
> **Decides what the momentum panel is for, rather than carrying it.** It reaches no decision, feeds no band and gates nothing, which makes it the only part of the technical half with no stated purpose. Either the corpus says it is context a reader weighs and that nothing computes with it, which is a sentence in section 15 and a claim the panel's own row can carry, or it is given a job, which is a component that reads it and a rule that says what it decides. It is decided here rather than filed as an obligation because nothing produces evidence for it: the panel has been drawn since 3.5 and the question is what the corpus intends, not what a measurement would show.
>
> ### 7.1 Setup resolution
> The setup horizon on forward returns: target reached, stop closed through, or the time cap expired. The resolution counts on the run page.
>
> **Done when** each of the three outcomes has its own test, a timed-out setup is counted in its own column and never in a rate, and the counts render.
>
> ### 7.2 The break-even score
> The hit rate each plan demanded, computed from its own entry, stop and target, and the share of setups that beat it.
>
> **Done when** the arithmetic is asserted against worked cases, and a verdict below the minimum is withheld with its count shown against the minimum.
>
> ### 7.3 The candidate register
> Migration creating `candidate_register`, append only. Registration before scoring, with the rule, the test and the date, and a stated maximum family size.
>
> **Done when** an update and a delete are both refused at the store, a retirement is a new row naming what it retires, and the correction divisor matches the rows registered before the window opened.
>
> ### 7.4 The shadow column
> Registered candidates evaluated nightly on every name-night exactly as live reasons are, written to the shadow column and shown nowhere.
>
> **Done when** a shadow candidate is evaluated on nights no live reason fired, which is what the every-name listing row exists for, and nothing shadow reaches any screen.
>
> ### 7.5 Reason verdicts on the run page
> Each reason against the bar its own setups demanded, with the resolved count and the divisor beside it, and the base rate pinned.
>
> **Done when** no verdict appears below its minimum, every verdict names its divisor, and the three display states are each proved.
>
> ### 7.6 Rule versions scored counterfactually
> Each ladder rule a named version, every night scored under every version from stored bars.
>
> **Done when** a version change opens a new window and the previous one is kept, and a rule is not changed while a window measuring it is open.
>
> ### 7.7 The model's proposal
> The model's own entry and exit proposal stored beside the computed one and scored on the same break-even rule, never overriding it.
>
> **Done when** the proposal is stored, scored and displayed as a second opinion, and no computed number is sourced from it.
>
> ### 7.8 Phase 7 report
> **Done when** every guardrail has its own test, and the loop has changed nothing on the strength of evidence below its stated minimum.
Now:
> ## Phase 7: phase 6's carried items
>
> **Visible output at 7.0.** A suspect name's page, its exported report and its row on tonight's list say its prices may not reflect a dividend or split.
>
> The items the phase 6 sign-off carried forward, made a phase of their own by the operator's ruling on 2026-09-14 (see: Phase 6's carried items are built as phase 7, and the improvement loop is phase 8). The first of them was ruled and committed under 7.0 while 7.0 was still the improvement loop's planning pass and that phase had not started, so the carried items take phase 7 and the improvement loop moves to phase 8, with every due point, placement, roster row and line on a page that named one of its checkpoints.
>
> ### 7.0 Planning
> This pass: it writes phase 7, moves the improvement loop from phase 7 to phase 8, and enters what the phase 6 sign-off and the ruling below carried. It builds nothing.
>
> Rules what a suspect name's own surfaces say, which the phase 6 sign-off carried here and the operator ruled ahead of the pass: a name whose retries are spent is asked for again weekly rather than waiting for another action, and its name page, its row on tonight's list and its exported report say its prices may not reflect a dividend or split (owes: A suspect name's own surfaces say so, ruled). With it, the property the check's membership clause carries is asserted by behaviour rather than by a source scan alone, since a name asked for weekly is asked for until it leaves the index (owes: A suspect name the index no longer holds is neither asked for nor named, asserted by behaviour).
>
> **The claims phase 7 predicts.** 350 claims and 334 PASS after phase 7, with 16 out of scope, 0 unexamined and 0 fail. The ruling added its 4 claims at 7.0 and all 4 pass, this pass adds none and moves none into or out of scope, and 7.1 and 7.2 add none, so the pair is the one the ruling left. The 16 are all at phase 8: 2 at 8.1, 3 at 8.3, 1 at 8.4, 9 at 8.5 and 1 at 8.6, which is where they stood at 7.1, 7.3, 7.4, 7.5 and 7.6 before the move. 7.2 checks the pair.
>
> **Done when** no due point, placement, roster row, open obligation or line on a page names a checkpoint of the improvement loop by its phase 7 number, the 16 out-of-scope claims read by the first and by the last checkpoint each note names are all at phase 8, and what the phase 6 sign-off carried is entered in the table.
>
> ### 7.1 A planning checkpoint lands with its planning entry
> The due-point reader answers whether a planning checkpoint has landed from the entry of the pass that plans its phase, and answers whether the phase itself has landed only from an entry that is not a planning pass, with its permanent proof turned and CLAUDE.md's convention moved with it (owes: A planning checkpoint lands with its planning entry). The phase 6 sign-off found the reader skipping every entry that opens "Not a checkpoint entry", which is how the pass that plans a phase is recorded, so no planning checkpoint ever landed and an obligation due at one could not fail; the one time the shape arose it hid the only row it could.
>
> **Done when** an open obligation due at a planning checkpoint passes before that checkpoint's planning entry is recorded and fails after it, asserted over a constructed plan, table and record; a planning entry lands its checkpoint and never its phase; and an entry opening the planning way at a planning checkpoint that is not its planning pass, being a ruling, lands neither.
>
> ### 7.2 Phase 7 report
> The phase report over phase 7, with the pair 7.0 predicted checked against the actual, and the two verdict notes on the read and write matrix that say all eleven cells are blank over rows of thirteen, the mark renderer's and the single page app's, corrected.
>
> **Done when** every phase 7 claim is PASS naming an instrument whose declared reach includes it, unexamined is zero, the pair 7.0 predicted is checked against the actual with every claim that moved named, and no verdict note on the read and write matrix states a count of cells its row does not carry.
>
> followed by the section above as `## Phase 8: the improvement loop`, its visible output at 8.1 and its checkpoints numbered 8.0 to 8.8 with 8.8 named Phase 8 report, the paragraph ruling what a suspect name's own surfaces say moved to 7.0, and this paragraph added at 8.0 before the momentum panel's:
>
> Rules how a count a verdict note states is kept to the row it describes (owes: A count a verdict note states read off the row it describes). The read and write matrix's note for the read API said eleven reads over a row the 7.0 ruling's series state read had made twelve until that ruling corrected it, and the notes for the mark renderer and the single page app say all eleven cells are blank over rows of thirteen, which 7.2 corrects, because a count typed into a note is read against nothing and the phase report prints it whatever the row holds. The evidence is in hand rather than produced by this pass: the three notes and the rows they describe.
Why: the phase 6 sign-off's carried items were committed under 7.0 while 7.0 was the improvement loop's planning pass and that phase had not started, and the operator ruled that they are phase 7 and the loop phase 8, so the plan carries phase 7's three checkpoints with the claims it predicts and the loop under its new number.

### 2026-09-15 - BUILD_PLAN.md - the loop's checkpoints named in 5.0's, 5.6's and 6.0's text and in the carried obligations table moved to phase 8, and two rows entered

Authorised by: Phase 6's carried items are built as phase 7, and the improvement loop is phase 8
Was:
> its counts at 5.6 and its verdicts at 7.5, because
>
> the harness had the whole row at 7.5.
>
> the reason record's verdict half is 7.5,
>
> The verdicts are 7.5's, because
>
> give 77 at phase 6 and 16 at phase 7 both ways
>
> | 6.1 | 7.0 | what the profile is allowed to say
>
> 7.0 is where it falls due because that is the pass before anything scores a feature
>
> | 6.1 | 7.0 | the fundamental analysis
>
> 7.0 is the planning pass that reads what phase 6 produced before phase 7 builds on it
>
> | 6.11 | 7.0 | a name's pass has the theme research runner
>
> What 7.0 has is those runs and that fixture
>
> | 6.11 | 7.0 | the facts assembler carries the latest quarter
>
> What 7.0 has is that run and the two components' code
>
> 29 at phase 6 and 12 at phase 7, and every one
>
> 250 resolved event-book setups, read on the run page, which 5.6 builds and 7.5 fills with verdicts.
>
> which is the minimum section 17 already states, read on the run page, which 5.6 builds and 7.5 fills with verdicts. Phase 7 arriving
Now:
> its counts at 5.6 and its verdicts at 8.5, because
>
> the harness had the whole row at 8.5.
>
> the reason record's verdict half is 8.5,
>
> The verdicts are 8.5's, because
>
> give 77 at phase 6 and 16 at phase 8 both ways
>
> | 6.1 | 8.0 | what the profile is allowed to say
>
> 8.0 is where it falls due because that is the pass before anything scores a feature
>
> | 6.1 | 8.0 | the fundamental analysis
>
> 8.0 is the planning pass that reads what phase 6 produced before phase 8 builds on it
>
> | 6.11 | 8.0 | a name's pass has the theme research runner
>
> What 8.0 has is those runs and that fixture
>
> | 6.11 | 8.0 | the facts assembler carries the latest quarter
>
> What 8.0 has is that run and the two components' code
>
> 29 at phase 6 and 12 at phase 8, and every one
>
> 250 resolved event-book setups, read on the run page, which 5.6 builds and 8.5 fills with verdicts.
>
> which is the minimum section 17 already states, read on the run page, which 5.6 builds and 8.5 fills with verdicts. Phase 8 arriving
>
> and two rows entered after the two the 7.0 ruling discharged:
>
> | **A planning checkpoint lands with its planning entry** | 6.11 sign-off | 7.1 | the due-point reader skips every entry opening "Not a checkpoint entry", which is how the pass that plans a phase is recorded, so no planning checkpoint lands and a row still open at one passes for as long as the plan runs. The phase 6 sign-off probed it by moving the volume profile row's due point to 6.0 and leaving the check green, with 6.5 turning it red as the control, and ruled it a repair owed before the next planning pass's entry. 7.1 produces the reader that lands a planning checkpoint from its planning entry and never its phase from it, with a permanent test over a constructed plan, table and record |
> | **A count a verdict note states read off the row it describes** | 7.0 ruling | 8.0 | a verdict note in the harness's placement map states counts about the row it passes, being its cells, its reads and its stores, and each count is typed into the note and read against nothing, so the phase report prints it whatever the row holds. The 7.0 ruling found the read API's matrix note at eleven reads over a row its series state read had made twelve and corrected it, and found the mark renderer's and the single page app's notes at all eleven cells blank over rows of thirteen, which 7.2 corrects. What 8.0 has is those three notes and the rows they describe, and what it rules is whether a note's count is read off the row, written without a count, or stated as the limit it is |
Why: each names a checkpoint of the loop, which moved with it, or a row due at its planning pass, which is 8.0 now; 7.0 enters the repair the phase 6 sign-off ruled owed, due at 7.1, and the count the 7.0 ruling found typed into verdict notes, due at 8.0, which cites it.

### 2026-09-14 - ARCHITECTURE.html - section 7's corporate action checker row asks for a spent name weekly and names where it is seen

Authorised by: A suspect name is asked for again on the five nights after it is marked and weekly after that, and its own page, its row on tonight's list and the run page say so until a refetch succeeds
Was:
> and is refetched again on the nights after until one succeeds or its retries are spent, and a name whose retries are spent stays suspect and named on the run page on every night until another action lands on it</td></tr>
Now:
> and is refetched again on the nights after, nightly while its retries last and weekly once they are spent, until one succeeds or it leaves the index, staying suspect until then with its name page, its row on tonight's list and the run page saying so</td></tr>
Why: a name whose retries are spent is asked for again every 7 days rather than waiting for another action, and its name page and its row on tonight's list say so beside the run page.

### 2026-09-14 - ARCHITECTURE.html - section 15.7's list reads series state and draws a line beside a suspect name

Authorised by: A suspect name is asked for again on the five nights after it is marked and weekly after that, and its own page, its row on tonight's list and the run page say so until a refetch succeeds
Was:
> <p><b>Reads:</b> the listings, ladders, levels and facts for that night, and the run log for the header.</p>
> Each row: name, close, day change, trend state in a word, the distance row mark, and the reasons</td></tr>
Now:
> <p><b>Reads:</b> the listings, ladders, levels and facts for that night, the run log for the header, and series state for the rows whose prices may not reflect a dividend or split, which this list did not name until 7.0.</p>
> Each row: name, close, day change, trend state in a word, the distance row mark, the reasons, and beside the name a line saying so where its prices may not reflect a dividend or split</td></tr>
Why: a row read from the list says where the name's prices may not carry a dividend's or a split's adjustment, which the list could not while nothing on it read the store that says so.

### 2026-09-14 - ARCHITECTURE.html - section 15.9 reads series state and opens with a line for a suspect name

Authorised by: A suspect name is asked for again on the five nights after it is marked and weekly after that, and its own page, its row on tonight's list and the run page say so until a refetch succeeds
Was:
> which the page has read since 6.6 and this list did not name until 6.8.</p>
> and no region for a name whose stored series is suspect; the table opened with `Why it is here`
Now:
> which the page has read since 6.6 and this list did not name until 6.8, and series state, for whether the name's prices may not reflect a dividend or split, which the page reads from 7.0.</p>
>   <tr><td>Prices may be out of date</td><td>present only when the name's stored series is suspect: one line saying its prices may not reflect a recent dividend or split, with when the refetch was last tried and why it failed, above everything the page draws from those prices</td></tr>
Why: every figure on the page is computed over prices that may not carry the action, so the page opens with the line before any of them, and the exported report carries it because it is the page's region.

### 2026-09-14 - ARCHITECTURE.html - section 16's matrix gives the read API its series state read

Authorised by: A suspect name is asked for again on the five nights after it is marked and weekly after that, and its own page, its row on tonight's list and the run page say so until a refetch succeeds
Was:
>   <tr><td>Read API</td><td><span class="r">R</span></td><td><span class="r">R</span></td><td><span class="r">R</span></td><td><span class="r">R</span></td><td><span class="r">R</span></td><td><span class="r">R</span></td><td><span class="r">R</span></td><td><span class="r">R</span></td><td><span class="r">R</span></td><td><span class="r">R</span></td><td><span class="r">R</span></td><td></td><td><span class="r">R</span> <span class="w">W</span></td></tr>
Now:
>   <tr><td>Read API</td><td><span class="r">R</span></td><td><span class="r">R</span></td><td><span class="r">R</span></td><td><span class="r">R</span></td><td><span class="r">R</span></td><td><span class="r">R</span></td><td><span class="r">R</span></td><td><span class="r">R</span></td><td><span class="r">R</span></td><td><span class="r">R</span></td><td><span class="r">R</span></td><td><span class="r">R</span></td><td><span class="r">R</span> <span class="w">W</span></td></tr>
Why: the read API reads `series_state` for the name page's line and the list row's, which `component-access` holds cell by cell to the component's own declaration.

### 2026-09-14 - ARCHITECTURE.html - section 17's per-name row bounds a spent name's retries at one request every 7 days

Authorised by: A suspect name is asked for again on the five nights after it is marked and weekly after that, and its own page, its row on tonight's list and the run page say so until a refetch succeeds
Was:
> and a name whose refetch failed is asked for again on at most 5 nights after the one that marked it, a failure on a night an action lands on it starting the count again, so one action costs at most 6 requests however long its failure lasts. That is bounded by the actions of the day and of the 5 nights before it rather than by the universe, and on the fixture's captured day it is one name in five hundred. A name whose retries are spent is not asked for again until another action lands on it, and stays suspect and named on the run page on every night until one does (see: A suspect name's retries are bounded, and a name whose retries are spent stays suspect and named on the run page until another action lands on it).
Now:
> and a name whose refetch failed is asked for again on each of the 5 nights after the one that marked it and then every 7 days until a refetch succeeds or the name leaves the index, a failure on a night an action lands on it starting the count again, so one action costs 6 requests over the 6 nights from the one it lands on and 1 every 7 days after that however long its failure lasts. That is bounded by the actions of the day and of the 5 nights before it, and by one request every 7 days for each name whose retries are spent, rather than by the universe, and on the fixture's captured day it is one name in five hundred. A name whose retries are spent stays suspect, and its name page, its row on tonight's list and the run page say so until a refetch succeeds (see: A suspect name is asked for again on the five nights after it is marked and weekly after that, and its own page, its row on tonight's list and the run page say so until a refetch succeeds).
Why: a failure that outlasts the nightly retries now costs one request every 7 days rather than none, for as long as the name is in the index, and the row states that bound in digits beside the one it had.

### 2026-09-14 - ARCHITECTURE.html - section 18's corporate action row asks for a spent name weekly and says so on the name's own surfaces

Authorised by: A suspect name is asked for again on the five nights after it is marked and weekly after that, and its own page, its row on tonight's list and the run page say so until a refetch succeeds
Was:
> if the check itself fails the name is marked suspect, and on the nights after it refetches it again, with or without an action of its own that day, until one refetch succeeds or the name's retries are spent; a name whose retries are spent is not refetched again until another action lands on it, and stays suspect with the reason its last refetch failed for (see: Adjusted history is re-fetched after a corporate action) (see: A suspect name's retries are bounded, and a name whose retries are spent stays suspect and named on the run page until another action lands on it)</td>
> <td>the run page's stale and failed region lists the check as partial, naming the suspect name, on every night the name stays suspect, and from the night its retries are spent says so with when it was last asked for and why</td>
> The retries are bounded because a failure that lasts would otherwise make a per-name request on every night it lasts, and a name whose retries are spent is named rather than dropped because a check that stopped asking in silence would look exactly like one that found nothing</td>
Now:
> if the check itself fails the name is marked suspect, and it refetches it again on each of the nights after, with or without an action of its own that day, until one refetch succeeds or the name's retries are spent, and after that on the first night a week or more after it was last asked for, until one succeeds or the name leaves the index; the name stays suspect with the reason its last refetch failed for, and its figures are computed over its stored series (see: Adjusted history is re-fetched after a corporate action) (see: A suspect name is asked for again on the five nights after it is marked and weekly after that, and its own page, its row on tonight's list and the run page say so until a refetch succeeds)</td>
> <td>the name page opens with a line saying its prices may not reflect a recent dividend or split, with when the refetch was last tried and why it failed, and the exported report carries it; the name's row on tonight's list says so beside the name; and the run page's stale and failed region lists the check as partial, naming the suspect name, on every night the name stays suspect, and on each night its retries are spent and it is not asked for says so with when it was last asked for and why</td>
> The retries are bounded because a failure that lasts would otherwise make a per-name request on every night it lasts, and asked for weekly once spent because a failure the provider has cleared would otherwise stay in the store until another action landed. A suspect name is named on its own surfaces because its figures are computed over prices that may not carry the action, and rather than dropped because a check that stopped asking in silence would look exactly like one that found nothing</td>
Why: the phase 6 sign-off found the run page the one surface naming a suspect name, while its name page, its row on tonight's list and its exported report drew its figures with nothing beside them, and a spent name stayed so until another action landed; the operator ruled both.

### 2026-09-14 - SCHEMA.md - series_state's count goes on past the limit and times a weekly retry from `checked_at`

Authorised by: A suspect name is asked for again on the five nights after it is marked and weekly after that, and its own page, its row on tonight's list and the run page say so until a refetch succeeds
Was:
> and a name at the limit is not asked for again until one does (see: A suspect name's retries are bounded, and a name whose retries are spent stays suspect and named on the run page until another action lands on it).
> | `checked_at` | TEXT | UTC instant of the check that set this |
Now:
> and a name at the limit is asked for again on the first night whose session is 7 or more days after the session of its `checked_at`, with the count going on past the limit, until a refetch succeeds (see: A suspect name is asked for again on the five nights after it is marked and weekly after that, and its own page, its row on tonight's list and the run page say so until a refetch succeeds). The read API reads the row as stored from the 7.0 ruling, for the line the name page opens with and the one tonight's list draws beside the name.
> | `checked_at` | TEXT | UTC instant of the check that set this, which times a spent name's weekly retry from its session |
Why: the check reads the instant it already writes to decide a spent name's weekly retry, and the read API reads the row, so the file says both without the table changing shape.

### 2026-09-14 - RUNBOOK.md - a suspect name's row says it is asked for weekly and flagged on its page and its list row

Authorised by: A suspect name is asked for again on the five nights after it is marked and weekly after that, and its own page, its row on tonight's list and the run page say so until a refetch succeeds
Was:
> | A name is marked suspect | the corporate action check itself failed | nothing at first: the check asks for the name's year again on each of the next 5 nights, and the run page's failed region names the name on every night it stays suspect. A night re-run by hand counts as one of them, except a re-run of the night the action landed, which finds the action again and starts the count at none. Where the line says its retries are spent, the refetch has failed on 6 nights running, counting a night re-run by hand as one, and the line gives when it was last asked for and why, so read the reason: the name's levels are computed over its stored series, which does not carry the action's adjustment, and the check does not ask for it again until another action lands on it. No verb asks for one name's year, and the store is never edited by hand |
Now:
> | A name is marked suspect | the corporate action check itself failed | nothing at first: the check asks for the name's year again on each of the next 5 nights and then weekly, and the name's page opens with a line saying its prices may not reflect a recent dividend or split, its row on tonight's list says so, and the run page's failed region names the name, on every night it stays suspect. A night re-run by hand counts as one of the 5, except a re-run of the night the action landed, which finds the action again and starts the count at none. Where the run page's line says its retries are spent, the refetch has failed on 6 nights running, counting a night re-run by hand as one, and the line gives when it was last asked for and why, so read the reason: the name's levels are computed over its stored series, which does not carry the action's adjustment, and the check asks for it again 7 days after the session it was last asked for, and every 7 days after that, until a refetch succeeds or the name leaves the index, when the lines go. No verb asks for one name's year, and the store is never edited by hand |
Why: the row sends the operator to what the pages show, and a spent name is now asked for every 7 days until a refetch succeeds or it leaves the index rather than until another action lands.

### 2026-09-14 - BUILD_PLAN.md - 7.0 cites the two items the operator ruled ahead of it, entered discharged, and 6.0's row cites the decision that superseded its own

Authorised by: A suspect name is asked for again on the five nights after it is marked and weekly after that, and its own page, its row on tonight's list and the run page say so until a refetch succeeds
Was:
> Their evidence is in hand rather than produced by this pass: the run's passes on the fixture's names, recorded in `PROGRESS.md`, and the code that decides both.
> | **A suspect name's retries bounded and decided** | 5.7 sign-off | 6.0, discharged | ruled, with the bound in the check and the decision it wrote (see: A suspect name's retries are bounded, and a name whose retries are spent stays suspect and named on the run page until another action lands on it). Found unwritten at 6.11, after the phase's other checkpoints had landed, and ruled on 2026-09-14. A suspect name is asked for again on at most 5 nights after the one that marked it, counted on its series state row from migration 23, and a failure on a night an action lands on the name starts the count again, so one action costs at most 6 requests however long its failure lasts. A name whose retries are spent is not asked for again until another action lands on it, stays suspect with its reason, and keeps the check's stage partial and naming it on every night it stays so, which is how the run page's stale and failed region lists it. Section 17's refetch row states the bound and `nightly-cost` asserts it over a constructed suspect name on the nights of its retries and after, and `corporate-actions` asserts what a name whose retries are spent is left as and the region that lists it |
Now:
> Their evidence is in hand rather than produced by this pass: the run's passes on the fixture's names, recorded in `PROGRESS.md`, and the code that decides both.
> 
> Rules what a suspect name's own surfaces say, which the phase 6 sign-off carried here and the operator ruled ahead of the pass: a name whose retries are spent is asked for again weekly rather than waiting for another action, and its name page, its row on tonight's list and its exported report say its prices may not reflect a dividend or split (owes: A suspect name's own surfaces say so, ruled). With it, the property the check's membership clause carries is asserted by behaviour rather than by a source scan alone, since a name asked for weekly is asked for until it leaves the index (owes: A suspect name the index no longer holds is neither asked for nor named, asserted by behaviour).
> | **A suspect name's retries bounded and decided** | 5.7 sign-off | 6.0, discharged | ruled, with the bound in the check and the decision it wrote, which the operator's ruling at 7.0 superseded by asking for a spent name weekly and saying so on its own surfaces (see: A suspect name is asked for again on the five nights after it is marked and weekly after that, and its own page, its row on tonight's list and the run page say so until a refetch succeeds). Found unwritten at 6.11, after the phase's other checkpoints had landed, and ruled on 2026-09-14. A suspect name is asked for again on at most 5 nights after the one that marked it, counted on its series state row from migration 23, and a failure on a night an action lands on the name starts the count again, so one action costs at most 6 requests however long its failure lasts. A name whose retries are spent is not asked for again until another action lands on it, stays suspect with its reason, and keeps the check's stage partial and naming it on every night it stays so, which is how the run page's stale and failed region lists it. Section 17's refetch row states the bound and `nightly-cost` asserts it over a constructed suspect name on the nights of its retries and after, and `corporate-actions` asserts what a name whose retries are spent is left as and the region that lists it |
> | **A suspect name's own surfaces say so, ruled** | 6.11 sign-off | 7.0, discharged | ruled by the operator ahead of the pass (see: A suspect name is asked for again on the five nights after it is marked and weekly after that, and its own page, its row on tonight's list and the run page say so until a refetch succeeds): a name whose retries are spent is asked for again on the first night 7 or more days after the session it was last asked for, until a refetch succeeds or it leaves the index, and its name page opens with a line saying its prices may not reflect a recent dividend or split, with when the refetch was last tried and why, which its exported report carries, and its row on tonight's list says so beside the name. Asserted under `corporate-actions` over a constructed name whose refetch fails, under `nightly-cost` against the derived expectation `suspect-retries`, and under `read-surface` on the routes the page, the list and the file are served from |
> | **A suspect name the index no longer holds is neither asked for nor named, asserted by behaviour** | 6.11 sign-off | 7.0, discharged | a test under `corporate-actions` spends a suspect name's retries, sets its leave date before a night its week has come round, and asserts the check neither asks for it nor names it, where the phase 6 sign-off's sweep found the property held by `EveryMembershipReadIsOneOfTheTwoFormsOverTheSession`'s scan alone |
Why: the phase 6 sign-off carried both items for 7.0 to enter, and the ruling that discharges them lands ahead of the pass, so the rows are entered discharged with 7.0's text citing them back, and 6.0's row cites a current decision rather than the one this supersedes.

### 2026-09-14 - RUNBOOK.md - a suspect name's row gives the reason where the line does, and says a hand re-run of the action's night starts the count again

Corrects: the ruling's first commit, which stated that the run page's failed region names a suspect name and its reason on every night it stays suspect and that any night re-run by hand counts as one of its retries, found while writing the ruling's record by reading the stage's detail and the check's count against the row. The region gives the reason from the night the retries are spent, and a re-run of the night the action landed finds the action again and starts the count at none.
Was:
> nothing at first: the check asks for the name's year again on each of the next 5 nights, and the run page's failed region names the name and the reason on every night it stays suspect. A night re-run by hand counts as one of them. Where the line says its retries are spent, the refetch has failed on 6 nights running, so read the reason:
Now:
> nothing at first: the check asks for the name's year again on each of the next 5 nights, and the run page's failed region names the name on every night it stays suspect. A night re-run by hand counts as one of them, except a re-run of the night the action landed, which finds the action again and starts the count at none. Where the line says its retries are spent, the refetch has failed on 6 nights running, counting a night re-run by hand as one, and the line gives when it was last asked for and why, so read the reason:
Why: the row sends the operator to a line, so it states what the line carries and from which night, and what a night re-run by hand does to the count.

### 2026-09-14 - ARCHITECTURE.html - section 18's corporate action row gives the reason on the run page from the night a suspect name's retries are spent, where the check writes it

Corrects: the ruling's first commit, which stated that the run page's stale and failed region names a suspect name and why on every night it stays suspect, found while writing the ruling's record by reading the stage's detail against the row. The check names a suspect name alone on the nights its refetch fails, and adds when it was last asked for and why from the night its retries are spent.
Was:
> <td>the run page's stale and failed region lists the check as partial, naming the suspect name and why, on every night the name stays suspect, and says its retries are spent once they are</td>
Now:
> <td>the run page's stale and failed region lists the check as partial, naming the suspect name, on every night the name stays suspect, and from the night its retries are spent says so with when it was last asked for and why</td>
Why: a row stating what a person sees states what the region draws, and the reason is drawn where the runbook sends the operator to read it, once the retries are spent.

### 2026-09-14 - ARCHITECTURE.html - section 16's series state row carries the retry count

Authorised by: A suspect name's retries are bounded, and a name whose retries are spent stays suspect and named on the run page until another action lands on it
Was:
> <tr><td><b>Series state</b></td><td>per name, whether its stored series can be trusted, with the reason when it cannot and the instant the check that said so ran</td><td>current state only, one row per name</td></tr>
Now:
> <tr><td><b>Series state</b></td><td>per name, whether its stored series can be trusted, with the reason when it cannot, the instant the check that said so ran, and how many nights after the one that marked it a suspect name has been asked for again</td><td>current state only, one row per name</td></tr>
Why: the store carries the count the check reads to bound a suspect name's retries from migration 23, and this is the row that states the store's columns, which `schema-columns` holds to SCHEMA.md.

### 2026-09-14 - ARCHITECTURE.html - section 19.1's series state row returned to what its expectation reads

Corrects: the ruling's first commit, which stated the retry count on section 19.1's fixture row for series state, taken for the store row, found while writing the ruling's record. That row's expectation reads each name's state and none's count, so the part would have passed with nothing asserting it.
Was:
> <tr><td>series state</td><td>which names the corporate action check left trusted and which it marked suspect, with how many nights each suspect name has been asked for again</td><td>section 7's corporate action checker</td></tr>
Now:
> <tr><td>series state</td><td>which names the corporate action check left trusted and which it marked suspect</td><td>section 7's corporate action checker</td></tr>
Why: a fixture row states what its expectation holds, and the count is stated on section 16's store row instead.

### 2026-09-14 - BUILD_PLAN.md - the suspect name's retries obligation discharged by the ruling 6.0 owed

Authorised by: A suspect name's retries are bounded, and a name whose retries are spent stays suspect and named on the run page until another action lands on it
Was:
> | **A suspect name's retries bounded and decided** | 5.7 sign-off | 6.0 | the action check refetches every suspect name every night with no cap, and section 17's refetch row, pinned by `nightly-cost`, still bounds the refetch by the day's actions while `nightly-cost` measures the fetch alone; no decision covers the retry. No name is suspect today. 6.0 rules the bound and writes the decision, and the limits row and its assertion move with it |
Now:
> | **A suspect name's retries bounded and decided** | 5.7 sign-off | 6.0, discharged | ruled, with the bound in the check and the decision it wrote (see: A suspect name's retries are bounded, and a name whose retries are spent stays suspect and named on the run page until another action lands on it). Found unwritten at 6.11, after the phase's other checkpoints had landed, and ruled on 2026-09-14.
Why: 6.11 found the row still owed at 6.0 with no decision written for it, and the ruling that discharges it is the one the row asked for.

### 2026-09-14 - RUNBOOK.md - a suspect name's row says what the check does across its retries and after them

Authorised by: A suspect name's retries are bounded, and a name whose retries are spent stays suspect and named on the run page until another action lands on it
Was:
> | A name is marked suspect | the corporate action check itself failed | re-run the night. If it recurs, the name's adjusted history and the provider's have diverged and the year needs a manual refetch |
Now:
> | A name is marked suspect | the corporate action check itself failed | nothing at first: the check asks for the name's year again on each of the next 5 nights, and the run page's failed region names the name and the reason on every night it stays suspect.
Why: a night re-run by hand no longer asks for a name whose retries are spent, and nothing in the system performs the manual refetch the row named, so the row says what the check does and what the page shows.

### 2026-09-14 - ARCHITECTURE.html - section 17's refetch carve-out states the bound on a suspect name's retries

Authorised by: A suspect name's retries are bounded, and a name whose retries are spent stays suspect and named on the run page until another action lands on it
Was:
> it makes one request per name whose adjusted prices an action moved, which is bounded by the day's actions rather than by the universe, and on the fixture's captured day that is one name in five hundred.
Now:
> it makes one request per name whose adjusted prices an action moved, and a name whose refetch failed is asked for again on at most 5 nights after the one that marked it, a failure on a night an action lands on it starting the count again, so one action costs at most 6 requests however long its failure lasts. That is bounded by the actions of the day and of the 5 nights before it rather than by the universe
Why: from the phase 5 sign-off a suspect name was asked for again on every night with no bound, so the row's bound by the day's actions had stopped being true, and the ruling bounds the retries and says what a name whose retries are spent is left as.

### 2026-09-14 - ARCHITECTURE.html - section 18's corporate action row says what a name whose retries are spent is left as and where it is seen

Authorised by: A suspect name's retries are bounded, and a name whose retries are spent stays suspect and named on the run page until another action lands on it
Was:
> <tr><td>A split or dividend not caught</td><td>the corporate action check refetches the year; if the check itself fails the name is marked suspect, and every night after refetches it again, with or without an action of its own that day, until one refetch succeeds (see: Adjusted history is re-fetched after a corporate action)</td><td>a note that prices are being refetched</td>
Now:
> <tr><td>A split or dividend not caught</td><td>the corporate action check refetches the year; if the check itself fails the name is marked suspect, and on the nights after it refetches it again, with or without an action of its own that day, until one refetch succeeds or the name's retries are spent; a name whose retries are spent is not refetched again until another action lands on it, and stays suspect with the reason its last refetch failed for
Why: the retries are bounded, and the surface the row named was drawn nowhere by that name, so the row names the region that lists a suspect name on every night it stays so.

### 2026-09-14 - ARCHITECTURE.html - the corporate action checker's catalogue row carries the bound on its retries

Authorised by: A suspect name's retries are bounded, and a name whose retries are spent stays suspect and named on the run page until another action lands on it
Was:
> and a name whose own check failed is marked suspect rather than passing and is refetched again every night until one succeeds</td></tr>
Now:
> and a name whose own check failed is marked suspect rather than passing and is refetched again on the nights after until one succeeds or its retries are spent, and a name whose retries are spent stays suspect and named on the run page on every night until another action lands on it</td></tr>
Why: the catalogue states what the component does, and the ruling changes what it does with a name whose refetch keeps failing.

### 2026-09-14 - ARCHITECTURE.html - the series state store row carries the retry count

Authorised by: A suspect name's retries are bounded, and a name whose retries are spent stays suspect and named on the run page until another action lands on it
Was:
> <tr><td>series state</td><td>which names the corporate action check left trusted and which it marked suspect</td><td>section 7's corporate action checker</td></tr>
Now:
> <tr><td>series state</td><td>which names the corporate action check left trusted and which it marked suspect, with how many nights each suspect name has been asked for again</td><td>section 7's corporate action checker</td></tr>
Why: the store carries the count the check reads to bound a suspect name's retries, from migration 23.

### 2026-09-14 - RUNBOOK.md - a pass's rows name the check its theme runs under a stage of its own

Corrects: the list of a pass's rows, which named one `claims` stage for a pass however many checks ran in it, found when 6.11's production run stopped MSFT's pass on a second row under that stage.
Was:
> `theme research` where the pass refreshed its theme, whose detail names the sites it dropped and the addresses short of a document, `prose`,
Now:
> `theme research` where the pass refreshed its theme, whose detail names the sites it dropped and the addresses short of a document, and `theme claims` ahead of it where the theme wrote a cycle to check, `prose`,
Why: a theme refreshed inside a name's pass checks its cycle under the name's run, so its check is a row of its own and the list a reader looks for rows in has to carry it.

### 2026-09-14 - ARCHITECTURE.html - the cause row cites the decision that replaced the one it cited

Authorised by: A research pass reads a name's news inside each stored move and since the company's own filing, and hands each section the documents code picks from it, the company's own filing first
Was:
> at most two a move, naming the fewest companies and then the earliest (see: A research pass hands each section the documents code picks for it, the company's own filing first). A move with no document inside it
Now:
> at most two a move, naming the fewest companies and then the earliest (see: A research pass reads a name's news inside each stored move and since the company's own filing, and hands each section the documents code picks from it, the company's own filing first). A move with no document inside it
Why: the decision it cited is superseded at 6.11, and a citation of a superseded name is refused.

### 2026-09-14 - RUNBOOK.md - a pass reads the news inside each move and since the release, not the stored year

Authorised by: A research pass reads a name's news inside each stored move and since the company's own filing, and hands each section the documents code picks from it, the company's own filing first
Was:
> It fetches the name's news for the stored year and its latest results release from the filings archive, tests each document for admissibility as it arrives and stores it with the verdict. It hands each section the documents code picks for it: two a move for the cause of each move, and six since the release beside the release itself for the sections built across the evidence (see: A research pass hands each section the documents code picks for it, the company's own filing first).
Now:
> It fetches its latest results release from the filings archive, then the name's news inside each stored move and from the release's filing date to the night, overlapping spans once, tests each document for admissibility as it arrives and stores it with the verdict. It hands each section the documents code picks for it: two a move for the cause of each move, and six since the release beside the release itself for the sections built across the evidence (see: A research pass reads a name's news inside each stored move and since the company's own filing, and hands each section the documents code picks from it, the company's own filing first). A window the provider has more of than a query reads is named as unread on the pass's row, and the sections are written from the windows that were read.
Why: 6.11's production run found MSFT's year past the most pages a query reads, and the pass reads only the windows the rule hands a section from.

### 2026-09-14 - ARCHITECTURE.html - a theme search is restricted to one site of the industry list at a time, and its call is handed a bounded set of pages

Authorised by: A theme pass searches each site on the industry list alone, and hands the model a bounded set of the pages they return
Was:
> <tr><td>Theme search parameters</td><td>a theme search names the industry and not a ticker, carries an explicit date range, is restricted to the industry source list, and requests full page text rather than snippets (see: A theme search is scoped by parameter, not by hope)</td><td>an unscoped query returns the wrong industry, undated articles, and pages whose text cannot be stored; a snippet cannot serve as the document a claim rests on. Search is for theme material alone, because a ticker-tagged feed is exhaustive over a date range and cannot return the wrong company (see: Theme material comes from a search tool, and per-name material never does)</td>
Now:
> <tr><td>Theme search parameters</td><td>a theme search names the industry and not a ticker, carries an explicit date range, is restricted to one site of the industry source list, asks for that site's first 3 results, and requests full page text rather than snippets, and a theme pass makes one such search for every site on the list; its call is handed at most 10 of the pages it admitted, every site's first before any site's second, each carried as its first 30,000 characters (see: A theme search is scoped by parameter, not by hope) (see: A theme pass searches each site on the industry list alone, and hands the model a bounded set of the pages they return)</td><td>an unscoped query returns the wrong industry, undated articles, and pages whose text cannot be stored; a snippet cannot serve as the document a claim rests on. A search restricted to more than one site is one the tool answered at 6.11 with nothing, with one site's pages or with pages from outside the restriction, so the restriction is a site at a time, and a page of a million and a half characters is carried as its opening so ten pages stay a bounded call. Search is for theme material alone, because a ticker-tagged feed is exhaustive over a date range and cannot return the wrong company (see: Theme material comes from a search tool, and per-name material never does)</td>
Why: 6.11 measured the one search over the whole list returning nothing for five industries, and a restriction to several sites answered with nothing or with one site's pages, so a pass searches a site at a time, and a site answers with pages whose text no call should carry whole.

### 2026-09-14 - RUNBOOK.md - what a theme pass costs against the search tool's allowance

Authorised by: A theme pass searches each site on the industry list alone, and hands the model a bounded set of the pages they return
Was:
> | Tavily | open web search, for theme material only | free tier is a thousand credits a month against a few hundred searches a year |
Now:
> | Tavily | open web search, for theme material only | free tier is a thousand credits a month; a theme pass makes twelve searches, one a site of the industry list, at one credit each at the basic depth, so a few hundred passes a year come to a few thousand searches |
Why: a pass searches each site on the list alone from 6.11, so it spends twelve credits where it spent one, which is still inside the free allowance at the volume the tool was chosen for.

### 2026-09-14 - RUNBOOK.md - the industry cycle paragraph says a theme pass searches a site at a time and what that costs

Authorised by: A theme pass searches each site on the industry list alone, and hands the model a bounded set of the pages they return
Was:
> The theme pass searches the open web for the industry over the quarter to the day, restricted to the industry list and asking for each page's text; it drops a result from a site the list does not carry and a result whose text is missing or no longer than its snippet, names both on its row, tests every page it keeps for admissibility and stores it, and has the spend cap make one call, which states no figure. [...] A theme pass costs one search against the tool's monthly allowance and one paid call: over the fixture's Semiconductors pages the call cost $0.0016.
Now:
> The theme pass searches each site on the industry list for the industry, one search a site over the quarter to the day, asking each for its first three results and each page's text (see: A theme pass searches each site on the industry list alone, and hands the model a bounded set of the pages they return); it drops a result from a site the list does not carry and a result whose text is missing or no longer than its snippet, names both on its row, tests every page it keeps for admissibility and stores it whole, and has the spend cap make one call over at most ten of the pages it admitted, each carried as its first 30,000 characters, which states no figure. [...] A theme pass costs twelve searches against the tool's monthly allowance and one paid call, or two where the checker refuses the first draft: over the ten Semiconductors pages the fixture's searches kept, the two calls cost $0.0119. Where the list's sites carry nothing about an industry's prices the model writes nothing and the cycle is left out with that line: over the eleven pages the searches kept for Scientific & Technical Instruments, being job postings, labour and price releases, statistics pages and trade news, its one call came back empty.
Why: the paragraph described the one search over the list, which 6.11 measured returning nothing, and a pass that makes twelve searches and hands a bounded set of pages costs what the new sentences state.

### 2026-09-14 - BUILD_PLAN.md - the theme search obligation 6.11 produces the evidence for, discharged

Authorised by: A theme pass searches each site on the industry list alone, and hands the model a bounded set of the pages they return
Was:
> | **A theme search over the whole list measured against its sites searched alone** | 6.9 | 6.11 | 6.11 produces every section section 4 specifies on the current hardware, and the section a theme pass writes exists only where the theme's search returns a page. At 6.9 the one search a theme pass makes, restricted to the twelve sites of the industry list, returned nothing for Consumer Electronics, where statista.com searched alone a minute apart returned ten results, every one on the site with its text and a publish date. So a search over the list that returns nothing does not say that no site on it covers the industry, and a name in an industry a site does cover can be left without the section while its page says, truly of the search and not of the list, that the search found nothing. What 6.11 produces is reports on real names, and what that answers is whether a theme pass searches the list once, each site once, or the sites a measurement shows covering the industry, measured over the industries of the names it reports on with the searches each form costs a theme |
Now:
> the row reads discharged at 6.11, with what the measurement found and what it read before.
Why: 6.11 measured the one search over the list against a search a site over the fixture's four industries and ruled the form, which is the evidence the row was carried for.

### 2026-09-14 - BUILD_PLAN.md - 6.10's text says the queue writes the lane's sections that rest on no document

Authorised by: The overnight queue writes the local lane's sections that rest on no document, and the paid model is for names you get serious about
Was:
> Run the overnight queue on the local model, writing the sections in the local lane for listed names whose research is missing or stale, in order of reasons fired, until the configured time limit rather than until a count of names is reached (see: The overnight queue is bounded by time, not by a count of names). It holds the machine awake while it works and reports whether it ran (see: The overnight run holds the machine awake and reports whether it ran). It makes no paid call, and no part of the arithmetic depends on it.
Now:
> Run the overnight queue on the local model, writing the sections in the local lane that rest on no document for listed names whose research is missing or stale, in order of reasons fired, until the configured time limit rather than until a count of names is reached (see: The overnight queue is bounded by time, not by a count of names). It holds the machine awake while it works and reports whether it ran (see: The overnight run holds the machine awake and reports whether it ran). It makes no paid call and no request, and no part of the arithmetic depends on it (see: The overnight queue writes the local lane's sections that rest on no document, and the paid model is for names you get serious about).
Why: the checkpoint took the decision that the queue fetches nothing and so writes only what a facts file supports, and section 14's step 17 was restated to it in the queue's commit while the plan's own text for the checkpoint still said the queue writes the local lane.

### 2026-09-13 - RUNBOOK.md - the jobs table says what the overnight queue writes and what stops it

Authorised by: The overnight queue writes the local lane's sections that rest on no document, and the paid model is for names you get serious about
Was:
> | the overnight queue | after the arithmetic, same invocation | the local model writes the sections in the local lane for listed names whose research is missing or stale, in priority order, until the configured time limit | nothing |
Now:
> | the overnight queue | after the arithmetic, same invocation | the local model writes the local lane's sections that rest on no document, for listed names whose research is missing or stale, in priority order, starting no pass once the configured hours have passed | nothing, and no request |
Why: the queue fetches nothing, so it writes only what a facts file supports, and it starts no pass after its limit rather than stopping one inside a call.

### 2026-09-13 - RUNBOOK.md - the wakefulness paragraph says how the machine is held and which nights the page names

Authorised by: A night the overnight queue did not run is a traded session with no queue row, read on the run page against the exchange calendar
Was:
> **The overnight queue holds the machine awake while it works.** A laptop left to itself sleeps, and a nightly job that silently did not run is worse than no nightly job. The run page states the previous night's outcome including how many queued passes completed and how many were left.
Now:
> **The overnight queue holds the machine awake while it works.** A laptop left to itself sleeps, and a nightly job that silently did not run is worse than no nightly job. On Windows it takes a power request and on macOS a power assertion, released when the queue ends, and on any other machine it takes none; the queue's row on the run log says which. The run page states the night's outcome, including how many queued passes completed and how many were left, and names every traded session since the queue last ran on which it did not run (see: A night the overnight queue did not run is a traded session with no queue row, read on the run page against the exchange calendar).
Why: a hold the scheduler cannot see is one an operator needs to find on the machine, and a night the machine slept through runs no arithmetic either, so the page reads it off the calendar.

### 2026-09-13 - RUNBOOK.md - the morning table tells a queue that did not run from one that could not

Authorised by: The overnight queue writes the local lane's sections that rest on no document, and the paid model is for names you get serious about
Was:
> | The run page says the queue did not run | the machine slept | expected to be visible rather than silent. Listed names open without a draft, as normal |
Now:
> | The run page says the queue did not run | the machine slept, or the night stopped before step 17 | expected to be visible rather than silent. Listed names open without a draft, as normal, and the next night that runs drafts them. Where the page says the queue could not run, the local model was not answering: start the runtime and load the model the settings name |
Why: a night whose arithmetic stopped never reaches step 17 either, and a queue that ran into a runtime that was not answering has its own line and its own remedy.

### 2026-09-13 - ARCHITECTURE.html - the lane paragraph says a pass writes a section moved left, not the overnight queue

Authorised by: The local lane's scope is a setting, and whatever it holds is written for nothing
Was:
> sections move left and the overnight queue begins writing them for nothing. The picture is the same either way; only the boundary moves. (see: The local lane's scope is a setting, and the overnight queue writes whatever is in it)
Now:
> sections move left and a pass writes them for nothing. The picture is the same either way; only the boundary moves. (see: The local lane's scope is a setting, and whatever it holds is written for nothing)
Why: a synthesis section moved into the local lane rests on documents, which only a pass that fetches has, and the night fetches none, so the sentence saying the queue begins writing it no longer holds.

### 2026-09-13 - ARCHITECTURE.html - step 17 writes the lane's sections that rest on no document, bounded by a limit of its own

Authorised by: The overnight queue writes the local lane's sections that rest on no document, and the paid model is for names you get serious about
Was:
> <li>Run the overnight queue on the local model, writing the sections in the local lane for listed names whose research is missing or stale, in priority order, until the configured time limit rather than until a count of names is reached (see: The overnight queue is bounded by time, not by a count of names). It holds the machine awake while it works and reports whether it ran (see: The overnight run holds the machine awake and reports whether it ran). This makes no paid call, and no part of the arithmetic above depends on it (see: The overnight queue writes a free first draft; the paid model is for names you get serious about).</li>
Now:
> <li>Run the overnight queue on the local model, writing the sections in the local lane that rest on no document for listed names whose research is missing or stale, in priority order, until the configured time limit rather than until a count of names is reached (see: The overnight queue is bounded by time, not by a count of names), a limit of its own rather than the night's deadline (see: The overnight queue is bounded by its own limit rather than the night's deadline, and starts no pass once the limit has passed). It holds the machine awake while it works and reports whether it ran (see: The overnight run holds the machine awake and reports whether it ran). This makes no paid call and no request, and no part of the arithmetic above depends on it (see: The overnight queue writes the local lane's sections that rest on no document, and the paid model is for names you get serious about).</li>
Why: the step fetches nothing, so the sections it can write are the ones a facts file alone supports, and the night's deadline is sized for the arithmetic, so the step names the limit that bounds it instead.

### 2026-09-13 - ARCHITECTURE.html - the wall clock row is the arithmetic's

Authorised by: The overnight queue is bounded by its own limit rather than the night's deadline, and starts no pass once the limit has passed
Was:
> <tr><td>Nightly wall clock, at index size</td><td>a night bounded by 5 minutes,
Now:
> <tr><td>Nightly wall clock, at index size</td><td>a night's arithmetic, steps 1 to 16, bounded by 5 minutes,
Why: step 17 runs for up to its own limit after the arithmetic has closed, so a wall clock over the whole night would be read against an hour of queue it was never sized for.

### 2026-09-13 - ARCHITECTURE.html - the night's deadline bounds the arithmetic, and the queue its own limit

Authorised by: The overnight queue is bounded by its own limit rather than the night's deadline, and starts no pass once the limit has passed
Was:
> each attempt bounded by 30 seconds; the night as a whole bounded by 15 minutes (see: A feed is tried three times with a doubling backoff, and the night has a deadline it cannot move)</td>
Now:
> each attempt bounded by 30 seconds; the arithmetic as a whole, steps 1 to 16, bounded by 15 minutes, and the overnight queue at step 17 bounded by its own limit instead (see: A feed is tried three times with a doubling backoff, and the night has a deadline it cannot move) (see: The overnight queue is bounded by its own limit rather than the night's deadline, and starts no pass once the limit has passed)</td>
Why: a queue held to the deadline would get what the arithmetic left of fifteen minutes and an hour's limit would be cut short with nothing saying so.

### 2026-09-13 - ARCHITECTURE.html - the overnight queue's limit set from the pass 6.10 measured

Authorised by: The overnight queue is bounded by time, not by a count of names
Was:
> <tr><td>Overnight queue</td><td>the local model writes the sections in the local lane, for listed names whose research is missing or stale, in order of reasons fired, stopping after a configured number of hours, proposed at 1 until 6.10 measures a pass on this machine; no paid call is ever made by the queue (see: A research pass is split by section difficulty, not run wholesale on one model)</td><td>a name count cannot bound the time because pass durations vary widely, and the whole point of the queue is that it costs nothing, so a paid fallback inside it would defeat it. The number of hours is a budget rather than a bound on behaviour, so what settles it is the per-pass duration 6.10 measures on this machine, stated as the hours that cover a named number of names at the measured rate with the rate and its population beside it. It is 1 until then, so the queue is bounded from the first night it runs rather than after the figure arrives</td>
Now:
> <tr><td>Overnight queue</td><td>the local model writes the sections in the local lane that rest on no document, for listed names whose research is missing or stale, in order of reasons fired, starting no pass once a configured number of hours has passed, which is 1: at the slowest pass 6.10 measured on this machine an hour covers every member of the index, the 503 the first live night loaded coming to 43 minutes at 5.13 seconds a pass; no paid call and no request is ever made by the queue (see: A research pass is split by section difficulty, not run wholesale on one model) (see: The overnight queue is bounded by its own limit rather than the night's deadline, and starts no pass once the limit has passed)</td><td>a name count cannot bound the time because pass durations vary widely, and the whole point of the queue is that it costs nothing, so a paid fallback inside it would defeat it. The number of hours is a budget rather than a bound on behaviour, so what settles it is the per-pass duration measured on this machine, stated as the hours that cover a named number of names at the measured rate with the rate and its population beside it. 6.10 measured 12 passes on the local model the shipped configuration names, over the fixture's four listed names on three nights: 3.35 seconds a pass on average and 5.13 at the slowest, the slowest being a name whose first draft the checker refused and whose second was written. A pass runs past the limit it started inside, by at most one pass</td>
Why: the row carried a proposed figure until this checkpoint measured a pass, and says in its own words that the figure is set from that measurement with the rate and its population beside it.

### 2026-09-13 - ARCHITECTURE.html - the slept row cites how a night the queue did not run is read

Authorised by: A night the overnight queue did not run is a traded session with no queue row, read on the run page against the exchange calendar
Was:
> a queue that silently fails looks identical to a quiet night, so the absence has to be stated rather than inferred from an empty result (see: The overnight run holds the machine awake and reports whether it ran)</td>
Now:
> a queue that silently fails looks identical to a quiet night, so the absence has to be stated rather than inferred from an empty result (see: The overnight run holds the machine awake and reports whether it ran) (see: A night the overnight queue did not run is a traded session with no queue row, read on the run page against the exchange calendar)</td>
Why: the row says the page states the night, and the decision says which nights those are, a traded session with no queue row rather than a night whose arithmetic ran.

### 2026-09-13 - ARCHITECTURE.html - the local model unavailable row cites the decision that replaced the one it cited

Authorised by: The overnight queue writes the local lane's sections that rest on no document, and the paid model is for names you get serious about
Was:
> the answer for each is the other lane (see: The overnight queue writes a free first draft; the paid model is for names you get serious about)</td>
Now:
> the answer for each is the other lane (see: The overnight queue writes the local lane's sections that rest on no document, and the paid model is for names you get serious about)</td>
Why: the decision it cited is superseded at 6.10, and a citation of a superseded name is refused.

### 2026-09-13 - CLAUDE.md - nightly-cost's row states the carve's second half

Authorised by: The night's zero-model-call rule bounds the arithmetic, and the overnight queue is carved out of it by name
Was:
> | `nightly-cost` | every CI run | The nightly path makes zero model calls and zero per-name network requests, asserted over the shipped source and over a recorded run, with the run measured over two universe sizes so the count is shown not to grow with the population |
Now:
> | `nightly-cost` | every CI run | The nightly path makes zero per-name network requests and its arithmetic zero model calls, asserted over the shipped source and over a recorded run, with the run measured over two universe sizes so the count is shown not to grow with the population. The night reaches no lane an open reaches, read off what the components its own file constructs declare, and every model call a whole recorded night makes sits on step 17's own row or on a pass that row names, with nothing spent anywhere, because the queue is carved out of the model-call rule by name and out of nothing else |
Why: the check now asserts which lane the night may call and that its model calls come from step 17 alone, which is the half of the carve 6.10 lands before the queue, and a roster row stating less than its check asserts is the row a later session narrows the check back to. It also asserted zero model calls against a figure its own helper wrote as a literal zero, which reading the calls off the run log replaces.

### 2026-09-13 - BUILD_PLAN.md - the industry list measurement read as a measure of the search, and one obligation created against 6.11

Corrects: the discharged row said the fixture's four industries drawing nothing over the whole list is what a theme pass finds until the list covers its industries, and one of the four, Consumer Electronics, drew ten storable pages from statista.com searched alone a minute apart. Found at 6.9 while its record was written, by reading the single-site searches against the whole-list ones.
Was:
> | **The industry source list measured against a search that ran** | 6.0 | 6.9, discharged | measured, and recorded in PROGRESS.md and in the list's own review note. One search a site over the quarter to 2026-09-08, each for an industry the site covers, returned 68 results and 50 storable ones, being on the site with text longer than their snippet and a publish date: semiconductors.org 10, trendforce.com 10, statista.com 10, iea.org 9, worldsteel.org 5, digitimes.com 3, spglobal.com 2, eia.gov 1, and none from bls.gov, census.gov, federalreserve.gov or ihsmarkit.com. Two of the three gated markings were guesses the search contradicts, statista.com returning ten storable pages of ten and digitimes.com three, and the list now marks ihsmarkit.com alone. The same theme search for the fixture's four industries over the whole list returned nothing for any of them, which is what a theme pass for most of the index finds until the list covers its industries. What it read before: 6.9 builds the search and is the first point at which anything can measure this list. 1.7 named it as the weaker of the two, because the licensed feed returned no article from any site on it, and the 6.0 review confirmed that with no more evidence than 1.7 had: a feed measurement cannot review a search list. What 6.9 produces is a search against the list over a theme, and what that answers is which of the twelve sites return a page whose full text can be stored, which is the question the gated markings on the list are a guess at |
Now: the row's last measured sentence says the empty searches measure the one search a pass makes rather than the list, and names the row that carries it. A second row is created, due at 6.11, for a theme search over the whole list measured against its sites searched alone, and 6.11's own text cites it back.
Why: a statement that the list does not cover an industry, resting on a search that returned nothing for an industry a site on the list does cover, would send a later session to widen the list when what returned less than the list holds is the search. It is chased from 6.11 because that is the checkpoint producing every section on real names.

### 2026-09-13 - ARCHITECTURE.html - the name screen reads the industry cycle from the theme store

Authorised by: A theme is the industry the index names for a member, and one theme pass serves every member it names
Was:
> <p><b>Reads:</b> the facts, ladder, levels, volume profile, moves and fundamentals for that name and date, every stored research section with its own date and model, the source documents those sections cite, which the dates-and-sources region draws and this list did not name until 6.0, the calendar that region draws beside them, and the run log, for what research has spent and cost and what the newest pass for the name came to, which the page has read since 6.6 and this list did not name until 6.8.</p>
Now:
> <p><b>Reads:</b> the facts, ladder, levels, volume profile, moves and fundamentals for that name and date, every stored research section with its own date and model, the industry cycle the theme store holds for the industry the membership row names for the name, which is the theme's section rather than one of the name's and is read that way from 6.9, the source documents those sections cite, which the dates-and-sources region draws and this list did not name until 6.0, the calendar that region draws beside them, and the run log, for what research has spent and cost and what the newest pass for the name came to, which the page has read since 6.6 and this list did not name until 6.8.</p>
Why: a name's industry cycle is its theme's section, read for the industry its membership row names, and a Reads line naming only the name's own research sections would not say where the page draws it from.

### 2026-09-13 - BUILD_PLAN.md - the three obligations 6.9 produces the evidence for, discharged

Corrects: nothing was wrong. Three carried obligations reached the checkpoint that produces their evidence, and each row records what was measured and ruled, in the form 6.8's discharge took.
Was:
> | **A theme section's figures checked against a facts file a theme has** | 6.4 | 6.9 | 6.9 builds the theme runner and writes the first theme section. A facts file is one per name and a theme is not a name, so 6.4's checker holds a theme section against no facts file and refuses every figure in it, while the industry cycle is a section about where an industry's own prices are, which is figures. What 6.9 produces is the theme record and its stored documents, and what it settles is where a figure about an industry's prices is computed so that code owns it before prose quotes it |
>
> | **The denied-category markers tested against what the search tool returns** | 6.3 | 6.9 | 6.9 builds the search tool and is the first point at which a document arrives through the path the product will use. 6.3's markers were read off eight pages asked for by hand, which is real evidence and not the same evidence: a page fetched by hand arrives as a reading tool's rendering rather than as the bytes a search tool returns, so what the fixture holds is seven constructed documents carrying markers measured elsewhere. What 6.9 produces is a set of results from a theme search and a company search, and what that answers is whether the markers fire on what the tool actually delivers, in both directions: a refusable page admitted, and an ordinary article refused. The four captured articles already hold the admitting half against the licensed feed |
>
> | **The industry source list measured against a search that ran** | 6.0 | 6.9 | 6.9 builds the search and is the first point at which anything can measure this list. 1.7 named it as the weaker of the two, because the licensed feed returned no article from any site on it, and the 6.0 review confirmed that with no more evidence than 1.7 had: a feed measurement cannot review a search list. What 6.9 produces is a search against the list over a theme, and what that answers is which of the twelve sites return a page whose full text can be stored, which is the question the gated markings on the list are a guess at |
Now:
> | **A theme section's figures checked against a facts file a theme has** | 6.4 | 6.9, discharged | settled as a rule rather than as a file (see: A theme section states no figure, because nothing the store holds is computed for an industry). A theme's facts file is empty by rule: nothing the store holds is computed about an industry's own prices, so the checker holds a theme section against none and refuses every figure in one, the section is asked for with no figure and no full date, and every sentence cites a document in the theme record. The evidence was the first theme section a model wrote: over the nine Semiconductors pages the fixture's search kept, the paid model wrote the cycle in words citing two of them, and the checker accepted it on its first draft. A facts file of medians over the members' own files was the branch not taken, because it is a figure about the members' shares rather than about the industry's prices. What it read before: 6.9 builds the theme runner and writes the first theme section. A facts file is one per name and a theme is not a name, so 6.4's checker holds a theme section against no facts file and refuses every figure in it, while the industry cycle is a section about where an industry's own prices are, which is figures. What 6.9 produces is the theme record and its stored documents, and what it settles is where a figure about an industry's prices is computed so that code owns it before prose quotes it |
>
> | **The denied-category markers tested against what the search tool returns** | 6.3 | 6.9, discharged | measured over what the tool returned, and recorded in the search admissibility expectation, which a test reruns from the captures. Over the twenty results of a company search for Keysight Technologies and a theme search for Semiconductors, seventeen carrying text: one refusable page refused, a quote page with no running prose; two refusable pages admitted, a quote page whose one paragraph of prose is the provider's description of the company and a move write-up bylined to a site's price tracker, both from sites no list carries, so on the path a theme pass takes the list drops each before the test reads it; and none of the other fourteen refused, a research vendor's report page and a listing of an association's posts among them. No marker was changed: the quote page rule's second half is 6.3's choice not to refuse an article filed under a quote address, and the write-up is the limit 6.3 stated for machine writing nobody declares. What it read before: 6.9 builds the search tool and is the first point at which a document arrives through the path the product will use. 6.3's markers were read off eight pages asked for by hand, which is real evidence and not the same evidence: a page fetched by hand arrives as a reading tool's rendering rather than as the bytes a search tool returns, so what the fixture holds is seven constructed documents carrying markers measured elsewhere. What 6.9 produces is a set of results from a theme search and a company search, and what that answers is whether the markers fire on what the tool actually delivers, in both directions: a refusable page admitted, and an ordinary article refused. The four captured articles already hold the admitting half against the licensed feed |
>
> | **The industry source list measured against a search that ran** | 6.0 | 6.9, discharged | measured, and recorded in PROGRESS.md and in the list's own review note. One search a site over the quarter to 2026-09-08, each for an industry the site covers, returned 68 results and 50 storable ones, being on the site with text longer than their snippet and a publish date: semiconductors.org 10, trendforce.com 10, statista.com 10, iea.org 9, worldsteel.org 5, digitimes.com 3, spglobal.com 2, eia.gov 1, and none from bls.gov, census.gov, federalreserve.gov or ihsmarkit.com. Two of the three gated markings were guesses the search contradicts, statista.com returning ten storable pages of ten and digitimes.com three, and the list now marks ihsmarkit.com alone. The same theme search for the fixture's four industries over the whole list returned nothing for any of them, which is what a theme pass for most of the index finds until the list covers its industries. What it read before: 6.9 builds the search and is the first point at which anything can measure this list. 1.7 named it as the weaker of the two, because the licensed feed returned no article from any site on it, and the 6.0 review confirmed that with no more evidence than 1.7 had: a feed measurement cannot review a search list. What 6.9 produces is a search against the list over a theme, and what that answers is which of the twelve sites return a page whose full text can be stored, which is the question the gated markings on the list are a guess at |
Why: A row whose checkpoint has landed is discharged in its own cell with what it read before, so the table carries no open row due at a landed checkpoint.

### 2026-09-13 - ARCHITECTURE.html - the lane table's industry cycle states no figure

Authorised by: A theme section states no figure, because nothing the store holds is computed for an industry
Was:
> <td>every sentence cites a document in the theme record; every figure is one code computed for the industry</td>
Now:
> <td>every sentence cites a document in the theme record; it states no figure, because nothing the store holds is computed for an industry (see: A theme section states no figure, because nothing the store holds is computed for an industry)</td>
Why: No code computes a figure about an industry's own prices, so the only figure a theme section could carry is one no rule can check, and the cell says what the rule does rather than a figure nothing produces.

### 2026-09-13 - ARCHITECTURE.html - an off-list search result is discarded before its text is read

Corrects: the row said the text of an off-list result is never fetched, and the search tool returns each result's text in the answer to the search, so no second request exists to be skipped. Found at 6.9 by the capture taken before the feed was written.
Was:
> <td>the result is discarded before its text is fetched, and the run log names the domain</td>
Now:
> <td>the result is discarded before its text is read, the tool having returned the text in the same answer, and the run log names the domain</td>
Why: The gate still runs before anything reads or tests the text, which is what the row is for, and it now says the mechanism the tool allows.

### 2026-09-13 - ARCHITECTURE.html - the research runner reads membership for the name's industry

Authorised by: A theme is the industry the index names for a member, and one theme pass serves every member it names
Was:
> <td>facts, fundamentals, filings archive, news feed, research store, theme store, source documents, run log</td> ... and starts no second plain pass where one did (see: A name opened again on the day its research pass ran starts no second pass unless the page asks for one)</td>; and the matrix row's Membership cell blank
Now:
> <td>membership, facts, fundamentals, filings archive, news feed, research store, theme store, source documents, run log</td> ... with a sentence saying it reads the industry the index names for the name and has the theme research runner refresh that theme first where the cycle is missing or stale; and the matrix row's Membership cell R
Why: The name's industry is what its theme is, and the runner reads it off the membership row the night writes.

### 2026-09-13 - ARCHITECTURE.html - the theme research runner reads the run log and says what it drops and when it does not start

Authorised by: A theme refresh runs off-peak, and a name opened at peak is written without one
Was:
> <td>theme store, source documents, search tool</td><td>theme store, source documents</td><td>researches an industry's own cycle once, so every name in that industry shares one paid pass, testing every document it fetched for admissibility before storing it as the per-name runner does, and has the spend cap make its every paid call</td>; and the matrix row's run log cell W
Now:
> <td>theme store, source documents, search tool, run log</td><td>theme store, source documents</td> with sentences saying a theme is the index's industry, that off-list and short results are dropped and named, that it does not start at peak, and that it reads the run log for a pass already run that day; and the matrix row's run log cell R W
Why: A theme pass is researched once a day at most, which it reads off its own rows, and the row names what it drops before anything is stored.

### 2026-09-13 - SCHEMA.md - membership carries the industry beside the sector

Authorised by: A theme is the industry the index names for a member, and one theme pass serves every member it names
Was:
> | `sector` | TEXT | the sector the provider last named for this ticker, null where it has named none. Last because it was added by an `ALTER TABLE` at 5.1 and SQLite appends, and this file states the order the store has rather than the order that reads best |
Now:
> | `sector` | TEXT | ... After `observed_at` because it was added by an `ALTER TABLE` at 5.1 ... |
> > | `industry` | TEXT | the industry the provider last named for this ticker, null where it has named none. Last because it was added by an `ALTER TABLE` at 6.9 |
Why: Migration 22 adds the industry, and the sector is no longer the last column.

### 2026-09-13 - RUNBOOK.md - what a pass does about the industry cycle, and the search tool's key

Authorised by: A theme is the industry the index names for a member, and one theme pass serves every member it names
Was:
> The industry cycle waits for the theme record, which the theme research runner writes.
> >
> > **Where to look.** Every stage of a pass is a row on the run log under one run, `research-<instant>-<TICKER>`: `fundamentals`, `staleness`, `prose`, one `research call:` row per paid call, `claims`, the second and third rounds' rows named for their round, and `research` last, whose detail says what was written, what was not and why, and what the documents came to.
Now:
> the sentence removed, a paragraph on the theme's cycle added before **Where to look.**, which now names the `theme research` row; and the secrets table gains `| Tavily, the search tool | EquityBrief:Providers:Tavily:ApiKey | EquityBrief.Worker |`
Why: 6.9 writes the theme record, so the sentence saying the cycle waits for it is no longer true, and a secrets file written by hand needs the search tool's key path.

### 2026-09-13 - ARCHITECTURE.html - the name screen's reads name the calendar and the run log

Corrects: the name screen has read the run log since 6.6, for the newest prose pass's sections not written, and since 6.7 for what research spent, and from 6.8 it reads the calendar for its dates and the newest research pass, and its Reads line named none of the three. Found at 6.8, where the page's research regions are drawn.
Was:
> <p><b>Reads:</b> the facts, ladder, levels, volume profile, moves and fundamentals for that name and date, every stored research section with its own date and model, and the source documents those sections cite, which the dates-and-sources region draws and this list did not name until 6.0.</p>
Now:
> <p><b>Reads:</b> the facts, ladder, levels, volume profile, moves and fundamentals for that name and date, every stored research section with its own date and model, the source documents those sections cite, which the dates-and-sources region draws and this list did not name until 6.0, the calendar that region draws beside them, and the run log, for what research has spent and cost and what the newest pass for the name came to, which the page has read since 6.6 and this list did not name until 6.8.</p>
Why: a screen's Reads line is where a reader looks for what the page depends on, and a page whose research line comes from the run log looks, from that line, as if it could not have drawn it.

### 2026-09-13 - ARCHITECTURE.html - the research runner's catalogue and matrix rows read the run log

Authorised by: A name opened again on the day its research pass ran starts no second pass unless the page asks for one
Was:
> <tr><td><b>Research runner</b></td><td><span class="layer L-research">research</span></td><td>on demand, per name</td><td>facts, fundamentals, filings archive, news feed, research store, theme store, source documents</td><td>research store, source documents</td><td>writes the narrative sections pending the checker's verdict, tests every document it fetched for admissibility before storing it, keeps what it stored with the verdict that admitted or refused it, and has the spend cap make its every paid call</td></tr>
>
> <tr><td>Research runner</td><td></td><td></td><td></td><td></td><td></td><td></td><td><span class="r">R</span></td><td><span class="r">R</span></td><td></td><td><span class="r">R</span> <span class="w">W</span></td><td><span class="r">R</span> <span class="w">W</span></td><td></td><td><span class="w">W</span></td></tr>
Now:
> <tr><td><b>Research runner</b></td><td><span class="layer L-research">research</span></td><td>on demand, per name</td><td>facts, fundamentals, filings archive, news feed, research store, theme store, source documents, run log</td><td>research store, source documents</td><td>writes the narrative sections pending the checker's verdict, tests every document it fetched for admissibility before storing it, keeps what it stored with the verdict that admitted or refused it, and has the spend cap make its every paid call. It reads the run log for whether a pass for the name already ran that day, and starts no second plain pass where one did (see: A name opened again on the day its research pass ran starts no second pass unless the page asks for one)</td></tr>
>
> <tr><td>Research runner</td><td></td><td></td><td></td><td></td><td></td><td></td><td><span class="r">R</span></td><td><span class="r">R</span></td><td></td><td><span class="r">R</span> <span class="w">W</span></td><td><span class="r">R</span> <span class="w">W</span></td><td></td><td><span class="r">R</span> <span class="w">W</span></td></tr>
Why: the runner reads the run log for whether a pass for the name ran to the end that day, and component-access reconciles the class's declaration against both rows cell by cell, so the catalogue's Reads cell and the matrix's run log cell say what the class declares.

### 2026-09-13 - ARCHITECTURE.html - the read API's catalogue row names the one thing it starts

Authorised by: The name page's control starts the worker's research verb, and the read API writes nothing it starts
Was:
> <td>read-only access for the app; performs no computation and no fetching</td></tr>
Now:
> <td>read-only access for the app; performs no computation and no fetching. The name page's control is the one thing it starts, the worker's research verb for that name as a process of its own, and it writes nothing that pass writes (see: The name page's control starts the worker's research verb, and the read API writes nothing it starts)</td></tr>
Why: the row said the read API gives read-only access and fetches nothing, which stays true of every store, and from 6.8 it also starts the worker's verb when the name page's control is pressed, which a reader of the row would otherwise find only in the route.

### 2026-09-13 - RUNBOOK.md - a second press on the same day starts nothing, and what a pass costs is stated beside the control

Authorised by: A name opened again on the day its research pass ran starts no second pass unless the page asks for one
Was:
> **What a pass costs.** Over the fixture's KEYS a pass wrote all eight sections it could write, three on the local model and five through the spend cap for $0.0201, in about two and a half minutes. A second press on the same day writes nothing, because every section it would write was written or left out today, and a press while a pass for the name is running is refused by name. The industry cycle waits for the theme record, which the theme research runner writes.
Now:
> **What a pass costs.** Over the fixture's KEYS a pass wrote all eight sections it could write, three on the local model and five through the spend cap for $0.0201. The name page states what the passes before it cost beside its control, because a pass's price is known only once it has been made. A second press on the same day starts nothing, because a pass for the name already ran that day, and a press while a pass for the name is running is refused by name; the page's rewrite and its option to have the paid model write the local lane's sections still start one (see: A name opened again on the day its research pass ran starts no second pass unless the page asks for one). The industry cycle waits for the theme record, which the theme research runner writes.
Why: the paragraph gave the per-section rule as the reason a second press writes nothing, and that rule leaves a section a pass had nothing to write from without a row, so a second press would fetch again; the reason is now the pass that ran that day. The two and a half minutes went with it: a duration measured once on this machine is a record's figure rather than a spec's, and it is in PROGRESS.md's 6.8 entry, where the $0.0201 beside it is pinned to the research record.

### 2026-09-13 - RUNBOOK.md - what the name page's control does

Authorised by: A pass is started only by a request carrying the name page's own header
Was:
> **Where to look.** Every stage of a pass is a row on the run log under one run, `research-<instant>-<TICKER>`:
Now:
> **What the control does.** It sends the press with a header of the page's own, and the read surface refuses a request without one, so another site's page open in a browser on this machine cannot start a pass (see: A pass is started only by a request carrying the name page's own header). The surface refuses a name the index does not hold, then starts the command above from the checkout it runs in, telling the worker the data root it reads so the pass writes the store the page shows, and returns at once. It writes nothing itself: the page shows what the pass wrote when it is opened again.
>
> **Where to look.** Every stage of a pass is a row on the run log under one run, `research-<instant>-<TICKER>`:
Why: the section said the control starts the command and nothing about what a press is refused for or which store the pass writes, which is what an operator reading the run log after a press that started nothing needs.

### 2026-09-13 - BUILD_PLAN.md - the three obligations 6.8 produces the evidence for, discharged

Corrects: nothing was wrong. Three carried obligations reached the checkpoint that produces their evidence, and each row records what was measured and ruled, in the form 6.6's discharge took.
Was:
> | **A dated calendar item's date held to something that can carry it** | 6.6 | 6.8 | 6.8 writes the first dated calendar items, the section of events a model reads out of stored filings and news that fall after the latest session. 6.4's checker holds every date in prose to one the facts file holds, and the file carries the provider's next dated event and nothing a document dates, so as built every date this section exists to state is refused and the section falls back on every name. Found at 6.6 while section 12.2's lane table was rewritten to say what each section must pass. What 6.8 produces is the first set of dated items a model wrote from stored documents, which is what shows whether such a date belongs in the facts file, read out of the document by code, or in a rule that holds a date to the document the sentence cites |
>
> | **A pass the spend cap refuses short of a reached cap stated on the name page** | 6.7 | 6.8 | 6.8 is the first checkpoint that makes a paid pass, and it makes it through the spend cap. The cap refuses a call where what has been spent and the most that call could cost would pass a cap, which for the request recorded at 6.7 starts $0.0101079 before the cap, and the name page states a pause from the moment a cap is reached, judged with no call in hand, because at 6.7 there is no pass for it to judge. So between a cap less one call's ceiling and the cap itself a call is refused, the run page carries the cap's row for it, and the name page draws nothing. Found at 6.7, by the mutation that drew a pause a cent before the cap, which survived until a case half a cent short of it was written. What 6.8 produces is a pass opened from the name page and refused by the cap, which is what shows whether the page states that pass's own refusal or a verdict it works out for itself |
>
> | **The research lane boundary measured against the fixture both ways** | authored with the architecture | 6.8 | 6.8 is where a pass over the fixture reproduces byte for byte from the recorded endpoint, which is what lets each section be run both ways and compared. The decision named phase 6 and this names the checkpoint inside it that produces the recording, which sharpens the point rather than moving it. It read 6.5 until 6.0, and 6.0's resplit moved the research runner from the fifth position to the eighth, so the point moved with the work rather than the work with the point |
Now:
> | **A dated calendar item's date held to something that can carry it** | 6.6 | 6.8, discharged | held to a rule rather than to the facts file (see: A dated calendar item's date rests on a document the sentence cites and falls after the night the facts were computed for). A date in this section is one a document the sentence cites states, read out of that document by the claim checker's own figure reader, and a full date falls after the night the facts file was computed for; every other section still holds a date to the facts file. The evidence was the first dated items a model wrote from stored documents: over the fixture's KEYS the paid model's first draft named two investor conferences, each dated by a document it cited and each after the night, and was accepted, while the local model's drafts in the lane comparison were left out. The facts file was the branch not taken, because a date read into the file by code would be a second reading of the document the sentence already rests on, and holding the date to that document is the same test with nothing to keep in step. Asserted over constructed sentences: a date the cited document states and after the night passes, the same date cited to a document that does not state it is refused, a stated date on or before the night is refused, and a date nobody stated is refused. What it read before: 6.8 writes the first dated calendar items, the section of events a model reads out of stored filings and news that fall after the latest session. 6.4's checker holds every date in prose to one the facts file holds, and the file carries the provider's next dated event and nothing a document dates, so as built every date this section exists to state is refused and the section falls back on every name. Found at 6.6 while section 12.2's lane table was rewritten to say what each section must pass. What 6.8 produces is the first set of dated items a model wrote from stored documents, which is what shows whether such a date belongs in the facts file, read out of the document by code, or in a rule that holds a date to the document the sentence cites |
>
> | **A pass the spend cap refuses short of a reached cap stated on the name page** | 6.7 | 6.8, discharged | the page states that pass's own refusal, and works out no verdict of its own for it. A pass stopped by the cap writes the cap's line for each section it did not reach on its own run log row, and the name page reads the newest pass: it draws one line saying the pass was stopped by the spend cap before a cap was reached, ending in the line the cap refused the first call with, and each section it did not reach with the same line, while its own verdict, judged with no call in hand, still draws no pause and offers the controls. Asserted over a pass at a day cap of one cent, whose first call the cap refused inside that call's ceiling: the page's line ends in the words of that call's own row, every paid section carries them, and no pause line is drawn. What it read before: 6.8 is the first checkpoint that makes a paid pass, and it makes it through the spend cap. The cap refuses a call where what has been spent and the most that call could cost would pass a cap, which for the request recorded at 6.7 starts $0.0101079 before the cap, and the name page states a pause from the moment a cap is reached, judged with no call in hand, because at 6.7 there is no pass for it to judge. So between a cap less one call's ceiling and the cap itself a call is refused, the run page carries the cap's row for it, and the name page draws nothing. Found at 6.7, by the mutation that drew a pause a cent before the cap, which survived until a case half a cent short of it was written. What 6.8 produces is a pass opened from the name page and refused by the cap, which is what shows whether the page states that pass's own refusal or a verdict it works out for itself |
>
> | **The research lane boundary measured against the fixture both ways** | authored with the architecture | 6.8, discharged | measured, and recorded in the research record's expectation, where a test reruns both passes from the recordings. Every section was run on both models over one evidence set, being KEYS's thirteen articles and its own release with the same documents handed each section either way: with every section on the paid model all eight it could write were accepted, over 11 calls for $0.025778823, and with every section on the local model three were, what the company sells, the segment commentary and the key under each figure, while the cause of each large move, the dated calendar items, the two cases, the risks and the short version were left out. The boundary moved on that evidence, and this machine's local lane is the three the local model wrote accepted (see: The fixture comparison moved the cause of each large move into the paid lane on this machine), a pass in the default lanes writing all eight for $0.020124825. What it read before: 6.8 is where a pass over the fixture reproduces byte for byte from the recorded endpoint, which is what lets each section be run both ways and compared. The decision named phase 6 and this names the checkpoint inside it that produces the recording, which sharpens the point rather than moving it. It read 6.5 until 6.0, and 6.0's resplit moved the research runner from the fifth position to the eighth, so the point moved with the work rather than the work with the point |
Why: an obligation is closed in its own row, where the next reader of the table meets it, with the text it carried kept inside the row rather than lost.

### 2026-09-13 - ARCHITECTURE.html - the research runner's Reads cell names the research store

Corrects: the research runner's catalogue row did not list the research store among what it reads, while its matrix row already gave it R W on research and theme. The runner reads each section's newest version to decide what a pass writes and which version comes next, and component-access refused the declaration that says so against the Reads cell. Found at 6.8, where the runner is built.
Was:
> <tr><td><b>Research runner</b></td><td><span class="layer L-research">research</span></td><td>on demand, per name</td><td>facts, fundamentals, filings archive, news feed, theme store, source documents</td>
Now:
> <tr><td><b>Research runner</b></td><td><span class="layer L-research">research</span></td><td>on demand, per name</td><td>facts, fundamentals, filings archive, news feed, research store, theme store, source documents</td>
Why: the catalogue row and the matrix row now agree with each other and with the class, which is what component-access reconciles cell by cell.

### 2026-09-13 - ARCHITECTURE.html - the facts assembler and the change detector run again for their night when an open fetches a name's fundamentals

Authorised by: A name's facts file is assembled again for its night when an open fetches its fundamentals
Was:
> <tr><td><b>Facts assembler</b></td><td><span class="layer L-compute">compute</span></td><td>nightly, per name</td>
>
> <tr><td><b>Change detector</b></td><td><span class="layer L-compute">compute</span></td><td>nightly, per name</td>
Now:
> <tr><td><b>Facts assembler</b></td><td><span class="layer L-compute">compute</span></td><td>nightly, per name, and for its night again when an open fetches a name's fundamentals</td>
>
> <tr><td><b>Change detector</b></td><td><span class="layer L-compute">compute</span></td><td>nightly, per name, and for its night again when an open fetches a name's fundamentals</td>
Why: the research verb runs both again after a fetch that stored a filing the night had not seen, so their Runs cells say so rather than stating a nightly schedule the verb departs from.

### 2026-09-13 - ARCHITECTURE.html - figure 12.2, its key, the lane table's cause row and why each section sits in its lane

Authorised by: The fixture comparison moved the cause of each large move into the paid lane on this machine
Was:
> <text x="192" y="236" text-anchor="middle" dominant-baseline="central" fill="var(--ink)" font-family="Segoe UI, Arial, sans-serif" font-size="14" font-weight="600">Classify and extract</text>
> <text x="192" y="255" text-anchor="middle" dominant-baseline="central" fill="var(--muted)" font-family="Segoe UI, Arial, sans-serif" font-size="12.5">what caused each big move;</text>
> <text x="192" y="272" text-anchor="middle" dominant-baseline="central" fill="var(--muted)" font-family="Segoe UI, Arial, sans-serif" font-size="12.5">the business explainer and segments</text>
>
> <text x="512" y="255" text-anchor="middle" dominant-baseline="central" fill="var(--muted)" font-family="Segoe UI, Arial, sans-serif" font-size="12.5">the two cases, the risks,</text>
> <text x="512" y="272" text-anchor="middle" dominant-baseline="central" fill="var(--muted)" font-family="Segoe UI, Arial, sans-serif" font-size="12.5">the short version at the top</text>
>
> <desc>Components fetch every document, then classification and extraction work goes to the local model while synthesis goes to the paid model. Both outputs pass the claim checker into one stored research record.</desc>
>
>   <p>Where the line falls is decided by one test: is the answer inside a single document, or does it have to be built across several that disagree. Naming what caused a large move is reading the few documents published inside it, which code has already picked out. The business explainer and the segment commentary are extraction from a filing already fetched. Both are small jobs a local model does reliably, and each call carries a few thousand tokens rather than the whole evidence set, which is what makes a consumer graphics card sufficient for that lane at all. Only the right-hand lane needs every document in memory at once, which is why the paid model does it. (see: A research pass is split by section difficulty, not run wholesale on one model)</p>
>
>   <tr><td>The cause of each large move</td><td>local</td><td>the largest moves of the stored year from the bars, each with its percentage change, the session it ended on and the session its change was measured from; then, for each move, which documents fetched for the name were published inside it. A move with no document inside it is not put to the model, and a section with none at all is not written</td>
>
> <p><b>Why each section sits in its lane.</b> The four local sections each have
> their answer in one place: the few documents inside one move, one filing, the
> segment table, or values already computed. Each call carries only those, a few
> thousand tokens, which is what makes a consumer graphics card enough for that
> lane. The five paid sections are built across documents that disagree.
Now:
> <text x="192" y="236" text-anchor="middle" dominant-baseline="central" fill="var(--ink)" font-family="Segoe UI, Arial, sans-serif" font-size="14" font-weight="600">Extract</text>
> <text x="192" y="255" text-anchor="middle" dominant-baseline="central" fill="var(--muted)" font-family="Segoe UI, Arial, sans-serif" font-size="12.5">the business explainer, the segments</text>
> <text x="192" y="272" text-anchor="middle" dominant-baseline="central" fill="var(--muted)" font-family="Segoe UI, Arial, sans-serif" font-size="12.5">and the key under each figure</text>
>
> <text x="512" y="255" text-anchor="middle" dominant-baseline="central" fill="var(--muted)" font-family="Segoe UI, Arial, sans-serif" font-size="12.5">each move's cause, the two cases,</text>
> <text x="512" y="272" text-anchor="middle" dominant-baseline="central" fill="var(--muted)" font-family="Segoe UI, Arial, sans-serif" font-size="12.5">the risks, the short version at the top</text>
>
> <desc>Components fetch every document, then extraction work goes to the local model while each move's cause and the synthesis go to the paid model. Both outputs pass the claim checker into one stored research record.</desc>
>
>   <p>Where the line falls is decided by one test: is the answer inside a single document, or does it have to be built across several that disagree. The business explainer and the segment commentary are extraction from a filing already fetched, and the key under each figure explains values already computed. Those are small jobs a local model does reliably, and each call carries a few thousand tokens rather than the whole evidence set, which is what makes a consumer graphics card sufficient for that lane at all. Naming what caused a large move reads the few documents code picked out inside it, which the test puts on the left, and the fixture comparison moved it right on this machine: over one evidence set the local model wrote figures from the articles that the facts file does not hold, again after being told why, where the paid model's first draft was accepted (see: The fixture comparison moved the cause of each large move into the paid lane on this machine). The right-hand lane needs every document in memory at once, which is why the paid model does it. (see: A research pass is split by section difficulty, not run wholesale on one model)</p>
>
>   <tr><td>The cause of each large move</td><td>paid</td><td>the largest moves of the stored year from the bars, each with its percentage change, the session it ended on and the session its change was measured from; then, for each move, the documents fetched for the name that were published inside it, at most two a move, naming the fewest companies and then the earliest (see: A research pass hands each section the documents code picks for it, the company's own filing first). A move with no document inside it is not put to the model, and a section with none at all is not written</td>
>
> <p><b>Why each section sits in its lane.</b> The three local sections each have
> their answer in one place: one filing, the segment table, or values already
> computed. Each call carries only those, a few thousand tokens, which is what makes
> a consumer graphics card enough for that lane. The cause of each large move has its
> answer in the few documents inside one move as well, and sits in the paid lane on
> this machine because the comparison measured the local model failing it there. The
> five other paid sections are built across documents that disagree.
Why: the comparison ran every section both ways over one evidence set and the cause of each large move was accepted from the paid model and left out by the local one, so this machine's default lane is three sections and the figure, its key, the table's lane column and the paragraph beneath it say so. The cause row's first cell also says which documents a move is handed, which is the evidence decision's rule and is described here because it is the same row.

### 2026-09-13 - ARCHITECTURE.html - the lane table's rows for the dated calendar items and the two cases say what each is handed and what a date is held to

Authorised by: A research pass hands each section the documents code picks for it, the company's own filing first
Was:
>   <tr><td>The dated calendar items</td><td>paid</td><td>the name's stored filings and news. The provider's own earnings dates are on the calendar already and are not asked for again</td><td>one sentence per dated event the documents name that falls after the latest session</td><td>every sentence cites the stored document that dates the event, and every figure is a rounding of one in the facts file. The rule that a date must be one the facts file holds would refuse every date this section exists to state, since that file carries only the provider's next event, and what such a date is held to is settled when the first of these is written</td></tr>
>
>   <tr><td>The two cases</td><td>paid</td><td>the facts file, the name's stored filings and news, and the theme record, handed over together</td>
Now:
>   <tr><td>The dated calendar items</td><td>paid</td><td>the company's own filing and the documents published since it, as the two cases are handed, and the night the facts file was computed for. The provider's own earnings dates are on the calendar already and are not asked for again</td><td>one sentence per dated event the documents name that falls after that night</td><td>every date is one a document the sentence cites states, and falls after the night the facts file was computed for (see: A dated calendar item's date rests on a document the sentence cites and falls after the night the facts were computed for); every sentence cites that document; every figure is a rounding of one in the facts file</td></tr>
>
>   <tr><td>The two cases</td><td>paid</td><td>the facts file, the company's own filing and at most six documents published since it naming the fewest companies, and the theme record where one is stored, handed over together</td>
Why: the two rows named the name's stored filings and news handed over together, which is not what a pass hands: a year of one name's news is millions of characters, so each section is handed what the rule picks. The calendar row's last cell also states the rule its dates are held to, which A dated calendar item's date rests on a document the sentence cites and falls after the night the facts were computed for settles, and which that cell had said would be settled when the first of these was written.

### 2026-09-13 - RUNBOOK.md - the research model's answer budget is 32,768 tokens

Corrects: the shipped answer budget of 8,192 tokens was too small for the research model with its reasoning on. Over 6.8's first passes, four of the paid calls ran to the end of the budget and returned nothing a section could store, the two cases and the short version among them, each still billed; at 32,768 none did, and the longest used 12,741. Found at 6.8, which makes the first passes.
Was:
> | the most one answer may run to, in tokens | `EquityBrief:Models:Research:AnswerTokens` | `8192` |
Now:
> | the most one answer may run to, in tokens | `EquityBrief:Models:Research:AnswerTokens` | `32768` |
Why: a budget a call reasons past returns no answer and is billed anyway, so the budget is set above what the measured calls used. The ceiling a call is judged by grows with it, which costs research refused inside a larger margin of a cap.

### 2026-09-13 - RUNBOOK.md - the local lane's default is three sections

Authorised by: The fixture comparison moved the cause of each large move into the paid lane on this machine
Was:
> | the sections the local lane holds | `EquityBrief:Models:LocalLane`, one entry per section in figure 12.2's own names | the cause of each large move, what the company sells, the segment commentary, the key under each figure |
Now:
> | the sections the local lane holds | `EquityBrief:Models:LocalLane`, one entry per section in figure 12.2's own names | what the company sells, the segment commentary, the key under each figure |
Why: the table states the code's default and a test reads the two against each other.

### 2026-09-13 - SCHEMA.md - a paid call's row is named for its round, and a refused call the provider billed carries its price

Corrects: the paid call row said a refused call spends nothing, and 6.8's comparison measured three refusals the provider had counted and billed, $0.0136 of spend the ledger would not have held. It also said one section's call in one pass is one row, and a pass that writes a section again inside the same run writes a second call for it, which the run log's key refuses without the round in the stage. Found at 6.8, which makes the first passes.
Was:
> **A paid call is a row of its own, and the rows are the ledger.** The spend cap writes one row for every call it judges, under the pass's run, with the stage `research call:` followed by the section asked for, so one section's call in one pass is one row. `ok` carries what the call cost in `spend`, one model call and one network request; `paused` is a call a cap refused before it was made, counting nothing and spending nothing; `refused` and `unavailable` are calls the provider declined or did not answer, counting the attempt and spending nothing.
Now:
> **A paid call is a row of its own, and the rows are the ledger.** The spend cap writes one row for every call it judges, under the pass's run, with the stage `research call:` followed by the section asked for, and from 6.8 the round where a pass asks for a section again inside the same run, as `research call: The two cases, round 2`, so one section's call in one round of one pass is one row. `ok` carries what the call cost in `spend`, one model call and one network request; `paused` is a call a cap refused before it was made, counting nothing and spending nothing; `unavailable` is a call the provider did not answer, counting the attempt and spending nothing; `refused` is a call the provider declined, which spends nothing, or answered with nothing a section could store, which the provider counted and billed and which carries that price in `spend`.
Why: a ledger that records a billed call as nothing lets research spend past a cap by what that call cost, and a stage that repeats inside a run is a row the store refuses.

### 2026-09-13 - SCHEMA.md - the newest filing's payload carries the company's identifier at the archive

Corrects: the payload's description named what it holds, and from 6.8 it also holds the identifier a research pass reads the company's own release from the archive by, which nothing else in the store carries. Found at 6.8, where the runner first needs it.
Was:
> `payload` holds the quarter's figures, the balance sheet, and the margin computed from that filing's own revenue and gross profit.
Now:
> `payload` holds the quarter's figures, the balance sheet, and the margin computed from that filing's own revenue and gross profit, and on the newest filing's row the company's identifier at the filings archive, from 6.8, which a research pass reads the company's own release by.
Why: a column's description that omits a key its readers depend on is a key the next reader finds by accident.

### 2026-09-13 - BUILD_PLAN.md - the research model's live transport is 6.7's, and 6.6 counted the first model call

Corrects: two sentences in phase 6's checkpoints that were wrong when 6.0 wrote them. 6.8 held the live implementation behind 6.7's interface, and 6.7's own deliverable prices a call from the provider's counts for the request as sent, which can only be captured through the transport that sends it, so 6.7 wrote the transport and 6.8's text described work already done. And 6.7 said the run log's `model_calls` carries a figure other than zero for the first time there, where 6.6's local lane counts every call it makes. Found at 6.7, the first while the recordings were captured through the feed and the second while its PROGRESS entry was checked against the store.
Was:
> `IResearchModelFeed` with its recorded double and configuration naming which provider answers (see: The research model is one interface with an implementation per wire format, chosen by configuration and never falling back), its key in the secrets file under its own path and refused by name at startup when blank, which is the refusal `RUNBOOK.md` promises for the one key the tree holds today.
>
> The run log's `spend` and `model_calls` carry a figure other than zero for the first time here, which is what that store row has promised since migration 1 and nothing has written.
>
> and at 6.8:
> Filings and news read by ticker, the theme record read, the narrative sections written, and every document stored as it is fetched through 6.3's test. The live implementation behind 6.7's interface, and the recording written as the pass runs.
Now:
> `IResearchModelFeed` with its recorded double and configuration naming which provider answers (see: The research model is one interface with an implementation per wire format, chosen by configuration and never falling back), its key in the secrets file under its own path and refused by name at startup when blank, which is the refusal `RUNBOOK.md` promises for the one key the tree holds today. The live transport behind the interface lands here as well, because the recorded calls the cap is priced against are captured through the request the feed itself sends, and counts captured from any other request price a call nothing makes.
>
> The run log's `spend` carries a figure other than zero for the first time here, which is what that store row has promised since migration 1 and nothing has written. Its `model_calls` first carried one at 6.6, where the local lane's calls are counted.
>
> and at 6.8:
> Filings and news read by ticker, the theme record read, the narrative sections written, and every document stored as it is fetched through 6.3's test. The recording written as the pass runs, through the live transport 6.7 put behind its interface.
Why: a plan that places work after the checkpoint that did it leaves 6.8 owing a deliverable that exists, and a sentence claiming a first that an earlier checkpoint had already produced is a figure a reader would take as measured.

### 2026-09-13 - RUNBOOK.md - the research model's settings name its provider in configuration alone, and its key moves beside them

Authorised by: The research model is named only in configuration, and a call is priced at the configured rates its own timestamp falls in
Was:
> | DeepSeek | the research model | peak rates are double; peak falls late at night in Eastern time, so reading after the close is never billed at peak |
>
> and in the secrets table:
> | DeepSeek | `EquityBrief:Providers:DeepSeek:ApiKey` | `EquityBrief.Worker` |
>
> and under the research model's settings:
> The research model is the one part of the system that costs money. Its key is in the table above and is refused by name at startup when it is blank, on a fixture run as on a live one. Everything else has a default.
>
> | Setting | Key | Default |
> |---|---|---|
> | which provider answers | `EquityBrief:Models:Research:Provider` | `deepseek` |
> | which of its models | `EquityBrief:Models:Research:Model` | `deepseek-flash` |
> | whether it thinks before answering | `EquityBrief:Models:Research:Thinking` | `enabled` |
> | how long one call may take, in seconds | `EquityBrief:Models:Research:TimeoutSeconds` | `600` |
> | the most research may spend in a UTC day, in dollars | `EquityBrief:Spend:DayCap` | `10` |
> | the most research may spend in a UTC month, in dollars | `EquityBrief:Spend:MonthCap` | `50` |
>
> **A provider or a model the build has no price for is refused at startup**, rather than called and recorded as costing nothing. The models the build prices are the provider's own two, at the rates its page gave on 2026-09-13; when the provider changes a price, the rates in the research model's feed are what change.
Now:
> | the research model's provider, DeepSeek as shipped | the research model | named in configuration and nowhere in the code, so another provider is a change of settings; the shipped one's peak rates are double and its peak falls late at night in Eastern time, so reading after the close is never billed at peak |
>
> and in the secrets table:
> | the research model's provider | `EquityBrief:Models:Research:ApiKey` | `EquityBrief.Worker` |
>
> and under the research model's settings:
> The research model is the one part of the system that costs money, and nothing in the code says which model it is. The shipped `appsettings.json` beside the worker names the provider and the model this installation uses and the rates that provider charges for it; a different provider or model is different values, set in `appsettings.Secrets.json` or the environment, either of which wins over the shipped file. Its key is in the table above and is refused by name at startup when it is blank, on a fixture run as on a live one.
>
> | Setting | Key | As shipped |
> |---|---|---|
> | the wire format the provider serves | `EquityBrief:Models:Research:Format` | `openai` |
> | where the provider answers | `EquityBrief:Models:Research:BaseAddress` | `https://api.deepseek.com/` |
> | which model answers | `EquityBrief:Models:Research:Model` | `deepseek-flash` |
> | the provider's own request fields, written as the JSON object it takes | `EquityBrief:Models:Research:Options` | none |
> | how long one call may take, in seconds | `EquityBrief:Models:Research:TimeoutSeconds` | `600` |
> | the most one answer may run to, in tokens | `EquityBrief:Models:Research:AnswerTokens` | `8192` |
> | dollars per million prompt tokens the provider serves from its cache | `EquityBrief:Models:Research:Prices:CacheHit` | `0.003` |
> | dollars per million prompt tokens it does not | `EquityBrief:Models:Research:Prices:CacheMiss` | `0.15` |
> | dollars per million output tokens, reasoning included | `EquityBrief:Models:Research:Prices:Output` | `0.60` |
> | the UTC hours the rates are multiplied in, one entry per window written as a start and an end hour | `EquityBrief:Models:Research:Prices:PeakHours` | `01-04, 06-10` |
> | the days those hours fall on, one entry per day | `EquityBrief:Models:Research:Prices:PeakDays` | `Monday, Tuesday, Wednesday, Thursday, Friday` |
> | what the rates are multiplied by in those hours | `EquityBrief:Models:Research:Prices:PeakMultiple` | `2` |
> | the most research may spend in a UTC day, in dollars | `EquityBrief:Spend:DayCap` | `10` |
> | the most research may spend in a UTC month, in dollars | `EquityBrief:Spend:MonthCap` | `50` |
>
> **Switching model is a change to these values and the key, and to nothing else.** A provider serving the OpenAI chat completions format takes its address, its model and its key, and its rates from its own price page; a provider with no peak pricing names no peak hours, and its multiple is then read as one. Options are the provider's own fields sent beside the request, as the shipped provider takes `{"thinking":{"type":"disabled"}}` to answer without reasoning first. A model asked with options is recorded as a different writer from the same model asked without them, so a section says which model wrote it and how it was asked. The one format this build implements is `openai`, and another is refused at startup rather than answered by this one (see: The research model is one interface with an implementation per wire format, chosen by configuration and never falling back).
>
> **A model with no prices is refused at startup**, rather than called and recorded as costing nothing, and so is a rate at or below zero for the uncached prompt or the output, a peak window whose start is not before its end, a multiple below one, a day that is not a day of the week, and options that set the model, the messages, the answer's budget or the stream, which the feed writes itself. The shipped rates are what the provider's page gave on 2026-09-13 for the shipped model. When the provider changes a price, these values are what change, and a call already made keeps the price its run log row recorded (see: The research model is named only in configuration, and a call is priced at the configured rates its own timestamp falls in).
Why: the operator ruled that the research model is to be switchable to any model, and a provider, a model list and a thinking mode written into the feed made switching a change to code. Every value the feed sends and every rate it prices at is now a setting the shipped configuration holds, so the table states what that file holds and a test reads the file to hold it there, and the key sits under the model's own settings rather than under a provider's name.

### 2026-09-13 - RUNBOOK.md - the caps' obligation fires on research passes, as its own row counts them

Corrects: the runbook said the obligation settling the caps fires once twenty paid calls carry a recorded cost, and the obligation's row in BUILD_PLAN.md counts research passes, which the run page's priced line counts as distinct runs rather than as calls. Found at 6.7 while the section beside it was rewritten.
Was:
> **Both caps are proposals**, marked so in section 17, and the obligation that settles them fires on the run page once twenty paid calls carry a recorded cost. A cap stops research rather than warning about it: a call is refused before it is made where the most it could cost would take the day or the month past its cap, research resumes when that UTC day or month ends, and the name page says research is paused and when it resumes (see: The spend cap is a stop, not an allowance).
Now:
> **Both caps are proposals**, marked so in section 17, and the obligation that settles them fires on the run page once twenty research passes carry a recorded cost. A cap stops research rather than warning about it: a call is refused before it is made where the most it could cost would take the day or the month past its cap, research resumes when that UTC day or month ends, and the name page says research is paused and when it resumes (see: The spend cap is a stop, not an allowance).
Why: a pass asks for several sections and each is a call, so a trigger read as calls fires after a handful of passes and settles both caps from far fewer priced passes than the row asks for.

### 2026-09-13 - ARCHITECTURE.html - section 5's sources box and section 6.1 name the research model as configuration

Authorised by: Two models for two jobs, and which research model answers is configuration
Was:
> <div class="box src"><b>Language models</b>a local model on the operator's own GPU for prose; DeepSeek V4 for research. Neither can search the web on its own, so documents are fetched by the components below and handed to the model</div>
>
> <p>Two models are used for different jobs. A local model on the operator's GPU writes prose from numbers, which is a small job it does well and free. DeepSeek V4 does research, which is a large-context job a local model does badly. Its API accepts the OpenAI and Anthropic request formats, so the endpoint is configuration rather than a code path, and the report footer always says which model wrote what. (see: Two models for two jobs, and the research model is DeepSeek V4) (see: A research record is written and dated per section, not as a whole)</p>
>
> <p>One consequence shapes the research runner. DeepSeek offers no server-side web search, so the model cannot go and find things; it can only read what it is handed through tool calls (see: The model never fetches; components fetch and hand it documents). For a per-name pass that costs nothing, because the documents it needs are filings and news the system already fetches. For a theme record it matters, because an industry's own pricing cycle is published by research firms rather than filed with a regulator, and that is section 22's second question.</p>
>
> <p>Rates carry a peak and off-peak split, with peak at double. In Eastern time the peak windows fall late at night, so the market session and the hours just after the close are off-peak and interactive reading is never billed at the peak rate. Anything queued rather than interactive is scheduled into off-peak deliberately (see: Queued work runs off-peak, and every schedule is written in UTC).</p>
Now:
> <div class="box src"><b>Language models</b>a local model on the operator's own GPU for prose; a hosted model named in configuration for research. Neither can search the web on its own, so documents are fetched by the components below and handed to the model</div>
>
> <p>Two models are used for different jobs. A local model on the operator's GPU writes prose from numbers, which is a small job it does well and free. A hosted model does research, which is a large-context job a local model does badly. Which hosted model is configuration and not code: the wire format, the address, the model, the options it is asked with and the rates it is priced at are settings, the shipped configuration names DeepSeek V4, and switching model changes those settings and nothing else. The report footer always says which model wrote what, with the options it was asked with. (see: Two models for two jobs, and which research model answers is configuration) (see: The research model is named only in configuration, and a call is priced at the configured rates its own timestamp falls in) (see: A research record is written and dated per section, not as a whole)</p>
>
> <p>One consequence shapes the research runner. The research model is given no web search of its own, whichever provider serves it, so the model cannot go and find things; it can only read what it is handed through tool calls (see: The model never fetches; components fetch and hand it documents). For a per-name pass that costs nothing, because the documents it needs are filings and news the system already fetches. For a theme record it matters, because an industry's own pricing cycle is published by research firms rather than filed with a regulator, and that is section 22's second question.</p>
>
> <p>The shipped provider's rates carry a peak and off-peak split, with peak at double, and the configuration states both. In Eastern time the peak windows fall late at night, so the market session and the hours just after the close are off-peak and interactive reading is never billed at the peak rate. Anything queued rather than interactive is scheduled into off-peak deliberately (see: Queued work runs off-peak, and every schedule is written in UTC).</p>
Why: which hosted model does research is now the value the shipped configuration holds rather than a decision's name, so the document describes a hosted model named in configuration and says which one ships. The sentence that the provider's API accepts two request formats, so the endpoint is configuration rather than a code path, was already wrong when the wire format decision was taken, since two providers do not share a request shape, and it is removed rather than carried.

### 2026-09-13 - ARCHITECTURE.html, SCHEMA.md - the run log is appended to by every component that writes

Corrects: the run log's catalogue row and SCHEMA's ownership row both said every component appends, and three do not: the single page app and the mark renderer touch no store, and the trend classifier returns its label to the ladder builder and writes nothing. Each of the three says "none" in its own Writes cell, so the corpus contradicted itself between rows. Found at 6.7, which is where the run log row is owed and where asserting it over the components in code showed the three.
Was:
> <tr><td><b>Run log</b></td><td><span class="layer L-store">store</span></td><td>always</td><td>every component appends</td><td>run log</td><td>durations, counts, spend, failures by component name</td></tr>
>
> and in SCHEMA.md's ownership table:
> | `run_log` | every component appends | RunLog | none |
Now:
> <tr><td><b>Run log</b></td><td><span class="layer L-store">store</span></td><td>always</td><td>every component that writes appends</td><td>run log</td><td>durations, counts, spend, failures by component name</td></tr>
>
> and in SCHEMA.md's ownership table:
> | `run_log` | every component that writes appends | RunLog | none |
Why: what the run log is owed is a row from every stage that changes a store, which is what makes a night and a pass readable afterwards; a renderer that changes nothing has nothing to record, and a statement that it appends is one no test could ever hold.

### 2026-09-13 - ARCHITECTURE.html - the spend cap's catalogue and matrix rows, and the two runners reaching the research model through it

Authorised by: Every paid call is made through the spend cap, which holds the research model
Was:
> <tr><td><b>Theme research runner</b></td><td><span class="layer L-research">research</span></td><td>on demand, per theme</td><td>theme store, source documents, search tool, research model</td><td>theme store, source documents</td><td>researches an industry's own cycle once, so every name in that industry shares one paid pass, testing every document it fetched for admissibility before storing it as the per-name runner does</td></tr>
>
> <tr><td><b>Research runner</b></td><td><span class="layer L-research">research</span></td><td>on demand, per name</td><td>facts, fundamentals, filings archive, news feed, theme store, source documents, research model</td><td>research store, source documents</td><td>writes the narrative sections pending the checker's verdict, tests every document it fetched for admissibility before storing it, and keeps what it stored with the verdict that admitted or refused it</td></tr>
Now:
> <tr><td><b>Theme research runner</b></td><td><span class="layer L-research">research</span></td><td>on demand, per theme</td><td>theme store, source documents, search tool</td><td>theme store, source documents</td><td>researches an industry's own cycle once, so every name in that industry shares one paid pass, testing every document it fetched for admissibility before storing it as the per-name runner does, and has the spend cap make its every paid call</td></tr>
>
> <tr><td><b>Research runner</b></td><td><span class="layer L-research">research</span></td><td>on demand, per name</td><td>facts, fundamentals, filings archive, news feed, theme store, source documents</td><td>research store, source documents</td><td>writes the narrative sections pending the checker's verdict, tests every document it fetched for admissibility before storing it, keeps what it stored with the verdict that admitted or refused it, and has the spend cap make its every paid call</td></tr>
> <tr><td><b>Spend cap</b></td><td><span class="layer L-research">research</span></td><td>before every paid call</td><td>run log, research model</td><td>run log</td><td>the one component that makes a paid call. It reads what the run log says was spent today and this month, refuses a call before it is made where the most that call could cost would take spend past either cap, and records on a row of its own what each call it made did cost, so the ledger is what the store holds and neither runner can reach the research model except through it (see: Every paid call is made through the spend cap, which holds the research model)</td></tr>
>
> and in the read and write matrix, a row after the research runner's:
> <tr><td>Spend cap</td><td></td><td></td><td></td><td></td><td></td><td></td><td></td><td></td><td></td><td></td><td></td><td></td><td><span class="r">R</span> <span class="w">W</span></td></tr>
Why: the cap is a stop only if no paid call can be made around it, so the component that makes paid calls is the cap and the runners hold it rather than the model. Their Reads cells lose the research model for that reason, and the cap's row is where the model is read and the run log is both read and written.

### 2026-09-13 - ARCHITECTURE.html - section 12.2's lane table says what code works out, what the model writes and what each section must pass

Corrects: the table said what each section is handed and why it sits in its lane, and nothing about what is worked out before a model is asked, what the model is asked to write or what the section must pass to be stored, so it did not explain what a pass does or calculates. Its cause row and the key above it described one date's headlines and that day's move, where a move spans sessions and, from 6.6, a cause may rest only on a document published inside its move, which code pairs before the model sees anything. Raised by the operator at 6.6, reading the table against what the checkpoint built. Writing the new last column found that the date rule would refuse every date the dated calendar items section exists to state, and the row says so rather than stating a rule that holds.
Was:
> <h4>What each lane actually writes</h4>
> <p>The figure names these inside its boxes. They are set out here because the
> key above explains where the line falls without saying what sits on either side
> of it.</p>
> <table>
>   <tr><th style="width:20%">Section</th><th>Lane</th><th>What goes in</th><th>Why it sits there</th></tr>
>   <tr><td>The cause of each large move</td><td>local</td><td>one date's headlines, and that day's percentage move</td><td>the answer is one of the headlines, so this is a choice among a set rather than a construction</td></tr>
>   <tr><td>What the company sells</td><td>local</td><td>one section of one filing</td><td>extraction: the answer is in the document, close to verbatim</td></tr>
>   <tr><td>The segment commentary</td><td>local</td><td>the segment table from the same filing</td><td>extraction again, one sentence per business unit</td></tr>
>   <tr><td>The key under each figure</td><td>local</td><td>the numbers that figure was drawn from</td><td>a fixed explanation over known values, with nothing to weigh</td></tr>
>   <tr><td>The industry cycle</td><td>paid</td><td>the theme record for this industry and nothing of the name's own</td><td>it is the one section written from a record shared by every name in the industry, and the record is built by a paid pass of its own (see: Industry research is per theme, not per name)</td></tr>
>   <tr><td>The dated calendar items</td><td>paid</td><td>the stored news and filings, read for items that carry a date</td><td>a dated item is a claim resting on a source rather than a provider event, and deciding which of several documents dates a thing is a judgement across them rather than a lookup in one (see: A calendar event is fetched once for the whole index, and the calendar holds provider events only)</td></tr>
>   <tr><td>The two cases</td><td>paid</td><td>the facts file, the filings, every stored news item, the theme record</td><td>the answer is in no single document; it is the shape of the disagreement between them, and a small model given contradictory evidence produces something fluent that says nothing</td></tr>
>   <tr><td>The risks, each with what would confirm it</td><td>paid</td><td>the same whole set</td><td>which risks are live now cannot be decided from one document</td></tr>
>   <tr><td>The short version</td><td>paid</td><td>the same whole set, written last</td><td>it summarises sections that do not agree with each other</td></tr>
> </table>
> <p>Every local call carries only the documents its own section needs, a few
> thousand tokens, which is what makes a consumer graphics card sufficient for
> that lane. Only the paid lane holds the whole evidence set at once. After the
> split the two are identical: both outputs pass the same claim checker, and both
> land in the same research record carrying their own date and the model that
> wrote them.</p>
>
> and in the key above the table:
> Naming what caused a large move is a choice among that day's headlines.
Now:
> <h4>What each lane actually writes</h4>
> <p>Every section is made in the same three steps. Code works out the figures and
> picks the documents the section may rest on. A model writes the words around
> them, and is never asked for a number or a date. The claim checker then refuses
> the section unless every figure in it is one code computed and every sentence
> rests on a document it is allowed to rest on (see: Code owns every number). The
> table says what each step does for each section. The lane is this machine's
> default and is a setting; the rest of a row stays the same when a section moves
> lane.</p>
> <table>
>   <tr><th style="width:16%">Section</th><th>Lane</th><th>What code works out first</th><th>What the model is asked to write</th><th>What it must pass to be stored</th></tr>
>   <tr><td>The cause of each large move</td><td>local</td><td>the largest moves of the stored year from the bars, each with its percentage change, the session it ended on and the session its change was measured from; then, for each move, which documents fetched for the name were published inside it. A move with no document inside it is not put to the model, and a section with none at all is not written</td><td>one sentence for each move that has a document inside it, saying what that document gives as the cause</td><td>each sentence names the session a move ended on and cites a stored, admitted document published inside that move (see: A cause of a move rests only on a document published inside that move); every figure is a rounding of one in the facts file</td></tr>
>   <tr><td>What the company sells</td><td>local</td><td>the company's own filing, fetched by ticker and tested for admissibility, handed over only if it passed</td><td>two to four sentences on what the company sells and to whom</td><td>every sentence cites a stored, admitted document; every figure is a rounding of one in the facts file</td></tr>
>   <tr><td>The segment commentary</td><td>local</td><td>the latest quarter of the segment table, read out of the filing into the facts file as one figure per business unit and line item, and the filing it came from</td><td>one sentence per business unit, saying what it reported for the quarter</td><td>every figure is a segment figure the facts file holds; every sentence cites the filing</td></tr>
>   <tr><td>The key under each figure</td><td>local</td><td>nothing new: every value the figure draws, the close, the averages, the levels, momentum, the latest quarter and the valuation, is already computed and in the facts file</td><td>three to five sentences on what those values show, for a reader who has not seen the figure, with money rounded to millions or billions and margins written as percentages</td><td>every figure is a rounding of one in the facts file. It cites no document, because it explains values already known and has nothing to weigh</td></tr>
>   <tr><td>The industry cycle</td><td>paid</td><td>a search per industry rather than per name, scoped to the industry's source list, a date range and full page text, with what passes admissibility stored as the theme record every name in the industry shares</td><td>where the industry's own prices are in their cycle, and the three things capping or driving them</td><td>every sentence cites a document in the theme record; every figure is one code computed for the industry</td></tr>
>   <tr><td>The dated calendar items</td><td>paid</td><td>the name's stored filings and news. The provider's own earnings dates are on the calendar already and are not asked for again</td><td>one sentence per dated event the documents name that falls after the latest session</td><td>every sentence cites the stored document that dates the event, and every figure is a rounding of one in the facts file. The rule that a date must be one the facts file holds would refuse every date this section exists to state, since that file carries only the provider's next event, and what such a date is held to is settled when the first of these is written</td></tr>
>   <tr><td>The two cases</td><td>paid</td><td>the facts file, the name's stored filings and news, and the theme record, handed over together</td><td>the bull case and the bear case side by side, each ending in what it needs to see at the next report</td><td>every figure is a rounding of one in the facts file; every sentence cites a stored, admitted document</td></tr>
>   <tr><td>The risks, each with what would confirm it</td><td>paid</td><td>the same whole set</td><td>each risk the documents support, with the figure or event that would confirm it</td><td>the same two rules</td></tr>
>   <tr><td>The short version</td><td>paid</td><td>the same whole set and the sections already written, handed over last</td><td>three or four paragraphs: what is true, what the market is arguing about, and what the plan therefore is</td><td>the same two rules</td></tr>
> </table>
> <p><b>Why each section sits in its lane.</b> The four local sections each have
> their answer in one place: the few documents inside one move, one filing, the
> segment table, or values already computed. Each call carries only those, a few
> thousand tokens, which is what makes a consumer graphics card enough for that
> lane. The five paid sections are built across documents that disagree. Which
> risks are live, what the two cases are and what they come to cannot be read out
> of one document, and a small model given contradictory evidence produces
> something fluent that says nothing. A dated item is a claim resting on a source
> rather than a provider event, and deciding which of several documents dates a
> thing is a judgement across them (see: A calendar event is fetched once for the
> whole index, and the calendar holds provider events only). The industry cycle is
> paid for a reason of its own: it is written from a record every name in its
> industry shares, and that record is built by a paid pass of its own (see:
> Industry research is per theme, not per name). After the split the two lanes are
> the same: both pass one claim checker and land in one research record, each
> section carrying its own date and the model that wrote it.</p>
>
> and in the key above the table:
> Naming what caused a large move is reading the few documents published inside it, which code has already picked out.
Why: a reader of the table should be able to say, for any section, which part is arithmetic, which part is words and what stops a wrong one being stored. The reasons each section sits in its lane are kept, in a paragraph beneath the table rather than a column, with both decisions they cited.

### 2026-09-13 - BUILD_PLAN.md - the model carve-out's first half, landed at 6.6 with the first model client

Corrects: 6.10's text said the whole of the nightly model-call carve-out lands there, before the queue. The scan that finds a model client refuses one in every shipped file, so the checkpoint that ships the local lane's feed could not pass `nightly-cost` without naming that file, and 6.6 is that checkpoint. Found at 6.6, on the first run after the feed was written.
Was:
> **The nightly model-call carve-out lands first, in its own commit, before the queue**, for the reason 2.1 states (see: The night's zero-model-call rule bounds the arithmetic, and the overnight queue is carved out of it by name).
Now:
> **The nightly model-call carve-out's second half lands first, in its own commit, before the queue**, for the reason 2.1 states (see: The night's zero-model-call rule bounds the arithmetic, and the overnight queue is carved out of it by name). The first half landed at 6.6 with the first model client, because the scan that finds one refuses it in any shipped file and the local lane's feed could not ship otherwise: it names the one file a model may be reached from and asserts that file is a model feed. What lands here is the half about the night, which lane the night may call and that its calls come from step 17 alone.
Why: the half that names a file and the half that names what the night may call are separable, and only the first was needed before a queue exists. The decision states the carve by name and says nothing about which checkpoint lands each part, so nothing it decides moves.

### 2026-09-13 - BUILD_PLAN.md - the two obligations 6.4 filed against 6.6, discharged

Corrects: nothing was wrong. Two carried obligations reached the checkpoint that produces their evidence, and each row records what was measured and ruled, in the form 6.3's discharge took.
Was:
> | **A whole-number figure tied to the fact it rounds, measured against written sections** | 6.4 | 6.6 | 6.6 writes the first sections a model produced from a facts file, and 6.6 is where the prompt that tells the model how to write a figure is written, so it is where the evidence and the only repair both first exist. 6.4 measured the claim checker's number rule over 11,727 figures from 411 live articles against AAPL's facts file of 38 values: 1,301 of 7,939 whole numbers pass by coincidence against 76 of 2,091 at one decimal and 14 of 1,547 at two. So the rule is strong for a figure stated with decimals and weak for a whole number, and what would close that is attribution, a figure tied to the fact it rounds rather than to any fact near it. What 6.6 produces is written sections to measure the coincidence rate over prose written from the file rather than prose written without it, and a prompt that can require a figure to name its fact |
>
> | **The facts file carries the figures a local lane section quotes** | 6.4 | 6.6 | 6.6 is where the prose writer first writes the local lane's sections. The segment commentary is one sentence per business unit from the segment table, and the facts file carries eleven fundamentals figures and none of the segment table, so every sentence the commentary could write fails the number rule as 6.4 built it and the section would fall back on every name. What 6.6 produces is the first segment commentary a model wrote, which is what shows which figures it quotes, and the facts assembler is where they would be added |
Now:
> | **A whole-number figure tied to the fact it rounds, measured against written sections** | 6.4 | 6.6, discharged | measured over the seven section answers the local model wrote from a facts file at 6.6, and ruled. Of the 58 figures in them none is unmatched and 12 are whole numbers. Eleven of the twelve are the fact they state, and the twelfth is the coincidence this row was filed for: an accepted key under each figure called the relative strength index "14 periods", and the 14 passed because a move of minus 13.89 per cent rounds to it. It was a window length, and the reader knew days, sessions and weeks as windows and not periods, so the repair is that a period is one, asserted on that sentence. Attribution is not built. Prose written from the file quoted money at a scale where the tolerance is half a million and wrote its small whole numbers as window lengths, so the weakness 6.4 measured over articles written without the file did not appear in prose written with it, and a prompt requiring every figure to carry the name of its fact would make every sentence a list. The residual is stated rather than closed: a small whole number that is neither a window nor a fact can still pass on a coincidence. What it read before: 6.6 writes the first sections a model produced from a facts file, and 6.6 is where the prompt that tells the model how to write a figure is written, so it is where the evidence and the only repair both first exist. 6.4 measured the claim checker's number rule over 11,727 figures from 411 live articles against AAPL's facts file of 38 values: 1,301 of 7,939 whole numbers pass by coincidence against 76 of 2,091 at one decimal and 14 of 1,547 at two. So the rule is strong for a figure stated with decimals and weak for a whole number, and what would close that is attribution, a figure tied to the fact it rounds rather than to any fact near it. What 6.6 produces is written sections to measure the coincidence rate over prose written from the file rather than prose written without it, and a prompt that can require a figure to name its fact |
>
> | **The facts file carries the figures a local lane section quotes** | 6.4 | 6.6, discharged | the facts assembler carries the latest quarter of the segment table the archive supplies, named by group, line item and period end, and every stored move with the session it was measured from beside the one it ended on. Measured over the first sections a model wrote from it: the segment commentary quoted four figures and what the company sells quoted two, each a segment's revenue or operating income for the quarter ended 2026-07-31 and each matched by exactly one segment fact, and both sections were accepted on their first draft. The move starts arrived because a cause may rest only on a document published inside its move (see: A cause of a move rests only on a document published inside that move). What it read before: 6.6 is where the prose writer first writes the local lane's sections. The segment commentary is one sentence per business unit from the segment table, and the facts file carries eleven fundamentals figures and none of the segment table, so every sentence the commentary could write fails the number rule as 6.4 built it and the section would fall back on every name. What 6.6 produces is the first segment commentary a model wrote, which is what shows which figures it quotes, and the facts assembler is where they would be added |
Why: an obligation is closed in its own row, where the next reader of the table meets it, with the text it carried kept inside the row rather than lost.

### 2026-09-13 - ARCHITECTURE.html - the claim checker's catalogue row names the theme store it reads and writes

Corrects: the catalogue row gave the claim checker the research store alone, while SCHEMA gives it Update on `theme_section`, the read and write matrix fills its Research and theme column, and section 12.2 says both runners' output passes one checker. So three of the four places the component is declared agreed and the fourth did not. Found at 6.4 by `component-access`, on the first run after the class declared what SCHEMA and the matrix already said.
Was:
> research store, facts, source documents | research store
Now: the Reads cell names research store, theme store, facts, source documents, the Writes cell names research store, theme store, and the What it does cell adds that it checks theme sections as well as a name's.
Why: the other three statements are the ones with reasons behind them. SCHEMA's ownership row is what `writer-ownership` enforces, the matrix's column is one column for both stores by design, and a theme runner whose sections nothing checks would write the only unchecked prose in the report.

### 2026-09-13 - BUILD_PLAN.md - three obligations the claim checker's measurement created, cited back by 6.6 and 6.9

Authorised by: A claim is a sentence, and every sentence in a researched section names the document it rests on
Was:
> 6.6's text opened its third paragraph with "A section is assigned to the local lane that the machine cannot hold", and 6.9's third paragraph opened with "`theme_section` written per theme with the industries that map to it", with no citation in either to an obligation 6.4 created.
Now: three rows in the carried obligations table, each due at the checkpoint that first produces its evidence: whole-number figures tied to the fact they round and the facts file carrying the segment figures, both at 6.6, and a theme section's figures at 6.9. A paragraph is added to each of 6.6 and 6.9 citing its rows back.
Why: 6.4 measured its own number rule and the measurement said where it is weak, and building the checker also showed two sections whose figures the facts file does not carry. None of the three can be settled at 6.4, because each needs a section a model wrote, and a finding that can only be settled later is a row chased from one end rather than a sentence in a record.

### 2026-09-13 - SCHEMA.md - the research and theme tables written out, with what the four statuses mean

Corrects: `theme_section` was described as a difference from `research_section`, which cannot be compared column by column against a built store, and the four statuses were listed with no meaning stated anywhere but `RUNBOOK.md`. Found at 6.4 while writing the migration that creates both tables.
Was:
> ### theme_section
> Grain: one row per theme, section and version. Same columns as `research_section` with `theme` in place of `ticker`, plus `industries` holding the industries that map to this theme.
Now: `theme_section` has a column table of its own with the same nine columns as `research_section` and `industries` last. The `research_section` notes gain what `section` is named by, that `prose` is empty for a section with no admissible source, the order `source_ids` is in, and that `reject_reason` is set on a fallback as well as a rejection. Two paragraphs follow: what the four statuses mean and how the retry is bounded, and that the checker writes two columns and no more.
Why: a table described by difference cannot be compared column by column against the store, and `schema-columns` compares every table the store holds, so the first migration to create it would have failed that check on a table SCHEMA does describe. The status meanings were not stated anywhere: the column listed four words, the architecture used rejected and fell back without saying which was the retry, and `RUNBOOK.md` was the only place that said what a fallback is. The retry bound is stated here because it is a property of the stored rows rather than of a return value, which is where the done condition asks for it to be asserted.

### 2026-09-13 - ARCHITECTURE.html - the marketing marker is a pairing, after a live run refused three real articles

Corrects: the admissibility row's marketing half rested on invitation language, and a live run over 411 real articles from one dated request refused three pieces of ordinary consumer-finance reporting. Two tripped on one invitation written twice, "sign up" and "signing up" counted as two. The third survived that repair: an article on how savers lose a retirement pot says "opening an account" and "sign up", which are two genuinely different invitations inside an article whose subject is accounts. Found by the demonstration this checkpoint ran, not by reading.
Was:
> A page carries an article where one paragraph of it runs to at least 3 sentences and at least 40 words, and 2 invitations to open an account mark a page that exists to open one
Now: the paragraph rule is unchanged, and the marketing half reads that a page exists to open an account where it carries one regulatory risk warning, or a leveraged-product term beside an invitation to open one, with the row stating that invitation language alone is not a marker and naming what the 411 measured.
Why: invitation language does not separate an article about accounts from a page selling one, and no amount of counting repairs that. What does separate them is the product: over the same 411 articles, "contract for difference" appears 0 times, "cfd" 0, "spread bet" twice and "trading platform" three times, and not one of those five carries any invitation at all, while both broker pages the by-hand measurement read carry the product and the invitation together. The number 2 leaves the row because the rule no longer counts anything, and the fixture's second broker page lost its risk warning in the same pass so the pairing is exercised by a committed document rather than only by constructed text.

### 2026-09-13 - BUILD_PLAN.md - 6.3's done condition amended, and the clause it cannot assert moved to 6.4

Corrects: 6.3's done condition required that a pass finding no admissible source for a section leaves the section absent with one line saying so. That is a claim about a surface which draws a section, and nothing at 6.3 can draw one: `source_document` carries no ticker and no section, by design, because one document supports claims about several names and about a theme, so no store at 6.3 ties a document to the section it would be cited in. Found by the phase report, which left one claim out of scope at 6.3 after everything else had passed.
Was:
> **Done when** each denied category is refused by a fixture document and nothing resting on it is written, a source is returned but its text cannot be retrieved and the result is discarded rather than cited with the url and the reason on the run log, a pass finds no admissible source for a section and the section is absent with one line saying so, and each refusal is read back off the run page's own markup rather than off the model behind it.
Now: the clause is replaced by the half 6.3 does deliver, being an intake that admits nothing at all saying so in one reading a pass can gate on and recording what was fetched and why each document was refused, with a paragraph beneath the condition stating that it was amended and why. The moved clause is added to 6.4's done condition, naming 6.3 as the checkpoint that could not assert it, because 6.4 creates `research_section` and is the first point at which a section exists to be absent and to carry the reason it is.
Why: the alternative was to draw the surface from something that does not exist, which is the one thing a done condition must not buy. A checkpoint amending its own done condition is legitimate and the rule about it is that the amendment cannot be invisible, so it is here and it is in 6.3's PROGRESS entry in the words CLAUDE.md asks for.

### 2026-09-13 - ARCHITECTURE.html - the inadmissible document row's rationale loses its commas so the row's parts can be read off it

Corrects: the row's second sentence formed a comma run of its own, so the reader that takes a row's parts from its own words picked up three items of prose from it alongside the six documents. Found at 6.3 while discharging the obligation to read that row's parts off the row, which is the direction a decomposition into four of six would otherwise pass.
Was:
> Six, because section 17 names four denied categories and a date rule and section 18 names the unretrievable case, and this cell named three of those until 6.0
Now: Six because section 17 names four denied categories and a date rule and section 18 names the unretrievable case. This cell named three of those until 6.0 and carried one verdict over all six until 6.3, where each became a claim of its own read off this row's own words.
Why: the reader is a run of three or more comma-separated items, which is what these rows use to enumerate. A rationale sentence written with commas is indistinguishable from an enumeration, so either the reader learns about sentences or the sentence stops looking like a list. The second is cheaper and it is the one the row can carry: the reader is shared with every decomposed row in section 15 and teaching it to skip a trailing clause would change what it reads for all of them.

### 2026-09-13 - BUILD_PLAN.md - the inadmissible document obligation discharged, and one created against 6.9

Authorised by: Each denied category is refused by a marker the document carries, and the domain is never one of them
Was:
> | **The inadmissible document row's parts read off the document** | 6.0 | 6.3 | 6.3 is where the six documents arrive, which is what makes the row's parts readable off the row rather than chosen by a reader. Section 19.1's row names six kinds the test refuses and carries one verdict over them, so five could be missing and the row would still pass, which is the fault the fifth phase 5 sign-off review found on section 15's rows. 6.0 widened the text and left the decomposition, because the reader that reads parts off a document is scoped to section 15's tables and widening it would make every fixture row enumerating three or more items owe parts in the same pass |
Now: the row is marked discharged and states what discharged it, with its prior text kept in the row as that table's convention requires. A second row is created, due at 6.9, for the markers being tested against what the search tool returns, and 6.9's own text cites it back.
Why: the discharge is a decomposition plus the assertion that makes it a reading of the row rather than a second statement of it. The new obligation exists because 6.3's markers were measured on pages fetched by hand: that is real evidence and it is not the same evidence a search tool produces, and the difference is worth chasing from one end rather than being remembered.

### 2026-09-13 - CLAUDE.md - claim-admissibility promoted, and the half of its row that is still owed

Authorised by: Each denied category is refused by a marker the document carries, and the domain is never one of them
Was:
> | `claim-admissibility` | from 6.3 | A poisoned paragraph, an unsourced claim, and each inadmissible document class are refused, and nothing resting on them is written |
Now: the row runs on every CI run and its Asserts cell states what is asserted: each of the four denied categories, the missing publish date and the unretrievable text reached by a document the fixture holds, the real documents it holds admitted, a document failing two gates refused by the kind rather than by the date, and every refusal kept as a row with its reason and no body. The poisoned paragraph and the unsourced claim are named as the row's other half, asserted from 6.4.
Why: the roster's Runs column admits three values and none of them says that half a row's property is built, so the split is stated in the cell where a reader will see it. Leaving the row at "from 6.3" was not available: a checkpoint row has to name a checkpoint the record does not show as landed, so the row fails `coverage-reported` the moment 6.3's entry is written. Stating the whole cell as running would be the wider defect, which this corpus has already had once when a roster row read wider than its check for two phases.

### 2026-09-13 - ARCHITECTURE.html - the admissibility row states the order it judges in and the numbers it judges by

Authorised by: Each denied category is refused by a marker the document carries, and the domain is never one of them
Was:
> a fetched document is stored only if it carries a publish date inside the window the pass asked for and is not on the denied-category list: algorithmic or AI-generated price forecasts, broker and platform marketing pages, AI-written summaries, and quote or hub pages with no article (see: A stored source is not automatically an admissible one)
Now: the same sentence, followed by the order the gates are judged in, being the kind first and the date second with the measurement that decided it, and the three numbers the test uses: a paragraph of at least 3 sentences and at least 40 words is what makes a page an article, and 2 invitations to open an account mark a page that exists to open one.
Why: the row is a claim about the code and it stated the rules without stating either the order or the numbers, so a document could be refused for two different reasons and the row would read as satisfied by whichever fired. The order is not a detail: three of the five refusable pages the 6.3 measurement read carried no publish date at all, so judging the date first would report almost nothing about the kinds. The numbers are stated here rather than in the record because a number in a record is not pinned to anything, and `claim-admissibility` reads these off the row against the constants the test uses.

### 2026-09-13 - SCHEMA.md - the two source document columns that admit null, and the url that is never a request

Corrects: the source document table's own column notes contradicted the paragraph beneath them. `published_on` read "date; a document with none is not stored" while the paragraph requires that a document failing admissibility be kept as a row with its refusal reason, and having no publish date is one of the things admissibility refuses a document for. So the file declared a refusal it also declared unstorable. Found at 6.3 while writing the migration against the file.
Was:
> | `id` | TEXT | |
> | `url` | TEXT | |
> | `title` | TEXT | |
> | `published_on` | TEXT | date; a document with none is not stored |
> | `fetched_at` | TEXT | UTC instant |
> | `body` | TEXT | the full text, not a snippet |
Now: `published_on` is a date that is null where the document carries none, and a row with none is a refusal carrying that reason. `body` is null on a refusal. `id` states its derivation, being the document's own url hashed so a second fetch of the same address conflicts rather than writing a second row. `url` states that it is the document's own address and never a provider request url. Two paragraphs follow the table: one saying that neither null is an absence of data and that an admitted row carries both, which the check asserts in both directions, and one applying the hard rule about request urls to this table and stating that the intake refuses a url carrying a credential marker rather than storing it.
Why: the contradiction had only one resolution that keeps both halves of the file. A not-null date column makes the refusal for a missing date unrecordable, which makes that refusal invisible, which is the one thing the paragraph about keeping refusals exists to prevent. The other direction, dropping the requirement to keep such a row, would leave the most common refusal in the measured set silent: three of the five refusable pages the 6.3 measurement read carried no publish date at all.

### 2026-09-13 - BUILD_PLAN.md - two scope obligations filed, and the momentum panel put to 7.0 to decide
Corrects: two scope decisions the operator took during 6.1 existed only in the conversation, which is a hole in the record: anything issued in conversation that will later be cited has to land in the repo when it is issued. The momentum panel's own gap is the third and it is not an obligation, because nothing produces evidence for it.
Was:
> | **The computed fundamental panel, and whether a fundamental state may fire a reason or gate a tranche** | 6.1 | 7.0 | 7.0 is the planning pass that reads what phase 6 produced before phase 7 builds on it, and what it needs is in hand by then rather than produced by it: 6.1 stores twelve filings a name, and 6.4 onward writes the first researched reports to read a panel against
>
> Decides what a computed panel over the twelve stored filings states, and the two questions that rest on it
Now: the fundamental row states the four readings it decides, being the computed trajectory, the guide record, earnings quality and the valuation position over the twelve stored quarters, each with the range it is placed in and the count of quarters behind it, and the three questions of a panel, a seventh reason on a state transition and a computed tranche precondition. A second row narrows what the volume profile is allowed to claim to a price region at the resolution of a day's range, with a treatment for a session whose range exceeds a multiple of the typical daily move and a count of the sessions that sets aside. 7.0's text cites both back and states that it decides what the momentum panel is for rather than carrying it further.
Why: the fundamental row's name is kept as 6.1 filed it, because 6.1's own record cites that name and a record is corrected by a new entry rather than edited, so a rename would leave the citation resolving to nothing. That is the precedent the bulk-payload row and the route row both set. The volume profile row is new: a published caution holds that a daily volume profile is not a volume profile, that caution is about a feature scored against a forward return, and narrowing what the claim says is what makes it not apply, so the narrowing belongs in the corpus before anything scores it. The momentum panel is decided rather than carried because the panel has been drawn since 3.5 and the open question is what the corpus intends by it, not what a measurement would show.

### 2026-09-12 - RUNBOOK.md - the archive's contact, which is a setting and not a key
Authorised by: The archive declares a contact in its user agent, and a blank one refuses at startup
Was:
> | Provider | Key | Which projects need it |
> |---|---|---|
> | EODHD | `EquityBrief:Providers:Eodhd:ApiKey` | `EquityBrief.Worker` |
Now: the table carries a second row for `EquityBrief:Providers:SecEdgar:Contact`, and a paragraph beneath says why a contact sits in a table of keys, what to put there and why a dedicated address rather than a personal mailbox.
Why: the archive needs no key and refuses a request that names no user agent, so the setting is what a request declares about this installation rather than what authorises it. This section exists because a file written by hand needs its names written down, and a setting absent from it is one the first hand-written file will not carry.

### 2026-09-12 - BUILD_PLAN.md - the probe obligation discharged
Corrects: nothing was wrong with the row; it is discharged because 6.1 did what it named. An open row whose due point the record shows as landed fails `obligation-reconciles`, which is what caught the moment the 6.1 entry was written and before the verifying run, since the record is what the reconciliation reads.
Was:
> | **Bulk fundamentals endpoint probed on the operator's key** | authored with the architecture | 6.1 | one live call on the key the credential path has held since 2.1. Nothing waits on evidence, and 6.1's done condition records the probe either way |
Now: the row reads discharged and its cell carries what the probe settled, being the six things the endpoint's name and SCHEMA's own column note would each have got wrong, and the weight measured against the account's own counter rather than read from documentation.
Why: an obligation is discharged where the evidence is, and the evidence here is what capturing before parsing produced. The prior text is kept in the cell after the words "What it read before", which is the form the other discharged rows use.

### 2026-09-12 - ARCHITECTURE.html - the stored filings expectation, and the fact strip owed whole
Authorised by: Twelve filings are stored and five are shown
Was:
> the expected outputs of section 19.1 without a row for the filings one fetch stores
Now: a row reading "stored filings | the twelve filings one fetch stores of the fourteen a capture holds, which parts sit on the newest filing alone, and which the provider files for nobody | the ruling that twelve filings are stored and five are shown, with the margin divided by the test rather than stated in the file"
Why: the table already carried a `fundamentals` row and that row is an input, being the captured payload. The expectation is an expected output and needed a row of its own rather than a second row under one name, because a reader keyed on a row's name answers about both. An expectation the corpus does not list is the fault 5.6 left and the phase 5 sign-off found.

### 2026-09-12 - ARCHITECTURE.html, SCHEMA.md, BUILD_PLAN.md - the fundamentals store, its window, and the two rows that named it
Authorised by: Twelve filings are stored and five are shown
Was:
> | Fundamentals fetcher | compute | on demand, per name | company financials feed, filings archive, fundamentals | fundamentals | fetches quarters, balance sheet, segment table and guidance when the stored copy predates the name's latest filing |
>
> | Facts assembler | compute | nightly, per name | bar store, indicators, swings, volume profile, levels, ladders, moves, calendar | facts | writes tonight's facts file with every number and its source, and its hash. The listings and the fundamentals join its reads at 5.4 and 6.1, which are the checkpoints that create those stores |
>
> Primary key: `ticker`, `filing_date`.
>
> Kept forever, never updated. Providers restate, and keeping the filing date is what makes it possible to know later what was known at the time.
Now: the fetcher's row names the two parts this provider files and says the archive joins its reads at 6.2, since the probe settled that the endpoint carries no segment table and no management guidance for any name; the assembler's row carries the fundamentals read it has announced since the architecture was written, and says the listings went to the change detector rather than here; the matrix gives the assembler an R in the Fundamentals column; SCHEMA states that one fetch writes the twelve most recent filings, which is a write window and not a retention one, and what the payload and the source column hold; and BUILD_PLAN gains the obligation the ruling files, with 7.0's text citing it back.
Why: the catalogue named both a feed and two parts that do not exist yet, and a row listing a feed no component declares fails the reconciliation in the direction nobody reads. The assembler's sentence was half wrong from 5.4: the listings read went to the change detector, because the retention that needed it is an update to a row the assembler inserts. And the store's own note said what is kept without saying what one fetch writes, which is the figure a later session reading section 4's five quarters would have narrowed.

### 2026-09-12 - CLAUDE.md, BUILD_PLAN.md, RUNBOOK.md, source-lists.json - the five obligations due at 6.0, and the readers and routes they turn on
Corrects: four of the five were rulings or tests the fourth phase 5 sign-off review left carried, and the fifth was the source lists' own review, owed since 1.7. Two check readers passed forms they could not see: `clock-usage` admitted a bare `Invariant(` wherever it appeared, which passes a literal on the name of the thing before it, and `price-storage-form` read neither a signed nor a numeric-literal operand, nor the framework's capitalised type names, nor the decimal type's own `ToDouble`. No test in the suite hosted a route, so every route body in the API's own file was unreached and each screen's PASS sat at the helper the route calls. The run page ordered the newest night by an instant a replay stamps, so a night replayed for an older session could take over the page. A refusal before the first step was recorded as a failure under the migrate stage and read on the page as a migration that failed. And numbers were formatted in the machine's culture into stored text in two places, which `clock-usage` never saw because it is keyed on date formats.
Was:
> the two roster rows without those forms; the five obligation rows reading 6.0 with their evidence cells open; `"reviewedOn": "2026-09-08"` and a review date of 2027-03-08; and the morning table without a row for a night that is missing from the page altogether
Now: both roster rows state what their readers now reach and, for the one form that is stated rather than closed, that the other reader carries it with both directions asserted; the five rows read discharged with their evidence; the source lists carry the 6.0 review, which records what the 1.7 measurement can and cannot settle and moves the review date; and the runbook's morning table says where to read a refusal the store could not hold.
Why: a reader blind to a form passes every use of it, so the only broken checks that survive are the ones that under-report. The routes are hosted in process against a throwaway store, which is 6.0's ruling on how the suite reaches a route, and the tonight route is asserted in both directions because the sign-off's own demonstration was to point it at the day before and leave the suite green. One obligation's rename was tried and reverted, since the record that cites the name is corrected by a new entry rather than edited.

### 2026-09-12 - CLAUDE.md, ARCHITECTURE.html, DECISIONS.md, BUILD_PLAN.md - the gap stop, which the corpus held and no stage ran
Corrects: section 18's gap row promises that the system computes nothing across a gap for that name and that the name's level and plan sections say not computed, and a decision has said since 1.5 that every computation over a name whose stored series has a gap stops and reports the gap's date. No stage did any of it. Two of 503 members are absent from an ordinary day's bulk file, so their series carry interior holes that the indicators, the swings, the profile, the levels and the moves all counted across as though the sessions either side were adjacent, and the listing's earnings reason reads the calendar alone, so a gapped name with a print inside the horizon reached tonight's list carrying an empty plan and nothing saying why. Found by the phase 5 sign-off, which observed that only the backfill consults the calendar at all, and carried to 6.0 as a ruling.
Was:
> | `gap-refusal` | every CI run | A series arriving with an interior session missing is refused, ... and one series alone reports that it cannot be checked rather than that it is clean |
>
> The state is derived on read from the stored sessions against the calendar rather than kept in a column, so nothing has to be brought back into step with the bars after a refetch fills the hole.
Now: the roster row covers the computation stop as well as the fetch refusal, stating that the five computed tables withhold every row while the ladder and the listing still carry theirs, asserted per table because the two failures are opposite; the decision states the two readings of the calendar and which behaviour uses which, and says that a series the closure table cannot place is counted as unchecked rather than reported clean; section 19.1 gains a gap stop expectation row; and the obligation row reads discharged with its evidence.
Why: section 18's row and the decision were right and the code was not, which is the one class the stopping rules say cannot be carried rather than written down. The roster row is widened with the check, because a check asserting more than its row says is a property nobody wrote down.

### 2026-09-12 - ARCHITECTURE.html, CLAUDE.md - the tables phase 6's claims are read from, filled where they were short
Corrects: the overnight queue is named in section 14's step 17, in section 17, in section 18, in section 20 and in figure 12.2 and had no catalogue row and no matrix row, so under the matrix's own blank-cell rule it held no permission to write anything; the theme research runner's reads named no search tool while three limits rows specify one; section 15.9 carried no region for research missing, stale or paused, each of which section 4's key, figure 12.1, section 15.12 and three section 18 rows describe; section 15.10 carried no region naming the queue while section 18 requires the run page to state that it did not run; both screens' read lists omitted a store their own regions draw; section 19.1 named no expectation for a research record, a theme record, the stored documents or the refused ones, and its inadmissible-document row named three kinds where section 17's assertion column asks for six; section 18 carried no row for a snippet, an off-list site, a publish date outside the window, the search tool, the local model or a failed theme refresh; section 17's spend cap and overnight queue rows carried no figure at all; figure 12.2 pointed at figures in section 17 that do not exist; the lane table assigned no lane to two of the sections figure 12.1 says a pass writes; and `claim-admissibility` was rostered from a checkpoint that builds no admissibility. Found at 6.0 reading the tables phase 6's claims come from against the phase they come from.
Was:
> the two screens' read lists and region tables without those rows; section 18 without the six; section 19.1 without the four and with an inadmissible-document row naming three kinds; "a configured amount of money per day and per month"; "stopping after a configured number of hours"; "the figures in section 17 are an upper bound rather than an estimate"; the lane table's seven rows; `| \`claim-admissibility\` | from 6.1 |`
Now: the queue has a catalogue row and a matrix row landing ahead of the component; the theme runner reads the search tool; three research states and the queue's region are drawn rows with their own due points; both read lists name the store they draw; section 19.1 carries four expectation rows and an inadmissible row naming six documents; section 18 carries six new rows; the spend cap reads 10 a day and 50 a month, both marked proposed with the arithmetic between them; the queue's limit reads 1 hour until 6.10 measures a pass; figure 12.2's consequence is stated against the cap; the lane table assigns the industry cycle and the dated calendar items; and the roster row reads from 6.3.
Why: a component in code with no row is refused in that direction, and the rows have to land before the component for the reason the change detector's did at 5.0. A state with no region is unexaminable, which is the fault the fifth phase 5 sign-off review found in a different place. And a limits row with no figure is a claim with nothing to pin.

### 2026-09-12 - BUILD_PLAN.md - phase 6 resplit to eleven build checkpoints, and every claim it moves re-pointed
Authorised by: A due point names a checkpoint that exists wherever its phase has been detailed
Was:
> ### 6.1 The fundamentals fetcher and the numbers section ... ### 6.9 Phase 6 report
>
> the eight build checkpoints and their done conditions, in which the filings archive sat inside the source store's checkpoint and the spend cap inside the research runner's, and in which the staleness judge sat seventh
Now: 6.1 through 6.11, with the filings archive and the spend cap as checkpoints of their own and the staleness judge fifth; 6.0 carries the resplit's reason, the re-point map and list A per checkpoint; 5.8's heading moves above the rule that closes phase 5, where it belonged and did not sit.
Why: the phase carries seven new components, four new stores and five new outward surfaces, and two of the eight checkpoints each carried a new provider, a new store and a new component at once. Condition 9 asks a checkpoint to name the property its mutation chose, and a checkpoint carrying three new things cannot say which of the three a survivor belongs to. The spend cap is split for a second reason: a gate built in the same commit as the thing it gates has no run in which the gate was absent.

### 2026-09-12 - BUILD_PLAN.md - the transaction question closed by ruling that no check is written
Authorised by: A transaction taken for speed is not asserted, and a scan that would need twelve exemptions is not written
Was:
> | **Every stage that writes in a loop opens a transaction, asserted rather than read** | 5.7 sign-off | 6.0 | open, with half of it done: ...
Now: the same row reading "6.0, discharged", with the ruling in its evidence cell and the text it carried before kept after it.
Why: the row asked 6.0 to decide whether a source scan over the loop shape earns its false positives, and it does not. All sixteen writers loop, so one keyed on the loop reports every one and one keyed on the transaction reports none, and the thing that separates a stage needing one is the row count it writes, which appears in no file's text. A scan reporting twelve findings that are not defects is made green by exempting twelve by name.

### 2026-09-12 - ARCHITECTURE.html - the night's zero-model-call rule carved for the overnight queue
Authorised by: The night's zero-model-call rule bounds the arithmetic, and the overnight queue is carved out of it by name
Was:
> Every night, all 500 names, no model and no per-name network calls
>
> | Model calls in the nightly run | 0 (see: The nightly run is arithmetic only) | the whole design rests on the nightly half being free | run log |
>
> No model is called and no per-name network request is made, and a feed that answers in pages counts every page it fetched,
Now: figure 5.1's band names the arithmetic, the limits row reads 0 in the arithmetic with step 17 carved out by name and its reason states why the carve keeps the rule true, and section 14's note scopes both clauses to steps 1 to 16 and names the carve.
Why: section 14's own list runs to seventeen steps and step 17 calls the local model for every listed name whose research is missing or stale. Three places said no model is called and the fourth was the step, which is the shape 1.2 carved for the backfill rather than loosening a rule to fit its own exception.

### 2026-09-12 - ARCHITECTURE.html, SCHEMA.md - admissibility placed where the fetch lands, and the pending state named as a status
Corrects: the claim checker's catalogue row said it refuses to store a document that fails the admissibility test while its matrix row gives it a read of the sources column and no write, and the research runner had the write and no read, so the component that decided could not store the verdict and the component that stored was not the one deciding. In the same two rows the catalogue named a research store pending and a research store, which is two stores in a sentence against one column here, so the gate the matrix key argues for was the one property the matrix could not carry. Found at 6.0 reading the two tables against each other before the components exist.
Was:
> <td>facts, fundamentals, filings archive, news feed, theme store, research model</td><td>research store (pending), source documents</td><td>writes the narrative sections and keeps every document it read</td>
>
> <td>research store (pending), facts, source documents</td><td>research store</td><td>rejects a number absent from the facts file and a claim whose source is not stored, and refuses to store a document that fails the admissibility test; one retry, then the section is left out rather than guessed</td>
>
> Only the research components write research, and the claim checker sits between the runner and what is served.
Now: the runner reads and writes the sources column and applies the test as it fetches; the checker reads the verdict and does not apply the test; both rows name the research store once; the matrix key states that the gate is the status on the row and the split of one table's operations between two owners; and SCHEMA's source-document section says who applies the test and notes that its own ownership row already said so.
Why: the test is applied per document after the fetch, and the runners are what fetch. A pending section is a status the row already carries, so a second store column would be a second statement of one fact and the one that goes stale.

### 2026-09-12 - ARCHITECTURE.html - three statements that had gone stale
Corrects: section 6.3 called every component in the compute layer a pure function of stored data and counted eleven of them, against a catalogue carrying nineteen of which seven fetch; section 7's verification harness row listed the sections it reports on and the list was short by the figures, section 19 and section 20; and section 22 cited a decision by the first half of its name. None of the three is read by a check, which is why all three survived. Found at 6.0 reading the sections phase 6 builds against.
Was:
> Eleven components, all pure functions of stored data. Given the same bars they produce the same facts file byte for byte, which is what makes the fixture diff possible.
>
> PASS, FAIL or UNEXAMINED for every claim in sections 7, 14, 15, 16, 17 and 18
>
> See the decision named Theme material comes from a search tool, whose results are stored like any other document.
Now: the layer is described as the stages that compute and the fetchers that feed them with the purity claim held to the first group and the count dropped, the harness row states what it covers by kind rather than by a list of section numbers, and the citation gives the whole name.
Why: a count restated beside the list it enumerates is a second statement whose only job is to go stale, and this one had gone stale twice over. A purity claim that covers the fetchers is false about six components that shipped in phase 1.

### 2026-09-12 - BUILD_PLAN.md - the local model and the lane's shape settled
Authorised by: The local lane is a configured list of section names, and the prose writer writes whatever the list holds
Was:
> | Which local model, and the section-to-lane assignment | The lane split is configuration by decision, and nothing says what the configuration's shape is | 6.0 |
Now: the same row, marked settled, naming the three decisions the hole took rather than one, and naming the short-version contradiction as closed by the second of them.
Why: the hole was one question and the answers fail differently. A wrong model identifier is a setting, a lane decided section by section in code is a setting that is not one, and a provider chosen by a fallback is a section whose author cannot be read off the record.

### 2026-09-12 - BUILD_PLAN.md - the research recording's format settled
Authorised by: A research pass is recorded per section call, keyed on the canonicalised request
Was:
> | How a research pass is recorded for a fixture | A pass must be reproducible without a network, which needs a recorded endpoint with a defined record format | 6.0 |
Now: the same row, marked settled, stating the record as a captured request and response pair per section call, keyed on a hash of the canonicalised request rather than on call order.
Why: section 19.2 asked for a recording model endpoint and said nothing about its shape, and the shape is what decides whether a replay is a check or a transcript of the run that made it. A sequence-keyed recording passes on that one run and fails on every other.

### 2026-09-11 - CLAUDE.md, BUILD_PLAN.md - two readers widened, and the closure table's end given a reminder
Corrects: `clock-usage` did not read a raw interpolated literal and passed a literal handed to `string.Format` with a provider, which formats the literal before the provider is seen; `price-storage-form` did not read a nullable cast or a `Convert` call, and one of the second shipped in no stated set; and the exchange closure table ends 2027-12-31 with nothing saying so before the first weekday past it. Found by the phase 5 sign-off reviewer.
Was:
> An interpolation hole carrying a date format is a third form and is read off its literal, passing only where the literal is handed to the invariant culture; a hole with no format over a date value carries nothing a text reader can key on and is outside what this check reaches.
>
> and every explicit cast to double or to decimal in the shipped source is one of a stated set of sites,
>
> This checkpoint builds the surface five operating obligations are read on (owes: The six reason thresholds calibrated from the nights they fired on), (owes: The three reason records that need resolved setups), (owes: The event setups' triggers calibrated from resolved setups), (owes: The night's instant moved later when a night finds the day's file not yet posted) and (owes: The nightly wall clock at index size, measured from nights that ran on the schedule). ... and, in the stale-and-failed region, a night that stopped at the fetch because the day's file was not yet posted.
Now: the `clock-usage` row reads raw literals, refuses a provider given to `string.Format` after the literal, and adds the composite format as a fourth form; the `price-storage-form` row includes the nullable casts and the two `Convert` calls; 5.6 builds the surface six operating obligations are read on, the sixth being the closure table's extension, read off the closing stage's line; and a carried obligation row states it.
Why: a reader that cannot see a form passes every use of it, and a table that refuses past its end needs its end said before the refusal, not by it.

### 2026-09-11 - ARCHITECTURE.html - a suspect name is retried every night
Corrects: a name whose refetch failed was marked suspect and nothing read the mark, so its stored history stayed unadjusted for an action the provider had applied until another action happened to land on it. Found by the phase 5 sign-off reviewer.
Was:
> <td>splits and dividends feed, historical price feed, membership, bar store</td><td>bar store, series state</td><td>refetches a name's full year when an action changes its adjusted prices, because stored history silently diverges otherwise; the refetch replaces the year inside one transaction, and a name whose own check failed is marked suspect rather than passing</td>
>
> <td>the corporate action check refetches the year; if the check itself fails the name is marked suspect (see: Adjusted history is re-fetched after a corporate action)</td>
>
> the read and write matrix's corporate action checker row carried W alone under series state
Now: the checker reads series state as well as writing it, the matrix cell reads R W, and a suspect name is refetched again every night until one refetch succeeds, in the catalogue row and in section 18's row.
Why: a mark nobody reads is a record of a failure and not a response to one, and the action that made the name suspect is still in its stored history unadjusted.

### 2026-09-11 - ARCHITECTURE.html - the run page opens on the night that ran, and a stage's detail is a cell
Corrects: the run page opened on the newest night the listings held and kept that night's rows alone, so a night that stopped before its list was absent from the page a person opens, and the claim that the region shows the stage a night stopped on passed through a read with the date supplied. The operational header carried each stage's own account of itself as a hover title. Found by the phase 5 sign-off reviewer, who stopped a night at the fetch and read the default page.
Was:
> <p><b>Route:</b> <code>#/run/&lt;date&gt;</code>.</p>
>
> <tr><td>Operational header</td><td>what ran, the instant each stage started and how long it took, model calls, network requests, and spend</td></tr>
Now: with no date the route opens on the newest night the run log carries a stage for, whether or not it wrote a list, and not on a night with no session; the operational header adds what each stage said about itself, in a cell.
Why: the page is where a stopped night is read the next morning, and a page that opens on the last night that listed shows a clean evening for exactly the nights that were not.

### 2026-09-11 - SCHEMA.md, ARCHITECTURE.html - a re-run replaces a night's facts file that differs
Authorised by: A re-run replaces a night's facts file where the store now computes a different one
Was:
> | `facts` | FactsAssembler | ChangeDetector | none |
>
> Declared column sets, stated per operation because that is the grain the rule is written at: FactsAssembler inserts `payload` and `payload_hash`; ChangeDetector updates `material_changes`, and updates `payload` to empty under the retention below. No column is written by two components in one operation, which is what permits the split.
>
> The run is idempotent in what it records about the market: running it twice on one night produces the same bars, the same membership spans and the same computed values.
Now: the assembler owns the delete on `facts`, used on one row, tonight's file where it differs from the one the store now computes; the column-set paragraph says so and says what happens to the change list of a replaced and of an unchanged row; section 14's note says a second run's values are what the store holds, the facts file included.
Why: the first file written for 2026-09-10 came from band sets a refetch had doubled, the clean files of the run that completed were dropped on the conflict, and the run log counted them written. Found by the phase 5 sign-off reviewer and by the builder reading the recovered store.

### 2026-09-11 - ARCHITECTURE.html, SCHEMA.md - an announced index change takes effect on its date
Authorised by: An announced index change takes effect on its effective date, and a joining name is stored from the announcement
Was:
> <li>Fetch the day's bulk bar file, one request, and store the bars for current members, first fetching in bulk, one request each, any session the store is missing since the last night that ran (see: A session the night finds missing is fetched in bulk before tonight's).</li>
>
> The same backfill runs for a single name on the night it joins the index.
>
> | `left` | TEXT | date, null while a member |
>
> Whether it is a member tonight is `left IS NULL`, which the row answers exactly.
Now: the fetch stores every name that has not left by the session, an announced joiner included; the backfill runs on the first night the feed carries a join; `left` is the date a name stops being a member and is set before it does; a member tonight is read against tonight's session; section 18 gains a row for a change announced before it takes effect.
Why: the membership feed carried the rebalance of 2026-09-21 by the 10th, and reading `left IS NULL` as membership dropped three names still in the index and listed four not yet in it. Found by the phase 5 sign-off reviewer in the operator's store.

### 2026-09-11 - ARCHITECTURE.html - the missed session is read off the bulk rows
Corrects: the catch-up compared tonight's session with the newest bar the store held, and a joiner's backfill writes through tonight, so one joiner backfilled on the night after a night that did not run hid the missed session from the whole index. Found by the phase 5 sign-off reviewer, who reproduced it.
Was:
> each session the exchange traded between the newest one the store holds and tonight's is fetched in bulk,
Now: between the newest one a night's bulk file stored and tonight's, with the reason stated in the row's last cell.
Why: the backfill and the refetch write one name's year through tonight, and only the bulk rows say where the last night that ran left the index.

### 2026-09-11 - RUNBOOK.md - the task fires on weekdays
Authorised by: A night on a day the exchange did not trade fetches nothing and exits clean
Was:
> $trigger = New-ScheduledTaskTrigger -Daily -At 6pm
>
> <string>[ "$(date -u +%H%M)" = "2330" ] &amp;&amp; exec "$HOME/EquityBrief/tools/nightly"</string>
Now: a weekly trigger on Monday to Friday at the same instant, the command that moves a daily task in place, and the macOS job checking the UTC weekday as well as the hour.
Why: a daily task ran every weekend and each of those nights was refused at the fetch and exited 1. The night now exits clean on a day with no session, so the weekday trigger saves the weekend's rows rather than preventing a failure.

### 2026-09-11 - SCHEMA.md - material_changes null means the comparison could not be made
Corrects: the column was a JSON list and nothing said what a name whose files cannot be compared carries. Until the phase 5 sign-off such a name stopped the change detector for every name, so the case never reached the column. Found by the phase 5 sign-off's builder, when NVDA's facts file of 2026-09-10 named a fact twice and stopped that night at step 13.
Was:
> | `material_changes` | TEXT | ChangeDetector. JSON list |
Now: a JSON list, empty where there was nothing to compare against, and null where the comparison could not be made, being a facts file that is empty or names a fact twice.
Why: an empty list reads as nothing changed and a null as unknown, and only one of them is what a detector that could not compare knows.

### 2026-09-11 - SCHEMA.md - an as-of writer replaces its set for that as-of whole
Corrects: SCHEMA said the computed tables' writers drop whole as-of sets below the retention boundary and said nothing about the set being written. `level` and `volume_profile` upserted on each band's edge, so the by-hand night of 2026-09-10, run after the scheduled one, left NVDA with two immediate support bands and MOS with 39 profile bands for one as-of, and the change detector stopped the night on NVDA's repeated facts. Found by the phase 5 sign-off's builder, reading that night's run log.
Was:
> A drop removes whole sessions or whole as-of sets below that date and never a row from inside a set that stands, which is the distinction the `bar` note draws.
Now: the same sentence, and one stating that the two writers keyed on an as-of date replace the name's set for the as-of they write whole, inside the transaction that writes it.
Why: a night writes a whole set, so a whole set is what a second run for that night replaces; an upsert keyed on a moving edge keeps the first set beside the second.

### 2026-09-11 - ARCHITECTURE.html - a missed session is fetched in bulk before tonight's
Authorised by: A session the night finds missing is fetched in bulk before tonight's
Was:
> <li>Fetch the day's bulk bar file, one request, and store the bars for current members.</li>
Now: the step fetches first, in bulk and one request each, any session the store is missing since the last night that ran; section 18 gains a row for a session the night finds missing.
Why: a night that did not run left every name short the same session, which the observed calendar cannot see, and 5.7 had read the provider as unable to serve an older session, so nothing could fill it. One probe showed it can.

### 2026-09-11 - ARCHITECTURE.html, BUILD_PLAN.md, RUNBOOK.md - the schedule moves on a refusal, and the posting-hour obligation is retired
Authorised by: The night runs at a fixed UTC instant, moved only when a night finds the day's file not yet posted
Was:
> The schedule is a UTC instant set after the provider posts the day's bulk file, registered with whatever scheduler the machine has, ...
>
> | **The provider's posting hour for the day's bulk file, measured from live fetches** | 2.0 | operating | 5 nights fetched under the schedule, read on the run page's operational header, which 5.6 builds. ...
>
> **The instant is 23:30 UTC, provisionally.** ... It is provisional because the posting hour has not been measured, ... Move the instant earlier once five nights show the file was already there.
>
> **So both become operating obligations, and 5.7 keeps the half it produces.** The posting hour's trigger is five nights fetched under the schedule, ...
Now: the instant stands while nights succeed and moves later on a night refused because the day's file was not yet posted; the posting-hour row is discharged at 5.7 as retired, a new operating row carries the refusal trigger and 5.6 cites it; 2.1's paragraph and the runbook say why more scheduled nights settle nothing.
Why: a scheduled fetch can only bound the posting hour from above, so the five-night trigger would fire without answering the question it named. A refusal is the only evidence that moves the instant, and with the bulk catch-up a refused night's session is fetched the next night rather than lost.

### 2026-09-11 - CLAUDE.md - record-append-only holds DECISIONS.md's names as well
Corrects: 5.0 superseded "News arrives in one dated feed request and is attributed to names locally" and deleted it rather than moving it to "Previously decided", and no check could notice: `no-superseded-citation` asks whether a citation resolves to a superseded entry, which a deleted one never does. Found by the phase 5 sign-off comparing 5.0's record, which says it superseded the entry, with "Previously decided", which did not hold it.
Was:
> ... so the guard's window is visible rather than the removal sitting outside it |
Now: the same row, with every decision name ever present in `DECISIONS.md` held as a high-water mark read from the history.
Why: `DECISIONS.md` is a record, and a record's names are the unit a correction keys on. The entry is restored, so the check starts from the history as it stands with no exemption.

### 2026-09-11 - CLAUDE.md - clock-usage and price-storage-form rows state the halves the first repair missed
Corrects: the phase 5 sign-off's first repair widened both checks and both still missed the shape they were widened for. `clock-usage` read `ToString` with no provider and not an interpolation hole carrying a date format, which is how every provider request URL in the tree rendered its dates. `price-storage-form` read crossing signatures, and putting any of the three inline casts it was widened for back into its method left the whole suite green. Found by the phase 5 sign-off's independent review of that repair, the second by a mutation that survived.
Was:
> ... the formatting half is keyed on the call passing no provider rather than on a name, since the provider is held under an alias in the renderer. Comments are stripped first, ...
>
> ... is a named crossing helper, with the set of them stated rather than counted. The two halves fail apart: ...
Now: the clock-usage row names the interpolation form and states the unformatted hole as outside its reach; the price-storage-form row adds the stated set of cast sites.
Why: a check asserting more than its roster row says is a property nobody wrote down, and one asserting less is the survivorship the roster exists to catch.

### 2026-09-11 - BUILD_PLAN.md - the transaction obligation is open, not discharged
Corrects: the row read "6.0, discharged" while its own text and 6.0's paragraph both said half of it is owed at 6.0, so the table counted 43 discharged and 4 open where the truth was 42 and 5. `obligation-reconciles` exempts a discharged row by design, since work done ahead of its checkpoint is legitimate, so only the label could say it. Found by the phase 5 sign-off.
Was:
> | **Every stage that writes in a loop opens a transaction, asserted rather than read** | 5.7 sign-off | 6.0, discharged | the phase 5 sign-off filed it and discharged the half that was assertable. ...
Now: the due cell reads "6.0" and the note says it is open with half of it done, and why it read otherwise.
Why: a row that reads as closed is chased by nobody, which is the fault the obligation table exists to prevent.

### 2026-09-11 - BUILD_PLAN.md, ARCHITECTURE.html - the bulk feed's unnamed refusal, diagnosed and discharged as a correction to 2.1
Corrects: the obligation row and 6.0's paragraph read the refusal of 2026-09-04 and 2026-09-08 as the provider serving something other than prices for an older session. One probe on the operator's key refuted it: the older file is prices, and six of its 50,249 rows carry a fractional volume the reader refused the whole file for with the framework's default message. Found by the phase 5 sign-off.
Was:
> | **A bulk payload that is not a price payload refused by name** | 5.7 | 6.0 | evidence in hand: sessions 2026-09-04 and 2026-09-08 both fail the fetch step with "One of the identified items was in an invalid format" while 2026-09-09 fetches cleanly, so the provider serves the bulk file for the most recent session and something else for older ones. ...
>
> Rules on the bulk feed's unnamed refusal in the same pass (owes: A bulk payload that is not a price payload refused by name), whose evidence is also in hand: the failure table has a row for a payload that is for another session and one for a payload holding none of the index, and none for a payload that is not prices at all, which is what the provider returns for a session that is not the most recent. It sits at a planning pass because the repair is a section 18 row and a named refusal rather than a component.
Now: the row reads "2.1, discharged" with the diagnosis that replaced the reading; 6.0's sentence is removed and 2.1's section carries the citation, because 2.1 set the reader's row policy on the live file; section 18 gains a row for a row the reader cannot read.
Why: any evening whose file carries one such row stopped the night at the fetch, and two of the first four days fetched at index size did, so a planning pass was the wrong place for it and the checkpoint whose rule refused the file is the one it corrects.

### 2026-09-11 - ARCHITECTURE.html - a night that stops is recorded, in 15.10 and in the Night close row
Corrects: section 18's unavailable-feed row promises the step and the reason "on the run log", and nothing wrote them there. A failed step's name went to stderr, which a scheduled task discards, so the run page drew the stages before it as clean and the stale-and-failed region as empty. The phase 5 sign-off's repair had just removed "what failed in which component" from the operational header on the reading that this region delivers it, and for a step that throws it could not. Found by the phase 5 sign-off reading the first scheduled night, which the scheduler recorded as exiting 1.
Was:
> <tr><td>Stale and failed</td><td>names carrying yesterday's bars, sections that fell back, documents refused by admissibility with the category that refused each</td></tr>
>
> ... stale names and duration, every one counted off the store rather than reported by the stage that wrote it</td></tr>
Now: the region names the stage a night stopped on with its outcome and the reason, and the Night close row says it records that stop as the stage's own row.
Why: the row the region is read against has to state what the region draws, or the sibling that stopped stating it leaves the promise nowhere. `RUNBOOK.md` already told the operator the run page says what failed in which component, and it now does.

### 2026-09-11 - BUILD_PLAN.md - 5.4's done condition names a night whose store holds the night before
Corrects: 5.4 read "the listing row count equals the index size on every completed night", which a night that cannot complete satisfies without anything being asserted. The shortlist builder refused every evening after a store's first, so the first scheduled night, 2026-09-10, stopped at step 12 with no listing written. Found by the phase 5 sign-off reading that night's run log and reproducing the refusal on `main` against a copy of the store.
Was:
> **Done when** the listing row count equals the index size on every completed night, the fired count in the header matches the reasons,
Now: "on every night, asserted on a night replayed over a store that already holds the night before it rather than only on a store's first", with a paragraph beneath the condition saying it was amended and why.
Why: every night the suite ran was a store's first or a re-run of one session, and the replay ran the facts before the listings where the night runs them after, so the population that shows the defect was one nothing held. The condition names that population now.

### 2026-09-10 - BUILD_PLAN.md - 5.3's done condition amended to what the fixture asserts
Corrects: 5.3 read "the facts file matches the fixture byte for byte" and the fixture refuses to be that. `facts.json` states in its own words that nothing in it is frozen from a run and that the payload hash is not stated, because a hash of a payload is a function of that payload and stating it would be a regression baseline wearing the clothes of an expectation. So the condition asked for exactly the thing done condition 7 warns against, the checkpoint built the stronger form instead, explained why in its Measured block, and left the condition saying something else with no amendment recorded. Found by the phase 5 sign-off.
Was:
> **Done when** the facts file matches the fixture byte for byte, and the per-operation split is proved rather than true by construction, since a facts re-run must not blank the change list.
Now: every fact and the stage that computed it match the fixture, the payload is written in name order so two runs over one store produce the same bytes, and the per-operation split is proved rather than true by construction; with a paragraph beneath it saying the condition was amended and by whom.
Why: a done condition narrower than its clause is the common defect in this class of corpus, and one wider than what the checkpoint can produce is the same fault mirrored. Amending it is legitimate; landing the amendment silently is what CLAUDE.md forbids, and 5.3 did the second by doing neither.

### 2026-09-10 - BUILD_PLAN.md, DECISIONS.md - the posting hour was still measured at 2.1 in two places
Corrects: 5.7 moved the posting hour to an operating row whose trigger is five nights fetched under the schedule, and two statements that it is measured at 2.1 were left standing, one of them inside the decision the schedule rests on. The obligation row says "Bounded at 2.1 and again at 2.6", which is the opposite claim: one fetch says the file was there by then and says nothing about when it appeared. Found by the phase 5 sign-off.
Was:
> The provider's posting hour for the day's bulk file is measured here from live fetches rather than taken from documentation, which is the one figure the schedule decision leaves open and the first thing a live feed can be asked
Now: bounded here rather than measured, with the operating row cited for what settles it.
Why: the decision's own ruling is unchanged, so the entry in `DECISIONS.md` is corrected rather than superseded and says so in the same breath. What was stale is a pointer inside its reasoning and not the rule it makes, and superseding a correct decision to repair a cross-reference would move every citation to it for nothing.

### 2026-09-10 - ARCHITECTURE.html - section 11's calibration window, and the flood it now has a number for
Corrects: section 11 said the thresholds are set against your own distribution "after a few weeks" while the obligation states sixty nights of listings, which its own text calls the quarter of trading the level window already uses. Two places holding one fact, and the vaguer of the two is the one a reader meets first. The same callout illustrates with "a hundred-name night says a hundred" and the first night at index size fired 477 of 503. Found by the phase 5 sign-off.
Was:
> so after a few weeks the thresholds are set against your own distribution (see: Condition thresholds are calibrated from your own nights, not from a backfill). Until then the app shows the strongest twenty and states the true fired count in the header, so a hundred-name night says a hundred.
Now: sixty nights of listings with the reason the obligation gives, the obligation cited, and the measured 477 of 503 recorded beside the hundred the paragraph illustrates with.
Why: the callout predicted the flood in the abstract and named which two reasons would cause it, which is why 477 of 503 is not a failed claim. What it did not carry was the figure, and a prediction that never records what actually happened cannot be read as confirmed or refuted.

### 2026-09-10 - ARCHITECTURE.html - section 19.1 gains the run page, and the preamble admits it
Corrects: 5.6 added `run-page.json` to the fixture and section 19.1 did not name it, which reopens the fault 4.0 reconciled this table for once already. Nothing could notice: `fixture-expectations` declares reach over sixteen keys and none is a run page, and `architecture-conformance` reads its population from the document, so a file with no row is invisible to it. Found by the phase 5 sign-off.
Was:
> one per stage's serialised output rather than one over a rendered page (see: The fixture is diffed on each stage's serialised output, of which the facts file is one).
Now: the same sentence, with the run page named as the one exception and why, and a table row for it reached by `read-surface`.
Why: the run page expectation holds the counts a record is computed from across every night the store holds, which belongs to no single stage's output. Either the table admits it or the file goes away, and the file is what carries 5.6's own expectations.

### 2026-09-10 - ARCHITECTURE.html - 15.10's operational header row states the instant it now draws
Corrects: the row promised "what ran, how long each stage took, model calls, network requests, spend, names stale, and what failed in which component" and the header drew neither of the last two, which are the sibling region's and pass on its own test. It also promised no instant, while `BUILD_PLAN.md` states twice that the header carries the instant each stage started, and the posting hour obligation is read there. A figure that is a clock time cannot be read off a duration. Found by the phase 5 sign-off.
Was:
> what ran, how long each stage took, model calls, network requests, spend, names stale, and what failed in which component
Now: what ran, the instant each stage started and how long it took, model calls, network requests, and spend.
Why: the row was wrong in both directions at once, promising two things the region beneath it delivers and omitting the one its own obligations need. `run_log` has stored `started_at` since 1.1 and it reached no surface.

### 2026-09-10 - SCHEMA.md - the facts column sets are disjoint per operation, not outright
Corrects: SCHEMA declared FactsAssembler and ChangeDetector to own disjoint column sets, and the retention 5.4 added made that false: `ChangeDetector` runs `UPDATE facts SET payload = ''`, so `payload` is the assembler's on insert and the detector's on update. The retention paragraph twelve lines below described exactly that while the declaration went on saying the sets do not overlap. Found by the phase 5 sign-off.
Was:
> Declared column sets: FactsAssembler owns `payload` and `payload_hash`; ChangeDetector owns `material_changes`. The sets are disjoint and the grain is the same, which is what permits the split.
Now: the sets stated per operation, which is the grain the ownership rule is written at, with the correction named in place.
Why: one section held two statements of one fact and the one a reader is most likely to trust was the wrong one. The rule the split rests on is that no column is written by two components in one operation, and that was true throughout; the declaration was stating something stronger and untrue.

### 2026-09-10 - CLAUDE.md - five roster rows widened to what their checks now assert
Corrects: five checks were widened by the phase 5 sign-off and a check that asserts more than its roster row says is a property nobody wrote down, which is the same fault as one that asserts less. The roster and the phase report enumerate checks by name, so the two would disagree with nothing to reconcile them.
Was:
> | `clock-usage` | every CI run | Nothing outside the clock reads the machine clock, no schedule is expressed in local time, and no date is parsed against the machine's locale. Comments are stripped first, because a sentence naming a pattern is not a use of it |
>
> | `store-portability` | every CI run | No row in a populated store carries an absolute path |
>
> | `price-storage-form` | every CI run | No migration declares a price or money column `REAL` |
>
> | `ci-parity` | every CI run | `tools/ci.ps1` and `tools/ci.sh` run the same steps in the same order, and a step that fails fails the script it runs in |
>
> | `two-platform` | the matrix | The suite passes on both windows and macos runners |
Now: each row states the widened property, and each names the half that was missing rather than only the half that is there.
Why: the five widenings are the rendering direction of `clock-usage`, a populated store for `store-portability`, the code half of the money boundary for `price-storage-form`, the data root and the cmdlet failure path for `ci-parity`, and every YAML condition rather than three literals for `two-platform`. Each was a check whose roster row claimed a property it reached half of.

### 2026-09-10 - CLAUDE.md, BUILD_PLAN.md - the calendar-time rule gains an instrument
Corrects: the rule that a done condition may not require calendar time arrived at 5.7 with nothing asserting it, in the same pass that left three statements of the condition it forbids standing. A convention with no check is a convention the next planning pass writes past, and this one had already been written past before the ink was dry. Found by repairing those three and asking what would have caught them.
Was:
> being one night on one machine at one moment. Section 17's limit is set when five scheduled nights over the whole index have run, as that measurement plus stated headroom with the headroom's reason, and the deadline follows at three times it.
Now: `done-condition-producible` on the roster, reading every done condition in `BUILD_PLAN.md` and section 20's Done when column; and 5.1's trailing sentence moved out of its done condition into a paragraph of its own, because a clause sitting inside one reads as something the checkpoint waits for.
Why: the check refuses a span of calendar, several nights or days, an unattended run, and evidence stated as accumulating, over 55 done conditions and 8 phase rows. It carries its own proof in both directions, since a sweep whose expected result is nothing is passed by a matcher that matches nothing at all. What it does not reach is prose about a done condition, which is where two of the three stale statements sat: it asserts that no done condition says this, not that nothing in the corpus describes one that did.

### 2026-09-10 - ARCHITECTURE.html, BUILD_PLAN.md - the amended done condition swept into the three places that restated it
Corrects: 5.7 amended its own done condition off calendar time in `BUILD_PLAN.md` and swept nothing else, so three further statements of the old condition were left standing, one of them in the document the corpus calls the source of truth. Found by the phase 5 sign-off, which read section 20 and reported that it states an unmet phase 5 done condition. Two of the three were inside `BUILD_PLAN.md` itself and the sign-off did not name those, which is what makes this an incomplete sweep rather than a single stale cell.
Was:
> a week of unattended nights with the list current each morning, and every stored listing carrying its entry, stop and target
>
> It moves to 5.7, whose done condition is already a week of unattended nights and which already carries the posting hour for exactly that reason.
>
> Section 17's limit is set at 5.7 from the week of nights that produces a distribution, as that measurement plus stated headroom with the headroom's reason, and the deadline follows at three times it.
Now: section 20's phase 5 cell requires all phase-5 rows PASS, the stored plan on every listing, and the schedule registered as a command; and 5.1's two references point at the operating trigger and the surface it is read on rather than at a checkpoint that waits.
Why: a done condition may not require calendar time, and the rule saying so was written at 5.7 in `CLAUDE.md` while three statements of the condition it forbids were left in place. An amendment that lands in one of two documents holding one fact leaves the other as the one a reader believes, and section 20 is the one a sign-off reads.

### 2026-09-10 - ARCHITECTURE.html - section 13's phase column, against the plan it describes
Corrects: every cell in 13.2's Phase column was wrong, and the sentence under the table contradicted three of them. 5.0 ruled the six reason thresholds an operating obligation triggered by sixty nights of listings, and repaired that row from 4 to 5 rather than off a phase altogether, so 13.2 and 13.4 both went on saying phase 5 tunes them while the obligation table said no checkpoint produces the evidence. The other three rows read 6 where the register is 7.3, the rule versions 7.6 and the model's proposal 7.7, and the table's own next sentence already said phase 7. Found while sweeping the stale phase 5 done condition, in the column beside it.
Was:
> 5, which is the phase that first records how many names fire each night, using that record rather than a backfill
>
> Only the first is cheap enough to do early, and it is the one that matters least. The other three need resolved setups, which take months to accumulate, which is why they sit in phase 7 and why phase 5 has an obligation described below.
>
> Beyond that the phases carry what their table already says. Phase 5 also tunes the thresholds, because it is the phase that first records how many names fire each night.
Now: the thresholds row names no phase and says why, the other three name their phase 7 checkpoints, and both prose passages state that all four wait on the calendar rather than on a checkpoint.
Why: the same defect as the entry above, from the other end. A ruling was taken in `BUILD_PLAN.md` and the document that describes the same thing was left saying what it said before, so the corpus held two answers and the check that would have caught it does not exist. Section 13 is not a claim source, which is why nothing failed and why it needed reading rather than running.

### 2026-09-10 - BUILD_PLAN.md - 6.0 gains the bulk payload refusal
Corrects: nothing in the corpus covered a bulk payload that is not a price payload at all. Section 18 has a row for one that is for another session and one for a payload holding none of the index, and the provider returns neither of those for a session that is not the most recent: it returns something the parser meets and throws a `FormatException` on. Found by replaying sessions 2026-09-04 and 2026-09-08 while measuring a night at index size.
Was:
> Settles which local model, the section-to-lane configuration shape, and how a research pass is recorded so a fixture can replay it without a network. Discharges the source-lists obligation from 1.7 against the coverage measurement (owes: Source lists reviewed against measured coverage), whose evidence has been in hand since 1.7 and which sits here because 6.0 is where the lists are next used rather than because anything is waited on.
Now: the same paragraph, and a second sentence pointing the new obligation here with the evidence stated, because the repair is a failure row and a named refusal rather than a component.
Why: the night refuses and names the step, so nothing wrong is stored and the defect is the message rather than the behaviour. A planning pass is where a failure row is added, and the evidence is already in hand rather than waited on.

### 2026-09-10 - CLAUDE.md - the store the verification scripts drop
Corrects: `tools/ci.*` dropped `data`, which is the operator's store and the one the nightly job fills. Found by running the two in one session: a live night backfilled 125,736 bars over 503 per-name requests, `tools/ci.sh` was run to verify the next commit, and the backfill was gone. Every session that verifies a checkpoint was silently resetting the store the schedule accumulates into, and the next night would have run a first-run backfill without anything saying why.
Was:
> /data             gitignored. the store lives here
Now: the same line, and `/data-ci` beside it as the store the verification scripts create and drop, with the note that they never reach the one above.
Why: the corpus already holds that nothing in the harness reaches `data/`, and that was true of the suite and false of the scripts that run it. A property held by everything except the one tool an operator runs by hand is not held. The scripts now export a data root of their own, so the path to the operator's store is not something they know rather than something they are trusted not to use.

### 2026-09-10 - RUNBOOK.md - the scheduled task's logon type, and what the boundary stores
Corrects: the registration written earlier the same day produces a task that runs only while the account is logged on, and says nothing about it. Found by registering it on the operator's machine and reading the task back: `Register-ScheduledTask` with no principal stores `InteractiveToken`, and the difference from a task that runs when logged off does not appear in `Get-ScheduledTask`, which shows both as ready.
Was:
> `-WakeToRun` because a laptop left to itself sleeps and a nightly job that silently did not run is worse than no nightly job. `-StartWhenAvailable` because a machine that was off at the instant should run the night when it comes back rather than skip it, and the night is idempotent. Confirm with `Get-ScheduledTask 'EquityBrief nightly'`, and remove with `Unregister-ScheduledTask 'EquityBrief nightly'`.
Now: the same two sentences, then the logon type stated as the thing that is invisible, the elevated principal that fixes it, and the one line that reads back which of the two is registered.
Why: the section exists so a nightly job does not silently fail to run, and it shipped with the commonest way for one to silently fail to run. A machine that has been running nights for a month stops on the morning after an update reboot, and the run page shows the night before, which reads as a night that has not happened yet rather than as a schedule that is gone.

### 2026-09-10 - RUNBOOK.md - what Task Scheduler stores for a UTC boundary
Corrects: the text said setting the boundary to an instant with a `Z` is what pins it, and reading the task back shows an offset rather than a `Z`, which reads as the pinning having failed.
Was:
> Setting `StartBoundary` to an instant with a `Z` is what pins it, and it is the same setting the interface calls synchronizing across time zones.
Now: the same sentence, and then what is actually stored, which is the machine's own offset carrying the same instant, with the note that a boundary carrying any offset is the pinned form and a boundary carrying none is the one that walks.
Why: an operator who checks the work and finds something other than what the runbook described has to decide whether the runbook or the machine is wrong, and the answer is neither.

### 2026-09-10 - BUILD_PLAN.md - 5.7's done condition, which required calendar time
Corrects: a done condition that read "a week of unattended nights has run" and so stopped the build for a week while producing nothing. Found when the operator asked why development halts for days at a time at the end of every phase. The two figures it waited on are produced by the system running on a schedule and by no checkpoint, which is the class this plan already carries as operating, and nothing was registered with any scheduler, so the wait would not have ended on its own.
Was:
> The week of unattended nights is what produces the posting hour (owes: The provider's posting hour for the day's bulk file, measured from live fetches). It stood at 3.7, which is the phase 3 report and produces no evenings; a week of nights that ran on a schedule is a week of observations of when the file actually appeared, and it is the first thing in the plan that yields several.
>
> The week of nights is also what sets the nightly wall clock at index size, which stood at 5.1 and which 5.1 cannot produce: one night run by hand is an observation and a limit needs a distribution. The limit is that week's measurement plus stated headroom with the headroom's reason, and the night's deadline follows at three times it, in section 17 and in the retry policy together.
>
> **Done when** every phase 5 claim is PASS naming an instrument whose declared reach includes it, unexamined is zero, a week of unattended nights has run with the list current each morning, the posting hour is recorded from those nights rather than from documentation, the wall clock limit is set from those nights with its headroom stated, and the pair 5.0 predicted is checked against the actual pair, with every claim that arrived unpredicted named and placed in the list it belonged in and every predicted claim that did not arrive named as such.
Now: the section states the defect and repairs it, both figures move to the operating form, and the done condition keeps the half 5.7 produces: the pair checked, the two rows converted, and the registration written as a command.
Why: `CLAUDE.md`'s deferral convention already names this failure, in the words "never, where the named point is a report or a page rather than the thing that measures". 5.7 is the phase 5 report. The posting hour had already been moved from 3.7, which is the phase 3 report, so it had been moved from one report to another and neither produces an evening.

### 2026-09-10 - BUILD_PLAN.md - the posting hour row, and a wall clock row beside it
Corrects: the same defect in the table the checkpoint text was reading. The posting hour was a checkpoint row due at 5.7, and the wall clock was carried in 5.1's and 5.7's prose without a row of its own.
Was:
> | **The provider's posting hour for the day's bulk file, measured from live fetches** | 2.0 | 5.7 | 5.7's done condition is a week of unattended nights, which is what produces evenings of observation. Bounded at 2.1 and again at 2.6. It stood at 3.7, which is the phase 3 report and produces no evenings |
Now: both rows are operating, each stating its numeric trigger, the surface it is read on and the checkpoint that built that surface, which is 5.6's operational header.
Why: an operating row is chased by the trigger firing rather than by a checkpoint arriving, and 5.6 already built the surface both are read on. The wall clock gains a row because a figure carried only in prose is a figure `obligation-reconciles` cannot see.

### 2026-09-10 - CLAUDE.md - a done condition may not require calendar time
Corrects: nothing in the conventions forbade the done condition above, so the next planning pass would have written another. The deferral rule covered where an obligation points and said nothing about what a checkpoint may wait for.
Was: the Conventions section moved from the deferral paragraph straight to "Obligations are named and cited, as decisions are".
Now: a paragraph between them stating that evidence which only accumulates is carried in the second deferral form, that the checkpoint keeps the half it produces, and that where a done condition needs the system to have run, what it requires is the procedure written down as a command rather than the operator having got around to it.
Why: the rule is stated with the evidence that produced it, which is that this corpus wrote such a condition once and it cost a week that was not even buying the evidence.

### 2026-09-10 - ARCHITECTURE.html - the wall clock row and the deadline that follows it
Corrects: both rows said the wall clock is proposed until 5.1 measures it, and 5.0 had already moved it to 5.7, so the document named a checkpoint the plan no longer did. Neither produced it.
Was:
> <tr><td>Nightly wall clock, at index size</td><td>the measured night plus stated headroom, proposed until 5.1 measures it. ...</td>...<td>run log duration, measured per step on a live night at index size and recorded with the run's own conditions</td></tr>
Now: the row states a figure the night is bounded by, names five scheduled nights read on the operational header as what settles it, and its asserted-by cell claims the relationship between the two rows rather than a measurement the harness cannot reach.
Why: a limit stated with no figure is a limit nothing can be pinned against, and a claim whose asserted-by cell describes a live measurement is a claim about the running system, which a green report never speaks to. The relationship between the wall clock and the deadline is assertable today and is now asserted, in the document and in the retry policy together.

### 2026-09-10 - RUNBOOK.md - registering the schedule, as a command
Corrects: step 8 read "Register the schedule with the platform's scheduler, in UTC", which is an instruction and not a command, and nothing was ever registered on either machine. Every figure that waited on nights was waiting on this.
Was:
> 8. Register the schedule with the platform's scheduler, in UTC.
Now: step 8 points at a new section carrying the command for each platform, the provisional UTC instant with the reason it is provisional, and the local-time trap in both schedulers.
Why: the two schedulers both trigger in local time and each needs a different thing done about it, which is exactly the knowledge a runbook exists to hold. A step that tells the reader to find a command is a step that gets deferred to a quieter evening.

### 2026-09-10 - BUILD_PLAN.md - the news window obligation, discharged
Authorised by: News is one dated query, paged to cover the day, and attributed to names locally
Was:
> | **One day of news exceeds one request at the provider's limit** | 2.5 | 5.5 | ...
Now:
> the same row with `5.5, discharged`
Why: 2.5 measured one dated request coming back at exactly the provider's cap and recorded the
obligation for the checkpoint that counts articles per name. 5.5 builds that counter, and the answer
is a page rather than a window: the query is paged until the day is covered, every page is counted,
the page count follows the day's news volume rather than the size of the universe, and a day past
the cap refuses rather than storing a truncated count.

### 2026-09-10 - ARCHITECTURE.html - the night close, and two reads the components turned out to need
Corrects: two cells and one missing row. The news pulse counter's matrix row was blank in every
column while the count it keeps is over the index rather than over every symbol the market wrote
about, which needs membership: one live request reached 3,232 distinct symbols against an index of
503. And section 14's closing step had no component behind it, so the counts it names were nobody's.
Was:
> the news pulse counter read no store at all; and neither the catalogue nor the matrix carried a
> row for the night's closing stage
Now:
> the counter reads membership, and the Night close has both rows, reading the four stores it counts
> off and appending to the run log
Why: a component is named in the catalogue in the same commit that introduces it, and
`component-access` refused the class until it was. The closing stage computes nothing and decides
nothing: every figure is counted off the store the night has just written, because a stage's own
count of what it wrote is the stage's opinion and this is the store's.

### 2026-09-10 - BUILD_PLAN.md - the two obligations 5.4 discharges, marked so
Corrects: nothing in the figures. Both rows were still open at a checkpoint the record now shows as
landed, which `obligation-reconciles` refuses: a row still owed at a checkpoint that has landed is a
row whose due point has passed with nothing saying so.
Was:
> the strength score row and the plan column's condition sentences row both read `5.4` with no
> discharge
Now:
> both read `5.4, discharged`
Why: 5.4 orders tonight's list on band strength as its tiebreaker, with the phase 3 sign-off's
measurement behind what that score is dominated by, and it makes the condition-to-words mapping's
catch-all fail rather than render with every sentence asserted on the surface a person reads.

### 2026-09-10 - ARCHITECTURE.html - the change detector's listings read, restored where the store exists
Corrects: nothing new. 5.3 cleared the cell because `listing` did not exist and a component
declaring a read of a table nothing has created is a declaration with nothing behind it. 5.4 creates
the store, so the read and the payload retention it needs land together.
Was:
> the change detector's Listings cell was blank, and its catalogue row said the retention arrives
> from 5.4
Now:
> the cell carries R and the row states the retention as one of the things it does
Why: the retention is what section 16 states for `facts`, and it is the detector's because that
component already owns Update on that table and a table may never have two owners for one operation.

### 2026-09-10 - ARCHITECTURE.html, CLAUDE.md - contradiction L repaired, and listings-coverage promoted
Corrects: contradiction L, which is the shortlist builder's matrix row disagreeing with its
catalogue row in its reads. The matrix gave it Fundamentals and News pulse, which the catalogue
names neither of, and blanked Membership, Bars and Facts, which four of the six reasons need. A
blank cell is a claim as much as a filled one, so the row made five wrong statements. It was
unreachable until the component existed and `component-access` refused it on the first run.
Was:
> the matrix row read Calendar, Computed tables, Fundamentals and News pulse and wrote Listings and
> Run log; the catalogue row read `levels, indicators, ladders, calendar, facts`; and
> `listings-coverage` was rostered `from 5.4`
Now:
> the matrix row reads Membership, Bars, Calendar, Computed tables and Facts and writes Listings and
> Run log; the catalogue names them; and the roster row reads `every CI run` with what the check
> asserts
Why: 5.0 ruled it from section 11's six reasons and section 1's statement that the shortlist selects
on chart state alone with no fundamentals and no model in the decision, which puts the Fundamentals
read on the wrong side. The News pulse is a coverage measure rather than chart state and goes with
it. What was missing is what tonight's close and today's volume come from, which neither side named.

### 2026-09-10 - ARCHITECTURE.html, BUILD_PLAN.md, SCHEMA.md - three reads move to the checkpoints that create the stores
Corrects: three cells filled ahead of the code they claim. The facts assembler's matrix row read
Listings and Fundamentals, and the change detector's read Listings, while `listing` is created at
5.4 and `fundamentals` at 6.1. `component-access` refuses a declaration with no cell behind it and a
cell with no declaration behind it, in both directions, and it refused these the moment the two
components landed. It is the same direction `writer-ownership` refused a deleter at 4.0 and
`schema-columns` refused a column at 5.0.
Was:
> the facts assembler read `bar store, indicators, swings, volume profile, levels, ladders, moves,
> listings, fundamentals, calendar` with Listings and Fundamentals filled; the change detector's
> Listings cell was filled; and 5.3 was to build the payload retention
Now:
> the three cells are blank and each row says which checkpoint fills it, and the retention lands at
> 5.4 with the store it reads
Why: a component declaring a read of a table nothing has created is a declaration with nothing
behind it, and the check that would catch it can only reach a row once the component exists. So the
cell is filled where the read is, which is the same rule the corpus has now applied to a deleter, a
column and a read.

### 2026-09-10 - SCHEMA.md - the last of the six computed tables gains its deleter
Authorised by: Every computed table's writer is its own deleter
Was:
> `move` gave Delete to nobody, and the note read "Five of the six are declared at 4.2 and `move`
> waits for 5.2"
Now:
> MoveAnnotator owns all three operations, and the note reads that all six are declared
Why: 4.0 ruled that every computed table's writer is its own deleter and 4.2 implemented it for
five of them. `move` waited because `MoveAnnotator` did not exist and a deleter declared before its
component deletes is a declaration with nothing behind it, which `writer-ownership` refuses in that
direction. The component exists now, so the ruling is complete.

### 2026-09-10 - SCHEMA.md, BUILD_PLAN.md - the sector is declared, and two limits leave the checkpoint that produces neither's evidence
Corrects: two due points naming a checkpoint that cannot supply what they need. Section 17's row
coverage claim is about a listings row for every name every night and `listing` is created at 5.4,
which is the same fault as the `listings-coverage` roster row 5.0 moved and was missed beside it.
And the wall clock at index size needs a night that ran on a schedule; 5.1 produces one night run by
hand on the machine at hand, which is an observation and not a bound. Both would have failed the
moment `PROGRESS.md` recorded 5.1, because a claim still out of scope naming a landed checkpoint is
refused.
Was:
> `SCHEMA.md` carried the sector as a ruling due at 5.1 and `membership` declared five columns; and
> the two limits were owed at 5.1
Now:
> `membership` declares six, with the sector last because `ALTER TABLE` appends and this file states
> the order the store has rather than the order that reads best; and the two limits are owed at 5.4
> and 5.7
Why: a deferral names what produces the evidence. 5.7's done condition is already a week of
unattended nights and it already carries the posting hour for the same reason, so a distribution of
durations arrives there and nowhere earlier. What 5.1 carries instead is the guard: every stage
records the instant it started and the instant it ended, so a night landing inside its limit by one
step doing nothing is legible rather than hidden in a total.

### 2026-09-10 - BUILD_PLAN.md, ARCHITECTURE.html - phase 5's claims predicted as a pair, and three expectations the fixture table did not name
Corrects: two things. Phase 5's checkpoint text stated its deliverables and not the rulings 5.0
took, so 5.1 claimed the backfill was new at index size when 2.6 had already run 126,235 bars over
503 tickers, and its wall clock read as a suite assertion when it is a property of the running
system. And section 19.1 named the expectation files a fixture holds and listed neither moves nor
forward returns nor news pulse, all three of which phase 5 produces, which is the same defect the
4.0 obligation found from the other end when the fixture held three files the table did not name.
Was:
> 5.0 predicted nothing; 5.1 read "Membership and backfill over the whole index" with "the nightly
> wall clock is inside its limit at index size" as a done condition; 5.2 through 5.7 stated their
> deliverables alone; and 19.1's expected outputs ran from membership to facts with no row for
> moves, forward returns or news pulse
Now:
> two disjoint lists with the arithmetic between them and the pair the sum makes, 234 claims and
> 162 PASS after phase 5; the computed chain named as what has never run at index size; the wall
> clock recorded per step with its conditions and with what it does not establish; and three rows
> in 19.1
Why: 5.7's done condition checks a total and a PASS count, and a prediction given as components is
checked by re-deriving it, which is not the same as checking it. Phase 4 predicted 204 and 93 and
came in at 203 and 92, and the one-away miss was legible because a pair had been stated.

### 2026-09-10 - ARCHITECTURE.html, RUNBOOK.md, BUILD_PLAN.md - one dated news query does not carry a day
Authorised by: News is one dated query, paged to cover the day, and attributed to names locally
Was:
> section 14's step read "Count today's articles per name from one news feed request"; section 17's
> weighted-call row priced "the news feed 5"; `RUNBOOK.md` read "News costs 5."; and the decision's
> name said news arrives in one dated feed request
Now:
> one dated query paged until the day is covered with every page counted, priced per page, and a
> decision whose name says so, with the page count named as a function of the day's news volume
Why: 2.5 measured one dated request for a single session coming back at exactly 1,000 articles,
which is the provider's cap, over 3,232 distinct symbols. One request does not carry a day, so a
count taken from it is a count over whatever the cap happened to include, which is a figure over a
mixed population and is not stated at all. The limits are restated rather than satisfied. The cost
rule survives on the measurement rather than on a workaround: one request already reached 3,232
symbols against an index of 503, so the feed is market-wide and adding names adds no articles, which
is what makes a paged query something other than a per-name call.

### 2026-09-10 - SCHEMA.md - the base rate's population, its null horizon and its grain
Authorised by: The base rate is over every name-night, and never over the listed ones
Was:
> `base_rate` read "the universe figure for the same window and horizon", and the file said nothing
> about which population, about the `setup` horizon, or about why one night's figure repeats across
> the index
Now:
> the figure for this horizon over every name-night in the window rather than over the listed ones,
> null for `setup`, and repeated per row by design because the row is what the run page reads
Why: the corpus named the figure in six places and defined it in none. 15.10 was the only place a
population appeared, and `CLAUDE.md` settles the rest on its own terms: a figure computed over listed
names only is a figure over the wrong population, because a listings row exists for every name.

### 2026-09-10 - ARCHITECTURE.html, SCHEMA.md, CLAUDE.md - fourteen faults the corpus carried into phase 5
Corrects: fourteen, each of which would have failed a check or produced a wrong reading during the
phase. `listings-coverage` was rostered from 5.1 and `listing` is created at 5.4, so the row would
have failed `coverage-reported` the moment 5.1 landed. The universe screen reads listings and 5.1
builds it three checkpoints before that store exists. The run page's reason record sat wholly at 7.5
while three operating obligations name 5.6 as the surface their trigger is read on. `ChangeDetector`
had a declared column set and no catalogue or matrix row. Section 16's calendar row still described
the `status` column 4.3 removed. `facts` had a retention rule nobody owned and half of it named a
condition nothing records. `news_pulse` had no updater and the night is declared idempotent. `move`'s
grain could not express the multi-day moves its catalogue row claims. 13.2 and 13.4 disagreed about
which phase tunes the thresholds. 15.3's route list lacked the route 15.7 uses. Every limit was
written against 500 and the fetched index is 503. And 19.2 listed no row for the screens or the
figures, so two claim sources had no declared check in the document.
Was:
> the calendar row read "whether the provider has confirmed it"; the facts row read "kept for every
> night a name was on the list or was opened; other nights keep the hash only"; the wall clock row
> read "Nightly wall clock, 500 names" and "under 5 minutes, proposed"; the threshold row read "4,
> using its own nightly record"; the route list omitted `#/night/<date>`; and the catalogue, the
> matrix and 19.2 carried no row for the change detector, the screens or the figures
Now:
> each corrected to what is true, with the change detector given both rows and a listings read, the
> facts retention owned by the component that already owns Update on that table, and "or was opened"
> dropped because nothing records that a name was read
Why: a rule the store cannot answer is a rule no test can induce, and a cell that promises one is
worse than a cell that says less. The wall clock is the sharpest of them: a limit set to the figure
that measured it is passed by construction by the night that set it, so the row now states the
measurement plus headroom with the headroom's reason, and the night's deadline follows at three
times whatever the limit becomes.

### 2026-09-10 - CLAUDE.md, ARCHITECTURE.html - the figures are read, and one names a store the tables do not carry
Corrects: no figure in the document was read by anything. The phase 4 sign-off found that figure
10.1's rows were reached by nothing, which is how the trailing stop rule drifted from the corpus for
a phase with no instrument asking. The cause is wider than the one figure: `ArchitectureTables.In`
matches table elements, every figure is a `div.fig`, and `EveryTableInTheDocumentIsPlaced` asserted
that every table the reader returned was placed. Its completeness was defined by the thing it was
checking, so four figures and fifty-nine boxes were unread and nothing could say so.
Was:
> the roster row read "in a table or in the nightly run's ordered list" and "every table in the
> document is placed so none can go unread"; and figure 5.1's nightly store box read "facts,
> levels, ladders, list reasons, forward returns"
Now:
> "in a table, in a figure, or in the nightly run's ordered list", with every table and every
> figure placed against a population read from the document rather than from the reader; and the
> box reads "listings", which is the name section 16 carries
Why: this is the shrinking-population defect in its third form, after a floor set to what a run
produced and a reader whose population was its own output, so the population is now the document's
own count of figure openings asserted against the number parsed. Figures 9.1 and 10.1 become claim
sources reached by `fixture-expectations`, figure 12.1's eight boxes are out of scope until phase 6,
and figure 5.1 is placed as the system diagram with every box asserted to name a component or store
sections 7 and 16 carry. That last assertion found the wording on its first run: the diagram named
a store called "list reasons" and the store is Listings.

### 2026-09-10 - ARCHITECTURE.html - the trailing stop and the fifth condition, as the code runs them
Authorised by: The trailing stop is the higher of the band beneath and the last swing low
Was:
> figure 10.1's trend box read "uptrend: tranches on pullback bands, the stop trails the last
> higher low", the key beneath it read "In a range the stop is the range floor. In an uptrend it
> trails", and the conditions box called its list fixed and named four patterns
Now:
> the stop trails to the higher of the band beneath and the last swing low beneath the tranche,
> with the reason a swing low alone is looser than the range rule; and the conditions box names
> the fifth answer, a tranche reaching the zone, as the absence of the four rather than a fifth
> pattern
Why: the phase 4 sign-off read the code against the corpus and found the corpus held neither rule.
The phrase "higher of" appeared in two records, a source comment and an expectation note, and the
string `ReachesTheZone` appeared nowhere at all while reaching the store and the page. A decision is
changed only by another decision, so the rule the code ran was a rule no spec stated.

### 2026-09-10 - CLAUDE.md - no-superseded-citation reads the specs and the code, not the records
Corrects: the check refused a superseded citation anywhere in the corpus, which forbids the corpus
from ever superseding a decision a record has cited. `PROGRESS.md` is append only and a record is
corrected by a new dated entry rather than by editing the old one, so the phase 4 sign-off's own
citation of the trailing stop rule turned the check red the moment that rule was superseded. Found
at 5.0 by superseding it.
Was:
> No cited name resolves to a decision under "Previously decided"
Now:
> the same for a spec or for code, with the three records excluded by name and the exclusion
> asserted to be removing something
Why: a superseded citation in a spec or in code is a live pointer to a dead rule; in a record it is
a dated statement of what the corpus held on the day it was written. The exclusion is asserted to
match at least one citation rather than left as a filter that reads as a rule and behaves as a
comment, which is the drift that file already carries one story about.

### 2026-09-10 - BUILD_PLAN.md - the eleven obligations the phase 4 sign-off left for 5.0 to enter
Corrects: nothing. Obligations recorded when they were created rather than remembered. The sign-off
edits no spec, so it named them and left their due points as proposals for this pass to place.
Was:
> the obligations table carried thirty-eight rows and none of the eleven; 5.0 read "Settles the
> base rate's population and window. Confirms the six reason thresholds as the proposals they are,
> and states that 5.6's own record is what calibrates them, so nothing is tuned in advance."; and
> 5.4 opened with its migration
Now:
> forty-nine rows, ten of them due at 5.0 and one at 5.4, each cited back by the checkpoint that
> owes it
Why: seven of the eleven are tests over phase 4 code with the evidence in hand, and 4.0 took five
such obligations in its own planning pass for the same reason. An obligation waiting for a
checkpoint that will not look at its subject is waiting for nothing, and 5.1 is whole-index
membership, backfill at scale, the universe screen and a live night, which is the checkpoint whose
failure mode nobody has seen yet.

### 2026-09-09 - CLAUDE.md - three verification rules the corpus had paid for twice and never written
Corrects: three defects the phase 4 sign-off found and left in a record, where a record is where a
finding goes to be true and unread. First, the sign-off sorted eleven surviving mutations into four
groups and only three of them had ever been named, and the unnamed one is the class where the test is
well formed and the data cannot take the shape the mutation would change. Second, an elapsed-time
bound written as an absolute number has been repaired twice by hand on one test, at a quarter of a
second and then at three seconds, and the rule behind both repairs was never stated. Third, 4.4 chose
its condition 9 mutation by a rule stated in advance, and that rule named where a stop sits, so two
of the three properties the checkpoint added went unmutated with the condition satisfied.
Was:
> the Verification list carried nine rules and none of them about classifying a surviving mutation,
> about calibrating a bound on elapsed time, or about how a mutation is chosen
Now:
> three further rules: a surviving mutation classified as a tautology, a missing property, an
> unreachable boundary or an unproducible shape, with only the first three defects in the test; a
> bound on elapsed time calibrated against something the machine also produces; and a mutation's
> stated rule naming the property it is trying to break rather than the line it edits, with the
> properties the checkpoint did not mutate named
Why: phase 5 writes assertions against news at index scale and phase 6 against filings and a model's
output, which is the largest untested payload surface in the project and the one where a captured
shape is least likely to be representative. The remedy for the fourth class is capture before parse
applied to assertions and not only to parsers, which is the instrument the corpus already has.

### 2026-09-09 - SCHEMA.md - the calendar window reaches a year behind
Authorised by: A calendar event is fetched once for the whole index, and the calendar holds provider events only
Was:
> the note said the window is a quarter and not the earnings horizon, and gave the reason for
> reaching ahead
Now:
> a quarter ahead and a year behind, with the reason for each and the reason it is a year and no
> further
Why: 4.8's earnings rule states the last two prints' one-day moves and needs the dates those moves
happened on, and the endpoint answers with historical and upcoming events over whatever range it is
asked for, so one request carries both. A year and no further because the moves are read off the
bars, which are kept for a year: a calendar reaching further back would name a print whose session
the store does not hold.

### 2026-09-09 - BUILD_PLAN.md - the trend-dependent stop is discharged at 4.5
Authorised by: The stop rule depends on the trend state
Was:
> | **The trend-dependent stop, which trails in an uptrend rather than sitting at the next band**
> | 4.4 | 4.5 | ...
Now:
> the same row with `4.5, discharged`
Why: 4.5 built the trailing rule at the top of the ladder and the trailing stop with it, which is
the same machinery. The stop is the higher of the band beneath and the most recent swing low
beneath the tranche, because a trailing stop that can sit below the range floor is not trailing
anything.

### 2026-09-09 - BUILD_PLAN.md - the trend-dependent stop moves to 4.5
Corrects: 4.4 places every stop at the low edge of the next band beneath, which is the rule
section 10 states for a range, and **The stop rule depends on the trend state** says an uptrend
trails the last higher low instead. Three of the four fixture names are in an uptrend, and the
literal trailing rule puts their stop inside the band the first tranche sits on: AAPL's last
higher low is 300.5700 and its first tranche runs 299.7415 to 320.28.
Was:
> 4.5 read "Exits on the resistance bands above the close, at most five, one closer than two
> typical days' moves from the blended entry listed and not traded, equal fractions per traded
> exit, the top of the ladder a trailing rule rather than a price."
Now:
> the same, and "The trailing machinery is what the trend-dependent stop needs, so it lands here
> too", with the obligation named
Why: a stop inside the band being bought is not a stop, so this is a checkpoint's work rather than
a line, and 4.5 builds the trailing rule at the top of the ladder, which is the same machinery: a
stop that follows a swing rather than naming a price.

### 2026-09-09 - ARCHITECTURE.html - the ladder builder reads the bar store
Corrects: the catalogue gave the ladder builder levels, indicators and the calendar, and it reads
the bar store too: the close it places tranches against and the sessions the tranche conditions
are read over are both bars. Found at 4.4 by declaring what the component touches and watching
`component-access` refuse the row.
Was:
> the catalogue row read `levels, indicators, calendar`, and the matrix row's Bars cell was blank
Now:
> `levels, indicators, calendar, bar store`, and the Bars cell carries R
Why: the same shape as contradiction K, in a row nothing could reach until the component existed.
A blank cell is a claim as much as a filled one, and this one was wrong in the direction that
reads as a component touching less than it does.

### 2026-09-09 - SCHEMA.md - the calendar's status column was a field the provider does not file
Corrects: 4.0 gave the `calendar` table a `status` column carrying `confirmed` or `estimated`, on
the reasoning that a booked print and an unconfirmed one are different things. They are, and the
provider files no such field. Found at 4.3 by capturing the endpoint before writing the parser,
which is the rule 1.6 and 1.7 set after 1.2 stored a membership parser reading a field the
provider does not send.
Was:
> | `status` | TEXT | `confirmed` or `estimated`, as the provider files it |
> and a note arguing that a name whose next print is an estimate is a different thing from one
> whose print is booked
Now:
> | `timing` | TEXT | `before`, `after`, or `unstated`, which is when in the session the provider
> says it falls |
> and a note recording what the payload carries, what it does not, and how the difference was
> found
Why: what the provider does carry is whether the report lands before the session or after it,
which decides which bar prices the print, and that earns a column for the same reason the status
was thought to. The explicit blank the failure table promises needs no status column: the row
exists or it does not.

### 2026-09-09 - SCHEMA.md - the calendar window is a quarter
Authorised by: A calendar event is fetched once for the whole index, and the calendar holds provider events only
Was:
> the note said the fetcher drops rows for events that have fallen out of the window it fetches
> and did not say what the window is
Now:
> ninety days, with the reason: every name reports once a quarter, so a quarter ahead holds every
> member's next print, and a window equal to the twenty-session horizon would mean a date arrives
> already inside it
Why: measured on the capture rather than assumed. The four fixture names' next prints fall six to
eight weeks out, which is outside a horizon-sized window and inside this one, so a horizon-sized
window would have made the earnings-soon condition fire on the day the provider published the date
rather than on the name approaching it.

### 2026-09-09 - ARCHITECTURE.html, RUNBOOK.md - the calendar endpoint's weight, measured
Authorised by: The night's cost is counted in weighted calls against the stated daily allowance
Was:
> the weighted-call row named the bulk file at 100, a ticker's history at 1, fundamentals at 10
> and news at 5, and the runbook's weights paragraph said the same four
Now:
> both name the earnings calendar at 1 for a whole window
Why: measured at 4.3 against the account's own request counter rather than read from
documentation, which is what the done condition asks and where the other four figures came from.
One request over ninety days returned 22,526 rows worldwide and moved the counter by one.

### 2026-09-09 - SCHEMA.md - five computed tables get their deleter
Authorised by: Every computed table's writer is its own deleter
Was:
> `indicator`, `swing`, `volume_profile`, `level` and `ladder` each gave Delete to none, and a
> note said the rows still read none because this file describes the code rather than the
> intention, with the five due to change at 4.2
Now:
> each of the five names its own writer as its deleter, and the note says why `move` is not among
> them, where the boundary comes from, and why the statement lives in each component's file
Why: 4.2 wrote the deletes, so the declaration now has code behind it. `move` waits for 5.2
because `MoveAnnotator` does not exist, and `writer-ownership` refuses a declaration with nothing
behind it in that direction as well, which it did when these rows were changed at 4.0 ahead of the
code. The statement is in each component's own file because a write is attributed to the file it
appears in, so a shared helper holding the `DELETE` would be a file that deletes and is declared
nowhere.

### 2026-09-09 - BUILD_PLAN.md - the swing boundaries move from 4.1 to 4.4
Corrects: a due point that produces no evidence, which is the defect 3.0 swept the corpus for and
which this pass wrote into the table itself. The row was placed at 4.1 because the trend
classifier reads swings. It reads them to ask which of two is later, and reaches neither the
plateau rule nor the outside day that is a peak and a trough at once, so the checkpoint could not
have produced the evidence whatever it built.
Was:
> | **The swing boundaries the committed fixture cannot reach** | 3.7 sign-off | 4.1 | 4.1 builds
> the trend classifier, which is the next component to read swings and the next to write
> constructed-input tests over them
Now:
> due at 4.4, which writes the constructed-input tests over the level arithmetic, so the swing
> cases land with them rather than one checkpoint writing one of the two sets
Why: found at 4.1 by looking for what the checkpoint could actually assert rather than by reading
the row. The two sets are the same kind of work over the same fixture, and 4.4 reads band edges
and roles, which is where a constructed series is being built anyway.

### 2026-09-09 - ARCHITECTURE.html - the trend state and the ladder are one stage, and the classifier writes nothing
Corrects: two things 4.0 got wrong about a component that writes nothing, both found at 4.1 by
building it. Section 14 carried the trend state and the ladder as two steps, and the classifier
writes nothing and hands its label to the builder that writes the row it sits on, so a step of its
own would be a step with no store behind it and the label would be computed twice or carried
between steps in nothing. And the read and write matrix gave the classifier a run log write while
its catalogue row says it writes nothing, which is contradiction K's shape in a row nothing could
reach until the class existed.
Was:
> two list items, "Classify the trend state for every name." and "Build the ladder for every name,
> writing a row whether or not it carries a tranche"; and the Trend classifier's matrix row
> carried W under Run log
Now:
> one item, "Classify the trend state and build the ladder for every name, writing a row whether
> or not it carries a tranche", citing both decisions; and the matrix row is blank throughout
> except its computed tables read
Why: the classifier is the one component in the catalogue that writes nothing at all, because it
is called inside another component's stage and hands back a value. A run log write is what every
other component has and this one does not, and a blank cell is a claim as much as a filled one.
The step count in the note beside the list is gone rather than corrected, because the list states
it.

### 2026-09-09 - BUILD_PLAN.md - phase 4 becomes ten checkpoints, and fifteen obligations are entered
Authorised by: Every computed table's writer is its own deleter
Was:
> phase 4 carried 4.0 through 4.7, with 4.1 as "The calendar fetcher and the trend state", 4.2
> "Tranches and stops", 4.3 "Exits, the invalidation and the near-exit skip", 4.4 "The plan column
> mark and the tables", 4.5 "The earnings trade", 4.6 "The arithmetic and the earnings rule" and
> 4.7 "Phase 4 report"; and the carried obligations table held 28 rows
Now:
> 4.0 through 4.9. The nightly chain and the trend state at 4.1, retention on the computed tables
> at 4.2, the calendar fetcher at 4.3, and the rest renumbered behind them. 4.0 gains the work
> the phase 3 sign-off left it and the clause restoring the claim prediction. The obligations
> table holds 43 rows
Why: 4.1 as written carried a new feed, a new table, a contradiction, a matrix column, a new
component and a new nightly chain at once, and done condition 9 raises the cost of every
checkpoint. Retention is its own checkpoint rather than folded into 4.1 because a deleter built in
the checkpoint that first populates the tables is a delete path whose first real exercise is a
night nobody has watched, over the tables whose growth is the argument for the ruling. The fifteen
new obligations are the six the phase 3 sign-off named and the nine mutations that survived it,
each with the checkpoint that produces its evidence rather than a phase.

### 2026-09-09 - ARCHITECTURE.html - section 14's per-name step becomes nine, and the calendar joins the night
Corrects: three components have shipped since phase 3 and no night has ever run one. `SwingFinder`,
`VolumeProfileBuilder` and `LevelBuilder` are called only from the suite, so a production store
holds no swing, no profile and no band. Nothing failed, because section 14 carried the nine
computations as one step whose due point was a phase, and a claim due at a phase cannot fail until
that phase's first checkpoint lands. Found at 4.0 while re-pointing the claims due at a phase.
Was:
> <li>For every name: indicators, swings, volume profile, levels, trend state, ladder, moves,
> list reasons, facts file.</li>
Now:
> a step fetching the index's dated events, then nine steps, one per computation, each naming the
> stage and the population it runs over, with the ladder step stating that a row is written
> whether or not it carries a tranche; and a note recording what the one step hid
Why: split rather than read as nine claims inside one step, which is where this differs from
contradiction F. Section 15.5 states seven marks and has seven rows, so splitting the Level chart
row would make that document disagree with itself. Section 14 states no count, the night runs
these stage by stage over the whole universe rather than name by name, which is what the
components do, and the order inside them is load bearing: the levels cannot be built before the
swings and the ladder cannot be built before the levels. Written as one step none of that was
visible, and neither was the absence the split now makes assertable.

### 2026-09-09 - ARCHITECTURE.html - a failure row for a name that cannot be classified
Authorised by: The trend state is read from the averages and the last two swings, and a name that cannot be classified says so
Was:
> section 18 carried no row between "Earnings date missing" and "Fewer than 200 bars for a new
> index member"
Now:
> a row for a name whose trend state cannot be classified: the ladder row is still written, the
> state is recorded as not classified with the input that was missing, and no tranche is placed
Why: the fourth trend state needs a failure row or it is a value nothing induces. The row also
carries why the ladder row is written rather than withheld, which is the same argument the
every-name listings row rests on.

### 2026-09-09 - ARCHITECTURE.html - section 19.1 lists the expectations that exist
Corrects: the fixture table listed ten files and the fixture holds three it does not name. The
membership expectation has been there since 1.1, the fetch expectation since 2.1 and the series
state expectation since 1.6. `fixture-replay` found the third by reporting a populated table
nothing expected; the first two had been unlisted for two phases.
Was:
> the table listed bars, fundamentals and news as inputs, then indicators, swings, volume
> profile, levels, ladder, listings and facts as expected outputs
Now:
> a calendar input, and membership, fetch and series state added to the expected outputs, each
> naming the component that produces it
Why: a claim-bearing table that does not name what the fixture holds is a set of expectations
nothing reads back, which is the same object as a check that runs nothing. The three arrive as
PASS on the day they are written, because what they describe has existed all along.

### 2026-09-09 - SCHEMA.md - the ladder row is written for every name, and the trend state has a fourth value
Authorised by: A ladder row is written for every index member every night
Was:
> Grain: one row per ticker per as-of date; `trend_state` TEXT `uptrend`, `downtrend`, `range`;
> `plan` TEXT JSON: tranches, stops, invalidation, exits, earnings setups, arithmetic; and the
> ownership summary carried no note on the six computed tables
Now:
> the grain says for every index member and not only the names carrying a plan, `trend_state`
> admits `not_classified`, `plan` carries the reason where there is nothing to carry, and a note
> records that the six computed tables have no deleter, what 4.0 ruled, and that the rows still
> read none because this file describes the code rather than the intention
Why: the ownership rows stay as they are until 4.2 writes the deletes, because a deleter declared
before the component deletes is a declaration with nothing behind it and `writer-ownership`
refuses it in that direction as well. Recording the ruling and its due point in the file is what
keeps the reader from finding a contradiction between the note and the table.

### 2026-09-09 - ARCHITECTURE.html - the ladder's rules, and the second book's name
Authorised by: The second book is keyed to a dated event, and an earnings print is the only kind on file
Was:
> section 10's key ended "The second book is keyed to dated events, of which an earnings print
> is one, and it never merges with the position book. (see: The earnings trade is a second book)
> Whether it carries events that are not prints, and where those come from, is settled at 4.0.";
> figure 10.1's first box named three trend states; the Trend classifier's catalogue row wrote
> "none, returns the label to the facts assembler" and labelled the chart "uptrend, downtrend or
> range"; the Ladder builder's row said it "places tranches, stops, the invalidation and the
> exits, computes reward to risk, and builds the earnings-trade setups"
Now:
> the citation resolves to the superseding decision and the deferral to 4.0 is replaced by its
> answer; the box carries a fourth state, not classified; the classifier returns its label to the
> ladder builder and labels a fourth state; the ladder builder writes a row for every index
> member every night and builds the event setups. The key gains the tranche condition rule, the
> exit fractions, the researched precondition and a flag stating that every figure in the three
> setups is a proposal
Why: 4.0 settles the holes the document deferred to it, and a deferral answered is a sentence
replaced rather than a sentence kept beside its answer. The fourth trend state exists because the
label decides whether a plan exists at all, and a name whose long average or second swing is
missing has not been measured rather than measured as a range.

### 2026-09-09 - ARCHITECTURE.html - the calendar gets a producer, a store and a column
Authorised by: A calendar event is fetched once for the whole index, and the calendar holds provider events only
Was:
> section 5 carried no earnings calendar feed box and no calendar fetcher box; section 7 had no
> Calendar fetcher row; section 16's data stores table had no Calendar row and its matrix had
> twelve columns, none of them a calendar; the Ladder builder's matrix row carried R under
> Fundamentals; and the matrix key read "The verification harness reads none of the eleven stores"
Now:
> both boxes are drawn, the catalogue row is written, the store row and a Calendar column are
> added with the four calendar readers carrying R and the fetcher R W, the Ladder builder's
> Fundamentals R is gone, and the key names no count
Why: contradiction E, resolved across all four readers together rather than for the rows the
contradiction happened to name. The Ladder builder was the row where the substitution is
confirmed: its catalogue phrase names the calendar and nothing in it names fundamentals, so the
read had been written into the nearest column there was. The Facts assembler and the Staleness
judge both name fundamentals in their own catalogue rows, so their Fundamentals cells stand and
only the calendar cell is added. The Shortlist builder's row is contradiction L and stays open to
5.4. The count in the key went rather than being raised, because the table beneath it already
states how many columns there are and a second statement is the one that goes stale: it read
eleven over a table of twelve before this edit.

### 2026-09-09 - SCHEMA.md - the calendar table
Authorised by: A calendar event is fetched once for the whole index, and the calendar holds provider events only
Was:
> no `calendar` section, and no `calendar` row in the ownership summary
Now:
> a table at the grain of one row per ticker, event date and kind, with `status` carrying whether
> the provider has confirmed the date, and one writer for all three operations
Why: four components declared a read against a table this file did not describe. `kind` carries
provider event kinds only and the reason is written into the file rather than left implied: a
researched date here would be a claim the claim checker cannot reach and a second inserter on a
nightly store. `status` is stored because a booked print and an estimated one are different
things, and a name with no row at all is the third state the failure table promises.

### 2026-09-09 - BUILD_PLAN.md - the tranche share was not a hole
Corrects: the holes table said the tranches carry shares that fall with distance and no rule
produces them, which reads as a gap in the specification. It is not one. The shares came from the
hand-made report the worked example is drawn from, where a person decided how much to commit, and
**The plan places a position and never sizes one** reserves exactly that to the reader.
Was:
> | The share of size per tranche | The plan places a position and never sizes one, yet the
> tranches carry shares that fall with distance, and no rule produces them | 4.0 |
Now:
> a row saying there is no hole, naming the worked example as where the shares came from, keeping
> the exits as they are because a fraction of what is already held is scaling out of a position
> that exists, and marking the row settled
Why: the row was the defect rather than the decision, so it is amended rather than the decision
superseded. A share of an intended position is a sizing rule written as a fraction, and calling
it a display convention does not change what a reader does with it, which is multiply it by a
capital figure the ladder does not have.

### 2026-09-09 - CLAUDE.md, BUILD_PLAN.md - a ninth done condition, and the count two documents disagreed on
Corrects: a checkpoint could satisfy every done condition without ever showing that an assertion
it wrote can fail, and three phases of evidence now say what that costs. Found by the phase 3
sign-off's mutation sweep, which ran 34 mutations over 32 distinct changes and left 13 green at
380 of 380. The pattern is not that some checkpoints write weaker tests: every checkpoint that
mutated its own work found its own holes, and of the two that recorded no mutation evidence, one
carries two of the three group one findings and one of the two group two findings.
Was:
> CLAUDE.md: "All eight, or it is not done:", a list of eight, and "satisfies all eight done
> conditions on its own"; BUILD_PLAN.md: "The seven general done conditions in `CLAUDE.md` apply
> to every checkpoint."
Now:
> "All nine", a ninth condition requiring one added assertion to be mutated and shown to go red
> with the mutation chosen before the run by a stated rule and recorded in the PROGRESS entry, a
> paragraph stating what the condition costs and what the evidence for it is, "all nine done
> conditions", and "The nine general done conditions".
Why: the seven against eight disagreement was already live and is repaired in the same edit
rather than left to be found again. It is named here as a defect the ninth exposed rather than
one it created: `stated-counts` reads CLAUDE.md's own sentence and the list beneath it, and the
count in BUILD_PLAN was outside what any check reads. The ninth condition binds from 4.1, and it
raises the cost of every checkpoint, which is stated in the rules beside it rather than left for
a later session to discover as an unexplained expense.

### 2026-09-09 - CLAUDE.md - a guard behind the record's append-only rule
Corrects: `PROGRESS.md` is declared append only in the document lifecycle table and nothing
asserted it. `changelog-reconciles` reads the five specs, `PROGRESS.md` is a record, and no check
covered a deletion from one. The gap is known because PR #37 deleted 70 lines from the record at
the operator's direction and CI stayed green, which the phase 3 sign-off recorded as a stated
property with no instrument behind it.
Was:
> the Checks table carried no row between `changelog-reconciles` and `pinned-constants`
Now:
> a `record-append-only` row, running every CI run, asserting that every entry heading ever
> present in `PROGRESS.md` is still present, read from the history as a high-water mark over the
> set of headings, with the one removal this repository has made named in the check with its
> commit and its reason
Why: beside `changelog-reconciles` rather than folded into it, because a check named for the
changelog that also guarded a record would be a name that stopped describing its scope. Headings
rather than a line count, because an entry's body can be reflowed without anything being lost and
a count would read that as a removal. The exemption is asserted in both directions: a removal
that is not the named one fails, and the named one being restored fails too, because an exemption
for a removal that is no longer there is an exemption nothing reads.

### 2026-09-09 - ARCHITECTURE.html, BUILD_PLAN.md - the averages are drawn, and one due point was wrong
Authorised by: The volume profile accumulates over the same sixty sessions as the level window
Was:
> section 15.5's Level chart mark said the moving averages arrive at 3.1 and were not drawn;
> section 17's Level window row said the window is the last 60 sessions without saying what
> else reads it; and the obligations table put `Phase 3's expectations owed for 3.0's rulings`
> at 3.1, with 3.1's own text citing it
Now:
> the averages are drawn, so the mark has one of its four elements absent rather than two; the
> Level window row states that the volume profile accumulates over the same window; and the
> expectations row is due at 3.4, cited by 3.4's text, because none of 3.0's three rulings is
> assertable against a stage 3.1 builds
Why: the due point was written by the pass that landed the rule requiring a due point to name
what produces the evidence, and it named a checkpoint that produces none of it. The indicator
engine reads no profile, no swing and no shelf, so a ruling about any of the three could not
have been asserted there. It is recorded rather than quietly moved, because the rule catching
its own author is the evidence that it works on something other than old text.

### 2026-09-09 - ARCHITECTURE.html, BUILD_PLAN.md - contradiction K, and the ninth row it did not name
Corrects: contradiction K, which said eight components' write sat one column to the right of the
store their catalogue row names. Repairing by reconciling every row against its catalogue row
rather than by repairing the eight it named found a ninth, the Research runner, whose write sat
in **Series state** where its row says source documents. It also found that on the Facts
assembler the displacement had moved a read as well as a write, putting **News pulse** where the
catalogue says fundamentals, so a repair confined to writes would have left that row failing on
a read the moment the component landed.
Was:
> the matrix gave Indicator engine, Swing finder, Volume profile builder and Move annotator a
> write in **Listings**; Shortlist builder in **Forward returns**; Facts assembler in
> **Fundamentals** with a read in **News pulse**; Forward return filler in **Facts**; News pulse
> counter in **Research and theme**; and Research runner in **Series state**. Contradiction K
> described eight rows and named Ladder builder among the three that are correct
Now:
> every one of those rows carries the cells its catalogue row names, and contradiction K reads
> nine with the Facts assembler's read named. Ladder builder is no longer listed as correct: its
> **Fundamentals** read appears in no catalogue phrase and its catalogue names a calendar the
> matrix has no column for, which is contradiction E and is settled at 4.0
Why: 23 of the 27 matrix rows are now reconciled against the catalogue in both directions. The
four left out each have a stated reason: the Read API reads every store, the Trend classifier
writes none, the Shortlist builder's reads are contradiction L at 5.4, and the Ladder builder is
contradiction E at 4.0. A repair that fixes the list it was handed cannot find what the list left
out, which is the whole reason this one was done by reconciliation.

### 2026-09-09 - ARCHITECTURE.html, DECISIONS.md - phase 3's three holes settled
Authorised by: A heavy volume shelf creates a band of its own and also strengthens one it coincides with
Was:
> "Whether a heavy volume shelf creates a band or only strengthens one built by the other
> three is settled at 3.0 and is not decided here. The two readings produce different band
> sets, so the level builder differs depending which is taken."; "The window for swings and
> retracements is the last sixty sessions"; and, in figure 9.1's first step, "the five
> retracements of the last two swings"
Now:
> a shelf creates a band of its own and also adds strength to one it coincides with, which is
> what the figure's first and last steps say separately; the window covers swings,
> retracements and the volume profile, one window and not two; and the five retracements are
> drawn between the window's most recent swing high and its most recent swing low, with a
> window holding no swing of one kind producing none rather than drawing from a substitute end
Why: three of the holes table's entries were assigned to 3.0 and each decides what a phase 3
component is. The shelf question makes the level builder a different component depending which
reading is taken; a profile window that differs from the level window would put a band on
volume no other member of the level table can see; and "the last two swings" reads equally as
two swings of the same kind, which is a fraction of a distance nobody retraced.

### 2026-09-09 - ARCHITECTURE.html - the citation pass completed
Corrects: 52 decisions stood uncited in the architecture and the obligation had been carried
from 0.5 to 1.8 to 3.0 without a pass that read the remaining sections. The 1.8 entry judged
that most of the residue settled things the document states no rule about, and reading it
showed that judgement was wrong: nearly all of them had a rule to sit at.
Was:
> 39 citations in the document, covering 29 of the 81 decisions then current
Now:
> 111 citations covering 85 of the 90 current decisions, placed at the sentence each rule rests
> on across sections 1, 2, 4, 6, 9, 10, 11, 12, 13, 14, 15, 17, 18, 19 and 20. Five stand
> uncited and each is named in the obligations table as a decision the document states no rule
> about
Why: a citation cannot be placed at a rule that does not exist, and the residue is now five
rather than a number nobody had read. The reading is what turned an estimate into a count.

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

### 2026-09-09 - ARCHITECTURE.html - the volume profile's bands, and the number the shelf threshold rests on
Corrects: a hole 3.0 did not find. Section 9.1 settled the window the profile accumulates over and never said how many bands it is divided into, and section 17's shelf threshold is stated as a multiple of an even share, which means a fifth with five bands and a fortieth with forty. The threshold rested on a number no document held, and 3.3 could not be built without it.

Was, in section 9.1:
> One window and not two: a shelf drawn from a different span would put a band on volume no other member of the level table can see, and its share of the period would be a share of a different period. (see: The volume profile accumulates over the same sixty sessions as the level window) Moving averages are computed from the full stored year

Was, in section 17's Volume shelf threshold row:
> derived from the worked example, where the main support band held about a fifth of the period's volume in a twentieth of its price range. One chart is not enough to fix a threshold, so phase 3 widens the fixture to four names of different character and the number is whatever makes all four agree with where their volume visibly clusters

Now: the same two passages, each stating the band count and citing the decision that settles it, with the spreading rule and the short-history rule added beside the window they belong to.

Why the band count is stated in that row rather than in a row of its own: a new row in section 17 is a new claim, and the count is read by nothing except this threshold. Stating it where the threshold is stated puts the two numbers a reader has to hold together in one cell, and it is the same move 3.0 made when it put the profile's window into the level window row rather than writing a second window row.

Why twenty: section 15.5 already describes a support band holding a fifth of the period's volume in a twentieth of its price range, so the figure was read out of the document rather than invented beside it. A fixed count rather than a fixed price width, because the threshold is one number for all five hundred names and a width in money puts three bands on a forty dollar name and three hundred on a four thousand dollar one.

### 2026-09-09 - BUILD_PLAN.md - the expectations owed for 3.0's rulings, discharged
Corrects: the row's producer cell, which named what 3.4 would do and could not yet say what it did.

Was:
> | **Phase 3's expectations owed for 3.0's rulings** | 3.0 | 3.4 | 3.4 builds the level builder, which is the first point at which all three of 3.0's rulings are assertable against the fixture: the shelf ruling decides what the builder collects, the retracement ruling decides what it draws between, and the profile window ruling is only observable where a band is built from a shelf. It was written as 3.1 by the pass that landed the rule, and 3.1 can assert none of the three: the indicator engine reads no profile, no swing and no shelf |

Now: the same row marked discharged, with the three places each ruling is read in the levels expectation named.

Why: the row's own rule is that a producer cell says what produces the evidence, and once the evidence exists the cell has to say where it is. The band it names, MSFT at 386.6219, is the one whose only anchor is a volume shelf, so it exists under the ruling 3.0 took and does not exist under the one it rejected. A discharge that pointed at the checkpoint rather than at the band would be a discharge nobody could check.

### 2026-09-09 - BUILD_PLAN.md - the volume shelf threshold, checked against four names
Corrects: the row's producer cell, which named the widening and could not yet say what the widening found.

Was:
> | **Volume shelf threshold checked against four names** | authored with the architecture | 3.6 | 3.6 widens the fixture to four names of different character, and the widening is the evidence |

Now: the same row marked discharged, with the measurement and the two candidates either side of the figure.

Why: the obligation was that one chart cannot fix a threshold, so a discharge that said the fixture is now four names wide would be a discharge that did not do the thing. The figure is asserted against its neighbours instead. At one even share, seven to ten of every name's twenty bands are shelves, which names most of the chart and discriminates nothing. At three, three of the four names have no shelf at all. At two, every name has one and it is a small minority of bands holding a fifth to a half of the period's volume. A threshold asserted alone agrees with itself; one asserted against its neighbours has to beat them.

### 2026-09-12 - BUILD_PLAN.md - the screens' parts stated in section 15 and not drawn, discharged
Corrects: the row's producer cell, which named what 5.8 would do and could not yet say what it did.

Was:
> | **The screens' parts stated in section 15 and not drawn** | 5.7 sign-off | 5.8 | nine parts of five section 15 rows are stated and not drawn: tonight's list's day change, trend state in a word and distance row mark; the night header's harness verdict; the selected name's level summary and the selection itself, which the composition fixes at the first row; the universe table's paging and its sessions until earnings; and the name page's twelve-month picture. Each is out of scope until 5.8, which draws them and has `read-surface` assert each off the markup. Filed at the fifth phase 5 sign-off review, whose blocking finding was that a row's parts were the reader's rather than the document's, so these nine sat under a PASS that covered the row |

Now: the same row marked discharged, naming for each part the thing the assertion reads it back against.

Why: the row's own rule is that a producer cell says what produces the evidence, and once the evidence exists the cell says where it is. Each of these nine is a claim about a surface, so a discharge saying the parts are drawn would be the discharge this row exists to refuse: what makes it true is that a check draws the surface and reads the part back off the markup, and the cell names what each is read against rather than that it is read.

### 2026-09-12 - BUILD_PLAN.md - 5.8's nine parts are ten claims, and the paragraph says which
Corrects: the checkpoint's own paragraph, which said nine parts and then listed ten items.

Was:
> and the app's selection is a thing the reader does rather than a position the composition fixes.

Now: the same clause, saying that this is 15.4's own statement of the part 15.7 already names, and that the nine parts carry ten claims.

Why: a claim is a row's part rather than a thing on a screen, and the selection is stated on two rows, so nine parts and ten claims are both right and the paragraph said only the first. A reader counting the list found ten against a stated nine, which is the shape `stated-counts` exists to catch and which that check cannot see here, the two numbers being about different populations. Written down rather than made to agree, because making them agree would mean calling the selection two parts or one claim, and it is neither.

### 2026-09-12 - ARCHITECTURE.html - the fundamentals fetcher's reads, at the checkpoint the cell promised
Corrects: the catalogue row's Reads cell and its own description, which named the archive as joining at 6.2 and now has to say what joining looked like.

Was:
> company financials feed, fundamentals | fundamentals | fetches the quarters and the balance sheet when the stored copy predates the name's latest filing, and marks the segment table and the guidance absent, which is what this provider files for nobody. The filings archive joins its reads at 6.2, which is the checkpoint that reaches an archive; the two parts it supplies were named here before either existed (see: Fundamentals are stored with the filing date they came from)

Now: the archive in the Reads cell, and a description saying that the two providers fail apart and what the guidance is stored as.

Why: the cell's own text promised the read at 6.2, and 6.2 has made it. What the promise did not say is what happens when one of two providers answers and the other does not, which is the part a component reading two feeds has to state: the archive's three parts are named as unread on the row and the other eight are stored, because refusing the fetch would lose eight figures to recover two.

### 2026-09-12 - SCHEMA.md - the fundamentals payload, with two providers behind one row
Corrects: the payload note, which described one provider's parts and said the archive would supply two more from 6.2.

Was:
> `payload` holds the quarter's figures, the balance sheet, and the margin computed from that filing's own revenue and gross profit, with `source` naming which of the three each part came from. The earnings bases, the valuation on each of them, and the next print's consensus estimate sit on the newest filing's row alone, because a ratio has a price in it and a price moves every session. `segments` and `guidance` are present and null: the company financials endpoint files neither for any name, so the absence is the provider's rather than this name's, and the filings archive supplies both from 6.2.

Now: the same two paragraphs, followed by what the second provider puts on the row, why its parts sit on the newest filing alone, and the three reasons `source` distinguishes.

Why: the note said `source` names which of three each part came from, and from 6.2 there are three reasons a part can be empty rather than three sources: the provider files it for nobody, the archive served none, and the archive was not read. A column that could not tell the third from the first would report a company with no segments after a failed fetch, which is the same class of fault as a blank cell reading as a zero. The `facts` part is new to the note because it is new to the row.
