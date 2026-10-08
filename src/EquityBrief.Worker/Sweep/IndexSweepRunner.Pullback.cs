using System.Globalization;
using System.Net;
using System.Text;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Readings;
using EquityBrief.Core.Sweep;

namespace EquityBrief.Worker.Sweep;

// One index's pullback candidates as its search reads them: the history as it stood, each member's series and the
// index's own strength, breadth and benchmark, every candidate clearing the loosest floor its levels read with its
// benchmark filled and each trade's cost taken off its result, and the floors and the gate a candidate clears.
sealed record PullbackPool(
    string Index,
    SweepHistoryInputs Inputs,
    DateOnly[] Calendar,
    SweepSeries[] Series,
    IReadOnlyList<SweepColumns.Session> Sessions,
    SweepBenchmark.Members Members,
    SweepCandidate[] Pool,
    int Nights,
    Func<SweepCandidate, IndexQuality, bool> Clears,
    HeavyweightHistory Companies);

public sealed partial class IndexSweepRunner
{
    // The S&P 400 pullback with profit and cover read at half steps about the setting the brief names, its trades
    // replayed under three exits; and the S&P 400 and 600 searched together and confirmed on each alone.
    public const string PullbackNeighbours = "pullback-neighbours";

    public const string PullbackJoint = "pullback-joint";

    // The setting the brief names for the S&P 400's pullback with profit and cover: a strength of 0.75, a depth of 2 to
    // 4 typical moves, a trigger fresh within 8 sessions, a reward to risk of 2, a band strength of 6, the market check
    // at half the members, the dry-up off and a stop of 0.5 to 2.5 moves.
    public static DialSetting NamedPullback { get; } = new(
        SweepGrid.IndexOf(SweepGrid.Extended.StrengthBars, 0.75),
        SweepGrid.IndexOf(SweepGrid.Extended.DepthLows, 2),
        SweepGrid.IndexOf(SweepGrid.Extended.DepthHighs, 4),
        SweepGrid.IndexOf(SweepGrid.Extended.DryUpCeilings, SweepGrid.Off),
        SweepGrid.IndexOf(SweepGrid.Extended.Freshness, 8),
        SweepGrid.IndexOf(SweepGrid.Extended.RewardToRiskFloors, 2.0),
        SweepGrid.Extended.StopBounds.ToList().IndexOf((0.5, 2.5)),
        SweepGrid.IndexOf(SweepGrid.Extended.MarketFloors, 0.50),
        SweepGrid.IndexOf(SweepGrid.Extended.BandStrengths, 6));

