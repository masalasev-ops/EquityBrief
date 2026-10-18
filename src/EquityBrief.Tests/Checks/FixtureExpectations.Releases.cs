using System.Security.Cryptography;
using System.Text;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Releases;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Local;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, from 20.2: the members' results releases, found and kept. The current filings feed, a filer's
// submissions and their further page, and an 8-K's own index page read as the SEC sent them, the accepted instant
// worked by hand at each of New York's offsets; a release's text compressed and read back byte for byte to its hash;
// the early slot over the archive's index storing a member's release, asking nothing beyond the page for an
// announcement carrying no item 2.02 and nothing at all for a filer that is no member or for a report; the late slot
// over the current feed asking a page only for a member's results; the history stopping at its '--until' and going on
// from where it stopped; the replay's own week against the expectation worked by hand; and the slot's row through the
// local run, with a refusal said on it.
// see: A member's results release is the EX-99 exhibit of its 8-K carrying item 2.02, stored compressed with the instant the SEC accepted it
public partial class FixtureExpectations
{
    // The rows 20.2 adds that this check reaches: section 17's row and section 18's row on the SEC refusing a release fetch.
    internal static readonly string[] ReleaseFetchClaims =
    [
        CheckReach.Key(Scope.LimitsTable, "Release fetch's settings"),
        CheckReach.Key(Scope.FailureTable, "The SEC refuses or does not answer a release fetch"),
    ];

    const string KeysCik = "0001601046";
    const string KeysAugust = "0001601046-26-000029";
    const string KeysMay = "0001601046-26-000019";
    const string MicrosoftCik = "0000789019";
    const string MicrosoftSeptember = "0001193125-26-380280";
    const string KeysExhibit = "exhibit991-q326pressrelease.htm";

    [Fact]
    public void TheCurrentFeedTheSubmissionsAndAnIndexPageAreReadAsTheSecSentThem()
    {
        // The current feed's seven entries as captured, each an 8-K or an 8-K/A, its instant read with its offset: GoPro's
        // 17:30:16 at New York's summer offset is 21:30:16 in UTC, worked by hand.
        var current = ResultsReleases.FromCurrent(Captured("current-8k-2026-10-16.atom"));

        Assert.Equal(
            ["0001500435 0001500435-26-000064 8-K 2.02,4.02,8.01 2026-10-16T21:30:16Z",
             "0002009312 0001493152-26-047462 8-K/A 9.01 2026-10-16T21:29:13Z",
             "0001512228 0001539497-26-002705 8-K 2.02,7.01,9.01 2026-10-16T21:15:39Z",
             "0001316835 0001193125-26-424245 8-K/A 5.02 2026-10-16T21:04:30Z",
             "0001512228 0001539497-26-002703 8-K 1.01,1.02 2026-10-16T21:00:37Z",
             "0001551182 0001551182-26-000034 8-K 8.01,9.01 2026-10-16T20:51:43Z",
             "0000033992 0001021771-26-000115 8-K 2.02,7.01,9.01 2026-10-16T20:48:59Z"],
            current.Select(FilingLine));
        Assert.All(current, filing => Assert.Equal(new DateOnly(2026, 10, 16), filing.Filed));

        // A page that is no Atom feed is refused rather than read as a feed listing nothing.
        Assert.Throws<FormatException>(() => ResultsReleases.FromCurrent("<html><body>Your request has been refused.</body></html>"));

        // Keysight's submissions: the two results announcements of their recent window with the SEC's own UTC instants,
        // and the one further page they name, of 2014-03-05 to 2015-12-01.
        var (cik, recent, pages) = ResultsReleases.FromSubmissions(Captured("filings-KEYS.json"));

        Assert.Equal(KeysCik, cik);
        Assert.Equal(
            [$"{KeysAugust} 2026-08-18T20:09:53Z", $"{KeysMay} 2026-05-19T20:08:49Z"],
            recent.Select(filing => FormattableString.Invariant($"{filing.Accession} {filing.Accepted:yyyy-MM-ddTHH:mm:ssZ}")));
        Assert.Equal([new SubmissionsPage("CIK0001601046-submissions-001.json", new DateOnly(2014, 3, 5), new DateOnly(2015, 12, 1))], pages);

        // The further page as captured, trimmed to six filings: its three results announcements and none of the 8-K
        // carrying item 5.02, the 10-Q or the Form 4.
        Assert.Equal(
            ["0001601046-15-000038 2015-11-19T21:19:26Z", "0001601046-15-000027 2015-08-19T20:06:45Z", "0001601046-15-000012 2015-05-19T20:07:21Z"],
            ResultsReleases.FromSubmissionsPage(Captured("submissions-KEYS-001.json"), KeysCik)
                .Select(filing => FormattableString.Invariant($"{filing.Accession} {filing.Accepted:yyyy-MM-ddTHH:mm:ssZ}")));

        // An 8-K's own page prints its accepted instant in New York's time: Keysight's August page 16:09:53 at the summer
        // offset of four hours is 20:09:53 in UTC, the instant its submissions state.
        Assert.Equal(Utc(2026, 8, 18, 20, 9, 53), ResultsReleases.AcceptedOn(Captured("filing-types-KEYS-8k.htm"), NewYork));

        // And at the winter offset of five: its page of 2026-02-23 printed 16:08:46, captured on 2026-10-18 with its
        // submissions stating 2026-02-23T21:08:46.000Z. That page is not committed, its lines carrying whitespace this
        // repository's tools cannot write back; its accepted line, the one the reader reads, is set in August's page.
        var winter = Captured("filing-types-KEYS-8k.htm").Replace("2026-08-18 16:09:53", "2026-02-23 16:08:46", StringComparison.Ordinal);

        Assert.Equal(Utc(2026, 2, 23, 21, 8, 46), ResultsReleases.AcceptedOn(winter, NewYork));

        // A page printing no accepted instant reads none.
        Assert.Null(ResultsReleases.AcceptedOn("<html></html>", NewYork));
    }

