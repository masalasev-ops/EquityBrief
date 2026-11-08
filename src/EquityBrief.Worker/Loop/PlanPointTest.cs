using System.Globalization;
using EquityBrief.Core.Components;
using EquityBrief.Core.Configuration;
using EquityBrief.Core.History;
using EquityBrief.Core.Indicators;
using EquityBrief.Core.Ladders;
using EquityBrief.Core.Levels;
using EquityBrief.Core.Loop;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Readings;
using EquityBrief.Core.Swings;
using EquityBrief.Core.Time;
using EquityBrief.Core.Volume;
using EquityBrief.Data;
using EquityBrief.Worker.Cards;
using EquityBrief.Worker.Filter;
using EquityBrief.Worker.History;
using EquityBrief.Worker.Indices;
using EquityBrief.Worker.Ledger;
using EquityBrief.Worker.Research;
using EquityBrief.Worker.Sweep;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Worker.Loop;

// The traded plan's buy and sell points tested over the long history, by hand and on one index at a time: every setup the
// pullback, the breakout, the drift and the dip passed under its live rule, the long ledger's from 2005 and the standing
// ledger's from 2019, walked under every combination of an entry, a stop and a sale over the long reader's closes, its
// levels plan read as of its session from the bands and the trend the night's own functions read. Each combination's
// figures by year, era and market regime go to a report under the data root's loop folder, and each family's recommended
// combination over the years no setting was chosen on, beside the traded plan's, to `plan_point`. It registers nothing and
// moves no plan, rule or version.
// see: Every price family's traded plan's buy and sell points are tested over the long history and one combination is recommended a family an index
public sealed class PlanPointTest(IClock clock, string databaseFile, string dataRoot, TextWriter output) : IComponent
{
    public static ComponentAccess Access => new(
        Stores:
        [
            new StoreTouch(Store.LongSetup, Touch.Read),
            new StoreTouch(Store.Setup, Touch.Read),
            new StoreTouch(Store.PlanPoint, Touch.Insert),
            new StoreTouch(Store.RunLog, Touch.Insert),
        ],
        Feeds: []);

    public const string Verb = "plan-points";

    // The example the operator named: CTSH as the S&P 500's night of 2026-11-06 flagged it.
    public const string ExampleTicker = "CTSH";
    public static DateOnly ExampleSession { get; } = new(2026, 11, 6);

    // Each index's own fund, which a trade's benchmark is held in over the same sessions, and the market the regime reads.
    public static IReadOnlyDictionary<string, string> Funds { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["GSPC"] = "SPY",
        ["MID"] = "IJH",
        ["SML"] = "IJR",
    };

    public const string Market = "GSPC";

    public const int RegimeDays = 200;

    const string Setups = @"
        SELECT family, ticker, session_date, entry, stop, target, trail, cap, company_value
        FROM long_setup WHERE index_code = $index AND live_pass = 1 AND family IN ('pullback', 'breakout', 'drift', 'dip')
        UNION ALL
        SELECT family, ticker, session_date, entry, stop, target, trail, cap, company_value
        FROM setup WHERE index_code = $index AND live_pass = 1 AND session_date >= $standing AND family IN ('pullback', 'breakout', 'drift', 'dip');";

    const string ExampleSetups = @"
        SELECT family, ticker, session_date, entry, stop, target, trail, cap, company_value
        FROM setup WHERE index_code = $index AND ticker = $ticker AND session_date = $session AND family IN ('pullback', 'breakout', 'drift', 'dip');";

    const string InsertPlanPoint = @"
        INSERT INTO plan_point (run_id, index_code, family, through, setups, combination, picks, fills, fill_rate, hit_rate, break_even, result_per_pick, edge_per_pick, edge_per_fill, days_held, worst_drawdown, traded_fills, traded_edge_per_pick, later_fills, later_edge_per_pick, traded_later_edge_per_pick)
        VALUES ($run_id, $index_code, $family, $through, $setups, $combination, $picks, $fills, $fill_rate, $hit_rate, $break_even, $result_per_pick, $edge_per_pick, $edge_per_fill, $days_held, $worst_drawdown, $traded_fills, $traded_edge_per_pick, $later_fills, $later_edge_per_pick, $traded_later_edge_per_pick);";

