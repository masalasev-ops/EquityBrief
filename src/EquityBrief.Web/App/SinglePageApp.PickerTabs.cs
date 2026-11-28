using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using EquityBrief.Core.Judge;
using EquityBrief.Web.Marks;

namespace EquityBrief.Web.App;

// From 24.49 where the trades tab is: the account chosen, the page of each list, and the past night opened, each a key of
// the tab's own address, the index kept as every page keeps it.
public sealed record JudgeTradesLink(string Universe, string? Account = null, int Open = 1, int Waiting = 1, int Closed = 1, int Nights = 1, DateOnly? Night = null)
{
    public const string AccountKey = "account";

    public const string OpenKey = "open";

    public const string WaitingKey = "waiting";

    public const string ClosedKey = "closed";

    public const string NightsKey = "nights";

    public const string NightKey = "night";

    public string Href()
    {
        var parts = new List<string> { Universes.Query + "=" + Universe };

        void Add(string key, string? value)
        {
            if (value is not null)
            {
                parts.Add(key + "=" + Uri.EscapeDataString(value));
            }
        }

        Add(AccountKey, Account);
        Add(OpenKey, Open > 1 ? Open.ToString(CultureInfo.InvariantCulture) : null);
        Add(WaitingKey, Waiting > 1 ? Waiting.ToString(CultureInfo.InvariantCulture) : null);
        Add(ClosedKey, Closed > 1 ? Closed.ToString(CultureInfo.InvariantCulture) : null);
        Add(NightsKey, Nights > 1 ? Nights.ToString(CultureInfo.InvariantCulture) : null);
        Add(NightKey, Night?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        return SinglePageApp.PickerTradesRoute + "?" + string.Join("&amp;", parts);
    }
}

// From 24.49 Tonight and Paper trading each carry two folder tabs, the rules' and the AI stock picker's, the rules' the default:
// the picker's page is the AI tab of Tonight, and its trades, every list paged at ten rows and its past nights one row a night,
// the AI tab of Paper trading.
// see: The AI stock picker's trades are drawn on Paper trading's own tab, every list paged at ten rows and one past night open at a time
public sealed partial class SinglePageApp
{
    public const string PickerTabRoute = "#/tonight/ai";

    public const string PickerTradesRoute = "#/paper/ai";

    // The rows a list of the trades tab draws a page.
    public const int TradesPage = 10;

    public const string RulesPicksTab = "The rules' picks";

    public const string PickerPicksTab = "The AI stock picker's picks";

    public const string RulesTradesTab = "The rules' trades";

    public const string PickerTradesTab = "The AI stock picker's trades";

    public const string SeeItsTrades = "See its trades and every past night on Paper trading";

