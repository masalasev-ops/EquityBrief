using System.Collections.Concurrent;
using EquityBrief.Core.Families;
using EquityBrief.Core.Loop;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Readings;
using EquityBrief.Core.Sweep;
using EquityBrief.Worker.Cards;
using EquityBrief.Worker.Indices;
using EquityBrief.Worker.Sweep;

namespace EquityBrief.Worker.Loop;

// What every procedure on an index is run over: the history as it stood, its series, sessions and members, the
// companies and their income, the calendar, the first session scored, the folds and the session the test span opens on.
public sealed record LoopRead(
    string Index,
    SweepHistoryInputs Inputs,
    SweepSeries[] Series,
    IReadOnlyList<SweepColumns.Session> Sessions,
    SweepBenchmark.Members Members,
    HeavyweightHistory Companies,
    IReadOnlyDictionary<string, IReadOnlyList<FiledIncome>> Income,
    DateOnly[] Calendar,
    int FirstScored,
    IReadOnlyList<LoopFold> Folds)
{
    public int Newest => Calendar.Length - 1;

    public int Opens => Array.IndexOf(Calendar, Folds[0].TestFrom);

    public string Ticker(int name) => Series[name].Name.Ticker;

    // The years a fold learns on, from the first scored year to the one before its own.
    public int LearningYears(LoopFold fold) => fold.TestFrom.Year - SweepColumns.FirstScored.Year;

    // The folds and, last, the procedure run on all finished data: what had ended by the newest session.
    public LoopFold AllFinished => new(Calendar[^1].Year + 1, Calendar[^1].AddDays(1), Calendar[^1].AddDays(1), false);

    public int YearsHeld => Calendar[^1].Year - SweepColumns.FirstScored.Year + 1;
}

// One proposal a procedure brought: its family and name, the change in words or none where the procedure run on all
// finished data chose no setting, the rule today in words, its unit, the cap a block of its waits, the setting each
// fold's learning years chose or none, how many folds chose within a grid step of the proposal, and its evidence.
public sealed record LoopProposalRead(
    string Family,
    string Proposal,
    string? Words,
    string Current,
    string Unit,
    int Cap,
    IReadOnlyList<(LoopFold Fold, string? Chosen)> Folds,
    int StableFolds,
    LoopEvidence Evidence)
{
    // What an engine found that the proposal answers, in words; none for a search.
    public string? Finding { get; init; }

    // The change in the form an approval applies it, none where the procedure chose no setting.
    // see: An approved change is applied before the next night from the night's own build, on the index it was approved on alone
    public LoopChange? Change { get; init; }

    // The rule today's units over the test years as each entered and its edge, the live alarm's reference.
    // see: The live alarm flags a rule whose edge stood under its reference's fifth percentile two periods running
    public IReadOnlyList<AlarmUnit>? Reference { get; init; }
}

// Part 0's procedures as the tester runs them inside each fold: the breakout's and the drift's grids on the S&P 400 and
// 600, the drift at the stop floor its runbook names and on the S&P 400 with its last two years read, and the sector
// heavyweights' six settings in each design. Each setting is walked once over the whole history, since a walk's trades
// before a session are the same whatever comes after it; a fold then reads only the trades that ended before its year
// through its view, chooses as the search chooses with the family floors in proportion to its learning years, and is
// tested on the trades its choice and the current rule entered in its year.
// see: A change is adopted only on test years the proposal never saw, and a search is judged as a procedure run year by year
// see: A search run inside a fold reads the family floors in proportion to the years it learns on
public static class LoopProcedures
{
    public const string Risks = RuleReplay.Risks;

    public const string Points = "points";

    public const string Grid = "the grid";

    public const string DesignA = "the six settings, design (a)";

    public const string DesignB = "the six settings, design (b)";

    // The drift's stop floor in typical moves, as the runbook runs its search.
    public const double DriftStopFloor = 1;

    // One trade a setting kept: its listing's session and the session it ended on, none while open, and the trade.
    sealed record Walked(int Entry, int? Exit, FamilyTrade Trade)
    {
        public double? Edge => Trade.Result is { } result && !double.IsNaN(Trade.Benchmark) ? result - Trade.Benchmark : null;
    }

    // The floors a fold's search reads: the family's trades and years in proportion to the years it learns on, each
    // rounded up.
    public static (int Trades, int Years) Floors(int learningYears) =>
        (Up(FamilySweep.TradeFloor * learningYears, SweepFigures.Years), Up(FamilySweep.YearsBeating * learningYears, SweepFigures.Years));

    static int Up(int numerator, int denominator) => (numerator + denominator - 1) / denominator;

