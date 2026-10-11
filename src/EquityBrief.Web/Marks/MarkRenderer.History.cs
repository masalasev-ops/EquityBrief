using System.Text;
using EquityBrief.Core.Ladders;
using EquityBrief.Core.Moves;
using EquityBrief.Core.Prices;

namespace EquityBrief.Web.Marks;

// One band on the ladder figure as the page is given it: its edges and role, how far it sits from the price the page
// draws in typical days' moves, none where the name has no typical move and nought inside the band, how many times the
// price turned at it and the last session it did, none where it holds no turn, and what the plan does there, none where
// it neither buys nor sells at it.
public sealed record LadderBand(decimal LowEdge, decimal HighEdge, string Role, double? Away, int Touches, DateOnly? LastTouch, string? Use);

// One level the long view draws across its line: its edges, its role, a support, a resistance or a rule, its words
// beneath the pointer and the short name it is drawn with at the line's end.
public sealed record LongViewLevel(decimal LowEdge, decimal HighEdge, string Role, string Says, string Label);

// One date the chart marks beneath a session's candle: a report, the session its reaction is read on, or an ex-dividend
// date, with what the mark says beneath the pointer.
public sealed record ChartEvent(DateOnly Session, string Kind, string Says)
{
    public const string Report = "report";

    public const string ExDividend = "ex-dividend";
}

// What the earnings reactions, the chart's marks, the long view, the moves' causes, the ladder figure and the plan's
// shares draw, each worked by the core from stored figures: whether the page draws the newest night, the only
// one the typical earnings move and the long view are stored for; the stock's typical earnings move with the pull it read
// or why none was read; the long view's month-end closes with the pull and the ratio that brought them to the stored
// prices or why none was read, the plan's levels and the year's high and low it draws across them and why the plan's are
// not drawn where they are not; the chart's report and ex-dividend marks; the bands about the price with their distances,
// turns and what the plan does at each; the plan's tranches as shares of a full position with their reward to risk, or
// why no plan is stored; the trail in words; and the instants the news labels cover.
public sealed record HistoryView(
    bool Newest,
    EarningsMove? Typical,
    string? TypicalPull,
    string? TypicalReason,
    IReadOnlyList<MonthEnd> LongView,
    string? LongViewPull,
    decimal? LongViewRatio,
    string? LongViewReason,
    IReadOnlyList<LongViewLevel> Levels,
    string? PlanLevelsAbsent,
    IReadOnlyList<ChartEvent> Events,
    IReadOnlyList<LadderBand> Ladder,
    decimal? LadderPrice,
    bool LadderLive,
    IReadOnlyList<TrancheShare> Shares,
    string? NoPlan,
    string? Trail,
    (DateTimeOffset First, DateTimeOffset Last)? LabelsCover,
    RiskLines? Risk = null,
    string? PlanReason = null,
    string? PlanForms = null);

// The four marks the reactions, the long view, the levels and the plan draw: each print's reaction against the
// stock's typical earnings move, the month-end closes over the pulled history with the plan's levels on the same prices,
// the ladder of the bands above and below the price, and each tranche's reward to risk to each target. Each takes stored
// values worked by the core and returns an SVG string with a title on every mark for the pointer, one axis, and a row
// above it naming what it draws where it draws two series or more; each degrades by saying what it has.
// see: Marks are defined once and every screen draws from that list
public sealed partial class MarkRenderer
{
    // ---- earnings reactions ----

