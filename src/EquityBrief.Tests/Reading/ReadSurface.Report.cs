using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Core.Quarters;
using EquityBrief.Core.Report;
using EquityBrief.Tests.Checks;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.Marks;

namespace EquityBrief.Tests.Reading;

// read-surface, 18.2: the nine marks the latest quarter, the segments, the margins, the analysts, the dividend's safety and
// the valuation draw, each over a full input with its values whole on its elements and a title on every mark, and over the
// input it degrades on with the words it says in place of what it cannot draw; and the regions read back off the rendered
// pages of the replayed fixture against the store, figure by figure.
// see: Marks are defined once and every screen draws from that list
// see: A figure is drawn at the places it is read at, and its element carries the stored value whole
public partial class ReadSurface
{
    // The rows 18.2 adds that this check reaches: section 15.5's nine marks and section 15.9's seven regions.
    internal static string[] ReportPageRows =>
    [
        CheckReach.Key("15.5 The mark vocabulary", "Growth bars"),
        CheckReach.Key("15.5 The mark vocabulary", "Margin lines"),
        CheckReach.Key("15.5 The mark vocabulary", "Share bars"),
        CheckReach.Key("15.5 The mark vocabulary", "Estimate trend"),
        CheckReach.Key("15.5 The mark vocabulary", "Rating bars"),
        CheckReach.Key("15.5 The mark vocabulary", "Dividend bars"),
        CheckReach.Key("15.5 The mark vocabulary", "Yield lines"),
        CheckReach.Key("15.5 The mark vocabulary", "Multiple band"),
        CheckReach.Key("15.5 The mark vocabulary", "Peer dots"),
        CheckReach.Key("15.9 Name", "The latest quarter"),
        CheckReach.Key("15.9 Name", "What management said"),
        CheckReach.Key("15.9 Name", "Segments"),
        CheckReach.Key("15.9 Name", "Margins"),
        CheckReach.Key("15.9 Name", "Analysts"),
        CheckReach.Key("15.9 Name", "Dividend safety"),
        CheckReach.Key("15.9 Name", "Valuation"),
    ];

    static readonly DateOnly ReportNight = new(2026, 10, 8);

    // Each mark's elements carrying a value and a title, as the pointer reads them.
    static (string Kind, string Key, string Value, string Title)[] Marked(string svg, string element) =>
    [
        .. Regex.Matches(svg, $"<{element} class=\"([^\"]+)\"[^>]*? data-([a-z-]+)=\"([^\"]+)\"><title>([^<]+)</title></{element}>")
            .Select(match => (match.Groups[1].Value, match.Groups[2].Value, match.Groups[3].Value, WebUtility.HtmlDecode(match.Groups[4].Value))),
    ];

    [Fact]
    public void TheQuartersTwoMarksDrawEachQuartersFiguresAndSayWhatTheyLack()
    {
        var marks = new MarkRenderer();

        // Growth bars: two quarters reading a growth and one reading none, each figure a bar in the rise or the fall hue by
        // its sign with its value whole on it and a title, about one zero rule, beneath a row naming the two series.
        var growth = marks.GrowthBars("CVX", [
            new QuarterGrowth(new DateOnly(2025, 12, 31), null, null),
            new QuarterGrowth(new DateOnly(2026, 3, 31), 12.5, -3.0),
            new QuarterGrowth(new DateOnly(2026, 6, 30), -4.2, 8.0),
        ]);

        Assert.Contains("data-mark=\"growth-bars\" data-quarters=\"2\"", growth, StringComparison.Ordinal);
        Assert.Equal(
            [
                ("gb-rev gb-rev-up", "revenue", "12.5", "Quarter to 2026-03-31: revenue +12.5% on a year earlier"),
                ("gb-eps gb-eps-down", "eps", "-3", "Quarter to 2026-03-31: earnings a share -3.0% on a year earlier"),
                ("gb-rev gb-rev-down", "revenue", "-4.2", "Quarter to 2026-06-30: revenue -4.2% on a year earlier"),
                ("gb-eps gb-eps-up", "eps", "8", "Quarter to 2026-06-30: earnings a share +8.0% on a year earlier"),
            ],
            Marked(growth, "rect").Where(mark => mark.Kind.StartsWith("gb-", StringComparison.Ordinal)));
        Assert.Contains(">Revenue on a year earlier</text>", growth, StringComparison.Ordinal);
        Assert.Contains(">Earnings a share on a year earlier</text>", growth, StringComparison.Ordinal);
        Assert.Single(Regex.Matches(growth, "<line class=\"m-axisline\""));

        // One quarter holding a growth says the bars need two.
        Assert.Equal(
            "<p class=\"mark-short\" data-mark=\"growth-bars\" data-quarters=\"1\">1 of the quarters stored for CVX hold a growth on a year earlier, and the bars need two.</p>",
            marks.GrowthBars("CVX", [new QuarterGrowth(new DateOnly(2026, 3, 31), null, null), new QuarterGrowth(new DateOnly(2026, 6, 30), 1.0, null)]));

        // Margin lines: three lines over two quarters, a margin no quarter holds drawn as no point, each point whole with
        // a title and each line named at its end and in the row above.
        var margins = marks.MarginLines("CVX", [
            new QuarterMargins(new DateOnly(2026, 3, 31), 45.0, 30.0, 25.0),
            new QuarterMargins(new DateOnly(2026, 6, 30), 46.5, 31.2, null),
        ]);

        Assert.Contains("data-mark=\"margin-lines\" data-quarters=\"2\"", margins, StringComparison.Ordinal);
        Assert.Equal(
            [
                ("ml-gross-dot", "gross", "45", "Quarter to 2026-03-31: gross margin 45.0%"),
                ("ml-gross-dot", "gross", "46.5", "Quarter to 2026-06-30: gross margin 46.5%"),
                ("ml-operating-dot", "operating", "30", "Quarter to 2026-03-31: operating margin 30.0%"),
                ("ml-operating-dot", "operating", "31.2", "Quarter to 2026-06-30: operating margin 31.2%"),
                ("ml-net-dot", "net", "25", "Quarter to 2026-03-31: net margin 25.0%"),
            ],
            Marked(margins, "circle"));
        Assert.Equal(3, Regex.Matches(margins, "<polyline class=\"ml-").Count);
        Assert.Equal(["Gross 46.5%", "Operating 31.2%", "Net 25.0%"], Regex.Matches(margins, "<text class=\"m-rownote\"[^>]*>([^<]+)</text>").Select(match => match.Groups[1].Value));

        Assert.Equal(
            "<p class=\"mark-short\" data-mark=\"margin-lines\" data-quarters=\"1\">1 of the quarters stored for CVX hold a margin, and the lines need two.</p>",
            marks.MarginLines("CVX", [new QuarterMargins(new DateOnly(2026, 3, 31), null, null, null), new QuarterMargins(new DateOnly(2026, 6, 30), 45.0, null, null)]));
    }

