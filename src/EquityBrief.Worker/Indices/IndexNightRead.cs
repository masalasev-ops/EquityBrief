using EquityBrief.Core.Cards;
using EquityBrief.Core.Families;
using EquityBrief.Core.Filter;
using EquityBrief.Core.Ledger;
using EquityBrief.Core.Loop;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Readings;
using EquityBrief.Core.Sweep;
using EquityBrief.Worker.Sweep;

namespace EquityBrief.Worker.Indices;

// One family's answer for one member of an index on the night: whether its rule passed it, and where it did, the trade
// bought at the night's close with its stop and its target or its trail and the figure its order reads; where it did
// not, the first part of the rule it failed; and from 20.11 every part's answer, the market check its own.
public sealed record IndexAnswer(
    string Ticker,
    string Family,
    bool Passed,
    int? Place,
    decimal? Entry,
    decimal? Stop,
    decimal? Target,
    decimal? Trail,
    int? Cap,
    double? OrderBy,
    string? Reason,
    IReadOnlyList<Gate>? Gates = null);

// What one index's night read: its members holding a bar on the session, its breadth among them, whether its market
// check opened, and every member's answer under each swing family.
public sealed record IndexNight(string Index, DateOnly Session, int Members, double? Breadth, bool MarketOpen, IReadOnlyList<IndexAnswer> Answers);

// The trade a family's rule places on a member on the night: the close it is bought at, its stop, its target or the
// distance its stop trails, the sessions it is given, and the figure the family's order reads.
public readonly record struct IndexTrade(decimal Entry, decimal Stop, decimal? Target, decimal? Trail, int Cap, double Order);

// One part of a rule past its setup as a member stands against it: the reason a member failing it is named by, whether
// it passed, and the words a member failing it for want of a reading carries in their place.
public readonly record struct RulePart(string Reason, bool Passed, string? Words = null)
{
    // The part as a gate, named as the rule's funnel names the part.
    public Gate Gate => new(RuleRows.PartOf(Reason), Passed, Passed ? string.Empty : Words ?? Reason, FamilyRule.Values());
}

// What one rule of a family reads on the night: the members its setup lists with their trades in the family's own
// order, its market check's answer, and every part of its rule past the setup a member is read against, each with its
// answer, the first a listed member fails naming it; and the members its setup lists read with the check open, the same
// as those it lists on a night the check opened.
// see: The S&P 400's and 600's nights store every part's answer for every member, the market check its own
public sealed record IndexFamilyRead(IReadOnlyList<(int Name, IndexTrade Trade)> Listed, Gate Market, Func<int, IReadOnlyList<RulePart>> PartsOf)
{
    public bool Open => Market.Passed;

    public IReadOnlyList<(int Name, IndexTrade Trade)>? ListedIfOpen { get; init; }

    public string? FailsOn(int name) => IndexNightRead.FirstFailed(PartsOf(name));
}

// The night's inputs every rule of one index reads: the sessions to the night and the night's place among them, the
// members' series, the index's sessions with its own breadth, the members the benchmark reads, the members holding a bar
// on the night, and their quarters as filed.
public sealed record IndexNightInputs(
    string Index,
    DateOnly Night,
    DateOnly[] Calendar,
    int At,
    SweepSeries[] Series,
    IReadOnlyList<SweepColumns.Session> Sessions,
    SweepBenchmark.Members Members,
    int[] Held,
    IReadOnlyDictionary<string, IReadOnlyList<FiledIncome>> Income)
{
    public double? Breadth => Sessions[At].Breadth;

    // The index's own breadth over its own members at the S&P 500's floor, which closes the lists of every rule of the
    // index with no freeze standing together; a registered rule reads the breadth and the floor it states.
    // see: Each frozen rule on the S&P 400 and 600 reads its market check at the level its own sweep chose
    public bool Open => Breadth >= FamilySweep.MarketFloor;
}

