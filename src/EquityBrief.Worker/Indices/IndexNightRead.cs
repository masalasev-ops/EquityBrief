using EquityBrief.Core.Families;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Readings;
using EquityBrief.Core.Sweep;
using EquityBrief.Worker.Sweep;

namespace EquityBrief.Worker.Indices;

// One family's answer for one member of an index on the night: whether its rule passed it, and where it did, the trade
// bought at the night's close with its stop and its target or its trail and the figure its order reads; where it did
// not, the first part of the rule it failed.
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
    string? Reason);

// What one index's night read: its members holding a bar on the session, its breadth among them, whether its market
// check opened, and every member's answer under each swing family.
public sealed record IndexNight(string Index, DateOnly Session, int Members, double? Breadth, bool MarketOpen, IReadOnlyList<IndexAnswer> Answers);

// The trade a family's rule places on a member on the night: the close it is bought at, its stop, its target or the
// distance its stop trails, the sessions it is given, and the figure the family's order reads.
public readonly record struct IndexTrade(decimal Entry, decimal Stop, decimal? Target, decimal? Trail, int Cap, double Order);

// What one rule of a family reads on the night: the members its setup lists with their trades in the family's own
// order, whether its market check opened, and the first part of its rule past the setup a listed member fails.
public sealed record IndexFamilyRead(IReadOnlyList<(int Name, IndexTrade Trade)> Listed, bool Open, Func<int, string?> FailsOn);

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

    // The index's own breadth over its own members closes every swing list of the index together.
    // see: Each index's market check closes its own swing lists together on its own breadth
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
    public static IndexFamilyRead Provisional(IndexNightInputs inputs, string family, Func<string, IReadOnlyList<double?>?>? readings = null)
    {
        string? FailsOnProvisional(int name) => FailsOn(inputs, name, 1m, MemberReadings.LowestPrice, IndexQuality.Profit);

        return family switch
        {
            SetupFamilies.Pullback => new(Pullbacks(inputs, SweepIdeas.BaseRule, PullbackCap, inputs.Sessions), inputs.Open, FailsOnProvisional),
            BreakoutRule.Name => new(Breakouts(inputs, BreakoutAsFrozen, inputs.Sessions), inputs.Open, FailsOnProvisional),
            FundamentalsRule.Name => Fundamentals(inputs, FundamentalsRule.Provisional, readings),
            _ => new(Drifts(inputs, DriftAsFrozen, 0, null, inputs.Sessions), inputs.Open, FailsOnProvisional),
        };
    }

    // The fundamentals-first family at a setting: the pullback's base listings on the night, its trade the pullback's,
    // each member held to the floors and then to the family's own parts, its profit in the index's own form among them.
    // see: The fundamentals-first family buys an improving business in an uptrend at the pullback's buy point
    public static IndexFamilyRead Fundamentals(IndexNightInputs inputs, FundamentalsSetting setting, Func<string, IReadOnlyList<double?>?>? readings) =>
        Fundamentals(inputs, setting, readings, Pullbacks(inputs, SweepIdeas.BaseRule, PullbackCap, inputs.Sessions));

    // The family over the pullback listings handed to it.
    public static IndexFamilyRead Fundamentals(IndexNightInputs inputs, FundamentalsSetting setting, Func<string, IReadOnlyList<double?>?>? readings, IReadOnlyList<(int Name, IndexTrade Trade)> pullbacks)
    {
        string? FailsOnFundamentals(int name)
        {
            var ticker = inputs.Series[name].Name.Ticker;

            return FailsOn(inputs, name, 1m, MemberReadings.LowestPrice, IndexQuality.Off)
                ?? (readings?.Invoke(ticker) is { } read
                    ? FundamentalsRule.FailsOn(setting, read, inputs.Income.GetValueOrDefault(ticker) ?? [], inputs.Night)
                    : FundamentalsRule.NoReadings);
        }

        return new(pullbacks, inputs.Open, FailsOnFundamentals);
    }

    // Every member's answer under one family, the passing ones placed in the family's own order and every other naming
    // the first part of the rule it failed.
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

        var answers = new List<IndexAnswer>();

        foreach (var name in inputs.Held)
        {
            var ticker = inputs.Series[name].Name.Ticker;
            var reason = !read.Open ? MarketClosed : !trades.ContainsKey(name) ? NoSetup : read.FailsOn(name);

            answers.Add(reason is null && trades[name] is var trade
                ? new IndexAnswer(ticker, family, true, places[name], trade.Entry, trade.Stop, trade.Target, trade.Trail, trade.Cap, trade.Order, null)
                : new IndexAnswer(ticker, family, false, null, null, null, null, null, null, null, reason));
        }

        return answers;
    }

    // Where a listed member stands on the night against the floors and the quality: its close as traded at least the
    // lowest close and the floor, its mean dollar volume over the 50 sessions to it at least the index's floor at the
    // stated multiple, its quarters filed before the night passing the profit gate, and where the quality asks it the
    // coverage as the member readings stored it for the night, a coverage not read passing nothing.
    // see: A member's coverage is read on the night only over quarters whose fetch read their interest expense
    public static string? FailsOn(IndexNightInputs inputs, int name, decimal floors, decimal lowestClose, IndexQuality quality, Func<string, bool?>? coverage = null)
    {
        var one = inputs.Series[name];
        var bar = BarOf(one, inputs.At);
        var close = one.Bars[bar];
        var traded = close.RawClose > 0m ? close.RawClose : close.Close;
        var window = one.Bars[Math.Max(0, bar - MemberReadings.DollarVolumeSessions + 1)..(bar + 1)].Select(day => (day.Close, day.Volume)).ToArray();
        var income = inputs.Income.GetValueOrDefault(one.Name.Ticker) ?? [];

        return !MemberReadings.ClearsTheFloors(inputs.Index, traded, MemberReadings.DollarVolume(window) / floors) || traded < lowestClose ? UnderTheFloors
            : quality != IndexQuality.Off && !MemberReadings.Profit(income, inputs.Night) ? NoProfit
            : quality == IndexQuality.Cover && coverage?.Invoke(one.Name.Ticker) is not true ? NoCover
            : null;
    }

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
