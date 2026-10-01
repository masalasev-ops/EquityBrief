using System.Globalization;
using EquityBrief.Core.Filter;
using EquityBrief.Core.Prices;
using static EquityBrief.Core.Families.FamilyRule;

namespace EquityBrief.Core.Families;

// A member's newest print whose reaction session is the night's or an earlier one, as the move annotator
// stored it: the day it was reported, the session the earnings rule took for it, whether the calendar
// carries its actual, and the provider's surprise in per cent.
public sealed record DriftPrint(DateOnly ReportDate, DateOnly ReactionSession, bool ActualFiled, double? SurprisePct);

// What the earnings drift's rule reads for one member on one night: the market check the night stored, the
// member's sessions oldest first and ending tonight, its newest print, the typical move stored for the
// session before the reaction's and the average volume stored for the reaction's, tonight's typical move
// and the low edges of tonight's bands, and the exclusions its series carries.
public sealed record DriftInputs(
    string Ticker,
    Gate Market,
    IReadOnlyList<FamilyBar> Bars,
    DriftPrint? Print,
    double? TypicalMoveBefore,
    double? VolumeAverageAtReaction,
    double? TypicalMove,
    IReadOnlyList<decimal> BandLowEdges,
    IReadOnlyList<string> Exclusions);

// The earnings drift family's rule: a report that beat its estimate, a reaction session that closed up a
// typical move or more on heavy volume, and a close still above that session's low, bought within a few
// sessions of the reaction. The stop is that session's low, and the target the nearer of the next band
// well above the close and a fixed multiple of the risk.
//
// It reads the reactions the move annotator stores from the calendar the night already fetches, so it adds
// no request. Every setting here is provisional until the family's sweep proposes the values its freeze
// registers.
// see: The earnings drift buys a beat with a strong reaction within five sessions, stopped under the reaction session's low
// see: A family runs on provisional settings until its freeze, and nothing before the freeze counts toward a checkpoint
public static class DriftRule
{
    public const string Name = "drift";

    // The horizon a drift's trade is scored under.
    public const string Horizon = "drift";

    // The sessions the reaction may be bought in: the reaction session itself and the ones after it.
    public const int WindowSessions = 5;

    // The typical moves of the session before it the reaction session's close has to be up by.
    public const double ReactionMoves = 1.0;

    public const double VolumeMultiple = 1.5;

    // The typical moves above the close a band has to sit to be the target, and the multiple of the risk
    // the target is placed at where no band is nearer.
    public const double TargetBandMoves = 2;

    public const double TargetRiskMultiple = 2.5;

    public const int CapSessions = 60;

    public const string Print = "print";
    public const string Beat = "beat";
    public const string Reaction = "reaction";
    public const string Volume = "volume";
    public const string Held = "held";

    // The trade gate's values naming what the target was read from: the band, or the multiple of the risk.
    public const string TargetFromValue = "target from";
    public const string FromBand = "band";
    public const string FromRisk = "risk";

    public static readonly string[] Order = [Market, Print, Beat, Reaction, Volume, Held, Trade];

    public static FamilyResult Evaluate(DriftInputs inputs)
    {
        var (print, at) = PrintGate(inputs);
        var (trade, stop, target) = TradeGate(inputs, at);

        return new FamilyResult(
            inputs.Ticker,
            Name,
            [inputs.Market, print, BeatGate(inputs, at), ReactionGate(inputs, at), VolumeGate(inputs, at), HeldGate(inputs, at), trade],
            inputs.Bars.Count > 0 ? inputs.Bars[^1].Close : null,
            stop,
            target,
            at is null ? null : inputs.Print?.SurprisePct,
            inputs.Exclusions);
    }

    // A print whose reaction session is tonight's or one of the sessions before inside the window, counted
    // over the member's own sessions. The answer carries where that session sits among the bars, which is
    // what every later gate reads, and none where no print is inside the window.
    static (Gate Gate, int? At) PrintGate(DriftInputs inputs)
    {
        if (inputs.Print is not { } print)
        {
            return (new Gate(Print, false, "no print's reaction is stored for the name", Values()), null);
        }

        var at = -1;

        for (var index = inputs.Bars.Count - 1; index >= 0; index--)
        {
            if (inputs.Bars[index].Session == print.ReactionSession)
            {
                at = index;

                break;
            }
        }

        var values = Values(("report", Day(print.ReportDate)), ("session", Day(print.ReactionSession)));

        if (at < 1)
        {
            return (new Gate(Print, false, Invariant($"not available: the stored bars do not reach the reaction session of {Day(print.ReactionSession)} and the close before it"), values), null);
        }

        var back = inputs.Bars.Count - 1 - at;
        var inside = back < WindowSessions;

        return (
            new Gate(
                Print,
                inside,
                inside
                    ? Invariant($"the print of {Day(print.ReportDate)} reacted on {Day(print.ReactionSession)}, {back} session(s) before tonight, inside the {WindowSessions}-session window")
                    : Invariant($"the print of {Day(print.ReportDate)} reacted on {Day(print.ReactionSession)}, {back} session(s) before tonight, outside the {WindowSessions}-session window"),
                Values(("report", Day(print.ReportDate)), ("session", Day(print.ReactionSession)), ("back", Whole(back)), ("window", Whole(WindowSessions)))),
            inside ? at : null);
    }

    static Gate NoPrint(string name) => new(name, false, "no print inside the window to read", Values());

