namespace EquityBrief.Core.EntryCheck;

// A wait entry's fill: the bar it traded into its level on, and the price it paid.
public readonly record struct WaitFill(int Bar, double Price);

// One trade as the measurement reads it: its result in the risk the rule's own plan set, its edge after costs, how it ended, and what two
// entries with different stops can be compared by: the sale over the fill in per cent and the result in the risk its own plan put up.
public readonly record struct EntryTrade(double Result, double? Edge, string End, double? Percent = null, double? OwnRisk = null);

// What one entry or one band came to over a group of trades. Rates and means read over the trades held, each mean its own count.
public sealed record EntryGroup(int Trades, double? MeanResult, double? TargetRate, double? StopRate, double? TrailRate, int Edges, double? MeanEdge, double? MeanPercent, double? MeanOwnRisk);

// The bands the entry check's read-only measurement splits each reading into, and the wait entries it walks. Each band's edges are
// provisional and pick a starting point; no rule, plan, list or gate reads them.
// see: The entry check's measurement is read-only over the stored years and registers nothing
public static class EntryMeasure
{
    public const int YearDays = 365;
    public const int ThreeYearDays = 1095;

    public const int FirstRecentYear = 2025;
    public const string BeforeRecent = "before 2025";
    public const string FromRecent = "from 2025";

    public const string Unread = "not read";
    public const string AllTrades = "all";

    public const string Stretch = "Stretch in typical days";
    public const string StretchedAtTheLine = "Stretched at the card's line";
    public const string GainTwenty = "Gain over 20 sessions";
    public const string Rsi = "RSI";
    public const string LargestGain = "Largest gain in 250 sessions";
    public const string VolumeRatio = "Volume against its 50-session mean";
    public const string GapSize = "Gap in typical days";
    public const string StopDistance = "Stop under the buy in typical days";
    public const string HighestClose = "Highest close since";
    public const string OlderResistance = "Older resistance above in typical days";
    public const string OneOffShare = "One-off item's share of the beat";

    public const string NoRelease = "no release stored";
    public const string NoItem = "no item found";
    public const string NoShare = "an item with no share read";
    public const string OverHalf = "over half";
    public const string HalfOrLess = "half or less";

    // The sessions a wait entry may take to fill, each walked apart.
    public static IReadOnlyList<int> Windows { get; } = [3, 5, 10];

    static string Between(double? value, double low, double high, string lowWords, string midWords, string highWords) =>
        value is not { } read || !double.IsFinite(read) ? Unread : read < low ? lowWords : read <= high ? midWords : highWords;

    // Each reading's band. The order names the highest close's and the one-off's; the rest are the order's readings at round edges.
    public static IReadOnlyList<(string Reading, string Band)> BandsOf(EntryReading reading, double? stopDays) =>
    [
        (Stretch, Between(reading.StretchDays, 2, 4, "under 2", "2 to 4", "over 4")),
        (StretchedAtTheLine, reading.StretchShare is null ? Unread : EntryReadings.Stretched(reading) ? "yes" : "no"),
        (GainTwenty, Between(reading.GainTwenty, 10, 25, "under 10%", "10% to 25%", "over 25%")),
        (Rsi, Between(reading.Rsi, 60, 70, "under 60", "60 to 70", "over 70")),
        (LargestGain, reading.LargestGain is { } largest ? largest ? "yes" : "no" : Unread),
        (VolumeRatio, Between(reading.VolumeRatio, 1.5, 3, "under 1.5", "1.5 to 3", "over 3")),
        (GapSize, GapBand(reading)),
        (StopDistance, Between(stopDays, 2, 4, "under 2", "2 to 4", "over 4")),
        (HighestClose, HighestBand(reading)),
        (OlderResistance, reading.OlderLow is null ? "none above" : Between(reading.OlderDays, 3, 10, "under 3", "3 to 10", "over 10")),
        (OneOffShare, OneOffBand(reading)),
    ];

