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
| `calendar` | CalendarFetcher | CalendarFetcher | CalendarFetcher |
| `indicator` | IndicatorEngine | IndicatorEngine | IndicatorEngine |
| `swing` | SwingFinder | SwingFinder | SwingFinder |
| `volume_profile` | VolumeProfileBuilder | VolumeProfileBuilder | VolumeProfileBuilder |
| `level` | LevelBuilder | LevelBuilder | LevelBuilder |
| `ladder` | LadderBuilder | LadderBuilder | LadderBuilder |
| `move` | MoveAnnotator | MoveAnnotator | MoveAnnotator |
| `listing` | ShortlistBuilder | ShortlistBuilder | none |
| `forward_return` | ForwardReturnFiller | ForwardReturnFiller | none |
| `facts` | FactsAssembler | ChangeDetector | FactsAssembler |
| `fundamentals` | FundamentalsFetcher | none | none |
| `news_pulse` | NewsPulseCounter | none | NewsPulseCounter |
| `research_section` | ResearchRunner, ProseWriter | ClaimChecker | none |
| `theme_section` | ThemeResearchRunner | ClaimChecker | none |
| `source_document` | ResearchRunner, ThemeResearchRunner | none | none |
| `candidate_register` | CandidateRegistrar | none | none |
| `rule_version` | RuleVersionScorer | RuleVersionScorer | none |
| `version_score` | RuleVersionScorer | RuleVersionScorer | RuleVersionScorer |
| `series_state` | CorporateActionChecker | CorporateActionChecker | none |
| `run_log` | every component that writes appends | RunLog | none |

**`bar` has three inserters and two deleters, and that is the one exception this file argues for.** Backfill inserts a name's first year, once, on the run that finds it holding none. BarFetcher inserts the day's bars and drops the sessions that fall out of the retention window on the night they fall out of it. CorporateActionChecker deletes and reinserts a name's whole year when an action changes its adjusted prices.

**Two removals are sanctioned, and neither takes a bar out of a series it leaves standing.** Retention removes every session below a date boundary, for every name at once, and what remains is still a contiguous series ending tonight. A refetch removes one name's whole year and writes it back inside the same transaction, so the series is replaced rather than shortened. The hard rule that bars are append-only is about the third thing, a bar inside a stored series being deleted or edited while the rest stands, and that is what stays forbidden: no update to a bar by anything, and no delete by any component this table does not name. `bar-append-only` asserts it over the shipped source and over every migration, permitting a delete only in the file of a component declared here as a deleter of `bar`, and its negative proof plants one in a file that is not.

This was a three-way contradiction until 1.4 and not a two-way one. The `bar` note said the fetcher drops old sessions, this row gave Delete to the corporate action checker alone, and the paragraph above said twice that a refetch was the only sanctioned removal. Any two of the three could be read as agreeing, which is why it survived a review.

