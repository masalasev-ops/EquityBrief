using System.Globalization;
using System.Text;
using EquityBrief.Core.Components;
using EquityBrief.Web.Marks;

namespace EquityBrief.Web.App;

// What a name's masthead states beside its ticker and last close: the company, its sector
// and industry as the membership row holds them, and the change on the day, which the
// projection reads off the two newest stored closes.
public sealed record NameMast(string? Company, string? Sector, string? Industry, double? DayChangePct);

// A current member as the masthead's search offers it: its ticker, its company's name, and the
// day its newest researched section was written, null where it holds none.
public sealed record Findable(string Ticker, string? Name, DateOnly? Researched);

// A name holding researched sections, as the researched list draws it.
public sealed record ResearchedCell(string Ticker, string? Name, string? Sector, DateOnly Written, int Sections);

// One request, as the queue screen draws it. The instant is carried as the store
// spells it, because a press to take one out names the request rather than the
// name: a name may have been asked for before and settled since.
public sealed record QueuedCell(
    string Ticker,
    string AskedAt,
    string AskedFrom,
    string Lane,
    string State,
    string? SettledAt,
    string? RunId,
    string? Reason,
    QueuedTime? Time = null);

// When one request's pass will start or started and will end or ended, as the read surface
// worked it out: what it rests on, the two instants in UTC as the store spells an instant,
// and the words the page states them in.
public sealed record QueuedTime(string Basis, string? Starts, string? Ends, string Words);

