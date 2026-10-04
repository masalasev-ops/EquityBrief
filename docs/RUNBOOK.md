# RUNBOOK.md

How the thing is operated. Written for the operator on the morning something looks wrong.

---

## What runs, and when

Five steps a night, from one scheduled invocation. None is part of the application, because scheduling lives outside it and the code has to run unmodified on Windows and macOS.

| Job | When | What it does | Costs |
|---|---|---|---|
| `tools/nightly` | after the US close | the arithmetic: membership, bars, corporate actions, indicators, swings, volume profile, levels, trend, ladder, moves, swing readings and the night's breadth, the readings of each member's reported quarters and the state they give it, listings, the swing filter, the shape proposal, facts, forward returns, news pulse | one bulk bar request, one news feed request, one request each for the index's and the VIX's daily series, a handful of calendar and membership calls. No model call |
| the quarters step | after the arithmetic, same invocation | asks the provider for the reported quarters of the members that reported since the last night, of those whose new quarter was not yet posted, of a member joining the index, and of every member once at the start, at most 260 a night, starting no ask once its own limit of 15 minutes has passed or an ask would take the night past the day's allowance | 11 weighted calls a member whose answer stores a quarter and 10 where it does not; no model call |
| the overnight queue | after the quarters step, same invocation | the local model writes the local lane's sections that rest on no document, for every name in the index whose research is missing or stale, the listed names first, starting no pass once the configured hours have passed | nothing, and no request |
| the news labeller | after the close, started by the night as a process of its own, as the report request's drain is | labels the stored news of the names on tonight's list through the news job's paid model, one call an article: the admitted articles of the thirty days before the night, newest first and at most twenty a name, the ones that profile has not labelled, under a run of its own | one paid call an article on the news job's profile, bounded by its own month limit and time limit and by the day and month caps; nothing on the night's own rows |
| the store's copy | after the labeller is started, started by the night as a process of its own | waits until the night, its drain and its labeller have finished, copies the store into the copies' folder, opens and reads the copy and keeps the newest three, under a run of its own | no call and no request; about half a gigabyte a copy on the copies' disk |

**Every schedule is expressed in UTC.** The research provider's peak and off-peak windows are fixed in UTC, and a schedule written in local time moves into peak when daylight saving changes with nothing to announce it. Convert for display only.

**The overnight queue holds the machine awake while it works.** A laptop left to itself sleeps, and a nightly job that silently did not run is worse than no nightly job. On Windows it takes a power request and on macOS a power assertion, released when the queue ends, and on any other machine it takes none; the queue's row on the run log says which. The run page states the night's outcome, including how many queued passes completed and how many were left, and names every traded session since the queue last ran on which it did not run (see: A night the overnight queue did not run is a traded session with no queue row, read on the run page against the exchange calendar).

**All five are idempotent.** Running a night twice produces identical stored state, makes no additional model call and asks for no member's quarters twice, a labeller run twice asks for no article twice under one profile, and a copy run twice makes a second copy, keeps the newest three and changes nothing in the store beyond its own row. A night run again for an earlier session with `--session` asks for no quarters at all, since what it would store is today's answer and not that night's, and starts no labeller, since the labels it would write are tonight's.

**Each night is built from a clean copy of the committed code, never from the main checkout's working tree.** `tools/nightly` reads the main checkout's own commit with no fetch and refuses a checkout off main or holding a commit origin/main does not have, before any worker exists, writing why to `night.refused` under the data root, which tonight's page and the run page read as the night's state for that evening, and exiting non-zero so the scheduler records the failure. Otherwise it exports that commit whole with `git archive` into `data/nights/<commit>/`, builds it once in Release and runs the night from it, with `EquityBrief__DataRoot` at the checkout's `data` and `EquityBrief__SecretsFile` at the checkout's `src/EquityBrief.Worker/appsettings.Secrets.json`, so the copy holds no secret and no store. A copy already built is run as it is, and one no night has used for a week is removed. The night records the commit on the run log under the stage `build`, which the run page shows, and `night.build` names it for `--resume`, the resume press and the report's drain, which run that build and never a newer one. A checkout behind origin/main runs and the line says by how many commits; an uncommitted edit or an untracked file is never built and costs no night. `tools/nightly --check` prints what a run would build and run, and runs nothing (see: Each night is built from a clean copy of the main checkout's own commit and never from its working tree, and refuses only a checkout off main or ahead of the remote's main).

**The quarters step fills every member once, over its first two nights.** On the first night it asks for 260 members in ticker order after any member that reported, and on the second the rest, so the index holds quarters by the third night's readings: a night reads only the quarters fetched on the nights before it, so the first night's list reads every member as no fundamentals yet and is drawn in the swing filter's own order, the second night's reads the first 260, and the third's reads every member the provider answered for. The quarters step's line in the run page's operational header counts each night's asks by why each was made and what came of it, and the members of the fill still owed. When the fill first owed none, on 2026-09-27, the measured split of the states, of beats, meets and misses and of the earnings quality bands was read off the store and put to you, and you ruled it: both signals for the state, a cent or 1% for a met estimate, and 1.0 and 2.0 for the quality's cut points, which the candidate that skips a deteriorating business freezes when it is registered (see: Four readings of a member's reported quarters are worked out every night by rules the measured split settled, and its state is read from sales and operating margin alone). After the fill a member is asked on the night after it reports, which the calendar says is about four times a year, so an ordinary night asks for a handful and a peak reporting night for up to a hundred or so (owes: The quarters fetch measured on a peak reporting night).

**The quarters step can be run by hand, to take the fill in one sitting rather than over two nights.**

```
dotnet run --project src/EquityBrief.Worker -- quarters --live
```

It is the night's own step, outside the night: the members due, asked for as the night asks for them, each ask dated by the session the clock falls on, bounded by the step's own limit and by the day's allowance. Run again the same day it asks no member that day already asked and takes the next 260 of the fill, so two runs fill the whole index, each ask costing what the night's does. Its rows on the run log are drawn on the run page as run by hand, apart from any night's stages. Run it when no night is running, and the next night's readings read what it stored.

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
             -ExecutionTimeLimit (New-TimeSpan -Hours 8)
Register-ScheduledTask -TaskName 'EquityBrief nightly' -Action $action `
             -Trigger $trigger -Settings $settings
```

Eight hours because a night that stops is tried again from the step that stopped, three more times fifteen minutes apart, each try under a deadline of its own, and the overnight queue and the report follow the close, so a limit of two would stop the process in the middle of a try (see: A night that stops before its close is tried again from the step that stopped, three more times fifteen minutes apart, each try under a deadline of its own). A task registered with the earlier limit of two hours takes the new one from an elevated PowerShell:

```powershell
$t = Get-ScheduledTask -TaskName 'EquityBrief nightly'
$t.Settings.ExecutionTimeLimit = 'PT8H'
Set-ScheduledTask -InputObject $t
```

Reading it back with `(Get-ScheduledTask -TaskName 'EquityBrief nightly').Settings.ExecutionTimeLimit` shows `PT8H`.

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

## Opening the screens

The screens are one page the read surface serves from the store the night writes. From the repository root, in a terminal left open:

```
dotnet run --project src/EquityBrief.Api -c Release
```

then open `http://localhost:5152/` in a browser, the address `src/EquityBrief.Api/Properties/launchSettings.json` gives it. The page's own links reach every screen: tonight's list at `#/`, the index at `#/universe`, one name's page and report at `#/name/<TICKER>`, and one night's run page at `#/run/<YYYY-MM-DD>`. Ctrl+C in the terminal stops it.

**It runs from a Release build of `main`.** A branch is worked on in this same checkout, and its gates and tests build Debug, so a build for a branch never rewrites the files the page runs from. After a merge the checkout is back on `main`, and the page is stopped and started again the same way to serve what merged.

**It reads the store the worker writes.** Its settings name the data root the worker's do, `data`, and it reads them from the settings file beside its own build, as the worker does. A relative root is read against the checkout the build sits in rather than the directory it was started from, because started this way it runs in its project directory, so the store it opens is the checkout's `data/equitybrief.db` however it is started, and so is the phase report the run page reads. `EquityBrief__DataRoot` in the environment overrides the setting, as it does for the worker, so a store kept elsewhere is named the same way for both.

**It can stay open while the night runs.** It writes one run log row when it starts and a research pass only when the name page's control is pressed, and otherwise reads. A page loaded while a night is running shows that night as far as it has got, and a reload once the run page shows the night closed shows all of it. After updating the checkout, run `tools/migrate` before starting it, as "Store maintenance" below says; until then every screen names the store's schema and the checkout's rather than drawing.

---

## Providers, and what each is for

| Provider | Used for | Notes |
|---|---|---|
| EODHD | bulk end-of-day bars, index constituents, company fundamentals, the earnings calendar, ticker-tagged news | the daily allowance is 100,000 weighted calls; a normal night spends a few hundred |
| SEC EDGAR | filings, segment tables, the earnings press release that carries guidance, and a call transcript where a company files one | free, no key, and the primary document rather than someone's summary of it |
| Tavily | open web search, for theme material only | free tier is a thousand credits a month; a theme pass makes twelve searches, one a site of the industry list, at one credit each at the basic depth, so a few hundred passes a year come to a few thousand searches |
| the research model's provider, DeepSeek as shipped | the research model | named in configuration and nowhere in the code, so another provider is a change of settings; the shipped one's peak rates are double and its peak falls late at night in Eastern time, so reading after the close is never billed at peak |
| a local model | prose, and the overnight queue's sections | on the operator's own machine, no marginal cost |

**Per-name material never uses search.** A ticker-tagged feed is exhaustive over a date range and cannot return the wrong company. Search is for industry material, where the query names the industry rather than a ticker.

### Call weights worth knowing

Bulk end-of-day for an entire exchange costs 100. A single-ticker historical request costs 1, which is why the backfill runs per ticker rather than replaying past sessions through the bulk endpoint: 500 calls against 25,000 for the same data. Fundamentals cost 10 per ticker, and the quarters step's ask costs 11 where it stores a quarter, the fundamentals and one request for three years of the member's closes, and 10 where the answer does not yet carry the quarter awaited. News costs 5 per page, and one dated query for a whole session takes more than one page: 2.5 measured a single request coming back at the provider's cap of 1,000 articles, so the day is paged and every page is counted. The page count follows how much news the market made that day and not how many names the universe holds. The index constituents come through the fundamentals endpoint and cost 10. The earnings calendar costs 1 for a whole window, measured at 4.3 against the account's own request counter rather than read from documentation: one request over ninety days returned 22,526 rows worldwide and moved the counter by one.

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
| SEC EDGAR | `EquityBrief:Providers:SecEdgar:Contact` | `EquityBrief.Worker` |
| DeepSeek, the `deepseek` profile's key | `EquityBrief:Models:Research:ApiKey` | `EquityBrief.Worker` |
| Claude, the key every Claude profile names | `EquityBrief:Models:Claude:ApiKey` | `EquityBrief.Worker` |
| Claude's workspace, where the key is not scoped to one | `EquityBrief:Models:Claude:WorkspaceId` | `EquityBrief.Worker` |
| Tavily, the search tool | `EquityBrief:Providers:Tavily:ApiKey` | `EquityBrief.Worker` |

**The archive's row is a contact and not a key, and it is written here for the same reason the key is.** The archive needs no key and refuses a request that names no user agent, and its fair-access policy asks that the agent carry contact details, so the setting is what a request declares about this installation rather than what authorises it. A blank one refuses at startup for the reason a blank key does. Put a dedicated address there, an alias or a plus-addressed variant rather than a personal mailbox: the value goes out in the header of every archive request for the life of the installation, and it sits in this file beside the keys, where anything identifying a person is one more thing that must never reach a captured fixture (see: The archive declares a contact in its user agent, and a blank one refuses at startup).