**The six computed tables have no deleter, and 4.0 ruled that each will be deleted by its own writer** (see: Every computed table's writer is its own deleter). Section 16 states one year, recomputed nightly and kept for the harness, over indicators, swings, volume profile, levels, ladders and moves, and this table gives Delete to nobody for any of the six. It is the same defect as `bar`'s before 1.4 and `news_pulse`'s before 1.4, in a third place: a retention window nobody owns is a table that grows forever while the document says it does not.

The grain is what makes it urgent rather than tidy. `indicator` and `swing` key on a session, so both replace with the series and grow only as it does. `volume_profile`, `level` and `ladder` key on an as-of date, so each writes a new set every night and replaces nothing. At the band counts the fixture averages, five hundred names put something of the order of three and a half million rows a year into a store nothing can reduce.

**Five of the six were declared at 4.2 and `move` waited for 5.2**, because `MoveAnnotator` did not exist until then and a deleter declared before its component deletes is a declaration with nothing behind it, which `writer-ownership` refuses in that direction too. It refused exactly that when the rows were changed at 4.0 ahead of the code, and again at 5.0 when the sector column was declared ahead of its migration. All six are declared now.

Each writer drops the rows that fall out of the window on the night they fall out, which is what `BarFetcher` does for `bar` and `NewsPulseCounter` for `news_pulse`. The boundary is one year back from the newest stored session, read from the store so a component run on its own drops what a night would. A drop removes whole sessions or whole as-of sets below that date and never a row from inside a set that stands, which is the distinction the `bar` note draws. The writers keyed on an as-of date, `level` and `volume_profile`, also replace the name's set for the as-of they are writing whole, inside the transaction that writes it, because their rows are keyed on a band's edge and a second run for one night after a refetch moved the prices would otherwise leave both sets standing.

The `DELETE` lives in each component's own file rather than in a shared helper, because a write is attributed to the file it appears in: a helper holding the statement would be a file that deletes and is declared nowhere.

**`facts` is inserted by one component and updated by another, and no column is written by both in one operation.** FactsAssembler inserts the facts file and its hash. ChangeDetector writes the material-change list on a row that already exists, and empties `payload` on that same row under the retention. A split is permitted where two components own disjoint declared column sets per operation on the same grain, and the declared sets are below. The delete is the assembler's, and it removes one row only: tonight's file for a name, where it differs from the one the store now computes, so the insert writes the new one in its place (see: A re-run replaces a night's facts file where the store now computes a different one).

**`research_section` and `theme_section` are inserted by the writers and updated only by the checker.** A pending section is written by whichever model wrote it and is then accepted or rejected by ClaimChecker. Nothing else touches the status.

**`rule_version` has one updater and no deleter, and the update is the close.** A window is opened by an insert and closed by writing its `closed_at` and `replaced_by`, which is the one field a version row ever changes: a closed window keeps every other column it was opened with, because the scores written under it are of the rule as it stood then and a row edited afterwards would make them scores of something else. Nothing deletes a version, for the reason nothing deletes a registration.

**`version_score` has a deleter and it is the retention, not a correction.** The scorer drops the scores that fall out of the one-year window on the night they fall out of it, as every computed table's writer does (see: Every computed table's writer is its own deleter). A score inside the window is replaced rather than corrected: a re-run of a night writes that night's set again, inside the transaction that writes it, which is the update this table declares. It is the same shape the two as-of-keyed computed tables have, where a second run for one night after a refetch moved the prices would otherwise leave both sets standing.

**`candidate_register` has no updater and no deleter, and that is load bearing.** Pre-registration only works if a registered candidate cannot be changed after results arrive. A retirement is a new dated row naming what it retires. `register-append-only` asserts the absence in both the source and a live attempt.

---

## Tables

### membership
Grain: one row per index, ticker and membership span.

| Column | Type | Notes |
|---|---|---|
| `index_code` | TEXT | the index this membership is in |
| `ticker` | TEXT | |
| `joined` | TEXT | date, null when the provider carries none |
| `left` | TEXT | the date the name stops being a member, null until a leave is announced. The provider carries a leave before it takes effect, so a name with a date here is still a member on every session before it |
| `observed_at` | TEXT | UTC instant of the fetch that recorded this |
| `sector` | TEXT | the sector the provider last named for this ticker, null where it has named none. After `observed_at` because it was added by an `ALTER TABLE` at 5.1 and SQLite appends, and this file states the order the store has rather than the order that reads best |
| `industry` | TEXT | the industry the provider last named for this ticker, null where it has named none. Last because it was added by an `ALTER TABLE` at 6.9 |

Unique on `index_code`, `ticker` and `joined` with the unknown folded to a value, which is an expression index rather than a primary key.

Kept forever. Without the spans, a name added last month would appear in a sixty-evening window it was never part of.

**`joined` admits an unknown, and that was forced by the provider rather than chosen.** The live payload carries 822 spans and 145 have no start date, two of them current members: IR and WAB are in tonight's snapshot of 503 and the provider will not say since when. Dropping such a name takes a real member out of the index and out of everything computed from it, and writing a date nobody has is the guess this file refuses elsewhere. The unknown cannot sit in a primary key, because SQLite treats nulls as distinct and a second night would insert a second row rather than conflicting with the first, so the uniqueness moved to an index that folds it. That is the one place a sentinel belongs: inside the index that enforces uniqueness, never in the column a query reads.

**`sector` comes from the response the loader already fetches, at no extra request, and a departed name keeps the last one it was seen with.** Ruled at 5.0 and declared here at 5.1, which is the checkpoint that migrates it and writes it, because a column declared before the migration creates it is a declaration with nothing behind it and `schema-columns` refuses it in that direction. The constituents payload carries a `Components` object with a sector and an industry per current member, beside the `HistoricalTickerComponents` object the membership spans are read from. The two are read for different things and only the second is the index: a permanent test refuses the snapshot object as the membership, and that stands. The sector is written when a name is seen in `Components` and is never cleared, so a name that has left keeps the sector it carried when it was last observed, dated by the `observed_at` already on the row. A name that left before this column existed carries null, and null is drawn as not on file and excluded by name from every sector bucket rather than falling into one, because a filter that reads an absent value as a category is the defect this file has already paid for twice.

**`industry` comes from the same snapshot object as `sector`, at no extra request, and it is what a theme is.** Added at 6.9, which is the checkpoint that writes the first theme record. One theme pass serves every member the index names in one industry, so the industry has to be on file for every member rather than for the names someone has opened, and the snapshot already carries it beside the sector. It is coalesced as the sector is, so a departed name keeps the industry it was last seen with (see: A theme is the industry the index names for a member, and one theme pass serves every member it names).

**A row whose join date is unknown answers no to a past-date query and yes to members now.** A comparison against null is null, so such a name is absent from the set for any past date, which is the truthful answer: nothing here can say whether it was a member in June. Whether it is a member tonight is a question about tonight's session, answered by a join date on or before it or unknown and a leave date after it or absent, and a row with no join date is a constituent the provider lists today. `left IS NULL` was that answer until the phase 5 sign-off and is not one, because the provider carries a rebalance before it takes effect: a name with a leave date a week out is a member for the week, and a name with a join date a week out is not yet one (see: An announced index change takes effect on its effective date, and a joining name is stored from the announcement).

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

### calendar
Grain: one row per ticker, event date and kind.

| Column | Type | Notes |
|---|---|---|
| `ticker` | TEXT | |
| `event_date` | TEXT | date the event falls on |
| `kind` | TEXT | a provider event kind, `earnings` today |
| `timing` | TEXT | `before`, `after`, or `unstated`, which is when in the session the provider says it falls |
| `detail` | TEXT | JSON: what the provider carries about the event beyond its date |
| `observed_at` | TEXT | UTC instant of the fetch that recorded this |

Primary key: `ticker`, `event_date`, `kind`.

**This table holds what the provider files and nothing else** (see: A calendar event is fetched once for the whole index, and the calendar holds provider events only). `kind` carries provider event kinds only. A dated item a research pass found is a claim resting on a source document, so it lives in `research_section` and reaches the report's Dates section from there. Writing one here would put a claim where the claim checker cannot reach it and would give this table a second inserter.

**`timing` is what the provider actually files, and `status` was not.** 4.0 gave this table a `status` column carrying `confirmed` or `estimated`, on the reasoning that a booked print and an unconfirmed one are different things. They are, and the provider does not say which: its payload carries no such field. What it does carry is whether the report falls before the session, after it, or at a time it does not state, which decides whether the print lands on the date itself or on the session after it, and that is worth a column of its own for the same reason. Found at 4.3 by capturing the endpoint before writing the parser, which is why that rule exists: 1.2 stored a membership parser reading a field the provider does not send, and the fixture agreed with it for two checkpoints because the same session wrote both.

The failure table's explicit blank is a name with no row at all. That is legible without a status column: the row exists or it does not.

One writer for all three operations. The fetcher inserts tonight's events, updates a date the provider has moved, and drops rows for events that have fallen out of the window it fetches, which is the same shape `BarFetcher` and `NewsPulseCounter` carry for their own tables.

**The window is a quarter ahead and a year behind.** Ahead, because every name reports once a quarter, so ninety days holds every member's next print, and a window equal to the twenty-session horizon would mean a date arrives already inside it: the earnings-soon condition would fire on the day the provider published the date rather than on the name approaching it. Measured on the fixture's own capture, the two names with a print ahead report six and seven weeks out, which is outside a horizon-sized window and inside this one.

Behind, because the earnings rule states the last two prints' one-day moves and needs the dates those moves happened on. The endpoint answers with historical and upcoming events over whatever range it is asked for, so one request carries both. A year and no further: the moves are read off the bars, which are kept for a year, so a calendar reaching further back would name a print whose session the store does not hold.

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
Grain: one row per ticker per as-of date, **for every index member and not only the names carrying a plan**.

| Column | Type | Notes |
|---|---|---|
| `ticker` | TEXT | |
| `as_of` | TEXT | date |
| `trend_state` | TEXT | `uptrend`, `downtrend`, `range`, or `not_classified` |
| `plan` | TEXT | JSON: tranches, stops, invalidation, exits, event setups, arithmetic, and where there are none, the reason there are none |

Primary key: `ticker`, `as_of`.

**A row is written for every member every night** (see: A ladder row is written for every index member every night). A name in a downtrend, a name whose only support band is anchored on a moving average, and a name whose trend could not be classified all get a row whose `plan` states why it is empty. An absent row says nothing, and the trend-changed condition compares tonight's label against last night's, so a name with no row on the night its band went ineligible has no yesterday for the transition that changes the whole plan.

**`trend_state` carries a fourth value** (see: The trend state is read from the averages and the last two swings, and a name that cannot be classified says so). `not_classified` is a name with fewer than 200 bars, so no long average, or with fewer than two swings of the kind the rule reads. It is a stored value rather than a default to `range` for the reason `indicator.bar_count` exists: a label decides whether a plan exists, and a label over inputs nobody had is a figure over a population that was not measured. The reason sits in `plan` beside the reason a plan is empty, because both answer the same question a reader asks of an empty plan section.

### move
Grain: one row per ticker and session selected as a large move.

| Column | Type | Notes |
|---|---|---|
| `ticker` | TEXT | |
| `session_date` | TEXT | |
| `sessions` | INTEGER | how many sessions the move spans, 1 for a single day |
| `change_pct` | REAL | |
| `rank` | INTEGER | position within the window by absolute size |

Primary key: `ticker`, `session_date`.

**`sessions` is what makes the catalogue row true, and it was added at 5.0.** The annotator selects the largest single-day and multi-day moves of the stored year, and a table keyed on one session with no span could carry only the first of those. `session_date` is the session the move ended on, so a five-day run and a one-day gap on the same date are one row and the longer span wins, which is the reading that keeps the primary key.

The cause of each move is not stored here. It is a researched claim and lives in `research_section` with its source.

### listing
Grain: one row per ticker per night, **for every index member and not only the listed ones**.

| Column | Type | Notes |
|---|---|---|
| `ticker` | TEXT | |
| `session_date` | TEXT | |
| `reasons` | TEXT | JSON: each of the six reasons with fired true or false and the values that made it so. From the 5.4 correction earnings soon's values carry `next dated event` and breakout on volume's carry `previous close`, and a row without them was written before it (see: Sessions to a dated event are counted on the exchange calendar and never on stored bars) |
| `fired_count` | INTEGER | how many of the six reasons fired on the row, counted from `reasons` |
| `plan_at_listing` | TEXT | JSON: the entry zone, stop and first traded target as they stood that night |
| `shadow_reasons` | TEXT | JSON: `candidates`, each registered candidate the night evaluated with whether it fired and the values that made it so, and `skipped`, each registered candidate the night could not evaluate with the reason. Written for every member on every night exactly as `reasons` is, and drawn on no screen (see: Candidate conditions are registered before they are scored, and scored in shadow before they are shown) |

Primary key: `ticker`, `session_date`.

**`shadow_reasons` carries two lists and not one, from 8.4.** A candidate that did not fire and a candidate nothing evaluated are opposite statements: the first is a measurement and the second is a hole in one. Folding the second into the first is how a record of having skipped a name-night stops existing, and the correction later divides by a family whose members are assumed to have been scored throughout. So a candidate the night could not evaluate, because its evaluator's version has moved or because the night computed none of the values it reads, is written into `skipped` with its reason and is named as a failure on the listings stage's run log row rather than being absent.

**`plan_at_listing` is the column the improvement loop rests on.** Bars can be replayed and the plan cannot, because by the time a verdict is possible the rules may have changed and recomputing would score old listings under new ones. It is written by a component that is already running and it is the difference between the loop being a feature and being a wait.

About 125,000 rows a year at index size. Kept forever.

### forward_return
Grain: one row per listing per horizon.

| Column | Type | Notes |
|---|---|---|
| `ticker` | TEXT | |
| `session_date` | TEXT | the listing's date |
| `horizon` | TEXT | `5`, `21`, or `setup` |
| `outcome` | TEXT | `win`, `loss`, `unresolved`, `never entered`, or null while immature. `never entered` is the setup horizon's alone: the price never closed at or below the entry zone's top edge, so there was no purchase to score (see: A setup is scored from its entry, and a target reached before the entry is never a win) |
| `resolved_on` | TEXT | date, null while unresolved |
| `return_pct` | REAL | for the two session horizons, the move from the listing's close; for the `setup` horizon, the move from the close the setup was entered at, and null where nothing was entered or the entry and the stop fell on one session (see: A setup is scored from its entry, and a target reached before the entry is never a win) |
| `base_rate` | REAL | the universe figure for this horizon, over every name-night in the window rather than over the listed ones, and null for the `setup` horizon |
| `break_even` | REAL | the share of the time this plan had to be right to come out even, as a percentage, computed from the close the setup was entered at, its stop and its first traded target. The `setup` horizon's alone, and null on the two session horizons, which ask what the market did rather than what a plan demanded (see: A stored break-even is measured from the close the setup was entered at, as a percentage beside the figures it is compared with) |

Primary key: `ticker`, `session_date`, `horizon`.

**`break_even` and `return_pct` are present together on the `setup` horizon and absent together.** Both are measured from the close the setup was entered at, so a setup nobody entered has neither and a setup that entered and stopped on one session has neither, the store not saying where in the zone that fill sat. Five of the 93 setups the operator's store had resolved on 2026-09-16 were that shape, which is why the figures computed over these rows state the population they were computed over rather than counting every resolved row.

**The base rate's population is every name-night** (see: The base rate is over every name-night, and never over the listed ones). A `listing` row exists for every index member every night, and the figure is computed over all of them for the horizon it is stated at. It is null for `setup`, because target before stop is a question about a plan and a name with no plan has no answer to it (see: The `setup` horizon has no universe base rate, and the column is null for it). It repeats down the table by design: the row is what the run page reads, and a figure that has to be joined for is a figure that can be shown without it (see: `base_rate` is stored per row on purpose, and the reason is that the row is what gets read).

The `setup` horizon is the one that matters: it records whether the plan's target was reached before its stop, within the time cap. `unresolved` is a value rather than a null, so it is counted in its own column and never in a rate.

### facts
Grain: one row per ticker per night.

| Column | Type | Written by |
|---|---|---|
| `ticker` | TEXT | FactsAssembler |
| `session_date` | TEXT | FactsAssembler |
| `payload` | TEXT | FactsAssembler on insert, ChangeDetector on the retention update that empties it. JSON: every number the computed sections may use, each with its source |
| `payload_hash` | TEXT | FactsAssembler |
| `material_changes` | TEXT | ChangeDetector. JSON list, empty where there was nothing to compare against, and null where the comparison could not be made, being a facts file that is empty or names a fact twice |

Primary key: `ticker`, `session_date`.

Declared column sets, stated per operation because that is the grain the rule is written at: FactsAssembler inserts `payload` and `payload_hash`, and deletes the row it is about to insert again where the stored hash differs from tonight's; ChangeDetector updates `material_changes`, and updates `payload` to empty under the retention below. No column is written by two components in one operation, which is what permits the split. A replaced row starts with no change list, and the detector, which runs next, writes it again; a list stands on a row whose file is unchanged, and one the detector made is kept where the file it was made against has since been emptied by the retention.

The column sets were declared disjoint until the phase 5 sign-off, and the retention 5.4 added made that false in the sentence a reader is most likely to trust: `ChangeDetector` runs `UPDATE facts SET payload = ''`, so `payload` is the assembler's on insert and the detector's on update. The retention paragraph below described exactly that and the declaration thirteen lines above went on saying the sets do not overlap, which is one section holding two statements of one fact.

**Retention, settled at 5.0 and implemented at 5.3.** Section 16 states that a facts row is kept for every night a name was on the list or was opened, and that other nights keep the hash only. Nobody owned it, and the behaviour it describes is not a delete: the row stays and `payload` is emptied, so the hash still answers whether a later night's facts differ without holding the facts they differ from. That is an update, and `ChangeDetector` already owns Update on this table, so the retention is its work rather than a second updater. A table may have different owners for different operations and never two for one, and giving the assembler an update here would break that for a rule that fits the component which already reads last night's row against tonight's.

The retention is implemented at 5.4 rather than at 5.3, because the listings read it needs is a read of a store 5.4 creates, and a component declaring a read of a table nothing has created is a declaration with nothing behind it.

**"or was opened" is dropped, because nothing records an open.** No table in this file holds that a name was read, so half the stated rule could not be implemented and the cell would have promised a behaviour no test could induce. What stands is the half the store can answer: a night the name fired is kept whole, and every other night keeps the hash. That gives `ChangeDetector` a listings read, which its matrix row carries, and the ordering is already right, since the shortlist is step 12 of the night and the facts file is step 13.

The cost is what the rule is for. One row per name per night at index size is about 125,000 rows a year, and a payload holding every number the computed sections may use is the largest of them by an order of magnitude; keeping every payload forever is hundreds of megabytes a year against the twelve the listings cost.

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

**One fetch writes the twelve most recent filings the provider returns, which is a write window and not a retention one** (see: Twelve filings are stored and five are shown). Nothing deletes from this table and no row has a deleter, so a name's count grows past twelve as later filings arrive one at a time. The endpoint returns years of quarters in one call, so the window costs nothing beyond what is written, and it is counted in filings rather than over a date range so a company that missed a filing does not get a shorter window than one that did not. The numbers section shows five of them, which is a display decision.

`payload` holds the quarter's figures, the balance sheet, and the margin computed from that filing's own revenue and gross profit, and on the newest filing's row the company's identifier at the filings archive, from 6.8, which a research pass reads the company's own release by. The earnings bases, the valuation on each of them, and the next print's consensus estimate sit on the newest filing's row alone, because a ratio has a price in it and a price moves every session.

**Two providers fill one row and `source` says which filled what, part by part.** The company financials endpoint supplies eight parts and the filings archive three: `segments`, `guidance` and `facts`, which that endpoint files for no name at all. The archive's three sit on the newest filing's row alone for the reason the ratios do and one of its own: a segment table is read from one filing's report page and the guidance from one announcement's exhibit, so writing either onto a historical row would state that an older quarter's segments were this quarter's, and deriving them per filing would cost a request per row for figures nothing reads.

`source` distinguishes three reasons a part can be empty and they are not the same morning: the provider files it for nobody, the archive answered and served none, and the archive was not read. A column that could not tell the third from the first would report a company with no segments after a failed fetch. `segments` holds the report the figures were read from, the scale the table stated, its period columns and its groups in the order the table states them; `guidance` holds management's own passage with the exhibit and the date it was filed on, and never a figure struck from it (see: Guidance is stored as management's own prose and never parsed into a figure); `facts` holds the archive's own filed figures under the concept each was filed against, over the same twelve quarters the filings are stored over, for a named set of concepts rather than every concept the archive holds.

