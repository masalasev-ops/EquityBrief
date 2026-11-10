using EquityBrief.Core.EntryCheck;

namespace EquityBrief.Core.Loop;

// One way to trade a flagged setup: where it buys and within how many sessions, where its stop stands and how it sells.
public sealed record PlanCombination(string Entry, int Window, string Stop, string Exit)
{
    public string Words => Window > 0
        ? FormattableString.Invariant($"{Entry} within {Window} sessions, {Stop}, {Exit}")
        : FormattableString.Invariant($"{Entry}, {Stop}, {Exit}");
}

// What the sale reads: a target the close must reach, a trail the stop follows the highest close by, the staged levels
// each selling an equal share with the last share held to the stop or the time limit, or none of them, the time limit alone.
public sealed record PlanSale(double? Target, double? Trail, IReadOnlyList<double> Staged)
{
    // From the 23.8 correction the dip's own sale: a close whose 2-session RSI stands above this level sells what is held at
    // the next open; none for every other family.
    // see: The buy and sell points sell the dip at the next open after its 2-session RSI closes above 65
    public double? RsiAbove { get; init; }
}

// How one filled pick ended: its result in its own risk before costs, the price each share sold at weighted by its share,
// the sessions held from the fill and how it ended; none where the history ends before it does.
public sealed record PlanOutcome(double Result, double Sold, int Sessions, string End);

// One pick a combination offered: the year and era its setup was read in, the market's side of its 200-day average that
// session, whether it filled, and where it did its result and edge after costs in its own risk, the sessions held, how it
// ended, the session it ended on and the rate it had to beat to break even, none where its sale names no reward.
public sealed record PlanPick(
    int Year,
    string Era,
    string Regime,
    bool Filled,
    double? Result,
    double? Edge,
    int Sessions,
    string? End,
    int EndedOn,
    double? BreakEven)
{
    // Whether the fill reached its sale: the target, a band, every staged level or the trail; for the time limit alone,
    // its last session rather than the stop.
    public bool Hit { get; init; }

    // From the 23.8 correction the edge is over the benchmark as ruled, and this is the second line: the result less its
    // index's own fund held over the same sessions in the same risk.
    // see: Every result over the long history is judged against the benchmark as ruled, with its index's own fund held over the same sessions as a second line
    public double? FundEdge { get; init; }
}

// What a combination came to over a group of picks: every pick offered, those filled, the fill rate, the share of fills
// reaching its sale rather than the stop beside the mean rate it had to beat, the mean result and edge per pick offered
// with a pick not filled counted as nothing and per fill, the mean sessions held and the worst fall of the results summed
// in the order the trades ended.
public sealed record PlanFigures(
    int Picks,
    int Fills,
    double? FillRate,
    double? HitRate,
    double? BreakEven,
    double? ResultPerPick,
    double? ResultPerFill,
    double? EdgePerPick,
    double? EdgePerFill,
    double? DaysHeld,
    double? WorstDrawdown,
    double? FundEdgePerPick = null,
    double? FundEdgePerFill = null);

// A combination's figures gathered a pick at a time, the same arithmetic as the figures over a list, the fills' results kept
// for the worst drawdown only where the tally is asked to keep them.
public sealed class PlanTally(bool drawdown)
{
    readonly List<(int EndedOn, double Result)> ended = [];
    int picks;
    int fills;
    int hits;
    int breaks;
    int edges;
    int funds;
    double breakSum;
    double resultSum;
    double edgeSum;
    double fundSum;
    double sessionSum;

    public void Add(PlanPick pick)
    {
        picks++;

        if (!pick.Filled || pick.Result is not { } result)
        {
            return;
        }

        fills++;
        resultSum += result;
        sessionSum += pick.Sessions;
        hits += pick.Hit ? 1 : 0;

        if (pick.BreakEven is { } even)
        {
            breaks++;
            breakSum += even;
        }

        if (pick.Edge is { } edge)
        {
            edges++;
            edgeSum += edge;
        }

        if (pick.FundEdge is { } fund)
        {
            funds++;
            fundSum += fund;
        }

        if (drawdown)
        {
            ended.Add((pick.EndedOn, result));
        }
    }

