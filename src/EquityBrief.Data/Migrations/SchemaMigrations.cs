namespace EquityBrief.Data.Migrations;

// Every migration, in the order they apply.
//
// The applied version is SQLite's own user_version pragma rather than a table.
// 0.2 creates run_log and no other table, and a version a table holds is a
// table SCHEMA.md would have to declare, grain, columns and owner, to record
// something the file format already records.
public static class SchemaMigrations
{
    // The column types are the ones SCHEMA.md declares. STRICT is what makes a
    // declared type mean something: without it a type is an affinity and an
    // INTEGER column takes any text at all.
    //
    // It is not the whole of the money rule and is not asked to be. SQLite
    // converts a REAL to TEXT because that conversion is lossless, so a double
    // written to spend is stored as its rendering rather than refused. The
    // money rule therefore rests on price-storage-form reading this text and on
    // prices being decimal in code, and MigrationRunnerTests pins the coercion
    // so a later session does not assume the store catches it.
    const string CreateRunLog = @"
        CREATE TABLE run_log (
            run_id            TEXT    NOT NULL,
            stage             TEXT    NOT NULL,
            started_at        TEXT    NOT NULL,
            ended_at          TEXT,
            outcome           TEXT,
            rows_written      INTEGER,
            model_calls       INTEGER,
            network_requests  INTEGER,
            spend             TEXT,
            detail            TEXT,
            PRIMARY KEY (run_id, stage)
        ) STRICT;
    ";

    // `left` is quoted in every statement that names it, here and in the loader.
    // It is a keyword in SQLite's grammar, as in LEFT JOIN, and an unquoted use
    // beside a table alias parses as the start of a join rather than as a
    // column. SCHEMA declares the column and the name is not changed to suit the
    // grammar; it is quoted instead.
    const string CreateMembership = @"
        CREATE TABLE membership (
            index_code   TEXT NOT NULL,
            ticker       TEXT NOT NULL,
            joined       TEXT NOT NULL,
            ""left""     TEXT,
            observed_at  TEXT NOT NULL,
            PRIMARY KEY (index_code, ticker, joined)
        ) STRICT;
    ";

    // The four price columns are TEXT and decimal in code
    // (see: Bars are never interpolated). STRICT does not enforce that, since
    // SQLite renders a double as text and stores it, so the storage half rests
    // on price-storage-form reading this declaration and the code half on the
    // runtime guard the store carries.
    //
    // volume is INTEGER because it is a count rather than a price, and
    // session_date is a date rather than an instant, which is why both it and
    // observed_at exist: the date carries the as-of meaning and the instant
    // carries distinctness.
    const string CreateBar = @"
        CREATE TABLE bar (
            ticker        TEXT    NOT NULL,
            session_date  TEXT    NOT NULL,
            open          TEXT    NOT NULL,
            high          TEXT    NOT NULL,
            low           TEXT    NOT NULL,
            close         TEXT    NOT NULL,
            volume        INTEGER NOT NULL,
            source        TEXT    NOT NULL,
            observed_at   TEXT    NOT NULL,
            PRIMARY KEY (ticker, session_date)
        ) STRICT;
    ";

    // The provider's unadjusted close, kept beside the adjusted set.
    //
    // Added rather than folded into migration 3, because a store already at
    // version 3 does not re-run it and would carry the table without the column.
    // Added rather than recreated, because bar-append-only forbids a migration
    // dropping a bar table, and that rule is worth more than a tidy column
    // order: SQLite appends, so SCHEMA declares this one last.
    //
    // Nullable because SQLite cannot add a NOT NULL column, and a default would
    // be a value nobody wrote. Every bar written from here carries it, and the
    // check asserts that over the store rather than trusting the type.
    const string AddRawClose = @"
        ALTER TABLE bar ADD COLUMN raw_close TEXT;
    ";

    // The largest moves of the stored year, one row per ticker and the session a
    // move ended on.
    //
    // `change_pct` is REAL because a percentage of a price is a statistic and
    // not a price, which is the same division `volume_profile` draws in one row.
    // `sessions` is how many sessions the move spans, 1 for a single day, and it
    // is what makes the catalogue row true: a table keyed on one session with no
    // span could carry only the single-day half of what the annotator selects.
    //
    // `rank` is the position within the name's own set by absolute size. The
    // cause of each move is not here: it is a researched claim and lives in
    // `research_section` with its source.
    const string CreateMove = @"
        CREATE TABLE move (
            ticker       TEXT NOT NULL,
            session_date TEXT NOT NULL,
            sessions     INTEGER NOT NULL,
            change_pct   REAL NOT NULL,
            rank         INTEGER NOT NULL,
            PRIMARY KEY (ticker, session_date)
        ) STRICT;
    ";

    // The forward returns, one row per listing per horizon.
    //
    // `outcome` is `win`, `loss`, `unresolved`, `never entered` or null while
    // immature. `unresolved` and `never entered` are values rather than nulls, so
    // each is counted in its own column and never in a rate, and an immature row
    // reads as not yet matured rather than as a blank or a zero.
    //
    // `return_pct` and `base_rate` are REAL because both are statistics rather
    // than prices. `base_rate` is null for the `setup` horizon, whose `return_pct`
    // runs from the close the setup was entered at.
    //
    // No deleter, and a decided row is never written again: this is the
    // operator's own record and it is kept forever.
    const string CreateForwardReturn = @"
        CREATE TABLE forward_return (
            ticker       TEXT NOT NULL,
            session_date TEXT NOT NULL,
            horizon      TEXT NOT NULL,
            outcome      TEXT,
            resolved_on  TEXT,
            return_pct   REAL,
            base_rate    REAL,
            PRIMARY KEY (ticker, session_date, horizon)
        ) STRICT;
    ";

    // The news pulse, one row per ticker per date.
    //
    // It exists so the staleness judge can work without spending anything, and
    // it is the one table whose retention has been owned since 1.4 by the
    // component that writes it.
    const string CreateNewsPulse = @"
        CREATE TABLE news_pulse (
            ticker       TEXT NOT NULL,
            session_date TEXT NOT NULL,
            article_count INTEGER NOT NULL,
            PRIMARY KEY (ticker, session_date)
        ) STRICT;
    ";

    // The listings, one row per ticker per night, for every index member and not
    // only the listed ones.
    //
    // The every-name grain is the whole point of the table. A shadow candidate
    // has to be evaluated on the nights it would have fired, and most of those
    // are nights no live reason surfaced that name, so writing rows only for
    // listed names would make section 13's shadow mechanism impossible without
    // anything announcing it.
    // see: Candidate conditions are registered before they are scored, and a candidate's picks are shown on the Run page while its outcomes wait for a look
    //
    // `plan_at_listing` is the column the improvement loop rests on. Bars can be
    // replayed and the plan cannot, because by the time a verdict is possible
    // the rules may have changed and recomputing would score old listings under
    // new ones.
    //
    // No deleter. This is the operator's own record and no provider can sell it
    // back, so it is kept forever.
    // see: Your own listing history is kept forever
    const string CreateListing = @"
        CREATE TABLE listing (
            ticker          TEXT NOT NULL,
            session_date    TEXT NOT NULL,
            reasons         TEXT NOT NULL,
            fired_count     INTEGER NOT NULL,
            plan_at_listing TEXT NOT NULL,
            shadow_reasons  TEXT NOT NULL,
            PRIMARY KEY (ticker, session_date)
        ) STRICT;
    ";

    // The facts file, one row per ticker per night, written by two components on
    // disjoint columns of the same row.
    //
    // `payload` and `payload_hash` are the assembler's; `material_changes` is
    // the change detector's. The sets are disjoint and the grain is the same,
    // which is what permits a table with an inserter and a different updater
    // under a rule that forbids two owners for one operation.
    //
    // Every column is TEXT. The payload is JSON holding every number the
    // computed sections may use with the source of each, and a number inside it
    // is in the storage form the store uses everywhere else, so a price is text
    // there too.
    const string CreateFacts = @"
        CREATE TABLE facts (
            ticker           TEXT NOT NULL,
            session_date     TEXT NOT NULL,
            payload          TEXT NOT NULL,
            payload_hash     TEXT NOT NULL,
            material_changes TEXT,
            PRIMARY KEY (ticker, session_date)
        ) STRICT;
    ";

    // The sector, on the membership row, added at 5.1 because that is where the
    // universe screen filters on it.
    //
    // Nullable for the reason raw_close is, and for a second reason of its own:
    // the value comes from the snapshot object of the constituents response,
    // which carries current members alone, so a name that has left the index has
    // no sector to read and null is what is true. Null is drawn as not on file
    // and excluded by name from every bucket rather than falling into one.
    const string AddMembershipSector = @"
        ALTER TABLE membership ADD COLUMN sector TEXT;
    ";

    // The industry, on the membership row, added at 6.9 because that is where a theme
    // is read: a theme is the industry the index names for a member, and every member
    // the index puts in one industry reads the one record its pass wrote. Nullable for
    // the sector's reason, since it comes from the same snapshot object.
    // see: A theme is the industry the index names for a member, and one theme pass serves every member it names
    const string AddMembershipIndustry = @"
        ALTER TABLE membership ADD COLUMN industry TEXT;
    ";

    // How many nights after the one that marked it a suspect name has been asked for
    // again, added at the 6.0 ruling because a count is what bounds them: the check reads
    // it to decide whether tonight asks again. A count rather than a history, since when
    // each attempt was made is the run log's.
    //
    // Not null with a default, unlike the columns above, because here the default is a
    // value that holds rather than one nobody wrote: every row that exists when the column
    // is added has been counted by nothing, and the rule counts from the night it lands, so
    // a name already suspect is asked for again on the limit's nights from then, and weekly
    // after them from the 7.0 ruling, which counts on past the limit in the same column.
    // see: A suspect name is asked for again on the five nights after it is marked and weekly after that, and its own page, its row on tonight's list and the run page say so until a refetch succeeds
    const string AddSeriesStateRetries = @"
        ALTER TABLE series_state ADD COLUMN retries INTEGER NOT NULL DEFAULT 0;
    ";

    // Whether a name's stored series can be trusted, at the grain the statement
    // is about, which is the name. Contradiction C: the failure table said a
    // name whose corporate action check failed is marked suspect and nothing
    // held that, so a failed check passed silently.
    //
    // Not a column on `bar`, whose grain is a session, and not on `membership`,
    // which records whether a name is in the index and not whether its bars are
    // believable.
    const string CreateSeriesState = @"
        CREATE TABLE series_state (
            ticker     TEXT NOT NULL,
            state      TEXT NOT NULL,
            reason     TEXT,
            checked_at TEXT NOT NULL,
            PRIMARY KEY (ticker)
        ) STRICT;
    ";

    // Indicators, one row per ticker, session and indicator name.
    //
    // `value` is REAL and not TEXT, which is the one place in this store where a
    // number computed from prices is not money. SCHEMA says so and the reason is
    // that an average of a price is a statistic about prices rather than a price:
    // nothing quotes it as money, no arithmetic on it settles a trade, and
    // price-storage-form's money column list does not name it.
    //
    // `value` is nullable because an indicator whose window is longer than the
    // history behind a session has no value, and `bar_count` is what makes that
    // null legible. A row is written either way, so an absence is a stored fact
    // rather than a missing row a reader has to interpret.
    const string CreateIndicator = @"
        CREATE TABLE indicator (
            ticker       TEXT NOT NULL,
            session_date TEXT NOT NULL,
            name         TEXT NOT NULL,
            value        REAL,
            bar_count    INTEGER NOT NULL,
            PRIMARY KEY (ticker, session_date, name)
        ) STRICT;
    ";

    // Swings, one row per ticker, session and direction.
    //
    // Every column is NOT NULL, which is the difference from `indicator` next
    // door and is worth stating rather than leaving to be noticed. An indicator
    // is written for every session whether or not its window is full, so an
    // absence is a stored fact. A swing is written only where there is one, so
    // there is no absent case to record: a session that is not a peak has no row
    // rather than a row with no price.
    //
    // `direction` carries the two values SCHEMA's column note names and no
    // constraint enforces that, because the enforcement is the one declared
    // writer: SwingFinder writes the two constants SwingSeries states and
    // nothing else inserts here. A CHECK would be a second statement of a fact
    // SCHEMA already carries, in a place SCHEMA does not describe.
    //
    // `price` is TEXT because a swing is a price. That is the money rule rather
    // than a preference, and it is the column that makes this table differ in
    // storage type from the indicator table computed off the same bars.
    const string CreateSwing = @"
        CREATE TABLE swing (
            ticker       TEXT NOT NULL,
            session_date TEXT NOT NULL,
            direction    TEXT NOT NULL,
            price        TEXT NOT NULL,
            confirmed_on TEXT NOT NULL,
            PRIMARY KEY (ticker, session_date, direction)
        ) STRICT;
    ";

    // The volume profile, one row per ticker, as-of date and price band.
    //
    // `band_low` and `band_high` are TEXT because they are prices, and
    // `share_of_period` is REAL because it is a fraction of a count rather than
    // a price. Both storage forms sit in one row here, which is the clearest
    // statement in this store of what the money rule actually divides: the edges
    // are money and the share is not.
    //
    // `share_count` is INTEGER and the arithmetic that fills it is a division, so
    // the builder apportions rather than rounds. The primary key is the as-of
    // date and the low edge, so a night recomputing its own date replaces its
    // rows and a later night adds a set of its own.
    const string CreateVolumeProfile = @"
        CREATE TABLE volume_profile (
            ticker          TEXT NOT NULL,
            as_of           TEXT NOT NULL,
            band_low        TEXT NOT NULL,
            band_high       TEXT NOT NULL,
            share_count     INTEGER NOT NULL,
            share_of_period REAL NOT NULL,
            PRIMARY KEY (ticker, as_of, band_low)
        ) STRICT;
    ";

