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
            new StoreTouch(Store.LongFiling, Touch.Read),
            new StoreTouch(Store.Company, Touch.Read),
            new StoreTouch(Store.PulledCompany, Touch.Read),
            new StoreTouch(Store.LongFact, Touch.Read),
            new StoreTouch(Store.FiledFact, Touch.Read),
            new StoreTouch(Store.PulledRevenue, Touch.Read),
            new StoreTouch(Store.RunLog, Touch.Insert),
        ],
        Feeds: []);

    public const string Verb = "plan-points";

    // The example the operator named: CTSH as the S&P 500's night of 2026-11-06 flagged it.
    public const string ExampleTicker = "CTSH";
    public static DateOnly ExampleSession { get; } = new(2026, 11, 6);

    // Each index's own fund, held over a pick's own sessions as the second line beside its benchmark, and the market the
    // regime reads.
    public static IReadOnlyDictionary<string, string> Funds => FundLine.Funds;

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
        INSERT INTO plan_point (run_id, index_code, family, through, setups, combination, picks, fills, fill_rate, hit_rate, break_even, result_per_pick, edge_per_pick, edge_per_fill, days_held, worst_drawdown, traded_fills, traded_edge_per_pick, later_fills, later_edge_per_pick, traded_later_edge_per_pick, fund_edge_per_pick, traded_fund_edge_per_pick, later_fund_edge_per_pick, traded_later_fund_edge_per_pick, traded_later_picks)
        VALUES ($run_id, $index_code, $family, $through, $setups, $combination, $picks, $fills, $fill_rate, $hit_rate, $break_even, $result_per_pick, $edge_per_pick, $edge_per_fill, $days_held, $worst_drawdown, $traded_fills, $traded_edge_per_pick, $later_fills, $later_edge_per_pick, $traded_later_edge_per_pick, $fund_edge_per_pick, $traded_fund_edge_per_pick, $later_fund_edge_per_pick, $traded_later_fund_edge_per_pick, $traded_later_picks);";

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

    // Why a session draws no levels plan, which the report prints beside a combination it could not place.
    public const string NoTypicalMove = "no typical daily move is read on the session";

    // One combination's outcome on one setup, as the example prints it.
    public sealed record Worked(PlanCombination Combination, double? Level, double? Fill, double? Stop, string Sale, PlanPick? Pick, string Words);

    public async Task<int> RunAsync(string index, bool print, CancellationToken cancellation = default, bool driftChecks = false)
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

        if (driftChecks)
        {
            return await DriftChecksAsync(index, setups, walker, cancellation);
        }

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

        // On the operator's order of 2026-11-10, items 1 and 2: a recommendation stopping 2 typical days under its buy walked
        // again at each wider stop, the one doing best from 2019 standing where it beats it there; then one buying at a zone or
        // the average walked beside the same distance under the close, the median of the level's distance in typical days over
        // the family's picks before 2019, at the same stop and sale, the distance standing only where it does better from 2019
        // by more than 0.02 risks a pick.
        // see: A recommendation stopping 2 typical days under its buy is walked again wider and buying at a level beside the same distance under the close, each kept where it does better from 2019
        var refinedSince = new Dictionary<(string Family, string Words), PlanFigures?>();
        var refinements = new List<string>();

        PlanFigures? SinceOf(string family, PlanCombination? combination) =>
            combination is null ? null
            : refinedSince.TryGetValue((family, combination.Words), out var refined) ? refined
            : figures.GetValueOrDefault((family, PlanPoints.Combinations.ToList().IndexOf(combination), Chosen));

        for (var at = 0; at < picked.Count; at++)
        {
            var (family, combination, chosen, plan, count) = picked[at];

            if (combination is null)
            {
                continue;
            }

            var ofFamily = setups.Where(one => one.Family == family).ToArray();
            var (final, finalUnseen) = (combination, chosen);

            if (combination.Stop == PlanPoints.TypicalStop(PlanPoints.WidestOfTheGrid))
            {
                PlanCombination[] wider = [.. PlanPoints.WiderDays.Select(days => combination with { Stop = PlanPoints.TypicalStop(days) })];
                var walkedWider = Tallied(walker, ofFamily, wider, cancellation);

                foreach (var one in wider)
                {
                    refinedSince[(family, one.Words)] = walkedWider[one.Words].Since;
                }

                var widest = PlanPoints.Wider(SinceOf(family, combination)?.EdgePerPick, [.. wider.Select(one => (one, walkedWider[one.Words].Since?.EdgePerPick))]);

                refinements.Add(FormattableString.Invariant($"The {family}'s stop of {PlanPoints.WidestOfTheGrid} typical days walked wider: from {LongLedger.StandingFrom.Year} {Shown(SinceOf(family, combination)?.EdgePerPick)} at {PlanPoints.WidestOfTheGrid}, ") + string.Join(", ", wider.Select(one => FormattableString.Invariant($"{Shown(walkedWider[one.Words].Since?.EdgePerPick)} over {walkedWider[one.Words].Since?.Fills ?? 0} fill(s) at {one.Stop}"))) + (widest is null ? "; the stop stays." : $"; {widest.Stop} stands."));

                if (widest is not null)
                {
                    (final, finalUnseen) = (widest, walkedWider[widest.Words].Unseen);
                }
            }

            if (final.Entry == PlanPoints.Average || PlanPoints.Zones.Contains(final.Entry))
            {
                if (final.Stop == PlanPoints.ZoneStop)
                {
                    refinements.Add($"The {family}'s entry at {final.Entry} keeps its zone: a plain distance under the close draws no zone's stop.");
                }
                else if (Median([.. ofFamily.Where(one => one.Session.Year < LongLedger.StandingFrom.Year).Select(one => walker.DistanceOf(one, final.Entry)).OfType<double>()]) is { } distance)
                {
                    var plain = final with { Entry = PlanPoints.DistanceEntry(Math.Round(distance, 2)) };
                    var walkedPlain = Tallied(walker, ofFamily, [plain], cancellation);

                    refinedSince[(family, plain.Words)] = walkedPlain[plain.Words].Since;

                    var replaces = PlanPoints.DistanceReplaces(SinceOf(family, final)?.EdgePerPick, walkedPlain[plain.Words].Since?.EdgePerPick);

                    refinements.Add(FormattableString.Invariant($"The {family}'s entry at {final.Entry} beside {plain.Entry}, the median distance before {LongLedger.StandingFrom.Year}: from {LongLedger.StandingFrom.Year} {Shown(SinceOf(family, final)?.EdgePerPick)} against {Shown(walkedPlain[plain.Words].Since?.EdgePerPick)} over {walkedPlain[plain.Words].Since?.Fills ?? 0} fill(s); ") + (replaces ? "the plain distance stands." : FormattableString.Invariant($"the entry at {final.Entry} stays, within {PlanPoints.ZoneKeptWithin} or better.")));

                    if (replaces)
                    {
                        (final, finalUnseen) = (plain, walkedPlain[plain.Words].Unseen);
                    }
                }
                else
                {
                    refinements.Add($"The {family}'s {final.Entry} keeps its level: no pick before {LongLedger.StandingFrom.Year} drew one under its close.");
                }
            }

            picked[at] = (family, final, finalUnseen, plan, count);
        }

        var lines = new List<string>
        {
            FormattableString.Invariant($"{setups.Count} flagged setup(s) of the pullback, the breakout, the drift and the dip on the {DecisionCards.NameOf(index)}, the long ledger's from 2005 and the standing ledger's from 2019, {unread} whose stock or session the long history does not hold left out; each walked under {PlanPoints.Combinations.Count} combination(s) of an entry, a stop and a sale."),
            "A level's buy fills on the first session after the pick within its window that trades into it, at its open where it opens at or under the level and at the level otherwise; the next open fills on the session after the pick. A pick its entry does not fill, whose levels plan draws no such zone, or whose fill stands at or under its stop, is offered and not taken, and counts as nothing in a figure per pick offered.",
            "A sale is signalled by a close and sold at the next session's open, as the ledger's fill step sells: a close under the stop, at or over the target or the first band above, at or over each staged level for its equal share, under the trail, or the time limit's last close. A result is in the risk the rule's own plan set at the pick's close, so every combination holds the next-open buy's number of shares, less the published round trip for its company's size at its prices. Its edge is that less its benchmark: the rule's own plan entered on every member of the index that session in the member's own typical moves, each bought at its own next open and walked under the combination's stop and sale, a zone's stop, the first band above and the staged sales read off the member's own levels plan, a member they cannot be placed on not entered. Beside it, as a second line, the result less its index's own fund held over the pick's own sessions in the same risk. The dip's own sale, which names neither a target nor a trail, sells at the next open after a close whose 2-session RSI stands above 65, or at its time limit, the pick and its benchmark's members alike.",
            FormattableString.Invariant($"The settings were chosen on {LongLedger.StandingFrom.Year} onward, so the years before it are the years no setting was chosen on, and the recommendation reads them alone: the highest edge per pick offered among the combinations resting on {PlanPoints.FewestFills} fills or more there. It is a proposal, and nothing here changes a pick, a plan, a rule or a version."),
        };

        lines.AddRange(refinements);

        foreach (var (family, combination, chosen, plan, count) in picked)
        {
            var since = SinceOf(family, combination);
            var tradedSince = figures.GetValueOrDefault((family, traded, Chosen));

            lines.Add(combination is null
                ? FormattableString.Invariant($"The {family}: {count} setup(s); no combination rests on {PlanPoints.FewestFills} fills over the years no setting was chosen on.")
                : FormattableString.Invariant($"The {family}: {count} setup(s); recommended {combination.Words}, edge per pick offered {Shown(chosen!.EdgePerPick)} over {chosen.Fills} fill(s) of {chosen.Picks} pick(s), against the traded plan's {Shown(plan?.EdgePerPick)} over {plan?.Fills ?? 0} fill(s); from {LongLedger.StandingFrom.Year}, which it was not chosen on, {Shown(since?.EdgePerPick)} over {since?.Fills ?? 0} fill(s) against the traded plan's {Shown(tradedSince?.EdgePerPick)} over {tradedSince?.Fills ?? 0}. Against the fund held over the same sessions, {Shown(chosen.FundEdgePerPick)} against {Shown(plan?.FundEdgePerPick)}, and from {LongLedger.StandingFrom.Year} {Shown(since?.FundEdgePerPick)} against {Shown(tradedSince?.FundEdgePerPick)}."));
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
            CheckReports.Cell(one.ResultPerPick, signed: true), CheckReports.Cell(one.EdgePerPick, signed: true), CheckReports.Cell(one.EdgePerFill, signed: true), CheckReports.Cell(one.FundEdgePerPick, signed: true), CheckReports.Cell(one.DaysHeld), CheckReports.Cell(one.WorstDrawdown),
        ];

        string[] columns = ["Family", "Combination", "Years", "Picks", "Fills", "Fill rate", "Hit rate", "Break-even rate", "Result per pick", "Edge per pick", "Edge per fill", "Against the fund per pick", "Days held", "Worst drawdown"];
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
                    ("$later_fills", Or(SinceOf(family, combination)?.Fills)),
                    ("$later_edge_per_pick", Or(SinceOf(family, combination)?.EdgePerPick)),
                    ("$traded_later_edge_per_pick", Or(figures.GetValueOrDefault((family, traded, Chosen))?.EdgePerPick)),
                    ("$fund_edge_per_pick", Or(chosen?.FundEdgePerPick)),
                    ("$traded_fund_edge_per_pick", Or(plan?.FundEdgePerPick)),
                    ("$later_fund_edge_per_pick", Or(SinceOf(family, combination)?.FundEdgePerPick)),
                    ("$traded_later_fund_edge_per_pick", Or(figures.GetValueOrDefault((family, traded, Chosen))?.FundEdgePerPick)),
                    ("$traded_later_picks", Or(figures.GetValueOrDefault((family, traded, Chosen))?.Picks)),
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

    // On the operator's order of 2026-11-10, item 4, read-only: the drift as traded, on the S&P 500 beside its entry on the first
    // pullback, and on the S&P 500 and 400 with the sales check, the reported quarter's revenue over the same quarter a year
    // before, each as first filed in the SEC's facts by the setup's session, a setup whose quarters the facts do not read
    // failing it; each alone and the two together, a mean edge a pick offered per $1 risked against the ruled benchmark over
    // 2005 to 2018 and from 2019. Prints and writes nothing.
    // see: The S&P 500 drift's first-pullback entry and the drift's sales check are measured read-only over the long history
    async Task<int> DriftChecksAsync(string index, IReadOnlyList<FlaggedSetup> setups, Walker walker, CancellationToken cancellation)
    {
        var drift = setups.Where(one => one.Family == Core.Families.DriftRule.Name).ToArray();
        var quarters = await QuarterRevenuesAsync([.. drift.Select(one => one.Ticker).Distinct(StringComparer.Ordinal)], cancellation);
        var read = drift.Select(one => (Setup: one, Grew: PlanPoints.SalesGrew(quarters.GetValueOrDefault(one.Ticker), one.Session))).ToArray();
        var traded = PlanPoints.Combinations.First(PlanPrices.Traded);
        var pullback = traded with { Entry = PlanPoints.FirstPullback, Window = PlanPoints.FirstPullbackSessions };
        FlaggedSetup[] passing = [.. read.Where(one => one.Grew == true).Select(one => one.Setup)];
        var large = index == Market;

        output.WriteLine(FormattableString.Invariant($"{Verb}: the drift's checks on the {DecisionCards.NameOf(index)}: {drift.Length} flagged setup(s); the sales check passes {passing.Length}, fails {read.Count(one => one.Grew == false)} and reads no quarter for {read.Count(one => one.Grew is null)}, which fail it"));

        var rows = new List<(string Name, IReadOnlyList<FlaggedSetup> Setups, PlanCombination Plan)> { ("the drift as traded", drift, traded) };

        if (large)
        {
            rows.Add(("the first pullback alone", drift, pullback));
        }

        rows.Add(("the sales check alone", passing, traded));

        if (large)
        {
            rows.Add(("the two together", passing, pullback));
        }

        foreach (var (name, chosen, plan) in rows)
        {
            var (before, since) = Tallied(walker, chosen, [plan], cancellation)[plan.Words];

            output.WriteLine(FormattableString.Invariant($"{Verb}: {name}, {plan.Words}: 2005 to 2018 {Shown(before.EdgePerPick)} per $1 risked over {before.Fills} fill(s) of {before.Picks} pick(s); from {LongLedger.StandingFrom.Year} {Shown(since.EdgePerPick)} over {since.Fills} fill(s) of {since.Picks} pick(s)"));
        }

        output.WriteLine($"{Verb}: printed alone, nothing written");

        return 0;
    }

    // Each stock's quarters of revenue as first filed in the SEC's facts the store holds, the long facts, the standing facts
    // and the pulled revenue, found through the filer the long filings, the companies and the pulled companies name for it.
    async Task<IReadOnlyDictionary<string, IReadOnlyList<Core.Families.QuarterRevenue>>> QuarterRevenuesAsync(IReadOnlyList<string> tickers, CancellationToken cancellation)
    {
        var wanted = tickers.ToHashSet(StringComparer.Ordinal);
        var filers = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        await using var connection = new SqliteConnection(SweepHistory.ConnectionString(databaseFile));
        await connection.OpenAsync(cancellation);

        foreach (var sql in new[] { TickerFilersLong, TickerFilersCompany, TickerFilersPulled })
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;

            await using var reader = await command.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                if (wanted.Contains(reader.GetString(0)))
                {
                    (filers.TryGetValue(reader.GetString(0), out var held) ? held : filers[reader.GetString(0)] = []).Add(reader.GetString(1));
                }
            }
        }

        var needed = filers.Values.SelectMany(one => one).ToHashSet(StringComparer.Ordinal);
        var facts = new Dictionary<string, List<Core.Families.FiledRevenue>>(StringComparer.Ordinal);
        var concepts = Core.Families.FirstFiledRevenue.Concepts.ToHashSet(StringComparer.Ordinal);

        foreach (var sql in new[] { LongFactsRevenue, FiledFactsRevenue, PulledRevenue })
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;

            await using var reader = await command.ExecuteReaderAsync(cancellation);

            while (await reader.ReadAsync(cancellation))
            {
                if (needed.Contains(reader.GetString(0)) && concepts.Contains(reader.GetString(1)))
                {
                    (facts.TryGetValue(reader.GetString(0), out var held) ? held : facts[reader.GetString(0)] = []).Add(new Core.Families.FiledRevenue(
                        reader.GetString(1), Day(reader.GetString(2)), Day(reader.GetString(3)), Money.FromStorage(reader.GetString(4)), Day(reader.GetString(5)), reader.GetString(6), reader.GetString(7)));
                }
            }
        }

        return filers.ToDictionary(
            pair => pair.Key,
            pair => Core.Families.FirstFiledRevenue.Quarters(pair.Value.SelectMany(cik => facts.GetValueOrDefault(cik) ?? [])),
            StringComparer.Ordinal);
    }

    // Combinations outside the grid walked over a family's setups, each one's figures before 2019 and from it.
    static Dictionary<string, (PlanFigures Unseen, PlanFigures Since)> Tallied(Walker walker, IReadOnlyList<FlaggedSetup> setups, IReadOnlyList<PlanCombination> combinations, CancellationToken cancellation)
    {
        var walked = new PlanPick?[setups.Count][];

        Parallel.For(0, setups.Count, new ParallelOptions { CancellationToken = cancellation }, at => walked[at] = walker.PicksUnder(setups[at], combinations));

        var tallied = new Dictionary<string, (PlanFigures Unseen, PlanFigures Since)>(StringComparer.Ordinal);

        for (var at = 0; at < combinations.Count; at++)
        {
            var (unseen, since) = (new PlanTally(false), new PlanTally(false));

            foreach (var picks in walked)
            {
                if (picks.Length > at && picks[at] is { } pick)
                {
                    (pick.Year < LongLedger.StandingFrom.Year ? unseen : since).Add(pick);
                }
            }

            tallied[combinations[at].Words] = (unseen.Figures(), since.Figures());
        }

        return tallied;
    }

    // The median of a list, none where it holds nothing.
    static double? Median(IReadOnlyList<double> values)
    {
        if (values.Count == 0)
        {
            return null;
        }

        var ordered = values.Order().ToArray();
        var middle = ordered.Length / 2;

        return ordered.Length % 2 == 1 ? ordered[middle] : (ordered[middle - 1] + ordered[middle]) / 2;
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

    static DateOnly Day(string stamp) => DateOnly.ParseExact(stamp, "yyyy-MM-dd", CultureInfo.InvariantCulture);

    // The filers the long filings, the companies and the pulled companies name for a stock, and the revenue facts of each.
    const string TickerFilersLong = "SELECT DISTINCT ticker, cik FROM long_filing;";

    const string TickerFilersCompany = "SELECT DISTINCT ticker, cik FROM company WHERE cik IS NOT NULL;";

    const string TickerFilersPulled = "SELECT DISTINCT ticker, cik FROM pulled_company WHERE cik IS NOT NULL;";

    static readonly string RevenueConcepts = string.Join(", ", Core.Families.FirstFiledRevenue.Concepts.Select(concept => "'" + concept + "'"));

    static readonly string LongFactsRevenue = "SELECT cik, concept, period_start, period_end, dollars, filed, form, accession FROM long_fact WHERE concept IN (" + RevenueConcepts + ");";

    static readonly string FiledFactsRevenue = "SELECT cik, concept, period_start, period_end, dollars, filed, form, accession FROM filed_fact WHERE concept IN (" + RevenueConcepts + ");";

    static readonly string PulledRevenue = "SELECT cik, concept, period_start, period_end, dollars, filed, form, accession FROM pulled_revenue WHERE concept IN (" + RevenueConcepts + ");";

    static string Instant(DateTimeOffset at) => at.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);

    static string Shown(double? figure) => figure is { } held ? held.ToString("+0.000;-0.000;0.000", CultureInfo.InvariantCulture) : "none";

    // A pick's two edges after costs in the rule's risk: over its benchmark, the members walked under its combination's stop
    // and sale, none where no member's walk has ended, and over its index's own fund held from the close before its fill to
    // the close before its sale.
    // see: Every result over the long history is judged against the benchmark as ruled, with its index's own fund held over the same sessions as a second line
    public static (double? Edge, double? Fund) EdgesOf(double result, double fill, double rulesRisk, double? benchmark, double bought, double sold) =>
        (benchmark is { } held && double.IsFinite(held) ? result - held : null, FundLine.Edge(result, fill, rulesRisk, bought, sold));

    // Walks one setup under every combination over the long reader's series.
    public sealed class Walker(SweepSeries[] series, DateOnly[] calendar, IReadOnlyDictionary<string, List<int>> names, IReadOnlyDictionary<DateOnly, double> fund, IReadOnlyDictionary<DateOnly, string> regime)
    {
        // From the 23.8 correction every name's closes and opens once, and each session's members with their bar on it,
        // which the benchmark walks under each combination's stop and sale.
        // see: Every result over the long history is judged against the benchmark as ruled, with its index's own fund held over the same sessions as a second line
        readonly Lazy<(double[] Closes, double[] Opens)>[] prices = [.. series.Select(one => new Lazy<(double[], double[])>(() =>
            ([.. one.Bars.Select(bar => Statistic.FromPrice(bar.Close))], [.. one.Bars.Select(bar => Statistic.FromPrice(bar.Open))])))];

        // From the 23.8 correction every name's 2-session RSI once, which the dip's own sale reads.
        // see: The buy and sell points sell the dip at the next open after its 2-session RSI closes above 65
        readonly Lazy<double[]>[] rsis = [.. series.Select(one => new Lazy<double[]>(() => PlanPoints.RsiOf([.. one.Bars.Select(bar => Statistic.FromPrice(bar.Close))])))];

        readonly Lazy<List<(int Name, int Bar)>[]> members = new(() =>
        {
            var held = new List<(int Name, int Bar)>[calendar.Length];

            for (var at = 0; at < held.Length; at++)
            {
                held[at] = [];
            }

            for (var name = 0; name < series.Length; name++)
            {
                for (var bar = 0; bar < series[name].Bars.Length; bar++)
                {
                    var at = series[name].SessionAt[bar];

                    if (at >= 0 && at < held.Length && series[name].Name.MemberOn(calendar[at]))
                    {
                        held[at].Add((name, bar));
                    }
                }
            }

            return held;
        });

        readonly System.Collections.Concurrent.ConcurrentDictionary<(int At, double StopMoves, double? Reward, double? Trail, int Cap, int Zone, string Stop, string Sale), double?> benchmarks = new();

        readonly System.Collections.Concurrent.ConcurrentDictionary<(int Name, int Bar), LevelsAsOf> memberLevels = new();

        // The benchmark of a combination on a session: every member that session under the same plan in its own typical
        // moves, bought at its own next open, its stop and its sale placed as the combination places them on the pick, a
        // zone's stop, the first band and the staged sales off the member's own levels plan.
        public double? BenchmarkOf(int at, Core.Ledger.SetupPlan plan, PlanCombination combination)
        {
            var zone = combination.Stop == PlanPoints.ZoneStop ? PlanPoints.Zones.ToList().IndexOf(combination.Entry) : -1;

            return benchmarks.GetOrAdd((at, plan.StopMoves, plan.RewardToRisk, plan.TrailMoves, plan.Cap, zone, combination.Stop, combination.Exit), key =>
                PlanPoints.BenchmarkOf(members.Value[key.At].Select(member =>
                {
                    var (closes, opens) = prices[member.Name].Value;
                    var typical = series[member.Name].Atr[member.Bar];

                    if (!(typical > 0))
                    {
                        return null;
                    }

                    var close = closes[member.Bar];
                    var risk = plan.StopMoves * typical;
                    var levels = key.Zone >= 0 || key.Sale is PlanPoints.FirstBand or PlanPoints.StagedSales
                        ? memberLevels.GetOrAdd((member.Name, member.Bar), place => LevelsAt(series[place.Name], place.Bar))
                        : null;

                    double? StopAt(double fill) => key.Stop == PlanPoints.RuleStop ? close - risk
                        : key.Zone >= 0 ? (levels!.Zones.Count > key.Zone ? levels.Zones[key.Zone].Stop : null)
                        : PlanPoints.StopOf(key.Stop, fill, close - risk, null, typical);

                    PlanSale? SaleAt(double fill) => key.Sale switch
                    {
                        PlanPoints.RuleSale => plan.TrailMoves is { } moves ? new PlanSale(null, moves * typical, [])
                            : plan.RewardToRisk is { } reward ? new PlanSale(close + (reward * risk), null, [])
                            : PlanPoints.DipSale,
                        PlanPoints.FirstBand => new PlanSale(levels!.Bands.Where(edge => edge > fill).Order().Cast<double?>().FirstOrDefault(), null, []),
                        PlanPoints.StagedSales => new PlanSale(null, null, levels!.Staged),
                        _ => new PlanSale(null, null, []),
                    };

                    return PlanPoints.MemberResult(closes, opens, member.Bar, risk, StopAt, SaleAt, plan.Cap, rsis[member.Name].Value);
                })));
        }

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

        // A setup walked under combinations outside the grid, a recommendation's refinements.
        public PlanPick?[] PicksUnder(FlaggedSetup setup, IReadOnlyList<PlanCombination> combinations) =>
            Placed(setup) is { } placed ? [.. Walk(setup, placed, combinations).Select(one => one.Pick)] : [];

        public IReadOnlyList<Worked> Worked(FlaggedSetup setup) =>
            Placed(setup) is { } placed ? Walk(setup, placed) : [];

        // How far under the pick's close an entry's level stands, in the stock's typical daily moves on the session; none where
        // the entry draws no level there.
        public double? DistanceOf(FlaggedSetup setup, string entry)
        {
            if (Placed(setup) is not { } placed || !(series[placed.Name].Atr[placed.Bar] is var typical && typical > 0))
            {
                return null;
            }

            var close = placed.Closes[placed.Bar];
            var zone = PlanPoints.Zones.ToList().IndexOf(entry);
            double? level = entry == PlanPoints.Average ? (series[placed.Name].Sma20[placed.Bar] is var average && average > 0 ? average : null)
                : zone >= 0 && LevelsAt(series[placed.Name], placed.Bar) is { } levels && zone < levels.Zones.Count ? levels.Zones[zone].Top
                : null;

            return level is { } held && held < close ? (close - held) / typical : null;
        }

        IReadOnlyList<Worked> Walk(FlaggedSetup setup, (int Name, int Bar, double[] Opens, double[] Lows, double[] Closes) placed, IReadOnlyList<PlanCombination>? combinations = null)
        {
            var (name, bar, opens, lows, closes) = placed;
            var one = series[name];
            var factor = setup.Entry > 0 ? closes[bar] / setup.Entry : 1;
            var ruleStop = setup.Stop * factor;
            // From the 23.8 correction the dip's own sale on its 2-session RSI, which names no target and no trail.
            // see: The buy and sell points sell the dip at the next open after its 2-session RSI closes above 65
            var dip = setup.Family == Core.Families.DipRule.Name;
            var ruleSale = dip ? PlanPoints.DipSale : new PlanSale(setup.Target * factor, setup.Trail * factor, []);
            var rsi = rsis[name].Value;

            // Every combination's result in the risk the rule's own plan set at the pick's close, so each is read on the same
            // number of shares as the next-open buy, a buy far down at a tight stop not reading large for its small risk.
            var rulesRisk = closes[bar] - ruleStop;

            if (!(rulesRisk > 0))
            {
                return [];
            }
            var typical = one.Atr[bar];
            var levels = LevelsAt(one, bar);

            // The rule's own plan in the stock's typical moves on the session, which the benchmark enters on every member.
            var session = Array.BinarySearch(calendar, setup.Session);
            var plan = typical > 0
                ? new Core.Ledger.SetupPlan(rulesRisk / typical, ruleSale.Target is { } aim ? (aim - closes[bar]) / rulesRisk : null, ruleSale.Trail is { } trail ? trail / typical : null, setup.Cap)
                : null;
            var era = LongLedger.EraOf(setup.Session.Year);
            var side = regime.GetValueOrDefault(setup.Session) ?? PlanPoints.Below;
            var worked = new List<Worked>();

            foreach (var combination in combinations ?? PlanPoints.Combinations)
            {
                var zone = PlanPoints.Zones.ToList().IndexOf(combination.Entry);
                (double Top, double? Stop)? zoneRead = zone >= 0 && zone < levels.Zones.Count ? levels.Zones[zone] : null;
                double? level = combination.Entry == PlanPoints.NextOpen ? null
                    : combination.Entry == PlanPoints.Average ? (one.Sma20[bar] > 0 ? one.Sma20[bar] : null)
                    : PlanPoints.DistanceOf(combination.Entry) is { } days ? (typical > 0 ? closes[bar] - (days * typical) : null)
                    : zoneRead?.Top;
                var pullback = combination.Entry == PlanPoints.FirstPullback;

                Worked NotTaken(string why, double? fill = null, double? stop = null) =>
                    new(combination, level, fill, stop, combination.Exit, new PlanPick(setup.Session.Year, era, side, false, null, null, 0, null, 0, null), why);

                if (combination.Entry != PlanPoints.NextOpen && !pullback && level is null)
                {
                    worked.Add(NotTaken(zone >= 0 ? "no such zone: " + (levels.Why ?? "the levels plan draws fewer zones") : "no 20-session average"));

                    continue;
                }

                (PlanSale Sale, string Words) SaleAt(double price)
                {
                    var sale = combination.Exit switch
                    {
                        PlanPoints.RuleSale => ruleSale,
                        PlanPoints.FirstBand => new PlanSale(levels.Bands.Where(edge => edge > price).Order().Cast<double?>().FirstOrDefault(), null, []),
                        PlanPoints.StagedSales => new PlanSale(null, null, levels.Staged),
                        _ => new PlanSale(null, null, []),
                    };

                    return (sale, sale.Target is { } target ? FormattableString.Invariant($"at {target:0.00}")
                        : sale.Trail is { } trail ? FormattableString.Invariant($"trailing {trail:0.00} under the highest close")
                        : sale.Staged.Any(edge => edge > price) ? "at " + string.Join(", ", sale.Staged.Where(edge => edge > price).Order().Select(edge => edge.ToString("0.00", CultureInfo.InvariantCulture)))
                        : sale.RsiAbove is { } above ? FormattableString.Invariant($"at the next open after its {Core.Families.DipRule.RsiSessions}-session RSI closes above {above:0}, or at session {setup.Cap}")
                        : FormattableString.Invariant($"at the close of session {setup.Cap}"));
                }

                if ((pullback ? PlanPoints.PullbackFillOf(closes, opens, bar, combination.Window) : PlanPoints.FillOf(opens, lows, bar, level, combination.Window)) is not { } fill)
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

                if (PlanPoints.Walk(closes, opens, fill.Bar, fill.Price, stop, sale, setup.Cap, rsi) is not { } outcome)
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

                var benchmark = plan is null || session < 0 ? null : BenchmarkOf(session, plan, combination);
                var (edge, fundEdge) = EdgesOf(result, fill.Price, rulesRisk, benchmark, bought, sold);

                // From the 23.6 correction a pick past 20 risks is no result, counted as offered and in no figure.
                // see: A long setup whose edge lies past 20 risks either way is counted as no result and listed beside the unexplained jumps
                if (PlanPoints.NoResult(result, edge))
                {
                    worked.Add(new Worked(combination, level, fill.Price, stop, saleWords, new PlanPick(setup.Session.Year, era, side, true, null, null, 0, null, 0, null), FormattableString.Invariant($"no result: {result:+0.00;-0.00} risks after costs, past {LongLedger.OutlyingRisks} either way")));

                    continue;
                }

                var pick = new PlanPick(setup.Session.Year, era, side, true, result, edge, outcome.Sessions, outcome.End, one.SessionAt[ended], PlanPoints.BreakEvenOf(fill.Price, stop, sale.Target))
                {
                    Hit = PlanPoints.HitOf(combination.Exit, outcome.End),
                    FundEdge = fundEdge,
                };

                worked.Add(new Worked(combination, level, fill.Price, stop, saleWords, pick, FormattableString.Invariant($"{outcome.End} after {outcome.Sessions} session(s), {result:+0.00;-0.00} risks after costs")));
            }

            return worked;
        }

        // The levels plan as of a bar, from the bands and the trend the night's own functions read over the year it held.
        public static LevelsAsOf LevelsAt(SweepSeries series, int bar) => PlanTrades.LevelsAt(series, bar);
    }
}
