using EquityBrief.Core.Families;
using EquityBrief.Core.Ledger;
using EquityBrief.Core.Loop;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Readings;
using EquityBrief.Core.Releases;
using EquityBrief.Core.Sweep;
using EquityBrief.Worker.Loop;

namespace EquityBrief.Worker.Sweep;

// One filing read for the first time on a session, as the Fundamentals family's history test replays the night that
// reads it: the session, the checks the scorecard passed and applied that night, those the filing before held, the
// change in its sales growth on the quarter before's where read, and whether a flag stood for the member the night
// before.
public readonly record struct ImprovingFiling(int Session, int Passed, int Applied, int PassedBefore, int AppliedBefore, double? GrowthChange, bool FlaggedBefore);

// A member's scorecard as the night reads it from a session on: the checks passed and applied.
public readonly record struct ImprovingScore(int Passed, int Applied);

// A results release as the history test reads it: the instant the SEC accepted it, the day in New York it was accepted
// on, and its words under the reading version.
public sealed record ImprovingRelease(DateTimeOffset Accepted, DateOnly On, ReleaseAnswer Answer);

// One setting's trades over the whole history: as walked, the same bought at the next open after each one's round trip,
// and its months, the unit its floors and the tester read.
public sealed record ImprovingBook(ImprovingSweepSetting Setting, IReadOnlyList<HeavyweightTrade> Trades, IReadOnlyList<HeavyweightTrade> Costed, IReadOnlyList<(int Opens, double? Edge)> Months);

// What the history could read in one year: the members' filings first read on its sessions, those with a scorecard of five
// checks or more, those raising the score, those clearing the starting setting's bar, those whose sales growth sped up,
// and those a flag stood for the night before; and the results releases read under the version accepted in it.
public sealed record ImprovingYear(int Year, int Filings, int Scored, int Rising, int Clearing, int Faster, int Flagged, int Releases);

// The Fundamentals family's history test on one index: every member as it stood replayed night by night from the first
// scored session. On each session after a filing day its scorecard is worked out by the core's one scorecard over its
// filer's facts as first filed before the session, and where the newest filing it reads is read for the first time, the
// score the filing before held over the facts filed before that filing, the change in its sales growth by the ledger's
// own reading, and whether its newest results release readable the night before flagged it by the family's own rule.
// Each of the 144 settings walks its trades from those readings as the night's step walks them: a trade whose stock left
// the index sold at its last close as a member, one whose score falls under the hold sold at the close of the night that
// reads it, one at its time limit sold at that close; each bought at the next open and judged after its round trip
// against an equal-weighted hold of the members answered on the night it was chosen, each from its own open after it.
// It writes nothing to the store and registers nothing.
// see: The Fundamentals family's history test walks its trades over the pulled history on 144 settings an index and registers nothing
// see: A Fundamentals trade is held while its score stays at seven in nine, and judged against an equal-weighted hold of its index's members after its round trip
// see: A Fundamentals flag stands only for a release reporting a quarter newer than the member's newest stored filing, accepted within the last 63 sessions
public sealed class ImprovingLay
{
    // The one group a night's buys and the members they are judged against stand under.
    public const string Group = ImprovingRule.Name;

    readonly LoopRead read;
    readonly HeavyweightTape tape;
    readonly IReadOnlyList<int> months;
    readonly IReadOnlyList<IReadOnlyList<ImprovingFiling>> filings;
    readonly IReadOnlyList<IReadOnlyDictionary<int, ImprovingScore>> scores;

    public ImprovingLay(LoopRead read, HeavyweightTape tape, IReadOnlyList<int> months, IReadOnlyList<IReadOnlyList<ImprovingFiling>> filings, IReadOnlyList<IReadOnlyDictionary<int, ImprovingScore>> scores)
    {
        this.read = read;
        this.tape = tape;
        this.months = months;
        this.filings = filings;
        this.scores = scores;
    }

    public IReadOnlyList<int> Months => months;

    public HeavyweightTape Tape => tape;

