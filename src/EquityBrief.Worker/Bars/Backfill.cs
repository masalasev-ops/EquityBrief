using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Bars;
using EquityBrief.Core.Components;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Bars;

// Backfill.
//
// Fetches one year for any member holding no history, one request per name, and
// never again for a name that already holds its year. A single day of bars
// computes nothing, so the store has to start full, and repeating this would
// spend hundreds of requests fetching bars already held.
//
// It runs per ticker deliberately, which is the opposite shape from the nightly
// fetch. The bulk endpoint is priced per request and the historical endpoint per
// name, so a year of past sessions costs 500 calls this way against 25,000
// through the bulk one
// (see: Bars come from EODHD, bulk nightly and per ticker for the backfill).
public sealed class Backfill(
    IHistoricalBarFeed feed,
    IClock clock,
    string databaseFile) : IComponent
{
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.Membership, Touch.Read),
            new StoreTouch(Store.Bar, Touch.Read | Touch.Insert),
            new StoreTouch(Store.RunLog, Touch.Read | Touch.Insert),
        ],
        Feeds: [Feed.HistoricalPrice]);

    public const string Stage = "backfill";
    public const string Source = "historical";

    // The outcome of a night that leaves a member holding no year, which puts the
    // stage on the run page's failed region on every night one is left.
    public const string Partial = "partial";

    // A name the backfill stored nothing for is asked for again on the refetch's
    // schedule: on each of these nights after the first, then on the first night
    // whose session is this many days after the session it was last asked for,
    // until one stores its year or the name leaves the index.
    // see: A name the backfill stored nothing for is asked for again on the five nights after and weekly after that, and its page and the run page say so until one stores its year
    public const int RetryNights = CorporateActionChecker.RetryNights;

    public const int WeeklyRetryDays = CorporateActionChecker.WeeklyRetryDays;

    // The names a backfill is owed for, being every name that has not left by
    // tonight's session: the fetch's population, for the fetch's reason. A name
    // that has left keeps its stored history and is not fetched again.
    //
    // `left IS NULL` until the phase 5 sign-off, which owed nothing to a name
    // whose leave was announced and not yet effective, so three names still in
    // the index had no bar at all.
    // see: An announced index change takes effect on its effective date, and a joining name is stored from the announcement
    const string CurrentMembers = @"
        SELECT ticker FROM membership
        WHERE index_code = $index_code AND (""left"" IS NULL OR ""left"" > $session)
        ORDER BY ticker;
    ";

    // Which of them already hold history. Any stored bar counts: the rule is
    // never repeated for a name that already holds its year, and a name with a
    // short series is a gap question rather than a backfill question
    // (see: A gap is a session the exchange traded and the store does not hold).
    const string TickersHoldingBars = "SELECT DISTINCT ticker FROM bar;";

    // The earlier rows of this stage that name what they asked for, which is the
    // record the schedule reads.
    const string EarlierRows = @"
        SELECT detail FROM run_log WHERE stage = $stage AND detail LIKE '{%';
    ";

    // Insert only. Bars are append-only, so a name already holding a session
    // keeps what it has rather than having it rewritten.
    const string InsertBar = @"
        INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at, raw_close)
        VALUES ($ticker, $session_date, $open, $high, $low, $close, $volume, $source, $observed_at, $raw_close)
        ON CONFLICT (ticker, session_date) DO NOTHING;
    ";

    const string AppendRun = @"
        INSERT INTO run_log (
            run_id, stage, started_at, ended_at, outcome,
            rows_written, model_calls, network_requests, spend, detail)
        VALUES (
            $run_id, $stage, $started_at, $ended_at, $outcome,
            $rows_written, 0, $network_requests, '0', $detail);
    ";

    // Counted before and after rather than by matching the observation instant.
    //
    // Keying on observed_at looked equivalent and is not: two runs sharing an
    // instant, which is what a fixed clock produces and what a fast machine can
    // produce for real, would each count the other's rows. A delta over the
    // table is measured from the store, which is what SCHEMA requires, and does
    // not rest on the instant happening to be unique.
    const string BarCount = "SELECT COUNT(*) FROM bar;";

    public async Task<BackfillOutcome> RunAsync(
        string indexCode,
        string runId,
        CancellationToken cancellationToken = default)
    {
        var startedAt = clock.UtcNow;
        var observedAt = startedAt.ToString("O");
        var to = clock.SessionDateAt(startedAt);
        var from = to.AddYears(-1);

        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync(cancellationToken);

        var members = await Read(connection, CurrentMembers, indexCode, cancellationToken, to);
        var holding = await Read(connection, TickersHoldingBars, null, cancellationToken);

        var owed = members.Except(holding, StringComparer.OrdinalIgnoreCase).ToArray();
        var asked = await AskedAsync(connection, cancellationToken);

        // A night is a session, and a night run again for its session decides as
        // that night did: only the sessions before it count.
        bool Due(string ticker)
        {
            if (!asked.TryGetValue(ticker, out var sessions))
            {
                return true;
            }

            var before = sessions.Where(session => session < to).ToArray();

            return before.Length <= RetryNights || to >= before.Max().AddDays(WeeklyRetryDays);
        }

        var due = owed.Where(Due).ToArray();
        var before = feed.Requests;
        var barsBefore = await Count(connection, cancellationToken);

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        // Fetched before anything is stored, so the calendar the refusal reads
        // is the whole set rather than whichever names happened to be stored
        // first. A series checked against a calendar built only from the names
        // already inserted would find fewer gaps the earlier it was checked.
        var fetched = new Dictionary<string, IReadOnlyList<ProviderBar>>(StringComparer.OrdinalIgnoreCase);

        foreach (var ticker in due)
        {
            fetched[ticker] = await feed.BarsAsync(ticker, from, to, cancellationToken);
        }

        // The refusal. Per name per night: a name whose series arrives with an
        // interior session missing is not stored, its stored series is left as
        // it was, and the gap's date is named. Everything else is stored.
        // see: Bars are never interpolated
        var series = fetched.ToDictionary(
            entry => entry.Key,
            entry => (IReadOnlyCollection<DateOnly>)[.. entry.Value.Select(bar => bar.SessionDate)],
            StringComparer.OrdinalIgnoreCase);

        // A name asked for alone has no series beside it to read the calendar off, so its
        // year is read against the exchange's own.
        var gaps = TradingCalendar.CanDetect(series)
            ? TradingCalendar.GapsIn(series)
            : [.. series.SelectMany(entry => TradingCalendar.Check(entry.Value).Missing.Select(session => new Gap(entry.Key, session)))];

        var refused = gaps
            .GroupBy(gap => gap.Ticker, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Select(gap => gap.SessionDate).ToArray(), StringComparer.OrdinalIgnoreCase);

        foreach (var ticker in due.Where(name => !refused.ContainsKey(name)))
        {
            foreach (var bar in fetched[ticker])
            {
                await using var insert = connection.CreateCommand();
                insert.CommandText = InsertBar;
                insert.Parameters.AddWithValue("$ticker", ticker);
                insert.Parameters.AddWithValue("$session_date", bar.SessionDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

                // Through the money guard, which refuses anything that is not a
                // decimal. STRICT would take a double and store its rendering.
                Money.Bind(insert, "$open", bar.Open);
                Money.Bind(insert, "$high", bar.High);
                Money.Bind(insert, "$low", bar.Low);
                Money.Bind(insert, "$close", bar.Close);

                // The unadjusted close, which is the input to the factor the
                // other four came through. Stored so a later restatement can be
                // audited against what the provider said at the time.
                Money.Bind(insert, "$raw_close", bar.RawClose);

                insert.Parameters.AddWithValue("$volume", bar.Volume);
                insert.Parameters.AddWithValue("$source", Source);
                insert.Parameters.AddWithValue("$observed_at", observedAt);

                await insert.ExecuteNonQueryAsync(cancellationToken);
            }
        }

        var requests = feed.Requests - before;
        var written = await Count(connection, cancellationToken) - barsBefore;

        // Every member still holding no year after tonight, with the nights it has
        // been asked for and the session it is next asked for on, null where that
        // is the next night.
        var unserved = owed
            .Where(ticker => !fetched.TryGetValue(ticker, out var year) || year.Count == 0 || refused.ContainsKey(ticker))
            .Order(StringComparer.Ordinal)
            .Select(ticker =>
            {
                var earlier = asked.TryGetValue(ticker, out var sessions) ? sessions.Where(session => session <= to) : [];
                var nights = earlier.Concat(fetched.ContainsKey(ticker) ? new[] { to } : Array.Empty<DateOnly>()).Distinct().ToArray();
                DateOnly? last = nights.Length == 0 ? null : nights.Max();
                DateOnly? next = nights.Length <= RetryNights || last is null ? null : last.Value.AddDays(WeeklyRetryDays);

                return new UnservedName(ticker, nights.Length, last, next);
            })
            .ToArray();

        await using var log = connection.CreateCommand();
        log.CommandText = AppendRun;
        log.Parameters.AddWithValue("$run_id", runId);
        log.Parameters.AddWithValue("$stage", Stage);
        log.Parameters.AddWithValue("$started_at", observedAt);
        log.Parameters.AddWithValue("$ended_at", clock.UtcNow.ToString("O"));
        log.Parameters.AddWithValue("$outcome", unserved.Length == 0 ? "ok" : Partial);
        log.Parameters.AddWithValue("$rows_written", written);

        // Measured off the feed, not stated. The done condition is that this
        // equals the number of names lacking history that are due, and a literal
        // could not fail that.
        log.Parameters.AddWithValue("$network_requests", requests);

        // What was asked for and what is still owed, on the surface a person
        // reads and the record the schedule reads back. A refusal that left no
        // trace would be a name quietly holding no history.
        log.Parameters.AddWithValue(
            "$detail",
            due.Length == 0 && unserved.Length == 0
                ? DBNull.Value
                : JsonSerializer.Serialize(new
                {
                    session = Text(to),
                    asked = due.Order(StringComparer.Ordinal).ToArray(),
                    refused = gaps.Select(gap => FormattableString.Invariant($"{gap.Ticker} has no session on {gap.SessionDate:yyyy-MM-dd}")).ToArray(),
                    unserved = unserved.Select(name => new
                    {
                        ticker = name.Ticker,
                        nights = name.Nights,
                        last = name.Last is { } lastOn ? Text(lastOn) : null,
                        next = name.Next is { } nextOn ? Text(nextOn) : null,
                    }).ToArray(),
                }));

        await log.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new BackfillOutcome(members.Count, owed.Length, requests, written, gaps, due.Length, unserved);
    }

    // The sessions each name was asked for on, read off this stage's earlier rows, each
    // session once however many runs asked on it.
    static async Task<IReadOnlyDictionary<string, HashSet<DateOnly>>> AskedAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = EarlierRows;
        command.Parameters.AddWithValue("$stage", Stage);

        var asked = new Dictionary<string, HashSet<DateOnly>>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            using var detail = JsonDocument.Parse(reader.GetString(0));

            if (!detail.RootElement.TryGetProperty("session", out var session)
                || !DateOnly.TryParseExact(session.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var on)
                || !detail.RootElement.TryGetProperty("asked", out var names))
            {
                continue;
            }

            foreach (var name in names.EnumerateArray())
            {
                var ticker = name.GetString()!;

                if (!asked.TryGetValue(ticker, out var nights))
                {
                    asked[ticker] = nights = [];
                }

                nights.Add(on);
            }
        }

        return asked;
    }

    static async Task<int> Count(SqliteConnection connection, CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = BarCount;

        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    static async Task<IReadOnlyList<string>> Read(
        SqliteConnection connection,
        string sql,
        string? indexCode,
        CancellationToken cancellationToken,
        DateOnly? session = null)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;

        if (indexCode is not null)
        {
            command.Parameters.AddWithValue("$index_code", indexCode);
        }

        if (session is { } on)
        {
            command.Parameters.AddWithValue("$session", Text(on));
        }

        var values = new List<string>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }

    static string Text(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    string ConnectionString => StoreConnection.For(databaseFile);
}

// A member still holding no year after a night: how many nights it has been asked
// for, the session it was last asked for on, and the session it is next asked for
// on, null where that is the next night.
public sealed record UnservedName(string Ticker, int Nights, DateOnly? Last, DateOnly? Next);

// What one backfill did. Members and Owed are separate because the done
// condition is about the second: the request count matches the number of names
// lacking history that are due, not the number of names.
public sealed record BackfillOutcome(
    int Members,
    int Owed,
    int Requests,
    int RowsWritten,
    IReadOnlyList<Gap> Refused,
    int Due = 0,
    IReadOnlyList<UnservedName>? Unserved = null);
