using System.Diagnostics;
using EquityBrief.Core.History;
using EquityBrief.Core.Ledger;
using EquityBrief.Data;
using EquityBrief.Worker.Cards;
using EquityBrief.Worker.History;
using EquityBrief.Worker.Research;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Ledger;

// From the 24.19 closing plan the rows of the codes named written again on one index from the prices as they stand, the
// codes' corrected: their long setups over 2005 to 2018, their standing setups over the span the history build wrote and their
// rows of every member's readings on every session, each worked as the full builds work it, every member read and the named
// codes' rows alone cleared and written, so each holds what a full rebuild writes for those codes; the sessions a night stored
// are left to the night. Without the word it states what it would write and writes nothing.
// see: The rows of the codes a correction moved are written again code by code, as a full rebuild writes them for those codes
public sealed partial class SetupLedger
{
    public const string CodesAgainStage = "ledger-codes-again";

    // The full builds' own clears of a session, each narrowed to one code.
    static readonly string ClearLongCode = ClearLongSession.Replace(";", " AND ticker = $ticker;", StringComparison.Ordinal);

    static readonly string ClearStandingCode = ClearTheNight.Replace(";", " AND ticker = $ticker;", StringComparison.Ordinal);

    static readonly string ClearSessionCode = ClearSession.Replace(";", " AND ticker = $ticker;", StringComparison.Ordinal);

    public async Task<int> CodesAgainAsync(string dataRoot, string index, IReadOnlySet<string> codes, bool word, TextWriter output, Func<TimeSpan, CancellationToken, Task>? wait = null, CancellationToken cancellation = default)
    {
        var startedAt = clock.UtcNow;
        var pause = wait ?? Task.Delay;

        await WaitForTheNightAsync(dataRoot, output, pause, cancellation);

        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        var longHistory = new LongHistoryReader(databaseFile);
        var through = await longHistory.NewestSessionAsync(cancellation);
        var live = await LiveRulesAsync(index, output, cancellation);
        var standingSpan = await HistorySpanAsync(index, cancellation);
        var nights = await StoredSessionsAsync(connection, index, NightSource, cancellation);

        output.WriteLine(FormattableString.Invariant($"{CodesAgainStage}: {codes.Count} code(s) on the {DecisionCards.NameOf(index)}, {string.Join(", ", codes.Order(StringComparer.Ordinal))}: their long setups over 2005 to 2018, their standing setups over {(standingSpan is { } span ? $"{Stamp(span.From)} to {Stamp(span.Through)}" : "no span the history build wrote")} and their rows of every member's readings from 2005, each worked over every member"));

        if (!word)
        {
            output.WriteLine($"{CodesAgainStage}: nothing written; give '--word' to write them");

            return 2;
        }

        var runId = FormattableString.Invariant($"{CodesAgainStage}-{index}-{startedAt:yyyyMMddTHHmmssZ}");
        var timed = Stopwatch.StartNew();

        // The long setups, each session under both readings.
        var prepared = await PrepareAsync(connection, index, through, output, cancellation, live: live, longHistory: longHistory);
        var atLastPrice = prepared.HeldAtLastPrice();
        var longSessions = Enumerable.Range(0, prepared.Calendar.Length).Where(at => prepared.Calendar[at] >= LongLedger.From && prepared.Calendar[at] <= LongLedger.Through).ToArray();
        var longWritten = 0;

        for (var taken = 0; taken < longSessions.Length; taken += ChunkSessions)
        {
            var chunk = longSessions.Skip(taken).Take(ChunkSessions).ToArray();

            await WaitForTheNightAsync(dataRoot, output, pause, cancellation);

            var reads = Computed(chunk.Length * 2, item => (item % 2 == 0 ? prepared : atLastPrice).Compute(chunk[item / 2]));

            using var held = await DrainLock.AcquireAsync(dataRoot, () => pause(Poll, cancellation), cancellation);
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellation);

            for (var item = 0; item < chunk.Length; item++)
            {
                var (at, asRuled, lastPrice) = (chunk[item], reads[2 * item], reads[2 * item + 1]);

                foreach (var ticker in codes)
                {
                    await ExecuteAsync(connection, transaction, ClearLongCode, [("$index", index), ("$session", Stamp(prepared.Calendar[at])), ("$ticker", ticker)], cancellation);
                }

                for (var place = 0; place < asRuled.Families.Count; place++)
                {
                    var (family, familyReads) = asRuled.Families[place];
                    var lastReads = lastPrice.Families[place].Reads;

                    for (var row = 0; row < family.Rows.Count; row++)
                    {
                        if (!codes.Contains(family.Rows[row].Ticker))
                        {
                            continue;
                        }

                        await InsertAsync(connection, transaction, family.Rows[row], null, null, familyReads[row], prepared.Replayed, at, HistorySource, cancellation, InsertLongSetup,
                        [
                            .. LastPriceParameters(SettleParameters(family.Rows[row].Index, family.Family, family.Rows[row].Ticker, family.Rows[row].Session, null, lastReads[row].Outcome, lastReads[row].Benchmark, prepared.Replayed, at)),
                            ("$run_id", runId),
                        ],
                        otherPass: asRuled.OtherPass(family.Family, family.Rows[row].Ticker));
                        longWritten++;
                    }
                }
            }

            await transaction.CommitAsync(cancellation);
        }

        using (await DrainLock.AcquireAsync(dataRoot, () => pause(Poll, cancellation), cancellation))
        {
            await WriteLongYearsAsync(connection, index, through, runId, cancellation);
        }

        output.WriteLine(FormattableString.Invariant($"{CodesAgainStage}: {longWritten} long setup(s) of the codes written over {longSessions.Length} session(s)"));