    // Reaction range: each print over the calendar's year as a bar of its session's move about a zero rule, in the rise or
    // the fall hue by its sign, inside a band from the typical earnings move under nought to the same over it, so a
    // reaction past the stock's habit reads at a glance. Where the typical move is not stated the bars stand alone and the
    // row above says so; no print says so.
    public string ReactionRange(string ticker, IReadOnlyList<ReactionCell> prints, EarningsMove? typical)
    {
        if (prints.Count == 0)
        {
            return Formatted($"<p class=\"mark-short\" data-mark=\"reaction-range\" data-prints=\"0\">No print of {Escaped(ticker)}'s over the calendar's year is stored with a session the bars reach, so no reaction is drawn.</p>");
        }

        double? range = typical is { Stated: true, MedianPct: { } median } ? median : null;
        double[] values = [.. prints.Select(print => print.MovePct), 0, .. range is { } band ? new[] { band, -band } : []];
        var (low, high) = Bounds(values.Min(), values.Max());
        double Y(double value) => ChartTop + ((high - value) / (high - low) * (ChartHigh - ChartTop - ChartBottom));

        ReactionCell[] ordered = [.. prints.OrderBy(print => print.ReportDate)];
        var slot = (ChartWide - ChartLeft - ChartRight) / ordered.Length;
        var bar = Math.Min(28, slot * 0.5);
        var svg = new StringBuilder();

        svg.Append(Formatted($"<svg class=\"reaction-range\" viewBox=\"0 0 {Number(ChartWide)} {Number(ChartHigh)}\" role=\"img\" data-mark=\"reaction-range\" data-prints=\"{ordered.Length}\" data-typical=\"{(range is { } shown ? shown.ToString(Invariant) : string.Empty)}\" aria-label=\"Each report's move against {Escaped(ticker)}'s typical earnings move\">"));
        svg.Append(range is null
            ? Legend(("rr-move", "The session's move"))
            : Legend(("rr-move", "The session's move"), ("rr-range", "The typical earnings move either way")));
        svg.Append(Ticks(low, high, Y, "%"));

        if (range is { } either)
        {
            svg.Append(Formatted($"<rect class=\"rr-range\" x=\"{Number(ChartLeft)}\" y=\"{Number(Y(either))}\" width=\"{Number(ChartWide - ChartLeft - ChartRight)}\" height=\"{Number(Y(-either) - Y(either))}\" data-low=\"{(-either).ToString(Invariant)}\" data-high=\"{either.ToString(Invariant)}\"><title>The typical earnings move, {either.ToString("0.0", Invariant)}% either way, the median size of {typical!.Prints} reactions since {DayOf(typical.FirstPrint!.Value)}</title></rect>"));
        }

        svg.Append(Formatted($"<line class=\"m-axisline\" x1=\"{Number(ChartLeft)}\" x2=\"{Number(ChartWide - ChartRight)}\" y1=\"{Number(Y(0))}\" y2=\"{Number(Y(0))}\"/>"));

        for (var at = 0; at < ordered.Length; at++)
        {
            var print = ordered[at];
            var middle = ChartLeft + (slot * at) + (slot / 2);
            var top = Math.Min(Y(print.MovePct), Y(0));
            var tall = Math.Max(Math.Abs(Y(print.MovePct) - Y(0)), 1);
            var past = range is { } limit && Math.Abs(print.MovePct) > limit;
            var said = range is null ? string.Empty : past ? ", past the typical move" : ", inside the typical move";

            svg.Append(Formatted($"<rect class=\"rr-move rr-{(print.MovePct >= 0 ? "up" : "down")}{(past ? " rr-past" : string.Empty)}\" x=\"{Number(middle - (bar / 2))}\" y=\"{Number(top)}\" width=\"{Number(bar)}\" height=\"{Number(tall)}\" rx=\"2\" data-report-date=\"{DayOf(print.ReportDate)}\" data-move=\"{print.MovePct.ToString(Invariant)}\" data-past=\"{(past ? "yes" : "no")}\"><title>Reported {DayOf(print.ReportDate)}: the session of {DayOf(print.Session)} moved {print.MovePct.ToString("+0.0;-0.0;0.0", Invariant)}%{said}</title></rect>"));
            svg.Append(Formatted($"<text class=\"m-tick\" x=\"{Number(middle)}\" y=\"{Number(ChartHigh - 10)}\" text-anchor=\"middle\">{print.ReportDate.ToString("MMM yy", Invariant)}</text>"));
        }

        return svg.Append("</svg>").ToString();
    }

