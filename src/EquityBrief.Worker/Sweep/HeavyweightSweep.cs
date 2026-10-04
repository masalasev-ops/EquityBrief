using System.Globalization;
using EquityBrief.Core.Bars;
using EquityBrief.Core.Families;
using EquityBrief.Core.Indicators;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Sweep;
using EquityBrief.Worker.Families;

namespace EquityBrief.Worker.Sweep;

// Where a sector's return is read from: the mean of its members' own returns, or its fund's.
public enum HeavyweightSectorReturn
{
    Members,
    Fund,
}

// How often the book rebalances: on the first session of each month, or of each week.
public enum HeavyweightPeriod
{
    Month,
    Week,
}

// What ends a holding besides its stock leaving the index: whichever comes first of no longer a leader at a rebalance
// and a close under its 200-session average, the one, or the other.
public enum HeavyweightExit
{
    Both,
    Drop,
    Break,
}

// One setting of the heavyweights' sweep: the size cut, the look-back, the leaders a sector, where a sector's return is
// read from, whether a leader needs a beta of at least one, the rebalance's period and the exit.
public sealed record HeavyweightSetting(
    int Largest,
    int LookBack,
    int Leaders,
    HeavyweightSectorReturn Sector,
    bool HighBeta,
    HeavyweightPeriod Period,
    HeavyweightExit Exit)
{
    public HeavyweightSettings Reading => new(Largest, LookBack, Leaders, HighBeta);

    public string Key => string.Join(
        "|",
        "size=" + (Largest == HeavyweightSweep.EveryCompany ? "every" : Largest.ToString(CultureInfo.InvariantCulture)),
        "look-back=" + LookBack.ToString(CultureInfo.InvariantCulture),
        "leaders=" + Leaders.ToString(CultureInfo.InvariantCulture),
        "sector=" + (Sector == HeavyweightSectorReturn.Members ? "members" : "fund"),
        "beta=" + (HighBeta ? "at least 1" : "off"),
        "rebalance=" + (Period == HeavyweightPeriod.Month ? "month" : "week"),
        "exit=" + Exit switch { HeavyweightExit.Drop => "drop", HeavyweightExit.Break => "break", _ => "both" });

    // How many dials the setting moves from another.
    public int Changes(HeavyweightSetting other) =>
        (Largest != other.Largest ? 1 : 0) + (LookBack != other.LookBack ? 1 : 0) + (Leaders != other.Leaders ? 1 : 0)
        + (Sector != other.Sector ? 1 : 0) + (HighBeta != other.HighBeta ? 1 : 0) + (Period != other.Period ? 1 : 0)
        + (Exit != other.Exit ? 1 : 0);
}

// One holding a setting's walk kept: the stock and its sector, the session it was bought on, the session its result was
// read at and why it ended, none while it is still held at the history's end; its result, the size cut's return over
// the same sessions, every member's and the index's, each in percent of the buy; and the year it ended in.
public sealed record HeavyweightTrade(
    int Name,
    string Sector,
    int Entry,
    int? End,
    string? Reason,
    double? Result,
    double? Cut,
    double? EveryMember,
    double? Index,
    int Year)
{
    public double? Edge => HeavyweightRule.Edge(Result, Cut);
}

// What one setting's holdings came to: the trades ended and those still held, the edge over the size cut and the
// plain result, the size cut's, every member's and the index's returns beside them, each year's trades and edge, the
// years the edge stood above nothing, the last three years together, the edge without its five largest results by
// size, the edge's standard error, and how long a holding was held.
public sealed record HeavyweightFigures(
    string Key,
    int Trades,
    int Open,
    double? Edge,
    double? Result,
    double? CutReturn,
    double? EveryMember,
    double? Index,
    int[] YearTrades,
    double?[] YearEdge,
    int YearsBeating,
    double? RecentEdge,
    double? EdgeWithoutLargest,
    double? StandardError,
    double? HeldMedian,
    double? HeldMean)
{
    public bool MeetsFloors => Trades >= FamilySweep.TradeFloor && YearsBeating >= FamilySweep.YearsBeating;
}

