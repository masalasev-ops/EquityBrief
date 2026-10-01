using System.Text;

namespace EquityBrief.Web.Marks;

// One stock a family's card lists: the night's row for it as every list draws a name, its place down the
// page, the trade its family's plan states, with no target where the plan trails its stop, the reward to risk as the card
// prints it, why the family lists it tonight in that family's words, and the labels of the other families
// it qualified under.
// see: A stock holds one trade across every family, and one qualifying under two is listed once under the first in the page's order
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
// see: A family runs on provisional settings until its freeze, and nothing before the freeze counts toward a checkpoint
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

public sealed partial class MarkRenderer
{
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