    [Fact]
    public void TheSegmentsAndTheAnalystsMarksDrawWhatWasReadAndSayWhatTheyLack()
    {
        var marks = new MarkRenderer();

        // Share bars: each segment's share of the company's revenue, 60% and 40% of a 300-unit line, each named with its
        // change on a year in the rise or the fall hue with its sign.
        var clean = new SegmentReading(
            "R9.htm",
            new DateOnly(2026, 6, 30),
            3,
            10_000_000_000m,
            10_000_000_000m,
            [
                new SegmentRow("Upstream", 6_000_000_000m, 5_000_000_000m, 20.0, null, null, null, 60.0),
                new SegmentRow("Downstream", 4_000_000_000m, 4_200_000_000m, -4.7619, null, null, null, 40.0),
            ],
            null);
        var shares = marks.ShareBars(clean);

        Assert.Contains("data-mark=\"share-bars\" data-segments=\"2\"", shares, StringComparison.Ordinal);
        Assert.Equal(
            [("sb-bar", "share", "60", "Upstream: 60.0% of revenue"), ("sb-bar", "share", "40", "Downstream: 40.0% of revenue")],
            Marked(shares, "rect"));
        Assert.Equal(["180", "120"], Regex.Matches(shares, "<rect class=\"sb-bar\"[^>]*width=\"([^\"]+)\"").Select(match => match.Groups[1].Value));
        Assert.Equal(
            [("up", "+20.0%"), ("down", "-4.8%")],
            Regex.Matches(shares, "<text class=\"sb-change sb-(up|down)\"[^>]*>([^<]+)</text>").Select(match => (match.Groups[1].Value, match.Groups[2].Value)));

        // A table summing to no total draws none and says why.
        Assert.Equal(
            "<p class=\"mark-short\" data-mark=\"share-bars\">No segment's share is drawn, because the segments' revenue does not sum to the company's within rounding.</p>",
            marks.ShareBars(clean with { Rows = [], Raw = "the segments' revenue does not sum to the company's within rounding" }));

        // Estimate trend: this fiscal year's consensus at its five points as one line, each point whole with a title, and
        // the next year's, filed at one point, drawn as none.
        EstimatePeriod year = new(
            EstimatePeriod.CurrentYear, new DateOnly(2026, 12, 31), 10.00m, null, null, 8.00m, 20, null, null, null, null, null,
            10.00m, 9.90m, 9.50m, 9.20m, 9.00m, null, null, null, null);
        var next = year with { Period = EstimatePeriod.NextYear, PeriodEnd = new DateOnly(2027, 12, 31), EpsNow = 11.00m, Eps7DaysAgo = null, Eps30DaysAgo = null, Eps60DaysAgo = null, Eps90DaysAgo = null };

        RatingFetch[] fetches = [new(new DateOnly(2026, 9, 27), 9, 6, 9, 0, 1, 4.1, 180m)];

        var analysts = AnalystView.Of(new DateOnly(2026, 9, 27), [year, next], fetches, ReportNight, 150m)!;
        var trend = marks.EstimateTrend(analysts);

        Assert.Contains("data-mark=\"estimate-trend\" data-lines=\"1\"", trend, StringComparison.Ordinal);
        Assert.Equal(
            ["90 9.00 This fiscal year: 9.00 90 days before the fetch", "60 9.20 This fiscal year: 9.20 60 days before the fetch", "30 9.50 This fiscal year: 9.50 30 days before the fetch", "7 9.90 This fiscal year: 9.90 7 days before the fetch", "0 10.00 This fiscal year: 10.00 at the fetch"],
            Regex.Matches(trend, "<circle class=\"et-0-dot\"[^>]*data-period=\"0y\" data-days-ago=\"(\\d+)\" data-eps=\"([^\"]+)\"><title>([^<]+)</title></circle>")
                .Select(match => match.Groups[1].Value + " " + match.Groups[2].Value + " " + match.Groups[3].Value));
        Assert.Contains(">This fiscal year, the analysts' consensus</text>", trend, StringComparison.Ordinal);

        Assert.Equal(
            "<p class=\"mark-short\" data-mark=\"estimate-trend\">Neither fiscal year's estimate is filed at two points of the last 90 days, so no trend is drawn.</p>",
            marks.EstimateTrend(AnalystView.Of(new DateOnly(2026, 9, 27), [next], fetches, ReportNight, 150m)!));

        // Rating bars: September's fetch stacked a grade a bar with its count whole and a title, a grade of none drawn as
        // none, and each of the other eleven months a dashed outline saying no fetch fell in it.
        var ratings = marks.RatingBars(analysts);

        Assert.Equal(
            [
                ("rt-strong-buy", "strong-buy", "9", "Sep 26: 9 strong buy as fetched on 2026-09-27"),
                ("rt-buy", "buy", "6", "Sep 26: 6 buy as fetched on 2026-09-27"),
                ("rt-hold", "hold", "9", "Sep 26: 9 hold as fetched on 2026-09-27"),
                ("rt-strong-sell", "strong-sell", "1", "Sep 26: 1 strong sell as fetched on 2026-09-27"),
            ],
            Marked(ratings, "rect").Where(mark => mark.Kind.StartsWith("rt-", StringComparison.Ordinal)));
        Assert.Equal(11, Regex.Matches(ratings, "<rect class=\"m-dash\"[^>]*data-fetched=\"none\"><title>[A-Z][a-z]{2} \\d{2}: no fetch this month</title></rect>").Count);
        Assert.Contains("data-month=\"2026-09\"", ratings, StringComparison.Ordinal);

        Assert.Equal(
            "<p class=\"mark-short\" data-mark=\"rating-bars\">No fetch in the last twelve months filed the analysts' rating counts, so none is drawn.</p>",
            marks.RatingBars(AnalystView.Of(new DateOnly(2026, 9, 27), [year], [], ReportNight, 150m)!));
    }