    public PlanFigures Figures()
    {
        double? worst = null;

        if (drawdown && fills > 0)
        {
            var (sum, peak, fall) = (0.0, 0.0, 0.0);

            foreach (var (_, result) in ended.OrderBy(one => one.EndedOn))
            {
                sum += result;
                peak = Math.Max(peak, sum);
                fall = Math.Max(fall, peak - sum);
            }

            worst = fall;
        }

        return new PlanFigures(
            picks,
            fills,
            picks > 0 ? 1.0 * fills / picks : null,
            fills > 0 ? 1.0 * hits / fills : null,
            breaks > 0 ? breakSum / breaks : null,
            picks > 0 ? resultSum / picks : null,
            fills > 0 ? resultSum / fills : null,
            picks > 0 && edges > 0 ? edgeSum / picks : null,
            edges > 0 ? edgeSum / edges : null,
            fills > 0 ? sessionSum / fills : null,
            worst,
            picks > 0 && funds > 0 ? fundSum / picks : null,
            funds > 0 ? fundSum / funds : null);
    }
}

// The traded plan's buy and sell points tested over the long history: each entry, the next open or a level the pick must
// trade into within a window, each stop and each sale, walked over a flagged setup's own closes as the ledger walks a
// setup, a close under the stop selling at that close. Nothing here moves a pick, a plan, a rule or a version.
// see: Every price family's traded plan's buy and sell points are tested over the long history and one combination is recommended a family an index
public static class PlanPoints
{
    public const string NextOpen = "the next open";
    public const string FirstZone = "the first zone";
    public const string SecondZone = "the second zone";
    public const string ThirdZone = "the third zone";

    // The average the entry check's wait entry buys at, the night's 20-session one.
    public const int AverageSessions = 20;

    public static string Average { get; } = FormattableString.Invariant($"the {AverageSessions}-session average");

    public const string RuleStop = "the rule's stop";
    public const string ZoneStop = "the zone's stop";

    public const string RuleSale = "the rule's sale";
    public const string FirstBand = "the first band above";
    public const string StagedSales = "the staged sales";
    public const string TimeLimit = "the time limit alone";

    public const string Above = "above its 200-day average";
    public const string Below = "below its 200-day average";

    public const string Stopped = "stop";
    public const string Reached = "sale";
    public const string Trailed = "trail";
    public const string Timed = "time";

    // The families the operator named, each with a price stop.
    public static IReadOnlyList<string> Families { get; } = ["pullback", "breakout", "drift", "dip"];

    public static IReadOnlyList<string> Zones { get; } = [FirstZone, SecondZone, ThirdZone];

    public static IReadOnlyList<int> Windows { get; } = [5, 10, 20];

    public static IReadOnlyList<double> TypicalDays { get; } = [0.75, 1, 1.5, 2];

    public static IReadOnlyList<string> Sales { get; } = [RuleSale, FirstBand, StagedSales, TimeLimit];

    // The fewest fills a recommendation rests on, over the years no setting was chosen on.
    public const int FewestFills = 100;

    public static string TypicalStop(double days) => FormattableString.Invariant($"{days:0.##} typical days under the buy");

    // On the operator's order of 2026-11-10, item 1, the wider stops a recommendation stopping 2 typical days under the buy
    // is walked again at, the one doing best from 2019 standing where it beats it there.
    // see: A recommendation stopping 2 typical days under its buy is walked again wider and buying at a level beside the same distance under the close, each kept where it does better from 2019
    public static IReadOnlyList<double> WiderDays { get; } = [2.5, 3, 4];

    public const double WidestOfTheGrid = 2;

    // On the same order, item 2, an entry at a plain distance under the pick's close in its typical daily moves, walked beside
    // a recommendation buying at a zone or the average; the zone is kept unless the distance does better from 2019 by more
    // than this many risks a pick.
    public const string UnderTheClose = "typical days under the close";

