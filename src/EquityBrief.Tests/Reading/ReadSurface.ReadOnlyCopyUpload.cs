using System.Diagnostics;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using EquityBrief.Core.Cards;
using EquityBrief.Core.Cloudflare;
using EquityBrief.Tests.Checks;
using EquityBrief.Worker.Cloudflare;

namespace EquityBrief.Tests.Reading;

// read-surface: the read-only copy's folder checked and uploaded. Every file read for forms, the surface's addresses, the
// press header, scripts, secrets, paths and the account's settings before anything leaves the machine; the folder sent to
// its own Cloudflare Pages project by direct upload, keyed by the BLAKE3 hash the host's own tool keys a file by, with the
// token in a header alone; and the verb writing the copy through the read surface's build, its report beside the folder.
// see: A read-only copy of the site is published to Cloudflare Pages from the read surface's own screens and holds no control
public partial class ReadSurface
{
    // BLAKE3's published test inputs, each byte its position modulo 251, and their hashes, with the empty input's and
    // "abc"'s, read from the function's own reference and from the host's tool's module, which agree.
    [Theory]
    [InlineData(0, "af1349b9f5f9a1a6a0404dea36dcc9499bcb25c9adc112b7cc9a93cae41f3262")]
    [InlineData(1, "2d3adedff11b61f14c886e35afa036736dcd87a74d27b5c1510225d0f592e213")]
    [InlineData(63, "e9bc37a594daad83be9470df7f7b3798297c3d834ce80ba85d6e207627b7db7b")]
    [InlineData(64, "4eed7141ea4a5cd4b788606bd23f46e212af9cacebacdc7d1f4c6dc7f2511b98")]
    [InlineData(65, "de1e5fa0be70df6d2be8fffd0e99ceaa8eb6e8c93a63f2d8d1c30ecb6b263dee")]
    [InlineData(1023, "10108970eeda3eb932baac1428c7a2163b0e924c9a9e25b35bba72b28f70bd11")]
    [InlineData(1024, "42214739f095a406f3fc83deb889744ac00df831c10daa55189b5d121c855af7")]
    [InlineData(1025, "d00278ae47eb27b34faecf67b4fe263f82d5412916c1ffd97c8cb7fb814b8444")]
    [InlineData(2048, "e776b6028c7cd22a4d0ba182a8bf62205d2ef576467e838ed6f2529b85fba24a")]
    [InlineData(2049, "5f4d72f40d7a5f82b15ca2b2e44b1de3c2ef86c426c95c1af0b6879522563030")]
    [InlineData(3000, "5fade288bf27444bee55ba2babb98c3c922c1e84c2e445e7d1f6da24756f5060")]
    public void TheCopysFilesAreKeyedByBlake3AsTheHostsToolKeysThem(int length, string hash)
    {
        var input = Enumerable.Range(0, length).Select(at => (byte)(at % 251)).ToArray();

        Assert.Equal(hash, Convert.ToHexStringLower(Blake3.Hash(input)));
    }

    [Fact]
    public void AFilesKeyIsTheHashOfItsBase64AndItsExtensionCutToThirtyTwoDigits()
    {
        Assert.Equal("6437b3ac38465133ffb63b75273a8db548c558465d79db03fd359c6cd5bd9d85", Convert.ToHexStringLower(Blake3.Hash("abc"u8)));

        // The key the host's own tool gives a page of these bytes, read off its module.
        Assert.Equal("94e5cce432e666735e8263a075eecd7c", CloudflarePages.HashOf("<p>EquityBrief</p>"u8.ToArray(), "/screens/x.html"));
        Assert.NotEqual(CloudflarePages.HashOf("<p>EquityBrief</p>"u8.ToArray(), "/screens/x.html"), CloudflarePages.HashOf("<p>EquityBrief</p>"u8.ToArray(), "/screens/x.txt"));
    }