### news_pulse
Grain: one row per ticker per date.

| Column | Type | Notes |
|---|---|---|
| `ticker` | TEXT | |
| `session_date` | TEXT | |
| `article_count` | INTEGER | |

Primary key: `ticker`, `session_date`.

One year retained, which is enough to hold a ninety-day baseline. This table exists so the staleness judge can work without spending anything.

**A night run twice writes the same count rather than a second row or a changed one.** Update is none and the primary key is `ticker` and `session_date`, so a second run of the same night conflicts on every row it already wrote. The insert ignores the conflict rather than replacing the row, which keeps the table insert-only as the ownership row declares and keeps the night idempotent in what it records about the market. Replacing would be an update by another name and would need declaring as one; failing would make a night that ran twice an error rather than a repeat.

The counter drops rows older than the window on the night they fall out of it, and is declared above as this table's deleter. It was declared retained with no deleter at all until 1.4, which is the same defect as `bar`'s in a second table: a retention window nobody owns is a table that grows forever while this file says it does not.

### research_section
Grain: one row per ticker, section and version.

| Column | Type | Notes |
|---|---|---|
| `ticker` | TEXT | |
| `section` | TEXT | which of the report's sections this is, named as figure 12.2's lane table names it |
| `version` | INTEGER | increments; earlier versions are kept |
| `as_of` | TEXT | date this section was written |
| `model` | TEXT | which model wrote it |
| `status` | TEXT | `pending`, `accepted`, `rejected`, `fallback` |
| `prose` | TEXT | empty where the section had no admissible source to be written from |
| `source_ids` | TEXT | JSON list of `source_document` ids, in the order the prose cites them |
| `reject_reason` | TEXT | null unless rejected or fallback |

