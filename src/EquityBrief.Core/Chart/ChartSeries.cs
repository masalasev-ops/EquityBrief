using EquityBrief.Core.Prices;

namespace EquityBrief.Core.Chart;

// One session of a stock's daily series as the chart page reads it: a stored bar, or a bar pulled before the store's
// year brought to the store's scale.
public sealed record SessionBar(DateOnly Session, decimal Open, decimal High, decimal Low, decimal Close, long Volume);

// One candle the chart page draws: the first and the last session it rolls up, the first session's open, the highest
// high, the lowest low, the last session's close and the volume of every session it holds.
public sealed record Candle(DateOnly First, DateOnly Last, decimal Open, decimal High, decimal Low, decimal Close, long Volume);

// A stretch of a weekly or monthly chart holding no daily row, between the candles either side of it.
public sealed record ChartHole(DateOnly After, DateOnly Before);

// The return over one span of the performance group, from the close it is measured from, none where the stored figures
// do not reach back that far.
public sealed record SpanReturn(string Span, double? Return, DateOnly? From);

public enum ChartInterval
{
    Daily,
    Weekly,
    Monthly,
}

// The chart page's arithmetic over the daily series, worked in the core when the page is read, so the page draws stored
// bars and computes nothing of its own.
// see: The bar store holds one year for every night's work, and the history pulled beside it is read by measurements and the name and chart pages' figures alone
public static class ChartSeries
{
    // The word the link names each interval by, the daily chart where the link names none or one it does not know.
    public static string Word(ChartInterval interval) => interval switch
    {
        ChartInterval.Weekly => "weekly",
        ChartInterval.Monthly => "monthly",
        _ => "daily",
    };

    public static ChartInterval Read(string? word) => word switch
    {
        "weekly" => ChartInterval.Weekly,
        "monthly" => ChartInterval.Monthly,
        _ => ChartInterval.Daily,
    };

    // The spans the performance group reads over the stored sessions, each a count of sessions back from the newest.
    public static IReadOnlyList<(string Span, int Sessions)> SessionSpans { get; } =
    [
        ("week", 5),
        ("month", 21),
        ("quarter", 63),
        ("half year", 126),
    ];

    public const string YearToDate = "year to date";

    // The spans read in years, one year off the stored sessions where they reach 252 back and every span otherwise off
    // the long view's month-end closes, which the night stores over the pulled history and the stored year at the
    // store's scale, so no span reads a pulled bar when the page is drawn.
    public static IReadOnlyList<(string Span, int Years)> YearSpans { get; } =
    [
        ("one year", 1),
        ("three years", 3),
        ("five years", 5),
        ("ten years", 10),
    ];

    public const int SessionsAYear = 252;

    // A pulled price brought to the store's scale: the ratio of the store's close to the pull's on the store's first
    // session, the session both hold, as the chart averages step brings its warm-up, rounded to the places the store
    // writes a price at. The volume stands as the pull sent it, since a ratio a dividend moved says nothing of shares.
    // see: The history pulled before the store's year sits apart from its bars under the pull that wrote it, read for the name and chart pages' figures alone and removed whole by that pull
    public static decimal Ratio(decimal storeClose, decimal pulledClose) =>
        pulledClose > 0
            ? storeClose / pulledClose
            : throw new ArgumentOutOfRangeException(nameof(pulledClose), pulledClose, "A pulled close of nothing sets no scale.");

    public static SessionBar AtStoreScale(SessionBar pulled, decimal ratio) => pulled with
    {
        Open = decimal.Round(pulled.Open * ratio, Statistic.Places),
        High = decimal.Round(pulled.High * ratio, Statistic.Places),
        Low = decimal.Round(pulled.Low * ratio, Statistic.Places),
        Close = decimal.Round(pulled.Close * ratio, Statistic.Places),
    };

    // The pulled sessions before the store's first, at the store's scale, then every stored session: the store's bar
    // read wherever it holds the session.
    public static IReadOnlyList<SessionBar> Joined(IReadOnlyList<SessionBar> stored, IReadOnlyList<SessionBar> pulled, decimal ratio)
    {
        if (stored.Count == 0)
        {
            return [];
        }

        var first = stored[0].Session;

        return
        [
            .. pulled.Where(bar => bar.Session < first).OrderBy(bar => bar.Session).Select(bar => AtStoreScale(bar, ratio)),
            .. stored,
        ];
    }

