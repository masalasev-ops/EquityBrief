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

    public static IReadOnlyList<Migration> All { get; } =
    [
        new Migration(1, "create run_log", CreateRunLog),
        new Migration(2, "create membership", CreateMembership),
    ];

    public static int LatestVersion => All.Max(migration => migration.Version);
}
