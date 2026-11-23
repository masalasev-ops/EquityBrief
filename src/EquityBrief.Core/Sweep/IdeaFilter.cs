using System.Globalization;

namespace EquityBrief.Core.Sweep;

// From 24.35 the swing filter's dials as a part of a pullback's line: its strength floor, its breadth floor, its depth band,
// its dry-up ceiling, its arrival window, its reward-to-risk floor, its stop band and its least band strength, each a level the
// sweep's extended grid holds, the earnings window held at the live filter's 15 sessions. A setup the line's event offers is
// kept where the filter passes it, read off its stored readings as the ledger read them: the sweep's own corner over the grid,
// and no report inside the window. Every level is the grid's, so the part is closed as every other part of the grammar is.
// see: An idea is one line of a closed grammar that code parses into one canonical line and tests with no build session
public sealed record IdeaFilter(double Strength, double Breadth, double DepthLow, double DepthHigh, double DryUp, int Arrival, double Reward, double StopLow, double StopHigh, int Band)
{
    public const int EarningsWindow = 15;

    // The readings a filter reads, the stop's distance in typical moves worked from the setup's own entry, stop and move.
    public static IReadOnlyList<string> Readings { get; } = ["strength", "breadth", "depth", "dry_up", "freshness", "reward_to_risk", "band_strength", "earnings_sessions", "move_share"];

    public const string StopMoves = "stop_moves";

    // The reading the stop's distance is worked in: the typical move as a share of the close.
    public const string MoveShare = "move_share";

    static SweepGrid Grid => SweepGrid.Extended;

    public string Part => FormattableString.Invariant(
        $"filter strength {N(Strength)}, breadth {(double.IsNegativeInfinity(Breadth) ? "off" : N(Breadth))}, depth {N(DepthLow)} to {N(DepthHigh)}, dry-up {(double.IsPositiveInfinity(DryUp) ? "off" : N(DryUp))}, arrival {Arrival}, reward {N(Reward)}, stop {N(StopLow)} to {N(StopHigh)}, band {Band}");

    public string Words => FormattableString.Invariant(
        $"strength at least {N(Strength)}, {(double.IsNegativeInfinity(Breadth) ? "the market not read" : "at least " + N(Breadth * 100) + "% of the index's stocks above their 200-day average")}, a pullback {N(DepthLow)} to {N(DepthHigh)} typical moves deep, volume {(double.IsPositiveInfinity(DryUp) ? "not read" : "under " + N(DryUp) + " times its average")}, arriving within {Arrival} sessions, reward at least {N(Reward)} times the risk, a stop {N(StopLow)} to {N(StopHigh)} typical moves under the close and a band of strength {Band} or more");

    // The setting on the extended grid the corner reads.
    public DialSetting Setting => new(
        SweepGrid.IndexOf(Grid.StrengthBars, Strength),
        SweepGrid.IndexOf(Grid.DepthLows, DepthLow),
        SweepGrid.IndexOf(Grid.DepthHighs, DepthHigh),
        SweepGrid.IndexOf(Grid.DryUpCeilings, DryUp),
        SweepGrid.IndexOf(Grid.Freshness, Arrival),
        SweepGrid.IndexOf(Grid.RewardToRiskFloors, Reward),
        Grid.StopBounds.ToList().IndexOf((StopLow, StopHigh)),
        SweepGrid.IndexOf(Grid.MarketFloors, Breadth),
        SweepGrid.IndexOf(Grid.BandStrengths, Band));

    // Whether the filter passes a setup off its readings: the sweep's corner over the grid at this setting, and no report
    // inside the earnings window; none passes where a reading the corner needs is not held.
    public bool Passes(IReadOnlyDictionary<string, double> readings)
    {
        double Read(string name) => readings.TryGetValue(name, out var value) ? value : double.NaN;

        if (!readings.ContainsKey("freshness") || !readings.ContainsKey("band_strength") || !readings.ContainsKey(StopMoves))
        {
            return false;
        }

        var corner = SweepCorner.Of(Grid, Read("strength"), Read("depth"), Read("dry_up"), (int)Math.Round(Read("freshness")), Read("reward_to_risk"), Read(StopMoves), Read("breadth"), (int)Math.Round(Read("band_strength")));
        var inTheWindow = readings.TryGetValue("earnings_sessions", out var sessions) && sessions >= 0 && sessions <= EarningsWindow;

        return corner is { } placed && placed.Passes(Setting) && !inTheWindow;
    }

    // A filter part read, none where a word or a level is not the grid's: "filter strength 0.5, breadth 0.45, depth 1 to 5,
    // dry-up 1.5, arrival 3, reward 2, stop 0.5 to 4, band 0", its dials in that order.
    public static IdeaFilter? Parse(string part)
    {
        if (!part.StartsWith("filter ", StringComparison.Ordinal))
        {
            return null;
        }

        var dials = part["filter ".Length..].Split(',', StringSplitOptions.TrimEntries);

        if (dials.Length != 8)
        {
            return null;
        }

        string[] Words(int at, string name) => dials[at].Split(' ') is { } words && words[0] == name ? words[1..] : [];
        double? Level(string word, IReadOnlyList<double> levels) =>
            word == "off" ? null : double.TryParse(word, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value) && SweepGrid.IndexOf(levels, value) >= 0 ? value : null;

        var strength = Words(0, "strength") is [var s] ? Level(s, Grid.StrengthBars) : null;
        var breadth = Words(1, "breadth") is [var b] ? b == "off" ? double.NegativeInfinity : Level(b, Grid.MarketFloors) : null;
        var depth = Words(2, "depth") is [var low, "to", var high] ? (Level(low, Grid.DepthLows), Level(high, Grid.DepthHighs)) : (null, null);
        var dryUp = Words(3, "dry-up") is [var d] ? d == "off" ? double.PositiveInfinity : Level(d, Grid.DryUpCeilings) : null;
        int? arrival = Words(4, "arrival") is [var a] && int.TryParse(a, NumberStyles.None, CultureInfo.InvariantCulture, out var sessions) && Grid.Freshness.Contains(sessions) ? sessions : null;
        var reward = Words(5, "reward") is [var r] ? Level(r, Grid.RewardToRiskFloors) : null;
        (double Low, double High)? stop = null;

        if (Words(6, "stop") is [var stopLow, "to", var stopHigh]
            && double.TryParse(stopLow, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var lowMoves)
            && double.TryParse(stopHigh, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var highMoves)
            && Grid.StopBounds.Contains((lowMoves, highMoves)))
        {
            stop = (lowMoves, highMoves);
        }
        int? band = Words(7, "band") is [var n] && int.TryParse(n, NumberStyles.None, CultureInfo.InvariantCulture, out var least) && Grid.BandStrengths.Contains(least) ? least : null;

        return strength is { } st && breadth is { } br && depth is ({ } dl, { } dh) && dryUp is { } du && arrival is { } ar && reward is { } rw && stop is { } sp && band is { } bd
            ? new IdeaFilter(st, br, dl, dh, du, ar, rw, sp.Low, sp.High, bd)
            : null;
    }

    static string N(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
}