    // The typical earnings move in a line above the reactions: the median size with the prints it was read over and the
    // oldest of them and how many were left out, or that too few prints were read to state one, with the pull it read or
    // why none was read; and on a page for an earlier night, that the move is stored for the newest night alone.
    public string TypicalMoveLine(string ticker, EarningsMove? typical, string? pull, string? reason, bool newest = true)
    {
        if (!newest)
        {
            return "<p class=\"typical-move\" data-typical=\"newest-night-alone\">The typical earnings move is stored for the newest night alone, over every print to it, so a page for an earlier night draws none; the newest night's page draws it.</p>";
        }

        if (typical is null)
        {
            return Formatted($"<p class=\"typical-move\" data-typical=\"none\">No typical earnings move is stored for {Escaped(ticker)} tonight; the chart averages step writes one each night for every name whose stored year holds no hole.</p>");
        }

        var read = pull is { } from
            ? Formatted($", read through the history pulled before the stored year from the pull {Escaped(from)}")
            : Formatted($", read over the stored year alone: {Escaped(reason ?? "no history before it was read")}");
        var left = typical.Unread == 0 ? string.Empty : Formatted($", {Counted(typical.Unread, "report", "reports")} left out where the prices did not reach them or a session around them was missing");

        return typical is { Stated: true, MedianPct: { } median, FirstPrint: { } since }
            ? Formatted($"<p class=\"typical-move\" data-typical=\"{median.ToString(Invariant)}\" data-prints=\"{typical.Prints}\" data-unread=\"{typical.Unread}\">The typical earnings move: {median.ToString("0.0", Invariant)}% either way, the median size of {typical.Prints} reports' moves since {DayOf(since)}{read}{left}.</p>")
            : Formatted($"<p class=\"typical-move\" data-typical=\"too-few\" data-prints=\"{typical.Prints}\" data-unread=\"{typical.Unread}\">No typical earnings move is stated: {Counted(typical.Prints, "report was", "reports were")} read, under the {EarningsMoves.FewestPrints} it is stated over{read}{left}.</p>");
    }

    // ---- the long view ----

