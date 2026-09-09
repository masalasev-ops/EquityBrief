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
    ];

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
