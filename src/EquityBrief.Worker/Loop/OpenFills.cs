using System.Collections.Concurrent;
using EquityBrief.Core.Ledger;
using EquityBrief.Core.Prices;
using EquityBrief.Worker.Sweep;

namespace EquityBrief.Worker.Loop;

// The monthly run's trades priced at the next session's open through the core's one pricing function, as every trade the
// lists and books keep is: the tester's procedures, the per-rule test and the books they walk. A trade's walk is the
// sweep's own over the same closes, so it ends where it ends bought at the close, and its result is moved by its own gap
// over the risk its close put up; the benchmark, the same plan on every member that session, is moved by the members' own
// gaps over their own risks, each read in typical moves so a session's is read once whatever the plan. A trade whose
// session after holds no open is not bought, and a session no member holds an open after holds no benchmark.
// see: A trade's entry is priced at the next session's open by a fill step of its own, and no rule's version moves because of it
public sealed class OpenFills(IReadOnlyList<SweepSeries> series, SweepBenchmark.Members members)
{
    readonly ConcurrentDictionary<int, double?> inMoves = new();

    readonly ConcurrentDictionary<int, double?> inCloses = new();

    // The open of the session after a bar, none where the series holds none.
    public static double? OpenAfter(SweepSeries one, int bar) =>
        bar >= 0 && bar + 1 < one.Bars.Length && one.Bars[bar + 1].Open > 0m ? Statistic.FromPrice(one.Bars[bar + 1].Open) : null;

    // A listing's walk bought at the next open, its sessions the walk's own.
    public (double? Result, int Sessions) Exit((double? Result, int Sessions) walked, FamilyListing listing) =>
        (Result(walked.Result, listing.Name, listing.Bar, listing.Entry, listing.Entry - listing.Stop), walked.Sessions);

    // A result bought at the close restated bought at the open of the session after its bar.
    public double? Result(double? atClose, int name, int bar, double close, double risk) =>
        atClose is not null && OpenAfter(series[name], bar) is { } open ? TradeFills.AtOpen(atClose, close, risk, open) : null;

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