// What the queue page's times rest on: how many passes that ran to their end the store
// holds, and their median in whole minutes where it holds any.
public sealed record QueueEstimate(int Passes, string? Minutes);

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

    // The researched names, reached from the masthead on every screen.
    public const string ResearchedRoute = "#/researched";

    // The queue, section 15.15, the fifth entry in the masthead.
    public const string QueueRoute = "#/queue";

    // The three states a request is settled in, spelled here because the page draws a
    // region per state and the surface's own constants sit in a project the page does
    // not reference. `read-surface` asserts the two agree.
    public const string Outstanding = "outstanding";
    public const string Writing = "writing";

    // The two lanes, in the operator's own words, which are what the page draws and what
    // a request carries.
    public const string LocalLane = "local";
    public const string PaidLane = "paid";

    // Where the name page's control sends a press, and the header the page's own script
    // puts on it. A form another site's page submits to this address carries no such
    // header, and a request carrying one from another origin is one the browser asks
    // about first and this surface never answers, so a pass is started by this page and
    // not by any page that can reach the machine.
    // see: A pass is started only by a request carrying the name page's own header
    public const string PassRoute = "/passes/";

    // Where a request is taken back out of the queue. A press names the request rather than
    // the name, because a name may have been asked for before and settled since.
    public const string WithdrawRoute = "/passes/withdraw/";
    public const string PassHeader = "X-EquityBrief-Pass";
    public const string PassHeaderValue = "name-page";

    // How long the page watches a pass it started, as a count of asks and the wait between
    // them: ten minutes, which is above the longest pass the run log has recorded, and three
    // seconds apart, which is short enough that a section looks as though it arrived when it
    // did. A page left open past the bound stops asking rather than asking for ever.
    // see: A pass the page starts is watched until it ends and the page redraws as each section lands
    public const int PassTicks = 200;
    public const int PassTickMillis = 3000;

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
    // What the head of every page states about report generation, in the operator's two
    // words and never a model's name: which lane would write a report if one were asked
    // for now. The local choice is drawn and refused, because what would let it be chosen
    // is a comparison of what the two lanes write, and that has not been measured over
    // enough models to choose on.
    // see: Every part of a page states where it came from and as of when, and a written section when it was written rather than which model wrote it
    public const string LaneWaitsOn = "the local lane is drawn and not offered until the reports the two lanes write have been compared";

    public string LaneSwitch(string lane) =>
        $$$"""
        <div class="lane" id="lane" data-lane="{{{Escaped(lane)}}}"><span class="lane-lbl">Report generation</span><span class="lane-opts">
        <span class="lane-opt" data-choice="local" data-offered="false" aria-disabled="true" title="{{{Escaped(LaneWaitsOn)}}}">Local</span>
        <span class="lane-opt{{{(lane == PaidLane ? " on" : string.Empty)}}}" data-choice="paid" data-offered="true">Paid</span>
        </span></div>
        """;

    public string Shell(string title, string lane = PaidLane) =>
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
        <header class="mast" id="mast"><div class="wrap"><div class="m-id" id="identity"><a class="m-brand" href="#/">{{{Escaped(title)}}}</a></div><div class="m-right"><form class="m-search" id="search" role="search"><input id="find" type="search" list="findable" placeholder="Find a ticker or company" aria-label="Find a name by its ticker or its company's name" autocomplete="off" spellcheck="false"><datalist id="findable"></datalist></form><nav class="m-nav" aria-label="Screens"><a href="#/" data-view="tonight">Tonight</a><a href="{{{UniverseRoute}}}" data-view="universe">Universe</a><a href="{{{ResearchedRoute}}}" data-view="researched">Researched</a><a href="{{{RunRoute}}}" data-view="run">Run</a><a href="{{{QueueRoute}}}" data-view="queue">Queue</a></nav>{{{LaneSwitch(lane)}}}<button type="button" class="theme" id="theme">Dark palette</button></div></div></header>
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
            // A name alone is tonight's page for it, and a name and a date is that evening's.
            const asked = path.slice('{{{NameRoute}}}'.length);
            const cut = asked.indexOf('/');
            const ticker = encodeURIComponent(cut < 0 ? asked : asked.slice(0, cut));
            const night = cut < 0 ? '' : '/' + encodeURIComponent(asked.slice(cut + 1));
            const response = await fetch('/screens/name/' + ticker + night);
            screen.innerHTML = await response.text();
          } else if (path === '{{{ResearchedRoute}}}') {
            view = 'researched';
            const researched = await fetch('/screens/researched');
            screen.innerHTML = await researched.text();
          } else if (path === '{{{QueueRoute}}}') {
            view = 'queue';
            const queued = await fetch('/screens/queue');
            screen.innerHTML = await queued.text();
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
        // put beside the control. It writes a request and starts nothing: the worker
        // drains the queue and the page is read again when the operator returns to it.
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
            line.textContent = 'Put this report in the queue? The worker starts on it at once and writes it at the off-peak rate. ' + (cost ? cost.textContent : '');
            const go = document.createElement('button');
            go.type = 'button'; go.className = 'btn'; go.textContent = 'Queue it';
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
          const said = form.nextElementSibling;
          if (said && said.getAttribute('data-started') === 'true') {
            watch(said.getAttribute('data-watch-from'));
          }
        });
        // Taking a report out of the queue. Asked once, since a press cannot be undone by
        // another press: what it removes is a request, and asking again writes a new one.
        // The screen is drawn again afterwards so the request leaves the region it was in.
        document.addEventListener('submit', async (event) => {
          const form = event.target;
          if (!(form instanceof HTMLFormElement) || !form.classList.contains('withdraw-control')) { return; }
          event.preventDefault();
          if (form.dataset.confirmed !== 'yes') {
            for (const open of screen.querySelectorAll('.confirm')) { open.remove(); }
            const box = document.createElement('div');
            box.className = 'confirm key';
            const line = document.createElement('p');
            line.textContent = 'Take ' + form.dataset.takes + ' out of the queue? Nothing has been written for it yet.';
            const go = document.createElement('button');
            go.type = 'button'; go.className = 'btn'; go.textContent = 'Take it out';
            const stop = document.createElement('button');
            stop.type = 'button'; stop.className = 'btn-2'; stop.textContent = 'Keep it';
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
          const said = await response.text();
          const kept = scrollY;
          await show();
          scrollTo(0, kept);
          const notice = document.createElement('div');
          notice.innerHTML = said;
          const drawn = notice.firstElementChild;
          if (drawn) { screen.querySelector('.queue-part[data-region="outstanding"]').prepend(drawn); }
        });
        // A pass the page started, watched until it ends: what it is doing is asked for every
        // few seconds and drawn beside the control, and the page is drawn again each time one
        // more section has landed, so the sections arrive as they are written rather than on
        // the next visit. Nothing here writes, and the reader's place on the page is kept.
        async function watch(from) {
          const hash = location.hash;
          const ticker = tickerIn(hash);
          let written = null;
          for (let tick = 0; tick < {{{PassTicks}}}; tick++) {
            await new Promise((wait) => setTimeout(wait, {{{PassTickMillis}}}));
            if (location.hash !== hash) { return; }
            let line;
            try {
              const asked = await fetch('{{{PassRoute}}}' + encodeURIComponent(ticker) + '?since=' + encodeURIComponent(from));
              if (!asked.ok) { return; }
              const box = document.createElement('div');
              box.innerHTML = await asked.text();
              line = box.querySelector('.pass-progress');
            } catch (error) { return; }
            if (!line) { return; }
            place(line);
            const sections = line.getAttribute('data-sections');
            const ended = line.getAttribute('data-state') === 'ended';
            if ((written !== null && sections !== written) || ended) {
              const kept = scrollY;
              await show();
              scrollTo(0, kept);
              place(line);
              if (ended) { return; }
            }
            written = sections;
          }
        }
        // Where the line goes: over the one already shown, beside the reply to the press, or
        // at the head of the research region, which is what survives the page being drawn again.
        function place(line) {
          const shown = screen.querySelector('.pass-progress');
          if (shown) { shown.replaceWith(line); return; }
          const said = screen.querySelector('.pass-started');
          if (said) { said.after(line); return; }
          const research = screen.querySelector('section.research');
          if (research) { research.prepend(line); }
        }
        function tickerIn(hash) {
          const asked = hash.slice('{{{NameRoute}}}'.length);
          const cut = asked.indexOf('/');
          return cut < 0 ? asked : asked.slice(0, cut);
        }
        // The masthead's search, over every current member by its ticker or its company's name.
        // Its list is read after the first screen, so the first screen never waits on it, and a
        // name picked from the list goes straight to its page.
        const find = document.getElementById('find');
        const findable = document.getElementById('findable');
        function found(text) {
          const typed = text.trim();
          if (typed === '') { return null; }
          const options = [...findable.querySelectorAll('option')];
          if (options.length === 0) { return typed.toUpperCase(); }
          const byTicker = options.find((option) => option.value === typed.toUpperCase());
          if (byTicker) { return byTicker.value; }
          const lower = typed.toLowerCase();
          const byName = options.find((option) => option.textContent.toLowerCase().includes(lower));
          return byName ? byName.value : null;
        }
        function go(text) {
          for (const old of screen.querySelectorAll('.notice[data-not-found]')) { old.remove(); }
          const ticker = found(text);
          if (ticker) {
            find.value = '';
            find.blur();
            fresh = true;
            location.hash = '{{{NameRoute}}}' + encodeURIComponent(ticker);
            return;
          }
          const line = document.createElement('p');
          line.className = 'notice';
          line.setAttribute('role', 'status');
          line.setAttribute('data-not-found', text.trim());
          line.textContent = 'No name in the index matches ' + text.trim() + '.';
          screen.prepend(line);
        }
        document.getElementById('search').addEventListener('submit', (event) => { event.preventDefault(); go(find.value); });
        find.addEventListener('input', (event) => {
          if (event.inputType === 'insertReplacementText' && [...findable.querySelectorAll('option')].some((option) => option.value === find.value)) { go(find.value); }
        });
        paintTheme();
        show().then(() => fetch('/screens/find')).then((response) => response.ok ? response.text() : '').then((options) => { findable.innerHTML = options; }).catch(() => { });
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
        DateOnly? filedOn = null,
        ListingHistoryCard? history = null,
        DateOnly? night = null,
        PeersView? peers = null,
        IReadOnlyList<ReactionCell>? reactions = null)
    {
        var region = new StringBuilder();
        var sections = written ?? [];
        var documents = sources ?? [];
        var session = bars.Count > 0 ? bars[^1].SessionDate : (DateOnly?)null;
        var company = mast?.Company is { Length: > 0 } named ? named : ticker;

        // The cards, held apart from the region so the contents can be written from what was
        // drawn and still stand above it. A card records itself as it is added, which is what
        // makes the contents a reading of the page rather than a second list of its sections.
        var body = new StringBuilder();
        var onThePage = new List<ContentsEntry>();

        void Card(string id, string title, string markup)
        {
            onThePage.Add(new ContentsEntry(onThePage.Count, title, id));
            body.Append(markup);
        }

        // A written section's own id, which its entry in the contents links to. Derived from
        // the section's name rather than from its position, so a name holding fewer sections
        // does not move another section's link.
        static string SectionId(string section) =>
            "s-" + new string([.. section.ToLowerInvariant().Select(letter => char.IsLetterOrDigit(letter) ? letter : '-')]);

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

                // The key under each figure is dated by the night whose figures it explains,
                // so it is drawn only beside that night's, and elsewhere the card says which
                // night it was written for.
                // see: The key under each figure is dated by the night whose figures it explains, written for every name each night, and drawn only beside that night's figures
                if (UnderTheFigures.Contains(name, StringComparer.Ordinal))
                {
                    Card(
                        SectionId(name),
                        name,
                        Cards.Dated(
                            name,
                            KeyDated,
                            section.AsOf,
                            section.AsOf == session
                                ? marks.WrittenSection(ticker, section, documents, dated: "written for the close of")
                                : marks.KeyForAnotherNight(ticker, name, section.AsOf, session),
                            section: name,
                            id: SectionId(name)));

                    continue;
                }

                Card(
                    SectionId(name),
                    name,
                    Cards.Dated(
                        name,
                        "Written",
                        section.AsOf,
                        marks.WrittenSection(ticker, section, documents),
                        section: name,
                        id: SectionId(name)));
            }
        }

        region.Append(Invariant($"<section class=\"name\" data-ticker=\"{Escaped(ticker)}\">"));

        // Where the name's stored series is suspect, first, since every figure after it is
        // computed over those prices, the masthead's close among them. Inside the region, so
        // the exported report carries it.
        region.Append(marks.PricesSuspect(ticker, suspect));
        region.Append(marks.NoYearServed(ticker, noYear));

        // A page about an earlier night says so above every figure it draws, and links to
        // tonight's, since each of those figures is what the store held that evening and a
        // reader arriving on a link has nothing else to tell them which evening they are in.
        // see: A name's page for an earlier night is what the store held that night
        region.Append(night is { } evening
            ? Invariant($"<p class=\"notice earlier-night\" data-night=\"{evening:yyyy-MM-dd}\" role=\"status\">This is {Escaped(ticker)} as the store held it after the close of {evening:yyyy-MM-dd}. <a href=\"{NameRoute}{Escaped(ticker)}\">Tonight's page</a></p>")
            : string.Empty);

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

        // What the page is for and what it refuses to do, before any figure, and the words it
        // uses beneath that. First, so a reader meets the refusals before the first number.
        Card("how-to-read", "How to read this page", Cards.Computed(
            "How to read this page",
            Intro(ticker, company),
            id: "how-to-read",
            region: "how-to-read"));

        // Why it is here, which section 15.9 puts above the chart and which is
        // present only when the name is on tonight's list, beneath the line
        // section 18 draws where the listing was written before the correction.
        var why = WrittenBeforeTheCorrectionLine(writtenBeforeTheCorrection) + marks.WhyItIsHere(ticker, firedReasons);

        if (firedReasons.Count > 0)
        {
            Card("why", "Why it is here", Cards.Computed(
                "Why it is here",
                why,
                title: night is { } listed ? Invariant($"On the list on {listed:yyyy-MM-dd} for these reasons") : "On tonight's list for these reasons",
                stamp: Cards.Night(session),
                id: "why",
                region: "why"));
        }
        else
        {
            body.Append(why);
        }

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
        Card("facts", "Tonight's figures", Cards.Computed(
            "Fact strip",
            trend + factStrip + Cards.Key(
                "Two sources.",
                "The close, the averages, momentum and the typical daily move are computed from the stored daily bars. The market value, the two price multiples and the report date come from the latest filing and the calendar.",
                "Relative strength and trend momentum are here for context. Nothing on this page is decided by them."),
            stamp: Cards.Night(session),
            id: "facts",
            region: "facts"));

        // The short version, section 4's first section, with its date beside it.
        Draw(AtTheTop);

        // How it got here, the twelve-month picture above the table of the biggest moves,
        // each move numbered on the picture as it is in the table.
        Card("how-it-got-here", "How it got here", Cards.Computed(
            "How it got here",
            marks.MovesTable(ticker, moves, twelveMonths, causes) + Cards.Key(
                "How to read it.",
                "A year of daily candles. The numbered circles mark the year's biggest moves, numbered as the table beneath lists them, largest first, and each row says what drove the move where research has been written. Beside each move is its group's median move over the same sessions: the name's industry where at least five other members share it, and its sector otherwise, with how many members the median was taken over, so a move the whole group made reads apart from one the name made alone.",
                "This is the path that produced tonight's bands."),
            title: "The last twelve months",
            stamp: Cards.Night(session),
            id: "how-it-got-here",
            region: "how-it-got-here"));

        // The peers, beneath the table of the biggest moves: every member of the group those
        // moves are read against, by price alone and in ticker order, ranking none.
        // see: Peers are shown by price alone, in section 2 beside the move table
        if (peers is not null)
        {
            Card("peers", "Its group, by price", Cards.Computed(
                "Its group, by price",
                marks.PeersTable(ticker, peers) + Cards.Key(
                    "How to read it.",
                    Invariant($"Every member of the group the moves above are read against, in ticker order with {Escaped(ticker)} marked. Each row gives the last stored close, how far it sits below the highest price among the bars the store holds for it, its return over the last {EquityBrief.Core.Moves.PeerReadings.ReturnWindow} sessions, its trend and where the close sits between its nearest bands, in typical days, all computed from the stored daily bars. A name holding too few bars for the return says how many it holds rather than giving one over fewer sessions."),
                    "The table lists and ranks none: a peer sitting further below its high is not a better or a worse name, and nothing on the page is decided by this table."),
                title: "Its group, by price alone",
                stamp: Cards.Night(session),
                id: "peers",
                region: "peers"));
        }

        // The chart region: the level chart and, on its price scale, the volume profile
        // beside it, both drawn at one scale so a price is at one height in both.
        //
        // At its own size, which is what the column is as wide as. A picture drawn smaller
        // than the space it is read in loses a year of candles to save nothing.
        const double Scale = 1;

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
            "A hollow candle closed above where it opened and a filled one closed below. Shaded bands are prices the stock has repeatedly turned at: green below the price is support, orange above it is resistance, and the column on the right names the last close and each band's edges, drawing the edges of the nearest band on either side in that band's own colour. The row above the chart names the averages and those two colours. Beside the chart, on the same prices, is how many shares traded at each price, with a rule where a bar would reach if every price had traded the same and another at twice that.",
            "The plan buys at the bands below the price and sells at the ones above. Where many shares changed hands, many holders paid about that price, which is why the price tends to stall there."));
        chart.Append("<div class=\"sub\">Momentum</div>");
        chart.Append("<div class=\"fig\">").Append(marks.MomentumPanel(ticker, readings)).Append("</div>");
        chart.Append(Cards.Key(
            "Context, not a signal.",
            "Each reading is drawn on its own scale with its neutral rule. Relative strength usually sits inside its shaded band, and the momentum bars read against their zero rule: above it is strengthening and below it weakening.",
            "Nothing in the plan or the list reads these. When they and the bands disagree, the plan follows the bands."));
        chart.Append("<div class=\"sub\">Levels</div>");
        chart.Append("<div class=\"tbl-wrap\">").Append(marks.LevelSummary(ticker, summary, absent)).Append("</div>");

        Card("chart", "The daily chart and its levels", Cards.Computed("The chart", chart.ToString(), title: "The daily chart and its levels", stamp: Cards.Night(session), id: "chart", region: "chart"));

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

        Card("plan", "Where it is bought, sold, and wrong", Cards.Computed("The plan", planned.ToString(), title: "Where it is bought, sold, and wrong", stamp: Cards.Night(session), id: "plan", region: "plan"));

        // The earnings reaction record, beside the earnings setups the plan closes on: what each
        // print over the calendar's year behind did on the session it moved.
        // see: Each print's reaction is read from the nightly calendar and the stored bars, and reaches no reason, gate or plan
        if (reactions is not null)
        {
            Card("reactions", "Earnings reactions", Cards.Computed(
                "Earnings reactions",
                marks.ReactionsTable(ticker, reactions) + Cards.Key(
                    "How to read it.",
                    "One row for every print over the last year, from the provider's earnings calendar: the day it was reported and whether before the open or after the close, the session the report moved, the earnings per share the provider says was expected and what was reported, the provider's surprise, and how far the stock closed that session from the close before it. A print reported after the close moves the next session, and one whose timing was not filed is read on its own day, as the earnings setups read it. A print with no filed estimate says so and has no surprise.",
                    "A record of what past reports did, not a forecast of the next: nothing on the page, no reason, plan or setup, reads it."),
                title: "What each report did to the price",
                stamp: Cards.Night(session),
                id: "reactions",
                region: "reactions"));
        }

        // The listing history, after the plan: the evenings the name was on the list and what
        // followed each, section 15.9's row.
        if (history is not null)
        {
            Card("listing-history", "The evenings it was on the list", Cards.Computed(
                "Listing history",
                marks.ListingHistory(ticker, history) + Cards.Key(
                    "How to read it.",
                    "The strip marks each stored session, inked where the name was on the list. Each row is one of those evenings, with where the price was five and twenty-one sessions later. A win means it was higher. The base rate is the share of every name on every night that was higher over the same span.",
                    "One evening is one observation. A record is measured per reason across every name it fired on, so none is formed here for this name."),
                title: "The evenings it was on the list",
                stamp: Cards.Night(session),
                id: "listing-history",
                region: "listing-history"));
        }

        // What the company sells, section 4's fifth section, after the plan and before the numbers.
        Draw(BeforeTheNumbers);

        // The numbers, which section 4 puts sixth. It arrives already written, for the
        // reason the event book does: what it holds is stored figures and the sentences
        // that state an absence, rather than a mark.
        Card("numbers", "The numbers", Cards.Dated(
            "The numbers",
            "Filed",
            filedOn,
            numbers + Cards.Key(
                "Where these come from.",
                "Each figure is from the company's own filing, dated as the filing is. The estimate is the analysts' average before the report.",
                "These are the company's reported results. Nothing in the plan is computed from them."),
            filed: true,
            note: "from the filing",
            id: "numbers"));

        // The industry cycle and the two cases, section 4's seventh and eighth.
        Draw(AfterTheNumbers);

        // What would make this wrong, section 4's ninth, after the two cases it tests.
        Draw(AfterThePlan);

        // Dates and sources, section 4's last two: the calendar, the dated items a pass
        // read out of the documents, and every document the written sections cite.
        Card("sources", "What the research read", Cards.Computed(
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

        Card(
            researchState?.State == "missing" ? "unwritten" : "research",
            "Where the research stands",
            researchState?.State == "missing"
                ? Invariant($"<section class=\"absent\" id=\"unwritten\"><div class=\"lbl\">Research not yet written</div><h2>The researched sections for {Escaped(ticker)} have not been written</h2>{research}</section>")
                : Cards.Computed("Research", research, title: "Where the research stands", id: "research", region: "research"));

        // The contents, written from the cards that were drawn and standing above them, which
        // is why the cards were held apart until now.
        region.Append(marks.Contents(ticker, onThePage));
        region.Append(body);

        // The walk, which section 15.9 puts last: previous and next on tonight's
        // list, so an evening's reading is one pass through with no return to
        // the list.
        region.Append(marks.Walk(ticker, previousOnTheList, nextOnTheList, night));

        region.Append("</section>");

        return region.ToString();
    }

    // What the name page is for, what it does not do, and its words, which open the page.
    // see: No reading of the fundamentals fires a reason, gates a tranche or draws a panel
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
    // at the top, the key beneath the chart's figures, what the company sells after the
    // plan and before the numbers, the cycle and the two cases after them, the risks after
    // those, and the dated items with the dates. The cause of each large move is drawn in the moves table, in
    // the row of the move each sentence names, and `read-surface` asserts every section
    // figure 12.2 names is placed exactly once across these and that table.
    public static readonly string[] AtTheTop = ["The short version"];
    public static readonly string[] BeforeTheNumbers = ["What the company sells", "The segment commentary"];
    public static readonly string[] AfterTheNumbers = ["The industry cycle", MarkRenderer.TheTwoCases];
    public static readonly string[] UnderTheFigures = [MarkRenderer.KeySection];

    // What the key's card states its date as, which is the night whose figures it explains
    // rather than the day it was written.
    public const string KeyDated = "For the close of";
    public static readonly string[] AfterThePlan = [MarkRenderer.TheRisks];
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

    // The masthead search's list: every current member by its ticker, labelled with its
    // company's name and the day its research was written where it holds any.
    public string FindOptions(IReadOnlyList<Findable> names)
    {
        var options = new StringBuilder();

        foreach (var name in names)
        {
            var label = (name.Name is { Length: > 0 } company ? company : name.Ticker)
                + (name.Researched is { } written ? ", researched " + Cards.Day(written) : string.Empty);

            options.Append(Invariant($"<option value=\"{Escaped(name.Ticker)}\">{Escaped(label)}</option>"));
        }

        return options.ToString();
    }

    // The researched names, section 15.8's researched region on a route of its own: every name
    // holding a researched section, newest first, each with the day its newest section was
    // written and how many sections it holds.
    // see: A researched name is one holding an accepted section besides the key under each figure
    public string ResearchedRegion(IReadOnlyList<ResearchedCell> rows)
    {
        var body = new StringBuilder();

        if (rows.Count == 0)
        {
            body.Append("<p class=\"degraded\" data-researched=\"none\">No name holds researched sections yet. A name's own page offers to write them, with what a pass has cost stated before it starts.</p>");
        }
        else
        {
            body.Append("<div class=\"tbl-wrap\"><table class=\"researched-table\"><thead><tr><th>Name</th><th>Sector</th><th>Written</th><th>Sections</th></tr></thead><tbody>");

            foreach (var row in rows)
            {
                body.Append(Invariant($"<tr data-ticker=\"{Escaped(row.Ticker)}\" data-written=\"{Cards.Day(row.Written)}\" data-sections=\"{row.Sections}\">"));
                body.Append(Invariant($"<td class=\"c-nm\"><a class=\"tk\" href=\"{NameRoute}{Uri.EscapeDataString(row.Ticker)}\">{Escaped(row.Ticker)}</a>"));
                body.Append(row.Name is { Length: > 0 } company ? Invariant($"<span class=\"co\">{Escaped(company)}</span></td>") : "</td>");
                body.Append(Invariant($"<td>{Escaped(row.Sector ?? "not on file")}</td><td class=\"num\">{Cards.Day(row.Written)}</td><td class=\"num\">{row.Sections}</td></tr>"));
            }

            body.Append("</tbody></table></div>");
        }

        body.Append(Cards.Key(
            "What is listed.",
            "Every name holding a section a research pass wrote and the claim checker accepted, newest first, with the day its newest section was written. The key under each figure is not counted, because the overnight queue writes it for every name in the index each night.",
            "A name here opens with its research in place. Any other name offers to write it on its own page, and the search box above finds any name in the index."));

        return Invariant($"<section class=\"researched\" data-names=\"{rows.Count}\">")
            + Cards.Masthead(
                "Researched",
                "<span class=\"m-screen\">Researched names</span>",
                rows.Count == 0 ? "No name holds researched sections yet" : Invariant($"{rows.Count} name(s) hold researched sections"))
            + Cards.Computed(
                "Researched",
                body.ToString(),
                title: "Names with research",
                lede: "Research is written when it is asked for on a name's page, and that page says when a filing, an earnings date or the name's news has made it stale.",
                region: "researched")
            + "</section>";
    }

    // The queue, section 15.15: which reports have been asked for, which one is being
    // written now, and what came of the rest.
    //
    // Three regions over one ordered read, split by state rather than by three reads, so
    // a request that moved between them cannot be drawn twice or missed by both. Nothing
    // here starts a pass: the press that wrote a request started the worker's drain, and a
    // drain that could not be started leaves a request outstanding rather than losing it.
    // see: A press writes a request and starts the worker's drain as a process of its own, and every pass waits for the off-peak hours
    public string QueueRegion(IReadOnlyList<QueuedCell> rows, QueueEstimate? estimate = null)
    {
        var outstanding = rows.Where(row => row.State == Outstanding).ToArray();
        var writing = rows.Where(row => row.State == Writing).ToArray();

        // Newest first, because a settled request is read to find out what came of the
        // one just asked for, and oldest first is the order the worker takes them in.
        var settled = rows
            .Where(row => row.State != Outstanding && row.State != Writing)
            .Reverse()
            .ToArray();

        var body = new StringBuilder();

        // Which lane would write what is queued here, stated where a reader is deciding
        // whether to ask for one, and what the choice they cannot make waits on.
        body.Append(Invariant($"<p class=\"lane-waits\" data-waits=\"local\">Report generation is set to the paid lane, and {LaneWaitsOn}.</p>"));

        // What every time on the page rests on, stated once above them.
        if (estimate is not null)
        {
            body.Append(estimate.Minutes is { } minutes
                ? Invariant($"<p class=\"queue-estimate\" data-passes=\"{estimate.Passes}\" data-median-minutes=\"{minutes}\">Times are New York's with the offset named and UTC beside them. A pass is expected to take {minutes} minutes, the median of the {estimate.Passes} {(estimate.Passes == 1 ? "pass" : "passes")} the store holds that ran to their end, and a pass asked at peak waits for the off-peak rate.</p>")
                : Invariant($"<p class=\"queue-estimate\" data-passes=\"0\">Times are New York's with the offset named and UTC beside them. The store holds no pass that ran to its end, so it cannot estimate how long one takes, and no request behind another is given a time.</p>"));
        }

        body.Append(Invariant($"<div class=\"queue-part\" data-region=\"outstanding\" data-rows=\"{outstanding.Length}\">"));
        body.Append("<h3>Outstanding</h3>");

        if (outstanding.Length == 0)
        {
            body.Append("<p class=\"degraded\" data-outstanding=\"none\">No report is waiting. A row on tonight's list and a name's own page both offer to ask for one.</p>");
        }
        else
        {
            body.Append("<p class=\"lede\">Oldest first, which is the order the worker takes them in.</p>");
            body.Append("<div class=\"tbl-wrap\"><table class=\"queue-table\"><thead><tr><th>Name</th><th>Asked</th><th>From</th><th>Lane</th><th>When</th><th>Take it out</th></tr></thead><tbody>");

            foreach (var row in outstanding)
            {
                body.Append(Row(row));
                body.Append(When(row));
                // The control the operator asked for: a report that has not been generated
                // is one nobody has started, so it is drawn on an outstanding request and
                // on no other. The press names the request by its instant.
                body.Append(Invariant($"<td><form class=\"withdraw-control\" method=\"post\" action=\"{WithdrawRoute}{Uri.EscapeDataString(row.Ticker)}\" data-takes=\"{Escaped(row.Ticker)}\" data-asked-at=\"{Escaped(row.AskedAt)}\">"));
                body.Append(Invariant($"<input type=\"hidden\" name=\"askedAt\" value=\"{Escaped(row.AskedAt)}\">"));
                body.Append("<button type=\"submit\" title=\"take this report out of the queue\">take it out</button></form></td></tr>");
            }

            body.Append("</tbody></table></div>");
        }

        body.Append("</div>");

        body.Append(Invariant($"<div class=\"queue-part\" data-region=\"writing\" data-rows=\"{writing.Length}\">"));
        body.Append("<h3>Being written</h3>");

        if (writing.Length == 0)
        {
            body.Append("<p class=\"degraded\" data-writing=\"none\">Nothing is being written. A press starts the worker on what is outstanding, and a request asked at peak waits for the off-peak rate.</p>");
        }
        else
        {
            body.Append("<div class=\"tbl-wrap\"><table class=\"queue-table\"><thead><tr><th>Name</th><th>Asked</th><th>From</th><th>Lane</th><th>When</th><th>Under</th></tr></thead><tbody>");

            foreach (var row in writing)
            {
                body.Append(Row(row));
                body.Append(When(row));
                body.Append(Invariant($"<td>{Escaped(row.RunId ?? "the pass it is running under is not on the run log yet")}</td></tr>"));
            }

            body.Append("</tbody></table></div>");
        }

        body.Append("</div>");

        body.Append(Invariant($"<div class=\"queue-part\" data-region=\"settled\" data-rows=\"{settled.Length}\">"));
        body.Append("<h3>Settled</h3>");

        if (settled.Length == 0)
        {
            body.Append("<p class=\"degraded\" data-settled=\"none\">No request has been settled yet.</p>");
        }
        else
        {
            body.Append("<p class=\"lede\">Newest first. Nothing is removed: what was asked for and what came of it are both kept.</p>");
            body.Append("<div class=\"tbl-wrap\"><table class=\"queue-table\"><thead><tr><th>Name</th><th>Asked</th><th>From</th><th>Lane</th><th>When</th><th>Came to</th><th>Why</th></tr></thead><tbody>");

            foreach (var row in settled)
            {
                body.Append(Row(row));
                body.Append(When(row));
                body.Append(Invariant($"<td class=\"q-state\">{Escaped(row.State)}</td>"));
                body.Append(Invariant($"<td>{Escaped(row.Reason ?? string.Empty)}</td></tr>"));
            }

            body.Append("</tbody></table></div>");
        }

        body.Append("</div>");

        body.Append(Cards.Key(
            "What is listed.",
            "Every report that has been asked for, from a row on tonight's list, from a name's own page or by the night for the first name its list draws, with what came of it. One name holds one outstanding request at a time, which the store enforces: a second press for a name already waiting adds nothing and says so.",
            "A request nobody has started can be taken out. One the worker has claimed cannot, because what a withdrawal removes is a report that has not been generated, and the refusal says which state refused it."));

        return Invariant($"<section class=\"queue\" data-requests=\"{rows.Count}\" data-outstanding=\"{outstanding.Length}\" data-writing=\"{writing.Length}\" data-settled=\"{settled.Length}\">")
            + Cards.Masthead(
                "Queue",
                "<span class=\"m-screen\">Report queue</span>",
                rows.Count == 0
                    ? "No report has been asked for yet"
                    : Invariant($"{outstanding.Length} outstanding, {writing.Length} being written, {settled.Length} settled"))
            + Cards.Computed(
                "Queue",
                body.ToString(),
                title: "Reports asked for",
                lede: "A press asks for a report and starts the worker, which writes it at the off-peak rate. Nothing on this screen starts a pass.",
                region: "queue")
            + "</section>";
    }

    // When a request's pass will start or started and will end or ended, with what that rests
    // on and both instants on the cell, so a reader of the markup reads the instants the words
    // state. A request drawn with no time says so rather than leaving the cell empty.
    // see: The queue page states when each request will be written
    static string When(QueuedCell row) =>
        row.Time is { } time
            ? Invariant($"<td class=\"q-when\" data-basis=\"{Escaped(time.Basis)}\" data-starts=\"{Escaped(time.Starts ?? string.Empty)}\" data-ends=\"{Escaped(time.Ends ?? string.Empty)}\">{Escaped(time.Words)}</td>")
            : "<td class=\"q-when\" data-basis=\"none\">no time is stated</td>";

    // The four cells every region shares, so a request reads the same way whichever
    // region it is in.
    static string Row(QueuedCell row) =>
        Invariant($"<tr data-ticker=\"{Escaped(row.Ticker)}\" data-asked-at=\"{Escaped(row.AskedAt)}\" data-state=\"{Escaped(row.State)}\">")
        + Invariant($"<td class=\"c-nm\"><a class=\"tk\" href=\"{NameRoute}{Uri.EscapeDataString(row.Ticker)}\">{Escaped(row.Ticker)}</a></td>")
        + Invariant($"<td>{Escaped(row.AskedAt)}</td><td>{Escaped(row.AskedFrom)}</td><td>{Escaped(row.Lane)}</td>");

    // What the selected name's region offers to open, in the words of what it opens: the
    // report, with the day it was written, where the name holds one, and the name's page
    // where it holds none, beneath a line saying so and the control asking for one. A link
    // calling itself a report for a name holding none is the page promising something it
    // does not have.
    // see: A researched name is one holding an accepted section besides the key under each figure
    static string SelectedLinks(string ticker, ListingCell? picked)
    {
        var page = NameRoute + Uri.EscapeDataString(ticker);

        // What the queue holds for the name, where it holds a request nobody has settled: said
        // in place of the line saying none is written and the control asking for one, since
        // what the reader would ask for is already asked.
        // see: The queue page states when each request will be written
        if (picked?.Queue is { } queued)
        {
            var opens = picked.ResearchedOn is { } written
                ? Invariant($"Open the full report for {Escaped(ticker)}, written {written:yyyy-MM-dd}")
                : Invariant($"Open {Escaped(ticker)}'s page");

            return Invariant($"<div class=\"sel-queued\" data-report-state=\"{Escaped(queued.State)}\" data-at=\"{Escaped(queued.At ?? string.Empty)}\">A report for {Escaped(ticker)} is {Escaped(queued.Words)}.</div>")
                + Invariant($"<div class=\"sel-links\"><a class=\"btn-2\" href=\"{page}\">{opens}</a></div>");
        }

        if (picked?.ResearchedOn is { } on)
        {
            return Invariant($"<div class=\"sel-links\" data-researched=\"true\" data-researched-on=\"{on:yyyy-MM-dd}\"><a class=\"btn-2\" href=\"{page}\">Open the full report for {Escaped(ticker)}, written {on:yyyy-MM-dd}</a></div>");
        }

        var unwritten = picked is null
            ? string.Empty
            : Invariant($"<div class=\"sel-unwritten\" data-researched=\"false\">No report is written for {Escaped(ticker)} yet.{MarkRenderer.AskForAReport(ticker)}</div>");

        return unwritten + Invariant($"<div class=\"sel-links\"><a class=\"btn-2\" href=\"{page}\">Open {Escaped(ticker)}'s page</a></div>");
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
            lede: "Each one has reached a price its own chart made significant. Most reasons first, then the strongest band.",
            stamp: Cards.Night(night),
            region: "list"));

        // The selected name's plan and level summary, which is what section 15.7
        // means by the common case needing no navigation: selecting a row draws its
        // plan beneath the list, on the same page, and brings it into view.
        // see: Selecting a row draws its plan beneath the list and is no navigation
        if (selectedName.Length > 0 && selectedTicker is { } chosen)
        {
            var picked = rows.Concat(watched).FirstOrDefault(row => row.Ticker == chosen);
            var name = picked?.Distance?.Name;

            region.Append(Cards.Computed(
                "Selected name",
                selectedName
                    + SelectedLinks(chosen, picked)
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

    // The run page, section 15.10's seven regions, in the order that section
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
        IReadOnlyList<DateOnly>? writtenBeforeTheCorrection = null,
        OrderComparison? orders = null,
        CandidateRegion? candidates = null,
        TrendVersionRegion? versions = null)
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

        if (candidates is { } judged)
        {
            region.Append(Cards.Computed(
                "Candidates' records",
                marks.CandidateRecords(judged),
                title: "What each registered candidate's setups have come to",
                lede: "The running figure is monitoring; only a look changes a verdict, and no name is named.",
                stamp: Cards.Night(night),
                region: "candidates"));
        }

        if (versions is { } trend)
        {
            region.Append(Cards.Computed(
                "The trend rule's versions",
                marks.TrendVersions(trend),
                title: "What each open version would have labelled, and what it would have taken away",
                lede: "A version changes the rule and no list: nothing here is drawn beside a name.",
                stamp: Cards.Night(night),
                region: "trend-versions"));
        }

        if (orders is { } comparison)
        {
            region.Append(Cards.Computed(
                "Tonight's order",
                marks.TonightsOrder(comparison),
                title: "The order the list is drawn in, against the one it replaced",
                lede: "Nothing is compared until every order has enough whole windows behind it.",
                stamp: Cards.Night(night),
                region: "orders"));
        }

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

    // A name asked for on something that is not a date: tonight's page with a line saying
    // what was asked for, as an unknown route is tonight's list with one.
    // see: A name's page for an earlier night is what the store held that night
    public static string NotANight(string asked) =>
        Invariant($"<p class=\"notice\" role=\"status\" data-not-a-night=\"{Escaped(asked)}\">{Escaped(asked)} is not an evening this reads, so this is tonight.</p>");

    // Every screen over a store behind this checkout, which names both schema numbers
    // rather than failing on the first column the store lacks.
    public string StoreBehind(int store, int checkout) =>
        Invariant($"<p class=\"degraded\" data-schema=\"{store}\" data-needs=\"{checkout}\">the store is at schema {store} and this checkout reads schema {checkout}, so nothing is drawn from it until tools/migrate applies the rest</p>");

    static string Invariant(FormattableString text) =>
        text.ToString(CultureInfo.InvariantCulture);

    static string Escaped(string text) =>
        text.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
}
