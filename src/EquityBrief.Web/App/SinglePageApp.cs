using System.Globalization;
using System.Text;
using EquityBrief.Core.Components;
using EquityBrief.Core.Filter;
using EquityBrief.Core.Shortlist;
using EquityBrief.Core.Time;
using EquityBrief.Web.Marks;

namespace EquityBrief.Web.App;

// What a name's masthead states beside its ticker and last close: the company, its sector
// and industry as the membership row holds them, and the change on the day, which the
// projection reads off the two newest stored closes.
public sealed record NameMast(string? Company, string? Sector, string? Industry, double? DayChangePct, string? Index = null);

// One band's distance from a delayed quote's price in typical days' moves, as the quote job stored it: none inside the
// band.
public sealed record QuoteDistance(decimal Low, decimal High, string Role, double? Days);

// The delayed quote the quote job stored for a name: the instant it was asked, its own time, the price, the previous close
// and the change on it, the stored night its bands are from, and each band's distance at the price.
public sealed record LiveQuoteView(
    DateTimeOffset AskedAt,
    DateTimeOffset QuotedAt,
    decimal Price,
    decimal? PreviousClose,
    decimal? Change,
    double? ChangePercent,
    DateOnly? Night,
    IReadOnlyList<QuoteDistance> Distances);

// The session the masthead's quote is asked in: its open and close, and whether the page is drawn inside it.
public sealed record QuoteSession(DateTimeOffset Open, DateTimeOffset Close, bool IsOpen);

// The one sentence under the masthead, carrying no figure, whether a model wrote it or code did, which it says, and the
// day a written one was written.
public sealed record HeadlineView(string Sentence, bool Written, DateOnly? WrittenOn = null);

// The four tiles under the headline, worked at the price the masthead draws, and whether that price is the quote.
public sealed record TilesView(
    EquityBrief.Core.Tiles.EarningsTile? Earnings,
    EquityBrief.Core.Tiles.GrowthTile? Growth,
    EquityBrief.Core.Tiles.YieldTile? Yield,
    EquityBrief.Core.Tiles.HighTile? High,
    decimal? Price,
    bool Live);

// The quotes stored in one session and the quote job's runs over it by what each came to, against the day's cap, with
// how many asks the name pages wrote in it and each ask no run of the job answered: the Run page's row for the quote runs.
public sealed record QuoteRunsView(
    DateTimeOffset From,
    DateTimeOffset To,
    IReadOnlyList<(string Ticker, DateTimeOffset AskedAt, double DelayMinutes)> Asked,
    IReadOnlyList<(string Outcome, int Runs)> Outcomes,
    int Cap,
    int Asks = 0,
    IReadOnlyList<(string Ticker, DateTimeOffset AskedAt)>? Unanswered = null)
{
    // The word a run that stored a quote writes. The worker's own constant cannot be referenced from here, so it is
    // stated and `read-surface` asserts the two agree.
    public const string Quoted = "quoted";
}

// A current member as the masthead's search offers it: its ticker, its company's name, and the
// day its newest researched section was written, null where it holds none.
public sealed record Findable(string Ticker, string? Name, DateOnly? Researched);

// One trade the operator took, as Past picks' "Your trades" draws it.
public sealed record YourTradeView(string Ticker, string TakenAt, string Rule, DateOnly Night, decimal Fill, DateOnly FillDate, bool Provisional, bool Open, DateOnly? EndedOn, string? EndReason, decimal? EndPrice, double? Result, string Unit);

// A name holding researched sections, as the researched list draws it.
public sealed record ResearchedCell(string Ticker, string? Name, string? Sector, DateOnly Written, int Sections, bool? Stale = null, IReadOnlyList<string>? StaleLines = null, double? Move = null, int Of = 0, string? Why = null);

// One request, as the queue screen draws it. The instant is carried as the store
// spells it, because a press to take one out names the request rather than the
// name: a name may have been asked for before and settled since. Beside it, the
// instant as the read surface marked it for the page, where it did.
public sealed record QueuedCell(
    string Ticker,
    string AskedAt,
    string AskedFrom,
    string Lane,
    string State,
    string? SettledAt,
    string? RunId,
    string? Reason,
    QueuedTime? Time = null,
    string? Asked = null);

// When one request's pass will start or started and will end or ended, as the read surface
// worked it out: what it rests on, the two instants in UTC as the store spells an instant,
// and the words the page states them in, as markup with each instant marked.
public sealed record QueuedTime(string Basis, string? Starts, string? Ends, string Words);

// What the queue page's times rest on: how many passes that ran to their end the store
// holds, and their median in whole minutes where it holds any.
public sealed record QueueEstimate(int Passes, string? Minutes, string? LongestMinutes = null);

// The research model at its peak price as the queue page is drawn: the window's end as the read surface
// marked it, and the price's multiple in words, which the Write now press states before it asks.
public sealed record QueuePeak(string Ends, string Multiple);

// A drain that stopped on an error and no pass has started since: the instant it started as the store spells one,
// that instant as the read surface marked it for the page, and the error it stopped on.
public sealed record QueueStop(string StartedAt, string Words, string Error);

