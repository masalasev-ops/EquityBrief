using System.Net;
using System.Net.Http;
using EquityBrief.Core.Providers;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker;

namespace EquityBrief.Tests.Providers;

// The first feed in this tree that reaches a provider.
//
// The transport is exercised through a handler rather than a socket. What this
// file is about is the shape of the request, the count, the parse and what a
// failure is allowed to say, and every one of those is a property of this class.
// Whether a socket opens is a property of the framework, and a test that bound a
// port would trade a real assertion for a flaky one on three runners.
//
// The socket is proved once by hand instead, against the provider, and the
// figures that run produced are recorded in `PROGRESS.md`. That is the same
// split the captured fixtures already use: the suite asserts the reading and
// the operator's own run asserts that there is something to read.
public class EodhdBulkPriceFeedTests
{
    const string Key = "demo-key-not-a-real-one";
    const string Base = "https://eodhd.example/api/";

    // The session the captured bulk file is for, and the one every request
    // below asks for. Named once, because a request that asks for a session
    // and a payload that carries another is the failure 2.3 induces.
    static readonly DateOnly Session = new(2026, 9, 8);

    // A handler that answers from a script and remembers what it was asked.
    sealed class Answering(Func<Uri, HttpResponseMessage> answer) : HttpMessageHandler
    {
        internal List<Uri> Asked { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Asked.Add(request.RequestUri!);

            return Task.FromResult(answer(request.RequestUri!));
        }
    }

    // The retry runs to the real policy and the waits are not taken. A test that
    // spent six seconds proving the backoff is one that gets a Skip attribute
    // the first time it is inconvenient, and a skipped test is a check that
    // stopped running.
    static ProviderRequest Patient() => new(RetryPolicy.Standard, (_, _) => Task.CompletedTask);

    static (EodhdBulkPriceFeed Feed, Answering Handler) Feed(Func<Uri, HttpResponseMessage> answer)
    {
        var handler = new Answering(answer);
        var client = new HttpClient(handler) { BaseAddress = new Uri(Base) };

        return (new EodhdBulkPriceFeed(client, new ProviderCredentials(Key), Patient()), handler);
    }

