using System.Collections.Concurrent;
using System.Globalization;
using EquityBrief.Core.Families;
using EquityBrief.Core.Ledger;
using EquityBrief.Core.Prices;
using EquityBrief.Data;
using EquityBrief.Worker.Cards;
using EquityBrief.Worker.Indices;
using EquityBrief.Worker.Loop;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Ledger;

// 21.3's exits check by hand, over one index's settled swing setups: each priced three ways from the history's bars with
// its stored stop, target, trail and cap, bought at the open after its session as the ledger buys it, sold at the close
// that triggered its exit as the ledger sells it, sold at the open of the session after that close, and with the target
// as a resting order filled on the first session whose high reaches it, the stop still a close and sold at the next
// open; the time limit at its own close either way. The short-term dip is shown as it already prices, sold at the next
// open. It reads the store and writes nothing to it, its answer an HTML page with its figures beside it under the data
// root's loop folder; a change it suggests waits for a fill step of its own on the operator's word.
// see: A read-only check changes no rule, and an exit's pricing changes only by a fill step on the operator's word
public sealed partial class SetupLedger
{
    public const string ExitsStage = "ledger-exits";

    const string SettledSwings = @"
        SELECT family, ticker, session_date, entry, stop, target, trail, cap, risk_moves, result, benchmark, cost, edge_after_cost, ""end"", edge
        FROM setup WHERE index_code = $index AND settled = 1 AND family IN ('pullback', 'breakout', 'drift', 'dip') ORDER BY session_date, family, ticker;";

    sealed record SettledSwing(string Family, string Ticker, DateOnly Session, double Entry, double Stop, double? Target, double? Trail, int Cap, double? RiskMoves, double? Result, double? Benchmark, double? Cost, double? EdgeAfterCost, string End, double? Edge);

    // One setup's three pricings with the stored result they are set beside, each edge after costs its result less the
    // stored benchmark and the stored cost, so only the sale moves between the three.
    public sealed record ExitPricedSetup(string Family, int Year, double? Stored, ExitPriced Priced, double? Benchmark, double? Cost)
    {
        public double? EdgeAtClose => Edge(Priced.AtClose.Result);

        public double? EdgeAtNextOpen => Edge(Priced.AtNextOpen.Result);

        public double? EdgeResting => Edge(Priced.Resting.Result);

        double? Edge(double? result) => result is { } made && Benchmark is { } benchmark ? made - benchmark - (Cost ?? 0) : null;
    }

    // One family's priced setups on an index over a year, or over every year where the year is none: the mean result and
    // the mean edge after costs under each pricing, each over the same setups so a difference is paired, and how many ended
    // at the target under the close's pricing and under the resting order.
    public sealed record ExitGroup(
        string Family,
        int? Year,
        int Priced,
        double? ResultAtClose,
        double? ResultAtNextOpen,
        double? ResultResting,
        int EdgesPriced,
        double? EdgeAtClose,
        double? EdgeAtNextOpen,
        double? EdgeResting,
        int TargetsAtClose,
        int TargetsResting,
        int StopsAtClose,
        int StopsResting)
    {
        public double? ResultNextOpenLessClose => ResultAtNextOpen - ResultAtClose;

        public double? ResultRestingLessClose => ResultResting - ResultAtClose;

        public double? EdgeNextOpenLessClose => EdgeAtNextOpen - EdgeAtClose;

        public double? EdgeRestingLessClose => EdgeResting - EdgeAtClose;
    }

    // Each family's figures over every year and then year by year, in the order given.
    public static IReadOnlyList<ExitGroup> ExitGroups(IReadOnlyList<ExitPricedSetup> setups, IReadOnlyList<string> order)
    {
        var groups = new List<ExitGroup>();

        foreach (var family in order.Where(family => setups.Any(setup => setup.Family == family)))
        {
            var own = setups.Where(setup => setup.Family == family).ToArray();

            groups.Add(Group(family, null, own));

            foreach (var year in own.Select(setup => setup.Year).Distinct().OrderBy(year => year))
            {
                groups.Add(Group(family, year, [.. own.Where(setup => setup.Year == year)]));
            }
        }

        return groups;
    }

