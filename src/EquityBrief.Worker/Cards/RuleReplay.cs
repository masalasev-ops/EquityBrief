using EquityBrief.Core.Cards;
using EquityBrief.Core.Families;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Readings;
using EquityBrief.Core.Sweep;
using EquityBrief.Worker.Candidates;
using EquityBrief.Worker.Indices;
using EquityBrief.Worker.Sweep;

namespace EquityBrief.Worker.Cards;

// One rule's replay for the record its card draws: the trades its one setting keeps over the pulled history with the
// sweep's own walk, five a night with one open trade a stock as the page lists them, each after its cost at the published
// table, with the sessions it was held and the lowest close it was held through. The S&P 500's rules apply no floors and
// no gate, and the S&P 400's and 600's apply their own; no setting is searched, so a replay costs one walk a rule. It
// reads the store and writes nothing.
// see: A rule's record is replayed at its one setting by the sweep's own code over the pulled history, after costs on every index
public static class RuleReplay
{
    public const string Risks = "risks";

    public const string Percent = "percent";

    public const string AsItStood = "as it stood";

    public const string SurvivorsOnly = "survivors only";

    // One rule's replay: its family, the rule in words, the setting it was walked at, the history's first and last
    // session, whether membership was read as it stood or as today's members, the unit its results are in, its trades,
    // and how many it listed on each scored session, zeros included, which the cards' stretch lines read.
    public sealed record Replayed(string Family, string Rule, string Settings, DateOnly From, DateOnly Through, string Membership, string Unit, IReadOnlyList<RecordTrade> Trades, IReadOnlyList<(DateOnly Session, int Listed)> Nights);

    // A registered breakout or drift rule walked in place of the family's frozen setting: its name as the register
    // holds it, its family, its place on the family's grid and the drift's stop floor in typical moves.
    public sealed record SwingVariant(string Name, string Family, int[] Setting, double StopFloor);

    // The families a card names on an index, in the page's order with the sector heavyweights after them.
    public static IReadOnlyList<string> Families { get; } = [SetupFamilies.Pullback, BreakoutRule.Name, DriftRule.Name, HeavyweightRule.Name];

    // Every rule a card can name on an index, or the families named, each replayed over the history the index's sweep
    // reads; where variants are given for the breakout or the drift, each is walked at its own setting in place of the
    // family's frozen one.
    public static async Task<IReadOnlyList<Replayed>> IndexAsync(string databaseFile, string index, Action<string> progress, CancellationToken cancellation, IReadOnlyList<string>? families = null, IReadOnlyList<SwingVariant>? variants = null)
    {
        bool Asked(string family) => families is null || families.Contains(family, StringComparer.Ordinal);

        var large = index == IndexFamilies.LargeIndex;
        var history = new SweepHistory(databaseFile);
        var through = await history.NewestSessionAsync(cancellation);
        var inputs = large
            ? await history.ReadAsync(through, progress, cancellation)
            : await history.ReadAsync(through, progress, cancellation, index: index, asItStood: true);
        var companies = await history.HeavyweightAsync(through, cancellation);
        var income = large ? new Dictionary<string, IReadOnlyList<FiledIncome>>(StringComparer.Ordinal) : await history.IncomeAsync(through, cancellation);
        var market = await history.MarketAsync(through, cancellation);
        var fund = large ? market.FirstOrDefault(one => one.Series == "GSPC") : (await history.SeriesOfAsync([IndexSweepRunner.IndexFunds[index]], through, cancellation)).FirstOrDefault();
        var calendar = inputs.Sessions;
        var sessionAt = calendar.Select((session, at) => (session, at)).ToDictionary(pair => pair.session, pair => pair.at);
        var firstScored = Array.FindIndex(calendar, session => session >= SweepColumns.FirstScored);
        var series = new SweepSeries[inputs.Names.Count];

        Parallel.For(0, inputs.Names.Count, name => series[name] = SweepColumns.Series(inputs.Names[name], sessionAt));

        var read = new Read(index, large, inputs, series, companies, income, calendar[firstScored], inputs.Survivors > 0 ? SurvivorsOnly : AsItStood);
        var replayed = new List<Replayed>();

        if (Asked(SetupFamilies.Pullback))
        {
            progress($"replaying the pullback on the {DecisionCards.NameOf(index)}");
            replayed.Add(Pullback(read, market, progress));
        }

        if (Asked(BreakoutRule.Name) || Asked(DriftRule.Name))
        {
            progress($"replaying the breakout and the drift on the {DecisionCards.NameOf(index)}");
            var sessions = SweepColumns.Sessions(series, calendar);
            var members = SweepBenchmark.On(series, calendar.Length);

            foreach (var family in new[] { BreakoutRule.Name, DriftRule.Name }.Where(Asked))
            {
                var own = (variants ?? []).Where(variant => variant.Family == family).ToArray();

                if (own.Length == 0)
                {
                    replayed.Add(Swing(read, family, sessions, members, firstScored));
                }

                foreach (var variant in own)
                {
                    progress($"replaying {variant.Name} on the {DecisionCards.NameOf(index)}");
                    replayed.Add(Swing(read, family, sessions, members, firstScored, variant.Setting, variant.StopFloor) with { Rule = variant.Name });
                }
            }
        }

        if (Asked(HeavyweightRule.Name))
        {
            progress($"replaying the sector heavyweights on the {DecisionCards.NameOf(index)}");
            replayed.Add(Heavyweights(read, fund, firstScored));
        }

        return replayed;
    }