// The S&P 400's and 600's swing rules read on one night with the sweep's own code over the members' year of bars, each
// index's strength ranked and breadth read among its own members. A family with no live rule registered on its index
// reads its provisional rule: the pullback's base as the ideas' run replays it, the breakout and the earnings drift at
// the S&P 500's frozen settings, each listing held to the price and dollar volume floors and the profit gate on its
// session, and every list closed together where the index's own breadth stands under the floor the S&P 500's filter
// reads. A family whose live rule stands reads that rule through the same functions at its own settings.
// see: The 400's and 600's provisional picks are computed on the night by the sweep's own code into tables of their own
// see: The 400 and 600 rules start provisional with liquidity floors and a profit gate before any testing
// see: A rule of the S&P 400's or 600's swing families is registered as the family on its index and evaluated by their step alone
public static class IndexNightRead
{
    // The families in the page's order, the words they are stored under, the fundamentals-first family last; and the one
    // the S&P 500 is read for here, its other families being the S&P 500's own.
    public static IReadOnlyList<string> Families { get; } = [SetupFamilies.Pullback, BreakoutRule.Name, DriftRule.Name, FundamentalsRule.Name];

    public static IReadOnlyList<string> LargeFamilies { get; } = [FundamentalsRule.Name];

    // The parts of a rule a member fails, in the rule's order, the first the row names.
    public const string MarketClosed = "the market check closed";

    public const string NoSetup = "no setup";

    public const string UnderTheFloors = "under the price or dollar volume floor";

    public const string NoProfit = "the profit check";

    public const string NoCover = "the interest cover check";

    // What a part reading the trade a setup places says of a member no setup listed.
    public const string NotReadWithoutASetup = "not read: no setup tonight";

    // The breadth a market check reads, the index's own unless its rule states the S&P 500's.
    public const string OwnBreadth = "the index's own breadth";

    public const string LargeBreadth = "the S&P 500's breadth";

    // The market check as a gate: the breadth it reads against its floor, failing where the night holds none.
    public static Gate MarketGate(double? breadth, double floor, string reading = OwnBreadth) =>
        breadth is { } share
            ? new Gate(
                RuleRows.Market,
                share >= floor,
                FormattableString.Invariant($"{reading} {FloorShare.Percent(share, floor)}% {(share >= floor ? "at or above" : "below")} its floor of {floor * 100:0.#}%"),
                FamilyRule.Values(("breadth", FamilyRule.Figure(share)), ("floor", FamilyRule.Figure(floor))))
            : new Gate(RuleRows.Market, false, $"{reading} not read tonight", FamilyRule.Values(("floor", FamilyRule.Figure(floor))));

    // The market check of a rule that reads none, open on every night.
    public static Gate NoMarketGate { get; } = new(RuleRows.Market, true, "its rule reads no market check", FamilyRule.Values());

    public static Gate SetupGateOf(bool listed) => new(RuleRows.Setup, listed, listed ? string.Empty : NoSetup, FamilyRule.Values());

    // The sessions with the night read as every member above its average, so a setup lists as on a night its market
    // check opened.
    public static IReadOnlyList<SweepColumns.Session> OpenOn(IReadOnlyList<SweepColumns.Session> sessions, int at) =>
        [.. sessions.Select((one, place) => place == at ? one with { Breadth = 1.0 } : one)];

    // Every session read as every member above its average, as the sweeps on each index's own history read a family's
    // listings before its market check keeps the sessions its level holds open: a drift waiting for its pullback reads
    // the sessions back to its reaction so.
    public static IReadOnlyList<SweepColumns.Session> AllOpen(IReadOnlyList<SweepColumns.Session> sessions) =>
        [.. sessions.Select(one => one with { Breadth = 1.0 })];

    // The first part a member fails in the rule's order, none where it passes every part.
    public static string? FirstFailed(IEnumerable<RulePart> parts) => parts.Where(part => !part.Passed).Select(part => part.Reason).FirstOrDefault();

    // The breakout's and the drift's settings as frozen on the S&P 500, their places on each family's sweep grid.
    public static int[] BreakoutAsFrozen { get; } = Places(BreakoutSweep.Grid, [126, 1.5, 0.85, 1.5]);

    public static int[] DriftAsFrozen { get; } = Places(DriftSweep.Grid, [3, 0.5, 2.0, 2.5]);

    // The pullback's trade is held as the base's own exit holds it, to its target or its stop on closes, the sessions the
    // live design gives it.
    public static int PullbackCap => SweepDesign.Live.Hold;

    public static int[] Places(FamilyGrid grid, double[] values) =>
        [.. values.Select((value, dial) => grid.Dials[dial].Levels.ToList().FindIndex(level => level == value))];

