using System.Collections.Concurrent;
using EquityBrief.Core.Families;
using EquityBrief.Core.Loop;
using EquityBrief.Core.Sweep;
using EquityBrief.Worker.Cards;
using EquityBrief.Worker.Indices;
using EquityBrief.Worker.Sweep;

namespace EquityBrief.Worker.Loop;

// The S&P 400's and 600's families searched on their own history as the tester judges a search: each search run inside
// every fold on the years before its test year, its floors and its second stage's rule in proportion to the years it
// learns on, and scored on that year against the rule the index's night reads today. The breakout's and the drift's grid
// with the market check's seven levels as a dial, each fold's second stage read over its own ten strongest settings and
// its survivors crossed, the fold choosing the stronger of its grid's choice and its crossings meeting the floors; and the
// sector heavyweights' two designs over their full grids at every quality and floor. It reads the history and writes
// nothing.
// see: A change is adopted only on test years the proposal never saw, and a search is judged as a procedure run year by year
// see: A search run inside a fold reads the family floors in proportion to the years it learns on
// see: A family's search on the S&P 400 or 600 is judged by the tester year by year with its second stage run inside each fold
public static class OwnProcedures
{
    // What a family's grid on an index's own history offers a fold: its grid, a setting's trades under a set of the second
    // stage's levels after their round trips, with the sessions each was held to its sale where a map is handed in, the
    // rule the index's night reads today, the strongest settings a second stage reads, and its levels.
    public interface IOwnSwing
    {
        string Family { get; }

        FamilyGrid Grid { get; }

        bool LastTwo { get; }

        bool Floored { get; }

        IReadOnlyList<DialLevel<FamilyListing>> Levels { get; }

        IReadOnlyList<FamilyTrade> TradesUnder(int[] setting, IReadOnlyList<DialLevel<FamilyListing>> together, ConcurrentDictionary<(int Name, int Session), int>? held = null);

        IReadOnlyList<FamilyTrade> NightsRule(ConcurrentDictionary<(int Name, int Session), int>? held = null);

        int[][] Strongest(IReadOnlyList<(int[] Setting, FamilyFigures Figures)> read, int tradesHeld = SweepDials.TradesHeld);
    }

    public const string SwingGrid = "the grid with the market check and its second stage, at the next open";

    public const string DesignA = "design (a)'s full grid, at the next open";

    public const string DesignB = "design (b)'s full grid, at the next open";

    // One trade a setting kept over the whole history: its listing's session, the session it ended on, none while open,
    // and the trade after its round trip.
    public sealed record Walked(int Entry, int? Exit, FamilyTrade Trade)
    {
        public double? Edge => Trade.Result is { } result && !double.IsNaN(Trade.Benchmark) ? result - Trade.Benchmark : null;
    }

    // What a fold chose: a setting of the grid, the second stage's levels crossed on it, none where it crossed none, and
    // its figures over the years the fold learned on.
    public sealed record SwingChoice(int[] Setting, IReadOnlyList<DialLevel<FamilyListing>> Levels, FamilyFigures Figures);

    static int Up(int numerator, int denominator) => (numerator + denominator - 1) / denominator;

    // The rule the second stage keeps a level by, in proportion to the years a fold learns on: a level is kept on a
    // setting where it is higher in six of every eight of those years, rounded up, two of them among the last three, and
    // leaves the trade floor in proportion; a switch is read on each year's total and on the result a trade, as the
    // second stage reads it. Over eight years it is the second stage's own rule.
    public static bool KeptOver(SweepMeasures without, SweepMeasures with, bool onTotals, int years)
    {
        if (with.Scored < Up(SweepMeasures.TradeFloor * years, SweepFigures.Years))
        {
            return false;
        }

        var (higher, recent) = (0, 0);
        var read = Math.Min(years, Math.Min(without.YearScored.Length, with.YearScored.Length));

        for (var year = 0; year < read; year++)
        {
            var up = onTotals
                ? SweepDials.Total(with, year) > SweepDials.Total(without, year)
                : with.YearEdge[year] is { } mine && without.YearEdge[year] is { } theirs && mine > theirs;

            if (up)
            {
                higher++;
                recent += year >= years - SweepIdeas.RecentYears ? 1 : 0;
            }
        }

        return higher >= Up(SweepIdeas.YearsBetter * years, SweepFigures.Years)
            && recent >= SweepIdeas.RecentYearsBetter
            && (!onTotals || (with.AverageMultiple is { } result && without.AverageMultiple is { } before && result > before));
    }

