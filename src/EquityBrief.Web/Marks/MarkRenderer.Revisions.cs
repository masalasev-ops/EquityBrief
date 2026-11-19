using System.Text;
using EquityBrief.Core.Cards;
using EquityBrief.Core.Families;

namespace EquityBrief.Web.Marks;

// Paper trading by revisions: a tile for each rule's running revision with the line
// of every running revision together against the index's fund; the Rule and Show controls; the revision Show draws with its
// running box, its plan in five plain rows, what it changed, its tiles and its four tabs one at a time; how it has done, its
// record since it started and the test that chose it never added together; its variants, each a row that unfolds to its
// own tabs; and its earlier revisions, folded. Every list draws at most ten rows a page in a stated order.
// see: A rule changes only through a new revision, a run of its registrations stating the same settings, and every trade belongs to the revision that listed it
// see: Every list of stocks on Paper trading draws at most ten rows a page, in a stated order, with the rows it shows counted above its pager
public sealed partial class MarkRenderer
{
    public const int RowsAPage = 10;

    public const string OpenTab = "open";

    public const string WaitingTab = "waiting";

    public const string ClosedTab = "closed";

    public const string LeftTab = "left";

    public static IReadOnlyList<string> RevisionTabs { get; } = [OpenTab, WaitingTab, ClosedTab, LeftTab];

    public const string RunningWords = "Running now";

