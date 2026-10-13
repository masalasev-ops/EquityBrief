using System.Text;
using EquityBrief.Core.Cards;
using EquityBrief.Core.Loop;

namespace EquityBrief.Web.Marks;

// One test year of a rule in its index's newest per-rule test: its units and total, and the side it was read against's.
public sealed record PaperYearView(int Year, bool Complete, int Units, double Total, int ComparedUnits, double ComparedTotal);

// One paper account as paper trading and the trade log draw it: its key in the link, its family, its standing (live,
// provisional, variant or the operator's own), its name and its rule's words, its test years verdict with what it was
// read against, its unit and its test years, its walk where the account settings are set, its trades and those ended,
// its record since registration's label until the count screen's minimum is met, and the day its record starts.
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
    DateOnly? Since)
{
    public const string Live = "live";

    public const string Provisional = "provisional";

    public const string Variant = "variant";

    public const string You = "you";

    public bool Head => Standing is Live or Provisional;
}

// One trade of an account's trade log: its stock, its buy session and price, its stop where it holds one, the session it
// ended on, its result in the account's unit, so far at tonight's close where it is open, and why the account skipped it.
public sealed record PaperTradeView(string Ticker, DateOnly Bought, decimal Buy, decimal? Stop, DateOnly? Ended, double? Result, bool Open, string? Skipped);

// Paper trading's marks: the chart of each account's return since its record began, the table of every account with its
// test years verdict and its record since registration side by side and never merged, and an account's trade log. Every
// figure is the core's walk of stored trades or a stored test row; the marks draw and compute none.
// see: A paper account draws its test years verdict and its record since registration apart, and never merges them
// see: A variant's picks are shown on its card when chosen and its results on paper trading beside its verdict, never on Tonight
public sealed partial class MarkRenderer
{
    // The line every page about paper accounts carries about entries.
    public const string EntryPriceLine = "Entries are priced at the close of the night that listed them; a trade placed after the signal would fill at the next session's open.";

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
    public string PaperTable(IReadOnlyList<PaperAccountView> accounts, bool walked)
    {
        var html = new StringBuilder();

        html.Append(Invariant, $"<div class=\"tbl-wrap opens-rows\"><table class=\"paper-table\" data-accounts=\"{accounts.Count}\" data-walked=\"{Flag(walked)}\"><thead><tr>");
        html.Append(TippedHeading("Account", "the setup and the rule whose trades the account takes: the live or provisional rule's list, each registered variant's own list or book, and your own trades"));
        html.Append(TippedHeading("Test years", "the rule's verdict in the newest monthly per-rule test over the ledger's years, a variant against its live rule and the live rule against its benchmark"));
        html.Append(TippedHeading("Since registered", "the account's own record from its first trade on: its trades, those skipped for cash and those with no cost read, under its too-few label until the test's count is met"));
        html.Append(TippedHeading("Return", "the account's value against its size, open positions at tonight's close", "r"));
        html.Append(TippedHeading("Worst drop", "the deepest fall from a high the account's value made", "r"));
        html.Append(TippedHeading("Against the fund", "the account's return less its index fund's over the same sessions", "r"));
        html.Append("</tr></thead><tbody>");

        foreach (var family in accounts.GroupBy(account => account.Family).OrderBy(group => EquityBrief.Core.Families.SetupFamilies.OrderOf(group.Key)))
        {
            foreach (var account in family)
            {
                html.Append(PaperRow(account, walked));
            }
        }

        html.Append("</tbody></table></div>");

        return html.ToString();
    }

