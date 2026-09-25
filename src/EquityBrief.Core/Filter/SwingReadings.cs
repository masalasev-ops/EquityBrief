using EquityBrief.Core.Prices;

namespace EquityBrief.Core.Filter;

// One session as the swing readings take it: its high, its low and its close, the adjusted prices
// the store holds, and its volume.
public readonly record struct ReadingBar(DateOnly SessionDate, decimal High, decimal Low, decimal Close, long Volume);

// What one name's series gives on one night, each reading absent where the series is too short
// for its window or the night holds nothing it is measured against, with the bars it was read over.
//
// `High` is the highest high of the sessions the window holds, `HighSession` the newest session
// making it, and `PullbackSessions` how many sessions have traded since that one. `Depth` is how far
// tonight's close sits below the high in tonight's typical daily moves, `DryUp` the median volume of
// the sessions since the high against the fifty-day average, and `Tightness` the mean true range of
// the last ten sessions against the mean of the last fifty.
public sealed record SwingReading(
    DateOnly Session,
    int Bars,
    double? ReturnShort,
    double? ReturnLong,
    decimal? High,
    DateOnly? HighSession,
    int? PullbackSessions,
    double? Depth,
    double? DryUp,
    double? Tightness);

// The night's breadth: how many members were read, how many close above the average, and the
// share, which is absent where fewer than half the night's members could be read.
public sealed record Breadth(int Members, int Counted, int Above, double? Share);

// The readings the swing filter's gates are measured against, as pure functions of a
// session-ordered series and of the night's members, so the reader reads and writes and this
// decides.
// see: Code owns every number
public static class SwingReadings
{
    // The two spans a return is taken over: about a quarter and about half a year of trading.
    public const int ReturnShortSessions = 63;

    public const int ReturnLongSessions = 126;

    // The averages breadth is read against: the long one decides, and the shorter is context. The stored readings
    // are selected by these, so a figure stated here is the average read.
    public const int BreadthAverageSessions = 200;

    public const int ContextAverageSessions = 50;

    public static string AverageNamed(int sessions) => FormattableString.Invariant($"sma{sessions}");

    // The sessions the recent high is taken over, ending tonight.
    public const int HighWindow = 20;

    // The two spans the range's tightness compares: the last two weeks of trading against the last
    // ten.
    public const int TightShortSessions = 10;

    public const int TightLongSessions = 50;

    // A return over a span: tonight's close against the close that many sessions before it, in
    // per cent, and none where the series is too short to hold that close or it is not above nought.
    public static double? ReturnOver(IReadOnlyList<ReadingBar> bars, int sessions)
    {
        if (bars.Count <= sessions)
        {
            return null;
        }

        var from = bars[^(sessions + 1)].Close;

        return from > 0 ? Statistic.FromRatio((bars[^1].Close - from) / from) * 100 : null;
    }

    // Each member's place among the members' returns: the share of the other members whose return
    // is strictly lower, a member sharing a return with another counting that one at half. A single
    // member has no other to be placed among, and a member with no return has no place.
    public static IReadOnlyDictionary<string, double> Places(IReadOnlyDictionary<string, double> returns)
    {
        var places = new Dictionary<string, double>(StringComparer.Ordinal);

        if (returns.Count < 2)
        {
            return places;
        }

        var others = returns.Count - 1;

        foreach (var (ticker, value) in returns)
        {
            var lower = 0;
            var tied = 0;

            foreach (var (other, compared) in returns)
            {
                if (string.Equals(other, ticker, StringComparison.Ordinal))
                {
                    continue;
                }

                if (compared < value)
                {
                    lower++;
                }
                else if (compared == value)
                {
                    tied++;
                }
            }

            places[ticker] = (lower + (tied / 2.0)) / others;
        }

        return places;
    }

    // One name's readings over its stored series, tonight being its last bar, measured against
    // tonight's typical daily move and fifty-day average volume where the night holds them.
    public static SwingReading Of(IReadOnlyList<ReadingBar> bars, double? typicalMove, double? volumeAverage)
    {
        if (bars.Count == 0)
        {
            throw new ArgumentException("A series with no bar has no night to read.", nameof(bars));
        }

        var tonight = bars[^1];

        decimal? high = null;
        DateOnly? highSession = null;
        int? since = null;
        double? depth = null;
        double? dryUp = null;

        if (bars.Count >= HighWindow)
        {
            var window = bars.Skip(bars.Count - HighWindow).ToArray();
            var top = window.Max(bar => bar.High);

            // The newest session making the high, so a high made twice counts the pullback from the later one.
            var at = Array.FindLastIndex(window, bar => bar.High == top);

            high = top;
            highSession = window[at].SessionDate;
            since = window.Length - 1 - at;

            if (typicalMove is > 0 and var move)
            {
                depth = Statistic.FromPrice(top - tonight.Close) / move;
            }

            if (since > 0 && volumeAverage is > 0 and var average)
            {
                dryUp = Median([.. window.Skip(at + 1).Select(bar => Statistic.FromVolume(bar.Volume))]) / average;
            }
        }

        return new SwingReading(
            tonight.SessionDate,
            bars.Count,
            ReturnOver(bars, ReturnShortSessions),
            ReturnOver(bars, ReturnLongSessions),
            high,
            highSession,
            since,
            depth,
            dryUp,
            Tightness(bars));
    }

    // The mean true range of the last ten sessions against the mean of the last fifty, and none where
    // the series holds too few sessions with one before them or the longer mean is nought.
    public static double? Tightness(IReadOnlyList<ReadingBar> bars)
    {
        if (bars.Count <= TightLongSessions)
        {
            return null;
        }

        var ranges = new double[TightLongSessions];

        for (var at = 0; at < TightLongSessions; at++)
        {
            var index = bars.Count - TightLongSessions + at;

            ranges[at] = Statistic.FromPrice(TrueRange(bars[index], bars[index - 1].Close));
        }

        var longMean = ranges.Average();

        return longMean > 0 ? ranges.Skip(TightLongSessions - TightShortSessions).Average() / longMean : null;
    }

    // A session's true range, a price: the widest of its own range and its distance from the close before it.
    static decimal TrueRange(ReadingBar bar, decimal previousClose) =>
        Math.Max(bar.High - bar.Low, Math.Max(Math.Abs(bar.High - previousClose), Math.Abs(bar.Low - previousClose)));

    // The night's breadth over its members, each a close tonight and an average where it holds
    // both. Not available where fewer than half the night's members hold both, since a share over
    // a handful would open or close a list on a handful.
    public static Breadth BreadthOf(int members, IReadOnlyList<(decimal Close, double Average)> held)
    {
        var above = held.Count(pair => Statistic.FromPrice(pair.Close) > pair.Average);

        return new Breadth(
            members,
            held.Count,
            above,
            held.Count > 0 && held.Count * 2 >= members ? above * 1.0 / held.Count : null);
    }

    // The median's own rule: the middle value, the mean of the two middle ones over an even count,
    // and none over nothing.
    public static double? Median(IReadOnlyList<double> values)
    {
        if (values.Count == 0)
        {
            return null;
        }

        var ordered = values.Order().ToArray();
        var middle = ordered.Length / 2;

        return ordered.Length % 2 == 1 ? ordered[middle] : (ordered[middle - 1] + ordered[middle]) / 2;
    }
}