    public static IndexNight Read(
        string indexCode,
        DateOnly night,
        IReadOnlyList<SweepName> names,
        IReadOnlyDictionary<string, IReadOnlyList<FiledIncome>> income,
        Func<IndexNightInputs, string, IndexFamilyRead?>? live = null)
    {
        if (Prepare(indexCode, night, names, income) is not { } inputs)
        {
            return new IndexNight(indexCode, night, 0, null, false, []);
        }

        var answers = new List<IndexAnswer>();

        foreach (var family in Families)
        {
            answers.AddRange(Answers(inputs, family, live?.Invoke(inputs, family) ?? Provisional(inputs, family)));
        }

        return new IndexNight(indexCode, night, inputs.Held.Length, inputs.Breadth, inputs.Open, answers);
    }

    // The night's inputs, none where no member holds a bar on the night.
    public static IndexNightInputs? Prepare(string indexCode, DateOnly night, IReadOnlyList<SweepName> names, IReadOnlyDictionary<string, IReadOnlyList<FiledIncome>> income)
    {
        var calendar = names.SelectMany(name => name.Bars.Select(bar => bar.Session)).Where(session => session <= night).Distinct().Order().ToArray();
        var at = Array.IndexOf(calendar, night);

        if (at < 0)
        {
            return null;
        }

        var sessionAt = calendar.Select((session, place) => (session, place)).ToDictionary(pair => pair.session, pair => pair.place);
        var series = new SweepSeries[names.Count];

        Parallel.For(0, names.Count, name => series[name] = SweepColumns.Series(names[name], sessionAt));

        var sessions = SweepColumns.Sessions(series, calendar);
        var members = SweepBenchmark.On(series, calendar.Length);
        var held = Enumerable.Range(0, series.Length).Where(name => BarOf(series[name], at) >= 0 && series[name].Member[BarOf(series[name], at)]).ToArray();

        return new IndexNightInputs(indexCode, night, calendar, at, series, sessions, members, held, income);
    }

    // A family's provisional rule on the night; the fundamentals-first family reads the catalogue's readings of each
    // member the pullback's base lists, and lists none on a night handed none.
    public static IndexFamilyRead Provisional(IndexNightInputs inputs, string family, Func<string, IReadOnlyList<double?>?>? readings = null) =>
        family switch
        {
            SetupFamilies.Pullback => Swing(inputs, sessions => Pullbacks(inputs, SweepIdeas.BaseRule, PullbackCap, sessions)),
            BreakoutRule.Name => Swing(inputs, sessions => Breakouts(inputs, BreakoutAsFrozen, sessions)),
            FundamentalsRule.Name => Fundamentals(inputs, FundamentalsRule.Provisional, readings),
            _ => Swing(inputs, sessions => Drifts(inputs, DriftAsFrozen, 0, null, sessions)),
        };

    // A swing family's read at the provisional floors and gate: its setups on the night, its market check on the index's
    // own breadth with its setups read with the check open where it closed, and each member's parts past the setup.
    static IndexFamilyRead Swing(IndexNightInputs inputs, Func<IReadOnlyList<SweepColumns.Session>, IReadOnlyList<(int Name, IndexTrade Trade)>> setups)
    {
        var market = MarketGate(inputs.Breadth, FamilySweep.MarketFloor);
        var listed = setups(inputs.Sessions);

        return new(listed, market, name => PartsOn(inputs, name, 1m, MemberReadings.LowestPrice, IndexQuality.Profit))
        {
            ListedIfOpen = market.Passed ? listed : setups(OpenOn(inputs.Sessions, inputs.At)),
        };
    }

    // Why a member the family's own rule lists is left off by the hooks an approval set.
    public const string NotByItsHooks = "a condition or a score floor its approved setting adds";

    // A swing family at the setting an approval stored for it on the index: the breakout's or the drift's setting on its
    // sweep's grid where it states one, the drift's stop floor with it, and the pullback at its base; each member held to
    // the provisional floors and gate, and the family's list kept and ordered by the setting's hooks over the night's
    // readings, a member they leave off naming why, and every member left off on a night handed no readings where the
    // hooks read them.
    // see: An approved change is applied by the night's own build on the index it was approved on alone
    public static IndexFamilyRead Stored(IndexNightInputs inputs, string family, LoopChange setting, Func<string, IReadOnlyList<double?>?>? readings, Func<long, TreeModel?>? trees = null)
    {
        int[]? places = setting.Places is { } held ? [.. held] : null;

        var read = family switch
        {
            SetupFamilies.Pullback => Swing(inputs, sessions => Pullbacks(inputs, SweepIdeas.BaseRule, PullbackCap, sessions)),
            BreakoutRule.Name => Swing(inputs, sessions => Breakouts(inputs, places ?? BreakoutAsFrozen, sessions)),
            DriftRule.Name => Swing(inputs, sessions => Drifts(inputs, places ?? DriftAsFrozen, setting.StopFloor ?? 0, null, sessions)),
            _ => Provisional(inputs, family, readings),
        };

        return Hooked(inputs, read, RuleHooks.Of(setting.Hooks).With(trees), readings);
    }

