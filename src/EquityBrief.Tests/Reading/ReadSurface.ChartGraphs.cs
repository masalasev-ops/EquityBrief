using System.Globalization;
using System.Text.RegularExpressions;
using EquityBrief.Core.Report;
using EquityBrief.Tests.Checks;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.Marks;

namespace EquityBrief.Tests.Reading;

// read-surface, 20.12: the chart page's three graphs, each over a full input with its values whole on its elements and a
// title on every mark, and over the input it degrades on with the words it says in place of what it cannot draw; the
// prints and the quarters they read worked by hand; and the graphs read back off every fixture name's chart page against
// the store.
// see: Marks are defined once and every screen draws from that list
// see: The chart page graphs the report's earnings and revenue and the analysts' figures beneath its chart, and no rule reads them from it
public partial class ReadSurface
{
    // The rows 20.12 adds that this check reaches: section 15.5's three marks and section 15.22's graphs.
    internal static readonly string[] ChartGraphRows =
    [
        CheckReach.Key("15.5 The mark vocabulary", "Earnings dots"),
        CheckReach.Key("15.5 The mark vocabulary", "Income bars"),
        CheckReach.Key("15.5 The mark vocabulary", "Target marker"),
        CheckReach.Key("15.22 Chart", "The graphs"),
    ];

    // Three prints, a beat, a miss and one filing no estimate, with the quarter to report next.
    static readonly EarningsPrint[] ThreePrints =
    [
        new(new DateOnly(2026, 1, 29), 2.35m, 2.40m),
        new(new DateOnly(2026, 4, 30), 1.62m, 1.57m),
        new(new DateOnly(2026, 7, 31), null, 1.70m),
    ];

    [Fact]
    public void ThePrintsAreDrawnOldestFirstWithTheQuarterToReportNextOnlyWhereItEndsAfterTheNewest()
    {
        // The stored prints in any order are drawn oldest first, and the current quarter's consensus ending after the
        // newest print's report date is the quarter to report next, the next quarter's and the year's read for none.
        (string Period, DateOnly Ends, decimal? Eps)[] consensus =
        [
            ("0q", new DateOnly(2026, 9, 30), 1.66m),
            ("+1q", new DateOnly(2026, 12, 31), 2.10m),
            ("0y", new DateOnly(2027, 6, 30), 7.30m),
        ];
        var (drawn, next) = QuarterLines.Earnings([ThreePrints[2], ThreePrints[0], ThreePrints[1]], consensus);

        Assert.Equal([new DateOnly(2026, 1, 29), new DateOnly(2026, 4, 30), new DateOnly(2026, 7, 31)], drawn.Select(print => print.ReportDate));
        Assert.Equal((new DateOnly(2026, 9, 30), 1.66m), next);

        // A beat and a miss by the actual less the estimate, and none where no estimate was filed.
        Assert.Equal([0.05m, -0.05m, null], drawn.Select(print => print.Difference));

        // A fetch made before the newest print files the quarter just reported as its current one, which is no quarter to
        // report next; nor is one ending on the newest print's day, one filing no figure, or a quarter ending its fiscal
        // year, which the answer files under the year alone.
        Assert.Null(QuarterLines.Earnings(ThreePrints, [("0q", new DateOnly(2026, 6, 30), 1.60m)]).Next);
        Assert.Null(QuarterLines.Earnings(ThreePrints, [("0q", new DateOnly(2026, 7, 31), 1.60m)]).Next);
        Assert.Null(QuarterLines.Earnings(ThreePrints, [("0q", new DateOnly(2026, 9, 30), null)]).Next);
        Assert.Null(QuarterLines.Earnings(ThreePrints, [("0y", new DateOnly(2026, 9, 30), 7.30m)]).Next);
        Assert.Equal((new DateOnly(2026, 8, 1), 1.60m), QuarterLines.Earnings(ThreePrints, [("0q", new DateOnly(2026, 8, 1), 1.60m)]).Next);
    }

