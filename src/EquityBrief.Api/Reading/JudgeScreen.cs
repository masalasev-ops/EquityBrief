using EquityBrief.Core.Cards;
using EquityBrief.Core.Judge;
using EquityBrief.Core.Prices;
using EquityBrief.Web.App;

namespace EquityBrief.Api.Reading;

// The AI stock picker's page as the store holds it, worked when the page is read: the judge's live picks walked into its account,
// the random accounts drawn from one seed over the same nights and members, the rules' accounts on the three indices given
// an equal share, the three indices' funds held, the scoreboard, whether it beats random so far, where it looked against the
// rules' picks, the newest night's cards, and a replayed night's record apart from the live one.
// see: The AI stock picker's picks set none of Tonight's buys before the monthly run of 2027-02-06, and nothing else of the picker waits for that run
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
        int draws = JudgeAccounts.RandomDraws,
        DateOnly? drawnNight = null)
    {
        // From 24.14 the record counts the live picks of the newest judge version alone, from its first night; and from the
        // 24.14 correction of 2026-11-21 each version's picks are bought and walked to their exits in an account of their own,
        // numbered in the order the versions first ran, an earlier version's drawn apart under its number.
        // see: Each of the picker's picks names the three to five facts that decided it, and a pick naming none or one its sheet lacks is refused
        // see: Each picker version's picks are bought and walked in an account of its own, and the record counts the newest version's
        var every = picks.Where(pick => !pick.Replayed).ToArray();
        var versions = Versions(every, runs);
        var version = versions.Length == 0 ? string.Empty : versions.OrderByDescending(one => one.Last).ThenByDescending(one => one.First).First().Key;
        var live = every.Where(pick => KeyOf(pick.Version) == version).ToArray();
        var earlier = versions.Length > 1;
        var replayed = picks.Where(pick => pick.Replayed).ToArray();
        // From 24.15 Home drawn for a past night reads that night's run and picks in place of the newest.
        var newest = runs.FirstOrDefault(run => !run.Replayed && (drawnNight is null || run.Session == drawnNight));

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
            ? versions.Single(one => one.Key == version).First
            : live.Select(pick => pick.Session).Concat(runs.Where(run => !run.Replayed).Select(run => run.Session)).DefaultIfEmpty(DateOnly.MaxValue).Min();
        var allSessions = bars.Values.SelectMany(rows => rows.Select(row => row.Session)).Distinct().Order().ToArray();
        var sessions = allSessions.Where(session => session >= first).ToArray();
        var closes = bars.ToDictionary(pair => pair.Key, pair => pair.Value.ToDictionary(row => row.Session, row => row.Close), StringComparer.Ordinal);

        decimal? Close(string ticker, DateOnly session) =>
            closes.TryGetValue(ticker, out var held) && held.TryGetValue(session, out var close) ? Statistic.ToPrice(close) : null;

        // Each earlier version's account: its picks bought at the open after their night and walked under their plans from its
        // first night, the same money, sizing and costs as the record's, each card saying what its account did with it.
        JudgeVersionView Apart((string Key, DateOnly First, DateOnly Last) one, int number)
        {
            var its = every.Where(pick => KeyOf(pick.Version) == one.Key).OrderBy(pick => pick.Session).ThenBy(pick => pick.Rank).ToArray();
            var itsTrades = its.Select(pick => TradeOf(pick.Ticker, pick.Session, pick.Hold, pick.Shape, pick.Rank, pick.TypicalMove)).ToArray();
            var walk = PaperAccount.Walk(settings, [.. itsTrades.OfType<PaperTrade>()], [.. allSessions.Where(session => session >= one.First)], Close);
            var cards = its.Select((pick, at) => Card(pick, its.Count(other => other.Session == pick.Session)) with { Night = pick.Session, Held = HeldOf(itsTrades[at], walk) }).ToArray();

            double? noOverSamePlan = null;
            (double? Result, double? OverSamePlan)[] results = [.. itsTrades.Select(trade => (trade is { } held ? JudgeAccounts.ResultOf(held) : null, noOverSamePlan))];

            return new JudgeVersionView(number, one.First, one.Last, cards, JudgeScore.Of(results), walk.Values);
        }

        var numbered = versions.Select((one, at) => (One: one, Number: at + 1)).ToArray();
        var apart = numbered.Where(pair => pair.One.Key != version).Select(pair => Apart(pair.One, pair.Number)).ToArray();
        int? recordNumber = earlier ? numbered.Single(pair => pair.One.Key == version).Number : null;

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

        // From 24.15 Home drawn for a past night an earlier version picked on names that version's account.
        var picksFrom = tonight.Length == 0 ? null : numbered.Where(pair => pair.One.Key == KeyOf(tonight[0].Version) && pair.One.Key != version).Select(pair => apart.Single(one => one.Number == pair.Number)).FirstOrDefault();

        return new JudgeView(
            newest?.Session,
            newest?.Outcome,
            newest?.Why,
            [.. tonight.Select(pick => Card(pick, tonight.Length))],
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
            CountedFrom = earlier ? first : null,
            Earlier = apart,
            RecordNumber = recordNumber,
            PicksFrom = picksFrom,
        };
    }

    // Why a pick its account's walk did not take is not bought there.
    public const string NotTaken = "its account's walk did not take it";

    // A version's key: its prompt version, a pick under no pin read as one version of its own.
    static string KeyOf(string? version) => version ?? string.Empty;

    // Each judge version's first and last night, read off its live picks and the live runs under its pin, in the order the
    // versions first ran.
    static (string Key, DateOnly First, DateOnly Last)[] Versions(IReadOnlyList<JudgePickRow> every, IReadOnlyList<JudgeRunRow> runs) =>
    [
        .. every.Select(pick => (Key: KeyOf(pick.Version), pick.Session))
            .Concat(runs.Where(run => !run.Replayed && run.Version is not null).Select(run => (Key: run.Version!, run.Session)))
            .GroupBy(one => one.Key, StringComparer.Ordinal)
            .Select(group => (group.Key, First: group.Min(one => one.Session), Last: group.Max(one => one.Session)))
            .OrderBy(one => one.First)
            .ThenBy(one => one.Key, StringComparer.Ordinal),
    ];

    // One pick's card: its rank of its night's picks, what it saw and the plan code set from its last close.
    static JudgeCardView Card(JudgePickRow pick, int of) =>
        new JudgeCardView(pick.Rank, of, pick.Ticker, pick.Company, pick.Sector, pick.Size, pick.Reason, pick.LastClose, pick.Stop, pick.Target, pick.GiveUp, pick.Hold, pick.Shape) with
        {
            Named = pick.Named is { } named ? [.. named.Select(key => Fact(key, pick.Sheet))] : null,
            Rest = [.. JudgeFacts.In(JudgeRound.Sheet).Where(field => pick.Named?.Contains(field.Key, StringComparer.Ordinal) != true).Select(field => Fact(field.Key, pick.Sheet))],
        };

    // What an account did with a pick: bought at the open after its night at the price it paid, its stop and take-profit from
    // that price, and its sale where its walk ended; skipped where the account took it up for none; none before it is bought.
    static JudgeHeld? HeldOf(PaperTrade? trade, PaperWalk walk)
    {
        if (trade is null)
        {
            return null;
        }

        var taken = walk.Trades.FirstOrDefault(one => one.Trade.Ticker == trade.Ticker && one.Trade.Bought == trade.Bought && one.Trade.Place == trade.Place);

        return new JudgeHeld(trade.Bought, trade.Buy, trade.Stop, trade.Target, trade.Trail, trade.Ended, trade.Sale, JudgeAccounts.ResultOf(trade), taken is null ? NotTaken : taken.Skipped);
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
