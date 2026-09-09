namespace EquityBrief.Core.Indicators;

// One indicator at one session. Value is null when the window the indicator
// needs is longer than the history behind that session, and BarCount is how
// many bars there were, which is what makes the null legible.
//
// Value is double because a statistic is double and a price is decimal, and the
// two worlds do not mix implicitly. SCHEMA declares the column REAL for the same
// reason: an average of a price is a statistic about prices, not a price, and
// nothing in a report quotes it as money.
public readonly record struct IndicatorPoint(DateOnly SessionDate, string Name, double? Value, int BarCount);

// One session's inputs, already across the money boundary.
//
// The engine crosses it once, explicitly, at the point it reads the store, and
// everything downstream of that is arithmetic in double. A record that carried
// decimals into here would push the crossing into every expression.
public readonly record struct SeriesBar(DateOnly SessionDate, double High, double Low, double Close, double Volume);

// The indicator arithmetic, as pure functions of a session-ordered series.
//
// Separate from the component that reads and writes, so the numbers can be
// asserted against arithmetic done by hand without a store in the way. Every
// definition is written out rather than referred to by name, because "RSI" names
// a family and the members disagree: the seeding is the whole difference and it
// is invisible in a result.
// see: Code owns every number
public static class IndicatorSeries
{
    public const string Sma20 = "sma20";
    public const string Sma50 = "sma50";
    public const string Sma200 = "sma200";
    public const string Rsi14 = "rsi14";
    public const string Macd = "macd";
    public const string MacdSignal = "macd_signal";
    public const string MacdHist = "macd_hist";
    public const string Atr14 = "atr14";
    public const string VolAvg20 = "vol_avg20";
    public const string VolAvg50 = "vol_avg50";

    // The ten SCHEMA's name column enumerates, in that order. Read from here by
    // the engine and by the check, so a name added to one is added to both.
    public static IReadOnlyList<string> Names { get; } =
    [
        Sma20, Sma50, Sma200, Rsi14, Macd, MacdSignal, MacdHist, Atr14, VolAvg20, VolAvg50,
    ];

    // The windows, and they are stated rather than inlined because bar_count is
    // the minimum of the window and the history, and a window written twice
    // drifts.
    public const int MacdFast = 12;
    public const int MacdSlow = 26;
    public const int MacdSmoothing = 9;
    public const int Wilder = 14;

    // MACD needs a slow average before it produces anything, and the signal
    // needs nine of those, so the first signal sits at 26 + 9 - 1 bars.
    public const int MacdSignalWarmup = MacdSlow + MacdSmoothing - 1;

    // RSI and ATR both read the change from the previous close, so fourteen
    // periods need fifteen bars.
    public const int WilderWarmup = Wilder + 1;

    public static IReadOnlyList<IndicatorPoint> For(IReadOnlyList<SeriesBar> bars)
    {
        var closes = bars.Select(bar => bar.Close).ToArray();
        var volumes = bars.Select(bar => bar.Volume).ToArray();

        var macd = new double?[bars.Count];
        var signal = new double?[bars.Count];
        var hist = new double?[bars.Count];

        MacdInto(closes, macd, signal, hist);

        var rsi = RsiInto(closes);
        var atr = AtrInto(bars);

        var points = new List<IndicatorPoint>(bars.Count * Names.Count);

        for (var index = 0; index < bars.Count; index++)
        {
            var session = bars[index].SessionDate;
            var history = index + 1;

            void Add(string name, double? value, int window) =>
                points.Add(new IndicatorPoint(session, name, value, Math.Min(history, window)));

            Add(Sma20, Mean(closes, index, 20), 20);
            Add(Sma50, Mean(closes, index, 50), 50);
            Add(Sma200, Mean(closes, index, 200), 200);
            Add(Rsi14, rsi[index], WilderWarmup);
            Add(Macd, macd[index], MacdSlow);
            Add(MacdSignal, signal[index], MacdSignalWarmup);
            Add(MacdHist, hist[index], MacdSignalWarmup);
            Add(Atr14, atr[index], WilderWarmup);
            Add(VolAvg20, Mean(volumes, index, 20), 20);
            Add(VolAvg50, Mean(volumes, index, 50), 50);
        }

        return points;
    }

    // The arithmetic mean of the last `window` values ending at `index`, or null
    // where there are not that many. Not a running sum: over a year of bars the
    // cost is nothing, and a running sum accumulates floating point error that a
    // reader cannot see and a fixture would freeze.
    static double? Mean(double[] values, int index, int window)
    {
        if (index + 1 < window)
        {
            return null;
        }

        var total = 0d;

        for (var at = index - window + 1; at <= index; at++)
        {
            total += values[at];
        }

        return total / window;
    }

