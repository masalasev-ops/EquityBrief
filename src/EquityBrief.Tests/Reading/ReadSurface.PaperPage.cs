using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Api.Reading;
using EquityBrief.Core.Cards;
using EquityBrief.Core.Families;
using EquityBrief.Core.Ledger;
using EquityBrief.Core.Loop;
using EquityBrief.Core.Prices;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.App;
using EquityBrief.Web.Marks;
using EquityBrief.Worker.Candidates;

namespace EquityBrief.Tests.Reading;

// read-surface: paper trading drawn a part a card from the top, every figure a part draws beside the walk worked by hand
// through the core's one function at its edges, and each part read back off the rendered page over constructed stores,
// the choice each tab, chip and select makes kept in the link with every other.
// see: Paper trading's accounts and a heavyweight's standing tonight are worked by one function in the core when a page is read
// see: A rule's scorecard draws its test years and its record since it went live as two halves, and never adds them together
public partial class ReadSurface
{
    // The parts the page draws in the place of the table of every account, and that table's row.
    internal static readonly string[] PaperPageRows =
    [
        .. new[] { "The tiles", "What the system bought", "An open trade", "A closed trade", "What the variants did differently", "Rule scorecard", "Your own trades" }
            .Select(row => CheckReach.Key("15.21 Paper trading", row)),
    ];

    internal static readonly string[] RetiredPaperRows = [CheckReach.Key("15.21 Paper trading", "Every account")];

    static readonly DateOnly Listed = new(2026, 9, 28);

    static readonly DateOnly Filled = new(2026, 9, 29);

    static readonly DateOnly OnTheNight = new(2026, 10, 2);

    static readonly DateOnly[] Week = [new(2026, 9, 28), new(2026, 9, 29), new(2026, 9, 30), new(2026, 10, 1), new(2026, 10, 2)];

    // A trade listed on the week's Monday and bought at the next open at 100, its plan's close 100, its stop 90 and its
    // target 120, given twenty sessions; the store's open on its fill session 100 too, so its scale is the store's.
    static readonly PaperTrade Planned = new("OT", Filled, 100m, 90m, null, null, 0m, 1, Planned: 100m, MarkedFrom: 100m, Listed: Listed, Target: 120m, Cap: 20);

    static PaperPosition Tonight(decimal close, double? move = 4, PaperTrade? trade = null) =>
        PaperAccount.Position(trade ?? Planned, OnTheNight, Week, (_, session) => session == OnTheNight ? close : null, move);

    [Fact]
    public void AnOpenTradeStandsNearItsTargetOrItsStopWithinOneTypicalMoveAndOnTrackBetween()
    {
        // At 117, three under its target of 120 on a move of 4: three quarters of a move, near its target; its result 17 on
        // the 10 its plan put at risk, 1.7 risks and 17 per cent, held four sessions after its Monday listing.
        Assert.Equal(new PaperPosition(117m, 117m, 90m, 1.7, 0.17, 4, PaperAccount.NearTarget), Tonight(117m));

        // One move exactly is near and a hundredth past it is not, at either end.
        Assert.Equal(PaperAccount.NearTarget, Tonight(116m).Standing);
        Assert.Equal(PaperAccount.OnTrack, Tonight(115.99m).Standing);
        Assert.Equal(PaperAccount.NearStop, Tonight(94m).Standing);
        Assert.Equal(PaperAccount.OnTrack, Tonight(94.01m).Standing);
        Assert.Equal(PaperAccount.OnTrack, Tonight(105m).Standing);

        // Filled at 101, a dollar over the close its plan was set at: its result is read in the 10 that close put at risk
        // over its stop, 1.6 risks at 117, and its move from its own buy.
        var over = PaperAccount.Position(Planned with { Buy = 101m, MarkedFrom = 101m }, OnTheNight, Week, (_, session) => session == OnTheNight ? 117m : null, 4);

        Assert.Equal((1.6, Statistic.FromRatio((117m / 101m) - 1m)), (over.Risks!.Value, over.Fraction!.Value));

        // A plan narrower than two moves is near both: the nearer is read, and the stop on a tie.
        var narrow = Planned with { Stop = 99m, Target = 101m };

        Assert.Equal(PaperAccount.NearStop, Tonight(100m, trade: narrow).Standing);
        Assert.Equal(PaperAccount.NearTarget, Tonight(100.5m, trade: narrow).Standing);
        Assert.Equal(PaperAccount.NearStop, Tonight(99.5m, trade: narrow).Standing);

        // A fill that opened past its stop or its target says so from its fill, wherever the close stands.
        Assert.Equal(PaperAccount.OpenedUnderStop, Tonight(117m, trade: Planned with { Past = FillPast.Stop }).Standing);
        Assert.Equal(PaperAccount.OpenedOverTarget, Tonight(105m, trade: Planned with { Past = FillPast.Target }).Standing);

        // No typical move, one of nothing or no close tonight reads no standing; with no close no result either.
        Assert.Equal(HeavyweightStandings.NotRead, Tonight(117m, move: null).Standing);
        Assert.Equal(HeavyweightStandings.NotRead, Tonight(117m, move: 0).Standing);

        var unread = PaperAccount.Position(Planned, OnTheNight, Week, (_, _) => null, 4);

        Assert.Equal((null, null, null, HeavyweightStandings.NotRead), (unread.Now, unread.Risks, unread.Fraction, unread.Standing));

        // Through a two for one split the store holds the trade's fill session at 50 and tonight's close at 58.5, and its
        // typical move at 2: by the ratio of its closes it stands at 117 on its own scale, its move 4 there, so it reads as
        // before; the close drawn is the store's.
        var split = PaperAccount.Position(Planned with { MarkedFrom = 50m }, OnTheNight, Week, (_, session) => session == OnTheNight ? 58.5m : null, 2);

        Assert.Equal(new PaperPosition(58.5m, 117m, 90m, 1.7, 0.17, 4, PaperAccount.NearTarget), split);

        // A trailing plan's stop tonight is its plan's raised to the highest close since its listing less the distance it
        // trails, never lowered: closes of 100, 106 and 113 after its Monday raise 90 to 103. At 108 that is a move and a
        // quarter under the close, on track; at 106 three quarters of a move, near its stop. It has no target.
        var trailing = Planned with { Target = null, Trail = 10m };
        var closes = new Dictionary<DateOnly, decimal> { [Week[1]] = 100m, [Week[2]] = 106m, [Week[3]] = 113m };

        PaperPosition Trailed(decimal tonight) =>
            PaperAccount.Position(trailing, OnTheNight, Week, (_, session) => session == OnTheNight ? tonight : closes.TryGetValue(session, out var close) ? close : null, 4);

        Assert.Equal((103m, PaperAccount.OnTrack), (Trailed(108m).Stop, Trailed(108m).Standing));
        Assert.Equal((103m, PaperAccount.NearStop), (Trailed(106m).Stop, Trailed(106m).Standing));

        // A holding reads the words its heavyweights rule gave it, its result in its fraction alone, and none where none
        // was handed in.
        var holding = Planned with { Stop = null, Target = null, BookHoldings = 22 };
        var leading = PaperAccount.Position(holding, OnTheNight, Week, (_, session) => session == OnTheNight ? 105m : null, 4, HeavyweightStandings.StillLeading);

        Assert.Equal((HeavyweightStandings.StillLeading, (double?)null, 0.05), (leading.Standing, leading.Risks, leading.Fraction!.Value));
        Assert.Equal(HeavyweightStandings.NotRead, PaperAccount.Position(holding, OnTheNight, Week, (_, session) => session == OnTheNight ? 105m : null, 4).Standing);
    }