    // The level bands, one row per ticker, as-of date and band.
    //
    // Every price column is TEXT and every flag is INTEGER, which is what STRICT
    // gives instead of a boolean: SQLite has none, and SCHEMA declares the two
    // flags as integers carrying 1 rather than as a type the store does not
    // have.
    //
    // `members` is TEXT holding JSON, and it is a column rather than a table for
    // one reason: a member has no identity of its own and nothing ever queries
    // for one. It is read as a whole with the band it belongs to, by the level
    // table and by the report, and a members table would be a join on every read
    // for a list nothing indexes.
    //
    // `has_non_average_anchor` is stored rather than derived because the ladder
    // builder reads it on every band, and a short average follows the price, so
    // a band anchored only on one sits at the price about half the time.
    // The calendar, one row per ticker, event date and kind.
    //
    // It holds what the provider files and nothing else. A dated item a research
    // pass found is a claim resting on a source document, so it lives in
    // `research_section` and reaches the report's Dates section from there:
    // writing one here would put a claim where the claim checker cannot reach it
    // and would give a nightly store a second inserter.
    //
    // `timing` is what the provider carries and `status` was not. 4.0 gave this
    // table a `status` column for whether the print was confirmed, and the
    // payload has no such field. What it does carry is whether the report falls
    // before the session or after it, which decides which bar prices the print.
    // Found at 4.3 by capturing the endpoint before writing the parser.
    const string CreateCalendar = @"
        CREATE TABLE calendar (
            ticker       TEXT NOT NULL,
            event_date   TEXT NOT NULL,
            kind         TEXT NOT NULL,
            timing       TEXT NOT NULL,
            detail       TEXT NOT NULL,
            observed_at  TEXT NOT NULL,
            PRIMARY KEY (ticker, event_date, kind)
        ) STRICT;
    ";

    // The ladder, one row per ticker per as-of date, for every index member and
    // not only the names carrying a plan.
    //
    // A name in a downtrend, a name whose only support band is anchored on a
    // moving average, and a name whose trend could not be classified all get a
    // row whose `plan` states why it is empty. An absent row says nothing, and
    // the trend-changed condition compares tonight's label against last night's,
    // so a name with no row on the night its band went ineligible has no
    // yesterday for the transition that changes the whole plan.
    //
    // `trend_state` carries four values and not three. `not_classified` is a
    // name with fewer than 200 bars or fewer than two swings of the kind the
    // rule reads, and it is stored rather than defaulted to `range` for the
    // reason `indicator.bar_count` exists: the label decides whether a plan
    // exists at all.
    //
    // `plan` is TEXT holding JSON, and it is a column rather than a set of
    // tables for the reason `level.members` is one: a tranche has no identity of
    // its own and nothing queries for one. What queries this table asks for a
    // name's plan on a date, which is the row.
    const string CreateLadder = @"
        CREATE TABLE ladder (
            ticker       TEXT NOT NULL,
            as_of        TEXT NOT NULL,
            trend_state  TEXT NOT NULL,
            plan         TEXT NOT NULL,
            PRIMARY KEY (ticker, as_of)
        ) STRICT;
    ";

    const string CreateLevel = @"
        CREATE TABLE level (
            ticker                  TEXT NOT NULL,
            as_of                   TEXT NOT NULL,
            low_edge                TEXT NOT NULL,
            high_edge               TEXT NOT NULL,
            role                    TEXT NOT NULL,
            immediate               INTEGER NOT NULL,
            strength                INTEGER NOT NULL,
            has_non_average_anchor  INTEGER NOT NULL,
            members                 TEXT NOT NULL,
            PRIMARY KEY (ticker, as_of, low_edge)
        ) STRICT;
    ";

    // One row per ticker per filing date, and every column TEXT because every one
    // of them is a date, an instant, a document or a name. The figures live inside
    // `payload` as JSON, in the storage form money takes, which is what keeps a
    // revenue out of a REAL column: a table with a column per figure would have
    // thirty of them and each would be a place to write a double.
    //
    // Kept forever and never updated, which is the ownership SCHEMA declares. A
    // provider that restates a quarter files it again under a new filing date, so
    // a restatement is a new row and what was known at the time stays readable.
    const string CreateFundamentals = @"
        CREATE TABLE fundamentals (
            ticker       TEXT NOT NULL,
            filing_date  TEXT NOT NULL,
            fetched_at   TEXT NOT NULL,
            payload      TEXT NOT NULL,
            source       TEXT NOT NULL,
            PRIMARY KEY (ticker, filing_date)
        ) STRICT;
    ";

    // One row per fetched document, and the one table here whose rows are kept
    // for two opposite reasons. An admitted document is kept because a claim
    // whose source is missing is not rendered, so the body a sentence rests on
    // has to still be there. A refused one is kept because a refusal that left
    // no row would be invisible, and the only surface that can show what was
    // refused is one reading these rows.
    //
    // `published_on` and `body` both admit null and neither is an absence of
    // data. A document refused for carrying no publish date is a row with none,
    // and a document refused for anything is a row with no body: that is what
    // the refusal being kept means. An admitted row carries both, which is a
    // property of the test rather than of the column and is asserted as one.
    //
    // `admissibility` holds either the acceptance or the category that refused
    // it, so one column answers both questions and a reader never has to pair a
    // verdict with a reason. Kept forever and never updated: a second fetch of
    // the same url conflicts on the id and leaves the first row standing, which
    // is what makes the verdict a record of what was decided at the time.
    const string CreateSourceDocument = @"
        CREATE TABLE source_document (
            id             TEXT NOT NULL,
            url            TEXT NOT NULL,
            title          TEXT NOT NULL,
            published_on   TEXT,
            fetched_at     TEXT NOT NULL,
            body           TEXT,
            admissibility  TEXT NOT NULL,
            PRIMARY KEY (id)
        ) STRICT;
    ";

    // The research store and the theme store, one row per section per version.
    //
    // Created together because they are one shape with a different subject, and
    // the checker that moves a section between statuses reads and writes both.
    // Every version is kept and nothing deletes one: a section rewritten next
    // month is a new version beside this one, which is what lets a reader see
    // what the report said at the time.
    //
    // The status is guarded in the table rather than only in the checker. A
    // status nobody declared is a section no surface knows how to draw, and a
    // check constraint is what makes that a refusal at the write rather than a
    // row the page silently skips. `prose` and `source_ids` are not null and may
    // be empty, because a section with no admissible source to be written from is
    // still a row, the one that records it was left out and why.
    const string CreateResearchSections = @"
        CREATE TABLE research_section (
            ticker         TEXT NOT NULL,
            section        TEXT NOT NULL,
            version        INTEGER NOT NULL,
            as_of          TEXT NOT NULL,
            model          TEXT NOT NULL,
            status         TEXT NOT NULL CHECK (status IN ('pending', 'accepted', 'rejected', 'fallback')),
            prose          TEXT NOT NULL,
            source_ids     TEXT NOT NULL,
            reject_reason  TEXT,
            PRIMARY KEY (ticker, section, version)
        ) STRICT;

        CREATE TABLE theme_section (
            theme          TEXT NOT NULL,
            section        TEXT NOT NULL,
            version        INTEGER NOT NULL,
            as_of          TEXT NOT NULL,
            model          TEXT NOT NULL,
            status         TEXT NOT NULL CHECK (status IN ('pending', 'accepted', 'rejected', 'fallback')),
            prose          TEXT NOT NULL,
            source_ids     TEXT NOT NULL,
            reject_reason  TEXT,
            industries     TEXT NOT NULL,
            PRIMARY KEY (theme, section, version)
        ) STRICT;
    ";

    // The candidate register, and the two triggers that make append-only a
    // property of the store rather than of the components that reach it.
    //
    // A registered candidate names an evaluator the code carries and the
    // parameters it is evaluated with, so the row says what will run rather than
    // describing it in prose a later session has to re-implement. It carries that
    // evaluator's version too, which is a hash of the evaluator's own source, so
    // a row names the exact code it was registered under and a changed evaluator
    // cannot be read as the one the register named.
    //
    // The triggers are the point of the table. Pre-registration only works if a
    // registered candidate cannot be changed once results are in, and a rule held
    // only by the components that write is a rule that lasts until something
    // writes another way. `RAISE(ABORT)` refuses the write and rolls back the
    // statement, so the register still reads as it did.
    //
    // `event` is constrained rather than left open, because a row that is neither
    // a registration nor a retirement is a row the divisor cannot count and the
    // shadow column cannot evaluate, and a check constraint is what makes that a
    // refusal at the write rather than a row every reader skips differently.
    // see: Candidate conditions are registered before they are scored, and a candidate's picks are shown on the Run page while its outcomes wait for a look
    const string CreateCandidateRegister = @"
        CREATE TABLE candidate_register (
            id                 INTEGER NOT NULL,
            candidate          TEXT NOT NULL,
            rule               TEXT NOT NULL,
            test               TEXT NOT NULL,
            evaluator          TEXT NOT NULL,
            parameters         TEXT NOT NULL,
            evaluator_version  TEXT NOT NULL,
            event              TEXT NOT NULL CHECK (event IN ('registered', 'retired')),
            retires            TEXT,
            registered_at      TEXT NOT NULL,
            evidence           TEXT,
            PRIMARY KEY (id)
        ) STRICT;

        CREATE TRIGGER candidate_register_is_append_only_on_change
        BEFORE UPDATE ON candidate_register
        BEGIN
            SELECT RAISE(ABORT, 'candidate_register is append only: a correction is a new row naming what it retires.');
        END;

        CREATE TRIGGER candidate_register_is_append_only_on_removal
        BEFORE DELETE ON candidate_register
        BEGIN
            SELECT RAISE(ABORT, 'candidate_register is append only: a retirement is a new row naming what it retires.');
        END;
    ";

    // A replace removes the row it conflicts with without firing a delete trigger, so an insert naming
    // an id the register holds is refused whatever its conflict clause.
    // see: Candidate conditions are registered before they are scored, and a candidate's picks are shown on the Run page while its outcomes wait for a look
    const string RefuseARegisterReplace = @"
        CREATE TRIGGER candidate_register_is_append_only_on_replace
        BEFORE INSERT ON candidate_register
        WHEN EXISTS (SELECT 1 FROM candidate_register WHERE id = NEW.id)
        BEGIN
            SELECT RAISE(ABORT, 'candidate_register is append only: a row it holds is never written over, and a correction is a new row naming what it retires.');
        END;
    ";

    // The rule versions and the scores written under them.
    //
    // A version's window is opened by an insert and closed by writing its
    // `closed_at` and `replaced_by`, and from migration 28 its `evidence`, and
    // nothing else on the row ever changes:
    // the scores written under it are of the rule as it stood when it opened, and
    // a row edited afterwards would make them scores of something else. The
    // instant is in the key, so a version closed and opened again is two windows
    // and not one.
    //
    // `sample` is what keeps a backfill from becoming evidence. A version added
    // later may be scored over the nights before it, because seeing what it would
    // have done is the point of scoring counterfactually. What it may not do is
    // count, so a score for a session on or before the New York date its window
    // opened on is `in_sample` and reaches no record and no verdict.
    // see: Adding a candidate later restarts the clock
    const string CreateRuleVersions = @"
        CREATE TABLE rule_version (
            rule             TEXT NOT NULL,
            version          TEXT NOT NULL,
            parameters       TEXT NOT NULL,
            parameters_hash  TEXT NOT NULL,
            code_version     TEXT NOT NULL,
            opened_at        TEXT NOT NULL,
            closed_at        TEXT,
            replaced_by      TEXT,
            PRIMARY KEY (rule, version, opened_at)
        ) STRICT;

        CREATE TABLE version_score (
            ticker        TEXT NOT NULL,
            session_date  TEXT NOT NULL,
            rule          TEXT NOT NULL,
            version       TEXT NOT NULL,
            opened_at     TEXT NOT NULL,
            plan          TEXT NOT NULL,
            sample        TEXT NOT NULL CHECK (sample IN ('scored', 'in_sample')),
            PRIMARY KEY (ticker, session_date, rule, version, opened_at)
        ) STRICT;
    ";

