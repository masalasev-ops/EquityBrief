using System.Text;
using EquityBrief.Core.History;
using EquityBrief.Web.Marks;

namespace EquityBrief.Web.App;

// One count of the data check's findings: a class, a kind within it or none for the class's whole, an era or none for every
// era, the findings and the stocks they stand on.
public sealed record DataCheckCount(string Class, string? Kind, string? Era, int Findings, int Stocks);

// One finding a person reads against the filings or the chart.
public sealed record DataCheckRow(string Ticker, DateOnly Session, string Class, string Kind, string Says);

// What the History page draws: the run that wrote the check's rows, when it ended and what it read, its counts and the findings
// left to read.
public sealed record DataCheckView(string? RunId, string? RanAt, string? Read, IReadOnlyList<DataCheckCount> Counts, IReadOnlyList<DataCheckRow> ToRead);

// The History page: the data check's counts over the years the pull holds, by class and by era, and the findings a person reads.
// see: The data check classifies every stock's pulled sessions by stated rules into a table of its own, and moves no price row
public sealed partial class SinglePageApp
{
    public const string HistoryRoute = "#/history";

    // The jumps and the three classes a person reads, each against the filings or the chart.
    public static IReadOnlyList<(string Class, string? Kind)> ReadByHand { get; } =
    [
        (DataCheck.Jump, DataCheck.Unexplained),
        (DataCheck.Jump, DataCheck.SpikeBack),
        (DataCheck.TickerChange, null),
        (DataCheck.WhenIssued, null),
        (DataCheck.Emergence, null),
    ];

    public const string HistoryLede =
        "Every stock's pulled sessions sorted by fixed rules, so the counts are read before anything is tested on the longer history. Nothing here changes a price: a bad print or a start is marked only on the operator's word.";

    public string HistoryRegion(DataCheckView view, LongMembershipView? membership = null, LongPricesView? prices = null, LongYearsView? years = null, IReadOnlyList<NewsMoodRow>? mood = null)
    {
        var region = new StringBuilder();

        region.Append("<section class=\"history\">");
        region.Append(Cards.Masthead("History", "<span class=\"m-screen\">History</span>", view.RanAt is { } ran ? $"Data check of {Escaped(ran)}" : "Not run yet"));

        if (view.RunId is null)
        {
            region.Append(Cards.Computed("Data check", "<p class=\"empty\">The data check has not run. <code>data-check</code> writes its rows over the years the pull holds.</p>", title: "The data check", lede: HistoryLede, region: "data-check"));

            if (membership is not null)
            {
                region.Append(LongMembershipCard(membership));
            }

            if (prices is not null)
            {
                region.Append(LongPricesCard(prices));
            }

            if (years is not null)
            {
                region.Append(LongYearsCard(years));
            }

            if (mood is not null)
            {
                region.Append(NewsMoodCard(mood));
            }

            return region.Append("</section>").ToString();
        }

        var eras = DataCheck.Eras.Concat(view.Counts.Select(count => count.Era).OfType<string>().Where(era => !DataCheck.Eras.Contains(era)).Distinct()).ToArray();
        string Cell(DataCheckCount? count) => count is null ? "<td class=\"n\">0</td>" : Invariant($"<td class=\"n\">{count.Findings:N0} <span class=\"on\">on {count.Stocks:N0}</span></td>");
        DataCheckCount? Count(string kindOf, string? kind, string? era) => view.Counts.FirstOrDefault(count => count.Class == kindOf && count.Kind == kind && count.Era == era);

        var table = new StringBuilder();

        table.Append("<div class=\"tbl-wrap\"><table class=\"history-table\" data-table=\"counts\"><thead><tr><th>Class</th>");
        table.Append(string.Concat(eras.Select(era => $"<th class=\"r\">{Escaped(era)}</th>")));
        table.Append("<th class=\"r\">Every era</th></tr></thead><tbody>");

        foreach (var kindOf in DataCheck.Classes)
        {
            table.Append($"<tr class=\"cls\" data-class=\"{Escaped(kindOf)}\"><td>{Escaped(Capital(kindOf))}</td>");
            table.Append(string.Concat(eras.Select(era => Cell(view.Counts.Where(count => count.Class == kindOf && count.Kind is null && count.Era == era).FirstOrDefault()))));
            table.Append(Cell(Count(kindOf, null, null)) + "</tr>");

            foreach (var (_, kind) in DataCheck.Kinds.Where(pair => pair.Class == kindOf && pair.Kind != DataCheck.ToRead))
            {
                table.Append($"<tr data-class=\"{Escaped(kindOf)}\" data-kind=\"{Escaped(kind)}\"><td class=\"kind\">{Escaped(kind)}</td>");
                table.Append(string.Concat(eras.Select(era => Cell(Count(kindOf, kind, era)))));
                table.Append(Cell(Count(kindOf, kind, null)) + "</tr>");
            }
        }

        table.Append("</tbody></table></div>");

        region.Append(Cards.Computed(
            "Data check",
            $"<p class=\"read\">{Escaped(view.Read ?? string.Empty)}</p>" + table,
            title: "Counts by class and era",
            lede: HistoryLede,
            stamp: $"<span class=\"run\">{Escaped(view.RunId)}</span>",
            region: "data-check"));

        var read = new StringBuilder();

        foreach (var (kindOf, kind) in ReadByHand)
        {
            var rows = view.ToRead.Where(row => row.Class == kindOf && (kind is null || row.Kind == kind)).ToArray();
            var named = kind is null ? Capital(kindOf) : $"{Capital(kindOf)}, {kind}";

            read.Append(Invariant($"<details data-read=\"{Escaped(kind ?? kindOf)}\"><summary>{Escaped(named)}: {rows.Length:N0}</summary>"));

            if (rows.Length > 0)
            {
                read.Append("<div class=\"tbl-wrap\"><table class=\"history-table\"><tbody>");

                foreach (var row in rows)
                {
                    read.Append($"<tr><td><a href=\"{ChartRoute}{Uri.EscapeDataString(row.Ticker)}\">{Escaped(row.Ticker)}</a></td><td class=\"says\">{Escaped(row.Says)}</td></tr>");
                }

                read.Append("</tbody></table></div>");
            }

            read.Append("</details>");
        }

        // From the 23.4 correction the unexplained jumps and the unsettled ends are marked on a page of their own.
        // see: The marking page proposes a mark for each unexplained jump and unsettled end from its evidence, and writes none without the operator's confirm
        read.Append(Invariant($"<p class=\"card-note\" data-marking=\"link\"><a href=\"{MarkingRoute}\">Mark the unexplained jumps and the ends no filing settles</a>, each with its evidence and a proposed mark.</p>"));

        region.Append(Cards.Computed(
            "Data check",
            read.ToString(),
            title: "To read against the filings",
            lede: "The jumps nothing stored explains and the starts and changes a rule flags, each read against the company's filings and its chart before the operator's word marks it.",
            region: "data-check-read"));

        if (membership is not null)
        {
            region.Append(LongMembershipCard(membership));
        }

        if (prices is not null)
        {
            region.Append(LongPricesCard(prices));
        }

        if (years is not null)
        {
            region.Append(LongYearsCard(years));
        }

        if (mood is not null)
        {
            region.Append(NewsMoodCard(mood));
        }

        return region.Append("</section>").ToString();
    }

    static string Capital(string words) => words.Length == 0 ? words : char.ToUpperInvariant(words[0]) + words[1..];
}
