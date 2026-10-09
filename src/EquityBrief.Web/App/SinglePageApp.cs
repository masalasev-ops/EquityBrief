using System.Globalization;
using System.Text;
using EquityBrief.Core.Components;
using EquityBrief.Core.Filter;
using EquityBrief.Core.Shortlist;
using EquityBrief.Web.Marks;

namespace EquityBrief.Web.App;

// What a name's masthead states beside its ticker and last close: the company, its sector
// and industry as the membership row holds them, and the change on the day, which the
// projection reads off the two newest stored closes.
public sealed record NameMast(string? Company, string? Sector, string? Industry, double? DayChangePct);

// A current member as the masthead's search offers it: its ticker, its company's name, and the
// day its newest researched section was written, null where it holds none.
public sealed record Findable(string Ticker, string? Name, DateOnly? Researched);

// One trade the operator took, as Past picks' "Your trades" draws it.
public sealed record YourTradeView(string Ticker, string TakenAt, string Rule, DateOnly Night, decimal Fill, DateOnly FillDate, bool Provisional, bool Open, DateOnly? EndedOn, string? EndReason, decimal? EndPrice, double? Result, string Unit);

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
public sealed record QueueEstimate(int Passes, string? Minutes, string? LongestMinutes = null);

// A drain that stopped on an error and no pass has started since: the instant it started as the store spells one,
// the words the page states that instant in, and the error it stopped on.
public sealed record QueueStop(string StartedAt, string Words, string Error);

