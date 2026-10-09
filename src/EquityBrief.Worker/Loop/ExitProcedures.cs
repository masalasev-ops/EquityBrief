using EquityBrief.Core.Families;
using EquityBrief.Core.Ledger;
using EquityBrief.Core.Loop;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Readings;
using EquityBrief.Core.Sweep;
using EquityBrief.Worker.Cards;
using EquityBrief.Worker.Indices;
using EquityBrief.Worker.Sweep;

namespace EquityBrief.Worker.Loop;

// The trade autopsy's exits as the tester runs them inside each fold, for the breakout and the earnings drift on every
// index and the S&P 400's and 600's provisional pullback: the rule as it stands walked once over the whole history
// under its own exit and once under each exit of the menu, each trade after its round trip and against the same plan
// under the same exit on every member, and the sector heavyweights' book under its two; each fold ranks
// the exits by their edge on the trades that ended before its year, through its view and over the family floors in
// proportion, and keeps those above the rule's own; the k-th proposal is the k-th exit each fold ranks, scored on that
// fold's year against the rule as it stands. The autopsy's figures are read off the rule's own finished trades.
// see: The trade autopsy proposes exits of a fixed menu, each tested as the procedure that chose it
// see: A change is adopted only on test years the proposal never saw, and a search is judged as a procedure run year by year
public static class ExitProcedures
{
    // The most exits the autopsy proposes a family an index.
    public const int Proposals = 5;

    public static string ProposalName(int rank) => FormattableString.Invariant($"the autopsy's exit, ranked {rank}");

    // One trade a walk kept: its listing's session, the session it ended on, none while open, the trade, and how it
    // ended where the menu's walk named it.
    public sealed record Walked(int Entry, int? Exit, FamilyTrade Trade, string End)
    {
        public double? Edge => Trade.Result is { } result && !double.IsNaN(Trade.Benchmark) ? result - Trade.Benchmark : null;
    }

    // The menu's exits a fold ranks above the rule's own on the trades that ended before its year, read through its view,
    // the strongest first, each meeting the family floors in proportion to the years it learns on; a tie to the lower
    // number.
    public static IReadOnlyList<(ExitChoice Exit, double Edge)> Rank(
        LoopFold fold,
        IReadOnlyList<DateOnly> calendar,
        int learningYears,
        int nights,
        IReadOnlyList<Walked> own,
        IReadOnlyList<IReadOnlyList<Walked>> byExit)
    {
        FamilyFigures Learned(IReadOnlyList<Walked> trades) =>
            FamilySweep.Figures(
                "learning",
                [.. new FoldView<Walked>(fold, trades, one => calendar[one.Entry], one => one.Exit is { } exit ? calendar[exit] : null).Learning.Select(one => one.Trade)],
                nights);

        var (trades, years) = LoopProcedures.Floors(learningYears);
        var ownEdge = Learned(own).Edge;

        return
        [
            .. ExitMenu.Swing
                .Select((exit, at) => (Exit: exit, Figures: Learned(byExit[at])))
                .Where(one => one.Figures.Edge is { } edge && one.Figures.Trades >= trades && one.Figures.YearsBeating >= years && (ownEdge is null || edge > ownEdge))
                .OrderByDescending(one => one.Figures.Edge)
                .ThenBy(one => one.Exit.Number)
                .Select(one => (one.Exit, one.Figures.Edge!.Value)),
        ];
    }

    // The proposals and the autopsy's figures for one family's rule on an index: the breakout and the drift on every
    // index and the S&P 400's and 600's provisional pullback. The S&P 500's pullback is the swing filter's, whose walk
    // every S&P 500 evaluator pins, and no exit is proposed for it.
    public static (IReadOnlyList<LoopProposalRead> Proposals, IReadOnlyList<AutopsyFigure> Figures) Autopsy(LoopRead read, RuleWalk rule, Action<string> progress)
    {
        progress(FormattableString.Invariant($"walking the {rule.Family} on the {DecisionCards.NameOf(read.Index)} under its own exit and the menu's {ExitMenu.Swing.Count}"));

        IReadOnlyList<IReadOnlyList<Walked>> walks = [.. ExitMenu.Swing.Select(rule.Under)];

        return Propose(read, rule.Family, rule.Current, rule.Closes, rule.Own, walks);
    }