    static string GapBand(EntryReading reading)
    {
        if (reading is not { GapHigh: { } high, GapLow: { } low })
        {
            return "no gap";
        }

        return reading.TypicalDay is { } day && day > 0 ? Prices.Statistic.FromPrice(low - high) / day <= 1 ? "up to 1" : "over 1" : Unread;
    }

    // The order's two spans of the stored years.
    public static string EraOf(DateOnly session) => session.Year < FirstRecentYear ? BeforeRecent : FromRecent;

    // How long since the close was last matched, a close never matched reading from where its history starts, so a short history
    // reads as a short lookback rather than a long one.
    static string HighestBand(EntryReading reading)
    {
        if ((reading.HighestSince ?? reading.HistoryStart) is not { } since)
        {
            return Unread;
        }

        var days = reading.Session.DayNumber - since.DayNumber;

        return days < YearDays ? "under 1 year" : days <= ThreeYearDays ? "1 to 3 years" : "over 3 years";
    }

    // The item's share of the beat, read only where the sentence ties it to earnings per share and a beat is stored, as the page reads it.
    static string OneOffBand(EntryReading reading)
    {
        if (reading.ReleaseOn is null)
        {
            return NoRelease;
        }

        if (reading.OneOffAmount is not { } amount)
        {
            return NoItem;
        }

        return reading is { OneOffTied: true, OneOffDirection: not null, Beat: { } beat } && beat > 0
            ? amount / beat > 0.5m ? OverHalf : HalfOrLess
            : NoShare;
    }

    // The first session after the pick, within the window, that trades into the level: one opening at or under it fills at that open, and
    // one reaching it after opening above fills at the level. None where no session does in the window, the series ends first, or the fill
    // would stand at or under the stop, since the plan is already broken there: each of those a pick not taken.
    public static WaitFill? FillOf(ReadOnlySpan<double> opens, ReadOnlySpan<double> lows, int pick, double level, double stop, int within)
    {
        if (!(level > stop))
        {
            return null;
        }

        for (var bar = pick + 1; bar <= pick + within && bar < opens.Length && bar < lows.Length; bar++)
        {
            if (opens[bar] <= 0)
            {
                continue;
            }

            if (opens[bar] <= level)
            {
                return opens[bar] > stop ? new WaitFill(bar, opens[bar]) : null;
            }

            if (lows[bar] <= level)
            {
                return new WaitFill(bar, level);
            }
        }

        return null;
    }

    // A result in a plan's own risk restated in the risk the rule's plan set, so two plans are compared on the same number of shares; as it
    // stands where the rule's plan sets no risk.
    public static double InTheRulesRisk(double result, double ownRisk, double rulesRisk) =>
        rulesRisk > 0 ? result * ownRisk / rulesRisk : result;

    // A group's figures: the trades holding a result, the share ending at the target, at the stop and at a trail, and the edges, the
    // percentages and the own risks held among them.
    public static EntryGroup GroupOf(IReadOnlyList<EntryTrade> trades)
    {
        if (trades.Count == 0)
        {
            return new EntryGroup(0, null, null, null, null, 0, null, null, null);
        }

        static double? Mean(IEnumerable<double?> values)
        {
            var held = values.OfType<double>().ToArray();

            return held.Length == 0 ? null : held.Average();
        }

        double Rate(string end) => 100d * trades.Count(trade => trade.End == end) / trades.Count;

        return new EntryGroup(
            trades.Count,
            trades.Average(trade => trade.Result),
            Rate(Ledger.SetupEnds.Target),
            Rate(Ledger.SetupEnds.Stop),
            Rate(Ledger.SetupEnds.Trail),
            trades.Count(trade => trade.Edge is not null),
            Mean(trades.Select(trade => trade.Edge)),
            Mean(trades.Select(trade => trade.Percent)),
            Mean(trades.Select(trade => trade.OwnRisk)));
    }
}
