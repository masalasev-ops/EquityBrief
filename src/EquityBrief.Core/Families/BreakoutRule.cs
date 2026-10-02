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

// The settings a breakout is read at: the sessions before tonight's its close is read against, the volume
// multiple, the most the newer ranges may be of the older, and the typical moves the stop sits beneath and
// trails the highest close by.
public sealed record BreakoutSettings(int HighSessions, double VolumeMultiple, double RangeCeiling, double StopMoves);

// The breakout family's rule: a close above the highest high of the sessions before it, on heavy volume, after
// the daily ranges narrowed. Bought at the close with a stop the typical moves beneath, which trails the
// highest close since the buy and is never lowered, and with no target.
//
// The rule reads its settings: the night at the ones its freeze registered, a registered variant at its own.
// see: A breakout is a close above the year's high on heavy volume after its ranges narrowed, sold on a trailing stop with no target
// see: The new families freeze at their sweeps' proposals, the breakout's provisional setting and the drift's wider stop registered beside them as variants
// see: A registered family rule is evaluated every night at its own settings and keeps its own list, its trades stored with their benchmark when they end
public static class BreakoutRule
{
    public const string Name = "breakout";

    // The horizon a breakout's trade is scored under.
    public const string Horizon = "breakout";

    // The sessions before tonight's the close is read against, half a year of them.
    public const int HighSessions = 126;

    public const double VolumeMultiple = 1.5;

    // The sessions the daily range is averaged over, and the most the newer average may be of the older.
    public const int RangeSessions = 20;

    public const double RangeCeiling = 0.85;

    // The typical moves the stop sits beneath the close and trails the highest close by.
    public const double StopMoves = 1.5;

    public const int CapSessions = 63;

    // The provisional setting the freeze replaced, registered beside it as a variant: the 251 sessions before
    // tonight's, which with tonight's are every session the store keeps for a name, ranges no wider than the
    // older, and the stop 2 typical moves beneath.
    public const int ProvisionalHighSessions = 251;

    public const double ProvisionalRangeCeiling = 1.0;

    public const double ProvisionalStopMoves = 2;

    // The settings the night lists at, and the provisional ones the freeze replaced.
    public static BreakoutSettings Live { get; } = new(HighSessions, VolumeMultiple, RangeCeiling, StopMoves);

    public static BreakoutSettings Provisional { get; } = new(ProvisionalHighSessions, VolumeMultiple, ProvisionalRangeCeiling, ProvisionalStopMoves);

    public const string NewHigh = "new high";
    public const string Volume = "volume";
    public const string Tightened = "tightened";

    // The new high gate's value naming the sessions before tonight's the close was read against, which a row
    // stored before the freeze does not carry, every one of those read at the provisional setting.
    public const string WindowValue = "window";

    public static readonly string[] Order = [Market, NewHigh, Volume, Tightened, Trade];

    public static FamilyResult Evaluate(BreakoutInputs inputs) => Evaluate(inputs, Live);

    public static FamilyResult Evaluate(BreakoutInputs inputs, BreakoutSettings settings)
    {
        var (trade, stop) = TradeGate(inputs, settings);
        var volume = VolumeGate(inputs, settings);

        return new FamilyResult(
            inputs.Ticker,
            Name,
            [inputs.Market, NewHighGate(inputs, settings), volume.Gate, TightenedGate(inputs, settings), trade],
            inputs.Bars.Count > 0 ? inputs.Bars[^1].Close : null,
            stop,
            null,
            volume.Multiple,
            inputs.Exclusions);
    }

    // The close above the highest high of the sessions before it. A close at that high is not above it.
    static Gate NewHighGate(BreakoutInputs inputs, BreakoutSettings settings)
    {
        var sessions = settings.HighSessions;

        if (inputs.Bars.Count < sessions + 1)
        {
            return new Gate(
                NewHigh,
                false,
                Invariant($"not available: {inputs.Bars.Count} sessions are stored, and a close is read against the {sessions} before it"),
                Values(("sessions", Whole(inputs.Bars.Count))));
        }

        var close = inputs.Bars[^1].Close;
        var high = inputs.Bars.Skip(inputs.Bars.Count - 1 - sessions).Take(sessions).Max(bar => bar.High);
        var passed = close > high;

        return new Gate(
            NewHigh,
            passed,
            Invariant($"the close of {close} is {(passed ? "above" : "not above")} the highest high of the {sessions} sessions before it, {high}"),
            Values(("close", Price(close)), ("high", Price(high)), ("sessions", Whole(inputs.Bars.Count)), (WindowValue, Whole(sessions))));
    }

    // Tonight's volume against its 50-session average, at or above the multiple.
    static (Gate Gate, double? Multiple) VolumeGate(BreakoutInputs inputs, BreakoutSettings settings)
    {
        if (inputs.Bars.Count == 0 || inputs.VolumeAverage50 is not { } average || average <= 0)
        {
            return (new Gate(Volume, false, "not available: no 50-session average volume is stored for the night", Values()), null);
        }

        var multiple = Statistic.FromVolume(inputs.Bars[^1].Volume) / average;
        var passed = multiple >= settings.VolumeMultiple;

        return (
            new Gate(
                Volume,
                passed,
                Invariant($"volume {multiple:0.00} times its 50-session average, {(passed ? "at or above" : "below")} {settings.VolumeMultiple:0.0#}"),
                Values(("multiple", Figure(multiple)), ("floor", Figure(settings.VolumeMultiple)))),
            multiple);
    }

    // The mean daily range, as a share of the close, of the sessions before tonight against that of the
    // same count before them: narrowed where the newer is no wider than the ceiling's share of the older.
    static Gate TightenedGate(BreakoutInputs inputs, BreakoutSettings settings)
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
        var passed = ratio <= settings.RangeCeiling;

        return new Gate(
            Tightened,
            passed,
            Invariant($"the mean daily range of the {RangeSessions} sessions before tonight is {Statistic.FromRatio(newer) * 100:0.00}% of the close, {(passed ? "no wider than" : "wider than")} {settings.RangeCeiling:0.0#} of the {Statistic.FromRatio(older) * 100:0.00}% of the {RangeSessions} before them"),
            Values(("recent", Figure(Statistic.FromRatio(newer))), ("before", Figure(Statistic.FromRatio(older))), ("ratio", Figure(ratio)), ("ceiling", Figure(settings.RangeCeiling))));
    }

    // The trade: bought at the close, the stop the typical moves beneath it. A night storing no typical
    // move places no stop, so the member has no trade and does not pass.
    static (Gate Gate, decimal? Stop) TradeGate(BreakoutInputs inputs, BreakoutSettings settings)
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

        var stop = close - Statistic.ToPrice(move * settings.StopMoves);

        if (stop <= 0 || stop >= close)
        {
            return (new Gate(Trade, false, Invariant($"{settings.StopMoves:0.#} typical moves of {move:0.00} beneath the close of {close} leave no stop below it and above zero"), Values(("close", Price(close)), ("typical move", Figure(move)))), null);
        }

        return (
            new Gate(
                Trade,
                true,
                Invariant($"bought at the close of {close} with the stop at {stop}, {settings.StopMoves:0.#} typical moves of {move:0.00} beneath, trailing the highest close since"),
                Values(("close", Price(close)), ("stop", Price(stop)), ("typical move", Figure(move)), ("moves", Figure(settings.StopMoves)))),
            stop);
    }
}
