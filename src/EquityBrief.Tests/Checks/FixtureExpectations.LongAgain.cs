using EquityBrief.Core.Ledger;
using EquityBrief.Data;
using EquityBrief.Worker.Ledger;
using Microsoft.Data.Sqlite;
using Xunit;

namespace EquityBrief.Tests.Checks;

// 23.10's second part: the long ledger written once more. A build writing it again passes over the sessions whose rows were
// written under the catalogue's version now and writes every other, so one stopped part way resumes where it stopped, while a
// build without it passes over every session it holds; and a session's rows are cleared in the transaction that writes them
// again, every other session's standing. Worked by hand over a migrated store.
// see: The long ledger is written once more at the second restart, each session replaced whole and resumed by its pin
public partial class FixtureExpectations
{
    [Fact]
    public async Task TheLongLedgerWrittenOnceMoreResumesByItsPinAndReplacesASessionWhole()
    {
        using var store = new TemporaryStore().Migrated();

        void Setup(string session, string ticker, string pin, string index = "GSPC") =>
            store.Execute(
                "INSERT INTO long_setup (index_code, family, ticker, session_date, rule, live_pass, picked, entry, stop, cap, end, settled, source, pin, last_end, last_settled, run_id) " +
                $"VALUES ('{index}', 'drift', '{ticker}', '{session}', 'provisional', 0, 0, '10', '9', 63, 'open', 0, 'history', '{pin}', 'open', 0, 'ledger-build-long');");

        // The 2010-03-01 session written under the catalogue's version now, two of 2010-03-02 under an earlier one, and the
        // S&P 400's 2010-03-02 under an earlier one too.
        Setup("2010-03-01", "AAA", LedgerReadings.Version);
        Setup("2010-03-02", "AAA", "000000000000");
        Setup("2010-03-02", "BBB", "000000000000");
        Setup("2010-03-02", "CCC", "000000000000", "MID");

        await using var connection = new SqliteConnection(StoreConnection.For(store.DatabaseFile));
        await connection.OpenAsync();

        // Writing it again passes over the session at the version now alone; a build writing only what it holds none of
        // passes over both.
        Assert.Equal([new DateOnly(2010, 3, 1)], (await SetupLedger.LongSessionsPassedAsync(connection, "GSPC", again: true, default)).Order());
        Assert.Equal([new DateOnly(2010, 3, 1), new DateOnly(2010, 3, 2)], (await SetupLedger.LongSessionsPassedAsync(connection, "GSPC", again: false, default)).Order());

        // Clearing 2010-03-02 on the S&P 500 removes its two rows and no other: the S&P 500's 2010-03-01 and the S&P 400's
        // 2010-03-02 stand; and a rollback leaves the session whole, so a session is never left half written.
        await using (var rolledBack = (SqliteTransaction)await connection.BeginTransactionAsync())
        {
            await SetupLedger.ClearLongSessionAsync(connection, rolledBack, "GSPC", new DateOnly(2010, 3, 2), default);
            await rolledBack.RollbackAsync();
        }

        Assert.Equal(4, Scalar(store, "SELECT COUNT(*) FROM long_setup;"));

        await using (var transaction = (SqliteTransaction)await connection.BeginTransactionAsync())
        {
            await SetupLedger.ClearLongSessionAsync(connection, transaction, "GSPC", new DateOnly(2010, 3, 2), default);
            await transaction.CommitAsync();
        }

        Assert.Equal("GSPC 2010-03-01 AAA|MID 2010-03-02 CCC", Text(store, "SELECT group_concat(index_code || ' ' || session_date || ' ' || ticker, '|') FROM (SELECT * FROM long_setup ORDER BY index_code, session_date, ticker);"));
    }
}