The same path works as an environment variable, with a double underscore for each colon, and an environment variable wins. A blank or missing key is refused by name at startup rather than reaching the provider as an anonymous request, because a rejection from the provider names nothing.

### The local model's settings

The local model takes settings and never a key. Each has a default measured on the machine this was first built for, so a blank file runs; set one where the machine or the runtime differs. They are configuration rather than secrets and may sit in either file.

| Setting | Key | Default |
|---|---|---|
| where the runtime answers | `EquityBrief:Models:Local:BaseAddress` | `http://127.0.0.1:1234/v1/` |
| which model answers | `EquityBrief:Models:Local:Model` | `qwen/qwen3.5-9b` |
| how long one section call may take, in seconds | `EquityBrief:Models:Local:TimeoutSeconds` | `300` |
| the context the model is loaded with, in tokens | `EquityBrief:Models:Local:ContextTokens` | `50176` |
| the sections the local lane holds | `EquityBrief:Models:LocalLane`, one entry per section in figure 12.2's own names | what the company sells, the segment commentary, the key under each figure |

**Set the context to what the runtime reports for the loaded model**, not to what the model could hold. A section whose prompt and answer would not fit is refused before any call and left for the paid path, and the run log names it with the estimate it was refused on; a context set above what is loaded lets the call go out, and the runtime refuses it with a 400 naming its own count instead. A value that is not a whole number is refused rather than read as the default.

**A key at `EquityBrief:Models:Local:ApiKey` is refused, in either file and on a fixture run as well as a live one.** A local model that asks for a key is a model on somebody else's machine, and the overnight queue is allowed to call this lane because it costs nothing (see: The local model answers at an OpenAI-compatible endpoint, and which model answers is configuration).

A lane naming a section figure 12.2 does not name is refused when the lane is read, and so is one naming a section twice.

### The overnight queue's settings

