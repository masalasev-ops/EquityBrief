using System.Globalization;
using EquityBrief.Core.Prices;

namespace EquityBrief.Core.EntryCheck;

// The entry check's sentences, each one reading in plain words with its numbers explained, and the card's line of flags.
// see: The entry check reads a member's bars every night into a table of its own, and no rule reads it
public static class EntryWords
{
    public const string NothingFlagged = "Entry check: nothing flagged";
    public const string See = "See the entry check";
    public const string StateNotSet = "not set yet: thresholds come from the test over stored history";
    public const string NoOlderBand = "none above in the history held";
    public const string Provisional = "Provisional, and it sets nothing.";

    public const string OneOffPhrasesMiss = "This reads fixed phrases and can miss an item.";
    public const string NoItemFound = "no per-share item found by these phrases";
    public const string NoReleaseStored = "No results release is stored for this stock, so no one-off item can be read.";

    // What the newest results release names, none where no release is stored, which the region draws as an outline. Its share of
    // the beat stands only where the sentence ties the item to earnings per share, says which way it moved them and a beat is
    // stored for the release.
    // see: The entry check's one-off reading reads a stored release by fixed phrases, and says it can miss an item
    public static string? OneOff(EntryReading reading)
    {
        if (reading.ReleaseOn is not { } on)
        {
            return null;
        }

        if (reading is not { OneOffAmount: { } amount, OneOffSentence: { } sentence })
        {
            return I($"The results release of {Day(on)}: {NoItemFound}.");
        }

        var share = reading is { OneOffTied: true, OneOffDirection: { } direction, Beat: { } beat } && beat > 0
            ? I($" That is {Whole(Statistic.FromPrice(amount / beat * 100))}% of the {Dollars(beat)} beat. ") + (beat - (direction * amount) > 0 ? I($"Without it the beat is {Dollars(beat - (direction * amount))}.") : "Without it there is no beat.")
            : string.Empty;

        return I($"The results release of {Day(on)} says: \"{sentence}\" The release names an item worth about {Dollars(amount)} a share.") + share + " " + OneOffPhrasesMiss;
    }

    // The wait plan as one sentence marked provisional, or why the ladder draws none, none where the plan was never read.
    // see: The wait plan is drawn from the ladder's own bands and stop, is provisional and sets nothing
    public static string? Wait(EntryReading reading)
    {
        if (reading.WaitWhy is { } why)
        {
            return $"{Provisional} No wait plan: {why}.";
        }

        if (reading is not { WaitLow: { } low, WaitHigh: { } high })
        {
            return null;
        }

        var below = reading.ZoneDays is { } days ? I($", {Tenth(days)} typical days below tonight's close") : string.Empty;
        var said = I($"{Provisional} Wait to buy between {Dollars(low)} and {Dollars(high)}{below}.");

        if (reading.WaitStop is not { } stop)
        {
            return said + " The ladder places no stop under this zone, so it draws no exit.";
        }

        // see: Each plan states the share of the time it must reach its exit before its stop to break even, and whether its shares are the whole position
        return said + I($" Stop {Dollars(stop)}.") + (reading is { WaitExit: { } exit, WaitReward: { } reward }
            ? I($" First exit {Dollars(exit)}, about {Tenth(reward)} to 1. ") + Cards.PlanWords.BreakEvenSaid(Prices.Statistic.ToPrice(reward), "its first exit")
            : " No resistance band in the year stands at twice the risk, so it draws no first exit.");
    }

    public static string? Stretch(EntryReading reading)
    {
        if (reading is not { StretchPercent: { } percent, StretchShare: { } share })
        {
            return null;
        }

        var side = percent >= 0 ? "above" : "below";
        var days = reading.StretchDays is { } read ? I($", which is {Tenth(Math.Abs(read))} typical days") : string.Empty;

        return I($"Price is {Tenth(Math.Abs(percent))}% {side} its 20-session average{days}. {(share < 1 ? "Under 1" : Whole(share))}% of this stock's days have been this far above it or more.");
    }

    public static string? HotRun(EntryReading reading)
    {
        var parts = new List<string>();

        if (reading.GainTwenty is { } gain)
        {
            parts.Add(I($"The price moved {Signed(gain)}% over 20 sessions"));
        }

        if (reading.Rsi is { } rsi)
        {
            parts.Add(I($"its relative strength reads {Tenth(rsi)} of 100"));
        }

        if (reading.LargestGain is { } largest)
        {
            parts.Add(largest ? "tonight was the largest one-day gain in 250 sessions" : "tonight was not the largest one-day gain in 250 sessions");
        }

        if (reading.VolumeRatio is { } ratio)
        {
            parts.Add(I($"volume was {Tenth(ratio)} times its 50-session mean"));
        }

        return parts.Count == 0 ? null : string.Join("; ", parts) + ".";
    }

