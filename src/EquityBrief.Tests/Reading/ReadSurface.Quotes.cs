using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using EquityBrief.Api.Reading;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Checks;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.App;
using EquityBrief.Web.Marks;
using EquityBrief.Worker.Quotes;

namespace EquityBrief.Tests.Reading;

// read-surface, 18.1: the masthead draws the delayed quote the quote job stored for the session the page is opened in,
// with its own time, its change on the previous close and that it is delayed, and the last stored close outside the
// session; a press asking for a quote is refused without the page's own header and outside the session and writes
// nothing inside the interval of the name's newest ask; the newest quote is read back with the tiles and each band's
// distance at its price; the headline carries no figure; the contents' written and filed entries carry their days for
// the page's script to mark new; and the Run page states the session's quotes against the cap.
// see: The name page draws a delayed quote in the regular session, asked by a worker job at most every five minutes under a day's cap
// see: The masthead carries the index and sector beside the price now, the delayed quote in the session and the last stored close outside it
public partial class ReadSurface
{
    // The rows 18.1 adds that this check reaches: the range bar among the marks, the name page's eight parts, the Run
    // page's quote runs and the interval between two asks.
    internal static string[] NamePageRows =>
    [
        CheckReach.Key("15.5 The mark vocabulary", "Range bar"),
        CheckReach.Key("15.9 Name", "Masthead"),
        CheckReach.Key("15.9 Name", "The quote while the page is open"),
        CheckReach.Key("15.9 Name", "Headline"),
        CheckReach.Key("15.9 Name", "Tiles"),
        CheckReach.Key("15.9 Name", "How the rules read it"),
        CheckReach.Key("15.9 Name", "The decision card"),
        CheckReach.Key("15.9 Name", "Reading column"),
        CheckReach.Key("15.9 Name", "Rules by role"),
        CheckReach.Key("15.10 Run", "The quote runs"),
        CheckReach.Key(Scope.LimitsTable, "Quote interval"),
    ];

    // Wednesday 2026-10-07: 14:58Z is 10:58 in New York, inside the session, and 20:00Z its close.
    static readonly DateTimeOffset InTheSession = new(2026, 10, 7, 14, 58, 0, TimeSpan.Zero);

    static readonly DateTimeOffset AfterTheClose = new(2026, 10, 7, 20, 0, 0, TimeSpan.Zero);

    static void StoreQuote(TemporaryStore store, string ticker, string askedAt, string night, string distances = "[]") =>
        store.Execute(
            "INSERT INTO live_quote (ticker, asked_at, answered_at, quoted_at, price, previous_close, change, change_pct, night, distances) VALUES " +
            $"('{ticker}', '{askedAt}', '{askedAt}', '2026-10-07T14:43:00Z', '231.40', '229.90', '1.50', 0.652, '{night}', '{distances}');");

