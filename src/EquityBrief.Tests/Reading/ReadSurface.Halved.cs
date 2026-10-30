using System.Net;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.Marks;

namespace EquityBrief.Tests.Reading;

// read-surface, 21.9: a trade a rule standing at the graded exposure sized at half, drawn in its account's log with its
// mark and its row's flag, and a trade sized whole with neither.
// see: A breakout's or a drift's graded exposure stands in its market check's place as a variant, and a night it holds one switch sizes its trades at half
public partial class ReadSurface
{
    // The row of paper trading's table this check reaches from 21.9.
    internal static readonly string[] HalvedClaims =
    [
        CheckReach.Key("15.21 Paper trading", Scope.PaperHalvedTrade),
    ];

    [Fact]
    public void ATradeSizedAtHalfIsMarkedInItsAccountsLogAndOneSizedWholeIsNot()
    {
        var account = new PaperAccountView("graded", "drift", PaperAccountView.Variant, "V7", "the S&P 400 drift rule, graded", "not tested yet", null, "risks", [], null, 2, 0, null, new DateOnly(2026, 11, 2));
        PaperTradeView[] trades =
        [
            new("AAA", new DateOnly(2026, 11, 3), 20m, 18m, null, 0.4, true, null, Halved: true),
            new("BBB", new DateOnly(2026, 11, 4), 30m, 27m, null, 0.1, true, null),
        ];
        var log = WebUtility.HtmlDecode(new MarkRenderer().PaperLog(account, trades));

        string Row(string ticker)
        {
            var from = log.IndexOf($"data-ticker=\"{ticker}\"", StringComparison.Ordinal);

            return log[from..log.IndexOf("</tr>", from, StringComparison.Ordinal)];
        }

        Assert.Contains("data-halved=\"true\"", Row("AAA"), StringComparison.Ordinal);
        Assert.Contains(">half size</span>", Row("AAA"), StringComparison.Ordinal);
        Assert.DoesNotContain("data-halved", Row("BBB"), StringComparison.Ordinal);
        Assert.DoesNotContain("half size", Row("BBB"), StringComparison.Ordinal);
    }
}
