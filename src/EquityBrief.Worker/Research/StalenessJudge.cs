using System.Globalization;
using EquityBrief.Core.Components;
using EquityBrief.Core.Research;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Research;

// The staleness judge. One question for one name, does the stored research still
// stand, answered from what the store already holds.
//
// On demand and per name, and it spends nothing: it holds no client, makes no
// request and calls no model, which is the whole reason the news pulse counter
// runs nightly. It writes the run log alone, so whether research is stale is
// derived on read rather than stored, which is the shape the gap state already
// has, and the name page reaches the same verdict through the same rules.
// see: Deciding not to spend must not cost anything
// see: Research goes stale on an event dated after the section was written, and a news spike is dated by the session it began
public sealed class StalenessJudge(IClock clock, string databaseFile) : IComponent
{
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.ResearchSection, Touch.Read),
            new StoreTouch(Store.Facts, Touch.Read),
            new StoreTouch(Store.Calendar, Touch.Read),
            new StoreTouch(Store.NewsPulse, Touch.Read),
            new StoreTouch(Store.Fundamentals, Touch.Read),
            new StoreTouch(Store.RunLog, Touch.Insert),
        ],
        Feeds: []);

    public const string Stage = "staleness";

    // The newest version of each section, whatever its status. The rules decide
    // which of them are research, so the judge does not filter before they do.
    const string NewestVersions = @"
        SELECT r.section, r.as_of, r.status
        FROM research_section r
        WHERE r.ticker = $ticker
          AND r.version = (SELECT MAX(s.version) FROM research_section s WHERE s.ticker = r.ticker AND s.section = r.section)
        ORDER BY r.section;
    ";

    // The night the store computed for this name, which is what the judgement is
    // as of, rather than the wall clock.
    const string NewestNight = @"
        SELECT MAX(session_date) FROM facts WHERE ticker = $ticker;
    ";

    const string NewestFiling = @"
        SELECT MAX(filing_date) FROM fundamentals WHERE ticker = $ticker;
    ";

    const string Earnings = @"
        SELECT event_date, timing FROM calendar WHERE ticker = $ticker AND kind = 'earnings' ORDER BY event_date;
    ";

    // A year of pulse at most, which is what the table keeps and more than the
    // ninety days and the window the rules read.
    const string Pulse = @"
        SELECT session_date, article_count FROM news_pulse WHERE ticker = $ticker ORDER BY session_date;
    ";

    const string AppendRun = @"
        INSERT INTO run_log (
            run_id, stage, started_at, ended_at, outcome,
            rows_written, model_calls, network_requests, spend, detail)
        VALUES (
            $run_id, $stage, $started_at, $ended_at, 'ok',
            0, 0, 0, '0', $detail);
    ";

    public async Task<StalenessVerdict> JudgeAsync(
        string ticker,
        bool refresh,
        string runId,
        CancellationToken cancellation = default)
    {
        var startedAt = clock.UtcNow;

        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        var sections = new List<SectionStanding>();

        await ReadAsync(connection, NewestVersions, ticker, cancellation, reader =>
            sections.Add(new SectionStanding(reader.GetString(0), Date(reader.GetString(1)), reader.GetString(2))));

        var earnings = new List<EarningsEvent>();

        await ReadAsync(connection, Earnings, ticker, cancellation, reader =>
            earnings.Add(new EarningsEvent(Date(reader.GetString(0)), reader.GetString(1))));

        var pulse = new List<PulseSession>();

        await ReadAsync(connection, Pulse, ticker, cancellation, reader =>
            pulse.Add(new PulseSession(Date(reader.GetString(0)), reader.GetInt32(1))));

        // A name the store has computed no night for is judged as of the session
        // the clock says it is, which is the only night there is to judge against.
        var night = await ScalarDateAsync(connection, NewestNight, ticker, cancellation)
            ?? clock.SessionDateAt(clock.UtcNow);

        var filed = await ScalarDateAsync(connection, NewestFiling, ticker, cancellation);

        var verdict = Staleness.Judge(night, sections, filed, earnings, pulse, refresh);

        await using var record = connection.CreateCommand();

        record.CommandText = AppendRun;
        record.Parameters.AddWithValue("$run_id", runId);
        record.Parameters.AddWithValue("$stage", Stage);
        record.Parameters.AddWithValue("$started_at", Instant(startedAt));
        record.Parameters.AddWithValue("$ended_at", Instant(clock.UtcNow));
        record.Parameters.AddWithValue("$detail", Detail(ticker, verdict));

        await record.ExecuteNonQueryAsync(cancellation);

        return verdict;
    }

    // The run log's line: the name, the night it was judged as of, the state, and
    // the line a page draws, with the stale sections named where there are any.
    public static string Detail(string ticker, StalenessVerdict verdict) =>
        FormattableString.Invariant($"{ticker} as of {verdict.Night:yyyy-MM-dd}: {verdict.State.ToString().ToLowerInvariant()}, {verdict.Line}")
        + (verdict.StaleSections.Count == 0 ? string.Empty : ". Stale: " + string.Join(", ", verdict.StaleSections));

    static async Task ReadAsync(
        SqliteConnection connection,
        string sql,
        string ticker,
        CancellationToken cancellation,
        Action<SqliteDataReader> read)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = sql;
        command.Parameters.AddWithValue("$ticker", ticker);

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            read(reader);
        }
    }

    static async Task<DateOnly?> ScalarDateAsync(
        SqliteConnection connection,
        string sql,
        string ticker,
        CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.CommandText = sql;
        command.Parameters.AddWithValue("$ticker", ticker);

        return await command.ExecuteScalarAsync(cancellation) is string value ? Date(value) : null;
    }

    static DateOnly Date(string value) =>
        DateOnly.ParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    static string Instant(DateTimeOffset instant) =>
        instant.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
}
