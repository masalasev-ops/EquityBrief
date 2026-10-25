using System.Text;
using EquityBrief.Core.Cards;
using EquityBrief.Core.Families;
using EquityBrief.Core.Loop;

namespace EquityBrief.Web.Marks;

// One test year of a rule in its index's newest per-rule test: its units and total, and the side it was read against's.
public sealed record PaperYearView(int Year, bool Complete, int Units, double Total, int ComparedUnits, double ComparedTotal);

// One paper account as paper trading and the trade log draw it: its key in the link, its family, its standing (live,
// provisional, variant or the operator's own), its name and its rule's words, its test years verdict with what it was
// read against, its unit and its test years, its walk where the account settings are set, its trades and those ended,
// its record since registration's label until the count screen's minimum is met, the day it starts, its adjusted p in
// that test, its record's count in one line, the words naming the version it counts, and how many trades it leaves out
// as listed before that version. Paper trading's page draws besides its short name and what it changes in words, its
// trades against its family's others, the years its test counted it better than what it was read against, each test year
// as a box, where its adjusted p stands on the scale of luck, its progress toward the count a verdict reads, and the
// trades its walk holds open and has closed, each worked by the core.
public sealed record PaperAccountView(
    string Key,
    string Family,
    string Standing,
    string Name,
    string Words,
    string Verdict,
    string? Against,
    string Unit,
    IReadOnlyList<PaperYearView> Years,
    PaperWalk? Walk,
    int Trades,
    int Ended,
    string? TooFew,
    DateOnly? Since,
    double? Adjusted = null,
    string? CountLine = null,
    string? Version = null,
    int Earlier = 0,
    string? Short = null,
    string? Changes = null,
    PaperOverlap? Overlap = null,
    int? Better = null,
    int? Counted = null,
    IReadOnlyList<PaperYear>? Boxes = null,
    PaperLuck? Luck = null,
    PaperProgress? Progress = null,
    int Open = 0,
    int Closed = 0)
{
    public const string Live = "live";

    public const string Provisional = "provisional";

    public const string Variant = "variant";

    public const string You = "you";

    public bool Head => Standing is Live or Provisional;
}

// One trade of an account's trade log: its stock, its buy session and price, its stop where it holds one, the session it
// ended on, its result in the account's unit, so far at tonight's close where it is open, why the account skipped it, and
// for a trade listed before the version the account counts, the version that listed it.
public sealed record PaperTradeView(string Ticker, DateOnly Bought, decimal Buy, decimal? Stop, DateOnly? Ended, double? Result, bool Open, string? Skipped, string? Version = null);

// One trade a live or provisional rule bought, as what the system bought draws it: the account and family that bought it,
// its stock and company, the night that listed it, the session it was bought on and its price, its plan's stop and target,
// whether its stop trails and the sessions it is given, the session it ended on, its figures tonight where it is open and
// where it ended, the variants holding it too, which end of its plan its fill opened past, and for a trade an earlier
// version listed, the version that listed it.
public sealed record PaperBoughtView(
    string Account,
    string Family,
    string Ticker,
    string? Company,
    DateOnly Listed,
    DateOnly Bought,
    decimal Buy,
    decimal? Stop,
    decimal? Target,
    bool Trails,
    int? Cap,
    DateOnly? Ended,
    PaperPosition? Open,
    PaperEnded? Closed,
    IReadOnlyList<string> AlsoHeld,
    string? Past,
    string? Version = null);

// A trade a family's variants took that its live or provisional rule did not take off the same night: its stock and
// company, the variants that took it by their short names, the night that listed it, the session it was bought on and its
// price, whether it is a book's holding, and its figures tonight or at its end.
public sealed record PaperOnlyView(string Ticker, string? Company, IReadOnlyList<string> By, DateOnly Listed, DateOnly Bought, decimal Buy, bool Book, PaperPosition? Open, PaperEnded? Closed);

// The index's fund over the days the live and provisional accounts' tiles count: its name, its return and the session the
// days start on, and where its series starts after them the session it does.
public sealed record PaperFundView(string Fund, double? Return, DateOnly? From, DateOnly? Starts);

// Paper trading as its page draws it: every account; the trades the live and provisional rules bought, open and closed, of
// the families the chips show, and those of earlier versions; the stocks they did not buy; the trades only the chosen
// family's variants took; and the fund's tile.
public sealed record PaperPageView(
    IReadOnlyList<PaperAccountView> Accounts,
    IReadOnlyList<PaperBoughtView> Open,
    IReadOnlyList<PaperBoughtView> Closed,
    IReadOnlyList<PaperBoughtView> Earlier,
    PaperNotBought NotBought,
    IReadOnlyList<PaperOnlyView> OnlyVariants,
    PaperFundView? Fund);

// The choices paper trading keeps in its link, one a part, and the link a choice opens with every other kept: the index,
// the family whose variants the chart adds, the closed tab of what the system bought, the family its chips show, the
// family the variants' part reads and the rule the scorecard reads, each first choice the key's absence.
public sealed record PaperLink(string Universe, string? Family = null, bool Closed = false, string? Show = null, string? Variants = null, string? Rule = null)
{
    public const string Route = EquityBrief.Web.App.SinglePageApp.PaperRoute;

    public const string FamilyKey = "family";

    public const string BoughtKey = "bought";

    public const string ShowKey = "show";

    public const string VariantsKey = "variants";

    public const string RuleKey = "rule";

    public const string ClosedValue = "closed";

    public string Href =>
        Route + "?universe=" + Uri.EscapeDataString(Universe)
        + (Family is { } family ? "&" + FamilyKey + "=" + Uri.EscapeDataString(family) : string.Empty)
        + (Closed ? "&" + BoughtKey + "=" + ClosedValue : string.Empty)
        + (Show is { } show ? "&" + ShowKey + "=" + Uri.EscapeDataString(show) : string.Empty)
        + (Variants is { } variants ? "&" + VariantsKey + "=" + Uri.EscapeDataString(variants) : string.Empty)
        + (Rule is { } rule ? "&" + RuleKey + "=" + Uri.EscapeDataString(rule) : string.Empty);
}

// Paper trading's marks: a tile a family's live or provisional account with the fund beside them, the chart of each
// account's return since its record began, what the live and provisional rules bought, what each family's variants did
// differently, a rule's scorecard with its test years and its record since it went live as two halves never added
// together, your own trades, and an account's trade log. Every figure is the core's or a stored row's; the marks draw and
// compute none.
// see: A rule's scorecard draws its test years and its record since it went live as two halves, and never adds them together
// see: A variant's picks are shown on its card when chosen and its results on paper trading beside its verdict, never on Tonight
public sealed partial class MarkRenderer
{
    // The line every page about paper accounts carries about entries.
    // see: A trade's entry is priced at the next session's open by a fill step of its own, and no rule's version moves because of it
    public const string EntryPriceLine = "Each entry is bought at the open of the session after the night that listed it.";

    // The line beneath the variants' table saying how a variant is adopted.
    public const string AdoptedLine = "A variant is adopted only through the monthly test and your approval on the Loop page.";

    // The warning beside a rule's test years where its setting was chosen on them.
    public const string TunedLine = "Its settings were tuned on these same years, so expect it to look worse from here on.";

    // What a provisional rule's record since it went live says in its place.
    public const string NoRecordLine = "A provisional rule keeps no record: it starts one when its family is frozen or a setting is approved, and its account counts every trade from its first meanwhile.";

