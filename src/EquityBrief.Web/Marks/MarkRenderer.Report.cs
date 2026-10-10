using System.Text;
using EquityBrief.Core.Report;
using EquityBrief.Web.App;

namespace EquityBrief.Web.Marks;

// What the name page's quarter, segments, margins, analysts, dividend and valuation regions draw, each worked by the core's
// one function for it from stored figures: the latest quarter line by line with the growth of the eight newest quarters,
// the fetch they came from and the currency they are in; the twelve newest quarters' margins; the segment table the rule
// made of the filing's; the analysts' figures; the dividend's safety; and the valuation, the night's multiple against its
// own quarters' and the stock's industry's members'.
public sealed record ReportView(
    LatestQuarter? Quarter,
    IReadOnlyList<QuarterGrowth> Growth,
    DateOnly? FetchedOn,
    string Currency,
    IReadOnlyList<QuarterMargins> Margins,
    SegmentReading? Segments,
    AnalystReading? Analysts,
    DividendSafetyReading? Dividend,
    ValuationView? Valuation);

// The valuation as the page draws it: the night's multiple, the low and high of its own quarters' multiples and where it
// sits among them, as the night's reading stored them, each quarter's multiple, and the peer table.
public sealed record ValuationView(
    double? Multiple,
    double? Low,
    double? High,
    string? Position,
    string? NotRead,
    IReadOnlyList<QuarterMultiple> Quarters,
    PeerTable? Peers);

// The regions phase 18 places between the decision card and the earnings reactions, and the nine marks they draw. Each
// mark takes stored values worked by the core and returns an SVG string with a title on every mark for the pointer, one
// axis, and a row above it naming what it draws where it draws two series or more; each degrades by saying what it has.
// A change is drawn in the rise or the fall hue by its sign with the sign always written, on the name page alone.
// see: Marks are defined once and every screen draws from that list
// see: A figure that rose or fell is drawn in a hue of its own on the name page and its export alone
public sealed partial class MarkRenderer
{
    const double ChartWide = 640;

    const double ChartHigh = 200;

    const double ChartLeft = 46;

    const double ChartRight = 14;

    const double ChartTop = 26;

    const double ChartBottom = 30;

    // ---- the latest quarter ----

