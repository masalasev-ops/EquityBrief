using System.Globalization;
using EquityBrief.Core.Components;
using EquityBrief.Core.History;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Sweep;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.History;

// The second sources' fill, by hand: every code's member sessions on the three indices that hold no price inside its own
// prices, as the long history's lists read them, cut into runs back to back on the calendar and asked of Yahoo, then
// Stooq, then Tiingo, the code holding most such sessions first; a run is kept from the first source whose answer the
// judge passes, at the store's scale with the source and the ratio beside it, and a run no source fills is written as
// such with each source's reason and stays as it was. Tiingo is asked on the operator's key within 50 requests an hour,
// 1,000 a day and 500 codes a month, counted off the asks the store records; a code past the month's is left for the
// next month and named. Without `--word` it states what it would ask and asks nothing.
// see: A hole inside a code's own prices is filled from a second source's traded prices where they hold one ratio to the store's on each side, never interpolated
public sealed partial class SecondSourceFill(string databaseFile, IClock clock, ISecondSourceFeed feed, bool tiingo, Func<TimeSpan, CancellationToken, Task>? wait = null) : IComponent
{
    public const string Stage = "second-source-fill";

    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.LongFillBar, Touch.Read | Touch.Insert),
            new StoreTouch(Store.LongFillHole, Touch.Read | Touch.Insert),
            new StoreTouch(Store.SourceAsk, Touch.Read | Touch.Insert),
            new StoreTouch(Store.UnreliableMark, Touch.Read | Touch.Insert),
            new StoreTouch(Store.PriceCheck, Touch.Read | Touch.Insert),
            new StoreTouch(Store.CheckSettled, Touch.Read | Touch.Insert),
            new StoreTouch(Store.RunLog, Touch.Insert),
        ],
        Feeds: [Feed.SecondSourcePrices]);

    readonly Func<TimeSpan, CancellationToken, Task> wait = wait ?? Task.Delay;

    // One hole of a code: its sessions back to back.
    public sealed record Hole(string Ticker, IReadOnlyList<DateOnly> Sessions);

    // What became of a hole: the source that filled it, none where none did, and why.
    public sealed record Outcome(string Ticker, DateOnly First, DateOnly Last, int Sessions, string? Source, string Why);

    public async Task<IReadOnlyList<Outcome>> RunAsync(bool word, TextWriter output, CancellationToken cancellation = default)
    {
        var runId = FormattableString.Invariant($"second-source-fill-{clock.UtcNow:yyyyMMddTHHmmssZ}-{Guid.NewGuid():N}")[..55];
        var startedAt = Instant(clock.UtcNow);
        var reader = new LongHistoryReader(databaseFile);
        var through = await reader.NewestStoredSessionAsync(cancellation);
        var missing = new SortedDictionary<string, SortedSet<DateOnly>>(StringComparer.Ordinal);
        var onIndex = new Dictionary<string, SortedSet<string>>(StringComparer.Ordinal);

        foreach (var index in LongSpans.Larger)
        {
            var (byCode, _) = await reader.MissingAsync(index, through, cancellation);

            foreach (var (ticker, days) in byCode)
            {
                if (!missing.TryGetValue(ticker, out var held))
                {
                    missing[ticker] = held = [];
                    onIndex[ticker] = [];
                }

                held.UnionWith(days);
                onIndex[ticker].Add(index);
            }
        }

        var unfilled = await UnfilledAsync(cancellation);
        var codes = new List<(string Ticker, SweepBar[] Series, IReadOnlyList<IReadOnlyList<DateOnly>> Runs)>();

        foreach (var (ticker, days) in missing)
        {
            var (series, calendar) = await reader.CodeSeriesAsync(ticker, through, cancellation);
            var runs = SecondSources.Runs(SecondSources.Holes(series, [.. days]), calendar)
                .Where(run => !unfilled.Contains((ticker, run[0], run[^1])))
                .ToArray();

            if (runs.Length > 0)
            {
                codes.Add((ticker, series, runs));
            }
        }

        codes = [.. codes.OrderByDescending(code => code.Runs.Sum(run => run.Count)).ThenBy(code => code.Ticker, StringComparer.Ordinal)];

        foreach (var index in LongSpans.Larger)
        {
            var on = codes.Where(code => onIndex[code.Ticker].Contains(index)).ToArray();

            output.WriteLine(FormattableString.Invariant($"{Stage}: {Named(index)}: {on.Length} code(s) with {on.Sum(code => code.Runs.Count)} hole(s) of {on.Sum(code => code.Runs.Sum(run => run.Count))} member session(s) inside their own prices"));
        }

        output.WriteLine(FormattableString.Invariant($"{Stage}: {codes.Count} code(s) across the three, {codes.Sum(code => code.Runs.Sum(run => run.Count))} session(s); at most {codes.Count} request(s) of Yahoo, {codes.Count} of Stooq and {codes.Count * 2} of Tiingo{(tiingo ? string.Empty : ", which is not asked: no key is set")}, Tiingo within {TiingoAllowance.PerHour} an hour, {TiingoAllowance.PerDay} a day and {TiingoAllowance.CodesPerMonth} codes a month"));

        foreach (var code in codes)
        {
            output.WriteLine(FormattableString.Invariant($"{Stage}: hole {code.Ticker} on {string.Join(" ", onIndex[code.Ticker])}: {code.Runs.Sum(run => run.Count)} session(s) in {code.Runs.Count} run(s), {string.Join(", ", code.Runs.Select(run => run.Count == 1 ? Text(run[0]) : $"{Text(run[0])} to {Text(run[^1])}"))}"));
        }

        if (!word)
        {
            output.WriteLine($"{Stage}: nothing asked; give '--word' to ask the sources and write what they fill");

            return [];
        }

        var outcomes = new List<Outcome>();
        var requests = 0;

        foreach (var (ticker, series, runs) in codes)
        {
            var remaining = runs.ToList();
            var reasons = new Dictionary<IReadOnlyList<DateOnly>, List<string>>();
            var kept = new List<(IReadOnlyList<DateOnly> Run, string Source, FillJudgement Judgement)>();
            var (from, to) = (runs.Min(run => run[0]).AddDays(-SecondSources.Margin), runs.Max(run => run[^1]).AddDays(SecondSources.Margin));

            foreach (var source in SecondSources.Order)
            {
                if (remaining.Count == 0)
                {
                    break;
                }

                var symbol = SecondSources.Symbol(ticker, source);
                var (answer, why, asked) = await AskAsync(runId, source, ticker, symbol, from, to, (runs.Min(run => run[0]), runs.Max(run => run[^1])), cancellation);

                requests += asked;

                if (answer is null || answer.Bars.Count == 0)
                {
                    foreach (var run in remaining)
                    {
                        Reason(reasons, run, $"{source}: {why}");
                    }

                    continue;
                }

                foreach (var run in remaining.ToArray())
                {
                    var judgement = SecondSources.Judge(series, run, answer.Bars);

                    if (judgement.Kept.Count == run.Count)
                    {
                        kept.Add((run, source, judgement));
                        remaining.Remove(run);
                    }
                    else
                    {
                        Reason(reasons, run, $"{source}: {judgement.Why}");
                    }
                }
            }

            outcomes.AddRange(await WriteAsync(runId, ticker, kept, remaining, reasons, cancellation));

            output.WriteLine(FormattableString.Invariant($"{Stage}: {ticker}: {kept.Sum(one => one.Run.Count)} session(s) filled{(kept.Count > 0 ? " from " + string.Join(", ", kept.Select(one => one.Source).Distinct(StringComparer.Ordinal)) : string.Empty)}, {remaining.Sum(run => run.Count)} left"));
        }

        await LogAsync(runId, startedAt, outcomes, requests, cancellation);

        foreach (var source in SecondSources.Order)
        {
            output.WriteLine(FormattableString.Invariant($"{Stage}: filled from {source}: {outcomes.Where(one => one.Source == source).Sum(one => one.Sessions)} session(s) of {outcomes.Where(one => one.Source == source).Select(one => one.Ticker).Distinct(StringComparer.Ordinal).Count()} code(s)"));
        }

        output.WriteLine(FormattableString.Invariant($"{Stage}: still open: {outcomes.Where(one => one.Source is null).Sum(one => one.Sessions)} session(s) of {outcomes.Where(one => one.Source is null).Select(one => one.Ticker).Distinct(StringComparer.Ordinal).Count()} code(s), {requests} request(s) made"));

        return outcomes;
    }

    static void Reason(Dictionary<IReadOnlyList<DateOnly>, List<string>> reasons, IReadOnlyList<DateOnly> run, string why)
    {
        if (!reasons.TryGetValue(run, out var said))
        {
            reasons[run] = said = [];
        }

        said.Add(why);
    }

    // One source asked for a code over the span, every request written as it is made; Tiingo asked only where its key is
    // set, its meta first and its prices only where the meta covers the code's holes, each request within its limits.
    async Task<(SourceAnswer? Answer, string Why, int Requests)> AskAsync(string runId, string source, string ticker, string symbol, DateOnly from, DateOnly to, (DateOnly First, DateOnly Last) holes, CancellationToken cancellation)
    {
        try
        {
            switch (source)
            {
                case SecondSources.Yahoo:
                {
                    var answer = await feed.YahooAsync(symbol, from, to, cancellation);

                    await RecordAsync(runId, source, "prices", ticker, symbol, from, to, answer.Bars.Count, answer.Outcome, cancellation);

                    return (answer, answer.Outcome, 1);
                }
                case SecondSources.Stooq:
                {
                    var answer = await feed.StooqAsync(symbol, from, to, cancellation);

                    await RecordAsync(runId, source, "prices", ticker, symbol, from, to, answer.Bars.Count, answer.Outcome, cancellation);

                    return (answer, answer.Outcome, 1);
                }
                default:
                {
                    if (!tiingo)
                    {
                        return (null, "not asked: no key is set", 0);
                    }

                    if (!await WithinAsync(ticker, cancellation))
                    {
                        return (null, FormattableString.Invariant($"not asked: the month's {TiingoAllowance.CodesPerMonth} codes are spent, left for next month"), 0);
                    }

                    var meta = await feed.TiingoMetaAsync(symbol, cancellation);
                    var covers = meta is { } span && span.First <= holes.First && span.Last >= holes.Last;
                    var said = meta is { } held
                        ? FormattableString.Invariant($"Tiingo holds {symbol} from {Text(held.First)} to {Text(held.Last)}")
                        : $"Tiingo holds no {symbol}";

                    await RecordAsync(runId, source, "meta", ticker, symbol, null, null, 0, said, cancellation);

                    if (!covers)
                    {
                        return (null, said + ", not over the holes", 1);
                    }

                    if (!await WithinAsync(ticker, cancellation))
                    {
                        return (null, FormattableString.Invariant($"not asked for prices: the month's {TiingoAllowance.CodesPerMonth} codes are spent, left for next month"), 1);
                    }

                    var answer = await feed.TiingoAsync(symbol, from, to, cancellation);

                    await RecordAsync(runId, source, "prices", ticker, symbol, from, to, answer.Bars.Count, answer.Outcome, cancellation);

                    return (answer, answer.Outcome, 2);
                }
            }
        }
        catch (ProviderRefusal refusal)
        {
            await RecordAsync(runId, source, "prices", ticker, symbol, from, to, 0, refusal.Message, cancellation);

            return (null, refusal.Message, 1);
        }
    }

    // Whether Tiingo may be asked for the code now, waiting out the hour's or the day's limit where one is spent; false where
    // the month's codes are spent and the code is not among them.
    async Task<bool> WithinAsync(string ticker, CancellationToken cancellation)
    {
        while (true)
        {
            var asks = new List<(DateTimeOffset, string)>();

            await using (var connection = await OpenAsync(cancellation))
            await using (var select = connection.CreateCommand())
            {
                select.CommandText = "SELECT asked_at, ticker FROM source_ask WHERE source = 'tiingo';";

                await using var rows = await select.ExecuteReaderAsync(cancellation);

                while (await rows.ReadAsync(cancellation))
                {
                    asks.Add((DateTimeOffset.ParseExact(rows.GetString(0), "yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal), rows.GetString(1)));
                }
            }

            switch (TiingoAllowance.Wait(asks, clock.UtcNow, ticker))
            {
                case null:
                    return false;
                case { } pause when pause > TimeSpan.Zero:
                    await wait(pause + TimeSpan.FromSeconds(1), cancellation);
                    break;
                default:
                    return true;
            }
        }
    }

    async Task RecordAsync(string runId, string source, string kind, string ticker, string symbol, DateOnly? from, DateOnly? to, int sessions, string outcome, CancellationToken cancellation)
    {
        await using var connection = await OpenAsync(cancellation);
        await using var insert = connection.CreateCommand();

        insert.CommandText = @"
            INSERT INTO source_ask (run_id, asked_at, source, kind, ticker, symbol, from_session, to_session, sessions, outcome)
            VALUES ($run_id, $asked_at, $source, $kind, $ticker, $symbol, $from, $to, $sessions, $outcome);";
        insert.Parameters.AddWithValue("$run_id", runId);
        insert.Parameters.AddWithValue("$asked_at", Instant(clock.UtcNow));
        insert.Parameters.AddWithValue("$source", source);
        insert.Parameters.AddWithValue("$kind", kind);
        insert.Parameters.AddWithValue("$ticker", ticker);
        insert.Parameters.AddWithValue("$symbol", symbol);
        insert.Parameters.AddWithValue("$from", from is { } first ? Text(first) : DBNull.Value);
        insert.Parameters.AddWithValue("$to", to is { } last ? Text(last) : DBNull.Value);
        insert.Parameters.AddWithValue("$sessions", sessions);
        insert.Parameters.AddWithValue("$outcome", outcome);
        await insert.ExecuteNonQueryAsync(cancellation);
    }

    // A code's kept sessions and every hole's outcome, in one transaction.
    async Task<IReadOnlyList<Outcome>> WriteAsync(
        string runId,
        string ticker,
        IReadOnlyList<(IReadOnlyList<DateOnly> Run, string Source, FillJudgement Judgement)> kept,
        IReadOnlyList<IReadOnlyList<DateOnly>> remaining,
        Dictionary<IReadOnlyList<DateOnly>, List<string>> reasons,
        CancellationToken cancellation)
    {
        var outcomes = new List<Outcome>();

        await using var connection = await OpenAsync(cancellation);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellation);

        foreach (var (run, source, judgement) in kept)
        {
            foreach (var bar in judgement.Kept)
            {
                await using var insert = connection.CreateCommand();

                insert.Transaction = transaction;
                insert.CommandText = @"
                    INSERT INTO long_fill_bar (ticker, session_date, open, high, low, close, raw_close, volume, source, ratio, volume_proportion, kind, run_id)
                    VALUES ($ticker, $session, $open, $high, $low, $close, $raw, $volume, $source, $ratio, $volume_proportion, 'hole', $run_id);";
                insert.Parameters.AddWithValue("$ticker", ticker);
                insert.Parameters.AddWithValue("$session", Text(bar.Session));
                insert.Parameters.AddWithValue("$open", Money.ToStorage(bar.Open));
                insert.Parameters.AddWithValue("$high", Money.ToStorage(bar.High));
                insert.Parameters.AddWithValue("$low", Money.ToStorage(bar.Low));
                insert.Parameters.AddWithValue("$close", Money.ToStorage(bar.Close));
                insert.Parameters.AddWithValue("$raw", Money.ToStorage(bar.RawClose));
                insert.Parameters.AddWithValue("$volume", bar.Volume);
                insert.Parameters.AddWithValue("$source", source);
                insert.Parameters.AddWithValue("$ratio", judgement.Ratio!.Value.ToString(RatioForm, CultureInfo.InvariantCulture));
                insert.Parameters.AddWithValue("$volume_proportion",judgement.VolumeRatio!.Value.ToString(RatioForm, CultureInfo.InvariantCulture));
                insert.Parameters.AddWithValue("$run_id", runId);
                await insert.ExecuteNonQueryAsync(cancellation);
            }

            outcomes.Add(new Outcome(ticker, run[0], run[^1], run.Count, source, judgement.Why));
        }

        foreach (var run in remaining)
        {
            outcomes.Add(new Outcome(ticker, run[0], run[^1], run.Count, null, string.Join("; ", reasons.GetValueOrDefault(run) ?? ["no source was asked"])));
        }

        foreach (var outcome in outcomes)
        {
            await using var insert = connection.CreateCommand();

            insert.Transaction = transaction;
            insert.CommandText = @"
                INSERT INTO long_fill_hole (run_id, ticker, first_session, last_session, sessions, source, why)
                VALUES ($run_id, $ticker, $first, $last, $sessions, $source, $why);";
            insert.Parameters.AddWithValue("$run_id", runId);
            insert.Parameters.AddWithValue("$ticker", outcome.Ticker);
            insert.Parameters.AddWithValue("$first", Text(outcome.First));
            insert.Parameters.AddWithValue("$last", Text(outcome.Last));
            insert.Parameters.AddWithValue("$sessions", outcome.Sessions);
            insert.Parameters.AddWithValue("$source", outcome.Source is { } named ? named : DBNull.Value);
            insert.Parameters.AddWithValue("$why", outcome.Why);
            await insert.ExecuteNonQueryAsync(cancellation);
        }

        await transaction.CommitAsync(cancellation);

        return outcomes;
    }

    async Task LogAsync(string runId, string startedAt, IReadOnlyList<Outcome> outcomes, int requests, CancellationToken cancellation)
    {
        await using var connection = await OpenAsync(cancellation);
        await using var insert = connection.CreateCommand();

        insert.CommandText = @"
            INSERT INTO run_log (run_id, stage, started_at, ended_at, outcome, rows_written, model_calls, network_requests, spend, detail)
            VALUES ($run_id, $stage, $started_at, $ended_at, 'ok', $rows, 0, $requests, '0', $detail);";
        insert.Parameters.AddWithValue("$run_id", runId);
        insert.Parameters.AddWithValue("$stage", Stage);
        insert.Parameters.AddWithValue("$started_at", startedAt);
        insert.Parameters.AddWithValue("$ended_at", Instant(clock.UtcNow));
        insert.Parameters.AddWithValue("$rows", outcomes.Where(one => one.Source is not null).Sum(one => one.Sessions));
        insert.Parameters.AddWithValue("$requests", requests);
        insert.Parameters.AddWithValue("$detail", FormattableString.Invariant(
            $"{outcomes.Count} hole(s) of {outcomes.Select(one => one.Ticker).Distinct(StringComparer.Ordinal).Count()} code(s) asked: {string.Join(", ", SecondSources.Order.Select(source => $"{outcomes.Where(one => one.Source == source).Sum(one => one.Sessions)} session(s) from {source}"))}, {outcomes.Where(one => one.Source is null).Sum(one => one.Sessions)} still open"));
        await insert.ExecuteNonQueryAsync(cancellation);
    }

    // The holes asked before that no source filled, each asked again only once a later run is given them by hand.
    async Task<HashSet<(string, DateOnly, DateOnly)>> UnfilledAsync(CancellationToken cancellation)
    {
        var unfilled = new HashSet<(string, DateOnly, DateOnly)>();

        await using var connection = await OpenAsync(cancellation);
        await using var select = connection.CreateCommand();

        select.CommandText = "SELECT ticker, first_session, last_session FROM long_fill_hole WHERE source IS NULL;";

        await using var rows = await select.ExecuteReaderAsync(cancellation);

        while (await rows.ReadAsync(cancellation))
        {
            unfilled.Add((rows.GetString(0), Day(rows.GetString(1)), Day(rows.GetString(2))));
        }

        return unfilled;
    }

    async Task<SqliteConnection> OpenAsync(CancellationToken cancellation)
    {
        var connection = new SqliteConnection(StoreConnection.For(databaseFile));

        await connection.OpenAsync(cancellation);

        return connection;
    }

    // A ratio written to ten places with its trailing zeros taken off.
    const string RatioForm = "0.##########";

    // The check's settling's writes: each difference, wild close or stretch settled, each correction of a stored price, each
    // mark and each close held to a source.
    const string InsertSettled = @"
        INSERT OR IGNORE INTO check_settled (run_id, index_code, kind, family, ticker, session_date, reading, table_value, other_value, grouped, outcome, source)
        VALUES ($run_id, $index, $kind, $family, $ticker, $session, $reading, $table, $other, $grouped, $outcome, $source);";

    const string InsertCorrection = @"
        INSERT OR IGNORE INTO long_fill_bar (ticker, session_date, open, high, low, close, raw_close, volume, source, ratio, volume_proportion, kind, run_id)
        VALUES ($ticker, $session, $open, $high, $low, $close, $raw, $volume, $source, $ratio, '1', 'correction', $run_id);";

    const string InsertMark = @"
        INSERT OR IGNORE INTO unreliable_mark (run_id, ticker, first_session, last_session, reading, kind, why, marked_at)
        VALUES ($run_id, $ticker, $first, $last, $reading, $kind, $why, $at);";

    const string InsertCheck = @"
        INSERT OR IGNORE INTO price_check (run_id, purpose, index_code, ticker, session_date, stored_close, source, source_close, scaled_close, agrees)
        VALUES ($run_id, $purpose, $index, $ticker, $session, $stored, $source, $own, $scaled, $agrees);";

    static string Named(string index) => index switch
    {
        "GSPC" => "the S&P 500",
        "MID" => "the S&P 400",
        _ => "the S&P 600",
    };

    static string Instant(DateTimeOffset at) => at.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

    static string Text(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    static DateOnly Day(string text) => DateOnly.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture);
}
