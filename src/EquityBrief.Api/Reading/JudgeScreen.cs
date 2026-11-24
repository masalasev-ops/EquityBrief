using EquityBrief.Core.Cards;
using EquityBrief.Core.Judge;
using EquityBrief.Core.Prices;
using EquityBrief.Web.App;

namespace EquityBrief.Api.Reading;

// The AI stock picker's page as the store holds it, worked when the page is read: the judge's live picks walked into its account,
// the random accounts drawn from one seed over the same nights and members, the rules' accounts on the three indices given
// an equal share, the three indices' funds held, the scoreboard, whether it beats random so far, where it looked against the
// rules' picks, the newest night's cards, and a replayed night's record apart from the live one. From 24.24 it is worked for
// one index at a time: that index's picker version and its picks, the best quarter of the marked, its variant "a fixed 5"
// in an account of its own beside it, random picks from that index's members, that index's rules and that index's fund;
// a version from before 24.24, which picked across the three indices, drawn apart under its number on each.
// see: The AI stock picker's picks set none of Tonight's buys before the monthly run of 2027-02-06, and nothing else of the picker waits for that run
// see: The AI stock picker picks within each index, the best quarter of the stocks it marks and at most 5
public static class JudgeScreen
{
    // The seed the random accounts are drawn from, so the page draws the same band each time it is read.
    public const int RandomSeed = 20261120;

    // A pick or run of one index: that index's own version's alone. One made across the three indices before 24.24 is no
    // index's, and is drawn once in a region of its own.
    static bool Within(string? runIndex, string index) => string.Equals(runIndex, index, StringComparison.Ordinal);