    // The two folder tabs atop Tonight or Paper trading, the one shown marked as the page, each keeping the index chosen.
    public static string FolderTabs(bool paper, bool ai, UniverseChoice reading)
    {
        var query = reading == Universes.Large ? string.Empty : "?" + Universes.Query + "=" + reading.Word;
        var (rulesRoute, aiRoute) = paper ? (PaperRoute, PickerTradesRoute) : (TonightRoute, PickerTabRoute);
        var (rulesWords, aiWords) = paper ? (RulesTradesTab, PickerTradesTab) : (RulesPicksTab, PickerPicksTab);

        return $"<nav class=\"folder-tabs\" data-tabs=\"{(paper ? "paper" : "tonight")}\" aria-label=\"Whose {(paper ? "trades" : "picks")}\">"
            + $"<a class=\"ft ft-rules\" data-tab=\"rules\" href=\"{rulesRoute}{query}\"{(ai ? string.Empty : " aria-current=\"page\"")}>{Escaped(rulesWords)}</a>"
            + $"<a class=\"ft ft-ai\" data-tab=\"ai\" href=\"{aiRoute}{query}\"{(ai ? " aria-current=\"page\"" : string.Empty)}>{Escaped(aiWords)}</a>"
            + "</nav>";
    }

    // The link from an account's region on the picks tab to its trades and past nights.
    static string TradesLink(UniverseChoice chosen, string account) =>
        $"<p class=\"judge-trades-link\" data-trades-link=\"{Escaped(account)}\"><a href=\"{new JudgeTradesLink(chosen.Word, account).Href()}\">{Escaped(SeeItsTrades)}</a></p>";

    // One account the trades tab offers: its key in the address, its words on the choice, and what it holds.
    public sealed record TradesAccount(string Key, string Words, JudgeVersionView Version, bool Record, bool Across);

    // The accounts the tab offers, the record first: the record's version, each earlier version, the record's fixed 5 and the
    // earlier version that picked across the three indices.
    public static IReadOnlyList<TradesAccount> TradesAccounts(JudgeView view)
    {
        var accounts = new List<TradesAccount>();

        if (view.Record is { } record)
        {
            accounts.Add(new TradesAccount("record", string.Create(CultureInfo.InvariantCulture, $"Version {record.Number}"), record, true, false));
        }

        accounts.AddRange(view.Earlier.Select(earlier => new TradesAccount(string.Create(CultureInfo.InvariantCulture, $"version-{earlier.Number}"), string.Create(CultureInfo.InvariantCulture, $"Version {earlier.Number}"), earlier, false, false)));

        if (view.FixedFive is { } five)
        {
            accounts.Add(new TradesAccount("fixed-five", "A fixed 5, its variant", five, false, false));
        }

        if (view.Across is { } across)
        {
            accounts.Add(new TradesAccount("across", "The earlier version, all 1,500 stocks", across, false, true));
        }

        return accounts;
    }

    public string JudgeTradesRegion(JudgeView view, JudgeTradesLink link, string selector = "")
    {
        var chosen = Universes.ByCode(view.Index) ?? Universes.Large;
        var accounts = TradesAccounts(view);
        var account = accounts.FirstOrDefault(one => one.Key == link.Account) ?? accounts.FirstOrDefault();
        var html = new StringBuilder();

        html.Append(CultureInfo.InvariantCulture, $"<section class=\"judge-trades\" data-index=\"{Escaped(view.Index)}\" data-account=\"{Escaped(account?.Key ?? "none")}\">");
        html.Append(Cards.Masthead("AI stock picker's trades: " + chosen.Titled, $"<span class=\"m-screen\">AI stock picker's trades: {Escaped(chosen.Titled)}</span>", view.Shown is { } night ? "Computed for " + night.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "Not run yet"));
        html.Append(selector);
        html.Append("<div class=\"ap-title\"><div><div class=\"lbl\">AI stock picker</div>");
        html.Append(CultureInfo.InvariantCulture, $"<h1>Its paper trading, {Escaped(chosen.Name)}</h1>");
        html.Append("<p class=\"lede\">Every trade your local model's picks made in their own paper account: what is open, what is waiting, what has closed, and every past night's picks. Nothing here is one of the rules' trades.</p></div>");

        if (accounts.Count > 1)
        {
            html.Append("<nav class=\"ap-ver\" aria-label=\"Which account\">");

            foreach (var one in accounts)
            {
                html.Append(CultureInfo.InvariantCulture, $"<a class=\"ap-ver-b{(one == account ? " on" : string.Empty)}\" data-account=\"{Escaped(one.Key)}\" href=\"{(link with { Account = one.Key, Open = 1, Waiting = 1, Closed = 1, Nights = 1, Night = null }).Href()}\"{(one == account ? " aria-current=\"true\"" : string.Empty)}>{Escaped(one.Words)}</a>");
            }

            html.Append("</nav>");
        }

        html.Append("</div>");

        if (account is null)
        {
            html.Append("<p class=\"empty\" data-trades=\"none\" data-summary>The AI stock picker has made no live pick on this index yet, so it holds no trade.</p></section>");

            return html.ToString();
        }

        var version = account.Version;

        html.Append(TradesTiles(view, version));
        html.Append(TradesChart(view, account));
        html.Append(OpenTrades(version, link, chosen));
        html.Append(WaitingTrades(version, link));
        html.Append(ClosedTrades(version, link));
        html.Append(PastNights(view, account, link, chosen));
        html.Append("</section>");

        return html.ToString();
    }

    // An account's return over its values as a tile draws it, none yet before it holds two.
    static string ShareOver(IReadOnlyList<(DateOnly Session, decimal Value)> values) =>
        values.Count > 1 && values[0].Value > 0m ? Figures.Share(ReturnFrom(values[^1].Value, values[0].Value)) : "None yet";

    // A comparison line read over the account's own sessions alone, so each tile and line is the same days.
    static IReadOnlyList<(DateOnly Session, decimal Value)> SameDays(IReadOnlyList<(DateOnly Session, decimal Value)> line, JudgeVersionView version) =>
        version.Values.Count == 0 ? [] : [.. line.Where(point => point.Session >= version.Values[0].Session)];

    static string TradesTiles(JudgeView view, JudgeVersionView version)
    {
        IReadOnlyList<(DateOnly Session, decimal Value)> Line(int at) => view.Lines.Count > at ? SameDays(view.Lines[at].Values, version) : [];

        var open = version.Picks.Count(pick => pick.Held is { Skipped: null, Ended: null });
        var started = version.Values.Count > 0 ? version.Values[0].Session.ToString("d MMM", CultureInfo.InvariantCulture) : "its first night";
        var tiles = new StringBuilder("<section class=\"ap-tiles\" data-tiles>");

        tiles.Append(CultureInfo.InvariantCulture, $"<div class=\"t\" data-tile=\"account\"><span class=\"k\">Its account</span><b>{ShareOver(version.Values)}</b><span class=\"s\">since {Escaped(started)}</span></div>");
        tiles.Append(CultureInfo.InvariantCulture, $"<div class=\"t\" data-tile=\"rules\"><span class=\"k\">The rules' trades</span><b>{ShareOver(Line(1))}</b><span class=\"s\">same days, same sizing</span></div>");
        tiles.Append(CultureInfo.InvariantCulture, $"<div class=\"t\" data-tile=\"random\"><span class=\"k\">Random picks</span><b>{ShareOver(Line(2))}</b><span class=\"s\">the middle of 1,000 random accounts</span></div>");
        tiles.Append(CultureInfo.InvariantCulture, $"<div class=\"t\" data-tile=\"index\"><span class=\"k\">Just holding the index</span><b>{ShareOver(Line(3))}</b><span class=\"s\">over the same days</span></div>");
        tiles.Append(CultureInfo.InvariantCulture, $"<div class=\"t\" data-tile=\"trades\"><span class=\"k\">Trades</span><b>{open} open · {version.Score.Closed} closed</b><span class=\"s\">{version.Score.Closed} of {version.Score.Needed} closed for a firm verdict</span></div>");
        tiles.Append("</section>");

        return tiles.ToString();
    }

    static string TradesChart(JudgeView view, TradesAccount account)
    {
        var version = account.Version;
        JudgeLineView[] lines =
        [
            new JudgeLineView(account.Record ? JudgeLineNames[0] : account.Words + ", its own account", version.Values),
            .. view.Lines.Skip(1).Select(line => line with { Values = SameDays(line.Values, version) }),
        ];

        return "<section class=\"card\" data-region=\"trades-chart\"><div class=\"card-h\"><div><div class=\"lbl\">Its account over time</div><h2>The picker against the rules, random picks and the index</h2></div></div><div class=\"card-b\">"
            + JudgeChart(lines, [], "The account against the rules, random picks and the index", AccountLineWaits)
            + "<p class=\"pp-note\">Each line is the same money with the same sizing and costs, from the account's first night. Nights replayed from before the picker started are not drawn here.</p></div></section>";
    }

    // One list's page of rows, its pager and its rows, the page asked for held to the pages the list has.
    static (int Page, T[] Rows) PageOf<T>(IReadOnlyList<T> rows, int asked)
    {
        var pages = Math.Max(1, (rows.Count + TradesPage - 1) / TradesPage);
        var page = Math.Clamp(asked, 1, pages);

        return (page, [.. rows.Skip((page - 1) * TradesPage).Take(TradesPage)]);
    }

    // "1 to 10 of 23" with the carousels' two arrows, each a link to the page before or after, or a mark where there is none.
    static string Pager(string list, int page, int count, Func<int, string> href)
    {
        if (count == 0)
        {
            return string.Empty;
        }

        var pages = (count + TradesPage - 1) / TradesPage;
        var from = ((page - 1) * TradesPage) + 1;
        var to = Math.Min(count, page * TradesPage);

        string Arrow(int other, string words, string mark) =>
            other >= 1 && other <= pages && other != page
                ? $"<a class=\"ap-arrow\" href=\"{href(other)}\" aria-label=\"{words}\">{mark}</a>"
                : $"<span class=\"ap-arrow off\" aria-hidden=\"true\">{mark}</span>";

        return string.Create(CultureInfo.InvariantCulture, $"<div class=\"ap-pager\" data-pager=\"{list}\"><span>{from} to {to} of {count}</span>")
            + Arrow(page - 1, "Previous page", "&#8249;")
            + Arrow(page + 1, "Next page", "&#8250;")
            + "</div>";
    }

    static string Stock(JudgeCardView pick) =>
        $"<td><b><a href=\"{Marks.MarkRenderer.ChartRoute}{Escaped(pick.Ticker)}\">{Escaped(pick.Ticker)}</a></b>{(pick.Company is { Length: > 0 } company ? $"<small>{Escaped(company)}</small>" : string.Empty)}</td>";

    static string DayOf(DateOnly? day) => day?.ToString("d MMM", CultureInfo.InvariantCulture) ?? string.Empty;

    static string List(string list, string label, string heading, string? lede, string head, IEnumerable<string> rows, string pager, string empty) =>
        $"<section class=\"card\" data-list=\"{list}\"><div class=\"card-h\"><div><div class=\"lbl\">{Escaped(label)}</div><h2>{Escaped(heading)}</h2>{(lede is null ? string.Empty : $"<p class=\"lede\">{Escaped(lede)}</p>")}</div></div><div class=\"card-b\">"
        + (rows.Any()
            ? $"{pager}<div class=\"tbl-wrap\"><table class=\"list-table ap-tbl\"><thead><tr>{head}</tr></thead><tbody>{string.Concat(rows)}</tbody></table></div>"
            : $"<p class=\"empty\">{Escaped(empty)}</p>")
        + "</div></section>";

    // Open now: each bought and not yet sold, with its plan's prices from the price it paid, its newest close and how long it
    // has been held of its limit.
    static string OpenTrades(JudgeVersionView version, JudgeTradesLink link, UniverseChoice chosen)
    {
        JudgeCardView[] open = [.. version.Picks.Where(pick => pick.Held is { Skipped: null, Ended: null }).OrderByDescending(pick => pick.Night).ThenBy(pick => pick.Rank)];
        var (page, rows) = PageOf(open, link.Open);

        string Row(JudgeCardView pick)
        {
            var held = pick.Held!;
            var whole = string.Equals(pick.Shape, JudgeMenu.Whole, StringComparison.Ordinal);
            var target = held.Target ?? pick.Target;
            double? since = held.Now is { } now && held.Paid > 0m ? Core.Prices.Statistic.FromRatio((now / held.Paid) - 1m) : null;
            var limit = JudgeMenu.HoldOf(pick.Hold)?.Sessions;

            return "<tr data-row>" + Stock(pick)
                + $"<td>{DayOf(pick.Night)}</td>"
                + $"<td>{DayOf(held.Bought)} at {Dollars(held.Paid)}</td>"
                + $"<td class=\"dn\">{Dollars(held.Stop ?? pick.Stop)}</td>"
                + $"<td class=\"up\">{(whole ? Dollars(target) : "half at " + Dollars(target) + ", then trails")}</td>"
                + $"<td>{(held.Now is { } close ? Dollars(close) : "none yet")}</td>"
                + $"<td class=\"{(since < 0 ? "dn" : "up")}\">{(since is { } made ? Figures.Share(made) : "none yet")}</td>"
                + string.Create(CultureInfo.InvariantCulture, $"<td>{held.Days ?? 0} of {(limit is { } sessions ? sessions.ToString(CultureInfo.InvariantCulture) : "its")} days</td>")
                + $"<td><a class=\"why\" href=\"{(link with { Night = pick.Night }).Href()}\">{Escaped(WhyTab)}</a></td>"
                + "</tr>";
        }

        return List(
            "open",
            "Open now",
            string.Create(CultureInfo.InvariantCulture, $"{open.Length} open trade{(open.Length == 1 ? string.Empty : "s")}"),
            null,
            "<th>Stock</th><th>Picked</th><th>Bought</th><th>Stop</th><th>Take-profit</th><th>Last close</th><th>Since bought</th><th>Held</th><th></th>",
            rows.Select(Row),
            Pager("open", page, open.Length, to => (link with { Open = to }).Href()),
            "No trade of this account is open now.");
    }

    // The session its order's last day falls on: the order's sessions counted from the open after its night.
    static DateOnly OrderEnds(DateOnly night)
    {
        var day = night;

        for (var session = 0; session < Core.Loop.BuyRanges.OrderSessions; session++)
        {
            day = Open(day);
        }

        return day;
    }

    // Where a pick not bought stands: waiting within its order, or not bought and why.
    public static string WhereItStands(JudgeCardView pick)
    {
        var ceiling = pick.Range is { } range ? Dollars(Core.Prices.Statistic.ToPrice(range.Ceiling)) : null;
        decimal? floor = pick.Range is { Floor: { } low } ? Core.Prices.Statistic.ToPrice(low) : null;

        return pick.Held switch
        {
            null when pick.Range is not null && pick.Night is { } night => pick.Opened
                ? $"<span class=\"ap-st wait\">Waiting</span> opened above; buys if it dips back to {ceiling} by {DayOf(OrderEnds(night))}"
                : $"<span class=\"ap-st wait\">Waiting</span> buys at the open if it opens in its range, or if it dips back to {ceiling} by {DayOf(OrderEnds(night))}",
            null => "<span class=\"ap-st wait\">Waiting</span> buys at the next open, not yet read by a night",
            { Skipped: Core.Cards.PaperAccount.UnderTheCash } => "<span class=\"ap-st no\">Not bought</span> no cash left in the account that night",
            { Skipped: JudgeAccounts.OpenedBelow } when floor is { } under => decimal.Round(under, 2) == decimal.Round(pick.Stop, 2)
                ? $"<span class=\"ap-st no\">Not bought</span> opened below {Dollars(under)}, under its stop"
                : $"<span class=\"ap-st no\">Not bought</span> opened below {Dollars(under)}, too far below the buy",
            { Skipped: JudgeAccounts.OpenedAbove } => $"<span class=\"ap-st no\">Not bought</span> opened above {ceiling} and did not dip back to it in time",
            { Skipped: { } why } => $"<span class=\"ap-st no\">Not bought</span> {Escaped(why)}",
            _ => string.Empty,
        };
    }

    static string WaitingTrades(JudgeVersionView version, JudgeTradesLink link)
    {
        JudgeCardView[] waiting = [.. version.Picks.Where(pick => pick.Held is null or { Skipped: not null }).OrderByDescending(pick => pick.Night).ThenBy(pick => pick.Rank)];
        var (page, rows) = PageOf(waiting, link.Waiting);
        var within = waiting.Count(pick => pick.Held is null);

        string Range(JudgeCardView pick) => pick.Range is { } range
            ? range.Floor is { } low
                ? $"{Dollars(Core.Prices.Statistic.ToPrice(low))} and {Dollars(Core.Prices.Statistic.ToPrice(range.Ceiling))}"
                : $"up to {Dollars(Core.Prices.Statistic.ToPrice(range.Ceiling))}"
            : "at the open";

        return List(
            "waiting",
            "Waiting or not bought",
            string.Create(CultureInfo.InvariantCulture, $"{within} waiting within their {Core.Loop.BuyRanges.OrderSessions} days · {waiting.Length - within} not bought"),
            null,
            "<th>Stock</th><th>Picked</th><th>Buys between</th><th>Where it stands</th>",
            rows.Select(pick => "<tr data-row>" + Stock(pick) + $"<td>{DayOf(pick.Night)}</td><td>{Range(pick)}</td><td>{WhereItStands(pick)}</td></tr>"),
            Pager("waiting", page, waiting.Length, to => (link with { Waiting = to }).Href()),
            "No pick of this account is waiting or went unbought.");
    }

    // Why a closed trade sold, as its plan's walk ended it: under its stop, at its take-profit, on its trailing give-up price or
    // at its time limit; none where the walk named none. A sale fills at the next open, so its price alone cannot say why.
    public static string WhySold(JudgeHeld held) => held.EndedBy switch
    {
        Core.Loop.PlanPoints.Stopped => "closed under its stop",
        Core.Loop.PlanPoints.Reached => "reached its take-profit",
        Core.Loop.PlanPoints.Trailed => "sold on its trailing give-up price",
        Core.Loop.PlanPoints.Timed => "reached its time limit",
        _ => "sold",
    };

    static string ClosedTrades(JudgeVersionView version, JudgeTradesLink link)
    {
        JudgeCardView[] closed = [.. version.Picks.Where(pick => pick.Held is { Skipped: null, Ended: not null }).OrderByDescending(pick => pick.Held!.Ended).ThenBy(pick => pick.Rank)];
        var (page, rows) = PageOf(closed, link.Closed);

        string Row(JudgeCardView pick)
        {
            var held = pick.Held!;
            double? result = held.Sold is { } sold && held.Paid > 0m ? Core.Prices.Statistic.FromRatio((sold / held.Paid) - 1m) : null;

            return "<tr data-row>" + Stock(pick)
                + $"<td>{DayOf(held.Bought)} at {Dollars(held.Paid)}</td>"
                + $"<td>{DayOf(held.Ended)}{(held.Sold is { } at ? " at " + Dollars(at) : string.Empty)}</td>"
                + $"<td>{Escaped(WhySold(held))}</td>"
                + $"<td class=\"{(result < 0 ? "dn" : "up")}\">{(result is { } made ? Figures.Share(made) : string.Empty)}</td>"
                + $"<td class=\"{(held.Result < 0 ? "dn" : "up")}\">{(held.Result is { } per ? Dollars(per) : string.Empty)}</td>"
                + "</tr>";
        }

        return List(
            "closed",
            "Closed",
            string.Create(CultureInfo.InvariantCulture, $"{closed.Length} closed trade{(closed.Length == 1 ? string.Empty : "s")}"),
            closed.Length == 0 ? "Nothing has closed yet. Each closed trade lists here with how it ended." : null,
            "<th>Stock</th><th>Bought</th><th>Sold</th><th>Why it sold</th><th>Result</th><th>Per $1 risked</th>",
            rows.Select(Row),
            Pager("closed", page, closed.Length, to => (link with { Closed = to }).Href()),
            "Nothing has closed yet.");
    }

    // Every night the account picked on, newest first, one row a night with its picks as chips and its counts; the night the
    // address names open with its pick cards, and the rest folded, one open at a time.
    static string PastNights(JudgeView view, TradesAccount account, JudgeTradesLink link, UniverseChoice chosen)
    {
        var version = account.Version;
        var nights = version.Picks.Where(pick => pick.Night is not null).GroupBy(pick => pick.Night!.Value).OrderByDescending(group => group.Key).ToArray();
        var asked = link.Night is { } named && Array.FindIndex(nights, group => group.Key == named) is >= 0 and var at ? (at / TradesPage) + 1 : link.Nights;
        var (page, rows) = PageOf(nights, asked);
        var opened = link.Night is { } chosenNight && rows.Any(group => group.Key == chosenNight) ? chosenNight : rows.Length > 0 && link.Night is null ? rows[0].Key : (DateOnly?)null;
        var html = new StringBuilder("<section class=\"card\" data-list=\"nights\"><div class=\"card-h\"><div><div class=\"lbl\">Past nights</div><h2>Every night's picks, newest first</h2><p class=\"lede\">One row a night. Open a night to see each pick's reasons and plan, as Tonight shows them.</p></div></div><div class=\"card-b\">");

        if (nights.Length == 0)
        {
            html.Append("<p class=\"empty\">This account has picked on no night yet.</p></div></section>");

            return html.ToString();
        }

        html.Append(Pager("nights", page, nights.Length, to => (link with { Nights = to, Night = null }).Href()));

        foreach (var night in rows)
        {
            var picks = night.OrderBy(pick => pick.Rank).ToArray();
            var bought = picks.Count(pick => pick.Held is { Skipped: null });
            var marked = view.MarkedOn.TryGetValue((account.Across ? null : view.Index, night.Key), out var count) ? string.Create(CultureInfo.InvariantCulture, $"rated {count} worth buying · ") : string.Empty;
            var key = night.Key.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
            var scope = string.Create(CultureInfo.InvariantCulture, $"{account.Key}-{key}");

            html.Append(CultureInfo.InvariantCulture, $"<details class=\"ap-night\" name=\"ap-night\" data-row data-night=\"{key}\"{(night.Key == opened ? " open" : string.Empty)}>");
            html.Append(CultureInfo.InvariantCulture, $"<summary><span class=\"d\">{night.Key.ToString("ddd d MMM", CultureInfo.InvariantCulture)}</span><span class=\"chips\">{string.Concat(picks.Select(pick => $"<i>{Escaped(pick.Ticker)}</i>"))}</span><span class=\"r\">{marked}picked {picks.Length} · {bought} bought</span></summary>");
            html.Append("<div class=\"judge-ideas\">");

            for (var place = 0; place < picks.Length; place++)
            {
                var pick = picks[place];
                var own = pick.Index is { } index ? Universes.ByCode(index) ?? chosen : chosen;

                html.Append(PickCard(pick, version.Score, account.Record ? null : version, string.Create(CultureInfo.InvariantCulture, $"judge-{scope}-{place + 1}"), own));
            }

            html.Append("</div></details>");
        }

        html.Append("</div></section>");

        return html.ToString();
    }

    // The lists on the trades tab drawing more rows than a page holds, each named with its count, read off the page as drawn:
    // the rows of each list section, and the past nights' rows.
    public static IReadOnlyList<(string List, int Rows)> UnpagedLists(string page) =>
    [
        .. Regex.Matches(page, "<section class=\"card\" data-list=\"(?<list>[a-z]+)\">(?<body>.*?)</section>", RegexOptions.Singleline)
            .Select(match => (List: match.Groups["list"].Value, Rows: Regex.Count(match.Groups["body"].Value, "<(?:tr|details)[^>]* data-row[ >]")))
            .Where(list => list.Rows > TradesPage),
    ];
}