    [Fact]
    public void AClosedTradeEndsInWordsWithItsResultItsSessionsAndItsEdge()
    {
        var end = new DateOnly(2026, 10, 1);
        var fixedPlan = Planned with { Ended = end };

        // Sold at its target of 120 two risks up, three sessions after its listing, its edge as its fill stored it.
        Assert.Equal(new PaperEnded(120m, 2.0, 0.2, PaperAccount.ReachedTarget, 3, 1.25), PaperAccount.Ended(fixedPlan with { Sale = 120m, Edge = 1.25 }, 2.0));

        // Under its stop of 90, under its stop; at its cap of three sessions between the two, its time limit, and on its
        // stop at its cap its time limit too, the walks selling only under a stop.
        Assert.Equal(PaperAccount.UnderStop, PaperAccount.Ended(fixedPlan with { Sale = 89m }, -1.1).Ending);
        Assert.Equal(PaperAccount.TimeLimit, PaperAccount.Ended(fixedPlan with { Sale = 104m, Cap = 3 }, 0.4).Ending);
        Assert.Equal(PaperAccount.TimeLimit, PaperAccount.Ended(fixedPlan with { Sale = 90m, Cap = 3 }, -1.0).Ending);

        // A trailing plan sold before its cap closed under its trailing stop, at a gain or a loss, and at its cap its time
        // limit.
        var trailing = fixedPlan with { Target = null, Trail = 10m };

        Assert.Equal(PaperAccount.UnderTrail, PaperAccount.Ended(trailing with { Sale = 104m }, 0.4).Ending);
        Assert.Equal(PaperAccount.UnderTrail, PaperAccount.Ended(trailing with { Sale = 96m }, -0.4).Ending);
        Assert.Equal(PaperAccount.TimeLimit, PaperAccount.Ended(trailing with { Sale = 104m, Cap = 3 }, 0.4).Ending);

        // A page list's trade reads the words its stored outcome reads as, wherever it was sold.
        Assert.Equal(PaperAccount.TimeLimit, PaperAccount.Ended(fixedPlan with { Sale = 120m, Outcome = PaperAccount.TimeLimit }, 2.0).Ending);

        // A holding says why its book sold it, its result its fraction alone.
        var sold = PaperAccount.Ended(fixedPlan with { Stop = null, Target = null, BookHoldings = 22, Sale = 105m, Outcome = "no longer the leader" }, 0.05);

        Assert.Equal(("sold: no longer the leader", (double?)null, 0.05), (sold.Ending, sold.Risks, sold.Fraction!.Value));

        // A trade whose result was not read was sold at no price the page can name.
        Assert.Equal(PaperAccount.NoResult, PaperAccount.Ended(fixedPlan, null).Ending);
    }

    [Fact]
    public void AClosedTradesEndingIsItsOwnExitReplayedOverItsClosesToTheSessionItEnded()
    {
        Func<string, DateOnly, decimal?> Closes(params decimal?[] after) =>
            (_, session) => session == Filled.AddDays(-1) ? 100m : Array.IndexOf(Week, session) is var at and > 0 && at <= after.Length ? after[at - 1] : null;

        // Trailing 10 under the highest close from a stop of 90, given three sessions: closes of 108 and 112 raise its
        // stop to 102, so a close of 101 on its third and last session falls through it, and the walk sells it there under
        // its trailing stop; the sale alone, on its cap's session, reads its time limit. A close of 103 there holds, and
        // the cap's close ends it at its time limit.
        var trailing = Planned with { Target = null, Trail = 10m, Cap = 3, Ended = Week[3], Sale = 101m };

        Assert.Equal(PaperAccount.TimeLimit, PaperAccount.Ended(trailing, 0.1).Ending);
        Assert.Equal(PaperAccount.UnderTrail, PaperAccount.Ended(trailing, 0.1, Week, Closes(108m, 112m, 101m)).Ending);
        Assert.Equal(PaperAccount.TimeLimit, PaperAccount.Ended(trailing with { Sale = 103m }, 0.3, Week, Closes(108m, 112m, 103m)).Ending);

        // A fixed plan whose last close stands on its stop of 90 is sold at its cap, the walks selling only under a stop.
        Assert.Equal(PaperAccount.TimeLimit, PaperAccount.Ended(Planned with { Cap = 3, Ended = Week[3], Sale = 90m }, -1.0, Week, Closes(95m, 92m, 90m)).Ending);

        // Under the menu's time exit ten sessions in unless it is above its buy: ten closes of 98, under its buy of 100 and
        // clear of its stop and its target, sell it on the tenth at that exit, where its sale alone reads under its stop.
        var time = ExitMenu.Swing.Single(choice => choice is { Kind: ExitKind.Time, N: 10, X: 0 });
        DateOnly[] fortnight = [.. Week, new(2026, 10, 5), new(2026, 10, 6), new(2026, 10, 7), new(2026, 10, 8), new(2026, 10, 9), new(2026, 10, 12)];
        var timed = Planned with { Exit = time.Number, Ended = fortnight[^1], Sale = 98m };

        Assert.Equal(PaperAccount.UnderStop, PaperAccount.Ended(timed, -0.2).Ending);
        Assert.Equal(PaperAccount.TimeExit, PaperAccount.Ended(timed, -0.2, fortnight, (_, session) => session == Filled.AddDays(-1) || session == Filled ? 100m : 98m).Ending);

        // Where a close is missing, or the closes the store holds would have ended it on another session, its sale is read.
        Assert.Equal(PaperAccount.TimeLimit, PaperAccount.Ended(trailing, 0.1, Week, Closes(108m, null, 101m)).Ending);
        Assert.Equal(PaperAccount.TimeLimit, PaperAccount.Ended(trailing, 0.1, Week, Closes(85m, 112m, 101m)).Ending);
    }

    [Fact]
    public async Task ATradeTheMenusExitSoldIsReadOffTheStoreUnderThatExit()
    {
        using var store = await FamilyPagesStore();

        // The S&P 400's drift kept TX on 2026-09-18 at 100 over a stop of 90 for a target of 120, given twenty sessions,
        // under the menu's time exit ten sessions in unless it is above its buy: ten closes of 98 sell it on the tenth, the
        // night, a fifth of a risk down, where its sale alone would read under its stop.
        var time = ExitMenu.Swing.Single(choice => choice is { Kind: ExitKind.Time, N: 10, X: 0 });

        store.Execute(
            "INSERT INTO index_family_trade (index_code, family, ticker, session_date, place, entry, stop, target, trail, cap, ended_on, result, cost, benchmark, members, exit, risk_moves) VALUES " +
            $"('MID', 'drift', 'TX', '2026-09-18', 1, '100', '90', '120', NULL, 20, '{TheSwitch}', -0.2, 0, NULL, NULL, {time.Number}, 2);");
        FillAtTheirOwnBuy(store);
        PickBar(store, "TX", "2026-09-18", "100");

        foreach (var session in new[] { "2026-09-21", "2026-09-22", "2026-09-23", "2026-09-24", "2026-09-25", "2026-09-28", "2026-09-29", "2026-09-30", "2026-10-01", TheSwitch })
        {
            PickBar(store, "TX", session, "98");
        }

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var closed = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/paper?universe=400&bought=closed&show=drift"));

        Assert.Contains("<td>Sold at its time exit<span class=\"pb-day\">held 10 sessions</span></td>", PaperRowOf(closed, "<tr data-ticker=\"TX\""), StringComparison.Ordinal);
    }

