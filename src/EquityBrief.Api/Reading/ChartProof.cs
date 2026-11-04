using System.Net;
using System.Text;
using EquityBrief.Core.Chart;
using EquityBrief.Web.Marks;

namespace EquityBrief.Api.Reading;

// A finding of the chart check as the proof page reads it.
public sealed record ChartProofRow(string Night, string Ticker, string Flag, string Kind, string Session, string Says);

// The chart check's page: the night's findings by flag, those new since the night before first, each stock linking to its chart.
// see: The chart check reads every member's chart after the bars land and asks nothing of the network, and lists what it finds the morning it appears
public static class ChartProof
{
    public const string Route = "/proof/charts";

    static readonly (string Flag, string Words)[] Flags =
    [
        ("a", "An average more than a quarter outside the candles in view"),
        ("b", "A one-day move over a quarter with no stored event, or a split the prices still carry"),
        ("c", "A break where the pulled history meets the stored bars"),
        ("e", "A missing session, a candle that could not have traded, one with no volume or copying the session before, a stock behind the night reading ok, or a stored action the bars do not carry"),
    ];

    public static IReadOnlyList<ChartProofRow> Fresh(IReadOnlyList<ChartProofRow> rows)
    {
        var nights = rows.Select(row => row.Night).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

        if (nights.Length < 2)
        {
            return [];
        }

        var before = rows.Where(row => row.Night == nights[0]).Select(Key).ToHashSet(StringComparer.Ordinal);

        return [.. rows.Where(row => row.Night == nights[^1] && !before.Contains(Key(row)))];
    }

    static string Key(ChartProofRow row) => string.Join('|', row.Ticker, row.Flag, row.Kind, row.Session);

    public static string Page(IReadOnlyList<ChartProofRow> rows)
    {
        var night = rows.Count > 0 ? rows.Max(row => row.Night) : null;
        var tonight = rows.Where(row => row.Night == night).ToArray();
        var older = tonight.Where(row => row.Kind.EndsWith(ChartCheck.InThePulledYears, StringComparison.Ordinal)).ToArray();
        var fresh = Fresh(rows);
        var page = new StringBuilder("<!doctype html><meta charset=\"utf-8\"><title>Chart check</title><body style=\"font:15px/1.5 system-ui,sans-serif;max-width:1100px;margin:24px auto;padding:0 16px\">");

        page.Append("<h1>Chart check</h1>");

        if (night is null)
        {
            return page.Append("<p>No night has run the chart check yet.</p>").ToString();
        }

        page.Append(FormattableString.Invariant($"<p data-night=\"{night}\">Over the night of {night}: {tonight.Length} finding(s) on {tonight.Select(row => row.Ticker).Distinct(StringComparer.Ordinal).Count()} stock(s), {older.Length} of them in the pulled years, {fresh.Count} new since the night before.</p>"));
        page.Append(Table("New since the night before", "fresh", fresh));

        foreach (var (flag, words) in Flags)
        {
            page.Append(Table(FormattableString.Invariant($"Flag {flag}: {words}"), flag, [.. tonight.Where(row => row.Flag == flag).Except(older)]));
        }

        if (older.Length > 0)
        {
            page.Append(FormattableString.Invariant($"<details data-flag=\"pulled-years\"><summary>{older.Length} in the pulled years, which no rule reads but the readings named as reading them</summary>"));
            page.Append(Table("In the pulled years", "older", older));
            page.Append("</details>");
        }

        return page.ToString();
    }

    static string Table(string heading, string key, IReadOnlyList<ChartProofRow> rows)
    {
        var table = new StringBuilder();

        table.Append(FormattableString.Invariant($"<h2 style=\"font-size:17px\">{WebUtility.HtmlEncode(heading)}: {rows.Count}</h2>"));

        if (rows.Count == 0)
        {
            return table.ToString();
        }

        table.Append(FormattableString.Invariant($"<table data-flag=\"{key}\" style=\"border-collapse:collapse\"><tbody>"));

        foreach (var row in rows)
        {
            var ticker = WebUtility.HtmlEncode(row.Ticker);

            table.Append(FormattableString.Invariant($"<tr><td style=\"padding:2px 10px 2px 0;vertical-align:top\"><a href=\"/{MarkRenderer.ChartRoute}{Uri.EscapeDataString(row.Ticker)}\">{ticker}</a></td><td style=\"padding:2px 10px 2px 0;vertical-align:top\">{WebUtility.HtmlEncode(row.Flag)}</td><td style=\"padding:2px 0\">{WebUtility.HtmlEncode(row.Says)}</td></tr>"));
        }

        return table.Append("</tbody></table>").ToString();
    }
}
