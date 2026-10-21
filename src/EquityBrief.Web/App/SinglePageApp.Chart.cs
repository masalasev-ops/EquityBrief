using System.Text;
using EquityBrief.Web.Marks;

namespace EquityBrief.Web.App;

// One figure of the chart page's grid: its label, the figure as the page writes it, the stored value whole on its element
// where it has one, its hue where it is signed, the page it opens where it opens one, and where it names several stocks
// the page each opens.
public sealed record GridFigure(string Label, string Shown, string? Value = null, string? Hue = null, string? Href = null, IReadOnlyList<(string Text, string Href)>? Links = null);

// One group of the chart page's grid, its heading and its figures in order.
public sealed record GridGroup(string Key, string Heading, IReadOnlyList<GridFigure> Figures);

// One route the read surface answered since it started: its pattern, how many of its times are read, and their median
// and slowest in milliseconds.
public sealed record RouteTime(string Route, int Requests, double Median, double Slowest);

// A stretch of the weekly or monthly chart holding no daily row, between the candles either side of it.
public sealed record ChartGap(DateOnly After, DateOnly Before);

// The chart page as the shell draws it: the stock, the interval shown, its candles with the averages at each, the night's
// bands, a listing rule's plan as lines, the report and ex-dividend marks, the stretches holding no daily row, the line
// naming what the weekly and monthly charts read before the store's year, the grid's groups, and the day the stock's
// report was written, none where none is.
public sealed record ChartPageView(
    string Ticker,
    string Interval,
    IReadOnlyList<ChartCandle> Candles,
    IReadOnlyList<ChartBand> Bands,
    IReadOnlyList<ChartPlan> Plans,
    IReadOnlyList<ChartEvent> Events,
    IReadOnlyList<ChartGap> Gaps,
    string? Before,
    IReadOnlyList<GridGroup> Grid,
    DateOnly? ReportWritten);

// The chart page, which every link opening a stock opens first, and the masthead it shares with the name page.
// see: Every link that opens a stock opens its chart page first, and the chart page links to its report
public sealed partial class SinglePageApp
{
    public const string ChartRoute = MarkRenderer.ChartRoute;

    // The intervals the chart page offers, each by the word its link carries.
    public static IReadOnlyList<(string Word, string Label)> Intervals { get; } =
    [
        ("daily", "Daily"),
        ("weekly", "Weekly"),
        ("monthly", "Monthly"),
    ];

