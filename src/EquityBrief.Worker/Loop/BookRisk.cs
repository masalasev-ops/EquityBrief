using System.Globalization;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Families;
using EquityBrief.Core.Loop;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Sweep;
using EquityBrief.Worker.Families;
using EquityBrief.Worker.Sweep;

namespace EquityBrief.Worker.Loop;

// On the operator's answer of 2026-11-14 to item 15 a book's picks measured in the unit every other card reads. A pick's result
// is its return from its fill at the open after its buy to its sale, less its round trip, for every dollar its stop risked from
// the buy's close. Its benchmark is the same for the index's other members on the buy's session, each bought at the same open
// with its own stop and take-profit at the same moves and multiple and sold at the first close past either or on the session
// the pick was sold, uncharged as the book's own benchmark is. A pick's edge is its result less its benchmark's mean.
// see: Every pick takes profit at its own sale's price, a price it turned down at before or a multiple of its risk
public static class BookRisk
{
    // Each ended pick's buy session and its edge per dollar risked, none where its stop or its fill is not read or no other
    // member of the index is.
    public static IReadOnlyList<(int Entry, double? Edge)> Edges(HeavyweightTape tape, IReadOnlyList<SweepSeries> series, IReadOnlyList<HeavyweightTrade> costed, (double Moves, double Multiple) stops) =>
        Edges(tape, costed, (name, entry) => Placed(tape, series, name, entry, stops));

    // The same over a placement handed in: each member's risk, stop, take-profit and fill at a buy session, none where none is.
    public static IReadOnlyList<(int Entry, double? Edge)> Edges(HeavyweightTape tape, IReadOnlyList<HeavyweightTrade> costed, Func<int, int, (double Risk, double Stop, double? TakeProfit, double Fill)?> placedAt)
    {
        var ended = costed.Where(trade => trade.End is not null && trade.Result is not null).ToArray();
        var edges = new (int Entry, double? Edge)[ended.Length];

        Parallel.ForEach(ended.Select((trade, at) => (trade, at)).GroupBy(pair => pair.trade.Entry), group =>
        {
            var entry = group.Key;
            var last = group.Max(pair => pair.trade.End!.Value);
            var placed = new Dictionary<int, (double Risk, double Stop, double? TakeProfit, double Fill, int? Hit)>();

            for (var name = 0; name < tape.Tickers.Length; name++)
            {
                if (tape.Member[name][entry] && placedAt(name, entry) is { } one)
                {
                    placed[name] = (one.Risk, one.Stop, one.TakeProfit, one.Fill, HitBy(tape, name, entry, last, one.Stop, one.TakeProfit));
                }
            }

            foreach (var (trade, at) in group)
            {
                var end = trade.End!.Value;
                var others = placed.Where(pair => pair.Key != trade.Name).Select(pair => ResultOf(tape, pair.Key, entry, end, pair.Value)).OfType<double>().ToArray();

                edges[at] = (entry, placed.TryGetValue(trade.Name, out var own) && others.Length > 0 ? (trade.Result!.Value / own.Risk) - others.Average() : null);
            }
        });

        return edges;
    }

    // A member's stop and take-profit placed at a session's close as the book places a holding's, its risk a fraction of that
    // close, and its fill at the next open over that close; none where the index does not hold it, it has no close, no stop is
    // placed or no open follows.
    static (double Risk, double Stop, double? TakeProfit, double Fill)? Placed(HeavyweightTape tape, IReadOnlyList<SweepSeries> series, int name, int entry, (double Moves, double Multiple) stops)
    {
        var close = tape.Close[name][entry];

        if (!tape.Member[name][entry] || !(close > 0) || tape.ExitsAt(name, entry, stops.Moves, stops.Multiple) is not { } exits || !(exits.Stop < close))
        {
            return null;
        }

        var bar = Array.BinarySearch(series[name].SessionAt, entry);

        if (bar < 0 || series[name].Bars[bar].Close <= 0m || OpenFills.OpenAfter(series[name], bar) is not { } open)
        {
            return null;
        }

        return ((close - exits.Stop) / close, exits.Stop, exits.TakeProfit, open / Statistic.FromPrice(series[name].Bars[bar].Close));
    }