    // Long view: the month-end closes as one line over the years the store and the pull hold, at the bar store's prices,
    // with each of the plan's levels drawn across it as a band in its role's hue and named at the right, the year's high
    // and low among them, every one on the same price axis. Fewer than two months say so.
    public string LongView(string ticker, IReadOnlyList<MonthEnd> closes, IReadOnlyList<LongViewLevel> levels)
    {
        if (closes.Count < 2)
        {
            return Formatted($"<p class=\"mark-short\" data-mark=\"long-view\" data-months=\"{closes.Count}\">{(closes.Count == 0 ? "No month-end close is" : "1 month-end close is")} stored for {Escaped(ticker)}, and the line needs two.</p>");
        }

        const double High = 260;
        const double Right = 130;
        double[] prices = [.. closes.Select(point => PlotValue(point.Close)), .. levels.SelectMany(level => new[] { PlotValue(level.LowEdge), PlotValue(level.HighEdge) })];
        var (low, top) = Bounds(prices.Min(), prices.Max(), zero: false);
        double Y(double price) => ChartTop + ((top - price) / (top - low) * (High - ChartTop - ChartBottom));
        var first = closes[0].Session.DayNumber;
        var span = Math.Max(closes[^1].Session.DayNumber - first, 1);
        double X(DateOnly session) => ChartLeft + ((ChartWide - ChartLeft - Right) * (session.DayNumber - first) / span);

        var svg = new StringBuilder();

        svg.Append(Formatted($"<svg class=\"long-view\" viewBox=\"0 0 {Number(ChartWide)} {Number(High)}\" role=\"img\" data-mark=\"long-view\" data-months=\"{closes.Count}\" data-axis-low=\"{low.ToString(Invariant)}\" data-axis-high=\"{top.ToString(Invariant)}\" aria-label=\"{Escaped(ticker)}'s month-end closes from {DayOf(closes[0].Session)} to {DayOf(closes[^1].Session)} with the plan's levels\">"));
        svg.Append(Legend(("lv-close", "Month-end close"), ("lv-sup", "Plan buys"), ("lv-res", "Plan sells or trails"), ("lv-rule", "Invalidation and year's range")));

        for (var step = 0; step <= 3; step++)
        {
            var value = low + ((top - low) * step / 3);

            svg.Append(Formatted($"<line class=\"m-gridline\" x1=\"{Number(ChartLeft)}\" x2=\"{Number(ChartWide - Right)}\" y1=\"{Number(Y(value))}\" y2=\"{Number(Y(value))}\"/>"));
            svg.Append(Formatted($"<text class=\"m-tick\" x=\"{Number(ChartLeft - 6)}\" y=\"{Number(Y(value) + 4)}\" text-anchor=\"end\">{value.ToString("0", Invariant)}</text>"));
        }

        // Each level's name at the line's end, at its own height where it can be and pushed down a line where another's
        // name stands within a line above it, so two levels at about one price are both read.
        var named = new List<(LongViewLevel Level, double Upper, double Lower, double Text)>();
        var below = double.MinValue;

        foreach (var level in levels.OrderByDescending(level => level.HighEdge).ThenByDescending(level => level.LowEdge))
        {
            var upper = Y(PlotValue(level.HighEdge));
            var lower = Y(PlotValue(level.LowEdge));
            var text = Math.Max(((upper + lower) / 2) + 4, below + 12);

            named.Add((level, upper, lower, text));
            below = text;
        }

        foreach (var (level, upper, lower, text) in named)
        {
            var kind = level.Role switch
            {
                "support" => "lv-sup",
                "resistance" => "lv-res",
                _ => "lv-rule",
            };

            svg.Append(Formatted($"<rect class=\"{kind}\" x=\"{Number(ChartLeft)}\" y=\"{Number(upper)}\" width=\"{Number(ChartWide - ChartLeft - Right)}\" height=\"{Number(Math.Max(lower - upper, 1.5))}\" data-low-edge=\"{level.LowEdge.ToString(Invariant)}\" data-high-edge=\"{level.HighEdge.ToString(Invariant)}\" data-role=\"{Escaped(level.Role)}\" data-y=\"{Number(upper)}\"><title>{Escaped(level.Says)}</title></rect>"));
            svg.Append(Formatted($"<text class=\"m-rownote\" x=\"{Number(ChartWide - Right + 6)}\" y=\"{Number(text)}\">{Escaped(Shortened(level.Label, 20))}</text>"));
        }

        svg.Append(Formatted($"<polyline class=\"lv-close\" fill=\"none\" points=\"{string.Join(' ', closes.Select(point => Number(X(point.Session)) + "," + Number(Y(PlotValue(point.Close)))))}\"/>"));

        foreach (var point in closes)
        {
            svg.Append(Formatted($"<circle class=\"lv-dot\" cx=\"{Number(X(point.Session))}\" cy=\"{Number(Y(PlotValue(point.Close)))}\" r=\"1.6\" data-session=\"{DayOf(point.Session)}\" data-close=\"{point.Close.ToString(Invariant)}\"><title>{DayOf(point.Session)}: {Price(point.Close)}</title></circle>"));
        }

        foreach (var year in closes.Select(point => point.Session.Year).Distinct().Where(year => year % 2 == closes[^1].Session.Year % 2))
        {
            var at = closes.First(point => point.Session.Year == year).Session;

            svg.Append(Formatted($"<text class=\"m-tick\" x=\"{Number(X(at))}\" y=\"{Number(High - 10)}\" text-anchor=\"middle\">{year}</text>"));
        }

        return svg.Append("</svg>").ToString();
    }