    // Each session a candle of its own on the daily chart; on the weekly chart the sessions of one week from Monday to
    // Friday make one candle, and on the monthly chart those of one calendar month.
    public static IReadOnlyList<Candle> Candles(IReadOnlyList<SessionBar> series, ChartInterval interval)
    {
        var ordered = series.OrderBy(bar => bar.Session).ToArray();

        if (interval == ChartInterval.Daily)
        {
            return [.. ordered.Select(bar => new Candle(bar.Session, bar.Session, bar.Open, bar.High, bar.Low, bar.Close, bar.Volume))];
        }

        return
        [
            .. ordered
                .GroupBy(bar => PeriodOf(bar.Session, interval))
                .Select(period => new Candle(
                    period.First().Session,
                    period.Last().Session,
                    period.First().Open,
                    period.Max(bar => bar.High),
                    period.Min(bar => bar.Low),
                    period.Last().Close,
                    period.Sum(bar => bar.Volume))),
        ];
    }

    // The week a session falls in by its Monday, and the month by its first day.
    public static DateOnly PeriodOf(DateOnly session, ChartInterval interval) => interval switch
    {
        ChartInterval.Weekly => session.AddDays(-(((int)session.DayOfWeek + 6) % 7)),
        ChartInterval.Monthly => new DateOnly(session.Year, session.Month, 1),
        _ => session,
    };

    // The weeks or months between two candles that hold no daily row, each named by the candles either side of it.
    public static IReadOnlyList<ChartHole> Holes(IReadOnlyList<Candle> candles, ChartInterval interval)
    {
        if (interval == ChartInterval.Daily)
        {
            return [];
        }

        var holes = new List<ChartHole>();

        for (var at = 1; at < candles.Count; at++)
        {
            var before = PeriodOf(candles[at - 1].Last, interval);
            var next = interval == ChartInterval.Weekly ? before.AddDays(7) : before.AddMonths(1);

            if (PeriodOf(candles[at].First, interval) > next)
            {
                holes.Add(new ChartHole(candles[at - 1].Last, candles[at].First));
            }
        }

        return holes;
    }

    // The return over each span, the newest stored close over the close it is measured from, less one: a session span's
    // that many sessions back; the year to date's the last close of the year before; and a year span's the month-end
    // close of the last month ending on or before the day that many years before the newest session, one year read off
    // the stored sessions where they reach it. None where the stored figures are short of the span, so a stock whose
    // history reaches eight years states no return over ten.
    public static IReadOnlyList<SpanReturn> Performance(IReadOnlyList<SessionBar> stored, IReadOnlyList<(DateOnly Session, decimal Close)> monthEnds)
    {
        var ordered = stored.OrderBy(bar => bar.Session).ToArray();

        if (ordered.Length == 0)
        {
            return
            [
                .. SessionSpans.Select(span => new SpanReturn(span.Span, null, null)),
                new SpanReturn(YearToDate, null, null),
                .. YearSpans.Select(span => new SpanReturn(span.Span, null, null)),
            ];
        }

        var newest = ordered[^1];
        var spans = new List<SpanReturn>();

        SpanReturn From(string span, DateOnly? on, decimal? close) =>
            on is { } day && close is > 0 ? new SpanReturn(span, Statistic.FromRatio((newest.Close / close.Value) - 1), day) : new SpanReturn(span, null, null);

        foreach (var (span, sessions) in SessionSpans)
        {
            spans.Add(ordered.Length > sessions ? From(span, ordered[^(sessions + 1)].Session, ordered[^(sessions + 1)].Close) : new SpanReturn(span, null, null));
        }

        var yearBefore = ordered.LastOrDefault(bar => bar.Session.Year < newest.Session.Year);

        spans.Add(From(YearToDate, yearBefore?.Session, yearBefore?.Close));

        var ends = monthEnds.OrderBy(end => end.Session).ToArray();

        foreach (var (span, years) in YearSpans)
        {
            if (years == 1 && ordered.Length > SessionsAYear)
            {
                spans.Add(From(span, ordered[^(SessionsAYear + 1)].Session, ordered[^(SessionsAYear + 1)].Close));

                continue;
            }

            // A year or more back every month has ended, so the newest month-end on or before the day is its month's last
            // session; one of a month before the month that ended last by the day is no close of that month, which the
            // long view does not hold, and the span reads none rather than an older close.
            var day = newest.Session.AddYears(-years);
            var month = ends.LastOrDefault(end => end.Session <= day);
            var lastEnded = new DateOnly(day.Year, day.Month, 1).AddMonths(-1);

            spans.Add(month.Session == default || new DateOnly(month.Session.Year, month.Session.Month, 1) < lastEnded
                ? new SpanReturn(span, null, null)
                : From(span, month.Session, month.Close));
        }

        return spans;
    }
}
