using System.Globalization;
using EquityBrief.Core.Books;
using EquityBrief.Core.Ledger;
using EquityBrief.Core.Prices;
using EquityBrief.Data;

namespace EquityBrief.Api.Reading;

// The prices the own exit's walk reads for the stocks it can hold, as the store holds them now: each session's open and
// close, and the sessions whose close stood under the stock's 200-day average.
public sealed class OwnExitPrices(
    IReadOnlyDictionary<string, SortedList<DateOnly, (decimal Open, decimal Close)>> bars,
    IReadOnlySet<(string Ticker, DateOnly Session)> under)
{
    public decimal? CloseOn(string ticker, DateOnly session) =>
        bars.TryGetValue(ticker, out var days) && days.TryGetValue(session, out var day) ? day.Close : null;

    // The gap a stock chosen at a night's close opens at the next session the store holds, through the core's one pricing
    // function, as the books' fills work it; none until that session is stored.
    public double? GapOf(string ticker, DateOnly chosenOn)
    {
        if (!bars.TryGetValue(ticker, out var days) || !days.TryGetValue(chosenOn, out var chosen) || chosen.Close <= 0m)
        {
            return null;
        }

        var after = days.Keys.FirstOrDefault(session => session > chosenOn);

        if (after == default)
        {
            return null;
        }

        var close = Statistic.FromPrice(chosen.Close);

        return TradeFills.AtOpen(0.0, close, close, Statistic.FromPrice(days[after].Open)) is { } atOpen ? -atOpen : null;
    }

    // The sessions a stock chosen on a night was held to another, its buy's counted first, each a session the store holds a
    // bar of it for, as the Fundamentals family counts them.
    public int SessionsHeld(string ticker, DateOnly chosenOn, DateOnly through) =>
        bars.TryGetValue(ticker, out var days) ? days.Keys.Count(session => session > chosenOn && session <= through) : 0;

    public bool UnderAverage(string ticker, DateOnly session) => under.Contains((ticker, session));
}

// What the read surface reads for the books' own exit: the index's membership spans, each book's stored nights and answers
// from the night its live exit first applied, and the prices of the stocks its walk can hold.
// see: Each book's own exit is walked beside its live book as a record on its card, and registers no version and draws no pick
public sealed partial class ReadApi
{
    const string OwnExitSpans = "SELECT ticker, joined, left FROM membership WHERE index_code = $index;";

    const string QualityOwnNights = @"
        SELECT session_date, reshuffle FROM quality_night
        WHERE rule = $rule AND index_code = $index AND session_date >= $since AND session_date <= $on AND fault IS NULL
        ORDER BY session_date;
    ";

    const string QualityOwnAnswers = @"
        SELECT session_date, ticker, passed, place, passed_checks, applied, carried
        FROM quality_result WHERE rule = $rule AND index_code = $index AND session_date >= $since AND session_date <= $on;
    ";

    const string ImprovingOwnNights = @"
        SELECT session_date, 0 FROM improving_night
        WHERE rule = $rule AND index_code = $index AND session_date >= $since AND session_date <= $on AND fault IS NULL
        ORDER BY session_date;
    ";

    const string ImprovingOwnAnswers = @"
        SELECT session_date, ticker, CASE WHEN status = 'bought' THEN 1 ELSE 0 END, NULL, passed_checks, applied, carried
        FROM improving_result WHERE rule = $rule AND index_code = $index AND session_date >= $since AND session_date <= $on;
    ";

    // From the operator's answers of 2026-11-16, item 6, every stock's bars and averages in one read each.
    const string OwnExitBars = @"
        SELECT ticker, session_date, open, close FROM bar
        WHERE ticker IN (SELECT value FROM json_each($tickers)) AND session_date >= $from AND session_date <= $on;
    ";

    const string OwnExitAverageValues = @"
        SELECT ticker, session_date, value FROM indicator
        WHERE ticker IN (SELECT value FROM json_each($tickers)) AND name = 'sma200' AND value IS NOT NULL
          AND session_date >= $since AND session_date <= $on;
    ";