    // A family's read with its list kept and ordered by hooks: those the hooks keep first in their order, then those they
    // leave off, each of which fails on them after the family's own parts, every member read against them by its own
    // readings. From 21.4 a rule stating the exposure reads its setups with the market check open and takes the exposure's
    // answer in the check's place, the picks the night's two switches allow, read off the night's readings, which every
    // member of the index shares; a member past the picks is left off by its hooks as one failing a condition is. The
    // expected value of the setups like each member is handed in by the step that lists where it has one.
    public static IndexFamilyRead Hooked(IndexNightInputs inputs, IndexFamilyRead read, RuleHooks hooks, Func<string, IReadOnlyList<double?>?>? readings, Func<string, double?>? expected = null)
    {
        if (!hooks.ReadsReadings && !hooks.ReadsExpected)
        {
            return read;
        }

        IReadOnlyList<double?>? ReadingsOf((int Name, IndexTrade Trade) one) => readings?.Invoke(inputs.Series[one.Name].Name.Ticker);

        double? ExpectedOf((int Name, IndexTrade Trade) one) => expected?.Invoke(inputs.Series[one.Name].Name.Ticker);

        var candidates = hooks.Exposure is not null ? read.ListedIfOpen ?? read.Listed : read.Listed;
        var kept = hooks.Order(candidates, ReadingsOf, ExpectedOf);
        var keeps = kept.Select(one => one.Name).ToHashSet();

        return read with
        {
            Listed = [.. kept, .. candidates.Where(one => !keeps.Contains(one.Name))],
            Market = hooks.Exposure is { } floor ? ExposureGate(hooks, floor, NightReadings(inputs, readings)) : read.Market,
            PartsOf = name => [.. read.PartsOf(name), new RulePart(NotByItsHooks, keeps.Contains(name))],
            ListedIfOpen = hooks.Exposure is not null ? candidates : read.ListedIfOpen,
        };
    }

    // The night's readings every member of the index shares, off the first member holding any; none where the night
    // supplies none.
    public static IReadOnlyList<double?>? NightReadings(IndexNightInputs inputs, Func<string, IReadOnlyList<double?>?>? readings) =>
        readings is null ? null : inputs.Held.Select(name => readings(inputs.Series[name].Name.Ticker)).FirstOrDefault(one => one is not null);

    // The exposure's answer in the market check's place: the picks the night allows, the breadth it read against the
    // floor the rule states and the VIX's close over its median, closed where it allows none.
    public static Gate ExposureGate(RuleHooks hooks, double floor, IReadOnlyList<double?>? night)
    {
        var picks = hooks.PicksUnder(night) ?? 0;
        var breadth = night is not null && RuleHooks.BreadthColumn >= 0 ? night[RuleHooks.BreadthColumn] : null;
        var over = night is not null && RuleHooks.VixOverMedianColumn >= 0 ? night[RuleHooks.VixOverMedianColumn] : null;

        return new Gate(
            RuleRows.Market,
            picks > 0,
            FormattableString.Invariant($"the exposure allows {picks} pick(s) tonight: the index's breadth {FamilyRule.Figure(breadth)} against its floor of {floor:0.##}, and the VIX at {FamilyRule.Figure(over)} of its median of the {LedgerReadings.VixMedianSessions} sessions before, at or under 1 holding"),
            FamilyRule.Values(("breadth", FamilyRule.Figure(breadth)), ("floor", FamilyRule.Figure(floor)), ("vix over median", FamilyRule.Figure(over)), ("picks", FamilyRule.Figure(picks))));
    }

