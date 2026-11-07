using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Components;
using EquityBrief.Core.Providers;

namespace EquityBrief.Worker.History;

// The provider's answers the long history's membership is matched over, kept whole in a folder before anything reads them:
// the exchange's day file at the last session on or before each quarter end the schedules read, and at the quarter end after
// the last, which the code a holding carries is read from; and the exchange's symbol lists, listed and delisted. Nothing in
// the store is read or written. It states its requests and their weight before the first.
// see: The funds' older schedules are read from the trust's N-Q and N-CSR filings at each quarter end from 2004-12-31 to 2018-09-30
public sealed class LongDayFiles(ILongHistoryFeed feed) : IComponent
{
    public static ComponentAccess Access => new(Stores: [], Feeds: [Feed.BulkPrice, Feed.HistoricalPrice]);

    public const string Exchange = "US";

    public const string DaysFolder = "days";

    public const string ListedFile = "symbols-listed.json";

    public const string DelistedFile = "symbols-delisted.json";

    // The weighted calls the provider counts a day file at.
    public const int DayFileWeight = 100;

    // How many days before a quarter end its last session is looked for.
    public const int SessionDays = 7;

    // A day file holding fewer rows is a day the exchange did not trade.
    public const int TradedRows = 1000;

    public int Requests => feed.Requests;

    // The quarter ends a day file is kept for: each the schedules read, and the one after the last.
    public static IReadOnlyList<DateOnly> Quarters { get; } =
    [
        .. LongScheduleFilings.QuarterEnds,
        new DateOnly(LongScheduleFilings.LastPeriod.AddMonths(3).Year, LongScheduleFilings.LastPeriod.AddMonths(3).Month, DateTime.DaysInMonth(LongScheduleFilings.LastPeriod.AddMonths(3).Year, LongScheduleFilings.LastPeriod.AddMonths(3).Month)),
    ];

    // The rows of a day file, each code's close, none for a row stating none.
    public static IReadOnlyDictionary<string, decimal> ClosesOf(string dayFile)
    {
        using var document = JsonDocument.Parse(dayFile);
        var closes = new Dictionary<string, decimal>(StringComparer.Ordinal);

        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            return closes;
        }

        foreach (var row in document.RootElement.EnumerateArray())
        {
            if (row.TryGetProperty("code", out var code) && code.ValueKind == JsonValueKind.String
                && row.TryGetProperty("close", out var close) && close.ValueKind == JsonValueKind.Number && close.TryGetDecimal(out var price))
            {
                closes[code.GetString()!] = price;
            }
        }

        return closes;
    }

    // The session a day file's rows are for, the one most of them state; none where it holds none.
    public static DateOnly? SessionOf(string dayFile)
    {
        using var document = JsonDocument.Parse(dayFile);

        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        return document.RootElement.EnumerateArray()
            .Select(row => row.TryGetProperty("date", out var date) && DateOnly.TryParseExact(date.GetString(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day) ? day : (DateOnly?)null)
            .Where(day => day is not null)
            .GroupBy(day => day!.Value)
            .OrderByDescending(group => group.Count())
            .Select(group => (DateOnly?)group.Key)
            .FirstOrDefault();
    }

    // Each quarter end's day file and the two symbol lists, a file already kept asked for no more.
    public async Task<IReadOnlyList<(DateOnly Quarter, DateOnly Session, int Rows)>> CaptureAsync(string folder, Action<string> progress, CancellationToken cancellation = default)
    {
        var days = Path.Combine(folder, DaysFolder);
        var kept = new List<(DateOnly, DateOnly, int)>();

        Directory.CreateDirectory(days);

        progress(FormattableString.Invariant($"about {Quarters.Count} day file(s) at {DayFileWeight} weighted call(s) each, {Quarters.Count * DayFileWeight} in all, and 2 symbol list(s), before the first request"));

        foreach (var quarter in Quarters)
        {
            var found = Directory.GetFiles(days, "*.json").Select(file => DateOnly.ParseExact(Path.GetFileNameWithoutExtension(file), "yyyy-MM-dd", CultureInfo.InvariantCulture))
                .Where(session => session <= quarter && session > quarter.AddDays(-SessionDays))
                .OrderDescending()
                .Select(session => (DateOnly?)session)
                .FirstOrDefault();

            for (var day = quarter; found is null && day > quarter.AddDays(-SessionDays); day = day.AddDays(-1))
            {
                if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
                {
                    continue;
                }

                var answer = await feed.DayFileAsync(Exchange, day, cancellation);
                var rows = ClosesOf(answer).Count;

                if (rows >= TradedRows && SessionOf(answer) == day)
                {
                    await File.WriteAllTextAsync(Path.Combine(days, FormattableString.Invariant($"{day:yyyy-MM-dd}.json")), answer, cancellation);
                    found = day;
                    progress(FormattableString.Invariant($"kept the day file of {day:yyyy-MM-dd} for {quarter:yyyy-MM-dd}, {rows} row(s)"));
                }
            }

            if (found is { } session)
            {
                kept.Add((quarter, session, ClosesOf(await File.ReadAllTextAsync(Path.Combine(days, FormattableString.Invariant($"{session:yyyy-MM-dd}.json")), cancellation)).Count));
            }
            else
            {
                progress(FormattableString.Invariant($"no day file answered a session in the {SessionDays} days to {quarter:yyyy-MM-dd}"));
            }
        }

        foreach (var (file, delisted) in new[] { (ListedFile, false), (DelistedFile, true) })
        {
            if (!File.Exists(Path.Combine(folder, file)))
            {
                await File.WriteAllTextAsync(Path.Combine(folder, file), await feed.SymbolsAsync(Exchange, delisted, cancellation), cancellation);
                progress($"kept the {(delisted ? "delisted" : "listed")} symbol list");
            }
        }

        return kept;
    }
}