// The shell the browser loads once, and the routes it answers.
//
// Section 15.4 puts the shell and the marks on the server and the routing in
// the page. So this writes the shell and nothing else: the marks arrive already
// drawn, and the only thing the browser does is ask for the one belonging to
// the route it is on. A page that assembled a mark from values would be the
// second renderer the marks decision exists to prevent.
// see: Marks are defined once and every screen draws from that list
// see: A screen reads and renders, and each figure it works out at a price or from stored figures has one function in the core
//
// At 1.3 it answered one route drawing a name's candles. At 4.1 that route asks
// for the name screen's chart region, which the server composes from the marks
// section 15.9 lists, because the profile is drawn against the chart's own price
// axis and a second request would be a second axis. The other four screens
// arrive with the data behind them, and this file is where they are added.
public sealed partial class SinglePageApp : IComponent
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

    // The Loop page's presses, approve or decline of a proposal and approve of a restore, each refused without the page's
    // own header as every press is.
    // see: An approved change is applied by the night's own build on the index it was approved on alone
    public const string LoopDecidePostRoute = MarkRenderer.LoopDecideRoute;
    public const string LoopRestorePostRoute = MarkRenderer.LoopRestoreRoute;

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

    // Paper trading, in the account page's place in the menu, the account page's old address still opening it.
    public const string PaperRoute = "#/paper";

    // The count the Loop's badge draws, the proposals put to the operator that hold no decision.
    public const string LoopPutRoute = "/screens/loop/put";
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

    // Where the queue page's Write now press starts one outstanding report at once, and the word the press
    // carries once the operator confirmed the peak price.
    // see: A press writes a request and starts the worker's drain as a process of its own, and a pass waits for the off-peak hours unless the operator writes it now
    public const string WriteNowRoute = "/passes/now/";

    public const string WriteNowAtPeak = "peak";

    // Where a name page asks for its delayed quote, carrying the page's own header, and reads back the newest one stored.
    // see: The name page draws a delayed quote in the regular session, asked by a worker job at most every five minutes under a day's cap
    public const string QuoteRoute = "/quotes/";

    // The price the masthead draws: the delayed quote marked as such with its change on the previous close in the session,
    // and outside it the last stored close with its change on the day.
    public static string PriceNow(IReadOnlyList<ChartBar> bars, NameMast? mast, LiveQuoteView? quote)
    {
        if (quote is { } quoted)
        {
            var sign = quoted.Change is { } moved ? Math.Sign(moved) : 0;
            var hue = sign > 0 ? " gain" : sign < 0 ? " loss" : string.Empty;
            var change = quoted.Change is { } amount
                ? Invariant($"<span class=\"m-chg{hue}\" data-change=\"{amount.ToString(CultureInfo.InvariantCulture)}\">{amount.ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture)}{(quoted.ChangePercent is { } percent ? Invariant($" ({percent.ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture)}%)") : string.Empty)}</span>")
                : string.Empty;

            return Invariant($"<span class=\"pill live\">LIVE</span><span class=\"m-px\" data-price=\"{quoted.Price.ToString(CultureInfo.InvariantCulture)}\">{Figures.Price(quoted.Price)}</span>{change}");
        }

        return (bars.Count > 0 ? Invariant($"<span class=\"m-px\">{bars[^1].Close.ToString(CultureInfo.InvariantCulture)}</span>") : string.Empty)
            + (mast?.DayChangePct is { } day
                ? Invariant($"<span class=\"m-chg {MarkRenderer.Hue(day)}\">{(Math.Round(day, 2) == 0 ? 0 : day).ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture)}% on the day</span>")
                : string.Empty);
    }

    // The line under a quote: its own time in New York, that it is delayed, the previous close, and the session the levels
    // and the plan are read through.
    public static string QuoteAsOf(LiveQuoteView quote, DateOnly? levelsThrough) =>
        Invariant($"Quote of {TimeZoneInfo.ConvertTime(quote.QuotedAt, NewYork).ToString("HH:mm", CultureInfo.InvariantCulture)} ET, delayed about 15 minutes")
        + (quote.PreviousClose is { } previous ? Invariant($". Previous close <span data-previous-close=\"{previous.ToString(CultureInfo.InvariantCulture)}\">{Figures.Price(previous)}</span>") : string.Empty)
        + (levelsThrough is { } through ? Invariant($". Levels and the plan come from completed sessions to {through:yyyy-MM-dd}") : string.Empty);

    static readonly TimeZoneInfo NewYork = EquityBrief.Core.Time.SessionZones.ResolveSessionZone(EquityBrief.Core.Time.SessionZones.UnitedStates);
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
        night.State is NightStates.NoSession or NightStates.NotYet
            ? string.Empty
            : "<section class=\"night-notice-box\">" + marks.NightNotice(night) + NightPress(night) + "</section>";

    // Tonight's top on a night the families drew the page's list: a tile a setup in the one family order with tonight's
    // count and its state, and one table of tonight's buy points across every setup, each family's picks in its own order.
    // see: Each setup family has a hue of its own drawn as a mark and a tint beside its word
    public static (IReadOnlyList<SetupTileView> Tiles, IReadOnlyList<BuyPointView> Points) TopOf(
        IReadOnlyList<FamilyCardView> families,
        HeavyweightCardView? heavyweights,
        bool marketOpen,
        DateOnly night)
    {
        var tiles = new List<SetupTileView>();
        var points = new List<BuyPointView>();

        static string HeldBack(int held) => Invariant($"{held} held back by a trade still open");

        foreach (var card in families.OrderBy(card => EquityBrief.Core.Families.SetupFamilies.OrderOf(card.Family)))
        {
            var forming = card.RuleChoice?.Forming?.Forming ?? 0;
            var state = card.Picks.Count > 0
                ? card.HeldBack > 0 ? HeldBack(card.HeldBack) : "listing tonight"
                : !marketOpen ? "the market check closed"
                : card.HeldBack > 0 ? HeldBack(card.HeldBack)
                : forming > 0 ? Invariant($"{forming} forming")
                : "nothing tonight";

            tiles.Add(new SetupTileView(card.Family, card.Label, card.Picks.Count, card.Picks.Count == 1 ? "pick" : "picks", state));
            points.AddRange(card.Picks.Select(pick => new BuyPointView(card.Family, card.Label, pick)));
        }

        if (heavyweights is not null)
        {
            var until = heavyweights.NextRebalance is { } next ? EquityBrief.Core.Bars.ExchangeClosures.SessionsUntil(night, next) : null;

            tiles.Add(new SetupTileView(
                EquityBrief.Core.Families.HeavyweightRule.Name,
                "Sector heavyweights",
                heavyweights.Holdings.Count,
                heavyweights.Holdings.Count == 1 ? "holding" : "holdings",
                heavyweights.LastRebalance == night ? "rebalanced tonight"
                    : until is { } sessions ? Invariant($"rebalance in {sessions} {(sessions == 1 ? "session" : "sessions")}")
                    : "its next rebalance not read"));
        }

        return (tiles, points);
    }

    // A setup's card under its place and words, or, where it lists nothing and no variant is chosen, folded to its one
    // line with no heading of its own.
    // see: Tonight's page is drawn from setup families, each a rule of its own listing at most five a night
    static string SetupCard(MarkRenderer marks, FamilyCardView card, DateOnly night) =>
        MarkRenderer.Folded(card)
            ? Invariant($"<section class=\"card folded\" id=\"setup-{Escaped(card.Family)}\" data-card=\"family\"><div class=\"card-b\">{marks.FamilyCard(card)}</div></section>")
            : Cards.Computed(
                Invariant($"Setup {card.Place} of {card.Of} · {card.Eyebrow}"),
                marks.FamilyCard(card),
                stamp: Cards.Night(night),
                id: "setup-" + card.Family,
                region: "family");

    // A card of one line with its reading guide behind its "?".
    static string LineCard(string region, string line, string guide) =>
        Invariant($"<section class=\"card folded\" data-card=\"{Escaped(region)}\"><div class=\"card-b\"><div class=\"card-one-line\">{line}<details class=\"guide\"><summary title=\"How to read this\" aria-label=\"How to read this\">?</summary><p class=\"guide-b\">{Escaped(guide)}</p></details></div></div></section>");

    // How tonight's buy points are read, written once for the table and for the line drawn in its place.
    public const string BuyPointsGuide =
        "Each row is one stock a setup lists tonight, tagged with its setup in the setup's hue, its plan set at the evening's close and bought at the next session's open. The stop is the price that says the plan was wrong and the target the price that says it was right. Select a row to draw its plan and its card just beneath it. A stock is listed once, under the first setup it qualified for in the page's order, and never while a trade for it is still open. The reward to risk is a fact about the chart and not a chance of anything.";

    // Tonight's buy points as a card of their own, the table across every setup with its key; on a night no setup lists
    // one, a line saying so with how many members stand one gate short, from 19.8 a link opening the card of them, its
    // guide behind its "?".
    static string BuyPointsCard(MarkRenderer marks, IReadOnlyList<BuyPointView> points, string? selected, string plan, DateOnly night, string query = "", int? oneShort = null, string? closeHref = null) =>
        points.Count == 0
            ? LineCard(
                "buy-points",
                Invariant($"<p class=\"buy-points-none\" data-buy-points=\"0\" data-one-short=\"{(oneShort is { } stated ? stated.ToString(CultureInfo.InvariantCulture) : "none")}\">No buy points tonight{(oneShort is not { } shortOf ? string.Empty : closeHref is { } href ? Invariant($" · <a class=\"close-open\" href=\"{Escaped(href)}\">{shortOf} one gate short</a>") : Invariant($" · {shortOf} one gate short"))}</p>"),
                BuyPointsGuide)
            : Cards.Computed(
                "Buy points",
                marks.BuyPoints(points, selected, plan, query) + Cards.Key(
                    "How to read the table.",
                    "Each row is one stock a setup lists tonight, tagged with its setup in the setup's hue, its plan set at the evening's close and bought at the next session's open. The stop is the price that says the plan was wrong and the target the price that says it was right. Select a row to draw its plan and its card just beneath it.",
                    "A stock is listed once, under the first setup it qualified for in the page's order, and never while a trade for it is still open. The reward to risk is a fact about the chart and not a chance of anything."),
                title: "Tonight's buy points",
                lede: Invariant($"{points.Count} across every setup tonight."),
                stamp: Cards.Night(night),
                region: "buy-points");

    // The date line's words for a night not yet run, in place of the notice it drew: when tonight's night starts, in New
    // York's time with UTC beneath the pointer, read off the start the newest nights share; none for a night in any other
    // state, or where the run log holds no night to read the start off.
    // see: The date line names tonight's night by the start the most of the newest seven nights share
    public static string NightDue(NightView night, TimeOnly? usual, TimeZoneInfo zone)
    {
        if (night.State != NightStates.NotYet || usual is not { } minute)
        {
            return string.Empty;
        }

        var starts = NightTime.On(night.Session, minute, zone);
        var local = TimeZoneInfo.ConvertTime(starts, zone);

        return Invariant($" · tonight's night runs at <time class=\"night-due\" datetime=\"{starts.UtcDateTime:yyyy-MM-dd'T'HH:mm'Z'}\" title=\"{starts.UtcDateTime:HH:mm} UTC\">{local:HH:mm} ET</time>");
    }

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
        <div class="lane" id="lane" data-lane="{{{Escaped(lane)}}}"><span class="lane-lbl">Reports written by</span><span class="lane-opts">
        <span class="lane-opt" data-choice="local" data-offered="false" aria-disabled="true" title="{{{Escaped(LaneWaitsOn)}}}">Local model</span>
        <span class="lane-opt{{{(lane == PaidLane ? " on" : string.Empty)}}}" data-choice="paid" data-offered="true">Paid model</span>
        </span></div>
        """;

    // The bar's two menus, each a name opening its links beneath it: the research the queue writes and the records the
    // nights and the loop keep, the report lane's switch at the foot of the research.
    // see: The bar's index menu chooses the index every page reads, each choice naming its cap and its members
    static string MenuGroup(string key, string name, string links) =>
        $"<div class=\"m-group\" data-group=\"{key}\"><button type=\"button\" class=\"m-group-btn\" aria-expanded=\"false\">{name}</button><div class=\"m-drop\">{links}</div></div>";

    // The bar's links: the four screens read most, then the two menus.
    string Nav(string lane) =>
        "<nav class=\"m-nav\" aria-label=\"Screens\">"
        + "<a href=\"#/\" data-view=\"tonight\">Tonight</a>"
        + $"<a href=\"{UniverseRoute}\" data-view=\"universe\">Universe</a>"
        + $"<a href=\"{PaperRoute}\" data-view=\"paper\">Paper trading</a>"
        + $"<a href=\"{LoopRoute}\" data-view=\"loop\">Loop<span class=\"m-badge\" id=\"loop-badge\" data-put=\"0\" hidden></span></a>"
        + MenuGroup("research", "Research", $"<a href=\"{ResearchedRoute}\" data-view=\"researched\">Researched</a><a href=\"{QueueRoute}\" data-view=\"queue\">Queue</a><a href=\"{WatchRoute}\" data-view=\"watch\">Watch list</a>" + LaneSwitch(lane))
        + MenuGroup("records", "Records", $"<a href=\"{PicksRoute}\" data-view=\"picks\">Past picks</a><a href=\"{RunRoute}\" data-view=\"run\">Run</a><a href=\"{LedgerRoute}\" data-view=\"ledger\">Ledger</a>")
        + "</nav>";

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
        <header class="mast" id="mast"><div class="wrap"><a class="m-brand" href="#/">{{{Escaped(title)}}}</a><div class="m-panel" id="menu">{{{Nav(lane)}}}<form class="m-search" id="search" role="search"><input id="find" type="search" list="findable" placeholder="Find a ticker or company" aria-label="Find a name by its ticker or its company's name" autocomplete="off" spellcheck="false"><datalist id="findable"></datalist></form><button type="button" class="theme" id="theme">Dark palette</button></div><div class="m-index" id="index-slot"></div><button type="button" class="m-menu" id="menu-button" aria-expanded="false" aria-controls="menu">Menu</button></div></header>
        <div class="wrap page-head" id="identity"></div>
        <main class="wrap" id="screen"></main>
        <script>
        const screen = document.getElementById('screen');
        const identity = document.getElementById('identity');
        const scrolls = { };
        let fresh = false;
        let drawing = false;
        // A press redrawing the screen to change one card keeps that card where it stood in the window: what finds the
        // card again after the redraw and how far down the window it stood, read by a redraw within ten seconds of it.
        let keptPlace = null;
        function keepPlace(control, find) {
          const card = control.closest('section.card');
          keptPlace = card ? { find: find, top: card.getBoundingClientRect().top, at: Date.now() } : null;
        }
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
            view = 'ledger';
            const ledger = await fetch('/screens/ledger' + (query ? '?' + query : ''));
            screen.innerHTML = await ledger.text();
          } else if (path === '{{{LoopRoute}}}') {
            view = 'loop';
            const loop = await fetch('/screens/loop' + (query ? '?' + query : ''));
            screen.innerHTML = await loop.text();
          } else if (path.startsWith('{{{ChartRoute}}}')) {
            view = 'chart';
            // A stock's chart page, which every link opening a stock opens first, at the interval its link names.
            const ticker = encodeURIComponent(path.slice('{{{ChartRoute}}}'.length));
            const response = await fetch('/screens/chart/' + ticker + (query ? '?' + query : ''));
            screen.innerHTML = await response.text();
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
            const queued = await fetch('/screens/queue' + (query ? '?' + query : ''));
            screen.innerHTML = await queued.text();
          } else if (path === '{{{PaperRoute}}}' || path === '{{{AccountRoute}}}') {
            view = 'paper';
            const paper = await fetch('/screens/paper' + (query ? '?' + query : ''));
            screen.innerHTML = await paper.text();
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
        // The screen's title and date line beneath the bar, the screen's own markers and the scroll, once a screen is in.
        function settle(view, hash, query) {
          const head = screen.querySelector('.screen-mast');
          identity.innerHTML = head ? head.innerHTML : '';
          for (const link of document.querySelectorAll('.m-nav a')) {
            if (link.dataset.view === view) { link.setAttribute('aria-current', 'page'); } else { link.removeAttribute('aria-current'); }
          }
          // A menu holding the screen drawn is marked as the screen's own link is.
          for (const group of document.querySelectorAll('.m-group')) {
            group.classList.toggle('current', group.querySelector('a[aria-current="page"]') !== null);
          }
          // The index menu a screen reading an index draws stands in the bar, and a screen reading none leaves its place empty.
          const pick = screen.querySelector('label.universe-pick');
          document.getElementById('index-slot').replaceChildren(...(pick ? [pick] : []));
          closeMenu();
          loopBadge();
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
          const placed = keptPlace && Date.now() - keptPlace.at < 10000 ? screen.querySelector(keptPlace.find) : null;
          const placedCard = placed ? placed.closest('section.card') : null;
          if (placedCard) { scrollTo(0, placedCard.getBoundingClientRect().top + scrollY - keptPlace.top); }
          else if (!fresh && scrolls[hash] != null) { scrollTo(0, scrolls[hash]); }
          else if (query.get('name') && document.getElementById('selected')) { document.getElementById('selected').scrollIntoView({ block: 'start' }); }
          else if (reasonRow) { reasonRow.scrollIntoView({ block: 'center' }); }
          else { scrollTo(0, 0); }
          keptPlace = null;
          fresh = false;
          drawing = false;
          paintTheme();
          quoting();
          marking();
        }
        addEventListener('hashchange', show);
        // A name page shown inside the regular session asks for its delayed quote once the interval since the name's
        // newest ask has passed, and reads the newest stored quote back every fifteen seconds while it is shown, drawing
        // the masthead's price, its line, the tiles and each band's distance at it. A page about an earlier night, the
        // exported file and a page outside the session carry no route and ask for nothing.
        // see: The name page draws a delayed quote in the regular session, asked by a worker job at most every five minutes under a day's cap
        let quoteTimer = null;
        function quoting() {
          if (quoteTimer) { clearInterval(quoteTimer); quoteTimer = null; }
          const head = screen.querySelector('.screen-mast[data-quote-route]');
          if (!head) { return; }
          const route = head.getAttribute('data-quote-route');
          const open = Date.parse(head.getAttribute('data-session-open'));
          const close = Date.parse(head.getAttribute('data-session-close'));
          const every = Number(head.getAttribute('data-interval-minutes')) * 60000;
          let asked = head.getAttribute('data-asked') ? Date.parse(head.getAttribute('data-asked')) : 0;
          let drawn = asked;
          async function tick() {
            const now = Date.now();
            if (!document.body.contains(head)) { clearInterval(quoteTimer); quoteTimer = null; return; }
            if (document.visibilityState !== 'visible' || now < open || now >= close) { return; }
            if (now - asked >= every) {
              asked = now;
              await fetch(route, { method: 'POST', headers: { '{{{PassHeader}}}': '{{{PassHeaderValue}}}' } }).catch(() => { });
            }
            const reply = await fetch(route).then((response) => response.ok ? response.json() : null).catch(() => null);
            if (!reply || !reply.live || Date.parse(reply.askedAt) <= drawn) { return; }
            drawn = Date.parse(reply.askedAt);
            asked = Math.max(asked, drawn);
            for (const place of [identity, head]) {
              const price = place.querySelector('.m-price');
              if (price) { price.innerHTML = reply.price; price.setAttribute('data-live', 'yes'); }
              const when = place.querySelector('.m-when');
              if (when) { when.innerHTML = reply.asOf; }
            }
            const tiles = screen.querySelector('.lead-tiles');
            if (tiles) { tiles.outerHTML = reply.tiles; }
            for (const band of reply.distances) {
              const cell = screen.querySelector('.level-summary tr.band[data-low-edge="' + CSS.escape(band.low) + '"][data-high-edge="' + CSS.escape(band.high) + '"] td.away');
              if (cell) { cell.textContent = band.away; cell.setAttribute('data-away', band.value); cell.setAttribute('data-at', 'quote'); }
              const rung = screen.querySelector('.ladder-figure g.lf-band[data-low-edge="' + CSS.escape(band.low) + '"][data-high-edge="' + CSS.escape(band.high) + '"] text.lf-away');
              if (rung) { rung.textContent = band.ladder; rung.setAttribute('data-away', band.value); rung.setAttribute('data-at', 'quote'); }
            }
            const ladder = screen.querySelector('.ladder-figure');
            if (ladder) {
              ladder.setAttribute('data-at', 'quote');
              const tag = ladder.querySelector('text.lf-price');
              if (tag) { tag.textContent = 'price now ' + reply.priceText; }
            }
          }
          tick();
          quoteTimer = setInterval(tick, 15000);
        }
        // A written or filed entry of a name page's contents dated after the day this browser last opened the page is
        // marked new. The day is kept in this browser alone, so the store records no visit, and the first visit and the
        // exported file mark nothing.
        // see: A section newer than the reader's last visit is marked new by the browser alone, and the store records no visit
        function marking() {
          const nav = screen.querySelector('nav.contents[data-ticker]');
          if (!nav) { return; }
          const key = 'eb-visited-' + nav.getAttribute('data-ticker');
          let last = null;
          try { last = localStorage.getItem(key); } catch (error) { }
          if (last) {
            for (const entry of nav.querySelectorAll('li[data-dated]')) {
              if (entry.getAttribute('data-dated') > last && !entry.querySelector('.pill.new')) {
                const pill = document.createElement('span');
                pill.className = 'pill new';
                pill.textContent = 'NEW';
                entry.appendChild(pill);
              }
            }
          }
          try { localStorage.setItem(key, new Date().toISOString().slice(0, 10)); } catch (error) { }
        }
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
          // A choice kept in the link under its own key with every other key kept, its first choice the key's absence.
          const kept = event.target.closest ? event.target.closest('select[data-merge-key]') : null;
          if (kept) { fresh = true; location.hash = merged(kept.getAttribute('data-merge-key'), kept.value === '' ? null : kept.value); }
        });
        // Words searched for kept in the link under the box's key with every other key kept, none where the box is
        // empty, and the page back to the first.
        document.addEventListener('submit', (event) => {
          const form = event.target;
          if (!(form instanceof HTMLFormElement) || !form.hasAttribute('data-merge-key')) { return; }
          event.preventDefault();
          const key = form.getAttribute('data-merge-key');
          const box = form.querySelector('input[name="' + key + '"]');
          const words = box ? box.value.trim() : '';
          const hash = merged(key, words === '' ? null : words);
          const cut = hash.indexOf('?');
          const params = new URLSearchParams(cut < 0 ? '' : hash.slice(cut + 1));
          params.delete('page');
          const searched = form.closest('section.card[data-card]');
          if (searched) { keepPlace(form, 'section.card[data-card="' + CSS.escape(searched.getAttribute('data-card')) + '"]'); }
          fresh = true;
          location.hash = (cut < 0 ? hash : hash.slice(0, cut)) + (params.toString() ? '?' + params.toString() : '');
        });
        // A name put on the watch list or taken off it: the press names the ticker the form carries, sends the
        // page's header, and the screen is drawn again with what the read surface said above the list.
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
        // A card folded behind a details element whose open state the link keeps: opening or folding it writes its key
        // into the link or takes it out, with no redraw, so a view is a link.
        document.addEventListener('toggle', (event) => {
          const details = event.target;
          if (!(details instanceof HTMLDetailsElement) || !details.dataset.linkKey) { return; }
          const hash = location.hash || '#/';
          const cut = hash.indexOf('?');
          const params = new URLSearchParams(cut < 0 ? '' : hash.slice(cut + 1));
          if (details.open) { params.set(details.dataset.linkKey, '{{{MarkRenderer.CloseToBuyOpen}}}'); } else { params.delete(details.dataset.linkKey); }
          const next = (cut < 0 ? hash : hash.slice(0, cut)) + (params.toString() ? '?' + params.toString() : '');
          if (next !== location.hash) { history.replaceState(null, '', next); }
        }, true);
        // A count of the stocks one gate short pressed on Tonight opens their card and brings it into view.
        document.addEventListener('click', (event) => {
          const link = event.target instanceof Element ? event.target.closest('a.close-open') : null;
          const card = link ? document.getElementById('{{{MarkRenderer.CloseToBuyId}}}') : null;
          if (!card) { return; }
          event.preventDefault();
          const details = card.querySelector('details');
          if (details) { details.open = true; }
          history.replaceState(null, '', link.getAttribute('href'));
          card.scrollIntoView({ block: 'start' });
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
        // The Loop page's presses, approve or decline of a proposal and approve of a restore: the form's fields sent with
        // the page's own header, the page drawn again, and what the read surface said put in the line of what was pressed.
        document.addEventListener('submit', async (event) => {
          const form = event.target;
          if (!(form instanceof HTMLFormElement) || !form.classList.contains('loop-press')) { return; }
          event.preventDefault();
          const key = form.getAttribute('data-loop-key');
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
          const holder = Array.from(screen.querySelectorAll('.loop-decision')).find((one) => one.getAttribute('data-loop-key') === key);
          const place = holder ? holder.querySelector('.loop-said') : null;
          if (place) { place.innerHTML = said; } else { const top = screen.querySelector('section.loop'); if (top) { top.insertAdjacentHTML('afterbegin', said); } }
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
          // A card's tab: the rule it chooses kept in the link under the family's own key with every other key kept, the
          // live rule as the key's absence, so the default returns to it.
          const tab = event.target.closest('a[data-rule-tab]');
          if (tab) {
            event.preventDefault();
            keepPlace(tab, 'a[data-rule-tab="' + CSS.escape(tab.getAttribute('data-rule-tab')) + '"]');
            location.hash = merged(tab.getAttribute('data-rule-tab'), tab.getAttribute('data-value') === '{{{RuleScreenWords.LiveSlug}}}' ? null : tab.getAttribute('data-value'));
            return;
          }
          // A chip, a sector's tile or a column's heading keeps its card where it stood, found again by the card's mark.
          const keeper = event.target.closest('a.chip[data-filter], a.sector-tile, a.sort');
          const keeperCard = keeper ? keeper.closest('section.card[data-card]') : null;
          if (keeperCard) { keepPlace(keeper, 'section.card[data-card="' + CSS.escape(keeperCard.getAttribute('data-card')) + '"]'); }
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
          // A paper account's key hides or shows its line on the chart, in the browser alone.
          const paperKey = event.target.closest('.paper-key[data-line]');
          if (paperKey) {
            const shown = paperKey.getAttribute('aria-pressed') !== 'true';
            paperKey.setAttribute('aria-pressed', shown ? 'true' : 'false');
            for (const line of screen.querySelectorAll('.paper-chart path[data-account="' + CSS.escape(paperKey.getAttribute('data-line')) + '"]')) { line.style.display = shown ? '' : 'none'; }
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
        // The menu: on a narrow screen its button opens the panel holding the links, the search, the palette's toggle and
        // the report generation lane, and a screen drawn closes it; on a wide one the panel stands in the line.
        const menuButton = document.getElementById('menu-button');
        const menuPanel = document.getElementById('menu');
        function closeMenu() { menuButton.setAttribute('aria-expanded', 'false'); menuPanel.classList.remove('open'); closeGroups(null); }
        // A menu on the bar opens its links beneath its name, and closes on a second press, on a press anywhere else and
        // as a screen is drawn.
        function closeGroups(kept) {
          for (const group of document.querySelectorAll('.m-group.open')) {
            if (group !== kept) { group.classList.remove('open'); group.querySelector('.m-group-btn').setAttribute('aria-expanded', 'false'); }
          }
        }
        document.addEventListener('click', (event) => {
          const named = event.target.closest('.m-group-btn');
          if (!named) { if (!event.target.closest('.m-drop')) { closeGroups(null); } return; }
          const group = named.closest('.m-group');
          const open = !group.classList.contains('open');
          closeGroups(group);
          group.classList.toggle('open', open);
          named.setAttribute('aria-expanded', open ? 'true' : 'false');
        });
        // The chart page's toggle shows or hides the night's levels behind the candles, in the browser alone.
        document.addEventListener('click', (event) => {
          const toggle = event.target.closest('[data-bands-toggle]');
          if (!toggle) { return; }
          const figure = toggle.closest('.card').querySelector('.chart-fig');
          const on = !figure.classList.contains('bands-on');
          figure.classList.toggle('bands-on', on);
          toggle.setAttribute('aria-pressed', on ? 'true' : 'false');
          toggle.textContent = on ? 'Hide the night\'s levels' : 'Show the night\'s levels';
        });
        // The line above the chart reads the candle under the pointer, and the newest again once the pointer leaves.
        function readCandle(candle) {
          const chart = candle.closest('.candle-chart');
          const reading = candle.closest('.card').querySelector('.chart-reading');
          if (reading) { reading.innerHTML = candle.getAttribute('data-reading'); }
          for (const label of chart.querySelectorAll('[data-legend]')) {
            const words = candle.getAttribute('data-' + label.getAttribute('data-legend'));
            if (words) { label.textContent = words; }
          }
        }
        document.addEventListener('pointerover', (event) => {
          const candle = event.target.closest && event.target.closest('.candle-chart .candle');
          if (candle) { readCandle(candle); }
        });
        document.addEventListener('pointerout', (event) => {
          const chart = event.target.closest && event.target.closest('.candle-chart');
          if (!chart || (event.relatedTarget && chart.contains(event.relatedTarget))) { return; }
          const candles = chart.querySelectorAll('.candle');
          if (candles.length > 0) { readCandle(candles[candles.length - 1]); }
        });
        menuButton.addEventListener('click', () => {
          const open = menuButton.getAttribute('aria-expanded') !== 'true';
          menuButton.setAttribute('aria-expanded', open ? 'true' : 'false');
          menuPanel.classList.toggle('open', open);
        });
        // A setup's tile at the top of tonight's page brings its card into view, the sector heavyweights' by their own card.
        screen.addEventListener('click', (event) => {
          const tile = event.target.closest('.setup-tile[data-card]');
          if (!tile) { return; }
          const family = tile.getAttribute('data-card');
          const card = family === '{{{EquityBrief.Core.Families.HeavyweightRule.Name}}}'
            ? screen.querySelector('section.card[data-card="heavyweights"]')
            : (screen.querySelector('section.family-card[data-family="' + CSS.escape(family) + '"]') || { closest: () => null }).closest('section.card');
          if (card) { card.scrollIntoView({ block: 'start' }); }
        });
        // The Loop's badge: the proposals put to the operator that hold no decision, read off the surface on every screen
        // drawn and shown at one or more.
        async function loopBadge() {
          const badge = document.getElementById('loop-badge');
          try {
            const answer = await fetch('{{{LoopPutRoute}}}');
            const put = answer.ok ? parseInt(await answer.text(), 10) : 0;
            badge.setAttribute('data-put', String(put > 0 ? put : 0));
            badge.textContent = put > 0 ? String(put) : '';
            badge.hidden = !(put > 0);
          } catch (error) { badge.hidden = true; }
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
        // Writing one report now, ahead of the queue. Off-peak it is pressed at once; inside a peak window the
        // press first says when the window ends and how many times the off-peak price the report costs, and is
        // sent, carrying the confirmation, only once the operator says so.
        document.addEventListener('submit', async (event) => {
          const form = event.target;
          if (!(form instanceof HTMLFormElement) || !form.classList.contains('now-control')) { return; }
          event.preventDefault();
          if (form.dataset.peak === 'true' && form.dataset.confirmed !== 'yes') {
            for (const open of screen.querySelectorAll('.confirm')) { open.remove(); }
            const box = document.createElement('div');
            box.className = 'confirm key';
            const line = document.createElement('p');
            const ends = form.querySelector('.now-peak');
            line.textContent = 'The research model is at its peak price until ' + (ends ? ends.textContent : 'its peak window ends')
              + ', so writing ' + form.dataset.writes + ' now costs about ' + form.dataset.multiple
              + ' its off-peak price. Write it now anyway?';
            const go = document.createElement('button');
            go.type = 'button'; go.className = 'btn'; go.textContent = 'Write it now';
            const stop = document.createElement('button');
            stop.type = 'button'; stop.className = 'btn-2'; stop.textContent = 'Wait for the off-peak price';
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
          const body = new URLSearchParams();
          if (form.dataset.confirmed === 'yes') { body.set('confirm', '{{{WriteNowAtPeak}}}'); }
          const response = await fetch(form.getAttribute('action'), {
            method: 'POST',
            headers: { '{{{PassHeader}}}': '{{{PassHeaderValue}}}' },
            body,
          });
          const said = await response.text();
          const kept = scrollY;
          await show();
          scrollTo(0, kept);
          const notice = document.createElement('div');
          notice.innerHTML = said;
          const drawn = notice.firstElementChild;
          const region = screen.querySelector('.queue-part[data-region="outstanding"]');
          if (drawn && region) { region.prepend(drawn); }
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
        async function go(text) {
          for (const old of screen.querySelectorAll('.notice[data-not-found]')) { old.remove(); }
          const ticker = found(text);
          // On the watch list the name found is added to it under the page's own header, and elsewhere its page opens.
          if (ticker && location.hash.startsWith('{{{WatchRoute}}}')) {
            find.value = '';
            find.blur();
            const response = await fetch('{{{WatchPostRoute}}}' + encodeURIComponent(ticker), {
              method: 'POST',
              headers: { '{{{PassHeader}}}': '{{{PassHeaderValue}}}' },
            });
            const said = await response.text();
            await show();
            const place = screen.querySelector('.watch-said');
            if (place) { place.innerHTML = said; }
            return;
          }
          if (ticker) {
            find.value = '';
            find.blur();
            fresh = true;
            location.hash = '{{{ChartRoute}}}' + encodeURIComponent(ticker);
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
    // see: A screen reads and renders, and each figure it works out at a price or from stored figures has one function in the core
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
        DecisionCardView? decision = null,
        AveragesFrom? averagesFrom = null,
        // The delayed quote of the session the page is drawn in, the session itself, the headline and the four tiles, and
        // whether the page asks for its quote, which an exported file and an earlier night's page do not.
        LiveQuoteView? quote = null,
        QuoteSession? quoteSession = null,
        HeadlineView? headline = null,
        TilesView? tiles = null,
        bool asksForQuotes = false,
        // The latest quarter line by line, the margins, the segment table, the analysts' figures, the dividend's safety
        // and the valuation, each worked by the core from stored figures, and the filing's segment table as filed.
        ReportView? report = null,
        string? rawSegments = null,
        // The typical earnings move, the long view, the chart's report and ex-dividend marks, the ladder figure, the
        // plan's shares and the days the news labels cover, each worked by the core from stored figures.
        HistoryView? history = null,
        // The next ninety days' dated events, and the latest quarter's lines the filing and the provider's fetch state
        // differently with whether any filing's facts are held to check them against.
        CalendarView? calendar = null,
        IReadOnlyList<EquityBrief.Core.Report.FigureConflict>? figuresChecked = null,
        bool filedFactsHeld = false,
        // The chart of what is behind the stock, from 19.2.
        BehindItView? behind = null)
    {
        var region = new StringBuilder();
        var sections = written ?? [];
        var documents = sources ?? [];
        var session = bars.Count > 0 ? bars[^1].SessionDate : (DateOnly?)null;
        var company = mast?.Company is { Length: > 0 } named ? named : ticker;

        // The cards, held apart from the region so the contents can be written from what was
        // drawn and still stand above it. A card records itself as it is added, at the place
        // section 4 gives its region, and the page draws them in the order of those places, which
        // is what makes the contents a reading of the page rather than a second list of its
        // sections and the page's order the one `RegionOrder` states.
        // see: The name page answers a buyer's questions in the order a buyer asks them
        var body = new StringBuilder();
        var onThePage = new List<ContentsEntry>();
        var composed = new List<(int Place, string Id, string Title, string Markup, DateOnly? Dated)>();

        void Card(string place, string id, string title, string markup, DateOnly? dated = null)
        {
            var at = Array.IndexOf(RegionOrder, place);

            composed.Add((at >= 0 ? at : throw new InvalidOperationException($"No region of section 4 is named '{place}'."), id, title, markup, dated));
        }

        // A written section's own id, which its entry in the contents links to. Derived from
        // the section's name rather than from its position, so a name holding fewer sections
        // does not move another section's link.
        static string SectionId(string section) => MarkRenderer.WrittenId(section);

        // The written sections drawn in one place, each where section 4 puts it, in a
        // card whose left column states the day it was written.
        // see: A research record is written and dated per section, not as a whole
        void Draw(string place, IReadOnlyList<string> placed, string? role = null)
        {
            foreach (var name in placed)
            {
                if (sections.FirstOrDefault(section => string.Equals(section.Section, name, StringComparison.Ordinal)) is not { } section)
                {
                    continue;
                }

                Card(
                    place,
                    SectionId(name),
                    name,
                    Cards.Dated(
                        name,
                        "Written",
                        section.AsOf,
                        marks.WrittenSection(ticker, section, documents, riskLines: string.Equals(name, MarkRenderer.TheRisks, StringComparison.Ordinal) ? history?.Risk : null),
                        section: name,
                        id: SectionId(name),
                        role: role),
                    section.AsOf);
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

        // The masthead the chart page draws as well, with a link back to the stock's chart from 19.11.
        // see: Every link that opens a stock opens its chart page first, and the chart page links to its report
        region.Append(Masthead(ticker, bars, mast, quote, quoteSession, asksForQuotes, night, suspect, watched, Invariant($" · <a class=\"m-chart\" href=\"{ChartRoute}{Uri.EscapeDataString(ticker)}\">Its chart</a>")));

        // The headline and the four tiles under the masthead, before the contents: one sentence carrying no figure, and the
        // quarter's earnings, the year's growth, the yield and the distance from the year's high, each worked from stored
        // figures at the price the masthead draws.
        // see: The headline carries no figure and is written last from the accepted claims, and code writes it where none is accepted
        // see: Four tiles under the headline are worked by code from stored figures at the price the page draws
        region.Append(marks.Lead(ticker, headline, tiles));

        // What the page is for and what it refuses to do, and the words it uses, first and folded shut under one line, so a
        // reader meets the refusals where the page opens and the figures stand at its head.
        Card("how-to-read", "how-to-read", "How to read this page", Cards.Computed(
            "How to read this page",
            "<details class=\"read-fold\"><summary>Open what the page is for, what it refuses to do and the words it uses</summary>" + Intro(ticker, company) + "</details>",
            id: "how-to-read",
            region: "how-to-read"));

        // How the rules read it: why the name is here, its gates, its swing readings and its member readings, each kept
        // whole and folded shut in one region after the plan, since a reader asks what the rules made of a name after what
        // to do about it. Each part keeps its own id, so a link to it still lands on it.
        var rules = new StringBuilder();

        static string Fold(string id, string title, string markup) =>
            $"<details class=\"rule-part\" id=\"{id}\" data-card=\"{id}\"><summary>{title}</summary>{markup}</details>";

        // Why it is here, present only when the name is on tonight's list or one gate short of it, beneath the line
        // section 18 draws where the listing was written before the correction, and otherwise the line saying it is not.
        // see: A member that missed exactly one gate and no exclusion is drawn close to a buy point nearest first, and recommends nothing
        var why = WrittenBeforeTheCorrectionLine(writtenBeforeTheCorrection)
            + (passed is { } filtered
                ? marks.WhyItPassed(ticker, filtered)
                : missed is { } oneShort
                    ? marks.WhyItIsClose(ticker, oneShort.Evening, oneShort.Gate)
                    : marks.WhyItIsHere(ticker, firedReasons));

        rules.Append(passed is not null || missed is not null || firedReasons.Count > 0
            ? Fold(
                "why",
                passed is { } through
                    ? Invariant($"On the list on {through.Evening:yyyy-MM-dd} because the swing filter passed it")
                    : missed is { } near
                    ? Invariant($"Close to a buy point on {near.Evening:yyyy-MM-dd}: one gate short")
                    : night is { } listed ? Invariant($"On the list on {listed:yyyy-MM-dd} for these reasons") : "On tonight's list for these reasons",
                why)
            : why);

        // The trend state, in a word. Read off the ladder row rather than worked
        // out here, and a name with no row says so rather than showing nothing:
        // an absence stated and an absence drawn as emptiness are different
        // things, and only the first is readable.
        var trend = trendState is null
            ? "<p class=\"trend-state\" data-trend-state=\"none\">no ladder row for this name yet</p>"
            : Invariant($"<p class=\"trend-state\" data-trend-state=\"{Escaped(trendState)}\" data-as-of=\"{trendAsOf:yyyy-MM-dd}\">trend {Escaped(trendState.Replace('_', ' '))}</p>");

        // The fact strip, the levels region's technical readings beneath the bands, stating seven things rather than one.
        // It arrives already written, for the reason the event book does: two of its seven parts are fundamentals and no
        // mark renders them.
        var technicals = Invariant($"<div class=\"technicals\" id=\"facts\" data-card=\"facts\"><div class=\"sub\">The technical readings</div>") + trend + factStrip + Cards.Key(
            "Two sources.",
            "The close, the averages, momentum and the typical daily move are computed from the stored daily bars. The market value, the two price multiples and the report date come from the latest filing and the calendar.",
            "Relative strength and trend momentum are here for context. Nothing on this page is decided by them.") + "</div>";

        // The swing readings, beneath the night's figures: each return with its place among the
        // members' returns, the recent high and the pullback from it, the volume while it came down
        // and the range's tightness, as the swing reader stored them for the night.
        // see: A page ranks no company as an investment, and the one reading of a company that orders a list is the direction of its reported quarters
        // From 19.2 the chart of what is behind the stock stands beneath the swing readings, or beneath the member readings
        // on an index the swing reader does not read.
        // see: A pick's card and a stock's page draw what is behind it by heading, its percentiles always and its push where the score passed
        var behindPart = behind is null ? string.Empty : marks.BehindIt(behind);
        var swingPart = swing is null
            ? string.Empty
            : Fold(
                "swing",
                "Its swing readings: where it stands for a swing trade",
                marks.SwingTable(ticker, swing) + behindPart + Cards.Key(
                    "How to read it.",
                    Invariant($"Each return is the close against the close {EquityBrief.Core.Filter.SwingReadings.ReturnShortSessions} and {EquityBrief.Core.Filter.SwingReadings.ReturnLongSessions} sessions before it, beside the share of the index's other members whose return over the same span is lower. The pullback is how far the close sits below the highest high of the last {EquityBrief.Core.Filter.SwingReadings.HighWindow} sessions, counted in the moves {Escaped(ticker)} usually makes in a session; the volume beneath it is the median session's volume since that high against its fifty-day average, and the tightness is the last {EquityBrief.Core.Filter.SwingReadings.TightShortSessions} sessions' true range against the last {EquityBrief.Core.Filter.SwingReadings.TightLongSessions}'s. All of it is computed from the stored daily bars."),
                    "These are facts about the chart. A high place is a strong return behind the name and not a forecast in front of it, and this page " + RankRefusal + "."));

        // The member readings, beneath the swing readings: what a trade in it costs, the quality its quarters give it, its
        // year's high, its volume and its industry, as the member reader stored them for the night under its index.
        // see: A 400 or 600 trade pays the published effective spread for its size and price, and its pass tests read the edge after it
        var memberPart = member is null
            ? string.Empty
            : Fold(
                "member",
                Invariant($"Its member readings: what a trade in it costs and what its quarters say, as a member of the {Escaped(member.Index)}"),
                marks.MemberReadingsTable(ticker, member) + (swing is null ? behindPart : string.Empty) + Cards.Key(
                    "How to read it.",
                    Invariant($"The dollar volume is the mean of the close times the volume over the last {EquityBrief.Core.Readings.MemberReadings.DollarVolumeSessions} sessions. A round trip is half the published effective spread for the company's value and the price at the buy and half again at the sale, stated at the table's figure and at double. The profit gate sums net income over the {EquityBrief.Core.Readings.MemberReadings.Quarters} newest quarters filed before the night, and the coverage asks their operating income for at least twice their interest expense, a company filing none and a financial company passing. The year's high is the highest high of the {EquityBrief.Core.Readings.MemberReadings.YearSessions} sessions before the night, and the industry's figures are its S&amp;P 500 members', each weighted by its company's value."),
                    "These are facts about its trading and its quarters as they stood on the night. The S&amp;P 400's and 600's rules read the price, the dollar volume and the profit gate before they list a name, and the rest are readings their sweeps test."));

        // The swing filter's answer for the name, whatever the name: each gate with whether it passed
        // and why, the setup and the trigger, the trade read both ways and the exclusions.
        var gatesPart = gates is null
            ? string.Empty
            : Fold(
                "gates",
                "Its gates: where it stands against the swing filter",
                marks.GatesTable(ticker, gates) + Cards.Key(
                    "How to read it.",
                    "Each gate is one question the swing filter asks of every member each night, in order: the market's breadth, the trend and strength, a setup, a trigger new on the night, and a trade worth taking. A name passes only where all five pass and no exclusion applies. The trade is read from the ladder's first tranche and from the swing plan that night's live rule used, and the plan marked as read decides the gate. An alternative plan being tested in the background is not drawn: how it would have traded a stock is the evaluation it waits for.",
                    gates.Rule == EquityBrief.Core.Shortlist.ListRules.Filter
                        ? "A failed gate names what it read and why it failed, and a name passing all five that no exclusion removes is on that evening's list."
                        : "A failed gate names what it read and why it failed. The six reasons drew that evening's list, so these answers decided nothing on it."));

        rules.Append(gatesPart).Append(swingPart).Append(memberPart);

        Card("rules", "rules", "How the rules read it", Cards.Computed(
            "How the rules read it",
            rules.ToString(),
            title: "What the swing filter and the readings made of it",
            lede: "Each part folds open: why it is here, its gates, its swing readings and its member readings, as the night stored them.",
            stamp: Cards.Night(gates?.Session ?? session),
            id: "rules",
            region: "rules"));

        // The short version, the first written region section 4 lists, with its date beside it. Where no
        // accepted one stands, code writes one from computed parts and its heading says why: the checker
        // left the model's out, the pass did not write it, or no research has been written for the name.
        // see: The short version and the headline are written last from the accepted claims alone, and code writes each where none is accepted
        if (sections.Any(section => string.Equals(section.Section, MarkRenderer.TheShortVersion, StringComparison.Ordinal)))
        {
            Draw("short", AtTheTop);
        }
        else
        {
            // The industry's cycle is its theme's and is drawn for every member, so it is not research
            // written for this name.
            var fellBack = (leftOut ?? []).Any(section => string.Equals(section.Section, MarkRenderer.TheShortVersion, StringComparison.Ordinal));
            var namedNotWritten = (notWritten ?? []).Any(section => string.Equals(section.Section, MarkRenderer.TheShortVersion, StringComparison.Ordinal));
            var nothingWritten = !sections.Any(section => !string.Equals(section.Section, EquityBrief.Core.Research.ClaimRules.CycleSection, StringComparison.Ordinal))
                && (leftOut ?? []).Count == 0;

            Card("short", SectionId(MarkRenderer.TheShortVersion), MarkRenderer.TheShortVersion, Cards.Computed(
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

        // How it got here: the table of the year's biggest moves, numbered as the chart numbers them, each with its cause
        // as the core took it and where that came from, and the days the news labels cover.
        // see: A move's cause is research's where written, then the labelled news inside it and then the filings dated inside it
        Card("how-it-got-here", "how-it-got-here", "How it got here", Cards.Computed(
            "How it got here",
            marks.MovesTable(ticker, moves, causes, history?.LabelsCover) + Cards.Key(
                "How to read it.",
                "The year's biggest moves, largest first and numbered as the chart above numbers them on their last sessions. Each one's cause is taken in a set order: research's sentences where research has been written; otherwise the stories the news labeller labelled that were published after the close the move is measured from and by the close of its last session, an opinion piece aside, with the results reported and the filings filed inside the move beneath them; otherwise those results and filings alone; otherwise the row says none was found. The column beside it says where the cause came from. Beside each move is its group's median move over the same sessions: the name's industry where at least five other members share it, and its sector otherwise, with how many members the median was taken over, so a move the whole group made reads apart from one the name made alone.",
                "A result or a filing inside a move says what fell on those sessions, not that it moved the price. This is the path that produced tonight's bands."),
            title: "The year's biggest moves and what was behind them",
            stamp: Cards.Night(session),
            id: "how-it-got-here",
            region: "how-it-got-here"));

        // The peers, beneath the table of the biggest moves: ten at most of the group those moves
        // are read against, by price alone, those sharing the name's industry first and then by how
        // closely each one's daily moves followed the name's.
        // see: Peers are shown by price alone, ten at most with the name's industry first and then the members whose daily moves followed it most closely
        if (peers is not null)
        {
            Card("peers", "peers", "Its group, by price", Cards.Computed(
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
        chart.Append(marks.LevelChart(ticker, bars, averages, bands, new ChartFrame(Scale: Scale, Markers: MarkRenderer.MovesOnTheChart(ticker, moves), Events: history?.Events)));

        if (bars.Count > 0 && profile.Count > 0)
        {
            chart.Append(marks.VolumeProfile(ticker, profile, marks.AxisFor(bars, averages), bands, Scale));
        }

        chart.Append("</div>");

        // Where the averages over the chart's first sessions came from, or why they start partway across it.
        // see: The chart's averages are read over the sessions before the store's year from the pulled history at the store's scale, by a step whose figures only the name and chart pages draw
        if (averagesFrom is { } from)
        {
            chart.Append(from.Pull is { } pull
                ? $"<p class=\"averages-from\" data-pull=\"{Escaped(pull)}\">The averages on the chart's first sessions are read through the history pulled before the stored year, from the pull {Escaped(pull)}, brought to the stored prices.</p>"
                : $"<p class=\"averages-from\" data-reason=\"{Escaped(from.Reason ?? string.Empty)}\">The averages start partway across the chart: {Escaped(from.Reason ?? "none was read before the stored year")}.</p>");
        }

        chart.Append(Cards.Key(
            "How to read it.",
            "A hollow candle closed above where it opened and a filled one closed below. Shaded bands are prices the stock has repeatedly turned at: green below the price is support, orange above it is resistance, and the column on the right names the last close and each band's edges, drawing the edges of the nearest band on either side in that band's own colour. The row above the chart names the averages and those two colours. A numbered circle marks one of the year's biggest moves on its last session, numbered as the table of moves lists them, and a lettered tag along the foot of the prices marks a report, R, on the session its reaction is read on, and an ex-dividend date, D. Beside the chart, on the same prices, is how many shares traded at each price, with a rule where a bar would reach if every price had traded the same and another at twice that.",
            "The plan buys at the bands below the price and sells at the ones above. Where many shares changed hands, many holders paid about that price, which is why the price tends to stall there."));
        chart.Append("<div class=\"sub\">Momentum</div>");
        chart.Append("<div class=\"fig\">").Append(marks.MomentumPanel(ticker, readings)).Append("</div>");
        chart.Append(Cards.Key(
            "Context, not a signal.",
            "Relative strength compares the size of the stock's recent up days with its recent down days, on a scale of 0 to 100. The momentum line is the gap between a fast and a slow average of the price and its signal line a slower average of that gap, so the bars show whether the gap is widening or narrowing. Read them beside the bands: near a support band, relative strength at or under 30, or bars below zero shrinking toward it, says the fall is losing force, and near a resistance band, 70 or over, or bars above zero shrinking, says the rise is.",
            "Nothing in the plan or the list reads these. When they and the bands disagree, the plan follows the bands."));
        Card("chart", "chart", "The chart", Cards.Computed("The chart", chart.ToString(), title: "The daily chart and its momentum", stamp: Cards.Night(session), id: "chart", region: "chart"));

        // The long view: the month-end closes over the history pulled before the store's year and the year itself, at
        // the stored prices, with the plan's levels and the year's high and low on the same axis, and a line naming the
        // pull and the ratio that brought it to the stored prices, or why none was read.
        // see: The long view draws the month-end closes over the pulled history at the bar store's scale with the plan's levels on the same prices
        if (history is { } read)
        {
            var viewed = new StringBuilder();

            if (!read.Newest)
            {
                viewed.Append("<p class=\"degraded\" data-long-view=\"newest-night-alone\">The long view is stored for the newest night alone, at that night's prices, so a page for an earlier night draws none; the newest night's page draws it.</p>");
            }
            else if (read.LongView.Count == 0)
            {
                viewed.Append($"<p class=\"degraded\" data-long-view=\"none\">No long view is stored for {Escaped(ticker)} tonight; the chart averages step writes one each night for every name whose stored year holds no hole.</p>");
            }
            else
            {
                viewed.Append("<div class=\"fig\">").Append(marks.LongView(ticker, read.LongView, read.Levels)).Append("</div>");
                viewed.Append(read.LongViewPull is { } longPull
                    ? read.LongViewRatio == 1m
                        ? $"<p class=\"long-view-basis\" data-pull=\"{Escaped(longPull)}\" data-ratio=\"1\">The months before the stored year are read from the history pulled before it, from the pull {Escaped(longPull)}, which stands at the stored prices: its close on the first session both hold is the stored one.</p>"
                        : Invariant($"<p class=\"long-view-basis\" data-pull=\"{Escaped(longPull)}\" data-ratio=\"{read.LongViewRatio}\">The months before the stored year are read from the history pulled before it, from the pull {Escaped(longPull)}, and brought to the stored prices by {read.LongViewRatio:0.######}, the stored close over the pulled close on the first session both hold.</p>")
                    : $"<p class=\"long-view-basis\" data-reason=\"{Escaped(read.LongViewReason ?? string.Empty)}\">The line starts with the stored year: {Escaped(read.LongViewReason ?? "no history before it was read")}.</p>");

                if (read.PlanLevelsAbsent is { } apart)
                {
                    viewed.Append($"<p class=\"long-view-levels\" data-plan-levels=\"absent\">The plan's levels are not drawn across it: {Escaped(apart)}.</p>");
                }
            }

            viewed.Append(Cards.Key(
                "How to read it.",
                "Each point is the last close a calendar month holds, the newest month's being the night's close, every one at the prices the stored year is adjusted to so a level the plan uses sits where the price stood. The green bands are where the plan buys, the orange where it sells or the trail begins, and the rules are the invalidation and the year's high and low.",
                "The years before the stored one show which of tonight's levels the price has met before. Nothing on the page is decided by them."));
            Card("long-view", "long-view", "The long view", Cards.Computed(
                "The long view",
                viewed.ToString(),
                title: read.LongView.Count > 0 && read.Newest ? Invariant($"Month-end closes since {read.LongView[0].Session:yyyy-MM}") : "Month-end closes",
                stamp: Cards.Night(session),
                id: "long-view",
                region: "long-view"));
        }

        // The levels: the ladder figure of the bands above and below the price the masthead draws, then each band from the
        // highest to the lowest with how far it sits from that price, which the page's script restates at a delayed quote
        // in the session, and beneath them the technical readings.
        var levelled = new StringBuilder();

        if (history is { } ladder)
        {
            levelled.Append("<div class=\"fig\">").Append(marks.LadderFigure(ticker, ladder.Ladder, ladder.LadderPrice, ladder.LadderLive)).Append("</div>");
            levelled.Append(Cards.Key(
                "How to read it.",
                "Each band is a row, those above the price drawn under Sell into these with the furthest first and those below it under Buy into these with the nearest first, a band the price is inside beside the price on the side of its role, spaced evenly rather than on a scale of prices. Each row gives the band's role, its prices, how far it sits from the price drawn in this stock's typical daily moves, what the plan does there where it buys or sells at it, and beneath them how many times the price reached it and turned away and the last session it did.",
                "A band near the price that has turned it often is where the next stall is most likely. The distance is restated at each newer delayed quote while the market is open."));
        }

        levelled.Append("<div class=\"tbl-wrap\">").Append(marks.LevelSummary(ticker, summary, absent, bars.Count > 0 ? bars[^1].Close : null)).Append("</div>");
        levelled.Append(technicals);

        Card("levels", "levels", "Levels", Cards.Computed("Levels", levelled.ToString(), title: "The bands above and below the price, and the technical readings", stamp: Cards.Night(session), id: "levels", region: "levels"));

        // The plan region: the plan column and the two tables it is read beside, the event
        // setups and the sizing arithmetic. A name with no plan says why, in the reason its ladder stored, and what would
        // bring one, and draws no figure, table or setup that would stand empty.
        // see: A name with no plan is drawn with the reason its ladder stored and what would bring a plan, and no empty table
        var planned = new StringBuilder();

        if (plan.Count == 0)
        {
            planned.Append(marks.PlanColumn(ticker, close, plan, history?.NoPlan ?? history?.PlanReason, history?.PlanForms));
        }
        else
        {
            planned.Append("<div class=\"plan-grid\"><div class=\"fig\">").Append(marks.PlanColumn(ticker, close, plan)).Append("</div><div>");
            planned.Append(marks.PlanTables(ticker, plan)).Append("</div></div>");

            // Each tranche as an equal share of a full position, its risk to the stop that sells it and its reward to risk to
            // each traded exit, a bar each, and the trail in the words of its rule.
            // see: Each tranche buys an equal share of a full position as the exits divide it, and its reward to risk is read to every traded exit
            if (history is { } shared)
            {
                planned.Append("<div class=\"sub\">Each tranche's share, risk and reward to risk</div>");
                planned.Append(marks.SharesTable(ticker, shared.Shares, shared.NoPlan));

                if (shared.Shares.Count > 0)
                {
                    planned.Append("<div class=\"fig\">").Append(marks.RewardToRiskBars(ticker, shared.Shares)).Append("</div>");
                }

                if (shared.Trail is { } trail)
                {
                    planned.Append("<p class=\"plan-trail\">").Append(Escaped(trail)).Append("</p>");
                }
            }

            planned.Append("<div class=\"sub\">Around the next report</div>").Append(eventBook);
            planned.Append("<div class=\"sub\">Sizing arithmetic</div>").Append(arithmetic);
            planned.Append(Cards.Key(
                "How to read the plan.",
                "The price now sits in the middle of the column. Orange zones above it are where part of the position is sold, and green blocks below are where it is bought. Each thin rule is a stop, and the heavy rule is the invalidation, the lowest stop. Each tranche buys an equal share of a full position, carried at the middle of its zone: its risk is that price less the stop that sells it, and its reward to risk to an exit is the exit's low edge less that price, over the risk. A tranche with no band beneath it has no stop of its own, so it states no risk and no reward to risk. The top traded exit is where the trail begins: what is left is held on a stop raised under the price rather than sold there, so a reward to it measures the move to where the trail begins and not a price the plan sells at.",
                "Everything above the price marker is a sale, everything below it is a purchase, and the lowest line is where the whole idea is wrong. The risk you take is yours to choose; the page only does the division."));
        }

        // Every earlier night the live list picked the name, folded beneath the plan: how many times and how each group
        // ended, then a row per listing, each trade as the Past picks screen draws it and as it stood on the night this
        // page draws. Absent for a name never picked before.
        // see: Every trade the live list recommended is shown, and their share waits for the minimum the reason records wait for
        if (earlier is { Count: > 0 } picked)
        {
            planned.Append(Fold(
                "on-the-list-before",
                Invariant($"On the list before: the nights the live list picked {Escaped(ticker)} before"),
                marks.OnTheListBefore(ticker, picked) + Cards.Key(
                    "How to read it.",
                    Invariant($"Each row is a night the live list picked {Escaped(ticker)} before this one, on the plan that night's rule traded: planned at that night's close, bought at the next session's open and followed to its target, its stop, or the end of its {EquityBrief.Core.Returns.ForwardReturnSeries.SetupSessionCap} sessions. The line runs from the stop in green to the target in orange with the buy between them, and the dot is where the price stood on the night this page draws, hollow while the trade was open and filled where it finished. A result is what the trade made in multiples of what it risked."),
                    "What these trades did is a record of the list's picks, not a forecast for this one, and nothing on the page is decided by it. How every pick has done is on the Past picks screen.")));
        }

        Card("plan", "plan", "The plan", Cards.Computed("The plan", planned.ToString(), title: "Entry and exit plan", stamp: Cards.Night(session), id: "plan", region: "plan", role: Cards.Buy));

        // The earnings reaction record: the stock's typical earnings move over every print the store holds, and what
        // each print over the calendar's year behind did on the session it moved, drawn against it.
        // see: Each print's reaction is read from the nightly calendar and the stored bars, and the earnings drift is the one rule that reads it
        if (reactions is not null)
        {
            Card("reactions", "reactions", "Earnings reactions", Cards.Computed(
                "Earnings reactions",
                marks.TypicalMoveLine(ticker, history?.Typical, history?.TypicalPull, history?.TypicalReason, history?.Newest ?? true)
                    + "<div class=\"fig\">" + marks.ReactionRange(ticker, reactions, history?.Typical) + "</div>"
                    + marks.ReactionsTable(ticker, reactions) + Cards.Key(
                    "How to read it.",
                    Invariant($"The stock's typical earnings move is the median size of the session's move over every report the store holds, the years pulled before the stored one among them, a fall and a rise of one size alike, and it is stated over {EquityBrief.Core.Moves.EarningsMoves.FewestPrints} reports or more. The bars are each report of the last year, drawn against that move either way. One row for every print over the last year, from the provider's earnings calendar: the day it was reported and whether before the open or after the close, the session the report moved, the earnings per share the provider says was expected and what was reported, the provider's surprise, and how far the stock closed that session from the close before it. A print reported after the close moves the next session, and one whose timing was not filed is read on its own day, as the earnings setups read it. A print with no filed estimate says so and has no surprise."),
                    "A record of what past reports did, not a forecast of the next: nothing on the page, no reason, plan or setup, reads it. A bar past the band moved further than this stock usually moves on a report."),
                title: "What each report did to the price",
                stamp: Cards.Night(session),
                id: "reactions",
                region: "reactions"));
        }

        // What was written about the company in the thirty days before the night, each article with the
        // label the labeller wrote for it, after its group by price; drawn for every member the page is
        // handed stories for.
        // see: The news labels alone name the model that wrote them
        if (news is { } stories)
        {
            Card("news", "news", "News", Cards.Computed(
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

        // The latest quarter, which section 4 lists after the card: each line against the estimate kept before the report
        // and the same quarter a year earlier, the eight newest quarters' growth, and what the numbers say with every figure
        // from the filing folded beneath. The numbers arrive already written, for the reason the event book does: what they
        // hold is stored figures and the sentences that state an absence, rather than a mark.
        // see: The latest quarter is read line by line against the estimate kept before its report and the same quarter a year earlier
        var quarterBody = new StringBuilder();

        quarterBody.Append(report is null ? string.Empty : marks.QuarterTable(ticker, report));
        quarterBody.Append(report is null ? string.Empty : Cards.Key(
            "How to read it.",
            "Each line of the newest quarter the company reported: what it reported, the analysts' average estimate kept before the report where one was, the difference in the line's own unit and in per cent of the estimate, the same quarter a year earlier and the change on it, and for gross, operating and net income their margin on revenue then and a year earlier. A rise is drawn in green and a fall in red, with its sign always written.",
            "A line beating its estimate says the quarter came in ahead of what was expected, and the change on a year says whether the business grew; neither moves the plan, which is read from the price."));
        quarterBody.Append(report is null ? string.Empty : "<div class=\"sub\">Growth on a year, the eight newest quarters</div><div class=\"fig\">" + marks.GrowthBars(ticker, report.Growth) + "</div>" + Cards.Key(
            "How to read it.",
            "Each quarter's revenue, the filled bar, and earnings a share, the outlined bar, against the same quarter a year earlier, in per cent about the zero rule. A bar above the rule rose and is drawn in green, one below it fell and is drawn in red.",
            "Growth that slows quarter by quarter reads as a business losing pace even while each quarter still grows."));
        quarterBody.Append(numbers);

        Card("quarter", "quarter", "The latest quarter", Cards.Dated(
            "The latest quarter",
            "Filed",
            filedOn,
            quarterBody + Cards.Key(
                "Where these come from.",
                "Each figure is from the company's own filing, dated as the filing is, as the newest fetch of its quarters stored it. The estimate is the analysts' average before the report.",
                "These are the company's reported results. Nothing in the plan is computed from them."),
            title: report?.Quarter is { } latest ? "The quarter to " + Cards.Day(latest.Quarter) + ", line by line" : "The latest quarter, line by line",
            filed: true,
            note: "from the filing",
            id: "quarter"),
            filedOn);

        // What management said with its results, the written section read from the newest and the previous results
        // releases, after the quarter where section 4 lists it; and from 20.3 beneath it the newest results release's four
        // answers as the local model read it, the card holding them alone where no section is written.
        // see: A release is read with every name and date in it replaced by a neutral word wherever it stands, and product names kept
        var releaseRead = report?.Release is { } shown ? marks.ReleaseReading(shown) : null;

        if (sections.FirstOrDefault(section => string.Equals(section.Section, MarkRenderer.WhatManagementSaid, StringComparison.Ordinal)) is { } managed)
        {
            Card(
                "management",
                SectionId(MarkRenderer.WhatManagementSaid),
                MarkRenderer.WhatManagementSaid,
                Cards.Dated(
                    MarkRenderer.WhatManagementSaid,
                    "Written",
                    managed.AsOf,
                    marks.WrittenSection(ticker, managed, documents) + (releaseRead ?? string.Empty),
                    section: MarkRenderer.WhatManagementSaid,
                    id: SectionId(MarkRenderer.WhatManagementSaid)),
                managed.AsOf);
        }
        else if (releaseRead is not null && report?.Release is { } alone)
        {
            Card(
                "management",
                SectionId(MarkRenderer.WhatManagementSaid),
                MarkRenderer.WhatManagementSaid,
                Cards.Dated(
                    MarkRenderer.WhatManagementSaid,
                    "Accepted",
                    alone.AcceptedOn,
                    releaseRead,
                    section: MarkRenderer.WhatManagementSaid,
                    id: SectionId(MarkRenderer.WhatManagementSaid)),
                alone.AcceptedOn);
        }

        // The segments, which section 4 lists after what management said: the segment table the stated rule made of the
        // filing's, each segment's share of revenue, what the company sells and the segment commentary where written, and
        // the filing's table as filed folded beneath.
        // see: The segment table is the segments whose revenue sums to the company's total within rounding, and the raw table where none do
        if (report?.Segments is not null || rawSegments is not null || sections.Any(section => InTheSegments.Contains(section.Section, StringComparer.Ordinal)))
        {
            var segmentBody = new StringBuilder();

            if (report?.Segments is { } segments)
            {
                segmentBody.Append(marks.SegmentsTable(segments, report.Currency));
                segmentBody.Append(segments.Clean ? "<div class=\"fig\">" + marks.ShareBars(segments) + "</div>" : string.Empty);
                segmentBody.Append(Cards.Key(
                    "How to read it.",
                    "Each segment the filing reports, its revenue for the quarter and its change on the same quarter a year earlier, its operating income where the filing states one, and its share of the company's revenue. The table stands only where the segments' revenue sums to the company's within rounding, totals, eliminations and corporate amounts left out; otherwise the filing's own table is drawn as filed with what the rows summed to.",
                    "A segment growing faster than the company is where the growth is coming from, and one shrinking is what is holding it back."));
            }

            foreach (var part in InTheSegments)
            {
                if (sections.FirstOrDefault(section => string.Equals(section.Section, part, StringComparison.Ordinal)) is { } partWritten)
                {
                    segmentBody.Append(Invariant($"<div class=\"written-part\" id=\"{SectionId(part)}\" data-section=\"{Escaped(part)}\"><div class=\"dl\"><span class=\"dl-k\">Written</span><b>{Cards.Day(partWritten.AsOf)}</b></div>"));
                    segmentBody.Append(marks.WrittenSection(ticker, partWritten, documents)).Append("</div>");
                }
            }

            segmentBody.Append(rawSegments is null ? string.Empty : "<details class=\"segments-raw\"><summary>The filing's segment table as filed</summary>" + rawSegments + "</details>");

            // Dated by the filing, or by its newest written part where no filing is stored, and marked new by the newer of
            // the two.
            var writtenOn = sections.Where(section => InTheSegments.Contains(section.Section, StringComparer.Ordinal)).Select(section => (DateOnly?)section.AsOf).Max();
            var segmentsNew = new[] { filedOn, writtenOn }.Max();

            Card("segments", "segments", "Segments", Cards.Dated(
                "Segments",
                filedOn is null && writtenOn is not null ? "Written" : "Filed",
                filedOn ?? writtenOn,
                segmentBody.ToString(),
                title: "What it sells and where the revenue comes from",
                filed: filedOn is not null,
                note: filedOn is null ? null : "from the filing",
                id: "segments"),
                segmentsNew);
        }

        // The margins of the twelve newest quarters, after the segments.
        if (report is { Margins.Count: > 0 } withMargins)
        {
            Card("margins", "margins", "Margins", Cards.Dated(
                "Margins",
                "Fetched",
                withMargins.FetchedOn,
                "<div class=\"fig\">" + marks.MarginLines(ticker, withMargins.Margins) + "</div>" + Cards.Key(
                    "How to read it.",
                    Invariant($"Gross, operating and net income as a share of revenue in each of the {withMargins.Margins.Count} newest quarters the newest fetch stored, one line each on one axis, named at its end."),
                    "Margins widening while revenue grows say each dollar of sales is earning more; margins narrowing say costs are rising faster than sales.")
                    + "<details class=\"margins-table\"><summary>The margins quarter by quarter</summary>" + marks.MarginTable(withMargins.Margins) + "</details>",
                title: "Gross, operating and net margins",
                filed: true,
                note: "from the filings",
                id: "margins"),
                withMargins.FetchedOn);
        }

        // The analysts' figures, after the margins, each labelled as theirs and dated by the fetch that kept it.
        // see: The name page draws the analysts' figures each labelled as theirs and dated by its fetch, and no written sentence states one
        if (report?.Analysts is { } analysts)
        {
            var analystBody = new StringBuilder();

            analystBody.Append(analysts.Consensus.Count > 0 ? marks.ConsensusTable(analysts, report.Currency) : "<p class=\"degraded\" data-absent=\"consensus\">The newest fetch kept no estimate trend for it, so no consensus is drawn.</p>");
            analystBody.Append(analysts.Target is { } target
                ? Invariant($"<p class=\"analyst-target\" data-target=\"{target}\">The analysts' mean target is {Figures.Price(target)}{(analysts.TargetAgainstPrice is { } against ? ", " + MarkRenderer.SignedFigure(against, 1, Invariant($"{(Math.Round(against, 1) == 0 ? 0 : against):+0.0;-0.0;0.0}%")) + " on the price drawn" : string.Empty)}{(analysts.MeanRating is { } mean ? Invariant($", and their mean rating {mean:0.00} on the provider's scale from one, a strong sell, to five, a strong buy") : string.Empty)}, as fetched on {Cards.Day(analysts.FetchedOn)}.</p>")
                : "<p class=\"degraded\" data-absent=\"target\">No fetch kept the analysts' mean target.</p>");
            analystBody.Append("<div class=\"sub\">The consensus over the last 90 days</div><div class=\"fig\">").Append(marks.EstimateTrend(analysts)).Append("</div>");
            analystBody.Append("<div class=\"sub\">Ratings by month</div><div class=\"fig\">").Append(marks.RatingBars(analysts)).Append("</div>");
            analystBody.Append(Cards.Key(
                "How to read it.",
                "Every figure here is the analysts' and not the company's: their average estimate of earnings a share and revenue for this and the next quarter and fiscal year with the range of their estimates and how many make one, how many raised or cut it over 7 and 30 days, the consensus as it stood 90, 60, 30 and 7 days before the fetch, and how many rate the stock at each grade in each month a fetch fell in. A month no fetch fell in is a dashed outline.",
                "Estimates rising before a report say the analysts expect more than they did; nothing on the page reads them, and no written section states one."));

            Card("analysts", "analysts", "Analysts", Cards.Dated(
                "Analysts",
                "Fetched",
                analysts.FetchedOn,
                analystBody.ToString(),
                title: "What the analysts expect, labelled as theirs",
                filed: true,
                note: "the analysts' figures",
                id: "analysts"),
                analysts.FetchedOn);
        }

        // The insiders' open-market trades, after the analysts, from 19.13: the year's purchases and sales as filed, the
        // totals over 90 days and the year, and the line counting the awards, exercises and withholding neither counts; or
        // that the store holds no insider history reaching the night.
        // see: Insiders' trades are read from their Form 4 filings, and only open-market purchases and sales are counted
        if (report is not null)
        {
            var insiderBody = report.Insiders is { } insiders
                ? marks.InsiderTrades(insiders) + Cards.Key(
                    "How to read it.",
                    "Each row is one open-market purchase, code P, or sale, code S, an insider of the company reported on a Form 4 at the SEC: who, their role as the filing states it, the day traded, the day filed and the shares, price and dollars. A purchase is green and a sale red, and a sale whose filing ticks the box for a Rule 10b5-1 plan, set up in advance, is marked planned. Awards, exercises, tax withheld, gifts and every other code are counted on the last line and in neither total.",
                    "Purchases in the open market are the trades the studies find informative; a planned sale says less than an unplanned one, and nothing on the page reads either.")
                : "<p class=\"degraded\" data-absent=\"insiders\">The store holds no insider history reaching this night, so no insider's trade is drawn.</p>";

            Card("insiders", "insiders", "Insiders", Cards.Dated(
                "Insiders",
                "Filed to",
                session,
                insiderBody,
                title: "What the company's own insiders bought and sold",
                filed: true,
                note: "the insiders' Form 4 filings",
                id: "insiders"),
                session);
        }

        // The dividend's safety for a payer, after the analysts.
        // see: A payer's dividend is read for its safety against its earnings and free cash flow and against the Treasury's 10-year, with every year it raised
        if (report?.Dividend is { } dividend)
        {
            Card("dividend", "dividend", "Dividend safety", Cards.Computed(
                "Dividend safety",
                marks.DividendTable(dividend, tiles?.Price, tiles?.Live == true, report.Currency)
                    + "<div class=\"sub\">The dividend a share each year</div><div class=\"fig\">" + marks.DividendBars(dividend) + "</div>"
                    + "<div class=\"sub\">The yield against the 10-year</div><div class=\"fig\">" + marks.YieldLines(dividend) + "</div>"
                    + Cards.Key(
                        "How to read it.",
                        "The rate is the forward annual dividend a share the company's newest fetch files, and the yield is it over the price the masthead draws. The payout on earnings is the rate over the last four quarters' earnings a share, and the payout on free cash flow the dividends paid over the last four quarters over their free cash flow. The bars are the dividends a share each year the store kept, a raise in green, a cut in red and a year level or not compared in grey, the year so far starred; the lines are the year's dividends over each session's close beside the Treasury's own 10-year par yield.",
                        "A payout under the earnings and the cash leaves room to keep paying; one above either is paid from something else, and a yield above the 10-year is paid for the risk of holding a stock."),
                title: "Whether the dividend is covered",
                stamp: Cards.Night(session),
                id: "dividend",
                region: "dividend"));
        }

        // The valuation, after the dividend: the night's multiple against its own quarters' and its industry's members'.
        // see: The valuation reads a stock against its own quarters and its industry's S&P 500 members from the night's readings alone
        if (report?.Valuation is { } valuation && (valuation.Quarters.Count > 0 || valuation.Peers is not null || valuation.Multiple is not null))
        {
            var valued = new StringBuilder();

            valued.Append(valuation.Multiple is { } multiple
                ? Invariant($"<p class=\"valuation-line\" data-multiple=\"{multiple}\" data-position=\"{Escaped(valuation.Position ?? "none")}\">At the night's close it trades at {multiple:0.0} times its last four quarters' earnings, against {valuation.Low:0.0} to {valuation.High:0.0} over its own quarters: {Escaped(valuation.Position ?? "not placed")}.</p>")
                : Invariant($"<p class=\"degraded\" data-absent=\"multiple\">No multiple was read on the night: {Escaped(valuation.NotRead ?? "the night stored no reading of its quarters")}.</p>"));
            valued.Append("<div class=\"fig\">").Append(marks.MultipleBand(ticker, valuation)).Append("</div>");

            if (valuation.Peers is { } peerValues)
            {
                valued.Append(Invariant($"<div class=\"sub\">Against {Escaped(peerValues.Industry)}'s S&amp;P 500 members on the night</div><div class=\"fig\">")).Append(marks.PeerDots(ticker, peerValues)).Append("</div>");
                valued.Append(marks.PeerTable(peerValues));
            }

            valued.Append(Cards.Key(
                "How to read it.",
                "Each quarter's multiple is the close after its report over its four quarters' earnings a share, from one fetch, and the band is their range; the dot at the right is the night's close over the newest four quarters' earnings. Beneath, each S&P 500 member sharing the stock's industry on the night, with the stock's own row marked: its multiple as the night read it, its dividend's yield at its close and its newest quarter's sales on a year earlier, and the median of each over the members holding it.",
                "A multiple near the bottom of its own range or of its industry's says the market pays less for its earnings than it has or than it does for its peers; it is not a forecast, and nothing on the page is decided by it."));

            Card("valuation", "valuation", "Valuation", Cards.Computed(
                "Valuation",
                valued.ToString(),
                title: "What the market pays for its earnings",
                stamp: Cards.Night(session),
                id: "valuation",
                region: "valuation"));
        }

        // The risks, the two cases they test and the industry cycle, after how the rules read the name, where section 4
        // lists them.
        Draw("risks", [MarkRenderer.TheRisks], Cards.Caution);
        Draw("two-cases", [MarkRenderer.TheTwoCases]);
        Draw("cycle", [EquityBrief.Core.Research.ClaimRules.CycleSection]);

        // The card of the family that listed the name on the night, third, after the short version.
        // see: A pick's card advises on the trade and removes no pick, and code computes every figure on it
        if (decision is not null)
        {
            Card("card", "card", "The decision card", Invariant($"<section class=\"card name-card\" id=\"card\" data-role=\"{Cards.Buy}\" data-family=\"{Escaped(decision.Family)}\" data-index=\"{Escaped(decision.Index)}\">{marks.DecisionCard(decision)}</section>"));
        }

        // The calendar, after the news: the next ninety days' results, ex-dividend date and the Fed's and the CPI's dates as a
        // timeline and a table, and the dated items a pass read out of the documents.
        // see: The calendar draws a name's next ninety days of results, ex-dividend dates and the Fed's and the CPI's dates beside the dated items the research read
        if (calendar is not null)
        {
            Card("calendar", "calendar", "Calendar", Cards.Computed(
                "Calendar",
                marks.TheCalendar(
                    ticker,
                    calendar,
                    sections.FirstOrDefault(section => string.Equals(section.Section, InTheDates, StringComparison.Ordinal)),
                    documents)
                    + Cards.Key(
                        "How to read it.",
                        "Each event of the ninety days after the night: the company's next results as the earnings calendar dates them, its next ex-dividend date, declared where the calendar carries one and otherwise estimated from its last ex-date at the payments a year its bars' steps show and drawn hollow, and each FOMC decision and CPI release the committed table dates. Beneath them, the dated items the research read out of its documents.",
                        "An event inside a hold can move the stock on its own day; a result or a Fed decision is when a plan's levels are most often broken."),
                title: "What is dated ahead",
                stamp: Cards.Night(session),
                id: "calendar",
                region: "calendar"));
        }

        // The sources, before where the research stands: every document the written sections cite, the latest quarter's
        // lines two sources state differently and what is computed rather than sourced.
        // see: A figure the filing and the provider's fetch state differently beyond rounding is listed under the sources with the filing first, and the page draws the fetch's
        Card("sources", "sources", "Sources", Cards.Computed(
            "Sources",
            marks.Sources(ticker, documents, figuresChecked ?? [], filedFactsHeld)
                + Cards.Key(
                    "How to read it.",
                    "Each document is listed with the day it was published and links to it. A line of the latest quarter the company's filing and the provider's fetch state differently by more than half a million dollars or a tenth of a per cent is listed with the filing's figure first; every region draws the fetch's, since every quarter the page reads shares one fetch's basis.",
                    "A figure listed under figures checked is one two sources disagree on, so read it with that in mind."),
            title: "What the page rests on",
            stamp: Cards.Night(session),
            id: "sources",
            region: "sources"));

        // Where the research stands, what the newest pass came to, the sections left out
        // or not written with the reason for each, and the controls with what research
        // has cost stated beside them before any is pressed.
        var research = marks.LeftOut(ticker, leftOut ?? [], researchState, notWritten, paused, pass, controls, cost);

        Card(
            "research",
            researchState?.State == "missing" ? "unwritten" : "research",
            "Where the research stands",
            researchState?.State == "missing"
                ? Invariant($"<section class=\"absent\" id=\"unwritten\"><div class=\"lbl\">Research not yet written</div><h2>The researched sections for {Escaped(ticker)} have not been written</h2>{research}</section>")
                : Cards.Computed("Research", research, title: "Where the research stands", id: "research", region: "research"));

        // The cards in section 4's order, each numbered as the contents numbers it from one, a written or filed one
        // carrying the day it is as of, which the page's script reads to mark one newer than the reader's last visit.
        foreach (var card in composed.OrderBy(card => card.Place))
        {
            onThePage.Add(new ContentsEntry(onThePage.Count + 1, card.Title, card.Id, card.Dated));
            body.Append(card.Markup);
        }

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

    // The places of the name page's regions, in the order the page draws them, which is section 4's: how to read the
    // page, the short version, the card, the latest quarter, what management said, the segments, the margins, the
    // analysts, from 19.13 the insiders, the dividend's safety, the valuation, the earnings reactions, the chart, the long
    // view, how it got here, the levels, the plan, how the rules read it, the risks, the two cases, the cycle, its group by
    // price, the news, the calendar, the sources, and where the research stands.
    // see: The name page answers a buyer's questions in the order a buyer asks them
    public static readonly string[] RegionOrder =
    [
        "how-to-read", "short", "card", "quarter", "management", "segments", "margins", "analysts", "insiders", "dividend",
        "valuation", "reactions", "chart", "long-view", "how-it-got-here", "levels", "plan", "rules", "risks", "two-cases",
        "cycle", "peers", "news", "calendar", "sources", "research",
    ];

    // Where each written section is drawn, which is section 4's order: the short version
    // at the top, what management said after the quarter, what the company sells and the
    // segment commentary inside the segments, the risks, the two cases and the cycle after how
    // the rules read the name, and the dated items with the dates. The cause of each large move
    // is drawn in the moves table, in the row of the move each sentence names, and `read-surface`
    // asserts every section figure 12.2 names is placed exactly once across these and that table.
    public static readonly string[] AtTheTop = ["The short version"];
    public static readonly string[] AfterTheQuarter = [MarkRenderer.WhatManagementSaid];
    public static readonly string[] InTheSegments = ["What the company sells", "The segment commentary"];
    public static readonly string[] AfterTheRules = [MarkRenderer.TheRisks, MarkRenderer.TheTwoCases, "The industry cycle"];
    public const string InTheDates = "The dated calendar items";
    public const string InTheMovesTable = "The cause of each large move";

    // The headline is drawn under the masthead, where the code's sentence stands until a written one is accepted.
    public const string InTheLead = "The headline";

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
    // see: A screen reads and renders, and each figure it works out at a price or from stored figures has one function in the core
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
        string selector = "",
        UniverseView? view = null)
    {
        var reading = universe ?? Universes.Large;
        var heading = "The universe: " + reading.Titled;
        var state = view?.State ?? new UniverseState();
        IReadOnlyList<UniverseCell> shown = view?.Kept ?? [.. rows
            .Where(row => trendFilter is null || (row.TrendState ?? "not classified") == trendFilter)
            .Where(row => sectorFilter is null || row.Sector == sectorFilter)];

        // The page the reader asked for, cut from the filtered rows by the
        // projection and handed here already cut. A caller that hands none is
        // drawing every filtered row, which is the whole table and what this did
        // until 5.8.
        var drawn = page ?? shown;

        var region = new StringBuilder();

        region.Append(Invariant($"<section class=\"universe\" data-universe=\"{reading.Word}\" data-names=\"{rows.Count}\" data-shown=\"{shown.Count}\" "));
        region.Append(Invariant($"data-drawn=\"{drawn.Count}\" data-page=\"{at}\" "));
        region.Append(Invariant($"data-trend-filter=\"{Escaped(trendFilter ?? "all")}\" data-sector-filter=\"{Escaped(sectorFilter ?? "all")}\">"));

        region.Append(Cards.Masthead(
            heading,
            $"<span class=\"m-screen\">{Escaped(heading)}</span>",
            night is { } on ? Invariant($"Every {reading.Name} member on the night of {on:yyyy-MM-dd}") : Invariant($"Every {reading.Name} member")));

        region.Append(selector);

        // The setup ledger of the same index, under Universe.

        // The S&P 400's and 600's members are read by the stages every member's figures need, and not by the listings,
        // the ladder or the swing readings, which read the S&P 500's alone, so their rows say so where those draw.
        if (reading != Universes.Large)
        {
            region.Append(Invariant($"<p class=\"oneline\" data-universe-reads=\"members\">The {Escaped(reading.Possessive)} {rows.Count} members carry their closes, typical moves and bands; the trend, the swing readings and the listing strip are read for the S&amp;P 500's members alone, so those columns are empty here.</p>"));
        }

        // A correction older than the age a notice is kept for is listed on the Run page, and this page links there.
        // see: A correction notice older than thirty days by the newest session it affects is folded onto the Run page
        region.Append(CorrectionIsOld(writtenBeforeTheCorrection, night)
            ? Invariant($"<p class=\"oneline corrections-link\" data-sessions=\"{writtenBeforeTheCorrection!.Count}\">A correction to listings more than {CorrectionAgeDays} days old is listed under Corrections on <a href=\"{RunRoute}\">the Run page</a>.</p>")
            : WrittenBeforeTheCorrectionLine(writtenBeforeTheCorrection));

        var kept = reading == Universes.Large ? null : reading.Word;

        region.Append(Cards.Computed(
            "Sectors",
            marks.SectorTiles(view?.Tiles ?? [.. sectors.Where(line => line.Sector != "sector not filed")], sectorFilter, trendFilter, kept, state, view?.NotFiled ?? 0, view?.LookBack ?? 0),
            stamp: Cards.Night(night),
            region: "sectors"));

        var table = new StringBuilder();

        table.Append(Guide(UniverseGuide));
        table.Append(marks.UniverseFilters(rows, trendFilter, sectorFilter, kept, state));
        table.Append(marks.UniverseControls(state, trendFilter, sectorFilter, kept, view?.ShortRead ?? reading == Universes.Large, view?.PageSizes ?? [50, 100], "all"));
        table.Append("<div class=\"tbl-wrap\">").Append(marks.UniverseTable(drawn, new UniverseSorting(MarkRenderer.SortHref(trendFilter, sectorFilter, kept, state), state.Sort, state.Descending, view?.DryUpCeiling))).Append("</div>");
        table.Append(marks.UniversePaging(shown.Count, at, pageSize > 0 ? pageSize : Math.Max(1, drawn.Count), trendFilter, sectorFilter, kept, state));
        table.Append("<p class=\"oneline\">The two right-hand columns count evenings a name appeared on the list. They say nothing about index membership, which every name here has.</p>");

        region.Append(Cards.Computed(
            "The index",
            table.ToString(),
            title: Invariant($"Every {reading.Name} member, nearest a level first"),
            lede: "The top of the table is what nearly fired. Filters, the search and the sort live in the address, so a view is a link you can keep.",
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
    public string ResearchedRegion(IReadOnlyList<ResearchedCell> rows, UniverseChoice? universe = null, string selector = "", string? sort = null)
    {
        var reading = universe ?? Universes.Large;
        var heading = "Researched: " + reading.Titled;
        var body = new StringBuilder();
        var byStale = sort == "stale";

        body.Append(Guide(ResearchedGuide));

        if (rows.Count == 0)
        {
            body.Append("<p class=\"degraded\" data-researched=\"none\">No name holds researched sections yet. A name's own page offers to write them, with what a pass has cost stated before it starts.</p>");
        }
        else
        {
            // The two orders, each a link keeping the index, the one drawn marked.
            body.Append(Invariant($"<nav class=\"researched-sort\" data-sort=\"{(byStale ? "stale" : "written")}\">Order by <a href=\"{ResearchedRoute}?universe={reading.Word}&amp;sort=written\"{(byStale ? string.Empty : " aria-current=\"true\"")}>day written</a> · <a href=\"{ResearchedRoute}?universe={reading.Word}&amp;sort=stale\"{(byStale ? " aria-current=\"true\"" : string.Empty)}>staleness</a></nav>"));
            body.Append("<div class=\"tbl-wrap\"><table class=\"researched-table\"><thead><tr><th>Name</th><th>Sector</th><th>Written</th><th>Why written</th><th>Stands</th><th class=\"r\">Since written</th><th class=\"r\">Sections</th></tr></thead><tbody>");

            foreach (var row in rows)
            {
                body.Append(Invariant($"<tr data-ticker=\"{Escaped(row.Ticker)}\" data-written=\"{Cards.Day(row.Written)}\" data-sections=\"{row.Sections}\" data-stale=\"{(row.Stale is { } stale ? (stale ? "true" : "false") : "none")}\" data-why=\"{Escaped(row.Why ?? "none")}\" data-move=\"{(row.Move is { } moved ? moved.ToString("R", CultureInfo.InvariantCulture) : "none")}\">"));
                body.Append(Invariant($"<td class=\"c-nm\"><a class=\"tk\" href=\"{ChartRoute}{Uri.EscapeDataString(row.Ticker)}\">{Escaped(row.Ticker)}</a>"));
                body.Append(row.Name is { Length: > 0 } company ? Invariant($"<span class=\"co\">{Escaped(company)}</span></td>") : "</td>");
                body.Append(Invariant($"<td>{Escaped(row.Sector ?? "sector not filed")}</td><td class=\"num\">{Cards.Day(row.Written)}</td><td>{Escaped(row.Why ?? "not read")}</td>"));
                body.Append(row.Stale switch
                {
                    true => Invariant($"<td><span class=\"status-pill failed\">stale</span> {Escaped(string.Join("; ", row.StaleLines ?? []))}</td>"),
                    false => "<td><span class=\"status-pill live\">stands</span></td>",
                    null => "<td class=\"degraded\">not read</td>",
                });
                body.Append(row.Move is { } move
                    ? Invariant($"<td class=\"r num {MarkRenderer.Hue(move * 100, 1)}\">{(Math.Round(move * 100, 1) == 0 ? 0 : move * 100):+0.0;-0.0;0.0}%</td>")
                    : "<td class=\"r degraded\">not read</td>");
                body.Append(Invariant($"<td class=\"r num\" data-of=\"{row.Of}\">{row.Sections} of {row.Of}</td></tr>"));
            }

            body.Append("</tbody></table></div>");
        }

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
    // a request that moved between them cannot be drawn twice or missed by both, the
    // settled ones a row a stock and New York day as the read surface grouped them, and
    // beneath them the passes no request names. Nothing here starts a pass: the press that
    // wrote a request started the worker's drain, and a drain that could not be started
    // leaves a request outstanding rather than losing it.
    // see: A press writes a request and starts the worker's drain as a process of its own, and a pass waits for the off-peak hours unless the operator writes it now
    public string QueueRegion(IReadOnlyList<QueuedCell> rows, QueueDays days, QueueEstimate? estimate = null, QueueStop? stopped = null, QueuePeak? peak = null)
    {
        var outstanding = rows.Where(row => row.State == Outstanding).ToArray();
        var writing = rows.Where(row => row.State == Writing).ToArray();
        var settled = rows.Count - outstanding.Length - writing.Length;

        var body = new StringBuilder();

        // Which lane would write what is queued here, stated where a reader is deciding
        // whether to ask for one, and what the choice they cannot make waits on.
        body.Append(Invariant($"<p class=\"lane-waits\" data-waits=\"local\">Report generation is set to the paid lane, and {LaneWaitsOn}.</p>"));

        // A drain that stopped on an error, said until a pass starts after it.
        // see: A drain that stops on an error writes a row of its own, and the queue page states it until a pass starts after it
        if (stopped is not null)
        {
            body.Append(Invariant($"<p class=\"drain-stopped\" data-started-at=\"{Escaped(stopped.StartedAt)}\">The drain that started at {stopped.Words} stopped on an error: {Escaped(stopped.Error)}; what is queued waits for the next drain, which a press or the next night starts.</p>"));
        }

        // What every time on the page rests on, stated once above them.
        // see: The queue page states when each request will be written in New York's time with UTC beneath the pointer
        if (estimate is not null)
        {
            body.Append(estimate.Minutes is { } minutes
                ? Invariant($"<p class=\"queue-estimate\" data-passes=\"{estimate.Passes}\" data-median-minutes=\"{minutes}\" data-longest-minutes=\"{estimate.LongestMinutes}\">{QueueTimesLine} A pass is expected to take {minutes} minutes, the median of the {estimate.Passes} {(estimate.Passes == 1 ? "pass" : "passes")} the store holds that ran to their end, and a pass starts only where the longest of them, {estimate.LongestMinutes} minutes, would end before the next peak window opens.</p>")
                : Invariant($"<p class=\"queue-estimate\" data-passes=\"0\">{QueueTimesLine} The store holds no pass that ran to its end, so it cannot estimate how long one takes, and no request behind another is given a time.</p>"));
        }

        body.Append(Invariant($"<div class=\"queue-part\" data-region=\"outstanding\" data-rows=\"{outstanding.Length}\">"));
        body.Append("<h3>Outstanding</h3>");

        if (outstanding.Length == 0)
        {
            body.Append("<p class=\"degraded\" data-outstanding=\"none\">No report is waiting. A row on tonight's list and a name's own page both offer to ask for one.</p>");
        }
        else
        {
            body.Append("<p class=\"lede\">Oldest first, which is the order the worker takes them in. Write now starts one at once, ahead of the rest, and asks first where the research model is at its peak price.</p>");
            body.Append("<div class=\"tbl-wrap\"><table class=\"queue-table\"><thead><tr><th>Name</th><th>Asked</th><th>From</th><th>Lane</th><th>When</th><th>Write now</th><th>Take it out</th></tr></thead><tbody>");

            foreach (var row in outstanding)
            {
                body.Append(Row(row));
                body.Append(When(row));
                // The press writing this one report at once. Inside a peak window it carries the window's end and the
                // price's multiple, which the shell's script states and asks about before it presses, and the read
                // surface refuses a press at peak that does not carry the confirmation.
                // see: A press writes a request and starts the worker's drain as a process of its own, and a pass waits for the off-peak hours unless the operator writes it now
                body.Append(Invariant($"<td><form class=\"now-control\" method=\"post\" action=\"{WriteNowRoute}{Uri.EscapeDataString(row.Ticker)}\" data-writes=\"{Escaped(row.Ticker)}\" data-peak=\"{(peak is null ? "false" : "true")}\""));
                body.Append(peak is null ? ">" : Invariant($" data-multiple=\"{Escaped(peak.Multiple)}\">"));
                body.Append(peak is null ? string.Empty : Invariant($"<span class=\"now-peak\" hidden>{peak.Ends}</span>"));
                body.Append("<button type=\"submit\" title=\"write this report now, ahead of the queue\">write now</button></form></td>");
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
            body.Append("<p class=\"degraded\" data-writing=\"none\">Nothing is being written. The night's requests wait for the off-peak run, five minutes after each peak window ends; a press on a name starts the worker on what is outstanding, a request asked at peak waiting for the off-peak rate, and Write now starts one at once.</p>");
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

        body.Append(SettledDays(days));
        body.Append(ByHandDays(days));

        body.Append(Cards.Key(
            "What is listed.",
            "Every report that has been asked for, from a row on tonight's list, from a name's own page or by the night for six names taken in turn across the three indices' pages, with what came of it. One name holds one outstanding request at a time, which the store enforces: a second press for a name already waiting adds nothing and says so.",
            "A request nobody has started can be written now or taken out. Write now starts that report alone at once; inside a peak window it says when the window ends and how many times the off-peak price a report costs, and starts only once that is confirmed. One the worker has claimed cannot be taken out, because what a withdrawal removes is a report that has not been generated, and the refusal says which state refused it."));

        return Invariant($"<section class=\"queue\" data-requests=\"{rows.Count}\" data-outstanding=\"{outstanding.Length}\" data-writing=\"{writing.Length}\" data-settled=\"{settled}\">")
            + Cards.Masthead(
                "Queue",
                "<span class=\"m-screen\">Report queue</span>",
                rows.Count == 0
                    ? "No report has been asked for yet"
                    : Invariant($"{outstanding.Length} outstanding, {writing.Length} being written, {settled} settled"))
            + Cards.Computed(
                "Queue",
                body.ToString(),
                title: "Reports asked for",
                lede: "A press asks for a report and starts the worker, which writes it at the off-peak rate, and the night's own requests wait for the off-peak run. Write now is the one control here that starts a pass.",
                region: "queue")
            + "</section>";
    }

    // What every time on the queue page is drawn in, stated once above them.
    // see: The queue page states when each request will be written in New York's time with UTC beneath the pointer
    public const string QueueTimesLine = "Times are New York's, marked ET, with UTC beneath the pointer.";

    // When a request's pass will start or started and will end or ended, with what that rests
    // on and both instants on the cell, so a reader of the markup reads the instants the words
    // state. The words arrive as markup, each instant marked by the read surface, and a
    // request drawn with no time says so rather than leaving the cell empty.
    // see: The queue page states when each request will be written in New York's time with UTC beneath the pointer
    static string When(QueuedCell row) =>
        row.Time is { } time
            ? Invariant($"<td class=\"q-when\" data-basis=\"{Escaped(time.Basis)}\" data-starts=\"{Escaped(time.Starts ?? string.Empty)}\" data-ends=\"{Escaped(time.Ends ?? string.Empty)}\">{time.Words}</td>")
            : "<td class=\"q-when\" data-basis=\"none\">no time is stated</td>";

    // The four cells the two waiting regions share, so a request reads the same way in
    // either, the instant it was asked at marked where the read surface marked it.
    static string Row(QueuedCell row) =>
        Invariant($"<tr data-ticker=\"{Escaped(row.Ticker)}\" data-asked-at=\"{Escaped(row.AskedAt)}\" data-state=\"{Escaped(row.State)}\">")
        + Invariant($"<td class=\"c-nm\"><a class=\"tk\" href=\"{ChartRoute}{Uri.EscapeDataString(row.Ticker)}\">{Escaped(row.Ticker)}</a></td>")
        + Invariant($"<td>{row.Asked ?? Escaped(row.AskedAt)}</td><td>{Escaped(row.AskedFrom)}</td><td>{Escaped(row.Lane)}</td>");

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
        // see: The queue page states when each request will be written in New York's time with UTC beneath the pointer
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
        IReadOnlyList<ListingCell> rows,
        int watching,
        string selectedName,
        string? selectedTicker = null,
        IReadOnlyList<ReasonRecord>? records = null,
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
        string selector = "",
        string due = "",
        IReadOnlyList<CloseToBuyRow>? closeToBuy = null,
        bool closeOpen = false,
        bool newest = true,
        string? closeHref = null)
    {
        var region = new StringBuilder();
        var byFilter = rule is { Rule: EquityBrief.Core.Shortlist.ListRules.Filter };
        var heading = "Tonight: " + Universes.Large.Titled;

        // A night whose readings of the reported quarters are stored draws its names state first, and one
        // before them in the filter's own order, so the card says which it drew.
        // see: Tonight's list is the swing filter's with improving businesses drawn first, and an evening is listed and ordered by the rule that listed it
        var byState = rows.Any(row => row.Business is not null);

        region.Append(Invariant($"<section class=\"tonight\" data-night=\"{night:yyyy-MM-dd}\" data-index=\"{index}\" data-fired=\"{fired}\" data-rule=\"{Escaped(rule?.Rule ?? EquityBrief.Core.Shortlist.ListRules.Reasons)}\" "));
        region.Append(Invariant($"data-selected=\"{Escaped(selectedTicker ?? "none")}\">"));

        region.Append(Cards.Masthead(
            heading,
            $"<span class=\"m-screen\">{Escaped(heading)}</span>",
            Invariant($"<span class=\"asof-words\">Night of {night:yyyy-MM-dd}, computed after the close{due}</span>") + Cards.NightPicker(night, held ?? [], NightRoute, "#/")));

        region.Append(selector);

        // A night before the families drew the page keeps its night card and its watch list card; on a night they drew
        // it, the tiles carry the passes, the market line the breadth, the link to what ran and the watch list's line.
        if (families is null)
        {
            region.Append(Cards.Computed(
                Invariant($"Night of {night:yyyy-MM-dd} · computed after the close"),
                marks.NightHeader(night, index, fired, market, byFilter ? listed : null)
                    + Invariant($"<p class=\"oneline\"><a href=\"{RunRoute}{night:yyyy-MM-dd}\">What ran tonight, what it cost and how the code is checked</a></p>"),
                stamp: Cards.Night(night),
                region: "night"));
        }

        region.Append(WrittenBeforeTheCorrectionLine(writtenBeforeTheCorrection));

        if (families is null)
        {
            region.Append(Cards.Computed(
                "Watch list",
                WatchLine(watching),
                title: "Shown every evening",
                lede: "These names appear whether or not they are on the list.",
                region: "watch"));
        }

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
                region.Append(marks.MarketLine(line with { RunHref = Invariant($"{RunRoute}{night:yyyy-MM-dd}"), Watching = watching, CloseHref = closeHref }));
            }

            // Tonight's top: a tile a setup, then one table of tonight's buy points across every setup, the row selected
            // drawing its plan and its card beneath it.
            // see: Selecting a row draws its plan and its card beneath the row and is no navigation
            var (tiles, points) = TopOf(families, heavyweights, line?.Open ?? true, night);
            var plan = selectedName.Length > 0 && selectedTicker is { } planned
                ? selectedName + SelectedLinks(planned, rows.Concat(close ?? []).FirstOrDefault(row => row.Ticker == planned))
                : string.Empty;

            region.Append(marks.SetupTiles(tiles));
            region.Append(BuyPointsCard(marks, points, selectedTicker, plan, night, oneShort: line is { CloseRead: true } read ? read.Close : null, closeHref: closeHref));

            // From 19.8 the stocks one gate short across every setup in one card beneath the buy points, folded to its line.
            // see: A member that missed exactly one gate and no exclusion is drawn close to a buy point nearest first, and recommends nothing
            if (closeToBuy is not null)
            {
                region.Append(marks.CloseToBuy(closeToBuy, closeOpen, newest, Universes.Large.Word));
            }

            // One card a setup, its header carrying the guide every setup card is read by in place of a key it repeated.
            // see: Every region is a card ruled down its left by its role, stating where its figures came from and how to read them
            foreach (var card in families)
            {
                region.Append(SetupCard(marks, card, night));
            }

            // The sector heavyweights' card after the swing families', its holdings, its last rebalance and its
            // next, drawn whatever the market check read.
            // see: The sector heavyweights hold the largest companies leading their sectors, rotated on the first session of each month whose stored year holds the closes their readings need
            // see: The market check closes every swing family's list together, and the sector heavyweights read none
            if (heavyweights is not null)
            {
                region.Append(Cards.Computed(
                    Invariant($"Rotation · {heavyweights.Eyebrow}"),
                    marks.HeavyweightCard(heavyweights),
                    stamp: Cards.Night(night),
                    id: "setup-" + EquityBrief.Core.Families.HeavyweightRule.Name,
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
        // nothing, which its key says. On a night the families drew the page's list each setup's card draws its own
        // behind an expander instead.
        // see: A member that missed exactly one gate and no exclusion is drawn close to a buy point nearest first, and recommends nothing
        if (families is null && byFilter && close is not null)
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
        // plan beneath the list, on the same page, and brings it into view. On a night the families drew the list, a buy
        // point's plan is drawn beneath its row in the table instead, and this region draws any other name selected.
        // see: Selecting a row draws its plan and its card beneath the row and is no navigation
        var inTheTable = families is not null && selectedTicker is { } asked && families.Any(card => card.Picks.Any(pick => pick.Row.Ticker == asked));

        if (!inTheTable && selectedName.Length > 0 && selectedTicker is { } chosen)
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
        else if (!inTheTable)
        {
            region.Append(selectedName);
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
        string? notComputed = null,
        string due = "",
        string? selectedTicker = null)
    {
        var region = new StringBuilder();
        var heading = "Tonight: " + universe.Titled;
        var query = "?" + Universes.Query + "=" + universe.Word;

        region.Append(Invariant($"<section class=\"tonight\" data-night=\"{night:yyyy-MM-dd}\" data-universe=\"{universe.Word}\" data-index-code=\"{Escaped(universe.Code)}\" data-selected=\"{Escaped(selectedTicker ?? "none")}\">"));

        region.Append(Cards.Masthead(
            heading,
            $"<span class=\"m-screen\">{Escaped(heading)}</span>",
            Invariant($"<span class=\"asof-words\">Night of {night:yyyy-MM-dd}, computed after the close{due}</span>") + Cards.NightPicker(night, held, NightRoute, "#/" + query, query)));

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
            : marks.MarketLine(line with { RunHref = Invariant($"{RunRoute}{night:yyyy-MM-dd}{query}") }));

        // Tonight's top, as the S&P 500's: a tile a setup and one table of the index's buy points across every setup, the row
        // selected, the first where the link names none, drawing its pick's card beneath it, and every row's link keeping
        // the index.
        var (tiles, points) = TopOf(families, heavyweights, line.Open, night);
        var chosen = points.Any(point => point.Pick.Row.Ticker == selectedTicker) ? selectedTicker : points.FirstOrDefault()?.Pick.Row.Ticker;

        region.Append(marks.SetupTiles(tiles));
        region.Append(BuyPointsCard(marks, points, chosen, string.Empty, night, "&" + Universes.Query + "=" + universe.Word, line.CloseRead ? line.Close : null));

        // From 19.8 the card of the stocks one gate short, which on an index whose night stores only the first part each
        // member failed says it reads none.
        region.Append(marks.CloseToBuy(line.CloseRead ? [] : null, false, false, universe.Word));

        // One card a setup, its header carrying the guide every setup card is read by in place of a key it repeated.
        // see: Every region is a card ruled down its left by its role, stating where its figures came from and how to read them
        foreach (var card in families)
        {
            region.Append(SetupCard(marks, card, night));
        }

        if (heavyweights is not null)
        {
            region.Append(Cards.Computed(
                Invariant($"Rotation · {heavyweights.Eyebrow}"),
                marks.HeavyweightCard(heavyweights),
                stamp: Cards.Night(night),
                id: "setup-" + EquityBrief.Core.Families.HeavyweightRule.Name,
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
        MonthlyRunView? monthly = null,
        CompareView? compare = null,
        ReportsView? reports = null,
        IReadOnlyList<FamilyRunRow>? setupFamilies = null,
        IReadOnlyList<FamilyRecordRow>? familyRecords = null,
        StoreCopyRead? storeCopy = null,
        string selector = "",
        string? duration = null,
        NightSpend? spend = null,
        NightProse? prose = null,
        IReadOnlyList<ReasonTrackRow>? totals = null,
        int fired = 0,
        bool listedByFilter = true,
        IReadOnlyList<DateOnly>? oldCorrections = null,
        IReadOnlyList<RouteTime>? routeTimes = null,
        DateTimeOffset? routeTimesSince = null,
        IReadOnlyList<LocalRunView>? localRuns = null)
    {
        var region = new StringBuilder();
        var heading = "Run evidence: " + Universes.Large.Titled;

        region.Append(Invariant($"<section class=\"run\" data-night=\"{night:yyyy-MM-dd}\" data-universe=\"{Universes.Large.Word}\" data-stages=\"{stages.Count}\">"));

        region.Append(Cards.Masthead(
            heading,
            $"<span class=\"m-screen\">{Escaped(heading)}</span>",
            Invariant($"<span class=\"asof-words\">Night of {night:yyyy-MM-dd}</span>") + Cards.NightPicker(night, held ?? [], RunRoute, RunRoute)));

        region.Append(selector);

        // Once sixty ordinary nights are stored under the open version, the page says so before anything else.
        region.Append(shape is { } due ? marks.ShapeDue(due) : string.Empty);

        // Health, from 19.7 the heading over how last night went and anything to worry about: the night's state from its
        // own run log rows and its tries, the four headline figures, the time its steps took, the press running the rest of
        // a night left unfinished, how long it took and the harness verdict, which Tonight drew in its header before 19.2,
        // and beside them the checklist.
        // see: A night's state is read off its own run log rows and its tries, and the pages that state it read that one state
        region.Append("<h2 class=\"run-heading\" data-heading=\"health\">Health</h2><div class=\"run-pair\">");
        region.Append(Cards.Computed(
            "Last night",
            (how is { } went ? marks.NightStatus(went) + NightPress(went) : string.Empty) + marks.NightOps(night, duration, harness),
            title: "How last night went",
            lede: "What the night's own run log says of it: its state, what it read and cost, and where its time went.",
            stamp: Cards.Night(night),
            region: "night"));

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

        // From 19.7 the live list's trades in one line linking their trade log, where a ring and tiles repeated Past picks.
        region.Append(trades is { } picked ? marks.TradesLine(picked, PicksRoute) : string.Empty);

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

        // From 19.7 the newest monthly run in the retired loop's place: its date and state, what it tested and put to you
        // for each family, each rule's verdict, links to the Loop page and paper trading, and the shape clock in one line.
        // see: A registered rule's looks and its futility are monitoring, and a change is adopted only by the operator's approval of a tested proposal
        if (monthly is { } month)
        {
            region.Append(Cards.Computed(
                "The monthly run",
                marks.MonthlyRunRegion(month, LoopRoute + "?universe=" + Universes.Large.Word, PaperRoute + "?universe=" + Universes.Large.Word),
                title: "What the monthly run tested",
                lede: "Each month the tester reads every family's proposals and every registered rule over the ledger's test years; a rule changes only when you approve a proposal that passed.",
                stamp: Cards.Night(night),
                region: "monthly-run"));
        }

        // Tonight's picks beside a version's, picks only.
        // see: Candidate conditions are registered before they are scored, and a candidate's picks are shown on the Run page while its outcomes wait for a look
        if (compare is { } compared)
        {
            region.Append(Cards.Computed(
                "Versions",
                marks.CompareRegion(compared, RunRoute + night.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), NameRoute),
                title: "Compare tonight's picks",
                lede: "What a registered version would have listed on this night, beside the live list. Picks only.",
                stamp: Cards.Night(night),
                region: "compare-picture"));
        }

        // Research, from 19.7 the heading over what research spent, the prose it wrote and the reports over the week, with
        // how each report did folded in the detail beneath.
        if (research is { } written)
        {
            region.Append("<h2 class=\"run-heading\" data-heading=\"research\">Research</h2>");
            region.Append(Cards.Computed(
                "Research",
                marks.NightResearch(night, spend, prose) + marks.ResearchRegion(written) + "<p class=\"run-reports-link\">How each report did, section by section, is folded in the detail beneath.</p>",
                title: "Written reports and what they cost",
                lede: "What the paid model spent this month against its cap, and the reports written over the last week.",
                stamp: Cards.Night(night),
                region: "research-picture"));
        }

        // From 20.1 the local model's runs under Research, newest first, each with the pin it read.
        // see: The local run is a process of its own at two slots outside the night and refuses any model file, runtime or prompt but its pin
        if (localRuns is { } runs)
        {
            region.Append(Cards.Computed(
                "Local model",
                marks.LocalModelRegion(runs),
                title: "The local model's runs",
                lede: "Each run of the local model, newest first whatever the night: what it read, the answers it refused and why, how long a call took, and the model, file, runtime and prompt it was pinned to.",
                stamp: Cards.Night(null),
                region: "local-model"));
        }

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

        // The reason totals, which Tonight drew beneath its list until 19.2: a statement about the evening rather than
        // about a name in it.
        if (totals is not null)
        {
            region.Append(Cards.Computed(
                "Reason totals",
                marks.ReasonTotals(totals, fired) + Cards.Key(
                    "One long bar.",
                    "Most of the night's fired names carry the same reason, so the evening is one thing happening to many names. Several short bars mean different things happened to a few names each.",
                    "These are the night's counts. How a reason's setups have ended over time is beneath, read against the base rate."),
                title: listedByFilter ? "Which reasons fired on the night, as context" : "Which reasons put the night's names on the list",
                lede: Invariant($"How many of the night's {fired} fired names carry each reason."),
                stamp: Cards.Night(night),
                region: "totals"));
        }

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

        region.Append(Fold("candidates", "The background versions and their statistics"));
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
                    "A provisional setup's trades are listed and followed like any other and are in no share and no record until its freeze."),
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

        region.Append(Fold("health", "What the night could not do and the code checks"));
        region.Append(Cards.Computed(
            "Stale and failed",
            marks.StaleAndFailed(stale, failed, refused, fellBack),
            title: "What the night could not do",
            stamp: Cards.Night(night),
            region: "stale"));

        region.Append(Cards.Computed(
            "Harness",
            marks.HarnessVerdicts(harness),
            title: "The last phase report",
            lede: "Four counts, kept apart and never added together.",
            region: "harness"));
        region.Append(Folded);

        // From 19.11 every page's time, kept by the read surface in memory since it started.
        region.Append(RouteTimesRegion(routeTimes, routeTimesSince));

        // From 19.5 the correction notices older than their age, folded at the page's end.
        // see: A correction notice older than thirty days by the newest session it affects is folded onto the Run page
        region.Append(CorrectionsRegion(oldCorrections, night));

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
    // From 19.7 each name's nearest setup across every family, its distance picture and its sessions to the next report;
    // the header's search adds a name here, so the page carries no box of its own.
    public string WatchRegion(DateOnly? night, IReadOnlyList<WatchCell> watched, int limit, MarkRenderer? marks = null)
    {
        var region = new StringBuilder();
        var shown = night is { } day ? day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "none";

        region.Append(Invariant($"<section class=\"watch\" data-night=\"{shown}\" data-watched=\"{watched.Count}\" data-limit=\"{limit}\">"));
        region.Append(Cards.Masthead("Watch list", "<span class=\"m-screen\">Watch list</span>", Invariant($"{watched.Count} of {limit} names, night of {shown}")));

        var body = new StringBuilder();

        body.Append(Guide(WatchGuide));
        body.Append("<div class=\"watch-said\" role=\"status\"></div>");
        body.Append(watched.Count < limit
            ? Invariant($"<p class=\"watch-add-line\" data-full=\"false\">Search a name in the bar above to add it here; {limit - watched.Count} {(limit - watched.Count == 1 ? "place" : "places")} left.</p>")
            : Invariant($"<p class=\"watch-full\" data-full=\"true\">The watch list holds {limit}, its limit; take one out to add another.</p>"));

        if (watched.Count == 0)
        {
            body.Append("<p class=\"degraded\" data-watch=\"none\">No name is watched yet. Search one in the bar above, or press Watch on any name's page.</p>");
        }
        else
        {
            body.Append("<div class=\"tbl-wrap\"><table class=\"watch-table\"><thead><tr><th>#</th><th>Name</th><th>Nearest setup</th><th>Distance to its bands</th><th class=\"r\">Report in</th><th class=\"r\">Close</th><th class=\"r\">Day</th><th>Trend</th>");
            body.Append("<th class=\"r\">Reward to risk</th><th>The swing filter</th><th>Added</th><th></th></tr></thead><tbody>");

            for (var at = 0; at < watched.Count; at++)
            {
                var one = watched[at];
                var row = one.Row;

                body.Append(Invariant($"<tr data-ticker=\"{Escaped(row.Ticker)}\" data-listed=\"{(one.Listed ? "true" : "false")}\" data-nearest=\"{Escaped(one.Nearest?.Family ?? "none")}\" data-missed=\"{(one.Nearest is { } near ? near.Missed.ToString(CultureInfo.InvariantCulture) : "none")}\"><td class=\"num\">{at + 1}</td>"));
                body.Append(Invariant($"<td><a href=\"{ChartRoute}{Escaped(row.Ticker)}\"><b>{Escaped(row.Ticker)}</b></a>{(one.Company is { Length: > 0 } company ? Invariant($"<span class=\"co\">{Escaped(company)}</span>") : string.Empty)}</td>"));
                body.Append(one.Nearest is { } nearest
                    ? Invariant($"<td><span class=\"fam-tag\" data-family=\"{Escaped(nearest.Family)}\"><span class=\"fam-swatch\" data-family=\"{Escaped(nearest.Family)}\"></span>{Escaped(FamilyLabel(nearest.Family))}</span> ")
                        + (nearest.Missed == 0
                            ? "<span class=\"chip\" data-gate=\"passed\">passed tonight</span></td>"
                            : Invariant($"<span class=\"chip\" data-gate=\"{Escaped(nearest.Gate ?? string.Empty)}\" title=\"{Escaped(nearest.Words ?? string.Empty)}\">{nearest.Missed} short: {Escaped(nearest.Gate ?? string.Empty)}</span></td>"))
                    : "<td><span class=\"degraded\">no answer stored</span></td>");
                body.Append(one.Cell is { } cell && marks is not null ? Invariant($"<td>{marks.DistanceRow(cell)}</td>") : "<td><span class=\"degraded\">none</span></td>");
                body.Append(one.Cell?.SessionsUntilEarnings is { } sessions ? Invariant($"<td class=\"r num\" data-sessions=\"{sessions}\">{sessions} session(s)</td>") : "<td class=\"r\"><span class=\"degraded\">no date</span></td>");
                body.Append(row.Close is { } close ? Invariant($"<td class=\"r num\">{close:0.00}</td>") : "<td class=\"r\"><span class=\"degraded\">none</span></td>");
                body.Append(row.DayChangePct is { } change ? Invariant($"<td class=\"r num {MarkRenderer.Hue(change)}\">{(Math.Round(change, 2) == 0 ? 0 : change):+0.00;-0.00;0.00}%</td>") : "<td class=\"r\"><span class=\"degraded\">none</span></td>");
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
        EquityBrief.Core.Ledger.LedgerPath? path,
        EquityBrief.Core.Ledger.LinkTable? links = null)
    {
        static string Named(string word) => word switch
        {
            "pullback" => "Pullback",
            "breakout" => "Breakout",
            "drift" => "Earnings drift",
            "heavyweight" => "Sector heavyweights",
            EquityBrief.Core.Families.FundamentalsRule.Name => EquityBrief.Core.Families.SetupFamilies.FundamentalsFirst.Label,
            _ => word,
        };

        var region = new StringBuilder();
        var heading = "Setup ledger: " + reading.Titled;
        var families = years.Select(year => year.Family).Distinct(StringComparer.Ordinal).OrderBy(EquityBrief.Core.Families.SetupFamilies.OrderOf).ToArray();

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

        foreach (var group in years.GroupBy(year => year.Family, StringComparer.Ordinal).OrderBy(group => EquityBrief.Core.Families.SetupFamilies.OrderOf(group.Key)))
        {
            var rows = group.OrderBy(year => year.Year).ToArray();
            var newest = rows.LastOrDefault(year => year.Settled > 0);

            region.Append(Cards.Computed(
                "Setup ledger",
                (group.Key == families[0] ? Guide(LedgerGuide) : string.Empty)
                    + marks.LedgerYears(group.Key, rows)
                    + Invariant($"<p class=\"ledger-deciles-said\">{(newest is null ? "No year of the family holds a settled setup yet." : Invariant($"The edge at each tenth of {newest.Year}'s {newest.Settled} settled setups, the median drawn larger, and the cut points beneath."))}</p>")
                    + marks.LedgerDeciles(newest),
                title: Escaped(Named(group.Key)) + " setups",
                stamp: Cards.Night(night),
                region: "ledger-" + group.Key));
        }

        region.Append(Cards.Computed(
            "Setup ledger",
            MarkRenderer.LedgerTabs(families, family, reading.Word, LedgerRoute)
                + marks.LedgerSettled(settled, reading.Word, LedgerRoute)
                + marks.LedgerPath(path),
            title: family is null ? "Settled setups" : Escaped(Named(family)) + ": newest settled setups",
            stamp: Cards.Night(night),
            region: "ledger-path"));

        // From 19.12 which readings go with winners for the family the tabs above chose, as the monthly run stored it.
        // see: Which readings go with winners is worked by the monthly run and stored, and the words move no rule
        region.Append(Cards.Computed(
            "Setup ledger",
            marks.LedgerLinks(links)
                + Cards.Key(
                    "How to read it.",
                    "Each row is one of the ledger's readings on this family's settled setups: how strongly its value at entry ranked with the setup's result in each test year, the mean of those years, how many of them pointed the same way, and how large the same mean comes out with the results shuffled among the setups of each night. A reading goes with winners where higher values went with better results beyond that shuffle, and with losers where they went with worse.",
                    "The words inform only: no reading is removed and no rule or proposal changes because of them, and the loop's engines read every reading whatever its row says."),
            title: family is null ? "Which readings go with winners" : "Which readings go with winners: " + Escaped(Named(family)),
            stamp: Cards.Night(night),
            region: "reading-links"));
        region.Append("</section>");

        return region.ToString();
    }

    // How the Researched page is read, opened from its "?".
    public const string ResearchedGuide =
        "Every name holding a section a research pass wrote and the claim checker accepted, with the day its newest section was written and why: tonight's list, asked by you, a regeneration you asked for, or a pass run by hand. A report stands until a filing, a passed earnings date or a jump in the name's news is dated after a section was written, which the stock's page judges the same way; a stale report names what made it so. The move is the stock's close tonight against its close on the day the report was written, and the sections count those accepted out of the full set. A key under each figure, which the overnight queue once wrote for every name, is not counted.";

    // How the Watch list is read, opened from its "?".
    public const string WatchGuide =
        "Each name you follow, drawn every evening whether or not a list holds it. Its nearest setup is the family that passed it tonight, or the one it missed by the fewest gates, a tie going to the family first in the one order, with the first gate it missed and that gate's own words under the pointer. Beside it the distance picture places the close between the nearest bands, and the report column counts the sessions to its next report. Search a name in the bar above to add it here.";

    // How the Ledger page is read, opened from its "?".
    public const string LedgerGuide =
        "Each family's card counts its setups a year on this index, the member-sessions its loose gates passed, with the share the live rule passes, the share the night's list picked of the rows a night wrote, and the mean result and edge of the settled ones, its edge a bar a year in green above nothing and red beneath. A family whose live rule passes few of its setups is choosing, and its edge against the same plan on every member says whether the choice paid. The strip draws the edge at each tenth of the newest year's settled setups; a wide strip is a family whose setups differ a great deal, which is what the loop's engines read. Beneath, a setup's path draws its closes from about ten sessions before it to where its path ended, with its buy, its stop and its target across.";

    // The families the Loop page draws, in the one order every page lists them in.
    public static IReadOnlyList<string> LoopFamilies { get; } = EquityBrief.Core.Families.SetupFamilies.Order;

    // How many proposals a run puts to the operator that hold no word of theirs yet: at most one a family, the one the
    // Loop page offers its presses on, read as the page reads it. The menu's badge counts them over each index's newest run.
    public static int PutWithoutWord(EquityBrief.Core.Loop.LoopRunRow run, IReadOnlyList<EquityBrief.Core.Loop.LoopProposalRow> proposals, LoopDecided decided) =>
        LoopFamilies.Count(family =>
            EquityBrief.Core.Loop.LoopDecisions.PutOf([.. proposals.Where(proposal => proposal.Family == family)], one => MarkRenderer.HeldBack(run, one, decided)) is { } put
            && !decided.Decisions.Any(one => one.Run == run.RunId && one.Family == family && one.Proposal == put.Proposal));

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
        IReadOnlyList<EquityBrief.Core.Loop.LoopFindingRow>? findings = null,
        IReadOnlyList<EquityBrief.Core.Loop.LoopReadingRow>? spreads = null,
        IReadOnlyList<EquityBrief.Core.Loop.LoopModelRow>? models = null,
        IReadOnlyList<EquityBrief.Core.Loop.LoopRankRow>? ranks = null,
        LoopDecided? decided = null,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? chips = null)
    {
        static string Named(string word) => word switch
        {
            "pullback" => "Pullback",
            "breakout" => "Breakout",
            "drift" => "Earnings drift",
            "heavyweight" => "Sector heavyweights",
            EquityBrief.Core.Families.FundamentalsRule.Name => EquityBrief.Core.Families.SetupFamilies.FundamentalsFirst.Label,
            _ => word,
        };

        var region = new StringBuilder();
        var heading = "Loop: " + reading.Titled;

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
                "<p class=\"degraded\" data-loop=\"none\">No tester run is stored for this index. The tester runs by hand with 'loop-test' on each index, and each month in the monthly run.</p>",
                title: "The loop",
                stamp: Cards.Night(night),
                region: "loop-none"));
            region.Append("</section>");

            return region.ToString();
        }

        // From 19.7 the page opens on a summary a family in the one order, its rule today as chips, the proposals tested,
        // the strongest one's adjusted p as a meter against the bar with its years better and the one put to you with its
        // presses, or why nothing is and when it next can be; beneath it the one line saying what a change must pass.
        var summary = new List<LoopSummaryRow>();
        var cards = new StringBuilder();

        foreach (var family in EquityBrief.Core.Families.SetupFamilies.Order)
        {
            var own = proposals.Where(proposal => proposal.Family == family).ToArray();
            var body = new StringBuilder(marks.LoopRule(family, own.FirstOrDefault()?.Current));
            var put = decided is null ? null : EquityBrief.Core.Loop.LoopDecisions.PutOf(own, one => MarkRenderer.HeldBack(run, one, decided));
            var presses = string.Empty;

            body.Append(marks.LoopFindings(family, [.. (findings ?? []).Where(finding => finding.Family == family)]));

            foreach (var proposal in own)
            {
                var decision = decided is null ? string.Empty : marks.LoopDecision(reading.Code, run, proposal, decided, put);

                if (put is not null && proposal.Proposal == put.Proposal)
                {
                    presses = decision;
                    decision = "<p class=\"loop-pressed-above\">Its presses stand in the summary above.</p>";
                }

                body.Append(marks.LoopProposalRow(proposal, [.. tests.Where(test => test.Family == family && test.Proposal == proposal.Proposal)], decision));
            }

            summary.Add(new LoopSummaryRow(
                family,
                chips?.GetValueOrDefault(family) ?? [],
                own.Length,
                own.Where(proposal => proposal.Adjusted is not null).MinBy(proposal => proposal.Adjusted) ?? own.FirstOrDefault(),
                put,
                presses,
                put is not null ? null : LoopWaiting(family, run, own, decided)));

            body.Append(marks.LoopReadings(family, [.. (spreads ?? []).Where(spread => spread.Family == family)]));
            body.Append(marks.LoopScore(family, own, (models ?? []).FirstOrDefault(model => model.Family == family), [.. (ranks ?? []).Where(rank => rank.Family == family)], run.Month));

            cards.Append(Cards.Computed(
                "Loop",
                body.ToString(),
                title: Escaped(Named(family)),
                stamp: Cards.Night(night),
                region: "loop-" + family));
        }

        region.Append(Cards.Computed(
            "Loop",
            Guide(LoopGuide) + marks.LoopSummary(summary) + MarkRenderer.LoopChangeLine(),
            title: Invariant($"What the run for {Escaped(run.Month)} found"),
            lede: "A row a setup: the rule today, what was tested against it, the strongest proposal against the bar, and the one put to you.",
            stamp: Cards.Night(night),
            region: "loop-summary"));
        region.Append(cards);

        // What the operator decided on this index and what the apply step did, and the live alarm's periods.
        // see: An approved change is applied by the night's own build on the index it was approved on alone
        // see: The live alarm flags a rule whose edge stood under its reference's fifth percentile two periods running
        if (decided is not null)
        {
            region.Append(Cards.Computed(
                "Loop",
                marks.LoopHistory(decided),
                title: "Your decisions",
                stamp: Cards.Night(night),
                region: "loop-decisions"));
            region.Append(Cards.Computed(
                "Loop",
                marks.LoopAlarm(reading.Code, decided) + Cards.Key(
                    "The live alarm.",
                    "Each month a live rule's mean edge after costs over the trades that ended in it is read against the fifth percentile of ten thousand draws of as many of its reference's trades, the rule today's over the newest run's test years in weekly blocks; two counted months running under it flag the rule, a sector heavyweights' book read by quarter.",
                    "The rule today was chosen on those same test years, so its reference reads high and the alarm is for a rule that has broken, not one drifting a little."),
                title: "The live alarm",
                stamp: Cards.Night(night),
                region: "loop-alarm"));
        }

        region.Append("</section>");

        return region.ToString();
    }

    // How the Loop page is read, opened from its "?".
    public const string LoopGuide =
        "Each month the tester runs a set of procedures for each setup on the years before each test year and scores what they choose on that year against the rule today; a proposal is the procedure run on all finished data. The meter draws a proposal's adjusted p, its chance of a result this strong by luck corrected for every proposal tested beside it, on a scale from a ten-thousandth to one with the bar marked: green at or under the bar, red over it. A proposal passes only where all four parts hold, and the strongest that passes and states a change is put to you; nothing changes a rule until you approve it. Each proposal opens to its four parts and its test years drawn as a pair of bars a year, the rule today's above and the proposal's beneath. The rule today was chosen on these same test years, so a proposal reads understated against it.";

    // Why nothing is put to the operator for a family and when it next can be: a family the run tested nothing for or none
    // of whose proposals passed waiting on the next monthly run, the first Saturday of the month after the run's, and a
    // passing proposal an earlier decline holds back waiting on a later run reading a new complete block.
    // see: A declined proposal is put again once a new complete block has been added since the decline and it passes with that block
    public static string LoopWaiting(string family, EquityBrief.Core.Loop.LoopRunRow run, IReadOnlyList<EquityBrief.Core.Loop.LoopProposalRow> own, LoopDecided? decided)
    {
        var next = NextMonthlyRun(run.Month);

        if (own.Count == 0)
        {
            return Invariant($"Nothing was tested for this setup in the run for {run.Month}; the next monthly run is on {next:yyyy-MM-dd}.");
        }

        return decided is not null && own.Any(one => one.Passed && one.Change is not null && MarkRenderer.HeldBack(run, one, decided))
            ? "Its passing proposal was declined; it is put to you again once a later run reads a new complete block of 63 sessions and it passes with that block."
            : Invariant($"{(own.Count == 1 ? "Its one proposal did not pass" : $"None of its {own.Count} proposals passed")} with a change to put to you; the next monthly run is on {next:yyyy-MM-dd}.");
    }

    // The first Saturday of the month after a run's month, the day the monthly run is scheduled on.
    public static DateOnly NextMonthlyRun(string month)
    {
        var first = DateOnly.ParseExact(month + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture).AddMonths(1);

        return first.AddDays(((int)DayOfWeek.Saturday - (int)first.DayOfWeek + 7) % 7);
    }

    // A line above an index's Tonight for each family whose live rule the alarm flags on its newest period, naming the
    // period and linking the Loop page, where a restore waits where an approval changed the rule; none where none is
    // flagged.
    // see: The live alarm flags a rule whose edge stood under its reference's fifth percentile two periods running
    public static string LoopAlarmNotice(UniverseChoice reading, IReadOnlyList<(string Family, DateOnly Period, int Streak)> flags) =>
        flags.Count == 0
            ? string.Empty
            : Invariant($"<div class=\"loop-alarm-notice\" data-flagged=\"{flags.Count}\" data-universe=\"{reading.Word}\">")
                + string.Concat(flags.Select(flag => Invariant(
                    $"<p data-family=\"{Escaped(flag.Family)}\" data-period=\"{flag.Period.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}\" data-streak=\"{flag.Streak}\"><b>The live alarm flags the {Escaped(flag.Family)} rule on the {Escaped(reading.Name)}.</b> Its edge after costs stood under its reference's low {flag.Streak} counted periods running, the newest from {flag.Period.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}. It changes nothing by itself; <a href=\"{LoopRoute}?universe={reading.Word}\">the Loop page</a> holds the periods and any restore waiting for your approval.</p>")))
                + "</div>";

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

    // The account's three settings as a form, with the line saying where they are kept, which the account's page and
    // paper trading both open with.
    // see: The account settings live in a file of their own under the data root and in nothing the store or the logs hold
    public static string AccountForm(decimal? size, decimal? riskPercent, decimal? positionCap, decimal proposedCap)
    {
        var body = new StringBuilder();

        body.Append("<div class=\"account-said\" role=\"status\"></div>");
        body.Append(Invariant($"<form class=\"account-control\" method=\"post\" action=\"{AccountPostRoute}\">"));
        body.Append(Invariant($"<label>Account size <input name=\"size\" inputmode=\"decimal\" required value=\"{(size is { } held ? held.ToString(CultureInfo.InvariantCulture) : string.Empty)}\"></label> "));
        body.Append(Invariant($"<label>Risk a trade, per cent <input name=\"risk\" inputmode=\"decimal\" required value=\"{(riskPercent is { } risk ? risk.ToString(CultureInfo.InvariantCulture) : string.Empty)}\"></label> "));
        body.Append(Invariant($"<label>Position cap, share of the account <input name=\"cap\" inputmode=\"decimal\" required value=\"{(positionCap ?? proposedCap).ToString(CultureInfo.InvariantCulture)}\"></label> "));
        body.Append("<button type=\"submit\" class=\"btn\">Save</button></form>");
        body.Append("<p class=\"degraded\">Kept on this machine in a file of their own under the data folder, never in the store, a log or an exported report.</p>");

        return body.ToString();
    }

    public string AccountRegion(decimal? size, decimal? riskPercent, decimal? positionCap, decimal proposedCap)
    {
        var region = new StringBuilder();
        var set = size is not null;

        region.Append(Invariant($"<section class=\"account\" data-set=\"{(set ? "true" : "false")}\">"));
        region.Append(Cards.Masthead("Account", "<span class=\"m-screen\">Account</span>", set ? "Your plan is sized from these" : "Not set"));
        region.Append(Cards.Computed(
            "Account",
            AccountForm(size, riskPercent, positionCap, proposedCap),
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
    public string PicksRegion(MarkRenderer marks, DateOnly? night, PicksSummary summary, IReadOnlyList<PickCell> shown, string? status, string? setup = null, IReadOnlyList<(string Family, string Label, int Trades)>? setups = null, IReadOnlyList<HeavyweightPickCell>? heavyweights = null, string selector = "", IReadOnlyList<IndexTradeCell>? businessFirst = null)
    {
        var region = new StringBuilder();
        var drawn = night is { } day ? day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "none";
        var lit = status is { } asked && PickStatus.Filters.Contains(asked, StringComparer.Ordinal) ? asked : "all";
        var heading = "Past picks: " + Universes.Large.Titled;

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
            region.Append(BusinessFirstPicksCard(marks, night, businessFirst));
            region.Append(HeavyweightPicksCard(marks, night, heavyweights));
            region.Append("</section>");

            return region.ToString();
        }

        region.Append(Cards.Computed(
            "Past picks",
            marks.PicksCounts(summary) + Cards.Key(
                "How to read it.",
                Invariant($"A trade is listed on the night the live list drew it and stays open until a close reaches its target, a close falls through its stop, or {EquityBrief.Core.Returns.ForwardReturnSeries.SetupSessionCap} sessions pass. A result is what the trade made in multiples of what it risked, so <span class=\"loss\">-1</span> is a full stop-out and <span class=\"gain\">+2</span> a target twice as far away as the stop."),
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
            "The lists' trades appear here, each under the setup that listed it. A registered variant's own trades are its account's trade log, chosen above, beside its test years verdict and apart from it; none ever appears on Tonight."));

        region.Append(Cards.Computed(
            "Past picks",
            body.ToString(),
            title: "Every trade, newest first",
            lede: "Filters live in the address, so a filtered view is a link you can keep.",
            stamp: Cards.Night(night),
            region: "picks"));
        region.Append(BusinessFirstPicksCard(marks, night, businessFirst));
        region.Append(HeavyweightPicksCard(marks, night, heavyweights));
        region.Append("</section>");

        return region.ToString();
    }

    // Past picks' card of the fundamentals-first family's S&P 500 trades, which the index families' step lists and keeps
    // under the S&P 500 apart from its own families, each named with its index as the S&P 400's and 600's are, and
    // nothing where it has listed none.
    // see: The fundamentals-first family buys an improving business in an uptrend at the pullback's buy point
    static string BusinessFirstPicksCard(MarkRenderer marks, DateOnly? night, IReadOnlyList<IndexTradeCell>? trades)
    {
        if (trades is not { Count: > 0 } kept)
        {
            return string.Empty;
        }

        var open = kept.Count(trade => trade.EndedOn is null);

        return Cards.Computed(
            "Past picks",
            Invariant($"<p class=\"list-count\" data-business-first=\"{kept.Count}\" data-open=\"{open}\">{kept.Count} {Escaped(Universes.Large.Name)} fundamentals-first trade{(kept.Count == 1 ? string.Empty : "s")}, {open} open</p>")
                + marks.IndexTrades(kept, Universes.Large.Name)
                + Cards.Key(
                    "How to read it.",
                    "A trade is listed on the night the setup passes a stock and stays open until a close reaches its target, a close falls through its stop, or its sessions run out. Its result is what it made in multiples of what it risked; its cost is its round trip at the published spread for its size and price, in the same units.",
                    "The setup is read on the S&P 500 by the S&P 400's and 600's step, apart from the S&P 500's own families, and is provisional: followed, and in no record until its freeze."),
            title: Invariant($"The {Universes.Large.Possessive} fundamentals-first trades, newest first"),
            lede: "Each listed by the fundamentals-first setup on its provisional settings, named with its index.",
            stamp: Cards.Night(night),
            region: "business-first-picks");
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
        var heading = "Run evidence: " + universe.Titled;
        var query = "?" + Universes.Query + "=" + universe.Word;

        region.Append(Invariant($"<section class=\"run\" data-night=\"{night:yyyy-MM-dd}\" data-universe=\"{universe.Word}\" data-stages=\"none\">"));
        region.Append(Cards.Masthead(
            heading,
            $"<span class=\"m-screen\">{Escaped(heading)}</span>",
            Invariant($"<span class=\"asof-words\">Night of {night:yyyy-MM-dd}</span>") + Cards.NightPicker(night, held, RunRoute, RunRoute + query, query)));
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
                ? Invariant($"<li>the {Escaped(universe.Possessive)} breadth: {EquityBrief.Core.Cards.FloorShare.Percent(breadth, view.Floor)}% of its members above their 200-day average, {(view.MarketOpen ? "at or above" : "below")} its floor of {view.Floor * 100:0.#}%, so its swing lists were {(view.MarketOpen ? "open" : "closed")}</li>")
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
        var heading = "Past picks: " + universe.Titled;
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
                        $"A holding is chosen at the close of a month's rebalance, its first session or the first after it whose stored year holds the closes the readings need, bought at the next session's open, and sold at the close of a later month's rebalance where it no longer leads its sector among the {Escaped(universe.Possessive)} members, or at its last close as a member. Its result is what it made in percent beside what its sector's largest members made over the same sessions.",
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
                    "A holding is chosen at the close of a month's rebalance, its first session or the first after it whose stored year holds the closes the readings need, bought at the next session's open, and sold at the close of a later month's rebalance where it no longer leads its sector, or at its last close as a member of the index. Its result is what it made from its buy to its sale in percent, dividends counted, beside what the sector's largest companies it was chosen from made over the same sessions, each in equal part.",
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