    // The line the masthead carries: the ticker, the company, the index that holds it and its sector, and the price
    // now. In the regular session that is the provider's delayed quote with its own time and its change on the
    // previous close, which the worker's quote job asked for and the page's script asks for again at most every
    // five minutes while the page is open; outside it, the last stored close with its change on the day and the
    // session it is from. Levels and the plan are read from completed sessions whichever is shown, and say so.
    // The name page and the chart page draw it alike, each with its own link after the sector.
    // see: The masthead carries the index and sector beside the price the page draws, a delayed quote in the session and the last stored close outside it
    // see: The name page draws a delayed quote in the regular session, asked by a worker job at most every five minutes under a day's cap
    public static string Masthead(
        string ticker,
        IReadOnlyList<ChartBar> bars,
        NameMast? mast,
        LiveQuoteView? quote,
        QuoteSession? quoteSession,
        bool asksForQuotes,
        DateOnly? night,
        SuspectPrices? suspect,
        bool? watched,
        string link)
    {
        var session = bars.Count > 0 ? bars[^1].SessionDate : (DateOnly?)null;
        var identity = new StringBuilder();
        var live = night is null && quoteSession is { IsOpen: true } && quote is not null ? quote : null;

        identity.Append(FormattableString.Invariant($"<span class=\"m-tk\">{Escaped(ticker)}</span>"));
        identity.Append(mast?.Company is { Length: > 0 } name ? FormattableString.Invariant($"<span class=\"m-co\">{Escaped(name)}</span>") : string.Empty);
        identity.Append(mast?.Index is { Length: > 0 } || mast?.Sector is { Length: > 0 }
            ? FormattableString.Invariant($"<span class=\"m-ix\">{string.Join(" · ", new[] { mast?.Index, mast?.Sector }.Where(part => part is { Length: > 0 }).Select(part => Escaped(part!)))}</span>")
            : string.Empty);
        identity.Append(FormattableString.Invariant($"<span class=\"m-price\" data-live=\"{(live is null ? "no" : "yes")}\">{PriceNow(bars, mast, live)}</span>"));
        identity.Append(watched is { } held ? WatchControl(ticker, held) : string.Empty);

        var asOf = "<span class=\"m-when\">" + (live is { } quoted
            ? QuoteAsOf(quoted, session)
            : session is { } last
                ? FormattableString.Invariant($"As of the close of {last:yyyy-MM-dd}, the last stored price{(suspect is null ? string.Empty : ". Prices may be out of date; see below")}. Levels and the plan come from completed sessions to {last:yyyy-MM-dd}")
                : "No session is stored for this name") + "</span>";

        asOf += mast?.Sector is { Length: > 0 } sector
            ? FormattableString.Invariant($" · <a href=\"#/universe?sector={Uri.EscapeDataString(sector)}\">{Escaped(sector)}</a>{(mast.Industry is { Length: > 0 } industry ? FormattableString.Invariant($", {Escaped(industry)}") : string.Empty)}")
            : string.Empty;

        asOf += link;

        // Where the page asks for its quote: its route, and the session's open and close, so the shell's script asks
        // only inside it, while the page is shown, and no more often than the interval. A page about an earlier night
        // and the exported file ask for none.
        var asks = night is null && quoteSession is { } window && asksForQuotes
            ? FormattableString.Invariant($" data-quote-route=\"{QuoteRoute}{Escaped(ticker)}\" data-session-open=\"{window.Open.UtcDateTime:yyyy-MM-ddTHH:mm:ssZ}\" data-session-close=\"{window.Close.UtcDateTime:yyyy-MM-ddTHH:mm:ssZ}\" data-interval-minutes=\"{EquityBrief.Core.Quotes.QuoteLimits.IntervalMinutes}\" data-asked=\"{(quote is { } stored ? FormattableString.Invariant($"{stored.AskedAt.UtcDateTime:yyyy-MM-ddTHH:mm:ssZ}") : string.Empty)}\"")
            : string.Empty;

        return Cards.Masthead(ticker, identity.ToString(), asOf, asks);
    }

