using System.Collections.Concurrent;
using EquityBrief.Core.Families;
using EquityBrief.Core.Ledger;
using EquityBrief.Core.Loop;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Readings;
using EquityBrief.Core.Sweep;
using EquityBrief.Worker.Families;
using EquityBrief.Worker.Loop;
using EquityBrief.Worker.Quality;

namespace EquityBrief.Worker.Sweep;

// One member's reading on a session the quality family's history test reads: its scorecard's checks passed and applied
// over its filer's facts as first filed before the session, its valuation's place against its own last twelve quarters,
// whether it closed above its 200-session average, and whether those facts reach every quarter the valuation reads,
// whatever the prices hold.
public readonly record struct QualityReading(int Passed, int Applied, double? Place, bool AboveAverage, bool FactsReach = false);

// One setting's book over the whole history: its holdings as walked, the same bought at the next open after each one's
// round trip, and its months, the tester's unit.
public sealed record QualityBook(QualitySettings Setting, IReadOnlyList<HeavyweightTrade> Trades, IReadOnlyList<HeavyweightTrade> Costed, IReadOnlyList<(int Opens, double? Edge)> Months);

// The quality family's history test on one index: every member as it stood read once at each month's first session from
// the first scored, its scorecard and its valuation worked out from its filer's facts as first filed before the session
// and its closes at the store's scale, and again on each session after it files; each of the 72 settings' books walked
// from those readings as the night's step walks its holdings; each holding bought at the next open and judged after its
// round trip against an equal-weighted hold of the members answered on the night it was chosen over the same sessions.
// It writes nothing to the store and registers nothing.
// see: Quality at a fair price's history test walks the book over the pulled history on 72 settings an index and registers nothing
// see: A quality holding is sold at a reshuffle where it no longer qualifies and at its last close as a member, and is bought at the next open
// see: A quality holding's edge is its return less an equal-weighted hold of its index's members over the same sessions, after its round trip
public sealed class QualityLay
{
    // The one group a reshuffle's choices and the members they are judged against stand under.
    public const string Group = QualityRule.Name;

    // Why a holding the history sold on a filing ended, the words the night's step stores.
    public const string OnAFiling = QualityFamily.OnAFiling;

    // Why a holding the engines' walk sold at a close under its 200-day average ended.
    public const string UnderTheAverage = "under its 200-day average";

    // The average the trend dial reads a close against, the tape's own.
    public const int TrendSessions = 200;

    readonly LoopRead read;
    readonly HeavyweightTape tape;
    readonly IReadOnlyList<int> reshuffles;
    readonly IReadOnlyDictionary<int, QualityReading?[]> readings;
    readonly IReadOnlyList<IReadOnlyDictionary<int, QualityReading>> filings;

    public QualityLay(LoopRead read, HeavyweightTape tape, IReadOnlyList<int> reshuffles, IReadOnlyDictionary<int, QualityReading?[]> readings, IReadOnlyList<IReadOnlyDictionary<int, QualityReading>> filings)
    {
        this.read = read;
        this.tape = tape;
        this.reshuffles = reshuffles;
        this.readings = readings;
        this.filings = filings;
    }

    public IReadOnlyList<int> Reshuffles => reshuffles;

    public HeavyweightTape Tape => tape;

    // What each reshuffle could read: the members answered, those with a score of five checks or more, those whose facts
    // as first filed reach every quarter the valuation reads, those valued against their own quarters, and those the
    // starting setting qualifies.
    public IReadOnlyList<(DateOnly Session, int Answered, int Scored, int FactsReach, int Valued, int Qualifying)> Coverage() =>
    [
        .. reshuffles.Select(session =>
        {
            var at = readings[session].OfType<QualityReading>().ToArray();

            return (
                read.Calendar[session],
                at.Length,
                at.Count(one => one.Applied >= FundamentalScorecard.FewestRead),
                at.Count(one => one.FactsReach),
                at.Count(one => one.Place is not null),
                at.Count(one => QualityRule.Answer(one.Passed, one.Applied, one.Place, one.AboveAverage, QualityRule.Starting).Passed));
        }),
    ];

