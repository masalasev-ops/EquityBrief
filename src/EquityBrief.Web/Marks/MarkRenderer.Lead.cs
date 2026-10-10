using System.Text;
using EquityBrief.Web.App;

namespace EquityBrief.Web.Marks;

// The head of a name's page beneath the masthead: one sentence carrying no figure, and four tiles, each a figure worked
// from stored values at the price the masthead draws, with what it is read against beneath it and its value whole on its
// element. A change is coloured by its sign on the name page alone, and the sign is always written beside it, so the hue
// is never the only channel.
// see: The headline carries no figure, and code writes it until a written one is accepted
// see: Four tiles under the headline are worked by code from stored figures at the price the page draws
// see: A figure that rose or fell is drawn in a hue of its own on the name page and its export alone
public sealed partial class MarkRenderer
{
    public string Lead(string ticker, HeadlineView? headline, TilesView? tiles)
    {
        var lead = new StringBuilder();

        lead.Append(Formatted($"<section class=\"lead\" data-ticker=\"{Escaped(ticker)}\">"));
        lead.Append(headline is { } said
            ? Formatted($"<h1 class=\"lead-line\" data-written=\"{(said.Written ? "model" : "code")}\">{Escaped(said.Sentence)}</h1>")
            : string.Empty);

        lead.Append(tiles is { } shown ? LeadTiles(shown) : string.Empty);

        return lead.Append("</section>").ToString();
    }

    // The four tiles alone, which the page's script draws again at a newer quote.
    public string LeadTiles(TilesView tiles) =>
        Formatted($"<div class=\"lead-tiles\" data-live=\"{(tiles.Live ? "yes" : "no")}\">")
        + EarningsTile(tiles.Earnings)
        + GrowthTile(tiles.Growth)
        + YieldTile(tiles.Yield, tiles.Price, tiles.Live)
        + HighTile(tiles.High, tiles.Live)
        + "</div>";

    static string EarningsTile(EquityBrief.Core.Tiles.EarningsTile? earnings) =>
        earnings is not { } tile
            ? LeadTile("earnings", "The latest quarter's earnings a share", "<span class=\"v none\">not stored</span>", "No reported quarter is stored for it yet.")
            : LeadTile(
                "earnings",
                Formatted($"Quarter to {tile.Quarter:yyyy-MM-dd}, earnings a share"),
                Formatted($"<span class=\"v\" data-value=\"{tile.Actual.ToString(Invariant)}\">{Figures.PerShare(tile.Actual)}</span>"),
                (tile.Estimate is { } estimate
                    ? Formatted($"estimate <span data-estimate=\"{estimate.ToString(Invariant)}\">{Figures.PerShare(estimate)}</span>") + (tile.AgainstEstimate is { } against ? ", " + Signed(against, "against") : string.Empty)
                    : "no estimate was filed")
                + (tile.OnTheYear is { } year ? "; " + Signed(year, "year") + " on a year" : tile.YearBefore is null ? "; no quarter a year before" : "; a year before read no change"));

    static string GrowthTile(EquityBrief.Core.Tiles.GrowthTile? growth) =>
        growth is not { } tile
            ? LeadTile("growth", "Sales, the last four quarters on the four before", "<span class=\"v none\">not read</span>", "Eight quarters of sales are not stored for it yet.")
            : LeadTile(
                "growth",
                "Sales, the last four quarters on the four before",
                Signed(tile.Percent, "growth", "v"),
                Formatted($"through the quarter to {tile.Through:yyyy-MM-dd}, from its filings"));

    static string YieldTile(EquityBrief.Core.Tiles.YieldTile? yield, decimal? price, bool live) =>
        yield is not { } tile
            ? LeadTile("yield", "Dividend yield", "<span class=\"v none\">none</span>", "No dividend is stored for it.")
            : LeadTile(
                "yield",
                "Dividend yield",
                Formatted($"<span class=\"v\" data-value=\"{tile.Percent.ToString(Invariant)}\">{tile.Percent.ToString("0.00", Invariant)}%</span>"),
                Formatted($"<span data-rate=\"{tile.Rate.ToString(Invariant)}\">{Figures.PerShare(tile.Rate)}</span> a year at {(price is { } at ? Formatted($"<span data-price=\"{at.ToString(Invariant)}\">{Figures.Price(at)}</span>") : "the price")}, {(live ? "the quote" : "the last close")}"));

    static string HighTile(EquityBrief.Core.Tiles.HighTile? high, bool live) =>
        high is not { } tile
            ? LeadTile("high", "From the 52-week high", "<span class=\"v none\">not stored</span>", "The year's high and low are not stored for it.")
            : LeadTile(
                "high",
                "From the 52-week high",
                Signed(tile.FromHigh, "from-high", "v") + RangeBar(tile),
                Formatted($"high <span data-high=\"{tile.High.ToString(Invariant)}\">{Figures.Price(tile.High)}</span> on {tile.HighOn:yyyy-MM-dd}; low <span data-low=\"{tile.Low.ToString(Invariant)}\">{Figures.Price(tile.Low)}</span> on {tile.LowOn:yyyy-MM-dd}; {(live ? "at the quote" : "at the last close")}"));

    // Where the price sits between the year's low and high: a line from the low to the high and a dot at the price, held
    // inside the line where the price is past either end.
    static string RangeBar(EquityBrief.Core.Tiles.HighTile tile)
    {
        const double Width = 120;
        var at = Math.Clamp(tile.Position, 0, 1) * Width;

        return Formatted($"<svg class=\"range-bar\" viewBox=\"-4 0 128 12\" width=\"128\" height=\"12\" role=\"img\" aria-label=\"The price between the year's low and high\" data-position=\"{tile.Position.ToString(Invariant)}\"><title>The price between the year's low of {Figures.Price(tile.Low)} and high of {Figures.Price(tile.High)}</title><line class=\"rb-track\" x1=\"0\" y1=\"6\" x2=\"{Width.ToString(Invariant)}\" y2=\"6\"/><circle class=\"rb-dot\" cx=\"{at.ToString("0.#", Invariant)}\" cy=\"6\" r=\"4\"/></svg>");
    }

    static string LeadTile(string kind, string key, string value, string beneath) =>
        Formatted($"<div class=\"lead-tile\" data-tile=\"{kind}\"><span class=\"k\">{key}</span>{value}<span class=\"s\">{beneath}</span></div>");

    // A change in per cent with its sign always written, in the rise or the fall hue by that sign and in ink where it is
    // none, its value whole on its element.
    static string Signed(double percent, string name, string? kind = null)
    {
        var hue = percent > 0 ? "up" : percent < 0 ? "down" : "flat";

        return Formatted($"<span class=\"{(kind is null ? string.Empty : kind + " ")}{hue}\" data-{name}=\"{percent.ToString(Invariant)}\">{percent.ToString("+0.0;-0.0;0.0", Invariant)}%</span>");
    }
}
