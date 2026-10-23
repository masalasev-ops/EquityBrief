using System.Text;
using EquityBrief.Core.Analysts;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Quarters;
using EquityBrief.Core.Report;

namespace EquityBrief.Web.Marks;

// From 20.13 the chart page's analysts as Yahoo and TipRanks answered for the stock: the targets' range against the price
// drawn, Yahoo's counts by month drawn as the report's rating bars draw the provider's, each firm's rating action, and on
// the operator's press each analyst's rating with the analyst's record as TipRanks scores it. Each figure is copied from
// the answer stored and dated by the ask, and none is worked out here.
// see: Marks are defined once and every screen draws from that list
// see: The chart page graphs the report's earnings and revenue beneath its chart with the analysts' figures Yahoo answers when it opens, and no rule reads them from it
// see: TipRanks' analyst ratings are asked only on a press and never past the month's limit, a stock again only 15 days after its last answer
public sealed partial class MarkRenderer
{
    // Target range: the analysts' low and high targets as a band on one line of prices, their mean and median ticked on
    // it, and the price drawn as a dot; the mean named above with how far above or below the price it stands in the rise
    // or the fall hue by its sign, the low and the high named beneath their ends, and a line saying how many analysts set
    // them and when Yahoo was asked. An answer holding no low or high falls to the target marker's words.
    public string TargetRange(string ticker, YahooAnalystView yahoo, decimal? price)
    {
        var answer = yahoo.Answer;

        if (answer.TargetLow is not { } low || answer.TargetHigh is not { } high || answer.TargetMean is not { } mean || price is not { } drawn || drawn <= 0)
        {
            return Formatted($"<p class=\"mark-short\" data-mark=\"target-range\">{(price is not { } shown || shown <= 0 ? Formatted($"No price is stored for {Escaped(ticker)} to set the analysts' targets against.") : Formatted($"Yahoo's answer of {DayOf(Day(yahoo.AskedAt))} holds no low, mean and high target for {Escaped(ticker)}, so no range is drawn."))}</p>");
        }

        const double High = 96;
        var ends = new[] { Statistic.FromPrice(low), Statistic.FromPrice(high), Statistic.FromPrice(drawn) };
        var (from, to) = Bounds(ends.Min(), ends.Max(), zero: false);
        double X(double value) => ChartLeft + ((value - from) / (to - from) * (ChartWide - ChartLeft - ChartRight - 60));
        var lowX = X(Statistic.FromPrice(low));
        var highX = X(Statistic.FromPrice(high));
        var meanX = X(Statistic.FromPrice(mean));
        var priceX = X(Statistic.FromPrice(drawn));
        var against = Percents.FromFraction(QuarterFetch.Grown(mean, drawn));
        var side = against is { } percent ? percent > 0 ? "gain" : percent < 0 ? "loss" : "flat" : "flat";
        var asked = DayOf(Day(yahoo.AskedAt));
        var svg = new StringBuilder();

        svg.Append(Formatted($"<svg class=\"target-range\" viewBox=\"0 0 {Number(ChartWide)} {Number(High)}\" role=\"img\" data-mark=\"target-range\" data-low=\"{low.ToString(Invariant)}\" data-mean=\"{mean.ToString(Invariant)}\" data-high=\"{high.ToString(Invariant)}\"{(answer.TargetMedian is { } median ? Formatted($" data-median=\"{median.ToString(Invariant)}\"") : string.Empty)} data-price=\"{drawn.ToString(Invariant)}\" aria-label=\"The analysts' targets from low to high against the price drawn\">"));
        svg.Append(Formatted($"<line class=\"m-axisline\" x1=\"{Number(ChartLeft)}\" x2=\"{Number(ChartWide - ChartRight - 60)}\" y1=\"48\" y2=\"48\"/>"));
        svg.Append(Formatted($"<rect class=\"tr-range\" x=\"{Number(lowX)}\" y=\"42\" width=\"{Number(Math.Max(highX - lowX, 2))}\" height=\"12\" rx=\"3\"><title>Yahoo's targets as asked on {asked}: low {Figures.Price(low)}, high {Figures.Price(high)}</title></rect>"));
        svg.Append(Formatted($"<rect class=\"tr-mean\" x=\"{Number(meanX - 2)}\" y=\"36\" width=\"4\" height=\"24\"><title>The mean target as asked on {asked}: {Figures.Price(mean)}</title></rect>"));

        if (answer.TargetMedian is { } middle)
        {
            var middleX = X(Statistic.FromPrice(middle));

            svg.Append(Formatted($"<rect class=\"tr-median\" x=\"{Number(middleX - 4)}\" y=\"44\" width=\"8\" height=\"8\" transform=\"rotate(45 {Number(middleX)} 48)\"><title>The median target as asked on {asked}: {Figures.Price(middle)}</title></rect>"));
        }

        svg.Append(Formatted($"<circle class=\"tm-price\" cx=\"{Number(priceX)}\" cy=\"48\" r=\"6\"><title>The price drawn: {Figures.Price(drawn)}</title></circle>"));
        svg.Append(Formatted($"<text class=\"m-tick\" x=\"{Number(meanX)}\" y=\"26\" text-anchor=\"middle\">Mean {Figures.Price(mean)}</text>"));
        svg.Append(Formatted($"<text class=\"m-tick\" x=\"{Number(lowX)}\" y=\"74\" text-anchor=\"middle\">Low {Figures.Price(low)}</text>"));
        svg.Append(Formatted($"<text class=\"m-tick\" x=\"{Number(highX)}\" y=\"74\" text-anchor=\"middle\">High {Figures.Price(high)}</text>"));
        svg.Append(Formatted($"<text class=\"m-tick\" x=\"{Number(priceX)}\" y=\"90\" text-anchor=\"middle\">Price {Figures.Price(drawn)}</text>"));
        svg.Append(against is { } away
            ? Formatted($"<text class=\"tm-against {side}\" x=\"{Number(ChartWide - ChartRight)}\" y=\"52\" text-anchor=\"end\" data-against=\"{away.ToString("R", Invariant)}\">{SignedNumber(away)}%</text>")
            : string.Empty);
        svg.Append("</svg>");

        var analysts = answer.Analysts is { } count ? Formatted($"{count} analysts") : "the analysts";
        var medianWords = answer.TargetMedian is { } held ? Formatted($", their median {Figures.Price(held)}") : string.Empty;
        var word = answer.Recommendation is { } said ? Formatted($"; Yahoo's word for their view is {Escaped(said.Replace('_', ' '))}") : string.Empty;

        svg.Append(Formatted($"<p class=\"mark-note\" data-asked=\"{asked}\">The targets {analysts} set, as Yahoo answered on {asked}{medianWords}{word}.</p>"));

        return svg.ToString();
    }

    // Yahoo's counts by month as the rating bars read the provider's: each month Yahoo labels, "0m" the month of the UTC
    // day it was asked and "-1m" the one before, oldest first, each as fetched on that day.
    public static AnalystReading YahooMonths(YahooAnalystView yahoo)
    {
        var asked = Day(yahoo.AskedAt);
        var months = new List<RatingMonth>();

        foreach (var month in yahoo.Answer.Months)
        {
            if (!month.Period.EndsWith('m') || !int.TryParse(month.Period[..^1], System.Globalization.NumberStyles.AllowLeadingSign, Invariant, out var back) || back > 0)
            {
                continue;
            }

            var first = new DateOnly(asked.Year, asked.Month, 1).AddMonths(back);

            months.Add(new RatingMonth(first.Year, first.Month, new RatingFetch(asked, month.StrongBuy, month.Buy, month.Hold, month.Sell, month.StrongSell, null, null)));
        }

        return new AnalystReading(
            asked,
            [],
            [],
            new Dictionary<string, IReadOnlyList<TrendPoint>>(),
            [.. months.OrderBy(month => month.Year).ThenBy(month => month.Month)],
            null,
            null,
            null);
    }

    // Firm actions: each firm's rating action Yahoo files, newest first and numbered, its day, the firm, the action, the
    // grade it moved from and to and the target it moved from and to, a raised target in the rise hue and a lowered one in
    // the fall hue; the first ten shown with a line saying how many of how many, and the rest behind a control.
    public string FirmActions(string ticker, YahooAnalystView yahoo)
    {
        var actions = yahoo.Answer.Actions;
        var asked = DayOf(Day(yahoo.AskedAt));

        if (actions.Count == 0)
        {
            return Formatted($"<p class=\"mark-short\" data-mark=\"firm-actions\">Yahoo's answer of {asked} files no firm's rating action for {Escaped(ticker)}.</p>");
        }

        var shown = Math.Min(actions.Count, AnalystLimits.ActionsShown);
        var html = new StringBuilder();

        html.Append(Formatted($"<div class=\"firm-actions\" data-mark=\"firm-actions\" data-actions=\"{actions.Count}\" data-shown=\"{shown}\">"));
        html.Append(Formatted($"<p class=\"muted\">Showing {shown} of {actions.Count} firm actions, newest first, as Yahoo answered on {asked}.</p>"));
        html.Append(ActionsTable(actions.Take(shown), 1));

        if (actions.Count > shown)
        {
            html.Append(Formatted($"<details class=\"more-actions\"><summary>Show the other {actions.Count - shown}</summary>"));
            html.Append(ActionsTable(actions.Skip(shown), shown + 1));
            html.Append("</details>");
        }

        return html.Append("</div>").ToString();
    }

    string ActionsTable(IEnumerable<YahooAction> actions, int first)
    {
        var table = new StringBuilder("<div class=\"tbl-wrap\"><table class=\"list-table actions-table\"><thead><tr><th class=\"r\">#</th><th>Day</th><th>Firm</th><th>Action</th><th>Rating</th><th class=\"r\">Target</th></tr></thead><tbody>");
        var at = first;

        foreach (var action in actions)
        {
            var moved = action.Target is { } target && action.PriorTarget is { } prior ? target > prior ? "gain" : target < prior ? "loss" : "flat" : "flat";
            var grade = action.ToGrade is not { } to ? "none stated"
                : action.FromGrade is { } from && !string.Equals(from, to, StringComparison.Ordinal) ? Formatted($"{Escaped(from)} to {Escaped(to)}")
                : Escaped(to);
            var aimed = action.Target is not { } now ? "none stated"
                : action.PriorTarget is { } before && before != now ? Formatted($"{Figures.Price(before)} to {Figures.Price(now)}")
                : Figures.Price(now);

            table.Append(Formatted($"<tr data-graded=\"{DayOf(Day(action.GradedAt))}\" data-firm=\"{Escaped(action.Firm)}\" data-action=\"{Escaped(action.Action)}\"{(action.Target is { } stated ? Formatted($" data-target=\"{stated.ToString(Invariant)}\"") : string.Empty)}>"));
            table.Append(Formatted($"<td class=\"r num\">{at}</td><td class=\"nw\">{DayOf(Day(action.GradedAt))}</td><td>{Escaped(action.Firm)}</td><td>{ActionWord(action.Action)}</td><td>{grade}</td><td class=\"r num {moved}\">{aimed}</td></tr>"));
            at++;
        }

        return table.Append("</tbody></table></div>").ToString();
    }

    // Yahoo's word for a firm's action, in the page's words.
    public static string ActionWord(string action) =>
        action switch
        {
            "up" => "Upgrade",
            "down" => "Downgrade",
            "init" => "Initiated",
            "main" => "Maintained",
            "reit" => "Reiterated",
            _ => Escaped(action),
        };

    // TipRanks' ratings: each analyst's newest rating TipRanks files, newest first and numbered, the analyst with the stars
    // TipRanks scores them at, the firm, the rating and its action, the target, its day, the share of the analyst's
    // ratings that made money and their average return over the market, the counts of buys, holds and sells over each
    // span above them, all dated by the press. No answer stored says so.
    public string TipRanksRatings(string ticker, TipRanksView? tipranks)
    {
        if (tipranks is null)
        {
            return Formatted($"<p class=\"mark-short\" data-mark=\"tipranks-ratings\">No TipRanks rating is stored for {Escaped(ticker)}. A press asks TipRanks once, which spends one of the month's calls.</p>");
        }

        var answer = tipranks.Answer;
        var asked = DayOf(Day(tipranks.AskedAt));
        var html = new StringBuilder();

        html.Append(Formatted($"<div class=\"tipranks-ratings\" data-mark=\"tipranks-ratings\" data-asked=\"{asked}\" data-ratings=\"{answer.Ratings.Count}\">"));

        var spans = answer.Counts
            .Select(span => Formatted($"<span data-span=\"{Escaped(span.Span)}\" data-analysts=\"{span.Analysts}\">{SpanWords(span.Span)}: {span.Analysts} analysts, {span.Buy} buy, {span.Hold} hold, {span.Sell} sell</span>"))
            .ToArray();

        html.Append(Formatted($"<p class=\"muted\">As TipRanks answered on {asked}{(answer.AsOf is { } asOf ? Formatted($", as of {DayOf(asOf)}") : string.Empty)}. {string.Join("; ", spans)}.</p>"));

        if (answer.Ratings.Count > 0)
        {
            html.Append("<div class=\"tbl-wrap\"><table class=\"list-table tipranks-table\"><thead><tr><th class=\"r\">#</th><th>Analyst</th><th>Firm</th><th>Rating</th><th class=\"r\">Target</th><th>Day</th><th class=\"r\">Stars</th><th class=\"r\">Made money</th><th class=\"r\">Average return</th></tr></thead><tbody>");

            var at = 1;

            foreach (var rating in answer.Ratings)
            {
                var returned = rating.AverageReturn is { } excess ? excess > 0 ? "gain" : excess < 0 ? "loss" : "flat" : "flat";

                html.Append(Formatted($"<tr data-analyst=\"{Escaped(rating.Analyst)}\"{(rating.Target is { } aimed ? Formatted($" data-target=\"{aimed.ToString(Invariant)}\"") : string.Empty)}>"));
                html.Append(Formatted($"<td class=\"r num\">{at}</td><td>{Escaped(rating.Analyst)}{(rating.Article is { } article ? Formatted($"<span class=\"muted tr-article\"> {Escaped(article)}</span>") : string.Empty)}</td><td>{(rating.Firm is { } firm ? Escaped(firm) : "none stated")}</td>"));
                html.Append(Formatted($"<td>{(rating.Rating is { } said ? Escaped(said) : "none stated")}{(rating.Action is { } action ? Formatted($" <span class=\"muted\">{Escaped(action)}</span>") : string.Empty)}</td>"));
                html.Append(Formatted($"<td class=\"r num\">{(rating.Target is { } target ? Figures.Price(target) : "none stated")}</td><td class=\"nw\">{(rating.RatedOn is { } on ? DayOf(on) : "none stated")}</td>"));
                html.Append(Formatted($"<td class=\"r num\">{(rating.Stars is { } stars ? stars.ToString("0.0", Invariant) : "none")}</td><td class=\"r num\">{(rating.SuccessRate is { } rate ? Figures.Share(rate) : "none")}</td><td class=\"r num {returned}\">{(rating.AverageReturn is { } average ? SignedNumber(average) + "%" : "none")}</td></tr>"));
                at++;
            }

            html.Append("</tbody></table></div>");
        }

        return html.Append("</div>").ToString();
    }

    // The month's TipRanks calls in words: how many are used and how many are left, the count starting again on its day;
    // none read yet said so.
    public static string TipRanksCallsLine(TipRanksCalls? calls, DateTimeOffset now) =>
        calls is null
            ? "<span class=\"ratings-calls\" data-calls=\"none\">TipRanks: no call read yet this month</span>"
            : Formatted($"<span class=\"ratings-calls\" data-used=\"{calls.UsedAt(now)}\" data-left=\"{calls.Left(now)}\" data-limit=\"{calls.Limit}\">TipRanks: {calls.UsedAt(now)} used, {calls.Left(now)} left this month, starting again on {DayOf(Day(calls.ResetsAt))}</span>");

    static string SpanWords(string span) =>
        span switch
        {
            "last30Days" => "30 days",
            "last90Days" => "90 days",
            "last12Months" => "12 months",
            _ => Escaped(span),
        };

    // The day an instant falls on in UTC, as the store writes its instants.
    static DateOnly Day(DateTimeOffset instant) => DateOnly.FromDateTime(instant.UtcDateTime);
}