    [Fact]
    public async Task AQuoteIsDrawnInTheSessionWithItsTimeAndTheLastCloseOutsideIt()
    {
        using var store = await FixtureExpectations.WithListings();

        var night = NightIn(store);
        var name = FiredNamesOn(store, night)[0];

        // The quote's distance for one of the name's own bands, a figure no close gives it.
        var band = Rows(store, $"SELECT low_edge, high_edge FROM level WHERE ticker = '{name}' AND as_of = (SELECT MAX(as_of) FROM level WHERE ticker = '{name}') ORDER BY low_edge LIMIT 1;").Single();
        var atTheQuote = $"<td class=\"away num\" data-away=\"7.7\">";

        StoreQuote(store, name, "2026-10-07T14:55:00Z", night, $"[{{\"low\":\"{band[0]}\",\"high\":\"{band[1]}\",\"role\":\"support\",\"days\":7.7}}]");

        // Inside the session: the quote, marked as such, its change on the previous close in the rise's hue with its
        // sign, and beneath it its own time in New York, that it is delayed, the previous close and the session the
        // levels and the plan are read through; and the route and the session's edges the page's script asks by.
        using (var host = new PassHost(store.Root) { Clock = FixedClock.At(InTheSession, SessionZones.UnitedStates) })
        {
            using var client = host.CreateClient();
            var page = await client.GetStringAsync($"/screens/name/{name}");

            Assert.Contains(
                "<span class=\"m-price\" data-live=\"yes\"><span class=\"pill live\">LIVE</span><span class=\"m-px\" data-price=\"231.40\">231.40</span><span class=\"m-chg up\" data-change=\"1.50\">+1.50 (+0.65%)</span></span>",
                page,
                StringComparison.Ordinal);
            Assert.Contains(
                $"<span class=\"m-when\">Quote of 10:43 ET, delayed about 15 minutes. Previous close <span data-previous-close=\"229.90\">229.90</span>. Levels and the plan come from completed sessions to {night}</span>",
                page,
                StringComparison.Ordinal);
            Assert.Contains(
                $"data-quote-route=\"{SinglePageApp.QuoteRoute}{name}\" data-session-open=\"2026-10-07T13:30:00Z\" data-session-close=\"2026-10-07T20:00:00Z\" data-interval-minutes=\"5\" data-asked=\"2026-10-07T14:55:00Z\"",
                page,
                StringComparison.Ordinal);
            Assert.Contains("<div class=\"lead-tiles\" data-live=\"yes\">", page, StringComparison.Ordinal);

            // Each band's distance is the one the quote job stored at the quote's price, from the page's first drawing.
            Assert.Contains(atTheQuote, page, StringComparison.Ordinal);

            // A page about an earlier night and the exported file draw the close and carry no route, whatever the clock.
            var earlier = await client.GetStringAsync($"/screens/name/{name}/{night}");

            Assert.DoesNotContain("data-quote-route", earlier, StringComparison.Ordinal);
            Assert.Contains("<span class=\"m-price\" data-live=\"no\">", earlier, StringComparison.Ordinal);
            Assert.DoesNotContain("data-quote-route", await client.GetStringAsync(ReportExporter.Route + name), StringComparison.Ordinal);
        }

        // Each host below starts at an instant of its own, since a start of the surface writes a row keyed on its instant.
        // After the close: the last stored close and its session, with nothing marked live; the route stays, and the
        // script asks nothing outside the session's edges.
        using (var host = new PassHost(store.Root) { Clock = FixedClock.At(AfterTheClose, SessionZones.UnitedStates) })
        {
            using var client = host.CreateClient();
            var page = await client.GetStringAsync($"/screens/name/{name}");

            Assert.Contains("<span class=\"m-price\" data-live=\"no\"><span class=\"m-px\">", page, StringComparison.Ordinal);
            Assert.DoesNotContain("LIVE", page, StringComparison.Ordinal);
            Assert.Contains($"<span class=\"m-when\">As of the close of {night}, the last stored price. Levels and the plan come from completed sessions to {night}</span>", page, StringComparison.Ordinal);
            Assert.Contains("<div class=\"lead-tiles\" data-live=\"no\">", page, StringComparison.Ordinal);
            Assert.DoesNotContain(atTheQuote, page, StringComparison.Ordinal);
        }

        // A Saturday holds no session, so the page carries no route and asks for nothing.
        using (var host = new PassHost(store.Root) { Clock = FixedClock.At(new DateTimeOffset(2026, 10, 10, 15, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates) })
        {
            using var client = host.CreateClient();

            Assert.DoesNotContain("data-quote-route", await client.GetStringAsync($"/screens/name/{name}"), StringComparison.Ordinal);
        }

        // A quote asked in another session is not this one's, and the page draws the close.
        store.Execute("UPDATE live_quote SET asked_at = '2026-10-06T14:55:00Z';");

        using (var host = new PassHost(store.Root) { Clock = FixedClock.At(InTheSession.AddSeconds(1), SessionZones.UnitedStates) })
        {
            using var client = host.CreateClient();

            Assert.Contains("<span class=\"m-price\" data-live=\"no\">", await client.GetStringAsync($"/screens/name/{name}"), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task AQuotePressIsRefusedWithoutThePagesHeaderAndOutsideTheSessionAndInsideTheIntervalAsksNothing()
    {
        using var store = await FixtureExpectations.WithListings();

        var name = FiredNamesOn(store, NightIn(store))[0];
        var launcher = new RecordingLauncher();

        // One host an instant, kept for every press at it, since a start of the surface writes a row keyed on its instant.
        var hosts = new Dictionary<DateTimeOffset, PassHost>();

        async Task<(HttpStatusCode Status, string Body)> Press(DateTimeOffset at, bool fromThePage)
        {
            if (!hosts.TryGetValue(at, out var host))
            {
                hosts[at] = host = new PassHost(store.Root) { Clock = FixedClock.At(at, SessionZones.UnitedStates), Launcher = launcher };
            }

            using var client = host.CreateClient();
            using var request = new HttpRequestMessage(HttpMethod.Post, SinglePageApp.QuoteRoute + name);

            if (fromThePage)
            {
                request.Headers.Add(SinglePageApp.PassHeader, SinglePageApp.PassHeaderValue);
            }

            using var reply = await client.SendAsync(request);

            return (reply.StatusCode, await reply.Content.ReadAsStringAsync());
        }

        int Asks() => (int)Count(store, "quote_request");

        // Without the page's own header: refused, nothing written, nothing started.
        Assert.Equal(HttpStatusCode.Forbidden, (await Press(InTheSession, fromThePage: false)).Status);
        Assert.Equal((0, 0), (Asks(), launcher.Quotes.Count));

        // Outside the session: refused the same way.
        Assert.Equal(HttpStatusCode.Conflict, (await Press(AfterTheClose, fromThePage: true)).Status);
        Assert.Equal((0, 0), (Asks(), launcher.Quotes.Count));

        // Inside it: the ask written and the quote job started with the name and the instant.
        var asked = await Press(InTheSession, fromThePage: true);

        Assert.Equal(HttpStatusCode.Accepted, asked.Status);
        Assert.Contains("data-asked=\"true\" data-started=\"true\" data-asked-at=\"2026-10-07T14:58:00Z\"", asked.Body, StringComparison.Ordinal);
        Assert.Equal((1, (name, InTheSession)), (Asks(), launcher.Quotes.Single()));

        // Four minutes and fifty-nine seconds later the newest ask stands and nothing is asked; five minutes later, again.
        var inside = await Press(InTheSession.AddSeconds(299), fromThePage: true);

        Assert.Equal(HttpStatusCode.OK, inside.Status);
        Assert.Contains("data-asked=\"false\" data-started=\"false\"", inside.Body, StringComparison.Ordinal);
        Assert.Equal((1, 1), (Asks(), launcher.Quotes.Count));
        Assert.Equal(HttpStatusCode.Accepted, (await Press(InTheSession.AddMinutes(5), fromThePage: true)).Status);
        Assert.Equal((2, 2), (Asks(), launcher.Quotes.Count));

        foreach (var host in hosts.Values)
        {
            host.Dispose();
        }
    }

    [Fact]
    public async Task TheNewestQuoteIsReadBackWithTheTilesAndEachBandsDistanceAtItsPrice()
    {
        using var store = await FixtureExpectations.WithListings();

        var night = NightIn(store);
        var name = FiredNamesOn(store, night)[0];

        store.Execute(
            "INSERT INTO live_quote (ticker, asked_at, answered_at, quoted_at, price, previous_close, change, change_pct, night, distances) VALUES " +
            $"('{name}', '2026-10-07T14:55:00Z', '2026-10-07T14:55:04Z', '2026-10-07T14:43:00Z', '231.40', '229.90', '1.50', 0.652, '{night}', " +
            "'[{\"low\":\"240.5\",\"high\":\"244\",\"role\":\"resistance\",\"days\":1.8},{\"low\":\"220\",\"high\":\"225.25\",\"role\":\"support\",\"days\":null}]');");

        using (var host = new PassHost(store.Root) { Clock = FixedClock.At(InTheSession, SessionZones.UnitedStates) })
        {
            using var client = host.CreateClient();
            using var reply = JsonDocument.Parse(await client.GetStringAsync(SinglePageApp.QuoteRoute + name));
            var root = reply.RootElement;

            Assert.True(root.GetProperty("live").GetBoolean());
            Assert.Equal("2026-10-07T14:55:00Z", root.GetProperty("askedAt").GetString());
            Assert.StartsWith("<span class=\"pill live\">LIVE</span><span class=\"m-px\" data-price=\"231.40\">231.40</span>", root.GetProperty("price").GetString(), StringComparison.Ordinal);
            Assert.StartsWith("Quote of 10:43 ET, delayed about 15 minutes.", root.GetProperty("asOf").GetString(), StringComparison.Ordinal);
            Assert.StartsWith("<div class=\"lead-tiles\" data-live=\"yes\">", root.GetProperty("tiles").GetString(), StringComparison.Ordinal);
            Assert.Equal(
                ["240.5 244 1.8 typical days 1.8", "220 225.25 not measured none"],
                root.GetProperty("distances").EnumerateArray().Select(band => $"{band.GetProperty("low").GetString()} {band.GetProperty("high").GetString()} {band.GetProperty("away").GetString()} {band.GetProperty("value").GetString()}"));
        }

        // After the close, none.
        using (var host = new PassHost(store.Root) { Clock = FixedClock.At(AfterTheClose, SessionZones.UnitedStates) })
        {
            using var client = host.CreateClient();
            using var reply = JsonDocument.Parse(await client.GetStringAsync(SinglePageApp.QuoteRoute + name));

            Assert.False(reply.RootElement.GetProperty("live").GetBoolean());
        }
    }

    [Fact]
    public async Task TheHeadlineCarriesNoFigureAndTheContentsDatedEntriesAreMarkedNewByThePagesScriptAlone()
    {
        using var store = await FixtureExpectations.WithListings();

        var night = NightIn(store);
        var names = FiredNamesOn(store, night);

        // One written section for the first name, written on the night.
        store.Execute($"INSERT INTO research_section (ticker, section, version, as_of, model, status, prose, source_ids, reject_reason) VALUES ('{names[0]}', 'What the company sells', 1, '{night}', 'a writer', 'accepted', 'A sentence.', '[]', NULL);");

        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        foreach (var name in names)
        {
            var page = WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/name/{name}"));
            var headline = Regex.Match(page, "<h1 class=\"lead-line\" data-written=\"code\">([^<]*)</h1>");
            var company = Regex.Match(page, "<span class=\"m-co\">([^<]*)</span>").Groups[1].Value;

            // One sentence written by code until a written one is accepted, holding no digit outside the company's name.
            Assert.True(headline.Success, $"{name}'s page draws no headline.");
            Assert.DoesNotMatch("[0-9]", headline.Groups[1].Value.Replace(company, string.Empty, StringComparison.Ordinal));
            Assert.EndsWith(".", headline.Groups[1].Value, StringComparison.Ordinal);

            // A computed region's entry carries no day, so the script marks it nothing.
            Assert.Contains("<li><a href=\"#chart\">", page, StringComparison.Ordinal);
        }

        // The written section's entry carries the day it was written, which the script reads against the last visit.
        Assert.Contains(
            $"<li data-dated=\"{night}\"><a href=\"#s-what-the-company-sells\">",
            WebUtility.HtmlDecode(await client.GetStringAsync($"/screens/name/{names[0]}")),
            StringComparison.Ordinal);

        // The shell's script marks an entry new where its day is after the day this browser last opened the page, and keeps
        // that day in the browser alone, read before this visit's is written; the exported file carries no script at all.
        var shell = new SinglePageApp().Shell("EquityBrief");

        Assert.Contains("if (entry.getAttribute('data-dated') > last && !entry.querySelector('.pill.new'))", shell, StringComparison.Ordinal);
        Assert.Contains("try { localStorage.setItem(key, new Date().toISOString().slice(0, 10)); } catch (error) { }", shell, StringComparison.Ordinal);
        Assert.True(
            shell.IndexOf("try { last = localStorage.getItem(key); }", StringComparison.Ordinal) < shell.IndexOf("localStorage.setItem(key", StringComparison.Ordinal),
            "The script writes this visit before it reads the last one, so it would mark nothing new.");
        Assert.DoesNotContain("<script", await client.GetStringAsync(ReportExporter.Route + names[0]), StringComparison.Ordinal);
    }

    // The range bar, the twenty-first mark, over a full input, a price past the year's high and the input it degrades on.
    [Fact]
    public void TheRangeBarPlacesThePriceBetweenTheYearsLowAndHighAndATileWithNoYearSaysSo()
    {
        var marks = new MarkRenderer();
        var high = new DateOnly(2026, 9, 15);
        var low = new DateOnly(2026, 1, 5);

        // 211.55 sits 61.55 of the 67.78 from the low of 150.00 to the high of 217.78: 0.9081 of the way, a dot at 109 of
        // the line's 120, with the high, the low and their sessions written beneath at the last close.
        var tiles = marks.LeadTiles(new TilesView(null, null, null, EquityBrief.Core.Tiles.NameTiles.High(211.55m, 217.78m, high, 150.00m, low), 211.55m, Live: false));
        var bar = Regex.Match(tiles, "<svg class=\"range-bar\"[^>]*data-position=\"([^\"]+)\">.*?<circle class=\"rb-dot\" cx=\"([^\"]+)\"", RegexOptions.Singleline);

        Assert.True(bar.Success, "The fourth tile draws no range bar.");
        Assert.Equal(0.9081, double.Parse(bar.Groups[1].Value, CultureInfo.InvariantCulture), 4);
        Assert.Equal("109", bar.Groups[2].Value);
        Assert.Contains("high <span data-high=\"217.78\">217.78</span> on 2026-09-15; low <span data-low=\"150.00\">150.00</span> on 2026-01-05; at the last close", tiles, StringComparison.Ordinal);
        Assert.Contains("<span class=\"v down\" data-from-high=\"", tiles, StringComparison.Ordinal);

        // A quote past the year's high is held at the line's end, and its distance from the high is a rise.
        var past = marks.LeadTiles(new TilesView(null, null, null, EquityBrief.Core.Tiles.NameTiles.High(230m, 217.78m, high, 150.00m, low), 230m, Live: true));

        Assert.Contains("<circle class=\"rb-dot\" cx=\"120\"", past, StringComparison.Ordinal);
        Assert.Contains("<span class=\"v up\" data-from-high=\"", past, StringComparison.Ordinal);
        Assert.Contains("; at the quote</span>", past, StringComparison.Ordinal);

        // A name whose year's high and low are not stored draws no bar and says so, as each tile with nothing stored does.
        var none = marks.LeadTiles(new TilesView(null, null, null, null, 211.55m, Live: false));

        Assert.DoesNotContain("range-bar", none, StringComparison.Ordinal);
        Assert.Contains("<div class=\"lead-tile\" data-tile=\"high\"><span class=\"k\">From the 52-week high</span><span class=\"v none\">not stored</span><span class=\"s\">The year's high and low are not stored for it.</span></div>", none, StringComparison.Ordinal);
        Assert.Contains("<span class=\"v none\">not stored</span><span class=\"s\">No reported quarter is stored for it yet.</span>", none, StringComparison.Ordinal);
        Assert.Contains("<span class=\"v none\">not read</span><span class=\"s\">Eight quarters of sales are not stored for it yet.</span>", none, StringComparison.Ordinal);
        Assert.Contains("<span class=\"v none\">none</span><span class=\"s\">No dividend is stored for it.</span>", none, StringComparison.Ordinal);
    }

    [Fact]
    public void TheRunPageStatesTheSessionsQuotesAgainstTheCapAndTheWordsAgreeWithTheJobs()
    {
        var open = new DateTimeOffset(2026, 10, 7, 13, 30, 0, TimeSpan.Zero);
        var close = new DateTimeOffset(2026, 10, 7, 20, 0, 0, TimeSpan.Zero);

        // Three quotes asked 14, 16 and 15 minutes after their own times: a median of 15 and at most 16; two runs refused
        // with the session closed and one at the cap.
        var view = new QuoteRunsView(
            open,
            close,
            [("CVX", open.AddHours(1), 14.0), ("PEP", open.AddHours(2), 16.0), ("CVX", open.AddHours(3), 15.0)],
            [(QuoteJob.AtTheCap, 1), (QuoteJob.Quoted, 3), (QuoteJob.SessionClosed, 2)],
            500);
        var drawn = WebUtility.HtmlDecode(new MarkRenderer().ResearchRegion(new ResearchPicture(new NightSpend(0m, 0m, 10m, 50m), [], Quotes: view)));

        Assert.Contains(
            "<p class=\"rp-quotes\" data-session=\"2026-10-07\" data-asked=\"3\" data-cap=\"500\" data-median-delay=\"15\">3 delayed quote(s) asked in the session of 2026-10-07, against a cap of 500, each asked a median 15 minutes after its own time and at most 16; 1 run(s) cap reached, 2 run(s) session closed.</p>",
            drawn,
            StringComparison.Ordinal);

        // A session that asked none says so.
        Assert.Contains(
            "No delayed quote was asked in the session of 2026-10-07, against a cap of 500.",
            WebUtility.HtmlDecode(new MarkRenderer().ResearchRegion(new ResearchPicture(new NightSpend(0m, 0m, 10m, 50m), [], Quotes: view with { Asked = [], Outcomes = [] }))),
            StringComparison.Ordinal);

        // The surface states the job's words, which it cannot reference.
        Assert.Equal((QuoteJob.Stage, QuoteJob.Quoted), (ReadApi.QuoteStage, QuoteRunsView.Quoted));
    }
}