    [Fact]
    public void ACompressedReleaseReadsBackByteForByteToItsHash()
    {
        var text = SecEdgarArchive.Plain(Captured("release-KEYS.htm"));
        var kept = ResultsReleases.Compressed(text);

        // The hash of the text's UTF-8 bytes, worked out here with the suite's own hash, and its length in characters,
        // the 22,996 the local run's capture states.
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text))), kept.Sha256);
        Assert.Equal(22996, kept.Length);
        Assert.True(kept.Gzip.Length < Encoding.UTF8.GetByteCount(text) / 2, $"The release compressed to {kept.Gzip.Length} bytes.");

        var back = ResultsReleases.Decompressed(kept.Gzip);

        Assert.Equal(text, back);
        Assert.Equal(kept.Sha256, ResultsReleases.HashOf(back));
    }

    [Fact]
    public async Task AnEarlySlotStoresAMembersReleaseAndAsksNothingForAnAnnouncementCarryingNoResultsOrAFilerThatIsNoMember()
    {
        using var store = ReleaseStore();

        // The newest day the archive's index was read: 2026-08-17, so the slot of 2026-08-19 reads 2026-08-18 alone.
        store.Execute("INSERT INTO release_day (day, source, posted, requests, announcements, results, stored, read_at, run_id) VALUES ('2026-08-17', 'daily index', 1, 1, 0, 0, 0, '2026-08-18T12:05:00Z', 'constructed');");

        // A member whose filer only a pulled company names: a slot reads the night's own companies alone, so it is no
        // filer the slot knows.
        store.Execute("INSERT INTO membership (index_code, ticker, joined, \"left\", observed_at) VALUES ('GSPC', 'OLD', '2026-01-02', NULL, '2026-01-02T00:00:00Z');");
        store.Execute("INSERT INTO pulled_company (ticker, cik, sector, industry_group, industry, sub_industry, delisted_on, pull) VALUES ('OLD', '0000012345', NULL, NULL, NULL, NULL, NULL, 'constructed');");

        var day = DayIndex(
            "1601046|KEYSIGHT TECHNOLOGIES, INC.|8-K|20260818|edgar/data/1601046/" + KeysAugust + ".txt",
            "1601046|KEYSIGHT TECHNOLOGIES, INC.|10-Q|20260818|edgar/data/1601046/0001601046-26-000030.txt",
            "789019|MICROSOFT CORP|8-K|20260818|edgar/data/789019/" + MicrosoftSeptember + ".txt",
            "1500435|GoPro, Inc.|8-K|20260818|edgar/data/1500435/0001500435-26-000064.txt",
            "12345|OLD CO|8-K|20260818|edgar/data/12345/0000012345-26-000001.txt");
        var feed = RecordedReleaseArchiveFeed.Of(
            days: new Dictionary<DateOnly, string> { [new DateOnly(2026, 8, 18)] = day },
            pages: new Dictionary<string, string> { [KeysAugust] = Captured("filing-types-KEYS-8k.htm"), [MicrosoftSeptember] = Captured("filing-page-" + MicrosoftSeptember + ".htm") },
            exhibits: new Dictionary<string, string> { [KeysAugust] = Captured("release-KEYS.htm") },
            submissions: new Dictionary<string, string> { [KeysCik] = Captured("filings-KEYS.json") });
        var clock = FixedClock.At(Utc(2026, 8, 19, 12, 5, 0), SessionZones.UnitedStates);

        var outcome = await new ReleaseFetch(feed, clock, store.DatabaseFile, store.Root, NoWait).SlotAsync(LocalSlots.Early, "early-constructed", Utc(2026, 8, 19, 21, 30, 0));

        // The day's index, Keysight's page, its exhibit and from 20.3 its submissions for its names, and Microsoft's page,
        // whose items carry no 2.02, so its exhibit is never asked; the 10-Q is no announcement, GoPro no member and the
        // member OLD no filer the night's companies name, so none of them is asked for.
        Assert.Equal(["2026-08-18", KeysAugust, KeysAugust + "/" + KeysExhibit, KeysCik, MicrosoftSeptember], feed.Asked);
        Assert.Equal((1, 2, 1, 1, 5, (string?)null), (outcome.Days, outcome.Announcements, outcome.Results, outcome.Stored, outcome.Documents, outcome.Refusal));
        Assert.Equal([$"{KeysCik}|[\"Keysight Technologies, Inc.\"]|[\"KEYS\"]|early-constructed"], Query(store, "SELECT cik, names, tickers, run_id FROM release_filer;"));

        // The release as kept: its instant from the page's 16:09:53 in New York, its exhibit, its address and its text,
        // compressed, back to the hash of the archive's reader's text.
        var text = SecEdgarArchive.Plain(Captured("release-KEYS.htm"));

        Assert.Equal(
            [$"{KeysAugust}|{KeysCik}|8-K|2026-08-18|2026-08-18T20:09:53Z|{KeysExhibit}|https://www.sec.gov/Archives/edgar/data/1601046/000160104626000029/{KeysExhibit}|{ResultsReleases.HashOf(text)}|22996|early-constructed"],
            Query(store, "SELECT accession, cik, form, filed, accepted_at, exhibit, url, text_sha256, text_length, run_id FROM release_document;"));
        Assert.Equal(text, ResultsReleases.Decompressed(Blob(store, "SELECT text_gzip FROM release_document;")));

        // The day's row: posted, five requests, two members' announcements, one carrying results, one stored.
        Assert.Equal(["2026-08-18|1|5|2|1|1"], Query(store, "SELECT day, posted, requests, announcements, results, stored FROM release_day WHERE day = '2026-08-18';"));

        // The next day's slot reads 2026-08-19 alone, which the archive has not posted yet, and asks nothing else.
        var next = RecordedReleaseArchiveFeed.Of();
        var later = await new ReleaseFetch(next, FixedClock.At(Utc(2026, 8, 20, 12, 5, 0), SessionZones.UnitedStates), store.DatabaseFile, store.Root, NoWait).SlotAsync(LocalSlots.Early, "early-next", Utc(2026, 8, 20, 21, 30, 0));

        Assert.Equal(["2026-08-19"], next.Asked);
        Assert.Equal("the archive has not posted its index of 2026-08-19 yet", later.Stopped);
        Assert.Equal(1L, Scalar(store, "SELECT COUNT(*) FROM release_document;"));

        // The days a slot reads, worked by hand: none read yet, the week before through the day before, weekdays alone;
        // and from the day after the newest read.
        Assert.Equal(
            [new DateOnly(2026, 8, 12), new DateOnly(2026, 8, 13), new DateOnly(2026, 8, 14), new DateOnly(2026, 8, 17), new DateOnly(2026, 8, 18)],
            ReleaseFetch.DaysToRead(null, new DateOnly(2026, 8, 19)));
        Assert.Equal([new DateOnly(2026, 8, 17)], ReleaseFetch.DaysToRead(new DateOnly(2026, 8, 14), new DateOnly(2026, 8, 18)));
    }

    [Fact]
    public async Task ALateSlotReadsItsDaysCurrentFilingsAndAsksAPageOnlyForAMembersResults()
    {
        using var store = ReleaseStore(("GPRO", "0001500435"), ("NB", "0001512228"), ("ETN", "0001551182"), ("BLDR", "0001316835"));

        // The captured feed's page, and no page after it. GoPro's and NioCorp's results are the members'; Kingstone's are
        // no member's; Eaton's 8-K, NioCorp's second and Builders FirstSource's 8-K/A carry no results. The pages and the
        // exhibits handed for GoPro and NioCorp are Keysight's, constructed answers standing for theirs.
        var feed = RecordedReleaseArchiveFeed.Of(
            current: [Captured("current-8k-2026-10-16.atom")],
            pages: new Dictionary<string, string> { ["0001500435-26-000064"] = Captured("filing-types-KEYS-8k.htm"), ["0001539497-26-002705"] = Captured("filing-types-KEYS-8k.htm") },
            exhibits: new Dictionary<string, string> { ["0001500435-26-000064"] = Captured("release-KEYS.htm"), ["0001539497-26-002705"] = Captured("release-KEYS.htm") },
            submissions: LateFilers);
        var clock = FixedClock.At(Utc(2026, 10, 16, 21, 40, 0), SessionZones.UnitedStates);

        var outcome = await new ReleaseFetch(feed, clock, store.DatabaseFile, store.Root, NoWait).SlotAsync(LocalSlots.Late, "late-constructed", Utc(2026, 10, 16, 22, 50, 0));

        Assert.Equal(
            ["0", "0001500435-26-000064", "0001500435-26-000064/" + KeysExhibit, "0001500435", "0001539497-26-002705", "0001539497-26-002705/" + KeysExhibit, "0001512228", "100"],
            feed.Asked);
        Assert.Equal((1, 5, 2, 2), (outcome.Pages, outcome.Announcements, outcome.Results, outcome.Stored));
        Assert.Equal(["0001500435|[\"GoPro, Inc.\"]", "0001512228|[\"NioCorp Developments Ltd.\"]"], Query(store, "SELECT cik, names FROM release_filer ORDER BY cik;"));

        // Each kept under the instant the feed stated, with its offset taken off, and the day's row under the instant the
        // slot started.
        Assert.Equal(
            ["0001500435-26-000064|2026-10-16T21:30:16Z", "0001539497-26-002705|2026-10-16T21:15:39Z"],
            Query(store, "SELECT accession, accepted_at FROM release_document ORDER BY accepted_at DESC;"));
        Assert.Equal(["2026-10-16|current filings|1|8|5|2|2|2026-10-16T21:40:00Z"], Query(store, "SELECT day, source, posted, requests, announcements, results, stored, read_at FROM release_day;"));

        // The next morning's early slot, reading 2026-10-16's index alone, finds GoPro's results the late slot stored and
        // asks nothing past the index: counted among the day's results, stored by none.
        store.Execute("INSERT INTO release_day (day, source, posted, requests, announcements, results, stored, read_at, run_id) VALUES ('2026-10-15', 'daily index', 1, 1, 0, 0, 0, '2026-10-16T12:05:00Z', 'constructed');");

        var morning = RecordedReleaseArchiveFeed.Of(
            days: new Dictionary<DateOnly, string> { [new DateOnly(2026, 10, 16)] = DayIndex("1500435|GoPro, Inc.|8-K|20261016|edgar/data/1500435/0001500435-26-000064.txt") });
        var early = await new ReleaseFetch(morning, FixedClock.At(Utc(2026, 10, 17, 12, 5, 0), SessionZones.UnitedStates), store.DatabaseFile, store.Root, NoWait)
            .SlotAsync(LocalSlots.Early, "early-after", Utc(2026, 10, 17, 21, 30, 0));

        Assert.Equal(["2026-10-16"], morning.Asked);
        Assert.Equal((1, 1, 0), (early.Announcements, early.Results, early.Stored));
        Assert.Equal(["2026-10-16|1|1|1|1|0"], Query(store, "SELECT day, posted, requests, announcements, results, stored FROM release_day WHERE source = 'daily index' AND day = '2026-10-16';"));

        // Where a slot run by hand at 21:10 read the feed before, the late slot reads back to that instant alone: GoPro's
        // and NioCorp's results after it are asked for, Builders FirstSource's 8-K/A at 21:04:30, NioCorp's second, Eaton's
        // and Kingstone's before it are not counted, and the page reaching back past it is the last asked.
        using var again = ReleaseStore(("GPRO", "0001500435"), ("NB", "0001512228"), ("ETN", "0001551182"), ("BLDR", "0001316835"));

        again.Execute("INSERT INTO release_day (day, source, posted, requests, announcements, results, stored, read_at, run_id) VALUES ('2026-10-16', 'current filings', 1, 1, 0, 0, 0, '2026-10-16T21:10:00Z', 'constructed');");

        var since = RecordedReleaseArchiveFeed.Of(
            current: [Captured("current-8k-2026-10-16.atom")],
            pages: new Dictionary<string, string> { ["0001500435-26-000064"] = Captured("filing-types-KEYS-8k.htm"), ["0001539497-26-002705"] = Captured("filing-types-KEYS-8k.htm") },
            exhibits: new Dictionary<string, string> { ["0001500435-26-000064"] = Captured("release-KEYS.htm"), ["0001539497-26-002705"] = Captured("release-KEYS.htm") },
            submissions: LateFilers);
        var read = await new ReleaseFetch(since, clock, again.DatabaseFile, again.Root, NoWait).SlotAsync(LocalSlots.Late, "late-again", Utc(2026, 10, 16, 22, 50, 0));

        Assert.Equal(["0", "0001500435-26-000064", "0001500435-26-000064/" + KeysExhibit, "0001500435", "0001539497-26-002705", "0001539497-26-002705/" + KeysExhibit, "0001512228"], since.Asked);
        Assert.Equal((1, 2, 2, 2), (read.Pages, read.Announcements, read.Results, read.Stored));
    }

    [Fact]
    public async Task AFilersFurtherPageOfSubmissionsIsReadWhereItReachesTheYearBeforeItsFirstSession()
    {
        // The further page's filings run to 2015-12-01: read for a filer whose first session is 2016-12-01, twelve months
        // after, and not for one whose first session is a day later.
        var page = new SubmissionsPage("CIK0001601046-submissions-001.json", new DateOnly(2014, 3, 5), new DateOnly(2015, 12, 1));

        Assert.Equal([page], ReleaseFetch.FurtherPages([page], new DateOnly(2016, 12, 1)));
        Assert.Empty(ReleaseFetch.FurtherPages([page], new DateOnly(2016, 12, 2)));

        // A ledger ticker's sessions from 2016-01-04 to 2016-02-01, its filer Keysight's as only its pulled company names
        // it, which the history reads: the further page read, and its release of 2015-11-19, the newest before the first
        // session, stored under the instant its submissions state. The page and the exhibit handed for it are August's,
        // constructed answers standing for its own.
        using var store = ReleaseStore();

        store.Execute("INSERT INTO pulled_company (ticker, cik, sector, industry_group, industry, sub_industry, delisted_on, pull) VALUES ('KEYSOLD', '0001601046', NULL, NULL, NULL, NULL, NULL, 'constructed');");

        foreach (var session in new[] { "2016-01-04", "2016-02-01" })
        {
            store.Execute(
                "INSERT INTO setup (index_code, family, ticker, session_date, rule, live_pass, entry, stop, cap, end, settled, source, pin) "
                + $"VALUES ('GSPC', 'pullback', 'KEYSOLD', '{session}', 'the live rule', 0, '10', '9', 21, 'open', 0, 'history', 'constructed');");
        }

        var feed = RecordedReleaseArchiveFeed.Of(
            submissions: new Dictionary<string, string> { [KeysCik] = Captured("filings-KEYS.json") },
            further: new Dictionary<string, string> { [page.Name] = Captured("submissions-KEYS-001.json") },
            pages: new Dictionary<string, string> { ["0001601046-15-000038"] = Captured("filing-types-KEYS-8k.htm") },
            exhibits: new Dictionary<string, string> { ["0001601046-15-000038"] = Captured("release-KEYS.htm") });
        var clock = new CallClock(Utc(2026, 10, 18, 9, 0, 0));

        var outcome = await new ReleaseFetch(feed, clock, store.DatabaseFile, store.Root, Moving(clock)).HistoryAsync(new DateOnly(2016, 1, 1), null, "history-further", TextWriter.Null);

        Assert.Equal([KeysCik, page.Name, "0001601046-15-000038", "0001601046-15-000038/" + KeysExhibit], feed.Asked);
        Assert.Equal((1, 1), (outcome.Results, outcome.Stored));
        Assert.Equal(["0001601046-15-000038|2015-11-19|2015-11-19T21:19:26Z"], Query(store, "SELECT accession, filed, accepted_at FROM release_document;"));
    }

    [Fact]
    public async Task AHistoryFetchStopsAtItsUntilAndGoesOnFromWhereItStopped()
    {
        using var store = ReleaseStore();

        // Keysight's sessions in the ledger, from 2026-06-01 to 2026-08-31: the release before its first, May's, and the
        // one inside, August's.
        foreach (var session in new[] { "2026-06-01", "2026-08-31" })
        {
            store.Execute(
                "INSERT INTO setup (index_code, family, ticker, session_date, rule, live_pass, entry, stop, cap, end, settled, source, pin) "
                + $"VALUES ('GSPC', 'pullback', 'KEYS', '{session}', 'the live rule', 0, '10', '9', 21, 'open', 0, 'history', 'constructed');");
        }

        RecordedReleaseArchiveFeed Feed() => RecordedReleaseArchiveFeed.Of(
            submissions: new Dictionary<string, string> { [KeysCik] = Captured("filings-KEYS.json") },
            pages: new Dictionary<string, string> { [KeysAugust] = Captured("filing-types-KEYS-8k.htm"), [KeysMay] = Captured("previous-filing-types-KEYS-8k.htm") },
            exhibits: new Dictionary<string, string> { [KeysAugust] = Captured("release-KEYS.htm"), [KeysMay] = Captured("previous-release-KEYS.htm") });

        // A clock that moves a tenth of a second with each request's pace: stopped at a quarter of a second, the run has
        // read the submissions and stored May's release, and stops before August's.
        var clock = new CallClock(Utc(2026, 10, 18, 9, 0, 0));
        var first = Feed();
        var stopped = await new ReleaseFetch(first, clock, store.DatabaseFile, store.Root, Moving(clock)).HistoryAsync(new DateOnly(2019, 1, 2), clock.UtcNow.AddSeconds(0.25), "history-first", TextWriter.Null);

        Assert.Equal([KeysCik, KeysMay, KeysMay + "/" + Exhibit(Captured("previous-filing-types-KEYS-8k.htm"))], first.Asked);
        Assert.Equal(1, stopped.Stored);
        Assert.StartsWith("stopped at 2026-10-18T09:00:00Z as asked", stopped.Stopped, StringComparison.Ordinal);
        Assert.Equal([KeysMay], Query(store, "SELECT accession FROM release_document;"));
        Assert.Equal(["stopped|1|3"], Query(store, $"SELECT outcome, rows_written, network_requests FROM run_log WHERE stage = '{ReleaseFetch.HistoryStage}' AND run_id = 'history-first';"));

        // Run again with no stop: the submissions read again, May's not asked again, August's stored.
        var second = Feed();
        var rest = await new ReleaseFetch(second, clock, store.DatabaseFile, store.Root, Moving(clock)).HistoryAsync(new DateOnly(2019, 1, 2), null, "history-second", TextWriter.Null);

        Assert.Equal([KeysCik, KeysAugust, KeysAugust + "/" + KeysExhibit], second.Asked);
        Assert.Equal((1, (string?)null), (rest.Stored, rest.Stopped));
        Assert.Equal([KeysMay, KeysAugust], Query(store, "SELECT accession FROM release_document ORDER BY accepted_at;"));

        // Which releases a filer's history keeps, worked by hand: the newest filed before its first session and every one
        // filed from it, a release filed on the first session inside and one filed the day before it the one before.
        var filings = ResultsReleases.FromSubmissions(Captured("filings-KEYS.json")).Filings;

        Assert.Equal([KeysMay, KeysAugust], ReleaseFetch.Wanted(filings, new DateOnly(2026, 6, 1), new DateOnly(2026, 8, 31)).Select(filing => filing.Accession));
        Assert.Equal([KeysMay, KeysAugust], ReleaseFetch.Wanted(filings, new DateOnly(2026, 8, 18), new DateOnly(2026, 8, 31)).Select(filing => filing.Accession));
        Assert.Equal([KeysAugust], ReleaseFetch.Wanted(filings, new DateOnly(2026, 8, 19), new DateOnly(2026, 8, 31)).Select(filing => filing.Accession));
    }

    [Fact]
    public async Task TheReplaysEarlySlotReadsTheFixturesWeekAsWorkedOutByHand()
    {
        var expected = Expected("release-fetch");

        Assert.Equal("release-fetch", expected.GetProperty("stage").GetString());
        Assert.Equal("derived", expected.GetProperty("derivation").GetString());
        Assert.Contains("read by hand", expected.GetProperty("derivedFrom").GetString(), StringComparison.Ordinal);
        Assert.Equal(["release_day"], expected.GetProperty("tables").EnumerateArray().Select(table => table.GetString()));

        // The slot the whole replay runs last, over the night's store at the night's clock.
        using var store = await FixtureReplay.ReplayedAsync();
        var feed = RecordedReleaseArchiveFeed.FromFolder(Folder());
        var outcome = await new ReleaseFetch(feed, FixedClock.At(FixtureReplay.Night, SessionZones.UnitedStates), store.DatabaseFile, store.Root, NoWait)
            .SlotAsync(LocalSlots.Early, "replay-releases", DateTimeOffset.MaxValue);

        var source = expected.GetProperty("source").GetString();

        Assert.Equal(
            expected.GetProperty("days").EnumerateArray().Select(day =>
                $"{day.GetProperty("day").GetString()}|{source}|{day.GetProperty("posted").GetInt32()}|{day.GetProperty("requests").GetInt32()}|{day.GetProperty("announcements").GetInt32()}|{day.GetProperty("results").GetInt32()}|{day.GetProperty("stored").GetInt32()}"),
            Query(store, "SELECT day, source, posted, requests, announcements, results, stored FROM release_day ORDER BY day;"));
        Assert.Equal(expected.GetProperty("documents").GetInt32(), outcome.Documents);
        Assert.Equal($"the archive has not posted its index of {expected.GetProperty("unread").GetString()} yet", outcome.Stopped);
        Assert.Equal(0L, Scalar(store, "SELECT COUNT(*) FROM release_document;"));
    }

    [Fact]
    public void AnUntilIsTheFirstInstantNewYorksClockShowsItsTime()
    {
        var seven = new TimeOnly(7, 0);

        // At the summer offset: 05:00 in New York, 07:00 the same morning; at 07:00 itself, the next morning.
        Assert.Equal(Utc(2026, 10, 18, 11, 0, 0), ResultsReleases.UntilNext(seven, Utc(2026, 10, 18, 9, 0, 0), NewYork));
        Assert.Equal(Utc(2026, 10, 19, 11, 0, 0), ResultsReleases.UntilNext(seven, Utc(2026, 10, 18, 11, 0, 0), NewYork));

        // Across the change of 2026-11-01: 08:00 at the summer offset on 2026-10-31, 07:00 the next morning at the winter
        // offset of five hours; and 05:00 on 2026-11-02, 07:00 that morning.
        Assert.Equal(Utc(2026, 11, 1, 12, 0, 0), ResultsReleases.UntilNext(seven, Utc(2026, 10, 31, 12, 0, 0), NewYork));
        Assert.Equal(Utc(2026, 11, 2, 12, 0, 0), ResultsReleases.UntilNext(seven, Utc(2026, 11, 2, 10, 0, 0), NewYork));
    }

    [Fact]
    public async Task ASlotsRowCountsTheReleasesItStoredAndTheSecsRequestsAndSaysWhereTheSecRefused()
    {
        using var store = ReleaseStore();
        using var models = new ModelsFolder();
        var settings = ReadingProfile(models.Root);
        var clock = FixedClock.At(Utc(2026, 8, 19, 12, 5, 0), SessionZones.UnitedStates);

        await new LocalRun(ReadingFeedOver(AnsweringRuntime(loaded: true, chat: _ => Runtime("local-reading-probe-answered.json")), settings), settings, clock, store.DatabaseFile, store.Root, NoWait).PinAsync(TextWriter.Null);
        store.Execute("INSERT INTO release_day (day, source, posted, requests, announcements, results, stored, read_at, run_id) VALUES ('2026-08-17', 'daily index', 1, 1, 0, 0, 0, '2026-08-18T12:05:00Z', 'constructed');");

        var feed = RecordedReleaseArchiveFeed.Of(
            days: new Dictionary<DateOnly, string> { [new DateOnly(2026, 8, 18)] = DayIndex("1601046|KEYSIGHT TECHNOLOGIES, INC.|8-K|20260818|edgar/data/1601046/" + KeysAugust + ".txt") },
            pages: new Dictionary<string, string> { [KeysAugust] = Captured("filing-types-KEYS-8k.htm") },
            exhibits: new Dictionary<string, string> { [KeysAugust] = Captured("release-KEYS.htm") },
            submissions: new Dictionary<string, string> { [KeysCik] = Captured("filings-KEYS.json") });
        var answering = AnsweringRuntime(loaded: true, chat: _ => Runtime("local-reading-KEYS-anonymised.json"));

        var slot = await new LocalRun(ReadingFeedOver(answering, settings), settings, clock, store.DatabaseFile, store.Root, NoWait, new ReleaseFetch(feed, clock, store.DatabaseFile, store.Root, NoWait))
            .SlotAsync(LocalSlots.Early, TextWriter.Null);

        // Finished: one release stored from four requests, its filer's submissions among them, the pin checked after the
        // fetch, and from 20.3 the release read, its row counting the release and its reading.
        Assert.Equal((LocalRun.Finished, 1, 4, 1), (slot.Outcome, slot.Fetched, slot.Requests, slot.Read));
        Assert.Single(answering.Asked, asked => asked.Contains("/chat/completions", StringComparison.Ordinal));
        Assert.Equal(["finished|2|4"], Query(store, $"SELECT outcome, rows_written, network_requests FROM run_log WHERE stage = '{LocalRun.Stage}' AND json_extract(detail, '$.kind') = '{LocalRun.EarlySlot}';"));

        var untouched = AnsweringRuntime(loaded: true, chat: _ => throw new InvalidOperationException("no reading is asked"));

        // An archive refusing the next day's index: the slot's row says the fetch was refused and why, storing nothing more.
        var thursday = FixedClock.At(Utc(2026, 8, 20, 12, 5, 0), SessionZones.UnitedStates);
        var refused = await new LocalRun(ReadingFeedOver(untouched, settings), settings, thursday, store.DatabaseFile, store.Root, NoWait, new ReleaseFetch(new RefusingReleaseFeed(), thursday, store.DatabaseFile, store.Root, NoWait))
            .SlotAsync(LocalSlots.Early, TextWriter.Null);

        Assert.Equal((LocalRun.FetchRefused, "The archive refused DailyIndex with status 403.", 0, 1), (refused.Outcome, refused.Why, refused.Fetched, refused.Requests));
        Assert.Equal(1L, Scalar(store, "SELECT COUNT(*) FROM release_document;"));
    }

    // ---- the release fetch's own helpers ----

    // GoPro's and NioCorp's submissions as the late slot's names read them, constructed: each filer's name and ticker
    // alone, as the SEC's submissions state them.
    static readonly Dictionary<string, string> LateFilers = new(StringComparer.Ordinal)
    {
        ["0001500435"] = """{"cik":"1500435","name":"GoPro, Inc.","tickers":["GPRO"]}""",
        ["0001512228"] = """{"cik":"1512228","name":"NioCorp Developments Ltd.","tickers":["NB"]}""",
    };

    // A migrated store whose members are Keysight and Microsoft and any others named, each with its filer.
    static TemporaryStore ReleaseStore(params (string Ticker, string Cik)[] others)
    {
        var store = new TemporaryStore().Migrated();

        foreach (var (ticker, cik) in new[] { ("KEYS", KeysCik), ("MSFT", MicrosoftCik) }.Concat(others))
        {
            store.Execute($"INSERT INTO membership (index_code, ticker, joined, \"left\", observed_at) VALUES ('GSPC', '{ticker}', '2026-01-02', NULL, '2026-01-02T00:00:00Z');");
            store.Execute($"INSERT INTO company (ticker, fetched_at, cik) VALUES ('{ticker}', '2026-08-01T00:00:00Z', '{cik}');");
        }

        return store;
    }

    // A day's index in the archive's own form, over the lines given.
    static string DayIndex(params string[] lines) =>
        "Description:           Master Index of EDGAR Dissemination Feed\n\n" + SecEdgarDailyIndex.Columns + "\n" + new string('-', 80) + "\n" + string.Join("\n", lines) + "\n";

    // The release exhibit a filing's page names.
    static string Exhibit(string page) => SecEdgarArchive.Release(SecEdgarArchive.Documents(page))!.FileName;

    static string FilingLine(ReleaseFiling filing) =>
        FormattableString.Invariant($"{filing.Cik} {filing.Accession} {filing.Form} {string.Join(',', filing.Items)} {filing.Accepted:yyyy-MM-ddTHH:mm:ssZ}");

    static DateTimeOffset Utc(int year, int month, int day, int hour, int minute, int second) => new(year, month, day, hour, minute, second, TimeSpan.Zero);

    static byte[] Blob(TemporaryStore store, string sql)
    {
        using var connection = store.Open();
        using var command = connection.CreateCommand();

        command.CommandText = sql;

        return (byte[])command.ExecuteScalar()!;
    }

    // An archive refusing every request with a page of its own, as a refusal that is no storage's answer reads.
    sealed class RefusingReleaseFeed : IReleaseArchiveFeed
    {
        public int Documents { get; private set; }

        Task<string?> Refuse(string document)
        {
            Documents++;

            throw new ProviderRefusal($"The archive refused {document} with status 403.", transient: false);
        }

        public Task<string?> CurrentAsync(int start, CancellationToken cancellation = default) => Refuse("CurrentFilings");

        public Task<string?> DailyIndexAsync(DateOnly day, CancellationToken cancellation = default) => Refuse("DailyIndex");

        public Task<string?> FilingPageAsync(string cik, string accession, CancellationToken cancellation = default) => Refuse("FilingIndex");

        public Task<string?> ExhibitAsync(string cik, string accession, string file, CancellationToken cancellation = default) => Refuse("ReleaseExhibit");

        public Task<string?> SubmissionsAsync(string cik, CancellationToken cancellation = default) => Refuse("Submissions");

        public Task<string?> SubmissionsPageAsync(string name, CancellationToken cancellation = default) => Refuse("SubmissionsPage");
    }
}
