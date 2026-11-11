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
    int Closed = 0,
    string? Note = null)
{
    public const string Live = "live";

    public const string Provisional = "provisional";

    public const string Variant = "variant";

    public const string You = "you";

    public bool Head => Standing is Live or Provisional;

    // From 21.17 the local model's cutoff's year a rule reading a release reading is judged from, none for any other.
    public int? JudgedFrom { get; init; }

    // From 23.2 the rule as the newest per-rule test stored it, which a verdict across the three indices names; none where
    // no test read it.
    public string? Tested { get; init; }

    // The first session its version's registration counts from, the day it starts, none where no registration starts the
    // account.
    public DateOnly? VersionFrom { get; init; }

    // From the 19.4 correction of 2026-11-07 a version's reach against its live rule, none for any other account.
    public EquityBrief.Core.Cards.VersionReachKind? Reach { get; init; }
}

// The trades a family's live account leaves out as listed before its version that a replay under its version reproduced,
// drawn apart under the account: its family, the session the replay counted from, and each trade.
// see: A paper account counts from its rule's current version's first session, and the same trades before it stand apart under the account
public sealed record PaperSameView(string Family, DateOnly Since, IReadOnlyList<PaperBoughtView> Trades);

// One trade of an account's trade log: its stock, its buy session and price, its stop where it holds one, the session it
// ended on, its result in the account's unit, so far at tonight's close where it is open, why the account skipped it, and
// for a trade listed before the version the account counts, the version that listed it.
public sealed record PaperTradeView(string Ticker, DateOnly Bought, decimal Buy, decimal? Stop, DateOnly? Ended, double? Result, bool Open, string? Skipped, string? Version = null, bool Halved = false);

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
    string? Version = null,
    string? Selling = null)
{
    // From the 19.4 correction of 2026-11-07 how a holding with no price stop sells, by its own setup's rule, which its row
    // draws in its stop's and target's place; none for a trade with a stop.
    public string? Sells { get; init; }
}

// A trade a family's variants took that its live or provisional rule did not take off the same night: its stock and
// company, the variants that took it by their short names, the night that listed it, the session it was bought on and its
// price, whether it is a book's holding, and its figures tonight or at its end.
public sealed record PaperOnlyView(string Ticker, string? Company, IReadOnlyList<string> By, DateOnly Listed, DateOnly Bought, decimal Buy, bool Book, PaperPosition? Open, PaperEnded? Closed);

// The index's fund over the days the live and provisional accounts' tiles count: its name, its return and the session the
// days start on, and where its series starts after them the session it does.
public sealed record PaperFundView(string Fund, double? Return, DateOnly? From, DateOnly? Starts);

// Paper trading as its page draws it: every account; the trades the live and provisional rules bought, open and closed, of
// the families the chips show, and those of earlier versions; the stocks they did not buy; the trades only the chosen
// family's variants took; the fund's tile; and from 21.11 the combined account.
public sealed record PaperPageView(
    IReadOnlyList<PaperAccountView> Accounts,
    IReadOnlyList<PaperBoughtView> Open,
    IReadOnlyList<PaperBoughtView> Closed,
    IReadOnlyList<PaperBoughtView> Earlier,
    PaperNotBought NotBought,
    IReadOnlyList<PaperOnlyView> OnlyVariants,
    PaperFundView? Fund,
    PaperCombinedView? Combined = null,
    IReadOnlyList<PaperComingView>? Coming = null,
    TradeGroupsView? Groups = null)
{
    // From 23.2 the newest pooled run's verdicts across the three indices, which the scorecard draws beside the index's own.
    public IReadOnlyList<LoopPooledRow> Pooled { get; init; } = [];

    // From 23.7 each rule's verdict over the long history on the index and the verdicts across the three over it, which the
    // scorecard draws beneath.
    public IReadOnlyList<PaperLongVerdict> Long { get; init; } = [];

    public IReadOnlyList<LoopPooledRow> LongPooled { get; init; } = [];

    // Each live account's same trades before its version, drawn apart under the account.
    public IReadOnlyList<PaperSameView> Same { get; init; } = [];

    // From the 19.4 correction of 2026-11-07 each live account's stocks not bought, named by its family, in the family order,
    // which the lines beneath what the system bought draw in the merged line's place.
    public IReadOnlyList<PaperAccountUnbought> Unbought { get; init; } = [];

    // From the 19.4 correction of 2026-11-07 the index's fund's closes over the sessions the chart can draw, which it draws
    // as a line of its own.
    public IReadOnlyList<(DateOnly Session, decimal Close)> FundCloses { get; init; } = [];
}

// From 21.20 a variant registered after the newest night, which no night has read yet: its family, its number and what it
// changes as its card will number it, the day it was registered and the night that first reads it, none where the closure
// table cannot place one.
public sealed record PaperComingView(string Family, string Short, string? Changes, string Words, DateOnly Registered, DateOnly? FirstNight);

// From the 21.11 correction of 2026-11-07 the live setups' accounts, averaged, as its page draws it: its walk, the day it
// counts from, the day every live account counts, the trades it holds open and has closed, how many accounts it averages,
// and each trade and each skip of those accounts over its days with the family whose account it is.
// see: The averaged account gives each live rule's own account an equal share, from the day every one of them counts
public sealed record PaperCombinedView(PaperWalk Walk, DateOnly Since, int Open, int Closed, int Accounts, IReadOnlyList<PaperCombinedTradeView> Trades)
{
    public const string Key = "combined";

    public const string Name = "The live setups' accounts, averaged";

    public int Skipped => Walk.Skipped;

    // The size each account it averages starts at, the settings' size, which a monthly book's holding is a share of.
    public decimal Size { get; init; }
}

// One trade of an account the averaged account holds a share of: the family whose account bought it, its stock, its buy
// session and price, the shares its own account bought, none where that account skipped it and why, the session it ended
// on, its return on the position, so far at tonight's close where it is open, and for a monthly book's holding the most
// holdings its book can hold, which its account sizes it across.
public sealed record PaperCombinedTradeView(string Family, string Ticker, DateOnly Bought, decimal Buy, long Shares, string? Skipped, DateOnly? Ended, double? Return, bool Open, int? BookHoldings = null);

// The choices paper trading keeps in its link, one a part, and the link a choice opens with every other kept: the index,
// the family whose variants the chart adds, the closed tab of what the system bought, the family its chips show, the
// family the variants' part reads and the rule the scorecard reads, each first choice the key's absence.
public sealed record PaperLink(string Universe, string? Family = null, bool Closed = false, string? Show = null, string? Variants = null, string? Rule = null)
{
    // From the 19.4 correction of 2026-11-07 the chart's span, every line since it began or the last 30 days alone.
    public bool Recent { get; init; }

    public const string SpanKey = "span";

    public const string RecentValue = "30";

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
        + (Rule is { } rule ? "&" + RuleKey + "=" + Uri.EscapeDataString(rule) : string.Empty)
        + (Recent ? "&" + SpanKey + "=" + RecentValue : string.Empty);
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
    public const string AdoptedLine = "A variant is adopted only through the monthly test and your approval on the Monthly check-up.";

    // The warning beside a rule's test years where its setting was chosen on them.
    public const string TunedLine = "Its settings were tuned on these same years, so expect it to look worse from here on.";

    // What a provisional rule's record since it went live says in its place.
    public const string NoRecordLine = "A provisional rule keeps no record: it starts one when its family is frozen or a setting is approved, and its account counts every trade from its first meanwhile.";

    // The page's script line that scrolls each paper chart drawn wider than its frame to its right-hand end, so a phone
    // opens it on its newest day; a chart that fits its frame does not scroll.
    // see: The paper chart opens scrolled to its newest day where it is wider than the screen
    public const string PaperChartToNewest = "for (const wide of screen.querySelectorAll('.paper-chart')) { wide.scrollLeft = wide.scrollWidth; }";

