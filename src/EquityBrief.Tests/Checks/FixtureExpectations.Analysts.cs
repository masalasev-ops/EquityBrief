using System.Net;
using System.Text.Json;
using EquityBrief.Core.Analysts;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Analysts;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 20.13: Yahoo's and TipRanks' captured answers for HPQ read as they were sent, each figure worked
// by hand off the capture; the TipRanks feed sending the key in its header alone and taking it out of a failure's words;
// the analyst job asking Yahoo once under the New York day's cap; and the ratings job asking TipRanks' count first and
// never past the month's limit, each worked over a constructed store.
// see: The chart page graphs the report's earnings and revenue beneath its chart with the analysts' figures Yahoo answers when it opens, and no rule reads them from it
// see: TipRanks' analyst ratings are asked only on a press and never past the month's limit, a stock again only 15 days after its last answer
public partial class FixtureExpectations
{
    // The rows 20.13 adds that this check reaches: section 17's two limits on the asks and the calls and section 18's four
    // failures.
    internal static string[] AnalystRows =>
    [
        CheckReach.Key(Scope.LimitsTable, "Analyst asks a day"),
        CheckReach.Key(Scope.LimitsTable, "TipRanks calls a month"),
        CheckReach.Key(Scope.LimitsTable, "TipRanks press wait"),
        CheckReach.Key(Scope.FailureTable, "Yahoo answers a stock's analysts with none, or does not answer"),
        CheckReach.Key(Scope.FailureTable, "The day's asks of Yahoo reach their cap"),
        CheckReach.Key(Scope.FailureTable, "The month's TipRanks calls are spent"),
        CheckReach.Key(Scope.FailureTable, "TipRanks does not answer, or answers with an error"),
    ];

    static string AnalystCapture(string file) => File.ReadAllText(Path.Combine(Repository.Root, "fixtures", Fixture, file));

    // A key no account holds, planted where a failure's words could carry it.
    const string PlantedKey = "constructed-tipranks-key-7f3a";

