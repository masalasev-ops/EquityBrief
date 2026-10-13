using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Bars;
using EquityBrief.Worker.Membership;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Tests.Bars;

// A member whose row the provider's day file refused, asked of Yahoo with Nasdaq's check once every file is read.
//
// The parsers are read over the answers each source sent for LUMN on the evening of 2026-10-12, when the provider's row
// opened at 5.25 above a high of 5.05; the rule and the fetcher are worked by hand over constructed answers.
// see: A member's day bar the provider sends impossible is skipped and asked of Yahoo with Nasdaq's check once every file is read
public class SubstituteBarTests
{
    const string Fixture = "membership-2026-09-05";
    const string Index = "GSPC";

    static readonly DateOnly Lumn = new(2026, 10, 12);
    static readonly DateOnly Night = new(2026, 9, 8);
    static readonly DateTimeOffset Evening = new(2026, 9, 8, 21, 10, 0, TimeSpan.Zero);
    static readonly DateTimeOffset Backfilled = new(2026, 9, 5, 21, 10, 0, TimeSpan.Zero);

    static string FixtureFolder() => Path.Combine(Repository.Root, "fixtures", Fixture);

    static string Captured(string file) => File.ReadAllText(Path.Combine(FixtureFolder(), file));

    [Fact]
    public void TheCapturedAnswersReadAsEachSourceSentThem()
    {
        // The provider's own row for the session, asked again by ticker after the night stopped: the same impossible
        // row the day file carried, so asking the provider again repairs nothing.
        using (var provider = JsonDocument.Parse(Captured("substitute-eodhd-LUMN.json")))
        {
            var rows = provider.RootElement.EnumerateArray().ToArray();

            Assert.True(ProviderBarReader.TryRead(rows[1], "LUMN", out var friday));
            Assert.Equal(new DateOnly(2026, 10, 9), friday!.SessionDate);

            var refusal = Assert.Throws<FormatException>(() => ProviderBarReader.TryRead(rows[2], "LUMN", out _));

            Assert.Contains("LUMN on 2026-10-12 adjusts to low 5.05, open 5.25, high 5.05, close 5.05", refusal.Message, StringComparison.Ordinal);
        }

        // Yahoo's chart: four sessions, each price read from its single-precision text and carried to four places.
        var bars = SubstituteBars.YahooChart(Captured("substitute-yahoo-LUMN.json"));

        Assert.Equal(
            [new DateOnly(2026, 10, 7), new DateOnly(2026, 10, 8), new DateOnly(2026, 10, 9), Lumn],
            bars.Select(bar => bar.Session));
        Assert.Equal(new DayBar(Lumn, 5.25m, 5.28m, 4.99m, 5.05m, 11781343), bars[3]);
        Assert.Equal(5.47m, bars[1].Low);

        // Nasdaq's quote after the close: the session it last closed, that close and the session's range.
        Assert.Equal(new DayRange(Lumn, 5.28m, 4.99m, 5.05m), SubstituteBars.NasdaqInfo(Captured("substitute-nasdaq-info-LUMN.json")));

        // Nasdaq's history on the session's own evening holds the sessions before it and not the session itself.
        var history = SubstituteBars.NasdaqHistory(Captured("substitute-nasdaq-history-LUMN.json"));

        Assert.Equal([new DateOnly(2026, 10, 9), new DateOnly(2026, 10, 8), new DateOnly(2026, 10, 7)], history.Select(range => range.Session));
        Assert.Equal(new DayRange(new DateOnly(2026, 10, 8), 5.88m, 5.465m, 5.56m), history[1]);

        // Inside the regular session the quote names no closed session, and none is read; an answer holding no table
        // holds no session.
        var open = Captured("substitute-nasdaq-info-LUMN.json").Replace("Closed at Oct 12, 2026 4:00 PM ET", "Oct 13, 2026 11:02 AM ET", StringComparison.Ordinal);

        Assert.Null(SubstituteBars.NasdaqInfo(open));
        Assert.Empty(SubstituteBars.NasdaqHistory("""{"data":null,"message":"no data","status":{"rCode":200}}"""));
    }

