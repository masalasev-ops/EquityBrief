# SCHEMA.md

The store, and the only place data ownership is declared. `writer-ownership` reads this file and reconciles it against the code in both directions: every writer declared here exists, and every writer in the code is declared here.

One SQLite file under the configured data root. One year of bars for five hundred names plus everything else is comfortably under a gigabyte, so the store is copied, diffed and backed up by hand.

---

## Conventions

**Prices and money are TEXT in storage and `decimal` in code. Statistics are `REAL` in storage and `double` in code.** Never `REAL` for a price. A helper that crosses the boundary does so explicitly and is named for it. `price-storage-form` asserts the storage half, which code review cannot see.

**Every table is `STRICT`, and that is not the money rule.** A `STRICT` table refuses a value it cannot losslessly convert, which is what stops text reaching an INTEGER column. It does not stop a double reaching a money column: SQLite renders the double as text and stores that, because the conversion is lossless. So the money rule rests on `price-storage-form` reading the migration text and on prices being `decimal` in code, and the suite pins the coercion with a test so the limit is read rather than rediscovered.

**Every instant is UTC.** A session date is a date, not an instant, and is stored as `TEXT` in `YYYY-MM-DD`. Where both a date and the instant that produced it matter, both are stored; the date carries the as-of semantics and the instant carries distinctness.

**No column holds an absolute path.** `store-portability` asserts it over a populated store.

**Grain is stated for every table.** A table with no stated grain is a table nobody can reason about.

**The applied schema version is SQLite's `user_version` pragma, not a table.** A version a table holds is a table this file would have to declare, with a grain, columns and an owner, to record what the file format already records. `schema-columns` asserts that every table in the store is one this file describes, and a migrations table nobody declared would fail it.

---

## Ownership summary

Operations are Insert, Update and Delete. A table may have different owners for different operations; it may never have two owners for one operation.

**Creating or altering a table is not one of the three.** The migration runner writes the schema and inserts nothing, so it owns no row in the table below. What a migration may do to a bar table is `bar-append-only`'s business, and that check reads every migration.

**The suite is exempt.** It writes to throwaway stores in temporary directories, because a check that reads the live store is a check whose result depends on last night. `writer-ownership` reads the shipped source, and nothing in the suite reaches the configured data root.

| Table | Insert | Update | Delete |
|---|---|---|---|
| `membership` | MembershipLoader | MembershipLoader | none |
| `bar` | Backfill, BarFetcher, CorporateActionChecker | none | BarFetcher, CorporateActionChecker |
| `indicator` | IndicatorEngine | IndicatorEngine | none |
| `swing` | SwingFinder | SwingFinder | none |
| `volume_profile` | VolumeProfileBuilder | VolumeProfileBuilder | none |
| `level` | LevelBuilder | LevelBuilder | none |
| `ladder` | LadderBuilder | LadderBuilder | none |
| `move` | MoveAnnotator | MoveAnnotator | none |
| `listing` | ShortlistBuilder | ShortlistBuilder | none |
| `forward_return` | ForwardReturnFiller | ForwardReturnFiller | none |
| `facts` | FactsAssembler | ChangeDetector | none |
| `fundamentals` | FundamentalsFetcher | none | none |
| `news_pulse` | NewsPulseCounter | none | NewsPulseCounter |
| `research_section` | ResearchRunner, ProseWriter | ClaimChecker | none |
| `theme_section` | ThemeResearchRunner | ClaimChecker | none |
| `source_document` | ResearchRunner, ThemeResearchRunner | none | none |
| `candidate_register` | CandidateRegistrar | none | none |
| `run_log` | every component appends | RunLog | none |

**`bar` has three inserters and two deleters, and that is the one exception this file argues for.** Backfill inserts a name's first year, once, on the run that finds it holding none. BarFetcher inserts the day's bars and drops the sessions that fall out of the retention window on the night they fall out of it. CorporateActionChecker deletes and reinserts a name's whole year when an action changes its adjusted prices.

**Two removals are sanctioned, and neither takes a bar out of a series it leaves standing.** Retention removes every session below a date boundary, for every name at once, and what remains is still a contiguous series ending tonight. A refetch removes one name's whole year and writes it back inside the same transaction, so the series is replaced rather than shortened. The hard rule that bars are append-only is about the third thing, a bar inside a stored series being deleted or edited while the rest stands, and that is what stays forbidden: no update to a bar by anything, and no delete by any component this table does not name. `bar-append-only` asserts it over the shipped source and over every migration, permitting a delete only in the file of a component declared here as a deleter of `bar`, and its negative proof plants one in a file that is not.

