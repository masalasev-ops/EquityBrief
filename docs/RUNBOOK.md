# RUNBOOK.md

How the thing is operated. Written for the operator on the morning something looks wrong.

---

## What runs, and when

Two jobs. Neither is part of the application, because scheduling lives outside it and the code has to run unmodified on Windows and macOS.

| Job | When | What it does | Costs |
|---|---|---|---|
| `tools/nightly` | after the US close | the arithmetic: membership, bars, corporate actions, indicators, swings, volume profile, levels, trend, ladder, moves, listings, facts, forward returns, news pulse | one bulk bar request, one news feed request, a handful of calendar and membership calls. No model call |
| the overnight queue | after the arithmetic, same invocation | the local model writes the sections in the local lane for listed names whose research is missing or stale, in priority order, until the configured time limit | nothing |

**Every schedule is expressed in UTC.** The research provider's peak and off-peak windows are fixed in UTC, and a schedule written in local time moves into peak when daylight saving changes with nothing to announce it. Convert for display only.

**The overnight queue holds the machine awake while it works.** A laptop left to itself sleeps, and a nightly job that silently did not run is worse than no nightly job. The run page states the previous night's outcome including how many queued passes completed and how many were left.

**Both jobs are idempotent.** Running a night twice produces identical stored state and makes no additional model call.

---

## Providers, and what each is for

| Provider | Used for | Notes |
|---|---|---|
| EODHD | bulk end-of-day bars, index constituents, company fundamentals, the earnings calendar, ticker-tagged news | the daily allowance is 100,000 weighted calls; a normal night spends a few hundred |
| SEC EDGAR | filings, segment tables, the earnings press release that carries guidance, and a call transcript where a company files one | free, no key, and the primary document rather than someone's summary of it |
| Tavily | open web search, for theme material only | free tier is a thousand credits a month against a few hundred searches a year |
| DeepSeek | the research model | peak rates are double; peak falls late at night in Eastern time, so reading after the close is never billed at peak |
| a local model | prose, and the overnight queue's sections | on the operator's own machine, no marginal cost |

**Per-name material never uses search.** A ticker-tagged feed is exhaustive over a date range and cannot return the wrong company. Search is for industry material, where the query names the industry rather than a ticker.

### Call weights worth knowing

Bulk end-of-day for an entire exchange costs 100. A single-ticker historical request costs 1, which is why the backfill runs per ticker rather than replaying past sessions through the bulk endpoint: 500 calls against 25,000 for the same data. Fundamentals cost 10 per ticker. News costs 5. The index constituents come through the fundamentals endpoint and cost 10.

---

## Secrets

`appsettings.Secrets.json` sits beside `appsettings.json` in each project that needs one. Gitignored, plaintext, never committed. Registered **before** environment variables so an environment variable still wins, which is the ordering that lets a runner override a local file rather than the reverse.

Moving to a new machine: copy the checkout, copy the store file, write the secrets file by hand. The secrets file is the one part of the move that is a human act and cannot be scripted.

---

## Moving the installation

The whole system is a checkout and one database file.

1. Clone the repository on the new machine.
2. Install a .NET SDK in the `10.0.3xx` band, which is what `global.json` pins and what the six projects need to build against `net10.0`. Without one the run fails at step 5 with a restore error that names neither.
3. Copy `data/equitybrief.db` into the configured data root.
4. Write `appsettings.Secrets.json` in each project that needs one.
5. Run `tools/migrate` and confirm it reports no pending migrations.
6. Run `tools/ci` and confirm green.
7. Register the schedule with the platform's scheduler, in UTC.
8. Run `tools/nightly` by hand once and read the run page before trusting the schedule.

**Re-measure the local and paid boundary after a hardware change** rather than carrying the previous setting over. How much of a research pass runs locally is set by how much context the local model can hold, which is a property of the machine.

---

## When something looks wrong in the morning

Read the run page first. It states what ran, how long, what was spent, how many names were stale, and what failed in which component.

| Symptom | Likely cause | What to do |
|---|---|---|
| Tonight's list is absent and a banner gives an old data date | the bulk price feed did not answer | nothing; the design keeps last night's bars and refuses to compute a list from them. Check the provider's status, then re-run `tools/nightly` |
| One name's chart shows a gap and its plan says not computed | a gap in that name's series | expected behaviour, not a fault. An interpolated bar would produce averages and swings that never happened. It clears when the provider fills the session |
| A name is marked suspect | the corporate action check itself failed | re-run the night. If it recurs, the name's adjusted history and the provider's have diverged and the year needs a manual refetch |
| The run page says the queue did not run | the machine slept | expected to be visible rather than silent. Listed names open without a draft, as normal |
| A research section is absent with a line saying no admissible source was found | every candidate document failed admissibility | not a fault. Writing the section from a price forecast or a year-old article would be worse than the gap |
| A section says fallback | the model was unreachable, or the claim checker rejected twice | the computed report is complete and useful on its own. The run log names the offending text |
| Research is paused | the spend cap was reached | it resumes at the start of the next period. The cap is a hard stop by design |
| The phase report is green but the lab did something wrong last night | a green report is a statement about the build and never about the running system | the two are different subjects. Nothing in the harness reaches `data/`, and a property about the running system is asserted by a guard the code carries or read on the morning it happens |

---

## Store maintenance

**Never edit the store by hand.** Every table has one declared writer per operation and `writer-ownership` asserts it in both directions; a hand edit is a write nobody declared.

**Back it up by copying the file.** No row holds an absolute path, so the copy works anywhere.

**A migration is applied by `tools/migrate` and never by an application start-up path.** A store that migrates itself when the app runs will migrate on a machine the operator did not intend, and a stage failing on a missing column is how that is discovered.

---

## What this system does not do

It does not trade, hold a position, or size one. It does not rank names against each other. It does not tell you a stock will go up. The list says a name reached a price where the plan says something, and the plan is arithmetic over levels the chart has visited.