    const string AppendRun = @"
        INSERT INTO run_log (run_id, stage, started_at, ended_at, outcome, rows_written, model_calls, network_requests, spend, detail)
        VALUES ($run_id, $stage, $started_at, $ended_at, 'ok', $rows_written, 0, 0, '0', $detail);";

    public const string All = "all years";
    public const string Unseen = "years no setting was chosen on";

    // The years the settings were chosen on, which the recommendation, chosen on the years before them, never read: a
    // recommendation's edge over the years it was chosen on reads high for having been chosen there, so its edge here
    // stands beside it.
    public const string Chosen = "years the settings were chosen on";

    // A flagged setup as the ledger stored it.
    public sealed record FlaggedSetup(string Family, string Ticker, DateOnly Session, double Entry, double Stop, double? Target, double? Trail, int Cap, double? CompanyValue);

    // The levels plan as of a setup's session: each zone's top and its own stop, the staged sales' levels and the low edges
    // of the resistance bands with real touches.
    public sealed record LevelsAsOf(IReadOnlyList<(double Top, double? Stop)> Zones, IReadOnlyList<double> Staged, IReadOnlyList<double> Bands, string? Why);

    // One combination's outcome on one setup, as the example prints it.
    public sealed record Worked(PlanCombination Combination, double? Level, double? Fill, double? Stop, string Sale, PlanPick? Pick, string Words);