    [Fact]
    public void TheStocksNotBoughtAreCountedAReasonAtATimeTheMostFirst()
    {
        PaperTaken Taken(string ticker, DateOnly listed, string? skipped) =>
            new(Planned with { Ticker = ticker, Listed = listed }, skipped is null ? 10 : 0, skipped);

        PaperWalk Walked(params PaperTaken[] trades) =>
            new([], trades, trades.Count(one => one.Skipped is null), trades.Count(one => one.Skipped is not null), 0, null, null, null, null, null, []);

        const string NoBar = "its stock holds no bar on its listing night or the session after";

        var unbought = PaperAccount.NotBought(
        [
            (Walked(Taken("T1", Week[1], PaperAccount.UnderTheCash), Taken("T2", Week[1], null), Taken("T3", Week[2], PaperAccount.NotSized)), [("U1", Week[4], PaperAccount.Waiting)]),
            (Walked(Taken("T4", Week[3], PaperAccount.UnderTheCash)), [("U2", Week[4], PaperAccount.Waiting), ("U3", Week[2], NoBar)]),
        ]);

        // Six not bought: two each under the cash and waiting, the cash's words first, then one each, the stored reason's
        // words before the walk's; each reason's stocks newest listing first.
        Assert.Equal(6, unbought.Count);
        Assert.Equal(
            [
                (PaperAccount.UnderTheCash, "T4,T1"),
                (PaperAccount.Waiting, "U1,U2"),
                (NoBar, "U3"),
                (PaperAccount.NotSized, "T3"),
            ],
            unbought.Reasons.Select(reason => (reason.Why, string.Join(",", reason.Stocks.Select(stock => stock.Ticker)))));
        Assert.Equal(0, PaperAccount.NotBought([(null, [])]).Count);
    }

    [Fact]
    public void ARulesTradesAreReadAgainstItsVariantsAndEachVariantHoldingAStockOnTheNightIsNamed()
    {
        PaperTrade Trade(string ticker, DateOnly listed, DateOnly? ended = null) =>
            Planned with { Ticker = ticker, Listed = listed, Bought = listed.AddDays(1), Ended = ended };

        // A stock taken off the same night is shared; the same stock off another night is not.
        Assert.Equal(
            new PaperOverlap(3, 1, 2),
            PaperAccount.Overlap([Trade("A", Week[0]), Trade("B", Week[1]), Trade("C", Week[2])], [Trade("A", Week[0]), Trade("B", Week[2]), Trade("D", Week[1])]));

        // Held on the night: bought on or before it and ended after it or not at all, one ended on the night held by none.
        Assert.Equal(
            ["V1", "V4"],
            PaperAccount.HeldBy(
                "B",
                OnTheNight,
                [
                    ("V1", [Trade("B", Week[1])]),
                    ("V2", [Trade("B", Week[1], ended: Week[3])]),
                    ("V3", [Planned with { Ticker = "B", Bought = new DateOnly(2026, 10, 5) }]),
                    ("V4", [Trade("B", Week[1], ended: new DateOnly(2026, 10, 5))]),
                    ("V5", [Trade("C", Week[1])]),
                    ("V6", [Trade("B", Week[1], ended: OnTheNight)]),
                ]));
    }

    [Fact]
    public void ATestYearIsBetterOrWorseThanWhatItWasReadAgainstWithTheEdgeEachUnitMade()
    {
        Assert.Equal(new PaperYear(2024, true, PaperAccount.Worse, -0.02, 150), PaperAccount.Year(2024, true, 150, -3.0, 0));
        Assert.Equal(new PaperYear(2026, false, PaperAccount.Better, 0.06, 50), PaperAccount.Year(2026, false, 50, 5.0, 2.0));
        Assert.Equal(new PaperYear(2022, true, PaperAccount.Even, 0, 10), PaperAccount.Year(2022, true, 10, 1.0, 1.0));
        Assert.Equal(new PaperYear(2023, true, PaperAccount.NoTrades, null, 0), PaperAccount.Year(2023, true, 0, 0, 0));
    }

    [Fact]
    public void AnAdjustedPIsPlacedOnTheScaleOfLuckInItsThreeZonesAndSaidInWords()
    {
        // The bar is the level a run is held to over twelve runs a year, one in 240.
        Assert.Equal(240, PaperAccount.RealOneIn);

        // Easily luck from one down to the level, at the level itself; maybe luck under it; real at the bar.
        Assert.Equal(new PaperLuck(PaperAccount.EasilyLuck, "about 100 times in 100", 0), PaperAccount.Luck(1));
        Assert.Equal((PaperAccount.EasilyLuck, "about 5 times in 100", PaperAccount.EasilyShare), Of(PaperAccount.Luck(LoopGate.Level)));
        Assert.Equal(PaperAccount.MaybeLuck, PaperAccount.Luck(LoopGate.Level - 0.0001).Zone);
        Assert.Equal((PaperAccount.Real, "about 1 in 240", PaperAccount.EasilyShare + PaperAccount.MaybeShare), Of(PaperAccount.Luck(LoopGate.Bar)));
        Assert.Equal(PaperAccount.MaybeLuck, PaperAccount.Luck(LoopGate.Bar + 0.0001).Zone);

        // Each zone read on a log scale over its own span: 0.37 a third of the way from one to the level, at a fifth of the
        // whole; a thousandth in the real zone; a ten-thousandth and under at the scale's end.
        Assert.Equal(0.6 * Math.Log10(0.37) / Math.Log10(LoopGate.Level), PaperAccount.Luck(0.37).Place, 12);
        Assert.Equal(0.9 + (0.1 * (Math.Log10(LoopGate.Bar) - Math.Log10(0.001)) / (Math.Log10(LoopGate.Bar) - Math.Log10(0.0001))), PaperAccount.Luck(0.001).Place, 12);
        Assert.Equal(1, PaperAccount.Luck(0.00001).Place, 12);

        // In words: so many times in a hundred, one time singular, and under one in a hundred one in so many.
        Assert.Equal("about 37 times in 100", PaperAccount.Luck(0.37).Words);
        Assert.Equal("about 1 time in 100", PaperAccount.Luck(0.012).Words);
        Assert.Equal("about 1 in 250", PaperAccount.Luck(0.004).Words);
        Assert.Equal("about 1 in 1,000", PaperAccount.Luck(0.001).Words);

        static (string, string, double) Of(PaperLuck luck) => (luck.Zone, luck.Words, luck.Place);
    }

