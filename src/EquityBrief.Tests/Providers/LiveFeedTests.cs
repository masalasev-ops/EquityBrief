using System.Net;
using System.Net.Http;
using EquityBrief.Core.Providers;
using EquityBrief.Tests.Checks;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker;

namespace EquityBrief.Tests.Providers;

// The three remaining live feeds, and the weighted-call budget.
//
// Each is asked the same two questions the bulk feed was at 2.1: does it request
// the endpoint the capture was taken from, and does it read the answer through
// the same parser the double uses. The second is the one that matters. A second
// parser would be a second opinion about what the provider sends, and 1.2 is the
// record of what that costs: a membership parser read a field the provider does
// not send and its fixture agreed with it for two checkpoints.
public class LiveFeedTests
{
    const string Key = "demo-key-not-a-real-one";
    const string Base = "https://eodhd.example/api/";

    static readonly DateOnly Session = new(2026, 8, 10);

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

    static (HttpClient Client, Answering Handler) Client(Func<Uri, HttpResponseMessage> answer)
    {
        var handler = new Answering(answer);

        return (new HttpClient(handler) { BaseAddress = new Uri(Base) }, handler);
    }

    static ProviderRequest Patient() => new(RetryPolicy.Standard, (_, _) => Task.CompletedTask);

    static HttpResponseMessage Ok(string body) => new(HttpStatusCode.OK) { Content = new StringContent(body) };

    static string Folder() => Path.Combine(Repository.Root, "fixtures", "membership-2026-09-05");

    static string Captured(string file) => File.ReadAllText(Path.Combine(Folder(), file));

    static string Manifest() => Captured("manifest.json");