    [Fact]
    public void TheEarningsDotsDrawEachPrintAgainstItsEstimateAndTheQuarterToReportNextAndSayWhatTheyLack()
    {
        var marks = new MarkRenderer();
        var dots = marks.EarningsDots("MSFT", ThreePrints, (new DateOnly(2026, 9, 30), 1.66m));

        Assert.Contains("data-mark=\"earnings-dots\" data-prints=\"3\" data-next=\"2026-09-30\"", dots, StringComparison.Ordinal);

        // Each estimate a hollow dot whole with a title, none for the print filing none.
        Assert.Equal(
            ["2026-01-29 2.35 Reported 2026-01-29: the estimate before it 2.35", "2026-04-30 1.62 Reported 2026-04-30: the estimate before it 1.62"],
            Regex.Matches(dots, "<circle class=\"ed-estimate\"[^>]*data-report-date=\"([^\"]+)\" data-estimate=\"([^\"]+)\"><title>([^<]+)</title>")
                .Select(match => $"{match.Groups[1].Value} {match.Groups[2].Value} {match.Groups[3].Value}"));

        // Each reported figure filled by its beat or its miss, whole with its difference and a title saying it.
        Assert.Equal(
            [
                "up 2026-01-29 2.40 0.05 Reported 2026-01-29: earnings a share 2.40, beating the estimate of 2.35 by 0.05",
                "down 2026-04-30 1.57 -0.05 Reported 2026-04-30: earnings a share 1.57, missing the estimate of 1.62 by 0.05",
                "none 2026-07-31 1.70  Reported 2026-07-31: earnings a share 1.70, no estimate filed before it",
            ],
            Regex.Matches(dots, "<circle class=\"ed-actual ed-actual-([a-z]+)\"[^>]*data-report-date=\"([^\"]+)\" data-actual=\"([^\"]+)\"(?: data-difference=\"([^\"]+)\")?><title>([^<]+)</title>")
                .Select(match => string.Join(' ', match.Groups.Values.Skip(1).Select(group => group.Value))));

        // Beneath each column the beat or the miss with its sign in the rise or the fall hue, and a print filing no estimate
        // saying so.
        Assert.Equal(
            [("ed-result gain", "0.05", "Beat +0.05"), ("ed-result loss", "-0.05", "Missed -0.05"), ("m-tick", "none", "No estimate")],
            Regex.Matches(dots, "<text class=\"([^\"]+)\"[^>]*data-difference=\"([^\"]+)\">([^<]+)</text>").Select(match => (match.Groups[1].Value, match.Groups[2].Value, match.Groups[3].Value)));

        // The quarter to report next its consensus alone, dashed and named.
        Assert.Contains("data-ends=\"2026-09-30\" data-estimate=\"1.66\"><title>The quarter to 2026-09-30, not yet reported: the analysts' consensus 1.66</title>", dots, StringComparison.Ordinal);
        Assert.Contains(">Next</text>", dots, StringComparison.Ordinal);
        Assert.Contains(">Est. 1.66</text>", dots, StringComparison.Ordinal);

        // Worked by hand: four columns over the plot from 46 to 626, a slot of 145 each, so the middles are 118.5, 263.5,
        // 408.5 and 553.5; and the figures span 1.57 to 2.40, padded by the half unit the axis keeps at least, so 1.07 to
        // 2.90 over the 128 between 26 and 154: 2.40 at 60.97, 1.57 at 119.03, 1.70 at 109.93 and the consensus of 1.66 at
        // 112.73.
        Assert.Equal(
            ["118.5 60.97", "263.5 119.03", "408.5 109.93"],
            Regex.Matches(dots, "<circle class=\"ed-actual [^\"]+\" cx=\"([^\"]+)\" cy=\"([^\"]+)\"").Select(match => $"{match.Groups[1].Value} {match.Groups[2].Value}"));
        Assert.Contains("<circle class=\"ed-next\" cx=\"553.5\" cy=\"112.73\"", dots, StringComparison.Ordinal);

        // With no quarter to report next, three columns and no dashed dot.
        var past = marks.EarningsDots("MSFT", ThreePrints, null);

        Assert.Contains("data-prints=\"3\" data-next=\"none\"", past, StringComparison.Ordinal);
        Assert.DoesNotContain("<circle class=\"ed-next\"", past, StringComparison.Ordinal);
        Assert.Contains("<circle class=\"ed-actual ed-actual-none\" cx=\"529.33\"", past, StringComparison.Ordinal);

        // One print holding its reported figure says the dots need two.
        Assert.Equal(
            "<p class=\"mark-short\" data-mark=\"earnings-dots\" data-prints=\"1\">1 of the prints stored for MSFT hold the earnings a share reported, and the dots need two.</p>",
            marks.EarningsDots("MSFT", [new EarningsPrint(new DateOnly(2026, 4, 30), 1.62m, null), ThreePrints[2]], (new DateOnly(2026, 9, 30), 1.66m)));
    }