Primary key: `ticker`, `section`, `version`.

**A record is written and dated per section, not as a whole.** Some sections are drafted overnight by the local model and others written days later by the paid model, so a single as-of date and a single model name for a whole record would be false.

**The four statuses are two outcomes and two waypoints.** A writer inserts a version as `pending`, and only the checker moves it. `accepted` is a section a reader is shown. `rejected` is a first attempt the checker refused, which the writer may try once more as the next version. `fallback` is a section left out: a second consecutive refusal on the same as-of date, or a first check of a section whose source list holds no admitted document, which is sent there directly because rewriting cannot create a source. A version whose previous one is `fallback` is a fresh first attempt, so the retry is bounded per pass rather than per section for all time. The store refuses any other status, because a status nobody declared is a section no surface knows how to draw. Settled at 6.4, where the checker that moves them is built.

**The checker writes two columns and no more.** Its update names `status` and `reject_reason`, so the prose, the model, the date and the source list a writer inserted are the ones a reader is shown, and a checker that rewrote any of them would be a second writer of the section. `claim-admissibility` asserts the statement's column set.

### theme_section
Grain: one row per theme, section and version.

| Column | Type | Notes |
|---|---|---|
| `theme` | TEXT | |
| `section` | TEXT | which of the report's sections this is, named as figure 12.2's lane table names it |
| `version` | INTEGER | increments; earlier versions are kept |
| `as_of` | TEXT | date this section was written |
| `model` | TEXT | which model wrote it |
| `status` | TEXT | `pending`, `accepted`, `rejected`, `fallback` |
| `prose` | TEXT | empty where the section had no admissible source to be written from |
| `source_ids` | TEXT | JSON list of `source_document` ids, in the order the prose cites them |
| `reject_reason` | TEXT | null unless rejected or fallback |
| `industries` | TEXT | JSON list of the industries that map to this theme |

