using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using EquityBrief.Core.Ledger;
using EquityBrief.Core.Loop;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Sweep;
using EquityBrief.Worker.Sweep;

namespace EquityBrief.Worker.Loop;

// The monthly run's trades priced at the next session's open through the core's one pricing function, as every trade the
// lists and books keep is: the tester's procedures, the per-rule test and the books they walk. A trade's walk is the
// sweep's own over the same closes, so it ends where it ends bought at the close, and its result is moved by its own gap
// over the risk its close put up; the benchmark, the same plan on every member that session, is moved by the members' own
// gaps over their own risks, each read in typical moves so a session's is read once whatever the plan. A trade whose
// session after holds no open is not bought, and a session no member holds an open after holds no benchmark. From 21.14 a
// swing trade a close's signal ends is sold at the open after that close, its result moved by that gap over the same
// risk, a walk that sells at an open of its own, as the dip's does, bought alone. From 21.17 the benchmark's members are
// sold the same way: each member's walk under the same plan and exit read again from its own closes, its sale moving its
// result by its own gap over its own risk, the mean of those moves worked once a session and plan.
// see: A trade's entry is priced at the next session's open by a fill step of its own, and no rule's version moves because of it
// see: Every swing trade is judged against a benchmark whose members are sold as the trade is, and no rule's version moves because of it
public sealed class OpenFills(IReadOnlyList<SweepSeries> series, SweepBenchmark.Members members)
{
    readonly ConcurrentDictionary<int, double?> inMoves = new();

    readonly ConcurrentDictionary<int, double?> inCloses = new();

    // The members' sales a walk reads, kept for every walk over the same history and members, since each of a run's walks
    // over them reads the same listings' members again.
    sealed class Sales(IReadOnlyList<SweepSeries> series)
    {
        public ConcurrentDictionary<(int Session, double StopMoves, double? Reward, double? Trail, int Cap, int Exit), double> Plans { get; } = new();

        public ConcurrentDictionary<(int Session, double StopMoves, double Reward), double[]> Pullbacks { get; } = new();

        public Lazy<double[][]> Closes { get; } = new(() => [.. series.Select(one => one.Bars.Select(bar => Statistic.FromPrice(bar.Close)).ToArray())]);
    }

    static readonly ConditionalWeakTable<SweepBenchmark.Members, ConditionalWeakTable<IReadOnlyList<SweepSeries>, Sales>> Shared = new();

    readonly Sales shared = Shared.GetValue(members, _ => new()).GetValue(series, read => new Sales(read));

    ConcurrentDictionary<(int Session, double StopMoves, double? Reward, double? Trail, int Cap, int Exit), double> sales => shared.Plans;

    ConcurrentDictionary<(int Session, double StopMoves, double Reward), double[]> pullbackSales => shared.Pullbacks;

    Lazy<double[][]> closes => shared.Closes;

    // The open of the session after a bar, none where the series holds none.
    public static double? OpenAfter(SweepSeries one, int bar) =>
        bar >= 0 && bar + 1 < one.Bars.Length && one.Bars[bar + 1].Open > 0m ? Statistic.FromPrice(one.Bars[bar + 1].Open) : null;

    // A listing's walk bought at the next open and, from 21.14, sold at the open after the close its stop, its target or
    // its trail signalled, or where a menu's exit names its end, after the close that end and a half's first sale
    // signalled; its sessions the walk's own.
    // see: A swing trade sold on a close's signal is sold at the next session's open by the fill step, and no rule's version moves because of it
    public (double? Result, int Sessions) Exit((double? Result, int Sessions) walked, FamilyListing listing, string? end = null, ExitChoice? exit = null) =>
        Sold(series[listing.Name], Bought(walked, listing), listing, end, exit);

    // A listing's walk already bought at the next open, sold as `Exit` sells it.
    public static (double? Result, int Sessions) Sold(SweepSeries one, (double? Result, int Sessions) bought, FamilyListing listing, string? end = null, ExitChoice? exit = null)
    {
        if (bought.Result is null)
        {
            return bought;
        }

        double Close(int bar) => Statistic.FromPrice(one.Bars[bar].Close);
        double? target = listing.Trails ? null : listing.Target;
        double? trail = listing.Trails ? listing.Trail : null;
        var outcome = new SetupOutcome(bought.Result, bought.Sessions, end ?? ExitFills.EndOf(Close, listing.Bar, listing.Entry, listing.Stop, target, trail, bought.Sessions, listing.Cap));
        var anchor = new SetupAnchor(DateOnly.MinValue, listing.Entry, listing.Stop, target, trail, listing.Cap, null);

        return (Sold(one, outcome, listing.Bar, listing.Entry - listing.Stop, ExitFills.HalfSoldOn(Close, listing.Bar, anchor, exit, outcome)), bought.Sessions);
    }

