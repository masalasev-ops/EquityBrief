using EquityBrief.Core.Cards;
using EquityBrief.Core.Loop;
using EquityBrief.Core.Prices;

namespace EquityBrief.Core.Judge;

// One member's sessions from a night on, the night first: each session's date, open and close, as statistics.
public sealed record JudgeBars(IReadOnlyList<DateOnly> Sessions, double[] Opens, double[] Closes);

// What the median random account and its middle band stood at on a session.
public sealed record JudgeBand(DateOnly Session, decimal Low, decimal Median, decimal High);

// The AI judge's four accounts, walked by the core's one account function with the same money, sizing and costs: the
// rules', every live rule's trades on the three indices as Paper trading averages them; the judge's, each pick bought at
// the open after its night and walked under the plan it chose, its stop and take-profit set from the price it paid; random
// picks', as many members each night as the judge picked, each under a plan drawn from the same menu, by a seeded draw
// repeated 1,000 times and drawn as its median and the band 8 in 10 of them fall in; and the index held. Until the readings
// for every stock on every session stand, each of the judge's picks is read against the same plan bought on every member
// that night. No store holds a figure of any of them.
// see: The AI judge's picks set none of Tonight's buys before the monthly run of 2027-02-06, and nothing else of the judge waits for that run
// see: The readings for every stock on every session are built once before the reviewer, the judge's comparison reading the same plan on every stock until then
public static class JudgeAccounts
{
    public const int RandomDraws = 1000;

    // The middle band of the random accounts, the share of them in 100 below its low and below its high.
    public const int BandLow = 10;

    public const int BandHigh = 90;

    // Every plan the menu holds, a hold with a take-profit, the plans a random pick is drawn from.
    public static IReadOnlyList<(string Hold, string Shape)> Plans { get; } =
        [.. JudgeMenu.Holds.SelectMany(hold => JudgeMenu.Shapes.Select(shape => (hold.Words, shape)))];

    // A plan's sale from the price it buys at and its risk: the whole at the target, or half there and the rest on a
    // give-up price its risk behind the highest close; none off the menu.
    public static PlanSale? SaleOf(string hold, string shape, double fill, double risk)
    {
        if (JudgeMenu.HoldOf(hold) is null || !JudgeMenu.IsShape(shape) || !(risk > 0))
        {
            return null;
        }

        var target = fill + (Statistic.FromRatio(JudgeMenu.TargetRisks) * risk);

        return string.Equals(shape, JudgeMenu.Whole, StringComparison.Ordinal)
            ? new PlanSale(target, null, [])
            : new PlanSale(null, risk, []) { HalfAt = target };
    }

    // The risk a plan puts up on a member: its typical daily move times the hold's stop in moves.
    public static double? RiskOf(string hold, double? typicalMove) =>
        JudgeMenu.HoldOf(hold) is { } held && typicalMove is > 0 ? typicalMove.Value * held.StopMoves : null;

    // One pick as the account trades it: bought at the open of the session after its night, its stop its risk under the
    // price paid, sold as its plan's walk sells it, at most its hold's sessions; open where its walk has not ended; none
    // where no session after the night is held to buy at, or the plan places no trade. The round trip is read at the buy.
    public static PaperTrade? Trade(string ticker, int place, string hold, string shape, double? typicalMove, JudgeBars bars, Func<decimal, decimal?> roundTripAt)
    {
        if (bars.Sessions.Count < 2 || bars.Opens.Length < 2 || !(bars.Opens[1] > 0)
            || RiskOf(hold, typicalMove) is not { } risk || JudgeMenu.HoldOf(hold) is not { } held)
        {
            return null;
        }

        var fill = bars.Opens[1];

        if (!(fill - risk > 0) || SaleOf(hold, shape, fill, risk) is not { } sale)
        {
            return null;
        }

        var buy = Statistic.ToPrice(fill);
        var stop = Statistic.ToPrice(fill - risk);
        var outcome = PlanPoints.Walk(bars.Closes, bars.Opens, 1, fill, fill - risk, sale, held.Sessions);
        var ended = outcome is { } done && 1 + done.Sessions < bars.Sessions.Count ? bars.Sessions[1 + done.Sessions] : (DateOnly?)null;

        return new PaperTrade(
            ticker,
            bars.Sessions[1],
            buy,
            stop,
            ended,
            ended is null ? null : Statistic.ToPrice(outcome!.Sold),
            roundTripAt(buy),
            place,
            Listed: bars.Sessions[0],
            Target: sale.Target is { } whole ? Statistic.ToPrice(whole) : Statistic.ToPrice(sale.HalfAt!.Value),
            Trail: sale.Trail is { } trail ? Statistic.ToPrice(trail) : null,
            Cap: held.Sessions);
    }