    // The chart page: the name page's masthead, the line opening the report or saying none is written with the name
    // page's own request, the chart full width above everything else with its interval links, its bands' toggle and
    // the line the pointer reads, then the grid of stored figures in groups. The index the page was opened under is kept
    // in every link the page draws.
    public string ChartRegion(
        MarkRenderer marks,
        ChartPageView view,
        IReadOnlyList<ChartBar> bars,
        NameMast? mast,
        LiveQuoteView? quote,
        QuoteSession? quoteSession,
        SuspectPrices? suspect,
        bool? watched,
        string? universe)
    {
        var ticker = view.Ticker;
        var kept = universe is { Length: > 0 } index ? "&universe=" + Uri.EscapeDataString(index) : string.Empty;
        var region = new StringBuilder();

        region.Append(FormattableString.Invariant($"<section class=\"chart-page\" data-ticker=\"{Escaped(ticker)}\" data-interval=\"{Escaped(view.Interval)}\">"));
        region.Append(marks.PricesSuspect(ticker, suspect));
        region.Append(Masthead(ticker, bars, mast, quote, quoteSession, asksForQuotes: true, night: null, suspect, watched, string.Empty));

        // The report: a link to it with the day it was written, or that none is written and the name page's request.
        region.Append(view.ReportWritten is { } written
            ? FormattableString.Invariant($"<p class=\"report-line\" data-report=\"written\" data-written=\"{written:yyyy-MM-dd}\"><a class=\"btn open-report\" href=\"{NameRoute}{Uri.EscapeDataString(ticker)}\">Open the report</a> <span class=\"muted\">written {written:yyyy-MM-dd}</span></p>")
            : FormattableString.Invariant($"<div class=\"report-line\" data-report=\"none\"><p>No report is written yet for {Escaped(ticker)}. <a href=\"{NameRoute}{Uri.EscapeDataString(ticker)}\">Its page</a> holds every computed figure meanwhile.</p>{MarkRenderer.AskForAReport(ticker)}</div>"));

        var chart = new StringBuilder();

        chart.Append("<div class=\"chart-controls\"><nav class=\"intervals\" aria-label=\"The chart's interval\">");

        foreach (var (word, label) in Intervals)
        {
            var current = string.Equals(word, view.Interval, StringComparison.Ordinal);

            chart.Append(FormattableString.Invariant($"<a class=\"interval{(current ? " on" : string.Empty)}\" data-interval=\"{word}\" href=\"{ChartRoute}{Uri.EscapeDataString(ticker)}?interval={word}{kept}\"{(current ? " aria-current=\"page\"" : string.Empty)}>{label}</a>"));
        }

        chart.Append("</nav>");
        chart.Append(view.Bands.Count > 0
            ? "<button type=\"button\" class=\"btn-2 bands-toggle\" data-bands-toggle aria-pressed=\"false\">Show the night's levels</button>"
            : "<span class=\"muted bands-none\">No level stored for the night</span>");
        chart.Append("</div>");

        chart.Append(view.Candles.Count > 0
            ? FormattableString.Invariant($"<p class=\"chart-reading\" aria-live=\"polite\">{MarkRenderer.ChartReading(view.Candles, view.Candles.Count - 1)}</p>")
            : string.Empty);
        chart.Append("<div class=\"fig chart-fig\">").Append(marks.CandleChart(ticker, view.Interval, view.Candles, view.Bands, view.Plans, view.Events)).Append("</div>");

        foreach (var gap in view.Gaps)
        {
            chart.Append(FormattableString.Invariant($"<p class=\"chart-gap\" data-after=\"{gap.After:yyyy-MM-dd}\" data-before=\"{gap.Before:yyyy-MM-dd}\">No daily row is stored between {gap.After:yyyy-MM-dd} and {gap.Before:yyyy-MM-dd}, so the chart draws no candle there.</p>"));
        }

        chart.Append(view.Before is { } before ? FormattableString.Invariant($"<p class=\"chart-before\">{Escaped(before)}</p>") : string.Empty);
        chart.Append(Cards.Key(
            "How to read it.",
            "A candle is a session on the daily chart, a week from Monday to Friday on the weekly one and a calendar month on the monthly one: its body runs from the first session's open to the last session's close, hollow where it closed up and filled where it closed down, and its line from the highest high to the lowest low. The three lines are the 20, 50 and 200 session averages, named above the chart with their values at the candle under the pointer. Beneath, each bar is the volume traded, green where the candle closed above the one before and red where it closed below. R marks a report and D an ex-dividend date. Where a rule lists the stock tonight, its buy at the next open, its stop and its target are drawn across the chart with the rule's name. The toggle shows the levels the night computed, green for support below the price and orange for resistance above it. The weekly and monthly charts read the history pulled before the stored year at the stored prices.",
            "The chart draws what the store holds and decides nothing. Open the report for what the rules made of the stock."));

        var last = bars.Count > 0 ? bars[^1].SessionDate : (DateOnly?)null;

        region.Append(Cards.Computed("The chart", chart.ToString(), title: FormattableString.Invariant($"{Escaped(ticker)}, {view.Interval}"), stamp: Cards.Night(last), id: "chart", region: "chart-page-chart"));

        var grid = new StringBuilder();

        grid.Append("<div class=\"chart-grid\">");

        foreach (var group in view.Grid)
        {
            grid.Append(FormattableString.Invariant($"<section class=\"grid-group\" data-group=\"{Escaped(group.Key)}\"><h3>{Escaped(group.Heading)}</h3><dl>"));

            foreach (var figure in group.Figures)
            {
                var shown = figure.Links is { Count: > 0 } links
                    ? string.Join(", ", links.Select(link => FormattableString.Invariant($"<a href=\"{Escaped(link.Href)}{kept.Replace("&", "?", StringComparison.Ordinal)}\">{Escaped(link.Text)}</a>")))
                    : figure.Href is { } href
                        ? FormattableString.Invariant($"<a href=\"{Escaped(href)}\">{Whole(Escaped(figure.Shown))}</a>")
                        : Whole(Escaped(figure.Shown));
                var classes = string.Join(' ', new[] { figure.Hue, figure.Shown == NotStored ? "degraded" : null }.Where(part => part is not null));

                grid.Append(FormattableString.Invariant($"<dt>{Escaped(figure.Label)}</dt><dd{(classes.Length > 0 ? FormattableString.Invariant($" class=\"{classes}\"") : string.Empty)}{(figure.Value is { } value ? FormattableString.Invariant($" data-value=\"{Escaped(value)}\"") : string.Empty)}>{shown}</dd>"));
            }

            grid.Append("</dl></section>");
        }

