using System.Globalization;
using System.Text;
using EquityBrief.Core.Components;
using EquityBrief.Web.Marks;

namespace EquityBrief.Web.App;

// The shell the browser loads once, and the routes it answers.
//
// Section 15.4 puts the shell and the marks on the server and the routing in
// the page. So this writes the shell and nothing else: the marks arrive already
// drawn, and the only thing the browser does is ask for the one belonging to
// the route it is on. A page that assembled a mark from values would be the
// second renderer the marks decision exists to prevent.
// see: Marks are defined once and every screen draws from that list
// see: A screen reads and renders, and computes nothing
//
// At 1.3 it answered one route drawing a name's candles. At 4.1 that route asks
// for the name screen's chart region, which the server composes from the marks
// section 15.9 lists, because the profile is drawn against the chart's own price
// axis and a second request would be a second axis. The other four screens
// arrive with the data behind them, and this file is where they are added.
public sealed class SinglePageApp : IComponent
{
    // It reads the read API and touches no store, which is its catalogue row
    // and its blank matrix cells.
    public static ComponentAccess Access => ComponentAccess.Nothing;

    public const string NameRoute = "#/name/";

    // The universe route, section 15.3's second. Filters live in the hash so a
    // filtered view is a link, which is what that section means by a route being
    // a link: what is on screen can be sent to another machine and be the same
    // thing.
    public const string UniverseRoute = "#/universe";

    // The hash route, so one document serves every screen and the browser never
    // asks the server for a page it already has.
    //
    // The script is four lines and renders nothing. It reads the hash and puts
    // the server's own markup where it goes, which is what keeps the rule that
    // a mark needs no script to draw.
    public string Shell(string title) =>
        $$"""
        <!doctype html>
        <html lang="en">
        <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1">
        <title>{{Escaped(title)}}</title>
        <style>
          :root { --ink: #1c1c1c; --muted: #6a6a6a; --rule: #d8d8d8; --panel: #f6f6f4; }
          body { margin: 0; padding: 24px; font: 14px/1.5 "Segoe UI", system-ui, sans-serif; color: var(--ink); }
          h1 { font-size: 19px; margin: 0 0 4px; }
          p.route { color: var(--muted); margin: 0 0 18px; }
          .degraded { color: var(--muted); }
        </style>
        </head>
        <body>
        <h1>{{Escaped(title)}}</h1>
        <p class="route">The index at <code>#/universe</code>, a name at <code>#/name/AAPL</code>.</p>
        <main id="screen"></main>
        <script>
        async function show() {
          const hash = location.hash;
          const screen = document.getElementById('screen');
          if (hash.startsWith('{{UniverseRoute}}')) {
            const query = hash.slice('{{UniverseRoute}}'.length);
            const universe = await fetch('/screens/universe' + query);
            screen.innerHTML = await universe.text();
            return;
          }
          if (!hash.startsWith('{{NameRoute}}')) { screen.innerHTML = ''; return; }
          const ticker = encodeURIComponent(hash.slice('{{NameRoute}}'.length));
          const response = await fetch('/screens/name/' + ticker);
          screen.innerHTML = await response.text();
        }
        addEventListener('hashchange', show);
        show();
        </script>
        </body>
        </html>
        """;

