using System.Text.RegularExpressions;
using EquityBrief.Api.Reading;
using EquityBrief.Core.Insiders;
using EquityBrief.Core.Ledger;
using EquityBrief.Core.Report;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.Marks;

namespace EquityBrief.Tests.Reading;

// read-surface, 22.8: the fundamentals card beneath the brief read on the chart page and in the full page's business
// section, each sentence worked by hand over a constructed scorecard: the word and the count, each check ticked or crossed
// with its figures, a check not read naming its line, a financial company's two not applying, a newer quarter reported and
// not yet filed, the four lines not scored, and each figure the store does not hold said so in its line.
// see: The fundamentals scorecard scores every member on nine checks of its filings as first filed, and only the quality family's rule reads it
public partial class ReadSurface
{
    // The rows 22.8 adds that this check reaches: section 15.22's card and section 15.9's.
    internal static string[] FundamentalsRows =>
    [
        CheckReach.Key("15.22 Chart", "Fundamentals"),
        CheckReach.Key("15.9 Name", "Fundamentals"),
    ];

    // Eight of nine: net income of $4.2B and cash from operations of $5.1B over four quarters, return on assets 8.4% against
    // 7.1%, long-term debt 21.2% of assets against 24.0%, the current ratio 1.42 against 1.50 and failing, shares 1.2% more
    // than a year before, the gross margin 48.3% against 46.1% and sales 0.67 times assets against 0.60.
    internal static IReadOnlyList<ScorecardCheck> EightOfNine() =>
    [
        new(1, CheckAnswer.Passed, Amount: 4_200_000_000m),
        new(2, CheckAnswer.Passed, Amount: 5_100_000_000m),
        new(3, CheckAnswer.Passed, Now: 0.084, YearBefore: 0.071),
        new(4, CheckAnswer.Passed, Amount: 5_100_000_000m, Against: 4_200_000_000m),
        new(5, CheckAnswer.Passed, Now: 0.212, YearBefore: 0.24),
        new(6, CheckAnswer.Failed, Now: 1.42, YearBefore: 1.5),
        new(7, CheckAnswer.Passed, Amount: 1_012_000_000m, Against: 1_000_000_000m, Now: 0.012),
        new(8, CheckAnswer.Passed, Now: 0.483, YearBefore: 0.461),
        new(9, CheckAnswer.Passed, Now: 2.0 / 3, YearBefore: 0.6),
    ];

    // A stored scorecard over the checks given, its count read off them as the scorecard counts them: gross profit 29% of
    // its assets, second of five, 20 times its earnings and its industry's median 25.
    internal static FundamentalScoreRow ScoreRowOf(
        IReadOnlyList<ScorecardCheck> checks,
        string ticker = "XYZ",
        DateOnly? awaited = null,
        bool financial = false,
        bool unread = false)
    {
        var passed = checks.Count(check => check.Answer == CheckAnswer.Passed);
        var applied = checks.Count(check => check.Answer is CheckAnswer.Passed or CheckAnswer.Failed);

        return new FundamentalScoreRow(
            "GSPC",
            BriefNight,
            ticker,
            unread ? null : new DateOnly(2026, 6, 30),
            unread ? null : new DateOnly(2025, 6, 30),
            awaited,
            financial,
            passed,
            applied,
            FundamentalScorecard.WordOf(passed, applied) is { } word ? ScorecardText.WordOf(word) : null,
            checks,
            unread ? null : 0.29,
            unread ? null : 2,
            unread ? null : 5,
            unread ? null : 20.0,
            "Machinery",
            unread ? null : 25.0);
    }

    // The stock's own readings the ledger stored: sales up 12% on a year, 3 points faster than the quarter before, and its
    // earnings over its value above half of its own last twelve quarters'.
    internal static IReadOnlyList<double?> ScoreReadings()
    {
        var readings = new double?[LedgerReadings.Count];

        readings[LedgerReadings.IndexOf("revenue_growth")] = 0.12;
        readings[LedgerReadings.IndexOf("growth_change")] = 0.03;
        readings[LedgerReadings.IndexOf("value_place")] = 0.5;

        return readings;
    }

