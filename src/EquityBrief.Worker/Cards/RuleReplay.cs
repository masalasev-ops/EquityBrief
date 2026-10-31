using System.Collections.Concurrent;
using System.Globalization;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Cards;
using EquityBrief.Core.Families;
using EquityBrief.Core.Ledger;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Readings;
using EquityBrief.Core.Releases;
using EquityBrief.Core.Sweep;
using EquityBrief.Worker.Candidates;
using EquityBrief.Worker.Indices;
using EquityBrief.Worker.Loop;
using EquityBrief.Worker.Sweep;

namespace EquityBrief.Worker.Cards;

// One rule's replay for the record its card draws: the trades its one setting keeps over the pulled history with the
// sweep's own walk, five a night with one open trade a stock as the page lists them, each after its cost at the published
// table, with the sessions it was held and the lowest close it was held through. The S&P 500's rules apply no floors and
// no gate, and the S&P 400's and 600's apply their own; no setting is searched, so a replay costs one walk a rule. From
// 20.11 a family frozen on the S&P 400 or 600 is replayed at its live rule's own settings by its sweep's own reading of
// the index's history, every trade bought at the open after its listing and after its round trip as that sweep priced
// it, so the record is the frozen rule's own. From 21.14 every swing trade a close's signal ends is sold at the open after
// that close, as the walks the record reads sell it. It reads the store and writes nothing.
// see: A rule's record is replayed at its one setting by the sweep's own code over the pulled history, after costs on every index
// see: A swing trade sold on a close's signal is sold at the next session's open by the fill step, and no rule's version moves because of it
// see: A frozen rule on the S&P 400 or 600 is replayed at its own settings by its sweep's own code, and its card draws that record alone
public static class RuleReplay
{
    public const string Risks = "risks";

    public const string Percent = "percent";

    public const string AsItStood = "as it stood";

    public const string SurvivorsOnly = "survivors only";

    // One rule's replay: its family, the rule in words, the setting it was walked at, the history's first and last
    // session, whether membership was read as it stood or as today's members, the unit its results are in, its trades,
    // and how many it listed on each scored session, zeros included, which the cards' stretch lines read.
    public sealed record Replayed(string Family, string Rule, string Settings, DateOnly From, DateOnly Through, string Membership, string Unit, IReadOnlyList<RecordTrade> Trades, IReadOnlyList<(DateOnly Session, int Listed)> Nights)
    {
        // Why a family's live rule was not replayed, none where it was: a setting its registration states that its sweep's
        // own reading reads at no level, so no record is written for it.
        public string? Refused { get; init; }

        // The session each kept trade was listed on, in the order of its trades, and how many listings the walk did not buy
        // at the open, where it read an open floor.
        public IReadOnlyList<DateOnly> Listed { get; init; } = [];

        public int Skipped { get; init; }
    }

    // A registered breakout or drift rule walked in place of the family's frozen setting: its name as the register
    // holds it, its family, its place on the family's grid and the drift's stop floor in typical moves; and the reward to
    // risk the next session's open has to leave for a listing to be bought, nought where every open is bought.
    // see: The drift's open floor is replayed beside its live rule unregistered, skipping a fill whose reward to risk at the open is under 1
    public sealed record SwingVariant(string Name, string Family, int[] Setting, double StopFloor, double OpenFloor = 0);

    // The families a card names on an index, in the page's order with the sector heavyweights after them.
    public static IReadOnlyList<string> Families { get; } = [SetupFamilies.Pullback, BreakoutRule.Name, DriftRule.Name, HeavyweightRule.Name];

