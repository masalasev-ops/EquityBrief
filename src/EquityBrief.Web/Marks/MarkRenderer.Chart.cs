using System.Text;

namespace EquityBrief.Web.Marks;

// One candle the chart page draws: the first and the last session it rolls up, its prices and volume, and the 20, 50
// and 200 session averages at its last session as the name page reads them, none where neither the indicator rows nor
// the chart's averages hold one.
public sealed record ChartCandle(DateOnly First, DateOnly Last, decimal Open, decimal High, decimal Low, decimal Close, long Volume, double? Sma20, double? Sma50, double? Sma200);

// A rule listing the stock tonight as the chart's lines: the rule's name, the evening's close its plan is set at, which
// the pick buys from at the next open, and its stop and its target where it sets them.
public sealed record ChartPlan(string Rule, decimal Buy, decimal? Stop, decimal? Target);

// The chart page's chart, drawn on the server as every mark is.
// see: Marks are defined once and every screen draws from that list
public sealed partial class MarkRenderer
{
    // The chart page's route, which every link that opens a stock opens first.
    // see: Every link that opens a stock opens its chart page first, and the chart page links to its report with the controls that write it again
    public const string ChartRoute = "#/chart/";

    // A link opening a stock's chart page.
    public static string ChartOf(string ticker) => ChartRoute + Uri.EscapeDataString(ticker);

    // The averages the chart page draws, each with the words its legend names it by.
    static readonly (string Name, string Words)[] PageAverages =
    [
        ("sma20", "20-session average"),
        ("sma50", "50-session average"),
        ("sma200", "200-session average"),
    ];

    static double? AverageOf(ChartCandle candle, string name) => name switch
    {
        "sma20" => candle.Sma20,
        "sma50" => candle.Sma50,
        _ => candle.Sma200,
    };

    // The candle's change on the one before it, in price and in per cent, none for the first.
    static (decimal Change, double Percent)? ChangeOf(IReadOnlyList<ChartCandle> candles, int at) =>
        at > 0 && candles[at - 1].Close > 0
            ? (candles[at].Close - candles[at - 1].Close, EquityBrief.Core.Prices.Statistic.FromRatio(((candles[at].Close / candles[at - 1].Close) - 1) * 100))
            : null;