    // The history laid once for every setting: the tape, each month's first session from the first scored, every member's
    // reading on each, and its reading on the first session after each of its filings.
    public static async Task<QualityLay> ReadAsync(LoopRead read, SweepHistory history, DateOnly through, Action<string> progress, CancellationToken cancellation)
    {
        var reshuffles = HeavyweightSweep.Rebalances(read.Calendar, read.FirstScored, HeavyweightPeriod.Month);
        var (tape, _) = HeavyweightSweep.Lay(read.Inputs, read.Companies, null, reshuffles.ToHashSet(), read.FirstScored);
        var ciks = read.Series.Select(one => read.Companies.Companies.GetValueOrDefault(one.Name.Ticker).Cik).OfType<string>();

        progress("reading every member's filings as first filed");

        var facts = await history.FiledFactsAsync(ciks, through, cancellation);

        progress(FormattableString.Invariant($"scoring and valuing {read.Series.Length} name(s) at {reshuffles.Count} reshuffle(s) and after each filing"));

        var at = reshuffles.ToDictionary(session => session, _ => new QualityReading?[read.Series.Length]);
        var after = new IReadOnlyDictionary<int, QualityReading>[read.Series.Length];

        Parallel.For(0, read.Series.Length, name =>
        {
            var series = read.Series[name];
            var company = read.Companies.Companies.GetValueOrDefault(series.Name.Ticker);
            IReadOnlyList<FiledFactRow> filed = company.Cik is { } cik && facts.TryGetValue(cik, out var held) ? held : [];
            var prices = PriceHistory.Of(series.Bars);

            QualityReading? ReadOn(int session) =>
                tape.Member[name][session] && !double.IsNaN(tape.Close[name][session])
                    ? Reading(filed, read.Calendar[session], FundamentalScorecard.IsFinancial(GicsSectors.On(series.Name.Ticker, company.Sector, read.Calendar[session])), prices, tape.Close[name][session], tape.Average200[name][session])
                    : null;

            foreach (var session in reshuffles)
            {
                at[session][name] = ReadOn(session);
            }

            var sessions = new Dictionary<int, QualityReading>();

            foreach (var day in filed.Select(fact => fact.Filed).Distinct())
            {
                if (FirstAfter(read.Calendar, day) is { } session && session >= read.FirstScored && ReadOn(session) is { } reading)
                {
                    sessions[session] = reading;
                }
            }

            after[name] = sessions;
        });

        return new QualityLay(read, tape, reshuffles, at.ToDictionary(pair => pair.Key, pair => (QualityReading?[])pair.Value), after);
    }

    // A member's reading on a session, from its facts as first filed before it and its closes.
    public static QualityReading Reading(IReadOnlyList<FiledFactRow> facts, DateOnly session, bool financial, PriceHistory prices, double close, double average200)
    {
        var card = FundamentalScorecard.Read(facts, session, financial, prices);
        var passed = card.Checks.Count(check => check.Answer == CheckAnswer.Passed);
        var applied = card.Checks.Count(check => check.Answer is CheckAnswer.Passed or CheckAnswer.Failed);
        var parts = ValuationReading.PartsOf(facts, session);

        return new QualityReading(
            passed,
            applied,
            ValuationReading.Place(parts, prices, session),
            !double.IsNaN(average200) && close > average200,
            ValuationReading.OldestDay(parts) is not null && parts.Counts.Any(count => count.Day < session));
    }

    // The first session after a day, on which a filing made that day is first read; none past the calendar's end.
    static int? FirstAfter(DateOnly[] calendar, DateOnly day)
    {
        var at = Array.BinarySearch(calendar, day);
        var next = at >= 0 ? at + 1 : ~at;

        return next < calendar.Length ? next : null;
    }

