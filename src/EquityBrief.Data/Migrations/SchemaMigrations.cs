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
    // see: Candidate conditions are registered before they are scored, and scored in shadow before they are shown
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
    // see: Candidate conditions are registered before they are scored, and scored in shadow before they are shown
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
    // see: Candidate conditions are registered before they are scored, and scored in shadow before they are shown
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
    ];

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
    // see: A request the page writes and the worker drains is what starts a pass, and the read surface writes the ask and never the research
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

    public static int LatestVersion => All.Max(migration => migration.Version);
}