    // The chart of each drawn account's return over the sessions its walk read, in per cent of the account's size: the
    // live or provisional accounts always, a chosen family's variants dashed, each line a family's mark with its key
    // beneath it, a key's press hiding or showing its line in the browser alone.
    public string PaperChart(IReadOnlyList<PaperAccountView> accounts, string? chosen)
    {
        var drawn = accounts.Where(account => account.Walk is { Values.Count: > 0 } && (account.Head || account.Standing == PaperAccountView.You || (account.Standing == PaperAccountView.Variant && account.Family == chosen))).ToArray();

        if (drawn.Length == 0)
        {
            return "<p class=\"degraded\" data-paper-chart=\"none\">No account has a session to draw yet.</p>";
        }

        var sessions = drawn.SelectMany(account => account.Walk!.Values.Select(value => value.Session)).Distinct().Order().ToArray();
        var at = sessions.Select((session, place) => (session, place)).ToDictionary(pair => pair.session, pair => pair.place);
        var returns = drawn.ToDictionary(account => account.Key, account => account.Walk!.Returns.Select(one => (one.Session, Return: one.Return * 100)).ToArray(), StringComparer.Ordinal);
        var low = Math.Min(0, returns.Values.SelectMany(line => line.Select(point => point.Return)).Min());
        var high = Math.Max(0, returns.Values.SelectMany(line => line.Select(point => point.Return)).Max());
        var span = high - low < 1 ? 1 : high - low;
        const double width = 720, height = 240, left = 44, right = 12, top = 12, bottom = 26;

        double X(DateOnly session) => sessions.Length == 1 ? left + ((width - left - right) / 2) : left + ((width - left - right) * at[session] / (sessions.Length - 1));

        double Y(double value) => top + ((height - top - bottom) * (high - value) / span);

        var svg = new StringBuilder();

        svg.Append(Invariant, $"<figure class=\"paper-chart\" data-lines=\"{drawn.Length}\" data-sessions=\"{sessions.Length}\" data-from=\"{DayOf(sessions[0])}\" data-to=\"{DayOf(sessions[^1])}\">");
        svg.Append(Invariant, $"<svg viewBox=\"0 0 {width} {height}\" role=\"img\" aria-label=\"Each drawn account's return in per cent of its size, session by session\">");
        svg.Append(Invariant, $"<line class=\"paper-zero\" x1=\"{left}\" x2=\"{width - right}\" y1=\"{Y(0):0.#}\" y2=\"{Y(0):0.#}\"/>");
        svg.Append(Invariant, $"<text class=\"paper-axis\" x=\"{left - 6}\" y=\"{Y(high) + 4:0.#}\" text-anchor=\"end\">{high:+0.#;-0.#;0}%</text>");
        svg.Append(Invariant, $"<text class=\"paper-axis\" x=\"{left - 6}\" y=\"{Y(low) + 4:0.#}\" text-anchor=\"end\">{low:+0.#;-0.#;0}%</text>");
        svg.Append(Invariant, $"<text class=\"paper-axis\" x=\"{left}\" y=\"{height - 6}\">{DayOf(sessions[0])}</text>");
        svg.Append(Invariant, $"<text class=\"paper-axis\" x=\"{width - right}\" y=\"{height - 6}\" text-anchor=\"end\">{DayOf(sessions[^1])}</text>");

        foreach (var account in drawn)
        {
            var points = returns[account.Key];
            var path = string.Join(" ", points.Select((point, place) => Formatted($"{(place == 0 ? "M" : "L")}{X(point.Session):0.#},{Y(point.Return):0.#}")));

            svg.Append(Invariant, $"<path class=\"paper-line{(account.Standing == PaperAccountView.Variant ? " dashed" : string.Empty)}\" data-account=\"{Escaped(account.Key)}\" data-family=\"{Escaped(account.Family)}\" data-standing=\"{account.Standing}\" d=\"{path}\"><title>{Escaped(account.Name)}</title></path>");
        }

        svg.Append("</svg><figcaption class=\"paper-keys\">");

        foreach (var account in drawn)
        {
            svg.Append(Invariant, $"<button type=\"button\" class=\"paper-key\" data-line=\"{Escaped(account.Key)}\" data-family=\"{Escaped(account.Family)}\" data-standing=\"{account.Standing}\" aria-pressed=\"true\" title=\"{Escaped(account.Words)}\"><span class=\"paper-key-mark\" data-family=\"{Escaped(account.Family)}\" data-standing=\"{account.Standing}\"></span>{Escaped(FamilyName(account.Family))} · {Escaped(account.Name)}</button>");
        }

        svg.Append("</figcaption></figure>");

        return svg.ToString();
    }

    // A fraction drawn as a signed per cent to a tenth, in the class its sign gives.
    static string PaperPercent(double fraction) =>
        SignedFigure(fraction * 100, 1, Formatted($"{Unsigned(fraction * 100, 1):+0.0;-0.0;0.0}%"));

