---
paths: tools/**
---

# The scripts and what they verify

This is CLAUDE.md's own text, moved here word for word so it loads for the session that
needs it and costs nothing to a session working elsewhere. It states no rule CLAUDE.md did
not already state. Where the text below says "this file", it was written in CLAUDE.md and
means CLAUDE.md; where it points above or below, it points inside this file unless it names
something else.

The command table these paragraphs describe stayed in CLAUDE.md, under Commands, with the
target framework, the PowerShell parity sentence and the secrets paragraph. The rule about a
running system that the report paragraph points at is in `writing-tests.md`.

## The scripts and the wrapper contract

**The Shell column is there because a cell naming a script does not say what can run it, and the wrong shell fails quietly in one direction.** Calling an extensionless bash script by name from PowerShell produces no output, leaves `$LASTEXITCODE` unset and leaves `$?` true, so a gate that never executed is indistinguishable from one that passed. Every bash entry point in this repository therefore ships with a `.ps1` wrapper that finds a bash, hands the work to the one script rather than reimplementing it, and exits with a named message where the machine has none. A wrapper must return both the script's output and its exit code; a PowerShell function's return value is its output stream, so returning `$LASTEXITCODE` from a function swallows everything the script printed.

**`tools/verify-phase` is what a phase signs off against.** It runs the suite, which is what replays the stages that exist over the committed fixture and diffs each one's output against expectations derived from the rules, and it writes what every test did. Then it parses `docs/ARCHITECTURE.html`'s tables and gives every claim in scope a verdict by reading that result: a claim is PASS only where the check reaching it ran and held, FAIL where that check ran and did not hold, and UNEXAMINED where it did not run. It writes `artifacts/phase-report.html` for the operator and `artifacts/phase-report.json` for a build session. A phase is not done until that report is green, and green means no claim failed, none is unexamined, no carried check failed or went unrun, and the run behind it was clean.

**The report is one instrument reading another, and the seam is where it went wrong once.** The tool ran no check from 0.5, and from 0.7, where the first verdict map arrived, it printed PASS from a name: it read a map naming the instrument that reaches each claim and never asked whether that instrument had run, so `Verdict.Fail` was assigned nowhere and the "fail 0" line was structural rather than measured. A tree with five failing tests produced an identical verdict block and the same green exit. What a green report says is that no claim in scope failed and none went unexamined. It says nothing about the claims out of scope, which are not checked at all, and nothing about a running system, which is the separate rule below.

**`tools/ci.*` is not a wrapper around `dotnet test`.** It runs every step of the CI workflow in order against a dropped store, exiting non-zero on the first failure. A green `dotnet test` does not satisfy done condition 2.

**The store it drops is `/data-ci` and never `/data`.** Both were `/data` until 5.7, so verifying a checkpoint deleted the store the nightly job fills, and the next scheduled night ran a first-run backfill of the whole index with nothing saying why. The scripts export a data root of their own, so the operator's store is not a path they know rather than one they are trusted not to use. This is the same rule as nothing in the harness reaching `data/`, which was true of the suite and false of the two scripts that run it.
