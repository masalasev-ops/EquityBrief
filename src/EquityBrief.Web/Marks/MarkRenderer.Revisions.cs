using System.Text;
using EquityBrief.Core.Cards;
using EquityBrief.Core.Families;
using EquityBrief.Core.Loop;

namespace EquityBrief.Web.Marks;

// Paper trading by revisions: a tile for each rule's running revision with the line
// of every running revision together against the index's fund; the Rule and Show controls; the revision Show draws with its
// running box, its plan in five plain rows, what it changed, its tiles and its four tabs one at a time; how it has done, its
// record since it started and the test that chose it never added together; its variants, each a row that unfolds to its
// own tabs; and its earlier revisions, folded. Every list draws at most ten rows a page in a stated order.
// see: A rule's revision is a run of its registrations stating the same settings, and every trade belongs to the revision that listed it
// see: Every list of stocks on Paper trading draws at most ten rows a page in a stated order, with the rows it shows counted above its pager
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
            html.Append(FormattableString.Invariant($"<p class=\"running-line\" data-as-worded data-open=\"{all.Open}\">Following every running revision at once: {(all.Walk.Return is { } made ? FormattableString.Invariant($"{(made >= 0 ? "up" : "down")} {Math.Abs(made) * 100:0.0}%") : "no return yet")} since {all.Since:d MMM}, with {all.Open} {(all.Open == 1 ? "trade" : "trades")} open. {Escaped(fund?.Fund ?? "The index's fund")} over the same days: {(all.Walk.FundReturn is { } fundMade ? PaperPercent(fundMade) : "not read")}.</p>"));
        }

        html.Append("<div class=\"setup-tiles\" data-as-worded>");

        foreach (var head in heads)
        {
            var trades = traded.GetValueOrDefault(head.Key) ?? [];
            var open = trades.Count(trade => trade.Open is not null);
            var closed = trades.Count(trade => trade.Closed is not null);

            html.Append(FormattableString.Invariant($"<a class=\"revision-tile\" data-family=\"{Escaped(head.Family)}\" data-standing=\"{head.Standing}\" data-account=\"{Escaped(head.Key)}\" data-return=\"{(head.Walk?.Return is { } held ? held.ToString("R", Invariant) : "none")}\" data-open=\"{head.Open}\" data-closed=\"{head.Closed}\" data-since=\"{(head.Since is { } began ? DayOf(began) : "none")}\" title=\"{Escaped(head.Words)}\" href=\"{Escaped((link with { Rule = head.Family, Pick = null, Tab = null, Page = 1, Unfold = null }).Href)}\" style=\"border-left-color:var(--fam-{Escaped(head.Family)}, var(--hair-2))\">"));
            html.Append(FormattableString.Invariant($"<span class=\"fam-tag\" data-family=\"{Escaped(head.Family)}\"><span class=\"fam-swatch\" data-family=\"{Escaped(head.Family)}\"></span>{Escaped(FamilyName(head.Family))}</span>"));
            // The rule's own return the largest figure, its index's fund over the same days beneath it and smaller.
            html.Append(FormattableString.Invariant($"<b>{(head.Walk?.Return is { } made ? PaperPercent(made) : "no return yet")}</b>{TileFund(head.Walk, fund?.Fund)}"));
            html.Append(FormattableString.Invariant($"<span class=\"rev-tile\">{Escaped(head.Revision?.Tag ?? "Not yet registered: its starting settings")}</span>"));
            html.Append(FormattableString.Invariant($"<span>{open} open · {closed} closed</span></a>"));
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
        html.Append(FormattableString.Invariant($"<label><span class=\"universe-lbl\">Rule</span> <select data-merge-key=\"{PaperLink.RuleKey}\" aria-label=\"The rule drawn below\">"));

        foreach (var one in heads)
        {
            html.Append(FormattableString.Invariant($"<option value=\"{Escaped(one.Family)}\"{(one.Family == head.Family ? " selected" : string.Empty)}>{Escaped(FamilyName(one.Family))}</option>"));
        }

        html.Append("</select></label>");
        html.Append(FormattableString.Invariant($"<label><span class=\"universe-lbl\">Show</span> <select data-merge-key=\"{PaperLink.PickKey}\" aria-label=\"What of the rule is drawn below\">"));
        html.Append(FormattableString.Invariant($"<option value=\"\"{(pick is null ? " selected" : string.Empty)}>{Escaped(running?.Tag ?? "Its starting settings")}</option>"));

        if (variants.Length > 0)
        {
            html.Append(FormattableString.Invariant($"<optgroup label=\"Variants of {Escaped(running?.Name.ToLowerInvariant() ?? "the rule")}: one change each, paper only\">"));

            foreach (var variant in variants)
            {
                html.Append(FormattableString.Invariant($"<option value=\"{Escaped(variant.Key)}\"{(pick == variant.Key ? " selected" : string.Empty)}>{Escaped(VariantName(variant))}{(variant.Since is { } since ? FormattableString.Invariant($", since {since:d MMM yyyy}") : string.Empty)}</option>"));
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

                html.Append(FormattableString.Invariant($"<option value=\"{value}\"{(pick == value ? " selected" : string.Empty)}>{Escaped(revision.Tag)}</option>"));
            }

            html.Append("</optgroup>");
        }

        html.Append("</select></label></div>");

        return html.ToString();
    }

    // How many settings a variant states differently from the running revision, each a chip of its name; a variant is one
    // change, and the checks read this mark to fail one that is more.
    public static int ChangesOf(PaperAccountView variant) =>
        variant.Differences.Count > 0 ? variant.Differences.Count : (variant.Changes ?? string.Empty).Split(" · ", StringSplitOptions.RemoveEmptyEntries).Length;

    // The take-profit a trade's row draws, as a mark the checks read to find one trade drawn with two exits on a page.
    public static string TakeProfitMark(decimal? price) => price is { } at ? at.ToString("0.00", Invariant) : "none";

    // The one change a variant is named by: the setting it was registered to try, which is any it differs in but the plan
    // where it differs in more, as a plain sentence.
    public static ChangeLine? NamedChange(PaperAccountView variant) =>
        variant.Differences.FirstOrDefault(change => change.Key != "hooks") ?? variant.Differences.FirstOrDefault();

    // A variant named by its one change as a sentence.
    public static string VariantName(PaperAccountView variant) =>
        NamedChange(variant)?.Sentence ?? Capitalised((variant.Changes ?? variant.Name).Replace(" · ", "; ", StringComparison.Ordinal));

    // The settings a variant differs in beside the one it is named by, which it carries until it is registered again on the
    // running revision's plan.
    public static IReadOnlyList<ChangeLine> AlsoDiffers(PaperAccountView variant) =>
        NamedChange(variant) is { } named ? [.. variant.Differences.Where(change => !ReferenceEquals(change, named))] : [];

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
            html.Append(FormattableString.Invariant($"<div class=\"running\" data-account=\"{Escaped(head.Key)}\" data-standing=\"{head.Standing}\" data-revision=\"{revision?.Number.ToString(Invariant) ?? "none"}\"><span class=\"vstate run\">{RunningWords}</span><span class=\"big\">{Escaped(revision is null ? "Its starting settings" : revision.Name + ", since " + RevisionView.Day(revision.From))}</span>"));
            html.Append(FormattableString.Invariant($"<span class=\"was\">Its picks are the ones on Home and Tonight.{(before is { Until: { } until } ? FormattableString.Invariant($" Before it: {before.Name.ToLowerInvariant()}, from {RevisionView.Ran(before.From, until)}.") : string.Empty)} No other revision of {Escaped(FamilyName(head.Family))} is trading. <a class=\"paper-log\" href=\"{LogHref(link.Universe, head.Key)}\">Its trade log</a></span></div>"));
        }
        else
        {
            html.Append(FormattableString.Invariant($"<div class=\"running was-running\" data-revision=\"{revision!.Number}\"><span class=\"vstate old\">No longer running</span><span class=\"big\">{Escaped(revision.Name + ", ran " + RevisionView.Ran(revision.From, revision.Until!.Value))}</span>"));
            html.Append("<span class=\"was\">Its picks no longer show on Home and Tonight. A trade it placed and still holds stays with it and sells by its plan below.</span></div>");
        }

        if (page.Plans.GetValueOrDefault(key) is { } plan)
        {
            html.Append(PlanBox(plan, null));
            html.Append("<p class=\"plan-foot\">Until a stock is bought, its stop and take-profit are worked from the price it waits for. Once bought, they are worked again from the price it actually paid.");

            if (revision?.Changed is { } changed && before is not null)
            {
                html.Append(FormattableString.Invariant($" <span data-changed>Changed from {Escaped(before.Name.ToLowerInvariant())}: {Escaped(Uncapitalised(changed))}.</span>"));
            }

            html.Append("</p>");

            // Which part of the take-profit row each of its trades and orders took, counted, so the row and the prices agree.
            string?[] notes = [.. waiting.Select(row => row.TakeProfitFrom), .. trades.Where(trade => trade.Open is not null).Select(trade => page.ProfitFrom.GetValueOrDefault(PaperPageView.ProfitKey(key, trade.Ticker, trade.Listed)))];
            var noBand = notes.Count(note => note is not null && note.EndsWith(": no band above on its night", StringComparison.Ordinal));
            var band = notes.Count(note => note == "the band above, read on its night");

            if (noBand + band > 0)
            {
                var multiple = notes.FirstOrDefault(note => note is not null && note.EndsWith(": no band above on its night", StringComparison.Ordinal))?.Split(':')[0];

                html.Append(FormattableString.Invariant($"<p class=\"plan-foot\" data-take-profit-from data-no-band=\"{noBand}\" data-band=\"{band}\">Of its {noBand + band} open {(noBand + band == 1 ? "trade and order" : "trades and orders")}, {band} {(band == 1 ? "takes" : "take")} profit at the band above read on its night, and {noBand} at {Escaped(multiple ?? "the multiple of the risk")}, its night having read no band above. Each row below says which.</p>"));
            }
        }

        html.Append(RevisionTiles(running ? head : null, trades, waiting));
        html.Append(Tabbed(trades, waiting, left, link.Tab, link.Page, tab => link with { Tab = tab, Page = 1 }, number => link with { Page = number }, null, null, (ticker, listed) => page.ProfitFrom.GetValueOrDefault(PaperPageView.ProfitKey(key, ticker, listed))));

        return html.Append("</div>").ToString();
    }

    public static string EarlierKey(string account, int revision) => account + "#" + revision.ToString(Invariant);

    static string Uncapitalised(string words) =>
        words.Length == 0 || (words.Length > 1 && char.IsUpper(words[1])) ? words : char.ToLowerInvariant(words[0]) + words[1..];

    // A plan's five rows, the one a variant changes marked.
    static string PlanBox(PlanRowsView plan, string? changed)
    {
        var html = new StringBuilder("<div class=\"plan-box\" data-plan>");

        void Row(string name, string words) =>
            html.Append(FormattableString.Invariant($"<div class=\"k\">{Escaped(name)}</div><div class=\"v\">{Escaped(words)}</div>"));

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
            html.Append(FormattableString.Invariant($"<div data-tile=\"account\"><b>${walk.Values[^1].Value:#,0}</b><span>Account now, {(walk.Return is { } made ? PaperPercent(made) : "no return yet")}{(head.Since is { } since ? FormattableString.Invariant($" since {since:d MMM}") : string.Empty)}</span></div>"));
            html.Append(FormattableString.Invariant($"<div data-tile=\"fund\"><b>{(walk.FundReturn is { } fund ? PaperPercent(fund) : "not read")}</b><span>The index's fund over the same days</span></div>"));
        }

        html.Append(FormattableString.Invariant($"<div data-tile=\"counts\"><b>{open} open · {waiting.Count} waiting</b><span>{closed.Length} closed: {took} took profit, {stopped} stopped</span></div>"));
        html.Append(FormattableString.Invariant($"<div data-tile=\"made\"><b>{(risks.Length > 0 ? FormattableString.Invariant($"{(risks.Average() >= 0 ? "+" : "-")}${Math.Abs(risks.Average()):0.00}") : "none yet")}</b><span>made per $1 risked, closed trades</span></div>"));

        if (head?.CountLine is { } count)
        {
            html.Append(FormattableString.Invariant($"<div data-tile=\"needed\"><b>{(head.Progress is { } progress ? FormattableString.Invariant($"{progress.Counted} of {progress.Needed}") : closed.Length.ToString(Invariant))}</b><span>{Escaped(head.Progress is null ? count : "closed trades needed to judge it")}</span></div>"));
        }

        html.Append("</div>");

        return html.ToString();
    }

    static string PerRisk(double risks) => FormattableString.Invariant($"{(risks >= 0 ? "+" : "-")}${Math.Abs(risks):0.00} per $1");

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
        string? revisionName,
        Func<string, DateOnly, string?>? placedBy = null)
    {
        var tab = chosen is { } named && RevisionTabs.Contains(named) ? named : OpenTab;
        PaperBoughtView[] open = [.. trades.Where(trade => trade.Open is not null).OrderBy(NearestSale).ThenBy(trade => trade.Ticker, StringComparer.Ordinal)];
        PaperBoughtView[] closed = [.. trades.Where(trade => trade.Closed is not null).OrderByDescending(trade => trade.Ended).ThenBy(trade => trade.Ticker, StringComparer.Ordinal)];
        var html = new StringBuilder();

        html.Append(FormattableString.Invariant($"<div class=\"tabset\" role=\"tablist\" data-tab=\"{tab}\">"));

        foreach (var (name, words, count) in new[] { (OpenTab, "Open", open.Length), (WaitingTab, "Waiting to buy", waiting.Count), (ClosedTab, "Closed", closed.Length), (LeftTab, "Left the waiting list", left.Count) })
        {
            html.Append(FormattableString.Invariant($"<a class=\"chip tab\" role=\"tab\" aria-selected=\"{(name == tab ? "true" : "false")}\" aria-pressed=\"{(name == tab ? "true" : "false")}\" data-tab-name=\"{name}\" href=\"{Escaped(tabLink(name).Href)}\">{words} · {count}</a>"));
        }

        html.Append("</div>");

        switch (tab)
        {
            case OpenTab:
                html.Append("<p class=\"sub-l\">Bought and still held, each selling by the plan above. Nearest to its stop or its take-profit first.</p>");
                html.Append(Paged(open, page, "nearest to a sale first", pageLink, rows => OpenRows(rows, against, revisionName, placedBy)));
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
                html.Append(FormattableString.Invariant($"<p class=\"sub-l\">Orders that left the waiting list in the last {LeftOrderView.Sessions} trading days.</p>"));
                html.Append(Paged(left, page, "newest first", pageLink, rows => "<ul class=\"left-list\">" + string.Concat(rows.Select(row => FormattableString.Invariant($"<li class=\"{(row.Bought ? "bt" : "cx")}\" data-ticker=\"{Escaped(row.Ticker)}\">{Escaped(row.Words)}</li>"))) + "</ul>"));
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
            near = Math.Min(near, EquityBrief.Core.Prices.Statistic.FromRatio((now - stop) / now));
        }

        if (trade.Target is { } target)
        {
            near = Math.Min(near, EquityBrief.Core.Prices.Statistic.FromRatio((target - now) / now));
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

        html.Append(FormattableString.Invariant($"<div class=\"pager\" data-rows=\"{shown.Length}\" data-of=\"{rows.Count}\"><span>Showing {first} to {first + shown.Length - 1} of {rows.Count}, {Escaped(order)}</span><span class=\"pg\">"));
        html.Append(at > 1 ? FormattableString.Invariant($"<a class=\"pg-btn\" href=\"{Escaped(pageLink(at - 1).Href)}\">‹ Previous</a>") : "<span class=\"pg-btn\" aria-disabled=\"true\">‹ Previous</span>");
        html.Append(FormattableString.Invariant($"<b>Page {at} of {pages}</b>"));
        html.Append(at < pages ? FormattableString.Invariant($"<a class=\"pg-btn\" href=\"{Escaped(pageLink(at + 1).Href)}\">Next ›</a>") : "<span class=\"pg-btn\" aria-disabled=\"true\">Next ›</span>");
        html.Append("</span></div>");

        return html.ToString();
    }

    static string StockTd(string ticker, string? company) =>
        FormattableString.Invariant($"<td class=\"c-nm\"><a class=\"name-link\" href=\"#/chart/{Uri.EscapeDataString(ticker)}\">{Escaped(ticker)}</a>{(company is { Length: > 0 } named ? FormattableString.Invariant($"<span class=\"co\">{Escaped(named)}</span>") : string.Empty)}</td>");

    static string MoneyTd(decimal? price, string? note = null) =>
        price is { } at ? FormattableString.Invariant($"<td class=\"r num\">${Figures.Price(at)}{(note is null ? string.Empty : FormattableString.Invariant($"<span class=\"row-note\">{Escaped(note)}</span>"))}</td>") : FormattableString.Invariant($"<td class=\"r\"><span class=\"degraded\">{Escaped(note ?? "none")}</span></td>");

    // How an open or ended trade takes profit where it names no price: a rising stop, how a book sells it, or its time limit.
    static string TakesProfitTd(PaperBoughtView trade, string? placedBy = null) =>
        trade.Target is { } target ? MoneyTd(target, placedBy)
        : trade.Trails ? "<td class=\"r\">rising stop, no price</td>"
        : trade.Sells is { } sells ? FormattableString.Invariant($"<td>{Escaped(sells)}</td>")
        : FormattableString.Invariant($"<td>none: sells after {trade.Cap?.ToString(Invariant) ?? "its"} trading days</td>");

    static string AgainstTd(Func<string, DateOnly, string>? against, string ticker, DateOnly listed) =>
        against is null ? string.Empty : FormattableString.Invariant($"<td class=\"against\">{Escaped(against(ticker, listed))}</td>");

    static string AgainstTh(string? revisionName) =>
        revisionName is null ? string.Empty : FormattableString.Invariant($"<th>{Escaped(revisionName)}</th>");

    static string OpenRows(IReadOnlyList<PaperBoughtView> rows, Func<string, DateOnly, string>? against, string? revisionName, Func<string, DateOnly, string?>? placedBy)
    {
        var html = new StringBuilder("<div class=\"tbl-wrap\"><table class=\"list-table revision-open\"><thead><tr><th>Stock</th><th class=\"r\">Bought at</th><th class=\"r\">Sells if it closes below</th><th class=\"r\">Takes profit at</th><th class=\"r\">Last close</th><th class=\"r\">So far</th><th class=\"r\">Trading days used</th><th>Where it stands</th>" + AgainstTh(revisionName) + "</tr></thead><tbody>");

        foreach (var trade in rows)
        {
            var position = trade.Open!;

            html.Append(FormattableString.Invariant($"<tr data-ticker=\"{Escaped(trade.Ticker)}\" data-listed=\"{trade.Listed:yyyy-MM-dd}\" data-take-profit=\"{TakeProfitMark(trade.Target)}\">"));
            html.Append(StockTd(trade.Ticker, trade.Company));
            html.Append(FormattableString.Invariant($"<td class=\"r num\">${Figures.Price(trade.Buy)}<span class=\"row-note\">{trade.Bought:d MMM yyyy}</span></td>"));
            html.Append(trade.Stop is { } stop ? MoneyTd(stop) : FormattableString.Invariant($"<td>{Escaped(trade.Sells ?? "no price stop")}</td>"));
            html.Append(TakesProfitTd(trade, placedBy?.Invoke(trade.Ticker, trade.Listed)));
            html.Append(MoneyTd(position.Now));
            html.Append(FormattableString.Invariant($"<td class=\"r num\">{(position.Risks is { } risks ? Escaped(PerRisk(risks)) + " risked" : string.Empty)}{(position.Fraction is { } fraction ? FormattableString.Invariant($"<span class=\"row-note\">{PaperPercent(fraction)}</span>") : string.Empty)}</td>"));
            html.Append(FormattableString.Invariant($"<td class=\"r num\">{(position.Held is { } held ? held.ToString(Invariant) : "-")}{(trade.Cap is { } cap ? FormattableString.Invariant($" of {cap}") : string.Empty)}</td>"));
            html.Append(FormattableString.Invariant($"<td{(PaperAccount.IsClosedUnder(position.Standing) ? " class=\"loss\"" : string.Empty)}>{Escaped(Capitalised(position.Standing))}{(trade.Past is not null ? "<span class=\"row-note\">It opened past its own price.</span>" : string.Empty)}</td>"));
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
            html.Append(FormattableString.Invariant($"<tr data-ticker=\"{Escaped(row.Ticker)}\" data-placed=\"{row.Placed:yyyy-MM-dd}\" data-take-profit=\"{TakeProfitMark(row.TakeProfit)}\">"));
            html.Append(StockTd(row.Ticker, row.Company));
            html.Append(FormattableString.Invariant($"<td class=\"r num\">{row.Placed:d MMM}</td>"));
            html.Append(row.BuyPrice is { } buy ? MoneyTd(buy) : FormattableString.Invariant($"<td>{Escaped(row.Words ?? "the next open")}</td>"));
            html.Append(MoneyTd(row.LastClose, row.AboveBuy is { } above ? FormattableString.Invariant($"{Math.Abs(above) * 100:0.0}% {(above >= 0 ? "above" : "below")}") : null));
            html.Append(MoneyTd(row.Stop, row.Stop is null ? "worked from the price it pays" : null));
            html.Append(row.TakeProfit is { } profit ? MoneyTd(profit, row.TakeProfitFrom ?? (row.TakeProfitIsPart ? "on part of it" : null)) :MoneyTd(null, row.BuyPrice is null ? "worked from the price it pays" : "none: sells on its time limit"));
            html.Append(row.LastDay is { } last ? FormattableString.Invariant($"<td class=\"r num\">{last:d MMM}{(row.DaysLeft is { } days ? FormattableString.Invariant($"<span class=\"row-note\">{days} trading {(days == 1 ? "day" : "days")} left</span>") : string.Empty)}</td>") : "<td class=\"r\">-</td>");
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

            html.Append(FormattableString.Invariant($"<tr data-ticker=\"{Escaped(trade.Ticker)}\" data-listed=\"{trade.Listed:yyyy-MM-dd}\">"));
            html.Append(StockTd(trade.Ticker, trade.Company));
            html.Append(FormattableString.Invariant($"<td class=\"r num\">${Figures.Price(trade.Buy)}<span class=\"row-note\">{trade.Bought:d MMM}</span></td>"));
            html.Append(FormattableString.Invariant($"<td class=\"r num\">{(ended.Sale is { } sale ? "$" + Figures.Price(sale) : "-")}<span class=\"row-note\">{(trade.Ended is { } end ? end.ToString("d MMM", Invariant) : string.Empty)}</span></td>"));
            html.Append(FormattableString.Invariant($"<td>{Escaped(Capitalised(ended.Ending))}</td>"));
            html.Append(FormattableString.Invariant($"<td class=\"r num\">{(ended.Risks is { } risks ? Escaped(PerRisk(risks)) + " risked" : string.Empty)}{(ended.Fraction is { } fraction ? FormattableString.Invariant($"<span class=\"row-note\">{PaperPercent(fraction)}</span>") : string.Empty)}</td>"));
            html.Append(AgainstTd(against, trade.Ticker, trade.Listed));
            html.Append("</tr>");
        }

        return html.Append("</tbody></table></div>").ToString();
    }

    // How the revision has done: since it started running, and the monthly test over past years that judges it, never added
    // together.
    public string HowItHasDone(PaperPageView page, PaperAccountView head)
    {
        var html = new StringBuilder();

        var longOwn = page.Long.FirstOrDefault(row => row.Family == head.Family && row.Standing != PaperAccountView.Variant);

        html.Append(FormattableString.Invariant($"<div class=\"score\" data-family=\"{Escaped(head.Family)}\" data-standing=\"{head.Standing}\" data-verdict=\"{Escaped(head.Verdict)}\">"));
        html.Append(FormattableString.Invariant($"<p class=\"sh-line\" data-chosen-on>{Escaped(ChosenOn(head.Family, [.. (head.Boxes ?? []).Select(box => box.Year)], longOwn))}</p>"));
        html.Append("<div class=\"score-halves\">");
        html.Append(RunningRecord(head));
        html.Append(TestRecord(head));
        html.Append("</div>");
        html.Append(PooledPart(page.Pooled.FirstOrDefault(row => row.Own && row.Family == head.Family), PooledSetups(page.Pooled)));
        html.Append(LongRecord(longOwn, page.LongPooled.FirstOrDefault(row => row.Own && row.Family == head.Family)));
        html.Append("</div>");

        return html.ToString();
    }

    // The years each test behind a revision read, each test with its own: the sweeps chose its settings on the standing years
    // from 2019, the plan test its take-profit on the long ledger's years before them, a book's own test its stop and
    // take-profit on the years before 2019 or the heavyweights' on 2019 to 2022, and the monthly test judges it over its test
    // years; the long history named only where its long test walked a trade before 2019.
    // see: Each rule takes profit in the shape its test chose on the early years, never at the nearest price it turned down at
    public static string ChosenOn(string family, IReadOnlyList<int> testYears, PaperLongVerdict? longRecord)
    {
        var standingFrom = EquityBrief.Core.History.LongLedger.StandingFrom.Year;
        var settings = testYears.Count > 0 ? FormattableString.Invariant($"Its settings were chosen on {standingFrom} to {testYears.Max()}.") : FormattableString.Invariant($"Its settings were chosen on the years from {standingFrom}.");
        var exit = family == HeavyweightRule.Name
            ? FormattableString.Invariant($" Its stop and take-profit were chosen on {EquityBrief.Core.Loop.BookExits.HeavyweightsChosenFrom} to {EquityBrief.Core.Loop.BookExits.HeavyweightsChosenThrough}.")
            : family is QualityRule.Name or ImprovingRule.Name
                ? FormattableString.Invariant($" Its stop and take-profit were chosen on the years before {standingFrom}.")
                : FormattableString.Invariant($" Its take-profit was chosen on {EquityBrief.Core.History.LongLedger.From.Year} to {EquityBrief.Core.History.LongLedger.Through.Year}.");
        var monthly = testYears.Count > 0
            ? FormattableString.Invariant($" The monthly test judges it over {testYears.Min()} to {testYears.Max()}")
            : " No monthly test has judged it yet";
        var tested = longRecord is { Why: null, NeverSeenUnits: > 0 } walked ? FormattableString.Invariant($", and it was tested over the long history from {walked.FirstTestYear}") : string.Empty;

        return settings + exit + monthly + tested + ".";
    }

    // What a rule not yet tested keeps in its record's place.
    public const string NotYetTestedRecord = "A rule not yet tested keeps no record: it starts one when its family is frozen or a setting is approved, and its account counts every trade from its first meanwhile.";

    // The record since the revision started running: the trades decided of the count a verdict needs, as a bar with where it
    // counts from, and what its decided trades made against the same plan bought on every stock, each said in a sentence.
    static string RunningRecord(PaperAccountView head)
    {
        var html = new StringBuilder();
        var book = head.Unit == LoopPoints;
        var started = head.Since is { } since ? " " + RevisionView.Day(since) : string.Empty;

        html.Append(FormattableString.Invariant($"<div class=\"score-half live\"><div class=\"sh-h\"><span class=\"sh-lbl\">Since it started running,{Escaped(started)}</span>{(head.Standing == PaperAccountView.Provisional ? string.Empty : head.TooFew is null ? " <span class=\"pill\">enough to judge</span>" : " <span class=\"pill\">too early to judge</span>")}</div>"));

        if (head.TooFew is { } few)
        {
            html.Append(FormattableString.Invariant($"<p class=\"sh-line\" data-too-few><span class=\"too-few\">{Escaped(few)}</span></p>"));
        }

        if (head.Standing == PaperAccountView.Provisional || head.Progress is not { } progress)
        {
            return html.Append(FormattableString.Invariant($"<p class=\"sh-line\" data-record=\"none\">{Escaped(NotYetTestedRecord)}</p></div>")).ToString();
        }

        var unit = book ? "months held" : "trades decided";

        html.Append(FormattableString.Invariant($"<p class=\"sh-count\" data-counted=\"{progress.Counted}\" data-needed=\"{progress.Needed}\">{progress.Counted} of {progress.Needed} {unit}</p>"));
        html.Append(FormattableString.Invariant($"<svg class=\"sh-progress\" viewBox=\"0 0 300 10\" role=\"img\" aria-label=\"{progress.Counted} of {progress.Needed}\" data-share=\"{progress.Share.ToString("R", Invariant)}\"><rect class=\"sp-track\" x=\"0\" y=\"0\" width=\"300\" height=\"10\" rx=\"5\"></rect><rect class=\"sp-fill\" x=\"0\" y=\"0\" width=\"{Number(300 * progress.Share)}\" height=\"10\" rx=\"5\"></rect></svg>"));
        html.Append(FormattableString.Invariant($"<div class=\"sh-ends\"><span>{(head.Since is { } from ? FormattableString.Invariant($"counting from <span class=\"day\">{DayOf(from)}</span>") : "counting from its first trade")}</span><span>{progress.Needed} {(book ? "months" : "decided trades")} for a verdict</span></div>"));
        html.Append(FormattableString.Invariant($"<p class=\"sh-line\" data-needs>{(head.TooFew is null ? FormattableString.Invariant($"It has the {progress.Needed} {(book ? "months" : "decided trades")} a verdict needs, so its record can be judged.") : FormattableString.Invariant($"A verdict needs {progress.Needed} {(book ? "months held" : "decided trades")}, and {progress.Counted} {(progress.Counted == 1 ? "is" : "are")} in, so it is too early to judge it on its own trades."))}</p>"));
        html.Append(progress.MeanEdge is { } edge
            ? FormattableString.Invariant($"<p class=\"sh-line\" data-edge=\"{edge.ToString("R", Invariant)}\">So far its decided {(book ? "holdings" : "trades")} made {(book ? SignedTwo(edge * 100) + " points a holding more than " + BookAgainst(head.Family, "its") : SignedTwo(edge) + " per $1 risked more than the same plan bought on every stock that night")}, over the {progress.Edged} whose comparison is read.</p>")
            : "<p class=\"sh-line\" data-edge=\"none\">None of its trades has ended and been compared with the same plan bought on every stock yet, so there is no result to read.</p>");

        return html.Append("</div>").ToString();
    }

    // The test over past years that chose the revision: each test year as a box, better or worse than the easy alternative by
    // how much, and how often luck alone would do as well, each said in a sentence.
    static string TestRecord(PaperAccountView head)
    {
        var html = new StringBuilder();
        var boxes = head.Boxes ?? [];
        var book = head.Unit == LoopPoints;
        var years = boxes.Count > 0 ? FormattableString.Invariant($", {boxes.Min(box => box.Year)} to {boxes.Max(box => box.Year)}") : string.Empty;

        html.Append(FormattableString.Invariant($"<div class=\"score-half history\"><div class=\"sh-h\"><span class=\"sh-lbl\">The monthly test{years}</span> <span class=\"pill verdict\" data-verdict=\"{Escaped(head.Verdict)}\">{Escaped(head.Verdict)}</span></div>"));
        html.Append(FormattableString.Invariant($"<p class=\"sh-line\" data-verdict-said>{Escaped(VerdictSaid(head.Verdict))}</p>"));

        if (boxes.Count == 0)
        {
            return html.Append(FormattableString.Invariant($"<p class=\"degraded\" data-history=\"none\">{(head.Verdict == EquityBrief.Core.Loop.RuleVerdict.NotTested ? "No monthly test has read this rule on this index yet." : "The test could not walk this rule over the history, so it holds no year and no chance of luck to draw.")}</p></div>")).ToString();
        }

        html.Append("<h4>Did it beat the easy alternative?</h4>");
        html.Append(FormattableString.Invariant($"<p class=\"sh-line\">{(head.Better is { } better && head.Counted is { } counted and > 0 ? FormattableString.Invariant($"Better in {better} of {counted} complete {(counted == 1 ? "year" : "years")}. ") : string.Empty)}Each box is a test year: did its {(book ? "holdings" : "trades")} beat {(book ? BookAgainst(head.Family, "their") : "the same plan bought on every stock that night")}, and by how much, {(book ? "in points a month" : "per $1 risked a trade")}.</p>"));

        if (head.JudgedFrom is { } judgedFrom)
        {
            html.Append(FormattableString.Invariant($"<p class=\"sh-line\" data-judged-from=\"{judgedFrom}\">Its verdict reads the years from {judgedFrom} alone. The years before are shown beside them, {Escaped(CutoffLabel)}.</p>"));
        }

        html.Append("<div class=\"sh-years\">");

        foreach (var box in boxes)
        {
            var kind = box.Word == PaperAccount.Better ? "better" : box.Word == PaperAccount.Worse ? "worse" : box.Word == PaperAccount.Even ? "even" : "none";
            var before = head.JudgedFrom is { } judged && box.Year < judged;
            var year = head.Years.FirstOrDefault(row => row.Year == box.Year);
            var said = year is null ? string.Empty : FormattableString.Invariant($" title=\"{box.Year}: {year.Units} {(book ? (year.Units == 1 ? "month" : "months") : (year.Units == 1 ? "trade" : "trades"))}, a total of {Escaped(Plain(head.Unit, year.Total))} against {Escaped(Plain(head.Unit, year.ComparedTotal))}{(before ? ", " + Escaped(CutoffLabel) : string.Empty)}\"");

            html.Append(FormattableString.Invariant($"<div class=\"sy {kind}\" data-year=\"{box.Year}\" data-word=\"{Escaped(box.Word)}\" data-edge=\"{(box.Edge is { } held ? held.ToString("R", Invariant) : "none")}\"{(before ? " data-before-cutoff=\"1\"" : string.Empty)}{said}>"));
            html.Append(FormattableString.Invariant($"<span class=\"sy-y\">{box.Year}{(box.Complete ? string.Empty : " so far")}</span><b>{Escaped(box.Word)}</b>"));
            html.Append(box.Edge is { } edge ? FormattableString.Invariant($"<span class=\"sy-e\">{(book ? SignedTwo(edge * 100) : SignedTwo(edge))}</span>") : string.Empty);
            html.Append("</div>");
        }

        html.Append("</div><h4>Could that just be luck?</h4>");

        if (head.Luck is { } luck)
        {
            html.Append(LuckScale(luck, head.Adjusted));
            html.Append(FormattableString.Invariant($"<p class=\"sh-line\" data-zone=\"{Escaped(luck.Zone)}\">{Escaped(luck.Zone)}: a rule picking at random would do this well {Escaped(luck.Words)}. To count as real it needs fewer than 1 in {PaperAccount.RealOneIn}.</p>"));
        }
        else
        {
            html.Append("<p class=\"degraded\" data-luck=\"none\">Too few blocks of sessions were read to say how often luck alone would do as well.</p>");
        }

        if (head.Head)
        {
            html.Append(FormattableString.Invariant($"<p class=\"sh-warn\">{Escaped(TunedLine)}</p>"));
        }

        return html.Append("</div>").ToString();
    }

    // A test's verdict in a plain sentence beside its word.
    static string VerdictSaid(string verdict) =>
        verdict == EquityBrief.Core.Loop.RuleVerdict.Passed ? "Passed: over its test years it beat the easy alternative by more than luck alone would."
        : verdict == EquityBrief.Core.Loop.RuleVerdict.WithinLuck ? "Within luck: over its test years it did better than the easy alternative, but not by more than a rule picking at random could have."
        : verdict == EquityBrief.Core.Loop.RuleVerdict.NotTested ? "No monthly test has read it yet."
        : verdict.StartsWith(EquityBrief.Core.Loop.RuleVerdict.NotTestablePrefix, StringComparison.Ordinal) ? "The past years cannot test it, because the store does not hold what it reads for them; its own trades will judge it."
        : "It failed one of the test's checks, so the test does not judge it better than the easy alternative.";

    // The S&P 500's heavyweights over the long history, whose fund holdings the long members' run reads from October 2018.
    public const string HeavyweightHoldingsLine = "The long history holds heavyweight picks only from October 2018, when the fund-holdings data begins.";

    // The rule over the long history from its family's first test year: whether it passed, the years better and its trades,
    // how often luck would do as well, the years it was chosen on and, where it holds any, the years before them; and the rules
    // of every index summed over the same history. Every figure is the long step's row, each said in a sentence.
    public static string LongRecord(PaperLongVerdict? own, LoopPooledRow? pooled)
    {
        var html = new StringBuilder();
        var book = own?.Book ?? false;
        var unit = book ? "months" : "trades";

        html.Append(FormattableString.Invariant($"<div class=\"score-long\" data-long=\"{(own is null ? "none" : own.Why is not null ? "not walked" : own.Passed ? "passed" : "did not pass")}\"><h4>{LongHeading}</h4>"));

        if (own is null)
        {
            return html.Append("<p class=\"degraded\" data-long-line=\"none\">No monthly run has tested this rule over the long history yet.</p></div>").ToString();
        }

        if (own.Why is { } why)
        {
            html.Append(FormattableString.Invariant($"<p class=\"degraded\" data-long-line=\"why\">{Escaped(Capitalised(why))}.</p>"));
        }
        else
        {
            var luck = own.Adjusted is { } p ? PaperAccount.Luck(p).Words : null;
            var standingFrom = EquityBrief.Core.History.LongLedger.StandingFrom.Year;
            var none = own.FirstTestYear < standingFrom && own.NeverSeenUnits == 0;

            // Where the long test walked none of the years before 2019 it says so plainly, and its verdict names the years it
            // read; a heavyweight book on an index whose fund holdings begin in October 2018 says that its history does.
            if (none)
            {
                html.Append(FormattableString.Invariant($"<p data-long-years=\"none before\">The long test walked no trade of it from {own.FirstTestYear} to {standingFrom - 1}, so its long record reads its years from {standingFrom} alone.</p>"));
            }

            if (own.Family == HeavyweightRule.Name && own.FirstTestYear >= standingFrom)
            {
                html.Append(FormattableString.Invariant($"<p data-long-years=\"holdings\">{Escaped(HeavyweightHoldingsLine)}</p>"));
            }

            html.Append(FormattableString.Invariant($"<p data-long-line=\"verdict\">From {(none ? standingFrom : own.FirstTestYear)} on, it {(own.Passed ? "passed" : "did not pass")}: better than the easy alternative in {own.Better ?? 0} of {own.Counted ?? 0} years, over {(own.Units ?? 0).ToString("N0", Invariant)} {unit}.{(luck is null ? string.Empty : FormattableString.Invariant($" A rule picking at random would do as well {Escaped(luck)}, and to pass it needs fewer than 1 in {PaperAccount.RealOneIn}."))}</p>"));

            if (own.NeverSeenUnits > 0)
            {
                html.Append(FormattableString.Invariant($"<p data-long-years=\"never seen\">From {own.FirstTestYear} to {EquityBrief.Core.History.LongLedger.StandingFrom.Year - 1}, years its settings were not chosen on: {own.NeverSeenUnits.ToString("N0", Invariant)} {unit}, {(own.NeverSeenTotal / own.NeverSeenUnits).ToString("+0.000;-0.000;0.000", Invariant)} {(book ? "points" : "per $1 risked")} each after costs.</p>"));
            }

            html.Append(FormattableString.Invariant($"<p data-long-years=\"chosen on\">From {EquityBrief.Core.History.LongLedger.StandingFrom.Year} on: {own.ChosenUnits.ToString("N0", Invariant)} {unit}{(own.ChosenUnits > 0 ? FormattableString.Invariant($", {(own.ChosenTotal / own.ChosenUnits).ToString("+0.000;-0.000;0.000", Invariant)} {(book ? "points" : "per $1 risked")} each after costs") : string.Empty)}. Its settings were chosen on these years, so they read better than it is likely to do from here.</p>"));
        }

        if (pooled is not null)
        {
            var luck = pooled.Adjusted is { } p ? PaperAccount.Luck(p).Words : null;

            html.Append(FormattableString.Invariant($"<p data-long-pooled=\"{(pooled.Passed ? "passed" : "did not pass")}\">{PooledHeading} over the long history: it {(pooled.Passed ? "passed" : "did not pass")}, better in {pooled.Better} of {pooled.Counted} years over {PooledUnits(pooled)} on {Escaped(IndicesSaid(pooled.Indices))}.{(luck is null ? string.Empty : FormattableString.Invariant($" A rule picking at random would do as well {Escaped(luck)}."))}</p>"));
        }
        else
        {
            html.Append(FormattableString.Invariant($"<p data-long-pooled=\"none\">{PooledHeading}: not summed yet.</p>"));
        }

        return html.Append("</div>").ToString();
    }

    // The running revision's variants, a row each that unfolds to its one change, its tiles and its own tabs, every row
    // saying what the running revision did with the same stock.
    public string VariantsOf(PaperPageView page, PaperAccountView head, PaperLink link)
    {
        var variants = page.Accounts.Where(account => account.Family == head.Family && account.Standing == PaperAccountView.Variant).ToArray();
        var coming = (page.Coming ?? []).Where(one => one.Family == head.Family).ToArray();
        var name = head.Revision?.Name ?? "The rule";

        if (variants.Length == 0 && coming.Length == 0)
        {
            return "<p class=\"degraded\" data-variants=\"0\">No variant is registered for this rule.</p>";
        }

        // A rule whose revision and every variant have made no trade since its record began says why in one line.
        var none = NoTradeLine([head, .. variants], coming.Length, head.Family) is { } said
            ? FormattableString.Invariant($"<p class=\"paper-no-trade\" data-no-trade=\"{variants.Length}\">{Escaped(said)}</p>")
            : string.Empty;

        var headTrades = page.Traded.GetValueOrDefault(head.Key) ?? [];
        var headWaiting = page.Waiting.GetValueOrDefault(head.Key) ?? [];

        string Against(string ticker, DateOnly listed) =>
            headTrades.FirstOrDefault(trade => trade.Ticker == ticker && trade.Listed == listed) is { } same
                ? same.Open is not null ? "Holds it too" : FormattableString.Invariant($"Took it too, sold {same.Ended:d MMM}")
                : headWaiting.FirstOrDefault(order => order.Ticker == ticker && order.Placed == listed) is { } order
                    ? order.BuyPrice is { } buy ? FormattableString.Invariant($"Waits to buy it at ${Figures.Price(buy)}") : "Buys it at the next open"
                    : "Did not take it";

        var html = new StringBuilder("<div class=\"revision-variants\" data-as-worded>");

        html.Append(none);
        html.Append(FormattableString.Invariant($"<p class=\"sub-l\">Each variant is {Escaped(name.ToLowerInvariant())} with one thing changed, traded only on paper and never as picks. A variant that does better can become the next revision only at a monthly run, on your word. Press a variant's row to unfold its trades under it; press again to fold it away.</p>"));
        html.Append(FormattableString.Invariant($"<div class=\"tbl-wrap\"><table class=\"list-table vtable\" data-variants=\"{variants.Length}\"><thead><tr><th>Variant, named by how it differs from {Escaped(name.ToLowerInvariant())} (press one to unfold it)</th><th class=\"r\">Trades</th><th class=\"r\">Trades {Escaped(name.ToLowerInvariant())} did not take</th><th class=\"r vres\">Result so far</th><th class=\"vpast\">On past years</th></tr></thead><tbody>"));

        foreach (var variant in variants)
        {
            var open = link.Unfold == variant.Key;
            var trades = page.Traded.GetValueOrDefault(variant.Key) ?? [];
            // A stock the running revision only waits for, or never listed, it has not taken.
            var alone = trades.Count(trade => !headTrades.Any(same => same.Ticker == trade.Ticker && same.Listed == trade.Listed));
            double[] ended = [.. trades.Where(trade => trade.Closed?.Risks is not null).Select(trade => trade.Closed!.Risks!.Value)];
            var toggle = link with { Unfold = open ? null : variant.Key, VariantTab = null, VariantPage = 1, Pick = null };

            html.Append(FormattableString.Invariant($"<tr class=\"vrow{(open ? " open" : string.Empty)}\" data-account=\"{Escaped(variant.Key)}\" data-standing=\"{variant.Standing}\" data-trades=\"{trades.Count}\" data-changes=\"{ChangesOf(variant)}\" id=\"variant-{Escaped(variant.Key)}\">"));
            var also = AlsoDiffers(variant);

            html.Append(FormattableString.Invariant($"<td><a class=\"vbtn\" title=\"{Escaped(variant.Words)}\" href=\"{Escaped(toggle.Href)}\" aria-expanded=\"{(open ? "true" : "false")}\"><span class=\"car\">{(open ? "▾" : "▸")}</span><b>{Escaped(VariantName(variant))}</b></a>{(variant.Since is { } since ? FormattableString.Invariant($"<span class=\"row-note\">since {since:d MMM yyyy}</span>") : string.Empty)}{(also.Count > 0 ? FormattableString.Invariant($"<span class=\"row-note\" data-also=\"{also.Count}\">Also differs from {Escaped(name.ToLowerInvariant())}: {Escaped(string.Join("; ", also.Select(change => Uncapitalised(change.Sentence))))}.</span>") : string.Empty)}{(variant.Reach is { } reach && EquityBrief.Core.Cards.VersionReach.Words(reach) is { } reached ? FormattableString.Invariant($"<span class=\"pv-reach\" data-reach=\"{reach.ToString().ToLowerInvariant()}\">{Escaped(Capitalised(reached))}.</span>") : string.Empty)}{AccountNote(variant)}<a class=\"paper-log\" href=\"{LogHref(link.Universe, variant.Key)}\">trade log</a></td>"));
            html.Append(FormattableString.Invariant($"<td class=\"r num\">{trades.Count}</td><td class=\"r num\">{alone}</td>"));
            html.Append(FormattableString.Invariant($"<td class=\"r num vres\" data-return=\"{(variant.Walk?.Return is { } held ? held.ToString("R", Invariant) : "none")}\"><span class=\"vres-line\">{(ended.Length > 0 ? Escaped(PerRisk(ended.Average())) + " risked" : "none closed")}</span>{(variant.Walk?.Return is { } made ? FormattableString.Invariant($"<span class=\"row-note\">{PaperPercent(made)} on the account</span>") : string.Empty)}{(variant.TooFew is { } few ? FormattableString.Invariant($"<span class=\"too-few\">{Escaped(few)}</span>") : string.Empty)}</td>"));
            html.Append(FormattableString.Invariant($"<td class=\"vpast\"><span class=\"pill\"{(variant.Adjusted is { } p ? FormattableString.Invariant($" title=\"adjusted p {p.ToString("0.0000", Invariant)} against the bar {LoopGate.Bar.ToString("0.0000", Invariant)}\"") : string.Empty)}>{Escaped(variant.Verdict)}</span>{(variant.Better is { } better && variant.Counted is { } counted ? FormattableString.Invariant($"<span class=\"row-note\">better in {better} of {counted} years</span>") : string.Empty)}</td>"));
            html.Append("</tr>");

            if (open)
            {
                var waiting = page.Waiting.GetValueOrDefault(variant.Key) ?? [];
                var left = page.Left.GetValueOrDefault(variant.Key) ?? [];

                html.Append("<tr class=\"vdetail\"><td colspan=\"5\"><div class=\"vx\">");
                html.Append(FormattableString.Invariant($"<div class=\"plan-box\" data-plan=\"changed\"><div class=\"k chg\">Its change<span class=\"chg-tag\">Changed</span></div><div class=\"v chg\">{Escaped(VariantName(variant))}.</div>"));

                if (also.Count > 0)
                {
                    foreach (var change in also)
                    {
                        html.Append(FormattableString.Invariant($"<div class=\"k chg\">Also differs<span class=\"chg-tag\">Changed</span></div><div class=\"v chg\">{Escaped(change.Sentence)}. It was registered before {Escaped(name.ToLowerInvariant())} took this part, and differs in it until it is registered again on {Escaped(name.ToLowerInvariant())}'s plan.</div>"));
                    }

                    html.Append(FormattableString.Invariant($"<div class=\"k\">Everything else</div><div class=\"v\">Same as {Escaped(name.ToLowerInvariant())}.</div></div>"));
                }
                else
                {
                    html.Append(FormattableString.Invariant($"<div class=\"k\">Everything else</div><div class=\"v\">Same as {Escaped(name.ToLowerInvariant())}: what it looks for, how it buys, when it sells at a loss, when it takes profit and its time limit.</div></div>"));
                }
                html.Append(RevisionTiles(variant, trades, waiting));
                html.Append(Tabbed(trades, waiting, left, link.VariantTab, link.VariantPage, tab => link with { VariantTab = tab, VariantPage = 1 }, number => link with { VariantPage = number }, Against, name, (ticker, listed) => page.ProfitFrom.GetValueOrDefault(PaperPageView.ProfitKey(variant.Key, ticker, listed))));
                html.Append(FormattableString.Invariant($"<p class=\"plan-foot\">Each tab works as on {Escaped(name.ToLowerInvariant())}'s card, every row saying what {Escaped(name.ToLowerInvariant())} did with the same stock. <a href=\"{Escaped(toggle.Href)}\">Fold it away</a></p>"));
                html.Append("</div></td></tr>");
            }
        }

        // A variant registered after the newest night, which no night has read yet: its change and its first night.
        foreach (var one in coming)
        {
            html.Append(FormattableString.Invariant($"<tr class=\"vrow coming\" data-standing=\"coming\" data-registered=\"{DayOf(one.Registered)}\" data-first-night=\"{(one.FirstNight is { } first ? DayOf(first) : "none")}\"><td><b>{Escaped(Capitalised(one.Changes ?? one.Short))}</b><span class=\"pv-note\" data-note=\"coming\">{Escaped(ComingLine(one))}</span></td><td class=\"r num\">0</td><td class=\"r num\">0</td><td class=\"r num\" data-return=\"none\">none yet</td><td></td></tr>"));
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

        var html = new StringBuilder(FormattableString.Invariant($"<section class=\"card name-trades\" data-card=\"name-trades\" data-ticker=\"{Escaped(ticker)}\" data-trades=\"{trades.Count}\" data-as-worded><div class=\"card-b\"><h3 class=\"card-t\">{Escaped(ticker)} on the rules' paper trades</h3>"));

        html.Append("<div class=\"tbl-wrap\"><table class=\"list-table\"><thead><tr><th>Setup</th><th>State</th><th class=\"r\">Price</th><th class=\"r\">Sells if it closes below</th><th class=\"r\">Takes profit at</th><th class=\"r\">Days left</th><th>Where it stands</th></tr></thead><tbody>");

        foreach (var trade in trades)
        {
            html.Append(FormattableString.Invariant($"<tr data-family=\"{Escaped(trade.Family)}\" data-waiting=\"{Flag(trade.Waiting)}\" data-ticker=\"{Escaped(ticker)}\" data-placed=\"{trade.Placed:yyyy-MM-dd}\" data-take-profit=\"{TakeProfitMark(trade.TakeProfit)}\"><td><span class=\"tag-stack\"><span class=\"fam-tag\" data-family=\"{Escaped(trade.Family)}\"><span class=\"fam-swatch\" data-family=\"{Escaped(trade.Family)}\"></span>{Escaped(FamilyName(trade.Family))}</span><span class=\"vtag\">{Escaped(trade.Revision)}</span></span></td>"));
            html.Append(FormattableString.Invariant($"<td>{(trade.Waiting ? FormattableString.Invariant($"Waiting to buy, placed {trade.Placed:d MMM}") : FormattableString.Invariant($"Bought {trade.Bought:d MMM yyyy}"))}</td>"));
            html.Append(MoneyTd(trade.Price, trade.Waiting ? "its buy price" : "paid"));
            html.Append(MoneyTd(trade.Stop));
            html.Append(MoneyTd(trade.TakeProfit, trade.TakeProfit is null ? "none: sells on its time limit" : null));
            html.Append(FormattableString.Invariant($"<td class=\"r num\">{(trade.DaysLeft is { } days ? days.ToString(Invariant) : "-")}</td><td>{Escaped(trade.Standing)}</td></tr>"));
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

        html.Append(FormattableString.Invariant($"<details class=\"earlier-revisions\" data-as-worded data-revisions=\"{earlier.Length}\"><summary>Earlier revisions of {Escaped(FamilyName(head.Family))} ({earlier.Length}), no longer running</summary>"));

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

            html.Append(FormattableString.Invariant($"<tr data-revision=\"{revision.Number}\"><td>{Escaped(revision.Name)}</td><td>{Escaped(RevisionView.Ran(revision.From, revision.Until!.Value))}</td><td>{Escaped(next?.Changed is { } changed ? Capitalised(changed) + "." : "-")}</td><td class=\"r num\">{trades.Length}</td><td class=\"r num\">{(ended.Length > 0 ? Escaped(PerRisk(ended.Average())) + " risked" : "none closed")}</td><td><a href=\"{Escaped((link with { Pick = value, Tab = null, Page = 1 }).Href)}\">Open its trades</a></td></tr>"));
        }

        html.Append("</tbody></table></div><p class=\"plan-foot\">A trade an earlier revision placed and still holds stays with that revision and sells by its plan. Its variants stopped when it stopped running.</p></details>");

        return html.ToString();
    }
}