// The shell the browser loads once, and the routes it answers.
//
// Section 15.4 puts the shell and the marks on the server and the routing in
// the page. So this writes the shell and nothing else: the marks arrive already
// drawn, and the only thing the browser does is ask for the one belonging to
// the route it is on. A page that assembled a mark from values would be the
// second renderer the marks decision exists to prevent.
// see: Marks are defined once and every screen draws from that list
// see: A screen reads and renders, and computes only the plan in the operator's money and a pick's open trades in its sector
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

    // The setup ledger's page, reached from the Universe page and drawn under it.
    public const string LedgerRoute = "#/ledger";

    public const string LoopRoute = "#/loop";

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

    // Every trade the live list recommended, section 15.17, between the universe and the researched names
    // in the masthead. Its status filter lives in the hash, so a filtered view is a link.
    public const string PicksRoute = "#/picks";

    // The queue, section 15.15, the fifth entry in the masthead.
    public const string QueueRoute = "#/queue";

    // The watch list page, second in the masthead, and the two presses that change it, each refused
    // without the page's own header as every press is.
    public const string WatchRoute = "#/watch";
    public const string WatchPostRoute = "/watch/";
    public const string UnwatchPostRoute = "/watch/remove/";

    // The account's page, last in the masthead, and the presses a pick's card makes, each refused without the page's
    // own header as every press is.
    // see: The account settings live in a file of their own under the data root and in nothing the store or the logs hold
    public const string AccountRoute = MarkRenderer.AccountRoute;
    public const string AccountPostRoute = "/account";
    public const string TakenPostRoute = "/taken/";
    public const string NotTakenPostRoute = "/taken/remove/";
    public const string ExitPostRoute = "/taken/exit/";

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
    // The press running the rest of a night left unfinished, from tonight's notice or the Run page.
    // see: A night left unfinished is run to its end from the step it stopped at by a press or a command, and one night runs at a time under a lock file
    public const string NightResumeRoute = "/night/resume";

    // The press, drawn beneath a night left unfinished and nowhere else.
    public static string NightPress(NightView night) =>
        night.State == NightStates.Unfinished
            ? $"<form class=\"night-control\" method=\"post\" action=\"{NightResumeRoute}\"><button type=\"submit\" class=\"btn\">Run the rest of the night</button></form>"
            : string.Empty;

    // Tonight's notice: the night's state as the Run page's headline names it, its tries, and the press
    // where it was left unfinished; nothing on a day the exchange did not trade.
    // see: A night's state is read off its own run log rows and its tries, and the pages that state it read that one state
    public string NightNotice(MarkRenderer marks, NightView night) =>
        night.State == NightStates.NoSession
            ? string.Empty
            : "<section class=\"night-notice-box\">" + marks.NightNotice(night) + NightPress(night) + "</section>";

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
        <header class="mast" id="mast"><div class="wrap"><div class="m-id" id="identity"><a class="m-brand" href="#/">{{{Escaped(title)}}}</a></div><div class="m-right"><form class="m-search" id="search" role="search"><input id="find" type="search" list="findable" placeholder="Find a ticker or company" aria-label="Find a name by its ticker or its company's name" autocomplete="off" spellcheck="false"><datalist id="findable"></datalist></form><nav class="m-nav" aria-label="Screens"><a href="#/" data-view="tonight">Tonight</a><a href="{{{WatchRoute}}}" data-view="watch">Watch list</a><a href="{{{UniverseRoute}}}" data-view="universe">Universe</a><a href="{{{PicksRoute}}}" data-view="picks">Past picks</a><a href="{{{ResearchedRoute}}}" data-view="researched">Researched</a><a href="{{{RunRoute}}}" data-view="run">Run</a><a href="{{{QueueRoute}}}" data-view="queue">Queue</a><a href="{{{AccountRoute}}}" data-view="account">Account</a></nav>{{{LaneSwitch(lane)}}}<button type="button" class="theme" id="theme">Dark palette</button></div></div></header>
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
            const run = await fetch('/screens/run/' + night + (query ? '?' + query : ''));
            screen.innerHTML = await run.text();
          } else if (path.startsWith('{{{UniverseRoute}}}')) {
            view = 'universe';
            const universe = await fetch('/screens/universe' + (query ? '?' + query : ''));
            screen.innerHTML = await universe.text();
          } else if (path === '{{{LedgerRoute}}}') {
            view = 'universe';
            const ledger = await fetch('/screens/ledger' + (query ? '?' + query : ''));
            screen.innerHTML = await ledger.text();
          } else if (path === '{{{LoopRoute}}}') {
            view = 'universe';
            const loop = await fetch('/screens/loop' + (query ? '?' + query : ''));
            screen.innerHTML = await loop.text();
          } else if (path.startsWith('{{{NameRoute}}}')) {
            view = 'name';
            // A name alone is tonight's page for it, and a name and a date is that evening's.
            const asked = path.slice('{{{NameRoute}}}'.length);
            const cut = asked.indexOf('/');
            const ticker = encodeURIComponent(cut < 0 ? asked : asked.slice(0, cut));
            const night = cut < 0 ? '' : '/' + encodeURIComponent(asked.slice(cut + 1));
            const response = await fetch('/screens/name/' + ticker + night);
            screen.innerHTML = await response.text();
          } else if (path === '{{{PicksRoute}}}') {
            view = 'picks';
            const picks = await fetch('/screens/picks' + (query ? '?' + query : ''));
            screen.innerHTML = await picks.text();
          } else if (path === '{{{ResearchedRoute}}}') {
            view = 'researched';
            const researched = await fetch('/screens/researched' + (query ? '?' + query : ''));
            screen.innerHTML = await researched.text();
          } else if (path === '{{{WatchRoute}}}') {
            view = 'watch';
            const watch = await fetch('/screens/watch');
            screen.innerHTML = await watch.text();
          } else if (path === '{{{QueueRoute}}}') {
            view = 'queue';
            const queued = await fetch('/screens/queue');
            screen.innerHTML = await queued.text();
          } else if (path === '{{{AccountRoute}}}') {
            view = 'account';
            const account = await fetch('/screens/account');
            screen.innerHTML = await account.text();
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
        // A name's year in the peers table or the universe table, what a column holds in its heading,
        // what the night measured a reason on tonight's list over, what a member's numbers say
        // beside its state and why a story was labelled as it was, each shown by the stylesheet while its cell is under the pointer or holds
        // the focus, placed beside the cell: beneath it where the window has room and above it where
        // it does not.
        function placePop(event) {
          const cell = event.target.closest ? event.target.closest('.peers-table td.peer, .universe-table td.c-nm, th.tipped, .list-table .reason, .business, .news-why') : null;
          const pop = cell ? cell.querySelector('.peer-pop, .head-tip, .why, .says') : null;
          if (!pop) { return; }
          const box = cell.getBoundingClientRect();
          const tall = pop.offsetHeight || 150;
          pop.style.left = Math.max(8, Math.min(box.left, innerWidth - (pop.offsetWidth || 340) - 8)) + 'px';
          pop.style.top = (box.bottom + tall + 8 < innerHeight ? box.bottom + 4 : Math.max(8, box.top - tall - 4)) + 'px';
        }
        document.addEventListener('mouseover', placePop);
        document.addEventListener('focusin', placePop);
        // The version a reader compares tonight's picks with lives in the link, so the view is one to share, and so
        // does the index a page reads, chosen under Universe.
        // A key merged into the link's own query, its path and every other key kept, so a card's chosen rule and the
        // index chosen under Universe stand in one link; the live rule is the key's absence, so the default returns to it.
        function merged(key, value) {
          const hash = location.hash || '#/';
          const cut = hash.indexOf('?');
          const path = cut < 0 ? hash : hash.slice(0, cut);
          const params = new URLSearchParams(cut < 0 ? '' : hash.slice(cut + 1));
          if (value === null) { params.delete(key); } else { params.set(key, value); }
          const query = params.toString();
          return path + (query ? '?' + query : '');
        }
        // A link of the page's own merged with the keys the link holds now that it does not set, the index among them.
        function mergedInto(target) {
          const cut = target.indexOf('?');
          const path = cut < 0 ? target : target.slice(0, cut);
          const params = new URLSearchParams(cut < 0 ? '' : target.slice(cut + 1));
          const own = location.hash.indexOf('?');
          const held = new URLSearchParams(own < 0 ? '' : location.hash.slice(own + 1));
          for (const [key, value] of held) { if (!params.has(key)) { params.set(key, value); } }
          const query = params.toString();
          return path + (query ? '?' + query : '');
        }
        document.addEventListener('change', (event) => {
          const chosen = event.target.closest ? event.target.closest('select[data-compare]') : null;
          if (chosen) { location.hash = chosen.getAttribute('data-compare') + '?version=' + encodeURIComponent(chosen.value); }
          const universe = event.target.closest ? event.target.closest('select[data-universe-route]') : null;
          if (universe) { location.hash = universe.getAttribute('data-universe-route') + '?{{{Universes.Query}}}=' + encodeURIComponent(universe.value); }
          // A card's rule, kept in the link under the family's own key with every other key kept.
          const rule = event.target.closest ? event.target.closest('select[data-rule-choice]') : null;
          if (rule) { location.hash = merged(rule.getAttribute('data-rule-choice'), rule.value === '{{{RuleScreenWords.LiveSlug}}}' ? null : rule.value); }
        });
        // A name put on the watch list or taken off it: the press names the ticker, from the box on the
        // watch list page or from the form's own, sends the page's header, and the screen is drawn again
        // with what the read surface said above the list.
        document.addEventListener('submit', async (event) => {
          const form = event.target;
          if (!(form instanceof HTMLFormElement) || !form.classList.contains('watch-control')) { return; }
          event.preventDefault();
          const box = form.querySelector('input[name="ticker"]');
          const ticker = (box ? box.value : (form.dataset.ticker || '')).trim().split(' ')[0].toUpperCase();
          if (ticker === '') { return; }
          for (const button of form.querySelectorAll('button')) { button.disabled = true; }
          const response = await fetch(form.getAttribute('action') + encodeURIComponent(ticker), {
            method: 'POST',
            headers: { '{{{PassHeader}}}': '{{{PassHeaderValue}}}' },
          });
          const said = await response.text();
          const kept = scrollY;
          await show();
          scrollTo(0, kept);
          const place = screen.querySelector('.watch-said');
          if (place) { place.innerHTML = said; }
        });
        // A pick's card's presses and the account page's: the form's fields sent with the page's own header, the screen
        // drawn again with the card left open, and what the read surface said put in the card's or the page's line.
        document.addEventListener('submit', async (event) => {
          const form = event.target;
          if (!(form instanceof HTMLFormElement) || !(form.classList.contains('card-press') || form.classList.contains('account-control'))) { return; }
          event.preventDefault();
          const holder = form.closest('tr.card-row, section.name-card');
          const open = holder ? holder.id || (holder.querySelector('.decision-card') ? 'name-card' : '') : '';
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
          const again = open === 'name-card' ? screen.querySelector('section.name-card') : (open ? document.getElementById(open) : null);
          if (again && again.matches('tr.card-row')) {
            again.hidden = false;
            const toggle = screen.querySelector('[aria-controls="' + again.id + '"]');
            if (toggle) { toggle.setAttribute('aria-expanded', 'true'); }
          }
          const place = again ? again.querySelector('.card-said') : screen.querySelector('.account-said');
          if (place) { place.innerHTML = said; }
        });
        // The press running the rest of a night left unfinished: sent with the page's own header, and what the
        // surface said put beside it, the press kept disabled once the rest has started.
        document.addEventListener('submit', async (event) => {
          const form = event.target;
          if (!(form instanceof HTMLFormElement) || !form.classList.contains('night-control')) { return; }
          event.preventDefault();
          for (const button of form.querySelectorAll('button')) { button.disabled = true; }
          const response = await fetch(form.getAttribute('action'), {
            method: 'POST',
            headers: { '{{{PassHeader}}}': '{{{PassHeaderValue}}}' },
          });
          for (const old of form.parentElement.querySelectorAll('.night-said')) { old.remove(); }
          form.insertAdjacentHTML('afterend', await response.text());
          const said = form.nextElementSibling;
          if (!said || said.getAttribute('data-resume') !== 'started') {
            for (const button of form.querySelectorAll('button')) { button.disabled = false; }
          }
        });
        // A link followed or a row picked is a new place, and back or forward returns to where
        // the reader was. Anywhere on a row of tonight's list but its links picks that row, which
        // draws its plan beneath the list.
        document.addEventListener('click', (event) => {
          // The hide control at the foot of a fold closes it and brings its heading back into view.
          const hide = event.target.closest('.fold-hide');
          if (hide) {
            const fold = hide.closest('details');
            if (fold) { fold.open = false; fold.querySelector('summary').scrollIntoView({ block: 'center' }); }
            return;
          }
          // A link to a place on this screen. Every screen's address opens "#/", so a link whose hash
          // does not is a place rather than a screen, and is scrolled to rather than taken as an
          // address, which would draw a screen of that name that does not exist. It lands below the
          // masthead, measured as it stands, since the masthead stays at the top and wraps on a
          // narrower window.
          const jump = event.target.closest('a[href^="#"]:not([href^="#/"])');
          if (jump) {
            event.preventDefault();
            const place = document.getElementById(decodeURIComponent(jump.getAttribute('href').slice(1)));
            const mast = document.querySelector('.mast');
            if (place) { scrollTo({ top: place.getBoundingClientRect().top + scrollY - (mast ? mast.offsetHeight : 0) - 12 }); }
            return;
          }
          if (event.target.closest('#theme')) {
            const next = currentTheme() === 'dark' ? 'light' : 'dark';
            document.documentElement.setAttribute('data-eb-theme', next);
            try { localStorage.setItem('eb-theme', next); } catch (error) { }
            paintTheme();
            return;
          }
          // A pick's card opens and closes in place beneath its row, and the row is not picked for it.
          const toggle = event.target.closest('.card-toggle');
          if (toggle) {
            const card = document.getElementById(toggle.getAttribute('aria-controls'));
            if (card) {
              card.hidden = !card.hidden;
              toggle.setAttribute('aria-expanded', card.hidden ? 'false' : 'true');
            }
            return;
          }
          const row = event.target.closest('.list-table tr[data-ticker]');
          if (row && !event.target.closest('a, button, form')) {
            const pick = row.getAttribute('data-select-href');
            if (pick) { fresh = true; location.hash = mergedInto(pick); }
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
    // see: A screen reads and renders, and computes only the plan in the operator's money and a pick's open trades in its sector
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
        DateOnly? night = null,
        PeersView? peers = null,
        IReadOnlyList<ReactionCell>? reactions = null,
        SwingReadingsView? swing = null,
        GatesView? gates = null,
        FilterWhy? passed = null,
        bool? watched = null,
        IReadOnlyList<PickCell>? earlier = null,
        (DateOnly Evening, EquityBrief.Core.Filter.MissedGate Gate)? missed = null,
        NumbersSayView? says = null,
        NewsView? news = null,
        ListedUnderView? listedUnder = null,
        string? heavyweight = null,
        MemberReadingsView? member = null,
        DecisionCardView? decision = null)
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
        // see: A name's page for an earlier night draws what the store held that night and nothing it learned after
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
        identity.Append(watched is { } held ? WatchControl(ticker, held) : string.Empty);

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
        // present only when the name is on tonight's list or one gate short of it,
        // beneath the line section 18 draws where the listing was written before the
        // correction.
        // see: A member that missed exactly one gate and no exclusion is drawn close to a buy point nearest first, and recommends nothing
        var why = WrittenBeforeTheCorrectionLine(writtenBeforeTheCorrection)
            + (passed is { } filtered
                ? marks.WhyItPassed(ticker, filtered)
                : missed is { } oneShort
                    ? marks.WhyItIsClose(ticker, oneShort.Evening, oneShort.Gate)
                    : marks.WhyItIsHere(ticker, firedReasons));

        if (passed is not null || missed is not null || firedReasons.Count > 0)
        {
            Card("why", "Why it is here", Cards.Computed(
                "Why it is here",
                why,
                title: passed is { } through
                    ? Invariant($"On the list on {through.Evening:yyyy-MM-dd} because the swing filter passed it")
                    : missed is { } near
                    ? Invariant($"Close to a buy point on {near.Evening:yyyy-MM-dd}: one gate short")
                    : night is { } listed ? Invariant($"On the list on {listed:yyyy-MM-dd} for these reasons") : "On tonight's list for these reasons",
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

        // The swing readings, beneath the night's figures: each return with its place among the
        // members' returns, the recent high and the pullback from it, the volume while it came down
        // and the range's tightness, as the swing reader stored them for the night.
        // see: A page ranks no company as an investment, and the one reading of a company that orders a list is the direction of its reported quarters
        if (swing is not null)
        {
            Card("swing", "Its swing readings", Cards.Computed(
                "Swing readings",
                marks.SwingTable(ticker, swing) + Cards.Key(
                    "How to read it.",
                    Invariant($"Each return is the close against the close {EquityBrief.Core.Filter.SwingReadings.ReturnShortSessions} and {EquityBrief.Core.Filter.SwingReadings.ReturnLongSessions} sessions before it, beside the share of the index's other members whose return over the same span is lower. The pullback is how far the close sits below the highest high of the last {EquityBrief.Core.Filter.SwingReadings.HighWindow} sessions, counted in the moves {Escaped(ticker)} usually makes in a session; the volume beneath it is the median session's volume since that high against its fifty-day average, and the tightness is the last {EquityBrief.Core.Filter.SwingReadings.TightShortSessions} sessions' true range against the last {EquityBrief.Core.Filter.SwingReadings.TightLongSessions}'s. All of it is computed from the stored daily bars."),
                    "These are facts about the chart. A high place is a strong return behind the name and not a forecast in front of it, and this page " + RankRefusal + "."),
                title: "Where it stands for a swing trade",
                stamp: Cards.Night(swing.Session),
                id: "swing",
                region: "swing"));
        }

        // The member readings, beneath the swing readings: what a trade in it costs, the quality its quarters give it, its
        // year's high, its volume and its industry, as the member reader stored them for the night under its index.
        // see: A 400 or 600 trade pays the published effective spread for its size and price, and its pass tests read the edge after it
        if (member is not null)
        {
            Card("member", "Its member readings", Cards.Computed(
                "Member readings",
                marks.MemberReadingsTable(ticker, member) + Cards.Key(
                    "How to read it.",
                    Invariant($"The dollar volume is the mean of the close times the volume over the last {EquityBrief.Core.Readings.MemberReadings.DollarVolumeSessions} sessions. A round trip is half the published effective spread for the company's value and the price at the buy and half again at the sale, stated at the table's figure and at double. The profit gate sums net income over the {EquityBrief.Core.Readings.MemberReadings.Quarters} newest quarters filed before the night, and the coverage asks their operating income for at least twice their interest expense, a company filing none and a financial company passing. The year's high is the highest high of the {EquityBrief.Core.Readings.MemberReadings.YearSessions} sessions before the night, and the industry's figures are its S&amp;P 500 members', each weighted by its company's value."),
                    "These are facts about its trading and its quarters as they stood on the night. The S&amp;P 400's and 600's rules read the price, the dollar volume and the profit gate before they list a name, and the rest are readings their sweeps test."),
                title: Invariant($"What a trade in it costs and what its quarters say, as a member of the {Escaped(member.Index)}"),
                stamp: Cards.Night(member.Session),
                id: "member",
                region: "member"));
        }

        // The swing filter's answer for the name, whatever the name: each gate with whether it passed
        // and why, the setup and the trigger, the trade read both ways and the exclusions.
        if (gates is not null)
        {
            Card("gates", "Its gates", Cards.Computed(
                "The swing filter's gates",
                marks.GatesTable(ticker, gates) + Cards.Key(
                    "How to read it.",
                    "Each gate is one question the swing filter asks of every member each night, in order: the market's breadth, the trend and strength, a setup, a trigger new on the night, and a trade worth taking. A name passes only where all five pass and no exclusion applies. The trade is read from the ladder's first tranche and from the swing plan that night's live rule used, and the plan marked as read decides the gate. An alternative plan being tested in the background is not drawn: how it would have traded a stock is the evaluation it waits for.",
                    gates.Rule == EquityBrief.Core.Shortlist.ListRules.Filter
                        ? "A failed gate names what it read and why it failed, and a name passing all five that no exclusion removes is on that evening's list."
                        : "A failed gate names what it read and why it failed. The six reasons drew that evening's list, so these answers decided nothing on it."),
                title: "Where it stands against the swing filter",
                stamp: Cards.Night(gates.Session),
                id: "gates",
                region: "gates"));
        }

        // The short version, the first written region section 4 lists, with its date beside it. Where no
        // accepted one stands, code writes one from computed parts and its heading says why: the checker
        // left the model's out, the pass did not write it, or no research has been written for the name.
        // see: The short version is written last from the sections that passed, and one left out is replaced by a summary code writes
        if (sections.Any(section => string.Equals(section.Section, MarkRenderer.TheShortVersion, StringComparison.Ordinal)))
        {
            Draw(AtTheTop);
        }
        else
        {
            // The industry's cycle is its theme's and is drawn for every member, so it is not research
            // written for this name.
            var fellBack = (leftOut ?? []).Any(section => string.Equals(section.Section, MarkRenderer.TheShortVersion, StringComparison.Ordinal));
            var namedNotWritten = (notWritten ?? []).Any(section => string.Equals(section.Section, MarkRenderer.TheShortVersion, StringComparison.Ordinal));
            var nothingWritten = !sections.Any(section => !string.Equals(section.Section, EquityBrief.Core.Research.ClaimRules.CycleSection, StringComparison.Ordinal))
                && (leftOut ?? []).Count == 0;

            Card(SectionId(MarkRenderer.TheShortVersion), MarkRenderer.TheShortVersion, Cards.Computed(
                MarkRenderer.TheShortVersion,
                marks.ShortVersionByCode(
                    ticker,
                    fellBack ? MarkRenderer.ShortVersionRefused
                        : !namedNotWritten && nothingWritten ? MarkRenderer.ShortVersionNoResearch
                        : MarkRenderer.ShortVersionNotWritten,
                    night,
                    passed,
                    missed,
                    firedReasons,
                    says,
                    gates),
                title: "The short version, written by code",
                stamp: Cards.Night(session),
                id: SectionId(MarkRenderer.TheShortVersion),
                region: "code-summary"));
        }

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

        // The peers, beneath the table of the biggest moves: ten at most of the group those moves
        // are read against, by price alone, those sharing the name's industry first and then by how
        // closely each one's daily moves followed the name's.
        // see: Peers are shown by price alone, ten at most with the name's industry first and then the members whose daily moves followed it most closely
        if (peers is not null)
        {
            Card("peers", "Its group, by price", Cards.Computed(
                "Its group, by price",
                marks.PeersTable(ticker, peers) + Cards.Key(
                    "How to read it.",
                    Invariant($"{Escaped(ticker)} first, then at most {EquityBrief.Core.Moves.PeerPicks.Shown} members of the group the moves above are read against: those sharing its industry first, then the ones whose daily moves followed {Escaped(ticker)}'s most closely. Moved with it is the correlation of the two names' daily returns over the sessions both hold: 1 is in step every day, 0 is no relation and a negative figure moved the other way, and a pair sharing fewer than {EquityBrief.Core.Moves.PeerPicks.FewestSessions} sessions says how many rather than giving one. Each row gives the last stored close, how far it sits below the highest price among the bars the store holds for it, its return over the last {EquityBrief.Core.Moves.PeerReadings.ReturnWindow} sessions, its trend and where the close sits between its nearest bands, in typical days, all computed from the stored daily bars. A ticker opens its own page, and holding the pointer over it draws its year of closes with its nearest bands."),
                    "The order is how closely each moved with this name, not which is the better company, and nothing on the page is decided by this table."),
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
            "Relative strength compares the size of the stock's recent up days with its recent down days, on a scale of 0 to 100. The momentum line is the gap between a fast and a slow average of the price and its signal line a slower average of that gap, so the bars show whether the gap is widening or narrowing. Read them beside the bands: near a support band, relative strength at or under 30, or bars below zero shrinking toward it, says the fall is losing force, and near a resistance band, 70 or over, or bars above zero shrinking, says the rise is.",
            "Nothing in the plan or the list reads these. When they and the bands disagree, the plan follows the bands."));
        chart.Append("<div class=\"sub\">Levels</div>");
        chart.Append("<div class=\"tbl-wrap\">").Append(marks.LevelSummary(ticker, summary, absent, bars.Count > 0 ? bars[^1].Close : null)).Append("</div>");

        Card("chart", "The daily chart and its levels", Cards.Computed("The chart", chart.ToString(), title: "The daily chart and its levels", stamp: Cards.Night(session), id: "chart", region: "chart"));

        // The key under each figure, beneath the figures it explains.
        Draw(UnderTheFigures);

        // The plan region: the plan column and the two tables it is read beside, the event
        // setups and the sizing arithmetic.
        var planned = new StringBuilder();

        planned.Append("<div class=\"plan-grid\"><div class=\"fig\">").Append(marks.PlanColumn(ticker, close, plan)).Append("</div><div>");
        planned.Append(marks.PlanTables(ticker, plan)).Append("</div></div>");
        planned.Append("<div class=\"sub\">Around the next report</div>").Append(eventBook);
        planned.Append("<div class=\"sub\">Sizing arithmetic</div>").Append(arithmetic);
        planned.Append(Cards.Key(
            "How to read the plan.",
            "The price now sits in the middle of the column. Orange zones above it are where part of the position is sold, and green blocks below are where it is bought. Each thin rule is a stop, and the heavy rule is the invalidation, the lowest stop.",
            "Everything above the price marker is a sale, everything below it is a purchase, and the lowest line is where the whole idea is wrong. The risk you take is yours to choose; the page only does the division."));

        Card("plan", "Entry and exit plan", Cards.Computed("The plan", planned.ToString(), title: "Entry and exit plan", stamp: Cards.Night(session), id: "plan", region: "plan"));

        // The earnings reaction record, beside the earnings setups the plan closes on: what each
        // print over the calendar's year behind did on the session it moved.
        // see: Each print's reaction is read from the nightly calendar and the stored bars, and the earnings drift is the one rule that reads it
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

        // Every earlier night the live list picked the name, after the plan and its earnings reactions: how
        // many times and how each group ended, then a row per listing, each trade as the Past picks screen
        // draws it and as it stood on the night this page draws. Absent for a name never picked before.
        // see: Every trade the live list recommended is shown, and their share waits for the minimum the reason records wait for
        if (earlier is { Count: > 0 } picked)
        {
            Card("on-the-list-before", "On the list before", Cards.Computed(
                "On the list before",
                marks.OnTheListBefore(ticker, picked) + Cards.Key(
                    "How to read it.",
                    Invariant($"Each row is a night the live list picked {Escaped(ticker)} before this one, on the plan that night's rule traded: bought at that night's close and followed to its target, its stop, or the end of its {EquityBrief.Core.Returns.ForwardReturnSeries.SetupSessionCap} sessions. The line runs from the stop in green to the target in orange with the buy between them, and the dot is where the price stood on the night this page draws, hollow while the trade was open and filled where it finished. A result is what the trade made in multiples of what it risked."),
                    "What these trades did is a record of the list's picks, not a forecast for this one, and nothing on the page is decided by it. How every pick has done is on the Past picks screen."),
                title: Invariant($"The nights the live list picked {Escaped(ticker)} before"),
                stamp: Cards.Night(session),
                id: "on-the-list-before",
                region: "on-the-list-before"));
        }

        // What was written about the company in the thirty days before the night, each article with the
        // label the labeller wrote for it, after the nights the list picked the name and before the written
        // sections; drawn for every member the page is handed stories for.
        // see: The news labels alone name the model that wrote them
        if (news is { } stories)
        {
            Card("news", "News", Cards.Computed(
                "News",
                marks.NewsRegion(stories) + Cards.Key(
                    "How to read it.",
                    Invariant($"Each row is an article naming {Escaped(ticker)} from the thirty days before this night, newest first, with the kind of story and the way it cuts for the company as the news labeller read it, and its one-sentence reason while the row is under the pointer or holds focus. The bar counts positive against negative over the window; an opinion piece sits in the opinion tab alone and counts in neither. A row the labeller could not read, or an article admissibility refused, says so."),
                    "The labels are context for a reader and decide nothing: no gate, reason, plan or candidate reads them."),
                title: Invariant($"What was written about {Escaped(company)} in the thirty days before"),
                stamp: Cards.Night(session),
                id: "news",
                region: "news"));
        }

        // What the company sells and the segment commentary, after the plan and before the
        // numbers, where section 4 lists them.
        Draw(BeforeTheNumbers);

        // The numbers, which section 4 lists after the segment commentary. It arrives already written, for the
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

        // The industry cycle and the two cases, after the numbers where section 4 lists them.
        Draw(AfterTheNumbers);

        // The risks, after the two cases they test, where section 4 lists them.
        Draw(AfterThePlan);

        // What the research read, the region section 4 lists before the last: the calendar, the dated items a pass
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

        // Under which setup the page listed the name on the night, or why a setup that passed it does not
        // list it, on a night the setup families drew the page's list.
        // see: A stock holds one trade across every swing family, and one qualifying under two is listed once under the first in the page's order
        if (listedUnder is not null)
        {
            region.Append(marks.ListedUnder(listedUnder));
        }

        // Where the sector heavyweights hold the name at the night's close, which card holds it and since when.
        // see: The sector heavyweights hold the largest companies leading their sectors, rotated on the first session of each month whose stored year holds the closes their readings need
        if (heavyweight is not null)
        {
            region.Append(Invariant($"<p class=\"listed-under heavyweight-held\">{Escaped(heavyweight)}</p>"));
        }

        // The card of the family that listed the name on the night, at the top of the page.
        // see: A pick's card advises on the trade and removes no pick, and code computes every figure on it
        if (decision is not null)
        {
            region.Append(Invariant($"<section class=\"name-card\" data-family=\"{Escaped(decision.Family)}\" data-index=\"{Escaped(decision.Index)}\">{marks.DecisionCard(decision)}</section>"));
        }

        region.Append(body);

        // The walk, which section 15.9 puts last: previous and next on tonight's
        // list, so an evening's reading is one pass through with no return to
        // the list.
        region.Append(marks.Walk(ticker, previousOnTheList, nextOnTheList, night));

        region.Append("</section>");

        return region.ToString();
    }

    // What the name page is for, what it does not do, and its words, which open the page.
    // see: Four readings of a member's reported quarters are worked out every night by rules the measured split settled, and its state is read from sales and operating margin alone
    // see: The plan places a position and never sizes one
    static string Intro(string ticker, string company) =>
        $"<section class=\"intro\" aria-label=\"About this page\"><p class=\"intro-p\">This page finds the prices {Escaped(company)} has repeatedly stopped falling or rising at, and sets out what to do if it reaches one of them again. " +
        "The chart, the levels and the plan are recomputed every evening; the written sections further down carry their own dates. " +
        $"It carries no forecast, what it says of the business is read from {Escaped(company)}'s reported quarters by fixed rules, and it gives no opinion on how much of your money to put in.</p>" +
        $"<ul class=\"refuse\"><li>It does not predict where the price will go.</li><li>It {RankRefusal}.</li>" +
        "<li>It does not say how much to buy: the sizing near the end only divides the amount you choose to risk.</li></ul>" +
        "<details class=\"gloss\"><summary>Glossary of terms</summary>" +
        Cards.Words(
            ("Support", "A price below the current one where this stock has repeatedly stopped falling and turned back up."),
            ("Resistance", "A price above the current one where this stock has repeatedly stopped rising and turned back down."),
            ("Band", "A support or resistance drawn as a narrow range of prices, because a stock never turns at exactly the same cent twice."),
            ("Touch", "A day the price reached a band and turned away from it."),
            ("Typical daily move", "How far this stock's price usually moves in one session. Distances on these pages are counted in these."),
            ("Tranche", "One of several smaller purchases that together make up the whole position, each made at a different band."),
            ("Stop", "The price at which a purchase is sold to limit the loss. It is where you admit that part of the plan was wrong."),
            ("Invalidation", "The lowest stop. Below it the reason for owning the stock is gone, and everything is sold."),
            ("Reason", "One of the six stated conditions, which chose tonight's list before the swing filter did and stand beside its names as context from it."),
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
    // see: A screen reads and renders, and computes only the plan in the operator's money and a pick's open trades in its sector
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
        DateOnly? night = null,
        UniverseChoice? universe = null,
        string selector = "")
    {
        var reading = universe ?? Universes.Large;
        var heading = "The universe: " + reading.Name;
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

        region.Append(Invariant($"<section class=\"universe\" data-universe=\"{reading.Word}\" data-names=\"{rows.Count}\" data-shown=\"{shown.Length}\" "));
        region.Append(Invariant($"data-drawn=\"{drawn.Count}\" data-page=\"{at}\" "));
        region.Append(Invariant($"data-trend-filter=\"{Escaped(trendFilter ?? "all")}\" data-sector-filter=\"{Escaped(sectorFilter ?? "all")}\">"));

        region.Append(Cards.Masthead(
            heading,
            $"<span class=\"m-screen\">{Escaped(heading)}</span>",
            night is { } on ? Invariant($"Every {reading.Name} member on the night of {on:yyyy-MM-dd}") : Invariant($"Every {reading.Name} member")));

        region.Append(selector);

        // The setup ledger of the same index, under Universe.
        region.Append(Invariant($"<p class=\"ledger-link\"><a href=\"{LedgerRoute}?universe={reading.Word}\" data-ledger=\"{reading.Word}\">The setup ledger: every near-setup on the {Escaped(reading.Name)} and how it turned out</a></p>"));
        region.Append(Invariant($"<p class=\"loop-link\"><a href=\"{LoopRoute}?universe={reading.Word}\" data-loop=\"{reading.Word}\">The loop: each family's proposals on the {Escaped(reading.Name)}, tested on years they never saw</a></p>"));

        // The S&P 400's and 600's members are read by the stages every member's figures need, and not by the listings,
        // the ladder or the swing readings, which read the S&P 500's alone, so their rows say so where those draw.
        if (reading != Universes.Large)
        {
            region.Append(Invariant($"<p class=\"oneline\" data-universe-reads=\"members\">The {Escaped(reading.Possessive)} {rows.Count} members carry their closes, typical moves and bands; the trend, the swing readings and the listing strip are read for the S&amp;P 500's members alone, so those columns are empty here.</p>"));
        }

        region.Append(WrittenBeforeTheCorrectionLine(writtenBeforeTheCorrection));
        region.Append(Cards.Computed("Sectors", marks.SectorStrip(sectors), stamp: Cards.Night(night), region: "sectors"));

        var table = new StringBuilder();

        var kept = reading == Universes.Large ? null : reading.Word;

        table.Append(marks.UniverseFilters(rows, trendFilter, sectorFilter, kept));
        table.Append("<div class=\"tbl-wrap\">").Append(marks.UniverseTable(drawn)).Append("</div>");
        table.Append(marks.UniversePaging(shown.Length, at, pageSize > 0 ? pageSize : Math.Max(1, drawn.Count), trendFilter, sectorFilter, kept));
        table.Append("<p class=\"oneline\">The two right-hand columns count evenings a name appeared on the list. They say nothing about index membership, which every name here has.</p>");
        table.Append(Cards.Key(
            "How to read the rows.",
            "The distance picture fixes each close at its centre line. The green block to its left is the nearest support, the orange block to its right the nearest resistance, one tick per typical day. Scan down that column for blocks touching the centre. In the strip, each column is one session, marked on an evening the name was on the list and a rule on an evening it was not.",
            "Names at the top are closest to one of their own levels, measured in days of their own ordinary movement. A strip with marks spread across it belongs to a name that keeps returning to its levels; a single mark is a one-off."));

        region.Append(Cards.Computed(
            "The index",
            table.ToString(),
            title: Invariant($"Every {reading.Name} member, nearest a level first"),
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
    public string ResearchedRegion(IReadOnlyList<ResearchedCell> rows, UniverseChoice? universe = null, string selector = "")
    {
        var reading = universe ?? Universes.Large;
        var heading = "Researched: " + reading.Name;
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

        return Invariant($"<section class=\"researched\" data-universe=\"{reading.Word}\" data-names=\"{rows.Count}\">")
            + Cards.Masthead(
                heading,
                $"<span class=\"m-screen\">{Escaped(heading)}</span>",
                rows.Count == 0 ? Invariant($"No {reading.Name} member holds researched sections yet") : Invariant($"{rows.Count} {reading.Name} member(s) hold researched sections"))
            + selector
            + Cards.Computed(
                "Researched",
                body.ToString(),
                title: Invariant($"{reading.Name} members with research"),
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
    public string QueueRegion(IReadOnlyList<QueuedCell> rows, QueueEstimate? estimate = null, QueueStop? stopped = null)
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

        // A drain that stopped on an error, said until a pass starts after it.
        // see: A drain that stops on an error writes a row of its own, and the queue page states it until a pass starts after it
        if (stopped is not null)
        {
            body.Append(Invariant($"<p class=\"drain-stopped\" data-started-at=\"{Escaped(stopped.StartedAt)}\">The drain that started at {Escaped(stopped.Words)} stopped on an error: {Escaped(stopped.Error)}; what is queued waits for the next drain, which a press or the next night starts.</p>"));
        }

        // What every time on the page rests on, stated once above them.
        if (estimate is not null)
        {
            body.Append(estimate.Minutes is { } minutes
                ? Invariant($"<p class=\"queue-estimate\" data-passes=\"{estimate.Passes}\" data-median-minutes=\"{minutes}\" data-longest-minutes=\"{estimate.LongestMinutes}\">Times are New York's with the offset named and UTC beside them. A pass is expected to take {minutes} minutes, the median of the {estimate.Passes} {(estimate.Passes == 1 ? "pass" : "passes")} the store holds that ran to their end, and a pass starts only where the longest of them, {estimate.LongestMinutes} minutes, would end before the next peak window opens.</p>")
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
            "Every report that has been asked for, from a row on tonight's list, from a name's own page or by the night for six names taken in turn across the three indices' pages, with what came of it. One name holds one outstanding request at a time, which the store enforces: a second press for a name already waiting adds nothing and says so.",
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
        int watching,
        string selectedName,
        HarnessCounts? harness,
        string? selectedTicker = null,
        IReadOnlyList<ReasonRecord>? records = null,
        IReadOnlyList<ReasonTrackRow>? totals = null,
        NightSpend? spend = null,
        NightProse? prose = null,
        IReadOnlyList<DateOnly>? writtenBeforeTheCorrection = null,
        MarketView? market = null,
        ListRuleView? rule = null,
        int? listed = null,
        IReadOnlyList<DateOnly>? held = null,
        IReadOnlyList<ListingCell>? close = null,
        IReadOnlyList<StillOpenCell>? stillOpen = null,
        IReadOnlyList<FamilyCardView>? families = null,
        MarketLineView? line = null,
        IReadOnlyList<CloseToBuyCell>? closeAcross = null,
        HeavyweightCardView? heavyweights = null,
        string selector = "")
    {
        var region = new StringBuilder();
        var byFilter = rule is { Rule: EquityBrief.Core.Shortlist.ListRules.Filter };
        var heading = "Tonight: " + Universes.Large.Name;

        // A night whose readings of the reported quarters are stored draws its names state first, and one
        // before them in the filter's own order, so the card says which it drew.
        // see: Tonight's list is the swing filter's with improving businesses drawn first, and an evening is listed and ordered by the rule that listed it
        var byState = rows.Any(row => row.Business is not null);

        region.Append(Invariant($"<section class=\"tonight\" data-night=\"{night:yyyy-MM-dd}\" data-index=\"{index}\" data-fired=\"{fired}\" data-rule=\"{Escaped(rule?.Rule ?? EquityBrief.Core.Shortlist.ListRules.Reasons)}\" "));
        region.Append(Invariant($"data-selected=\"{Escaped(selectedTicker ?? "none")}\">"));

        region.Append(Cards.Masthead(
            heading,
            $"<span class=\"m-screen\">{Escaped(heading)}</span>",
            Invariant($"Night of {night:yyyy-MM-dd}, computed after the close") + Cards.NightPicker(night, held ?? [], NightRoute, "#/")));

        region.Append(selector);

        region.Append(Cards.Computed(
            Invariant($"Night of {night:yyyy-MM-dd} · computed after the close"),
            marks.NightHeader(night, index, fired, duration, harness, spend, prose, market, byFilter ? listed : null)
                + Invariant($"<p class=\"oneline\"><a href=\"{RunRoute}{night:yyyy-MM-dd}\">What ran tonight, and what it cost</a></p>"),
            stamp: Cards.Night(night),
            region: "night"));

        region.Append(WrittenBeforeTheCorrectionLine(writtenBeforeTheCorrection));

        region.Append(Cards.Computed(
            "Watch list",
            WatchLine(watching),
            title: "Shown every evening",
            lede: "These names appear whether or not they are on the list.",
            region: "watch"));

        // On a night the families drew the page's list, one card a family in the page's order, each with
        // its rule in a sentence, whether it is live or provisional, its picks and the notes on what it
        // passed and the page holds back; on a night before them, the one list as it was drawn.
        // see: Tonight's page is drawn from setup families, each a rule of its own listing at most five a night
        if (families is not null)
        {
            // The line the page opens its setups on: whether the market check left the lists open, and the
            // night's counts across every setup.
            // see: The market check closes every swing family's list together, and the sector heavyweights read none
            if (line is not null)
            {
                region.Append(marks.MarketLine(line));
            }

            foreach (var card in families)
            {
                region.Append(Cards.Computed(
                    Invariant($"Setup {card.Place} of {card.Of} · {card.Eyebrow}"),
                    marks.FamilyCard(card) + Cards.Key(
                        "How to read the card.",
                        "Each row is one stock this setup lists tonight, bought at the evening's close. The stop is the price that says the plan was wrong and the target the price that says it was right, and the bar shows where the buy sits between them. Select a row to draw its plan just below the cards; report opens the stock's full page.",
                        "A stock is listed once across every card, under the first setup it qualified for, and never while a trade for it is still open; the notes under the rows name what was held back and why. The reward to risk is a fact about the chart and not a chance of anything."),
                    title: Escaped(card.Heading),
                    lede: Escaped(card.Rule),
                    stamp: Cards.Night(night),
                    region: "family"));
            }

            // The sector heavyweights' card after the swing families', its holdings, its last rebalance and its
            // next, drawn whatever the market check read.
            // see: The sector heavyweights hold the largest companies leading their sectors, rotated on the first session of each month whose stored year holds the closes their readings need
            // see: The market check closes every swing family's list together, and the sector heavyweights read none
            if (heavyweights is not null)
            {
                region.Append(Cards.Computed(
                    Invariant($"Rotation · {heavyweights.Eyebrow}"),
                    marks.HeavyweightCard(heavyweights) + Cards.Key(
                        "How to read the card.",
                        "Each row is a stock the sector heavyweights hold at tonight's close, bought at the close of a month's rebalance, its first session or the first after it whose stored year holds the closes the readings need, as one of the two leaders of its sector's largest companies. It has no stop and no target: it is held while it leads, and sold at the close of a later month's rebalance where the rule would no longer buy it, or at its last close as a member of the index. Its lead is how far its return over the look-back ran ahead of its sector fund's at the rebalance that read it.",
                        "A month-long holding and not a swing trade, so the market check that closes the swing setups' lists does not close this card, and a stock held here can be listed by a swing setup too: each card keeps its own one trade a stock."),
                    title: Escaped(heavyweights.Heading),
                    lede: Escaped(heavyweights.Rule),
                    stamp: Cards.Night(night),
                    region: "heavyweights"));
            }
        }
        else
        {
        region.Append(Cards.Computed(
            "The list",
            marks.TonightList(rows, TonightDrawn, records, rule) + Cards.Key(
                "How to read the list.",
                "Each reason has its own column, always in the same place, so a night that is all one thing shows as one dark stripe running down one column. The one-word heads are short for at entry zone, crossed a level, breakout on volume, trend state changed, unusual volume and earnings soon; point at a head for its full name, and at a reason for the values that made it true and its record. The distance picture fixes the close at its centre line: the green block to its left is the nearest support and the orange block to its right the nearest resistance, one tick per typical day, so a block touching the centre is a name at an edge. The last line of each column is that reason's record across every name it has fired for, and a dashed one is not yet measured. Select a row to draw its plan just below the list; report opens the name's full page.",
                (byFilter
                    ? "Every name here passed the swing filter's five gates at a price its own chart made significant, and its gates say whether it pulled back to support or broke out; the reasons beside it are context."
                        + (byState ? " The word beside the trend is the state the company's reported quarters give it, read from sales and operating margin alone; it orders the names and removes none." : string.Empty)
                    : "Every name here has reached a price its own chart made significant, and the reason says what kind of arrival it was.")
                    + " The distance picture counts in days of the stock's own ordinary movement, so a block one tick from the centre is a distance the price often covers in a single session."),
            title: byFilter ? "Names at a buy point tonight" : "Names that fired tonight",
            lede: byFilter
                ? byState
                    ? "Each passed every gate of the swing filter, its trigger arrived and nothing excluded it; improving businesses first, then steady, then the names whose quarters read no state, then deteriorating, each state in the filter's order, the trade's reward to risk first, then strength, then band strength. The reasons stand beside them as context."
                    : "Each passed every gate of the swing filter, its trigger arrived and nothing excluded it; in the filter's order, the trade's reward to risk first, then strength, then band strength. The reasons stand beside them as context."
                : "Each one has reached a price its own chart made significant. Most reasons first, then the plan's reward to risk.",
            stamp: Cards.Night(night),
            region: "list"));
        }

        // "Still open", between the list and "Close to a buy point" on a night the swing filter listed: the
        // stocks that passed every gate tonight while a trade the list recommended for them on an earlier
        // night is still open. A stock holds one open trade at a time, so none of these is a new trade. On a
        // night the families drew the list, the card of the family that passed the stock says so in a note.
        // see: A stock holds one open trade on each rule's list, and it is free the night after its trade ends
        if (byFilter && stillOpen is not null && families is null)
        {
            region.Append(Cards.Computed(
                "Still open",
                marks.StillOpen(stillOpen, night) + Cards.Key(
                    "How to read it.",
                    "Each stock here passed every gate of the swing filter tonight while a trade the list recommended for it on an earlier night is still open: that trade has not reached its target or its stop, and its sessions have not run out. A stock holds one open trade at a time, so this is not a new trade; the row shows where the price stands against the open trade's stop and target, and a trade that ended at tonight's close frees the stock from the next night.",
                    "Until the rule reaches the filter the stock is still drawn on the list above, marked as listed again; from then on it is excluded there and drawn here alone."),
                title: "Passed again while an earlier trade is still open",
                lede: "One open trade per stock: a stock the list already holds is not listed again until the night after its trade ends.",
                stamp: Cards.Night(night),
                region: "still-open"));
        }

        // "Close to a buy point", beneath the list on a night the swing filter listed: the members one gate
        // short, drawing the places the list leaves of the twenty, nearest to qualifying first. It recommends
        // nothing, which its key says.
        // see: A member that missed exactly one gate and no exclusion is drawn close to a buy point nearest first, and recommends nothing
        // On a night the families drew the page's list it is one list across every setup, a row a stock and
        // setup with the setup's label and the one gate it missed, the setups in the page's order.
        // see: Tonight's page is drawn from setup families, each a rule of its own listing at most five a night
        if (families is not null && closeAcross is not null)
        {
            region.Append(Cards.Computed(
                "Close to a buy point",
                marks.CloseAcross(closeAcross, TonightDrawn) + Cards.Key(
                    "How to read this list.",
                    "Each row is a stock that passed every gate of one setup but one tonight, with nothing excluding it. It is not a buy point and recommends nothing: it is what to watch for tomorrow.",
                    "A stock a single gate short under two setups has a row for each. The setups are drawn in the page's order."),
                title: "Close to a buy point",
                lede: "One list across every setup: each row passed every gate of its setup but one.",
                stamp: Cards.Night(night),
                region: "close"));
        }
        else if (byFilter && close is not null)
        {
            region.Append(Cards.Computed(
                "Close to a buy point",
                marks.TonightList(close, TonightDrawn - Math.Min(rows.Count, TonightDrawn), rule: rule, oneGateShort: true) + Cards.Key(
                    "How to read this list.",
                    CloseKeyText,
                    "Each row reads as a row of the list above does, with the one gate it missed in place of the gates it passed. The distance is how far short it is as a share of the bar it needed. The news labeller reads the list above alone, so a row here carries no news counts."),
                title: "Close to a buy point",
                lede: "Each passed every gate of the swing filter but one, and nothing excluded it; nearest to qualifying first, then in the list's own order.",
                stamp: Cards.Night(night),
                region: "close"));
        }

        // The selected name's plan and level summary, which is what section 15.7
        // means by the common case needing no navigation: selecting a row draws its
        // plan beneath the list, on the same page, and brings it into view.
        // see: Selecting a row draws its plan beneath the list and is no navigation
        if (selectedName.Length > 0 && selectedTicker is { } chosen)
        {
            var picked = rows.Concat(close ?? []).FirstOrDefault(row => row.Ticker == chosen);
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
                    "Most of tonight's fired names carry the same reason, so the evening is one thing happening to many names. Several short bars mean different things happened to a few names each.",
                    "These are tonight's counts. How a reason's setups have ended over time is on the run page, read against the base rate."),
                title: byFilter ? "Which reasons fired tonight, as context" : "Which reasons put tonight's names on the list",
                lede: Invariant($"How many of tonight's {fired} fired names carry each reason."),
                stamp: Cards.Night(night),
                region: "totals"));
        }

        region.Append("</section>");

        return region.ToString();
    }

    // Tonight's page for the S&P 400 or the S&P 600: its heading naming the index, the Universe selector, the line its
    // setups open on with the index's own breadth and counts, one card a swing family each provisional with its rule
    // written from the index's settings, and the index's sector heavyweights' card. A night the index's families did not
    // read says so.
    // see: Every page reads one index at a time chosen under Universe, and every figure names its index
    public string IndexTonightRegion(
        MarkRenderer marks,
        DateOnly night,
        UniverseChoice universe,
        string selector,
        IReadOnlyList<DateOnly> held,
        MarketLineView? line,
        IReadOnlyList<FamilyCardView> families,
        HeavyweightCardView? heavyweights,
        string? notComputed = null)
    {
        var region = new StringBuilder();
        var heading = "Tonight: " + universe.Name;
        var query = "?" + Universes.Query + "=" + universe.Word;

        region.Append(Invariant($"<section class=\"tonight\" data-night=\"{night:yyyy-MM-dd}\" data-universe=\"{universe.Word}\" data-index-code=\"{Escaped(universe.Code)}\" data-selected=\"none\">"));

        region.Append(Cards.Masthead(
            heading,
            $"<span class=\"m-screen\">{Escaped(heading)}</span>",
            Invariant($"Night of {night:yyyy-MM-dd}, computed after the close") + Cards.NightPicker(night, held, NightRoute, "#/" + query, query)));

        region.Append(selector);

        if (line is null)
        {
            region.Append(Invariant($"<p class=\"degraded\" data-index-night=\"none\">The {Escaped(universe.Possessive)} families read nothing for {night:yyyy-MM-dd}: no night of theirs is stored for it.</p>"));
            region.Append("</section>");

            return region.ToString();
        }

        // A night whose part failed opens on the words its cards carry in place of a market line it did not read.
        region.Append(notComputed is { } said
            ? $"<p class=\"degraded\" data-index-night=\"not-computed\">{Escaped(said)}</p>"
            : marks.MarketLine(line));

        foreach (var card in families)
        {
            region.Append(Cards.Computed(
                Invariant($"Setup {card.Place} of {card.Of} · {card.Eyebrow}"),
                marks.FamilyCard(card) + Cards.Key(
                    "How to read the card.",
                    $"Each row is one stock this setup lists tonight among the {Escaped(universe.Possessive)} members, bought at the evening's close. The stop is the price that says the plan was wrong and the target the price that says it was right, and the bar shows where the buy sits between them; report opens the stock's full page.",
                    $"The rule runs on provisional settings until its freeze, read on the {Escaped(universe.Possessive)} own members alone, and its record starts at the freeze. A stock is listed once across every card of every index, and never while a trade for it is still open on any of them; the notes under the rows name what was held back and on which index's list."),
                title: Escaped(card.Heading),
                lede: Escaped(card.Rule),
                stamp: Cards.Night(night),
                region: "family"));
        }

        if (heavyweights is not null)
        {
            region.Append(Cards.Computed(
                Invariant($"Rotation · {heavyweights.Eyebrow}"),
                marks.HeavyweightCard(heavyweights) + Cards.Key(
                    "How to read the card.",
                    $"Each row is a stock the {Escaped(universe.Possessive)} sector heavyweights hold at tonight's close, bought at the close of a month's rebalance, its first session or the first after it whose stored year holds the closes the readings need, as one of the leaders of its sector's largest members of the index. It has no stop and no target: it is held while it leads, and sold at the close of a later month's rebalance where the rule would no longer buy it, or at its last close as a member of the index.",
                    "A month-long holding and not a swing trade, so the market check that closes the swing setups' lists does not close this card."),
                title: Escaped(heavyweights.Heading),
                lede: Escaped(heavyweights.Rule),
                stamp: Cards.Night(night),
                region: "heavyweights"));
        }

        region.Append("</section>");

        return region.ToString();
    }

    // The refusal to rank, held once: the name page draws it, and sections 15.9 and 15.14 state it in these words.
    // see: A page ranks no company as an investment, and the one reading of a company that orders a list is the direction of its reported quarters
    public const string RankRefusal =
        "ranks no company as an investment, and the one rank it draws is a return's place among the members' returns, a fact about the chart";

    // Section 17's list display count, held here so the app and the projection
    // agree about it rather than each stating it. The two lists on Tonight share it.
    public const int TonightDrawn = 20;

    // What "Close to a buy point" is and is not, under its rows.
    // see: A member that missed exactly one gate and no exclusion is drawn close to a buy point nearest first, and recommends nothing
    public const string CloseKeyText =
        "These stocks are not picks. None of them passed the filter, the edge clock and Past picks never count them, and the near-miss measurement on the run page already scores stocks like these by the gate they missed. They are here so a stock one condition away is seen before it arrives rather than after.";

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

    // The run page, section 15.10's regions in the order that section states them: the questions a
    // reader asks answered top down, each a picture with a line or two of plain words beneath it, and
    // every table the page drew before kept whole beneath them in a section folded shut, so nothing is
    // removed. A region with nothing to draw says so rather than drawing empty, because an empty region
    // reads as a night that produced nothing.
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
        TrendVersionRegion? versions = null,
        ShapeState? shape = null,
        MarketView? market = null,
        FunnelView? funnel = null,
        IReadOnlyList<TriggerLine>? triggers = null,
        ProposalView? proposal = null,
        OverlapView? overlap = null,
        EdgeView? edge = null,
        NearMissView? nearMisses = null,
        IReadOnlyList<DateOnly>? held = null,
        NightView? how = null,
        MarketPicture? picture = null,
        PicksSummary? trades = null,
        IReadOnlyList<FreshNight>? fresh = null,
        ResearchPicture? research = null,
        IReadOnlyList<WorryItem>? worries = null,
        IReadOnlyList<VersionLine>? background = null,
        CompareView? compare = null,
        IReadOnlyList<CheckpointRow>? checkpoints = null,
        ReportsView? reports = null,
        IReadOnlyList<FamilyRunRow>? setupFamilies = null,
        IReadOnlyList<FamilyRecordRow>? familyRecords = null,
        StoreCopyRead? storeCopy = null,
        string selector = "")
    {
        var region = new StringBuilder();
        var heading = "Run evidence: " + Universes.Large.Name;

        region.Append(Invariant($"<section class=\"run\" data-night=\"{night:yyyy-MM-dd}\" data-universe=\"{Universes.Large.Word}\" data-stages=\"{stages.Count}\">"));

        region.Append(Cards.Masthead(
            heading,
            $"<span class=\"m-screen\">{Escaped(heading)}</span>",
            Invariant($"Night of {night:yyyy-MM-dd}") + Cards.NightPicker(night, held ?? [], RunRoute, RunRoute)));

        region.Append(selector);

        // Once sixty ordinary nights are stored under the open version, the page says so before anything else.
        region.Append(shape is { } due ? marks.ShapeDue(due) : string.Empty);

        // How last night went: its state from its own run log rows and its tries, the four headline figures,
        // the time its steps took, and the press running the rest of a night left unfinished, above
        // everything else.
        // see: A night's state is read off its own run log rows and its tries, and the pages that state it read that one state
        if (how is { } went)
        {
            region.Append(Cards.Computed(
                "Last night",
                marks.NightStatus(went) + NightPress(went),
                title: "How last night went",
                lede: "What the night's own run log says of it: its state, what it read and cost, and where its time went.",
                stamp: Cards.Night(night),
                region: "night"));
        }

        // The market and the funnel side by side, each a picture with its words beneath.
        region.Append("<div class=\"run-pair\">");

        if (picture is { } pictured)
        {
            region.Append(Cards.Computed(
                "The market",
                marks.MarketRegion(pictured),
                title: "How the market stood",
                lede: "The share of the index above its own long average, which the market gate reads, and how heavily the index traded.",
                stamp: Cards.Night(night),
                region: "market-picture"));
        }

        region.Append(Cards.Computed(
            "Tonight's list",
            marks.FunnelPicture(funnel, NightRoute + night.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)),
            title: "From the whole index to the list",
            lede: "How many stocks passed each of the swing filter's checks in turn, down to the ones listed.",
            stamp: Cards.Night(night),
            region: "funnel-picture"));

        region.Append("</div>");

        // How the live list's trades are going and whether it finds new stocks, side by side.
        region.Append("<div class=\"run-pair\">");

        if (trades is { } picked)
        {
            region.Append(Cards.Computed(
                "The picks so far",
                marks.TradesRegion(picked, PicksRoute),
                title: "How the list's trades are going",
                lede: "Every trade the live list recommended, by where each one stands; the share that won waits until enough have finished to say something.",
                stamp: Cards.Night(night),
                region: "trades-picture"));
        }

        if (fresh is { } evenings)
        {
            region.Append(Cards.Computed(
                "Freshness",
                marks.FreshBars(evenings, night),
                title: "Is the list finding new stocks?",
                lede: "Each evening's list, split into the names new that evening and the ones it listed the evening before too.",
                stamp: Cards.Night(night),
                region: "fresh-picture"));
        }

        region.Append("</div>");

        // How the system learns, tonight's picks compared with a version's, and each version at a checkpoint.
        // Picks only: a version's outcomes wait for its look.
        // see: Candidate conditions are registered before they are scored, and a candidate's picks are shown on the Run page while its outcomes wait for a look
        if (background is { } running && edge is { } judging)
        {
            var route = RunRoute + night.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            region.Append(Cards.Computed(
                "The learning loop",
                marks.LearningRegion(shape, judging, running, route + "?version=" + (running.FirstOrDefault(version => !version.Live)?.Slug ?? string.Empty)),
                title: "How the system learns",
                lede: "Two clocks run every night, and nothing changes the list without your decision: the first tunes how many stocks pass each check, and the second judges over years whether the picks make money, by running other versions of the rules beside the live one.",
                stamp: Cards.Night(night),
                region: "learning-picture"));

            if (compare is { } compared)
            {
                region.Append(Cards.Computed(
                    "The learning loop",
                    marks.CompareRegion(compared, route, NameRoute),
                    title: "Compare tonight's picks",
                    lede: "What a background version would have listed on this night, beside the live list. Picks only: how each version's trades turn out stays hidden until its checkpoint.",
                    stamp: Cards.Night(night),
                    region: "compare-picture"));
            }

            if (checkpoints is { } rows)
            {
                region.Append(Cards.Computed(
                    "The learning loop",
                    marks.CheckpointRegion(rows),
                    title: "At a checkpoint",
                    lede: "Each version's results, locked until its first checkpoint, about two years in, when a clearly worse version can be dropped; none can be promoted before the second.",
                    stamp: Cards.Night(night),
                    region: "checkpoint-picture"));
            }
        }

        // Research and spend beside anything to worry about.
        region.Append("<div class=\"run-pair\">");

        if (research is { } written)
        {
            region.Append(Cards.Computed(
                "Research",
                marks.ResearchRegion(written),
                title: "Written reports and what they cost",
                lede: "What the paid model spent this month against its cap, and the reports and drafts written over the last week.",
                stamp: Cards.Night(night),
                region: "research-picture"));
        }

        if (worries is { } items)
        {
            // The store's newest copy beneath the checklist, where the surface was handed the copies to read.
            // see: The store is copied once the night and every process it started have finished and the newest three copies are kept after each is opened and read, and the copy writes a row as it starts and one as it ends
            region.Append(Cards.Computed(
                "Health",
                marks.WorryRegion(items, harness) + (storeCopy is { } copies ? marks.StoreCopyLine(copies.Newest) : string.Empty),
                title: "Anything to worry about?",
                lede: "Each item turns red with its reason when it fails.",
                stamp: Cards.Night(night),
                region: "worry-picture"));
        }

        region.Append("</div>");

        // The detail: every table the page drew before, each in a section folded shut, so a reader checking
        // the arithmetic finds it whole and a reader glancing does not have to scroll past it.
        region.Append("<section class=\"run-detail\"><div class=\"lbl\">The detail, for when you want it</div>");
        region.Append(Fold("operational", "Every step of the night, with its time and what it wrote"));
        region.Append(Cards.Computed(
            Invariant($"Run of {night:yyyy-MM-dd}"),
            "<div class=\"tbl-wrap\">" + marks.OperationalHeader(night, stages, priced) + "</div>",
            title: "What ran, and what it cost",
            lede: "Each stage with the instant it started in UTC, how long it took, what it wrote and what it said about itself.",
            stamp: Cards.Night(night),
            region: "operational"));
        region.Append(Folded);

        // How each report's sections came out. The drafts a trial or a review wrote beside a report are written to
        // files by a command and drawn on no page.
        // see: The run page draws how each report's sections came out and each section's rates over the newest twenty reports, and no trial's drafts
        if (reports is { } read)
        {
            region.Append(Fold("reports", "How each report did, section by section, and each section's rates"));
            region.Append(Cards.Computed(
                "Reports",
                marks.ReportsRegion(read, NameRoute),
                title: "How each report did",
                lede: "Each section of each report the paid model wrote over the last seven nights: passed the claim check first time or on its retry, left out, or standing from an earlier day, with what its calls cost.",
                stamp: Cards.Night(night),
                region: "reports"));
            region.Append(Folded);
        }

        // The market on the night: the breadth, the share above the shorter average as context, and how
        // heavily the index traded.
        region.Append(Fold("market", "The market and the funnel, as the tables of counts"));
        region.Append(Cards.Computed(
            Invariant($"Market of {night:yyyy-MM-dd}"),
            marks.MarketReading(market),
            title: "The market on the night",
            lede: "How much of the index closed above its own long average, the same above the shorter one, and how heavily the index traded against its own fifty-day average, each read off the stored bars and averages.",
            stamp: Cards.Night(night),
            region: "market"));

        // The swing filter's funnel: how many members each gate passed in order and how many it removed.
        region.Append(Cards.Computed(
            Invariant($"Swing filter of {night:yyyy-MM-dd}"),
            marks.Funnel(funnel),
            title: "The swing filter's funnel",
            lede: "Every member through the five gates in order, how many each passed and removed, and how many the exclusions removed of the rest. It is stored for every member every night, and on an evening the swing filter listed, the names passing are the list.",
            stamp: Cards.Night(night),
            region: "funnel"));

        region.Append(WrittenBeforeTheCorrectionLine(writtenBeforeTheCorrection));
        region.Append(Folded);

        region.Append(Fold("records", "The old list's reasons, kept as context"));
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
        region.Append(Folded);

        if (shape is { } clock)
        {
            region.Append(Fold("calibration", "Each check's pass counts against its range, the shape proposal and the edge clock"));
            region.Append(Cards.Computed(
                "Calibration",
                marks.Calibration(clock, triggers ?? []) + marks.Proposal(proposal) + marks.Edge(edge) + marks.NearMisses(nearMisses) + Cards.Key(
                    "How to read it.",
                    "The shape clock counts, over the ordinary nights under the open filter version, how many members pass each gate after the market and every gate before it, the market held open, and how many reach the list, against the bands each is calibrated to. An event night, one on which a usually quiet gate or reason passes more than a quarter of the index or the index trades at 1.8 times its fifty-day volume, is counted and read by no median. The lines beneath count what five other settings are waiting on.",
                    "A median outside its band is what the shape calibration moves a threshold for, once sixty ordinary nights are stored; until then the figures are drawn as not yet measured and decide nothing."),
                title: "What the calibration is waiting on",
                lede: "Thresholds move only through the shape calibration and your rulings.",
                stamp: Cards.Night(night),
                region: "calibration"));
            region.Append(Folded);
        }

        // The list from night to night: of tonight's names, how many were on it before.
        region.Append(Fold("overlap", "The list from night to night, as counts"));
        region.Append(Cards.Computed(
            Invariant($"Overlap of {night:yyyy-MM-dd}"),
            marks.Overlap(overlap),
            title: "The list from night to night",
            lede: "How many of the night's names were on the list the evening before and over the five and twenty evenings before, each evening read by the rule that listed it.",
            stamp: Cards.Night(night),
            region: "overlap"));
        region.Append(Folded);

        region.Append(Fold("candidates", "The background versions: statistics and checkpoints"));
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

        region.Append(Folded);

        if (versions is { } trend)
        {
            region.Append(Fold("trend-versions", "Rule versions being measured on the plan's rules"));
            region.Append(Cards.Computed(
                "The trend rule's versions",
                marks.TrendVersions(trend),
                title: "What each open version would have labelled, and what it would have taken away",
                lede: "A version changes the rule and no list: nothing here is drawn beside a name.",
                stamp: Cards.Night(night),
                region: "trend-versions"));
            region.Append(Folded);
        }

        // The setup families the page is drawn from: each one's standing, what it lists on the night, the
        // trades the page has listed under it and its record.
        // see: Tonight's page is drawn from setup families, each a rule of its own listing at most five a night
        if (setupFamilies is { Count: > 0 })
        {
            region.Append(Cards.Computed(
                "Setup families",
                marks.FamilyRun(setupFamilies) + (familyRecords is { Count: > 0 } ? marks.FamilyRecords(familyRecords) : string.Empty) + Cards.Key(
                    "How to read it.",
                    "Each row is one setup the page is drawn from. A live setup's rule is registered and its record counts from that day; a provisional one runs on settings taken from published evidence until its sweep proposes the values its freeze registers.",
                    "A provisional setup's trades are listed and followed like any other and are in no share and no checkpoint until its freeze."),
                title: "The setups the page is drawn from",
                lede: "One row a setup, in the page's order: where its rule stands, what it lists tonight and how its trades stand.",
                stamp: Cards.Night(night),
                region: "families"));
        }

        if (orders is { } comparison)
        {
            region.Append(Fold("orders", "The order tonight's list is drawn in, against the one it replaced"));
            region.Append(Cards.Computed(
                "Tonight's order",
                marks.TonightsOrder(comparison),
                title: "The order the list is drawn in, against the one it replaced",
                lede: "Nothing is compared until every order has enough whole windows behind it.",
                stamp: Cards.Night(night),
                region: "orders"));
            region.Append(Folded);
        }

        region.Append(Fold("health", "What the night could not do, the overnight queue and the code checks"));
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
        region.Append(Folded);

        region.Append("</section></section>");

        return region.ToString();

        static string Fold(string fold, string summary) =>
            Invariant($"<details class=\"fold\" data-fold=\"{fold}\"><summary>{Escaped(summary)}</summary>");
    }

    // The end of a section the Run page folds shut.
    const string Folded = "</details>";

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

    // Tonight's line for the watch list, which is a page of its own: how many names it holds and a link.
    // see: The watch list is the operator's own, up to twenty names of the index, on a page of its own
    public static string WatchLine(int watching) =>
        watching == 0
            ? Invariant($"<p class=\"watch-line\" data-watching=\"0\">No name is watched yet. <a href=\"{WatchRoute}\">Add names on the watch list</a></p>")
            : Invariant($"<p class=\"watch-line\" data-watching=\"{watching}\">{watching} name(s) watched. <a href=\"{WatchRoute}\">Open the watch list</a></p>");

    // A name page's press to put the name on the watch list or take it off, on the masthead's line after the
    // change on the day and at the small controls' size. A watched name says so beside the press taking it off,
    // so the press never has to be read to learn which of the two the name is.
    // see: The watch list is the operator's own, up to twenty names of the index, on a page of its own
    public static string WatchControl(string ticker, bool watched) =>
        Invariant($"<form class=\"watch-control name-watch\" method=\"post\" action=\"{(watched ? UnwatchPostRoute : WatchPostRoute)}\" data-ticker=\"{Escaped(ticker)}\" data-watched=\"{(watched ? "true" : "false")}\">")
        + (watched
            ? Invariant($"<span class=\"watching\">&#9733; On your watch list</span><button type=\"submit\" aria-label=\"Take {Escaped(ticker)} off the watch list\">Remove</button></form>")
            : Invariant($"<button type=\"submit\" aria-label=\"Add {Escaped(ticker)} to the watch list\">&#9734; Add to watch list</button></form>"));

    // The watch list page: the names the operator follows, each drawn from the night's own rows whether or
    // not the list holds it with what the swing filter said of it, a box to add a name of the index while
    // the list holds fewer than its limit, and a press to take each out.
    // see: The watch list is the operator's own, up to twenty names of the index, on a page of its own
    public string WatchRegion(DateOnly? night, IReadOnlyList<WatchCell> watched, int limit)
    {
        var region = new StringBuilder();
        var shown = night is { } day ? day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "none";

        region.Append(Invariant($"<section class=\"watch\" data-night=\"{shown}\" data-watched=\"{watched.Count}\" data-limit=\"{limit}\">"));
        region.Append(Cards.Masthead("Watch list", "<span class=\"m-screen\">Watch list</span>", Invariant($"{watched.Count} of {limit} names, night of {shown}")));

        var body = new StringBuilder();

        body.Append("<div class=\"watch-said\" role=\"status\"></div>");
        body.Append(watched.Count < limit
            ? Invariant($"<form class=\"watch-control watch-add\" method=\"post\" action=\"{WatchPostRoute}\"><input name=\"ticker\" list=\"findable\" placeholder=\"Add a name from the S&amp;P 500\" aria-label=\"A ticker to watch\" autocomplete=\"off\" spellcheck=\"false\"><button type=\"submit\" class=\"btn\">Add</button></form>")
            : Invariant($"<p class=\"watch-full\" data-full=\"true\">The watch list holds {limit}, its limit; take one out to add another.</p>"));

        if (watched.Count == 0)
        {
            body.Append("<p class=\"degraded\" data-watch=\"none\">No name is watched yet. Add one above, or press Watch on any name's page.</p>");
        }
        else
        {
            body.Append("<div class=\"tbl-wrap\"><table class=\"watch-table\"><thead><tr><th>#</th><th>Name</th><th class=\"r\">Close</th><th class=\"r\">Day</th><th>Trend</th>");
            body.Append("<th class=\"r\">Reward to risk</th><th>The swing filter</th><th>Added</th><th></th></tr></thead><tbody>");

            for (var at = 0; at < watched.Count; at++)
            {
                var one = watched[at];
                var row = one.Row;

                body.Append(Invariant($"<tr data-ticker=\"{Escaped(row.Ticker)}\" data-listed=\"{(one.Listed ? "true" : "false")}\"><td class=\"num\">{at + 1}</td>"));
                body.Append(Invariant($"<td><a href=\"{NameRoute}{Escaped(row.Ticker)}\"><b>{Escaped(row.Ticker)}</b></a>{(one.Company is { Length: > 0 } company ? Invariant($"<span class=\"co\">{Escaped(company)}</span>") : string.Empty)}</td>"));
                body.Append(row.Close is { } close ? Invariant($"<td class=\"r num\">{close:0.00}</td>") : "<td class=\"r\"><span class=\"degraded\">none</span></td>");
                body.Append(row.DayChangePct is { } change ? Invariant($"<td class=\"r num\">{change:+0.00;-0.00;0.00}%</td>") : "<td class=\"r\"><span class=\"degraded\">none</span></td>");
                body.Append(Invariant($"<td>{Escaped(row.TrendState ?? "none")}</td>"));
                body.Append(row.RewardToRisk is { } ratio ? Invariant($"<td class=\"r num\">{ratio:0.00}</td>") : "<td class=\"r\"><span class=\"degraded\">none</span></td>");
                body.Append(Invariant($"<td class=\"{(one.Listed ? "listed" : "stopped")}\" data-filter=\"{Escaped(one.Filter)}\">{Escaped(one.Filter)}</td>"));
                body.Append(Invariant($"<td class=\"num\">{one.Added:yyyy-MM-dd}</td>"));
                body.Append(Invariant($"<td><form class=\"watch-control\" method=\"post\" action=\"{UnwatchPostRoute}\" data-ticker=\"{Escaped(row.Ticker)}\"><button type=\"submit\" class=\"btn-2\" aria-label=\"Stop watching {Escaped(row.Ticker)}\">&#215;</button></form></td></tr>"));
            }

            body.Append("</tbody></table></div>");
        }

        region.Append(Cards.Computed(
            "Watch list",
            body.ToString(),
            title: "Names you follow",
            lede: Invariant($"Drawn every evening whether or not the swing filter lists them, up to {limit} names of the index."),
            region: "watch-list"));
        region.Append("</section>");

        return region.ToString();
    }

    // The account's page: the size of the operator's account, the risk a trade in per cent of it and the position cap,
    // each kept in a file of their own under the data root and in no store, log or export, written whole by the press.
    // A pick's card sizes its plan from them; where they are not set the card draws its plan in prices and risks.
    // see: The account settings live in a file of their own under the data root and in nothing the store or the logs hold
    // Past picks' "Your trades" for the index the page reads: every trade the operator took from that index's cards,
    // open ones first in the order taken and then the ended newest first, each with its card's family and night, its
    // fill, where and why it ended with its result, and the exit press on an open one.
    // see: The operator's own record states its average result once twenty of its trades in a family and index have ended
    // The setup ledger's page under Universe: for the index chosen, each family's setups a year with the share the live
    // rule passes and the share the night's list picked, and the cut points between the deciles of result and edge of
    // its newest year holding settled setups; then one family's newest settled setups and the chosen one's closes with
    // its plan's lines. The page draws the summary the ledger's writers refresh and computes nothing.
    // see: A setup is every member-session a family's loose gates pass, and its readings are defined once and read as they stood
    public string LedgerRegion(
        MarkRenderer marks,
        DateOnly? night,
        UniverseChoice reading,
        string selector,
        IReadOnlyList<EquityBrief.Core.Ledger.LedgerYear> years,
        string? family,
        IReadOnlyList<EquityBrief.Core.Ledger.LedgerSetupRow> settled,
        EquityBrief.Core.Ledger.LedgerPath? path)
    {
        static string Named(string word) => word switch
        {
            "pullback" => "Pullback",
            "breakout" => "Breakout",
            "drift" => "Earnings drift",
            "heavyweight" => "Sector heavyweights",
            _ => word,
        };

        var region = new StringBuilder();
        var heading = "Setup ledger: " + reading.Name;
        var families = years.Select(year => year.Family).Distinct(StringComparer.Ordinal).ToArray();

        region.Append(Invariant($"<section class=\"ledger\" data-universe=\"{reading.Word}\" data-families=\"{families.Length}\" data-family=\"{Escaped(family ?? "none")}\">"));
        region.Append(Cards.Masthead(
            heading,
            $"<span class=\"m-screen\">{Escaped(heading)}</span>",
            night is { } on ? Invariant($"Every near-setup on the {reading.Name}, as of the close of {on:yyyy-MM-dd}") : "No night is stored yet"));
        region.Append(selector);

        if (years.Count == 0)
        {
            region.Append(Cards.Computed(
                "Setup ledger",
                "<p class=\"degraded\" data-ledger=\"none\">The ledger holds no setup on this index yet. Each night appends the setups its families' loose gates pass, and the history build writes the years before the store's own.</p>",
                title: "Every near-setup",
                stamp: Cards.Night(night),
                region: "ledger-none"));
            region.Append("</section>");

            return region.ToString();
        }

        foreach (var group in years.GroupBy(year => year.Family, StringComparer.Ordinal))
        {
            var rows = group.OrderBy(year => year.Year).ToArray();
            var newest = rows.LastOrDefault(year => year.Settled > 0);

            region.Append(Cards.Computed(
                "Setup ledger",
                marks.LedgerYears(group.Key, rows)
                    + Cards.Key(
                        "Setups a year.",
                        "Each row is a year of the family's setups on this index, the member-sessions its loose gates passed, with the share the live rule passes, the share the night's list picked of the rows a night wrote, and the mean result and edge of the settled ones.",
                        "A family whose live rule passes few of its setups is choosing, and its edge against the same plan on every member says whether the choice paid.")
                    + marks.LedgerDeciles(newest)
                    + Cards.Key(
                        "The deciles.",
                        newest is null ? "No year of the family holds a settled setup yet." : Invariant($"The cut points between the tenths of {newest.Year}'s {newest.Settled} settled setups, result beside edge."),
                        "A wide spread between the low and the high tenths is a family whose setups differ a great deal, which is what the loop's engines read."),
                title: Escaped(Named(group.Key)) + " setups",
                stamp: Cards.Night(night),
                region: "ledger-" + group.Key));
        }

        var choose = string.Join(" · ", families.Select(word => word == family
            ? $"<b data-family-chosen=\"{Escaped(word)}\">{Escaped(Named(word))}</b>"
            : $"<a href=\"{LedgerRoute}?universe={reading.Word}&amp;family={Uri.EscapeDataString(word)}\" data-family-link=\"{Escaped(word)}\">{Escaped(Named(word))}</a>"));

        region.Append(Cards.Computed(
            "Setup ledger",
            $"<p class=\"ledger-families\">{choose}</p>"
                + marks.LedgerSettled(settled, reading.Word, LedgerRoute)
                + marks.LedgerPath(path)
                + Cards.Key(
                    "A setup's path.",
                    "The closes from about ten sessions before the setup to the session its path ended, with its buy, its stop and its target drawn across where the plan holds them, and the session it was bought on marked.",
                    "The figures in the table above are this path's end against the same plan on every member that session."),
            title: family is null ? "Settled setups" : Escaped(Named(family)) + ": newest settled setups",
            stamp: Cards.Night(night),
            region: "ledger-path"));
        region.Append("</section>");

        return region.ToString();
    }

    // The families the Loop page draws, in the page's order.
    public static IReadOnlyList<string> LoopFamilies { get; } = ["pullback", "breakout", "drift", "heavyweight"];

    // The Loop page under Universe: for the index chosen and the month its newest tester run is for, each family's rule
    // today in the words the run stored, and each proposal tested against it with its verdict part by part and its test
    // years; the months held for the index are offered above. The page draws the tester's rows and computes nothing.
    // see: A change is adopted only on test years the proposal never saw, and a search is judged as a procedure run year by year
    public string LoopRegion(
        MarkRenderer marks,
        DateOnly? night,
        UniverseChoice reading,
        string selector,
        IReadOnlyList<string> months,
        EquityBrief.Core.Loop.LoopRunRow? run,
        IReadOnlyList<EquityBrief.Core.Loop.LoopProposalRow> proposals,
        IReadOnlyList<EquityBrief.Core.Loop.LoopTestRow> tests,
        IReadOnlyList<EquityBrief.Core.Loop.LoopFindingRow>? findings = null)
    {
        static string Named(string word) => word switch
        {
            "pullback" => "Pullback",
            "breakout" => "Breakout",
            "drift" => "Earnings drift",
            "heavyweight" => "Sector heavyweights",
            _ => word,
        };

        var region = new StringBuilder();
        var heading = "Loop: " + reading.Name;

        region.Append(Invariant($"<section class=\"loop\" data-universe=\"{reading.Word}\" data-month=\"{Escaped(run?.Month ?? "none")}\" data-run=\"{Escaped(run?.RunId ?? "none")}\" data-proposals=\"{proposals.Count}\">"));
        region.Append(Cards.Masthead(
            heading,
            $"<span class=\"m-screen\">{Escaped(heading)}</span>",
            run is { } held ? Invariant($"Proposals tested on years they never saw, the run for {held.Month} through the close of {held.Through:yyyy-MM-dd}") : "No tester run is stored for this index"));
        region.Append(selector);

        if (months.Count > 1)
        {
            region.Append("<p class=\"loop-months\">" + string.Join(" · ", months.Select(month => month == run?.Month
                ? $"<b data-month-chosen=\"{Escaped(month)}\">{Escaped(month)}</b>"
                : $"<a href=\"{LoopRoute}?universe={reading.Word}&amp;month={Uri.EscapeDataString(month)}\" data-month-link=\"{Escaped(month)}\">{Escaped(month)}</a>")) + "</p>");
        }

        if (run is null)
        {
            region.Append(Cards.Computed(
                "Loop",
                "<p class=\"degraded\" data-loop=\"none\">No tester run is stored for this index. The tester runs by hand with 'loop-test' on the S&amp;P 400 and 600, and each month in the monthly run.</p>",
                title: "The loop",
                stamp: Cards.Night(night),
                region: "loop-none"));
            region.Append("</section>");

            return region.ToString();
        }

        foreach (var family in LoopFamilies)
        {
            var own = proposals.Where(proposal => proposal.Family == family).ToArray();
            var body = new StringBuilder(marks.LoopRule(family, own.FirstOrDefault()?.Current));

            body.Append(marks.LoopFindings(family, [.. (findings ?? []).Where(finding => finding.Family == family)]));

            foreach (var proposal in own)
            {
                body.Append(marks.LoopProposal(proposal, [.. tests.Where(test => test.Family == family && test.Proposal == proposal.Proposal)]));
            }

            if (own.Length > 0)
            {
                body.Append(Cards.Key(
                    "Each proposal.",
                    "A procedure is run in each test year on the trades that ended before the year began and scored on that year against the rule today; the four parts must all hold for a proposal to pass, and the setting put forward is the procedure run on all finished data.",
                    "The rule today was chosen on these same test years, so a proposal reads understated against it."));
            }

            region.Append(Cards.Computed(
                "Loop",
                body.ToString(),
                title: Escaped(Named(family)),
                stamp: Cards.Night(night),
                region: "loop-" + family));
        }

        region.Append("</section>");

        return region.ToString();
    }

    public string YourTradesRegion(string indexName, IReadOnlyList<YourTradeView> trades)
    {
        var body = new StringBuilder();

        if (trades.Count == 0)
        {
            body.Append(Invariant($"<p class=\"degraded\" data-trades=\"none\">You have taken no trade from a card on the {Escaped(indexName)}.</p>"));
        }
        else
        {
            body.Append("<div class=\"tbl-wrap\"><table class=\"your-trades\"><tr><th>Stock</th><th>Rule and night</th><th>Fill</th><th>State</th><th>Result</th></tr>");

            foreach (var trade in trades.Where(one => one.Open).Concat(trades.Where(one => !one.Open).OrderByDescending(one => one.EndedOn)))
            {
                var fill = Invariant($"{trade.Fill.ToString("0.00", CultureInfo.InvariantCulture)} for {trade.FillDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}") +
                    (trade.Provisional ? ", provisional" : string.Empty);
                var state = trade.Open
                    ? Invariant($"open<form class=\"card-press\" method=\"post\" action=\"{ExitPostRoute}{Escaped(trade.Ticker)}/{Escaped(trade.TakenAt)}\"><label>Exit price <input name=\"price\" inputmode=\"decimal\" required></label> <label>on <input name=\"date\" type=\"date\" required></label> <button type=\"submit\">Record exit</button></form><p class=\"card-said\" aria-live=\"polite\"></p>")
                    : Invariant($"ended by its {Escaped(trade.EndReason ?? "exit")} on {trade.EndedOn?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)} at {trade.EndPrice?.ToString("0.00", CultureInfo.InvariantCulture)}");
                var result = trade.Result is { } read
                    ? read.ToString(trade.Unit == "percent" ? "0.00'%'" : "0.00' risks'", CultureInfo.InvariantCulture)
                    : "not read";

                body.Append(Invariant($"<tr data-ticker=\"{Escaped(trade.Ticker)}\" data-open=\"{(trade.Open ? "yes" : "no")}\"><td>{Escaped(trade.Ticker)}</td><td>{Escaped(trade.Rule)}, {trade.Night.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}</td><td>{fill}</td><td>{state}</td><td>{result}</td></tr>"));
            }

            body.Append("</table></div>");
        }

        return Cards.Computed(
            "Your trades",
            body.ToString(),
            title: Invariant($"Your trades on the {Escaped(indexName)}"),
            lede: "The trades you took from a pick's card, followed each night under the rule's own management. Nothing that picks a stock reads them.",
            region: "your-trades");
    }

    public string AccountRegion(decimal? size, decimal? riskPercent, decimal? positionCap, decimal proposedCap)
    {
        var region = new StringBuilder();
        var set = size is not null;

        region.Append(Invariant($"<section class=\"account\" data-set=\"{(set ? "true" : "false")}\">"));
        region.Append(Cards.Masthead("Account", "<span class=\"m-screen\">Account</span>", set ? "Your plan is sized from these" : "Not set"));

        var body = new StringBuilder();

        body.Append("<div class=\"account-said\" role=\"status\"></div>");
        body.Append(Invariant($"<form class=\"account-control\" method=\"post\" action=\"{AccountPostRoute}\">"));
        body.Append(Invariant($"<label>Account size <input name=\"size\" inputmode=\"decimal\" required value=\"{(size is { } held ? held.ToString(CultureInfo.InvariantCulture) : string.Empty)}\"></label> "));
        body.Append(Invariant($"<label>Risk a trade, per cent <input name=\"risk\" inputmode=\"decimal\" required value=\"{(riskPercent is { } risk ? risk.ToString(CultureInfo.InvariantCulture) : string.Empty)}\"></label> "));
        body.Append(Invariant($"<label>Position cap, share of the account <input name=\"cap\" inputmode=\"decimal\" required value=\"{(positionCap ?? proposedCap).ToString(CultureInfo.InvariantCulture)}\"></label> "));
        body.Append("<button type=\"submit\" class=\"btn\">Save</button></form>");
        body.Append("<p class=\"degraded\">Kept on this machine in a file of their own under the data folder, never in the store, a log or an exported report.</p>");

        region.Append(Cards.Computed(
            "Account",
            body.ToString(),
            title: "Your account",
            lede: "A pick's card sizes its plan from these: the shares the risk a trade buys over the stop's distance, no more than the cap allows.",
            region: "account"));
        region.Append("</section>");

        return region.ToString();
    }

    // The Past picks screen, section 15.17: every trade the live list recommended from the swing filter's
    // first night, counted and then drawn one to a row, newest first, under the status filter the hash
    // carries. Only the live list's trades appear, each on the plan its night's rule traded, and the
    // share that reached its target waits for the minimum the run page's records wait for.
    // see: Every trade the live list recommended is shown, and their share waits for the minimum the reason records wait for
    // The counts and the rows are those of the setup the hash names, where it names one, and of every setup
    // where it names none; the setups are the ones the page has listed a trade under, each with its trades.
    public string PicksRegion(MarkRenderer marks, DateOnly? night, PicksSummary summary, IReadOnlyList<PickCell> shown, string? status, string? setup = null, IReadOnlyList<(string Family, string Label, int Trades)>? setups = null, IReadOnlyList<HeavyweightPickCell>? heavyweights = null, string selector = "")
    {
        var region = new StringBuilder();
        var drawn = night is { } day ? day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "none";
        var lit = status is { } asked && PickStatus.Filters.Contains(asked, StringComparer.Ordinal) ? asked : "all";
        var heading = "Past picks: " + Universes.Large.Name;

        region.Append(Invariant($"<section class=\"picks\" data-night=\"{drawn}\" data-universe=\"{Universes.Large.Word}\" data-trades=\"{summary.Listed}\" data-shown=\"{shown.Count}\" data-status=\"{lit}\">"));
        region.Append(Cards.Masthead(
            heading,
            $"<span class=\"m-screen\">{Escaped(heading)}</span>",
            night is null ? "No night is stored yet" : Invariant($"Every trade the {Universes.Large.Possessive} lists recommended, as of the close of {drawn}")));
        region.Append(selector);

        if (summary.Listed == 0)
        {
            region.Append(Cards.Computed(
                "Past picks",
                "<p class=\"degraded\" data-picks=\"none\">No trade has been listed yet. The live list recommends a trade on each night the swing filter passes a name, and every one it recommends is followed here from that night on.</p>",
                title: "How the list's picks have done",
                stamp: Cards.Night(night),
                region: "picks-summary"));
            region.Append(HeavyweightPicksCard(marks, night, heavyweights));
            region.Append("</section>");

            return region.ToString();
        }

        region.Append(Cards.Computed(
            "Past picks",
            marks.PicksCounts(summary) + Cards.Key(
                "How to read it.",
                Invariant($"A trade is listed on the night the live list drew it and stays open until a close reaches its target, a close falls through its stop, or {EquityBrief.Core.Returns.ForwardReturnSeries.SetupSessionCap} sessions pass. A result is what the trade made in multiples of what it risked, so -1 is a full stop-out and +2 a target twice as far away as the stop."),
                "Each trade is a fact and is always shown. A share over a handful of trades looks like evidence and is not, so it waits for the minimums, and it never appears without the share the trades needed to break even beside it."),
            title: "How the list's picks have done",
            lede: Invariant($"Every name the live list recommended from the swing filter's first night, each followed from the close it was listed at to its target, its stop, or the end of its {EquityBrief.Core.Returns.ForwardReturnSeries.SetupSessionCap} sessions."),
            stamp: Cards.Night(night),
            region: "picks-summary"));

        var body = new StringBuilder();

        body.Append(marks.PicksFilters(summary, status, setup, setups));
        // The rows shown of the rows there are: the trades, and the listings that repeated a trade still open,
        // drawn with their mark and counted as no trade.
        // see: A repeat listing made before the rule reached the filter is marked and counted once
        body.Append(summary.Repeats == 0
            ? Invariant($"<p class=\"list-count\" data-shown=\"{shown.Count}\" data-trades=\"{summary.Listed}\">Showing {shown.Count} of {summary.Listed} trade{(summary.Listed == 1 ? string.Empty : "s")}</p>")
            : Invariant($"<p class=\"list-count\" data-shown=\"{shown.Count}\" data-trades=\"{summary.Listed}\" data-repeats=\"{summary.Repeats}\">Showing {shown.Count} of {summary.Listed + summary.Repeats} rows: {summary.Listed} trade{(summary.Listed == 1 ? string.Empty : "s")} and {summary.Repeats} listed again while an earlier trade was open</p>"));
        // The trades a setup on provisional settings listed are followed like any other and are in no share.
        // see: A family lists on provisional settings until its freeze, and nothing before the freeze counts toward a checkpoint
        if (summary.Provisional > 0)
        {
            body.Append(Invariant($"<p class=\"provisional-count\" data-provisional=\"{summary.Provisional}\">{summary.Provisional} of them {(summary.Provisional == 1 ? "was" : "were")} listed by a setup not yet frozen: followed like any other, and in no share and no average until its freeze.</p>"));
        }

        body.Append(shown.Count == 0
            ? "<p class=\"degraded\" data-shown=\"none\">No trade stands in this status yet.</p>"
            : marks.PicksTable(shown, named: true));
        body.Append(Cards.Key(
            "How to read the trade line.",
            "The line runs from the stop on the left, in green, to the target on the right, in orange, with the buy marked between them. The dot is where the price is now, hollow while the trade is open and filled where it finished, and a price past either end sits at that end. A setup that trails its stop names no target, so its row draws no line and its result is what it made in multiples of what it risked.",
            "Only the live list's trades appear here, each under the setup that listed it. The alternatives being tested in the background stay hidden until one of them is promoted."));

        region.Append(Cards.Computed(
            "Past picks",
            body.ToString(),
            title: "Every trade, newest first",
            lede: "Filters live in the address, so a filtered view is a link you can keep.",
            stamp: Cards.Night(night),
            region: "picks"));
        region.Append(HeavyweightPicksCard(marks, night, heavyweights));
        region.Append("</section>");

        return region.ToString();
    }

    // The Run page for the S&P 400 or the S&P 600: how the index's own night went, read off the rows its families
    // stored, and its setups with what each listed and how their trades stand. The night's steps are one list for every
    // index and are drawn under the S&P 500, which the page links to.
    // see: Every page reads one index at a time chosen under Universe, and every figure names its index
    public string IndexRunRegion(
        MarkRenderer marks,
        DateOnly night,
        UniverseChoice universe,
        string selector,
        IReadOnlyList<DateOnly> held,
        IndexRunView? view,
        IReadOnlyList<FamilyRunRow> setups,
        IReadOnlyList<FamilyRecordRow>? records = null,
        IReadOnlyList<(string Family, string Rule, int Stretch, int Mark)>? pastTheirMark = null)
    {
        var region = new StringBuilder();
        var heading = "Run evidence: " + universe.Name;
        var query = "?" + Universes.Query + "=" + universe.Word;

        region.Append(Invariant($"<section class=\"run\" data-night=\"{night:yyyy-MM-dd}\" data-universe=\"{universe.Word}\" data-stages=\"none\">"));
        region.Append(Cards.Masthead(
            heading,
            $"<span class=\"m-screen\">{Escaped(heading)}</span>",
            Invariant($"Night of {night:yyyy-MM-dd}") + Cards.NightPicker(night, held, RunRoute, RunRoute + query, query)));
        region.Append(selector);

        var body = new StringBuilder();

        if (view is null)
        {
            body.Append(Invariant($"<p class=\"degraded\" data-index-night=\"none\">The {Escaped(universe.Possessive)} families read nothing for {night:yyyy-MM-dd}: no night of theirs is stored for it.</p>"));
        }
        else if (view.Fault is { } fault)
        {
            body.Append(Invariant($"<p class=\"degraded\" data-index-night=\"not-computed\">Not computed tonight: the {Escaped(universe.Possessive)} part of the night of {night:yyyy-MM-dd} failed on {Escaped(fault)}, and the S&amp;P 500's night was built regardless.</p>"));
        }
        else
        {
            body.Append(Invariant($"<ul class=\"index-night\" data-members=\"{view.Members}\" data-passed=\"{view.Passed}\" data-listed=\"{view.Listed}\" data-held-back=\"{view.HeldBack}\" data-kept=\"{view.Kept}\" data-ended=\"{view.Ended}\" data-holdings=\"{view.Holdings}\" data-rebalanced=\"{(view.Rebalanced ? "true" : "false")}\">"));
            body.Append(Invariant($"<li>{view.Members} {Escaped(universe.Name)} members read on {night:yyyy-MM-dd}</li>"));
            body.Append(view.Breadth is { } breadth
                ? Invariant($"<li>the {Escaped(universe.Possessive)} breadth: {breadth * 100:0.0}% of its members above their 200-day average, {(view.MarketOpen ? "at or above" : "below")} its floor of {view.Floor * 100:0.#}%, so its swing lists were {(view.MarketOpen ? "open" : "closed")}</li>")
                : Invariant($"<li>the {Escaped(universe.Possessive)} breadth was not available, so its swing lists were {(view.MarketOpen ? "open" : "closed")}</li>"));
            body.Append(Invariant($"<li>{view.Passed} of {view.Members} {Escaped(universe.Name)} members passed a setup, {view.Listed} listed and {view.HeldBack} held back by a trade still open on any index's list</li>"));
            body.Append(Invariant($"<li>{view.Kept} {Escaped(universe.Name)} trade(s) kept tonight and {view.Ended} ended</li>"));
            body.Append(Invariant($"<li>the {Escaped(universe.Possessive)} sector heavyweights {(view.Rebalanced ? "rebalanced" : "carried their holdings")} tonight and hold {view.Holdings}</li>"));
            body.Append("</ul>");
        }

        body.Append(Invariant($"<p class=\"oneline\">The night's steps are one list for all three indices: <a href=\"{RunRoute}{night:yyyy-MM-dd}\">see them under the S&amp;P 500</a>.</p>"));

        region.Append(Cards.Computed(
            "Last night",
            body.ToString(),
            title: Invariant($"How the {universe.Possessive} night went"),
            lede: Invariant($"Read off the rows the {universe.Possessive} families stored for the night, on its own members alone."),
            stamp: Cards.Night(night),
            region: "night"));

        // The rule drawing a setup's list past the mark its own past empty nights set, in one line, or that none is.
        // see: A card's stretch line counts its mark over past empty nights and draws none under thirty completed stretches
        var stretches = pastTheirMark is null
            ? string.Empty
            : pastTheirMark.Count == 0
                ? "<p class=\"stretch-worry\" data-past-mark=\"0\">No setup's rule has gone longer without a pick than its own history says it does.</p>"
                : Invariant($"<p class=\"stretch-worry flagged\" data-past-mark=\"{pastTheirMark.Count}\">") + Escaped(string.Join("; ", pastTheirMark.Select(rule => Invariant($"the {rule.Family}'s rule has listed nothing for {rule.Stretch} nights, past its mark of {rule.Mark}")))) + "</p>";

        region.Append(Cards.Computed(
            "Setups",
            stretches + marks.FamilyRun(setups) + Cards.Key(
                "How to read it.",
                Invariant($"One row a setup of the {universe.Possessive} page, each on provisional settings until its freeze: what it listed tonight and every trade its list has kept, open and finished."),
                "A provisional rule's record starts at its freeze, so no record is read here until the operator freezes it."),
            title: Invariant($"The {universe.Possessive} setups"),
            lede: "Each a rule of its own on the index's own members, listing at most five a night.",
            stamp: Cards.Night(night),
            region: "setups"));

        // Each frozen family's registered rules, the live rule first, each over its own trades after each one's round trip.
        // see: A rule of the S&P 400's or 600's swing families is registered as the family on its index and evaluated by their step alone
        if (records is { Count: > 0 })
        {
            region.Append(Cards.Computed(
                "Records",
                marks.FamilyRecords(records) + Cards.Key(
                    "How to read it.",
                    Invariant($"One row a rule a freeze registered on the {universe.Name}, each family's live rule first: the trades its own list kept, those decided with their edge, being each trade's result less its own round trip at the published table less the same plan on every member of the index that night, in multiples of the risk, its whole blocks of 63 sessions against the look they wait for, and the level its looks are read at, its family's own share over its own rules on the index."),
                    "A rule passes a checkpoint only where its blocks' sign-flip test falls under its level at a look."),
                title: Invariant($"The {universe.Possessive} registered rules"),
                lede: "Each frozen family's rules, read over their own trades.",
                stamp: Cards.Night(night),
                region: "records"));
        }

        region.Append("</section>");

        return region.ToString();
    }

    // Past picks for the S&P 400 or the S&P 600: every trade its lists kept, newest first, each with its result before
    // and after its cost, and its sector heavyweights' holdings, each named as the index's. Every rule of the index is
    // provisional, so no share or average is drawn: each trade is followed and counts toward nothing until a freeze.
    // see: Every page reads one index at a time chosen under Universe, and every figure names its index
    public string IndexPicksRegion(MarkRenderer marks, DateOnly? night, UniverseChoice universe, string selector, IReadOnlyList<IndexTradeCell> trades, IReadOnlyList<HeavyweightPickCell> holdings)
    {
        var region = new StringBuilder();
        var drawn = night is { } day ? day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "none";
        var heading = "Past picks: " + universe.Name;
        var open = trades.Count(trade => trade.EndedOn is null);

        region.Append(Invariant($"<section class=\"picks\" data-night=\"{drawn}\" data-universe=\"{universe.Word}\" data-trades=\"{trades.Count}\" data-shown=\"{trades.Count}\" data-status=\"all\">"));
        region.Append(Cards.Masthead(
            heading,
            $"<span class=\"m-screen\">{Escaped(heading)}</span>",
            night is null ? "No night is stored yet" : Invariant($"Every trade the {universe.Possessive} lists kept, as of the close of {drawn}")));
        region.Append(selector);

        var body = new StringBuilder();

        body.Append(Invariant($"<p class=\"list-count\" data-shown=\"{trades.Count}\" data-trades=\"{trades.Count}\" data-open=\"{open}\">Showing {trades.Count} of {trades.Count} {Escaped(universe.Name)} trade{(trades.Count == 1 ? string.Empty : "s")}, {open} open</p>"));
        body.Append(trades.Count == 0
            ? Invariant($"<p class=\"degraded\" data-picks=\"none\">No trade has been listed on the {Escaped(universe.Possessive)} lists yet. Each night its families pass a stock, the first five a setup are listed and followed here from that close on.</p>")
            : marks.IndexTrades(trades, universe.Name));
        body.Append(Cards.Key(
            "How to read it.",
            "A trade is listed on the night its list drew it and stays open until a close reaches its target, a close falls through its stop or trailing stop, or its sessions run out. Its result is what it made in multiples of what it risked; its cost is its round trip at the published spread for its size and price, in the same units, and the result after cost is what the index's tests read.",
            "Every rule of this index is provisional, so no share and no average is drawn: each trade is a fact and is followed, and none counts toward a rule's record until its freeze."));

        region.Append(Cards.Computed(
            "Past picks",
            body.ToString(),
            title: Invariant($"Every {universe.Name} trade, newest first"),
            lede: Invariant($"Each listed by a setup on the {universe.Possessive} provisional settings, named with its index."),
            stamp: Cards.Night(night),
            region: "picks"));

        if (holdings.Count > 0)
        {
            var ended = holdings.Count(holding => holding.Sold is not null);

            region.Append(Cards.Computed(
                "Past picks",
                Invariant($"<p class=\"list-count\" data-holdings=\"{holdings.Count}\" data-ended=\"{ended}\">{holdings.Count} {Escaped(universe.Name)} holding{(holdings.Count == 1 ? string.Empty : "s")}, {ended} sold and {holdings.Count - ended} held</p>")
                    + marks.HeavyweightPicks(holdings)
                    + Cards.Key(
                        "How to read it.",
                        $"A holding is bought at the close of a month's rebalance, its first session or the first after it whose stored year holds the closes the readings need, and sold at the close of a later month's rebalance where it no longer leads its sector among the {Escaped(universe.Possessive)} members, or at its last close as a member. Its result is what it made in percent beside what its sector's largest members made over the same sessions.",
                        "Provisional like every rule of this index: followed, and in no record until its freeze."),
                title: Invariant($"The {universe.Possessive} sector heavyweights, newest first"),
                lede: "Held while leading: a result in percent rather than in multiples of a risk, since a holding has no stop.",
                stamp: Cards.Night(night),
                region: "heavyweight-picks"));
        }

        region.Append("</section>");

        return region.ToString();
    }

    // Past picks' card of the sector heavyweights' holdings, each in percent beside its sector's largest companies
    // over the same sessions, and nothing where the book has bought nothing.
    // see: A sector heavyweight's trade is scored by its percent return less the equal-weighted return of the size cut it was chosen from
    // see: A family lists on provisional settings until its freeze, and nothing before the freeze counts toward a checkpoint
    static string HeavyweightPicksCard(MarkRenderer marks, DateOnly? night, IReadOnlyList<HeavyweightPickCell>? holdings)
    {
        if (holdings is not { Count: > 0 })
        {
            return string.Empty;
        }

        var ended = holdings.Count(holding => holding.Sold is not null);

        return Cards.Computed(
            "Past picks",
            Invariant($"<p class=\"list-count\" data-holdings=\"{holdings.Count}\" data-ended=\"{ended}\">{holdings.Count} holding{(holdings.Count == 1 ? string.Empty : "s")}, {ended} sold and {holdings.Count - ended} held</p>")
                + "<p class=\"provisional-count heavyweight-book\">The page's own book, at the setting the family froze at: each registered rule's record is read off a book of its own, on the run page.</p>"
                + marks.HeavyweightPicks(holdings)
                + Cards.Key(
                    "How to read it.",
                    "A holding is bought at the close of a month's rebalance, its first session or the first after it whose stored year holds the closes the readings need, and sold at the close of a later month's rebalance where it no longer leads its sector, or at its last close as a member of the index. Its result is what it made from its buy to its sale in percent, dividends counted, beside what the sector's largest companies it was chosen from made over the same sessions, each in equal part.",
                    "The difference is what leading its sector was worth over being merely large, which is what each rule's record asks; a holding still held has neither yet."),
            title: "Sector heavyweights, newest first",
            lede: "Held while leading: a result in percent rather than in multiples of a risk, since a holding has no stop.",
            stamp: Cards.Night(night),
            region: "heavyweight-picks");
    }

    // An evening before the swing filter's first night, which neither dated screen draws: the record
    // starts on the first night the filter listed, and the line says so and opens it. The screen's own
    // calendar sits in its header, over the nights from that one on.
    // see: The dated screens open from the swing filter's first night, and no evening before it is drawn
    public static string BeforeTheRecord(DateOnly asked, DateOnly first, string title, string route, string newest, IReadOnlyList<DateOnly> held) =>
        Invariant($"<section class=\"before-the-record\" data-night=\"{asked:yyyy-MM-dd}\" data-first=\"{first:yyyy-MM-dd}\">")
        + Cards.Masthead(title, Invariant($"<span class=\"m-screen\">{Escaped(title)}</span>"), Invariant($"The record starts on {first:yyyy-MM-dd}") + Cards.NightPicker(first, held, route, newest))
        + Invariant($"<p class=\"banner\" role=\"status\">The record starts on {first:yyyy-MM-dd}, the swing filter's first night, so {asked:yyyy-MM-dd} is not drawn. <a href=\"{route}{first:yyyy-MM-dd}\">Open {first:yyyy-MM-dd}</a></p>")
        + "</section>";

    // A name asked for on something that is not a date: tonight's page with a line saying
    // what was asked for, as an unknown route is tonight's list with one.
    // see: A name's page for an earlier night draws what the store held that night and nothing it learned after
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
