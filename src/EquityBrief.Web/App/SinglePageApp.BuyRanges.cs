using System.Globalization;
using System.Text;
using EquityBrief.Core.Loop;

namespace EquityBrief.Web.App;

// The buy range's newest widths test: the day it ran and its rows on the index.
public sealed record BuyRangeTableView(string Day, IReadOnlyList<RangeWidthRow> Rows)
{
    // Whether the run walked the AI stock picker's menu over the price families' setups, its width provisional.
    public bool Provisional { get; init; }
}

// The buy range's widths on the Improvement loop page for the index it reads: when the test last ran and the width it chose,
// every width beside the buy at the open over 2005 to 2018 and from 2019, what each missed and kept out of, each setup on its
// own folded beneath, and how a card's plan reads at the chosen width. Before the test has run it says when it runs.
// see: Every buy at the next open carries a range from its last close, its widths chosen by test on the early years for each index
public sealed partial class SinglePageApp
{
    public const string BuyRangeLede = "Every plan buying at the next open will carry a range from its last close: a ceiling some typical daily moves above it and a floor some under it, or none. An open above the ceiling waits up to 2 days for the price to come back to it; an open under the floor buys nothing. Each width is walked over the index's setups from 2005, chosen on 2005 to 2018 and checked from 2019. No plan uses a width before you approve one.";

    public const string BuyRangeProvisional = "Provisional: walked as the AI stock picker buys, under each plan of its menu, over every stock and session the four price families flagged, since the picker has no history of its own. It runs again over every member on every session once that table is rebuilt, and that width replaces this one from the next night.";

    // The close and typical daily move a card's plan is shown at.
    public const decimal ExampleClose = 100m;

    public const decimal ExampleMove = 2m;

    public string BuyRangeRegion(UniverseChoice chosen, BuyRangeTableView? table)
    {
        if (table is null || !table.Rows.Any(row => row.Index == chosen.Code))
        {
            return Cards.Computed("Buy ranges", "<p class=\"empty\" data-ranges=\"none\" data-summary>The widths test has not run on this index yet. It runs by hand over the long ledger once the ledger has been rebuilt through the fixed reader.</p>", title: "How far from the last close a buy may open", lede: Escaped(BuyRangeLede), region: "loop-ranges");
        }

        var rows = table.Rows.Where(row => row.Index == chosen.Code).ToArray();
        var picked = rows.FirstOrDefault(row => row.Chosen && row.Family == BuyRanges.AllSetups);
        var html = new StringBuilder(picked?.Widths is { } widths
            ? $"<p data-summary data-ranges=\"chosen\">The test of {Escaped(DayOf(table.Day))} on the {Escaped(chosen.Name)} chose {Escaped(widths.Words)}: the highest edge a pick over 2005 to 2018. It waits for your approval.</p>"
            : $"<p data-summary data-ranges=\"unchosen\">The test of {Escaped(DayOf(table.Day))} on the {Escaped(chosen.Name)} chose no width: none bought the {PlanPoints.FewestFills} picks over 2005 to 2018 a choice rests on.</p>");

        // see: The picker's buy range width is chosen first over the price families' setups under its menu and then over every member on every session
        if (table.Provisional)
        {
            html.Append($"<p class=\"lm-small\" data-ranges=\"provisional\">{Escaped(BuyRangeProvisional)}</p>");
        }

        foreach (var (years, words) in new[] { (BuyRanges.Early, "Chosen on 2005 to 2018"), (BuyRanges.Late, "Checked from 2019") })
        {
            html.Append(CultureInfo.InvariantCulture, $"<p class=\"lm-h\">{words}</p>");
            html.Append(RangeTable(rows.Where(row => row.Family == BuyRanges.AllSetups && row.Years == years)));
        }

        html.Append(table.Provisional ? "<details class=\"lm-fold\" data-ranges=\"setups\"><summary>Each plan of the picker's menu on its own</summary>" : "<details class=\"lm-fold\" data-ranges=\"setups\"><summary>Each setup on its own</summary>");

        foreach (var family in rows.Select(row => row.Family).Where(family => family != BuyRanges.AllSetups).Distinct())
        {
            var named = family.StartsWith(BuyRanges.PickerPlan, StringComparison.Ordinal) ? Capital(family[BuyRanges.PickerPlan.Length..]) : IdeaGrammar.FamilyWords(family);

            html.Append(CultureInfo.InvariantCulture, $"<p class=\"lm-p\"><b>{Escaped(named)}</b>, over 2005 to 2018</p>");
            html.Append(RangeTable(rows.Where(row => row.Family == family && row.Years == BuyRanges.Early)));
        }

        html.Append("</details>");

        if (picked?.Widths is { } shown && BuyRanges.Of(Core.Prices.Statistic.FromPrice(ExampleClose), Core.Prices.Statistic.FromPrice(ExampleMove), shown) is { } range)
        {
            html.Append(CultureInfo.InvariantCulture, $"<p class=\"lm-h\">How a card's plan reads at this width</p><p class=\"lm-small\">With a last close of ${ExampleClose:0.00} and a typical daily move of ${ExampleMove:0.00}:</p><ul class=\"range-plan\" data-ranges=\"plan\">");

            foreach (var line in BuyRanges.PlanLines(range))
            {
                html.Append(CultureInfo.InvariantCulture, $"<li>{Escaped(line)}</li>");
            }

            html.Append("</ul>");
        }

        return Cards.Computed("Buy ranges", html.ToString(), title: "How far from the last close a buy may open", lede: Escaped(BuyRangeLede), region: "loop-ranges");
    }

    static string RangeTable(IEnumerable<RangeWidthRow> rows)
    {
        var html = new StringBuilder("<div class=\"tbl-wrap\"><table class=\"lm-table range-table\"><thead><tr><th>Width</th><th class=\"r\">Bought</th><th class=\"r\">Opened above</th><th class=\"r\">Opened below</th><th class=\"r\">Edge a pick</th><th class=\"r\">Winners missed</th><th class=\"r\">Losses kept out</th></tr></thead><tbody>");

        foreach (var row in rows)
        {
            var name = row.Widths is { } widths ? widths.Key : "At the open, no range";
            var tally = row.Tally;

            html.Append(CultureInfo.InvariantCulture, $"<tr data-width=\"{Escaped(row.Width)}\"{(row.Chosen ? " data-chosen" : string.Empty)}><td data-h=\"Width\">{Escaped(name)}{(row.Chosen ? " <b>chosen</b>" : string.Empty)}</td>");
            html.Append(CultureInfo.InvariantCulture, $"<td data-h=\"Bought\" class=\"r num\">{tally.Bought:N0} of {tally.Offered:N0}</td><td data-h=\"Opened above\" class=\"r num\">{tally.Above:N0}</td><td data-h=\"Opened below\" class=\"r num\">{tally.Below:N0}</td>");
            html.Append(CultureInfo.InvariantCulture, $"<td data-h=\"Edge a pick\" class=\"r num\">{Risks(tally.EdgePerPick)}</td><td data-h=\"Winners missed\" class=\"r num\">{Risks(tally.MissedPerPick)}</td><td data-h=\"Losses kept out\" class=\"r num\">{Risks(tally.SavedPerPick)}</td></tr>");
        }

        return html.Append("</tbody></table></div>").ToString();
    }

    static string Risks(double? value) =>
        value is { } held ? held.ToString("+0.000;-0.000;0.000", CultureInfo.InvariantCulture) : "none";
}
