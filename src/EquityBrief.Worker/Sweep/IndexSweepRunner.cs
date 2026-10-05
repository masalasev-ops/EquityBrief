using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;
using EquityBrief.Core.Configuration;
using EquityBrief.Core.Families;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Readings;
using EquityBrief.Core.Sweep;
using EquityBrief.Core.Time;

namespace EquityBrief.Worker.Sweep;

// The quality a 400 or 600 rule may hold a member to: none, the profit gate, or the profit gate and the interest cover.
// The fourth level, the state as well, reads quarters the pulled history does not hold as they stood, and is not swept.
public enum IndexQuality
{
    Off,
    Profit,
    Cover,
}

// A setup family's sweep on the S&P 400 or the S&P 600 alone, by hand: the history read once from the live store,
// read-only, over the index's members today, each read as a member on every session, so every figure holds survivors
// only; the index's own strength, market check and benchmark; a listing kept where its close clears $5, its mean dollar
// volume over 50 sessions clears the index's floor and its four newest quarters filed before the session sum above
// nothing; and every trade's result read after its cost at the published table's value, the edge before costs and at
// double the cost beside it. The proposal reads the edge after costs. It writes nothing to the store.
// see: Each index runs every family as rules of its own, ranked and benchmarked on that index's members alone
// see: A 400 or 600 trade pays the published effective spread for its size and price, and its pass tests read the edge after it
// see: The 400 and 600 rules start provisional with liquidity floors and a profit gate before any testing
public sealed class IndexSweepRunner(IClock clock, string databaseFile, string dataRoot, string? configuredFolder, TextWriter output)
{
    public const string Verb = "sweep-index";

    public const string FiguresFile = "figures.json";

    // The indices a sweep of their own is read for, and the name each is called by.
    public static IReadOnlyDictionary<string, string> Indices { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["MID"] = "S&P 400",
        ["SML"] = "S&P 600",
    };

    // The families a sweep of an index is built for so far, and the words its report names each by: the pullback's
    // base at its provisional settings alone, its nine dials' search to come, and the sector heavyweights' design (a).
    public static IReadOnlyDictionary<string, string> Families { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        [Pullback] = "pullback's base",
        [PullbackSearch] = "pullback's nine dials",
        [BreakoutRule.Name] = "breakouts'",
        [DriftRule.Name] = "earnings drift's",
        [Heavyweights] = "sector heavyweights'",
    };

    public const string Pullback = "pullback";

    public const string PullbackSearch = "pullback-search";

    // The longest the pullback's search samples its grid for, the pullback sweep's own budget a design.
    public static readonly TimeSpan SearchBudget = SweepSearch.SampleBudget;

    public const string Heavyweights = "heavyweights";

