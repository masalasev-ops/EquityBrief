using EquityBrief.Web.Cloudflare;

namespace EquityBrief.Tests.Reading;

// read-surface: from the operator's correction of 2026-11-26 the read-only copy is read against the app before its upload:
// every link resolving inside the copy or kept not-held with its classes, no page holding a section its page in the app does
// not, and Home and Tonight laid out as the app lays them out. Each check is shown passing a page that stands as the app's
// and naming the page of each that does not.
// see: The read-only copy draws each page as the app draws it, and a link it does not hold keeps its element
public partial class ReadSurface
{
    static readonly HashSet<string> HeldForTheCheck = new(["#/", "#/tonight/plan/RCL"], StringComparer.Ordinal);

    [Fact]
    public void ALinkThatLosesItsClassOrOpensWhatTheCopyDoesNotHoldIsAFault()
    {
        const string Drawn = "<a class=\"pl-row on\" href=\"#/tonight/plan/RCL\">RCL</a><a class=\"g-tile\" href=\"#/paper?tab=recent\">Last 5 trading days</a>";

        // As the copy writes it: the held plan linked, the tab not held kept as its element with its class.
        Assert.Empty(SiteCopy.LinkFaults("tonight.html", Drawn, "<a class=\"pl-row on\" href=\"#/tonight/plan/RCL\">RCL</a><a class=\"g-tile not-held\">Last 5 trading days</a>", HeldForTheCheck));

        // The tab drawn as a bare span, as the copy drew it before: one link fewer.
        Assert.Equal(
            ["tonight.html carries 1 links where its page as drawn carries 2"],
            SiteCopy.LinkFaults("tonight.html", Drawn, "<a class=\"pl-row on\" href=\"#/tonight/plan/RCL\">RCL</a><span class=\"not-held\">Last 5 trading days</span>", HeldForTheCheck));

        // Its class lost, its link taken with no mark, and a link to a page the copy does not hold.
        Assert.Equal(
            ["tonight.html: link 2, \"Last 5 trading days\", lost its class g-tile"],
            SiteCopy.LinkFaults("tonight.html", Drawn, "<a class=\"pl-row on\" href=\"#/tonight/plan/RCL\">RCL</a><a class=\"not-held\">Last 5 trading days</a>", HeldForTheCheck));
        Assert.Equal(
            ["tonight.html: link 2, \"Last 5 trading days\", lost its link and is not marked not-held"],
            SiteCopy.LinkFaults("tonight.html", Drawn, "<a class=\"pl-row on\" href=\"#/tonight/plan/RCL\">RCL</a><a class=\"g-tile\">Last 5 trading days</a>", HeldForTheCheck));
        Assert.Equal(
            ["tonight.html: link 2, \"Last 5 trading days\", opens #/paper?tab=recent, which the copy does not hold"],
            SiteCopy.LinkFaults("tonight.html", Drawn, Drawn, HeldForTheCheck));
    }

    [Fact]
    public void APageHoldingASectionItsPageInTheAppDoesNotIsAFault()
    {
        const string Drawn = "<section class=\"card\" data-card=\"market-strip\"></section><section class=\"card\" data-card=\"plans\"></section>";

        Assert.Empty(SiteCopy.SectionFaults("tonight.html", Drawn, Drawn));
        Assert.Empty(SiteCopy.SectionFaults("tonight.html", Drawn, "<section class=\"card\" data-card=\"plans\"></section>"));
        Assert.Equal(
            ["home.html holds the section market-strip, which its page in the app does not hold"],
            SiteCopy.SectionFaults("home.html", "<section class=\"card\" data-card=\"plans\"></section>", Drawn));
    }

    static LaidOut Laid(double scroll = 390, IReadOnlyList<LaidBlock>? blocks = null, IReadOnlyList<LaidBlock>? sections = null, IReadOnlyList<LaidBlock>? rows = null) =>
        new(390, scroll,
            blocks ?? [new("div#identity.page-head.wrap", 40), new("main#screen.wrap", 2000)],
            sections ?? [new("market-strip", 120), new("plans", 900)],
            rows ?? [new("grid", 52), new("grid", 52)]);