    // The edge over a window's last two years together, each year's edge weighted by its trades, none where neither
    // holds one.
    public static double? LastTwoYears(int[] trades, double?[] edges, int years)
    {
        var (sum, count) = (0.0, 0);

        for (var year = Math.Max(0, years - 2); year < Math.Min(years, edges.Length); year++)
        {
            if (edges[year] is { } edge && trades[year] > 0)
            {
                sum += edge * trades[year];
                count += trades[year];
            }
        }

        return count > 0 ? sum / count : null;
    }

    // The breakout's or the drift's grid on an index as a procedure.
    public static LoopProposalRead Swing(LoopRead read, string family, Action<string> progress)
    {
        var drift = family == DriftRule.Name;
        var floor = drift ? DriftStopFloor : 0;
        var lastTwo = drift && read.Index == IndexSweepRunner.DriftLastTwoYearsIndex;
        var adapter = FamilySweepRunner.For(family, read.Series, read.Sessions, read.Members, read.FirstScored, read.Calendar, floor);
        var currentAdapter = floor > 0 ? FamilySweepRunner.For(family, read.Series, read.Sessions, read.Members, read.FirstScored, read.Calendar) : adapter;
        int[] provisional = family == BreakoutRule.Name ? [.. IndexNightRead.BreakoutAsFrozen] : [.. IndexNightRead.DriftAsFrozen];
        var settings = adapter.Grid.Settings;
        var walked = new IReadOnlyList<Walked>[settings.Count];

        // One setting at a time, as the index sweep walks them, since the adapter keeps its exits and benchmarks as it
        // reads them.
        progress(FormattableString.Invariant($"walking the {family}'s {settings.Count} setting(s) on the {DecisionCards.NameOf(read.Index)} over the whole history"));

        for (var at = 0; at < settings.Count; at++)
        {
            walked[at] = Walk(read, adapter, settings[at]);
        }

        var current = Walk(read, currentAdapter, provisional);
        var nights = read.Calendar.Length - read.FirstScored;

        // Each setting's figures over what a fold learns on, read through the fold's view.
        int[]? ChooseIn(LoopFold fold) => Choose(
            adapter.Grid,
            [
                .. settings.Select((setting, at) =>
                {
                    var learning = new FoldView<Walked>(fold, walked[at], one => read.Calendar[one.Entry], one => one.Exit is { } exit ? read.Calendar[exit] : null).Learning;

                    return (setting, FamilySweep.Figures(adapter.Grid.Key(setting), [.. learning.Select(one => one.Trade)], nights));
                }),
            ],
            read.LearningYears(fold),
            lastTwo);

        var chosen = read.Folds.Select(fold => (Fold: fold, Setting: ChooseIn(fold))).ToArray();
        var proposal = ChooseIn(read.AllFinished);
        var placeOf = settings.Select((setting, place) => (setting, place)).ToDictionary(pair => adapter.Grid.Key(pair.setting), pair => pair.place, StringComparer.Ordinal);

        static IReadOnlyList<(int Entry, double? Edge)> Units(IReadOnlyList<Walked> trades) => [.. trades.Select(one => (one.Entry, one.Edge))];

        var floorWords = floor > 0 ? FormattableString.Invariant($", its stop held at least {floor:0.##} typical move under the buy") : string.Empty;
        var cap = walked.SelectMany(one => one).Concat(current).Select(one => one.Trade.Listing.Cap).DefaultIfEmpty(0).Max();

        return new LoopProposalRead(
            family,
            Grid,
            proposal is null ? null : RuleReplay.SwingWords(family, proposal) + floorWords,
            RuleReplay.SwingWords(family, provisional),
            Risks,
            cap,
            [.. chosen.Select(one => (one.Fold, one.Setting is null ? null : RuleReplay.SwingWords(family, one.Setting) + floorWords))],
            proposal is null ? 0 : chosen.Count(one => one.Setting is { } setting && setting.Zip(proposal).All(pair => Math.Abs(pair.First - pair.Second) <= 1)),
            Evidence(read.Folds, read.Calendar, [.. chosen.Select(one => one.Setting is null ? null : Units(walked[placeOf[adapter.Grid.Key(one.Setting)]]))], Units(current)))
        {
            Change = proposal is null ? null : LoopChange.OfSetting(proposal, adapter.Grid.Key(proposal), floor),
            Reference = Reference(read, Units(current)),
        };
    }

    // The rule today's units that entered over the test years and hold an edge, as each entered: the live alarm's
    // reference.
    // see: The live alarm flags a rule whose edge stood under its reference's fifth percentile two periods running
    public static IReadOnlyList<AlarmUnit> Reference(LoopRead read, IReadOnlyList<(int Entry, double? Edge)> current) =>
    [
        .. current
            .Where(one => one.Edge is not null && read.Folds.Any(fold => LoopFolds.Tests(fold, read.Calendar[one.Entry])))
            .Select(one => new AlarmUnit(read.Calendar[one.Entry], one.Edge!.Value)),
    ];

