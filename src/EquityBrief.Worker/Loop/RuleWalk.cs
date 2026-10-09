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
// its trade would have ended on under the rule's own exit, and its edge after its round trip had it been taken, each
// none where the history has not reached its end.
public sealed record RuleListing(int Name, int Bar, int Session, int? Exit, double? Edge);

// One family's rule on an index read once for every engine: its words, its closes, the sessions a trade is given,
// every listing it makes over the whole history, and its walk, five a night with one open trade a stock, under its own
// exit or one of the menu's, over every listing, over those a filter keeps or over each night's listings as a rule's
// hooks order and keep them, each trade after its round trip and against the same plan under the same exit on every
// member that session.
// see: The trade autopsy proposes exits of a fixed menu, each tested as the procedure that chose it
// see: Winners against losers proposes a condition only where it beats a within-night shuffle of its own search
// see: A fitted statistical model is a rule
public sealed class RuleWalk(
    string family,
    string current,
    double[][] closes,
    int cap,
    IReadOnlyList<RuleListing> listings,
    Func<ExitChoice?, Func<int, bool>?, IReadOnlyList<ExitProcedures.Walked>> walk,
    Func<Func<IReadOnlyList<int>, IReadOnlyList<int>>, IReadOnlyList<ExitProcedures.Walked>> arranged)
{
    IReadOnlyList<ExitProcedures.Walked>? own;

    public string Family => family;

    public string Current => current;

    public double[][] Closes => closes;

    public int Cap => cap;

    public IReadOnlyList<RuleListing> Listings => listings;

    public IReadOnlyList<ExitProcedures.Walked> Own => own ??= walk(null, null);

    public IReadOnlyList<ExitProcedures.Walked> Under(ExitChoice exit) => walk(exit, null);

    // The rule's own walk over the listings a filter keeps, by each listing's place in the listings.
    public IReadOnlyList<ExitProcedures.Walked> Keeping(Func<int, bool> keep) => walk(null, keep);

    // The rule's own walk under its own exit over each night's listings as an arrangement orders and keeps them: handed
    // a night's listings in the rule's own order, by their places in the listings, it returns those kept in the order
    // the walk takes them.
    public IReadOnlyList<ExitProcedures.Walked> Arranged(Func<IReadOnlyList<int>, IReadOnlyList<int>> arrange) => arranged(arrange);

    // The rule's own walk with its hooks set as a registration would set them: each night's listings kept and ordered by
    // the hooks exactly as the night keeps and orders a hooked rule's candidates, a listing's readings read by its place.
    public IReadOnlyList<ExitProcedures.Walked> Hooked(RuleHooks hooks, Func<int, IReadOnlyList<double?>> readingsOf) =>
        arranged(night => hooks.Order(night, readingsOf));

    static double[][] ClosesOf(IReadOnlyList<SweepSeries> series) =>
        [.. series.Select(one => one.Bars.Select(bar => Statistic.FromPrice(bar.Close)).ToArray())];

    // The breakout or the drift at the setting it froze at on the S&P 500, which the S&P 400 and 600 run provisionally:
    // its sweep's own listings and exit, each listing kept only where it clears the index's floors and gate, and on the
    // S&P 500 every listing.
    public static RuleWalk Swing(LoopRead read, string family, bool large)
    {
        var adapter = FamilySweepRunner.For(family, read.Series, read.Sessions, read.Members, read.FirstScored, read.Calendar);
        int[] setting = family == BreakoutRule.Name ? [.. IndexNightRead.BreakoutAsFrozen] : [.. IndexNightRead.DriftAsFrozen];
        var closes = ClosesOf(read.Series);
        var tickers = read.Series.Select(one => one.Name.Ticker).ToArray();
        FamilyListing[] listed =
        [
            .. adapter.Listings(setting).Where(listing => listing.Move > 0
                && (large || IndexSweepRunner.Clears(read.Index, read.Series[listing.Name], listing.Bar, read.Income.GetValueOrDefault(tickers[listing.Name]) ?? []))),
        ];

        RuleListing Unit(FamilyListing listing)
        {
            var (result, sessions) = adapter.Exit(listing);
            var benchmark = result is null ? double.NaN : adapter.Benchmark(listing);
            double? edge = result is { } made && !double.IsNaN(benchmark)
                ? made - IndexSweepRunner.CostInRisk(read.Series[listing.Name], listing, made, read.Companies, 1) - benchmark
                : null;

            return new RuleListing(listing.Name, listing.Bar, listing.Session, result is null ? null : listing.Session + sessions, edge);
        }

        // Each night's listings in the family's own order handed to the arrangement, and each it keeps carrying its place
        // in the arrangement as its order, so the walk takes them as the arrangement ordered them.
        IReadOnlyList<ExitProcedures.Walked> Arranged(Func<IReadOnlyList<int>, IReadOnlyList<int>> arrange)
        {
            var ordered = new List<FamilyListing>();

            foreach (var night in Enumerable.Range(0, listed.Length).GroupBy(at => listed[at].Session).OrderBy(group => group.Key))
            {
                int[] own = [.. night.OrderByDescending(at => listed[at].Order).ThenByDescending(at => listed[at].ThenBy).ThenBy(at => tickers[listed[at].Name], StringComparer.Ordinal)];
                var kept = arrange(own);

                for (var place = 0; place < kept.Count; place++)
                {
                    ordered.Add(listed[kept[place]] with { Order = kept.Count - place, ThenBy = 0 });
                }
            }

            return ExitProcedures.Walk(read, adapter, closes, ordered, null);
        }

        return new RuleWalk(
            family,
            RuleReplay.SwingWords(family, setting),
            closes,
            listed.Select(one => one.Cap).DefaultIfEmpty(0).Max(),
            [.. listed.Select(Unit)],
            (exit, keep) => ExitProcedures.Walk(read, adapter, closes, keep is null ? listed : [.. listed.Where((_, at) => keep(at))], exit),
            Arranged);
    }

    // The S&P 400's or 600's provisional pullback, the pullback's base as its record replays it, five a night with one
    // open trade a stock over the listings clearing the index's floors and gate, the plan bought at the close with its
    // stop its typical moves under and its target its reward to risk.
    public static RuleWalk Pullback(LoopRead read, IReadOnlyList<SweepMarketSeries> market, Action<string> progress)
    {
        var (replay, _) = SweepIdeasRunner.Read(read.Inputs, market, progress);
        var series = replay.Series;
        var tickers = series.Select(one => one.Name.Ticker).ToArray();
        var closes = ClosesOf(series);
        var rule = SweepIdeas.BaseRule with { PerNight = SetupFamilies.ListedANight };
        var ownExit = SweepAxes.ExitIndex(SweepIdeas.Cap, breakEven: false);

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

        RuleListing Unit(IdeaListing listing)
        {
            var (result, sessions, benchmark) = OwnExit(listing);

            return new RuleListing(
                listing.Name,
                listing.Bar,
                listing.Session,
                result is null ? null : listing.Session + sessions,
                result is not null && !double.IsNaN(benchmark) ? Costed(listing, result) - benchmark : null);
        }

        IReadOnlyList<ExitProcedures.Walked> Walk(ExitChoice? exit, Func<int, bool>? keep)
        {
            IReadOnlyList<IdeaListing> kept = keep is null ? listings : [.. listings.Where((_, at) => keep(at))];

            if (exit is null)
            {
                return [.. SweepIdeas.Walk(kept, tickers, rule.PerNight, OwnExit, rule.Order).Select(trade => Of(trade, trade.Listing.Plan.Ends[ownExit], string.Empty))];
            }

            var ended = new Dictionary<(int Name, int Session), (int Sessions, string End)>();

            (double? Result, int Sessions, double Benchmark) Exit(IdeaListing listing)
            {
                var (name, bar) = (listing.Name, listing.Bar);
                var move = series[name].Atr[bar];
                var entry = closes[name][bar];
                var risk = listing.StopMoves * move;
                var outcome = ExitMenu.Replay(closes[name], bar, new SetupAnchor(DateOnly.MinValue, entry, entry - risk, entry + (listing.RewardToRisk * risk), null, SweepIdeas.Cap, listing.StopMoves), move, exit);
                var sessionAt = series[name].SessionAt;
                var sessions = bar + outcome.Sessions < sessionAt.Length ? sessionAt[bar + outcome.Sessions] - sessionAt[bar] : outcome.Sessions;
                var benchmark = ExitMenu.Benchmark(closes, read.Members.Names[listing.Session], read.Members.Bars[listing.Session], (member, at) => series[member].Atr[at], listing.StopMoves, listing.RewardToRisk, null, SweepIdeas.Cap, exit).Average;

                ended[(name, listing.Session)] = (sessions, outcome.End);

                return (outcome.Result, sessions, benchmark);
            }

            return
            [
                .. SweepIdeas.Walk(kept, tickers, rule.PerNight, Exit, rule.Order).Select(trade =>
                {
                    var (sessions, end) = ended[(trade.Listing.Name, trade.Listing.Session)];

                    return Of(trade, sessions, end);
                }),
            ];
        }

        // Each night's listings in the walk's own order handed to the arrangement, and each it keeps carrying its place in
        // the arrangement in the order the walk reads first, so the walk takes them as the arrangement ordered them.
        IReadOnlyList<ExitProcedures.Walked> Arranged(Func<IReadOnlyList<int>, IReadOnlyList<int>> arrange)
        {
            var ordered = new List<IdeaListing>();

            foreach (var night in Enumerable.Range(0, listings.Count).GroupBy(at => listings[at].Session).OrderBy(group => group.Key))
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
                var kept = arrange(own);

                for (var place = 0; place < kept.Count; place++)
                {
                    ordered.Add(listings[kept[place]] with { RsiFall = kept.Count - place });
                }
            }

            return [.. SweepIdeas.Walk(ordered, tickers, rule.PerNight, OwnExit, IdeaOrder.RsiFall).Select(trade => Of(trade, trade.Listing.Plan.Ends[ownExit], string.Empty))];
        }

        progress(FormattableString.Invariant($"read the pullback's {listings.Count} listing(s) on the {DecisionCards.NameOf(read.Index)}"));

        return new RuleWalk(SetupFamilies.Pullback, RuleReplay.PullbackWords, closes, SweepIdeas.Cap, [.. listings.Select(Unit)], Walk, Arranged);
    }
}
