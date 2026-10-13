using System.Globalization;
using EquityBrief.Api.Passes;
using EquityBrief.Core.Research;
using EquityBrief.Core.Spending;
using EquityBrief.Web.App;

namespace EquityBrief.Api.Reading;

// The queue page's settled requests and the passes no request names: a settled row a stock and New York day of asking,
// with the newest request's outcome and how many times it was asked that day, and a row a New York day of the research
// passes no request names, with how many reports they wrote and what their paid calls cost, each over the newest
// fourteen New York days to the page's own or over every day.
// see: The queue page draws settled requests a row a stock and New York day over fourteen days, and the passes no request names a row a day
public static class QueueScreen
{
    // The New York days the settled requests and the passes no request names are drawn over, the page's own among them.
    public const int Window = 14;

    // One stock's settled requests on one New York day: the newest of them, whose outcome the row states, and how many
    // were asked.
    public sealed record SettledDay(string Ticker, DateOnly Day, RequestRow Latest, int Asked);

    // The research passes no request names on one New York day: how many wrote a paid call and what those calls cost.
    public sealed record ByHandDay(DateOnly Day, int Reports, decimal Cost);

    // The New York day an instant falls on.
    public static DateOnly DayOf(DateTimeOffset instant, TimeZoneInfo zone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(instant, zone).DateTime);

    // The first New York day the page draws, the page's own day the last of them.
    public static DateOnly FirstDay(DateTimeOffset now, TimeZoneInfo zone) => DayOf(now, zone).AddDays(-(Window - 1));

    // Every settled request a row a stock and day, newest day first and within a day the newest asked first, kept from
    // the first day given or every day where none is.
    public static IReadOnlyList<SettledDay> Settled(IReadOnlyList<RequestRow> rows, TimeZoneInfo zone, DateOnly? from) =>
    [
        .. rows
            .Where(row => row.State is not (ResearchRequests.Outstanding or ResearchRequests.Writing))
            .GroupBy(row => (row.Ticker, Day: DayOf(row.AskedAt, zone)))
            .Where(group => from is not { } first || group.Key.Day >= first)
            .Select(group => new SettledDay(group.Key.Ticker, group.Key.Day, group.MaxBy(row => row.AskedAt)!, group.Count()))
            .OrderByDescending(day => day.Day)
            .ThenByDescending(day => day.Latest.AskedAt)
            .ThenBy(day => day.Ticker, StringComparer.Ordinal),
    ];

    // The research passes no request names, a row a New York day of their start, newest first, each a run whose answered
    // paid calls the run log holds, counted once as a report with its calls' spend summed. A request names its pass's run
    // once it settles; a pass a request being written is under carries none yet, so a run of that stock started at or
    // after the request was asked is its pass and not one run by hand.
    public static IReadOnlyList<ByHandDay> ByHand(
        IReadOnlyList<(string RunId, decimal Spend)> paidCalls,
        IReadOnlyList<RequestRow> requests,
        TimeZoneInfo zone,
        DateOnly? from)
    {
        var named = requests.Select(row => row.RunId).OfType<string>().ToHashSet(StringComparer.Ordinal);
        var writing = requests.Where(row => row.State == ResearchRequests.Writing).ToArray();

        bool Requested(string runId, DateTimeOffset started) =>
            named.Contains(runId) || writing.Any(row => PassRun.IsFor(runId, row.Ticker) && started >= row.AskedAt);

        return
        [
            .. paidCalls
                .Select(call => (call.RunId, call.Spend, Started: PassRun.StartedAt(call.RunId)))
                .Where(call => call.Started is { } started && !Requested(call.RunId, started))
                .GroupBy(call => DayOf(call.Started!.Value, zone))
                .Where(group => from is not { } first || group.Key >= first)
                .Select(group => new ByHandDay(group.Key, group.Select(call => call.RunId).Distinct(StringComparer.Ordinal).Count(), group.Sum(call => call.Spend)))
                .OrderByDescending(day => day.Day),
        ];
    }

    // What the page draws beside the requests, over the window to the page's own New York day or over every day, with
    // how many rows of either table the window leaves out.
    public static QueueDays Drawn(
        IReadOnlyList<RequestRow> rows,
        IReadOnlyList<(string RunId, decimal Spend)> paidCalls,
        TimeZoneInfo zone,
        DateTimeOffset now,
        bool all)
    {
        DateOnly? from = all ? null : FirstDay(now, zone);

        var settled = Settled(rows, zone, from);
        var byHand = ByHand(paidCalls, rows, zone, from);
        var older = all ? 0 : Settled(rows, zone, null).Count - settled.Count + ByHand(paidCalls, rows, zone, null).Count - byHand.Count;

        return new QueueDays(
            [
                .. settled.Select(day => new SettledCell(
                    day.Ticker,
                    Dated(day.Day),
                    day.Latest.AskedAt.ToString(ResearchRequests.Instant, CultureInfo.InvariantCulture),
                    day.Latest.State,
                    day.Latest.SettledAt is { } at ? QueueTimes.Marked(at, zone) : null,
                    day.Latest.Reason,
                    day.Asked)),
            ],
            [.. byHand.Select(day => new ByHandCell(Dated(day.Day), day.Reports, day.Cost.ToString(CultureInfo.InvariantCulture), SpendVerdict.Money(day.Cost)))],
            older,
            all,
            Window);
    }

    static string Dated(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
