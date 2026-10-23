using System.Text;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Report;

namespace EquityBrief.Web.Marks;

// From 20.12 the chart page's graphs beneath its chart, each from a figure the report reads: the newest prints' earnings
// a share against the estimate filed before each, the newest quarters' revenue against net income with the net margin,
// and the analysts' mean target against the price drawn, the analysts' ratings by month being the report's own bars.
// see: Marks are defined once and every screen draws from that list
// see: The chart page graphs the report's earnings and revenue beneath its chart with the analysts' figures Yahoo answers when it opens, and no rule reads them from it
public sealed partial class MarkRenderer
{
    // The room beneath a graph's plot for its labels' two rows.
    const double LabelRows = 16;

    // Earnings dots: each of the newest prints in a column, its estimate a hollow dot and its reported earnings a share a
    // filled one, beat or miss beneath by the difference with its sign in the rise or the fall hue, a print with no filed
    // estimate its reported figure alone saying so, and the quarter to report next its consensus alone as a dashed dot.
    // Fewer than two prints holding a reported figure say so.
    public string EarningsDots(string ticker, IReadOnlyList<EarningsPrint> prints, (DateOnly Ends, decimal Eps)? next)
    {
        var read = prints.Where(print => print.Actual is not null).ToArray();

        if (read.Length < 2)
        {
            return Formatted($"<p class=\"mark-short\" data-mark=\"earnings-dots\" data-prints=\"{read.Length}\">{read.Length} of the prints stored for {Escaped(ticker)} hold the earnings a share reported, and the dots need two.</p>");
        }

        var columns = read.Length + (next is null ? 0 : 1);
        var values = read
            .SelectMany(print => new[] { print.Estimate, print.Actual })
            .OfType<decimal>()
            .Concat(next is { } ahead ? [ahead.Eps] : [])
            .Select(Statistic.FromPrice)
            .ToArray();
        var (low, high) = Bounds(values.Min(), values.Max(), zero: false);
        double Y(double value) => ChartTop + ((high - value) / (high - low) * (ChartHigh - ChartTop - ChartBottom - LabelRows));
        var slot = (ChartWide - ChartLeft - ChartRight) / columns;
        var svg = new StringBuilder();

        svg.Append(Formatted($"<svg class=\"earnings-dots\" viewBox=\"0 0 {Number(ChartWide)} {Number(ChartHigh)}\" role=\"img\" data-mark=\"earnings-dots\" data-prints=\"{read.Length}\" data-next=\"{(next is null ? "none" : DayOf(next.Value.Ends))}\" aria-label=\"Earnings a share reported against the estimate over {read.Length} prints\">"));
        svg.Append(Legend(("ed-estimate", "The estimate before the report"), ("ed-actual", "Reported")));
        svg.Append(Ticks(low, high, Y, string.Empty));

        for (var at = 0; at < read.Length; at++)
        {
            var print = read[at];
            var middle = ChartLeft + (slot * at) + (slot / 2);
            var actual = print.Actual!.Value;

            if (print.Estimate is { } estimate)
            {
                svg.Append(Formatted($"<circle class=\"ed-estimate\" cx=\"{Number(middle)}\" cy=\"{Number(Y(Statistic.FromPrice(estimate)))}\" r=\"5\" data-report-date=\"{DayOf(print.ReportDate)}\" data-estimate=\"{estimate.ToString(Invariant)}\"><title>Reported {DayOf(print.ReportDate)}: the estimate before it {Figures.PerShare(estimate)}</title></circle>"));
            }

            var difference = print.Difference;
            var side = difference is not { } moved ? "none" : moved > 0 ? "up" : moved < 0 ? "down" : "flat";
            var against = difference is { } by
                ? by > 0 ? Formatted($", beating the estimate of {Figures.PerShare(print.Estimate!.Value)} by {Figures.PerShare(by)}")
                    : by < 0 ? Formatted($", missing the estimate of {Figures.PerShare(print.Estimate!.Value)} by {Figures.PerShare(-by)}")
                    : Formatted($", in line with the estimate of {Figures.PerShare(print.Estimate!.Value)}")
                : ", no estimate filed before it";

            svg.Append(Formatted($"<circle class=\"ed-actual ed-actual-{side}\" cx=\"{Number(middle)}\" cy=\"{Number(Y(Statistic.FromPrice(actual)))}\" r=\"5\" data-report-date=\"{DayOf(print.ReportDate)}\" data-actual=\"{actual.ToString(Invariant)}\"{(difference is { } held ? Formatted($" data-difference=\"{held.ToString(Invariant)}\"") : string.Empty)}><title>Reported {DayOf(print.ReportDate)}: earnings a share {Figures.PerShare(actual)}{against}</title></circle>"));
            svg.Append(Formatted($"<text class=\"m-tick\" x=\"{Number(middle)}\" y=\"{Number(ChartHigh - LabelRows - 8)}\" text-anchor=\"middle\">{print.ReportDate.ToString("MMM yy", Invariant)}</text>"));
            svg.Append(difference is { } shown
                ? Formatted($"<text class=\"ed-result {(shown > 0 ? "gain" : shown < 0 ? "loss" : "flat")}\" x=\"{Number(middle)}\" y=\"{Number(ChartHigh - 8)}\" text-anchor=\"middle\" data-difference=\"{shown.ToString(Invariant)}\">{(shown > 0 ? "Beat +" + Figures.PerShare(shown) : shown < 0 ? "Missed -" + Figures.PerShare(-shown) : "In line")}</text>")
                : Formatted($"<text class=\"m-tick\" x=\"{Number(middle)}\" y=\"{Number(ChartHigh - 8)}\" text-anchor=\"middle\" data-difference=\"none\">No estimate</text>"));
        }

