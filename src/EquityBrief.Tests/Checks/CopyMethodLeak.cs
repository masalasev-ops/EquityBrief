using System.Diagnostics;
using EquityBrief.Core.Cards;
using EquityBrief.Core.Cloudflare;
using EquityBrief.Core.Providers;
using EquityBrief.Web.App;
using EquityBrief.Web.Cloudflare;
using EquityBrief.Web.Marks;
using EquityBrief.Worker.Cloudflare;

namespace EquityBrief.Tests.Checks;

// copy-method-leak, 22.17: the read-only copy carries no part of the method. Each kind of leak planted in a page is named
// by its file and its text, a clean page passes, a registration's settings are read part by part, the upload is refused
// where the read surface found a leak and goes ahead where it found none, and a pick's card drawn for the copy carries none
// of what the app's card draws of its rule.
// see: The read-only copy is published to Cloudflare Pages with no control and no part of the method, and its upload refuses a page that leaks one
public class CopyMethodLeak
{
    const string Live = "the live breakout rule at a 126-session high, 1.5 times the volume, ranges at 0.85 and the stop 1.5 typical moves beneath";

    const string Variant = "the breakout rule at a 251-session high, 1.25 times the volume, ranges at 1 and the stop 3 typical moves beneath";

    const string Index = "the live S&P 400 pullback rule at a strength of 0.75, 1 to 3 typical moves deep, the profit gate";

    const string Page = "screens/tonight.html";

    static IReadOnlyList<MethodNeedle> Needles => SiteCopy.MethodNeedles([Live, Variant, Index, "the live swing filter, version 5"], [Live, Index]);

    [Fact]
    public void ALiveRulesSettingsAreEachPartOfItsRegistrationCarryingAFigureAndThreeWords()
    {
        Assert.Equal(["the live breakout rule at a 126-session high", "1.5 times the volume", "ranges at 0.85 and the stop 1.5 typical moves beneath"], SiteCopy.SettingWords(Live));
        Assert.Equal(["the live S&P 400 pullback rule at a strength of 0.75", "1 to 3 typical moves deep"], SiteCopy.SettingWords(Index));

        // A part of two words, as short as a count plain prose carries, is no setting.
        Assert.Empty(SiteCopy.SettingWords("the live swing filter, version 5"));
    }

    [Fact]
    public void EachKindOfLeakPlantedInAPageIsNamedByItsFileAndItsTextAndACleanPagePasses()
    {
        var needles = Needles;

        Assert.Empty(SiteCopy.Leaks(Page, "<p class=\"setup-about\">A stock breaks above its recent high on heavy volume.</p><p>Buy at about 197.82, sell if it closes below 188.05.</p>", needles));

        var planted = new (string Text, string Leak)[]
        {
            ("<a href=\"#/loop?universe=400\">Loop</a>", "a Loop route: \"#/loop\""),
            ("<a href=\"#/ledger\">Ledger</a>", "a Ledger route: \"#/ledger\""),
            ($"<span title=\"{Variant}\">V1</span>", $"a rule, version or variant name: \"{Variant}\""),
            ("<span title=\"the live S&amp;P 400 pullback rule at a strength of 0.75, 1 to 3 typical moves deep, the profit gate\">Live</span>", $"a rule, version or variant name: \"{Index}\""),
            ("<td>bought on 1.5 times the volume</td>", "a live rule's setting words: \"1.5 times the volume\""),
            ("<option>Version: 1 to 5 moves down</option>", "a version's label: \"Version: \""),
            ("<p class=\"card-rule\">The setup's settings: the pullback's base</p>", "a settings line: \"settings: \""),
            ("<td>breadth 61.6% at or above its floor of 45%</td>", "a threshold's words: \"at or above\""),
            ("<td>strength 0.47 below its floor of 0.50</td>", "a threshold's words: \"floor of\""),
        };

        foreach (var (text, leak) in planted)
        {
            Assert.Contains($"{Page} carries {leak}", SiteCopy.Leaks(Page, "<section>" + text + "</section>", needles));
        }

        // A report's written prose is a company's words about its business: the threshold phrases inside it are no leak,
        // every other kind still is, and a version's label is read in its own case.
        Assert.Empty(SiteCopy.Leaks(Page, "<section class=\"written-section\"><p>revenue at or above the guided range, a multiple falling through the floor of its own range</p></section>", needles));
        Assert.Equal([$"{Page} carries a Loop route: \"#/loop\""], SiteCopy.Leaks(Page, "<section class=\"written-section\"><a href=\"#/loop\">the Loop</a></section>", needles));
        Assert.Empty(SiteCopy.Leaks(Page, "<p>The research model could not be reached for The short version: the response ended.</p>", needles));
    }