    [Fact]
    public void ARecordsProgressCountsItsDecidedTradesOrABooksMonthsTowardTheCountWithItsMeanEdge()
    {
        PaperTrade Trade(DateOnly? ended, double? edge) => Planned with { Ended = ended, Edge = edge };

        // Three decided of 300, a hundredth of the count, their mean edge over the two whose comparison is read.
        Assert.Equal(
            new PaperProgress(3, LoopScreens.SwingTrades, 0.01, 0.2, 2),
            PaperAccount.Progress(false, [Trade(Week[2], 0.5), Trade(Week[3], -0.1), Trade(Week[3], null), Trade(null, null)], Listed, OnTheNight));

        // A book counts the whole months from its start: from the fifteenth of January to the second of October eight.
        Assert.Equal(new PaperProgress(8, LoopScreens.BookMonths, 8.0 / 36, null, 0), PaperAccount.Progress(true, [], new DateOnly(2026, 1, 15), OnTheNight));

        // The share is never past the whole.
        Assert.Equal(1.0, PaperAccount.Progress(false, [.. Enumerable.Repeat(Trade(Week[2], null), 400)], Listed, OnTheNight).Share);

        // The walk's open and closed trades: those it bought, one ended on the night closed, a skip in neither.
        var walk = new PaperWalk([], [new(Planned, 10, null), new(Planned with { Ended = Week[3] }, 10, null), new(Planned with { Ended = OnTheNight }, 10, null), new(Planned, 0, PaperAccount.UnderTheCash)], 3, 1, 0, null, null, null, null, null, []);

        Assert.Equal((1, 2), PaperAccount.Held(walk, OnTheNight));
    }

    [Fact]
    public void TheFundsReturnIsReadOverTheSameDaysOrWhereItsSeriesStartsLater()
    {
        Assert.Equal(new PaperFund(0.05, null), PaperAccount.FundOver(Week, session => session == Week[0] ? 400m : session == Week[4] ? 420m : null));
        Assert.Equal(new PaperFund(null, Week[2]), PaperAccount.FundOver(Week, session => session >= Week[2] ? 410m : null));
        Assert.Equal(new PaperFund(null, null), PaperAccount.FundOver([], _ => 400m));
    }

    // The S&P 400's page lists over a constructed week, each of their figures worked by hand. The pullback's list: C1 and
    // C2 on Monday, C1 sold at its target of 24 on Thursday two risks up and C2 at 26.40 under its stop of 27 on Wednesday,
    // 1.2 risks down, as their walks sell them; O1 on
    // Tuesday at 100 over a stop at 90 for a target of 120; S1 the same night at 100 over a stop a dime under; O2 on
    // Wednesday at 50 over 46 for 60; and U1 on Friday, the night, with no fill yet. The breakout's list: O3 on Tuesday at 40
    // over a stop of 36 trailing by 4. Each is bought at its own close, with no round trip.
    static async Task<TemporaryStore> PaperIndexStore()
    {
        var store = await FamilyPagesStore();

        void Trade(string family, string ticker, string session, int place, string entry, string stop, string? target, string? trail, string? ended, double? result, double? benchmark) =>
            store.Execute(
                "INSERT INTO index_family_trade (index_code, family, ticker, session_date, place, entry, stop, target, trail, cap, ended_on, result, cost, benchmark, members) VALUES " +
                $"('MID', '{family}', '{ticker}', '{session}', {place}, '{entry}', '{stop}', {Quoted(target)}, {Quoted(trail)}, 20, {Quoted(ended)}, {Number(result)}, 0, {Number(benchmark)}, {(benchmark is null ? "NULL" : "400")});");

        Trade("pullback", "C1", "2026-09-28", 1, "20", "18", "24", null, "2026-10-01", 2.0, 0.5);
        Trade("pullback", "C2", "2026-09-28", 2, "30", "27", "36", null, "2026-09-30", -1.2, null);
        Trade("pullback", "O1", "2026-09-29", 1, "100", "90", "120", null, null, null, null);
        Trade("pullback", "S1", "2026-09-29", 2, "100", "99.9", "110", null, null, null, null);
        Trade("pullback", "O2", "2026-09-30", 1, "50", "46", "60", null, null, null, null);
        Trade("breakout", "O3", "2026-09-29", 1, "40", "36", null, "4", null, null, null);
        FillAtTheirOwnBuy(store);
        Trade("pullback", "U1", "2026-10-02", 1, "25", "23", "29", null, null, null, null);

        foreach (var (ticker, closes) in new (string, string[])[]
        {
            ("C1", ["2026-09-28:20", "2026-09-29:21", "2026-09-30:22", "2026-10-01:24"]),
            ("C2", ["2026-09-28:30", "2026-09-29:29", "2026-09-30:26.4"]),
            ("O1", ["2026-09-29:100", "2026-09-30:105", "2026-10-01:110", "2026-10-02:117"]),
            ("O2", ["2026-09-30:50", "2026-10-01:48", "2026-10-02:46.8"]),
            ("O3", ["2026-09-29:40", "2026-09-30:44", "2026-10-01:47", "2026-10-02:46"]),
        })
        {
            foreach (var close in closes)
            {
                PickBar(store, ticker, close[..10], close[11..]);
            }
        }

        store.Execute("INSERT INTO indicator (ticker, session_date, name, value, bar_count) VALUES ('O1', '2026-10-02', 'atr14', 4, 14), ('O2', '2026-10-02', 'atr14', 1.6, 14), ('O3', '2026-10-02', 'atr14', 2, 14);");
        store.Execute("INSERT INTO membership (index_code, ticker, joined, \"left\", observed_at, sector, industry, name) VALUES ('MID', 'O1', NULL, NULL, '2026-10-02T22:00:00Z', NULL, NULL, 'Oak One Inc');");
        store.Execute("INSERT INTO market_bar (series, session_date, open, high, low, close, run_id) VALUES ('IJH', '2026-09-28', '50', '50', '50', '50', 'test'), ('IJH', '2026-10-02', '51', '51', '51', '51', 'test');");

        // An account of 10,000 risking one per cent a trade, a position up to the whole account.
        AccountFile.Write(store.Root, new AccountSettings(10_000m, 1m, 1m));

        return store;
    }

    // A rendered row of a table, from its opening tag to its close.
    static string PaperRowOf(string page, string opening)
    {
        var at = page.IndexOf(opening, StringComparison.Ordinal);

        Assert.True(at >= 0, $"no row opens {opening}");

        return page[at..(page.IndexOf("</tr>", at, StringComparison.Ordinal) + 5)];
    }

