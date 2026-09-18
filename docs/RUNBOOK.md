# RUNBOOK.md

How the thing is operated. Written for the operator on the morning something looks wrong.

---

## What runs, and when

Two jobs. Neither is part of the application, because scheduling lives outside it and the code has to run unmodified on Windows and macOS.

| Job | When | What it does | Costs |
|---|---|---|---|
| `tools/nightly` | after the US close | the arithmetic: membership, bars, corporate actions, indicators, swings, volume profile, levels, trend, ladder, moves, listings, facts, forward returns, news pulse | one bulk bar request, one news feed request, a handful of calendar and membership calls. No model call |
| the overnight queue | after the arithmetic, same invocation | the local model writes the local lane's sections that rest on no document, for listed names whose research is missing or stale, in priority order, starting no pass once the configured hours have passed | nothing, and no request |

**Every schedule is expressed in UTC.** The research provider's peak and off-peak windows are fixed in UTC, and a schedule written in local time moves into peak when daylight saving changes with nothing to announce it. Convert for display only.

**The overnight queue holds the machine awake while it works.** A laptop left to itself sleeps, and a nightly job that silently did not run is worse than no nightly job. On Windows it takes a power request and on macOS a power assertion, released when the queue ends, and on any other machine it takes none; the queue's row on the run log says which. The run page states the night's outcome, including how many queued passes completed and how many were left, and names every traded session since the queue last ran on which it did not run (see: A night the overnight queue did not run is a traded session with no queue row, read on the run page against the exchange calendar).

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
| Tavily | open web search, for theme material only | free tier is a thousand credits a month; a theme pass makes twelve searches, one a site of the industry list, at one credit each at the basic depth, so a few hundred passes a year come to a few thousand searches |
| the research model's provider, DeepSeek as shipped | the research model | named in configuration and nowhere in the code, so another provider is a change of settings; the shipped one's peak rates are double and its peak falls late at night in Eastern time, so reading after the close is never billed at peak |
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
| SEC EDGAR | `EquityBrief:Providers:SecEdgar:Contact` | `EquityBrief.Worker` |
| the research model's provider | `EquityBrief:Models:Research:ApiKey` | `EquityBrief.Worker` |
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