    // The first session after the buy, through the last, whose close is past the stop or the take-profit while the index holds
    // the stock; none where none is.
    static int? HitBy(HeavyweightTape tape, int name, int entry, int last, double stop, double? takeProfit)
    {
        for (var session = entry + 1; session <= last && session < tape.Calendar.Length; session++)
        {
            if (!tape.Member[name][session])
            {
                return null;
            }

            if (!double.IsNaN(tape.Close[name][session]) && TakeProfit.EndOf(tape.Close[name][session], stop, takeProfit) is not null)
            {
                return session;
            }
        }

        return null;
    }

    // A member's result for every dollar risked, bought at the open after the pick's buy and sold at its stop or take-profit's
    // close where one comes by the pick's sale, else at its last close as a member by then.
    static double? ResultOf(HeavyweightTape tape, int name, int entry, int end, (double Risk, double Stop, double? TakeProfit, double Fill, int? Hit) placed)
    {
        var sold = placed.Hit is { } hit && hit <= end ? hit : tape.LastMemberClose[name][end];

        return sold >= entry && !double.IsNaN(tape.Close[name][sold])
            ? Core.Ledger.TradeFills.AtOpen((tape.Close[name][sold] / tape.Close[name][entry]) - 1.0, 1, 1, placed.Fill) / placed.Risk
            : null;
    }

    // The mean edge per dollar risked over the picks whose buy a session test holds, and how many.
    public static (double? Mean, int Picks) Mean(IReadOnlyList<(int Entry, double? Edge)> edges, Func<int, bool> on)
    {
        var kept = edges.Where(one => one.Edge is not null && on(one.Entry)).Select(one => one.Edge!.Value).ToArray();

        return (kept.Length > 0 ? kept.Average() : null, kept.Length);
    }

    // A book's line: each stop and take-profit's edge per dollar risked from its family's first test year on the index to 2018,
    // from 2019, and over both, the one chosen on the years before 2019, and the box's sentence at it.
    public static string Line(string family, string rule, string index, IReadOnlyList<DateOnly> calendar, IReadOnlyList<(string Words, IReadOnlyList<(int Entry, double? Edge)> Edges)> choices)
    {
        var first = LongTest.FirstTestYear(family, index);
        var from = Core.History.LongLedger.StandingFrom.Year;
        bool Early(int entry) => calendar[entry].Year >= first && calendar[entry].Year < from;
        bool Late(int entry) => calendar[entry].Year >= from;
        bool Both(int entry) => calendar[entry].Year >= first;
        static string Shown((double? Mean, int Picks) figure) => figure.Mean is { } mean ? FormattableString.Invariant($"{mean:+0.000;-0.000} over {figure.Picks}") : FormattableString.Invariant($"none over {figure.Picks}");
        var years = first < from ? FormattableString.Invariant($"from {first} to {from - 1}") : FormattableString.Invariant($"before {from}");
        var chosen = choices.Where(one => Mean(one.Edges, Early).Mean is not null).OrderByDescending(one => Mean(one.Edges, Early).Mean).ToArray();

        return FormattableString.Invariant($"book risk: {family}, {rule}, per $1 risked against the index's other stocks bought the same way: ")
            + string.Join("; ", choices.Select(one => FormattableString.Invariant($"{one.Words} {Shown(Mean(one.Edges, Early))} {years}, {Shown(Mean(one.Edges, Late))} from {from}, {Shown(Mean(one.Edges, Both))} from {first}")))
            + (chosen is [var best, ..]
                ? FormattableString.Invariant($"; chosen {years}: {best.Words}; ") + Box(best.Edges, calendar, first)
                : FormattableString.Invariant($"; no choice holds a pick {years}") + (first < from ? string.Empty : FormattableString.Invariant($", its family's first test year on the index being {first}")));
    }