    // Friday 2026-10-23 at 11:00 in New York; its New York day runs from 04:00Z to 04:00Z the next morning.
    static readonly DateTimeOffset AnalystsAsked = new(2026, 10, 23, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public void YahoosCapturedAnswerForHpqIsReadAsItWasSent()
    {
        var answer = YahooAnalysts.Parse(AnalystCapture("analysts-yahoo-HPQ.json"))!;

        // The four targets as Yahoo wrote them, read from their own digits, over the 17 analysts setting them.
        Assert.Equal(("USD", 19.0m, 27.51765m, 29.0m, 33.8m, 17, "hold"), (answer.Currency, answer.TargetLow, answer.TargetMean, answer.TargetMedian, answer.TargetHigh, answer.Analysts, answer.Recommendation));

        // The four months under Yahoo's own labels, the month asked first.
        Assert.Equal(
            ["0m 0 2 10 4 2", "-1m 0 2 10 3 3", "-2m 0 2 10 3 2", "-3m 0 2 10 3 2"],
            answer.Months.Select(month => $"{month.Period} {month.StrongBuy} {month.Buy} {month.Hold} {month.Sell} {month.StrongSell}"));

        // The twelve actions newest first: RBC's initiation with no grade before it and its prior target of nought read as
        // none, Morgan Stanley's cut from 19 to 17 the sixth, and JP Morgan's raise the last.
        Assert.Equal(12, answer.Actions.Count);
        Assert.Equal(
            new YahooAction(new DateTimeOffset(2026, 9, 11, 13, 39, 28, TimeSpan.Zero), "RBC Capital", "init", null, "Sector Perform", null, 33.0m, "Announces"),
            answer.Actions[0]);
        Assert.Equal(
            new YahooAction(new DateTimeOffset(2026, 8, 24, 15, 39, 0, TimeSpan.Zero), "Morgan Stanley", "main", "Underweight", "Underweight", 19.0m, 17.0m, "Lowers"),
            answer.Actions[5]);
        Assert.Equal(
            new YahooAction(new DateTimeOffset(2026, 5, 28, 19, 13, 17, TimeSpan.Zero), "JP Morgan", "main", "Neutral", "Neutral", 22.0m, 26.0m, "Raises"),
            answer.Actions[11]);
        Assert.True(answer.Actions.Zip(answer.Actions.Skip(1)).All(pair => pair.First.GradedAt >= pair.Second.GradedAt));

        // A symbol Yahoo does not know, an empty result and a result holding no target, month or action read as none.
        Assert.Null(YahooAnalysts.Parse("{\"quoteSummary\":{\"result\":null,\"error\":{\"code\":\"Not Found\",\"description\":\"Quote not found for symbol: ZZZZ\"}}}"));
        Assert.Null(YahooAnalysts.Parse("{\"quoteSummary\":{\"result\":[],\"error\":null}}"));
        Assert.Null(YahooAnalysts.Parse("{\"quoteSummary\":{\"result\":[{\"price\":{\"currency\":\"USD\"}}],\"error\":null}}"));

        // An answer that is no object, a result that is no object, and text that is no JSON are refused.
        Assert.Throws<FormatException>(() => YahooAnalysts.Parse("[]"));
        Assert.Throws<FormatException>(() => YahooAnalysts.Parse("{\"quoteSummary\":{\"result\":[5],\"error\":null}}"));
        Assert.Throws<FormatException>(() => YahooAnalysts.Parse("Too Many Requests"));
    }

    [Fact]
    public void TipRanksCapturedAnswersForHpqAreReadAsTheyWereSent()
    {
        var answer = TipRanksAnswers.Ratings(AnalystCapture("analysts-tipranks-HPQ.json"))!;

        Assert.Equal((new DateOnly(2026, 10, 23), 16), (answer.AsOf, answer.Total));
        Assert.Equal(
            ["last30Days 2 0 2 0", "last90Days 13 1 7 5", "last12Months 16 1 8 7"],
            answer.Counts.Select(span => $"{span.Span} {span.Analysts} {span.Buy} {span.Hold} {span.Sell}"));

        // Sixteen ratings newest first, RBC's of 2026-10-15 at their head with the analyst's record as TipRanks scores it,
        // the success rate a fraction and the return over the market in per cent; Raymond James' by an analyst TipRanks
        // does not name carries no record; Mizuho's of 2025-11-26 the last.
        Assert.Equal(16, answer.Ratings.Count);
        Assert.Equal(
            new TipRanksRating("David Paige", "RBC Capital", "Hold", "maintained", new DateOnly(2026, 10, 15), 33.0m, "USD", 4.4, 0.657, 27.6, 1410, 35, "Analysts Conflicted on These Technology Names: Semtech (NASDAQ: SMTC) and HP (NYSE: HPQ)"),
            answer.Ratings[0]);

        var unnamed = answer.Ratings.Single(rating => rating.Firm == "Raymond James");

        Assert.Equal(("Unknown Analyst", "downgraded", new DateOnly(2026, 1, 13), 21.0m), (unnamed.Analyst, unnamed.Action, unnamed.RatedOn, unnamed.Target));
        Assert.Equal((null, null, null, null, 0), (unnamed.Stars, unnamed.SuccessRate, unnamed.AverageReturn, unnamed.Rank, unnamed.Ratings));
        Assert.Equal(("Mizuho Securities", new DateOnly(2025, 11, 26)), (answer.Ratings[^1].Firm, answer.Ratings[^1].RatedOn));

        // The count of the month's calls, and the same answer as the last event of a stream.
        var usage = TipRanksAnswers.Usage(AnalystCapture("analysts-tipranks-usage.json"));

        Assert.Equal(new TipRanksUsage(3, 100, new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero)), usage);
        Assert.Equal(usage, TipRanksAnswers.Usage("event: message\r\ndata: " + AnalystCapture("analysts-tipranks-usage.json").Trim() + "\r\n\r\n"));

        // A tool's error and the server's error are refused in their own words; ratings counting none and listing none
        // read as none; and text that is no JSON is refused.
        Assert.Contains("Ticker not found", Assert.Throws<ProviderRefusal>(() => TipRanksAnswers.Ratings("{\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"content\":[{\"type\":\"text\",\"text\":\"Ticker not found\"}],\"isError\":true}}")).Message, StringComparison.Ordinal);
        Assert.Contains("Invalid params", Assert.Throws<ProviderRefusal>(() => TipRanksAnswers.Usage("{\"jsonrpc\":\"2.0\",\"id\":1,\"error\":{\"code\":-32602,\"message\":\"Invalid params\"}}")).Message, StringComparison.Ordinal);
        Assert.Null(TipRanksAnswers.Ratings(ToolAnswer("{\"ticker\":\"ZZZZ\",\"asOf\":\"2026-10-23\",\"totalRatings\":0,\"counts\":{\"last30Days\":{\"analysts\":0,\"buy\":0,\"hold\":0,\"sell\":0}},\"ratings\":[]}")));
        Assert.Throws<FormatException>(() => TipRanksAnswers.Ratings("Bad Gateway"));

        // The secrets file's limit read from the number it opens with, and none from words or nought.
        Assert.Equal([100, null, null, null, null], new[] { "100 Calls/Month", null, string.Empty, "Calls", "0" }.Select(TipRanksCredentials.Limit));
    }

