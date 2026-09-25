using System.Globalization;
using EquityBrief.Core.Prices;

namespace EquityBrief.Core.Filter;

// One of tonight's bands as the gates read it: both edges, its role against tonight's close, its
// strength, and whether any member of it is not a moving average.
public sealed record FilterBand(decimal LowEdge, decimal HighEdge, string Role, int Strength, bool HasNonAverageAnchor);

// The ladder's first tranche as the night's listing kept it: the entry the arithmetic takes, its stop,
// the first traded target, their reward to risk, and the arithmetic's own words where it computes none.
public sealed record FirstTranche(decimal? Entry, decimal? Stop, decimal? Target, decimal? RewardToRisk, string? Absent);

// A session before tonight and whether the trigger's event happened on it, as that session's stored
// result says; null where no result is stored for it.
public readonly record struct SessionEvent(DateOnly Session, bool? Fired);

// Everything one member's gates are evaluated against on one night, already read. Nullable throughout,
// because every member is evaluated and a gate reading an absent value fails and says why. The sessions
// before the session before, newest first, carry the trigger's events the arrival window reads.
public sealed record GateInputs(
    string Ticker,
    Breadth? Breadth,
    string? TrendState,
    double? Strength,
    SwingReading? Reading,
    string? ReadingNote,
    decimal? Close,
    decimal? PreviousClose,
    decimal? PreviousHigh,
    long? Volume,
    double? VolumeAverage50,
    double? TypicalMove,
    IReadOnlyList<FilterBand> Bands,
    FirstTranche? Ladder,
    DateOnly? NextEarnings,
    int? SessionsToEarnings,
    bool Suspect,
    DateOnly? Gap,
    DateOnly? SessionBefore,
    bool? TriggerFiredTheSessionBefore,
    IReadOnlyList<SessionEvent>? Earlier = null);

// One gate's answer: whether it passed, the sentence saying why, and the values that decided it.
public sealed record Gate(string Name, bool Passed, string Reason, IReadOnlyDictionary<string, string> Values);

// One reading of the trade: the entry, the stop and the target it is read from, their reward to risk,
// how far the stop sits below the entry in typical daily moves, and why it is absent where it is.
public sealed record TradeReading(decimal? Entry, decimal? Stop, decimal? Target, decimal? RewardToRisk, double? StopInMoves, string? Absent);

// One member's evaluation on one night: the five gates in order, the setup family that passed, whether
// the pullback's trigger event happened tonight, the trade read both ways, the exclusions that apply
// and the notes on the ones that could not be read, and whether it passes the whole filter.
public sealed record GateResult(
    string Ticker,
    IReadOnlyList<Gate> Gates,
    string? Family,
    bool? TriggerEvent,
    TradeReading LadderTrade,
    TradeReading SwingTrade,
    IReadOnlyList<string> Exclusions,
    IReadOnlyList<string> Notes,
    double? Strength,
    int? BandStrength)
{
    public bool Passed => Gates.All(gate => gate.Passed) && Exclusions.Count == 0;
}

// The swing filter's five gates, its trigger, its exclusions and its order, as pure functions of one
// member's inputs and the settings, so the component reads and writes and this decides, and the count
// verb reads the same answers as the night.
// see: Code owns every number
public static class SwingGates
{
    public const string Market = "market";
    public const string Trend = "trend and strength";
    public const string Setup = "setup";
    public const string Trigger = "trigger";
    public const string Trade = "trade";

    // Section 11's five, in the order a name meets them, which is the order the funnel counts.
    public static readonly string[] Order = [Market, Trend, Setup, Trigger, Trade];

    public const string Pullback = "pullback";
    public const string Breakout = "breakout";

    public const string EarningsExclusion = "earnings inside the holding window";
    public const string SuspectExclusion = "suspect series";
    public const string GapExclusion = "gap";

    public const string Uptrend = "uptrend";

    // The setup's values naming whether the close sat inside an anchored support band and whether it
    // cleared a band that sat at or above the previous close, which the shape proposer recounts from:
    // neither is a threshold, so a count under another setting keeps them as the night found them.
    public const string PullbackBandValue = "inside an anchored support band";

    public const string BreakoutBandValue = "cleared a band above the previous close";

    // The trigger's value naming the session its event arrived on inside the window, or none, which the
    // shape proposer recounts from: no threshold moves it.
    public const string ArrivedValue = "arrived";
    public const string SupportRole = "support";

