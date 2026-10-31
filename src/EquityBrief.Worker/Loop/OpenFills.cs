using System.Collections.Concurrent;
using EquityBrief.Core.Ledger;
using EquityBrief.Core.Loop;
using EquityBrief.Core.Prices;
using EquityBrief.Worker.Sweep;

namespace EquityBrief.Worker.Loop;

// The monthly run's trades priced at the next session's open through the core's one pricing function, as every trade the
// lists and books keep is: the tester's procedures, the per-rule test and the books they walk. A trade's walk is the
// sweep's own over the same closes, so it ends where it ends bought at the close, and its result is moved by its own gap
// over the risk its close put up; the benchmark, the same plan on every member that session, is moved by the members' own
// gaps over their own risks, each read in typical moves so a session's is read once whatever the plan. A trade whose
// session after holds no open is not bought, and a session no member holds an open after holds no benchmark. From 21.14 a
// swing trade a close's signal ends is sold at the open after that close, its result moved by that gap over the same
// risk, a walk that sells at an open of its own, as the dip's does, bought alone; the benchmark is not moved by it.
// see: A trade's entry is priced at the next session's open by a fill step of its own, and no rule's version moves because of it
public sealed class OpenFills(IReadOnlyList<SweepSeries> series, SweepBenchmark.Members members)
{
    readonly ConcurrentDictionary<int, double?> inMoves = new();

    readonly ConcurrentDictionary<int, double?> inCloses = new();

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

    // A listing's benchmark bought at the next open on every member, its risk the plan's stop in typical moves.
    public double Benchmark(double atClose, FamilyListing listing) =>
        listing.Move > 0 ? Benchmark(atClose, listing.Session, (listing.Entry - listing.Stop) / listing.Move) : double.NaN;

    public double Benchmark(double atClose, int session, double riskMoves) =>
        !double.IsNaN(atClose) && riskMoves > 0 && inMoves.GetOrAdd(session, Gap) is { } gap ? atClose - (gap / riskMoves) : double.NaN;

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