Primary key: `theme`, `section`, `version`.

Its columns were described until 6.4 as `research_section`'s with `theme` in place of `ticker` and `industries` added, and 6.4 wrote them out because a table in the store has to be comparable against this file column by column, and a description by difference cannot be compared against anything. The statuses mean what they mean on `research_section` and the checker writes the same two columns.

### source_document
Grain: one row per fetched document.

| Column | Type | Notes |
|---|---|---|
| `id` | TEXT | the document's own url, hashed, so a second fetch of the same address conflicts with the row it already has rather than writing a second one |
| `url` | TEXT | the document's own address, never a provider request url |
| `title` | TEXT | |
| `published_on` | TEXT | date, null where the document carries none. A row with none is a refusal carrying that reason |
| `fetched_at` | TEXT | UTC instant |
| `body` | TEXT | the full text, not a snippet, null on a refusal |
| `admissibility` | TEXT | `accepted`, or the denied category that refused it |

Primary key: `id`.

A document that fails admissibility is not stored with a body. The row is kept with its refusal reason so a later reader can see what was rejected and why, which is the only way a refusal is visible at all.

**Two columns admit null and neither is an absence of data.** `published_on` is null where the document carried no publish date, which is one of the things admissibility refuses a document for, so the row that records that refusal is a row with no date in it. `body` is null on every refusal, which is what keeping the refusal without storing the document means. Both were declared not null in this file's own notes until 6.3, and the two notes contradicted the paragraph above them: a document with no date was said not to be stored at all, while the refusal it fails is one the file requires be kept as a row. An admitted row carries both, which is a property of the test rather than of the column, and `claim-admissibility` asserts it as one in both directions.

