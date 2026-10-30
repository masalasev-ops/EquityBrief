using System.Globalization;
using EquityBrief.Core.Families;
using EquityBrief.Core.Ledger;
using EquityBrief.Core.Loop;
using EquityBrief.Core.Prices;
using EquityBrief.Data;
using EquityBrief.Worker.Cards;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Loop;

// From 21.8 a family's ranking variant walked over the ledger: its candidates on a session are the family's setups the
// ledger laid that session at the family's loose gates, the index's floors and the family's defining event kept, ordered
// by the score or the expected value its hooks name and the best five taken as the night takes them, a stock held by a
// trade still open passed over and free the night after that trade ends, each trade the setup's own path with its edge
// less its round trip in risks. The audit's fifths that choose the order are read off the same setups: each settled setup
// entered in a fold's year placed by that fold's score among its own hundredths, and, where the score's top fifth stands
// no higher than its bottom, by the expected value of the setups like it among the year's setups.
// see: A family's ranking variant orders by the learned score where its audit's top fifth stands above the bottom and otherwise by the expected value, and is built nowhere neither orders
public sealed class RankingWalk(string index, string family, IReadOnlyList<RankingWalk.Setup> setups)
{
    // A setup as the walk reads it: the audit's edge is the score audit's, its edge after the cost the night stored or its
    // edge where none was stored, and the walk's is its edge less its round trip from the published table where the night
    // stored none.
    public sealed record Setup(string Ticker, DateOnly Session, DateOnly? Ended, bool Settled, double? AuditEdge, double? WalkEdge, double Entry, double Stop, double?[] Readings);

    static readonly string Setups =
        "SELECT ticker, session_date, ended_on, settled, edge, edge_after_cost, result, cost, entry, stop, company_value, "
        + string.Join(", ", LedgerReadings.All.Select(reading => reading.Column))
        + " FROM setup WHERE index_code = $index AND family = $family AND session_date >= $from ORDER BY session_date, ticker;";

    const int ReadingsFrom = 11;

    // The families a ranking variant is offered for: the gated swing families and the dip.
    public static IReadOnlyList<string> Families { get; } = [SetupFamilies.Pullback, BreakoutRule.Name, DriftRule.Name, DipRule.Name];

    public string Index => index;

    public string Family => family;

    public int Count => setups.Count;

    public IReadOnlyList<Setup> All => setups;

    // The family's setups on the index from a session on, each reading masked where the run may not read it yet.
    public static async Task<RankingWalk> ReadAsync(SqliteConnection connection, string index, string family, DateOnly from, DateOnly through, CancellationToken cancellation)
    {
        var read = new List<Setup>();

        await using var command = connection.CreateCommand();

        command.CommandText = Setups;
        command.Parameters.AddWithValue("$index", index);
        command.Parameters.AddWithValue("$family", family);
        command.Parameters.AddWithValue("$from", from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            var readings = new double?[LedgerReadings.Count];

            for (var at = 0; at < readings.Length; at++)
            {
                readings[at] = reader.IsDBNull(ReadingsFrom + at) || !LedgerReadings.LoopReads(at, through) ? null : reader.GetDouble(ReadingsFrom + at);
            }

            double? edge = reader.IsDBNull(4) ? null : reader.GetDouble(4);
            double? result = reader.IsDBNull(6) ? null : reader.GetDouble(6);
            var entry = Statistic.FromPrice(Money.FromStorage(reader.GetString(8)));
            var stop = Statistic.FromPrice(Money.FromStorage(reader.GetString(9)));
            var afterCost = LikeSetups.AfterCost(ChanceForm.Trailing, result, reader.IsDBNull(7) ? null : reader.GetDouble(7), entry, stop, reader.IsDBNull(10) ? null : reader.GetDouble(10));

            read.Add(new Setup(
                reader.GetString(0),
                Day(reader.GetString(1)),
                reader.IsDBNull(2) ? null : Day(reader.GetString(2)),
                !reader.IsDBNull(3) && reader.GetInt32(3) == 1,
                reader.IsDBNull(5) ? edge : reader.GetDouble(5),
                edge is { } beat && result is { } made && afterCost is { } after ? beat - (made - after) : null,
                entry,
                stop,
                readings));
        }

        return new RankingWalk(index, family, read);
    }