    // The name screen's chart region, composed from stored values.
    //
    // Section 15.9 puts the level chart, the volume profile on the same price
    // axis, the momentum panel and the level summary table in one region, and
    // every one of those marks has existed since 3.5 while the app served none
    // of them: the route drew candles and averages, and the rest were asserted
    // against stores the suite built and drawn on no page.
    //
    // It is one region rather than four requests because the profile is drawn
    // against the chart's own price axis, and a second request would be a second
    // axis. It computes nothing: every value here arrives already stored, and
    // the only arithmetic is the axis the mark renderer itself derives.
    // see: A screen reads and renders, and computes nothing
    // see: Marks are defined once and every screen draws from that list
    public string NameRegion(
        MarkRenderer marks,
        string ticker,
        IReadOnlyList<ChartBar> bars,
        IReadOnlyList<ChartAverage> averages,
        IReadOnlyList<ChartBand> bands,
        IReadOnlyList<ProfileBand> profile,
        IReadOnlyList<MomentumReading> readings,
        IReadOnlyList<SummaryBand> summary,
        IReadOnlyList<AbsentAverage> absent,
        string? trendState,
        DateOnly? trendAsOf,
        DateOnly? nextEvent,
        IReadOnlyList<PlanRow> plan,
        decimal close,
        string eventBook,
        string arithmetic,
        IReadOnlyList<MoveCell> moves)
    {
        var region = new StringBuilder();

        region.Append(Invariant($"<section class=\"name\" data-ticker=\"{Escaped(ticker)}\">"));

        // The trend state, in a word. Read off the ladder row rather than worked
        // out here, and a name with no row says so rather than showing nothing:
        // an absence stated and an absence drawn as emptiness are different
        // things, and only the first is readable.
        region.Append(trendState is null
            ? "<p class=\"trend-state\" data-trend-state=\"none\">no ladder row for this name yet</p>"
            : Invariant($"<p class=\"trend-state\" data-trend-state=\"{Escaped(trendState)}\" data-as-of=\"{trendAsOf:yyyy-MM-dd}\">{Escaped(trendState.Replace('_', ' '))}</p>"));

        // The next dated event, which section 15.9 puts in the fact strip. A
        // name with none says the date is not on file rather than showing an
        // empty space: a guessed date is a wrong date, and a blank is a date a
        // reader supplies themselves.
        region.Append(nextEvent is null
            ? "<p class=\"fact-strip\" data-next-event=\"none\">next dated event: not on file</p>"
            : Invariant($"<p class=\"fact-strip\" data-next-event=\"{nextEvent:yyyy-MM-dd}\">next dated event: {nextEvent:yyyy-MM-dd}</p>"));

        region.Append(marks.LevelChart(ticker, bars, averages, bands));

        if (bars.Count > 0 && profile.Count > 0)
        {
            region.Append(marks.VolumeProfile(ticker, profile, marks.AxisFor(bars, averages)));
        }

        // How it got here, which section 15.9 puts after the chart region. Its
        // cause column arrives at 6.5 and is absent rather than blank until
        // then, stated once by the table rather than in every row.
        region.Append(marks.MovesTable(ticker, moves));

        region.Append(marks.MomentumPanel(ticker, readings));
        region.Append(marks.LevelSummary(ticker, summary, absent));

        // The plan region, which section 15.9 puts after the chart: the plan
        // column and the two tables it is read beside.
        region.Append(marks.PlanColumn(ticker, close, plan));
        region.Append(marks.PlanTables(ticker, plan));

        // The second book, which section 15.9 puts in the plan region beside the
        // tranche and exit tables. It arrives already written, because what it
        // holds is prose and stored figures rather than a mark.
        region.Append(eventBook);

        // The sizing arithmetic and the earnings rule, which section 15.9 puts
        // last in the plan region. It arrives written for the same reason the
        // event book does.
        region.Append(arithmetic);

        region.Append("</section>");

        return region.ToString();
    }

    // The universe screen's three regions, composed from stored values.
    //
    // Section 15.8 answers where everything sits, including the names nothing
    // happened to, so the population is the index and not the names with bars. A
    // name the night computed nothing for is a row saying so.
    //
    // The regions the listings store feeds arrive at 5.4, when that store
    // exists: how many names in a sector are on tonight's list, the evening a
    // name was last on it, and the listing strip. Each is absent and says so
    // rather than being drawn as a zero, which would read as nothing having
    // fired.
    // see: A screen reads and renders, and computes nothing
    public string UniverseRegion(
        MarkRenderer marks,
        IReadOnlyList<UniverseCell> rows,
        IReadOnlyList<SectorLine> sectors,
        string? trendFilter = null,
        string? sectorFilter = null)
    {
        var shown = rows
            .Where(row => trendFilter is null || (row.TrendState ?? "not classified") == trendFilter)
            .Where(row => sectorFilter is null || row.Sector == sectorFilter)
            .ToArray();

        var region = new StringBuilder();

        region.Append(Invariant($"<section class=\"universe\" data-names=\"{rows.Count}\" data-shown=\"{shown.Length}\" "));
        region.Append(Invariant($"data-trend-filter=\"{Escaped(trendFilter ?? "all")}\" data-sector-filter=\"{Escaped(sectorFilter ?? "all")}\">"));

        region.Append(marks.SectorStrip(sectors));
        region.Append(marks.UniverseFilters(rows));
        region.Append(marks.UniverseTable(shown));

        region.Append("</section>");

        return region.ToString();
    }

    static string Invariant(FormattableString text) =>
        text.ToString(CultureInfo.InvariantCulture);

    static string Escaped(string text) =>
        text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
}
