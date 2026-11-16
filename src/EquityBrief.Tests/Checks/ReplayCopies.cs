using EquityBrief.Worker.Replay;

namespace EquityBrief.Tests.Checks;

// On the operator's rulings of 2026-11-16 the replay runs the old build and the new over two copies of the store capped
// night by night and compares their trades. A copy is prepared only where the copying left its mark, its later rows hidden
// behind the caps and the deciding steps' tables stood as on the evening before the first night replayed; the comparison
// names each rule whose rows differ and passes each whose rows are the same.
// see: The replay compares the old build and the new over two copies of the store capped night by night, and a moved live rule keeps its record only where every trade is the same
public sealed class ReplayCopies
{
    const string Bars = @"
        INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at, raw_close) VALUES
            ('AAA', '2026-11-10', '10', '11', '9', '10', 100, 'bulk', '2026-11-11T01:00:00Z', '10'),
            ('AAA', '2026-11-11', '10', '11', '9', '10', 100, 'bulk', '2026-11-12T01:00:00Z', '10'),
            ('AAA', '2026-11-12', '10', '11', '9', '10', 100, 'bulk', '2026-11-13T01:00:00Z', '10'),
            ('AAA', '2026-11-13', '10', '11', '9', '10', 100, 'bulk', '2026-11-14T01:00:00Z', '10');";

    const string Trades = @"
        INSERT INTO family_trade (candidate, ticker, session_date, family, place, entry, stop, cap, ended_on, result) VALUES
            ('a rule', 'AAA', '2026-11-10', 'drift', 1, '10', '9', 60, '2026-11-12', 0.5),
            ('a rule', 'BBB', '2026-11-12', 'drift', 1, '10', '9', 60, NULL, NULL),
            ('another rule', 'AAA', '2026-11-11', 'drift', 1, '10', '9', 60, '2026-11-11', -1.0);";

    static TemporaryStore Marked()
    {
        var store = new TemporaryStore().Migrated();

        store.Execute(Bars);
        store.Execute(Trades);
        File.WriteAllText(Path.Combine(store.Root, ReplayCopy.CopyMark), "a replay's copy");

        return store;
    }

    static object? Scalar(TemporaryStore store, string sql)
    {
        using var connection = store.Open();
        using var command = connection.CreateCommand();

        command.CommandText = sql;

        return command.ExecuteScalar();
    }

    [Fact]
    public async Task ACopyIsPreparedOnlyWhereItsMarkStandsAndThenReadsNothingAfterItsCeiling()
    {
        using var unmarked = new TemporaryStore().Migrated();

        unmarked.Execute(Bars);

        await Assert.ThrowsAsync<InvalidOperationException>(() => ReplayCopy.PrepareAsync(unmarked.DatabaseFile, new DateOnly(2026, 11, 12)));
        Assert.Null(ReplayCopy.CeilingOf(unmarked.DatabaseFile));
        Assert.Equal("2026-11-13", Scalar(unmarked, "SELECT MAX(session_date) FROM bar;"));

        using var store = Marked();

        Assert.StartsWith("prepared for 2026-11-12", await ReplayCopy.PrepareAsync(store.DatabaseFile, new DateOnly(2026, 11, 12)), StringComparison.Ordinal);
        Assert.Equal(new DateOnly(2026, 11, 11), ReplayCopy.CeilingOf(store.DatabaseFile));
        Assert.Equal("2026-11-11", Scalar(store, "SELECT MAX(session_date) FROM bar;"));
        Assert.Equal(4L, Scalar(store, "SELECT COUNT(*) FROM bar_all;"));

        // The trade bought on the first night replayed is gone, and the one it ended open again; one ended before it stands.
        Assert.Equal(0L, Scalar(store, "SELECT COUNT(*) FROM family_trade WHERE ticker = 'BBB';"));
        Assert.Equal(1L, Scalar(store, "SELECT COUNT(*) FROM family_trade WHERE ticker = 'AAA' AND candidate = 'a rule' AND ended_on IS NULL AND result IS NULL;"));
        Assert.Equal("2026-11-11", Scalar(store, "SELECT ended_on FROM family_trade WHERE candidate = 'another rule';"));

        await ReplayCopy.CeilAsync(store.DatabaseFile, new DateOnly(2026, 11, 13));

        Assert.Equal("2026-11-13", Scalar(store, "SELECT MAX(session_date) FROM bar;"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => ReplayCopy.PrepareAsync(store.DatabaseFile, new DateOnly(2026, 11, 12)));
    }

    [Fact]
    public async Task TheComparisonNamesEachRuleWhoseRowsDifferAndPassesEachWhoseRowsAreTheSame()
    {
        using var old = Marked();
        using var again = old.Copy();

        File.WriteAllText(Path.Combine(again.Root, ReplayCopy.CopyMark), "a replay's copy");

        var same = await ReplayCompare.CompareAsync(old.DatabaseFile, again.DatabaseFile);

        Assert.All(same, one => Assert.True(one.Same, one.Table + " " + one.Owner));
        Assert.Contains(same, one => one.Table == "family_trade" && one.Owner == "a rule" && one.Old == 2);

        again.Execute("UPDATE family_trade SET result = 0.25 WHERE candidate = 'a rule' AND ticker = 'AAA';");

        var compared = await ReplayCompare.CompareAsync(old.DatabaseFile, again.DatabaseFile);
        var differs = Assert.Single(compared, one => !one.Same);

        Assert.Equal(("family_trade", "a rule"), (differs.Table, differs.Owner));
        Assert.Contains("result=0.5", differs.First, StringComparison.Ordinal);
        Assert.True(compared.Single(one => one.Owner == "another rule").Same);
    }

    [Fact]
    public void AStoreHoldingNoCeilingIsNoReplaysCopy()
    {
        using var store = new TemporaryStore().Migrated();

        Assert.Null(ReplayCopy.CeilingOf(store.DatabaseFile));
        Assert.False(ReplayCopy.IsACopy(store.DatabaseFile));
        Assert.True(ReplayCompare.Written("filled_at"));
        Assert.False(ReplayCompare.Written("opened_at"));
    }
}
