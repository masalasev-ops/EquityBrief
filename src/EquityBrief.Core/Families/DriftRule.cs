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

// The settings an earnings drift is read at: the sessions the reaction may be bought in, the typical moves
// its close has to be up by, its volume multiple, the multiple of the risk the target is placed at where no
// band is nearer, and the fewest typical moves the stop may sit under the buy, none where it is zero.
public sealed record DriftSettings(int WindowSessions, double ReactionMoves, double VolumeMultiple, double TargetRiskMultiple, double StopFloorMoves = 0);

// The earnings drift family's rule: a report that beat its estimate, a reaction session that closed up by its
// typical moves on heavy volume, and a close still above that session's low, bought within a few sessions of
// the reaction. The stop is that session's low, moved down to the stop floor's distance under the buy where
// the settings hold one and the low sits nearer, and the target the nearer of the next band well above the
// close and a fixed multiple of the risk.
//
// It reads the reactions the move annotator stores from the calendar the night already fetches, so it adds
// no request. The rule reads its settings: the night at the ones its freeze registered, a registered variant
// at its own.
// see: The earnings drift buys a beat with a strong reaction within five sessions, stopped under the reaction session's low
// see: The new families freeze at their sweeps' proposals, the breakout's provisional setting and the drift's wider stop registered beside them as variants
// see: A registered family rule is evaluated every night at its own settings and keeps its own list, its trades stored with their benchmark when they end
public static class DriftRule
{
    public const string Name = "drift";

    // The horizon a drift's trade is scored under.
    public const string Horizon = "drift";

    // The sessions the reaction may be bought in: the reaction session itself and the ones after it.
    public const int WindowSessions = 3;

    // The typical moves of the session before it the reaction session's close has to be up by.
    public const double ReactionMoves = 0.5;

    public const double VolumeMultiple = 2.0;

    // The typical moves above the close a band has to sit to be the target, and the multiple of the risk
    // the target is placed at where no band is nearer.
    public const double TargetBandMoves = 2;

    public const double TargetRiskMultiple = 2.5;

    public const int CapSessions = 60;

    // The provisional setting the freeze replaced: five sessions, one typical move up, 1.5 times the volume.
    public const int ProvisionalWindowSessions = 5;

    public const double ProvisionalReactionMoves = 1.0;

    public const double ProvisionalVolumeMultiple = 1.5;

    // The stop floor of the variant the operator added at the freeze: no closer than one typical move.
    public const double VariantStopFloorMoves = 1;

    // The settings the night lists at, and the provisional ones the freeze replaced.
    public static DriftSettings Live { get; } = new(WindowSessions, ReactionMoves, VolumeMultiple, TargetRiskMultiple);

    public static DriftSettings Provisional { get; } = new(ProvisionalWindowSessions, ProvisionalReactionMoves, ProvisionalVolumeMultiple, TargetRiskMultiple);

    public const string Print = "print";
    public const string Beat = "beat";
    public const string Reaction = "reaction";
    public const string Volume = "volume";
    public const string Held = "held";

    // The trade gate's values naming what the target was read from: the band, or the multiple of the risk.
    public const string TargetFromValue = "target from";
    public const string FromBand = "band";
    public const string FromRisk = "risk";

    // The trade gate's value naming where the stop of a rule holding a stop floor was placed: at the
    // reaction session's low, or moved down to the floor.
    public const string StopFromValue = "stop from";
    public const string FromLow = "low";
    public const string FromFloor = "floor";

    public static readonly string[] Order = [Market, Print, Beat, Reaction, Volume, Held, Trade];

    public static FamilyResult Evaluate(DriftInputs inputs) => Evaluate(inputs, Live);

    public static FamilyResult Evaluate(DriftInputs inputs, DriftSettings settings)
    {
        var (print, at) = PrintGate(inputs, settings);
        var (trade, stop, target) = TradeGate(inputs, at, settings);

        return new FamilyResult(
            inputs.Ticker,
            Name,
            [inputs.Market, print, BeatGate(inputs, at), ReactionGate(inputs, at, settings), VolumeGate(inputs, at, settings), HeldGate(inputs, at), trade],
            inputs.Bars.Count > 0 ? inputs.Bars[^1].Close : null,
            stop,
            target,
            at is null ? null : inputs.Print?.SurprisePct,
            inputs.Exclusions);
    }

    // A print whose reaction session is tonight's or one of the sessions before inside the window, counted
    // over the member's own sessions. The answer carries where that session sits among the bars, which is
    // what every later gate reads, and none where no print is inside the window.
    static (Gate Gate, int? At) PrintGate(DriftInputs inputs, DriftSettings settings)
    {
        var window = settings.WindowSessions;

        if (inputs.Print is not { } print)
        {
            return (new Gate(Print, false, RuleWording.Drift.NoReaction, Values()), null);
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
            return (new Gate(Print, false, RuleWording.Drift.BarsShort(print.ReactionSession), values), null);
        }

        var back = inputs.Bars.Count - 1 - at;
        var inside = back < window;

        return (
            new Gate(
                Print,
                inside,
                RuleWording.Drift.Reacted(print.ReportDate, print.ReactionSession, back, inside, window),
                Values(("report", Day(print.ReportDate)), ("session", Day(print.ReactionSession)), ("back", Whole(back)), ("window", Whole(window)))),
            inside ? at : null);
    }

