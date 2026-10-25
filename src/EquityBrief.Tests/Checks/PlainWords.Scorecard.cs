using EquityBrief.Api.Reading;
using EquityBrief.Core.Ledger;
using EquityBrief.Web.App;
using EquityBrief.Web.Marks;
using static EquityBrief.Tests.Reading.ReadSurface;

namespace EquityBrief.Tests.Checks;

// plain-words, 22.8: the fundamentals card speaks in the reader's words in every state it draws, as the chart page draws
// it and as the full page does.
// see: A redesigned page speaks in the reader's words: every card opens on a sentence from its numbers, and no internal term reaches the screen outside the glossary
public partial class PlainWords
{
    // The card drawn as the chart page draws it and as the full page does, over a constructed scorecard.
    static IEnumerable<string> ScorecardCards(FundamentalsView view)
    {
        var drawn = new MarkRenderer().Fundamentals("XYZ", view);

        yield return Cards.Computed("Fundamentals", drawn, title: "XYZ's fundamentals", stamp: Cards.Night(new DateOnly(2026, 10, 23)), id: "fundamentals", region: "chart-page-fundamentals");
        yield return Cards.Computed("Fundamentals", drawn, title: "Nine checks of its filings as first filed", stamp: Cards.Night(new DateOnly(2026, 10, 23)), id: "fundamentals", region: "fundamentals");
    }

    // Strong, mixed, weak and with no word; every check passed, failed, not filed for each line it names and not applying
    // to a financial company; a newer quarter awaited; no quarter's filing and no scorecard stored; and the lines not scored
    // with their figures and with nothing stored.
    [Fact]
    public void TheFundamentalsCardSpeaksInTheReadersWordsInEveryStateItDraws()
    {
        var terms = Terms();
        var eight = EightOfNine();
        string[] lines =
        [
            FundamentalScorecard.NetIncomeLine, FundamentalScorecard.CashLine, FundamentalScorecard.AssetsLine, FundamentalScorecard.DebtLine,
            FundamentalScorecard.CurrentAssetsLine, FundamentalScorecard.CurrentLiabilitiesLine, FundamentalScorecard.SharesLine,
            FundamentalScorecard.SalesLine, FundamentalScorecard.GrossProfitLine,
        ];

        var rows = new List<FundamentalScoreRow?>
        {
            ScoreRowOf(eight),
            ScoreRowOf([.. eight.Select(check => check.Number <= 3 ? check with { Answer = CheckAnswer.Failed } : check)]),
            ScoreRowOf(eight, awaited: new DateOnly(2026, 9, 30)),
            ScoreRowOf([.. eight.Select(check => check.Number is 6 or 8 ? new ScorecardCheck(check.Number, CheckAnswer.NotApplying) : check)], financial: true),
            ScoreRowOf([.. eight.Select(check => check with { Answer = CheckAnswer.Failed, Amount = check.Amount is { } amount ? -amount : null })]),
            ScoreRowOf([.. eight.Take(4)]),
            ScoreRowOf([], unread: true),
            null,
        };

        // Each line a check names where it is not filed, on each check in turn.
        foreach (var line in lines)
        {
            rows.Add(ScoreRowOf([.. eight.Select(check => check.Number == 5 ? new ScorecardCheck(5, CheckAnswer.NotFiled, Missing: line) : check)]));
        }

        var views = rows
            .SelectMany(row => new[]
            {
                FundamentalsScreen.Of(row, "S&P 500", ScoreReadings(), ScoreAnalysts(), ScoreInsiders(250_000m)),
                FundamentalsScreen.Of(row, null, null, null, null),
            })
            .ToArray();

        Assert.All(views.SelectMany(ScorecardCards), card => Assert.Empty(FaultsOn(FundamentalsPage, card, terms)));
        Assert.True(views.Length >= 30, $"Drew {views.Length} card(s), expected one a state at least.");

        // The brief read's line in each state speaks in the reader's words too.
        Assert.All(rows, row => Assert.DoesNotContain(terms, term => Pattern(term).IsMatch(FundamentalsScreen.Line(row).Words)));
    }

    // The check fails a term planted in the card and a card not opening on its summary sentence.
    [Fact]
    public void TheCheckFailsATermPlantedInTheFundamentalsCardAndACardNotOpeningOnItsSummary()
    {
        var terms = Terms();
        var card = ScorecardCards(FundamentalsScreen.Of(ScoreRowOf(EightOfNine()), "S&P 500", ScoreReadings(), ScoreAnalysts(), ScoreInsiders(0m))).First();
        const string Summary = "Strong: 8 of 9 checks, on its filings to 2026-06-30.";

        Assert.Contains(Summary, card, StringComparison.Ordinal);
        Assert.Empty(FaultsOn(FundamentalsPage, card, terms));
        Assert.Contains(
            FaultsOn(FundamentalsPage, card.Replace(Summary, "Strong: 8 of 9 checks over the last four sessions.", StringComparison.Ordinal), terms),
            fault => fault.StartsWith("'session'", StringComparison.Ordinal));
        Assert.Contains(
            FaultsOn(FundamentalsPage, card.Replace("<div class=\"fundamentals\"", "<p>Before it</p><div class=\"fundamentals\"", StringComparison.Ordinal), terms),
            fault => fault.Contains("before its summary sentence", StringComparison.Ordinal));
    }
}