    // Each fold's ranking of the menu's exits above the rule's own, the k-th proposal the k-th exit each fold ranks, and
    // the autopsy's figures read off the rule's own finished trades.
    static (IReadOnlyList<LoopProposalRead> Proposals, IReadOnlyList<AutopsyFigure> Figures) Propose(
        LoopRead read,
        string family,
        string current,
        double[][] closes,
        IReadOnlyList<Walked> own,
        IReadOnlyList<IReadOnlyList<Walked>> walks)
    {
        var nights = read.Calendar.Length - read.FirstScored;

        IReadOnlyList<(ExitChoice Exit, double Edge)> Ranked(LoopFold fold) => Rank(fold, read.Calendar, read.LearningYears(fold), nights, own, walks);

        var ranked = read.Folds.Select(Ranked).ToArray();
        var finished = Ranked(read.AllFinished);
        var ownFinished = FamilySweep.Figures(
            "finished",
            [.. new FoldView<Walked>(read.AllFinished, own, one => read.Calendar[one.Entry], one => one.Exit is { } exit ? read.Calendar[exit] : null).Learning.Select(one => one.Trade)],
            nights).Edge ?? 0;
        var figures = PathAutopsy.Read([.. own.Where(one => one.Exit is not null && one.Trade.Result is not null).Select(one => Path(read, closes, one))]);

        static IReadOnlyList<(int Entry, double? Edge)> Units(IReadOnlyList<Walked> trades) => [.. trades.Select(one => (one.Entry, one.Edge))];

        var proposals = new List<LoopProposalRead>();

        for (var rank = 1; rank <= Proposals; rank++)
        {
            var place = rank - 1;
            var chosen = ranked.Select(fold => place < fold.Count ? fold[place].Exit : null).ToArray();
            var named = place < finished.Count ? finished[place] : default;

            proposals.Add(new LoopProposalRead(
                family,
                ProposalName(rank),
                named.Exit is null ? null : "exit: " + named.Exit.Words,
                current,
                LoopProcedures.Risks,
                walks.Concat([own]).SelectMany(one => one).Select(one => one.Trade.Listing.Cap).DefaultIfEmpty(0).Max(),
                [.. read.Folds.Select((fold, at) => (fold, chosen[at] is { } exit ? "exit: " + exit.Words : null))],
                named.Exit is null ? 0 : chosen.Count(exit => exit?.Number == named.Exit.Number),
                LoopProcedures.Evidence(read.Folds, read.Calendar, [.. chosen.Select(exit => exit is null ? null : Units(walks[exit.Number - 1]))], Units(own)))
            {
                Finding = named.Exit is null ? null : PathAutopsy.Finding(figures, named.Exit, ownFinished, named.Edge),
                Change = named.Exit is null ? null : LoopChange.OfHooks(new Dictionary<string, double>(StringComparer.Ordinal) { [RuleHooks.ExitParameter] = named.Exit.Number }),
                Reference = LoopProcedures.Reference(read, Units(own)),
            });
        }

        return (proposals, figures);
    }

    // The sector heavyweights' two exits of the menu, a holding also sold at a close under its 200-day average and a
    // rebalance each week, each a change to the setting the rule today holds at, walked by the book's own walk over the
    // whole history and ranked in each fold above the rule's own on what ended before its year, a book's months its unit.
    public static IReadOnlyList<LoopProposalRead> Heavyweights(LoopRead read, HeavyweightLay lay, Action<string> progress)
    {
        var current = read.Index == WalkForwardTester.LargeIndex ? HeavyweightSweep.Frozen : IndexHeavyweights.Provisional;
        (HeavyweightSetting Setting, string Words)[] menu =
        [
            (current with { Exit = HeavyweightExit.Both }, "a holding also sold at a close under its 200-day average"),
            (current with { Period = HeavyweightPeriod.Week }, "rebalanced each week"),
        ];

        progress(FormattableString.Invariant($"walking the heavyweights on the {DecisionCards.NameOf(read.Index)} under their own exit and the menu's two"));

        var own = lay.BookOf(current, "the rule today");
        var books = menu.Where(one => one.Setting != current).Select(one => (one.Words, Book: lay.BookOf(one.Setting, one.Words))).ToArray();

        HeavyweightFigures Learned(LoopFold fold, HeavyweightBook book) =>
            HeavyweightSweep.Figures(book.Setting.Key, new FoldView<HeavyweightTrade>(fold, book.Costed, trade => read.Calendar[trade.Entry], trade => trade.End is { } end ? read.Calendar[end] : null).Learning);

        IReadOnlyList<(int At, double Edge)> Ranked(LoopFold fold)
        {
            var (trades, years) = LoopProcedures.Floors(read.LearningYears(fold));
            var ownEdge = Learned(fold, own).Edge;

            return
            [
                .. books
                    .Select((one, at) => (At: at, Figures: Learned(fold, one.Book)))
                    .Where(one => one.Figures.Edge is { } edge && one.Figures.Trades >= trades && one.Figures.YearsBeating >= years && (ownEdge is null || edge > ownEdge))
                    .OrderByDescending(one => one.Figures.Edge)
                    .ThenBy(one => one.At)
                    .Select(one => (one.At, one.Figures.Edge!.Value)),
            ];
        }

        var ranked = read.Folds.Select(Ranked).ToArray();
        var finished = Ranked(read.AllFinished);
        var ownFinished = Learned(read.AllFinished, own).Edge ?? 0;
        var proposals = new List<LoopProposalRead>();

        for (var rank = 1; rank <= books.Length; rank++)
        {
            var place = rank - 1;
            int?[] chosen = [.. ranked.Select(fold => place < fold.Count ? fold[place].At : (int?)null)];
            var named = place < finished.Count ? finished[place] : ((int At, double Edge)?)null;

            proposals.Add(new LoopProposalRead(
                HeavyweightRule.Name,
                ProposalName(rank),
                named is { } exit ? "exit: " + books[exit.At].Words : null,
                RuleReplay.HeavyweightWords(current),
                LoopProcedures.Points,
                BookMonths.LongestMonth,
                [.. read.Folds.Select((fold, at) => (fold, chosen[at] is { } choice ? "exit: " + books[choice].Words : null))],
                named is { } best ? chosen.Count(choice => choice == best.At) : 0,
                LoopProcedures.BookEvidence(read.Folds, read.Calendar, [.. chosen.Select(choice => choice is { } at ? books[at].Book.Months : null)], own.Months))
            {
                Finding = named is { } found
                    ? FormattableString.Invariant($"{books[found.At].Words} read {found.Edge * 100:+0.00;-0.00} points a holding on the years it learned on against the rule's own {ownFinished * 100:+0.00;-0.00}")
                    : null,
                Change = named is { } kept ? new LoopChange(null, books[kept.At].Book.Setting.Key, null, LoopChange.NoHooks) : null,
                Reference = LoopProcedures.Reference(read, own),
            });
        }

        return proposals;
    }

