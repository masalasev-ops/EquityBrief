using System.Net;
using System.Text;
using System.Text.Json;

namespace EquityBrief.Tests.Harness;

// Writes the two files a phase signs off against: one for the operator to read
// and one for a build session to parse.
internal static class PhaseReportWriter
{
    internal static string HtmlPath(string root) => Path.Combine(root, "artifacts", "phase-report.html");

    internal static string JsonPath(string root) => Path.Combine(root, "artifacts", "phase-report.json");

    internal static void Write(PhaseReportModel report, string root, DateTimeOffset generatedAt)
    {
        Directory.CreateDirectory(Path.Combine(root, "artifacts"));

        File.WriteAllText(JsonPath(root), Json(report, generatedAt));
        File.WriteAllText(HtmlPath(root), Html(report, generatedAt));
    }

    internal static string Json(PhaseReportModel report, DateTimeOffset generatedAt) =>
        JsonSerializer.Serialize(
            new
            {
                generatedAt = generatedAt.ToString("O"),
                green = report.Count(Verdict.Fail) == 0 && report.Count(Verdict.Unexamined) == 0,
                tables = new
                {
                    total = report.Tables.Count,
                    claimSources = report.Tables.Count(table => table.Claims > 0),
                    placedWithoutClaims = report.Tables.Count(table => table.Claims == 0),
                },
                fixture = new
                {
                    state = report.Fixture.State,
                    folders = report.Fixture.Folders,
                    note = report.Fixture.Note,
                },
                summary = new
                {
                    pass = report.Count(Verdict.Pass),
                    fail = report.Count(Verdict.Fail),
                    outOfScope = report.Count(Verdict.OutOfScope),
                    unexamined = report.Count(Verdict.Unexamined),
                },
                reconciliation = new
                {
                    placements = report.Reconciled,
                    floor = Reconciliation.Floor,
                },
                coverage = report.Coverage.Select(check => new
                {
                    check = check.Check,
                    runs = check.Runs,
                    carrier = check.Carrier,
                    reads = check.Reads,
                }),
                unsafeDuePointExceptions = report.UnsafeExceptions.Select(exception => new
                {
                    subject = exception.Subject,
                    declared = exception.Declared,
                    derived = PlanCheckpoints.DueFor(exception.Subject),
                }),
                placement = report.Tables.Select(table => new
                {
                    heading = table.Heading,
                    claims = table.Claims,
                    placement = table.Placement,
                    check = table.Check,
                    due = table.Due,
                }),
                claims = report.Claims.Select(claim => new
                {
                    table = claim.Table,
                    subject = claim.Subject,
                    verdict = Name(claim.Verdict),
                    note = claim.Note,
                    by = claim.By,
                }),
            },
            new JsonSerializerOptions { WriteIndented = true });

    internal static string Name(Verdict verdict) => verdict switch
    {
        Verdict.Pass => "PASS",
        Verdict.Fail => "FAIL",
        Verdict.OutOfScope => "OUT OF SCOPE",
        _ => "UNEXAMINED",
    };