    // A listing's walk bought at the next open alone, for a walk that already sells at an open, as the dip's does.
    public (double? Result, int Sessions) Bought((double? Result, int Sessions) walked, FamilyListing listing) =>
        (Result(walked.Result, listing.Name, listing.Bar, listing.Entry, listing.Entry - listing.Stop), walked.Sessions);

    // A result bought at the close restated bought at the open of the session after its bar.
    public double? Result(double? atClose, int name, int bar, double close, double risk) =>
        atClose is not null && OpenAfter(series[name], bar) is { } open ? TradeFills.AtOpen(atClose, close, risk, open) : null;

    // A result bought at the open, its path's end at a number of bars from its buy, sold at the open after the close that
    // signalled the end where its end calls for one, a half at half weight where a menu's half exit sold one.
    public double? Sold(SetupOutcome bought, int name, int bar, double risk, int? halfOn = null) => Sold(series[name], bought, bar, risk, halfOn);

    public static double? Sold(SweepSeries one, SetupOutcome bought, int bar, double risk, int? halfOn = null) =>
        ExitFills.Sold(bought, at => Statistic.FromPrice(one.Bars[at].Close), at => OpenAfter(one, at), bar, risk, halfOn).Result;

    // A pullback's result bought at the open under one of its plan's exits, sold at the open after the close that ended
    // it where its stop or its target did, the cap's close standing; its sessions are the calendar's, read to its bar.
    public double? Pullback(double? bought, int name, int bar, SweepPlanOutcomes plan, int exit, double risk) => Pullback(series[name], bought, bar, plan, exit, risk);

    public static double? Pullback(SweepSeries one, double? bought, int bar, SweepPlanOutcomes plan, int exit, double risk)
    {
        if (bought is null)
        {
            return null;
        }

        var ended = Array.BinarySearch(one.SessionAt, one.SessionAt[bar] + plan.Ends[exit]);
        var end = plan.Code[exit] switch
        {
            SweepPlanOutcomes.Win => SetupEnds.Target,
            SweepPlanOutcomes.Loss => SetupEnds.Stop,
            _ => SetupEnds.Cap,
        };

        return ended > bar ? Sold(one, new SetupOutcome(bought, ended - bar, end), bar, risk) : end == SetupEnds.Cap ? bought : null;
    }

    // A listing's benchmark bought at the next open on every member, its risk the plan's stop in typical moves, and from
    // 21.17 each member sold as the listing is under its own exit or the menu's one given.
    public double Benchmark(double atClose, FamilyListing listing, ExitChoice? exit = null)
    {
        if (!(listing.Move > 0) || !(listing.Entry > listing.Stop))
        {
            return double.NaN;
        }

        var risk = listing.Entry - listing.Stop;
        var riskMoves = risk / listing.Move;

        return Benchmark(atClose, listing.Session, riskMoves) + Sale(listing.Session, riskMoves, listing.Trails ? null : (listing.Target - listing.Entry) / risk, listing.Trails ? listing.Trail / risk : null, listing.Cap, exit);
    }

    // A listing's benchmark bought at the next open alone, for a walk whose members already sell at an open of their own,
    // as the dip's do.
    public double BenchmarkBought(double atClose, FamilyListing listing) =>
        listing.Move > 0 ? Benchmark(atClose, listing.Session, (listing.Entry - listing.Stop) / listing.Move) : double.NaN;

    // The benchmark bought at the next open alone, for a plan whose members' sale is added beside it or a walk that sells
    // at an open of its own.
    public double Benchmark(double atClose, int session, double riskMoves) =>
        !double.IsNaN(atClose) && riskMoves > 0 && inMoves.GetOrAdd(session, Gap) is { } gap ? atClose - (gap / riskMoves) : double.NaN;

    // From 21.17 a pullback's plan's benchmark bought at the next open and each member sold as the pullback is under the
    // plan's exit, its hold and whether its stop moves to the buy.
    public double Benchmark(double atClose, int session, double riskMoves, double rewardToRisk, int exit) =>
        Benchmark(atClose, session, riskMoves) + PullbackSale(session, riskMoves, rewardToRisk, exit);

    // The members' sale alone under a pullback's plan, for a plan bought at a fill of its own on a later session.
    public double PullbackSale(int session, double stopMoves, double rewardToRisk, int exit) => PullbackSales(session, stopMoves, rewardToRisk)[exit];

    // The members' sale under a pullback's plan for every exit, worked once a session and plan.
    public double[] PullbackSales(int session, double stopMoves, double rewardToRisk) =>
        pullbackSales.GetOrAdd((session, stopMoves, rewardToRisk), key => PullbackSalesOf(key.Session, key.StopMoves, key.Reward));