    public static IReadOnlyList<Migration> All { get; } =
    [
        new Migration(1, "create run_log", CreateRunLog),
        new Migration(2, "create membership", CreateMembership),
        new Migration(3, "create bar", CreateBar),
        new Migration(4, "add bar.raw_close", AddRawClose),
        new Migration(5, "create series_state", CreateSeriesState),
        new Migration(6, "membership.joined admits an unknown", JoinedMayBeUnknown),
        new Migration(7, "create indicator", CreateIndicator),
        new Migration(8, "create swing", CreateSwing),
        new Migration(9, "create volume_profile", CreateVolumeProfile),
        new Migration(10, "create level", CreateLevel),
        new Migration(11, "create ladder", CreateLadder),
        new Migration(12, "create calendar", CreateCalendar),
        new Migration(13, "add membership.sector", AddMembershipSector),
        new Migration(14, "create move", CreateMove),
        new Migration(15, "create facts", CreateFacts),
        new Migration(16, "create listing", CreateListing),
        new Migration(17, "create forward_return", CreateForwardReturn),
        new Migration(18, "create news_pulse", CreateNewsPulse),
        new Migration(19, "create fundamentals", CreateFundamentals),
        new Migration(20, "create source_document", CreateSourceDocument),
        new Migration(21, "create research_section and theme_section", CreateResearchSections),
        new Migration(22, "add membership.industry", AddMembershipIndustry),
        new Migration(23, "add series_state.retries", AddSeriesStateRetries),
        new Migration(24, "add forward_return.break_even", AddForwardReturnBreakEven),
        new Migration(25, "create candidate_register", CreateCandidateRegister),
        new Migration(26, "create rule_version and version_score", CreateRuleVersions),
        new Migration(27, "candidate_register refuses a replace", RefuseARegisterReplace),
        new Migration(28, "add rule_version.evidence", AddRuleVersionEvidence),
        new Migration(29, "add membership.name", AddMembershipName),
        new Migration(30, "create research_request", CreateResearchRequest),
        new Migration(31, "add listing.band_strength", AddListingBandStrength),
        new Migration(32, "add forward_return's calibrated bar and what the trade came to", AddCalibratedBar),
        new Migration(33, "create version_block", CreateVersionBlock),
        new Migration(34, "research_request.asked_from admits the night", RequestAskedByTheNight),
        new Migration(35, "add move's group median", AddMoveGroupMedian),
        new Migration(36, "create peer_reading", CreatePeerReading),
        new Migration(37, "create earnings_reaction", CreateEarningsReaction),
        new Migration(38, "create swing_reading and market_reading", CreateSwingReadings),
        new Migration(39, "create gate_result and filter_version", CreateGateResults),
        new Migration(40, "create shape_proposal", CreateShapeProposal),
        new Migration(41, "add gate_result.shadow", AddGateResultShadow),
        new Migration(42, "create list_rule", CreateListRule),
        new Migration(43, "create watch_list", CreateWatchList),
        new Migration(44, "add research_request.refresh", AddResearchRequestRefresh),
        new Migration(45, "create fundamentals_snapshot", CreateFundamentalsSnapshot),
        new Migration(46, "create pulled_bar and pulled_earnings", CreatePulledHistory),
        new Migration(47, "add gate_result's plan clear of the noise", AddClearPlan),
        new Migration(48, "create reported_quarter, quarter_ask and fundamental_reading", CreateReportedQuarters),
        new Migration(49, "add peer_reading.peers", AddPeerPicks),
        new Migration(50, "add research_section.parts", AddRiskParts),
        new Migration(51, "create pulled_surprise", CreatePulledSurprise),
        new Migration(52, "create news_article and news_label", CreateNewsArticlesAndLabels),
        new Migration(53, "create family_night and family_pick", CreateFamilyPick),
        new Migration(54, "create family_result", CreateFamilyResult),
        new Migration(55, "create pulled_market_bar", CreatePulledMarketBar),
        new Migration(56, "add family_result.shadow and create family_trade", AddFamilyShadowAndTrades),
        new Migration(57, "create market_bar", CreateMarketBar),
        new Migration(58, "create pulled_company, pulled_shares, pulled_split and pulled_revenue", CreatePulledCompanies),
        new Migration(59, "add reported_quarter.shares, create company, heavyweight_night and heavyweight_holding", CreateHeavyweights),
        new Migration(60, "add heavyweight_night.beta, create heavyweight_rule_night, heavyweight_rule_holding and estimate_reading", CreateRuleBooks),
        new Migration(61, "create pulled_member", CreatePulledMembers),
        new Migration(62, "create pulled_income", CreatePulledIncome),
        new Migration(63, "create pulled_snapshot and pulled_holding", CreatePulledHoldings),
        new Migration(64, "create index_family_night, index_family_result, index_family_pick, index_family_trade and index_heavyweight_holding, and add family_pick.held_index", CreateIndexFamilies),
        new Migration(65, "create sweep_answer", CreateSweepAnswer),
        new Migration(66, "create member_reading and switch_reading, and add company's rating counts, reported_quarter's interest expense and family_trade.cost", CreateMemberReadings),
        new Migration(67, "create index_rule_trade", CreateIndexRuleTrades),
        new Migration(68, "create index_heavyweight_rule_night and index_heavyweight_rule_holding", CreateIndexHeavyweightRules),
        new Migration(69, "create decision_card and rule_record", CreateDecisionCards),
        new Migration(70, "add decision_card's sector, trail, cap, round trip and book holdings, and create taken_trade", CreateTakenTrades),
        new Migration(71, "add taken_trade's end and decision_card's hits, and create taken_record and dividend_reading", CreateTakenRecords),
        new Migration(72, "add index_heavyweight_holding.lead", AddIndexHoldingLead),
        new Migration(73, "add index_heavyweight_rule_holding.lead", AddIndexRuleHoldingLead),
        new Migration(74, "add pulled_holding's shares and value", AddPulledHoldingValue),
        new Migration(75, "create rule_night, rule_pick and forming_row", CreateRuleCards),
        new Migration(76, "create kept_bar", CreateKeptBar),
        new Migration(77, "create setup and setup_night", CreateLedger),
        new Migration(78, "create filed_fact, filed_fact_pull, filing_day and ledger_summary, and add setup's business readings", CreateFiledFacts),
        new Migration(79, "create loop_run, loop_proposal and loop_test", CreateLoopTests),
        new Migration(80, "add family_trade.exit, index_rule_trade.exit and loop_proposal.finding, and create loop_finding", CreateLoopFindings),
        new Migration(81, "create loop_reading", CreateLoopReadings),
        new Migration(82, "create loop_model, and add decision_card's score_rank and similar", CreateLoopModels),
        new Migration(83, "add loop_proposal.change, index_family_trade's exit and risk_moves and decision_card.approved, and create loop_reference, loop_decision, loop_applied, provisional_setting and loop_alarm", CreateLoopApprovals),
        new Migration(84, "create chart_average", CreateChartAverages),
    ];

    // The chart's averages over the sessions the indicator rows leave empty, read through the sessions before the store's
    // year from the pulled history: a row an average a name, its sessions and values as a list, the pull they came from,
    // or why none was read.
    // see: The chart's averages are read over the sessions before the store's year from the pulled history at the store's scale, by a step only the chart reads
    const string CreateChartAverages = @"
        CREATE TABLE chart_average (
            ticker    TEXT NOT NULL,
            name      TEXT NOT NULL CHECK (name IN ('sma20', 'sma50', 'sma200')),
            night     TEXT NOT NULL,
            sessions  TEXT NOT NULL,
            pull      TEXT,
            reason    TEXT,
            PRIMARY KEY (ticker, name)
        ) STRICT;
    ";

    // What an approval reads and writes, and the alarm: the change each proposal makes in the form an approval applies;
    // the rule today's trades over a run's test years, the alarm's reference; the operator's decisions, each once a
    // proposal of a run; what the apply step did with each; the S&P 400's and 600's provisional settings an approval
    // stands from, a row a change and never edited; each live rule's periods, each written once every trade that ended in
    // it holds its edge and read against the reference of the tester run it names; the exit an index page's trade is
    // walked under with its stop's distance in typical moves; and the change a card's rule stands at where an approval
    // set one.
    // see: An approved change is applied before the next night from the night's own build, on the index it was approved on alone
    // see: The live alarm flags a rule whose edge stood under its reference's fifth percentile two periods running
    const string CreateLoopApprovals = @"
        ALTER TABLE loop_proposal ADD COLUMN change TEXT;
        ALTER TABLE index_family_trade ADD COLUMN exit INTEGER;
        ALTER TABLE index_family_trade ADD COLUMN risk_moves REAL;
        ALTER TABLE decision_card ADD COLUMN approved TEXT;

        CREATE TABLE loop_reference (
            run_id      TEXT    NOT NULL,
            index_code  TEXT    NOT NULL,
            family      TEXT    NOT NULL,
            place       INTEGER NOT NULL,
            entered     TEXT    NOT NULL,
            edge        REAL    NOT NULL,
            PRIMARY KEY (run_id, index_code, family, place)
        ) STRICT;

        CREATE TABLE loop_decision (
            run_id      TEXT NOT NULL,
            index_code  TEXT NOT NULL,
            family      TEXT NOT NULL,
            proposal    TEXT NOT NULL,
            decision    TEXT NOT NULL CHECK (decision IN ('approved', 'declined')),
            reason      TEXT,
            decided_at  TEXT NOT NULL,
            PRIMARY KEY (run_id, index_code, family, proposal)
        ) STRICT;

        CREATE TABLE loop_applied (
            run_id      TEXT NOT NULL,
            index_code  TEXT NOT NULL,
            family      TEXT NOT NULL,
            proposal    TEXT NOT NULL,
            applied_at  TEXT NOT NULL,
            outcome     TEXT NOT NULL CHECK (outcome IN ('applied', 'refused')),
            words       TEXT NOT NULL,
            PRIMARY KEY (run_id, index_code, family, proposal)
        ) STRICT;

        CREATE TABLE provisional_setting (
            id          INTEGER PRIMARY KEY,
            index_code  TEXT NOT NULL,
            family      TEXT NOT NULL,
            change      TEXT NOT NULL,
            words       TEXT NOT NULL,
            set_at      TEXT NOT NULL,
            run_id      TEXT NOT NULL,
            proposal    TEXT NOT NULL
        ) STRICT;

        CREATE TABLE loop_alarm (
            index_code  TEXT    NOT NULL,
            family      TEXT    NOT NULL,
            period      TEXT    NOT NULL,
            trades      INTEGER NOT NULL,
            edge        REAL,
            edge_floor  REAL,
            counted     INTEGER NOT NULL,
            under       INTEGER NOT NULL,
            streak      INTEGER NOT NULL,
            flagged     INTEGER NOT NULL,
            reference   TEXT    NOT NULL,
            run_id      TEXT    NOT NULL,
            PRIMARY KEY (index_code, family, period)
        ) STRICT;
    ";

    // Each learned score a tester run fits, a family and a fold's year a row, its parameters whole with their hash, the
    // window it learned on and the pin of the code that fitted it; and a card's rank under its index's score and its part
    // of setups like the pick.
    // see: A fitted statistical model is a rule
    // see: A pick's card draws the setups like it under its rule beneath the rule's record, and the score's rank only once the score passed on its index
    const string CreateLoopModels = @"
        CREATE TABLE loop_model (
            run_id          TEXT    NOT NULL,
            index_code      TEXT    NOT NULL,
            family          TEXT    NOT NULL,
            year            INTEGER NOT NULL,
            learned_from    TEXT    NOT NULL,
            learned_before  TEXT    NOT NULL,
            setups          INTEGER NOT NULL,
            readings        TEXT    NOT NULL,
            parameters      TEXT    NOT NULL,
            hash            TEXT    NOT NULL,
            pin             TEXT    NOT NULL,
            words           TEXT    NOT NULL,
            PRIMARY KEY (run_id, family, year)
        ) STRICT;

        ALTER TABLE decision_card ADD COLUMN score_rank INTEGER;
        ALTER TABLE decision_card ADD COLUMN similar TEXT;
    ";

    // Each reading's spread over a family's finished listings in a tester run: the units holding it, the winners and the
    // losers among them, each side's median and the mean edge of each tenth in the reading's order.
    // see: Winners against losers proposes a condition only where it beats a within-night shuffle of its own search
    const string CreateLoopReadings = @"
        CREATE TABLE loop_reading (
            run_id          TEXT    NOT NULL,
            index_code      TEXT    NOT NULL,
            family          TEXT    NOT NULL,
            reading         TEXT    NOT NULL,
            units           INTEGER NOT NULL,
            winners         INTEGER NOT NULL,
            losers          INTEGER NOT NULL,
            winners_median  REAL,
            losers_median   REAL,
            deciles         TEXT    NOT NULL,
            PRIMARY KEY (run_id, index_code, family, reading)
        ) STRICT;
    ";

    // The exit a family rule's trade was kept under, none for the rule's own; the autopsy's finding a proposal states;
    // and the autopsy's figures of a family's finished trades, one row a run, family and figure.
    // see: Every engine's settings hooks land together and all default off, so the families' pins move once
    // see: The trade autopsy proposes exits of a fixed menu, each tested as the procedure that chose it
    const string CreateLoopFindings = @"
        ALTER TABLE family_trade ADD COLUMN exit INTEGER;
        ALTER TABLE index_rule_trade ADD COLUMN exit INTEGER;
        ALTER TABLE loop_proposal ADD COLUMN finding TEXT;

        CREATE TABLE loop_finding (
            run_id      TEXT    NOT NULL,
            index_code  TEXT    NOT NULL,
            family      TEXT    NOT NULL,
            figure      TEXT    NOT NULL,
            value       REAL,
            trades      INTEGER NOT NULL,
            words       TEXT    NOT NULL,
            PRIMARY KEY (run_id, index_code, family, figure)
        ) STRICT;
    ";

    // The walk-forward tester's record: one row a run on an index, one a proposal it tested with its verdict, and one a
    // proposal's test year with the setting its fold chose and each side's units and total edge after costs.
    // see: A proposal passes the tester on a block sign-flip test of its total edge after costs against the current rule, corrected within a run and held to a fixed bar across runs
    const string CreateLoopTests = @"
        CREATE TABLE loop_run (
            run_id      TEXT    NOT NULL PRIMARY KEY,
            month       TEXT    NOT NULL,
            index_code  TEXT    NOT NULL,
            through     TEXT    NOT NULL,
            started_at  TEXT    NOT NULL,
            ended_at    TEXT    NOT NULL,
            folds       INTEGER NOT NULL
        ) STRICT;

