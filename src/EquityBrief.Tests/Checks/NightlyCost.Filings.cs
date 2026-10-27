using System.Globalization;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Ledger;

namespace EquityBrief.Tests.Checks;

// nightly-cost, the seventh carve-out: the filings refresh after the close, counted night by night over constructed
// nights with an archive the test holds, so what is asked on each night is read off the documents the archive was
// asked for rather than off what the step says it did. The operator's test of 2026-10-09: a member that filed is
// refreshed, and one that did not is not asked for.
// see: The night refreshes the facts and the insiders' trades of the members that filed since its last read of the archive's daily index, after the close under its own limit
public partial class NightlyCost
{
    // The rows the filings refresh adds that this check reaches: section 17's row and section 18's two.
    internal static readonly string[] FiledFactsClaims =
    [
        CheckReach.Key(Scope.LimitsTable, "Filings refresh"),
        CheckReach.Key(Scope.FailureTable, "The archive refuses or does not answer the filings refresh"),
        CheckReach.Key(Scope.FailureTable, "The archive has not posted a day's index"),
    ];

    // The night's step as section 14 states it, which nightly-run reaches.
    internal const string FilingsStep =
        "Read the archive's daily index for each weekday since the refresh last read one, through the night's own session where the archive has posted it, and ask the archive for the facts of each member whose filer filed a quarterly or annual report, an amendment to one, or a results announcement whose own page carries item 2.02, storing each fact not yet stored as first filed; a day the archive posted no index for before the session's own is read as none and the session's own, not yet posted, is read on a later night; bounded by its own limit and never by the night's deadline, a refusal leaving the facts as they were and its days for the next night; then read the business readings of tonight's setups again for the members refreshed (see: The night refreshes the facts and the insiders' trades of the members that filed since its last read of the archive's daily index, after the close under its own limit). From 22.8, then score every member again, so tonight's scorecards read every filing the archive posted before the session as tonight's setups do (see: The fundamentals scorecard scores every member on nine checks of its filings as first filed, and only the quality family's rule reads it).";

    // Every row the filings refresh adds, named after phase 16's report until phase 17's own pair is checked: the
    // component's catalogue and matrix rows, its three stores, the three above and the night's step.
    internal static string[] FiledFactsRows =>
    [
        CheckReach.Key(Scope.CatalogueTable, "Filings refresher"),
        CheckReach.Key(Scope.MatrixTable, "Filings refresher"),
        CheckReach.Key(Scope.StoresTable, "Filed facts"),
        CheckReach.Key(Scope.StoresTable, "Facts pulls"),
        CheckReach.Key(Scope.StoresTable, "Filing days"),
        .. FiledFactsClaims,
        CheckReach.Key(NightlyRunSteps.Heading, FilingsStep),
    ];

    static readonly DateOnly FilingsSession = new(2026, 10, 7);

    static readonly Func<TimeSpan, CancellationToken, Task> NoPause = (_, _) => Task.CompletedTask;

    // Four members of the S&P 500, each with its filer, and a filer that is no member.
    static TemporaryStore FilingsStore()
    {
        var store = new TemporaryStore().Migrated();

        foreach (var (ticker, cik) in new[] { ("AAA", "0000000101"), ("BBB", "0000000102"), ("CCC", "0000000103"), ("DDD", "0000000104") })
        {
            store.Execute($"INSERT INTO membership (index_code, ticker, joined, \"left\", observed_at) VALUES ('GSPC', '{ticker}', '2020-01-02', NULL, '2026-09-01T23:30:00Z');");
            store.Execute($"INSERT INTO company (ticker, fetched_at, cik, sector) VALUES ('{ticker}', '2026-09-01T23:40:00.000Z', '{cik}', 'Industrials');");
        }

        return store;
    }

    internal static string DayIndex(DateOnly day, params (string Cik, string Company, string Form, string Accession)[] filings) =>
        "Description:           Daily Index of EDGAR Dissemination Feed\n"
        + "Last Data Received:    constructed\n"
        + "Comments:              constructed by the suite\n"
        + "Anonymous FTP:         none\n"
        + " \n"
        + SecEdgarDailyIndex.Columns + "\n"
        + new string('-', 80) + "\n"
        + string.Concat(filings.Select(filing =>
            $"{filing.Cik.TrimStart('0')}|{filing.Company}|{filing.Form}|{day.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}|edgar/data/{filing.Cik.TrimStart('0')}/{filing.Accession}.txt\n"));

