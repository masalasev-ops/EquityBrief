using System.Net;
using EquityBrief.Core.Cards;
using EquityBrief.Tests.Checks;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.Marks;

namespace EquityBrief.Tests.Reading;

// read-surface, 21.11: the combined account, every live rule's trades in one pot walked by the core's one function, one
// position a stock, three open a GICS sector, each book a fifth of the account and a buy the free cash cannot cover
// skipped, worked by hand; and paper trading's tile, line and card for it.
// see: The combined account holds every live rule's trades in one pot with one position a stock and three a sector, each book family a fifth of it
public partial class ReadSurface
{
    // The rows this check reaches from 21.11: paper trading's, section 17's settings and section 18's trade two families
    // took.
    internal static readonly string[] CombinedPageClaims =
    [
        CheckReach.Key("15.21 Paper trading", Scope.PaperCombined),
        CheckReach.Key(Scope.LimitsTable, Scope.PaperCombined),
        CheckReach.Key(Scope.FailureTable, Scope.PaperTwoFamilies),
    ];

    static readonly DateOnly CombinedDay1 = new(2026, 11, 2);
    static readonly DateOnly CombinedDay2 = new(2026, 11, 3);
    static readonly DateOnly CombinedDay3 = new(2026, 11, 4);

    static CombinedTrade Handed(string ticker, DateOnly bought, decimal buy, decimal? stop, string family, int familyPlace, string sector, int place = 1, DateOnly? ended = null, decimal? sale = null, int? book = null) =>
        new(new PaperTrade(ticker, bought, buy, stop, ended, sale, null, place, book), family, familyPlace, sector, book is not null);

    [Fact]
    public void TheCombinedAccountIsWorkedByHandOverAStockTwoFamiliesPickedASectorAtItsCapABuyTheCashCannotCoverAndTwoBooks()
    {
        // $10,000, 1 per cent a trade, and from 22.59 at most a tenth of the account in one stock whatever the settings state:
        // a swing trade risks $100 and holds at most $1,000, the settings' fifth no longer read.
        var account = new AccountSettings(10_000m, 1m, 0.2m);

        // On the first session, in Tonight's order: the pullback's A at 100 over a stop of 99, $10,000 by its risk and so the
        // cap's 10 shares; the breakout's A, already held; its B at 50 over 49, the cap's 20 shares; the drift's C at 20 over
        // 19, the cap's 50 shares; its D at 10, a fourth open in sector S1, skipped; the Fundamentals family's G at 30, a book
        // of 10 sized at a tenth of a fifth of the account, $200, 6 shares; the dip's E at 25 over 24, the cap's 40 shares in
        // S2; and quality's F at 40, $200, 5 shares. Cash left $5,620.
        // On the second session, the account worth $10,000 at its buys, the pullback's H1 to H5 at 100 over 99 take the cap's
        // 10 shares each, $5,000, and H6's $1,000 is over the free $620 and is skipped where it stands; quality's J after it,
        // $200 at 40, is still bought, leaving $420. On the third A is sold at 110 first, $1,100, and the drift's I at 50 over
        // 49 is bought, the cap now a tenth of $10,100, 20 shares at $1,000.
        CombinedTrade[] trades =
        [
            Handed("A", CombinedDay1, 100m, 99m, "pullback", BuyOrder.Of("pullback"), "S1", ended: CombinedDay3, sale: 110m),
            Handed("A", CombinedDay1, 100m, 99m, "breakout", BuyOrder.Of("breakout"), "S1"),
            Handed("B", CombinedDay1, 50m, 49m, "breakout", BuyOrder.Of("breakout"), "S1", place: 2),
            Handed("C", CombinedDay1, 20m, 19m, "drift", BuyOrder.Of("drift"), "S1"),
            Handed("D", CombinedDay1, 10m, 9.5m, "drift", BuyOrder.Of("drift"), "S1", place: 2),
            Handed("E", CombinedDay1, 25m, 24m, "dip", BuyOrder.Of("dip"), "S2"),
            Handed("F", CombinedDay1, 40m, null, "quality", BuyOrder.Of("quality"), "S3", book: 10),
            Handed("G", CombinedDay1, 30m, null, "improving", BuyOrder.Of("improving"), "S4", book: 10),
            .. Enumerable.Range(1, 6).Select(at => Handed("H" + at, CombinedDay2, 100m, 99m, "pullback", BuyOrder.Of("pullback"), "S" + (4 + at), place: at)),
            Handed("J", CombinedDay2, 40m, null, "quality", BuyOrder.Of("quality"), "S11", book: 10),
            Handed("I", CombinedDay3, 50m, 49m, "drift", BuyOrder.Of("drift"), "S12"),
        ];
        var buys = trades.GroupBy(one => one.Trade.Ticker, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.First().Trade.Buy, StringComparer.Ordinal);
        var walked = PaperAccount.Combined(account, trades, [CombinedDay1, CombinedDay2, CombinedDay3], (ticker, _) => buys[ticker]);

        Assert.Equal(0.1m, PaperAccount.AccountCap);
        Assert.Equal(
            [
                "pullback|A|10|", $"breakout|A|0|{PaperAccount.AlreadyHeld}", "breakout|B|20|", "drift|C|50|", $"drift|D|0|{PaperAccount.SectorFull}", "improving|G|6|", "dip|E|40|", "quality|F|5|",
                "pullback|H1|10|", "pullback|H2|10|", "pullback|H3|10|", "pullback|H4|10|", "pullback|H5|10|", $"pullback|H6|0|{PaperAccount.UnderTheCash}", "quality|J|5|",
                "drift|I|20|",
            ],
            walked.Trades.Select(one => $"{one.From.Family}|{one.Taken.Trade.Ticker}|{one.Taken.Shares}|{one.Taken.Skipped}"));
        Assert.Equal((1, 1, 1), (walked.AlreadyHeld, walked.SectorFull, walked.UnderCash));
        Assert.Equal((13, 3), (walked.Walk.Taken, walked.Walk.Skipped));

        // Marked at their buys, the account stands at $10,000 on the first two sessions and at $10,100 once A is sold.
        Assert.Equal([10_000m, 10_000m, 10_100m], walked.Walk.Values.Select(one => one.Value));
        Assert.Equal(0.01, walked.Walk.Return!.Value, 12);
    }