**The url stored is the document's, and no request url reaches this table.** A provider request carries its key in the query string, so the hard rule that no request url reaches a log line or a store row holds here as it does everywhere: what is stored is the address the document is published at, which a reader can open. The intake refuses a url carrying a credential marker rather than storing it, because such a url could only have come from a fetcher handing over its own request, and that is a defect in the fetcher rather than a property of the document.

**The test is applied by whichever runner fetched the document, and never by the checker.** Ruled at 6.0. The catalogue said the claim checker refuses to store a document that fails the test while the matrix gave the checker a read of this table and no write, so the component that decided could not store the verdict and the component that stored was not the one deciding. The test runs on a document as it is fetched, which is where the runners are, so each runner applies it and writes the verdict into `admissibility`, and the checker reads that column to decide whether a claim resting on the document may be written. The ownership row above already said this and the architecture did not: Insert here is the two runners and nobody else, which is what forced the question.

### candidate_register
Grain: one row per registration event. Append only.

| Column | Type | Notes |
|---|---|---|
| `id` | INTEGER | |
| `candidate` | TEXT | the candidate condition's name |
| `rule` | TEXT | its stated rule |
| `test` | TEXT | the test it will be judged by |
| `evaluator` | TEXT | the name of an evaluator the Core code carries, refused at the write where it carries none |
| `parameters` | TEXT | JSON, the values that evaluator is run with |
| `evaluator_version` | TEXT | that evaluator's version as the code carried it when the row was written, which is a hash of the evaluator's own source |
| `event` | TEXT | `registered` or `retired`, constrained in the table |
| `retires` | TEXT | for a retirement, the candidate it retires |
| `registered_at` | TEXT | UTC instant |
| `evidence` | TEXT | for a retirement, the figures that produced it |