    [Fact]
    public async Task TheTipRanksFeedSendsTheKeyInItsHeaderAloneAndTakesItOutOfAFailuresWords()
    {
        var server = new TipRanksServer();
        var feed = new TipRanksRatingsFeed(
            new HttpClient(server) { BaseAddress = new Uri(TipRanksRatingsFeed.Address) },
            new TipRanksCredentials(PlantedKey),
            new ProviderRequest(TipRanksRatingsFeed.Policy, (_, _) => Task.CompletedTask),
            new ProviderRequest(TipRanksRatingsFeed.Policy with { Attempts = 1 }, (_, _) => Task.CompletedTask));

        Assert.Equal(3, (await feed.UsageAsync()).Used);
        Assert.Equal(16, (await feed.RatingsAsync("HPQ"))!.Ratings.Count);

        // The session opened, its notice, the count and the ratings: four requests, each to the server's own address with
        // no query, each carrying the key as a bearer token, and every one after the opening carrying the session it named.
        Assert.Equal(4, feed.Requests);
        Assert.Equal(["initialize", "notifications/initialized", "tools/call get_my_usage", "tools/call get_recent_analyst_ratings HPQ"], server.Asked.Select(asked => asked.Method));
        Assert.All(server.Asked, asked => Assert.Equal(TipRanksRatingsFeed.Address, asked.Address));
        Assert.All(server.Asked, asked => Assert.Equal("Bearer " + PlantedKey, asked.Authorization));
        Assert.All(server.Asked, asked => Assert.DoesNotContain(PlantedKey, asked.Address, StringComparison.Ordinal));
        Assert.Equal([null, "session-1", "session-1", "session-1"], server.Asked.Select(asked => asked.Session));

        // A server that cannot be reached and whose failure quotes the key and an address: the refusal holds neither.
        var failing = new TipRanksRatingsFeed(
            new HttpClient(new FailingHandler($"could not reach https://mcp.tipranks.com/mcp/?apikey={PlantedKey}: {PlantedKey} was refused")) { BaseAddress = new Uri(TipRanksRatingsFeed.Address) },
            new TipRanksCredentials(PlantedKey),
            new ProviderRequest(TipRanksRatingsFeed.Policy, (_, _) => Task.CompletedTask));
        var refusal = await Assert.ThrowsAsync<ProviderRefusal>(() => failing.UsageAsync());

        Assert.DoesNotContain(PlantedKey, refusal.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("https://", refusal.Message, StringComparison.Ordinal);
        Assert.Contains(ProviderCredentials.KeyWithheld, refusal.Message, StringComparison.Ordinal);

        // The count is asked once more on a transient refusal, and the ratings, which spend a call, never are.
        var busy = new TipRanksServer { BusyFor = ["tools/call get_my_usage", "tools/call get_recent_analyst_ratings HPQ"] };
        var again = new TipRanksRatingsFeed(
            new HttpClient(busy) { BaseAddress = new Uri(TipRanksRatingsFeed.Address) },
            new TipRanksCredentials(PlantedKey),
            new ProviderRequest(TipRanksRatingsFeed.Policy, (_, _) => Task.CompletedTask),
            new ProviderRequest(TipRanksRatingsFeed.Policy with { Attempts = 1 }, (_, _) => Task.CompletedTask));

        Assert.Equal(3, (await again.UsageAsync()).Used);
        await Assert.ThrowsAsync<ProviderRefusal>(() => again.RatingsAsync("HPQ"));
        Assert.Equal(2, busy.Asked.Count(asked => asked.Method == "tools/call get_my_usage"));
        Assert.Equal(1, busy.Asked.Count(asked => asked.Method == "tools/call get_recent_analyst_ratings HPQ"));
    }

    [Fact]
    public async Task TheTipRanksFeedReadsNoBodyFromANoticeTheServerAcceptsAndClosesEachConnection()
    {
        // TipRanks' server answered the notice that a session is open 202 with no message and a body sent with no length,
        // which it held open on a connection kept alive, as the first live call of 2026-10-23 found: the feed waited on it
        // past its bound on each try and the job asked for no ratings. A server holding that body open for good, against
        // a bound of two seconds: the count is read, and every request asks for its connection to be closed.
        var server = new TipRanksServer { NoticeNeverEnds = true };
        var feed = new TipRanksRatingsFeed(
            new HttpClient(server) { BaseAddress = new Uri(TipRanksRatingsFeed.Address) },
            new TipRanksCredentials(PlantedKey),
            new ProviderRequest(TipRanksRatingsFeed.Policy with { Attempts = 1, Timeout = TimeSpan.FromSeconds(2) }, (_, _) => Task.CompletedTask));

        Assert.Equal(new TipRanksUsage(3, 100, new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero)), await feed.UsageAsync());
        Assert.Equal(["initialize", "notifications/initialized", "tools/call get_my_usage"], server.Asked.Select(asked => asked.Method));
        Assert.All(server.Asked, asked => Assert.True(asked.Closes));
    }

