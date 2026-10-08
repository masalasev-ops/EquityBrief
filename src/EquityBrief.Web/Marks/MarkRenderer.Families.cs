using System.Text;

namespace EquityBrief.Web.Marks;

// One stock a family's card lists: the night's row for it as every list draws a name, its place down the
// page, the trade its family's plan states, with no target where the plan trails its stop, the reward to risk as the card
// prints it, why the family lists it tonight in that family's words, and the labels of the other families
// it qualified under.
// see: A stock holds one trade across every swing family, and one qualifying under two is listed once under the first in the page's order
public sealed record FamilyPickCell(
    ListingCell Row,
    int Place,
    decimal? Buy,
    decimal? Stop,
    decimal? Target,
    bool Trailing,
    decimal? RewardToRisk,
    string Why,
    IReadOnlyList<string> Also,
    DecisionCardView? Card = null);

// One family's card on a night: the family's words, where it stands in the page's order, the day its
// rule went live or nothing while it runs on provisional settings, how many variants are scored beside
// it, its picks in the page's order, the notes beneath them, and why it lists nothing where it lists none.
// see: Tonight's page is drawn from setup families, each a rule of its own listing at most five a night
// see: A family lists on provisional settings until its freeze, and nothing before the freeze counts toward a checkpoint
public sealed record FamilyCardView(
    string Family,
    string Label,
    string Heading,
    string Eyebrow,
    string Rule,
    int Place,
    int Of,
    DateOnly? LiveSince,
    int Variants,
    IReadOnlyList<FamilyPickCell> Picks,
    IReadOnlyList<string> Notes,
    string? Empty,
    bool SweepFoundNone = false,
    RuleView? RuleChoice = null);

// One choice on a card's selector: the rule's name as the register holds it, the slug the link keeps it under, its
// variant number by its first registration or none for the live rule.
public sealed record RuleChoiceView(string Rule, string Slug, int? Variant, bool Live);

// One clause of a rule's words, marked where the live rule's words do not carry it.
public sealed record RulePartView(string Text, bool Differs);

// One pick of a variant's own list on a night, drawn under the band and never as tonight's list.
public sealed record VariantPickView(int Place, string Ticker, decimal Entry, decimal Stop, decimal? Target, double? RewardToRisk, string Why);

// A rule's empty stretch on a night against its mark, with the completed stretches and the sessions the mark was read
// over, the mark none under the floors.
public sealed record StretchLineView(int Stretch, int? Mark, bool Flagged, int Completed, int Sessions);

// One member forming a breakout under a card's rule.
public sealed record FormingRowView(int Place, string Ticker, decimal Close, decimal High, double MovesUnder, double VolumeNeeded, double Volume, double RangeRatio, IReadOnlyList<string> Missing, DateOnly? NextEarnings);

// The breakouts forming under a card's rule: the rows drawn of the count forming, and whether the market check left
// the rule's list open on the night.
public sealed record FormingListView(IReadOnlyList<FormingRowView> Rows, int Forming, bool MarketOpen);

// The rule a card draws: the choice made, every choice, the chosen rule's words in clauses, a variant's own picks, the
// funnel, the stretch line, the forming list, whether the night evaluated the rule, and the line an index with no
// variant carries.
// see: A variant's picks are shown on its card when chosen and its results only under its tests
public sealed record RuleView(
    RuleChoiceView Chosen,
    IReadOnlyList<RuleChoiceView> Choices,
    IReadOnlyList<RulePartView> Parts,
    IReadOnlyList<VariantPickView>? Picks,
    IReadOnlyList<(string Gate, int Passed)>? Funnel,
    StretchLineView? Stretch,
    FormingListView? Forming,
    bool Evaluated,
    string? NoVariantLine);

// The line tonight's page opens on, on a night the families drew its list: the index it is over, whether the
// market check left the lists open, the breadth it read against its floor, how many of the index's members a
// setup passed, the buy points the page lists and how many of the setups list one, the stocks a single gate
// short of one where the night read them, and the trades still open.
// see: The market check closes every swing family's list together, and the sector heavyweights read none
// see: Every page reads one index at a time chosen under Universe, and every figure names its index
public sealed record MarketLineView(
    bool Open,
    double? Breadth,
    double? Floor,
    int BuyPoints,
    int SetupsListing,
    int Setups,
    int Close,
    int OpenTrades,
    string Index = "S&P 500",
    int? Passed = null,
    int? Members = null,
    bool CloseRead = true,
    string? Universe = null);

// One stock a single gate short of a buy point under one setup, as the shared list draws it: the setup's
// word and label, the gate it missed, and that gate's own words.
public sealed record CloseToBuyCell(string Ticker, string? Company, string Family, string Setup, string Gate, string Words);

// What a name's page says of the page's list on its night: the night, the setup that lists the name with its
// place and the other setups it qualified under, or none where no setup lists it, and a sentence for each
// setup that passed it and holds it back.
public sealed record ListedUnderView(DateOnly Night, string? Family, string? Heading, int? Place, IReadOnlyList<string> Also, IReadOnlyList<string> HeldBack);

// One setup family on the run page: the day its rule went live or none while it is provisional, the variants
// scored beside it, what it lists on the night, the trades the page has listed under it with how many are
// open and finished, and its record in words.
public sealed record FamilyRunRow(string Family, string Heading, DateOnly? LiveSince, int Variants, int ListedTonight, int Trades, int Open, int Finished, string Record);

// One registered rule of a new setup family on the run page, read over its own trades: the family's heading,
// the rule's name and whether it is the live one, the trades it kept, those decided and their edge, the whole
// blocks against the look they wait for, the level its looks are read at, the record in words, the session its
// record counts from and, where a replay at its registration found a trade it would not have kept the same, why
// it restarted there.
// see: A registered family rule is evaluated every night at its own settings and keeps its own list, its trades stored with their benchmark when they end
// see: A family rule registered again keeps its record from its first registration where a replay of its stored nights reproduces every trade, and restarts at the change otherwise
public sealed record FamilyRecordRow(string Family, string Heading, string Rule, bool Live, int Trades, int Decided, double? Edge, int Blocks, int NextLook, double Level, string Record, DateOnly? From = null, string? Restarted = null);