    // A rule's trades over the listings it is handed under an exit, or its own where none is given, each result after its
    // round trip and against the same plan under the same exit on every member that session.
    public static IReadOnlyList<Walked> Walk(LoopRead read, FamilySweepRunner.Adapter adapter, double[][] closes, IReadOnlyList<FamilyListing> listings, ExitChoice? exit)
    {
        var tickers = read.Series.Select(one => one.Name.Ticker).ToArray();
        var ended = new Dictionary<(int Name, int Session), (int Sessions, string End)>();

        static SetupAnchor Anchor(FamilyListing listing) =>
            new(DateOnly.MinValue, listing.Entry, listing.Stop, listing.Trails ? null : listing.Target, listing.Trails ? listing.Trail : null, listing.Cap, listing.Move > 0 ? (listing.Entry - listing.Stop) / listing.Move : null);

        (double? Result, int Sessions) Exit(FamilyListing listing)
        {
            if (exit is null)
            {
                var walked = adapter.Exit(listing);

                ended[(listing.Name, listing.Session)] = (walked.Sessions, string.Empty);

                return walked;
            }

            var outcome = ExitMenu.Replay(closes[listing.Name], listing.Bar, Anchor(listing), listing.Move, exit);

            ended[(listing.Name, listing.Session)] = (outcome.Sessions, outcome.End);

            return (outcome.Result, outcome.Sessions);
        }

        double Benchmark(FamilyListing listing) =>
            exit is null
                ? adapter.Benchmark(listing)
                : ExitMenu.Benchmark(
                    closes,
                    read.Members.Names[listing.Session],
                    read.Members.Bars[listing.Session],
                    (name, bar) => read.Series[name].Atr[bar],
                    (listing.Entry - listing.Stop) / listing.Move,
                    listing.Trails ? null : (listing.Target - listing.Entry) / (listing.Entry - listing.Stop),
                    listing.Trails ? listing.Trail / (listing.Entry - listing.Stop) : null,
                    listing.Cap,
                    exit).Average;

        int YearOf(int session) => read.Calendar[session].Year - SweepColumns.FirstScored.Year;

        var trades = FamilySweep.Walk(listings, tickers, YearOf, Exit, Benchmark);

        return
        [
            .. trades.Select(trade =>
            {
                var listing = trade.Listing;
                var (sessions, end) = ended[(listing.Name, listing.Session)];
                var costed = trade.Result is { } result
                    ? trade with { Result = result - IndexSweepRunner.CostInRisk(read.Series[listing.Name], listing, result, read.Companies, 1) }
                    : trade;

                return new Walked(listing.Session, trade.Result is null ? null : listing.Session + sessions, costed, end);
            }),
        ];
    }

    // A finished trade's closes from its buy to its end, its stop read as where it ended where the walk named none.
    static TradePath Path(LoopRead read, double[][] closes, Walked trade)
    {
        var listing = trade.Trade.Listing;
        var series = closes[listing.Name];
        var sessions = trade.Exit!.Value - listing.Session;
        var path = series[listing.Bar..Math.Min(series.Length, listing.Bar + sessions + 1)];
        var last = path[^1];
        var end = trade.End.Length > 0
            ? trade.End
            : last < listing.Stop ? SetupEnds.Stop : !listing.Trails && last >= listing.Target ? SetupEnds.Target : SetupEnds.Cap;

        return new TradePath(path, listing.Entry, listing.Entry - listing.Stop, end);
    }
}