    public const double ZoneKeptWithin = 0.02;

    public static string DistanceEntry(double days) => FormattableString.Invariant($"{days:0.##} {UnderTheClose}");

    public static double? DistanceOf(string entry) =>
        entry.EndsWith(" " + UnderTheClose, StringComparison.Ordinal)
        && double.TryParse(entry[..^(UnderTheClose.Length + 1)], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var days)
        && days > 0
            ? days
            : null;

    // The wider stop to use, of those walked, where its mean edge a pick offered from 2019 beats the recommendation's own;
    // none where none does.
    public static PlanCombination? Wider(double? since, IReadOnlyList<(PlanCombination Combination, double? Since)> wider) =>
        wider
            .Where(one => one.Since is { } edge && (since is not { } held || edge > held))
            .OrderByDescending(one => one.Since)
            .Select(one => one.Combination)
            .FirstOrDefault();

    // Whether a plain distance replaces a zone or the average: only where it does better from 2019 by more than the margin.
    public static bool DistanceReplaces(double? zone, double? distance) =>
        distance is { } plain && (zone is not { } level || plain > level + ZoneKeptWithin);

    // Every combination: the next open, then each zone and the average within each window; for each the rule's stop, a
    // zone's own stop where the entry is a zone, and each typical-days stop; and each sale.
    public static IReadOnlyList<PlanCombination> Combinations { get; } = Every();

    static IReadOnlyList<PlanCombination> Every()
    {
        var entries = new List<(string Entry, int Window)> { (NextOpen, 0) };

        foreach (var entry in Zones.Append(Average))
        {
            entries.AddRange(Windows.Select(window => (entry, window)));
        }

        return
        [
            .. from one in entries
               from stop in StopsFor(one.Entry)
               from sale in Sales
               select new PlanCombination(one.Entry, one.Window, stop, sale),
        ];
    }

    public static IReadOnlyList<string> StopsFor(string entry) =>
        [RuleStop, .. Zones.Contains(entry) ? [ZoneStop] : Array.Empty<string>(), .. TypicalDays.Select(TypicalStop)];

    // The stop a combination sets: the rule's own price, the zone's own, or the typical days under the fill's price.
    public static double? StopOf(string stop, double fill, double ruleStop, double? zoneStop, double typicalDay)
    {
        if (stop == RuleStop)
        {
            return ruleStop;
        }

        if (stop == ZoneStop)
        {
            return zoneStop;
        }

        var days = TypicalDays.Concat(WiderDays).FirstOrDefault(one => TypicalStop(one) == stop);

        return days > 0 && typicalDay > 0 ? fill - (days * typicalDay) : null;
    }

    // Where a level's buy fills: the first session after the pick within the window trading into it, at its open where it
    // opens at or under the level and at the level otherwise; the next open fills at the session after the pick.
    public static WaitFill? FillOf(ReadOnlySpan<double> opens, ReadOnlySpan<double> lows, int pick, double? level, int window)
    {
        if (window == 0)
        {
            return pick + 1 < opens.Length && opens[pick + 1] > 0 ? new WaitFill(pick + 1, opens[pick + 1]) : null;
        }

        return level is { } held && held > 0 ? EntryMeasure.FillOf(opens, lows, pick, held, 0, window) : null;
    }