        CREATE TABLE loop_proposal (
            run_id         TEXT    NOT NULL,
            index_code     TEXT    NOT NULL,
            family         TEXT    NOT NULL,
            proposal       TEXT    NOT NULL,
            words          TEXT,
            current_words  TEXT    NOT NULL,
            unit           TEXT    NOT NULL,
            units          INTEGER NOT NULL,
            blocks         INTEGER NOT NULL,
            adjusted       REAL,
            gate           INTEGER NOT NULL,
            stable         INTEGER NOT NULL,
            counted        INTEGER NOT NULL,
            better         INTEGER NOT NULL,
            trimmed        REAL,
            counts         INTEGER NOT NULL,
            detectable     REAL,
            stable_folds   INTEGER NOT NULL,
            passed         INTEGER NOT NULL,
            PRIMARY KEY (run_id, index_code, family, proposal)
        ) STRICT;

        CREATE TABLE loop_test (
            run_id          TEXT    NOT NULL,
            index_code      TEXT    NOT NULL,
            family          TEXT    NOT NULL,
            proposal        TEXT    NOT NULL,
            year            INTEGER NOT NULL,
            complete        INTEGER NOT NULL,
            chosen          TEXT,
            current_units   INTEGER NOT NULL,
            proposed_units  INTEGER NOT NULL,
            current_total   REAL    NOT NULL,
            proposed_total  REAL    NOT NULL,
            PRIMARY KEY (run_id, index_code, family, proposal, year)
        ) STRICT;
    ";

    // The SEC's facts as first filed, one row a filer, concept and period, which the night's filings refresh and the
    // whole refresh write and the ledger reads; one row a filer each time its facts were asked for; one row a day of
    // the archive's daily index the refresh has read; and the five business readings a setup reads from the facts.
    // see: The SEC's facts are stored as first filed in a table the night reads, and a setup's business readings read those filed before its session
    // see: The night refreshes the facts of the members that filed since its last read of the archive's daily index, after the close under its own limit
    const string CreateFiledFacts = @"
        CREATE TABLE filed_fact (
            cik           TEXT NOT NULL,
            concept       TEXT NOT NULL,
            period_start  TEXT NOT NULL,
            period_end    TEXT NOT NULL,
            dollars       TEXT NOT NULL,
            filed         TEXT NOT NULL,
            form          TEXT NOT NULL,
            accession     TEXT NOT NULL,
            run_id        TEXT NOT NULL,
            PRIMARY KEY (cik, concept, period_start, period_end)
        ) STRICT;

        CREATE TABLE filed_fact_pull (
            cik        TEXT    NOT NULL,
            pulled_at  TEXT    NOT NULL,
            run_id     TEXT    NOT NULL,
            accession  TEXT,
            stored     INTEGER NOT NULL,
            PRIMARY KEY (cik, pulled_at)
        ) STRICT;

        CREATE TABLE filing_day (
            day        TEXT    NOT NULL PRIMARY KEY,
            run_id     TEXT    NOT NULL,
            read_at    TEXT    NOT NULL,
            posted     INTEGER NOT NULL,
            filings    INTEGER NOT NULL,
            members    INTEGER NOT NULL,
            refreshed  INTEGER NOT NULL
        ) STRICT;

        ALTER TABLE setup ADD COLUMN revenue_growth REAL;
        ALTER TABLE setup ADD COLUMN growth_change REAL;
        ALTER TABLE setup ADD COLUMN gross_margin_change REAL;
        ALTER TABLE setup ADD COLUMN operating_margin_change REAL;
        ALTER TABLE setup ADD COLUMN cash_over_income REAL;

        CREATE TABLE ledger_summary (
            index_code      TEXT    NOT NULL,
            family          TEXT    NOT NULL,
            year            INTEGER NOT NULL,
            setups          INTEGER NOT NULL,
            live_passes     INTEGER NOT NULL,
            night_rows      INTEGER NOT NULL,
            picked          INTEGER NOT NULL,
            settled         INTEGER NOT NULL,
            result_mean     REAL,
            edge_mean       REAL,
            result_deciles  TEXT,
            edge_deciles    TEXT,
            refreshed_at    TEXT    NOT NULL,
            PRIMARY KEY (index_code, family, year)
        ) STRICT;
    ";

    // The setup ledger: one row a member-session a family's loose gates pass on an index, with the live rule's own
    // pass beside it, its plan as prices, its readings as they stood, and what its path came to under the plan's exit
    // against the same plan on every member that night; and one row a family, index and session with the members
    // the gates were read over. A reading the inputs do not reach is null and never nought.
    // see: A setup is every member-session a family's loose gates pass, and its readings are defined once and read as they stood
    // see: The bars the fetcher drops are kept in a table of their own that no night reads, and a setup is stored as its anchor
    const string CreateLedger = @"
        CREATE TABLE setup (
            index_code               TEXT    NOT NULL,
            family                   TEXT    NOT NULL,
            ticker                   TEXT    NOT NULL,
            session_date             TEXT    NOT NULL,
            rule                     TEXT    NOT NULL,
            live_pass                INTEGER NOT NULL,
            picked                   INTEGER,
            entry                    TEXT    NOT NULL,
            stop                     TEXT    NOT NULL,
            target                   TEXT,
            trail                    TEXT,
            cap                      INTEGER NOT NULL,
            risk_moves               REAL,
            close_over_twenty        REAL,
            close_over_fifty        REAL,
            close_over_long       REAL,
            fifty_over_long       REAL,
            move_share               REAL,
            rsi                      REAL,
            rsi_up                   REAL,
            volume_ratio             REAL,
            return_quarter               REAL,
            return_half_year              REAL,
            return_twelve_less_one   REAL,
            strength                 REAL,
            strength_twelve_less_one REAL,
            high_ratio               REAL,
            since_high               REAL,
            depth                    REAL,
            dry_up                   REAL,
            gap_down                 REAL,
            rsi_low                  REAL,
            tightness                REAL,
            liquidity                REAL,
            earnings_sessions        REAL,
            surprise_sessions        REAL,
            surprise_percent         REAL,
            reward_to_risk           REAL,
            freshness                REAL,
            band_strength            REAL,
            volume_multiple          REAL,
            range_ratio              REAL,
            reaction_moves           REAL,
            breadth                  REAL,
            highs_less_lows          REAL,
            index_over_long       REAL,
            vix                      REAL,
            vix_change               REAL,
            mid_over_large           REAL,
            small_over_large         REAL,
            credit_over_fifty       REAL,
            profit                   REAL,
            coverage                 REAL,
            result                   REAL,
            benchmark                REAL,
            cost                     REAL,
            edge                     REAL,
            edge_after_cost          REAL,
            sessions                 INTEGER,
            end                      TEXT    NOT NULL,
            ended_on                 TEXT,
            settled                  INTEGER NOT NULL,
            source                   TEXT    NOT NULL,
            pin                      TEXT    NOT NULL,
            PRIMARY KEY (index_code, family, ticker, session_date)
        ) STRICT;

        CREATE INDEX setup_open ON setup (index_code, settled, session_date);

        CREATE TABLE setup_night (
            index_code      TEXT    NOT NULL,
            family          TEXT    NOT NULL,
            session_date    TEXT    NOT NULL,
            members         INTEGER NOT NULL,
            setups          INTEGER NOT NULL,
            live_passes     INTEGER NOT NULL,
            source          TEXT    NOT NULL,
            PRIMARY KEY (index_code, family, session_date)
        ) STRICT;
    ";

    // The bars the fetcher drops as they fall out of the year it keeps, copied here in the transaction that drops them
    // and never read by a night: a setup's path, replayed from them under any exit from the anchor the setup stores.
    // see: The bars the fetcher drops are kept in a table of their own that no night reads, and a setup is stored as its anchor
    const string CreateKeptBar = @"
        CREATE TABLE kept_bar (
            ticker        TEXT    NOT NULL,
            session_date  TEXT    NOT NULL,
            open          TEXT    NOT NULL,
            high          TEXT    NOT NULL,
            low           TEXT    NOT NULL,
            close         TEXT    NOT NULL,
            volume        INTEGER NOT NULL,
            source        TEXT    NOT NULL,
            observed_at   TEXT    NOT NULL,
            raw_close     TEXT,
            kept_on       TEXT    NOT NULL,
            PRIMARY KEY (ticker, session_date)
        ) STRICT;
    ";

    // The cards' rule rows: one row a standing rule, index and night, live or variant, with whether the night
    // evaluated it, how many it listed with zeros, how many members passed each of its gates and every gate before,
    // its empty stretch and the mark the stretch is read against, written by the night or replayed from the pulled
    // history; one row a pick of a rule whose list no other table keeps, the swing filter's variants, with its plan,
    // the figures that listed it and its end as the walk ends it; and one row a member forming a breakout under a
    // breakout rule on a night, at most the stated rows a rule with the whole count on each.
    // see: A variant's picks are shown on its card when chosen and its results only under its tests
    // see: The forming list advises and never lists a stock
    // see: A card's stretch line counts its mark over past empty nights and draws none under thirty completed stretches
    const string CreateRuleCards = @"
        CREATE TABLE rule_night (
            index_code      TEXT NOT NULL,
            session_date    TEXT NOT NULL,
            family          TEXT NOT NULL,
            rule            TEXT NOT NULL,
            evaluated       INTEGER NOT NULL,
            listed          INTEGER NOT NULL,
            gates           TEXT,
            stretch         INTEGER,
            mark            INTEGER,
            flagged         INTEGER NOT NULL,
            completed       INTEGER,
            sessions        INTEGER,
            source          TEXT NOT NULL,
            PRIMARY KEY (index_code, session_date, family, rule)
        ) STRICT;

        CREATE TABLE rule_pick (
            index_code      TEXT NOT NULL,
            rule            TEXT NOT NULL,
            ticker          TEXT NOT NULL,
            session_date    TEXT NOT NULL,
            family          TEXT NOT NULL,
            place           INTEGER NOT NULL,
            entry           TEXT NOT NULL,
            stop            TEXT,
            target          TEXT,
            reward_to_risk  REAL,
            cap             INTEGER NOT NULL,
            why             TEXT NOT NULL,
            ended_on        TEXT,
            result          REAL,
            PRIMARY KEY (index_code, rule, ticker, session_date)
        ) STRICT;

        CREATE TABLE forming_row (
            index_code      TEXT NOT NULL,
            session_date    TEXT NOT NULL,
            rule            TEXT NOT NULL,
            place           INTEGER NOT NULL,
            ticker          TEXT NOT NULL,
            close           TEXT NOT NULL,
            high            TEXT NOT NULL,
            moves_under     REAL NOT NULL,
            volume_needed   REAL NOT NULL,
            volume          REAL NOT NULL,
            range_ratio     REAL NOT NULL,
            missing         TEXT NOT NULL,
            next_earnings   TEXT,
            forming         INTEGER NOT NULL,
            PRIMARY KEY (index_code, session_date, rule, place)
        ) STRICT;
    ";

    // Each holding of a fund's filing, the shares the fund held and their value in dollars as the filing states them,
    // which give the price the fund valued a share at on the quarter's end; null where the filing states neither, as the
    // schedules before the first N-PORT state none, and on rows a pull wrote before the columns were, filled by the next
    // holdings pull reading the quarter.
    // see: Membership as it stood is rebuilt from the funds' quarterly holdings filed with the SEC, matched by ISIN and then by name
    const string AddPulledHoldingValue = @"
        ALTER TABLE pulled_holding ADD COLUMN shares TEXT;
        ALTER TABLE pulled_holding ADD COLUMN value_usd TEXT;
    ";

    // Each holding of a registered S&P 400 or 600 heavyweights rule's book, its lead over its sector's members' mean at the
    // rebalance that bought it, none for a design (b) rule, which reads no lead over a sector.
    // see: An S&P 400 or 600 heavyweights holding keeps the lead it was bought on
    const string AddIndexRuleHoldingLead = @"
        ALTER TABLE index_heavyweight_rule_holding ADD COLUMN lead REAL;
    ";

    // Each holding of an S&P 400's or 600's book, its lead over its sector's members' mean at the rebalance that bought it,
    // none on a holding bought before the book stored one.
    // see: An S&P 400 or 600 heavyweights holding keeps the lead it was bought on
    const string AddIndexHoldingLead = @"
        ALTER TABLE index_heavyweight_holding ADD COLUMN lead REAL;
    ";

    // Where the night's follower found each taken trade ended, the session, the close it was sold at as the stock traded
    // that day, why and its result from the fill, in multiples of its risk or in per cent for a holding with no stop; the
    // operator's own record, one row an index and family, written by the follower over every trade the operator took, with
    // the rule's own picks listed on the same nights beside it, each followed from the plan's buy; and
    // the dividend part of each answer the quarters fetch stores, the forward rate, the last declared ex-date and each
    // year's count, which a pick's card estimates the next ex-date from where the calendar declares none.
    // see: The operator's own record states its average result once twenty of its trades in a family and index have ended
    // see: A pick's next ex-dividend date is the calendar's where it declares one and the last declared date plus the usual interval where it does not
    const string CreateTakenRecords = @"
        ALTER TABLE taken_trade ADD COLUMN ended_on TEXT;
        ALTER TABLE taken_trade ADD COLUMN end_price TEXT;
        ALTER TABLE taken_trade ADD COLUMN end_reason TEXT;
        ALTER TABLE taken_trade ADD COLUMN result REAL;
        ALTER TABLE decision_card ADD COLUMN hits TEXT;