    public static GateResult Evaluate(GateInputs inputs, FilterSettings settings)
    {
        var pullbackBand = PullbackBand(inputs);
        var breakoutBand = BreakoutBand(inputs);

        var market = MarketGate(inputs, settings);
        var trend = TrendGate(inputs, settings);
        var (setup, family) = SetupGate(inputs, settings, pullbackBand, breakoutBand);
        var (trigger, triggerEvent) = TriggerGate(inputs, settings, family, pullbackBand, breakoutBand);

        var setupBand = family == Breakout ? breakoutBand : pullbackBand ?? breakoutBand;
        var ladder = LadderTrade(inputs);
        var swing = SwingTrade(inputs, setupBand);
        var trade = TradeGate(settings, settings.Trade == TradeInput.Ladder ? ladder : swing);

        var (exclusions, notes) = Excluded(inputs, settings);

        return new GateResult(
            inputs.Ticker,
            [market, trend, setup, trigger, trade],
            family,
            triggerEvent,
            ladder,
            swing,
            exclusions,
            notes,
            inputs.Strength,
            setupBand?.Strength);
    }

    // The market: breadth at or above its floor. A night whose breadth is not available closes the gate
    // for every member and says how many held it of how many.
    static Gate MarketGate(GateInputs inputs, FilterSettings settings)
    {
        if (inputs.Breadth is not { Share: { } share } breadth)
        {
            return new Gate(
                Market,
                false,
                inputs.Breadth is { } held
                    ? Invariant($"breadth is not available: {held.Counted} of the {held.Members} members hold a close and a 200-day average, fewer than half")
                    : "no market reading is stored for the night",
                Values());
        }

        var passed = share >= settings.BreadthFloor;

        return new Gate(
            Market,
            passed,
            Invariant($"breadth {share * 100:0.0}% {(passed ? "at or above" : "below")} its floor of {settings.BreadthFloor * 100:0.#}%"),
            Values(("breadth", Figure(share)), ("floor", Figure(settings.BreadthFloor)), ("counted", Whole(breadth.Counted))));
    }

    // The name's trend and its strength: the classifier's uptrend, and the mean of its two places among
    // the members' returns at or above its floor.
    static Gate TrendGate(GateInputs inputs, FilterSettings settings)
    {
        var values = Values(("trend state", inputs.TrendState ?? "none"), ("strength", Figure(inputs.Strength)), ("floor", Figure(settings.StrengthFloor)));

        if (inputs.TrendState is null)
        {
            return new Gate(Trend, false, "no trend state is stored for the night", values);
        }

        if (inputs.Strength is not { } strength)
        {
            return new Gate(
                Trend,
                false,
                inputs.ReadingNote ?? Invariant($"no place among the members' returns: {inputs.Reading?.Bars ?? 0} bars, too few for a return over both spans"),
                values);
        }

        var up = inputs.TrendState == Uptrend;
        var strong = strength >= settings.StrengthFloor;

        return new Gate(
            Trend,
            up && strong,
            Invariant($"{(up ? "an uptrend" : inputs.TrendState.Replace('_', ' ') + ", not an uptrend")}; strength {strength:0.000} {(strong ? "at or above" : "below")} its floor of {settings.StrengthFloor:0.000}"),
            values);
    }

    // The band tonight's close is inside, where the band is support and has a member that is not a moving
    // average; the highest of them where more than one holds the close.
    static FilterBand? PullbackBand(GateInputs inputs) =>
        inputs.Close is { } close
            ? inputs.Bands
                .Where(band => band.Role == SupportRole && band.HasNonAverageAnchor && band.LowEdge < close && close <= band.HighEdge)
                .OrderByDescending(band => band.LowEdge)
                .FirstOrDefault()
            : null;

    // The band tonight's close cleared that sat at or above last night's close, the breakout reason's own
    // test, the highest of them where the close cleared more than one.
    // see: Breakout on volume reads resistance at the previous session's close
    static FilterBand? BreakoutBand(GateInputs inputs) =>
        inputs.Close is { } close && inputs.PreviousClose is { } before
            ? inputs.Bands
                .Where(band => band.LowEdge >= before && close > band.HighEdge)
                .OrderByDescending(band => band.HighEdge)
                .FirstOrDefault()
            : null;

