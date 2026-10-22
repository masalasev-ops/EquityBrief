using System.Globalization;
using EquityBrief.Core.Families;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Time;
using EquityBrief.Worker.Dips;
using EquityBrief.Worker.Families;
using EquityBrief.Worker.Indices;
using Xunit;

namespace EquityBrief.Tests.Checks;

// The short-term dip, worked by hand: its 2-session relative strength index, each gate on both sides of its threshold and
// exactly at it, the report window's edges, its exits at the next open and its time limit's close, its order, and its
// night over a constructed S&P 400 giving way to every other family's list and open trades.
// see: The dip lists last among the swing families on every index and gives way to every other family's list and open trades
public partial class FixtureExpectations
{
    [Fact]
    public void TheDipsTwoSessionRsiIsWildersWorkedByHand()
    {
        // Changes of +1, -0.5, -0.5 and -1: the seed's mean gain 0.5 and loss 0.25 read 66.67; smoothed by two, a gain of
        // 0.25 and a loss of 0.375 read 40; then 0.125 and 0.6875 read 15.38.
        var rsi = DipRule.Rsi([10, 11, 10.5, 10, 9]);

        Assert.Null(rsi[0]);
        Assert.Null(rsi[1]);
        Assert.Equal(100 - (100 / 3.0), rsi[2]!.Value, 9);
        Assert.Equal(40.0, rsi[3]!.Value, 9);
        Assert.Equal(100 - (100 / (1 + (0.125 / 0.6875))), rsi[4]!.Value, 9);

        // A run no loss has touched reads 100, and a run too short for the window reads none.
        Assert.Equal(100.0, DipRule.Rsi([1, 2, 3])[2]);
        Assert.All(DipRule.Rsi([1, 2]), value => Assert.Null(value));
    }

    // 199 closes of 100 and the night's own: its 200-session average is (19,900 + close) / 200.
    static double[] FlatThen(double close) => [.. Enumerable.Repeat(100.0, 199), close];

    static DipMember Member(IReadOnlyList<double> closes, double? move = 1.5, bool floors = true, IReadOnlyList<int>? reports = null) =>
        new("DX", closes, move, floors, "under the floors", reports ?? [], null, DipRule.NoMarket);

    static bool Passes(DipAnswer answer, string gate) => answer.Gates.Single(one => one.Name == gate).Passed;

    [Fact]
    public void EachOfTheDipsGatesIsWorkedByHandOnBothSidesOfItsThresholdAndExactlyAtIt()
    {
        var any = DipRule.Provisional with { RsiUnder = 101 };

        // The trend: the close above its 200-session average. At 100 the average is 100 and the close is not above it.
        Assert.False(Passes(DipRule.Read(Member(FlatThen(100)), any), DipRule.TrendGate));
        Assert.True(Passes(DipRule.Read(Member(FlatThen(100.5)), any), DipRule.TrendGate));
        Assert.False(Passes(DipRule.Read(Member(FlatThen(99.5)), any), DipRule.TrendGate));

        // The history: 199 closes hold no 200-session average, 200 do.
        var short199 = DipRule.Read(Member([.. Enumerable.Repeat(100.0, 198), 101]), any);
        Assert.False(Passes(short199, DipRule.HistoryGate));
        Assert.Equal(DipRule.ShortHistory, short199.Reason);
        Assert.True(Passes(DipRule.Read(Member(FlatThen(101)), any), DipRule.HistoryGate));

        // The drop: 200 closes of 10, then 11, 10.5, 10 and 9. Flat changes hold no gain or loss, so the night reads a gain
        // of 0.0625 against a loss of 0.6875, an RSI of 8.33; the floor at that figure exactly is not passed.
        double[] drop = [.. Enumerable.Repeat(10.0, 200), 11, 10.5, 10, 9];
        var rsi = DipRule.Rsi(drop)[^1]!.Value;

        Assert.Equal(100 - (100 / (1 + (0.0625 / 0.6875))), rsi, 9);
        Assert.True(Passes(DipRule.Read(Member(drop), DipRule.Provisional), DipRule.DropGate));
        Assert.False(Passes(DipRule.Read(Member(drop), DipRule.Provisional with { RsiUnder = rsi }), DipRule.DropGate));
        Assert.True(Passes(DipRule.Read(Member(drop), DipRule.Provisional with { RsiUnder = rsi + 0.001 }), DipRule.DropGate));
        Assert.False(Passes(DipRule.Read(Member(drop), DipRule.Provisional with { RsiUnder = rsi - 0.001 }), DipRule.DropGate));

        // The typical move: none and nought place no stop; any above nought does.
        Assert.False(Passes(DipRule.Read(Member(FlatThen(101), move: null), any), DipRule.MoveGate));
        Assert.False(Passes(DipRule.Read(Member(FlatThen(101), move: 0), any), DipRule.MoveGate));
        Assert.True(Passes(DipRule.Read(Member(FlatThen(101), move: 0.01), any), DipRule.MoveGate));

        // The floors as the index families read them, and the market setting off passing every member.
        var under = DipRule.Read(Member(FlatThen(101), floors: false), any);
        Assert.Equal((false, DipRule.UnderTheFloors), (Passes(under, DipRule.FloorsGate), under.Reason));
        Assert.True(Passes(under, DipRule.MarketGate));

        // A member passing every gate is bought at its close with its stop four typical moves under it and its time limit.
        var passed = DipRule.Read(Member(FlatThen(101), move: 1.5), any);
        Assert.Equal((true, 101m, 95m, 10), (passed.Passed, passed.Entry, passed.Stop, passed.TimeLimit));
    }