    // DD's headline, which refused the upload of 2026-11-04: a model's headline is prose as a written section is, every other
    // kind of leak in it still refuses, and a headline code wrote is read as the page is.
    // see: A headline the model wrote is prose to the copy's method check, and one code wrote is read as the page is
    [Fact]
    public void AModelsHeadlineIsProseToTheCheckAndACodeWrittenHeadlineIsReadAsThePageIs()
    {
        const string Page = "screens/name/DD.html";
        const string Guidance = "Guidance was raised, with full-year earnings and organic growth now at or above the prior high end, offsetting deteriorating reported quarters and water demand disruption.";
        var needles = Needles;
        var marks = new MarkRenderer();

        Assert.Empty(SiteCopy.Leaks(Page, marks.Lead("DD", new HeadlineView(Guidance, Written: true), null), needles));
        Assert.Equal(
            [$"{Page} carries a threshold's words: \"at or above\""],
            SiteCopy.Leaks(Page, marks.Lead("DD", new HeadlineView("Breadth at or above its setting keeps this stock on the list.", Written: false), null), needles));
        Assert.Equal(
            [$"{Page} carries a threshold's words: \"floor of\""],
            SiteCopy.Leaks(Page, marks.Lead("DD", new HeadlineView("Strength holds over its floor of the setting.", Written: false), null), needles));
        Assert.Equal(
            [$"{Page} carries a Loop route: \"#/loop\""],
            SiteCopy.Leaks(Page, marks.Lead("DD", new HeadlineView("See #/loop for this stock.", Written: true), null), needles));

        // A threshold's words beyond the headline are still read on the same page.
        Assert.Equal(
            [$"{Page} carries a threshold's words: \"at or above\""],
            SiteCopy.Leaks(Page, marks.Lead("DD", new HeadlineView(Guidance, Written: true), null) + "<td>breadth at or above 45%</td>", needles));

        // The copy's rewording leaves the model's headline as accepted and rewords code's own target close beside it.
        var worded = SiteCopy.Worded(marks.Lead("DD", new HeadlineView("Earnings came in at or above it.", Written: true), null) + "<p>a close at or above 22.13</p>");

        Assert.Contains("Earnings came in at or above it.", worded, StringComparison.Ordinal);
        Assert.Contains("a close at 22.13 or higher", worded, StringComparison.Ordinal);
        Assert.Contains("Reads at it or higher.", SiteCopy.Worded(marks.Lead("DD", new HeadlineView("Reads at or above it.", Written: false), null)), StringComparison.Ordinal);
    }

    [Fact]
    public void TheCopyHoldsNoLoopPageOrLedgerAndItsBarNamesNeither()
    {
        string[] method = [EquityBrief.Web.App.SinglePageApp.LoopRoute, EquityBrief.Web.App.SinglePageApp.LedgerRoute];

        Assert.DoesNotContain(SiteCopy.Menu, entry => method.Contains(entry.Route));
        Assert.DoesNotContain(EquityBrief.Api.Cloudflare.SiteCopyCommand.IndexScreens, screen => method.Contains(screen));
        Assert.All(method, route => Assert.Null(SiteCopy.ServerRoute(SiteCopy.Canonical(route + "?universe=400"))));
        Assert.DoesNotContain("#/loop", SiteCopy.Shell(new DateOnly(2026, 10, 30), new Dictionary<string, string>()), StringComparison.Ordinal);
        Assert.Contains(SiteCopy.NoIndex, SiteCopy.Shell(new DateOnly(2026, 10, 30), new Dictionary<string, string>()), StringComparison.Ordinal);
        Assert.Contains(SiteCopy.NoIndex, SiteCopy.Missing(new DateOnly(2026, 10, 30)), StringComparison.Ordinal);
        Assert.Contains("X-Robots-Tag: noindex, nofollow", EquityBrief.Api.Cloudflare.SiteCopyCommand.Headers, StringComparison.Ordinal);
    }

