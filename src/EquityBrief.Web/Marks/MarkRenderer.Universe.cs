using System.Text;

namespace EquityBrief.Web.Marks;

// What the universe's link carries beside its trend, its sector and its page, from 19.5: the words searched for, the
// near-level chip lit, the one gate short chip, the column sorted on and which way, and the rows a page.
public sealed record UniverseState(string? Search = null, string? Near = null, bool Short = false, string? Sort = null, bool Descending = false, string? Rows = null)
{
    // The keys a link keeps, each left out where the state holds none, written after the trend, the sector and the
    // index so a link the table drew before 19.5 reads as it did.
    public string Extra(bool keepNear = true, bool keepShort = true, bool keepSort = true, bool keepRows = true)
    {
        var parts = new List<string>();

        if (Search is { Length: > 0 } words)
        {
            parts.Add("q=" + Uri.EscapeDataString(words));
        }

        if (keepNear && Near is { Length: > 0 } near)
        {
            parts.Add("near=" + Uri.EscapeDataString(near));
        }

        if (keepShort && Short)
        {
            parts.Add("short=1");
        }

        if (keepSort && Sort is { Length: > 0 } sort)
        {
            parts.Add("sort=" + Uri.EscapeDataString(sort));

            if (Descending)
            {
                parts.Add("dir=desc");
            }
        }

        if (keepRows && Rows is { Length: > 0 } rows)
        {
            parts.Add("rows=" + Uri.EscapeDataString(rows));
        }

        return string.Concat(parts.Select(part => "&amp;" + part));
    }
}

// The universe's controls from 19.5 and its sector tiles: a search box, the chips for a close near its support or its
// resistance and a member one gate short of a setup, the rows a page, and a tile a GICS sector filtering the table.
public sealed partial class MarkRenderer
{
    // A link to the universe with the trend, the sector and the index kept, and the state's other keys.
    static string UniverseHref(string? trend, string? sector, string? universe, string extra) =>
        "#/universe?page=1"
        + (trend is { Length: > 0 } ? "&amp;trend=" + Uri.EscapeDataString(trend) : string.Empty)
        + (sector is { Length: > 0 } ? "&amp;sector=" + Uri.EscapeDataString(sector) : string.Empty)
        + (universe is { Length: > 0 } ? "&amp;universe=" + Uri.EscapeDataString(universe) : string.Empty)
        + extra;

    // The link a column's heading sorts the table by, every other key kept and the page back to the first.
    public static Func<string, bool, string> SortHref(string? trend, string? sector, string? universe, UniverseState state) =>
        (key, descending) => UniverseHref(trend, sector, universe, (state with { Sort = key, Descending = descending }).Extra());

    // The search box, the near-level chips, the one gate short chip with what it reads on the index, and the rows a page,
    // each a link keeping every other key; the box's press is the shell's, which puts its words in the link.
    public string UniverseControls(UniverseState state, string? trend, string? sector, string? universe, bool shortRead, IReadOnlyList<int> sizes, string all)
    {
        var html = new StringBuilder();

        html.Append(Formatted($"<form class=\"universe-search\" role=\"search\" data-merge-key=\"q\"><label><span class=\"chips-label\">Search</span><input type=\"search\" name=\"q\" value=\"{Escaped(state.Search ?? string.Empty)}\" placeholder=\"A ticker or a company\" autocomplete=\"off\" spellcheck=\"false\"></label><button type=\"submit\" class=\"btn-2\">Find</button></form>"));
        html.Append(Formatted($"<nav class=\"universe-filters more-chips\" data-near=\"{Escaped(state.Near ?? "none")}\" data-short=\"{Flag(state.Short)}\" data-rows=\"{Escaped(state.Rows ?? sizes[0].ToString(Invariant))}\">"));
        html.Append("<span class=\"chips-label\">Near a level</span>");

        foreach (var (near, words) in new[] { ((string?)null, "any"), ("support", "near support"), ("resistance", "near resistance") })
        {
            html.Append(Formatted($"<a class=\"chip\" data-filter=\"near\" data-value=\"{Escaped(near ?? "any")}\" aria-pressed=\"{Flag(state.Near == near)}\" href=\"{UniverseHref(trend, sector, universe, (state with { Near = near }).Extra())}\">{Escaped(words)}</a>"));
        }

        html.Append("<span class=\"chip-break\"></span><span class=\"chips-label\">Setups</span>");
        html.Append(shortRead
            ? Formatted($"<a class=\"chip\" data-filter=\"short\" data-value=\"one gate short\" aria-pressed=\"{Flag(state.Short)}\" href=\"{UniverseHref(trend, sector, universe, (state with { Short = !state.Short }).Extra())}\">one gate short of a setup</a>")
            : "<span class=\"chip degraded\" data-filter=\"short\" data-value=\"not read\">one gate short: this index's night stores only the first part each member failed</span>");

        html.Append("<span class=\"chip-break\"></span><span class=\"chips-label\">Rows a page</span>");

        foreach (var size in sizes.Select(one => one.ToString(Invariant)).Append(all))
        {
            var lit = (state.Rows ?? sizes[0].ToString(Invariant)) == size;

            html.Append(Formatted($"<a class=\"chip\" data-filter=\"rows\" data-value=\"{size}\" aria-pressed=\"{Flag(lit)}\" href=\"{UniverseHref(trend, sector, universe, (state with { Rows = size == sizes[0].ToString(Invariant) ? null : size }).Extra())}\">{size}</a>"));
        }

        html.Append("</nav>");

        return html.ToString();
    }

