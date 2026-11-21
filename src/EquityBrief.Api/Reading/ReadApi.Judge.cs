using System.Globalization;
using EquityBrief.Core.Indicators;
using EquityBrief.Core.Prices;
using EquityBrief.Data;

namespace EquityBrief.Api.Reading;

// One run of the AI judge as its page reads it: the night, its outcome and why, the members it screened and kept, and
// whether it was a replayed night's.
public sealed record JudgeRunRow(string RunId, DateOnly Session, string Outcome, string Why, int Screened, int Kept, bool Replayed);

// One of the AI judge's picks as its page reads it: the night, its rank, the member, the hold and take-profit it chose, its
// reason, the last close and typical move its prices were worked from, those prices, the company's name and the sector and
// size the judge was shown, and whether a replayed night's.
public sealed record JudgePickRow(
    DateOnly Session,
    int Rank,
    string Index,
    string Ticker,
    string Hold,
    string Shape,
    string Reason,
    decimal LastClose,
    double TypicalMove,
    decimal Stop,
    decimal Target,
    decimal? GiveUp,
    string? Company,
    string? Sector,
    string? Size,
    bool Replayed)
{
    // From 24.14 the keys of the facts it named, none on a pick made before the judge named its facts; the prompt version
    // of the pin it was picked under; and its sheet as the judge was shown it, each field's shown text by its key.
    public IReadOnlyList<string>? Named { get; init; }

    public string? Version { get; init; }

    public IReadOnlyDictionary<string, string> Sheet { get; init; } = new Dictionary<string, string>(StringComparer.Ordinal);
}

// The AI judge's page reads its runs, its picks and the members' sessions from its first night, all from the store opened
// for reading, writing nothing.
// see: The AI judge picks straight after the night under the local model's lock, its picks stored before the next open or shown as missed
public sealed partial class ReadApi
{
    // A replayed night's run, which 24.7's replay writes under this prefix, read apart from the live record.
    public const string JudgeReplayPrefix = "judge-replay-";

    const string JudgeRuns = @"
        SELECT run_id, session_date, outcome, detail, screened, kept FROM judge_run ORDER BY session_date DESC, ended_at DESC;";

    const string JudgePicks = @"
        SELECT p.session_date, p.rank, p.index_code, p.ticker, p.hold, p.take_profit, p.reason, p.last_close, p.typical_move,
               p.stop, p.target, p.give_up_distance,
               COALESCE((SELECT l.name FROM listed_name l WHERE l.ticker = p.ticker),
                        (SELECT m.name FROM membership m WHERE m.ticker = p.ticker AND m.name IS NOT NULL ORDER BY m.joined DESC LIMIT 1)),
               (SELECT json_extract(f.fields, '$.sector') FROM judge_fact f WHERE f.ticker = p.ticker AND f.session_date = p.session_date),
               (SELECT json_extract(f.fields, '$.size') FROM judge_fact f WHERE f.ticker = p.ticker AND f.session_date = p.session_date),
               p.run_id, p.fields,
               (SELECT n.prompt_version FROM judge_run r JOIN judge_pin n ON n.id = r.pin_id WHERE r.run_id = p.run_id),
               (SELECT f.fields FROM judge_fact f JOIN judge_run r ON r.run_id = p.run_id
                WHERE f.ticker = p.ticker AND f.session_date = p.session_date AND f.version = r.fact_version)
        FROM judge_pick p ORDER BY p.session_date DESC, p.rank;";

    const string JudgeBarsFrom = "SELECT ticker, session_date, open, close FROM bar WHERE session_date >= $from ORDER BY ticker, session_date;";

    const string JudgeTypicals = "SELECT ticker, session_date, value FROM indicator WHERE name = $name AND session_date IN (SELECT value FROM json_each($nights));";

    const string JudgeMembers = "SELECT session_date, ticker FROM judge_fact WHERE session_date IN (SELECT value FROM json_each($nights)) ORDER BY session_date, ticker;";

    const string JudgeCosts = "SELECT ticker, session_date, cost FROM member_reading WHERE cost IS NOT NULL AND session_date IN (SELECT value FROM json_each($nights));";

    const string ClosesOn = "SELECT ticker, close FROM bar WHERE session_date = $on AND ticker IN (SELECT value FROM json_each($tickers));";

    // From 24.15 each stock's close on a night, a price, for Home's line on a past night's pick still held.
    public async Task<IReadOnlyDictionary<string, decimal>> ClosesOnAsync(IReadOnlyList<string> tickers, DateOnly on)
    {
        var closes = new Dictionary<string, decimal>(StringComparer.Ordinal);

        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = ClosesOn;
        command.Parameters.AddWithValue("$on", Stamp(on));
        command.Parameters.AddWithValue("$tickers", System.Text.Json.JsonSerializer.Serialize(tickers));

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            closes[reader.GetString(0)] = Money.FromStorage(reader.GetString(1));
        }