    internal static string ItemsPage(params string[] items) =>
        "<div class=\"formGrouping\">\n<div class=\"infoHead\">Items</div>\n<div class=\"info\">"
        + string.Concat(items.Select(item => $"Item {item}: constructed<br />"))
        + "</div>\n</div>";

    // A filer's facts holding one quarter of revenue, filed on the day given under the report named, or the filer's first.
    internal static string OneQuarter(string cik, string filed, string? accession = null) =>
        "{\"cik\":" + cik.TrimStart('0') + ",\"facts\":{\"us-gaap\":{\"Revenues\":{\"units\":{\"USD\":["
        + "{\"start\":\"2026-04-01\",\"end\":\"2026-06-30\",\"val\":1000,\"filed\":\"" + filed + "\",\"form\":\"10-Q\",\"accn\":\"" + (accession ?? cik + "-26-000001") + "\"}"
        + "]}}}}}";

    static IReadOnlyList<string> FilingRows(TemporaryStore store, string sql)
    {
        using var connection = store.Open();
        using var command = connection.CreateCommand();

        command.CommandText = sql;

        using var reader = command.ExecuteReader();
        var rows = new List<string>();

        while (reader.Read())
        {
            rows.Add(Convert.ToString(reader.GetValue(0), CultureInfo.InvariantCulture)!);
        }

        return rows;
    }