    // A tile a GICS sector in its order: its members, the share of them in an uptrend as a bar or that the trend is not
    // read on the index, how many tonight's lists hold, and its fund's return over the live heavyweights rule's look-back,
    // each tile a link filtering the table to its sector and the lit one marked.
    public string SectorTiles(IReadOnlyList<SectorLine> tiles, string? lit, string? trend, string? universe, UniverseState state, int notFiled, int lookBack)
    {
        var html = new StringBuilder();

        html.Append(Formatted($"<div class=\"sector-tiles\" data-sectors=\"{tiles.Count}\" data-not-filed=\"{notFiled}\">"));

        foreach (var tile in tiles)
        {
            var share = tile.Names == 0 ? 0 : 1.0 * tile.InUptrend / tile.Names;
            var href = tile.Sector == lit ? UniverseHref(trend, null, universe, state.Extra()) : UniverseHref(trend, tile.Sector, universe, state.Extra());

            html.Append(Formatted($"<a class=\"sector-tile\" data-sector=\"{Escaped(tile.Sector)}\" data-names=\"{tile.Names}\" data-uptrend=\"{tile.InUptrend}\" data-listed=\"{tile.OnTheList}\" data-fund=\"{Escaped(tile.Fund ?? "none")}\" data-fund-return=\"{(tile.FundReturn is { } made ? made.ToString("R", Invariant) : "none")}\" aria-pressed=\"{Flag(tile.Sector == lit)}\" href=\"{href}\">"));
            html.Append(Formatted($"<b class=\"tile-name\">{Escaped(tile.Sector)}</b><span class=\"tile-count\">{tile.Names} {(tile.Names == 1 ? "member" : "members")}</span>"));
            html.Append(tile.TrendRead
                ? Formatted($"<span class=\"tile-trend\">{tile.InUptrend} in an uptrend<span class=\"cell-bar\" aria-hidden=\"true\"><span class=\"cell-fill\" style=\"width:{share * 100:0.#}%\"></span></span></span>")
                : "<span class=\"tile-trend degraded\">the trend is not read on this index</span>");
            html.Append(Formatted($"<span class=\"tile-listed\">{tile.OnTheList} on tonight's lists</span>"));
            html.Append(tile.Fund is { } fund
                ? Formatted($"<span class=\"tile-fund\">{Escaped(fund)} {(tile.FundReturn is { } fundReturn ? SignedFigure(fundReturn * 100, 1, Formatted($"{Unsigned(fundReturn * 100, 1):+0.0;-0.0;0.0}%")) : "<span class=\"degraded\">not read</span>")} over {lookBack} sessions</span>")
                : "<span class=\"tile-fund degraded\">no sector fund</span>");
            html.Append("</a>");
        }

        html.Append("</div>");
        html.Append(notFiled > 0 ? Formatted($"<p class=\"oneline\" data-not-filed=\"{notFiled}\">{notFiled} {(notFiled == 1 ? "member files" : "members file")} no sector and {(notFiled == 1 ? "is" : "are")} counted in none.</p>") : string.Empty);

        return html.ToString();
    }
}
