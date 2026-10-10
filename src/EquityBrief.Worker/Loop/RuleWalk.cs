using EquityBrief.Core.Families;
using EquityBrief.Core.Ledger;
using EquityBrief.Core.Loop;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Sweep;
using EquityBrief.Worker.Cards;
using EquityBrief.Worker.Indices;
using EquityBrief.Worker.Sweep;

namespace EquityBrief.Worker.Loop;

// One listing a rule made over the history as the engines read it: the stock and its bar, the session, the session
// its trade would have ended on under the exit the rule stands at, and its edge after its round trip had it been taken,
// each none where the history has not reached its end.
public sealed record RuleListing(int Name, int Bar, int Session, int? Exit, double? Edge);

// One family's rule on an index read once for every engine: its words, its closes, the sessions a trade is given,
// every listing it makes over the whole history, and its walk, five a night with one open trade a stock, over every
// listing or over those a filter keeps, each night's listings kept and ordered by the hooks the rule stands at with a
// proposal's set on top of them as an approval sets them, under the exit either names or the rule's own, each trade
// after its round trip and against the same plan under the same exit on every member that session. A rule standing at
// its family's own setting states no hook and is walked as it always was; one an approval changed on the S&P 400 or
// 600 is walked at the setting and the hooks the approval stored.
// see: The trade autopsy proposes exits of a fixed menu, each tested as the procedure that chose it
// see: Winners against losers proposes a condition only where it beats a within-night shuffle of its own search
// see: A fitted statistical model is a rule
// see: An approved change is applied before the next night from the night's own build, on the index it was approved on alone
public sealed class RuleWalk(
    string family,
    string current,
    double[][] closes,
    int cap,
    IReadOnlyList<RuleListing> listings,
    Func<ExitChoice?, Func<int, bool>?, Func<IReadOnlyList<int>, IReadOnlyList<int>>?, IReadOnlyList<ExitProcedures.Walked>> walk,
    IReadOnlyDictionary<string, double>? standing = null,
    Func<int, IReadOnlyList<double?>>? readingsOf = null)
{
    IReadOnlyList<ExitProcedures.Walked>? own;

    public string Family => family;

    public string Current => current;

    public double[][] Closes => closes;

    public int Cap => cap;

    public IReadOnlyList<RuleListing> Listings => listings;

    // The hooks the rule stands at, none where it stands at its family's own setting.
    public IReadOnlyDictionary<string, double> Standing => standing ?? LoopChange.NoHooks;

    public IReadOnlyList<ExitProcedures.Walked> Own => own ??= Walk(LoopChange.NoHooks, null, readingsOf);

    public IReadOnlyList<ExitProcedures.Walked> Under(ExitChoice exit) =>
        Walk(new Dictionary<string, double>(StringComparer.Ordinal) { [RuleHooks.ExitParameter] = exit.Number }, null, readingsOf);

    // The rule's walk over the listings a filter keeps, by each listing's place in the listings.
    public IReadOnlyList<ExitProcedures.Walked> Keeping(Func<int, bool> keep) => Walk(LoopChange.NoHooks, keep, readingsOf);

    // The rule's walk with a proposal's hooks set on top of those it stands at, as an approval sets them: each night's
    // listings kept and ordered by them exactly as the night keeps and orders a hooked rule's candidates, a listing's
    // readings read by its place.
    public IReadOnlyList<ExitProcedures.Walked> Hooked(IReadOnlyDictionary<string, double> change, Func<int, IReadOnlyList<double?>> readings) =>
        Walk(change, null, readings);

    // The hooks a walk reads: a change's on top of the rule's own, through the one composition an approval applies.
    public static RuleHooks Composed(IReadOnlyDictionary<string, double> change, IReadOnlyDictionary<string, double> standing) =>
        RuleHooks.Of(LoopChange.OfHooks(change).OnTopOf(new LoopChange(null, null, null, standing)).Hooks);

    IReadOnlyList<ExitProcedures.Walked> Walk(IReadOnlyDictionary<string, double> change, Func<int, bool>? keep, Func<int, IReadOnlyList<double?>>? readings)
    {
        var hooks = Composed(change, Standing);

        if (hooks.ReadsReadings && readings is null)
        {
            throw new InvalidOperationException($"The {family} stands at hooks reading the catalogue's readings, and the walk was handed none.");
        }

        return walk(hooks.ExitChoice, keep, hooks.ReadsReadings ? night => hooks.Order(night, at => readings!(at)) : null);
    }

    static double[][] ClosesOf(IReadOnlyList<SweepSeries> series) =>
        [.. series.Select(one => one.Bars.Select(bar => Statistic.FromPrice(bar.Close)).ToArray())];

    // The setting a family stands at in words: its own words with the hooks an approval set, where it set any.
    static string WordsOf(string words, IReadOnlyDictionary<string, double> hooks) =>
        RuleHooks.Of(hooks).Words() is { } hooked ? words + "; " + hooked : words;

    // The breakout or the drift at the setting it froze at on the S&P 500, which the S&P 400 and 600 run provisionally,
    // or at the setting an approval stored for it there: its sweep's own listings and exit, each listing kept only where
    // it clears the index's floors and gate, and on the S&P 500 every listing.
    public static RuleWalk Swing(LoopRead read, string family, bool large, LoopChange? standing = null, LoopReadings? readings = null)
    {
        var floor = standing?.StopFloor ?? 0;
        var adapter = floor > 0
            ? FamilySweepRunner.For(family, read.Series, read.Sessions, read.Members, read.FirstScored, read.Calendar, floor)
            : FamilySweepRunner.For(family, read.Series, read.Sessions, read.Members, read.FirstScored, read.Calendar);
        int[] setting = standing?.Places is { } places ? [.. places] : family == BreakoutRule.Name ? [.. IndexNightRead.BreakoutAsFrozen] : [.. IndexNightRead.DriftAsFrozen];
        var hooks = standing?.Hooks ?? LoopChange.NoHooks;
        var exit = RuleHooks.Of(hooks).ExitChoice;
        var closes = ClosesOf(read.Series);
        var tickers = read.Series.Select(one => one.Name.Ticker).ToArray();
        FamilyListing[] listed =
        [
            .. adapter.Listings(setting).Where(listing => listing.Move > 0
                && (large || IndexSweepRunner.Clears(read.Index, read.Series[listing.Name], listing.Bar, read.Income.GetValueOrDefault(tickers[listing.Name]) ?? []))),
        ];

        RuleListing Unit(FamilyListing listing)
        {
            var (result, sessions) = exit is null ? adapter.Exit(listing) : Menu(listing);
            var benchmark = result is null ? double.NaN : exit is null ? adapter.Benchmark(listing) : ExitProcedures.MenuBenchmark(read, closes, listing, exit);
            double? edge = result is { } made && !double.IsNaN(benchmark)
                ? made - IndexSweepRunner.CostInRisk(read.Series[listing.Name], listing, made, read.Companies, 1) - benchmark
                : null;

            return new RuleListing(listing.Name, listing.Bar, listing.Session, result is null ? null : listing.Session + sessions, edge);
        }

        (double? Result, int Sessions) Menu(FamilyListing listing)
        {
            var outcome = ExitProcedures.Replayed(closes, listing, exit!);

            return (outcome.Result, outcome.Sessions);
        }

        // The listings a filter keeps, and each night's handed to an arrangement in the family's own order, each kept
        // carrying its place in the arrangement as its order, so the walk takes them as the arrangement ordered them.
        IReadOnlyList<ExitProcedures.Walked> Walk(ExitChoice? under, Func<int, bool>? keep, Func<IReadOnlyList<int>, IReadOnlyList<int>>? arrange)
        {
            if (arrange is null)
            {
                return ExitProcedures.Walk(read, adapter, closes, keep is null ? listed : [.. listed.Where((_, at) => keep(at))], under);
            }

            var ordered = new List<FamilyListing>();

            foreach (var night in Enumerable.Range(0, listed.Length).Where(at => keep is null || keep(at)).GroupBy(at => listed[at].Session).OrderBy(group => group.Key))
            {
                int[] own = [.. night.OrderByDescending(at => listed[at].Order).ThenByDescending(at => listed[at].ThenBy).ThenBy(at => tickers[listed[at].Name], StringComparer.Ordinal)];
                var kept = arrange(own);

                for (var place = 0; place < kept.Count; place++)
                {
                    ordered.Add(listed[kept[place]] with { Order = kept.Count - place, ThenBy = 0 });
                }
            }

            return ExitProcedures.Walk(read, adapter, closes, ordered, under);
        }

        var floorWords = floor > 0 ? FormattableString.Invariant($", its stop held at least {floor:0.##} typical move under the buy") : string.Empty;

        return new RuleWalk(
            family,
            WordsOf(RuleReplay.SwingWords(family, setting) + floorWords, hooks),
            closes,
            listed.Select(one => one.Cap).DefaultIfEmpty(0).Max(),
            [.. listed.Select(Unit)],
            Walk,
            hooks,
            readings is null ? null : at => readings.Of(listed[at].Name, listed[at].Bar, listed[at].Session));
    }

    // The S&P 400's or 600's provisional pullback, the pullback's base as its record replays it, five a night with one
    // open trade a stock over the listings clearing the index's floors and gate, the plan bought at the close with its
    // stop its typical moves under and its target its reward to risk, at the hooks an approval stored for it where it
    // stored any.
    public static RuleWalk Pullback(LoopRead read, IReadOnlyList<SweepMarketSeries> market, Action<string> progress, LoopChange? standing = null, LoopReadings? readings = null)
    {
        var (replay, _) = SweepIdeasRunner.Read(read.Inputs, market, progress);
        var series = replay.Series;
        var tickers = series.Select(one => one.Name.Ticker).ToArray();
        var closes = ClosesOf(series);
        var rule = SweepIdeas.BaseRule with { PerNight = SetupFamilies.ListedANight };
        var ownExit = SweepAxes.ExitIndex(SweepIdeas.Cap, breakEven: false);
        var hooks = standing?.Hooks ?? LoopChange.NoHooks;
        var standingExit = RuleHooks.Of(hooks).ExitChoice;

        bool Keeps(int name, int bar) => IndexSweepRunner.Clears(read.Index, series[name], bar, read.Income.GetValueOrDefault(tickers[name]) ?? []);

        var listings = replay.Listings(rule, Keeps);

        // A listing as the family's walk states one.
        FamilyListing PlanOf(IdeaListing listing)
        {
            var move = series[listing.Name].Atr[listing.Bar];
            var entry = closes[listing.Name][listing.Bar];
            var stop = entry - (listing.StopMoves * move);

            return new FamilyListing(listing.Name, listing.Bar, listing.Session, listing.RewardToRisk, listing.Strength, entry, stop, entry + (listing.RewardToRisk * (entry - stop)), double.NaN, SweepIdeas.Cap, move);
        }

        double? Costed(IdeaListing listing, double? result) =>
            result is { } made ? made - IndexSweepRunner.CostInRisk(series[listing.Name], PlanOf(listing), made, read.Companies, 1) : null;

        ExitProcedures.Walked Of(IdeaTrade trade, int sessions, string end) =>
            new(trade.Listing.Session, trade.Result is null ? null : trade.Listing.Session + sessions, new FamilyTrade(PlanOf(trade.Listing), trade.Listing.Year, Costed(trade.Listing, trade.Result), trade.Benchmark), end);

        // The base's own exit read off the plan its candidate carries, as the ideas' run reads it.
        (double? Result, int Sessions, double Benchmark) OwnExit(IdeaListing listing)
        {
            var plan = listing.Plan;
            double? result = plan.Code[ownExit] is SweepPlanOutcomes.Win or SweepPlanOutcomes.Loss or SweepPlanOutcomes.Unresolved && !float.IsNaN(plan.Multiple[ownExit])
                ? plan.Multiple[ownExit]
                : null;

            return (result, plan.Ends[ownExit], plan.Benchmark[ownExit]);
        }

        // A listing's trade under an exit of the menu and the same plan under it on every member that session.
        (double? Result, int Sessions, string End, double Benchmark) MenuExit(IdeaListing listing, ExitChoice exit)
        {
            var (name, bar) = (listing.Name, listing.Bar);
            var move = series[name].Atr[bar];
            var entry = closes[name][bar];
            var risk = listing.StopMoves * move;
            var outcome = ExitMenu.Replay(closes[name], bar, new SetupAnchor(DateOnly.MinValue, entry, entry - risk, entry + (listing.RewardToRisk * risk), null, SweepIdeas.Cap, listing.StopMoves), move, exit);
            var sessionAt = series[name].SessionAt;
            var sessions = bar + outcome.Sessions < sessionAt.Length ? sessionAt[bar + outcome.Sessions] - sessionAt[bar] : outcome.Sessions;
            var benchmark = ExitMenu.Benchmark(closes, read.Members.Names[listing.Session], read.Members.Bars[listing.Session], (member, at) => series[member].Atr[at], listing.StopMoves, listing.RewardToRisk, null, SweepIdeas.Cap, exit).Average;

            return (outcome.Result, sessions, outcome.End, benchmark);
        }

        // A listing's trade under the exit the rule stands at, its own or the menu's an approval set.
        (double? Result, int Sessions, double Benchmark) Standing(IdeaListing listing)
        {
            if (standingExit is null)
            {
                return OwnExit(listing);
            }

            var (result, sessions, _, benchmark) = MenuExit(listing, standingExit);

            return (result, sessions, benchmark);
        }

        RuleListing Unit(IdeaListing listing)
        {
            var (result, sessions, benchmark) = Standing(listing);

            return new RuleListing(
                listing.Name,
                listing.Bar,
                listing.Session,
                result is null ? null : listing.Session + sessions,
                result is not null && !double.IsNaN(benchmark) ? Costed(listing, result) - benchmark : null);
        }

        // The listings a filter keeps, each night's handed to an arrangement in the walk's own order and each kept
        // carrying its place in the arrangement in the order the walk reads first, walked under the exit named or the
        // base's own.
        IReadOnlyList<ExitProcedures.Walked> Walk(ExitChoice? exit, Func<int, bool>? keep, Func<IReadOnlyList<int>, IReadOnlyList<int>>? arrange)
        {
            IReadOnlyList<IdeaListing> kept;
            var order = rule.Order;

            if (arrange is null)
            {
                kept = keep is null ? listings : [.. listings.Where((_, at) => keep(at))];
            }
            else
            {
                var ordered = new List<IdeaListing>();

                foreach (var night in Enumerable.Range(0, listings.Count).Where(at => keep is null || keep(at)).GroupBy(at => listings[at].Session).OrderBy(group => group.Key))
                {
                    int[] own =
                    [
                        .. night
                            .OrderByDescending(at => rule.Order == IdeaOrder.RsiFall ? (double.IsNaN(listings[at].RsiFall) ? double.NegativeInfinity : listings[at].RsiFall) : 0.0)
                            .ThenByDescending(at => listings[at].RewardToRisk)
                            .ThenByDescending(at => listings[at].Strength)
                            .ThenByDescending(at => listings[at].Band)
                            .ThenBy(at => tickers[listings[at].Name], StringComparer.Ordinal),
                    ];
                    var arranged = arrange(own);

                    for (var place = 0; place < arranged.Count; place++)
                    {
                        ordered.Add(listings[arranged[place]] with { RsiFall = arranged.Count - place });
                    }
                }

                kept = ordered;
                order = IdeaOrder.RsiFall;
            }

            if (exit is null)
            {
                return [.. SweepIdeas.Walk(kept, tickers, rule.PerNight, OwnExit, order).Select(trade => Of(trade, trade.Listing.Plan.Ends[ownExit], string.Empty))];
            }

            var ended = new Dictionary<(int Name, int Session), (int Sessions, string End)>();

            (double? Result, int Sessions, double Benchmark) Exit(IdeaListing listing)
            {
                var (result, sessions, end, benchmark) = MenuExit(listing, exit);

                ended[(listing.Name, listing.Session)] = (sessions, end);

                return (result, sessions, benchmark);
            }

            return
            [
                .. SweepIdeas.Walk(kept, tickers, rule.PerNight, Exit, order).Select(trade =>
                {
                    var (sessions, end) = ended[(trade.Listing.Name, trade.Listing.Session)];

                    return Of(trade, sessions, end);
                }),
            ];
        }

        progress(FormattableString.Invariant($"read the pullback's {listings.Count} listing(s) on the {DecisionCards.NameOf(read.Index)}"));

        return new RuleWalk(
            SetupFamilies.Pullback,
            WordsOf(RuleReplay.PullbackWords, hooks),
            closes,
            SweepIdeas.Cap,
            [.. listings.Select(Unit)],
            Walk,
            hooks,
            readings is null ? null : at => readings.Of(listings[at].Name, listings[at].Bar, listings[at].Session));
    }
}
