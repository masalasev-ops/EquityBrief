using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;

namespace EquityBrief.Worker.Loop;

// Where 21.3's read-only checks put their answers: one HTML page a check and index with its figures as JSON beside it,
// under the data root's loop folder, written over the last answer of the same check and index and read by no night, no
// page and no rule. A table is a heading, its columns and its rows, every cell already in words or figures.
// see: A read-only check changes no rule, and an exit's pricing changes only by a fill step on the operator's word
public static class CheckReports
{
    public const string Folder = "checks";

    public sealed record Table(string Heading, IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyList<string>> Rows, string? Note = null);

    static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    // The page and the figures written, their paths returned for the run's own lines.
    public static (string Report, string Figures) Write(string dataRoot, string name, string title, IReadOnlyList<string> lines, IReadOnlyList<Table> tables, object figures, DateTimeOffset at)
    {
        var folder = Path.Combine(dataRoot, MonthlyRun.Folder, Folder);

        Directory.CreateDirectory(folder);

        var report = Path.Combine(folder, name + ".html");
        var figuresFile = Path.Combine(folder, name + ".json");

        File.WriteAllText(report, Page(title, lines, tables, at), Encoding.UTF8);
        File.WriteAllText(figuresFile, JsonSerializer.Serialize(figures, Json), Encoding.UTF8);

        return (report, figuresFile);
    }

    public static string Page(string title, IReadOnlyList<string> lines, IReadOnlyList<Table> tables, DateTimeOffset at)
    {
        var page = new StringBuilder();

        page.Append("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width, initial-scale=1\"><title>")
            .Append(WebUtility.HtmlEncode(title))
            .Append("</title><style>body{font-family:system-ui,sans-serif;margin:24px;max-width:1200px}table{border-collapse:collapse;margin:12px 0 24px}th,td{border:1px solid #bbb;padding:4px 8px;text-align:right;font-variant-numeric:tabular-nums}th:first-child,td:first-child{text-align:left}caption{text-align:left;font-weight:600;padding:6px 0}p.note{color:#444}</style></head><body><h1>")
            .Append(WebUtility.HtmlEncode(title))
            .Append("</h1><p class=\"note\">Written ")
            .Append(WebUtility.HtmlEncode(at.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)))
            .Append(" UTC by a read-only check. It wrote no store row and changes no rule.</p>");

        foreach (var line in lines)
        {
            page.Append("<p>").Append(WebUtility.HtmlEncode(line)).Append("</p>");
        }

        foreach (var table in tables)
        {
            page.Append("<table><caption>").Append(WebUtility.HtmlEncode(table.Heading)).Append("</caption><tr>");

            foreach (var column in table.Columns)
            {
                page.Append("<th>").Append(WebUtility.HtmlEncode(column)).Append("</th>");
            }

            page.Append("</tr>");

            foreach (var row in table.Rows)
            {
                page.Append("<tr>");

                foreach (var cell in row)
                {
                    page.Append("<td>").Append(WebUtility.HtmlEncode(cell)).Append("</td>");
                }

                page.Append("</tr>");
            }

            page.Append("</table>");

            if (table.Note is { } note)
            {
                page.Append("<p class=\"note\">").Append(WebUtility.HtmlEncode(note)).Append("</p>");
            }
        }

        return page.Append("</body></html>").ToString();
    }

    // A figure in words for a cell: three places, a sign on a signed one, and a dash where there is none.
    public static string Cell(double? value, bool signed = false) =>
        value is { } figure ? figure.ToString(signed ? "+0.000;-0.000;0.000" : "0.000", CultureInfo.InvariantCulture) : "-";

    public static string Cell(int value) => value.ToString(CultureInfo.InvariantCulture);

    public static string Share(int part, int whole) =>
        whole == 0 ? "-" : (part * 100.0 / whole).ToString("0.0", CultureInfo.InvariantCulture) + "%";
}
