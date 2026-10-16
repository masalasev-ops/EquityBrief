using System.Collections.Concurrent;
using System.Globalization;
using EquityBrief.Core.Families;
using EquityBrief.Core.Ledger;
using EquityBrief.Core.Prices;
using EquityBrief.Data;
using EquityBrief.Worker.Cards;
using EquityBrief.Worker.Indices;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Ledger;

// The next-open check by hand, over one index's settled setups: each priced at the close of the night that listed it, as
// the ledger prices it, and at the next session's open with the same stop, target, trail and cap, over the history the
// build reads, read through the bar store's newest session; and the differences stated by family and year with how often
// the next open fell under the stop or over the target. It reads the store and writes nothing, not even a run log row.
public sealed partial class SetupLedger
{
    public const string NextOpenStage = "ledger-next-open";

    const string SettledSetups = @"
        SELECT family, ticker, session_date, entry, stop, target, trail, cap, risk_moves, result, edge, sessions
        FROM setup WHERE index_code = $index AND settled = 1 ORDER BY session_date, family, ticker;";

    // How near a figure worked again must come to the stored one to count as the same: the pulled years are priced by
    // the median ratio over the sessions the pull and the store both hold, which a later read moves by a rounding.
    const double SameWithin = 1e-6;

    const double NearWithin = 1e-3;

    sealed record SettledSetup(string Family, string Ticker, DateOnly Session, double Entry, double Stop, double? Target, double? Trail, int Cap, double? RiskMoves, double? Result, double? Edge, int Sessions);

    public async Task<int> NextOpenAsync(string index, TextWriter output, CancellationToken cancellation = default)
    {
        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        var settled = new List<SettledSetup>();

        await using (var command = Command(connection, null, SettledSetups, [("$index", index)]))
        await using (var reader = await command.ExecuteReaderAsync(cancellation))
        {
            while (await reader.ReadAsync(cancellation))
            {
                double? Price(int column) => reader.IsDBNull(column) ? null : Statistic.FromPrice(Money.FromStorage(reader.GetString(column)));
                double? Real(int column) => reader.IsDBNull(column) ? null : reader.GetDouble(column);

                settled.Add(new SettledSetup(
                    reader.GetString(0),
                    reader.GetString(1),
                    Date(reader.GetString(2)),
                    Price(3) ?? 0,
                    Price(4) ?? 0,
                    Price(5),
                    Price(6),
                    reader.GetInt32(7),
                    Real(8),
                    Real(9),
                    Real(10),
                    reader.IsDBNull(11) ? 0 : reader.GetInt32(11)));
            }
        }

        if (settled.Count == 0)
        {
            output.WriteLine($"{NextOpenStage}: the ledger holds no settled setup on the {DecisionCards.NameOf(index)}, so nothing was priced");

            return 0;
        }

        if (await ScalarAsync(connection, NewestSession, [], cancellation) is not string newest)
        {
            output.WriteLine($"{NextOpenStage}: the bar store holds no session, so nothing was priced");

            return 0;
        }

        var prepared = await PrepareAsync(connection, index, Date(newest), output, cancellation, heavyweightsRead: settled.Any(setup => setup.Family == HeavyweightRule.Name));
        var priced = new NextOpenPricing?[settled.Count];
        var sameResult = new bool[settled.Count];
        var sameEdge = new bool?[settled.Count];
        var nearResult = new bool[settled.Count];
        var nearEdge = new bool[settled.Count];
        var benchmarks = new ConcurrentDictionary<(int At, SetupPlan Plan), (BenchmarkReading Close, BenchmarkReading Open)>();
        var opens = prepared.Opens;

        Parallel.For(0, settled.Count, place =>
        {
            var setup = settled[place];
            var at = Array.BinarySearch(prepared.Calendar, setup.Session);

            if (at < 0 || !prepared.Places.TryGetValue(setup.Ticker, out var name) || IndexNightRead.BarOf(prepared.Series[name], at) is not (>= 0 and var bar))
            {
                return;
            }

            var closes = prepared.Closes[name];
            double? open = bar + 1 < opens[name].Length && bar + 1 < closes.Length ? opens[name][bar + 1] : null;
            (double? Result, BenchmarkReading? Benchmark) atClose;
            (double? Result, BenchmarkReading? Benchmark) atOpen;
            NextOpenGap gap;

            if (setup.Family == HeavyweightRule.Name)
            {
                gap = open is > 0 ? NextOpenGap.Inside : NextOpenGap.NoOpen;

                var members = prepared.Heavyweights.TryGetValue(at, out var cuts) && cuts.FirstOrDefault(cut => cut.Largest.Contains(setup.Ticker, StringComparer.Ordinal)) is { } sector
                    ? sector.Largest
                        .Where(prepared.Places.ContainsKey)
                        .Select(ticker => prepared.Places[ticker])
                        .Select(member => (prepared.Closes[member], opens[member], IndexNightRead.BarOf(prepared.Series[member], at), prepared.Stopped[member]))
                        .ToArray()
                    : null;

                atClose = (
                    bar + setup.Sessions < closes.Length && closes[bar] > 0 ? (closes[bar + setup.Sessions] / closes[bar]) - 1 : null,
                    members is null ? null : HeavyweightPaths.Cut([.. members.Select(member => (member.Item1, member.Item3, member.Item4))], setup.Sessions));
                atOpen = (
                    NextOpenCheck.HeavyweightResult(closes, bar, setup.Sessions, open),
                    members is null ? null : NextOpenCheck.HeavyweightCut(members, setup.Sessions));
            }
            else
            {
                // The stored plan in the history's prices: the same stop, target and trail, scaled by the history's
                // close on the session over the buy the row stored.
                var factor = setup.Entry > 0 ? closes[bar] / setup.Entry : 1;
                var anchor = new SetupAnchor(setup.Session, closes[bar], setup.Stop * factor, setup.Target * factor, setup.Trail * factor, setup.Cap, setup.RiskMoves);
                var plan = PlanOf(new OpenSetup(setup.Family, setup.Ticker, setup.Session, anchor, null, SetupEnds.Open), prepared.Series[name].Atr[bar]);
                var (closeBenchmark, openBenchmark) = benchmarks.GetOrAdd((at, plan), key => Benchmarks(prepared, opens, key.At, setup.Session, key.Plan));

                gap = NextOpenCheck.Gap(anchor, open);
                atClose = (SetupReplay.Replay(closes, bar, anchor).Result, closeBenchmark);
                atOpen = (NextOpenCheck.Replay(closes, bar, anchor, open)?.Result, openBenchmark);
            }

            double? Edge((double? Result, BenchmarkReading? Benchmark) read) =>
                read.Result is { } result && read.Benchmark is { Settled: true, Mean: { } mean } ? result - mean : null;

            var edgeAtClose = Edge(atClose);
            var edgeAtOpen = Edge(atOpen);

            // From 19.10's move the ledger stores each setup's result and edge at the open, so those are what the store
            // is held to.
            priced[place] = new NextOpenPricing(setup.Family, setup.Session.Year, gap, atClose.Result, edgeAtClose, atOpen.Result, edgeAtOpen);
            sameResult[place] = atOpen.Result is { } worked && setup.Result is { } stored && Math.Abs(worked - stored) <= SameWithin * Math.Max(1, Math.Abs(stored));
            sameEdge[place] = setup.Edge is { } storedEdge ? edgeAtOpen is { } workedEdge && Math.Abs(workedEdge - storedEdge) <= SameWithin * Math.Max(1, Math.Abs(storedEdge)) : null;
            nearResult[place] = atOpen.Result is { } near && setup.Result is { } held && Math.Abs(near - held) <= NearWithin;
            nearEdge[place] = setup.Edge is { } heldEdge && edgeAtOpen is { } nearEdgeWorked && Math.Abs(nearEdgeWorked - heldEdge) <= NearWithin;
        });

        var read = priced.OfType<NextOpenPricing>().ToArray();

        foreach (var group in NextOpenCheck.Groups(read, [.. LedgerSetups.Families, HeavyweightRule.Name]))
        {
            output.WriteLine($"{NextOpenStage}: {Line(index, group)}");
        }

        var edges = sameEdge.Count(same => same is not null);

        output.WriteLine(FormattableString.Invariant(
            $"{NextOpenStage}: {read.Length} of {settled.Count} settled setup(s) on the {DecisionCards.NameOf(index)} priced over the history read through {newest}, {settled.Count - read.Length} whose stock or session the history does not hold; {sameResult.Count(same => same)} of {settled.Count} results at the open worked again as stored to a millionth and {nearResult.Count(near => near)} to a thousandth, and {sameEdge.Count(same => same == true)} of the {edges} stored edges to a millionth and {nearEdge.Count(near => near)} to a thousandth; nothing written"));

        return 0;
    }