    internal static AnalystReading ScoreAnalysts() =>
        new(new DateOnly(2026, 10, 20), [], [new RevisionRow("0y", 1, 0, 5, 2, 1.5)], new Dictionary<string, IReadOnlyList<TrendPoint>>(), [], 2.0, 120m, 20.0);

    internal static InsiderView ScoreInsiders(decimal sold) =>
        new([], new InsiderTotals(0m, sold, 0, sold > 0 ? 1 : 0), new InsiderTotals(0m, sold, 0, sold > 0 ? 1 : 0), 0, 0, 0, 0, null);

    [Fact]
    public void EachSentenceOfTheFundamentalsCardIsWrittenFromItsStoredFigureAsWorkedByHand()
    {
        var view = FundamentalsScreen.Of(ScoreRowOf(EightOfNine()), "S&P 500", ScoreReadings(), ScoreAnalysts(), ScoreInsiders(250_000m));

        Assert.Equal("Strong: 8 of 9 checks, on its filings to 2026-06-30.", view.Summary);
        Assert.Equal("strong", view.Word);
        Assert.Equal(
            [
                "passed|Net income over the last four quarters, $4.2B, was above zero.",
                "passed|Cash from operations over the last four quarters, $5.1B, was above zero.",
                "passed|Return on assets was 8.4%, up from a year ago's 7.1%.",
                "passed|Cash from operations, $5.1B, was above net income, $4.2B.",
                "passed|Long-term debt was 21.2% of its average assets, down from a year ago's 24.0%.",
                "failed|Current assets were 1.42 times current liabilities, down from a year ago's 1.50 times.",
                "passed|Shares outstanding rose 1.2% over the year, within the 2.0% allowed.",
                "passed|Gross margin was 48.3%, up from a year ago's 46.1%.",
                "passed|Sales were 0.67 times assets, up from a year ago's 0.60 times.",
            ],
            view.Checks.Select(check => $"{check.Answer}|{check.Words}"));
        Assert.Null(view.Awaited);

        // The four lines not scored: sales off the ledger's readings, the place on gross profit among the index's members, the
        // multiple against its own last twelve quarters and its industry's median, and the estimates with the insiders.
        Assert.Equal(
            [
                "Sales grew 12.0% on a year in the newest quarter, faster than in the quarter before.",
                "Gross profit was 29.0% of its assets, 2nd of the 5 S&P 500 stocks with the figure.",
                "It trades at 20.0 times its last four quarters' earnings, cheaper than in 6 of its own last 12 quarters; its industry's median on the S&P 500 is 25.0.",
                "Over 30 days 5 analysts raised and 2 cut their estimate for the year; over 90 days insiders bought $0 and sold $250,000 in the open market.",
            ],
            view.Context.Select(line => line.Words));

        // The brief read's line in its business panel.
        Assert.Equal("Fundamentals: strong, 8 of 9 checks on its filings to 2026-06-30, in the card below.", FundamentalsScreen.Line(ScoreRowOf(EightOfNine())).Words);
    }

    [Fact]
    public void EachCheckFailingAndEachShareMoveIsSaidInItsOwnWords()
    {
        IReadOnlyList<ScorecardCheck> failing =
        [
            new(1, CheckAnswer.Failed, Amount: -50_000_000m),
            new(2, CheckAnswer.Failed, Amount: -20_000_000m),
            new(3, CheckAnswer.Failed, Now: 0.05, YearBefore: 0.05),
            new(4, CheckAnswer.Failed, Amount: -20_000_000m, Against: -50_000_000m),
            new(5, CheckAnswer.Failed, Now: 0.3, YearBefore: 0.25),
            new(6, CheckAnswer.Failed, Now: 1.1, YearBefore: 1.2),
            new(7, CheckAnswer.Failed, Amount: 105m, Against: 100m, Now: 0.05),
            new(8, CheckAnswer.Failed, Now: 0.3, YearBefore: 0.31),
            new(9, CheckAnswer.Failed, Now: 0.5, YearBefore: 0.55),
        ];
        var view = FundamentalsScreen.Of(ScoreRowOf(failing), "S&P 400", null, null, null);

        Assert.Equal("Weak: 0 of 9 checks, on its filings to 2026-06-30.", view.Summary);
        Assert.Equal(
            [
                "Net income over the last four quarters was a loss of $50.0M.",
                "Cash from operations over the last four quarters was -$20.0M, not above zero.",
                "Return on assets was 5.0%, the same as a year ago's 5.0%.",
                "Cash from operations, -$20.0M, was not above net income, -$50.0M.",
                "Long-term debt was 30.0% of its average assets, up from a year ago's 25.0%.",
                "Current assets were 1.10 times current liabilities, down from a year ago's 1.20 times.",
                "Shares outstanding rose 5.0% over the year, more than the 2.0% allowed.",
                "Gross margin was 30.0%, down from a year ago's 31.0%.",
                "Sales were 0.50 times assets, down from a year ago's 0.55 times.",
            ],
            view.Checks.Select(check => check.Words));

        // Fewer shares pass, and none moved passes too.
        Assert.Equal("Shares outstanding fell 3.0% over the year, within the 2.0% allowed.", FundamentalsScreen.Of(ScoreRowOf([new(7, CheckAnswer.Passed, Now: -0.03)]), null, null, null, null).Checks[0].Words);
        Assert.Equal("Shares outstanding were unchanged over the year, within the 2.0% allowed.", FundamentalsScreen.Of(ScoreRowOf([new(7, CheckAnswer.Passed, Now: 0.0)]), null, null, null, null).Checks[0].Words);
    }