    [Fact]
    public async Task TheAnalystJobAsksYahooOnceUnderTheNewYorkDaysCapAndStoresWhatItSent()
    {
        using var store = new TemporaryStore().Migrated();

        var hpq = YahooAnalysts.Parse(AnalystCapture("analysts-yahoo-HPQ.json"))!;
        var feed = new ConstructedAnalystFeed(hpq);
        var outcome = await new AnalystJob(new FixedClock(AnalystsAsked.AddSeconds(4), NewYork), store.DatabaseFile, feed).RunAsync("HPQ", AnalystsAsked);

        Assert.Equal((AnalystJob.Answered, 1), (outcome.Outcome, feed.Requests));
        Assert.Equal(
            ["2026-10-23T15:00:00Z|2026-10-23T15:00:04Z|USD|19.0|27.51765|29.0|33.8|17|hold"],
            Query(store, "SELECT asked_at, answered_at, currency, target_low, target_mean, target_median, target_high, analysts, recommendation FROM analyst_view WHERE ticker = 'HPQ';"));

        // The lists read back as Yahoo sent them, each target in the store's own form.
        var lists = Query(store, "SELECT months, actions FROM analyst_view;").Single().Split('|');

        Assert.Equal(hpq.Months, EquityBrief.Data.Analysts.StoredAnalysts.MonthsOf(lists[0]));
        Assert.Equal(hpq.Actions, EquityBrief.Data.Analysts.StoredAnalysts.ActionsOf(lists[1]));
        Assert.Equal(["answered|1|1|HPQ|2026-10-23T15:00:00Z"], Query(store, $"SELECT outcome, rows_written, network_requests, json_extract(detail, '$.ticker'), json_extract(detail, '$.askedAt') FROM run_log WHERE stage = '{AnalystJob.Stage}';"));

        // The cap counts the runs of the New York day that asked Yahoo: one at 03:59Z, the evening before in New York, and
        // one that asked nothing count none; with 98 more the next ask is the 100th and asks, and the one after it none.
        store.Execute($"INSERT INTO run_log (run_id, stage, started_at, ended_at, outcome, rows_written, model_calls, network_requests, spend, detail) VALUES ('yesterday', '{AnalystJob.Stage}', '2026-10-23T03:59:00Z', '2026-10-23T03:59:01Z', 'answered', 1, 0, 3, '0', '{{}}'), ('nothing', '{AnalystJob.Stage}', '2026-10-23T05:00:00Z', '2026-10-23T05:00:01Z', 'cap reached', 0, 0, 0, '0', '{{}}');");

        for (var at = 0; at < 98; at++)
        {
            store.Execute($"INSERT INTO run_log (run_id, stage, started_at, ended_at, outcome, rows_written, model_calls, network_requests, spend, detail) VALUES ('asked-{at}', '{AnalystJob.Stage}', '2026-10-23T14:00:00Z', '2026-10-23T14:00:01Z', 'failed', 0, 0, 3, '0', '{{}}');");
        }

        var underTheCap = new ConstructedAnalystFeed(hpq);

        Assert.Equal(AnalystJob.Answered, (await new AnalystJob(new FixedClock(AnalystsAsked.AddMinutes(20), NewYork), store.DatabaseFile, underTheCap).RunAsync("KEYS", AnalystsAsked.AddMinutes(20))).Outcome);

        var atTheCap = new ConstructedAnalystFeed(hpq);
        var refused = await new AnalystJob(new FixedClock(AnalystsAsked.AddMinutes(40), NewYork), store.DatabaseFile, atTheCap).RunAsync("MSFT", AnalystsAsked.AddMinutes(40));

        Assert.Equal((AnalystJob.AtTheCap, 0), (refused.Outcome, atTheCap.Requests));
        Assert.Equal($"{AnalystLimits.DailyCap} asks of Yahoo this New York day against a cap of {AnalystLimits.DailyCap}, so nothing was asked", refused.Detail);

        // The next New York day asks again.
        Assert.Equal(AnalystJob.Answered, (await new AnalystJob(new FixedClock(new DateTimeOffset(2026, 10, 24, 4, 0, 0, TimeSpan.Zero), NewYork), store.DatabaseFile, new ConstructedAnalystFeed(hpq)).RunAsync("MSFT", new DateTimeOffset(2026, 10, 24, 4, 0, 0, TimeSpan.Zero))).Outcome);

        // An answer holding no analysts stores nothing, and a failure's address is taken out of its words.
        using var other = new TemporaryStore().Migrated();

        Assert.Equal(AnalystJob.NoAnalysts, (await new AnalystJob(new FixedClock(AnalystsAsked, NewYork), other.DatabaseFile, new ConstructedAnalystFeed(null)).RunAsync("ZZZZ", AnalystsAsked)).Outcome);

        var failed = await new AnalystJob(new FixedClock(AnalystsAsked.AddMinutes(1), NewYork), other.DatabaseFile, new ConstructedAnalystFeed(hpq, new ProviderRefusal("Yahoo's quote summary could not be reached: GET https://query1.finance.yahoo.com/v10/finance/quoteSummary/HPQ?crumb=abc failed", transient: true))).RunAsync("HPQ", AnalystsAsked.AddMinutes(1));

        Assert.Equal(AnalystJob.Failed, failed.Outcome);
        Assert.DoesNotContain("https://", failed.Detail, StringComparison.Ordinal);
        Assert.Contains(ProviderCredentials.AddressWithheld, failed.Detail, StringComparison.Ordinal);
        Assert.Empty(Query(other, "SELECT ticker FROM analyst_view;"));
        Assert.Equal([AnalystJob.NoAnalysts, AnalystJob.Failed], Query(other, $"SELECT outcome FROM run_log WHERE stage = '{AnalystJob.Stage}' ORDER BY started_at;"));
    }