    // One index's pool, read as its pullback search reads it.
    async Task<PullbackPool> PoolAsync(string indexCode, SweepHistory history, DateOnly through, CancellationToken cancellation)
    {
        var inputs = await history.ReadAsync(through, output.WriteLine, cancellation, index: indexCode, asItStood: true);
        var companies = await history.HeavyweightAsync(through, cancellation);
        var income = await history.IncomeAsync(through, cancellation);
        var calendar = inputs.Sessions;
        var sessionAt = calendar.Select((session, at) => (session, at)).ToDictionary(pair => pair.session, pair => pair.at);
        var firstScored = Array.FindIndex(calendar, session => session >= SweepColumns.FirstScored);
        var parallelism = Environment.ProcessorCount;
        var series = new SweepSeries[inputs.Names.Count];

        Parallel.For(0, inputs.Names.Count, name => series[name] = SweepColumns.Series(inputs.Names[name], sessionAt));

        var sessions = SweepColumns.Sessions(series, calendar);
        var members = SweepBenchmark.On(series, calendar.Length);
        var found = new List<SweepCandidate>[series.Length];

        output.WriteLine(FormattableString.Invariant($"reading the {Indices[indexCode]}'s candidates over {calendar.Length - firstScored:N0} session(s)"));
        Parallel.For(0, series.Length, name => found[name] = SweepCandidates.For(series[name], name, sessions, calendar, firstScored, firstScored, calendar.Length));

        int BarOf(SweepCandidate candidate) => Array.BinarySearch(series[candidate.Name].SessionAt, candidate.Session);

        bool ClearsAt(SweepCandidate candidate, IndexQuality quality, decimal floors) =>
            Clears(indexCode, series[candidate.Name], BarOf(candidate), income.GetValueOrDefault(series[candidate.Name].Name.Ticker) ?? [], quality, floors, companies.Companies.GetValueOrDefault(series[candidate.Name].Name.Ticker).Sector);

        var pool = found
            .SelectMany(list => list)
            .Where(candidate => BarOf(candidate) >= 0 && ClearsAt(candidate, IndexQuality.Off, 1m))
            .OrderBy(candidate => candidate.Session)
            .ThenBy(candidate => candidate.Name)
            .ToArray();

        SweepBenchmark.Fill(pool, series, members, parallelism);

        var exits = SweepAxes.Exits;

        Parallel.For(0, pool.Length, at =>
        {
            var candidate = pool[at];
            var one = series[candidate.Name];
            var bar = BarOf(candidate);
            var entry = Statistic.FromPrice(one.Bars[bar].Close);

            foreach (var outcomes in candidate.Plans)
            {
                if (outcomes is null)
                {
                    continue;
                }

                var stop = entry - (outcomes.StopMoves * one.Atr[bar]);

                for (var exit = 0; exit < exits; exit++)
                {
                    if (!float.IsNaN(outcomes.Multiple[exit]) && entry > stop)
                    {
                        outcomes.Multiple[exit] -= (float)CostInRisk(one, bar, entry, stop, outcomes.Multiple[exit], companies, 1);
                    }
                }
            }
        });

        return new PullbackPool(indexCode, inputs, calendar, series, sessions, members, pool, calendar.Length - firstScored, (candidate, quality) => ClearsAt(candidate, quality, 1m), companies);
    }