        // The standing setups over the span the history build wrote, the sessions a night stored left to it; each session's
        // family counts written again whole, as the full build writes them.
        var standingWritten = 0;

        if (standingSpan is { } standing)
        {
            var standingPrepared = await PrepareAsync(connection, index, through, output, cancellation, live: live);
            var standingSessions = Enumerable.Range(0, standingPrepared.Calendar.Length)
                .Where(at => standingPrepared.Calendar[at] >= standing.From && standingPrepared.Calendar[at] <= standing.Through && !nights.Contains(standingPrepared.Calendar[at]))
                .ToArray();

            for (var taken = 0; taken < standingSessions.Length; taken += ChunkSessions)
            {
                var chunk = standingSessions.Skip(taken).Take(ChunkSessions).ToArray();

                await WaitForTheNightAsync(dataRoot, output, pause, cancellation);

                var computed = Computed(chunk.Length, item => standingPrepared.Compute(chunk[item]));

                using var held = await DrainLock.AcquireAsync(dataRoot, () => pause(Poll, cancellation), cancellation);
                await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellation);

                for (var item = 0; item < chunk.Length; item++)
                {
                    var at = chunk[item];
                    var night = Stamp(standingPrepared.Calendar[at]);

                    foreach (var ticker in codes)
                    {
                        await ExecuteAsync(connection, transaction, ClearStandingCode, [("$index", index), ("$night", night), ("$source", HistorySource), ("$ticker", ticker)], cancellation);
                    }

                    await ExecuteAsync(connection, transaction, ClearTheNightsRows, [("$index", index), ("$night", night), ("$source", HistorySource)], cancellation);

                    foreach (var (family, reads) in computed[item].Families)
                    {
                        for (var place = 0; place < family.Rows.Count; place++)
                        {
                            if (codes.Contains(family.Rows[place].Ticker))
                            {
                                await InsertAsync(connection, transaction, family.Rows[place], null, null, reads[place], standingPrepared.Replayed, at, HistorySource, cancellation,
                                    otherPass: computed[item].OtherPass(family.Family, family.Rows[place].Ticker));
                                standingWritten++;
                            }
                        }

                        await ExecuteAsync(connection, transaction, InsertNight,
                        [
                            ("$index", index), ("$family", family.Family), ("$session", night), ("$members", family.Members),
                            ("$setups", family.Rows.Count), ("$live_passes", family.LivePasses), ("$source", HistorySource),
                        ], cancellation);
                    }
                }

                await transaction.CommitAsync(cancellation);
            }

            using (await DrainLock.AcquireAsync(dataRoot, () => pause(Poll, cancellation), cancellation))
            {
                await RefreshSummaryAsync(connection, index, cancellation);
            }

            output.WriteLine(FormattableString.Invariant($"{CodesAgainStage}: {standingWritten} standing setup(s) of the codes written over {standingSessions.Length} session(s)"));
        }

        // The codes' rows of every member's readings, the sessions a night stored left to it.
        var tablePrepared = await PrepareAsync(connection, index, through, output, cancellation, heavyweightsRead: false, live: live, longHistory: longHistory);

        tablePrepared.ReportFilings = await ReportFilingsAsync(connection, cancellation);

        var tableNights = await SessionsAsync(connection, NightSessionsOf, [("$index", index)], cancellation);
        var tableSessions = Enumerable.Range(0, tablePrepared.Calendar.Length)
            .Where(at => tablePrepared.Calendar[at] >= LongLedger.From && tablePrepared.Calendar[at] <= through && !tableNights.Contains(tablePrepared.Calendar[at]))
            .ToArray();
        var rowsWritten = 0L;

        for (var taken = 0; taken < tableSessions.Length; taken += ChunkSessions)
        {
            var chunk = tableSessions.Skip(taken).Take(ChunkSessions).ToArray();

            await WaitForTheNightAsync(dataRoot, output, pause, cancellation);

            var rows = Computed<IReadOnlyList<(string Ticker, string Readings)>>(chunk.Length, item => tablePrepared.MemberReadings(chunk[item]));

            using var held = await DrainLock.AcquireAsync(dataRoot, () => pause(Poll, cancellation), cancellation);
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellation);

            for (var item = 0; item < chunk.Length; item++)
            {
                var session = Stamp(tablePrepared.Calendar[chunk[item]]);

                foreach (var ticker in codes)
                {
                    await ExecuteAsync(connection, transaction, ClearSessionCode, [("$index", index), ("$session", session), ("$ticker", ticker)], cancellation);
                }

                foreach (var (ticker, readings) in rows[item].Where(row => codes.Contains(row.Ticker)))
                {
                    await ExecuteAsync(connection, transaction, InsertSessionReading,
                        [("$index", index), ("$session", session), ("$ticker", ticker), ("$readings", readings), ("$pin", LedgerReadings.Version), ("$source", BuildSource)], cancellation);
                    rowsWritten++;
                }
            }

            await transaction.CommitAsync(cancellation);
        }

        using (await DrainLock.AcquireAsync(dataRoot, () => pause(Poll, cancellation), cancellation))
        {
            await CountSessionYearsAsync(connection, null, index, LongLedger.From.Year, through.Year, cancellation);
        }

        var detail = FormattableString.Invariant($"{codes.Count} code(s) on the {DecisionCards.NameOf(index)} written again from their prices as they stand: {longWritten} long setup(s), {standingWritten} standing setup(s) and {rowsWritten} row(s) of every member's readings, in {timed.Elapsed.TotalMinutes:0} minute(s)");

        await AppendAsync(connection, runId, CodesAgainStage, startedAt, (int)Math.Min(int.MaxValue, longWritten + standingWritten + rowsWritten), Ok, detail, cancellation);
        output.WriteLine($"{CodesAgainStage}: {detail}");

        return 0;
    }
}