        CREATE TABLE taken_record (
            index_code  TEXT    NOT NULL,
            family      TEXT    NOT NULL,
            unit        TEXT    NOT NULL,
            won         INTEGER NOT NULL,
            lost        INTEGER NOT NULL,
            ended       INTEGER NOT NULL,
            open_trades INTEGER NOT NULL,
            average     REAL,
            same_nights INTEGER NOT NULL,
            rule_listed INTEGER NOT NULL,
            rule_won    INTEGER NOT NULL,
            rule_lost   INTEGER NOT NULL,
            rule_ended  INTEGER NOT NULL,
            rule_average REAL,
            night       TEXT    NOT NULL,
            PRIMARY KEY (index_code, family)
        ) STRICT;

        CREATE TABLE dividend_reading (
            ticker        TEXT NOT NULL,
            fetched_at    TEXT NOT NULL,
            forward_rate  TEXT,
            last_ex_date  TEXT,
            by_year       TEXT NOT NULL,
            PRIMARY KEY (ticker, fetched_at)
        ) STRICT;
    ";

    // What a pick's card sizes its plan and words its management from, stored by the night beside the lines: the
    // stock's sector, the trail and the cap the rule manages the trade by, the round trip a share at the published
    // table bought and sold at the buy, and, for a book holding with no stop, the most holdings its book can hold. And the operator's own
    // trades, one row a trade taken from a card, written by the read surface's presses alone: what was taken, from which
    // card, at what fill and whether that fill is still the plan's buy awaiting the next session's open, the plan it is
    // managed by, and an exit the operator records. No row holds the account's size, its risk or its cap.
    // see: A taken trade's fill is the next session's open once its bar is stored, and the plan's buy marked provisional until then
    // see: The account settings live in a file of their own under the data root and in nothing the store or the logs hold
    const string CreateTakenTrades = @"
        ALTER TABLE decision_card ADD COLUMN sector TEXT;
        ALTER TABLE decision_card ADD COLUMN trail TEXT;
        ALTER TABLE decision_card ADD COLUMN cap INTEGER;
        ALTER TABLE decision_card ADD COLUMN round_trip TEXT;
        ALTER TABLE decision_card ADD COLUMN book_holdings INTEGER;

        CREATE TABLE taken_trade (
            ticker            TEXT NOT NULL,
            taken_at          TEXT NOT NULL,
            index_code        TEXT NOT NULL,
            family            TEXT NOT NULL,
            night             TEXT NOT NULL,
            sector            TEXT,
            fill              TEXT NOT NULL,
            fill_date         TEXT NOT NULL,
            provisional       INTEGER NOT NULL,
            entered           INTEGER NOT NULL,
            stop              TEXT,
            target            TEXT,
            trail             TEXT,
            cap               INTEGER,
            exit_price        TEXT,
            exit_date         TEXT,
            followed_through  TEXT,
            PRIMARY KEY (ticker, taken_at)
        ) STRICT;
    ";

    // A pick's card on a night: one row an index, night, family and stock the family listed, with the plan's prices, the rule
    // the card names in words, the card's values it was read with, its lines and the rule's record as the card read it; and
    // a rule's record, one row an index and family, replayed at the rule's one setting over the pulled history after each
    // trade's cost, with how many trades had ended by each session held.
    // see: A pick's card advises on the trade and removes no pick, and code computes every figure on it
    // see: A rule's record is replayed at its one setting by the sweep's own code over the pulled history, after costs on every index
    const string CreateDecisionCards = @"
        CREATE TABLE decision_card (
            index_code    TEXT NOT NULL,
            session_date  TEXT NOT NULL,
            family        TEXT NOT NULL,
            ticker        TEXT NOT NULL,
            place         INTEGER NOT NULL,
            entry         TEXT,
            stop          TEXT,
            target        TEXT,
            rule          TEXT NOT NULL,
            settings      TEXT NOT NULL,
            lines         TEXT NOT NULL,
            record        TEXT,
            PRIMARY KEY (index_code, session_date, family, ticker)
        ) STRICT;

        CREATE TABLE rule_record (
            index_code       TEXT NOT NULL,
            family           TEXT NOT NULL,
            rule             TEXT NOT NULL,
            settings         TEXT NOT NULL,
            recorded_at      TEXT NOT NULL,
            first_session    TEXT NOT NULL,
            last_session     TEXT NOT NULL,
            membership       TEXT NOT NULL,
            unit             TEXT NOT NULL,
            trades           INTEGER NOT NULL,
            won              REAL,
            average          REAL,
            median_sessions  INTEGER,
            ended_by         TEXT NOT NULL,
            worst_close      REAL,
            PRIMARY KEY (index_code, family)
        ) STRICT;
    ";

    // Each registered sector heavyweights rule of the S&P 400 and 600 keeping a book of its own: one row a rule and each
    // night it rebalanced on, with what it bought and sold there; and one row a holding, carried every night as the
    // index's own book carries its holdings, and once sold, its sale, why, its result, its size cut's return and its round
    // trip at the published table.
    // see: A rule of the S&P 400's or 600's sector heavyweights keeps a book of its own in either design, read by the index families' step
    const string CreateIndexHeavyweightRules = @"
        CREATE TABLE index_heavyweight_rule_night (
            candidate     TEXT NOT NULL,
            index_code    TEXT NOT NULL,
            session_date  TEXT NOT NULL,
            bought        INTEGER NOT NULL,
            sold          INTEGER NOT NULL,
            PRIMARY KEY (candidate, session_date)
        ) STRICT;

        CREATE TABLE index_heavyweight_rule_holding (
            candidate    TEXT NOT NULL,
            index_code   TEXT NOT NULL,
            ticker       TEXT NOT NULL,
            entered_on   TEXT NOT NULL,
            sector       TEXT NOT NULL,
            entry_close  TEXT NOT NULL,
            growth       REAL NOT NULL,
            cut          TEXT NOT NULL,
            through      TEXT NOT NULL,
            ended_on     TEXT,
            exit_close   TEXT,
            reason       TEXT,
            result       REAL,
            cut_return   REAL,
            cost         REAL,
            PRIMARY KEY (candidate, ticker, entered_on)
        ) STRICT;
    ";

    // Each registered rule of the S&P 400's and 600's swing families keeping its own list: one row a trade, its plan as
    // the night placed it with the stop's distance in typical moves and the reward to risk its benchmark reads, and once
    // it ends its result, its round trip at the published table beside it and never in it, and the benchmark of the
    // same plan on every member of the index that night with how many it averaged.
    // see: A rule of the S&P 400's or 600's swing families is registered as the family on its index and evaluated by their step alone
    const string CreateIndexRuleTrades = @"
        CREATE TABLE index_rule_trade (
            candidate       TEXT NOT NULL,
            index_code      TEXT NOT NULL,
            family          TEXT NOT NULL,
            ticker          TEXT NOT NULL,
            session_date    TEXT NOT NULL,
            place           INTEGER NOT NULL,
            entry           TEXT NOT NULL,
            stop            TEXT NOT NULL,
            target          TEXT,
            trail           TEXT,
            cap             INTEGER NOT NULL,
            risk_moves      REAL,
            reward_to_risk  REAL,
            ended_on        TEXT,
            result          REAL,
            cost            REAL,
            benchmark       REAL,
            members         INTEGER,
            PRIMARY KEY (candidate, ticker, session_date)
        ) STRICT;
    ";

    // Every reading of every member of the three indices as it stood each night, the market switches an S&P 400's or
    // 600's rule may read, the analysts' five rating counts each storing fetch files, each quarter's interest expense as
    // filed with whether the fetch that stored it read one, and the round trip each S&P 500 family trade paid, its
    // result left as it was.
    // see: A 400 or 600 trade pays the published effective spread for its size and price, and its pass tests read the edge after it
    const string CreateMemberReadings = @"
        CREATE TABLE member_reading (
            index_code     TEXT NOT NULL,
            session_date   TEXT NOT NULL,
            ticker         TEXT NOT NULL,
            close          TEXT,
            dollar_volume  TEXT,
            company_value  TEXT,
            cost           REAL,
            cost_double    REAL,
            profit         INTEGER,
            coverage       INTEGER,
            state          TEXT,
            year_high      TEXT,
            nearness       REAL,
            since_high     INTEGER,
            volume_ratio   REAL,
            industry          TEXT,
            industry_month    REAL,
            industry_quarter  REAL,
            peer_surprise     REAL,
            PRIMARY KEY (index_code, session_date, ticker)
        ) STRICT;

        CREATE TABLE switch_reading (
            session_date     TEXT NOT NULL,
            ijh_half_year    REAL,
            ijh_year         REAL,
            ijr_half_year    REAL,
            ijr_year         REAL,
            hyg_average      REAL,
            hyg_change       REAL,
            PRIMARY KEY (session_date)
        ) STRICT;

        ALTER TABLE company ADD COLUMN strong_buy INTEGER;
        ALTER TABLE company ADD COLUMN buy INTEGER;
        ALTER TABLE company ADD COLUMN hold INTEGER;
        ALTER TABLE company ADD COLUMN sell INTEGER;
        ALTER TABLE company ADD COLUMN strong_sell INTEGER;
        ALTER TABLE reported_quarter ADD COLUMN interest_expense TEXT;
        ALTER TABLE reported_quarter ADD COLUMN interest_read INTEGER NOT NULL DEFAULT 0;
        ALTER TABLE family_trade ADD COLUMN cost REAL;
    ";

    // The answer each sweep run states, recorded by the command run after it: the run, the index and the family as the
    // cards name it, the design where a family sweeps more than one, and whether a setting it read met the floors, read by
    // the cards to say a sweep found none.
    // see: No family on any index is set aside or hidden by a test result without the operator's word
    const string CreateSweepAnswer = @"
        CREATE TABLE sweep_answer (
            run           TEXT NOT NULL,
            index_code    TEXT NOT NULL,
            family        TEXT NOT NULL,
            design        TEXT,
            answer        TEXT NOT NULL,
            recorded_at   TEXT NOT NULL,
            PRIMARY KEY (run)
        ) STRICT;
    ";

    // The S&P 400's and 600's provisional rules as the night reads them with the sweep's own code over its year of
    // bars, each index's rows apart from the S&P 500's: what each night read for an index, every member's answer under
    // each family, the page's list for the index, each listed trade with its result before and after its cost, and the
    // sector heavyweights' book within the index; and on the S&P 500's list, the index whose trade holds a stock back
    // where it is an S&P 400's or 600's.
    // see: The 400's and 600's provisional picks are computed on the night by the sweep's own code into tables of their own
    const string CreateIndexFamilies = @"
        ALTER TABLE family_pick ADD COLUMN held_index TEXT;

        CREATE TABLE index_family_night (
            index_code    TEXT NOT NULL,
            session_date  TEXT NOT NULL,
            members       INTEGER NOT NULL,
            breadth       REAL,
            market_open   INTEGER NOT NULL,
            settings      TEXT NOT NULL,
            rebalanced    INTEGER NOT NULL,
            fault         TEXT,
            PRIMARY KEY (index_code, session_date)
        ) STRICT;

        CREATE TABLE index_family_result (
            index_code    TEXT NOT NULL,
            session_date  TEXT NOT NULL,
            ticker        TEXT NOT NULL,
            family        TEXT NOT NULL,
            passed        INTEGER NOT NULL,
            place         INTEGER,
            entry         TEXT,
            stop          TEXT,
            target        TEXT,
            trail         TEXT,
            cap           INTEGER,
            order_by      REAL,
            reason        TEXT,
            PRIMARY KEY (index_code, session_date, ticker, family)
        ) STRICT;

        CREATE TABLE index_family_pick (
            index_code    TEXT NOT NULL,
            session_date  TEXT NOT NULL,
            ticker        TEXT NOT NULL,
            family        TEXT NOT NULL,
            state         TEXT NOT NULL,
            place         INTEGER,
            also          TEXT NOT NULL,
            held_index    TEXT,
            held_family   TEXT,
            held_night    TEXT,
            PRIMARY KEY (index_code, session_date, ticker, family)
        ) STRICT;

        CREATE TABLE index_family_trade (
            index_code    TEXT NOT NULL,
            family        TEXT NOT NULL,
            ticker        TEXT NOT NULL,
            session_date  TEXT NOT NULL,
            place         INTEGER NOT NULL,
            entry         TEXT NOT NULL,
            stop          TEXT NOT NULL,
            target        TEXT,
            trail         TEXT,
            cap           INTEGER NOT NULL,
            ended_on      TEXT,
            result        REAL,
            cost          REAL,
            benchmark     REAL,
            members       INTEGER,
            PRIMARY KEY (index_code, family, ticker, session_date)
        ) STRICT;

        CREATE TABLE index_heavyweight_holding (
            index_code    TEXT NOT NULL,
            ticker        TEXT NOT NULL,
            entered_on    TEXT NOT NULL,
            sector        TEXT NOT NULL,
            entry_close   TEXT NOT NULL,
            growth        REAL NOT NULL,
            cut           TEXT NOT NULL,
            through       TEXT NOT NULL,
            ended_on      TEXT,
            exit_close    TEXT,
            reason        TEXT,
            result        REAL,
            cut_return    REAL,
            cost          REAL,
            PRIMARY KEY (index_code, ticker, entered_on)
        ) STRICT;
    ";

    // The S&P 400's and 600's funds' quarter-end holdings as the SEC holds their filings, one row a snapshot and one a
    // holding of common stock, each matched to the provider's code by ISIN, by name or by neither, marked by the pull that
    // wrote it, removed whole by that pull and read by no night: membership as it stood, a name held from the first
    // snapshot holding it to the last.
    // see: Membership as it stood is rebuilt from the funds' quarterly holdings filed with the SEC, matched by ISIN and then by name
    const string CreatePulledHoldings = @"
        CREATE TABLE pulled_snapshot (
            index_code  TEXT NOT NULL,
            period      TEXT NOT NULL,
            accession   TEXT NOT NULL,
            filed       TEXT NOT NULL,
            holdings    INTEGER NOT NULL,
            equity      INTEGER NOT NULL,
            pull        TEXT NOT NULL,
            PRIMARY KEY (index_code, period)
        ) STRICT;