    // What every rule of an index is replayed over.
    sealed record Read(
        string Index,
        bool Large,
        SweepHistoryInputs Inputs,
        SweepSeries[] Series,
        HeavyweightHistory Companies,
        IReadOnlyDictionary<string, IReadOnlyList<FiledIncome>> Income,
        DateOnly From,
        string Membership)
    {
        public string Ticker(int name) => Series[name].Name.Ticker;

        // A listing the index's rule keeps: every one on the S&P 500, and on the S&P 400 and 600 one clearing the floors and
        // the gate on its session.
        public bool Keeps(int name, int bar) =>
            Large || IndexSweepRunner.Clears(Index, Series[name], bar, Income.GetValueOrDefault(Ticker(name)) ?? []);
    }

    // The pullback's base in words, as its record and the tester name it.
    public static string PullbackWords { get; } =
        FormattableString.Invariant($"the pullback's base, its reward to risk at least {SweepIdeas.BaseRewardToRisk:0.##}, five a night, held on closes to {SweepIdeas.Cap} sessions");

    // The pullback's base, the ideas' run's replay of the live design at its reward to risk floor, five a night, held on
    // closes to its cap, each trade's result after its cost in multiples of its risk.
    static Replayed Pullback(Read read, IReadOnlyList<SweepMarketSeries> market, Action<string> progress)
    {
        var (replay, _) = SweepIdeasRunner.Read(read.Inputs, market, progress);
        var series = replay.Series;
        var rule = SweepIdeas.BaseRule with { PerNight = SetupFamilies.ListedANight };
        var trades = replay.Trades(rule, read.Large ? null : (name, bar) => IndexSweepRunner.Clears(read.Index, series[name], bar, read.Income.GetValueOrDefault(series[name].Name.Ticker) ?? []));
        var exit = SweepAxes.ExitIndex(SweepIdeas.Cap, breakEven: false);
        var kept = new List<RecordTrade>();
        var nights = Nights(read, trades.Select(trade => trade.Listing.Session));

        foreach (var trade in trades)
        {
            if (trade.Result is not { } result)
            {
                continue;
            }

            var listing = trade.Listing;
            var one = series[listing.Name];
            var entry = Statistic.FromPrice(one.Bars[listing.Bar].Close);
            var stop = entry - (listing.StopMoves * one.Atr[listing.Bar]);
            int held = listing.Plan.Ends[exit];

            kept.Add(new RecordTrade(
                result - IndexSweepRunner.CostInRisk(one, listing.Bar, entry, stop, result, read.Companies, 1),
                held,
                Worst(one, listing.Bar, held, entry, entry - stop),
                listing.Plan.Code[exit] == SweepPlanOutcomes.Win));
        }

        return new Replayed(
            SetupFamilies.Pullback,
            PullbackWords,
            rule.Key,
            read.From,
            read.Inputs.Through,
            read.Membership,
            Risks,
            kept,
            nights);
    }