    [Fact]
    public async Task TheTilesDrawEachLiveOrProvisionalRulesReturnBesideTheIndexsFundOverTheSameDays()
    {
        using var store = await PaperIndexStore();
        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var page = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/paper?universe=400"));

        // The pullback's account of 10,000 at one per cent: Monday C1 50 shares at 20 and C2 33 at 30, 8,010 left; Tuesday O1
        // 10 at 100, 7,010 left, and S1, its stop a dime under 100, sized at the cap's 100 shares for 10,000, over the free
        // cash and skipped; Wednesday C2 sold at 26.40 first, 7,881.2, then O2 24 at 50 off a worth of 9,881.2, 6,681.2 left;
        // Thursday C1 sold at 24, 7,881.2; Friday O1 at 117 and O2 at 46.8: 7,881.2 and 1,170 and 1,123.2, 10,174.4, a return
        // of 0.01744.
        Assert.Contains("<div class=\"paper-tile\" data-family=\"pullback\" data-standing=\"provisional\" data-return=\"0.01744\" data-open=\"2\" data-closed=\"2\" data-since=\"2026-09-28\"", page, StringComparison.Ordinal);

        // The breakout's: O3 25 shares at 40 over its risk of 4, at 46 on Friday, 10,150, a return of 0.015.
        Assert.Contains("<div class=\"paper-tile\" data-family=\"breakout\" data-standing=\"provisional\" data-return=\"0.015\" data-open=\"1\" data-closed=\"0\" data-since=\"2026-09-29\"", page, StringComparison.Ordinal);

        // The families holding no trade say so, and every family has its tile in the one order, from 22.11 Quality at a fair
        // price's after the setups' and from 22.3 the short-term dip's before the sector heavyweights', and from 22.13 the
        // fundamentals-first family's none.
        Assert.Equal(
            ["pullback", "breakout", "drift", "dip", "heavyweight", "quality", "improving"],
            Regex.Matches(page, "<div class=\"paper-tile\" data-family=\"([a-z]+)\"").Select(match => match.Groups[1].Value));
        Assert.Contains("data-family=\"drift\" data-standing=\"provisional\" data-return=\"none\" data-open=\"0\" data-closed=\"0\" data-since=\"none\"", page, StringComparison.Ordinal);

        // The S&P 400's fund, IJH, over the same days from the earliest a tile counts from: 50 to 51, two per cent.
        Assert.Contains("<div class=\"paper-tile fund\" data-fund=\"IJH\" data-return=\"0.02\" data-from=\"2026-09-28\">", page, StringComparison.Ordinal);
        Assert.Contains("holding it over the same days, since <span class=\"day\">2026-09-28</span>", page, StringComparison.Ordinal);

        // The tiles stand one beneath another on a phone, and the chart stands beneath the tiles; every date the page draws
        // stands whole, and a record's too-few words stay on one line beside its return.
        Assert.Contains("@media (max-width:560px){\n .paper-tiles{grid-template-columns:1fr}\n}", Stylesheet.Css, StringComparison.Ordinal);
        Assert.Contains(".paper .day,.paper-variants .too-few{white-space:nowrap}", Stylesheet.Css, StringComparison.Ordinal);
        Assert.True(page.IndexOf("class=\"paper-tiles\"", StringComparison.Ordinal) < page.IndexOf("class=\"paper-chart\"", StringComparison.Ordinal), "the chart is not drawn beneath the tiles");
    }

    [Fact]
    public async Task WhatTheSystemBoughtDrawsEachOpenAndClosedTradeAndTheStocksNotBoughtUnderItsTabsAndChips()
    {
        using var store = await PaperIndexStore();
        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var page = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/paper?universe=400"));

        // Open by default, newest first: O2 bought Wednesday, then Tuesday's O1 and O3 in the family order.
        Assert.Equal(["O2", "O1", "O3"], Regex.Matches(page, "<tr data-ticker=\"([A-Z0-9]+)\" data-family=\"[a-z]+\" data-account=\"[^\"]+\" data-bought=\"[^\"]+\" data-standing=").Select(match => match.Groups[1].Value));
        Assert.Contains("<a class=\"chip\" data-filter=\"bought\" href=\"#/paper?universe=400\" aria-pressed=\"true\">Open · 3</a>", page, StringComparison.Ordinal);
        Assert.Contains("<a class=\"chip\" data-filter=\"bought\" href=\"#/paper?universe=400&bought=closed\" aria-pressed=\"false\">Closed · 2</a>", page, StringComparison.Ordinal);

        // O1 at 117, three under its target on a move of 4: near its target; 1.70 risks and 17 per cent, three sessions of
        // its twenty since its Tuesday listing; its company named and its stock opening its chart.
        var first = PaperRowOf(page, "<tr data-ticker=\"O1\"");

        Assert.Contains("data-standing=\"near its target\"", first, StringComparison.Ordinal);
        Assert.Contains("<td class=\"pb-stock\"><a href=\"#/chart/O1\">O1</a><span class=\"pb-co\">Oak One Inc</span></td>", first, StringComparison.Ordinal);
        Assert.Contains("<td class=\"r num\">100.00<span class=\"pb-day\">2026-09-29</span></td><td class=\"r num\">90.00</td><td class=\"r num\">120.00</td><td class=\"r num\">117.00<span class=\"pb-day\">3 of 20 sessions</span></td>", first, StringComparison.Ordinal);
        Assert.Contains("<span class=\"gain\">+1.70</span> risks<span class=\"pb-day\"><span class=\"gain\">+17.0%</span></span>", first, StringComparison.Ordinal);
        Assert.Contains("<span class=\"pb-standing near-target\" data-standing=\"near its target\">Near its target</span>", first, StringComparison.Ordinal);

        // O2 at 46.8, eight tenths over its stop of 46 on a move of 1.6: half a move, near its stop; -0.80 risks.
        var second = PaperRowOf(page, "<tr data-ticker=\"O2\"");

        Assert.Contains("data-standing=\"near its stop\"", second, StringComparison.Ordinal);
        Assert.Contains("<span class=\"loss\">-0.80</span> risks<span class=\"pb-day\"><span class=\"loss\">-6.4%</span></span>", second, StringComparison.Ordinal);
        Assert.Contains("46.80<span class=\"pb-day\">2 of 20 sessions</span>", second, StringComparison.Ordinal);

        // O3 trails: its stop raised from 36 to 43 by its close of 47, its target none as it trails, and at 46 a move and a
        // half over its stop on a move of 2, on track.
        var third = PaperRowOf(page, "<tr data-ticker=\"O3\"");

        Assert.Contains("data-standing=\"on track\"", third, StringComparison.Ordinal);
        Assert.Contains("<td class=\"r num\">43.00<span class=\"pb-day\">raised from 36.00</span></td><td class=\"r\">it trails</td>", third, StringComparison.Ordinal);
        Assert.Contains("<span class=\"gain\">+1.50</span> risks", third, StringComparison.Ordinal);

        // Beneath them the stocks not bought, a reason at a time, each linking to its chart, and the line about entries.
        Assert.Contains(
            "<p class=\"paper-unbought\" data-unbought=\"2\">Not bought: 2 listed stocks. <span class=\"pu-reason\" data-count=\"1\">The free cash was under its cost (1): <a href=\"#/chart/S1\" title=\"listed 2026-09-29\">S1</a>.</span> <span class=\"pu-reason\" data-count=\"1\">Waits on the next session's open (1): <a href=\"#/chart/U1\" title=\"listed 2026-10-02\">U1</a>.</span></p>",
            page,
            StringComparison.Ordinal);
        Assert.Contains($"<p class=\"paper-entry\">{MarkRenderer.EntryPriceLine}</p>", page, StringComparison.Ordinal);

        // Closed: C1 sold at its target of 24 on Thursday, held three sessions, two risks and twenty per cent, its edge 1.5
        // over its benchmark of half a risk; C2 at 26.40 under its stop of 27, held two, 1.2 risks and twelve per cent down,
        // its benchmark not written and its edge not read; each ending its own walk replayed over its closes.
        var closed = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/paper?universe=400&bought=closed"));
        var target = PaperRowOf(closed, "<tr data-ticker=\"C1\"");
        var stop = PaperRowOf(closed, "<tr data-ticker=\"C2\"");

        Assert.Contains("data-ended=\"2026-10-01\" data-edge=\"1.5\"", target, StringComparison.Ordinal);
        Assert.Contains("<td class=\"r num\">24.00<span class=\"pb-day\">2026-10-01</span></td><td>Reached its target<span class=\"pb-day\">held 3 sessions</span></td>", target, StringComparison.Ordinal);
        Assert.Contains("<span class=\"gain\">+2.00</span> risks<span class=\"pb-day\"><span class=\"gain\">+20.0%</span></span>", target, StringComparison.Ordinal);
        Assert.Contains("<td class=\"r num\"><span class=\"gain\">+1.50</span> risks</td>", target, StringComparison.Ordinal);
        Assert.Contains("<td class=\"r num\">26.40<span class=\"pb-day\">2026-09-30</span></td><td>Closed under its stop<span class=\"pb-day\">held 2 sessions</span></td>", stop, StringComparison.Ordinal);
        Assert.Contains("<span class=\"loss\">-1.20</span> risks<span class=\"pb-day\"><span class=\"loss\">-12.0%</span></span>", stop, StringComparison.Ordinal);
        Assert.Contains("<span class=\"degraded\">not read yet</span>", stop, StringComparison.Ordinal);
        Assert.Contains("aria-pressed=\"true\">Closed · 2</a>", closed, StringComparison.Ordinal);

        // A chip keeps one family's trades and the stocks it did not buy, with every other key kept in its links.
        var breakout = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/paper?universe=400&show=breakout&bought=closed"));

        Assert.Contains("href=\"#/paper?universe=400&show=breakout\" aria-pressed=\"false\">Open · 1</a>", breakout, StringComparison.Ordinal);
        Assert.Contains("aria-pressed=\"true\">Closed · 0</a>", breakout, StringComparison.Ordinal);
        Assert.Contains("<a class=\"chip\" data-filter=\"show\" data-family=\"breakout\" href=\"#/paper?universe=400&bought=closed&show=breakout\" aria-pressed=\"true\">", breakout, StringComparison.Ordinal);
        Assert.Contains("<p class=\"paper-unbought\" data-unbought=\"0\">Every stock these rules listed was bought.</p>", breakout, StringComparison.Ordinal);

        // A trade a rule's earlier version listed is folded beneath, that version's day and code under the pointer and not
        // in the text.
        var earlier = new MarkRenderer().PaperBought(
            new PaperPageView([], [], [], [new PaperBoughtView("list-pullback", "pullback", "E1", null, Listed, Filled, 100m, 90m, 120m, false, 20, null, null, null, [], null, "the version registered 2026-09-21, code v")], new PaperNotBought([]), [], null),
            new PaperLink("400"));

        Assert.Contains("<details class=\"paper-earlier\" data-earlier=\"1\">", earlier, StringComparison.Ordinal);
        Assert.Contains("<td><span title=\"the version registered 2026-09-21, code v\">an earlier version</span></td>", earlier, StringComparison.Ordinal);
        Assert.DoesNotContain(">the version registered", earlier, StringComparison.Ordinal);

        // Each tab, chip and select in a card keeps the card where it stood, and the tabs and chips wrap.
        Assert.Contains("const keeper = event.target.closest('a.chip[data-filter], a.sector-tile, a.sort');", new SinglePageApp().Shell("EquityBrief"), StringComparison.Ordinal);
        Assert.Contains(".paper-tabs,.paper-chips{display:flex;flex-wrap:wrap;", Stylesheet.Css, StringComparison.Ordinal);
        Assert.Contains("<section class=\"card\" data-card=\"paper-bought\">", page, StringComparison.Ordinal);
    }