    // The fundamentals-first family at a setting: the pullback's base listings on the night, its trade the pullback's,
    // each member held to the floors and then to the family's own parts, its profit in the index's own form among them.
    // see: The fundamentals-first family buys an improving business in an uptrend at the pullback's buy point
    public static IndexFamilyRead Fundamentals(IndexNightInputs inputs, FundamentalsSetting setting, Func<string, IReadOnlyList<double?>?>? readings)
    {
        var pullbacks = Pullbacks(inputs, SweepIdeas.BaseRule, PullbackCap, inputs.Sessions);

        return Fundamentals(inputs, setting, readings, pullbacks, inputs.Open ? pullbacks : Pullbacks(inputs, SweepIdeas.BaseRule, PullbackCap, OpenOn(inputs.Sessions, inputs.At)));
    }

    // The family over the pullback listings handed to it, and those the pullback lists with the check open where they
    // differ.
    public static IndexFamilyRead Fundamentals(
        IndexNightInputs inputs,
        FundamentalsSetting setting,
        Func<string, IReadOnlyList<double?>?>? readings,
        IReadOnlyList<(int Name, IndexTrade Trade)> pullbacks,
        IReadOnlyList<(int Name, IndexTrade Trade)>? ifOpen = null)
    {
        IReadOnlyList<RulePart> PartsOnFundamentals(int name)
        {
            var ticker = inputs.Series[name].Name.Ticker;

            return
            [
                .. PartsOn(inputs, name, 1m, MemberReadings.LowestPrice, IndexQuality.Off),
                .. FundamentalsRule.Parts(setting, readings?.Invoke(ticker), inputs.Income.GetValueOrDefault(ticker) ?? [], inputs.Night).Select(part => new RulePart(part.Reason, part.Passed)),
            ];
        }

        return new(pullbacks, MarketGate(inputs.Breadth, FamilySweep.MarketFloor), PartsOnFundamentals) { ListedIfOpen = ifOpen ?? pullbacks };
    }

    // Every member's answer under one family, the passing ones placed in the family's own order and every other naming
    // the first part of the rule it failed, each with every part's answer: the market check, the setup read with the check
    // open, and each part past the setup.
    public static IReadOnlyList<IndexAnswer> Answers(IndexNightInputs inputs, string family, IndexFamilyRead read)
    {
        var places = new Dictionary<int, int>();
        var trades = new Dictionary<int, IndexTrade>();

        foreach (var (name, trade) in read.Listed)
        {
            trades.TryAdd(name, trade);
        }

        foreach (var (name, _) in read.Listed)
        {
            if (read.Open && !places.ContainsKey(name) && read.FailsOn(name) is null)
            {
                places[name] = places.Count + 1;
            }
        }

        var ifOpen = (read.ListedIfOpen ?? read.Listed).Select(one => one.Name).ToHashSet();
        var answers = new List<IndexAnswer>();

        foreach (var name in inputs.Held)
        {
            var ticker = inputs.Series[name].Name.Ticker;
            var parts = read.PartsOf(name);
            var reason = !read.Open ? MarketClosed : !trades.ContainsKey(name) ? NoSetup : FirstFailed(parts);
            IReadOnlyList<Gate> gates = [read.Market, SetupGateOf(ifOpen.Contains(name)), .. parts.Select(part => part.Gate)];

            answers.Add(reason is null && trades[name] is var trade
                ? new IndexAnswer(ticker, family, true, places[name], trade.Entry, trade.Stop, trade.Target, trade.Trail, trade.Cap, trade.Order, null, gates)
                : new IndexAnswer(ticker, family, false, null, null, null, null, null, null, null, reason, gates));
        }

        return answers;
    }