    // ---- the levels ----

    // Ladder figure: the bands stacked above and below the price the page draws, those above it under "Sell into these"
    // with the furthest first, then the price, then those below it under "Buy into these" with the nearest first, a band
    // holding the price beside the price on the side of its role. Each row is a band's role as a pill in its own hue, its
    // edges, how far it sits in typical days' moves, what the plan does there, and beneath them how many times the price
    // turned at it and the last session it did. The rows are spaced evenly in price order and not on a price scale, so a
    // band ten moves away reads as a row and not as a point off the figure. A name with no band says so.
    public string LadderFigure(string ticker, IReadOnlyList<LadderBand> bands, decimal? price, bool live)
    {
        if (bands.Count == 0 || price is not { } now)
        {
            return Formatted($"<p class=\"mark-short\" data-mark=\"ladder-figure\" data-bands=\"{bands.Count}\">{Escaped(ticker)} has no band stored to stack about the price.</p>");
        }

        bool Above(LadderBand band) => band.LowEdge > now || (band.HighEdge >= now && band.Role == "resistance");

        LadderBand[] above = [.. bands.Where(Above).OrderByDescending(band => band.LowEdge)];
        LadderBand[] below = [.. bands.Where(band => !Above(band)).OrderByDescending(band => band.HighEdge)];
        const double Row = 42;
        const double Head = 24;
        const double None = 26;
        var high = (Row * (above.Length + below.Length)) + 30 + (Head * 2) + 12 + (above.Length == 0 ? None : 0) + (below.Length == 0 ? None : 0);
        var svg = new StringBuilder();
        var y = 6.0;

        svg.Append(Formatted($"<svg class=\"ladder-figure\" viewBox=\"0 0 {Number(ChartWide)} {Number(high)}\" role=\"img\" data-mark=\"ladder-figure\" data-bands=\"{bands.Count}\" data-above=\"{above.Length}\" data-below=\"{below.Length}\" data-price=\"{now.ToString(Invariant)}\" data-at=\"{(live ? "quote" : "close")}\" aria-label=\"{Escaped(ticker)}'s bands above and below the price\">"));

        void Heading(string words, string kind)
        {
            svg.Append(Formatted($"<text class=\"lf-head {kind}\" x=\"8\" y=\"{Number(y + 16)}\">{words}</text>"));
            y += Head;
        }

        void Band(LadderBand band, bool over)
        {
            var hue = band.Role == "resistance" ? "res" : "sup";
            var edges = band.LowEdge == band.HighEdge ? Price(band.LowEdge) : Formatted($"{Price(band.LowEdge)} to {Price(band.HighEdge)}");
            var away = Away(band.Away, over);
            var turns = band.Touches == 0
                ? "never turned the price"
                : Formatted($"turned the price {Times(band.Touches)}, last {DayOf(band.LastTouch!.Value)}");
            var use = band.Use is { } said ? Formatted($"; {said}") : string.Empty;

            svg.Append(Formatted($"<g class=\"lf-band lf-{hue}\" data-low-edge=\"{band.LowEdge.ToString(Invariant)}\" data-high-edge=\"{band.HighEdge.ToString(Invariant)}\" data-side=\"{(over ? "above" : "below")}\" data-touches=\"{band.Touches}\" data-last-touch=\"{(band.LastTouch is { } last ? DayOf(last) : string.Empty)}\" data-use=\"{Escaped(band.Use ?? string.Empty)}\"><title>{Escaped(band.Role)} {Escaped(edges)}: {Escaped(away)}; {Escaped(turns)}{Escaped(use)}</title>"));
            svg.Append(Formatted($"<rect class=\"lf-pill lf-{hue}\" x=\"8\" y=\"{Number(y + 4)}\" width=\"96\" height=\"20\" rx=\"10\"/><text class=\"lf-pill-t\" x=\"56\" y=\"{Number(y + 18)}\" text-anchor=\"middle\">{Escaped(band.Role)}</text>"));
            svg.Append(Formatted($"<text class=\"m-row\" x=\"116\" y=\"{Number(y + 18)}\">{Escaped(edges)}</text>"));
            svg.Append(Formatted($"<text class=\"m-row lf-away\" x=\"250\" y=\"{Number(y + 18)}\" data-away=\"{(band.Away is { } value ? value.ToString(Invariant) : "none")}\">{Escaped(away)}</text>"));

            if (band.Use is { } doing)
            {
                svg.Append(Formatted($"<text class=\"m-row lf-use\" x=\"420\" y=\"{Number(y + 18)}\">{Escaped(doing)}</text>"));
            }

            svg.Append(Formatted($"<text class=\"m-rownote\" x=\"116\" y=\"{Number(y + 34)}\">{Escaped(turns)}</text></g>"));
            y += Row;
        }

        // A side holding no band says so under its heading, where a heading over nothing reads as a list that failed to draw.
        void Nothing(string words, string side)
        {
            svg.Append(Formatted($"<text class=\"m-rownote lf-none\" x=\"8\" y=\"{Number(y + 16)}\" data-side=\"{side}\">{words}</text>"));
            y += None;
        }

        Heading("Sell into these", "lf-sell");

        if (above.Length == 0)
        {
            Nothing("No band stands above the price, so the plan has nothing above it to sell into.", "above");
        }

        foreach (var band in above)
        {
            Band(band, over: true);
        }

        svg.Append(Formatted($"<g class=\"lf-now\"><line class=\"m-now\" x1=\"8\" x2=\"{Number(ChartWide - 8)}\" y1=\"{Number(y + 15)}\" y2=\"{Number(y + 15)}\" stroke-dasharray=\"4 3\"/><rect class=\"m-nowtag\" x=\"{Number((ChartWide / 2) - 90)}\" y=\"{Number(y + 4)}\" width=\"180\" height=\"22\" rx=\"2\"/><text class=\"m-nowtag-t lf-price\" x=\"{Number(ChartWide / 2)}\" y=\"{Number(y + 19)}\" text-anchor=\"middle\" data-price=\"{now.ToString(Invariant)}\">{(live ? "price now" : "last close")} {Price(now)}</text></g>"));
        y += 30;

        Heading("Buy into these", "lf-buy");

        if (below.Length == 0)
        {
            Nothing("No band stands below the price, so the plan has nowhere beneath it to buy.", "below");
        }

        foreach (var band in below)
        {
            Band(band, over: false);
        }

        return svg.Append("</svg>").ToString();
    }

