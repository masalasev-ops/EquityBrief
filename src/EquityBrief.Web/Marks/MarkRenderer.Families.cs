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
    IReadOnlyList<string> Also);

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
    string? Empty);

// The line tonight's page opens on, on a night the families drew its list: whether the market check left
// the lists open, the breadth it read against its floor, the buy points the page lists and how many of the
// setups list one, the stocks a single gate short of one, and the trades still open.
// see: The market check closes every swing family's list together, and the sector heavyweights read none
public sealed record MarketLineView(bool Open, double? Breadth, double? Floor, int BuyPoints, int SetupsListing, int Setups, int Close, int OpenTrades);

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

public sealed partial class MarkRenderer
{
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
            ? Formatted($"{breadth * 100:0.0}% of the members closed above their 200-day average, {(line.Open ? "at or above" : "below")} its floor of {floor * 100:0.#}%")
            : "the night's breadth is not available";

        var body = new StringBuilder();

        body.Append(Invariant, $"<p class=\"market-line\" data-open=\"{Flag(line.Open)}\" data-breadth=\"{(line.Breadth is { } held ? held.ToString("R", Invariant) : "none")}\" data-floor=\"{(line.Floor is { } bar ? bar.ToString("R", Invariant) : "none")}\" ");
        body.Append(Invariant, $"data-buy-points=\"{line.BuyPoints}\" data-setups-listing=\"{line.SetupsListing}\" data-setups=\"{line.Setups}\" data-close=\"{line.Close}\" data-open-trades=\"{line.OpenTrades}\">");
        body.Append(Invariant, $"<b class=\"market-{(line.Open ? "open" : "closed")}\">{(line.Open ? "The lists are open" : "The lists are closed")}</b>: {Escaped(check)}. ");
        body.Append(Invariant, $"<span class=\"market-counts\">{Count(line.BuyPoints, "buy point")} tonight across {line.SetupsListing} of {Count(line.Setups, "setup")} · {line.Close} close to a buy point · ");
        body.Append(Invariant, $"<a href=\"#/picks?status={PickStatus.Open}\">{Count(line.OpenTrades, "open trade")}</a></span></p>");

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

    // One family's card: its state and counts in a line, its picks as rows numbered by their place down
    // the page, or why it lists nothing, and the notes beneath.
    // see: Tonight's page is drawn from setup families, each a rule of its own listing at most five a night
    public string FamilyCard(FamilyCardView card)
    {
        var body = new StringBuilder();
        var provisional = card.LiveSince is null;

        body.Append(Invariant, $"<section class=\"family-card\" data-family=\"{Escaped(card.Family)}\" data-place=\"{card.Place}\" data-of=\"{card.Of}\" data-picks=\"{card.Picks.Count}\" ");
        body.Append(Invariant, $"data-state=\"{(provisional ? "provisional" : "live")}\" data-live-since=\"{(card.LiveSince is { } since ? DayOf(since) : "none")}\" data-variants=\"{card.Variants}\">");

        // The rule's standing, the night's count and the variants scored beside it, in one line.
        body.Append("<p class=\"family-state\">");
        body.Append(card.LiveSince is { } live
            ? Formatted($"Live rule since <b>{DayOf(live)}</b>")
            : $"<b class=\"provisional\">{Escaped(EquityBrief.Core.Families.SetupFamilies.Provisional)}</b>");
        body.Append(Invariant, $" · {Count(card.Picks.Count, "pick")} tonight · {Count(card.Variants, "variant")} scoring in the background</p>");

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
            }

            body.Append("</tbody></table></div>");
        }

        foreach (var note in card.Notes)
        {
            body.Append(Invariant, $"<p class=\"family-note\">{Escaped(note)}</p>");
        }

        body.Append("</section>");

        return body.ToString();
    }

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
            : $"<td class=\"r num\" data-reward-to-risk=\"none\">{(pick.Trailing ? "open" : "<span class=\"degraded\">none</span>")}</td>");

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