    // A setting and the levels crossed on it, as a key and in a proposal's words.
    public static string KeyOf(FamilyGrid grid, int[] setting, IReadOnlyList<DialLevel<FamilyListing>> levels) =>
        grid.Key(setting) + (levels.Count == 0 ? string.Empty : " with " + string.Join("; ", levels.Select(level => level.Dial + ", " + level.Name)));

    // The breakout's or the drift's search on an index's own history as a procedure: every setting of the grid walked
    // once over the whole history and every setting a fold's second stage reads under each level once, a fold reading
    // only the trades that ended before its year.
    public static LoopProposalRead Swing(LoopRead read, IOwnSwing own, Action<string> progress)
    {
        var grid = own.Grid;
        var settings = grid.Settings;
        var nights = read.Calendar.Length - read.FirstScored;
        var walks = new Dictionary<string, IReadOnlyList<Walked>>(StringComparer.Ordinal);

        // One walk at a time, as the index sweep walks them, since the adapter keeps its exits and benchmarks as it reads.
        IReadOnlyList<Walked> Walk(int[] setting, IReadOnlyList<DialLevel<FamilyListing>> levels)
        {
            var key = KeyOf(grid, setting, levels);

            if (!walks.TryGetValue(key, out var walked))
            {
                var held = new ConcurrentDictionary<(int Name, int Session), int>();

                walks[key] = walked = Kept(own.TradesUnder(setting, levels, held), held);
            }

            return walked;
        }

        progress(FormattableString.Invariant($"walking the {own.Family}'s {settings.Count} setting(s) on the {DecisionCards.NameOf(read.Index)} over the whole history"));

        var first = settings.Select(setting => Walk(setting, [])).ToArray();
        var nightsHeld = new ConcurrentDictionary<(int Name, int Session), int>();
        var current = Kept(own.NightsRule(nightsHeld), nightsHeld);

        SwingChoice? ChooseIn(LoopFold fold)
        {
            var years = read.LearningYears(fold);
            var (tradeFloor, yearFloor) = LoopProcedures.Floors(years);

            IReadOnlyList<FamilyTrade> Learned(IReadOnlyList<Walked> walked) =>
                [.. new FoldView<Walked>(fold, walked, one => read.Calendar[one.Entry], one => one.Exit is { } exit ? read.Calendar[exit] : null).Learning.Select(one => one.Trade)];

            bool Meets(FamilyFigures figures) =>
                figures.Edge is not null
                && figures.Trades >= tradeFloor
                && figures.YearsBeating >= yearFloor
                && (!own.LastTwo || LoopProcedures.LastTwoYears(figures.YearTrades, figures.YearEdge, years) > 0);

            var figures = settings.Select((setting, at) => (Setting: setting, Figures: FamilySweep.Figures(grid.Key(setting), Learned(first[at]), nights))).ToArray();
            var choices = new List<SwingChoice>();

            if (LoopProcedures.Choose(grid, figures, years, own.LastTwo) is { } chosen)
            {
                choices.Add(new SwingChoice(chosen, [], figures.First(one => grid.Key(one.Setting) == grid.Key(chosen)).Figures));
            }

            // The fold's second stage over its own ten strongest settings, each level alone and the survivors crossed.
            var ten = own.Strongest(figures, Up(SweepDials.TradesHeld * years, SweepFigures.Years));
            var without = ten.Select(setting => SweepDials.Of(Learned(Walk(setting, [])))).ToArray();
            var crossedLevels = own.Levels
                .Select(level =>
                {
                    var pairs = ten.Select((setting, at) => (Without: without[at], With: SweepDials.Of(Learned(Walk(setting, [level]))))).ToArray();
                    var changes = pairs
                        .Select(pair => level.Switch ? Change(pair.Without.AverageMultiple, pair.With.AverageMultiple) : Change(pair.Without.Edge, pair.With.Edge))
                        .OfType<double>()
                        .ToArray();

                    return (Level: level, KeptOn: pairs.Count(pair => KeptOver(pair.Without, pair.With, level.Switch, years)), Change: Median(changes));
                })
                .Where(one => one.KeptOn >= SweepDials.KeptOn)
                .GroupBy(one => one.Level.Dial, StringComparer.Ordinal)
                .Select(dial => dial.OrderByDescending(one => one.KeptOn).ThenByDescending(one => one.Change ?? double.MinValue).First().Level)
                .ToArray();

            if (crossedLevels.Length > 0)
            {
                foreach (var setting in ten)
                {
                    var crossed = FamilySweep.Figures(KeyOf(grid, setting, crossedLevels), Learned(Walk(setting, crossedLevels)), nights);

                    if (Meets(crossed))
                    {
                        choices.Add(new SwingChoice(setting, crossedLevels, crossed));
                    }
                }
            }

            return choices
                .OrderByDescending(one => one.Figures.Edge)
                .ThenBy(one => one.Levels.Count)
                .ThenBy(one => grid.Changes(one.Setting))
                .ThenBy(one => one.Figures.Key, StringComparer.Ordinal)
                .FirstOrDefault();
        }

        var folds = new List<(LoopFold Fold, SwingChoice? Choice)>();

        foreach (var fold in read.Folds)
        {
            progress(FormattableString.Invariant($"the {own.Family}'s fold for {fold.Year}"));
            folds.Add((fold, ChooseIn(fold)));
        }

        var proposal = ChooseIn(read.AllFinished);

        static IReadOnlyList<(int Entry, double? Edge)> Units(IReadOnlyList<Walked> walked) => [.. walked.Select(one => (one.Entry, one.Edge))];

        string Words(SwingChoice choice) => KeyOf(grid, choice.Setting, choice.Levels);

        static bool SameLevels(SwingChoice one, SwingChoice other) =>
            one.Levels.Select(level => level.Dial + "|" + level.Name).Order(StringComparer.Ordinal).SequenceEqual(other.Levels.Select(level => level.Dial + "|" + level.Name).Order(StringComparer.Ordinal));

        var cap = walks.Values.SelectMany(one => one).Concat(current).Select(one => one.Trade.Listing.Cap).DefaultIfEmpty(0).Max();

        return new LoopProposalRead(
            own.Family,
            SwingGrid,
            proposal is null ? null : Words(proposal),
            grid.Key([.. grid.Provisional]) + (own.Floored ? ", with no stop floor" : string.Empty),
            LoopProcedures.Risks,
            cap,
            [.. folds.Select(one => (one.Fold, one.Choice is null ? null : Words(one.Choice)))],
            proposal is null ? 0 : folds.Count(one => one.Choice is { } choice && SameLevels(choice, proposal) && choice.Setting.Zip(proposal.Setting).All(pair => Math.Abs(pair.First - pair.Second) <= 1)),
            LoopProcedures.Evidence(read.Folds, read.Calendar, [.. folds.Select(one => one.Choice is null ? null : Units(Walk(one.Choice.Setting, one.Choice.Levels)))], Units(current)));
    }

