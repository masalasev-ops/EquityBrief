using System.Globalization;
using System.Text.Json;

namespace EquityBrief.Core.Filter;

// The one gate a member close to a buy point missed: which, what it had against the bar it needed in plain
// words, how far short that is as a share of the bar, and the trade its plan states where it states one.
// see: A member that missed exactly one gate and no exclusion is drawn close to a buy point nearest first, and recommends nothing
public sealed record MissedGate(string Gate, string Words, double Distance, string? Trade = null);

// A member's stored swing filter result as "Close to a buy point" reads it: the five gates' answers, how many
// exclusions it carries, whether it passed, the gates' stored reasons and values, and the entry, stop and
// target each plan stored.
public sealed record NearMissResult(
    string Ticker,
    bool Market,
    bool Trend,
    bool Setup,
    bool Trigger,
    bool Trade,
    int Exclusions,
    bool Passed,
    string Gates,
    double? Strength = null,
    int? BandStrength = null,
    decimal? Entry = null,
    decimal? SwingStop = null,
    decimal? SwingTarget = null,
    decimal? ClearStop = null,
    decimal? ClearTarget = null);

// "Close to a buy point": the members the swing filter stored as missing exactly one of its five gates on a
// night with nothing excluding them, each with the gate it missed, what it had against the bar it needed in
// plain words, and how far that is.
//
// It reads the gate results the night stored for every member and the settings of the version each was
// stored under, and computes nothing a gate did not: a reading the gate decided on is read back off the row,
// and a bar off the version. The one figure it works out is the distance, which orders the list.
//
// The distance is how far the deciding value sits from its bar as a share of the bar, so a tenth short reads
// the same on every gate whatever its unit is: sessions, multiples of risk, typical moves or a share of the
// index. A condition not met at all counts as one whole bar short, since none of it is met: no firing among
// the stored results, no band to set a target or a stop at, a close outside an anchored support band. A trend
// that is not an uptrend is half a bar short for a range and a whole bar for a downtrend, the classifier's
// range sitting between the two. Where a gate misses on more than one of its conditions, their shortfalls add.
//
// The page draws the list and the overnight queue drafts its names after tonight's list, both in the order
// this states, so the two read one rule.
// see: A member that missed exactly one gate and no exclusion is drawn close to a buy point nearest first, and recommends nothing
public static class NearMiss
{
    // How far short a condition not met at all is, and a range against an uptrend.
    public const double WholeBar = 1.0;
    public const double RangeShort = 0.5;

    // The gates a result failed, in the funnel's order.
    public static IReadOnlyList<string> Failed(NearMissResult result) =>
    [
        .. new[]
        {
            (SwingGates.Market, result.Market),
            (SwingGates.Trend, result.Trend),
            (SwingGates.Setup, result.Setup),
            (SwingGates.Trigger, result.Trigger),
            (SwingGates.Trade, result.Trade),
        }.Where(one => !one.Item2).Select(one => one.Item1),
    ];

    // Whether a result is close to a buy point: exactly one gate failed and nothing excluded it. Two failed
    // gates or any exclusion keep it off, an exclusion being a reason not to trade rather than a check narrowly
    // failed.
    public static bool MissedOne(NearMissResult result) =>
        !result.Passed && result.Exclusions == 0 && Failed(result).Count == 1;

