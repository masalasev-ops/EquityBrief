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

    // Tonight's list, section 15.3's first route. `#/` resolves to the newest
    // night and `#/night/<date>` to an earlier one, which is the pair 15.7
    // names and which 15.3's own list lacked until 5.0.
    public const string NightRoute = "#/night/";

    // The run page, section 15.3's last route. The date is the night's, resolved
    // through the clock rather than through the UTC date the run log carries,
    // because a run that starts after the close in New York carries tomorrow's.
    public const string RunRoute = "#/run/";

    // Where the name page's control sends a press, and the header the page's own script
    // puts on it. A form another site's page submits to this address carries no such
    // header, and a request carrying one from another origin is one the browser asks
    // about first and this surface never answers, so a pass is started by this page and
    // not by any page that can reach the machine.
    // see: A pass is started only by a request carrying the name page's own header
    public const string PassRoute = "/passes/";
    public const string PassHeader = "X-EquityBrief-Pass";
    public const string PassHeaderValue = "name-page";

    // The hash route, so one document serves every screen and the browser never
    // asks the server for a page it already has.
    //
    // The script renders nothing. It reads the hash and puts the server's own
    // markup where it goes, which is what keeps the rule that a mark needs no
    // script to draw.
    //
    // It splits the hash into a path and a query before it routes, so the three
    // things section 15.4 says the app carries are one mechanism: routing is the
    // path, and filters and selection are the query. Written the other way, with
    // each route slicing the whole hash, `#/?name=AAPL` matched no route at all
    // and the front page went blank on a selected name.
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
          const cut = hash.indexOf('?');
          const path = cut < 0 ? hash : hash.slice(0, cut);
          const query = cut < 0 ? '' : hash.slice(cut + 1);
          if (path === '' || path === '#/' || path.startsWith('{{NightRoute}}')) {
            const date = path.startsWith('{{NightRoute}}') ? path.slice('{{NightRoute}}'.length) : '';
            const night = date === '' ? '' : '/' + encodeURIComponent(date);
            const tonight = await fetch('/screens/tonight' + night + (query ? '?' + query : ''));
            screen.innerHTML = await tonight.text();
            return;
          }
          if (path.startsWith('{{RunRoute}}')) {
            const night = encodeURIComponent(path.slice('{{RunRoute}}'.length));
            const run = await fetch('/screens/run/' + night);
            screen.innerHTML = await run.text();
            return;
          }
          if (path.startsWith('{{UniverseRoute}}')) {
            const universe = await fetch('/screens/universe' + (query ? '?' + query : ''));
            screen.innerHTML = await universe.text();
            return;
          }
          if (!path.startsWith('{{NameRoute}}')) { screen.innerHTML = ''; return; }
          const ticker = encodeURIComponent(path.slice('{{NameRoute}}'.length));
          const response = await fetch('/screens/name/' + ticker);
          screen.innerHTML = await response.text();
        }
        addEventListener('hashchange', show);
        // A research control is a form, sent here with the page's own header so the
        // surface knows the press came from this page, and what the surface said back is
        // put beside the control. The pass runs as a process of its own and lands on the
        // run log, so the page is read again when the operator returns to it.
        document.addEventListener('submit', async (event) => {
          const form = event.target;
          if (!(form instanceof HTMLFormElement) || !form.classList.contains('research-control')) { return; }
          event.preventDefault();
          for (const button of form.querySelectorAll('button')) { button.disabled = true; }
          const response = await fetch(form.getAttribute('action'), {
            method: 'POST',
            headers: { '{{PassHeader}}': '{{PassHeaderValue}}' },
            body: new URLSearchParams(new FormData(form)),
          });
          form.insertAdjacentHTML('afterend', await response.text());
        });
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
        string factStrip,
        IReadOnlyList<PlanRow> plan,
        decimal close,
        string eventBook,
        string arithmetic,
        string numbers,
        IReadOnlyList<MoveCell> moves,
        IReadOnlyList<ChartBar> twelveMonths,
        IReadOnlyList<FiredReason> firedReasons,
        string? previousOnTheList,
        string? nextOnTheList,
        IReadOnlyList<LeftOutSection>? leftOut = null,
        ResearchStateLine? researchState = null,
        CauseSource? causes = null,
        IReadOnlyList<LeftOutSection>? notWritten = null,
        string? provenance = null,
        ResearchPausedLine? paused = null,
        IReadOnlyList<WrittenCell>? written = null,
        IReadOnlyList<SourceCell>? sources = null,
        IReadOnlyList<DateCell>? dates = null,
        ResearchPassLine? pass = null,
        IReadOnlyList<ResearchControl>? controls = null,
        ResearchCost? cost = null,
        SuspectPrices? suspect = null)
    {
        var region = new StringBuilder();
        var sections = written ?? [];
        var documents = sources ?? [];

        // The written sections drawn in one place, each where section 4 puts it.
        void Draw(IReadOnlyList<string> placed)
        {
            foreach (var name in placed)
            {
                if (sections.FirstOrDefault(section => string.Equals(section.Section, name, StringComparison.Ordinal)) is { } section)
                {
                    region.Append(marks.WrittenSection(ticker, section, documents));
                }
            }
        }

        region.Append(Invariant($"<section class=\"name\" data-ticker=\"{Escaped(ticker)}\">"));

        // Where the name's stored series is suspect, first, since every figure after it is
        // computed over those prices. Inside the region, so the exported report carries it.
        region.Append(marks.PricesSuspect(ticker, suspect));

        // The trend state, in a word. Read off the ladder row rather than worked
        // out here, and a name with no row says so rather than showing nothing:
        // an absence stated and an absence drawn as emptiness are different
        // things, and only the first is readable.
        region.Append(trendState is null
            ? "<p class=\"trend-state\" data-trend-state=\"none\">no ladder row for this name yet</p>"
            : Invariant($"<p class=\"trend-state\" data-trend-state=\"{Escaped(trendState)}\" data-as-of=\"{trendAsOf:yyyy-MM-dd}\">{Escaped(trendState.Replace('_', ' '))}</p>"));

        // The fact strip, which section 15.9 puts above the chart and which states
        // seven things rather than one. It arrives already written, for the reason
        // the event book does: two of its seven parts are fundamentals and no mark
        // renders them. A name with no value for a part says so rather than
        // showing an empty space, because a guessed figure is a wrong figure and a
        // blank is one a reader supplies themselves.
        region.Append(factStrip);

        // Why it is here, which section 15.9 puts above the chart and which is
        // present only when the name is on tonight's list.
        region.Append(marks.WhyItIsHere(ticker, firedReasons));

        // Where the research stands, what the newest pass came to, the sections left out
        // or not written with the reason for each, and the controls with what research
        // has cost stated beside them. Above the short version, because it is the answer
        // to whether there is research to read at all.
        region.Append(marks.LeftOut(ticker, leftOut ?? [], researchState, notWritten, paused, pass, controls, cost));

        // The short version, section 4's first section, with its date and model beneath.
        Draw(AtTheTop);

        region.Append(marks.LevelChart(ticker, bars, averages, bands));

        if (bars.Count > 0 && profile.Count > 0)
        {
            region.Append(marks.VolumeProfile(ticker, profile, marks.AxisFor(bars, averages)));
        }

        // How it got here, which section 15.9 puts after the chart region. Its
        // cause column is drawn where a cause section has been accepted for the
        // name and is absent rather than blank where none has, stated once by the
        // table rather than in every row.
        region.Append(marks.MovesTable(ticker, moves, twelveMonths, causes));

        // What the company sells, section 4's third section, before the numbers.
        Draw(BeforeTheNumbers);

        // The numbers, which section 4 puts fourth and which section 15.9 draws
        // after the table of moves. It arrives already written, for the reason the
        // event book does: what it holds is stored figures and the sentences that
        // state an absence, rather than a mark.
        region.Append(numbers);

        // The industry cycle and the two cases, section 4's fifth and sixth.
        Draw(AfterTheNumbers);

        region.Append(marks.MomentumPanel(ticker, readings));
        region.Append(marks.LevelSummary(ticker, summary, absent));

        // The key under each figure, beneath the figures it explains.
        Draw(UnderTheFigures);

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

        // What would make this wrong, section 4's ninth, after the plan it is about.
        Draw(AfterThePlan);

        // Dates and sources, section 4's last two: the calendar, the dated items a pass
        // read out of the documents, and every document the written sections cite.
        region.Append(marks.DatesAndSources(
            ticker,
            dates ?? [],
            sections.FirstOrDefault(section => string.Equals(section.Section, InTheDates, StringComparison.Ordinal)),
            documents));

        // The walk, which section 15.9 puts last: previous and next on tonight's
        // list, so an evening's reading is one pass through with no return to
        // the list.
        region.Append(marks.Walk(ticker, previousOnTheList, nextOnTheList));

        // The provenance footer, which section 15.9 puts last, arriving written for
        // the reason the event book does: what it holds is dates and names read off
        // the stores rather than a mark over values.
        region.Append(provenance ?? string.Empty);

        region.Append("</section>");

        return region.ToString();
    }

    // Where each written section is drawn, which is section 4's order: the short version
    // at the top, what the company sells before the numbers, the cycle and the two cases
    // after them, the key beneath the figures, the risks after the plan, and the dated
    // items with the dates. The cause of each large move is drawn in the moves table, in
    // the row of the move each sentence names, and `read-surface` asserts every section
    // figure 12.2 names is placed exactly once across these and that table.
    public static readonly string[] AtTheTop = ["The short version"];
    public static readonly string[] BeforeTheNumbers = ["What the company sells", "The segment commentary"];
    public static readonly string[] AfterTheNumbers = ["The industry cycle", "The two cases"];
    public static readonly string[] UnderTheFigures = ["The key under each figure"];
    public static readonly string[] AfterThePlan = ["The risks, each with what would confirm it"];
    public const string InTheDates = "The dated calendar items";
    public const string InTheMovesTable = "The cause of each large move";

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
        string? sectorFilter = null,
        IReadOnlyList<UniverseCell>? page = null,
        int at = 1,
        int pageSize = 0,
        IReadOnlyList<DateOnly>? writtenBeforeTheCorrection = null)
    {
        var shown = rows
            .Where(row => trendFilter is null || (row.TrendState ?? "not classified") == trendFilter)
            .Where(row => sectorFilter is null || row.Sector == sectorFilter)
            .ToArray();

        // The page the reader asked for, cut from the filtered rows by the
        // projection and handed here already cut. A caller that hands none is
        // drawing every filtered row, which is the whole table and what this did
        // until 5.8.
        var drawn = page ?? shown;

        var region = new StringBuilder();

        region.Append(Invariant($"<section class=\"universe\" data-names=\"{rows.Count}\" data-shown=\"{shown.Length}\" "));
        region.Append(Invariant($"data-drawn=\"{drawn.Count}\" data-page=\"{at}\" "));
        region.Append(Invariant($"data-trend-filter=\"{Escaped(trendFilter ?? "all")}\" data-sector-filter=\"{Escaped(sectorFilter ?? "all")}\">"));

        region.Append(WrittenBeforeTheCorrectionLine(writtenBeforeTheCorrection));
        region.Append(marks.SectorStrip(sectors));
        region.Append(marks.UniverseFilters(rows));
        region.Append(marks.UniverseTable(drawn));
        region.Append(marks.UniversePaging(shown.Length, at, pageSize > 0 ? pageSize : Math.Max(1, drawn.Count), trendFilter, sectorFilter));

        region.Append("</section>");

        return region.ToString();
    }

    // Tonight's list, section 15.7's four regions that the listings store feeds.
    //
    // The header states the true fired count over the whole index, the watch
    // list sits above the list rather than inside it, the list draws at most
    // twenty, and the selected name carries its plan column and level summary so
    // the common case of checking a plan needs no navigation.
    // see: The page shows twenty and states the true count
    public string TonightRegion(
        MarkRenderer marks,
        DateOnly night,
        int index,
        int fired,
        string? duration,
        IReadOnlyList<ListingCell> rows,
        IReadOnlyList<ListingCell> watched,
        string selectedName,
        HarnessCounts? harness,
        string? selectedTicker = null,
        IReadOnlyList<ReasonRecord>? records = null,
        IReadOnlyList<ReasonTrackRow>? totals = null,
        NightSpend? spend = null,
        NightProse? prose = null,
        IReadOnlyList<DateOnly>? writtenBeforeTheCorrection = null)
    {
        var region = new StringBuilder();

        region.Append(Invariant($"<section class=\"tonight\" data-night=\"{night:yyyy-MM-dd}\" data-index=\"{index}\" data-fired=\"{fired}\" "));
        region.Append(Invariant($"data-selected=\"{Escaped(selectedTicker ?? "none")}\">"));

        region.Append(marks.NightHeader(night, index, fired, duration, harness, spend, prose));
        region.Append(WrittenBeforeTheCorrectionLine(writtenBeforeTheCorrection));
        region.Append(marks.WatchList(watched));
        region.Append(marks.TonightList(rows, TonightDrawn, records));

        // The selected name's plan and level summary, which is what section 15.7
        // means by the common case needing no navigation. It arrives already
        // composed, because the marks it holds are the name screen's.
        region.Append(selectedName);

        // The reason totals, 15.7's last region, drawn beneath the list because
        // it is a statement about the evening rather than about a name in it.
        if (totals is not null)
        {
            region.Append(marks.ReasonTotals(totals));
        }

        region.Append("</section>");

        return region.ToString();
    }

    // Section 17's list display count, held here so the app and the projection
    // agree about it rather than each stating it.
    public const int TonightDrawn = 20;

    // Section 18's row for listings written before the 5.4 correction: the rows
    // stay as written, since a listing records what that night listed, and a
    // route drawing a session they belong to says what they could not do. Absent
    // for a session written since, which is every session from the correction on.
    // see: Sessions to a dated event are counted on the exchange calendar and never on stored bars
    public const string WrittenBeforeTheCorrectionText =
        "earnings soon on those rows counted every future print as tonight's, and breakout on volume could not fire";

    static string WrittenBeforeTheCorrectionLine(IReadOnlyList<DateOnly>? sessions)
    {
        if (sessions is not { Count: > 0 })
        {
            return string.Empty;
        }

        var dates = string.Join(", ", sessions.Select(session => session.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));

        return $"<p class=\"written-before-correction\" data-sessions=\"{Escaped(dates)}\">The listings for {Escaped(dates)} were written before a correction: {WrittenBeforeTheCorrectionText}.</p>";
    }

    // The run page, section 15.10's six regions, in the order that section
    // states them.
    //
    // Two of the six are absent and say so. The shadow candidates need the
    // candidate register, which phase 8 builds, and the reason records' verdict
    // half needs resolved setups, which no checkpoint accumulates. Each is
    // stated rather than drawn empty, because an empty region reads as a night
    // that produced nothing.
    //
    // It is deliberately not a status dashboard. A page of green tiles invites a
    // glance and this one is meant to be read, which is why every region is a
    // table of counts rather than a light.
    public string RunRegion(
        MarkRenderer marks,
        DateOnly night,
        IReadOnlyList<StageRow> stages,
        IReadOnlyList<StageRow> failed,
        IReadOnlyList<ReasonRecord> records,
        IReadOnlyList<ReasonTrackRow> tracks,
        IReadOnlyList<BaseRateLine> baseRates,
        int nights,
        IReadOnlyList<string> stale,
        IReadOnlyList<RefusedDocument> refused,
        IReadOnlyList<LeftOutSection> fellBack,
        QueueNight queue,
        HarnessCounts? harness,
        PricedCalls? priced = null,
        IReadOnlyList<DateOnly>? writtenBeforeTheCorrection = null)
    {
        var region = new StringBuilder();

        region.Append(Invariant($"<section class=\"run\" data-night=\"{night:yyyy-MM-dd}\" data-stages=\"{stages.Count}\">"));

        region.Append(marks.OperationalHeader(night, stages, priced));
        region.Append(WrittenBeforeTheCorrectionLine(writtenBeforeTheCorrection));
        region.Append(marks.ReasonRecords(records, tracks, baseRates, nights));

        region.Append("<section class=\"shadow-candidates\" data-shadow=\"absent\">");
        region.Append("<p class=\"degraded\">registered candidates that are not on the list, and the correction divisor beside each threshold, arrive with the register at 8.4</p>");
        region.Append("</section>");

        region.Append(marks.StaleAndFailed(stale, failed, refused, fellBack));
        region.Append(marks.OvernightQueue(queue));
        region.Append(marks.HarnessVerdicts(harness));

        region.Append("</section>");

        return region.ToString();
    }

    // Section 18's banner half. The bulk price feed not answering keeps last
    // night's bars, and what a reader must not be shown is tonight's list built
    // from them: the list is absent rather than wrong, and the banner gives the
    // data date so the absence is legible rather than a page that failed.
    //
    // A night the store has no listings for is the same shape from the reader's
    // side, whatever caused it, so this is the one branch and it names the date
    // the store does have.
    public string StaleBanner(DateOnly asked, DateOnly? held)
    {
        var banner = new StringBuilder();

        banner.Append(Invariant($"<section class=\"tonight\" data-night=\"{asked:yyyy-MM-dd}\" data-list=\"absent\" "));
        banner.Append(Invariant($"data-data-date=\"{(held is { } date ? date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "none")}\">"));

        banner.Append(Invariant($"<p class=\"banner degraded\">no list was computed for {asked:yyyy-MM-dd}. "));

        banner.Append(held is { } newest
            ? Invariant($"The newest data the store holds is {newest:yyyy-MM-dd}.</p>")
            : "The store holds no night at all.</p>");

        banner.Append("<p class=\"banner-reason\">tonight's list is absent rather than wrong, because a list computed from last night's bars is a list about last night.</p>");
        banner.Append("</section>");

        return banner.ToString();
    }

    static string Invariant(FormattableString text) =>
        text.ToString(CultureInfo.InvariantCulture);

    static string Escaped(string text) =>
        text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
}