    string PaperRow(PaperAccountView account, bool walked)
    {
        var walk = account.Walk;
        var row = new StringBuilder();

        string Percent(double? figure) => figure is { } held ? SignedFigure(held * 100, 1, Formatted($"{Unsigned(held * 100, 1):+0.0;-0.0;0.0}%")) : "<span class=\"degraded\">not read</span>";

        row.Append(Invariant, $"<tr data-account=\"{Escaped(account.Key)}\" data-family=\"{Escaped(account.Family)}\" data-standing=\"{account.Standing}\" data-verdict=\"{Escaped(account.Verdict)}\" data-trades=\"{account.Trades}\" data-ended=\"{account.Ended}\">");
        row.Append(account.Standing == PaperAccountView.You
            ? "<td class=\"paper-name\">"
            : Formatted($"<td class=\"paper-name\"><span class=\"fam-tag\" data-family=\"{Escaped(account.Family)}\"><span class=\"fam-swatch\" data-family=\"{Escaped(account.Family)}\"></span>{Escaped(FamilyName(account.Family))}</span> "));
        row.Append(Invariant, $"<span class=\"status-pill {(account.Standing == PaperAccountView.You ? "variant" : account.Standing)}\">{Escaped(account.Name)}</span>");
        row.Append(Invariant, $"<a class=\"paper-log\" href=\"#/picks?account={Uri.EscapeDataString(account.Key)}\" title=\"{Escaped(account.Words)}\">trade log</a></td>");

        row.Append("<td class=\"paper-verdict\">");
        row.Append(Invariant, $"<span class=\"verdict-word\" data-rank=\"{RuleVerdict.Rank(account.Verdict)}\">{Escaped(account.Verdict)}</span>");

        if (account.Head && account.Years.Count > 0)
        {
            row.Append(Invariant, $"<span class=\"verdict-chosen\">{Escaped(RuleVerdict.Chosen)}</span>");
        }

        if (account.Years.Count > 0)
        {
            row.Append(Invariant, $"<details class=\"paper-years\"><summary>{account.Years.Count} test year(s), against {Escaped(account.Against ?? "its benchmark")}</summary><table><tr><th>Year</th><th class=\"r\">This rule</th><th class=\"r\">{(account.Head ? "Its benchmark" : "The live rule")}</th></tr>");

            foreach (var year in account.Years)
            {
                row.Append(Invariant, $"<tr data-year=\"{year.Year}\" data-complete=\"{Flag(year.Complete)}\"><td>{year.Year}{(year.Complete ? string.Empty : ", partial")}</td><td class=\"r num\">{LoopFigure(account.Unit, year.Total)} over {LoopUnits(account.Unit, year.Units)}</td><td class=\"r num\">{LoopFigure(account.Unit, year.ComparedTotal)} over {LoopUnits(account.Unit, year.ComparedUnits)}</td></tr>");
            }

            row.Append("</table></details>");
        }

        row.Append("</td><td class=\"paper-since\">");
        row.Append(Invariant, $"{account.Trades} trade(s)");
        row.Append(walk is { } shown ? Formatted($", {shown.Skipped} skipped, {shown.Uncosted} uncosted") : string.Empty);
        row.Append(account.Since is { } since ? Formatted($", from {DayOf(since)}") : string.Empty);
        row.Append(account.Standing == PaperAccountView.You
            ? string.Empty
            : account.TooFew is { } few ? Formatted($"<span class=\"too-few\" data-too-few=\"{Escaped(few)}\">{Escaped(few)}</span>") : "<span class=\"too-few met\">its count is met</span>");
        row.Append("</td>");

        if (walk is null || !walked)
        {
            row.Append("<td class=\"r degraded\" colspan=\"3\">set your account to walk it</td>");
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
    // line, labelled not evidence yet under the minimums.
    public string PaperLog(PaperAccountView account, IReadOnlyList<PaperTradeView> trades)
    {
        var html = new StringBuilder();
        var percent = account.Unit == LoopPoints;

        string Result(double? result) => result is not { } held
            ? "<span class=\"degraded\">not read</span>"
            : percent ? SignedFigure(held * 100, 1, Formatted($"{Unsigned(held * 100, 1):+0.0;-0.0;0.0}%")) : SignedTwo(held) + " risks";

        html.Append(Invariant, $"<div class=\"paper-log-head\" data-account=\"{Escaped(account.Key)}\" data-family=\"{Escaped(account.Family)}\" data-trades=\"{trades.Count}\">");
        html.Append(Invariant, $"<p class=\"paper-log-verdict\"><b>Test years.</b> <span data-verdict=\"{Escaped(account.Verdict)}\">{Escaped(account.Verdict)}</span>{(account.Head && account.Years.Count > 0 ? "; " + Escaped(RuleVerdict.Chosen) : string.Empty)}.</p>");
        html.Append(Invariant, $"<p class=\"paper-log-since\"><b>Since registered.</b> {account.Trades} trade(s), {account.Ended} ended{(account.TooFew is { } few ? Formatted($"; <span class=\"too-few\" data-too-few=\"{Escaped(few)}\">{Escaped(few)}</span>") : string.Empty)}.</p>");
        html.Append(Invariant, $"<p class=\"paper-entry\">{Escaped(EntryPriceLine)}</p></div>");

        if (trades.Count == 0)
        {
            html.Append("<p class=\"degraded\" data-log=\"none\">This account holds no trade yet.</p>");

            return html.ToString();
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
            html.Append(Invariant, $"<td><a href=\"#/name/{Uri.EscapeDataString(trade.Ticker)}\">{Escaped(trade.Ticker)}</a></td><td>{DayOf(trade.Bought)}</td>");
            html.Append(Invariant, $"<td class=\"r num\">{Figures.Price(trade.Buy)}</td><td class=\"r num\">{(trade.Stop is { } stop ? Figures.Price(stop) : "<span class=\"degraded\">none</span>")}</td>");
            html.Append(trade.Open ? "<td><span class=\"pill\">open</span></td>" : Formatted($"<td>{(trade.Ended is { } on ? DayOf(on) : "not read")}</td>"));
            html.Append(Invariant, $"<td class=\"r num\">{Result(trade.Result)}{(trade.Open ? " <span class=\"degraded\">so far</span>" : string.Empty)}{(trade.Skipped is { } why ? Formatted($"<span class=\"degraded\"> skipped: {Escaped(why)}</span>") : string.Empty)}</td></tr>");
        }

        html.Append("</tbody></table></div>");

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
