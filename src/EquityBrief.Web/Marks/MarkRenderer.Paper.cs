using System.Text;
using EquityBrief.Core.Cards;
using EquityBrief.Core.Loop;

namespace EquityBrief.Web.Marks;

// One test year of a rule in its index's newest per-rule test: its units and total, and the side it was read against's.
public sealed record PaperYearView(int Year, bool Complete, int Units, double Total, int ComparedUnits, double ComparedTotal);

// One paper account as paper trading and the trade log draw it: its key in the link, its family, its standing (live,
// provisional, variant or the operator's own), its name and its rule's words, its test years verdict with what it was
// read against, its unit and its test years, its walk where the account settings are set, its trades and those ended,
// its record since registration's label until the count screen's minimum is met, the day it starts, its adjusted p in
// that test, its record's count in one line, the words naming the version it counts, and how many trades it leaves out
// as listed before that version.
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
    int Earlier = 0)
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

// Paper trading's marks: the chart of each account's return since its record began, the table of every account with its
// test years verdict and its record since registration side by side and never merged, and an account's trade log. Every
// figure is the core's walk of stored trades or a stored test row; the marks draw and compute none.
// see: A paper account draws its test years verdict and its record since registration apart, and never merges them
// see: A variant's picks are shown on its card when chosen and its results on paper trading beside its verdict, never on Tonight
public sealed partial class MarkRenderer
{
    // The line every page about paper accounts carries about entries.
    // see: A trade's entry is priced at the next session's open by a fill step of its own, and no rule's version moves because of it
    public const string EntryPriceLine = "Each entry is bought at the open of the session after the night that listed it.";

    // The line beneath the table saying how a variant is adopted.
    public const string AdoptedLine = "A variant is adopted only through the monthly test and your approval on the Loop page.";

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

    // The table of every account, grouped by family in the one order, each row its test years verdict and its record since
    // registration side by side, the verdict's years opening beneath it.
    public string PaperTable(IReadOnlyList<PaperAccountView> accounts, bool walked, string universe = "500")
    {
        var html = new StringBuilder();

        html.Append(Invariant, $"<div class=\"tbl-wrap opens-rows\"><table class=\"paper-table\" data-accounts=\"{accounts.Count}\" data-walked=\"{Flag(walked)}\"><thead><tr>");
        html.Append(TippedHeading("Account", "the setup and the rule whose trades the account takes: the live or provisional rule's list, each registered variant's own list or book, and your own trades"));
        html.Append(TippedHeading("Test years", "the rule's verdict in the newest monthly per-rule test over the ledger's years, a variant against its live rule and the live rule against its benchmark"));
        html.Append(TippedHeading("Since registered", "the account's own record from its start, its rule's current version's or a provisional account's first trade: its trades, those skipped for cash and those with no cost read, under its too-few label until the test's count is met, and the version it counts"));
        html.Append(TippedHeading("Return", "the account's value against its size, open positions at tonight's close", "r"));
        html.Append(TippedHeading("Worst drop", "the deepest fall from a high the account's value made", "r"));
        html.Append(TippedHeading("Against the fund", "the account's return less its index fund's over the same sessions", "r"));
        html.Append("</tr></thead><tbody>");

        foreach (var family in accounts.GroupBy(account => account.Family).OrderBy(group => EquityBrief.Core.Families.SetupFamilies.OrderOf(group.Key)))
        {
            foreach (var account in family)
            {
                html.Append(PaperRow(account, walked, universe));
            }
        }

        html.Append("</tbody></table></div>");

        return html.ToString();
    }