This was a three-way contradiction until 1.4 and not a two-way one. The `bar` note said the fetcher drops old sessions, this row gave Delete to the corporate action checker alone, and the paragraph above said twice that a refetch was the only sanctioned removal. Any two of the three could be read as agreeing, which is why it survived a review.

**`facts` is inserted by one component and updated by another, on disjoint columns.** FactsAssembler writes the facts file and its hash. ChangeDetector writes only the material-change list, on a row that already exists. A split is permitted where two components own disjoint declared column sets on the same grain, and the declared sets are below.

**`research_section` and `theme_section` are inserted by the writers and updated only by the checker.** A pending section is written by whichever model wrote it and is then accepted or rejected by ClaimChecker. Nothing else touches the status.

**`candidate_register` has no updater and no deleter, and that is load bearing.** Pre-registration only works if a registered candidate cannot be changed after results arrive. A retirement is a new dated row naming what it retires. `register-append-only` asserts the absence in both the source and a live attempt.

---

## Tables

### membership
Grain: one row per index, ticker and membership span.

| Column | Type | Notes |
|---|---|---|
| `index_code` | TEXT | the index this membership is in |
| `ticker` | TEXT | |
| `joined` | TEXT | date |
| `left` | TEXT | date, null while a member |
| `observed_at` | TEXT | UTC instant of the fetch that recorded this |

Primary key: `index_code`, `ticker`, `joined`.

Kept forever. Without the spans, a name added last month would appear in a sixty-evening window it was never part of.

### bar
Grain: one row per ticker per session.

| Column | Type | Notes |
|---|---|---|
| `ticker` | TEXT | |
| `session_date` | TEXT | date |
| `open`, `high`, `low`, `close` | TEXT | decimal in code, and one adjusted price set |
| `volume` | INTEGER | |
| `source` | TEXT | which endpoint delivered it |
| `observed_at` | TEXT | UTC instant |
| `raw_close` | TEXT | decimal in code, the provider's unadjusted close |

Primary key: `ticker`, `session_date`.

**The four prices are one set and it is the adjusted one** (see: The stored series is adjusted). The provider adjusts the close alone, so the other three are scaled by the same factor before they are stored. A bar holding three raw prices beside one adjusted price is a bar that could not have traded, and 1.2 stored 96 of 756 fixture bars whose close fell outside their own low and high.

**`raw_close` is the input to that factor, which is why it is kept.** The factor is the adjusted close over the raw one, and a store holding only the adjusted set cannot recompute or audit it after a later restatement moves it. The corporate action checker's refetch is what moves it, so the component that rewrites a year needs the input to the arithmetic and not only its output. It is written by whichever component writes the bar and is never read by the arithmetic that draws or computes: those read the adjusted set.

It sits last because migration 4 adds it to a table migration 3 created, and `bar-append-only` forbids a migration dropping a bar table to reorder its columns.

One year retained. The fetcher drops sessions older than the retention window on the night they fall out of it, and is declared above as a deleter of this table because it does.

### indicator
Grain: one row per ticker, session and indicator name.

| Column | Type | Notes |
|---|---|---|
| `ticker` | TEXT | |
| `session_date` | TEXT | |
| `name` | TEXT | `sma20`, `sma50`, `sma200`, `rsi14`, `macd`, `macd_signal`, `macd_hist`, `atr14`, `vol_avg20`, `vol_avg50` |
| `value` | REAL | null when not available |
| `bar_count` | INTEGER | how many bars the value was computed from; the reason a null is legible |

Primary key: `ticker`, `session_date`, `name`.

`bar_count` is a column rather than a note because an average of sixty bars labelled two hundred day is a lie, and the only way to see it later is to have stored what it was computed over.

### swing
Grain: one row per ticker, session and direction.

| Column | Type | Notes |
|---|---|---|
| `ticker` | TEXT | |
| `session_date` | TEXT | the session that is the peak or trough |
| `direction` | TEXT | `high` or `low` |
| `price` | TEXT | decimal |
| `confirmed_on` | TEXT | date the lookback completed |

