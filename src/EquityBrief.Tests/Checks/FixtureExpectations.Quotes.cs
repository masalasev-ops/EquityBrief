using System.Text.Json;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Quotes;
using EquityBrief.Core.Tiles;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Quotes;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 18.1: the regular session a name page asks for its delayed quote in, read off the closures
// table with the exchange's early closes; the four tiles under the headline, each worked by hand from stored figures;
// and the quote job, which asks once inside the session and under the day's cap and stores each band's distance at the
// price, worked over a constructed store.
// see: The name page draws a delayed quote in the regular session, asked by a worker job at most every five minutes under a day's cap
// see: Four tiles under the headline are worked by code from stored figures at the price the page draws
public partial class FixtureExpectations
{
    // The rows 18.1 adds that this check reaches: section 17's cap on the day's quotes and section 18's three failures.
    internal static string[] QuoteRows =>
    [
        CheckReach.Key(Scope.LimitsTable, "Live quotes a day"),
        CheckReach.Key(Scope.FailureTable, "A quote asked outside the regular session"),
        CheckReach.Key(Scope.FailureTable, "The day's quotes reach their cap"),
        CheckReach.Key(Scope.FailureTable, "The provider answers a quote with no price, or does not answer"),
    ];

    static readonly TimeZoneInfo NewYork = SessionZones.ResolveSessionZone(SessionZones.UnitedStates);

    [Fact]
    public void TheRegularSessionRunsFromNineThirtyToFourInNewYorkAndToOneOnAnEarlyClose()
    {
        // Wednesday 2026-10-07, in daylight time: 13:30Z to 20:00Z. Open at its first second and closed at its close.
        var october = RegularSession.On(new DateOnly(2026, 10, 7), NewYork)!.Value;

        Assert.Equal((new DateTimeOffset(2026, 10, 7, 13, 30, 0, TimeSpan.Zero), new DateTimeOffset(2026, 10, 7, 20, 0, 0, TimeSpan.Zero)), (october.Open, october.Close));
        Assert.False(RegularSession.IsOpen(new DateTimeOffset(2026, 10, 7, 13, 29, 59, TimeSpan.Zero), NewYork));
        Assert.True(RegularSession.IsOpen(new DateTimeOffset(2026, 10, 7, 13, 30, 0, TimeSpan.Zero), NewYork));
        Assert.True(RegularSession.IsOpen(new DateTimeOffset(2026, 10, 7, 19, 59, 59, TimeSpan.Zero), NewYork));
        Assert.False(RegularSession.IsOpen(new DateTimeOffset(2026, 10, 7, 20, 0, 0, TimeSpan.Zero), NewYork));

        // Wednesday 2026-11-18, in standard time: 14:30Z to 21:00Z.
        Assert.Equal(
            (new DateTimeOffset(2026, 11, 18, 14, 30, 0, TimeSpan.Zero), new DateTimeOffset(2026, 11, 18, 21, 0, 0, TimeSpan.Zero)),
            RegularSession.On(new DateOnly(2026, 11, 18), NewYork)!.Value);

        // The day after Thanksgiving closes at 13:00 in New York, 18:00Z; Thanksgiving itself, a Saturday and a day past
        // the closures table hold no session.
        Assert.Equal(new DateTimeOffset(2026, 11, 27, 18, 0, 0, TimeSpan.Zero), RegularSession.On(new DateOnly(2026, 11, 27), NewYork)!.Value.Close);
        Assert.False(RegularSession.IsOpen(new DateTimeOffset(2026, 11, 27, 18, 0, 0, TimeSpan.Zero), NewYork));
        Assert.True(RegularSession.IsOpen(new DateTimeOffset(2026, 11, 27, 17, 59, 59, TimeSpan.Zero), NewYork));
        Assert.Null(RegularSession.On(new DateOnly(2026, 11, 26), NewYork));
        Assert.Null(RegularSession.On(new DateOnly(2026, 10, 10), NewYork));
        Assert.Null(RegularSession.On(new DateOnly(2028, 1, 3), NewYork));

        // The early closes are the three the exchange's page listed through the table's end.
        Assert.Equal([new DateOnly(2026, 11, 27), new DateOnly(2026, 12, 24), new DateOnly(2027, 11, 26)], RegularSession.EarlyCloses.Order());
    }