    // Each test year as a pair of bars from one zero line on one scale, this rule's total above and the compared side's
    // beneath, the year beside them and both figures under the pointer.
    static string YearBars(PaperAccountView account, string against)
    {
        const int Width = 200;
        const int Label = 34;
        const int Row = 14;
        var top = account.Years.SelectMany(year => new[] { Math.Abs(year.Total), Math.Abs(year.ComparedTotal) }).DefaultIfEmpty(0).Max();
        var axis = Label + ((Width - Label) / 2.0);
        var half = ((Width - Label) / 2.0) - 2;
        var height = account.Years.Count * Row;
        var svg = new StringBuilder();

        string Bar(double total, int y, string kind)
        {
            var length = top > 0 ? Math.Abs(total) / top * half : 0;
            var x = total >= 0 ? axis : axis - length;

            return FormattableString.Invariant($"<rect class=\"{kind}\" x=\"{x:0.#}\" y=\"{y}\" width=\"{length:0.#}\" height=\"5\"/>");
        }

        static string Made(double total) =>
            total >= 0 ? FormattableString.Invariant($"made {total:0.0}") : FormattableString.Invariant($"lost {-total:0.0}");

        svg.Append(Invariant, $"<svg class=\"paper-yearbars\" width=\"{Width}\" height=\"{height}\" viewBox=\"0 0 {Width} {height}\" role=\"img\" aria-label=\"Each test year's total, this rule's above and {Escaped(against)}'s beneath\">");
        svg.Append(Invariant, $"<line class=\"yb-axis\" x1=\"{axis:0.#}\" y1=\"0\" x2=\"{axis:0.#}\" y2=\"{height}\"/>");

        foreach (var (year, at) in account.Years.Select((year, at) => (year, at)))
        {
            var y = at * Row;

            svg.Append(Invariant, $"<g data-year=\"{year.Year}\" data-total=\"{year.Total.ToString("R", Invariant)}\" data-compared=\"{year.ComparedTotal.ToString("R", Invariant)}\">");
            svg.Append(Invariant, $"<title>{year.Year}: this rule {Made(year.Total)} {account.Unit} over {year.Units}, {Escaped(against)} {Made(year.ComparedTotal)} over {year.ComparedUnits}</title>");
            svg.Append(Invariant, $"<text class=\"yb-year\" x=\"0\" y=\"{y + 10}\">{year.Year}</text>");
            svg.Append(Bar(year.Total, y + 2, "yb-this"));
            svg.Append(Bar(year.ComparedTotal, y + 7, "yb-that"));
            svg.Append("</g>");
        }

        svg.Append("</svg>");

        return svg.ToString();
    }

    // The words saying where an account starts and the version it counts, beside its count on the lists' verdicts.
    public static string StartWords(PaperAccountView account) =>
        account.Since is { } since
            ? Formatted($", from {DayOf(since)}{(account.Version is { } version ? ", counting " + Escaped(version) : string.Empty)}")
            : account.Version is { } words ? ", counting " + Escaped(words) : string.Empty;

    // The version an account counts and how many trades of earlier versions it leaves out, in its row and its log.
    static string VersionLine(PaperAccountView account) =>
        account.Version is not { } version
            ? string.Empty
            : Formatted($"<span class=\"paper-version\" data-earlier=\"{account.Earlier}\">counts {Escaped(version)}{(account.Earlier > 0 ? Formatted($"; {account.Earlier} trade(s) of earlier versions left out") : string.Empty)}</span>");