    static ExitGroup Group(string family, int? year, IReadOnlyList<ExitPricedSetup> own)
    {
        var priced = own.Where(setup => setup.Priced.AtClose.Result is not null && setup.Priced.AtNextOpen.Result is not null && setup.Priced.Resting.Result is not null).ToArray();
        var edged = priced.Where(setup => setup.EdgeAtClose is not null).ToArray();

        static double? Mean(IEnumerable<double?> values)
        {
            var held = values.OfType<double>().ToArray();

            return held.Length == 0 ? null : held.Average();
        }

        return new ExitGroup(
            family,
            year,
            priced.Length,
            Mean(priced.Select(setup => setup.Priced.AtClose.Result)),
            Mean(priced.Select(setup => setup.Priced.AtNextOpen.Result)),
            Mean(priced.Select(setup => setup.Priced.Resting.Result)),
            edged.Length,
            Mean(edged.Select(setup => setup.EdgeAtClose)),
            Mean(edged.Select(setup => setup.EdgeAtNextOpen)),
            Mean(edged.Select(setup => setup.EdgeResting)),
            priced.Count(setup => setup.Priced.AtClose.End == SetupEnds.Target),
            priced.Count(setup => setup.Priced.Resting.End == SetupEnds.Target),
            priced.Count(setup => setup.Priced.AtClose.End is SetupEnds.Stop or SetupEnds.Trail),
            priced.Count(setup => setup.Priced.Resting.End is SetupEnds.Stop or SetupEnds.Trail));
    }