    // The S&P 500's breakout over the constructed night the family pages read, as the paper trading test stores it: the live
    // rule and one variant registered, the variant keeping T7 and T1, ended, T6, open through a split, and K1 off the night
    // the page list bought it; the live rule's own trade of B0 benchmarked at a quarter of a risk; and the newest per-rule
    // test reading the live rule passed against its benchmark over three test years and the variant within luck.
    static async Task<TemporaryStore> PaperVariantsStore()
    {
        var store = await FamilyPagesStore();

        RegisteredRule(store, 201, TheSetupFamilies.Breakouts[0], "2026-01-02T12:00:00Z");
        RegisteredRule(store, 202, TheSetupFamilies.Breakouts[^2], "2026-01-02T12:00:00Z");
        Kept(store, ProvisionalBreakout, "breakout", "T7", "2026-09-28", "2026-09-29", -0.5, 0.0);
        Kept(store, ProvisionalBreakout, "breakout", "T1", "2026-09-28", "2026-09-30", 1.0, 0.2);
        Kept(store, ProvisionalBreakout, "breakout", "T6", "2026-10-01", null, null, null);
        Kept(store, ProvisionalBreakout, "breakout", "K1", TheSwitch, null, null, null);
        Kept(store, LiveBreakout, "breakout", "B0", "2026-09-25", "2026-09-30", 1.0, 0.25);

        foreach (var (ticker, session, close) in new[] { ("T7", "2026-09-28", "100"), ("T1", "2026-09-28", "100"), ("T6", "2026-10-01", "50"), ("T6", TheSwitch, "51.5") })
        {
            PickBar(store, ticker, session, close);
        }

        store.Execute(
            "INSERT INTO loop_rule (run_id, month, index_code, family, rule, standing, words, against, unit, units, blocks, adjusted, gate, stable, counted, better, trimmed, counts, passed, why) VALUES " +
            $"('loop-rules-GSPC-20261003T120000Z', '2026-10', 'GSPC', 'breakout', '{LiveBreakout}', 'live', 'the live breakout', 'its benchmark', 'risks', 420, 9, 0.001, 1, 1, 3, 3, 4.5, 1, 1, NULL), " +
            $"('loop-rules-GSPC-20261003T120000Z', '2026-10', 'GSPC', 'breakout', '{ProvisionalBreakout}', 'variant', 'the variant', '{LiveBreakout}', 'risks', 380, 9, 0.3, 0, 1, 3, 2, 1.0, 1, 0, NULL);");
        store.Execute(
            "INSERT INTO loop_rule_year (run_id, index_code, family, rule, year, complete, units, total, compared_units, compared_total) VALUES " +
            $"('loop-rules-GSPC-20261003T120000Z', 'GSPC', 'breakout', '{LiveBreakout}', 2024, 1, 150, -3.0, 150, 0), " +
            $"('loop-rules-GSPC-20261003T120000Z', 'GSPC', 'breakout', '{LiveBreakout}', 2025, 1, 140, 12.5, 140, 0), " +
            $"('loop-rules-GSPC-20261003T120000Z', 'GSPC', 'breakout', '{LiveBreakout}', 2026, 0, 50, 5.0, 50, 0);");
        AccountFile.Write(store.Root, new AccountSettings(10_000m, 1m, 1m));

        return store;
    }

