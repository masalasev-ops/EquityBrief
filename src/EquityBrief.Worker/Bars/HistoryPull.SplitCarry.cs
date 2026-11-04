using System.Globalization;
using EquityBrief.Core.Bars;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Bars;

public sealed record SplitCarryOutcome(string Pull, IReadOnlyList<string> Lines, int Rows, int Requests);

// `history-pull --carry-splits --names <codes>`: one request a name for the splits the provider records, and for each
// split its own adjusted closes still carry as a jump, every session of the newest pull written again under a new pull with
// the sessions before the split on the split's basis. An earlier pull's rows stand beside it, and a reader reads the newest
// pull holding what it reads.
// see: A split the provider records and its own adjusted closes do not apply is applied to a new pull of the stock's history
public sealed partial class HistoryPull
{
    public const string SplitCarryStage = "history-pull-carry-splits";

    const string NewestPullOf = "SELECT pull FROM pulled_bar WHERE ticker = $ticker ORDER BY pull DESC LIMIT 1;";

    const string SessionsOfPull = "SELECT session_date, open, high, low, close, raw_close, volume FROM pulled_bar WHERE ticker = $ticker AND pull = $pull ORDER BY session_date;";

    const string AppendCarry = @"
        INSERT INTO run_log (run_id, stage, started_at, ended_at, outcome, rows_written, model_calls, network_requests, spend, detail)
        VALUES ($run_id, $stage, $started_at, $ended_at, 'ok', $rows, 0, $requests, '0', $detail);
    ";

    public static async Task<SplitCarryOutcome> CarrySplitsAsync(
        ISplitHistoryFeed splits,
        IClock clock,
        string databaseFile,
        IReadOnlyCollection<string> names,
        string runId,
        CancellationToken cancellation = default)
    {
        var started = clock.UtcNow;
        var requestsBefore = splits.Requests;
        var lines = new List<string>();
        var written = 0;

        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        foreach (var ticker in names)
        {
            await using var newest = connection.CreateCommand();
            newest.CommandText = NewestPullOf;
            newest.Parameters.AddWithValue("$ticker", ticker);

            if (await newest.ExecuteScalarAsync(cancellation) is not string pull)
            {
                lines.Add(FormattableString.Invariant($"{ticker}: no pull holds it, so nothing is carried"));

                continue;
            }

            var sessions = new List<PulledSession>();

            await using (var read = connection.CreateCommand())
            {
                read.CommandText = SessionsOfPull;
                read.Parameters.AddWithValue("$ticker", ticker);
                read.Parameters.AddWithValue("$pull", pull);

                await using var reader = await read.ExecuteReaderAsync(cancellation);

                while (await reader.ReadAsync(cancellation))
                {
                    sessions.Add(new PulledSession(
                        DateOnly.ParseExact(reader.GetString(0), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                        Money.FromStorage(reader.GetString(1)),
                        Money.FromStorage(reader.GetString(2)),
                        Money.FromStorage(reader.GetString(3)),
                        Money.FromStorage(reader.GetString(4)),
                        Money.FromStorage(reader.GetString(5)),
                        reader.GetInt64(6)));
                }
            }

            var left = SplitCarry.NotCarried(sessions, await splits.SplitsAsync(ticker, sessions[0].Session, cancellation));

            if (left.Count == 0)
            {
                lines.Add(FormattableString.Invariant($"{ticker}: every split the provider records is carried in {pull}"));

                continue;
            }

            await using var transaction = await connection.BeginTransactionAsync(cancellation);

            foreach (var session in SplitCarry.Carried(sessions, left))
            {
                await using var insert = connection.CreateCommand();
                insert.CommandText = InsertBar;
                insert.Parameters.AddWithValue("$ticker", ticker);
                insert.Parameters.AddWithValue("$session_date", Text(session.Session));
                Money.Bind(insert, "$open", session.Open);
                Money.Bind(insert, "$high", session.High);
                Money.Bind(insert, "$low", session.Low);
                Money.Bind(insert, "$close", session.Close);
                Money.Bind(insert, "$raw_close", session.RawClose);
                insert.Parameters.AddWithValue("$volume", session.Volume);
                insert.Parameters.AddWithValue("$pull", runId);

                written += await insert.ExecuteNonQueryAsync(cancellation);
            }

            await transaction.CommitAsync(cancellation);

            var applied = left.Select(split => string.Join(", ", Text(split.ExDate), split.NewShares.ToString("0.####", CultureInfo.InvariantCulture) + " for " + split.OldShares.ToString("0.####", CultureInfo.InvariantCulture)));

            lines.Add(FormattableString.Invariant($"{ticker}: {string.Join(" and ", applied)} applied to the {sessions.Count} session(s) of {pull}"));
        }

        var requests = splits.Requests - requestsBefore;

        await using (var run = connection.CreateCommand())
        {
            run.CommandText = AppendCarry;
            run.Parameters.AddWithValue("$run_id", runId);
            run.Parameters.AddWithValue("$stage", SplitCarryStage);
            run.Parameters.AddWithValue("$started_at", started.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
            run.Parameters.AddWithValue("$ended_at", clock.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
            run.Parameters.AddWithValue("$rows", written);
            run.Parameters.AddWithValue("$requests", requests);
            run.Parameters.AddWithValue("$detail", string.Join("; ", lines));

            await run.ExecuteNonQueryAsync(cancellation);
        }

        return new SplitCarryOutcome(runId, lines, written, requests);
    }
}