// What the heavyweights' sweep proposes: the setting with the best edge among those meeting the floors with every
// setting one step from it, or none where no setting meets them.
public sealed record HeavyweightProposal(HeavyweightSetting? Setting, HeavyweightFigures? Proposed, IReadOnlyList<(HeavyweightSetting Setting, HeavyweightFigures Figures)> Neighbours)
{
    public bool SetAside => Proposed is null;
}

// How the replay at the provisional setting read the rebalances the night's book stored: the sessions and sectors
// compared, those whose largest companies, leads and leaders were the stored ones, and each difference found.
public sealed record HeavyweightComparison(int Sessions, int Sectors, int Matched, IReadOnlyList<string> Differences);

// The history laid out on the calendar for the heavyweights' walk: each name's adjusted close, its 200-session average
// and whether the index held it on every session, the newest session at or before each on which the index held it and
// it closed, the index's close, and the first session scored.
public sealed class HeavyweightTape
{
    public required string[] Tickers { get; init; }

    public required DateOnly[] Calendar { get; init; }

    public required double[][] Close { get; init; }

    public required double[][] Average200 { get; init; }

    public required bool[][] Member { get; init; }

    public required int[][] LastMemberClose { get; init; }

    public required double[] Index { get; init; }

    public required int First { get; init; }

    // The newest session at or before one on which the index held a name and it closed, -1 where none.
    public static int[] LastMemberCloseOf(bool[] member, double[] close)
    {
        var last = new int[close.Length];
        var held = -1;

        for (var session = 0; session < close.Length; session++)
        {
            if (member[session] && !double.IsNaN(close[session]))
            {
                held = session;
            }

            last[session] = held;
        }

        return last;
    }
}

// One member of the index on a session a rebalance may read: the member as the rule reads it, its return at each
// look-back the sweep reads.
public sealed record HeavyweightCandidate(int Name, HeavyweightMember Member, double?[] Returns);

// One session a rebalance may read: every member on it, and each fund's return at each look-back by its sector.
public sealed record HeavyweightSession(int Session, IReadOnlyList<HeavyweightCandidate> Members, IReadOnlyList<IReadOnlyDictionary<string, double?>> FundReturns);

// One rebalance as a setting's walk reads it: each sector's leaders and the size cut they were chosen from, by name.
public sealed record HeavyweightRebalance(IReadOnlyList<(string Sector, int[] Leaders, int[] Cut)> Sectors)
{
    public IEnumerable<int> Buys => Sectors.SelectMany(sector => sector.Leaders);
}

// The sector heavyweights' sweep: the family's rule replayed over the pulled history under each of its settings, every
// member as it stood on each session a rebalance reads, ranked and led by the night's own reading, the holdings walked
// session by session as the night's book walks them, and each trade scored by its percent return less the equal-weighted
// return of the size cut it was chosen from over the same sessions, every member's and the index's return beside it as
// context. One holding a stock in each setting, as the book holds it.
// see: The heavyweights' sweep replays the book over the pulled history across its settings and proposes the best edge among those meeting the family sweeps' floors
// see: A sector heavyweight's trade is scored by its percent return less the equal-weighted return of the size cut it was chosen from
// see: A heavyweight's beta is read over 251 daily returns against the index
public static class HeavyweightSweep
{
    // The size cut that reads every company of a sector.
    public const int EveryCompany = int.MaxValue;

    public static IReadOnlyList<int> Sizes { get; } = [HeavyweightRule.Largest, 10, EveryCompany];

    public static IReadOnlyList<int> LookBacks { get; } = [63, HeavyweightRule.LookBack, 251];

    public static IReadOnlyList<int> LeaderCounts { get; } = [HeavyweightRule.Leaders, 2];

    public static HeavyweightSetting Provisional { get; } = new(
        HeavyweightRule.Largest,
        HeavyweightRule.LookBack,
        HeavyweightRule.Leaders,
        HeavyweightSectorReturn.Members,
        false,
        HeavyweightPeriod.Month,
        HeavyweightExit.Both);

