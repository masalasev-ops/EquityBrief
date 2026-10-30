using System.Globalization;
using EquityBrief.Core.Components;
using EquityBrief.Core.Families;
using EquityBrief.Core.Ledger;
using EquityBrief.Core.Loop;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using EquityBrief.Worker.Cards;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Loop;

// 21.3's score bucket audit by hand, over one index: each settled swing setup entered in a tester fold's year is scored
// by that fold's stored model, ridge and, where one stands, trees, which learned only on paths that ended before the
// year, placed among the model's own stored hundredths on the index into five groups of a fifth, and each group states
// its setups, the share winning, the mean edge after costs in risks and the rule's own picks among them; the setups since
// the first monthly run are scored the same way by the newest model fitted before their night. It reads the store and
// writes nothing to it, its answer an HTML page with its figures beside it under the data root's loop folder.
// see: A read-only check changes no rule, and an exit's pricing changes only by a fill step on the operator's word
public sealed class ScoreAudit(IClock clock, string databaseFile, string dataRoot, TextWriter output) : IComponent
{
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.Setup, Touch.Read),
            new StoreTouch(Store.LoopModel, Touch.Read),
            new StoreTouch(Store.LoopRun, Touch.Read),
        ],
        Feeds: []);

    public const string Verb = "ledger-score-audit";

    // Five groups of the model's hundredths, each twenty wide: the first under the 20th, the last at or over the 80th.
    public const int Groups = 5;

    public const int GroupWidth = 20;

    public const string Folds = "the test years, each setup scored by its fold's model";

    public const string SinceFirstRun = "since the first monthly run, each setup scored by the newest model fitted before its night";

    const string Runs = "SELECT run_id, through, started_at FROM loop_run WHERE index_code = $index ORDER BY started_at;";

    const string Models = "SELECT run_id, family, kind, year, parameters FROM loop_model WHERE index_code = $index;";

    static readonly string SettledSwings =
        "SELECT family, session_date, picked, live_pass, result, edge, edge_after_cost, " + string.Join(", ", LedgerReadings.All.Select(reading => reading.Column))
        + " FROM setup WHERE index_code = $index AND settled = 1 AND family IN ('pullback', 'breakout', 'drift', 'dip') ORDER BY session_date, family, ticker;";

    sealed record Run(string Id, DateOnly Through, DateOnly Started);

    sealed record Stored(string RunId, string Family, string Kind, int Year, IScoreModel Model);

    sealed record SettledSetup(string Family, DateOnly Session, bool Picked, bool LivePass, double? Result, double? Edge, double?[] Readings);

    // A setup placed: its family, the model's kind, the year it was entered in, its group, whether the rule's own list held
    // it and whether the live rule passed it, what it came to and its edge after costs.
    public sealed record Placed(string Family, string Kind, int Year, double Score, int Rank, int Group, bool Picked, bool LivePass, double? Result, double? Edge);

    // One group's figures for a family, kind and population.
    public sealed record GroupRow(string Family, string Kind, string Population, int Group, int Setups, int Winning, double? MeanEdge, int Picked, int Passed)
    {
        public double? WinningShare => Setups == 0 ? null : Winning * 1.0 / Setups;
    }

    // The group a rank falls in: the last hundredth at or under the score, over twenty, the top group taking the hundredth
    // of a hundred as well.
    public static int GroupOf(int rank) => Math.Min(Groups - 1, Math.Max(0, rank) / GroupWidth);

    // A setup's score under a model, its rank among the model's own hundredths on the index and its group; none where a
    // reading the model weighs is not held or the model learned on no setup of the index.
    public static (double Score, int Rank, int Group)? Place(IScoreModel model, string index, IReadOnlyList<double?> readings) =>
        model.Score(readings) is { } score && model.Rank(index, score) is { } rank ? (score, rank, GroupOf(rank)) : null;

    // Every group's row for a population, a row for each of the five groups of each family and kind placed, the groups
    // with no setup stated at nought.
    public static IReadOnlyList<GroupRow> Rows(IReadOnlyList<Placed> placed, string population) =>
    [
        .. placed
            .GroupBy(one => (one.Family, one.Kind))
            .OrderBy(pair => pair.Key.Family, StringComparer.Ordinal)
            .ThenBy(pair => pair.Key.Kind, StringComparer.Ordinal)
            .SelectMany(pair => Enumerable.Range(0, Groups).Select(group =>
            {
                var own = pair.Where(one => one.Group == group).ToArray();
                var edges = own.Select(one => one.Edge).OfType<double>().ToArray();

                return new GroupRow(
                    pair.Key.Family,
                    pair.Key.Kind,
                    population,
                    group,
                    own.Length,
                    own.Count(one => one.Result is > 0),
                    edges.Length == 0 ? null : edges.Average(),
                    own.Count(one => one.Picked),
                    own.Count(one => one.LivePass));
            })),
    ];

    public async Task<int> RunAsync(string index, CancellationToken cancellation = default)
    {
        if (!RidgeScore.Indices.Contains(index, StringComparer.Ordinal))
        {
            output.WriteLine($"{Verb}: name an index with '--index', GSPC, MID or SML");

            return 2;
        }

        await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
        await connection.OpenAsync(cancellation);

        var runs = new List<Run>();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = Runs;
            command.Parameters.AddWithValue("$index", index);

            await using var reader = await command.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                runs.Add(new Run(reader.GetString(0), Date(reader.GetString(1)), Date(reader.GetString(2)[..10])));
            }
        }

        if (runs.Count == 0)
        {
            output.WriteLine($"{Verb}: no monthly run has fitted a score on the {DecisionCards.NameOf(index)}, so nothing was placed");

            return 0;
        }

        var newest = runs[^1];
        var models = new List<Stored>();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = Models;
            command.Parameters.AddWithValue("$index", index);

            await using var reader = await command.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                var kind = reader.GetString(2);
                IScoreModel model = kind == ScoreProcedures.Trees ? TreeModel.Parse(reader.GetString(4)) : RidgeModel.Parse(reader.GetString(4));

                models.Add(new Stored(reader.GetString(0), reader.GetString(1), kind, reader.GetInt32(3), model));
            }
        }

        var setups = new List<SettledSetup>();

        await using (var command = connection.CreateCommand())
        {
            command.CommandText = SettledSwings;
            command.Parameters.AddWithValue("$index", index);

            await using var reader = await command.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                var readings = new double?[LedgerReadings.Count];

                for (var at = 0; at < readings.Length; at++)
                {
                    readings[at] = reader.IsDBNull(7 + at) || !LedgerReadings.LoopReads(at, newest.Through) ? null : reader.GetDouble(7 + at);
                }

                setups.Add(new SettledSetup(
                    reader.GetString(0),
                    Date(reader.GetString(1)),
                    !reader.IsDBNull(2) && reader.GetInt32(2) == 1,
                    !reader.IsDBNull(3) && reader.GetInt32(3) == 1,
                    reader.IsDBNull(4) ? null : reader.GetDouble(4),
                    reader.IsDBNull(6) ? (reader.IsDBNull(5) ? null : reader.GetDouble(5)) : reader.GetDouble(6),
                    readings));
            }
        }

        // The newest run's fold models, a year each, and every run's model fitted on all finished data, whose year is past
        // the newest session the run read.
        var folds = models.Where(model => model.RunId == newest.Id && model.Year <= newest.Through.Year).ToDictionary(model => (model.Family, model.Kind, model.Year));
        var finished = models.Where(model => model.Year > runs.Single(run => run.Id == model.RunId).Through.Year).ToArray();
        var inFolds = new List<Placed>();
        var sinceFirst = new List<Placed>();

        foreach (var setup in setups)
        {
            foreach (var kind in new[] { ScoreProcedures.Ridge, ScoreProcedures.Trees })
            {
                if (folds.TryGetValue((setup.Family, kind, setup.Session.Year), out var fold) && Place(fold.Model, index, setup.Readings) is { } placed)
                {
                    inFolds.Add(new Placed(setup.Family, kind, setup.Session.Year, placed.Score, placed.Rank, placed.Group, setup.Picked, setup.LivePass, setup.Result, setup.Edge));
                }

                if (setup.Session >= runs[0].Started)
                {
                    var before = runs.LastOrDefault(run => run.Through < setup.Session);
                    var model = before is null ? null : finished.FirstOrDefault(one => one.RunId == before.Id && one.Family == setup.Family && one.Kind == kind);

                    if (model is not null && Place(model.Model, index, setup.Readings) is { } since)
                    {
                        sinceFirst.Add(new Placed(setup.Family, kind, setup.Session.Year, since.Score, since.Rank, since.Group, setup.Picked, setup.LivePass, setup.Result, setup.Edge));
                    }
                }
            }
        }

        var rows = Rows(inFolds, Folds).Concat(Rows(sinceFirst, SinceFirstRun)).ToArray();
        var lines = new List<string>
        {
            FormattableString.Invariant($"{setups.Count} settled swing setup(s) on the {DecisionCards.NameOf(index)}; the newest run {newest.Id} read through {newest.Through:yyyy-MM-dd} and stored {folds.Count} fold model(s) over {folds.Keys.Select(key => key.Year).Distinct().Count()} test year(s); {inFolds.Count} placement(s) in the test years and {sinceFirst.Count} since the first run of {runs[0].Started:yyyy-MM-dd}, each setup once a model kind."),
            "A fold's model learned only on paths that ended before the fold's year, and a setup since the first run is scored by the model fitted on all finished data by the newest run that read through a session before the setup's own. Each group is a fifth of the model's own hundredths on the index: under the 20th, the 20th to the 39th, the 40th to the 59th, the 60th to the 79th, and the 80th up.",
            "Winning is a result above nothing; the edge is the setup's edge after costs in risks, its edge where the night stored no cost; picked is the rule's own list holding the stock that night, passed the live rule passing it.",
        };

        foreach (var line in lines)
        {
            output.WriteLine($"{Verb}: {line}");
        }

        foreach (var row in rows)
        {
            output.WriteLine(FormattableString.Invariant($"{Verb}: {row.Family}, {row.Kind}, {row.Population}, group {row.Group + 1}: {row.Setups} setup(s), {CheckReports.Share(row.Winning, row.Setups)} winning, mean edge {CheckReports.Cell(row.MeanEdge, true)}, {row.Picked} picked, {row.Passed} passed"));
        }

        // From 21.8 the order each family's ranking variant takes on the index, read off the same fifths in the test years:
        // the learned score where its top fifth stands above its bottom, otherwise the expected value of the setups like
        // each where its own top fifth does, and none otherwise.
        // see: A family's ranking variant orders by the learned score where its audit's top fifth stands above the bottom and otherwise by the expected value, and is built nowhere neither orders
        var rankings = new List<RankingRow>();

        if (folds.Count > 0)
        {
            var like = await LikePool.ReadAsync(connection, index, cancellation);
            var from = new DateOnly(folds.Keys.Min(key => key.Year), 1, 1);

            foreach (var family in RankingWalk.Families)
            {
                var walk = await RankingWalk.ReadAsync(connection, index, family, from, newest.Through, cancellation);
                var (order, score, expected) = walk.Choose(year => folds.TryGetValue((family, ScoreProcedures.Ridge, year), out var fold) ? fold.Model : null, walk.ExpectedOf(like));

                rankings.Add(new RankingRow(family, order, score, expected));
                output.WriteLine($"{Verb}: {RankingLine(family, order, score, expected)}");
            }
        }

        var (report, figures) = CheckReports.Write(
            dataRoot,
            FormattableString.Invariant($"score-audit-{index}"),
            $"Score buckets on the {DecisionCards.NameOf(index)}",
            lines,
            [new CheckReports.Table(
                "Each family's settled setups by the group their score falls in",
                ["Family", "Kind", "Population", "Group", "Setups", "Winning", "Mean edge after costs", "Picked", "Passed"],
                [.. rows.Select(row => (IReadOnlyList<string>)[row.Family, row.Kind, row.Population, CheckReports.Cell(row.Group + 1), CheckReports.Cell(row.Setups), CheckReports.Share(row.Winning, row.Setups), CheckReports.Cell(row.MeanEdge, true), CheckReports.Cell(row.Picked), CheckReports.Cell(row.Passed)])]),
             new CheckReports.Table(
                "Each family's ranking variant: the order its fifths choose",
                ["Family", "Learned score's bottom fifth", "Learned score's top fifth", "Expected value's bottom fifth", "Expected value's top fifth", "Ordered by"],
                [.. rankings.Select(row => (IReadOnlyList<string>)[row.Family, CheckReports.Cell(row.Score.Bottom, true), CheckReports.Cell(row.Score.Top, true), CheckReports.Cell(row.Expected.Bottom, true), CheckReports.Cell(row.Expected.Top, true), row.Order == RankingOrder.None ? "not built" : RankingChoice.Words(row.Order)])])],
            new { index, run = newest.Id, through = newest.Through, settled = setups.Count, placedInFolds = inFolds.Count, placedSinceFirstRun = sinceFirst.Count, rows, rankings },
            clock.UtcNow);

        output.WriteLine($"{Verb}: written to {report} with its figures at {figures}; no store row written");

        return 0;
    }

    // From 21.8 a family's ranking variant on an index: the order its fifths choose, and the fifths.
    public sealed record RankingRow(string Family, RankingOrder Order, RankingFifths Score, RankingFifths Expected);

    // A family's ranking variant in a run's words.
    public static string RankingLine(string family, RankingOrder order, RankingFifths score, RankingFifths expected) =>
        $"the {family}'s ranking variant "
        + (order == RankingOrder.None ? "is not built" : "orders by " + RankingChoice.Words(order))
        + ": the learned score's fifths, " + RankingWalk.FifthsWords(score)
        + "; the expected value's fifths, " + RankingWalk.FifthsWords(expected);

    static DateOnly Date(string stamp) => DateOnly.ParseExact(stamp, "yyyy-MM-dd", CultureInfo.InvariantCulture);
}