    [Fact]
    public void TheDipRefusesAReportInTheDropOrDueOnTheTimeLimitsLastSessionAndNotOneDueTheSessionAfter()
    {
        var setting = DipRule.Provisional with { RsiUnder = 101 };

        bool Refused(int sessions) => !Passes(DipRule.Read(Member(FlatThen(101), reports: [sessions]), setting), DipRule.ReportGate);

        // The three sessions to the listing are the night and the two before it; the time limit's last is the 10th after.
        Assert.False(Refused(-3));
        Assert.True(Refused(-2));
        Assert.True(Refused(0));
        Assert.True(Refused(1));
        Assert.True(Refused(10));
        Assert.False(Refused(11));
        Assert.Equal(DipRule.ReportNear, DipRule.Read(Member(FlatThen(101), reports: [0]), setting).Reason);

        // On the night, a report dated tonight counts nought sessions, one dated the session before the time limit ends
        // counts ahead on the exchange's own calendar, and one on a day the calendar holds back is read at the first
        // session on or after it.
        var night = new DateOnly(2026, 10, 2);
        DateOnly[] calendar = [new(2026, 9, 28), new(2026, 9, 29), new(2026, 9, 30), new(2026, 10, 1), night];
        var (reports, next) = DipNight.ReportsOf([new(2026, 9, 27), night, new(2026, 10, 16), new(2026, 10, 19)], calendar, calendar.Length - 1, night);

        // 2026-09-27 is a Sunday, read at the Monday after, four sessions back; 2026-10-16 is the tenth session after
        // Friday 2026-10-02 and 2026-10-19 the eleventh.
        Assert.Equal([-4, 0, 10, 11], reports);
        Assert.Equal(new DateOnly(2026, 10, 16), next);
    }

    static DipSession Day(int day, double open, double close, double? average) => new(new DateOnly(2026, 10, day), open, close, average);

    [Fact]
    public void EachOfTheDipsExitsIsWorkedByHand()
    {
        // Bought at 99 on the next session's open, its stop 96 from a listing close of 100.
        // A close of 101 above its 5-session average of 99.6 on the second session sells at the third's open, 101.5.
        var exit = DipRule.Walk(100, 96, 10, [Day(5, 99, 98, 99), Day(6, 98.5, 101, 99.6), Day(7, 101.5, 102, 100)]);
        Assert.Equal((new DateOnly(2026, 10, 5), 99.0, new DateOnly(2026, 10, 7), 101.5, DipEnd.Exit, 2), (exit.BoughtOn, exit.Open, exit.EndedOn, exit.Sale, exit.End, exit.Sessions));
        Assert.Equal((101.5 - 100) / 4, exit.ResultAtClose!.Value, 9);

        // Bought at the open, the core's one pricing function restates the result over the risk the close set: (101.5 - 99) / 4.
        Assert.Equal((101.5 - 99) / 4, TradeFillsAtOpen(exit.ResultAtClose.Value, 100, 4, 99), 9);

        // A close of 95.9 under the stop on the first session sells at the second's open.
        var stop = DipRule.Walk(100, 96, 10, [Day(5, 99, 95.9, 99), Day(6, 95, 97, 98)]);
        Assert.Equal((new DateOnly(2026, 10, 6), 95.0, DipEnd.Stop, 1), (stop.EndedOn, stop.Sale, stop.End, stop.Sessions));
        Assert.Equal((95 - 100) / 4.0, stop.ResultAtClose!.Value, 9);

        // With neither on any of its three sessions the time limit sells at the third's own close.
        var limit = DipRule.Walk(100, 96, 3, [Day(5, 99, 98, 99), Day(6, 98, 97.5, 98.5), Day(7, 97.4, 97.2, 98), Day(8, 97, 99, 98)]);
        Assert.Equal((new DateOnly(2026, 10, 7), 97.2, DipEnd.Limit, 3), (limit.EndedOn, limit.Sale, limit.End, limit.Sessions));

        // A trigger on the last stored session waits for its next session's open, and so does a path with no session yet.
        var waiting = DipRule.Walk(100, 96, 10, [Day(5, 99, 98, 99), Day(6, 98.5, 101, 99.6)]);
        Assert.Equal((DipEnd.Open, (DateOnly?)null, 2), (waiting.End, waiting.EndedOn, waiting.Sessions));
        Assert.Equal((DipEnd.Open, (DateOnly?)null), (DipRule.Walk(100, 96, 10, []).End, DipRule.Walk(100, 96, 10, []).BoughtOn));
    }