    public async Task<int> RunAsync(string index, bool print, CancellationToken cancellation = default)
    {
        if (!Funds.TryGetValue(index, out var fund))
        {
            output.WriteLine($"{Verb}: name '--index GSPC', 'MID' or 'SML'");

            return 2;
        }

        if (NightLock.Holder(dataRoot) is { } holder)
        {
            output.WriteLine($"{Verb}: the night holds the store ({holder}); run it once the night has finished");

            return 2;
        }

        var started = clock.UtcNow;
        var history = new SweepHistory(databaseFile);
        var through = await history.NewestSessionAsync(cancellation);
        var longHistory = new LongHistoryReader(databaseFile);
        var inputs = await longHistory.ReadAsync(index, through, output.WriteLine, cancellation);
        var calendar = inputs.Sessions;
        var sessionAt = calendar.Select((session, at) => (session, at)).ToDictionary(pair => pair.session, pair => pair.at);
        var series = new SweepSeries[inputs.Names.Count];

        Parallel.For(0, inputs.Names.Count, name => series[name] = SweepColumns.Series(inputs.Names[name], sessionAt));

        var market = await longHistory.SeriesOfAsync([Market, fund], through, cancellation);
        var regime = RegimeOf(market.FirstOrDefault(one => one.Series == Market));
        var held = market.FirstOrDefault(one => one.Series == fund)?.Closes.ToDictionary(one => one.Session, one => one.Close) ?? [];
        var setups = await SetupsAsync(Setups, [("$index", index), ("$standing", Text(LongLedger.StandingFrom))], cancellation);
        var names = new Dictionary<string, List<int>>(StringComparer.Ordinal);

        for (var name = 0; name < series.Length; name++)
        {
            (names.TryGetValue(series[name].Name.Ticker, out var list) ? list : names[series[name].Name.Ticker] = []).Add(name);
        }

        output.WriteLine(FormattableString.Invariant($"{Verb}: {setups.Count} flagged setup(s) of the four families on the {DecisionCards.NameOf(index)} through {Text(through)}, each walked under {PlanPoints.Combinations.Count} combination(s)"));

        var walker = new Walker(series, calendar, names, held, regime);
        var tallies = new Dictionary<(string Family, int Combination, string Group), PlanTally>();
        var unread = 0;
        var done = 0;

        PlanTally TallyOf(string family, int combination, string group) =>
            tallies.TryGetValue((family, combination, group), out var tally) ? tally : tallies[(family, combination, group)] = new PlanTally(group is All or Unseen);

        foreach (var chunk in setups.Chunk(4_000))
        {
            var walked = new PlanPick?[chunk.Length][];

            Parallel.For(0, chunk.Length, new ParallelOptions { CancellationToken = cancellation }, at => walked[at] = walker.Picks(chunk[at]));

            for (var at = 0; at < chunk.Length; at++)
            {
                if (walked[at] is not { Length: > 0 } picks)
                {
                    unread++;

                    continue;
                }

                for (var combination = 0; combination < picks.Length; combination++)
                {
                    if (picks[combination] is not { } pick)
                    {
                        continue;
                    }

                    var family = chunk[at].Family;

                    TallyOf(family, combination, All).Add(pick);
                    TallyOf(family, combination, pick.Era).Add(pick);
                    TallyOf(family, combination, pick.Regime).Add(pick);
                    TallyOf(family, combination, pick.Year.ToString(CultureInfo.InvariantCulture)).Add(pick);

                    TallyOf(family, combination, pick.Year < LongLedger.StandingFrom.Year ? Unseen : Chosen).Add(pick);
                }
            }

            done += chunk.Length;
            output.WriteLine(FormattableString.Invariant($"{Verb}: {done} of {setups.Count} setup(s) walked"));
        }

        var figures = tallies.ToDictionary(pair => pair.Key, pair => pair.Value.Figures());
        var picked = new List<(string Family, PlanCombination? Combination, PlanFigures? Figures, PlanFigures? Traded, int Setups)>();
        var traded = Array.FindIndex(PlanPoints.Combinations.ToArray(), one => one.Entry == PlanPoints.NextOpen && one.Stop == PlanPoints.RuleStop && one.Exit == PlanPoints.RuleSale);

        foreach (var family in PlanPoints.Families)
        {
            var unseen = PlanPoints.Combinations
                .Select((combination, at) => (combination, figures.GetValueOrDefault((family, at, Unseen))))
                .Where(one => one.Item2 is not null)
                .Select(one => (one.combination, one.Item2!))
                .ToArray();
            var recommended = PlanPoints.Recommended(unseen);
            var at = recommended is null ? -1 : Array.IndexOf(PlanPoints.Combinations.ToArray(), recommended);

            picked.Add((family, recommended, at < 0 ? null : figures[(family, at, Unseen)], figures.GetValueOrDefault((family, traded, Unseen)), setups.Count(one => one.Family == family)));
        }

        var lines = new List<string>
        {
            FormattableString.Invariant($"{setups.Count} flagged setup(s) of the pullback, the breakout, the drift and the dip on the {DecisionCards.NameOf(index)}, the long ledger's from 2005 and the standing ledger's from 2019, {unread} whose stock or session the long history does not hold left out; each walked under {PlanPoints.Combinations.Count} combination(s) of an entry, a stop and a sale."),
            "A level's buy fills on the first session after the pick within its window that trades into it, at its open where it opens at or under the level and at the level otherwise; the next open fills on the session after the pick. A pick its entry does not fill, whose levels plan draws no such zone, or whose fill stands at or under its stop, is offered and not taken, and counts as nothing in a figure per pick offered.",
            "A sale is signalled by a close and sold at the next session's open, as the ledger's fill step sells: a close under the stop, at or over the target or the first band above, at or over each staged level for its equal share, under the trail, or the time limit's last close. A result is in the risk the rule's own plan set at the pick's close, so every combination holds the next-open buy's number of shares, less the published round trip for its company's size at its prices; its edge is that less its index's own fund held over the same sessions in the same risk. A rule naming neither a target nor a trail, the dip, sells at its time limit.",
            FormattableString.Invariant($"The settings were chosen on {LongLedger.StandingFrom.Year} onward, so the years before it are the years no setting was chosen on, and the recommendation reads them alone: the highest edge per pick offered among the combinations resting on {PlanPoints.FewestFills} fills or more there. It is a proposal, and nothing here changes a pick, a plan, a rule or a version."),
        };

        foreach (var (family, combination, chosen, plan, count) in picked)
        {
            var since = combination is null ? null : figures.GetValueOrDefault((family, PlanPoints.Combinations.ToList().IndexOf(combination), Chosen));
            var tradedSince = figures.GetValueOrDefault((family, traded, Chosen));

            lines.Add(combination is null
                ? FormattableString.Invariant($"The {family}: {count} setup(s); no combination rests on {PlanPoints.FewestFills} fills over the years no setting was chosen on.")
                : FormattableString.Invariant($"The {family}: {count} setup(s); recommended {combination.Words}, edge per pick offered {Shown(chosen!.EdgePerPick)} over {chosen.Fills} fill(s) of {chosen.Picks} pick(s), against the traded plan's {Shown(plan?.EdgePerPick)} over {plan?.Fills ?? 0} fill(s); from {LongLedger.StandingFrom.Year}, which it was not chosen on, {Shown(since?.EdgePerPick)} over {since?.Fills ?? 0} fill(s) against the traded plan's {Shown(tradedSince?.EdgePerPick)} over {tradedSince?.Fills ?? 0}."));
        }

        var example = index == Market ? await ExampleAsync(walker, cancellation) : [];

        if (index == Market)
        {
            lines.Add(example.Count == 0
                ? FormattableString.Invariant($"{ExampleTicker} of {Text(ExampleSession)} is no setup of the four families the store holds on the {DecisionCards.NameOf(index)}.")
                : FormattableString.Invariant($"{ExampleTicker} of {Text(ExampleSession)} worked through every combination: each entry's level, its fill or why none, its stop and its sale, and its result where its path has ended."));
        }

        foreach (var line in lines)
        {
            output.WriteLine($"{Verb}: {line}");
        }

        foreach (var one in example)
        {
            output.WriteLine(FormattableString.Invariant($"{Verb}: {ExampleTicker} {one.Combination.Words}: buy {(one.Level is { } level ? level.ToString("0.00", CultureInfo.InvariantCulture) : "at the next open")}, stop {(one.Stop is { } stop ? stop.ToString("0.00", CultureInfo.InvariantCulture) : "none")}, sell {one.Sale}; {one.Words}"));
        }

        if (print)
        {
            output.WriteLine($"{Verb}: printed alone, nothing written");

            return 0;
        }

        static IReadOnlyList<string> Row(string family, PlanCombination combination, string group, PlanFigures one) =>
        [
            family, combination.Words, group, CheckReports.Cell(one.Picks), CheckReports.Cell(one.Fills), CheckReports.Cell(one.FillRate), CheckReports.Cell(one.HitRate), CheckReports.Cell(one.BreakEven),
            CheckReports.Cell(one.ResultPerPick, signed: true), CheckReports.Cell(one.EdgePerPick, signed: true), CheckReports.Cell(one.EdgePerFill, signed: true), CheckReports.Cell(one.DaysHeld), CheckReports.Cell(one.WorstDrawdown),
        ];

        string[] columns = ["Family", "Combination", "Years", "Picks", "Fills", "Fill rate", "Hit rate", "Break-even rate", "Result per pick", "Edge per pick", "Edge per fill", "Days held", "Worst drawdown"];
        var groups = new[] { Unseen, Chosen, All }.Concat(LongLedger.Eras.Select(era => era.Era)).Concat([PlanPoints.Above, PlanPoints.Below]).ToArray();

        var (report, figuresFile) = CheckReports.Write(
            dataRoot,
            FormattableString.Invariant($"plan-points-{index}"),
            $"The traded plan's buy and sell points on the {DecisionCards.NameOf(index)}",
            lines,
            [
                new CheckReports.Table(
                    "Each family's combinations over the years no setting was chosen on, the highest edge per pick first",
                    columns,
                    [.. PlanPoints.Families.SelectMany(family => PlanPoints.Combinations
                        .Select((combination, at) => (combination, figures: figures.GetValueOrDefault((family, at, Unseen))))
                        .Where(one => one.figures is not null)
                        .OrderByDescending(one => one.figures!.EdgePerPick ?? double.MinValue)
                        .Select(one => Row(family, one.combination, Unseen, one.figures!)))]),
                new CheckReports.Table(
                    "The recommended combination and the traded plan by years, era and market regime",
                    columns,
                    [.. picked.SelectMany(one => new[] { one.Combination, PlanPoints.Combinations[traded] }.OfType<PlanCombination>().Distinct().SelectMany(combination =>
                        groups.Select(group => (group, figures: figures.GetValueOrDefault((one.Family, PlanPoints.Combinations.ToList().IndexOf(combination), group))))
                            .Where(read => read.figures is not null)
                            .Select(read => Row(one.Family, combination, read.group, read.figures!))))]),
                new CheckReports.Table(
                    FormattableString.Invariant($"{ExampleTicker} of {Text(ExampleSession)} under every combination"),
                    ["Combination", "Level", "Fill", "Stop", "Sale", "What came of it"],
                    [.. example.Select(one => (IReadOnlyList<string>)[one.Combination.Words, CheckReports.Cell(one.Level), CheckReports.Cell(one.Fill), CheckReports.Cell(one.Stop), one.Sale, one.Words])]),
            ],
            new
            {
                index,
                through = Text(through),
                setups = setups.Count,
                unread,
                combinations = PlanPoints.Combinations.Select(one => one.Words),
                figures = figures.Select(pair => new { pair.Key.Family, combination = PlanPoints.Combinations[pair.Key.Combination].Words, group = pair.Key.Group, figures = pair.Value }),
                recommended = picked.Select(one => new { one.Family, combination = one.Combination?.Words, one.Figures, traded = one.Traded, one.Setups }),
                example = example.Select(one => new { combination = one.Combination.Words, one.Level, one.Fill, one.Stop, one.Sale, one.Words }),
            },
            clock.UtcNow);

        var runId = FormattableString.Invariant($"{Verb}-{index}-{started:yyyyMMddTHHmmssZ}");

        using (await DrainLock.AcquireAsync(dataRoot, () => Task.Delay(SetupLedger.Poll, cancellation), cancellation))
        {
            await using var connection = new SqliteConnection(StoreConnection.For(databaseFile));
            await connection.OpenAsync(cancellation);
            await using var transaction = connection.BeginTransaction();

            static object Or(object? value) => value ?? DBNull.Value;

            foreach (var (family, combination, chosen, plan, count) in picked)
            {
                await ExecuteAsync(connection, transaction, InsertPlanPoint,
                [
                    ("$run_id", runId),
                    ("$index_code", index),
                    ("$family", family),
                    ("$through", Text(through)),
                    ("$setups", count),
                    ("$combination", Or(combination?.Words)),
                    ("$picks", Or(chosen?.Picks)),
                    ("$fills", Or(chosen?.Fills)),
                    ("$fill_rate", Or(chosen?.FillRate)),
                    ("$hit_rate", Or(chosen?.HitRate)),
                    ("$break_even", Or(chosen?.BreakEven)),
                    ("$result_per_pick", Or(chosen?.ResultPerPick)),
                    ("$edge_per_pick", Or(chosen?.EdgePerPick)),
                    ("$edge_per_fill", Or(chosen?.EdgePerFill)),
                    ("$days_held", Or(chosen?.DaysHeld)),
                    ("$worst_drawdown", Or(chosen?.WorstDrawdown)),
                    ("$traded_fills", Or(plan?.Fills)),
                    ("$traded_edge_per_pick", Or(plan?.EdgePerPick)),
                    ("$later_fills", Or(combination is null ? null : figures.GetValueOrDefault((family, PlanPoints.Combinations.ToList().IndexOf(combination), Chosen))?.Fills)),
                    ("$later_edge_per_pick", Or(combination is null ? null : figures.GetValueOrDefault((family, PlanPoints.Combinations.ToList().IndexOf(combination), Chosen))?.EdgePerPick)),
                    ("$traded_later_edge_per_pick", Or(figures.GetValueOrDefault((family, traded, Chosen))?.EdgePerPick)),
                ], cancellation);
            }

            await ExecuteAsync(connection, transaction, AppendRun,
            [
                ("$run_id", runId),
                ("$stage", Verb),
                ("$started_at", Instant(started)),
                ("$ended_at", Instant(clock.UtcNow)),
                ("$rows_written", picked.Count),
                ("$detail", FormattableString.Invariant($"{setups.Count} setup(s) on the {DecisionCards.NameOf(index)} walked under {PlanPoints.Combinations.Count} combination(s); {picked.Count(one => one.Combination is not null)} of {picked.Count} famil(ies) with a combination recommended")),
            ], cancellation);

            await transaction.CommitAsync(cancellation);
        }

        output.WriteLine($"{Verb}: written to {report} with its figures at {figuresFile}, and each family's recommendation to the store; registered nothing");

        return 0;
    }

