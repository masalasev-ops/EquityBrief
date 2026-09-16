---
paths: docs/**
---

# Editing the corpus

This is CLAUDE.md's own text, moved here word for word so it loads for the session that
needs it and costs nothing to a session working elsewhere. It states no rule CLAUDE.md did
not already state. Where the text below says "this file", it was written in CLAUDE.md and
means CLAUDE.md; where it points above or below, it points inside this file unless it names
something else.

The conventions that bind a session working anywhere stayed in CLAUDE.md: the prose
convention, the commit subject, which checkpoint a commit belongs to, the planning pass, and
anything issued in conversation landing in the repo when it is issued.

## Corpus editing conventions

**Decisions are named, not numbered.** A decision belongs in `DECISIONS.md` when a later session could reasonably choose differently **and** the wrong choice would be invisible. Mechanisms with one obvious implementation, and anything a test already enforces, do not.

A decision is identified by its bold name in `DECISIONS.md`. Cite the exact name. In code: `// see: Code owns every number`. In a document: `(see: Code owns every number)`. Same string either way, so one checker covers both. A number tells a reader nothing and forces a lookup; a name tells them the thing directly. A misremembered name fails to resolve, where a misremembered number resolves to the wrong decision and nobody notices. Decision names carry no terminal punctuation, because a name ending in a period is awkward to cite and invites the paraphrase `decision-resolves` exists to reject.

**A deferral names what produces the evidence, not a phase.** A carried obligation or a deferred question names what produces the evidence it waits on, in one of two forms and never neither. The first names a checkpoint that produces that evidence, and that checkpoint's own text cites the obligation by name. The second is for a deferral no checkpoint produces: it states a numeric trigger, the surface the trigger is read on, and the checkpoint that builds that surface. A deferral to a phase is a guess about when evidence appears and it fails in both directions: early, where the evidence is already in hand and the corpus keeps a guess it could have replaced, and never, where the named point is a report or a page rather than the thing that measures. The truncation rule at 2.3 was the first kind, naming phase 5 when the evidence arrived three checkpoints earlier on the first live night, and the rule as written would have refused every night.

**A done condition may not require calendar time.** Evidence that only accumulates, being nights that ran, setups that resolved, or documents that arrived, is produced by the system operating and by no checkpoint. A checkpoint that waits for it stops the build for the length of the wait, produces nothing during it, and cannot end it from inside a session, because the only thing that ends it is the calendar. Such evidence is carried in the second deferral form above, and the checkpoint keeps the half it does produce. Written the other way it also hides a second fault: 5.7 read as a week of unattended nights while nothing was registered with any scheduler, so the wait was not a wait for evidence at all and would not have ended on its own. Where a done condition needs the system to have run, what it requires is the procedure written down as a command, not the operator having got around to it.

**Obligations are named and cited, as decisions are.** An obligation is identified by its bold name in `BUILD_PLAN.md`'s carried obligations table. Cite the exact name. In code: `// owes: Volume shelf threshold checked against four names`. In a document: `(owes: Volume shelf threshold checked against four names)`. Same string either way, so one checker covers both, and the marker is what makes the reverse direction assertable: without one, a checkpoint announcing an obligation cannot be told from prose that happens to use the same words. The example names a real obligation rather than a placeholder, because a passage describing a citation form is a passage containing one, and exempting the placeholder is how `decision-resolves` came to carry an exemption for a shape the corpus no longer had. Obligation names carry no terminal punctuation, for the reason decision names do not. `obligation-reconciles` asserts both directions and asserts the split between the two forms, read from the cells rather than from what a row calls itself, because a row carrying a checkpoint due point and a numeric trigger reads as tracked from either end and is chased from neither.

**A decision is changed only by another decision.** No finding, progress entry, checkpoint note or conversation supersedes one. Work that changes a decision writes a new one, names what it supersedes, and moves the old entry to "Previously decided" in the same commit, reasoning intact.

**Nothing in the corpus is struck through.** A spec is edited cleanly and its prior text goes to `CHANGELOG.md`. A record is corrected by a new dated entry naming what it corrects. Strikethrough leaves a document that is half history and half current state, and the reader has to work out which is which on every line.

**Components are named, not coded.** The catalogue lives in `ARCHITECTURE.html` under "Component catalogue", and a new component is added there in the same commit that introduces it.

**Headings in ARCHITECTURE.html carry numbers and everything else does not.** That document is read section by section by a person and by the conformance check, and its numbers are navigation. Cross-document references cite heading text, not numbers, because a misremembered number resolves to the wrong place and nothing notices. Checkpoint identifiers keep their numbers, because they name work in a sequence where the sequence is the point.