    // The benchmark both ways for one session and plan, over the members the history held on the session.
    static (BenchmarkReading Close, BenchmarkReading Open) Benchmarks(PreparedHistory prepared, double[][] opens, int at, DateOnly session, SetupPlan plan)
    {
        var names = prepared.Members.Names[at];
        var bars = prepared.Members.Bars[at];
        var atClose = new List<(double[] Closes, int Bar, double Move, bool Stopped)>(names.Length);
        var atOpen = new List<(double[] Closes, double[] Opens, int Bar, double Move, bool Stopped)>(names.Length);

        for (var place = 0; place < names.Length; place++)
        {
            var member = names[place];
            var move = prepared.Series[member].Atr[bars[place]];

            atClose.Add((prepared.Closes[member], bars[place], move, prepared.Stopped[member]));
            atOpen.Add((prepared.Closes[member], opens[member], bars[place], move, prepared.Stopped[member]));
        }

        return (SetupBenchmark.Of(atClose, session, plan), NextOpenCheck.Benchmark(atOpen, session, plan));
    }

    // One group's line: a swing family's figures in multiples of the risk its plan put up at the listing's close, the
    // heavyweights' as fractions of the sum that close put to the stock.
    public static string Line(string index, NextOpenGroup group)
    {
        var book = group.Family == HeavyweightRule.Name;
        var unit = book ? "as a fraction of the sum at the close" : "in the close's risks";
        var places = book ? "0.0000" : "0.000";
        var gaps = book
            ? FormattableString.Invariant($"the next open not held {group.NoOpen}, the book placing no stop or target")
            : FormattableString.Invariant($"the next open at or under the stop {group.UnderStop}, at or over the target {group.OverTarget}, not held {group.NoOpen}");

        string Figure(double? value) => value is { } held ? held.ToString(places, CultureInfo.InvariantCulture) : "none";

        string Change(double? value) => value is { } held ? held.ToString($"+{places};-{places};{places}", CultureInfo.InvariantCulture) : "none";

        return FormattableString.Invariant(
            $"the {DecisionCards.NameOf(index)}'s {group.Family}, {(group.Year is { } year ? year.ToString(CultureInfo.InvariantCulture) : "every year")}: {group.Settled} settled, {group.Priced} priced both ways, the mean result {unit} {Figure(group.ResultAtClose)} at the close and {Figure(group.ResultAtOpen)} at the next open ({Change(group.ResultDifference)}); the mean edge over {group.EdgesPriced} {Figure(group.EdgeAtClose)} and {Figure(group.EdgeAtOpen)} ({Change(group.EdgeDifference)}); {gaps}");
    }
}
