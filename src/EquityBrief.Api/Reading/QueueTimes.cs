using System.Globalization;
using EquityBrief.Api.Passes;
using EquityBrief.Core.Providers;

namespace EquityBrief.Api.Reading;

// What a request's time rests on, which the page states beside the time: a pass that can
// start now, the end of a peak window, an estimate over the passes the store holds, no
// estimate because the store holds none, a pass that has started, or a request settled.
public enum TimeBasis
{
    Now,
    PeakEnds,
    Estimated,
    NotEstimated,
    Started,
    Settled,
}

// When one request's pass will start or started, and when it is expected to end or ended.
public sealed record RequestTime(TimeBasis Basis, DateTimeOffset? Starts, DateTimeOffset? Ends, int Ahead);

// The passes a start behind other requests is estimated from: their median duration and how
// many there are, or none where the store holds no pass that ran to its end.
public sealed record PassEstimate(TimeSpan? Median, int Count);

// When each request on the queue will be written, worked out in one place from the queue, the
// passes the store holds, the prices' peak windows and the instant the page is drawn at.
//
// The drain takes requests one at a time, oldest first, and waits for the end of a peak window
// before a pass it would start inside one, so a request's start is the end of whatever is ahead
// of it, moved past a peak window where it falls in one. What is ahead of a request is the pass
// being written and every older request outstanding. A pass is expected to take the median of
// the passes the store holds, and where it holds none nothing behind the first request is given
// a time, because a time with nothing under it would read as a promise.
// see: The queue page states when each request will be written
public static class QueueTimes
{
    public static PassEstimate Estimate(IReadOnlyList<TimeSpan> passes)
    {
        if (passes.Count == 0)
        {
            return new PassEstimate(null, 0);
        }

        var sorted = passes.Order().ToArray();
        var middle = sorted.Length / 2;

        // An even count takes the mean of the two middle passes, which is the median's own rule.
        return new PassEstimate(
            sorted.Length % 2 == 1 ? sorted[middle] : (sorted[middle - 1] + sorted[middle]) / 2,
            sorted.Length);
    }

    // Every request's time, in the order the rows are given, which is the queue's own order:
    // oldest first. A request being written carries the instant its pass started, where the
    // run log holds one.
    public static IReadOnlyList<RequestTime> For(
        IReadOnlyList<RequestRow> rows,
        IReadOnlyDictionary<RequestRow, DateTimeOffset?> started,
        PassEstimate estimate,
        ResearchPricing? pricing,
        DateTimeOffset now)
    {
        DateTimeOffset OffPeak(DateTimeOffset at) => pricing is null ? at : pricing.OffPeakFrom(at);

        // Where the next pass can start, and whether that is known: after every pass being
        // written, each expected to end a median after it started and never before now.
        DateTimeOffset? cursor = now;
        var ahead = 0;

        foreach (var row in rows.Where(row => row.State == ResearchRequests.Writing))
        {
            ahead++;

            var start = started.GetValueOrDefault(row);
            var end = estimate.Median is { } median ? Latest(start ?? now, median, now) : (DateTimeOffset?)null;

            cursor = cursor is { } known && end is { } ends ? (ends > known ? ends : known) : null;
        }

        var times = new Dictionary<RequestRow, RequestTime>();

        foreach (var row in rows)
        {
            if (row.State == ResearchRequests.Writing)
            {
                var start = started.GetValueOrDefault(row);

                times[row] = new RequestTime(
                    TimeBasis.Started,
                    start,
                    estimate.Median is { } median ? Latest(start ?? now, median, now) : null,
                    0);
            }
            else if (row.State != ResearchRequests.Outstanding)
            {
                times[row] = new RequestTime(TimeBasis.Settled, null, row.SettledAt, 0);
            }
        }

        foreach (var row in rows.Where(row => row.State == ResearchRequests.Outstanding))
        {
            if (cursor is not { } from)
            {
                times[row] = new RequestTime(TimeBasis.NotEstimated, null, null, ahead);
            }
            else
            {
                var starts = OffPeak(from);
                var basis = ahead > 0
                    ? TimeBasis.Estimated
                    : starts > from ? TimeBasis.PeakEnds : TimeBasis.Now;
                var ends = estimate.Median is { } median ? starts + median : (DateTimeOffset?)null;

                times[row] = new RequestTime(basis, starts, ends, ahead);
                cursor = ends;
            }

            ahead++;
        }

        return [.. rows.Select(row => times[row])];
    }

    // A pass's expected end: a median after it started, and never before the instant the page
    // is drawn at, since a pass still being written has not ended yet.
    static DateTimeOffset Latest(DateTimeOffset start, TimeSpan median, DateTimeOffset now) =>
        start + median > now ? start + median : now;

    // An instant as the page states it: the date and time in New York with the offset that
    // applies on that date, and the time in UTC beside it.
    public static string Stated(DateTimeOffset instant, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTime(instant, zone);

        return local.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
            + " New York (UTC" + local.ToString("zzz", CultureInfo.InvariantCulture) + "), "
            + instant.UtcDateTime.ToString("HH:mm", CultureInfo.InvariantCulture) + " UTC";
    }

    // What the page says of a request's time, each instant in New York's time with its offset
    // and in UTC beside it.
    public static string Words(RequestTime time, RequestRow row, TimeZoneInfo zone)
    {
        string At(DateTimeOffset instant) => Stated(instant, zone);

        var ends = time.Ends is { } end ? ", and is expected to end about " + At(end) : string.Empty;

        return time.Basis switch
        {
            TimeBasis.Now => "starts now" + ends,
            TimeBasis.PeakEnds => "starts " + At(time.Starts!.Value) + ", when the peak window ends" + ends,
            TimeBasis.Estimated => "starts about " + At(time.Starts!.Value) + ", after " + Ahead(time.Ahead) + ends,
            TimeBasis.NotEstimated => "starts after " + Ahead(time.Ahead) + ", and no time is stated because the store holds no pass that ran to its end",
            TimeBasis.Started => (time.Starts is { } started ? "started " + At(started) : "started, and its pass has written no row yet")
                + (time.Ends is { } expected ? ", and is expected to end about " + At(expected) : ", and no end is stated because the store holds no pass that ran to its end"),
            _ => row.State + (time.Ends is { } settled ? " " + At(settled) : string.Empty),
        };
    }

    static string Ahead(int count) =>
        count == 1 ? "the 1 request ahead of it" : "the " + count.ToString(CultureInfo.InvariantCulture) + " requests ahead of it";

    // How long a pass is expected to take, in whole minutes, as the page states it beside the
    // count it is the median of.
    public static string Minutes(TimeSpan span) =>
        ((int)Math.Round(span.TotalMinutes, MidpointRounding.AwayFromZero)).ToString(CultureInfo.InvariantCulture);
}