    // Where a member stands on the night against the floors and the quality, each a part of its own in the rule's order:
    // its close as traded at least the lowest close and the floor with its mean dollar volume over the 50 sessions to it at
    // least the index's floor at the stated multiple, its quarters filed before the night passing the profit gate, and
    // where the quality asks it the coverage as the member readings stored it for the night, a coverage not read passing
    // nothing; a part a member fails saying what it had.
    // see: A member's coverage is read on the night only over quarters whose fetch read their interest expense
    public static IReadOnlyList<RulePart> PartsOn(IndexNightInputs inputs, int name, decimal floors, decimal lowestClose, IndexQuality quality, Func<string, bool?>? coverage = null)
    {
        var one = inputs.Series[name];
        var bar = BarOf(one, inputs.At);
        var close = one.Bars[bar];
        var traded = close.RawClose > 0m ? close.RawClose : close.Close;
        var window = one.Bars[Math.Max(0, bar - MemberReadings.DollarVolumeSessions + 1)..(bar + 1)].Select(day => (day.Close, day.Volume)).ToArray();
        var dollarVolume = MemberReadings.DollarVolume(window);
        var least = MemberReadings.DollarVolumeFloor(inputs.Index) is not null ? Math.Max(lowestClose, MemberReadings.LowestPrice) : lowestClose;
        var traded50 = dollarVolume is { } mean ? FormattableString.Invariant($"{mean:#,0} a session over the 50 sessions to tonight") : "fewer than 50 sessions of volume to tonight";
        var floorWords = MemberReadings.DollarVolumeFloor(inputs.Index) is { } held ? FormattableString.Invariant($" and its floor of {held * floors:#,0}") : string.Empty;
        var parts = new List<RulePart>
        {
            new(
                UnderTheFloors,
                MemberReadings.ClearsTheFloors(inputs.Index, traded, dollarVolume / floors) && traded >= lowestClose,
                FormattableString.Invariant($"closed at {traded:0.00} with {traded50}, against the least close of {least:0.00}{floorWords}")),
        };

        if (quality != IndexQuality.Off)
        {
            var income = inputs.Income.GetValueOrDefault(one.Name.Ticker) ?? [];
            var filed = MemberReadings.FiledBefore(income, inputs.Night);
            var words = filed.Count < MemberReadings.Quarters || filed.Any(quarter => quarter.NetIncome is null)
                ? FormattableString.Invariant($"{filed.Count(quarter => quarter.NetIncome is not null)} of the {MemberReadings.Quarters} quarters the profit check sums filed with their net income before tonight")
                : FormattableString.Invariant($"net income of {filed.Sum(quarter => quarter.NetIncome!.Value):#,0} over its {MemberReadings.Quarters} newest quarters as filed, not above nothing");

            parts.Add(new(NoProfit, MemberReadings.Profit(income, inputs.Night), words));
        }

        if (quality == IndexQuality.Cover)
        {
            var covered = coverage?.Invoke(one.Name.Ticker);

            parts.Add(new(NoCover, covered is true, covered is null ? "its interest cover not read for tonight" : "its interest cover under its floor"));
        }

        return parts;
    }

    // The first of those parts a listed member fails, none where it passes them all.
    public static string? FailsOn(IndexNightInputs inputs, int name, decimal floors, decimal lowestClose, IndexQuality quality, Func<string, bool?>? coverage = null) =>
        FirstFailed(PartsOn(inputs, name, floors, lowestClose, quality, coverage));

    public static int BarOf(SweepSeries series, int session) => Array.BinarySearch(series.SessionAt, session);

    // The pullback on the night at a rule's setting: the candidates the sweep reads for the session, the setting passing
    // them as the ideas' run replays it, each trade the plan's entry at the close, its stop that many typical moves under
    // and its target its reward to risk above, ordered as the list orders them, by reward to risk, then strength, then
    // band strength.
    public static IReadOnlyList<(int Name, IndexTrade Trade)> Pullbacks(IndexNightInputs inputs, IdeaRule rule, int cap, IReadOnlyList<SweepColumns.Session> sessions)
    {
        var (series, at) = (inputs.Series, inputs.At);
        var found = new List<SweepCandidate>[series.Length];

        Parallel.For(0, series.Length, name => found[name] = SweepCandidates.For(series[name], name, sessions, inputs.Calendar, at, at, at + 1));

        var replay = new IdeaReplay(series, [.. found.SelectMany(list => list)], inputs.Members, [], 1);
        var listed = new List<(int, IndexTrade)>();

        foreach (var trade in replay.Trades(rule).Where(trade => trade.Listing.Session == at)
            .OrderByDescending(trade => trade.Listing.RewardToRisk)
            .ThenByDescending(trade => trade.Listing.Strength)
            .ThenByDescending(trade => trade.Listing.Band)
            .ThenBy(trade => series[trade.Listing.Name].Name.Ticker, StringComparer.Ordinal))
        {
            var one = series[trade.Listing.Name];
            var entry = one.Bars[trade.Listing.Bar].Close;
            var risk = trade.Listing.StopMoves * one.Atr[trade.Listing.Bar];
            var stop = Statistic.ToPrice(Statistic.FromPrice(entry) - risk);

            if (listed.All(one => one.Item1 != trade.Listing.Name))
            {
                listed.Add((trade.Listing.Name, new IndexTrade(entry, stop, Statistic.ToPrice(Statistic.FromPrice(entry) + (trade.Listing.RewardToRisk * risk)), null, cap, trade.Listing.RewardToRisk)));
            }
        }

        return listed;
    }