    [Fact]
    public async Task TheMembershipFeedAsksTheEndpointTheCaptureCameFromAndReadsItTheSameWay()
    {
        var (client, handler) = Client(_ => Ok(Captured("index-constituents.json")));
        var feed = new EodhdIndexMembershipFeed(client, new ProviderCredentials(Key), Patient());

        var live = await feed.ConstituentsAsync("GSPC");
        var recorded = await RecordedIndexMembershipFeed
            .FromFile(Path.Combine(Folder(), "index-constituents.json"))
            .ConstituentsAsync("GSPC");

        Assert.Equal(recorded, live);
        Assert.NotEmpty(live);
        Assert.Equal(1, feed.Requests);

        var asked = Assert.Single(handler.Asked);

        Assert.Equal("/api/fundamentals/GSPC.INDX", asked.AbsolutePath);
        Assert.Contains("fundamentals/GSPC.INDX", Manifest(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheHistoricalFeedAsksForTheWindowAndTheSuffixTheProviderWants()
    {
        var (client, handler) = Client(_ => Ok(Captured("bars-AAPL.json")));
        var feed = new EodhdHistoricalBarFeed(client, new ProviderCredentials(Key), Patient());

        var from = new DateOnly(2025, 9, 4);
        var to = new DateOnly(2026, 9, 4);

        var live = await feed.BarsAsync("AAPL", from, to);
        var recorded = await RecordedHistoricalBarFeed.FromFolder(Folder()).BarsAsync("AAPL", from, to);

        Assert.Equal(recorded, live);
        Assert.NotEmpty(live);

        var asked = Assert.Single(handler.Asked);

        // The exchange suffix goes on at the request and is never carried. Every
        // store in this system keys on the ticker alone, and the news parser
        // strips the same suffix off an attribution for the same reason.
        Assert.Equal("/api/eod/AAPL.US", asked.AbsolutePath);
        Assert.Contains("from=2025-09-04", asked.Query, StringComparison.Ordinal);
        Assert.Contains("to=2026-09-04", asked.Query, StringComparison.Ordinal);
        Assert.Contains("period=d", asked.Query, StringComparison.Ordinal);
        Assert.Contains("eod/AAPL.US?from=2025-09-04&to=2026-09-04&period=d", Manifest(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheActionFeedAsksOncePerKindOnTheRouteTheCaptureNames()
    {
        var (client, handler) = Client(asked =>
            Ok(Captured(asked.Query.Contains("type=splits", StringComparison.Ordinal)
                ? "actions-splits-2026-08-10.json"
                : "actions-dividends-2026-08-10.json")));

        var feed = new EodhdCorporateActionFeed(client, new ProviderCredentials(Key), Patient());

        var live = await feed.ActionsAsync("US", Session);
        var recorded = await RecordedCorporateActionFeed.FromFolder(Folder()).ActionsAsync("US", Session);

        Assert.Equal(recorded, live);
        Assert.NotEmpty(live);

        // Two requests, one per kind, and both counted. The night's claim is
        // that its cost does not grow with the universe, and two is a constant.
        Assert.Equal(2, feed.Requests);
        Assert.Equal(2, handler.Asked.Count);
        Assert.Contains(handler.Asked, one => one.Query.Contains("type=splits", StringComparison.Ordinal));
        Assert.Contains(handler.Asked, one => one.Query.Contains("type=dividends", StringComparison.Ordinal));
        Assert.All(handler.Asked, one => Assert.Contains("date=2026-08-10", one.Query, StringComparison.Ordinal));
        Assert.Contains("type=splits&date=2026-08-10", Manifest(), StringComparison.Ordinal);
        Assert.Contains("type=dividends&date=2026-08-10", Manifest(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task NoLiveFeedLetsTheKeyOrTheAddressIntoAFailure()
    {
        // The rule every one of them is shaped around, asserted over all three
        // rather than over the one that happened to be written first.
        var credentials = new ProviderCredentials(Key);
        var thrown = new HttpRequestException($"refused for {Base}anything?api_token={Key}&fmt=json");

        var membership = new EodhdIndexMembershipFeed(
            Client(_ => throw thrown).Client, credentials, Patient());
        var historical = new EodhdHistoricalBarFeed(
            Client(_ => throw thrown).Client, credentials, Patient());
        var actions = new EodhdCorporateActionFeed(
            Client(_ => throw thrown).Client, credentials, Patient());

        var failures = new List<Exception>
        {
            await Assert.ThrowsAsync<ProviderRefusal>(() => membership.ConstituentsAsync("GSPC")),
            await Assert.ThrowsAsync<ProviderRefusal>(
                () => historical.BarsAsync("AAPL", new DateOnly(2026, 1, 1), new DateOnly(2026, 2, 1))),
            await Assert.ThrowsAsync<ProviderRefusal>(() => actions.ActionsAsync("US", Session)),
        };

        Assert.All(failures, failure =>
        {
            foreach (var text in new[] { failure.Message, failure.ToString() })
            {
                Assert.DoesNotContain(Key, text, StringComparison.Ordinal);
                Assert.DoesNotContain("api_token", text, StringComparison.Ordinal);
            }

            Assert.Null(failure.InnerException);
        });
    }

    [Fact]
    public async Task ARejectedKeyIsNotRetriedByAnyOfThem()
    {
        // The counter-test on the retry, over every feed. A rejected key is
        // wrong three times, and turning one clear refusal into three and a
        // delay is the shape a retry policy gets wrong.
        var credentials = new ProviderCredentials(Key);

        var (mc, mh) = Client(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var (hc, hh) = Client(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));
        var (ac, ah) = Client(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));

        await Assert.ThrowsAsync<ProviderRefusal>(
            () => new EodhdIndexMembershipFeed(mc, credentials, Patient()).ConstituentsAsync("GSPC"));
        await Assert.ThrowsAsync<ProviderRefusal>(
            () => new EodhdHistoricalBarFeed(hc, credentials, Patient())
                .BarsAsync("AAPL", new DateOnly(2026, 1, 1), new DateOnly(2026, 2, 1)));
        await Assert.ThrowsAsync<ProviderRefusal>(
            () => new EodhdCorporateActionFeed(ac, credentials, Patient()).ActionsAsync("US", Session));

        Assert.All(new[] { mh, hh, ah }, handler => Assert.Single(handler.Asked));

        // And the counter-test on the counter-test: a rejected rate is retried
        // to the policy, so the single attempt above is about the status rather
        // than about the retry never running.
        var (rc, rh) = Client(_ => new HttpResponseMessage(HttpStatusCode.TooManyRequests));

        await Assert.ThrowsAsync<ProviderRefusal>(
            () => new EodhdIndexMembershipFeed(rc, credentials, Patient()).ConstituentsAsync("GSPC"));

        Assert.Equal(RetryPolicy.Standard.Attempts, rh.Asked.Count);
    }

    [Fact]
    public void EveryWeightAndTheAllowanceAreTheOnesTheRunbookStates()
    {
        // The figures have been in RUNBOOK since the architecture was written
        // and no code read either. Read back rather than repeated, because a
        // number stated in a document and again in code is two places holding
        // one fact.
        var runbook = Corpus.Read("docs/RUNBOOK.md");

        Assert.Contains("100,000 weighted calls", runbook, StringComparison.Ordinal);
        Assert.Contains($"entire exchange costs {ProviderWeights.BulkEndOfDay}", runbook, StringComparison.Ordinal);
        Assert.Contains(
            $"single-ticker historical request costs {ProviderWeights.HistoricalPerTicker}",
            runbook,
            StringComparison.Ordinal);
        Assert.Contains($"Fundamentals cost {ProviderWeights.Fundamentals} per ticker", runbook, StringComparison.Ordinal);
        Assert.Contains($"News costs {ProviderWeights.News}", runbook, StringComparison.Ordinal);
        Assert.Contains($"cost {ProviderWeights.Fundamentals}.", runbook, StringComparison.Ordinal);
        Assert.Equal(100_000, ProviderWeights.DailyAllowance);
    }

    [Fact]
    public async Task ANightsWeightedTotalIsCountedInTheUnitsTheProviderBillsIn()
    {
        // A night counted in requests alone says four where the provider says
        // two hundred and twelve, which is the whole reason the allowance could
        // not be read against anything before this.
        var feeds = NightFeeds.FromFixture(Folder());

        Assert.Equal(0, feeds.WeightedCalls);

        await feeds.Membership.ConstituentsAsync("GSPC");
        await feeds.Bulk.RowsAsync("US", new DateOnly(2026, 9, 8));
        await feeds.Corporate.ActionsAsync("US", Session);
        await feeds.Historical.BarsAsync("AAPL", new DateOnly(2025, 9, 4), new DateOnly(2026, 9, 4));

        // One membership at 10, one bulk at 100, two action requests at 100 each
        // and one ticker's history at 1. Five requests, 311 weighted calls.
        Assert.Equal(5, feeds.Requests);
        Assert.Equal(
            ProviderWeights.Fundamentals
            + ProviderWeights.BulkEndOfDay
            + (2 * ProviderWeights.BulkEndOfDay)
            + ProviderWeights.HistoricalPerTicker,
            feeds.WeightedCalls);
        Assert.Equal(311, feeds.WeightedCalls);
    }
}
