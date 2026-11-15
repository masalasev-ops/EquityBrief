using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Api.Reading;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.App;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace EquityBrief.Tests.Reading;

// From the second 23.10 correction of 2026-11-13, on the operator's answer of that day: "no read-back, sweep or test run against
// 5152 may send an approval, a rejection or any other request that writes, and a check that fails a read-back script which does".
// Every route of the read surface that writes refuses a request that names no person's browser, a script's, a command line's,
// a windowless browser's or none, with the page's own header and all, and writes nothing; and no script or skill this
// repository keeps carries the page's header, without which nothing reaches a write.
// see: A write reaches the store only from a person's press on a page, and the read surface refuses one from a script or a headless browser
public partial class ReadSurface
{
    // What sends a read-back: Python's urllib, curl, Windows PowerShell's web cmdlets, node's fetch, a headless Chrome, and a
    // client naming nothing; and the browser a person presses with.
    static readonly string?[] ScriptAgents =
    [
        "Python-urllib/3.14",
        "curl/8.9.1",
        "Mozilla/5.0 (Windows NT; Windows NT 10.0; en-US) WindowsPowerShell/5.1.26100.7019",
        "node",
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) HeadlessChrome/142.0.0.0 Safari/537.36",
        null,
    ];

    [Fact]
    public async Task EveryRouteThatWritesRefusesAScriptAWindowlessBrowserOrNothingNamedAndWritesNothing()
    {
        using var store = new TemporaryStore().Migrated();
        using var host = new Host(store.Root);
        using var page = host.CreateClient();

        // The read surface's routes that write, each its pattern with every part filled.
        var writes = host.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .OfType<RouteEndpoint>()
            .Where(endpoint => endpoint.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Any(method => !WriteGuard.Reads(method)) == true)
            .Select(endpoint => "/" + Regex.Replace(endpoint.RoutePattern.RawText!.TrimStart('/'), "\\{[^}]+\\}", "1"))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        // Sixteen, stated in advance: the passes, the quote, the analysts, the ratings, the night's resume, the withdraw, the
        // write now, the watch and unwatch, the account, the taken, not taken and exit presses, the Loop page's decide and restore,
        // and the marking page's confirm.
        Assert.Equal(16, writes.Length);

        using var bare = host.Server.CreateClient();

        foreach (var route in writes)
        {
            foreach (var agent in ScriptAgents)
            {
                using var press = new HttpRequestMessage(HttpMethod.Post, route) { Content = new FormUrlEncodedContent([new("decision", "approve"), new("proposal", "a change")]) };

                press.Headers.Add(SinglePageApp.PassHeader, SinglePageApp.PassHeaderValue);

                if (agent is not null)
                {
                    press.Headers.TryAddWithoutValidation("User-Agent", agent);
                }

                var response = await bare.SendAsync(press);

                Assert.True(response.StatusCode == HttpStatusCode.Forbidden, $"{route} from {agent ?? "nothing named"}: {response.StatusCode}");
                Assert.Contains("data-refused=\"script\"", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
            }
        }

        Assert.Equal(0L, Count(store, "loop_decision") + Count(store, "session_mark") + Count(store, "research_request") + Count(store, "taken_trade"));

        // The browser a person presses with is let through to the route, which answers as it always did.
        Assert.True(WriteGuard.FromAPersonsBrowser(PageFactory.PageAgent));
        Assert.All(ScriptAgents, agent => Assert.False(WriteGuard.FromAPersonsBrowser(agent)));
        Assert.Equal(HttpStatusCode.Forbidden, (await page.PostAsync(SinglePageApp.MarkingPostRoute, new FormUrlEncodedContent([]))).StatusCode);
        Assert.DoesNotContain("data-refused=\"script\"", await (await page.PostAsync(SinglePageApp.MarkingPostRoute, new FormUrlEncodedContent([]))).Content.ReadAsStringAsync(), StringComparison.Ordinal);

        // A page read from a script still reads.
        using var read = new HttpRequestMessage(HttpMethod.Get, "/screens/marking");

        read.Headers.TryAddWithoutValidation("User-Agent", "Python-urllib/3.14");

        Assert.Equal(HttpStatusCode.OK, (await bare.SendAsync(read)).StatusCode);
    }

    // No script or skill this repository keeps names the page's own header or its value, which every press carries, so none of
    // them can send a write the read surface would take, read over every file under tools/ and .claude/; the reader shown to find
    // a planted line.
    [Fact]
    public void NoScriptOrSkillThisRepositoryKeepsCarriesThePagesHeader()
    {
        static bool Carries(string text) =>
            text.Contains(SinglePageApp.PassHeader, StringComparison.OrdinalIgnoreCase) || text.Contains(SinglePageApp.PassHeaderValue, StringComparison.Ordinal);

        var files = new[] { "tools", ".claude" }
            .Select(folder => Path.Combine(Repository.Root, folder))
            .Where(Directory.Exists)
            .SelectMany(folder => Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
            .Where(file => !file.EndsWith(".json", StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(file).StartsWith("appsettings", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.True(files.Length >= 30, $"files read: {files.Length}");
        Assert.Empty(files.Where(file => Carries(File.ReadAllText(file))).Select(file => Path.GetRelativePath(Repository.Root, file)));
        Assert.True(Carries($"headers = {{'{SinglePageApp.PassHeader}': '{SinglePageApp.PassHeaderValue}'}}"));
        Assert.False(Carries("urllib.request.urlopen('http://localhost:5152/screens/loop')"));
    }
}