    [Fact]
    public void TheIncomeBarsDrawEachQuartersRevenueAndNetIncomeWithTheNetMarginAndSayWhatTheyLack()
    {
        // Thirteen quarters to 2026-06-30 given newest first: the twelve newest read oldest first, each with its net margin
        // on its revenue to six places of the fraction, a third reading 33.3333, and none where no net income is filed.
        static QuarterRow Quarter(DateOnly end, decimal? revenue, decimal? net) => new(end, null, revenue, null, null, net, null, null, null, null, null, null, null, true);

        var quarters = Enumerable.Range(0, 13)
            .Select(at => new DateOnly(2023, 4, 1).AddMonths(3 * at).AddMonths(3).AddDays(-1))
            .Select((end, at) => Quarter(end, 300_000_000m, at == 12 ? 100_000_000m : at == 11 ? -30_000_000m : at == 10 ? null : 60_000_000m))
            .Reverse()
            .ToArray();
        var income = QuarterLines.Income(quarters);

        Assert.Equal(12, income.Count);
        Assert.Equal(new DateOnly(2023, 9, 30), income[0].Quarter);
        Assert.Equal(new DateOnly(2026, 6, 30), income[^1].Quarter);
        Assert.Equal([60_000_000m, null, -30_000_000m, 100_000_000m], income.TakeLast(4).Select(quarter => quarter.NetIncome));
        Assert.Equal(["20", "none", "-10", "33.3333"], income.TakeLast(4).Select(quarter => quarter.Net is { } net ? net.ToString("0.######", CultureInfo.InvariantCulture) : "none"));

        var marks = new MarkRenderer();
        var bars = marks.IncomeBars("KEYS", [
            new QuarterIncome(new DateOnly(2025, 12, 31), 1_000_000_000m, 150_000_000m, 15.0),
            new QuarterIncome(new DateOnly(2026, 3, 31), 1_200_000_000m, -60_000_000m, -5.0),
            new QuarterIncome(new DateOnly(2026, 6, 30), 1_100_000_000m, null, null),
        ], "USD");

        Assert.Contains("data-mark=\"income-bars\" data-quarters=\"3\"", bars, StringComparison.Ordinal);

        // Each quarter's revenue a bar whole with a title in its scale.
        Assert.Equal(
            [
                ("ib-revenue", "revenue", "1000000000", "Quarter to 2025-12-31: revenue $1.0B"),
                ("ib-revenue", "revenue", "1200000000", "Quarter to 2026-03-31: revenue $1.2B"),
                ("ib-revenue", "revenue", "1100000000", "Quarter to 2026-06-30: revenue $1.1B"),
            ],
            Marked(bars, "rect").Where(mark => mark.Kind == "ib-revenue"));

        // Net income beside it in the rise or the fall hue by its sign, none where none is filed.
        Assert.Equal(
            [
                ("ib-income ib-income-up", "income", "150000000", "Quarter to 2025-12-31: net income $150.0M"),
                ("ib-income ib-income-down", "income", "-60000000", "Quarter to 2026-03-31: net income -$60.0M"),
            ],
            Marked(bars, "rect").Where(mark => mark.Kind.StartsWith("ib-income", StringComparison.Ordinal)));

        // The net margin a line over the quarters holding one, each point whole with a title.
        Assert.Equal(
            [("ib-margin-dot", "net", "15", "Quarter to 2025-12-31: net margin 15.0%"), ("ib-margin-dot", "net", "-5", "Quarter to 2026-03-31: net margin -5.0%")],
            Marked(bars, "circle"));
        Assert.Single(Regex.Matches(bars, "<polyline class=\"ib-margin\""));

        // A loss hangs from the zero rule and a profit stands on it.
        var zero = Regex.Match(bars, "<line class=\"m-axisline\" x1=\"[^\"]+\" x2=\"[^\"]+\" y1=\"([^\"]+)\"").Groups[1].Value;
        var profit = Regex.Match(bars, "<rect class=\"ib-income ib-income-up\" x=\"[^\"]+\" y=\"([^\"]+)\" width=\"[^\"]+\" height=\"([^\"]+)\"");
        var loss = Regex.Match(bars, "<rect class=\"ib-income ib-income-down\" x=\"[^\"]+\" y=\"([^\"]+)\"");

        Assert.Single(Regex.Matches(bars, "<line class=\"m-axisline\""));
        Assert.Equal(zero, loss.Groups[1].Value);
        Assert.Equal(
            double.Parse(zero, CultureInfo.InvariantCulture),
            double.Parse(profit.Groups[1].Value, CultureInfo.InvariantCulture) + double.Parse(profit.Groups[2].Value, CultureInfo.InvariantCulture),
            1);

        // One quarter holding a revenue says the bars need two; quarters filing no net income draw no margin and say so.
        Assert.Equal(
            "<p class=\"mark-short\" data-mark=\"income-bars\" data-quarters=\"1\">1 of the quarters stored for KEYS hold a revenue, and the bars need two.</p>",
            marks.IncomeBars("KEYS", [new QuarterIncome(new DateOnly(2026, 3, 31), null, 5m, null), new QuarterIncome(new DateOnly(2026, 6, 30), 1_100_000_000m, null, null)], "USD"));

        var unfiled = marks.IncomeBars("KEYS", [new QuarterIncome(new DateOnly(2026, 3, 31), 1_000_000_000m, null, null), new QuarterIncome(new DateOnly(2026, 6, 30), 1_100_000_000m, null, null)], "USD");

        Assert.EndsWith("</svg><p class=\"mark-note\" data-margin-absent=\"net\">No quarter of these files a net income, so no net margin is drawn.</p>", unfiled, StringComparison.Ordinal);
        Assert.DoesNotContain("<polyline", unfiled, StringComparison.Ordinal);
        Assert.DoesNotContain("<rect class=\"ib-income", unfiled, StringComparison.Ordinal);
    }