    [Fact]
    public async Task AMemberThatFiledIsRefreshedOnTheNextSessionsNightAndOneThatDidNotIsNotAskedFor()
    {
        using var store = FilingsStore();

        var monday = new DateOnly(2026, 10, 5);
        var tuesday = new DateOnly(2026, 10, 6);

        // On the Tuesday, the session before the night: AAA files a 10-Q, BBB an 8-K whose page carries results, CCC an
        // 8-K whose page does not, DDD an insider's Form 4, a director's purchase, and a filer that is no member a 10-K and
        // a Form 4. The archive has posted no index for the Monday or the week before it, and none yet for the night's own
        // session.
        var first = RecordedFilingsRefreshFeed.Of(
            new Dictionary<DateOnly, string>
            {
                [tuesday] = DayIndex(
                    tuesday,
                    ("0000000999", "NOT A MEMBER INC", "10-K", "0000000999-26-000009"),
                    ("0000000101", "AAA INC", "10-Q", "0000000101-26-000001"),
                    ("0000000102", "BBB INC", "8-K", "0000000102-26-000002"),
                    ("0000000103", "CCC INC", "8-K", "0000000103-26-000003"),
                    ("0000000104", "DDD INC", "4", "0000000104-26-000004"),
                    ("0000000999", "NOT A MEMBER INC", "4", "0000000999-26-000019")),
            },
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["0000000102-26-000002"] = ItemsPage("2.02", "9.01"),
                ["0000000103-26-000003"] = ItemsPage("7.01", "9.01"),
            },
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["0000000101"] = OneQuarter("0000000101", "2026-10-06"),
                ["0000000102"] = OneQuarter("0000000102", "2026-10-06"),
            },
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["0000000104-26-000004"] = FixtureExpectations.Submission(
                    "0000000104-26-000004", "20261006", "0000000104", "DDD", [("0000009104", "A DIRECTOR", true, false, null)], [("2026-10-05", "P", "100", "50", true)]),
            });

        var night = FixedClock.At(new DateTimeOffset(2026, 10, 7, 23, 30, 0, TimeSpan.Zero), SessionZones.UnitedStates);
        var outcome = await new FilingsRefresher(first, night, store.DatabaseFile, pause: NoPause).NightAsync(Index, "night-first");

        // Read from the week before the session through the session, the days the archive posted none for recorded
        // as such, the session's own stopping the read: the two pages asked, the facts of the two that filed a report
        // or results, from 19.13 the Form 4 of the member whose insider filed one, after the day's facts, and nothing for
        // the member whose announcement carried none or the filer that is no member, its Form 4 among them.
        Assert.Equal(
            ["2026-09-30", "2026-10-01", "2026-10-02", "2026-10-05", "2026-10-06", "0000000102-26-000002", "0000000103-26-000003", "0000000101", "0000000102", "0000000104-26-000004", "2026-10-07"],
            first.Asked);
        Assert.Equal(["AAA", "BBB"], outcome.Refreshed);
        Assert.Equal(FilingsSession, outcome.NotYetPosted);
        Assert.Equal((6, 2, 2, 1, 11), (outcome.Indexes, outcome.Pages, outcome.FactsAsked, outcome.Ownerships, outcome.Documents));
        Assert.Equal(1, outcome.Trades);
        Assert.Equal(["0000000101|Revenues|2026-06-30|1000", "0000000102|Revenues|2026-06-30|1000"], FilingRows(store, "SELECT cik || '|' || concept || '|' || period_end || '|' || dollars FROM filed_fact ORDER BY cik;"));
        Assert.Equal(["0000000104|DDD|P|100|50"], FilingRows(store, "SELECT cik || '|' || ticker || '|' || code || '|' || shares || '|' || price FROM insider_trade;"));
        Assert.Equal(
            ["2026-09-30|0|0|0", "2026-10-01|0|0|0", "2026-10-02|0|0|0", "2026-10-05|0|0|0", "2026-10-06|1|2|2"],
            FilingRows(store, "SELECT day || '|' || posted || '|' || members || '|' || refreshed FROM filing_day ORDER BY day;"));
        Assert.Equal(
            ["2026-09-30|0|0", "2026-10-01|0|0", "2026-10-02|0|0", "2026-10-05|0|0", "2026-10-06|1|1"],
            FilingRows(store, "SELECT first_day || '|' || filings || '|' || stored FROM insider_read WHERE source = 'filing' ORDER BY first_day;"));
        Assert.Equal(["ok|11|2"], FilingRows(store, $"SELECT outcome || '|' || network_requests || '|' || rows_written FROM run_log WHERE run_id = 'night-first' AND stage = '{FilingsRefresher.Stage}';"));
        Assert.Contains("2026-10-07 not yet posted, read on a later night", FilingRows(store, $"SELECT detail FROM run_log WHERE run_id = 'night-first';").Single(), StringComparison.Ordinal);

        // The next session's night: the archive has now posted Wednesday's index, where CCC files its 10-K. Only that
        // day and the night's own are read, CCC alone is asked for, and nothing already read is read again.
        var second = RecordedFilingsRefreshFeed.Of(
            new Dictionary<DateOnly, string>
            {
                [FilingsSession] = DayIndex(FilingsSession, ("0000000103", "CCC INC", "10-K", "0000000103-26-000013")),
            },
            facts: new Dictionary<string, string>(StringComparer.Ordinal) { ["0000000103"] = OneQuarter("0000000103", "2026-10-07", "0000000103-26-000013") });

        var after = await new FilingsRefresher(second, FixedClock.At(new DateTimeOffset(2026, 10, 8, 23, 30, 0, TimeSpan.Zero), SessionZones.UnitedStates), store.DatabaseFile, pause: NoPause)
            .NightAsync(Index, "night-second");

        Assert.Equal(["2026-10-07", "0000000103", "2026-10-08"], second.Asked);
        Assert.Equal(["CCC"], after.Refreshed);
        Assert.Equal(3, FilingRows(store, "SELECT cik FROM filed_fact;").Count);
        Assert.Equal(["0000000101", "0000000102", "0000000103"], FilingRows(store, "SELECT cik FROM filed_fact_pull ORDER BY cik;"));
    }

    [Fact]
    public void TheDaysARefreshReadsAreTheWeekdaysSinceItsLastReadThroughTheSessionAndNoMoreThanItsCount()
    {
        // None read before: the week before the session, Saturday and Sunday passed over.
        Assert.Equal(
            [new DateOnly(2026, 9, 30), new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 2), new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 6), FilingsSession],
            FilingsRefresher.DaysToRead(null, FilingsSession));

        // A Friday read last: the Monday to the session.
        Assert.Equal(
            [new DateOnly(2026, 10, 5), new DateOnly(2026, 10, 6), FilingsSession],
            FilingsRefresher.DaysToRead(new DateOnly(2026, 10, 2), FilingsSession));

        // The session read already: nothing.
        Assert.Empty(FilingsRefresher.DaysToRead(FilingsSession, FilingsSession));

        // A month unread: the ten weekdays after the last read and no more, the rest on the next night.
        var days = FilingsRefresher.DaysToRead(new DateOnly(2026, 9, 4), FilingsSession);

        Assert.Equal(FilingsRefresher.DaysANight, days.Count);
        Assert.Equal((new DateOnly(2026, 9, 7), new DateOnly(2026, 9, 18)), (days[0], days[^1]));
    }

    // A feed whose archive refuses every document.
    sealed class RefusingArchive : IFilingsRefreshFeed
    {
        public int Documents { get; private set; }

        public Task<string?> DailyIndexAsync(DateOnly day, CancellationToken cancellation = default)
        {
            Documents++;

            throw new ProviderRefusal("The archive refused DailyIndex with status 403.", transient: false);
        }

        public Task<string?> FilingPageAsync(string cik, string accession, CancellationToken cancellation = default) =>
            throw new InvalidOperationException("no page is asked of a refusing archive");

        public Task<IReadOnlyDictionary<string, IReadOnlyList<ConceptFact>>?> FactsAsync(string cik, IReadOnlyList<string> concepts, CancellationToken cancellation = default) =>
            throw new InvalidOperationException("no facts are asked of a refusing archive");

        public Task<string?> OwnershipAsync(string cik, string accession, CancellationToken cancellation = default) =>
            throw new InvalidOperationException("no insider's filing is asked of a refusing archive");

        public Task<string?> FilingListingAsync(string cik, string accession, CancellationToken cancellation = default) =>
            throw new InvalidOperationException("no report's listing is asked of a refusing archive");

        public Task<string?> FilingDocumentAsync(string cik, string accession, string file, CancellationToken cancellation = default) =>
            throw new InvalidOperationException("no report's instance is asked of a refusing archive");
    }

    [Fact]
    public async Task ARefusedArchiveLeavesTheFactsAsTheyWereAndItsDaysForTheNextNightAndALimitPassedAsksNothing()
    {
        using var store = FilingsStore();

        store.Execute("INSERT INTO filed_fact (cik, concept, period_start, period_end, dollars, filed, form, accession, run_id) VALUES ('0000000101', 'Revenues', '2026-01-01', '2026-03-31', '900', '2026-05-01', '10-Q', 'a', 'earlier');");

        var night = FixedClock.At(new DateTimeOffset(2026, 10, 7, 23, 30, 0, TimeSpan.Zero), SessionZones.UnitedStates);
        var refused = await new FilingsRefresher(new RefusingArchive(), night, store.DatabaseFile, pause: NoPause).NightAsync(Index, "night-refused");

        Assert.NotNull(refused.Refusal);
        Assert.Equal(["0000000101|900"], FilingRows(store, "SELECT cik || '|' || dollars FROM filed_fact;"));
        Assert.Empty(FilingRows(store, "SELECT day FROM filing_day;"));
        Assert.Equal(["partial"], FilingRows(store, "SELECT outcome FROM run_log WHERE run_id = 'night-refused';"));
        Assert.Contains("stopped on the archive's refusal, facts left as they were", FilingRows(store, "SELECT detail FROM run_log WHERE run_id = 'night-refused';").Single(), StringComparison.Ordinal);

        // A step whose own limit has passed asks the archive for nothing and leaves every day for the next night.
        var archive = RecordedFilingsRefreshFeed.Of(new Dictionary<DateOnly, string>());
        var limited = await new FilingsRefresher(archive, night, store.DatabaseFile, TimeSpan.Zero, NoPause).NightAsync(Index, "night-limited");

        Assert.Empty(archive.Asked);
        Assert.Empty(limited.Days);
        Assert.Empty(FilingRows(store, "SELECT day FROM filing_day;"));
    }

    // A listing of a report's documents, its instance among them.
    static string ListingOf(params string[] names) =>
        "{\"directory\":{\"item\":[" + string.Join(",", names.Select(name => "{\"name\":\"" + name + "\"}")) + "]}}";

    [Fact]
    public async Task AReportTheFactsAnswerCarriesNoFigureOfIsReadFromItsOwnInstanceAndOneItCarriesIsNot()
    {
        using var store = FilingsStore();

        var tuesday = new DateOnly(2026, 10, 6);

        // On the Tuesday AAA and BBB each file a 10-Q: the facts answer carries AAA's and not yet BBB's, holding only an
        // earlier report of its, and the archive holds BBB's listing and its instance, the captured one served under BBB's
        // report.
        var feed = RecordedFilingsRefreshFeed.Of(
            new Dictionary<DateOnly, string>
            {
                [tuesday] = DayIndex(tuesday, ("0000000101", "AAA INC", "10-Q", "0000000101-26-000001"), ("0000000102", "BBB INC", "10-Q", "0000000102-26-000005")),
            },
            facts: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["0000000101"] = OneQuarter("0000000101", "2026-10-06"),
                ["0000000102"] = OneQuarter("0000000102", "2026-08-01"),
            },
            listings: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["0000000102-26-000005"] = ListingOf("bbb-20260630.htm", "bbb-20260630_htm.xml", "bbb-20260630_lab.xml", "FilingSummary.xml"),
            },
            instances: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["0000000102-26-000005"] = EquityBrief.Tests.Providers.SecEdgarInstanceTests.Abbott,
            });

        var night = FixedClock.At(new DateTimeOffset(2026, 10, 7, 23, 30, 0, TimeSpan.Zero), SessionZones.UnitedStates);
        var outcome = await new FilingsRefresher(feed, night, store.DatabaseFile, pause: NoPause).NightAsync(Index, "night-instance");

        // Each filer's facts once, and BBB's report alone read from its listing and the instance it names.
        Assert.Equal(
            ["0000000101", "0000000102", "0000000102-26-000005 index.json", "0000000102-26-000005 bbb-20260630_htm.xml"],
            feed.Asked.Where(asked => !asked.StartsWith("2026-", StringComparison.Ordinal)));
        Assert.Equal((1, 2, 2, 10), (outcome.Instances, outcome.InstanceDocuments, outcome.FactsAsked, outcome.Documents));
        Assert.Equal(["AAA", "BBB"], outcome.Refreshed);

        // Its figures stored as first filed under the report's accession, filing day and form.
        Assert.Equal(
            ["2025-01-01|2025-06-30|3104000000", "2025-04-01|2025-06-30|1779000000", "2026-01-01|2026-06-30|2005000000", "2026-04-01|2026-06-30|928000000"],
            FilingRows(store, "SELECT period_start || '|' || period_end || '|' || dollars FROM filed_fact WHERE cik = '0000000102' AND concept = 'NetIncomeLoss' ORDER BY period_start, period_end;"));
        Assert.Equal(
            ["2026-10-06|10-Q|0000000102-26-000005"],
            FilingRows(store, "SELECT DISTINCT filed || '|' || form || '|' || accession FROM filed_fact WHERE cik = '0000000102' AND concept <> 'Revenues';"));
        Assert.Empty(FilingRows(store, "SELECT concept FROM filed_fact WHERE cik = '0000000101' AND concept <> 'Revenues';"));
        Assert.Contains("10 document(s) asked of the archive", FilingRows(store, "SELECT detail FROM run_log WHERE run_id = 'night-instance';").Single(), StringComparison.Ordinal);
        Assert.Contains("2 of them a report's listing or instance, 1 report(s) the facts answer carried no figure of read from their own instance", FilingRows(store, "SELECT detail FROM run_log WHERE run_id = 'night-instance';").Single(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheMembersReportsTheStoreHoldsNoFactOfAreReadByHandFromTheirOwnInstance()
    {
        using var store = FilingsStore();

        // The refresh has read through the Wednesday. On the Monday AAA's 10-Q, stored already, BBB's 10-K/A, stored not at
        // all, and a report of a filer that is no member; the archive posted no index for the other days.
        store.Execute("INSERT INTO filing_day (day, run_id, read_at, posted, filings, members, refreshed) VALUES ('2026-10-07', 'earlier', '2026-10-07T23:40:00.000Z', 1, 0, 0, 0);");
        store.Execute("INSERT INTO filed_fact (cik, concept, period_start, period_end, dollars, filed, form, accession, run_id) VALUES ('0000000101', 'Revenues', '2026-04-01', '2026-06-30', '1000', '2026-10-05', '10-Q', '0000000101-26-000007', 'earlier');");

        var monday = new DateOnly(2026, 10, 5);
        var feed = RecordedFilingsRefreshFeed.Of(
            new Dictionary<DateOnly, string>
            {
                [monday] = DayIndex(
                    monday,
                    ("0000000101", "AAA INC", "10-Q", "0000000101-26-000007"),
                    ("0000000102", "BBB INC", "10-K/A", "0000000102-26-000008"),
                    ("0000000999", "NOT A MEMBER INC", "10-Q", "0000000999-26-000009")),
            },
            listings: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["0000000102-26-000008"] = ListingOf("bbb-20260630.htm", "bbb-20260630_htm.xml"),
            },
            instances: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["0000000102-26-000008"] = EquityBrief.Tests.Providers.SecEdgarInstanceTests.Abbott,
            });

        var clock = FixedClock.At(new DateTimeOffset(2026, 10, 8, 14, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates);
        var outcome = await new FilingsRefresher(feed, clock, store.DatabaseFile, pause: NoPause).InstancesAsync(new DateOnly(2026, 10, 1), Index, null, "instances-by-hand");

        // Every weekday's index from the first through the newest read, and BBB's report alone read from its instance.
        Assert.Equal(
            ["2026-10-01", "2026-10-02", "2026-10-05", "0000000102-26-000008 index.json", "0000000102-26-000008 bbb-20260630_htm.xml", "2026-10-06", "2026-10-07"],
            feed.Asked);
        Assert.Equal(["BBB"], outcome.Refreshed);
        Assert.Equal((1, 2, 7), (outcome.Instances, outcome.InstanceDocuments, outcome.Documents));
        Assert.Equal(
            ["2026-10-05|10-K/A|928000000"],
            FilingRows(store, "SELECT filed || '|' || form || '|' || dollars FROM filed_fact WHERE cik = '0000000102' AND concept = 'NetIncomeLoss' AND period_start = '2026-04-01';"));
        Assert.Empty(FilingRows(store, "SELECT concept FROM filed_fact WHERE cik IN ('0000000101', '0000000999') AND accession <> '0000000101-26-000007';"));
        Assert.Equal(
            ["ok|7"],
            FilingRows(store, $"SELECT outcome || '|' || network_requests FROM run_log WHERE run_id = 'instances-by-hand' AND stage = '{FilingsRefresher.InstancesStage}';"));

        // Run again, the report now stored is not asked for.
        var again = RecordedFilingsRefreshFeed.Of(new Dictionary<DateOnly, string> { [monday] = DayIndex(monday, ("0000000102", "BBB INC", "10-K/A", "0000000102-26-000008")) });
        var repeat = await new FilingsRefresher(again, clock, store.DatabaseFile, pause: NoPause).InstancesAsync(monday, Index, null, "instances-again");

        Assert.Equal(["2026-10-05", "2026-10-06", "2026-10-07"], again.Asked);
        Assert.Equal(0, repeat.Instances);
    }

    // A report filed for several registrants together is listed once for each, the index ordering them by name, so a
    // member's report whose first line is another registrant's is still read by hand under the member's own line.
    [Fact]
    public async Task AMembersReportFiledWithOtherRegistrantsIsReadByHandUnderItsOwnLine()
    {
        using var store = FilingsStore();

        store.Execute("INSERT INTO filing_day (day, run_id, read_at, posted, filings, members, refreshed) VALUES ('2026-10-05', 'earlier', '2026-10-05T23:40:00.000Z', 1, 0, 0, 0);");

        var monday = new DateOnly(2026, 10, 5);
        var feed = RecordedFilingsRefreshFeed.Of(
            new Dictionary<DateOnly, string>
            {
                [monday] = DayIndex(
                    monday,
                    ("0000000998", "AAA CAROLINAS LLC", "10-Q", "0000000101-26-000011"),
                    ("0000000101", "AAA INC", "10-Q", "0000000101-26-000011")),
            },
            listings: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["0000000101-26-000011"] = ListingOf("aaa-20260630.htm", "aaa-20260630_htm.xml"),
            },
            instances: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["0000000101-26-000011"] = EquityBrief.Tests.Providers.SecEdgarInstanceTests.Abbott,
            });

        var clock = FixedClock.At(new DateTimeOffset(2026, 10, 6, 14, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates);
        var outcome = await new FilingsRefresher(feed, clock, store.DatabaseFile, pause: NoPause).InstancesAsync(monday, Index, null, "combined-by-hand");

        Assert.Equal(["2026-10-05", "0000000101-26-000011 index.json", "0000000101-26-000011 aaa-20260630_htm.xml"], feed.Asked);
        Assert.Equal(["AAA"], outcome.Refreshed);
        Assert.Equal(1, outcome.Instances);
        Assert.Equal(
            ["2026-10-05|10-Q|928000000"],
            FilingRows(store, "SELECT filed || '|' || form || '|' || dollars FROM filed_fact WHERE cik = '0000000101' AND concept = 'NetIncomeLoss' AND period_start = '2026-04-01';"));
        Assert.Empty(FilingRows(store, "SELECT concept FROM filed_fact WHERE cik = '0000000998';"));
    }
}