        return closes;
    }

    // Each member's round trip on each night named as the member readings state it, in per cent of its price.
    public async Task<IReadOnlyDictionary<(string Ticker, DateOnly Night), double>> JudgeCostsAsync(IReadOnlyList<DateOnly> nights)
    {
        var costs = new Dictionary<(string, DateOnly), double>();

        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = JudgeCosts;
        command.Parameters.AddWithValue("$nights", System.Text.Json.JsonSerializer.Serialize(nights.Select(Stamp)));

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            costs[(reader.GetString(0), Day(reader.GetString(1)))] = reader.GetDouble(2);
        }

        return costs;
    }

    public async Task<IReadOnlyList<JudgeRunRow>> JudgeRunsAsync()
    {
        var rows = new List<JudgeRunRow>();

        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = JudgeRuns;

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var run = reader.GetString(0);

            rows.Add(new JudgeRunRow(run, Day(reader.GetString(1)), reader.GetString(2), reader.GetString(3), reader.GetInt32(4), reader.GetInt32(5), run.StartsWith(JudgeReplayPrefix, StringComparison.Ordinal)));
        }

        return rows;
    }

    public async Task<IReadOnlyList<JudgePickRow>> JudgePicksAsync()
    {
        var rows = new List<JudgePickRow>();

        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = JudgePicks;

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            rows.Add(new JudgePickRow(
                Day(reader.GetString(0)),
                reader.GetInt32(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                Money.FromStorage(reader.GetString(7)),
                reader.GetDouble(8),
                Money.FromStorage(reader.GetString(9)),
                Money.FromStorage(reader.GetString(10)),
                reader.IsDBNull(11) ? null : Money.FromStorage(reader.GetString(11)),
                reader.IsDBNull(12) ? null : reader.GetString(12),
                reader.IsDBNull(13) ? null : reader.GetString(13),
                reader.IsDBNull(14) ? null : reader.GetString(14),
                reader.GetString(15).StartsWith(JudgeReplayPrefix, StringComparison.Ordinal))
            {
                Named = reader.IsDBNull(16) ? null : reader.GetString(16).Split(',', StringSplitOptions.RemoveEmptyEntries),
                Version = reader.IsDBNull(17) ? null : reader.GetString(17),
                Sheet = reader.IsDBNull(18) ? new Dictionary<string, string>(StringComparer.Ordinal) : SheetOf(reader.GetString(18)),
            });
        }

        return rows;
    }

    // A member's stored fields as the judge was shown them, each field's text by its key.
    static IReadOnlyDictionary<string, string> SheetOf(string json)
    {
        var sheet = new Dictionary<string, string>(StringComparer.Ordinal);

        if (System.Text.Json.Nodes.JsonNode.Parse(json) is System.Text.Json.Nodes.JsonObject fields)
        {
            foreach (var (key, value) in fields)
            {
                if (value is System.Text.Json.Nodes.JsonValue text && text.TryGetValue(out string? shown) && shown is not null)
                {
                    sheet[key] = shown;
                }
            }
        }

        return sheet;
    }

    // Every member's sessions from a day on, each session's open and close as statistics, oldest first.
    public async Task<IReadOnlyDictionary<string, IReadOnlyList<(DateOnly Session, double Open, double Close)>>> JudgeBarsAsync(DateOnly from)
    {
        var bars = new Dictionary<string, List<(DateOnly, double, double)>>(StringComparer.Ordinal);

        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = JudgeBarsFrom;
        command.Parameters.AddWithValue("$from", Stamp(from));

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var ticker = reader.GetString(0);

            if (!bars.TryGetValue(ticker, out var held))
            {
                bars[ticker] = held = [];
            }

            held.Add((Day(reader.GetString(1)), Statistic.FromPrice(Money.FromStorage(reader.GetString(2))), Statistic.FromPrice(Money.FromStorage(reader.GetString(3)))));
        }

        return bars.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<(DateOnly, double, double)>)pair.Value, StringComparer.Ordinal);
    }

    // Each member's typical daily move on each night named.
    public async Task<IReadOnlyDictionary<(string Ticker, DateOnly Night), double>> JudgeTypicalsAsync(IReadOnlyList<DateOnly> nights)
    {
        var moves = new Dictionary<(string, DateOnly), double>();

        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = JudgeTypicals;
        command.Parameters.AddWithValue("$name", IndicatorSeries.Atr14);
        command.Parameters.AddWithValue("$nights", System.Text.Json.JsonSerializer.Serialize(nights.Select(Stamp)));

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            if (!reader.IsDBNull(2))
            {
                moves[(reader.GetString(0), Day(reader.GetString(1)))] = reader.GetDouble(2);
            }
        }

        return moves;
    }

    // The members the judge's facts were written for on each night named, the members a random pick is drawn from.
    public async Task<IReadOnlyDictionary<DateOnly, IReadOnlyList<string>>> JudgeMembersAsync(IReadOnlyList<DateOnly> nights)
    {
        var members = new Dictionary<DateOnly, List<string>>();

        await using var connection = Open();
        await using var command = connection.CreateCommand();

        command.CommandText = JudgeMembers;
        command.Parameters.AddWithValue("$nights", System.Text.Json.JsonSerializer.Serialize(nights.Select(Stamp)));

        await using var reader = await command.ExecuteReaderAsync();

        while (await reader.ReadAsync())
        {
            var night = Day(reader.GetString(0));

            if (!members.TryGetValue(night, out var held))
            {
                members[night] = held = [];
            }

            held.Add(reader.GetString(1));
        }

        return members.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<string>)pair.Value);
    }
}
