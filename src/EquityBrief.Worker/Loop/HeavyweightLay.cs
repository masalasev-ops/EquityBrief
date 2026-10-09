using System.Collections.Concurrent;
using EquityBrief.Core.Families;
using EquityBrief.Core.Loop;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Readings;
using EquityBrief.Core.Sweep;
using EquityBrief.Worker.Sweep;

namespace EquityBrief.Worker.Loop;

// One of the six settings' books over the whole history: its holdings as walked, the same after each one's round trip,
// and its months, the tester's unit.
public sealed record HeavyweightBook(HeavyweightSixSetting Setting, IReadOnlyList<HeavyweightTrade> Trades, IReadOnlyList<HeavyweightTrade> Costed, IReadOnlyList<(int Opens, double? Edge)> Months);

// The sector heavyweights' history laid out once for the tester on an index, as the six settings' search lays it: the
// tape, each month's first session, the members as each rebalance reads them, what clears the index's quality and
// floors, and design (b)'s industries' leads over the S&P 500 as it stood; each setting's book is walked from them.
public sealed class HeavyweightLay
{
    readonly LoopRead read;
    readonly HeavyweightTape tape;
    readonly IReadOnlyDictionary<int, HeavyweightSession> sessions;
    readonly IReadOnlyList<int> months;
    readonly IReadOnlyDictionary<string, int> names;
    readonly Func<int, int, IReadOnlyList<string>> leading;
    readonly Func<int, string?> industryOf;
    readonly ConcurrentDictionary<(int From, int To), double?> every = new();

    HeavyweightLay(LoopRead read, HeavyweightTape tape, IReadOnlyDictionary<int, HeavyweightSession> sessions, IReadOnlyList<int> months, Func<int, int, IReadOnlyList<string>> leading, Func<int, string?> industryOf)
    {
        this.read = read;
        this.tape = tape;
        this.sessions = sessions;
        this.months = months;
        this.leading = leading;
        this.industryOf = industryOf;
        names = tape.Tickers.Select((ticker, at) => (ticker, at)).ToDictionary(pair => pair.ticker, pair => pair.at, StringComparer.Ordinal);
    }

    // Each month's first session from the first scored, the months a book's edge is read over.
    public IReadOnlyList<int> Months => months;

    // The S&P 500's book reads its index's own series, and the S&P 400's and 600's their funds'; every month's first
    // session and every week's are laid, so a book rebalancing weekly reads its sessions as a monthly one does.
    public static async Task<HeavyweightLay> ReadAsync(LoopRead read, SweepHistory history, DateOnly through, Action<string> progress, CancellationToken cancellation)
    {
        var months = HeavyweightSweep.Rebalances(read.Calendar, read.FirstScored, HeavyweightPeriod.Month);
        var weeks = HeavyweightSweep.Rebalances(read.Calendar, read.FirstScored, HeavyweightPeriod.Week);
        var fund = read.Index == WalkForwardTester.LargeIndex
            ? (await history.MarketAsync(through, cancellation)).FirstOrDefault(series => series.Series == "GSPC")
            : (await history.SeriesOfAsync([IndexSweepRunner.IndexFunds[read.Index]], through, cancellation)).FirstOrDefault();
        var (tape, sessions) = HeavyweightSweep.Lay(read.Inputs, read.Companies, fund, months.Union(weeks).ToHashSet(), read.FirstScored);

        if (read.Index == WalkForwardTester.LargeIndex)
        {
            return new HeavyweightLay(read, tape, sessions, months, (_, _) => [], _ => null);
        }
        var industries = await history.IndustriesAsync(cancellation);
        var spy = (await history.SeriesOfAsync(["SPY"], through, cancellation)).FirstOrDefault();

        progress("reading the S&P 500 as it stood for its industries' leads");

        var large = await history.ReadAsync(through, null, cancellation);
        var industryReturns = new SweepIndustries(large, read.Companies, industries);
        var spyCloses = (spy?.Closes ?? []).ToDictionary(pair => pair.Session, pair => pair.Close);
        var leads = new ConcurrentDictionary<(int From, int To), IReadOnlyList<string>>();
        var calendar = read.Calendar;

        // The industries whose S&P 500 members' value-weighted return over the sessions leads SPY's, the strongest first,
        // none where SPY holds no close at either end, as the six settings' search reads them.
        IReadOnlyList<string> Leading(int from, int to) => leads.GetOrAdd((from, to), span =>
        {
            var (start, day) = (calendar[span.From], calendar[span.To]);

            if (!spyCloses.TryGetValue(day, out var now) || !spyCloses.TryGetValue(start, out var then) || then <= 0)
            {
                return [];
            }

            var market = (now / then) - 1.0;

            return [.. industryReturns.Between(start, day).Select(pair => (Industry: pair.Key, Lead: pair.Value - market)).Where(pair => pair.Lead > 0).OrderByDescending(pair => pair.Lead).ThenBy(pair => pair.Industry, StringComparer.Ordinal).Select(pair => pair.Industry)];
        });

        return new HeavyweightLay(read, tape, sessions, months, Leading, name => industries.GetValueOrDefault(read.Ticker(name)));
    }

    string? SectorOf(int name) => read.Companies.Companies.GetValueOrDefault(read.Ticker(name)).Sector;