    // A walk's trades with the session each ended on, none while open.
    static IReadOnlyList<Walked> Kept(IReadOnlyList<FamilyTrade> trades, ConcurrentDictionary<(int Name, int Session), int> held) =>
        [.. trades.Select(trade => new Walked(trade.Listing.Session, trade.Result is null ? null : trade.Listing.Session + held[(trade.Listing.Name, trade.Listing.Session)], trade))];

    static double? Change(double? without, double? with) => without is { } before && with is { } after ? after - before : null;

    static double? Median(double[] values)
    {
        if (values.Length == 0)
        {
            return null;
        }

        Array.Sort(values);

        return values.Length % 2 == 1 ? values[values.Length / 2] : (values[(values.Length / 2) - 1] + values[values.Length / 2]) / 2;
    }

    public const string PullbackSearch = "the search over nine dials with the market check and its second stage, at the next open";

    // One trade a pullback setting kept: the session listed, the session it ended on and its edge after its round trip,
    // none where no benchmark was read.
    public sealed record PulledBack(int Entry, int End, double? Edge);

    // What a fold of the pullback chose: a point of the search's space, the levels read with it, and its figures over the
    // years the fold learned on.
    public sealed record PullbackChoice(int[] Point, IReadOnlyList<DialLevel<SweepCandidate>> Levels, SweepMeasures Measures, string Words);

