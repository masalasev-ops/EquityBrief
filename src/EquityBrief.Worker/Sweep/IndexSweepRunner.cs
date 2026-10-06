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
        [Followers] = "followers of the S&P 500's industry leaders, the sector heavyweights'",
    };

    public const string Pullback = "pullback";

    public const string PullbackSearch = "pullback-search";

    // The longest the pullback's search samples its grid for, the pullback sweep's own budget a design.
    public static readonly TimeSpan SearchBudget = SweepSearch.SampleBudget;

    public const string Heavyweights = "heavyweights";

    public const string Followers = "heavyweights-b";

    // The heavyweights' two designs as an answer names them, (a) the index's own leaders and (b) the S&P 500's followers.
    public const string DesignA = "a";

    public const string DesignB = "b";

    // Design (b)'s dials: the sessions an industry's lead is read over, how many leading industries are read, and how many
    // of the index's members in each are bought.
    public static IReadOnlyList<int> LeadWindows { get; } = [21, 63];

    public static IReadOnlyList<int> IndustriesKept { get; } = [5, 10, EveryLeading];

    public static IReadOnlyList<int> MembersPerIndustry { get; } = [1, 2];

    public const int EveryLeading = int.MaxValue;

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
        var folder = Claim(SweepFolder.Resolve(configuredFolder, dataRoot), started);

        output.WriteLine("run " + Path.GetFileName(folder));

        if (family == Pullback)
        {
            return await PullbackAsync(indexCode, named, words, history, through, inputs, companies, income, folder, cancellation);
        }

        if (family == PullbackSearch)
        {
            return await PullbackSearchAsync(indexCode, named, words, history, through, inputs, companies, income, folder, started, cancellation);
        }

        if (family == Heavyweights)
        {
            return await HeavyweightsAsync(indexCode, named, words, history, through, inputs, companies, income, folder, cancellation);
        }

        if (family == Followers)
        {
            return await FollowersAsync(indexCode, named, words, history, through, inputs, companies, income, folder, cancellation);
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

        // The second stage, every level read after costs: each new dial's levels and each market switch alone on the ten
        // strongest settings of the grid, the highest edges among those holding the trade floor and the rest after by
        // their trades, and then the levels that survive crossed.
        var industries = await history.IndustriesAsync(cancellation);
        var market = await history.SeriesOfAsync(["SPY", IndexFunds[indexCode], "HYG"], through, cancellation);

        output.WriteLine("reading the S&P 500 as it stood for its industries, its breadth and its reports");

        var large = await history.ReadAsync(through, null, cancellation);
        var industryReturns = new SweepIndustries(large, companies, industries);
        var largeBreadth = SweepSwitches.LargeBreadth(large, calendar);
        var switches = new SweepSwitches(calendar, market, IndexFunds[indexCode]);
        var breadthInstead = sessions.Select((one, at) => one with { Breadth = largeBreadth[at] }).ToArray();
        var largeAdapter = FamilySweepRunner.For(family, series, breadthInstead, members, firstScored, calendar, floored ? stopFloor : 0);
        var isBreakout = family == BreakoutRule.Name;
        var isDrift = family == DriftRule.Name;
        var wide = isDrift ? new DriftSweep(series, members) : null;
        var wideReadings = wide?.Readings(sessions, firstScored, DriftWideWindow) ?? [];
        var wideReadingsLarge = wide?.Readings(breadthInstead, firstScored, DriftWideWindow) ?? [];
        var backOf = wideReadings.Concat(wideReadingsLarge).GroupBy(one => (one.Name, one.Bar)).ToDictionary(group => group.Key, group => group.First().Back);
        var reports = large.Names
            .Where(name => industries.ContainsKey(name.Ticker))
            .SelectMany(name => name.Surprises.Select(surprise => (Name: name, Surprise: surprise)))
            .GroupBy(pair => industries[pair.Name.Ticker], StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);

        decimal Traded(FamilyListing listing)
        {
            var bar = series[listing.Name].Bars[listing.Bar];

            return bar.RawClose > 0m ? bar.RawClose : bar.Close;
        }

        // The year's high a breakout is read against: the highest high of the 251 sessions before the listing, and the
        // sessions since the session it was made on.
        (double High, int Since) YearHigh(FamilyListing listing) =>
            MemberReadings.YearHigh(series[listing.Name].Bars, listing.Bar) is { } found
                ? (Statistic.FromPrice(found.High), found.Since)
                : (double.NaN, int.MaxValue);

        // The drift's peer reading: the mean surprise of the S&P 500 members of the listing's industry that reported in
        // the 20 sessions before its reaction session, each weighted by its company's value on the session before the
        // reaction, above nothing; where none reported the reading is none, which is not above nothing.
        bool PeersBeat(FamilyListing listing)
        {
            if (!backOf.TryGetValue((listing.Name, listing.Bar), out var back)
                || listing.Session - back - PeerSessions < 0
                || !industries.TryGetValue(tickers[listing.Name], out var industry)
                || !reports.TryGetValue(industry, out var reported))
            {
                return false;
            }

            var reaction = listing.Session - back;
            var (from, to) = (calendar[reaction - PeerSessions], calendar[reaction - 1]);
            var peers = new List<(double Value, double Figure)>();

            foreach (var (name, surprise) in reported)
            {
                if (surprise.EventDate < from || surprise.EventDate > to || !name.MemberOn(surprise.EventDate)
                    || SweepIndustries.BarOn(name, to) is not { } bar
                    || CompanyValue.On(new SessionClose(bar.Session, bar.Close, bar.RawClose > 0m ? bar.RawClose : bar.Close), companies.Counts.GetValueOrDefault(name.Ticker) ?? [], companies.Splits.GetValueOrDefault(name.Ticker) ?? []) is not { } value
                    || value <= 0m)
                {
                    continue;
                }

                peers.Add((Statistic.FromPrice(value), surprise.Percent));
            }

            // The peers' mean surprise through the one function the night reads it with.
            return MemberReadings.ValueWeighted(peers) is > 0;
        }

        var levels = new List<DialLevel<FamilyListing>>
        {
            new("dollar volume floor", "half the provisional", Floors: DollarVolumeHalf),
            new("dollar volume floor", "twice the provisional", Floors: 2m),
            new("minimum price", "$10", Keep: listing => Traded(listing) >= HigherPrice),
            new("quality", "off", Quality: IndexQuality.Off),
            new("quality", "profit and cover", Quality: IndexQuality.Cover),
            new("hold", "21 sessions", Hold: 21),
            new("hold", "42 sessions", Hold: 42),
        };

        if (isDrift)
        {
            levels.Add(new("hold", "63 sessions", Hold: 63));
        }

        levels.Add(new("industry fall", "21 sessions", Keep: listing => !industryReturns.Fell(tickers[listing.Name], calendar, listing.Session, 21)));
        levels.Add(new("industry fall", "63 sessions", Keep: listing => !industryReturns.Fell(tickers[listing.Name], calendar, listing.Session, 63)));

        if (isBreakout)
        {
            levels.Add(new("nearness to the year's high", "at least 0.95", Keep: listing => listing.Entry / YearHigh(listing).High >= NearHigh));
            levels.Add(new("nearness to the year's high", "at it or above", Keep: listing => listing.Entry / YearHigh(listing).High >= 1));
            levels.Add(new("recency of the year's high", "within 63 sessions", Keep: listing => YearHigh(listing).Since <= RecentHighSessions));
            levels.Add(new("recency of the year's high", "older than 63 sessions", Keep: listing => YearHigh(listing).Since > RecentHighSessions));
            levels.Add(new("volume multiple", "3.0", Keep: listing => listing.Order >= HighestVolume));
        }

        if (isDrift)
        {
            levels.Add(new("window", $"{DriftWideWindow} sessions", Window: DriftWideWindow));
            levels.Add(new("peer version", "on", Keep: PeersBeat));
        }

        levels.AddRange(Switches<FamilyListing>(indexCode, switches, listing => listing.Session));

        // The trades a setting makes under a set of levels together, after costs: the provisional floors, gate, hold and
        // window where none of them sets its own, and the S&P 500's breadth closing the market check where one asks it.
        IReadOnlyList<FamilyTrade> TradesUnder(int[] setting, IReadOnlyList<DialLevel<FamilyListing>> together)
        {
            var quality = together.Select(level => level.Quality).OfType<IndexQuality>().DefaultIfEmpty(IndexQuality.Profit).First();
            var floors = together.Select(level => level.Floors).OfType<decimal>().DefaultIfEmpty(1m).First();
            var hold = together.Select(level => level.Hold).OfType<int>().DefaultIfEmpty(0).First();
            var window = together.Select(level => level.Window).OfType<int>().DefaultIfEmpty(0).First();
            var instead = together.Any(level => level.LargeBreadth);
            var listings = window > 0 && isDrift
                ? DriftSweep.Listings(instead ? wideReadingsLarge : wideReadings, setting, floored ? stopFloor : 0, window)
                : (instead ? largeAdapter : adapter).Listings(setting);
            var kept = listings
                .Where(listing => Clears(indexCode, series[listing.Name], listing.Bar, income.GetValueOrDefault(tickers[listing.Name]) ?? [], quality, floors, companies.Companies.GetValueOrDefault(tickers[listing.Name]).Sector)
                    && together.All(level => level.Keep is not { } keep || keep(listing)))
                .Select(listing => hold > 0 ? listing with { Cap = hold } : listing)
                .ToArray();

            return [.. FamilySweep.Walk(kept, tickers, YearOf, adapter.Exit, adapter.Benchmark).Select(trade => After(trade, 1))];
        }

        var ten = read
            .OrderBy(one => one.Figures.Trades >= SweepDials.TradesHeld ? 0 : 1)
            .ThenByDescending(one => one.Figures.Trades >= SweepDials.TradesHeld ? one.Figures.Edge ?? double.MinValue : one.Figures.Trades)
            .ThenBy(one => one.Figures.Key, StringComparer.Ordinal)
            .Take(SweepDials.Settings)
            .Select(one => one.Setting)
            .ToArray();

        output.WriteLine(FormattableString.Invariant($"the second stage: {levels.Count} level(s) and switch(es) alone on the {ten.Length} strongest settings"));

        var withoutDials = ten.Select(setting => SweepDials.Of(TradesUnder(setting, []))).ToArray();
        var tries = levels
            .SelectMany(level => ten.Select((setting, at) => new DialTry(level.Dial, level.Name, level.Switch, at, withoutDials[at], SweepDials.Of(TradesUnder(setting, [level])))))
            .ToList();
        var dialsRead = SweepDials.Read(tries);
        var crossedLevels = dialsRead
            .Where(one => one.KeptOn >= SweepDials.KeptOn)
            .GroupBy(one => one.Dial)
            .Select(dial => dial.OrderByDescending(one => one.KeptOn).ThenByDescending(one => one.MedianChange ?? double.MinValue).First())
            .Select(best => levels.First(level => level.Dial == best.Dial && level.Name == best.Level))
            .ToArray();
        var crossed = crossedLevels.Length > 0 ? ten.Select(setting => SweepDials.Of(TradesUnder(setting, crossedLevels))).ToArray() : [];
        var stage = StageSection(
            dialsRead,
            [.. ten.Select(setting => adapter.Grid.Key(setting))],
            [.. crossedLevels.Select(level => level.Dial + ", " + level.Name)],
            crossed,
            isDrift
                ? "The holds are read at 21, 42 and 63 sessions beside the drift's own 60, its window at 20 sessions beside the grid's, and its peer reading over the 20 sessions before the reaction; the quality's fourth level, the state the reported quarters read, is not read, since the history holds no quarters as they stood for it."
                : "The holds are read at 21 and 42 sessions beside the family's own 63, and the year's high over the 251 sessions before the listing; the quality's fourth level, the state the reported quarters read, is not read, since the history holds no quarters as they stood for it.");
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

        var report = WriteRun(
            folder,
            FamilySweepReport.Build(run, adapter.Grid, read, proposal) + Costs([.. shown.Distinct(StringComparer.Ordinal)], before, read.ToDictionary(one => one.Figures.Key, one => one.Figures, StringComparer.Ordinal), doubled) + stage,
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
                strongestTen = ten.Select((setting, at) => new { setting = adapter.Grid.Key(setting), after = withoutDials[at] }),
                dials = dialsRead,
                tries = tries.Select(one => new { one.Dial, one.Level, one.Switch, one.Setting, one.Kept, with = one.With }),
                crossedLevels = crossedLevels.Select(level => level.Dial + ", " + level.Name),
                crossed = crossed.Select((measures, at) => new { setting = adapter.Grid.Key(ten[at]), after = measures }),
            },
            new SweepAnswer(indexCode, family, null, proposal.Proposed is not null || crossed.Any(MeetsTheFloors)));

        output.WriteLine(proposal.Proposed is { } shownProposal
            ? FormattableString.Invariant($"proposed {shownProposal.Key}, edge after costs {FamilySweepReport.Number(shownProposal.Edge)} over {shownProposal.Trades} trades, before costs {FamilySweepReport.Number(before[shownProposal.Key].Edge)}, at double {FamilySweepReport.Number(doubled[shownProposal.Key].Edge)}")
            : "none passed: no setting meets the floors after costs, and the report states the strongest settings and what could be tried next");
        output.WriteLine(crossedLevels.Length == 0
            ? FormattableString.Invariant($"second stage: no level of {levels.Count} survives on {SweepDials.KeptOn} of the {ten.Length} strongest settings")
            : FormattableString.Invariant($"second stage: {string.Join("; ", crossedLevels.Select(level => level.Dial + ", " + level.Name))} survive; crossed, the strongest of the ten reads {FamilySweepReport.Number(crossed.Max(one => one.Edge))} after costs over {crossed.OrderByDescending(one => one.Edge ?? double.MinValue).First().Scored} trades"));
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

        var report = WriteRun(folder, page.ToString(), new { index = indexCode, family = Pullback, note, listed, kept, all, before, after, doubled }, null);

        output.WriteLine(FormattableString.Invariant($"the base: edge after costs {SweepIdeasReport.Number(after.Edge)} over {after.Trades} trades, before costs {SweepIdeasReport.Number(before.Edge)}, at double {SweepIdeasReport.Number(doubled.Edge)}, with no floors or gate {SweepIdeasReport.Number(all.Edge)} over {all.Trades}"));
        output.WriteLine("report " + report);

        return 0;
    }

    // The pullback's nine dials on the index alone, searched by the pullback sweep's own second stage for the live design:
    // every member's candidates as the history stood, its strength, market check and benchmark read among the index's
    // own members, a candidate kept only where it clears the floors and the gate on its session, and every trade's
    // result after its cost at the table's value, its benchmark paying none; the provisional base's setting read beside
    // the search's proposal, each before costs and at double the cost as well. A search proposing nothing, or a
    // proposal short of the floors after costs, brings the strongest settings and what could be tried next. The second
    // stage then tries each new dial's levels and each market switch alone on the ten strongest settings and crosses
    // the levels that survive.
    // see: A trade's cost comes off the trade and not its benchmark
    // see: No family on any index is set aside or hidden by a test result without the operator's word
    async Task<int> PullbackSearchAsync(string indexCode, string named, string words, SweepHistory history, DateOnly through, SweepHistoryInputs inputs, HeavyweightHistory companies, IReadOnlyDictionary<string, IReadOnlyList<FiledIncome>> income, string folder, DateTimeOffset started, CancellationToken cancellation)
    {
        var industries = await history.IndustriesAsync(cancellation);
        var market = await history.SeriesOfAsync(["SPY", IndexFunds[indexCode], "HYG"], through, cancellation);

        output.WriteLine("reading the S&P 500 as it stood for its industries and its breadth");

        var large = await history.ReadAsync(through, null, cancellation);

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

        bool ClearsAt(SweepCandidate candidate, IndexQuality quality, decimal floors) =>
            Clears(indexCode, series[candidate.Name], BarOf(candidate), income.GetValueOrDefault(series[candidate.Name].Name.Ticker) ?? [], quality, floors, companies.Companies.GetValueOrDefault(series[candidate.Name].Name.Ticker).Sector);

        // The loosest every level of the second stage reads, no quality and half the dollar volume floor; the provisional
        // floors and gate keep a part of it, which the search reads.
        var all = found.SelectMany(list => list).ToArray();
        var pool = all
            .Where(candidate => BarOf(candidate) >= 0 && ClearsAt(candidate, IndexQuality.Off, DollarVolumeHalf))
            .OrderBy(candidate => candidate.Session)
            .ThenBy(candidate => candidate.Name)
            .ToArray();

        SweepBenchmark.Fill(pool, series, members, parallelism);

        var candidates = pool.Where(candidate => ClearsAt(candidate, IndexQuality.Profit, 1m)).ToArray();

        // Each trade's cost in multiples of its risk, a candidate's plans and exits apiece, taken off its result and put
        // back to read the edge before costs or at double.
        var exits = SweepAxes.Exits;
        var costs = new float[pool.Length][];

        Parallel.For(0, pool.Length, at =>
        {
            var candidate = pool[at];
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
            for (var at = 0; at < pool.Length; at++)
            {
                for (var plan = 0; plan < pool[at].Plans.Length; plan++)
                {
                    if (pool[at].Plans[plan] is not { } outcomes)
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

        // The second stage, every level read after costs: each new dial's levels and each market switch alone on the ten
        // strongest settings, and then the levels that survive crossed.
        var ten = search.Strongest(SweepDials.Settings, SweepDials.TradesHeld);
        var withoutDials = ten.Select(MeasuresOf).ToArray();
        var industryReturns = new SweepIndustries(large, companies, industries);
        var largeBreadth = SweepSwitches.LargeBreadth(large, calendar).Select(share => share ?? double.NaN).ToArray();
        var switches = new SweepSwitches(calendar, market, IndexFunds[indexCode]);

        decimal Traded(SweepCandidate candidate)
        {
            var bar = series[candidate.Name].Bars[BarOf(candidate)];

            return bar.RawClose > 0m ? bar.RawClose : bar.Close;
        }

        IReadOnlyList<DialLevel<SweepCandidate>> levels =
        [
            new("dollar volume floor", "half the provisional", Floors: DollarVolumeHalf),
            new("dollar volume floor", "twice the provisional", Floors: 2m),
            new("minimum price", "$10", Keep: candidate => Traded(candidate) >= HigherPrice),
            new("quality", "off", Quality: IndexQuality.Off),
            new("quality", "profit and cover", Quality: IndexQuality.Cover),
            new("hold", "20 sessions", Hold: 20),
            new("hold", "40 sessions", Hold: 40),
            new("industry fall", "21 sessions", Keep: candidate => !industryReturns.Fell(series[candidate.Name].Name.Ticker, calendar, candidate.Session, 21)),
            new("industry fall", "63 sessions", Keep: candidate => !industryReturns.Fell(series[candidate.Name].Name.Ticker, calendar, candidate.Session, 63)),
            .. Switches<SweepCandidate>(indexCode, switches, candidate => candidate.Session),
        ];

        // The ten settings read under a set of levels together, the provisional floors, gate and hold where none of them
        // sets its own; the S&P 500's breadth stands in each candidate's own for the read alone.
        SweepMeasures[] ReadUnder(IReadOnlyList<DialLevel<SweepCandidate>> together)
        {
            var quality = together.Select(level => level.Quality).OfType<IndexQuality>().DefaultIfEmpty(IndexQuality.Profit).First();
            var floors = together.Select(level => level.Floors).OfType<decimal>().DefaultIfEmpty(1m).First();
            var held = design with { Hold = together.Select(level => level.Hold).OfType<int>().DefaultIfEmpty(design.Hold).First() };
            var kept = pool.Where(candidate => ClearsAt(candidate, quality, floors) && together.All(level => level.Keep is not { } keep || keep(candidate))).ToArray();
            var breadths = together.Any(level => level.LargeBreadth) ? kept.Select(candidate => candidate.Breadth).ToArray() : null;

            if (breadths is not null)
            {
                foreach (var candidate in kept)
                {
                    candidate.Breadth = largeBreadth[candidate.Session];
                }
            }

            try
            {
                var levelPicks = SweepStages.Picks(kept, held);

                return [.. ten.Select(point => SweepStages.Measures(levelPicks, held, space.Setting(point), space.Conditions(point), nights))];
            }
            finally
            {
                if (breadths is not null)
                {
                    for (var at = 0; at < kept.Length; at++)
                    {
                        kept[at].Breadth = breadths[at];
                    }
                }
            }
        }

        output.WriteLine(FormattableString.Invariant($"the second stage: {levels.Count} level(s) and switch(es) alone on the {ten.Count} strongest settings"));

        var tries = levels
            .SelectMany(level => ReadUnder([level]).Select((with, at) => new DialTry(level.Dial, level.Name, level.Switch, at, withoutDials[at], with)))
            .ToList();
        var dialsRead = SweepDials.Read(tries);
        var surviving = dialsRead.Where(one => one.KeptOn >= SweepDials.KeptOn).ToArray();
        var crossedLevels = surviving
            .GroupBy(one => one.Dial)
            .Select(dial => dial.OrderByDescending(one => one.KeptOn).ThenByDescending(one => one.MedianChange ?? double.MinValue).First())
            .Select(best => levels.First(level => level.Dial == best.Dial && level.Name == best.Level))
            .ToArray();
        var crossed = crossedLevels.Length > 0 ? ReadUnder(crossedLevels) : [];
        var proposed = proposal is not null && MeetsTheFloors(after[1]);
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

        page.Append(StageSection(
            dialsRead,
            [.. ten.Select(point => space.Describe(point))],
            [.. crossedLevels.Select(level => level.Dial + ", " + level.Name)],
            crossed,
            "The holds are read at 20, 40 and 63 sessions, the holds the candidates' exits carry, where the plan named 21, 42 and 63, and the quality's fourth level, the state the reported quarters read, is not read, since the history holds no quarters as they stood for it."));

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

        var report = WriteRun(
            folder,
            page.ToString(),
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
                strongestTen = ten.Select((point, at) => new { setting = space.Describe(point), after = withoutDials[at] }),
                dials = dialsRead,
                tries = tries.Select(one => new { one.Dial, one.Level, one.Switch, one.Setting, one.Kept, with = one.With }),
                crossedLevels = crossedLevels.Select(level => level.Dial + ", " + level.Name),
                crossed = crossed.Select((measures, at) => new { setting = space.Describe(ten[at]), after = measures, meets = MeetsTheFloors(measures) }),
            },
            new SweepAnswer(indexCode, SetupFamilies.Pullback, null, proposed || crossed.Any(MeetsTheFloors)));

        output.WriteLine(proposed
            ? FormattableString.Invariant($"proposed {space.Describe(proposal!.Point)}, edge after costs {FamilySweepReport.Number(after[1].Edge)} over {after[1].Scored:N0} trades, {YearsAbove(after[1])} of 8 years above nothing, before costs {FamilySweepReport.Number(before[1].Edge)}, at double {FamilySweepReport.Number(doubled[1].Edge)}")
            : FormattableString.Invariant($"none passed: the provisional base reads {FamilySweepReport.Number(after[0].Edge)} after costs over {after[0].Scored:N0} trades; the report states the strongest settings and what could be tried next"));
        output.WriteLine(crossedLevels.Length == 0
            ? FormattableString.Invariant($"second stage: no level of {levels.Count} survives on {SweepDials.KeptOn} of the {ten.Count} strongest settings")
            : FormattableString.Invariant($"second stage: {string.Join("; ", crossedLevels.Select(level => level.Dial + ", " + level.Name))} survive; crossed, the strongest of the ten reads {FamilySweepReport.Number(crossed.Max(one => one.Edge))} after costs, {crossed.Count(MeetsTheFloors)} of {ten.Count} meeting the floors"));
        output.WriteLine("report " + report);

        return 0;
    }

    // How many of the search's strongest settings a report reads in full.
    const int StrongestRead = 5;

    // The file a run creates in its folder to hold it, created only where none is.
    public const string ClaimFile = "run.claim";

    // A setting's trades after costs meeting the family sweeps' floors: at least 300 scored and an edge above nothing in
    // at least 6 of the 8 years, the one reading of the floors every table, line and answer of a run takes.
    // see: A sweep passes where a setting it read meets the floors, and a card says none passed only where every design's newest answer does
    static int YearsAbove(SweepMeasures measures) => measures.YearEdge.Count(edge => edge is > 0);

    internal static bool MeetsTheFloors(SweepMeasures measures) => measures.Scored >= FamilySweep.TradeFloor && YearsAbove(measures) >= FamilySweep.YearsBeating;

    // A run's report and its figures, written into the run folder it claimed, with the answer a search states beside them,
    // none for the pullback's base, which reads one setting and searches none; the report's path.
    // see: No family on any index is set aside or hidden by a test result without the operator's word
    static string WriteRun(string folder, string page, object figures, SweepAnswer? answer)
    {
        var report = Path.Combine(folder, SweepFolder.ReportFile);

        File.WriteAllText(report, page);
        File.WriteAllText(Path.Combine(folder, FiguresFile), JsonSerializer.Serialize(figures, SweepRunner.Json));

        if (answer is not null)
        {
            File.WriteAllText(Path.Combine(folder, SweepAnswer.File), answer.Json());
        }

        return report;
    }

    // The second stage's levels: half the provisional dollar volume floor, the higher minimum price, the drift's wider
    // window and the sessions before a reaction its peers' reports are read over.
    public const decimal DollarVolumeHalf = 0.5m;

    public const decimal HigherPrice = 10m;

    public const int DriftWideWindow = 20;

    public const int PeerSessions = MemberReadings.PeerSessions;

    // The breakout's levels: the sessions before a listing its year's high is read over, the nearness to it, the
    // sessions within which it is recent and the highest volume multiple.
    public const int BreakoutYearSessions = MemberReadings.YearSessions;

    public const double NearHigh = 0.95;

    public const int RecentHighSessions = 63;

    public const double HighestVolume = 3.0;

    // The market switches every second stage reads, each closing the list on a session it is closed: the index's fund
    // against SPY up over 126 and over 252 sessions, HYG above its average and up over 63 sessions, and the S&P 500's
    // breadth read in place of the index's own.
    static IEnumerable<DialLevel<T>> Switches<T>(string indexCode, SweepSwitches switches, Func<T, int> sessionOf)
    {
        var fund = IndexFunds[indexCode];

        yield return new($"small against large", $"{fund} over SPY up over 126 sessions", Switch: true, Keep: one => switches.SmallLeads(sessionOf(one), 126));
        yield return new($"small against large", $"{fund} over SPY up over 252 sessions", Switch: true, Keep: one => switches.SmallLeads(sessionOf(one), 252));
        yield return new("credit", "HYG above its 50-session average", Switch: true, Keep: one => switches.CreditAboveItsAverage(sessionOf(one)));
        yield return new("credit", "HYG up over 63 sessions", Switch: true, Keep: one => switches.CreditRising(sessionOf(one)));
        yield return new("the S&P 500's breadth", "in place of the index's own", Switch: true, LargeBreadth: true);
    }

    // The second stage's section of a report: each level alone with the settings that kept it and the median change it
    // made, and the survivors crossed on each of the strongest settings with whether it meets the floors.
    static string StageSection(IReadOnlyList<DialSurvivor> read, IReadOnlyList<string> settings, IReadOnlyList<string> crossedLevels, IReadOnlyList<SweepMeasures> crossed, string reading)
    {
        var page = new StringBuilder("<h3>The second stage: each new dial and switch alone on the ten strongest settings</h3>");

        page.Append(FormattableString.Invariant($"<p>Each level is read on each of the {settings.Count} strongest settings the first stage read, the highest edges after costs among those holding {SweepDials.TradesHeld} trades, twice the floor, so a level keeping half a setting's trades off can still be kept. It is kept on a setting where it is higher in at least {SweepIdeas.YearsBetter} of the 8 years with {SweepIdeas.RecentYearsBetter} of the last {SweepIdeas.RecentYears} and leaves at least {SweepMeasures.TradeFloor} trades: on the edge after costs, or for a market switch on the year's total result with its result a trade higher as well. A level survives where at least {SweepDials.KeptOn} of the {SweepDials.Settings} keep it. {WebUtility.HtmlEncode(reading)}</p>"));
        page.Append("<div class=\"table\"><table><thead><tr><th>Dial</th><th>Level</th><th>Kept on</th><th>Median change</th><th>Survives</th></tr></thead><tbody>");

        foreach (var one in read)
        {
            page.Append(FormattableString.Invariant($"<tr><td>{WebUtility.HtmlEncode(one.Dial)}</td><td>{WebUtility.HtmlEncode(one.Level)}</td><td class=\"num\">{one.KeptOn} of {settings.Count}</td><td class=\"num\">{FamilySweepReport.Number(one.MedianChange)}{(one.Switch ? " a trade" : string.Empty)}</td><td>{(one.KeptOn >= SweepDials.KeptOn ? "yes" : "no")}</td></tr>"));
        }

        page.Append("</tbody></table></div>");

        if (crossedLevels.Count == 0)
        {
            return page.Append("<p>No level survives, so nothing is crossed.</p>").ToString();
        }

        page.Append($"<h3>The survivors crossed</h3><p>{WebUtility.HtmlEncode(string.Join("; ", crossedLevels))}, together on each of the ten settings.</p>");
        page.Append("<div class=\"table\"><table><thead><tr><th>Setting</th><th>Trades</th><th>Edge after costs</th><th>Years above nothing</th><th>Meets the floors</th></tr></thead><tbody>");

        for (var at = 0; at < settings.Count; at++)
        {
            var meets = MeetsTheFloors(crossed[at]);

            page.Append(FormattableString.Invariant($"<tr><td>{WebUtility.HtmlEncode(settings[at])}</td><td class=\"num\">{crossed[at].Scored:N0}</td><td class=\"num\">{FamilySweepReport.Number(crossed[at].Edge)}</td><td class=\"num\">{YearsAbove(crossed[at])} of 8</td><td>{(meets ? "yes" : "no")}</td></tr>"));
        }

        return page.Append("</tbody></table></div>").ToString();
    }

    // A run's folder is named by the second it started, and a run started in a second another run's folder already
    // holds takes the next second free: the index sweeps run side by side, and a second run writing into the first's
    // folder would replace its report. A folder is held by the file created in it only where none is, which two runs
    // cannot both create.
    public static string Claim(string root, DateTimeOffset started)
    {
        for (var stamp = started; ; stamp = stamp.AddSeconds(1))
        {
            var folder = Path.Combine(root, SweepFolder.RunName(stamp));
            var claim = Path.Combine(folder, ClaimFile);

            Directory.CreateDirectory(folder);

            try
            {
                using (new FileStream(claim, FileMode.CreateNew, FileAccess.Write))
                {
                    return folder;
                }
            }
            catch (IOException) when (File.Exists(claim))
            {
            }
        }
    }

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

        var report = WriteRun(folder, page.ToString(), new { index = indexCode, family = Heavyweights, note, provisional, proposal = proposal.Proposed?.Key, settings = after.Select(one => new { one.After.Key, figures[one.After.Key].Before, figures[one.After.Key].After, figures[one.After.Key].Doubled }) }, new SweepAnswer(indexCode, HeavyweightRule.Name, DesignA, proposal.Proposed is not null));

        var (firstBefore, firstAfter, firstDoubled) = figures[provisional];

        output.WriteLine(FormattableString.Invariant($"provisional {provisional}: edge after costs {Points(firstAfter.Edge)} points over {firstAfter.Trades} holdings, before {Points(firstBefore.Edge)}, at double {Points(firstDoubled.Edge)}, {firstAfter.YearsBeating} of 8 years"));
        output.WriteLine(proposal.Proposed is { } shown ? "proposed " + shown.Key : "none passed: no setting meets the floors after costs");
        output.WriteLine("report " + report);

        return 0;
    }

    // The sector heavyweights' design (b) on the index alone, followers of the S&P 500's industry leaders: on the first
    // session of each month, the GICS industries whose S&P 500 members' return, each weighted by its value at the window's
    // start, leads SPY's over the lead window, the strongest first; in each industry kept, the index's members clearing
    // the floors and the quality, by their own return over the window, the strongest bought; each held while its
    // industry still leads and it stays among those bought, sold at a rebalance where it does not and at its last close
    // as a member. Each holding scored in points against the equal-weighted members of its sector in the index after its
    // round trip, the benchmark paying none. The proposal is the strongest edge meeting the family sweeps' floors, with
    // no plateau read across this smaller grid.
    // see: The 400 and 600 each sweep two heavyweight designs and keep the stronger after costs
    async Task<int> FollowersAsync(string indexCode, string named, string words, SweepHistory history, DateOnly through, SweepHistoryInputs inputs, HeavyweightHistory companies, IReadOnlyDictionary<string, IReadOnlyList<FiledIncome>> income, string folder, CancellationToken cancellation)
    {
        var industries = await history.IndustriesAsync(cancellation);
        var spy = (await history.SeriesOfAsync(["SPY"], through, cancellation)).FirstOrDefault();

        output.WriteLine("reading the S&P 500 as it stood for its industries' leads");

        var large = await history.ReadAsync(through, null, cancellation);
        var calendar = inputs.Sessions;
        var first = Array.FindIndex(calendar, session => session >= SweepColumns.FirstScored);
        var months = HeavyweightSweep.Rebalances(calendar, first, HeavyweightPeriod.Month);
        var (tape, _) = HeavyweightSweep.Lay(inputs, companies, null, months.ToHashSet(), first);
        var sessionAt = calendar.Select((session, at) => (session, at)).ToDictionary(pair => pair.session, pair => pair.at);
        var series = new SweepSeries[inputs.Names.Count];

        Parallel.For(0, inputs.Names.Count, name => series[name] = SweepColumns.Series(inputs.Names[name], sessionAt));

        var spyCloses = (spy?.Closes ?? []).ToDictionary(pair => pair.Session, pair => pair.Close);
        var industryReturns = new SweepIndustries(large, companies, industries);

        int BarOf(int name, int session) => Array.BinarySearch(series[name].SessionAt, session);

        string? SectorOf(string ticker) => companies.Companies.GetValueOrDefault(ticker).Sector;

        // Each industry's lead over SPY on a rebalance at a window, read once: the industries leading, the strongest first.
        var leads = new System.Collections.Concurrent.ConcurrentDictionary<(int Session, int Window), IReadOnlyList<(string Industry, double Lead)>>();

        IReadOnlyList<(string Industry, double Lead)> LeadsOn(int session, int window) => leads.GetOrAdd((session, window), key =>
        {
            if (key.Session - key.Window < 0)
            {
                return [];
            }

            var (day, start) = (calendar[key.Session], calendar[key.Session - key.Window]);

            if (!spyCloses.TryGetValue(day, out var spyNow) || !spyCloses.TryGetValue(start, out var spyThen) || spyThen <= 0)
            {
                return [];
            }

            var market = (spyNow / spyThen) - 1.0;

            return [.. industryReturns.Between(start, day).Select(pair => (pair.Key, pair.Value - market)).Where(pair => pair.Item2 > 0).OrderByDescending(pair => pair.Item2).ThenBy(pair => pair.Key, StringComparer.Ordinal)];
        });

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

        var every = new System.Collections.Concurrent.ConcurrentDictionary<(int From, int To), double?>();

        double? EveryOnce(int from, int to) => every.GetOrAdd((from, to), span => HeavyweightSweep.EveryMember(tape, span.From, span.To));

        var settings = (
            from window in LeadWindows
            from kept in IndustriesKept
            from perIndustry in MembersPerIndustry
            from variant in HeavyweightVariants
            select (Window: window, Kept: kept, PerIndustry: perIndustry, variant.Quality, variant.Floors)).ToArray();

        string KeyOf((int Window, int Kept, int PerIndustry, IndexQuality Quality, decimal Floors) setting) =>
            FormattableString.Invariant($"window={setting.Window}|industries={(setting.Kept == EveryLeading ? "every leading" : setting.Kept.ToString(CultureInfo.InvariantCulture))}|members={setting.PerIndustry}|") + VariantKey(setting.Quality, setting.Floors).TrimEnd('|');

        var figures = new System.Collections.Concurrent.ConcurrentDictionary<string, (HeavyweightFigures Before, HeavyweightFigures After, HeavyweightFigures Doubled)>(StringComparer.Ordinal);

        output.WriteLine(FormattableString.Invariant($"walking {settings.Length} setting(s) over {months.Count} monthly rebalances, SPY {(spy is null ? "not held" : "held")}, {industries.Count:N0} companies' industries"));

        Parallel.ForEach(settings, setting =>
        {
            var rebalances = new Dictionary<int, HeavyweightRebalance>();

            foreach (var session in months)
            {
                var kept = LeadsOn(session, setting.Window).Take(setting.Kept).Select(pair => pair.Industry).ToHashSet(StringComparer.Ordinal);
                var bought = new List<int>();

                foreach (var industry in kept)
                {
                    bought.AddRange(Enumerable.Range(0, series.Length)
                        .Where(name => tape.Member[name][session] && industries.GetValueOrDefault(series[name].Name.Ticker) == industry
                            && session - setting.Window >= 0 && tape.Close[name][session - setting.Window] > 0 && !double.IsNaN(tape.Close[name][session])
                            && BarOf(name, session) is var bar && bar >= 0
                            && Clears(indexCode, series[name], bar, income.GetValueOrDefault(series[name].Name.Ticker) ?? [], setting.Quality, setting.Floors, SectorOf(series[name].Name.Ticker)))
                        .OrderByDescending(name => (tape.Close[name][session] / tape.Close[name][session - setting.Window]) - 1.0)
                        .ThenBy(name => series[name].Name.Ticker, StringComparer.Ordinal)
                        .Take(setting.PerIndustry));
                }

                rebalances[session] = new HeavyweightRebalance([
                    .. bought
                        .GroupBy(name => SectorOf(series[name].Name.Ticker) ?? "none", StringComparer.Ordinal)
                        .Select(sector => (
                            sector.Key,
                            sector.ToArray(),
                            Enumerable.Range(0, series.Length).Where(name => tape.Member[name][session] && (SectorOf(series[name].Name.Ticker) ?? "none") == sector.Key).ToArray())),
                ]);
            }

            var trades = HeavyweightSweep.Walk(tape, rebalances, HeavyweightExit.Drop, EveryOnce);
            var key = KeyOf(setting);

            figures[key] = (
                HeavyweightSweep.Figures(key, trades),
                HeavyweightSweep.Figures(key, [.. trades.Select(trade => Costed(trade, 1))]),
                HeavyweightSweep.Figures(key, [.. trades.Select(trade => Costed(trade, TradeCost.Doubled))]));
        });

        var after = settings.Select(setting => figures[KeyOf(setting)].After).ToArray();
        var proposed = after.Where(one => one.MeetsFloors && one.Edge is not null).OrderByDescending(one => one.Edge).ThenBy(one => one.Key, StringComparer.Ordinal).FirstOrDefault();
        var note = FormattableString.Invariant($"{Membership(named, inputs)} An industry's lead is its S&P 500 members' return over the window, each weighted by its value at the window's start, the S&P 500 read as it stood, less SPY's; {industries.Count:N0} companies carry a GICS industry. Every setting rebalances monthly and is read at three qualities and the index's dollar volume floor once and twice, a member read only where its close was at least $5. Edges are in points of the buy against the equal-weighted members of the holding's sector in the index, after each holding's round trip at the published table, the benchmark paying none.");

        static string Points(double? value) => value is { } one ? FormattableString.Invariant($"{one * 100:+0.00;-0.00}") : "none";

        var page = new StringBuilder("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"><title>" + WebUtility.HtmlEncode(named) + " heavyweights design (b)</title><style>" + SweepReport.Style + "</style></head><body><main>");

        page.Append(FormattableString.Invariant($"<h1>The {WebUtility.HtmlEncode(named)} {WebUtility.HtmlEncode(words)} design (b)</h1><p class=\"survivors\">{WebUtility.HtmlEncode(note)}</p>"));
        page.Append("<p>Hou (2007) finds big firms lead small ones within an industry mainly in taking in bad news; this design buys on the leaders' good news, the weaker side of that finding.</p>");
        page.Append(proposed is not null ? $"<p>Proposed: {WebUtility.HtmlEncode(proposed.Key)}.</p>" : "<p>No setting meets the floors after costs.</p>");
        page.Append(FormattableString.Invariant($"<p>{after.Count(one => one.MeetsFloors)} of {after.Length} settings meet the floors, where luck alone passes about {HeavyweightSweep.Luck(after.Length):0} with no effect at all.</p>"));
        page.Append("<div class=\"table\"><table><thead><tr><th>Setting</th><th>Holdings</th><th>Edge before costs, points</th><th>After</th><th>At double</th><th>Years above nothing after</th><th>Without the five largest</th><th>Error</th></tr></thead><tbody>");

        foreach (var shown in after.OrderByDescending(one => one.Edge ?? double.MinValue).Take(15))
        {
            var (shownBefore, shownAfter, shownDoubled) = figures[shown.Key];

            page.Append(FormattableString.Invariant($"<tr><td>{WebUtility.HtmlEncode(shown.Key)}</td><td class=\"num\">{shownAfter.Trades:N0}</td><td class=\"num\">{Points(shownBefore.Edge)}</td><td class=\"num\">{Points(shownAfter.Edge)}</td><td class=\"num\">{Points(shownDoubled.Edge)}</td><td class=\"num\">{shownAfter.YearsBeating} of 8</td><td class=\"num\">{Points(shownAfter.EdgeWithoutLargest)}</td><td class=\"num\">{Points(shownAfter.StandardError)}</td></tr>"));
        }

        page.Append("</tbody></table></div></main></body></html>");

        var report = WriteRun(folder, page.ToString(), new { index = indexCode, family = Followers, note, proposal = proposed?.Key, settings = after.Select(one => new { one.Key, figures[one.Key].Before, figures[one.Key].After, figures[one.Key].Doubled }) }, new SweepAnswer(indexCode, HeavyweightRule.Name, DesignB, proposed is not null));

        var best = after.OrderByDescending(one => one.Edge ?? double.MinValue).First();

        output.WriteLine(proposed is not null
            ? FormattableString.Invariant($"proposed {proposed.Key}: edge after costs {Points(proposed.Edge)} points over {proposed.Trades} holdings, {proposed.YearsBeating} of 8 years")
            : FormattableString.Invariant($"none passed: no setting meets the floors after costs; the strongest, {best.Key}, reads {Points(best.Edge)} points over {best.Trades} holdings, {best.YearsBeating} of 8 years"));
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