    // Every combination of the dials.
    public static IReadOnlyList<HeavyweightSetting> Settings { get; } =
    [
        .. from largest in Sizes
           from lookBack in LookBacks
           from leaders in LeaderCounts
           from sector in new[] { HeavyweightSectorReturn.Members, HeavyweightSectorReturn.Fund }
           from beta in new[] { false, true }
           from period in new[] { HeavyweightPeriod.Month, HeavyweightPeriod.Week }
           from exit in new[] { HeavyweightExit.Both, HeavyweightExit.Drop, HeavyweightExit.Break }
           select new HeavyweightSetting(largest, lookBack, leaders, sector, beta, period, exit),
    ];

    // The settings one step from a setting: the size cut and the look-back a level either way, and each other dial to
    // each of its other levels, the exit's two others among them.
    public static IEnumerable<HeavyweightSetting> Neighbours(HeavyweightSetting setting)
    {
        foreach (var step in new[] { -1, 1 })
        {
            if (Sizes.IndexOf(setting.Largest) + step is var size && size >= 0 && size < Sizes.Count)
            {
                yield return setting with { Largest = Sizes[size] };
            }
        }

        foreach (var step in new[] { -1, 1 })
        {
            if (LookBacks.IndexOf(setting.LookBack) + step is var lookBack && lookBack >= 0 && lookBack < LookBacks.Count)
            {
                yield return setting with { LookBack = LookBacks[lookBack] };
            }
        }

        foreach (var leaders in LeaderCounts.Where(leaders => leaders != setting.Leaders))
        {
            yield return setting with { Leaders = leaders };
        }

        yield return setting with { Sector = setting.Sector == HeavyweightSectorReturn.Members ? HeavyweightSectorReturn.Fund : HeavyweightSectorReturn.Members };
        yield return setting with { HighBeta = !setting.HighBeta };
        yield return setting with { Period = setting.Period == HeavyweightPeriod.Month ? HeavyweightPeriod.Week : HeavyweightPeriod.Month };

        foreach (var exit in new[] { HeavyweightExit.Both, HeavyweightExit.Drop, HeavyweightExit.Break }.Where(exit => exit != setting.Exit))
        {
            yield return setting with { Exit = exit };
        }
    }

    static int IndexOf(this IReadOnlyList<int> levels, int value)
    {
        for (var at = 0; at < levels.Count; at++)
        {
            if (levels[at] == value)
            {
                return at;
            }
        }

        return -1;
    }

    // The sessions a period rebalances on from the first scored: the first session of each month, or of each week from
    // its Monday, on the history's calendar.
    public static IReadOnlyList<int> Rebalances(IReadOnlyList<DateOnly> calendar, int first, HeavyweightPeriod period)
    {
        var sessions = new List<int>();

        for (var session = first; session < calendar.Count; session++)
        {
            if (session == first || !Same(calendar[session - 1], calendar[session], period))
            {
                sessions.Add(session);
            }
        }

        return sessions;
    }

    static bool Same(DateOnly before, DateOnly day, HeavyweightPeriod period) =>
        period == HeavyweightPeriod.Month
            ? before.Year == day.Year && before.Month == day.Month
            : MondayOf(before) == MondayOf(day);

    static DateOnly MondayOf(DateOnly day) => day.AddDays(-(((int)day.DayOfWeek + 6) % 7));