    [Fact]
    public void ATargetsCloseTheCodeWritesIsSaidInTheCopysWordsAndAReportsProseIsLeftAsWritten()
    {
        Assert.Equal("<p>sold at the next open after a close at 22.13 or higher, whichever comes first</p>", SiteCopy.Worded("<p>sold at the next open after a close at or above 22.13, whichever comes first</p>"));
        Assert.Equal("<td>A close at it or higher sells the trade at a gain</td>", SiteCopy.Worded("<td>A close at or above it sells the trade at a gain</td>"));
        Assert.Equal("<li>the open was at the target of 30.00 or higher.</li>", SiteCopy.Worded("<li>the open was at or above the target of 30.00.</li>"));

        const string Prose = "<section class=\"written-section\"><p>margins at or above 20 percent</p></section>";

        Assert.Equal("<p>a close at 9.50 or higher</p>" + Prose, SiteCopy.Worded("<p>a close at or above 9.50</p>" + Prose));
    }

    // The upload of 2026-11-17, refused for "2 times the risk" on a pick's take-profit and "at or above" in the books' column:
    // the copy says each take-profit by its dollars alone and every other multiple of the risk code wrote with no figure,
    // while the app's own line keeps every word it draws.
    // see: The read-only copy states no multiple of the risk and retries an upload only the network failed, and Home says when it was last uploaded
    [Fact]
    public void EveryMultipleOfTheRiskCodeWroteIsLeftOutOfTheCopyAndTheAppsLineKeepsIt()
    {
        var risk = new TakeProfitView(518.26m, EquityBrief.Core.Loop.TakeProfitSource.Risk, Multiple: 2, Buy: 423.57m, Stop: 376.22m) { Placing = EquityBrief.Core.Loop.TakeProfitPlacing.Risk };
        var beyond = risk with { Placing = EquityBrief.Core.Loop.TakeProfitPlacing.Beyond };
        var nearest = risk with { Placing = EquityBrief.Core.Loop.TakeProfitPlacing.Turned };

        // The app's lines, word for word as the local pages draw them.
        Assert.Equal("2 times the risk: $94.69 up for $47.35 down.", risk.From);
        Assert.Equal("No earlier high 2 times the risk or more above the buy, so it aims for 2 times the risk: $94.69 up for $47.35 down.", beyond.From);
        Assert.Equal("No earlier high above the buy, so it aims for 2 times the risk: $94.69 up for $47.35 down.", nearest.From);
        Assert.Equal("2 times the risk: $94.69 up for $47.35 down. Take-profit added at the rules restart of 16 Nov 2026.", (risk with { AddedOn = new DateOnly(2026, 11, 16) }).Line);

        // The copy's.
        Assert.Equal("<p>$94.69 up for $47.35 down. Take-profit added at the rules restart of 16 Nov 2026.</p>", SiteCopy.Worded("<p>" + (risk with { AddedOn = new DateOnly(2026, 11, 16) }).Line + "</p>"));
        Assert.Equal("<p>Half of what is left. $94.69 up for $47.35 down.</p>", SiteCopy.Worded("<p>Half of what is left. " + risk.From + "</p>"));
        Assert.Equal("<p>No earlier high far enough above the buy, so its take-profit is $94.69 up for $47.35 down.</p>", SiteCopy.Worded("<p>" + beyond.From + "</p>"));
        Assert.Equal("<p>No earlier high far enough above the buy, so its take-profit is $94.69 up for $47.35 down.</p>", SiteCopy.Worded("<p>" + nearest.From + "</p>"));
        Assert.Equal(
            "<p>Close to a buy point on 2026-09-25: one gate short, the trade gate, the reward is too small for the risk. It is not a pick.</p>",
            SiteCopy.Worded("<p>Close to a buy point on 2026-09-25: one gate short, the trade gate, the reward is 1.62 times the risk, needs 2. It is not a pick.</p>"));

        // The sale rows', the books' and the waiting cards' words for a multiple, each with no figure.
        Assert.Equal("<td>taking profit on half at a set multiple of the risk, the stop on the other half then moved up to the buy</td>", SiteCopy.Worded("<td>" + EquityBrief.Core.Families.RuleWording.TakeProfit.TakingHalf(2) + "</td>"));
        Assert.Equal("<td>taking profit at a set multiple of the risk</td>", SiteCopy.Worded("<td>" + EquityBrief.Core.Families.RuleWording.TakeProfit.Taking(2.5) + "</td>"));
        Assert.Equal("<td>taking profit at a set multiple of the risk alone</td>", SiteCopy.Worded("<td>" + EquityBrief.Core.Families.RuleWording.TakeProfit.TakingAtRisk(3) + "</td>"));
        Assert.Equal("<td>taking profit at the first price it turned down at a set multiple of the risk or more above the buy</td>", SiteCopy.Worded("<td>" + EquityBrief.Core.Families.RuleWording.TakeProfit.TakingBeyond(2) + "</td>"));
        Assert.Equal("<li>the nearer of the next price band about two typical moves up, or a set multiple of the risk</li>", SiteCopy.Worded("<li>" + EquityBrief.Core.Families.RuleWording.DriftTarget("2.5") + "</li>"));
        Assert.Equal(
            "<li>Its take profit is set with its stop: the nearest price above the buy where the stock turned down before, or a set multiple of the risk above the buy.</li>",
            SiteCopy.Worded("<li>Its take profit is set with its stop: the nearest price above the buy where the stock turned down before, or 2 times the risk above the buy.</li>"));

        // A report's written prose is left as the claim checker accepted it.
        const string Prose = "<section class=\"written-section\"><p>a payout of 2 times the risk</p></section>";

        Assert.Equal(Prose, SiteCopy.Worded(Prose));

        // The setting the upload was refused for is found in the app's line and in none of the copy's.
        var needles = SiteCopy.MethodNeedles([], ["the live pullback rule at a strength of 0.75, 2 times the risk"]);

        Assert.Equal([$"{Page} carries a live rule's setting words: \"2 times the risk\""], SiteCopy.Leaks(Page, "<p>" + risk.From + "</p>", needles));
        Assert.Empty(SiteCopy.Leaks(Page, SiteCopy.Worded("<p>" + risk.From + "</p><p>" + beyond.From + "</p>"), needles));
    }

