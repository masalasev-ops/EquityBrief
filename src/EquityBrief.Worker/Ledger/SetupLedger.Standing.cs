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

// From 21.18, on the operator's ruling of 2026-11-01, the read-only print `ledger-exits --index <index> --standing`: over
// every settled swing setup of the pullback, the breakout and the drift on the index, each bought at the open after its
// session, the mean edge after costs a trade as it is sold now and with its target as a sell order standing from the
// entry, each against the same plan on every member sold the same way, split before 2025 and from it, with how many
// setups each holds and how many whose exit the standing order moves. It reads the store and writes nothing, not even a
// page or a run log row.
// see: Every swing trade is judged against a benchmark whose members are sold as the trade is, and no rule's version moves because of it
public sealed partial class SetupLedger
{
    public const string StandingFlag = "--standing";

    // One setup priced both ways, each edge after its stored cost against its own benchmark.
    public sealed record StandingPriced(string Family, int Year, double? EdgeNow, double? EdgeStanding, bool Moved);

    // One family's figures over a span: the setups both edges were read for, each mean edge, and how many moved.
    public sealed record StandingGroup(string Family, string Span, int Setups, double? EdgeNow, double? EdgeStanding, int Moved);

    // The spans each family is split over: before 2025, from 2025, and every year.
    public static IReadOnlyList<StandingGroup> StandingGroups(IReadOnlyList<StandingPriced> setups, IReadOnlyList<string> order)
    {
        var groups = new List<StandingGroup>();

        foreach (var family in order)
        {
            foreach (var (span, keep) in new (string, Func<int, bool>)[] { ("before 2025", year => year < 2025), ("from 2025", year => year >= 2025), ("every year", _ => true) })
            {
                var own = setups.Where(setup => setup.Family == family && keep(setup.Year) && setup.EdgeNow is not null && setup.EdgeStanding is not null).ToArray();

                groups.Add(new StandingGroup(
                    family,
                    span,
                    own.Length,
                    own.Length == 0 ? null : own.Average(setup => setup.EdgeNow!.Value),
                    own.Length == 0 ? null : own.Average(setup => setup.EdgeStanding!.Value),
                    own.Count(setup => setup.Moved)));
            }
        }

        return groups;
    }

    public async Task<int> StandingAsync(string index, TextWriter output, CancellationToken cancellation = default)
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
                    reader.GetString(0), reader.GetString(1), Date(reader.GetString(2)), Price(3) ?? 0, Price(4) ?? 0, Price(5), Price(6),
                    reader.GetInt32(7), Real(8), Real(9), Real(10), Real(11), Real(12), reader.IsDBNull(13) ? string.Empty : reader.GetString(13), Real(14)));
            }
        }

        if (settled.Count == 0 || await ScalarAsync(connection, NewestSession, [], cancellation) is not string newest)
        {
            output.WriteLine($"{ExitsStage}: the ledger holds no settled swing setup or the bar store no session on the {DecisionCards.NameOf(index)}, so nothing was priced");

            return 0;
        }

        var prepared = await PrepareAsync(connection, index, Date(newest), output, cancellation, heavyweightsRead: false);
        var priced = new StandingPriced?[settled.Count];
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

            if (!(bar + 1 < opens[name].Length && bar + 1 < closes.Length && opens[name][bar + 1] > 0))
            {
                return;
            }

            var fill = opens[name][bar + 1];
            var factor = setup.Entry > 0 ? closes[bar] / setup.Entry : 1;
            var anchor = new SetupAnchor(setup.Session, closes[bar], setup.Stop * factor, setup.Target * factor, setup.Trail * factor, setup.Cap, setup.RiskMoves);

            if (ExitPricings.Price(closes, opens[name], HighsOf(name), bar, anchor, fill) is not { } three
                || StandingTarget.Sold(closes, opens[name], HighsOf(name), bar, anchor, fill) is not { } standing)
            {
                return;
            }

            var move = prepared.Series[name].Atr[bar];
            var plan = anchor.Trails
                ? new SetupPlan(anchor.RiskMoves ?? anchor.Risk / move, null, move > 0 ? anchor.Trail!.Value / move : anchor.Trail!.Value, anchor.Cap)
                : new SetupPlan(anchor.RiskMoves ?? anchor.Risk / move, anchor.Target is { } aim ? (aim - anchor.Entry) / anchor.Risk : null, null, anchor.Cap);
            var slots = prepared.Members.Names[at];
            var now = ExitFills.Benchmark(
                [.. slots.Select((member, slot) => (prepared.Closes[member], opens[member], prepared.Members.Bars[at][slot], prepared.Series[member].Atr[prepared.Members.Bars[at][slot]], prepared.Stopped[member]))],
                setup.Session,
                plan);
            var standingBenchmark = StandingTarget.Benchmark(
                [.. slots.Select((member, slot) => (prepared.Closes[member], opens[member], HighsOf(member), prepared.Members.Bars[at][slot], prepared.Series[member].Atr[prepared.Members.Bars[at][slot]]))],
                setup.Session,
                plan);
            var cost = setup.Cost ?? 0;
            double? edgeNow = three.AtNextOpen.Result is { } made && now is { } against ? made - against - cost : null;
            double? edgeStanding = standing.Result is { } sold && standingBenchmark is { } standingAgainst ? sold - standingAgainst - cost : null;
            var moved = standing.End != three.AtNextOpen.End || standing.Result is not { } a || three.AtNextOpen.Result is not { } b || Math.Abs(a - b) > 1e-9;

            priced[place] = new StandingPriced(setup.Family, setup.Session.Year, edgeNow, edgeStanding, moved);
        });

        foreach (var group in StandingGroups([.. priced.OfType<StandingPriced>()], [SetupFamilies.Pullback, BreakoutRule.Name, DriftRule.Name]))
        {
            output.WriteLine(string.Create(CultureInfo.InvariantCulture, $"{ExitsStage}: {DecisionCards.NameOf(index)}, {group.Family}, {group.Span}: {group.Setups:N0} setup(s); mean edge after costs a trade {Signed(group.EdgeNow)} sold as now and {Signed(group.EdgeStanding)} with the target standing, each against the same plan on every member sold the same way; {group.Moved:N0} of {group.Setups:N0} ({(group.Setups > 0 ? 100.0 * group.Moved / group.Setups : 0):0.0} per cent) whose exit the standing order moves"));
        }

        output.WriteLine($"{ExitsStage}: printed and nothing written");

        return 0;
    }

    static string Signed(double? value) => value is { } held ? held.ToString("+0.000;-0.000;0.000", CultureInfo.InvariantCulture) : "none";
}