    // The random accounts' trades: for each draw and each night the judge picked on, as many members as it picked, each
    // drawn without repeat from the night's members and each under a plan drawn from the menu, all from the one seed; a
    // member its plan places no trade on is drawn past, so a draw holds no more trades a night than the judge's count.
    public static IReadOnlyList<IReadOnlyList<PaperTrade>> Draws(
        IReadOnlyList<(DateOnly Night, int Count, IReadOnlyList<string> Members)> nights,
        Func<string, DateOnly, string, string, int, PaperTrade?> trade,
        int seed,
        int draws = RandomDraws)
    {
        var random = new Random(seed);
        var accounts = new List<IReadOnlyList<PaperTrade>>();

        for (var draw = 0; draw < draws; draw++)
        {
            var trades = new List<PaperTrade>();

            foreach (var (night, count, members) in nights)
            {
                var pool = members.ToArray();
                var taken = 0;

                for (var at = 0; at < pool.Length && taken < count; at++)
                {
                    var other = at + random.Next(pool.Length - at);

                    (pool[at], pool[other]) = (pool[other], pool[at]);

                    var (hold, shape) = Plans[random.Next(Plans.Count)];

                    if (trade(pool[at], night, hold, shape, taken + 1) is { } one)
                    {
                        trades.Add(one);
                        taken++;
                    }
                }
            }

            accounts.Add(trades);
        }

        return accounts;
    }

    // The random accounts' median and middle band on each session their walks share, each read off the accounts' values
    // sorted: the low below which a tenth of them stand, the median and the high below which nine tenths stand.
    public static IReadOnlyList<JudgeBand> Band(IReadOnlyList<PaperWalk> walks)
    {
        if (walks.Count == 0)
        {
            return [];
        }

        var sessions = walks[0].Values.Select(value => value.Session).ToArray();
        var bands = new List<JudgeBand>();

        for (var at = 0; at < sessions.Length; at++)
        {
            var values = walks.Where(walk => at < walk.Values.Count).Select(walk => walk.Values[at].Value).Order().ToArray();

            decimal At(int share) => values[(int)Math.Round(share / 100.0 * (values.Length - 1), MidpointRounding.AwayFromZero)];

            bands.Add(new JudgeBand(sessions[at], At(BandLow), At(50), At(BandHigh)));
        }

        return bands;
    }

    // The index held: the account's size grown by the fund's close over its close on the first session it holds, none
    // before then.
    public static IReadOnlyList<(DateOnly Session, decimal Value)> IndexHeld(decimal size, IReadOnlyList<DateOnly> sessions, Func<DateOnly, decimal?> fund)
    {
        var start = sessions.Select(fund).FirstOrDefault(close => close is > 0);

        return start is not { } first
            ? []
            : [.. sessions.Where(session => fund(session) is not null).Select(session => (session, size * fund(session)!.Value / first))];
    }

    // The rules' account: each index's averaged live rules' account given an equal share again, over the sessions every one
    // of them counts.
    public static AveragedWalk Rules(decimal size, IReadOnlyList<(string Index, PaperWalk Walk)> indices, IReadOnlyList<DateOnly> sessions, Func<DateOnly, decimal?>? fund = null) =>
        PaperAccount.Averaged(size, indices, sessions, fund);

    // A pick read against the same plan bought on every member that night: its own result in its risk less the mean of
    // the members', each bought at its own next open with its risk its own typical move times the hold's stop; none where
    // the pick's walk has not ended or no member's has.
    public static double? AgainstEveryMember(double? pickResult, string hold, string shape, IEnumerable<(JudgeBars Bars, double? TypicalMove)> members)
    {
        if (pickResult is not { } own || JudgeMenu.HoldOf(hold) is not { } held)
        {
            return null;
        }

        var results = members.Select(member => RiskOf(hold, member.TypicalMove) is { } risk
            ? PlanPoints.MemberResult(member.Bars.Closes, member.Bars.Opens, 0, risk, fill => fill - risk, fill => SaleOf(hold, shape, fill, risk), held.Sessions)
            : null);

        return PlanPoints.BenchmarkOf(results) is { } mean ? own - mean : null;
    }

    // A walked trade's result in the risk its plan put up, none while it is open.
    public static double? ResultOf(PaperTrade trade) =>
        trade.Sale is { } sale && trade.Stop is { } stop && trade.Buy > stop
            ? Statistic.FromRatio((sale - trade.Buy) / (trade.Buy - stop))
            : null;
}
