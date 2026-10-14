using System.Globalization;
using EquityBrief.Api.Passes;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Research;
using EquityBrief.Web.Marks;

namespace EquityBrief.Api.Reading;

// What a request's time rests on, which the page states beside the time: a pass that can
// start now, the end of a peak window, the off-peak run the night's requests wait for, an
// estimate over the passes the store holds, no estimate because the store holds none, a pass
// that has started, or a request settled.
public enum TimeBasis
{
    Now,
    PeakEnds,
    OffPeakRun,
    Estimated,
    NotEstimated,
    Started,
    Settled,
}

// The research model at its peak price at the instant the page is drawn: when the window ends and how
// many times the off-peak price a call inside it costs, which the Write now press states before it asks.
public sealed record PeakNow(DateTimeOffset Ends, decimal Multiple);

// When one request's pass will start or started, and when it is expected to end or ended.
public sealed record RequestTime(TimeBasis Basis, DateTimeOffset? Starts, DateTimeOffset? Ends, int Ahead);

// The passes a start behind other requests is estimated from: their median duration, how many
// there are and the longest of them, which bounds when a pass may start, or none where the store
// holds no pass that ran to its end.
public sealed record PassEstimate(TimeSpan? Median, int Count, TimeSpan? Longest = null);

// When each request on the queue will be written, worked out in one place from the queue, the
// passes the store holds, the prices' peak windows and the instant the page is drawn at.
//
// The drain takes requests one at a time, oldest first, and waits for the end of a peak window
// before a pass it would start inside one or that the longest pass the store holds would carry
// into one, so a request's start is the end of whatever is ahead of it, moved past a peak window
// where it falls in one or would run into one. What is ahead of a request is the pass
// being written and every older request outstanding. A pass is expected to take the median of
// the passes the store holds, and where it holds none nothing behind the first request is given
// a time, because a time with nothing under it would read as a promise.
//
// The night starts no drain, so a queue holding the night's requests alone, with nothing being
// written, starts at the next off-peak run and not now; a press for any name starts the drain,
// which takes the night's requests ahead of it in their order.
// see: The queue page states when each request will be written in New York's time with UTC beneath the pointer
// see: The night queues its report requests and starts neither the drain nor the labeller, and the off-peak run starts both five minutes after each peak window ends
public static class QueueTimes
{
    // The peak price at an instant: where any profile the drain waits out is at peak, the instant every one of
    // them is off peak again and the largest multiple of those at peak, and none where none is.
    public static PeakNow? Peak(IReadOnlyList<ResearchPricing> pricings, DateTimeOffset now)
    {
        var atPeak = pricings.Where(pricing => pricing.IsPeak(now)).ToArray();

        if (atPeak.Length == 0)
        {
            return null;
        }

        return new PeakNow(ResearchPricing.StartFor(pricings, now, TimeSpan.Zero), atPeak.Max(pricing => pricing.PeakMultiple));
    }

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
            sorted.Length,
            sorted[^1]);
    }

    // Every request's time, in the order the rows are given, which is the queue's own order:
    // oldest first. A request being written carries the instant its pass started, where the
    // run log holds one.
    public static IReadOnlyList<RequestTime> For(
        IReadOnlyList<RequestRow> rows,
        IReadOnlyDictionary<RequestRow, DateTimeOffset?> started,
        PassEstimate estimate,
        IReadOnlyList<ResearchPricing> pricings,
        DateTimeOffset now)
    {
        // The instant the drain would take a request at, from the one function it calls over the same
        // profiles' windows, so the page and the drain cannot disagree about when a pass starts.
        // see: A pass starts only where the longest pass the store holds would end before a peak window opens
        DateTimeOffset OffPeak(DateTimeOffset at) => ResearchPricing.StartFor(pricings, at, estimate.Longest ?? TimeSpan.Zero);

        // A queue of the night's requests alone, with no pass being written, waits for the next off-peak run.
        var run = rows.Any(row => row.State == ResearchRequests.Writing)
            || rows.Any(row => row.State == ResearchRequests.Outstanding && row.AskedFrom != ResearchRequests.FromNight)
                ? null
                : OffPeakRun.NextAt(pricings, now);

        // Where the next pass can start, and whether that is known: after every pass being
        // written, each expected to end a median after it started and never before now.
        DateTimeOffset? cursor = run ?? now;
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
                    : run is not null ? TimeBasis.OffPeakRun
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

    // An instant as tonight's rows and the selected name's region state it: the date and time in
    // New York with the offset that applies on that date, and the time in UTC beside it.
    public static string Stated(DateTimeOffset instant, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTime(instant, zone);

        return local.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
            + " New York (UTC" + local.ToString("zzz", CultureInfo.InvariantCulture) + "), "
            + instant.UtcDateTime.ToString("HH:mm", CultureInfo.InvariantCulture) + " UTC";
    }

    // An instant as the queue page draws it: the date and time in New York marked ET, with the
    // date and time in UTC beneath the pointer and the instant whole on the element.
    // see: The queue page states when each request will be written in New York's time with UTC beneath the pointer
    public static string Marked(DateTimeOffset instant, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTime(instant, zone);
        var utc = instant.UtcDateTime;

        return "<time datetime=\"" + utc.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture)
            + "\" title=\"" + utc.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " UTC\">"
            + local.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " ET</time>";
    }

    // What the queue page says of a request's time, as markup, each instant marked as above. The
    // words around the instants are the code's own and carry nothing markup reads.
    public static string Words(RequestTime time, RequestRow row, TimeZoneInfo zone)
    {
        string At(DateTimeOffset instant) => Marked(instant, zone);

        var ends = time.Ends is { } end ? ", and is expected to end about " + At(end) : string.Empty;

        return time.Basis switch
        {
            TimeBasis.Now => "starts now" + ends,
            TimeBasis.PeakEnds => "starts " + At(time.Starts!.Value) + ", when the peak window ends" + ends,
            TimeBasis.OffPeakRun => "starts " + At(time.Starts!.Value) + ", at the off-peak run the night's requests wait for" + ends,
            TimeBasis.Estimated => "starts about " + At(time.Starts!.Value) + ", after " + Ahead(time.Ahead) + ends,
            TimeBasis.NotEstimated => "starts after " + Ahead(time.Ahead) + ", and no time is stated because the store holds no pass that ran to its end",
            TimeBasis.Started => (time.Starts is { } started ? "started " + At(started) : "started, and its pass has written no row yet")
                + (time.Ends is { } expected ? ", and is expected to end about " + At(expected) : ", and no end is stated because the store holds no pass that ran to its end"),
            _ => row.State + (time.Ends is { } settled ? " " + At(settled) : string.Empty),
        };
    }

    // What the queue holds for each name with a request nobody has settled, as tonight's rows
    // and the selected name's region state it, from the same times the queue page states.
    public static IReadOnlyDictionary<string, QueueState> States(
        IReadOnlyList<RequestRow> rows,
        IReadOnlyList<RequestTime> times,
        TimeZoneInfo zone)
    {
        var states = new Dictionary<string, QueueState>(StringComparer.Ordinal);

        for (var at = 0; at < rows.Count; at++)
        {
            var (row, time) = (rows[at], times[at]);
            var instant = time.Starts?.ToString(ResearchRequests.Instant, CultureInfo.InvariantCulture);

            if (row.State == ResearchRequests.Writing)
            {
                states[row.Ticker] = new QueueState(
                    QueueState.Writing,
                    instant,
                    time.Starts is { } started ? "being written since " + Stated(started, zone) : "being written, and its pass has written no row yet");
            }
            else if (row.State == ResearchRequests.Outstanding)
            {
                states[row.Ticker] = new QueueState(
                    QueueState.Queued,
                    instant,
                    time.Basis switch
                    {
                        TimeBasis.Now => "queued, and starts now",
                        TimeBasis.PeakEnds => "queued, and starts " + Stated(time.Starts!.Value, zone) + ", when the peak window ends",
                        TimeBasis.OffPeakRun => "queued, and starts " + Stated(time.Starts!.Value, zone) + ", at the off-peak run",
                        TimeBasis.Estimated => "queued, and starts about " + Stated(time.Starts!.Value, zone),
                        _ => "queued behind " + Ahead(time.Ahead),
                    });
            }
        }

        return states;
    }

    // A price multiple in words: twice for two, and the figure and times otherwise.
    public static string Times(decimal multiple) =>
        multiple == 2m ? "twice" : multiple.ToString("0.##", CultureInfo.InvariantCulture) + " times";

    static string Ahead(int count) =>
        count == 1 ? "the 1 request ahead of it" : "the " + count.ToString(CultureInfo.InvariantCulture) + " requests ahead of it";

    // How long a pass is expected to take, in whole minutes, as the page states it beside the
    // count it is the median of.
    public static string Minutes(TimeSpan span) =>
        ((int)Math.Round(span.TotalMinutes, MidpointRounding.AwayFromZero)).ToString(CultureInfo.InvariantCulture);
}