    // The history laid on the calendar, and every session a rebalance may read with its members as they stood: on
    // each, every name the index held with a bar, its company and sector as the companies pull filed them, its value
    // from the newest count filed before the session on the count's split basis, the dollars it traded over the fifty
    // sessions to it, its close and averages, its beta against the index, and its return at each look-back; and each
    // sector fund's return at each look-back by its own sessions.
    public static (HeavyweightTape Tape, IReadOnlyDictionary<int, HeavyweightSession> Sessions) Lay(
        SweepHistoryInputs inputs,
        HeavyweightHistory history,
        SweepMarketSeries? index,
        IReadOnlySet<int> read,
        int first)
    {
        var calendar = inputs.Sessions;
        var sessionAt = calendar.Select((session, at) => (session, at)).ToDictionary(pair => pair.session, pair => pair.at);
        var names = inputs.Names;
        var close = new double[names.Count][];
        var average50 = new double[names.Count][];
        var average200 = new double[names.Count][];
        var member = new bool[names.Count][];
        var lastMemberClose = new int[names.Count][];
        var barAt = new int[names.Count][];
        var bars = new SweepBar[names.Count][];
        var indexClose = new double[calendar.Length];

        Array.Fill(indexClose, double.NaN);

        foreach (var (session, value) in index?.Closes ?? [])
        {
            if (sessionAt.TryGetValue(session, out var at))
            {
                indexClose[at] = value;
            }
        }

        Parallel.For(0, names.Count, name =>
        {
            var series = names[name].Bars.Where(bar => sessionAt.ContainsKey(bar.Session)).ToArray();
            var closes = Filled(calendar.Length);
            var fifty = Filled(calendar.Length);
            var twoHundred = Filled(calendar.Length);
            var at = new int[calendar.Length];

            Array.Fill(at, -1);

            if (series.Length > 0)
            {
                var points = IndicatorSeries.For([.. series.Select(bar => new SeriesBar(bar.Session, Statistic.FromPrice(bar.High), Statistic.FromPrice(bar.Low), Statistic.FromPrice(bar.Close), Statistic.FromVolume(bar.Volume)))]);

                foreach (var point in points)
                {
                    if (point.Value is { } value && (point.Name == IndicatorSeries.Sma50 || point.Name == IndicatorSeries.Sma200))
                    {
                        (point.Name == IndicatorSeries.Sma50 ? fifty : twoHundred)[sessionAt[point.SessionDate]] = value;
                    }
                }
            }

            for (var bar = 0; bar < series.Length; bar++)
            {
                var session = sessionAt[series[bar].Session];

                closes[session] = Statistic.FromPrice(series[bar].Close);
                at[session] = bar;
            }

            var held = new bool[calendar.Length];

            for (var session = 0; session < calendar.Length; session++)
            {
                held[session] = names[name].MemberOn(calendar[session]);
            }

            close[name] = closes;
            average50[name] = fifty;
            average200[name] = twoHundred;
            member[name] = held;
            lastMemberClose[name] = HeavyweightTape.LastMemberCloseOf(held, closes);
            barAt[name] = at;
            bars[name] = series;
        });

        var funds = history.Funds
            .Where(fund => GicsSectors.Funds.Values.Contains(fund.Series, StringComparer.Ordinal))
            .ToDictionary(fund => GicsSectors.Funds.Single(pair => pair.Value == fund.Series).Key, fund => fund, StringComparer.Ordinal);
        var fundAt = funds.ToDictionary(pair => pair.Key, pair => pair.Value.Closes.Select((one, at) => (one.Session, at)).ToDictionary(one => one.Session, one => one.at), StringComparer.Ordinal);
        var sessions = new Dictionary<int, HeavyweightSession>();

        foreach (var session in read.Order())
        {
            var day = calendar[session];
            var candidates = new List<HeavyweightCandidate>();

            for (var name = 0; name < names.Count; name++)
            {
                if (!member[name][session] || barAt[name][session] is var bar && bar < 0)
                {
                    continue;
                }

                var ticker = names[name].Ticker;
                var series = bars[name];
                var company = history.Companies.TryGetValue(ticker, out var filed) ? filed : (null, null);
                decimal? value = series[bar].RawClose > 0m && history.Counts.TryGetValue(ticker, out var counts)
                    ? CompanyValue.On(new SessionClose(day, series[bar].Close, series[bar].RawClose), counts, history.Splits.GetValueOrDefault(ticker) ?? [])
                    : null;
                var returns = LookBacks.Select(lookBack => bar >= lookBack && series[bar - lookBack].Close > 0m ? Statistic.FromRatio(series[bar].Close / series[bar - lookBack].Close) - 1.0 : (double?)null).ToArray();

                candidates.Add(new HeavyweightCandidate(
                    name,
                    new HeavyweightMember(
                        ticker,
                        CompanyRank.CompanyOf(ticker, company.Cik),
                        GicsSectors.On(ticker, company.Sector, day),
                        value,
                        CompanyRank.DollarVolume(series[Math.Max(0, bar + 1 - CompanyRank.DollarVolumeSessions)..(bar + 1)].Select(one => (one.RawClose, one.Volume))),
                        null,
                        close[name][session],
                        Held(average50[name][session]),
                        Held(average200[name][session]),
                        BetaOf(series, bar, sessionAt, indexClose)),
                    returns));
            }

            var fundReturns = LookBacks.Select(lookBack => (IReadOnlyDictionary<string, double?>)GicsSectors.Eleven.ToDictionary(
                sector => sector,
                sector => funds.TryGetValue(sector, out var fund) && fundAt[sector].TryGetValue(day, out var at) && at >= lookBack && fund.Closes[at - lookBack].Close > 0
                    ? fund.Closes[at].Close / fund.Closes[at - lookBack].Close - 1.0
                    : (double?)null,
                StringComparer.Ordinal)).ToArray();

            sessions[session] = new HeavyweightSession(session, candidates, fundReturns);
        }

        var tape = new HeavyweightTape
        {
            Tickers = [.. names.Select(name => name.Ticker)],
            Calendar = calendar,
            Close = close,
            Average200 = average200,
            Member = member,
            LastMemberClose = lastMemberClose,
            Index = indexClose,
            First = first,
        };

        return (tape, sessions);
    }