The queue runs in the same invocation after the arithmetic has closed and recorded its counts, and before the night's own request, over the local model and nothing else. It writes the local lane's sections that rest on no document, which in this machine's lane is the key under each figure: what the company sells and the segment commentary rest on the company's own filing, which the pass an open starts fetches, and the night fetches nothing for a name (see: The overnight queue writes the local lane's sections that rest on no document for every name, and the paid model is for names you get serious about). A name whose only outstanding section rests on documents is not queued.

| Setting | Key | Default |
|---|---|---|
| the hours after which the queue starts no pass | `EquityBrief:Queue:Hours`, a whole number above zero | `1` |

**An hour covers the whole index at the rate measured on this machine.** 6.10 measured 12 passes on the local model the settings name, over the fixture's four listed names on three nights: 3.35 seconds a pass on average and 5.13 at the slowest, so 503 members at the slowest come to 43 minutes. The queue starts no pass once the hours have passed and finishes the one it is in, so it runs past them by at most one pass; the night's fifteen-minute deadline bounds the arithmetic and not the queue (see: The overnight queue is bounded by its own limit rather than the night's deadline, and starts no pass once the limit has passed). Measure again after a change of model or machine, and set the hours from what a pass takes there.

**Where to read what it did.** The queue's own row on the run log, under the night's run with the stage `overnight queue`, says what it came to: `ok` where it ran through every name it queued, `limit` where it stopped at its hours with names left, and `unavailable` where the local model did not answer, which stops the queue at that name. Its detail names the names listed and queued, every pass it ran under a run of its own with what each wrote, the names it left, and whether the machine was held awake. Each name's pass writes the judge's, the writer's and the checker's rows under that pass's run.

### The paid models, the one word each job names, and the spend caps

The paid models are the one part of the system that costs money, and nothing in the code says which model any of them is. The shipped `appsettings.json` beside the worker holds one profile per model under `EquityBrief:Models:Profiles`, each naming its wire format, where its provider answers, the model, the section of the secrets file its key sits under, any options, the rates its provider charges and the earliest date its provider publishes for retiring it. Each job that calls a paid model names the profile it uses by one word, its `Use`, under a section of its own beside how long one call may take and the most one answer may run to. Values set in `appsettings.Secrets.json` or the environment win over the shipped file.

| Setting | Key | As shipped |
|---|---|---|
| the profile the research job uses | `EquityBrief:Models:Research:Use` | `deepseek` |
| how long one research call may take, in seconds | `EquityBrief:Models:Research:TimeoutSeconds` | `600` |
| the most one research answer may run to, in tokens | `EquityBrief:Models:Research:AnswerTokens` | `32768` |
| the profile each section of a report is written by, one entry per section in figure 12.2's own names | `EquityBrief:Models:Research:Sections:<section>` | `deepseek` |
| the profile a trial asks beside a report, none being off | `EquityBrief:Models:Research:Trial:Use` | none |
| the sections a trial asks it for, one entry per section | `EquityBrief:Models:Research:Trial:Sections` | `The short version, The two cases` |
| the reports a trial runs over before it stops | `EquityBrief:Models:Research:Trial:Reports` | `3` |
| the day a trial counts its reports from, as `yyyy-MM-dd` | `EquityBrief:Models:Research:Trial:From` | `2026-09-30` |
| the profile a review asks to check a section's own draft, none being off | `EquityBrief:Models:Research:Review:Use` | `deepseek` |
| the sections a review asks it for, one entry per section | `EquityBrief:Models:Research:Review:Sections` | `The two cases, The risks, each with what would confirm it` |
| the reports a review runs over before it stops | `EquityBrief:Models:Research:Review:Reports` | `3` |
| the day a review counts its reports from, as `yyyy-MM-dd` | `EquityBrief:Models:Research:Review:From` | `2026-09-30` |
| the profile the news job uses | `EquityBrief:Models:News:Use` | `deepseek` |
| how long one label call may take, in seconds | `EquityBrief:Models:News:TimeoutSeconds` | `60` |
| the most one label answer may run to, in tokens | `EquityBrief:Models:News:AnswerTokens` | `2000` |
| the most the news labeller's own calls may cost in a UTC month, in dollars | `EquityBrief:Models:News:MonthLimit` | `5` |
| how long the news labeller may run from its start, in minutes | `EquityBrief:Models:News:TimeLimitMinutes` | `20` |
| the most every paid job together may spend in a UTC day, in dollars | `EquityBrief:Spend:DayCap` | `10` |
| the most every paid job together may spend in a UTC month, in dollars | `EquityBrief:Spend:MonthCap` | `50` |

Each profile's fields, under `EquityBrief:Models:Profiles:<profile>`:

| Field | What it holds | `deepseek` | `claude-haiku` | `claude-sonnet` | `claude-sonnet-no-thinking` |
|---|---|---|---|---|---|
| `Format` | the wire format the provider serves | `openai` | `anthropic` | `anthropic` | `anthropic` |
| `BaseAddress` | where the provider answers | `https://api.deepseek.com/` | `https://api.anthropic.com/` | `https://api.anthropic.com/` | `https://api.anthropic.com/` |
| `Model` | which model answers | `deepseek-flash` | `claude-haiku-4-5-20251001` | `claude-sonnet-5-5` | `claude-sonnet-5-5` |
| `Key` | the section of the secrets file holding its `ApiKey` | `Research` | `Claude` | `Claude` | `Claude` |
| `Options` | the provider's own request fields, written as the JSON object it takes | none | none | none | none |
| `Thinking` | for the `anthropic` format, `off` or an effort level, `low`, `medium`, `high`, `xhigh` or `max`; none is the provider's default | none | none | none | `off` |
| `Retires` | the earliest date the provider publishes for retiring the model | none | `2026-10-15` | `2027-09-28` | `2027-09-28` |
| `RetiresReadOn` | the day that date was read | `2026-09-29` | `2026-09-29` | `2026-09-29` | `2026-09-29` |
| `Prices:CacheHit` | dollars per million prompt tokens the provider serves from its cache | `0.003` | `0.10` | `0.20` | `0.20` |
| `Prices:CacheWrite` | dollars per million prompt tokens written to its cache | none | `1.25` | `2.50` | `2.50` |
| `Prices:CacheMiss` | dollars per million prompt tokens it does neither with | `0.15` | `1.00` | `2.00` | `2.00` |
| `Prices:Output` | dollars per million output tokens, reasoning included | `0.60` | `5.00` | `10.00` | `10.00` |
| `Prices:PeakHours` | the UTC hours the rates are multiplied in, one entry per window written as a start and an end hour | `01-04, 06-10` | none | none | none |
| `Prices:PeakDays` | the days those hours fall on, one entry per day | `Monday, Tuesday, Wednesday, Thursday, Friday` | none | none | none |
| `Prices:PeakMultiple` | what the rates are multiplied by in those hours | `2` | none | none | none |

**Switching a job's model is changing its one word.** Set `EquityBrief:Models:Research:Use` to a profile, and the next pass the drain starts is written by that profile's model; a pass already running finishes on the model it started with. Report generation asks DeepSeek alone, on the operator's ruling of 2026-09-30, so no research setting names `claude-haiku`, `claude-sonnet` or `claude-sonnet-no-thinking` and the suite refuses a shipped file that does, until a decision replaces that one (see: Report generation asks DeepSeek alone, and no research setting names a Claude profile). Nothing else changes: the other profiles stay as they are, a section records the model that wrote it, and a call already made keeps the price its run log row recorded (see: A paid job names its model profile in one word the operator switches, and a profile is priced at its configured rates at its call's own timestamp). The answer budget is the job's and not the profile's, so a switch to a model that counts its reasoning inside its answer wants a budget that holds both: DeepSeek ran on `32768` from 6.7, and Claude Sonnet 5.5 on `16000` for the day it wrote research.

**Switching one section's model is changing its word in the map.** `EquityBrief:Models:Research:Sections` holds one entry per section in figure 12.2's own names, each naming a profile, and that section is written by it while the others stay on theirs; a section the map does not hold is written by the job's `Use`. It ships naming `deepseek` for all nine, so nothing moves until a word is changed. A map naming a section figure 12.2 does not name, or a profile the profiles do not hold, stops the research job at startup with a line naming it. A pass asks each profile its sections name whether it answers before it starts, each profile's calls go through a spend cap of its own against the same day and month caps, since the caps sum every call on the run log, and a section records the model that wrote it. The drain waits out the peak windows of the job's `Use` and of every profile the map names together, and the queue page states the start it will take from the same windows (see: Research names a profile per section as well as per job, and a Claude profile states its thinking).

**A Claude profile states its thinking.** `Thinking` is `off`, or an effort level from `low` to `max`, or none for the provider's default. Off is sent as `"thinking":{"type":"between_tools"}`, which the model reads as no thinking before the answer; Sonnet 5.5 refused `{"type":"disabled"}` with a 400 naming that form, and refused a thinking token budget with a 400 naming the effort level in its place, both captured in the fixture on 2026-09-30, so a budget is not a setting. An effort level is sent as `output_config.effort`, beside the answer's format where the request asks for one. A `Thinking` on an `openai` profile, or beside options setting `thinking` or `output_config` themselves, is refused at startup, and a model asked with a thinking setting is recorded as a different writer from the same model asked without one, as `claude-sonnet-5-5 thinking off`.

**A trial asks a second profile beside a report, and never writes into it.** Where `EquityBrief:Models:Research:Trial:Use` names a profile, a pass that wrote any of the trial's sections in its paid lane, first time, is followed by the trial: each of those sections asked of the trial's profile with the request the pass built for its first draft, the same facts file and documents, and for the short version the sections the pass had accepted. The answer is checked in memory by the claim checker's rules and told once what was refused, as a pass's retry is. It writes no research section, so the page draws the pass's report whatever the trial wrote; it writes one run log row a section under the pass's run, with the stage `section trial: <section>`, holding each round's draft, verdict and price, the outcome and the cost, and each of its calls is written as `research call: <section>, trial`, which the run page leaves out of the pass count and the report costs. It stops by itself once it has run over `Reports` reports since `From` for its profile. It ships naming no profile, so no report is tried: it first shipped asking Claude Sonnet 5.5 for the short version and the two cases, and the operator ruled before any report was tried that report generation asks DeepSeek alone (see: A trial asks a second profile for named sections beside a report, and ships naming none). To end one early, blank its `Use`; to choose its model for a section, change that section's word in the map.

**A review asks a section's model to check its own draft, over the next three reports.** Where `EquityBrief:Models:Research:Review:Use` names a profile, a pass that wrote the two cases or the risks at the first draft is followed by a review: the same request with the pass's draft handed back and the checks each sentence is read against, a reason rather than a restated figure, the size of a change weighed, specific to the company, and for a risk a confirmation that is what the risk coming true would look like, and the whole section asked for again revised. Its answer is checked in memory as a trial's is, written as `section review: <section>` beside the pass's rows with its calls as `research call: <section>, review`, never into the report, and it stops after `Reports` reports. Its drafts are written to the report's comparison file, named as the model reviewing its draft, beside the pass's own drafts and the trial's. It ships naming `deepseek`, the model that writes those sections, on the operator's word of 2026-09-30, so the three reports from that day that write either section are reviewed and it then stops, at most four DeepSeek calls a report; to end it early, blank its `Use` (see: A review asks a section's model to check its own draft against the section's rules, beside a stated number of reports).

**Reading how each report did.** The run page's detail folds a section, How each report did, drawing each report of the seven nights with a cell for each section: first where it passed the claim check at its first draft, retry where it passed on its retry, out where it was left out, with why numbered beneath, and earlier where it stood from an earlier day, each with what its own calls cost and none of a trial's. Beneath are how many of the newest twenty reports' two cases carried a figure on both sides and each section's share passed first time and left out over the newest twenty reports that warranted it. The drafts a trial or a review wrote are drawn on no page (see: The run page draws how each report's sections came out and each section's rates over the newest twenty reports, and no trial's drafts).

**Writing the comparisons to files.** From the repository root, after a report a trial or a review ran beside, or whenever the counts below are wanted:

```
dotnet run --project src/EquityBrief.Api -- comparisons
```

It reads the store through the read API, writes nothing to it and starts no page, and writes into the checkout's `sampleReports` folder, which the repository ignores, a file of the same name written again over the one before:

- `<TICKER>_comparison_<day>.html` for each report a trial or a review asked beside, each section with the pass's drafts, the review's and the trial's side by side, their outcomes, rounds and costs, a second report of the same stock on the same day ending `_2`;
- `section_rates_before_and_after_the_addendum.html`, each section's share passed first time and share left out over the ten reports whose passes started before the research prompt's addendum merged at 2026-09-30 11:40:11 UTC and the ten that started after, once the tenth after has been written;
- `two_cases_both_sides.html`, of the reports whose passes started after the two cases' ask merged at 2026-09-30 04:54:22 UTC, the first twenty that drafted the two cases and how many carried a figure on both sides, beside the 5 of 8 accepted drafts measured before the ask changed, once the twentieth has been written.

For each count whose window is not yet full it prints how far it has come, as `comparisons: section rates: 3 of 10 reports written since the addendum merged and 10 of 10 before it, so no file yet`, and writes no file for it (see: The drafts compared beside a report and the research template's before and after counts are written to files by a command, and drawn on no page).

**Adding a key.** A profile's `Key` names the section of the secrets file holding its key, as `EquityBrief:Models:<Key>:ApiKey` in the worker's `appsettings.Secrets.json`: `Research` for DeepSeek, as it has been since 6.7, and `Claude` for every Claude profile. A Claude key that is not scoped to a workspace is refused by the provider on every request until the workspace it bills to is named beside it, as `EquityBrief:Models:Claude:WorkspaceId`, the `wrkspc_` identifier the provider's console shows for the workspace; a key made inside a workspace needs none. A job whose profile names a key the secrets file does not hold stops with a plain line saying which profile and which key, on the pass's own run log row, which the drain settles the request under and the run page draws; no other profile answers for it (see: A paid model is one interface with an implementation per wire format, and a job never falls back from the profile it names).

**Adding a profile** is one more entry under `EquityBrief:Models:Profiles` with the fields above. A provider serving the OpenAI chat completions format takes `openai`, and Claude's own messages interface takes `anthropic`; another format is refused at startup rather than answered by one of these. A provider with no peak pricing names no peak hours, and its multiple is then read as one; a provider charging nothing apart for a cache write names no write rate. Options are the provider's own fields sent beside the request, as DeepSeek takes `{"thinking":{"type":"disabled"}}` to answer without reasoning first. A model asked with options is recorded as a different writer from the same model asked without them.

**A model with no prices is refused at startup**, rather than called and recorded as costing nothing, and so is a rate at or below zero for the uncached prompt or the output, a write rate below zero, a peak window whose start is not before its end, a multiple below one, a day that is not a day of the week, a retirement date not written as `yyyy-MM-dd`, and options that set the model, the messages, the system prompt, the answer's budget or the stream, which the feed writes itself. DeepSeek's rates are what its page gave on 2026-09-13; Claude's are what https://platform.claude.com/docs/en/about-claude/pricing gave on 2026-09-29, and the model identifiers and retirement dates what the models overview page gave the same day. When a provider changes a price or a date, these values are what change.

**A profile nearing its retirement date is named on the run page.** From 30 days before a profile's `Retires`, the run page's list of anything to worry about carries one line naming the profile, each job using it and the date, so a switch is made before the day the model stops answering; on that day the job's own check that the model answers stops it with its line, and no other profile answers for it (see: A profile carries its provider's earliest retirement date, and the run page names it from thirty days before). As shipped, `claude-haiku`'s date of 2026-10-15 is inside that reach, and it draws the line once a job uses it.

**A report costing more than $2 is named on the run page** the morning after it was written, with its cost, in the same list, so an expensive report is seen before the month's spend shows it.

**Both caps are proposals**, marked so in section 17, and the obligation that settles them fires on the run page once twenty research passes carry a recorded cost. A cap stops research rather than warning about it: a call is refused before it is made where the most it could cost would take the day or the month past its cap, research resumes when that UTC day or month ends, and the name page says research is paused and when it resumes (see: The spend cap is a stop, not an allowance).

### Writing one name's research

A pass writes one name's research: the sections not yet written, the ones gone stale, and the ones left out on an earlier day. The queue's drain runs it, and so does this, from the repository root:

```
dotnet run --project src/EquityBrief.Worker -- research --ticker KEYS
```

`--refresh` is a regenerate: it fetches the company's figures again whatever the store holds, assembles the night's facts file again from them and writes every section again, and it starts nothing and fetches nothing on a day a pass for the name already ran. `--paid-for-local` has the research model write the local lane's sections as well, which is the page's option where the local model is unavailable or cannot hold one, and which the drain adds to every regenerate a press asks for. `--live` and `--fixture <folder>` choose the source for one run, as they do for the night.

**What it does, in order.** It fetches the name's fundamentals where the store holds none, and where that stores a filing the night had not seen it assembles the night's facts file again, so the pass writes from the quarter it just fetched (see: A name's facts file is assembled again for its night when an open fetches its fundamentals). It asks the staleness judge which sections stand. Where the paid lane has work it asks the research model whether it answers, and does not start where it does not (see: A research pass does not start where the research model does not answer). It fetches its latest results release from the filings archive, then the name's news inside each stored move and from the release's filing date to the night, overlapping spans once and none older than three months before the night, tests each document for admissibility as it arrives and stores it with the verdict. It hands each section the documents code picks for it: two a move for the cause of each move's episode, and six since the release beside the release itself for the sections built across the evidence, one from each stretch of the days since it (see: A research pass reads a name's news from the last three months alone, inside each stored move and since the company's own filing, and hands each section the documents code picks from it). A window the provider has more of than a query reads is named as unread on the pass's row, and the sections are written from the windows that were read. The local lane writes its sections, the spend cap makes every paid call, and the claim checker reads each section; a section refused is written again in the same pass, up to three times, each retry told what the draft before it was refused for, and one refused at the third retry is left out (see: A retry names each thing the check refused, and a section refused on its third retry is left out). The short version is written last, from the sections already accepted.

**What a pass costs.** Over the fixture's KEYS a pass wrote seven of the eight sections it could write, three on the local model and four through the spend cap for $0.0337, a sum that includes the cause of each large move asked twice and answered twice with reasoning and no text, and the short version asked again after the checker refused its first draft, since every draft and every empty answer is billed, and is at the off-peak rate, since the pass's paid calls were recorded on a weekday outside the provider's peak windows; recorded inside a peak window, the same calls cost twice as much. The local model's segment commentary, told the one figure its first draft was refused for, wrote a second draft without it and passed, and the short version, told the two dates its first draft named that the facts file does not hold, wrote a second draft without them and passed. The name page states what the passes before it cost beside its control, because a pass's price is known only once it has been made. A second press on the same day starts nothing, because a pass for the name already ran that day, and a press while a pass for the name is running is refused by name; the page's rewrite and its option to have the paid model write the local lane's sections still start one (see: A name opened again on the day its research pass ran starts no second pass unless the page asks for one). Once the day a report was written has passed, the page offers Regenerate Report, which fetches the company's figures again, a filing made since and the day's market value, multiples, dividend, ratings and next report among them, and has the paid model write every section again from them whether or not a trigger has fired. Over the fixture's KEYS a pass with every section paid cost $0.0387, and the fetch weighs ten of the provider's daily 100,000 calls. It runs once a name a day: a request the drain reaches for a name a pass already wrote that day starts nothing and fetches nothing, and its run log row says why (see: A regenerated report is written whole by the paid model from the company's figures as they stand on the day it runs, once a name a day).

**A report the connection cut is not the day's report, and a regenerate the same day fills what a report left out.** Where a pass's run log holds a call of the report's own with the outcome `unavailable`, the model could not be reached for that section, and the pass is not the day's report: a regenerate or a second press the same day runs. A trial's or a review's call that could not be reached leaves the pass the day's report. On the day a report was written, a regenerate writes the sections it left out, whether the checker refused them at the last retry or the connection lost them, and none it accepted; where it left nothing out, a regenerate that day starts nothing. The name page reads the pass as written and does not offer the control that day, so the request is written by a press to `/passes/<TICKER>` carrying the page's header and `refresh=true` (see: A regenerate on the day a report was written writes only the sections that report left out, and a report the connection cut is not the day's).

**What the control does.** It sends the press with a header of the page's own, and the read surface refuses a request without one, so another site's page open in a browser on this machine cannot ask for a pass (see: A pass is started only by a request carrying the name page's own header). The surface refuses a name the index does not hold, then writes a request, starts the worker's drain from a copy of the worker's build and returns at once. The drain runs the pass at the off-peak rate, and the page shows what it wrote as it lands and when it is opened again (see: A press writes a request and starts the worker's drain as a process of its own, and every pass waits for the off-peak hours).

### Measuring a sector's sites

A name's page says where its industry's cycle was declined for lack of industry sources, with the pages the searches found and the sector the source list carries no site of its own for (see: An industry cycle the model declined is named as declined for lack of industry sources). Sites proposed for that sector are measured before any joins the list, from the repository root:

```
dotnet run --project src/EquityBrief.Worker -- measure-sources --sector "Healthcare" --sites cms.gov,fda.gov --industries "Medical Devices,Healthcare Plans"
```

It searches each site for each industry as a theme pass does, one search a site and an industry over the quarter to the day, judges each page by admissibility and the density rule, and says which sites join: a site joins where, for at least one of the industries, it returned at least one page admissibility admitted and the density rule reads as about that industry. It writes nothing to the store; the report is `artifacts/measure-sources-<sector>-<date>.json`, one search per site and industry against the search tool's free allowance. A site that joins is added under its sector in `source-lists.json`'s `industry.sectors`, with the measured result in `sectorsNote` and the file's review date moved to the day, and a site that does not stays out with its result noted (see: A theme search adds its sector's sites, and a site joins the list only where a measurement found industry material on it).

### Draining the queue

A press asks for a report and starts the drain, which writes it. The drain is also run by hand, which is how a request is taken where no press started one, as one left outstanding because the drain could not be started:

```
dotnet run --project src/EquityBrief.Worker -- drain
```

It takes the oldest request nobody has started, runs the pass for that name, and settles the request under what that pass's own run says it came to: `written` where the run came to `ok`, and `refused` otherwise, carrying the run's own words for why. A verb that exits without failing has run, and a pass that ran is not a pass that wrote. It then takes the next, until no request is outstanding, and prints how many it took and how many of those wrote.

**Every pass waits for the off-peak rate.** A drain started inside one of the research model's peak windows, which the configured prices state in UTC, waits with its requests still outstanding until the window ends, and one that reaches a window between passes waits there before the next (see: Queued work runs off-peak, and every schedule is written in UTC). The windows are 01:00 to 04:00 and 06:00 to 10:00 UTC on weekdays, 21:00 to 24:00 and 02:00 to 06:00 in New York until 2026-11-01 and an hour earlier after it. A drain with nothing outstanding ends without waiting.

**One drain at a time.** A drain holds `data/drains/drain.lock` open for as long as it runs, and a drain started while another holds it waits for it to end and then takes whatever it left, so the queue page's times, which assume one pass after another, are the order passes run in. A drain run by hand waits the same way.

**Where a press's drain runs from.** A press copies the worker's build output to `data/drains/`, under the data root, into a folder named for that build, and starts `dotnet EquityBrief.Worker.dll drain` from the copy with the checkout as its working directory and the surface's data root in `EquityBrief__DataRoot`. A copy and never the build itself, because the night builds the worker again from the checkout and a drain still writing then would hold its files open, which stops the night's build on Windows. Every press over the same build starts from the same copy, a new build is copied again, and a copy nothing has been started from for seven days is removed at the next press. Where no worker is built beside the surface, or the surface is running from anywhere but its own build, the press writes its request, starts nothing and says so, and the request waits for the drain run by hand.

**A press's drain outlives the surface.** It is a process of its own with no window, so stopping the surface does not stop it and an interrupt sent to the surface's console does not reach it. To stop a drain, stop its process: on Windows `Get-CimInstance Win32_Process -Filter "Name='dotnet.exe'" | Where-Object CommandLine -Match 'EquityBrief.Worker.dll drain'` names it. A drain stopped partway leaves its request `writing`, which the queue screen shows as being written until the next drain starts, puts it back as outstanding and takes it in its turn; a pass that ends on an error settles its request as refused with the error, and the drain goes on to the next (see: A request a drain left being written is put back as outstanding by the next drain, and a pass that fails settles its request as refused). On macOS a terminal's interrupt reaches every process started from it, the drain among them, so a surface started in a terminal there is stopped once its drain has ended.

A request the drain has claimed is `writing` and cannot be taken out, because what the queue screen offers to remove is a report that has not been generated. One name holds one outstanding request at a time, which the store enforces rather than the screen: a second press for a name already waiting adds nothing and says so.

**The industry cycle is the theme's.** A theme is the industry the index names for a member, and one theme pass writes the cycle every member of that industry reads (see: A theme is the industry the index names for a member, and one theme pass serves every member it names). A name's pass refreshes its theme before anything of its own where the cycle is missing, left out on an earlier day, or older than a trigger the judge fired for the name. The theme pass searches each site on the industry list for the industry, one search a site over the quarter to the day, asking each for its first three results and each page's text (see: A theme pass searches each site on the industry list alone, and hands the model a bounded set of the pages they return); it drops a result from a site the list does not carry and a result whose text is missing or no longer than its snippet, names both on its row, tests every page it keeps for admissibility and stores it whole, and has the spend cap make one call over at most ten of the pages it admitted, each carried as its first 30,000 characters, which states no figure. It does not start inside the research model's peak windows, which the configured prices state in UTC: its row says when the window closes, the name's own sections are written without it, and the next open of any member after that refreshes it (see: A theme refresh runs off-peak, and a name opened at peak is written without one). A theme is researched once a day at most, so a second member opened the same evening reads what the first one's pass came to, and a search that found nothing is not run again that day. A theme pass costs twelve searches against the tool's monthly allowance and one paid call, or two where the checker refuses the first draft: over the ten Semiconductors pages the fixture's searches kept, the two calls cost $0.0119. Where the list's sites carry nothing about an industry's prices the model writes nothing and the cycle is left out with that line: over the eleven pages the searches kept for Scientific & Technical Instruments, being job postings, labour and price releases, statistics pages and trade news, its one call came back empty.

**Where to look.** Every stage of a pass is a row on the run log under one run, `research-<instant>-<TICKER>`: `fundamentals`, `staleness`, `theme research` where the pass refreshed its theme, whose detail names the sites it dropped and the addresses short of a document, and `theme claims` ahead of it where the theme wrote a cycle to check, `prose`, one `research call:` row per paid call, `claims`, the second and third rounds' rows named for their round, and `research` last, whose detail says what was written, what was not and why, and what the documents came to.

### Labelling the news

The night starts the labeller after its own report request, as a process of its own on the night's build, so every call and every dollar it makes sits on its own run and never the night's (see: The news labeller is a process of its own the night starts after the close, and its calls and its spend are its own). It is also run by hand, which is how a night whose labeller stopped at a limit is finished early, or a night's list labelled under a profile just switched to:

```
dotnet run --project src/EquityBrief.Worker -- label-news
```

It takes the newest stored night, or the one `--session yyyy-MM-dd` names, reads its list in the order it is drawn, and for each name asks the news job's profile for a label of each stored admitted article of the thirty days before the night, newest first and at most twenty a name, leaving out the articles that profile has already labelled or marked unreadable under the instruction's version. Before it asks anything it resolves the profile, so a missing key stops it with its line, and asks the provider for its model list, which bills nothing, so a model that does not answer stops it there; neither falls back to another profile. It stops at its own time limit and month limit, at the day or month cap, and where a peak window of its profile opens, and what is left waits for the next night. It prints how many names it reached, the labels written, the unreadable answers, the articles refused by admissibility and the ones labelled before, what the run cost and the month so far, and what stopped it.

**A label is never overwritten** (see: A news article is stored once per member with its admissibility judged, and a label is never overwritten). A label is kept by the profile that wrote it and the instruction's version, so switching the news job's profile, the one word `EquityBrief:Models:News:Use`, takes effect on the next night or the next run by hand and writes rows of its own beside the first profile's; nothing is labelled again under the old one. The shipped word is `claude-haiku`, whose provider publishes a retirement date of 2026-10-15 at the earliest, which the run page warns of from thirty days before; on the day the model stops answering the labeller stops with its line, and the fix is the one word.

**Filling the window at once.** Articles reach the store from the night's one news query, so the labeller's thirty-day window fills over thirty nights. To fill it now, on the operator's word, since it costs about ten weighted calls a day against the day's allowance:

```
dotnet run --project src/EquityBrief.Worker -- news-fill --days 30 --live
```

It makes the same dated query for each of the last days, stores every article naming a member that is not stored already, judged for admissibility as the night judges it, and prints the count a day and the total. `--fixture <folder>` reads a fixture's capture instead, and with neither the source the settings name is used.

**Where to look.** The labeller's rows on the run log sit under one run, `label-news-<instant>`: one `research call: news label, <TICKER> <article>` row per paid call, a retry's named for its round, and `news-labels` last, whose detail names the profile and the model, the names and how many it reached, the labels, the unreadable answers by cause, the articles refused by admissibility and the ones labelled before, the cost and the month's, and the stop. The night's own row for the step, `label-news`, says it started the labeller and nothing else; a labeller that could not start, its key missing or its model not answering, writes a `news-labels` row of its own with `refused` and the reason. The labels are drawn under News on the stock's page, the positive and negative counts beside each row of tonight's list, and the labeller's own line sits in the run page's research and spend region, with its nights counted toward twenty under what else is waiting on a count.

### Exporting one name's report

The name page carries a link, *Export this report as a file*, and the browser saves what it answers wherever the operator chooses. The read surface answers the same file at `/exports/name/<TICKER>`, which is the address the link asks for.

The file is the name page's own region, drawn by the same code from the same store, in a document that needs nothing else to be read: its styles are inline, it carries no script and fetches nothing, every disclosure is open, and a link to another of the application's pages is kept as its words (see: A single report can still be exported as a self-contained file). It leaves out the research controls and the pause, which are the application asking the operator something rather than part of the report. It is named for the name and the newest session its figures are from, `EquityBrief-KEYS-2026-09-08.html`, so two exports on different nights are two files, and its opening line states that session. Nothing is written to the store by an export.

### Counting the swing filter's shape

```
dotnet run --project src/EquityBrief.Worker -- filter-counts
dotnet run --project src/EquityBrief.Worker -- filter-counts --year
```

`filter-counts` prints, for every night the store keeps bands, trends and plans for, how many members pass each of the swing filter's five gates in order with what each removed, each gate alone, each gate relaxed with every other held, and the pullbacks among them, under six settings: the market gate at 45% and at 50%, each with the trade gate read from the ladder's first tranche, from the swing trade at the nearest bands and from the swing trade clear of the noise. `--year` adds every session of the stored year on which breadth can be read, replaying the bands, the trend and the plan as of each session through the same functions the night calls, and says how many sessions it left out and how closely the replay reproduces what the store kept. It opens the store read-only and writes nothing, reads no outcome, and runs in under a minute; it is safe beside a night but reads the store as it stands, so a count taken while a night writes may land on half a night. It is what the operator rules the filter's starting settings from (see: The swing filter's starting settings are ruled from shape counts before tonight's list switches to it).

### Opening the swing filter's version, and a shape proposal accepted or rejected

The filter's settings move only through this command, run by the operator from the repository root, and a night never runs it (see: A shape acceptance restarts the live filter's edge clock, and after one acceptance while the list is live each further one states the blocks it restarts). Until a version is open the filter runs on section 17's proposed values and no night counts toward the shape clock's sixty.

**The first version** is opened on the settings the operator rules from the counts, naming each setting that differs from section 17's proposed value, the trade gate's reading, and the evidence:

```
dotnet run --project src/EquityBrief.Worker -- shape --settings strengthFloor=0.6,rewardToRiskFloor=1.5 --trade swing --evidence "the ruling of the day, from filter-counts over the stored year"
```

The operator's ruling of 2026-09-25 opened version 1 with:

```
dotnet run --project src/EquityBrief.Worker -- shape --settings strengthFloor=0.5,depthLow=1,depthHigh=5,dryUpCeiling=1.5,stopLow=0.5,stopHigh=4,rewardToRiskFloor=1.5,arrivalSessions=3 --trade swing --evidence "the operator's ruling of 2026-09-25 on 12.2's counts and the re-measure under the three-session arrival window"
```

The operator's second ruling of 2026-09-25 opened version 2 at a 45% market floor, every other setting version 1's, run at 14:04:07Z (see: Version 2 of the swing filter lowers the market floor to 45% before the list's first live night, and nothing else moves):

```
dotnet run --project src/EquityBrief.Worker -- shape --settings breadthFloor=0.45 --evidence "the operator's ruling of 2026-09-25: the market floor at 45%, from version 1 replayed over the 53 stored sessions, which listed a median of 3 names a night and none on 5 at 45%, and none on 7 at 50%, the two differing only on 2026-09-23 and 2026-09-24, the only sessions breadth fell below 50%"
```

**A rule correction** taken before the family's first scored night moves the plan the trade gate reads without counting as an acceptance (see: A rule correction taken before the family's first scored night opens a filter version and registers the family again at one instant, and is no shape acceptance). The operator's ruling of 2026-09-26 moves it to section 10's plan for the swing trade, opening version 3 with every other setting version 2's, run before the night of 2026-09-28 (see: The swing filter's trade gate reads section 10's plan for the swing trade, and the plan at the nearest bands is the variant in the reward to risk variant's place):