    static string Html(PhaseReportModel report, DateTimeOffset generatedAt)
    {
        var page = new StringBuilder();

        page.Append("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\">");
        page.Append("<title>EquityBrief phase report</title><style>");
        page.Append("body{font:15px/1.5 system-ui,sans-serif;margin:2rem auto;max-width:60rem;padding:0 1rem}");
        page.Append("table{border-collapse:collapse;width:100%;margin:1rem 0}");
        page.Append("th,td{border:1px solid #ccc;padding:.35rem .5rem;text-align:left;vertical-align:top}");
        page.Append("th{background:#f2f2f2}.PASS{color:#0a6}.FAIL{color:#c00;font-weight:600}");
        page.Append(".UNEXAMINED{color:#a60}.OUTOFSCOPE{color:#666}");
        page.Append("</style></head><body>");

        page.Append("<h1>EquityBrief phase report</h1>");
        page.Append($"<p>Generated {Escape(generatedAt.ToString("u"))}. ");
        page.Append("Unexamined means a claim this phase should have been able to assert and could not. ");
        page.Append("Out of scope is counted separately and never added to it.</p>");

        page.Append("<h2>Summary</h2><table><tr><th>Verdict</th><th>Claims</th></tr>");

        foreach (var verdict in new[] { Verdict.Pass, Verdict.Fail, Verdict.OutOfScope, Verdict.Unexamined })
        {
            var name = Name(verdict);
            page.Append($"<tr><td class=\"{name.Replace(" ", string.Empty)}\">{name}</td>");
            page.Append($"<td>{report.Count(verdict)}</td></tr>");
        }

        page.Append("</table>");

        page.Append("<h2>Fixture</h2>");
        page.Append($"<p><b>{Escape(report.Fixture.State)}</b>, {report.Fixture.Folders} captured. ");
        page.Append($"{Escape(report.Fixture.Note)}</p>");

        page.Append("<h2>Reconciliation</h2>");
        page.Append($"<p><b>{report.Reconciled}</b> placements and verdicts were reconciled ");
        page.Append($"against a declared reach, against a floor of {Reconciliation.Floor} stated ");
        page.Append("in advance. A placement or a verdict naming a check whose declared reach ");
        page.Append("does not include it stops the harness, and so does a check declaring reach ");
        page.Append("over something nothing sends it.</p>");

        page.Append($"<h2>Check coverage ({report.Coverage.Count})</h2>");
        page.Append("<p>Every check the roster says runs, what carries it, and what it opens. A ");
        page.Append("roster row with nothing behind it is a property nobody keeps.</p>");
        page.Append("<table><tr><th>Check</th><th>Runs</th><th>Carried by</th><th>Reads</th></tr>");

        foreach (var check in report.Coverage)
        {
            page.Append($"<tr><td>{Escape(check.Check)}</td><td>{Escape(check.Runs)}</td>");
            page.Append($"<td>{Escape(check.Carrier)}</td><td>{Escape(check.Reads)}</td></tr>");
        }

        page.Append("</table>");

        if (report.UnsafeExceptions.Count > 0)
        {
            page.Append($"<h2>Unsafe due-point exceptions ({report.UnsafeExceptions.Count})</h2>");
            page.Append("<p>Due points are read from BUILD_PLAN's checkpoint text. These subjects ");
            page.Append("are named by the plan in a way the derivation must not take, and the value ");
            page.Append("it would take is <b>earlier</b> than the truth, which fails the day that ");
            page.Append("checkpoint lands. They are listed here every run because an exception ");
            page.Append("visible only in a source comment is one nobody sees again.</p>");
            page.Append("<table><tr><th>Subject</th><th>Plan derives</th><th>Declared</th></tr>");

            foreach (var exception in report.UnsafeExceptions)
            {
                page.Append($"<tr><td>{Escape(exception.Subject)}</td>");
                page.Append($"<td>{Escape(PlanCheckpoints.DueFor(exception.Subject) ?? "nothing")}</td>");
                page.Append($"<td>{Escape(exception.Declared)}</td></tr>");
            }

            page.Append("</table>");
        }

        page.Append("<h2>Every table in the architecture</h2>");
        page.Append("<p>A table nobody placed is a table that can go unread, so all of them are ");
        page.Append("here. A table that makes no claims names the instrument covering it instead, ");
        page.Append("or the point at which one will.</p>");
        page.Append("<table><tr><th>Heading</th><th>Claims</th><th>Placement</th>");
        page.Append("<th>Instrument</th><th>Due</th></tr>");

        foreach (var table in report.Tables)
        {
            page.Append($"<tr><td>{Escape(table.Heading)}</td><td>{table.Claims}</td>");
            page.Append($"<td>{Escape(table.Placement)}</td><td>{Escape(table.Check)}</td>");
            page.Append($"<td>{Escape(table.Due)}</td></tr>");
        }

        page.Append("</table>");

        page.Append($"<h2>Claims ({report.Claims.Count})</h2>");
        page.Append("<table><tr><th>Table</th><th>Subject</th><th>Verdict</th><th>Note</th>");
        page.Append("<th>Reached by</th></tr>");

        foreach (var claim in report.Claims)
        {
            var name = Name(claim.Verdict);
            page.Append($"<tr><td>{Escape(claim.Table)}</td><td>{Escape(claim.Subject)}</td>");
            page.Append($"<td class=\"{name.Replace(" ", string.Empty)}\">{name}</td>");
            page.Append($"<td>{Escape(claim.Note)}</td><td>{Escape(claim.By)}</td></tr>");
        }

        page.Append("</table></body></html>");

        return page.ToString();
    }

    static string Escape(string value) => WebUtility.HtmlEncode(value);
}