    // A band's distance in the words the ladder figure and the page's script write it in, on the side of the price the
    // band stands.
    public static string Away(double? days, bool above) => days switch
    {
        null => "not measured",
        0 => "the price is inside it",
        { } count => Formatted($"{count:0.0} typical days {(above ? "above" : "below")}"),
    };

    // ---- the plan ----

    // The tranches as shares of a full position: each zone, the price it is carried at, its share, the stop that sells it,
    // the risk a share carries, and its reward to risk to each traded exit, the top one named where the trail begins, with
    // a line saying what the shares add to. A tranche the plan places no stop under states no risk and no reward to risk.
    public string SharesTable(string ticker, IReadOnlyList<TrancheShare> tranches, string? noPlan = null)
    {
        if (tranches.Count == 0)
        {
            return noPlan is { } why
                ? Formatted($"<p class=\"degraded\" data-tranche-shares=\"no-plan\">No share is drawn: {Escaped(why)}.</p>")
                : Formatted($"<p class=\"degraded\" data-tranche-shares=\"none\">{Escaped(ticker)}'s plan places no tranche, so no share is drawn.</p>");
        }

        TargetReward[] targets = [.. tranches.SelectMany(tranche => tranche.Targets).DistinctBy(target => target.Target).OrderBy(target => target.Target)];
        var table = new StringBuilder();

        table.Append(Formatted($"<div class=\"tbl-wrap\"><table class=\"shares-table\" data-ticker=\"{Escaped(ticker)}\" data-rows=\"{tranches.Count}\"><tr><th>Tranche</th><th class=\"num\">Zone</th><th class=\"num\">Carried at</th><th class=\"num\">Share</th><th class=\"num\">Sold below</th><th class=\"num\">Risk a share</th>"));

        foreach (var target in targets)
        {
            table.Append(Formatted($"<th class=\"num\">Reward to risk to {Price(target.Target)}{(target.TrailBegins ? ", where the trail begins" : string.Empty)}</th>"));
        }

        table.Append("</tr>");

        for (var at = 0; at < tranches.Count; at++)
        {
            var tranche = tranches[at];
            var zone = tranche.LowEdge == tranche.HighEdge ? Price(tranche.LowEdge) : Formatted($"{Price(tranche.LowEdge)} to {Price(tranche.HighEdge)}");

            table.Append(Formatted($"<tr data-tranche=\"{at + 1}\" data-share=\"{tranche.Share}\" data-buy=\"{tranche.Buy.ToString(Invariant)}\" data-stop=\"{(tranche.Stop is { } stop ? stop.ToString(Invariant) : string.Empty)}\" data-risk=\"{(tranche.Risk is { } risk ? risk.ToString(Invariant) : string.Empty)}\"><td>Tranche {at + 1}</td><td class=\"num\">{Escaped(zone)}</td><td class=\"num\">{Price(tranche.Buy)}</td><td class=\"num\">{tranche.Share}</td>"));
            table.Append(tranche.Stop is { } sold
                ? Formatted($"<td class=\"num\">{Price(sold)}</td><td class=\"num\">{(tranche.Risk is { } carried ? Price(carried) : "none stated")}</td>")
                : "<td class=\"num\">none: no band beneath it</td><td class=\"num\">none stated</td>");

            foreach (var target in targets)
            {
                table.Append(tranche.Targets.FirstOrDefault(reward => reward.Target == target.Target) is { RewardToRisk: { } ratio } toTarget
                    ? Formatted($"<td class=\"num\" data-target=\"{target.Target.ToString(Invariant)}\" data-reward-to-risk=\"{ratio.ToString(Invariant)}\">{Statistic.FromRatio(ratio).ToString("0.00", Invariant)} on {Price(toTarget.Reward)}</td>")
                    : Formatted($"<td class=\"num\" data-target=\"{target.Target.ToString(Invariant)}\" data-reward-to-risk=\"\">none, no risk stated</td>"));
            }

            table.Append("</tr>");
        }

        var (held, whole) = PlanSizes.Sum(tranches);

        table.Append("</table></div>");
        table.Append(Formatted($"<p class=\"shares-sum\" data-held=\"{held}\" data-whole=\"{whole}\">{(held == whole ? (whole == 1 ? "One share makes the whole position." : Formatted($"{held} equal shares of 1/{whole} make the whole position.")) : Formatted($"{held} of {whole} shares are placed."))}</p>"));

        return table.ToString();
    }