    bool ClearsOn(int name, int session, IndexQuality quality) =>
        Array.BinarySearch(read.Series[name].SessionAt, session) is var bar && bar >= 0
        && IndexSweepRunner.Clears(read.Index, read.Series[name], bar, read.Income.GetValueOrDefault(read.Ticker(name)) ?? [], quality, 1m, SectorOf(name));

    // One setting's book over the whole history: its rebalances read as the six settings' search reads them, its holdings
    // walked, each after its round trip, and its months.
    public HeavyweightBook Book(HeavyweightSixSetting setting, bool designA)
    {
        var rebalances = HeavyweightSix.RebalancesOf(setting, read.Calendar, months).ToDictionary(
            session => session,
            session => designA
                ? HeavyweightSix.ReadA(tape, sessions[session], setting, names, ClearsOn)
                : HeavyweightSix.ReadB(tape, session, setting, leading, industryOf, SectorOf, ClearsOn));
        var trades = HeavyweightSweep.Walk(tape, rebalances, designA ? setting.Leaders!.Exit : HeavyweightExit.Drop, (from, to) => every.GetOrAdd((from, to), span => HeavyweightSweep.EveryMember(tape, span.From, span.To)));
        var holdings = trades
            .Select(trade => new BookHolding(
                trade.Entry,
                trade.End,
                Cost(trade),
                (from, to) => Return(trade.Name, from, tape.LastMemberClose[trade.Name][to]),
                (from, to) => CutReturn(CutOf(rebalances, trade), from, to)))
            .ToArray();

        return new HeavyweightBook(setting, trades, [.. trades.Select(Costed)], BookMonths.Edges(months, holdings));
    }

    // A book at one of the sweep's own settings over the whole history, as the night's book reads it: on the S&P 500
    // every member at each rebalance of the setting's period, and on the S&P 400 or 600 the members clearing the index's
    // floors and gate; its holdings walked under the setting's exit, each after its round trip, and its months.
    public HeavyweightBook BookOf(HeavyweightSetting setting, string words)
    {
        var large = read.Index == WalkForwardTester.LargeIndex;
        var rebalances = HeavyweightSweep.Rebalances(read.Calendar, read.FirstScored, setting.Period).ToDictionary(
            session => session,
            session =>
            {
                var laid = sessions[session];
                var kept = large ? laid : laid with { Members = [.. laid.Members.Where(member => ClearsOn(member.Name, session, IndexQuality.Profit))] };

                return HeavyweightSweep.Read(kept, setting, names);
            });
        var trades = HeavyweightSweep.Walk(tape, rebalances, setting.Exit, (from, to) => every.GetOrAdd((from, to), span => HeavyweightSweep.EveryMember(tape, span.From, span.To)));
        var holdings = trades
            .Select(trade => new BookHolding(
                trade.Entry,
                trade.End,
                Cost(trade),
                (from, to) => Return(trade.Name, from, tape.LastMemberClose[trade.Name][to]),
                (from, to) => CutReturn(CutOf(rebalances, trade), from, to)))
            .ToArray();

        return new HeavyweightBook(new HeavyweightSixSetting(0, words, false, IndexQuality.Profit, false, setting), trades, [.. trades.Select(Costed)], BookMonths.Edges(months, holdings));
    }

    // The size cut a holding was bought against: its sector's at the rebalance that bought it.
    static int[] CutOf(IReadOnlyDictionary<int, HeavyweightRebalance> rebalances, HeavyweightTrade trade) =>
        rebalances.TryGetValue(trade.Entry, out var rebalance)
            ? rebalance.Sectors.FirstOrDefault(sector => sector.Sector == trade.Sector).Cut ?? []
            : [];

    // A name's return from one session's close to a later one's, none where either holds no close.
    double? Return(int name, int from, int to) =>
        to >= from && from >= 0 && tape.Close[name][from] > 0 && !double.IsNaN(tape.Close[name][to])
            ? (tape.Close[name][to] / tape.Close[name][from]) - 1.0
            : null;

    // The size cut's return over the same sessions, each member read to its last close as a member and one holding none
    // counted as nothing, as the walk scores a holding's cut.
    double? CutReturn(int[] cut, int from, int to) =>
        cut.Length > 0 ? cut.Average(one => Return(one, from, tape.LastMemberClose[one][to]) ?? 0.0) : null;

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

        var value = CompanyValue.On(new SessionClose(bought.Session, bought.Close, entry), read.Companies.Counts.GetValueOrDefault(read.Ticker(trade.Name)) ?? [], read.Companies.Splits.GetValueOrDefault(read.Ticker(trade.Name)) ?? []);

        return TradeCost.InPercent(value, entry, exit) / 100.0;
    }

    // A holding's result less its round trip, as the six settings' search scores it: a holding ended on a session its
    // stock holds no bar for is scored as walked.
    HeavyweightTrade Costed(HeavyweightTrade trade)
    {
        var series = read.Series[trade.Name];

        return trade.Result is { } result && trade.End is { } end
            && Array.BinarySearch(series.SessionAt, trade.Entry) >= 0 && Array.BinarySearch(series.SessionAt, end) >= 0
                ? trade with { Result = result - Cost(trade) }
                : trade;
    }
}