    // Wilder's RSI. The first average gain and loss are the simple means of the
    // first fourteen changes, and every later one is the previous average
    // carried forward with a weight of thirteen fourteenths. That seeding is the
    // whole difference between this and the exponential form some libraries
    // ship, and the two disagree for the length of the series rather than for
    // the first few bars.
    static double?[] RsiInto(double[] closes)
    {
        var rsi = new double?[closes.Length];

        if (closes.Length < WilderWarmup)
        {
            return rsi;
        }

        var gain = 0d;
        var loss = 0d;

        for (var index = 1; index <= Wilder; index++)
        {
            var change = closes[index] - closes[index - 1];

            gain += change > 0 ? change : 0;
            loss += change < 0 ? -change : 0;
        }

        var averageGain = gain / Wilder;
        var averageLoss = loss / Wilder;

        rsi[Wilder] = FromAverages(averageGain, averageLoss);

        for (var index = Wilder + 1; index < closes.Length; index++)
        {
            var change = closes[index] - closes[index - 1];

            averageGain = ((averageGain * (Wilder - 1)) + (change > 0 ? change : 0)) / Wilder;
            averageLoss = ((averageLoss * (Wilder - 1)) + (change < 0 ? -change : 0)) / Wilder;

            rsi[index] = FromAverages(averageGain, averageLoss);
        }

        return rsi;
    }

    // A series that only rose has no average loss, and the ratio is undefined
    // rather than infinite. RSI is 100 there by definition, which is the value
    // the formula tends to, and returning it explicitly is what stops a division
    // producing an infinity that would be stored as one.
    static double FromAverages(double gain, double loss) =>
        loss == 0
            ? (gain == 0 ? 50 : 100)
            : 100 - (100 / (1 + (gain / loss)));

    // Wilder's ATR over the true range, seeded the same way as the RSI: the
    // simple mean of the first fourteen true ranges, then carried forward.
    //
    // True range is the widest of the session's own range, the gap up from the
    // previous close, and the gap down to it. The first session has no previous
    // close and so has no true range, which is why fourteen periods need fifteen
    // bars here as well.
    static double?[] AtrInto(IReadOnlyList<SeriesBar> bars)
    {
        var atr = new double?[bars.Count];

        if (bars.Count < WilderWarmup)
        {
            return atr;
        }

        var ranges = new double[bars.Count];

        for (var index = 1; index < bars.Count; index++)
        {
            var bar = bars[index];
            var previous = bars[index - 1].Close;

            ranges[index] = Math.Max(
                bar.High - bar.Low,
                Math.Max(Math.Abs(bar.High - previous), Math.Abs(bar.Low - previous)));
        }

        var seed = 0d;

        for (var index = 1; index <= Wilder; index++)
        {
            seed += ranges[index];
        }

        var average = seed / Wilder;

        atr[Wilder] = average;

        for (var index = Wilder + 1; index < bars.Count; index++)
        {
            average = ((average * (Wilder - 1)) + ranges[index]) / Wilder;
            atr[index] = average;
        }

        return atr;
    }

    // MACD, its signal and their difference.
    //
    // Both exponential averages are seeded with the simple mean of their own
    // first window rather than with the first close, which is the other common
    // seeding and produces a different series for hundreds of bars. The signal
    // is seeded the same way over the first nine MACD values, so it starts at
    // 26 + 9 - 1 bars rather than at 26.
    static void MacdInto(double[] closes, double?[] macd, double?[] signal, double?[] hist)
    {
        var fast = Ema(closes, MacdFast);
        var slow = Ema(closes, MacdSlow);

        for (var index = 0; index < closes.Length; index++)
        {
            if (fast[index] is { } quick && slow[index] is { } slower)
            {
                macd[index] = quick - slower;
            }
        }

        var line = macd
            .Select(value => value ?? 0)
            .ToArray();

        var start = MacdSlow - 1;

        if (start >= closes.Length)
        {
            return;
        }

        var smoothed = Ema(line[start..], MacdSmoothing);

        for (var index = 0; index < smoothed.Length; index++)
        {
            if (smoothed[index] is not { } value)
            {
                continue;
            }

            var at = start + index;

            signal[at] = value;
            hist[at] = macd[at] - value;
        }
    }

    // An exponential moving average seeded with the simple mean of its first
    // window, null until that window is full.
    static double?[] Ema(double[] values, int window)
    {
        var result = new double?[values.Length];

        if (values.Length < window)
        {
            return result;
        }

        var seed = 0d;

        for (var index = 0; index < window; index++)
        {
            seed += values[index];
        }

        var average = seed / window;
        var weight = 2d / (window + 1);

        result[window - 1] = average;

        for (var index = window; index < values.Length; index++)
        {
            average = ((values[index] - average) * weight) + average;
            result[index] = average;
        }

        return result;
    }
}