    // The chart of each drawn account's return over the sessions its walk read, in per cent of the account's size: the
    // live or provisional accounts always, a chosen family's variants dashed, each line a family's mark with its key
    // beneath it, a key's press hiding or showing its line in the browser alone.
    // From the 19.4 correction of 2026-11-07 each line starts on its own account's first day and never as a flat run before
    // it, the span from the earliest of those days, and a choice kept in the link between every line since it began and the
    // last 30 days alone.
    // see: The paper chart draws each account from its own first day in its family's colour beside the fund on round gridlines, with an end label on each line
    public string PaperChart(IReadOnlyList<PaperAccountView> accounts, string? chosen, PaperCombinedView? combined = null, PaperLink? link = null, IReadOnlyList<(DateOnly Session, decimal Close)>? fund = null, string? fundName = null)
    {
        // From 21.11 the combined account drawn as a line of its own, after every family's, from the 21.11 correction of
        // 2026-11-07 the live setups' accounts averaged.
        PaperAccountView[] together = combined is { Walk.Values.Count: > 0 } all
            ? [new PaperAccountView(PaperCombinedView.Key, PaperCombinedView.Key, PaperCombinedView.Key, PaperCombinedView.Name, "each live setup's own account given an equal share of one account", string.Empty, null, "per cent", [], all.Walk, all.Walk.Trades.Count, all.Closed, null, all.Since)]
            : [];
        // From the 19.4 correction of 2026-11-07 an account holding no trade draws no line, its key saying so.
        var shown = accounts.Where(account => account.Walk is { Values.Count: > 0 } && (account.Head || account.Standing == PaperAccountView.You || (account.Standing == PaperAccountView.Variant && account.Family == chosen))).Concat(together).ToArray();
        var drawn = shown.Where(account => account.Walk!.Taken > 0).ToArray();
        var idle = shown.Where(account => account.Walk!.Taken == 0).ToArray();

        if (drawn.Length == 0)
        {
            return "<p class=\"degraded\" data-paper-chart=\"none\">No account has a trade to draw yet.</p>" + (idle.Length > 0 ? "<p class=\"paper-keys\">" + string.Concat(idle.Select(NoTradeKey)) + "</p>" : string.Empty);
        }

        var every = drawn.SelectMany(account => account.Walk!.Values.Select(value => value.Session)).Distinct().Order().ToArray();
        var recent = link?.Recent == true;
        var sessions = recent ? [.. every.Where(session => session > every[^1].AddDays(-RecentDays))] : every;
        var at = sessions.Select((session, place) => (session, place)).ToDictionary(pair => pair.session, pair => pair.place);
        var returns = drawn.ToDictionary(account => account.Key, account => account.Walk!.Returns.Where(one => at.ContainsKey(one.Session)).Select(one => (one.Session, Return: one.Return * 100)).ToArray(), StringComparer.Ordinal);
        drawn = [.. drawn.Where(account => returns[account.Key].Length > 0)];

        // From the 19.4 correction of 2026-11-07 the index's fund over the chart's days, its return from its close on the
        // span's first day it holds one, a session it holds no close for left out of its line.
        var held = (fund ?? []).Where(one => one.Session >= every[0] && one.Session <= every[^1]).OrderBy(one => one.Session).ToArray();
        (DateOnly Session, double Return)[] fundLine = held.Length > 0 && held[0].Close > 0m
            ? [.. held.Where(one => at.ContainsKey(one.Session)).Select(one => (one.Session, EquityBrief.Core.Prices.Statistic.FromRatio((one.Close / held[0].Close) - 1m) * 100))]
            : [];
        // From the 19.4 correction of 2026-11-07 the scale runs over round steps holding every point and nought, each step a
        // gridline with its label and nought's labelled as the zero line; the right margin holds each line's end label.
        var (low, high, step) = RoundSteps(
            Math.Min(0, returns.Values.SelectMany(line => line.Select(point => point.Return)).Concat(fundLine.Select(point => point.Return)).Min()),
            Math.Max(0, returns.Values.SelectMany(line => line.Select(point => point.Return)).Concat(fundLine.Select(point => point.Return)).Max()));
        var span = high - low;
        const double width = 860, height = 260, left = 48, right = 150, top = 12, bottom = 28;

        double X(DateOnly session) => sessions.Length == 1 ? left + ((width - left - right) / 2) : left + ((width - left - right) * at[session] / (sessions.Length - 1));

        double Y(double value) => top + ((height - top - bottom) * (high - value) / span);

        var svg = new StringBuilder();

        if (link is { } kept)
        {
            svg.Append("<nav class=\"paper-span\" aria-label=\"The chart's span\">");
            svg.Append(Invariant, $"<a class=\"chip\" data-filter=\"{PaperLink.SpanKey}\" href=\"{Escaped((kept with { Recent = false }).Href)}\" aria-pressed=\"{Flag(!recent)}\">Since each began</a>");
            svg.Append(Invariant, $"<a class=\"chip\" data-filter=\"{PaperLink.SpanKey}\" href=\"{Escaped((kept with { Recent = true }).Href)}\" aria-pressed=\"{Flag(recent)}\">Last {RecentDays} days</a>");
            svg.Append("</nav>");
        }

        svg.Append(Invariant, $"<figure class=\"paper-chart\" data-lines=\"{drawn.Length}\" data-sessions=\"{sessions.Length}\" data-from=\"{DayOf(sessions[0])}\" data-to=\"{DayOf(sessions[^1])}\" data-span=\"{(recent ? PaperLink.RecentValue : "all")}\">");
        svg.Append(Invariant, $"<svg viewBox=\"0 0 {width} {height}\" role=\"img\" aria-label=\"Each drawn account's return in per cent of its size, session by session\">");
        // A gridline at each round step with its label, nought's the zero line.
        for (var at2 = 0; low + (at2 * step) <= high + (step / 1000); at2++)
        {
            var level = Math.Round(low + (at2 * step), 6);
            var zero = Math.Abs(level) < step / 1000;

            svg.Append(Invariant, $"<line class=\"{(zero ? "paper-zero" : "paper-grid")}\" data-level=\"{(zero ? "0" : level.ToString("R", Invariant))}\" x1=\"{left}\" x2=\"{width - right}\" y1=\"{Y(level):0.#}\" y2=\"{Y(level):0.#}\"/>");
            svg.Append(Invariant, $"<text class=\"paper-axis{(zero ? " paper-zero-label" : string.Empty)}\" x=\"{left - 6}\" y=\"{Y(level) + 4:0.#}\" text-anchor=\"end\">{(zero ? "0%" : Formatted($"{level:+0.##;-0.##}%"))}</text>");
        }

        // A date beneath the first session of each week the span holds, none closer than a label's width to the one before.
        var labelled = double.NegativeInfinity;

        foreach (var session in sessions.Where((session, place) => place == 0 || WeekOf(session) != WeekOf(sessions[place - 1])))
        {
            if (X(session) - labelled < 40)
            {
                continue;
            }

            labelled = X(session);
            svg.Append(Invariant, $"<text class=\"paper-axis paper-date\" data-date=\"{DayOf(session)}\" x=\"{X(session):0.#}\" y=\"{height - 8}\" text-anchor=\"middle\">{session.ToString("d MMM", Invariant)}</text>");
        }

        foreach (var account in drawn)
        {
            var points = returns[account.Key];
            var path = string.Join(" ", points.Select((point, place) => Formatted($"{(place == 0 ? "M" : "L")}{X(point.Session):0.#},{Y(point.Return):0.#}")));

            svg.Append(Invariant, $"<path class=\"paper-line{(account.Standing == PaperAccountView.Variant ? " dashed" : string.Empty)}\" data-account=\"{Escaped(account.Key)}\" data-family=\"{Escaped(account.Family)}\" data-standing=\"{account.Standing}\" d=\"{path}\"><title>{Escaped(account.Name)}</title></path>");
        }

        if (fundLine.Length > 0 && fundName is { } named)
        {
            var path = string.Join(" ", fundLine.Select((point, place) => Formatted($"{(place == 0 ? "M" : "L")}{X(point.Session):0.#},{Y(point.Return):0.#}")));

            svg.Append(Invariant, $"<path class=\"paper-line paper-fund\" data-account=\"{FundKey}\" data-standing=\"{FundKey}\" d=\"{path}\"><title>{Escaped(named)}, the index's fund</title></path>");
        }

        // Each line ends in a label of its name and its latest return, the labels moved apart where two would overlap.
        (string Key, string Family, string Standing, string Text, double Y)[] ends =
        [
            .. drawn.Select(account => (account.Key, account.Family, account.Standing, EndName(account) + " " + Formatted($"{returns[account.Key][^1].Return:+0.0;-0.0;0.0}%"), Y(returns[account.Key][^1].Return))),
            .. fundLine.Length > 0 && fundName is { } ended ? [(FundKey, FundKey, FundKey, ended + " " + Formatted($"{fundLine[^1].Return:+0.0;-0.0;0.0}%"), Y(fundLine[^1].Return))] : Array.Empty<(string, string, string, string, double)>(),
        ];

        // A dot in the line's own colour at its end marks it, its label in the ink beside, as a family's hue is a mark and never
        // a text's colour.
        foreach (var (key, family, standing, _, y) in ends)
        {
            svg.Append(Invariant, $"<circle class=\"paper-dot\" data-family=\"{Escaped(family)}\" data-standing=\"{Escaped(standing)}\" cx=\"{width - right:0.#}\" cy=\"{y:0.#}\" r=\"3\"/>");
        }

        foreach (var (key, family, standing, text, y) in Spread(ends, top + 4, height - bottom))
        {
            svg.Append(Invariant, $"<text class=\"paper-end\" data-end=\"{Escaped(key)}\" data-family=\"{Escaped(family)}\" data-standing=\"{Escaped(standing)}\" x=\"{width - right + 6}\" y=\"{y + 4:0.#}\">{Escaped(text)}</text>");
        }

        // From the 19.4 correction of 2026-11-07 a band over each session the pointer reads, carrying its day and each line's
        // return on it, which the page's script draws in the box above the chart.
        var half = sessions.Length > 1 ? (width - left - right) / (sessions.Length - 1) / 2 : (width - left - right) / 2;
        var fundAt = fundLine.ToDictionary(point => point.Session, point => point.Return);

        foreach (var session in sessions)
        {
            var rows = drawn
                .SelectMany(account => returns[account.Key].Where(point => point.Session == session).Take(1).Select(point => (Account: account, point.Return)))
                .Select(one => Formatted($"{EndName(one.Account)}|{one.Return:+0.0;-0.0;0.0}%|{one.Account.Family}|{one.Account.Standing}"))
                .Concat(fundName is { } tipped && fundAt.TryGetValue(session, out var made) ? [Formatted($"{tipped}|{made:+0.0;-0.0;0.0}%|{FundKey}|{FundKey}")] : []);

            svg.Append(Invariant, $"<rect class=\"paper-hit\" data-day=\"{session.ToString("ddd d MMM yyyy", Invariant)}\" data-tip=\"{Escaped(string.Join(";", rows))}\" x=\"{Math.Max(left, X(session) - half):0.#}\" y=\"{top}\" width=\"{Math.Min(X(session) + half, width - right) - Math.Max(left, X(session) - half):0.#}\" height=\"{height - top - bottom}\"/>");
        }

        svg.Append("</svg><div class=\"paper-tip\" role=\"status\" hidden></div><figcaption class=\"paper-keys\">");

        foreach (var account in drawn)
        {
            svg.Append(Invariant, $"<button type=\"button\" class=\"paper-key\" data-line=\"{Escaped(account.Key)}\" data-family=\"{Escaped(account.Family)}\" data-standing=\"{account.Standing}\" aria-pressed=\"true\" title=\"{Escaped(Copying ? FamilyName(account.Family) : EquityBrief.Core.Families.RuleWording.ScreenName(account.Words))}\"><span class=\"paper-key-mark\" data-family=\"{Escaped(account.Family)}\" data-standing=\"{account.Standing}\"></span>{Escaped(account.Standing == PaperCombinedView.Key ? account.Name : FamilyName(account.Family) + " · " + account.Name)}</button>");
        }

        if (fundLine.Length > 0 && fundName is { } keyed)
        {
            svg.Append(Invariant, $"<button type=\"button\" class=\"paper-key\" data-line=\"{FundKey}\" data-standing=\"{FundKey}\" aria-pressed=\"true\" title=\"The index's fund held over the chart's days\"><span class=\"paper-key-mark\" data-standing=\"{FundKey}\"></span>{Escaped(keyed)}, the index's fund</button>");
        }

        svg.Append(string.Concat(idle.Select(NoTradeKey)));
        svg.Append("</figcaption></figure>");

        return svg.ToString();
    }

    // The calendar days the chart's recent span reaches back from its last session.
    public const int RecentDays = 30;

    // The least distance in the chart's units between two end labels, a line of their text.
    public const double EndGap = 13;

    // The chart's scale in round steps: a step of 1, 2, 2.5 or 5 times a power of ten giving no more than six steps, and the
    // low and high ends at whole steps holding both ends given; a flat span reads one per cent either side.
    public static (double Low, double High, double Step) RoundSteps(double low, double high)
    {
        if (high - low < 1e-9)
        {
            return (Math.Min(low, -1), Math.Max(high, 1), 1);
        }

        var raw = (high - low) / 6;
        var power = Math.Pow(10, Math.Floor(Math.Log10(raw)));
        var step = new[] { 1, 2, 2.5, 5, 10 }.Select(times => times * power).First(candidate => candidate >= raw - 1e-12);

        return (Math.Floor((low / step) + 1e-9) * step, Math.Ceiling((high / step) - 1e-9) * step, step);
    }

    // End labels at their lines' ends, each pushed down to leave the one above at least a label's height, then those pushed
    // past the bottom drawn back up, the order of the lines' ends kept.
    public static IReadOnlyList<(string Key, string Family, string Standing, string Text, double Y)> Spread(IReadOnlyList<(string Key, string Family, string Standing, string Text, double Y)> ends, double top, double bottom)
    {
        var placed = ends.OrderBy(end => end.Y).ThenBy(end => end.Key, StringComparer.Ordinal).ToArray();

        for (var at = 0; at < placed.Length; at++)
        {
            var least = at == 0 ? top : placed[at - 1].Y + EndGap;

            placed[at] = placed[at] with { Y = Math.Max(placed[at].Y, least) };
        }

        for (var at = placed.Length - 1; at >= 0; at--)
        {
            var most = at == placed.Length - 1 ? bottom : placed[at + 1].Y - EndGap;

            placed[at] = placed[at] with { Y = Math.Min(placed[at].Y, most) };
        }

        return placed;
    }

    // A line's name on its end label: its setup's, a variant's with its number, yours, and the averaged account's short.
    static string EndName(PaperAccountView account) =>
        account.Standing == PaperCombinedView.Key ? "Averaged"
            : account.Standing == PaperAccountView.You ? "You"
            : account.Standing == PaperAccountView.Variant ? FamilyName(account.Family) + " " + (account.Short ?? account.Name)
            : FamilyName(account.Family);

    // The Monday of a session's week, which the chart's dates are drawn a week at a time by.
    static DateOnly WeekOf(DateOnly session) => session.AddDays(-(((int)session.DayOfWeek + 6) % 7));

    // The key the chart's line of the index's fund is drawn and pressed by.
    public const string FundKey = "fund";

    // The key of an account with no trade, which draws no line: its name and that it has no trades yet, nothing to press.
    // see: The paper chart draws each account from its own first day in its family's colour beside the fund on round gridlines, with an end label on each line
    static string NoTradeKey(PaperAccountView account) =>
        Formatted($"<span class=\"paper-key paper-key-none\" data-none=\"{Escaped(account.Key)}\" data-family=\"{Escaped(account.Family)}\" data-standing=\"{account.Standing}\"><span class=\"paper-key-mark\" data-family=\"{Escaped(account.Family)}\" data-standing=\"{account.Standing}\"></span>{Escaped(account.Standing == PaperCombinedView.Key ? account.Name : FamilyName(account.Family) + " · " + account.Name)} · no trades yet</span>");

    // A fraction drawn as a signed per cent to a tenth, in the class its sign gives.
    static string PaperPercent(double fraction) =>
        SignedFigure(fraction * 100, 1, Formatted($"{Unsigned(fraction * 100, 1):+0.0;-0.0;0.0}%"));

    // The words heading an account's trades listed before its version that replay the same under it, from the session the
    // replay counted from.
    // see: A paper account counts from its rule's current version's first session, and the same trades before it stand apart under the account
    public static string SameWords(DateOnly since) =>
        FormattableString.Invariant($"The same trades before this version (since {since:d MMM}), kept in the rule's record and not in this account");

    // The index's fund on a tile, over that tile's own account's days: its return over them, or where its closes start later
    // the day they start; none where the account holds no walk.
    // see: Each tile draws the index's fund over its own account's days, and no tile draws the fund over days its account did not count
    static string TileFund(PaperWalk? walk, string? fund) =>
        walk is null || fund is null
            ? string.Empty
            : walk.FundReturn is { } held
                ? Formatted($"<div class=\"pt-fund\" data-fund=\"{Escaped(fund)}\" data-fund-return=\"{held.ToString("R", Invariant)}\">{Escaped(fund)} over the same days {PaperPercent(held)}</div>")
                : walk.FundFrom is { } starts
                    ? Formatted($"<div class=\"pt-fund\" data-fund=\"{Escaped(fund)}\" data-fund-return=\"none\">{Escaped(fund)}'s closes start on <span class=\"day\">{DayOf(starts)}</span>, after this account's first day</div>")
                    : Formatted($"<div class=\"pt-fund\" data-fund=\"{Escaped(fund)}\" data-fund-return=\"none\">{Escaped(fund)} over the same days not read</div>");

    // A tile a family's live or provisional account in the one family order: its return since its record began, the trades
    // its walk holds open and has closed and the day it counts from, the version it counts under the pointer, and from the
    // 19.4 correction of 2026-11-07 the index's fund over its own days beneath it, so no tile reads the fund over days its
    // account did not count.
    public string PaperTiles(IReadOnlyList<PaperAccountView> accounts, PaperFundView? fund, PaperCombinedView? combined = null, decimal? size = null)
    {
        var heads = accounts.Where(account => account.Head).OrderBy(account => SetupFamilies.OrderOf(account.Family)).ToArray();
        var html = new StringBuilder();

        html.Append(Invariant, $"<div class=\"paper-tiles\" data-tiles=\"{heads.Length + (combined is null ? 0 : 1)}\">");

        // From 21.11 the combined account's tile first, from the 21.11 correction of 2026-11-07 the live setups' accounts
        // averaged: its return since the day every live account counts, what its accounts hold and what they skipped.
        if (combined is { } all)
        {
            var together = all.Walk.Return;

            html.Append(Invariant, $"<div class=\"paper-tile combined\" data-standing=\"{PaperCombinedView.Key}\" data-return=\"{(together is { } held ? held.ToString("R", Invariant) : "none")}\" data-open=\"{all.Open}\" data-closed=\"{all.Closed}\" data-skipped=\"{all.Skipped}\" data-since=\"{DayOf(all.Since)}\" data-accounts=\"{all.Accounts}\" title=\"Each live setup's own account given an equal share of one account of your size\">");
            html.Append(Invariant, $"<div class=\"pt-name\">{Escaped(PaperCombinedView.Name)}</div>");
            html.Append(together is { } figure ? Formatted($"<div class=\"pt-figure\">{PaperPercent(figure)}</div>") : "<div class=\"pt-figure degraded\">none yet</div>");
            html.Append(Formatted($"<div class=\"pt-sub\">{Escaped(TileSaid(together, all.Since, size, all.Open))} {all.Closed} closed, {all.Skipped} skipped.</div>"));
            html.Append(TileFund(all.Walk, fund?.Fund));
            html.Append("</div>");
        }

        foreach (var head in heads)
        {
            var made = head.Walk?.Return;

            html.Append(Invariant, $"<div class=\"paper-tile\" data-family=\"{Escaped(head.Family)}\" data-standing=\"{head.Standing}\" data-return=\"{(made is { } held ? held.ToString("R", Invariant) : "none")}\" data-open=\"{head.Open}\" data-closed=\"{head.Closed}\" data-since=\"{(head.Since is { } since ? DayOf(since) : "none")}\" title=\"{Escaped(Copying ? FamilyName(head.Family) : Capitalised(head.Words) + (head.Version is { } version ? "; counts " + version : string.Empty))}\">");
            html.Append(Invariant, $"<div class=\"pt-name\"><span class=\"fam-swatch\" data-family=\"{Escaped(head.Family)}\"></span>{Escaped(FamilyName(head.Family))} <span class=\"status-pill {head.Standing}\">{Escaped(head.Name)}</span></div>");
            html.Append(made is { } figure ? Formatted($"<div class=\"pt-figure\">{PaperPercent(figure)}</div>") : "<div class=\"pt-figure degraded\">none yet</div>");
            html.Append(Invariant, $"<div class=\"pt-sub\">{Escaped(TileSaid(made, head.Since, size, head.Open))}{(head.Since is null || made is null ? string.Empty : Formatted($" {head.Closed} closed."))}</div>");
            html.Append(head.Since is null ? string.Empty : TileFund(head.Walk, fund?.Fund));
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

        // The account each row walks, named above the table, and beneath it one line an account naming what that account
        // did not buy, none it holds.
        // see: Each table and line of what the system bought names the account it walks, and draws no stock that account holds as not bought
        html.Append("<p class=\"paper-walks\" data-walks=\"each\">Each row is a trade of its setup's own account, which the Setup column names.</p>");
        html.Append(link.Closed ? ClosedTrades(page.Closed) : OpenTrades(page.Open));

        if (page.Unbought.Count == 0)
        {
            html.Append(NotBought(page.NotBought, page.Groups?.ToBuyFromReshuffles ?? 0));
        }
        else
        {
            // From the 19.10 correction of 2026-11-07 each account's listings waiting on the next open first, as to buy and
            // never as not bought, none it holds, each held elsewhere named so; then what each did not buy.
            // see: A listing waiting on its next session's open is drawn as to buy in its own account and never as not bought, nor where that account holds it
            foreach (var account in page.Unbought.Where(account => account.Waiting.Count > 0))
            {
                html.Append(ToBuy(account));
            }

            if (page.Unbought.All(account => account.NotBought.Count == 0))
            {
                html.Append(page.Unbought.Any(account => account.Waiting.Count > 0)
                    ? "<p class=\"paper-unbought\" data-unbought=\"0\">Every stock these rules listed was bought or is to buy at the next open.</p>"
                    : "<p class=\"paper-unbought\" data-unbought=\"0\">Every stock these rules listed was bought.</p>");
            }

            foreach (var account in page.Unbought.Where(account => account.NotBought.Count > 0))
            {
                html.Append(NotBought(account.NotBought, 0, account.Family));
            }
        }

        if (page.Groups is { } groups)
        {
            html.Append(Invariant, $"<p class=\"card-note paper-system\" data-system=\"{groups.System.Bought}\" data-system-open=\"{groups.System.Open}\" data-version=\"{groups.ThisVersion.Bought}\" data-earlier-trades=\"{groups.EarlierVersions.Bought}\" data-same-trades=\"{groups.SameTrades.Bought}\">{Escaped(groups.SystemLine)} {(groups.SameLine.Length == 0 ? string.Empty : Escaped(groups.SameLine) + " ")}<a href=\"{App.SinglePageApp.PicksRoute}\">See every trade on Past picks</a></p>");
        }

        // Each live account's same trades before its version under one press of its own, named by its setup and the day the
        // replay counted from, in none of the account's figures.
        // see: A paper account counts from its rule's current version's first session, and the same trades before it stand apart under the account
        foreach (var same in page.Same)
        {
            html.Append(Invariant, $"<details class=\"paper-earlier paper-same\" data-family=\"{Escaped(same.Family)}\" data-same=\"{same.Trades.Count}\" data-since=\"{DayOf(same.Since)}\"><summary>{Escaped(FamilyName(same.Family))}: {Escaped(SameWords(same.Since))}, {same.Trades.Count} {(same.Trades.Count == 1 ? "trade" : "trades")}</summary>");
            html.Append("<div class=\"tbl-wrap\"><table class=\"paper-bought\"><thead><tr><th>Stock</th><th>Setup</th><th class=\"r\">Bought</th><th>Open or how it ended</th><th class=\"r\">Result</th></tr></thead><tbody>");

            foreach (var trade in same.Trades)
            {
                html.Append(Invariant, $"<tr data-ticker=\"{Escaped(trade.Ticker)}\" data-bought=\"{DayOf(trade.Bought)}\">");
                html.Append(StockCell(trade.Ticker, trade.Company, null));
                html.Append(FamilyCell(trade.Family));
                html.Append(Invariant, $"<td class=\"r num\">{Figures.Price(trade.Buy)}<span class=\"pb-day\">{DayOf(trade.Bought)}</span></td>");
                html.Append(trade.Closed is { } ended ? Formatted($"<td>{Escaped(Capitalised(ended.Ending))}</td>") : trade.Selling is { } sells ? Formatted($"<td>{Escaped(Capitalised(TradeGroupsView.SellsWords))}: {Escaped(sells)}</td>") : "<td><span class=\"pill\">open</span></td>");
                html.Append(Invariant, $"<td class=\"r num\">{Result(trade.Stop is null, trade.Closed is { } shut ? (shut.Risks, shut.Fraction) : (trade.Open?.Risks, trade.Open?.Fraction))}</td></tr>");
            }

            html.Append("</tbody></table></div></details>");
        }

        // On the read-only copy too, each row its stock, family, the words "an earlier version", the day bought and how it ended or
        // that it is open with its result, the version itself drawn nowhere.
        // see: The read-only copy is published to Cloudflare Pages with no control and no part of the method, and its upload refuses a page that leaks one
        if (page.Earlier.Count > 0)
        {
            html.Append(Invariant, $"<details class=\"paper-earlier\" data-earlier=\"{page.Earlier.Count}\"><summary>Earlier versions: {page.Earlier.Count} {(page.Earlier.Count == 1 ? "trade" : "trades")} a rule's earlier versions listed, in the line above and left out of every account</summary>");
            html.Append("<div class=\"tbl-wrap\"><table class=\"paper-bought\"><thead><tr><th>Stock</th><th>Setup</th><th>Listed by</th><th class=\"r\">Bought</th><th>Open or how it ended</th><th class=\"r\">Result</th></tr></thead><tbody>");

            foreach (var trade in page.Earlier)
            {
                html.Append(Invariant, $"<tr data-ticker=\"{Escaped(trade.Ticker)}\" data-bought=\"{DayOf(trade.Bought)}\"{(Copying ? string.Empty : Formatted($" data-version=\"{Escaped(trade.Version ?? string.Empty)}\""))}>");
                html.Append(StockCell(trade.Ticker, trade.Company, null));
                html.Append(FamilyCell(trade.Family));
                html.Append(Copying ? "<td>an earlier version</td>" : Formatted($"<td><span title=\"{Escaped(trade.Version ?? string.Empty)}\">an earlier version</span></td>"));
                html.Append(Invariant, $"<td class=\"r num\">{Figures.Price(trade.Buy)}<span class=\"pb-day\">{DayOf(trade.Bought)}</span></td>");
                html.Append(trade.Closed is { } ended ? Formatted($"<td>{Escaped(Capitalised(ended.Ending))}</td>") : trade.Selling is { } sells ? Formatted($"<td>{Escaped(Capitalised(TradeGroupsView.SellsWords))}: {Escaped(sells)}</td>") : "<td><span class=\"pill\">open</span></td>");
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

    // A company's name is the filer's own words, drawn as filed and never in the reader's words.
    // see: A company's name and a filer's words are drawn as filed, and never in the reader's words
    static string StockCell(string ticker, string? company, string? also) =>
        Formatted($"<td class=\"pb-stock\"><a href=\"#/chart/{Uri.EscapeDataString(ticker)}\">{Escaped(ticker)}</a>{(company is { } name ? Formatted($"<span class=\"pb-co\"{ReaderText.AsFiledMark}>{Escaped(name)}</span>") : string.Empty)}{(also is { } said ? Formatted($"<span class=\"pb-also\">{Escaped(said)}</span>") : string.Empty)}</td>");

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
        html.Append(TippedHeading("Setup", "the family whose live or provisional rule bought it, in that family's own account"));
        html.Append(TippedHeading("Bought", "its buy at the open of the session after the night that listed it, and that session", "r"));
        html.Append(TippedHeading("Stop", "its stop tonight: the plan's, or a trailing stop raised to the highest close since its listing less the distance it trails", "r"));
        html.Append(TippedHeading("Target", "its plan's target, or that its stop trails and it has none, or none for a holding sold only at a rebalance and a short-term dip sold on its exit", "r"));
        html.Append(TippedHeading("Last close", "tonight's close, and the sessions since its listing night against the sessions its plan gives it", "r"));
        html.Append(TippedHeading("So far", "its result at tonight's close in multiples of the risk its plan's close put up, with its move in per cent beneath; a holding's in per cent alone", "r"));
        html.Append(TippedHeading("Where it stands", "near its target or near its stop within one typical day's move of either, on track otherwise, or that its buy opened past its stop or its target; a holding's lead over its sector tonight"));
        html.Append("</tr></thead><tbody>");

        foreach (var trade in trades)
        {
            var open = trade.Open;
            var book = trade.Stop is null;

            html.Append(Invariant, $"<tr data-ticker=\"{Escaped(trade.Ticker)}\" data-family=\"{Escaped(trade.Family)}\" data-account=\"{Escaped(trade.Account)}\" data-bought=\"{DayOf(trade.Bought)}\" data-standing=\"{Escaped(open?.Standing ?? string.Empty)}\">");
            html.Append(StockCell(trade.Ticker, trade.Company, Copying ? null : AlsoHeld(trade.AlsoHeld)));
            html.Append(FamilyCell(trade.Family));
            html.Append(Invariant, $"<td class=\"r num\">{Figures.Price(trade.Buy)}<span class=\"pb-day\">{DayOf(trade.Bought)}</span></td>");
            // A holding with no price stop draws how its rule sells it across its stop's and target's cells.
            // see: A holding with no price stop draws how its rule sells it and whether it still qualifies tonight by its own rule
            html.Append(book
                ? Formatted($"<td class=\"pb-sells\" colspan=\"2\" data-sells>{(trade.Sells is { } sells ? Escaped(sells) : "<span class=\"degraded\">sold by its rule, which this page did not read</span>")}</td>")
                : Formatted($"<td class=\"r num\">{(open?.Stop is { } stop ? Figures.Price(stop) : "<span class=\"degraded\">none</span>")}{(trade.Trails && open?.Stop is { } raised && trade.Stop is { } first && raised > first ? Formatted($"<span class=\"pb-day\">raised from {Figures.Price(first)}</span>") : string.Empty)}</td>"));
            html.Append(book
                ? string.Empty
                : trade.Target is { } target ? Formatted($"<td class=\"r num\">{Figures.Price(target)}</td>")
                : trade.Trails ? "<td class=\"r\">it trails</td>"
                : "<td class=\"r\"><span class=\"degraded\">none</span></td>");
            html.Append(Invariant, $"<td class=\"r num\">{(open?.Close is { } shut ? Figures.Price(shut) : "<span class=\"degraded\">not stored</span>")}<span class=\"pb-day\">{Held(open?.Held, trade.Cap)}</span></td>");
            html.Append(Invariant, $"<td class=\"r num\">{Result(book, (open?.Risks, open?.Fraction))}</td>");
            html.Append(Invariant, $"<td><span class=\"pb-standing {StandingClass(open?.Standing)}\" data-standing=\"{Escaped(open?.Standing ?? string.Empty)}\">{Escaped(trade.Selling is { } why ? Capitalised(TradeGroupsView.SellsWords) + ": " + why : Capitalised(open?.Standing ?? HeavyweightStandings.NotRead))}</span></td></tr>");
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
        { } still when still.StartsWith("Still qualifies", StringComparison.Ordinal) => "leading",
        { } fell when fell.StartsWith("No longer qualifies", StringComparison.Ordinal) || fell.StartsWith("Score ", StringComparison.Ordinal) || fell.StartsWith("Now dearer", StringComparison.Ordinal) => "lost-lead",
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
        html.Append(TippedHeading("Setup", "the family whose live or provisional rule bought it, in that family's own account"));
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