    static Gate NoPrint(string name) => new(name, false, RuleWording.Drift.NoPrint, Values());

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
            return new Gate(Beat, false, RuleWording.Drift.NoActual(print.ReportDate), Values());
        }

        var passed = surprise > 0;

        return new Gate(
            Beat,
            passed,
            RuleWording.Drift.Beat(print.ReportDate, passed, surprise),
            Values(("surprise", Figure(surprise))));
    }

    // The reaction session's close against the close before it, in typical moves of the session before.
    static Gate ReactionGate(DriftInputs inputs, int? at, DriftSettings settings)
    {
        if (at is not { } index)
        {
            return NoPrint(Reaction);
        }

        if (inputs.TypicalMoveBefore is not { } move || move <= 0)
        {
            return new Gate(Reaction, false, RuleWording.Drift.NoMoveBefore, Values());
        }

        var rise = inputs.Bars[index].Close - inputs.Bars[index - 1].Close;
        var moves = Statistic.FromPrice(rise) / move;
        var passed = moves >= settings.ReactionMoves;

        return new Gate(
            Reaction,
            passed,
            RuleWording.Drift.Reaction(rise >= 0, Math.Abs(moves), passed, settings.ReactionMoves),
            Values(("moves", Figure(moves)), ("floor", Figure(settings.ReactionMoves)), ("typical move", Figure(move))));
    }

    // The reaction session's volume against its 50-session average on that session.
    static Gate VolumeGate(DriftInputs inputs, int? at, DriftSettings settings)
    {
        if (at is not { } index)
        {
            return NoPrint(Volume);
        }

        if (inputs.VolumeAverageAtReaction is not { } average || average <= 0)
        {
            return new Gate(Volume, false, RuleWording.Drift.NoVolumeAverage, Values());
        }

        var multiple = Statistic.FromVolume(inputs.Bars[index].Volume) / average;
        var passed = multiple >= settings.VolumeMultiple;

        return new Gate(
            Volume,
            passed,
            RuleWording.Drift.Volume(multiple, passed, settings.VolumeMultiple),
            Values(("multiple", Figure(multiple)), ("floor", Figure(settings.VolumeMultiple))));
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
            RuleWording.Drift.Held(close, passed, low),
            Values(("close", Price(close)), ("low", Price(low))));
    }

    // The trade: bought at the close, stopped at the reaction session's low, moved down to the stop floor's
    // typical moves under the close where the settings hold a floor and the low sits nearer than it, and
    // aimed at the nearer of the lowest band the stated typical moves or more above the close and the
    // stated multiple of the risk.
    static (Gate Gate, decimal? Stop, decimal? Target) TradeGate(DriftInputs inputs, int? at, DriftSettings settings)
    {
        if (at is not { } index)
        {
            return (NoPrint(Trade), null, null);
        }

        var close = inputs.Bars[^1].Close;
        var low = inputs.Bars[index].Low;
        var stop = low;
        var floored = false;

        if (settings.StopFloorMoves > 0)
        {
            if (inputs.TypicalMove is not { } night || night <= 0)
            {
                return (new Gate(Trade, false, RuleWording.Drift.NoTypicalMove, Values(("close", Price(close)))), null, null);
            }

            var floor = close - Statistic.ToPrice(night * settings.StopFloorMoves);

            if (floor < low)
            {
                stop = floor;
                floored = true;
            }
        }

        if (stop <= 0 || stop >= close)
        {
            return (new Gate(Trade, false, RuleWording.Drift.NoStop(low, close), Values(("close", Price(close)))), null, null);
        }

        var risk = close - stop;
        var byRisk = close + Statistic.ToPrice(Statistic.FromPrice(risk) * settings.TargetRiskMultiple);

        decimal[] farEnough = inputs.TypicalMove is { } move && move > 0
            ? [.. inputs.BandLowEdges.Where(edge => edge > close && Statistic.FromPrice(edge - close) / move >= TargetBandMoves)]
            : [];

        var fromBand = farEnough.Length > 0 && farEnough.Min() < byRisk;
        var target = fromBand ? farEnough.Min() : byRisk;
        var rewardToRisk = Math.Round((target - close) / risk, PriceForm.Places, MidpointRounding.AwayFromZero);

        var values = new List<(string Key, string Value)>
        {
            ("close", Price(close)),
            ("stop", Price(stop)),
            ("target", Price(target)),
            (TargetFromValue, fromBand ? FromBand : FromRisk),
            (RewardToRiskValue, rewardToRisk.ToString(CultureInfo.InvariantCulture)),
        };

        if (settings.StopFloorMoves > 0)
        {
            values.Add((StopFromValue, floored ? FromFloor : FromLow));
        }

        return (
            new Gate(
                Trade,
                true,
                (floored
                    ? RuleWording.Drift.BoughtOnTheFloor(close, stop, settings.StopFloorMoves, low, target)
                    : RuleWording.Drift.BoughtOnTheLow(close, stop, target))
                    + (fromBand
                        ? RuleWording.Drift.TargetFromBand(TargetBandMoves)
                        : RuleWording.Drift.TargetFromRisk(settings.TargetRiskMultiple))
                    + RuleWording.Drift.RewardToRisk(rewardToRisk),
                Values([.. values])),
            stop,
            target);
    }

    static string Day(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