    // The named setting's half steps on the S&P 400 with profit and cover: each dial read half a grid step either side
    // with the others held, a whole-number dial at the integer nearer the setting and the dry-up off at its nearest
    // level, the stop at its two neighbouring options; each named where it falls under the floors. Then the setting's
    // own trades replayed from the listing's close under the fixed stop, the stop to break-even once a close stands a
    // risk up, and a stop trailing two typical moves under the highest close once one does, each against the same plan
    // on every member that session under the same exit, after each trade's cost.
    // see: The S&P 400 pullback with profit and cover is read at half steps about the setting the brief names and its trades under three exits
    async Task<int> PullbackNeighboursAsync(string indexCode, string named, SweepHistory history, DateOnly through, string folder, DateTimeOffset started, CancellationToken cancellation)
    {
        var read = await PoolAsync(indexCode, history, through, cancellation);
        var candidates = read.Pool.Where(candidate => read.Clears(candidate, IndexQuality.Cover)).ToArray();
        var design = SweepDesign.Live;
        var (grid, centre, neighbours) = HalfSteps(NamedPullback);
        var picks = SweepStages.Picks(candidates, design, grid);
        var at = SweepStages.Measures(picks, design, centre, ConditionSetting.Off, read.Nights);
        var rows = neighbours.Select(one => (one.Dial, one.Direction, one.Value, Measures: SweepStages.Measures(picks, design, one.Setting, ConditionSetting.Off, read.Nights))).ToArray();
        var kept = new HashSet<(int Name, int Session)>();

        SweepStages.Measures(SweepStages.Picks(candidates, design), design, NamedPullback, ConditionSetting.Off, read.Nights, kept);

        var trades = candidates.Where(candidate => kept.Contains((candidate.Name, candidate.Session))).ToArray();
        var exits = new[] { SweepExits.Fixed, SweepExits.BreakEven, SweepExits.Trail }.Select(exit => (Exit: exit, Read: ReplayExits(read, trades, exit))).ToArray();
        var page = new StringBuilder("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"><title>" + WebUtility.HtmlEncode(named) + " pullback, half steps</title><style>" + SweepReport.Style + "</style></head><body><main>");
        var breaking = rows.Where(row => !MeetsTheFloors(row.Measures)).ToArray();

        page.Append(FormattableString.Invariant($"<h1>The {WebUtility.HtmlEncode(named)} pullback with profit and cover, at half steps about the setting the brief names</h1><p class=\"survivors\">{WebUtility.HtmlEncode(Membership(named, read.Inputs))} A candidate is kept only where its close was at least $5, its mean dollar volume over the 50 sessions to it at least {MemberReadings.DollarVolumeFloor(indexCode)!.Value:N0} dollars, its four newest quarters filed before it summed above nothing and its interest cover held: {candidates.Length:N0} of the {read.Pool.Length:N0} candidates clearing the floors. Every edge is after each trade's cost at the published table's value.</p>"));
        page.Append(FormattableString.Invariant($"<p class=\"setting\" data-trades=\"{at.Scored}\" data-years=\"{YearsAbove(at)}\">The setting: {WebUtility.HtmlEncode(Words(grid, centre))}. It reads {at.Scored:N0} trades at an edge of {FamilySweepReport.Number(at.Edge)} after costs, above nothing in {YearsAbove(at)} of the 8 years, {(MeetsTheFloors(at) ? "meeting" : "short of")} the floors of {FamilySweep.TradeFloor} trades and {FamilySweep.YearsBeating} years.</p>"));
        page.Append(FormattableString.Invariant($"<p class=\"breaks\" data-breaking=\"{breaking.Length}\">{(breaking.Length == 0 ? "No half step falls under the floors." : "The half steps that fall under the floors: " + WebUtility.HtmlEncode(string.Join("; ", breaking.Select(row => row.Dial + " " + row.Direction + " at " + row.Value))) + ".")}</p>"));
        page.Append("<div class=\"table\"><table class=\"half-steps\"><thead><tr><th>Dial</th><th>Step</th><th>At</th><th>Trades</th><th>Edge after costs</th><th>Years above nothing</th><th>Meets the floors</th></tr></thead><tbody>");

        foreach (var row in rows)
        {
            page.Append(FormattableString.Invariant($"<tr><td>{WebUtility.HtmlEncode(row.Dial)}</td><td>{WebUtility.HtmlEncode(row.Direction)}</td><td>{WebUtility.HtmlEncode(row.Value)}</td><td class=\"num\">{row.Measures.Scored:N0}</td><td class=\"num\">{FamilySweepReport.Number(row.Measures.Edge)}</td><td class=\"num\">{YearsAbove(row.Measures)} of 8</td><td>{(MeetsTheFloors(row.Measures) ? "yes" : "no")}</td></tr>"));
        }

        page.Append("</tbody></table></div>");
        page.Append(FormattableString.Invariant($"<h3>Its trades under three exits</h3><p>The setting's {trades.Length:N0} trades, each replayed from the listing's close as its benchmark is entered, its stop and target where its plan puts them and held for at most {SweepAxes.Holds[^1]} sessions, on closes: a close under the stop loses and one at or over the target wins, each read before a move, which sets the next session's stop. Under the second the stop moves to the buy once a close stands a risk up; under the third it trails {SweepExits.TrailMoves:0} typical moves under the highest close once one does, never lowered. The paths are replayed in memory, since no path is stored before the ledger is built.</p>"));
        page.Append("<div class=\"table\"><table class=\"exits\"><thead><tr><th>Exit</th><th>Trades</th><th>Result after costs</th><th>Benchmark</th><th>Edge after costs</th><th>Years above nothing</th></tr></thead><tbody>");

        foreach (var (exit, one) in exits)
        {
            page.Append(FormattableString.Invariant($"<tr data-exit=\"{exit}\"><td>{WebUtility.HtmlEncode(SweepExits.Words(exit))}</td><td class=\"num\">{one.Trades:N0}</td><td class=\"num\">{FamilySweepReport.Number(one.Result)}</td><td class=\"num\">{FamilySweepReport.Number(one.Benchmark)}</td><td class=\"num\">{FamilySweepReport.Number(one.Edge)}</td><td class=\"num\">{one.YearsAbove} of 8</td></tr>"));
        }

        page.Append("</tbody></table></div>");
        page.Append(Luck(rows.Length + 1, exits.Length));
        page.Append("</main></body></html>");

        var report = WriteRun(
            folder,
            page.ToString(),
            new
            {
                index = indexCode,
                family = PullbackNeighbours,
                started,
                setting = Words(grid, centre),
                at,
                halfSteps = rows.Select(row => new { row.Dial, row.Direction, row.Value, row.Measures, meets = MeetsTheFloors(row.Measures) }),
                exits = exits.Select(one => new { exit = SweepExits.Words(one.Exit), one.Read }),
            },
            null);

        output.WriteLine(FormattableString.Invariant($"the setting: {at.Scored} trades, edge after costs {FamilySweepReport.Number(at.Edge)}, {YearsAbove(at)} of 8 years; {breaking.Length} of {rows.Length} half steps fall under the floors{(breaking.Length > 0 ? ": " + string.Join("; ", breaking.Select(row => row.Dial + " " + row.Direction)) : string.Empty)}"));

        foreach (var (exit, one) in exits)
        {
            output.WriteLine(FormattableString.Invariant($"{SweepExits.Words(exit)}: {one.Trades} trades, edge after costs {FamilySweepReport.Number(one.Edge)}, {one.YearsAbove} of 8 years"));
        }

        output.WriteLine("report " + report);

        return 0;
    }

    // The pullback's nine dials searched over the S&P 400's and 600's candidates together with profit and cover, each
    // candidate read on its own index's strength, breadth, benchmark, floors and gate and both on one calendar, by the
    // pullback search's own stages; then the provisional base, the proposal and the strongest settings each read on
    // the two together and on each index alone, the floors met together and on each.
    // see: The S&P 400's and 600's pullbacks are searched together for a shared setting and confirmed on each alone
    async Task<int> PullbackJointAsync(string indexCode, SweepHistory history, DateOnly through, string folder, DateTimeOffset started, CancellationToken cancellation)
    {
        var mid = await PoolAsync("MID", history, through, cancellation);
        var sml = await PoolAsync("SML", history, through, cancellation);

        if (!mid.Calendar.SequenceEqual(sml.Calendar))
        {
            output.WriteLine($"{Verb}: the S&P 400's and the S&P 600's histories hold different sessions, so their candidates cannot be walked on one calendar");

            return 2;
        }

        // Each index's candidates read against its own floors and gate first; the S&P 600's then numbered past the S&P
        // 400's names, so one open trade a stock is read for each stock apart.
        var midCandidates = mid.Pool.Where(candidate => mid.Clears(candidate, IndexQuality.Cover)).ToArray();
        var smlCandidates = sml.Pool.Where(candidate => sml.Clears(candidate, IndexQuality.Cover)).ToArray();

        foreach (var candidate in smlCandidates)
        {
            candidate.Name += mid.Series.Length;
        }

        var joint = midCandidates.Concat(smlCandidates).OrderBy(candidate => candidate.Session).ThenBy(candidate => candidate.Name).ToArray();
        var design = SweepDesign.Live;
        var grid = SweepGrid.Widened;
        var space = SweepSpace.For(ConditionSetting.Off.On, grid);
        var nights = mid.Nights;
        var parallelism = Environment.ProcessorCount;
        var baseSetting = grid.Carry(SweepGrid.Fine, DialSetting.LiveOnFine);
        var live = SweepStages.Direct(joint, design, baseSetting, ConditionSetting.Off, nights, grid: grid);
        var search = new SweepDesignSearch(design, SweepStages.Picks(joint, design, grid), nights, space);

        output.WriteLine(FormattableString.Invariant($"{midCandidates.Length:N0} S&P 400 and {smlCandidates.Length:N0} S&P 600 candidates clear their floors, gate and cover; searching the live design's grid over both for up to {SearchBudget.TotalHours:0} hours"));
        search.EvaluateCoarse(ConditionSetting.Off, parallelism);

        var (sampleSize, gridSize, perPoint) = search.EvaluateSample(SearchBudget, SweepSearch.Seed, parallelism);

        search.FixTheLine(SweepSearch.PlateauMargin);

        var leaders = search.LeadersOf(SweepSearch.Leaders);
        var (proposal, trailing) = search.Propose(leaders, live);
        var refinement = new List<string>();
        var extensions = new List<string>();
        var limits = new List<string>();

        if (proposal is not null)
        {
            proposal = search.Refine(proposal, live, refinement);
            proposal = search.Extend(proposal, live, refinement, extensions, limits, LooksBeyond);
        }

        var shown = new List<(string Label, int[] Point)> { ("the provisional base", space.LivePoint()) };

        if (proposal is not null)
        {
            shown.Add(("the proposal", proposal.Point));
        }

        shown.AddRange(search.Strongest(StrongestRead).Select((point, at) => (FormattableString.Invariant($"the strongest setting read, {at + 1}"), point)));

        SweepMeasures On(SweepCandidate[] candidates, int[] point) => SweepStages.Direct(candidates, design, space.Setting(point), space.Conditions(point), nights, grid: grid);

        var read = shown.Select(one => (one.Label, one.Point, Joint: On(joint, one.Point), Mid: On(midCandidates, one.Point), Small: On(smlCandidates, one.Point), Depth: DepthWords(search, space, one.Point))).ToArray();
        var confirmed = read.Where(one => MeetsTheFloors(one.Joint) && MeetsTheFloors(one.Mid) && MeetsTheFloors(one.Small)).ToArray();
        var page = new StringBuilder("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"><title>S&amp;P 400 and 600 pullback search</title><style>" + SweepReport.Style + "</style></head><body><main>");

        string Cells(SweepMeasures measures) =>
            FormattableString.Invariant($"<td class=\"num\">{measures.Scored:N0}</td><td class=\"num\">{FamilySweepReport.Number(measures.Edge)}</td><td class=\"num\">{YearsAbove(measures)} of 8</td>");

        page.Append(FormattableString.Invariant($"<h1>The S&amp;P 400's and 600's pullback with profit and cover, searched together and confirmed on each</h1><p class=\"survivors\">{WebUtility.HtmlEncode(Membership(Indices["MID"], mid.Inputs))} {WebUtility.HtmlEncode(Membership(Indices["SML"], sml.Inputs))} Each candidate clears its own index's floors, its four newest quarters summing above nothing and its interest cover held, and is read on its own index's strength, breadth and benchmark: {midCandidates.Length:N0} on the S&amp;P 400 and {smlCandidates.Length:N0} on the S&amp;P 600. Every edge is after each trade's cost.</p>"));
        page.Append(FormattableString.Invariant($"<p>The pullback sweep's second stage over the live design: {sampleSize:N0} settings sampled of {gridSize:N0} at {perPoint * 1000:0.00} ms each, {search.Evaluations:N0} read in all; the best edge {FamilySweepReport.Number(search.BestEdge)}, the plateau's line {FamilySweepReport.Number(search.Line)}, {leaders.Count} leader(s) meeting the floors over both, {trailing} trailing the provisional base in a recent year.</p>"));
        page.Append(FormattableString.Invariant($"<p class=\"confirmed\" data-confirmed=\"{confirmed.Length}\">{(confirmed.Length == 0 ? "No setting read meets the floors over both and on each index alone." : WebUtility.HtmlEncode(string.Join("; ", confirmed.Select(one => one.Label))) + " meet the floors over both and on each index alone.")} The floors are {FamilySweep.TradeFloor} trades and an edge after costs above nothing in {FamilySweep.YearsBeating} of the 8 years.</p>"));
        page.Append("<div class=\"table\"><table class=\"joint\"><thead><tr><th>Read</th><th>Setting</th><th>Depth</th><th>Both: trades</th><th>Edge</th><th>Years</th><th>S&amp;P 400: trades</th><th>Edge</th><th>Years</th><th>S&amp;P 600: trades</th><th>Edge</th><th>Years</th></tr></thead><tbody>");

        foreach (var one in read)
        {
            page.Append($"<tr><td>{WebUtility.HtmlEncode(one.Label)}</td><td>{WebUtility.HtmlEncode(space.Describe(one.Point))}</td><td>{WebUtility.HtmlEncode(one.Depth)}</td>{Cells(one.Joint)}{Cells(one.Mid)}{Cells(one.Small)}</tr>");
        }

        page.Append("</tbody></table></div>");

        if (refinement.Count + extensions.Count + limits.Count > 0)
        {
            page.Append("<h3>The proposal's refinement</h3><ul>");

            foreach (var line in refinement.Concat(extensions).Concat(limits))
            {
                page.Append($"<li>{WebUtility.HtmlEncode(line)}</li>");
            }

            page.Append("</ul>");
        }

        page.Append(Luck(search.Evaluations, 0));
        page.Append("</main></body></html>");

        var report = WriteRun(
            folder,
            page.ToString(),
            new
            {
                index = "MID+SML",
                family = PullbackJoint,
                started,
                sampleSize,
                gridSize,
                evaluations = search.Evaluations,
                bestEdge = search.BestEdge,
                line = search.Line,
                leaders = leaders.Count,
                read = read.Select(one => new { one.Label, setting = space.Describe(one.Point), one.Depth, joint = one.Joint, mid = one.Mid, small = one.Small }),
                confirmed = confirmed.Select(one => one.Label),
                extensions,
                limits,
            },
            null);

        foreach (var one in read)
        {
            output.WriteLine(FormattableString.Invariant($"{one.Label}: both {one.Joint.Scored} trades at {FamilySweepReport.Number(one.Joint.Edge)}, {YearsAbove(one.Joint)} of 8; S&P 400 {one.Mid.Scored} at {FamilySweepReport.Number(one.Mid.Edge)}, {YearsAbove(one.Mid)}; S&P 600 {one.Small.Scored} at {FamilySweepReport.Number(one.Small.Edge)}, {YearsAbove(one.Small)}; depth {one.Depth}"));
        }

        output.WriteLine(FormattableString.Invariant($"{confirmed.Length} setting(s) meet the floors over both and on each alone"));
        output.WriteLine("report " + report);

        return 0;
    }

    // A setting's depth on the plateau and the dial that binds it, the direction a step leaves the plateau or meets a
    // grid end.
    static string DepthWords(SweepDesignSearch search, SweepSpace space, int[] point)
    {
        var depth = search.Depth(point);

        return depth.Dial < 0
            ? FormattableString.Invariant($"{depth.Depth}, no dial binding it")
            : FormattableString.Invariant($"{depth.Depth}, bound by the {space.Dials[depth.Dial].Name} going {(depth.Direction < 0 ? "lower" : "higher")}{(depth.AtAGridEnd ? " at a grid end" : string.Empty)}");
    }

    // The grid of half steps about a setting: each ordered dial holding the setting's value with half a step either
    // side, a whole-number dial the integer nearer the setting, an end the grid holds no value past read on the one
    // side alone, the dry-up off read at its nearest level and the market check off at its lowest floor; the stop's
    // options the extended grid's own, read at the two beside the setting's. Each neighbour moves one dial.
    public static (SweepGrid Grid, DialSetting Centre, IReadOnlyList<(string Dial, string Direction, string Value, DialSetting Setting)> Neighbours) HalfSteps(DialSetting setting)
    {
        var extended = SweepGrid.Extended;
        var neighbours = new List<(string Dial, string Direction, string Value, DialSetting Setting)>();

        static (IReadOnlyList<double> Levels, int Centre) Halves(IReadOnlyList<double> levels, int at, bool whole)
        {
            var value = levels[at];

            if (double.IsInfinity(value))
            {
                var nearest = double.IsPositiveInfinity(value) ? levels.Where(double.IsFinite).Max() : levels.Where(double.IsFinite).Min();

                return (double.IsPositiveInfinity(value) ? [nearest, value] : [value, nearest], double.IsPositiveInfinity(value) ? 1 : 0);
            }

            var list = new List<double>();

            if (at > 0 && double.IsFinite(levels[at - 1]))
            {
                list.Add(whole ? Math.Ceiling((levels[at - 1] + value) / 2) : (levels[at - 1] + value) / 2);
            }

            var centre = list.Count;

            list.Add(value);

            if (at < levels.Count - 1 && double.IsFinite(levels[at + 1]))
            {
                list.Add(whole ? Math.Floor((value + levels[at + 1]) / 2) : (value + levels[at + 1]) / 2);
            }

            return (list, centre);
        }

        var strength = Halves(extended.StrengthBars, setting.Strength, false);
        var depthLow = Halves(extended.DepthLows, setting.DepthLow, false);
        var depthHigh = Halves(extended.DepthHighs, setting.DepthHigh, false);
        var dryUp = Halves(extended.DryUpCeilings, setting.DryUp, false);
        var fresh = Halves([.. extended.Freshness.Select(value => 1.0 * value)], setting.Freshness, true);
        var reward = Halves(extended.RewardToRiskFloors, setting.RewardToRisk, false);
        var market = Halves(extended.MarketFloors, setting.Market, false);
        var band = Halves([.. extended.BandStrengths.Select(value => 1.0 * value)], setting.Band, true);
        var grid = new SweepGrid(
            strength.Levels,
            depthLow.Levels,
            depthHigh.Levels,
            dryUp.Levels,
            [.. fresh.Levels.Select(value => (int)value)],
            reward.Levels,
            extended.StopBounds,
            market.Levels,
            [.. band.Levels.Select(value => (int)value)]);
        var centre = new DialSetting(strength.Centre, depthLow.Centre, depthHigh.Centre, dryUp.Centre, fresh.Centre, reward.Centre, setting.Stop, market.Centre, band.Centre);

        void Add(string dial, (IReadOnlyList<double> Levels, int Centre) halves, Func<DialSetting, int, DialSetting> move)
        {
            for (var at = 0; at < halves.Levels.Count; at++)
            {
                if (at != halves.Centre)
                {
                    neighbours.Add((dial, at < halves.Centre ? "lower" : "higher", Label(halves.Levels[at]), move(centre, at)));
                }
            }
        }

        Add("strength", strength, (one, at) => one with { Strength = at });
        Add("depth's low end", depthLow, (one, at) => one with { DepthLow = at });
        Add("depth's high end", depthHigh, (one, at) => one with { DepthHigh = at });
        Add("dry-up", dryUp, (one, at) => one with { DryUp = at });
        Add("freshness", fresh, (one, at) => one with { Freshness = at });
        Add("reward to risk", reward, (one, at) => one with { RewardToRisk = at });
        Add("market check", market, (one, at) => one with { Market = at });
        Add("band strength", band, (one, at) => one with { Band = at });

        var (low, high) = extended.StopBounds[setting.Stop];

        for (var option = 0; option < extended.StopBounds.Count; option++)
        {
            var (otherLow, otherHigh) = extended.StopBounds[option];

            if (option != setting.Stop && (otherLow == low) != (otherHigh == high))
            {
                neighbours.Add(("stop", otherLow != low ? (otherLow < low ? "lower" : "higher") + " at its low end" : (otherHigh < high ? "lower" : "higher") + " at its high end", Label(otherLow) + " to " + Label(otherHigh), centre with { Stop = option }));
            }
        }

        return (grid, centre, neighbours);
    }

    static string Label(double value) =>
        double.IsPositiveInfinity(value) || double.IsNegativeInfinity(value) ? "off" : value.ToString("0.###", CultureInfo.InvariantCulture);

    // A setting on a grid in words, each dial named with its value.
    static string Words(SweepGrid grid, DialSetting setting)
    {
        var (low, high) = grid.StopBounds[setting.Stop];

        return string.Join(
            ", ",
            "strength " + Label(grid.StrengthBars[setting.Strength]),
            "depth " + Label(grid.DepthLows[setting.DepthLow]) + " to " + Label(grid.DepthHighs[setting.DepthHigh]),
            "dry-up " + Label(grid.DryUpCeilings[setting.DryUp]),
            "freshness " + grid.Freshness[setting.Freshness].ToString(CultureInfo.InvariantCulture),
            "reward to risk " + Label(grid.RewardToRiskFloors[setting.RewardToRisk]),
            "stop " + Label(low) + " to " + Label(high),
            "market " + Label(grid.MarketFloors[setting.Market]),
            "band strength " + grid.BandStrengths[setting.Band].ToString(CultureInfo.InvariantCulture));
    }

    // A set of trades replayed under one exit, each against the same plan on every member that session.
    static SweepExitRead ReplayExits(PullbackPool read, IReadOnlyList<SweepCandidate> trades, int exit)
    {
        var closes = read.Series.Select(one => one.Bars.Select(bar => Statistic.FromPrice(bar.Close)).ToArray()).ToArray();
        var cap = SweepAxes.Holds[^1];
        var planAt = SweepCandidate.PlanAt(SweepDesign.Live.Plan, SweepDesign.Live.Support);
        var results = new List<(int Year, double Result, double Benchmark)>();

        foreach (var candidate in trades)
        {
            if (candidate.Plans[planAt] is not { } plan)
            {
                continue;
            }

            var one = read.Series[candidate.Name];
            var bar = Array.BinarySearch(one.SessionAt, candidate.Session);
            var move = one.Atr[bar];
            var entry = closes[candidate.Name][bar];
            var risk = plan.StopMoves * move;

            if (!(risk > 0) || SweepExits.Replay(closes[candidate.Name], bar, entry, entry - risk, entry + (plan.RewardToRisk * risk), move, exit, cap) is not { } result)
            {
                continue;
            }

            var cost = CostInRisk(one, bar, entry, entry - risk, result, read.Companies, 1);
            var benchmark = SweepExits.Benchmark(read.Series, closes, read.Members, candidate.Session, plan.StopMoves, plan.RewardToRisk, exit, cap);

            if (!double.IsNaN(benchmark))
            {
                results.Add((candidate.Year, result - cost, benchmark));
            }
        }

        var years = Enumerable.Range(0, SweepFigures.Years).Select(year => results.Where(one => one.Year == year).ToArray()).ToArray();

        return new SweepExitRead(
            results.Count,
            results.Count > 0 ? results.Average(one => one.Result) : null,
            results.Count > 0 ? results.Average(one => one.Benchmark) : null,
            results.Count > 0 ? results.Average(one => one.Result - one.Benchmark) : null,
            years.Count(inYear => inYear.Length > 0 && inYear.Average(one => one.Result - one.Benchmark) > 0));
    }
}

// What a set of trades came to under one exit: how many, their mean result after costs, the mean of their benchmarks,
// the mean edge, and the years whose edge stood above nothing.
public sealed record SweepExitRead(int Trades, double? Result, double? Benchmark, double? Edge, int YearsAbove);