    // The listed stocks not bought, in one line beneath an account's trades, a reason at a time: "Not bought: n picks,
    // <reason>: tickers", each stock linking to its chart, never a row of a trade table.
    // see: A skipped pick never sits in a trade table, and each account's trades end on one line naming what it did not buy by reason
    static string NotBought(PaperNotBought unbought, int reshuffled = 0, string? family = null) =>
        unbought.Count == 0
            ? "<p class=\"paper-unbought\" data-unbought=\"0\">Every stock these rules listed was bought.</p>"
            : Formatted($"<p class=\"paper-unbought\" data-unbought=\"{unbought.Count}\"{(family is null ? string.Empty : Formatted($" data-family=\"{Escaped(family)}\""))}>{(family is null ? string.Empty : Formatted($"<b>{Escaped(OwnAccount(family))}.</b> "))}Not bought: ")
                + string.Join("; ", unbought.Reasons.Select(reason => Formatted($"<span class=\"pu-reason\" data-count=\"{reason.Stocks.Count}\">{reason.Stocks.Count} {(reason.Stocks.Count == 1 ? "pick" : "picks")}, {Escaped(reason.Why)}{(reshuffled > 0 && reason.Why == EquityBrief.Core.Cards.PaperAccount.Waiting ? Formatted($", {reshuffled} of them the monthly books' reshuffle") : string.Empty)}: {string.Join(", ", reason.Stocks.Select(stock => Formatted($"<a href=\"#/chart/{Uri.EscapeDataString(stock.Ticker)}\" title=\"listed {DayOf(stock.Listed)}\">{Escaped(stock.Ticker)}</a>")))}</span>")))
                + ".</p>";

    // One account's listings waiting on the next session's open, as to buy: its setup's name, each stock linking to its chart,
    // one another live account holds open named with that account, and a monthly setup's said to be its reshuffle's.
    static string ToBuy(PaperAccountUnbought account) =>
        Formatted($"<p class=\"paper-tobuy\" data-tobuy=\"{account.Waiting.Count}\" data-family=\"{Escaped(account.Family)}\"><b>{Escaped(OwnAccount(account.Family))}.</b> To buy at the next trading day's open{(account.Family is HeavyweightRule.Name or QualityRule.Name ? ", the monthly reshuffle's" : string.Empty)}: ")
            + string.Join(", ", account.Waiting.Select(stock => Formatted($"<a href=\"#/chart/{Uri.EscapeDataString(stock.Ticker)}\" title=\"listed {DayOf(stock.Listed)}\">{Escaped(stock.Ticker)}</a>{(stock.HeldBy.Count == 0 ? string.Empty : Formatted($" ({Escaped(string.Join(" and ", stock.HeldBy.Select(OwnAccount)))} already holds it)"))}")))
            + ".</p>";

    // A family's own account in words, its name's possessive as English writes it.
    static string OwnAccount(string family) =>
        FamilyName(family) is var name && name.EndsWith('s') ? name + "' own account" : name + "'s own account";

    // The picks an account's walk skipped, in that one line, a reason at a time, the most skipped first.
    static string Skipped(IEnumerable<(string Ticker, DateOnly Listed, string Why)> skipped) =>
        NotBought(new PaperNotBought(
        [
            .. skipped
                .GroupBy(one => one.Why, StringComparer.Ordinal)
                .OrderByDescending(group => group.Count())
                .ThenBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => new PaperUnbought(group.Key, [.. group.OrderByDescending(one => one.Listed).ThenBy(one => one.Ticker, StringComparer.Ordinal).Select(one => (one.Ticker, one.Listed))])),
        ]));

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
    // saying how a variant is adopted. From 21.20 a variant registered after the newest night follows them at once, with
    // what it changes and the day it was registered and its first night, and no trades until a night reads it. A family
    // with no variant says so.
    public string PaperVariants(PaperPageView page, PaperLink link, string family)
    {
        var html = new StringBuilder();
        var families = page.Accounts.Where(account => account.Head).OrderBy(account => SetupFamilies.OrderOf(account.Family)).ToArray();
        var rows = page.Accounts.Where(account => account.Family == family && account.Standing != PaperAccountView.You).OrderBy(account => account.Head ? 0 : 1).ToArray();
        var coming = (page.Coming ?? []).Where(one => one.Family == family).ToArray();
        var variants = rows.Count(account => !account.Head) + coming.Length;

        html.Append(FamilyPick(
            PaperLink.VariantsKey,
            "The setup whose variants are drawn",
            [.. families.Select(head =>
            {
                var count = page.Accounts.Count(account => account.Family == head.Family && account.Standing == PaperAccountView.Variant) + (page.Coming ?? []).Count(one => one.Family == head.Family);

                return (head.Family, Formatted($"{FamilyName(head.Family)} · {(head.Standing == PaperAccountView.Live ? "live" : "provisional")} rule{(count == 0 ? ", no variant" : Formatted($" and {count} {(count == 1 ? "variant" : "variants")}"))}"));
            })],
            family));

        if (variants == 0)
        {
            html.Append(Invariant, $"<p class=\"degraded\" data-variants=\"none\">No variant is registered for {Escaped(FamilyName(family))} on this index, so its rule alone trades.</p>");
        }

        // From the 19.4 correction of 2026-11-07 a family whose rule and every version have made no trade since its record
        // began says why in one line in place of a table of zeros.
        // see: A version is marked as narrowing or widening its live rule by the direction of each setting it changes
        if (NoTradeLine(rows, coming.Length, family) is { } none)
        {
            html.Append(Invariant, $"<p class=\"paper-no-trade\" data-no-trade=\"{rows.Length - 1}\">{Escaped(none)}</p>");
            html.Append(Invariant, $"<p class=\"paper-adopted\">{Escaped(AdoptedLine)}</p>");

            return html.ToString();
        }

        html.Append(Invariant, $"<div class=\"tbl-wrap\"><table class=\"paper-variants\" data-family=\"{Escaped(family)}\" data-rows=\"{rows.Length + coming.Length}\"><thead><tr>");
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
            html.Append(Invariant, $"<td class=\"pv-rule\"><a class=\"paper-log\" href=\"{LogHref(link.Universe, account.Key)}\" title=\"{Escaped(Capitalised(account.Words) + (account.Version is { } version ? "; counts " + version : string.Empty) + (account.Since is { } since ? "; from " + DayOf(since) : string.Empty))}\">{Escaped(account.Short ?? account.Name)}</a><span class=\"pv-verdict\" data-verdict=\"{Escaped(account.Verdict)}\">{Escaped(account.Verdict)}</span>{SinceWords(account)}</td>");
            html.Append(Invariant, $"<td>{Escaped(account.Changes ?? string.Empty)}{(account.Reach is { } reach ? Formatted($"<span class=\"pv-reach\" data-reach=\"{reach.ToString().ToLowerInvariant()}\">{Escaped(EquityBrief.Core.Cards.VersionReach.Words(reach))}</span>") : string.Empty)}{AccountNote(account)}</td>");
            html.Append(Invariant, $"<td class=\"r num\">{account.Overlap?.Trades ?? account.Trades}</td><td class=\"r num\">{account.Overlap?.Shared ?? 0}</td><td class=\"r num\">{account.Overlap?.Only ?? 0}</td>");
            html.Append(Invariant, $"<td class=\"r num\" data-return=\"{(account.Walk?.Return is { } held ? held.ToString("R", Invariant) : "none")}\">{(account.Walk?.Return is { } made ? PaperPercent(made) : "<span class=\"degraded\">none yet</span>")}{(account.TooFew is { } few ? Formatted($"<span class=\"too-few\">{Escaped(few)}</span>") : string.Empty)}</td></tr>");
        }

        foreach (var one in coming)
        {
            html.Append(Invariant, $"<tr data-standing=\"coming\" data-registered=\"{DayOf(one.Registered)}\" data-first-night=\"{(one.FirstNight is { } first ? DayOf(first) : "none")}\">");
            html.Append(Invariant, $"<td class=\"pv-rule\"><span title=\"{Escaped(Capitalised(one.Words))}\">{Escaped(one.Short)}</span></td>");
            html.Append(Invariant, $"<td>{Escaped(one.Changes ?? string.Empty)}<span class=\"pv-note\" data-note=\"coming\">{Escaped(ComingLine(one))}</span></td>");
            html.Append("<td class=\"r num\">0</td><td class=\"r num\">0</td><td class=\"r num\">0</td><td class=\"r num\" data-return=\"none\"><span class=\"degraded\">none yet</span></td></tr>");
        }

        html.Append("</tbody></table></div>");

        if (variants > coming.Length)
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

        // From 23.2 the setup's rules summed across the three indices beside the index's own verdict, with the passes luck
        // alone would bring a year.
        // see: One verdict across the three indices stands beside each index's own, and an approval still changes one index's rule
        html.Append(PooledPart(page.Pooled.FirstOrDefault(row => row.Own && row.Family == head.Family), PooledSetups(page.Pooled)));

        // From 23.7 the rule over the long history, the years its setting never saw apart from those it was chosen on.
        // see: The monthly run's last step judges every registered rule over the long history from each family's own first test year, capped at three hours
        html.Append(LongPart(
            page.Long.FirstOrDefault(row => row.Family == head.Family && row.Standing != PaperAccountView.Variant),
            page.LongPooled.FirstOrDefault(row => row.Own && row.Family == head.Family)));

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
                html.Append(Invariant, $"<td><b title=\"{Escaped(Capitalised(variant.Words))}\">{Escaped(variant.Short ?? variant.Name)}</b><span class=\"pb-co\">{Escaped(variant.Changes ?? variant.Words)}</span>{(variant.Reach is { } reach ? Formatted($"<span class=\"pv-reach\" data-reach=\"{reach.ToString().ToLowerInvariant()}\">{Escaped(EquityBrief.Core.Cards.VersionReach.Words(reach))}</span>") : string.Empty)}{AccountNote(variant)}</td>");
                html.Append(Invariant, $"<td><span class=\"pill\"{(variant.Adjusted is { } p ? Formatted($" title=\"adjusted p {p.ToString("0.0000", Invariant)} against the bar {LoopGate.Bar.ToString("0.0000", Invariant)}\"") : string.Empty)}>{Escaped(variant.Verdict)}</span>{(variant.Tested is { } tested ? PooledChangeNote(tested, page.Pooled) : string.Empty)}</td>");
                html.Append(Invariant, $"<td class=\"r num\">{(variant.Better is { } of && variant.Counted is { } counted ? Formatted($"{of} of {counted}") : "<span class=\"degraded\">not read</span>")}{JudgedFromNote(variant)}</td>");
                html.Append(Invariant, $"<td class=\"r num\">{Progressed(variant.Progress, variant.Standing)}</td></tr>");
            }

            html.Append("</tbody></table></div></details>");
        }

        html.Append("</div>");

        return html.ToString();
    }

    // From 22.17 the read-only copy's scorecard: each family's live or provisional rule by its family, the trades its account
    // has closed and its return so far, never a setting, a test year or a variant.
    // see: The read-only copy is published to Cloudflare Pages with no control and no part of the method, and its upload refuses a page that leaks one
    public string PlainScorecard(PaperPageView page)
    {
        var heads = page.Accounts.Where(account => account.Head).OrderBy(account => SetupFamilies.OrderOf(account.Family)).ToArray();

        if (heads.Length == 0)
        {
            return "<p class=\"degraded\" data-score=\"none\">No rule stands on this index yet.</p>";
        }

        var html = new StringBuilder("<div class=\"tbl-wrap\"><table class=\"paper-bought plain-score\"><thead><tr><th>Setup</th><th class=\"r\">Trades closed</th><th class=\"r\">Return so far</th></tr></thead><tbody>");

        foreach (var head in heads)
        {
            html.Append(Invariant, $"<tr data-family=\"{Escaped(head.Family)}\" data-closed=\"{head.Closed}\">");
            html.Append(FamilyCell(head.Family));
            html.Append(Invariant, $"<td class=\"r num\">{head.Closed}</td>");
            html.Append(Invariant, $"<td class=\"r num\">{(head.Walk?.Return is { } made ? PaperPercent(made) : "<span class=\"degraded\">none yet</span>")}</td></tr>");
        }

        return html.Append("</tbody></table></div>").ToString();
    }

    // From 21.14 the line an account's rule carries beneath its words, the tone variant's that its history before the local
    // model's cutoff does not count; none for any other.
    // The one line a family draws where its live or provisional rule and every version standing beside it have made no trade
    // since the rule's record began: why none could trade where each version narrows the rule, and otherwise how many of
    // them narrow it, widen it or take its picks another way; none where any has traded, a version waits on its first night
    // or the family has no version.
    public static string? NoTradeLine(IReadOnlyList<PaperAccountView> rows, int coming, string family)
    {
        var head = rows.FirstOrDefault(row => row.Head);
        var versions = rows.Where(row => !row.Head).ToArray();

        if (head is null || versions.Length == 0 || coming > 0 || rows.Any(row => (row.Overlap?.Trades ?? row.Trades) > 0))
        {
            return null;
        }

        var began = head.Since is { } since ? " since its record began on " + since.ToString("d MMM", Invariant) : " since its record began";

        if (versions.All(version => version.Reach == EquityBrief.Core.Cards.VersionReachKind.Narrows))
        {
            return Formatted($"{FamilyName(family)} has made no trade{began}, and each of its versions narrows it, so none could trade either.");
        }

        string Count(EquityBrief.Core.Cards.VersionReachKind? kind, string one, string many) =>
            versions.Count(version => version.Reach == kind) is var count and > 0 ? Formatted($"{count} {(count == 1 ? one : many)}") : string.Empty;

        string[] parts =
        [
            Count(EquityBrief.Core.Cards.VersionReachKind.Narrows, "narrows it", "narrow it"),
            Count(EquityBrief.Core.Cards.VersionReachKind.Widens, "widens it", "widen it"),
            Count(EquityBrief.Core.Cards.VersionReachKind.SoldDifferently, "takes the same picks sold differently", "take the same picks sold differently"),
            Count(EquityBrief.Core.Cards.VersionReachKind.Reordered, "takes the same picks in another order", "take the same picks in another order"),
            Count(EquityBrief.Core.Cards.VersionReachKind.Unread, "changes a setting whose reach is not read here", "change a setting whose reach is not read here"),
            Count(null, "has no live rule's settings to read against", "have no live rule's settings to read against"),
        ];
        var said = parts.Where(part => part.Length > 0).ToArray();

        return Formatted($"{FamilyName(family)} has made no trade{began}, and none of its {versions.Length} versions has traded either: {(said.Length == 1 ? said[0] : string.Join(", ", said[..^1]) + " and " + said[^1])}.");
    }

    // From the 19.4 correction of 2026-11-07 a row's start in the versions' part: the live or provisional rule's record since
    // its account's day, each version's trading since its own, none where an account has no day yet.
    // see: Each row of what the versions did differently gives its start, the live rule's record since and each version's trading since
    static string SinceWords(PaperAccountView account) =>
        account.Since is not { } since
            ? string.Empty
            : Formatted($"<span class=\"pv-since\" data-since=\"{DayOf(since)}\">{(account.Head ? "record since" : "trading since")} {since.ToString("d MMM", Invariant)}</span>");

    // A variant no night has read yet: the day it was registered and the night that first reads it, by its weekday and day.
    public static string ComingLine(PaperComingView one) =>
        Formatted($"Registered {DayOf(one.Registered)}. Its first night is {(one.FirstNight is { } first ? first.DayOfWeek + " " + DayOf(first) : "the next night the exchange trades")}.");

    // From 23.5 a drift account carries the words that its years read mostly survivors, beside any other note.
    // see: The S&P 500 drift is judged from 2019 where no year's surprises reach the coverage its rule asks and every year is labelled mostly survivors
    static string AccountNote(PaperAccountView account) =>
        (account.Note is { } note ? Formatted($"<span class=\"pv-note\" data-note=\"tone\">{Escaped(note)}</span>") : string.Empty)
        + (account.Family == EquityBrief.Core.Families.DriftRule.Name ? Formatted($"<span class=\"pv-note\" data-note=\"survivors\">{Escaped(EquityBrief.Core.History.LongFirstYears.SurvivorsNote)}</span>") : string.Empty)
        + (StandsAtTheTarget(account.Words) ? Formatted($"<span class=\"pv-note\" data-note=\"standing\">{Escaped(EquityBrief.Core.Loop.ExitMenu.StandingWords)}</span>") : string.Empty);

    // From 21.17 the words beside a variant's years better where its rule reads a release reading: its verdict reads the
    // years from the local model's cutoff's year alone, the years before read by a model trained after them; none for any
    // other.
    // see: A rule or a change reading a release reading is judged on the test years from the model's cutoff's year alone, the earlier years printed beside it
    static string JudgedFromNote(PaperAccountView account) =>
        account.JudgedFrom is { } from
            ? Formatted($"<span class=\"pv-note\" data-judged-from=\"{from}\">from {from} alone; the years before {Escaped(CutoffLabel)}</span>")
            : string.Empty;

    // A record's progress in words: decided trades or a book's months of the count a verdict reads.
    static string Progressed(PaperProgress? progress, string standing) =>
        standing == PaperAccountView.Provisional ? "no record kept"
            : progress is not { } held ? "<span class=\"degraded\">not read</span>"
            : held.Needed == LoopScreens.BookMonths ? Formatted($"{held.Counted} of {held.Needed} months")
            : Formatted($"{held.Counted} of {held.Needed} decided");

    static string CutoffLabel => ReleaseJudging.TrainedAfter;

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
        // From 21.17 a rule reading a release reading is judged on the years from the local model's cutoff's year alone, the
        // years before drawn beside them and labelled as read by a model trained after them.
        // see: A rule or a change reading a release reading is judged on the test years from the model's cutoff's year alone, the earlier years printed beside it
        if (head.JudgedFrom is { } from)
        {
            html.Append(Invariant, $"<p class=\"sh-line\" data-judged-from=\"{from}\">Its verdict reads the years from {from} alone. The years before are shown beside them, {Escaped(CutoffLabel)}.</p>");
        }

        html.Append("<div class=\"sh-years\">");

        foreach (var box in boxes)
        {
            var kind = box.Word == PaperAccount.Better ? "better" : box.Word == PaperAccount.Worse ? "worse" : box.Word == PaperAccount.Even ? "even" : "none";
            var year = head.Years.FirstOrDefault(row => row.Year == box.Year);
            var before = head.JudgedFrom is { } judged && box.Year < judged;

            html.Append(Invariant, $"<div class=\"sy {kind}\" data-year=\"{box.Year}\" data-word=\"{Escaped(box.Word)}\" data-edge=\"{(box.Edge is { } held ? held.ToString("R", Invariant) : "none")}\"{(before ? " data-before-cutoff=\"1\"" : string.Empty)}{(year is null ? string.Empty : Formatted($" title=\"{box.Year}: {year.Units} {(book ? (year.Units == 1 ? "month" : "months") : (year.Units == 1 ? "trade" : "trades"))}, a total of {Escaped(Plain(head.Unit, year.Total))} against {Escaped(Plain(head.Unit, year.ComparedTotal))}{(before ? ", " + Escaped(CutoffLabel) : string.Empty)}\""))}>");
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
    // leaves out, each naming the version that listed it, and the same trades before its version among them.
    // see: A paper account counts from its rule's current version's first session, and the same trades before it stand apart under the account
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

        // The trades it bought alone, and beneath them the one line naming what it did not buy by reason.
        // see: A skipped pick never sits in a trade table, and each account's trades end on one line naming what it did not buy by reason
        var bought = trades.Where(trade => trade.Skipped is null).ToArray();

        html.Append(Invariant, $"<div class=\"tbl-wrap\"><table class=\"paper-log-table\" data-rows=\"{bought.Length}\"><thead><tr><th>Stock</th><th>Bought</th><th class=\"r\">Buy</th><th class=\"r\">Stop</th><th>Ended</th><th class=\"r\">Result</th></tr></thead><tbody>");

        foreach (var trade in bought.OrderByDescending(trade => trade.Bought).ThenBy(trade => trade.Ticker, StringComparer.Ordinal))
        {
            html.Append(Invariant, $"<tr data-ticker=\"{Escaped(trade.Ticker)}\" data-bought=\"{DayOf(trade.Bought)}\" data-open=\"{Flag(trade.Open)}\"{(trade.Halved ? " data-halved=\"true\"" : string.Empty)}>");
            html.Append(Invariant, $"<td><a href=\"#/chart/{Uri.EscapeDataString(trade.Ticker)}\">{Escaped(trade.Ticker)}</a>{(trade.Halved ? " <span class=\"pill\" title=\"The market check held one of its two signals that night, so the trade risked half the usual amount\">half size</span>" : string.Empty)}</td><td>{DayOf(trade.Bought)}</td>");
            html.Append(Invariant, $"<td class=\"r num\">{Figures.Price(trade.Buy)}</td><td class=\"r num\">{(trade.Stop is { } stop ? Figures.Price(stop) : "<span class=\"degraded\">none</span>")}</td>");
            html.Append(trade.Open ? "<td><span class=\"pill\">open</span></td>" : Formatted($"<td>{(trade.Ended is { } on ? DayOf(on) : "not read")}</td>"));
            html.Append(Invariant, $"<td class=\"r num\">{Result(trade.Result)}{(trade.Open ? " <span class=\"degraded\">so far</span>" : string.Empty)}</td></tr>");
        }

        html.Append("</tbody></table></div>");

        if (trades.Any(trade => trade.Skipped is not null))
        {
            html.Append(Skipped(trades.Where(trade => trade.Skipped is not null).Select(trade => (trade.Ticker, trade.Bought, trade.Skipped!))));
        }

        return html.ToString() + EarlierVersions(earlier ?? [], Result);
    }

    // From 21.11 the combined account's card, from the 21.11 correction of 2026-11-07 the live setups' accounts averaged: one
    // line saying what it is, then each trade its accounts hold over its days, newest first and each session's in Tonight's
    // order, with the setup whose own account bought it, the shares that account bought, and its return on the position, so
    // far where it is open, or why that account skipped it.
    // see: The averaged account gives each live rule's own account an equal share, from the day every one of them counts
    public static string AveragedLine(PaperCombinedView all) =>
        Formatted($"Each of the {all.Accounts} live setups' own accounts holds an equal share of this one from {DayOf(all.Since)}, the day every one of them counts, so it holds what each holds and skips what each skips, and no pick twice: following every rule reads the same. Each row is a trade as its setup's own account bought it, its shares that account's.");

    // How each monthly book's holdings are sized, as the code sizes them: in its own setup's account, an equal share of that
    // account across the most holdings its book can hold, at most a tenth of it; none where the account holds no book.
    // see: A holding with no stop is sized at an equal share of the account across the most holdings its book can hold
    public static string? BooksLine(PaperCombinedView all)
    {
        var books = all.Trades
            .Where(trade => trade.Skipped is null && trade.BookHoldings is > 0)
            .GroupBy(trade => trade.Family, StringComparer.Ordinal)
            .OrderBy(group => BuyOrder.Of(group.Key))
            .Select(group => (Family: group.Key, Holdings: group.First().BookHoldings!.Value))
            .ToArray();

        return books.Length == 0 || all.Size <= 0m
            ? null
            : "Each holding of a monthly setup is sized in its own setup's account at an equal share of that account across the most holdings the setup can hold, never more than a tenth of it: "
                + string.Join("; ", books.Select(book => Formatted($"{FamilyName(book.Family)} across {book.Holdings}, about ${Math.Min(all.Size / book.Holdings, all.Size * PaperAccount.AccountCap):#,0} a holding on ${all.Size:#,0}")))
                + ". An account's value moves with its trades, so each position's dollars stand beside it.";
    }

    public string PaperCombined(PaperCombinedView? combined)
    {
        if (combined is not { } all)
        {
            return "<p class=\"degraded\" data-combined=\"none\">No live setup's account has a day to count from yet, so the averaged account holds none.</p>";
        }

        var html = new StringBuilder();

        var bought = all.Trades.Where(trade => trade.Skipped is null).ToArray();

        html.Append(Invariant, $"<div class=\"paper-combined\" data-trades=\"{bought.Length}\" data-skipped=\"{all.Skipped}\" data-accounts=\"{all.Accounts}\" data-since=\"{DayOf(all.Since)}\">");
        html.Append(Invariant, $"<p class=\"paper-combined-skips\">{Escaped(AveragedLine(all))}</p>");
        html.Append(BooksLine(all) is { } books ? Formatted($"<p class=\"paper-combined-books\" data-books>{Escaped(books)}</p>") : string.Empty);
        html.Append("<div class=\"tbl-wrap\"><table class=\"paper-combined-table\"><thead><tr><th>Setup</th><th>Stock</th><th>Bought</th><th class=\"r\">Buy</th><th class=\"r\">Shares</th><th class=\"r\">Dollars</th><th>Ended</th><th class=\"r\">Return</th></tr></thead><tbody>");

        // Each session's trades in Tonight's order of the setups, each setup's in its own account's order; a pick an account
        // skipped is on the line beneath and never a row.
        // see: A skipped pick never sits in a trade table, and each account's trades end on one line naming what it did not buy by reason
        foreach (var trade in bought.Select((trade, at) => (Trade: trade, At: at)).OrderByDescending(one => one.Trade.Bought).ThenBy(one => BuyOrder.Of(one.Trade.Family)).ThenBy(one => one.At).Select(one => one.Trade))
        {
            html.Append(Invariant, $"<tr data-family=\"{Escaped(trade.Family)}\" data-ticker=\"{Escaped(trade.Ticker)}\" data-bought=\"{DayOf(trade.Bought)}\" data-open=\"{Flag(trade.Open)}\">");
            html.Append(Invariant, $"<td><span class=\"fam-swatch\" data-family=\"{Escaped(trade.Family)}\"></span>{Escaped(FamilyName(trade.Family))}</td><td><a href=\"#/chart/{Uri.EscapeDataString(trade.Ticker)}\">{Escaped(trade.Ticker)}</a></td><td>{DayOf(trade.Bought)}</td>");
            html.Append(Invariant, $"<td class=\"r num\">{Figures.Price(trade.Buy)}</td><td class=\"r num\">{trade.Shares.ToString("#,0", Invariant)}</td><td class=\"r num\" data-dollars=\"{(trade.Shares * trade.Buy).ToString("0.00", Invariant)}\">${trade.Shares * trade.Buy:#,0}</td>");
            html.Append(trade.Open ? "<td><span class=\"pill\">open</span></td>" : Formatted($"<td>{(trade.Ended is { } on ? DayOf(on) : "not read")}</td>"));
            html.Append(Invariant, $"<td class=\"r num\">{(trade.Return is { } made ? PaperPercent(made) : "<span class=\"degraded\">not read</span>")}{(trade.Open ? " <span class=\"degraded\">so far</span>" : string.Empty)}</td></tr>");
        }

        html.Append("</tbody></table></div>");
        html.Append(all.Skipped == 0 ? "<p class=\"paper-unbought\" data-unbought=\"0\">Every pick its accounts were handed was bought.</p>" : Skipped(all.Trades.Where(trade => trade.Skipped is not null).Select(trade => (trade.Ticker, trade.Bought, trade.Skipped!))));
        html.Append("</div>");

        return html.ToString();
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
                : family == ImprovingRule.Name
                    ? ImprovingRule.Label
                    : SetupFamilies.Named(family)?.Label ?? family;
}
