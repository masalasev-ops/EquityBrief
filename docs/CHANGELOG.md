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
