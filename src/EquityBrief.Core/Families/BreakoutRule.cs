using EquityBrief.Core.Filter;
using EquityBrief.Core.Prices;
using static EquityBrief.Core.Families.FamilyRule;

namespace EquityBrief.Core.Families;

// What the breakout's rule reads for one member on one night: the market check the night stored, the
// member's sessions oldest first and ending tonight, its 50-session average volume and its typical daily
// move as the indicators stored them, and the exclusions its series carries.
public sealed record BreakoutInputs(
    string Ticker,
    Gate Market,
    IReadOnlyList<FamilyBar> Bars,
    double? VolumeAverage50,
    double? TypicalMove,
    IReadOnlyList<string> Exclusions);

// The breakout family's rule: a close above the highest high of the year before it, on heavy volume, after
// the daily ranges narrowed. Bought at the close with a stop two typical moves beneath, which trails the
// highest close since the buy and is never lowered, and with no target.
//
// Every setting here is provisional, taken from published evidence and the operator's ruling, until the
// family's sweep proposes the values its freeze registers.
// see: A breakout is a close above the year's high on heavy volume after its ranges narrowed, sold on a trailing stop with no target
// see: A family runs on provisional settings until its freeze, and nothing before the freeze counts toward a checkpoint
public static class BreakoutRule
{
    public const string Name = "breakout";

    // The horizon a breakout's trade is scored under.
    public const string Horizon = "breakout";

    // The sessions before tonight's the close is read against, which with tonight's is every session the
    // store keeps for a name.
    public const int HighSessions = 251;

    public const double VolumeMultiple = 1.5;

    // The sessions the daily range is averaged over, and the most the newer average may be of the older.
    public const int RangeSessions = 20;

    public const double RangeCeiling = 1.0;

    // The typical moves the stop sits beneath the close and trails the highest close by.
    public const double StopMoves = 2;

    public const int CapSessions = 63;

    public const string NewHigh = "new high";
    public const string Volume = "volume";
    public const string Tightened = "tightened";

    public static readonly string[] Order = [Market, NewHigh, Volume, Tightened, Trade];

    public static FamilyResult Evaluate(BreakoutInputs inputs)
    {
        var (trade, stop) = TradeGate(inputs);
        var volume = VolumeGate(inputs);

        return new FamilyResult(
            inputs.Ticker,
            Name,
            [inputs.Market, NewHighGate(inputs), volume.Gate, TightenedGate(inputs), trade],
            inputs.Bars.Count > 0 ? inputs.Bars[^1].Close : null,
            stop,
            null,
            volume.Multiple,
            inputs.Exclusions);
    }

    // The close above the highest high of the sessions before it. A close at that high is not above it.
    static Gate NewHighGate(BreakoutInputs inputs)
    {
        if (inputs.Bars.Count < HighSessions + 1)
        {
            return new Gate(
                NewHigh,
                false,
                Invariant($"not available: {inputs.Bars.Count} sessions are stored, and a close is read against the {HighSessions} before it"),
                Values(("sessions", Whole(inputs.Bars.Count))));
        }

        var close = inputs.Bars[^1].Close;
        var high = inputs.Bars.Skip(inputs.Bars.Count - 1 - HighSessions).Take(HighSessions).Max(bar => bar.High);
        var passed = close > high;

        return new Gate(
            NewHigh,
            passed,
            Invariant($"the close of {close} is {(passed ? "above" : "not above")} the highest high of the {HighSessions} sessions before it, {high}"),
            Values(("close", Price(close)), ("high", Price(high)), ("sessions", Whole(inputs.Bars.Count))));
    }