```
dotnet run --project src/EquityBrief.Worker -- shape --rule-correction --trade clear --restarts 0 --evidence "the operator's ruling of 2026-09-26: the live trade gate reads section 10's plan for the swing trade, and the plan at the nearest bands is the variant in the reward to risk variant's place, a rule correction taken before the family's first scored night"
```

It closes the open version and opens the next with the plan named and every other setting as it stood, retires every standing swing family candidate and registers the family the code writes for the new version at the same instant, and states the non-empty blocks the live filter's clock has run, which it restarts. It is refused with nothing changed where no version is open, where the version already reads the plan named, where no live filter candidate stands, and without `--restarts` or at any other count. A merge that moves the evaluators' pins leaves the six standing candidates on a version the code no longer carries, so a night run after it and before this command marks the swing filter's step failed naming them, evaluates none of them, and still draws its list on the open version's plan; run it after the merge, when no night is running.

A rule correction that names no plan moves no setting and opens the next version with every setting as the open one held it, written as the code now writes a version's settings. The operator's ruling of 2026-09-26 removes breakouts, so the base's tightness and the breakout's volume drop out of the settings, and it moves a stop inside a support band to that band's low edge; both change the gates' code and move the evaluators' pins, and they land in one change with the night's deadline at an hour and the pin that reads past comments (see: The swing filter reads pullbacks alone, and a breakout returns only as a registered candidate built from its measured record). Run after that merge, stating the blocks the live filter has run, with the ladder windows closed and opened again beside it as the section on ladder rule versions says:

```
dotnet run --project src/EquityBrief.Worker -- shape --rule-correction --restarts 0 --evidence "the operator's ruling of 2026-09-26: the filter reads pullbacks alone and a stop inside a support band moves to that band's low edge, landing with the night's deadline at an hour and the pin that reads past comments in one change and one remedy"
```

It is refused where the open version's settings are already written as the code writes them, since it would then change nothing.

**The pullback's freeze** opens its base as the next version: the open version's settings with the reward-to-risk floor at 2, retiring every standing swing family candidate and registering the family's eight for the new version at the same instant, among them the pullback in the top 3 sectors, top quarter of each, stating the non-empty blocks the live filter's clock has run and counting as no acceptance (see: The pullback's freeze opens its base as the next filter version and registers the swing family again at one instant). The operator's rulings of 2026-10-02 freeze it with 13.9, which moves every swing family evaluator's version, so run it after that merge and before the night, when no night is running, first of 13.9's commands; it is the remedy's first step:

```
dotnet run --project src/EquityBrief.Worker -- shape --freeze --restarts 0 --evidence "the operator's rulings of 2026-10-02: the pullback freezes at its base with the reward-to-risk floor raised to 2, the sector leaders registered beside it as its eighth rule"
```

It is taken once: a version the freeze opened refuses a second, as it refuses with nothing changed where no version is open, where no live filter candidate stands, and without `--restarts` or at any other count.

The settings are `breadthFloor`, `strengthFloor`, `depthLow`, `depthHigh`, `dryUpCeiling`, `rewardToRiskFloor`, `stopLow`, `stopHigh`, `earningsWindowSessions` and `arrivalSessions`, and `--trade` takes `ladder`, `swing` or `clear`; every setting not named keeps the open version's value, or section 17's where none is open. It closes the open version and opens the next, named 1, 2 and so on in the order opened. A setting the filter does not hold, a value that is not a finite number, a range whose low end sits above its high and settings the open version already holds are each refused with nothing changed.

**A shape proposal** is written by the night once sixty ordinary nights are stored under the open version, and the run page's Calibration region draws it with each gate's setting held and proposed and, beside it, the non-empty blocks the live filter's clock has run (see: The shape proposer moves one setting a gate, nearest first, and never applies what it proposes). Accept it by its number, or reject it with the reason:

```
dotnet run --project src/EquityBrief.Worker -- shape --accept 1
dotnet run --project src/EquityBrief.Worker -- shape --reject 1 --reason "the window held the index's quarterly rebalance"
```

A rejection writes the reason on the proposal's row and changes nothing else, and the next proposal for the version waits on sixty more ordinary nights. An acceptance opens the proposal's settings as the next version; a proposal written for a version no longer open is refused. Before the swing family registers, an acceptance restarts nothing. Once the live filter's candidate stands registered, an acceptance also retires it and registers the accepted settings in the same write, which restarts its edge clock: the first such acceptance states nothing more, and every later one must state the blocks the run page draws beside the proposal, and is refused with nothing changed without them or at any other count:

```
dotnet run --project src/EquityBrief.Worker -- shape --accept 2 --restarts 3
```

Each attempt, refused or not, is one row on the run log under `shape` and a run id beginning `shape-`, and an acceptance that retires and registers adds the registrar's own row under the same run id.

### Replaying the swing filter's results for the sessions before its first stored night

A pullback's trigger arrives where it first fired within the arrival window, read off the stored results of the sessions before the night. The sessions before the filter's first stored night hold none, so on its first nights every pullback fails its trigger for want of them. Replay them, then run the newest night again so it reads them:

```
dotnet run --project src/EquityBrief.Worker -- filter-history --from 2026-09-21 --through 2026-09-23
tools/nightly.ps1 --session 2026-09-24
```

Each session is replayed under the open version's settings and stored under the version `replayed`, which the trigger's arrival reads and no clock, page or scored setup does (see: The swing filter's results are replayed for the sessions before its first stored night for the trigger's arrival alone, and removed once no night can read them). A session already holding results, the night's own session and a range holding no session are each refused with nothing written. Each run is one row on the run log under `filter history` and a run id beginning `filter-history-`, which the run page draws as run by hand. Run it when no night is running.

Once no night can read them, take the replayed results out. On the operator's store that is any time after the night of 2026-09-29 has finished:

```
dotnet run --project src/EquityBrief.Worker -- filter-history --remove --from 2026-09-21 --through 2026-09-23
```

It removes the replayed results of the sessions named and nothing else, a night's own results staying, and prints and writes on the run log each session it removed with its count and the names among them that passed, under `filter history removal` and a run id beginning `filter-history-`. A session a night can still read is refused whole, naming it: the trigger's arrival reads each member's sessions before the night as far back as the open version's window or a standing candidate's reaches, three on version 2, so each member's newest four bars are kept, which covers both the night after the newest session the store holds and the newest night run again. A range holding no replayed result is refused too, and a refusal writes nothing. Run it when no night is running.

### Pulling history before the store's year

The store keeps a year of bars, and a session can be read only once two hundred sessions stand before it, so a replay over the store alone reaches about fifty sessions. The history pull stores older history apart from the store's own bars, for a measurement to read, and nothing a night runs reads it (see: The history pulled before the store's year sits apart from its bars, marked by the pull that wrote it, read by no night and removed whole by that pull):

```
dotnet run --project src/EquityBrief.Worker -- history-pull --from 2018-01-01 --live
```

It asks every name the index held on any session from that date to tonight, the members that have left since among them, for its daily bars over the whole span, one request a name, and the earnings calendar once for each calendar month of the span, and stores what comes back in `pulled_bar` and `pulled_earnings`, every row carrying the pull's run id, which begins `history-pull-` and is printed first. A pull from 2018 asks about seven hundred names and about a hundred months, so it costs about eight hundred weighted calls. A name the provider does not answer, and a session one name misses where at least half the names spanning it hold it, are each named on its run log row and printed, and a day fewer than half hold is counted apart as no one's missing session and named on the row. Run it when no night is running.

The earnings surprises the sweep's earnings beat condition reads are pulled apart, over the same span, by the same verb (see: The surprises pulled before the store's year sit beside the pulled prints and are read by no night):

```
dotnet run --project src/EquityBrief.Worker -- history-pull --surprises --from 2018-01-01 --live
```

It asks the earnings calendar once a calendar month of the span, about a hundred requests at a weight of one from 2018, and stores each print of a name the index held over the span in `pulled_surprise` with the figures as filed and the provider's surprise where the print carries one, every row carrying the pull's run id. Run it before a sweep whose report should read the surprises back to 2018; a sweep run without it reads the calendar's own year and says so.

The index's and the VIX's daily series, which the ideas' run's market switches read, are pulled apart by the same verb (see: The index's and the VIX's daily series are pulled beside the pulled bars, marked by their pull and read by no night):

```
dotnet run --project src/EquityBrief.Worker -- history-pull --market --from 2018-01-01 --live
```

It asks the provider's historical endpoint once for each, `GSPC` and `VIX` under its index exchange, two requests at a weight of one whatever the span, and stores every session sent in `pulled_market_bar`, every row carrying the pull's run id. It prints each series' sessions with the first and the last. A series the provider refuses stores nothing, is named, and the command exits with a failure. Run it when no night is running.

Each company of the history, its splits, the sector funds and each filer's revenue as first filed, which the sector heavyweights and the context checks are measured over, are pulled apart by the same verb, in this order (see: The pulls behind the heavyweights and the context checks store into tables of their own and are read by no night):

```
dotnet run --project src/EquityBrief.Worker -- history-pull --companies --from 2018-01-01 --live
dotnet run --project src/EquityBrief.Worker -- history-pull --splits --from 2018-01-01 --live
dotnet run --project src/EquityBrief.Worker -- history-pull --sector-etfs --from 2018-01-01 --live
dotnet run --project src/EquityBrief.Worker -- history-pull --revenue --live
```

The companies pull asks the fundamentals endpoint once for every name the index held over the span, about seven hundred names at its weight of ten, so about seven thousand weighted calls, and stores each answered name's filer, GICS sector and delisting in `pulled_company` and its quarterly share counts with the days their balance sheets were filed in `pulled_shares`. It prints the names it could not get with why, the companies filing no sector, no count with its date or no CIK, and how many of the fourteen members GICS moved after the close of 2023-03-17 the provider files in the sector they moved to, naming any it does not. The splits pull asks each of the same names for its splits, about seven hundred requests at a weight of one, into `pulled_split`. The sector funds pull asks for the eleven funds' daily series, eleven requests, into `pulled_market_bar` beside the index's and the VIX's, and exits with a failure where a fund is refused, as the market pull does. The revenue pull reads the filers the companies pull stored, so it is run after it and takes no date, and asks the SEC's archive once a filer and revenue concept, about four thousand requests that cost nothing against the allowance, a tenth of a second apart, which takes about fifteen minutes; it prints each concept's filers and figures and names the filers stating under none of them. The archive asks for a contact in the request, which the worker reads from `EquityBrief:Providers:SecEdgar:Contact` as a research pass does. A name, fund or filer not served is named and the rest stored. Run them when no night is running.

Remove a pull whole by its run id:

```
dotnet run --project src/EquityBrief.Worker -- history-pull --purge <the pull's run id>
```

A purge removes every row that pull wrote from the eight tables and nothing else, and a run id no row carries is refused with nothing written. Each pull and each purge is one row on the run log, under `history-pull`, `history-pull-surprises`, `history-pull-market`, `history-pull-sector-funds`, `history-pull-companies`, `history-pull-splits`, `history-pull-revenue` and `history-purge`, and the run page draws each as run by hand.

