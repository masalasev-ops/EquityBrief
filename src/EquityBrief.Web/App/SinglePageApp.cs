using System.Globalization;
using System.Text;
using EquityBrief.Core.Components;
using EquityBrief.Web.Marks;

namespace EquityBrief.Web.App;

// What a name's masthead states beside its ticker and last close: the company, its sector
// and industry as the membership row holds them, and the change on the day, which the
// projection reads off the two newest stored closes.
public sealed record NameMast(string? Company, string? Sector, string? Industry, double? DayChangePct);

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
    //
    // The masthead is the shell's and its words are the screen's: each screen's markup
    // opens with the line naming what it is and as of when, and the script moves those
    // words into the masthead rather than writing any of its own. The palette is a stamp
    // on the root element, remembered between visits, with the machine's preference as
    // the default (section 15.13).
    public string Shell(string title) =>
        $$$"""
        <!doctype html>
        <html lang="en">
        <head>
        <meta charset="utf-8">
        <meta name="viewport" content="width=device-width, initial-scale=1, viewport-fit=cover">
        <title>{{{Escaped(title)}}}</title>
        <style>
        {{{Stylesheet.Css}}}
        </style>
        <script>
        try { const kept = localStorage.getItem('eb-theme'); if (kept === 'dark' || kept === 'light') { document.documentElement.setAttribute('data-eb-theme', kept); } } catch (error) { }
        </script>
        </head>
        <body>
        <header class="mast" id="mast"><div class="wrap"><div class="m-id" id="identity"><a class="m-brand" href="#/">{{{Escaped(title)}}}</a></div><div class="m-right"><nav class="m-nav" aria-label="Screens"><a href="#/" data-view="tonight">Tonight</a><a href="{{{UniverseRoute}}}" data-view="universe">Universe</a><a href="{{{RunRoute}}}" data-view="run">Run</a></nav><button type="button" class="theme" id="theme">Dark palette</button></div></div></header>
        <main class="wrap" id="screen"></main>
        <script>
        const screen = document.getElementById('screen');
        const identity = document.getElementById('identity');
        const brand = identity.innerHTML;
        const scrolls = { };
        let fresh = false;
        let drawing = false;
        try { history.scrollRestoration = 'manual'; } catch (error) { }
        addEventListener('scroll', () => { if (!drawing) { scrolls[location.hash] = scrollY; } }, { passive: true });
        async function show() {
          drawing = true;
          const hash = location.hash;
          const cut = hash.indexOf('?');
          const path = cut < 0 ? hash : hash.slice(0, cut);
          const query = cut < 0 ? '' : hash.slice(cut + 1);
          let view = 'tonight';
          if (path === '' || path === '#/' || path.startsWith('{{{NightRoute}}}')) {
            const date = path.startsWith('{{{NightRoute}}}') ? path.slice('{{{NightRoute}}}'.length) : '';
            const night = date === '' ? '' : '/' + encodeURIComponent(date);
            const tonight = await fetch('/screens/tonight' + night + (query ? '?' + query : ''));
            screen.innerHTML = await tonight.text();
          } else if (path.startsWith('{{{RunRoute}}}')) {
            view = 'run';
            const night = encodeURIComponent(path.slice('{{{RunRoute}}}'.length));
            const run = await fetch('/screens/run/' + night);
            screen.innerHTML = await run.text();
          } else if (path.startsWith('{{{UniverseRoute}}}')) {
            view = 'universe';
            const universe = await fetch('/screens/universe' + (query ? '?' + query : ''));
            screen.innerHTML = await universe.text();
          } else if (path.startsWith('{{{NameRoute}}}')) {
            view = 'name';
            const ticker = encodeURIComponent(path.slice('{{{NameRoute}}}'.length));
            const response = await fetch('/screens/name/' + ticker);
            screen.innerHTML = await response.text();
          } else {
            // An unknown route resolves to tonight with a line saying what was asked for,
            // rather than to a blank page.
            const tonight = await fetch('/screens/tonight');
            screen.innerHTML = await tonight.text();
            const line = document.createElement('p');
            line.className = 'notice';
            line.setAttribute('role', 'status');
            line.setAttribute('data-unknown-route', hash);
            line.textContent = 'Nothing is drawn at ' + hash + ', so this is Tonight.';
            screen.prepend(line);
          }
          settle(view, hash, new URLSearchParams(query));
        }
        // The masthead's words, the screen's own markers and the scroll, once a screen is in.
        function settle(view, hash, query) {
          const head = screen.querySelector('.screen-mast');
          identity.innerHTML = brand + (head ? head.innerHTML : '');
          for (const link of document.querySelectorAll('.m-nav a')) {
            if (link.dataset.view === view) { link.setAttribute('aria-current', 'page'); } else { link.removeAttribute('aria-current'); }
          }
          const named = head ? head.getAttribute('data-title') : null;
          document.title = named ? named + ' · EquityBrief' : 'EquityBrief';
          const tonight = screen.querySelector('section.tonight');
          const chosen = tonight ? tonight.getAttribute('data-selected') : null;
          for (const row of screen.querySelectorAll('.list-table tr[data-ticker]')) {
            row.classList.toggle('sel', row.getAttribute('data-ticker') === chosen);
          }
          const asked = query.get('reason');
          const reasonRow = asked ? screen.querySelector('.records-table tr[data-reason="' + CSS.escape(asked) + '"]') : null;
          if (reasonRow) { reasonRow.classList.add('hl'); }
          if (!fresh && scrolls[hash] != null) { scrollTo(0, scrolls[hash]); }
          else if (query.get('name') && document.getElementById('selected')) { document.getElementById('selected').scrollIntoView({ block: 'start' }); }
          else if (reasonRow) { reasonRow.scrollIntoView({ block: 'center' }); }
          else { scrollTo(0, 0); }
          fresh = false;
          drawing = false;
          paintTheme();
        }
        addEventListener('hashchange', show);
        // A link followed or a row picked is a new place, and back or forward returns to where
        // the reader was. Anywhere on a row of tonight's list but its links picks that row, which
        // draws its plan beneath the list.
        document.addEventListener('click', (event) => {
          if (event.target.closest('#theme')) {
            const next = currentTheme() === 'dark' ? 'light' : 'dark';
            document.documentElement.setAttribute('data-eb-theme', next);
            try { localStorage.setItem('eb-theme', next); } catch (error) { }
            paintTheme();
            return;
          }
          const row = event.target.closest('.list-table tr[data-ticker]');
          if (row && !event.target.closest('a, button, form')) {
            const pick = row.querySelector('a.select');
            if (pick) { fresh = true; location.hash = pick.getAttribute('href'); }
            return;
          }
          if (event.target.closest('a[href^="#"]')) { fresh = true; }
        });
        function currentTheme() {
          const own = document.documentElement.getAttribute('data-eb-theme');
          if (own) { return own; }
          return matchMedia('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
        }
        function paintTheme() {
          document.getElementById('theme').textContent = currentTheme() === 'dark' ? 'Light palette' : 'Dark palette';
        }
        // A research control is a form, sent here with the page's own header so the
        // surface knows the press came from this page, and what the surface said back is
        // put beside the control. The pass runs as a process of its own and lands on the
        // run log, so the page is read again when the operator returns to it.
        document.addEventListener('submit', async (event) => {
          const form = event.target;
          if (!(form instanceof HTMLFormElement) || !form.classList.contains('research-control')) { return; }
          event.preventDefault();
          // Asked once before anything starts, with what research has cost and where spend
          // stands against the caps stated in the question.
          if (form.dataset.confirmed !== 'yes') {
            for (const open of screen.querySelectorAll('.confirm')) { open.remove(); }
            const cost = screen.querySelector('.research-cost');
            const box = document.createElement('div');
            box.className = 'confirm key';
            const line = document.createElement('p');
            line.textContent = 'Start this research pass? ' + (cost ? cost.textContent : '');
            const go = document.createElement('button');
            go.type = 'button'; go.className = 'btn'; go.textContent = 'Start it';
            const stop = document.createElement('button');
            stop.type = 'button'; stop.className = 'btn-2'; stop.textContent = 'Cancel';
            go.addEventListener('click', () => { form.dataset.confirmed = 'yes'; box.remove(); form.requestSubmit(); });
            stop.addEventListener('click', () => { box.remove(); });
            const actions = document.createElement('div');
            actions.className = 'sel-links';
            actions.append(go, stop);
            box.append(line, actions);
            form.after(box);
            return;
          }
          for (const button of form.querySelectorAll('button')) { button.disabled = true; }
          const response = await fetch(form.getAttribute('action'), {
            method: 'POST',
            headers: { '{{{PassHeader}}}': '{{{PassHeaderValue}}}' },
            body: new URLSearchParams(new FormData(form)),
          });
          form.insertAdjacentHTML('afterend', await response.text());
        });
        paintTheme();
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
        SuspectPrices? suspect = null,
        IReadOnlyList<DateOnly>? writtenBeforeTheCorrection = null,
        NoYear? noYear = null,
        NameMast? mast = null,
        DateOnly? filedOn = null)
    {
        var region = new StringBuilder();
        var sections = written ?? [];
        var documents = sources ?? [];
        var session = bars.Count > 0 ? bars[^1].SessionDate : (DateOnly?)null;
        var company = mast?.Company is { Length: > 0 } named ? named : ticker;

        // The written sections drawn in one place, each where section 4 puts it, in a
        // card whose left column states the day it was written.
        // see: A research record is written and dated per section, not as a whole
        void Draw(IReadOnlyList<string> placed)
        {
            foreach (var name in placed)
            {
                if (sections.FirstOrDefault(section => string.Equals(section.Section, name, StringComparison.Ordinal)) is not { } section)
                {
                    continue;
                }

                region.Append(Cards.Dated(
                    name,
                    "Written",
                    section.AsOf,
                    marks.WrittenSection(ticker, section, documents),
                    section: name));
            }
        }

        region.Append(Invariant($"<section class=\"name\" data-ticker=\"{Escaped(ticker)}\">"));

        // Where the name's stored series is suspect, first, since every figure after it is
        // computed over those prices, the masthead's close among them. Inside the region, so
        // the exported report carries it.
        region.Append(marks.PricesSuspect(ticker, suspect));
        region.Append(marks.NoYearServed(ticker, noYear));

        // The line the masthead carries: the ticker, the company, the last stored close
        // with its change on the day, and the session it is from. No screen fetches a
        // price, so the price is the last one the store holds and says so.
        // see: The masthead carries the last stored close and the session it is from
        var identity = new StringBuilder();

        identity.Append(Invariant($"<span class=\"m-tk\">{Escaped(ticker)}</span>"));
        identity.Append(mast?.Company is { Length: > 0 } name ? Invariant($"<span class=\"m-co\">{Escaped(name)}</span>") : string.Empty);
        identity.Append(bars.Count > 0 ? Invariant($"<span class=\"m-px\">{bars[^1].Close.ToString(CultureInfo.InvariantCulture)}</span>") : string.Empty);
        identity.Append(mast?.DayChangePct is { } change
            ? Invariant($"<span class=\"m-chg\">{change.ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture)}% on the day</span>")
            : string.Empty);

        var asOf = session is { } last
            ? Invariant($"As of the close of {last:yyyy-MM-dd}, the last stored price{(suspect is null ? string.Empty : ". Prices may be out of date; see below")}")
            : "No session is stored for this name";

        asOf += mast?.Sector is { Length: > 0 } sector
            ? Invariant($" · <a href=\"#/universe?sector={Uri.EscapeDataString(sector)}\">{Escaped(sector)}</a>{(mast.Industry is { Length: > 0 } industry ? Invariant($", {Escaped(industry)}") : string.Empty)}")
            : string.Empty;

        region.Append(Cards.Masthead(ticker, identity.ToString(), asOf));

        // What the page is for and what it refuses to do, before any figure, and the
        // words it uses one disclosure down.
        region.Append(Intro(ticker, company));

        // Why it is here, which section 15.9 puts above the chart and which is
        // present only when the name is on tonight's list, beneath the line
        // section 18 draws where the listing was written before the correction.
        var why = WrittenBeforeTheCorrectionLine(writtenBeforeTheCorrection) + marks.WhyItIsHere(ticker, firedReasons);

        region.Append(firedReasons.Count > 0
            ? Cards.Computed("Why it is here", why, title: "On tonight's list for these reasons", stamp: Cards.Night(session), region: "why")
            : why);

        // The trend state, in a word. Read off the ladder row rather than worked
        // out here, and a name with no row says so rather than showing nothing:
        // an absence stated and an absence drawn as emptiness are different
        // things, and only the first is readable.
        var trend = trendState is null
            ? "<p class=\"trend-state\" data-trend-state=\"none\">no ladder row for this name yet</p>"
            : Invariant($"<p class=\"trend-state\" data-trend-state=\"{Escaped(trendState)}\" data-as-of=\"{trendAsOf:yyyy-MM-dd}\">trend {Escaped(trendState.Replace('_', ' '))}</p>");

        // The fact strip, which section 15.9 puts above the chart and which states
        // seven things rather than one. It arrives already written, for the reason
        // the event book does: two of its seven parts are fundamentals and no mark
        // renders them.
        region.Append(Cards.Computed(
            "Fact strip",
            trend + factStrip + Cards.Key(
                "Two sources.",
                "The close, the averages, momentum and the typical daily move are computed from the stored daily bars. The market value, the two price multiples and the report date come from the latest filing and the calendar.",
                "Relative strength and trend momentum are here for context. Nothing on this page is decided by them."),
            stamp: Cards.Night(session),
            region: "facts"));

        // The short version, section 4's first section, with its date beside it.
        Draw(AtTheTop);

        // How it got here, the twelve-month picture above the table of the biggest moves,
        // each move numbered on the picture as it is in the table.
        region.Append(Cards.Computed(
            "How it got here",
            marks.MovesTable(ticker, moves, twelveMonths, causes) + Cards.Key(
                "How to read it.",
                "A year of daily candles. The numbered circles mark the year's biggest moves, numbered as the table beneath lists them, largest first, and each row says what drove the move where research has been written.",
                "This is the path that produced tonight's bands."),
            title: "The last twelve months",
            stamp: Cards.Night(session),
            region: "how-it-got-here"));

        // What the company sells, section 4's third section, before the numbers.
        Draw(BeforeTheNumbers);

        // The numbers, which section 4 puts fourth. It arrives already written, for the
        // reason the event book does: what it holds is stored figures and the sentences
        // that state an absence, rather than a mark.
        region.Append(Cards.Dated(
            "The numbers",
            "Filed",
            filedOn,
            numbers + Cards.Key(
                "Where these come from.",
                "Each figure is from the company's own filing, dated as the filing is. The estimate is the analysts' average before the report.",
                "These are the company's reported results. Nothing in the plan is computed from them."),
            filed: true,
            note: "from the filing"));

        // The industry cycle and the two cases, section 4's fifth and sixth.
        Draw(AfterTheNumbers);

        // The chart region: the level chart and, on its price scale, the volume profile
        // beside it, both drawn at one scale so a price is at one height in both.
        const double Scale = 0.7;

        var chart = new StringBuilder();

        chart.Append("<div class=\"fig row-fig\">");
        chart.Append(marks.LevelChart(ticker, bars, averages, bands, new ChartFrame(Scale: Scale)));

        if (bars.Count > 0 && profile.Count > 0)
        {
            chart.Append(marks.VolumeProfile(ticker, profile, marks.AxisFor(bars, averages), bands, Scale));
        }

        chart.Append("</div>");
        chart.Append(Cards.Key(
            "How to read it.",
            "A hollow candle closed above where it opened and a filled one closed below. Shaded bands are prices the stock has repeatedly turned at: green below the price is support, orange above it is resistance, and the column on the right names the last close and each band's edges. Beside the chart, on the same prices, is how many shares traded at each price, with a rule where a bar would reach if every price had traded the same and another at twice that.",
            "The plan buys at the bands below the price and sells at the ones above. Where many shares changed hands, many holders paid about that price, which is why the price tends to stall there."));
        chart.Append("<div class=\"sub\">Momentum</div>");
        chart.Append("<div class=\"fig\">").Append(marks.MomentumPanel(ticker, readings)).Append("</div>");
        chart.Append(Cards.Key(
            "Context, not a signal.",
            "Each reading is drawn on its own scale with its neutral rule. Relative strength usually sits inside its shaded band, and the momentum bars read against their zero rule: above it is strengthening and below it weakening.",
            "Nothing in the plan or the list reads these. When they and the bands disagree, the plan follows the bands."));
        chart.Append("<div class=\"sub\">Levels</div>");
        chart.Append("<div class=\"tbl-wrap\">").Append(marks.LevelSummary(ticker, summary, absent)).Append("</div>");

        region.Append(Cards.Computed("The chart", chart.ToString(), title: "The daily chart and its levels", stamp: Cards.Night(session), region: "chart"));

        // The key under each figure, beneath the figures it explains.
        Draw(UnderTheFigures);

        // The plan region: the plan column and the two tables it is read beside, the event
        // setups and the sizing arithmetic.
        var planned = new StringBuilder();

        planned.Append("<div class=\"plan-grid\"><div class=\"fig\">").Append(marks.PlanColumn(ticker, close, plan)).Append("</div><div>");
        planned.Append("<div class=\"sub\" style=\"margin-top:0\">Tranches and exits</div><div class=\"tbl-wrap\">").Append(marks.PlanTables(ticker, plan)).Append("</div></div></div>");
        planned.Append("<div class=\"sub\">Around the next report</div>").Append(eventBook);
        planned.Append("<div class=\"sub\">Sizing arithmetic</div>").Append(arithmetic);
        planned.Append(Cards.Key(
            "How to read the plan.",
            "The price now sits in the middle of the column. Orange zones above it are where part of the position is sold, and green blocks below are where it is bought. Each thin rule is a stop, and the heavy rule is the invalidation, the lowest stop.",
            "Everything above the price marker is a sale, everything below it is a purchase, and the lowest line is where the whole idea is wrong. The risk you take is yours to choose; the page only does the division."));

        region.Append(Cards.Computed("The plan", planned.ToString(), title: "Where it is bought, sold, and wrong", stamp: Cards.Night(session), region: "plan"));

        // What would make this wrong, section 4's ninth, after the plan it is about.
        Draw(AfterThePlan);

        // Dates and sources, section 4's last two: the calendar, the dated items a pass
        // read out of the documents, and every document the written sections cite.
        region.Append(Cards.Computed(
            "Dates and sources",
            marks.DatesAndSources(
                ticker,
                dates ?? [],
                sections.FirstOrDefault(section => string.Equals(section.Section, InTheDates, StringComparison.Ordinal)),
                documents),
            title: "What the research read",
            stamp: Cards.Night(session),
            id: "sources",
            region: "sources"));

        // Where the research stands, what the newest pass came to, the sections left out
        // or not written with the reason for each, and the controls with what research
        // has cost stated beside them before any is pressed.
        var research = marks.LeftOut(ticker, leftOut ?? [], researchState, notWritten, paused, pass, controls, cost);

        region.Append(researchState?.State == "missing"
            ? Invariant($"<section class=\"absent\" id=\"unwritten\"><div class=\"lbl\">Research not yet written</div><h2>The researched sections for {Escaped(ticker)} have not been written</h2>{research}</section>")
            : Cards.Computed("Research", research, title: "Where the research stands", region: "research"));

        // The provenance footer, arriving written for the reason the event book does:
        // what it holds is dates read off the stores rather than a mark over values.
        region.Append(Cards.Computed("Provenance", provenance ?? string.Empty, title: "Where each part of this page came from", region: "provenance"));

        // The walk, which section 15.9 puts last: previous and next on tonight's
        // list, so an evening's reading is one pass through with no return to
        // the list.
        region.Append(marks.Walk(ticker, previousOnTheList, nextOnTheList));

        region.Append("</section>");

        return region.ToString();
    }

    // What the name page is for, what it does not do, and its words, which open the page.
    // see: Improving what surfaces a name is in scope; ranking names against each other is not
    // see: The plan places a position and never sizes one
    static string Intro(string ticker, string company) =>
        $"<section class=\"intro\" aria-label=\"About this page\"><p class=\"intro-p\">This page finds the prices {Escaped(company)} has repeatedly stopped falling or rising at, and sets out what to do if it reaches one of them again. " +
        "The chart, the levels and the plan are recomputed every evening; the written sections further down carry their own dates. " +
        $"It carries no forecast, no view on whether {Escaped(company)} is a good business, and no opinion on how much of your money to put in.</p>" +
        $"<ul class=\"refuse\"><li>It does not predict where the price will go.</li><li>It does not rank {Escaped(ticker)} against any other name.</li>" +
        "<li>It does not say how much to buy: the sizing near the end only divides the amount you choose to risk.</li></ul>" +
        "<details class=\"gloss\"><summary>Words used on this page</summary>" +
        Cards.Words(
            ("Support", "A price below the current one where this stock has repeatedly stopped falling and turned back up."),
            ("Resistance", "A price above the current one where this stock has repeatedly stopped rising and turned back down."),
            ("Band", "A support or resistance drawn as a narrow range of prices, because a stock never turns at exactly the same cent twice."),
            ("Touch", "A day the price reached a band and turned away from it."),
            ("Typical daily move", "How far this stock's price usually moves in one session. Distances on these pages are counted in these."),
            ("Tranche", "One of several smaller purchases that together make up the whole position, each made at a different band."),
            ("Stop", "The price at which a purchase is sold to limit the loss. It is where you admit that part of the plan was wrong."),
            ("Invalidation", "The lowest stop. Below it the reason for owning the stock is gone, and everything is sold."),
            ("Reason", "The specific thing that happened tonight to put a name on the list, such as reaching the price its plan buys at."),
            ("Break-even", "The share of setups that must reach their target, given how far away their targets and stops sit, for the whole set to neither make nor lose money.")) +
        "</details></section>";

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
        IReadOnlyList<DateOnly>? writtenBeforeTheCorrection = null,
        DateOnly? night = null)
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

        region.Append(Cards.Masthead(
            "The universe",
            "<span class=\"m-screen\">The universe</span>",
            night is { } on ? Invariant($"Every name in the index on the night of {on:yyyy-MM-dd}") : "Every name in the index"));

        region.Append(WrittenBeforeTheCorrectionLine(writtenBeforeTheCorrection));
        region.Append(Cards.Computed("Sectors", marks.SectorStrip(sectors), stamp: Cards.Night(night), region: "sectors"));

        var table = new StringBuilder();

        table.Append(marks.UniverseFilters(rows, trendFilter, sectorFilter));
        table.Append("<div class=\"tbl-wrap\">").Append(marks.UniverseTable(drawn)).Append("</div>");
        table.Append(marks.UniversePaging(shown.Length, at, pageSize > 0 ? pageSize : Math.Max(1, drawn.Count), trendFilter, sectorFilter));
        table.Append("<p class=\"oneline\">The two right-hand columns count evenings a name appeared on the list. They say nothing about index membership, which every name here has.</p>");
        table.Append(Cards.Key(
            "How to read the rows.",
            "The distance picture fixes each close at its centre line. The green block to its left is the nearest support, the orange block to its right the nearest resistance, one tick per typical day. Scan down that column for blocks touching the centre. In the strip, each column is one session, marked on an evening the name was on the list and a rule on an evening it was not.",
            "Names at the top are closest to one of their own levels, measured in days of their own ordinary movement. A strip with marks spread across it belongs to a name that keeps returning to its levels; a single mark is a one-off."));

        region.Append(Cards.Computed(
            "The index",
            table.ToString(),
            title: "Every name, nearest a level first",
            lede: "The top of the table is what nearly fired. Filters live in the address, so a filtered view is a link you can keep.",
            stamp: Cards.Night(night),
            region: "index"));

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

        region.Append(Cards.Masthead("Tonight", "<span class=\"m-screen\">Tonight</span>", Invariant($"Night of {night:yyyy-MM-dd}, computed after the close")));

        region.Append(Cards.Computed(
            Invariant($"Night of {night:yyyy-MM-dd} · computed after the close"),
            marks.NightHeader(night, index, fired, duration, harness, spend, prose)
                + Invariant($"<p class=\"oneline\"><a href=\"{RunRoute}{night:yyyy-MM-dd}\">What ran tonight, and what it cost</a></p>"),
            stamp: Cards.Night(night),
            region: "night"));

        region.Append(WrittenBeforeTheCorrectionLine(writtenBeforeTheCorrection));

        region.Append(Cards.Computed(
            "Watch list",
            marks.WatchList(watched),
            title: "Shown every evening",
            lede: "These names appear whether or not a reason fired for them.",
            region: "watch"));

        region.Append(Cards.Computed(
            "The list",
            marks.TonightList(rows, TonightDrawn, records) + Cards.Key(
                "How to read the list.",
                "Each reason has its own column, always in the same place, so a night that is all one thing shows as one dark stripe running down one column. The one-word heads are short for at entry zone, crossed a level, breakout on volume, trend state changed, unusual volume and earnings soon; point at a head for its full name, and at a reason for the values that made it true and its record. The distance picture fixes the close at its centre line: the green block to its left is the nearest support and the orange block to its right the nearest resistance, one tick per typical day, so a block touching the centre is a name at an edge. The last line of each column is that reason's record across every name it has fired for, and a dashed one is not yet measured. Select a row to draw its plan just below the list; report opens the name's full page.",
                "Every name here has reached a price its own chart made significant, and the reason says what kind of arrival it was. The distance picture counts in days of the stock's own ordinary movement, so a block one tick from the centre is a distance the price often covers in a single session."),
            title: "Names that fired tonight",
            lede: "Each one has reached a price its own chart made significant. Most reasons first, then the strongest band; twenty drawn.",
            stamp: Cards.Night(night),
            region: "list"));

        // The selected name's plan and level summary, which is what section 15.7
        // means by the common case needing no navigation: selecting a row draws its
        // plan beneath the list, on the same page, and brings it into view.
        // see: Selecting a row draws its plan beneath the list and is no navigation
        if (selectedName.Length > 0 && selectedTicker is { } chosen)
        {
            var name = rows.Concat(watched).FirstOrDefault(row => row.Ticker == chosen)?.Distance?.Name;

            region.Append(Cards.Computed(
                "Selected name",
                selectedName
                    + Invariant($"<div class=\"sel-links\"><a class=\"btn-2\" href=\"{NameRoute}{Uri.EscapeDataString(chosen)}\">Open the full report for {Escaped(chosen)}</a></div>")
                    + Cards.Key(
                        "How to read the plan.",
                        "The price now sits in the middle. Orange zones above it are where part of the position is sold, and green blocks below are where it is bought. Each thin rule is a stop, and the heavy rule is where the plan is wrong.",
                        "Everything above the price marker is a sale, everything below it is a purchase, and the lowest line is where the whole idea is wrong."),
                title: Invariant($"<span class=\"tk\" style=\"font-size:19px\">{Escaped(chosen)}</span> {Escaped(name ?? string.Empty)}"),
                lede: "Select any row in the list above to draw its plan here.",
                stamp: Cards.Night(night),
                id: "selected",
                region: "selected"));
        }
        else
        {
            region.Append(selectedName);
        }

        // The reason totals, 15.7's last region, drawn beneath the list because
        // it is a statement about the evening rather than about a name in it.
        if (totals is not null)
        {
            region.Append(Cards.Computed(
                "Reason totals",
                marks.ReasonTotals(totals, fired) + Cards.Key(
                    "One long bar.",
                    "Most of tonight's names arrived the same way, so the list holds fewer separate stories than its rows suggest. Several short bars mean different things happened to a few names each.",
                    "These are tonight's counts. How a reason's setups have ended over time is on the run page, read against the base rate."),
                title: "Which reasons put tonight's names on the list",
                lede: Invariant($"How many of tonight's {fired} fired names carry each reason."),
                stamp: Cards.Night(night),
                region: "totals"));
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
        ShadowRegion shadow,
        PricedCalls? priced = null,
        IReadOnlyList<DateOnly>? writtenBeforeTheCorrection = null)
    {
        var region = new StringBuilder();

        region.Append(Invariant($"<section class=\"run\" data-night=\"{night:yyyy-MM-dd}\" data-stages=\"{stages.Count}\">"));

        region.Append(Cards.Masthead("Run evidence", "<span class=\"m-screen\">Run evidence</span>", Invariant($"Night of {night:yyyy-MM-dd}")));

        region.Append(Cards.Computed(
            Invariant($"Run of {night:yyyy-MM-dd}"),
            "<div class=\"tbl-wrap\">" + marks.OperationalHeader(night, stages, priced) + "</div>",
            title: "What ran, and what it cost",
            lede: "Each stage with the instant it started in UTC, how long it took, what it wrote and what it said about itself.",
            stamp: Cards.Night(night),
            region: "operational"));

        region.Append(WrittenBeforeTheCorrectionLine(writtenBeforeTheCorrection));

        region.Append(Cards.Computed(
            "Reason records",
            marks.ReasonRecords(records, tracks, baseRates, nights) + Cards.Key(
                "How to read a record.",
                "Each bar is every setup the reason has produced. The dark part reached its target before its stop, the lighter part hit its stop first, and the dashed part has not resolved yet, which is not a smaller amount of losing. A record below either minimum shows its count against the minimum and no share at all.",
                "The dashed part has not finished yet and is not a loss. A share appears only once enough setups have finished to say something, and the base rate above the table is what any name on any night did, so a reason is worth something only if it beats it."),
            title: "How each reason's setups have ended",
            lede: "The base rate sits above the rows, so no reason's figure stands alone.",
            stamp: Cards.Night(night),
            region: "records"));

        region.Append(Cards.Computed(
            "Shadow candidates",
            marks.ShadowCandidates(shadow),
            title: "Registered variants, measured but not listed",
            lede: "The correction divisor is shown because testing many variants makes one look good by chance.",
            region: "shadow"));

        region.Append(Cards.Computed(
            "Stale and failed",
            marks.StaleAndFailed(stale, failed, refused, fellBack),
            title: "What the night could not do",
            stamp: Cards.Night(night),
            region: "stale"));

        region.Append(Cards.Computed(
            "Overnight queue",
            marks.OvernightQueue(queue),
            title: "The local model's overnight pass",
            stamp: Cards.Night(night),
            region: "queue"));

        region.Append(Cards.Computed(
            "Harness",
            marks.HarnessVerdicts(harness),
            title: "The last phase report",
            lede: "Four counts, kept apart and never added together.",
            region: "harness"));

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

    // Every screen over a store behind this checkout, which names both schema numbers
    // rather than failing on the first column the store lacks.
    public string StoreBehind(int store, int checkout) =>
        Invariant($"<p class=\"degraded\" data-schema=\"{store}\" data-needs=\"{checkout}\">the store is at schema {store} and this checkout reads schema {checkout}, so nothing is drawn from it until tools/migrate applies the rest</p>");

    static string Invariant(FormattableString text) =>
        text.ToString(CultureInfo.InvariantCulture);

    static string Escaped(string text) =>
        text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
}