    [Fact]
    public void AtOrAboveItsTakeProfitPriceIsSaidAsAtItsTakeProfitPriceOrHigher()
    {
        const string Column = "<th>A close under its stop, or at or above its take-profit price, sells it at that close before the next reshuffle.</th>";
        const string Sold = "<p>JLL was sold at the close of 16 Nov 2026: it closed at or above its take-profit price.</p>";

        Assert.Equal("<th>A close under its stop, or at its take-profit price or higher, sells it at that close before the next reshuffle.</th>", SiteCopy.Worded(Column));
        Assert.Equal("<p>JLL was sold at the close of 16 Nov 2026: it closed at its take-profit price or higher.</p>", SiteCopy.Worded(Sold));
        Assert.Equal([$"{Page} carries a threshold's words: \"at or above\""], SiteCopy.Leaks(Page, Column, Needles));
        Assert.Empty(SiteCopy.Leaks(Page, SiteCopy.Worded(Column + Sold), Needles));
    }

    // A worker and a read surface built beside it, as the publisher finds the surface it starts.
    static (string Worker, string Surface) Builds(string root)
    {
        var worker = Path.Combine(root, "src", "EquityBrief.Worker", "bin", "Debug", "net10.0");
        var surface = Path.Combine(root, "src", "EquityBrief.Api", "bin", "Debug", "net10.0");

        Directory.CreateDirectory(worker);
        Directory.CreateDirectory(surface);
        File.WriteAllText(Path.Combine(surface, "EquityBrief.Api.dll"), "a surface built already");

        return (worker, surface);
    }