    [Fact]
    public void ACheckNotReadAFinancialCompanyAndANewerQuarterEachSaySo()
    {
        // A financial company: the current ratio and the gross margin not applying, seven of seven.
        var financial = EightOfNine()
            .Select(check => check.Number is 6 or 8 ? new ScorecardCheck(check.Number, CheckAnswer.NotApplying) : check)
            .ToArray();
        var bank = FundamentalsScreen.Of(ScoreRowOf(financial, financial: true), "S&P 500", null, null, null);

        Assert.Equal("Strong: 7 of 7 checks, on its filings to 2026-06-30.", bank.Summary);
        Assert.Equal("Current assets against current liabilities: not scored, as it does not apply to a financial company.", bank.Checks[5].Words);
        Assert.Equal("Gross margin: not scored, as it does not apply to a financial company.", bank.Checks[7].Words);
        Assert.Equal("not applying", bank.Checks[5].Answer);

        // A line not filed names the line.
        var unfiled = EightOfNine()
            .Select(check => check.Number == 5 ? new ScorecardCheck(5, CheckAnswer.NotFiled, Missing: FundamentalScorecard.DebtLine) : check)
            .ToArray();

        Assert.Equal("Long-term debt against assets: not scored, as its long-term debt is not stored for the periods it reads.", FundamentalsScreen.Of(ScoreRowOf(unfiled), null, null, null, null).Checks[4].Words);

        // None read at all says so in its own words.
        Assert.Equal(
            "Not scored, as none of its checks could be read from its filings to 2026-06-30.",
            FundamentalsScreen.Of(ScoreRowOf([new(1, CheckAnswer.NotFiled, Missing: FundamentalScorecard.NetIncomeLine)]), null, null, null, null).Summary);

        // Four read give no word, and say how many could be read.
        IReadOnlyList<ScorecardCheck> four =
        [
            new(1, CheckAnswer.Passed, Amount: 1m),
            new(2, CheckAnswer.Passed, Amount: 2m),
            new(3, CheckAnswer.Passed, Now: 0.1, YearBefore: 0.05),
            new(4, CheckAnswer.Passed, Amount: 2m, Against: 1m),
            new(9, CheckAnswer.NotFiled, Missing: FundamentalScorecard.SalesLine),
        ];
        var unworded = FundamentalsScreen.Of(ScoreRowOf(four), null, null, null, null);

        Assert.Equal("Not scored, as only 4 of its checks could be read from its filings to 2026-06-30.", unworded.Summary);
        Assert.Null(unworded.Word);
        Assert.Equal("Fundamentals: not scored, as only 4 of its checks could be read from its filings to 2026-06-30.", FundamentalsScreen.Line(ScoreRowOf(four)).Words);

        // A newer quarter reported and not yet filed is named beside the quarter the checks read.
        Assert.Equal(
            "Its quarter to 2026-09-30 has been reported, but its filing is not stored yet; these checks read the quarter to 2026-06-30.",
            FundamentalsScreen.Of(ScoreRowOf(EightOfNine(), awaited: new DateOnly(2026, 9, 30)), null, null, null, null).Awaited);

        // No quarter's filing stored, and no scorecard stored at all.
        Assert.Equal("Not scored, as none of its quarters' filings is stored.", FundamentalsScreen.Of(ScoreRowOf([], unread: true), null, null, null, null).Summary);
        Assert.Equal("Not scored, as no scorecard is stored for it on the night.", FundamentalsScreen.Of(null, null, null, null, null).Summary);
        Assert.Equal("Fundamentals: not scored, as no scorecard is stored for it on the night.", FundamentalsScreen.Line(null).Words);
    }