    [Fact]
    public async Task TheRatingsJobAsksTheCountFirstAndAsksNothingPastTheMonthsLimitOrInsideAStocksFifteenDays()
    {
        using var store = new TemporaryStore().Migrated();

        var hpq = TipRanksAnswers.Ratings(AnalystCapture("analysts-tipranks-HPQ.json"))!;
        var redact = new TipRanksCredentials(PlantedKey).Redact;

        async Task<(AnalystOutcome Outcome, ConstructedRatingsFeed Feed)> Run(string ticker, int used, int tipranks, int? stated, double minutes, Exception? failure = null)
        {
            var feed = new ConstructedRatingsFeed(used, tipranks, hpq, failure);

            return (await new RatingsJob(new FixedClock(AnalystsAsked.AddMinutes(minutes), NewYork), store.DatabaseFile, feed, stated, redact).RunAsync(ticker, AnalystsAsked.AddMinutes(minutes)), feed);
        }

        // 3 of 100 used: the count, the ratings and the count again, the ratings stored with the 4 the call left.
        var (answered, first) = await Run("HPQ", 3, 100, 100, 0);

        Assert.Equal((RatingsJob.Answered, 2, 1), (answered.Outcome, first.UsageCalls, first.RatingsCalls));
        Assert.Equal(["2026-10-23T15:00:00Z|2026-10-23|16"], Query(store, "SELECT asked_at, as_of, total FROM analyst_rating WHERE ticker = 'HPQ';"));
        Assert.Equal(hpq.Ratings, EquityBrief.Data.Analysts.StoredAnalysts.RatingsOf(Query(store, "SELECT ratings FROM analyst_rating;").Single()));
        Assert.Equal(["answered|4|100|2026-11-01T00:00:00Z"], Query(store, $"SELECT outcome, json_extract(detail, '$.used'), json_extract(detail, '$.limit'), json_extract(detail, '$.resetsAt') FROM run_log WHERE stage = '{RatingsJob.Stage}';"));

        // The same stock inside the 15 days after the press TipRanks answered asks nothing at all, half an hour after it and
        // a second before the 15th day's instant, and at that instant it asks again.
        var (again, twice) = await Run("HPQ", 4, 100, 100, 30);

        Assert.Equal((RatingsJob.Waiting, 0, 0), (again.Outcome, twice.UsageCalls, twice.RatingsCalls));
        Assert.Equal("TipRanks answered for HPQ at 2026-10-23T15:00:00Z, and its press opens again at 2026-11-07T15:00:00Z, so no call was made", again.Detail);

        var (justBefore, notYet) = await Run("HPQ", 4, 100, 100, (AnalystLimits.RatingsWaitDays * 24 * 60) - (1 / 60d));

        Assert.Equal((RatingsJob.Waiting, 0), (justBefore.Outcome, notYet.RatingsCalls));

        var (opened, asked) = await Run("HPQ", 4, 100, 100, AnalystLimits.RatingsWaitDays * 24 * 60);

        Assert.Equal((RatingsJob.Answered, 1), (opened.Outcome, asked.RatingsCalls));

        // At the limit no ratings are asked, and one under it they are: 100 of 100 against 99, the secrets file's 50 the
        // lesser at 50 against 49, and TipRanks' own 40 the lesser at 40 against 39.
        foreach (var (used, tipranks, stated, spent) in new (int, int, int?, bool)[] { (100, 100, 100, true), (99, 100, 100, false), (50, 100, 50, true), (49, 100, 50, false), (40, 40, 100, true), (39, 40, 100, false), (100, 100, null, true) })
        {
            var ticker = $"T{used}{tipranks}{stated}";
            var (outcome, feed) = await Run(ticker, used, tipranks, stated, 1);

            Assert.Equal((spent ? RatingsJob.AtTheLimit : RatingsJob.Answered, spent ? 0 : 1), (outcome.Outcome, feed.RatingsCalls));
            Assert.Equal(Math.Min(stated ?? tipranks, tipranks).ToString(System.Globalization.CultureInfo.InvariantCulture), Query(store, $"SELECT json_extract(detail, '$.limit') FROM run_log WHERE stage = '{RatingsJob.Stage}' AND json_extract(detail, '$.ticker') = '{ticker}';").Single());
        }

        // A failure whose words carry the key and an address: neither reaches the run's row, its line or the console's.
        var (failed, broken) = await Run("KEYS", 10, 100, 100, 2, new ProviderRefusal($"TipRanks could not be reached: key {PlantedKey} at https://mcp.tipranks.com/mcp/?apikey={PlantedKey}", transient: true));

        Assert.Equal((RatingsJob.Failed, 1), (failed.Outcome, broken.RatingsCalls));
        Assert.DoesNotContain(PlantedKey, failed.Detail, StringComparison.Ordinal);
        Assert.DoesNotContain(PlantedKey, string.Join("\n", Query(store, "SELECT detail FROM run_log;")), StringComparison.Ordinal);
        Assert.DoesNotContain("https://", string.Join("\n", Query(store, "SELECT detail FROM run_log;")), StringComparison.Ordinal);

        // A count that cannot be read asks for no ratings.
        var unread = new ConstructedRatingsFeed(10, 100, hpq, usageFailure: new ProviderRefusal("TipRanks answered 503.", transient: true));

        Assert.Equal(RatingsJob.Failed, (await new RatingsJob(new FixedClock(AnalystsAsked.AddMinutes(3), NewYork), store.DatabaseFile, unread, 100, redact).RunAsync("NFLX", AnalystsAsked.AddMinutes(3))).Outcome);
        Assert.Equal(0, unread.RatingsCalls);

        // And having reached no answer it starts no wait: NFLX's next press asks a minute later.
        var (afterNone, askedAgain) = await Run("NFLX", 10, 100, 100, 4);

        Assert.Equal((RatingsJob.Answered, 1), (afterNone.Outcome, askedAgain.RatingsCalls));
    }

