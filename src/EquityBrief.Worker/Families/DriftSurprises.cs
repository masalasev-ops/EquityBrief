using System.Globalization;
using EquityBrief.Core.Components;
using EquityBrief.Core.Families;
using EquityBrief.Data;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Families;

// One S&P 500 drift row a night stored a surprise on: the night, the stock, the surprise its drift order read, the
// calendar's figure today for the newest print the stock reacted to by that night, none where the calendar holds none,
// and whether the row passed the drift.
public sealed record SurpriseRead(DateOnly Night, string Ticker, double Stored, double? Today, bool Passed);

// What the check found over every such row: the nights and rows read and the stocks among them; the rows whose figure
// today differs from the night's to two decimals, their stocks, the median and the largest difference in points and the
// row it was on; the rows turning from a miss to a beat or back and their stocks; the stocks of the differing rows that
// passed the drift; and the pulled surprises the history reads, the prints with a figure, their first and last report
// dates and the pulls that wrote them.
public sealed record SurpriseCheck(
    int Nights,
    int Rows,
    int Stocks,
    int Differ,
    int DifferStocks,
    double? Median,
    SurpriseRead? Largest,
    int Flipped,
    IReadOnlyList<string> FlippedStocks,
    IReadOnlyList<string> PassedStocks,
    int Pulled,
    DateOnly? PulledFrom,
    DateOnly? PulledThrough,
    int Pulls);

