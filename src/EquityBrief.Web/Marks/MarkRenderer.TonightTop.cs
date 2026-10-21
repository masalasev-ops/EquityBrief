using System.Text;

namespace EquityBrief.Web.Marks;

// One setup's tile at the top of tonight's page: the family's word and label, tonight's count, what that count is of,
// and its state in words, in the family's tint and edge.
public sealed record SetupTileView(string Family, string Label, int Count, string Counted, string State);

// One of tonight's buy points as the table across every setup draws it: the setup's word and label and its pick.
public sealed record BuyPointView(string Family, string Label, FamilyPickCell Pick);

// Tonight's top: a tile a setup and one table of tonight's buy points across every setup, each row tagged with its
// setup and folding beneath it the stock's plan and its pick's card.
// see: Each buy point's row folds its plan and its card beneath it, closed until a press on the row or a link naming it opens it
// see: Each setup family has a hue of its own drawn as a mark and a tint beside its word
public sealed partial class MarkRenderer
{
    // What each column of the buy points table holds, in the order the columns are drawn.
    public static IReadOnlyList<(string Heading, string Says)> BuyPointHeadings { get; } =
    [
        ("Setup", "The setup that lists the stock tonight, in its hue."),
        ("#", "The row's place in the table, counted from one across every setup, each setup's picks in its own order."),
        ("Stock", "The ticker opens the stock's chart page. Beside it, plan opens the stock's plan and its pick's card in a row beneath it and folds them again, as a press anywhere else on the row does; report opens the stock's written report or not written its page, and the company is named beneath. A label names another setup the stock also qualified under tonight."),
        ("Business", "The state the company's reported quarters gave it on the night, with what its numbers say under the pointer. It removes no stock from the table."),
        ("Buy", "The evening's close the plan is set at and sized from. The pick buys at the next open, the open of the session after tonight."),
        ("Stop", "The price a close beneath ends the trade at a loss, or the first level of a stop that trails the price."),
        ("Target", "The price a close at or above ends the trade at a gain. A setup that trails its stop names none."),
        ("Reward to risk", "How far the target sits above the buy against how far the stop sits below it, so 2.00 means twice as much to gain as to lose. It is a fact about the chart and not a chance of anything."),
        ("News, 30 days", NewsSays),
    ];

    // A tile a setup, each in its family's tint with its edge in its hue, its word beside both, opening its card.
    public string SetupTiles(IReadOnlyList<SetupTileView> tiles)
    {
        var body = new StringBuilder();

        body.Append(Invariant, $"<nav class=\"setup-tiles\" aria-label=\"Tonight's setups\" data-tiles=\"{tiles.Count}\">");

        foreach (var tile in tiles)
        {
            body.Append(Invariant, $"<button type=\"button\" class=\"setup-tile fam-edge\" data-family=\"{Escaped(tile.Family)}\" data-card=\"{Escaped(tile.Family)}\" data-count=\"{tile.Count}\" data-state=\"{Escaped(tile.State)}\">");
            body.Append(Invariant, $"<span class=\"fam-tag\" data-family=\"{Escaped(tile.Family)}\"><span class=\"fam-swatch\" data-family=\"{Escaped(tile.Family)}\"></span>{Escaped(tile.Label)}</span>");
            body.Append(Invariant, $"<b class=\"tile-count\">{tile.Count}</b><span class=\"tile-counted\">{Escaped(tile.Counted)}</span><span class=\"tile-state\">{Escaped(tile.State)}</span></button>");
        }

        body.Append("</nav>");

        return body.ToString();
    }