    static string ToolAnswer(string tool) =>
        JsonSerializer.Serialize(new { jsonrpc = "2.0", id = 1, result = new { content = new[] { new { type = "text", text = tool } }, isError = false } });

    // Yahoo answering one constructed answer, or failing, counting its asks.
    sealed class ConstructedAnalystFeed(YahooAnalystAnswer? answer, Exception? failure = null) : IAnalystFeed
    {
        public int Requests { get; private set; }

        public Task<YahooAnalystAnswer?> AnalystsAsync(string ticker, CancellationToken cancellation = default)
        {
            Requests++;

            return failure is { } thrown ? throw thrown : Task.FromResult(answer);
        }
    }

    // TipRanks counting the month's calls as its server does, one more for each ratings call, and answering one
    // constructed answer or failing.
    sealed class ConstructedRatingsFeed(int used, int limit, TipRanksAnswer? answer, Exception? ratingsFailure = null, Exception? usageFailure = null) : IAnalystRatingsFeed
    {
        int counted = used;

        public int UsageCalls { get; private set; }

        public int RatingsCalls { get; private set; }

        public int Requests => UsageCalls + RatingsCalls;

        public Task<TipRanksUsage> UsageAsync(CancellationToken cancellation = default)
        {
            UsageCalls++;

            return usageFailure is { } thrown
                ? throw thrown
                : Task.FromResult(new TipRanksUsage(counted, limit, new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero)));
        }