    // An account's row; its link opens its trade log on Past picks on the same index.
    string PaperRow(PaperAccountView account, bool walked, string universe)
    {
        var walk = account.Walk;
        var row = new StringBuilder();

        string Percent(double? figure) => figure is { } held ? SignedFigure(held * 100, 1, Formatted($"{Unsigned(held * 100, 1):+0.0;-0.0;0.0}%")) : "<span class=\"degraded\">not read</span>";

        row.Append(Invariant, $"<tr data-account=\"{Escaped(account.Key)}\" data-family=\"{Escaped(account.Family)}\" data-standing=\"{account.Standing}\" data-verdict=\"{Escaped(account.Verdict)}\" data-trades=\"{account.Trades}\" data-ended=\"{account.Ended}\">");
        row.Append(account.Standing == PaperAccountView.You
            ? "<td class=\"paper-name\">"
            : Formatted($"<td class=\"paper-name\"><span class=\"fam-tag\" data-family=\"{Escaped(account.Family)}\"><span class=\"fam-swatch\" data-family=\"{Escaped(account.Family)}\"></span>{Escaped(FamilyName(account.Family))}</span> "));
        row.Append(Invariant, $"<span class=\"status-pill {(account.Standing == PaperAccountView.You ? "variant" : account.Standing)}\">{Escaped(account.Name)}</span>");
        var log = Formatted($"#/picks?universe={Uri.EscapeDataString(universe)}&amp;account={Uri.EscapeDataString(account.Key)}");

        row.Append(Invariant, $"<a class=\"paper-log\" href=\"{log}\" title=\"{Escaped(account.Words)}\">trade log</a></td>");

        row.Append("<td class=\"paper-verdict\">");
        row.Append(Invariant, $"<span class=\"verdict-word\" data-rank=\"{RuleVerdict.Rank(account.Verdict)}\">{Escaped(account.Verdict)}</span>");

        // The figures behind the word, so two rules reading the same word can be told apart: the adjusted p against the
        // bar the test reads it at, and each test year's total beside the compared side's as a pair of bars.
        if (account.Adjusted is { } adjusted)
        {
            row.Append(Invariant, $"<span class=\"paper-p\" data-adjusted=\"{adjusted.ToString("R", Invariant)}\" data-bar=\"{LoopGate.Bar.ToString("R", Invariant)}\">adjusted p {adjusted:0.000} against the bar {LoopGate.Bar:0.0000}</span>");
        }

        if (account.Head && account.Years.Count > 0)
        {
            row.Append(Invariant, $"<span class=\"verdict-chosen\">{Escaped(RuleVerdict.Chosen)}</span>");
        }

        if (account.Years.Count > 0)
        {
            var against = account.Head ? "its benchmark" : account.Against == EquityBrief.Core.Cards.RuleRows.ProvisionalRule ? "the provisional rule" : "the live rule";

            row.Append(YearBars(account, against));
            row.Append(Invariant, $"<details class=\"paper-years\"><summary>{account.Years.Count} test year(s), against {against}</summary><table><tr><th>Year</th><th class=\"r\">This rule</th><th class=\"r\">{Capitalised(against)}</th></tr>");

            foreach (var year in account.Years)
            {
                row.Append(Invariant, $"<tr data-year=\"{year.Year}\" data-complete=\"{Flag(year.Complete)}\"><td>{year.Year}{(year.Complete ? string.Empty : ", partial")}</td><td class=\"r num\">{LoopFigure(account.Unit, year.Total)} over {LoopUnits(account.Unit, year.Units)}</td><td class=\"r num\">{LoopFigure(account.Unit, year.ComparedTotal)} over {LoopUnits(account.Unit, year.ComparedUnits)}</td></tr>");
            }

            row.Append("</table></details>");
        }

        // The record's count in one line against the test's, which says when it is too few to read, then what the walk
        // skipped and when the record starts.
        row.Append(Invariant, $"</td><td class=\"paper-since\" data-too-few=\"{Escaped(account.TooFew ?? "none")}\">");
        row.Append(Invariant, $"<span class=\"paper-count\">{Escaped(account.CountLine ?? Formatted($"{account.Trades} trade(s)"))}</span>");

        var detail = new List<string>();

        if (walk is { } shown)
        {
            detail.Add(Formatted($"{shown.Skipped} skipped · {shown.Uncosted} uncosted"));
        }

        if (account.Since is { } since)
        {
            detail.Add(Formatted($"from <span class=\"day\">{DayOf(since)}</span>"));
        }

        row.Append(detail.Count > 0 ? "<span class=\"paper-walked\">" + string.Join(" · ", detail) + "</span>" : string.Empty);
        row.Append(VersionLine(account));
        row.Append(account.Standing != PaperAccountView.You && account.TooFew is null ? "<span class=\"too-few met\">its count is met</span>" : string.Empty);
        row.Append("</td>");

        if (!walked)
        {
            row.Append("<td class=\"r degraded\" colspan=\"3\">set your account to walk it</td>");
        }
        else if (walk is null)
        {
            row.Append("<td class=\"r degraded\" colspan=\"3\" data-walk=\"none\">no trade to walk yet</td>");
        }
        else
        {
            row.Append(Invariant, $"<td class=\"r num\" data-return=\"{(walk.Return is { } made ? made.ToString("R", Invariant) : "none")}\">{Percent(walk.Return)}{(walk.MarkedFrom is { } marked && walk.Values.Count > 0 && marked != walk.Values[0].Session ? Formatted($"<span class=\"degraded\"> realized only before {DayOf(marked)}</span>") : string.Empty)}</td>");
            row.Append(Invariant, $"<td class=\"r num\" data-worst=\"{(walk.WorstDrop is { } worst ? worst.ToString("R", Invariant) : "none")}\">{(walk.WorstDrop is { } drop ? Formatted($"{drop * 100:0.0}%") : "<span class=\"degraded\">not read</span>")}</td>");
            row.Append(Invariant, $"<td class=\"r num\" data-difference=\"{(walk.Difference is { } difference ? difference.ToString("R", Invariant) : "none")}\">{(walk.Difference is not null ? Percent(walk.Difference) : walk.FundFrom is { } from ? Formatted($"<span class=\"degraded\">fund not stored before {DayOf(from)}</span>") : "<span class=\"degraded\">not read</span>")}</td>");
        }

        row.Append("</tr>");

        return row.ToString();
    }

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
        family == EquityBrief.Core.Families.HeavyweightRule.Name
            ? EquityBrief.Core.Families.SetupFamilies.SectorHeavyweights.Heading
            : EquityBrief.Core.Families.SetupFamilies.Named(family)?.Label ?? family;
}