    // The walk over the sessions from one day to another under the hooks given, a ranking by the score holding its ranker
    // and one by the expected value handed the figure: each trade at the place of its session in the calendar given, with
    // the walk's edge.
    public IReadOnlyList<(int Entry, double? Edge)> Walk(RuleHooks hooks, IReadOnlyDictionary<DateOnly, int> places, DateOnly from, DateOnly through, Func<Setup, double?>? expected = null)
    {
        var kept = new List<(int Entry, double? Edge)>();
        var holding = new Dictionary<string, DateOnly?>(StringComparer.Ordinal);

        foreach (var night in setups.Where(setup => setup.Session >= from && setup.Session <= through && places.ContainsKey(setup.Session)).GroupBy(setup => setup.Session))
        {
            foreach (var setup in hooks.Order([.. night], setup => setup.Readings, expected))
            {
                if (holding.TryGetValue(setup.Ticker, out var until) && (until is null || until >= night.Key))
                {
                    continue;
                }

                if (setup.Stop >= setup.Entry || setup.Stop <= 0)
                {
                    continue;
                }

                holding[setup.Ticker] = setup.Ended;
                kept.Add((places[setup.Session], setup.WalkEdge));
            }
        }

        return kept;
    }

    // The walk in each fold's year by that fold's score, or by the expected value of the setups like each matched among
    // the setups ended before the fold's year; none for a fold no score stands for.
    public IReadOnlyList<IReadOnlyList<(int Entry, double? Edge)>?> Folds(RankingOrder order, IReadOnlyList<LoopFold> folds, IReadOnlyDictionary<DateOnly, int> places, Func<int, IScoreModel?> scoreOf, Func<int, Setup, double?> expectedOf) =>
    [
        .. folds.Select(fold => order != RankingOrder.None && scoreOf(fold.Year) is { } model
            ? Walk(
                order == RankingOrder.Score ? Ranking(order).WithRanker(model) : Ranking(order),
                places,
                fold.TestFrom,
                fold.TestThrough,
                order == RankingOrder.Expected ? setup => expectedOf(fold.Year, setup) : null)
            : null),
    ];

    // The expected value of the setups like a setup of the walk's family in a fold's year, matched among the pool's
    // setups ended before the year on that fold's score.
    public Func<int, Setup, double?> ExpectedOf(LikePool like) => (year, setup) => like.ExpectedAt(family, year, setup.Ticker, setup.Readings);

    // The hooks of a ranking by an order.
    public static RuleHooks Ranking(RankingOrder order) =>
        RuleHooks.Of(new Dictionary<string, double>(StringComparer.Ordinal) { [RuleHooks.RankingParameter] = RankingChoice.HookOf(order) ?? 0 });

    // The audit's fifths and the order they choose, over the settled setups entered in a fold's year: each placed by the
    // fold's score among its own hundredths on the index, and by the expected value of the setups like it among the
    // setups the score placed, the second read where the first does not order.
    public (RankingOrder Order, RankingFifths Score, RankingFifths Expected) Choose(Func<int, IScoreModel?> scoreOf, Func<int, Setup, double?> expectedOf)
    {
        var placed = new List<(Setup Setup, int Year, int Group)>();

        foreach (var setup in setups.Where(one => one.Settled))
        {
            if (scoreOf(setup.Session.Year) is { } model && ScoreAudit.Place(model, index, setup.Readings) is { } at)
            {
                placed.Add((setup, setup.Session.Year, at.Group));
            }
        }

        var score = RankingChoice.Fifths(placed.Select(one => (one.Group, one.Setup.AuditEdge)));
        var figured = placed
            .Select(one => (one.Setup, Figure: expectedOf(one.Year, one.Setup)))
            .Where(one => one.Figure is not null)
            .ToArray();
        var groups = RankingChoice.GroupsOf([.. figured.Select(one => one.Figure!.Value)]);
        var expected = RankingChoice.Fifths(figured.Select((one, at) => (groups[at], one.Setup.AuditEdge)));

        return (RankingChoice.Of(score, expected), score, expected);
    }

    // The audit's fifths in a run's words.
    public static string FifthsWords(RankingFifths fifths) =>
        fifths.Bottom is null && fifths.Top is null
            ? "none placed"
            : FormattableString.Invariant($"the top fifth's {fifths.TopSetups} at {Edge(fifths.Top)} against the bottom fifth's {fifths.BottomSetups} at {Edge(fifths.Bottom)}");

    static string Edge(double? edge) => edge is { } held ? FormattableString.Invariant($"{held:+0.000;-0.000} risks") : "none";

    static DateOnly Day(string text) => DateOnly.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture);
}