    // What each year could read: the filings of the members answered on the night that first reads them, and the releases
    // of the index's members' filers.
    public IReadOnlyList<ImprovingYear> Coverage(IReadOnlyDictionary<string, IReadOnlyList<ImprovingRelease>> releases)
    {
        var read = this.read;
        var ciks = read.Series
            .Select(one => read.Companies.Companies.GetValueOrDefault(one.Name.Ticker).Cik)
            .OfType<string>()
            .Select(Core.Providers.SecEdgarArchive.Padded)
            .ToHashSet(StringComparer.Ordinal);
        var accepted = releases
            .Where(pair => ciks.Contains(pair.Key))
            .SelectMany(pair => pair.Value)
            .GroupBy(release => release.On.Year)
            .ToDictionary(year => year.Key, year => year.Count());
        var all = filings
            .SelectMany((one, name) => one.Where(filing => tape.Member[name][filing.Session] && !double.IsNaN(tape.Close[name][filing.Session])))
            .ToArray();

        return
        [
            .. Enumerable.Range(SweepColumns.FirstScored.Year, read.YearsHeld).Select(year =>
            {
                var inYear = all.Where(one => read.Calendar[one.Session].Year == year).ToArray();

                return new ImprovingYear(
                    year,
                    inYear.Length,
                    inYear.Count(one => one.Applied >= FundamentalScorecard.FewestRead),
                    inYear.Count(one => ImprovingRule.Rise(one.Passed, one.Applied, one.PassedBefore, one.AppliedBefore) > 0),
                    inYear.Count(one => FundamentalScorecard.Clears(one.Passed, one.Applied, ImprovingGrid.Starting.BuyInNine)),
                    inYear.Count(one => one.GrowthChange > 0),
                    inYear.Count(one => one.FlaggedBefore),
                    accepted.GetValueOrDefault(year));
            }),
        ];
    }

    // The history laid once for every setting: the tape, each month's first session from the first scored, and every
    // member's scorecards and newly read filings.
    public static async Task<ImprovingLay> ReadAsync(LoopRead read, SweepHistory history, IReadOnlyDictionary<string, IReadOnlyList<ImprovingRelease>> releases, DateOnly through, Action<string> progress, CancellationToken cancellation)
    {
        var months = HeavyweightSweep.Rebalances(read.Calendar, read.FirstScored, HeavyweightPeriod.Month);
        var (tape, _) = HeavyweightSweep.Lay(read.Inputs, read.Companies, null, new HashSet<int>(), read.FirstScored);
        var ciks = read.Series.Select(one => read.Companies.Companies.GetValueOrDefault(one.Name.Ticker).Cik).OfType<string>();

        progress("reading every member's filings as first filed");

        var facts = await history.FiledFactsAsync(ciks, through, cancellation);

        progress(FormattableString.Invariant($"replaying {read.Series.Length} name(s) night by night from {read.Calendar[read.FirstScored]:yyyy-MM-dd}"));

        var filings = new IReadOnlyList<ImprovingFiling>[read.Series.Length];
        var scores = new IReadOnlyDictionary<int, ImprovingScore>[read.Series.Length];

        Parallel.For(0, read.Series.Length, name =>
        {
            var series = read.Series[name];
            var company = read.Companies.Companies.GetValueOrDefault(series.Name.Ticker);
            IReadOnlyList<FiledFactRow> filed = company.Cik is { } cik && facts.TryGetValue(cik, out var held) ? held : [];
            IReadOnlyList<ImprovingRelease> released = company.Cik is { } filer && releases.TryGetValue(Core.Providers.SecEdgarArchive.Padded(filer), out var own) ? own : [];

            (filings[name], scores[name]) = Replay(
                filed,
                released,
                read.Calendar,
                read.FirstScored,
                session => FundamentalScorecard.IsFinancial(GicsSectors.On(series.Name.Ticker, company.Sector, read.Calendar[session])),
                PriceHistory.Of(series.Bars));
        });

        return new ImprovingLay(read, tape, months, filings, scores);
    }

