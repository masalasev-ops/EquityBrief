using EquityBrief.Data.Migrations;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Tests.Checks;

// From the 23.10 correction of 2026-11-16: on the operator's order of 2026-11-14 the Quality and Fundamentals books sell a
// holding at a close under its stop or at its take-profit, and the two tables' end reasons admitted neither, so the first
// holding to cross either stopped its index's book for that night and every night after. Every reason each book ends a
// holding with, read from the constants its step writes, is one its table admits, and the rebuild keeps every row whole.
// see: Each rule takes profit in the shape its test chose on the early years, never at the nearest price it turned down at
public sealed class BookEndReasons
{
    static readonly string[] QualityReasons =
    [
        "open",
        EquityBrief.Worker.Quality.QualityFamily.NoLongerQualifies,
        EquityBrief.Worker.Quality.QualityFamily.LeftTheIndex,
        EquityBrief.Worker.Quality.QualityFamily.UnderItsAverage,
        EquityBrief.Core.Loop.TakeProfit.UnderItsStop,
        EquityBrief.Core.Loop.TakeProfit.TookProfit,
    ];

    static readonly string[] ImprovingReasons =
    [
        "open",
        EquityBrief.Core.Families.ImprovingRule.ScoreFell,
        EquityBrief.Core.Families.ImprovingRule.TimeLimit,
        EquityBrief.Core.Families.ImprovingRule.LeftTheIndex,
        EquityBrief.Core.Families.ImprovingRule.UnderItsAverage,
        EquityBrief.Core.Loop.TakeProfit.UnderItsStop,
        EquityBrief.Core.Loop.TakeProfit.TookProfit,
    ];

    const string QualityRow = @"
        INSERT INTO quality_holding (rule, index_code, ticker, chosen_on, buy_close, growth, through, end_reason, stop, take_profit, take_profit_from, half_sold_on, exit_added_on)
        VALUES ('a rule', 'MID', $ticker, '2026-11-12', '10.00', 1.0, '2026-11-12', $reason, '9.00', '12.00', 'half 2', '2026-11-13', '2026-11-12');";

    const string ImprovingRow = @"
        INSERT INTO improving_trade (rule, index_code, ticker, chosen_on, filed, passed_checks, applied, before_passed, before_applied, buy_close, growth, through, sessions, end_reason, stop, take_profit, take_profit_from, half_sold_on, exit_added_on)
        VALUES ('a rule', 'MID', $ticker, '2026-11-12', '2026-11-10', 8, 9, 7, 9, '10.00', 1.0, '2026-11-12', 0, $reason, '9.00', '12.00', 'half 2', '2026-11-13', '2026-11-12');";

    static int Insert(SqliteConnection connection, string sql, string ticker, string reason)
    {
        using var command = connection.CreateCommand();

        command.CommandText = sql;
        command.Parameters.AddWithValue("$ticker", ticker);
        command.Parameters.AddWithValue("$reason", reason);

        return command.ExecuteNonQuery();
    }

    [Fact]
    public void EveryReasonTheQualityAndFundamentalsBooksEndAHoldingWithIsOneTheirTablesAdmit()
    {
        using var store = new TemporaryStore().Migrated();
        using var connection = store.Open();

        Assert.Equal(["a close under its stop", "took profit"], [EquityBrief.Core.Loop.TakeProfit.UnderItsStop, EquityBrief.Core.Loop.TakeProfit.TookProfit]);

        for (var at = 0; at < QualityReasons.Length; at++)
        {
            Assert.Equal(1, Insert(connection, QualityRow, $"Q{at}", QualityReasons[at]));
        }

        for (var at = 0; at < ImprovingReasons.Length; at++)
        {
            Assert.Equal(1, Insert(connection, ImprovingRow, $"I{at}", ImprovingReasons[at]));
        }

        // The checks still stand: a reason neither step writes is refused by both.
        Assert.Throws<SqliteException>(() => Insert(connection, QualityRow, "QX", "a reason no step writes"));
        Assert.Throws<SqliteException>(() => Insert(connection, ImprovingRow, "IX", "a reason no step writes"));
    }

    [Fact]
    public void TheRebuildKeepsEveryRowAndEveryColumnOfBothTables()
    {
        using var store = new TemporaryStore();

        new MigrationRunner([.. SchemaMigrations.All.Where(migration => migration.Version < 150)]).Apply(store.DatabaseFile);

        using (var connection = store.Open())
        {
            Insert(connection, QualityRow, "AAA", EquityBrief.Worker.Quality.QualityFamily.UnderItsAverage);
            Insert(connection, ImprovingRow, "BBB", EquityBrief.Core.Families.ImprovingRule.TimeLimit);
        }

        string Rows(string table)
        {
            using var connection = store.Open();
            using var command = connection.CreateCommand();

            command.CommandText = $"SELECT * FROM {table};";

            using var reader = command.ExecuteReader();
            var rows = new List<string>();

            while (reader.Read())
            {
                rows.Add(string.Join("|", Enumerable.Range(0, reader.FieldCount).Select(column => reader.GetName(column) + "=" + (reader.IsDBNull(column) ? "null" : Convert.ToString(reader.GetValue(column), System.Globalization.CultureInfo.InvariantCulture)))));
            }

            return string.Join("\n", rows);
        }

        var quality = Rows("quality_holding");
        var improving = Rows("improving_trade");

        Assert.Contains("stop=9.00|take_profit=12.00|take_profit_from=half 2|half_sold_on=2026-11-13|exit_added_on=2026-11-12", quality, StringComparison.Ordinal);

        MigrationRunner.Standard().Apply(store.DatabaseFile);

        Assert.Equal(quality, Rows("quality_holding"));
        Assert.Equal(improving, Rows("improving_trade"));
    }
}