    [Fact]
    public void TheDividendAndTheValuationsMarksDrawWhatWasReadAndSayWhatTheyLack()
    {
        var marks = new MarkRenderer();

        // Four payments a year from 2022, held level in 2023, cut in 2024, raised in 2025, and three so far in 2026, at closes
        // of 100 beside the 10-year on the two sessions to the night.
        KeptDividend[] kept =
        [
            .. new[] { 2, 5, 8, 11 }.Select(month => new KeptDividend(new DateOnly(2022, month, 10), 1.00m)),
            .. new[] { 2, 5, 8, 11 }.Select(month => new KeptDividend(new DateOnly(2023, month, 10), 1.00m)),
            .. new[] { 2, 5, 8, 11 }.Select(month => new KeptDividend(new DateOnly(2024, month, 10), 0.95m)),
            .. new[] { 2, 5, 8, 11 }.Select(month => new KeptDividend(new DateOnly(2025, month, 10), 1.00m)),
            .. new[] { 2, 5, 8 }.Select(month => new KeptDividend(new DateOnly(2026, month, 10), 1.00m)),
        ];

        var dividend = DividendSafety.Of(
            4.00m,
            100m,
            8.00m,
            [],
            kept,
            [(new DateOnly(2026, 10, 7), 4.10), (ReportNight, 4.20)],
            [(new DateOnly(2026, 10, 7), 100m), (ReportNight, 100m)],
            ReportNight)!;

        // Dividend bars: a year each, the first not compared, one held level and not raised in grey, a cut, a raise, and the
        // year so far starred.
        var bars = marks.DividendBars(dividend);

        Assert.Contains("data-mark=\"dividend-bars\" data-years=\"5\"", bars, StringComparison.Ordinal);
        Assert.Equal(
            [
                "db-level 2022 4.00 4 not compared 2022: 4.00 a share over 4 payment(s)",
                "db-level 2023 4.00 4 no 2023: 4.00 a share over 4 payment(s)",
                "db-cut 2024 3.80 4 no 2024: 3.80 a share over 4 payment(s)",
                "db-raised 2025 4.00 4 yes 2025: 4.00 a share over 4 payment(s)",
                "db-level db-partial 2026 3.00 3 not compared 2026: 3.00 a share over 3 payment(s), so far this year",
            ],
            Regex.Matches(bars, "<rect class=\"(db-[a-z]+(?: db-partial)?)\"[^>]*data-year=\"(\\d+)\" data-total=\"([^\"]+)\" data-payments=\"(\\d+)\" data-raised=\"([^\"]+)\"><title>([^<]+)</title></rect>")
                .Select(match => string.Join(' ', match.Groups.Values.Skip(1).Select(group => group.Value))));
        Assert.Contains(">2026*</text>", bars, StringComparison.Ordinal);

        Assert.Equal(
            "<p class=\"mark-short\" data-mark=\"dividend-bars\" data-years=\"1\">The store keeps the dividends of 1 year(s), and the bars need two.</p>",
            marks.DividendBars(DividendSafety.Of(4.00m, 100m, 8.00m, [], [.. kept.Where(paid => paid.ExDate.Year == 2026)], [], [], ReportNight)!));

        // Yield lines: the trailing yield, 4.00 over 100 on each session, and the 10-year, each a line over the two
        // sessions, its newest point whole with a title and named at its end.
        var lines = marks.YieldLines(dividend);

        Assert.Contains("data-mark=\"yield-lines\" data-sessions=\"2\"", lines, StringComparison.Ordinal);
        Assert.Equal(2, Regex.Matches(lines, "<polyline class=\"yl-(?:yield|ten)\" fill=\"none\" data-sessions=\"2\"").Count);
        Assert.Equal(
            [("yl-yield-dot", "yield", "4", "Yield 4.00% on 2026-10-08"), ("yl-ten-dot", "ten-year", "4.2", "10-year 4.20% on 2026-10-08")],
            Marked(lines, "circle"));

        Assert.Equal(
            "<p class=\"mark-short\" data-mark=\"yield-lines\" data-sessions=\"1\">Neither the trailing yield nor the 10-year is held on two sessions of the year, so no line is drawn.</p>",
            marks.YieldLines(DividendSafety.Of(4.00m, 100m, 8.00m, [], kept, [(ReportNight, 4.20)], [(ReportNight, 100m)], ReportNight)!));

        // Multiple band: each quarter's multiple whole on its dot within the band of their range, and the night's beside.
        var valuation = new ValuationView(22.5, 20.0, 25.0, "in the middle of its range", null, [new QuarterMultiple(new DateOnly(2026, 3, 31), 25m), new QuarterMultiple(new DateOnly(2026, 6, 30), 20m)], null);
        var band = marks.MultipleBand("CVX", valuation);

        Assert.Contains("data-mark=\"multiple-band\" data-quarters=\"2\"", band, StringComparison.Ordinal);
        Assert.Contains("<rect class=\"mb-band\"", band, StringComparison.Ordinal);
        Assert.Contains("data-low=\"20\" data-high=\"25\"/>", band, StringComparison.Ordinal);
        Assert.Equal(
            [
                ("mb-dot", "multiple", "25", "Quarter to 2026-03-31: 25.0 times its four quarters' earnings"),
                ("mb-dot", "multiple", "20", "Quarter to 2026-06-30: 20.0 times its four quarters' earnings"),
                ("mb-night", "multiple", "22.5", "Tonight: 22.5 times"),
            ],
            Marked(band, "circle"));

        Assert.Equal(
            "<p class=\"mark-short\" data-mark=\"multiple-band\" data-quarters=\"1\">1 of the quarters stored for CVX hold a multiple, and the band needs two.</p>",
            marks.MultipleBand("CVX", valuation with { Quarters = [new QuarterMultiple(new DateOnly(2026, 6, 30), 20m)] }));

        // Peer dots: each member's multiple on one line, the stock's own larger and named last, and the median a rule.
        var peers = PeerValuation.Of("Oil & Gas Integrated", "OWN", [
            new PeerValue("AAA", 20.0, 2.0, 5.0, false),
            new PeerValue("BBB", 15.0, null, 10.0, false),
            new PeerValue("CCC", null, 1.0, null, false),
            new PeerValue("OWN", 25.0, 3.0, 7.0, false),
        ])!;
        var dots = marks.PeerDots("OWN", peers);

        Assert.Contains("data-mark=\"peer-dots\" data-members=\"3\"", dots, StringComparison.Ordinal);
        Assert.Equal(
            ["pd-dot 4 BBB 15 BBB: 15.0 times", "pd-dot 4 AAA 20 AAA: 20.0 times", "pd-own 7 OWN 25 OWN: 25.0 times"],
            Regex.Matches(dots, "<circle class=\"(pd-dot|pd-own)\"[^>]*r=\"(\\d+)\" data-ticker=\"([^\"]+)\" data-multiple=\"([^\"]+)\"><title>([^<]+)</title></circle>")
                .Select(match => string.Join(' ', match.Groups.Values.Skip(1).Select(group => group.Value))));
        Assert.Contains("data-median=\"20\"><title>The median, 20.0 times</title></line>", dots, StringComparison.Ordinal);
        Assert.Contains(">OWN 25.0x</text>", dots, StringComparison.Ordinal);

        Assert.Equal(
            "<p class=\"mark-short\" data-mark=\"peer-dots\" data-members=\"1\">1 of the members read in Oil &amp; Gas Integrated hold a multiple tonight, and the dots need two.</p>",
            marks.PeerDots("OWN", PeerValuation.Of("Oil & Gas Integrated", "OWN", [new PeerValue("CCC", null, 1.0, null, false), new PeerValue("OWN", 25.0, 3.0, 7.0, false)])!));
    }