    // The record box's sentence for a book at one stop and take-profit, from the first year it is judged and holds a pick.
    public static string Box(IReadOnlyList<(int Entry, double? Edge)> edges, IReadOnlyList<DateOnly> calendar, int first)
    {
        var kept = edges.Where(one => one.Edge is not null && calendar[one.Entry].Year >= first).ToArray();

        return kept.Length == 0
            ? FormattableString.Invariant($"no pick from {first} to measure")
            : FormattableString.Invariant($"From {kept.Min(one => calendar[one.Entry].Year)} to {calendar[^1].Year}, picks like this made ${kept.Average(one => one.Edge!.Value).ToString("0.00", CultureInfo.InvariantCulture)} more for every $1 risked than buying the index's other stocks the same way, over {kept.Length} picks.");
    }

    // Each live book on the index and its walk at a stop and take-profit or at none, the trades costed and the months, built as the
    // per-rule test builds it; a live S&P 400 or 600 heavyweights rule off its design's six settings is named with no walk.
    public static IEnumerable<(string Family, string Rule, HeavyweightTape? Tape, Func<(double Moves, double Multiple)?, (IReadOnlyList<HeavyweightTrade> Costed, IReadOnlyList<(int Opens, double? Edge)> Months)>? Book)> LiveBooks(
        IReadOnlyList<RegisterRow> standing, string index, HeavyweightLay heavyweights, QualityLay? quality, ImprovingLay? improving)
    {
        foreach (var row in standing.Where(row => FamilyRecords.IsLive(row.Candidate)).OrderBy(row => row.Id))
        {
            var p = CandidateEvaluator.Read(row.Parameters);

            switch (CandidateEvaluators.Find(row.Evaluator))
            {
                case SectorHeavyweightCandidate when index == WalkForwardTester.LargeIndex:
                {
                    var setting = RuleTests.SettingOf(p);
                    var words = row.Rule;

                    yield return (HeavyweightRule.Name, row.Candidate, heavyweights.Tape, stops =>
                    {
                        var book = heavyweights.BookOf(setting, words, stops);

                        return (book.Costed, book.Months);
                    });
                    break;
                }

                case IndexHeavyweightCandidate held when held.Index == index && !FamilyRecords.IsBackground(row.Candidate):
                {
                    var designA = p.GetValueOrDefault(IndexHeavyweightCandidate.DesignParameter) == IndexHeavyweightCandidate.DesignA;
                    var six = p.GetValueOrDefault(IndexRuleCandidate.FloorsParameter, 1) == 1 ? RuleTests.SixOf(p, designA) : null;

                    if (six is null)
                    {
                        yield return (HeavyweightRule.Name, row.Candidate, null, null);
                    }
                    else
                    {
                        yield return (HeavyweightRule.Name, row.Candidate, heavyweights.Tape, stops =>
                        {
                            var book = heavyweights.Book(six, designA, stops);

                            return (book.Costed, book.Months);
                        });
                    }

                    break;
                }

                case QualityCandidate held when held.Index == index && quality is not null:
                {
                    var setting = QualityCandidate.SettingsOf(p);

                    yield return (QualityRule.Name, row.Candidate, quality.Tape, stops =>
                    {
                        var book = quality.Book(setting, stops: stops);

                        return (book.Costed, book.Months);
                    });
                    break;
                }

                case ImprovingCandidate held when held.Index == index && improving is not null:
                {
                    var setting = ImprovingCandidate.SettingsOf(p);

                    yield return (ImprovingRule.Name, row.Candidate, improving.Tape, stops =>
                    {
                        var book = improving.Book(setting, stops: stops);

                        return (book.Costed, book.Months);
                    });
                    break;
                }
            }
        }
    }
}