    // One setting walked over a design's picks as the stages walk it, one open trade a stock over the whole history: each
    // trade is tallied only where it ended before a session, under its year moved by a shift, so a fold's years sit where
    // the record's last years do and its recent years are its own; a listing before the scored years is tallied in none.
    public static void WalkUntil(IReadOnlyList<SweepPick> picks, DialSetting setting, in ConditionSetting conditions, SweepStages.Tally tally, int before, int shift)
    {
        tally.Clear();

        var exits = tally.Exits;
        var width = SweepFigures.Width;
        var words = tally.Words;
        var name = -1;
        var stopFloor = SweepStages.StopFloor(setting);

        for (var at = 0; at < picks.Count; at++)
        {
            var pick = picks[at];

            if (pick.Name != name)
            {
                name = pick.Name;
                Array.Fill(tally.OpenUntil, -1);
            }

            if (!pick.Corner.Passes(setting) || !conditions.Passes(pick.Readings))
            {
                continue;
            }

            var plan = pick.Plan;
            var year = pick.Year >= 0 ? pick.Year + shift : -1;

            for (var index = 0; index < exits.Length; index++)
            {
                var exit = exits[index];
                var sums = tally.Sums.AsSpan(index * width, width);

                if (pick.Session <= tally.OpenUntil[index])
                {
                    if (pick.Session < before)
                    {
                        SweepFigures.TallyBlocked(sums, year);
                    }

                    continue;
                }

                var ends = pick.Session + plan.Ends[exit];

                if (ends < before)
                {
                    if (plan.IsATradeAt(exit, stopFloor))
                    {
                        SweepFigures.Tally(sums, year, pick.Block, plan.Code[exit], plan.Null[exit], plan.BreakEven[exit], plan.Multiple[exit], plan.EdgeAt(exit));
                    }
                    else
                    {
                        SweepFigures.Tally(sums, year, pick.Block, SweepPlanOutcomes.NeverEntered, float.NaN, float.NaN, float.NaN, float.NaN);
                    }

                    tally.Nights[(index * words) + (pick.Session >> 6)] |= 1UL << (pick.Session & 63);
                }

                tally.OpenUntil[index] = ends;
            }
        }
    }

    // A fold's summary held to the search's floors in proportion to the years it learned on: its trades, its years and its
    // blocks read as eight years' worth, so a floor the search holds a whole record to holds the fold to its share of it.
    public static SweepSummary InProportion(SweepSummary summary, int years) =>
        years >= SweepFigures.Years
            ? summary
            : summary with
            {
                Scored = summary.Scored * SweepFigures.Years / years,
                YearsBeatingBoth = (byte)Math.Min(byte.MaxValue, summary.YearsBeatingBoth * SweepFigures.Years / years),
                YearsBeatingBreakEven = (byte)Math.Min(byte.MaxValue, summary.YearsBeatingBreakEven * SweepFigures.Years / years),
                Blocks = (byte)Math.Min(byte.MaxValue, summary.Blocks * SweepFigures.Years / years),
            };

    // A setting's figures over the trades that ended before a session.
    static SweepMeasures Until(IReadOnlyList<SweepPick> picks, SweepDesign design, DialSetting setting, ConditionSetting conditions, int before, int shift, int nights)
    {
        var tally = SweepStages.Tally.OneExit(design.ExitIndex, SweepStages.SessionsOf(picks));

        WalkUntil(picks, setting, conditions, tally, before, shift);

        return SweepMeasures.Of(tally.SumsOf(0), tally.NightsOf(0), nights);
    }

    // A setting's trades over the whole history, one open trade a stock, each the walk counts a result for.
    public static IReadOnlyList<PulledBack> Trades(IReadOnlyList<SweepPick> picks, SweepDesign design, DialSetting setting, ConditionSetting conditions)
    {
        var exit = design.ExitIndex;
        var stopFloor = SweepStages.StopFloor(setting);
        var trades = new List<PulledBack>();
        var (name, openUntil) = (-1, -1);

        foreach (var pick in picks)
        {
            if (pick.Name != name)
            {
                name = pick.Name;
                openUntil = -1;
            }

            if (!pick.Corner.Passes(setting) || !conditions.Passes(pick.Readings) || pick.Session <= openUntil)
            {
                continue;
            }

            var plan = pick.Plan;

            openUntil = pick.Session + plan.Ends[exit];

            if (plan.Code[exit] is SweepPlanOutcomes.Win or SweepPlanOutcomes.Loss or SweepPlanOutcomes.Unresolved && !float.IsNaN(plan.Multiple[exit]) && plan.IsATradeAt(exit, stopFloor))
            {
                var edge = plan.EdgeAt(exit);

                trades.Add(new PulledBack(pick.Session, openUntil, float.IsNaN(edge) ? null : edge));
            }
        }

        return trades;
    }