    // The setup: a pullback into an anchored support band from a recent high, on volume that dried up
    // while it came down; or a tight base breaking out through a band on heavy volume. The pullback is
    // read first and names the family where both pass.
    static (Gate Gate, string? Family) SetupGate(GateInputs inputs, FilterSettings settings, FilterBand? pullbackBand, FilterBand? breakoutBand)
    {
        var reading = inputs.Reading;

        if (reading is null)
        {
            return (new Gate(Setup, false, inputs.ReadingNote ?? "no swing readings are stored for the night", Values()), null);
        }

        var depthIn = reading.Depth is { } depth && depth >= settings.DepthLow && depth <= settings.DepthHigh;
        var dry = reading.DryUp is { } dryUp && dryUp < settings.DryUpCeiling;
        var pullback = depthIn && dry && pullbackBand is not null;

        var tight = reading.Tightness is { } tightness && tightness < settings.TightnessCeiling;
        var heavy = inputs.Volume is { } volume && inputs.VolumeAverage50 is > 0 and var average
            && Statistic.FromVolume(volume) >= settings.BreakoutVolumeMultiple * average;
        var breakout = tight && heavy && breakoutBand is not null;

        var family = pullback ? Pullback : breakout ? Breakout : null;

        var parts = new List<string>
        {
            pullback
                ? "a pullback into an anchored support band"
                : "no pullback: " + string.Join(", ", Missing(
                    (depthIn, reading.Depth is { } d ? Invariant($"depth {d:0.00} outside {settings.DepthLow:0.##} to {settings.DepthHigh:0.##} typical moves") : "no depth"),
                    (dry, reading.DryUp is { } v ? Invariant($"dry-up {v:0.00} not below {settings.DryUpCeiling:0.##}") : reading.PullbackSessions == 0 ? "the high was made on the night" : "no dry-up"),
                    (pullbackBand is not null, "the close is inside no anchored support band"))),
            breakout
                ? "a tight base breaking out"
                : "no breakout: " + string.Join(", ", Missing(
                    (tight, reading.Tightness is { } t ? Invariant($"tightness {t:0.00} not below {settings.TightnessCeiling:0.##}") : "no tightness"),
                    (breakoutBand is not null, "the close cleared no band that sat at or above the previous close"),
                    (heavy, inputs.Volume is null || inputs.VolumeAverage50 is not > 0 ? "no volume against its average" : Invariant($"volume below {settings.BreakoutVolumeMultiple:0.##} times its fifty-day average")))),
        };

        return (
            new Gate(
                Setup,
                family is not null,
                string.Join("; ", parts),
                Values(
                    ("family", family ?? "none"),
                    ("depth", Figure(reading.Depth)),
                    ("dry-up", Figure(reading.DryUp)),
                    ("tightness", Figure(reading.Tightness)),
                    ("volume multiple", inputs.Volume is { } v2 && inputs.VolumeAverage50 is > 0 and var a2 ? Figure(Statistic.FromVolume(v2) / a2) : "none"),
                    ("band low", Price(pullbackBand?.LowEdge ?? breakoutBand?.LowEdge)),
                    ("band high", Price(pullbackBand?.HighEdge ?? breakoutBand?.HighEdge)),
                    (PullbackBandValue, pullbackBand is null ? "no" : "yes"),
                    (BreakoutBandValue, breakoutBand is null ? "no" : "yes"))),
            family);
    }

    // The pullback's trigger event: tonight's close above the previous session's high, or back inside
    // or above the band after a session closing below its low edge. Null where tonight's bars cannot say.
    static bool? PullbackEvent(GateInputs inputs, FilterBand? band)
    {
        if (inputs.Close is not { } close || inputs.PreviousHigh is not { } previousHigh)
        {
            return null;
        }

        return close > previousHigh
            || (band is not null && inputs.PreviousClose is { } before && before < band.LowEdge && close >= band.LowEdge);
    }

    // The trigger's arrival inside the window: the newest session back from tonight, tonight being 0, on
    // which its event happened where it had not on the session before it, or none. Where none is found
    // and the answer turned on a session whose event cannot be read, the first such session back. The
    // events before tonight are newest first, the session before at 0.
    // see: Arrival is a trigger that first fired within the last three sessions, and the trade is read from tonight's close
    public static (int? At, int? Unread) Arrival(bool? tonight, IReadOnlyList<bool?> before, int window)
    {
        bool? Fired(int back) => back == 0 ? tonight : back - 1 < before.Count ? before[back - 1] : null;

        int? unread = null;

        for (var back = 0; back < window; back++)
        {
            var on = Fired(back);
            var prior = Fired(back + 1);

            if (on == true && prior == false)
            {
                return (back, null);
            }

            if (on is null)
            {
                unread ??= back;
            }
            else if (on == true && prior is null)
            {
                unread ??= back + 1;
            }
        }

        return (null, unread);
    }