        grid.Append("</div>");
        grid.Append(Cards.Key(
            "How to read it.",
            "Every figure is read from what the store holds for the stock, worked by the same function the stock's report page uses for it. A figure EquityBrief fetches and holds none of for this stock says not stored, and each dividend figure says pays none where the company's fetch files a dividend rate of nought and no dividend is kept in the year. The returns are the newest close over the close the span reaches back to, over the stored year and, for the longer spans, the month-end closes of the history pulled before it.",
            "A figure here is context for the chart and no rule reads it from this page."));

        region.Append(Cards.Computed("The figures", grid.ToString(), title: "The stock in figures", stamp: Cards.Night(last), id: "figures", region: "chart-page-figures"));
        region.Append("</section>");

        return region.ToString();
    }

    // What a figure EquityBrief fetches and holds none of for a stock says.
    public const string NotStored = "not stored";

    // What each dividend figure says for a company paying none.
    // see: A company filing a dividend rate of nought with no dividend kept in the year is drawn as paying none, and one storing neither as not stored
    public const string PaysNone = "pays none";

    // A figure's text with each date in it held on one line, since a browser breaks a line at a date's hyphens.
    static string Whole(string escaped) =>
        System.Text.RegularExpressions.Regex.Replace(escaped, @"\b\d{4}-\d{2}-\d{2}\b", date => "<span class=\"nw\">" + date.Value + "</span>");

    // Every page's time on the Run page, from 19.11: the slowest routes since the read surface started, each with how
    // many of its times are read, their median and their slowest, kept in memory and read off no store.
    // see: Each request's route and server time is kept in memory for the read surface's life and drawn on the Run page, and no store holds it
    static string RouteTimesRegion(IReadOnlyList<RouteTime>? times, DateTimeOffset? since)
    {
        if (times is null)
        {
            return string.Empty;
        }

        var body = new StringBuilder();
        var started = since is { } at ? FormattableString.Invariant($" since it started at {at.UtcDateTime:yyyy-MM-dd HH:mm} UTC") : string.Empty;

        if (times.Count == 0)
        {
            body.Append(FormattableString.Invariant($"<p class=\"degraded\" data-route-times=\"0\">No request is timed yet{started}.</p>"));
        }
        else
        {
            body.Append(FormattableString.Invariant($"<div class=\"tbl-wrap\"><table class=\"list-table route-times\" data-route-times=\"{times.Count}\"><thead><tr><th>Route</th><th class=\"r\">Requests</th><th class=\"r\">Median</th><th class=\"r\">Slowest</th></tr></thead><tbody>"));

            foreach (var time in times)
            {
                body.Append(FormattableString.Invariant($"<tr data-route=\"{Escaped(time.Route)}\" data-requests=\"{time.Requests}\" data-median=\"{time.Median.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)}\" data-slowest=\"{time.Slowest.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)}\">"));
                body.Append(FormattableString.Invariant($"<td><code>{Escaped(time.Route)}</code></td><td class=\"r num\">{time.Requests}</td><td class=\"r num\">{time.Median.ToString("#,##0", System.Globalization.CultureInfo.InvariantCulture)} ms</td><td class=\"r num\">{time.Slowest.ToString("#,##0", System.Globalization.CultureInfo.InvariantCulture)} ms</td></tr>"));
            }

            body.Append("</tbody></table></div>");
        }

        body.Append(Cards.Key(
            "How to read it.",
            FormattableString.Invariant($"Each row is a route the read surface answered{started}, the slowest first: how many of its times are read, the newest {EquityBrief.Web.App.SinglePageApp.RouteTimesKept} at most, and the median and slowest time the server took to answer it, in milliseconds. The chart page's route is named with its interval, since each reads its own rows. The times are kept in memory alone, so a restart starts them again."),
            "A slow route is a finding for a ruling of its own; this region changes no page."));

        return Cards.Computed("Every page's time", body.ToString(), title: "Every page's time", region: "route-times");
    }

    // The most times a route keeps, which the Run page's key states and the read surface's record keeps to.
    public const int RouteTimesKept = 1000;
}