// One trade an S&P 400's or 600's list kept, as Past picks draws it: the stock and its company, the setup that listed
// it and the night, its plan, where it ended by the page's night, its result before its cost, its cost and its result
// after, each in multiples of its risk and none while open.
// see: Every page reads one index at a time chosen under Universe, and every figure names its index
public sealed record IndexTradeCell(
    string Ticker,
    string? Company,
    string Family,
    string Setup,
    DateOnly Listed,
    decimal Entry,
    decimal Stop,
    decimal? Target,
    bool Trailing,
    DateOnly? EndedOn,
    double? Result,
    double? Cost,
    double? AfterCost);

// How an S&P 400's or 600's night went, as its Run page states it: the members read, the index's own breadth against
// the floor and whether its swing lists were open, the members a setup passed, those listed and held back, the trades
// kept and ended, and its sector heavyweights' rebalance and holdings.
public sealed record IndexRunView(int Members, double? Breadth, double Floor, bool MarketOpen, int Passed, int Listed, int HeldBack, int Kept, int Ended, bool Rebalanced, int Holdings, string? Fault = null);

public sealed partial class MarkRenderer
{
    // What each column of an index's trades on Past picks holds.
    public static IReadOnlyList<(string Heading, string Says)> IndexTradeHeadings { get; } =
    [
        ("Stock", "The ticker opens the stock's page, with the company beneath."),
        ("Setup", "The setup that listed it, on the index's provisional settings."),
        ("Listed", "The night its list drew it, bought at that evening's close."),
        ("Buy", "The close it was bought at."),
        ("Stop", "The price a close beneath ends it at a loss, or the first level of a stop that trails."),
        ("Target", "The price a close at or above ends it at a gain; a setup that trails its stop names none."),
        ("Status", "Open, or the session it ended on."),
        ("Result", "What it made in multiples of what it risked, before its cost."),
        ("Cost", "Its round trip at the published spread for its size and price, in multiples of its risk."),
        ("After cost", "Its result less its cost, which is what the index's tests read."),
    ];