    // One setting's walk over the tape, as the night's step walks its holdings: on each session a holding whose stock left
    // the index is sold at its last close as a member; where the setting reads it, one whose first session after a filing
    // reads its score under the floor is sold at that close; and on each reshuffle each holding answered that no longer
    // qualifies is sold at the close, one with no close kept, and the places left are filled down the ranking. Each
    // reshuffle's choices stand under the group with every member answered that session, the members a holding is judged
    // against.
    // From 21.5 the walk the engines read as well: a holding also sold at the first close under its 200-day average after
    // the session it was chosen on, the menu's sale no dial of the rule reads; and each reshuffle's qualifying members, in
    // the family's own ranking, handed to an arrangement a proposal's hooks set, which keeps and orders them as the night's
    // hooks do.
    // see: Every change the engines propose is one its family's night reads, so each is put to the operator
    public static (List<HeavyweightTrade> Trades, Dictionary<int, HeavyweightRebalance> Rebalances) Walk(
        HeavyweightTape tape,
        IReadOnlyList<int> reshuffles,
        IReadOnlyDictionary<int, QualityReading?[]> readings,
        IReadOnlyList<IReadOnlyDictionary<int, QualityReading>> filings,
        QualitySettings setting,
        bool soldUnderAverage = false,
        Func<int, IReadOnlyList<int>, IReadOnlyList<int>>? arrange = null)
    {
        var trades = new List<HeavyweightTrade>();
        var rebalances = new Dictionary<int, HeavyweightRebalance>();
        var open = new SortedDictionary<int, int>();
        var reshuffleAt = reshuffles.ToHashSet();

        void End(int name, int session, string reason)
        {
            var entry = open[name];
            var sold = tape.LastMemberClose[name][session];
            var cut = rebalances[entry].Sectors[0].Cut;

            trades.Add(new HeavyweightTrade(
                name,
                Group,
                entry,
                sold,
                reason,
                Return(tape, name, entry, sold),
                cut.Length > 0 ? cut.Average(one => Return(tape, one, entry, tape.LastMemberClose[one][sold]) ?? 0.0) : null,
                null,
                null,
                tape.Calendar[sold].Year - SweepColumns.FirstScored.Year));
            open.Remove(name);
        }

        for (var session = tape.First; session < tape.Calendar.Length; session++)
        {
            foreach (var name in open.Keys.ToArray())
            {
                if (!tape.Member[name][session])
                {
                    End(name, session - 1, QualityFamily.LeftTheIndex);
                }
                else if (setting.SellOnFiling && session > open[name] && filings[name].TryGetValue(session, out var filed)
                    && !FundamentalScorecard.Clears(filed.Passed, filed.Applied, setting.ScoreFloorInNine))
                {
                    End(name, session, OnAFiling);
                }
                else if (soldUnderAverage && session > open[name] && tape.Close[name][session] < tape.Average200[name][session])
                {
                    End(name, session, UnderTheAverage);
                }
            }

            if (!reshuffleAt.Contains(session))
            {
                continue;
            }

            var read = readings[session];

            bool Qualifies(int name) =>
                read[name] is { } one && QualityRule.Answer(one.Passed, one.Applied, one.Place, one.AboveAverage, setting).Passed;

            foreach (var name in open.Keys.ToArray())
            {
                if (read[name] is not null && !Qualifies(name))
                {
                    End(name, session, QualityFamily.NoLongerQualifies);
                }
            }

            var answered = Enumerable.Range(0, read.Length).Where(name => read[name] is not null).ToArray();
            var ranked = QualityRule.Ranked(answered.Where(Qualifies), name => (read[name]!.Value.Passed, read[name]!.Value.Applied, read[name]!.Value.Place, tape.Tickers[name]));
            var arranged = arrange is null ? ranked : arrange(session, ranked);
            var chosen = arranged.Where(name => !open.ContainsKey(name)).Take(Math.Max(0, setting.Holdings - open.Count)).ToArray();

            rebalances[session] = new HeavyweightRebalance([(Group, chosen, answered)]);

            foreach (var name in chosen)
            {
                open[name] = session;
            }
        }

        foreach (var (name, entry) in open)
        {
            trades.Add(new HeavyweightTrade(name, Group, entry, null, null, null, null, null, null, -1));
        }

        return (trades, rebalances);
    }

    // A name's return from one session's close to a later one's, none where either holds no close.
    static double? Return(HeavyweightTape tape, int name, int from, int to) =>
        to >= from && from >= 0 && tape.Close[name][from] > 0 && !double.IsNaN(tape.Close[name][to])
            ? (tape.Close[name][to] / tape.Close[name][from]) - 1.0
            : null;