Primary key: `id`.

No update, no delete. A correction is a new row.

**The three evaluator columns are what make the row a registration rather than a description.** A candidate naming its rule in prose alone is a row a later session has to re-implement from words, and what it implements is then whatever it read the words to mean, which is the thing pre-registration exists to stop. `evaluator` names code that exists, `parameters` carries the values it is run with, and `evaluator_version` is a hash of that evaluator's source with line endings normalised to LF and any leading byte order mark removed, so the same evaluator hashes the same on both platforms and on a runner that checked the tree out with either ending. A changed evaluator is a new registration retiring the old one, never an edited row, and `register-append-only` fails a registered, unretired candidate whose evaluator's source has moved away from the version its row names.

### rule_version
Grain: one row per rule per version. Append only but for the close.

| Column | Type | Notes |
|---|---|---|
| `rule` | TEXT | one of the four ladder rules the build carries, refused at the write where it carries none |
| `version` | TEXT | the version's name, `live` for the rule the night itself applies |
| `parameters` | TEXT | JSON, the values this version is replayed with |
| `parameters_hash` | TEXT | a hash of those parameters with the code version, which is what the night compares the live rule against |
| `code_version` | TEXT | the build's version of the rule's own code, hashed into `parameters_hash` |
| `opened_at` | TEXT | UTC instant the window opened |
| `closed_at` | TEXT | UTC instant it closed, null while open |
| `replaced_by` | TEXT | for a closed window, the version that replaced it |

Primary key: `rule`, `version`, `opened_at`.

**The instant is in the key, so a version closed and opened again is two windows and not one.** Scores belong to a window rather than to a version name, and a key without the instant would merge two measurements of the same name taken either side of a change, which is the thing frozen windows exist to prevent.

### version_score
Grain: one row per name per night per rule per version.