Primary key: `ticker`, `session_date`, `direction`.

`confirmed_on` exists because a swing is not knowable on its own day. A component reading swings as of a date must not see one confirmed later.

### volume_profile
Grain: one row per ticker, as-of date and price band.

| Column | Type | Notes |
|---|---|---|
| `ticker` | TEXT | |
| `as_of` | TEXT | date |
| `band_low`, `band_high` | TEXT | decimal |
| `share_count` | INTEGER | shares traded in this band over the window |
| `share_of_period` | REAL | fraction of the window's total volume |

Primary key: `ticker`, `as_of`, `band_low`.

### level
Grain: one row per ticker, as-of date and band.

| Column | Type | Notes |
|---|---|---|
| `ticker` | TEXT | |
| `as_of` | TEXT | date |
| `low_edge`, `high_edge` | TEXT | decimal |
| `role` | TEXT | `support` or `resistance` |
| `immediate` | INTEGER | 1 for the nearest band on its side |
| `strength` | INTEGER | |
| `has_non_average_anchor` | INTEGER | 1 when a member is something other than a moving average |
| `members` | TEXT | JSON: each member's kind, price and date |

Primary key: `ticker`, `as_of`, `low_edge`.

`has_non_average_anchor` is a stored column rather than a derived one because the ladder builder reads it on every band and a short moving average follows the price, so a band anchored only on one sits at the price about half the time.

### ladder
Grain: one row per ticker per as-of date.

| Column | Type | Notes |
|---|---|---|
| `ticker` | TEXT | |
| `as_of` | TEXT | date |
| `trend_state` | TEXT | `uptrend`, `downtrend`, `range` |
| `plan` | TEXT | JSON: tranches, stops, invalidation, exits, earnings setups, arithmetic |

Primary key: `ticker`, `as_of`.

### move
Grain: one row per ticker and session selected as a large move.

| Column | Type | Notes |
|---|---|---|
| `ticker` | TEXT | |
| `session_date` | TEXT | |
| `change_pct` | REAL | |
| `rank` | INTEGER | position within the window by absolute size |

Primary key: `ticker`, `session_date`.

The cause of each move is not stored here. It is a researched claim and lives in `research_section` with its source.

### listing
Grain: one row per ticker per night, **for every index member and not only the listed ones**.

| Column | Type | Notes |
|---|---|---|
| `ticker` | TEXT | |
| `session_date` | TEXT | |
| `reasons` | TEXT | JSON: each of the six reasons with fired true or false and the values that made it so |
| `fired_count` | INTEGER | zero for most rows |
| `plan_at_listing` | TEXT | JSON: the entry zone, stop and first traded target as they stood that night |
| `shadow_reasons` | TEXT | JSON: registered candidates, evaluated the same way, shown nowhere |

Primary key: `ticker`, `session_date`.

**`plan_at_listing` is the column the improvement loop rests on.** Bars can be replayed and the plan cannot, because by the time a verdict is possible the rules may have changed and recomputing would score old listings under new ones. It is written by a component that is already running and it is the difference between the loop being a feature and being a wait.

About 125,000 rows a year at index size. Kept forever.

### forward_return
Grain: one row per listing per horizon.

| Column | Type | Notes |
|---|---|---|
| `ticker` | TEXT | |
| `session_date` | TEXT | the listing's date |
| `horizon` | TEXT | `5`, `21`, or `setup` |
| `outcome` | TEXT | `win`, `loss`, `unresolved`, or null while immature |
| `resolved_on` | TEXT | date, null while unresolved |
| `return_pct` | REAL | null for the `setup` horizon |
| `base_rate` | REAL | the universe figure for the same window and horizon |

Primary key: `ticker`, `session_date`, `horizon`.

The `setup` horizon is the one that matters: it records whether the plan's target was reached before its stop, within the time cap. `unresolved` is a value rather than a null, so it is counted in its own column and never in a rate.

### facts
Grain: one row per ticker per night.

| Column | Type | Written by |
|---|---|---|
| `ticker` | TEXT | FactsAssembler |
| `session_date` | TEXT | FactsAssembler |
| `payload` | TEXT | FactsAssembler. JSON: every number the computed sections may use, each with its source |
| `payload_hash` | TEXT | FactsAssembler |
| `material_changes` | TEXT | ChangeDetector. JSON list |