        CREATE TABLE pulled_holding (
            index_code  TEXT NOT NULL,
            period      TEXT NOT NULL,
            holding     TEXT NOT NULL,
            name        TEXT NOT NULL,
            cusip       TEXT,
            isin        TEXT,
            ticker      TEXT,
            matched_by  TEXT,
            pull        TEXT NOT NULL,
            PRIMARY KEY (index_code, period, holding)
        ) STRICT;
    ";

    // Each pulled company's quarterly income as its filer filed it, one row a quarter, marked by the companies pull that
    // wrote it, removed whole by that pull and read by no night: the net income the S&P 400's and 600's profit gate sums
    // and the operating income and interest expense their coverage reads, each as it stood on the day it was filed.
    // see: The 400 and 600 rules start provisional with liquidity floors and a profit gate before any testing
    const string CreatePulledIncome = @"
        CREATE TABLE pulled_income (
            ticker            TEXT NOT NULL,
            period_end        TEXT NOT NULL,
            filing_date       TEXT NOT NULL,
            net_income        TEXT,
            operating_income  TEXT,
            interest_expense  TEXT,
            pull              TEXT NOT NULL,
            PRIMARY KEY (ticker, period_end)
        ) STRICT;
    ";

    // Today's members of the S&P 400 and the S&P 600 as the indices' fundamentals answers list them, one row a member,
    // marked by the pull that wrote it, removed whole by that pull and read by no night: the names the wider universe's
    // first test pulls the history of, which hold survivors alone since the answers carry no span of membership.
    // see: A wider universe is tested first on today's members, and widened only where a family's edge improves even so and holds on membership as it stood
    const string CreatePulledMembers = @"
        CREATE TABLE pulled_member (
            index_code  TEXT NOT NULL,
            ticker      TEXT NOT NULL,
            exchange    TEXT NOT NULL,
            name        TEXT,
            sector      TEXT,
            industry    TEXT,
            pull        TEXT NOT NULL,
            PRIMARY KEY (index_code, ticker)
        ) STRICT;
    ";

    // Each registered sector heavyweights rule's own book, read and held as the page's book is, the rule it belongs to
    // first in each key: every sector's largest companies at each of the rule's rebalances, and each holding the rule
    // kept, with its growth and its size cut's carried each night and, once it ends, its result and the cut's return.
    // Every book reads each ranked company's beta where the night could read one, a statistic. And each member's
    // analysts' estimate for its current fiscal year as the night asked for it, a member a rule reading them passed on
    // everything else, one row a member a night, its estimates money per share as text.
    // see: Each registered sector heavyweights rule keeps a book of its own beside the page's, its holdings scored in percent against their size cut
    // see: The night asks for the estimates of each member a rule reading them passes on everything else, once a member a night
    const string CreateRuleBooks = @"
        ALTER TABLE heavyweight_night ADD COLUMN beta REAL;

        CREATE TABLE heavyweight_rule_night (
            candidate      TEXT    NOT NULL,
            session_date   TEXT    NOT NULL,
            sector         TEXT    NOT NULL,
            place          INTEGER NOT NULL,
            ticker         TEXT    NOT NULL,
            company        TEXT    NOT NULL,
            company_value  TEXT    NOT NULL,
            look_back      REAL,
            sector_return  REAL,
            lead           REAL,
            trend          INTEGER NOT NULL,
            leader         INTEGER NOT NULL,
            beta           REAL,
            PRIMARY KEY (candidate, session_date, sector, place)
        ) STRICT;

        CREATE TABLE heavyweight_rule_holding (
            candidate    TEXT NOT NULL,
            ticker       TEXT NOT NULL,
            entered_on   TEXT NOT NULL,
            sector       TEXT NOT NULL,
            company      TEXT NOT NULL,
            entry_close  TEXT NOT NULL,
            growth       REAL NOT NULL,
            cut          TEXT NOT NULL,
            through      TEXT NOT NULL,
            ended_on     TEXT,
            exit_close   TEXT,
            reason       TEXT,
            result       REAL,
            cut_return   REAL,
            PRIMARY KEY (candidate, ticker, entered_on)
        ) STRICT;

        CREATE TABLE estimate_reading (
            ticker             TEXT NOT NULL,
            session_date       TEXT NOT NULL,
            year_end           TEXT,
            current_estimate   TEXT,
            days_ago_estimate  TEXT,
            not_read           TEXT,
            run_id             TEXT NOT NULL,
            PRIMARY KEY (ticker, session_date)
        ) STRICT;
    ";

    // One member's answer under one setup family on one night, for every family but the pullback, whose
    // answers are the swing filter's own rows. `gates` is the family's gates in order with their reasons
    // and values, the form `gate_result` keeps its own in. The trade's prices are `TEXT`; `order_by`, the
    // figure the family's order reads, is a statistic and `REAL`. `place` is the row's place among the
    // names the family passed, in its own order. A row that passed is one of the family's trades and is
    // kept; one that did not is dropped once its session is older than the bars the store keeps.
    // see: Every computed table's writer is its own deleter
    // see: Tonight's page is drawn from setup families, each a rule of its own listing at most five a night
    const string CreateFamilyResult = @"
        CREATE TABLE family_result (
            session_date TEXT    NOT NULL,
            ticker       TEXT    NOT NULL,
            family       TEXT    NOT NULL,
            passed       INTEGER NOT NULL,
            missed       INTEGER NOT NULL,
            place        INTEGER,
            entry        TEXT,
            stop         TEXT,
            target       TEXT,
            order_by     REAL,
            exclusions   TEXT    NOT NULL,
            gates        TEXT    NOT NULL,
            PRIMARY KEY (session_date, ticker, family)
        ) STRICT;
    ";

    // The page's list on a night, drawn from the setup families. `family_night` holds one row a session the
    // families drew, with the families on the page that night in the page's order, so a session they drew
    // and listed nothing on is told from one drawn before them. `family_pick` holds one row a stock under
    // each family that passed it, listed with its place down the page and the other families it qualified
    // under, or held back with why, a trade still open naming the family and the night that listed it.
    // see: Tonight's page is drawn from setup families, each a rule of its own listing at most five a night
    // see: A stock holds one trade across every swing family, and one qualifying under two is listed once under the first in the page's order
    const string CreateFamilyPick = @"
        CREATE TABLE family_night (
            session_date TEXT NOT NULL PRIMARY KEY,
            families     TEXT NOT NULL
        ) STRICT;

        CREATE TABLE family_pick (
            session_date TEXT NOT NULL,
            ticker       TEXT NOT NULL,
            family       TEXT NOT NULL,
            state        TEXT NOT NULL CHECK (state IN ('listed', 'under another', 'open trade', 'past five')),
            place        INTEGER,
            also         TEXT NOT NULL,
            held_family  TEXT,
            held_night   TEXT,
            PRIMARY KEY (session_date, ticker, family)
        ) STRICT;
    ";

    // One completed block of one version's record, frozen when the block completed.
    //
    // The record is read at 8 blocks, 504 sessions, and again at 16, 1,008, while the scores
    // and the labels a block is computed from are kept one year. A record recomputed when it
    // is read could hold 3 whole blocks at most against a floor of 8, so a verdict would be
    // withheld for as long as the window stayed open. This table is what the retention does
    // not reach, and it has no deleter for that reason rather than by omission.
    //
    // The two excesses and the two counts are what a difference, a setup count and an excess
    // in points are read from; the two pairs of sums are what a design effect and a smallest
    // excess are read from. `origin` is the session block 0 is counted from, written with the
    // first block and never recomputed, because retention moving the earliest scored session
    // forward would re-cut the blocks under a record already being read.
    //
    // `opened_at` is in the key because a version closed and opened again under one name is
    // two windows, and scores belong to a window rather than to a name.
    // see: A version's record is read from the blocks frozen as each completed
    // see: A version's record belongs to the window its scores were written under and never to the version's name
    const string CreateVersionBlock = @"
        CREATE TABLE version_block (
            rule                 TEXT NOT NULL,
            version              TEXT NOT NULL,
            opened_at            TEXT NOT NULL,
            block                INTEGER NOT NULL,
            origin               TEXT NOT NULL,
            version_excess       REAL NOT NULL,
            version_setups       INTEGER NOT NULL,
            live_excess          REAL NOT NULL,
            live_setups          INTEGER NOT NULL,
            version_null_sum     REAL NOT NULL,
            version_null_spread  REAL NOT NULL,
            live_null_sum        REAL NOT NULL,
            live_null_spread     REAL NOT NULL,
            frozen_at            TEXT NOT NULL,
            PRIMARY KEY (rule, version, opened_at, block)
        ) STRICT;
    ";

    // The bar a setup with no edge would have cleared, at the round trip the calibration carries
    // and at the sensitivity shown beside it, what the plan put at risk from the close it was
    // entered at, and whether the session it resolved on was one the name reported on.
    //
    // On the row rather than computed when a record is read, because the bar is simulated under the
    // name's trailing volatility and the calendar dates a print for a year, and both are dropped
    // behind a record that is read over four years of nights.
    //
    // Nullable, and a row decided before this carries none: a setup whose bar nothing computed is
    // one no record counts, rather than one counted against the bar its plan stated.
    // see: A setup's null win probability is calibrated from its own plan, and its planned break-even is shown beside it
    // see: The calibrated null carries a round trip of ten basis points, and thirty is shown as a sensitivity
    const string AddCalibratedBar = @"
        ALTER TABLE forward_return ADD COLUMN null_win REAL;
        ALTER TABLE forward_return ADD COLUMN null_win_at_sensitivity REAL;
        ALTER TABLE forward_return ADD COLUMN planned_risk REAL;
        ALTER TABLE forward_return ADD COLUMN on_earnings INTEGER;
    ";

    // The highest strength of the name's bands on the listing's night, which the order tonight's
    // list is compared against reads. On the listing row because the listing is kept and the
    // bands are dropped a year back, and a comparison needing two years of nights would lose its
    // benchmark to the retention rule before it could be read.
    //
    // Nullable, and every row written before it reads as a row that recorded none, which is what
    // it is: the comparison counts the nights that recorded it and no other.
    // see: A listing records the band strength the old order read, and the three orders are compared over the nights that recorded it
    const string AddListingBandStrength = @"
        ALTER TABLE listing ADD COLUMN band_strength INTEGER;
    ";

    // The evidence a window was closed on, written by the close that ends or replaces it.
    // see: A rule version change closes the window with the evidence that produced it and opens its replacement in the same write
    const string AddRuleVersionEvidence = @"
        ALTER TABLE rule_version ADD COLUMN evidence TEXT;
    ";

    // A report somebody asked for, which the worker drains. The read surface writes the ask
    // and the worker writes what came of it, which is one table with two writers and never
    // two writers for one operation.
    //
    // At most one outstanding request per name, which is what a second press is refused
    // against. SQLite states that as a partial index rather than a table constraint, because
    // the uniqueness holds for one value of `state` and not across the column.
    // see: A press writes a request and starts the worker's drain as a process of its own, and every pass waits for the off-peak hours
    const string CreateResearchRequest = @"
        CREATE TABLE research_request (
            ticker       TEXT NOT NULL,
            asked_at     TEXT NOT NULL,
            asked_from   TEXT NOT NULL CHECK (asked_from IN ('list', 'name')),
            lane         TEXT NOT NULL CHECK (lane IN ('local', 'paid')),
            state        TEXT NOT NULL CHECK (state IN ('outstanding', 'writing', 'written', 'refused', 'withdrawn')),
            settled_at   TEXT,
            run_id       TEXT,
            reason       TEXT,
            PRIMARY KEY (ticker, asked_at)
        ) STRICT;

        CREATE UNIQUE INDEX research_request_one_outstanding_per_name
        ON research_request (ticker) WHERE state = 'outstanding';
    ";

    // Each move's group, and the group's median move over the same sessions, on the move row
    // the annotator already owns, so a move and the median it is read against are one row and
    // cannot drift apart. The kind and the name say which group, industry or sector, the
    // members how many others it holds and the counted how many held both closes; the median
    // is null where none did.
    // see: A large move is shown beside its group's median move over the same sessions
    const string AddMoveGroupMedian = @"
        ALTER TABLE move ADD COLUMN group_kind TEXT;
        ALTER TABLE move ADD COLUMN group_name TEXT;
        ALTER TABLE move ADD COLUMN group_members INTEGER;
        ALTER TABLE move ADD COLUMN group_counted INTEGER;
        ALTER TABLE move ADD COLUMN group_median REAL;
    ";

    // Each name's two readings for the peers table, one row per name the store holds bars for,
    // written again every night by the move annotator and deleted by it where the name holds no
    // bars or its series has a gap. The year's high is a price and is text; the distance below it
    // and the return are statistics; the bars are how many the readings were taken over, which
    // is what a return the name holds too few bars for says instead. The group is the one the
    // name's moves are read against, so the peers table lists members the medians were taken
    // over.
    // see: Peers are shown by price alone, ten at most with the name's industry first and then the members whose daily moves followed it most closely
    const string CreatePeerReading = @"
        CREATE TABLE peer_reading (
            ticker         TEXT NOT NULL PRIMARY KEY,
            session_date   TEXT NOT NULL,
            group_kind     TEXT NOT NULL,
            group_name     TEXT,
            year_high      TEXT NOT NULL,
            below_high_pct REAL NOT NULL,
            return_pct     REAL,
            bars           INTEGER NOT NULL
        ) STRICT;
    ";