    // The pullback's search on an index's own history as a procedure: inside each fold the search over every setting of its
    // grid on the trades that ended before the fold's year, its floors in proportion to the years it learned on, its
    // proposal refined and extended as the search's own; the fold's second stage over its own ten strongest settings, each
    // level alone and the survivors crossed with and without the profit and interest-cover check; and the fold choosing
    // the stronger of the proposal and the crossings meeting the floors in proportion. The pool is read charged one round
    // trip.
    public static LoopProposalRead Pullback(LoopRead read, IndexSweepRunner.OwnPullbackRead own, Action<string> progress)
    {
        var space = own.Space;
        var parallelism = Environment.ProcessorCount;
        var plain = own.Under([]);
        var newest = read.Calendar.Length;

        PullbackChoice? ChooseIn(LoopFold fold)
        {
            var years = Math.Min(read.LearningYears(fold), SweepFigures.Years);
            var before = Array.FindIndex(read.Calendar, session => session >= fold.TestFrom) is var first && first >= 0 ? first : newest;
            var shift = SweepFigures.Years - years;
            var nights = Math.Max(1, before - read.FirstScored);
            var picks = plain.Picks;
            var search = new SweepDesignSearch(
                own.Design,
                nights,
                SweepStages.SessionsOf(picks),
                space,
                (point, tally) =>
                {
                    WalkUntil(picks, space.Setting(point), space.Conditions(point), tally, before, shift);

                    return InProportion(SweepMeasures.Summary(tally.SumsOf(0), tally.NightsOf(0), nights), years);
                },
                point => Until(picks, own.Design, space.Setting(point), space.Conditions(point), before, shift, nights));

            search.EvaluateCoarse(ConditionSetting.Off, parallelism);
            search.EvaluateSample(IndexSweepRunner.SearchBudget, SweepSearch.Seed, parallelism);
            search.FixTheLine(SweepSearch.PlateauMargin);

            var live = Until(picks, own.Design, own.BaseSetting, ConditionSetting.Off, before, shift, nights);
            var (proposal, _) = search.Propose(search.LeadersOf(SweepSearch.Leaders), live);

            if (proposal is not null)
            {
                proposal = search.Refine(proposal, live, []);
                proposal = search.Extend(proposal, live, [], [], [], IndexSweepRunner.LooksBeyond);
            }

            // The floors in proportion, read on the fold's own years as they fell.
            var (tradeFloor, yearFloor) = LoopProcedures.Floors(years);

            bool Meets(SweepMeasures measures) => measures.Scored >= tradeFloor && measures.YearEdge.Count(edge => edge is > 0) >= yearFloor;

            SweepMeasures Learned((SweepDesign Held, List<SweepPick> Picks) under, int[] point, IReadOnlyList<DialLevel<SweepCandidate>> levels) =>
                Until(under.Picks, under.Held, own.SettingUnder(point, levels), space.Conditions(point), before, 0, nights);

            string Words(int[] point, IReadOnlyList<DialLevel<SweepCandidate>> levels) =>
                space.Describe(point) + (levels.Count == 0 ? string.Empty : " with " + string.Join("; ", levels.Select(level => level.Dial + ", " + level.Name)));

            var choices = new List<PullbackChoice>();

            if (proposal is not null && Learned(plain, proposal.Point, []) is var proposed && Meets(proposed))
            {
                choices.Add(new PullbackChoice(proposal.Point, [], proposed, Words(proposal.Point, [])));
            }

            var ten = search.Strongest(SweepDials.Settings, SweepDials.TradesHeld);
            var without = ten.Select(point => Learned(plain, point, [])).ToArray();
            var crossedLevels = own.Levels
                .Select(level =>
                {
                    var under = own.Under([level]);
                    var pairs = ten.Select((point, at) => (Without: without[at], With: Learned(under, point, [level]))).ToArray();
                    var changes = pairs
                        .Select(pair => level.Switch ? Change(pair.Without.AverageMultiple, pair.With.AverageMultiple) : Change(pair.Without.Edge, pair.With.Edge))
                        .OfType<double>()
                        .ToArray();

                    return (Level: level, KeptOn: pairs.Count(pair => KeptOver(pair.Without, pair.With, level.Switch, years)), Change: Median(changes));
                })
                .Where(one => one.KeptOn >= SweepDials.KeptOn)
                .GroupBy(one => one.Level.Dial, StringComparer.Ordinal)
                .Select(dial => dial.OrderByDescending(one => one.KeptOn).ThenByDescending(one => one.Change ?? double.MinValue).First().Level)
                .ToArray();

            void Cross(IReadOnlyList<DialLevel<SweepCandidate>> levels)
            {
                var under = own.Under(levels);

                foreach (var point in ten)
                {
                    if (Learned(under, point, levels) is var measures && Meets(measures))
                    {
                        choices.Add(new PullbackChoice(point, levels, measures, Words(point, levels)));
                    }
                }
            }

            if (crossedLevels.Length > 0)
            {
                Cross(crossedLevels);
            }

            Cross([.. crossedLevels.Where(level => level.Dial != own.Cover.Dial), own.Cover]);

            return choices
                .OrderByDescending(one => one.Measures.Edge ?? double.MinValue)
                .ThenBy(one => one.Levels.Count)
                .ThenBy(one => one.Words, StringComparer.Ordinal)
                .FirstOrDefault();
        }

        IReadOnlyList<PulledBack> TradesOf(int[] point, IReadOnlyList<DialLevel<SweepCandidate>> levels)
        {
            var under = levels.Count == 0 ? plain : own.Under(levels);

            return Trades(under.Picks, under.Held, own.SettingUnder(point, levels), space.Conditions(point));
        }

        // A trade still open when the history ends is no unit, as a swing family's open trade is none.
        IReadOnlyList<(int Entry, double? Edge)> Units(IReadOnlyList<PulledBack> trades) =>
            [.. trades.Where(one => one.End < newest).Select(one => (one.Entry, one.Edge))];

        var folds = new List<(LoopFold Fold, PullbackChoice? Choice)>();

        foreach (var fold in read.Folds)
        {
            progress(FormattableString.Invariant($"the pullback's fold for {fold.Year}: searching its grid on the years before it"));
            folds.Add((fold, ChooseIn(fold)));
        }

        progress("the pullback's search on all finished data");

        var proposal = ChooseIn(read.AllFinished);
        var current = TradesOf(own.BasePoint, []);
        var chosen = folds.Select(one => one.Choice is null ? null : TradesOf(one.Choice.Point, one.Choice.Levels)).ToArray();
        var cap = chosen.OfType<IReadOnlyList<PulledBack>>().Append(current).SelectMany(one => one).Select(one => one.End - one.Entry).DefaultIfEmpty(0).Max();

        static bool SameLevels(PullbackChoice one, PullbackChoice other) =>
            one.Levels.Select(level => level.Dial + "|" + level.Name).Order(StringComparer.Ordinal).SequenceEqual(other.Levels.Select(level => level.Dial + "|" + level.Name).Order(StringComparer.Ordinal));

        return new LoopProposalRead(
            SetupFamilies.Pullback,
            PullbackSearch,
            proposal?.Words,
            space.Describe(own.BasePoint),
            LoopProcedures.Risks,
            cap,
            [.. folds.Select(one => (one.Fold, one.Choice?.Words))],
            proposal is null ? 0 : folds.Count(one => one.Choice is { } choice && SameLevels(choice, proposal) && choice.Point.Zip(proposal.Point).All(pair => Math.Abs(pair.First - pair.Second) <= 1)),
            LoopProcedures.Evidence(read.Folds, read.Calendar, [.. chosen.Select(one => one is null ? null : Units(one))], Units(current)));
    }

