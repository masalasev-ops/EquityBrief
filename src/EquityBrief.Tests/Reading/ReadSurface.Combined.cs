using System.Net;
using EquityBrief.Core.Cards;
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
        // $10,000, 1 per cent a trade and at most a fifth in one stock: a swing trade risks $100 and holds at most $2,000.
        var account = new AccountSettings(10_000m, 1m, 0.2m);

        // On the first session, in the families' order: the pullback's A at 100 over a stop of 99, $10,000 by its risk and
        // so the cap's 20 shares; the breakout's A, already held; its B at 50 over 49, the cap's 40 shares; the drift's C at
        // 20 over 19, 100 shares at the cap and its risk alike; its D at 10, a fourth open in sector S1, skipped; the dip's E
        // at 25 over 24, the cap's 80 shares in S2; quality's F at 40, a book of 10 sized at a tenth of a fifth of the
        // account, $200, 5 shares; and the Fundamentals family's G at 30, $200 at 30, 6 shares. Cash left $1,620.
        // On the second session the pullback's H at 100 over 99 sizes the cap's 20 shares, $2,000, over the free $1,620,
        // and is skipped. On the third A is sold at 110 first, $2,200, and then the drift's I at 50 over 49 is bought, the
        // cap now a fifth of $10,200, 40 shares at $2,000.
        CombinedTrade[] trades =
        [
            Handed("A", CombinedDay1, 100m, 99m, "pullback", 0, "S1", ended: CombinedDay3, sale: 110m),
            Handed("A", CombinedDay1, 100m, 99m, "breakout", 1, "S1"),
            Handed("B", CombinedDay1, 50m, 49m, "breakout", 1, "S1", place: 2),
            Handed("C", CombinedDay1, 20m, 19m, "drift", 2, "S1"),
            Handed("D", CombinedDay1, 10m, 9.5m, "drift", 2, "S1", place: 2),
            Handed("E", CombinedDay1, 25m, 24m, "dip", 3, "S2"),
            Handed("F", CombinedDay1, 40m, null, "quality", 4, "S3", book: 10),
            Handed("G", CombinedDay1, 30m, null, "improving", 5, "S4", book: 10),
            Handed("H", CombinedDay2, 100m, 99m, "pullback", 0, "S5"),
            Handed("I", CombinedDay3, 50m, 49m, "drift", 2, "S6"),
        ];
        var buys = trades.GroupBy(one => one.Trade.Ticker, StringComparer.Ordinal).ToDictionary(group => group.Key, group => group.First().Trade.Buy, StringComparer.Ordinal);
        var walked = PaperAccount.Combined(account, trades, [CombinedDay1, CombinedDay2, CombinedDay3], (ticker, _) => buys[ticker]);

        Assert.Equal(
            ["pullback|A|20|", $"breakout|A|0|{PaperAccount.AlreadyHeld}", "breakout|B|40|", "drift|C|100|", $"drift|D|0|{PaperAccount.SectorFull}", "dip|E|80|", "quality|F|5|", "improving|G|6|", $"pullback|H|0|{PaperAccount.UnderTheCash}", "drift|I|40|"],
            walked.Trades.Select(one => $"{one.From.Family}|{one.Taken.Trade.Ticker}|{one.Taken.Shares}|{one.Taken.Skipped}"));
        Assert.Equal((1, 1, 1), (walked.AlreadyHeld, walked.SectorFull, walked.UnderCash));
        Assert.Equal((7, 3), (walked.Walk.Taken, walked.Walk.Skipped));

        // Marked at their buys, the account stands at $10,000 on the first two sessions and at $10,200 once A is sold.
        Assert.Equal([10_000m, 10_000m, 10_200m], walked.Walk.Values.Select(one => one.Value));
        Assert.Equal(0.02, walked.Walk.Return!.Value, 12);
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
                new("pullback", "A", CombinedDay1, 100m, 20, null, null, 0.03, true),
                new("breakout", "A", CombinedDay1, 100m, 0, PaperAccount.AlreadyHeld, null, null, true),
            ]);
        var marks = new MarkRenderer();
        var tiles = WebUtility.HtmlDecode(marks.PaperTiles([], null, combined));
        var chart = WebUtility.HtmlDecode(marks.PaperChart([], null, combined));
        var card = WebUtility.HtmlDecode(marks.PaperCombined(combined));

        // The tile: its return on the size, +0.6% with A up 3 per cent on its $2,000, what it holds and what it skipped.
        Assert.Contains("<div class=\"paper-tile combined\" data-standing=\"combined\"", tiles, StringComparison.Ordinal);
        Assert.Contains("data-skipped=\"1\"", tiles, StringComparison.Ordinal);
        Assert.Contains(">All setups together<", tiles, StringComparison.Ordinal);
        Assert.Contains("+0.6%", tiles, StringComparison.Ordinal);
        Assert.Contains("1 open · 0 closed · 1 skipped · since", tiles, StringComparison.Ordinal);

        // Its line in the chart, keyed by its own name.
        Assert.Contains("data-standing=\"combined\"", chart, StringComparison.Ordinal);
        Assert.Contains("</span>All setups together</button>", chart, StringComparison.Ordinal);

        // The card: one sentence counting what it skipped and why, and each trade with the setup it came from.
        Assert.Contains("It skipped 1 pick(s): 1 because another setup already held the stock, 0 because three positions in the stock's sector were already open, and 0 because the free cash could not cover the buy.", card, StringComparison.Ordinal);
        Assert.Contains($"data-family=\"breakout\" data-ticker=\"A\" data-bought=\"2026-11-02\" data-open=\"true\" data-skipped=\"{PaperAccount.AlreadyHeld}\"", card, StringComparison.Ordinal);
        Assert.Contains($"skipped: {PaperAccount.AlreadyHeld}", card, StringComparison.Ordinal);
        Assert.Contains(">20</td>", card, StringComparison.Ordinal);
        Assert.Contains("+3.0%", card, StringComparison.Ordinal);

        // No combined account to draw before any live rule has a trade.
        Assert.Contains("data-combined=\"none\"", marks.PaperCombined(null), StringComparison.Ordinal);
    }
}
