using System.Globalization;
using EquityBrief.Core.Cards;
using EquityBrief.Core.Components;
using EquityBrief.Core.Families;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using EquityBrief.Worker.Indices;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Cards;

// The taken trades' follower. After the night's cards, each trade the operator took and no night has ended is followed
// over the stored closes from its fill's session to the night under its rule's own management: a provisional fill is
// replaced by its session's stored open once that bar is stored; the trade ends at its stop, its target or its cap, at
// the book's own sale for a sector heavyweight, at an exit the operator recorded, or at its last close as a member where
// the stock has left every index, whichever comes first. Every price is read at the series' scale as stored tonight, each
// day's own price taken there by that day's adjustment, so a split inside a trade ends nothing. Then the operator's
// record, one row an index and family over every trade taken, is written again. It reads stored rows alone, with no
// request and no model, and nothing that makes a pick reads what it writes.
// see: A taken trade's fill is the next session's open once its bar is stored, and the plan's buy marked provisional until then
// see: The operator's own record states its average result once twenty of its trades in a family and index have ended
// see: The nightly run is arithmetic only
public sealed class TakenFollower : IComponent
{
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.Membership, Touch.Read),
            new StoreTouch(Store.Bar, Touch.Read),
            new StoreTouch(Store.HeavyweightHolding, Touch.Read),
            new StoreTouch(Store.IndexHeavyweightHolding, Touch.Read),
            new StoreTouch(Store.DecisionCard, Touch.Read),
            new StoreTouch(Store.TakenTrade, Touch.Read | Touch.Update),
            new StoreTouch(Store.TakenRecord, Touch.Insert | Touch.Delete),
            new StoreTouch(Store.RunLog, Touch.Insert),
        ],
        Feeds: []);

    public const string Stage = "taken-follower";

    // The units a record and a result are read in.
    public const string Risks = "risks";

    public const string Percent = "percent";

    const string NewestSession = "SELECT MAX(session_date) FROM bar;";

    const string Following = @"
        SELECT t.ticker, t.taken_at, t.index_code, t.family, t.night, t.fill, t.fill_date, t.provisional, t.stop, t.target,
               t.trail, t.cap, t.exit_price, t.exit_date, c.entry
        FROM taken_trade t
        LEFT JOIN decision_card c
            ON c.index_code = t.index_code AND c.session_date = t.night AND c.family = t.family AND c.ticker = t.ticker
        WHERE t.ended_on IS NULL
        ORDER BY t.taken_at, t.ticker;
    ";

    const string BarsFrom = @"
        SELECT session_date, open, close, raw_close FROM bar
        WHERE ticker = $ticker AND session_date >= $from AND session_date <= $night
        ORDER BY session_date;
    ";

    const string LargeSale = @"
        SELECT ended_on FROM heavyweight_holding WHERE ticker = $ticker AND entered_on = $night AND ended_on IS NOT NULL;
    ";

    const string IndexSale = @"
        SELECT ended_on FROM index_heavyweight_holding
        WHERE index_code = $index AND ticker = $ticker AND entered_on = $night AND ended_on IS NOT NULL;
    ";

    const string MemberOn = @"
        SELECT COUNT(*) FROM membership
        WHERE ticker = $ticker AND (joined IS NULL OR joined <= $session) AND (""left"" IS NULL OR ""left"" > $session);
    ";

    const string Follow = @"
        UPDATE taken_trade SET fill = $fill, provisional = $provisional, followed_through = $night,
            ended_on = $ended_on, end_price = $end_price, end_reason = $end_reason, result = $result
        WHERE ticker = $ticker AND taken_at = $taken_at;
    ";

    const string EveryTaken = @"
        SELECT index_code, family, night, end_reason, result FROM taken_trade ORDER BY index_code, family;
    ";

    // The picks a family's rule listed on an index on one night, each a card the night stored.
    const string ListedOn = @"
        SELECT ticker, entry, stop, target, trail, cap FROM decision_card
        WHERE index_code = $index AND family = $family AND session_date = $night
        ORDER BY place, ticker;
    ";

    const string ClearRecords = "DELETE FROM taken_record;";

    const string InsertRecord = @"
        INSERT INTO taken_record (index_code, family, unit, won, lost, ended, open_trades, average,
            same_nights, rule_listed, rule_won, rule_lost, rule_ended, rule_average, night)
        VALUES ($index, $family, $unit, $won, $lost, $ended, $open_trades, $average,
            $same_nights, $rule_listed, $rule_won, $rule_lost, $rule_ended, $rule_average, $night);
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

    public TakenFollower(IClock clock, string databaseFile)
    {
        this.clock = clock;
        this.databaseFile = databaseFile;
    }

    // One taken trade as the follower reads it, its prices as the operator and the card stated them.
    sealed record Taken(string Ticker, string TakenAt, string Index, string Family, DateOnly Night, decimal Fill, DateOnly FillDate, bool Provisional, decimal? Stop, decimal? Target, decimal? Trail, int? Cap, decimal? ExitPrice, DateOnly? ExitDate, decimal? Buy);

    // One stored session: its open and close at the series' scale tonight and the factor taking a price as traded that
    // day to that scale.
    sealed record Session(DateOnly Day, decimal Open, decimal Close, decimal Factor);

    public async Task<TakenFollowed> RunAsync(string runId, CancellationToken cancellation = default)
    {
        var startedAt = clock.UtcNow;

        try
        {
            await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
            await connection.OpenAsync(cancellation);

            if (await ScalarAsync(connection, NewestSession, [], cancellation) is not string newest)
            {
                await AppendAsync(connection, runId, startedAt, 0, "ok", "no session: the store holds no bar", cancellation);

                return new TakenFollowed(0, 0, 0);
            }

            var night = Date(newest);
            var taken = await TakenAsync(connection, cancellation);
            var (followed, ended, filled) = (0, 0, 0);

            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellation);

            foreach (var trade in taken)
            {
                var sessions = await SessionsAsync(connection, transaction, trade, night, cancellation);
                var outcome = await FollowAsync(connection, transaction, trade, sessions, night, cancellation);

                await ExecuteAsync(connection, transaction, Follow,
                [
                    ("$ticker", trade.Ticker),
                    ("$taken_at", trade.TakenAt),
                    ("$fill", outcome.Fill.ToString(CultureInfo.InvariantCulture)),
                    ("$provisional", outcome.Provisional ? 1 : 0),
                    ("$night", Stamp(night)),
                    ("$ended_on", outcome.EndedOn is { } on ? Stamp(on) : DBNull.Value),
                    ("$end_price", outcome.EndPrice is { } price ? price.ToString(CultureInfo.InvariantCulture) : DBNull.Value),
                    ("$end_reason", (object?)outcome.Reason ?? DBNull.Value),
                    ("$result", outcome.Result is { } result ? result : DBNull.Value),
                ], cancellation);

                followed++;
                ended += outcome.EndedOn is null ? 0 : 1;
                filled += trade.Provisional && !outcome.Provisional ? 1 : 0;
            }

            var records = await RecordAsync(connection, transaction, night, cancellation);

            await transaction.CommitAsync(cancellation);
            await AppendAsync(connection, runId, startedAt, followed + records, "ok",
                FormattableString.Invariant($"{followed} taken trade(s) followed, {ended} ended, {filled} provisional fill(s) replaced by the stored open; {records} record(s)"),
                cancellation);

            return new TakenFollowed(followed, ended, filled);
        }
        catch (Exception failure) when (!cancellation.IsCancellationRequested)
        {
            await AppendAfterAFailureAsync(runId, startedAt, "the follower stopped on " + IndexFamilies.Cause(failure), cancellation);

            return new TakenFollowed(0, 0, 0, IndexFamilies.Cause(failure));
        }
    }

    sealed record Followed(decimal Fill, bool Provisional, DateOnly? EndedOn, decimal? EndPrice, string? Reason, double? Result);

    // A trade the operator took, or with planned the rule's own pick bought at the plan's buy as its night traded, the
    // way the rule's own record reads its trades, so the two differ by the operator's choices and fills alone.
    async Task<Followed> FollowAsync(SqliteConnection connection, SqliteTransaction transaction, Taken trade, IReadOnlyList<Session> sessions, DateOnly night, CancellationToken cancellation, bool planned = false)
    {
        var fill = trade.Fill;
        var provisional = trade.Provisional;
        var at = sessions.ToDictionary(session => session.Day);

        // The plan's buy stands in for the fill until the fill's session's open is stored, the price that session traded at.
        if (provisional && at.TryGetValue(trade.FillDate, out var opened))
        {
            fill = PriceForm.Round(opened.Open / opened.Factor);
            provisional = false;
        }

        // The plan's prices as the night traded, and the fill as its session traded, each at the series' scale tonight.
        if (!at.TryGetValue(trade.Night, out var listed) || !at.TryGetValue(trade.FillDate, out var bought))
        {
            return new Followed(fill, provisional, null, null, null, null);
        }

        decimal? Scaled(decimal? price) => price * listed.Factor;

        var entry = planned ? fill * listed.Factor : fill * bought.Factor;
        var stop = Scaled(trade.Stop);
        var held = sessions.Where(session => session.Day >= trade.FillDate).ToArray();
        var walked = trade.Family == HeavyweightRule.Name
            ? null
            : TakenWalk.Follow([.. held.Select(session => session.Close)], stop, Scaled(trade.Target), Scaled(trade.Trail), trade.Cap);

        var ends = new List<(DateOnly On, string Reason, decimal Close, decimal Raw)>();

        if (walked is { } rule)
        {
            var end = held[rule.Session - 1];

            ends.Add((end.Day, rule.Reason, end.Close, end.Close / end.Factor));
        }

        if (trade.Family == HeavyweightRule.Name && await SoldAsync(connection, transaction, trade, cancellation) is { } sold && at.TryGetValue(sold, out var sale))
        {
            ends.Add((sold, TakenWalk.Sold, sale.Close, sale.Close / sale.Factor));
        }

        if (trade.ExitDate is { } exited && trade.ExitPrice is { } exitPrice && exited <= night)
        {
            var factor = held.LastOrDefault(session => session.Day <= exited)?.Factor ?? bought.Factor;

            ends.Add((exited, TakenWalk.Exited, exitPrice * factor, exitPrice));
        }

        if (held.Length > 0 && await ScalarAsync(connection, MemberOn, [("$ticker", trade.Ticker), ("$session", Stamp(night))], cancellation, transaction) is long members && members == 0)
        {
            var last = held[^1];

            ends.Add((last.Day, TakenWalk.Left, last.Close, last.Close / last.Factor));
        }

        if (ends.Count == 0)
        {
            return new Followed(fill, provisional, null, null, null, null);
        }

        var first = ends.OrderBy(end => end.On).First();
        var risk = TakenWalk.Risk(entry, stop, Scaled(trade.Buy));
        double? result = risk is { } distance
            ? Statistic.FromRatio((first.Close - entry) / distance)
            : trade.Stop is null && entry > 0m ? Statistic.FromRatio(((first.Close / entry) - 1m) * 100m) : null;

        return new Followed(fill, provisional, first.On, PriceForm.Round(first.Raw), first.Reason, result);
    }

    async Task<DateOnly?> SoldAsync(SqliteConnection connection, SqliteTransaction transaction, Taken trade, CancellationToken cancellation)
    {
        var sold = trade.Index == IndexFamilies.LargeIndex
            ? await ScalarAsync(connection, LargeSale, [("$ticker", trade.Ticker), ("$night", Stamp(trade.Night))], cancellation, transaction)
            : await ScalarAsync(connection, IndexSale, [("$index", trade.Index), ("$ticker", trade.Ticker), ("$night", Stamp(trade.Night))], cancellation, transaction);

        return sold is string on ? Date(on) : null;
    }

    // The operator's record over every trade taken, one row an index and family: a rule with a target counts its trades
    // won at the target and lost at the stop, a trailing rule and the sector heavyweights each trade ended, and every rule
    // its open trades; the average result over the ended once there are enough of them to read. Beside it the same counts
    // over the rule's own picks listed on the nights the operator took one, each bought at the plan's buy, which separates
    // the operator's choices and fills from the market of those weeks.
    async Task<int> RecordAsync(SqliteConnection connection, SqliteTransaction transaction, DateOnly night, CancellationToken cancellation)
    {
        var trades = new List<(string Index, string Family, DateOnly Night, string? Reason, double? Result)>();

        await using (var command = Command(connection, transaction, EveryTaken, []))
        await using (var reader = await command.ExecuteReaderAsync(cancellation))
        {
            while (await reader.ReadAsync(cancellation))
            {
                trades.Add((reader.GetString(0), reader.GetString(1), Date(reader.GetString(2)), reader.IsDBNull(3) ? null : reader.GetString(3), reader.IsDBNull(4) ? null : reader.GetDouble(4)));
            }
        }

        await ExecuteAsync(connection, transaction, ClearRecords, [], cancellation);

        var written = 0;

        foreach (var group in trades.GroupBy(trade => (trade.Index, trade.Family)))
        {
            var rule = SetupFamilies.Named(group.Key.Family);
            var scored = group.Key.Family != HeavyweightRule.Name && rule is { Trails: false };
            var ended = group.Where(trade => trade.Reason is not null).Select(trade => (trade.Reason, trade.Result)).ToArray();
            var nights = group.Select(trade => trade.Night).Distinct().Order().ToArray();
            var listed = new List<(string? Reason, double? Result)>();

            foreach (var on in nights)
            {
                listed.AddRange(await ListedAsync(connection, transaction, group.Key.Index, group.Key.Family, on, night, cancellation));
            }

            var ruleEnded = listed.Where(pick => pick.Reason is not null).ToArray();

            await ExecuteAsync(connection, transaction, InsertRecord,
            [
                ("$index", group.Key.Index),
                ("$family", group.Key.Family),
                ("$unit", group.Key.Family == HeavyweightRule.Name ? Percent : Risks),
                ("$won", scored ? ended.Count(trade => trade.Reason == TakenWalk.Targeted) : 0),
                ("$lost", scored ? ended.Count(trade => trade.Reason == TakenWalk.Stopped) : 0),
                ("$ended", ended.Length),
                ("$open_trades", group.Count() - ended.Length),
                ("$average", Average(ended)),
                ("$same_nights", nights.Length),
                ("$rule_listed", listed.Count),
                ("$rule_won", scored ? ruleEnded.Count(pick => pick.Reason == TakenWalk.Targeted) : 0),
                ("$rule_lost", scored ? ruleEnded.Count(pick => pick.Reason == TakenWalk.Stopped) : 0),
                ("$rule_ended", ruleEnded.Length),
                ("$rule_average", Average(ruleEnded)),
                ("$night", Stamp(night)),
            ], cancellation);

            written++;
        }

        return written;

        // The mean result over the ended, read once enough have ended, each count read on its own.
        static object Average(IReadOnlyList<(string? Reason, double? Result)> ended)
        {
            var results = ended.Where(trade => trade.Result is not null).Select(trade => trade.Result!.Value).ToArray();

            return ended.Count >= TakenWalk.RecordMinimum && results.Length > 0 ? results.Average() : DBNull.Value;
        }
    }

    // Each pick the rule listed on a night, followed to tonight from the plan's buy, its fill session the first stored after
    // the night; a pick no session has followed yet is listed and open.
    async Task<IReadOnlyList<(string? Reason, double? Result)>> ListedAsync(SqliteConnection connection, SqliteTransaction transaction, string index, string family, DateOnly on, DateOnly night, CancellationToken cancellation)
    {
        var cards = new List<Taken>();

        await using (var command = Command(connection, transaction, ListedOn, [("$index", index), ("$family", family), ("$night", Stamp(on))]))
        await using (var reader = await command.ExecuteReaderAsync(cancellation))
        {
            while (await reader.ReadAsync(cancellation))
            {
                decimal? Price(int column) => reader.IsDBNull(column) ? null : Money(reader.GetString(column));

                var buy = Money(reader.GetString(1));

                cards.Add(new Taken(reader.GetString(0), string.Empty, index, family, on, buy, on, false, Price(2), Price(3), Price(4), reader.IsDBNull(5) ? null : reader.GetInt32(5), null, null, buy));
            }
        }

        var picks = new List<(string? Reason, double? Result)>();

        foreach (var card in cards)
        {
            var sessions = await SessionsAsync(connection, transaction, card, night, cancellation);

            if (sessions.FirstOrDefault(session => session.Day > on) is not { } first)
            {
                picks.Add((null, null));

                continue;
            }

            var outcome = await FollowAsync(connection, transaction, card with { FillDate = first.Day }, sessions, night, cancellation, planned: true);

            picks.Add((outcome.Reason, outcome.Result));
        }

        return picks;
    }

    static async Task<IReadOnlyList<Taken>> TakenAsync(SqliteConnection connection, CancellationToken cancellation)
    {
        var taken = new List<Taken>();

        await using var command = Command(connection, null, Following, []);
        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            decimal? Price(int column) => reader.IsDBNull(column) ? null : Money(reader.GetString(column));

            taken.Add(new Taken(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                Date(reader.GetString(4)),
                Money(reader.GetString(5)),
                Date(reader.GetString(6)),
                reader.GetInt64(7) == 1,
                Price(8),
                Price(9),
                Price(10),
                reader.IsDBNull(11) ? null : reader.GetInt32(11),
                Price(12),
                reader.IsDBNull(13) ? null : Date(reader.GetString(13)),
                Price(14)));
        }

        return taken;
    }

    // The stock's sessions from the card's night to tonight, each with the factor taking its own day's prices to tonight's
    // scale: the stored close over the close as it traded, one where the store holds no raw close.
    static async Task<IReadOnlyList<Session>> SessionsAsync(SqliteConnection connection, SqliteTransaction transaction, Taken trade, DateOnly night, CancellationToken cancellation)
    {
        var sessions = new List<Session>();

        await using var command = Command(connection, transaction, BarsFrom, [("$ticker", trade.Ticker), ("$from", Stamp(trade.Night)), ("$night", Stamp(night))]);
        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            var close = Money(reader.GetString(2));
            var raw = reader.IsDBNull(3) ? close : Money(reader.GetString(3));

            sessions.Add(new Session(Date(reader.GetString(0)), Money(reader.GetString(1)), close, raw > 0m ? close / raw : 1m));
        }

        return sessions;
    }

    async Task AppendAsync(SqliteConnection connection, string runId, DateTimeOffset startedAt, int rows, string outcome, string detail, CancellationToken cancellation) =>
        await ExecuteAsync(connection, null, AppendRun,
        [
            ("$run_id", runId),
            ("$stage", Stage),
            ("$started_at", startedAt.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)),
            ("$ended_at", clock.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)),
            ("$outcome", outcome),
            ("$rows_written", rows),
            ("$detail", detail),
        ], cancellation);

    // The stage's row after a failure, on a connection of its own, and none where the store will not take it: recording
    // the failure is never what stops the night.
    async Task AppendAfterAFailureAsync(string runId, DateTimeOffset startedAt, string detail, CancellationToken cancellation)
    {
        try
        {
            await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
            await connection.OpenAsync(cancellation);
            await AppendAsync(connection, runId, startedAt, 0, "not computed", detail, cancellation);
        }
        catch (Exception) when (!cancellation.IsCancellationRequested)
        {
        }
    }

    static SqliteCommand Command(SqliteConnection connection, SqliteTransaction? transaction, string sql, IReadOnlyList<(string Name, object Value)> parameters)
    {
        var command = connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText = sql;

        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        return command;
    }

    static async Task ExecuteAsync(SqliteConnection connection, SqliteTransaction? transaction, string sql, IReadOnlyList<(string Name, object Value)> parameters, CancellationToken cancellation)
    {
        await using var command = Command(connection, transaction, sql, parameters);

        await command.ExecuteNonQueryAsync(cancellation);
    }

    static async Task<object?> ScalarAsync(SqliteConnection connection, string sql, IReadOnlyList<(string Name, object Value)> parameters, CancellationToken cancellation, SqliteTransaction? transaction = null)
    {
        await using var command = Command(connection, transaction, sql, parameters);

        return await command.ExecuteScalarAsync(cancellation);
    }

    static decimal Money(string stored) => decimal.Parse(stored, NumberStyles.Number | NumberStyles.AllowExponent, CultureInfo.InvariantCulture);

    static string Stamp(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    static DateOnly Date(string stamp) => DateOnly.ParseExact(stamp, "yyyy-MM-dd", CultureInfo.InvariantCulture);
}

// What a night's follower did: the trades it followed, those it ended, the provisional fills it replaced, and why it
// stopped where it did.
public sealed record TakenFollowed(int Followed, int Ended, int Filled, string? Fault = null);