    [Fact]
    public void TheFilesAreSentInBucketsOfFortyMegabytesAndAThousandFilesAtMost()
    {
        PagesFile Of(int bytes, int at) => new($"/{at}.html", at.ToString("x32", System.Globalization.CultureInfo.InvariantCulture), "text/html", new byte[bytes]);

        var large = CloudflarePages.Buckets([Of(30 << 20, 1), Of(30 << 20, 2), Of(10 << 20, 3)]);

        Assert.Equal([2, 1], large.Select(bucket => bucket.Count));
        Assert.All(large, bucket => Assert.True(bucket.Sum(file => (long)file.Bytes.Length) <= CloudflarePages.BucketBytes));

        var many = CloudflarePages.Buckets([.. Enumerable.Range(0, 2500).Select(at => Of(10, at))]);

        Assert.Equal([1000, 1000, 500], many.Select(bucket => bucket.Count));
    }

    // The host, answered here: each request recorded with its path, its header and its body, the project missing until it
    // is made, and the check of missing files answering the hashes it is told it lacks.
    sealed class PagesHost(IReadOnlySet<string> lacking, bool projectExists = false, string? failOn = null) : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Path, string? Bearer, string Body)> Asked { get; } = [];

        bool made = projectExists;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? string.Empty : await request.Content.ReadAsStringAsync(cancellationToken);
            var path = request.RequestUri!.AbsolutePath;

            Asked.Add((request.Method, path, request.Headers.Authorization?.Parameter, body));

            if (failOn is not null && path.EndsWith(failOn, StringComparison.Ordinal))
            {
                return Answer(HttpStatusCode.BadRequest, new JsonObject { ["success"] = false, ["errors"] = new JsonArray(new JsonObject { ["code"] = 8000000, ["message"] = "refused for " + request.Headers.Authorization?.Parameter }) });
            }

            JsonNode? result = path switch
            {
                _ when path.EndsWith("/pages/projects/equitybrief", StringComparison.Ordinal) && request.Method == HttpMethod.Get => made ? new JsonObject { ["name"] = "equitybrief" } : null,
                _ when path.EndsWith("/pages/projects", StringComparison.Ordinal) => Made(),
                _ when path.EndsWith("/upload-token", StringComparison.Ordinal) => new JsonObject { ["jwt"] = "the-upload-jwt" },
                _ when path.EndsWith("/check-missing", StringComparison.Ordinal) => new JsonArray([.. JsonNode.Parse(body)!["hashes"]!.AsArray().Select(hash => hash!.GetValue<string>()).Where(lacking.Contains).Select(hash => (JsonNode)hash)]),
                _ when path.EndsWith("/deployments", StringComparison.Ordinal) => new JsonObject { ["url"] = "https://abc.equitybrief.pages.dev" },
                _ => new JsonObject(),
            };

            return result is null && request.Method == HttpMethod.Get && path.EndsWith("/equitybrief", StringComparison.Ordinal)
                ? new HttpResponseMessage(HttpStatusCode.NotFound) { Content = new StringContent("{\"success\":false}") }
                : Answer(HttpStatusCode.OK, new JsonObject { ["success"] = true, ["result"] = result });
        }

        JsonNode Made()
        {
            made = true;

            return new JsonObject { ["subdomain"] = "equitybrief.pages.dev" };
        }

        static HttpResponseMessage Answer(HttpStatusCode status, JsonNode body) => new(status) { Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json") };
    }

    [Fact]
    public async Task TheCopyIsUploadedToItsOwnProjectSendingOnlyTheFilesTheHostLacksWithTheTokenInAHeaderAlone()
    {
        const string Token = "a-cloudflare-token-for-the-suite";
        const string Account = "0123456789abcdef0123456789abcdef";
        PagesFile[] files = [new("/index.html", "a".PadLeft(32, '0'), "text/html", "shell"u8.ToArray()), new("/screens/tonight.html", "b".PadLeft(32, '0'), "text/html", "tonight"u8.ToArray())];
        var host = new PagesHost(new HashSet<string>([files[1].Hash], StringComparer.Ordinal));
        var said = new List<string>();

        var deployed = await new CloudflarePages(new HttpClient(host), Token, Account, PublishedSite.Project).DeployAsync(files, "/*\n  X-Robots-Tag: noindex\n", said.Add);

        Assert.Equal("https://abc.equitybrief.pages.dev", deployed);

        // The project read, made where missing on its production branch, the token asked for, the missing files asked,
        // the one lacking sent, every hash confirmed and the deployment made, in that order.
        Assert.Equal(
            [
                "GET /client/v4/accounts/" + Account + "/pages/projects/equitybrief",
                "POST /client/v4/accounts/" + Account + "/pages/projects",
                "GET /client/v4/accounts/" + Account + "/pages/projects/equitybrief/upload-token",
                "POST /client/v4/pages/assets/check-missing",
                "POST /client/v4/pages/assets/upload",
                "POST /client/v4/pages/assets/upsert-hashes",
                "POST /client/v4/accounts/" + Account + "/pages/projects/equitybrief/deployments",
            ],
            host.Asked.Select(asked => asked.Method + " " + asked.Path));
        Assert.Contains("\"production_branch\":\"main\"", host.Asked[1].Body, StringComparison.Ordinal);

        // The token goes to the API's own calls and the upload's JWT to the asset calls, and neither is in a body.
        Assert.Equal([Token, Token, Token, "the-upload-jwt", "the-upload-jwt", "the-upload-jwt", Token], host.Asked.Select(asked => asked.Bearer));
        Assert.All(host.Asked, asked => Assert.DoesNotContain(Token, asked.Body, StringComparison.Ordinal));

        var sent = JsonNode.Parse(host.Asked[4].Body)!.AsArray();

        Assert.Equal(files[1].Hash, Assert.Single(sent)!["key"]!.GetValue<string>());
        Assert.Equal(Convert.ToBase64String(files[1].Bytes), sent[0]!["value"]!.GetValue<string>());
        Assert.Matches("name=\"?manifest\"?", host.Asked[6].Body);
        Assert.Contains(JsonSerializer.Serialize(files.ToDictionary(file => file.Address, file => file.Hash)), host.Asked[6].Body, StringComparison.Ordinal);
        Assert.Matches("name=\"?_headers\"?", host.Asked[6].Body);
        Assert.Contains("X-Robots-Tag: noindex", host.Asked[6].Body, StringComparison.Ordinal);
        Assert.Contains("2 files, 1 the host lacks", Assert.Single(said, line => line.Contains("lacks", StringComparison.Ordinal)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task NoUploadNamesTheOperatorsOtherSiteAndAFailureIsSaidWithTheTokenTakenOut()
    {
        const string Token = "a-cloudflare-token-for-the-suite";
        var never = new PagesHost(new HashSet<string>(StringComparer.Ordinal));

        await Assert.ThrowsAsync<InvalidOperationException>(() => new CloudflarePages(new HttpClient(never), Token, "account", PublishedSite.NeverProject).DeployAsync([], string.Empty, _ => { }));
        Assert.Empty(never.Asked);

        var failing = new PagesHost(new HashSet<string>(StringComparer.Ordinal), projectExists: true, failOn: "/upload-token");
        var pages = new CloudflarePages(new HttpClient(failing), Token, "account", PublishedSite.Project);
        var failed = await Assert.ThrowsAsync<InvalidOperationException>(() => pages.DeployAsync([], string.Empty, _ => { }));

        Assert.Contains("could not ask for an upload token: the host answered 400, 8000000: refused for (withheld)", failed.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Token, failed.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheFoldersCheckRefusesAFormAPressAScriptASecretAPathAndTheAccountNamingEachFileAndNoValue()
    {
        using var folder = new TemporaryDirectory();
        const string Secret = "the-suites-secret-value";
        var secrets = Path.Combine(folder.Path, "secrets.json");
        var site = Path.Combine(folder.Path, "site");

        Directory.CreateDirectory(Path.Combine(site, "screens"));
        File.WriteAllText(secrets, "{ \"EquityBrief\": { \"Providers\": { \"Some\": { \"ApiKey\": \"" + Secret + "\" } } } // a comment the settings reader skips\n}");
        File.WriteAllText(Path.Combine(site, "clean.html"), "<p data-ticker=\"ROP\">ROP</p><a href=\"https://www.sec.gov/x\">source</a>");

        var needles = SiteChecks.Needles([secrets], [@"E:\Somewhere  Odd\data"], folder.Path);
        var clean = SiteChecks.Read(site, needles);

        Assert.Empty(clean.Refusals);
        Assert.Contains("  data-ticker  1", clean.Lines);
        Assert.Contains("  www.sec.gov  1", clean.Lines);

        File.WriteAllText(Path.Combine(site, "screens", "form.html"), "<form class=\"x\" action=\"/passes/ROP\"><button>ask</button></form>");
        File.WriteAllText(Path.Combine(site, "screens", "route.html"), "<section data-quote-route=\"/quotes/ROP\"></section>");
        File.WriteAllText(Path.Combine(site, "screens", "script.html"), "<script src=\"https://cdn.example/x.js\"></script><script>fetch('https://elsewhere.example/a', { method: 'POST' });</script>");
        File.WriteAllText(Path.Combine(site, "screens", "secret.html"), "<p>" + Secret + "</p>\n<p>E:/Somewhere  Odd/data/equitybrief.db</p>\n<p>riskPercent</p>\n<p>X-EquityBrief-Pass</p>");
        File.WriteAllText(Path.Combine(site, "screens", "card.html"), "<p class=\"card-money\" data-account=\"set\">37 shares, 99.90 at risk</p>");

        var refused = SiteChecks.Read(site, needles);

        foreach (var reason in new[]
        {
            "screens/form.html carries a form",
            "screens/form.html carries action=\"/passes/ROP\", an address of the read surface",
            "screens/route.html carries data-quote-route=\"/quotes/ROP\", an address of the read surface",
            "screens/script.html loads a script from https://cdn.example/x.js",
            "screens/script.html holds a script that asks another site or sends a request",
            "screens/secret.html:1 carries the value of EquityBrief:Providers:Some:ApiKey",
            "screens/secret.html:2 carries an absolute path of this machine",
            "screens/secret.html:3 carries an account setting's name",
            "screens/secret.html carries the page's press header",
            "screens/card.html carries a card's plan sized on the account",
        })
        {
            Assert.Contains(refused.Refusals, refusal => refusal.StartsWith(reason, StringComparison.Ordinal));
        }

        // The secret is named by its key and never printed.
        Assert.DoesNotContain(refused.Refusals.Concat(refused.Lines), line => line.Contains(Secret, StringComparison.Ordinal));

        // The account's own size where it is kept and is not the example's.
        AccountFile.Write(folder.Path, new AccountSettings(123457m, 0.73m, 0.19m));
        File.WriteAllText(Path.Combine(site, "screens", "sized.html"), "<p>On an account of 123,457</p>");

        Assert.Contains(SiteChecks.Read(site, SiteChecks.Needles([], [], folder.Path)).Refusals, refusal => refusal.StartsWith("screens/sized.html:1 carries the account's size", StringComparison.Ordinal));
    }

    [Fact]
    public void EveryAddressThePageSendsAPressOrAFetchToIsOneTheCheckRefuses()
    {
        string[] sent =
        [
            EquityBrief.Web.App.SinglePageApp.PassRoute, EquityBrief.Web.App.SinglePageApp.WithdrawRoute, EquityBrief.Web.App.SinglePageApp.WriteNowRoute,
            EquityBrief.Web.App.SinglePageApp.QuoteRoute, EquityBrief.Web.App.SinglePageApp.AnalystsRoute, EquityBrief.Web.App.SinglePageApp.RatingsRoute,
            EquityBrief.Web.App.SinglePageApp.WatchPostRoute, EquityBrief.Web.App.SinglePageApp.UnwatchPostRoute, EquityBrief.Web.App.SinglePageApp.AccountPostRoute,
            EquityBrief.Web.App.SinglePageApp.TakenPostRoute, EquityBrief.Web.App.SinglePageApp.NotTakenPostRoute, EquityBrief.Web.App.SinglePageApp.ExitPostRoute,
            EquityBrief.Web.App.SinglePageApp.NightResumeRoute, EquityBrief.Web.App.SinglePageApp.LoopDecidePostRoute, EquityBrief.Web.App.SinglePageApp.LoopRestorePostRoute,
            EquityBrief.Web.App.SinglePageApp.LoopPutRoute, EquityBrief.Web.App.ReportExporter.Route, EquityBrief.Api.Reading.ReportProof.Route, EquityBrief.Core.Sweep.SweepFolder.Route,
        ];

        Assert.All(sent, address => Assert.Contains(SiteChecks.SurfaceAddresses, prefix => address.StartsWith(prefix, StringComparison.Ordinal)));
    }

    // A worker build and a read surface build laid out as a checkout holds them, under a temporary folder.
    static (string Worker, string Surface) Builds(string root)
    {
        var worker = Path.Combine(root, "src", "EquityBrief.Worker", "bin", "Release", "net10.0");
        var surface = Path.Combine(root, "src", "EquityBrief.Api", "bin", "Release", "net10.0");

        Directory.CreateDirectory(worker);
        Directory.CreateDirectory(surface);
        File.WriteAllText(Path.Combine(surface, "EquityBrief.Api.dll"), "a surface built already");

        return (worker, surface);
    }

    [Fact]
    public async Task TheVerbRewritesItsOwnFolderWritesItsReportBesideItAndUploadsOnlyWhenAskedWithTheTokenSet()
    {
        using var root = new TemporaryDirectory();
        var data = Path.Combine(root.Path, "data");
        var (worker, surface) = Builds(root.Path);
        var stale = Path.Combine(SitePublisher.FolderIn(data), "stale.html");
        var started = new List<ProcessStartInfo>();

        Directory.CreateDirectory(Path.GetDirectoryName(stale)!);
        File.WriteAllText(stale, "an earlier copy's page");
        File.WriteAllText(Path.Combine(data, "equitybrief.db"), "the store");

        (int, IReadOnlyList<string>) Surface(ProcessStartInfo start, TimeSpan most)
        {
            started.Add(start);
            Directory.CreateDirectory(start.ArgumentList[2]);
            File.WriteAllText(Path.Combine(start.ArgumentList[2], "index.html"), "<p>the copy</p>");
            File.WriteAllText(Path.Combine(start.ArgumentList[2], PublishedSite.HeadersFile), "/*\n");

            return (0, ["publish-site: night 2026-10-27, 1 screens held"]);
        }

        var host = new PagesHost(new HashSet<string>(StringComparer.Ordinal), projectExists: true);
        var publisher = new SitePublisher(data, worker, [], [], Surface, () => new HttpClient(host));
        var said = new List<string>();

        // Written and read, and nothing sent: the folder written again whole, the surface beside the worker started with
        // the copy's verb and the folder, the store named in its environment, and the report beside the folder.
        Assert.Equal(0, await publisher.RunAsync(upload: false, "token", "account", said.Add));
        Assert.Equal(surface, SitePublisher.SurfaceBuildBeside(worker));
        Assert.False(File.Exists(stale));
        Assert.Equal([Path.Combine(surface, "EquityBrief.Api.dll"), PublishedSite.Verb, SitePublisher.FolderIn(data)], started[0].ArgumentList);
        Assert.Equal(data, started[0].Environment["EquityBrief__DataRoot"]);
        Assert.Equal("the store", File.ReadAllText(Path.Combine(data, "equitybrief.db")));
        Assert.Contains("Nothing was uploaded: the copy is written and read, and --upload uploads it once this report is read.", File.ReadAllText(SitePublisher.ReportIn(data)), StringComparison.Ordinal);
        Assert.False(File.Exists(Path.Combine(SitePublisher.FolderIn(data), PublishedSite.ReportFile)));
        Assert.Empty(host.Asked);

        // Asked to upload with no token set: nothing sent, and said.
        Assert.Equal(0, await publisher.RunAsync(upload: true, null, "account", said.Add));
        Assert.Contains($"Nothing was uploaded: {PublishedSite.TokenKey} is not set in the secrets file.", said);
        Assert.Empty(host.Asked);

        // With both set the folder is sent, the headers file beside the deployment and not as a page.
        Assert.Equal(0, await publisher.RunAsync(upload: true, "token", "account", said.Add));
        Assert.Contains(host.Asked, asked => asked.Path.EndsWith("/deployments", StringComparison.Ordinal) && asked.Body.Contains("\"/index.html\"", StringComparison.Ordinal) && !asked.Body.Contains("\"/_headers\"", StringComparison.Ordinal));

        // A copy the check refuses is not sent, and the verb ends in a failure.
        var refusing = new SitePublisher(data, worker, [], [], (start, most) =>
        {
            Directory.CreateDirectory(start.ArgumentList[2]);
            File.WriteAllText(Path.Combine(start.ArgumentList[2], "index.html"), "<form action=\"/passes/X\"></form>");

            return (0, []);
        }, () => new HttpClient(host));
        var asked = host.Asked.Count;

        Assert.Equal(1, await refusing.RunAsync(upload: true, "token", "account", said.Add));
        Assert.Equal(asked, host.Asked.Count);
        Assert.Contains("Nothing was uploaded, since the check refused it.", said);
    }

    [Fact]
    public async Task TheReadSurfacesOwnBuildWritesTheCopyWithNoAccountSettingNoControlAndNoWriteToTheStore()
    {
        using var store = await FixtureReplay.ResearchedAsync();
        var worker = Path.Combine(Repository.Root, "src", "EquityBrief.Worker", "bin", Repository.Configuration, Repository.Framework);

        // Settings no page draws without reading them, so a copy carrying either read them.
        AccountFile.Write(store.Root, new AccountSettings(123457m, 0.73m, 0.19m));

        var before = StoreBytesHash(store.DatabaseFile);
        var said = new List<string>();
        var ended = await new SitePublisher(store.Root, worker, [], [store.Root]).RunAsync(upload: false, null, null, said.Add);

        Assert.True(ended == 0, string.Join('\n', said));
        Assert.Contains("Nothing the check reads refuses the upload.", said);
        Assert.Equal(before, StoreBytesHash(store.DatabaseFile));

        var folder = SitePublisher.FolderIn(store.Root);
        var pages = Directory.EnumerateFiles(folder, "*.html", SearchOption.AllDirectories).Select(File.ReadAllText).ToArray();

        Assert.True(pages.Length >= 20, $"The copy holds {pages.Length} pages, expected at least 20.");
        Assert.All(pages, page =>
        {
            Assert.DoesNotContain("123,457", page, StringComparison.Ordinal);
            Assert.DoesNotContain("123457", page, StringComparison.Ordinal);
            Assert.DoesNotContain("data-account=\"set\"", page, StringComparison.Ordinal);
            Assert.DoesNotContain("<form", page, StringComparison.Ordinal);
        });
        Assert.Contains(PublishedSite.Name, File.ReadAllText(Path.Combine(folder, "index.html")), StringComparison.Ordinal);
    }
}
