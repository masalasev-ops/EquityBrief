using EquityBrief.Core.Families;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Readings;
using EquityBrief.Core.Sweep;
using EquityBrief.Worker.Sweep;

namespace EquityBrief.Worker.Indices;

// One family's answer for one member of an index on the night: whether its provisional rule passed it, and where it
// did, the trade bought at the night's close with its stop and its target or its trail and the figure its order reads;
// where it did not, the first part of the rule it failed.
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

// The S&P 400's and 600's provisional swing rules read on one night with the sweep's own code over the members' year of
// bars, each index's strength ranked and breadth read among its own members: the pullback's base as the ideas' run
// replays it, the breakout and the earnings drift at the S&P 500's frozen settings, each listing held to the price and
// dollar volume floors and the profit gate on its session, and every list closed together where the index's own
// breadth stands under the floor the S&P 500's filter reads.
// see: The 400's and 600's provisional picks are computed on the night by the sweep's own code into tables of their own
// see: The 400 and 600 rules start provisional with liquidity floors and a profit gate before any testing
public static class IndexNightRead
{
    // The families in the page's order, the words they are stored under.
    public static IReadOnlyList<string> Families { get; } = [SetupFamilies.Pullback, BreakoutRule.Name, DriftRule.Name];

    // The parts of a rule a member fails, in the rule's order, the first the row names.
    public const string MarketClosed = "the market check closed";

    public const string NoSetup = "no setup";

    public const string UnderTheFloors = "under the price or dollar volume floor";

    public const string NoProfit = "the profit check";

    // The breakout's and the drift's settings as frozen on the S&P 500, their places on each family's sweep grid.
    public static int[] BreakoutAsFrozen { get; } = Places(BreakoutSweep.Grid, [126, 1.5, 0.85, 1.5]);

    public static int[] DriftAsFrozen { get; } = Places(DriftSweep.Grid, [3, 0.5, 2.0, 2.5]);

    // The pullback's trade is held as the base's own exit holds it, to its target or its stop on closes, the sessions the
    // live design gives it.
    public static int PullbackCap => SweepDesign.Live.Hold;

    static int[] Places(FamilyGrid grid, double[] values) =>
        [.. values.Select((value, dial) => grid.Dials[dial].Levels.ToList().FindIndex(level => level == value))];