    static double[] Filled(int length)
    {
        var values = new double[length];

        Array.Fill(values, double.NaN);

        return values;
    }

    static double? Held(double value) => double.IsNaN(value) ? null : value;

    // A member's beta on a bar: its closes over the newest 252 of its bars to the bar beside the index's on the same
    // sessions, none where the index holds no close on one of them.
    static double? BetaOf(SweepBar[] series, int bar, IReadOnlyDictionary<DateOnly, int> sessionAt, double[] index)
    {
        if (bar < HeavyweightRule.BetaReturns)
        {
            return null;
        }

        var closes = new (double Stock, double Index)[HeavyweightRule.BetaReturns + 1];

        for (var at = 0; at <= HeavyweightRule.BetaReturns; at++)
        {
            var one = series[bar - HeavyweightRule.BetaReturns + at];
            var market = index[sessionAt[one.Session]];

            if (double.IsNaN(market))
            {
                return null;
            }

            closes[at] = (Statistic.FromPrice(one.Close), market);
        }

        return HeavyweightRule.Beta(closes);
    }

    // A session's sectors at a setting, read by the night's own rule over the members as they stood with their return
    // at the setting's look-back, a fund's return handed in for the sector's where the setting reads it.
    public static IReadOnlyList<HeavyweightSector> Sectors(HeavyweightSession session, HeavyweightSetting setting)
    {
        var at = LookBacks.IndexOf(setting.LookBack);
        var members = session.Members.Select(candidate => candidate.Member with { Return = candidate.Returns[at] }).ToArray();

        return HeavyweightRule.Read(members, setting.Reading, setting.Sector == HeavyweightSectorReturn.Fund ? session.FundReturns[at] : null);
    }

    // A session's reading at a setting as the walk reads it: each sector's leaders and its size cut, by name.
    public static HeavyweightRebalance Read(HeavyweightSession session, HeavyweightSetting setting, IReadOnlyDictionary<string, int> names) =>
        new([.. Sectors(session, setting).Select(sector => (sector.Sector, sector.Leaders.Select(leader => names[leader]).ToArray(), sector.Largest.Select(ranked => names[ranked.Ticker]).ToArray()))]);

    // Every setting walked over the history laid out: each reading's rebalances read once over every session either
    // period reads, then walked at each period and each exit, every member's return over a holding's sessions read
    // once for each span.
    public static IReadOnlyList<(HeavyweightSetting Setting, HeavyweightFigures Figures)> ReadAll(
        HeavyweightTape tape,
        IReadOnlyDictionary<int, HeavyweightSession> sessions,
        IReadOnlyList<int> months,
        IReadOnlyList<int> weeks)
    {
        var names = tape.Tickers.Select((ticker, at) => (ticker, at)).ToDictionary(pair => pair.ticker, pair => pair.at, StringComparer.Ordinal);
        var everyMember = new System.Collections.Concurrent.ConcurrentDictionary<(int From, int To), double?>();
        var byKey = new System.Collections.Concurrent.ConcurrentDictionary<string, HeavyweightFigures>(StringComparer.Ordinal);
        var read = months.Concat(weeks).Distinct().ToArray();

        double? EveryMemberOnce(int from, int to) => everyMember.GetOrAdd((from, to), span => EveryMember(tape, span.From, span.To));

        Parallel.ForEach(
            Settings.GroupBy(setting => (setting.Largest, setting.LookBack, setting.Leaders, setting.Sector, setting.HighBeta)),
            group =>
            {
                var rebalances = read.ToDictionary(session => session, session => Read(sessions[session], group.First(), names));

                foreach (var setting in group)
                {
                    var period = setting.Period == HeavyweightPeriod.Month ? months : weeks;

                    byKey[setting.Key] = Figures(setting.Key, Walk(tape, period.ToDictionary(session => session, session => rebalances[session]), setting.Exit, EveryMemberOnce));
                }
            });

        return [.. Settings.Select(setting => (setting, byKey[setting.Key]))];
    }