    [Fact]
    public void TheFourTilesAreWorkedByHandFromStoredFigures()
    {
        // Eight quarters of one fetch, the newest to 2026-06-30, each three months apart.
        TileQuarter[] quarters =
        [
            new(new DateOnly(2026, 6, 30), 120m, 2.20m, 2.00m),
            new(new DateOnly(2026, 3, 31), 110m, 2.00m, 2.05m),
            new(new DateOnly(2025, 12, 31), 105m, 1.90m, null),
            new(new DateOnly(2025, 9, 30), 100m, 1.80m, 1.80m),
            new(new DateOnly(2025, 6, 30), 100m, 2.00m, 1.90m),
            new(new DateOnly(2025, 3, 31), 95m, 1.70m, 1.60m),
            new(new DateOnly(2024, 12, 31), 90m, 1.60m, 1.50m),
            new(new DateOnly(2024, 9, 30), 85m, 1.50m, 1.40m),
        ];

        // The newest quarter's 2.20 against its estimate of 2.00 is 10% above it, and against the 2.00 a year before 10% up.
        var earnings = NameTiles.Earnings(quarters)!;

        Assert.Equal((new DateOnly(2026, 6, 30), 2.20m, 2.00m, 2.00m), (earnings.Quarter, earnings.Actual, earnings.Estimate, earnings.YearBefore));
        Assert.Equal(10.0, earnings.AgainstEstimate!.Value, 9);
        Assert.Equal(10.0, earnings.OnTheYear!.Value, 9);

        // The four newest quarters' sales, 120 + 110 + 105 + 100 = 435, against the four before, 100 + 95 + 90 + 85 =
        // 370: 435 / 370 - 1 = 0.175676 to six places, 17.5676%.
        var growth = NameTiles.SalesGrowth(quarters)!;

        Assert.Equal((GrowthTile.Sales, new DateOnly(2026, 6, 30)), (growth.Basis, growth.Through));
        Assert.Equal(17.5676, growth.Percent, 4);

        // Seven quarters read no growth, and a quarter a year before at a loss reads no change on the year.
        Assert.Null(NameTiles.SalesGrowth(quarters[..7]));
        Assert.Null(NameTiles.Earnings([quarters[0], quarters[4] with { EpsActual = -0.10m }])!.OnTheYear);

        // A forward rate of 7.12 at a price of 211.55 yields 3.3656%; a company paying nothing has no yield.
        Assert.Equal(3.3656, NameTiles.Yield(7.12m, 211.55m)!.Percent, 4);
        Assert.Null(NameTiles.Yield(0m, 211.55m));
        Assert.Null(NameTiles.Yield(null, 211.55m));

        // 211.55 against the year's high of 217.78 is 2.8607% under it, and at 0.9081 of the way from the low of 150.00.
        var high = NameTiles.High(211.55m, 217.78m, new DateOnly(2026, 9, 15), 150.00m, new DateOnly(2026, 1, 5))!;

        Assert.Equal(-2.8607, high.FromHigh, 4);
        Assert.Equal(0.9081, high.Position, 4);
        Assert.Null(NameTiles.High(100m, 100m, new DateOnly(2026, 9, 15), 100m, new DateOnly(2026, 9, 15)));
    }