Primary key: `ticker`, `session_date`.

Declared column sets: FactsAssembler owns `payload` and `payload_hash`; ChangeDetector owns `material_changes`. The sets are disjoint and the grain is the same, which is what permits the split.

### fundamentals
Grain: one row per ticker per filing date.

| Column | Type | Notes |
|---|---|---|
| `ticker` | TEXT | |
| `filing_date` | TEXT | date the figures were reported |
| `fetched_at` | TEXT | UTC instant |
| `payload` | TEXT | JSON: quarters, balance sheet, segments, guidance, ratings |
| `source` | TEXT | which provider or filing each part came from |

Primary key: `ticker`, `filing_date`.

Kept forever, never updated. Providers restate, and keeping the filing date is what makes it possible to know later what was known at the time.

### news_pulse
Grain: one row per ticker per date.

| Column | Type | Notes |
|---|---|---|
| `ticker` | TEXT | |
| `session_date` | TEXT | |
| `article_count` | INTEGER | |

Primary key: `ticker`, `session_date`.

One year retained, which is enough to hold a ninety-day baseline. This table exists so the staleness judge can work without spending anything.

The counter drops rows older than the window on the night they fall out of it, and is declared above as this table's deleter. It was declared retained with no deleter at all until 1.4, which is the same defect as `bar`'s in a second table: a retention window nobody owns is a table that grows forever while this file says it does not.

### research_section
Grain: one row per ticker, section and version.

| Column | Type | Notes |
|---|---|---|
| `ticker` | TEXT | |
| `section` | TEXT | which of the report's sections this is |
| `version` | INTEGER | increments; earlier versions are kept |
| `as_of` | TEXT | date this section was written |
| `model` | TEXT | which model wrote it |
| `status` | TEXT | `pending`, `accepted`, `rejected`, `fallback` |
| `prose` | TEXT | |
| `source_ids` | TEXT | JSON list of `source_document` ids |
| `reject_reason` | TEXT | null unless rejected |

Primary key: `ticker`, `section`, `version`.

**A record is written and dated per section, not as a whole.** Some sections are drafted overnight by the local model and others written days later by the paid model, so a single as-of date and a single model name for a whole record would be false.

### theme_section
Grain: one row per theme, section and version. Same columns as `research_section` with `theme` in place of `ticker`, plus `industries` holding the industries that map to this theme.

### source_document
Grain: one row per fetched document.

| Column | Type | Notes |
|---|---|---|
| `id` | TEXT | |
| `url` | TEXT | |
| `title` | TEXT | |
| `published_on` | TEXT | date; a document with none is not stored |
| `fetched_at` | TEXT | UTC instant |
| `body` | TEXT | the full text, not a snippet |
| `admissibility` | TEXT | `accepted`, or the denied category that refused it |

Primary key: `id`.

A document that fails admissibility is not stored with a body. The row is kept with its refusal reason so a later reader can see what was rejected and why, which is the only way a refusal is visible at all.

### candidate_register
Grain: one row per registration event. Append only.

| Column | Type | Notes |
|---|---|---|
| `id` | INTEGER | |
| `candidate` | TEXT | the candidate condition's name |
| `rule` | TEXT | its stated rule |
| `test` | TEXT | the test it will be judged by |
| `event` | TEXT | `registered` or `retired` |
| `retires` | TEXT | for a retirement, the candidate it retires |
| `registered_at` | TEXT | UTC instant |
| `evidence` | TEXT | for a retirement, the figures that produced it |

Primary key: `id`.

No update, no delete. A correction is a new row.

### run_log
Grain: one row per run per stage.

| Column | Type | Notes |
|---|---|---|
| `run_id` | TEXT | |
| `stage` | TEXT | |
| `started_at`, `ended_at` | TEXT | UTC instants |
| `outcome` | TEXT | |
| `rows_written` | INTEGER | measured from the store, not self-reported |
| `model_calls` | INTEGER | |
| `network_requests` | INTEGER | |
| `spend` | TEXT | decimal |
| `detail` | TEXT | JSON |

Primary key: `run_id`, `stage`.

`rows_written` is measured rather than self-reported, because a stage's own count of what it wrote is the stage's opinion and the halt condition keys on that number.
