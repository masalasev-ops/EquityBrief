using System.Net;
using System.Text.RegularExpressions;
using EquityBrief.Core.Bars;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Sweep;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker;
using EquityBrief.Worker.Bars;
using EquityBrief.Worker.Sweep;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 14.8: the wider universe's first test. Today's members of the S&P 400 and 600 read off the
// operator's two probe answers and refused where an answer lists none; the members pull storing them marked by the pull,
// refusing the night's own index and removed whole by its purge; every other pull given a wider index asking its
// members and naming a name not served; the sweep history reading the 1,500 as the S&P 500's history with today's
// members on every session, and the 500 alone without them; and the run's answer and the survivors' words on every
// figure it states for the 1,500, read back off a constructed report.
// see: A wider universe is tested first on today's members, and widened only where a family's edge improves even so and holds on membership as it stood
public partial class FixtureExpectations
{
    // The rows the test adds that this check reaches: section 17's row and section 18's two.
    internal static readonly string[] WiderUniverseClaims =
    [
        CheckReach.Key(Scope.LimitsTable, "Wider universe test"),
        CheckReach.Key(Scope.FailureTable, "A wider index's answer lists no member, or cannot be read"),
        CheckReach.Key(Scope.FailureTable, "A member of the S&P 400 or 600 a pull does not serve"),
    ];

    [Fact]
    public void TodaysComponentsAreReadOffTheProbedAnswersAndAnAnswerListingNoneIsRefused()
    {
        // The operator's two calls of 2026-10-04, asked with historical=1: each answer lists today's members alone, the
        // S&P 600's capture holding 602 of its 603, one cut since its company's name carries a word the credential scan
        // refuses, as the manifest says.
        var mid = IndexComponents.Parse(File.ReadAllText(Path.Combine(Folder(), "phase14-mid-cap-components.json")), "MID");
        var small = IndexComponents.Parse(File.ReadAllText(Path.Combine(Folder(), "phase14-small-cap-components.json")), "SML");

        Assert.Equal((400, 602), (mid.Count, small.Count));
        Assert.All([.. mid, .. small], member => Assert.Equal("US", member.Exchange));
        Assert.Equal(mid.Select(member => member.Ticker).Order(StringComparer.Ordinal), mid.Select(member => member.Ticker));
        Assert.Contains(new IndexComponent("GPS", "US", "Gap Inc", "Consumer Cyclical", "Apparel Retail"), mid);
        Assert.Contains(new IndexComponent("PBH", "US", "Prestige Brand Holdings Inc", "Healthcare", "Drug Manufacturers - Specialty & Generic"), small);

        // An answer carrying no components, the S&P 400's own answer to the spans it does not carry among them, and one
        // listing none with a code are refused; a component with no code is passed over and one filed twice read once.
        Assert.Throws<FormatException>(() => IndexComponents.Parse("{\"General\": {\"Code\": \"MID\"}}", "MID"));
        Assert.Throws<FormatException>(() => IndexComponents.Parse(File.ReadAllText(Path.Combine(Folder(), "phase14-mid-cap-history.json")), "MID"));
        Assert.Throws<FormatException>(() => IndexComponents.Parse("{\"Components\": {\"0\": {\"Exchange\": \"US\"}}}", "SML"));
        Assert.Equal(
            [new IndexComponent("AAA", "US", null, null, null)],
            IndexComponents.Parse("{\"Components\": {\"0\": {\"Code\": \"AAA\", \"Exchange\": \"US\"}, \"1\": {\"Exchange\": \"US\"}, \"2\": {\"Code\": \"AAA\", \"Exchange\": \"US\"}}}", "SML"));
    }

    // An index's components as a feed answers them, refusing an index it holds no answer for, and counting its requests.
    sealed class ConstructedComponents(IReadOnlyDictionary<string, IReadOnlyList<IndexComponent>> answers) : IIndexComponentsFeed
    {
        public int Requests { get; private set; }