    // One setting's book: its walk, each holding and its members bought at the open after its reshuffle and the holding
    // charged its round trip, and its months.
    public QualityBook Book(QualitySettings setting, bool soldUnderAverage = false, Func<int, IReadOnlyList<int>, IReadOnlyList<int>>? arrange = null)
    {
        var (trades, rebalances) = Walk(tape, reshuffles, readings, filings, setting, soldUnderAverage || setting.SoldUnderAverage, arrange);
        var holdings = trades
            .Select(trade => new BookHolding(
                trade.Entry,
                trade.End,
                Cost(trade),
                (from, to) => from == trade.Entry ? FromFill(trade.Name, from, tape.LastMemberClose[trade.Name][to]) : Return(tape, trade.Name, from, tape.LastMemberClose[trade.Name][to]),
                (from, to) => CutReturn(rebalances[trade.Entry].Sectors[0].Cut, from, to, bought: from == trade.Entry)))
            .ToArray();

        return new QualityBook(setting, trades, [.. trades.Select(trade => Costed(trade, rebalances))], BookMonths.Edges(reshuffles, holdings));
    }

    // A holding and its members at the open after its reshuffle, less its round trip where its stock holds a bar on both
    // its sessions.
    HeavyweightTrade Costed(HeavyweightTrade trade, IReadOnlyDictionary<int, HeavyweightRebalance> rebalances)
    {
        var atOpen = IndexSweepRunner.AtTheOpen(trade, tape, read.Series, rebalances);
        var series = read.Series[trade.Name];

        return atOpen.Result is { } result && trade.End is { } end && Array.BinarySearch(series.SessionAt, trade.Entry) >= 0 && Array.BinarySearch(series.SessionAt, end) >= 0
            ? atOpen with { Result = result - Cost(trade) }
            : atOpen;
    }

    // A name's return from the open after a session, a fraction of the sum the session's close put to it, through the
    // core's one pricing function; none where no open is held.
    double? FromFill(int name, int from, int to) =>
        Return(tape, name, from, to) is { } made && FillOver(name, from) is { } fill ? TradeFills.AtOpen(made, 1, 1, fill) : null;

    double? FillOver(int name, int session)
    {
        var series = read.Series[name];

        return Array.BinarySearch(series.SessionAt, session) is var bar && bar >= 0 && series.Bars[bar].Close > 0m && OpenFills.OpenAfter(series, bar) is { } open
            ? open / Statistic.FromPrice(series.Bars[bar].Close)
            : null;
    }

    double? CutReturn(int[] cut, int from, int to, bool bought) =>
        cut.Length > 0 ? cut.Average(one => (bought ? FromFill(one, from, tape.LastMemberClose[one][to]) : Return(tape, one, from, tape.LastMemberClose[one][to])) ?? 0.0) : null;

    // A holding's round trip as a fraction of its buy, its company valued on the buy and its prices read as they traded;
    // a holding still held is charged at its buy's price at both ends.
    double Cost(HeavyweightTrade trade)
    {
        var series = read.Series[trade.Name];

        if (Array.BinarySearch(series.SessionAt, trade.Entry) is var buy && buy < 0)
        {
            return 0;
        }

        var bought = series.Bars[buy];
        var entry = bought.RawClose > 0m ? bought.RawClose : bought.Close;
        var exit = entry;

        if (trade.End is { } end && Array.BinarySearch(series.SessionAt, end) is var sale && sale >= 0)
        {
            var sold = series.Bars[sale];

            exit = sold.RawClose > 0m ? sold.RawClose : sold.Close;
        }

        var ticker = series.Name.Ticker;
        var value = CompanyValue.On(new SessionClose(bought.Session, bought.Close, entry), read.Companies.Counts.GetValueOrDefault(ticker) ?? [], read.Companies.Splits.GetValueOrDefault(ticker) ?? []);

        return TradeCost.InPercent(value, entry, exit) / 100.0;
    }