        if (next is { } coming)
        {
            var middle = ChartLeft + (slot * read.Length) + (slot / 2);

            svg.Append(Formatted($"<circle class=\"ed-next\" cx=\"{Number(middle)}\" cy=\"{Number(Y(Statistic.FromPrice(coming.Eps)))}\" r=\"5\" data-ends=\"{DayOf(coming.Ends)}\" data-estimate=\"{coming.Eps.ToString(Invariant)}\"><title>The quarter to {DayOf(coming.Ends)}, not yet reported: the analysts' consensus {Figures.PerShare(coming.Eps)}</title></circle>"));
            svg.Append(Formatted($"<text class=\"m-tick\" x=\"{Number(middle)}\" y=\"{Number(ChartHigh - LabelRows - 8)}\" text-anchor=\"middle\">Next</text>"));
            svg.Append(Formatted($"<text class=\"m-tick\" x=\"{Number(middle)}\" y=\"{Number(ChartHigh - 8)}\" text-anchor=\"middle\">Est. {Figures.PerShare(coming.Eps)}</text>"));
        }

        return svg.Append("</svg>").ToString();
    }

    // Income bars: each of the newest quarters' revenue and net income as filed, two bars a quarter on one axis of money
    // about a zero rule, net income in the rise or the fall hue by its sign, and the net margin a line on its own axis at
    // the right. Fewer than two quarters holding a revenue say so.
    public string IncomeBars(string ticker, IReadOnlyList<QuarterIncome> income, string currency)
    {
        var read = income.Where(quarter => quarter.Revenue is not null).ToArray();

        if (read.Length < 2)
        {
            return Formatted($"<p class=\"mark-short\" data-mark=\"income-bars\" data-quarters=\"{read.Length}\">{read.Length} of the quarters stored for {Escaped(ticker)} hold a revenue, and the bars need two.</p>");
        }

        // The room at the right holds the margin's axis.
        const double Margins = 44;
        var money = read.SelectMany(quarter => new[] { quarter.Revenue, quarter.NetIncome }).OfType<decimal>().Select(Statistic.FromPrice).Append(0).ToArray();
        var (low, high) = Bounds(money.Min(), money.Max());
        var plotBottom = ChartHigh - ChartBottom;
        double Y(double value) => ChartTop + ((high - value) / (high - low) * (plotBottom - ChartTop));
        var margins = read.Select(quarter => quarter.Net).OfType<double>().ToArray();
        var (marginLow, marginHigh) = margins.Length > 0 ? Bounds(margins.Min(), margins.Max()) : (0d, 1d);
        double M(double percent) => ChartTop + ((marginHigh - percent) / (marginHigh - marginLow) * (plotBottom - ChartTop));
        var slot = (ChartWide - ChartLeft - ChartRight - Margins) / read.Length;
        var bar = Math.Min(16, slot / 3);
        double Middle(int at) => ChartLeft + (slot * at) + (slot / 2);
        var svg = new StringBuilder();

        svg.Append(Formatted($"<svg class=\"income-bars\" viewBox=\"0 0 {Number(ChartWide)} {Number(ChartHigh)}\" role=\"img\" data-mark=\"income-bars\" data-quarters=\"{read.Length}\" aria-label=\"Revenue and net income over {read.Length} quarters with the net margin\">"));
        svg.Append(Legend([
            ("ib-revenue", "Revenue"),
            .. read.Any(quarter => quarter.NetIncome is not null) ? [("ib-income", "Net income")] : Array.Empty<(string, string)>(),
            .. margins.Length > 0 ? [("ib-margin", "Net margin, at the right")] : Array.Empty<(string, string)>(),
        ]));

        foreach (var value in TickValues(low, high))
        {
            svg.Append(Formatted($"<line class=\"m-gridline\" x1=\"{Number(ChartLeft)}\" x2=\"{Number(ChartWide - ChartRight - Margins)}\" y1=\"{Number(Y(value))}\" y2=\"{Number(Y(value))}\"/>"));
            svg.Append(Formatted($"<text class=\"m-tick\" x=\"{Number(ChartLeft - 6)}\" y=\"{Number(Y(value) + 4)}\" text-anchor=\"end\">{Figures.Money(Statistic.ToPrice(value), currency)}</text>"));
        }

        if (margins.Length > 0)
        {
            foreach (var value in TickValues(marginLow, marginHigh))
            {
                svg.Append(Formatted($"<text class=\"m-tick ib-margin-tick\" x=\"{Number(ChartWide - ChartRight)}\" y=\"{Number(M(value) + 4)}\" text-anchor=\"end\">{Unsigned(value, 4).ToString("0.####", Invariant)}%</text>"));
            }
        }

        svg.Append(Formatted($"<line class=\"m-axisline\" x1=\"{Number(ChartLeft)}\" x2=\"{Number(ChartWide - ChartRight - Margins)}\" y1=\"{Number(Y(0))}\" y2=\"{Number(Y(0))}\"/>"));

        for (var at = 0; at < read.Length; at++)
        {
            var quarter = read[at];
            var middle = Middle(at);
            var revenue = quarter.Revenue!.Value;
            var top = Y(Statistic.FromPrice(revenue));

            svg.Append(Formatted($"<rect class=\"ib-revenue\" x=\"{Number(middle - bar - 1)}\" y=\"{Number(Math.Min(top, Y(0)))}\" width=\"{Number(bar)}\" height=\"{Number(Math.Max(Math.Abs(top - Y(0)), 1))}\" rx=\"2\" data-quarter=\"{DayOf(quarter.Quarter)}\" data-revenue=\"{revenue.ToString(Invariant)}\"><title>Quarter to {DayOf(quarter.Quarter)}: revenue {Figures.Money(revenue, currency)}</title></rect>"));

            if (quarter.NetIncome is { } net)
            {
                var y = Y(Statistic.FromPrice(net));

                svg.Append(Formatted($"<rect class=\"ib-income {(net >= 0 ? "ib-income-up" : "ib-income-down")}\" x=\"{Number(middle + 1)}\" y=\"{Number(Math.Min(y, Y(0)))}\" width=\"{Number(bar)}\" height=\"{Number(Math.Max(Math.Abs(y - Y(0)), 1))}\" rx=\"2\" data-quarter=\"{DayOf(quarter.Quarter)}\" data-income=\"{net.ToString(Invariant)}\"><title>Quarter to {DayOf(quarter.Quarter)}: net income {Figures.Money(net, currency)}</title></rect>"));
            }

            svg.Append(Formatted($"<text class=\"m-tick\" x=\"{Number(middle)}\" y=\"{Number(ChartHigh - 10)}\" text-anchor=\"middle\">{(at % 2 == (read.Length - 1) % 2 ? quarter.Quarter.ToString("MMM yy", Invariant) : string.Empty)}</text>"));
        }

        // The net margin a line over the quarters holding one, each point whole with a title.
        var points = read.Select((quarter, at) => (quarter, at)).Where(pair => pair.quarter.Net is not null).ToArray();

        if (points.Length > 0)
        {
            svg.Append(Formatted($"<polyline class=\"ib-margin\" fill=\"none\" points=\"{string.Join(' ', points.Select(point => Number(Middle(point.at)) + "," + Number(M(point.quarter.Net!.Value))))}\"/>"));

            foreach (var (quarter, at) in points)
            {
                svg.Append(Formatted($"<circle class=\"ib-margin-dot\" cx=\"{Number(Middle(at))}\" cy=\"{Number(M(quarter.Net!.Value))}\" r=\"3\" data-quarter=\"{DayOf(quarter.Quarter)}\" data-net=\"{quarter.Net!.Value.ToString(Invariant)}\"><title>Quarter to {DayOf(quarter.Quarter)}: net margin {Tenth(quarter.Net!.Value)}%</title></circle>"));
            }
        }

        svg.Append("</svg>");

        return points.Length > 0
            ? svg.ToString()
            : svg.Append("<p class=\"mark-note\" data-margin-absent=\"net\">No quarter of these files a net income, so no net margin is drawn.</p>").ToString();
    }

    // Target marker: the analysts' mean target and the price drawn on one line of prices, the target named with how far
    // it stands above or below the price in the rise or the fall hue by its sign, and dated by the fetch as the report
    // dates it; the provider files no low or high target, and the line says so. A store holding no target says so.
    public string TargetMarker(string ticker, AnalystReading? analysts, decimal? price)
    {
        if (analysts?.Target is not { } target || price is not { } drawn || drawn <= 0)
        {
            return Formatted($"<p class=\"mark-short\" data-mark=\"target-marker\">{(analysts?.Target is null ? Formatted($"No fetch filed the analysts' mean target for {Escaped(ticker)}, so none is drawn.") : Formatted($"No price is stored for {Escaped(ticker)} to set the analysts' mean target against."))}</p>");
        }

        const double High = 78;
        var ends = new[] { Statistic.FromPrice(target), Statistic.FromPrice(drawn) };
        var (low, top) = Bounds(ends.Min(), ends.Max(), zero: false);
        double X(double value) => ChartLeft + ((value - low) / (top - low) * (ChartWide - ChartLeft - ChartRight - 60));
        var against = analysts.TargetAgainstPrice;
        var side = against is { } percent ? percent > 0 ? "gain" : percent < 0 ? "loss" : "flat" : "flat";
        var svg = new StringBuilder();

        svg.Append(Formatted($"<svg class=\"target-marker\" viewBox=\"0 0 {Number(ChartWide)} {Number(High)}\" role=\"img\" data-mark=\"target-marker\" data-target=\"{target.ToString(Invariant)}\" data-price=\"{drawn.ToString(Invariant)}\" aria-label=\"The analysts' mean target against the price drawn\">"));
        svg.Append(Formatted($"<line class=\"m-axisline\" x1=\"{Number(ChartLeft)}\" x2=\"{Number(ChartWide - ChartRight - 60)}\" y1=\"40\" y2=\"40\"/>"));
        svg.Append(Formatted($"<circle class=\"tm-price\" cx=\"{Number(X(Statistic.FromPrice(drawn)))}\" cy=\"40\" r=\"6\"><title>The price drawn: {Figures.Price(drawn)}</title></circle>"));
        svg.Append(Formatted($"<text class=\"m-tick\" x=\"{Number(X(Statistic.FromPrice(drawn)))}\" y=\"64\" text-anchor=\"middle\">Price {Figures.Price(drawn)}</text>"));
        svg.Append(Formatted($"<rect class=\"tm-target\" x=\"{Number(X(Statistic.FromPrice(target)) - 6)}\" y=\"34\" width=\"12\" height=\"12\" rx=\"2\"><title>The analysts' mean target as fetched on {DayOf(analysts.FetchedOn)}: {Figures.Price(target)}</title></rect>"));
        svg.Append(Formatted($"<text class=\"m-tick\" x=\"{Number(X(Statistic.FromPrice(target)))}\" y=\"24\" text-anchor=\"middle\">Mean target {Figures.Price(target)}</text>"));
        svg.Append(against is { } away
            ? Formatted($"<text class=\"tm-against {side}\" x=\"{Number(ChartWide - ChartRight)}\" y=\"44\" text-anchor=\"end\" data-against=\"{away.ToString("R", Invariant)}\">{SignedNumber(away)}%</text>")
            : string.Empty);
        svg.Append("</svg>");
        svg.Append(Formatted($"<p class=\"mark-note\" data-fetched=\"{DayOf(analysts.FetchedOn)}\">The analysts' mean target as fetched on {DayOf(analysts.FetchedOn)}; the provider files no low or high target.</p>"));

        return svg.ToString();
    }

    // A per-cent with its sign always written, to one place, nought written bare.
    static string SignedNumber(double percent) =>
        Unsigned(percent, 1) is var shown && shown > 0 ? "+" + shown.ToString("0.0", Invariant) : shown.ToString("0.0", Invariant);
}