    static HttpResponseMessage Ok(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body) };

    static string Captured() =>
        File.ReadAllText(Directory
            .GetFiles(
                Path.Combine(Repository.Root, "fixtures", "membership-2026-09-05"),
                RecordedBulkPriceFeed.FilePrefix + "*.json")
            .Single());

    [Fact]
    public async Task OneRequestNamesTheEndpointTheCapturedResponseWasTakenFrom()
    {
        var (feed, handler) = Feed(_ => Ok(Captured()));

        await feed.RowsAsync("US", Session);

        var asked = Assert.Single(handler.Asked);

        // The path the fixture's own manifest records for this file, so the
        // double and the live feed are known to be reading the same endpoint
        // rather than assumed to be.
        Assert.Equal("/api/eod-bulk-last-day/US", asked.AbsolutePath);
        Assert.Contains("fmt=json", asked.Query, StringComparison.Ordinal);
        Assert.Contains($"api_token={Key}", asked.Query, StringComparison.Ordinal);

        var manifest = File.ReadAllText(Path.Combine(
            Repository.Root, "fixtures", "membership-2026-09-05", "manifest.json"));

        Assert.Contains($"{EodhdBulkPriceFeed.Endpoint}/US", manifest, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheLiveFeedAndTheDoubleReadOneResponseTheSameWay()
    {
        // The parser is shared rather than written twice, and this is the
        // assertion that says so in behaviour rather than in a comment. A second
        // parser would be a second opinion about what the provider sends, and
        // the fixture would only ever exercise one of them.
        var (feed, _) = Feed(_ => Ok(Captured()));

        var live = await feed.RowsAsync("US", Session);
        var recorded = await new RecordedBulkPriceFeed(Captured()).RowsAsync("US", Session);

        Assert.NotEmpty(live);
        Assert.Equal(recorded, live);
    }

    [Fact]
    public async Task ARequestIsCountedWhetherOrNotItAnswers()
    {
        // Counted before the call. A count incremented on success reports a
        // night that tried and failed as a night that made no request, and the
        // run log's figure is what the cost limit is read off.
        var (refused, _) = Feed(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests));

        await Assert.ThrowsAsync<ProviderRefusal>(() => refused.RowsAsync("US", Session));

        Assert.Equal(1, refused.Requests);

        var (answered, _) = Feed(_ => Ok(Captured()));

        await answered.RowsAsync("US", Session);

        Assert.Equal(1, answered.Requests);
    }

    [Fact]
    public async Task ARefusalIsAFailureRatherThanAnExchangeThatDidNotTrade()
    {
        foreach (var status in new[] { HttpStatusCode.Unauthorized, HttpStatusCode.TooManyRequests, HttpStatusCode.InternalServerError })
        {
            var (feed, _) = Feed(_ => new HttpResponseMessage(status));

            var failure = await Assert.ThrowsAsync<ProviderRefusal>(() => feed.RowsAsync("US", Session));

            // The status is named, because "the feed did not answer" and "the
            // feed refused the key" are different mornings for the operator.
            Assert.Contains(((int)status).ToString(), failure.Message, StringComparison.Ordinal);

            // And whether it was worth asking again, which is what decides how
            // many attempts one request costs.
            Assert.Equal(RetryPolicy.Transient((int)status), failure.Transient);
            Assert.Equal(failure.Transient ? RetryPolicy.Standard.Attempts : 1, feed.Attempts);
        }
    }

    [Fact]
    public async Task NoFailureCarriesTheKeyOrTheRequestUrl()
    {
        // The rule this feed is shaped around. The key travels in the query
        // string on this provider, so it is one substring away from every
        // message a failed request produces, and the run log is a store this
        // repository copies between machines.
        //
        // The transport failure is given a message carrying the key, which is
        // the case a redaction that only handled its own strings would miss.
        var (thrown, _) = Feed(_ => throw new HttpRequestException(
            $"Connection refused for {Base}eod-bulk-last-day/US?api_token={Key}&fmt=json"));

        var failure = await Assert.ThrowsAsync<ProviderRefusal>(() => thrown.RowsAsync("US", Session));

        foreach (var text in new[] { failure.Message, failure.ToString() })
        {
            Assert.DoesNotContain(Key, text, StringComparison.Ordinal);
            Assert.DoesNotContain("api_token", text, StringComparison.Ordinal);
        }

        // Redacted rather than dropped: the transport still says what went
        // wrong, with the address taken out of it. The address goes whole rather
        // than the key alone, because a message quoting a URL this process did
        // not build would carry a live key past a scrub that only looked for the
        // value this object happens to hold.
        Assert.Contains("Connection refused", failure.Message, StringComparison.Ordinal);
        Assert.Contains(ProviderCredentials.AddressWithheld, failure.Message, StringComparison.Ordinal);

        // And the inner exception is not attached, so a logger expanding the
        // chain cannot reach a message this code never scrubbed.
        Assert.Null(failure.InnerException);

        // A transport that did not answer is worth asking again, which is why
        // this one cost three attempts and still one request.
        Assert.True(failure.Transient);
        Assert.Equal(RetryPolicy.Standard.Attempts, thrown.Attempts);
        Assert.Equal(1, thrown.Requests);
    }

    [Fact]
    public async Task APayloadThatCannotBeParsedFailsRatherThanAnsweringWithNoBars()
    {
        var (feed, _) = Feed(_ => Ok("{\"message\":\"not an array\"}"));

        await Assert.ThrowsAsync<FormatException>(() => feed.RowsAsync("US", Session));
    }

    // One row that traded, one listed symbol that did not, and one row from
    // another exchange. The middle one is what the first live night found and
    // the captured fixture could not have: all seven of its hand-picked rows
    // traded, and 62 of the 44,362 rows the exchange actually sent did not.
    const string Mixed = """
        [
          {"code":"AAPL","exchange_short_name":"US","date":"2026-09-08","open":317.1,"high":320.7,"low":314.9,"close":316.22,"adjusted_close":316.22,"volume":35423000},
          {"code":"DEWM","exchange_short_name":"US","date":"2026-09-08","open":0,"high":0,"low":0,"close":0,"adjusted_close":0,"volume":0},
          {"code":"VOD","exchange_short_name":"LSE","date":"2026-09-08","open":1,"high":2,"low":1,"close":2,"adjusted_close":2,"volume":10}
        ]
        """;

    [Fact]
    public async Task ASymbolThatDidNotTradeIsSkippedAndNamedRatherThanStoppingTheNight()
    {
        var (feed, _) = Feed(_ => Ok(Mixed));

        var rows = await feed.RowsAsync("US", Session);

        // The night survives a penny stock. Before this, one row with no
        // positive close threw out of the parser and the fetch step failed for
        // five hundred names, which is what the first live run did.
        var only = Assert.Single(rows);

        Assert.Equal("AAPL", only.Ticker);

        // Named rather than counted away. The same skip at scale is a night that
        // stores almost nothing and reports success, and a bare count would not
        // say which names went missing.
        Assert.Equal(["DEWM"], feed.NotSessions);
    }

    [Fact]
    public async Task ARowFromAnotherExchangeIsNotCountedAsASymbolThatDidNotTrade()
    {
        // The two skips are different and the counter must not conflate them.
        // A row from another exchange is not this night's at all; a row that did
        // not trade is this night's and is a fact worth reading.
        var (feed, _) = Feed(_ => Ok(Mixed));

        await feed.RowsAsync("US", Session);

        Assert.DoesNotContain("VOD", feed.NotSessions);
    }

    [Fact]
    public async Task AnExchangeWhereNothingTradedIsRefusedRatherThanStored()
    {
        // Skipping rows reopens the failure the parser has refused since 1.4 if
        // it is allowed to skip all of them: answering with no bars is stored as
        // an exchange that did not trade, which reads as a night that ran.
        var (feed, _) = Feed(_ => Ok("""
            [{"code":"DEWM","exchange_short_name":"US","date":"2026-09-08","open":0,"high":0,"low":0,"close":0,"adjusted_close":0,"volume":0}]
            """));

        var failure = await Assert.ThrowsAsync<FormatException>(() => feed.RowsAsync("US", Session));

        Assert.Contains("did not trade", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ARowMissingAFieldStillFails()
    {
        // The counter-test on the skip. Only the not-a-session case is forgiven;
        // a payload that cannot be read is still a payload that cannot be read,
        // and a skip that swallowed a malformed row would be the parser
        // reporting fewer bars instead of a fault.
        var (feed, _) = Feed(_ => Ok("""
            [{"code":"AAPL","exchange_short_name":"US","open":1,"high":2,"low":1,"close":2,"adjusted_close":2,"volume":10}]
            """));

        await Assert.ThrowsAsync<FormatException>(() => feed.RowsAsync("US", Session));
    }

    [Fact]
    public void ABaseAddressWithoutATrailingSlashStillReachesTheEndpoint()
    {
        // Uri resolution against a base with no trailing slash drops the last
        // segment, so "https://eodhd.com/api" would send the request to
        // "https://eodhd.com/eod-bulk-last-day/US" and the provider would answer
        // 404 rather than anything naming the cause.
        //
        // Asserted on the factory rather than on a request, because the factory
        // is where a base address from configuration arrives.
        Assert.NotNull(EodhdBulkPriceFeed.Live("https://eodhd.example/api", new ProviderCredentials(Key)));
        Assert.NotNull(EodhdBulkPriceFeed.Live("https://eodhd.example/api/", new ProviderCredentials(Key)));

        // The default is already well formed, which is what the two above are
        // guarding rather than replacing.
        Assert.EndsWith("/", EodhdBulkPriceFeed.DefaultBaseAddress, StringComparison.Ordinal);
    }

    [Fact]
    public void TheLiveBulkFeedIsRefusedByNameWhenTheKeyIsBlank()
    {
        // The startup refusal RUNBOOK promises, at the point a live feed is
        // asked for. A blank base address falls back and a blank key does not.
        foreach (var blank in new[] { null, "", " " })
        {
            var refusal = Assert.Throws<InvalidOperationException>(() => NightFeeds.LiveBulk(null, blank));

            Assert.Contains(ProviderCredentials.ApiKeyName, refusal.Message, StringComparison.Ordinal);
        }

        Assert.NotNull(NightFeeds.LiveBulk(null, Key));
        Assert.NotNull(NightFeeds.LiveBulk(" ", Key));
    }

    [Fact]
    public void TheOnlyShippedFilesHoldingAClientAreTheLiveFeeds()
    {
        // The exemption, read against what it now carries rather than against an
        // empty list. This is the assertion the carve-out commit could only make
        // on constructed sources, running for the first time against the tree.
        Assert.Contains(
            "src/EquityBrief.Core/Providers/EodhdBulkPriceFeed.cs",
            Checks.NightlyCost.MayHoldAClient);

        Assert.Single(Checks.NightlyCost.MayHoldAClient);
    }
}