| Column | Type | Notes |
|---|---|---|
| `ticker` | TEXT | |
| `session_date` | TEXT | the listing night scored |
| `rule` | TEXT | |
| `version` | TEXT | |
| `opened_at` | TEXT | the window the score belongs to, which is what makes it a score of a rule as it stood |
| `plan` | TEXT | JSON, the plan this version produced for that name-night |
| `sample` | TEXT | `scored` or `in_sample`; a score a backfill wrote for a night before its version opened is `in_sample` and counts toward no record and no verdict |

Primary key: `ticker`, `session_date`, `rule`, `version`, `opened_at`.

**`sample` is the column that keeps a backfill from becoming evidence.** A version added later may be scored over the nights before it, because seeing what it would have done is the point of scoring counterfactually at all. What it may not do is count: a rule written after those nights were seen and then scored on them is measured in sample, and a record holding such a score is a record of having fitted the rule to what already happened. The scorer writes the flag from the window's own `opened_at` against the night being scored, so the classification is arithmetic rather than a caller's claim about itself.

One year retained, dropped by the scorer on the night the rows fall out of the window, at the order of the index times the versions open.

### series_state
Grain: one row per ticker.

| Column | Type | Notes |
|---|---|---|
| `ticker` | TEXT | |
| `state` | TEXT | `ok` or `suspect` |
| `reason` | TEXT | why, in words, when the state is not ok |
| `checked_at` | TEXT | UTC instant of the check that set this, which times a spent name's weekly retry from its session |
| `retries` | INTEGER | how many nights after the one that marked it the name has been asked for again and failed, 0 when the state is ok; last because SQLite appends |

Primary key: `ticker`.

**Contradiction C, resolved at 1.6 with a table rather than a column.** The failure table says that when the corporate action check itself fails the name is marked suspect, and nothing held that. It could not be a column on `bar`, whose grain is a session, and it is not what `membership` records: whether a name's stored series can be trusted is not a fact about whether the name is in the index. So it is a table of its own, at the grain the statement is actually about, which is the name.

One row per ticker rather than one per check, because the question asked of it is whether this name's series can be trusted now. When it happened is in the run log, which is the record of what each night did, and a second history here would be the same fact in two places.

No deleter. A name that becomes trustworthy again is set back to `ok` by the check that established it, which is an update on the row that already exists.

**`retries` is a count and not a history, added at the 6.0 ruling.** The check reads it to decide whether tonight asks for a suspect name again, which is a question about now, and each attempt's night is still the run log's. A night an action lands on the name sets it back to 0 however the refetch goes, because the action is a new reason to ask, and a name at the limit is asked for again on the first night whose session is 7 or more days after the session of its `checked_at`, with the count going on past the limit, until a refetch succeeds (see: A suspect name is asked for again on the five nights after it is marked and weekly after that, and its own page, its row on tonight's list and the run page say so until a refetch succeeds). The read API reads the row as stored from the 7.0 ruling, for the line the name page opens with and the one tonight's list draws beside the name. It is not null with a default of 0, where `bar.raw_close` is nullable, because here the default holds: nothing counted a row that exists when the column is added, and the rule counts from the night it lands.

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

**A paid call is a row of its own, and the rows are the ledger.** The spend cap writes one row for every call it judges, under the pass's run, with the stage `research call:` followed by the section asked for, and from 6.8 the round where a pass asks for a section again inside the same run, as `research call: The two cases, round 2`, so one section's call in one round of one pass is one row. `ok` carries what the call cost in `spend`, one model call and one network request; `paused` is a call a cap refused before it was made, counting nothing and spending nothing; `unavailable` is a call the provider did not answer, counting the attempt and spending nothing; `refused` is a call the provider declined, which spends nothing, or answered with nothing a section could store, which the provider counted and billed and which carries that price in `spend`. `spend` is TEXT holding a decimal, written invariant and never rounded, and what the cap judges a call by is these rows summed by the UTC day and the UTC month each started in, so the ledger is measured off this table rather than kept by anything that spends (see: Every paid call is made through the spend cap, which holds the research model).

**The overnight queue writes one row under the night's run, and each name it gives a pass is a run of its own.** From 6.10 the queue's row carries the stage `overnight queue`: `ok` where it ran through every name it queued, `limit` where it started no pass once its hours had passed and left names for the next night, and `unavailable` where the local model did not answer, which stops it at that name. `model_calls` is every call its passes made, the one that found the local model not answering included, `network_requests` and `rows_written` are zero, since it fetches nothing and writes no research itself, and `spend` is zero. `detail` names the night, the names listed and queued, every pass it ran with the run that pass was written under, the pass it stopped at, the names it left, and whether the machine was held awake. Each pass's staleness, prose and claims rows are under that pass's own run, so a night's model calls are read off the queue's row and the runs it names (see: The night's zero-model-call rule bounds the arithmetic, and the overnight queue is carved out of it by name).