    // The fund each index's heavyweights read a leader's beta against.
    public static IReadOnlyDictionary<string, string> IndexFunds { get; } = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["MID"] = "IJH",
        ["SML"] = "IJR",
    };

    // Given a stop floor, the drift's stop is held that many typical moves under the buy where the reaction's low sits
    // nearer, which a risk a hair wide would otherwise turn into a result of thousands of risks.
    public async Task<int> RunAsync(string indexCode, string family, CancellationToken cancellation = default, double stopFloor = 0, bool survivorsOnly = false)
    {
        if (!Indices.TryGetValue(indexCode, out var named))
        {
            output.WriteLine($"{Verb}: name an index with '--index', one of {string.Join(", ", Indices.Keys)}");

            return 2;
        }

        if (!Families.TryGetValue(family, out var words))
        {
            output.WriteLine($"{Verb}: name a family with '--family', one of {string.Join(", ", Families.Keys)}");

            return 2;
        }

        if (NightLock.Holder(dataRoot) is { } holder)
        {
            output.WriteLine($"{Verb}: the night holds the store ({holder}); run it once the night has finished");

            return 2;
        }

        if (SweepRunner.InTheNightsWindow(clock.UtcNow, FamilySweepRunner.Expected))
        {
            output.WriteLine(FormattableString.Invariant($"{Verb}: a run started now would reach the night's window, which begins at {SweepRunner.PauseFrom:hh\\:mm} UTC on a weekday; run it after the night"));

            return 2;
        }

        var started = clock.UtcNow;
        var history = new SweepHistory(databaseFile);
        var through = await history.NewestSessionAsync(cancellation);
        var inputs = await history.ReadAsync(through, output.WriteLine, cancellation, index: indexCode, asItStood: !survivorsOnly);

        if (inputs.Names.Count == 0)
        {
            output.WriteLine($"{Verb}: the store holds no member of the {named}; pull them first with 'history-pull --members --index {indexCode}', then their history");

            return 2;
        }

        var companies = await history.HeavyweightAsync(through, cancellation);
        var income = await history.IncomeAsync(through, cancellation);
        var folder = Path.Combine(SweepFolder.Resolve(configuredFolder, dataRoot), SweepFolder.RunName(started));

        Directory.CreateDirectory(folder);
        output.WriteLine("run " + Path.GetFileName(folder));

        if (family == Pullback)
        {
            return await PullbackAsync(indexCode, named, words, history, through, inputs, companies, income, folder, cancellation);
        }

        if (family == PullbackSearch)
        {
            return PullbackSearchRun(indexCode, named, words, inputs, companies, income, folder, started);
        }

        if (family == Heavyweights)
        {
            return await HeavyweightsAsync(indexCode, named, words, history, through, inputs, companies, income, folder, cancellation);
        }

        var calendar = inputs.Sessions;
        var sessionAt = calendar.Select((session, at) => (session, at)).ToDictionary(pair => pair.session, pair => pair.at);
        var firstScored = Array.FindIndex(calendar, session => session >= SweepColumns.FirstScored);
        var nights = calendar.Length - firstScored;
        var series = new SweepSeries[inputs.Names.Count];

        Parallel.For(0, inputs.Names.Count, name => series[name] = SweepColumns.Series(inputs.Names[name], sessionAt));

        var sessions = SweepColumns.Sessions(series, calendar);
        var members = SweepBenchmark.On(series, calendar.Length);
        var tickers = inputs.Names.Select(name => name.Ticker).ToArray();
        var open = Enumerable.Range(firstScored, nights).Count(session => sessions[session].Breadth >= FamilySweep.MarketFloor);
        var withIncome = tickers.Count(ticker => income.ContainsKey(ticker));

        int YearOf(int session) => calendar[session].Year - SweepColumns.FirstScored.Year;

        var floored = family == DriftRule.Name && stopFloor > 0;
        var adapter = FamilySweepRunner.For(family, series, sessions, members, firstScored, calendar, floored ? stopFloor : 0);
        var read = new List<(int[] Setting, FamilyFigures Figures)>();
        var before = new Dictionary<string, FamilyFigures>(StringComparer.Ordinal);
        var doubled = new Dictionary<string, FamilyFigures>(StringComparer.Ordinal);
        var (listed, kept) = (0L, 0L);

        FamilyTrade After(FamilyTrade trade, int multiple) =>
            trade.Result is { } result ? trade with { Result = result - CostInRisk(series[trade.Listing.Name], trade.Listing, result, companies, multiple) } : trade;

        foreach (var setting in adapter.Grid.Settings)
        {
            var listings = adapter.Listings(setting).ToArray();
            var clearing = listings.Where(listing => Clears(indexCode, series[listing.Name], listing.Bar, income.GetValueOrDefault(tickers[listing.Name]) ?? [])).ToArray();
            var trades = FamilySweep.Walk(clearing, tickers, YearOf, adapter.Exit, adapter.Benchmark);
            var key = adapter.Grid.Key(setting);

            listed += listings.Length;
            kept += clearing.Length;
            before[key] = FamilySweep.Figures(key, trades, nights);
            read.Add((setting, FamilySweep.Figures(key, [.. trades.Select(trade => After(trade, 1))], nights)));
            doubled[key] = FamilySweep.Figures(key, [.. trades.Select(trade => After(trade, TradeCost.Doubled))], nights);
        }

        var proposal = FamilySweep.Propose(adapter.Grid, read);
        var note = FormattableString.Invariant(
            $"{Membership(named, inputs)} A listing is kept only where its close was at least $5, its mean dollar volume over the 50 sessions to it at least {MemberReadings.DollarVolumeFloor(indexCode)!.Value:N0} dollars and its four newest quarters filed before it summed above nothing; {kept:N0} of the {listed:N0} listings every setting made together cleared them, and {withIncome:N0} of the {inputs.Names.Count:N0} members hold quarters of income. Every edge on this page is after each trade's cost at the published table's value, and the table below sets the edge before costs and at double the cost beside it.");
        var run = new FamilySweepRun(family, $"{named} {words}", calendar[firstScored], through, inputs.Names.Count, nights, open, adapter.Readings, started, clock.UtcNow, floored ? note + FormattableString.Invariant($" The stop is held at least {stopFloor:0.##} typical moves under the buy where the reaction's low sits nearer, as the drift's stop floor variant holds it.") : note);
        var provisional = adapter.Grid.Key([.. adapter.Grid.Provisional]);
        var shown = new List<string> { provisional };

        if (proposal.Proposed is { } proposed)
        {
            shown.Add(proposed.Key);
        }
        else
        {
            shown.AddRange(SweepNonePassed.StrongestOf(read.Select(one => new Strongest(one.Figures.Key, one.Figures.Trades, one.Figures.YearsBeating, one.Figures.Edge))).Select(one => one.Key));
        }

        var report = Path.Combine(folder, SweepFolder.ReportFile);
        var figures = Path.Combine(folder, FiguresFile);

        File.WriteAllText(report, FamilySweepReport.Build(run, adapter.Grid, read, proposal) + Costs([.. shown.Distinct(StringComparer.Ordinal)], before, read.ToDictionary(one => one.Figures.Key, one => one.Figures, StringComparer.Ordinal), doubled));
        File.WriteAllText(figures, JsonSerializer.Serialize(
            new
            {
                index = indexCode,
                run,
                listed,
                kept,
                proposal = proposal.Proposed?.Key,
                afterCosts = read.Select(one => one.Figures),
                beforeCosts = before.Values,
                atDoubleCost = doubled.Values,
            },
            SweepRunner.Json));

        output.WriteLine(proposal.Proposed is { } shownProposal
            ? FormattableString.Invariant($"proposed {shownProposal.Key}, edge after costs {FamilySweepReport.Number(shownProposal.Edge)} over {shownProposal.Trades} trades, before costs {FamilySweepReport.Number(before[shownProposal.Key].Edge)}, at double {FamilySweepReport.Number(doubled[shownProposal.Key].Edge)}")
            : "none passed: no setting meets the floors after costs, and the report states the strongest settings and what could be tried next");
        output.WriteLine("report " + report);

        return 0;
    }

    // The pullback's base on the index alone: the ideas' run's replay of the live design's picks with the base's reward
    // to risk floor, read among the index's members, a listing kept only where it clears the floors and the gate, and
    // each trade's result after its cost. Its nine dials' search is to come.
    async Task<int> PullbackAsync(string indexCode, string named, string words, SweepHistory history, DateOnly through, SweepHistoryInputs inputs, HeavyweightHistory companies, IReadOnlyDictionary<string, IReadOnlyList<FiledIncome>> income, string folder, CancellationToken cancellation)
    {
        var market = await history.MarketAsync(through, cancellation);
        var (replay, ideas) = SweepIdeasRunner.Read(inputs, market, output.WriteLine);
        var series = replay.Series;
        var tickers = series.Select(one => one.Name.Ticker).ToArray();
        var (listed, kept) = (0L, 0L);

        bool Keep(int name, int bar)
        {
            var clears = Clears(indexCode, series[name], bar, income.GetValueOrDefault(tickers[name]) ?? []);

            listed++;
            kept += clears ? 1 : 0;

            return clears;
        }

        var unfiltered = replay.Trades(SweepIdeas.BaseRule);
        var trades = replay.Trades(SweepIdeas.BaseRule, Keep);

        IdeaTrade After(IdeaTrade trade, int multiple)
        {
            if (trade.Result is not { } result)
            {
                return trade;
            }

            var one = series[trade.Listing.Name];
            var entry = Statistic.FromPrice(one.Bars[trade.Listing.Bar].Close);
            var stop = entry - (trade.Listing.StopMoves * one.Atr[trade.Listing.Bar]);

            return trade with { Result = result - CostInRisk(one, trade.Listing.Bar, entry, stop, result, companies, multiple) };
        }

        var nights = ideas.ScoredNights;
        var all = SweepIdeas.Figures("the base, with no floors or gate", unfiltered, nights);
        var before = SweepIdeas.Figures("the base, before costs", trades, nights);
        var after = SweepIdeas.Figures("the base, after costs", [.. trades.Select(trade => After(trade, 1))], nights);
        var doubled = SweepIdeas.Figures("the base, at double the cost", [.. trades.Select(trade => After(trade, TradeCost.Doubled))], nights);
        var note = FormattableString.Invariant($"{Membership(named, inputs)} A listing is kept only where its close was at least $5, its mean dollar volume over the 50 sessions to it at least {MemberReadings.DollarVolumeFloor(indexCode)!.Value:N0} dollars and its four newest quarters filed before it summed above nothing; {kept:N0} of the {listed:N0} listings the base made cleared them.");
        var page = new StringBuilder("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"><title>" + WebUtility.HtmlEncode(named) + " pullback base</title><style>" + SweepReport.Style + "</style></head><body><main>");

        page.Append(FormattableString.Invariant($"<h1>The {WebUtility.HtmlEncode(named)} {WebUtility.HtmlEncode(words)}, at its provisional settings</h1><p class=\"survivors\">{WebUtility.HtmlEncode(note)}</p><div class=\"table\"><table><thead><tr><th>Read</th><th>Trades</th><th>Edge</th><th>Error</th><th>2024 to 2026 edge</th><th>Without the five largest</th><th>Years above nothing</th><th>Near stops</th></tr></thead><tbody>"));

        foreach (var figures in new[] { all, before, after, doubled })
        {
            page.Append(FormattableString.Invariant($"<tr><td>{WebUtility.HtmlEncode(figures.Key)}</td><td class=\"num\">{figures.Trades:N0}</td><td class=\"num\">{SweepIdeasReport.Number(figures.Edge)}</td><td class=\"num\">{SweepIdeasReport.Number(figures.StandardError)}</td><td class=\"num\">{SweepIdeasReport.Number(figures.RecentEdge)}</td><td class=\"num\">{SweepIdeasReport.Number(figures.EdgeWithoutLargest)}</td><td class=\"num\">{figures.YearEdge.Count(edge => edge > 0)} of 8</td><td class=\"num\">{SweepIdeasReport.Number(figures.CloseStops)}</td></tr>"));
        }

        page.Append("</tbody></table></div></main></body></html>");

        var report = Path.Combine(folder, SweepFolder.ReportFile);

        File.WriteAllText(report, page.ToString());
        File.WriteAllText(Path.Combine(folder, FiguresFile), JsonSerializer.Serialize(new { index = indexCode, family = Pullback, note, listed, kept, all, before, after, doubled }, SweepRunner.Json));

        output.WriteLine(FormattableString.Invariant($"the base: edge after costs {SweepIdeasReport.Number(after.Edge)} over {after.Trades} trades, before costs {SweepIdeasReport.Number(before.Edge)}, at double {SweepIdeasReport.Number(doubled.Edge)}, with no floors or gate {SweepIdeasReport.Number(all.Edge)} over {all.Trades}"));
        output.WriteLine("report " + report);

        return 0;
    }

    // The pullback's nine dials on the index alone, searched by the pullback sweep's own second stage for the live design:
    // every member's candidates as the history stood, its strength, market check and benchmark read among the index's
    // own members, a candidate kept only where it clears the floors and the gate on its session, and every trade's
    // result after its cost at the table's value, its benchmark paying none; the provisional base's setting read beside
    // the search's proposal, each before costs and at double the cost as well. A search proposing nothing, or a
    // proposal short of the floors after costs, brings the strongest settings and what could be tried next.
    // see: A trade's cost comes off the trade and not its benchmark
    // see: No family on any index is set aside or hidden by a test result without the operator's word
    int PullbackSearchRun(string indexCode, string named, string words, SweepHistoryInputs inputs, HeavyweightHistory companies, IReadOnlyDictionary<string, IReadOnlyList<FiledIncome>> income, string folder, DateTimeOffset started)
    {
        var calendar = inputs.Sessions;
        var sessionAt = calendar.Select((session, at) => (session, at)).ToDictionary(pair => pair.session, pair => pair.at);
        var firstScored = Array.FindIndex(calendar, session => session >= SweepColumns.FirstScored);
        var nights = calendar.Length - firstScored;
        var parallelism = Environment.ProcessorCount;
        var series = new SweepSeries[inputs.Names.Count];

        Parallel.For(0, inputs.Names.Count, name => series[name] = SweepColumns.Series(inputs.Names[name], sessionAt));

        var sessions = SweepColumns.Sessions(series, calendar);
        var members = SweepBenchmark.On(series, calendar.Length);
        var found = new List<SweepCandidate>[series.Length];

        output.WriteLine(FormattableString.Invariant($"reading the candidates of {series.Length:N0} name(s) over {nights:N0} session(s)"));
        Parallel.For(0, series.Length, name => found[name] = SweepCandidates.For(series[name], name, sessions, calendar, firstScored, firstScored, calendar.Length));

        int BarOf(SweepCandidate candidate) => Array.BinarySearch(series[candidate.Name].SessionAt, candidate.Session);

        var all = found.SelectMany(list => list).ToArray();
        var candidates = all
            .Where(candidate => BarOf(candidate) is var bar && bar >= 0 && Clears(indexCode, series[candidate.Name], bar, income.GetValueOrDefault(series[candidate.Name].Name.Ticker) ?? []))
            .OrderBy(candidate => candidate.Session)
            .ThenBy(candidate => candidate.Name)
            .ToArray();

        SweepBenchmark.Fill(candidates, series, members, parallelism);

        // Each trade's cost in multiples of its risk, a candidate's plans and exits apiece, taken off its result and put
        // back to read the edge before costs or at double.
        var exits = SweepAxes.Exits;
        var costs = new float[candidates.Length][];

        Parallel.For(0, candidates.Length, at =>
        {
            var candidate = candidates[at];
            var one = series[candidate.Name];
            var bar = BarOf(candidate);
            var entry = Statistic.FromPrice(one.Bars[bar].Close);
            var row = new float[candidate.Plans.Length * exits];

            for (var plan = 0; plan < candidate.Plans.Length; plan++)
            {
                if (candidate.Plans[plan] is not { } outcomes)
                {
                    continue;
                }

                var stop = entry - (outcomes.StopMoves * one.Atr[bar]);

                for (var exit = 0; exit < exits; exit++)
                {
                    if (!float.IsNaN(outcomes.Multiple[exit]) && entry > stop)
                    {
                        row[(plan * exits) + exit] = (float)CostInRisk(one, bar, entry, stop, outcomes.Multiple[exit], companies, 1);
                    }
                }
            }

            costs[at] = row;
        });

        void Charge(int multiple)
        {
            for (var at = 0; at < candidates.Length; at++)
            {
                for (var plan = 0; plan < candidates[at].Plans.Length; plan++)
                {
                    if (candidates[at].Plans[plan] is not { } outcomes)
                    {
                        continue;
                    }

                    for (var exit = 0; exit < exits; exit++)
                    {
                        outcomes.Multiple[exit] -= multiple * costs[at][(plan * exits) + exit];
                    }
                }
            }
        }

        var design = SweepDesign.Live;
        var space = SweepSpace.For(ConditionSetting.Off.On);
        var baseSetting = SweepGrid.Extended.Carry(SweepGrid.Fine, DialSetting.LiveOnFine);

        Charge(1);

        var live = SweepStages.Direct(candidates, design, baseSetting, ConditionSetting.Off, nights);
        var picks = SweepStages.Picks(candidates, design);
        var search = new SweepDesignSearch(design, picks, nights, space);

        output.WriteLine(FormattableString.Invariant($"{candidates.Length:N0} of {all.Length:N0} candidates clear the floors and the gate; searching the live design's grid for up to {SearchBudget.TotalHours:0} hours"));
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
            proposal = search.Extend(proposal, live, refinement, extensions, limits);
        }

        var strongest = search.Strongest(StrongestRead);
        var shown = new List<(string Label, int[] Point)> { ("the provisional base", space.LivePoint()) };

        if (proposal is not null)
        {
            shown.Add(("the proposal", proposal.Point));
        }

        shown.AddRange(strongest.Select((point, at) => (FormattableString.Invariant($"the strongest setting read, {at + 1}"), point)));

        SweepMeasures MeasuresOf(int[] point) => SweepStages.Direct(candidates, design, space.Setting(point), space.Conditions(point), nights);

        var after = shown.Select(one => MeasuresOf(one.Point)).ToArray();

        Charge(-1);

        var before = shown.Select(one => MeasuresOf(one.Point)).ToArray();

        Charge(2);

        var doubled = shown.Select(one => MeasuresOf(one.Point)).ToArray();

        Charge(-1);

        static int YearsAbove(SweepMeasures measures) => measures.YearEdge.Count(edge => edge is > 0);

        bool Meets(SweepMeasures measures) => measures.Scored >= FamilySweep.TradeFloor && YearsAbove(measures) >= FamilySweep.YearsBeating;

        var proposed = proposal is not null && Meets(after[1]);
        var note = FormattableString.Invariant($"{Membership(named, inputs)} A candidate is kept only where its close was at least $5, its mean dollar volume over the 50 sessions to it at least {MemberReadings.DollarVolumeFloor(indexCode)!.Value:N0} dollars and its four newest quarters filed before it summed above nothing: {candidates.Length:N0} of the {all.Length:N0} candidates the members made. Every edge is after each trade's cost at the published table's value, its benchmark the same plan on every member of the index paying none, and the table sets the edge before costs and at double beside it.");
        var page = new StringBuilder("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"><title>" + WebUtility.HtmlEncode(named) + " pullback search</title><style>" + SweepReport.Style + "</style></head><body><main>");

        page.Append(FormattableString.Invariant($"<h1>The {WebUtility.HtmlEncode(named)} {WebUtility.HtmlEncode(words)}, searched on its own members</h1><p class=\"survivors\">{WebUtility.HtmlEncode(note)}</p>"));
        page.Append(FormattableString.Invariant($"<p>The pullback sweep's second stage over the live design: {sampleSize:N0} settings sampled of {gridSize:N0} on its grid at {perPoint * 1000:0.00} ms each, {search.Evaluations:N0} read in all; the best edge {FamilySweepReport.Number(search.BestEdge)}, the plateau's line {FamilySweepReport.Number(search.Line)}, {leaders.Count} leader(s) meeting the sweep's own floors, {trailing} trailing the provisional base in a recent year.</p>"));
        page.Append(FormattableString.Invariant($"<p class=\"floors\">{(proposed ? "The proposal meets the floors after costs" : proposal is null ? "The search proposes nothing" : "The search's proposal falls short of the floors after costs")}: at least {FamilySweep.TradeFloor} trades and an edge above nothing in at least {FamilySweep.YearsBeating} of the 8 years.</p>"));
        page.Append("<div class=\"table\"><table><thead><tr><th>Read</th><th>Setting</th><th>Trades</th><th>Edge after costs</th><th>Years above nothing</th><th>Before costs</th><th>At double the cost</th><th>Without the five largest</th></tr></thead><tbody>");

        for (var at = 0; at < shown.Count; at++)
        {
            var trimmed = SweepStages.WithoutTheLargest(SweepStages.Picks(candidates, design), design, space.Setting(shown[at].Point), space.Conditions(shown[at].Point));

            page.Append(FormattableString.Invariant($"<tr><td>{WebUtility.HtmlEncode(shown[at].Label)}</td><td>{WebUtility.HtmlEncode(space.Describe(shown[at].Point))}</td><td class=\"num\">{after[at].Scored:N0}</td><td class=\"num\">{FamilySweepReport.Number(after[at].Edge)}</td><td class=\"num\">{YearsAbove(after[at])} of 8</td><td class=\"num\">{FamilySweepReport.Number(before[at].Edge)}</td><td class=\"num\">{FamilySweepReport.Number(doubled[at].Edge)}</td><td class=\"num\">{FamilySweepReport.Number(trimmed.Edge)}</td></tr>"));
        }

        page.Append("</tbody></table></div>");
        page.Append("<h3>Each year after costs</h3><div class=\"table\"><table><thead><tr><th>Read</th>");

        for (var year = 0; year < 8; year++)
        {
            page.Append(FormattableString.Invariant($"<th>{SweepColumns.FirstScored.Year + year}</th>"));
        }

        page.Append("</tr></thead><tbody>");

        for (var at = 0; at < shown.Count; at++)
        {
            page.Append($"<tr><td>{WebUtility.HtmlEncode(shown[at].Label)}</td>");

            for (var year = 0; year < 8; year++)
            {
                page.Append(FormattableString.Invariant($"<td class=\"num\">{FamilySweepReport.Number(year < after[at].YearEdge.Length ? after[at].YearEdge[year] : null)} ({(year < after[at].YearScored.Length ? after[at].YearScored[year] : 0):N0})</td>"));
            }

            page.Append("</tr>");
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

        if (!proposed)
        {
            var rows = shown.Skip(proposal is null ? 1 : 2).Select((one, at) => new Strongest(one.Label + ": " + space.Describe(one.Point), after[at + (proposal is null ? 1 : 2)].Scored, YearsAbove(after[at + (proposal is null ? 1 : 2)]), after[at + (proposal is null ? 1 : 2)].Edge));
            var five = SweepNonePassed.StrongestOf(rows);
            var next = new List<string>(SweepNonePassed.FloorsMissed(five));

            if (strongest.Count > 0)
            {
                next.AddRange(SweepNonePassed.GridEnds([.. space.Dials.Select(dial => (dial.Name, dial.Labels))], strongest[0]));
            }

            next.Add(SweepNonePassed.Ideas(["its exits, its market switches and its count a night, which the ideas' run reads on the pullback's base"]));
            page.Append(SweepNonePassed.Section(five, next, string.Empty, FamilySweepReport.Number));
        }

        page.Append("</main></body></html>");

        var report = Path.Combine(folder, SweepFolder.ReportFile);

        File.WriteAllText(report, page.ToString());
        File.WriteAllText(Path.Combine(folder, FiguresFile), JsonSerializer.Serialize(
            new
            {
                index = indexCode,
                family = PullbackSearch,
                note,
                candidates = all.Length,
                kept = candidates.Length,
                sampleSize,
                gridSize,
                evaluations = search.Evaluations,
                bestEdge = search.BestEdge,
                line = search.Line,
                leaders = leaders.Count,
                proposed,
                started,
                read = shown.Select((one, at) => new { one.Label, setting = space.Describe(one.Point), after = after[at], before = before[at], doubled = doubled[at] }),
            },
            SweepRunner.Json));

        output.WriteLine(proposed
            ? FormattableString.Invariant($"proposed {space.Describe(proposal!.Point)}, edge after costs {FamilySweepReport.Number(after[1].Edge)} over {after[1].Scored:N0} trades, {YearsAbove(after[1])} of 8 years above nothing, before costs {FamilySweepReport.Number(before[1].Edge)}, at double {FamilySweepReport.Number(doubled[1].Edge)}")
            : FormattableString.Invariant($"none passed: the provisional base reads {FamilySweepReport.Number(after[0].Edge)} after costs over {after[0].Scored:N0} trades; the report states the strongest settings and what could be tried next"));
        output.WriteLine("report " + report);

        return 0;
    }

    // How many of the search's strongest settings a report reads in full.
    const int StrongestRead = 5;

    // The sector heavyweights' design (a) on the index alone: each sector's return its members' mean, a leader's beta
    // read against the index's fund, the members each rebalance reads those clearing the floors and the gate on its
    // session, and each holding's result in per cent after its round trip, its size cut's return paying none.
    // see: A trade's cost comes off the trade and not its benchmark
    async Task<int> HeavyweightsAsync(string indexCode, string named, string words, SweepHistory history, DateOnly through, SweepHistoryInputs inputs, HeavyweightHistory companies, IReadOnlyDictionary<string, IReadOnlyList<FiledIncome>> income, string folder, CancellationToken cancellation)
    {
        var fund = (await history.SeriesOfAsync([IndexFunds[indexCode]], through, cancellation)).FirstOrDefault();
        var calendar = inputs.Sessions;
        var first = Array.FindIndex(calendar, session => session >= SweepColumns.FirstScored);
        var months = HeavyweightSweep.Rebalances(calendar, first, HeavyweightPeriod.Month);
        var weeks = HeavyweightSweep.Rebalances(calendar, first, HeavyweightPeriod.Week);
        var read = months.Concat(weeks).ToHashSet();

        output.WriteLine(FormattableString.Invariant($"laying out {inputs.Names.Count} name(s), {read.Count} session(s) read by a rebalance, beta against {fund?.Series ?? "no fund"}"));

        var (tape, sessions) = HeavyweightSweep.Lay(inputs, companies, fund, read, first);
        var sessionAt = calendar.Select((session, at) => (session, at)).ToDictionary(pair => pair.session, pair => pair.at);
        var series = new SweepSeries[inputs.Names.Count];

        Parallel.For(0, inputs.Names.Count, name => series[name] = SweepColumns.Series(inputs.Names[name], sessionAt));

        int BarOf(int name, int session) => Array.BinarySearch(series[name].SessionAt, session);

        var (members, clearing) = (0L, 0L);
        var filtered = new Dictionary<(IndexQuality Quality, decimal Floors), Dictionary<int, HeavyweightSession>>();

        string? SectorOf(int name) => companies.Companies.GetValueOrDefault(series[name].Name.Ticker).Sector;

        foreach (var variant in HeavyweightVariants)
        {
            var bySession = new Dictionary<int, HeavyweightSession>();

            foreach (var (session, one) in sessions)
            {
                var kept = one.Members.Where(candidate => BarOf(candidate.Name, session) is var bar && bar >= 0 && Clears(indexCode, series[candidate.Name], bar, income.GetValueOrDefault(series[candidate.Name].Name.Ticker) ?? [], variant.Quality, variant.Floors, SectorOf(candidate.Name))).ToArray();

                if (variant == (IndexQuality.Profit, 1m))
                {
                    members += one.Members.Count;
                    clearing += kept.Length;
                }

                bySession[session] = one with { Members = kept };
            }

            filtered[variant] = bySession;
        }

        HeavyweightTrade Costed(HeavyweightTrade trade, int multiple)
        {
            if (trade.Result is not { } result || trade.End is not { } end || BarOf(trade.Name, trade.Entry) is var buy && buy < 0 || BarOf(trade.Name, end) is var sale && sale < 0)
            {
                return trade;
            }

            var bars = series[trade.Name].Bars;
            var ticker = series[trade.Name].Name.Ticker;
            var bought = bars[buy];
            var sold = bars[sale];
            var entry = bought.RawClose > 0m ? bought.RawClose : bought.Close;
            var exit = sold.RawClose > 0m ? sold.RawClose : sold.Close;
            var value = CompanyValue.On(new SessionClose(bought.Session, bought.Close, entry), companies.Counts.GetValueOrDefault(ticker) ?? [], companies.Splits.GetValueOrDefault(ticker) ?? []);

            return trade with { Result = result - (TradeCost.InPercent(value, entry, exit, multiple) / 100.0) };
        }

        var names = tape.Tickers.Select((ticker, at) => (ticker, at)).ToDictionary(pair => pair.ticker, pair => pair.at, StringComparer.Ordinal);
        // Design (a)'s grid rebalances monthly, each setting walked at each quality and each floor.
        var settings = HeavyweightSweep.Settings.Where(setting => setting.Sector == HeavyweightSectorReturn.Members && setting.Period == HeavyweightPeriod.Month).ToArray();
        var every = new System.Collections.Concurrent.ConcurrentDictionary<(int From, int To), double?>();
        var figures = new System.Collections.Concurrent.ConcurrentDictionary<string, (HeavyweightFigures Before, HeavyweightFigures After, HeavyweightFigures Doubled)>(StringComparer.Ordinal);

        double? EveryOnce(int from, int to) => every.GetOrAdd((from, to), span => HeavyweightSweep.EveryMember(tape, span.From, span.To));

        output.WriteLine(FormattableString.Invariant($"walking {settings.Length} setting(s) at each of {HeavyweightVariants.Count} qualities and floors, {settings.Length * HeavyweightVariants.Count} in all, {clearing:N0} of {members:N0} member-sessions clearing the provisional floors and the gate"));

        Parallel.ForEach(
            from variant in HeavyweightVariants
            from grouped in settings.GroupBy(setting => (setting.Largest, setting.LookBack, setting.Leaders, setting.HighBeta))
            select (Variant: variant, Group: grouped),
            pair =>
            {
                var prefix = VariantKey(pair.Variant.Quality, pair.Variant.Floors);
                var rebalances = months.ToDictionary(session => session, session => HeavyweightSweep.Read(filtered[pair.Variant][session], pair.Group.First(), names));

                foreach (var setting in pair.Group)
                {
                    var trades = HeavyweightSweep.Walk(tape, rebalances, setting.Exit, EveryOnce);
                    var key = prefix + setting.Key;

                    figures[key] = (
                        HeavyweightSweep.Figures(key, trades),
                        HeavyweightSweep.Figures(key, [.. trades.Select(trade => Costed(trade, 1))]),
                        HeavyweightSweep.Figures(key, [.. trades.Select(trade => Costed(trade, TradeCost.Doubled))]));
                }
            });

        // Each variant's own proposal, its neighbours read within it, and the stronger of them after costs kept.
        var after = HeavyweightVariants
            .SelectMany(variant => settings.Select(setting => (setting, figures[VariantKey(variant.Quality, variant.Floors) + setting.Key].After)))
            .ToArray();
        var proposals = HeavyweightVariants
            .Select(variant => HeavyweightSweep.Propose([.. settings.Select(setting => (setting, figures[VariantKey(variant.Quality, variant.Floors) + setting.Key].After))]))
            .ToArray();
        var proposal = proposals.Where(one => one.Proposed is not null).OrderByDescending(one => one.Proposed!.Edge).FirstOrDefault() ?? proposals[0];
        var provisional = VariantKey(IndexQuality.Profit, 1m) + (HeavyweightSweep.Frozen with { Sector = HeavyweightSectorReturn.Members }).Key;
        var note = FormattableString.Invariant($"{Membership(named, inputs)} Each sector's return is its members' mean and a leader's beta is read against {fund?.Series ?? "no fund"}. Every setting rebalances monthly and is read at three qualities, none, the profit gate, and the profit gate with the interest cover, the state the fourth level adds not read since the history holds no quarters as they stood for it, and at the index's dollar volume floor once and twice, a member read by a rebalance only where its close was at least $5: at the provisional floors and gate {clearing:N0} of {members:N0} member-sessions clear. Edges are in points of the buy, after each holding's round trip at the published table, its size cut paying none.");

        static string Points(double? value) => value is { } one ? FormattableString.Invariant($"{one * 100:+0.00;-0.00}") : "none";

        var page = new StringBuilder("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"><title>" + WebUtility.HtmlEncode(named) + " heavyweights</title><style>" + SweepReport.Style + "</style></head><body><main>");

        page.Append(FormattableString.Invariant($"<h1>The {WebUtility.HtmlEncode(named)} {WebUtility.HtmlEncode(words)} design (a)</h1><p class=\"survivors\">{WebUtility.HtmlEncode(note)}</p>"));
        page.Append(proposal.Proposed is { } proposed
            ? $"<p>Proposed: {WebUtility.HtmlEncode(proposed.Key)}.</p>"
            : "<p>No setting meets the floors after costs.</p>");
        page.Append(FormattableString.Invariant($"<p>{after.Count(one => one.After.MeetsFloors)} of {after.Length} settings meet the floors, where luck alone passes about {HeavyweightSweep.Luck(after.Length):0} with no effect at all. Each quality and floor's own proposal: {string.Join("; ", HeavyweightVariants.Select((variant, at) => VariantKey(variant.Quality, variant.Floors).TrimEnd('|') + " " + (proposals[at].Proposed is { } own ? Points(own.Edge) + " points" : "none")))}.</p>"));
        page.Append("<div class=\"table\"><table><thead><tr><th>Setting</th><th>Holdings</th><th>Edge before costs, points</th><th>After</th><th>At double</th><th>Years above nothing after</th><th>Without the five largest</th><th>Error</th></tr></thead><tbody>");

        foreach (var key in new[] { provisional }
            .Concat(proposals.Where(one => one.Proposed is not null).Select(one => one.Proposed!.Key))
            .Concat(after.OrderByDescending(one => one.After.Edge ?? double.MinValue).Take(10).Select(one => one.After.Key))
            .Distinct(StringComparer.Ordinal))
        {
            var (shownBefore, shownAfter, shownDoubled) = figures[key];

            page.Append(FormattableString.Invariant($"<tr><td>{WebUtility.HtmlEncode(key)}</td><td class=\"num\">{shownAfter.Trades:N0}</td><td class=\"num\">{Points(shownBefore.Edge)}</td><td class=\"num\">{Points(shownAfter.Edge)}</td><td class=\"num\">{Points(shownDoubled.Edge)}</td><td class=\"num\">{shownAfter.YearsBeating} of 8</td><td class=\"num\">{Points(shownAfter.EdgeWithoutLargest)}</td><td class=\"num\">{Points(shownAfter.StandardError)}</td></tr>"));
        }

        page.Append("</tbody></table></div></main></body></html>");

        var report = Path.Combine(folder, SweepFolder.ReportFile);

        File.WriteAllText(report, page.ToString());
        File.WriteAllText(Path.Combine(folder, FiguresFile), JsonSerializer.Serialize(new { index = indexCode, family = Heavyweights, note, provisional, proposal = proposal.Proposed?.Key, settings = after.Select(one => new { one.After.Key, figures[one.After.Key].Before, figures[one.After.Key].After, figures[one.After.Key].Doubled }) }, SweepRunner.Json));

        var (firstBefore, firstAfter, firstDoubled) = figures[provisional];

        output.WriteLine(FormattableString.Invariant($"provisional {provisional}: edge after costs {Points(firstAfter.Edge)} points over {firstAfter.Trades} holdings, before {Points(firstBefore.Edge)}, at double {Points(firstDoubled.Edge)}, {firstAfter.YearsBeating} of 8 years"));
        output.WriteLine(proposal.Proposed is { } shown ? "proposed " + shown.Key : "none passed: no setting meets the floors after costs");
        output.WriteLine("report " + report);

        return 0;
    }

    // The words every figure of a run carries for the membership it read: today's members on every session, survivors
    // only, or the members of each session as the fund's quarter-end holdings filed them.
    // see: Membership as it stood is rebuilt from the funds' quarterly holdings filed with the SEC, matched by ISIN and then by name
    public static string Membership(string named, SweepHistoryInputs inputs) =>
        inputs.Survivors > 0
            ? FormattableString.Invariant($"Survivors only: the {named}'s {inputs.Names.Count:N0} members today, each read as a member on every session, which flatters the index, its strength, market check and benchmark read among them alone.")
            : FormattableString.Invariant($"As it stood: the {inputs.Names.Count:N0} names the {named}'s fund held at a quarter end it filed with the SEC from 2018-12-31, its N-Q, its annual report of 2019-03-31 and its N-PORT filings from 2019-09-30, each a member from the first snapshot holding it to the last and read among the members of each session, a name the N-Q holds read from the history's start, a name held on 2019-03-31 and 2019-09-30 read as a member between them, and a holding matched to no code left out.");

    // Whether a listing clears the index's floors and the quality on its session: the close as it traded, the mean dollar
    // volume over the 50 bars to it on the adjusted close and the provider's split-adjusted volume against the index's
    // floor at a multiple of it, and the quarters filed before it, the profit gate at the provisional quality.
    public static bool Clears(string indexCode, SweepSeries series, int bar, IReadOnlyList<FiledIncome> income, IndexQuality quality = IndexQuality.Profit, decimal floors = 1m, string? sector = null)
    {
        var bars = series.Bars;
        var held = bars[bar];
        var traded = held.RawClose > 0m ? held.RawClose : held.Close;
        var window = bars[Math.Max(0, bar - MemberReadings.DollarVolumeSessions + 1)..(bar + 1)].Select(one => (one.Close, one.Volume)).ToArray();

        return MemberReadings.ClearsTheFloors(indexCode, traded, MemberReadings.DollarVolume(window) / floors)
            && quality switch
            {
                IndexQuality.Off => true,
                IndexQuality.Profit => MemberReadings.Profit(income, held.Session),
                _ => MemberReadings.Profit(income, held.Session) && MemberReadings.Coverage(income, held.Session, sector),
            };
    }

    // The quality levels and the multiples of the index's dollar volume floor the heavyweights' design (a) is swept over.
    public static IReadOnlyList<(IndexQuality Quality, decimal Floors)> HeavyweightVariants { get; } =
    [
        .. from quality in new[] { IndexQuality.Off, IndexQuality.Profit, IndexQuality.Cover }
           from floors in new[] { 1m, 2m }
           select (quality, floors),
    ];

    public static string VariantKey(IndexQuality quality, decimal floors) =>
        "quality=" + quality switch { IndexQuality.Off => "off", IndexQuality.Profit => "profit", _ => "profit and cover" }
        + FormattableString.Invariant($"|floors={floors:0}x|");

    // A kept trade's round trip in multiples of its risk at a multiple of the table, its company valued on the listing's
    // session and its prices read as they traded for their bands, the sale at the price its result puts it.
    public static double CostInRisk(SweepSeries series, FamilyListing listing, double result, HeavyweightHistory companies, int multiple) =>
        CostInRisk(series, listing.Bar, listing.Entry, listing.Stop, result, companies, multiple);

    public static double CostInRisk(SweepSeries series, int bar, double entry, double stop, double result, HeavyweightHistory companies, int multiple)
    {
        var held = series.Bars[bar];
        var ticker = series.Name.Ticker;
        var traded = held.RawClose > 0m ? held.RawClose : held.Close;
        var value = CompanyValue.On(new SessionClose(held.Session, held.Close, traded), companies.Counts.GetValueOrDefault(ticker) ?? [], companies.Splits.GetValueOrDefault(ticker) ?? []);
        var factor = held.Close > 0m ? Statistic.FromRatio(traded / held.Close) : 1.0;
        var sale = entry + (result * (entry - stop));

        return TradeCost.InRisk(
            value,
            Statistic.ToPrice(entry * factor),
            Statistic.ToPrice(stop * factor),
            Statistic.ToPrice(Math.Max(sale * factor, 0.01)),
            multiple);
    }

    // The provisional setting's and the proposal's or the strongest settings' edges before costs, at the table's cost and
    // at double.
    static string Costs(IReadOnlyList<string> keys, IReadOnlyDictionary<string, FamilyFigures> before, IReadOnlyDictionary<string, FamilyFigures> after, IReadOnlyDictionary<string, FamilyFigures> doubled)
    {
        var html = new StringBuilder("<h3>The cost of a trade</h3><p>Each setting's edge before costs, after each trade's cost at the published table's value, and at double the cost, the first row the provisional setting.</p><div class=\"table\"><table class=\"costs\"><thead><tr><th>Setting</th><th>Trades</th><th>Edge before costs</th><th>After costs</th><th>At double the cost</th><th>Years above nothing after costs</th></tr></thead><tbody>");

        foreach (var key in keys)
        {
            html.Append(FormattableString.Invariant($"<tr data-key=\"{WebUtility.HtmlEncode(key)}\"><td>{WebUtility.HtmlEncode(key)}</td><td class=\"num\">{after[key].Trades:N0}</td><td class=\"num\">{FamilySweepReport.Number(before[key].Edge)}</td><td class=\"num\">{FamilySweepReport.Number(after[key].Edge)}</td><td class=\"num\">{FamilySweepReport.Number(doubled[key].Edge)}</td><td class=\"num\">{after[key].YearsBeating} of 8</td></tr>"));
        }

        return html.Append("</tbody></table></div>").ToString();
    }
}
