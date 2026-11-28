using System.Globalization;
using EquityBrief.Core.Cards;
using EquityBrief.Core.Components;
using EquityBrief.Core.Judge;
using EquityBrief.Core.Loop;
using EquityBrief.Core.Paper;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Paper;

// A night's order changes on the three indices, written after the AI stock picker's picks in its own process: each index's
// accounts worked by the core's one function the page reads them through, each live account's trades and the stocks it kept,
// each live rule's orders at a price, and the picker's picks as its account trades them; the lists written to the store and
// sent as one phone message where a topic is saved. Where the 4:30 pm check ran on the session, each order it read otherwise
// than the night is named. Without the word it prints the lists and writes and sends nothing.
// see: Each night ends with the order changes for every live rule, book and the AI stock picker, on Paper trading and in one phone message
public sealed class OrderChangesRun(IClock clock, string databaseFile, string dataRoot, IPhoneMessageFeed phone) : IComponent
{
    public const string Stage = "order-changes";

    public const string Title = "Order changes";

    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.JudgePick, Touch.Read),
            new StoreTouch(Store.JudgeRun, Touch.Read),
            new StoreTouch(Store.JudgePin, Touch.Read),
            new StoreTouch(Store.Bar, Touch.Read),
            new StoreTouch(Store.FillCheck, Touch.Read),
            new StoreTouch(Store.OrderChange, Touch.Insert),
            new StoreTouch(Store.TrailStop, Touch.Read | Touch.Insert),
            new StoreTouch(Store.RunLog, Touch.Insert),
        ],
        Feeds: [Feed.PhoneMessages]);

    // The sessions of the picker's picks read back: a pick holds at most a quarter, so half a year of nights covers every pick
    // still open.
    const int PickerDays = 190;

    public async Task<OrderSummary> RunAsync(DateOnly night, bool word, TextWriter output, CancellationToken cancellation = default)
    {
        var started = clock.UtcNow;
        var sessions = await SessionsAsync(night.AddDays(-PickerDays), cancellation);
        var previous = sessions.Where(session => session < night).DefaultIfEmpty(night).Max();
        var indices = new List<IndexOrders>();
        var raisedTonight = new List<(string Index, TrailStopRow Row)>();

        foreach (var index in PaperFiguresRun.Indices)
        {
            var name = Core.Candidates.OwnStepCandidate.NameOf(index);
            var worked = await PaperWork.WorkAsync(new PaperReads(databaseFile), index, name, index is not (FundHoldings.MidCapIndex or FundHoldings.SmallCapIndex), night, PaperFiguresRun.SettingsOf(dataRoot), yours: false);
            var sources = worked.Sources.ToDictionary(source => source.Key, StringComparer.Ordinal);
            var accounts = worked.Views
                .Where(view => view.Head)
                .Select(view => new AccountOrders(
                    SetupNames.WhoOf(view.Family, name),
                    sources.TryGetValue(view.Key, out var source) && source.Book,
                    [.. (view.Walk?.Trades ?? []).Where(taken => taken.Skipped is null).Select(taken => taken.Trade)],
                    source?.Unfilled ?? [],
                    view.Family))
                .ToArray();
            var closes = worked.Closes.Where(pair => pair.Key.Session == night).ToDictionary(pair => pair.Key.Ticker, pair => pair.Value, StringComparer.Ordinal);

            // From the operator's answers of 2026-11-28, item 4: each open trailing trade's stop as tonight raised it, written
            // with the word, and the stops earlier nights stored, which a sale is read against.
            // see: A trailing trade's stop is stored as each night raises it, and a sale above its first stop is sold on its trailing stop
            var calendar = worked.Closes.Keys.Select(key => key.Session).Distinct().Order().ToArray();

            foreach (var account in accounts.Where(account => !account.Book && account.Family is not null))
            {
                raisedTonight.AddRange(TrailStops.Of(account.Family!, account.Taken, night, calendar, worked.Close).Select(row => (index, row)));
            }

            indices.Add(new IndexOrders(index, name, worked.Read.Planned, accounts, await PickerAsync(index, night, cancellation), closes, await RaisedAsync(index, night, cancellation)));
        }

        var summary = OrderChanges.Of(night, previous, indices, await ChecksAsync(night, cancellation));

        foreach (var (heading, lines) in OrderChanges.Sections(summary))
        {
            output.WriteLine($"{Stage}: {heading}");

            foreach (var line in lines)
            {
                output.WriteLine($"{Stage}:   {line}");
            }
        }

        if (!word)
        {
            output.WriteLine($"{Stage}: nothing written or sent; give '--word' to write them and send the message");

            return summary;
        }

        var runId = FormattableString.Invariant($"{Stage}-{night:yyyyMMdd}-{started:yyyyMMddTHHmmssZ}");

        await WriteAsync(runId, summary, started, cancellation);
        await WriteRaisedAsync(runId, night, raisedTonight, started, cancellation);

        string sent;

        try
        {
            sent = await phone.SendAsync(FormattableString.Invariant($"{Title}, {OrderChanges.Day(night)}"), OrderChanges.Message(summary), cancellation)
                ? "the message sent"
                : "no message sent: no ntfy topic is saved";
        }
        catch (ProviderRefusal refusal)
        {
            sent = "the message not sent: " + refusal.Message;
        }

        var detail = FormattableString.Invariant($"{summary.Changes.Count} change(s) on {night:yyyy-MM-dd}, {string.Join(", ", OrderChanges.Lists.Select(list => $"{summary.Of(list.List).Count} {list.List}"))}; {sent}");

        await LogAsync(runId, started, summary.Changes.Count, detail, cancellation);
        output.WriteLine($"{Stage}: {detail}");

        return summary;
    }

    // The picker's picks on the index of its live version, the newest run's, each as its account trades it.
    async Task<IReadOnlyList<PickerOrder>> PickerAsync(string index, DateOnly night, CancellationToken cancellation)
    {
        var picks = new List<(DateOnly Night, int Rank, string Ticker, string Hold, string Shape, decimal Close, double Typical, string Key)>();

        await using var connection = await OpenAsync(cancellation);

        await using (var select = connection.CreateCommand())
        {
            select.CommandText = PickerPicks;
            select.Parameters.AddWithValue("$index", index);
            select.Parameters.AddWithValue("$from", Text(night.AddDays(-PickerDays)));
            select.Parameters.AddWithValue("$night", Text(night));

            await using var rows = await select.ExecuteReaderAsync(cancellation);

            while (await rows.ReadAsync(cancellation))
            {
                picks.Add((Day(rows.GetString(0)), rows.GetInt32(1), rows.GetString(2), rows.GetString(3), rows.GetString(4), Money.FromStorage(rows.GetString(5)), rows.GetDouble(6), rows.IsDBNull(7) ? string.Empty : rows.GetString(7)));
            }
        }

        if (picks.Count == 0)
        {
            return [];
        }

        var live = picks.OrderByDescending(pick => pick.Night).First().Key;
        var kept = picks.Where(pick => pick.Key == live).ToArray();
        var bars = new Dictionary<string, List<(DateOnly Session, double Open, double Close, double Low)>>(StringComparer.Ordinal);

        await using (var select = connection.CreateCommand())
        {
            select.CommandText = PickerBars;
            select.Parameters.AddWithValue("$from", Text(kept.Min(pick => pick.Night)));
            select.Parameters.AddWithValue("$night", Text(night));
            select.Parameters.AddWithValue("$tickers", System.Text.Json.JsonSerializer.Serialize(kept.Select(pick => pick.Ticker).Distinct(StringComparer.Ordinal)));

            await using var rows = await select.ExecuteReaderAsync(cancellation);

            while (await rows.ReadAsync(cancellation))
            {
                var ticker = rows.GetString(0);

                if (!bars.TryGetValue(ticker, out var list))
                {
                    bars[ticker] = list = [];
                }

                list.Add((Day(rows.GetString(1)), Statistic.FromPrice(Money.FromStorage(rows.GetString(2))), Statistic.FromPrice(Money.FromStorage(rows.GetString(3))), Statistic.FromPrice(Money.FromStorage(rows.GetString(4)))));
            }
        }

        var orders = new List<PickerOrder>();

        foreach (var pick in kept)
        {
            var range = JudgeAccounts.RangeOf(pick.Night, pick.Close, pick.Typical);

            if (!bars.TryGetValue(pick.Ticker, out var rows) || rows.FindIndex(row => row.Session == pick.Night) is var at && at < 0)
            {
                orders.Add(new PickerOrder(pick.Ticker, pick.Night, range, null, null, null));

                continue;
            }

            var after = rows.Skip(at).ToArray();
            var held = new JudgeBars([.. after.Select(row => row.Session)], [.. after.Select(row => row.Open)], [.. after.Select(row => row.Close)], [.. after.Select(row => row.Low)]);
            var placed = JudgeAccounts.Placed(pick.Ticker, pick.Rank, pick.Hold, pick.Shape, pick.Typical, held, _ => null, JudgeAccounts.WidthsOn(pick.Night));
            DateOnly? decided = placed.NotBought switch
            {
                BuyRanges.Below when held.Sessions.Count > 1 => held.Sessions[1],
                BuyRanges.Above when held.Sessions.Count > BuyRanges.OrderSessions => held.Sessions[BuyRanges.OrderSessions],
                _ => null,
            };

            orders.Add(new PickerOrder(pick.Ticker, pick.Night, range, placed.Trade, placed.NotBought, decided));
        }

        return orders;
    }

    // The orders the newest 4:30 pm check of the session read as filled, none where no check ran on it.
    async Task<IReadOnlyList<FillCheckRow>?> ChecksAsync(DateOnly night, CancellationToken cancellation)
    {
        await using var connection = await OpenAsync(cancellation);
        await using var select = connection.CreateCommand();

        select.CommandText = CheckRows;
        select.Parameters.AddWithValue("$night", Text(night));

        var rows = new List<FillCheckRow>();
        var ran = false;

        await using var read = await select.ExecuteReaderAsync(cancellation);

        while (await read.ReadAsync(cancellation))
        {
            ran = true;

            if (read.IsDBNull(5))
            {
                continue;
            }

            var (price, low) = (Money.FromStorage(read.GetString(4)), Money.FromStorage(read.GetString(5)));

            if (low <= price)
            {
                rows.Add(new FillCheckRow(read.GetString(0), read.GetString(1), read.GetString(2), Day(read.GetString(3)), price, low));
            }
        }

        return ran ? rows : null;
    }

    async Task<IReadOnlyList<DateOnly>> SessionsAsync(DateOnly from, CancellationToken cancellation)
    {
        await using var connection = await OpenAsync(cancellation);
        await using var select = connection.CreateCommand();

        select.CommandText = "SELECT DISTINCT session_date FROM bar WHERE session_date >= $from ORDER BY session_date;";
        select.Parameters.AddWithValue("$from", Text(from));

        var sessions = new List<DateOnly>();

        await using var rows = await select.ExecuteReaderAsync(cancellation);

        while (await rows.ReadAsync(cancellation))
        {
            sessions.Add(Day(rows.GetString(0)));
        }

        return sessions;
    }

    async Task WriteAsync(string runId, OrderSummary summary, DateTimeOffset at, CancellationToken cancellation)
    {
        await using var connection = await OpenAsync(cancellation);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellation);

        foreach (var (list, changes) in summary.Changes.GroupBy(change => change.List).Select(group => (group.Key, group.ToArray())))
        {
            for (var place = 0; place < changes.Length; place++)
            {
                await using var insert = connection.CreateCommand();

                insert.Transaction = transaction;
                insert.CommandText = InsertChange;
                insert.Parameters.AddWithValue("$run_id", runId);
                insert.Parameters.AddWithValue("$session", Text(summary.Night));
                insert.Parameters.AddWithValue("$list", list);
                insert.Parameters.AddWithValue("$place", place);
                insert.Parameters.AddWithValue("$index", changes[place].Index);
                insert.Parameters.AddWithValue("$who", changes[place].Who);
                insert.Parameters.AddWithValue("$ticker", changes[place].Ticker);
                insert.Parameters.AddWithValue("$words", changes[place].Words);
                insert.Parameters.AddWithValue("$checked", summary.Checked ? 1 : 0);
                insert.Parameters.AddWithValue("$at", Instant(at));
                await insert.ExecuteNonQueryAsync(cancellation);
            }
        }

        // A night with no change writes one row saying it was worked, so the page tells none tonight from none worked.
        if (summary.Changes.Count == 0)
        {
            await using var insert = connection.CreateCommand();

            insert.Transaction = transaction;
            insert.CommandText = InsertChange;
            insert.Parameters.AddWithValue("$run_id", runId);
            insert.Parameters.AddWithValue("$session", Text(summary.Night));
            insert.Parameters.AddWithValue("$list", OrderChanges.NewOrders);
            insert.Parameters.AddWithValue("$place", -1);
            insert.Parameters.AddWithValue("$index", string.Empty);
            insert.Parameters.AddWithValue("$who", string.Empty);
            insert.Parameters.AddWithValue("$ticker", string.Empty);
            insert.Parameters.AddWithValue("$words", OrderChanges.NoneTonight);
            insert.Parameters.AddWithValue("$checked", summary.Checked ? 1 : 0);
            insert.Parameters.AddWithValue("$at", Instant(at));
            await insert.ExecuteNonQueryAsync(cancellation);
        }

        await transaction.CommitAsync(cancellation);
    }

    async Task LogAsync(string runId, DateTimeOffset started, int rows, string detail, CancellationToken cancellation)
    {
        await using var connection = await OpenAsync(cancellation);
        await using var insert = connection.CreateCommand();

        insert.CommandText = @"
            INSERT INTO run_log (run_id, stage, started_at, ended_at, outcome, rows_written, model_calls, network_requests, spend, detail)
            VALUES ($run_id, $stage, $started_at, $ended_at, 'ok', $rows, 0, $requests, '0', $detail);";
        insert.Parameters.AddWithValue("$run_id", runId);
        insert.Parameters.AddWithValue("$stage", Stage);
        insert.Parameters.AddWithValue("$started_at", Instant(started));
        insert.Parameters.AddWithValue("$ended_at", Instant(clock.UtcNow));
        insert.Parameters.AddWithValue("$rows", rows);
        insert.Parameters.AddWithValue("$requests", phone.CanSend ? 1 : 0);
        insert.Parameters.AddWithValue("$detail", detail);
        await insert.ExecuteNonQueryAsync(cancellation);
    }

    async Task<SqliteConnection> OpenAsync(CancellationToken cancellation)
    {
        var connection = new SqliteConnection(StoreConnection.For(databaseFile));

        await connection.OpenAsync(cancellation);

        return connection;
    }

    const string PickerPicks = @"
        SELECT p.session_date, p.rank, p.ticker, p.hold, p.take_profit, p.last_close, p.typical_move, n.prompt_version
        FROM judge_pick p
        JOIN judge_run r ON r.run_id = p.run_id
        LEFT JOIN judge_pin n ON n.id = r.pin_id
        WHERE r.index_code = $index AND p.session_date >= $from AND p.session_date <= $night AND p.run_id NOT LIKE 'judge-replay-%' AND p.in_quarter = 1
        ORDER BY p.session_date, p.rank;";

    const string PickerBars = @"
        SELECT ticker, session_date, open, close, low FROM bar
        WHERE session_date >= $from AND session_date <= $night AND ticker IN (SELECT value FROM json_each($tickers))
        ORDER BY ticker, session_date;";

    const string CheckRows = @"
        SELECT index_code, who, ticker, listed, price, low FROM fill_check
        WHERE run_id = (SELECT run_id FROM fill_check WHERE session_date = $night ORDER BY run_id DESC LIMIT 1);";

    const string InsertChange = @"
        INSERT INTO order_change (run_id, session_date, list, place, index_code, who, ticker, words, checked, written_at)
        VALUES ($run_id, $session, $list, $place, $index, $who, $ticker, $words, $checked, $at);";

    // The trailing stops the nights before stored on an index, over the half year a trailing trade's time limit reaches.
    const string StoredRaised = @"
        SELECT family, ticker, listed_on, session_date, first_stop, stop FROM trail_stop
        WHERE index_code = $index AND session_date >= $from AND session_date < $night
        ORDER BY session_date, run_id;";

    const string InsertRaised = @"
        INSERT INTO trail_stop (run_id, session_date, index_code, family, ticker, listed_on, first_stop, stop, written_at)
        VALUES ($run_id, $session, $index, $family, $ticker, $listed, $first, $stop, $at);";

    // The stops earlier nights stored for an index's trailing trades.
    async Task<IReadOnlyList<TrailStopRow>> RaisedAsync(string index, DateOnly night, CancellationToken cancellation)
    {
        var rows = new List<TrailStopRow>();

        await using var connection = await OpenAsync(cancellation);
        await using var select = connection.CreateCommand();

        select.CommandText = StoredRaised;
        select.Parameters.AddWithValue("$index", index);
        select.Parameters.AddWithValue("$from", Text(night.AddDays(-PickerDays)));
        select.Parameters.AddWithValue("$night", Text(night));

        await using var read = await select.ExecuteReaderAsync(cancellation);

        while (await read.ReadAsync(cancellation))
        {
            rows.Add(new TrailStopRow(read.GetString(0), read.GetString(1), Day(read.GetString(2)), Day(read.GetString(3)), Money.FromStorage(read.GetString(4)), Money.FromStorage(read.GetString(5))));
        }

        return rows;
    }

    // Each open trailing trade's stop as tonight raised it, in one transaction.
    async Task WriteRaisedAsync(string runId, DateOnly night, IReadOnlyList<(string Index, TrailStopRow Row)> raised, DateTimeOffset at, CancellationToken cancellation)
    {
        await using var connection = await OpenAsync(cancellation);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellation);

        foreach (var (index, row) in raised)
        {
            await using var insert = connection.CreateCommand();

            insert.Transaction = transaction;
            insert.CommandText = InsertRaised;
            insert.Parameters.AddWithValue("$run_id", runId);
            insert.Parameters.AddWithValue("$session", Text(night));
            insert.Parameters.AddWithValue("$index", index);
            insert.Parameters.AddWithValue("$family", row.Family);
            insert.Parameters.AddWithValue("$ticker", row.Ticker);
            insert.Parameters.AddWithValue("$listed", Text(row.ListedOn));
            insert.Parameters.AddWithValue("$first", Money.ToStorage(row.FirstStop));
            insert.Parameters.AddWithValue("$stop", Money.ToStorage(row.Stop));
            insert.Parameters.AddWithValue("$at", Instant(at));
            await insert.ExecuteNonQueryAsync(cancellation);
        }

        await transaction.CommitAsync(cancellation);
    }

    static string Text(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    static DateOnly Day(string text) => DateOnly.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    static string Instant(DateTimeOffset at) => at.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
}
