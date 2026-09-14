using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Api.Reading;
using EquityBrief.Core.Configuration;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Checks;
using EquityBrief.Web.App;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;

namespace EquityBrief.Tests.Reading;

// read-surface, 6.11: the exported report, section 15.4's second surface. One name's report as
// a file: the name screen's region from the same composition and the same reads, in a document
// that needs nothing but itself to be read, drawing every mark and every figure the page draws
// and nothing the store does not hold.
public partial class ReadSurface
{
    static IReadOnlyList<string> Blocks(string html, string pattern) =>
        [.. Regex.Matches(html, pattern, RegexOptions.Singleline).Select(match => match.Value)];

    [Fact]
    public async Task TheExportedReportIsTheNameRegionInAFileThatNeedsNothingElseToBeRead()
    {
        using var store = await FixtureReplay.ResearchedAsync();
        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var page = await client.GetStringAsync("/screens/name/KEYS");
        using var response = await client.GetAsync(ReportExporter.Route + "KEYS");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType!.MediaType);

        // Offered as a download, under the name and the newest session its figures are from,
        // read off the store rather than written here.
        var newest = Rows(store, "SELECT MAX(session_date) FROM bar WHERE ticker = 'KEYS';").Single()[0];

        Assert.Equal("attachment", response.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Equal($"EquityBrief-KEYS-{newest}.html", response.Content.Headers.ContentDisposition.FileNameStar ?? response.Content.Headers.ContentDisposition.FileName!.Trim('"'));

        var file = await response.Content.ReadAsStringAsync();

        // A document of its own, with nothing a reader needs the application or the network for:
        // no script, no router's links, no form, no stylesheet or picture fetched from anywhere.
        Assert.StartsWith("<!doctype html>", file, StringComparison.Ordinal);
        Assert.Contains("data-exported=\"report\" data-ticker=\"KEYS\"", file, StringComparison.Ordinal);
        Assert.Contains($"with every figure as the store held it on the session of {newest}", file, StringComparison.Ordinal);

        foreach (var needs in new[] { "<script", "href=\"#/", "<form", "<link", " src=", "@import", "url(", SinglePageApp.PassRoute, ReportExporter.Route })
        {
            Assert.DoesNotContain(needs, file, StringComparison.OrdinalIgnoreCase);
        }

        // The page carries the link to the file, and the file does not.
        Assert.Contains(ReportExporter.Route + "KEYS", page, StringComparison.Ordinal);

        // The same pictures and the same written sections as the page, by the same composition:
        // every mark the page draws, drawn byte for byte, and every section written.
        var marks = Blocks(page, "<svg.*?</svg>");
        var sections = Blocks(page, "<section class=\"written-section\".*?</section>");

        Assert.True(marks.Count >= 4, $"The page drew {marks.Count} marks, expected at least 4.");
        Assert.True(sections.Count >= 7, $"The page drew {sections.Count} written sections, expected at least 7.");
        Assert.Equal(marks, Blocks(file, "<svg.*?</svg>"));
        Assert.Equal(sections, Blocks(file, "<section class=\"written-section\".*?</section>"));

        // Every value the file states, as a data attribute, is one the page states as often or
        // more, so the file adds no figure the page does not draw.
        var onThePage = Blocks(page, "data-[a-z-]+=\"[^\"]*\"").GroupBy(value => value).ToDictionary(group => group.Key, group => group.Count());
        var extra = Blocks(file, "data-[a-z-]+=\"[^\"]*\"")
            .Where(value => !value.StartsWith("data-exported=", StringComparison.Ordinal))
            .GroupBy(value => value)
            .Where(group => group.Count() > onThePage.GetValueOrDefault(group.Key))
            .Select(group => group.Key)
            .ToArray();

        Assert.True(extra.Length <= 1 && extra.All(value => value == "data-ticker=\"KEYS\""), $"The file states values the page does not: {string.Join(", ", extra)}");

        // And against the store directly: one candle for each stored session of the name, in
        // order, and each accepted section at the version and on the date the store holds it.
        var sessions = Rows(store, "SELECT session_date FROM bar WHERE ticker = 'KEYS' ORDER BY session_date;").Select(row => row[0]).ToArray();

        // The name's own chart, the first the region draws, ahead of the moves table's year.
        var chart = Blocks(file, "<svg[^>]*class=\"level-chart\" data-ticker=\"KEYS\".*?</svg>").First();

        Assert.Contains($"class=\"level-chart\" data-ticker=\"KEYS\" data-sessions=\"{sessions.Length}\"", chart, StringComparison.Ordinal);
        Assert.Equal(sessions, Regex.Matches(chart, "<g class=\"candle\" data-session=\"([^\"]*)\">").Select(match => match.Groups[1].Value).ToArray());

        foreach (var accepted in Rows(store, "SELECT section, as_of FROM research_section WHERE ticker = 'KEYS' AND status = 'accepted' AND section != 'The cause of each large move';"))
        {
            Assert.Contains($"data-section=\"{WebUtility.HtmlEncode(accepted[0])}\"", file, StringComparison.Ordinal);
            Assert.Contains(accepted[1], file, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void EveryDisclosureInTheFileIsOpenAndTheFileIsNamedForTheNameAndItsSession()
    {
        // Over constructed markup, since the name region carries no disclosure today and a rule
        // that has only ever met an empty case is one nobody has seen work.
        Assert.Equal(
            "<details open><summary>a</summary></details><details open class=\"b\"></details><DETAILS open></DETAILS>",
            ReportExporter.Opened("<details><summary>a</summary></details><details open class=\"b\"></details><DETAILS></DETAILS>"));

        // Without the router: a link to one of the application's routes is kept as its words, and
        // a link to a cited document is kept as a link.
        Assert.Equal(
            "<span class=\"unrouted\">previous: AAPL</span> <a href=\"https://a.test/doc\">a document</a>",
            ReportExporter.Unrouted("<a href=\"#/name/AAPL\">previous: AAPL</a> <a href=\"https://a.test/doc\">a document</a>"));

        var file = new ReportExporter().Document("KEYS", new DateOnly(2026, 9, 8), "<section class=\"name\"><details><summary>why</summary>the prose</details><a href=\"#/name/MSFT\">next: MSFT</a></section>");

        Assert.Contains("<details open><summary>why</summary>the prose</details><span class=\"unrouted\">next: MSFT</span>", file, StringComparison.Ordinal);
        Assert.Contains("<title>EquityBrief: KEYS, 2026-09-08</title>", file, StringComparison.Ordinal);
        Assert.DoesNotContain("<script", file, StringComparison.OrdinalIgnoreCase);

        Assert.Equal("EquityBrief-KEYS-2026-09-08.html", ReportExporter.FileName("KEYS", new DateOnly(2026, 9, 8)));
        Assert.Equal("EquityBrief-BRK.B.html", ReportExporter.FileName("BRK.B", null));
        Assert.Equal("EquityBrief-A_B_C.html", ReportExporter.FileName("A/B\\C", null));

        // A name that is not markup-safe is escaped wherever it is written.
        Assert.Contains("data-ticker=\"&lt;x&gt;\"", new ReportExporter().Document("<x>", null, string.Empty), StringComparison.Ordinal);
        Assert.Contains("href=\"/exports/name/A%26B\"", ReportExporter.Link("A&B"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheFileLeavesOutTheControlAndThePauseThePageDrawsAtTheSameInstant()
    {
        // The file is the report less what the application asks the operator, so each of the two
        // is asserted where the page draws it: a stale name's offer to rewrite, and a pause at a
        // cap, which one page never carries together because a paused page offers no control. The
        // test above opens both surfaces under the machine's clock over spend that reaches no cap,
        // where the page draws no pause for the file to leave out, and the 6.11 sweep's mutation
        // handing the route's spend verdict to the file survived it.
        using var store = await FixtureReplay.ResearchedAsync();

        store.Execute("UPDATE research_section SET as_of = '2026-08-01' WHERE ticker = 'KEYS';");

        using var host = new ClockedHost(store.Root, FixedClock.At(AWeekLater, SessionZones.UnitedStates));
        using var client = host.CreateClient();

        var offering = await client.GetStringAsync("/screens/name/KEYS");
        var offeredFile = await client.GetStringAsync(ReportExporter.Route + "KEYS");

        Assert.Contains("<form class=\"research-control\" method=\"post\" action=\"/passes/KEYS\" data-kind=\"rewrite\"", offering, StringComparison.Ordinal);
        Assert.Contains("<p class=\"research-cost\"", offering, StringComparison.Ordinal);
        Assert.DoesNotContain("research-control", offeredFile, StringComparison.Ordinal);
        Assert.DoesNotContain("research-cost", offeredFile, StringComparison.Ordinal);

        // The day cap reached on the day both are opened, which each surface reads per request.
        Spend(store, "research-today", "research call: The two cases", "2026-09-15T12:00:00Z", "10.00");

        var paused = await client.GetStringAsync("/screens/name/KEYS");
        var pausedFile = await client.GetStringAsync(ReportExporter.Route + "KEYS");

        Assert.Contains("<p class=\"research-paused\" data-cap=\"day\" data-resumes-at=\"2026-09-16T00:00:00Z\">", paused, StringComparison.Ordinal);
        Assert.DoesNotContain("research-paused", pausedFile, StringComparison.Ordinal);

        // Both files are still the report: the research state and every written section the page draws.
        foreach (var file in new[] { offeredFile, pausedFile })
        {
            Assert.Contains("<p class=\"research-state\" data-state=\"stale\">", file, StringComparison.Ordinal);
            Assert.Equal(Blocks(paused, "<section class=\"written-section\".*?</section>"), Blocks(file, "<section class=\"written-section\".*?</section>"));
        }
    }

    // The read surface under a clock the test fixes, so a page and its file are opened at one
    // instant against the spend the test wrote for it.
    sealed class ClockedHost(string root, IClock clock) : WebApplicationFactory<ReadApi>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseSetting(StoreLocation.DataRootKey, root);
            builder.ConfigureTestServices(services => services.AddSingleton(clock));
        }
    }
}