    [Fact]
    public void ALineNotScoredWithNothingStoredSaysSo()
    {
        var bare = FundamentalsScreen.Of(ScoreRowOf(EightOfNine(), unread: true), "S&P 500", null, null, null);

        Assert.Equal(
            [
                "Sales against a year before: not read from its filings.",
                "Gross profit against assets: not read from its filings.",
                "Price against earnings: not read, as it earned nothing over its last four quarters or no share count is filed.",
                "The analysts' changes to their estimates over 30 days are not stored; insiders' open-market trades over 90 days are not read.",
            ],
            bare.Context.Select(line => line.Words));

        // A multiple with no place of its own and no industry's median, and insiders with no trade.
        var partly = FundamentalsScreen.Of(ScoreRowOf(EightOfNine()) with { IndustryMultiple = null }, "S&P 500", new double?[LedgerReadings.Count], null, ScoreInsiders(0m));

        Assert.Equal("It trades at 20.0 times its last four quarters' earnings; its industry's median is not read.", partly.Context[2].Words);
        Assert.Equal("The analysts' changes to their estimates over 30 days are not stored; insiders made no open-market trade over 90 days.", partly.Context[3].Words);
        Assert.Equal("Sales against a year before: not read from its filings.", partly.Context[0].Words);
    }

    // A card read back off a rendered page: its summary, each check's answer and sentence, the line naming a newer quarter
    // and each line not scored.
    static (string Summary, IReadOnlyList<string> Checks, string? Awaited, IReadOnlyDictionary<string, string> Context) FundamentalsOfPage(string page, string region)
    {
        var card = Assert.Single(Blocks(page, $"<section class=\"card\"[^>]*data-card=\"{region}\".*?</section>"));

        return (
            Regex.Match(card, "<p class=\"fundamentals-summary\" data-summary>(.*?)</p>").Groups[1].Value,
            [.. Regex.Matches(card, "<li data-check=\"(\\d)\" data-answer=\"([^\"]+)\"><span[^>]*>[^<]*</span><span>(.*?)</span></li>").Select(match => $"{match.Groups[1].Value}|{match.Groups[2].Value}|{match.Groups[3].Value}")],
            Regex.Match(card, "<p class=\"fundamentals-awaited\">(.*?)</p>") is { Success: true } awaited ? awaited.Groups[1].Value : null,
            Regex.Matches(card, "<li data-line=\"([^\"]+)\"[^>]*>(.*?)</li>").ToDictionary(match => match.Groups[1].Value, match => match.Groups[2].Value, StringComparer.Ordinal));
    }