    public static IndexNight Read(string indexCode, DateOnly night, IReadOnlyList<SweepName> names, IReadOnlyDictionary<string, IReadOnlyList<FiledIncome>> income)
    {
        var calendar = names.SelectMany(name => name.Bars.Select(bar => bar.Session)).Where(session => session <= night).Distinct().Order().ToArray();
        var at = Array.IndexOf(calendar, night);

        if (at < 0)
        {
            return new IndexNight(indexCode, night, 0, null, false, []);
        }

        var sessionAt = calendar.Select((session, place) => (session, place)).ToDictionary(pair => pair.session, pair => pair.place);
        var series = new SweepSeries[names.Count];

        Parallel.For(0, names.Count, name => series[name] = SweepColumns.Series(names[name], sessionAt));

        var sessions = SweepColumns.Sessions(series, calendar);
        var members = SweepBenchmark.On(series, calendar.Length);
        var breadth = sessions[at].Breadth;
        var open = breadth >= FamilySweep.MarketFloor;
        var held = Enumerable.Range(0, series.Length).Where(name => BarOf(series[name], at) >= 0 && series[name].Member[BarOf(series[name], at)]).ToArray();

        // Where each listing's member stands on the night: whether it clears the floors and the gate.
        string? FailsOn(int name)
        {
            var one = series[name];
            var bar = BarOf(one, at);
            var close = one.Bars[bar];
            var traded = close.RawClose > 0m ? close.RawClose : close.Close;
            var window = one.Bars[Math.Max(0, bar - MemberReadings.DollarVolumeSessions + 1)..(bar + 1)].Select(day => (day.Close, day.Volume)).ToArray();

            return !MemberReadings.ClearsTheFloors(indexCode, traded, MemberReadings.DollarVolume(window)) ? UnderTheFloors
                : !MemberReadings.Profit(income.GetValueOrDefault(one.Name.Ticker) ?? [], night) ? NoProfit
                : null;
        }

        var answers = new List<IndexAnswer>();

        // Every member's answer under one family, the passing ones placed in the family's own order and every other
        // naming the first part of the rule it failed.
        void Answer(string family, IReadOnlyList<(int Name, (decimal Entry, decimal Stop, decimal? Target, decimal? Trail, int Cap, double Order) Trade)> listed)
        {
            var places = new Dictionary<int, int>();
            var trades = listed.ToDictionary(one => one.Name, one => one.Trade);

            foreach (var (name, _) in listed)
            {
                if (open && FailsOn(name) is null)
                {
                    places[name] = places.Count + 1;
                }
            }

            foreach (var name in held)
            {
                var ticker = series[name].Name.Ticker;
                var reason = !open ? MarketClosed : !trades.ContainsKey(name) ? NoSetup : FailsOn(name);

                answers.Add(reason is null && trades[name] is var trade
                    ? new IndexAnswer(ticker, family, true, places[name], trade.Entry, trade.Stop, trade.Target, trade.Trail, trade.Cap, trade.Order, null)
                    : new IndexAnswer(ticker, family, false, null, null, null, null, null, null, null, reason));
            }
        }

        Answer(SetupFamilies.Pullback, Pullbacks(series, sessions, members, calendar, at));
        Answer(BreakoutRule.Name, FromListings(series, BreakoutSweep.Listings(new BreakoutSweep(series, members).Readings(sessions, at), BreakoutAsFrozen), at));
        Answer(DriftRule.Name, FromListings(series, DriftSweep.Listings(new DriftSweep(series, members).Readings(sessions, at), DriftAsFrozen), at));

        return new IndexNight(indexCode, night, held.Length, breadth, open, answers);
    }

    static int BarOf(SweepSeries series, int session) => Array.BinarySearch(series.SessionAt, session);

    // The pullback's base on the night: the candidates the sweep reads for the session, the base's setting passing them as
    // the ideas' run replays it, each trade the plan's entry at the close, its stop that many typical moves under and its
    // target its reward to risk above, ordered as the list orders them, by reward to risk, then strength, then band
    // strength.
    static IReadOnlyList<(int Name, (decimal Entry, decimal Stop, decimal? Target, decimal? Trail, int Cap, double Order) Trade)> Pullbacks(
        SweepSeries[] series,
        IReadOnlyList<SweepColumns.Session> sessions,
        SweepBenchmark.Members members,
        DateOnly[] calendar,
        int at)
    {
        var found = new List<SweepCandidate>[series.Length];

        Parallel.For(0, series.Length, name => found[name] = SweepCandidates.For(series[name], name, sessions, calendar, at, at, at + 1));

        var replay = new IdeaReplay(series, [.. found.SelectMany(list => list)], members, [], 1);
        var listed = new List<(int, (decimal, decimal, decimal?, decimal?, int, double))>();

        foreach (var trade in replay.Trades(SweepIdeas.BaseRule).Where(trade => trade.Listing.Session == at)
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
                listed.Add((trade.Listing.Name, (entry, stop, Statistic.ToPrice(Statistic.FromPrice(entry) + (trade.Listing.RewardToRisk * risk)), null, PullbackCap, trade.Listing.RewardToRisk)));
            }
        }

        return listed;
    }

    // A family's listings on the night as trades, in the family's own order.
    static IReadOnlyList<(int Name, (decimal Entry, decimal Stop, decimal? Target, decimal? Trail, int Cap, double Order) Trade)> FromListings(SweepSeries[] series, IEnumerable<FamilyListing> listings, int at)
    {
        var listed = new List<(int, (decimal, decimal, decimal?, decimal?, int, double))>();

        foreach (var listing in listings.Where(listing => listing.Session == at).OrderByDescending(listing => listing.Order).ThenBy(listing => series[listing.Name].Name.Ticker, StringComparer.Ordinal))
        {
            if (listed.Any(one => one.Item1 == listing.Name))
            {
                continue;
            }

            listed.Add((listing.Name, (
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