    // Walks one setting's holdings session by session from the first scored, as the night's book walks them: each
    // holding ended where its stock left the index, at the close of its last session as a member, and where the exit
    // reads the average, at a close under it; on a rebalance, each holding the rule would not buy ended at that close
    // where the exit reads it, and each leader not held bought at that close with its sector's size cut. A holding
    // still open at the history's end has no result.
    public static List<HeavyweightTrade> Walk(
        HeavyweightTape tape,
        IReadOnlyDictionary<int, HeavyweightRebalance> rebalances,
        HeavyweightExit exit,
        Func<int, int, double?>? everyMember = null)
    {
        var trades = new List<HeavyweightTrade>();
        var open = new SortedDictionary<int, (int Entry, string Sector, int[] Cut)>();

        void End(int name, int session, string reason)
        {
            var (entry, sector, cut) = open[name];
            var sold = tape.LastMemberClose[name][session];
            var result = Return(tape, name, entry, sold);
            double? cutReturn = cut.Length > 0 ? cut.Average(one => Return(tape, one, entry, tape.LastMemberClose[one][sold]) ?? 0.0) : null;
            double? market = IndexReturn(tape, entry, sold);

            trades.Add(new HeavyweightTrade(name, sector, entry, sold, reason, result, cutReturn, everyMember?.Invoke(entry, sold), market, tape.Calendar[sold].Year - SweepColumns.FirstScored.Year));
            open.Remove(name);
        }

        for (var session = tape.First; session < tape.Calendar.Length; session++)
        {
            foreach (var name in open.Keys.ToArray())
            {
                if (!tape.Member[name][session])
                {
                    End(name, session - 1, HeavyweightBook.LeftTheIndex);
                }
                else if (exit != HeavyweightExit.Drop
                    && !double.IsNaN(tape.Close[name][session])
                    && HeavyweightRule.Broken(tape.Close[name][session], Held(tape.Average200[name][session])))
                {
                    End(name, session, HeavyweightBook.UnderTheAverage);
                }
            }

            if (!rebalances.TryGetValue(session, out var rebalance))
            {
                continue;
            }

            if (exit != HeavyweightExit.Break)
            {
                var buys = rebalance.Buys.ToHashSet();

                foreach (var name in open.Keys.Where(name => !buys.Contains(name)).ToArray())
                {
                    End(name, session, HeavyweightBook.NoLongerTheLeader);
                }
            }

            foreach (var (sector, leaders, cut) in rebalance.Sectors)
            {
                foreach (var leader in leaders.Where(leader => !open.ContainsKey(leader) && !double.IsNaN(tape.Close[leader][session])))
                {
                    open[leader] = (session, sector, cut);
                }
            }
        }

        foreach (var (name, (entry, sector, _)) in open)
        {
            trades.Add(new HeavyweightTrade(name, sector, entry, null, null, null, null, null, null, -1));
        }

        return trades;
    }

    // A name's return from a session's close to a later one's, none where either holds no close.
    static double? Return(HeavyweightTape tape, int name, int from, int to) =>
        to >= from && !double.IsNaN(tape.Close[name][from]) && tape.Close[name][from] > 0 && !double.IsNaN(tape.Close[name][to])
            ? (tape.Close[name][to] / tape.Close[name][from]) - 1.0
            : null;