    // One filled pick walked over its closes from the fill's session, as the ledger walks a setup and its fill step sells
    // it: a close under the stop sells what is held; a close at or over a staged level sells its share; a close at or over
    // the target sells what is held; a close under the trail, the highest earlier close less its distance and never under
    // the stop, sells what is held; and the close of the time limit's last session sells what is left; each sale at the
    // next session's open. None where the history ends before the sale.
    public static PlanOutcome? Walk(ReadOnlySpan<double> closes, ReadOnlySpan<double> opens, int fillBar, double fill, double stop, PlanSale sale, int cap, ReadOnlySpan<double> rsi = default)
    {
        if (!(fill > stop) || cap <= 0 || fillBar >= closes.Length)
        {
            return null;
        }

        var levels = sale.Staged.Where(level => level > fill).Order().ToArray();
        var share = levels.Length > 0 ? 1.0 / (levels.Length + 1) : 1.0;
        var (held, sold, next, highest) = (1.0, 0.0, 0, fill);

        for (var session = fillBar; session < fillBar + cap; session++)
        {
            if (session + 1 >= closes.Length || session + 1 >= opens.Length || !(opens[session + 1] > 0))
            {
                return null;
            }

            var close = closes[session];
            var open = opens[session + 1];
            var floor = sale.Trail is { } distance ? Math.Max(stop, highest - distance) : stop;

            highest = Math.Max(highest, close);

            if (close < stop)
            {
                return Ended(sold + (held * open), session, Stopped);
            }

            while (next < levels.Length && close >= levels[next])
            {
                (sold, held, next) = (sold + (share * open), held - share, next + 1);
            }

            if (sale.Target is { } target && close >= target)
            {
                return Ended(sold + (held * open), session, Reached);
            }

            if (sale.RsiAbove is { } level && session < rsi.Length && rsi[session] > level)
            {
                return Ended(sold + (held * open), session, Reached);
            }

            if (sale.Trail is not null && session > fillBar && close < floor)
            {
                return Ended(sold + (held * open), session, Trailed);
            }

            if (session == fillBar + cap - 1)
            {
                return Ended(sold + (held * open), session, levels.Length > 0 && next == levels.Length ? Reached : Timed);
            }
        }

        return null;

        PlanOutcome Ended(double price, int session, string end) => new((price - fill) / (fill - stop), price, session + 1 - fillBar, end);
    }

    // From the 23.8 correction the benchmark a combination is read against: the same plan entered on every member of the
    // index that session, each bought at its own next open and sold as the combination sells, its stop and its sale placed
    // on the member as the combination places them on the pick, a zone's stop, the first band above and the staged sales
    // read off the member's own levels plan; a member its stop or sale cannot be placed on is not entered.
    // see: Every result over the long history is judged against the benchmark as ruled, with its index's own fund held over the same sessions as a second line
    //
    // One member's result, in the risk the rule's plan sets on it at the session's close: bought at its next open, its stop
    // and its sale placed from that fill, and walked as the pick is; none where the member places no trade or its walk has
    // not ended.
    public static double? MemberResult(ReadOnlySpan<double> closes, ReadOnlySpan<double> opens, int bar, double risk, Func<double, double?> stopAt, Func<double, PlanSale?> saleAt, int cap, ReadOnlySpan<double> rsi = default)
    {
        if (bar < 0 || bar + 1 >= closes.Length || bar + 1 >= opens.Length || !(risk > 0) || !(closes[bar] - risk > 0))
        {
            return null;
        }

        var fill = opens[bar + 1];

        if (!(fill > 0) || stopAt(fill) is not { } placed || !(fill > placed) || saleAt(fill) is not { } sale)
        {
            return null;
        }

        return Walk(closes, opens, bar + 1, fill, placed, sale, cap, rsi) is { } outcome ? (outcome.Sold - fill) / risk : null;
    }

    // The mean of the members' results, none where none ended. A member whose result lies past 20 risks either way is left
    // out: a member whose prices stand still sets a risk near nought, and its result read in it runs to hundreds.
    public static double? BenchmarkOf(IEnumerable<double?> results)
    {
        var (sum, count) = (0.0, 0);

        foreach (var result in results)
        {
            if (result is { } held && Math.Abs(held) <= History.LongLedger.OutlyingRisks)
            {
                sum += held;
                count++;
            }
        }

        return count > 0 ? sum / count : null;
    }

    // The rate a fill must reach its sale at to break even before costs, where the sale names one price: the risk over
    // the risk and the reward together.
    public static double? BreakEvenOf(double fill, double stop, double? target) =>
        target is { } held && held > fill && fill > stop ? (fill - stop) / (held - stop) : null;

