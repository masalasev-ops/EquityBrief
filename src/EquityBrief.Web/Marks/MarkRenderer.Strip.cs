using System.Text;

namespace EquityBrief.Web.Marks;

// One tile of the market strip: its key and label, its level as drawn, its change on the day, in per cent or for a yield
// in points, its words under the level, the words beneath it, whether its figures are Yahoo's quote, and whether Yahoo
// answered nothing for it in the session so it draws the close before.
public sealed record StripTileView(string Key, string Label, string? Level, double? Change, string? Words, string? Beneath, bool Quoted, bool PriorClose, bool Points = false);

// The market strip as a page draws it: its sentence from the night's figures, the line above the tiles saying whose
// prices they are and as of when, and the tiles in their order.
public sealed record MarketStripView(string Summary, string Line, bool Quoted, IReadOnlyList<StripTileView> Tiles);

// From 21.13 the market strip atop Tonight on every index and atop every page the copy holds: one row of tiles wrapping at
// a phone's width, each with its level and its change on the day, a rise in the gain's green with an arrow up and a fall
// in the loss's red with an arrow down, so the colour is not the only sign.
// see: The market strip draws each market's close and its change on the day from the night's store, and Yahoo's delayed quote in the session
public sealed partial class MarkRenderer
{
    public const string StripRegion = "market-strip";

    public const string PriorCloseOnly = "prior close only";

    public string MarketStrip(MarketStripView strip)
    {
        var html = new StringBuilder();

        html.Append(CardSummary(strip.Summary));
        html.Append(Invariant, $"<p class=\"strip-line\" data-quoted=\"{Flag(strip.Quoted)}\">{Escaped(strip.Line)}</p>");
        html.Append(Invariant, $"<div class=\"strip-tiles\" data-tiles=\"{strip.Tiles.Count}\">");

        foreach (var tile in strip.Tiles)
        {
            var hue = tile.Change is { } moved ? Hue(moved, 2) : "flat";

            html.Append(Invariant, $"<div class=\"strip-tile\" data-tile=\"{Escaped(tile.Key)}\" data-change=\"{(tile.Change is { } change ? change.ToString("R", Invariant) : "none")}\" data-quoted=\"{Flag(tile.Quoted)}\" data-prior=\"{Flag(tile.PriorClose)}\">");
            html.Append(Invariant, $"<span class=\"st-label\">{Escaped(tile.Label)}</span>");
            html.Append(tile.Level is { } level ? Formatted($"<b class=\"st-level\">{Escaped(level)}</b>") : "<b class=\"st-level degraded\">not stored</b>");
            html.Append(tile.Change is { } shown ? Formatted($"<span class=\"st-change {hue}\">{Arrow(shown)}{Escaped(Changed(shown, tile.Points))}</span>") : string.Empty);
            html.Append(tile.Words is { } words ? Formatted($"<span class=\"st-words\">{Escaped(words)}</span>") : string.Empty);
            html.Append(tile.Beneath is { } beneath ? Formatted($"<span class=\"st-beneath\">{Escaped(beneath)}</span>") : string.Empty);
            html.Append(tile.PriorClose ? Formatted($"<span class=\"st-beneath st-prior\">{PriorCloseOnly}</span>") : string.Empty);
            html.Append("</div>");
        }

        html.Append("</div>");

        return html.ToString();
    }

    // The arrow a change carries beside its hue, none where it is flat.
    static string Arrow(double change) =>
        Unsigned(change, 2) > 0 ? "<span class=\"st-arrow\" aria-label=\"up\">▲</span> "
        : Unsigned(change, 2) < 0 ? "<span class=\"st-arrow\" aria-label=\"down\">▼</span> "
        : string.Empty;

    static string Changed(double change, bool points) =>
        points
            ? Unsigned(change, 2).ToString("+0.00;-0.00;0.00", Invariant)
            : Unsigned(change, 2).ToString("+0.00;-0.00;0.00", Invariant) + "%";
}