    [Fact]
    public async Task TheUploadIsRefusedWhereTheReadSurfaceFoundTheMethodAndGoesAheadWhereItFoundNone()
    {
        using var root = new TemporaryDirectory();
        var data = Path.Combine(root.Path, "data");
        var (worker, _) = Builds(root.Path);
        var asked = 0;
        const string Leak = "publish-site: leaks the method: screens/tonight.html carries a Loop route: \"#/loop\"";

        Func<ProcessStartInfo, TimeSpan, (int, IReadOnlyList<string>)> Surface(int ending) => (start, _) =>
        {
            Directory.CreateDirectory(start.ArgumentList[2]);
            File.WriteAllText(Path.Combine(start.ArgumentList[2], "index.html"), "<p>the copy</p>");
            File.WriteAllText(Path.Combine(start.ArgumentList[2], PublishedSite.HeadersFile), "/*\n");

            return (ending, ending == PublishedSite.Leaked ? [Leak] : []);
        };

        ICloudflarePagesFeed Feed(string token, string account)
        {
            asked++;

            throw new InvalidOperationException("the upload was asked for");
        }

        var said = new List<string>();

        Assert.Equal(1, await new SitePublisher(data, worker, [], [], Surface(PublishedSite.Leaked), Feed).RunAsync(upload: true, "token", "account", said.Add));
        Assert.Equal(0, asked);
        Assert.Contains("  " + Leak, said);
        Assert.Contains("  " + SiteChecks.LeakedTheMethod, said);
        Assert.Contains("Nothing was uploaded, since the check refused it.", said);

        // The same copy with no leak found refuses nothing.
        said.Clear();

        Assert.Equal(0, await new SitePublisher(data, worker, [], [], Surface(0), Feed).RunAsync(upload: false, "token", "account", said.Add));
        Assert.Contains("Nothing the check reads refuses the upload.", said);
        Assert.DoesNotContain("  " + SiteChecks.LeakedTheMethod, said);
    }

    // A feed failing each try in turn as listed, the network's failure or Cloudflare's refusal, and going up after the list.
    sealed class TriedFeed(params Exception[] failures) : ICloudflarePagesFeed
    {
        public int Tries { get; private set; }

        public Task<string> DeployAsync(IReadOnlyList<PagesFile> files, string headers, Action<string> say) =>
            ++Tries <= failures.Length ? Task.FromException<string>(failures[Tries - 1]) : Task.FromResult("https://abc123.equitybrief.pages.dev");

        public string Scrubbed(string said) => said;
    }

    static PagesUnreachable NoHost => new("the host could not be reached: No such host is known. (api.cloudflare.com:443)", new HttpRequestException("No such host is known."));

