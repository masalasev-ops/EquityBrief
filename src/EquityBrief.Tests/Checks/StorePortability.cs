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
        Assert.Equal(1, scan.TablesScanned);
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

        Assert.Equal(2, scan.TablesScanned);
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
}