    // The index's return from a session's close to the newest it holds at or before a later one.
    static double? IndexReturn(HeavyweightTape tape, int from, int to)
    {
        if (double.IsNaN(tape.Index[from]) || tape.Index[from] <= 0)
        {
            return null;
        }

        for (var session = to; session > from; session--)
        {
            if (!double.IsNaN(tape.Index[session]))
            {
                return (tape.Index[session] / tape.Index[from]) - 1.0;
            }
        }

        return 0.0;
    }

    // Every member's return from a session's close to a later one's, each read to the close of its last session as a
    // member at or before it, weighted equally, over the members holding a close on the first; none where none does.
    public static double? EveryMember(HeavyweightTape tape, int from, int to)
    {
        double sum = 0;
        var counted = 0;

        for (var name = 0; name < tape.Tickers.Length; name++)
        {
            if (tape.Member[name][from] && Return(tape, name, from, tape.LastMemberClose[name][to]) is { } one)
            {
                sum += one;
                counted++;
            }
        }

        return counted > 0 ? sum / counted : null;
    }

    public static HeavyweightFigures Figures(string key, IReadOnlyList<HeavyweightTrade> trades)
    {
        var years = SweepFigures.Years;
        var done = trades.Where(trade => trade.Edge is not null && trade.Year is >= 0 and < SweepFigures.Years).ToArray();
        var yearTrades = new int[years];
        var yearEdge = new double?[years];

        for (var year = 0; year < years; year++)
        {
            var inYear = done.Where(trade => trade.Year == year).ToArray();

            yearTrades[year] = inYear.Length;
            yearEdge[year] = inYear.Length > 0 ? inYear.Average(trade => trade.Edge!.Value) : null;
        }

        var recent = done.Where(trade => trade.Year >= years - FamilySweep.RecentYears).ToArray();
        var trimmed = done.OrderByDescending(trade => Math.Abs(trade.Result!.Value)).Skip(FamilySweep.LargestLeftOut).ToArray();
        double? edge = done.Length > 0 ? done.Average(trade => trade.Edge!.Value) : null;
        double? error = null;

        if (done.Length > 1 && edge is { } mean)
        {
            error = Math.Sqrt(done.Sum(trade => Math.Pow(trade.Edge!.Value - mean, 2)) / (done.Length - 1) / done.Length);
        }

        var held = done.Select(trade => (double)(trade.End!.Value - trade.Entry)).Order().ToArray();
        var withIndex = done.Where(trade => trade.Index is not null).ToArray();
        var withEvery = done.Where(trade => trade.EveryMember is not null).ToArray();

        return new HeavyweightFigures(
            key,
            done.Length,
            trades.Count(trade => trade.End is null),
            edge,
            done.Length > 0 ? done.Average(trade => trade.Result!.Value) : null,
            done.Length > 0 ? done.Average(trade => trade.Cut!.Value) : null,
            withEvery.Length > 0 ? withEvery.Average(trade => trade.EveryMember!.Value) : null,
            withIndex.Length > 0 ? withIndex.Average(trade => trade.Index!.Value) : null,
            yearTrades,
            yearEdge,
            yearEdge.Count(value => value > 0),
            recent.Length > 0 ? recent.Average(trade => trade.Edge!.Value) : null,
            trimmed.Length > 0 ? trimmed.Average(trade => trade.Edge!.Value) : null,
            error,
            held.Length > 0 ? (held.Length % 2 == 1 ? held[held.Length / 2] : (held[(held.Length / 2) - 1] + held[held.Length / 2]) / 2) : null,
            held.Length > 0 ? held.Average() : null);
    }