    // The one gate a result missed, in words and as a distance, read off its stored values against the bars
    // the version it was stored under holds. The trigger's arrival is read off the sessions before the night
    // the store holds a result for, being how many sessions before tonight its trigger first fired, or none
    // where no firing is among them.
    public static MissedGate? MissOf(NearMissResult result, FilterSettings settings, int? firedSessionsBefore)
    {
        if (!MissedOne(result))
        {
            return null;
        }

        var missed = Failed(result)[0];
        var (reason, values) = Gate(result.Gates, missed);

        string Held(string key) => values.TryGetValue(key, out var value) ? value : "none";

        double? Number(string key) =>
            double.TryParse(Held(key), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : null;

        var words = new List<string>();
        var distance = 0.0;

        void Short(double shortfall, string said)
        {
            distance += shortfall;
            words.Add(said);
        }

        switch (missed)
        {
            case SwingGates.Market:
            {
                var floor = Number("floor") ?? settings.BreadthFloor;

                if (Number("breadth") is { } breadth)
                {
                    Short((floor - breadth) / floor, Say($"the market's breadth is {breadth * 100:0.0}%, needs {floor * 100:0.#}%"));
                }

                break;
            }

            case SwingGates.Trend:
            {
                var state = Held("trend state");

                if (state != SwingGates.Uptrend)
                {
                    Short(state == "range" ? RangeShort : WholeBar, $"the chart reads {(state == "none" ? "no trend" : "a " + state.Replace('_', ' '))}, needs an uptrend");
                }

                var floor = Number("floor") ?? settings.StrengthFloor;

                if (Number("strength") is { } strength)
                {
                    if (strength < floor)
                    {
                        Short((floor - strength) / floor, Say($"strength {strength:0.00}, needs {floor:0.00}"));
                    }
                }
                else
                {
                    Short(WholeBar, "strength is not available");
                }

                break;
            }

            case SwingGates.Setup:
            {
                if (Number("depth") is { } depth)
                {
                    if (depth < settings.DepthLow)
                    {
                        Short((settings.DepthLow - depth) / settings.DepthLow, Say($"the pullback is {depth:0.00} typical moves deep, needs {settings.DepthLow:0.##} to {settings.DepthHigh:0.##}"));
                    }
                    else if (depth > settings.DepthHigh)
                    {
                        Short((depth - settings.DepthHigh) / settings.DepthHigh, Say($"the pullback is {depth:0.00} typical moves deep, needs {settings.DepthLow:0.##} to {settings.DepthHigh:0.##}"));
                    }
                }

                if (Number("dry-up") is { } dryUp && dryUp > settings.DryUpCeiling)
                {
                    Short((dryUp - settings.DryUpCeiling) / settings.DryUpCeiling, Say($"volume while it came down is {dryUp:0.00} times its average, needs {settings.DryUpCeiling:0.##} or less"));
                }

                if (Held(SwingGates.PullbackBandValue) != "yes")
                {
                    Short(WholeBar, "the close is not inside an anchored support band");
                }

                break;
            }

            case SwingGates.Trigger:
            {
                var window = (int?)Number("arrival window") ?? settings.ArrivalSessions;

                // The window counts tonight among its sessions, so a firing some sessions before tonight is
                // inside it while that count is below the window. One inside it on a row whose trigger still
                // failed is a firing whose arrival a missing result decided, and it is the gate's own words.
                if (firedSessionsBefore is { } before && before + 1 > window)
                {
                    Short((before + 1.0 - window) / window, Say($"the buy signal fired {before} session{(before == 1 ? string.Empty : "s")} before tonight, outside its {window}-session window"));
                }
                else if (firedSessionsBefore is null)
                {
                    Short(WholeBar, "the buy signal has not fired in the sessions the store holds");
                }
                else
                {
                    Short(WholeBar, reason);
                }

                break;
            }

            case SwingGates.Trade:
            {
                if (Number("reward to risk") is { } ratio)
                {
                    if (ratio < settings.RewardToRiskFloor)
                    {
                        Short((settings.RewardToRiskFloor - ratio) / settings.RewardToRiskFloor, Say($"the reward is {ratio:0.00} times the risk, needs {settings.RewardToRiskFloor:0.##}"));
                    }
                }
                else
                {
                    Short(WholeBar, reason);
                }

                if (Number("stop in typical moves") is { } stop)
                {
                    if (stop < settings.StopLow)
                    {
                        Short((settings.StopLow - stop) / settings.StopLow, Say($"the stop is {stop:0.00} typical moves below the entry, needs {settings.StopLow:0.##} to {settings.StopHigh:0.##}"));
                    }
                    else if (stop > settings.StopHigh)
                    {
                        Short((stop - settings.StopHigh) / settings.StopHigh, Say($"the stop is {stop:0.00} typical moves below the entry, needs {settings.StopLow:0.##} to {settings.StopHigh:0.##}"));
                    }
                }

                break;
            }
        }

        // A gate that failed with nothing its values measure is a whole bar short, in the gate's own words.
        if (words.Count == 0)
        {
            Short(WholeBar, reason);
        }

        return new MissedGate(missed, string.Join("; ", words), distance, TradeOf(result, settings));
    }

    // The trade the plan the trade gate reads states, where it states one: entered at the night's close,
    // stopped and won where that plan puts them.
    static string? TradeOf(NearMissResult result, FilterSettings settings)
    {
        decimal? stop = settings.Trade switch
        {
            TradeInput.Clear => result.ClearStop,
            TradeInput.Swing => result.SwingStop,
            _ => null,
        };

        decimal? target = settings.Trade switch
        {
            TradeInput.Clear => result.ClearTarget,
            TradeInput.Swing => result.SwingTarget,
            _ => null,
        };

        return result.Entry is { } entry && stop is { } placed && target is { } won
            ? Say($"entry {entry:0.00}, stop {placed:0.00}, target {won:0.00}")
            : null;
    }

    // The first time a trigger fired before tonight in the run of firings nearest tonight, as sessions before
    // tonight, off the member's stored results newest first, tonight's own among them; none where no stored
    // session holds a firing. A firing is a session holding the event with none on the session before it.
    public static int? FiredSessionsBefore(IReadOnlyList<bool?> eventsNewestFirst)
    {
        for (var at = 0; at < eventsNewestFirst.Count; at++)
        {
            if (eventsNewestFirst[at] != true)
            {
                continue;
            }

            // The start of the run this event belongs to: the oldest consecutive session holding it.
            var first = at;

            while (first + 1 < eventsNewestFirst.Count && eventsNewestFirst[first + 1] == true)
            {
                first++;
            }

            return first;
        }

        return null;
    }

    // The trade's reward to risk the trade gate stored, which breaks a tie in the distance as the list's own
    // order does, and none where it stored none.
    public static double? RewardToRisk(string gates) =>
        double.TryParse(Gate(gates, SwingGates.Trade).Values.GetValueOrDefault("reward to risk"), NumberStyles.Float, CultureInfo.InvariantCulture, out var ratio)
            ? ratio
            : null;

    // Close to a buy point, nearest first, a tie going by the first list's own order: the state the member's
    // reported quarters give it, then the trade's reward to risk, then strength, then band strength, then the
    // ticker.
    public static IReadOnlyList<T> Ordered<T>(
        IEnumerable<T> rows,
        Func<T, string> ticker,
        Func<T, double> distance,
        Func<T, int> statePlace,
        Func<T, double?> rewardToRisk,
        Func<T, double?> strength,
        Func<T, int?> bandStrength) =>
    [
        .. rows
            .OrderBy(distance)
            .ThenBy(statePlace)
            .ThenBy(row => rewardToRisk(row) is null)
            .ThenByDescending(rewardToRisk)
            .ThenByDescending(strength)
            .ThenByDescending(bandStrength)
            .ThenBy(ticker, StringComparer.Ordinal),
    ];

    static (string Reason, IReadOnlyDictionary<string, string> Values) Gate(string json, string name)
    {
        using var document = JsonDocument.Parse(json);

        foreach (var one in document.RootElement.GetProperty("gates").EnumerateArray())
        {
            if (one.GetProperty("gate").GetString() == name)
            {
                return (
                    one.GetProperty("reason").GetString() ?? string.Empty,
                    one.GetProperty("values").EnumerateObject().ToDictionary(value => value.Name, value => value.Value.GetString() ?? "none", StringComparer.Ordinal));
            }
        }

        return (string.Empty, new Dictionary<string, string>(StringComparer.Ordinal));
    }

    static string Say(FormattableString words) => words.ToString(CultureInfo.InvariantCulture);
}