    public async Task<int> ExitsAsync(string index, TextWriter output, CancellationToken cancellation = default)
    {
        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        var settled = new List<SettledSwing>();

        await using (var command = Command(connection, null, SettledSwings, [("$index", index)]))
        await using (var reader = await command.ExecuteReaderAsync(cancellation))
        {
            while (await reader.ReadAsync(cancellation))
            {
                double? Price(int column) => reader.IsDBNull(column) ? null : Statistic.FromPrice(Money.FromStorage(reader.GetString(column)));
                double? Real(int column) => reader.IsDBNull(column) ? null : reader.GetDouble(column);

                settled.Add(new SettledSwing(
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
                    Real(11),
                    Real(12),
                    reader.IsDBNull(13) ? string.Empty : reader.GetString(13),
                    Real(14)));
            }
        }

        if (settled.Count == 0)
        {
            output.WriteLine($"{ExitsStage}: the ledger holds no settled swing setup on the {DecisionCards.NameOf(index)}, so nothing was priced");

            return 0;
        }

        if (await ScalarAsync(connection, NewestSession, [], cancellation) is not string newest)
        {
            output.WriteLine($"{ExitsStage}: the bar store holds no session, so nothing was priced");

            return 0;
        }

        var prepared = await PrepareAsync(connection, index, Date(newest), output, cancellation, heavyweightsRead: false);
        var priced = new ExitPricedSetup?[settled.Count];
        var near = new bool[settled.Count];
        var highs = new ConcurrentDictionary<int, double[]>();
        var opens = prepared.Opens;

        double[] HighsOf(int name) => highs.GetOrAdd(name, key => [.. prepared.Series[key].Bars.Select(bar => Statistic.FromPrice(bar.High))]);

        Parallel.For(0, settled.Count, place =>
        {
            var setup = settled[place];

            if (setup.Family == DipRule.Name)
            {
                return;
            }

            var at = Array.BinarySearch(prepared.Calendar, setup.Session);

            if (at < 0 || !prepared.Places.TryGetValue(setup.Ticker, out var name) || IndexNightRead.BarOf(prepared.Series[name], at) is not (>= 0 and var bar))
            {
                return;
            }

            var closes = prepared.Closes[name];
            double? open = bar + 1 < opens[name].Length && bar + 1 < closes.Length ? opens[name][bar + 1] : null;

            // The stored plan in the history's prices: the same stop, target and trail, scaled by the history's close on the
            // session over the buy the row stored, as the next-open check scales them.
            var factor = setup.Entry > 0 ? closes[bar] / setup.Entry : 1;
            var anchor = new SetupAnchor(setup.Session, closes[bar], setup.Stop * factor, setup.Target * factor, setup.Trail * factor, setup.Cap, setup.RiskMoves);

            if (ExitPricings.Price(closes, opens[name], HighsOf(name), bar, anchor, open) is not { } three)
            {
                return;
            }

            priced[place] = new ExitPricedSetup(setup.Family, setup.Session.Year, setup.Result, three, setup.Benchmark, setup.Cost);
            near[place] = three.AtClose.Result is { } worked && setup.Result is { } stored && Math.Abs(worked - stored) <= NearWithin;
        });

        var read = priced.OfType<ExitPricedSetup>().ToArray();
        var swings = settled.Count(setup => setup.Family != DipRule.Name);
        var dips = settled.Where(setup => setup.Family == DipRule.Name).ToArray();
        var groups = ExitGroups(read, [SetupFamilies.Pullback, BreakoutRule.Name, DriftRule.Name]);
        var lines = new List<string>
        {
            FormattableString.Invariant($"{read.Length} of {swings} settled swing setup(s) on the {DecisionCards.NameOf(index)} priced three ways over the history read through {newest}, {swings - read.Length} whose stock, session or next open the history does not hold left out; {near.Count(same => same)} of the {read.Length} reproduce the stored result at the close within {NearWithin}."),
            "Each result is in multiples of the risk the plan put up at the listing's close, bought at the open after it. The edge after costs under each pricing is its result less the stored benchmark and the stored cost, so only the sale moves between the three.",
            "Under the resting order a session whose high reaches the target sells at the target before its close is read, so a session reaching the target and closing under the stop sells at the target; a plan whose stop trails rests no order.",
        };

        if (dips.Length > 0)
        {
            var dipResults = dips.Select(dip => dip.Result).OfType<double>().ToArray();
            var dipEdges = dips.Select(dip => dip.EdgeAfterCost ?? dip.Edge).OfType<double>().ToArray();

            lines.Add(FormattableString.Invariant($"The short-term dip is shown as it already prices, sold at the next open: {dips.Length} settled setup(s), mean result {(dipResults.Length == 0 ? "-" : dipResults.Average().ToString("+0.000;-0.000", CultureInfo.InvariantCulture))} risks, mean edge after costs, its edge where the night stored no cost, {(dipEdges.Length == 0 ? "-" : dipEdges.Average().ToString("+0.000;-0.000", CultureInfo.InvariantCulture))} over {dipEdges.Length}."));
        }

        foreach (var line in lines)
        {
            output.WriteLine($"{ExitsStage}: {line}");
        }

        var rows = groups.Select(group => (IReadOnlyList<string>)
        [
            group.Family,
            group.Year is { } year ? CheckReports.Cell(year) : "all years",
            CheckReports.Cell(group.Priced),
            CheckReports.Cell(group.ResultAtClose, signed: true),
            CheckReports.Cell(group.ResultAtNextOpen, signed: true),
            CheckReports.Cell(group.ResultResting, signed: true),
            CheckReports.Cell(group.ResultNextOpenLessClose, signed: true),
            CheckReports.Cell(group.ResultRestingLessClose, signed: true),
            CheckReports.Cell(group.EdgesPriced),
            CheckReports.Cell(group.EdgeAtClose, signed: true),
            CheckReports.Cell(group.EdgeAtNextOpen, signed: true),
            CheckReports.Cell(group.EdgeResting, signed: true),
            CheckReports.Cell(group.EdgeNextOpenLessClose, signed: true),
            CheckReports.Cell(group.EdgeRestingLessClose, signed: true),
            CheckReports.Cell(group.TargetsAtClose),
            CheckReports.Cell(group.TargetsResting),
            CheckReports.Cell(group.StopsAtClose),
            CheckReports.Cell(group.StopsResting),
        ]).ToArray();

        foreach (var group in groups)
        {
            output.WriteLine(FormattableString.Invariant($"{ExitsStage}: {group.Family}, {(group.Year is { } year ? year.ToString(CultureInfo.InvariantCulture) : "all years")}: {group.Priced} priced; mean result {CheckReports.Cell(group.ResultAtClose, true)} at the close, {CheckReports.Cell(group.ResultAtNextOpen, true)} at the next open, {CheckReports.Cell(group.ResultResting, true)} with the target resting; mean edge after costs over {group.EdgesPriced}: {CheckReports.Cell(group.EdgeAtClose, true)}, {CheckReports.Cell(group.EdgeAtNextOpen, true)}, {CheckReports.Cell(group.EdgeResting, true)}; targets reached {group.TargetsAtClose} at the close against {group.TargetsResting} resting"));
        }

        var (report, figures) = CheckReports.Write(
            Path.GetDirectoryName(Path.GetFullPath(databaseFile))!,
            FormattableString.Invariant($"exits-{index}"),
            $"Exits priced three ways on the {DecisionCards.NameOf(index)}",
            lines,
            [new CheckReports.Table(
                "By family and year: results and edges after costs under each pricing, in risks",
                ["Family", "Year", "Priced", "Result at close", "Result at next open", "Result resting", "Next open less close", "Resting less close", "With a benchmark", "Edge at close", "Edge at next open", "Edge resting", "Edge next open less close", "Edge resting less close", "Targets at close", "Targets resting", "Stops at close", "Stops resting"],
                rows)],
            new { index, through = newest, settled = settled.Count, swings, priced = read.Length, reproduced = near.Count(same => same), groups, dip = new { settled = dips.Length } },
            clock.UtcNow);

        output.WriteLine($"{ExitsStage}: written to {report} with its figures at {figures}; no store row written");

        return 0;
    }
}
