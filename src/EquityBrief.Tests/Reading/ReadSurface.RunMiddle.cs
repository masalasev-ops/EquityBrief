using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Api.Reading;
using EquityBrief.Core.Shortlist;
using EquityBrief.Tests.Checks;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.Marks;

namespace EquityBrief.Tests.Reading;

// read-surface, the Run page's trades, freshness, research and checklist regions: each projection worked by
// hand over constructed rows and each region read back off the rendered page over a constructed store.
// see: A night's state is read off its own run log rows and its tries, and the pages that state it read that one state
// see: Every trade the live list recommended is shown, and their share waits for the minimum the reason records wait for
public partial class ReadSurface
{
    // The claims the 12.3 correction drawing the Run page's trades, freshness, research and checklist adds, which
    // this check reaches and the phase's pair names. Declared before the reach that takes them in.
    internal static readonly string[] RunMiddleClaims =
    [
        CheckReach.Key("15.10 Run", "How the list's trades are going, the live list's trades still open and those that reached the target or were stopped out or ran out of time"),
        CheckReach.Key("15.10 Run", "How the list's trades are going, a ring of the trades decided against the minimum a share waits on"),
        CheckReach.Key("15.10 Run", "How the list's trades are going, the share and the break-even and the average result together once both minimums are met as Past picks draws them"),
        CheckReach.Key("15.10 Run", "How the list's trades are going, a link opening Past picks"),
        CheckReach.Key("15.10 Run", "Is the list finding new stocks, a bar for each of the last twenty evenings split into the names new that evening and those the evening before also listed"),
        CheckReach.Key("15.10 Run", "Is the list finding new stocks, the night's split in words"),
        CheckReach.Key("15.10 Run", "Research and spend, the month's spend against the month cap"),
        CheckReach.Key("15.10 Run", "Research and spend, the reports the paid model wrote on each of the last seven nights"),
        CheckReach.Key("15.10 Run", "Research and spend, the reports and the overnight drafts written over those nights"),
        CheckReach.Key("15.10 Run", "Anything to worry about, a checklist of plain items each turning red with its reason where it fails"),
        CheckReach.Key("15.10 Run", "Anything to worry about, every stock holding the night's prices and every step of the night finished"),
        CheckReach.Key("15.10 Run", "Anything to worry about, no research document refused and no section fallen back"),
        CheckReach.Key("15.10 Run", "Anything to worry about, every company awaiting a quarter asked on schedule"),
        CheckReach.Key("15.10 Run", "Anything to worry about, the four harness counts beneath"),
        CheckReach.Key("15.5 The mark vocabulary", "Trades ring"),
        CheckReach.Key("15.5 The mark vocabulary", "Freshness bars"),
        CheckReach.Key("15.5 The mark vocabulary", "Research bars"),
        CheckReach.Key("15.5 The mark vocabulary", "Checklist"),
    ];

    static ListingRow Listing(string ticker, string session, bool listed, string rule) =>
        new(ticker, DateOnly.ParseExact(session, "yyyy-MM-dd", CultureInfo.InvariantCulture), "[]", listed ? 1 : 0, "{}", Listed: listed, Rule: rule);

    // Four evenings, the first listed by the reasons: A and B on it; the filter's first night lists A and C, so
    // one new and one also on the evening before; the next lists C, D and E, two new and one repeated; the last
    // lists nobody. The evening before the first filter night is read for what it listed and is not drawn.
    [Fact]
    public void TheFreshnessBarsSplitEachEveningIntoNewAndRepeatedByTheRuleThatListedIt()
    {
        ListingRow[] listings =
        [
            Listing("A", "2026-09-23", true, ListRules.Reasons), Listing("B", "2026-09-23", true, ListRules.Reasons), Listing("C", "2026-09-23", false, ListRules.Reasons),
            Listing("A", "2026-09-24", true, ListRules.Filter), Listing("C", "2026-09-24", true, ListRules.Filter), Listing("B", "2026-09-24", false, ListRules.Filter),
            Listing("C", "2026-09-25", true, ListRules.Filter), Listing("D", "2026-09-25", true, ListRules.Filter), Listing("E", "2026-09-25", true, ListRules.Filter),
            Listing("A", "2026-09-28", false, ListRules.Filter),
        ];

        var fresh = RunScreen.Freshness(listings, new DateOnly(2026, 9, 28), new DateOnly(2026, 9, 24));

        Assert.Equal(
            [("2026-09-24", 2, 1, 1), ("2026-09-25", 3, 1, 2), ("2026-09-28", 0, 0, 0)],
            fresh.Select(evening => (evening.Session.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), evening.Listed, evening.Repeated, evening.New)));

        var marks = new MarkRenderer();
        var drawn = WebUtility.HtmlDecode(marks.FreshBars(fresh, new DateOnly(2026, 9, 25)));