        public Task<IReadOnlyList<IndexComponent>> ComponentsAsync(string indexCode, CancellationToken cancellation = default)
        {
            Requests++;

            return answers.TryGetValue(indexCode, out var listed)
                ? Task.FromResult(listed)
                : throw new ProviderRefusal("the provider answered 404", transient: false);
        }
    }

    static readonly IClock WiderClock = FixedClock.At(new DateTimeOffset(2026, 9, 4, 21, 10, 0, TimeSpan.Zero), SessionZones.UnitedStates);

    [Fact]
    public async Task TheMembersPullStoresTodaysMembersMarkedByThePullAndItsPurgeRemovesThem()
    {
        using var store = new TemporaryStore().Migrated();

        var feed = new ConstructedComponents(new Dictionary<string, IReadOnlyList<IndexComponent>>(StringComparer.Ordinal)
        {
            ["MID"] = [new("AAA", "US", "Aaa Inc", "Technology", "Software"), new("BBB", "US", null, null, null)],
        });

        // Each member the answer lists stored with its pull, the row saying they are survivors alone.
        var first = await HistoryPull.PullMembersAsync(feed, WiderClock, store.DatabaseFile, "MID", "history-pull-one");

        Assert.Equal(new HistoryMemberOutcome("MID", 2, 2, 2, null, 1), first);
        Assert.Equal(
            ["MID|AAA|US|Aaa Inc|Technology|Software|history-pull-one", "MID|BBB|US|-|-|-|history-pull-one"],
            TextRows(store, "SELECT index_code || '|' || ticker || '|' || exchange || '|' || IFNULL(name, '-') || '|' || IFNULL(sector, '-') || '|' || IFNULL(industry, '-') || '|' || pull FROM pulled_member ORDER BY ticker;"));
        Assert.Equal(
            ["history-pull-members|ok|2|1|MID: 2 member(s) today, 2 new and 2 held, survivors alone since the answer carries no span of membership; 1 request(s)"],
            TextRows(store, "SELECT stage || '|' || outcome || '|' || rows_written || '|' || network_requests || '|' || detail FROM run_log WHERE run_id = 'history-pull-one';"));

        // A second pull adds the member no earlier pull holds and keeps the first's rows.
        var again = new ConstructedComponents(new Dictionary<string, IReadOnlyList<IndexComponent>>(StringComparer.Ordinal)
        {
            ["MID"] = [new("AAA", "US", "Renamed", null, null), new("CCC", "US", null, null, null)],
        });
        var second = await HistoryPull.PullMembersAsync(again, WiderClock, store.DatabaseFile, "MID", "history-pull-two");

        Assert.Equal((2, 1, 3), (second.Listed, second.Written, second.Held));
        Assert.Equal(["AAA|Aaa Inc|history-pull-one", "BBB|-|history-pull-one", "CCC|-|history-pull-two"], TextRows(store, "SELECT ticker || '|' || IFNULL(name, '-') || '|' || pull FROM pulled_member ORDER BY ticker;"));

        // An index the provider refuses stores nothing and says why; the night's own index is refused before any request.
        var refused = await HistoryPull.PullMembersAsync(feed, WiderClock, store.DatabaseFile, "SML", "history-pull-three");

        Assert.Equal(("the provider answered 404", 0, 0), (refused.Refused, refused.Written, refused.Held));
        Assert.Equal(["refused|SML: nothing was stored, the provider answered 404; 1 request(s)"], TextRows(store, "SELECT outcome || '|' || detail FROM run_log WHERE run_id = 'history-pull-three';"));
        await Assert.ThrowsAsync<ArgumentException>(() => HistoryPull.PullMembersAsync(feed, WiderClock, store.DatabaseFile, "GSPC", "history-pull-four"));

        using (var output = new StringWriter())
        using (var error = new StringWriter())
        {
            Assert.Equal(2, await HistoryPull.RunAsync(["--members", "--index", "GSPC"], () => throw new InvalidOperationException("no night feeds"), WiderClock, store.DatabaseFile, output, error, members: () => feed));
            Assert.Contains("name a wider index to pull the members of with '--index', one of MID, SML", error.ToString(), StringComparison.Ordinal);
        }

        // The purge removes the first pull's two members and leaves the second's.
        var purged = await HistoryPull.PurgeAsync(WiderClock, store.DatabaseFile, "history-pull-one", "history-purge-one");

        Assert.Equal(2, purged.Members);
        Assert.Equal(["CCC"], TextRows(store, "SELECT ticker FROM pulled_member;"));
    }