    [Fact]
    public async Task TheQuoteJobAsksOnceInsideTheSessionUnderTheCapAndStoresEachBandsDistanceAtThePrice()
    {
        using var store = new TemporaryStore().Migrated();

        // Two bands of the night of 2026-10-06 and a typical move of 2.00: a resistance from 215 to 218 and a support from
        // 200 to 205.
        store.Execute(
            "INSERT INTO level (ticker, as_of, low_edge, high_edge, role, immediate, strength, has_non_average_anchor, members) VALUES " +
            "('CVX', '2026-10-06', '215', '218', 'resistance', 1, 3, 1, '[]'), ('CVX', '2026-10-06', '200', '205', 'support', 1, 4, 1, '[]');");
        store.Execute($"INSERT INTO indicator (ticker, session_date, name, value, bar_count) VALUES ('CVX', '2026-10-06', '{EquityBrief.Core.Indicators.IndicatorSeries.Atr14}', 2.0, 250);");

        var quoted = new ProviderQuote(new DateTimeOffset(2026, 10, 7, 14, 43, 0, TimeSpan.Zero), 211.40m, 210.10m, 1.30m, 0.6187);
        var inSession = new DateTimeOffset(2026, 10, 7, 14, 58, 5, TimeSpan.Zero);
        var asked = new DateTimeOffset(2026, 10, 7, 14, 58, 0, TimeSpan.Zero);

        // Inside the session it asks once and stores the quote: 211.40 is 3.60 under the resistance's low edge, 1.8
        // typical moves, and 6.40 above the support's high edge, 3.2.
        var feed = new ConstructedQuoteFeed(quoted);
        var outcome = await new QuoteJob(new FixedClock(inSession, NewYork), store.DatabaseFile, feed).RunAsync("CVX", asked);

        Assert.Equal((QuoteJob.Quoted, 1), (outcome.Outcome, feed.Requests));

        var row = Query(store, "SELECT asked_at, quoted_at, price, previous_close, change, change_pct, night, distances FROM live_quote WHERE ticker = 'CVX';").Single().Split('|');

        Assert.Equal(["2026-10-07T14:58:00Z", "2026-10-07T14:43:00Z", "211.40", "210.10", "1.30", "0.6187", "2026-10-06"], row[..7]);

        using (var distances = JsonDocument.Parse(row[7]))
        {
            Assert.Equal(
                ["200 205 support 3.2", "215 218 resistance 1.8"],
                distances.RootElement.EnumerateArray()
                    .Select(band => $"{band.GetProperty("low").GetString()} {band.GetProperty("high").GetString()} {band.GetProperty("role").GetString()} {band.GetProperty("days").GetDouble().ToString("0.0", System.Globalization.CultureInfo.InvariantCulture)}")
                    .Order(StringComparer.Ordinal));
        }

        Assert.Equal(
            ["quoted|1|1"],
            Query(store, $"SELECT outcome, network_requests, json_extract(detail, '$.weighted') FROM run_log WHERE stage = '{QuoteJob.Stage}';"));

        // Outside the session, after its close, it asks nothing and stores nothing.
        var afterTheClose = new ConstructedQuoteFeed(quoted);
        var closed = await new QuoteJob(new FixedClock(new DateTimeOffset(2026, 10, 7, 20, 0, 0, TimeSpan.Zero), NewYork), store.DatabaseFile, afterTheClose).RunAsync("CVX", new DateTimeOffset(2026, 10, 7, 20, 0, 0, TimeSpan.Zero));

        Assert.Equal((QuoteJob.SessionClosed, 0), (closed.Outcome, afterTheClose.Requests));

        // At the cap, 500 quotes already asked in the session, the next asks nothing; at 499 it asks.
        for (var at = 0; at < 498; at++)
        {
            store.Execute($"INSERT INTO live_quote (ticker, asked_at, answered_at, quoted_at, price, previous_close, change, change_pct, night, distances) VALUES ('Q{at}', '2026-10-07T15:00:00Z', '2026-10-07T15:00:01Z', '2026-10-07T14:45:00Z', '10', NULL, NULL, NULL, NULL, '[]');");
        }

        var underTheCap = new ConstructedQuoteFeed(quoted);

        Assert.Equal(QuoteJob.Quoted, (await new QuoteJob(new FixedClock(inSession.AddMinutes(10), NewYork), store.DatabaseFile, underTheCap).RunAsync("CVX", asked.AddMinutes(10))).Outcome);

        var atTheCap = new ConstructedQuoteFeed(quoted);

        Assert.Equal((QuoteJob.AtTheCap, 0), ((await new QuoteJob(new FixedClock(inSession.AddMinutes(20), NewYork), store.DatabaseFile, atTheCap).RunAsync("CVX", asked.AddMinutes(20))).Outcome, atTheCap.Requests));
        Assert.Equal(QuoteLimits.DailyCap, int.Parse(Query(store, "SELECT COUNT(*) FROM live_quote;").Single(), System.Globalization.CultureInfo.InvariantCulture));

        // A provider holding no price stores nothing, and its run says so with the request it made.
        using var other = new TemporaryStore().Migrated();
        var none = new ConstructedQuoteFeed(null);

        Assert.Equal((QuoteJob.NoPrice, 1), ((await new QuoteJob(new FixedClock(inSession, NewYork), other.DatabaseFile, none).RunAsync("CVX", asked)).Outcome, none.Requests));
        Assert.Empty(Query(other, "SELECT ticker FROM live_quote;"));

        // A provider that does not answer stores nothing either, and its run says the request failed and why.
        var unanswered = new UnansweredQuoteFeed();
        var failed = await new QuoteJob(new FixedClock(inSession.AddMinutes(10), NewYork), other.DatabaseFile, unanswered).RunAsync("CVX", asked.AddMinutes(10));

        Assert.Equal((QuoteJob.Failed, 1), (failed.Outcome, unanswered.Requests));
        Assert.Contains("the provider did not answer", failed.Detail, StringComparison.Ordinal);
        Assert.Empty(Query(other, "SELECT ticker FROM live_quote;"));
        Assert.Equal(
            [QuoteJob.NoPrice, QuoteJob.Failed],
            Query(other, $"SELECT outcome FROM run_log WHERE stage = '{QuoteJob.Stage}' ORDER BY started_at, run_id;"));
    }

