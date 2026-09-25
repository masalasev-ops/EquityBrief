using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Components;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Returns;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Returns;

public sealed record ForwardReturnOutcome(int ListingsExamined, int RowsWritten, int Matured, int Immature, int Kept, int PlansExamined = 0, int PlansNotScorable = 0);

// The forward return filler. Fills the five and twenty-one session outcomes of
// past listings as those sessions mature, and computes the universe base rate
// for the same windows.
//
// It runs once per night rather than per name, because the base rate is a figure
// over the whole population and a per-name pass would compute it once per name
// from the same rows.
// see: Every forward-return figure is shown against the universe base rate
// see: An outcome once decided is never rewritten, and a setup still in play is scored with its plan scaled by its listing session's adjustment factor
public sealed class ForwardReturnFiller : IComponent
{
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.Bar, Touch.Read),
            new StoreTouch(Store.Calendar, Touch.Read),
            new StoreTouch(Store.Listing, Touch.Read),
            new StoreTouch(Store.GateResult, Touch.Read),
            new StoreTouch(Store.ForwardReturn, Touch.Read | Touch.Insert | Touch.Update),
            new StoreTouch(Store.RunLog, Touch.Insert),
        ],
        Feeds: []);

    public const string Stage = "forward-returns";

    // Every listing with the rows already written for it, oldest first.
    const string EveryListing = @"
        SELECT l.ticker, l.session_date, l.plan_at_listing, f.horizon, f.outcome
        FROM listing l
        LEFT JOIN forward_return f ON f.ticker = l.ticker AND f.session_date = l.session_date
        ORDER BY l.session_date, l.ticker;
    ";

    // Every swing filter row carrying a plan of its own, with the swing horizons already written for it.
    // A row whose setup found no band has no stop to score from and carries none, and a replayed row is no
    // setup a night drew.
    // see: The swing filter's setups are scored on the swing trade's own plan from the listing close, and their first twenty sessions are context
    // see: The swing filter's results are replayed for the sessions before its first stored night, for the trigger's arrival alone
    const string EveryPlannedGateRow = @"
        SELECT g.ticker, g.session_date, g.swing_stop, g.swing_target, f.horizon, f.outcome
        FROM gate_result g
        LEFT JOIN forward_return f
            ON f.ticker = g.ticker AND f.session_date = g.session_date AND f.horizon IN ('swing', 'swing-20')
        WHERE g.swing_stop IS NOT NULL AND g.swing_target IS NOT NULL AND g.version <> '" + EquityBrief.Core.Filter.ReplayedResults.Version + @"'
        ORDER BY g.session_date, g.ticker;
    ";

    // The sessions after a listing's own, in order, which is what a horizon is
    // counted in. Sessions rather than days, because a horizon stated in
    // sessions is a horizon in trading and a month of days is not a month of
    // trading.
    const string SessionsAfter = @"
        SELECT session_date, close
        FROM bar
        WHERE ticker = $ticker AND session_date > $session_date
        ORDER BY session_date
        LIMIT $sessions;
    ";

    const string CloseOn = @"
        SELECT close, raw_close
        FROM bar
        WHERE ticker = $ticker AND session_date <= $session_date
        ORDER BY session_date DESC
        LIMIT 1;
    ";

    // The closes the trailing volatility is measured over, newest first and one more than the
    // window, since a window of sessions of change is read over one more session of closes.
    const string ClosesTo = @"
        SELECT close
        FROM bar
        WHERE ticker = $ticker AND session_date <= $session_date
        ORDER BY session_date DESC
        LIMIT $sessions;
    ";

    // The name's prints on file, which decide whether the session a setup resolved on was one it
    // reported on. The calendar holds a year, which is why the answer is stored beside the outcome
    // rather than asked for when a record is read years later.
    const string PrintsFor = @"
        SELECT event_date, timing
        FROM calendar
        WHERE ticker = $ticker AND kind = $kind
        ORDER BY event_date;
    ";

    const string Upsert = @"
        INSERT INTO forward_return (
            ticker, session_date, horizon, outcome, resolved_on, return_pct, base_rate, break_even,
            null_win, null_win_at_sensitivity, planned_risk, on_earnings)
        VALUES (
            $ticker, $session_date, $horizon, $outcome, $resolved_on, $return_pct, $base_rate, $break_even,
            $null_win, $null_win_at_sensitivity, $planned_risk, $on_earnings)
        ON CONFLICT (ticker, session_date, horizon) DO UPDATE SET
            outcome = excluded.outcome,
            resolved_on = excluded.resolved_on,
            return_pct = excluded.return_pct,
            base_rate = excluded.base_rate,
            break_even = excluded.break_even,
            null_win = excluded.null_win,
            null_win_at_sensitivity = excluded.null_win_at_sensitivity,
            planned_risk = excluded.planned_risk,
            on_earnings = excluded.on_earnings;
    ";

    // The base rate is written across every row of a horizon once it is known,
    // which is what makes "no forward-return figure without its base rate" a
    // property of the store rather than a habit of the page.
    // see: `base_rate` is stored per row on purpose, and the reason is that the row is what gets read
    const string SetBaseRate = @"
        UPDATE forward_return SET base_rate = $base_rate WHERE horizon = $horizon;
    ";

    const string AppendRun = @"
        INSERT INTO run_log (
            run_id, stage, started_at, ended_at, outcome,
            rows_written, model_calls, network_requests, spend, detail)
        VALUES (
            $run_id, $stage, $started_at, $ended_at, $outcome,
            $rows_written, 0, 0, '0', $detail);
    ";

    readonly IClock clock;
    readonly string databaseFile;

    public ForwardReturnFiller(IClock clock, string databaseFile)
    {
        this.clock = clock;
        this.databaseFile = databaseFile;
    }

    sealed record Listing(string Ticker, string SessionDate, string Plan, Dictionary<string, string?> Outcomes);

    sealed record PlannedRow(string Ticker, string SessionDate, decimal Stop, decimal Target, Dictionary<string, string?> Outcomes);

    public async Task<ForwardReturnOutcome> RunAsync(string runId, CancellationToken cancellation = default)
    {
        var startedAt = clock.UtcNow;

        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        var listings = new List<Listing>();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = EveryListing;

            await using var reader = await command.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                var (ticker, session) = (reader.GetString(0), reader.GetString(1));

                if (listings.Count == 0 || listings[^1].Ticker != ticker || listings[^1].SessionDate != session)
                {
                    listings.Add(new Listing(ticker, session, reader.GetString(2), new Dictionary<string, string?>(StringComparer.Ordinal)));
                }

                if (!reader.IsDBNull(3))
                {
                    listings[^1].Outcomes[reader.GetString(3)] = reader.IsDBNull(4) ? null : reader.GetString(4);
                }
            }
        }

        var (written, matured, immature, kept) = (0, 0, 0, 0);
        var byHorizon = ForwardReturnSeries.Horizons.ToDictionary(
            horizon => horizon,
            _ => new List<string?>(),
            StringComparer.Ordinal);

        // One transaction around every write this stage makes, rather than one
        // per row. A row that commits on its own costs a disk sync and a sync
        // costs the same whatever the row holds, so the price is paid per row
        // and not per byte. Over the four names of the committed fixture that is
        // invisible; over 503 listings at three horizons each it was measured at
        // 334 seconds, which was more than half the night.
        //
        // It also makes the stage atomic, which is what it should have been: a
        // fill interrupted halfway left some horizons of some listings written
        // and the base rate belonging to none of them.
        await using var transaction = await connection.BeginTransactionAsync(cancellation);

        foreach (var listing in listings)
        {
            var open = new List<string>();

            foreach (var horizon in ForwardReturnSeries.Horizons)
            {
                if (listing.Outcomes.TryGetValue(horizon, out var stored) && Decided(stored))
                {
                    byHorizon[horizon].Add(stored);
                    kept++;
                }
                else
                {
                    open.Add(horizon);
                }
            }

            if (open.Count == 0)
            {
                continue;
            }

            var (stop, target, entryHigh) = PlanBounds(listing.Plan);

            // A setup with no plan never resolves, so it alone reads no bars. A
            // listing whose close the store no longer holds has nothing to measure
            // from, so it is handed no sessions and stays open.
            var scorable = open.Any(horizon => horizon != ForwardReturnSeries.Setup || stop is not null && target is not null);
            var origin = scorable
                ? await CloseOnAsync(connection, listing.Ticker, listing.SessionDate, cancellation)
                : null;
            var after = origin is null
                ? []
                : await SessionsAfterAsync(connection, listing.Ticker, listing.SessionDate, cancellation);

            foreach (var horizon in open)
            {
                var outcome = horizon switch
                {
                    ForwardReturnSeries.Setup => ForwardReturnSeries.OverSetup(
                        after, stop, target, entryHigh, origin?.Close, RawClose(listing, origin, stop, target)),
                    _ => ForwardReturnSeries.Over(
                        int.Parse(horizon, CultureInfo.InvariantCulture), after, origin?.Close ?? 0m),
                };

                // The bar a setup with no edge would have cleared, computed on the night the setup
                // resolves rather than when a record is read: it is simulated under the name's
                // trailing volatility and read against the calendar, and both are dropped a year
                // back while a record is read over four years of nights.
                // see: A setup's null win probability is calibrated from its own plan, and its planned break-even is shown beside it
                var calibrated = horizon == ForwardReturnSeries.Setup
                    ? await CalibratedAsync(connection, listing.Ticker, listing.SessionDate, outcome, origin, stop, target, after, cancellation)
                    : Calibration.None;

                if (!listing.Outcomes.ContainsKey(horizon) || outcome.Outcome is not null)
                {
                    await WriteAsync(connection, transaction, listing.Ticker, listing.SessionDate, outcome, calibrated, cancellation);
                    written++;
                }

                if (outcome.Outcome is null)
                {
                    immature++;
                }
                else
                {
                    matured++;
                }

                byHorizon[horizon].Add(outcome.Outcome);
            }
        }

        // The swing filter's rows, each scored on its own plan from the night's close, over the setup's cap
        // and, as context read by no verdict, over twenty sessions. The calibrated bar rides on the capped
        // horizon alone, the one a candidate's record reads. A plan whose close the night's own raw close
        // does not sit between its stop and its target was not one the night could enter at its close,
        // and it is counted as not scorable rather than scored from a later fill.
        // see: The swing filter's setups are scored on the swing trade's own plan from the listing close, and their first twenty sessions are context
        var planned = await PlannedRowsAsync(connection, transaction, cancellation);
        var notScorable = 0;

        foreach (var row in planned)
        {
            var open = ForwardReturnSeries.SwingHorizons
                .Where(horizon => !(row.Outcomes.TryGetValue(horizon, out var stored) && Decided(stored)))
                .ToArray();

            kept += ForwardReturnSeries.SwingHorizons.Length - open.Length;

            if (open.Length == 0)
            {
                continue;
            }

            var origin = await CloseOnAsync(connection, row.Ticker, row.SessionDate, cancellation);

            if (row.Stop >= row.Target || origin is { RawClose: { } raw } && (raw < row.Stop || raw > row.Target))
            {
                notScorable++;

                continue;
            }

            var after = origin is null ? [] : await SessionsAfterAsync(connection, row.Ticker, row.SessionDate, cancellation);
            var rawClose = origin is null ? null : RawClose(row.Ticker, row.SessionDate, origin, row.Stop, row.Target);

            foreach (var horizon in open)
            {
                var outcome = ForwardReturnSeries.OverSetup(
                    after, row.Stop, row.Target, null, origin?.Close, rawClose, horizon, ForwardReturnSeries.CapOf(horizon));

                var calibrated = horizon == ForwardReturnSeries.Swing
                    ? await CalibratedAsync(connection, row.Ticker, row.SessionDate, outcome, origin, row.Stop, row.Target, after, cancellation)
                    : Calibration.None;

                if (!row.Outcomes.ContainsKey(horizon) || outcome.Outcome is not null)
                {
                    await WriteAsync(connection, transaction, row.Ticker, row.SessionDate, outcome, calibrated, cancellation);
                    written++;
                }

                if (outcome.Outcome is null)
                {
                    immature++;
                }
                else
                {
                    matured++;
                }
            }
        }

        // The base rate per horizon, over every name-night rather than over the
        // rows where a reason fired. The setup horizon has none, because there
        // is no universe-wide notion of target before stop for a name with no
        // plan that night.
        foreach (var horizon in ForwardReturnSeries.Horizons)
        {
            if (horizon == ForwardReturnSeries.Setup)
            {
                continue;
            }

            var rate = ForwardReturnSeries.BaseRate(byHorizon[horizon]);

            await using var command = connection.CreateCommand();

            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = SetBaseRate;
            command.Parameters.AddWithValue("$horizon", horizon);
            command.Parameters.AddWithValue("$base_rate", (object?)rate ?? DBNull.Value);

            await command.ExecuteNonQueryAsync(cancellation);
        }

        await transaction.CommitAsync(cancellation);

        await RecordAsync(connection, runId, startedAt, listings.Count, written, matured, immature, kept, planned.Count, notScorable, cancellation);

        return new ForwardReturnOutcome(listings.Count, written, matured, immature, kept, planned.Count, notScorable);
    }

    static bool Decided(string? outcome) => outcome is not null;

    // Every writer stores a raw close beside the adjusted one, so a listing bar
    // without one is a fault in the store rather than a setup to score.
    static decimal? RawClose(Listing listing, (decimal Close, decimal? RawClose)? origin, decimal? stop, decimal? target) =>
        RawClose(listing.Ticker, listing.SessionDate, origin, stop, target);

    static decimal? RawClose(string ticker, string sessionDate, (decimal Close, decimal? RawClose)? origin, decimal? stop, decimal? target) =>
        origin is not { } bar || stop is null || target is null
            ? null
            : bar.RawClose is { } raw && raw > 0
                ? raw
                : throw new InvalidOperationException(
                    $"{ticker}'s bar for the listing of {sessionDate} carries no raw close above " +
                    "zero, so the adjustment its plan is scored at cannot be read.");

    static async Task<IReadOnlyList<PlannedRow>> PlannedRowsAsync(SqliteConnection connection, System.Data.Common.DbTransaction transaction, CancellationToken cancellation)
    {
        var rows = new List<PlannedRow>();

        await using var command = connection.CreateCommand();

        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = EveryPlannedGateRow;

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            var (ticker, session) = (reader.GetString(0), reader.GetString(1));

            if (rows.Count == 0 || rows[^1].Ticker != ticker || rows[^1].SessionDate != session)
            {
                rows.Add(new PlannedRow(ticker, session, Money.FromStorage(reader.GetString(2)), Money.FromStorage(reader.GetString(3)), new Dictionary<string, string?>(StringComparer.Ordinal)));
            }

            if (!reader.IsDBNull(4))
            {
                rows[^1].Outcomes[reader.GetString(4)] = reader.IsDBNull(5) ? null : reader.GetString(5);
            }
        }

        return rows;
    }

    // The stop, the first traded target and the entry zone's top edge as they
    // stood the night of the listing. Bars can be replayed and the plan cannot,
    // which is why the listing carries it rather than this reading tonight's
    // ladder.
    static (decimal? Stop, decimal? Target, decimal? EntryHigh) PlanBounds(string plan)
    {
        using var document = JsonDocument.Parse(plan);
        var root = document.RootElement;

        decimal? Read(string name) =>
            root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? decimal.Parse(value.GetString()!, CultureInfo.InvariantCulture)
                : null;

        return (Read("stop"), Read("firstTradedTarget"), Read("entryHigh"));
    }

    // What the calibration came to for one setup: the bar at the pinned round trip and at the
    // sensitivity beside it, what the plan put at risk from the fill, and whether the session it
    // resolved on was one the name reported on.
    sealed record Calibration(double? Null, double? Sensitivity, double? Risk, bool? OnEarnings)
    {
        public static Calibration None { get; } = new(null, null, null, null);
    }

    // The calibrated bar for a resolved setup, from the fill it was scored from.
    //
    // A setup nobody entered and one still open have no bar, for the reason they have no return:
    // the bar is what a plan with no edge would have done from the price this one was filled at,
    // and an unfilled plan was never in the market.
    static async Task<Calibration> CalibratedAsync(
        SqliteConnection connection,
        string ticker,
        string sessionDate,
        ForwardReturn outcome,
        (decimal Close, decimal? RawClose)? origin,
        decimal? stop,
        decimal? target,
        IReadOnlyList<ReturnBar> after,
        CancellationToken cancellation)
    {
        if (outcome.Outcome is not (ForwardReturnSeries.Win or ForwardReturnSeries.Loss)
            || outcome.EnteredAt is not { } fill || fill <= 0
            || outcome.SessionsLeft is not { } left || left <= 0
            || stop is not { } storedStop
            || target is not { } storedTarget
            || origin is not { } bar)
        {
            return Calibration.None;
        }

        // The plan keeps the scale the series had on the listing night, as the scoring reads it.
        // see: An outcome once decided is never rewritten, and a setup still in play is scored with its plan scaled by its listing session's adjustment factor
        var restated = bar.RawClose is { } raw && raw > 0 ? bar.Close / raw : 1m;
        var floor = storedStop * restated;
        var ceiling = storedTarget * restated;
        var session = DateOnly.ParseExact(sessionDate, "yyyy-MM-dd", CultureInfo.InvariantCulture);

        var risk = floor < fill ? Statistic.FromRatio((fill - floor) / fill) * 100 : (double?)null;
        var reported = OnEarnings(await PrintsAsync(connection, ticker, cancellation), after, outcome.ResolvedOn);

        var volatility = NullWin.Volatility(Changes(await ClosesToAsync(connection, ticker, sessionDate, cancellation)));

        if (volatility is not { } scatter || floor >= fill || ceiling <= fill)
        {
            return new Calibration(null, null, risk, reported);
        }

        var seed = NullWin.SeedFor(ticker, session.DayNumber);
        var below = Statistic.FromRatio(floor / fill);
        var above = Statistic.FromRatio(ceiling / fill);

        return new Calibration(
            NullWin.For(below, above, scatter, left, NullWin.CostBasisPoints, seed),
            NullWin.For(below, above, scatter, left, NullWin.SensitivityBasisPoints, seed),
            risk,
            reported);
    }

    // A session's change over the one before it, as the volatility reads them.
    static IReadOnlyList<double> Changes(IReadOnlyList<decimal> closes)
    {
        var changes = new List<double>(Math.Max(0, closes.Count - 1));

        for (var at = 1; at < closes.Count; at++)
        {
            changes.Add(closes[at - 1] <= 0 ? 0 : Statistic.FromRatio(closes[at] / closes[at - 1]));
        }

        return changes;
    }

    // Whether the session a setup resolved on was one the name reported on.
    //
    // A report before the open moves that day's session and one after the close moves the next,
    // and a timing the provider left unstated is read as the print's own day, which is the reading
    // that assumes least and is the one the ladder's earnings rule takes.
    // see: The second book is keyed to a dated event, and an earnings print is the only kind on file
    static bool? OnEarnings(
        IReadOnlyList<(DateOnly Date, string Timing)> prints,
        IReadOnlyList<ReturnBar> after,
        DateOnly? resolvedOn)
    {
        if (resolvedOn is not { } on)
        {
            return null;
        }

        foreach (var (date, timing) in prints)
        {
            var landed = timing == AfterTheClose
                ? after.FirstOrDefault(session => session.SessionDate > date).SessionDate
                : after.FirstOrDefault(session => session.SessionDate >= date).SessionDate;

            if (landed != default && landed == on)
            {
                return true;
            }
        }

        return false;
    }

    public const string AfterTheClose = "after";

    public const string EarningsKind = "earnings";

    static async Task<IReadOnlyList<(DateOnly Date, string Timing)>> PrintsAsync(
        SqliteConnection connection,
        string ticker,
        CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = PrintsFor;
        command.Parameters.AddWithValue("$ticker", ticker);
        command.Parameters.AddWithValue("$kind", EarningsKind);

        var prints = new List<(DateOnly, string)>();

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            prints.Add((
                DateOnly.ParseExact(reader.GetString(0), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                reader.IsDBNull(1) ? string.Empty : reader.GetString(1)));
        }

        return prints;
    }

    static async Task<IReadOnlyList<decimal>> ClosesToAsync(
        SqliteConnection connection,
        string ticker,
        string sessionDate,
        CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = ClosesTo;
        command.Parameters.AddWithValue("$ticker", ticker);
        command.Parameters.AddWithValue("$session_date", sessionDate);
        command.Parameters.AddWithValue("$sessions", NullWin.VolatilityWindow + 1);

        var closes = new List<decimal>();

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            closes.Add(Money.FromStorage(reader.GetString(0)));
        }

        closes.Reverse();

        return closes;
    }

    async Task WriteAsync(
        SqliteConnection connection,
        System.Data.Common.DbTransaction transaction,
        string ticker,
        string sessionDate,
        ForwardReturn outcome,
        Calibration calibrated,
        CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = Upsert;
        command.Parameters.AddWithValue("$ticker", ticker);
        command.Parameters.AddWithValue("$session_date", sessionDate);
        command.Parameters.AddWithValue("$horizon", outcome.Horizon);
        command.Parameters.AddWithValue("$outcome", (object?)outcome.Outcome ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$resolved_on",
            outcome.ResolvedOn is { } on ? on.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : DBNull.Value);
        command.Parameters.AddWithValue("$return_pct", (object?)outcome.ReturnPct ?? DBNull.Value);

        // The bar this plan set for itself, on the setup horizon's row alone and
        // only where the entry close it is measured from is known. It is written
        // beside the setup rather than summed per reason, because the row is what
        // the page reads and what a later session scores each setup's own wins
        // against.
        // see: A condition is judged against the break-even its own plan demands
        command.Parameters.AddWithValue("$break_even", (object?)outcome.BreakEven ?? DBNull.Value);

        // The calibrated bar beside the planned one, what the plan put at risk from the fill, and
        // whether the name reported on the session it resolved on. The setup horizon's alone: the
        // two session horizons ask what the market did and have no plan to simulate.
        // see: The calibrated null carries a round trip of ten basis points, and thirty is shown as a sensitivity
        command.Parameters.AddWithValue("$null_win", (object?)calibrated.Null ?? DBNull.Value);
        command.Parameters.AddWithValue("$null_win_at_sensitivity", (object?)calibrated.Sensitivity ?? DBNull.Value);
        command.Parameters.AddWithValue("$planned_risk", (object?)calibrated.Risk ?? DBNull.Value);
        command.Parameters.AddWithValue(
            "$on_earnings",
            calibrated.OnEarnings is { } reported ? reported ? 1 : 0 : DBNull.Value);

        // The base rate is written by the pass below rather than here, because
        // it is not known until every listing has been read.
        command.Parameters.AddWithValue("$base_rate", DBNull.Value);

        await command.ExecuteNonQueryAsync(cancellation);
    }

    static async Task<IReadOnlyList<ReturnBar>> SessionsAfterAsync(
        SqliteConnection connection,
        string ticker,
        string sessionDate,
        CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = SessionsAfter;
        command.Parameters.AddWithValue("$ticker", ticker);
        command.Parameters.AddWithValue("$session_date", sessionDate);
        command.Parameters.AddWithValue("$sessions", ForwardReturnSeries.SetupSessionCap);

        var bars = new List<ReturnBar>();

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            bars.Add(new ReturnBar(
                DateOnly.ParseExact(reader.GetString(0), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                Money.FromStorage(reader.GetString(1))));
        }

        return bars;
    }

    static async Task<(decimal Close, decimal? RawClose)?> CloseOnAsync(
        SqliteConnection connection,
        string ticker,
        string sessionDate,
        CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = CloseOn;
        command.Parameters.AddWithValue("$ticker", ticker);
        command.Parameters.AddWithValue("$session_date", sessionDate);

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        return await reader.ReadAsync(cancellation)
            ? (Money.FromStorage(reader.GetString(0)), reader.IsDBNull(1) ? null : Money.FromStorage(reader.GetString(1)))
            : null;
    }

    async Task RecordAsync(
        SqliteConnection connection,
        string runId,
        DateTimeOffset startedAt,
        int listings,
        int written,
        int matured,
        int immature,
        int kept,
        int plans,
        int notScorable,
        CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = AppendRun;
        command.Parameters.AddWithValue("$run_id", runId);
        command.Parameters.AddWithValue("$stage", Stage);
        command.Parameters.AddWithValue("$started_at", startedAt.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$ended_at", clock.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$outcome", "ok");
        command.Parameters.AddWithValue("$rows_written", written);

        // The kept and immature counts beside the matured one, because a night that
        // matured nothing and a night that wrote nothing are different nights and a
        // single total hides which.
        command.Parameters.AddWithValue(
            "$detail",
            $"{listings} listing(s), {written} row(s) written, {kept} kept as decided, " +
            $"{matured} newly matured, {immature} not yet matured; " +
            $"{plans} swing plan(s) read, {notScorable} not scorable from the night's close");

        await command.ExecuteNonQueryAsync(cancellation);
    }
}