    [Fact]
    public void APageLaidOutApartFromTheAppsIsAFaultNamingThePage()
    {
        const string Page = "Tonight on the S&P 500 at 390";
        var app = Laid();

        // The same page, and one whose heights stand within the few pixels allowed.
        Assert.Empty(SiteCopy.LayoutFaults(Page, app, Laid()));
        Assert.Empty(SiteCopy.LayoutFaults(Page, app, Laid(sections: [new("market-strip", 120 + SiteCopy.LayoutPixels), new("plans", 900 - SiteCopy.LayoutPixels)])));

        // The copy's own markets card atop the page, a block the app's page does not hold.
        Assert.Equal(
            [$"{Page}: the page's blocks are [div#strip.copy-strip.wrap, div#identity.page-head.wrap, main#screen.wrap] in the copy and [div#identity.page-head.wrap, main#screen.wrap] in the app"],
            SiteCopy.LayoutFaults(Page, app, Laid(blocks: [new("div#strip.copy-strip.wrap", 300), new("div#identity.page-head.wrap", 40), new("main#screen.wrap", 2000)])));

        // A section gone or out of order, and one taller than the app's past the pixels allowed.
        Assert.Equal(
            [$"{Page}: its sections are [plans, market-strip] in the copy and [market-strip, plans] in the app"],
            SiteCopy.LayoutFaults(Page, app, Laid(sections: [new("plans", 900), new("market-strip", 120)])));
        Assert.Equal(
            [$"{Page}: the section plans stands 905 px tall in the copy and 900 px in the app"],
            SiteCopy.LayoutFaults(Page, app, Laid(sections: [new("market-strip", 120), new("plans", 905)])));

        // A plan's row run into the text beside it, laid out inline, and the page wider than its window.
        Assert.Equal(
            [$"{Page}: plan row 2 is laid out as inline in the copy and as grid in the app", $"{Page}: plan row 2 stands 18 px tall in the copy and 52 px in the app"],
            SiteCopy.LayoutFaults(Page, app, Laid(rows: [new("grid", 52), new("inline", 18)])));
        Assert.Equal(
            [$"{Page}: it draws 1 plan rows in the copy and 2 in the app"],
            SiteCopy.LayoutFaults(Page, app, Laid(rows: [new("grid", 52)])));
        Assert.Equal(
            [$"{Page}: the copy's page is 412 px wide in a window 390 px wide, so it scrolls sideways"],
            SiteCopy.LayoutFaults(Page, app, Laid(scroll: 412)));
    }

    // The copy's shell runs the app's own scripts for the waiting cards and Tonight's plans, so a row of the plans opens its
    // plan and a setup's chip narrows the cards in the copy as in the app; and a waiting card drawn plain for the copy keeps its
    // row of checks empty, the checks of the night that placed it being the method.
    [Fact]
    public void TheCopysShellRunsTheAppsScriptsAndItsWaitingCardsDrawNoChecks()
    {
        var shell = SiteCopy.Shell(new DateOnly(2026, 11, 25), new Dictionary<string, string>());

        Assert.Contains(EquityBrief.Web.App.SinglePageApp.WaitingScript, shell, StringComparison.Ordinal);
        Assert.Contains(EquityBrief.Web.App.SinglePageApp.PlansScript, shell, StringComparison.Ordinal);
        Assert.Contains("carousels();", shell, StringComparison.Ordinal);

        var order = new EquityBrief.Web.Marks.WaitingOrderView("pullback", "KTOS", new DateOnly(2026, 11, 20), 61.20m, new DateOnly(2026, 12, 4), "Kratos", 64.10m);
        var app = EquityBrief.Web.Marks.MarkRenderer.WaitingCarousel([order], "S&P 400", new DateOnly(2026, 11, 25));
        var plain = EquityBrief.Web.Marks.MarkRenderer.WaitingCarousel([order], "S&P 400", new DateOnly(2026, 11, 25), plain: true);

        Assert.Contains("class=\"wo-fold\"", app, StringComparison.Ordinal);
        Assert.DoesNotContain("wo-fold", plain, StringComparison.Ordinal);
        Assert.DoesNotContain("Why it qualified", plain, StringComparison.Ordinal);
        Assert.Contains("<div class=\"wo-why\" data-row=\"checks\"></div>", plain, StringComparison.Ordinal);
    }

    // The windows the check lays the pages out in are a desktop's and a phone's at its true size and density, and the browser
    // reads the fields the check compares under their own names.
    [Fact]
    public void TheLayoutIsReadInADesktopsWindowAndATrue390()
    {
        Assert.Equal(new (string, int, int, double, bool)[] { ("1440", 1440, 900, 1d, false), ("390", 390, 844, 3d, true) }, SiteCopy.Windows);

        foreach (var field in new[] { "Width:", "ScrollWidth:", "Blocks:", "Sections:", "Rows:", "Key:", "Height:" })
        {
            Assert.Contains(field, SiteCopy.LayoutScript, StringComparison.Ordinal);
        }

        Assert.Contains(Path.Combine("/Applications", "Google Chrome.app", "Contents", "MacOS", "Google Chrome"), EquityBrief.Api.Cloudflare.CopyLayout.Browsers());
    }
}