    [Fact]
    public void TheTargetMarkerSetsTheMeanTargetAgainstThePriceAndSaysWhatItLacks()
    {
        var marks = new MarkRenderer();
        RatingFetch[] fetches = [new(new DateOnly(2026, 9, 27), 9, 6, 9, 0, 1, 4.1, 180m)];

        // A mean target of 180 against a price of 150 stands 20 per cent above it, drawn with its sign in the rise hue.
        var above = marks.TargetMarker("CVX", AnalystView.Of(new DateOnly(2026, 9, 27), [], fetches, ReportNight, 150m), 150m);

        Assert.Contains("data-mark=\"target-marker\" data-target=\"180\" data-price=\"150\"", above, StringComparison.Ordinal);
        Assert.Contains("<title>The price drawn: 150.00</title>", above, StringComparison.Ordinal);
        Assert.Contains(">Price 150.00</text>", above, StringComparison.Ordinal);
        Assert.Contains("<title>The analysts' mean target as fetched on 2026-09-27: 180.00</title>", above, StringComparison.Ordinal);
        Assert.Contains(">Mean target 180.00</text>", above, StringComparison.Ordinal);
        Assert.Contains("<text class=\"tm-against gain\" x=\"626\" y=\"44\" text-anchor=\"end\" data-against=\"20\">+20.0%</text>", above, StringComparison.Ordinal);
        Assert.EndsWith("</svg><p class=\"mark-note\" data-fetched=\"2026-09-27\">The analysts' mean target as fetched on 2026-09-27; the provider files no low or high target.</p>", above, StringComparison.Ordinal);

        // Worked by hand: 150 and 180 padded by 8 per cent of their span, 147.6 to 182.4 over the 520 from 46, so the price
        // at 81.86 and the target's square centred at 530.14.
        Assert.Contains("<circle class=\"tm-price\" cx=\"81.86\" cy=\"40\" r=\"6\">", above, StringComparison.Ordinal);
        Assert.Contains("<rect class=\"tm-target\" x=\"524.14\" y=\"34\"", above, StringComparison.Ordinal);

        // Against a price of 200 it stands 10 per cent below, in the fall hue.
        Assert.Contains(
            "<text class=\"tm-against loss\" x=\"626\" y=\"44\" text-anchor=\"end\" data-against=\"-10\">-10.0%</text>",
            marks.TargetMarker("CVX", AnalystView.Of(new DateOnly(2026, 9, 27), [], fetches, ReportNight, 200m), 200m),
            StringComparison.Ordinal);

        // No reading, a fetch filing no target, and no price each say what the marker lacks.
        const string NoTarget = "<p class=\"mark-short\" data-mark=\"target-marker\">No fetch filed the analysts' mean target for CVX, so none is drawn.</p>";

        Assert.Equal(NoTarget, marks.TargetMarker("CVX", null, 150m));
        Assert.Equal(NoTarget, marks.TargetMarker("CVX", AnalystView.Of(new DateOnly(2026, 9, 27), [], [fetches[0] with { Target = null }], ReportNight, 150m), 150m));
        Assert.Equal(
            "<p class=\"mark-short\" data-mark=\"target-marker\">No price is stored for CVX to set the analysts' mean target against.</p>",
            marks.TargetMarker("CVX", AnalystView.Of(new DateOnly(2026, 9, 27), [], fetches, ReportNight, null), null));
    }

