using System.Globalization;
using System.Text;
using EquityBrief.Core.Families;
using EquityBrief.Core.History;

namespace EquityBrief.Web.App;

// One family's year on one index under one reading, as the long build wrote it.
public sealed record LongYearRow(string Index, string Family, int Year, string Reading, int Settled, int Targets, double? EdgeAfterCost, int Crossing, double? EdgeAfterCostWithout);

// What the History page draws of the long ledger: the session the long history was read through and each family's years.
public sealed record LongYearsView(string? ReadThrough, IReadOnlyList<LongYearRow> Rows);

// The History page's families by year from 2005: on each index and the three pooled, the trades settled, the share reaching
// its target and the mean edge after costs as ruled, the same at the last price beside it, 2008 marked, the years the live
// settings were chosen on told from the years they never saw, the drift's years labelled mostly survivors and the S&P 400's
// and 600's years to 2008 labelled lower coverage.
// see: The long ledger is read over the long spans and the long prices with the store's own closes after them, each setup under the rule as ruled and with a stock whose prices stop ended at its last price
public sealed partial class SinglePageApp
{
    public const string LongYearsLede =
        "Each family's trades by year from 2005, every rule read as it stands today. The settings were chosen on 2019 onward; the years before are ones they never saw. A stock whose prices stop while a trade is open is left out as the rule is written, and counted at its last price in the figure beside it.";

    public const string Pooled = "pooled";

    static readonly (string Index, string Name)[] LongYearIndices = [("GSPC", "S&P 500"), ("MID", "S&P 400"), ("SML", "S&P 600"), (Pooled, "All three")];

    string LongYearsCard(LongYearsView view)
    {
        if (view.Rows.Count == 0)
        {
            return Cards.Computed("Long ledger", "<p class=\"empty\">The long ledger has not been written. <code>ledger-build --long</code> writes it on each index.</p>", title: "Each family by year", lede: LongYearsLede, region: "long-years");
        }

        var body = new StringBuilder();

        foreach (var family in view.Rows.Select(row => row.Family).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal))
        {
            var rows = view.Rows.Where(row => row.Family == family).ToArray();
            var years = rows.Select(row => row.Year).Distinct().Order().ToArray();
            var book = family is HeavyweightRule.Name or QualityRule.Name or ImprovingRule.Name;

            body.Append(Invariant($"<details data-family=\"{Escaped(family)}\"><summary>{Escaped(Capital(family))}: {rows.Where(row => row.Reading == LongLedger.AsRuled).Sum(row => row.Settled):N0} trades, {years[0]} to {years[^1]}</summary>"));

            if (family == DriftRule.Name)
            {
                body.Append($"<p class=\"read\" data-note=\"survivors\">{Escaped(LongFirstYears.SurvivorsNote)}</p>");
            }

            body.Append("<div class=\"tbl-wrap\"><table class=\"history-table\" data-table=\"long-years\"><thead><tr><th>Year</th>");
            body.Append(string.Concat(LongYearIndices.Select(index => $"<th class=\"r\" colspan=\"3\">{Escaped(index.Name)}</th>")));
            body.Append("</tr><tr><th></th>");
            body.Append(string.Concat(LongYearIndices.Select(_ => "<th class=\"r\">Trades</th><th class=\"r\">At target</th><th class=\"r\">Edge after costs</th>")));
            body.Append("</tr></thead><tbody>");

            foreach (var year in years)
            {
                var seen = LongLedger.ChosenOn(year) ? "chosen on" : "never seen";
                var marks = year == LongLedger.Crisis ? " data-crisis=\"2008\"" : string.Empty;

                body.Append(Invariant($"<tr data-year=\"{year}\" data-seen=\"{seen}\"{marks}><td>{year}{(year == LongLedger.Crisis ? " <span class=\"on\">crisis</span>" : string.Empty)} <span class=\"on\">{seen}</span></td>"));

                foreach (var (index, _) in LongYearIndices)
                {
                    var asRuled = Year(rows, index, year, LongLedger.AsRuled);
                    var lastPrice = Year(rows, index, year, LongLedger.LastPrice);
                    var lower = index is "MID" or "SML" && year <= LongFloors.LowerThrough ? Invariant($" title=\"{Escaped(LongFloors.LowerLabel)}\" data-note=\"lower\"") : string.Empty;

                    body.Append(Invariant($"<td class=\"n\" data-index=\"{index}\"{lower}>{asRuled.Settled:N0}</td>"));
                    body.Append(Invariant($"<td class=\"n\">{(asRuled.Settled > 0 ? (100.0 * asRuled.Targets / asRuled.Settled).ToString("0", CultureInfo.InvariantCulture) + "%" : "none")}</td>"));
                    body.Append(Invariant($"<td class=\"n\" data-edge=\"{Shown(asRuled.Edge, book)}\">{Shown(asRuled.Edge, book)} <span class=\"on\">last price {Shown(lastPrice.Edge, book)}</span></td>"));
                }

                body.Append("</tr>");
            }

            body.Append("</tbody></table></div></details>");
        }

        body.Append(Invariant($"<p class=\"read\" data-note=\"lower\">The S&amp;P 400's and 600's years to {LongFloors.LowerThrough}: {Escaped(LongFloors.LowerLabel)}.</p>"));

        return Cards.Computed(
            "Long ledger",
            body.ToString(),
            title: "Each family by year",
            lede: LongYearsLede,
            stamp: view.ReadThrough is { } through ? $"<span class=\"run\">read through {Escaped(through)}</span>" : null,
            region: "long-years");
    }

    // One index's year, or the three pooled: the trades settled, those at target and the mean edge after costs weighted by
    // the trades each index settled.
    static (int Settled, int Targets, double? Edge) Year(IReadOnlyList<LongYearRow> rows, string index, int year, string reading)
    {
        var held = rows.Where(row => row.Year == year && row.Reading == reading && (index == Pooled || row.Index == index)).ToArray();
        var weighed = held.Where(row => row.EdgeAfterCost is not null && row.Settled > 0).ToArray();
        var settled = weighed.Sum(row => row.Settled);

        return (held.Sum(row => row.Settled), held.Sum(row => row.Targets), settled == 0 ? null : weighed.Sum(row => row.Settled * row.EdgeAfterCost!.Value) / settled);
    }

    // A swing family's edge in risks and a book's in per cent of its buy.
    static string Shown(double? edge, bool book) =>
        edge is not { } held ? "none"
        : book ? (held * 100).ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture) + "%"
        : held.ToString("+0.00;-0.00;0.00", CultureInfo.InvariantCulture) + " R";
}