    // The running revisions' tiles, one a rule, each opening its rule below, and the line of every running revision together
    // against the index's fund over the same days.
    public static string RunningTiles(IReadOnlyList<PaperAccountView> accounts, PaperCombinedView? combined, PaperFundView? fund, PaperLink link, IReadOnlyDictionary<string, IReadOnlyList<PaperBoughtView>> traded)
    {
        var html = new StringBuilder();
        var heads = accounts.Where(account => account.Head).OrderBy(account => SetupFamilies.OrderOf(account.Family)).ToArray();

        if (combined is { } all)
        {
            html.Append(Formatted($"<p class=\"running-line\" data-as-worded data-open=\"{all.Open}\">Following every running revision at once: {(all.Walk.Return is { } made ? Formatted($"{(made >= 0 ? "up" : "down")} {Math.Abs(made) * 100:0.0}%") : "no return yet")} since {all.Since:d MMM}, with {all.Open} {(all.Open == 1 ? "trade" : "trades")} open. {Escaped(fund?.Fund ?? "The index's fund")} over the same days: {(all.Walk.FundReturn is { } fundMade ? PaperPercent(fundMade) : "not read")}.</p>"));
        }

        html.Append("<div class=\"setup-tiles\" data-as-worded>");

        foreach (var head in heads)
        {
            var trades = traded.GetValueOrDefault(head.Key) ?? [];
            var open = trades.Count(trade => trade.Open is not null);
            var closed = trades.Count(trade => trade.Closed is not null);

            html.Append(Formatted($"<a class=\"revision-tile\" data-family=\"{Escaped(head.Family)}\" href=\"{Escaped((link with { Rule = head.Family, Pick = null, Tab = null, Page = 1, Unfold = null }).Href)}\" style=\"border-left-color:var(--fam-{Escaped(head.Family)}, var(--hair-2))\">"));
            html.Append(Formatted($"<span class=\"fam-tag\" data-family=\"{Escaped(head.Family)}\"><span class=\"fam-swatch\" data-family=\"{Escaped(head.Family)}\"></span>{Escaped(FamilyName(head.Family))}</span>"));
            html.Append(Formatted($"<b>{(head.Walk?.Return is { } made ? PaperPercent(made) : "no return yet")}</b>"));
            html.Append(Formatted($"<span class=\"rev-tile\">{Escaped(head.Revision?.Tag ?? "Not yet registered: its starting settings")}</span>"));
            html.Append(Formatted($"<span>{open} open · {closed} closed</span></a>"));
        }

        html.Append("</div>");

        return html.ToString();
    }

    // The Rule and Show controls: the rule, and what of it is drawn, its running revision first, then its variants each by
    // its one change, then its earlier revisions with the dates they ran.
    public static string RuleShowControls(IReadOnlyList<PaperAccountView> accounts, PaperAccountView head, string? pick)
    {
        var html = new StringBuilder();
        var heads = accounts.Where(account => account.Head).OrderBy(account => SetupFamilies.OrderOf(account.Family)).ToArray();
        var variants = accounts.Where(account => account.Family == head.Family && account.Standing == PaperAccountView.Variant).ToArray();
        var running = head.Revision;

        html.Append("<div class=\"pick-bar\" data-controls=\"rule-show\" data-as-worded>");
        html.Append(Formatted($"<label><span class=\"universe-lbl\">Rule</span> <select data-merge-key=\"{PaperLink.RuleKey}\" aria-label=\"The rule drawn below\">"));

        foreach (var one in heads)
        {
            html.Append(Formatted($"<option value=\"{Escaped(one.Family)}\"{(one.Family == head.Family ? " selected" : string.Empty)}>{Escaped(FamilyName(one.Family))}</option>"));
        }

        html.Append("</select></label>");
        html.Append(Formatted($"<label><span class=\"universe-lbl\">Show</span> <select data-merge-key=\"{PaperLink.PickKey}\" aria-label=\"What of the rule is drawn below\">"));
        html.Append(Formatted($"<option value=\"\"{(pick is null ? " selected" : string.Empty)}>{Escaped(running?.Tag ?? "Its starting settings")}</option>"));

        if (variants.Length > 0)
        {
            html.Append(Formatted($"<optgroup label=\"Variants of {Escaped(running?.Name.ToLowerInvariant() ?? "the rule")}: one change each, paper only\">"));

            foreach (var variant in variants)
            {
                html.Append(Formatted($"<option value=\"{Escaped(variant.Key)}\"{(pick == variant.Key ? " selected" : string.Empty)}>{Escaped(VariantName(variant))}{(variant.Since is { } since ? Formatted($", since {since:d MMM yyyy}") : string.Empty)}</option>"));
            }

            html.Append("</optgroup>");
        }

        var earlier = head.Revisions.Where(revision => !revision.Running).Reverse().ToArray();

        if (earlier.Length > 0)
        {
            html.Append("<optgroup label=\"Earlier revisions, no longer running\">");

            foreach (var revision in earlier)
            {
                var value = PaperLink.RevisionPrefix + revision.Number.ToString(Invariant);

                html.Append(Formatted($"<option value=\"{value}\"{(pick == value ? " selected" : string.Empty)}>{Escaped(revision.Tag)}</option>"));
            }

            html.Append("</optgroup>");
        }

        html.Append("</select></label></div>");

        return html.ToString();
    }

    // A variant named by what it changes from the rule's running revision, as its settings are drawn.
    public static string VariantName(PaperAccountView variant) =>
        Capitalised((variant.Changes ?? variant.Name).Replace(" · ", "; ", StringComparison.Ordinal));

    // The revision Show draws: the running box, the plan, what changed from the one before, its tiles and its four tabs.
    public string RevisionCard(PaperPageView page, PaperAccountView head, RevisionView? revision, PaperLink link)
    {
        var html = new StringBuilder("<div class=\"revision-card\" data-as-worded>");
        var running = revision is null || revision.Running;
        var key = running ? head.Key : EarlierKey(head.Key, revision!.Number);
        var before = revision is null ? null : head.Revisions.LastOrDefault(one => one.Number < revision.Number);
        var trades = running ? page.Traded.GetValueOrDefault(head.Key) ?? [] : [.. (page.EarlierTraded.GetValueOrDefault(head.Key) ?? []).Where(trade => trade.Version == revision!.Name)];
        var waiting = page.Waiting.GetValueOrDefault(key) ?? [];
        var left = page.Left.GetValueOrDefault(key) ?? [];

        if (running)
        {
            html.Append(Formatted($"<div class=\"running\" data-revision=\"{revision?.Number.ToString(Invariant) ?? "none"}\"><span class=\"vstate run\">{RunningWords}</span><span class=\"big\">{Escaped(revision is null ? "Its starting settings" : revision.Name + ", since " + RevisionView.Day(revision.From))}</span>"));
            html.Append(Formatted($"<span class=\"was\">Its picks are the ones on Home and Tonight.{(before is { Until: { } until } ? Formatted($" Before it: {before.Name.ToLowerInvariant()}, from {RevisionView.Ran(before.From, until)}.") : string.Empty)} No other revision of {Escaped(FamilyName(head.Family))} is trading.</span></div>"));
        }
        else
        {
            html.Append(Formatted($"<div class=\"running was-running\" data-revision=\"{revision!.Number}\"><span class=\"vstate old\">No longer running</span><span class=\"big\">{Escaped(revision.Name + ", ran " + RevisionView.Ran(revision.From, revision.Until!.Value))}</span>"));
            html.Append("<span class=\"was\">Its picks no longer show on Home and Tonight. A trade it placed and still holds stays with it and sells by its plan below.</span></div>");
        }

        if (page.Plans.GetValueOrDefault(key) is { } plan)
        {
            html.Append(PlanBox(plan, null));
            html.Append("<p class=\"plan-foot\">Until a stock is bought, its stop and take-profit are worked from the price it waits for. Once bought, they are worked again from the price it actually paid.");

            if (revision?.Changed is { } changed && before is not null)
            {
                html.Append(Formatted($" <span data-changed>Changed from {Escaped(before.Name.ToLowerInvariant())}: {Escaped(changed)}.</span>"));
            }

            html.Append("</p>");
        }

        html.Append(RevisionTiles(running ? head : null, trades, waiting));
        html.Append(Tabbed(trades, waiting, left, link.Tab, link.Page, tab => link with { Tab = tab, Page = 1 }, number => link with { Page = number }, null, null));

        return html.Append("</div>").ToString();
    }

    public static string EarlierKey(string account, int revision) => account + "#" + revision.ToString(Invariant);

    // A plan's five rows, the one a variant changes marked.
    static string PlanBox(PlanRowsView plan, string? changed)
    {
        var html = new StringBuilder("<div class=\"plan-box\" data-plan>");

        void Row(string name, string words) =>
            html.Append(Formatted($"<div class=\"k\">{Escaped(name)}</div><div class=\"v\">{Escaped(words)}</div>"));

        Row("What it looks for", plan.LooksFor);
        Row("How it buys", plan.Buys);
        Row("When it sells at a loss", plan.Loss);
        Row("When it takes profit", plan.Profit);
        Row("Time limit", plan.Limit);
        html.Append("</div>");

        return html.ToString();
    }

    // A revision's tiles: its account now and the index's fund over the same days where it runs, its open, waiting and closed
    // trades with how the closed ended, what its closed trades made per dollar risked, and the count a verdict needs.
    static string RevisionTiles(PaperAccountView? head, IReadOnlyList<PaperBoughtView> trades, IReadOnlyList<WaitingRowView> waiting)
    {
        var html = new StringBuilder("<div class=\"tiles\">");
        var closed = trades.Where(trade => trade.Closed is not null).ToArray();
        var open = trades.Count(trade => trade.Open is not null);
        var took = closed.Count(trade => trade.Closed!.Ending == PaperAccount.ReachedTarget);
        var stopped = closed.Count(trade => trade.Closed!.Ending is PaperAccount.UnderStop or PaperAccount.UnderTrail);
        double[] risks = [.. closed.Select(trade => trade.Closed!.Risks).OfType<double>()];

        if (head?.Walk is { } walk && walk.Values.Count > 0)
        {
            html.Append(Formatted($"<div data-tile=\"account\"><b>${walk.Values[^1].Value:#,0}</b><span>Account now, {(walk.Return is { } made ? PaperPercent(made) : "no return yet")}{(head.Since is { } since ? Formatted($" since {since:d MMM}") : string.Empty)}</span></div>"));
            html.Append(Formatted($"<div data-tile=\"fund\"><b>{(walk.FundReturn is { } fund ? PaperPercent(fund) : "not read")}</b><span>The index's fund over the same days</span></div>"));
        }

        html.Append(Formatted($"<div data-tile=\"counts\"><b>{open} open · {waiting.Count} waiting</b><span>{closed.Length} closed: {took} took profit, {stopped} stopped</span></div>"));
        html.Append(Formatted($"<div data-tile=\"made\"><b>{(risks.Length > 0 ? PerRisk(risks.Average()) : "none yet")}</b><span>made per $1 risked, closed trades</span></div>"));

        if (head?.CountLine is { } count)
        {
            html.Append(Formatted($"<div data-tile=\"needed\"><b>{(head.Progress is { } progress ? Formatted($"{progress.Counted} of {progress.Needed}") : closed.Length.ToString(Invariant))}</b><span>{Escaped(head.Progress is null ? count : "closed trades needed to judge it")}</span></div>"));
        }

        html.Append("</div>");

        return html.ToString();
    }

    static string PerRisk(double risks) => Formatted($"{(risks >= 0 ? "+" : "-")}${Math.Abs(risks):0.00} per $1");

    // The four tabs, one list showing at a time, each list ten rows a page in its stated order. A variant's rows each say what
    // the running revision did with the same stock.
    static string Tabbed(
        IReadOnlyList<PaperBoughtView> trades,
        IReadOnlyList<WaitingRowView> waiting,
        IReadOnlyList<LeftOrderView> left,
        string? chosen,
        int page,
        Func<string, PaperLink> tabLink,
        Func<int, PaperLink> pageLink,
        Func<string, DateOnly, string>? against,
        string? revisionName)
    {
        var tab = chosen is { } named && RevisionTabs.Contains(named) ? named : OpenTab;
        PaperBoughtView[] open = [.. trades.Where(trade => trade.Open is not null).OrderBy(NearestSale).ThenBy(trade => trade.Ticker, StringComparer.Ordinal)];
        PaperBoughtView[] closed = [.. trades.Where(trade => trade.Closed is not null).OrderByDescending(trade => trade.Ended).ThenBy(trade => trade.Ticker, StringComparer.Ordinal)];
        var html = new StringBuilder();

        html.Append(Formatted($"<div class=\"tabset\" role=\"tablist\" data-tab=\"{tab}\">"));

        foreach (var (name, words, count) in new[] { (OpenTab, "Open", open.Length), (WaitingTab, "Waiting to buy", waiting.Count), (ClosedTab, "Closed", closed.Length), (LeftTab, "Left the waiting list", left.Count) })
        {
            html.Append(Formatted($"<a class=\"chip tab\" role=\"tab\" aria-selected=\"{(name == tab ? "true" : "false")}\" aria-pressed=\"{(name == tab ? "true" : "false")}\" data-tab-name=\"{name}\" href=\"{Escaped(tabLink(name).Href)}\">{words} · {count}</a>"));
        }

        html.Append("</div>");

        switch (tab)
        {
            case OpenTab:
                html.Append("<p class=\"sub-l\">Bought and still held, each selling by the plan above. Nearest to its stop or its take-profit first.</p>");
                html.Append(Paged(open, page, "nearest to a sale first", pageLink, rows => OpenRows(rows, against, revisionName)));
                break;
            case WaitingTab:
                html.Append("<p class=\"sub-l\">Placed on earlier nights and not bought yet. Nearest to its buy price first.</p>");
                html.Append(Paged(waiting, page, "nearest to its buy price first", pageLink, rows => WaitingRows(rows, against, revisionName)));
                break;
            case ClosedTab:
                html.Append("<p class=\"sub-l\">Newest first.</p>");
                html.Append(Paged(closed, page, "newest first", pageLink, rows => ClosedRows(rows, against, revisionName)));
                break;
            default:
                html.Append(Formatted($"<p class=\"sub-l\">Orders that left the waiting list in the last {LeftOrderView.Sessions} trading days.</p>"));
                html.Append(Paged(left, page, "newest first", pageLink, rows => "<ul class=\"left-list\">" + string.Concat(rows.Select(row => Formatted($"<li class=\"{(row.Bought ? "bt" : "cx")}\" data-ticker=\"{Escaped(row.Ticker)}\">{Escaped(row.Words)}</li>"))) + "</ul>"));
                break;
        }

        return html.ToString();
    }

    // How near an open trade stands to a sale: the smaller of its distance above its stop and below its take-profit as a share
    // of its last close; none read last.
    static double NearestSale(PaperBoughtView trade)
    {
        if (trade.Open?.Now is not { } now || now <= 0m)
        {
            return double.MaxValue;
        }

        var near = double.MaxValue;

        if (trade.Stop is { } stop)
        {
            near = Math.Min(near, (double)((now - stop) / now));
        }

        if (trade.Target is { } target)
        {
            near = Math.Min(near, (double)((target - now) / now));
        }

        return near;
    }

    // A list's page of at most ten rows, the rows it shows counted above its Previous and Next; one with none says so.
    static string Paged<T>(IReadOnlyList<T> rows, int page, string order, Func<int, PaperLink> pageLink, Func<IReadOnlyList<T>, string> draw)
    {
        if (rows.Count == 0)
        {
            return "<p class=\"degraded\" data-rows=\"0\">None.</p>";
        }

        var pages = (rows.Count + RowsAPage - 1) / RowsAPage;
        var at = Math.Clamp(page, 1, pages);
        T[] shown = [.. rows.Skip((at - 1) * RowsAPage).Take(RowsAPage)];
        var first = ((at - 1) * RowsAPage) + 1;
        var html = new StringBuilder(draw(shown));

        html.Append(Formatted($"<div class=\"pager\" data-rows=\"{shown.Length}\" data-of=\"{rows.Count}\"><span>Showing {first} to {first + shown.Length - 1} of {rows.Count}, {Escaped(order)}</span><span class=\"pg\">"));
        html.Append(at > 1 ? Formatted($"<a class=\"pg-btn\" href=\"{Escaped(pageLink(at - 1).Href)}\">‹ Previous</a>") : "<span class=\"pg-btn\" aria-disabled=\"true\">‹ Previous</span>");
        html.Append(Formatted($"<b>Page {at} of {pages}</b>"));
        html.Append(at < pages ? Formatted($"<a class=\"pg-btn\" href=\"{Escaped(pageLink(at + 1).Href)}\">Next ›</a>") : "<span class=\"pg-btn\" aria-disabled=\"true\">Next ›</span>");
        html.Append("</span></div>");

        return html.ToString();
    }

    static string StockTd(string ticker, string? company) =>
        Formatted($"<td class=\"c-nm\"><a class=\"name-link\" href=\"#/chart/{Uri.EscapeDataString(ticker)}\">{Escaped(ticker)}</a>{(company is { Length: > 0 } named ? Formatted($"<span class=\"co\">{Escaped(named)}</span>") : string.Empty)}</td>");

