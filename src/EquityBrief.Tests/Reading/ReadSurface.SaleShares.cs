using EquityBrief.Core.Loop;
using EquityBrief.Web.Marks;

namespace EquityBrief.Tests.Reading;

// read-surface, 23.10: on the operator's answer of 2026-11-17 on item 5 a plan staging its sales with a take-profit on half
// draws one set of sales, each part naming its share of the whole holding in the order its price is reached, and the shares
// a card's sale rows name add up to the whole holding and never more.
// see: A pick's card draws the sale its traded plan was registered with, worked from the price the plan buys at
public partial class ReadSurface
{
    static readonly DateOnly SharesNight = new(2026, 11, 16);

    // KTOS's order on the S&P 400 as stored: bought at 31.4114 with its stop two typical days of 2.51595 under, staged sales at
    // 44.1095 and 45.47, and from the rules restart of 15 Nov a take-profit on half at 3 times the risk, 31.4114 + 3 x 5.0319.
    static WaitingOrderView Kratos() => Staged("KTOS", 31.4114m, 26.3795m, [44.1095m, 45.47m], half: true, added: new DateOnly(2026, 11, 15));

    static WaitingOrderView Staged(string ticker, decimal buy, decimal stop, decimal[] stages, bool half, DateOnly? added = null, bool taking = true) =>
        new("drift", ticker, new DateOnly(2026, 11, 10), buy, PlanOrderWords.LastDay(new DateOnly(2026, 11, 10), 20), ticker + " Inc.", buy * 1.2m)
        {
            Stop = stop,
            Sale = new PlanSaleView(PlanPoints.StagedSales, buy, stop, null, stages, null, null, 60)
            {
                TakeProfit = taking ? new TakeProfitView(buy + (3m * (buy - stop)), TakeProfitSource.Risk, Multiple: 3, Buy: buy, Stop: stop) { Half = half, AddedOn = added } : null,
            },
        };

    [Fact]
    public void AStagedSaleTakingProfitOnHalfDrawsEachPartsShareOfTheWholeHoldingInTheOrderItsPriceIsReached()
    {
        var rows = MarkRenderer.WaitingPlanRows(Kratos());
        IReadOnlyList<(string Key, string Label, string Value, string? Note)> expected =
            [
                ("buy", "Buy if it dips to", "$31.41", null),
                ("stop", "Sell if it closes below", "$26.38", null),
                ("stage", "Sell a third at", "$44.11", null),
                ("stage", "Sell a third at", "$45.47", null),
                ("target", "Take profit on a sixth at", "$46.51", "Half of what is left. 3 times the risk: $15.10 up for $5.03 down. Take-profit added at the rules restart of 15 Nov 2026."),
                ("last", "Sell the last sixth", "after 60 trading days", "Its stop moves up to the buy, $31.41, once the sixth at $46.51 sells."),
            ];

        Assert.Equal(expected, rows);

        var html = MarkRenderer.WaitingCarousel([Kratos()], "S&P 400", SharesNight);

        Assert.Contains("<div class=\"home-prices wo-plan\" data-row=\"plan\"><p class=\"sale-parts\" data-sale-parts>Sells in 4 parts as the price rises.</p>", html, StringComparison.Ordinal);
        Assert.Equal(
            [
                "Sell at the next trading day's open after a day that closes below 26.38.",
                "Sell a third at the next trading day's open after a day that closes at or above 44.11.",
                "Sell a third at the next trading day's open after a day that closes at or above 45.47.",
                "Sell a sixth, half of what is left, at the next trading day's open after a day that closes at or above 46.51, the stop on the rest then moving up to the buy, 31.41.",
                "Keep the last sixth until a day that closes below the stop, at the buy once the sixth at 46.51 sells, or the time limit.",
                "Otherwise sell at the close 60 trading days after the buy.",
            ],
            Kratos().Sale!.Steps());
    }

    [Fact]
    public void TheSharesEveryCardsSaleRowsNameAddUpToTheWholeHoldingNeverMore()
    {
        // The four the store held on 17 Nov, each worked by hand: CNH a quarter at each of three levels, an eighth at the half and
        // the last eighth; AAON half at its one level, a quarter at the half and the last quarter; and three shapes beside them:
        // the half's price under both levels, half first, then a third and the last sixth at the levels with nothing left; the
        // staged sales alone; and the staged sales with a whole take-profit on the last third.
        WaitingOrderView[] orders =
        [
            Kratos(),
            Staged("CNH", 10.99m, 9.786m, [12.41m, 13.28m, 13.68m], half: true, added: new DateOnly(2026, 11, 15)),
            Staged("MAT", 14.19m, 12.924m, [16.43m, 16.73m], half: true, added: new DateOnly(2026, 11, 15)),
            Staged("AAON", 64.58m, 52.366m, [89.31m], half: true),
            Staged("HALF", 40m, 38m, [50m, 52m], half: true),
            Staged("STAGED", 40m, 38m, [50m, 52m], half: false, taking: false),
            Staged("WHOLE", 40m, 38m, [41m, 42m], half: false),
        ];
        var html = MarkRenderer.WaitingCarousel(orders, "S&P 400", SharesNight);

        Assert.Empty(MarkRenderer.CardsSellingOtherThanTheWhole(html));

        string Labels(string ticker) => string.Join(" | ", MarkRenderer.WaitingPlanRows(orders.Single(order => order.Ticker == ticker)).Where(row => row.Key is not ("buy" or "stop")).Select(row => row.Label + " " + row.Value));

        Assert.Equal("Sell a quarter at $12.41 | Sell a quarter at $13.28 | Sell a quarter at $13.68 | Take profit on an eighth at $14.60 | Sell the last eighth after 60 trading days", Labels("CNH"));
        Assert.Equal("Sell half at $89.31 | Take profit on a quarter at $101.22 | Sell the last quarter after 60 trading days", Labels("AAON"));
        Assert.Equal("Take profit on half at $46.00 | Sell a third at $50.00 | Sell a sixth at $52.00 | Otherwise sell after 60 trading days", Labels("HALF"));

        foreach (var order in orders)
        {
            Assert.Equal(1m, MarkRenderer.SharesNamed(MarkRenderer.WaitingPlanRows(order).Select(row => (row.Label, row.Note))));
        }
    }

    [Fact]
    public void TheGuardFindsACardWhoseSaleRowsNameTwoPlansShares()
    {
        // KTOS as the card drew it on 17 Nov: a third, a third, half and the other half, which is more than the whole.
        const string drawn = "<article class=\"wo-card fam-edge\" data-family=\"drift\" data-ticker=\"KTOS\"><div class=\"home-prices wo-plan\">"
            + "<div data-plan=\"stage\"><span>Sell a third at</span><b class=\"aim\">$44.11</b></div>"
            + "<div data-plan=\"stage\"><span>Sell a third at</span><b class=\"aim\">$45.47</b></div>"
            + "<div data-plan=\"target\"><span>Take profit on half at</span><b class=\"aim\">$46.51</b></div>"
            + "<div data-plan=\"other-half\"><span>Sell the other half</span><b class=\"aim\">after 60 trading days</b></div></div></article>";

        Assert.Equal(["KTOS"], MarkRenderer.CardsSellingOtherThanTheWhole(drawn));
    }
}