### Sweeping the swing filter over the stored history

The sweep replays the swing filter over the history the store and the pulled tables hold, across its designs, its settings and seven conditions, and writes a report proposing a starting point and the variants that run beside it. It registers nothing (see: A starting point is proposed from the deepest setting of a plateau on the edge and never its best variation, and nothing is registered before the operator approves it):

```
dotnet run --project src/EquityBrief.Worker -c Release -- sweep
```

It reads the live store directly over a read-only connection, one short read a name, and writes nothing to it: its saved chunks, its state and its report are files in a run folder of its own, named by the instant it started, under the folder `sweep` beside the store, or the folder the setting `EquityBrief:Sweep:Folder` names; nothing sits at that folder's root, a run's report lives in its folder as the full account, the figures a freeze cites are recorded in `PROGRESS.md` by a pull request of documents, and the runs before it are the operator's to remove (see: The sweep reads the live store read-only in short reads and writes nothing to it, pausing for every night). It prints its run's name first. It runs for days: the candidates and their benchmarks, then the point-in-time check, which rebuilds a sample of name-sessions with the night's own components over scratch stores under the machine's temporary folder and stops the run before stage 1 on any difference, then stage 1, the conditions' two steps and stage 2, each timed on its first chunk with the projection written to the log and the state file before it runs on, and stage 2 sampling each design for up to 4 hours. It looks after itself: on a weekday it pauses before 23:00 UTC for the night and goes on once the night has let its lock go, it waits whenever a drain holds its lock, a chunk that fails twice stops it with the report saying which and why, and started again with `--run <name>` it goes on from the first chunk it lacks, under the build that started it alone; a finished run is never written again, so a sweep over newer sessions is a new run. Its progress is the file `sweep.log` in the run's folder, and the newest report is served at `http://localhost:5152/sweep` with the earlier runs linked above it, each at `/sweep/<run>`. Start it on `main` from this checkout's Release build, so the Debug builds a branch's gates and tests make never stop it, and the night it pauses for is the one the scheduler runs.

### Sweeping a setup family

Each setup family's rule is replayed over the stored history across its own grid, one family a run (see: A setup family's sweep replays its own rule over the stored history and proposes the best edge among the settings meeting its floors):

```
dotnet run --project src/EquityBrief.Worker -c Release -- sweep-family --family breakout
dotnet run --project src/EquityBrief.Worker -c Release -- sweep-family --family drift
dotnet run --project src/EquityBrief.Worker -c Release -- sweep-family --family leader
```

It reads the live store read-only and writes nothing to it, and writes its report and its figures into a run folder of its own under the sweep's folder, served at `http://localhost:5152/sweep` as the newest run with the earlier runs linked. It takes minutes: the breakouts' and the drift's well under one, the sector leaders' a few, since each leader's bands and plan are computed for its session. The leaders' report states how many of the history's names carry a sector, which the membership files as it stands today. It does not start while the night holds the store or when the night's window would come before an hour has passed, saying when to run it instead. Named with no family, it says which families it is built for. Start it on `main` from this checkout's Release build. It registers nothing; each family's freeze is the operator's.

### The ideas on the base

Each new idea is added to the base, today's rule with its reward-to-risk floor at 2, one at a time, over the stored history and the index's and the VIX's series the market pull stored (see: A new idea is added to the base one at a time and kept only where it is better in six of eight years):

```
dotnet run --project src/EquityBrief.Worker -c Release -- sweep-ideas
```

It reads the live store read-only and writes nothing to it, and writes its report and its figures into a run folder of its own under the sweep's folder, served at `http://localhost:5152/sweep` as the newest run with the earlier runs linked. It computes the sweep's candidates and their benchmark first, which takes minutes, and does not start while a night holds the store or inside the night's window. A switch reading a series the store holds none of is left out and the report names it. Start it on `main` from this checkout's Release build, as the sweep is started. Nothing is registered: a starting point it proposes is frozen only on the operator's go.

### The ideas on a frozen family

The same ideas are read on the breakouts and the earnings drift as frozen, one family a run, each idea added alone to the family's live rule and judged by the same test (see: The frozen families are read with the pullback's ideas one at a time, and nothing they show is frozen or registered):

```
dotnet run --project src/EquityBrief.Worker -c Release -- sweep-family-ideas --family breakout
dotnet run --project src/EquityBrief.Worker -c Release -- sweep-family-ideas --family drift
```

It reads the live store read-only and writes nothing to it, and writes its report and its figures into a run folder of its own under the sweep's folder, served at `http://localhost:5152/sweep` as the newest run with the earlier runs linked. It takes minutes. It reads the breakout with eleven ideas and the drift with thirteen, the two trailing stops among the drift's, and its report states the tries, the passes and what luck alone passes of that many. It does not start while the night holds the store or when the night's window would come before an hour has passed, and a switch reading a series the store holds none of is left out and named. Named with no family, it says which it reads. Start it on `main` from this checkout's Release build, so the Debug builds a branch's gates make never stop it, or from a Release build written to a folder of its own while the sweep runs from the checkout's. Nothing it shows is frozen or registered.

### Registering a candidate and versioning a ladder rule

Both are decisions a person takes, from the repository root, and a night never takes either. Nothing is registered and no window is open until someone runs one of these.

**A candidate condition** is registered before anything scores it, naming an evaluator the code carries and the values it runs with (see: Candidate conditions are registered before they are scored, and a candidate's picks are shown on the Run page while its outcomes wait for a look):

```
dotnet run --project src/EquityBrief.Worker -- register --candidate "momentum index at thirty" --rule "the relative strength index at or below thirty" --test "the share of its setups that beat their own break-even" --evaluator momentum-index-reading --parameters level=30
```

**Phase 10's three candidates** are registered by one command, which writes all three rows at one instant and none of them if any is refused (see: The three candidates are registered at one instant). Run it once, after the crossing margin is settled and once the phase's code is in the checkout, and never a second time:

```
dotnet run --project src/EquityBrief.Worker -- register --the-three
```

It writes arrived and narrow at a zone no wider than 1 typical daily move, volume against the night at 2 times both the night's median ratio and the name's own fifty-day average, and crossed by a margin at 0.5 of a typical move past the edge the close went through, each with the rule and the test the code carries. At one instant because each candidate's level is divided across the candidates the first night evaluated it also evaluated, so registering them a day apart would test the first at the whole level and the others at a share of it. From the night after, all three stand at 0.05 over 3 and every window opens with three standing. A second run is refused whole, naming the first candidate that already stands, and writes nothing.

The evaluators carried are `momentum-index-reading`, which reads `level`, `momentum-histogram-turn`, which reads `margin`, `arrived-and-narrow`, which reads `width`, `volume-against-the-night`, which reads `multiple`, and `crossed-by-a-margin`, which reads `margin`, beside the swing family's `swing-filter` and the new setup families' `breakout`, which reads `highSessions`, `volumeMultiple`, `rangeCeiling` and `stopMoves`, and `drift`, which reads `windowSessions`, `reactionMoves`, `volumeMultiple`, `targetRiskMultiple` and `stopFloorMoves`. The registrar refuses an evaluator the code does not carry, a parameter the evaluator does not read, a value that is not a finite number, a tenth candidate of one setup family, a registration stating no rule or no test, a retirement stating no evidence, a live reason's name, and any change to a candidate that stands registered. A live reason is retired, and a candidate promoted, only by changing section 11 and the code together (see: A live reason is added or retired only by a change to section 11 and the code together, and the register holds candidates alone). A live reason is retired once its record holds 400 resolved setups, read against the resolved count the run page draws beside it, since no page computes that floor. A candidate standing registered under a name section 11 has come to carry is retired like any other, which is how a promoted candidate leaves the family, and the two procedures below say when either is due. A change is a retirement and a new registration, under the same name or another, and a name registered again stands once:

```
dotnet run --project src/EquityBrief.Worker -- register --retire "momentum index at thirty" --evidence "the figures that produced the retirement"
```

From the next night every standing candidate is evaluated on every name into the shadow column, its picks drawn on the Run page's comparison of tonight's picks alone and its results nowhere until a look reads them, and a registration made while a night runs is evaluated from the night after, since a night reads the register as it stood when the night started; the run page states how many are registered, the family's divisor, and beside it the count of distinct trials and the level each starts at, as the register stands when the page is read. A registration whose rule is one already counted, with only the version it reads moved, adds no trial; one with any threshold or condition moved adds one and lowers the level of every look not yet read; and a retirement taken before any look was read takes its trial out of the count (see: Holm's level passes between the candidates by a graph fixed when they are registered, and its first step is 0.05 over the distinct trials read at a look or still running). A change to any source an evaluation runs through, which `CandidateEvaluator` lists, moves every evaluator's version, and from the next night each standing candidate is skipped and named as a failure on the listings stage's run log row until it is retired and registered again. A name the night holds no readings for is skipped on its row and counted on the listings stage's line. The listings stage appears under the run page's failures only where a registered candidate's evaluator is one the code no longer carries or has moved, and the remedy is a retirement and a new registration (see: Only a missing or moved evaluator fails the shadow column, and a name-night without the readings is a counted skip). Each attempt, refused or not, is one row on the run log under `candidate-register` and a run id beginning `register-`, which the run page draws as run by hand; a command giving both `--candidate` and `--retire`, a flag where a value goes, or a store behind the checkout is refused, the last writing nothing.

**The swing family** is registered by one command, which retires phase 10's three, each on the words saying no result of it was read before it, and registers the live filter at the open filter version's settings with its eight variants, all twelve rows at one instant and none of them if any is refused (see: The three phase 10 candidates are retired when the swing family registers, and each retirement says no result of theirs was read). Run it once, after the merge that carries it and after filter version 1 is open, and before the night, since from that merge the three's evaluator versions have moved and the listings stage names them as failures until they are retired:

```
dotnet run --project src/EquityBrief.Worker -- register --the-family
```

It is refused whole where no filter version is open, where any of the three does not stand registered, and on a second run. From the night after, the nine are evaluated in the swing filter's own stage over every member's gate inputs, the seventh reading the state the night's fundamental readings stored for each member and leaving off a deteriorating one, the eighth reading each member's standing in its sector in place of the trend and strength gate, the ninth firing as the live filter does with its record keeping the night's first three in the list's own order, and the run page's shadow region counts nine registered and a divisor of 9.

