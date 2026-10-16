using System.Globalization;
using System.Text;
using EquityBrief.Core.Insiders;

namespace EquityBrief.Web.Marks;

// The report's Insiders region from 19.13, drawn from the core's view and working none of it: the year's open-market
// purchases and sales a row each, a purchase in the gain's green and a sale in the loss's red with a planned sale marked,
// newest first; the totals over 90 days and the year with the net in the hue of its sign; and the line counting the
// awards, exercises, withholding and other transactions neither total counts. Each element carries its stored figure.
// see: Insiders' trades are read from their Form 4 filings, and only open-market purchases and sales are counted
// see: A figure that rose or fell is drawn green or red with its sign on every page
public sealed partial class MarkRenderer
{
    public string InsiderTrades(InsiderView view)
    {
        var html = new StringBuilder();

        if (view.Lines.Count == 0)
        {
            html.Append("<p class=\"insider-none\" data-trades=\"0\">No insider of the company bought or sold in the open market in the year to this night.</p>");
        }
        else
        {
            html.Append("<div class=\"tbl-wrap\"><table class=\"insider-trades\"><thead><tr><th>Who</th><th>Role</th><th>Traded</th><th>Filed</th><th>Shares</th><th>Price</th><th>Value</th></tr></thead><tbody>");

            foreach (var line in view.Lines)
            {
                var kind = line.Purchase ? "purchase" : "sale";

                html.Append(Formatted($"<tr class=\"{(line.Purchase ? "gain" : "loss")}\" data-kind=\"{kind}\" data-planned=\"{(line.Planned ? "1" : "0")}\" data-traded=\"{DayOf(line.Traded)}\" data-filed=\"{DayOf(line.Filed)}\">"));
                html.Append($"<td>{Escaped(line.Owners)}</td><td>{Escaped(line.Role)}</td>");
                html.Append($"<td class=\"nw\">{DayOf(line.Traded)}</td><td class=\"nw\">{DayOf(line.Filed)}</td>");
                html.Append(Formatted($"<td data-shares=\"{line.Shares}\">{line.Shares.ToString("#,##0.###", CultureInfo.InvariantCulture)}</td>"));
                html.Append(line.Price is { } price ? Formatted($"<td data-price=\"{price}\">{Figures.Price(price)}</td>") : "<td data-price=\"\">not stated</td>");
                html.Append(line.Dollars is { } dollars
                    ? Formatted($"<td data-value=\"{dollars}\">{(line.Purchase ? "+" : "-")}{Figures.Money(dollars)}{(line.Planned ? " <span class=\"planned\">planned</span>" : string.Empty)}</td>")
                    : $"<td data-value=\"\">not stated{(line.Planned ? " <span class=\"planned\">planned</span>" : string.Empty)}</td>");
                html.Append("</tr>");
            }

            html.Append("</tbody></table></div>");
        }

        html.Append("<div class=\"insider-totals\">");

        foreach (var (words, totals, span) in new[] { ("Over the 90 days to this night", view.Ninety, "ninety"), ("Over the year to this night", view.Year, "year") })
        {
            var net = EquityBrief.Core.Prices.Statistic.FromPrice(totals.Net);

            html.Append(Formatted($"<p data-span=\"{span}\" data-bought=\"{totals.Bought}\" data-sold=\"{totals.Sold}\" data-net=\"{totals.Net}\" data-buyers=\"{totals.Buyers}\" data-sellers=\"{totals.Sellers}\">"));
            html.Append($"<b>{words}:</b> bought {Figures.Money(totals.Bought)} by {Counted(totals.Buyers, "insider", "insiders")}, sold {Figures.Money(totals.Sold)} by {Counted(totals.Sellers, "insider", "insiders")}, net ");
            html.Append(SignedFigure(net, 0, (totals.Net > 0 ? "+" : string.Empty) + Figures.Money(totals.Net)));
            html.Append(".</p>");
        }

        html.Append("</div>");
        html.Append(Formatted($"<p class=\"insider-left-out\" data-awards=\"{view.Awards}\" data-exercises=\"{view.Exercises}\" data-withholding=\"{view.Withholding}\" data-others=\"{view.Others}\">"));
        html.Append($"In the year, {Counted(view.Awards, "award", "awards")}, {Counted(view.Exercises, "exercise or conversion", "exercises and conversions")}, {Counted(view.Withholding, "withholding of shares for tax", "withholdings of shares for tax")} and {Counted(view.Others, "other transaction", "other transactions")}, counted in neither total.</p>");

        return html.ToString();
    }
}