        public Task<TipRanksAnswer?> RatingsAsync(string ticker, CancellationToken cancellation = default)
        {
            RatingsCalls++;
            counted++;

            return ratingsFailure is { } thrown ? throw thrown : Task.FromResult(answer);
        }
    }

    // TipRanks' server as the feed meets it: the opening answered with a session in its header, the notice taken with no
    // body, and each tool answered with its capture; a method named busy answered 503 the first time.
    sealed class TipRanksServer : HttpMessageHandler
    {
        public List<(string Method, string Address, string? Authorization, string? Session, bool Closes)> Asked { get; } = [];

        public HashSet<string> BusyFor { get; init; } = [];

        // The notice answered 202 with a body that never ends, as the live server held it open on a kept connection.
        public bool NoticeNeverEnds { get; init; }

        readonly HashSet<string> refused = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            var root = body.RootElement;
            var method = root.GetProperty("method").GetString()!;

            if (method == "tools/call")
            {
                var call = root.GetProperty("params");

                method += " " + call.GetProperty("name").GetString() + (call.GetProperty("arguments").TryGetProperty("ticker", out var ticker) ? " " + ticker.GetString() : string.Empty);
            }

            Asked.Add((
                method,
                request.RequestUri!.AbsoluteUri,
                request.Headers.Authorization?.ToString(),
                request.Headers.TryGetValues(TipRanksRatingsFeed.SessionHeader, out var sessions) ? sessions.Single() : null,
                request.Headers.ConnectionClose == true));

            if (BusyFor.Contains(method) && refused.Add(method))
            {
                return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
            }

            var answer = method switch
            {
                "initialize" => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("event: message\ndata: {\"jsonrpc\":\"2.0\",\"id\":1,\"result\":{\"protocolVersion\":\"2025-06-18\",\"serverInfo\":{\"name\":\"TipRanksMCP\",\"version\":\"\"}}}\n\n") },
                "notifications/initialized" => NoticeNeverEnds
                    ? new HttpResponseMessage(HttpStatusCode.Accepted) { Content = new StreamContent(new EndlessStream()) }
                    : new HttpResponseMessage(HttpStatusCode.Accepted),
                "tools/call get_my_usage" => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(AnalystCapture("analysts-tipranks-usage.json")) },
                _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(AnalystCapture("analysts-tipranks-HPQ.json")) },
            };

            if (method == "initialize")
            {
                answer.Headers.Add(TipRanksRatingsFeed.SessionHeader, "session-1");
            }

            return answer;
        }
    }

    // A body that never ends: each read waits until the request's own bound cancels it.
    sealed class EndlessStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);

            return 0;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    // A server that cannot be reached, its failure in the words given.
    sealed class FailingHandler(string said) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException(said);
    }
}