    // Reward to risk bars: for each tranche a bar to each traded exit of its reward over its risk on one axis from nought,
    // with a rule at one where the reward equals the risk, a bar past the axis's end held at the end and saying so, and the
    // top exit named where the trail begins. A plan with no tranche stating a risk or no traded exit says so.
    public string RewardToRiskBars(string ticker, IReadOnlyList<TrancheShare> tranches)
    {
        (int At, TrancheShare Tranche, TargetReward Target)[] bars =
        [
            .. tranches.SelectMany((tranche, at) => tranche.Targets.Select(target => (at, tranche, target))).Where(item => item.target.RewardToRisk is not null),
        ];

        if (bars.Length == 0)
        {
            return Formatted($"<p class=\"mark-short\" data-mark=\"reward-to-risk\" data-tranches=\"{tranches.Count}\">{Escaped(ticker)}'s plan holds no tranche stating a risk with a traded exit to read a reward to risk against.</p>");
        }

        const double Bar = 14;
        const double Gap = 6;
        const double Most = 6;
        const double Label = 200;
        const double Foot = 10;
        var axis = Math.Min(Math.Max(3, bars.Max(item => Statistic.FromRatio(item.Target.RewardToRisk!.Value))), Most);

        // As tall as the bars it draws, a gap between one tranche's bars and the next's: a tranche stating no risk draws no
        // bar and takes no room, and nothing is drawn beneath the last bar but the foot of the rule at one.
        var high = ChartTop + (bars.Length * (Bar + Gap)) + ((bars.Select(item => item.At).Distinct().Count() - 1) * 8) + Foot;
        double X(double ratio) => Label + ((ChartWide - Label - ChartRight - 60) * Math.Min(ratio, axis) / axis);

        var svg = new StringBuilder();

        svg.Append(Formatted($"<svg class=\"reward-to-risk\" viewBox=\"0 0 {Number(ChartWide)} {Number(high)}\" role=\"img\" data-mark=\"reward-to-risk\" data-bars=\"{bars.Length}\" data-axis=\"{axis.ToString(Invariant)}\" aria-label=\"Each tranche's reward to risk to each traded exit\">"));
        svg.Append(Formatted($"<line class=\"rk-one\" x1=\"{Number(X(1))}\" x2=\"{Number(X(1))}\" y1=\"{Number(ChartTop - 6)}\" y2=\"{Number(high - Foot + 4)}\"/><text class=\"m-tick\" x=\"{Number(X(1))}\" y=\"{Number(ChartTop - 10)}\" text-anchor=\"middle\">reward equals risk</text>"));

        var y = ChartTop;
        var last = -1;

        foreach (var (at, tranche, target) in bars)
        {
            if (at != last && last >= 0)
            {
                y += 8;
            }

            last = at;
            var ratio = Statistic.FromRatio(target.RewardToRisk!.Value);
            var past = ratio > axis;
            var trail = target.TrailBegins ? ", where the trail begins" : string.Empty;

            svg.Append(Formatted($"<text class=\"m-row\" x=\"8\" y=\"{Number(y + 11)}\">Tranche {at + 1} to {Price(target.Target)}{(target.TrailBegins ? " (trail)" : string.Empty)}</text>"));
            svg.Append(Formatted($"<rect class=\"rk-bar{(ratio < 1 ? " rk-under" : string.Empty)}\" x=\"{Number(Label)}\" y=\"{Number(y)}\" width=\"{Number(Math.Max(X(ratio) - Label, 1))}\" height=\"{Number(Bar)}\" rx=\"2\" data-tranche=\"{at + 1}\" data-target=\"{target.Target.ToString(Invariant)}\" data-trail=\"{(target.TrailBegins ? "yes" : "no")}\" data-reward-to-risk=\"{target.RewardToRisk!.Value.ToString(Invariant)}\"><title>Tranche {at + 1}, bought at {Price(tranche.Buy)} with {Price(tranche.Risk!.Value)} at risk a share: {Price(target.Reward)} to {Price(target.Target)}{trail}, a reward to risk of {ratio.ToString("0.00", Invariant)}</title></rect>"));
            svg.Append(Formatted($"<text class=\"m-rownote\" x=\"{Number(X(ratio) + 6)}\" y=\"{Number(y + 11)}\">{ratio.ToString("0.00", Invariant)}{(past ? ", past the axis's end" : string.Empty)}</text>"));
            y += Bar + Gap;
        }

        return svg.Append("</svg>").ToString();
    }
}