    // One book a heavyweights procedure reads: its key, how many dials it moves from the provisional setting, and the book.
    sealed record GridBook(string Key, int Changes, HeavyweightBook Book);

    // The sector heavyweights' two designs on an index's own history as two procedures over their full grids, each read
    // against the index's provisional book, design (a)'s first, each book's months its unit.
    public static IReadOnlyList<LoopProposalRead> Heavyweights(LoopRead read, HeavyweightLay lay, Action<string> progress)
    {
        var provisional = IndexHeavyweights.Provisional;
        var current = lay.BookFrom(lay.RebalancesA(provisional, IndexQuality.Profit, 1m), provisional.Exit, "the provisional book");
        var designA = (
            from variant in IndexSweepRunner.HeavyweightVariants
            from setting in HeavyweightSweep.Settings.Where(setting => setting.Sector == HeavyweightSectorReturn.Members && setting.Period == HeavyweightPeriod.Month)
            select (Variant: variant, Setting: setting)).ToArray();
        var designB = (
            from window in IndexSweepRunner.LeadWindows
            from kept in IndexSweepRunner.IndustriesKept
            from perIndustry in IndexSweepRunner.MembersPerIndustry
            from variant in IndexSweepRunner.HeavyweightVariants
            select (Window: window, Kept: kept, PerIndustry: perIndustry, Variant: variant)).ToArray();

        progress(FormattableString.Invariant($"walking the heavyweights' {designA.Length} design (a) and {designB.Length} design (b) settings on the {DecisionCards.NameOf(read.Index)} over the whole history"));

        var booksA = new GridBook[designA.Length];
        var booksB = new GridBook[designB.Length];

        Parallel.For(0, designA.Length, at =>
        {
            var (variant, setting) = designA[at];
            var key = IndexSweepRunner.VariantKey(variant.Quality, variant.Floors) + setting.Key;

            booksA[at] = new GridBook(key, setting.Changes(HeavyweightSweep.Provisional), lay.BookFrom(lay.RebalancesA(setting, variant.Quality, variant.Floors), setting.Exit, key));
        });

        Parallel.For(0, designB.Length, at =>
        {
            var (window, kept, perIndustry, variant) = designB[at];
            var key = IndexSweepRunner.FollowersKey(window, kept, perIndustry, variant.Quality, variant.Floors);

            booksB[at] = new GridBook(key, 0, lay.BookFrom(lay.RebalancesB(window, kept, perIndustry, variant.Quality, variant.Floors), HeavyweightExit.Drop, key));
        });

        return [Book(read, booksA, current, DesignA), Book(read, booksB, current, DesignB)];
    }