    // A book's months as the floors read them, each by the session it opens on; where a fold is named only the months it
    // learns on, those whose close, the session the next month opens on, falls before its year.
    // see: A quality book meets its floors in months held, and a fold reads them in proportion to the years it learns on
    public static QualityMonths MonthsOf(QualityBook book, IReadOnlyList<DateOnly> calendar, IReadOnlyList<int> reshuffles, LoopFold? fold = null)
    {
        var closes = reshuffles.Zip(reshuffles.Skip(1)).ToDictionary(pair => pair.First, pair => pair.Second);

        return QualityRule.Months(
            book.Months
                .Where(month => fold is null || (closes.TryGetValue(month.Opens, out var close) && LoopFolds.Learns(fold, calendar[close])))
                .Select(month => (calendar[month.Opens], month.Edge)),
            SweepColumns.FirstScored.Year);
    }

    // The proposal over every setting's figures: the best edge among those whose months meet the family's floors, ties to
    // the fewest dials moved from the starting setting and then the key; none where none meets them.
    public static QualitySettings? Propose(IReadOnlyList<(QualitySettings Setting, HeavyweightFigures Figures, QualityMonths Months)> read) =>
        read.Where(one => QualityRule.MeetsFloors(one.Months) && one.Figures.Edge is not null)
            .OrderByDescending(one => one.Figures.Edge)
            .ThenBy(one => one.Setting.Changes(QualityRule.Starting))
            .ThenBy(one => one.Setting.Key, StringComparer.Ordinal)
            .Select(one => one.Setting)
            .FirstOrDefault();

    // The setting a fold chooses of the 72: the best edge among those whose months meet the floors in proportion to its
    // years, ties as the proposal breaks them; none where none meets them.
    public static QualitySettings? Choose(IReadOnlyList<(QualitySettings Setting, HeavyweightFigures Figures, QualityMonths Months)> read, int years) =>
        read.Where(one => one.Figures.Edge is not null && QualityRule.MeetsFloors(one.Months, years))
            .OrderByDescending(one => one.Figures.Edge)
            .ThenBy(one => one.Setting.Changes(QualityRule.Starting))
            .ThenBy(one => one.Setting.Key, StringComparer.Ordinal)
            .Select(one => one.Setting)
            .FirstOrDefault();

    // The grid as a procedure the tester runs year by year: each fold chooses among the 72 books on the holdings and the
    // months that ended before its year, and is tested on its choice's months in its year against the starting setting's,
    // or from 21.5 against the live rule's book handed in, where the proposal states the change an approval registers.
    // see: A change is adopted only on test years the proposal never saw, and a search is judged as a procedure run year by year
    // see: An approved change registers the family's live rule again at the change on every index, and the rule it replaces runs beside it for 63 sessions or in the background where the family holds nine
    public static LoopProposalRead Procedure(LoopRead read, IReadOnlyList<QualityBook> books, IReadOnlyList<int> reshuffles, QualityBook? live = null)
    {
        var current = live ?? books.Single(book => book.Setting == QualityRule.Starting);

        QualitySettings? ChooseIn(LoopFold fold) => Choose(
            [
                .. books.Select(book =>
                {
                    var learning = new FoldView<HeavyweightTrade>(fold, book.Costed, trade => read.Calendar[trade.Entry], trade => trade.End is { } end ? read.Calendar[end] : null).Learning;

                    return (book.Setting, HeavyweightSweep.Figures(book.Setting.Key, learning), MonthsOf(book, read.Calendar, reshuffles, fold));
                }),
            ],
            read.LearningYears(fold));

        var chosen = read.Folds.Select(fold => (Fold: fold, Setting: ChooseIn(fold))).ToArray();
        var proposal = ChooseIn(read.AllFinished);
        var byKey = books.ToDictionary(book => book.Setting.Key, StringComparer.Ordinal);

        return new LoopProposalRead(
            QualityRule.Name,
            LoopProcedures.Grid,
            proposal?.Words,
            current.Setting.Words,
            LoopProcedures.Points,
            BookMonths.LongestMonth,
            [.. chosen.Select(one => (one.Fold, one.Setting?.Words))],
            proposal is null ? 0 : chosen.Count(one => one.Setting is { } setting && setting.Changes(proposal) <= 1),
            LoopProcedures.BookEvidence(read.Folds, read.Calendar, [.. chosen.Select(one => one.Setting is null ? null : byKey[one.Setting.Key].Months)], current.Months))
        {
            Change = live is not null && proposal is not null && proposal != live.Setting ? new LoopChange(null, proposal.Key, null, LoopChange.NoHooks) : null,
        };
    }
}