    [Fact]
    public void WhatManagementSaidIsDrawnAsItsGuidanceAboveWhatIsWorkingBesideWhatIsNot()
    {
        var marks = new MarkRenderer();
        SourceCell[] releases =
        [
            new("r", "Results release, q3.htm", "https://www.sec.gov/Archives/edgar/data/1601046/q3.htm", new DateOnly(2026, 8, 18)),
            new("p", "Previous results release, q2.htm", "https://www.sec.gov/Archives/edgar/data/1601046/q2.htm", new DateOnly(2026, 5, 19)),
        ];

        const string Said =
            "On guidance management now expects the coming quarter to grow faster than the release before expected [D1] [D2].\n\n"
            + "What is working is \"strong demand across our communications customers\" [D1]. Orders ran ahead of shipments [D1].\n\n"
            + "What is not working is \"supply chain constraints\" [D1].";

        static string[] RowsIn(string part) =>
            [.. Regex.Matches(part, "<li><p class=\"prose\">(.*?)</p></li>").Select(match => WebUtility.HtmlDecode(match.Groups[1].Value))];

        var drawn = marks.WrittenSection("KEYS", new WrittenCell(MarkRenderer.WhatManagementSaid, Said, new DateOnly(2026, 9, 8), "a writer", ["r", "p"]), releases);
        var guidance = Regex.Match(drawn, "<div class=\"management-guidance\" data-said=\"guidance\">(.*?)</div><div class=\"two-col management-cols\">", RegexOptions.Singleline);
        var working = Regex.Match(drawn, "<div class=\"col-up\" data-said=\"working\"><h4>What is working</h4>(.*?)</div>", RegexOptions.Singleline);
        var notWorking = Regex.Match(drawn, "<div class=\"col-down\" data-said=\"not working\"><h4>What is not working</h4>(.*?)</div>", RegexOptions.Singleline);

        // The guidance above, and what is working beside what is not, each the section's own sentences in their order.
        Assert.True(guidance.Success && working.Success && notWorking.Success, drawn);
        Assert.Equal(["On guidance management now expects the coming quarter to grow faster than the release before expected [D1] [D2]."], RowsIn(guidance.Groups[1].Value));
        Assert.Equal(["What is working is \"strong demand across our communications customers\" [D1].", "Orders ran ahead of shipments [D1]."], RowsIn(working.Groups[1].Value));
        Assert.Equal(["What is not working is \"supply chain constraints\" [D1]."], RowsIn(notWorking.Groups[1].Value));

        // Prose that does not open on the three words, or names them out of order, is drawn as it was written.
        foreach (var prose in new[] { "Management expects growth to continue [D1].", "On guidance it expects more [D1].\n\nWhat is not working is nothing named [D1].\n\nWhat is working is demand [D1]." })
        {
            var asWritten = marks.WrittenSection("KEYS", new WrittenCell(MarkRenderer.WhatManagementSaid, prose, new DateOnly(2026, 9, 8), "a writer", ["r"]), releases);

            Assert.DoesNotContain("management-guidance", asWritten, StringComparison.Ordinal);
            Assert.DoesNotContain("management-cols", asWritten, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void TheRegionsTablesDrawEachPartTheirRowsNameAndSayWhatTheyCannotRead()
    {
        var marks = new MarkRenderer();

        // The latest quarter: a line above its estimate and a line below it, each difference in its own unit and in per cent
        // of the estimate with its sign in the rise or the fall hue and each value whole; a line the fetch did not read and
        // one the provider filed as none, each saying so.
        LatestQuarter latest = new(
            new DateOnly(2026, 6, 30),
            new DateOnly(2026, 7, 30),
            new DateOnly(2025, 6, 30),
            [
                new QuarterLine(QuarterLines.Revenue, 1_100_000_000m, 1_050_000_000m, 50_000_000m, 4.7619, 900_000_000m, 22.2222, null, null, false, null),
                new QuarterLine(QuarterLines.GrossProfit, null, null, null, null, 495_000_000m, null, null, 55.0, false, QuarterLines.FetchedBefore),
                new QuarterLine(QuarterLines.NetIncome, null, null, null, null, 135_000_000m, null, null, 15.0, false, QuarterLines.NotFiled),
                new QuarterLine(QuarterLines.Eps, 2.20m, 2.30m, -0.10m, -4.3478, 1.60m, 37.5, null, null, true, null),
            ]);
        var quarter = marks.QuarterTable("CVX", new ReportView(latest, [], new DateOnly(2026, 9, 8), "USD", [], null, null, null, null));

        Assert.Contains("<table class=\"quarter-table\" data-quarter=\"2026-06-30\" data-year-earlier=\"2025-06-30\">", quarter, StringComparison.Ordinal);
        Assert.Contains(
            "<tr data-line=\"Revenue\"><td>Revenue</td><td class=\"num\" data-reported=\"1100000000\">$1.1B</td><td class=\"num\" data-estimate=\"1050000000\">$1.1B</td>"
            + "<td class=\"num\"><span class=\"up\" data-difference=\"50000000\">+$50.0M</span> (<span class=\"up\" data-against=\"4.7619\">+4.8%</span>)</td>"
            + "<td class=\"num\" data-year-earlier=\"900000000\">$900.0M</td><td class=\"num\"><span class=\"up\" data-on-the-year=\"22.2222\">+22.2%</span></td>",
            quarter,
            StringComparison.Ordinal);
        Assert.Contains(
            "<td class=\"num\"><span class=\"down\" data-difference=\"-0.10\">-0.10</span> (<span class=\"down\" data-against=\"-4.3478\">-4.3%</span>)</td>",
            quarter,
            StringComparison.Ordinal);
        Assert.Contains($"<td class=\"num degraded\" data-reported=\"absent\">{QuarterLines.FetchedBefore}</td><td class=\"num degraded\" data-estimate=\"absent\">none kept</td>", quarter, StringComparison.Ordinal);
        Assert.Contains("<tr data-line=\"Net income\"><td>Net income</td><td class=\"num degraded\" data-reported=\"absent\">not filed</td>", quarter, StringComparison.Ordinal);
        Assert.Equal(
            "<p class=\"degraded\" data-absent=\"quarter\" data-ticker=\"CVX\">No reported quarter is stored for CVX yet, so no line is read.</p>",
            marks.QuarterTable("CVX", new ReportView(null, [], null, "USD", [], null, null, null, null)));

        // The margins quarter by quarter, the newest first, a margin no quarter holds saying none.
        Assert.Equal(
            ["2026-06-30 46.5 absent 20", "2026-03-31 45 30 25"],
            Regex.Matches(
                    marks.MarginTable([new QuarterMargins(new DateOnly(2026, 3, 31), 45.0, 30.0, 25.0), new QuarterMargins(new DateOnly(2026, 6, 30), 46.5, null, 20.0)]),
                    "<tr data-quarter=\"([^\"]+)\"><td>[^<]+</td><td class=\"num[^\"]*\" data-gross=\"([^\"]+)\">[^<]+</td><td class=\"num[^\"]*\" data-operating=\"([^\"]+)\">[^<]+</td><td class=\"num[^\"]*\" data-net=\"([^\"]+)\">")
                .Select(match => string.Join(' ', match.Groups.Values.Skip(1).Select(group => group.Value))));

        // A segment table summing to no total stands in the table's place saying what its rows summed to and why.
        Assert.Equal(
            "<p class=\"degraded\" data-segments=\"raw\" data-report=\"R65.htm\" data-total=\"12559938000\" data-sum=\"5100000000\">The filing's segment table is drawn as filed beneath, because the segments' revenue does not sum to the company's within rounding: its segments' revenue sums to $5.1B against the company's $12.6B.</p>",
            WebUtility.HtmlDecode(marks.SegmentsTable(
                new SegmentReading("R65.htm", new DateOnly(2026, 6, 30), 3, 12_559_938_000m, 5_100_000_000m, [], "the segments' revenue does not sum to the company's within rounding"),
                "USD")));

        // The consensus and its revisions, a count the provider filed none of saying so, every figure the analysts'.
        EstimatePeriod year = new(
            EstimatePeriod.CurrentYear, new DateOnly(2026, 12, 31), 10.00m, 9.00m, 11.00m, 8.00m, 20, 1_000_000_000m, 950_000_000m, 1_050_000_000m, 800_000_000m, 18,
            10.00m, 9.90m, 9.50m, 9.20m, 9.00m, 3, 8, null, 2);
        var consensus = marks.ConsensusTable(AnalystView.Of(new DateOnly(2026, 9, 27), [year], [], ReportNight, 150m)!, "USD");

        Assert.Contains("<table class=\"consensus-table\" data-fetched=\"2026-09-27\"><tr><th>The analysts' estimate for</th>", consensus, StringComparison.Ordinal);
        Assert.Contains(
            "<tr data-period=\"0y\" data-period-end=\"2026-12-31\"><td>This fiscal year to 2026-12-31</td><td class=\"num\" data-eps=\"10.00\">10.00</td><td class=\"num\">9.00 to 11.00</td><td class=\"num\" data-analysts=\"20\">20</td>"
            + "<td class=\"num\"><span class=\"up\" data-eps-growth=\"25\">+25.0%</span></td><td class=\"num\" data-revenue=\"1000000000\">$1.0B</td><td class=\"num\">$1.0B to $1.1B</td><td class=\"num\"><span class=\"up\" data-revenue-growth=\"25\">+25.0%</span></td></tr>",
            consensus,
            StringComparison.Ordinal);
        Assert.Contains(
            "<tr data-period=\"0y\"><td>This fiscal year</td><td class=\"num\" data-up-7=\"3\">3</td><td class=\"num degraded\" data-down-7=\"absent\">none filed</td><td class=\"num\" data-up-30=\"8\">8</td><td class=\"num\" data-down-30=\"2\">2</td><td class=\"num\"><span class=\"up\" data-change-30=\"5.2632\">+5.3%</span></td></tr>",
            consensus,
            StringComparison.Ordinal);

        // The dividend's safety at the quote: the rate, the yield at the price drawn, the 10-year on its session and the
        // spread with its sign, both payouts with what each was read from, and the years raised in a row.
        KeptDividend[] kept =
        [
            .. new[] { 2, 5, 8, 11 }.Select(month => new KeptDividend(new DateOnly(2024, month, 10), 0.95m)),
            .. new[] { 2, 5, 8, 11 }.Select(month => new KeptDividend(new DateOnly(2025, month, 10), 1.00m)),
            .. new[] { 2, 5, 8 }.Select(month => new KeptDividend(new DateOnly(2026, month, 10), 1.00m)),
        ];
        CashQuarter[] cash =
        [
            new(new DateOnly(2026, 6, 30), 1_000_000_000m, -500_000_000m),
            new(new DateOnly(2026, 3, 31), 1_000_000_000m, -500_000_000m),
            new(new DateOnly(2025, 12, 31), 1_000_000_000m, -500_000_000m),
            new(new DateOnly(2025, 9, 30), 1_000_000_000m, -500_000_000m),
        ];

        var safety = marks.DividendTable(DividendSafety.Of(4.00m, 100m, 8.00m, cash, kept, [(ReportNight, 4.20)], [], ReportNight)!, 100m, live: true, "USD");

        Assert.Contains("<tr data-row=\"rate\"><td>Rate a share a year, forward</td><td class=\"num\"><span data-rate=\"4.00\">4.00</span></td></tr>", safety, StringComparison.Ordinal);
        Assert.Contains("<tr data-row=\"yield\"><td>Yield at 100.00, the quote</td><td class=\"num\"><span data-yield=\"4\">4.00%</span></td></tr>", safety, StringComparison.Ordinal);
        Assert.Contains("<tr data-row=\"ten-year\"><td>The Treasury's 10-year par yield on 2026-10-08</td><td class=\"num\"><span data-ten-year=\"4.2\">4.20%</span></td></tr>", safety, StringComparison.Ordinal);
        Assert.Matches("<tr data-row=\"spread\"><td>The yield against the 10-year</td><td class=\"num\"><span class=\"down\" data-spread=\"-0\\.2[0-9]*\">-0\\.20 points</span></td></tr>", safety);
        Assert.Contains("<span data-payout-earnings=\"50\">50.0%</span> of 8.00", safety, StringComparison.Ordinal);
        Assert.Contains("<span data-payout-cash=\"50\">50.0%</span>, $2.0B of $4.0B", safety, StringComparison.Ordinal);
        Assert.Contains("<tr data-row=\"raised\"><td>Whole years in a row it raised the dividend</td><td class=\"num\"><span data-raised=\"1\">1</span></td></tr>", safety, StringComparison.Ordinal);

        // With no forward rate, no 10-year, no earnings and a quarter with no dividends paid, each figure says why it is not
        // read, at the last close.
        var unread = marks.DividendTable(DividendSafety.Of(null, 100m, null, [cash[0] with { DividendsPaid = null }, .. cash[1..]], kept, [], [], ReportNight)!, 100m, live: false, "USD");

        Assert.Contains("<td>Paid a share over the last year</td>", unread, StringComparison.Ordinal);
        Assert.Contains("<td>Yield at 100.00, the last close</td>", unread, StringComparison.Ordinal);
        Assert.Contains("<td>The Treasury's 10-year par yield</td><td class=\"num\"><span class=\"degraded\" data-ten-year=\"absent\">not read yet</span></td>", unread, StringComparison.Ordinal);
        Assert.Contains("<span class=\"degraded\" data-payout-earnings=\"absent\">not read: the last four quarters' earnings are not above nought or not stored</span>", WebUtility.HtmlDecode(unread), StringComparison.Ordinal);
        Assert.Contains("<span class=\"degraded\" data-payout-cash=\"absent\">not read: the last four quarters do not all hold their free cash flow and dividends paid, or it is not above nought</span>", unread, StringComparison.Ordinal);

        // The members' table: the stock's own row marked, a figure the night read none of saying so, and the median of each
        // column over the members holding it.
        var peers = marks.PeerTable(PeerValuation.Of("Oil & Gas Integrated", "OWN", [
            new PeerValue("AAA", 20.0, 2.0, 5.0, false),
            new PeerValue("BBB", 15.0, null, 10.0, false),
            new PeerValue("CCC", null, 1.0, null, false),
            new PeerValue("OWN", 25.0, 3.0, 7.0, false),
        ])!);

        Assert.Equal(["BBB", "AAA", "OWN", "CCC"], Regex.Matches(peers, "<tr data-ticker=\"([^\"]+)\"").Select(match => match.Groups[1].Value));
        Assert.Contains("<tr data-ticker=\"OWN\" class=\"key-row\" data-own=\"yes\"><td><a href=\"#/name/OWN\">OWN</a></td><td class=\"num\" data-multiple=\"25\">25.0</td><td class=\"num\" data-yield=\"3\">3.00%</td><td class=\"num\"><span class=\"up\" data-sales-growth=\"7\">+7.0%</span></td></tr>", peers, StringComparison.Ordinal);
        Assert.Contains("<tr data-ticker=\"CCC\"><td><a href=\"#/name/CCC\">CCC</a></td><td class=\"num degraded\">none read</td><td class=\"num\" data-yield=\"1\">1.00%</td><td class=\"num degraded\">none read</td></tr>", peers, StringComparison.Ordinal);
        Assert.Contains("<tr class=\"group-row\" data-row=\"median\"><td>The median</td><td class=\"num\">20.0 of 3</td><td class=\"num\">2.00% of 3</td><td class=\"num\">7.0% of 3</td></tr>", peers, StringComparison.Ordinal);
    }

    // A stored money figure, a percentage of one worked as the core works it, and a figure drawn on the page, as decimals.
    static decimal Held(string stored) => decimal.Parse(stored, NumberStyles.Float, CultureInfo.InvariantCulture);

    static decimal? Change(decimal now, decimal then) => then > 0m ? decimal.Round((now - then) / then, 6, MidpointRounding.ToEven) * 100 : null;

    [Fact]
    public async Task TheRegionsAfterTheCardAreReadBackOffThePageAgainstTheStore()
    {
        using var store = await FixtureReplay.ReplayedAsync();

        // MSFT, whose bars run to the night and which pays: every region but what management said, which no pass wrote,
        // drawn after the card in section 4's order and before the earnings reactions.
        var page = await NameRoute(store, "MSFT");
        string[] order = ["quarter", "segments", "margins", "analysts", "dividend", "valuation", "reactions"];

        Assert.Equal(
            order,
            Regex.Matches(page, "<section class=\"[^\"]*\" id=\"([^\"]+)\"").Select(match => match.Groups[1].Value).Where(order.Contains));
        Assert.DoesNotContain("id=\"management\"", page, StringComparison.Ordinal);

        // The newest fetch's quarters as the store holds them, oldest first.
        var quarters = Rows(store, "SELECT period_end, revenue, gross_profit, operating_income, net_income, eps_actual, operating_cash_flow, free_cash_flow, IFNULL(eps_trailing, 'null'), IFNULL(close_after, 'null'), IFNULL(dividends_paid, 'null') FROM reported_quarter WHERE ticker = 'MSFT' ORDER BY period_end;")
            .Select(row => (End: DateOnly.ParseExact(row[0], "yyyy-MM-dd", CultureInfo.InvariantCulture), Row: row))
            .ToArray();
        var newest = quarters[^1];
        var yearEarlier = quarters.Single(quarter => Math.Abs(quarter.End.DayNumber - newest.End.AddYears(-1).DayNumber) <= QuarterFetch.NearDays);

        Assert.Equal(12, quarters.Length);

        // The latest quarter: each line's reported figure and its year earlier whole on the line, in the store's own text.
        Assert.Contains(FormattableString.Invariant($"data-quarter=\"{newest.End:yyyy-MM-dd}\" data-year-earlier=\"{yearEarlier.End:yyyy-MM-dd}\""), page, StringComparison.Ordinal);

        foreach (var (line, column) in new[] { (QuarterLines.Revenue, 1), (QuarterLines.GrossProfit, 2), (QuarterLines.OperatingIncome, 3), (QuarterLines.NetIncome, 4), (QuarterLines.Eps, 5), (QuarterLines.OperatingCashFlow, 6), (QuarterLines.FreeCashFlow, 7) })
        {
            var drawn = Regex.Match(page, $"<tr data-line=\"{line}\"[^>]*><td>{line}</td><td class=\"num\" data-reported=\"([^\"]+)\">.*?<td class=\"num\" data-year-earlier=\"([^\"]+)\">", RegexOptions.Singleline);

            Assert.True(drawn.Success, $"MSFT's latest quarter draws no line {line} with its figures.");
            Assert.Equal((Held(newest.Row[column]), Held(yearEarlier.Row[column])), (Held(drawn.Groups[1].Value), Held(drawn.Groups[2].Value)));
        }

        // The growth bars: each of the eight newest quarters' revenue on the quarter within a week of a year before it,
        // worked here from the stored revenue.
        var grown = quarters.TakeLast(8)
            .Select(quarter => (quarter.End, Revenue: quarters.Where(earlier => Math.Abs(earlier.End.DayNumber - quarter.End.AddYears(-1).DayNumber) <= QuarterFetch.NearDays).Select(earlier => Change(Held(quarter.Row[1]), Held(earlier.Row[1]))).FirstOrDefault()))
            .Where(quarter => quarter.Revenue is not null)
            .ToArray();
        var bars = Regex.Matches(page, "<rect class=\"gb-rev gb-rev-(?:up|down)\"[^>]*data-quarter=\"([^\"]+)\" data-revenue=\"([^\"]+)\">")
            .Select(match => (End: DateOnly.ParseExact(match.Groups[1].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture), Revenue: (decimal?)Held(match.Groups[2].Value)))
            .ToArray();

        Assert.True(grown.Length >= 4, $"MSFT's stored quarters give {grown.Length} growths, expected at least 4.");
        Assert.Equal(grown, bars);

        // The margin lines: each of the twelve quarters' gross, operating and net margins, worked here from the stored lines.
        foreach (var (kind, column) in new[] { ("gross", 2), ("operating", 3), ("net", 4) })
        {
            var expected = quarters
                .Select(quarter => (quarter.End, Margin: decimal.Round(Held(quarter.Row[column]) / Held(quarter.Row[1]), 6, MidpointRounding.ToEven) * 100))
                .ToArray();
            var drawn = Regex.Matches(page, $"<circle class=\"ml-{kind}-dot\"[^>]*data-quarter=\"([^\"]+)\" data-{kind}=\"([^\"]+)\">")
                .Select(match => (End: DateOnly.ParseExact(match.Groups[1].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture), Margin: Held(match.Groups[2].Value)))
                .ToArray();

            Assert.Equal(expected, drawn);
        }

        // The analysts: each period of the newest fetch's trend in the consensus table, the mean target whole on its line,
        // and the fetch's month's rating counts stacked, every other month dashed.
        var trend = Rows(store, "SELECT period, period_end, eps_average FROM estimate_trend WHERE ticker = 'MSFT' ORDER BY period_end;");

        Assert.Equal(
            trend.Select(row => (row[0], row[1], Held(row[2]))),
            Regex.Matches(page, "<tr data-period=\"([^\"]+)\" data-period-end=\"([^\"]+)\"><td>[^<]+</td><td class=\"num\" data-eps=\"([^\"]+)\">")
                .Select(match => (match.Groups[1].Value, match.Groups[2].Value, Held(match.Groups[3].Value))));

        var company = Rows(store, "SELECT target_price, strong_buy, buy, hold, sell, strong_sell, substr(fetched_at, 1, 7) FROM company WHERE ticker = 'MSFT';").Single();

        Assert.Contains($"<p class=\"analyst-target\" data-target=\"{company[0]}\">", page, StringComparison.Ordinal);
        Assert.Equal(
            new[] { ("strong-buy", company[1]), ("buy", company[2]), ("hold", company[3]), ("sell", company[4]), ("strong-sell", company[5]) }.Where(grade => grade.Item2 != "0"),
            Regex.Matches(page, $"<rect class=\"rt-[a-z-]+\"[^>]*data-month=\"{company[6]}\" data-([a-z-]+)=\"(\\d+)\">").Select(match => (match.Groups[1].Value, match.Groups[2].Value)));
        Assert.Equal(11, Regex.Matches(page, "<rect class=\"m-dash\"[^>]*data-fetched=\"none\">").Count);

        // The dividend's safety: the forward rate the fetch kept, the 10-year on the night, and the payout on earnings and
        // on free cash flow, worked here from the stored rate, the newest four quarters' earnings and their cash lines.
        var rate = Held(Rows(store, "SELECT forward_rate FROM dividend_reading WHERE ticker = 'MSFT';").Single()[0]);
        var four = quarters.TakeLast(4).ToArray();

        Assert.Contains(FormattableString.Invariant($"<span data-rate=\"{rate}\">"), page, StringComparison.Ordinal);
        Assert.Contains("The Treasury's 10-year par yield on 2026-09-08</td><td class=\"num\"><span data-ten-year=\"4.8\">4.80%</span>", page, StringComparison.Ordinal);
        Assert.InRange(Held(Regex.Match(page, "data-payout-earnings=\"([^\"]+)\"").Groups[1].Value) - (rate / Held(newest.Row[8]) * 100), -0.000001m, 0.000001m);
        Assert.InRange(
            Held(Regex.Match(page, "data-payout-cash=\"([^\"]+)\"").Groups[1].Value) - (four.Sum(quarter => Math.Abs(Held(quarter.Row[10]))) / four.Sum(quarter => Held(quarter.Row[7])) * 100),
            -0.000001m,
            0.000001m);

        // The yield against the 10-year: the 10-year line over every session of the year the store holds for both, and no
        // yield line, since the store keeps no dividend of MSFT's a year back.
        var sessions = Rows(store, "SELECT COUNT(*) FROM treasury_yield WHERE session_date IN (SELECT session_date FROM bar WHERE ticker = 'MSFT');").Single()[0];

        Assert.Contains($"<polyline class=\"yl-ten\" fill=\"none\" data-sessions=\"{sessions}\"", page, StringComparison.Ordinal);
        Assert.DoesNotContain("<polyline class=\"yl-yield\"", page, StringComparison.Ordinal);

        // The valuation: each quarter's multiple, the close after its report over its four quarters' earnings to six places,
        // worked here from the stored figures.
        Assert.Equal(
            quarters
                .Where(quarter => quarter.Row[9] != "null" && quarter.Row[8] != "null" && Held(quarter.Row[8]) > 0m)
                .Select(quarter => (quarter.End, decimal.Round(Held(quarter.Row[9]) / Held(quarter.Row[8]), 6, MidpointRounding.ToEven))),
            Regex.Matches(page, "<circle class=\"mb-dot\"[^>]*data-quarter=\"([^\"]+)\" data-multiple=\"([^\"]+)\">")
                .Select(match => (DateOnly.ParseExact(match.Groups[1].Value, "yyyy-MM-dd", CultureInfo.InvariantCulture), Held(match.Groups[2].Value))));

        // Each region dated as its row says: the quarter by the newest filing, the margins and the analysts by the fetch.
        var filed = Rows(store, "SELECT MAX(filing_date) FROM fundamentals WHERE ticker = 'MSFT';").Single()[0];
        var fetched = Rows(store, "SELECT substr(MAX(fetched_at), 1, 10) FROM reported_quarter WHERE ticker = 'MSFT';").Single()[0];

        Assert.Contains($"<div class=\"lbl\">The latest quarter</div><div class=\"dl\"><span class=\"dl-k\">Filed</span><b>{filed}</b>", page, StringComparison.Ordinal);
        Assert.Contains($"<div class=\"lbl\">Margins</div><div class=\"dl\"><span class=\"dl-k\">Fetched</span><b>{fetched}</b>", page, StringComparison.Ordinal);
        Assert.Contains($"<div class=\"lbl\">Analysts</div><div class=\"dl\"><span class=\"dl-k\">Fetched</span><b>{fetched}</b><span>the analysts' figures</span>", page, StringComparison.Ordinal);

        // The margins quarter by quarter folded beneath the lines, a row a stored quarter.
        var folded = Regex.Match(page, "<details class=\"margins-table\"><summary>The margins quarter by quarter</summary>(.*?)</details>", RegexOptions.Singleline);

        Assert.True(folded.Success, "The margins draw no table folded beneath their lines.");
        Assert.Equal(quarters.Length, Regex.Matches(folded.Groups[1].Value, "<tr data-quarter=").Count);

        // The revisions beneath the consensus, each period's counts as the trend stored them; the mean rating in the line
        // with the mean target; and both fiscal years' trends as a line each.
        var revised = Rows(store, "SELECT period, IFNULL(up_last_seven_days, 'absent'), IFNULL(down_last_seven_days, 'absent'), IFNULL(up_last_thirty_days, 'absent'), IFNULL(down_last_thirty_days, 'absent') FROM estimate_trend WHERE ticker = 'MSFT' ORDER BY period_end;");

        Assert.Equal(
            revised.Select(row => string.Join(' ', row)),
            Regex.Matches(page, "<tr data-period=\"([^\"]+)\"><td>[^<]+</td><td class=\"num[^\"]*\" data-up-7=\"([^\"]+)\">[^<]+</td><td class=\"num[^\"]*\" data-down-7=\"([^\"]+)\">[^<]+</td><td class=\"num[^\"]*\" data-up-30=\"([^\"]+)\">[^<]+</td><td class=\"num[^\"]*\" data-down-30=\"([^\"]+)\">")
                .Select(match => string.Join(' ', match.Groups.Values.Skip(1).Select(group => group.Value))));
        Assert.Contains("and their mean rating 4.59 on the provider's scale from one, a strong sell, to five, a strong buy, as fetched on " + fetched + ".</p>", WebUtility.HtmlDecode(page), StringComparison.Ordinal);
        Assert.Contains("data-mark=\"estimate-trend\" data-lines=\"2\"", page, StringComparison.Ordinal);

        // The yield at the last close, the spread on the 10-year with its sign, the earnings the payout was read from, and
        // a payer the store keeps no dividend of saying it reads no years and drawing no bars.
        var close = Rows(store, "SELECT close FROM bar WHERE ticker = 'MSFT' ORDER BY session_date DESC LIMIT 1;").Single()[0];

        Assert.Contains($"<td>Yield at {Figures.Price(Held(close))}, the last close</td>", page, StringComparison.Ordinal);
        Assert.Matches("<tr data-row=\"spread\"><td>The yield against the 10-year</td><td class=\"num\"><span class=\"down\" data-spread=\"-[0-9.]+\">-[0-9.]+ points</span>", page);
        Assert.Contains(FormattableString.Invariant($"</span> of {Figures.PerShare(Held(newest.Row[8]))}</td>"), page, StringComparison.Ordinal);
        Assert.Contains("<span data-raised=\"0\">0</span> (no dividend is kept to read years from)", page, StringComparison.Ordinal);
        Assert.Contains("<p class=\"mark-short\" data-mark=\"dividend-bars\" data-years=\"0\">", page, StringComparison.Ordinal);

        // The valuation's line says why no multiple was read on the night, the fixture's year being too short for one.
        Assert.Contains("<p class=\"degraded\" data-absent=\"multiple\">No multiple was read on the night: no quarter stored.</p>", page, StringComparison.Ordinal);

        // With the night's reading carrying a multiple and two members sharing the industry, the line places the multiple in
        // its own range, and the members' dots and table follow with the stock's own row marked and the medians.
        var reading = EquityBrief.Core.Quarters.Readings.FromJson(Rows(store, "SELECT readings FROM fundamental_reading WHERE ticker = 'MSFT' AND session_date = '2026-09-08';").Single()[0]);

        string Valued(decimal multiple) =>
            (reading with { Valuation = new EquityBrief.Core.Quarters.ValuationReading([], multiple, 20.1m, 30.2m, "in the lower half of its range", null) }).ToJson().Replace("'", "''", StringComparison.Ordinal);

        store.Execute($"UPDATE fundamental_reading SET readings = '{Valued(24.5m)}' WHERE ticker = 'MSFT' AND session_date = '2026-09-08';");
        store.Execute("UPDATE member_reading SET industry = 'Software - Infrastructure' WHERE ticker = 'MSFT' AND session_date = '2026-09-08';");

        foreach (var (peer, multiple) in new[] { ("PEERA", 18.0m), ("PEERB", 31.0m) })
        {
            store.Execute(
                "INSERT INTO member_reading (index_code, session_date, ticker, close, dollar_volume, cost, cost_double, profit, industry) " +
                $"VALUES ('GSPC', '2026-09-08', '{peer}', '100.00', '1000000', 0.1, 0.2, 1, 'Software - Infrastructure');");
            store.Execute(
                "INSERT INTO fundamental_reading (ticker, session_date, state, read_from, fetched_at, awaited, readings) " +
                $"SELECT '{peer}', session_date, state, read_from, fetched_at, awaited, '{Valued(multiple)}' FROM fundamental_reading WHERE ticker = 'MSFT' AND session_date = '2026-09-08';");
        }

        var valued = await NameRoute(store, "MSFT");

        Assert.Contains(
            "<p class=\"valuation-line\" data-multiple=\"24.5\" data-position=\"in the lower half of its range\">At the night's close it trades at 24.5 times its last four quarters' earnings, against 20.1 to 30.2 over its own quarters: in the lower half of its range.</p>",
            WebUtility.HtmlDecode(valued),
            StringComparison.Ordinal);
        Assert.Contains("data-mark=\"peer-dots\" data-members=\"3\"", valued, StringComparison.Ordinal);
        Assert.Equal(
            ["PEERA", "MSFT", "PEERB"],
            Regex.Matches(Regex.Match(valued, "<table class=\"peer-value-table\".*?</table>", RegexOptions.Singleline).Value, "<tr data-ticker=\"([^\"]+)\"").Select(match => match.Groups[1].Value));
        Assert.Contains("<tr data-ticker=\"MSFT\" class=\"key-row\" data-own=\"yes\">", valued, StringComparison.Ordinal);
        Assert.Contains("<td>The median</td><td class=\"num\">24.5 of 3</td>", valued, StringComparison.Ordinal);

        // KEYS, whose filing's segments sum to the company's revenue: its two groups' revenue and the total read back, each
        // with its bar, the filing's table folded beneath and the region dated by the filing. Read off the page against the
        // figures the archive's captured report states.
        var keysight = await NameRoute(store, "KEYS");

        Assert.Contains("<table class=\"segment-table\" data-report=\"R85.htm\" data-quarter=\"2026-07-31\" data-months=\"3\" data-total=\"1846000000\" data-sum=\"1846000000\">", keysight, StringComparison.Ordinal);
        Assert.Equal(
            [("Communications Solutions Group", "1345000000"), ("Electronic Industrial Solutions Group", "501000000")],
            Regex.Matches(keysight, "<tr data-segment=\"([^\"]+)\"><td>[^<]+</td><td class=\"num\" data-revenue=\"([^\"]+)\">").Select(match => (match.Groups[1].Value, match.Groups[2].Value)));
        Assert.Contains("data-mark=\"share-bars\" data-segments=\"2\"", keysight, StringComparison.Ordinal);
        Assert.Contains("<details class=\"segments-raw\"><summary>The filing's segment table as filed</summary>", WebUtility.HtmlDecode(keysight), StringComparison.Ordinal);
        Assert.Contains(
            $"<div class=\"lbl\">Segments</div><div class=\"dl\"><span class=\"dl-k\">Filed</span><b>{Rows(store, "SELECT MAX(filing_date) FROM fundamentals WHERE ticker = 'KEYS';").Single()[0]}</b>",
            keysight,
            StringComparison.Ordinal);

        // NFLX with its trend taken out of the store: the newest fetch's counts still stand, and the table says the fetch
        // kept no trend.
        store.Execute("DELETE FROM estimate_trend WHERE ticker = 'NFLX';");

        Assert.Contains(
            "<p class=\"degraded\" data-absent=\"consensus\">The newest fetch kept no estimate trend for it, so no consensus is drawn.</p>",
            await NameRoute(store, "NFLX"),
            StringComparison.Ordinal);
    }
}