    // A member's scorecard on each session it can change on, the first after a day a fact was filed and the first after
    // the GICS moves, and on each of them where the newest filing the scorecard reads is read for the first time, that
    // filing as the night reads it.
    public static (IReadOnlyList<ImprovingFiling> Filings, IReadOnlyDictionary<int, ImprovingScore> Scores) Replay(
        IReadOnlyList<FiledFactRow> facts,
        IReadOnlyList<ImprovingRelease> releases,
        IReadOnlyList<DateOnly> calendar,
        int first,
        Func<int, bool> financialOn,
        PriceHistory prices)
    {
        var read = new List<ImprovingFiling>();
        var scores = new Dictionary<int, ImprovingScore>();
        var sessions = facts
            .Select(fact => fact.Filed)
            .Append(GicsSectors.MovedAfter)
            .Distinct()
            .Select(day => FirstAfter(calendar, day))
            .OfType<int>()
            .Where(session => session >= first)
            .Distinct()
            .Order();

        foreach (var session in sessions)
        {
            var night = calendar[session];
            var financial = financialOn(session);
            var card = FundamentalScorecard.Read(facts, night, financial, prices);

            scores[session] = new ImprovingScore(card.Passed, card.Applied);

            if (ImprovingRule.FiledOn(facts, card.Quarter, night) is not { } filed || FirstAfter(calendar, filed) != session)
            {
                continue;
            }

            var before = FundamentalScorecard.Read(facts, filed, financial, prices);

            read.Add(new ImprovingFiling(
                session,
                card.Passed,
                card.Applied,
                before.Passed,
                before.Applied,
                FiledFacts.Read(facts, night).GrowthChange,
                session > 0 && FlagStood(facts, releases, calendar, calendar[session - 1], financialOn(session - 1), prices)));
        }

        return (read, scores);
    }

    // Whether a flag stood for a member on a night: its newest release readable that night, read under the version,
    // flagged by the family's own rule against the scorecard the night reads, its sessions counted on the history's own
    // calendar.
    public static bool FlagStood(IReadOnlyList<FiledFactRow> facts, IReadOnlyList<ImprovingRelease> releases, IReadOnlyList<DateOnly> calendar, DateOnly night, bool financial, PriceHistory prices)
    {
        if (releases.FirstOrDefault(release => ReleaseAvailability.ReadableOn(release.Accepted, night)) is not { } newest)
        {
            return false;
        }

        var card = FundamentalScorecard.Read(facts, night, financial, prices);

        return ImprovingRule.FlagStands(card.Applied, newest.On, ImprovingRule.FiledOn(facts, card.Quarter, night), ImprovingRule.NewestQuarter(facts, night), night, newest.Answer, (from, to) => SessionsBetween(calendar, from, to));
    }

    // The sessions a calendar holds from a day through another, both counted where they are sessions.
    public static int SessionsBetween(IReadOnlyList<DateOnly> calendar, DateOnly from, DateOnly to) =>
        Math.Max(0, (FirstAfter(calendar, to) ?? calendar.Count) - (FirstAfter(calendar, from.AddDays(-1)) ?? calendar.Count));