    static double TradeFillsAtOpen(double atClose, double close, double risk, double open) =>
        EquityBrief.Core.Ledger.TradeFills.AtOpen(atClose, close, risk, open)!.Value;

    [Fact]
    public void TheDipsFiveSessionAverageExitReadsTheCloseToItAndTheFourBefore()
    {
        // Closes 10, 11, 12, 13, 14, 15 and 9: after a listing on the third, the fourth session's average is (10 + 11 + 12 +
        // 13) / 4 short of five and none, the fifth's (10 + 11 + 12 + 13 + 14) / 5 = 12, the sixth's 13 and the seventh's 12.6.
        var bars = new (DateOnly, double, double)[] { (new(2026, 10, 1), 10, 10), (new(2026, 10, 2), 11, 11), (new(2026, 10, 3), 12, 12), (new(2026, 10, 4), 13, 13), (new(2026, 10, 5), 14, 14), (new(2026, 10, 6), 15, 15), (new(2026, 10, 7), 9, 9) };
        var sessions = DipRule.Sessions(bars, 2, 5);

        Assert.Equal([null, 12.0, 13.0, 12.6], sessions.Select(session => session.Average));
    }

    [Fact]
    public void TheDipListsTheLowestTwoSessionRsiFirstAndThenByTicker()
    {
        static DipAnswer Passing(string ticker, double rsi) => new(ticker, [], 10m, 9m, 10, rsi, null);

        Assert.Equal(["C", "A", "B"], DipRule.Ranked([Passing("B", 5), Passing("C", 3), Passing("A", 5), new DipAnswer("D", [], null, null, null, 1, DipRule.NoDrop)]).Select(answer => answer.Ticker));
    }

    const string DipNightSession = "2026-10-02";

    // A year of bars ending on the night, a calendar day a session: closes rising from 80 by 0.16 a session to the session
    // before the drop, then falling by the given amount on each of the last two, each bar's range 2 about its close, a
    // million shares traded a session, which clears the S&P 400's floors at about $100.
    static IReadOnlyList<FamilyBar> DipYear(double drop)
    {
        var night = DateOnly.ParseExact(DipNightSession, "yyyy-MM-dd", CultureInfo.InvariantCulture);

        return
        [
            .. Enumerable.Range(0, 252).Select(at =>
            {
                var close = at < 250 ? 80 + (0.16 * at) : 80 + (0.16 * 249) - (drop * (at - 249));
                var price = Statistic.ToPrice(close);

                return new FamilyBar(night.AddDays(at - 251), price + 1m, price - 1m, price, 1_000_000);
            }),
        ];
    }

