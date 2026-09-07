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