**The swing family registered again** is how a rule joins it, as the night's best three did on the operator's ruling of 2026-10-02 (see: The pullback's ninth rule keeps the night's best three in the list's own order, and the family is registered again whole to add it). One command retires every standing swing family rule on the evidence given and registers the family the code writes for the filter version already open, all at one instant and none of them if any is refused; no version opens, so the shape clock's count goes on. Run it after the merge that carries the new rule and before the night, since that merge moves the swing filter rule's evaluator version and the night names the family as failures until it is registered again:

```
dotnet run --project src/EquityBrief.Worker -- register --the-family-again --evidence "the figures or the ruling the family is registered again on"
```

It is refused whole where no filter version is open and where no swing family rule stands. A rule registered again under its own name keeps its record, which is read by name from its first night, and the rule joining it starts its own on the first night after.

**A new setup family's freeze** registers its live rule at the starting point its sweep proposed and the operator approved, with its variants, at one instant and none of them if any is refused, the family's rules counted apart from every other family's (see: The new families freeze at their sweeps' proposals, the breakout's provisional setting and the drift's wider stop registered beside them as variants) (see: Each setup family's correction for luck counts its own rules alone, at most nine a family). The breakouts' writes the live rule and its six variants, the five settings one step from it on the sweep's grid and the provisional setting; the earnings drift's writes the live rule, its five neighbours and the proposal with its stop no closer than one typical move under the buy. Run each after 13.9's merge and before the night, after the pullback's freeze:

```
dotnet run --project src/EquityBrief.Worker -- register --family breakout
dotnet run --project src/EquityBrief.Worker -- register --family drift
```

A second run is refused whole, its names standing, and a family no freeze is written for is refused by name. The commands write the family the code writes now, the live rule switched on the market among its variants since the operator's ruling of 2026-10-03. From the night after, each rule is evaluated over every member at its own settings in the family evaluator's stage, keeps its own list of at most five a night with one open trade a stock, and its trades, results and benchmarks are stored by the family recorder; the run page draws each rule's record beneath the setup families' table (see: A registered family rule is evaluated every night at its own settings and keeps its own list, its trades stored with their benchmark when they end).

**A new setup family registered again** is how a rule joins it once its freeze stands, as each family's market switch did on the operator's ruling of 2026-10-03 (see: The breakout and the earnings drift each register a variant listing only on nights its market switch is open, each family registered again whole and its records replayed). One command a family retires every standing rule of that family on the evidence given and registers the family the code writes, all at one instant and none of them if any is refused. Run it after the merge that carries the new rule and before the night, through the remedy that change commits:

```
dotnet run --project src/EquityBrief.Worker -- register --family-again breakout --evidence "the figures or the ruling the family is registered again on"
```

It is refused where no rule of the family stands, since its freeze registers it, and for a family no freeze is written for. Before it writes, it replays each standing rule of the family at its settings under the code as it stands, over every night the store holds since the rule's record began, and prints a line a rule: `replay: '<rule>' carries its record on: reproduced its N trade(s) over M night(s) from <session>` where every trade the replay keeps is the one the record stored, and `replay: '<rule>' restarts its record at this registration: <the first trade that differed>` where one is not (see: A family rule registered again keeps its record from its first registration where a replay of its stored nights reproduces every trade, and restarts at the change otherwise). A rule that carries on counts its record from where it began; one that restarts counts from the first session on or after the day of this registration, its earlier trades staying stored. Each replay writes one row a rule on the run log under a run of its own named `replay-` and its instant, and the PROGRESS entry that owes the command copies the lines it printed.

**Candidates whose evaluator moved** are registered again by one command, after the merge that moved them and before the night, since from that merge the listings stage names each as a failure until it is registered again (see: A candidate whose evaluator a code change moved is registered again unchanged, every one at one instant):

```
dotnet run --project src/EquityBrief.Worker -- register --moved --evidence "the level builder's touches and band width corrected, which moved every evaluator's version"
```

It retires every standing candidate whose evaluator the code no longer carries at the version it was registered with, on the evidence given, and registers each again with its name, rule, test, evaluator and parameters as they stood and the version the code now carries, all at one instant or none. A setup family rule among them is replayed first, as the family registered again replays each of its rules, and prints the same line a rule, its record carrying on where the replay reproduces every trade and restarting otherwise; every other candidate registered again this way starts its record again, as any new registration does. It is refused where no standing candidate's evaluator has moved, and whole where one's evaluator is no longer carried at all, which is a retirement to write first.

**Promoting a candidate.** The run page's candidates' record region states, for each registered candidate, what its last look read. A promotion is due where that field says the boundary was crossed, and never on the running figure beside it, which is monitoring and moves every night (see: The nightly running figure is monitoring and never the verdict). A look is read at 8, 12 and 16 non-empty blocks of 63 sessions and at no other count, so the field changes only on the night a block completes and only then (see: A candidate's verdict is read only at looks fixed when it is registered, with each look's boundary found over every sign vector its blocks allow). The first look cannot promote: the level it releases is below the smallest p-value eight blocks can produce, so it can only retire.

Where the field says a promotion is due, the promotion itself is a change to section 11 and the code together, as a live reason's addition always is (see: A live reason is added or retired only by a change to section 11 and the code together, and the register holds candidates alone). First read the region and write down what it states: the look, the blocks, the setups, the share against the calibrated bar, the sign-flip p-value, the level the look spent and the step of the graph that level came from. Those figures are the evidence, and the register row below is where they are kept. Then add the condition to section 11 and to the shortlist builder's reasons in one change, with its own checkpoint's entry, as a live reason; the six live reasons become seven, and the family the live threshold is divided by becomes seven with them. Last, retire the candidate from the register, so a promoted candidate leaves the family and its level passes to the candidates still standing, with the evidence opening with the word `promoted` and then the figures read off the region:

```
dotnet run --project src/EquityBrief.Worker -- register --retire "momentum index at thirty" --evidence "promoted at the look of 12 blocks: 214 setups over 12 blocks, 41.2% against a calibrated 37.0%, sign-flip 0.0041 at a level of 0.025 spent 0.0057"
```

The word at the front is what tells a candidate that left the family having been shown from one that left having not, and the graph reads it: a promoted candidate's level passes in equal shares to those still standing, and a retired one's passes to no one.

**Retiring a candidate.** A retirement is due where the region's verdict field says the futility guideline is met, which is read at the first two looks and means the candidate's setups won less often than the bar their own plans and the calibration set, or where it says the last look passed without crossing (see: A candidate is retired by a futility guideline at its first two looks or by reaching its last look). The guideline binds nothing: it is a reason to stop spending nights on a candidate, and a candidate left standing through it is tested at the same boundary as before. Run the same command with the evidence the region states and no `promoted` at the front of it. A retired candidate is evaluated on no further night, its level reaches nobody, and its record stays on the page as the record of what was tried.

**A version of a ladder rule** is scored beside the rule the night applies, so the rule's live window is opened first, carrying the build's own values, and the version beside it under the same parameter names (see: A ladder rule's version is measured beside that rule's live window, and both count against a bound of eighteen):

```
dotnet run --project src/EquityBrief.Worker -- version --rule "the near-exit skip" --live-window
dotnet run --project src/EquityBrief.Worker -- version --rule "the near-exit skip" --version "three typical days" --parameters nearExitInTypicalDays=3
dotnet run --project src/EquityBrief.Worker -- version --list
dotnet run --project src/EquityBrief.Worker -- version --rule "the near-exit skip" --replace "three typical days" --with "four typical days" --parameters nearExitInTypicalDays=4 --evidence "the figures that produced the change"
dotnet run --project src/EquityBrief.Worker -- version --rule "the near-exit skip" --close "four typical days" --evidence "the figures that produced the close"
dotnet run --project src/EquityBrief.Worker -- version --backfill 2026-09-14
```

The five rules and the names each is replayed from are `merge distance` from `typicalMoveMultiple`, `where the stop sits` from `stopTrailsTheLastHigherLow`, `the near-exit skip` from `nearExitInTypicalDays`, `zone edges from non-average anchors only` from `zoneEdgesFromNonAverageAnchorsOnly`, and `the trend rule` from `downtrendFromAverages` and `nightsTheNewLabelHolds`. A multiple is above 0 and written to at most four places, a count of typical days is a whole number from 0, and a flag is 1 or 0; a version at its rule's live values is refused, and so is a parameter named twice (see: A version of a ladder rule is refused at values its replay would not apply as given, or at its rule's live values). At most two windows of the merge distance and four of each other rule are open at once, live windows included, because a merge distance version replays the level stage as well as the ladder stage, where every other version replays the ladder stage alone. A live window is closed only after the versions beside it. `--list` names the open windows with each rule's count against its cap. A change of version is one command, `--replace`, which closes the old window with the evidence that produced the change and the name of the version replacing it and opens that version in the same write, so a rule at its cap can still be changed; `--close` ends a window with nothing replacing it and takes its evidence too (see: A rule version change closes the window with the evidence that produced it and opens its replacement in the same write). A command takes one form: a second form, a flag its form does not take, or a flag where a value goes is refused rather than read as what was probably meant.

**Phase 10's three trend versions** are opened by name, one command each, at the numbers the code carries rather than at numbers typed at the prompt (see: The trend rule is a fifth ladder rule a version replays, and none of its three versions is live). The rule's live window is opened first, as every version's is:

```
dotnet run --project src/EquityBrief.Worker -- version --rule "the trend rule" --live-window
dotnet run --project src/EquityBrief.Worker -- version --trend-version "below both averages"
dotnet run --project src/EquityBrief.Worker -- version --trend-version "below both under a cross"
dotnet run --project src/EquityBrief.Worker -- version --trend-version "the new label holds two nights"
```

The first reads a close below both the 50-day and the 200-day average as a downtrend whatever the swings say; the second reads the same only where the 50-day sits below the 200-day; the third leaves the label alone and holds a name in downtrend until the label that replaced it has stood two nights, the night being scored among them. Each is opened on its own, and a version's scores count from the night after its own window opened, so three opened a day apart measure three stretches of nights and nothing about them needs one instant: a version's level is not divided across the others, which is what the candidates' one instant exists for. A name the code does not offer is refused, naming the three it does. None of the three is live and none becomes live by crossing: a promotion is a ruling, and the ruling closes the live window and opens the version as the rule the night runs.

The run page's versions region states what each open version labelled the night's names, how many names it moves off the night's own label, and how often a stored label went away and came back over the nights the store holds, which is the reading the third version's two nights is settled from and never an outcome (owes: The trend confirmation's nights settled from flip-backs). A version of this rule takes setups away and never adds one, because the label it writes is the one that carries no tranche at all, so its own record is the live rule's setups less the ones its label removes and the difference between them is what a look reads (see: A trend version is judged by the candidates' test on its difference from the live rule). Where both averages versions cross, the narrower is kept unless the wider is ahead by 5 points of win share, and the choice between them is read at the check over both of them rather than at either one's own p-value.

`version --backfill 2026-09-14` scores a past night under the windows open now, from that night's own bars, bands and trend at that night's own price scale, and keeps every score already stored (see: A backfill scores only a night the store computed, at that night's own price scale, and never rewrites a score already stored). It is refused for a day the exchange did not trade, for a session the store holds no bar for, which is every date after the newest session it holds, and for a session no night computed, which is one fetched by a backfill or as a missed session; a name that night computed no bands or plan for is left out and counted. A score counts only for a session after the date in New York its window opened on, and every other score is flagged in sample and counts toward no record (see: A version's score counts only for a session after the New York date its window opened on). A version of the merge distance or of the zone edges is skipped over a band set stored before member sources were written. Its line says what it wrote, kept, left out, skipped and dropped.

Every open, replacement, close and backfill, refused or not, is one row on the run log under `rule-versions`, a refusal under the outcome `refused`, and `--list` writes none. The rows are under a run id beginning `version-`, and the run page draws them as run by hand, apart from the night's own stages. A command against a store behind this checkout is refused and writes nothing, the run log included: run `tools/migrate` first.

**Where a night stops at the rule versions step** naming a rule that moved, the code or the build's values changed while a window measuring that rule was open, and the scores already written say nothing about the rule as it now stands. Close the rule's versions and then its live window, each with `--close` and evidence naming the hash the night's row gives, open them again, and re-run the night, with `--session` once it is past midnight in New York: a night, a replayed one included, scores under the windows the store holds open when its step runs. The closed rows are kept with what they were opened with. A night that stops at the step naming a version whose merge distance no price can hold stops for that version alone: close it.

**Before merging an edit to a pinned source**, close every open window with its evidence and open them again after the merge. The ladder rules' code version is the pin of every file `RuleVersionScorer.CodeVersionSources` lists, so any edit to a line of code in one of them is a new code version, and the next night with a live window open stops at the rule versions step; a comment, a decision citation among them, and a blank line move no pin, so an edit to those alone needs nothing closed. `version --list` names each live window the build no longer hashes to before a night stops on it (see: The ladder rules' code version pins every source a live ladder rule or its replay runs through, its comments and blank lines aside).

**Closing and opening a window again after midnight in New York costs the coming session.** A score counts only for a session after the New York date its window opened on, so a window reopened at, say, twenty past midnight Eastern opens on that day's own date and the night that runs that evening is flagged in sample and counts toward no record. Run the remedy before midnight Eastern where the choice is there, and where it is not, read the first counted session as the one after (see: A version's score counts only for a session after the New York date its window opened on).

