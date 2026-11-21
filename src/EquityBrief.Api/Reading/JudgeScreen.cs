using EquityBrief.Core.Cards;
using EquityBrief.Core.Judge;
using EquityBrief.Core.Prices;
using EquityBrief.Web.App;

namespace EquityBrief.Api.Reading;

// The AI judge's page as the store holds it, worked when the page is read: the judge's live picks walked into its account,
// the random accounts drawn from one seed over the same nights and members, the rules' accounts on the three indices given
// an equal share, the three indices' funds held, the scoreboard, whether it beats random so far, where it looked against the
// rules' picks, the newest night's cards, and a replayed night's record apart from the live one.
// see: The AI judge's picks set none of Tonight's buys before the monthly run of 2027-02-06, and nothing else of the judge waits for that run
public static class JudgeScreen
{
    // The seed the random accounts are drawn from, so the page draws the same band each time it is read.
    public const int RandomSeed = 20261120;

    public static JudgeView Build(
        IReadOnlyList<JudgeRunRow> runs,
        IReadOnlyList<JudgePickRow> picks,
        IReadOnlyDictionary<string, IReadOnlyList<(DateOnly Session, double Open, double Close)>> bars,
        IReadOnlyDictionary<(string Ticker, DateOnly Night), double> typicals,
        IReadOnlyDictionary<DateOnly, IReadOnlyList<string>> members,
        IReadOnlyList<(string Index, PaperWalk Walk)> rules,
        IReadOnlyList<(string Ticker, DateOnly Bought, double? Result)> ruleTrades,
        IReadOnlyList<Func<DateOnly, decimal?>> funds,
        AccountSettings settings,
        Func<string, DateOnly, decimal, decimal?> roundTrip,
        int draws = JudgeAccounts.RandomDraws)
    {
        // From 24.14 the record counts the live picks of the newest judge version alone, from its first night; the picks of
        // an earlier version are drawn on their night's cards and counted in no account.
        // see: Each of the judge's picks names the three to five facts that decided it, and a pick naming none or one its sheet lacks is refused
        var every = picks.Where(pick => !pick.Replayed).ToArray();
        var version = every.OrderByDescending(pick => pick.Session).Select(pick => pick.Version).FirstOrDefault();
        var live = every.Where(pick => string.Equals(pick.Version, version, StringComparison.Ordinal)).ToArray();
        var earlier = every.Length > live.Length;
        var replayed = picks.Where(pick => pick.Replayed).ToArray();
        var newest = runs.FirstOrDefault(run => !run.Replayed);

        JudgeBars? BarsFrom(string ticker, DateOnly night)
        {
            if (!bars.TryGetValue(ticker, out var rows))
            {
                return null;
            }

            var at = rows.Select(row => row.Session).ToList().IndexOf(night);

            return at < 0 ? null : new JudgeBars([.. rows.Skip(at).Select(row => row.Session)], [.. rows.Skip(at).Select(row => row.Open)], [.. rows.Skip(at).Select(row => row.Close)]);
        }

        var trades = new Dictionary<(string, DateOnly, string, string, int), PaperTrade?>();

        PaperTrade? TradeOf(string ticker, DateOnly night, string hold, string shape, int place, double? typical)
        {
            if (!trades.TryGetValue((ticker, night, hold, shape, place), out var trade))
            {
                trade = BarsFrom(ticker, night) is { } held
                    ? JudgeAccounts.Trade(ticker, place, hold, shape, typical, held, buy => roundTrip(ticker, night, buy))
                    : null;
                trades[(ticker, night, hold, shape, place)] = trade;
            }

            return trade;
        }

        double? Typical(string ticker, DateOnly night) => typicals.TryGetValue((ticker, night), out var move) ? move : null;

        var first = earlier
            ? live.Select(pick => pick.Session).DefaultIfEmpty(DateOnly.MaxValue).Min()
            : live.Select(pick => pick.Session).Concat(runs.Where(run => !run.Replayed).Select(run => run.Session)).DefaultIfEmpty(DateOnly.MaxValue).Min();
        var sessions = bars.Values.SelectMany(rows => rows.Select(row => row.Session)).Where(session => session >= first).Distinct().Order().ToArray();
        var closes = bars.ToDictionary(pair => pair.Key, pair => pair.Value.ToDictionary(row => row.Session, row => row.Close), StringComparer.Ordinal);

        decimal? Close(string ticker, DateOnly session) =>
            closes.TryGetValue(ticker, out var held) && held.TryGetValue(session, out var close) ? Statistic.ToPrice(close) : null;

        var judged = live.Select(pick => (Pick: pick, Trade: TradeOf(pick.Ticker, pick.Session, pick.Hold, pick.Shape, pick.Rank, pick.TypicalMove))).ToArray();
        var judgeWalk = PaperAccount.Walk(settings, [.. judged.Select(one => one.Trade).OfType<PaperTrade>()], sessions, Close);

        // The random accounts: the nights the judge picked on, as many members a night as it picked.
        IReadOnlyList<(DateOnly, int, IReadOnlyList<string>)> nights =
        [
            .. live.GroupBy(pick => pick.Session).OrderBy(group => group.Key)
                .Select(group => (group.Key, group.Count(), members.TryGetValue(group.Key, out var offered) ? offered : (IReadOnlyList<string>)[])),
        ];
        var drawn = JudgeAccounts.Draws(nights, (ticker, night, hold, shape, place) => TradeOf(ticker, night, hold, shape, place, Typical(ticker, night)), RandomSeed, draws);
        var randomWalks = drawn.Select(draw => PaperAccount.Walk(settings, draw, sessions, Close)).ToArray();
        var band = JudgeAccounts.Band(randomWalks);
        var randomResults = drawn.SelectMany(draw => draw).Select(JudgeAccounts.ResultOf).OfType<double>().ToArray();

        // The three indices' funds held, given an equal share.
        var held = funds.Select(fund => JudgeAccounts.IndexHeld(settings.Size, sessions, fund)).Where(line => line.Count > 0).ToArray();
        IReadOnlyList<(DateOnly, decimal)> index = held.Length == 0
            ? []
            : [.. held.SelectMany(line => line).GroupBy(point => point.Session).Where(group => group.Count() == held.Length).OrderBy(group => group.Key).Select(group => (group.Key, group.Average(point => point.Value)))];

        var rulesWalk = rules.Count > 0 && sessions.Length > 0 ? JudgeAccounts.Rules(settings.Size, rules, sessions).Walk : null;

        // The live record, each closed pick read against the same plan bought on every member that night.
        var record = judged.Select(one =>
        {
            var result = one.Trade is { } trade ? JudgeAccounts.ResultOf(trade) : null;
            var offered = members.TryGetValue(one.Pick.Session, out var those) ? those : [];
            var over = result is null ? null : JudgeAccounts.AgainstEveryMember(result, one.Pick.Hold, one.Pick.Shape, offered.Select(member => BarsFrom(member, one.Pick.Session) is { } memberBars ? (memberBars, Typical(member, one.Pick.Session)) : ((JudgeBars, double?)?)null).OfType<(JudgeBars, double?)>());

            return (result, over);
        }).ToArray();

        (double? Result, double? OverSamePlan)[] replayedResults =
        [
            .. replayed.Select(pick => TradeOf(pick.Ticker, pick.Session, pick.Hold, pick.Shape, pick.Rank, pick.TypicalMove) is { } trade ? JudgeAccounts.ResultOf(trade) : null)
                .Select(result => (result, default(double?))),
        ];

        var tonight = newest is null ? [] : every.Where(pick => pick.Session == newest.Session).OrderBy(pick => pick.Rank).ToArray();

        return new JudgeView(
            newest?.Session,
            newest?.Outcome,
            newest?.Why,
            [.. tonight.Select(pick => new JudgeCardView(pick.Rank, tonight.Length, pick.Ticker, pick.Company, pick.Sector, pick.Size, pick.Reason, pick.LastClose, pick.Stop, pick.Target, pick.GiveUp, pick.Hold, pick.Shape) with
            {
                Named = pick.Named is { } named ? [.. named.Select(key => Fact(key, pick.Sheet))] : null,
                Rest = [.. JudgeFacts.In(JudgeRound.Sheet).Where(field => pick.Named?.Contains(field.Key, StringComparer.Ordinal) != true).Select(field => Fact(field.Key, pick.Sheet))],
            })],
            JudgeScore.Of(record),
            JudgeScore.AgainstRandom(judgeWalk.Values.Count > 0 ? judgeWalk.Values[^1].Value : null, band.Count > 0 ? band[^1] : null),
            randomResults.Length == 0 ? null : randomResults.Average(),
            JudgeScore.Looked([.. judged.Select((one, at) => (one.Pick.Ticker, one.Trade?.Bought ?? one.Pick.Session.AddDays(1), record[at].result))], ruleTrades),
            [
                new JudgeLineView(SinglePageApp.JudgeLineNames[0], judgeWalk.Values),
                new JudgeLineView(SinglePageApp.JudgeLineNames[1], rulesWalk?.Values ?? []),
                new JudgeLineView(SinglePageApp.JudgeLineNames[2], [.. band.Select(row => (row.Session, row.Median))]),
                new JudgeLineView(SinglePageApp.JudgeLineNames[3], index),
            ],
            band,
            replayed.Length == 0 ? null : JudgeScore.Of(replayedResults),
            replayed.Select(pick => pick.Session).Distinct().Count())
        {
            FirstNight = live.Length == 0 ? null : live.Min(pick => pick.Session),
            FirstOpen = live.Length == 0 ? null : OpenAfter(live.Min(pick => pick.Session)),
            Bought = judged.Any(one => one.Trade is not null),
            CountedFrom = earlier && live.Length > 0 ? live.Min(pick => pick.Session) : null,
        };
    }

    // One fact of a pick's sheet: its words and its value as the judge was shown it, a field it held none of not on file.
    static JudgeFactView Fact(string key, IReadOnlyDictionary<string, string> sheet)
    {
        var field = JudgeFacts.All.FirstOrDefault(one => one.Key == key);
        var shown = sheet.TryGetValue(key, out var text) ? text : JudgeFacts.NotOnFile;

        return new JudgeFactView(field?.Words ?? key, shown, JudgeFactView.Compared(key, shown));
    }

    // From 24.13 the first session the exchange trades after a night, the open its picks buy at.
    public static DateOnly OpenAfter(DateOnly night)
    {
        var day = night.AddDays(1);

        while (!Core.Bars.ExchangeClosures.IsSession(day))
        {
            day = day.AddDays(1);
        }

        return day;
    }
}