    // Each print's earnings reaction, one row per name and print over the calendar's year behind,
    // written again every night by the move annotator from the calendar and the stored bars and
    // deleted by it where a print falls out of either. The estimate and the actual are kept as the
    // provider sent them, as text, and are null where it filed none; the surprise is the provider's
    // and is null beside no estimate; the move is the reaction session's, from the close before it.
    // see: Each print's reaction is read from the nightly calendar and the stored bars, and the earnings drift is the one rule that reads it
    const string CreateEarningsReaction = @"
        CREATE TABLE earnings_reaction (
            ticker           TEXT NOT NULL,
            report_date      TEXT NOT NULL,
            timing           TEXT NOT NULL CHECK (timing IN ('before', 'after', 'unstated')),
            reaction_session TEXT NOT NULL,
            estimate         TEXT,
            actual           TEXT,
            surprise_pct     REAL,
            move_pct         REAL NOT NULL,
            PRIMARY KEY (ticker, report_date)
        ) STRICT;
    ";

    // The night's own request, marked as asked by the night beside the two screens a press
    // comes from. A rebuild rather than an alter, because SQLite cannot change a check a table
    // was created with: every row is copied across whole, and the index refusing a second
    // outstanding request for a name is built again over them.
    // see: The six reports a night are taken in turn across the three indices, one at a time in the page's order
    const string RequestAskedByTheNight = @"
        CREATE TABLE research_request_rebuilt (
            ticker       TEXT NOT NULL,
            asked_at     TEXT NOT NULL,
            asked_from   TEXT NOT NULL CHECK (asked_from IN ('list', 'name', 'night')),
            lane         TEXT NOT NULL CHECK (lane IN ('local', 'paid')),
            state        TEXT NOT NULL CHECK (state IN ('outstanding', 'writing', 'written', 'refused', 'withdrawn')),
            settled_at   TEXT,
            run_id       TEXT,
            reason       TEXT,
            PRIMARY KEY (ticker, asked_at)
        ) STRICT;

        INSERT INTO research_request_rebuilt (ticker, asked_at, asked_from, lane, state, settled_at, run_id, reason)
        SELECT ticker, asked_at, asked_from, lane, state, settled_at, run_id, reason FROM research_request;

        DROP TABLE research_request;

        ALTER TABLE research_request_rebuilt RENAME TO research_request;

        CREATE UNIQUE INDEX research_request_one_outstanding_per_name
        ON research_request (ticker) WHERE state = 'outstanding';
    ";

    // The company's name, on the membership row, from the span the index feed lists. Nullable
    // for a span stating none, and coalesced by the loader as the sector is.
    // see: The membership row carries the company's name the index feed states
    const string AddMembershipName = @"
        ALTER TABLE membership ADD COLUMN name TEXT;
    ";

    // The bar each plan set for itself, beside the setup it belongs to.
    //
    // An alter rather than a rebuild, because the column is nullable and carries
    // no key: every stored row reads as a row whose break-even has not been
    // computed yet, which it has not, and the filler writes it on every setup it
    // scores from then on.
    //
    // `REAL` rather than `TEXT`, because a break-even is a share of a plan's range
    // and not a price. The prices it is computed from are decimal in code and TEXT
    // in storage, and the crossing between them happens once, in a helper named
    // for it.
    // see: A condition is judged against the break-even its own plan demands
    const string AddForwardReturnBreakEven = @"
        ALTER TABLE forward_return ADD COLUMN break_even REAL;
    ";

    // The provider carries no join date for 145 of the 822 spans it returns,
    // and two of those are current members. Dropping them takes two real names
    // out of the index and out of everything computed from it; writing a date
    // nobody has is the guess this corpus refuses. So the column admits null.
    //
    // A rebuild rather than an alter, because the unknown cannot sit in a
    // primary key: SQLite treats NULLs as distinct, so a second night would
    // insert a second row for the same name rather than conflicting with the
    // first. The uniqueness moves to an expression index that folds the unknown
    // to a value, which is the one place a sentinel belongs: inside the index
    // that enforces uniqueness, and never in the column a query reads.
    const string JoinedMayBeUnknown = @"
        CREATE TABLE membership_rebuilt (
            index_code   TEXT NOT NULL,
            ticker       TEXT NOT NULL,
            joined       TEXT,
            ""left""     TEXT,
            observed_at  TEXT NOT NULL
        ) STRICT;

        INSERT INTO membership_rebuilt (index_code, ticker, joined, ""left"", observed_at)
        SELECT index_code, ticker, joined, ""left"", observed_at FROM membership;

        DROP TABLE membership;

        ALTER TABLE membership_rebuilt RENAME TO membership;

        CREATE UNIQUE INDEX membership_span
            ON membership (index_code, ticker, IFNULL(joined, ''));
    ";

    // The readings the swing filter's gates are measured against, one row per member per night,
    // and the night's breadth, one row per night. Every figure but the recent high is a statistic
    // and is `REAL`; the high is a price and is `TEXT`.
    // see: Every computed table's writer is its own deleter
    const string CreateSwingReadings = @"
        CREATE TABLE swing_reading (
            ticker             TEXT    NOT NULL,
            session_date       TEXT    NOT NULL,
            bars               INTEGER NOT NULL,
            return_short          REAL,
            return_long         REAL,
            place_short           REAL,
            place_long          REAL,
            strength           REAL,
            recent_high            TEXT,
            high_session       TEXT,
            pullback_sessions  INTEGER,
            depth              REAL,
            dry_up             REAL,
            tightness          REAL,
            note               TEXT,
            PRIMARY KEY (ticker, session_date)
        ) STRICT;

        CREATE TABLE market_reading (
            session_date         TEXT    NOT NULL PRIMARY KEY,
            members              INTEGER NOT NULL,
            counted              INTEGER NOT NULL,
            above                INTEGER NOT NULL,
            breadth              REAL,
            counted_context           INTEGER NOT NULL,
            above_context             INTEGER NOT NULL,
            breadth_context           REAL,
            volume_counted       INTEGER NOT NULL,
            median_volume_ratio  REAL
        ) STRICT;
    ";

    // Every member's swing filter result for every night, kept whole, because the near misses a gate
    // is judged by are read over the years the edge clock needs and nothing can compute them again
    // once the readings behind them have been dropped. The prices of the swing trade's own plan are
    // `TEXT`; every ratio and distance is a statistic and `REAL`. `gates` is the five gates' answers
    // with their reasons and values, and the notes on what the night could not count.
    //
    // And the filter's versions: each a set of the settings the gates read, opened by the operator's
    // acceptance of a shape proposal and closed by the next. None is open until the first is accepted,
    // and the filter reads section 17's proposed values until then.
    // see: Every computed table's writer is its own deleter
    // see: A shape acceptance restarts the live filter's edge clock, and after one acceptance while the list is live each further one states the blocks it restarts
    const string CreateGateResults = @"
        CREATE TABLE gate_result (
            ticker                 TEXT    NOT NULL,
            session_date           TEXT    NOT NULL,
            version                TEXT    NOT NULL,
            code                   TEXT    NOT NULL,
            market                 INTEGER NOT NULL,
            trend                  INTEGER NOT NULL,
            setup                  INTEGER NOT NULL,
            family                 TEXT,
            trigger_pass           INTEGER NOT NULL,
            trigger_event          INTEGER,
            trade                  INTEGER NOT NULL,
            ladder_reward_to_risk  REAL,
            ladder_stop_moves      REAL,
            swing_entry            TEXT,
            swing_stop             TEXT,
            swing_target           TEXT,
            swing_reward_to_risk   REAL,
            swing_stop_moves       REAL,
            exclusions             TEXT    NOT NULL,
            passed                 INTEGER NOT NULL,
            rank                   INTEGER,
            strength               REAL,
            band_strength          INTEGER,
            gates                  TEXT    NOT NULL,
            PRIMARY KEY (ticker, session_date)
        ) STRICT;

        CREATE TABLE filter_version (
            version    TEXT NOT NULL PRIMARY KEY,
            settings   TEXT NOT NULL,
            opened_at  TEXT NOT NULL,
            closed_at  TEXT,
            evidence   TEXT NOT NULL
        ) STRICT;
    ";

    // A shape proposal the proposer wrote at a trigger, and the decision on it. The proposer inserts and
    // never decides; the operator's command writes the decision and nothing about the proposal itself.
    // Kept whole, since a rejected proposal is as much the record as an accepted one.
    // see: The shape proposer moves one setting a gate, nearest first, and never applies what it proposes
    const string CreateShapeProposal = @"
        CREATE TABLE shape_proposal (
            id               INTEGER PRIMARY KEY,
            proposed_at      TEXT    NOT NULL,
            session_date     TEXT    NOT NULL,
            version          TEXT    NOT NULL,
            ordinary         INTEGER NOT NULL,
            current_settings TEXT    NOT NULL,
            settings         TEXT    NOT NULL,
            levers           TEXT    NOT NULL,
            list_now         REAL,
            list_proposed    REAL,
            findings         TEXT    NOT NULL,
            decision         TEXT CHECK (decision IS NULL OR decision IN ('accepted', 'rejected')),
            decided_at       TEXT,
            reason           TEXT,
            opened           TEXT
        ) STRICT;
    ";

    // Each standing swing family candidate evaluated over the member's gate inputs in the filter's own
    // stage, and the ones the night could not evaluate with why: the shadow split by stage, the listings
    // stage keeping its own evaluators on its own rows.
    // see: A variant of the swing filter is registered as a whole rule and runs on unchanged when the live settings move
    const string AddGateResultShadow = @"
        ALTER TABLE gate_result ADD COLUMN shadow TEXT;
    ";

    // The rule each evening's list was drawn by, written by the night that drew it, so an evening before the
    // switch reads as listed by the reasons and one from it by the swing filter, whatever code reads it later.
    // see: Tonight's list is the swing filter's with improving businesses drawn first, and an evening is listed and ordered by the rule that listed it
    const string CreateListRule = @"
        CREATE TABLE list_rule (
            session_date TEXT NOT NULL PRIMARY KEY,
            rule         TEXT NOT NULL CHECK (rule IN ('reasons', 'filter'))
        ) STRICT;
    ";

    const string CreateWatchList = @"
        CREATE TABLE watch_list (
            ticker   TEXT NOT NULL PRIMARY KEY,
            added_at TEXT NOT NULL
        ) STRICT;
    ";

    // Whether a press asked for every section to be written again rather than only what is not
    // written or has gone stale, carried on the request so the drain that answers it later runs
    // the pass the press meant. A request written before the column is one that asked for no
    // rewrite, which is what every press before it could ask.
    // see: Nothing expires on a timer
    const string AddResearchRequestRefresh = @"
        ALTER TABLE research_request ADD COLUMN refresh INTEGER NOT NULL DEFAULT 0 CHECK (refresh IN (0, 1));
    ";

    // The parts of a fetch that are as of the fetch rather than as of a filing, one row per fetch.
    // A filing's row is never updated, so a fetch finding no new filing had nowhere to put a price
    // that has moved since; this is where it goes, and the newest copy is read in place of the
    // newest filing row's.
    // see: A regenerated report is written whole by the paid model from the company's figures as they stand on the day it runs, once a name a day
    const string CreateFundamentalsSnapshot = @"
        CREATE TABLE fundamentals_snapshot (
            ticker     TEXT NOT NULL,
            fetched_at TEXT NOT NULL,
            payload    TEXT NOT NULL,
            PRIMARY KEY (ticker, fetched_at)
        ) STRICT;
    ";

    // The history pulled on the operator's command for the sessions before the store's rolling year,
    // held apart from the bar and calendar tables every night reads. Every row carries the run id of
    // the pull that wrote it, which is what removes a pull whole.
    // see: The history pulled before the store's year sits apart from its bars, marked by the pull that wrote it, read by no night and removed whole by that pull
    const string CreatePulledHistory = @"
        CREATE TABLE pulled_bar (
            ticker       TEXT    NOT NULL,
            session_date TEXT    NOT NULL,
            open         TEXT    NOT NULL,
            high         TEXT    NOT NULL,
            low          TEXT    NOT NULL,
            close        TEXT    NOT NULL,
            raw_close    TEXT    NOT NULL,
            volume       INTEGER NOT NULL,
            pull         TEXT    NOT NULL,
            PRIMARY KEY (ticker, session_date)
        ) STRICT;

        CREATE TABLE pulled_earnings (
            ticker     TEXT NOT NULL,
            event_date TEXT NOT NULL,
            timing     TEXT NOT NULL,
            pull       TEXT NOT NULL,
            PRIMARY KEY (ticker, event_date)
        ) STRICT;
    ";

    // Section 10's plan for the swing trade beside the plan at the nearest bands, both entered at the
    // night's close: the stop at the setup band's low edge, or the next support band's below it where
    // that is less than a typical move below the entry, and the target at the lowest low edge of a band
    // two typical moves or more above it. A row written before the column carries the nearest bands'
    // plan alone, which is the plan every candidate standing then read.
    // see: A swing filter row carries both swing plans, each scored from the night's close, and a candidate's setups are scored on the plan its own trade gate reads
    const string AddClearPlan = @"
        ALTER TABLE gate_result ADD COLUMN clear_stop TEXT;
        ALTER TABLE gate_result ADD COLUMN clear_target TEXT;
        ALTER TABLE gate_result ADD COLUMN clear_reward_to_risk REAL;
        ALTER TABLE gate_result ADD COLUMN clear_stop_moves REAL;
    ";