    // A tile a family's live or provisional account in the one family order: its return since its record began, the trades
    // its walk holds open and has closed and the day it counts from, the version it counts under the pointer; and the
    // index's fund over the same days.
    public string PaperTiles(IReadOnlyList<PaperAccountView> accounts, PaperFundView? fund)
    {
        var heads = accounts.Where(account => account.Head).OrderBy(account => SetupFamilies.OrderOf(account.Family)).ToArray();
        var html = new StringBuilder();

        html.Append(Invariant, $"<div class=\"paper-tiles\" data-tiles=\"{heads.Length + (fund is null ? 0 : 1)}\">");

        foreach (var head in heads)
        {
            var made = head.Walk?.Return;

            html.Append(Invariant, $"<div class=\"paper-tile\" data-family=\"{Escaped(head.Family)}\" data-standing=\"{head.Standing}\" data-return=\"{(made is { } held ? held.ToString("R", Invariant) : "none")}\" data-open=\"{head.Open}\" data-closed=\"{head.Closed}\" data-since=\"{(head.Since is { } since ? DayOf(since) : "none")}\" title=\"{Escaped(Capitalised(head.Words) + (head.Version is { } version ? "; counts " + version : string.Empty))}\">");
            html.Append(Invariant, $"<div class=\"pt-name\"><span class=\"fam-swatch\" data-family=\"{Escaped(head.Family)}\"></span>{Escaped(FamilyName(head.Family))} <span class=\"status-pill {head.Standing}\">{Escaped(head.Name)}</span></div>");
            html.Append(made is { } figure ? Formatted($"<div class=\"pt-figure\">{PaperPercent(figure)}</div>") : "<div class=\"pt-figure degraded\">none yet</div>");
            html.Append(Invariant, $"<div class=\"pt-sub\">{(head.Since is { } from ? Formatted($"{head.Open} open · {head.Closed} closed · since <span class=\"day\">{DayOf(from)}</span>") : "no trade yet")}</div></div>");
        }

        if (fund is { } index)
        {
            html.Append(Invariant, $"<div class=\"paper-tile fund\" data-fund=\"{Escaped(index.Fund)}\" data-return=\"{(index.Return is { } held ? held.ToString("R", Invariant) : "none")}\" data-from=\"{(index.From is { } from ? DayOf(from) : "none")}\">");
            html.Append(Invariant, $"<div class=\"pt-name\">The index's fund, {Escaped(index.Fund)}</div>");
            html.Append(index.Return is { } figure ? Formatted($"<div class=\"pt-figure\">{PaperPercent(figure)}</div>") : "<div class=\"pt-figure degraded\">not read</div>");
            html.Append(index.From is not { } start
                ? "<div class=\"pt-sub\">no account has a day to count from yet</div>"
                : index.Return is null && index.Starts is { } starts
                    ? Formatted($"<div class=\"pt-sub\">its closes start on <span class=\"day\">{DayOf(starts)}</span>, after the days the tiles count</div>")
                    : Formatted($"<div class=\"pt-sub\">holding it over the same days, since <span class=\"day\">{DayOf(start)}</span></div>"));
            html.Append("</div>");
        }

        html.Append("</div>");

        return html.ToString();
    }

    // The family selector beside the chart: the live and provisional accounts alone, or a family's variants added, the
    // choice kept in the link with every other key.
    public static string PaperFamilies(IReadOnlyList<PaperAccountView> accounts, PaperLink link)
    {
        var html = new StringBuilder();

        html.Append("<nav class=\"paper-families\" aria-label=\"Add a family's variants to the chart\">");
        html.Append(Invariant, $"<a href=\"{Escaped((link with { Family = null }).Href)}\"{(link.Family is null ? " aria-current=\"true\"" : string.Empty)}>Live and provisional only</a>");

        foreach (var family in accounts.Where(account => account.Standing == PaperAccountView.Variant).Select(account => account.Family).Distinct().OrderBy(SetupFamilies.OrderOf))
        {
            html.Append(Invariant, $"<a class=\"fam-tag\" data-family=\"{Escaped(family)}\" href=\"{Escaped((link with { Family = family }).Href)}\"{(family == link.Family ? " aria-current=\"true\"" : string.Empty)}><span class=\"fam-swatch\" data-family=\"{Escaped(family)}\"></span>{Escaped(FamilyName(family))} with its variants</a>");
        }

        html.Append("</nav>");

        return html.ToString();
    }

    // What the live and provisional rules bought: the open and closed tabs and the family chips, each kept in the link with
    // the card held in place, the trades of the tab chosen newest first, the stocks not bought with each linking to its
    // chart, the trades of earlier versions folded, and the line about entries.
    public string PaperBought(PaperPageView page, PaperLink link)
    {
        var html = new StringBuilder();
        var families = page.Accounts.Where(account => account.Head).Select(account => account.Family).Distinct().OrderBy(SetupFamilies.OrderOf).ToArray();

        html.Append("<div class=\"paper-tabs\" role=\"group\" aria-label=\"Open or closed trades\">");
        html.Append(Invariant, $"<a class=\"chip\" data-filter=\"{PaperLink.BoughtKey}\" href=\"{Escaped((link with { Closed = false }).Href)}\" aria-pressed=\"{Flag(!link.Closed)}\">Open · {page.Open.Count}</a>");
        html.Append(Invariant, $"<a class=\"chip\" data-filter=\"{PaperLink.BoughtKey}\" href=\"{Escaped((link with { Closed = true }).Href)}\" aria-pressed=\"{Flag(link.Closed)}\">Closed · {page.Closed.Count}</a>");
        html.Append("</div>");
        html.Append("<div class=\"paper-chips\" role=\"group\" aria-label=\"The families whose trades are drawn\"><span class=\"chips-label\">Show</span>");
        html.Append(Invariant, $"<a class=\"chip\" data-filter=\"{PaperLink.ShowKey}\" href=\"{Escaped((link with { Show = null }).Href)}\" aria-pressed=\"{Flag(link.Show is null)}\">Every family</a>");

        foreach (var family in families)
        {
            html.Append(Invariant, $"<a class=\"chip\" data-filter=\"{PaperLink.ShowKey}\" data-family=\"{Escaped(family)}\" href=\"{Escaped((link with { Show = family }).Href)}\" aria-pressed=\"{Flag(link.Show == family)}\"><span class=\"fam-swatch\" data-family=\"{Escaped(family)}\"></span>&nbsp;{Escaped(FamilyName(family))}</a>");
        }

        html.Append("</div>");
        html.Append(link.Closed ? ClosedTrades(page.Closed) : OpenTrades(page.Open));
        html.Append(NotBought(page.NotBought));

        if (page.Earlier.Count > 0)
        {
            html.Append(Invariant, $"<details class=\"paper-earlier\" data-earlier=\"{page.Earlier.Count}\"><summary>Earlier versions: {page.Earlier.Count} {(page.Earlier.Count == 1 ? "trade" : "trades")} a rule's earlier versions listed, left out of every figure on this page</summary>");
            html.Append("<div class=\"tbl-wrap\"><table class=\"paper-bought\"><thead><tr><th>Stock</th><th>Setup</th><th>Listed by</th><th class=\"r\">Bought</th><th>Open or how it ended</th><th class=\"r\">Result</th></tr></thead><tbody>");

            foreach (var trade in page.Earlier)
            {
                html.Append(Invariant, $"<tr data-ticker=\"{Escaped(trade.Ticker)}\" data-bought=\"{DayOf(trade.Bought)}\" data-version=\"{Escaped(trade.Version ?? string.Empty)}\">");
                html.Append(StockCell(trade.Ticker, trade.Company, null));
                html.Append(FamilyCell(trade.Family));
                html.Append(Invariant, $"<td><span title=\"{Escaped(trade.Version ?? string.Empty)}\">an earlier version</span></td>");
                html.Append(Invariant, $"<td class=\"r num\">{Figures.Price(trade.Buy)}<span class=\"pb-day\">{DayOf(trade.Bought)}</span></td>");
                html.Append(trade.Closed is { } ended ? Formatted($"<td>{Escaped(Capitalised(ended.Ending))}</td>") : "<td><span class=\"pill\">open</span></td>");
                html.Append(Invariant, $"<td class=\"r num\">{Result(trade.Stop is null, trade.Closed is { } shut ? (shut.Risks, shut.Fraction) : (trade.Open?.Risks, trade.Open?.Fraction))}</td></tr>");
            }

            html.Append("</tbody></table></div></details>");
        }

        html.Append(Invariant, $"<p class=\"paper-entry\">{Escaped(EntryPriceLine)}</p>");

        return html.ToString();
    }

    // A trade's result: a swing trade's in risks with its per cent beneath, a holding's in per cent alone; not read where
    // none is.
    static string Result(bool book, (double? Risks, double? Fraction) result) =>
        book
            ? result.Fraction is { } held ? PaperPercent(held) : "<span class=\"degraded\">not read</span>"
            : result.Risks is { } risks
                ? SignedTwo(risks) + " risks" + (result.Fraction is { } share ? Formatted($"<span class=\"pb-day\">{PaperPercent(share)}</span>") : string.Empty)
                : result.Fraction is { } only ? PaperPercent(only) : "<span class=\"degraded\">not read</span>";

    static string StockCell(string ticker, string? company, string? also) =>
        Formatted($"<td class=\"pb-stock\"><a href=\"#/chart/{Uri.EscapeDataString(ticker)}\">{Escaped(ticker)}</a>{(company is { } name ? Formatted($"<span class=\"pb-co\">{Escaped(name)}</span>") : string.Empty)}{(also is { } said ? Formatted($"<span class=\"pb-also\">{Escaped(said)}</span>") : string.Empty)}</td>");

    static string FamilyCell(string family) =>
        Formatted($"<td><span class=\"fam-tag\" data-family=\"{Escaped(family)}\"><span class=\"fam-swatch\" data-family=\"{Escaped(family)}\"></span>{Escaped(FamilyName(family))}</span></td>");

    // The variants holding a stock too, in words: one, two joined by "and", more with the last after "and".
    static string? AlsoHeld(IReadOnlyList<string> names) =>
        names.Count == 0 ? null
            : (names.Count == 1 ? names[0] : string.Join(", ", names.Take(names.Count - 1)) + " and " + names[^1]) + (names.Count == 1 ? " holds it too" : " hold it too");

    // The open trades: the stock and its company linking to its chart with the variants holding it too, its setup, its buy
    // and the session it was bought on, its stop tonight, its target or that it trails or none for a holding, tonight's close
    // with the sessions held of its cap, its result so far, and where it stands in words.
    string OpenTrades(IReadOnlyList<PaperBoughtView> trades)
    {
        if (trades.Count == 0)
        {
            return "<p class=\"degraded\" data-bought=\"none\">No trade of these rules is open.</p>";
        }

        var html = new StringBuilder();

        html.Append(Invariant, $"<div class=\"tbl-wrap\"><table class=\"paper-bought\" data-tab=\"open\" data-rows=\"{trades.Count}\"><thead><tr>");
        html.Append(TippedHeading("Stock", "the stock, opening its chart, with its company and the variants of its rule holding it open too"));
        html.Append(TippedHeading("Setup", "the setup family whose live or provisional rule bought it"));
        html.Append(TippedHeading("Bought", "its buy at the open of the session after the night that listed it, and that session", "r"));
        html.Append(TippedHeading("Stop", "its stop tonight: the plan's, or a trailing stop raised to the highest close since its listing less the distance it trails", "r"));
        html.Append(TippedHeading("Target", "its plan's target, or that its stop trails and it has none, or none for a holding sold only at a rebalance", "r"));
        html.Append(TippedHeading("Last close", "tonight's close, and the sessions since its listing night against the sessions its plan gives it", "r"));
        html.Append(TippedHeading("So far", "its result at tonight's close in multiples of the risk its plan's close put up, with its move in per cent beneath; a holding's in per cent alone", "r"));
        html.Append(TippedHeading("Where it stands", "near its target or near its stop within one typical day's move of either, on track otherwise, or that its buy opened past its stop or its target; a holding's lead over its sector tonight"));
        html.Append("</tr></thead><tbody>");

        foreach (var trade in trades)
        {
            var open = trade.Open;
            var book = trade.Stop is null;

            html.Append(Invariant, $"<tr data-ticker=\"{Escaped(trade.Ticker)}\" data-family=\"{Escaped(trade.Family)}\" data-account=\"{Escaped(trade.Account)}\" data-bought=\"{DayOf(trade.Bought)}\" data-standing=\"{Escaped(open?.Standing ?? string.Empty)}\">");
            html.Append(StockCell(trade.Ticker, trade.Company, AlsoHeld(trade.AlsoHeld)));
            html.Append(FamilyCell(trade.Family));
            html.Append(Invariant, $"<td class=\"r num\">{Figures.Price(trade.Buy)}<span class=\"pb-day\">{DayOf(trade.Bought)}</span></td>");
            html.Append(book
                ? "<td class=\"r\"><span class=\"degraded\">none</span></td>"
                : Formatted($"<td class=\"r num\">{(open?.Stop is { } stop ? Figures.Price(stop) : "<span class=\"degraded\">none</span>")}{(trade.Trails && open?.Stop is { } raised && trade.Stop is { } first && raised > first ? Formatted($"<span class=\"pb-day\">raised from {Figures.Price(first)}</span>") : string.Empty)}</td>"));
            html.Append(book
                ? "<td class=\"r\"><span class=\"degraded\">none</span></td>"
                : trade.Target is { } target ? Formatted($"<td class=\"r num\">{Figures.Price(target)}</td>") : "<td class=\"r\">it trails</td>");
            html.Append(Invariant, $"<td class=\"r num\">{(open?.Close is { } shut ? Figures.Price(shut) : "<span class=\"degraded\">not stored</span>")}<span class=\"pb-day\">{Held(open?.Held, trade.Cap)}</span></td>");
            html.Append(Invariant, $"<td class=\"r num\">{Result(book, (open?.Risks, open?.Fraction))}</td>");
            html.Append(Invariant, $"<td><span class=\"pb-standing {StandingClass(open?.Standing)}\" data-standing=\"{Escaped(open?.Standing ?? string.Empty)}\">{Escaped(Capitalised(open?.Standing ?? HeavyweightStandings.NotRead))}</span></td></tr>");
        }

        html.Append("</tbody></table></div>");

        return html.ToString();
    }

    // The class an open trade's standing is drawn in: the gain's green near its target or a holding still leading, the
    // loss's red near its stop, opened under it or a holding under its average, the caution's amber opened over its target
    // or a holding that lost its lead, and ink on track or where nothing was read.
    static string StandingClass(string? standing) => standing switch
    {
        PaperAccount.NearTarget => "near-target",
        PaperAccount.NearStop => "near-stop",
        PaperAccount.OpenedUnderStop => "opened-under",
        PaperAccount.OpenedOverTarget => "opened-over",
        HeavyweightStandings.StillLeading => "leading",
        HeavyweightStandings.LostItsLead => "lost-lead",
        HeavyweightStandings.BelowItsAverage => "below-average",
        _ => "steady",
    };

    // Sessions held: so many of its cap, or so many where it has none.
    static string Held(int? held, int? cap) =>
        held is not { } count ? "held a span the calendar does not reach"
            : cap is { } of ? Formatted($"{count} of {of} sessions")
            : Formatted($"{count} {(count == 1 ? "session" : "sessions")}");

    // The closed trades: the stock and its company, its setup, its buy and its sale with their sessions, how it ended with
    // the sessions it was held, its result, and its edge over the same plan on every stock, a holding's over its size cut.
    string ClosedTrades(IReadOnlyList<PaperBoughtView> trades)
    {
        if (trades.Count == 0)
        {
            return "<p class=\"degraded\" data-bought=\"none\">No trade of these rules has closed yet.</p>";
        }

        var html = new StringBuilder();

        html.Append(Invariant, $"<div class=\"tbl-wrap\"><table class=\"paper-bought\" data-tab=\"closed\" data-rows=\"{trades.Count}\"><thead><tr>");
        html.Append(TippedHeading("Stock", "the stock, opening its chart, with its company"));
        html.Append(TippedHeading("Setup", "the setup family whose live or provisional rule bought it"));
        html.Append(TippedHeading("Bought", "its buy at the open of the session after the night that listed it, and that session", "r"));
        html.Append(TippedHeading("Sold", "the price it was sold at, at its target, at a close through its stop, at its cap's close or at its book's sale, and that session", "r"));
        html.Append(TippedHeading("How it ended", "what ended it, in words, and the sessions from its listing night to its end"));
        html.Append(TippedHeading("Result", "what it came to in multiples of the risk its plan's close put up, with its move in per cent beneath; a holding's in per cent alone", "r"));
        html.Append(TippedHeading("Against the same plan on every stock", "its result less the same plan bought on every member of the index that night, in risks; a holding's less its size cut's return, in points; not read until every member's trade of that plan has had its cap", "r"));
        html.Append("</tr></thead><tbody>");

        foreach (var trade in trades)
        {
            var closed = trade.Closed;
            var book = trade.Stop is null;

            html.Append(Invariant, $"<tr data-ticker=\"{Escaped(trade.Ticker)}\" data-family=\"{Escaped(trade.Family)}\" data-account=\"{Escaped(trade.Account)}\" data-bought=\"{DayOf(trade.Bought)}\" data-ended=\"{(trade.Ended is { } end ? DayOf(end) : "none")}\" data-edge=\"{(closed?.Edge is { } held ? held.ToString("R", Invariant) : "none")}\">");
            html.Append(StockCell(trade.Ticker, trade.Company, null));
            html.Append(FamilyCell(trade.Family));
            html.Append(Invariant, $"<td class=\"r num\">{Figures.Price(trade.Buy)}<span class=\"pb-day\">{DayOf(trade.Bought)}</span></td>");
            html.Append(Invariant, $"<td class=\"r num\">{(closed?.Sale is { } sale ? Figures.Price(sale) : "<span class=\"degraded\">not read</span>")}<span class=\"pb-day\">{(trade.Ended is { } on ? DayOf(on) : string.Empty)}</span></td>");
            html.Append(Invariant, $"<td>{Escaped(Capitalised(closed?.Ending ?? PaperAccount.NoResult))}<span class=\"pb-day\">{(closed?.Held is { } kept ? Formatted($"held {kept} {(kept == 1 ? "session" : "sessions")}") : string.Empty)}{(trade.Past switch { EquityBrief.Core.Ledger.FillPast.Stop => "; " + PaperAccount.OpenedUnderStop, EquityBrief.Core.Ledger.FillPast.Target => "; " + PaperAccount.OpenedOverTarget, _ => string.Empty })}</span></td>");
            html.Append(Invariant, $"<td class=\"r num\">{Result(book, (closed?.Risks, closed?.Fraction))}</td>");
            html.Append(closed?.Edge is { } edge
                ? Formatted($"<td class=\"r num\">{(book ? SignedTwo(edge * 100) + " points" : SignedTwo(edge) + " risks")}</td>")
                : "<td class=\"r\"><span class=\"degraded\">not read yet</span></td>");
            html.Append("</tr>");
        }

        html.Append("</tbody></table></div>");

        return html.ToString();
    }

    // The listed stocks not bought, in one line: how many, then each reason with its stocks, each linking to its chart.
    static string NotBought(PaperNotBought unbought) =>
        unbought.Count == 0
            ? "<p class=\"paper-unbought\" data-unbought=\"0\">Every stock these rules listed was bought.</p>"
            : Formatted($"<p class=\"paper-unbought\" data-unbought=\"{unbought.Count}\">Not bought: {unbought.Count} listed {(unbought.Count == 1 ? "stock" : "stocks")}. ")
                + string.Join(" ", unbought.Reasons.Select(reason => Formatted($"<span class=\"pu-reason\" data-count=\"{reason.Stocks.Count}\">{Escaped(Capitalised(reason.Why))} ({reason.Stocks.Count}): {string.Join(", ", reason.Stocks.Select(stock => Formatted($"<a href=\"#/chart/{Uri.EscapeDataString(stock.Ticker)}\" title=\"listed {DayOf(stock.Listed)}\">{Escaped(stock.Ticker)}</a>")))}.</span>")))
                + "</p>";

    // A family's choice for the variants' part or the scorecard, kept in the link under its key, its first family the key's
    // absence.
    static string FamilyPick(string key, string label, IReadOnlyList<(string Family, string Words)> options, string chosen) =>
        Formatted($"<label class=\"paper-pick\"><span class=\"chips-label\">Setup</span> <select data-merge-key=\"{key}\" aria-label=\"{Escaped(label)}\">")
        + string.Concat(options.Select((option, at) => Formatted($"<option value=\"{(at == 0 ? string.Empty : Escaped(option.Family))}\"{(option.Family == chosen ? " selected" : string.Empty)}>{Escaped(option.Words)}</option>")))
        + "</select></label>";

    // An account's trade log on Past picks, on the same index.
    static string LogHref(string universe, string key) =>
        Formatted($"#/picks?universe={Uri.EscapeDataString(universe)}&amp;account={Uri.EscapeDataString(key)}");

    // What a family's variants did differently: the family chosen in the link, one at a time; a row its live or provisional
    // rule and a row a variant, each with what it changes in words, its trades, those shared and those its alone, and its
    // return so far with its record's label, its verdict beside it; the trades only a variant took beneath; and the line
    // saying how a variant is adopted. A family with no variant says so.
    public string PaperVariants(PaperPageView page, PaperLink link, string family)
    {
        var html = new StringBuilder();
        var families = page.Accounts.Where(account => account.Head).OrderBy(account => SetupFamilies.OrderOf(account.Family)).ToArray();
        var rows = page.Accounts.Where(account => account.Family == family && account.Standing != PaperAccountView.You).OrderBy(account => account.Head ? 0 : 1).ToArray();
        var variants = rows.Count(account => !account.Head);

        html.Append(FamilyPick(
            PaperLink.VariantsKey,
            "The setup whose variants are drawn",
            [.. families.Select(head =>
            {
                var count = page.Accounts.Count(account => account.Family == head.Family && account.Standing == PaperAccountView.Variant);

                return (head.Family, Formatted($"{FamilyName(head.Family)} · {(head.Standing == PaperAccountView.Live ? "live" : "provisional")} rule{(count == 0 ? ", no variant" : Formatted($" and {count} {(count == 1 ? "variant" : "variants")}"))}"));
            })],
            family));

        if (variants == 0)
        {
            html.Append(Invariant, $"<p class=\"degraded\" data-variants=\"none\">No variant is registered for {Escaped(FamilyName(family))} on this index, so its rule alone trades.</p>");
        }

        html.Append(Invariant, $"<div class=\"tbl-wrap\"><table class=\"paper-variants\" data-family=\"{Escaped(family)}\" data-rows=\"{rows.Length}\"><thead><tr>");
        html.Append(TippedHeading("Rule", "the live or provisional rule first and each variant by its number, its verdict in the newest monthly test beside it and its full words under the pointer"));
        html.Append(TippedHeading("What it changes", "the live rule's settings in short words, and each variant's settings that differ from them"));
        html.Append(TippedHeading("Trades", "the trades its own list or book kept since its account started", "r"));
        html.Append(TippedHeading("Shared", "a variant's trades the live rule also took in the same stock off the same night; on the live rule's row, its trades any variant also took", "r", "Shared with the live rule"));
        html.Append(TippedHeading("Only this one", "the trades no other rule of the family took in the same stock off the same night", "r"));
        html.Append(TippedHeading("Return so far", "its account's return on your size since it started, with its record's too-few label until it holds the test's count", "r"));
        html.Append("</tr></thead><tbody>");

        foreach (var account in rows)
        {
            html.Append(Invariant, $"<tr data-account=\"{Escaped(account.Key)}\" data-standing=\"{account.Standing}\" data-trades=\"{account.Overlap?.Trades ?? account.Trades}\" data-shared=\"{account.Overlap?.Shared ?? 0}\" data-only=\"{account.Overlap?.Only ?? 0}\">");
            html.Append(Invariant, $"<td class=\"pv-rule\"><a class=\"paper-log\" href=\"{LogHref(link.Universe, account.Key)}\" title=\"{Escaped(Capitalised(account.Words) + (account.Version is { } version ? "; counts " + version : string.Empty) + (account.Since is { } since ? "; from " + DayOf(since) : string.Empty))}\">{Escaped(account.Short ?? account.Name)}</a><span class=\"pv-verdict\" data-verdict=\"{Escaped(account.Verdict)}\">{Escaped(account.Verdict)}</span></td>");
            html.Append(Invariant, $"<td>{Escaped(account.Changes ?? string.Empty)}</td>");
            html.Append(Invariant, $"<td class=\"r num\">{account.Overlap?.Trades ?? account.Trades}</td><td class=\"r num\">{account.Overlap?.Shared ?? 0}</td><td class=\"r num\">{account.Overlap?.Only ?? 0}</td>");
            html.Append(Invariant, $"<td class=\"r num\" data-return=\"{(account.Walk?.Return is { } held ? held.ToString("R", Invariant) : "none")}\">{(account.Walk?.Return is { } made ? PaperPercent(made) : "<span class=\"degraded\">none yet</span>")}{(account.TooFew is { } few ? Formatted($"<span class=\"too-few\">{Escaped(few)}</span>") : string.Empty)}</td></tr>");
        }

        html.Append("</tbody></table></div>");

        if (variants > 0)
        {
            html.Append("<h3 class=\"pv-only-h\">Bought only by a variant</h3>");

            if (page.OnlyVariants.Count == 0)
            {
                html.Append("<p class=\"degraded\" data-only=\"none\">No variant bought a stock off a night its rule did not.</p>");
            }
            else
            {
                html.Append(Invariant, $"<div class=\"tbl-wrap\"><table class=\"paper-only\" data-rows=\"{page.OnlyVariants.Count}\"><thead><tr><th>Stock</th><th>Which variant</th><th class=\"r\">Bought</th><th>Open or how it ended</th><th class=\"r\">Result</th></tr></thead><tbody>");

                foreach (var trade in page.OnlyVariants)
                {
                    html.Append(Invariant, $"<tr data-ticker=\"{Escaped(trade.Ticker)}\" data-bought=\"{DayOf(trade.Bought)}\" data-by=\"{Escaped(string.Join("|", trade.By))}\">");
                    html.Append(StockCell(trade.Ticker, trade.Company, null));
                    html.Append(Invariant, $"<td>{Escaped(string.Join(", ", trade.By))}</td>");
                    html.Append(Invariant, $"<td class=\"r num\">{Figures.Price(trade.Buy)}<span class=\"pb-day\">{DayOf(trade.Bought)}</span></td>");
                    html.Append(trade.Closed is { } ended ? Formatted($"<td>{Escaped(Capitalised(ended.Ending))}</td>") : "<td><span class=\"pill\">open</span></td>");
                    html.Append(Invariant, $"<td class=\"r num\">{Result(trade.Book, trade.Closed is { } shut ? (shut.Risks, shut.Fraction) : (trade.Open?.Risks, trade.Open?.Fraction))}</td></tr>");
                }

                html.Append("</tbody></table></div>");
            }
        }

        html.Append(Invariant, $"<p class=\"paper-adopted\">{Escaped(AdoptedLine)}</p>");

        return html.ToString();
    }

    // A rule's scorecard, the rule chosen in the link one at a time: its header with its full words and the version it counts
    // under the pointer; on history, each test year a box saying better or worse with its edge, and where its adjusted p
    // stands on the scale of luck in words, with the warning where its setting was chosen on those years; since it went live,
    // its progress toward the count a verdict reads and its result so far, not evidence yet, or for a provisional rule that
    // it keeps no record; the two halves never added together; and its variants folding out beneath.
    public string PaperScorecard(PaperPageView page, PaperLink link, string family)
    {
        var html = new StringBuilder();
        var heads = page.Accounts.Where(account => account.Head).OrderBy(account => SetupFamilies.OrderOf(account.Family)).ToArray();

        html.Append(FamilyPick(
            PaperLink.RuleKey,
            "The rule whose scorecard is drawn",
            [.. heads.Select(head => (head.Family, Formatted($"{FamilyName(head.Family)} · {(head.Standing == PaperAccountView.Live ? "live" : "provisional")} rule")))],
            family));

        if (heads.FirstOrDefault(account => account.Family == family) is not { } head)
        {
            return html.ToString() + "<p class=\"degraded\" data-score=\"none\">No rule stands for this family on this index.</p>";
        }

        var variants = page.Accounts.Where(account => account.Family == family && account.Standing == PaperAccountView.Variant).ToArray();

        html.Append(Invariant, $"<div class=\"score\" data-family=\"{Escaped(head.Family)}\" data-standing=\"{head.Standing}\" data-verdict=\"{Escaped(head.Verdict)}\">");
        html.Append(Invariant, $"<div class=\"score-h\"><span class=\"fam-swatch\" data-family=\"{Escaped(head.Family)}\"></span><h3>{Escaped(FamilyName(head.Family))}</h3> <span class=\"status-pill {head.Standing}\">{Escaped(head.Name)}</span> <span class=\"score-rule\" title=\"{Escaped(Capitalised(head.Words) + (head.Version is { } version ? "; counts " + version : string.Empty))}\">{Escaped(head.Changes ?? head.Words)}</span> <a class=\"paper-log\" href=\"{LogHref(link.Universe, head.Key)}\">trade log</a></div>");
        html.Append("<div class=\"score-halves\">");
        html.Append(History(head));
        html.Append(SinceLive(head));
        html.Append("</div>");

        if (variants.Length == 0)
        {
            html.Append("<p class=\"degraded score-none\" data-score-variants=\"none\">No variant is registered for this rule.</p>");
        }
        else
        {
            html.Append(Invariant, $"<details class=\"score-variants\" data-variants=\"{variants.Length}\"><summary>Its {variants.Length} {(variants.Length == 1 ? "variant" : "variants")}</summary><div class=\"tbl-wrap\"><table class=\"score-table\"><thead><tr>");
            html.Append(TippedHeading("Variant", "each variant by its number with the settings that differ from the rule's"));
            html.Append(TippedHeading("On history", "its verdict in the newest monthly test, read against the live or provisional rule; its adjusted p under the pointer"));
            html.Append(TippedHeading("Years better", "the complete test years it stood above the live or provisional rule, of those the test counted", "r", "Years better than the live rule"));
            html.Append(TippedHeading("Since live", "its own decided trades, or a book's months, against the count a verdict reads", "r"));
            html.Append("</tr></thead><tbody>");

            foreach (var variant in variants)
            {
                html.Append(Invariant, $"<tr data-account=\"{Escaped(variant.Key)}\" data-verdict=\"{Escaped(variant.Verdict)}\" data-better=\"{(variant.Better is { } better ? better.ToString(Invariant) : "none")}\">");
                html.Append(Invariant, $"<td><b>{Escaped(variant.Short ?? variant.Name)}</b><span class=\"pb-co\">{Escaped(variant.Changes ?? variant.Words)}</span></td>");
                html.Append(Invariant, $"<td><span class=\"pill\"{(variant.Adjusted is { } p ? Formatted($" title=\"adjusted p {p.ToString("0.0000", Invariant)} against the bar {LoopGate.Bar.ToString("0.0000", Invariant)}\"") : string.Empty)}>{Escaped(variant.Verdict)}</span></td>");
                html.Append(Invariant, $"<td class=\"r num\">{(variant.Better is { } of && variant.Counted is { } counted ? Formatted($"{of} of {counted}") : "<span class=\"degraded\">not read</span>")}</td>");
                html.Append(Invariant, $"<td class=\"r num\">{Progressed(variant.Progress, variant.Standing)}</td></tr>");
            }

            html.Append("</tbody></table></div></details>");
        }

        html.Append("</div>");

        return html.ToString();
    }

    // A record's progress in words: decided trades or a book's months of the count a verdict reads.
    static string Progressed(PaperProgress? progress, string standing) =>
        standing == PaperAccountView.Provisional ? "no record kept"
            : progress is not { } held ? "<span class=\"degraded\">not read</span>"
            : held.Needed == LoopScreens.BookMonths ? Formatted($"{held.Counted} of {held.Needed} months")
            : Formatted($"{held.Counted} of {held.Needed} decided");

    // The scorecard's history half: the test years as boxes, better or worse with each year's edge, and the scale of luck.
    static string History(PaperAccountView head)
    {
        var html = new StringBuilder();
        var boxes = head.Boxes ?? [];
        var book = head.Unit == LoopPoints;

        html.Append(Invariant, $"<div class=\"score-half history\"><div class=\"sh-h\"><span class=\"sh-lbl\">{(boxes.Count > 0 ? Formatted($"On history, {boxes.Min(box => box.Year)} to {boxes.Max(box => box.Year)}") : "On history")}</span> <span class=\"pill verdict\" data-verdict=\"{Escaped(head.Verdict)}\"{(head.Adjusted is { } p ? Formatted($" title=\"adjusted p {p.ToString("0.0000", Invariant)} against the bar {LoopGate.Bar.ToString("0.0000", Invariant)}\"") : string.Empty)}>{Escaped(head.Verdict)}</span></div>");

        if (boxes.Count == 0)
        {
            html.Append(Invariant, $"<p class=\"degraded\" data-history=\"none\">{(head.Verdict == RuleVerdict.NotTested ? "No monthly test has read this rule on this index yet." : "The test could not walk this rule over the history, so it holds no year and no chance of luck to draw.")}</p></div>");

            return html.ToString();
        }

        html.Append("<h4>Did it beat the easy alternative?</h4>");
        html.Append(Invariant, $"<p class=\"sh-line\">{(head.Better is { } better && head.Counted is { } counted and > 0 ? Formatted($"Better in {better} of {counted} complete {(counted == 1 ? "year" : "years")}. ") : string.Empty)}Each box is a test year: did its {(book ? "holdings" : "trades")} beat {(book ? BookAgainst(head.Family, "their") : "the same plan bought on every stock that night")}, and by how much, in {(book ? "points a month" : "risks a trade")}.</p>");
        html.Append("<div class=\"sh-years\">");

        foreach (var box in boxes)
        {
            var kind = box.Word == PaperAccount.Better ? "better" : box.Word == PaperAccount.Worse ? "worse" : box.Word == PaperAccount.Even ? "even" : "none";
            var year = head.Years.FirstOrDefault(row => row.Year == box.Year);

            html.Append(Invariant, $"<div class=\"sy {kind}\" data-year=\"{box.Year}\" data-word=\"{Escaped(box.Word)}\" data-edge=\"{(box.Edge is { } held ? held.ToString("R", Invariant) : "none")}\"{(year is null ? string.Empty : Formatted($" title=\"{box.Year}: {year.Units} {(book ? (year.Units == 1 ? "month" : "months") : (year.Units == 1 ? "trade" : "trades"))}, a total of {Escaped(Plain(head.Unit, year.Total))} against {Escaped(Plain(head.Unit, year.ComparedTotal))}\""))}>");
            html.Append(Invariant, $"<span class=\"sy-y\">{box.Year}{(box.Complete ? string.Empty : " so far")}</span><b>{Escaped(box.Word)}</b>");
            html.Append(box.Edge is { } edge ? Formatted($"<span class=\"sy-e\">{(book ? SignedTwo(edge * 100) : SignedTwo(edge))}</span>") : string.Empty);
            html.Append("</div>");
        }

        html.Append("</div>");
        html.Append("<h4>Could that just be luck?</h4>");

        if (head.Luck is { } luck)
        {
            html.Append(LuckScale(luck, head.Adjusted));
            html.Append(Invariant, $"<p class=\"sh-line\" data-zone=\"{Escaped(luck.Zone)}\">{Escaped(luck.Zone)}: a rule picking at random would do this well {Escaped(luck.Words)}. To count as real it needs fewer than 1 in {PaperAccount.RealOneIn}.</p>");
        }
        else
        {
            html.Append("<p class=\"degraded\" data-luck=\"none\">Too few blocks of sessions were read to say how often luck alone would do as well.</p>");
        }

        if (head.Head)
        {
            html.Append(Invariant, $"<p class=\"sh-warn\">{Escaped(TunedLine)}</p>");
        }

        html.Append("</div>");

        return html.ToString();
    }

    // A total in its unit, written plainly for a title: a book's in points, a swing rule's in risks.
    static string Plain(string unit, double total) =>
        unit == LoopPoints ? Formatted($"{Unsigned(total * 100, 2):+0.00;-0.00;0.00} points") : Formatted($"{Unsigned(total, 2):+0.00;-0.00;0.00} risks");

    // The scale of luck: its three zones at their shares of the width, each named beneath it, the rule's mark at its place,
    // its adjusted p against the bar under the pointer.
    static string LuckScale(PaperLuck luck, double? adjusted)
    {
        const double Wide = 300;
        var easily = Wide * PaperAccount.EasilyShare;
        var maybe = Wide * PaperAccount.MaybeShare;
        var real = Wide - easily - maybe;
        var mark = Math.Clamp(luck.Place * Wide, 8, Wide - 8);
        var said = adjusted is { } p ? Formatted($"adjusted p {p.ToString("0.0000", Invariant)} against the bar {LoopGate.Bar.ToString("0.0000", Invariant)}") : "no adjusted p read";

        return Formatted($"<figure class=\"luck\" data-place=\"{luck.Place.ToString("R", Invariant)}\" title=\"{said}\"><svg class=\"luck-scale\" viewBox=\"0 0 {Wide} 40\" role=\"img\" aria-label=\"{Escaped(luck.Zone)}, {said}\"><rect class=\"ls-easily\" x=\"0\" y=\"7\" width=\"{Number(easily - 2)}\" height=\"10\" rx=\"5\"></rect><rect class=\"ls-maybe\" x=\"{Number(easily)}\" y=\"7\" width=\"{Number(maybe - 2)}\" height=\"10\"></rect><rect class=\"ls-real\" x=\"{Number(easily + maybe)}\" y=\"7\" width=\"{Number(real)}\" height=\"10\" rx=\"5\"></rect><circle class=\"ls-mark\" cx=\"{Number(mark)}\" cy=\"12\" r=\"7\"></circle><text class=\"ls-word\" x=\"0\" y=\"34\">{Escaped(PaperAccount.EasilyLuck)}</text><text class=\"ls-word\" x=\"{Number(easily)}\" y=\"34\">{Escaped(PaperAccount.MaybeLuck)}</text><text class=\"ls-word\" x=\"{Number(Wide)}\" y=\"34\" text-anchor=\"end\">{Escaped(PaperAccount.Real)}</text></svg></figure>");
    }

    // What a book's holdings are read against: the sector heavyweights' their size cut, and from 22.11 Quality at a fair
    // price's the index's stocks over the same days.
    static string BookAgainst(string family, string whose) =>
        family == EquityBrief.Core.Families.QualityRule.Name ? "the index's stocks over the same days" : whose + " size cut";

    // The scorecard's half since it went live: its progress toward the count a verdict reads as a bar with where it counts
    // from, and its mean edge so far, not evidence yet until the count is met; a provisional rule's saying it keeps none.
    static string SinceLive(PaperAccountView head)
    {
        var html = new StringBuilder();
        var book = head.Unit == LoopPoints;

        html.Append(Invariant, $"<div class=\"score-half live\"><div class=\"sh-h\"><span class=\"sh-lbl\">Since it went live</span>{(head.Standing == PaperAccountView.Provisional ? string.Empty : head.TooFew is null ? " <span class=\"pill\">its count is met</span>" : " <span class=\"pill\">not evidence yet</span>")}</div>");

        if (head.Standing == PaperAccountView.Provisional || head.Progress is not { } progress)
        {
            html.Append(Invariant, $"<p class=\"sh-line\" data-record=\"none\">{Escaped(NoRecordLine)}</p></div>");

            return html.ToString();
        }

        html.Append(Invariant, $"<p class=\"sh-count\" data-counted=\"{progress.Counted}\" data-needed=\"{progress.Needed}\">{(book ? Formatted($"{progress.Counted} of {progress.Needed} months held") : Formatted($"{progress.Counted} of {progress.Needed} trades decided"))}</p>");
        html.Append(Invariant, $"<svg class=\"sh-progress\" viewBox=\"0 0 300 10\" role=\"img\" aria-label=\"{progress.Counted} of {progress.Needed}\" data-share=\"{progress.Share.ToString("R", Invariant)}\"><rect class=\"sp-track\" x=\"0\" y=\"0\" width=\"300\" height=\"10\" rx=\"5\"></rect><rect class=\"sp-fill\" x=\"0\" y=\"0\" width=\"{Number(300 * progress.Share)}\" height=\"10\" rx=\"5\"></rect></svg>");
        html.Append(Invariant, $"<div class=\"sh-ends\"><span>{(head.Since is { } since ? Formatted($"counting from <span class=\"day\">{DayOf(since)}</span>") : "counting from its first trade")}</span><span>{progress.Needed} {(book ? "months" : "decided trades")} for a verdict</span></div>");
        html.Append(progress.MeanEdge is { } edge
            ? Formatted($"<p class=\"sh-line\" data-edge=\"{edge.ToString("R", Invariant)}\">So far: {(book ? SignedTwo(edge * 100) + " points a holding against " + BookAgainst(head.Family, "its") : SignedTwo(edge) + " risks a trade against the same plan on every stock")}, over {progress.Edged} decided {(progress.Edged == 1 ? (book ? "holding" : "trade") : (book ? "holdings" : "trades"))} whose comparison is read.{(head.TooFew is null ? string.Empty : " Not evidence yet.")}</p>")
            : "<p class=\"sh-line\" data-edge=\"none\">No decided trade has its comparison read yet, so there is nothing to read.</p>");
        html.Append("</div>");

        return html.ToString();
    }

    // Your own trades on the index, as their account: its count in one line, its return, its worst drop and its return less
    // the fund's, the day it starts and its trade log; or that you have taken none.
    public string PaperYours(PaperAccountView? you, string universe)
    {
        if (you is null || you.Trades == 0)
        {
            return "<p class=\"degraded\" data-yours=\"none\">You have not marked a trade as taken on this index. Press Taken on a pick's card to follow a trade you placed; your trades stand here apart from the system's, and no test reads them.</p>";
        }

        var walk = you.Walk;
        var html = new StringBuilder();

        html.Append(Invariant, $"<div class=\"paper-yours\" data-trades=\"{you.Trades}\" data-return=\"{(walk?.Return is { } made ? made.ToString("R", Invariant) : "none")}\">");
        html.Append(Invariant, $"<p><span class=\"paper-count\">{Escaped(you.CountLine ?? Formatted($"{you.Trades} trades"))}</span>");

        if (walk is { } shown)
        {
            html.Append(Invariant, $" · return {(shown.Return is { } r ? PaperPercent(r) : "<span class=\"degraded\">not read</span>")} · worst drop {(shown.WorstDrop is { } w ? Formatted($"{w * 100:0.0}%") : "<span class=\"degraded\">not read</span>")} · against the fund {(shown.Difference is { } d ? PaperPercent(d) : shown.FundFrom is { } from ? Formatted($"<span class=\"degraded\">not stored before {DayOf(from)}</span>") : "<span class=\"degraded\">not read</span>")}");
        }

        html.Append(Invariant, $"{(you.Since is { } since ? Formatted($" · from <span class=\"day\">{DayOf(since)}</span>") : string.Empty)} · <a class=\"paper-log\" href=\"{LogHref(universe, you.Key)}\">trade log</a></p>");
        html.Append(Invariant, $"<p class=\"degraded\">{Escaped(RuleVerdict.Yours)}.</p></div>");

        return html.ToString();
    }

    // The words saying where an account starts and the version it counts, beside its count on the lists' verdicts.
    public static string StartWords(PaperAccountView account) =>
        account.Since is { } since
            ? Formatted($", from {DayOf(since)}{(account.Version is { } version ? ", counting " + Escaped(version) : string.Empty)}")
            : account.Version is { } words ? ", counting " + Escaped(words) : string.Empty;

    // The version an account counts and how many trades of earlier versions it leaves out, in its trade log.
    static string VersionLine(PaperAccountView account) =>
        account.Version is not { } version
            ? string.Empty
            : Formatted($"<span class=\"paper-version\" data-earlier=\"{account.Earlier}\">counts {Escaped(version)}{(account.Earlier > 0 ? Formatted($"; {account.Earlier} trade(s) of earlier versions left out") : string.Empty)}</span>");

    // An account's trade log: its verdict and its record's label beside each other and apart, each trade a row newest
    // first, an open one's result so far at tonight's close marked open, and the running total of its ended trades as a
    // line, labelled not evidence yet under the minimums; beneath it, folded, the trades of earlier versions the account
    // leaves out, each naming the version that listed it.
    // see: A paper account counts only the trades of its rule's current version, from where the Run page's record of that rule starts
    public string PaperLog(PaperAccountView account, IReadOnlyList<PaperTradeView> trades, IReadOnlyList<PaperTradeView>? earlier = null)
    {
        var html = new StringBuilder();
        var percent = account.Unit == LoopPoints;

        string Result(double? result) => result is not { } held
            ? "<span class=\"degraded\">not read</span>"
            : percent ? SignedFigure(held * 100, 1, Formatted($"{Unsigned(held * 100, 1):+0.0;-0.0;0.0}%")) : SignedTwo(held) + " risks";

        html.Append(Invariant, $"<div class=\"paper-log-head\" data-account=\"{Escaped(account.Key)}\" data-family=\"{Escaped(account.Family)}\" data-trades=\"{trades.Count}\">");
        html.Append(Invariant, $"<p class=\"paper-log-verdict\"><b>Test years.</b> <span data-verdict=\"{Escaped(account.Verdict)}\">{Escaped(account.Verdict)}</span>{(account.Head && account.Years.Count > 0 ? "; " + Escaped(RuleVerdict.Chosen) : string.Empty)}.</p>");
        html.Append(Invariant, $"<p class=\"paper-log-since\"><b>Since registered.</b> {account.Trades} trade(s), {account.Ended} ended{(account.TooFew is { } few ? Formatted($"; <span class=\"too-few\" data-too-few=\"{Escaped(few)}\">{Escaped(few)}</span>") : string.Empty)}.</p>");
        html.Append(Invariant, $"<p class=\"paper-log-start\" data-since=\"{(account.Since is { } since ? DayOf(since) : "none")}\"><b>Counts from.</b> {(account.Since is { } from ? DayOf(from) : "its first trade, none yet")}{VersionLine(account)}.</p>");
        html.Append(Invariant, $"<p class=\"paper-entry\">{Escaped(EntryPriceLine)}</p></div>");

        if (trades.Count == 0)
        {
            html.Append("<p class=\"degraded\" data-log=\"none\">This account holds no trade yet.</p>");

            return html.ToString() + EarlierVersions(earlier ?? [], Result);
        }

        var ended = trades.Where(trade => !trade.Open && trade.Result is not null && trade.Skipped is null).OrderBy(trade => trade.Ended).ThenBy(trade => trade.Bought).ToArray();

        if (ended.Length > 1)
        {
            html.Append(RunningTotal(ended, percent, account.TooFew is not null));
        }

        html.Append("<div class=\"tbl-wrap\"><table class=\"paper-log-table\"><thead><tr><th>Stock</th><th>Bought</th><th class=\"r\">Buy</th><th class=\"r\">Stop</th><th>Ended</th><th class=\"r\">Result</th></tr></thead><tbody>");

        foreach (var trade in trades.OrderByDescending(trade => trade.Bought).ThenBy(trade => trade.Ticker, StringComparer.Ordinal))
        {
            html.Append(Invariant, $"<tr data-ticker=\"{Escaped(trade.Ticker)}\" data-bought=\"{DayOf(trade.Bought)}\" data-open=\"{Flag(trade.Open)}\"{(trade.Skipped is null ? string.Empty : " data-skipped=\"" + Escaped(trade.Skipped) + "\"")}>");
            html.Append(Invariant, $"<td><a href=\"#/chart/{Uri.EscapeDataString(trade.Ticker)}\">{Escaped(trade.Ticker)}</a></td><td>{DayOf(trade.Bought)}</td>");
            html.Append(Invariant, $"<td class=\"r num\">{Figures.Price(trade.Buy)}</td><td class=\"r num\">{(trade.Stop is { } stop ? Figures.Price(stop) : "<span class=\"degraded\">none</span>")}</td>");
            html.Append(trade.Open ? "<td><span class=\"pill\">open</span></td>" : Formatted($"<td>{(trade.Ended is { } on ? DayOf(on) : "not read")}</td>"));
            html.Append(Invariant, $"<td class=\"r num\">{Result(trade.Result)}{(trade.Open ? " <span class=\"degraded\">so far</span>" : string.Empty)}{(trade.Skipped is { } why ? Formatted($"<span class=\"degraded\"> skipped: {Escaped(why)}</span>") : string.Empty)}</td></tr>");
        }

        html.Append("</tbody></table></div>");

        return html.ToString() + EarlierVersions(earlier ?? [], Result);
    }

    // The trades of earlier versions an account leaves out, folded, each row naming the version that listed it; none where
    // it leaves none out.
    static string EarlierVersions(IReadOnlyList<PaperTradeView> earlier, Func<double?, string> result)
    {
        if (earlier.Count == 0)
        {
            return string.Empty;
        }

        var html = new StringBuilder();

        html.Append(Invariant, $"<details class=\"paper-earlier\" data-earlier=\"{earlier.Count}\"><summary>Earlier versions: {earlier.Count} trade(s) listed before the version this account counts, left out of its line, return, worst drop and counts</summary>");
        html.Append("<div class=\"tbl-wrap\"><table class=\"paper-log-table\"><thead><tr><th>Stock</th><th>Bought</th><th>Listed by</th><th class=\"r\">Buy</th><th class=\"r\">Stop</th><th>Ended</th><th class=\"r\">Result</th></tr></thead><tbody>");

        foreach (var trade in earlier.OrderByDescending(trade => trade.Bought).ThenBy(trade => trade.Ticker, StringComparer.Ordinal))
        {
            html.Append(Invariant, $"<tr data-ticker=\"{Escaped(trade.Ticker)}\" data-bought=\"{DayOf(trade.Bought)}\" data-open=\"{Flag(trade.Open)}\" data-version=\"{Escaped(trade.Version ?? string.Empty)}\">");
            html.Append(Invariant, $"<td><a href=\"#/chart/{Uri.EscapeDataString(trade.Ticker)}\">{Escaped(trade.Ticker)}</a></td><td>{DayOf(trade.Bought)}</td><td>{Escaped(trade.Version ?? string.Empty)}</td>");
            html.Append(Invariant, $"<td class=\"r num\">{Figures.Price(trade.Buy)}</td><td class=\"r num\">{(trade.Stop is { } stop ? Figures.Price(stop) : "<span class=\"degraded\">none</span>")}</td>");
            html.Append(trade.Open ? "<td><span class=\"pill\">open</span></td>" : Formatted($"<td>{(trade.Ended is { } on ? DayOf(on) : "not read")}</td>"));
            html.Append(Invariant, $"<td class=\"r num\">{result(trade.Result)}{(trade.Open ? " <span class=\"degraded\">so far</span>" : string.Empty)}</td></tr>");
        }

        html.Append("</tbody></table></div></details>");

        return html.ToString();
    }

    // The running total of an account's ended trades in its unit, one point a trade in the order they ended.
    static string RunningTotal(IReadOnlyList<PaperTradeView> ended, bool percent, bool tooFew)
    {
        var totals = new double[ended.Count];
        var sum = 0.0;

        for (var at = 0; at < ended.Count; at++)
        {
            sum += ended[at].Result!.Value * (percent ? 100 : 1);
            totals[at] = sum;
        }

        var low = Math.Min(0, totals.Min());
        var high = Math.Max(0, totals.Max());
        var span = high - low <= 0 ? 1 : high - low;
        const double width = 360, height = 80;

        double X(int at) => 4 + ((width - 8) * at / (ended.Count - 1));

        double Y(double value) => 4 + ((height - 8) * (high - value) / span);

        var path = string.Join(" ", totals.Select((total, at) => Formatted($"{(at == 0 ? "M" : "L")}{X(at):0.#},{Y(total):0.#}")));

        return Formatted($"<figure class=\"paper-running\" data-total=\"{sum.ToString("R", Invariant)}\" data-ended=\"{ended.Count}\"><svg viewBox=\"0 0 {width} {height}\" role=\"img\" aria-label=\"The running total of the ended trades\"><line class=\"paper-zero\" x1=\"4\" x2=\"{width - 4}\" y1=\"{Y(0):0.#}\" y2=\"{Y(0):0.#}\"/><path class=\"paper-line\" d=\"{path}\"/></svg><figcaption>Running total of {ended.Count} ended trades: {(percent ? SignedFigure(sum, 1, Formatted($"{Unsigned(sum, 1):+0.0;-0.0;0.0}%")) : SignedTwo(sum) + " risks")}{(tooFew ? ", not evidence yet" : string.Empty)}</figcaption></figure>");
    }

    // A family's name as its tag reads it.
    static string FamilyName(string family) =>
        family == HeavyweightRule.Name
            ? SetupFamilies.SectorHeavyweights.Heading
            : family == QualityRule.Name
                ? QualityRule.Label
                : SetupFamilies.Named(family)?.Label ?? family;
}
