# CHANGELOG.md

The prior text of every clean edit to a spec. `changelog-reconciles` reads the git history and fails any commit that deleted a line from a spec without changing this file.

Specs are `CLAUDE.md`, `ARCHITECTURE.html`, `SCHEMA.md`, `BUILD_PLAN.md` and `RUNBOOK.md`. Records correct themselves with new dated entries and do not appear here.

Nothing in the corpus is struck through. A spec is edited cleanly and what it said before is written down here, with the decision that authorised the change.

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
