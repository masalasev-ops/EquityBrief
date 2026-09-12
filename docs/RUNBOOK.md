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

### Registering the schedule

Until 5.7 this section said to register the schedule with the platform's scheduler, which is an instruction and not a command, and nothing was ever registered. A night that nobody scheduled produces no evening of observation however long anyone waits for one, so the two figures that were waiting on a week of nights waited on this instead.

**The instant is 23:30 UTC.** The close is 20:00 UTC in summer and 21:00 in winter, and the bulk file posts after it, so this leaves the provider a margin at both ends of the year (see: The night runs at a fixed UTC instant, moved only when a night finds the day's file not yet posted). It stands while nights succeed. A night that runs before the file is posted refuses at the fetch, because the file carries none of the index, and that refusal is on the run page's stale-and-failed region the next morning. The session is not lost: the next night fetches it as a missed session before its own. That refusal is the only evidence that moves the instant, and it moves it later (owes: The night's instant moved later when a night finds the day's file not yet posted). Nights that found the file already there do not move it earlier, because they say only that it was there by then.

**Windows.** Task Scheduler triggers fire in local time unless the trigger's own start boundary carries a zone, which is the trap this repository's UTC rule exists for: a task registered at a local hour walks an hour relative to the provider when daylight saving changes, and it walks into the wrong side of the close twice a year. Setting `StartBoundary` to an instant with a `Z` is what pins it, and it is the same setting the interface calls synchronizing across time zones. Task Scheduler rewrites the `Z` into the machine's own offset when it stores the task, so reading it back shows `2026-09-10T19:30:00-04:00` rather than the instant that was handed in. That is the same instant and still the pinned form: what matters is that the boundary carries an offset at all, since a boundary with none is a local wall-clock time and is the one that walks.

```powershell
$root    = 'E:\Stock Analysis  Tool Ideas\EquityBrief'
$action  = New-ScheduledTaskAction -Execute 'powershell.exe' `
             -Argument "-NoProfile -ExecutionPolicy Bypass -File `"$root\tools\nightly.ps1`"" `
             -WorkingDirectory $root
$trigger = New-ScheduledTaskTrigger -Weekly -DaysOfWeek Monday,Tuesday,Wednesday,Thursday,Friday -At 6pm
$trigger.StartBoundary = '2026-09-11T23:30:00Z'
$settings = New-ScheduledTaskSettingsSet -WakeToRun -StartWhenAvailable `
             -DontStopIfGoingOnBatteries -AllowStartIfOnBatteries `
             -ExecutionTimeLimit (New-TimeSpan -Hours 2)
Register-ScheduledTask -TaskName 'EquityBrief nightly' -Action $action `
             -Trigger $trigger -Settings $settings
```

`-WakeToRun` because a laptop left to itself sleeps and a nightly job that silently did not run is worse than no nightly job. `-StartWhenAvailable` because a machine that was off at the instant should run the night when it comes back rather than skip it, and the night is idempotent.

**Weekdays only, because the exchange trades on weekdays.** 23:30 UTC is 19:30 or 18:30 in New York, so the UTC weekday and the session's weekday are the same day at both ends of the year. A night that does run on a day with no session, being a holiday or a night started by hand, asks the closure table first, writes one run log row naming the day, fetches nothing and exits 0 (see: A night on a day the exchange did not trade fetches nothing and exits clean). So the weekday trigger saves the weekend's rows and the night does not depend on it. A task registered daily before the phase 5 sign-off is moved to weekdays in place, keeping its action, settings and principal:

```powershell
$trigger = New-ScheduledTaskTrigger -Weekly -DaysOfWeek Monday,Tuesday,Wednesday,Thursday,Friday -At 6pm
$trigger.StartBoundary = '2026-09-11T23:30:00Z'
Set-ScheduledTask -TaskName 'EquityBrief nightly' -Trigger $trigger
```

Reading it back shows `DaysOfWeek` as 62, being Monday to Friday as bits.

**Registered from an ordinary shell that task runs only while the account is logged on,** which is the one thing about it that is invisible. With no principal given, Task Scheduler stores `InteractiveToken`, and a locked screen still counts as logged on while a sign-out or a restart that nobody signed back into does not. So the machine that has been running nights for a month stops on the morning after an update reboot, and the run page shows the night before.

The durable form names a principal, and it needs an elevated shell because logging a task on without a session is a batch logon and granting one is an administrator's to grant. From an elevated PowerShell, add to the registration above:

```powershell
-Principal (New-ScheduledTaskPrincipal -UserId $env:USERNAME -LogonType S4U -RunLevel Limited)
```

Confirm which one is registered, because `Get-ScheduledTask` shows both as `Ready` and the difference does not appear in the task list:

```powershell
([xml](Export-ScheduledTask -TaskName 'EquityBrief nightly')).Task.Principals.Principal.LogonType
```

`S4U` runs whether or not anyone is logged on and stores no password. `InteractiveToken` is worth keeping only on a machine that is never signed out of, and on that machine it is worth knowing it is what you have. Remove either with `Unregister-ScheduledTask 'EquityBrief nightly'`.

**macOS.** `launchd` has no UTC option at all: `StartCalendarInterval` is local time and there is no zone field. So the job is registered to run every hour and the script itself decides, which is the only form that holds the UTC rule across a daylight saving change without anyone editing a plist twice a year.

```xml
<!-- ~/Library/LaunchAgents/dev.equitybrief.nightly.plist -->
<plist version="1.0"><dict>
  <key>Label</key><string>dev.equitybrief.nightly</string>
  <key>ProgramArguments</key>
  <array>
    <string>/bin/bash</string>
    <string>-lc</string>
    <string>[ "$(date -u +%u)" -le 5 ] &amp;&amp; [ "$(date -u +%H%M)" = "2330" ] &amp;&amp; exec "$HOME/EquityBrief/tools/nightly"</string>
  </array>
  <key>StartCalendarInterval</key><dict><key>Minute</key><integer>30</integer></dict>
  <key>RunAtLoad</key><false/>
</dict></plist>
```

Load with `launchctl load ~/Library/LaunchAgents/dev.equitybrief.nightly.plist`, and confirm with `launchctl list | grep equitybrief`.

**The source defaults to live, so neither needs setting for a live night.** `EquityBrief:Providers:Source` is worth reading before registering anything only because a machine that has been replaying a capture carries it as `fixture`, and a scheduled night would go on replaying that capture every evening, storing the same session forever while the run page shows a night that worked. What a live night does need is the key: with no `EquityBrief:Providers:Eodhd:ApiKey` beside the worker it refuses by name rather than reaching the provider anonymously.

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

Bulk end-of-day for an entire exchange costs 100. A single-ticker historical request costs 1, which is why the backfill runs per ticker rather than replaying past sessions through the bulk endpoint: 500 calls against 25,000 for the same data. Fundamentals cost 10 per ticker. News costs 5 per page, and one dated query for a whole session takes more than one page: 2.5 measured a single request coming back at the provider's cap of 1,000 articles, so the day is paged and every page is counted. The page count follows how much news the market made that day and not how many names the universe holds. The index constituents come through the fundamentals endpoint and cost 10. The earnings calendar costs 1 for a whole window, measured at 4.3 against the account's own request counter rather than read from documentation: one request over ninety days returned 22,526 rows worldwide and moved the counter by one.

---

## Secrets

`appsettings.Secrets.json` sits beside `appsettings.json` in each project that needs one. Gitignored, plaintext, never committed. Registered **before** environment variables so an environment variable still wins, which is the ordering that lets a runner override a local file rather than the reverse.

Moving to a new machine: copy the checkout, copy the store file, write the secrets file by hand. The secrets file is the one part of the move that is a human act and cannot be scripted.

**The key names, because a file written by hand needs them written down.** This section said to write the file and never said what to put in it, so the first one written by hand used a name of its own and the code did not find it. The name is the configuration path, colon separated, and it nests in the file:

```json
{
  "EquityBrief": { "Providers": { "Eodhd": { "ApiKey": "..." } } }
}
```

| Provider | Key | Which projects need it |
|---|---|---|
| EODHD | `EquityBrief:Providers:Eodhd:ApiKey` | `EquityBrief.Worker` |

The same path works as an environment variable, with a double underscore for each colon, and an environment variable wins. A blank or missing key is refused by name at startup rather than reaching the provider as an anonymous request, because a rejection from the provider names nothing.

---

## Moving the installation

The whole system is a checkout and one database file.

1. Clone the repository on the new machine.
2. Install a .NET SDK in the `10.0.3xx` band, which is what `global.json` pins and what the six projects need to build against `net10.0`. Without one the run fails at step 6 with a restore error that names neither.
3. On Windows, install Git for Windows, which is where `bash` comes from. Every bash entry point in `/tools` ships with a `.ps1` beside it and that wrapper hands the work to the one script, so a machine with no usable bash cannot run `tools/ci` or `tools/verify-phase`. It does not need to be on `PATH`: the wrapper looks beside `git` as well, because a default install puts `git.exe` in `cmd\` and `bash.exe` in `bin\` and only the first goes on `PATH`. What it must not be is the WSL launcher, which is on `PATH` as `bash` on any machine with the feature enabled and cannot open a Windows path; the wrapper passes over it by asking each candidate whether it can read the script.
4. Copy `data/equitybrief.db` into the configured data root.
5. Write `appsettings.Secrets.json` in each project that needs one.
6. Run `tools/migrate` and confirm it reports no pending migrations.
7. Run `tools/ci` and confirm green.
8. Register the schedule, by running the command for the platform under "Registering the schedule" above rather than by finding one. Confirm the task exists before moving on.
9. Run `tools/nightly` by hand once and read the run page before trusting the schedule.

**Re-measure the local and paid boundary after a hardware change** rather than carrying the previous setting over. How much of a research pass runs locally is set by how much context the local model can hold, which is a property of the machine.

---

## When something looks wrong in the morning

Read the run page first. It states what ran, how long, what was spent, how many names were stale, and what failed in which component.

| Symptom | Likely cause | What to do |
|---|---|---|
| Tonight's list is absent and a banner gives an old data date | the bulk price feed did not answer | nothing; the design keeps last night's bars and refuses to compute a list from them. Check the provider's status, then re-run `tools/nightly` |
| One name's chart shows a gap and its plan says not computed | a gap in that name's series | expected behaviour, not a fault. An interpolated bar would produce averages and swings that never happened. It clears when the provider fills the session |
| A name is marked suspect | the corporate action check itself failed | re-run the night. If it recurs, the name's adjusted history and the provider's have diverged and the year needs a manual refetch |
| A night is missing from the run page altogether | the night was refused before it knew where the store is | the store is the only record this system keeps, so a refusal that happens before the data root resolves cannot be written to it. Read the scheduler's own history: Task Scheduler's `Last Run Result` on Windows, `launchctl list` on macOS. Every refusal after that point does write a row, under the first step with an outcome of `refused` rather than `failed`, so a refused night and a failed migration are different lines on the page |
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

It does not trade, hold a position, or size one. It does not rank names against each other. It does not tell you a stock will go up. The list says a name is sitting at a price its own chart has made significant, and the levels and the plan are both arithmetic recomputed from that chart the same evening.