    // How many trades a rule's walk listed on each scored session of the history, zeros included, read off the sessions
    // its trades were listed on.
    static IReadOnlyList<(DateOnly Session, int Listed)> Nights(Read read, IEnumerable<int> listedOn)
    {
        var calendar = read.Inputs.Sessions;
        var first = Array.FindIndex(calendar, session => session >= read.From);
        var counts = new int[calendar.Length];

        foreach (var session in listedOn)
        {
            if (session >= 0 && session < counts.Length)
            {
                counts[session]++;
            }
        }

        return [.. Enumerable.Range(Math.Max(0, first), calendar.Length - Math.Max(0, first)).Select(session => (calendar[session], counts[session]))];
    }

    // The breakout or the drift at the setting it was frozen at on the S&P 500, which the S&P 400 and 600 run as their
    // provisional rule, or at a registered rule's own setting: its sweep's own listings and exit, five a night with one
    // open trade a stock.
    static Replayed Swing(Read read, string family, IReadOnlyList<SweepColumns.Session> sessions, SweepBenchmark.Members members, int firstScored, int[]? at = null, double stopFloor = 0)
    {
        var calendar = read.Inputs.Sessions;
        var adapter = FamilySweepRunner.For(family, read.Series, sessions, members, firstScored, calendar, stopFloor);
        int[] setting = at ?? (family == BreakoutRule.Name ? [.. IndexNightRead.BreakoutAsFrozen] : [.. IndexNightRead.DriftAsFrozen]);
        var tickers = read.Series.Select(one => one.Name.Ticker).ToArray();
        var held = new Dictionary<(int Name, int Session), int>();

        (double? Result, int Sessions) Exit(FamilyListing listing)
        {
            var walked = adapter.Exit(listing);

            held[(listing.Name, listing.Session)] = walked.Sessions;

            return walked;
        }

        int YearOf(int session) => calendar[session].Year - SweepColumns.FirstScored.Year;

        var trades = FamilySweep.Walk(adapter.Listings(setting).Where(listing => read.Keeps(listing.Name, listing.Bar)), tickers, YearOf, Exit, adapter.Benchmark);
        var kept = new List<RecordTrade>();

        foreach (var trade in trades)
        {
            if (trade.Result is not { } result)
            {
                continue;
            }

            var listing = trade.Listing;
            var risk = listing.Entry - listing.Stop;
            var sessionsHeld = held[(listing.Name, listing.Session)];

            kept.Add(new RecordTrade(
                result - IndexSweepRunner.CostInRisk(read.Series[listing.Name], listing, result, read.Companies, 1),
                sessionsHeld,
                Worst(read.Series[listing.Name], listing.Bar, sessionsHeld, listing.Entry, risk),
                listing.Trails ? null : result >= ((listing.Target - listing.Entry) / risk) - Tolerance));
        }

        return new Replayed(family, SwingWords(family, setting), adapter.Grid.Key(setting), read.From, read.Inputs.Through, read.Membership, Risks, kept, Nights(read, trades.Select(trade => trade.Listing.Session)));
    }

    // The breakout's or the drift's setting in the words the register names a family rule in, read off the setting's own
    // dials, so the card names its rule as the run page does and never by the sweep's key.
    public static string SwingWords(string family, IReadOnlyList<int> setting)
    {
        double Dial(FamilyGrid grid, int dial) => grid.Value([.. setting], dial);

        return "the " + (family == BreakoutRule.Name
            ? TheSetupFamilies.Words(new BreakoutSettings((int)Dial(BreakoutSweep.Grid, 0), Dial(BreakoutSweep.Grid, 1), Dial(BreakoutSweep.Grid, 2), Dial(BreakoutSweep.Grid, 3)))
            : TheSetupFamilies.Words(new DriftSettings((int)Dial(DriftSweep.Grid, 0), Dial(DriftSweep.Grid, 1), Dial(DriftSweep.Grid, 2), Dial(DriftSweep.Grid, 3))));
    }