    // Every rule a card can name on an index, or the families named, each replayed over the history the index's sweep
    // reads; where variants are given for the breakout or the drift, each is walked at its own setting in place of the
    // family's frozen one; and on the S&P 400 or 600 each family whose live rule is among the rules given is replayed at
    // that rule's own settings by its sweep's own reading of the index's history, every other keeping its provisional
    // replay.
    public static async Task<IReadOnlyList<Replayed>> IndexAsync(string databaseFile, string index, Action<string> progress, CancellationToken cancellation, IReadOnlyList<string>? families = null, IReadOnlyList<SwingVariant>? variants = null, IReadOnlyList<IndexRule>? live = null)
    {
        bool Asked(string family) => families is null || families.Contains(family, StringComparer.Ordinal);

        var large = index == IndexFamilies.LargeIndex;

        // A family's live rule on the index, as the night reads its list by it; none on the S&P 500, whose families' records
        // follow their own settings.
        IndexRule? LiveOf(string family) => large ? null : (live ?? []).FirstOrDefault(rule => rule.Live && rule.Index == index && rule.Family == family);

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
        Frozen? frozen = null;

        // The history laid out as the sweeps on the index's own history lay it, with the S&P 500 as it stood beside it, read
        // once and only where a family's live rule is replayed.
        async Task<Frozen> FrozenAsync() =>
            frozen ??= new Frozen(IndexSweepRunner.OwnSwingRead.Laid(index, inputs, companies, income, through), await IndexSweepRunner.OwnSwingRead.LargeAsync(history, through, progress, cancellation), history, through);

        if (Asked(SetupFamilies.Pullback))
        {
            if (LiveOf(SetupFamilies.Pullback) is { } rule)
            {
                progress($"replaying the {DecisionCards.NameOf(index)}'s live pullback rule at its own settings by its sweep's own reading");
                replayed.Add(await FrozenPullbackAsync(read, await FrozenAsync(), rule, progress, cancellation));
            }
            else
            {
                progress($"replaying the pullback on the {DecisionCards.NameOf(index)}");
                replayed.Add(Pullback(read, market, progress));
            }
        }

        var swing = new[] { BreakoutRule.Name, DriftRule.Name }.Where(Asked).ToArray();

        if (swing.Any(family => LiveOf(family) is null))
        {
            progress($"replaying the breakout and the drift on the {DecisionCards.NameOf(index)}");
        }

        IReadOnlyList<SweepColumns.Session>? sessions = null;
        SweepBenchmark.Members? members = null;

        foreach (var family in swing)
        {
            if (LiveOf(family) is { } rule)
            {
                progress($"replaying the {DecisionCards.NameOf(index)}'s live {family} rule at its own settings by its sweep's own reading");
                replayed.Add(await FrozenSwingAsync(read, await FrozenAsync(), rule, cancellation));

                continue;
            }

            sessions ??= SweepColumns.Sessions(series, calendar);
            members ??= SweepBenchmark.On(series, calendar.Length);

            var own = (variants ?? []).Where(variant => variant.Family == family).ToArray();

            if (own.Length == 0)
            {
                replayed.Add(Swing(read, family, sessions, members, firstScored));
            }

            foreach (var variant in own)
            {
                progress($"replaying {variant.Name} on the {DecisionCards.NameOf(index)}");
                replayed.Add(Swing(read, family, sessions, members, firstScored, variant.Setting, variant.StopFloor, variant.OpenFloor) with { Rule = variant.Name });
            }
        }

        if (Asked(HeavyweightRule.Name))
        {
            if (LiveOf(HeavyweightRule.Name) is { } rule)
            {
                progress($"replaying the {DecisionCards.NameOf(index)}'s live sector heavyweights rule at its own settings by its sweep's own reading");
                replayed.Add(await FrozenBookAsync(read, await FrozenAsync(), rule, progress, cancellation));
            }
            else
            {
                progress($"replaying the sector heavyweights on the {DecisionCards.NameOf(index)}");
                replayed.Add(Heavyweights(read, fund, firstScored));
            }
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

    // What every frozen rule of an index is replayed over: the history laid out as its sweeps on the index's own history lay
    // it, the S&P 500 as it stood beside it, and the store's history and its last session, which their further series are
    // read through.
    sealed record Frozen(LoopRead Laid, SweepHistoryInputs Large, SweepHistory History, DateOnly Through);

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

            if (OpenAfter(one, listing.Bar) is not { } open
                || OpenFills.Pullback(one, TradeFills.AtOpen(result, entry, entry - stop, open), listing.Bar, listing.Plan, exit, entry - stop) is not { } atOpen)
            {
                continue;
            }

            kept.Add(new RecordTrade(
                atOpen - IndexSweepRunner.CostInRisk(one, listing.Bar, entry, stop, result, read.Companies, 1),
                held,
                Worst(one, listing.Bar, held, open, entry - stop),
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
    static Replayed Swing(Read read, string family, IReadOnlyList<SweepColumns.Session> sessions, SweepBenchmark.Members members, int firstScored, int[]? at = null, double stopFloor = 0, double openFloor = 0)
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

        var listings = adapter.Listings(setting).Where(listing => read.Keeps(listing.Name, listing.Bar));
        var skipped = 0;
        var trades = openFloor > 0
            ? WalkSkippingAtTheOpen(listings, tickers, YearOf, Exit, adapter.Benchmark, listing => LeavesUnder(OpenAfter(read.Series[listing.Name], listing.Bar), listing, openFloor), () => skipped++)
            : FamilySweep.Walk(listings, tickers, YearOf, Exit, adapter.Benchmark);
        var kept = new List<RecordTrade>();
        var listed = new List<DateOnly>();

        foreach (var trade in trades)
        {
            if (trade.Result is not { } result)
            {
                continue;
            }

            var listing = trade.Listing;
            var risk = listing.Entry - listing.Stop;
            var sessionsHeld = held[(listing.Name, listing.Session)];

            if (OpenAfter(read.Series[listing.Name], listing.Bar) is not { } open
                || OpenFills.Sold(read.Series[listing.Name], (TradeFills.AtOpen(result, listing.Entry, risk, open), sessionsHeld), listing).Result is not { } atOpen)
            {
                continue;
            }

            kept.Add(new RecordTrade(
                atOpen - IndexSweepRunner.CostInRisk(read.Series[listing.Name], listing, result, read.Companies, 1),
                sessionsHeld,
                Worst(read.Series[listing.Name], listing.Bar, sessionsHeld, open, risk),
                listing.Trails ? null : result >= ((listing.Target - listing.Entry) / risk) - Tolerance));
            listed.Add(calendar[listing.Session]);
        }

        return new Replayed(family, SwingWords(family, setting), adapter.Grid.Key(setting), read.From, read.Inputs.Through, read.Membership, Risks, kept, Nights(read, trades.Select(trade => trade.Listing.Session)))
        {
            Listed = listed,
            Skipped = skipped,
        };
    }

    // Whether a listing's next open leaves less reward than the floor's multiple of its risk, the target's distance above the
    // open over the stop's below it, or opens at or past its stop; a listing whose next open is not held is not judged, and
    // a plan whose stop trails and names no target is judged by its stop alone.
    // see: The drift's open floor is replayed beside its live rule unregistered, skipping a fill whose reward to risk at the open is under 1
    public static bool LeavesUnder(double? open, FamilyListing listing, double floor) =>
        open is { } price && (price <= listing.Stop || (!listing.Trails && (listing.Target - price) / (price - listing.Stop) < floor));

    // The family's walk with one change: a listing the night's list kept whose next open the floor skips is not bought, its
    // place on the night's list spent, since the list is chosen at the close and no listing after it moves up, and its stock
    // free for the rule's later listings, since no trade holds it. Every other step is the family's own walk.
    // see: The drift's open floor is replayed beside its live rule unregistered, skipping a fill whose reward to risk at the open is under 1
    public static List<FamilyTrade> WalkSkippingAtTheOpen(
        IEnumerable<FamilyListing> listings,
        IReadOnlyList<string> tickers,
        Func<int, int> yearOf,
        Func<FamilyListing, (double? Result, int Sessions)> exit,
        Func<FamilyListing, double> benchmark,
        Func<FamilyListing, bool> skips,
        Action? skipped = null,
        int perNight = FamilySweep.PerNight)
    {
        var kept = new List<FamilyTrade>();
        var openUntil = new Dictionary<int, int>();

        foreach (var night in listings.GroupBy(listing => listing.Session).OrderBy(group => group.Key))
        {
            var taken = 0;

            foreach (var listing in night
                .OrderByDescending(one => one.Order)
                .ThenByDescending(one => one.ThenBy)
                .ThenBy(one => tickers[one.Name], StringComparer.Ordinal))
            {
                if (taken == perNight)
                {
                    break;
                }

                if (openUntil.TryGetValue(listing.Name, out var held) && listing.Session <= held)
                {
                    continue;
                }

                taken++;

                if (skips(listing))
                {
                    skipped?.Invoke();

                    continue;
                }

                var (result, sessions) = exit(listing);

                openUntil[listing.Name] = listing.Session + sessions;
                kept.Add(new FamilyTrade(listing, yearOf(listing.Session), result, result is null ? double.NaN : benchmark(listing)));
            }
        }

        return kept;
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

    // The words an S&P 400's or 600's family's provisional rule is replayed under, which a card its night drew by that rule
    // reads its stored record by; none for a family the replay keeps no record of.
    public static string? ProvisionalWords(string family) => family switch
    {
        SetupFamilies.Pullback => PullbackWords,
        BreakoutRule.Name => SwingWords(family, IndexNightRead.BreakoutAsFrozen),
        DriftRule.Name => SwingWords(family, IndexNightRead.DriftAsFrozen),
        HeavyweightRule.Name => HeavyweightWords(IndexHeavyweights.Provisional),
        _ => null,
    };

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
            var close = Statistic.FromPrice(bought.Close);

            // From 19.10 the holding is bought at the open of the session after its rebalance, its result a fraction of
            // the sum the rebalance's close put to it.
            if (OpenAfter(read.Series[trade.Name], buy) is not { } open || TradeFills.AtOpen(result, close, close, open) is not { } atOpen)
            {
                continue;
            }

            for (var session = trade.Entry + 1; session <= end && session < closes.Length; session++)
            {
                if (closes[trade.Entry] > 0 && !double.IsNaN(closes[session]))
                {
                    lowest = Math.Min(lowest, ((closes[session] * close / closes[trade.Entry] / open) - 1) * 100);
                }
            }

            kept.Add(new RecordTrade(((atOpen * 100) - TradeCost.InPercent(value, entry, exit)), end - trade.Entry, lowest, null));
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

    // A frozen breakout's or drift's settings as its sweep on the index's own history reads them: its place on the family's
    // grid with the market check's level as the grid's last dial, the second stage's levels it states, and the key the
    // sweep names that setting by.
    public sealed record SwingAt(int[] Setting, IReadOnlyList<DialLevel<FamilyListing>> Levels, string Key);

    // A frozen pullback's settings as its search on the index's own history reads them: its place on the search's own grid,
    // its point in the search's space, the second stage's levels it states, and the key the search names it by.
    public sealed record PullbackAt(DialSetting Setting, int[] Point, IReadOnlyList<DialLevel<SweepCandidate>> Levels, string Key);

    // A frozen sector heavyweights book's settings as its design's sweep at the next open reads them: design (a)'s setting,
    // or design (b)'s window, industries and members an industry, with the quality and the multiple of the dollar volume
    // floor, and the key the sweep names it by.
    public sealed record BookAt(HeavyweightSetting? Setting, int Window, int Industries, int Members, IndexQuality Quality, decimal Floors, string Key);

    // A registered breakout's or drift's settings on its sweep's own grid, or why the sweep reads no such setting: a dial
    // off the family's grid, a market check's floor none of the sweep's levels reads, or a level the second stage did not
    // read.
    public static (SwingAt? At, string? Refused) OwnSwing(IndexSweepRunner.OwnSwingRead own, IndexRuleCandidate evaluator, IReadOnlyDictionary<string, double> p)
    {
        double[] values = evaluator switch
        {
            IndexBreakoutCandidate =>
            [
                p[IndexBreakoutCandidate.HighSessionsParameter],
                p[IndexBreakoutCandidate.VolumeMultipleParameter],
                p[IndexBreakoutCandidate.RangeCeilingParameter] == 0 ? double.PositiveInfinity : p[IndexBreakoutCandidate.RangeCeilingParameter],
                p[IndexBreakoutCandidate.StopMovesParameter],
            ],
            IndexDriftCandidate =>
            [
                p[IndexDriftCandidate.WindowSessionsParameter],
                p[IndexDriftCandidate.ReactionMovesParameter],
                p[IndexDriftCandidate.VolumeMultipleParameter],
                p[IndexDriftCandidate.TargetMultipleParameter],
            ],
            _ => [],
        };
        var places = IndexNightRead.Places(own.Adapter.Grid, values);

        if (values.Length == 0 || places.Any(place => place < 0))
        {
            return (null, "its dials name a setting its sweep's grid does not hold");
        }

        if (MarketLevelOf(p) is not { } market)
        {
            return (null, "its market check reads a floor none of its sweep's levels reads");
        }

        var (levels, refused) = LevelsOf(own.Levels, evaluator, p);

        if (levels is null)
        {
            return (null, refused);
        }

        int[] setting = [.. places, market];

        return (new SwingAt(setting, levels, OwnProcedures.KeyOf(own.Grid, setting, levels)), null);
    }

    // The market check's level among the seven its sweep on the index's own history reads, in the order that sweep lays
    // them: none first, then the index's own breadth at each of its four floors, then the S&P 500's at its own floor; none
    // where the floor stated is not one of them.
    // see: Each frozen rule on the S&P 400 and 600 reads its market check at the level its own sweep chose
    public static int? MarketLevelOf(IReadOnlyDictionary<string, double> p)
    {
        var breadth = p.GetValueOrDefault(IndexRuleCandidate.LargeBreadthParameter);

        if (breadth == IndexRuleCandidate.NoMarketCheck)
        {
            return 0;
        }

        if (breadth == 1)
        {
            return 1 + IndexSweepRunner.OwnBreadthFloors.Count;
        }

        var place = IndexSweepRunner.OwnBreadthFloors.ToList().IndexOf(p.TryGetValue(IndexRuleCandidate.BreadthFloorParameter, out var floor) ? floor : FamilySweep.MarketFloor);

        return place < 0 ? null : 1 + place;
    }

    // A registered pullback's settings on its search's own grid, its market check the search's own dial at the floor of the
    // index's own breadth it states, off where it reads none or the S&P 500's, which the second stage reads as a level; or
    // why the search reads no such setting.
    public static (PullbackAt? At, string? Refused) OwnPullback(IndexSweepRunner.OwnPullbackRead own, IndexRuleCandidate evaluator, IReadOnlyDictionary<string, double> p)
    {
        var grid = own.Grid;
        var breadth = p.GetValueOrDefault(IndexRuleCandidate.LargeBreadthParameter);
        var dial = p[IndexPullbackCandidate.MarketParameter];
        var market = breadth == IndexRuleCandidate.NoMarketCheck || breadth == 1 ? SweepGrid.MarketOff
            : dial != 0 ? dial
            : p.TryGetValue(IndexRuleCandidate.BreadthFloorParameter, out var floor) ? floor : FamilySweep.MarketFloor;
        int[] at =
        [
            SweepGrid.IndexOf(grid.StrengthBars, p[IndexPullbackCandidate.StrengthParameter]),
            SweepGrid.IndexOf(grid.DepthLows, p[IndexPullbackCandidate.DepthLowParameter]),
            SweepGrid.IndexOf(grid.DepthHighs, p[IndexPullbackCandidate.DepthHighParameter]),
            SweepGrid.IndexOf(grid.DryUpCeilings, p[IndexPullbackCandidate.DryUpParameter] == 0 ? SweepGrid.Off : p[IndexPullbackCandidate.DryUpParameter]),
            grid.Freshness.ToList().FindIndex(level => level == p[IndexPullbackCandidate.FreshnessParameter]),
            SweepGrid.IndexOf(grid.RewardToRiskFloors, p[IndexPullbackCandidate.RewardToRiskParameter]),
            grid.StopBounds.ToList().FindIndex(bounds => bounds.Low == p[IndexPullbackCandidate.StopLowParameter] && bounds.High == p[IndexPullbackCandidate.StopHighParameter]),
            SweepGrid.IndexOf(grid.MarketFloors, market),
            grid.BandStrengths.ToList().FindIndex(level => level == p[IndexPullbackCandidate.BandParameter]),
        ];

        if (at.Any(place => place < 0))
        {
            return (null, "its dials name a setting its search's grid does not hold");
        }

        var (levels, refused) = LevelsOf(own.Levels, evaluator, p, breadth == 1 ? own.LargeLevel : null);

        if (levels is null)
        {
            return (null, refused);
        }

        var setting = new DialSetting(at[0], at[1], at[2], at[3], at[4], at[5], at[6], at[7], at[8]);
        var point = own.Space.Point(setting, ConditionSetting.Off);
        var key = own.Space.Describe(point) + (levels.Count == 0 ? string.Empty : " with " + string.Join("; ", levels.Select(level => level.Dial + ", " + level.Name)));

        return (new PullbackAt(setting, point, levels, key), null);
    }

    // A registered heavyweights book's settings as its design's sweep reads them, or why it reads none: design (a) reading
    // twelve months' strength, which its sweep at the next open read at no setting.
    public static (BookAt? At, string? Refused) OwnBook(IReadOnlyDictionary<string, double> p)
    {
        var quality = p[IndexRuleCandidate.QualityParameter] switch
        {
            0 => IndexQuality.Off,
            2 => IndexQuality.Cover,
            _ => IndexQuality.Profit,
        };
        var floors = p[IndexRuleCandidate.FloorsParameter] == 2 ? 2m : 1m;

        if (p[IndexHeavyweightCandidate.DesignParameter] == IndexHeavyweightCandidate.DesignA)
        {
            if (IndexRules.TwelveOne(p))
            {
                return (null, "it reads twelve months' strength skipping the latest month, which its design's sweep at the next open read at no setting");
            }

            var setting = IndexRules.HeavyweightSettingOf(p);

            return (new BookAt(setting, 0, 0, 0, quality, floors, IndexSweepRunner.VariantKey(quality, floors) + setting.Key), null);
        }

        var window = (int)p[IndexHeavyweightCandidate.WindowParameter];
        var industries = p[IndexHeavyweightCandidate.IndustriesParameter] == 0 ? IndexSweepRunner.EveryLeading : (int)p[IndexHeavyweightCandidate.IndustriesParameter];
        var members = (int)p[IndexHeavyweightCandidate.MembersParameter];

        return (new BookAt(null, window, industries, members, quality, floors, IndexSweepRunner.FollowersKey(window, industries, members, quality, floors)), null);
    }

    // The second stage's levels a registration states, each found among its sweep's own: a quality, a multiple of the dollar
    // volume floor, a hold or a window by the figure it sets, every other by its dial's name and level, and the S&P 500's
    // breadth where it is handed in; none where one it states is not among them exactly once, or where it states a market
    // switch, a breakout's own level or a reading version, which no sweep on the index's own history reads, saying which.
    // The levels are kept in the order the second stage reads them, which is the order its key names a crossing in.
    static (IReadOnlyList<DialLevel<T>>? Levels, string? Refused) LevelsOf<T>(IReadOnlyList<DialLevel<T>> offered, IndexRuleCandidate evaluator, IReadOnlyDictionary<string, double> p, DialLevel<T>? also = null)
    {
        double At(string name) => p.GetValueOrDefault(name);

        foreach (var name in new[] { IndexRuleCandidate.FundOverSpyParameter, IndexRuleCandidate.CreditAverageParameter, IndexRuleCandidate.CreditChangeParameter, IndexBreakoutCandidate.NearnessParameter, IndexBreakoutCandidate.RecencyParameter, IndexBreakoutCandidate.HighVolumeParameter })
        {
            if (At(name) != 0)
            {
                return (null, $"its {name} at {Number(At(name))} is a level its replay reads none of");
            }
        }

        if (ReleaseTone.VersionOf(p) > 0)
        {
            return (null, "it reads a release reading, which its sweep on the index's own history reads none of");
        }

        var wanted = new List<(string Stated, Func<DialLevel<T>, bool> Is)>();
        decimal? floors = At(IndexRuleCandidate.FloorsParameter) switch
        {
            0.5 => IndexSweepRunner.DollarVolumeHalf,
            2 => 2m,
            _ => null,
        };
        IndexQuality? quality = At(IndexRuleCandidate.QualityParameter) switch
        {
            0 => IndexQuality.Off,
            2 => IndexQuality.Cover,
            _ => null,
        };

        if (floors is { } multiple)
        {
            wanted.Add(($"{IndexRuleCandidate.FloorsParameter} at {Number(At(IndexRuleCandidate.FloorsParameter))}", level => level.Floors == multiple));
        }

        if (At(IndexRuleCandidate.LowestCloseParameter) == Statistic.FromPrice(IndexSweepRunner.HigherPrice))
        {
            wanted.Add(($"{IndexRuleCandidate.LowestCloseParameter} at {Number(At(IndexRuleCandidate.LowestCloseParameter))}", level => level.Dial == "minimum price"));
        }

        if (quality is { } held)
        {
            wanted.Add(($"{IndexRuleCandidate.QualityParameter} at {Number(At(IndexRuleCandidate.QualityParameter))}", level => level.Quality == held));
        }

        if (At(IndexRuleCandidate.HoldParameter) is var hold and > 0)
        {
            wanted.Add(($"{IndexRuleCandidate.HoldParameter} at {Number(hold)}", level => level.Hold == (int)hold));
        }

        if (At(IndexRuleCandidate.IndustryFallParameter) is var fall and > 0)
        {
            wanted.Add(($"{IndexRuleCandidate.IndustryFallParameter} at {Number(fall)}", level => level.Dial == "industry fall" && level.Name == FormattableString.Invariant($"{fall} sessions")));
        }

        if (evaluator is IndexDriftCandidate)
        {
            if (At(IndexDriftCandidate.WideWindowParameter) is var window and > 0)
            {
                wanted.Add(($"{IndexDriftCandidate.WideWindowParameter} at {Number(window)}", level => level.Window == (int)window));
            }

            if (At(IndexDriftCandidate.PeersParameter) == 1)
            {
                wanted.Add(($"{IndexDriftCandidate.PeersParameter} at 1", level => level.Dial == "peer version"));
            }

            if (At(IndexDriftCandidate.EntryParameter) == 1)
            {
                wanted.Add(($"{IndexDriftCandidate.EntryParameter} at 1", level => level.Dial == "entry"));
            }

            if (At(IndexDriftCandidate.RevenueGrewParameter) == 1)
            {
                wanted.Add(($"{IndexDriftCandidate.RevenueGrewParameter} at 1", level => level.Dial == "revenue"));
            }
        }

        if (also is not null)
        {
            wanted.Add(($"{IndexRuleCandidate.LargeBreadthParameter} at 1", level => ReferenceEquals(level, also)));
        }

        var found = new List<DialLevel<T>>();

        foreach (var (stated, isIt) in wanted)
        {
            var matching = offered.Where(isIt).ToArray();

            if (matching.Length != 1)
            {
                return (null, $"its {stated} is no level its sweep's second stage read");
            }

            found.Add(matching[0]);
        }

        return ([.. offered.Where(level => found.Any(one => ReferenceEquals(one, level)))], null);
    }

    // A frozen breakout or drift replayed as its sweep on the index's own history reads it: the setting its registration
    // states with the market check's level it reads, the drift's stop floor where it states one, and the second stage's
    // levels it states read together as the sweep reads a crossing; five a night with one open trade a stock, every trade
    // bought at the open after its listing and after its round trip at the published table.
    static async Task<Replayed> FrozenSwingAsync(Read read, Frozen frozen, IndexRule rule, CancellationToken cancellation)
    {
        var p = rule.Parameters;
        var words = IndexRules.Words(rule.Evaluator, p);
        var stopFloor = rule.Evaluator is IndexDriftCandidate ? p[IndexDriftCandidate.StopFloorParameter] : 0;
        var own = await IndexSweepRunner.OwnSwingRead.ReadAsync(frozen.Laid, frozen.Large, rule.Family, frozen.History, frozen.Through, stopFloor, cancellation);
        var (at, refused) = OwnSwing(own, rule.Evaluator, p);

        if (at is null)
        {
            return Refusal(read, rule.Family, words, refused!);
        }

        var held = new ConcurrentDictionary<(int Name, int Session), int>();
        var trades = own.TradesUnder(at.Setting, at.Levels, held);
        var kept = new List<RecordTrade>();

        foreach (var trade in trades)
        {
            var listing = trade.Listing;
            var one = own.Series[listing.Name];
            var risk = listing.Entry - listing.Stop;

            if (trade.Result is not { } result || OpenAfter(one, listing.Bar) is not { } open)
            {
                continue;
            }

            var sessions = held[(listing.Name, listing.Session)];

            // Whether it ended at its target is read off its walk at the close, where its target's multiple stands, the
            // walk the sweep's own adapter makes.
            kept.Add(new RecordTrade(
                result,
                sessions,
                Worst(one, listing.Bar, sessions, open, risk),
                listing.Trails ? null : own.Adapter.Exit(listing).Result >= ((listing.Target - listing.Entry) / risk) - Tolerance));
        }

        return new Replayed(rule.Family, words, at.Key, read.From, read.Inputs.Through, read.Membership, Risks, kept, Nights(read, trades.Select(trade => trade.Listing.Session)));
    }

    // A frozen pullback replayed as its search on the index's own history reads it: every candidate's plan bought at the
    // open after its listing and charged its round trip, the setting its registration states on the search's own grid with
    // the levels it states, the profit and cover check among them, and the stages' own walk, one open trade a stock.
    static async Task<Replayed> FrozenPullbackAsync(Read read, Frozen frozen, IndexRule rule, Action<string> progress, CancellationToken cancellation)
    {
        var words = IndexRules.Words(rule.Evaluator, rule.Parameters);
        var own = await IndexSweepRunner.OwnPullbackRead.ReadAsync(frozen.Laid, frozen.Large, frozen.History, frozen.Through, progress, cancellation);
        var (at, refused) = OwnPullback(own, rule.Evaluator, rule.Parameters);

        if (at is null)
        {
            return Refusal(read, SetupFamilies.Pullback, words, refused!);
        }

        own.Charge(1);

        var (design, picks) = own.Under(at.Levels);
        var setting = own.SettingUnder(at.Point, at.Levels);
        var walked = new HashSet<(int Name, int Session)>();

        SweepStages.Measures(picks, design, setting, own.Space.Conditions(at.Point), own.Nights, walked);

        var exit = design.ExitIndex;
        var floor = SweepStages.StopFloor(setting);
        var kept = new List<RecordTrade>();
        var listed = new List<int>();

        foreach (var pick in picks)
        {
            if (!walked.Remove((pick.Name, pick.Session)))
            {
                continue;
            }

            listed.Add(pick.Session);

            var plan = pick.Plan;

            if (pick.Year is < 0 or >= SweepFigures.Years
                || plan.Code[exit] is not (SweepPlanOutcomes.Win or SweepPlanOutcomes.Loss or SweepPlanOutcomes.Unresolved)
                || float.IsNaN(plan.Multiple[exit])
                || !plan.IsATradeAt(exit, floor))
            {
                continue;
            }

            var one = frozen.Laid.Series[pick.Name];
            var bar = Array.BinarySearch(one.SessionAt, pick.Session);

            if (bar < 0 || OpenAfter(one, bar) is not { } open)
            {
                continue;
            }

            int sessions = plan.Ends[exit];

            kept.Add(new RecordTrade(plan.Multiple[exit], sessions, Worst(one, bar, sessions, open, plan.StopMoves * one.Atr[bar]), plan.Code[exit] == SweepPlanOutcomes.Win));
        }

        return new Replayed(SetupFamilies.Pullback, words, at.Key, read.From, read.Inputs.Through, read.Membership, Risks, kept, Nights(read, listed));
    }

    // A frozen sector heavyweights book replayed as its design's sweep at the next open reads it: its rebalances over the
    // members clearing the quality and the floors it states, its holdings walked under its exit, each bought at the open
    // after its rebalance and after its round trip, its result in per cent of its buy; a holding still held at the
    // history's end is in no record.
    static async Task<Replayed> FrozenBookAsync(Read read, Frozen frozen, IndexRule rule, Action<string> progress, CancellationToken cancellation)
    {
        var words = IndexRules.Words(rule.Evaluator, rule.Parameters);
        var (at, refused) = OwnBook(rule.Parameters);

        if (at is null)
        {
            return Refusal(read, HeavyweightRule.Name, words, refused!);
        }

        var lay = await HeavyweightLay.ReadAsync(frozen.Laid, frozen.History, frozen.Through, progress, cancellation, frozen.Large);
        var book = at.Setting is { } setting
            ? lay.BookFrom(lay.RebalancesA(setting, at.Quality, at.Floors), setting.Exit, at.Key)
            : lay.BookFrom(lay.RebalancesB(at.Window, at.Industries, at.Members, at.Quality, at.Floors), HeavyweightExit.Drop, at.Key);
        var kept = new List<RecordTrade>();

        foreach (var trade in book.Costed)
        {
            var one = frozen.Laid.Series[trade.Name];
            var buy = Array.BinarySearch(one.SessionAt, trade.Entry);

            if (trade.Result is not { } result || trade.End is not { } end || buy < 0 || OpenAfter(one, buy) is not { } open)
            {
                continue;
            }

            var lowest = 0.0;

            for (var bar = buy + 1; bar < one.Bars.Length && one.SessionAt[bar] <= end; bar++)
            {
                lowest = Math.Min(lowest, ((Statistic.FromPrice(one.Bars[bar].Close) / open) - 1) * 100);
            }

            kept.Add(new RecordTrade(result * 100, end - trade.Entry, lowest, null));
        }

        return new Replayed(HeavyweightRule.Name, words, at.Key, read.From, read.Inputs.Through, read.Membership, Percent, kept, []);
    }

    // A family's live rule the replay cannot read, in its words with why and no trade.
    static Replayed Refusal(Read read, string family, string words, string why) =>
        new(family, words, string.Empty, read.From, read.Inputs.Through, read.Membership, family == HeavyweightRule.Name ? Percent : Risks, [], []) { Refused = why };

    static string Number(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    // How near a result may sit under its target's multiple and still be read as the close that reached it.
    const double Tolerance = 1e-9;

    // The open of the session after a bar, which a trade is bought at from 19.10, none where the series holds none.
    // see: A trade's entry is priced at the next session's open by a fill step of its own, and no rule's version moves because of it
    static double? OpenAfter(SweepSeries series, int bar) =>
        bar >= 0 && bar + 1 < series.Bars.Length && series.Bars[bar + 1].Open > 0m ? Statistic.FromPrice(series.Bars[bar + 1].Open) : null;

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