The queue runs as the night's last step, in the same invocation, after the arithmetic has closed and recorded its counts, over the local model and nothing else. It writes the local lane's sections that rest on no document, which in this machine's lane is the key under each figure: what the company sells and the segment commentary rest on the company's own filing, which the pass an open starts fetches, and the night fetches nothing for a name (see: The overnight queue writes the local lane's sections that rest on no document, and the paid model is for names you get serious about). A name whose only outstanding section rests on documents is not queued.

| Setting | Key | Default |
|---|---|---|
| the hours after which the queue starts no pass | `EquityBrief:Queue:Hours`, a whole number above zero | `1` |

**An hour covers the whole index at the rate measured on this machine.** 6.10 measured 12 passes on the local model the settings name, over the fixture's four listed names on three nights: 3.35 seconds a pass on average and 5.13 at the slowest, so 503 members at the slowest come to 43 minutes. The queue starts no pass once the hours have passed and finishes the one it is in, so it runs past them by at most one pass; the night's fifteen-minute deadline bounds the arithmetic and not the queue (see: The overnight queue is bounded by its own limit rather than the night's deadline, and starts no pass once the limit has passed). Measure again after a change of model or machine, and set the hours from what a pass takes there.

**Where to read what it did.** The queue's own row on the run log, under the night's run with the stage `overnight queue`, says what it came to: `ok` where it ran through every name it queued, `limit` where it stopped at its hours with names left, and `unavailable` where the local model did not answer, which stops the queue at that name. Its detail names the names listed and queued, every pass it ran under a run of its own with what each wrote, the names it left, and whether the machine was held awake. Each name's pass writes the judge's, the writer's and the checker's rows under that pass's run.

### The research model's settings and the spend caps

The research model is the one part of the system that costs money, and nothing in the code says which model it is. The shipped `appsettings.json` beside the worker names the provider and the model this installation uses and the rates that provider charges for it; a different provider or model is different values, set in `appsettings.Secrets.json` or the environment, either of which wins over the shipped file. Its key is in the table above and is refused by name at startup when it is blank, on a fixture run as on a live one.

| Setting | Key | As shipped |
|---|---|---|
| the wire format the provider serves | `EquityBrief:Models:Research:Format` | `openai` |
| where the provider answers | `EquityBrief:Models:Research:BaseAddress` | `https://api.deepseek.com/` |
| which model answers | `EquityBrief:Models:Research:Model` | `deepseek-flash` |
| the provider's own request fields, written as the JSON object it takes | `EquityBrief:Models:Research:Options` | none |
| how long one call may take, in seconds | `EquityBrief:Models:Research:TimeoutSeconds` | `600` |
| the most one answer may run to, in tokens | `EquityBrief:Models:Research:AnswerTokens` | `32768` |
| dollars per million prompt tokens the provider serves from its cache | `EquityBrief:Models:Research:Prices:CacheHit` | `0.003` |
| dollars per million prompt tokens it does not | `EquityBrief:Models:Research:Prices:CacheMiss` | `0.15` |
| dollars per million output tokens, reasoning included | `EquityBrief:Models:Research:Prices:Output` | `0.60` |
| the UTC hours the rates are multiplied in, one entry per window written as a start and an end hour | `EquityBrief:Models:Research:Prices:PeakHours` | `01-04, 06-10` |
| the days those hours fall on, one entry per day | `EquityBrief:Models:Research:Prices:PeakDays` | `Monday, Tuesday, Wednesday, Thursday, Friday` |
| what the rates are multiplied by in those hours | `EquityBrief:Models:Research:Prices:PeakMultiple` | `2` |
| the most research may spend in a UTC day, in dollars | `EquityBrief:Spend:DayCap` | `10` |
| the most research may spend in a UTC month, in dollars | `EquityBrief:Spend:MonthCap` | `50` |

**Switching model is a change to these values and the key, and to nothing else.** A provider serving the OpenAI chat completions format takes its address, its model and its key, and its rates from its own price page; a provider with no peak pricing names no peak hours, and its multiple is then read as one. Options are the provider's own fields sent beside the request, as the shipped provider takes `{"thinking":{"type":"disabled"}}` to answer without reasoning first. A model asked with options is recorded as a different writer from the same model asked without them, so a section says which model wrote it and how it was asked. The one format this build implements is `openai`, and another is refused at startup rather than answered by this one (see: The research model is one interface with an implementation per wire format, chosen by configuration and never falling back).

**A model with no prices is refused at startup**, rather than called and recorded as costing nothing, and so is a rate at or below zero for the uncached prompt or the output, a peak window whose start is not before its end, a multiple below one, a day that is not a day of the week, and options that set the model, the messages, the answer's budget or the stream, which the feed writes itself. The shipped rates are what the provider's page gave on 2026-09-13 for the shipped model. When the provider changes a price, these values are what change, and a call already made keeps the price its run log row recorded (see: The research model is named only in configuration, and a call is priced at the configured rates its own timestamp falls in).

**Both caps are proposals**, marked so in section 17, and the obligation that settles them fires on the run page once twenty research passes carry a recorded cost. A cap stops research rather than warning about it: a call is refused before it is made where the most it could cost would take the day or the month past its cap, research resumes when that UTC day or month ends, and the name page says research is paused and when it resumes (see: The spend cap is a stop, not an allowance).

### Writing one name's research

A pass writes one name's research: the sections not yet written, the ones gone stale, and the ones left out on an earlier day. The name page's control starts it, and so does this, from the repository root, which is all the control does:

```
dotnet run --project src/EquityBrief.Worker -- research --ticker KEYS
```

`--refresh` writes every section again, and `--paid-for-local` has the research model write the local lane's sections as well, which is the page's option where the local model is unavailable or cannot hold one. `--live` and `--fixture <folder>` choose the source for one run, as they do for the night.

**What it does, in order.** It fetches the name's fundamentals where the store holds none, and where that stores a filing the night had not seen it assembles the night's facts file again, so the pass writes from the quarter it just fetched (see: A name's facts file is assembled again for its night when an open fetches its fundamentals). It asks the staleness judge which sections stand. Where the paid lane has work it asks the research model whether it answers, and does not start where it does not (see: A research pass does not start where the research model does not answer). It fetches its latest results release from the filings archive, then the name's news inside each stored move and from the release's filing date to the night, overlapping spans once, tests each document for admissibility as it arrives and stores it with the verdict. It hands each section the documents code picks for it: two a move for the cause of each move, and six since the release beside the release itself for the sections built across the evidence (see: A research pass reads a name's news inside each stored move and since the company's own filing, and hands each section the documents code picks from it, the company's own filing first). A window the provider has more of than a query reads is named as unread on the pass's row, and the sections are written from the windows that were read. The local lane writes its sections, the spend cap makes every paid call, and the claim checker reads each section; a section refused once is written once more in the same pass, and the short version is written last, from the sections already accepted.

**What a pass costs.** Over the fixture's KEYS a pass wrote all eight sections it could write, three on the local model and five through the spend cap for $0.0201. The name page states what the passes before it cost beside its control, because a pass's price is known only once it has been made. A second press on the same day starts nothing, because a pass for the name already ran that day, and a press while a pass for the name is running is refused by name; the page's rewrite and its option to have the paid model write the local lane's sections still start one (see: A name opened again on the day its research pass ran starts no second pass unless the page asks for one).

**What the control does.** It sends the press with a header of the page's own, and the read surface refuses a request without one, so another site's page open in a browser on this machine cannot start a pass (see: A pass is started only by a request carrying the name page's own header). The surface refuses a name the index does not hold, then starts the command above from the checkout it runs in, telling the worker the data root it reads so the pass writes the store the page shows, and returns at once. It writes nothing itself: the page shows what the pass wrote when it is opened again.

**The industry cycle is the theme's.** A theme is the industry the index names for a member, and one theme pass writes the cycle every member of that industry reads (see: A theme is the industry the index names for a member, and one theme pass serves every member it names). A name's pass refreshes its theme before anything of its own where the cycle is missing, left out on an earlier day, or older than a trigger the judge fired for the name. The theme pass searches each site on the industry list for the industry, one search a site over the quarter to the day, asking each for its first three results and each page's text (see: A theme pass searches each site on the industry list alone, and hands the model a bounded set of the pages they return); it drops a result from a site the list does not carry and a result whose text is missing or no longer than its snippet, names both on its row, tests every page it keeps for admissibility and stores it whole, and has the spend cap make one call over at most ten of the pages it admitted, each carried as its first 30,000 characters, which states no figure. It does not start inside the research model's peak windows, which the configured prices state in UTC: its row says when the window closes, the name's own sections are written without it, and the next open of any member after that refreshes it (see: A theme refresh runs off-peak, and a name opened at peak is written without one). A theme is researched once a day at most, so a second member opened the same evening reads what the first one's pass came to, and a search that found nothing is not run again that day. A theme pass costs twelve searches against the tool's monthly allowance and one paid call, or two where the checker refuses the first draft: over the ten Semiconductors pages the fixture's searches kept, the two calls cost $0.0119. Where the list's sites carry nothing about an industry's prices the model writes nothing and the cycle is left out with that line: over the eleven pages the searches kept for Scientific & Technical Instruments, being job postings, labour and price releases, statistics pages and trade news, its one call came back empty.

**Where to look.** Every stage of a pass is a row on the run log under one run, `research-<instant>-<TICKER>`: `fundamentals`, `staleness`, `theme research` where the pass refreshed its theme, whose detail names the sites it dropped and the addresses short of a document, and `theme claims` ahead of it where the theme wrote a cycle to check, `prose`, one `research call:` row per paid call, `claims`, the second and third rounds' rows named for their round, and `research` last, whose detail says what was written, what was not and why, and what the documents came to.

### Exporting one name's report

The name page carries a link, *Export this report as a file*, and the browser saves what it answers wherever the operator chooses. The read surface answers the same file at `/exports/name/<TICKER>`, which is the address the link asks for.

The file is the name page's own region, drawn by the same code from the same store, in a document that needs nothing else to be read: its styles are inline, it carries no script and fetches nothing, every disclosure is open, and a link to another of the application's pages is kept as its words (see: A single report can still be exported as a self-contained file). It leaves out the research controls and the pause, which are the application asking the operator something rather than part of the report. It is named for the name and the newest session its figures are from, `EquityBrief-KEYS-2026-09-08.html`, so two exports on different nights are two files, and its opening line states that session. Nothing is written to the store by an export.

### Registering a candidate and versioning a ladder rule

Both are decisions a person takes, from the repository root, and a night never takes either. Nothing is registered and no window is open until someone runs one of these.

**A candidate condition** is registered before anything scores it, naming an evaluator the code carries and the values it runs with (see: Candidate conditions are registered before they are scored, and scored in shadow before they are shown):

```
dotnet run --project src/EquityBrief.Worker -- register --candidate "momentum index at thirty" --rule "the relative strength index at or below thirty" --test "the share of its setups that beat their own break-even" --evaluator momentum-index-reading --parameters level=30
```

The evaluators carried are `momentum-index-reading`, which reads `level`, and `momentum-histogram-turn`, which reads `margin`. The registrar refuses an evaluator the code does not carry, a parameter the evaluator does not read, a value that is not a finite number, a ninth candidate, a registration stating no rule or no test, a retirement stating no evidence, a live reason's name, and any change to a candidate that stands registered. A live reason is retired, and a candidate promoted, only by changing section 11 and the code together (see: A live reason is added or retired only by a change to section 11 and the code together, and the register holds candidates alone). A live reason is retired once its record holds 400 resolved setups, read against the resolved count the run page draws beside it, since no page computes that floor. A candidate standing registered under a name section 11 has come to carry is retired like any other, which is how a promoted candidate leaves the family; no candidate can be promoted yet, because nothing computes a candidate's record (owes: A candidate's shadow record computed, and a promotion written against it). A change is a retirement and a new registration, under the same name or another, and a name registered again stands once:

```
dotnet run --project src/EquityBrief.Worker -- register --retire "momentum index at thirty" --evidence "the figures that produced the retirement"
```

From the next night every standing candidate is evaluated on every name into the shadow column and shown nowhere, and a registration made while a night runs is evaluated from the night after, since a night reads the register as it stood when the night started; the run page states how many are registered and the divisor that sets, as the register stands when the page is read. A change to any source an evaluation runs through, which `CandidateEvaluator` lists, moves every evaluator's version, and from the next night each standing candidate is skipped and named as a failure on the listings stage's run log row until it is retired and registered again. A name the night holds no readings for is skipped on its row and counted on the listings stage's line. The listings stage appears under the run page's failures only where a registered candidate's evaluator is one the code no longer carries or has moved, and the remedy is a retirement and a new registration (see: Only a missing or moved evaluator fails the shadow column, and a name-night without the readings is a counted skip). Each attempt, refused or not, is one row on the run log under `candidate-register` and a run id beginning `register-`, which the run page draws as run by hand; a command giving both `--candidate` and `--retire`, a flag where a value goes, or a store behind the checkout is refused, the last writing nothing.

**A version of a ladder rule** is scored beside the rule the night applies, so the rule's live window is opened first, carrying the build's own values, and the version beside it under the same parameter names (see: A ladder rule's version is measured beside that rule's live window, and both count against the bound):

```
dotnet run --project src/EquityBrief.Worker -- version --rule "the near-exit skip" --live-window
dotnet run --project src/EquityBrief.Worker -- version --rule "the near-exit skip" --version "three typical days" --parameters nearExitInTypicalDays=3
dotnet run --project src/EquityBrief.Worker -- version --list
dotnet run --project src/EquityBrief.Worker -- version --rule "the near-exit skip" --replace "three typical days" --with "four typical days" --parameters nearExitInTypicalDays=4 --evidence "the figures that produced the change"
dotnet run --project src/EquityBrief.Worker -- version --rule "the near-exit skip" --close "four typical days" --evidence "the figures that produced the close"
dotnet run --project src/EquityBrief.Worker -- version --backfill 2026-09-14
```

The four rules and the names each is replayed from are `merge distance` from `typicalMoveMultiple`, `where the stop sits` from `stopTrailsTheLastHigherLow`, `the near-exit skip` from `nearExitInTypicalDays`, and `zone edges from non-average anchors only` from `zoneEdgesFromNonAverageAnchorsOnly`. A multiple is above 0 and written to at most four places, a count of typical days is a whole number from 0, and a flag is 1 or 0; a version at its rule's live values is refused, and so is a parameter named twice (see: A version of a ladder rule is refused at values its replay would not apply as given, or at its rule's live values). At most two windows of the merge distance and four of each other rule are open at once, live windows included, because a merge distance version replays the level stage as well as the ladder stage, where every other version replays the ladder stage alone. A live window is closed only after the versions beside it. `--list` names the open windows with each rule's count against its cap. A change of version is one command, `--replace`, which closes the old window with the evidence that produced the change and the name of the version replacing it and opens that version in the same write, so a rule at its cap can still be changed; `--close` ends a window with nothing replacing it and takes its evidence too (see: A rule version change closes the window with the evidence that produced it and opens its replacement in the same write). A command takes one form: a second form, a flag its form does not take, or a flag where a value goes is refused rather than read as what was probably meant.

`version --backfill 2026-09-14` scores a past night under the windows open now, from that night's own bars, bands and trend at that night's own price scale, and keeps every score already stored (see: A backfill scores only a night the store computed, at that night's own price scale, and never rewrites a score already stored). It is refused for a day the exchange did not trade, for a session the store holds no bar for, which is every date after the newest session it holds, and for a session no night computed, which is one fetched by a backfill or as a missed session; a name that night computed no bands or plan for is left out and counted. A score counts only for a session after the date in New York its window opened on, and every other score is flagged in sample and counts toward no record (see: A version's score counts only for a session after the New York date its window opened on). A version of the merge distance or of the zone edges is skipped over a band set stored before member sources were written. Its line says what it wrote, kept, left out, skipped and dropped.

Every open, replacement, close and backfill, refused or not, is one row on the run log under `rule-versions`, a refusal under the outcome `refused`, and `--list` writes none. The rows are under a run id beginning `version-`, and the run page draws them as run by hand, apart from the night's own stages. A command against a store behind this checkout is refused and writes nothing, the run log included: run `tools/migrate` first.

**Where a night stops at the rule versions step** naming a rule that moved, the code or the build's values changed while a window measuring that rule was open, and the scores already written say nothing about the rule as it now stands. Close the rule's versions and then its live window, each with `--close` and evidence naming the hash the night's row gives, open them again, and re-run the night, with `--session` once it is past midnight in New York: a night, a replayed one included, scores under the windows the store holds open when its step runs. The closed rows are kept with what they were opened with. A night that stops at the step naming a version whose merge distance no price can hold stops for that version alone: close it.

**Before merging an edit to a pinned source**, close every open window with its evidence and open them again after the merge. The ladder rules' code version is the pin of every file `RuleVersionScorer.CodeVersionSources` lists, so any edit to one of them, a comment included, is a new code version, and the next night with a live window open stops at the rule versions step. `version --list` names each live window the build no longer hashes to before a night stops on it (see: The ladder rules' code version pins every source a live ladder rule or its replay runs through).

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

| Symptom | Likely cause | What to do |
|---|---|---|
| Tonight's list is absent and a banner gives an old data date | the bulk price feed did not answer | nothing; the design keeps last night's bars and refuses to compute a list from them. Check the provider's status, then re-run `tools/nightly` |
| One name's chart shows a gap and its plan says not computed | a gap in that name's series | expected behaviour, not a fault. An interpolated bar would produce averages and swings that never happened. It clears when the provider fills the session |
| A name is marked suspect | the corporate action check itself failed | nothing at first: the check asks for the name's year again on each of the next 5 nights and then weekly, and the name's page opens with a line saying its prices may not reflect a recent dividend or split, its row on tonight's list says so, and the run page's failed region names the name, on every night it stays suspect. A night re-run by hand counts as one of the 5, except a re-run of the night the action landed, which finds the action again and starts the count at none. Where the run page's line says its retries are spent, the refetch has failed on 6 nights running, counting a night re-run by hand as one, and the line gives when it was last asked for and why, so read the reason: the name's levels are computed over its stored series, which does not carry the action's adjustment, and the check asks for it again 7 days after the session it was last asked for, and every 7 days after that, until a refetch succeeds or the name leaves the index. When it leaves, the run page's region stops naming it, because that region is about tonight; its name page and its exported report keep their line, because they are about a name whose stored prices are still the ones that may not reflect the action. No verb asks for one name's year, and the store is never edited by hand |
| A name's page opens with a line saying no year of prices came back, and the run page's failed region names the backfill | the provider returned nothing when the backfill asked for the name's year | nothing at first: the backfill asks again on each of the next 5 nights and then on the first night 7 or more days after the session it was last asked for, until one stores its year or the name leaves the index, and the page's line says how many nights it has been asked for and when it is asked next. A ticker the membership feed lists and the price file carries under another ticker never gets a year, because the feed's listing is the index (see: A ticker the index feed stops listing leaves the index on the night it goes unlisted), so it stays without prices until the provider's two files agree |
| The listings stage's run log names a name with a dated event beyond the exchange calendar | the calendar holds a print dated past the last date the exchange closure table covers | nothing for that name tonight: its earnings soon is not counted and does not fire, and its row says beyond the exchange calendar. If the closing line also names the table's end, extend the table from the exchange's published closures, which is the operating obligation about the closure table, and the count returns the next night |
| A past night's list, run page, the universe screen, a name page or its exported report carries a line saying its listings were written before a correction | the rows are from before the 5.4 correction, when earnings soon counted stored bars after the night and breakout on volume could not fire | nothing; the rows are kept as written, because a listing records what its night listed, and the run page's record for those two reasons already leaves them out, with the line above the records saying how many nights those two stand on. A name page and its report read the newest night, so their line shows only while the newest night's rows were written before the correction |
| A night is missing from the run page altogether | the night was refused before it knew where the store is | the store is the only record this system keeps, so a refusal that happens before the data root resolves cannot be written to it. Read the scheduler's own history: Task Scheduler's `Last Run Result` on Windows, `launchctl list` on macOS. Every refusal after that point does write a row, under the first step with an outcome of `refused` rather than `failed`, so a refused night and a failed migration are different lines on the page |
| A screen says the store is at one schema and the checkout reads another | the checkout was updated with a migration that neither `tools/migrate` nor a night has applied | run `tools/migrate`. The night applies it at its first step as well; until one has, the screens name both schema numbers and the run page draws only its run log, and the `version` verb refuses and writes nothing |
| The run page says the queue did not run | the machine slept, or the night stopped before the overnight queue | expected to be visible rather than silent. Listed names open without a draft, as normal, and the next night that runs drafts them. Where the page says the queue could not run, the local model was not answering: start the runtime and load the model the settings name |
| A research section is absent with a line saying no admissible source was found | every candidate document failed admissibility | not a fault. Writing the section from a price forecast or a year-old article would be worse than the gap |
| A section says fallback | the model was unreachable, or the claim checker rejected twice | the computed report is complete and useful on its own. The run log names the offending text |
| Research is paused | the spend cap was reached | it resumes at the start of the next period. The cap is a hard stop by design |
| The phase report is green but the lab did something wrong last night | a green report is a statement about the build and never about the running system | the two are different subjects. Nothing in the harness reaches `data/`, and a property about the running system is asserted by a guard the code carries or read on the morning it happens |

---

## Store maintenance

**Never edit the store by hand.** Every table has one declared writer per operation and `writer-ownership` asserts it in both directions; a hand edit is a write nobody declared.

**Back it up by copying the file.** No row holds an absolute path, so the copy works anywhere.

**A migration is applied by `tools/migrate` and never by an application start-up path.** A store that migrates itself when the app runs will migrate on a machine the operator did not intend, and a stage failing on a missing column is how that is discovered. After updating the checkout, run `tools/migrate` before starting the read surface or running a verb; the screens and the loop's verbs name a store behind the checkout rather than migrate it.

---

## What this system does not do

It does not trade, hold a position, or size one. It does not rank names against each other. It does not tell you a stock will go up. The list says a name is sitting at a price its own chart has made significant, and the levels and the plan are both arithmetic recomputed from that chart the same evening.
