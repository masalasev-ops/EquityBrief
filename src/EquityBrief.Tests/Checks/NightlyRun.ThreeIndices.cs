using System.Globalization;
using EquityBrief.Api.Reading;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Web.Marks;
using EquityBrief.Worker;
using EquityBrief.Worker.Bars;
using EquityBrief.Worker.Membership;

namespace EquityBrief.Tests.Checks;

// nightly-run: from 15.1 the night reads the S&P 400's and 600's members beside the S&P 500's, each from its fund's
// holdings file, and every stage that stores or computes what each member needs reads the three indices, while every
// stage that ranks, lists or records reads the S&P 500's alone.
// see: The universe is the S&P 1500's three indices with each member tagged by its index, and membership is fetched
// see: The S&P 400's and 600's members are read each night from their funds' own holdings files
public partial class NightlyRun
{
    const string ThreeIndicesRun = "night-20260908T211000Z";

    // The rows 15.1 adds, each reached here, named beside phase 14's pair as rows after its report.
    internal static readonly string[] ThreeIndicesRows =
    [
        CheckReach.Key(Scope.FailureTable, "A fund's holdings file the night cannot read"),
        CheckReach.Key("15.10 Run", "How last night went, " + MembersPart),
    ];

    // The part the Run page's first region gains, read as one of the parts its row states.
    internal const string MembersPart = "each index's members beside the time to the close on a night that read more than one index";

    static readonly DateOnly FixtureSession = new(2026, 9, 8);

    // A fund's holdings file as iShares serves it: the fund's name, the day its holdings are as of, the columns, a stock
    // a row and the fund's cash after them, which is no member.
    static string Holdings(string fund, string asOf, params string[] tickers) =>
        $"iShares Core S&P {fund} ETF\nFund Holdings as of,\"{asOf}\"\nInception Date,\"May 22, 2000\"\nShares Outstanding,\"1.00\"\n\n" +
        "Ticker,Name,Type,Sector,Asset Class,Market Value,Notional Value,Quantity,Price,Location,Exchange,Currency,FX Rate,Market Currency,Accrual Date,Market Weight,Notional Weight\n" +
        string.Concat(tickers.Select(ticker =>
            $"\"{ticker}\",\"{ticker} COMPANY\",\"EQUITY\",\"Industrials\",\"Equity\",\"1.00\",\"1.00\",\"1.00\",\"1.00\",\"United States\",\"NYSE\",\"USD\",\"1.00\",\"USD\",\"-\",\"0.10\",\"0.10\"\n")) +
        "\"USD\",\"USD CASH\",\"CASH\",\"Cash and/or Derivatives\",\"Cash\",\"0.00\",\"0.00\",\"0.00\",\"100.00\",\"United States\",\"-\",\"USD\",\"1.00\",\"USD\",\"-\",\"0.01\",\"0.01\"\n";

    // The fixture's feeds with the funds' files handed in, and a year served for each name the bulk file carries
    // beside the S&P 500's four, KEYS's year standing in for theirs, since the fixture captured no other.
    static NightFeeds WithFunds(RecordedFundHoldingsFeed funds)
    {
        var folder = FixtureFolder();
        var years = Directory
            .GetFiles(folder, "bars-*.json")
            .ToDictionary(path => Path.GetFileNameWithoutExtension(path)["bars-".Length..], File.ReadAllText, StringComparer.OrdinalIgnoreCase);

        foreach (var ticker in new[] { "A", "AA", "AAL", "XRAY" })
        {
            years[ticker] = years["KEYS"];
        }

        return NightFeeds.FromFixture(folder) with { Historical = new RecordedHistoricalBarFeed(years), Funds = funds };
    }