    // The trigger: for a breakout, the first close above the band, which the band test already is; for a
    // pullback, and for a name with no setup so its near misses can be read, the event's arrival inside
    // the window, read off the stored results of the sessions before. A session with no stored result
    // the answer turns on cannot say, and the gate fails rather than passing on the absence.
    // see: Arrival is a trigger that first fired within the last three sessions, and the trade is read from tonight's close
    static (Gate Gate, bool? Event) TriggerGate(GateInputs inputs, FilterSettings settings, string? family, FilterBand? pullbackBand, FilterBand? breakoutBand)
    {
        var tonight = PullbackEvent(inputs, pullbackBand);
        var window = settings.ArrivalSessions;

        List<DateOnly?> sessions = [inputs.SessionBefore, .. (inputs.Earlier ?? []).Select(earlier => (DateOnly?)earlier.Session)];
        List<bool?> fired = [inputs.TriggerFiredTheSessionBefore, .. (inputs.Earlier ?? []).Select(earlier => earlier.Fired)];

        string On(int back) =>
            back == 0 ? "tonight"
            : back - 1 < sessions.Count && sessions[back - 1] is { } day ? day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : back == 1 ? "the session before"
            : Invariant($"the session {back} before tonight");

        var (at, unread) = Arrival(tonight, fired, window);
        var values = Values(
            ("event tonight", tonight is { } happened ? (happened ? "yes" : "no") : "none"),
            ("event the session before", inputs.TriggerFiredTheSessionBefore is { } earlier ? (earlier ? "yes" : "no") : "none"),
            ("previous high", Price(inputs.PreviousHigh)),
            ("arrival window", Whole(window)),
            (ArrivedValue, at is { } back ? On(back) : "none"));

        if (family == Breakout)
        {
            return (new Gate(Trigger, true, "the first close above the band it cleared", values), tonight);
        }

        if (at is 0)
        {
            return (new Gate(Trigger, true, $"the trigger fired tonight and not on {On(1)}", values), tonight);
        }

        if (at is { } arrived)
        {
            return (new Gate(Trigger, true, Invariant($"the trigger first fired on {On(arrived)}, {arrived} session(s) before tonight, inside the {window}-session window"), values), tonight);
        }

        if (unread is 0)
        {
            return (new Gate(Trigger, false, "no previous session's high to read the trigger against", values), null);
        }

        if (unread is { } missing)
        {
            return (new Gate(Trigger, false, $"no gate result is stored for {On(missing)}, so the trigger's arrival cannot be read", values), tonight);
        }

        if (window == 1)
        {
            return tonight == true
                ? (new Gate(Trigger, false, $"the trigger fired on {On(1)} too, so tonight is not its arrival", values), true)
                : (new Gate(Trigger, false, "the close is not above the previous session's high and did not come back into the band", values), tonight);
        }

        return tonight == true
            ? (new Gate(Trigger, false, Invariant($"the trigger fired on every session back to {On(window)}, so it did not arrive in the last {window} sessions"), values), true)
            : (new Gate(Trigger, false, Invariant($"the trigger did not arrive in the last {window} sessions"), values), tonight);
    }

    // The ladder's first tranche, as the listing kept it, with the stop's distance in typical moves.
    static TradeReading LadderTrade(GateInputs inputs)
    {
        if (inputs.Ladder is not { } plan)
        {
            return new TradeReading(null, null, null, null, null, "no plan is stored for the night");
        }

        return new TradeReading(
            plan.Entry,
            plan.Stop,
            plan.Target,
            plan.RewardToRisk,
            Moves(plan.Entry, plan.Stop, inputs.TypicalMove),
            plan.RewardToRisk is null ? plan.Absent ?? "the plan's arithmetic computes no reward to risk" : null);
    }

    // The swing trade's own plan: the entry at tonight's close, the stop at the setup band's low edge,
    // and the target at the lowest low edge of a band above the close.
    static TradeReading SwingTrade(GateInputs inputs, FilterBand? setupBand)
    {
        if (inputs.Close is not { } close)
        {
            return new TradeReading(null, null, null, null, null, "no close is stored for the night");
        }

        if (setupBand is null)
        {
            return new TradeReading(close, null, null, null, null, "no setup band to set a stop at");
        }

        var stop = setupBand.LowEdge;
        var bandsAbove = inputs.Bands.Where(band => band.LowEdge > close).ToArray();

        if (bandsAbove.Length == 0)
        {
            return new TradeReading(close, stop, null, null, Moves(close, stop, inputs.TypicalMove), "no band above the close to set a target at");
        }

        var above = bandsAbove.Min(band => band.LowEdge);

        if (close <= stop)
        {
            return new TradeReading(close, stop, above, null, null, "the stop is not below the entry");
        }

        return new TradeReading(
            close,
            stop,
            above,
            Math.Round((above - close) / (close - stop), PriceForm.Places, MidpointRounding.AwayFromZero),
            Moves(close, stop, inputs.TypicalMove),
            null);
    }