    [Fact]
    public async Task EveryFixtureNamesChartPageDrawsItsGraphsBeneathTheChartFromTheStore()
    {
        using var store = await FixtureReplay.ReplayedAsync();
        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var names = Rows(store, "SELECT DISTINCT ticker FROM bar ORDER BY ticker;").Select(row => row[0]).ToArray();
        var printsRead = 0;
        var quartersRead = 0;
        var targetsRead = 0;

        Assert.True(names.Length >= 4, $"The fixture holds {names.Length} names' bars.");

        foreach (var ticker in names)
        {
            var page = await client.GetStringAsync($"/screens/chart/{ticker}");

            // The graphs' card beneath the chart's and above the figures'.
            var chart = page.IndexOf("id=\"chart\" data-card=\"chart-page-chart\"", StringComparison.Ordinal);
            var graphs = page.IndexOf("<section class=\"card\" id=\"graphs\" data-card=\"chart-page-graphs\">", StringComparison.Ordinal);
            var figures = page.IndexOf("id=\"figures\" data-card=\"chart-page-figures\"", StringComparison.Ordinal);

            Assert.True(chart > 0 && chart < graphs && graphs < figures, $"{ticker}'s chart page draws its graphs at {graphs}, its chart at {chart} and its figures at {figures}.");

            // The earnings dots: every print the reaction record holds with its reported figure, oldest first, each figure
            // whole as stored, and the current quarter's consensus of the newest trend after the newest print as the next.
            var prints = Rows(store, $"SELECT report_date, IFNULL(estimate, 'none'), actual FROM earnings_reaction WHERE ticker = '{ticker}' AND actual IS NOT NULL ORDER BY report_date;");

            if (prints.Count >= 2)
            {
                Assert.Equal(
                    prints.Select(row => (row[0], Held(row[2]))),
                    Regex.Matches(page, "<circle class=\"ed-actual ed-actual-[a-z]+\"[^>]*data-report-date=\"([^\"]+)\" data-actual=\"([^\"]+)\"")
                        .Select(match => (match.Groups[1].Value, Held(match.Groups[2].Value))));
                Assert.Equal(
                    prints.Where(row => row[1] != "none").Select(row => (row[0], Held(row[1]))),
                    Regex.Matches(page, "<circle class=\"ed-estimate\"[^>]*data-report-date=\"([^\"]+)\" data-estimate=\"([^\"]+)\"")
                        .Select(match => (match.Groups[1].Value, Held(match.Groups[2].Value))));

                var next = Rows(store, $"SELECT period_end FROM estimate_trend WHERE ticker = '{ticker}' AND period = '0q' AND eps_average IS NOT NULL AND period_end > '{prints[^1][0]}' AND fetched_at = (SELECT MAX(fetched_at) FROM estimate_trend WHERE ticker = '{ticker}');");

                Assert.Contains($"data-prints=\"{prints.Count}\" data-next=\"{(next.Count == 1 ? next[0][0] : "none")}\"", page, StringComparison.Ordinal);
                printsRead += prints.Count;
            }
            else
            {
                Assert.Contains($"<p class=\"mark-short\" data-mark=\"earnings-dots\" data-prints=\"{prints.Count}\">", page, StringComparison.Ordinal);
            }

            // The income bars: the newest fetch's quarters holding a revenue, oldest first, each revenue and net income whole
            // as stored, and the net margin to six places of the fraction.
            var quarters = Rows(store, $"SELECT period_end, revenue, IFNULL(net_income, 'none') FROM reported_quarter WHERE ticker = '{ticker}' AND revenue IS NOT NULL AND fetched_at = (SELECT MAX(fetched_at) FROM reported_quarter WHERE ticker = '{ticker}') ORDER BY period_end;");

            if (quarters.Count >= 2)
            {
                Assert.Equal(
                    quarters.Select(row => (row[0], Held(row[1]))),
                    Regex.Matches(page, "<rect class=\"ib-revenue\"[^>]*data-quarter=\"([^\"]+)\" data-revenue=\"([^\"]+)\"")
                        .Select(match => (match.Groups[1].Value, Held(match.Groups[2].Value))));
                Assert.Equal(
                    quarters.Where(row => row[2] != "none").Select(row => (Held(row[2]) >= 0m ? "up" : "down", row[0], Held(row[2]))),
                    Regex.Matches(page, "<rect class=\"ib-income ib-income-([a-z]+)\"[^>]*data-quarter=\"([^\"]+)\" data-income=\"([^\"]+)\"")
                        .Select(match => (match.Groups[1].Value, match.Groups[2].Value, Held(match.Groups[3].Value))));
                Assert.Equal(
                    quarters.Where(row => row[2] != "none" && Held(row[1]) != 0m).Select(row => (row[0], decimal.Round(Held(row[2]) / Held(row[1]), 6, MidpointRounding.ToEven) * 100)),
                    Regex.Matches(page, "<circle class=\"ib-margin-dot\"[^>]*data-quarter=\"([^\"]+)\" data-net=\"([^\"]+)\"")
                        .Select(match => (match.Groups[1].Value, Held(match.Groups[2].Value))));
                quartersRead += quarters.Count;
            }
            else
            {
                Assert.Contains($"<p class=\"mark-short\" data-mark=\"income-bars\" data-quarters=\"{quarters.Count}\">", page, StringComparison.Ordinal);
            }

            // The target marker: the newest company fetch's mean target against the newest stored close, the page's price
            // outside the session.
            var target = Rows(store, $"SELECT IFNULL(target_price, 'none') FROM company WHERE ticker = '{ticker}' ORDER BY fetched_at DESC LIMIT 1;");
            var close = Rows(store, $"SELECT close FROM bar WHERE ticker = '{ticker}' ORDER BY session_date DESC LIMIT 1;").Single()[0];

            if (target.Count == 1 && target[0][0] != "none")
            {
                var marker = Regex.Match(page, "data-mark=\"target-marker\" data-target=\"([^\"]+)\" data-price=\"([^\"]+)\"");

                Assert.True(marker.Success, $"{ticker}'s chart page draws no target marker.");
                Assert.Equal((Held(target[0][0]), Held(close)), (Held(marker.Groups[1].Value), Held(marker.Groups[2].Value)));
                targetsRead++;
            }
            else
            {
                Assert.Contains($"No fetch filed the analysts' mean target for {ticker}, so none is drawn.", System.Net.WebUtility.HtmlDecode(page), StringComparison.Ordinal);
            }
        }

        // The fixture's four names give prints, quarters and targets to read, so each comparison above read some.
        Assert.True(printsRead >= 8, $"Read {printsRead} prints off the chart pages, expected at least 8.");
        Assert.True(quartersRead >= 24, $"Read {quartersRead} quarters off the chart pages, expected at least 24.");
        Assert.True(targetsRead >= 2, $"Read {targetsRead} targets off the chart pages, expected at least 2.");
    }
}
