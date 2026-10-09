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
| `kept_bar` | BarFetcher | none | none |
| `market_bar` | MarketSeriesFetcher | MarketSeriesFetcher | none |
| `calendar` | CalendarFetcher | CalendarFetcher | CalendarFetcher |
| `pulled_bar` | HistoryPull | none | HistoryPull |
| `pulled_earnings` | HistoryPull | none | HistoryPull |
| `pulled_surprise` | HistoryPull | none | HistoryPull |
| `pulled_market_bar` | HistoryPull | none | HistoryPull |
| `pulled_company` | HistoryPull | none | HistoryPull |
| `pulled_shares` | HistoryPull | none | HistoryPull |
| `pulled_split` | HistoryPull | none | HistoryPull |
| `pulled_revenue` | HistoryPull | none | HistoryPull |
| `pulled_member` | HistoryPull | none | HistoryPull |
| `pulled_income` | HistoryPull | none | HistoryPull |
| `pulled_snapshot` | HistoryPull | none | HistoryPull |
| `pulled_holding` | HistoryPull | HistoryPull | HistoryPull |
| `indicator` | IndicatorEngine | IndicatorEngine | IndicatorEngine |
| `swing` | SwingFinder | SwingFinder | SwingFinder |
| `volume_profile` | VolumeProfileBuilder | VolumeProfileBuilder | VolumeProfileBuilder |
| `level` | LevelBuilder | LevelBuilder | LevelBuilder |
| `ladder` | LadderBuilder | LadderBuilder | LadderBuilder |
| `move` | MoveAnnotator | MoveAnnotator | MoveAnnotator |
| `peer_reading` | MoveAnnotator | MoveAnnotator | MoveAnnotator |
| `earnings_reaction` | MoveAnnotator | MoveAnnotator | MoveAnnotator |
| `swing_reading` | SwingReader | none | SwingReader |
| `market_reading` | SwingReader | SwingReader | SwingReader |
| `gate_result` | SwingFilter, FilterHistory | none | SwingFilter, FilterHistory |
| `filter_version` | ShapeCommand | ShapeCommand | none |
| `shape_proposal` | ShapeProposer | ShapeCommand | none |
| `listing` | ShortlistBuilder | ShortlistBuilder | none |
| `list_rule` | NightClose | NightClose | none |
| `family_result` | FamilyEvaluator | none | FamilyEvaluator |
| `family_night` | FamilyLister | none | FamilyLister |
| `family_pick` | FamilyLister | none | FamilyLister |
| `family_trade` | FamilyRecorder | FamilyRecorder | FamilyRecorder |
| `heavyweight_night` | HeavyweightBook | none | HeavyweightBook |
| `heavyweight_holding` | HeavyweightBook | HeavyweightBook | HeavyweightBook |
| `heavyweight_rule_night` | HeavyweightBook | none | HeavyweightBook |
| `heavyweight_rule_holding` | HeavyweightBook | HeavyweightBook | HeavyweightBook |
| `index_family_night` | IndexFamilies | none | IndexFamilies |
| `index_family_result` | IndexFamilies | none | IndexFamilies |
| `index_family_pick` | IndexFamilies | none | IndexFamilies |
| `index_family_trade` | IndexFamilies | IndexFamilies | IndexFamilies |
| `index_heavyweight_holding` | IndexFamilies | IndexFamilies | IndexFamilies |
| `index_rule_trade` | IndexFamilies | IndexFamilies | IndexFamilies |
| `index_heavyweight_rule_night` | IndexFamilies | none | IndexFamilies |
| `index_heavyweight_rule_holding` | IndexFamilies | IndexFamilies | IndexFamilies |
| `decision_card` | DecisionCards | none | DecisionCards |
| `rule_record` | RuleRecorder | RuleRecorder | none |
| `rule_night` | RuleCards | none | RuleCards |
| `rule_pick` | RuleCards | RuleCards | RuleCards |
| `forming_row` | RuleCards | none | RuleCards |
| `setup` | SetupLedger | SetupLedger | SetupLedger |
| `setup_night` | SetupLedger | none | SetupLedger |
| `ledger_summary` | SetupLedger | none | SetupLedger |
| `loop_run` | WalkForwardTester | none | none |
| `loop_proposal` | WalkForwardTester | none | none |
| `loop_test` | WalkForwardTester | none | none |
| `loop_finding` | WalkForwardTester | none | none |
| `filed_fact` | FilingsRefresher | none | none |
| `filed_fact_pull` | FilingsRefresher | none | none |
| `filing_day` | FilingsRefresher | none | none |
| `taken_trade` | ReadApi | ReadApi, TakenFollower | ReadApi |
| `taken_record` | TakenFollower | none | TakenFollower |
| `dividend_reading` | QuarterFetcher | none | none |
| `sweep_answer` | SweepAnswers | SweepAnswers | none |
| `forward_return` | ForwardReturnFiller | ForwardReturnFiller | none |
| `facts` | FactsAssembler | ChangeDetector | FactsAssembler |
| `fundamentals` | FundamentalsFetcher | none | none |
| `fundamentals_snapshot` | FundamentalsFetcher | none | none |
| `reported_quarter` | QuarterFetcher | none | none |
| `quarter_ask` | QuarterFetcher | none | none |
| `company` | QuarterFetcher | none | none |
| `fundamental_reading` | FundamentalReader | none | FundamentalReader |
| `member_reading` | MemberReader | none | MemberReader |
| `switch_reading` | MemberReader | none | MemberReader |
| `estimate_reading` | EstimatesFetcher | none | none |
| `news_pulse` | NewsPulseCounter | none | NewsPulseCounter |
| `news_article` | NewsPulseCounter | none | NewsPulseCounter |
| `news_label` | NewsLabeller | none | NewsLabeller |
| `research_section` | ResearchRunner, ProseWriter | ClaimChecker | none |
| `theme_section` | ThemeResearchRunner | ClaimChecker | none |
| `source_document` | ResearchRunner, ThemeResearchRunner | none | none |
| `candidate_register` | CandidateRegistrar | none | none |
| `rule_version` | RuleVersionScorer | RuleVersionScorer | none |
| `version_score` | RuleVersionScorer | RuleVersionScorer | RuleVersionScorer |
| `version_block` | RuleVersionScorer | none | none |
| `series_state` | CorporateActionChecker | CorporateActionChecker | none |
| `research_request` | ReadApi, RequestDrain | ReadApi, RequestDrain | none |
| `watch_list` | ReadApi | none | ReadApi |
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

**`research_request`, `watch_list` and `taken_trade` are the three tables the read surface writes, and the split on the first is by operation.** ReadApi does two things: it inserts a request when a press asks for one, from tonight's list or from a name's page, and it updates a request nobody has claimed to `withdrawn` when a press on the queue screen takes it out. RequestDrain belongs to the worker and moves the same row through `writing` and then `written` or `refused`, puts a row a drain left `writing` when it ended back to `outstanding` (see: A request a drain left being written is put back as outstanding by the next drain, and a pass that fails settles its request as refused), and after the night's overnight queue it inserts the night's own requests, six taken in turn from the three indices' pages: the insert is split between the two by what asks, a press on a screen or the night (see: The six reports a night are taken in turn across the three indices, one at a time in the page's order). No column is written by both in one operation and the declared sets are below, which is the permission `facts` is already declared under. The read surface still writes nothing a pass writes: a request is an ask, and the research it leads to is the worker's (see: A press writes a request and starts the worker's drain as a process of its own, and every pass waits for the off-peak hours). The watch list is the operator's own: ReadApi inserts a name on one press and deletes it on another, and nothing but the pages reads it (see: The watch list is the operator's own, up to twenty names of the index, on a page of its own). The taken trades are the operator's own too: ReadApi inserts one on a card's Taken press, deletes one on its Not taken press before a night has followed it, and records an exit on a third, and no component that picks a stock, orders a list or keeps a record reads them.

**`research_section` and `theme_section` are inserted by the writers and updated only by the checker.** A pending section is written by whichever model wrote it and is then accepted or rejected by ClaimChecker. Nothing else touches the status.

**`rule_version` has one updater and no deleter, and the update is the close.** A window is opened by an insert and closed by writing its `closed_at`, `evidence` and, for a replacement, `replaced_by`, which is the one change a version row ever takes: a closed window keeps every other column it was opened with, because the scores written under it are of the rule as it stood then and a row edited afterwards would make them scores of something else. Nothing deletes a version, for the reason nothing deletes a registration.

**`version_score` has a deleter and it is the retention, not a correction.** The scorer drops the scores that fall out of the one-year window on the night they fall out of it, as every computed table's writer does (see: Every computed table's writer is its own deleter). A score inside the window is replaced rather than corrected: a re-run of a night writes that night's set again, inside the transaction that writes it, which is the update this table declares. A backfill writes only the scores not stored yet and keeps the rest (see: A backfill scores only a night the store computed, at that night's own price scale, and never rewrites a score already stored). It is the same shape the two as-of-keyed computed tables have, where a second run for one night after a refetch moved the prices would otherwise leave both sets standing.