    // A book's holdings that entered over the test years and hold an edge after their round trip, as each entered.
    public static IReadOnlyList<AlarmUnit> Reference(LoopRead read, HeavyweightBook book) =>
    [
        .. book.Costed
            .Where(trade => trade.Edge is not null && read.Folds.Any(fold => LoopFolds.Tests(fold, read.Calendar[trade.Entry])))
            .Select(trade => new AlarmUnit(read.Calendar[trade.Entry], trade.Edge!.Value)),
    ];

    // The setting a search chooses from what a fold learns on: the best edge among the settings meeting the floors in
    // proportion to its years, the S&P 400 drift's last two of them above nothing as well, ties to the fewest dials moved
    // from the provisional setting and then the key; none where no setting meets them.
    public static int[]? Choose(FamilyGrid grid, IReadOnlyList<(int[] Setting, FamilyFigures Figures)> read, int years, bool lastTwo)
    {
        var (trades, yearsBeating) = Floors(years);

        return read
            .Where(one => one.Figures.Edge is not null
                && one.Figures.Trades >= trades
                && one.Figures.YearsBeating >= yearsBeating
                && (!lastTwo || LastTwoYears(one.Figures.YearTrades, one.Figures.YearEdge, years) > 0))
            .OrderByDescending(one => one.Figures.Edge)
            .ThenBy(one => grid.Changes(one.Setting))
            .ThenBy(one => one.Figures.Key, StringComparer.Ordinal)
            .Select(one => one.Setting)
            .FirstOrDefault();
    }

    // A swing procedure's evidence: in each fold's year the trades its choice entered, or the rule's own where it chose
    // none, against the rule's, each closed trade a unit at the session it was entered on. A fold that chose none adds
    // a unit and its twin on each side, so nothing either way.
    public static LoopEvidence Evidence(
        IReadOnlyList<LoopFold> folds,
        IReadOnlyList<DateOnly> calendar,
        IReadOnlyList<IReadOnlyList<(int Entry, double? Edge)>?> chosen,
        IReadOnlyList<(int Entry, double? Edge)> current)
    {
        var units = new List<PairedUnit>();
        var (currentResults, proposedResults) = (new List<double>(), new List<double>());

        for (var at = 0; at < folds.Count; at++)
        {
            var fold = folds[at];

            foreach (var (entry, edge) in (chosen[at] ?? current).Where(one => LoopFolds.Tests(fold, calendar[one.Entry]) && one.Edge is not null))
            {
                units.Add(new PairedUnit(entry, edge, null));
                proposedResults.Add(edge!.Value);
            }

            foreach (var (entry, edge) in current.Where(one => LoopFolds.Tests(fold, calendar[one.Entry]) && one.Edge is not null))
            {
                units.Add(new PairedUnit(entry, null, edge));
                currentResults.Add(edge!.Value);
            }
        }

        return new LoopEvidence(false, units, LoopJudge.Years(folds, calendar, units), currentResults, proposedResults);
    }

    // The heavyweights' setting a fold chooses of the six: the best edge among those meeting the floors in proportion
    // to its years, ties to the six's own order; none where none meets them.
    public static HeavyweightSixSetting? ChooseSix(IReadOnlyList<(HeavyweightSixSetting Setting, HeavyweightFigures Figures)> read, int years)
    {
        var (trades, yearsBeating) = Floors(years);

        return read
            .Where(one => one.Figures.Edge is not null && one.Figures.Trades >= trades && one.Figures.YearsBeating >= yearsBeating)
            .OrderByDescending(one => one.Figures.Edge)
            .ThenBy(one => one.Setting.Place)
            .Select(one => one.Setting)
            .FirstOrDefault();
    }

    // A book's evidence: in each fold's year the months of the book its choice keeps, or the rule's own where it chose
    // none, paired with the rule's months.
    public static LoopEvidence BookEvidence(
        IReadOnlyList<LoopFold> folds,
        IReadOnlyList<DateOnly> calendar,
        IReadOnlyList<IReadOnlyList<(int Opens, double? Edge)>?> chosen,
        IReadOnlyList<(int Opens, double? Edge)> current)
    {
        var units = new List<PairedUnit>();
        var (currentResults, proposedResults) = (new List<double>(), new List<double>());

        for (var at = 0; at < folds.Count; at++)
        {
            var fold = folds[at];

            foreach (var unit in BookMonths.Paired(chosen[at] ?? current, current).Where(unit => LoopFolds.Tests(fold, calendar[unit.Session])))
            {
                units.Add(unit);

                if (unit.Proposed is { } proposed)
                {
                    proposedResults.Add(proposed);
                }

                if (unit.Current is { } held)
                {
                    currentResults.Add(held);
                }
            }
        }

        return new LoopEvidence(true, units, LoopJudge.Years(folds, calendar, units), currentResults, proposedResults);
    }