    // The breakout's listings on the night at a setting of its sweep's grid, in its own order.
    public static IReadOnlyList<(int Name, IndexTrade Trade)> Breakouts(IndexNightInputs inputs, int[] setting, IReadOnlyList<SweepColumns.Session> sessions) =>
        FromListings(inputs, BreakoutSweep.Listings(new BreakoutSweep(inputs.Series, inputs.Members).Readings(sessions, inputs.At), setting));

    // The drift's listings on the night at a setting of its sweep's grid, its stop held the stated typical moves under the
    // buy where the reaction's low sits nearer, and read over a wider window where one is stated.
    public static IReadOnlyList<(int Name, IndexTrade Trade)> Drifts(IndexNightInputs inputs, int[] setting, double stopFloor, double? window, IReadOnlyList<SweepColumns.Session> sessions) =>
        FromListings(inputs, DriftSweep.Listings(new DriftSweep(inputs.Series, inputs.Members).Readings(sessions, inputs.At, window), setting, stopFloor, window));

    // From 20.11 the drift on the night bought on the first pullback after its reaction, as its sweep's entry level reads
    // it: every listing its setting makes over the sessions a reaction the night's pullback follows can sit in, each moved
    // to the first session within ten after its reaction whose close is below the close before it, and kept where that
    // session is the night, a member's first in the order its readings are read; its stop the one its listing set, its
    // target the nearer of the band and the setting's multiple of the new risk. A member whose first such session came
    // before the night, or finds its close at or under the stop, lists none, and no later session is read for it.
    // see: A drift rule on the S&P 400 or 600 stating the first pullback is bought on the night through the one function its search's entry level reads
    public static IReadOnlyList<(int Name, IndexTrade Trade)> DriftPullbacks(IndexNightInputs inputs, int[] setting, double stopFloor, double? window, IReadOnlyList<SweepColumns.Session> sessions, IDictionary<int, int>? backs = null)
    {
        var readings = new DriftSweep(inputs.Series, inputs.Members).Readings(sessions, Math.Max(0, inputs.At - DriftSweep.FirstPullbackSessions), window);
        var reactions = readings.ToDictionary(reading => (reading.Name, reading.Bar), reading => reading.Bar - reading.Back);
        var multiple = DriftSweep.Grid.Value(setting, 3);
        var moved = DriftSweep.Listings(readings, setting, stopFloor, window)
            .SelectMany(listing => DriftSweep.FirstPullback(listing, inputs.Series[listing.Name], reactions[(listing.Name, listing.Bar)], multiple, sessions)
                .Select(pullback => (Listing: pullback, Reaction: reactions[(listing.Name, listing.Bar)])))
            .Where(one => one.Listing.Session == inputs.At)
            .DistinctBy(one => one.Listing.Name)
            .ToArray();

        foreach (var (listing, reaction) in moved)
        {
            backs?.TryAdd(listing.Name, inputs.At - inputs.Series[listing.Name].SessionAt[reaction]);
        }

        return FromListings(inputs, moved.Select(one => one.Listing));
    }

    // A family's listings on the night as trades, in the family's own order.
    static IReadOnlyList<(int Name, IndexTrade Trade)> FromListings(IndexNightInputs inputs, IEnumerable<FamilyListing> listings)
    {
        var (series, at) = (inputs.Series, inputs.At);
        var listed = new List<(int, IndexTrade)>();

        foreach (var listing in listings.Where(listing => listing.Session == at).OrderByDescending(listing => listing.Order).ThenBy(listing => series[listing.Name].Name.Ticker, StringComparer.Ordinal))
        {
            if (listed.Any(one => one.Item1 == listing.Name))
            {
                continue;
            }

            listed.Add((listing.Name, new IndexTrade(
                series[listing.Name].Bars[listing.Bar].Close,
                Statistic.ToPrice(listing.Stop),
                double.IsNaN(listing.Target) ? null : Statistic.ToPrice(listing.Target),
                listing.Trails ? Statistic.ToPrice(listing.Trail) : null,
                listing.Cap,
                listing.Order)));
        }

        return listed;
    }
}