        Assert.Contains("<g data-session=\"2026-09-25\" data-listed=\"3\" data-new=\"2\" data-repeated=\"1\">", drawn, StringComparison.Ordinal);
        Assert.Contains("On this night: 2 new and 1 also on the list the evening before. Over these 3 evening(s), 60% of the names listed were new.", drawn, StringComparison.Ordinal);
        Assert.Contains("no evening of the list is stored up to this night", marks.FreshBars([], new DateOnly(2026, 9, 25)), StringComparison.Ordinal);
        Assert.Equal(20, RunScreen.FreshEvenings);
    }

    // Two nights: on the first, two research passes each made a paid call that answered, a third's call was
    // refused and a by-hand command's row is no pass, and the queue completed four of six; on the second, one
    // pass and a queue that completed none.
    [Fact]
    public void TheResearchRegionCountsThePaidPassesAndTheOvernightDraftsOfEachNight()
    {
        (DateOnly, IReadOnlyList<RunStageRow>)[] week =
        [
            (new DateOnly(2026, 9, 24),
            [
                LogRow("research-20260925T010000Z-KEYS", "research call: The risks", "2026-09-25T01:00:00Z", "2026-09-25T01:01:00Z", spend: "0.01"),
                LogRow("research-20260925T010000Z-KEYS", "research call: The two cases", "2026-09-25T01:01:00Z", "2026-09-25T01:02:00Z", spend: "0.01"),
                LogRow("research-20260925T020000Z-MSFT", "research call: The risks", "2026-09-25T02:00:00Z", "2026-09-25T02:01:00Z", spend: "0.01"),
                LogRow("research-20260925T030000Z-AAPL", "research call: The risks", "2026-09-25T03:00:00Z", "2026-09-25T03:01:00Z", "paused"),
                LogRow("shape-20260925T030000Z", "shape", "2026-09-25T03:00:00Z", "2026-09-25T03:01:00Z", "accepted"),
                LogRow(NightRun, RunScreen.QueueStage, "2026-09-24T23:41:00Z", "2026-09-25T00:20:00Z", detail: "4 of 6 queued pass(es) completed, 2 left for the next night"),
            ]),
            (new DateOnly(2026, 9, 25),
            [
                LogRow("research-20260926T010000Z-HUM", "research call: The risks", "2026-09-26T01:00:00Z", "2026-09-26T01:01:00Z", spend: "0.02"),
                LogRow(NightRun, RunScreen.QueueStage, "2026-09-25T23:41:00Z", "2026-09-26T00:20:00Z", "failed", detail: "step 'queue' failed: SQLite Error 5: 'database is locked'."),
            ]),
        ];

        var research = RunScreen.Research(week);

        Assert.Equal([(2, 4), (1, 0)], research.Select(night => (night.PaidPasses, night.Drafts)));

        var drawn = WebUtility.HtmlDecode(new MarkRenderer().ResearchRegion(new ResearchPicture(new NightSpend(0.02m, 0.26m, 10m, 50m), research)));

        Assert.Contains("<b>$0.26</b> spent this month, of the $50.00 month cap", drawn, StringComparison.Ordinal);
        Assert.Contains("<g data-session=\"2026-09-24\" data-paid=\"2\" data-drafts=\"4\">", drawn, StringComparison.Ordinal);
        Assert.Contains("<div class=\"tile\" data-figure=\"paid reports\"><b>3</b>", drawn, StringComparison.Ordinal);
        Assert.Contains("<div class=\"tile\" data-figure=\"overnight drafts\"><b>4</b>", drawn, StringComparison.Ordinal);
    }

    // Each item held, failed with its reason, or not read. Whether every step finished is the night's own state,
    // so a by-hand command that failed that day is none of it, and a night finished with its queue failing after
    // the close is not finished whole; the quarters are read off the night's own step.
    [Fact]
    public void TheChecklistTurnsRedWithItsReasonAndReadsTheNightsOwnState()
    {
        var finished = RunScreen.Night(
            [.. FinishedNight.Where(row => row.Stage != RunScreen.QueueStage), LogRow("shape-20260910T235000Z", "shape", "2026-09-10T23:50:00Z", "2026-09-10T23:51:00Z", "refused")],
            TenthOfSeptember,
            DateTimeOffset.Parse("2026-09-11T01:00:00Z", CultureInfo.InvariantCulture),
            FifteenMinutes);

        string Quarters(string detail, string outcome = "ok") => detail + "|" + outcome;

        IReadOnlyList<WorryItem> Read(IReadOnlyList<string> stale, NightView night, int refused, int fellBack, string? quarters) =>
            RunScreen.Worries(
                stale,
                night,
                [.. Enumerable.Range(0, refused).Select(_ => new RefusedDocument("a quote page", "t", "u"))],
                [.. Enumerable.Range(0, fellBack).Select(_ => new LeftOutSection("KEYS", "The risks", "no figure matched"))],
                quarters is null ? [] : [LogRow(NightRun, "quarters", "2026-09-10T23:41:00Z", "2026-09-10T23:42:00Z", quarters.Split('|')[1], detail: quarters.Split('|')[0])]);

        var held = Read([], finished, 0, 0, Quarters("3 of 3 member(s) due asked: 3 reporting, 0 waiting, 0 joining, 0 filled; 3 stored, 0 not yet posted, 0 returning nothing, 0 refused; 36 quarter row(s), 33 weighted call(s); 0 member(s) of the fill still owed"));

        Assert.Equal(5, held.Count);
        Assert.All(held, item => Assert.Equal((WorryItem.Held, (string?)null), (item.State, item.Why)));

        var failed = Read(["P", "TAP"], RunScreen.Night(FinishedNight, TenthOfSeptember, DateTimeOffset.Parse("2026-09-11T01:00:00Z", CultureInfo.InvariantCulture), FifteenMinutes), 1, 1, Quarters("3 of 3 member(s) due asked: 3 reporting, 0 waiting, 0 joining, 0 filled; 1 stored, 0 not yet posted, 0 returning nothing, 2 refused; 12 quarter row(s), 33 weighted call(s); 0 member(s) of the fill still owed"));

        Assert.Equal(
            [
                "2 carry an earlier session's bars: P, TAP",
                "after the close, overnight queue failed: database is locked",
                "1 refused by admissibility",
                "1 fell back: KEYS The risks",
                "2 ask(s) refused",
            ],
            failed.Select(item => item.Why));
        Assert.All(failed, item => Assert.Equal(WorryItem.Failed, item.State));

        Assert.Equal("1 left at the step's limit", Read([], finished, 0, 0, Quarters("4 of 5 member(s) due asked: 4 reporting, 0 waiting, 0 joining, 0 filled; 4 stored, 0 not yet posted, 0 returning nothing, 0 refused; 48 quarter row(s), 44 weighted call(s); 0 member(s) of the fill still owed; 1 left at the step's limit"))[4].Why);
        Assert.Equal((WorryItem.NotRead, "the night ran no quarters step"), (Read([], finished, 0, 0, null)[4].State, Read([], finished, 0, 0, null)[4].Why));

        var drawn = WebUtility.HtmlDecode(new MarkRenderer().WorryRegion(failed, new HarnessCounts(665, 0, 0, 0)));

        Assert.Contains("<li data-item=\"Every stock has the night's prices\" data-state=\"failed\">", drawn, StringComparison.Ordinal);
        Assert.Contains(": <b class=\"worry-why\">2 carry an earlier session's bars: P, TAP</b>", drawn, StringComparison.Ordinal);
        Assert.Contains("<div class=\"tile\" data-count=\"passed\"><b>665</b>", drawn, StringComparison.Ordinal);
    }

    // The trades region over Past picks' own summary: below the minimum a dashed ring and the counts it waits on,
    // and at it the three figures Past picks draws, never before.
    [Fact]
    public void TheTradesRegionDrawsTheShareOnlyOnceBothMinimumsAreMet()
    {
        var marks = new MarkRenderer();
        var below = WebUtility.HtmlDecode(marks.TradesRegion(new PicksSummary(12, 5, 3, 4, 3, 2, 0, 7, 4, 250, 60, null, null, null), "#/picks"));

        Assert.Contains("data-open=\"3\" data-target=\"4\" data-stopped=\"3\" data-time=\"2\" data-decided=\"7\" data-needed=\"250\" data-met=\"false\"", below, StringComparison.Ordinal);
        Assert.Contains("class=\"tr-track tr-unmet\"", below, StringComparison.Ordinal);
        Assert.Contains("The share that reached the target is drawn once 250 trades are decided over 60 listing nights: 7 and 4 so far.", below, StringComparison.Ordinal);
        Assert.DoesNotContain("reached the target against", below, StringComparison.Ordinal);
        Assert.Contains("<a href=\"#/picks\">Every trade on Past picks", below, StringComparison.Ordinal);

        var met = WebUtility.HtmlDecode(marks.TradesRegion(new PicksSummary(300, 70, 10, 140, 120, 30, 0, 260, 64, 250, 60, 53.8, 41.2, 0.21), "#/picks"));

        Assert.Contains("data-met=\"true\"", met, StringComparison.Ordinal);
        Assert.Contains("<b>53.8%</b> reached the target against <b>41.2%</b> needed to break even, and the average trade came to <b>0.21</b> of its risk.", met, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheRunPageDrawsTheTradesTheFreshnessTheResearchAndTheChecklistAboveTheDetail()
    {
        using var store = RunTopStore();
        using var host = new Host(store.Root);
        using var client = host.CreateClient();

        var page = WebUtility.HtmlDecode(await client.GetStringAsync("/screens/run/2026-09-10"));
        var detail = page.IndexOf("<section class=\"run-detail\">", StringComparison.Ordinal);

        foreach (var card in new[] { "trades-picture", "fresh-picture", "research-picture", "worry-picture" })
        {
            var at = page.IndexOf($"data-card=\"{card}\"", StringComparison.Ordinal);

            Assert.True(at > 0 && at < detail, $"The run page draws no {card} above the detail.");
        }

        Assert.Contains("<div class=\"trades-picture\" data-listed=\"1\"", page, StringComparison.Ordinal);
        Assert.Matches("<li data-item=\"Every step of the night finished\" data-state=\"failed\">.*?after the close, overnight queue failed: database is locked", page);
        Assert.Contains("<li data-item=\"Every company awaiting a new quarter was asked on schedule\" data-state=\"not read\">", page, StringComparison.Ordinal);
    }
}