    // The proposal over every setting's figures: the best edge among those meeting the floors, ties to the fewest dials
    // moved from the provisional setting and then the key, with every setting one step from it, the higher edge first;
    // none where no setting meets the floors.
    public static HeavyweightProposal Propose(IReadOnlyList<(HeavyweightSetting Setting, HeavyweightFigures Figures)> read)
    {
        var best = read
            .Where(one => one.Figures.MeetsFloors && one.Figures.Edge is not null)
            .OrderByDescending(one => one.Figures.Edge)
            .ThenBy(one => one.Setting.Changes(Provisional))
            .ThenBy(one => one.Figures.Key, StringComparer.Ordinal)
            .FirstOrDefault();

        if (best.Figures is null)
        {
            return new HeavyweightProposal(null, null, []);
        }

        var keys = Neighbours(best.Setting).Select(setting => setting.Key).ToHashSet(StringComparer.Ordinal);

        return new HeavyweightProposal(
            best.Setting,
            best.Figures,
            [
                .. read
                    .Where(one => keys.Contains(one.Figures.Key))
                    .OrderByDescending(one => one.Figures.Edge ?? double.MinValue)
                    .ThenBy(one => one.Figures.Key, StringComparer.Ordinal),
            ]);
    }

    // The patterns of eight years in which an edge with no effect stands above nothing in at least the floor's years,
    // of the 256 the years can fall in.
    public static int LuckPatterns() =>
        Enumerable.Range(0, 1 << SweepFigures.Years).Count(pattern => System.Numerics.BitOperations.PopCount((uint)pattern) >= FamilySweep.YearsBeating);

    // How many settings luck alone would put above nothing in the floor's years, were the settings independent.
    public static double Luck(int settings) => 1.0 * settings * LuckPatterns() / (1 << SweepFigures.Years);

    // The replay at the provisional setting held to the rebalances the night's book stored: on each stored session,
    // each sector's largest companies in order, each lead within a billionth and each leader, against the rows the
    // night wrote; a session the history does not hold, a sector either side holds alone and every row that differs
    // named.
    public static HeavyweightComparison Compare(IReadOnlyList<StoredHeavyweightRow> stored, Func<DateOnly, IReadOnlyList<HeavyweightSector>?> provisionalAt)
    {
        var differences = new List<string>();
        var sessions = 0;
        var sectors = 0;
        var matched = 0;

        foreach (var night in stored.GroupBy(row => row.Session).OrderBy(group => group.Key))
        {
            sessions++;

            if (provisionalAt(night.Key) is not { } replayed)
            {
                differences.Add(FormattableString.Invariant($"{night.Key:yyyy-MM-dd}: the history holds no reading of the session"));

                continue;
            }

            var byName = replayed.ToDictionary(sector => sector.Sector, StringComparer.Ordinal);

            foreach (var sector in night.GroupBy(row => row.Sector, StringComparer.Ordinal).OrderBy(group => group.Key, StringComparer.Ordinal))
            {
                sectors++;

                var rows = sector.OrderBy(row => row.Place).ToArray();

                if (!byName.TryGetValue(sector.Key, out var replay))
                {
                    differences.Add(FormattableString.Invariant($"{night.Key:yyyy-MM-dd} {sector.Key}: the replay reads no such sector"));

                    continue;
                }

                var same = rows.Length == replay.Largest.Count
                    && rows.Zip(replay.Largest).All(pair =>
                        pair.First.Place == pair.Second.Place
                        && string.Equals(pair.First.Ticker, pair.Second.Ticker, StringComparison.Ordinal)
                        && pair.First.Trend == pair.Second.Trend
                        && pair.First.Leader == pair.Second.Leader
                        && (pair.First.Lead, pair.Second.Lead) switch
                        {
                            (null, null) => true,
                            ({ } one, { } two) => Math.Abs(one - two) <= 1e-9,
                            _ => false,
                        });

                if (same)
                {
                    matched++;
                }
                else
                {
                    differences.Add(FormattableString.Invariant($"{night.Key:yyyy-MM-dd} {sector.Key}: stored {string.Join(", ", rows.Select(row => row.Ticker + (row.Leader ? " (leader)" : string.Empty)))}; replayed {string.Join(", ", replay.Largest.Select(ranked => ranked.Ticker + (ranked.Leader ? " (leader)" : string.Empty)))}"));
                }
            }

            foreach (var alone in replayed.Select(sector => sector.Sector).Except(night.Select(row => row.Sector), StringComparer.Ordinal))
            {
                differences.Add(FormattableString.Invariant($"{night.Key:yyyy-MM-dd} {alone}: the replay reads a sector the night stored no row for"));
            }
        }

        return new HeavyweightComparison(sessions, sectors, matched, differences);
    }
}