    // The S&P 400 on the constructed night. DA to DF fall by 4, 3.5, 3, 2.5, 2 and 1.5 a session over the last two, so
    // their 2-session RSIs order them DA first; DH falls by 5 and holds a trade on an S&P 500 card from the week before; DL
    // falls by 4.5 and the S&P 400's breakout listed it tonight; DR falls by 3 with a report on the night; DN does not fall.
    static TemporaryStore DipStore()
    {
        var store = new TemporaryStore().Migrated();
        var members = new (string Ticker, double Drop)[] { ("DA", 4), ("DB", 3.5), ("DC", 3), ("DD", 2.5), ("DE", 2), ("DF", 1.5), ("DH", 5), ("DL", 4.5), ("DR", 3), ("DN", -0.16) };

        foreach (var (ticker, drop) in members)
        {
            StoreYear(store, ticker, DipYear(drop));
            store.Execute($"INSERT INTO membership (index_code, ticker, joined, \"left\", observed_at) VALUES ('MID', '{ticker}', '2024-01-02', NULL, '2024-01-02T00:00:00Z');");
        }

        store.Execute($"INSERT INTO calendar (ticker, event_date, kind, timing, detail, observed_at) VALUES ('DR', '{DipNightSession}', 'earnings', 'after', '', '2026-09-01T00:00:00Z');");
        store.Execute("INSERT INTO family_night (session_date, families) VALUES ('2026-09-28', '[\"pullback\",\"breakout\",\"drift\"]');");
        store.Execute("INSERT INTO family_pick (session_date, ticker, family, state, place, also, held_family, held_night) VALUES ('2026-09-28', 'DH', 'breakout', 'listed', 1, '[]', NULL, NULL);");
        store.Execute($"INSERT INTO index_family_pick (index_code, session_date, ticker, family, state, place, also) VALUES ('MID', '{DipNightSession}', 'DL', 'breakout', 'listed', 1, '[]');");

        return store;
    }

    [Fact]
    public async Task TheDipsNightStoresEveryMembersAnswerAndGivesWayToEveryOtherFamilysListAndOpenTrades()
    {
        using var store = DipStore();
        var clock = FixedClock.At(new DateTimeOffset(2026, 10, 2, 23, 40, 0, TimeSpan.Zero), SessionZones.UnitedStates);
        var outcome = await new DipNight(clock, store.DatabaseFile).RunAsync("night-dip");
        var mid = outcome.Indices.Single(index => index.Index == "MID");

        // Ten members read, eight passing: every one but DR, whose report falls on the night, and DN, which did not fall.
        Assert.Equal((10, 8), (mid.Members, mid.Passed));
        Assert.Equal(
            [
                "DA|1|3|null", "DB|1|4|null", "DC|1|5|null", "DD|1|6|null", "DE|1|7|null", "DF|1|8|null", "DH|1|1|null", "DL|1|2|null",
                $"DN|0|null|{DipRule.NoDrop}", $"DR|0|null|{DipRule.ReportNear}",
            ],
            FamilyRows(store, "SELECT ticker, passed, place, reason FROM dip_result WHERE index_code = 'MID' ORDER BY ticker;"));

        // The list gives way: DH held by its S&P 500 trade, DL listed tonight under the breakout, then the first five of the
        // rest listed and DF past them.
        Assert.Equal(
            [
                $"DA|{FamilyList.Listed}|1|null|null", $"DB|{FamilyList.Listed}|2|null|null", $"DC|{FamilyList.Listed}|3|null|null",
                $"DD|{FamilyList.Listed}|4|null|null", $"DE|{FamilyList.Listed}|5|null|null", $"DF|{FamilyList.PastFive}|null|null|null",
                $"DH|{FamilyList.OpenTrade}|null|null|GSPC", $"DL|{FamilyList.UnderAnother}|null|breakout|null",
            ],
            FamilyRows(store, "SELECT ticker, state, place, listed_under, held_index FROM dip_pick WHERE index_code = 'MID' ORDER BY ticker;"));

        // The five listed kept as trades under the provisional rule, bought on the plan at the night's close, held at most
        // ten sessions, each stop four typical moves under the close.
        Assert.Equal(
            ["DA|1|10|4", "DB|2|10|4", "DC|3|10|4", "DD|4|10|4", "DE|5|10|4"],
            FamilyRows(store, $"SELECT ticker, place, time_limit, risk_moves FROM dip_trade WHERE rule = '{DipNight.ProvisionalRule}' AND index_code = 'MID' ORDER BY ticker;"));
        Assert.Equal((5, 1, 1), (mid.Listed, mid.HeldByATrade, mid.UnderAnother));
        Assert.All(
            FamilyRows(store, "SELECT entry, stop, end_reason FROM dip_trade WHERE index_code = 'MID';"),
            row => Assert.True(row.Split('|') is [var entry, var stop, "open"] && decimal.Parse(stop, CultureInfo.InvariantCulture) < decimal.Parse(entry, CultureInfo.InvariantCulture), row));

        // No other family's row is written: the families' own tables hold what the store was given and nothing more.
        Assert.Equal(["1"], FamilyRows(store, "SELECT COUNT(*) FROM index_family_pick;"));
        Assert.Equal(["0"], FamilyRows(store, "SELECT COUNT(*) FROM index_family_trade;"));
    }
}