    // Tonight's buy points across every setup, in the one family order and each setup's own order, each row tagged with
    // its setup and followed by its fold: the stock's plan as the page hands it and, where the night wrote one, its pick's
    // card, hidden but for the row the link names and opened and closed in place by the control in its name cell or a press
    // anywhere else on the row, any number open at once. The summary across the setups, whose cards draw their own picks as
    // rows from the same cells.
    // see: A setup's card draws its live picks as rows of its own, each ticker opening its chart page
    public string BuyPoints(IReadOnlyList<BuyPointView> points, string? open, IReadOnlyDictionary<string, string>? plans = null)
    {
        if (points.Count == 0)
        {
            return "<p class=\"degraded buy-points-none\" data-buy-points=\"0\">No setup lists a buy point tonight.</p>";
        }

        var body = new StringBuilder();

        body.Append(Invariant, $"<div class=\"tbl-wrap opens-rows\"><table class=\"list-table buy-points\" data-rows=\"{points.Count}\"><thead><tr>");

        foreach (var (heading, says) in BuyPointHeadings)
        {
            body.Append(TippedHeading(heading, says, heading switch
            {
                "#" => "place",
                "Buy" or "Stop" or "Target" or "Reward to risk" => "r",
                _ => null,
            }));
        }

        body.Append("</tr></thead><tbody>");

        foreach (var (point, number) in points.Select((point, at) => (point, at + 1)))
        {
            var pick = point.Pick;
            var row = pick.Row;
            var plan = plans?.GetValueOrDefault(row.Ticker) ?? string.Empty;
            var folds = plan.Length > 0 || pick.Card is not null;
            var fold = FoldId(point.Family, row.Ticker);
            var opened = folds && string.Equals(row.Ticker, open, StringComparison.Ordinal);

            body.Append(Invariant, $"<tr data-ticker=\"{Escaped(row.Ticker)}\" data-family=\"{Escaped(point.Family)}\"{(folds ? Formatted($" data-fold=\"{Escaped(fold)}\"") : string.Empty)}{(opened ? " class=\"sel\"" : string.Empty)}>");
            body.Append(Invariant, $"<td class=\"setup\"><span class=\"fam-tag\" data-family=\"{Escaped(point.Family)}\"><span class=\"fam-swatch\" data-family=\"{Escaped(point.Family)}\"></span>{Escaped(point.Label)}</span></td>");
            body.Append(Invariant, $"<td class=\"place\" data-place=\"{number}\">{number}</td>");
            body.Append(PickNameCell(pick, folds ? FoldToggle(fold, opened) : string.Empty));
            body.Append(PickBusinessCell(pick));
            body.Append(PriceCell("buy", pick.Buy, null));
            body.Append(PriceCell("stop", pick.Stop, null));
            body.Append(PriceCell("target", pick.Target, pick.Trailing ? "trailing" : null));
            body.Append(RewardCell(pick));
            body.Append(PickNewsCell(pick));
            body.Append("</tr>");

            if (folds)
            {
                body.Append(Invariant, $"<tr class=\"fold-row\" id=\"{Escaped(fold)}\" data-fold-for=\"{Escaped(row.Ticker)}\"{(opened ? string.Empty : " hidden")}><td colspan=\"{BuyPointHeadings.Count}\"><div class=\"row-open\">{plan}{(pick.Card is { } card ? DecisionCard(card) : string.Empty)}</div></td></tr>");
            }
        }

        body.Append("</tbody></table></div>");

        // From 19.10 each pick is bought at the next session's open, its plan standing where tonight's close set it.
        // see: A trade's entry is priced at the next session's open by a fill step of its own, and no rule's version moves because of it
        body.Append(Invariant, $"<p class=\"buys-next\">Each pick {Escaped(EquityBrief.Core.Ledger.TradeFills.Waiting)}, the open of the session after this night, with its stop and target where this night's close set them.</p>");

        return body.ToString();
    }

    // The id a buy point's fold and the control opening it share, the setup in it so a stock under two setups folds twice.
    public static string FoldId(string family, string ticker) =>
        $"fold-{family}-{ticker}".ToLowerInvariant().Replace('.', '-').Replace(' ', '-');

    // The control in a buy point's name cell opening and closing its fold, read by the page's script as a card's control is.
    static string FoldToggle(string fold, bool opened) =>
        Formatted($" <button type=\"button\" class=\"card-toggle fold-toggle\" aria-expanded=\"{(opened ? "true" : "false")}\" aria-controls=\"{Escaped(fold)}\" title=\"open this stock's plan and its pick's card\">plan</button>");

    // A pick's reward to risk as its plan states it, open where the plan trails its stop.
    static string RewardCell(FamilyPickCell pick) =>
        pick.RewardToRisk is { } ratio
            ? Formatted($"<td class=\"r num\" data-reward-to-risk=\"{ratio.ToString(Invariant)}\">{Figures.Ratio(ratio)}</td>")
            : $"<td class=\"r num\" data-reward-to-risk=\"none\">{(pick.Trailing ? "open" : pick.Stop is not null && pick.Target is not null ? "<span class=\"degraded\">not stored</span>" : "<span class=\"degraded\">none</span>")}</td>";
}