    // The sector heavyweights' setting in the register's words, its sector's return, its period and its exit read off the
    // sweep's own setting.
    public static string HeavyweightWords(HeavyweightSetting setting) =>
        "the " + TheSetupFamilies.Words(new HeavyweightSettings(
            setting.Largest,
            setting.LookBack,
            setting.Leaders,
            setting.HighBeta,
            FundReturn: setting.Sector == HeavyweightSectorReturn.Fund,
            Weekly: setting.Period == HeavyweightPeriod.Week,
            SoldOnLeading: setting.Exit != HeavyweightExit.Break,
            SoldUnderAverage: setting.Exit != HeavyweightExit.Drop));

    // The sector heavyweights' book at the setting frozen on the S&P 500, the S&P 400's and 600's reading each sector's
    // return from its members within the index after their floors and gate, every holding's result after its cost in per
    // cent of its buy; a holding still held at the history's end is in no record.
    static Replayed Heavyweights(Read read, SweepMarketSeries? fund, int first)
    {
        var calendar = read.Inputs.Sessions;
        var setting = read.Large ? HeavyweightSweep.Frozen : IndexHeavyweights.Provisional;
        var period = HeavyweightSweep.Rebalances(calendar, first, setting.Period);
        var (tape, laid) = HeavyweightSweep.Lay(read.Inputs, read.Companies, fund, period.ToHashSet(), first);
        var names = tape.Tickers.Select((ticker, at) => (ticker, at)).ToDictionary(pair => pair.ticker, pair => pair.at, StringComparer.Ordinal);

        int BarOf(int name, int session) => Array.BinarySearch(read.Series[name].SessionAt, session);

        var rebalances = period.ToDictionary(
            session => session,
            session =>
            {
                var one = laid[session];
                var kept = read.Large ? one : one with { Members = [.. one.Members.Where(member => BarOf(member.Name, session) is var bar && bar >= 0 && read.Keeps(member.Name, bar))] };

                return HeavyweightSweep.Read(kept, setting, names);
            });
        var trades = HeavyweightSweep.Walk(tape, rebalances, setting.Exit);
        var kept = new List<RecordTrade>();

        foreach (var trade in trades)
        {
            if (trade.Result is not { } result || trade.End is not { } end || BarOf(trade.Name, trade.Entry) is var buy && buy < 0 || BarOf(trade.Name, end) is var sale && sale < 0)
            {
                continue;
            }

            var bars = read.Series[trade.Name].Bars;
            var ticker = read.Ticker(trade.Name);
            var bought = bars[buy];
            var sold = bars[sale];
            var entry = bought.RawClose > 0m ? bought.RawClose : bought.Close;
            var exit = sold.RawClose > 0m ? sold.RawClose : sold.Close;
            var value = CompanyValue.On(new SessionClose(bought.Session, bought.Close, entry), read.Companies.Counts.GetValueOrDefault(ticker) ?? [], read.Companies.Splits.GetValueOrDefault(ticker) ?? []);
            var closes = tape.Close[trade.Name];
            var lowest = 0.0;

            for (var session = trade.Entry + 1; session <= end && session < closes.Length; session++)
            {
                if (closes[trade.Entry] > 0 && !double.IsNaN(closes[session]))
                {
                    lowest = Math.Min(lowest, ((closes[session] / closes[trade.Entry]) - 1) * 100);
                }
            }

            kept.Add(new RecordTrade(((result * 100) - TradeCost.InPercent(value, entry, exit)), end - trade.Entry, lowest, null));
        }

        return new Replayed(
            HeavyweightRule.Name,
            HeavyweightWords(setting),
            setting.Key,
            read.From,
            read.Inputs.Through,
            read.Membership,
            Percent,
            kept,
            []);
    }

    // How near a result may sit under its target's multiple and still be read as the close that reached it.
    const double Tolerance = 1e-9;

    // The lowest close a trade was held through, from the session after its buy to the one it ended on, in multiples of its
    // risk from its buy, and nothing where it never closed under its buy.
    static double Worst(SweepSeries series, int bar, int sessions, double entry, double risk)
    {
        if (!(risk > 0))
        {
            return 0;
        }

        var lowest = 0.0;

        for (var step = 1; step <= sessions && bar + step < series.Bars.Length; step++)
        {
            lowest = Math.Min(lowest, (Statistic.FromPrice(series.Bars[bar + step].Close) - entry) / risk);
        }

        return lowest;
    }
}