    // Paper trading hands the account each family's place in Tonight's order: the Fundamentals family's buy of a session
    // before the dip's, where the families' page order placed it after.
    [Fact]
    public void PaperTradingHandsTheCombinedAccountEachFamilysPlaceInTonightsOrder()
    {
        EquityBrief.Api.Reading.PaperAccountSource Account(string family, string ticker, bool book) =>
            new(family, family, PaperAccountView.Live, family, family, null, book, [(new PaperTrade(ticker, CombinedDay1, 100m, book ? null : 99m, null, null, null, 1, book ? 10 : null), null)], CombinedDay1);

        var view = EquityBrief.Api.Reading.PaperScreen.Combined(
            [Account("dip", "E", book: false), Account("improving", "G", book: true)],
            new AccountSettings(10_000m, 1m, 0.2m),
            [CombinedDay1, CombinedDay2],
            CombinedDay2,
            (_, _) => 100m,
            _ => 100m,
            new Dictionary<string, EquityBrief.Api.Reading.PaperStockRow>(StringComparer.Ordinal));

        Assert.Equal(["improving G", "dip E"], view!.Trades.Select(trade => trade.Family + " " + trade.Ticker));
    }

    // Tonight's order of a night's buys, as the combined account fills a session and Tonight draws them, held by one order.
    [Fact]
    public void TheCombinedAccountFillsASessionInTheOrderTonightDrawsItsBuysIn()
    {
        Assert.Equal(
            ["pullback", "breakout", "drift", "fundamentals", "improving", "dip", "heavyweight", "quality"],
            BuyOrder.Families);
        Assert.Equal(BuyOrder.Families.Count - 1, BuyOrder.Of("quality"));

        BuyPointView Point(string family) => new(family, family, new FamilyPickCell(new ListingCell("T" + family, CombinedDay1, 1, 0, 10m, []), 1, 10m, 9m, 12m, false, 2m, "why", []));

        PlanlessBuyView Planless(string family) => new(family, family, "P" + family, null, "why", "business", 10m, "sells", "#/");

        var drawn = MarkRenderer.BuysInOrder(
            [Point("pullback"), Point("breakout"), Point("drift"), Point("fundamentals"), Point("dip")],
            [Planless("improving"), Planless("heavyweight"), Planless("quality")]);

        Assert.Equal(BuyOrder.Families, drawn.Select(buy => buy.Point?.Family ?? buy.Planless!.Family));
    }