    // The first session after a day, on which a filing made that day is first read; none past the calendar's end.
    public static int? FirstAfter(IReadOnlyList<DateOnly> calendar, DateOnly day)
    {
        var low = 0;
        var high = calendar.Count;

        while (low < high)
        {
            var middle = (low + high) / 2;

            if (calendar[middle] <= day)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low < calendar.Count ? low : null;
    }

    // One setting's walk over the tape, as the night's step walks its trades: on each session a trade whose stock left the
    // index is sold at its last close as a member; one whose stock closes that session is sold at its close where the
    // newest scorecard read stands under the hold or the session is the last of its time limit; and the members whose
    // newly read filing the setting buys, closing that session and holding no trade, are chosen at its close, a stock
    // sold that session not chosen again. Each session's choices stand under the group with every member answered that
    // session, the members a trade is judged against.
    public static (List<HeavyweightTrade> Trades, Dictionary<int, HeavyweightRebalance> Chosen) Walk(
        HeavyweightTape tape,
        IReadOnlyList<IReadOnlyList<ImprovingFiling>> filings,
        IReadOnlyList<IReadOnlyDictionary<int, ImprovingScore>> scores,
        ImprovingSweepSetting setting)
    {
        var trades = new List<HeavyweightTrade>();
        var chosen = new Dictionary<int, HeavyweightRebalance>();
        var open = new SortedDictionary<int, (int Entry, ImprovingScore Score, int Held)>();
        var bySession = filings
            .SelectMany((one, name) => one.Select(filing => (Name: name, Filing: filing)))
            .GroupBy(one => one.Filing.Session)
            .ToDictionary(session => session.Key, session => session.ToArray());

        void End(int name, int session, string reason)
        {
            var entry = open[name].Entry;
            var sold = tape.LastMemberClose[name][session];
            var cut = chosen[entry].Sectors[0].Cut;
            var bought = sold > entry;

            // A trade sold before it was bought, its stock gone the session after its night, holds no result.
            trades.Add(new HeavyweightTrade(
                name,
                Group,
                entry,
                sold,
                reason,
                bought ? Return(tape, name, entry, sold) : null,
                bought && cut.Length > 0 ? cut.Average(one => Return(tape, one, entry, tape.LastMemberClose[one][sold]) ?? 0.0) : null,
                null,
                null,
                tape.Calendar[sold].Year - SweepColumns.FirstScored.Year));
            open.Remove(name);
        }

        for (var session = tape.First; session < tape.Calendar.Length; session++)
        {
            var sold = new HashSet<int>();

            foreach (var name in open.Keys.ToArray())
            {
                if (!tape.Member[name][session])
                {
                    End(name, session - 1, ImprovingRule.LeftTheIndex);
                    sold.Add(name);

                    continue;
                }

                var (entry, score, held) = open[name];

                if (scores[name].TryGetValue(session, out var newest))
                {
                    score = newest;
                }

                if (double.IsNaN(tape.Close[name][session]))
                {
                    open[name] = (entry, score, held);

                    continue;
                }

                held++;
                open[name] = (entry, score, held);

                var fell = score.Applied > 0 && !FundamentalScorecard.Clears(score.Passed, score.Applied, setting.HoldInNine);

                if (fell || held >= setting.Sessions)
                {
                    End(name, session, fell ? ImprovingRule.ScoreFell : ImprovingRule.TimeLimit);
                    sold.Add(name);
                }
            }

            if (!bySession.TryGetValue(session, out var read))
            {
                continue;
            }

            var buys = read
                .Where(one => !open.ContainsKey(one.Name) && !sold.Contains(one.Name)
                    && tape.Member[one.Name][session] && !double.IsNaN(tape.Close[one.Name][session])
                    && ImprovingGrid.Buys(one.Filing.Passed, one.Filing.Applied, one.Filing.PassedBefore, one.Filing.AppliedBefore, one.Filing.GrowthChange, one.Filing.FlaggedBefore, setting))
                .Select(one => one.Name)
                .Order()
                .ToArray();

            if (buys.Length == 0)
            {
                continue;
            }

            var answered = Enumerable.Range(0, tape.Tickers.Length).Where(name => tape.Member[name][session] && !double.IsNaN(tape.Close[name][session])).ToArray();

            chosen[session] = new HeavyweightRebalance([(Group, buys, answered)]);

            foreach (var name in buys)
            {
                open[name] = (session, scores[name].GetValueOrDefault(session), 0);
            }
        }

        foreach (var (name, (entry, _, _)) in open)
        {
            trades.Add(new HeavyweightTrade(name, Group, entry, null, null, null, null, null, null, -1));
        }

        return (trades, chosen);
    }

    // One setting's book: its walk, each trade and its members bought at the open after its night and the trade charged
    // its round trip, and its months.
    public ImprovingBook Book(ImprovingSweepSetting setting)
    {
        var (trades, chosen) = Walk(tape, filings, scores, setting);
        var holdings = trades
            .Select(trade => new BookHolding(
                trade.Entry,
                trade.End,
                Cost(trade),
                (from, to) => from == trade.Entry ? FromFill(trade.Name, from, tape.LastMemberClose[trade.Name][to]) : Return(tape, trade.Name, from, tape.LastMemberClose[trade.Name][to]),
                (from, to) => CutReturn(chosen[trade.Entry].Sectors[0].Cut, from, to, bought: from == trade.Entry)))
            .ToArray();

        return new ImprovingBook(setting, trades, [.. trades.Select(trade => Costed(trade, chosen))], BookMonths.Edges(months, holdings));
    }

    // A trade and its members at the open after its night, less its round trip where its stock holds a bar on both its
    // sessions.
    HeavyweightTrade Costed(HeavyweightTrade trade, IReadOnlyDictionary<int, HeavyweightRebalance> chosen)
    {
        var atOpen = IndexSweepRunner.AtTheOpen(trade, tape, read.Series, chosen);
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

    // A trade's round trip as a fraction of its buy, its company valued on the buy and its prices read as they traded; a
    // trade still held is charged at its buy's price at both ends.
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
    public static QualityMonths MonthsOf(ImprovingBook book, IReadOnlyList<DateOnly> calendar, IReadOnlyList<int> months, LoopFold? fold = null)
    {
        var closes = months.Zip(months.Skip(1)).ToDictionary(pair => pair.First, pair => pair.Second);

        return QualityRule.Months(
            book.Months
                .Where(month => fold is null || (closes.TryGetValue(month.Opens, out var close) && LoopFolds.Learns(fold, calendar[close])))
                .Select(month => (calendar[month.Opens], month.Edge)),
            SweepColumns.FirstScored.Year);
    }

    // The proposal over every setting's figures: the best edge among those whose months meet the floors, ties to the
    // fewest dials moved from the starting setting and then the key; none where none meets them.
    public static ImprovingSweepSetting? Propose(IReadOnlyList<(ImprovingSweepSetting Setting, HeavyweightFigures Figures, QualityMonths Months)> read) =>
        Choose(read, SweepFigures.Years);

    // The setting a fold chooses of the 144: the best edge among those whose months meet the floors in proportion to its
    // years, ties as the proposal breaks them; none where none meets them.
    public static ImprovingSweepSetting? Choose(IReadOnlyList<(ImprovingSweepSetting Setting, HeavyweightFigures Figures, QualityMonths Months)> read, int years) =>
        read.Where(one => one.Figures.Edge is not null && QualityRule.MeetsFloors(one.Months, years))
            .OrderByDescending(one => one.Figures.Edge)
            .ThenBy(one => one.Setting.Changes(ImprovingGrid.Starting))
            .ThenBy(one => one.Setting.Key, StringComparer.Ordinal)
            .Select(one => one.Setting)
            .FirstOrDefault();

    // The grid as a procedure the tester runs year by year: each fold chooses among the 144 books on the trades and the
    // months that ended before its year, and is tested on its choice's months in its year against the starting setting's.
    // see: A change is adopted only on test years the proposal never saw, and a search is judged as a procedure run year by year
    public static LoopProposalRead Procedure(LoopRead read, IReadOnlyList<ImprovingBook> books, IReadOnlyList<int> months)
    {
        var current = books.Single(book => book.Setting == ImprovingGrid.Starting);

        ImprovingSweepSetting? ChooseIn(LoopFold fold) => Choose(
            [
                .. books.Select(book =>
                {
                    var learning = new FoldView<HeavyweightTrade>(fold, book.Costed, trade => read.Calendar[trade.Entry], trade => trade.End is { } end ? read.Calendar[end] : null).Learning;

                    return (book.Setting, HeavyweightSweep.Figures(book.Setting.Key, learning), MonthsOf(book, read.Calendar, months, fold));
                }),
            ],
            read.LearningYears(fold));

        var chosen = read.Folds.Select(fold => (Fold: fold, Setting: ChooseIn(fold))).ToArray();
        var proposal = ChooseIn(read.AllFinished);
        var byKey = books.ToDictionary(book => book.Setting.Key, StringComparer.Ordinal);

        return new LoopProposalRead(
            ImprovingRule.Name,
            LoopProcedures.Grid,
            proposal?.Words,
            ImprovingGrid.Starting.Words,
            LoopProcedures.Points,
            BookMonths.LongestMonth,
            [.. chosen.Select(one => (one.Fold, one.Setting?.Words))],
            proposal is null ? 0 : chosen.Count(one => one.Setting is { } setting && setting.Changes(proposal) <= 1),
            LoopProcedures.BookEvidence(read.Folds, read.Calendar, [.. chosen.Select(one => one.Setting is null ? null : byKey[one.Setting.Key].Months)], current.Months));
    }

    // A name's return from one session's close to a later one's, none where either holds no close.
    static double? Return(HeavyweightTape tape, int name, int from, int to) =>
        to >= from && from >= 0 && tape.Close[name][from] > 0 && !double.IsNaN(tape.Close[name][to])
            ? (tape.Close[name][to] / tape.Close[name][from]) - 1.0
            : null;
}