    // A quote feed whose provider does not answer, throwing what the live feed throws then, counting its requests.
    sealed class UnansweredQuoteFeed : IQuoteFeed
    {
        public int Requests { get; private set; }

        public Task<ProviderQuote?> QuoteAsync(string ticker, CancellationToken cancellation = default)
        {
            Requests++;

            throw EodhdQuery.Unreachable("quote feed", new HttpRequestException("the provider did not answer"), new ProviderCredentials("constructed-key"));
        }
    }

    [Fact]
    public void TheProvidersQuoteIsReadAsItSendsItAndAPriceItDoesNotHoldIsNone()
    {
        // The answer's timestamp is the quote's own instant in seconds, 1791384180 being 2026-10-07T14:43:00Z, and its
        // figures are read from their own digits.
        var quote = ProviderQuotes.Parse("{\"code\":\"CVX.US\",\"timestamp\":1791384180,\"gmtoffset\":0,\"open\":210.5,\"high\":212.1,\"low\":209.8,\"close\":211.4,\"volume\":5123456,\"previousClose\":210.1,\"change\":1.3,\"change_p\":0.6187}")!;

        Assert.Equal((new DateTimeOffset(2026, 10, 7, 14, 43, 0, TimeSpan.Zero), 211.4m, 210.1m, 1.3m), (quote.QuotedAt, quote.Price, quote.PreviousClose, quote.Change));
        Assert.Equal(0.6187, quote.ChangePercent!.Value, 9);

        // A listing the provider holds no price for answers "NA", which is no quote; an answer that is not an object, or
        // a price with no timestamp, is refused.
        Assert.Null(ProviderQuotes.Parse("{\"code\":\"XX.US\",\"timestamp\":\"NA\",\"close\":\"NA\"}"));
        Assert.Throws<FormatException>(() => ProviderQuotes.Parse("[]"));
        Assert.Throws<FormatException>(() => ProviderQuotes.Parse("{\"close\":211.4}"));
        Assert.Throws<FormatException>(() => ProviderQuotes.Parse("not json"));
    }

    // A quote feed answering what the test hands it, counting its requests.
    sealed class ConstructedQuoteFeed(ProviderQuote? quote) : IQuoteFeed
    {
        public int Requests { get; private set; }

        public Task<ProviderQuote?> QuoteAsync(string ticker, CancellationToken cancellation = default)
        {
            Requests++;

            return Task.FromResult(quote);
        }
    }
}