    static string MoneyTd(decimal? price, string? note = null) =>
        price is { } at ? Formatted($"<td class=\"r num\">${Figures.Price(at)}{(note is null ? string.Empty : Formatted($"<span class=\"row-note\">{Escaped(note)}</span>"))}</td>") : Formatted($"<td class=\"r\"><span class=\"degraded\">{Escaped(note ?? "none")}</span></td>");

    // How an open or ended trade takes profit where it names no price: a rising stop, how a book sells it, or its time limit.
    static string TakesProfitTd(PaperBoughtView trade) =>
        trade.Target is { } target ? MoneyTd(target)
        : trade.Trails ? "<td class=\"r\">rising stop, no price</td>"
        : trade.Sells is { } sells ? Formatted($"<td>{Escaped(sells)}</td>")
        : Formatted($"<td>none: sells after {trade.Cap?.ToString(Invariant) ?? "its"} trading days</td>");

    static string AgainstTd(Func<string, DateOnly, string>? against, string ticker, DateOnly listed) =>
        against is null ? string.Empty : Formatted($"<td class=\"against\">{Escaped(against(ticker, listed))}</td>");

    static string AgainstTh(string? revisionName) =>
        revisionName is null ? string.Empty : Formatted($"<th>{Escaped(revisionName)}</th>");

    static string OpenRows(IReadOnlyList<PaperBoughtView> rows, Func<string, DateOnly, string>? against, string? revisionName)
    {
        var html = new StringBuilder("<div class=\"tbl-wrap\"><table class=\"list-table revision-open\"><thead><tr><th>Stock</th><th class=\"r\">Bought at</th><th class=\"r\">Sells if it closes below</th><th class=\"r\">Takes profit at</th><th class=\"r\">Last close</th><th class=\"r\">So far</th><th class=\"r\">Trading days used</th><th>Where it stands</th>" + AgainstTh(revisionName) + "</tr></thead><tbody>");

        foreach (var trade in rows)
        {
            var position = trade.Open!;

            html.Append(Formatted($"<tr data-ticker=\"{Escaped(trade.Ticker)}\" data-listed=\"{trade.Listed:yyyy-MM-dd}\">"));
            html.Append(StockTd(trade.Ticker, trade.Company));
            html.Append(Formatted($"<td class=\"r num\">${Figures.Price(trade.Buy)}<span class=\"row-note\">{trade.Bought:d MMM yyyy}</span></td>"));
            html.Append(trade.Stop is { } stop ? MoneyTd(stop) : Formatted($"<td>{Escaped(trade.Sells ?? "no price stop")}</td>"));
            html.Append(TakesProfitTd(trade));
            html.Append(MoneyTd(position.Now));
            html.Append(Formatted($"<td class=\"r num\">{(position.Risks is { } risks ? Escaped(PerRisk(risks)) + " risked" : string.Empty)}{(position.Fraction is { } fraction ? Formatted($"<span class=\"row-note\">{PaperPercent(fraction)}</span>") : string.Empty)}</td>"));
            html.Append(Formatted($"<td class=\"r num\">{(position.Held is { } held ? held.ToString(Invariant) : "-")}{(trade.Cap is { } cap ? Formatted($" of {cap}") : string.Empty)}</td>"));
            html.Append(Formatted($"<td>{Escaped(position.Standing)}{(trade.Past is not null ? "<span class=\"row-note\">It opened past its own price.</span>" : string.Empty)}</td>"));
            html.Append(AgainstTd(against, trade.Ticker, trade.Listed));
            html.Append("</tr>");
        }

        return html.Append("</tbody></table></div>").ToString();
    }

    static string WaitingRows(IReadOnlyList<WaitingRowView> rows, Func<string, DateOnly, string>? against, string? revisionName)
    {
        var html = new StringBuilder("<div class=\"tbl-wrap\"><table class=\"list-table revision-waiting\"><thead><tr><th>Stock</th><th class=\"r\">Placed</th><th class=\"r\">Buys if it falls to</th><th class=\"r\">Last close</th><th class=\"r\">Would sell if it closes below</th><th class=\"r\">Would take profit at</th><th class=\"r\">Cancelled if not bought by</th>" + AgainstTh(revisionName) + "</tr></thead><tbody>");

        foreach (var row in rows)
        {
            html.Append(Formatted($"<tr data-ticker=\"{Escaped(row.Ticker)}\" data-placed=\"{row.Placed:yyyy-MM-dd}\">"));
            html.Append(StockTd(row.Ticker, row.Company));
            html.Append(Formatted($"<td class=\"r num\">{row.Placed:d MMM}</td>"));
            html.Append(row.BuyPrice is { } buy ? MoneyTd(buy) : Formatted($"<td>{Escaped(row.Words ?? "the next open")}</td>"));
            html.Append(MoneyTd(row.LastClose, row.AboveBuy is { } above ? Formatted($"{Math.Abs(above) * 100:0.0}% {(above >= 0 ? "above" : "below")}") : null));
            html.Append(MoneyTd(row.Stop, row.Stop is null ? "worked from the price it pays" : null));
            html.Append(row.TakeProfit is { } profit ? MoneyTd(profit, row.TakeProfitIsPart ? "on part of it" : null) : MoneyTd(null, row.BuyPrice is null ? "worked from the price it pays" : "none: sells on its time limit"));
            html.Append(row.LastDay is { } last ? Formatted($"<td class=\"r num\">{last:d MMM}{(row.DaysLeft is { } days ? Formatted($"<span class=\"row-note\">{days} trading {(days == 1 ? "day" : "days")} left</span>") : string.Empty)}</td>") : "<td class=\"r\">-</td>");
            html.Append(AgainstTd(against, row.Ticker, row.Placed));
            html.Append("</tr>");
        }

        return html.Append("</tbody></table></div>").ToString();
    }

    static string ClosedRows(IReadOnlyList<PaperBoughtView> rows, Func<string, DateOnly, string>? against, string? revisionName)
    {
        var html = new StringBuilder("<div class=\"tbl-wrap\"><table class=\"list-table revision-closed\"><thead><tr><th>Stock</th><th class=\"r\">Bought</th><th class=\"r\">Sold</th><th>How it ended</th><th class=\"r\">Result</th>" + AgainstTh(revisionName) + "</tr></thead><tbody>");

        foreach (var trade in rows)
        {
            var ended = trade.Closed!;

            html.Append(Formatted($"<tr data-ticker=\"{Escaped(trade.Ticker)}\" data-listed=\"{trade.Listed:yyyy-MM-dd}\">"));
            html.Append(StockTd(trade.Ticker, trade.Company));
            html.Append(Formatted($"<td class=\"r num\">${Figures.Price(trade.Buy)}<span class=\"row-note\">{trade.Bought:d MMM}</span></td>"));
            html.Append(Formatted($"<td class=\"r num\">{(ended.Sale is { } sale ? "$" + Figures.Price(sale) : "-")}<span class=\"row-note\">{(trade.Ended is { } end ? end.ToString("d MMM", Invariant) : string.Empty)}</span></td>"));
            html.Append(Formatted($"<td>{Escaped(Capitalised(ended.Ending))}</td>"));
            html.Append(Formatted($"<td class=\"r num\">{(ended.Risks is { } risks ? Escaped(PerRisk(risks)) + " risked" : string.Empty)}{(ended.Fraction is { } fraction ? Formatted($"<span class=\"row-note\">{PaperPercent(fraction)}</span>") : string.Empty)}</td>"));
            html.Append(AgainstTd(against, trade.Ticker, trade.Listed));
            html.Append("</tr>");
        }

        return html.Append("</tbody></table></div>").ToString();
    }

    // How the revision has done: since it started running, and the test over past years that chose it, never added together.
    public string HowItHasDone(PaperPageView page, PaperAccountView head)
    {
        var html = new StringBuilder();

        html.Append(Formatted($"<div class=\"score\" data-family=\"{Escaped(head.Family)}\" data-standing=\"{head.Standing}\" data-verdict=\"{Escaped(head.Verdict)}\">"));
        html.Append("<p class=\"sub-l\" data-as-worded>Two records, never added together: its trades since it started running, and the test over past years that chose it.</p>");
        html.Append("<div class=\"score-halves\">");
        html.Append(SinceLive(head));
        html.Append(History(head));
        html.Append("</div>");
        html.Append(PooledPart(page.Pooled.FirstOrDefault(row => row.Own && row.Family == head.Family), PooledSetups(page.Pooled)));
        html.Append(LongPart(
            page.Long.FirstOrDefault(row => row.Family == head.Family && row.Standing != PaperAccountView.Variant),
            page.LongPooled.FirstOrDefault(row => row.Own && row.Family == head.Family)));
        html.Append("</div>");

        return html.ToString();
    }

    // The running revision's variants, a row each that unfolds to its one change, its tiles and its own tabs, every row
    // saying what the running revision did with the same stock.
    public string VariantsOf(PaperPageView page, PaperAccountView head, PaperLink link)
    {
        var variants = page.Accounts.Where(account => account.Family == head.Family && account.Standing == PaperAccountView.Variant).ToArray();
        var name = head.Revision?.Name ?? "The rule";

        if (variants.Length == 0)
        {
            return "<p class=\"degraded\" data-variants=\"0\">No variant is registered for this rule.</p>";
        }

        var headTrades = page.Traded.GetValueOrDefault(head.Key) ?? [];
        var headWaiting = page.Waiting.GetValueOrDefault(head.Key) ?? [];

        string Against(string ticker, DateOnly listed) =>
            headTrades.FirstOrDefault(trade => trade.Ticker == ticker && trade.Listed == listed) is { } same
                ? same.Open is not null ? "Holds it too" : Formatted($"Took it too, sold {same.Ended:d MMM}")
                : headWaiting.FirstOrDefault(order => order.Ticker == ticker && order.Placed == listed) is { } order
                    ? order.BuyPrice is { } buy ? Formatted($"Waits to buy it at ${Figures.Price(buy)}") : "Buys it at the next open"
                    : "Did not take it";

        var html = new StringBuilder("<div class=\"revision-variants\" data-as-worded>");

        html.Append(Formatted($"<p class=\"sub-l\">Each variant is {Escaped(name.ToLowerInvariant())} with one thing changed, traded only on paper and never as picks. A variant that does better can become the next revision only at a monthly run, on your word. Press a variant's row to unfold its trades under it; press again to fold it away.</p>"));
        html.Append(Formatted($"<div class=\"tbl-wrap\"><table class=\"list-table vtable\" data-variants=\"{variants.Length}\"><thead><tr><th>Variant, named by how it differs from {Escaped(name.ToLowerInvariant())} (press one to unfold it)</th><th class=\"r\">Trades</th><th class=\"r\">Trades {Escaped(name.ToLowerInvariant())} did not take</th><th class=\"r\">Result so far</th><th>On past years</th></tr></thead><tbody>"));

        foreach (var variant in variants)
        {
            var open = link.Unfold == variant.Key;
            var trades = page.Traded.GetValueOrDefault(variant.Key) ?? [];
            var alone = trades.Count(trade => Against(trade.Ticker, trade.Listed) == "Did not take it");
            double[] ended = [.. trades.Where(trade => trade.Closed?.Risks is not null).Select(trade => trade.Closed!.Risks!.Value)];
            var toggle = link with { Unfold = open ? null : variant.Key, VariantTab = null, VariantPage = 1, Pick = null };

            html.Append(Formatted($"<tr class=\"vrow{(open ? " open" : string.Empty)}\" data-account=\"{Escaped(variant.Key)}\" id=\"variant-{Escaped(variant.Key)}\">"));
            html.Append(Formatted($"<td><a class=\"vbtn\" href=\"{Escaped(toggle.Href)}\" aria-expanded=\"{(open ? "true" : "false")}\"><span class=\"car\">{(open ? "▾" : "▸")}</span><b>{Escaped(VariantName(variant))}</b></a>{(variant.Since is { } since ? Formatted($"<span class=\"row-note\">since {since:d MMM yyyy}</span>") : string.Empty)}{AccountNote(variant)}</td>"));
            html.Append(Formatted($"<td class=\"r num\">{trades.Count}</td><td class=\"r num\">{alone}</td>"));
            html.Append(Formatted($"<td class=\"r num\">{(ended.Length > 0 ? Escaped(PerRisk(ended.Average())) + " risked" : "none closed")}</td>"));
            html.Append(Formatted($"<td><span class=\"pill\">{Escaped(variant.Verdict)}</span>{(variant.Better is { } better && variant.Counted is { } counted ? Formatted($"<span class=\"row-note\">better in {better} of {counted} years</span>") : string.Empty)}</td>"));
            html.Append("</tr>");

            if (open)
            {
                var waiting = page.Waiting.GetValueOrDefault(variant.Key) ?? [];
                var left = page.Left.GetValueOrDefault(variant.Key) ?? [];

                html.Append("<tr class=\"vdetail\"><td colspan=\"5\"><div class=\"vx\">");
                html.Append(Formatted($"<div class=\"plan-box\" data-plan=\"changed\"><div class=\"k chg\">{Escaped(VariantName(variant))}<span class=\"chg-tag\">Changed</span></div><div class=\"v chg\">{Escaped(VariantName(variant))}.</div>"));
                html.Append(Formatted($"<div class=\"k\">Everything else</div><div class=\"v\">Same as {Escaped(name.ToLowerInvariant())}: what it looks for, how it buys, when it sells at a loss, when it takes profit and its time limit.</div></div>"));
                html.Append(RevisionTiles(variant, trades, waiting));
                html.Append(Tabbed(trades, waiting, left, link.VariantTab, link.VariantPage, tab => link with { VariantTab = tab, VariantPage = 1 }, number => link with { VariantPage = number }, Against, name));
                html.Append(Formatted($"<p class=\"plan-foot\">Each tab works as on {Escaped(name.ToLowerInvariant())}'s card, every row saying what {Escaped(name.ToLowerInvariant())} did with the same stock. <a href=\"{Escaped(toggle.Href)}\">Fold it away</a></p>"));
                html.Append("</div></td></tr>");
            }
        }

        html.Append("</tbody></table></div></div>");

        return html.ToString();
    }

    // The top of a stock's own page: each open trade and waiting order the rules' revisions hold on it, the revision, when it
    // was bought or placed, its price, stop and one take-profit, its days left and where it stands; nothing where none holds it.
    public static string NameTradesCard(string ticker, IReadOnlyList<NameTradeView> trades)
    {
        if (trades.Count == 0)
        {
            return string.Empty;
        }

        var html = new StringBuilder(Formatted($"<section class=\"card name-trades\" data-card=\"name-trades\" data-ticker=\"{Escaped(ticker)}\" data-trades=\"{trades.Count}\" data-as-worded><div class=\"card-b\"><h3 class=\"card-t\">{Escaped(ticker)} on the rules' paper trades</h3>"));

        html.Append("<div class=\"tbl-wrap\"><table class=\"list-table\"><thead><tr><th>Setup</th><th>State</th><th class=\"r\">Price</th><th class=\"r\">Sells if it closes below</th><th class=\"r\">Takes profit at</th><th class=\"r\">Days left</th><th>Where it stands</th></tr></thead><tbody>");

        foreach (var trade in trades)
        {
            html.Append(Formatted($"<tr data-family=\"{Escaped(trade.Family)}\" data-waiting=\"{Flag(trade.Waiting)}\"><td><span class=\"tag-stack\"><span class=\"fam-tag\" data-family=\"{Escaped(trade.Family)}\"><span class=\"fam-swatch\" data-family=\"{Escaped(trade.Family)}\"></span>{Escaped(FamilyName(trade.Family))}</span><span class=\"vtag\">{Escaped(trade.Revision)}</span></span></td>"));
            html.Append(Formatted($"<td>{(trade.Waiting ? Formatted($"Waiting to buy, placed {trade.Placed:d MMM}") : Formatted($"Bought {trade.Bought:d MMM yyyy}"))}</td>"));
            html.Append(MoneyTd(trade.Price, trade.Waiting ? "its buy price" : "paid"));
            html.Append(MoneyTd(trade.Stop));
            html.Append(MoneyTd(trade.TakeProfit, trade.TakeProfit is null ? "none: sells on its time limit" : null));
            html.Append(Formatted($"<td class=\"r num\">{(trade.DaysLeft is { } days ? days.ToString(Invariant) : "-")}</td><td>{Escaped(trade.Standing)}</td></tr>"));
        }

        return html.Append("</tbody></table></div></div></section>").ToString();
    }

    public const string StartingTonight = "If you were starting tonight";

    // The rule's earlier revisions, folded: each with the sessions it ran, what the next revision changed, its trades and what
    // they made, and the link that draws it above.
    public static string EarlierRevisionsOf(PaperPageView page, PaperAccountView head, PaperLink link)
    {
        var earlier = head.Revisions.Where(revision => !revision.Running).Reverse().ToArray();
        var html = new StringBuilder();

        html.Append(Formatted($"<details class=\"earlier-revisions\" data-as-worded data-revisions=\"{earlier.Length}\"><summary>Earlier revisions of {Escaped(FamilyName(head.Family))} ({earlier.Length}), no longer running</summary>"));

        if (earlier.Length == 0)
        {
            return html.Append("<p class=\"degraded\">None: the running revision is its first.</p></details>").ToString();
        }

        html.Append("<div class=\"tbl-wrap\"><table class=\"list-table\"><thead><tr><th>Revision</th><th>Ran</th><th>What the next revision changed</th><th class=\"r\">Trades</th><th class=\"r\">Result</th><th></th></tr></thead><tbody>");

        foreach (var revision in earlier)
        {
            var next = head.Revisions.FirstOrDefault(one => one.Number == revision.Number + 1);
            PaperBoughtView[] trades = [.. (page.EarlierTraded.GetValueOrDefault(head.Key) ?? []).Where(trade => trade.Version == revision.Name)];
            double[] ended = [.. trades.Where(trade => trade.Closed?.Risks is not null).Select(trade => trade.Closed!.Risks!.Value)];
            var value = PaperLink.RevisionPrefix + revision.Number.ToString(Invariant);

            html.Append(Formatted($"<tr data-revision=\"{revision.Number}\"><td>{Escaped(revision.Name)}</td><td>{Escaped(RevisionView.Ran(revision.From, revision.Until!.Value))}</td><td>{Escaped(next?.Changed is { } changed ? Capitalised(changed) + "." : "-")}</td><td class=\"r num\">{trades.Length}</td><td class=\"r num\">{(ended.Length > 0 ? Escaped(PerRisk(ended.Average())) + " risked" : "none closed")}</td><td><a href=\"{Escaped((link with { Pick = value, Tab = null, Page = 1 }).Href)}\">Open its trades</a></td></tr>"));
        }

        html.Append("</tbody></table></div><p class=\"plan-foot\">A trade an earlier revision placed and still holds stays with that revision and sells by its plan. Its variants stopped when it stopped running.</p></details>");

        return html.ToString();
    }
}