    // The members of a name's group its peers table draws, in the order it draws them, each with whether
    // it shares the name's industry and how closely its daily moves followed the name's, as JSON. A row
    // written before the column holds none, and the page says the night has not chosen them yet rather
    // than drawing the whole group.
    // see: Peers are shown by price alone, ten at most with the name's industry first and then the members whose daily moves followed it most closely
    const string AddPeerPicks = @"
        ALTER TABLE peer_reading ADD COLUMN peers TEXT;
    ";

    // The fields a section's prose was composed from, which the risks are answered as and checked by: a
    // fact that is not listed, a level the facts file does not hold, a kind outside the seven and two risks
    // resting on one fact or one kind are read off them. Null for every other section and on a row written
    // before this migration.
    // see: Each risk is returned as fields and confirmed by a listed fact or an event of one kind, and no two risks share either
    const string AddRiskParts = @"
        ALTER TABLE research_section ADD COLUMN parts TEXT;
    ";

    // The surprises pulled beside the pulled prints, on the operator's ruling of 2026-09-30: for each print the
    // earnings calendar answers over the span, the report date and its timing, the earnings figures as filed,
    // kept as text since nothing computes with them, and the provider's surprise in per cent, a statistic, each
    // row carrying the pull that wrote it and removed whole with it. Read by no night, as the two tables beside
    // it are; the sweep's fifth condition reads it by hand.
    // see: The surprises pulled before the store's year sit beside the pulled prints and are read by no night
    const string CreatePulledSurprise = @"
        CREATE TABLE pulled_surprise (
            ticker            TEXT NOT NULL,
            event_date        TEXT NOT NULL,
            timing            TEXT NOT NULL,
            eps_actual        TEXT,
            eps_estimate      TEXT,
            surprise_percent  REAL,
            pull              TEXT NOT NULL,
            PRIMARY KEY (ticker, event_date)
        ) STRICT;
    ";

    // The index's and the VIX's daily series a market pull stored, one row a series and session, the
    // prices as the provider sent them, kept as text, and the pull that wrote the row, read by no night.
    // see: The index's and the VIX's daily series are pulled beside the pulled bars, marked by their pull and read by no night
    const string CreatePulledMarketBar = @"
        CREATE TABLE pulled_market_bar (
            series       TEXT NOT NULL,
            session_date TEXT NOT NULL,
            open         TEXT NOT NULL,
            high         TEXT NOT NULL,
            low          TEXT NOT NULL,
            close        TEXT NOT NULL,
            pull         TEXT NOT NULL,
            PRIMARY KEY (series, session_date)
        ) STRICT;
    ";

    // The index's and the VIX's daily series as the night fetches them, one row a series and session, the prices
    // as the provider sent them, kept as text, and the night that first stored the row. Insert only: a session a
    // night already holds keeps that night's row, and the table is kept whole.
    // see: The night asks for the market series' daily closes once a series, and keeps them apart from the members' bars
    const string CreateMarketBar = @"
        CREATE TABLE market_bar (
            series       TEXT NOT NULL,
            session_date TEXT NOT NULL,
            open         TEXT NOT NULL,
            high         TEXT NOT NULL,
            low          TEXT NOT NULL,
            close        TEXT NOT NULL,
            run_id       TEXT NOT NULL,
            PRIMARY KEY (series, session_date)
        ) STRICT;
    ";

    // Each company of the history the pulls stored, read by no night: its filer, its GICS classification and the day it
    // was delisted, one row a ticker; its quarterly share counts with the day each balance sheet was filed and the
    // session whose split basis the provider restated them to; its splits; and each figure its filer stated under a
    // revenue concept, one row a filing and period, with the day it was filed. Counts, ratios and dollars are text,
    // the form an exact figure takes. Every row carries the pull that wrote it, which removes the pull whole.
    // see: The pulls behind the heavyweights and the context checks store into tables of their own and are read by no night
    const string CreatePulledCompanies = @"
        CREATE TABLE pulled_company (
            ticker          TEXT NOT NULL,
            cik             TEXT,
            sector          TEXT,
            industry_group  TEXT,
            industry        TEXT,
            sub_industry    TEXT,
            delisted_on     TEXT,
            pull            TEXT NOT NULL,
            PRIMARY KEY (ticker)
        ) STRICT;

        CREATE TABLE pulled_shares (
            ticker         TEXT NOT NULL,
            period_end     TEXT NOT NULL,
            filing_date    TEXT NOT NULL,
            shares         TEXT NOT NULL,
            basis_session  TEXT NOT NULL,
            pull           TEXT NOT NULL,
            PRIMARY KEY (ticker, period_end)
        ) STRICT;

        CREATE TABLE pulled_split (
            ticker      TEXT NOT NULL,
            ex_date     TEXT NOT NULL,
            new_shares  TEXT NOT NULL,
            old_shares  TEXT NOT NULL,
            pull        TEXT NOT NULL,
            PRIMARY KEY (ticker, ex_date)
        ) STRICT;

        CREATE TABLE pulled_revenue (
            cik           TEXT NOT NULL,
            concept       TEXT NOT NULL,
            period_start  TEXT NOT NULL,
            period_end    TEXT NOT NULL,
            accession     TEXT NOT NULL,
            dollars       TEXT NOT NULL,
            filed         TEXT NOT NULL,
            form          TEXT NOT NULL,
            pull          TEXT NOT NULL,
            PRIMARY KEY (cik, concept, period_start, period_end, accession)
        ) STRICT;
    ";

    // The sector heavyweights' data and book. Each storing quarters fetch keeps the shares its balance sheet files
    // beside the quarter, on the fetch's split basis, and the company it answered for: its filer and its GICS
    // classification, one row a fetch. The book keeps, for each rebalance session, every sector's largest companies
    // with their values, returns, leads, trend and whether each was bought; and each holding, its sector and company,
    // its entry and exit, why it ended, and its growth and its size cut's carried each night from that night's closes,
    // which a corporate action's refetch of the year leaves standing. Values and closes are text, growths and returns
    // statistics.
    // see: The sector heavyweights hold the largest companies leading their sectors, rotated on the first session of each month whose stored year holds the closes their readings need
    // see: A heavyweight's result is the product of its daily close ratios since its buy, carried each night
    const string CreateHeavyweights = @"
        ALTER TABLE reported_quarter ADD COLUMN shares TEXT;

        CREATE TABLE company (
            ticker          TEXT NOT NULL,
            fetched_at      TEXT NOT NULL,
            cik             TEXT,
            sector          TEXT,
            industry_group  TEXT,
            industry        TEXT,
            sub_industry    TEXT,
            PRIMARY KEY (ticker, fetched_at)
        ) STRICT;

        CREATE TABLE heavyweight_night (
            session_date   TEXT    NOT NULL,
            sector         TEXT    NOT NULL,
            place          INTEGER NOT NULL,
            ticker         TEXT    NOT NULL,
            company        TEXT    NOT NULL,
            company_value  TEXT    NOT NULL,
            look_back      REAL,
            sector_return  REAL,
            lead           REAL,
            trend          INTEGER NOT NULL,
            leader         INTEGER NOT NULL,
            PRIMARY KEY (session_date, sector, place)
        ) STRICT;

        CREATE TABLE heavyweight_holding (
            ticker       TEXT NOT NULL,
            entered_on   TEXT NOT NULL,
            sector       TEXT NOT NULL,
            company      TEXT NOT NULL,
            entry_close  TEXT NOT NULL,
            growth       REAL NOT NULL,
            cut          TEXT NOT NULL,
            through      TEXT NOT NULL,
            ended_on     TEXT,
            exit_close   TEXT,
            reason       TEXT,
            result       REAL,
            cut_return   REAL,
            PRIMARY KEY (ticker, entered_on)
        ) STRICT;
    ";

    // Each registered family rule's verdict on a member, stored on that member's row of its family beside the
    // live rule's answer; and each trade a registered family rule keeps on its own list, stored the night it
    // is made with the plan it was bought on, and once it ends with its result and the benchmark of the same
    // plan entered on every member that night, each written once.
    // see: A registered family rule is evaluated every night at its own settings and keeps its own list, its trades stored with their benchmark when they end
    const string AddFamilyShadowAndTrades = @"
        ALTER TABLE family_result ADD COLUMN shadow TEXT;

        CREATE TABLE family_trade (
            candidate      TEXT    NOT NULL,
            ticker         TEXT    NOT NULL,
            session_date   TEXT    NOT NULL,
            family         TEXT    NOT NULL,
            place          INTEGER NOT NULL,
            entry          TEXT    NOT NULL,
            stop           TEXT    NOT NULL,
            target         TEXT,
            risk_moves     REAL,
            reward_to_risk REAL,
            cap            INTEGER NOT NULL,
            ended_on       TEXT,
            result         REAL,
            benchmark      REAL,
            members        INTEGER,
            PRIMARY KEY (candidate, ticker, session_date)
        ) STRICT;
    ";

    // The articles the night's one news query brings back, one row per member per article with its text cut
    // and its admissibility judged as it is stored, kept for thirty-one days; and the labels a paid model gave
    // them, one row per article per profile per instruction version, never overwritten, an answer that could
    // not be read kept as unreadable with its cause so it is not paid for again.
    // see: The news labeller is a process of its own the night starts after the close, and its calls and its spend are its own
    // see: A news article is stored once per member with its admissibility judged, and a label is never overwritten
    const string CreateNewsArticlesAndLabels = @"
        CREATE TABLE news_article (
            ticker          TEXT NOT NULL,
            article_id      TEXT NOT NULL,
            link            TEXT NOT NULL,
            title           TEXT NOT NULL,
            source          TEXT NOT NULL,
            published_at    TEXT NOT NULL,
            text            TEXT NOT NULL,
            length          INTEGER NOT NULL,
            admissibility   TEXT NOT NULL,
            session_date    TEXT NOT NULL,
            PRIMARY KEY (ticker, article_id)
        ) STRICT;

        CREATE TABLE news_label (
            ticker               TEXT NOT NULL,
            article_id           TEXT NOT NULL,
            profile              TEXT NOT NULL,
            instruction_version  INTEGER NOT NULL,
            model                TEXT NOT NULL,
            outcome              TEXT NOT NULL,
            cause                TEXT,
            kind                 TEXT,
            direction            TEXT,
            reason               TEXT,
            labelled_at          TEXT NOT NULL,
            run_id               TEXT NOT NULL,
            PRIMARY KEY (ticker, article_id, profile, instruction_version)
        ) STRICT;
    ";

    // A member's reported quarters, one row per fetch per quarter, the asks that fetched them, and the
    // readings every night works out from them.
    //
    // A quarter is keyed on its fetch and not on itself, because the provider restates per-share
    // figures and adjusts its closes as of the day it is asked, so the quarters a reading reads are one
    // fetch's and never two. The figures a fetch works out from the whole answer are kept beside the
    // ones it copies, since they read quarters and closes older than the twelve a fetch keeps. Money
    // and the ratios struck from it are text, which is the storage form money takes. Every table is
    // kept for good: Past picks and a candidate read a night's readings by the night.
    // see: Reported quarters are stored per fetch, so every quarter a reading reads shares one fetch's per-share basis
    // see: A member's reported quarters are fetched on the night after it reports, and asked for again on the five nights after and weekly after that until the quarter is posted
    const string CreateReportedQuarters = @"
        CREATE TABLE reported_quarter (
            ticker               TEXT NOT NULL,
            fetched_at           TEXT NOT NULL,
            session_date         TEXT NOT NULL,
            period_end           TEXT NOT NULL,
            filing_date          TEXT,
            report_date          TEXT,
            revenue              TEXT,
            operating_income     TEXT,
            net_income           TEXT,
            operating_cash_flow  TEXT,
            eps_actual           TEXT,
            eps_estimate         TEXT,
            eps_trailing         TEXT,
            sales_growth         TEXT,
            sales_growth_before  TEXT,
            operating_margin     TEXT,
            margin_year_earlier  TEXT,
            close_after          TEXT,
            close_after_session  TEXT,
            basis_session        TEXT,
            basis_close          TEXT,
            PRIMARY KEY (ticker, fetched_at, period_end)
        ) STRICT;

        CREATE TABLE quarter_ask (
            ticker        TEXT NOT NULL,
            session_date  TEXT NOT NULL,
            asked_at      TEXT NOT NULL,
            reason        TEXT NOT NULL CHECK (reason IN ('fill', 'joined', 'report', 'waiting')),
            awaited       TEXT,
            outcome       TEXT NOT NULL CHECK (outcome IN ('stored', 'not yet posted', 'nothing returned', 'refused')),
            quarters      INTEGER NOT NULL,
            weighted      INTEGER NOT NULL,
            nights        INTEGER NOT NULL,
            next_ask      TEXT,
            detail        TEXT,
            PRIMARY KEY (ticker, session_date)
        ) STRICT;

        CREATE TABLE fundamental_reading (
            ticker        TEXT NOT NULL,
            session_date  TEXT NOT NULL,
            state         TEXT NOT NULL CHECK (state IN ('improving', 'steady', 'deteriorating', 'not enough quarters', 'no fundamentals yet')),
            read_from     TEXT,
            fetched_at    TEXT,
            awaited       TEXT,
            readings      TEXT NOT NULL,
            PRIMARY KEY (ticker, session_date)
        ) STRICT;
    ";

    public static int LatestVersion => All.Max(migration => migration.Version);
}