    public static JudgeView Build(
        string within,
        IReadOnlyList<JudgeRunRow> runs,
        IReadOnlyList<JudgePickRow> picks,
        IReadOnlyDictionary<string, IReadOnlyList<(DateOnly Session, double Open, double Close, double Low)>> bars,
        IReadOnlyDictionary<(string Ticker, DateOnly Night), double> typicals,
        IReadOnlyDictionary<DateOnly, IReadOnlyList<(string Index, string Ticker)>> everyMember,
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
        // The night an index's version that has not run picks first: the session after the newest night any version ran.
        var startsOn = runs.Where(run => !run.Replayed).Select(run => (DateOnly?)run.Session).Max() is { } newestRan ? OpenAfter(newestRan) : (DateOnly?)null;

        // From the operator's correction of 2026-11-23 the newest night the earlier version picked across the three indices, or
        // the night drawn where it picked then, which Home counts where the index's own version has not picked since.
        var acrossRan = runs.Where(run => !run.Replayed && run.Index is null && (drawnNight is null || run.Session == drawnNight)).Select(run => (DateOnly?)run.Session).Max();

        // From the operator's correction of 2026-11-23 every night a version ran, and the night the page draws.
        // see: The AI stock picker's page draws one night at a time, and its count line says what decided the number
        DateOnly[] ranNights = [.. runs.Where(run => !run.Replayed).Select(run => run.Session).Distinct().OrderDescending()];
        var shown = drawnNight is { } chosenNight && ranNights.Contains(chosenNight) ? chosenNight : ranNights.Length == 0 ? (DateOnly?)null : ranNights[0];

        runs =[.. runs.Where(run => Within(run.Index, within) && run.Standing is null or "running")];

        var started = runs.Any(run => !run.Replayed);

        var ranked = picks.Where(pick => !pick.Replayed && Within(pick.RunIndex, within)).ToArray();
        var every = ranked.Where(pick => pick.InQuarter).ToArray();
        var versions = Versions(every, runs);
        var version = versions.Length == 0 ? string.Empty : versions.OrderByDescending(one => one.Last).ThenByDescending(one => one.First).First().Key;
        var live = every.Where(pick => KeyOf(pick) == version).ToArray();

        // The members its random picks and its same plan are drawn from: the index's, or every index's for a version that
        // picked across the three before 24.24.
        var across = !version.Contains(KeyMark, StringComparison.Ordinal);
        IReadOnlyDictionary<DateOnly, IReadOnlyList<string>> members = everyMember.ToDictionary(
            pair => pair.Key,
            pair => (IReadOnlyList<string>)[.. pair.Value.Where(member => across || member.Index == within).Select(member => member.Ticker).Distinct(StringComparer.Ordinal)]);
        var earlier = versions.Length > 1;
        var replayed = picks.Where(pick => pick.Replayed && Within(pick.RunIndex, within) && pick.InQuarter).ToArray();
        // From 24.15 Home drawn for a past night reads that night's run and picks in place of the newest.
        var newest = runs.FirstOrDefault(run => !run.Replayed && (drawnNight is null || run.Session == drawnNight));

        JudgeBars? BarsFrom(string ticker, DateOnly night)
        {
            if (!bars.TryGetValue(ticker, out var rows))
            {
                return null;
            }

            var at = rows.Select(row => row.Session).ToList().IndexOf(night);

            return at < 0 ? null : new JudgeBars([.. rows.Skip(at).Select(row => row.Session)], [.. rows.Skip(at).Select(row => row.Open)], [.. rows.Skip(at).Select(row => row.Close)], [.. rows.Skip(at).Select(row => row.Low)]);
        }

        // From the operator's answers of 2026-11-24 a pick of a night from the range's first buys within its range, the
        // random picks of those nights as well, so the two are bought the same way; a pick its range did not buy keeps the
        // trade the open would have made, which its card reads its result from.
        // see: Every picker version buys within the approved range from the night of 2026-11-24, the picks of earlier nights bought at the open as they were
        var trades = new Dictionary<(string, DateOnly, string, string, int), JudgePlaced>();

        JudgePlaced PlacedOf(string ticker, DateOnly night, string hold, string shape, int place, double? typical)
        {
            if (!trades.TryGetValue((ticker, night, hold, shape, place), out var placed))
            {
                placed = BarsFrom(ticker, night) is { } held
                    ? JudgeAccounts.Placed(ticker, place, hold, shape, typical, held, buy => roundTrip(ticker, night, buy), JudgeAccounts.WidthsOn(night))
                    : new JudgePlaced(null, null);
                trades[(ticker, night, hold, shape, place)] = placed;
            }

            return placed;
        }

        PaperTrade? TradeOf(string ticker, DateOnly night, string hold, string shape, int place, double? typical) =>
            PlacedOf(ticker, night, hold, shape, place, typical).Trade;

        // What a pick's account did with it: as its walk took it, or for a pick its range did not buy, the side it opened on
        // with the trade the open would have made.
        JudgeHeld? HeldFor(JudgePickRow pick, PaperTrade? trade, PaperWalk walk)
        {
            if (PlacedOf(pick.Ticker, pick.Session, pick.Hold, pick.Shape, pick.Rank, pick.TypicalMove).NotBought is not { } side)
            {
                return HeldOf(trade, walk);
            }

            var open = BarsFrom(pick.Ticker, pick.Session) is { } held
                ? JudgeAccounts.Trade(pick.Ticker, pick.Rank, pick.Hold, pick.Shape, pick.TypicalMove, held, buy => roundTrip(pick.Ticker, pick.Session, buy))
                : null;

            return new JudgeHeld(open?.Bought ?? OpenAfter(pick.Session), open?.Buy ?? pick.LastClose, open?.Stop, open?.Target, open?.Trail, open?.Ended, open?.Sale, open is null ? null : JudgeAccounts.ResultOf(open), NotBoughtWords(side));
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
        JudgeVersionView Apart((string Key, DateOnly First, DateOnly Last) one, int number, bool fixedFive = false)
        {
            var its = (fixedFive ? ranked : every).Where(pick => KeyOf(pick) == one.Key).OrderBy(pick => pick.Session).ThenBy(pick => pick.Rank).ToArray();
            var itsTrades = its.Select(pick => TradeOf(pick.Ticker, pick.Session, pick.Hold, pick.Shape, pick.Rank, pick.TypicalMove)).ToArray();
            var walk = PaperAccount.Walk(settings, [.. itsTrades.OfType<PaperTrade>()], [.. allSessions.Where(session => session >= one.First)], Close);
            var cards = its.Select((pick, at) => Card(pick, its.Count(other => other.Session == pick.Session)) with { Night = pick.Session, Held = HeldFor(pick, itsTrades[at], walk) }).ToArray();

            double? noOverSamePlan = null;
            (double? Result, double? OverSamePlan)[] results = [.. itsTrades.Select(trade => (trade is { } held ? JudgeAccounts.ResultOf(held) : null, noOverSamePlan))];

            return new JudgeVersionView(number, one.First, one.Last, cards, JudgeScore.Of(results), walk.Values) { Variant = fixedFive ? JudgeNight.FixedFive : null };
        }

        var numbered = versions.Select((one, at) => (One: one, Number: at + 1)).ToArray();
        var apart = numbered.Where(pair => pair.One.Key != version).Select(pair => Apart(pair.One, pair.Number)).ToArray();
        int? recordNumber = earlier ? numbered.Single(pair => pair.One.Key == version).Number : null;

        // The earlier version's picks, made across the three indices before it picked within each: no index's, bought and
        // walked in its own account and never in an index's, each card naming the index its stock belongs to, and drawn
        // while a trade of it is not yet bought or not yet sold.
        var acrossPicks = picks.Where(pick => !pick.Replayed && pick.RunIndex is null).OrderBy(pick => pick.Session).ThenBy(pick => pick.Rank).ToArray();
        JudgeVersionView? acrossView = null;
        var acrossOpen = false;

        if (acrossPicks.Length > 0)
        {
            var acrossTrades = acrossPicks.Select(pick => TradeOf(pick.Ticker, pick.Session, pick.Hold, pick.Shape, pick.Rank, pick.TypicalMove)).ToArray();
            var acrossFirst = acrossPicks[0].Session;
            var acrossWalk = PaperAccount.Walk(settings, [.. acrossTrades.OfType<PaperTrade>()], [.. allSessions.Where(session => session >= acrossFirst)], Close);
            var cards = acrossPicks.Select((pick, at) => Card(pick, acrossPicks.Count(other => other.Session == pick.Session)) with { Night = pick.Session, Held = HeldFor(pick, acrossTrades[at], acrossWalk), Index = pick.Index }).ToArray();
            double? noneOver = null;
            (double? Result, double? OverSamePlan)[] acrossResults = [.. acrossTrades.Select(trade => (trade is { } held ? JudgeAccounts.ResultOf(held) : null, noneOver))];

            acrossView = new JudgeVersionView(0, acrossFirst, acrossPicks[^1].Session, cards, JudgeScore.Of(acrossResults), acrossWalk.Values);
            acrossOpen = cards.Any(card => card.Held is null or { Skipped: null, Ended: null });
        }

        // From 24.24 the record's version's variant "a fixed 5", every pick its second round ranked, in an account of its own.
        var fixedFive = numbered.Where(pair => pair.One.Key == version && version.Contains(KeyMark, StringComparison.Ordinal)).Select(pair => Apart(pair.One, pair.Number, fixedFive: true)).FirstOrDefault();

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

        var tonight = newest is null ? [] : every.Where(pick => pick.Session == newest.Session && (newest.Index is null || pick.RunId == newest.RunId)).OrderBy(pick => pick.Rank).ToArray();

        // From 24.15 Home drawn for a past night an earlier version picked on names that version's account.
        var picksFrom = tonight.Length == 0 ? null : numbered.Where(pair => pair.One.Key == KeyOf(tonight[0]) && pair.One.Key != version).Select(pair => apart.Single(one => one.Number == pair.Number)).FirstOrDefault();

        // From 24.24 a card of the record's its account had no cash for says it was not bought, and from the operator's answers
        // of 2026-11-24 one its range did not buy says which side of it the stock opened on.
        JudgeCardView Tonight(JudgePickRow pick)
        {
            var card = Card(pick, tonight.Length);

            if (KeyOf(pick) != version)
            {
                return card;
            }

            var trade = judged.FirstOrDefault(one => one.Pick == pick).Trade;

            return HeldFor(pick, trade, judgeWalk) is { Skipped: PaperAccount.UnderTheCash or JudgeAccounts.OpenedAbove or JudgeAccounts.OpenedBelow } skipped
                ? card with { Held = skipped }
                : card;
        }

        // The index's tile: its account's return since its first session, none before it has bought a trade, its trades still
        // open and closed, and its picks tonight.
        var score = JudgeScore.Of(record);
        var tile = new JudgeTile(
            within,
            judgeWalk.Trades.Any(one => one.Skipped is null) && judgeWalk.Values.Count > 1 && judgeWalk.Values[0].Value > 0m ? Statistic.FromRatio((judgeWalk.Values[^1].Value / judgeWalk.Values[0].Value) - 1m) : null,
            judgeWalk.Trades.Count(one => one.Skipped is null && one.Trade.Ended is null),
            score.Closed,
            tonight.Length)
        {
            Started = started,
            StartsOn = startsOn,
        };

        return new JudgeView(
            newest?.Session,
            newest?.Outcome,
            newest?.Why,
            [.. tonight.Select(Tonight)],
            score,
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
            Index = within,
            Marked = newest is { Index: not null } ran && ran.Outcome is "picked" or "none" ? ran.Kept : null,
            FixedFive = fixedFive,
            Tiles = [tile],
            Across = acrossView,
            AcrossOpen = acrossOpen,
            AcrossNight = acrossRan is { } acrossOn && (newest is null || acrossOn > newest.Session)
                ? (acrossOn, acrossPicks.Count(pick => pick.Session == acrossOn))
                : null,
            Shown = shown,
            Nights = ranNights,
            Past = shown is { } chosenOn && ranNights.Length > 0 && chosenOn < ranNights[0],
            AcrossTonight = acrossView is null || shown is not { } on ? [] : [.. acrossView.Picks.Where(card => card.Night == on)],
            AcrossStillOpen = AcrossOpenOn(acrossView, shown, ranNights.Length > 0 && shown < ranNights[0]),
        };
    }

    // From the operator's correction of 2026-11-23 the earlier version's trades still open on a night after the one it picked
    // on, counted from its newest night before it: each not skipped and not yet sold, by that night on a past night and by
    // the newest prices on the newest. None where it holds none then, or picked on no night before it.
    // see: The AI stock picker's page draws one night at a time, and its count line says what decided the number
    static (DateOnly Night, int Open)? AcrossOpenOn(JudgeVersionView? across, DateOnly? shown, bool past)
    {
        if (across is null || shown is not { } on || across.Picks.Where(card => card.Night < on).Select(card => card.Night).Max() is not { } picked)
        {
            return null;
        }

        bool OpenOn(JudgeCardView card) => card.Held switch
        {
            null => true,
            { Skipped: not null } => false,
            { Ended: { } sold } => past && sold > on,
            _ => true,
        };

        var open = across.Picks.Count(card => card.Night <= picked && OpenOn(card));

        return open == 0 ? null : (picked, open);
    }

    // Why a pick its account's walk did not take is not bought there.
    public const string NotTaken = "its account's walk did not take it";

    // Why a pick its range did not buy is not bought, by the side of the range it opened on.
    static string NotBoughtWords(string side) => side == Core.Loop.BuyRanges.Above ? JudgeAccounts.OpenedAbove : JudgeAccounts.OpenedBelow;

    // What sets an index's own version's key apart from one picked across the three indices.
    const string KeyMark = "|";

    // A version's key: its prompt version, from 24.24 with the index it picked within, a pick under no pin read as one
    // version of its own.
    static string KeyOf(JudgePickRow pick) => KeyOf(pick.Version, pick.RunIndex);

    static string KeyOf(string? version, string? index) => (version ?? string.Empty) + (index is null ? string.Empty : KeyMark + index);

    // Each judge version's first and last night, read off its live picks and the live runs under its pin, in the order the
    // versions first ran.
    static (string Key, DateOnly First, DateOnly Last)[] Versions(IReadOnlyList<JudgePickRow> every, IReadOnlyList<JudgeRunRow> runs) =>
    [
        .. every.Select(pick => (Key: KeyOf(pick), pick.Session))
            .Concat(runs.Where(run => !run.Replayed && run.Version is not null).Select(run => (Key: KeyOf(run.Version, run.Index), run.Session)))
            .GroupBy(one => one.Key, StringComparer.Ordinal)
            .Select(group => (group.Key, First: group.Min(one => one.Session), Last: group.Max(one => one.Session)))
            .OrderBy(one => one.First)
            .ThenBy(one => one.Key, StringComparer.Ordinal),
    ];

    // One pick's card: its rank of its night's picks, what it saw and the plan code set from its last close.
    static JudgeCardView Card(JudgePickRow pick, int of) =>
        new JudgeCardView(pick.Rank, of, pick.Ticker, pick.Company, pick.Sector, pick.Size, pick.Reason, pick.LastClose, pick.Stop, pick.Target, pick.GiveUp, pick.Hold, pick.Shape) with
        {
            Range = JudgeAccounts.RangeOf(pick.Session, pick.LastClose, pick.TypicalMove),
            Named = pick.Named is { } named ? [.. named.Select(key => Fact(key, pick.Sheet, pick.Tenths))] : null,
            Rest = [.. JudgeFacts.In(JudgeRound.Sheet).Where(field => pick.Named?.Contains(field.Key, StringComparer.Ordinal) != true).Select(field => Fact(field.Key, pick.Sheet, pick.Tenths))],
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

    // One fact of a pick's sheet: its words and its value drawn in the page's own words from the text the judge was shown, a
    // field it held none of not on file, with its key and the tenth it stood in among its index's members that night, none
    // where it was not placed.
    static JudgeFactView Fact(string key, IReadOnlyDictionary<string, string> sheet, IReadOnlyDictionary<string, int> tenths)
    {
        var field = JudgeFacts.All.FirstOrDefault(one => one.Key == key);
        var shown = sheet.TryGetValue(key, out var text) ? text : JudgeFacts.NotOnFile;
        var value = JudgeFactView.ValueOf(key, shown);

        return new JudgeFactView(JudgeFactView.LabelOf(key, field?.Words ?? key), value, JudgeFactView.Compared(key, value))
        {
            Key = key,
            Tenth = shown != JudgeFacts.NotOnFile && tenths.TryGetValue(key, out var tenth) ? tenth : null,
        };
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
