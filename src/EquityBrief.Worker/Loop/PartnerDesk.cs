using System.Globalization;
using EquityBrief.Core.Components;
using EquityBrief.Core.Configuration;
using EquityBrief.Core.Ledger;
using EquityBrief.Core.Loop;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using EquityBrief.Worker.Cards;
using EquityBrief.Worker.Research;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Loop;

// From 24.31 the trained partner's desk: on an index, a row a member each month of sessions from 2005 to 2018 off the
// every-session table, its readings on the session and its return over the month after off the long history's closes; the
// learned score's trees fitted on 2005 to 2014 alone and checked on 2015 to 2018; and the tables read off them written, which
// readings mattered most, each one's fifths and the best two's grid, each cell with its cases and never a name, a ticker, a
// date or anything from 2019 on. It reads only the index's own members, fits only on the operator's word, and moves no rule.
// see: The trained partner learns each index's members over 2005 to 2014 and is checked on 2015 to 2018, and its tables name no stock and read nothing from 2019
public sealed class PartnerDesk(IClock clock, string databaseFile, string dataRoot, TextWriter output, Func<TimeSpan, CancellationToken, Task>? pause = null) : IComponent
{
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.SessionReading, Touch.Read),
            new StoreTouch(Store.UnreliableMark, Touch.Read),
            new StoreTouch(Store.LongBar, Touch.Read),
            new StoreTouch(Store.PartnerRun, Touch.Insert),
            new StoreTouch(Store.PartnerCell, Touch.Insert),
            new StoreTouch(Store.RunLog, Touch.Insert),
        ],
        Feeds: []);

    public const string Verb = "partner";

    public const string WordFlag = "--word";

    static readonly TimeSpan Poll = TimeSpan.FromSeconds(30);

    static readonly string Through = new DateOnly(Partner.CheckThrough, 12, 31).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    static readonly string Sessions = $"SELECT DISTINCT session_date FROM session_reading WHERE index_code = $index AND session_date <= '{Through}' ORDER BY session_date;";

    static readonly string Readings = $"SELECT ticker, session_date, readings FROM session_reading WHERE index_code = $index AND session_date <= '{Through}';";

    static readonly string Closes = $@"
        SELECT ticker, session_date, close FROM long_bar
        WHERE session_date <= '{Through}' AND ticker IN (SELECT DISTINCT ticker FROM session_reading WHERE index_code = $index AND session_date <= '{Through}');";

    const string InsertRun = @"
        INSERT INTO partner_run (run_id, index_code, trained_rows, checked_rows, spread, model_hash, model, ran_at)
        VALUES ($run, $index, $trained, $checked, $spread, $hash, $model, $at);";

    const string InsertCell = @"
        INSERT INTO partner_cell (run_id, index_code, kind, reading, fifth, second, second_fifth, cases, figure)
        VALUES ($run, $index, $kind, $reading, $fifth, $second, $second_fifth, $cases, $figure);";

    const string AppendRun = @"
        INSERT INTO run_log (run_id, stage, started_at, ended_at, outcome, rows_written, model_calls, network_requests, spend, detail)
        VALUES ($run_id, $stage, $started_at, $ended_at, 'ok', $rows, 0, 0, '0', $detail);";

    // The rows of an index the partner reads: on every month of sessions from the index's first in the every-session table,
    // each member standing there with a close on the session and on the session a month after, both by 2018, its readings
    // and its return between them. No other index's members and nothing after 2018 are read.
    public async Task<IReadOnlyList<PartnerRow>> RowsAsync(string index, CancellationToken cancellation = default)
    {
        var calendar = new List<DateOnly>();

        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = Sessions;
            command.Parameters.AddWithValue("$index", index);

            await using var reader = await command.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                calendar.Add(Day(reader.GetString(0)));
            }
        }

        var place = calendar.Select((session, at) => (session, at)).ToDictionary(pair => pair.session, pair => pair.at);
        var closes = new Dictionary<string, double?[]>(StringComparer.Ordinal);

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = Closes;
            command.Parameters.AddWithValue("$index", index);

            await using var reader = await command.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                if (place.TryGetValue(Day(reader.GetString(1)), out var at))
                {
                    var ticker = reader.GetString(0);

                    if (!closes.TryGetValue(ticker, out var series))
                    {
                        closes[ticker] = series = new double?[calendar.Count];
                    }

                    series[at] = Statistic.FromPrice(Money.FromStorage(reader.GetString(2)));
                }
            }
        }

        var rows = new List<PartnerRow>();
        var marked = await Ledger.UnreliableMarkRows.ReadAsync(connection, cancellation);

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = Readings;
            command.Parameters.AddWithValue("$index", index);

            await using var reader = await command.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                // A stock-session the check marked unreliable or excluded is skipped.
                // see: A difference of 1% or more the check finds is settled by a second source's closes where one agrees with a side, and otherwise its stock-session is marked unreliable and skipped by every reader of the table
                if (marked.Skips(reader.GetString(0), Day(reader.GetString(1))))
                {
                    continue;
                }

                if (!place.TryGetValue(Day(reader.GetString(1)), out var at) || at % Partner.LabelSessions != 0 || at + Partner.LabelSessions >= calendar.Count)
                {
                    continue;
                }

                if (!closes.TryGetValue(reader.GetString(0), out var series) || series[at] is not { } now || now <= 0 || series[at + Partner.LabelSessions] is not { } then)
                {
                    continue;
                }

                double?[] readings = [.. System.Text.Json.JsonSerializer.Deserialize<double?[]>(reader.GetString(2)) ?? []];

                if (readings.Length != LedgerReadings.Count)
                {
                    continue;
                }

                rows.Add(new PartnerRow(calendar[at], calendar[at + Partner.LabelSessions], readings, (then / now) - 1));
            }
        }

        rows.Sort((one, other) => one.Session.CompareTo(other.Session));

        return rows;
    }

    // The partner on an index: without the word, its rows on each side of 2015 printed and nothing fitted; with it, its
    // trees fitted on 2005 to 2014 and checked on 2015 to 2018 and its tables written. 0 where it printed or wrote, 2 where it
    // refused to start.
    public async Task<int> RunAsync(string index, bool word, CancellationToken cancellation = default)
    {
        if (!WalkForwardTester.Indices.Contains(index, StringComparer.Ordinal))
        {
            output.WriteLine($"{Verb}: name an index with '--index', GSPC, MID or SML");

            return 2;
        }

        if (NightLock.Holder(dataRoot) is { } holder)
        {
            output.WriteLine($"{Verb}: the night holds the store ({holder}); run it once the night has finished");

            return 2;
        }

        var started = clock.UtcNow;
        var runId = FormattableString.Invariant($"{Verb}-{index}-{started.UtcDateTime:yyyyMMddTHHmmssZ}");
        var rows = await RowsAsync(index, cancellation);
        var (train, check) = Partner.Split(rows);
        var name = DecisionCards.NameOf(index);

        output.WriteLine(FormattableString.Invariant($"{Verb}: the {name}'s {rows.Count} row(s), a member each month of sessions: {train.Count} of {Partner.TrainFrom} to {Partner.TrainThrough} to learn on and {check.Count} of {Partner.CheckFrom} to {Partner.CheckThrough} to check on"));

        if (!word)
        {
            output.WriteLine($"{Verb}: nothing fitted; the partner is fitted over the store on the operator's word alone, given with '{WordFlag}'");

            return 0;
        }

        var scoreRows = new ScoreRows(rows.Count, clock.SessionDateAt(started));

        foreach (var row in rows)
        {
            scoreRows.Add(index, row.Session, row.Ended, row.Readings, row.Label, 1.0);
        }

        var model = TreeFit.Fit(scoreRows, train);
        var cells = new List<(string Kind, PartnerCell Cell, double? Figure)>();
        double? spread = null;

        if (model is not null)
        {
            spread = Partner.Spread(model, rows, check);

            var importances = Partner.Importances(model, rows, train, check);

            cells.AddRange(importances.Select((one, rank) => CellOf("importance", new PartnerCell(one.Reading, rank + 1, null, null, check.Count, null), one.Importance)));
            cells.AddRange(importances.SelectMany(one => Partner.Table(rows, train, check, one.Reading)).Select(cell => CellOf("table", cell, cell.Mean)));

            if (importances.Count >= 2)
            {
                cells.AddRange(Partner.Grid(rows, train, check, importances[0].Reading, importances[1].Reading).Select(cell => CellOf("grid", cell, cell.Mean)));
            }
        }

        using (var held = await DrainLock.AcquireAsync(dataRoot, () => (pause ?? Task.Delay)(Poll, cancellation), cancellation))
        {
            await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
            await connection.OpenAsync(cancellation);
            await using var transaction = connection.BeginTransaction();

            await using (var insert = connection.CreateCommand())
            {
                insert.Transaction = transaction;
                insert.CommandText = InsertRun;
                insert.Parameters.AddWithValue("$run", runId);
                insert.Parameters.AddWithValue("$index", index);
                insert.Parameters.AddWithValue("$trained", train.Count);
                insert.Parameters.AddWithValue("$checked", check.Count);
                insert.Parameters.AddWithValue("$spread", (object?)spread ?? DBNull.Value);
                insert.Parameters.AddWithValue("$hash", (object?)model?.Hash ?? DBNull.Value);
                insert.Parameters.AddWithValue("$model", (object?)model?.Canonical() ?? DBNull.Value);
                insert.Parameters.AddWithValue("$at", Instant(clock.UtcNow));
                await insert.ExecuteNonQueryAsync(cancellation);
            }

            foreach (var (kind, cell, figure) in cells)
            {
                await using var insert = connection.CreateCommand();

                insert.Transaction = transaction;
                insert.CommandText = InsertCell;
                insert.Parameters.AddWithValue("$run", runId);
                insert.Parameters.AddWithValue("$index", index);
                insert.Parameters.AddWithValue("$kind", kind);
                insert.Parameters.AddWithValue("$reading", Partner.NameOf(cell.Reading));
                insert.Parameters.AddWithValue("$fifth", cell.Fifth);
                insert.Parameters.AddWithValue("$second", cell.Second is { } second ? Partner.NameOf(second) : DBNull.Value);
                insert.Parameters.AddWithValue("$second_fifth", (object?)cell.SecondFifth ?? DBNull.Value);
                insert.Parameters.AddWithValue("$cases", cell.Cases);
                insert.Parameters.AddWithValue("$figure", (object?)figure ?? DBNull.Value);
                await insert.ExecuteNonQueryAsync(cancellation);
            }

            var detail = model is null
                ? FormattableString.Invariant($"the {name}'s rows of {Partner.TrainFrom} to {Partner.TrainThrough} are too few for the trees to learn on: {train.Count}")
                : FormattableString.Invariant($"the {name}'s trees {model.Hash} learned on {train.Count} row(s) and checked on {check.Count}, the top fifth's month less the bottom's {spread?.ToString("+0.0000;-0.0000", CultureInfo.InvariantCulture) ?? "none"}, {cells.Count} cell(s) written");

            await using (var log = connection.CreateCommand())
            {
                log.Transaction = transaction;
                log.CommandText = AppendRun;
                log.Parameters.AddWithValue("$run_id", runId);
                log.Parameters.AddWithValue("$stage", Verb);
                log.Parameters.AddWithValue("$started_at", Instant(started));
                log.Parameters.AddWithValue("$ended_at", Instant(clock.UtcNow));
                log.Parameters.AddWithValue("$rows", 1 + cells.Count);
                log.Parameters.AddWithValue("$detail", detail);
                await log.ExecuteNonQueryAsync(cancellation);
            }

            await transaction.CommitAsync(cancellation);
            output.WriteLine($"{Verb}: {detail}");
        }

        return 0;
    }

    static (string Kind, PartnerCell Cell, double? Figure) CellOf(string kind, PartnerCell cell, double? figure) => (kind, cell, figure);

    static string Instant(DateTimeOffset at) => at.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

    static DateOnly Day(string text) => DateOnly.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture);
}