**`version_block` has no updater and no deleter, and the absence is the whole point of the table.** The retention does not reach it and nothing else removes a row from it. A block's two excesses and two counts are computed on the night the block completes and written once; a block already held is not recomputed, and the write that reaches one anyway keeps the row it conflicts with rather than replacing it. A sum that moved after the look that read it would make that look's boundary one found over an arrangement the record no longer has, which is the same reason a look reads whole blocks rather than the closed setups inside an unfinished one (see: A version's record is read from the blocks frozen as each completed). A window holds at most 16 blocks, the last look never being extended, so the windows open at once hold at most 288 rows between them. A closed window keeps its blocks, and the runbook's remedy for a moved pin closes every open window and opens its replacement, so the table holds at most 16 rows for each window ever opened and has no bound over the windows open at once.

**`candidate_register` has no updater and no deleter, and that is load bearing.** Pre-registration only works if a registered candidate cannot be changed after results arrive. A retirement is a new dated row naming what it retires, and a name retired may be registered again, standing once by its last row (see: A candidate stands by the last row naming it, and a name retired and registered again stands once). `register-append-only` asserts the absence in both the source and a live attempt, a replace included.

---

## Tables

### membership
Grain: one row per index, ticker and membership span.

| Column | Type | Notes |
|---|---|---|
| `index_code` | TEXT | the index this membership is in: `GSPC`, the S&P 500, whose spans the provider answers, and from 15.1 `MID` and `SML`, the S&P 400 and 600, read from their funds' holdings files |
| `ticker` | TEXT | |
| `joined` | TEXT | date, null when the provider carries none; a 400 or 600 span's is the session of the night its fund first listed the name |
| `left` | TEXT | the date the name stops being a member, null until a leave is announced or the feed stops listing the span. The provider carries a leave before it takes effect, so a name with a date here is still a member on every session before it. A span the feed no longer lists carries the session of the night that found it unlisted |
| `observed_at` | TEXT | UTC instant of the fetch that recorded this |
| `sector` | TEXT | the sector the provider last named for this ticker, null where it has named none, and null on every 400 and 600 row, whose fund's file names GICS's sectors where the provider names its own. After `observed_at` because it was added by an `ALTER TABLE` at 5.1 and SQLite appends, and this file states the order the store has rather than the order that reads best |
| `industry` | TEXT | the industry the provider last named for this ticker, null where it has named none. After `sector` because it was added by an `ALTER TABLE` at 6.9 |
| `name` | TEXT | the company's name as the provider's span for this ticker states it, null where the span states none. Last because it was added by an `ALTER TABLE` at the 5.1 correction of 2026-09-18 |

Unique on `index_code`, `ticker` and `joined` with the unknown folded to a value, which is an expression index rather than a primary key.

Kept forever. Without the spans, a name added last month would appear in a sixty-evening window it was never part of.

**A span the feed no longer lists leaves on the session of the night that finds it unlisted, and a screen draws one span a ticker.** The upsert is keyed on the join date, so a span whose start the provider corrects is written as a second row, and the night that writes it closes the first on its session, as it closes the span of a ticker the provider renamed and lists no longer. A span the feed lists again is reopened by the upsert, which writes the feed's leave date over the one the night wrote. A night whose feed stops listing more tickers than the loader closes in one night closes none (see: A ticker the index feed stops listing leaves the index on the night it goes unlisted). The sessions before a re-dated span was closed are covered by both of its rows, so the read surface draws each ticker once, from its current span with the newest `observed_at`, and the nightly stages read every current span and write one row a ticker.

**The S&P 400's and 600's rows are read from their funds' holdings files, which carry today's holdings and no dates** (see: The S&P 400's and 600's members are read each night from their funds' own holdings files). The loader writes them in the same run and under the same instant as the S&P 500's, one request a fund: a stock a file lists for the first time opens a span on the night's session, a stock it lists again keeps the span it opened on, and a span it stops listing closes on the night's session, unless more than 8 of the 400's or 12 of the 600's went unlisted at once, which closes none. A name the S&P 500 holds on the session is not written under a fund's index, since the three indices hold no company in common, so its span there closes as if the file had dropped it. A file that cannot be read changes no row of its index. Each row carries the file's name for the company and no sector (see: The universe is the S&P 1500's three indices with each member tagged by its index, and membership is fetched).

**`joined` admits an unknown, and that was forced by the provider rather than chosen.** The live payload carries 822 spans and 145 have no start date, two of them current members: IR and WAB are in tonight's snapshot of 503 and the provider will not say since when. Dropping such a name takes a real member out of the index and out of everything computed from it, and writing a date nobody has is the guess this file refuses elsewhere. The unknown cannot sit in a primary key, because SQLite treats nulls as distinct and a second night would insert a second row rather than conflicting with the first, so the uniqueness moved to an index that folds it. That is the one place a sentinel belongs: inside the index that enforces uniqueness, never in the column a query reads.

**`sector` comes from the response the loader already fetches, at no extra request, and a departed name keeps the last one it was seen with.** Ruled at 5.0 and declared here at 5.1, which is the checkpoint that migrates it and writes it, because a column declared before the migration creates it is a declaration with nothing behind it and `schema-columns` refuses it in that direction. The constituents payload carries a `Components` object with a sector and an industry per current member, beside the `HistoricalTickerComponents` object the membership spans are read from. The two are read for different things and only the second is the index: a permanent test refuses the snapshot object as the membership, and that stands. The sector is written when a name is seen in `Components` and is never cleared, so a name that has left keeps the sector it carried when it was last observed, dated by the `observed_at` already on the row. A name that left before this column existed carries null, and null is drawn as not on file and excluded by name from every sector bucket rather than falling into one, because a filter that reads an absent value as a category is the defect this file has already paid for twice.

**`industry` comes from the same snapshot object as `sector`, at no extra request, and it is what a theme is.** Added at 6.9, which is the checkpoint that writes the first theme record. One theme pass serves every member the index names in one industry, so the industry has to be on file for every member rather than for the names someone has opened, and the snapshot already carries it beside the sector. It is coalesced as the sector is, so a departed name keeps the industry it was last seen with (see: A theme is the industry the index names for a member, and one theme pass serves every member it names).

**`name` comes from the span the loader already reads, at no extra request, and is coalesced as the sector is.** The spans carry a name for every ticker they list, current and departed, where the snapshot object carries current members alone, so the name is read from the span and a departed name has one. A night whose span states none keeps the name the row holds. The name page states it beside the ticker, and a research pass reads it to tell an article about the company from one that only mentions it (see: The membership row carries the company's name the index feed states).

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

**`raw_close` is the input to that factor, which is why it is kept.** The factor is the adjusted close over the raw one, and a store holding only the adjusted set cannot recompute or audit it after a later restatement moves it. The corporate action checker's refetch is what moves it, so the component that rewrites a year needs the input to the arithmetic and not only its output. It is written by whichever component writes the bar. The arithmetic that draws or computes reads the adjusted set, and one reader reads this column: the forward return filler, for the listing session's factor a stored plan is scaled by, because a plan keeps the scale the series had on its night and a later restatement moves the series and not the plan (see: An outcome once decided is never rewritten, and a setup still in play is scored with its plan scaled by its listing session's adjustment factor).

It sits last because migration 4 adds it to a table migration 3 created, and `bar-append-only` forbids a migration dropping a bar table to reorder its columns.

One year retained. The fetcher drops sessions older than the retention window on the night they fall out of it, and is declared above as a deleter of this table because it does.

### kept_bar
Grain: one row per ticker and session the fetcher dropped as it fell out of the year it keeps.

| Column | Type | Notes |
|---|---|---|
| `ticker` | TEXT | |
| `session_date` | TEXT | date |
| `open`, `high`, `low`, `close` | TEXT | decimal in code, the bar's adjusted set as it stood when dropped |
| `volume` | INTEGER | |
| `source` | TEXT | which endpoint delivered the bar |
| `observed_at` | TEXT | UTC instant the bar was stored |
| `raw_close` | TEXT | decimal in code, the provider's unadjusted close, null where the bar carried none |
| `kept_on` | TEXT | the night whose drop kept it |

Primary key: `ticker`, `session_date`.

**The fetcher writes it in the transaction that drops the bars, and nothing updates or deletes it.** A session already kept is left as it was. No night reads it: it is a setup's path, replayed from the anchor the setup stores (see: The bars the fetcher drops are kept in a table of their own that no night reads, and a setup is stored as its anchor).

### market_bar
Grain: one row per series per session a night stored.

| Column | Type | Notes |
|---|---|---|
| `series` | TEXT | `GSPC`, the index itself, `VIX`, one of the eleven sector funds, `XLB` to `XLY`, or from 15.1 one of the index and credit funds, `SPY`, `IJH`, `IJR` and `HYG` |
| `session_date` | TEXT | date |
| `open`, `high`, `low`, `close` | TEXT | decimal in code, as the provider sent them on the night that wrote the row |
| `run_id` | TEXT | the run id of the night that wrote the row |

Primary key: `series`, `session_date`.

**The index's, the VIX's and the sector funds' daily series as the night fetches them, read by the family evaluator and the heavyweight book** (see: The night asks for the market series' daily closes once a series, and keeps them apart from the members' bars). The fetch step asks the provider once a series over the 400 days before the night's session, the index and the VIX under its index exchange and each fund as a listing, and inserts each session no night has stored. A session held keeps its first row, but a fund's: the provider adjusts a fund's closes for each dividend it pays, so the fetcher writes a fund's held session again from an answer stating it at another close, the one update this table takes, and a fund's return over a year is read over closes on one basis. The table is kept whole, seventeen rows a session from 15.1, when the index and credit funds joined, each a fund whose held session is written again the same way. A series the provider refuses or sends nothing for stores nothing that night and stops nothing. The family evaluator reads the index's and the VIX's closes on the store's own sessions to the night for the registered rules whose market switch reads them, and the heavyweight book reads the index's closes for each stock's beta and each fund's for its sector's return; nothing else reads the table: it is apart from `bar` because no series is a member's, and apart from `pulled_market_bar` because a night reads it.

### calendar
Grain: one row per ticker, event date and kind.

| Column | Type | Notes |
|---|---|---|
| `ticker` | TEXT | |
| `event_date` | TEXT | date the event falls on |
| `kind` | TEXT | a provider event kind, `earnings` or, from 16.3, `ex-dividend` for a date the dividend calendar declares, its `timing` `unstated` and its `detail` empty |
| `timing` | TEXT | `before`, `after`, or `unstated`, which is when in the session the provider says it falls |
| `detail` | TEXT | JSON: what the provider carries about the event beyond its date, being the period it covers and the estimate, the actual and the surprise as the provider sent them |
| `observed_at` | TEXT | UTC instant of the fetch that recorded this |

Primary key: `ticker`, `event_date`, `kind`.

**This table holds what the provider files and nothing else** (see: A calendar event is fetched once for the whole index, and the calendar holds provider events only). `kind` carries provider event kinds only. A dated item a research pass found is a claim resting on a source document, so it lives in `research_section` and reaches the report's Dates section from there. Writing one here would put a claim where the claim checker cannot reach it and would give this table a second inserter.

**`timing` is what the provider actually files, and `status` was not.** 4.0 gave this table a `status` column carrying `confirmed` or `estimated`, on the reasoning that a booked print and an unconfirmed one are different things. They are, and the provider does not say which: its payload carries no such field. What it does carry is whether the report falls before the session, after it, or at a time it does not state, which decides whether the print lands on the date itself or on the session after it, and that is worth a column of its own for the same reason. Found at 4.3 by capturing the endpoint before writing the parser, which is why that rule exists: 1.2 stored a membership parser reading a field the provider does not send, and the fixture agreed with it for two checkpoints because the same session wrote both.

The failure table's explicit blank is a name with no row at all. That is legible without a status column: the row exists or it does not.

One writer for all three operations. The fetcher inserts tonight's events and updates the ones it holds, removes a row inside the window that tonight's answer no longer carries, and drops rows for events that have fallen out of the window it fetches, which is the same shape `BarFetcher` and `NewsPulseCounter` carry for their own tables. A date the provider has moved is a new row, because the date is part of the key, and its old one goes as a row the answer no longer carries; an answer that stores no member's print removes nothing, since a provider answering nothing for the index is not one saying nothing is scheduled. The rows are the index's own listing's: the endpoint answers for every market in one payload, a ticker the index holds can be listed elsewhere as another company or as the member's own shares on dates of their own, and only the index's exchange is read.

**The window is a quarter ahead and a year behind.** Ahead, because every name reports once a quarter, so ninety days holds every member's next print, and a window equal to the twenty-session horizon would mean a date arrives already inside it: the earnings-soon condition would fire on the day the provider published the date rather than on the name approaching it. Measured on the fixture's own capture, the two names with a print ahead report six and seven weeks out, which is outside a horizon-sized window and inside this one.

Behind, because the earnings rule states the last two prints' one-day moves and needs the dates those moves happened on. The endpoint answers with historical and upcoming events over whatever range it is asked for, so one request carries both. A year and no further: the moves are read off the bars, which are kept for a year, so a calendar reaching further back would name a print whose session the store does not hold.

### pulled_bar
Grain: one row per ticker per session a pull reached.

| Column | Type | Notes |
|---|---|---|
| `ticker` | TEXT | |
| `session_date` | TEXT | date |
| `open`, `high`, `low`, `close` | TEXT | decimal in code, and one adjusted price set, as the provider sent it on the pull's day |
| `raw_close` | TEXT | decimal in code, the provider's unadjusted close |
| `volume` | INTEGER | |
| `pull` | TEXT | the run id of the pull that wrote the row |

Primary key: `ticker`, `session_date`.

**This is not the bar table, and no night reads it** (see: The history pulled before the store's year sits apart from its bars, marked by the pull that wrote it, read by no night and removed whole by that pull). The operator's history pull asks every name the index held on any session from a date to tonight for its daily bars over that whole span, in the adjusted form `bar` holds, and stores them here. It reaches tonight rather than stopping where `bar` begins, because the fetcher drops `bar`'s oldest session every night, so a pull that stopped there would leave a hole between the two within a week; a reader holding both reads `bar` where `bar` holds the session.

**`pull` is what removes a pull whole.** The pull's purge deletes every row one pull wrote and nothing else. No stored bar may be removed that way, and these rows may because no night, listing, score or page ever read one. A second pull inserts only the sessions no earlier pull holds, so each row belongs to exactly one pull.

**A session missing from one name's pulled series is kept as a hole rather than refused.** The pull names it on its run log row, read against the days at least half the pulled names whose series span each day hold, and the reader of the series decides what a hole stops, because nothing is computed from this table on its own. A day fewer than half of them hold is named apart on the same row and is no name's hole: the exchange's closure table covers the store's own years alone, so what places an older session is the names that traded it, and a bar the provider sent for one name on a day the exchange was closed is stored as sent.

### pulled_earnings
Grain: one row per ticker per earnings report date a pull reached.

| Column | Type | Notes |
|---|---|---|
| `ticker` | TEXT | |
| `event_date` | TEXT | the report date the provider files |
| `timing` | TEXT | `before`, `after`, or `unstated`, as `calendar` holds it |
| `pull` | TEXT | the run id of the pull that wrote the row |

Primary key: `ticker`, `event_date`.

The earnings prints of the names a pull asked for, over the same span, from the earnings calendar asked once a calendar month and read for the index's own listing as `calendar` is. Kept apart from `calendar` for the reason `pulled_bar` is kept apart from `bar`, and removed with it by the same pull.

### pulled_surprise
Grain: one row per ticker per earnings report date a surprise pull reached.

| Column | Type | Notes |
|---|---|---|
| `ticker` | TEXT | |
| `event_date` | TEXT | the report date the provider files |
| `timing` | TEXT | `before`, `after`, or `unstated`, as `calendar` holds it |
| `eps_actual` | TEXT | the earnings per share reported, as the provider sent it, and null where the print has not happened; text because nothing computes with it |
| `eps_estimate` | TEXT | the estimate the provider carries, as sent, and null where it carries none |
| `surprise_percent` | REAL | the provider's surprise in per cent, a statistic; null where the print carries no estimate, no actual or no surprise, since a surprise against nothing is not one |
| `pull` | TEXT | the run id of the pull that wrote the row |

Primary key: `ticker`, `event_date`.

**The surprises of the names a surprise pull asked for over its span, read by no night** (see: The surprises pulled before the store's year sit beside the pulled prints and are read by no night). The operator's `history-pull --surprises` asks the earnings calendar once a calendar month of the span, as the bars' pull asks it, and stores each print of a name the index held over the span with the figures as filed and the provider's surprise, which is the one figure the sweep's fifth condition reads. Kept apart from `calendar` and from `pulled_earnings` because it carries figures those do not and is removed whole with its pull, and a second pull inserts only the prints no earlier pull holds. The sweep reads it by hand where a pull stored any print carrying a surprise, and the calendar's own year otherwise.

### pulled_market_bar
Grain: one row per series per session a market pull or a sector funds pull reached.

| Column | Type | Notes |
|---|---|---|
| `series` | TEXT | `GSPC`, the index itself, `VIX`, the ticker of one of the eleven sector funds, or from 15.1 of one of the index and credit funds |
| `session_date` | TEXT | date |
| `open`, `high`, `low`, `close` | TEXT | decimal in code, as the provider sent them on the pull's day |
| `pull` | TEXT | the run id of the pull that wrote the row |

Primary key: `series`, `session_date`.

**The index's and the VIX's daily series, read by no night** (see: The index's and the VIX's daily series are pulled beside the pulled bars, marked by their pull and read by no night). The operator's `history-pull --market` asks the provider once a series for the whole span, under its index exchange rather than a listing, and stores every session it sends. Kept apart from `bar` and `pulled_bar` because neither series is a member's, and removed whole with its pull as they are; a second pull inserts only the sessions no earlier pull holds. A series the provider refuses stores nothing and the pull fails, so nothing reads a series that is not there. The ideas' run reads both series through the sweep history, by hand and never on a night.

**The eleven sector funds' series sit in the same table, from `history-pull --sector-etfs`** (see: The pulls behind the heavyweights and the context checks store into tables of their own and are read by no night). The pull asks the historical endpoint once a fund, under the stock exchange the funds list on, and stores every session sent in the adjusted form a member's bars take, so a fund's return over a span is read as a member's is; a fund refused stores nothing, the others are stored and the pull fails. A fund's series is read by measurements alone, as the index's and the VIX's are. From 15.1 `history-pull --index-funds` pulls SPY's, IJH's, IJR's and HYG's series the same way under a stage of its own, for the readings and the sweeps of each index.

### pulled_company
Grain: one row per ticker a companies pull answered.

| Column | Type | Notes |
|---|---|---|
| `ticker` | TEXT | the ticker the index holds the name under, which the pull asked for |
| `cik` | TEXT | the filer's CIK padded to ten digits, null where the provider files none |
| `sector` | TEXT | the GICS sector the provider files as of its last update, null where it files none |
| `industry_group` | TEXT | the GICS industry group, null where none is filed |
| `industry` | TEXT | the GICS industry, null where none is filed |
| `sub_industry` | TEXT | the GICS sub-industry, null where none is filed |
| `delisted_on` | TEXT | the day a delisted company left its exchange, null for a company still listed |
| `pull` | TEXT | the run id of the pull that wrote the row |

Primary key: `ticker`.

**Each company the history holds, as the provider files it today, read by no night** (see: The pulls behind the heavyweights and the context checks store into tables of their own and are read by no night). The operator's `history-pull --companies --from <date>` asks the fundamentals endpoint once for every name the index held on any session of the span, departed members among them, with a filter naming these fields, and stores one row a name it answered; a name not served stores nothing and is named on the pull's row. The sector is GICS's, which no other table holds: `membership.sector` is the index snapshot's scheme. It has no date, so a session reads it through the fourteen moves of 2023-03-17 (see: A company's sector on a session is the GICS sector the provider files, with the fourteen moves of 2023-03-17 read by date), and two listings carrying one CIK are one company (see: Companies are ranked by CIK with one listing held, the class that traded the more dollars over fifty sessions). Removed whole with its pull, and a second pull adds only the tickers no earlier pull holds.

### pulled_shares
Grain: one row per ticker per quarter a companies pull answered with a count and the day its balance sheet was filed.

| Column | Type | Notes |
|---|---|---|
| `ticker` | TEXT | |
| `period_end` | TEXT | the last day of the quarter the balance sheet closes |
| `filing_date` | TEXT | the day the balance sheet was filed |
| `shares` | TEXT | decimal in code, the shares outstanding as the provider files them, restated to the split basis of `basis_session` |
| `basis_session` | TEXT | the session the pull ran on, whose split basis the provider restates every count to |
| `pull` | TEXT | the run id of the pull that wrote the row |

Primary key: `ticker`, `period_end`.

**The counts a company's value on a session is read from, read by no night** (see: A company's value on a session is the newest share count filed before it times the session's close on the count's split basis). A session reads the count of the latest quarter among the sheets filed before it, times the session's unadjusted close divided by every split in `pulled_split` after the session and on or before `basis_session`. A balance sheet carrying no count or no filing date cannot be read as it stood and stores no row; the pull counts them on its row. Removed whole with its pull, and a second pull adds only the quarters no earlier pull holds.

### pulled_split
Grain: one row per ticker per split a splits pull answered over its span.

| Column | Type | Notes |
|---|---|---|
| `ticker` | TEXT | |
| `ex_date` | TEXT | the first session trading on the new basis |
| `new_shares` | TEXT | decimal in code, the shares one block of old shares became |
| `old_shares` | TEXT | decimal in code, the old shares in that block |
| `pull` | TEXT | the run id of the pull that wrote the row |

Primary key: `ticker`, `ex_date`.

**The splits a session's close is put on a count's basis by, read by no night.** The operator's `history-pull --splits --from <date>` asks the per-name splits endpoint once for every name the index held over the span and stores each split from the date to the night it ran, a ratio kept as its two numbers rather than as their quotient, which a split of one for three would round. The provider files a spin-off's and a merger's price adjustment as a split too, 1,281 for 1,000 at GE HealthCare's spin-off, and they are stored as sent: a value reads a plain split as a change of shares and the rest as none, told apart by the ratio (see: A company's value on a session is the newest share count filed before it times the session's close on the count's split basis). Removed whole with its pull, and a second pull adds only the splits no earlier pull holds.

### pulled_revenue
Grain: one row per filer per revenue concept per period per filing stating it.

| Column | Type | Notes |
|---|---|---|
| `cik` | TEXT | the filer's CIK padded to ten digits, as `pulled_company` carries it |
| `concept` | TEXT | the archive's concept the figure was filed under |
| `period_start` | TEXT | the first day of the period the figure covers |
| `period_end` | TEXT | the last day of the period |
| `accession` | TEXT | the accession number of the filing that stated it |
| `dollars` | TEXT | decimal in code, the revenue stated |
| `filed` | TEXT | the day the filing was made |
| `form` | TEXT | the filing's form, as the archive names it |
| `pull` | TEXT | the run id of the pull that wrote the row |

Primary key: `cik`, `concept`, `period_start`, `period_end`, `accession`.

**Every revenue figure each pulled company's filer stated, with the day it was filed, read by no night** (see: A quarter's revenue is read as first filed, a fiscal fourth quarter being the year less its first nine months). The operator's `history-pull --revenue` asks the archive once a filer for its whole facts, for the filers `pulled_company` carries, and stores every figure stated for a period under each revenue concept, a figure stated for an instant being no revenue. Keyed on the filer rather than the ticker, since two listings of one company share a filer, and on the filing, since a quarter a later filing states again as its year-earlier column or restates is a row of its own beside the one that first stated it, which is what lets a quarter be read as first filed. A concept a filer never filed under stores nothing and fails nothing. Removed whole with its pull, and a second pull adds only the figures no earlier pull holds.

### pulled_member
Grain: one row per wider index per member its answer listed today.

| Column | Type | Notes |
|---|---|---|
| `index_code` | TEXT | `MID` for the S&P 400 or `SML` for the S&P 600 |
| `ticker` | TEXT | the listing's code as the answer files it |
| `exchange` | TEXT | the listing's exchange as the answer files it |
| `name` | TEXT | the company's name as filed, null where none is |
| `sector` | TEXT | the sector as filed, null where none is |
| `industry` | TEXT | the industry as filed, null where none is |
| `pull` | TEXT | the run id of the pull that wrote the row |

Primary key: `index_code`, `ticker`.

**Today's members of the S&P 400 and 600, survivors alone, read by no night** (see: A wider universe is tested first on today's members, and widened only where a family's edge improves even so and holds on membership as it stood). The operator's `history-pull --members --index <MID or SML>` asks that index's fundamentals once and stores each member its answer lists today. The answer carries no span of membership, so a row says only that the name is a member today, and a company that left either index before today is in neither. Every other pull given that index reads its names from these rows. The sweep history reads them as members on every session when the wider universe is asked for. Removed whole with its pull, and a second pull adds only the members no earlier pull holds.

### pulled_income
Grain: one row per ticker per quarter a companies pull answered with an income statement and the day it was filed.

| Column | Type | Notes |
|---|---|---|
| `ticker` | TEXT | |
| `period_end` | TEXT | the last day of the quarter the income statement closes |
| `filing_date` | TEXT | the day the statement was filed |
| `net_income` | TEXT | decimal in code, the quarter's net income as filed, null where the statement states none |
| `operating_income` | TEXT | decimal in code, the quarter's operating income as filed, null where the statement states none |
| `interest_expense` | TEXT | decimal in code, the quarter's interest expense as filed, with the sign the provider files it under, null where the statement states none |
| `pull` | TEXT | the run id of the pull that wrote the row |

Primary key: `ticker`, `period_end`.

**Each company's quarterly income as its filer filed it, read by no night** (see: The 400 and 600 rules start provisional with liquidity floors and a profit gate before any testing). From 15.2 the companies pull stores each quarter of the income statements the same answer carries beside its balance sheets, at no request of its own. The S&P 400's and 600's profit gate sums the net income of the four newest quarters filed before a session, and their coverage reads the operating income against the interest expense of the same four, so a sweep reads each as it stood. A statement carrying no filing date cannot be read as it stood and stores no row; the pull counts them on its row. Removed whole with its pull, and a second pull adds only the quarters no earlier pull holds.

### pulled_snapshot
Grain: one row per wider index per quarter end its fund filed its holdings for.

| Column | Type | Notes |
|---|---|---|
| `index_code` | TEXT | `MID` for the S&P 400's fund, IJH, and `SML` for the S&P 600's, IJR |
| `period` | TEXT | the quarter end the filing's holdings are as of |
| `accession` | TEXT | the filing's accession number at the SEC, an N-PORT or, for the quarter ends 2018-12-31 and 2019-03-31, the N-Q and the annual report carrying the fund's schedule |
| `filed` | TEXT | the day the filing was filed |
| `holdings` | INTEGER | every holding the filing lists |
| `equity` | INTEGER | the holdings of common stock among them, each a row of `pulled_holding` |
| `pull` | TEXT | the run id of the pull that wrote the row |

Primary key: `index_code`, `period`.

### pulled_holding
Grain: one row per wider index per quarter end per holding of common stock its fund filed, keyed by the holding's ISIN, its CUSIP where it carries no ISIN, or its name where it carries neither.

| Column | Type | Notes |
|---|---|---|
| `index_code` | TEXT | as `pulled_snapshot` |
| `period` | TEXT | as `pulled_snapshot` |
| `holding` | TEXT | the key the holding is stored under |
| `name` | TEXT | the holding's name as the fund filed it |
| `cusip` | TEXT | null where the fund filed none, as a schedule before the first N-PORT files none |
| `isin` | TEXT | the ISIN filed, or the one the CUSIP makes, null where neither is filed |
| `ticker` | TEXT | the provider's code the holding matched, null where it matched none; matched again by a later pull reading the quarter |
| `matched_by` | TEXT | `isin`, `name`, `held`, `carried` or `figi`, `held` for a code its fund held by ISIN within a year and, from 17.1, `carried` for a schedule's holding carrying the next coded quarter's code and `figi` for a ticker OpenFIGI mapped its identifier to, null where it matched none; matched again with `ticker` |
| `pull` | TEXT | the run id of the pull that wrote the row, kept when a later pull matches it again |
| `shares` | TEXT | from 17.1, the shares the fund filed, as filed; null on a row stored before and not yet matched again, or where the filing states none |
| `value_usd` | TEXT | decimal in code, from 17.1, the holding's value in dollars as filed, named for the N-PORT's own element; the value a share is this over `shares`; null as `shares` |

Primary key: `index_code`, `period`, `holding`.

**The S&P 400's and 600's funds' quarter-end holdings, read by no night** (see: Membership as it stood is rebuilt from the funds' quarterly holdings filed with the SEC, matched by ISIN and then by name). From 15.3 the holdings pull reads each fund's public quarter-end filings at the SEC, the 28 from the quarter to 2019-09-30, the N-Q for the quarter to 2018-12-31 and the annual report for the year to 2019-03-31, whose schedules name each holding with no identifier (see: The funds' holdings before their first public N-PORT are read from their N-Q of 2018-12-31 and their annual report of 2019-03-31), and the provider's listed and delisted US symbols, and stores every holding of common stock with the code it matched: by its ISIN first, which names one security however its ticker was later reused, then by its name with the corporate suffixes taken out, the provider's names first and then the names the funds' own filings carry beside an ISIN, both funds' alike, a name match kept only where its code traded in the six days to the quarter's end at a close the fund's value a share stands within 5 per cent of, read off a pulled bar or, where none is pulled, off the provider's own daily prices for those days, and a name nothing matched read again against the symbols sharing its first word and half its words (see: A holding matched by name is kept only where its code traded at the quarter's end); every code a holding matched kept only where its closes move in step with the fund's values a share over the quarters it matched, the holding's codes by name read in place of one that does not and each held to the level at every quarter end it is read at or to one ratio across them (see: A holding's code is kept only where its closes move in step with the fund's values a share, and a match by name is held to the level at each quarter end as well), from 17.1 a holding still matched to none whose identifier the symbol lists carry under no code mapped through OpenFIGI to the tickers it traded under, each read under the same checks and stored under `figi` where it holds (see: A holding the symbol lists carry under no code is mapped to the tickers it traded under through OpenFIGI, each held to the checks a name's code is), a holding still matched to none at two quarters or more read against the codes its fund held by ISIN within a year, a code it held by ISIN as another holding at one of this holding's quarter ends left out (see: A renamed company is matched to a code its fund held by ISIN within a year where its close equals the value a share to the cent at two quarter ends) (see: A code the fund held by ISIN beside a holding at one quarter end is another holding and never that holding renamed), and from 17.1 a holding of the N-Q or the annual report still matched to none carrying the code the next coded quarter's holding of the identical name matched under the key `carried` (see: A holding of a schedule filed with no identifier carries the code the next coded quarter's holding of the identical name matched, where the closes move in step across them); and a holding matched by none stored with no code and named on the pull's row. From 17.1 each holding carries its shares and its value as the filing states them, as text, from which the fund's value a share is read; a row stored before 17.1 holds none until a pull matches its quarter again. The sweeps read membership as it stood off these rows, a name a member from the first snapshot holding it to the last, and every other pull of a wider index asks for each code a snapshot matched beside the members today. Removed whole with its pull. A second pull adds the quarters no earlier pull holds and matches the holdings of the others again, updating a row's code and key where the rule as it stands reads another and its shares and value where none were stored, so a pull is never removed to be matched again.

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
| `members` | TEXT | JSON: each member's source, kind, price and date |

Primary key: `ticker`, `as_of`, `low_edge`.

`has_non_average_anchor` is a stored column rather than a derived one because the ladder builder reads it on every band and a short moving average follows the price, so a band anchored only on one sits at the price about half the time.

A band set stored before member sources were written carries none, and it is kept as written. The rule version scorer does not read a source off a kind, since two sources can write the same word, so a version that reads members is skipped over such a set and counted on the step's run log row.

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
| `group_kind` | TEXT | `industry` or `sector`, the group the move is read against; null on a row written before 11.5 |
| `group_name` | TEXT | the industry or sector the membership row names, null where it names none |
| `group_members` | INTEGER | how many other members the group holds on the session, the name never among them |
| `group_counted` | INTEGER | how many of them held a close on both of the move's sessions |
| `group_median` | REAL | the median of those members' moves over the same sessions, in per cent; null where none held both closes |

Primary key: `ticker`, `session_date`.

**The group columns are the annotator's, written with the move they sit beside.** The group is the industry where at least five other members share it on the night's session and the sector otherwise, read off the membership, so the annotator reads the membership as well as the bars (see: A name's group is its industry where at least five other members share it on the session, and its sector otherwise, and every surface that uses it says which and how many) (see: A large move is shown beside its group's median move over the same sessions).

**`sessions` is what makes the catalogue row true, and it was added at 5.0.** The annotator selects the largest single-day and multi-day moves of the stored year, and a table keyed on one session with no span could carry only the first of those. `session_date` is the session the move ended on, so a five-day run and a one-day gap on the same date are one row and the longer span wins, which is the reading that keeps the primary key.

The cause of each move is not stored here. It is a researched claim and lives in `research_section` with its source.

### peer_reading
Grain: one row per ticker, the newest night's.

| Column | Type | Notes |
|---|---|---|
| `ticker` | TEXT | |
| `session_date` | TEXT | the name's newest stored session, which the readings are taken at |
| `group_kind` | TEXT | `industry` or `sector`, the group the name's moves are read against |
| `group_name` | TEXT | the industry or sector the membership row names, null where it names none |
| `year_high` | TEXT | decimal in code: the highest high among the bars the store holds for the name |
| `below_high_pct` | REAL | how far the newest close sits below `year_high`, in per cent of it |
| `return_pct` | REAL | the newest close against the close sixty sessions before it, in per cent; null where the name holds fewer bars than that and the one it is measured from |
| `bars` | INTEGER | how many bars both readings were read over |
| `peers` | TEXT | JSON: the members of the group the name's peers table draws, in the order it draws them, ten at most, each with `ticker`, `sameIndustry`, `likeness`, the correlation of the two names' daily returns over the sessions both hold, null where they share fewer than sixty, and `sessions`, how many they share; null on a row written before migration 49 |

Primary key: `ticker`.

**The move annotator writes it over the bars it already reads for the moves, and is its own deleter** (see: Every computed table's writer is its own deleter) (see: Peers are shown by price alone, ten at most with the name's industry first and then the members whose daily moves followed it most closely). A row is replaced every night rather than kept by night, so a name's page for an earlier night says the readings are the newest night's and draws none. The annotator deletes the row of a name the gap stop withheld that night and of a name holding no bars, since a row left standing would be read beside a close it was not taken at. Nothing reads it but the peers table: no reason, gate, plan or candidate evaluator.

### earnings_reaction
Grain: one row per ticker and print over the calendar's year behind.

| Column | Type | Notes |
|---|---|---|
| `ticker` | TEXT | |
| `report_date` | TEXT | the day the print was reported |
| `timing` | TEXT | `before`, `after` or `unstated`, as the calendar holds it |
| `reaction_session` | TEXT | the session the earnings rule takes for the print |
| `estimate` | TEXT | the estimate as the provider sent it; null where it filed none |
| `actual` | TEXT | the actual as the provider sent it; null where it filed none |
| `surprise_pct` | REAL | the provider's surprise, in per cent; null beside no estimate |
| `move_pct` | REAL | the reaction session's close against the close before it, in per cent |

Primary key: `ticker`, `report_date`.

**The move annotator writes it from the calendar and the stored bars, and is its own deleter** (see: Every computed table's writer is its own deleter) (see: Each print's reaction is read from the nightly calendar and the stored bars, and the earnings drift is the one rule that reads it). A print's session is the one the earnings rule takes for it, read by calling the rule's own function one print at a time, so no source a rule version pins is edited to share the reading. A print whose session the stored bars do not reach, or whose session has no stored close before it, is left out and counted on the run log rather than read off a session the store does not hold, which is how the retention drops a print with its bars. The annotator writes the whole set again every night and deletes the prints a name no longer holds, and the rows of a name the gap stop withheld or that holds no bars. A print with no filed estimate keeps its actual and carries no surprise, so it is never read as having met an estimate. The name page draws it, the facts assembler quotes it, and the family evaluator reads each name's newest print for the earnings drift; no reason, no gate of the swing filter, no plan and no candidate evaluator reads it.

### swing_reading
Grain: one row per ticker and night, **for every index member on the night and not only the names it could read**.

| Column | Type | Notes |
|---|---|---|
| `ticker` | TEXT | |
| `session_date` | TEXT | the night, being the newest session any name holds |
| `bars` | INTEGER | how many bars the readings were read over |
| `return_short` | REAL | tonight's close against the close 63 sessions before it, in per cent; null where the name holds too few bars |
| `return_long` | REAL | the same over 126 sessions |
| `place_short` | REAL | the share of the other members read tonight whose 63-session return is strictly lower, one sharing it counting half; null where the name has no such return or no other member has one |
| `place_long` | REAL | the same over 126 sessions |
| `strength` | REAL | the mean of the two places, and null where either is |
| `recent_high` | TEXT | decimal in code: the highest high of the 20 sessions ending tonight; null where the name holds fewer |
| `high_session` | TEXT | the newest of those sessions making that high |
| `pullback_sessions` | INTEGER | how many sessions have traded since the high's session, 0 where it was made tonight |
| `depth` | REAL | how far tonight's close sits below `recent_high`, in tonight's typical daily moves; null where the night holds no typical move |
| `dry_up` | REAL | the median volume of the sessions since the high's session against the fifty-day average; null where the high was made tonight or the night holds no average |
| `tightness` | REAL | the mean true range of the last 10 sessions against the mean of the last 50; null where the name holds too few sessions |
| `note` | TEXT | why a member was read over nothing: no bar stored, no bar for the night with the last session it holds, or a gap in its stored series with the gap's date; null where it was read |

Primary key: `ticker`, `session_date`.

**The swing reader writes it for every member every night and is its own deleter** (see: Every computed table's writer is its own deleter). A night run again replaces its own set whole, and the rows fall out one year back from the newest stored session as every computed table's do. A member the night reads nothing for keeps its row with the reason, for the reason the listing row does: a gate whose reading is absent says why rather than finding no row. The places are read among the members read that night, so a name with no return and a name the night could not read are in no one's population.

### market_reading
Grain: one row per night.

| Column | Type | Notes |
|---|---|---|
| `session_date` | TEXT | the night |
| `members` | INTEGER | the index's members on the night |
| `counted` | INTEGER | the members read that night holding a 200-day average |
| `above` | INTEGER | how many of them close above it |
| `breadth` | REAL | `above` over `counted`; null where `counted` is under half of `members` |
| `counted_context` | INTEGER | the same over the 50-day average |
| `above_context` | INTEGER | |
| `breadth_context` | REAL | context only, and nothing reads it for a decision |
| `volume_counted` | INTEGER | the members read that night trading some volume and holding a fifty-day average above nought |
| `median_volume_ratio` | REAL | the median of their volume against that average, and null over none |

Primary key: `session_date`.

**The swing reader writes it and is its own deleter** (see: Every computed table's writer is its own deleter), dropping a night one year back from the newest stored session as it drops the name rows. What a night's breadth decided is kept on the gate rows the filter writes from 12.2, which are kept forever.

### gate_result
Grain: one row per index member per night.

| Column | Type | Notes |
|---|---|---|
| `ticker` | TEXT | |
| `session_date` | TEXT | the night |
| `version` | TEXT | the open filter version the night ran under, or `none` where none was open and it ran on section 17's proposed values, or `replayed` on a session before the filter's first stored night whose results `FilterHistory` replayed under the open version's settings, which the trigger's arrival reads and nothing else does, and which `FilterHistory` removes on the operator's command once no night can read them |
| `code` | TEXT | the pin of the filter's code the row was written by |
| `market` | INTEGER | 1 where the market gate passed |
| `trend` | INTEGER | 1 where the trend and strength gate passed |
| `setup` | INTEGER | 1 where a setup passed |
| `family` | TEXT | `pullback` or `breakout`, null where no setup passed |
| `trigger_pass` | INTEGER | 1 where the trigger passed, which for a pullback is its event arriving inside the window the settings name: tonight or a session before whose own stored result shows the event, with none on the session before that one |
| `trigger_event` | INTEGER | 1 where the pullback's trigger event happened tonight, 0 where it did not, null where the night's bars cannot say |
| `trade` | INTEGER | 1 where the trade gate passed on the plan the settings name |
| `ladder_reward_to_risk` | REAL | the ladder's first tranche's reward to risk as the listing kept it, null where it computes none |
| `ladder_stop_moves` | REAL | how far that tranche's stop sits below its entry in typical moves |
| `swing_entry` | TEXT | the swing trade's entry, the night's close, which both swing plans enter at |
| `swing_stop` | TEXT | the swing trade at the nearest bands: its stop, the setup band's low edge |
| `swing_target` | TEXT | its target, the lowest low edge of a band above the close |
| `swing_reward_to_risk` | REAL | |
| `swing_stop_moves` | REAL | |
| `exclusions` | TEXT | JSON: the exclusions that apply, empty where none does |
| `passed` | INTEGER | 1 where every gate passed and no exclusion applies |
| `rank` | INTEGER | the place among the names passing, null for every other |
| `strength` | REAL | the mean of the two places the ranking reads |
| `band_strength` | INTEGER | the setup band's strength, the ranking's third key |
| `gates` | TEXT | JSON: the five gates' answers, each with its reason and values, and the notes on what could not be counted |
| `shadow` | TEXT | JSON: from 12.5, each swing family candidate standing when the night started, whether it fired with each gate's answer, the market read or not, the exclusions and the session it arrived on, and the candidates the night could not evaluate with why; null on a row written before it or with no family standing |
| `clear_stop` | TEXT | from migration 47, section 10's plan for the swing trade, entered at `swing_entry`: its stop, the setup band's low edge, or the next support band's beneath it where that is less than a typical move below the close; null where no stop is placed and on a row written before the column (see: A swing filter row carries both swing plans, each scored from the night's close, and a candidate's setups are scored on the plan its own trade gate reads) |
| `clear_target` | TEXT | its target, the lowest low edge of a band two typical moves or more above the close, null where none sits there |
| `clear_reward_to_risk` | REAL | null where either is |
| `clear_stop_moves` | REAL | how far its stop sits below the close in typical moves, wherever a stop is placed |

Primary key: `(ticker, session_date)`.

**The swing filter writes it and is its own deleter** (see: Every computed table's writer is its own deleter), and its only delete is a night run again replacing its own rows. `FilterHistory` deletes only the replayed rows it wrote, on the operator's command, for sessions no night can read any longer, and never a night's own row (see: The swing filter's results are replayed for the sessions before its first stored night for the trigger's arrival alone, and removed once no night can read them). Nothing is dropped by age: a gate's near misses are read over the years the edge clock needs, and the readings behind a row are dropped after one.

### filter_version
Grain: one row per version of the swing filter's settings.

| Column | Type | Notes |
|---|---|---|
| `version` | TEXT | the version's name, its place in the order the versions were opened, 1 for the first |
| `settings` | TEXT | JSON: every threshold the gates read and the trade gate's input, each named |
| `opened_at` | TEXT | UTC instant |
| `closed_at` | TEXT | UTC instant, null while the version is open |
| `evidence` | TEXT | the figures it was opened on |

Primary key: `version`.

**The shape command writes it and is its only writer** (see: A shape acceptance restarts the live filter's edge clock, and after one acceptance while the list is live each further one states the blocks it restarts): an acceptance, of a proposal or of settings the operator rules, closes the open row and inserts the next at one instant, and nothing deletes a version. The swing filter reads the open row and, where none is open, runs on section 17's proposed values and says so on every row.

### shape_proposal
Grain: one row per proposal the shape proposer writes.

| Column | Type | Notes |
|---|---|---|
| `id` | INTEGER | the proposal's number, which the command takes |
| `proposed_at` | TEXT | UTC instant |
| `session_date` | TEXT | the night whose run crossed the trigger |
| `version` | TEXT | the filter version whose ordinary nights it read |
| `ordinary` | INTEGER | how many ordinary nights it read |
| `current_settings` | TEXT | JSON: the settings the version held, each named |
| `settings` | TEXT | JSON: the settings proposed, each named |
| `levers` | TEXT | JSON: each gate's setting, the value held and the one proposed, none where no value in the range reaches the band or the gate has no threshold, its median count under each, and its band |
| `list_now` | REAL | the list's median over the ordinary nights under the settings held, null over none |
| `list_proposed` | REAL | the same under the settings proposed |
| `findings` | TEXT | JSON: each gate no value in its range brings inside its band, in words |
| `decision` | TEXT | `accepted` or `rejected`, null until the command takes one |
| `decided_at` | TEXT | UTC instant, null until then |
| `reason` | TEXT | a rejection's reason, null otherwise |
| `opened` | TEXT | the version an acceptance opened, null otherwise |

Primary key: `id`.

**The shape proposer inserts it and the shape command writes its decision** (see: The shape proposer moves one setting a gate, nearest first, and never applies what it proposes). The proposer writes one row a version once sixty ordinary nights are stored under it, and after a rejection the next once sixty more are; the command writes `decision`, `decided_at`, `reason` and `opened` on a row with no decision and nothing else on it, and nothing deletes one.

### listing
Grain: one row per ticker per night, **for every index member and not only the listed ones**.

| Column | Type | Notes |
|---|---|---|
| `ticker` | TEXT | |
| `session_date` | TEXT | |
| `reasons` | TEXT | JSON: each of the six reasons with fired true or false and the values that made it so. From the 5.4 correction earnings soon's values carry `next dated event` and breakout on volume's carry `previous close`, and a row without them was written before it (see: Sessions to a dated event are counted on the exchange calendar and never on stored bars). A member the night evaluates over nothing, having no bar for the session, a gap in its stored series or no bar stored at all, fires nothing, and its earnings soon carries the date the calendar holds on or after the row's session with `not counted` and the reason in place of the count (see: A member the night evaluates over nothing keeps the dated event the calendar holds and says no count was made) |
| `fired_count` | INTEGER | how many of the six reasons fired on the row, counted from `reasons` |
| `plan_at_listing` | TEXT | JSON: the entry zone, stop and first traded target as they stood that night |
| `shadow_reasons` | TEXT | JSON: `candidates`, each registered candidate the night evaluated with whether it fired and the values that made it so, and `skipped`, each registered candidate the night could not evaluate with the reason. Written for every member on every night exactly as `reasons` is, and drawn on no screen (see: Candidate conditions are registered before they are scored, and a candidate's picks are shown on the Run page while its outcomes wait for a look). A row written before 8.4 carries an empty `candidates` and a `note` naming the checkpoint the register was then due at, in place of `skipped` |
| `band_strength` | INTEGER | the highest strength of any band the night stored for the name on the row's session, and 0 where it stored none, which a stale or gapped member's session never holds; null on a row written before 10.1. What the order tonight's list is compared against reads, kept here because the bands are dropped a year back and the listing is kept (see: A listing records the band strength the old order read, and the three orders are compared over the nights that recorded it) |

Primary key: `ticker`, `session_date`.

**`shadow_reasons` carries two lists and not one, from 8.4.** A candidate that did not fire and a candidate nothing evaluated are opposite statements: the first is a measurement and the second is a hole in one. Folding the second into the first is how a record of having skipped a name-night stops existing, and the correction later divides by a family whose members are assumed to have been scored throughout. So a candidate the night could not evaluate is written into `skipped` with its reason rather than being absent. Only a candidate whose evaluator the code does not carry, or whose evaluator's version has moved, is named as a failure on the listings stage's run log row, once; a name with no bar for the session, a name whose stored series has a gap, and a reading not available are skips of that name-night alone, counted by cause on the same row and failing nothing (see: Only a missing or moved evaluator fails the shadow column, and a name-night without the readings is a counted skip).

**`plan_at_listing` is the column the improvement loop rests on.** Bars can be replayed and the plan cannot, because by the time a verdict is possible the rules may have changed and recomputing would score old listings under new ones. It is written by a component that is already running and it is the difference between the loop being a feature and being a wait.

About 125,000 rows a year at index size. Kept forever.

### list_rule
Grain: one row per session a night's swing filter drew the list for.

| Column | Type | Notes |
|---|---|---|
| `session_date` | TEXT | the session the list was drawn for, the newest any name holds on the night that drew it |
| `rule` | TEXT | `filter`, the swing filter; `reasons` is the rule an evening holding no row was drawn by, and the column allows it so the rule is one word wherever it is read |

Primary key: `session_date`.

**The night close writes it and is its only writer, from the swing filter's step** (see: Tonight's list is the swing filter's with improving businesses drawn first, and an evening is listed and ordered by the rule that listed it). Once the swing filter has stored its rows for the night's session, the night records that session as listed by the filter, before any later step can stop the night, and only where the filter stored rows for it. A night run again over a session records it again, and the session is then listed by the rule of the night that drew it last. An evening holding no row was listed by any of the six reasons firing, which is every evening before the switch, so every surface reading a listing reads this table beside it and names the rule. Nothing deletes a row.

### family_result
Grain: one row per session, member and setup family but the pullback.

| Column | Type | Notes |
|---|---|---|
| `session_date` | TEXT | the session the member was evaluated for, the newest any name holds on the night |
| `ticker` | TEXT | |
| `family` | TEXT | the family, by the word it is stored under |
| `passed` | INTEGER | 1 where every gate passed and no exclusion applies, 0 otherwise |
| `missed` | INTEGER | how many of the family's gates the member did not pass, which a name close to a buy point is counted by |
| `place` | INTEGER | the place among the names the family passed that night, in the family's own order, null for every other |
| `entry` | TEXT | the price the trade is bought at, the night's close, null where the member holds no bar for the night |
| `stop` | TEXT | the trade's stop as the night placed it, the first level of a stop that trails; null where none could be placed |
| `target` | TEXT | the trade's target, null for a family that trails its stop and names none |
| `order_by` | REAL | the figure the family's order reads, largest first: a breakout's volume against its average, an earnings drift's surprise, a sector leader's sector rank, negated so the first sector sorts first, its own return then deciding among the leaders of one sector; null where the family reads none for the member |
| `exclusions` | TEXT | JSON: the exclusions the member's series carries, a gap or a suspect series as the swing filter stored them, empty where none does |
| `gates` | TEXT | JSON: the family's gates in order, each with whether it passed, its reason and the values that decided it, the first of them the market gate the swing filter stored that night |
| `shadow` | TEXT | JSON: each candidate registered under the row's family and standing when the night started, with whether it fired at its own settings and the values that decided it, among them the buy, the stop, the target, the figures the family's order reads and the night's typical move; and each one skipped with why; null where none of the family stands registered (see: A registered family rule is evaluated every night at its own settings and keeps its own list, its trades stored with their benchmark when they end) |

Primary key: `session_date`, `ticker`, `family`.

**The family evaluator writes it in the swing filter's step and is its own deleter** (see: Tonight's page is drawn from setup families, each a rule of its own listing at most five a night) (see: Every computed table's writer is its own deleter). After the swing filter has stored its rows, it evaluates every member the filter evaluated under each family but the pullback, whose answers are the filter's own `gate_result` rows, and stores every answer, the ones that did not pass included, since a card that lists nothing says how far the members got and a name one gate short is drawn as close to a buy point. The market gate on each row is the one answer the filter stored for the night, so no family passes a member on a night it closed (see: The market check closes every swing family's list together, and the sector heavyweights read none). A night run again replaces its own rows whole. A row that passed is a trade its family's record counts and is kept; a row that did not is deleted once its session is older than the oldest bar the store holds, since nothing reads a near miss whose bars are gone.

**A passed row's trade is scored on `forward_return` under its family's horizon,** from the night's close: a breakout's under `breakout`, sold on its trailing stop (see: A breakout is a close above the year's high on heavy volume after its ranges narrowed, sold on a trailing stop with no target), and an earnings drift's under `drift`, to its target, its stop or its cap as a setup is (see: The earnings drift buys a beat with a strong reaction within five sessions, stopped under the reaction session's low). A sector leader's row, stored on a night before the sector leaders became a variant of the pullback, holds the pullback's plan as the swing filter's row stored it, and its trade is scored on that `gate_result` row, under the horizon of the plan its night's filter version read, so no outcome row is written for it here (see: The sector leaders are a variant of the pullback's starting point and not a family of their own). The prices here keep the scale the series had on the night, as a setup's plan does.

### family_night
Grain: one row per session the setup families drew the page's list for.

| Column | Type | Notes |
|---|---|---|
| `session_date` | TEXT | the session the list was drawn for, the newest any name holds on the night that drew it |
| `families` | TEXT | JSON: the families on the page that night, by the word each is stored under, in the page's order |

Primary key: `session_date`.

**The family lister writes it in the swing filter's step and is its own deleter** (see: Tonight's page is drawn from setup families, each a rule of its own listing at most five a night) (see: Every computed table's writer is its own deleter). It records each session it draws whether or not any family passed a stock on it, so a session the families drew and listed nothing on is told from one drawn before them: a reader of the list asks this table which rule drew a session's list, and where it holds no row reads the list as it did before the families, by the swing filter's passing names on a night `list_rule` names the filter for and by the reasons before that. The families are kept by the night, so an earlier night's page is drawn with the families that night's page held whatever families have joined since. A night run again replaces its own row, and a night the swing filter stored no result for writes none.

### family_pick
Grain: one row per session, stock and family that passed it.

| Column | Type | Notes |
|---|---|---|
| `session_date` | TEXT | the session the list was drawn for |
| `ticker` | TEXT | |
| `family` | TEXT | the family that passed the stock, by the word it is stored under |
| `state` | TEXT | `listed`, the page lists the stock under this family; `under another`, it is listed tonight under a family earlier in the page's order; `open trade`, a trade a list made for it on an earlier night is still open; `past five`, the family's five places were taken |
| `place` | INTEGER | a listed row's place down the page, counted from one across every family; null on a row held back |
| `also` | TEXT | JSON: on a listed row, the other families the stock qualified under that night, in the page's order; an empty list on every other row |
| `held_family` | TEXT | on an `open trade` row, the family that listed the trade still open; null otherwise |
| `held_night` | TEXT | on an `open trade` row, the session that trade was listed on; null otherwise |
| `held_index` | TEXT | on an `open trade` row whose trade an S&P 400 or 600 list made, that index, `MID` or `SML`; null otherwise, the trade an S&P 500 card's, and on every row written before 15.1's second half |

Primary key: `session_date`, `ticker`, `family`.

**The family lister writes the page's list here and is its own deleter** (see: A stock holds one trade across every swing family, and one qualifying under two is listed once under the first in the page's order). After the swing filter has stored its rows it reads the names each family passed, in that family's own order, and every trade a list made on an earlier night with what became of it, and draws the list by one rule: the families in the page's order; a stock whose trade is still open is listed by none and its row names the family and the night that listed that trade; a stock already listed tonight is not listed again; and a family lists at most five, a name past its five still listed by a later family it qualified under. So a stock holds at most one `listed` row a session, and that row's `also` carries the labels the page draws beside it. The pullback family's names are the ones `gate_result` holds as passed, improving businesses first and then the filter's own order, and its trade is the plan its night's trade gate read; another family's names are the ones `family_result` holds as passed, in the places its evaluator stored, and its trade is that row's; nothing here restates a plan. A trade's outcome is the `forward_return` row of its stock and session under its family's horizon. A night run again replaces its own rows whole and touches no other night's.

### family_trade
Grain: one row per registered family rule, stock and session the rule's own list kept a trade on.

| Column | Type | Notes |
|---|---|---|
| `candidate` | TEXT | the registered rule's name, as the register holds it |
| `ticker` | TEXT | |
| `session_date` | TEXT | the session the rule listed the stock on, the trade bought at that close |
| `family` | TEXT | the family the rule belongs to, by the word it is stored under |
| `place` | INTEGER | the trade's place on the rule's own list that night, counted from one, at most five |
| `entry` | TEXT | the price the trade is bought at, the night's close |
| `stop` | TEXT | the trade's stop as the night placed it, the first level of a stop that trails |
| `target` | TEXT | the trade's target, null for a plan that trails its stop and names none |
| `risk_moves` | REAL | the stop's distance below the buy in the night's typical moves, which the benchmark's plan is placed by; null where the night stored no typical move |
| `reward_to_risk` | REAL | the target's distance above the buy over the stop's below it, null for a trailing plan |
| `cap` | INTEGER | the sessions the trade is given, its family's |
| `ended_on` | TEXT | the session the trade ended on, a close through its stop, at its target or at its cap, or its cap's session where the stock's closes ran out before; null while it is open |
| `result` | REAL | what the trade came to in multiples of its risk; null while it is open and where the stock's closes ran out before it ended |
| `benchmark` | REAL | the average result of the same plan entered at the close on every member the index held that night with a bar and a typical move, the stop the trade's distance in each member's own typical moves and the target its reward to risk above, or the stop trailing at that distance; null until every such trade has had its cap, and where none could be entered |
| `members` | INTEGER | how many members the benchmark averaged, null until it is written |
| `cost` | REAL | from 15.2, the round trip the trade paid in multiples of its risk at the published table, its company valued as the night's member readings read it on the listing session and read in the $1 to 2 billion band where they hold none, the sale where its result puts it; null while the trade is open or ended with no result. The result is the one the record stored and is never read after it |
| `exit` | INTEGER | from 17.5, the exit of the menu the trade and its benchmark are walked under, by its number, where the rule's registration names one; null for the rule's own exit |

Primary key: `candidate`, `ticker`, `session_date`.

**The family recorder writes it in the swing filter's step, after the family lister, and is its own deleter** (see: A registered family rule is evaluated every night at its own settings and keeps its own list, its trades stored with their benchmark when they end). For each family candidate standing when the night started it reads the verdicts the family evaluator stored on that family's rows, the members it fired on in the family's own order, and keeps at most five, none whose trade on its own list is still open, a trade freeing its stock the night after it ends, so each rule's record counts the trades it alone would have made and is never held by another rule's (see: A stock holds one open trade on each rule's list, and it is free the night after its trade ends). From 17.5 a rule whose registration states the engines' hooks keeps the members meeting its conditions, ordered by its score, over the night's readings of the ledger's catalogue, and walks each trade and its benchmark under the exit it names, stored on the trade; a rule stating none keeps and walks as before (see: Every engine's settings hooks land together and all default off, so the families' pins move once). Each night it first walks every trade not yet ended over the closes since, at the scale the series has now, and writes where it ended and its result; and once a trade's cap has passed it writes the benchmark and how many members it averaged. Each is written once and never recomputed, because the bars and the typical moves they are read from are kept a year and a record is read over many. A night run again replaces the trades it kept for that night and touches no other night's. The rows are never deleted otherwise: they are the record each rule's checkpoints read.

### heavyweight_night
Grain: one row per rebalance session, sector and place among the sector's largest companies.

| Column | Type | Notes |
|---|---|---|
| `session_date` | TEXT | the rebalance session |
| `sector` | TEXT | the GICS sector, as read on the session |
| `place` | INTEGER | the company's place by value in its sector, counted from one, at most ten at the setting the freeze registered |
| `ticker` | TEXT | the listing held for the company, the class that traded the more dollars over the fifty sessions to the session |
| `company` | TEXT | the company the listing belongs to: `CIK` and its filer where the provider files one, and `ticker` and the stock where not |
| `company_value` | TEXT | decimal in code, the company's value on the session, the newest count filed before it times the session's close on the count's split basis |
| `look_back` | REAL | the stock's return over the look-back to the session, null where it holds too few closes |
| `sector_return` | REAL | the sector's return over the look-back, its fund's at the setting the freeze registered and its members' mean where a setting reads that, null where none is read |
| `lead` | REAL | the stock's return less the sector's, null where either is |
| `trend` | INTEGER | 1 where the session's close is above its 50-day average and that above its 200-day, 0 otherwise |
| `leader` | INTEGER | 1 where the rule bought the stock as its sector's leader on the session, 0 otherwise |
| `beta` | REAL | the stock's beta over 251 daily returns against the index to the session, null where it or the index holds too few closes; null on a row written before 14.6 |

Primary key: `session_date`, `sector`, `place`.

**The heavyweight book writes it in the swing filter's step on each rebalance and is its own deleter** (see: The sector heavyweights hold the largest companies leading their sectors, rotated on the first session of each month whose stored year holds the closes their readings need) (see: A heavyweight is bought where it leads its sector above nothing and passes the trend gate, and sold where the rule would not buy it). The page's book, at the setting the family's freeze registered (see: The sector heavyweights freeze at their sweep's proposal, the proposal's three passing neighbours registered beside them as variants). On the first night of a month it runs, and on its first night, it reads every member's company and its newest count, values each, ranks each sector's companies once a company and stores the largest with their returns, leads, trend gate and beta and which it bought; a member with no sector filed stands in no sector and one with no value in no rank, its return still in its sector's mean where the setting reads that (see: A sector's return is the mean of its members' own returns over the look-back). A rebalance reading the funds or the betas waits for the first night the store holds the night's close of every fund and of the index, and writes nothing until then; so does one on a night the store's year holds fewer closes than its readings need, and one reading no lead, or no beta where it reads one. A month counts as read where a row of it read a lead, so a month whose rows read none is read again. A night run again replaces its own session's rows and touches no other. Kept forever otherwise, the record of what each rebalance read, which the family's sweep is read against on sampled sessions.

### heavyweight_holding
Grain: one row per stock and the session the sector heavyweights bought it on.

| Column | Type | Notes |
|---|---|---|
| `ticker` | TEXT | |
| `entered_on` | TEXT | the rebalance session it was bought on, at that close |
| `sector` | TEXT | the sector it was bought as the leader of |
| `company` | TEXT | the company the listing belongs to, as `heavyweight_night` names it |
| `entry_close` | TEXT | decimal in code, the close it was bought at |
| `growth` | REAL | its close carried over its buy's, the product of each night's close over the close of the session it was last carried to |
| `cut` | TEXT | JSON: each of the size cut it was chosen from, its ticker, its growth carried the same way and the session it was carried to |
| `through` | TEXT | the session the holding's own growth was last carried to |
| `ended_on` | TEXT | the session it was sold on, null while it is held |
| `exit_close` | TEXT | decimal in code, the close it was sold at, null while held |
| `reason` | TEXT | `no longer the leader`, `a close under its 200-day average` or `left the index`, null while held |
| `result` | REAL | its growth less one at the sale, its return in percent of the buy as a fraction, null while held |
| `cut_return` | REAL | the mean of the size cut's growths less one at the sale, null while held |

Primary key: `ticker`, `entered_on`.

**The heavyweight book writes it in the swing filter's step, after the family recorder, and is its own deleter** (see: A heavyweight's result is the product of its daily close ratios since its buy, carried each night) (see: A heavyweight leaving the index is sold at its last session's close as a member). Every night it carries each holding's growth, and its size cut's, by tonight's closes over the closes of the session each was last carried to, both read as the store holds them tonight, ends one whose stock is no longer a member at the session it was last carried to and, where the setting reads it, one closing under its 200-day average tonight, and on a rebalance ends each the rule would not buy where the setting sells on that and buys each leader it does not hold, with the sector's largest as its size cut. It reads no market check and holds no stock back for a trade a swing family holds, nor any swing family's for it (see: The market check closes every swing family's list together, and the sector heavyweights read none) (see: A stock holds one trade across every swing family, and one qualifying under two is listed once under the first in the page's order). A night run again deletes what it bought that night, opens again what it ended that night, and writes the night again; a night for a session earlier than one it has read is read for nothing. The rows are never deleted otherwise: tonight's card and Past picks draw each in percent beside its size cut's return (see: A sector heavyweight's trade is scored by its percent return less the equal-weighted return of the size cut it was chosen from), and each registered rule's record is read off its own book.

### heavyweight_rule_night
Grain: one row per registered heavyweights rule, rebalance session, sector and place among the sector's largest companies.

| Column | Type | Notes |
|---|---|---|
| `candidate` | TEXT | the registered rule whose book read the rebalance, by its name in the register |
| `session_date` | TEXT | the rebalance session |
| `sector` | TEXT | the GICS sector, as read on the session |
| `place` | INTEGER | the company's place by value in its sector, counted from one, at most the rule's size cut |
| `ticker` | TEXT | the listing held for the company |
| `company` | TEXT | the company the listing belongs to, as `heavyweight_night` names it |
| `company_value` | TEXT | decimal in code, the company's value on the session |
| `look_back` | REAL | the stock's return over the rule's look-back to the session, null where it holds too few closes |
| `sector_return` | REAL | the sector's return over the rule's look-back, its fund's or its members' mean as the rule reads it, null where none is read |
| `lead` | REAL | the stock's return less the sector's, null where either is |
| `trend` | INTEGER | 1 where the session's close is above its 50-day average and that above its 200-day, 0 otherwise |
| `leader` | INTEGER | 1 where the rule bought the stock as one of its sector's leaders on the session, 0 otherwise |
| `beta` | REAL | the stock's beta over 251 daily returns against the index to the session, null where it or the index holds too few closes |

Primary key: `candidate`, `session_date`, `sector`, `place`.

**The heavyweight book writes it for each registered rule's own book, in the swing filter's step on each of the rule's rebalances, and is its own deleter** (see: Each registered sector heavyweights rule keeps a book of its own beside the page's, its holdings scored in percent against their size cut). Each heavyweights rule standing registered when the night started is read as the page's book is, at its own registration's settings: its first night and each first night of its period it reads every sector's largest companies and stores them with which it bought, and a rebalance reading the funds or the betas waits for a night the store holds their closes, as one waits on a night the store's year holds fewer closes than its readings need or it reads no lead, or no beta where it reads one; a period counts as read where a row of it read a lead. A night run again replaces its own session's rows for every rule and touches no other. Kept forever otherwise, the record of what each rule's rebalances read.

### heavyweight_rule_holding
Grain: one row per registered heavyweights rule, stock and the session the rule bought it on.

| Column | Type | Notes |
|---|---|---|
| `candidate` | TEXT | the registered rule whose book holds it, by its name in the register |
| `ticker` | TEXT | |
| `entered_on` | TEXT | the rebalance session it was bought on, at that close |
| `sector` | TEXT | the sector it was bought as a leader of |
| `company` | TEXT | the company the listing belongs to, as `heavyweight_night` names it |
| `entry_close` | TEXT | decimal in code, the close it was bought at |
| `growth` | REAL | its close carried over its buy's, the product of each night's close over the close of the session it was last carried to |
| `cut` | TEXT | JSON: each of the size cut it was chosen from, its ticker, its growth carried the same way and the session it was carried to |
| `through` | TEXT | the session the holding's own growth was last carried to |
| `ended_on` | TEXT | the session it was sold on, null while it is held |
| `exit_close` | TEXT | decimal in code, the close it was sold at, null while held |
| `reason` | TEXT | `no longer the leader`, `a close under its 200-day average` or `left the index`, null while held |
| `result` | REAL | its growth less one at the sale, its return in percent of the buy as a fraction, null while held |
| `cut_return` | REAL | the mean of the size cut's growths less one at the sale, null while held |

Primary key: `candidate`, `ticker`, `entered_on`.

**The heavyweight book writes it for each registered rule's own book, in the swing filter's step, and is its own deleter** (see: Each registered sector heavyweights rule keeps a book of its own beside the page's, its holdings scored in percent against their size cut). Each night it carries each of a rule's holdings and its size cut, ends the holdings the rule's exits end and buys the rule's leaders it does not hold, as `heavyweight_holding` is kept for the page's book, one holding a stock in each rule's book and none held back for another's. A night run again deletes what each rule bought that night, opens again what each ended that night, and writes the night again. The rows are never deleted otherwise: they are each rule's record, its holdings' edge, the result less the size cut's return, read over blocks of 63 sessions by the session each ended on, which the run page draws beside the swing families' rules'.

### index_family_night
Grain: one row per session and index the S&P 400's and 600's provisional rules were read for.

| Column | Type | Notes |
|---|---|---|
| `index_code` | TEXT | `MID` for the S&P 400 or `SML` for the S&P 600 |
| `session_date` | TEXT | the session read, the newest any name holds on the night |
| `members` | INTEGER | the index's members the night read, each holding a bar on the session |
| `breadth` | REAL | the share of those members holding a close and a 200-day average whose close stood above it, null where fewer than half hold both |
| `market_open` | INTEGER | 1 where the breadth stood at or above the floor the S&P 500's filter reads, 0 otherwise, which closes every swing family's list of the index that night |
| `settings` | TEXT | JSON: each family's rule as the night read it, its settings, its floors and its gate, the words each card's description is written from |
| `rebalanced` | INTEGER | 1 where the index's sector heavyweights read a rebalance that night, the first night of a month the book reads, 0 otherwise |
| `fault` | TEXT | null where the index's night was computed; otherwise the failure that stopped it, its type and message, the row then holding no breadth, 0 members and 0 in `market_open` and `rebalanced` |

Primary key: `index_code`, `session_date`.

**The index families write it in their step, after the S&P 500's families, and are its own deleter** (see: The 400's and 600's provisional picks are computed on the night by the sweep's own code into tables of their own). A night run again replaces its own rows. An index whose part of the night failed has its writes of the night undone and a row naming the failure written in their place, where no earlier try of the night computed one (see: A failure in the S&P 400's or 600's part of the night is caught and named, and the S&P 500's night is built regardless).

### index_family_result
Grain: one row per session, index, member and family.

| Column | Type | Notes |
|---|---|---|
| `index_code` | TEXT | the index the member was read in |
| `session_date` | TEXT | the session read |
| `ticker` | TEXT | |
| `family` | TEXT | the family, by the word it is stored under |
| `passed` | INTEGER | 1 where the family's provisional rule passed the member, its floors and its gate included, 0 otherwise |
| `place` | INTEGER | the place among the members the family passed that night, in the family's own order, null for every other |
| `entry` | TEXT | the price the trade is bought at, the night's close, null on a row that did not pass |
| `stop` | TEXT | the trade's stop as the night placed it, the first level of a stop that trails, null on a row that did not pass |
| `target` | TEXT | the trade's target, null for a family that trails its stop and names none |
| `trail` | TEXT | the distance a trailing stop is held under the highest close since the buy, null for a family that names a target |
| `cap` | INTEGER | the sessions the trade is given, its family's, null on a row that did not pass |
| `order_by` | REAL | the figure the family's order reads, largest first, null where it reads none |
| `reason` | TEXT | on a row that did not pass, the first part of the rule it failed in the rule's order: the market check, no setup, the price under $5, the dollar volume under the floor or the profit check; null on a row that passed |

Primary key: `index_code`, `session_date`, `ticker`, `family`.

**The index families write it in their step and are its own deleter** (see: The 400's and 600's provisional picks are computed on the night by the sweep's own code into tables of their own). Every member the night read gets a row under each family whether it passed or not, so a card says how far the members got and a count of members passing is read off the rows. A night run again replaces its own rows; a row that did not pass is deleted once its session is older than the oldest bar the store holds.

### index_family_pick
Grain: one row per session, index, stock and family that passed it.

| Column | Type | Notes |
|---|---|---|
| `index_code` | TEXT | the index whose list the row is on |
| `session_date` | TEXT | the session the list was drawn for |
| `ticker` | TEXT | |
| `family` | TEXT | the family that passed the stock |
| `state` | TEXT | `listed`, the index's page lists the stock under this family; `under another`, it is listed tonight under a family earlier in the page's order; `open trade`, a trade a list of any index made for it on an earlier night is still open; `past five`, the family's five places were taken |
| `place` | INTEGER | a listed row's place down the index's page, counted from one across its families; null on a row held back |
| `also` | TEXT | JSON: on a listed row, the other families the stock qualified under that night in the page's order; an empty list otherwise |
| `held_index` | TEXT | on an `open trade` row, the index whose list made the trade still open, `GSPC` for the S&P 500's; null otherwise |
| `held_family` | TEXT | on an `open trade` row, the family that listed the trade; null otherwise |
| `held_night` | TEXT | on an `open trade` row, the session that trade was listed on; null otherwise |

Primary key: `index_code`, `session_date`, `ticker`, `family`.

**The index families write each index's list here and are its own deleter** (see: The 400's and 600's provisional picks are computed on the night by the sweep's own code into tables of their own). The list is drawn by the S&P 500's rule within each index: the families in the page's order, a family listing at most five, a stock listed once under the first family it qualified under, and a stock whose trade on any card of any index is still open listed by none. A night run again replaces its own rows.

### index_family_trade
Grain: one row per index, family, stock and session the index's list kept a trade on.

| Column | Type | Notes |
|---|---|---|
| `index_code` | TEXT | the index whose list kept the trade |
| `family` | TEXT | the family that listed it |
| `ticker` | TEXT | |
| `session_date` | TEXT | the session it was listed on, bought at that close |
| `place` | INTEGER | its place down the index's page that night |
| `entry` | TEXT | the night's close it was bought at |
| `stop` | TEXT | its stop as the night placed it, the first level of a stop that trails |
| `target` | TEXT | its target, null for a trade whose stop trails |
| `trail` | TEXT | the distance its stop trails under the highest close since the buy, null for a trade with a target |
| `cap` | INTEGER | the sessions it is given |
| `ended_on` | TEXT | the session it ended on, null while it is open |
| `result` | REAL | what it came to in multiples of its risk before its cost, null while open |
| `cost` | REAL | its round trip in multiples of its risk at the published table, its company valued as the member readings read it under its index on its night and one they read none for in the $1 to 2 billion band, which the result after costs subtracts, null while open |
| `benchmark` | REAL | the average result of the same plan entered at the close on every member of the index that night, null until every such trade has had its cap |
| `members` | INTEGER | how many members the benchmark averaged, null until it is written |

Primary key: `index_code`, `family`, `ticker`, `session_date`.

**The index families write it and are its own deleter** (see: A 400 or 600 trade pays the published effective spread for its size and price, and its pass tests read the edge after it). Each night they first walk every trade not yet ended over the closes since and write where it ended, its result and its cost, and once a trade's cap has passed its benchmark; then they keep each listed row of tonight's lists. A night run again replaces the trades it kept for that night. The rows are never deleted otherwise: each index's Past picks and its forward-return scoring read them.

### index_heavyweight_holding
Grain: one row per index, stock and the session the index's sector heavyweights bought it on.

| Column | Type | Notes |
|---|---|---|
| `index_code` | TEXT | the index whose book holds it |
| `ticker` | TEXT | |
| `entered_on` | TEXT | the rebalance session it was bought on, at that close |
| `sector` | TEXT | the sector it was bought as a leader of |
| `entry_close` | TEXT | the close it was bought at |
| `growth` | REAL | its close carried over its buy's, the product of each night's close over the close of the session it was last carried to |
| `cut` | TEXT | JSON: each of the size cut it was chosen from, its ticker, its growth carried the same way and the session it was carried to |
| `through` | TEXT | the session its growth was last carried to |
| `ended_on` | TEXT | the session it was sold on, null while held |
| `exit_close` | TEXT | the close it was sold at, for a holding whose stock left the index its stock's close on the session it was last carried to as the bar table holds it, null while held |
| `reason` | TEXT | `no longer the leader`, `a close under its 200-day average` or `left the index`, null while held |
| `result` | REAL | its growth less one at the sale, null while held |
| `cut_return` | REAL | the mean of the size cut's growths less one at the sale, null while held |
| `cost` | REAL | its round trip as a fraction of the buy at the published table, its company valued as the member readings read it under its index on its buy and one they read none for in the $1 to 2 billion band, null while held |
| `lead` | REAL | its return over the look-back less its sector's members' mean in the index at the rebalance that bought it, null on a holding bought before the book stored one |

Primary key: `index_code`, `ticker`, `entered_on`.

**The index families write it for each index's book and are its own deleter** (see: The 400 and 600 each sweep two heavyweight designs and keep the stronger after costs). Each night they carry each holding and its size cut by tonight's closes, end a holding whose stock left the index at its last close as a member and one the rule's exit ends, and on the first night of a month buy each sector's leaders within the index the rule buys and do not hold, each with its lead, as the S&P 500's book is kept (see: A heavyweight leaving the index is sold at its last session's close as a member) (see: An S&P 400 or 600 heavyweights holding keeps the lead it was bought on). A night run again deletes what it bought that night, opens again what it ended that night, and writes the night again. By hand, `index-families --leavers` writes again the close and the round trip of a holding sold on leaving the index at a close other than its stock's on the session it was sold. The rows are never deleted otherwise.

### index_rule_trade
Grain: one row per registered rule of an S&P 400's or 600's swing family, stock and session the rule's own list kept a trade on.

| Column | Type | Notes |
|---|---|---|
| `candidate` | TEXT | the registered rule, by the name its registration carries |
| `index_code` | TEXT | the index the rule reads |
| `family` | TEXT | the swing family it is a rule of |
| `ticker` | TEXT | |
| `session_date` | TEXT | the session it was kept on, bought at that close |
| `place` | INTEGER | its place on the rule's own list that night, counted from one |
| `entry` | TEXT | the night's close it was bought at |
| `stop` | TEXT | its stop as the night placed it, the first level of a stop that trails |
| `target` | TEXT | its target, null for a trade whose stop trails |
| `trail` | TEXT | the distance its stop trails under the highest close since the buy, null for a trade with a target |
| `cap` | INTEGER | the sessions it is given, the rule's hold where it states one and its family's own cap otherwise |
| `risk_moves` | REAL | the stop's distance under the buy in the night's typical moves, which its benchmark's plan is placed at |
| `reward_to_risk` | REAL | its target's distance over its stop's, null for a trade whose stop trails |
| `ended_on` | TEXT | the session it ended on, null while it is open |
| `result` | REAL | what it came to in multiples of its risk before its cost, null while open |
| `cost` | REAL | its round trip in multiples of its risk at the published table, its company valued as the member readings read it on its night, which the result after costs subtracts, null while open |
| `benchmark` | REAL | the average result of the same plan entered at the close on every member of the index that night, null until the trade's cap has passed |
| `members` | INTEGER | how many members the benchmark averaged, null until it is written |
| `exit` | INTEGER | from 17.5, the exit of the menu the trade and its benchmark are walked under, by its number, where the rule's registration names one; null for the rule's own exit |

Primary key: `candidate`, `ticker`, `session_date`.

**The index families write it for each registered rule and are its own deleter** (see: A rule of the S&P 400's or 600's swing families is registered as the family on its index and evaluated by their step alone). Each night, for each index, they first walk every trade not yet ended over its stock's closes since and write where it ended, its result and its cost, and once its cap's sessions have passed its benchmark; then each rule standing registered when the night started keeps its members passing tonight in its family's order, five at most, none whose stock it holds a trade on. From 17.5 a rule whose registration states the engines' hooks keeps those meeting its conditions, ordered by its score, over the night's readings of the ledger's catalogue, none on a night handed none, and walks its trades under the exit it names (see: Every engine's settings hooks land together and all default off, so the families' pins move once). A night run again replaces the trades it kept that night and no other night's. The rows are never deleted otherwise: a rule's record on the index's Run page is its trades.

### index_heavyweight_rule_night
Grain: one row per registered sector heavyweights rule of the S&P 400 or 600 and each session its own book rebalanced on.

| Column | Type | Notes |
|---|---|---|
| `candidate` | TEXT | the registered rule, by the name its registration carries |
| `index_code` | TEXT | the index the rule reads |
| `session_date` | TEXT | the session the book rebalanced on |
| `bought` | INTEGER | the stocks it bought at the session's close |
| `sold` | INTEGER | the holdings it sold at the rebalance for no longer being bought |

Primary key: `candidate`, `session_date`.

**The index families write it for each registered heavyweights rule and are its own deleter** (see: A rule of the S&P 400's or 600's sector heavyweights keeps a book of its own in either design, read by the index families' step). A rule's last row is its last rebalance, and a rule holding none rebalances on its first night; a rebalance waiting for what its design reads writes none. A night run again deletes its own row and writes it again.

### index_heavyweight_rule_holding
Grain: one row per registered sector heavyweights rule of the S&P 400 or 600, stock and the session its own book bought it on.

| Column | Type | Notes |
|---|---|---|
| `candidate` | TEXT | the registered rule, by the name its registration carries |
| `index_code` | TEXT | the index the rule reads |
| `ticker` | TEXT | |
| `entered_on` | TEXT | the rebalance session it was bought on, at that close |
| `sector` | TEXT | the sector it was bought in, `none` for a design (b) follower filing none |
| `entry_close` | TEXT | the close it was bought at |
| `growth` | REAL | its close carried over its buy's, the product of each night's close over the close of the session it was last carried to |
| `cut` | TEXT | JSON: each of the size cut it was chosen from, design (a)'s sector's largest companies and design (b)'s sector's members in the index, its ticker, its growth carried the same way and the session it was carried to |
| `through` | TEXT | the session its growth was last carried to |
| `ended_on` | TEXT | the session it was sold on, null while held |
| `exit_close` | TEXT | the close it was sold at, for a holding whose stock left the index its stock's close on the session it was last carried to as the bar table holds it, null while held |
| `reason` | TEXT | `no longer the leader`, `no longer among those it buys`, `a close under its 200-day average` or `left the index`, null while held |
| `result` | REAL | its growth less one at the sale, null while held |
| `cut_return` | REAL | the mean of the size cut's growths less one at the sale, null while held |
| `cost` | REAL | its round trip as a fraction of the buy at the published table, its company valued as the member readings read it on its buy, null while held |
| `lead` | REAL | for a design (a) rule, its return over the rule's look-back less its sector's members' mean in the index at the rebalance that bought it; null for a design (b) rule, which reads no lead over a sector |

Primary key: `candidate`, `ticker`, `entered_on`.

**The index families write it for each registered heavyweights rule and are its own deleter** (see: A rule of the S&P 400's or 600's sector heavyweights keeps a book of its own in either design, read by the index families' step). Each night they carry each rule's holdings and their size cuts by tonight's closes, sell one whose stock left the index at its last close as a member and one closing under its 200-day average where the rule reads that exit, and on the rule's rebalance sell each holding it no longer buys where it sells on that and buy each stock it buys and does not hold, a design (a) rule's each with its lead (see: An S&P 400 or 600 heavyweights holding keeps the lead it was bought on). A night run again deletes what each rule bought that night and opens again what each sold that night. The rows are never deleted otherwise: a rule's record on the index's Run page is its holdings.

### decision_card
Grain: one row per index, night, family and stock the family listed or the index's sector heavyweights' book bought.

| Column | Type | Notes |
|---|---|---|
| `index_code` | TEXT | the index the pick was listed on, `GSPC`, `MID` or `SML` |
| `session_date` | TEXT | the night |
| `family` | TEXT | the family that listed it, by the word it is stored under, `heavyweight` for a book's buy |
| `ticker` | TEXT | |
| `place` | INTEGER | its place down the family's list, or among the book's buys of the night in the order of their sectors |
| `entry` | TEXT | decimal in code, the plan's buy, null where the row the card reads stores none |
| `stop` | TEXT | decimal in code, the plan's stop, null for a rule setting none |
| `target` | TEXT | decimal in code, the plan's target, null for a rule that trails or sets none |
| `rule` | TEXT | the rule the card names, its family and its index in words |
| `settings` | TEXT | JSON: the card's values the night read it with |
| `lines` | TEXT | JSON: the checklist's lines in order, each its place, its name, its verdict, `tick`, `note` or `warning`, and its words |
| `record` | TEXT | JSON: the rule's record as the card read it, its rule in words, trades, share won, mean result and its unit, median sessions held, the sessions by which the card's share of the trades had ended and that share, the median worst close, the history's first and last session and its membership; null where the rule has not been replayed |
| `sector` | TEXT | the stock's sector as its company's newest fetch filed it, which the card's sixth line counts the operator's open trades in; null where none is filed |
| `trail` | TEXT | decimal in code, the distance a trailing rule raises its stop by, the plan's buy less its first stop; null for a rule that does not trail |
| `cap` | INTEGER | the most sessions the rule holds the trade, null for a rule that caps none |
| `round_trip` | TEXT | decimal in code, the round trip a share at the published table, bought and sold at the buy; null where the plan states no buy |
| `book_holdings` | INTEGER | for a book's buy, the most holdings the book can hold, its leaders in each of the eleven sectors, over which a holding with no stop is sized; null for a family's pick |
| `hits` | TEXT | JSON: what could hit the trade, the hold's last session, the stock's stored reactions in typical moves and in the plan's risks with how many passed the stop's distance, the next ex-dividend date inside the hold, declared or estimated, with its payment, and the market events inside the hold with each kind whose table ends first; null on a card written before 16.3 |

Primary key: `index_code`, `session_date`, `family`, `ticker`.

**The decision cards write it in the night after every index's families and books, and are its own deleter** (see: A pick's card advises on the trade and removes no pick, and code computes every figure on it). A night run again deletes its own night's rows and writes them again, and no other night's; nothing else deletes a row, so an earlier night's cards stand as they were read, with the values they were read with.

### rule_record
Grain: one row per index and family a card names on it.

| Column | Type | Notes |
|---|---|---|
| `index_code` | TEXT | `GSPC`, `MID` or `SML` |
| `family` | TEXT | the family, by the word it is stored under |
| `rule` | TEXT | the rule replayed, in words |
| `settings` | TEXT | the one setting replayed, as its sweep keys it |
| `recorded_at` | TEXT | UTC instant of the command that wrote it |
| `first_session` | TEXT | the history's first scored session |
| `last_session` | TEXT | the history's last session |
| `membership` | TEXT | `as it stood` or `survivors only` |
| `unit` | TEXT | `risks` for a rule with a stop, `percent` for one with none |
| `trades` | INTEGER | the trades the replay kept that ended inside the history |
| `won` | REAL | the share that ended at its target, null for a rule that sets no target |
| `average` | REAL | the mean result after each trade's cost, in its unit |
| `median_sessions` | INTEGER | the median sessions held |
| `ended_by` | TEXT | JSON: how many trades ended at each session held, from one, which any share's holding time is read from |
| `worst_close` | REAL | the median of the lowest close each trade was held through against its buy, in its unit, nothing for one that never closed under its buy |

Primary key: `index_code`, `family`.

**The rule recorder writes it by hand, one row an index and family over what an earlier run wrote** (see: A rule's record is replayed at its one setting by the sweep's own code over the pulled history, after costs on every index). It replays each family's live rule on the S&P 500 and its provisional rule on the S&P 400 and 600 at its one setting, and a freeze's remedy runs it again so the row follows the rule a card names. No row is deleted.

### rule_night
Grain: one row per index, night, family and rule standing when the night started, live or variant, or replayed from the pulled history by the record command.

| Column | Type | Notes |
|---|---|---|
| `index_code` | TEXT | `GSPC`, `MID` or `SML` |
| `session_date` | TEXT | the night, or the replayed session |
| `family` | TEXT | the family, by the word it is stored under |
| `rule` | TEXT | the rule's name as the register holds it, or `provisional` for the rule drawing a family's list on an index no freeze has registered one on |
| `evaluated` | INTEGER | 1 where the night evaluated the rule, 0 where its index's part failed |
| `listed` | INTEGER | how many the rule listed on the night, zeros included |
| `gates` | TEXT | JSON: each gate or part of the rule in its order with how many members passed it and every one before; null where the night did not evaluate the rule, for a sector heavyweights rule, and on a replayed row |
| `stretch` | INTEGER | the nights the rule has listed nothing for, counting this one where it listed nothing; null where it was not evaluated and for a sector heavyweights rule |
| `mark` | INTEGER | the stretch the share of its past empty nights had not gone past; null under the floors |
| `flagged` | INTEGER | 1 where the stretch is past the mark |
| `completed` | INTEGER | the stretches completed to the night, a stretch completing on the night a pick ends it |
| `sessions` | INTEGER | the nights evaluated to and including this one, which the mark was counted over |
| `source` | TEXT | `night` for a row the night wrote, `history` for one the record command's replay wrote |

Primary key: `index_code`, `session_date`, `family`, `rule`.

**The rule cards stage writes the night's rows in the swing filter's step after the decision cards, and the record command's `--nights` form writes the history's through the same component, which is its own deleter: a night run again replaces its own rows, and a replay writes over the rule's history** (see: A card's stretch line counts its mark over past empty nights and draws none under thirty completed stretches).

### rule_pick
Grain: one row per index, rule, stock and night a rule whose list no other table keeps kept a pick on, the swing filter's variants.

| Column | Type | Notes |
|---|---|---|
| `index_code` | TEXT | `GSPC` |
| `rule` | TEXT | the variant's name as the register holds it |
| `ticker` | TEXT | |
| `session_date` | TEXT | the night the pick was kept on, bought at that close |
| `family` | TEXT | `pullback` |
| `place` | INTEGER | the pick's place on the variant's own list, counted from one, at most five |
| `entry` | TEXT | decimal in code, the night's close |
| `stop` | TEXT | decimal in code, the stop of the plan the variant reads, the plan clear of the noise or the swing plan at the nearest bands |
| `target` | TEXT | decimal in code, that plan's target, null where it names none |
| `reward_to_risk` | REAL | that plan's, null where it has no target |
| `cap` | INTEGER | the sessions the pick is given, the pullback family's |
| `why` | TEXT | JSON: the figures the swing filter stored for the member on the night, each gate's values by name |
| `ended_on` | TEXT | the session the walk ended it on, at a close through its stop, at its target or at its cap; null while it is open |
| `result` | REAL | what it came to in multiples of its risk; null while open and where the closes ran out before its cap |

Primary key: `index_code`, `rule`, `ticker`, `session_date`.

**The rule cards stage writes and ends them, and is its own deleter: a night run again removes its own picks and clears the ends it wrote at its close, which the walk writes again** (see: A variant's picks are shown on its card when chosen and its results only under its tests).

### forming_row
Grain: one row per index, night, breakout rule and place a member forming a breakout under the rule stands at.

| Column | Type | Notes |
|---|---|---|
| `index_code` | TEXT | `GSPC`, `MID` or `SML` |
| `session_date` | TEXT | the night |
| `rule` | TEXT | the breakout rule's name as the register holds it, or `provisional` |
| `place` | INTEGER | the member's place in the list, the nearest misses first, counted from one, at most the stated rows |
| `ticker` | TEXT | |
| `close` | TEXT | decimal in code, the night's close |
| `high` | TEXT | decimal in code, the highest high of the rule's look-back, the price it would have to close above |
| `moves_under` | REAL | how far the close sits under the high in the member's typical moves |
| `volume_needed` | REAL | the shares the rule would need, its multiple of the 50-session average |
| `volume` | REAL | the night's shares |
| `range_ratio` | REAL | the newer ranges over the older, as the rule's tightening gate reads them |
| `missing` | TEXT | JSON: the gates the member still fails in the rule's order, the new high always among them |
| `next_earnings` | TEXT | the member's next report date where one falls within the stated sessions after the night, null otherwise |
| `forming` | INTEGER | how many members were forming under the rule that night, the rows drawn or more |

Primary key: `index_code`, `session_date`, `rule`, `place`.

**The rule cards stage writes them and is its own deleter: a night run again replaces its own rows** (see: The forming list advises and never lists a stock).

### setup
Grain: one row per index, family, stock and session a family's loose gates passed the stock on.

| Column | Type | Notes |
|---|---|---|
| `index_code` | TEXT | `GSPC`, `MID` or `SML` |
| `family` | TEXT | `pullback`, `breakout`, `drift` or, on the S&P 500, `heavyweight` |
| `ticker` | TEXT | |
| `session_date` | TEXT | the session the setup was read on, the one it is bought at the close of |
| `rule` | TEXT | the words of the live rule's setting the pass beside it was read at: the index's live pullback setting on the extended grid, or the breakout's or the drift's frozen setting |
| `live_pass` | INTEGER | 1 where that setting passes the member-session as the sweep replays it, the market check included, 0 where it does not |
| `picked` | INTEGER | 1 where the night's own list held the stock under the family, 0 where the night listed and did not, null on a row the history build wrote |
| `entry` | TEXT | decimal in code, the close the setup is bought at |
| `stop` | TEXT | decimal in code, the plan's stop |
| `target` | TEXT | decimal in code, the plan's target, null where the stop trails |
| `trail` | TEXT | decimal in code, the distance the stop follows the highest close by, null where it does not |
| `cap` | INTEGER | the sessions the trade is given before its last close ends it |
| `risk_moves` | REAL | the stop's distance under the buy in the member's typical moves |
| `close_over_twenty` | REAL | the first of the forty readings, each as the catalogue in the core defines it and as it stood on the session, null where the inputs do not reach it and never nought: the close over its 20-session average |
| `close_over_fifty` | REAL | the close over its 50-session average |
| `close_over_long` | REAL | the close over its 200-session average |
| `fifty_over_long` | REAL | the 50-session average over the 200-session average |
| `move_share` | REAL | the typical move over the close |
| `rsi` | REAL | the relative strength index on the session |
| `rsi_up` | REAL | 1 where the relative strength index rose on the session, 0 where it did not |
| `volume_ratio` | REAL | the session's volume over the mean of the 50 sessions before it |
| `return_quarter` | REAL | the return over 63 sessions |
| `return_half_year` | REAL | the return over 126 sessions |
| `return_twelve_less_one` | REAL | the return over the 231 sessions ending 21 sessions before |
| `strength` | REAL | the mean of the member's places among the members' returns over 63 and 126 sessions |
| `strength_twelve_less_one` | REAL | the member's place among the members' returns over the 231 sessions ending 21 sessions before |
| `high_ratio` | REAL | the close over the highest high of the 252 sessions ending on the session |
| `since_high` | REAL | the sessions since the highest high of the 20 sessions before |
| `depth` | REAL | the pullback from that high in typical moves |
| `dry_up` | REAL | the volume while it came down over the 50-session average |
| `gap_down` | REAL | the largest gap down inside that pullback in typical moves |
| `rsi_low` | REAL | the lowest relative strength index since that high |
| `tightness` | REAL | the mean daily range of the 20 sessions before over that of the 20 before them |
| `liquidity` | REAL | the base-10 logarithm of the mean of close times volume over the 50 sessions to the session |
| `earnings_sessions` | REAL | the weekdays to the next report on file, the report's day counted |
| `surprise_sessions` | REAL | the sessions since the newest surprise's reaction session |
| `surprise_percent` | REAL | that surprise in per cent |
| `reward_to_risk` | REAL | the plan's target distance over its stop distance |
| `freshness` | REAL | for a pullback the sessions since its trigger first fired, for a drift the sessions since its reaction |
| `band_strength` | REAL | for a pullback the strength of the band holding the close |
| `volume_multiple` | REAL | for a breakout the session's volume over its 50-session average, for a drift the reaction session's |
| `range_ratio` | REAL | for a breakout the mean range of the 20 sessions before over that of the 20 before them |
| `reaction_moves` | REAL | for a drift the reaction session's rise in typical moves |
| `breadth` | REAL | the share of the index's members closing above their 200-session average |
| `highs_less_lows` | REAL | the members at a 252-session high less those at a 252-session low, over the members held |
| `index_over_long` | REAL | the index's own series' close over its 200-session average, the S&P 500 by its index and the 400 and 600 by their funds |
| `vix` | REAL | the VIX's close |
| `vix_change` | REAL | the VIX's close over its close 10 sessions before |
| `mid_over_large` | REAL | IJH's return over SPY's across 63 sessions |
| `small_over_large` | REAL | IJR's return over SPY's across 63 sessions |
| `credit_over_fifty` | REAL | HYG's close over its 50-session average |
| `profit` | REAL | 1 where the four newest quarters filed before the session sum their net income above nothing, 0 otherwise |
| `coverage` | REAL | 1 where those quarters' operating income is at least twice their interest expense or the company is a financial one, 0 otherwise; the last of the forty readings migration 77 created |
| `result` | REAL | what the path came to in multiples of the risk under the plan's exit, null while open; a heavyweights' setup, which holds no stop and whose `stop` is nothing, its close at the end over the buy less one, a fraction of the buy |
| `benchmark` | REAL | the mean of the same plan entered on every member of the index that session, in risks, null until every member's path has ended; a heavyweights' setup's, its sector's size cut's mean return over the same sessions, as a fraction |
| `cost` | REAL | the round trip in risks at the member's own cost on the night, null where the night stored none and on a history row |
| `edge` | REAL | the result less the benchmark, null until both are read |
| `edge_after_cost` | REAL | the edge less the cost, null where either is |
| `sessions` | INTEGER | the sessions held, to the close that ended it or the last held while open |
| `end` | TEXT | `open`, `stop`, `target`, `trail`, `cap`, `rebalance` or `none`, `rebalance` a heavyweights' setup sold at a later rebalance the rule did not buy it at, `none` an anchor placing no trade |
| `ended_on` | TEXT | the session the path ended on, null while open |
| `settled` | INTEGER | 1 once the result and the benchmark are both final, 0 before |
| `source` | TEXT | `night` for a row the night's step wrote, `history` for one the build wrote |
| `pin` | TEXT | the readings' version, the pin of their catalogue and the source that fills them |
| `revenue_growth` | REAL | from migration 78, the first of the five business readings, all from the facts filed before the session: the newest quarter's revenue as first filed over the same quarter's a year before, less one |
| `growth_change` | REAL | that growth less the quarter before's growth on its own year before |
| `gross_margin_change` | REAL | the newest quarter's gross profit over its revenue less the same quarter's a year before |
| `operating_margin_change` | REAL | the newest quarter's operating income over its revenue less the same quarter's a year before |
| `cash_over_income` | REAL | the newest fiscal year's cash from operations over its net income, null where that income is not above nothing |

Primary key: `index_code`, `family`, `ticker`, `session_date`. Indexed on `index_code`, `settled` and `session_date`, which the step closing the windows reads by.

**The five business readings sit last because migration 78 adds them to the table migration 77 created.** The night's step writes them from the facts the store held when it ran, and the filings refresh, after the close, has the ledger read them again for the members it refreshed, so a night's row reads every filing the archive posted before its session (see: The SEC's facts are stored as first filed in a table the night reads, and a setup's business readings read those filed before its session).

**The setup ledger writes it in the night after the families, and by hand over the history, and is its own updater and deleter: a night run again replaces its own rows, the build replaces the span's, and the step closes a window by updating its row once its path ended or its benchmark settled** (see: A setup is every member-session a family's loose gates pass, and its readings are defined once and read as they stood). Every price is TEXT and every reading REAL, because a reading is a statistic and a plan is money.

### setup_night
Grain: one row per index, family and session the ledger read.

| Column | Type | Notes |
|---|---|---|
| `index_code` | TEXT | `GSPC`, `MID` or `SML` |
| `family` | TEXT | `pullback`, `breakout` or `drift` |
| `session_date` | TEXT | the session |
| `members` | INTEGER | the members the gates were read over, those holding a bar on the session with no gap |
| `setups` | INTEGER | how many of them the family's loose gates passed |
| `live_passes` | INTEGER | how many of those the live rule's own setting passes |
| `source` | TEXT | `night` or `history`, as `setup` carries it |

Primary key: `index_code`, `family`, `session_date`.

**The setup ledger writes it beside the setups and is its own deleter** (see: A setup is every member-session a family's loose gates pass, and its readings are defined once and read as they stood). An index the night held no member of on the session has no row.

### ledger_summary
Grain: one row per index, family and year of the setups' sessions.

| Column | Type | Notes |
|---|---|---|
| `index_code` | TEXT | `GSPC`, `MID` or `SML` |
| `family` | TEXT | as `setup` carries it |
| `year` | INTEGER | the calendar year of the setups' sessions |
| `setups` | INTEGER | the setups of that year |
| `live_passes` | INTEGER | how many of them the live rule passes |
| `night_rows` | INTEGER | how many a night wrote, which carry a pick |
| `picked` | INTEGER | how many of those the night's list picked |
| `settled` | INTEGER | how many are settled |
| `result_mean` | REAL | the settled setups' mean result, null where none is settled |
| `edge_mean` | REAL | their mean edge, null where none is settled |
| `result_deciles` | TEXT | the nine cut points between the deciles of their results by the nearest rank, comma-separated in the invariant form, null where none is settled |
| `edge_deciles` | TEXT | the same for their edges |
| `refreshed_at` | TEXT | UTC instant of the refresh that wrote it |

Primary key: `index_code`, `family`, `year`.

**The Ledger page's counts, rewritten whole for an index by the setup ledger after each night and each build**, so the page draws them and computes none (see: A setup is every member-session a family's loose gates pass, and its readings are defined once and read as they stood). The cut points are statistics and are stored as text only because nine of them sit in one cell; each reads back as the double it was.

### loop_run
Grain: one row per tester run on an index.

| Column | Type | Notes |
|---|---|---|
| `run_id` | TEXT | the run, naming the verb, the index and its start |
| `month` | TEXT | the month the run is for, as `yyyy-MM` |
| `index_code` | TEXT | `GSPC`, `MID` or `SML` |
| `through` | TEXT | the newest session the run read, as `yyyy-MM-dd` |
| `started_at` | TEXT | UTC instant |
| `ended_at` | TEXT | UTC instant |
| `folds` | INTEGER | how many test years the run read |

Primary key: `run_id`.

**Written once by the walk-forward tester in the transaction that writes its proposals and test years**, and never updated, so a later run for the same month stands beside it and the Loop page reads the newest (see: A change is adopted only on test years the proposal never saw, and a search is judged as a procedure run year by year).

### loop_proposal
Grain: one row per run, family and proposal tested.

| Column | Type | Notes |
|---|---|---|
| `run_id` | TEXT | the run |
| `index_code` | TEXT | as `loop_run` carries it |
| `family` | TEXT | `pullback`, `breakout`, `drift` or `heavyweight` |
| `proposal` | TEXT | the procedure, in words |
| `words` | TEXT | the change the procedure makes run on all finished data, in words; null where it chose no setting |
| `current_words` | TEXT | the rule today, in words |
| `unit` | TEXT | `risks` for a swing family, `points` for a book |
| `units` | INTEGER | the proposal's trades, or the book's months either side held, over the test years |
| `blocks` | INTEGER | the blocks of 63 sessions the gate read |
| `adjusted` | REAL | the step-down's adjusted p-value; null where the blocks are under the floor |
| `gate` | INTEGER | 1 where the adjusted p-value is at or under the bar |
| `stable` | INTEGER | 1 where the stability screen held |
| `counted` | INTEGER | the complete test years the screen counted |
| `better` | INTEGER | of those, the years the proposal's total stood above the rule's |
| `trimmed` | REAL | the proposal's total less the rule's with the five largest results left out of each side; null where neither side holds one |
| `counts` | INTEGER | 1 where the units reach the count the family is judged on |
| `detectable` | REAL | the difference a unit the gate detects four times in five at the bar; null under two blocks |
| `stable_folds` | INTEGER | the folds choosing within a grid step of the proposal |
| `passed` | INTEGER | 1 where all four parts held and the proposal names a change |
| `finding` | TEXT | from 17.5, an exit proposal's finding in words: the autopsy's figures where it read the rule's trades, and the exit's edge on the years it learned on against the rule's; null for any other proposal and one naming no change |

Primary key: `run_id`, `index_code`, `family`, `proposal`.

**Written once by the walk-forward tester with its run.** Every figure is a statistic and stored as the double it is (see: A proposal passes the tester on a block sign-flip test of its total edge after costs against the current rule, corrected within a run and held to a fixed bar across runs).

### loop_test
Grain: one row per run, proposal and test year.

| Column | Type | Notes |
|---|---|---|
| `run_id` | TEXT | the run |
| `index_code` | TEXT | as `loop_run` carries it |
| `family` | TEXT | as `loop_proposal` carries it |
| `proposal` | TEXT | as `loop_proposal` carries it |
| `year` | INTEGER | the test year |
| `complete` | INTEGER | 1 where a later year holds the newest session |
| `chosen` | TEXT | the setting the fold chose on what ended before the year began, in words; null where none met the floors |
| `current_units` | INTEGER | the rule's trades, or the book's months, entered in the year with a result |
| `proposed_units` | INTEGER | the same for the setting chosen, or the rule's where none was |
| `current_total` | REAL | the rule's total edge after costs over them |
| `proposed_total` | REAL | the setting chosen's |

Primary key: `run_id`, `index_code`, `family`, `proposal`, `year`.

**Written once by the walk-forward tester with its run**, so the Loop page's test years read a fold's choice as it was made (see: A change is adopted only on test years the proposal never saw, and a search is judged as a procedure run year by year).

### loop_finding
Grain: one row per run, family and figure of the trade autopsy.

| Column | Type | Notes |
|---|---|---|
| `run_id` | TEXT | the run |
| `index_code` | TEXT | as `loop_run` carries it |
| `family` | TEXT | `pullback`, `breakout` or `drift` |
| `figure` | TEXT | `best`, `worst`, `sessions to best`, `stopped once a risk up` or `target hits' worst` |
| `value` | REAL | the median in risks or sessions, or the share of the stop's trades; null where no trade is read |
| `trades` | INTEGER | the finished trades the figure was read over |
| `words` | TEXT | the figure in words, as the Loop page draws it |

Primary key: `run_id`, `index_code`, `family`, `figure`.

**Written once by the walk-forward tester with its run**, from 17.5, read off the rule's own finished trades over the whole history; context for the exits the autopsy proposes and never a test of them (see: The trade autopsy proposes exits of a fixed menu, each tested as the procedure that chose it).

### filed_fact
Grain: one row per filer, concept and period, as first filed.

| Column | Type | Notes |
|---|---|---|
| `cik` | TEXT | the filer's CIK padded to ten digits |
| `concept` | TEXT | the archive's concept the figure was filed under, one of the six measures' |
| `period_start` | TEXT | the first day of the period, a quarter, nine months or a year |
| `period_end` | TEXT | the last day of the period, on or after 2015-01-01 |
| `dollars` | TEXT | decimal in code, the figure as the first filing stating the period states it |
| `filed` | TEXT | the day that filing was made |
| `form` | TEXT | its form, as the archive names it |
| `accession` | TEXT | its accession number |
| `run_id` | TEXT | the refresh that stored the row |

Primary key: `cik`, `concept`, `period_start`, `period_end`.

**The SEC's facts as first filed, written by the filings refresh and read by the night's ledger** (see: The SEC's facts are stored as first filed in a table the night reads, and a setup's business readings read those filed before its session). The night's refresh and the whole refresh by hand insert a period's figure only where none is stored, so a later filing stating it again changes nothing, and nothing updates or deletes a row.

### filed_fact_pull
Grain: one row per filer each time the refresh asked for its facts.

| Column | Type | Notes |
|---|---|---|
| `cik` | TEXT | the filer's CIK padded to ten digits |
| `pulled_at` | TEXT | UTC instant of the ask |
| `run_id` | TEXT | the refresh that asked |
| `accession` | TEXT | the filing that set the night's ask off, null for the whole refresh |
| `stored` | INTEGER | the facts the answer added that were not stored before |

Primary key: `cik`, `pulled_at`.

**What each refresh asked the archive for and what it added**, append-only, which is how a member's facts are known to have been asked after a filing.

### filing_day
Grain: one row per day of the archive's daily index the refresh read.

| Column | Type | Notes |
|---|---|---|
| `day` | TEXT | the day the index covers |
| `run_id` | TEXT | the refresh that read it |
| `read_at` | TEXT | UTC instant of the read |
| `posted` | INTEGER | 1 where the archive posted an index for the day, 0 where it holds none, a weekday it was closed |
| `filings` | INTEGER | the filings the index lists |
| `members` | INTEGER | the member filers whose filing set a refresh off |
| `refreshed` | INTEGER | how many of them were refreshed |

Primary key: `day`.

**The days the refresh has read, the newest of which it starts the next night's read after** (see: The night refreshes the facts of the members that filed since its last read of the archive's daily index, after the close under its own limit). A day is written only once every member it set off was refreshed, so a refusal or the step's limit leaves the day to be read again; the night's own session, not yet posted when the night runs, is never written by that night.

### taken_trade
Grain: one row per trade the operator took from a pick's card.

| Column | Type | Notes |
|---|---|---|
| `ticker` | TEXT | |
| `taken_at` | TEXT | UTC instant of the Taken press |
| `index_code` | TEXT | the index of the card it was taken from, `GSPC`, `MID` or `SML` |
| `family` | TEXT | the family of that card, by the word it is stored under |
| `night` | TEXT | the night of that card |
| `sector` | TEXT | the stock's sector as the card stored it, which a card's sixth line counts open trades in; null where none was filed |
| `fill` | TEXT | decimal in code, the price the operator entered, or the plan's buy until the next session's open replaces it |
| `fill_date` | TEXT | the session the fill is for, the session after the card's night unless the operator entered another |
| `provisional` | INTEGER | 1 while the fill is the plan's buy awaiting the next session's open, 0 once it is an entered price or the open |
| `entered` | INTEGER | 1 where the operator entered the price, which is never replaced |
| `stop` | TEXT | decimal in code, the card's stop, null for a rule setting none |
| `target` | TEXT | decimal in code, the card's target, null for a rule that trails or sets none |
| `trail` | TEXT | decimal in code, the card's trail, null for a rule that does not trail |
| `cap` | INTEGER | the card's cap in sessions, null for a rule that caps none |
| `exit_price` | TEXT | decimal in code, the price of an exit the operator recorded, null while none is |
| `exit_date` | TEXT | the session of that exit |
| `followed_through` | TEXT | the newest night that followed the trade, null until one has |
| `ended_on` | TEXT | the session the night's follower found the trade ended on, null while it stands |
| `end_price` | TEXT | decimal in code, the price it ended at as the stock traded that day: the close its rule sold at, the book's sale, the operator's exit or its last close as a member |
| `end_reason` | TEXT | `stop`, `target`, `cap`, `sold`, `exit` or `left` |
| `result` | REAL | its result from the fill, in multiples of its risk, or in per cent of the fill for a holding with no stop; null while it stands |

Primary key: `ticker`, `taken_at`.

**The read surface writes it on the card's presses, and the night's follower follows it** (see: A taken trade's fill is the next session's open once its bar is stored, and the plan's buy marked provisional until then). A Taken press inserts a row from the card the store holds, refused for a stock already holding an open trade, a fill at or under the stop and a card stating no buy; a Not taken press deletes a row only while no night has followed it and no exit is recorded; and an exit press records the exit on an open row. The follower writes each open row's follow each night, the provisional fill replaced by its session's stored open, and where the trade ended. No row holds the account's size, its risk or its cap (see: The account settings live in a file of their own under the data root and in nothing the store or the logs hold).

Declared column sets, stated per operation because that is the grain the rule is written at: ReadApi updates `exit_price` and `exit_date` when the operator records an exit; TakenFollower updates `fill`, `provisional`, `followed_through`, `ended_on`, `end_price`, `end_reason` and `result` when it follows a trade. No column is written by both in one operation, which is what permits the split.

### taken_record
Grain: one row per index and family the operator has taken a trade under.

| Column | Type | Notes |
|---|---|---|
| `index_code` | TEXT | `GSPC`, `MID` or `SML` |
| `family` | TEXT | by the word it is stored under |
| `unit` | TEXT | `risks`, or `percent` for the sector heavyweights |
| `won` | INTEGER | the trades ended at their target, none for a rule setting no target |
| `lost` | INTEGER | the trades ended at their stop, none for a rule setting no target |
| `ended` | INTEGER | every trade ended, however |
| `open_trades` | INTEGER | the trades still open |
| `average` | REAL | the mean result over the ended, in the unit, once twenty have ended; null before |
| `same_nights` | INTEGER | the nights the operator took a trade of the family on the index |
| `rule_listed` | INTEGER | the rule's own picks listed on those nights, each a card the night stored |
| `rule_won` | INTEGER | those picks ended at their target, followed from the plan's buy; none for a rule setting no target |
| `rule_lost` | INTEGER | those picks ended at their stop; none for a rule setting no target |
| `rule_ended` | INTEGER | those picks ended, however |
| `rule_average` | REAL | the mean result over those ended, in the unit, once twenty have ended; null before |
| `night` | TEXT | the night the follower wrote it |

Primary key: `index_code`, `family`.

**The night's follower writes it whole each night over every trade the operator took, and nothing that makes a pick reads it** (see: The operator's own record states its average result once twenty of its trades in a family and index have ended). Beside the operator's counts it writes the same counts over the rule's own picks listed on the nights the operator took one, each followed under the same management from the plan's buy as its night traded, so the two rows differ by the operator's choices and fills and not by the market of those weeks.

### dividend_reading
Grain: one row per member and fetch the quarters fetch stored, where the answer files a dividend part.

| Column | Type | Notes |
|---|---|---|
| `ticker` | TEXT | |
| `fetched_at` | TEXT | UTC instant of the fetch, the same as its quarters' |
| `forward_rate` | TEXT | decimal in code, the forward annual rate a share as the provider files it |
| `last_ex_date` | TEXT | the last declared ex-dividend date the answer files, null where none |
| `by_year` | TEXT | JSON: each year's count of dividends, oldest first, empty for a company paying none |

Primary key: `ticker`, `fetched_at`.

**The quarters fetch keeps the dividend part of the answer it already asks for, at no further request** (see: A pick's next ex-dividend date is the calendar's where it declares one and the last declared date plus the usual interval where it does not). No row is deleted or updated.

### sweep_answer
Grain: one row per sweep run recorded.

| Column | Type | Notes |
|---|---|---|
| `run` | TEXT | the run's folder name under the sweep's folder, the instant it started |
| `index_code` | TEXT | the index the run read, `GSPC`, `MID` or `SML` |
| `family` | TEXT | the family as the cards name it, by the word it is stored under |
| `design` | TEXT | the design where the family sweeps more than one, `a` or `b` for an index's sector heavyweights; null otherwise |
| `answer` | TEXT | `passed` where a setting the run read met the floors, its proposal or a setting its second stage crossed, and `none passed` otherwise |
| `recorded_at` | TEXT | UTC instant of the command that recorded it |

Primary key: `run`.

**The sweep answers command writes it, by hand after a sweep run, and nothing deletes it** (see: No family on any index is set aside or hidden by a test result without the operator's word). The command reads the answer the run states in its own folder, written by the sweep's own code where its report says whether a setting passed, and decides nothing itself: a run stating no answer, being one made before runs stated one or the pullback's base, which searches nothing, is refused with nothing written. Recording a run again writes its row again, which is the update. A family's card draws its one line, "Its sweep found no setting that passed the floors", where the newest answer recorded for each of the family's designs on its index says none passed and no live rule of the family was registered after the newest of them. The rows are kept: each is what the operator was told of a sweep, and a row recorded for a run whose folder was later removed still says what that run found.

### forward_return
Grain: one row per listing per horizon, and one per swing filter row carrying a plan per swing horizon.

| Column | Type | Notes |
|---|---|---|
| `ticker` | TEXT | |
| `session_date` | TEXT | the listing's date, which a swing filter row shares |
| `horizon` | TEXT | `5`, `21` or `setup` for a listing; `swing` or `swing-20` for a swing filter row's plan at the nearest bands and `clear` or `clear-20` for its section 10 plan, each over the setup's cap and over twenty sessions as context (see: A swing filter row carries both swing plans, each scored from the night's close, and a candidate's setups are scored on the plan its own trade gate reads); `breakout` for a trade the breakout family passed, over its own cap (see: A breakout is a close above the year's high on heavy volume after its ranges narrowed, sold on a trailing stop with no target); `drift` for one the earnings drift family passed, over its own (see: The earnings drift buys a beat with a strong reaction within five sessions, stopped under the reaction session's low) |
| `outcome` | TEXT | `win`, `loss`, `unresolved`, `never entered`, `trailed`, or null while immature. `trailed` is a trailing trade's alone, sold at a close under its stop, and such a trade ended by its cap is `unresolved`; it is never `win` or `loss`, having no target. `never entered` is the setup horizon's alone: the price never closed at or below the entry zone's top edge, so there was no purchase to score (see: A setup is scored from its entry, and a target reached before the entry is never a win) |
| `resolved_on` | TEXT | date: the session a horizon matured or a setup resolved on, the cap's own session for a setup timed out, and null while immature |
| `return_pct` | REAL | for the two session horizons, the move from the listing's close; for the `setup` horizon, the move from the close the setup was entered at, and null where nothing was entered or the entry and the stop fell on one session (see: A setup is scored from its entry, and a target reached before the entry is never a win) |
| `base_rate` | REAL | the universe figure for this horizon, over every name-night in the window rather than over the listed ones, and null for the `setup` horizon |
| `break_even` | REAL | the share of the time this plan had to be right to come out even, as a percentage, computed from the close the setup was entered at, its stop and its first traded target. The `setup` horizon's alone, and null on the two session horizons, which ask what the market did rather than what a plan demanded (see: A stored break-even is measured from the close the setup was entered at, as a percentage beside the figures it is compared with) |
| `null_win` | REAL | the share of the time a plan with no edge would have reached this one's target before its stop, simulated from the fill it was scored from under the name's trailing volatility with the session cap and the round trip the calibration carries. The `setup` horizon's alone, on a resolved row alone, and null on a row decided before the column existed (see: A setup's null win probability is calibrated from its own plan, and its planned break-even is shown beside it) |
| `null_win_at_sensitivity` | REAL | the same bar at the round trip shown as a sensitivity beside the one the calibration carries (see: The calibrated null carries a round trip of ten basis points, and thirty is shown as a sensitivity) |
| `planned_risk` | REAL | what the plan put at risk from the fill, as a percentage of it, which is what a realized loss is stated in multiples of, and what a trailing trade's result is divided by to be read in multiples of its risk |
| `on_earnings` | INTEGER | 1 where the session the setup resolved on was one the name reported on, 0 where it was not, and null where nothing resolved. Stored rather than asked for when a record is read, because the calendar holds a year and a candidate's record is read over four |

Primary key: `ticker`, `session_date`, `horizon`.

**`break_even` and `return_pct` are present together on the `setup` horizon and absent together.** Both are measured from the close the setup was entered at, so a setup nobody entered has neither and a setup that entered and stopped on one session has neither, the store not saying where in the zone that fill sat. Five of the 93 setups the operator's store had resolved on 2026-09-16 were that shape, which is why the figures computed over these rows state the population they were computed over rather than counting every resolved row. An entry is admitted only inside the plan's own range, from its stop to the lower of its entry zone's top edge and its target, so no entry sits where the share is undefined (see: An entry is admitted only inside the plan's own range, and a plan with no entry zone takes its whole range as its zone).

**The four columns beside the two figures are written on the night a setup resolves and are not recomputed.** The bar a setup with no edge would have cleared is simulated under the name's trailing volatility, and whether the session it resolved on was one the name reported on is read off the calendar. Bars are kept for a year and the calendar dates a print for a year, while a candidate's record is read over about four years of nights, so a figure recomputed when the record is read would be a figure that stopped being computable. A row decided before these columns existed carries none of them, and a record counts no setup without a bar rather than counting it against the bar its plan stated.

**A row is written until its outcome is decided and never after** (see: An outcome once decided is never rewritten, and a setup still in play is scored with its plan scaled by its listing session's adjustment factor). The filler writes a row while its `outcome` is null and leaves it once it is not, so a corporate action refetch restating the bars a decided row was scored on, and the year's retention dropping them, change nothing stored. `base_rate` is the one column written over a decided row, being the universe's figure as the window fills. A setup still in play is scored with its plan's stop, first traded target and entry zone's top edge each multiplied by the listing session's `close` over its `raw_close`, a factor of one on the night the listing is written.

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

**"or was opened" is dropped, because nothing records an open.** No table in this file holds that a name was read, so half the stated rule could not be implemented and the cell would have promised a behaviour no test could induce. What stands is the half the store can answer: a night the name fired is kept whole, and every other night keeps the hash. That gives `ChangeDetector` a listings read, which its matrix row carries, and the ordering is already right, since the night writes the shortlist before the facts file.

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

`payload` holds the quarter's figures, the balance sheet, the margin computed from that filing's own revenue and gross profit, and the quarter's growth on the same quarter a year before and on the quarter before it, computed from the earlier filings returned with it (see: A quarter's growth is computed on its own row from the filings the provider returned), and on the newest filing's row the company's identifier at the filings archive, from 6.8, which a research pass reads the company's own release by. The earnings bases, the valuation on each of them, and the next print's consensus estimate sit on the newest filing's row alone, because a ratio has a price in it and a price moves every session, and every fetch stores them again as its own copy in `fundamentals_snapshot`, which readers take over the row's.

**Two providers fill one row and `source` says which filled what, part by part.** The company financials endpoint supplies eleven parts, the analysts' `ratings` and the `dividend` among them, and the filings archive five: `segments`, `revenueTables`, `tableGrowth`, `guidance` and `facts`, which that endpoint files for no name at all. The archive's five sit on the newest filing's row alone for the reason the ratios do and one of its own: a segment table is read from one filing's report page and the guidance from one announcement's exhibit, so writing either onto a historical row would state that an older quarter's segments were this quarter's, and deriving them per filing would cost a request per row for figures nothing reads. A seventeenth part, `periodEnd`, is the quarter's end and comes from either provider on any row: the company financials endpoint labels a quarter with the last day of its month, and the archive's index states the period each 10-Q and 10-K covers, so a row takes the period of the periodic report ending within a week of that label and keeps the label only where the archive was not read or indexes no such report, and `source` names whose date it is (see: A quarter ends on the date the company's own filing states).

`source` distinguishes three reasons a part can be empty and they are not the same morning: the provider files it for nobody, the archive answered and served none, and the archive was not read. A column that could not tell the third from the first would report a company with no segments after a failed fetch. `segments` holds the report the figures were read from, the scale the table stated, its period columns and its groups in the order the table states them; `revenueTables` holds the filing's other tables of revenue by a grouping, by market, product or region, each in that shape (see: The filing's other tables of revenue by a grouping are kept beside its segment table); `tableGrowth` holds each of those tables' groups' change on the same months a year before, computed from the columns the table states (see: A group's growth in a filing's own tables is computed from the columns the table states); `guidance` holds management's own passage with the exhibit and the date it was filed on, and never a figure struck from it (see: Guidance is stored as management's own prose, and the facts file carries each figure the passage states as the claim checker reads it); `ratings` holds the analysts' mean rating on a scale of one to five, their mean target price and how many rate the name at each of five grades from a strong buy to a strong sell, as the provider files them (see: The fundamentals row carries the analysts' ratings the provider files, on the newest filing alone); `dividend` holds, on the newest filing's row alone for the reason the ratios do, the forward annual rate a share, the forward yield and the payout ratio as the provider states them, and the ex-dividend and pay dates, as the provider files them and a company paying none filing a rate of zero and no dates (see: The numbers section draws the dividend the provider files from the newest filing alone, and nothing for a company paying none); `facts` holds the archive's own filed figures under the concept each was filed against, over the same twelve quarters the filings are stored over, for a named set of concepts rather than every concept the archive holds.

### fundamentals_snapshot
Grain: one row per ticker per fetch.

| Column | Type | Notes |
|---|---|---|
| `ticker` | TEXT | |
| `fetched_at` | TEXT | UTC instant of the fetch |
| `payload` | TEXT | JSON: the earnings bases, the valuation, the market value, the ratings, the dividend and the next report as that fetch stated them |

Primary key: `ticker`, `fetched_at`.

Kept forever, never updated. Every fetch the fundamentals fetcher makes writes one, whether or not it found a filing the store did not hold, in the same transaction as the filings it wrote. The six parts are the ones a `fundamentals` row carries on the newest filing alone because they are as of the fetch rather than as of a filing, and they are built by the same code for both, so a copy and the newest row written by one fetch hold the same values. A `fundamentals` row is never updated, so without this table a later fetch finding no new filing would have nowhere to put a price that has moved. The facts assembler reads the newest copy over the newest filing's own parts, and the read surface does the same for the newest copy fetched on or before the night a page is about, so an earlier night's page draws what the store held that night (see: A regenerated report is written whole by the paid model from the company's figures as they stand on the day it runs, once a name a day).

### reported_quarter
Grain: one row per ticker per fetch per quarter.

| Column | Type | Notes |
|---|---|---|
| `ticker` | TEXT | |
| `fetched_at` | TEXT | UTC instant of the fetch |
| `session_date` | TEXT | the night's session the fetch was made on |
| `period_end` | TEXT | date the quarter ends, as the provider labels it |
| `filing_date` | TEXT | date the figures were filed, null where the provider files none |
| `report_date` | TEXT | date the quarter was reported, from the provider's earnings history |
| `revenue`, `operating_income`, `net_income`, `operating_cash_flow` | TEXT | decimal in code, as the provider files them |
| `eps_actual`, `eps_estimate` | TEXT | decimal in code, earnings a share and the analysts' estimate, on this fetch's per-share basis |
| `eps_trailing` | TEXT | decimal in code, the actual earnings a share of this quarter and the three before it summed, worked out at the fetch |
| `sales_growth` | TEXT | decimal in code, revenue against the same quarter a year earlier, worked out at the fetch |
| `sales_growth_before` | TEXT | decimal in code, the year-earlier quarter's own growth on the year before it |
| `operating_margin` | TEXT | decimal in code, operating income over revenue |
| `margin_year_earlier` | TEXT | decimal in code, the year-earlier quarter's operating margin |
| `close_after` | TEXT | decimal in code, the close on the first session after the report, from the closes this fetch asked for |
| `close_after_session` | TEXT | the session that close is on |
| `basis_session` | TEXT | the newest session of the closes this fetch asked for |
| `basis_close` | TEXT | decimal in code, that session's close, which tonight's close is brought to this fetch's basis by |
| `shares` | TEXT | decimal in code, the shares outstanding the quarter's balance sheet files, on this fetch's split basis, null where it files none and on every row a fetch stored before the column existed |
| `interest_expense` | TEXT | from 15.2, decimal in code, the quarter's interest expense as the income statement files it, with the sign the provider files it under, null where it files none |
| `interest_read` | INTEGER | from 15.2, 1 where the fetch that stored the row read the income statement's interest expense, filed or not, and 0 on every row stored before, whose null says nothing about what was filed |

Primary key: `ticker`, `fetched_at`, `period_end`.

Kept forever, never updated and never deleted. One fetch writes the twelve newest quarters it returns, whether or not an earlier fetch stored some of them, so a reading reads one fetch's rows and never mixes two (see: Reported quarters are stored per fetch, so every quarter a reading reads shares one fetch's per-share basis). The provider restates earnings a share after a split and adjusts its closes as of the day it is asked, so a quarter's earnings and the close after its report are on one basis only beside the other quarters the same fetch returned. The growths, the margins, the trailing earnings and the close after each report are worked out at the fetch because they read quarters and closes older than the twelve kept: the twelfth quarter's trailing earnings need three quarters before it, and a close three years back is held by no bar the store keeps. About four fetches a member a year write about 25,000 rows a year.

This table is apart from `fundamentals` and the report pass's own fetch, which are unchanged. The rows `fundamentals` holds carry no operating income and no operating cash flow and are never updated, so they could never gain the two, and writing that table nightly would read the filings archive on the night after a release, often before the quarterly report exists, and change every member's facts file.

From 14.3 each row carries the shares its quarter's balance sheet files, read from the answer the fetch already asked for, and the sector heavyweights value a member from the newest fetch carrying any: the newest count filed before the session times tonight's close brought to the count's basis by `basis_close` over the store's close on `basis_session` tonight (see: The night values a member from its newest fetch, tonight's close brought to the count's basis by the fetch's own close). The provider restates its counts to its split basis as of the day it is asked, which is the fetch's, so the counts a fetch stored share that basis with its closes.

### quarter_ask
Grain: one row per ticker per night asked.

| Column | Type | Notes |
|---|---|---|
| `ticker` | TEXT | |
| `session_date` | TEXT | the night's session the ask was made on |
| `asked_at` | TEXT | UTC instant |
| `reason` | TEXT | `fill`, `joined`, `report` or `waiting`: the start's fill, a member's first night in the index, the first night after a report, or an ask again for a quarter not yet in the answer |
| `awaited` | TEXT | the period end of the quarter asked for, null where any quarter would do |
| `outcome` | TEXT | `stored`, `not yet posted`, `nothing returned` or `refused` |
| `quarters` | INTEGER | the quarter rows the ask stored |
| `weighted` | INTEGER | the weighted calls the ask spent: 10 for the fundamentals, and 1 for the closes where the answer was stored |
| `nights` | INTEGER | the nights the member has been asked on for this quarter, this one included |
| `next_ask` | TEXT | where the ask stored nothing, the session the member is next asked on once its asks after the first pass the retries, and null where that is the next night |
| `detail` | TEXT | what the provider said where the ask stored nothing |

Primary key: `ticker`, `session_date`.

Kept forever, never updated and never deleted. It is the record the schedule reads: a member is asked on the first night after its report and, where the answer does not yet carry the quarter, on each of the five nights after and weekly after that, until the quarter appears or the next report date passes (see: A member's reported quarters are fetched on the night after it reports, and asked for again on the five nights after and weekly after that until the quarter is posted). A night run again for its session keeps the ask it made, so the insert ignores the conflict rather than asking twice. The run page reads it for who was asked tonight and why, who is still waiting and when each is asked next, and how far the fill has come.

### company
Grain: one row per ticker per storing fetch.

| Column | Type | Notes |
|---|---|---|
| `ticker` | TEXT | |
| `fetched_at` | TEXT | UTC instant of the fetch, the one its `reported_quarter` rows carry |
| `cik` | TEXT | the company's filer at the filings archive, ten digits with the leading zeros, null where the provider files none |
| `sector` | TEXT | the GICS sector the provider files, null where it files none |
| `industry_group` | TEXT | the GICS industry group, null where none is filed |
| `industry` | TEXT | the GICS industry, null where none is filed |
| `sub_industry` | TEXT | the GICS sub-industry, null where none is filed |
| `strong_buy`, `buy`, `hold`, `sell`, `strong_sell` | INTEGER | from 15.2, the analysts' five rating counts the same answer files, null where it files none and on every row stored before |

Primary key: `ticker`, `fetched_at`.

Kept forever, never updated and never deleted. Each fetch the quarters step stores writes one row in the same write as its quarters, from the answer it already asked for and with no request of its own, and `quarters --companies` asks every member no fetch has stored one for, once, so the sector heavyweights read every member from their first night. The heavyweights read each member's newest row: the CIK ranks a company once whatever classes it lists (see: Companies are ranked by CIK with one listing held, the class that traded the more dollars over fifty sessions), and the sector places it, with the fourteen moves of 2023-03-17 read by date (see: A company's sector on a session is the GICS sector the provider files, with the fourteen moves of 2023-03-17 read by date). The provider files the classification as of its last update with no dated history, which is why the moves are a committed table, and `membership.sector` is the index snapshot's own scheme and is not read for it.

### fundamental_reading
Grain: one row per ticker per night.

| Column | Type | Notes |
|---|---|---|
| `ticker` | TEXT | |
| `session_date` | TEXT | the night's session |
| `state` | TEXT | `improving`, `steady`, `deteriorating`, `not enough quarters` or `no fundamentals yet` |
| `read_from` | TEXT | the period end of the newest quarter the readings were read from, null where none is stored |
| `fetched_at` | TEXT | the fetch the quarters were read from |
| `awaited` | TEXT | the period end of a newer quarter the calendar says has been reported and the store does not yet hold |
| `readings` | TEXT | JSON: the four readings, each with the quarters it was read over, its figures as text and what was absent where it could not be read |

Primary key: `ticker`, `session_date`.

Kept forever: Past picks draws the state a trade carried on its listing night, and the order a night's list was drawn in is read from that night's rows. A night reads only the quarters fetched on the nights before it, so a night run again later never reads a quarter from its future. The reader's delete removes one night's set, the night it is writing, so a night run again replaces its own rows whole and a member no longer in the index keeps none on it (see: Four readings of a member's reported quarters are worked out every night by rules the measured split settled, and its state is read from sales and operating margin alone).

### member_reading
Grain: one row per index, session and member of the S&P 500, 400 and 600 on the night.

| Column | Type | Notes |
|---|---|---|
| `index_code` | TEXT | the index the member was read in, `GSPC`, `MID` or `SML` |
| `session_date` | TEXT | the night's session |
| `ticker` | TEXT | |
| `close` | TEXT | decimal in code, the session's close as it traded, null where the member holds no bar on it |
| `dollar_volume` | TEXT | decimal in code, the mean of close times volume over the 50 sessions to it, null under 50 |
| `company_value` | TEXT | decimal in code, the company's value on the session from the count filed before it, null where none was filed |
| `cost` | REAL | the round trip a trade bought and sold at the close pays, in per cent of it, at the published table's value |
| `cost_double` | REAL | the same at double the table |
| `profit` | INTEGER | 1 where the four newest quarters filed before the session sum their net income above nothing, 0 otherwise |
| `coverage` | INTEGER | 1 where the same four hold operating income at least twice their interest expense or the company is a financial one, 0 where they do not, null where a quarter among them was stored before interest expense was read |
| `state` | TEXT | the state the night's fundamental reading gave the member, null where it read none |
| `year_high` | TEXT | decimal in code, the highest high of the 251 sessions before the session, null on a member's first bar |
| `nearness` | REAL | the close over that high, as stored |
| `since_high` | INTEGER | the sessions since that high was made |
| `volume_ratio` | REAL | the session's volume over its 50-session average as the indicators store it |
| `industry` | TEXT | the GICS industry the member's newest company fetch files, null where none is filed |
| `industry_month` | REAL | the industry's S&P 500 members' return over the 21 sessions to the session, each weighted by its value at the window's start, null where the industry holds no S&P 500 member read |
| `industry_quarter` | REAL | the same over 63 sessions |
| `peer_surprise` | REAL | the mean surprise in per cent of the industry's S&P 500 members reporting in the 20 sessions before the session, each weighted by its value on the session before it, null where none reported |

Primary key: `index_code`, `session_date`, `ticker`.

**The member reader writes it in the night after the fundamental readings, and is its own deleter** (see: A 400 or 600 trade pays the published effective spread for its size and price, and its pass tests read the edge after it). A night run again for a session replaces that session's rows and no other's. Every reading is the one function the sweeps read it with, so a figure the page shows on a night is the one a sweep would have read there. Kept forever.

### switch_reading
Grain: one row per session the night read.

| Column | Type | Notes |
|---|---|---|
| `session_date` | TEXT | the night's session |
| `ijh_half_year` | REAL | IJH's adjusted close over SPY's on the session, over the same 126 sessions before, null where a close is missing |
| `ijh_year` | REAL | the same over 252 sessions |
| `ijr_half_year` | REAL | IJR's, over 126 sessions |
| `ijr_year` | REAL | IJR's, over 252 sessions |
| `hyg_average` | REAL | HYG's adjusted close over its mean over the 50 sessions to it |
| `hyg_change` | REAL | HYG's adjusted close over its close 63 sessions before |

Primary key: `session_date`.

**The member reader writes it with the members' readings and is its own deleter.** A switch reads open where its reading is above one, as the sweeps read it. The closes are aligned on the funds' own sessions in `market_bar` and not the members' bars, so a year's window is read once the funds hold 253 sessions (see: The night's switches are aligned on the funds' own sessions, so a year's window is read from closes the members' year of bars never reaches). Kept forever.

### estimate_reading
Grain: one row per ticker per night the night asked for its estimates.

| Column | Type | Notes |
|---|---|---|
| `ticker` | TEXT | a member a standing rule reading analysts' estimates passed on everything else |
| `session_date` | TEXT | the night's session |
| `year_end` | TEXT | the end of the current fiscal year the answer files the estimate for, null where it files none |
| `current_estimate` | TEXT | decimal in code, the consensus estimate of the year's earnings a share as the answer files it, null where it files none |
| `days_ago_estimate` | TEXT | decimal in code, the same estimate 30 days before, null where the answer files none |
| `not_read` | TEXT | why the answer reads no estimate, null where it reads both |
| `run_id` | TEXT | the run id of the night that asked |

Primary key: `ticker`, `session_date`.

Kept forever, never updated and never deleted, the only history of the estimates the revisions variant read, since the provider keeps none (see: The night asks for the estimates of each member a rule reading them passes on everything else, once a member a night). The estimates fetcher writes a row for each member it asks the provider for, in the swing filter's step before any verdict is written, and a night run again reads its row back and asks nothing; a member the provider does not serve stores nothing, so a run of the rest of the night asks for it again. The swing filter hands each reading to the verdicts of the rules reading it, which read it as raised where the current estimate stands above the one 30 days before (see: A member's estimates are raised where its current fiscal year's consensus earnings estimate stands above its level 30 days before).

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

### news_article
Grain: one row per ticker per article, the article being one the night's one news query brought back naming the member.

| Column | Type | Notes |
|---|---|---|
| `ticker` | TEXT | |
| `article_id` | TEXT | the first sixteen hex characters of a hash of the link |
| `link` | TEXT | |
| `title` | TEXT | |
| `source` | TEXT | the channel that delivered it, as the feed names it |
| `published_at` | TEXT | UTC instant, as the provider stamped it |
| `text` | TEXT | cut at the instruction's length |
| `length` | INTEGER | the text's length as delivered, before the cut |
| `admissibility` | TEXT | the verdict the admissibility test gave the article as it was stored |
| `session_date` | TEXT | the session it was first stored on |

Primary key: `ticker`, `article_id`.

Thirty-one days retained, a day past the thirty the labeller reads, and the counter drops the rows past it on the night they fall out, as it drops its own. Insert only, with a conflict ignored, so a night run twice and a second night that brings the same article back leave the row as it was (see: A news article is stored once per member with its admissibility judged, and a label is never overwritten).

### news_label
Grain: one row per ticker per article per profile per instruction version.

| Column | Type | Notes |
|---|---|---|
| `ticker` | TEXT | |
| `article_id` | TEXT | |
| `profile` | TEXT | the news job's profile that labelled it |
| `instruction_version` | INTEGER | the instruction's version it was labelled under |
| `model` | TEXT | the model as the provider answered, with the options it was asked with |
| `outcome` | TEXT | `labelled` or `unreadable` |
| `cause` | TEXT | why an answer was unreadable, one of the seven causes, null on a label |
| `kind` | TEXT | one of the nine kinds, null on an unreadable answer |
| `direction` | TEXT | `positive`, `negative` or `neutral`, null on an unreadable answer |
| `reason` | TEXT | one sentence holding no digit, null on an unreadable answer |
| `labelled_at` | TEXT | UTC instant |
| `run_id` | TEXT | the labeller's run that wrote it |

Primary key: `ticker`, `article_id`, `profile`, `instruction_version`.

Insert only, with a conflict ignored, so a label is never overwritten and after a switch the new profile writes rows of its own; an unreadable answer keeps its row so the article is not paid for again under that profile and version. The labeller drops a label whose article the counter has dropped, and is declared above as this table's deleter (see: A news article is stored once per member with its admissibility judged, and a label is never overwritten).

### research_section
Grain: one row per ticker, section and version.

| Column | Type | Notes |
|---|---|---|
| `ticker` | TEXT | |
| `section` | TEXT | which of the report's sections this is, named as figure 12.2's lane table names it |
| `version` | INTEGER | increments; earlier versions are kept |
| `as_of` | TEXT | date this section was written, and for the key under each figure the night of the facts file it was written from (see: The key under each figure is dated by the night whose figures it explains, written for every name each night, and drawn only beside that night's figures) |
| `model` | TEXT | which model wrote it |
| `status` | TEXT | `pending`, `accepted`, `rejected`, `fallback` |
| `prose` | TEXT | empty where the section had no admissible source to be written from |
| `source_ids` | TEXT | JSON list of `source_document` ids, in the order the prose cites them |
| `reject_reason` | TEXT | null unless rejected or fallback |
| `parts` | TEXT | JSON: from migration 50, the fields the risks section is answered as, `risks`, each with `risk`, `riskDocument`, `confirm` holding a listed `fact` or an `event` with its `kind`, `direction` and `level` where it names a fact, `why` and `whyDocument`, which its prose was composed from and the checker reads; null for every other section, for a risks answer that was not those fields, and on a row written before migration 50 (see: Each risk is returned as fields and confirmed by a listed fact or an event of one kind, and no two risks share either) |

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
| `evaluator_version` | TEXT | that evaluator's version as the code carried it when the row was written, which is the pin of every source its evaluation runs through |
| `event` | TEXT | `registered` or `retired`, constrained in the table |
| `retires` | TEXT | for a retirement, the candidate it retires |
| `registered_at` | TEXT | UTC instant, held to the second, and a row in the same second as a night's start or a window's opening is read as after it |
| `evidence` | TEXT | for a retirement, the figures that produced it |

Primary key: `id`.

No update, no delete and no replace, each refused by the table. A correction is a new row.

**The three evaluator columns are what make the row a registration rather than a description.** A candidate naming its rule in prose alone is a row a later session has to re-implement from words, and what it implements is then whatever it read the words to mean, which is the thing pre-registration exists to stop. `evaluator` names code that exists, `parameters` carries the values it is run with, and `evaluator_version` is the pin of that evaluator's source and every source its evaluation runs through but the catalogue listing every evaluator, with line endings normalised to LF and any leading byte order mark removed, so the same code pins the same on both platforms and on a runner that checked the tree out with either ending (see: A registration names an evaluator the code carries, and its version is the pin of every source its evaluation runs through but the catalogue). A changed evaluation is a new registration retiring the old one, never an edited row, and `register-append-only` fails a standing candidate whose evaluation's sources have moved away from the version its row names.

### rule_version
Grain: one row per rule per version. Append only but for the close.

| Column | Type | Notes |
|---|---|---|
| `rule` | TEXT | one of the five ladder rules the build carries, refused at the write where it carries none |
| `version` | TEXT | the version's name, `live` for the rule the night itself applies |
| `parameters` | TEXT | JSON, the values this version is replayed with, each one the replay applies as given |
| `parameters_hash` | TEXT | a hash of those parameters with the code version, which is what the night compares the live rule against |
| `code_version` | TEXT | the build's version of the rule's own code, hashed into `parameters_hash` |
| `opened_at` | TEXT | UTC instant the window opened |
| `closed_at` | TEXT | UTC instant it closed, null while open |
| `replaced_by` | TEXT | for a window closed by a replacement, the version the same write opened |
| `evidence` | TEXT | for a closed window, the figures or the reason that closed it, written by the close and required by it; last because SQLite appends |

Primary key: `rule`, `version`, `opened_at`.

**The instant is in the key, so a version closed and opened again is two windows and not one.** Scores belong to a window rather than to a version name, and a key without the instant would merge two measurements of the same name taken either side of a change, which is the thing frozen windows exist to prevent.

**A version row stands beside an open `live` row of its rule, and every open row counts against the bound.** The scorer writes every row when a person runs the worker's `version` verb and never during a night: a `live` row carries the build's own parameters and nothing else, a version row is refused where its rule has no open `live` row or where its `parameters` name anything other than what the rule is replayed from, and a `live` row is not closed while a version of its rule is open. At most two rows of the merge distance and four of each other rule are open at once, `live` rows included (see: A ladder rule's version is measured beside that rule's live window, and both count against a bound of eighteen). A version row is refused where a value is one the replay would not apply as given or where every value is its rule's live one (see: A version of a ladder rule is refused at values its replay would not apply as given, or at its rule's live values). A window is closed only with its evidence, and a version is changed by one write that closes the old window naming the version replacing it and opens that version, the rule's cap counted once the old one is closed (see: A rule version change closes the window with the evidence that produced it and opens its replacement in the same write).

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
| `sample` | TEXT | `scored` or `in_sample`; a score for a session on or before the New York date its window opened on is `in_sample` and counts toward no record and no verdict |

Primary key: `ticker`, `session_date`, `rule`, `version`, `opened_at`.

**`sample` is the column that keeps a backfill from becoming evidence.** A version added later may be scored over the nights before it, because seeing what it would have done is the point of scoring counterfactually at all. What it may not do is count: a rule written after those nights were seen and then scored on them is measured in sample, and a record holding such a score is a record of having fitted the rule to what already happened. The scorer writes the flag from the date in New York the window's own `opened_at` falls on, read through the clock, against the session being scored, so the classification is arithmetic rather than a caller's claim about itself, and a window opened on a session's own evening, after that night was read, counts from the session after it (see: A version's score counts only for a session after the New York date its window opened on).

One year retained, counted back from the newest stored session as the bars are, whatever night the scorer is scoring, and dropped by the scorer on the night the rows fall out of the window, at the order of the index times the versions open.

### version_block
Grain: one row per rule, version, window and completed block.

| Column | Type | Notes |
|---|---|---|
| `rule` | TEXT | |
| `version` | TEXT | |
| `opened_at` | TEXT | the window the block belongs to, as `version_score` keys a score to one |
| `block` | INTEGER | the block's number, counted in blocks of 63 exchange sessions from `origin` |
| `origin` | TEXT | the session block 0 is counted from, being the first session this window's score counted for, written with the window's first block and never recomputed |
| `version_excess` | REAL | the version's excess over this block: its wins less the bars those setups were judged against |
| `version_setups` | INTEGER | how many setups that excess is over |
| `live_excess` | REAL | the live rule's excess over the same block |
| `live_setups` | INTEGER | how many setups that excess is over |
| `version_null_sum` | REAL | the sum of the bars the version's setups were judged against |
| `version_null_spread` | REAL | the sum of `p(1 - p)` over those bars, which is the scatter independent setups would give |
| `live_null_sum` | REAL | the same sum over the live rule's setups |
| `live_null_spread` | REAL | the same scatter over the live rule's setups |
| `frozen_at` | TEXT | UTC instant of the write that froze the block; last because SQLite appends |

Primary key: `rule`, `version`, `opened_at`, `block`.

**Nothing here is dropped and nothing here is edited.** `version_score` and `ladder` are kept one year, counted back from the newest stored session, and a version's record is read at 8 blocks and again at 16, which is 1,008 sessions and about four years. A record computed from those two tables when it is read could hold 3 whole blocks at most against a floor of 8, so this table holds the one number per block the record reads and the per-night detail goes on being dropped (see: A version's record is read from the blocks frozen as each completed). The two excesses and the two counts are what a difference, a setup count and an excess in points are read from, and the two pairs of sums are what a design effect and a smallest excess are read from, so every field a record exposes is derivable from these rows alone.

**A block is frozen 63 sessions after its last session, which is 125 behind the night it completes.** The one-year window is about 252 sessions, so the rows a block is computed from are still there on the night it is frozen, with 127 sessions of margin.

**`origin` is on every row of a window and carries one value.** It is a fact about the window rather than about the block, and it is stored because retention moves the earliest scored session forward: an origin read from the store each night would re-cut the blocks under a record already being read, and a look's boundary was found over the arrangement its blocks had. It is written with the window's first block, which completes 125 sessions in, and every later block of that window takes it from the row already held.

**Only a rule whose versions can only take a setup away carries rows here**, which is the trend rule. A version of any other rule produces plans whose outcomes nothing computed, so there is no live side to difference it against, and the table is keyed on the rule so a later one can join it.

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

### research_request
Grain: one row per request.

| Column | Type | Written by |
|---|---|---|
| `ticker` | TEXT | ReadApi on a press and RequestDrain on the night's own request, each on the insert |
| `asked_at` | TEXT | UTC instant of the press or of the night's request, and the order the drain works in; on the insert |
| `asked_from` | TEXT | `list` or `name`, the screen a press came from, written by ReadApi, or `night`, the night's own request, written by RequestDrain; on the insert |
| `lane` | TEXT | `local` or `paid`, carried from the press so a queue drained later writes under the lane it meant, and `paid` on the night's own request; on the insert |
| `state` | TEXT | `outstanding`, `writing`, `written`, `refused` or `withdrawn` |
| `settled_at` | TEXT | UTC instant the request settled or was withdrawn, written by RequestDrain when it moves the request to `written` or `refused` and by ReadApi when it moves it to `withdrawn`; null while it is `outstanding` or `writing`, because a claim writes `state` alone |
| `run_id` | TEXT | the pass's run, written by RequestDrain when it settles the request, null before; last but two because SQLite appends |
| `reason` | TEXT | why, in words, for `refused` and `withdrawn` and null otherwise; last but one because SQLite appends |
| `refresh` | INTEGER | 1 where the press asked for every section to be written again, which the drain hands the pass as the operator's own ask, and 0 otherwise, constrained in the table; 0 on every row written before the column and on the night's own request; written by ReadApi on the insert; last because SQLite appends |

Primary key: `ticker`, `asked_at`.

At most one row per ticker in state `outstanding`, which is what a second press is refused against. SQLite cannot state a partial uniqueness in a table constraint, so it is a unique index over `ticker` filtered to that state.

Declared column sets, stated per operation because that is the grain the rule is written at: ReadApi inserts `ticker`, `asked_at`, `asked_from`, `lane`, `state` and `refresh` for a press, RequestDrain inserts the first five for the night's own request, which asks for no rewrite and takes the column's 0, and ReadApi updates `state`, `settled_at` and `reason` when it withdraws one. RequestDrain updates `state` when it claims a request, and `state`, `settled_at`, `reason` and `run_id` when it finishes with one. Both write `state` and never in one operation: a claim moves it off `outstanding`, and a withdrawal names `outstanding` in its own statement and moves nothing once a claim has.

**A withdrawal races a claim, and the store settles it rather than the reader.** The queue screen is read before a press and the drain may claim the request between the two, so the withdrawal states the state it expects and moves nothing where the row has left it. The reader is told which state refused them, which is the answer to what was asked: a report already being written is not one that has not been generated.

**The run is written at the settle and not at the claim.** A pass names its own run from the instant it starts, which the claim cannot know before it runs, so the request carries the run of the pass that answered it rather than one invented beside it.

**There is no deleter.** A withdrawn request stays a row, because what is asked of this table is what was asked for and what came of it, and removing the row would answer the second question by erasing the first. Nothing falls out of a window either: the table grows by presses rather than by names or nights.

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

**A paid call is a row of its own, and the rows are the ledger.** The spend cap writes one row for every call it judges, under the pass's run, with the stage `research call:` followed by the section asked for, and from 6.8 the round where a pass asks for a section again inside the same run, as `research call: The two cases, round 2`, so one section's call in one round of one pass is one row. `ok` carries what the call cost in `spend`, one model call and one network request; `paused` is a call a cap refused before it was made, counting nothing and spending nothing; `unavailable` is a call the provider did not answer, counting the attempt and spending nothing; `refused` is a call the provider declined, which spends nothing, or answered with nothing a section could store, which the provider counted and billed and which carries that price in `spend`. `spend` is TEXT holding a decimal, written invariant and never rounded, and what the cap judges a call by is these rows summed by the UTC day and the UTC month each started in, so the ledger is measured off this table rather than kept by anything that spends, whichever job's model the call was made to (see: Every paid call is made through the spend cap, which holds each paid job's model).

**A trial's calls stand in the ledger under a round of their own, and its section rows spend nothing.** From the 12.6 correction giving each section a profile, a trial's call is written by the spend cap under the pass's run as `research call: The two cases, trial`, and its retry as `research call: The two cases, trial, round 2`, so the caps count it as they count every call and the run page leaves it out of a pass's count and a report's cost by the round's word. Beside them the trial writes one row a section with the stage `section trial:` followed by the section, its outcome `first time`, `on retry`, `left out` or `not answered`, no calls and a `spend` of `0`, since the calls are already in the ledger, and a `detail` holding the ticker, the section, the trial's profile and model, the pass's version it was asked beside with that version's model, status and prose, each round's draft, verdict and price, the outcome, the calls and the cost. The trial counts the reports it has run over by the distinct runs holding such rows for its profile from its first day, which is what stops it (see: A trial asks a second profile for named sections beside a report, and ships naming none).

**A review's rows are a trial's under words of their own.** From the 12.6 correction refining the research prompt, a review's calls are written as `research call: The two cases, review` and `research call: The two cases, review, round 2`, and its section rows with the stage `section review:` followed by the section, spending nothing, their `detail` shaped as a trial's with a `mode` of `review` beside it, the draft it was handed being the version under `compared`. A review counts its own reports by its own stage, apart from a trial's (see: A review asks a section's model to check its own draft against the section's rules, beside a stated number of reports).

**A command a person runs through the `version` or `register` verb writes under a run id of the verb's name and its instant to the ten-millionth of a second.** Every open, replacement, close and backfill, refused or not, is one row under `rule-versions`, a listing writes none, every registration and retirement, refused or not, is one row under `candidate-register`, and the run page draws the rows as run by hand rather than as stages of the night.

**A replay of a family rule is one row a rule under a run of its own.** The family replay a registration of a family rule runs before it writes is a run named `replay-` and its instant to the ten-millionth of a second, apart from the registration's own run, one row a rule with the stage `family-replay:` followed by the rule's name, the outcome `reproduced` where every trade it kept is the one the rule's record stored and `differs` where one is not, and a `detail` of JSON carrying the rule, the code version it was replayed under, the session it replayed from, the nights it read, the trades the record stored and the replay kept, and in words what it found, the first trade that differed among them. The run page reads where each rule's record counts from off the newest such row under the version the rule stands at (see: A family rule registered again keeps its record from its first registration where a replay of its stored nights reproduces every trade, and restarts at the change otherwise).

**The backfill's row names what it asked for and every member still holding no year, and its schedule reads those rows back.** Its `detail` is JSON carrying the session, the names it asked for, each refused year's missing session, and each member still holding no year with how many nights it has been asked for, the session it was last asked for on, and the session it is next asked for on, null where that is the next night. A night that asks for nothing and leaves no member without a year writes none. The count of nights a name was asked for is read off these rows rather than kept a second time, because the run log is the record of what each night did, and the name page reads the newest row naming a name for the line it opens with (see: A name the backfill stored nothing for is asked for again on the five nights after and weekly after that, and its page and the run page say so until one stores its year).

**The overnight queue writes one row under the night's run, and each name it gives a pass is a run of its own.** From 6.10 the queue's row carries the stage `overnight queue`: `ok` where it ran through every name it queued, `limit` where it started no pass once its hours had passed and left names for the next night, and `unavailable` where the local model could not be called, which stops it at that name: the runtime not answering, a load refused or past its allowance, a model the runtime does not hold, or settings naming no model the lane can call. `model_calls` is every call its passes made, the one that found the local model not answering included and none where the settings name no model, `network_requests` and `rows_written` are zero, since it fetches nothing and writes no research itself, and `spend` is zero. `detail` names the night, the names listed and queued, every pass it ran with the run that pass was written under, the pass it stopped at, the names it left, and whether the machine was held awake. Each pass's staleness, prose and claims rows are under that pass's own run, so a night's model calls are read off the queue's row and the runs it names (see: The night's zero-model-call rule bounds the arithmetic, and the overnight queue is carved out of it by name).

**A night's try again is a run of its own, and the stop it follows says when it starts.** A try the night makes again after a stop before the close, and a run of the rest of a night by the press or `--resume`, writes under the id of the night's first try with `-try-` and its number, and writes only the stages it runs. Beside the stop, the try that stopped writes one row under its own run with the stage `try again` and the outcome `waiting`, with `started_at` and `ended_at` the instant it wrote it and `detail` naming the next try's number, the step it starts from and the instant it starts at; a try with no try left writes none. Written by `Nightly` alone, and read by `RunScreen.Night` alone, which reads a night's first try and every try carrying its id as one night (see: A night that stops before its close is tried again from the step that stopped, three more times fifteen minutes apart, each try under a deadline of its own).

**The store's copy writes two rows of its own, one as it starts and one as it ends, and names its folder relative to the data root.** From the operator's ruling of 2026-10-02 the copy the night starts after its labeller writes under `backup-` and its instant. As it starts, before it waits for anything, it writes a row with the stage `store-backup-started`, its start at both ends and the outcome `started`, its `detail` JSON saying whether it waits for the labeller; a start with no ending row of its run is a copy ended before it finished. As it ends it writes a row with the stage `store-backup`: `ok` where a copy was made, opened and read, its `detail` JSON carrying the copy's name, the folder relative to the data root with forward separators, or its name alone where it lies on a volume no relative path reaches, its bytes, the newest session and the bars it read back, the copies kept, those removed, any kept copy that did not open and read, the copies the newest ending row before it kept that were gone from the folder when it began, removed by no copy, and what it waited for; `failed` where it made none or the new copy did not open and read, with why. The copies the ending rows name, made or refused, are the store's own, and the only ones a copy counts or removes (see: A store's copy counts and removes only the copies its own rows name, and a test or a rehearsal names a copies' folder of its own). The night's own step writes one row under the night's run with the stage `backup`, saying the copy was started or why it was not (see: The store is copied once the night and every process it started have finished and the newest three copies are kept after each is opened and read, and the copy writes a row as it starts and one as it ends).

**A drain that stops on an error writes one row of its own.** From the 9.2 correction of 2026-10-03 an error escaping the drain's put-back or its queue is written by `RequestDrain` under `drain-` and the instant the drain started, with the stage `drain` and the outcome `failed`, `started_at` the drain's start and `ended_at` the instant it stopped, nothing counted, nothing spent and `detail` the error's type and words with no path a machine roots. The queue page reads the newest such row while no row of a pass's run starts after it, and the run page's checklist names each such row on the night it fell on (see: A drain that stops on an error writes a row of its own, and the queue page states it until a pass starts after it).

### watch_list
Grain: one row per name the operator watches.

| Column | Type | Notes |
|---|---|---|
| `ticker` | TEXT | the name, a member of the index when it was added; the key |
| `added_at` | TEXT | the UTC instant of the press that added it |

At most twenty rows, which ReadApi holds to on the press that would add a twenty-first. Written on the operator's press alone, from the watch list page or a name's own page, and read by the pages alone.
