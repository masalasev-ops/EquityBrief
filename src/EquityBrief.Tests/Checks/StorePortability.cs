namespace EquityBrief.Tests.Checks;

// store-portability. No row in a populated store carries an absolute path,
// because the store must stay a file that can be copied to the other machine.
public class StorePortability
{
    const char WindowsSeparator = (char)92;

    [Fact]
    public void TheMigratedStoreCarriesNoAbsolutePath()
    {
        using var store = new TemporaryStore().Migrated();

        var scan = AbsolutePaths.Scan(store);

        Assert.Empty(scan.Offenders);

        // Scope, and it is context rather than a pass. A freshly migrated store
        // holds no rows, so this assertion has found nothing because there is
        // nothing there. The next test is the one that carries the property.
        //
        // The table count is derived from the migrations rather than written
        // here, so it moves with the schema instead of failing at every new
        // table. The two sources are independent: the migrations say what should
        // be created and the scan reads what the store actually has, so the
        // comparison catches a migration that ran and made no table.
        Assert.Equal(TablesTheMigrationsCreate(), scan.TablesScanned);
        Assert.Equal(0, scan.RowsScanned);
    }

    [Fact]
    public void TheCheckReportsAnAbsolutePathInAPopulatedStore()
    {
        using var store = new TemporaryStore().Migrated();

        // A table the suite creates, so the check that guards the declared
        // tables never has to write to one.
        store.Execute("CREATE TABLE probe (id INTEGER PRIMARY KEY, note TEXT) STRICT;");
        store.Execute(
            "INSERT INTO probe (id, note) VALUES " +
            "(1, 'a relative path, data/equitybrief.db'), " +
            "(2, '/Users/someone/EquityBrief/data/equitybrief.db'), " +
            "(3, 'C:" + WindowsSeparator + "EquityBrief" + WindowsSeparator + "data');");

        var scan = AbsolutePaths.Scan(store);

        Assert.Equal(TablesTheMigrationsCreate() + 1, scan.TablesScanned);
        Assert.Equal(3, scan.RowsScanned);
        Assert.Equal(2, scan.Offenders.Count);
        Assert.All(scan.Offenders, offence => Assert.Equal("note", offence.Column));
    }

    [Fact]
    public void BothPlatformsFormsAreRecognisedWhereverThisRuns()
    {
        // A store written on Windows is read on macOS and the reverse, so the
        // check cannot use the running platform's idea of what rooted means.
        Assert.True(AbsolutePaths.LooksAbsolute("/data/equitybrief.db"));
        Assert.True(AbsolutePaths.LooksAbsolute("C:" + WindowsSeparator + "data"));
        Assert.True(AbsolutePaths.LooksAbsolute(WindowsSeparator + "server"));

        Assert.False(AbsolutePaths.LooksAbsolute("data/equitybrief.db"));
        Assert.False(AbsolutePaths.LooksAbsolute("AAPL"));
        Assert.False(AbsolutePaths.LooksAbsolute(""));
    }

    [Fact]
    public void APathInTheMiddleOfASentenceIsFound()
    {
        // The obligation carried out of the 0.7 review, and the case that
        // motivated it. `run_log.detail` is where exception text lands, and an
        // exception names its path in the middle of a message rather than as the
        // whole value. Every one of these passed the check until 1.3.
        Assert.True(AbsolutePaths.LooksAbsolute(
            "could not open C:" + WindowsSeparator + "EquityBrief" + WindowsSeparator + "data"));
        Assert.True(AbsolutePaths.LooksAbsolute("SqliteException: unable to open /var/db/equitybrief.db"));
        Assert.True(AbsolutePaths.LooksAbsolute("at Backfill.RunAsync in /Users/someone/src/Backfill.cs:line 88"));
        Assert.True(AbsolutePaths.LooksAbsolute("the file '/data/equitybrief.db' is locked"));

        // A UNC path is rooted, and its two leading separators must not read as
        // a separator with another separator after it.
        Assert.True(AbsolutePaths.LooksAbsolute(
            new string(WindowsSeparator, 2) + "server" + WindowsSeparator + "share"));
    }

    [Fact]
    public void TheThingsAWiderMatchWouldWronglyCatchAreNotCaught()
    {
        // The counter-test, and the reason the widening looks for a shape rather
        // than for a separator. A check reporting any of these would be a check
        // somebody turns off, and turning it off is how the property is lost.
        //
        // A URL is not a portability fault either way: it reads the same on the
        // other machine, which is the whole test the rule applies.
        Assert.False(AbsolutePaths.LooksAbsolute("fetched from https://eodhd.com/api/eod/AAPL.US"));
        Assert.False(AbsolutePaths.LooksAbsolute("eod/AAPL.US?from=2025-09-04&to=2026-09-04"));
        Assert.False(AbsolutePaths.LooksAbsolute("the session of 2026/09/08"));
        Assert.False(AbsolutePaths.LooksAbsolute("held 1/4 of the period's volume"));
        Assert.False(AbsolutePaths.LooksAbsolute("support and/or resistance"));
        Assert.False(AbsolutePaths.LooksAbsolute("data/equitybrief.db is relative and stays so"));

        // A timestamp, which is every observed_at in the store and is the one a
        // drive-letter rule without the separator would have reported: a letter,
        // a colon, and no separator after it.
        Assert.False(AbsolutePaths.LooksAbsolute("2026-09-08T12:37:08Z"));

        // And a separator with nothing after it is not a path.
        Assert.False(AbsolutePaths.LooksAbsolute("ends with /"));
    }

    [Fact]
    public void TheScanFindsAnEmbeddedPathInTheColumnExceptionTextLandsIn()
    {
        // Through the scan rather than through the matcher, because the property
        // is about a populated store and the column is a real one: SCHEMA gives
        // run_log a detail column and that is where a failure's text goes.
        using var store = new TemporaryStore().Migrated();

        store.Execute(
            "INSERT INTO run_log (run_id, stage, started_at, ended_at, outcome, " +
            "rows_written, model_calls, network_requests, spend, detail) VALUES " +
            "('run-1', 'backfill', '2026-09-08T00:00:00Z', '2026-09-08T00:01:00Z', 'failed', " +
            "0, 0, 0, '0', 'unable to open /Users/someone/EquityBrief/data/equitybrief.db');");

        var scan = AbsolutePaths.Scan(store);
        var offence = Assert.Single(scan.Offenders);

        Assert.Equal("run_log", offence.Table);
        Assert.Equal("detail", offence.Column);
        Assert.Equal(1, scan.RowsScanned);
    }

    // How many tables the migrations create, read from their SQL. An
    // independent source from the store the scan opens.
    static int TablesTheMigrationsCreate() =>
        EquityBrief.Data.Migrations.SchemaMigrations.All
            .Sum(migration => System.Text.RegularExpressions.Regex
                .Matches(migration.Sql, @"CREATE\s+TABLE", System.Text.RegularExpressions.RegexOptions.IgnoreCase)
                .Count);
}