    // The latest quarter's lines: what was reported, the estimate kept before the report, the difference, the same quarter
    // a year earlier and the change on it, and each income line's margin, every change coloured by its sign.
    public string QuarterTable(string ticker, ReportView view)
    {
        if (view.Quarter is not { } quarter)
        {
            return Formatted($"<p class=\"degraded\" data-absent=\"quarter\" data-ticker=\"{Escaped(ticker)}\">No reported quarter is stored for {Escaped(ticker)} yet, so no line is read.</p>");
        }

        var table = new StringBuilder();

        table.Append(Formatted($"<div class=\"tbl-wrap\"><table class=\"quarter-table\" data-quarter=\"{DayOf(quarter.Quarter)}\" data-year-earlier=\"{(quarter.YearEarlier is { } earlier ? DayOf(earlier) : "none")}\">"));
        table.Append("<tr><th>Line</th><th class=\"num\">Reported</th><th class=\"num\">Estimate before the report</th><th class=\"num\">Difference</th>");
        table.Append(Formatted($"<th class=\"num\">{(quarter.YearEarlier is { } before ? "Quarter to " + DayOf(before) : "A year earlier")}</th><th class=\"num\">Change on a year</th><th class=\"num\">Margin, then a year earlier</th></tr>"));

        foreach (var line in quarter.Lines)
        {
            string Amount(decimal? value, string name) =>
                value is { } held
                    ? Formatted($"<td class=\"num\" data-{name}=\"{held.ToString(Invariant)}\">{(line.PerShare ? Figures.PerShare(held) : Figures.Money(held, view.Currency))}</td>")
                    : Formatted($"<td class=\"num degraded\" data-{name}=\"absent\">{(name == "reported" ? Escaped(line.NotRead ?? QuarterLines.NotFiled) : "none kept")}</td>");

            table.Append(Formatted($"<tr data-line=\"{Escaped(line.Line)}\"{(line.Line == QuarterLines.Eps ? " class=\"key-row\"" : string.Empty)}><td>{Escaped(line.Line)}</td>"));
            table.Append(Amount(line.Reported, "reported"));
            table.Append(Amount(line.Estimate, "estimate"));
            table.Append(line.Difference is { } difference
                ? "<td class=\"num\">" + SignedAmount(difference, line.PerShare, view.Currency, "difference") + (line.AgainstEstimate is { } against ? " (" + Signed(against, "against") + ")" : string.Empty) + "</td>"
                : "<td class=\"num degraded\" data-difference=\"absent\">none</td>");
            table.Append(Amount(line.YearEarlier, "year-earlier"));
            table.Append(line.OnTheYear is { } year ? "<td class=\"num\">" + Signed(year, "on-the-year") + "</td>" : "<td class=\"num degraded\" data-on-the-year=\"absent\">none</td>");
            table.Append(line.Margin is { } margin
                ? Formatted($"<td class=\"num\" data-margin=\"{margin.ToString(Invariant)}\">{margin.ToString("0.0", Invariant)}%{(line.MarginYearEarlier is { } then ? Formatted($", <span data-margin-year-earlier=\"{then.ToString(Invariant)}\">{then.ToString("0.0", Invariant)}%</span>") : string.Empty)}</td>")
                : "<td class=\"num\"></td>");
            table.Append("</tr>");
        }

        return table.Append("</table></div>").ToString();
    }

    // A difference in its own unit with its sign always written, in the rise or the fall hue by that sign.
    static string SignedAmount(decimal amount, bool perShare, string currency, string name)
    {
        var hue = amount > 0 ? "up" : amount < 0 ? "down" : "flat";
        var drawn = perShare ? Figures.PerShare(Math.Abs(amount)) : Figures.Money(Math.Abs(amount), currency);

        return Formatted($"<span class=\"{hue}\" data-{name}=\"{amount.ToString(Invariant)}\">{(amount > 0 ? "+" : amount < 0 ? "-" : string.Empty)}{drawn}</span>");
    }

    // Growth bars: each of the eight newest quarters' revenue and earnings a share on the same quarter a year earlier, in
    // per cent on one axis about a zero rule, revenue's bar filled and earnings' drawn as an outline, each in the rise or
    // the fall hue by its sign. Fewer than two quarters holding a growth say so.
    public string GrowthBars(string ticker, IReadOnlyList<QuarterGrowth> growth)
    {
        var read = growth.Where(quarter => quarter.Revenue is not null || quarter.Eps is not null).ToArray();

        if (read.Length < 2)
        {
            return Formatted($"<p class=\"mark-short\" data-mark=\"growth-bars\" data-quarters=\"{read.Length}\">{read.Length} of the quarters stored for {Escaped(ticker)} hold a growth on a year earlier, and the bars need two.</p>");
        }

        var values = read.SelectMany(quarter => new[] { quarter.Revenue, quarter.Eps }).OfType<double>().Append(0).ToArray();
        var (low, high) = Bounds(values.Min(), values.Max());
        double Y(double value) => ChartTop + ((high - value) / (high - low) * (ChartHigh - ChartTop - ChartBottom));

        var slot = (ChartWide - ChartLeft - ChartRight) / read.Length;
        var bar = Math.Min(18, slot / 3);
        var svg = new StringBuilder();

        svg.Append(Formatted($"<svg class=\"growth-bars\" viewBox=\"0 0 {Number(ChartWide)} {Number(ChartHigh)}\" role=\"img\" data-mark=\"growth-bars\" data-quarters=\"{read.Length}\" aria-label=\"Revenue and earnings a share on a year earlier over {read.Length} quarters\">"));
        svg.Append(Legend(("gb-rev", "Revenue on a year earlier"), ("gb-eps", "Earnings a share on a year earlier")));
        svg.Append(Ticks(low, high, Y, "%"));
        svg.Append(Formatted($"<line class=\"m-axisline\" x1=\"{Number(ChartLeft)}\" x2=\"{Number(ChartWide - ChartRight)}\" y1=\"{Number(Y(0))}\" y2=\"{Number(Y(0))}\"/>"));

        for (var at = 0; at < read.Length; at++)
        {
            var quarter = read[at];
            var middle = ChartLeft + (slot * at) + (slot / 2);

            foreach (var (value, offset, kind, said) in new[] { (quarter.Revenue, -bar - 1, "gb-rev", "revenue"), (quarter.Eps, 1d, "gb-eps", "earnings a share") })
            {
                if (value is not { } change)
                {
                    continue;
                }

                var top = Math.Min(Y(change), Y(0));
                var tall = Math.Max(Math.Abs(Y(change) - Y(0)), 1);
                var hue = change >= 0 ? "up" : "down";

                svg.Append(Formatted($"<rect class=\"{kind} {kind}-{hue}\" x=\"{Number(middle + offset)}\" y=\"{Number(top)}\" width=\"{Number(bar)}\" height=\"{Number(tall)}\" rx=\"2\" data-quarter=\"{DayOf(quarter.Quarter)}\" data-{(kind == "gb-rev" ? "revenue" : "eps")}=\"{change.ToString(Invariant)}\"><title>Quarter to {DayOf(quarter.Quarter)}: {said} {change.ToString("+0.0;-0.0;0.0", Invariant)}% on a year earlier</title></rect>"));
            }

            svg.Append(Formatted($"<text class=\"m-tick\" x=\"{Number(middle)}\" y=\"{Number(ChartHigh - 10)}\" text-anchor=\"middle\">{quarter.Quarter.ToString("MMM yy", Invariant)}</text>"));
        }

        return svg.Append("</svg>").ToString();
    }

    // ---- margins ----

    // Margin lines: the gross, operating and net margins of the twelve newest quarters in per cent on one axis, each line
    // named at its end and in the row above. Fewer than two quarters holding a margin say so.
    public string MarginLines(string ticker, IReadOnlyList<QuarterMargins> margins)
    {
        var read = margins.Where(quarter => quarter.Gross is not null || quarter.Operating is not null || quarter.Net is not null).ToArray();

        if (read.Length < 2)
        {
            return Formatted($"<p class=\"mark-short\" data-mark=\"margin-lines\" data-quarters=\"{read.Length}\">{read.Length} of the quarters stored for {Escaped(ticker)} hold a margin, and the lines need two.</p>");
        }

        var values = read.SelectMany(quarter => new[] { quarter.Gross, quarter.Operating, quarter.Net }).OfType<double>().Append(0).ToArray();
        var (low, high) = Bounds(values.Min(), values.Max());
        double Y(double value) => ChartTop + ((high - value) / (high - low) * (ChartHigh - ChartTop - ChartBottom));
        double X(int at) => ChartLeft + ((ChartWide - ChartLeft - ChartRight - 60) * at / Math.Max(read.Length - 1, 1));

        var svg = new StringBuilder();

        svg.Append(Formatted($"<svg class=\"margin-lines\" viewBox=\"0 0 {Number(ChartWide)} {Number(ChartHigh)}\" role=\"img\" data-mark=\"margin-lines\" data-quarters=\"{read.Length}\" aria-label=\"Gross, operating and net margins over {read.Length} quarters\">"));
        svg.Append(Legend(("ml-gross", "Gross margin"), ("ml-operating", "Operating margin"), ("ml-net", "Net margin")));
        svg.Append(Ticks(low, high, Y, "%"));

        foreach (var (kind, said, read1) in new (string, string, Func<QuarterMargins, double?>)[]
                 {
                     ("ml-gross", "gross", quarter => quarter.Gross),
                     ("ml-operating", "operating", quarter => quarter.Operating),
                     ("ml-net", "net", quarter => quarter.Net),
                 })
        {
            var points = read.Select((quarter, at) => (quarter, at, value: read1(quarter))).Where(point => point.value is not null).ToArray();

            if (points.Length == 0)
            {
                continue;
            }

            svg.Append(Formatted($"<polyline class=\"{kind}\" fill=\"none\" points=\"{string.Join(' ', points.Select(point => Number(X(point.at)) + "," + Number(Y(point.value!.Value))))}\"/>"));

            foreach (var point in points)
            {
                svg.Append(Formatted($"<circle class=\"{kind}-dot\" cx=\"{Number(X(point.at))}\" cy=\"{Number(Y(point.value!.Value))}\" r=\"3\" data-quarter=\"{DayOf(point.quarter.Quarter)}\" data-{said}=\"{point.value!.Value.ToString(Invariant)}\"><title>Quarter to {DayOf(point.quarter.Quarter)}: {said} margin {point.value!.Value.ToString("0.0", Invariant)}%</title></circle>"));
            }

            var last = points[^1];

            svg.Append(Formatted($"<text class=\"m-rownote\" x=\"{Number(X(last.at) + 8)}\" y=\"{Number(Y(last.value!.Value) + 4)}\">{Capital(said)} {last.value!.Value.ToString("0.0", Invariant)}%</text>"));
        }

        foreach (var (quarter, at) in read.Select((quarter, at) => (quarter, at)).Where(pair => pair.at % 2 == (read.Length - 1) % 2))
        {
            svg.Append(Formatted($"<text class=\"m-tick\" x=\"{Number(X(at))}\" y=\"{Number(ChartHigh - 10)}\" text-anchor=\"middle\">{quarter.Quarter.ToString("MMM yy", Invariant)}</text>"));
        }

        return svg.Append("</svg>").ToString();
    }

    // The margins as a table beneath the lines, a quarter a row.
    public string MarginTable(IReadOnlyList<QuarterMargins> margins)
    {
        var table = new StringBuilder("<div class=\"tbl-wrap\"><table class=\"margin-table\"><tr><th>Quarter</th><th class=\"num\">Gross</th><th class=\"num\">Operating</th><th class=\"num\">Net</th></tr>");

        foreach (var quarter in margins.Reverse())
        {
            string Cell(double? value, string name) =>
                value is { } held
                    ? Formatted($"<td class=\"num\" data-{name}=\"{held.ToString(Invariant)}\">{held.ToString("0.0", Invariant)}%</td>")
                    : Formatted($"<td class=\"num degraded\" data-{name}=\"absent\">none</td>");

            table.Append(Formatted($"<tr data-quarter=\"{DayOf(quarter.Quarter)}\"><td>{DayOf(quarter.Quarter)}</td>"))
                .Append(Cell(quarter.Gross, "gross"))
                .Append(Cell(quarter.Operating, "operating"))
                .Append(Cell(quarter.Net, "net"))
                .Append("</tr>");
        }

        return table.Append("</table></div>").ToString();
    }

    // ---- segments ----

    // The segment table the rule made: each segment's revenue and its change on a year, its earnings and their change, and
    // its share of the company's revenue, beside the company's total; or why the rows sum to no total, where the raw
    // table drawn beneath stands instead.
    public string SegmentsTable(SegmentReading segments, string currency)
    {
        if (!segments.Clean)
        {
            return Formatted($"<p class=\"degraded\" data-segments=\"raw\" data-report=\"{Escaped(segments.Report)}\" data-total=\"{(segments.Total is { } total ? total.ToString(Invariant) : "none")}\" data-sum=\"{(segments.Sum is { } sum ? sum.ToString(Invariant) : "none")}\">The filing's segment table is drawn as filed beneath, because {Escaped(segments.Raw!)}{(segments.Total is { } whole && segments.Sum is { } summed ? Formatted($": its segments' revenue sums to {Figures.Money(summed, currency)} against the company's {Figures.Money(whole, currency)}") : string.Empty)}.</p>");
        }

        var table = new StringBuilder();

        table.Append(Formatted($"<div class=\"tbl-wrap\"><table class=\"segment-table\" data-report=\"{Escaped(segments.Report)}\" data-quarter=\"{(segments.Quarter is { } on ? DayOf(on) : "none")}\" data-months=\"{segments.Months}\" data-total=\"{segments.Total!.Value.ToString(Invariant)}\" data-sum=\"{segments.Sum!.Value.ToString(Invariant)}\">"));
        table.Append("<tr><th>Segment</th><th class=\"num\">Revenue</th><th class=\"num\">Change on a year</th><th class=\"num\">Operating income</th><th class=\"num\">Change on a year</th><th class=\"num\">Share of revenue</th></tr>");

        foreach (var row in segments.Rows)
        {
            table.Append(Formatted($"<tr data-segment=\"{Escaped(row.Segment)}\"><td>{Escaped(row.Segment)}</td>"));
            table.Append(Formatted($"<td class=\"num\" data-revenue=\"{row.Revenue.ToString(Invariant)}\">{Figures.Money(row.Revenue, currency)}</td>"));
            table.Append(row.RevenueChange is { } revenue ? "<td class=\"num\">" + Signed(revenue, "revenue-change") + "</td>" : "<td class=\"num degraded\">none</td>");
            table.Append(row.Earnings is { } earnings ? Formatted($"<td class=\"num\" data-earnings=\"{earnings.ToString(Invariant)}\">{Figures.Money(earnings, currency)}</td>") : "<td class=\"num degraded\" data-earnings=\"absent\">not filed</td>");
            table.Append(row.EarningsChange is { } change ? "<td class=\"num\">" + Signed(change, "earnings-change") + "</td>" : "<td class=\"num degraded\">none</td>");
            table.Append(Formatted($"<td class=\"num\" data-share=\"{row.Share.ToString(Invariant)}\">{row.Share.ToString("0.0", Invariant)}%</td></tr>"));
        }

        table.Append(Formatted($"<tr class=\"key-row\"><td>The company</td><td class=\"num\">{Figures.Money(segments.Total!.Value, currency)}</td><td class=\"num\"></td><td class=\"num\"></td><td class=\"num\"></td><td class=\"num\">100.0%</td></tr>"));

        return table.Append("</table></div>").ToString();
    }

    // Share bars: each segment's share of the company's revenue as a horizontal bar named with its share and its change on
    // a year in the rise or the fall hue. A table that sums to no total draws none and says so.
    public string ShareBars(SegmentReading segments)
    {
        if (!segments.Clean || segments.Rows.Count == 0)
        {
            return Formatted($"<p class=\"mark-short\" data-mark=\"share-bars\">No segment's share is drawn, because {Escaped(segments.Raw ?? "the table names no segment")}.</p>");
        }

        const double Row = 26;
        const double Label = 220;
        var wide = ChartWide - Label - 120;
        var svg = new StringBuilder();

        svg.Append(Formatted($"<svg class=\"share-bars\" viewBox=\"0 0 {Number(ChartWide)} {Number((segments.Rows.Count * Row) + 8)}\" role=\"img\" data-mark=\"share-bars\" data-segments=\"{segments.Rows.Count}\" aria-label=\"Each segment's share of the company's revenue\">"));

        for (var at = 0; at < segments.Rows.Count; at++)
        {
            var row = segments.Rows[at];
            var y = 4 + (at * Row);

            svg.Append(Formatted($"<text class=\"m-row\" x=\"0\" y=\"{Number(y + 15)}\">{Escaped(Shortened(row.Segment, 30))}</text>"));
            svg.Append(Formatted($"<rect class=\"sb-bar\" x=\"{Number(Label)}\" y=\"{Number(y + 4)}\" width=\"{Number(Math.Max(wide * row.Share / 100, 1))}\" height=\"14\" rx=\"3\" data-share=\"{row.Share.ToString(Invariant)}\"><title>{Escaped(row.Segment)}: {row.Share.ToString("0.0", Invariant)}% of revenue</title></rect>"));
            svg.Append(Formatted($"<text class=\"m-rownote\" x=\"{Number(Label + (wide * row.Share / 100) + 6)}\" y=\"{Number(y + 15)}\">{row.Share.ToString("0.0", Invariant)}%</text>"));

            if (row.RevenueChange is { } change)
            {
                svg.Append(Formatted($"<text class=\"sb-change sb-{(change >= 0 ? "up" : "down")}\" x=\"{Number(ChartWide - 4)}\" y=\"{Number(y + 15)}\" text-anchor=\"end\">{change.ToString("+0.0;-0.0;0.0", Invariant)}%</text>"));
            }
        }

        return svg.Append("</svg>").ToString();
    }

    // ---- analysts ----

    // The analysts' consensus for each period the newest fetch filed, with its range, how many estimate it and its growth
    // on the year before, for earnings a share and revenue, every figure labelled as theirs.
    public string ConsensusTable(AnalystReading analysts, string currency)
    {
        var table = new StringBuilder();

        table.Append(Formatted($"<div class=\"tbl-wrap\"><table class=\"consensus-table\" data-fetched=\"{DayOf(analysts.FetchedOn)}\"><tr><th>The analysts' estimate for</th><th class=\"num\">Earnings a share</th><th class=\"num\">Range</th><th class=\"num\">Analysts</th><th class=\"num\">On the year before</th><th class=\"num\">Revenue</th><th class=\"num\">Range</th><th class=\"num\">On the year before</th></tr>"));

        foreach (var row in analysts.Consensus)
        {
            string Range(decimal? low, decimal? high, bool perShare) =>
                low is { } bottom && high is { } top
                    ? (perShare ? Figures.PerShare(bottom) + " to " + Figures.PerShare(top) : Figures.Money(bottom, currency) + " to " + Figures.Money(top, currency))
                    : "none filed";

            table.Append(Formatted($"<tr data-period=\"{Escaped(row.Period)}\" data-period-end=\"{DayOf(row.PeriodEnd)}\"><td>{PeriodWords(row.Period)} to {DayOf(row.PeriodEnd)}</td>"));
            table.Append(row.Eps is { } eps ? Formatted($"<td class=\"num\" data-eps=\"{eps.ToString(Invariant)}\">{Figures.PerShare(eps)}</td>") : "<td class=\"num degraded\">none filed</td>");
            table.Append("<td class=\"num\">").Append(Range(row.EpsLow, row.EpsHigh, true)).Append("</td>");
            table.Append(row.EpsAnalysts is { } count ? Formatted($"<td class=\"num\" data-analysts=\"{count}\">{count}</td>") : "<td class=\"num degraded\">none filed</td>");
            table.Append(row.EpsGrowth is { } growth ? "<td class=\"num\">" + Signed(growth, "eps-growth") + "</td>" : "<td class=\"num degraded\">none</td>");
            table.Append(row.Revenue is { } revenue ? Formatted($"<td class=\"num\" data-revenue=\"{revenue.ToString(Invariant)}\">{Figures.Money(revenue, currency)}</td>") : "<td class=\"num degraded\">none filed</td>");
            table.Append("<td class=\"num\">").Append(Range(row.RevenueLow, row.RevenueHigh, false)).Append("</td>");
            table.Append(row.RevenueGrowth is { } sales ? "<td class=\"num\">" + Signed(sales, "revenue-growth") + "</td>" : "<td class=\"num degraded\">none</td>");
            table.Append("</tr>");
        }

        table.Append("</table></div>");

        // The revisions beneath: who raised and who cut over 7 and 30 days, and the consensus against 30 days before.
        table.Append("<div class=\"tbl-wrap\"><table class=\"revisions-table\"><tr><th>Revisions to the estimate for</th><th class=\"num\">Raised, 7 days</th><th class=\"num\">Cut, 7 days</th><th class=\"num\">Raised, 30 days</th><th class=\"num\">Cut, 30 days</th><th class=\"num\">On 30 days before</th></tr>");

        foreach (var row in analysts.Revisions)
        {
            string Count(int? value, string name) =>
                value is { } held ? Formatted($"<td class=\"num\" data-{name}=\"{held}\">{held}</td>") : Formatted($"<td class=\"num degraded\" data-{name}=\"absent\">none filed</td>");

            table.Append(Formatted($"<tr data-period=\"{Escaped(row.Period)}\"><td>{PeriodWords(row.Period)}</td>"))
                .Append(Count(row.Up7, "up-7"))
                .Append(Count(row.Down7, "down-7"))
                .Append(Count(row.Up30, "up-30"))
                .Append(Count(row.Down30, "down-30"))
                .Append(row.Change30 is { } change ? "<td class=\"num\">" + Signed(change, "change-30") + "</td>" : "<td class=\"num degraded\">none</td>")
                .Append("</tr>");
        }

        return table.Append("</table></div>").ToString();
    }

    // The provider's word for a period, in the page's.
    static string PeriodWords(string period) => period switch
    {
        EquityBrief.Core.Quarters.EstimatePeriod.CurrentQuarter => "This quarter",
        EquityBrief.Core.Quarters.EstimatePeriod.NextQuarter => "Next quarter",
        EquityBrief.Core.Quarters.EstimatePeriod.CurrentYear => "This fiscal year",
        EquityBrief.Core.Quarters.EstimatePeriod.NextYear => "Next fiscal year",
        _ => throw new InvalidOperationException($"The estimate trend holds a period '{period}' the page has no words for."),
    };

    // Estimate trend: the consensus earnings a share for this fiscal year and the next as each stood 90, 60, 30 and 7 days
    // before the fetch and at it, a line each on one axis, named at its end. A period holding fewer than two points draws
    // no line and the mark says so where neither year holds two.
    public string EstimateTrend(AnalystReading analysts)
    {
        var lines = new[] { EquityBrief.Core.Quarters.EstimatePeriod.CurrentYear, EquityBrief.Core.Quarters.EstimatePeriod.NextYear }
            .Where(period => analysts.Trends.TryGetValue(period, out var points) && points.Count >= 2)
            .Select(period => (Period: period, Points: analysts.Trends[period]))
            .ToArray();

        if (lines.Length == 0)
        {
            return "<p class=\"mark-short\" data-mark=\"estimate-trend\">Neither fiscal year's estimate is filed at two points of the last 90 days, so no trend is drawn.</p>";
        }

        var values = lines.SelectMany(line => line.Points.Select(point => EquityBrief.Core.Prices.Statistic.FromPrice(point.Eps))).ToArray();
        var (low, high) = Bounds(values.Min(), values.Max(), zero: false);
        double Y(double value) => ChartTop + ((high - value) / (high - low) * (ChartHigh - ChartTop - ChartBottom));
        double X(int daysAgo) => ChartLeft + ((ChartWide - ChartLeft - ChartRight - 120) * (90 - daysAgo) / 90);

        var svg = new StringBuilder();

        svg.Append(Formatted($"<svg class=\"estimate-trend\" viewBox=\"0 0 {Number(ChartWide)} {Number(ChartHigh)}\" role=\"img\" data-mark=\"estimate-trend\" data-lines=\"{lines.Length}\" aria-label=\"The analysts' consensus earnings a share over the 90 days to {DayOf(analysts.FetchedOn)}\">"));
        svg.Append(Legend([.. lines.Select((line, at) => ("et-" + at.ToString(Invariant), PeriodWords(line.Period) + ", the analysts' consensus"))]));
        svg.Append(Ticks(low, high, Y, string.Empty));

        for (var at = 0; at < lines.Length; at++)
        {
            var (period, points) = lines[at];

            svg.Append(Formatted($"<polyline class=\"et-{at}\" fill=\"none\" points=\"{string.Join(' ', points.Select(point => Number(X(point.DaysAgo)) + "," + Number(Y(EquityBrief.Core.Prices.Statistic.FromPrice(point.Eps)))))}\"/>"));

            foreach (var point in points)
            {
                svg.Append(Formatted($"<circle class=\"et-{at}-dot\" cx=\"{Number(X(point.DaysAgo))}\" cy=\"{Number(Y(EquityBrief.Core.Prices.Statistic.FromPrice(point.Eps)))}\" r=\"3\" data-period=\"{Escaped(period)}\" data-days-ago=\"{point.DaysAgo}\" data-eps=\"{point.Eps.ToString(Invariant)}\"><title>{PeriodWords(period)}: {Figures.PerShare(point.Eps)} {(point.DaysAgo == 0 ? "at the fetch" : Formatted($"{point.DaysAgo} days before the fetch"))}</title></circle>"));
            }

            var last = points[^1];

            svg.Append(Formatted($"<text class=\"m-rownote\" x=\"{Number(X(last.DaysAgo) + 8)}\" y=\"{Number(Y(EquityBrief.Core.Prices.Statistic.FromPrice(last.Eps)) + 4)}\">{PeriodWords(period)} {Figures.PerShare(last.Eps)}</text>"));
        }

        foreach (var days in EquityBrief.Core.Report.AnalystView.TrendDays)
        {
            svg.Append(Formatted($"<text class=\"m-tick\" x=\"{Number(X(days))}\" y=\"{Number(ChartHigh - 10)}\" text-anchor=\"middle\">{(days == 0 ? "now" : Formatted($"{days} days"))}</text>"));
        }

        return svg.Append("</svg>").ToString();
    }

    // Rating bars: the analysts' five rating counts in each of the twelve months to the night, a stacked bar a month on one
    // ramp from a strong buy in the rise hue through hold in grey to a strong sell in the fall hue, a month no fetch fell in
    // drawn as a dashed outline saying so. A store holding no count in any month says so.
    public string RatingBars(AnalystReading analysts)
    {
        if (!analysts.Months.Any(month => month.Fetch is not null))
        {
            return "<p class=\"mark-short\" data-mark=\"rating-bars\">No fetch in the last twelve months filed the analysts' rating counts, so none is drawn.</p>";
        }

        var most = analysts.Months.Max(month => month.Fetch is { } fetch ? Total(fetch) : 0);
        var slot = (ChartWide - ChartLeft - ChartRight) / analysts.Months.Count;
        var bar = Math.Min(28, slot * 0.6);
        double Tall(int count) => (ChartHigh - ChartTop - ChartBottom) * count / Math.Max(most, 1);

        var svg = new StringBuilder();

        svg.Append(Formatted($"<svg class=\"rating-bars\" viewBox=\"0 0 {Number(ChartWide)} {Number(ChartHigh)}\" role=\"img\" data-mark=\"rating-bars\" data-months=\"{analysts.Months.Count}\" aria-label=\"The analysts' rating counts by month\">"));
        svg.Append(Legend(("rt-strong-buy", "Strong buy"), ("rt-buy", "Buy"), ("rt-hold", "Hold"), ("rt-sell", "Sell"), ("rt-strong-sell", "Strong sell")));

        for (var at = 0; at < analysts.Months.Count; at++)
        {
            var month = analysts.Months[at];
            var x = ChartLeft + (slot * at) + ((slot - bar) / 2);
            var label = new DateOnly(month.Year, month.Month, 1).ToString("MMM yy", Invariant);

            if (month.Fetch is not { } fetch)
            {
                svg.Append(Formatted($"<rect class=\"m-dash\" x=\"{Number(x)}\" y=\"{Number(ChartHigh - ChartBottom - 24)}\" width=\"{Number(bar)}\" height=\"24\" rx=\"2\" data-month=\"{month.Year}-{month.Month:00}\" data-fetched=\"none\"><title>{label}: no fetch this month</title></rect>"));
            }
            else
            {
                var bottom = ChartHigh - ChartBottom;

                foreach (var (kind, count, said) in new[]
                         {
                             ("rt-strong-buy", fetch.StrongBuy ?? 0, "strong buy"),
                             ("rt-buy", fetch.Buy ?? 0, "buy"),
                             ("rt-hold", fetch.Hold ?? 0, "hold"),
                             ("rt-sell", fetch.Sell ?? 0, "sell"),
                             ("rt-strong-sell", fetch.StrongSell ?? 0, "strong sell"),
                         })
                {
                    if (count == 0)
                    {
                        continue;
                    }

                    var tall = Tall(count);

                    bottom -= tall;
                    svg.Append(Formatted($"<rect class=\"{kind}\" x=\"{Number(x)}\" y=\"{Number(bottom)}\" width=\"{Number(bar)}\" height=\"{Number(Math.Max(tall - 1, 1))}\" data-month=\"{month.Year}-{month.Month:00}\" data-{kind[3..]}=\"{count}\"><title>{label}: {count} {said} as fetched on {DayOf(fetch.FetchedOn)}</title></rect>"));
                }
            }

            svg.Append(Formatted($"<text class=\"m-tick\" x=\"{Number(x + (bar / 2))}\" y=\"{Number(ChartHigh - 10)}\" text-anchor=\"middle\">{label}</text>"));
        }

        return svg.Append("</svg>").ToString();

        static int Total(RatingFetch fetch) => (fetch.StrongBuy ?? 0) + (fetch.Buy ?? 0) + (fetch.Hold ?? 0) + (fetch.Sell ?? 0) + (fetch.StrongSell ?? 0);
    }

    // ---- the dividend ----

    // The dividend's safety: the rate and the yield at the price drawn against the 10-year, the payout on earnings and on
    // free cash flow, and how many whole years in a row it was raised.
    public string DividendTable(DividendSafetyReading dividend, decimal? price, bool live, string currency)
    {
        var table = new StringBuilder("<div class=\"tbl-wrap\"><table class=\"dividend-table\">");

        void Row(string label, string cell, string kind) =>
            table.Append(Formatted($"<tr data-row=\"{kind}\"><td>{label}</td><td class=\"num\">{cell}</td></tr>"));

        Row(dividend.RateIsForward ? "Rate a share a year, forward" : "Paid a share over the last year", Formatted($"<span data-rate=\"{dividend.Rate.ToString(Invariant)}\">{Figures.PerShare(dividend.Rate)}</span>"), "rate");
        Row(
            Formatted($"Yield at {(price is { } at ? Figures.Price(at) : "the price")}, {(live ? "the quote" : "the last close")}"),
            dividend.Yield is { } yield ? Formatted($"<span data-yield=\"{yield.ToString(Invariant)}\">{yield.ToString("0.00", Invariant)}%</span>") : "<span class=\"degraded\">no price</span>",
            "yield");
        Row(
            dividend.TenYearOn is { } on ? "The Treasury's 10-year par yield on " + DayOf(on) : "The Treasury's 10-year par yield",
            dividend.TenYear is { } ten ? Formatted($"<span data-ten-year=\"{ten.ToString(Invariant)}\">{ten.ToString("0.00", Invariant)}%</span>") : "<span class=\"degraded\" data-ten-year=\"absent\">not read yet</span>",
            "ten-year");
        Row(
            "The yield against the 10-year",
            dividend.Spread is { } spread ? Formatted($"<span class=\"{(spread >= 0 ? "up" : "down")}\" data-spread=\"{spread.ToString(Invariant)}\">{spread.ToString("+0.00;-0.00;0.00", Invariant)} points</span>") : "<span class=\"degraded\">none</span>",
            "spread");
        Row(
            "Payout on earnings, the rate over the last four quarters' earnings a share",
            dividend.PayoutOnEarnings is { } earnings
                ? Formatted($"<span data-payout-earnings=\"{earnings.ToString(Invariant)}\">{earnings.ToString("0.0", Invariant)}%</span> of {Figures.PerShare(dividend.EarningsTrailing!.Value)}")
                : "<span class=\"degraded\" data-payout-earnings=\"absent\">not read: the last four quarters' earnings are not above nought or not stored</span>",
            "payout-earnings");
        Row(
            "Payout on free cash flow, the last four quarters' dividends paid over their free cash flow",
            dividend.PayoutOnCash is { } cash
                ? Formatted($"<span data-payout-cash=\"{cash.ToString(Invariant)}\">{cash.ToString("0.0", Invariant)}%</span>, {Figures.Money(dividend.DividendsPaid!.Value, currency)} of {Figures.Money(dividend.FreeCashFlow!.Value, currency)}")
                : "<span class=\"degraded\" data-payout-cash=\"absent\">not read: the last four quarters do not all hold their free cash flow and dividends paid, or it is not above nought</span>",
            "payout-cash");
        Row(
            "Whole years in a row it raised the dividend",
            Formatted($"<span data-raised=\"{dividend.RaisedInARow}\">{dividend.RaisedInARow}</span>{(dividend.Years.Count == 0 ? " (no dividend is kept to read years from)" : string.Empty)}"),
            "raised");

        return table.Append("</table></div>").ToString();
    }

    // Dividend bars: the dividends a share each year the store kept, a raise in the rise hue and a cut in the fall hue, a
    // year held level or not compared in grey, and the night's own year paler and starred as so far. Fewer than two years
    // kept say so.
    public string DividendBars(DividendSafetyReading dividend)
    {
        var years = dividend.Years.TakeLast(16).ToArray();

        if (years.Length < 2)
        {
            return Formatted($"<p class=\"mark-short\" data-mark=\"dividend-bars\" data-years=\"{years.Length}\">The store keeps the dividends of {years.Length} year(s), and the bars need two.</p>");
        }

        var most = years.Max(year => EquityBrief.Core.Prices.Statistic.FromPrice(year.Total));
        var slot = (ChartWide - ChartLeft - ChartRight) / years.Length;
        var bar = Math.Min(30, slot * 0.6);
        double Tall(double total) => (ChartHigh - ChartTop - ChartBottom) * total / Math.Max(most, 0.0001);

        var svg = new StringBuilder();

        svg.Append(Formatted($"<svg class=\"dividend-bars\" viewBox=\"0 0 {Number(ChartWide)} {Number(ChartHigh)}\" role=\"img\" data-mark=\"dividend-bars\" data-years=\"{years.Length}\" aria-label=\"The dividends a share each year\">"));
        svg.Append(Legend(("db-raised", "Raised on the year before"), ("db-cut", "Cut"), ("db-level", "Level or not compared")));

        for (var at = 0; at < years.Length; at++)
        {
            var year = years[at];
            var x = ChartLeft + (slot * at) + ((slot - bar) / 2);
            var tall = Math.Max(Tall(EquityBrief.Core.Prices.Statistic.FromPrice(year.Total)), 1);

            // A year compared and not raised is a cut only where it paid less than the year before; one that paid the same
            // held level.
            var before = dividend.Years.FirstOrDefault(earlier => earlier.Year == year.Year - 1);
            var kind = year.Raised switch
            {
                true => "db-raised",
                false when before is not null && year.Total < before.Total => "db-cut",
                _ => "db-level",
            };

            svg.Append(Formatted($"<rect class=\"{kind}{(year.Whole ? string.Empty : " db-partial")}\" x=\"{Number(x)}\" y=\"{Number(ChartHigh - ChartBottom - tall)}\" width=\"{Number(bar)}\" height=\"{Number(tall)}\" rx=\"2\" data-year=\"{year.Year}\" data-total=\"{year.Total.ToString(Invariant)}\" data-payments=\"{year.Payments}\" data-raised=\"{(year.Raised is { } raised ? (raised ? "yes" : "no") : "not compared")}\"><title>{year.Year}: {Figures.PerShare(year.Total)} a share over {year.Payments} payment(s){(year.Whole ? string.Empty : ", so far this year")}</title></rect>"));
            svg.Append(Formatted($"<text class=\"m-tick\" x=\"{Number(x + (bar / 2))}\" y=\"{Number(ChartHigh - 10)}\" text-anchor=\"middle\">{year.Year}{(year.Whole ? string.Empty : "*")}</text>"));
        }

        return svg.Append("</svg>").ToString();
    }

    // Yield lines: the trailing yield, the year's dividends over each session's close, beside the Treasury's 10-year over
    // the sessions either holds, in per cent on one axis. Fewer than two sessions holding either say so.
    public string YieldLines(DividendSafetyReading dividend)
    {
        var points = dividend.Series;

        if (points.Count(point => point.Yield is not null) < 2 && points.Count(point => point.TenYear is not null) < 2)
        {
            return Formatted($"<p class=\"mark-short\" data-mark=\"yield-lines\" data-sessions=\"{points.Count}\">Neither the trailing yield nor the 10-year is held on two sessions of the year, so no line is drawn.</p>");
        }

        var values = points.SelectMany(point => new[] { point.Yield, point.TenYear }).OfType<double>().Append(0).ToArray();
        var (low, high) = Bounds(values.Min(), values.Max());
        double Y(double value) => ChartTop + ((high - value) / (high - low) * (ChartHigh - ChartTop - ChartBottom));
        var first = points[0].Session.DayNumber;
        var span = Math.Max(points[^1].Session.DayNumber - first, 1);
        double X(DateOnly session) => ChartLeft + ((ChartWide - ChartLeft - ChartRight - 90) * (session.DayNumber - first) / span);

        var svg = new StringBuilder();

        svg.Append(Formatted($"<svg class=\"yield-lines\" viewBox=\"0 0 {Number(ChartWide)} {Number(ChartHigh)}\" role=\"img\" data-mark=\"yield-lines\" data-sessions=\"{points.Count}\" aria-label=\"The trailing dividend yield against the Treasury's 10-year\">"));
        svg.Append(Legend(("yl-yield", "Trailing dividend yield"), ("yl-ten", "The Treasury's 10-year")));
        svg.Append(Ticks(low, high, Y, "%"));

        foreach (var (kind, said, read) in new (string, string, Func<YieldPoint, double?>)[]
                 {
                     ("yl-yield", "yield", point => point.Yield),
                     ("yl-ten", "10-year", point => point.TenYear),
                 })
        {
            var held = points.Where(point => read(point) is not null).ToArray();

            if (held.Length < 2)
            {
                continue;
            }

            svg.Append(Formatted($"<polyline class=\"{kind}\" fill=\"none\" data-sessions=\"{held.Length}\" points=\"{string.Join(' ', held.Select(point => Number(X(point.Session)) + "," + Number(Y(read(point)!.Value))))}\"/>"));

            var last = held[^1];

            svg.Append(Formatted($"<circle class=\"{kind}-dot\" cx=\"{Number(X(last.Session))}\" cy=\"{Number(Y(read(last)!.Value))}\" r=\"3\" data-session=\"{DayOf(last.Session)}\" data-{(kind == "yl-yield" ? "yield" : "ten-year")}=\"{read(last)!.Value.ToString(Invariant)}\"><title>{Capital(said)} {read(last)!.Value.ToString("0.00", Invariant)}% on {DayOf(last.Session)}</title></circle>"));
            svg.Append(Formatted($"<text class=\"m-rownote\" x=\"{Number(X(last.Session) + 8)}\" y=\"{Number(Y(read(last)!.Value) + 4)}\">{Capital(said)} {read(last)!.Value.ToString("0.00", Invariant)}%</text>"));
        }

        svg.Append(Formatted($"<text class=\"m-tick\" x=\"{Number(ChartLeft)}\" y=\"{Number(ChartHigh - 10)}\">{DayOf(points[0].Session)}</text>"));
        svg.Append(Formatted($"<text class=\"m-tick\" x=\"{Number(ChartWide - ChartRight - 90)}\" y=\"{Number(ChartHigh - 10)}\" text-anchor=\"end\">{DayOf(points[^1].Session)}</text>"));

        return svg.Append("</svg>").ToString();
    }

    // ---- valuation ----

    // Multiple band: each quarter's own multiple as a line within the band of their low and high, and the night's multiple
    // as a dot at the right. Fewer than two quarters holding a multiple say so.
    public string MultipleBand(string ticker, ValuationView valuation)
    {
        var quarters = valuation.Quarters;

        if (quarters.Count < 2)
        {
            return Formatted($"<p class=\"mark-short\" data-mark=\"multiple-band\" data-quarters=\"{quarters.Count}\">{quarters.Count} of the quarters stored for {Escaped(ticker)} hold a multiple, and the band needs two.</p>");
        }

        var values = quarters.Select(quarter => EquityBrief.Core.Prices.Statistic.FromRatio(quarter.Multiple))
            .Concat(valuation.Multiple is { } night ? [night] : [])
            .ToArray();
        var (low, high) = Bounds(values.Min(), values.Max(), zero: false);
        double Y(double value) => ChartTop + ((high - value) / (high - low) * (ChartHigh - ChartTop - ChartBottom));
        double X(int at) => ChartLeft + ((ChartWide - ChartLeft - ChartRight - 110) * at / Math.Max(quarters.Count, 1));

        var bandLow = quarters.Min(quarter => EquityBrief.Core.Prices.Statistic.FromRatio(quarter.Multiple));
        var bandHigh = quarters.Max(quarter => EquityBrief.Core.Prices.Statistic.FromRatio(quarter.Multiple));
        var svg = new StringBuilder();

        svg.Append(Formatted($"<svg class=\"multiple-band\" viewBox=\"0 0 {Number(ChartWide)} {Number(ChartHigh)}\" role=\"img\" data-mark=\"multiple-band\" data-quarters=\"{quarters.Count}\" aria-label=\"The multiple of each quarter within their range, and tonight's\">"));
        svg.Append(Legend(("mb-line", "Each quarter's multiple"), ("mb-band", "Their range"), ("mb-night", "Tonight's multiple")));
        svg.Append(Ticks(low, high, Y, "x"));
        svg.Append(Formatted($"<rect class=\"mb-band\" x=\"{Number(ChartLeft)}\" y=\"{Number(Y(bandHigh))}\" width=\"{Number(X(quarters.Count) - ChartLeft)}\" height=\"{Number(Math.Max(Y(bandLow) - Y(bandHigh), 1))}\" data-low=\"{bandLow.ToString(Invariant)}\" data-high=\"{bandHigh.ToString(Invariant)}\"/>"));
        svg.Append(Formatted($"<polyline class=\"mb-line\" fill=\"none\" points=\"{string.Join(' ', quarters.Select((quarter, at) => Number(X(at)) + "," + Number(Y(EquityBrief.Core.Prices.Statistic.FromRatio(quarter.Multiple)))))}\"/>"));

        for (var at = 0; at < quarters.Count; at++)
        {
            var multiple = EquityBrief.Core.Prices.Statistic.FromRatio(quarters[at].Multiple);

            svg.Append(Formatted($"<circle class=\"mb-dot\" cx=\"{Number(X(at))}\" cy=\"{Number(Y(multiple))}\" r=\"3\" data-quarter=\"{DayOf(quarters[at].Quarter)}\" data-multiple=\"{quarters[at].Multiple.ToString(Invariant)}\"><title>Quarter to {DayOf(quarters[at].Quarter)}: {multiple.ToString("0.0", Invariant)} times its four quarters' earnings</title></circle>"));
        }

        if (valuation.Multiple is { } tonight)
        {
            svg.Append(Formatted($"<circle class=\"mb-night\" cx=\"{Number(X(quarters.Count))}\" cy=\"{Number(Y(tonight))}\" r=\"5\" data-multiple=\"{tonight.ToString(Invariant)}\"><title>Tonight: {tonight.ToString("0.0", Invariant)} times</title></circle>"));
            svg.Append(Formatted($"<text class=\"m-rownote\" x=\"{Number(X(quarters.Count) + 10)}\" y=\"{Number(Y(tonight) + 4)}\">Tonight {tonight.ToString("0.0", Invariant)}x</text>"));
        }

        svg.Append(Formatted($"<text class=\"m-tick\" x=\"{Number(ChartLeft)}\" y=\"{Number(ChartHigh - 10)}\">{DayOf(quarters[0].Quarter)}</text>"));
        svg.Append(Formatted($"<text class=\"m-tick\" x=\"{Number(X(quarters.Count))}\" y=\"{Number(ChartHigh - 10)}\" text-anchor=\"middle\">tonight</text>"));

        return svg.Append("</svg>").ToString();
    }

    // Peer dots: each member's multiple as a dot on one line, the stock's own larger and named, and the median a rule.
    // Fewer than two members holding a multiple say so.
    public string PeerDots(string ticker, PeerTable peers)
    {
        var held = peers.Rows.Where(row => row.Multiple is not null).ToArray();

        if (held.Length < 2)
        {
            return Formatted($"<p class=\"mark-short\" data-mark=\"peer-dots\" data-members=\"{held.Length}\">{held.Length} of the members read in {Escaped(peers.Industry)} hold a multiple tonight, and the dots need two.</p>");
        }

        const double High = 70;
        var (low, top) = Bounds(held.Min(row => row.Multiple!.Value), held.Max(row => row.Multiple!.Value), zero: false);
        double X(double value) => ChartLeft + ((ChartWide - ChartLeft - ChartRight) * (value - low) / (top - low));

        var svg = new StringBuilder();

        svg.Append(Formatted($"<svg class=\"peer-dots\" viewBox=\"0 0 {Number(ChartWide)} {Number(High)}\" role=\"img\" data-mark=\"peer-dots\" data-members=\"{held.Length}\" aria-label=\"Each member's multiple in {Escaped(peers.Industry)}\">"));
        svg.Append(Formatted($"<line class=\"m-axisline\" x1=\"{Number(ChartLeft)}\" x2=\"{Number(ChartWide - ChartRight)}\" y1=\"34\" y2=\"34\"/>"));

        if (peers.MedianMultiple is { } median)
        {
            svg.Append(Formatted($"<line class=\"pd-median\" x1=\"{Number(X(median))}\" x2=\"{Number(X(median))}\" y1=\"18\" y2=\"50\" data-median=\"{median.ToString(Invariant)}\"><title>The median, {median.ToString("0.0", Invariant)} times</title></line>"));
            svg.Append(Formatted($"<text class=\"m-tick\" x=\"{Number(X(median))}\" y=\"62\" text-anchor=\"middle\">median {median.ToString("0.0", Invariant)}x</text>"));
        }

        foreach (var row in held.OrderBy(row => row.Own))
        {
            svg.Append(Formatted($"<circle class=\"{(row.Own ? "pd-own" : "pd-dot")}\" cx=\"{Number(X(row.Multiple!.Value))}\" cy=\"34\" r=\"{(row.Own ? 7 : 4)}\" data-ticker=\"{Escaped(row.Ticker)}\" data-multiple=\"{row.Multiple!.Value.ToString(Invariant)}\"><title>{Escaped(row.Ticker)}: {row.Multiple!.Value.ToString("0.0", Invariant)} times</title></circle>"));

            if (row.Own)
            {
                svg.Append(Formatted($"<text class=\"m-row\" x=\"{Number(X(row.Multiple!.Value))}\" y=\"16\" text-anchor=\"middle\">{Escaped(ticker)} {row.Multiple!.Value.ToString("0.0", Invariant)}x</text>"));
            }
        }

        return svg.Append("</svg>").ToString();
    }

    // The peer table: the stock's own row marked and each member's multiple, yield and sales growth, then the median row.
    public string PeerTable(PeerTable peers)
    {
        var table = new StringBuilder();

        table.Append(Formatted($"<div class=\"tbl-wrap\"><table class=\"peer-value-table\" data-industry=\"{Escaped(peers.Industry)}\" data-members=\"{peers.Rows.Count}\"><tr><th>Member</th><th class=\"num\">Multiple of four quarters' earnings</th><th class=\"num\">Dividend yield</th><th class=\"num\">Sales on a year</th></tr>"));

        foreach (var row in peers.Rows)
        {
            table.Append(Formatted($"<tr data-ticker=\"{Escaped(row.Ticker)}\"{(row.Own ? " class=\"key-row\" data-own=\"yes\"" : string.Empty)}><td><a href=\"#/name/{Escaped(row.Ticker)}\">{Escaped(row.Ticker)}</a></td>"));
            table.Append(row.Multiple is { } multiple ? Formatted($"<td class=\"num\" data-multiple=\"{multiple.ToString(Invariant)}\">{multiple.ToString("0.0", Invariant)}</td>") : "<td class=\"num degraded\">none read</td>");
            table.Append(row.Yield is { } yield ? Formatted($"<td class=\"num\" data-yield=\"{yield.ToString(Invariant)}\">{yield.ToString("0.00", Invariant)}%</td>") : "<td class=\"num\">none</td>");
            table.Append(row.SalesGrowth is { } growth ? "<td class=\"num\">" + Signed(growth, "sales-growth") + "</td>" : "<td class=\"num degraded\">none read</td>");
            table.Append("</tr>");
        }

        string Median(double? value, int count, string format, string unit) =>
            value is { } middle ? Formatted($"{middle.ToString(format, Invariant)}{unit} of {count}") : "none";

        table.Append(Formatted($"<tr class=\"group-row\" data-row=\"median\"><td>The median</td><td class=\"num\">{Median(peers.MedianMultiple, peers.Multiples, "0.0", string.Empty)}</td><td class=\"num\">{Median(peers.MedianYield, peers.Yields, "0.00", "%")}</td><td class=\"num\">{Median(peers.MedianGrowth, peers.Growths, "0.0", "%")}</td></tr>"));

        return table.Append("</table></div>").ToString();
    }

    // ---- what the charts share ----

    // A row above a chart naming each series it draws, a swatch beside each name.
    static string Legend(params (string Kind, string Said)[] series)
    {
        var row = new StringBuilder();
        var x = ChartLeft;

        foreach (var (kind, said) in series)
        {
            row.Append(Formatted($"<rect class=\"{kind} lg-swatch\" x=\"{Number(x)}\" y=\"6\" width=\"10\" height=\"10\" rx=\"2\"/><text class=\"m-legend-t\" x=\"{Number(x + 14)}\" y=\"15\">{Escaped(said)}</text>"));
            x += 24 + (said.Length * 5.6);
        }

        return row.ToString();
    }

    // Four ticks on the axis at the left with their values, and a light rule across the plot at each.
    string Ticks(double low, double high, Func<double, double> y, string unit)
    {
        var ticks = new StringBuilder();

        for (var step = 0; step <= 3; step++)
        {
            var value = low + ((high - low) * step / 3);

            ticks.Append(Formatted($"<line class=\"m-gridline\" x1=\"{Number(ChartLeft)}\" x2=\"{Number(ChartWide - ChartRight)}\" y1=\"{Number(y(value))}\" y2=\"{Number(y(value))}\"/>"));
            ticks.Append(Formatted($"<text class=\"m-tick\" x=\"{Number(ChartLeft - 6)}\" y=\"{Number(y(value) + 4)}\" text-anchor=\"end\">{value.ToString(Math.Abs(high - low) < 3 ? "0.00" : "0.#", Invariant)}{unit}</text>"));
        }

        return ticks.ToString();
    }

    // An axis's ends with a little room past the values, including nought where the values straddle it or zero is asked for.
    static (double Low, double High) Bounds(double low, double high, bool zero = true)
    {
        if (zero)
        {
            low = Math.Min(low, 0);
            high = Math.Max(high, 0);
        }

        var room = Math.Max((high - low) * 0.08, 0.5);

        return (low - (zero && low == 0 ? 0 : room), high + room);
    }

    static string Capital(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text[1..];

    static string Shortened(string text, int most) => text.Length <= most ? text : text[..(most - 1)] + "…";
}