    // From the 23.6 correction a pick whose result or whose edge over its benchmark lies past 20 risks either way is no result,
    // counted as offered and in no figure, as an open pick is.
    // see: A long setup whose edge lies past 20 risks either way is counted as no result and listed beside the unexplained jumps
    public static bool NoResult(double result, double? edge) =>
        Math.Abs(result) > History.LongLedger.OutlyingRisks || History.LongLedger.Outlying(edge);

    // The dip's own sale: no target and no trail, sold at the next open after a close whose 2-session RSI stands above 65.
    // see: The buy and sell points sell the dip at the next open after its 2-session RSI closes above 65
    public static PlanSale DipSale { get; } = new(null, null, []) { RsiAbove = global::EquityBrief.Core.Families.DipRule.RsiExit };

    // A series' 2-session RSI as the dip reads it, NaN where it reads none.
    public static double[] RsiOf(IReadOnlyList<double> closes) =>
        [.. global::EquityBrief.Core.Families.DipRule.Rsi(closes, global::EquityBrief.Core.Families.DipRule.RsiSessions).Select(one => one ?? double.NaN)];

    // Whether an outcome reached its sale, the time limit alone reaching it at its last session.
    public static bool HitOf(string sale, string end) =>
        sale == TimeLimit ? end != Stopped : end is Reached or Trailed;

    // A combination's figures over a group of picks, the worst drawdown read over the fills' results summed in the order
    // their trades ended.
    public static PlanFigures FiguresOf(IReadOnlyList<PlanPick> picks)
    {
        var fills = picks.Where(pick => pick.Filled && pick.Result is not null).ToArray();
        var edges = fills.Where(pick => pick.Edge is not null).ToArray();
        var funds = fills.Where(pick => pick.FundEdge is not null).ToArray();
        var breaks = fills.Where(pick => pick.BreakEven is not null).ToArray();
        var (sum, peak, worst) = (0.0, 0.0, 0.0);

        foreach (var fill in fills.OrderBy(one => one.EndedOn))
        {
            sum += fill.Result!.Value;
            peak = Math.Max(peak, sum);
            worst = Math.Max(worst, peak - sum);
        }

        return new PlanFigures(
            picks.Count,
            fills.Length,
            picks.Count > 0 ? 1.0 * fills.Length / picks.Count : null,
            fills.Length > 0 ? 1.0 * fills.Count(one => one.Hit) / fills.Length : null,
            breaks.Length > 0 ? breaks.Average(one => one.BreakEven!.Value) : null,
            picks.Count > 0 ? fills.Sum(one => one.Result!.Value) / picks.Count : null,
            fills.Length > 0 ? fills.Average(one => one.Result!.Value) : null,
            picks.Count > 0 && edges.Length > 0 ? edges.Sum(one => one.Edge!.Value) / picks.Count : null,
            edges.Length > 0 ? edges.Average(one => one.Edge!.Value) : null,
            fills.Length > 0 ? fills.Average(one => one.Sessions) : null,
            fills.Length > 0 ? worst : null,
            picks.Count > 0 && funds.Length > 0 ? funds.Sum(one => one.FundEdge!.Value) / picks.Count : null,
            funds.Length > 0 ? funds.Average(one => one.FundEdge!.Value) : null);
    }

    // The combination recommended on a family and index: the highest edge per pick offered over the years no setting was
    // chosen on among those resting on the fewest fills or more, the fewer changes from the next-open buy at the rule's
    // stop and sale first on a tie; none where no combination rests on enough.
    public static PlanCombination? Recommended(IReadOnlyList<(PlanCombination Combination, PlanFigures Unseen)> read) =>
        read
            .Where(one => one.Unseen.Fills >= FewestFills && one.Unseen.EdgePerPick is not null)
            .OrderByDescending(one => one.Unseen.EdgePerPick)
            .ThenBy(one => Changes(one.Combination))
            .Select(one => one.Combination)
            .FirstOrDefault();

    public static int Changes(PlanCombination combination) =>
        (combination.Entry == NextOpen ? 0 : 1) + (combination.Stop == RuleStop ? 0 : 1) + (combination.Exit == RuleSale ? 0 : 1);
}
