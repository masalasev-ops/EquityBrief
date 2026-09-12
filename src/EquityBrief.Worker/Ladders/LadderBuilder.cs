using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Components;
using EquityBrief.Core.Ladders;
using EquityBrief.Core.Levels;
using EquityBrief.Core.Indicators;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Swings;
using EquityBrief.Data.Swings;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using EquityBrief.Worker.Bars;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Ladders;

public sealed record LadderOutcome(
    int MembersConsidered,
    int RowsWritten,
    int RowsDropped,
    int Uptrend,
    int Downtrend,
    int Range,
    int NotClassified,
    int WithoutBars);

// The ladder builder. Reads the levels, the indicators and the calendar, and
// writes one row per index member per night carrying the trend state and the
// plan.
//
// A row for every member, whether or not it carries a tranche.
// see: A ladder row is written for every index member every night
//
// At 4.1 the plan is empty on every row and states why: the tranches, the stops,
// the exits and the event book arrive at 4.4 through 4.7. That is the shape the
// checkpoint owes rather than a placeholder. A row whose plan says the tranches
// are not built yet is a row a screen can draw and a reader can understand, and
// the alternative, writing no row until the plan exists, is what leaves a name
// without a yesterday on the night its band went ineligible.
//
// It makes no request and calls no model.
// see: The nightly run is arithmetic only
public sealed class LadderBuilder : IComponent
{
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.Level, Touch.Read),
            new StoreTouch(Store.Indicator, Touch.Read),
            new StoreTouch(Store.Bar, Touch.Read),
            new StoreTouch(Store.Calendar, Touch.Read),
            new StoreTouch(Store.Ladder, Touch.Insert | Touch.Update | Touch.Delete),
            new StoreTouch(Store.RunLog, Touch.Insert),
        ],
        Feeds: []);

    public const string Stage = "ladders";

    // The population is the index, not the names with bars. A member the store
    // holds nothing for still gets a row saying so, because the row count per
    // night is asserted against the index size and a name quietly absent would
    // make that count wrong in the direction nobody looks.
    //
    // Members on tonight's session, being joined by it where the join date is
    // known and not left by it.
    // see: An announced index change takes effect on its effective date, and a joining name is stored from the announcement
    const string CurrentMembers = @"
        SELECT ticker
        FROM membership
        WHERE index_code = $index
          AND (joined IS NULL OR joined <= $session)
          AND (""left"" IS NULL OR ""left"" > $session)
        ORDER BY ticker;
    ";

    const string LastSessionFor = @"
        SELECT session_date, close
        FROM bar
        WHERE ticker = $ticker
        ORDER BY session_date DESC
        LIMIT 1;
    ";

    // The retention drop, one year back from the as-of date this run wrote.
    //
    // Section 16 states one year over the six computed tables and SCHEMA gave
    // Delete to nobody until 4.2, which is contradiction A and contradiction H
    // in a third place: a retention window nobody owns is a table that grows
    // forever while the document says it does not.
    //
    // Nothing here can remove a row from inside a set it leaves standing. The
    // boundary is a date and every row below it goes, for every name at once,
    // which is the same shape BarFetcher's drop has and the same reason it is
    // sanctioned.
    // Keyed on the as-of date, and one row per index member every night
    // whether or not it carries a plan, so this is the table whose growth is
    // most exactly the index size times the nights.
    const string DropOlderThan = @"
        DELETE FROM ladder WHERE as_of < $oldest;
    ";

    const string BandsFor = @"
        SELECT low_edge, high_edge, role, immediate, strength, has_non_average_anchor
        FROM level
        WHERE ticker = $ticker
              AND as_of = (SELECT MAX(as_of) FROM level WHERE ticker = $ticker)
        ORDER BY low_edge;
    ";

    const string TypicalMoveFor = @"
        SELECT value
        FROM indicator
        WHERE ticker = $ticker AND session_date = $as_of AND name = $name AND value IS NOT NULL;
    ";

    // The lookback the tranche conditions are read over, newest last, so the
    // reader takes the window rather than the whole year.
    const string RecentFor = @"
        SELECT session_date, high, low, close
        FROM (SELECT session_date, high, low, close FROM bar WHERE ticker = $ticker
              ORDER BY session_date DESC LIMIT $window)
        ORDER BY session_date;
    ";

    // The next dated event on or after the as-of session. The calendar holds a
    // quarter ahead, so a name that reports inside that window has a row and one
    // that does not has none, which is the explicit blank rather than a guess.
    const string NextEventFor = @"
        SELECT event_date
        FROM calendar
        WHERE ticker = $ticker AND event_date >= $as_of
        ORDER BY event_date
        LIMIT 1;
    ";

    // The prints already on file, newest last. The calendar reaches a year back
    // since 4.8, so the two most recent are here for any name that has reported.
    const string PastEventsFor = @"
        SELECT event_date, timing
        FROM calendar
        WHERE ticker = $ticker AND event_date < $as_of
        ORDER BY event_date;
    ";

    // The sessions the earnings rule reads its moves off. It needs the whole
    // stored year rather than the condition window, because a print a year ago
    // is a print in the year the bars cover.
    const string SessionsFor = @"
        SELECT session_date, high, low, close
        FROM bar
        WHERE ticker = $ticker
        ORDER BY session_date;
    ";

    const string Upsert = @"
        INSERT INTO ladder (ticker, as_of, trend_state, plan)
        VALUES ($ticker, $as_of, $trend_state, $plan)
        ON CONFLICT (ticker, as_of) DO UPDATE SET
            trend_state = excluded.trend_state,
            plan = excluded.plan;
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

    public LadderBuilder(IClock clock, string databaseFile)
    {
        this.clock = clock;
        this.databaseFile = databaseFile;
    }

    public async Task<LadderOutcome> RunAsync(
        string indexCode,
        string runId,
        CancellationToken cancellation = default)
    {
        var startedAt = clock.UtcNow;

        await using var connection = new SqliteConnection($"Data Source={databaseFile}");
        await connection.OpenAsync(cancellation);

        var members = await MembersAsync(connection, indexCode, clock.SessionDateAt(startedAt), cancellation);

        var written = 0;
        var withoutBars = 0;
        var stop = new SeriesGapStop();
        var counts = TrendState.All.ToDictionary(state => state, _ => 0, StringComparer.Ordinal);

        // The newest session any name holds, which is the night the ladders are
        // about, read off the store for the reason the shortlist builder reads it
        // there: a replay on a weekend has a clock a session ahead of every bar.
        var newest = await NewestSessionAsync(connection, cancellation);

        await using var transaction = await connection.BeginTransactionAsync(cancellation);

        foreach (var ticker in members)
        {
            var session = await LastSessionAsync(connection, ticker, cancellation);

            // A member whose newest bar is older than the night's, being a name
            // the day's file carried nothing for. Its row is the night's, not
            // classified, with a plan saying why, as a member with no bar at all
            // gets: a trend and a plan read off an old bar are a chart the name
            // did not trade tonight. Until the phase 5 sign-off such a row was
            // dated by the name's own last bar, so EQR and PSTG held one ladder
            // row each, dated 2026-08-17 and 2026-04-16, and none for any night
            // after, which is what the listings did until the correction before
            // this one and what the reviewer found surviving here.
            // see: A ladder row is written for every index member every night
            if (session is { } last && newest is { } night && last.SessionDate < night)
            {
                var reason = "no bar for this session; the last session stored for the name is " +
                    last.SessionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

                counts[TrendState.NotClassified]++;

                await using var stale = connection.CreateCommand();

                stale.Transaction = (SqliteTransaction)transaction;
                stale.CommandText = Upsert;
                stale.Parameters.AddWithValue("$ticker", ticker);
                stale.Parameters.AddWithValue("$as_of", night.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                stale.Parameters.AddWithValue("$trend_state", TrendState.NotClassified);
                stale.Parameters.AddWithValue("$plan", Serialised(new Ladder([], [], null, reason, []), []));

                await stale.ExecuteNonQueryAsync(cancellation);

                written++;

                continue;
            }

            // A member whose stored series has an interior hole. Its row is
            // still written, because every member gets one, and its plan names
            // the gap's date rather than saying the trend could not be
            // classified: both are true and only one tells the reader what to
            // do about it. No stage above this one computed anything for such a
            // name, so the bands and the averages a plan reads are absent, and
            // a plan built from what is left would be a plan over a chart that
            // skips a session.
            // see: A ladder row is written for every index member every night
            // see: A gap is a session the exchange traded and the store does not hold
            var held = await SessionsAsync(connection, ticker, cancellation);

            if (stop.Stops(ticker, [.. held.Select(bar => bar.SessionDate)]))
            {
                var gapped = "the stored series has a gap at "
                    + stop.Gaps[^1].SessionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                    + ", so nothing is computed for this name until that session arrives";

                counts[TrendState.NotClassified]++;

                await using var row = connection.CreateCommand();

                row.Transaction = (SqliteTransaction)transaction;
                row.CommandText = Upsert;
                row.Parameters.AddWithValue("$ticker", ticker);
                row.Parameters.AddWithValue(
                    "$as_of",
                    (session?.SessionDate ?? clock.SessionDateAt(clock.UtcNow))
                        .ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                row.Parameters.AddWithValue("$trend_state", TrendState.NotClassified);
                row.Parameters.AddWithValue("$plan", Serialised(new Ladder([], [], null, gapped, []), []));

                await row.ExecuteNonQueryAsync(cancellation);

                written++;

                continue;
            }

            // A member with no stored bar at all. It still gets a row, dated by
            // the clock rather than by a session it does not have, because the
            // row count is the claim and a missing row is the failure the count
            // exists to catch.
            //
            // The session date and not the UTC date, which is what `IClock`
            // exists to keep apart. It read the UTC date until the phase 5
            // sign-off, so a night running after midnight in UTC, being any
            // evening after eight in New York, dated this row a day ahead of
            // every row beside it.
            var asOf = session?.SessionDate ?? clock.SessionDateAt(clock.UtcNow);

            var trend = session is { } bar
                ? await TrendClassifier.ForAsync(connection, ticker, bar.SessionDate, bar.Close, cancellation)
                : new Trend(TrendState.NotClassified, "no stored bars");

            var plan = session is { } priced
                ? await PlanAsync(connection, ticker, priced.SessionDate, priced.Close, trend, cancellation)
                : new Ladder([], [], null, trend.Reason, []);

            var prints = session is { } dated
                ? await EarningsRuleAsync(connection, ticker, dated.SessionDate, plan, cancellation)
                : [];

            if (session is null)
            {
                withoutBars++;
            }

            counts[trend.State]++;

            await using var command = connection.CreateCommand();

            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = Upsert;
            command.Parameters.AddWithValue("$ticker", ticker);
            command.Parameters.AddWithValue("$as_of", asOf.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            command.Parameters.AddWithValue("$trend_state", trend.State);
            command.Parameters.AddWithValue("$plan", Serialised(plan, prints));

            await command.ExecuteNonQueryAsync(cancellation);

            written++;
        }

        await transaction.CommitAsync(cancellation);

        // The retention drop, after the write and outside its transaction, so
        // a night that fails partway leaves the old sets standing rather than
        // dropping them for rows it never wrote.
        var dropped = await DroppedAsync(connection, await BoundaryAsync(connection, cancellation), cancellation);

        var outcome = new LadderOutcome(
            members.Count,
            written,
            dropped,
            counts[TrendState.Uptrend],
            counts[TrendState.Downtrend],
            counts[TrendState.Range],
            counts[TrendState.NotClassified],
            withoutBars);

        await RecordAsync(connection, runId, startedAt, outcome, stop.Report(), cancellation);

        return outcome;
    }

    // The plan for one name, from the bands the level builder wrote, the typical
    // day's move and the sessions the conditions are read over.
    //
    // A name with no band set at all gets an empty plan saying so rather than
    // one that looks placed and is not.
    async Task<Ladder> PlanAsync(
        SqliteConnection connection,
        string ticker,
        DateOnly asOf,
        decimal close,
        Trend trend,
        CancellationToken cancellation)
    {
        if (!trend.Classified)
        {
            return new Ladder([], [], null, trend.Reason, []);
        }

        var bands = await BandsAsync(connection, ticker, cancellation);

        if (bands.Count == 0)
        {
            return new Ladder([], [], null, "no bands are stored for this name", []);
        }

        var typicalMove = await TypicalMoveAsync(connection, ticker, asOf, cancellation);

        if (typicalMove is not { } move)
        {
            return new Ladder([], [], null, "no typical daily move is stored for this name", []);
        }

        var recent = await RecentAsync(connection, ticker, cancellation);

        // The swing lows the trailing stop follows, in session order and as of
        // the date, through the reader the level builder and the classifier
        // both use rather than a third copy of the clause.
        var lows = StoredSwings.AsOf(connection, ticker, asOf)
            .Where(swing => swing.Direction == SwingSeries.Low)
            .OrderBy(swing => swing.SessionDate)
            .Select(swing => swing.Price)
            .ToArray();

        // The next dated event on file, which is what the second book is keyed
        // to. A name with none gets no setups rather than setups built from a
        // guessed date.
        var nextEvent = await NextEventAsync(connection, ticker, asOf, cancellation);

        return LadderSeries.For(bands, close, move, recent, trend.State, lows, nextEvent);
    }

    // The earnings rule's figures for one name, which are a statement about the
    // plan rather than part of it: they say what the last two prints did to the
    // price and how that compares with what the plan is risking.
    async Task<IReadOnlyList<PastPrint>> EarningsRuleAsync(
        SqliteConnection connection,
        string ticker,
        DateOnly asOf,
        Ladder plan,
        CancellationToken cancellation)
    {
        var prints = await PastEventsAsync(connection, ticker, asOf, cancellation);

        if (prints.Count == 0)
        {
            return [];
        }

        var sessions = await SessionsAsync(connection, ticker, cancellation);

        // The distance the first tranche is risking, which is what the moves are
        // stated against. A plan with no tranche has none, and the moves are
        // still worth stating: a reader deciding whether to open a position at
        // all reads the same two numbers.
        var stopDistance = plan.Tranches.Count > 0 && plan.Tranches[0].Stop is { } stop
            ? LadderSeries.Midpoint(plan.Tranches[0]) - stop
            : (decimal?)null;

        return LadderSeries.EarningsRuleFor(prints, sessions, stopDistance);
    }

    // The plan, as SCHEMA's column describes it. Prices are written in the
    // storage form rather than as JSON numbers, because a JSON number is a
    // double and a price is not.
    //
    // An empty plan states why it is empty. An empty plan stating its reason and
    // an absent plan are different objects, and only the first is readable on a
    // screen.
    static string Serialised(Ladder plan, IReadOnlyList<PastPrint> prints) =>
        JsonSerializer.Serialize(new
        {
            tranches = plan.Tranches.Select(tranche => new
            {
                lowEdge = Money.ToStorage(tranche.LowEdge),
                highEdge = Money.ToStorage(tranche.HighEdge),
                condition = tranche.Condition.ToString(),
                stop = tranche.Stop is { } stop ? Money.ToStorage(stop) : null,
            }),
            exits = plan.Exits.Select(exit => new
            {
                lowEdge = Money.ToStorage(exit.LowEdge),
                highEdge = Money.ToStorage(exit.HighEdge),
                traded = exit.Traded,
                trailing = exit.Trailing,
                fraction = exit.Fraction,
                reason = exit.Reason,
            }),
            invalidation = plan.Invalidation is { } price ? Money.ToStorage(price) : null,
            events = plan.Events.Select(setup => new
            {
                name = setup.Name,
                eventDate = setup.EventDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                trigger = setup.Trigger,
                entry = Money.ToStorage(setup.Entry),
                stop = Money.ToStorage(setup.Stop),
                target = Money.ToStorage(setup.Target),
                proposal = true,
            }),
            // The arithmetic, derived from the plan above rather than stored
            // beside it. Every figure here is a function of prices the row
            // already carries, so nothing can drift between the two.
            // see: Code owns every number
            arithmetic = Figures(LadderSeries.ArithmeticFor(plan)),
            earningsRule = prints.Select(print => new
            {
                eventDate = print.EventDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                session = print.Session.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                move = Money.ToStorage(print.Move),
                shareOfStop = print.ShareOfStop is { } share ? Money.ToStorage(Round(share)) : null,
            }),
            reason = plan.Reason ?? (plan.Tranches.Count == 0
                ? "no tranche is placed"
                : plan.Events.Count == 0
                    ? "no dated event is on file, so the second book is empty"
                    : "every figure in the second book is a proposal"),
        });

    // The arithmetic in the storage form, so the figures a reader sees are the
    // figures the arithmetic produced rather than a rendering of them.
    static object Figures(PlanArithmetic arithmetic) => new
    {
        absent = arithmetic.Absent,
        firstEntry = Money.ToStorage(arithmetic.FirstEntry),
        firstRisk = Money.ToStorage(arithmetic.FirstRisk),
        firstReward = Money.ToStorage(arithmetic.FirstReward),
        blendedEntry = arithmetic.BlendedEntry is { } entry ? Money.ToStorage(entry) : null,
        blendedRisk = arithmetic.BlendedRisk is { } risk ? Money.ToStorage(risk) : null,
        blendedReward = arithmetic.BlendedReward is { } reward ? Money.ToStorage(reward) : null,
        firstRewardToRisk = arithmetic.FirstRewardToRisk is { } first ? Money.ToStorage(Round(first)) : null,
        blendedRewardToRisk = arithmetic.BlendedRewardToRisk is { } blended ? Money.ToStorage(Round(blended)) : null,
        breakEven = arithmetic.BreakEven is { } even ? Money.ToStorage(Round(even)) : null,
    };

    // A ratio is not a price and carries no storage precision of its own, so it
    // is quoted to the four places every other figure on the row carries.
    static decimal Round(decimal ratio) => Math.Round(ratio, PriceForm.Places, MidpointRounding.AwayFromZero);

    static async Task<IReadOnlyList<Level>> BandsAsync(
        SqliteConnection connection,
        string ticker,
        CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = BandsFor;
        command.Parameters.AddWithValue("$ticker", ticker);

        var bands = new List<Level>();

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            bands.Add(new Level(
                Money.FromStorage(reader.GetString(0)),
                Money.FromStorage(reader.GetString(1)),
                reader.GetString(2),
                reader.GetInt32(3) == 1,
                reader.GetInt32(4),
                reader.GetInt32(5) == 1,
                []));
        }

        return bands;
    }

    // The crossing a statistic makes to become a price, named for it as the
    // level builder's is. A typical day's move is stored REAL because it is a
    // statistic about prices and it is compared here against band edges.
    static async Task<decimal?> TypicalMoveAsync(
        SqliteConnection connection,
        string ticker,
        DateOnly asOf,
        CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = TypicalMoveFor;
        command.Parameters.AddWithValue("$ticker", ticker);
        command.Parameters.AddWithValue("$as_of", asOf.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$name", IndicatorSeries.Atr14);

        var value = await command.ExecuteScalarAsync(cancellation);

        return value is null or DBNull ? null : Statistic.ToPrice((double)value);
    }

    static async Task<IReadOnlyList<(DateOnly Date, EventTiming Timing)>> PastEventsAsync(
        SqliteConnection connection,
        string ticker,
        DateOnly asOf,
        CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = PastEventsFor;
        command.Parameters.AddWithValue("$ticker", ticker);
        command.Parameters.AddWithValue("$as_of", asOf.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        var prints = new List<(DateOnly, EventTiming)>();

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            prints.Add((
                DateOnly.ParseExact(reader.GetString(0), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                reader.GetString(1) switch
                {
                    "before" => EventTiming.Before,
                    "after" => EventTiming.After,
                    _ => EventTiming.Unstated,
                }));
        }

        return prints;
    }

    static async Task<IReadOnlyList<LadderBar>> SessionsAsync(
        SqliteConnection connection,
        string ticker,
        CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = SessionsFor;
        command.Parameters.AddWithValue("$ticker", ticker);

        var bars = new List<LadderBar>();

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            bars.Add(new LadderBar(
                DateOnly.ParseExact(reader.GetString(0), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                Money.FromStorage(reader.GetString(1)),
                Money.FromStorage(reader.GetString(2)),
                Money.FromStorage(reader.GetString(3))));
        }

        return bars;
    }

    static async Task<DateOnly?> NextEventAsync(
        SqliteConnection connection,
        string ticker,
        DateOnly asOf,
        CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = NextEventFor;
        command.Parameters.AddWithValue("$ticker", ticker);
        command.Parameters.AddWithValue("$as_of", asOf.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        var value = await command.ExecuteScalarAsync(cancellation);

        return value is null or DBNull
            ? null
            : DateOnly.ParseExact((string)value, "yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    static async Task<IReadOnlyList<LadderBar>> RecentAsync(
        SqliteConnection connection,
        string ticker,
        CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = RecentFor;
        command.Parameters.AddWithValue("$ticker", ticker);
        command.Parameters.AddWithValue("$window", LadderSeries.ConditionLookback);

        var bars = new List<LadderBar>();

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            bars.Add(new LadderBar(
                DateOnly.ParseExact(reader.GetString(0), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                Money.FromStorage(reader.GetString(1)),
                Money.FromStorage(reader.GetString(2)),
                Money.FromStorage(reader.GetString(3))));
        }

        return bars;
    }

    async Task<IReadOnlyList<string>> MembersAsync(
        SqliteConnection connection,
        string indexCode,
        DateOnly session,
        CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = CurrentMembers;
        command.Parameters.AddWithValue("$index", indexCode);
        command.Parameters.AddWithValue("$session", session.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        var members = new List<string>();

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            members.Add(reader.GetString(0));
        }

        return members;
    }

    static async Task<(DateOnly SessionDate, decimal Close)?> LastSessionAsync(
        SqliteConnection connection,
        string ticker,
        CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = LastSessionFor;
        command.Parameters.AddWithValue("$ticker", ticker);

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        return await reader.ReadAsync(cancellation)
            ? (DateOnly.ParseExact(reader.GetString(0), "yyyy-MM-dd", CultureInfo.InvariantCulture),
               Money.FromStorage(reader.GetString(1)))
            : null;
    }

    async Task RecordAsync(
        SqliteConnection connection,
        string runId,
        DateTimeOffset startedAt,
        LadderOutcome outcome,
        string gaps,
        CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = AppendRun;
        command.Parameters.AddWithValue("$run_id", runId);
        command.Parameters.AddWithValue("$stage", Stage);
        command.Parameters.AddWithValue("$started_at", startedAt.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$ended_at", clock.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        command.Parameters.AddWithValue("$outcome", "ok");
        command.Parameters.AddWithValue("$rows_written", outcome.RowsWritten);

        command.Parameters.AddWithValue(
            "$detail",
            $"{outcome.RowsWritten} row(s) for {outcome.MembersConsidered} member(s), " +
            $"{outcome.Uptrend} uptrend, {outcome.Downtrend} downtrend, {outcome.Range} range, " +
            $"{outcome.NotClassified} not classified, {outcome.WithoutBars} with no stored bars, " +
            $"{outcome.RowsDropped} dropped{gaps}");

        await command.ExecuteNonQueryAsync(cancellation);
    }

    // One year back from the newest stored session, which is the boundary
    // BarFetcher already drops bars at. Read from the store rather than passed
    // in, so a component run on its own drops the same rows a night would.
    static async Task<DateOnly?> BoundaryAsync(SqliteConnection connection, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT MAX(session_date) FROM bar;";

        var newest = await command.ExecuteScalarAsync(cancellation);

        return newest is null or DBNull
            ? null
            : DateOnly.ParseExact((string)newest, "yyyy-MM-dd", CultureInfo.InvariantCulture)
                .AddYears(-BarFetcher.RetentionYears);
    }

    static async Task<DateOnly?> NewestSessionAsync(SqliteConnection connection, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT MAX(session_date) FROM bar;";

        return await command.ExecuteScalarAsync(cancellation) is string newest
            ? DateOnly.ParseExact(newest, "yyyy-MM-dd", CultureInfo.InvariantCulture)
            : null;
    }

    static async Task<int> DroppedAsync(
        SqliteConnection connection,
        DateOnly? boundary,
        CancellationToken cancellation)
    {
        if (boundary is not { } oldest)
        {
            return 0;
        }

        await using var command = connection.CreateCommand();

        command.CommandText = DropOlderThan;
        command.Parameters.AddWithValue("$oldest", oldest.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        return await command.ExecuteNonQueryAsync(cancellation);
    }

}