    // The provider's surprise above zero. A print whose actual the calendar does not carry yet has no
    // surprise to read, and it does not pass until one is filed.
    static Gate BeatGate(DriftInputs inputs, int? at)
    {
        if (at is null || inputs.Print is not { } print)
        {
            return NoPrint(Beat);
        }

        if (!print.ActualFiled || print.SurprisePct is not { } surprise)
        {
            return new Gate(Beat, false, Invariant($"the calendar carries no actual for the print of {Day(print.ReportDate)} yet, so no surprise is read"), Values());
        }

        var passed = surprise > 0;

        return new Gate(
            Beat,
            passed,
            passed
                ? Invariant($"the print of {Day(print.ReportDate)} beat its estimate by {surprise:0.0#}%")
                : Invariant($"the print of {Day(print.ReportDate)} did not beat its estimate: a surprise of {surprise:0.0#}%"),
            Values(("surprise", Figure(surprise))));
    }

    // The reaction session's close against the close before it, in typical moves of the session before.
    static Gate ReactionGate(DriftInputs inputs, int? at)
    {
        if (at is not { } index)
        {
            return NoPrint(Reaction);
        }

        if (inputs.TypicalMoveBefore is not { } move || move <= 0)
        {
            return new Gate(Reaction, false, "not available: no typical move is stored for the session before the reaction's", Values());
        }

        var rise = inputs.Bars[index].Close - inputs.Bars[index - 1].Close;
        var moves = Statistic.FromPrice(rise) / move;
        var passed = moves >= ReactionMoves;

        return new Gate(
            Reaction,
            passed,
            Invariant($"the reaction session closed {(rise >= 0 ? "up" : "down")} {Math.Abs(moves):0.00} typical moves, {(passed ? "at or above" : "below")} {ReactionMoves:0.0#} up"),
            Values(("moves", Figure(moves)), ("floor", Figure(ReactionMoves)), ("typical move", Figure(move))));
    }

    // The reaction session's volume against its 50-session average on that session.
    static Gate VolumeGate(DriftInputs inputs, int? at)
    {
        if (at is not { } index)
        {
            return NoPrint(Volume);
        }

        if (inputs.VolumeAverageAtReaction is not { } average || average <= 0)
        {
            return new Gate(Volume, false, "not available: no 50-session average volume is stored for the reaction session", Values());
        }

        var multiple = Statistic.FromVolume(inputs.Bars[index].Volume) / average;
        var passed = multiple >= VolumeMultiple;

        return new Gate(
            Volume,
            passed,
            Invariant($"the reaction session's volume was {multiple:0.00} times its 50-session average, {(passed ? "at or above" : "below")} {VolumeMultiple:0.0#}"),
            Values(("multiple", Figure(multiple)), ("floor", Figure(VolumeMultiple))));
    }

    // Tonight's close above the reaction session's low: a reaction given back to its low is not drifting.
    static Gate HeldGate(DriftInputs inputs, int? at)
    {
        if (at is not { } index)
        {
            return NoPrint(Held);
        }

        var close = inputs.Bars[^1].Close;
        var low = inputs.Bars[index].Low;
        var passed = close > low;

        return new Gate(
            Held,
            passed,
            Invariant($"the close of {close} is {(passed ? "above" : "not above")} the reaction session's low of {low}"),
            Values(("close", Price(close)), ("low", Price(low))));
    }

    // The trade: bought at the close, stopped at the reaction session's low, and aimed at the nearer of the
    // lowest band the stated typical moves or more above the close and the stated multiple of the risk.
    static (Gate Gate, decimal? Stop, decimal? Target) TradeGate(DriftInputs inputs, int? at)
    {
        if (at is not { } index)
        {
            return (NoPrint(Trade), null, null);
        }

        var close = inputs.Bars[^1].Close;
        var stop = inputs.Bars[index].Low;

        if (stop <= 0 || stop >= close)
        {
            return (new Gate(Trade, false, Invariant($"the reaction session's low of {stop} leaves no stop below the close of {close} and above zero"), Values(("close", Price(close)))), null, null);
        }

        var risk = close - stop;
        var byRisk = close + Statistic.ToPrice(Statistic.FromPrice(risk) * TargetRiskMultiple);

        decimal[] farEnough = inputs.TypicalMove is { } move && move > 0
            ? [.. inputs.BandLowEdges.Where(edge => edge > close && Statistic.FromPrice(edge - close) / move >= TargetBandMoves)]
            : [];

        var fromBand = farEnough.Length > 0 && farEnough.Min() < byRisk;
        var target = fromBand ? farEnough.Min() : byRisk;
        var rewardToRisk = Math.Round((target - close) / risk, PriceForm.Places, MidpointRounding.AwayFromZero);

        return (
            new Gate(
                Trade,
                true,
                Invariant($"bought at the close of {close} with the stop at the reaction session's low, {stop}, and the target at {target}, ")
                    + (fromBand
                        ? Invariant($"the lowest band {TargetBandMoves:0.#} typical moves or more above the close")
                        : Invariant($"{TargetRiskMultiple:0.0#} times the risk above the close"))
                    + Invariant($", a reward to risk of {rewardToRisk:0.00}"),
                Values(
                    ("close", Price(close)),
                    ("stop", Price(stop)),
                    ("target", Price(target)),
                    (TargetFromValue, fromBand ? FromBand : FromRisk),
                    (RewardToRiskValue, rewardToRisk.ToString(CultureInfo.InvariantCulture)))),
            stop,
            target);
    }

    static string Day(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
