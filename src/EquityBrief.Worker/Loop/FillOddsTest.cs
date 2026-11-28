using System.Globalization;
using EquityBrief.Core.Components;
using EquityBrief.Core.Configuration;
using EquityBrief.Core.History;
using EquityBrief.Core.Loop;
using EquityBrief.Core.Paper;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Loop;

// The fill odds, by hand and on one index at a time: every setup the pullback, the breakout, the drift and the dip passed
// under its live rule, the long ledger's from 2005 and the standing ledger's from 2019, its order placed as its rule's live
// plan places it where that plan buys at a level, walked over the long reader's prices as the plan walk fills it, each night
// it waited read for how far its close stood above the level in its typical daily moves and the days it had left, and
// tallied by band and days left with how many filled before their last day. A plan buying at the next open places no order
// and is not read. It writes the tally a run and registers, moves and changes nothing.
// see: A waiting order's fill odds are how often the ledger's past orders of its rule this far away with as long left filled before their last day
public sealed class FillOddsTest(IClock clock, string databaseFile, string dataRoot, TextWriter output) : IComponent
{
    public const string Verb = "fill-odds";

    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.LongSetup, Touch.Read),
            new StoreTouch(Store.Setup, Touch.Read),
            new StoreTouch(Store.CandidateRegister, Touch.Read),
            new StoreTouch(Store.FillOdds, Touch.Insert),
            new StoreTouch(Store.RunLog, Touch.Insert),
        ],
        Feeds: []);

    const string Setups = @"
        SELECT family, ticker, session_date, entry, stop, target, trail, cap, company_value
        FROM long_setup WHERE index_code = $index AND live_pass = 1 AND family IN ('pullback', 'breakout', 'drift', 'dip')
        UNION ALL
        SELECT family, ticker, session_date, entry, stop, target, trail, cap, company_value
        FROM setup WHERE index_code = $index AND live_pass = 1 AND session_date >= $standing AND family IN ('pullback', 'breakout', 'drift', 'dip');";

    public async Task<int> RunAsync(string index, bool word, CancellationToken cancellation = default)
    {
        if (!PlanPointTest.Funds.ContainsKey(index))
        {
            output.WriteLine($"{Verb}: name '--index GSPC', 'MID' or 'SML'");

            return 2;
        }

        if (NightLock.Holder(dataRoot) is { } holder)
        {
            output.WriteLine($"{Verb}: the night holds the store ({holder}); run it once the night has finished");

            return 2;
        }

        var started = clock.UtcNow;

        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        var plans = PlanPointTest.LivePlans(await PlanPointTest.RegisterRowsAsync(connection, null, cancellation), started, index)
            .Where(pair => pair.Value is { Window: > 0 } plan && plan.Entry != PlanPoints.NextOpen)
            .ToDictionary(pair => pair.Key, pair => pair.Value!, StringComparer.Ordinal);

        output.WriteLine(FormattableString.Invariant($"{Verb}: the live plans buying at a level on the {Cards.DecisionCards.NameOf(index)}: {(plans.Count == 0 ? "none" : string.Join("; ", plans.Select(pair => $"{pair.Key}, {pair.Value.Entry} within {pair.Value.Window} session(s)")))}"));

        if (plans.Count == 0)
        {
            return 0;
        }

        var setups = new List<PlanPointTest.FlaggedSetup>();

        await using (var select = connection.CreateCommand())
        {
            select.CommandText = Setups;
            select.Parameters.AddWithValue("$index", index);
            select.Parameters.AddWithValue("$standing", Text(LongLedger.StandingFrom));

            await using var rows = await select.ExecuteReaderAsync(cancellation);

            while (await rows.ReadAsync(cancellation))
            {
                double? Price(int column) => rows.IsDBNull(column) ? null : Core.Prices.Statistic.FromPrice(Money.FromStorage(rows.GetString(column)));

                if (plans.ContainsKey(rows.GetString(0)))
                {
                    setups.Add(new PlanPointTest.FlaggedSetup(
                        rows.GetString(0), rows.GetString(1), Day(rows.GetString(2)), Price(3) ?? 0, Price(4) ?? 0, Price(5), Price(6), rows.GetInt32(7), rows.IsDBNull(8) ? null : rows.GetDouble(8)));
                }
            }
        }

        output.WriteLine(FormattableString.Invariant($"{Verb}: {setups.Count} past order(s) of those rules to walk; nothing is written without '--word'"));

        if (!word)
        {
            return 2;
        }

        var (walker, through, _) = await PlanPointTest.WalkerAsync(databaseFile, index, output.WriteLine, cancellation);
        var runId = FormattableString.Invariant($"{Verb}-{index}-{started:yyyyMMddTHHmmssZ}");
        var written = 0;

        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellation);

        foreach (var (family, plan) in plans)
        {
            var states = new List<OrderState>();
            var orders = 0;

            foreach (var setup in setups.Where(one => one.Family == family))
            {
                if (walker.OrderOf(setup, plan.Entry, plan.Window) is not { } order)
                {
                    continue;
                }

                orders++;
                states.AddRange(FillOdds.States(order.Closes, order.Bar, order.Level, order.Typical, plan.Window, order.FillBar));
            }

            var cells = FillOdds.Tally(states);

            foreach (var cell in cells)
            {
                await using var insert = connection.CreateCommand();

                insert.Transaction = transaction;
                insert.CommandText = @"
                    INSERT INTO fill_odds (run_id, index_code, family, band, days_left, orders, filled)
                    VALUES ($run_id, $index, $family, $band, $days_left, $orders, $filled);";
                insert.Parameters.AddWithValue("$run_id", runId);
                insert.Parameters.AddWithValue("$index", index);
                insert.Parameters.AddWithValue("$family", family);
                insert.Parameters.AddWithValue("$band", cell.Band);
                insert.Parameters.AddWithValue("$days_left", cell.DaysLeft);
                insert.Parameters.AddWithValue("$orders", cell.Orders);
                insert.Parameters.AddWithValue("$filled", cell.Filled);
                await insert.ExecuteNonQueryAsync(cancellation);
                written++;
            }

            output.WriteLine(FormattableString.Invariant($"{Verb}: {family}: {orders} past order(s) through {Text(through)}, {states.Count} night(s) waited, {cells.Count} cell(s)"));
        }

        await using (var log = connection.CreateCommand())
        {
            log.Transaction = transaction;
            log.CommandText = @"
                INSERT INTO run_log (run_id, stage, started_at, ended_at, outcome, rows_written, model_calls, network_requests, spend, detail)
                VALUES ($run_id, $stage, $started_at, $ended_at, 'ok', $rows, 0, 0, '0', $detail);";
            log.Parameters.AddWithValue("$run_id", runId);
            log.Parameters.AddWithValue("$stage", Verb);
            log.Parameters.AddWithValue("$started_at", Instant(started));
            log.Parameters.AddWithValue("$ended_at", Instant(clock.UtcNow));
            log.Parameters.AddWithValue("$rows", written);
            log.Parameters.AddWithValue("$detail", FormattableString.Invariant($"{written} cell(s) of the fill odds on {index} through {Text(through)}"));
            await log.ExecuteNonQueryAsync(cancellation);
        }

        await transaction.CommitAsync(cancellation);

        return 0;
    }

    static string Text(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    static DateOnly Day(string text) => DateOnly.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    static string Instant(DateTimeOffset at) => at.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}