    // A historical feed answering a bar on each weekday asked for of the names it holds, and refusing the others.
    sealed class WiderBars(IReadOnlySet<string> served) : IHistoricalBarFeed
    {
        public int Requests { get; private set; }

        public List<string> Asked { get; } = [];

        public Task<IReadOnlyList<ProviderBar>> BarsAsync(string ticker, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
        {
            Requests++;
            Asked.Add(ticker);

            if (!served.Contains(ticker))
            {
                throw new ProviderRefusal("the provider answered 404", transient: false);
            }

            var bars = new List<ProviderBar>();

            for (var day = from; day <= to; day = day.AddDays(1))
            {
                if (day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
                {
                    bars.Add(new ProviderBar(day, 10m, 11m, 9m, 10.5m, 10.5m, 1_000));
                }
            }

            return Task.FromResult<IReadOnlyList<ProviderBar>>(bars);
        }
    }

    // An earnings calendar answering every window with the prints it holds inside it.
    sealed class WiderPrints(IReadOnlyList<CalendarEvent> prints) : IEarningsCalendarFeed
    {
        public int Requests { get; private set; }

        public Task<IReadOnlyList<CalendarEvent>> EventsAsync(DateOnly from, DateOnly to, CancellationToken cancellation = default)
        {
            Requests++;

            return Task.FromResult<IReadOnlyList<CalendarEvent>>([.. prints.Where(print => print.EventDate >= from && print.EventDate <= to)]);
        }
    }

    [Fact]
    public async Task APullGivenAWiderIndexAsksItsMembersTodayAndNamesANameNotServed()
    {
        using var store = new TemporaryStore().Migrated();

        store.Execute(
            "INSERT INTO pulled_member (index_code, ticker, exchange, name, sector, industry, pull) VALUES " +
            "('MID', 'AAA', 'US', NULL, NULL, NULL, 'p'), ('MID', 'BBB', 'US', NULL, NULL, NULL, 'p'), ('MID', 'ZZZ', 'US', NULL, NULL, NULL, 'p'), ('SML', 'SSS', 'US', NULL, NULL, NULL, 'p');");

        var bars = new WiderBars(new HashSet<string>(["AAA", "BBB"], StringComparer.Ordinal));
        var prints = new WiderPrints(
        [
            new("AAA", new DateOnly(2026, 9, 2), EventTiming.After, null, null, null),
            new("QQQ", new DateOnly(2026, 9, 2), EventTiming.After, null, null, null),
        ]);

        // The S&P 400's members today and none of the 600's or of the night's own membership: the week to Friday
        // 2026-09-04, five sessions each for the two served, the third named, and the print of a member kept alone.
        var outcome = await new HistoryPull(bars, prints, WiderClock, store.DatabaseFile).PullAsync("MID", new DateOnly(2026, 8, 31), "history-pull-mid");

        Assert.Equal(["AAA", "BBB", "ZZZ"], bars.Asked);
        Assert.Equal((3, 2, 10, 1), (outcome.Names, outcome.Stored, outcome.BarsWritten, outcome.EarningsWritten));
        Assert.Equal(["ZZZ: the provider answered 404"], outcome.Unanswered);
        Assert.Equal(["AAA|5", "BBB|5"], TextRows(store, "SELECT ticker || '|' || COUNT(*) FROM pulled_bar WHERE pull = 'history-pull-mid' GROUP BY ticker ORDER BY ticker;"));
        Assert.Equal(["AAA|2026-09-02"], TextRows(store, "SELECT ticker || '|' || event_date FROM pulled_earnings;"));
    }

    [Fact]
    public async Task TheSweepHistoryReadsTheWiderUniverseAsTheFiveHundredsHistoryWithTodaysMembersOnEverySession()
    {
        using var store = new TemporaryStore().Migrated();

        // AAA joined the S&P 500 on 2026-08-03 and is in today's 400, BBB left the 500 on 2026-08-05, and CCC, in today's
        // 600, the 500 never held.
        store.Execute(
            "INSERT INTO membership (index_code, ticker, joined, \"left\", observed_at) VALUES ('GSPC', 'AAA', '2026-08-03', NULL, '2026-08-07T21:10:00Z'), ('GSPC', 'BBB', NULL, '2026-08-05', '2026-08-07T21:10:00Z');" +
            "INSERT INTO pulled_member (index_code, ticker, exchange, name, sector, industry, pull) VALUES ('MID', 'AAA', 'US', NULL, NULL, NULL, 'p'), ('SML', 'CCC', 'US', NULL, NULL, NULL, 'p');");

        foreach (var ticker in new[] { "AAA", "BBB", "CCC" })
        {
            foreach (var day in new[] { 3, 4, 5, 6, 7 })
            {
                store.Execute(
                    "INSERT INTO pulled_bar (ticker, session_date, open, high, low, close, raw_close, volume, pull) VALUES " +
                    $"('{ticker}', '2026-08-{day:00}', '10', '11', '9', '10.5', '10.5', 1000, 'p');");
            }
        }

        var through = new DateOnly(2026, 8, 7);
        var history = new SweepHistory(store.DatabaseFile);
        var fiveHundred = await history.ReadAsync(through);
        var fifteenHundred = await history.ReadAsync(through, wider: true);

        // The 500 alone: the two names it held, each over its spans, and no member of the wider indices.
        Assert.Equal(["AAA", "BBB"], fiveHundred.Names.Select(name => name.Ticker));
        Assert.Equal((0, 0), (fiveHundred.Survivors, fiveHundred.WiderMembers));
        Assert.False(fiveHundred.Names[0].MemberOn(new DateOnly(2026, 7, 31)));

        // The 1,500: AAA a member on every session, BBB still over its own span, and CCC, the 500's never, a survivor
        // read on every session with its bars.
        SweepName Named(string ticker) => fifteenHundred.Names.Single(name => name.Ticker == ticker);

        Assert.Equal(["AAA", "BBB", "CCC"], fifteenHundred.Names.Select(name => name.Ticker));
        Assert.Equal((1, 2), (fifteenHundred.Survivors, fifteenHundred.WiderMembers));
        Assert.True(Named("AAA").MemberOn(new DateOnly(2026, 7, 31)));
        Assert.False(Named("AAA").Survivor);
        Assert.False(Named("BBB").MemberOn(new DateOnly(2026, 8, 6)));
        Assert.True(Named("CCC").Survivor);
        Assert.True(Named("CCC").MemberOn(new DateOnly(2018, 1, 2)));
        Assert.Equal(5, Named("CCC").Bars.Length);
    }

    // A rule's figures over eight years, each year's edge the same, its trades and its nights listing as stated.
    static IdeaFigures WiderFigures(string key, double edge, int trades = 2_000, int nights = 900) =>
        new(
            key,
            trades,
            trades,
            nights,
            1_000,
            edge,
            edge + 0.05,
            0.01,
            [.. Enumerable.Repeat(trades / SweepFigures.Years, SweepFigures.Years)],
            [.. Enumerable.Repeat<double?>(edge, SweepFigures.Years)],
            [.. Enumerable.Repeat(edge * trades / SweepFigures.Years, SweepFigures.Years)],
            edge,
            edge * trades * 3 / SweepFigures.Years,
            edge,
            edge * trades,
            0.1);

    [Fact]
    public void TheRunJudgesEachFamilyByTheIdeasTestAndStatesSurvivorsOnEveryFigureItGivesTheWiderUniverse()
    {
        // The pullback's base better on the 1,500 in every year, the breakout lower and the drift better but on too few
        // nights: one family improves, so nothing is adopted until membership as it stood is read.
        WiderReading[] oneImproves =
        [
            WiderUniverse.Read("pullback", "pullback's base", WiderFigures("500", 0.10), WiderFigures("1500", 0.20)),
            WiderUniverse.Read("breakout", "breakouts'", WiderFigures("500", 0.10), WiderFigures("1500", 0.05)),
            WiderUniverse.Read("drift", "earnings drift's", WiderFigures("500", 0.10), WiderFigures("1500", 0.20, nights: 300)),
        ];

        Assert.Equal([true, false, false], oneImproves.Select(reading => reading.Improves));
        Assert.Equal((false, false), (oneImproves[2].Test.EnoughNights, WiderUniverse.Dropped(oneImproves)));
        Assert.Equal(
            "The pullback's base edge improves on the 1,500 on survivors, so nothing is adopted until the result is confirmed on membership as it stood, from the provider or from the iShares IJH and IJR holdings by date.",
            WiderUniverse.Outcome(oneImproves));

        // None improving: the widening is dropped.
        WiderReading[] noneImproves = [oneImproves[1], oneImproves[2]];

        Assert.True(WiderUniverse.Dropped(noneImproves));
        Assert.Equal("No family's edge improves on the 1,500 even on survivors, so the widening is dropped and recorded as not adopted.", WiderUniverse.Outcome(noneImproves));
        Assert.Equal(1.0 * 3 * SweepIdeas.LuckPatterns() / 256, WiderUniverse.Luck(3), 9);

        // The report: every row and every sentence giving a figure for the 1,500 carries the survivors' words, and no row
        // for the 500 does; the answer and its tries read off the page.
        var run = new WiderRun(new DateOnly(2019, 1, 2), new DateOnly(2026, 10, 2), 828, 1_810, 1_003, 982, 1_949, new DateTimeOffset(2026, 10, 4, 19, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 10, 4, 19, 40, 0, TimeSpan.Zero));
        var page = WiderUniverse.Build(run, oneImproves);
        var wider = Regex.Matches(page, "<tr data-universe=\"1500\".*?</tr>", RegexOptions.Singleline).Select(match => WebUtility.HtmlDecode(match.Value)).ToArray();
        var alone = Regex.Matches(page, "<tr data-universe=\"500\".*?</tr>", RegexOptions.Singleline).Select(match => WebUtility.HtmlDecode(match.Value)).ToArray();
        var lines = Regex.Matches(page, "<p class=\"family-line\">(.*?)</p>", RegexOptions.Singleline).Select(match => WebUtility.HtmlDecode(match.Groups[1].Value)).ToArray();

        Assert.Equal((3, 3, 3), (wider.Length, alone.Length, lines.Length));
        Assert.All(wider, row => Assert.Contains(WiderUniverse.SurvivorsOnly, row, StringComparison.Ordinal));
        Assert.All(wider, row => Assert.Equal(7 + SweepFigures.Years, Regex.Matches(row, "title=\"").Count));

        // The words stand in the row's own label, where they are read without a pointer over a figure, as well as on
        // each figure.
        Assert.All(wider, row => Assert.Equal("The 1,500, " + WiderUniverse.SurvivorsOnly, Regex.Match(row, "<td>(.*?)</td>").Groups[1].Value));
        Assert.All(alone, row => Assert.DoesNotContain(WiderUniverse.SurvivorsOnly, row, StringComparison.Ordinal));
        Assert.All(lines, line => Assert.Contains("on the 1,500 (" + WiderUniverse.SurvivorsOnly + ")", line, StringComparison.Ordinal));
        Assert.Contains("data-dropped=\"no\" data-tries=\"3\" data-improves=\"1\"", page, StringComparison.Ordinal);
        Assert.Contains("The 1,500's history holds survivors only.", WebUtility.HtmlDecode(page), StringComparison.Ordinal);
        Assert.Contains("1,003 members of the S&P 400 and 600, 982 of them never held", WebUtility.HtmlDecode(page), StringComparison.Ordinal);
    }
}