    // The mean move the members' sales make to the same plan on every member that session under an exit of the menu or
    // the plan's own, each member's stop its own typical moves under its close and its target or its trail the plan's
    // multiple of its own risk: its walk read again from its closes, and its sale at the open after the close that ended
    // it where its end calls for one, a half's at half weight; nought where no member's walk ended with its open held.
    public double Sale(int session, double stopMoves, double? rewardToRisk, double? trailRisks, int cap, ExitChoice? exit) =>
        sales.GetOrAdd((session, stopMoves, rewardToRisk, trailRisks, cap, exit?.Number ?? 0), key => SaleOf(key.Session, key.StopMoves, key.Reward, key.Trail, key.Cap, exit));

    double SaleOf(int session, double stopMoves, double? rewardToRisk, double? trailRisks, int cap, ExitChoice? exit)
    {
        var read = new List<(SetupOutcome, double?)>(members.Names[session].Length);

        for (var place = 0; place < members.Names[session].Length; place++)
        {
            var (name, bar) = (members.Names[session][place], members.Bars[session][place]);
            var one = series[name];
            var move = one.Atr[bar];
            var entry = closes.Value[name][bar];
            var risk = stopMoves * move;

            if (!(risk > 0) || entry - risk <= 0)
            {
                continue;
            }

            var anchor = new SetupAnchor(DateOnly.MinValue, entry, entry - risk, rewardToRisk is { } reward ? entry + (reward * risk) : null, trailRisks is { } trail ? trail * risk : null, cap, stopMoves);
            var outcome = ExitMenu.Replay(closes.Value[name], bar, anchor, move, exit);
            double Close(int at) => closes.Value[name][at];

            read.Add((outcome, ExitFills.SaleMove(outcome, Close, at => OpenAfter(one, at), bar, risk, ExitFills.HalfSoldOn(Close, bar, anchor, exit, outcome))));
        }

        return ExitFills.SaleMoves(read).Mean;
    }

    // The same for a pullback's plan under every exit, each member walked as the sweep's benchmark walks it, once with its
    // stop standing and once moved: a close under its stop or at its target ends it for every hold not yet reached and is
    // sold at the next open, the stop raised to the buy from the session after a close a risk up where the exit moves it,
    // and a hold's close ends it otherwise, sold at that close; a member whose closes run out before a hold, or whose sale's
    // open is not held, is not counted under it.
    double[] PullbackSalesOf(int session, double stopMoves, double rewardToRisk)
    {
        var holds = SweepAxes.Holds;
        var sums = new double[SweepAxes.Exits];
        var counts = new int[SweepAxes.Exits];

        for (var place = 0; place < members.Names[session].Length; place++)
        {
            var (name, bar) = (members.Names[session][place], members.Bars[session][place]);
            var one = series[name];
            var row = closes.Value[name];
            var entry = row[bar];
            var risk = stopMoves * one.Atr[bar];

            if (!(risk > 0) || !(entry > 0))
            {
                continue;
            }

            for (var move = 0; move < 2; move++)
            {
                var (floor, target, holdAt) = (entry - risk, entry + (rewardToRisk * risk), 0);

                for (var held = 1; held <= holds[^1] && bar + held < row.Length && holdAt < holds.Count; held++)
                {
                    var close = row[bar + held];

                    if (close < floor || close >= target)
                    {
                        if (OpenAfter(one, bar + held) is { } open)
                        {
                            for (var hold = holdAt; hold < holds.Count; hold++)
                            {
                                sums[(hold * 2) + move] += (open - close) / risk;
                                counts[(hold * 2) + move]++;
                            }
                        }

                        break;
                    }

                    if (move == 1 && close >= entry + risk && floor < entry)
                    {
                        floor = entry;
                    }

                    while (holdAt < holds.Count && held == holds[holdAt])
                    {
                        counts[(holdAt * 2) + move]++;
                        holdAt++;
                    }
                }
            }
        }

        return [.. sums.Select((sum, exit) => counts[exit] > 0 ? sum / counts[exit] : 0)];
    }

    // A book's size cut, each holding's result a fraction of its close, moved by the members' gaps over their closes.
    public double? Cut(double? atClose, int session) =>
        atClose is { } held && inCloses.GetOrAdd(session, CutGap) is { } gap ? held - gap : null;

    // The members' mean gap over a session in their own typical moves: the core's benchmark at the open of a plan whose
    // risk is one typical move, read from nothing.
    double? Gap(int session) => Shift(session, (one, bar) => one.Atr[bar]);

    // The same over their closes, which a book's result is a fraction of.
    double? CutGap(int session) => Shift(session, (one, bar) => Statistic.FromPrice(one.Bars[bar].Close));

    double? Shift(int session, Func<SweepSeries, int, double> risk)
    {
        var listed = new List<FillMember>(members.Names[session].Length);

        for (var place = 0; place < members.Names[session].Length; place++)
        {
            var one = series[members.Names[session][place]];
            var bar = members.Bars[session][place];

            listed.Add(new FillMember(true, Statistic.FromPrice(one.Bars[bar].Close), risk(one, bar), OpenAfter(one, bar)));
        }

        return TradeFills.Benchmark(0, listed).Mean is { } moved ? -moved : null;
    }
}