    static RecordedFundHoldingsFeed Funds(string? midCap, string? smallCap) =>
        new(new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [FundHoldings.MidCapIndex] = midCap ?? "<!doctype html><html><body>iShares Core S&amp;P Mid-Cap ETF</body></html>",
            [FundHoldings.SmallCapIndex] = smallCap ?? "<!doctype html><html><body>iShares Core S&amp;P Small-Cap ETF</body></html>",
        });

    [Fact]
    public async Task TheNightReadsTheThreeIndicesMembersAndEveryStageStoringWhatAMemberNeedsReadsThemWhileTheListReadsTheFiveHundredAlone()
    {
        // The fixture's night with the S&P 400's fund holding AA, XRAY and AAPL and the S&P 600's holding AAL, four
        // names the night's bulk file carries. AAPL is an S&P 500 member, so it is read as the 500's alone. AA has
        // been the 400's since the first of the month under the S&P 500's Technology label, so a group read across the
        // indices would put it beside AAPL, KEYS and MSFT.
        using var store = new TemporaryStore().Migrated();

        store.Execute("INSERT INTO membership (index_code, ticker, joined, \"left\", sector, observed_at) VALUES ('MID', 'AA', '2026-09-01', NULL, 'Technology', '2026-09-01T21:10:00Z');");

        var feeds = WithFunds(Funds(Holdings("Mid-Cap", "Sep 04, 2026", "AA", "XRAY", "AAPL"), Holdings("Small-Cap", "Sep 04, 2026", "AAL")));

        var (code, output, error) = await NightAsync(store, feeds, ThreeIndicesRun, FixedClock.At(Night, SessionZones.UnitedStates), SilentQueue());

        Assert.True(code == 0, error);

        // Each new wider member's span opens on the night's session, tagged by its index and carrying no sector, since
        // a fund's file names GICS's sectors where the S&P 500's rows carry the provider's own; a member the file
        // already listed keeps its span.
        Assert.Equal(
            ["MID AA 2026-09-01 Technology", "MID XRAY 2026-09-08 -", "SML AAL 2026-09-08 -"],
            Texts(store, "SELECT index_code || ' ' || ticker || ' ' || IFNULL(joined, '') || ' ' || IFNULL(sector, '-') FROM membership WHERE index_code <> 'GSPC' ORDER BY 1;"));
        Assert.Equal(0, Count(store, "SELECT COUNT(*) FROM membership WHERE ticker = 'AAPL' AND index_code <> 'GSPC';"));

        var membership = RunLog(store, ThreeIndicesRun).Single(row => row.Stage == MembershipLoader.Stage);

        Assert.Equal("ok", membership.Outcome);
        Assert.Contains("MID: 2 member(s) from IJH's holdings as of 2026-09-04, 1 joining on 2026-09-08, 1 the GSPC holds read as its member alone", membership.Detail, StringComparison.Ordinal);
        Assert.Contains("SML: 1 member(s) from IJR's holdings as of 2026-09-04, 1 joining on 2026-09-08", membership.Detail, StringComparison.Ordinal);

        // One request for the provider's answer and one a fund.
        Assert.Equal(3, Count(store, $"SELECT network_requests FROM run_log WHERE run_id = '{ThreeIndicesRun}' AND stage = 'membership';"));
        Assert.Equal(2, feeds.Funds.Requests);

        // The stages that store or compute what a member needs read the wider members: the night's bars from the one
        // bulk file, a year asked for each, an ask of its quarters, a reading of them and the moves over its bars.
        Assert.Equal(["AA", "AAL", "XRAY"], Texts(store, "SELECT ticker FROM bar WHERE session_date = '2026-09-08' AND ticker IN ('AA', 'AAL', 'XRAY') ORDER BY ticker;"));
        Assert.Contains("backfill: ", output, StringComparison.Ordinal);
        Assert.Equal(7, Count(store, $"SELECT network_requests FROM run_log WHERE run_id = '{ThreeIndicesRun}' AND stage = 'backfill';"));
        Assert.Equal(["AA", "AAL", "XRAY"], Texts(store, "SELECT ticker FROM quarter_ask WHERE ticker IN ('AA', 'AAL', 'XRAY') ORDER BY ticker;"));
        Assert.Equal(3, Count(store, "SELECT COUNT(DISTINCT ticker) FROM fundamental_reading WHERE ticker IN ('AA', 'AAL', 'XRAY');"));

        // The stages that rank, list or record read the S&P 500's four members alone.
        Assert.Equal(4, Count(store, "SELECT COUNT(*) FROM listing WHERE session_date = '2026-09-08';"));
        Assert.Equal(4, Count(store, "SELECT COUNT(*) FROM gate_result WHERE session_date = '2026-09-08';"));
        Assert.Equal(4, Count(store, "SELECT COUNT(*) FROM swing_reading WHERE session_date = '2026-09-08';"));
        Assert.Equal(4, Count(store, "SELECT COUNT(DISTINCT ticker) FROM ladder;"));

        // A name's group is read among its own index's members: the fixture's groups as the S&P 500 alone gives them,
        // each Technology name beside the other two and NFLX beside nobody, AA's label reaching none of them.
        Assert.Equal(["2"], Texts(store, "SELECT DISTINCT CAST(group_members AS TEXT) FROM move WHERE ticker = 'AAPL';"));
        Assert.Equal(["0"], Texts(store, "SELECT DISTINCT CAST(group_members AS TEXT) FROM move WHERE ticker = 'NFLX';"));

        // The close counts each index's members, and the run page draws them beside the time to the close.
        var close = RunLog(store, ThreeIndicesRun).Single(row => row.Stage == "close");

        Assert.Contains("; members on the session: GSPC 4, MID 2, SML 1", close.Detail, StringComparison.Ordinal);

        var after = new DateTimeOffset(2026, 9, 9, 1, 0, 0, TimeSpan.Zero);
        var night = RunScreen.Night(await new ReadApi(store.DatabaseFile, FixedClock.At(after, SessionZones.UnitedStates)).RunLogAsync(FixtureSession), FixtureSession, after, RetryPolicy.Standard.Deadline);

        Assert.Equal([new IndexMembers("GSPC", 4), new IndexMembers("MID", 2), new IndexMembers("SML", 1)], night.Members);

        var region = new MarkRenderer().NightStatus(night);

        Assert.Contains("data-members=\"GSPC 4, MID 2, SML 1\"", region, StringComparison.Ordinal);
        Assert.Contains(
            FormattableString.Invariant($"Members read in {night.Seconds:0} seconds to the close: 4 of the S&amp;P 500, 2 of the S&amp;P 400 and 1 of the S&amp;P 600."),
            region,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ANightReadingOneIndexDrawsNoLineOfMembersAndStoresNoWiderRow()
    {
        // The fixture's own night holds no fund's file, so it reads its own index alone, as every night before 15.1.
        using var store = new TemporaryStore();

        var (code, _, error) = await NightAsync(store, NightFeeds.FromFixture(FixtureFolder()), ThreeIndicesRun, FixedClock.At(Night, SessionZones.UnitedStates));

        Assert.True(code == 0, error);
        Assert.Equal(0, Count(store, "SELECT COUNT(*) FROM membership WHERE index_code <> 'GSPC';"));
        Assert.DoesNotContain("members on the session", RunLog(store, ThreeIndicesRun).Single(row => row.Stage == "close").Detail, StringComparison.Ordinal);

        var after = new DateTimeOffset(2026, 9, 9, 1, 0, 0, TimeSpan.Zero);
        var night = RunScreen.Night(await new ReadApi(store.DatabaseFile, FixedClock.At(after, SessionZones.UnitedStates)).RunLogAsync(FixtureSession), FixtureSession, after, RetryPolicy.Standard.Deadline);

        Assert.Null(night.Members);
        Assert.DoesNotContain("ns-members", new MarkRenderer().NightStatus(night), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AFundsFileTheNightCannotReadKeepsItsIndexsMembersOfTheNightBeforeAndSaysSo()
    {
        using var store = new TemporaryStore().Migrated();

        var provider = RecordedIndexMembershipFeed.FromFile(Path.Combine(FixtureFolder(), "index-constituents.json"));
        var tuesday = FixedClock.At(Night, SessionZones.UnitedStates);
        var wednesday = FixedClock.At(Night.AddDays(1), SessionZones.UnitedStates);

        await new MembershipLoader(provider, tuesday, store.DatabaseFile, Funds(Holdings("Mid-Cap", "Sep 04, 2026", "AA", "XRAY"), Holdings("Small-Cap", "Sep 04, 2026", "AAL")))
            .LoadAsync("GSPC", "night-first");

        // The next night the S&P 400's fund answers its product page, and the 600's lists another name.
        await new MembershipLoader(provider, wednesday, store.DatabaseFile, Funds(null, Holdings("Small-Cap", "Sep 08, 2026", "AAL", "A")))
            .LoadAsync("GSPC", "night-unread");

        Assert.Equal(
            ["MID AA 2026-09-08", "MID XRAY 2026-09-08", "SML A 2026-09-09", "SML AAL 2026-09-08"],
            Texts(store, "SELECT index_code || ' ' || ticker || ' ' || joined FROM membership WHERE index_code <> 'GSPC' AND \"left\" IS NULL ORDER BY 1;"));

        var row = RunLog(store, "night-unread").Single();

        Assert.Equal(MembershipLoader.Held, row.Outcome);
        Assert.Contains("MID: its fund's holdings file could not be read, so its members of the night before are kept: The IJH holdings file states no date its holdings are as of, so it was not read.", row.Detail, StringComparison.Ordinal);
        Assert.Contains("SML: 2 member(s) from IJR's holdings as of 2026-09-08, 1 joining on 2026-09-09", row.Detail, StringComparison.Ordinal);

        // The run page's stale-and-failed region names the step and why.
        var api = new ReadApi(store.DatabaseFile, wednesday);
        var failed = RunScreen.Failed(RunScreen.Stages(await api.RunLogAsync(new DateOnly(2026, 9, 9))));
        var region = new MarkRenderer().StaleAndFailed([], failed, [], []);

        Assert.Equal((MembershipLoader.Stage, MembershipLoader.Held), (Assert.Single(failed).Stage, Assert.Single(failed).Outcome));
        Assert.Contains("holdings file could not be read, so its members of the night before are kept", System.Net.WebUtility.HtmlDecode(region), StringComparison.Ordinal);

        // The failure table states it, read off its own cell.
        var document = Corpus.Read("docs/ARCHITECTURE.html");
        var at = document.IndexOf("<td>A fund's holdings file the night cannot read</td>", StringComparison.Ordinal);

        Assert.True(at >= 0, "Section 18 carries no row for a fund's file the night cannot read.");
        Assert.Contains("changes no span of its index", document[at..document.IndexOf("</tr>", at, StringComparison.Ordinal)], StringComparison.Ordinal);
    }

    [Fact]
    public async Task ANameMovingBetweenIndicesClosesOneSpanAndOpensAnotherTheSameNight()
    {
        using var store = new TemporaryStore().Migrated();

        var provider = RecordedIndexMembershipFeed.FromFile(Path.Combine(FixtureFolder(), "index-constituents.json"));

        await new MembershipLoader(provider, FixedClock.At(Night, SessionZones.UnitedStates), store.DatabaseFile, Funds(Holdings("Mid-Cap", "Sep 04, 2026", "AA", "XRAY"), Holdings("Small-Cap", "Sep 04, 2026", "AAL")))
            .LoadAsync("GSPC", "night-before");

        // AA moves from the S&P 400 to the 600, and the 400's fund still lists KEYS, an S&P 500 member.
        await new MembershipLoader(provider, FixedClock.At(Night.AddDays(1), SessionZones.UnitedStates), store.DatabaseFile, Funds(Holdings("Mid-Cap", "Sep 08, 2026", "XRAY", "KEYS"), Holdings("Small-Cap", "Sep 08, 2026", "AAL", "AA")))
            .LoadAsync("GSPC", "night-moved");

        Assert.Equal(
            ["MID AA 2026-09-08 2026-09-09", "MID XRAY 2026-09-08 -", "SML AA 2026-09-09 -", "SML AAL 2026-09-08 -"],
            Texts(store, "SELECT index_code || ' ' || ticker || ' ' || joined || ' ' || IFNULL(\"left\", '-') FROM membership WHERE index_code <> 'GSPC' ORDER BY 1;"));

        var row = RunLog(store, "night-moved").Single();

        Assert.Equal("ok", row.Outcome);
        Assert.Contains("MID: 1 member(s) from IJH's holdings as of 2026-09-08, 0 joining on 2026-09-09, 1 the GSPC holds read as its member alone; 1 ticker(s) the feed no longer lists, each left the index on 2026-09-09: AA", row.Detail, StringComparison.Ordinal);
        Assert.Contains("SML: 2 member(s) from IJR's holdings as of 2026-09-08, 1 joining on 2026-09-09", row.Detail, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(FundHoldings.MidCapIndex, MembershipLoader.MostUnlistedFromTheMidCapFile, false)]
    [InlineData(FundHoldings.MidCapIndex, MembershipLoader.MostUnlistedFromTheMidCapFile + 1, true)]
    [InlineData(FundHoldings.SmallCapIndex, MembershipLoader.MostUnlistedFromTheSmallCapFile, false)]
    [InlineData(FundHoldings.SmallCapIndex, MembershipLoader.MostUnlistedFromTheSmallCapFile + 1, true)]
    public async Task AFundThatStopsListingMoreTickersThanItsIndexClosesInANightClosesNone(string index, int unlisted, bool held)
    {
        using var store = new TemporaryStore().Migrated();

        var provider = RecordedIndexMembershipFeed.FromFile(Path.Combine(FixtureFolder(), "index-constituents.json"));
        var dropped = Enumerable.Range(0, unlisted).Select(at => $"Z{at:D3}").ToArray();
        var kept = new[] { "AA", "XRAY" };

        string File(string fund, params string[] tickers) => Holdings(fund, "Sep 04, 2026", tickers);

        var first = index == FundHoldings.MidCapIndex
            ? Funds(File("Mid-Cap", [.. kept, .. dropped]), File("Small-Cap", "AAL"))
            : Funds(File("Mid-Cap", kept), File("Small-Cap", ["AAL", .. dropped]));
        var second = Funds(File("Mid-Cap", kept), File("Small-Cap", "AAL"));

        await new MembershipLoader(provider, FixedClock.At(Night, SessionZones.UnitedStates), store.DatabaseFile, first).LoadAsync("GSPC", "night-listed");
        await new MembershipLoader(provider, FixedClock.At(Night.AddDays(1), SessionZones.UnitedStates), store.DatabaseFile, second).LoadAsync("GSPC", "night-unlisted");

        Assert.Equal(held ? unlisted : 0, Count(store, $"SELECT COUNT(*) FROM membership WHERE index_code = '{index}' AND ticker LIKE 'Z%' AND \"left\" IS NULL;"));
        Assert.Equal(held ? 0 : unlisted, Count(store, $"SELECT COUNT(*) FROM membership WHERE index_code = '{index}' AND ticker LIKE 'Z%' AND \"left\" = '2026-09-09';"));

        var row = RunLog(store, "night-unlisted").Single();

        Assert.Equal(held ? MembershipLoader.Held : "ok", row.Outcome);

        if (held)
        {
            Assert.Contains(
                $"{index}: {(index == FundHoldings.MidCapIndex ? kept.Length : 1)} member(s)",
                row.Detail,
                StringComparison.Ordinal);
            Assert.Contains(
                $"{unlisted} ticker(s) the feed no longer lists, more than the {MembershipLoader.MostUnlistedFor(index)} a night closes, so none was closed",
                row.Detail,
                StringComparison.Ordinal);
        }

        // The figures the failure table states, read off its own cell.
        var document = Corpus.Read("docs/ARCHITECTURE.html");
        var at = document.IndexOf("<td>A ticker the index feed stops listing</td>", StringComparison.Ordinal);
        var does = document[at..document.IndexOf("</tr>", at, StringComparison.Ordinal)].Split("</td>")[1];

        Assert.Contains(
            $"more than {MembershipLoader.MostUnlistedFromTheMidCapFile} of the 400's or {MembershipLoader.MostUnlistedFromTheSmallCapFile} of the 600's tickers in one night",
            does,
            StringComparison.Ordinal);
    }

    [Fact]
    public void AFundsFileIsReadForItsStocksAloneAndAPageThatIsNoHoldingsFileIsRefused()
    {
        // The captured files, three rows of each as served: the stocks as they trade, the date they are as of.
        var folder = FixtureFolder();
        var small = FundHoldings.Parse(File.ReadAllText(Path.Combine(folder, "phase15-ishares-IJR-holdings.csv")), "IJR");
        var mid = FundHoldings.Parse(File.ReadAllText(Path.Combine(folder, "phase15-ishares-IJH-holdings.csv")), "IJH");

        Assert.Equal(new DateOnly(2026, 10, 1), small.AsOf);
        Assert.Equal(["FORM", "CSW", "UPBD"], small.Holdings.Select(holding => holding.Ticker));
        Assert.Equal(["GAP", "CDP", "FLG"], mid.Holdings.Select(holding => holding.Ticker));
        Assert.Equal(("FORMFACTOR INC", "Information Technology"), (small.Holdings[0].Name, small.Holdings[0].Sector));

        // A swap on a member, a warrant with no ticker, an index future and cash are no member, and a class of
        // shares named with a space is the provider's with a hyphen. A swap can stand on a name the fund holds no
        // stock of, as IJR's file of 2026-10-01 held one on FG, which a reader taking swaps would make a member.
        const string Columns = "Ticker,Name,Type,Sector,Asset Class,Market Value,Notional Value,Quantity,Price,Location,Exchange,Currency,FX Rate,Market Currency,Accrual Date,Market Weight,Notional Weight\n";

        static string Row(string ticker, string type, string asset) =>
            $"\"{ticker}\",\"{ticker} NAME\",\"{type}\",\"Industrials\",\"{asset}\",\"1.00\",\"1,000.00\",\"1.00\",\"1.00\",\"United States\",\"NYSE\",\"USD\",\"1.00\",\"USD\",\"-\",\"0.10\",\"0.10\"\n";

        var mixed = FundHoldings.Parse(
            "iShares Core S&P Mid-Cap ETF\nFund Holdings as of,\"Oct 01, 2026\"\n\n" + Columns +
            Row("MOG A", "EQUITY", "Equity") + Row("CNO", "SWAP", "Equity") + Row("FG", "SWAP", "Equity") + Row("-", "WARRANT", "Equity") + Row("FAZ6", "INDEX", "Futures") + Row("USD", "CASH", "Cash") + Row("CNO", "EQUITY", "Equity"),
            "IJH");

        Assert.Equal(["MOG-A", "CNO"], mixed.Holdings.Select(holding => holding.Ticker));

        // A page that is no holdings file is refused, naming what it lacks.
        Assert.Contains("states no date", Assert.Throws<ProviderRefusal>(() => FundHoldings.Parse("<!doctype html><html></html>", "IJH")).Message, StringComparison.Ordinal);
        Assert.Contains("carries no row of column names", Assert.Throws<ProviderRefusal>(() => FundHoldings.Parse("Fund Holdings as of,\"Oct 01, 2026\"\n", "IJH")).Message, StringComparison.Ordinal);
        Assert.Contains("carries no column naming each holding's type", Assert.Throws<ProviderRefusal>(() => FundHoldings.Parse("Fund Holdings as of,\"Oct 01, 2026\"\nTicker,Name\n\"AA\",\"ALCOA\"\n", "IJH")).Message, StringComparison.Ordinal);
        Assert.Contains("lists no stock", Assert.Throws<ProviderRefusal>(() => FundHoldings.Parse("Fund Holdings as of,\"Oct 01, 2026\"\n" + Columns + Row("USD", "CASH", "Cash"), "IJH")).Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheMembersVerbRunsTheNightsMembershipAndBackfillStepsByHandAndNothingElse()
    {
        using var store = new TemporaryStore().Migrated();

        var feeds = WithFunds(Funds(Holdings("Mid-Cap", "Sep 04, 2026", "AA", "XRAY"), Holdings("Small-Cap", "Sep 04, 2026", "AAL")));
        var output = new StringWriter();
        var error = new StringWriter();

        var code = await MembersVerb.RunAsync([], () => feeds, FixedClock.At(Night, SessionZones.UnitedStates), store.DatabaseFile, output, error);

        Assert.True(code == 0, error.ToString());
        Assert.StartsWith("members: ", output.ToString(), StringComparison.Ordinal);
        Assert.Equal(3, Count(store, "SELECT COUNT(*) FROM membership WHERE index_code <> 'GSPC';"));

        // Its two steps alone, under a run id no page reads as a night.
        Assert.Equal(
            [MembershipLoader.Stage, Backfill.Stage],
            Texts(store, "SELECT stage FROM run_log ORDER BY rowid;"));
        Assert.All(Texts(store, "SELECT run_id FROM run_log;"), run => Assert.StartsWith(MembersVerb.ByHandPrefix, run, StringComparison.Ordinal));
        Assert.Equal(0, Count(store, "SELECT COUNT(*) FROM listing;") + Count(store, "SELECT COUNT(*) FROM gate_result;"));
    }

    [Fact]
    public async Task TheIndexAndCreditFundsArePulledWholeUnderAPullOfTheirOwn()
    {
        using var store = new TemporaryStore().Migrated();

        var bars = new RecordedHistoricalBarFeed(MarketSeriesFetcher.IndexAndCreditFunds
            .Where(fund => fund != "HYG")
            .ToDictionary(fund => fund, fund => "[{\"date\":\"2026-09-04\",\"open\":10,\"high\":11,\"low\":9,\"close\":10.5,\"adjusted_close\":10.5,\"volume\":100},{\"date\":\"2026-09-08\",\"open\":10.5,\"high\":12,\"low\":10,\"close\":11,\"adjusted_close\":11,\"volume\":100}]"));

        var outcome = await HistoryPull.PullSectorFundsAsync(bars, FixedClock.At(Night, SessionZones.UnitedStates), store.DatabaseFile, new DateOnly(2026, 9, 1), "history-pull-index-funds-1", funds: MarketSeriesFetcher.IndexAndCreditFunds, stage: HistoryPull.IndexFundsStage);

        Assert.Equal(["SPY", "IJH", "IJR", "HYG"], MarketSeriesFetcher.IndexAndCreditFunds);
        Assert.Equal(["IJH", "IJR", "SPY"], outcome.Stored.Select(series => series.Series).Order(StringComparer.Ordinal));
        Assert.Single(outcome.Refused, line => line.StartsWith("HYG: ", StringComparison.Ordinal));
        Assert.Equal(6, Count(store, "SELECT COUNT(*) FROM pulled_market_bar WHERE pull = 'history-pull-index-funds-1';"));
        Assert.Equal([HistoryPull.IndexFundsStage], Texts(store, "SELECT stage FROM run_log;"));
        Assert.StartsWith("pulled the index and credit funds 2026-09-01 to 2026-09-08: 3 of 4 fund(s) answered", HistoryPull.Detail(outcome, "index and credit funds", 4), StringComparison.Ordinal);

        // And each is a fund whose held session the night may write again.
        Assert.All(MarketSeriesFetcher.IndexAndCreditFunds, fund => Assert.True(MarketSeriesFetcher.IsFund(fund), fund));
    }
}