    // An index's trades on Past picks, newest first, each with its result before and after its cost.
    public string IndexTrades(IReadOnlyList<IndexTradeCell> rows, string index)
    {
        var body = new StringBuilder();

        body.Append(Invariant, $"<div class=\"tbl-wrap\"><table class=\"list-table index-trades\" data-index=\"{Escaped(index)}\" data-rows=\"{rows.Count}\"><thead><tr>");

        foreach (var (heading, says) in IndexTradeHeadings)
        {
            body.Append(TippedHeading(heading, says, heading is "Buy" or "Stop" or "Target" or "Result" or "Cost" or "After cost" ? "r" : null));
        }

        body.Append("</tr></thead><tbody>");

        foreach (var row in rows)
        {
            body.Append(Invariant, $"<tr data-ticker=\"{Escaped(row.Ticker)}\" data-index=\"{Escaped(index)}\" data-family=\"{Escaped(row.Family)}\" data-listed=\"{DayOf(row.Listed)}\" data-ended=\"{(row.EndedOn is { } end ? DayOf(end) : "open")}\" ");
            body.Append(Invariant, $"data-result=\"{Stated(row.Result)}\" data-cost=\"{Stated(row.Cost)}\" data-after-cost=\"{Stated(row.AfterCost)}\">");
            body.Append(Invariant, $"<td class=\"c-nm\"><a class=\"name-link\" href=\"#/name/{Uri.EscapeDataString(row.Ticker)}\">{Escaped(row.Ticker)}</a>{(row.Company is { Length: > 0 } company ? Formatted($"<span class=\"co\">{Escaped(company)}</span>") : string.Empty)}</td>");
            body.Append(Invariant, $"<td class=\"setup\" data-setup=\"{Escaped(row.Family)}\">{Escaped(row.Setup)} · {Escaped(index)}</td>");
            body.Append(Invariant, $"<td>{DayOf(row.Listed)}</td>");
            body.Append(PriceCell("buy", row.Entry, null));
            body.Append(PriceCell("stop", row.Stop, null));
            body.Append(PriceCell("target", row.Target, row.Trailing ? "trailing" : null));
            body.Append(row.EndedOn is { } ended ? Formatted($"<td>ended {DayOf(ended)}</td>") : "<td><b>open</b></td>");
            body.Append(Invariant, $"<td class=\"r num\">{Multiple(row.Result)}</td><td class=\"r num\">{Multiple(row.Cost)}</td><td class=\"r num\">{Multiple(row.AfterCost)}</td></tr>");
        }

        body.Append("</tbody></table></div>");

        return body.ToString();
    }

    static string Multiple(double? value) =>
        value is { } stated ? stated.ToString("+0.00;-0.00;0.00", Invariant) : "<span class=\"degraded\">none yet</span>";

    // The run page's setups, one row a family in the page's order.
    public string FamilyRun(IReadOnlyList<FamilyRunRow> rows)
    {
        var body = new StringBuilder();

        body.Append(Invariant, $"<div class=\"tbl-wrap\"><table class=\"list-table family-run\" data-rows=\"{rows.Count}\"><thead><tr>");

        foreach (var (heading, says) in new[]
        {
            ("Setup", "The setup family, in the page's order."),
            ("Rule", "Live since the day its rule was registered, or provisional while no freeze has registered it."),
            ("Variants", "The variants of its rule scored in the background beside it."),
            ("Listed tonight", "The stocks the page lists under it on this night."),
            ("Trades so far", "Every trade the page has listed under it, with how many are still open and how many finished."),
            ("Record", "Where its record stands against what it waits for. A provisional setup's record starts at its freeze."),
        })
        {
            body.Append(TippedHeading(heading, says, heading is "Variants" or "Listed tonight" ? "r" : null));
        }

        body.Append("</tr></thead><tbody>");

        foreach (var row in rows)
        {
            body.Append(Invariant, $"<tr data-family=\"{Escaped(row.Family)}\" data-state=\"{(row.LiveSince is null ? "provisional" : "live")}\" data-live-since=\"{(row.LiveSince is { } since ? DayOf(since) : "none")}\" data-variants=\"{row.Variants}\" data-listed=\"{row.ListedTonight}\" data-trades=\"{row.Trades}\" data-open=\"{row.Open}\" data-finished=\"{row.Finished}\">");
            body.Append(Invariant, $"<td class=\"setup\">{Escaped(row.Heading)}</td>");
            body.Append(row.LiveSince is { } live
                ? Formatted($"<td>live since {DayOf(live)}</td>")
                : "<td><span class=\"provisional\">provisional</span></td>");
            body.Append(Invariant, $"<td class=\"r num\">{row.Variants}</td><td class=\"r num\">{row.ListedTonight}</td>");
            body.Append(Invariant, $"<td>{Count(row.Trades, "trade")}, {row.Open} open and {row.Finished} finished</td>");
            body.Append(Invariant, $"<td class=\"family-record\">{Escaped(row.Record)}</td></tr>");
        }

        body.Append("</tbody></table></div>");

        return body.ToString();
    }

    // The registered rules of the new setups, one row a rule, each read over its own trades.
    public string FamilyRecords(IReadOnlyList<FamilyRecordRow> rows)
    {
        var body = new StringBuilder();

        body.Append(Invariant, $"<div class=\"tbl-wrap\"><table class=\"list-table family-records\" data-rows=\"{rows.Count}\"><thead><tr>");

        foreach (var (heading, says) in new[]
        {
            ("Setup", "The setup family the rule belongs to."),
            ("Rule", "The registered rule, its live one first, each a variant otherwise."),
            ("Trades", "The trades its own list kept, five a night at most with one open a stock; a sector heavyweights rule's, the holdings its own book kept."),
            ("Decided", "Its trades ended with a result and the benchmark of the same plan on every member that night; a heavyweights rule's, its holdings sold, with its size cut's return over the same sessions."),
            ("Edge", "The average of each decided trade's result less its benchmark, in multiples of its risk; a heavyweights rule's in percentage points of the buy, its result less its size cut's return."),
            ("Whole blocks", "Blocks of 63 sessions whose trades have all had their cap, against the look they wait for; a heavyweights rule's holding counted in the block of the session it ended on."),
            ("Level", "The level its looks are read at, the setup's own share of 0.05 over its own rules."),
        })
        {
            body.Append(TippedHeading(heading, says, heading is "Trades" or "Decided" or "Edge" or "Whole blocks" or "Level" ? "r" : null));
        }

        body.Append("</tr></thead><tbody>");

        foreach (var row in rows)
        {
            body.Append(Invariant, $"<tr data-family=\"{Escaped(row.Family)}\" data-rule=\"{Escaped(row.Rule)}\" data-live=\"{Flag(row.Live)}\" data-trades=\"{row.Trades}\" data-decided=\"{row.Decided}\" data-edge=\"{(row.Edge is { } edge ? edge.ToString("0.###", Invariant) : "none")}\" data-blocks=\"{row.Blocks}\" data-look=\"{row.NextLook}\" data-level=\"{row.Level.ToString("0.#####", Invariant)}\" data-from=\"{(row.From is { } from ? DayOf(from) : "none")}\" data-restarted=\"{Flag(row.Restarted is not null)}\">");
            body.Append(Invariant, $"<td class=\"setup\">{Escaped(row.Heading)}</td>");
            body.Append(Invariant, $"<td>{Escaped(row.Rule)}{(row.Live ? " <b>live</b>" : string.Empty)}");

            if (row.From is { } counted)
            {
                body.Append(Invariant, $"<span class=\"record-from\">its record counts from {DayOf(counted)}</span>");
            }

            if (row.Restarted is { } why)
            {
                body.Append(Invariant, $"<span class=\"record-restarted\">it restarted at its registration, since {Escaped(why)}</span>");
            }

            body.Append("</td>");
            body.Append(Invariant, $"<td class=\"r num\">{row.Trades}</td><td class=\"r num\">{row.Decided}</td>");
            body.Append(Invariant, $"<td class=\"r num\">{(row.Edge is not { } shown ? "none yet" : row.Family == EquityBrief.Core.Families.HeavyweightRule.Name ? Formatted($"{shown * 100:+0.0;-0.0;0.0} points") : shown.ToString("0.000", Invariant))}</td>");
            body.Append(Invariant, $"<td class=\"r num\">{row.Blocks} of {row.NextLook}</td>");
            body.Append(Invariant, $"<td class=\"r num\">{row.Level.ToString("0.####", Invariant)}</td></tr>");
        }

        body.Append("</tbody></table></div>");

        return body.ToString();
    }

    // A name's line on the page's list for its night.
    public string ListedUnder(ListedUnderView view)
    {
        var body = new StringBuilder();

        body.Append(Invariant, $"<p class=\"listed-under\" data-night=\"{DayOf(view.Night)}\" data-family=\"{Escaped(view.Family ?? "none")}\" data-place=\"{(view.Place is { } at ? at.ToString(Invariant) : "none")}\" data-also=\"{Escaped(string.Join(",", view.Also))}\">");

        if (view.Family is not null)
        {
            body.Append(Invariant, $"On the page's list for {DayOf(view.Night)} under <b>{Escaped(view.Heading ?? view.Family)}</b>, at place {view.Place}");
            body.Append(view.Also.Count > 0
                ? Formatted($"; it also qualified as {Escaped(string.Join(" and ", view.Also.Select(Article)))}.")
                : ".");
        }

        foreach (var held in view.HeldBack)
        {
            body.Append(Invariant, $"{(view.Family is not null || held != view.HeldBack[0] ? " " : string.Empty)}{Escaped(held)}");
        }

        body.Append("</p>");

        return body.ToString();
    }

    // The market line: the check's answer in words with both figures, then the night's counts, the open
    // trades linking to Past picks.
    public string MarketLine(MarketLineView line)
    {
        var check = line.Breadth is { } breadth && line.Floor is { } floor
            ? Formatted($"the {line.Index}'s breadth: {breadth * 100:0.0}% of its members closed above their 200-day average, {(line.Open ? "at or above" : "below")} its floor of {floor * 100:0.#}%")
            : $"the {line.Index}'s breadth is not available tonight";

        var body = new StringBuilder();

        body.Append(Invariant, $"<p class=\"market-line\" data-index=\"{Escaped(line.Index)}\" data-open=\"{Flag(line.Open)}\" data-breadth=\"{(line.Breadth is { } held ? held.ToString("R", Invariant) : "none")}\" data-floor=\"{(line.Floor is { } bar ? bar.ToString("R", Invariant) : "none")}\" ");
        body.Append(Invariant, $"data-passed=\"{(line.Passed is { } passed ? passed.ToString(Invariant) : "none")}\" data-members=\"{(line.Members is { } members ? members.ToString(Invariant) : "none")}\" ");
        body.Append(Invariant, $"data-buy-points=\"{line.BuyPoints}\" data-setups-listing=\"{line.SetupsListing}\" data-setups=\"{line.Setups}\" data-close=\"{(line.CloseRead ? line.Close.ToString(Invariant) : "none")}\" data-open-trades=\"{line.OpenTrades}\">");
        body.Append(Invariant, $"<b class=\"market-{(line.Open ? "open" : "closed")}\">{(line.Open ? "The lists are open" : "The lists are closed")}</b>: {Escaped(check)}. ");
        body.Append("<span class=\"market-counts\">");

        if (line.Passed is { } through && line.Members is { } of)
        {
            body.Append(Invariant, $"{through} of {of} {Escaped(line.Index)} members passed a setup · ");
        }

        body.Append(Invariant, $"{Count(line.BuyPoints, "buy point")} tonight across {line.SetupsListing} of {Count(line.Setups, "setup")} · ");
        body.Append(line.CloseRead ? Formatted($"{line.Close} close to a buy point · ") : string.Empty);
        body.Append(Invariant, $"<a href=\"#/picks?status={PickStatus.Open}{(line.Universe is { } chosen ? "&amp;universe=" + Uri.EscapeDataString(chosen) : string.Empty)}\">{Count(line.OpenTrades, "open trade")}</a></span></p>");

        return body.ToString();
    }

    // What each column of the shared list of stocks close to a buy point holds.
    public static IReadOnlyList<(string Heading, string Says)> CloseAcrossHeadings { get; } =
    [
        ("#", "The row's place in this list, the setups in the page's order."),
        ("Stock", "The ticker opens the stock's page, with the company beneath."),
        ("Setup", "The setup the stock is a single gate short of."),
        ("The gate it missed", "The one gate of that setup the stock did not pass tonight, in the gate's own words."),
    ];

    // The shared list of stocks close to a buy point: one row a stock and setup, each passing every gate of
    // that setup but one with nothing excluding it, the first of them drawn and the count of all stated.
    public string CloseAcross(IReadOnlyList<CloseToBuyCell> rows, int drawn)
    {
        var body = new StringBuilder();
        var shown = rows.Take(drawn).ToArray();

        body.Append(Invariant, $"<p class=\"list-count\" data-shown=\"{shown.Length}\" data-close=\"{rows.Count}\">Showing {shown.Length} of {rows.Count} close to a buy point</p>");

        if (shown.Length == 0)
        {
            body.Append("<p class=\"degraded\" data-close=\"none\">No stock is a single gate short of a buy point under any setup tonight.</p>");

            return body.ToString();
        }

        body.Append(Invariant, $"<div class=\"tbl-wrap\"><table class=\"list-table close-across\" data-rows=\"{shown.Length}\"><thead><tr>");

        foreach (var (heading, says) in CloseAcrossHeadings)
        {
            body.Append(TippedHeading(heading, says, heading == "#" ? "place" : null));
        }

        body.Append("</tr></thead><tbody>");

        foreach (var (row, at) in shown.Select((row, at) => (row, at)))
        {
            body.Append(Invariant, $"<tr data-ticker=\"{Escaped(row.Ticker)}\" data-family=\"{Escaped(row.Family)}\" data-gate=\"{Escaped(row.Gate)}\">");
            body.Append(Invariant, $"<td class=\"place\">{at + 1}</td>");
            body.Append(Invariant, $"<td class=\"c-nm\"><a class=\"name-link\" href=\"#/name/{Uri.EscapeDataString(row.Ticker)}\">{Escaped(row.Ticker)}</a>{(row.Company is { Length: > 0 } company ? Formatted($"<span class=\"co\">{Escaped(company)}</span>") : string.Empty)}</td>");
            body.Append(Invariant, $"<td class=\"setup\" data-setup=\"{Escaped(row.Family)}\">{Escaped(row.Setup)}</td>");
            body.Append(Invariant, $"<td class=\"missed-gate\"><b>{Escaped(row.Gate)}</b>: {Escaped(row.Words)}</td></tr>");
        }

        body.Append("</tbody></table></div>");

        return body.ToString();
    }

    // What each column of a family's card holds, in the order the columns are drawn.
    public static IReadOnlyList<(string Heading, string Says)> FamilyHeadings { get; } =
    [
        ("#", "The stock's place down the page, counted from one across every card."),
        ("Stock", "The ticker selects the row and draws its plan and levels beneath the cards. Beside it, report opens the stock's written report, or not written opens its page where no report is written yet, and the company is named beneath. A label names another setup the stock also qualified under tonight."),
        ("Business", "The state the company's reported quarters gave it on the night, with what its numbers say under the pointer. It removes no stock from a card."),
        ("Buy", "The price the plan buys at, which is the evening's close."),
        ("Stop", "The price a close beneath ends the trade at a loss, or the first level of a stop that trails the price."),
        ("Target", "The price a close at or above ends the trade at a gain. A setup that trails its stop names none."),
        ("Stop to target", "Where the buy sits between the stop, on the left, and the target, on the right. The nearer the mark is to the left, the less the plan risks against what it stands to gain."),
        ("Reward to risk", "How far the target sits above the buy against how far the stop sits below it, so 2.00 means twice as much to gain as to lose. It is a fact about the chart and not a chance of anything."),
        ("Why tonight", "What the stock did that made its setup list it on this evening, in the figures the night stored."),
        ("News, 30 days", NewsSays),
    ];

    // The one line a card draws under its standing while its family's recorded sweep found no setting that passed the
    // floors, and nothing otherwise; the card still lists its picks.
    // see: No family on any index is set aside or hidden by a test result without the operator's word
    internal static string SweepFoundNoneLine(bool drawn) =>
        drawn ? $"<p class=\"family-sweep\" data-sweep=\"none passed\">{Escaped(EquityBrief.Core.Sweep.SweepAnswer.Line)}</p>" : string.Empty;

    // One family's card: its state and counts in a line, its picks as rows numbered by their place down
    // the page, or why it lists nothing, and the notes beneath.
    // see: Tonight's page is drawn from setup families, each a rule of its own listing at most five a night
    public string FamilyCard(FamilyCardView card)
    {
        var body = new StringBuilder();
        var provisional = card.LiveSince is null;
        var variant = card.RuleChoice is { Chosen.Live: false } chosen ? chosen : null;

        body.Append(Invariant, $"<section class=\"family-card\" data-family=\"{Escaped(card.Family)}\" data-place=\"{card.Place}\" data-of=\"{card.Of}\" data-picks=\"{card.Picks.Count}\" ");
        body.Append(Invariant, $"data-state=\"{(provisional ? "provisional" : "live")}\" data-live-since=\"{(card.LiveSince is { } since ? DayOf(since) : "none")}\" data-variants=\"{card.Variants}\" ");
        body.Append(Invariant, $"data-rule=\"{Escaped(card.RuleChoice?.Chosen.Slug ?? RuleScreenWords.LiveSlug)}\" data-variant=\"{(variant is null ? "none" : variant.Chosen.Variant!.Value.ToString(Invariant))}\">");

        // The rule's standing, the night's count and the variants scored beside it, in one line.
        body.Append("<p class=\"family-state\">");
        body.Append(card.LiveSince is { } live
            ? Formatted($"Live rule since <b>{DayOf(live)}</b>")
            : $"<b class=\"provisional\">{Escaped(EquityBrief.Core.Families.SetupFamilies.ProvisionalStatus)}</b>");
        body.Append(Invariant, $" · {Count(card.Picks.Count, "pick")} tonight · {Count(card.Variants, "variant")} scoring in the background</p>");
        body.Append(SweepFoundNoneLine(card.SweepFoundNone));

        // The selector, the band a variant is drawn under, and the rule's words with the clauses the live rule's lack
        // marked, for the rule the link chose.
        // see: A variant's picks are shown on its card when chosen and its results only under its tests
        if (card.RuleChoice is { } rule)
        {
            body.Append(RuleChoice(card.Family, rule));

            if (variant is not null)
            {
                body.Append(Invariant, $"<p class=\"variant-band\" role=\"status\" data-variant=\"{variant.Chosen.Variant}\">{Escaped(RuleScreenWords.Band(variant.Chosen.Variant!.Value))}</p>");
                body.Append(RuleWordsLine(rule.Parts));
                body.Append(VariantPicks(variant.Picks ?? []));
                body.Append(RuleFunnel(rule));
                body.Append(StretchLine(rule.Stretch, rule.Evaluated));
                body.Append(FormingList(rule.Forming));

                foreach (var note in card.Notes)
                {
                    body.Append(Invariant, $"<p class=\"family-note\">{Escaped(note)}</p>");
                }

                body.Append("</section>");

                return body.ToString();
            }
        }

        if (card.Picks.Count == 0)
        {
            body.Append(Invariant, $"<p class=\"degraded family-empty\" data-picks=\"0\">{Escaped(card.Empty ?? "No stock qualified under this setup tonight.")}</p>");
        }
        else
        {
            body.Append(Invariant, $"<div class=\"tbl-wrap\"><table class=\"list-table family-table\" data-rows=\"{card.Picks.Count}\"><thead><tr>");

            foreach (var (heading, says) in FamilyHeadings)
            {
                body.Append(TippedHeading(heading, says, heading switch
                {
                    "#" => "place",
                    "Buy" or "Stop" or "Target" or "Reward to risk" => "r",
                    _ => null,
                }));
            }

            body.Append("</tr></thead><tbody>");

            foreach (var pick in card.Picks)
            {
                body.Append(FamilyRow(card.Family, pick));

                // The pick's card in place beneath its row, opened by the control in its name cell.
                if (pick.Card is { } decision)
                {
                    body.Append(CardRow(decision, FamilyHeadings.Count));
                }
            }

            body.Append("</tbody></table></div>");
        }

        foreach (var note in card.Notes)
        {
            body.Append(Invariant, $"<p class=\"family-note\">{Escaped(note)}</p>");
        }

        // The live rule's funnel, stretch line and forming list, beneath its picks and notes.
        if (card.RuleChoice is { } drawn)
        {
            body.Append(RuleFunnel(drawn));
            body.Append(StretchLine(drawn.Stretch, drawn.Evaluated));
            body.Append(FormingList(drawn.Forming));
        }

        body.Append("</section>");

        return body.ToString();
    }

    // The card's selector: the live rule first, then each variant by its number and the register's words, the choice
    // kept in the link under the family's own key; and on an index no freeze has registered a rule on, the line saying
    // so beside the one choice.
    static string RuleChoice(string family, RuleView rule)
    {
        var body = new StringBuilder();

        body.Append(Invariant, $"<p class=\"rule-choice\"><label>Rule <select data-rule-choice=\"{Escaped(family)}\" aria-label=\"The rule this card is drawn by\">");

        foreach (var choice in rule.Choices)
        {
            var label = choice.Live ? choice.Rule : FormattableString.Invariant($"Variant {choice.Variant}: {choice.Rule}");

            body.Append(Invariant, $"<option value=\"{Escaped(choice.Live ? RuleScreenWords.LiveSlug : choice.Slug)}\"{(choice.Slug == rule.Chosen.Slug ? " selected" : string.Empty)} data-variant=\"{(choice.Variant is { } number ? number.ToString(Invariant) : "none")}\">{Escaped(label)}</option>");
        }

        body.Append("</select></label>");

        if (rule.NoVariantLine is { } line)
        {
            body.Append(Invariant, $" <span class=\"no-variant\" data-no-variant=\"true\">{Escaped(line)}</span>");
        }

        body.Append("</p>");

        return body.ToString();
    }

    // The chosen rule's words in clauses, each the live rule's words lack marked.
    static string RuleWordsLine(IReadOnlyList<RulePartView> parts) =>
        "<p class=\"rule-words\">" + string.Join(", ", parts.Select(part => part.Differs ? $"<mark class=\"differs\">{Escaped(part.Text)}</mark>" : Escaped(part.Text))) + ".</p>";

    // A variant's own picks, drawn under the band: each with its plan and the figures that listed it.
    static string VariantPicks(IReadOnlyList<VariantPickView> picks)
    {
        if (picks.Count == 0)
        {
            return "<p class=\"degraded variant-empty\" data-picks=\"0\">This variant lists nothing tonight.</p>";
        }

        var body = new StringBuilder();

        body.Append(Invariant, $"<div class=\"tbl-wrap\"><table class=\"list-table variant-table\" data-rows=\"{picks.Count}\"><thead><tr>");
        body.Append("<th scope=\"col\">#</th><th scope=\"col\">Stock</th><th scope=\"col\" class=\"r\">Buy</th><th scope=\"col\" class=\"r\">Stop</th><th scope=\"col\" class=\"r\">Target</th><th scope=\"col\" class=\"r\">Reward to risk</th><th scope=\"col\">Why</th>");
        body.Append("</tr></thead><tbody>");

        foreach (var pick in picks)
        {
            body.Append(Invariant, $"<tr data-ticker=\"{Escaped(pick.Ticker)}\" data-variant-pick=\"true\"><td class=\"place\">{pick.Place}</td>");
            body.Append(Invariant, $"<td class=\"c-nm\"><a class=\"name-link\" href=\"#/name/{Uri.EscapeDataString(pick.Ticker)}\">{Escaped(pick.Ticker)}</a></td>");
            body.Append(Invariant, $"<td class=\"r\" data-entry=\"{pick.Entry.ToString(Invariant)}\">{Price(pick.Entry)}</td>");
            body.Append(Invariant, $"<td class=\"r\" data-stop=\"{pick.Stop.ToString(Invariant)}\">{Price(pick.Stop)}</td>");
            body.Append(Invariant, $"<td class=\"r\">{(pick.Target is { } target ? Price(target) : "trails")}</td>");
            body.Append(Invariant, $"<td class=\"r\">{(pick.RewardToRisk is { } reward ? reward.ToString("0.0", Invariant) : "none")}</td>");
            body.Append(Invariant, $"<td class=\"why\">{Escaped(WhyWords(pick.Why))}</td></tr>");
        }

        body.Append("</tbody></table></div>");

        return body.ToString();
    }

    // The figures behind a pick as stored, each named, in one line.
    static string WhyWords(string why)
    {
        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(why);

            return string.Join("; ", document.RootElement.EnumerateObject().Select(value => $"{value.Name} {value.Value.GetString()}"));
        }
        catch (System.Text.Json.JsonException)
        {
            return why;
        }
    }

    // The rule's funnel: each gate with how many members passed it and every gate before, in the rule's order.
    static string RuleFunnel(RuleView rule)
    {
        if (rule.Funnel is not { Count: > 0 } funnel)
        {
            return rule.Evaluated ? string.Empty : "<p class=\"degraded rule-unevaluated\" data-evaluated=\"false\">The night did not evaluate this rule.</p>";
        }

        var body = new StringBuilder("<ol class=\"rule-funnel\" data-gates=\"" + funnel.Count.ToString(Invariant) + "\">");

        foreach (var (gate, passed) in funnel)
        {
            body.Append(Invariant, $"<li data-gate=\"{Escaped(gate)}\" data-passed=\"{passed}\">{Escaped(gate)}: <b>{passed}</b></li>");
        }

        body.Append("</ol>");

        return body.ToString();
    }

    // The stretch line: how many nights the rule has listed nothing for and the mark its past empty nights set, flagged
    // past the mark; no mark under the floors, with how far the floors are from being met.
    // see: A card's stretch line counts its mark over past empty nights and draws none under thirty completed stretches
    static string StretchLine(StretchLineView? stretch, bool evaluated)
    {
        if (stretch is null)
        {
            return string.Empty;
        }

        var words = stretch.Stretch == 0
            ? "Listed tonight, so its empty stretch is nothing"
            : FormattableString.Invariant($"No pick for {stretch.Stretch} night{(stretch.Stretch == 1 ? string.Empty : "s")}");
        var mark = stretch.Mark is { } level
            ? FormattableString.Invariant($"; on {RuleScreenWords.SharePercent}% of past empty nights the stretch was {level} or shorter")
            : FormattableString.Invariant($"; no mark yet: {stretch.Completed} of {EquityBrief.Core.Cards.RuleStretch.CompletedStretchesFloor} stretches completed over {stretch.Sessions} of {EquityBrief.Core.Cards.RuleStretch.SessionsFloor} sessions");

        return FormattableString.Invariant($"<p class=\"stretch-line{(stretch.Flagged ? " flagged" : string.Empty)}\" data-stretch=\"{stretch.Stretch}\" data-mark=\"{(stretch.Mark is { } held ? held.ToString(Invariant) : "none")}\" data-flagged=\"{Flag(stretch.Flagged)}\" data-completed=\"{stretch.Completed}\" data-sessions=\"{stretch.Sessions}\">")
            + Escaped(words + mark + (stretch.Flagged ? ". Past its mark." : "."))
            + "</p>";
    }

    // The breakouts forming under a breakout rule: the rows drawn of the count forming, nearest misses first, each with
    // the price it would have to close above, the volume the rule would need against tonight's, its ranges' ratio, the
    // gates it still fails and its next earnings date where one falls within the stated sessions; a line where the
    // market check closed the rule's list; and the closing line saying what the rule would read that night.
    // see: The forming list advises and never lists a stock
    static string FormingList(FormingListView? forming)
    {
        if (forming is null)
        {
            return string.Empty;
        }

        var body = new StringBuilder();

        body.Append(Invariant, $"<section class=\"forming\" data-forming=\"{forming.Forming}\" data-drawn=\"{forming.Rows.Count}\" data-market-open=\"{Flag(forming.MarketOpen)}\">");
        body.Append(Invariant, $"<h4>Breakouts forming: {forming.Forming}{(forming.Rows.Count < forming.Forming ? FormattableString.Invariant($", {forming.Rows.Count} drawn") : string.Empty)}</h4>");

        if (!forming.MarketOpen)
        {
            body.Append("<p class=\"degraded forming-closed\" data-market-open=\"false\">The market check closed this rule's list tonight, so it would list none of these whatever they did.</p>");
        }

        if (forming.Rows.Count == 0)
        {
            body.Append("<p class=\"forming-empty\" data-rows=\"0\">No member is forming a breakout under this rule tonight.</p>");
        }
        else
        {
            body.Append(Invariant, $"<div class=\"tbl-wrap\"><table class=\"list-table forming-table\" data-rows=\"{forming.Rows.Count}\"><thead><tr>");
            body.Append("<th scope=\"col\">#</th><th scope=\"col\">Stock</th><th scope=\"col\" class=\"r\">Close</th><th scope=\"col\" class=\"r\">Close above</th><th scope=\"col\" class=\"r\">Moves under</th><th scope=\"col\" class=\"r\">Volume needed</th><th scope=\"col\" class=\"r\">Volume tonight</th><th scope=\"col\" class=\"r\">Range ratio</th><th scope=\"col\">Still fails</th><th scope=\"col\">Next report</th>");
            body.Append("</tr></thead><tbody>");

            foreach (var row in forming.Rows)
            {
                body.Append(Invariant, $"<tr data-ticker=\"{Escaped(row.Ticker)}\" data-forming-row=\"true\" data-high=\"{row.High.ToString(Invariant)}\" data-volume-needed=\"{row.VolumeNeeded.ToString("0", Invariant)}\"><td class=\"place\">{row.Place}</td>");
                body.Append(Invariant, $"<td class=\"c-nm\"><a class=\"name-link\" href=\"#/name/{Uri.EscapeDataString(row.Ticker)}\">{Escaped(row.Ticker)}</a></td>");
                body.Append(Invariant, $"<td class=\"r\">{Price(row.Close)}</td><td class=\"r\">{Price(row.High)}</td>");
                body.Append(Invariant, $"<td class=\"r\">{row.MovesUnder.ToString("0.00", Invariant)}</td>");
                body.Append(Invariant, $"<td class=\"r\">{Shares(row.VolumeNeeded)}</td><td class=\"r\">{Shares(row.Volume)}</td>");
                body.Append(Invariant, $"<td class=\"r\">{row.RangeRatio.ToString("0.00", Invariant)}</td>");
                body.Append(Invariant, $"<td>{Escaped(string.Join(", ", row.Missing))}</td>");
                body.Append(Invariant, $"<td>{(row.NextEarnings is { } next ? DayOf(next) : "none within the window")}</td></tr>");
            }

            body.Append("</tbody></table></div>");
        }

        body.Append(Invariant, $"<p class=\"forming-key\">{Escaped(RuleScreenWords.FormingClosingLine)}</p>");
        body.Append("</section>");

        return body.ToString();
    }

    static string Shares(double volume) => volume >= 1_000_000 ? (volume / 1_000_000).ToString("0.0", Invariant) + "M" : volume >= 1_000 ? (volume / 1_000).ToString("0", Invariant) + "K" : volume.ToString("0", Invariant);

    static string Count(int count, string noun) => Formatted($"{count} {noun}{(count == 1 ? string.Empty : "s")}");

    // One pick as a row of its family's card. The row selects as a row of the list does, and its name cell
    // carries what a list row's does: the report it holds or the control asking for one, what the queue
    // holds for it, the company and a suspect series.
    string FamilyRow(string family, FamilyPickCell pick)
    {
        var row = pick.Row;
        var cells = new StringBuilder();

        cells.Append(Invariant, $"<tr data-ticker=\"{Escaped(row.Ticker)}\" data-family=\"{Escaped(family)}\" data-also=\"{Escaped(string.Join(",", pick.Also))}\" ");
        cells.Append(Invariant, $"data-selects=\"{Escaped(row.Ticker)}\" data-select-href=\"#/night/{row.SessionDate:yyyy-MM-dd}?name={Uri.EscapeDataString(row.Ticker)}\">");
        cells.Append(Invariant, $"<td class=\"place\" data-place=\"{pick.Place}\">{pick.Place}</td>");

        cells.Append(Invariant, $"<td class=\"c-nm\"><a class=\"name-link\" href=\"#/name/{Uri.EscapeDataString(row.Ticker)}\">{Escaped(row.Ticker)}</a>");

        foreach (var also in pick.Also)
        {
            cells.Append(Invariant, $" <span class=\"also-family\" data-also=\"{Escaped(also)}\">also {Escaped(Article(also))}</span>");
        }

        if (pick.Card is { } decision)
        {
            cells.Append(CardToggle(decision));
        }

        var researched = row.ResearchedOn is not null;

        cells.Append(Invariant, $" <a class=\"open{(researched ? string.Empty : " unwritten")}\" href=\"#/name/{Uri.EscapeDataString(row.Ticker)}\" ");
        cells.Append(Invariant, $"data-report-state=\"{ReportState(row)}\" data-researched=\"{(researched ? "true" : "false")}\"");

        if (row.ResearchedOn is { } on)
        {
            cells.Append(Invariant, $" data-researched-on=\"{on:yyyy-MM-dd}\" title=\"open the report, written {on:yyyy-MM-dd}\">report</a>");
        }
        else
        {
            cells.Append(" title=\"open the name, whose researched sections are not written\">not written</a>");

            if (row.Queue is null)
            {
                cells.Append(AskForAReport(row.Ticker));
            }
        }

        if (row.Queue is { } queued)
        {
            cells.Append(Invariant, $"<span class=\"report-state\" data-report-state=\"{Escaped(queued.State)}\" data-at=\"{Escaped(queued.At ?? string.Empty)}\">{Escaped(queued.Words)}</span>");
        }

        if (row.Distance?.Name is { Length: > 0 } company)
        {
            cells.Append(Invariant, $"<span class=\"co\">{Escaped(company)}</span>");
        }

        cells.Append(row.Suspect is { } suspect
            ? $" <span class=\"prices-suspect\" data-last-asked-at=\"{Escaped(suspect.LastAskedAt)}\" title=\"last tried {Escaped(suspect.LastAskedAt)}, because {Escaped(suspect.Reason)}\">prices may not reflect a dividend or split</span></td>"
            : "</td>");

        cells.Append(row.Business is null
            ? "<td class=\"business-cell\"><span class=\"degraded\" data-state=\"none\">not read</span></td>"
            : $"<td class=\"business-cell\">{BusinessWord(row.Business)}</td>");

        cells.Append(PriceCell("buy", pick.Buy, null));
        cells.Append(PriceCell("stop", pick.Stop, null));
        cells.Append(PriceCell("target", pick.Target, pick.Trailing ? "trailing" : null));
        cells.Append(Invariant, $"<td class=\"stop-to-target\">{StopToTarget(pick)}</td>");

        cells.Append(pick.RewardToRisk is { } ratio
            ? Formatted($"<td class=\"r num\" data-reward-to-risk=\"{ratio.ToString(Invariant)}\">{Figures.Ratio(ratio)}</td>")
            : $"<td class=\"r num\" data-reward-to-risk=\"none\">{(pick.Trailing ? "open" : pick.Stop is not null && pick.Target is not null ? "<span class=\"degraded\">not stored</span>" : "<span class=\"degraded\">none</span>")}</td>");

        cells.Append(Invariant, $"<td class=\"why-tonight\">{Escaped(pick.Why)}</td>");

        cells.Append(row.NewsPositive is { } positive && row.NewsNegative is { } negative
            ? Formatted($"<td class=\"news-counts\" data-positive=\"{positive}\" data-negative=\"{negative}\"><span class=\"np\">+{positive}</span> <span class=\"nn\">-{negative}</span></td>")
            : "<td class=\"news-counts\" data-positive=\"none\" data-negative=\"none\"><span class=\"degraded\">no label</span></td>");

        cells.Append("</tr>");

        return cells.ToString();
    }

    // A label's family with its article, as "also a sector leader" reads.
    static string Article(string label) =>
        label.Length > 0 && "aeiou".Contains(char.ToLowerInvariant(label[0]), StringComparison.Ordinal) ? "an " + label.ToLowerInvariant() : "a " + label.ToLowerInvariant();

    static string PriceCell(string kind, decimal? price, string? instead) =>
        price is { } stated
            ? Formatted($"<td class=\"r num plan-{kind}\" data-{kind}=\"{stated.ToString(Invariant)}\">{Figures.Price(stated)}</td>")
            : $"<td class=\"r num plan-{kind}\" data-{kind}=\"none\">{(instead is null ? "<span class=\"degraded\">none</span>" : Escaped(instead))}</td>";

    // Where the buy sits between the stop and the target, as a mark on a bar whose left end is the stop and
    // whose right end is the target. A plan that trails its stop has no right end, and says so.
    static string StopToTarget(FamilyPickCell pick)
    {
        if (pick.Buy is not { } buy || pick.Stop is not { } stop)
        {
            return "<span class=\"degraded\" data-along=\"none\">no plan stored</span>";
        }

        if (pick.Target is not { } target || target <= stop)
        {
            return "<span class=\"stt trailing\" data-along=\"none\" title=\"the stop trails the price, and the plan names no target\"><span class=\"stt-stop\"></span><span class=\"stt-open\">no fixed target</span></span>";
        }

        var along = Math.Clamp((PlotValue(buy) - PlotValue(stop)) / (PlotValue(target) - PlotValue(stop)), 0, 1);

        return Formatted($"<span class=\"stt\" data-along=\"{along:0.###}\" title=\"the buy sits {along * 100:0}% of the way from the stop to the target\"><span class=\"stt-mark\" style=\"left:{along * 100:0.#}%\"></span></span>");
    }
}