    // The upload of 2026-11-13, lost to a name the network could not resolve: an upload the network failed is tried again after
    // each wait, four tries over half an hour, each try said; one Cloudflare answered and refused, and a copy the check refused,
    // are tried no more; and each outcome is written to the record Home reads, the last upload kept through a failure.
    // see: The read-only copy states no multiple of the risk and retries an upload only the network failed, and Home says when it was last uploaded
    [Fact]
    public async Task AnUploadTheNetworkFailedIsTriedAgainAndAnyOtherFailureIsNot()
    {
        using var root = new TemporaryDirectory();
        var data = Path.Combine(root.Path, "data");
        var (worker, _) = Builds(root.Path);
        var at = new DateTimeOffset(2026, 11, 18, 5, 58, 0, TimeSpan.Zero);
        var clock = new EquityBrief.Core.Time.FixedClock(at, EquityBrief.Core.Time.SessionZones.ResolveSessionZone(EquityBrief.Core.Time.SessionZones.UnitedStates));

        Func<ProcessStartInfo, TimeSpan, (int, IReadOnlyList<string>)> Surface(int ending) => (start, _) =>
        {
            Directory.CreateDirectory(start.ArgumentList[2]);
            File.WriteAllText(Path.Combine(start.ArgumentList[2], "index.html"), "<p>the copy</p>");
            File.WriteAllText(Path.Combine(start.ArgumentList[2], PublishedSite.HeadersFile), "/*\n");

            return (ending, ending == PublishedSite.Leaked
                ? ["publish-site: leaks the method: screens/home.html carries a live rule's setting words: \"2 times the risk\"", "publish-site: leaks the method: screens/tonight.html carries a threshold's words: \"at or above\"", "publish-site: leaks the method: screens/tonight.html carries a live rule's setting words: \"2 times the risk\""]
                : [PublishedSite.NightLine(new DateOnly(2026, 11, 17), 1200)]);
        };

        async Task<(int Outcome, List<string> Said, List<TimeSpan> Waited)> Publish(ICloudflarePagesFeed feed, int ending = 0)
        {
            var said = new List<string>();
            var waited = new List<TimeSpan>();
            var outcome = await new SitePublisher(data, worker, [], [], Surface(ending), (_, _) => feed, clock, span => { waited.Add(span); return Task.CompletedTask; })
                .RunAsync(upload: true, "token", "account", said.Add);

            return (outcome, said, waited);
        }

        // Two tries the network failed, then the upload goes up on the third.
        var twice = new TriedFeed(NoHost, new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout of 600 seconds elapsing."));
        var (outcome, said, waited) = await Publish(twice);

        Assert.Equal(0, outcome);
        Assert.Equal(3, twice.Tries);
        Assert.Equal([TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(10)], waited);
        Assert.Contains("The upload's try 1 of 4 failed: the host could not be reached: No such host is known. (api.cloudflare.com:443); trying again in 5 minutes.", said);
        Assert.Contains("The upload's try 2 of 4 failed: The request was canceled due to the configured HttpClient.Timeout of 600 seconds elapsing.; trying again in 10 minutes.", said);
        Assert.Contains("Uploaded to the Cloudflare Pages project 'equitybrief': https://abc123.equitybrief.pages.dev", said);
        Assert.Equal(new SiteUpload(at, new DateOnly(2026, 11, 17), "https://abc123.equitybrief.pages.dev", at, null), SiteUpload.Read(data));

        // Every try the network failed: four tries over half an hour, and the record keeps the last upload.
        var never = new TriedFeed(NoHost, NoHost, NoHost, NoHost);

        (outcome, said, waited) = await Publish(never);

        Assert.Equal(1, outcome);
        Assert.Equal(4, never.Tries);
        Assert.Equal(30, waited.Sum(span => span.TotalMinutes));
        Assert.Contains("The upload failed: the host could not be reached: No such host is known. (api.cloudflare.com:443), on its last try of 4.", said);
        Assert.Equal(new SiteUpload(at, new DateOnly(2026, 11, 17), "https://abc123.equitybrief.pages.dev", at, "Cloudflare could not be reached on any of 4 tries over 30 minutes"), SiteUpload.Read(data));

        // Cloudflare's own refusal is tried once.
        var refused = new TriedFeed(new InvalidOperationException("could not make the deployment: the host answered 403, 10000: Authentication error"));

        (outcome, said, waited) = await Publish(refused);

        Assert.Equal(1, outcome);
        Assert.Equal(1, refused.Tries);
        Assert.Empty(waited);
        Assert.DoesNotContain(said, line => line.StartsWith("The upload's try", StringComparison.Ordinal));
        Assert.Equal("Cloudflare refused the upload: could not make the deployment: the host answered 403, 10000: Authentication error", SiteUpload.Read(data)!.Failed);

        // A copy the check refused is never sent and never tried again.
        var unasked = new TriedFeed();

        (outcome, _, waited) = await Publish(unasked, PublishedSite.Leaked);

        Assert.Equal(1, outcome);
        Assert.Equal(0, unasked.Tries);
        Assert.Empty(waited);
        Assert.Equal(new SiteUpload(at, new DateOnly(2026, 11, 17), "https://abc123.equitybrief.pages.dev", at, "the check found words of the method on 2 pages of the copy, so nothing was sent"), SiteUpload.Read(data));

        // A run asked for no upload leaves the record as it stands.
        var before = File.ReadAllText(SiteUpload.In(data));

        Assert.Equal(0, await new SitePublisher(data, worker, [], [], Surface(0), (_, _) => new TriedFeed(), clock).RunAsync(upload: false, "token", "account", _ => { }));
        Assert.Equal(before, File.ReadAllText(SiteUpload.In(data)));
    }

    // Home's line on the app: when the copy was last uploaded and with which night, whether it is behind the page, and why the
    // newest attempt failed; and nothing where the copy's own Home is drawn.
    // see: The read-only copy states no multiple of the risk and retries an upload only the network failed, and Home says when it was last uploaded
    [Fact]
    public void HomeSaysWhenTheCopyWasLastUploadedAndWhyTheNewestAttemptFailed()
    {
        var zone = EquityBrief.Core.Time.SessionZones.ResolveSessionZone(EquityBrief.Core.Time.SessionZones.UnitedStates);
        var uploaded = new DateTimeOffset(2026, 11, 12, 5, 58, 0, TimeSpan.Zero);
        var failed = new DateTimeOffset(2026, 11, 17, 20, 48, 0, TimeSpan.Zero);
        var night = new DateOnly(2026, 11, 17);

        Assert.Equal("No upload of the public copy is recorded yet.", new SiteUploadView(null, zone).Line(night));
        Assert.Equal("none", new SiteUploadView(null, zone).State(night));

        var current = new SiteUploadView(SiteUpload.Succeeded(uploaded, new DateOnly(2026, 11, 11), "https://a.equitybrief.pages.dev"), zone);

        Assert.Equal("The public copy was last uploaded at 00:58 ET on 12 Nov 2026, with the night of 11 Nov 2026.", current.Line(new DateOnly(2026, 11, 11)));
        Assert.Equal("uploaded", current.State(new DateOnly(2026, 11, 11)));
        Assert.Equal("The public copy was last uploaded at 00:58 ET on 12 Nov 2026, with the night of 11 Nov 2026. It is behind this page, which shows the night of 17 Nov 2026.", current.Line(night));
        Assert.Equal("behind", current.State(night));

        var refused = new SiteUploadView(SiteUpload.FailedAfter(current.Record, failed, "the check found words of the method on 9 pages of the copy, so nothing was sent"), zone);

        Assert.Equal(
            "The public copy was last uploaded at 00:58 ET on 12 Nov 2026, with the night of 11 Nov 2026. It is behind this page, which shows the night of 17 Nov 2026. The newest attempt, at 15:48 ET on 17 Nov 2026, failed: the check found words of the method on 9 pages of the copy, so nothing was sent.",
            refused.Line(night));
        Assert.Equal("failed", refused.State(night));
    }

    [Fact]
    public void APicksCardDrawnForTheCopyCarriesNoneOfWhatTheAppsCardDrawsOfItsRule()
    {
        var card = new DecisionCardView(
            "GSPC",
            new DateOnly(2026, 10, 30),
            "pullback",
            "WDAY",
            Live,
            197.82m,
            188.05m,
            227.49m,
            [new CardLineView(1, CardLines.TrendName, "tick", "Trend and strength: the market check, breadth 61.6% at or above its floor of 45%; the trend, an uptrend")],
            new CardRecordView(Live, 1891, 0.37, 0.21, "risks", 7, 15, 0.75, -0.4, new DateOnly(2019, 1, 2), new DateOnly(2026, 10, 15), "as it stood"));

        var app = new MarkRenderer().DecisionCard(card);
        var copy = new MarkRenderer { Copying = true }.DecisionCard(card);

        Assert.NotEmpty(SiteCopy.Leaks(Page, app, Needles));
        Assert.Empty(SiteCopy.Leaks(Page, copy, Needles));
        Assert.Contains("<p class=\"card-summary\" data-summary>Picked by the pullback on 30 Oct 2026 and bought at the next trading day's open, at about 197.82.", copy, StringComparison.Ordinal);
        Assert.Equal(3, System.Text.RegularExpressions.Regex.Matches(copy, "class=\"plan-tile\"").Count);
        Assert.DoesNotContain("card-why", copy, StringComparison.Ordinal);
        Assert.DoesNotContain("card-record", copy, StringComparison.Ordinal);
    }
}