### Running a remedy

A remedy is the worker commands a change owes the operator's store: the windows closed and opened again where a pin moved, and the filter's rule correction where the family is registered again. It is committed as a file under `tools/remedies/`, named by date, checkpoint and what it does, one step a line written exactly as the change's PROGRESS entry prints the arguments after `--`, with a comment at the top saying what it is for; the entry's `Remedy:` field names the file. It is run once, from the main checkout after its fast-forward to the merge, with no night, drain or queue running, and before midnight in New York where the choice is there:

```
tools/remedy.ps1 tools/remedies/2026-09-29-12.2-pullbacks-alone.txt
```

The script builds the worker once, runs each step in order printing its number before it, and stops at the first step that fails with that step's exit code, naming the step to start from again once the cause is fixed: `--from N` starts at step N and `--list` prints the steps numbered and runs nothing. It is refused while the night's lock file stands under the data root, `EquityBrief__DataRoot` where that is set and `data/` otherwise, because a remedy closes the windows a running night scores under. The first file committed is the remedy of the 12.2 correction of 2026-09-29, which the operator ran on 2026-09-30, and every remedy after it is issued the same way: a session that writes a remedy into a scratch folder of its own has left the record.

---

## Moving the installation

The whole system is a checkout and one database file.

1. Clone the repository on the new machine.
2. Install a .NET SDK in the `10.0.3xx` band, which is what `global.json` pins and what the six projects need to build against `net10.0`. Without one, `tools/migrate` below fails with a restore error that names neither.
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

**A night that stops tries again by itself.** A step before the close that fails or passes the night's deadline stops that try, and the night runs again from that step fifteen minutes later, up to three more times, each try under a deadline of its own. While it waits, tonight's page and the Run page say it is waiting to try again, when the next try starts and every try so far with the step it stopped at and its reason. A night refused before its first step, one whose day's allowance is spent and a step after the close are not tried again (see: A night that stops before its close is tried again from the step that stopped, three more times fifteen minutes apart, each try under a deadline of its own).

**Running the rest of a night left unfinished.** Where both pages say the night was left unfinished, read each try's reason first: a fault that stopped four tries the same way will stop a fifth, and wants a fix before anything else. Then press "Run the rest of the night" on either page, or from the checkout:

```
tools/nightly.ps1 --resume
```

It runs the newest night on the run log from the first step its tries have not finished, as one more try under that night's id and on that night's session, and says so and does nothing where the night finished. Run the next morning for the evening before, it asks for no quarters and no report, as a night run for a named session does. A night holds a lock file under the data root while it runs, so the press and the command are refused while a night runs, and the pages say which run holds it (see: A night left unfinished is run to its end from the step it stopped at by a press or a command, and one night runs at a time under a lock file). A night for an earlier session is still run whole with `--session`.

| Symptom | Likely cause | What to do |
|---|---|---|
| Tonight's list is absent and a banner gives an old data date | the bulk price feed did not answer | nothing; the design keeps last night's bars and refuses to compute a list from them. Check the provider's status, then re-run `tools/nightly` |
| One name's chart shows a gap and its plan says not computed | a gap in that name's series | expected behaviour, not a fault. An interpolated bar would produce averages and swings that never happened. It clears when the provider fills the session |
| A name is marked suspect | the corporate action check itself failed | nothing at first: the check asks for the name's year again on each of the next 5 nights and then weekly, and the name's page opens with a line saying its prices may not reflect a recent dividend or split, its row on tonight's list says so, and the run page's failed region names the name, on every night it stays suspect. A night re-run by hand counts as one of the 5, except a re-run of the night the action landed, which finds the action again and starts the count at none. Where the run page's line says its retries are spent, the refetch has failed on 6 nights running, counting a night re-run by hand as one, and the line gives when it was last asked for and why, so read the reason: the name's levels are computed over its stored series, which does not carry the action's adjustment, and the check asks for it again 7 days after the session it was last asked for, and every 7 days after that, until a refetch succeeds or the name leaves the index. When it leaves, the run page's region stops naming it, because that region is about tonight; its name page and its exported report keep their line, because they are about a name whose stored prices are still the ones that may not reflect the action. No verb asks for one name's year, and the store is never edited by hand |
| A name's page opens with a line saying no year of prices came back, and the run page's failed region names the backfill | the provider returned nothing when the backfill asked for the name's year | nothing at first: the backfill asks again on each of the next 5 nights and then on the first night 7 or more days after the session it was last asked for, until one stores its year or the name leaves the index, and the page's line says how many nights it has been asked for and when it is asked next. A ticker the membership feed lists and the price file carries under another ticker never gets a year, because the feed's listing is the index (see: A ticker the index feed stops listing leaves the index on the night it goes unlisted), so it stays without prices until the provider's two files agree |
| The listings stage's run log names a name with a dated event beyond the exchange calendar | the calendar holds a print dated past the last date the exchange closure table covers | nothing for that name tonight: its earnings soon is not counted and does not fire, and its row says beyond the exchange calendar. If the closing line also names the table's end, extend the table from the exchange's published closures, which is the operating obligation about the closure table, and the count returns the next night |
| A past night's list, run page, the universe screen, a name page or its exported report carries a line saying its listings were written before a correction | the rows are from before the 5.4 correction, when earnings soon counted stored bars after the night and breakout on volume could not fire | nothing; the rows are kept as written, because a listing records what its night listed, and the run page's record for those two reasons already leaves them out, with the line above the records saying how many nights those two stand on. A name page and its report read the newest night, so their line shows only while the newest night's rows were written before the correction |
| Tonight's notice and the run page say the night was refused before its first step | the main checkout was off main, or held a commit origin/main does not have, when the scheduler ran `tools/nightly` | put the checkout back on main at or behind origin/main, which is what a merge's fast-forward leaves, and run `tools/nightly.ps1 --session <yyyy-MM-dd>` for the evening; the refusal clears when a night next passes the check. An uncommitted edit or a stray file is never the cause, since neither is built |
| A night is missing from the run page altogether | the night was refused before it knew where the store is | the store is the only record this system keeps, so a refusal that happens before the data root resolves cannot be written to it. Read the scheduler's own history: Task Scheduler's `Last Run Result` on Windows, `launchctl list` on macOS. Every refusal after that point does write a row, under the first step with an outcome of `refused` rather than `failed`, so a refused night and a failed migration are different lines on the page |
| A screen says the store is at one schema and the checkout reads another | the checkout was updated with a migration that neither `tools/migrate` nor a night has applied | run `tools/migrate`. The night applies it at its first step as well; until one has, the screens name both schema numbers and the run page draws only its run log, and the `version` verb refuses and writes nothing |
| The run page says the queue did not run | the machine slept, or the night stopped before the overnight queue | expected to be visible rather than silent. Listed names open without a draft, as normal, and the next night that runs drafts them. Where the page says the queue could not run, the local model was not answering: start the runtime and load the model the settings name |
| A night's step or a research pass failed with `database is locked` | another writer held the store for more than ten minutes, which no stage the night has measured does | find what else was writing, and once it has finished run the night again for its session with `tools/nightly --session <yyyy-MM-dd>`: the arithmetic comes to the same stored state and the queue drafts the names it did not reach. A pass is started again from its page (see: A writer waits up to ten minutes for another, and a pass stores what it fetched in one write) |
| A research section is absent with a line saying no admissible source was found | every candidate document failed admissibility | not a fault. Writing the section from a price forecast or a year-old article would be worse than the gap |
| A section says fallback | the model was unreachable, or the claim checker rejected the section at its first draft and every retry | the computed report is complete and useful on its own. The run log names the offending text, and a regenerate the same day writes the section again |
| Research is paused | the spend cap was reached | it resumes at the start of the next period. The cap is a hard stop by design |
| The labeller's `news-labels` row on the run log says its model did not answer, or names the month limit, the cap, the time limit or a peak window as its stop | the news job's model is down or retired, or a limit the labeller runs under was reached | nothing for a limit: what was left waits for the next night. For a model that does not answer, read the provider's status, and where the model is retired switch the one word `EquityBrief:Models:News:Use` to another profile; nothing falls back by itself |
| The phase report is green but the lab did something wrong last night | a green report is a statement about the build and never about the running system | the two are different subjects. Nothing in the harness reaches `data/`, and a property about the running system is asserted by a guard the code carries or read on the morning it happens |

---

## Store maintenance

**Never edit the store by hand.** Every table has one declared writer per operation and `writer-ownership` asserts it in both directions; a hand edit is a write nobody declared.

**The night copies it, and keeps the newest three copies** (see: The store is copied once the night and every process it started have finished, and the newest three copies are kept after each is opened and read). After it starts the labeller, the night starts the copy as a process of its own. It waits while the night holds its lock, for the labeller until its last row or its time limit and ten minutes more, and for the drain, whose lock it then holds while it copies, so a press that asks for a report meanwhile waits a minute for it. It copies through SQLite's own backup into the copies' folder, names the copy by its instant, `equitybrief-yyyyMMddTHHmmssZ.db`, opens it and reads its bars back against the store's, and keeps the newest three of the copies its own run log names, opening and reading each before any older one is removed and removing none where one does not; a copy another store made in the folder it neither counts nor removes (see: A store's copy counts and removes only the copies its own rows name, and a test or a rehearsal names a copies' folder of its own). It gives up after twenty hours. Its row is under `backup-` and its instant with the stage `store-backup`, and the run page draws the newest copy's time and folder beneath anything to worry about.

**The copies' folder is a setting of the worker's,** `EquityBrief:Backup:Folder`, absolute or under the data root, and `backups` under the data root where it is not set. It is set on a machine's own and never in `appsettings.json`, since a folder is a path of that machine: on the operator's Windows machine, on the operator's ruling of 2026-10-02, it is `E:\EquityBrief-backups`, set in the worker's `appsettings.Secrets.json`, which the night and every process it starts read. The copy's row names the folder relative to the data root, and the read surface reads it back against its own, so the surface needs no setting of its own. A folder on the store's own disk guards against a damaged file and not against the disk failing. Every worker run on the machine reads the same setting, so a night rehearsed over a copy of the store names a folder of its own under its own root beside its data root, `$env:EquityBrief__Backup__Folder = "$r/backups"` with `$env:EquityBrief__DataRoot = $r`; otherwise its copy lands in the operator's folder, where the operator's own copies never count it.

**A copy can be made by hand,** waiting for no labeller:

```
dotnet run --project src/EquityBrief.Worker -- backup
```

**To restore from a copy,** which is the operator's decision alone (see: The operator's store is never deleted, and every site that removes a file is stated where a check holds it), stop the read surface and make sure no night, drain or labeller is running, move the damaged store aside rather than deleting it, copy the copy the run page's line names into the data root as `equitybrief.db`, run `tools/migrate`, and start the read surface again. The run page's line names the newest copy that opened and read. No row holds an absolute path, so a copy works on either machine.

**A migration is applied by `tools/migrate` and never by an application start-up path.** A store that migrates itself when the app runs will migrate on a machine the operator did not intend, and a stage failing on a missing column is how that is discovered. After updating the checkout, run `tools/migrate` before starting the read surface or running a verb; the screens and the loop's verbs name a store behind the checkout rather than migrate it.

---

## What this system does not do

It does not trade, hold a position, or size one. It does not rank names against each other. It does not tell you a stock will go up. The list says a name is sitting at a price its own chart has made significant, and the levels and the plan are both arithmetic recomputed from that chart the same evening.