    async Task<IReadOnlyList<Worked>> ExampleAsync(Walker walker, CancellationToken cancellation)
    {
        var rows = await SetupsAsync(ExampleSetups, [("$index", Market), ("$ticker", ExampleTicker), ("$session", Text(ExampleSession))], cancellation);

        return rows.Count == 0 ? [] : walker.Worked(rows[0]);
    }

    async Task<List<FlaggedSetup>> SetupsAsync(string sql, IReadOnlyList<(string Name, string Value)> parameters, CancellationToken cancellation)
    {
        await using var connection = new SqliteConnection(SweepHistory.ConnectionString(databaseFile));
        await connection.OpenAsync(cancellation);
        await using var command = connection.CreateCommand();

        command.CommandText = sql;

        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        var rows = new List<FlaggedSetup>();

        await using var reader = await command.ExecuteReaderAsync(cancellation);

        while (await reader.ReadAsync(cancellation))
        {
            double? Price(int column) => reader.IsDBNull(column) ? null : Statistic.FromPrice(Money.FromStorage(reader.GetString(column)));

            rows.Add(new FlaggedSetup(
                reader.GetString(0),
                reader.GetString(1),
                DateOnly.ParseExact(reader.GetString(2), "yyyy-MM-dd", CultureInfo.InvariantCulture),
                Price(3) ?? 0,
                Price(4) ?? 0,
                Price(5),
                Price(6),
                reader.GetInt32(7),
                reader.IsDBNull(8) ? null : reader.GetDouble(8)));
        }

        return rows;
    }