    // Whether a stock was a member of the index on a session: a span it joined on or before, which it had not left.
    public async Task<Func<string, DateOnly, bool>> OwnExitMembersAsync(string index)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = OwnExitSpans;
        command.Parameters.AddWithValue("$index", index);

        await using var reader = await command.ExecuteReaderAsync();
        var spans = new Dictionary<string, List<(DateOnly? Joined, DateOnly? Left)>>(StringComparer.Ordinal);

        while (await reader.ReadAsync())
        {
            var ticker = reader.GetString(0);

            if (!spans.TryGetValue(ticker, out var list))
            {
                spans[ticker] = list = [];
            }

            list.Add((reader.IsDBNull(1) ? null : OwnExitDate(reader.GetString(1)), reader.IsDBNull(2) ? null : OwnExitDate(reader.GetString(2))));
        }

        return (ticker, session) => spans.TryGetValue(ticker, out var list)
            && list.Any(span => (span.Joined is not { } joined || joined <= session) && (span.Left is not { } left || session < left));
    }

    // Quality at a fair price's nights under a rule from a night to another, each with every member's stored answer.
    public Task<IReadOnlyList<OwnExitNight>> QualityOwnNightsAsync(string index, string rule, DateOnly since, DateOnly on) =>
        OwnNightsAsync(QualityOwnNights, QualityOwnAnswers, index, rule, since, on, bought: false);

    // The Fundamentals family's nights under a rule from a night to another, each with every member's stored answer and the
    // stocks the live book bought on it.
    public Task<IReadOnlyList<OwnExitNight>> ImprovingOwnNightsAsync(string index, string rule, DateOnly since, DateOnly on) =>
        OwnNightsAsync(ImprovingOwnNights, ImprovingOwnAnswers, index, rule, since, on, bought: true);

    async Task<IReadOnlyList<OwnExitNight>> OwnNightsAsync(string nightsSql, string answersSql, string index, string rule, DateOnly since, DateOnly on, bool bought)
    {
        await using var connection = Open();
        var nights = new List<(DateOnly Night, bool Reshuffle)>();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = nightsSql;
            command.Parameters.AddWithValue("$rule", rule);
            command.Parameters.AddWithValue("$index", index);
            command.Parameters.AddWithValue("$since", Day(since));
            command.Parameters.AddWithValue("$on", Day(on));

            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                nights.Add((OwnExitDate(reader.GetString(0)), reader.GetInt64(1) == 1));
            }
        }

        var answers = nights.ToDictionary(night => night.Night, _ => new Dictionary<string, OwnExitAnswer>(StringComparer.Ordinal));
        var boughtOn = nights.ToDictionary(night => night.Night, _ => new List<string>());

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = answersSql;
            command.Parameters.AddWithValue("$rule", rule);
            command.Parameters.AddWithValue("$index", index);
            command.Parameters.AddWithValue("$since", Day(since));
            command.Parameters.AddWithValue("$on", Day(on));

            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                var night = OwnExitDate(reader.GetString(0));

                if (!answers.TryGetValue(night, out var byTicker))
                {
                    continue;
                }

                var ticker = reader.GetString(1);
                var flag = reader.GetInt64(2) == 1;

                byTicker[ticker] = new OwnExitAnswer(
                    !bought && flag,
                    reader.IsDBNull(3) ? null : reader.GetInt32(3),
                    reader.GetInt32(4),
                    reader.GetInt32(5),
                    reader.GetDouble(6));

                if (bought && flag)
                {
                    boughtOn[night].Add(ticker);
                }
            }
        }

        return [.. nights.Select(night => new OwnExitNight(night.Night, night.Reshuffle, answers[night.Night], [.. boughtOn[night.Night].Order(StringComparer.Ordinal)]))];
    }

    // The opens and closes of the stocks a walk can hold from the earliest night it reads to the newest, and the sessions
    // from a night on whose close stood under the stock's 200-day average.
    public async Task<OwnExitPrices> OwnExitPricesAsync(IEnumerable<string> tickers, DateOnly from, DateOnly since, DateOnly on)
    {
        await using var connection = Open();
        string[] wanted = [.. tickers.Distinct(StringComparer.Ordinal)];
        var bars = wanted.ToDictionary(ticker => ticker, _ => new SortedList<DateOnly, (decimal Open, decimal Close)>(), StringComparer.Ordinal);
        var under = new HashSet<(string Ticker, DateOnly Session)>();
        var named = System.Text.Json.JsonSerializer.Serialize(wanted);

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = OwnExitBars;
            command.Parameters.AddWithValue("$tickers", named);
            command.Parameters.AddWithValue("$from", Day(from));
            command.Parameters.AddWithValue("$on", Day(on));

            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                bars[reader.GetString(0)][OwnExitDate(reader.GetString(1))] = (Money.FromStorage(reader.GetString(2)), Money.FromStorage(reader.GetString(3)));
            }
        }

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = OwnExitAverageValues;
            command.Parameters.AddWithValue("$tickers", named);
            command.Parameters.AddWithValue("$since", Day(since));
            command.Parameters.AddWithValue("$on", Day(on));

            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                var ticker = reader.GetString(0);
                var session = OwnExitDate(reader.GetString(1));

                if (bars[ticker].TryGetValue(session, out var day) && Statistic.FromPrice(day.Close) < reader.GetDouble(2))
                {
                    under.Add((ticker, session));
                }
            }
        }

        return new OwnExitPrices(bars, under);
    }

    // Each live position open on a night or chosen since, as its book stored it: the stock, the night it was chosen, its gap
    // to the fill, its end and its result where it ended by the newest night.
    const string QualityLiveSince = @"
        SELECT ticker, chosen_on, CASE WHEN bought_on <= $on THEN gap END,
               CASE WHEN ended_on <= $on THEN ended_on END, CASE WHEN ended_on <= $on THEN result END
        FROM quality_holding
        WHERE rule = $rule AND index_code = $index AND chosen_on <= $on AND (ended_on IS NULL OR ended_on >= $since)
        ORDER BY chosen_on, ticker;
    ";

    const string ImprovingLiveSince = @"
        SELECT ticker, chosen_on, CASE WHEN bought_on <= $on THEN gap END,
               CASE WHEN ended_on <= $on THEN ended_on END, CASE WHEN ended_on <= $on THEN result END
        FROM improving_trade
        WHERE rule = $rule AND index_code = $index AND chosen_on <= $on AND (ended_on IS NULL OR ended_on >= $since)
        ORDER BY chosen_on, ticker;
    ";

    const string HeavyweightLiveSince = @"
        SELECT ticker, entered_on, 0.0, CASE WHEN ended_on <= $on THEN ended_on END, CASE WHEN ended_on <= $on THEN result END
        FROM heavyweight_holding
        WHERE entered_on <= $on AND (ended_on IS NULL OR ended_on >= $since)
        ORDER BY entered_on, ticker;
    ";

    const string IndexHeavyweightLiveSince = @"
        SELECT ticker, entered_on, 0.0, CASE WHEN ended_on <= $on THEN ended_on END, CASE WHEN ended_on <= $on THEN result END
        FROM index_heavyweight_rule_holding
        WHERE candidate = $rule AND index_code = $index AND entered_on <= $on AND (ended_on IS NULL OR ended_on >= $since)
        ORDER BY entered_on, ticker;
    ";

    // The carried growth on a member's stored answer on a night, for the nights the live positions were chosen on.
    const string QualityCarriedOn = @"
        SELECT ticker, carried FROM quality_result WHERE rule = $rule AND index_code = $index AND session_date = $on;
    ";

    const string ImprovingCarriedOn = @"
        SELECT ticker, carried FROM improving_result WHERE rule = $rule AND index_code = $index AND session_date = $on;
    ";

    // The S&P 500 heavyweights' rebalances from a night: each session with the leaders it stored.
    const string HeavyweightRebalancesSince = @"
        SELECT session_date, ticker, leader FROM heavyweight_night WHERE session_date >= $since AND session_date <= $on;
    ";

    // An S&P 400 or 600 heavyweights rule's rebalances from a night.
    const string IndexHeavyweightRebalancesSince = @"
        SELECT session_date FROM index_heavyweight_rule_night
        WHERE candidate = $rule AND index_code = $index AND session_date >= $since AND session_date <= $on;
    ";

    public enum OwnExitBook
    {
        Quality,
        Improving,
        Heavyweights,
        IndexHeavyweights,
    }

    public async Task<IReadOnlyList<OwnExitLive>> OwnExitLiveAsync(OwnExitBook book, string index, string rule, DateOnly since, DateOnly on)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = book switch
        {
            OwnExitBook.Quality => QualityLiveSince,
            OwnExitBook.Improving => ImprovingLiveSince,
            OwnExitBook.Heavyweights => HeavyweightLiveSince,
            _ => IndexHeavyweightLiveSince,
        };
        command.Parameters.AddWithValue("$rule", rule);
        command.Parameters.AddWithValue("$index", index);
        command.Parameters.AddWithValue("$since", Day(since));
        command.Parameters.AddWithValue("$on", Day(on));

        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<OwnExitLive>();

        while (await reader.ReadAsync())
        {
            rows.Add(new OwnExitLive(
                reader.GetString(0),
                OwnExitDate(reader.GetString(1)),
                reader.IsDBNull(2) ? null : reader.GetDouble(2),
                reader.IsDBNull(3) ? null : OwnExitDate(reader.GetString(3)),
                reader.IsDBNull(4) ? null : reader.GetDouble(4)));
        }

        return rows;
    }

    // The carried growth each member's stored answer held on each of the nights given.
    public async Task<IReadOnlyDictionary<(string Ticker, DateOnly Night), double>> OwnExitCarriedAsync(OwnExitBook book, string index, string rule, IEnumerable<DateOnly> nights)
    {
        await using var connection = Open();
        var carried = new Dictionary<(string Ticker, DateOnly Night), double>();

        foreach (var night in nights.Distinct())
        {
            await using var command = connection.CreateCommand();

            command.CommandText = book == OwnExitBook.Quality ? QualityCarriedOn : ImprovingCarriedOn;
            command.Parameters.AddWithValue("$rule", rule);
            command.Parameters.AddWithValue("$index", index);
            command.Parameters.AddWithValue("$on", Day(night));

            await using var reader = await command.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                carried[(reader.GetString(0), night)] = reader.GetDouble(1);
            }
        }

        return carried;
    }

    // The heavyweights' rebalances from a night to another: each with the stocks the live book bought on it and, for the
    // S&P 500's book, the leaders it stored.
    public async Task<IReadOnlyList<OwnExitRebalance>> OwnExitRebalancesAsync(OwnExitBook book, string index, string rule, DateOnly since, DateOnly on, IReadOnlyList<OwnExitLive> live)
    {
        await using var connection = Open();
        await using var command = connection.CreateCommand();
        var leaders = new SortedDictionary<DateOnly, HashSet<string>>();

        command.CommandText = book == OwnExitBook.Heavyweights ? HeavyweightRebalancesSince : IndexHeavyweightRebalancesSince;
        command.Parameters.AddWithValue("$rule", rule);
        command.Parameters.AddWithValue("$index", index);
        command.Parameters.AddWithValue("$since", Day(since));
        command.Parameters.AddWithValue("$on", Day(on));

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var night = OwnExitDate(reader.GetString(0));

            if (!leaders.TryGetValue(night, out var set))
            {
                leaders[night] = set = new HashSet<string>(StringComparer.Ordinal);
            }

            if (book == OwnExitBook.Heavyweights && reader.GetInt64(2) == 1)
            {
                set.Add(reader.GetString(1));
            }
        }

        return [.. leaders.Select(pair => new OwnExitRebalance(
            pair.Key,
            [.. live.Where(position => position.ChosenOn == pair.Key).Select(position => position.Ticker).Order(StringComparer.Ordinal)],
            book == OwnExitBook.Heavyweights ? pair.Value : null))];
    }

    static DateOnly OwnExitDate(string stored) => DateOnly.ParseExact(stored, "yyyy-MM-dd", CultureInfo.InvariantCulture);
}

// A live position as its book stored it: the stock, the night it was chosen, its gap to the fill, and its end and result
// where it ended by the newest night.
public sealed record OwnExitLive(string Ticker, DateOnly ChosenOn, double? Gap, DateOnly? EndedOn, double? Result);