    [Fact]
    public async Task WhatTheVariantsDidDifferentlyDrawsTheFamilyChosenInTheLinkWithTheTradesOnlyAVariantTook()
    {
        using var store = await PaperVariantsStore();
        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var page = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/paper?variants=breakout"));
        var variantKey = RunScreen.Slug(ProvisionalBreakout);

        // The select lists every family in the one order, the first its key's absence, the one the link names chosen.
        var select = Regex.Match(page, "<select data-merge-key=\"variants\".*?</select>", RegexOptions.Singleline).Value;

        Assert.Equal(["", "breakout", "drift", "dip", "heavyweight", "quality", "improving"], Regex.Matches(select, "<option value=\"([^\"]*)\"").Select(match => match.Groups[1].Value));
        Assert.Contains("<option value=\"breakout\" selected>Breakout · live rule and 1 variant</option>", select, StringComparison.Ordinal);
        Assert.DoesNotContain("<option value=\"fundamentals\"", select, StringComparison.Ordinal);

        // The live rule first, then V1: the page list's B0, K1 and K2 against the variant's T7, T1, T6 and K1, the one stock
        // both took off one night shared, K1; each with its verdict beside it.
        var live = PaperRowOf(page, "<tr data-account=\"list-breakout\"");
        var variant = PaperRowOf(page, $"<tr data-account=\"{variantKey}\"");

        Assert.StartsWith("<tr data-account=\"list-breakout\" data-standing=\"live\" data-trades=\"3\" data-shared=\"1\" data-only=\"2\">", live, StringComparison.Ordinal);
        Assert.Contains($"<span class=\"pv-verdict\" data-verdict=\"{RuleVerdict.Passed}\">", live, StringComparison.Ordinal);
        Assert.StartsWith($"<tr data-account=\"{variantKey}\" data-standing=\"variant\" data-trades=\"4\" data-shared=\"1\" data-only=\"3\">", variant, StringComparison.Ordinal);
        Assert.Contains($"<span class=\"pv-verdict\" data-verdict=\"{RuleVerdict.WithinLuck}\">", variant, StringComparison.Ordinal);
        Assert.Contains("<span class=\"too-few\">too few trades (2 of 300)</span>", variant, StringComparison.Ordinal);

        // The variant named by its number, what it changes the settings its tab names, its full words under the pointer.
        var (_, tabs) = RuleScreen.Accounts("breakout", true, "S&P 500", await new ReadApi(store.DatabaseFile, EquityBrief.Core.Time.FixedClock.At(new DateTimeOffset(2026, 10, 3, 12, 0, 0, TimeSpan.Zero), EquityBrief.Core.Time.SessionZones.UnitedStates)).RegisteredCandidatesAsync(), new DateOnly(2026, 10, 2));
        var tab = Assert.Single(tabs).Tab;

        Assert.StartsWith("V1 · ", tab, StringComparison.Ordinal);
        Assert.Contains($"\">V1</a>", variant, StringComparison.Ordinal);
        Assert.Contains($"<td>{tab["V1 · ".Length..]}</td>", variant, StringComparison.Ordinal);
        Assert.Contains($"title=\"{char.ToUpperInvariant(ProvisionalBreakout[0])}{ProvisionalBreakout[1..]}; counts the version registered 2026-01-02, code v; from ", variant, StringComparison.Ordinal);

        // Beneath, the trades only V1 took, newest first: T6 open at 103 by its closes' ratio through its split, a risk; T1
        // and T7 each sold under its trailing stop before its cap.
        var only = Regex.Match(page, "<table class=\"paper-only\".*?</table>", RegexOptions.Singleline).Value;

        Assert.Equal(["T6", "T1", "T7"], Regex.Matches(only, "<tr data-ticker=\"([A-Z0-9]+)\"").Select(match => match.Groups[1].Value));
        Assert.Contains("<tr data-ticker=\"T6\" data-bought=\"2026-10-01\" data-by=\"V1\">", only, StringComparison.Ordinal);
        Assert.Contains("<td><span class=\"pill\">open</span></td><td class=\"r num\"><span class=\"gain\">+1.00</span> risks<span class=\"pb-day\"><span class=\"gain\">+3.0%</span></span></td>", only, StringComparison.Ordinal);
        Assert.Contains("<td>Closed under its trailing stop</td><td class=\"r num\"><span class=\"loss\">-0.50</span> risks", only, StringComparison.Ordinal);
        Assert.Contains($"<p class=\"paper-adopted\">{MarkRenderer.AdoptedLine}</p>", page, StringComparison.Ordinal);

        // The page list's K1, which V1 holds too, names it.
        Assert.Contains("<span class=\"pb-also\">V1 holds it too</span>", PaperRowOf(page, "<tr data-ticker=\"K1\""), StringComparison.Ordinal);

        // A family with no variant says so, and draws no trade only a variant took.
        var none = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/paper?variants=fundamentals"));

        Assert.Contains("<p class=\"degraded\" data-variants=\"none\">No variant is registered for Fundamentals first on this index, so its rule alone trades.</p>", none, StringComparison.Ordinal);
        Assert.DoesNotContain("Bought only by a variant", none, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheRuleScorecardDrawsItsHistoryAndItsRecordSinceItWentLiveAsTwoHalvesAndItsVariantsBeneath()
    {
        using var store = await PaperVariantsStore();
        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var page = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/paper?rule=breakout"));
        var card = Regex.Match(page, "<div class=\"score\".*?</details>", RegexOptions.Singleline).Value;
        var live = card.IndexOf("<div class=\"score-half live\"", StringComparison.Ordinal);
        var history = card[card.IndexOf("<div class=\"score-half history\"", StringComparison.Ordinal)..live];
        var since = card[live..card.IndexOf("<details class=\"score-variants\"", StringComparison.Ordinal)];

        Assert.StartsWith($"<div class=\"score\" data-family=\"breakout\" data-standing=\"live\" data-verdict=\"{RuleVerdict.Passed}\">", card, StringComparison.Ordinal);

        // Adjusted p values, version codes and registration days stand under the pointer and nowhere in the page's text, a
        // heading's tip being under the pointer too.
        var shown = Regex.Replace(page, "<span class=\"head-tip\"[^>]*>.*?</span></th>", "</th>", RegexOptions.Singleline);
        var text = Regex.Replace(Regex.Replace(shown, "<(script|style)\\b.*?</\\1>", " ", RegexOptions.Singleline), "<[^>]+>", " ");

        Assert.Contains("adjusted p 0.0010 against the bar", page, StringComparison.Ordinal);
        Assert.Contains("; counts the version registered 2026-01-02, code v", page, StringComparison.Ordinal);
        Assert.DoesNotContain("adjusted p", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotMatch(", code \\w", text);
        Assert.DoesNotMatch("registered \\d{4}-\\d{2}-\\d{2}", text);
        Assert.Contains("<select data-merge-key=\"rule\" aria-label=\"The rule whose scorecard is drawn\"><option value=\"\">Pullback · live rule</option><option value=\"breakout\" selected>Breakout · live rule</option>", page, StringComparison.Ordinal);

        // On history, 2024 to 2026: better in three of three complete years as the test counted, and a box a year, 2024's
        // three risks under its benchmark over 150 trades two hundredths a trade, 2025's 12.5 over 140 nine, and 2026's five
        // over 50 so far a tenth.
        Assert.Contains("<span class=\"sh-lbl\">On history, 2024 to 2026</span>", history, StringComparison.Ordinal);
        Assert.Contains("Better in 3 of 3 complete years.", history, StringComparison.Ordinal);
        Assert.Contains("<div class=\"sy worse\" data-year=\"2024\" data-word=\"Worse\" data-edge=\"-0.02\"", history, StringComparison.Ordinal);
        Assert.Contains("<span class=\"sy-y\">2024</span><b>Worse</b><span class=\"sy-e\"><span class=\"loss\">-0.02</span></span>", history, StringComparison.Ordinal);
        Assert.Contains("<span class=\"sy-y\">2025</span><b>Better</b><span class=\"sy-e\"><span class=\"gain\">+0.09</span></span>", history, StringComparison.Ordinal);
        Assert.Contains("<span class=\"sy-y\">2026 so far</span><b>Better</b><span class=\"sy-e\"><span class=\"gain\">+0.10</span></span>", history, StringComparison.Ordinal);

        // Its adjusted p of a thousandth real on the scale, its mark at its place, in words, the p itself under the pointer;
        // and the warning that its settings were tuned on these years.
        Assert.Contains($"<figure class=\"luck\" data-place=\"{PaperAccount.Luck(0.001).Place.ToString("R", System.Globalization.CultureInfo.InvariantCulture)}\" title=\"adjusted p 0.0010 against the bar 0.0042\">", history, StringComparison.Ordinal);
        Assert.Contains("<p class=\"sh-line\" data-zone=\"Real\">Real: a rule picking at random would do this well about 1 in 1,000. To count as real it needs fewer than 1 in 240.</p>", history, StringComparison.Ordinal);
        Assert.Contains($"<p class=\"sh-warn\">{MarkRenderer.TunedLine}</p>", history, StringComparison.Ordinal);

        // Since it went live, apart: B0 decided of 300, its edge read off its live rule's own trade, three quarters of a risk,
        // not evidence yet; no figure of the history in it and none of it in the history.
        Assert.Contains("<p class=\"sh-count\" data-counted=\"1\" data-needed=\"300\">1 of 300 trades decided</p>", since, StringComparison.Ordinal);
        Assert.Contains("<p class=\"sh-line\" data-edge=\"0.75\">So far: <span class=\"gain\">+0.75</span> risks a trade against the same plan on every stock, over 1 decided trade whose comparison is read. Not evidence yet.</p>", since, StringComparison.Ordinal);
        Assert.DoesNotContain("sy ", since, StringComparison.Ordinal);
        Assert.DoesNotContain("sh-count", history, StringComparison.Ordinal);

        // Its variant folds out beneath with its verdict, its years better than its rule and its progress.
        Assert.Contains($"<details class=\"score-variants\" data-variants=\"1\"><summary>Its 1 variant</summary>", card, StringComparison.Ordinal);
        Assert.Contains($"<tr data-account=\"{RunScreen.Slug(ProvisionalBreakout)}\" data-verdict=\"{RuleVerdict.WithinLuck}\" data-better=\"2\">", card, StringComparison.Ordinal);
        Assert.Contains("<td class=\"r num\">2 of 3</td><td class=\"r num\">2 of 300 decided</td>", card, StringComparison.Ordinal);

        // A provisional rule keeps no record since it went live, and a rule no test has read says so.
        var provisional = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/paper?rule=dip"));

        Assert.Contains($"<p class=\"sh-line\" data-record=\"none\">{MarkRenderer.NoRecordLine}</p>", provisional, StringComparison.Ordinal);
        Assert.Contains("<p class=\"degraded\" data-history=\"none\">No monthly test has read this rule on this index yet.</p>", provisional, StringComparison.Ordinal);
    }

    [Fact]
    public async Task EachSelectsChoiceIsKeptInTheLinkWithEveryOtherPartsChoice()
    {
        using var store = await PaperVariantsStore();
        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        // The link names every part's choice and the page draws each, its own links keeping the others.
        var page = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/paper?family=breakout&bought=closed&show=pullback&variants=drift&rule=heavyweight"));

        Assert.Contains("data-family=\"breakout\" data-walked=\"true\" data-example=\"false\" data-bought=\"closed\" data-show=\"pullback\" data-variants=\"drift\" data-rule=\"heavyweight\">", page, StringComparison.Ordinal);
        Assert.Contains("<option value=\"drift\" selected>", Regex.Match(page, "<select data-merge-key=\"variants\".*?</select>", RegexOptions.Singleline).Value, StringComparison.Ordinal);
        Assert.Contains("<option value=\"heavyweight\" selected>", Regex.Match(page, "<select data-merge-key=\"rule\".*?</select>", RegexOptions.Singleline).Value, StringComparison.Ordinal);
        Assert.Contains("data-filter=\"show\" href=\"#/paper?universe=500&family=breakout&bought=closed&variants=drift&rule=heavyweight\" aria-pressed=\"false\">Every family</a>", page, StringComparison.Ordinal);
        Assert.Contains("<a href=\"#/paper?universe=500&bought=closed&show=pullback&variants=drift&rule=heavyweight\">Live and provisional only</a>", page, StringComparison.Ordinal);
        Assert.Equal("#/paper?universe=400&family=drift&bought=closed&show=pullback&variants=breakout&rule=heavyweight", new PaperLink("400", "drift", true, "pullback", "breakout", "heavyweight").Href);

        // A select's choice is merged into the link under its key, the first choice its absence, a choice inside a card
        // keeping the card where it stood; a name the link gives that is no family is read as none.
        var shell = new SinglePageApp().Shell("EquityBrief");

        Assert.Contains("if (kept) { fresh = true; location.hash = merged(kept.getAttribute('data-merge-key'), kept.value === '' ? null : kept.value); }", shell, StringComparison.Ordinal);
        Assert.Contains("const keptCard = kept ? kept.closest('section.card[data-card]') : null;", shell, StringComparison.Ordinal);
        Assert.Contains("if (keptCard) { keepPlace(kept, 'section.card[data-card=\"' + CSS.escape(keptCard.getAttribute('data-card')) + '\"]'); }", shell, StringComparison.Ordinal);
        Assert.True(shell.IndexOf("if (keptCard) { keepPlace(", StringComparison.Ordinal) < shell.IndexOf("if (kept) { fresh = true;", StringComparison.Ordinal), "the card's place is kept after the link moves");
        Assert.Contains("data-variants=\"pullback\" data-rule=\"pullback\">", WebUtility.HtmlDecode(await client.GetStringAsync("/screens/paper?variants=nothing&rule=nothing")), StringComparison.Ordinal);
    }

    [Fact]
    public async Task YourOwnTradesStandApartWithTheirCountTheirReturnAndTheirTradeLog()
    {
        using var store = await PaperVariantsStore();
        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        Assert.Contains("data-yours=\"none\">You have not marked a trade as taken on this index.", WebUtility.HtmlDecode(await client.GetStringAsync("/screens/paper")), StringComparison.Ordinal);

        // A trade taken from a card filled at 50 on the Wednesday over a stop at 45: one per cent of 10,000 buys 20 shares,
        // 1,000, paying half its round trip at the buy, the published table's 0.129 per cent at each end for a company valued
        // at none, 0.645; at 52 on the night they are worth 1,040 beside 8,999.355 in cash, a return of 0.0039355.
        store.Execute(
            "INSERT INTO taken_trade (ticker, taken_at, index_code, family, night, sector, fill, fill_date, provisional, entered, stop, target, trail, cap) VALUES " +
            "('YT', '2026-09-30T15:00:00Z', 'GSPC', 'pullback', '2026-09-29', NULL, '50', '2026-09-30', 0, 1, '45', '60', NULL, 20);");
        PickBar(store, "YT", "2026-09-30", "50");
        PickBar(store, "YT", TheSwitch, "52");

        var page = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/paper"));
        var yours = Regex.Match(page, "<div class=\"paper-yours\".*?</div>", RegexOptions.Singleline).Value;

        Assert.StartsWith("<div class=\"paper-yours\" data-trades=\"1\" data-return=\"0.0039355\">", yours, StringComparison.Ordinal);
        Assert.Contains("<span class=\"paper-count\">1 trade · 0 decided</span>", yours, StringComparison.Ordinal);
        Assert.Contains("<a class=\"paper-log\" href=\"#/picks?universe=500&account=you\">trade log</a>", yours, StringComparison.Ordinal);
        Assert.Contains($"{RuleVerdict.Yours}.", yours, StringComparison.Ordinal);
    }
}