    // What the pointer reads of one candle, which the line above the chart states for the newest candle and the shell's
    // script states again for the candle under the pointer: its sessions, its open, high, low, close and volume, its
    // change on the candle before it, and each average at its last session.
    public static string ChartReading(IReadOnlyList<ChartCandle> candles, int at)
    {
        var candle = candles[at];
        var sessions = candle.First == candle.Last
            ? FormattableString.Invariant($"{candle.Last:yyyy-MM-dd}")
            : FormattableString.Invariant($"{candle.First:yyyy-MM-dd} to {candle.Last:yyyy-MM-dd}");
        var change = ChangeOf(candles, at) is { } moved
            ? Formatted($", {SignedFigure(moved.Percent, 2, Formatted($"{(moved.Change > 0 ? "+" : string.Empty)}{Price(moved.Change)} ({Change2(moved.Percent)})"))}")
            : string.Empty;
        var lines = string.Join(" · ", PageAverages.Select(average => AverageOf(candle, average.Name) is { } value
            ? Formatted($"{average.Words} {value.ToString("#,##0.00", Invariant)}")
            : Formatted($"{average.Words} not stored")));

        return Formatted($"{sessions}: open {Price(candle.Open)}, high {Price(candle.High)}, low {Price(candle.Low)}, close {Price(candle.Close)}, volume {candle.Volume.ToString("N0", Invariant)}{change}. {lines}");
    }

    // Where a label in the column starts, past the plot's right edge.
    const int ColumnLabelAt = 9;

    // The column's width: the longest of the newest close and each plan price beside its kind at the legend's measure of
    // a character, set the label's start in from the plot and a margin past its end, and never narrower than the name
    // page's column, so no price it names runs past the chart's edge.
    public static int PriceColumn(decimal close, IReadOnlyList<ChartPlan> plans)
    {
        var longest = plans
            .SelectMany(plan => new (string Kind, decimal? Price)[] { ("buy", plan.Buy), ("stop", plan.Stop), ("target", plan.Target) })
            .Where(named => named.Price is not null)
            .Select(named => Formatted($"{named.Kind} {Price(named.Price!.Value)}").Length)
            .Append(Price(close).Length)
            .Max();

        return Math.Max(AxisWidth, (int)Math.Ceiling(ColumnLabelAt + (longest * LegendCharacter) + 4));
    }

    // The chart page's chart: candles at the interval shown in neutral ink, hollow closing up and filled closing down;
    // the three averages; volume beneath in the gain's green on a candle closing up on the one before and the loss's red
    // on one closing down; the report and ex-dividend marks on the candle holding their session; the night's level bands
    // in a group the page's toggle shows; and a listing rule's buy, stop and target as rules across the plot with the
    // rule's name in the column. Each candle carries what the pointer reads of it.
    // see: Green means support or a gain and red a stop or a loss, with orange for resistance
    // see: The chart marks each report and each ex-dividend date over its sessions, the ex-dates a kept dividend's and before the first kept one the steps the dividends leave in the bars
    public string CandleChart(
        string ticker,
        string interval,
        IReadOnlyList<ChartCandle> candles,
        IReadOnlyList<ChartBand> bands,
        IReadOnlyList<ChartPlan> plans,
        IReadOnlyList<ChartEvent> events)
    {
        if (candles.Count < FewestBars)
        {
            return Degraded(ticker, candles.Count);
        }

        // One scale over the candles, the averages and every price a plan draws, so a stop or a target past the
        // candles' range is still a line on the pane. The bands do not widen it, as on the name page's chart.
        var prices = candles
            .SelectMany(candle => new[] { PlotValue(candle.High), PlotValue(candle.Low) })
            .Concat(PageAverages.SelectMany(average => candles.Select(candle => AverageOf(candle, average.Name))).OfType<double>())
            .Concat(plans.SelectMany(plan => new decimal?[] { plan.Buy, plan.Stop, plan.Target }).OfType<decimal>().Select(PlotValue))
            .ToArray();
        var axis = new PriceAxis(prices.Min(), prices.Max());
        var loudest = candles.Max(candle => candle.Volume);
        var busiest = loudest > 0 ? loudest : 1;
        var slot = (Width - (2.0 * Margin)) / candles.Count;
        var body = Math.Max(1, Math.Min(11, slot * 0.62));

        double Centre(int index) => Margin + (slot * index) + (slot / 2);

        var wide = Width + PriceColumn(candles[^1].Close, plans);
        var svg = new StringBuilder();

        svg.Append(Invariant, $"<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 {wide} {ChartHeight}\" width=\"{wide}\" height=\"{ChartHeight}\" ");
        svg.Append(Invariant, $"role=\"img\" class=\"candle-chart\" data-ticker=\"{Escaped(ticker)}\" data-interval=\"{Escaped(interval)}\" data-candles=\"{candles.Count}\" ");
        svg.Append(Invariant, $"data-axis-low=\"{Number(axis.Low)}\" data-axis-high=\"{Number(axis.High)}\">");
        svg.Append(Invariant, $"<title>{Escaped(ticker)}, {candles.Count} {Escaped(interval)} candles from {candles[0].First:yyyy-MM-dd} to {candles[^1].Last:yyyy-MM-dd}</title>");
        svg.Append(Invariant, $"<desc>{Escaped(interval)} candles with the 20, 50 and 200 session averages over a volume pane on a shared time axis, the night's level bands shown by the page's toggle and a listing rule's buy, stop and target as lines.</desc>");

        // The averages named above the picture with their values at the newest candle, which the shell's script sets to
        // the candle under the pointer.
        svg.Append("<g class=\"m-legend\">");

        double legendAt = Margin;

        for (var line = 0; line < PageAverages.Length; line++)
        {
            var (name, words) = PageAverages[line];
            var text = LegendWords(words, AverageOf(candles[^1], name));

            svg.Append(Invariant, $"<line class=\"m-legend-swatch\" x1=\"{Number(legendAt)}\" y1=\"12\" x2=\"{Number(legendAt + 20)}\" y2=\"12\" ");
            svg.Append(Invariant, $"stroke=\"var(--ink, #1c1c1c)\" stroke-opacity=\"{Number(0.34 + (0.22 * line))}\" stroke-width=\"1.4\"/>");
            svg.Append(Invariant, $"<text class=\"m-legend-t\" data-legend=\"{name}\" x=\"{Number(legendAt + 26)}\" y=\"16\">{Escaped(text)}</text>");

            legendAt += 26 + ((words.Length + 12) * LegendCharacter) + 22;
        }

        svg.Append("</g>");
        svg.Append(Invariant, $"<g class=\"m-body\" transform=\"translate(0,{LegendRow})\">");
        svg.Append(Invariant, $"<rect class=\"m-plot\" x=\"{Margin}\" y=\"0\" width=\"{Width - (2 * Margin)}\" height=\"{PriceHeight}\"/>");

        // The night's bands, in a group the page's toggle shows and hides, behind everything else.
        if (bands.Count > 0)
        {
            svg.Append(Invariant, $"<g class=\"level-bands\" data-bands=\"{bands.Count}\">");

            foreach (var band in bands)
            {
                var top = At(axis, PlotValue(band.HighEdge));
                var bottom = At(axis, PlotValue(band.LowEdge));
                var support = band.Role == "support";

                svg.Append(Invariant, $"<rect class=\"level-band\" data-role=\"{Escaped(band.Role)}\" data-low-edge=\"{band.LowEdge.ToString(Invariant)}\" data-high-edge=\"{band.HighEdge.ToString(Invariant)}\" ");
                svg.Append(Invariant, $"x=\"{Margin}\" y=\"{Number(Math.Min(top, bottom))}\" width=\"{Width - (2 * Margin)}\" height=\"{Number(Math.Max(Math.Abs(bottom - top), 1))}\" ");
                svg.Append(Invariant, $"fill=\"{(support ? SupportHue : ResistanceHue)}\" fill-opacity=\"{Number(band.Immediate ? 0.22 : 0.12)}\"/>");
            }

            svg.Append("</g>");
        }

        // The averages beneath the candles, one path a run of values, so a candle with no stored average breaks the line.
        for (var line = 0; line < PageAverages.Length; line++)
        {
            var (name, _) = PageAverages[line];
            var run = new StringBuilder();

            svg.Append(Invariant, $"<g class=\"moving-average\" data-average=\"{name}\" data-values=\"{candles.Count(candle => AverageOf(candle, name) is not null)}\">");

            for (var index = 0; index <= candles.Count; index++)
            {
                if (index < candles.Count && AverageOf(candles[index], name) is { } point)
                {
                    run.Append(run.Length == 0 ? 'M' : 'L').Append(Number(Centre(index))).Append(' ').Append(Number(At(axis, point))).Append(' ');

                    continue;
                }

                if (run.Length > 0)
                {
                    svg.Append(Invariant, $"<path d=\"{run.ToString().Trim()}\" fill=\"none\" stroke=\"var(--ink, #1c1c1c)\" stroke-opacity=\"{Number(0.34 + (0.22 * line))}\" stroke-width=\"1.4\"/>");
                    run.Clear();
                }
            }

            svg.Append("</g>");
        }

        // Each candle with what the pointer reads of it, and a column the height of the pane the pointer finds it by.
        for (var index = 0; index < candles.Count; index++)
        {
            var candle = candles[index];
            var centre = Centre(index);
            var openY = At(axis, PlotValue(candle.Open));
            var closeY = At(axis, PlotValue(candle.Close));
            var height = Math.Abs(openY - closeY);
            var left = centre - (body / 2);

            svg.Append(Invariant, $"<g class=\"candle\" data-at=\"{index}\" data-first=\"{candle.First:yyyy-MM-dd}\" data-last=\"{candle.Last:yyyy-MM-dd}\" ");
            svg.Append(Invariant, $"data-open=\"{candle.Open.ToString(Invariant)}\" data-high=\"{candle.High.ToString(Invariant)}\" data-low=\"{candle.Low.ToString(Invariant)}\" data-close=\"{candle.Close.ToString(Invariant)}\" data-volume=\"{candle.Volume}\" ");
            svg.Append(Invariant, $"data-average20=\"{(candle.Sma20 is { } a20 ? a20.ToString("R", Invariant) : "none")}\" data-average50=\"{(candle.Sma50 is { } a50 ? a50.ToString("R", Invariant) : "none")}\" data-average200=\"{(candle.Sma200 is { } a200 ? a200.ToString("R", Invariant) : "none")}\" ");
            svg.Append(Invariant, $"data-reading=\"{Escaped(ChartReading(candles, index))}\"");

            foreach (var (name, words) in PageAverages)
            {
                svg.Append(Invariant, $" data-{name}=\"{Escaped(LegendWords(words, AverageOf(candle, name)))}\"");
            }

            svg.Append('>');
            svg.Append(Invariant, $"<rect class=\"candle-hit\" x=\"{Number(centre - (slot / 2))}\" y=\"0\" width=\"{Number(slot)}\" height=\"{PriceHeight}\" fill=\"transparent\"/>");
            svg.Append(Invariant, $"<line x1=\"{Number(centre)}\" y1=\"{Number(At(axis, PlotValue(candle.High)))}\" x2=\"{Number(centre)}\" y2=\"{Number(At(axis, PlotValue(candle.Low)))}\" stroke=\"var(--ink, #1c1c1c)\" stroke-width=\"1\"/>");
            svg.Append(height < 1
                ? Formatted($"<line x1=\"{Number(left)}\" y1=\"{Number(closeY)}\" x2=\"{Number(left + body)}\" y2=\"{Number(closeY)}\" stroke=\"var(--ink, #1c1c1c)\" stroke-width=\"1\"/>")
                : Formatted($"<rect x=\"{Number(left)}\" y=\"{Number(Math.Min(openY, closeY))}\" width=\"{Number(body)}\" height=\"{Number(height)}\" fill=\"{(candle.Close > candle.Open ? "none" : "var(--ink, #1c1c1c)")}\" stroke=\"var(--ink, #1c1c1c)\" stroke-width=\"1\"/>"));
            svg.Append("</g>");
        }

        // A listing rule's plan as rules across the pane, the buy and the target in the gain's green and the stop in the
        // loss's red, each named in the column with the rule's name.
        var named = new List<(double Y, string Text, string Kind)>();

        foreach (var plan in plans)
        {
            foreach (var (kind, price, words) in new (string Kind, decimal? Price, string Words)[]
            {
                ("buy", plan.Buy, Formatted($"{plan.Rule} buys at the next open, planned from {Price(plan.Buy)}")),
                ("stop", plan.Stop, plan.Stop is { } stop ? Formatted($"{plan.Rule} stop {Price(stop)}") : string.Empty),
                ("target", plan.Target, plan.Target is { } target ? Formatted($"{plan.Rule} target {Price(target)}") : string.Empty),
            })
            {
                if (price is not { } at)
                {
                    continue;
                }

                var y = At(axis, PlotValue(at));

                svg.Append(Invariant, $"<line class=\"plan-line plan-{kind}\" data-rule=\"{Escaped(plan.Rule)}\" data-kind=\"{kind}\" data-price=\"{at.ToString(Invariant)}\" ");
                svg.Append(Invariant, $"x1=\"{Margin}\" y1=\"{Number(y)}\" x2=\"{Width - Margin}\" y2=\"{Number(y)}\"{(kind == "buy" ? " stroke-dasharray=\"6 3\"" : string.Empty)}><title>{Escaped(words)}</title></line>");
                named.Add((y, Formatted($"{(kind == "buy" ? "buy" : kind)} {Price(at)}"), kind));
            }
        }

        // Each report and ex-dividend date on the candle whose sessions hold it.
        var marked = events
            .Select(item => (Item: item, At: IndexHolding(candles, item.Session)))
            .Where(pair => pair.At >= 0)
            .OrderBy(pair => pair.Item.Session)
            .ToArray();

        if (marked.Length > 0)
        {
            svg.Append(Invariant, $"<g class=\"chart-events\" data-events=\"{marked.Length}\">");

            foreach (var (item, at) in marked)
            {
                var x = Centre(at);
                var report = item.Kind == ChartEvent.Report;

                svg.Append(Invariant, $"<g class=\"ev-mark {(report ? "ev-report" : "ev-dividend")}\" data-kind=\"{Escaped(item.Kind)}\" data-session=\"{item.Session:yyyy-MM-dd}\" data-at=\"{at}\"><title>{Escaped(item.Says)}</title>");
                svg.Append(Invariant, $"<rect class=\"ev-tag\" x=\"{Number(x - 6)}\" y=\"{PriceHeight - 15}\" width=\"12\" height=\"12\" rx=\"2\"/>");
                svg.Append(Invariant, $"<text class=\"ev-tag-t\" x=\"{Number(x)}\" y=\"{PriceHeight - 5.5}\" text-anchor=\"middle\">{(report ? "R" : "D")}</text></g>");
            }

            svg.Append("</g>");
        }

        // The newest close as a rule and a tag in the column, and the plan's prices named beside it.
        var now = At(axis, PlotValue(candles[^1].Close));

        svg.Append(Invariant, $"<line class=\"m-now\" x1=\"{Margin}\" y1=\"{Number(now)}\" x2=\"{Width - Margin}\" y2=\"{Number(now)}\" stroke-dasharray=\"4 3\"/>");
        svg.Append(Invariant, $"<g class=\"price-column\" data-close=\"{candles[^1].Close.ToString(Invariant)}\">");
        svg.Append(Invariant, $"<line class=\"m-axisline\" x1=\"{Width + 1}\" y1=\"0\" x2=\"{Width + 1}\" y2=\"{PriceHeight}\"/>");
        svg.Append(Invariant, $"<rect class=\"m-nowtag\" x=\"{Width + 4}\" y=\"{Number(now - 11)}\" width=\"{AxisWidth - 6}\" height=\"22\" rx=\"2\"/>");
        svg.Append(Invariant, $"<text class=\"m-nowtag-t\" x=\"{Width + ColumnLabelAt}\" y=\"{Number(now + 5)}\">{Price(candles[^1].Close)}</text>");

        var placed = new List<double> { now };

        foreach (var (y, text, kind) in named.OrderBy(price => Math.Abs(price.Y - now)))
        {
            if (y < 8 || y > PriceHeight - 4 || placed.Any(other => Math.Abs(other - y) < 18))
            {
                continue;
            }

            placed.Add(y);
            svg.Append(Invariant, $"<text class=\"m-tick plan-tick-{kind}\" x=\"{Width + ColumnLabelAt}\" y=\"{Number(y + 5)}\">{Escaped(text)}</text>");
        }

        svg.Append("</g>");

        // Volume beneath, each bar in the hue of its candle's close on the one before it.
        var volumeTop = PriceHeight + Gap;

        svg.Append(Invariant, $"<g class=\"volume-pane\" data-candles=\"{candles.Count}\">");
        svg.Append(Invariant, $"<line x1=\"{Margin}\" y1=\"{volumeTop}\" x2=\"{Width - Margin}\" y2=\"{volumeTop}\" stroke=\"var(--rule, #d8d8d8)\" stroke-width=\"1\"/>");

        for (var index = 0; index < candles.Count; index++)
        {
            var candle = candles[index];
            var height = candle.Volume * 1.0 / busiest * (VolumeHeight - Margin);
            var side = ChangeOf(candles, index) is { } moved ? moved.Change > 0 ? "up" : moved.Change < 0 ? "down" : "flat" : "flat";

            svg.Append(Invariant, $"<rect class=\"volume vol-{side}\" data-last=\"{candle.Last:yyyy-MM-dd}\" x=\"{Number(Centre(index) - (body / 2))}\" ");
            svg.Append(Invariant, $"y=\"{Number(volumeTop + VolumeHeight - Margin - height)}\" width=\"{Number(body)}\" height=\"{Number(height)}\"/>");
        }

        svg.Append(Invariant, $"<text class=\"m-cap\" x=\"{Margin}\" y=\"{volumeTop - 6}\">Volume, the tallest bar {loudest.ToString("N0", Invariant)} shares</text>");
        svg.Append("</g>");

        // The shared time axis: the first and last candles and three between, each by its last session.
        svg.Append("<g class=\"time-axis\">");

        var dateY = PriceHeight + Gap + VolumeHeight + DateRow - 3;

        foreach (var tick in new[] { 0, candles.Count / 4, candles.Count / 2, candles.Count * 3 / 4, candles.Count - 1 }.Distinct())
        {
            var anchor = tick == 0 ? "start" : tick == candles.Count - 1 ? "end" : "middle";
            var x = tick == 0 ? Margin : tick == candles.Count - 1 ? Width - Margin : Centre(tick);

            svg.Append(Invariant, $"<text class=\"m-tick\" x=\"{Number(x)}\" y=\"{dateY}\" text-anchor=\"{anchor}\">{candles[tick].Last:yyyy-MM-dd}</text>");
        }

        svg.Append("</g></g></svg>");

        return svg.ToString();
    }

    // An average as the legend names it at a candle: its words and its value there, or that none is stored.
    static string LegendWords(string words, double? value) =>
        value is { } at ? Formatted($"{words} {at.ToString("#,##0.00", Invariant)}") : Formatted($"{words} not stored");

    // The candle whose first and last sessions hold a day, or none.
    static int IndexHolding(IReadOnlyList<ChartCandle> candles, DateOnly session)
    {
        for (var at = 0; at < candles.Count; at++)
        {
            if (candles[at].First <= session && session <= candles[at].Last)
            {
                return at;
            }
        }

        return -1;
    }
}