    // Tonight's volume against its 50-session average, at or above the multiple.
    static (Gate Gate, double? Multiple) VolumeGate(BreakoutInputs inputs)
    {
        if (inputs.Bars.Count == 0 || inputs.VolumeAverage50 is not { } average || average <= 0)
        {
            return (new Gate(Volume, false, "not available: no 50-session average volume is stored for the night", Values()), null);
        }

        var multiple = Statistic.FromVolume(inputs.Bars[^1].Volume) / average;
        var passed = multiple >= VolumeMultiple;

        return (
            new Gate(
                Volume,
                passed,
                Invariant($"volume {multiple:0.00} times its 50-session average, {(passed ? "at or above" : "below")} {VolumeMultiple:0.0#}"),
                Values(("multiple", Figure(multiple)), ("floor", Figure(VolumeMultiple)))),
            multiple);
    }

    // The mean daily range, as a share of the close, of the sessions before tonight against that of the
    // same count before them: narrowed where the newer is no wider than the ceiling's share of the older.
    static Gate TightenedGate(BreakoutInputs inputs)
    {
        var needed = RangeSessions * 2 + 1;

        if (inputs.Bars.Count < needed)
        {
            return new Gate(
                Tightened,
                false,
                Invariant($"not available: {inputs.Bars.Count} sessions are stored, and the ranges are read over the {RangeSessions * 2} before tonight"),
                Values(("sessions", Whole(inputs.Bars.Count))));
        }

        var before = inputs.Bars.Skip(inputs.Bars.Count - needed).Take(RangeSessions * 2).ToArray();

        if (before.Any(bar => bar.Close <= 0))
        {
            return new Gate(Tightened, false, "not available: a session before tonight holds no close above zero", Values());
        }

        var older = before.Take(RangeSessions).Average(bar => (bar.High - bar.Low) / bar.Close);
        var newer = before.Skip(RangeSessions).Average(bar => (bar.High - bar.Low) / bar.Close);

        if (older <= 0)
        {
            return new Gate(Tightened, false, Invariant($"not available: the {RangeSessions} sessions the newer ranges are read against held no range"), Values());
        }

        var ratio = Statistic.FromRatio(newer / older);
        var passed = ratio <= RangeCeiling;

        return new Gate(
            Tightened,
            passed,
            Invariant($"the mean daily range of the {RangeSessions} sessions before tonight is {Statistic.FromRatio(newer) * 100:0.00}% of the close, {(passed ? "no wider than" : "wider than")} the {Statistic.FromRatio(older) * 100:0.00}% of the {RangeSessions} before them"),
            Values(("recent", Figure(Statistic.FromRatio(newer))), ("before", Figure(Statistic.FromRatio(older))), ("ratio", Figure(ratio)), ("ceiling", Figure(RangeCeiling))));
    }

    // The trade: bought at the close, the stop the typical moves beneath it. A night storing no typical
    // move places no stop, so the member has no trade and does not pass.
    static (Gate Gate, decimal? Stop) TradeGate(BreakoutInputs inputs)
    {
        if (inputs.Bars.Count == 0)
        {
            return (new Gate(Trade, false, "no close is stored for the night", Values()), null);
        }

        var close = inputs.Bars[^1].Close;

        if (inputs.TypicalMove is not { } move || move <= 0)
        {
            return (new Gate(Trade, false, "no typical move is stored for the night to place the stop by", Values(("close", Price(close)))), null);
        }

        var stop = close - Statistic.ToPrice(move * StopMoves);

        if (stop <= 0 || stop >= close)
        {
            return (new Gate(Trade, false, Invariant($"{StopMoves:0.#} typical moves of {move:0.00} beneath the close of {close} leave no stop below it and above zero"), Values(("close", Price(close)), ("typical move", Figure(move)))), null);
        }

        return (
            new Gate(
                Trade,
                true,
                Invariant($"bought at the close of {close} with the stop at {stop}, {StopMoves:0.#} typical moves of {move:0.00} beneath, trailing the highest close since"),
                Values(("close", Price(close)), ("stop", Price(stop)), ("typical move", Figure(move)), ("moves", Figure(StopMoves)))),
            stop);
    }
}