    [Fact]
    public void PaperTradingDrawsTheCombinedAccountsTileLineAndCardWithWhatItSkippedAndWhy()
    {
        var account = new AccountSettings(10_000m, 1m, 0.2m);
        CombinedTrade[] handed =
        [
            Handed("A", CombinedDay1, 100m, 99m, "pullback", 0, "S1"),
            Handed("A", CombinedDay1, 100m, 99m, "breakout", 1, "S1"),
        ];
        var walk = PaperAccount.Combined(account, handed, [CombinedDay1, CombinedDay2], (_, session) => session == CombinedDay1 ? 100m : 103m).Walk;
        var combined = new PaperCombinedView(
            walk,
            CombinedDay1,
            1,
            0,
            1,
            0,
            0,
            [
                new("pullback", "A", CombinedDay1, 100m, 10, null, null, 0.03, true),
                new("breakout", "A", CombinedDay1, 100m, 0, PaperAccount.AlreadyHeld, null, null, true),
                new("improving", "G", CombinedDay1, 30m, 6, null, null, 0.01, true),
                new("dip", "E", CombinedDay1, 25m, 40, null, null, 0.0, true),
            ]);
        var marks = new MarkRenderer();
        var tiles = WebUtility.HtmlDecode(marks.PaperTiles([], null, combined));
        var chart = WebUtility.HtmlDecode(marks.PaperChart([], null, combined));
        var card = WebUtility.HtmlDecode(marks.PaperCombined(combined));

        // The tile: its return on the size, +0.3% with A up 3 per cent on its $1,000, what it holds and what it skipped.
        Assert.Contains("<div class=\"paper-tile combined\" data-standing=\"combined\"", tiles, StringComparison.Ordinal);
        Assert.Contains("data-skipped=\"1\"", tiles, StringComparison.Ordinal);
        Assert.Contains(">All setups together<", tiles, StringComparison.Ordinal);
        Assert.Contains("+0.3%", tiles, StringComparison.Ordinal);
        Assert.Contains("Up 0.3% since 2 Nov, with 1 trade open. 0 closed, 1 skipped.", tiles, StringComparison.Ordinal);

        // Its line in the chart, keyed by its own name.
        Assert.Contains("data-standing=\"combined\"", chart, StringComparison.Ordinal);
        Assert.Contains("</span>All setups together</button>", chart, StringComparison.Ordinal);

        // The card: one sentence counting what it skipped and why, and each trade with the setup it came from.
        Assert.Contains("It skipped 1 pick(s): 1 because another setup already owned the stock, 0 because three positions in the stock's sector were already open, and 0 because the free cash could not cover the buy.", card, StringComparison.Ordinal);
        Assert.Contains($"data-family=\"breakout\" data-ticker=\"A\" data-bought=\"2026-11-02\" data-open=\"true\" data-skipped=\"{PaperAccount.AlreadyHeld}\"", card, StringComparison.Ordinal);
        Assert.Contains($"skipped: {PaperAccount.AlreadyHeld}", card, StringComparison.Ordinal);
        Assert.Contains(">10</td>", card, StringComparison.Ordinal);
        Assert.Contains("+3.0%", card, StringComparison.Ordinal);

        // From 22.59 the session's rows in the order the account filled them, Tonight's, the Fundamentals' G before the dip's E
        // where the families' page order drew the dip first, under the line saying how it fills.
        Assert.Contains(WebUtility.HtmlDecode(MarkRenderer.CombinedFillLine), card, StringComparison.Ordinal);
        Assert.Equal(
            ["pullback A", "breakout A", "improving G", "dip E"],
            System.Text.RegularExpressions.Regex.Matches(card, "<tr data-family=\"([a-z]+)\" data-ticker=\"([A-Z]+)\"").Select(row => row.Groups[1].Value + " " + row.Groups[2].Value));

        // The card's words carry no term of 15.23's word list.
        var said = PlainWords.Visible(marks.PaperCombined(combined));
        Assert.All(PlainWords.Terms(), term => Assert.DoesNotMatch(PlainWords.Pattern(term), said));

        // No combined account to draw before any live rule has a trade.
        Assert.Contains("data-combined=\"none\"", marks.PaperCombined(null), StringComparison.Ordinal);
    }
}
