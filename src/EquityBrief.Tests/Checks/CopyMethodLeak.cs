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