    static LoopProposalRead Book(LoopRead read, IReadOnlyList<GridBook> books, HeavyweightBook current, string design)
    {
        // A fold's choice: the best edge among the settings meeting the floors in proportion to its years, ties to the
        // fewest dials moved from the provisional setting and then the key, as the sweep proposes.
        GridBook? ChooseIn(LoopFold fold)
        {
            var (trades, yearsBeating) = LoopProcedures.Floors(read.LearningYears(fold));

            return books
                .Select(book => (Book: book, Figures: HeavyweightSweep.Figures(book.Key, new FoldView<HeavyweightTrade>(fold, book.Book.Costed, trade => read.Calendar[trade.Entry], trade => trade.End is { } end ? read.Calendar[end] : null).Learning)))
                .Where(one => one.Figures.Edge is not null && one.Figures.Trades >= trades && one.Figures.YearsBeating >= yearsBeating)
                .OrderByDescending(one => one.Figures.Edge)
                .ThenBy(one => one.Book.Changes)
                .ThenBy(one => one.Book.Key, StringComparer.Ordinal)
                .Select(one => one.Book)
                .FirstOrDefault();
        }

        var chosen = read.Folds.Select(fold => (Fold: fold, Book: ChooseIn(fold))).ToArray();
        var proposal = ChooseIn(read.AllFinished);

        return new LoopProposalRead(
            HeavyweightRule.Name,
            design,
            proposal?.Key,
            RuleReplay.HeavyweightWords(IndexHeavyweights.Provisional),
            LoopProcedures.Points,
            BookMonths.LongestMonth,
            [.. chosen.Select(one => (one.Fold, one.Book?.Key))],
            proposal is null ? 0 : chosen.Count(one => one.Book?.Key == proposal.Key),
            LoopProcedures.BookEvidence(read.Folds, read.Calendar, [.. chosen.Select(one => one.Book?.Book.Months)], current.Months));
    }
}