    // Over the night the family pages are read on: F1's scorecard stored on the night, eight of nine with its quarter to
    // 2026-09-30 reported and not yet filed, and N1 with none stored. The chart page draws the card beneath the brief read,
    // whose business panel opens on its line, and the full page draws the same card after the margins.
    [Fact]
    public async Task TheChartPageDrawsTheCardBeneathTheBriefAndTheFullPageDrawsItInItsBusinessSection()
    {
        using var store = await FamilyPagesStore();
        var checks = EightOfNine();

        store.Execute(
            "INSERT INTO fundamental_score (index_code, session_date, ticker, quarter, year_before, awaited, financial, passed, applied, word, checks, gross_profitability, gross_rank, gross_ranked, multiple, industry, industry_multiple) VALUES " +
            $"('GSPC', '{TheSwitch}', 'F1', '2026-06-30', '2025-06-30', '2026-09-30', 0, 8, 9, 'strong', '{ScorecardText.Of(checks)}', 0.29, 2, 5, 20.0, 'Machinery', 25.0);");

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var chart = System.Net.WebUtility.HtmlDecode(await client.GetStringAsync("/screens/chart/F1"));
        var f1 = FundamentalsOfPage(chart, "chart-page-fundamentals");
        var expected = FundamentalsScreen.Of(ScoreRowOf(checks, "F1"), "S&P 500", null, null, null);

        Assert.True(chart.IndexOf("data-card=\"chart-page-brief\"", StringComparison.Ordinal) < chart.IndexOf("data-card=\"chart-page-fundamentals\"", StringComparison.Ordinal));
        Assert.Equal("Strong: 8 of 9 checks, on its filings to 2026-06-30.", f1.Summary);
        Assert.Equal(expected.Checks.Select(check => $"{check.Number}|{check.Answer}|{check.Words}"), f1.Checks);
        Assert.Equal("Its quarter to 2026-09-30 has been reported, but its filing is not stored yet; these checks read the quarter to 2026-06-30.", f1.Awaited);
        Assert.Equal(["sales", "gross", "price", "estimates"], f1.Context.Keys);
        Assert.Equal("Gross profit was 29.0% of its assets, 2nd of the 5 S&P 500 stocks with the figure.", f1.Context["gross"]);
        Assert.StartsWith("It trades at 20.0 times its last four quarters' earnings", f1.Context["price"], StringComparison.Ordinal);
        Assert.EndsWith("; its industry's median on the S&P 500 is 25.0.", f1.Context["price"], StringComparison.Ordinal);
        Assert.Equal("Fundamentals: strong, 8 of 9 checks on its filings to 2026-06-30, in the card below.", BriefOfPage(chart).Lines["fundamentals"]);

        // The full page draws the same card, its region in the business section after the margins.
        var name = System.Net.WebUtility.HtmlDecode(await client.GetStringAsync("/screens/name/F1"));
        var onName = FundamentalsOfPage(name, "fundamentals");

        Assert.Equal(f1.Summary, onName.Summary);
        Assert.Equal(f1.Checks, onName.Checks);
        Assert.Equal(f1.Awaited, onName.Awaited);
        Assert.Equal(f1.Context["gross"], onName.Context["gross"]);
        Assert.Equal(Array.IndexOf(Web.App.SinglePageApp.RegionOrder, "margins") + 1, Array.IndexOf(Web.App.SinglePageApp.RegionOrder, "fundamentals"));

        // N1, no scorecard stored: both pages say so.
        var n1 = FundamentalsOfPage(System.Net.WebUtility.HtmlDecode(await client.GetStringAsync("/screens/chart/N1")), "chart-page-fundamentals");

        Assert.Equal("Not scored, as no scorecard is stored for it on the night.", n1.Summary);
        Assert.Empty(n1.Checks);
        Assert.Equal("Not scored, as no scorecard is stored for it on the night.", FundamentalsOfPage(System.Net.WebUtility.HtmlDecode(await client.GetStringAsync("/screens/name/N1")), "fundamentals").Summary);
    }

    // The card's lines wrap inside it at a phone's width, and no rule of it gives it a width of its own.
    [Fact]
    public async Task TheCardsLinesWrapAndNothingInItSetsAWidth()
    {
        using var store = new TemporaryStore().Migrated();
        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var shell = await client.GetStringAsync("/");
        var rules = Regex.Matches(shell, @"\.fundamentals-[a-z-]+[^{]*\{[^}]*\}").Select(match => match.Value).ToArray();

        Assert.Contains(".fundamentals-checks li{display:flex;gap:8px;margin:0 0 5px;overflow-wrap:anywhere}", shell, StringComparison.Ordinal);
        Assert.Contains(".fundamentals-context li{margin:0 0 4px;overflow-wrap:anywhere}", shell, StringComparison.Ordinal);
        Assert.NotEmpty(rules);
        Assert.All(rules, rule => Assert.DoesNotMatch(@"(?<![a-z-])width:|min-width:(?!0[;}])", rule));
        Assert.DoesNotContain("style=", new MarkRenderer().Fundamentals("XYZ", FundamentalsScreen.Of(ScoreRowOf(EightOfNine()), "S&P 500", ScoreReadings(), ScoreAnalysts(), ScoreInsiders(0m))), StringComparison.Ordinal);
    }
}