    static double? Moves(decimal? entry, decimal? stop, double? typicalMove) =>
        entry is { } from && stop is { } to && typicalMove is > 0 and var move ? Statistic.FromPrice(from - to) / move : null;

    // The trade: its reward to risk at or above its floor, and its stop between the two distances below
    // the entry, read off the plan the settings name.
    static Gate TradeGate(FilterSettings settings, TradeReading trade)
    {
        var values = Values(
            ("input", settings.Trade == TradeInput.Ladder ? "ladder" : "swing"),
            ("reward to risk", trade.RewardToRisk is { } ratio ? ratio.ToString(CultureInfo.InvariantCulture) : "none"),
            ("stop in typical moves", Figure(trade.StopInMoves)));

        if (trade.RewardToRisk is not { } rewardToRisk)
        {
            return new Gate(Trade, false, trade.Absent ?? "no reward to risk", values);
        }

        if (trade.StopInMoves is not { } moves)
        {
            return new Gate(Trade, false, "no typical move to read the stop's distance in", values);
        }

        var enough = Statistic.FromRatio(rewardToRisk) >= settings.RewardToRiskFloor;
        var placed = moves >= settings.StopLow && moves <= settings.StopHigh;

        return new Gate(
            Trade,
            enough && placed,
            Invariant($"reward to risk {rewardToRisk} {(enough ? "at or above" : "below")} {settings.RewardToRiskFloor:0.##}; the stop {moves:0.00} typical moves below the entry, {(placed ? "inside" : "outside")} {settings.StopLow:0.##} to {settings.StopHigh:0.##}"),
            values);
    }

    // The exclusions, each a reason a name that passes every gate is still left off, and the notes on
    // an earnings date the night could not count.
    static (IReadOnlyList<string> Exclusions, IReadOnlyList<string> Notes) Excluded(GateInputs inputs, FilterSettings settings)
    {
        var exclusions = new List<string>();
        var notes = new List<string>();

        if (inputs.Gap is not null)
        {
            exclusions.Add(GapExclusion);
        }

        if (inputs.Suspect)
        {
            exclusions.Add(SuspectExclusion);
        }

        if (inputs.SessionsToEarnings is { } sessions && sessions >= 0 && sessions <= settings.EarningsWindowSessions)
        {
            exclusions.Add(EarningsExclusion);
        }
        else if (inputs.NextEarnings is null)
        {
            notes.Add("no earnings date is on file, so none is excluded for one");
        }
        else if (inputs.SessionsToEarnings is null)
        {
            notes.Add("the next earnings date is past the end of the closure table, so its sessions are not counted");
        }

        return (exclusions, notes);
    }

    // The order of the names passing: reward to risk on the plan the settings name, then strength, then
    // the setup band's strength, then the ticker, so two reads of one night cannot disagree.
    public static IReadOnlyList<GateResult> Ranked(IEnumerable<GateResult> results, FilterSettings settings) =>
    [
        .. results
            .Where(result => result.Passed)
            .OrderByDescending(result => (settings.Trade == TradeInput.Ladder ? result.LadderTrade : result.SwingTrade).RewardToRisk)
            .ThenByDescending(result => result.Strength)
            .ThenByDescending(result => result.BandStrength)
            .ThenBy(result => result.Ticker, StringComparer.Ordinal),
    ];

    static IEnumerable<string> Missing(params (bool Held, string Why)[] conditions) =>
        conditions.Where(condition => !condition.Held).Select(condition => condition.Why);

    static IReadOnlyDictionary<string, string> Values(params (string Key, string Value)[] values) =>
        values.ToDictionary(value => value.Key, value => value.Value, StringComparer.Ordinal);

    static string Figure(double? value) => value is { } held ? held.ToString("R", CultureInfo.InvariantCulture) : "none";

    static string Whole(int value) => value.ToString(CultureInfo.InvariantCulture);

    static string Price(decimal? value) => value is { } held ? held.ToString(CultureInfo.InvariantCulture) : "none";

    static string Invariant(FormattableString text) => FormattableString.Invariant(text);
}
