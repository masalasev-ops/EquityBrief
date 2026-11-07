using System.Globalization;
using EquityBrief.Core.Levels;

namespace EquityBrief.Core.Cards;

// A band of the night as the flags read it: its edges, whether a retracement anchors it, and how many times the price
// turned at it.
public sealed record FlagBand(decimal Low, decimal High, bool Retracement, int Touches)
{
    // A stored band's members read for the flags: a retracement among its kinds, and its turns as the levels region counts them.
    public static FlagBand Of(decimal low, decimal high, IReadOnlyList<(string Kind, DateOnly Date)> members) =>
        new(low, high, members.Any(member => member.Kind.StartsWith("retracement", StringComparison.Ordinal)), BandTurns.Of(members).Count);
}

// The arithmetic a plan's own numbers raise, as information beside the plan and changing nothing it does: a reward to risk
// to its first exit under one to one; a first exit at a retracement the price has never turned at, with the reward to risk
// to the first band above it that has; a gate passed by under a tenth; and a stop within about a typical day of the buy
// while a report falls inside the hold and the stock's median report-day move is wider than the stop.
// see: A plan's arithmetic is flagged beside it as information and changes nothing the plan does
public static class PlanFlags
{
    // How narrowly a gate's floor may be passed before the plan says so, and how near the buy a stop sits to be within
    // about a typical day.
    public const decimal GateMargin = 0.1m;
    public const double AboutATypicalDay = 1.25;

    public static IReadOnlyList<string> Of(
        decimal entry,
        decimal stop,
        decimal? exit,
        IReadOnlyList<FlagBand> bands,
        string exitName,
        decimal? gate = null,
        double? stopTypical = null,
        bool reportInside = false,
        double? reportMedianTypical = null)
    {
        var flags = new List<string>();
        var risk = entry - stop;

        if (risk <= 0)
        {
            return flags;
        }

        if (exit is { } first && first > entry)
        {
            var reward = (first - entry) / risk;

            if (reward < 1m)
            {
                flags.Add(I($"Its reward to risk to {exitName} is {reward:0.00} to 1, under one to one: it risks more than it makes there."));
            }

            if (bands.FirstOrDefault(band => band.Low <= first && first <= band.High) is { Retracement: true, Touches: 0 })
            {
                var turned = bands.Where(band => band.Low > first && band.Touches > 0).OrderBy(band => band.Low).FirstOrDefault();

                flags.Add(I($"{Capitalised(exitName)}, {first:0.00}, is a retracement the price has never turned at") + (turned is { } held
                    ? I($"; to the first band above it that has, {held.Low:0.00}, its reward to risk is {(held.Low - entry) / risk:0.00} to 1.")
                    : "; no band above it has turned the price."));
            }

            if (gate is { } floor && reward >= floor && reward - floor < GateMargin)
            {
                flags.Add(I($"It passed its {floor:0.##} to 1 gate by {reward - floor:0.00}, under a tenth."));
            }
        }

        if (stopTypical is { } near && near <= AboutATypicalDay && reportInside && reportMedianTypical is { } move && move > near)
        {
            flags.Add(I($"Its stop is {near:0.00} typical days below the buy while a report falls inside the hold, and its median report-day move of {move:0.00} typical days is wider than that."));
        }

        return flags;
    }

    static string Capitalised(string words) => words.Length == 0 ? words : char.ToUpperInvariant(words[0]) + words[1..];

    static string I(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