// From 21.14, on the operator's ruling of 2026-10-30, the read-only check of the surprises the drift reads: every drift
// row the S&P 500's nights stored a surprise on, from the first such night, read against the calendar's figure today
// for the same print through the night's own inputs, and the pulled surprises the drift's sweeps and the walk-forward
// tester read, so the operator sees how far the provider revised a surprise after a night read it and that the history
// takes each as pulled, revised. It reads the store and writes nothing, and prints what it found.
// see: A read-only check changes no rule, and an exit's pricing changes only by a fill step on the operator's word
public sealed class DriftSurprises(string databaseFile, TextWriter output) : IComponent
{
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.Bar, Touch.Read),
            new StoreTouch(Store.MarketBar, Touch.Read),
            new StoreTouch(Store.Indicator, Touch.Read),
            new StoreTouch(Store.Level, Touch.Read),
            new StoreTouch(Store.EarningsReaction, Touch.Read),
            new StoreTouch(Store.GateResult, Touch.Read),
            new StoreTouch(Store.FamilyResult, Touch.Read),
            new StoreTouch(Store.PulledSurprise, Touch.Read),
        ],
        Feeds: []);

    public const string Verb = "drift-surprises";

    const string Rows = "SELECT session_date, ticker, order_by, passed FROM family_result WHERE family = $family AND order_by IS NOT NULL ORDER BY session_date, ticker;";

    const string Pulled = "SELECT COUNT(*), MIN(event_date), MAX(event_date), COUNT(DISTINCT pull) FROM pulled_surprise WHERE surprise_percent IS NOT NULL;";

    public async Task<int> RunAsync(CancellationToken cancellation = default)
    {
        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        var stored = new List<(DateOnly Night, string Ticker, double Surprise, bool Passed)>();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = Rows;
            command.Parameters.AddWithValue("$family", DriftRule.Name);

            await using var reader = await command.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                stored.Add((Date(reader.GetString(0)), reader.GetString(1), reader.GetDouble(2), !reader.IsDBNull(3) && reader.GetInt64(3) == 1));
            }
        }

        var rows = new List<SurpriseRead>();

        foreach (var night in stored.GroupBy(row => row.Night).OrderBy(group => group.Key))
        {
            var inputs = await FamilyEvaluator.InputsAsync(connection, night.Key, 0, cancellation);
            var today = (inputs?.Members ?? [])
                .Where(member => member.Inputs.Drift.Print is not null)
                .ToDictionary(member => member.Ticker, member => member.Inputs.Drift.Print!.SurprisePct, StringComparer.Ordinal);

            rows.AddRange(night.Select(row => new SurpriseRead(row.Night, row.Ticker, row.Surprise, today.GetValueOrDefault(row.Ticker), row.Passed)));
        }

        (int Count, DateOnly? From, DateOnly? Through, int Pulls) pulled;

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = Pulled;

            await using var reader = await command.ExecuteReaderAsync(cancellation);

            await reader.ReadAsync(cancellation);
            pulled = (reader.GetInt32(0), reader.IsDBNull(1) ? null : Date(reader.GetString(1)), reader.IsDBNull(2) ? null : Date(reader.GetString(2)), reader.GetInt32(3));
        }

        foreach (var line in Lines(Of(rows, pulled)))
        {
            output.WriteLine(line);
        }

        return 0;
    }

    // The check over the rows read and the pulled surprises counted, worked by hand by the suite.
    public static SurpriseCheck Of(IReadOnlyList<SurpriseRead> rows, (int Count, DateOnly? From, DateOnly? Through, int Pulls) pulled)
    {
        var differ = rows.Where(row => row.Today is { } today && Math.Round(today, 2) != Math.Round(row.Stored, 2)).ToArray();
        var gaps = differ.Select(row => Math.Abs(row.Today!.Value - row.Stored)).OrderBy(gap => gap).ToArray();
        var flipped = differ.Where(row => Math.Sign(row.Stored) != Math.Sign(row.Today!.Value)).ToArray();

        return new SurpriseCheck(
            rows.Select(row => row.Night).Distinct().Count(),
            rows.Count,
            rows.Select(row => row.Ticker).Distinct(StringComparer.Ordinal).Count(),
            differ.Length,
            differ.Select(row => row.Ticker).Distinct(StringComparer.Ordinal).Count(),
            gaps.Length == 0 ? null : gaps.Length % 2 == 1 ? gaps[gaps.Length / 2] : (gaps[(gaps.Length / 2) - 1] + gaps[gaps.Length / 2]) / 2,
            differ.OrderByDescending(row => Math.Abs(row.Today!.Value - row.Stored)).FirstOrDefault(),
            flipped.Length,
            [.. flipped.Select(row => row.Ticker).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)],
            [.. differ.Where(row => row.Passed).Select(row => row.Ticker).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)],
            pulled.Count,
            pulled.From,
            pulled.Through,
            pulled.Pulls);
    }

    // What the command prints of the check.
    public static IReadOnlyList<string> Lines(SurpriseCheck check) =>
    [
        FormattableString.Invariant($"{Verb}: {check.Rows} drift row(s) over {check.Stocks} stock(s) on {check.Nights} night(s) stored a surprise"),
        FormattableString.Invariant($"{check.Differ} row(s) over {check.DifferStocks} stock(s) differ from the calendar's figure today, a median of {Points(check.Median)} and at most {Points(check.Largest is { } largest ? Math.Abs(largest.Today!.Value - largest.Stored) : null)}{(check.Largest is { } row ? FormattableString.Invariant($", {row.Ticker}'s {row.Stored:0.##} on {row.Night:yyyy-MM-dd} against {row.Today:0.##} today") : string.Empty)}"),
        FormattableString.Invariant($"{check.Flipped} row(s) turn from a miss to a beat or back: {(check.FlippedStocks.Count == 0 ? "none" : string.Join(", ", check.FlippedStocks))}"),
        FormattableString.Invariant($"of the differing rows the drift passed on: {(check.PassedStocks.Count == 0 ? "none" : string.Join(", ", check.PassedStocks))}"),
        FormattableString.Invariant($"the history the drift's sweeps and the walk-forward tester read takes its surprises as pulled, so revised: {check.Pulled} print(s) with a figure from {Day(check.PulledFrom)} to {Day(check.PulledThrough)}, written by {check.Pulls} pull(s)"),
        $"{Verb}: printed and nothing written",
    ];

    static string Points(double? points) => points is { } value ? value.ToString("0.0", CultureInfo.InvariantCulture) + " points" : "none";

    static string Day(DateOnly? day) => day is { } held ? held.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "none";

    static DateOnly Date(string stamp) => DateOnly.ParseExact(stamp, "yyyy-MM-dd", CultureInfo.InvariantCulture);
}