    // The gap, and where a pick's rule states a stop, that stop with its distance under the buy; the fall back into the gap is
    // named only where the stop sits inside the gap or at its top.
    public static string? Gap(EntryReading reading, decimal? buy, decimal? stop, string? rule)
    {
        if (reading is not { GapHigh: { } high, GapLow: { } low })
        {
            return null;
        }

        var said = I($"Tonight's low of {Dollars(low)} is above the prior session's high of {Dollars(high)}, a gap of {Dollars(low - high)}.");

        if (stop is not { } floor || buy is not { } entry)
        {
            return said;
        }

        var under = reading.TypicalDay is { } day && day > 0 ? I($", {Tenth((Statistic.FromPrice(entry) - Statistic.FromPrice(floor)) / day)} typical days under the buy") : string.Empty;

        return said + I($" {(rule is null ? "The rule's" : "The " + rule + " rule's")} stop is {Dollars(floor)}{under}.") + (floor >= high && floor <= low ? " A fall back into the gap would reach it." : string.Empty);
    }

    public static string Highest(EntryReading reading) =>
        reading.HighestSince is { } since
            ? I($"Tonight's close was last matched or passed on {Day(since)}.")
            : reading.HistoryStart is { } start
                ? I($"This is the highest close since its history starts on {Day(start)}.")
                : "The history held does not read how long since this close.";

    public static string Older(EntryReading reading) =>
        reading is { OlderLow: { } low, OlderHigh: { } high, OlderOn: { } on }
            ? I($"The nearest band above tonight's close from the pulled history is {Dollars(low)} to {Dollars(high)}, from {on.Year}") + (reading.OlderDays is { } days ? I($", {Tenth(days)} typical days above") : string.Empty) + "."
            : "Older resistance: " + NoOlderBand + ".";

    // The flags that apply in plain words, in the card's order.
    public static IReadOnlyList<string> Flags(EntryReading reading)
    {
        var flags = new List<string>();

        if (EntryReadings.Stretched(reading) && reading.StretchDays is { } days)
        {
            flags.Add(I($"Stretched: {Tenth(days)} typical days above its 20-session average"));
        }

        if (reading.LargestGain == true)
        {
            flags.Add("Largest one-day gain in 250 sessions");
        }

        if (reading.GapHigh is not null)
        {
            flags.Add("Gap below the buy");
        }

        if (EntryReadings.HighestLongSince(reading))
        {
            flags.Add(reading.HighestSince is { } since ? I($"Highest close since {Day(since)}") : I($"Highest close since its history starts on {Day(reading.HistoryStart!.Value)}"));
        }

        if (reading.OneOffAmount is not null)
        {
            flags.Add("Release names a one-off item");
        }

        return flags;
    }

    public static string CardLine(EntryReading reading) =>
        Flags(reading) is { Count: > 0 } flags ? string.Join(" · ", flags) : NothingFlagged;

    // The same flags in Home's plain words, for a reader new to investing, its days as Home writes a day.
    // see: Home's line of an idea's entry check says each flag in plain words, and dates a day as d Mon yyyy
    public static IReadOnlyList<string> HomeFlags(EntryReading reading)
    {
        var flags = new List<string>();

        if (EntryReadings.Stretched(reading) && reading.StretchPercent is { } percent)
        {
            flags.Add(I($"Stretched: {Tenth(percent)}% above its average price of the last 20 trading days"));
        }

        if (reading.LargestGain == true)
        {
            flags.Add("Biggest one-day rise in a year");
        }

        if (reading.GapHigh is not null)
        {
            flags.Add("Jumped at the open, leaving a gap below the buy price");
        }

        if (EntryReadings.HighestLongSince(reading))
        {
            flags.Add(reading.HighestSince is { } since ? I($"Highest close since {HomeDay(since)}") : I($"Highest close in its history held, back to {HomeDay(reading.HistoryStart!.Value)}"));
        }

        if (reading.OneOffAmount is not null)
        {
            flags.Add("The company's report includes a one-off item");
        }

        return flags;
    }

    public static string HomeLine(EntryReading reading) =>
        HomeFlags(reading) is { Count: > 0 } flags ? string.Join(" · ", flags) : NothingFlagged;

    static string I(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);

    static string Tenth(double value) => value.ToString("0.0", CultureInfo.InvariantCulture);

    static string Whole(double value) => value.ToString("0", CultureInfo.InvariantCulture);

    static string Signed(double value) => value.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture);

    static string Dollars(decimal value) => "$" + value.ToString("0.00", CultureInfo.InvariantCulture);

    static string Day(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    static string HomeDay(DateOnly day) => day.ToString("d MMM yyyy", CultureInfo.InvariantCulture);
}