    static async Task ExecuteAsync(SqliteConnection connection, SqliteTransaction transaction, string sql, IReadOnlyList<(string Name, object Value)> parameters, CancellationToken cancellation)
    {
        await using var command = connection.CreateCommand();

        command.Transaction = transaction;
        command.CommandText = sql;

        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        await command.ExecuteNonQueryAsync(cancellation);
    }

    // Each session's side of the market's 200-day average, none before it holds that many closes.
    static IReadOnlyDictionary<DateOnly, string> RegimeOf(SweepMarketSeries? market)
    {
        var sides = new Dictionary<DateOnly, string>();

        if (market is null)
        {
            return sides;
        }

        var closes = market.Closes;
        var sum = 0.0;

        for (var at = 0; at < closes.Count; at++)
        {
            sum += closes[at].Close;

            if (at >= RegimeDays)
            {
                sum -= closes[at - RegimeDays].Close;
            }

            if (at >= RegimeDays - 1)
            {
                sides[closes[at].Session] = closes[at].Close >= sum / RegimeDays ? PlanPoints.Above : PlanPoints.Below;
            }
        }

        return sides;
    }

    static string Text(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    static string Instant(DateTimeOffset at) => at.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    static string Shown(double? figure) => figure is { } held ? held.ToString("+0.000;-0.000;0.000", CultureInfo.InvariantCulture) : "none";

    // Walks one setup under every combination over the long reader's series.
    public sealed class Walker(SweepSeries[] series, DateOnly[] calendar, IReadOnlyDictionary<string, List<int>> names, IReadOnlyDictionary<DateOnly, double> fund, IReadOnlyDictionary<DateOnly, string> regime)
    {
        // The setup's stock, its bar and its arrays, none where the long history does not hold it on its session.
        (int Name, int Bar, double[] Opens, double[] Lows, double[] Closes)? Placed(FlaggedSetup setup)
        {
            var at = Array.BinarySearch(calendar, setup.Session);

            if (at < 0 || !names.TryGetValue(setup.Ticker, out var held))
            {
                return null;
            }

            foreach (var name in held)
            {
                if (IndexNightRead.BarOf(series[name], at) is >= 0 and var bar)
                {
                    var bars = series[name].Bars;

                    return (name, bar, [.. bars.Select(one => Statistic.FromPrice(one.Open))], [.. bars.Select(one => Statistic.FromPrice(one.Low))], [.. bars.Select(one => Statistic.FromPrice(one.Close))]);
                }
            }

            return null;
        }

        public PlanPick?[] Picks(FlaggedSetup setup) =>
            Placed(setup) is { } placed ? [.. Walk(setup, placed).Select(one => one.Pick)] : [];

        public IReadOnlyList<Worked> Worked(FlaggedSetup setup) =>
            Placed(setup) is { } placed ? Walk(setup, placed) : [];

        IReadOnlyList<Worked> Walk(FlaggedSetup setup, (int Name, int Bar, double[] Opens, double[] Lows, double[] Closes) placed)
        {
            var (name, bar, opens, lows, closes) = placed;
            var one = series[name];
            var factor = setup.Entry > 0 ? closes[bar] / setup.Entry : 1;
            var ruleStop = setup.Stop * factor;
            var ruleSale = new PlanSale(setup.Target * factor, setup.Trail * factor, []);

            // Every combination's result in the risk the rule's own plan set at the pick's close, so each is read on the same
            // number of shares as the next-open buy, a buy far down at a tight stop not reading large for its small risk.
            var rulesRisk = closes[bar] - ruleStop;

            if (!(rulesRisk > 0))
            {
                return [];
            }
            var typical = one.Atr[bar];
            var levels = LevelsAt(one, bar);
            var era = LongLedger.EraOf(setup.Session.Year);
            var side = regime.GetValueOrDefault(setup.Session) ?? PlanPoints.Below;
            var worked = new List<Worked>();

            foreach (var combination in PlanPoints.Combinations)
            {
                var zone = PlanPoints.Zones.ToList().IndexOf(combination.Entry);
                (double Top, double? Stop)? zoneRead = zone >= 0 && zone < levels.Zones.Count ? levels.Zones[zone] : null;
                double? level = combination.Entry == PlanPoints.NextOpen ? null
                    : combination.Entry == PlanPoints.Average ? (one.Sma20[bar] > 0 ? one.Sma20[bar] : null)
                    : zoneRead?.Top;

                Worked NotTaken(string why, double? fill = null, double? stop = null) =>
                    new(combination, level, fill, stop, combination.Exit, new PlanPick(setup.Session.Year, era, side, false, null, null, 0, null, 0, null), why);

                if (combination.Entry != PlanPoints.NextOpen && level is null)
                {
                    worked.Add(NotTaken(zone >= 0 ? "no such zone: " + (levels.Why ?? "the levels plan draws fewer zones") : "no 20-session average"));

                    continue;
                }

                (PlanSale Sale, string Words) SaleAt(double price)
                {
                    var sale = combination.Exit switch
                    {
                        PlanPoints.RuleSale => ruleSale.Target is null && ruleSale.Trail is null ? new PlanSale(null, null, []) : ruleSale,
                        PlanPoints.FirstBand => new PlanSale(levels.Bands.Where(edge => edge > price).Order().Cast<double?>().FirstOrDefault(), null, []),
                        PlanPoints.StagedSales => new PlanSale(null, null, levels.Staged),
                        _ => new PlanSale(null, null, []),
                    };

                    return (sale, sale.Target is { } target ? FormattableString.Invariant($"at {target:0.00}")
                        : sale.Trail is { } trail ? FormattableString.Invariant($"trailing {trail:0.00} under the highest close")
                        : sale.Staged.Any(edge => edge > price) ? "at " + string.Join(", ", sale.Staged.Where(edge => edge > price).Order().Select(edge => edge.ToString("0.00", CultureInfo.InvariantCulture)))
                        : FormattableString.Invariant($"at the close of session {setup.Cap}"));
                }

                if (PlanPoints.FillOf(opens, lows, bar, level, combination.Window) is not { } fill)
                {
                    if (bar + 1 >= closes.Length)
                    {
                        // The pick's own session is the newest held: the plan as it would stand on a buy at its level, or at the
                        // close for the next open, nothing walked.
                        var price = level ?? closes[bar];
                        var planned = PlanPoints.StopOf(combination.Stop, price, ruleStop, zoneRead?.Stop, typical);

                        worked.Add(new Worked(combination, level, null, planned, SaleAt(price).Words, null, "not yet traded: the history ends on the pick's session"));
                    }
                    else
                    {
                        worked.Add(NotTaken("not filled within its window"));
                    }

                    continue;
                }

                if (PlanPoints.StopOf(combination.Stop, fill.Price, ruleStop, zoneRead?.Stop, typical) is not { } stop || !(fill.Price > stop))
                {
                    worked.Add(NotTaken("its fill stands at or under its stop", fill.Price));

                    continue;
                }

                var (sale, saleWords) = SaleAt(fill.Price);

                if (PlanPoints.Walk(closes, opens, fill.Bar, fill.Price, stop, sale, setup.Cap) is not { } outcome)
                {
                    worked.Add(new Worked(combination, level, fill.Price, stop, saleWords, null, "open: the history ends before its sale"));

                    continue;
                }

                var trip = Statistic.FromPrice(TradeCost.RoundTrip(
                    setup.CompanyValue is { } value && double.IsFinite(value) ? Statistic.ToPrice(value) : null,
                    Statistic.ToPrice(fill.Price),
                    Statistic.ToPrice(outcome.Sold)));
                var result = (outcome.Sold - fill.Price - trip) / rulesRisk;
                var ended = Math.Min(fill.Bar + outcome.Sessions, one.Bars.Length - 1);
                var bought = fund.GetValueOrDefault(one.Bars[Math.Max(0, fill.Bar - 1)].Session);
                var sold = fund.GetValueOrDefault(one.Bars[Math.Max(0, ended - 1)].Session);
                double? edge = bought > 0 && sold > 0 ? result - (((sold / bought) - 1) * fill.Price / rulesRisk) : null;
                var pick = new PlanPick(setup.Session.Year, era, side, true, result, edge, outcome.Sessions, outcome.End, one.SessionAt[ended], PlanPoints.BreakEvenOf(fill.Price, stop, sale.Target))
                {
                    Hit = PlanPoints.HitOf(combination.Exit, outcome.End),
                };

                worked.Add(new Worked(combination, level, fill.Price, stop, saleWords, pick, FormattableString.Invariant($"{outcome.End} after {outcome.Sessions} session(s), {result:+0.00;-0.00} risks after costs")));
            }

            return worked;
        }

        // The levels plan as of a bar, from the bands and the trend the night's own functions read over the year it held.
        static LevelsAsOf LevelsAt(SweepSeries series, int bar)
        {
            var from = Math.Max(0, bar - VolumeProfileSeries.Window + 1);
            var bars = series.Bars[from..(bar + 1)].Select(one => new ReplayBar(one.Session, one.High, one.Low, one.Close, one.Volume)).ToArray();
            var indicators = new Dictionary<string, double>(StringComparer.Ordinal);

            void Hold(string key, double value)
            {
                if (double.IsFinite(value) && value > 0)
                {
                    indicators[key] = value;
                }
            }

            Hold(IndicatorSeries.Atr14, series.Atr[bar]);
            Hold(IndicatorSeries.Sma20, series.Sma20[bar]);
            Hold(IndicatorSeries.Sma50, series.Sma50[bar]);
            Hold(IndicatorSeries.Sma200, series.Sma200[bar]);

            if (!indicators.TryGetValue(IndicatorSeries.Atr14, out var atr))
            {
                return new LevelsAsOf([], [], [], "no typical daily move is read on the session");
            }

            var confirmed = series.Swings.Take(series.Confirmed[bar]).ToArray();
            var close = series.Bars[bar].Close;
            decimal? Average(string key) => indicators.TryGetValue(key, out var value) ? Statistic.ToPrice(value) : null;
            var trend = TrendSeries.For(close, Average(IndicatorSeries.Sma50), Average(IndicatorSeries.Sma200), confirmed);
            var bands = SessionReplay.BandsOver(bars, indicators, confirmed);
            var recent = bars.Skip(Math.Max(0, bars.Length - LadderSeries.ConditionLookback)).Select(one => new LadderBar(one.SessionDate, one.High, one.Low, one.Close)).ToArray();
            var lows = confirmed.Where(swing => swing.Direction == SwingSeries.Low).OrderBy(swing => swing.SessionDate).Select(swing => swing.Price).ToArray();
            var ladder = LadderSeries.For(bands, close, Statistic.ToPrice(atr), recent, trend.State, lows);
            var touched = bands
                .Where(band => band.Role == LevelSeries.Resistance && band.HasNonAverageAnchor && band.Members.Any(member => member.Source == MemberSource.Touch))
                .Select(band => Statistic.FromPrice(band.LowEdge))
                .ToArray();

            return new LevelsAsOf(
                [.. ladder.Tranches.Take(LadderSeries.MostTranches).Select(tranche => (Statistic.FromPrice(tranche.HighEdge), tranche.Stop is { } stop ? Statistic.FromPrice(stop) : default(double?)))],
                [.. ladder.Exits.Where(exit => exit.Traded && !exit.Trailing).Select(exit => Statistic.FromPrice(exit.LowEdge))],
                touched,
                ladder.Reason);
        }
    }
}
