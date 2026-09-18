---
paths: src/EquityBrief.Tests/**, docs/PROGRESS.md
---

# Writing an assertion

This is CLAUDE.md's own text, moved here word for word so it loads for the session that
needs it and costs nothing to a session working elsewhere. It states no rule CLAUDE.md did
not already state. Where the text below says "this file", it was written in CLAUDE.md and
means CLAUDE.md; where it points above or below, it points inside this file unless it names
something else.

Two rules the script mechanics in `scripts.md` point at live here: a green report being a
statement about the build, and nothing in the harness reaching `data/`.

## Verification

Rules that exist before anything has gone wrong, taken from what has gone wrong elsewhere:

- Greps over markdown must be whitespace-tolerant, and markup-tolerant over the span they match. A phrase written with emphasis where the writer wanted emphasis defeats a pattern built on a literal space.
- A sweep expecting a non-zero count states that count in advance. "Returns nothing" is self-validating; "returns 17" is not.
- A test proving a check works must be permanent, not a break-and-revert done by hand once.
- An assertion must fail when the thing it guards is removed, and the proof of that is permanent. A source scan that finds a pattern is not evidence the behaviour exists; a behavioural test that exercises the path is. Where both are cheap, write both and let the scan report coverage while the test carries the claim.
- A figure states the population it was computed over, in the same breath, and a figure over a mixed population is not stated at all. Population is the rows, the filter and the source together.
- A claim that something is visible is a claim about a surface. Where a property is asserted to be stated, recorded on every row, or shown, the assertion names the surface a person reads it on and checks that surface.
- A green report is a statement about the build and never about the running system. Where a property is about the running system, being what the store holds or what the night produced, it is asserted by a guard the code carries so the fault refuses instead of passing, and by a figure a person reads on the morning it happens.
- A guard over a population states which population, and where a check has two paths, they are one loop or the split is the thing asserted.
- A matcher keyed on a prefix answers about everything sharing that prefix. Where a key is the opening of a value rather than the whole of it, the property is that exactly one key matches, asserted in both directions rather than left to the order a dictionary happens to yield. This is a shape to sweep for rather than a defect to fix one instance at a time: it has arrived four times, as the nightly step keys, as a subject matched without its table, as a phase read as landed from any heading beginning with its number, and as a roster row retired by a heading whose entry said it was not a checkpoint.
- A bound on elapsed time is a bound on the machine unless it is calibrated against something the machine also produces. An absolute number asserts that the runner is at least as fast as the machine the test was written on, which is a claim about hardware wearing the clothes of a claim about behaviour. Where the absolute form is the bound being asserted, the assertion says so and says why. `two-platform` has caught this twice and both times on one test, raised from a quarter of a second to three and then made a multiple of a run the same machine timed, so raising the number is the habit the rule exists to stop.
- A surviving mutation is classified by what let it survive, and the classes are not equivalent. A **tautology** asserts a thing against itself and cannot fail. A **missing property** is one the code states and no test names. An **unreachable boundary** is asserted over a case the committed fixture cannot reach. An **unproducible shape** is asserted over a shape the data cannot take: the test is well formed and the world is not shaped that way, so the mutation is equivalent under an invariant nothing states. It has arrived twice, as a reference identity that value equality cannot break because merging leaves no two bands equal, and as a first match that a last match cannot break because stops are strictly decreasing. The first three are defects in the test. The last is not, and its remedy is the invariant written down and asserted where it holds, and, where the data is a provider payload, the shape assertion derived from a captured payload rather than from the shape the writer expected. Capture before parse, applied to assertions and not only to parsers.
- A mutation's stated rule names the property the mutation is trying to break, not the line it edits. A checkpoint that adds several properties says which it chose and why, and names the properties it added and did not mutate, because the unmutated ones are what the next sweep has to find. 4.4 added tranches, stops and conditions and its rule named where a stop sits, which satisfied the condition and left two of the three unmutated.

**Two of these are specific to this tool and worth naming separately.** A verification figure computed over listed names only is a figure over the wrong population, because a listings row exists for every name. And a check that reads the live store is a check whose result depends on last night, which is a different instrument from the one this corpus builds; nothing in the harness reaches `data/`.