    // A setting's trades over the whole history, kept where they clear the index's floors and gate, each result after
    // its round trip and its exit session beside it.
    static IReadOnlyList<Walked> Walk(LoopRead read, FamilySweepRunner.Adapter adapter, int[] setting)
    {
        var tickers = read.Series.Select(one => one.Name.Ticker).ToArray();
        var held = new ConcurrentDictionary<(int Name, int Session), int>();

        (double? Result, int Sessions) Exit(FamilyListing listing)
        {
            var walked = adapter.Exit(listing);

            held[(listing.Name, listing.Session)] = walked.Sessions;

            return walked;
        }

        int YearOf(int session) => read.Calendar[session].Year - SweepColumns.FirstScored.Year;

        var listings = adapter.Listings(setting)
            .Where(listing => IndexSweepRunner.Clears(read.Index, read.Series[listing.Name], listing.Bar, read.Income.GetValueOrDefault(tickers[listing.Name]) ?? []));
        var trades = FamilySweep.Walk(listings, tickers, YearOf, Exit, adapter.Benchmark);

        return
        [
            .. trades.Select(trade =>
            {
                var listing = trade.Listing;
                var costed = trade.Result is { } result
                    ? trade with { Result = result - IndexSweepRunner.CostInRisk(read.Series[listing.Name], listing, result, read.Companies, 1) }
                    : trade;

                return new Walked(listing.Session, trade.Result is null ? null : listing.Session + held[(listing.Name, listing.Session)], costed);
            }),
        ];
    }

    // The sector heavyweights' six settings of each design on an index as two procedures, read against the index's
    // provisional book, design (a)'s first, each book's months its unit.
    public static IReadOnlyList<LoopProposalRead> Heavyweights(LoopRead read, HeavyweightLay lay, Action<string> progress)
    {
        var current = lay.Book(HeavyweightSix.DesignA[0], designA: true);

        progress(FormattableString.Invariant($"walking the heavyweights' twelve settings on the {DecisionCards.NameOf(read.Index)} over the whole history"));

        return
        [
            Six(read, lay, current, HeavyweightSix.DesignA, designA: true),
            Six(read, lay, current, HeavyweightSix.DesignB, designA: false),
        ];
    }

    static LoopProposalRead Six(LoopRead read, HeavyweightLay lay, HeavyweightBook current, IReadOnlyList<HeavyweightSixSetting> six, bool designA)
    {
        var books = six.Select(setting => lay.Book(setting, designA)).ToArray();

        // Each setting's figures over what a fold learns on, read through the fold's view.
        HeavyweightSixSetting? ChooseIn(LoopFold fold) => ChooseSix(
            [
                .. books.Select(book =>
                {
                    var learning = new FoldView<HeavyweightTrade>(fold, book.Costed, trade => read.Calendar[trade.Entry], trade => trade.End is { } end ? read.Calendar[end] : null).Learning;

                    return (book.Setting, HeavyweightSweep.Figures(book.Setting.Key, learning));
                }),
            ],
            read.LearningYears(fold));

        var chosen = read.Folds.Select(fold => (Fold: fold, Setting: ChooseIn(fold))).ToArray();
        var proposal = ChooseIn(read.AllFinished);
        var design = designA ? DesignA : DesignB;

        return new LoopProposalRead(
            HeavyweightRule.Name,
            design,
            proposal is null ? null : Words(proposal, designA),
            RuleReplay.HeavyweightWords(IndexHeavyweights.Provisional),
            Points,
            BookMonths.LongestMonth,
            [.. chosen.Select(one => (one.Fold, one.Setting is null ? null : Words(one.Setting, designA)))],
            proposal is null ? 0 : chosen.Count(one => one.Setting?.Place == proposal.Place),
            BookEvidence(read.Folds, read.Calendar, [.. chosen.Select(one => one.Setting is null ? null : books[one.Setting.Place - 1].Months)], current.Months))
        {
            Change = proposal is null ? null : new LoopChange(null, proposal.Key, null, LoopChange.NoHooks),
            Reference = Reference(read, current),
        };
    }

    static string Words(HeavyweightSixSetting setting, bool designA) =>
        FormattableString.Invariant($"design ({(designA ? IndexSweepRunner.DesignA : IndexSweepRunner.DesignB)}), {setting.Words}");
}