    // Answers each request with what the function gives for its address, and keeps every address asked.
    sealed class Answering(Func<Uri, string> answer) : HttpMessageHandler
    {
        public List<Uri> Asked { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Asked.Add(request.RequestUri!);

            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(answer(request.RequestUri!)) });
        }
    }

    [Fact]
    public async Task TheLiveFeedAsksTheWeekToTheSessionAndReadsTheQuoteWhereTheHistoryHoldsNoRow()
    {
        // Nasdaq's history asked for one day alone answers that the date is less than the date it starts from and holds
        // no row, as it did for LUMN's session asked so, so it is asked for the week to the session.
        var yahoo = new Answering(_ => Captured("substitute-yahoo-LUMN.json"));
        var nasdaq = new Answering(uri => uri.AbsolutePath.EndsWith("/historical", StringComparison.Ordinal)
            ? Captured("substitute-nasdaq-history-LUMN.json")
            : Captured("substitute-nasdaq-info-LUMN.json"));
        var feed = new YahooNasdaqBarFeed(
            new HttpClient(yahoo) { BaseAddress = new Uri(YahooNasdaqBarFeed.YahooAddress) },
            new HttpClient(nasdaq) { BaseAddress = new Uri(YahooNasdaqBarFeed.NasdaqAddress) });

        Assert.Equal(new DayBar(Lumn, 5.25m, 5.28m, 4.99m, 5.05m, 11781343), await feed.BarAsync("LUMN", Lumn));
        Assert.Equal(new DayRange(Lumn, 5.28m, 4.99m, 5.05m), await feed.RangeAsync("LUMN", Lumn));
        Assert.Equal(3, feed.Requests);
        Assert.Equal("/api/quote/LUMN/historical?assetclass=stocks&fromdate=2026-10-05&todate=2026-10-12&limit=10", nasdaq.Asked[0].PathAndQuery);
        Assert.Equal("/api/quote/LUMN/info?assetclass=stocks", nasdaq.Asked[1].PathAndQuery);

        // An older session the history holds is read from it, and the quote is not asked.
        Assert.Equal(new DayRange(new DateOnly(2026, 10, 8), 5.88m, 5.465m, 5.56m), await feed.RangeAsync("LUMN", new DateOnly(2026, 10, 8)));
        Assert.Equal(3, nasdaq.Asked.Count);

        // Yahoo names a class of shares with a hyphen where the store writes a dot.
        await feed.BarAsync("BRK.B", Lumn);

        Assert.Equal("/v8/finance/chart/BRK-B", yahoo.Asked[1].AbsolutePath);
    }

    // LUMN's refused row as the provider's file stated it: open 5.25, close and adjusted close 5.05.
    static readonly UnreadableRow Refused = new("LUMN", "LUMN on 2026-10-12 adjusts to low 5.05, open 5.25, high 5.05, close 5.05", 5.25m, 5.05m, 5.05m);

    static readonly DayBar Yahoo = new(Lumn, 5.25m, 5.28m, 4.99m, 5.05m, 11781343);

    static readonly DayRange Nasdaq = new(Lumn, 5.28m, 4.99m, 5.05m);

    [Fact]
    public void TheRuleStoresLumnsBarAndRefusesOnEachCheckWorkedByHand()
    {
        var (bar, why) = SubstituteBars.Judge(Refused, Lumn, Yahoo, Nasdaq);

        Assert.Equal(new ProviderBar(Lumn, 5.25m, 5.28m, 4.99m, 5.05m, 5.05m, 11781343), bar);
        Assert.Equal("stored from Yahoo, checked against Nasdaq", why);

        // The open half a cent from the provider's is within the tolerance, and a hundredth of a cent past it is not.
        Assert.NotNull(SubstituteBars.Judge(Refused, Lumn, Yahoo with { Open = 5.255m, High = 5.28m }, Nasdaq).Bar);
        Assert.Equal(
            "Yahoo's open 5.2551 stands 0.0051 from the provider's 5.25",
            SubstituteBars.Judge(Refused, Lumn, Yahoo with { Open = 5.2551m }, Nasdaq).Why);
        Assert.Equal(
            "Yahoo's close 5.04 stands 0.01 from the provider's 5.05",
            SubstituteBars.Judge(Refused, Lumn, Yahoo with { Close = 5.04m }, Nasdaq with { Close = 5.04m }).Why);

        // A refused row stating no open or close is checked against Nasdaq alone.
        Assert.NotNull(SubstituteBars.Judge(Refused with { Open = null, Close = null, AdjustedClose = null }, Lumn, Yahoo, Nasdaq).Bar);

        Assert.Equal("Yahoo holds no bar for 2026-10-12", SubstituteBars.Judge(Refused, Lumn, null, Nasdaq).Why);
        Assert.Equal(
            "Yahoo's newest bar is for 2026-10-09 and not 2026-10-12",
            SubstituteBars.Judge(Refused, Lumn, Yahoo with { Session = new DateOnly(2026, 10, 9) }, Nasdaq).Why);
        Assert.Equal(
            "Yahoo's bar for 2026-10-12, low 4.99, open 5.25, high 5.05, close 5.05, could not have traded either",
            SubstituteBars.Judge(Refused, Lumn, Yahoo with { High = 5.05m }, Nasdaq).Why);
        Assert.Equal("Nasdaq states no high and low for 2026-10-12", SubstituteBars.Judge(Refused, Lumn, Yahoo, null).Why);
        Assert.Equal(
            "Yahoo's high 5.28, low 4.99 and close 5.05 are not Nasdaq's 5.29, 4.99 and 5.05",
            SubstituteBars.Judge(Refused, Lumn, Yahoo, Nasdaq with { High = 5.29m }).Why);
        Assert.Null(SubstituteBars.Judge(Refused, Lumn, Yahoo, Nasdaq with { Low = 4.98m }).Bar);

        // At the provider's own adjustment where its row stated two closes: an adjusted close of half the close halves
        // every price, and the raw close stays the session's own.
        var halved = SubstituteBars.Judge(Refused with { AdjustedClose = 2.525m }, Lumn, Yahoo, Nasdaq).Bar;

        Assert.Equal(new ProviderBar(Lumn, 2.625m, 2.64m, 2.495m, 2.525m, 5.05m, 11781343), halved);
    }

    static async Task<TemporaryStore> WithAYearStored()
    {
        var store = new TemporaryStore().Migrated();
        var clock = FixedClock.At(Backfilled, SessionZones.UnitedStates);

        await new MembershipLoader(
            RecordedIndexMembershipFeed.FromFile(Path.Combine(FixtureFolder(), "index-constituents.json")),
            clock,
            store.DatabaseFile).LoadAsync(Index, "run-0");

        await new Backfill(RecordedHistoricalBarFeed.FromFolder(FixtureFolder()), clock, store.DatabaseFile).RunAsync(Index, "run-1");

        return store;
    }

    // The fixture's day file with AAPL's row in LUMN's shape: its high, low and close all the close, under its open.
    static string WithAaplImpossible()
    {
        var file = System.Text.RegularExpressions.Regex.Replace(
            Captured("bulk-2026-09-08.json"),
            @"(""code"":\s*""AAPL""[^}]*?""high"":\s*)320\.7(,\s*""low"":\s*)314\.9",
            "${1}316.22${2}316.22");

        Assert.NotEqual(Captured("bulk-2026-09-08.json"), file);

        return file;
    }

    // Yahoo's chart and Nasdaq's quote for AAPL's session of the fixture, in the shapes the captures hold.
    static string AaplChart()
    {
        var open = new DateTimeOffset(2026, 9, 8, 13, 30, 0, TimeSpan.Zero).ToUnixTimeSeconds();

        return "{\"chart\":{\"result\":[{\"meta\":{\"gmtoffset\":-14400},\"timestamp\":[" + open.ToString(CultureInfo.InvariantCulture) +
            "],\"indicators\":{\"quote\":[{\"volume\":[35423000],\"low\":[314.8999938964844],\"high\":[320.70001220703125],\"close\":[316.2200012207031],\"open\":[317.1000061035156]}]}}],\"error\":null}}";
    }

    static string AaplQuote(string range) =>
        "{\"data\":{\"secondaryData\":{\"lastSalePrice\":\"$316.22\",\"lastTradeTimestamp\":\"Closed at Sep 8, 2026 4:00 PM ET\"},\"keyStats\":{\"dayrange\":{\"value\":\"" + range + "\"}}}}";

    static RecordedSubstituteBarFeed Sources(string range) => new(
        new Dictionary<string, string> { ["AAPL"] = AaplChart() },
        new Dictionary<string, string> { ["AAPL"] = Captured("substitute-nasdaq-history-LUMN.json") },
        new Dictionary<string, string> { ["AAPL"] = AaplQuote(range) });

    static (string Source, string Open, string High, string Low, string Close, string Raw, long Volume)? AaplBar(TemporaryStore store)
    {
        using var connection = new SqliteConnection($"Data Source={store.DatabaseFile}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT source, open, high, low, close, raw_close, volume FROM bar WHERE ticker = 'AAPL' AND session_date = '2026-09-08';";

        using var reader = command.ExecuteReader();

        return reader.Read()
            ? (reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.GetString(5), reader.GetInt64(6))
            : null;
    }

    [Fact]
    public async Task AMemberTheFileRefusesIsStoredFromTheSecondSourcesWhereEveryCheckHolds()
    {
        using var store = await WithAYearStored();
        var sources = Sources("314.90 - 320.70");

        var outcome = await new BarFetcher(new RecordedBulkPriceFeed(WithAaplImpossible()), FixedClock.At(Evening, SessionZones.UnitedStates), store.DatabaseFile, sources)
            .RunAsync(Index, "run-substitute");

        var refused = Assert.Single(outcome.Refused!);

        Assert.Equal(("AAPL", Night, true, "stored from Yahoo, checked against Nasdaq"), (refused.Ticker, refused.Session, refused.Stored, refused.Why));
        Assert.Contains("AAPL on 2026-09-08 adjusts to low 316.22, open 317.1, high 316.22, close 316.22", refused.Reason, StringComparison.Ordinal);

        // Yahoo once, Nasdaq's history, which holds no row for the session, and then its quote: three.
        Assert.Equal(3, outcome.SubstituteRequests);
        Assert.Equal(("substitute", "317.1", "320.7", "314.9", "316.22", "316.22", 35423000L), AaplBar(store));

        using var connection = new SqliteConnection($"Data Source={store.DatabaseFile}");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT network_requests, detail FROM run_log WHERE run_id = 'run-substitute' AND stage = 'fetch';";

        using var reader = command.ExecuteReader();

        Assert.True(reader.Read());
        Assert.Equal(4L, reader.GetInt64(0));
        Assert.Contains("\"substituteRequests\":3,\"refused\":[{\"ticker\":\"AAPL\",\"session\":\"2026-09-08\",\"stored\":true,\"why\":\"stored from Yahoo, checked against Nasdaq\"", reader.GetString(1), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AMemberWhoseSecondSourcesDisagreeIsLeftWithoutTheSessionAndNamed()
    {
        using var store = await WithAYearStored();

        var outcome = await new BarFetcher(new RecordedBulkPriceFeed(WithAaplImpossible()), FixedClock.At(Evening, SessionZones.UnitedStates), store.DatabaseFile, Sources("314.90 - 321.70"))
            .RunAsync(Index, "run-disagree");

        var refused = Assert.Single(outcome.Refused!);

        Assert.False(refused.Stored);
        Assert.Equal("Yahoo's high 320.7, low 314.9 and close 316.22 are not Nasdaq's 321.70, 314.90 and 316.22", refused.Why);
        Assert.Null(AaplBar(store));
        Assert.Equal(FixtureExpectation.CurrentMembers.Length - 1, outcome.MembersStored);
    }

    // Answers no bar for any ticker, or throws for one, and counts what it was asked.
    sealed class Counting(string? failsFor = null) : ISubstituteBarFeed
    {
        public List<string> Bars { get; } = [];

        public int Ranges { get; private set; }

        public int Requests => Bars.Count + Ranges;

        public Task<DayBar?> BarAsync(string ticker, DateOnly session, CancellationToken cancellation = default)
        {
            Bars.Add(ticker);

            return ticker == failsFor
                ? throw new ProviderRefusal("Yahoo's chart could not be reached: the connection was reset.", transient: true)
                : Task.FromResult<DayBar?>(null);
        }

        public Task<DayRange?> RangeAsync(string ticker, DateOnly session, CancellationToken cancellation = default)
        {
            Ranges++;

            return Task.FromResult<DayRange?>(null);
        }
    }

    [Fact]
    public async Task TheSecondSourcesAreAskedForTwentyMembersANightAndOneThatFailsStopsNoOther()
    {
        using var store = new TemporaryStore().Migrated();

        // Twenty-two members: one row that reads and twenty-one whose open stands above their high.
        var tickers = Enumerable.Range(1, 22).Select(at => FormattableString.Invariant($"M{at:00}")).ToArray();

        foreach (var ticker in tickers)
        {
            store.Execute($"INSERT INTO membership (index_code, ticker, joined, \"left\", observed_at) VALUES ('GSPC', '{ticker}', NULL, NULL, '2026-09-04T21:10:00Z');");
        }

        var file = "[" + string.Join(",", tickers.Select((ticker, at) => at == 0
            ? $$"""{"code":"{{ticker}}","exchange_short_name":"US","date":"2026-09-08","open":10,"high":11,"low":9,"close":10.5,"adjusted_close":10.5,"volume":1000}"""
            : $$"""{"code":"{{ticker}}","exchange_short_name":"US","date":"2026-09-08","open":10,"high":9.5,"low":9.5,"close":9.5,"adjusted_close":9.5,"volume":1000}""")) + "]";

        var sources = new Counting(failsFor: "M02");

        var outcome = await new BarFetcher(new RecordedBulkPriceFeed(file), FixedClock.At(Evening, SessionZones.UnitedStates), store.DatabaseFile, sources)
            .RunAsync(Index, "run-most");

        // In ticker order, the first twenty asked once each of Yahoo, and Nasdaq never, since Yahoo answered no bar.
        Assert.Equal(tickers[1..21], sources.Bars);
        Assert.Equal(0, sources.Ranges);
        Assert.Equal(20, outcome.SubstituteRequests);
        Assert.Equal(21, outcome.Refused!.Count);
        Assert.Equal("the second sources could not be read: Yahoo's chart could not be reached: the connection was reset.", outcome.Refused[0].Why);
        Assert.Equal("Yahoo holds no bar for 2026-09-08", outcome.Refused[1].Why);
        Assert.Equal(("M22", "past the 20 members a night asks the second sources for"), (outcome.Refused[20].Ticker, outcome.Refused[20].Why));
        Assert.All(outcome.Refused, one => Assert.False(one.Stored));
        Assert.Equal(1, outcome.MembersStored);
        Assert.Empty(outcome.Unaccounted);

        // A file every member's row reads asks the second sources nothing.
        using var whole = await WithAYearStored();
        var idle = new Counting();

        var ordinary = await new BarFetcher(RecordedBulkPriceFeed.FromFolder(FixtureFolder()), FixedClock.At(Evening, SessionZones.UnitedStates), whole.DatabaseFile, idle)
            .RunAsync(Index, "run-ordinary");

        Assert.Equal((0, 0), (idle.Requests, ordinary.SubstituteRequests));
        Assert.Empty(ordinary.Refused!);
    }
}
