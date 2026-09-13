using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Spending;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Research;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 6.7: the spend cap as a rule over a ledger, each cap on its
// own, and section 17's row read off the document against the constants.
//
// Over constructed ledgers, because the rule is a function of rows and an instant and
// the edges it turns on, the last second of a UTC day and the first of a month, are
// shapes no recorded pass lands on.
public partial class FixtureExpectations
{
    static SpentRow Spent(string instant, string amount) =>
        new(DateTimeOffset.Parse(instant, CultureInfo.InvariantCulture), decimal.Parse(amount, CultureInfo.InvariantCulture));

    [Fact]
    public void TheDayCapRefusesOnItsOwnAndResumesAtTheNextUtcMidnight()
    {
        // Today at the day cap and the month well below its own.
        var ledger = new SpendLedger(
        [
            Spent("2026-09-15T14:00:00Z", "6.00"),
            Spent("2026-09-15T18:30:00Z", "4.00"),
            Spent("2026-09-14T23:59:59Z", "3.00"),
        ]);

        var verdict = SpendRule.Judge(ledger, SpendCaps.Default, DateTimeOffset.Parse("2026-09-15T20:00:00Z", CultureInfo.InvariantCulture));

        Assert.True(verdict.Paused);
        Assert.Equal(SpendVerdict.DayCap, verdict.Cap);
        Assert.Equal(10.00m, verdict.SpentToday);
        Assert.Equal(13.00m, verdict.SpentThisMonth);
        Assert.Equal(DateTimeOffset.Parse("2026-09-16T00:00:00Z", CultureInfo.InvariantCulture), verdict.ResumesAt);
        Assert.Equal(
            "research is paused: today's spend of $10.00 has reached the $10.00 day cap, and it resumes at 2026-09-16 00:00 UTC",
            verdict.Line);

        // A cent below, and it does not.
        var below = SpendRule.Judge(
            new SpendLedger([Spent("2026-09-15T14:00:00Z", "9.99")]),
            SpendCaps.Default,
            DateTimeOffset.Parse("2026-09-15T20:00:00Z", CultureInfo.InvariantCulture));

        Assert.False(below.Paused);
        Assert.Null(below.ResumesAt);

        // The day is a UTC day: the same spend a second before midnight belongs to
        // the day before, and a pass just after midnight is judged on a fresh day.
        var afterMidnight = SpendRule.Judge(ledger, SpendCaps.Default, DateTimeOffset.Parse("2026-09-16T00:00:01Z", CultureInfo.InvariantCulture));

        Assert.False(afterMidnight.Paused);
        Assert.Equal(0m, afterMidnight.SpentToday);
    }

    [Fact]
    public void TheMonthCapRefusesOnItsOwnAndResumesAtTheFirstOfTheNextUtcMonth()
    {
        // Nothing spent today, and the month at its cap from earlier days.
        var ledger = new SpendLedger(
        [
            Spent("2026-09-01T00:00:00Z", "20.00"),
            Spent("2026-09-10T12:00:00Z", "30.00"),
            Spent("2026-08-31T23:59:59Z", "40.00"),
        ]);

        var verdict = SpendRule.Judge(ledger, SpendCaps.Default, DateTimeOffset.Parse("2026-09-15T20:00:00Z", CultureInfo.InvariantCulture));

        Assert.True(verdict.Paused);
        Assert.Equal(SpendVerdict.MonthCap, verdict.Cap);
        Assert.Equal(0m, verdict.SpentToday);
        Assert.Equal(50.00m, verdict.SpentThisMonth);
        Assert.Equal(DateTimeOffset.Parse("2026-10-01T00:00:00Z", CultureInfo.InvariantCulture), verdict.ResumesAt);

        // August's forty is August's: on the last second of August the month stands at
        // forty, below its cap, and what stops research then is the day, which spent
        // all forty and resumes a second later. A December pause resumes in the next
        // year.
        var lastOfAugust = SpendRule.Judge(ledger, SpendCaps.Default, DateTimeOffset.Parse("2026-08-31T23:59:59Z", CultureInfo.InvariantCulture));

        Assert.Equal(SpendVerdict.DayCap, lastOfAugust.Cap);
        Assert.Equal(40.00m, lastOfAugust.SpentThisMonth);
        Assert.Equal(DateTimeOffset.Parse("2026-09-01T00:00:00Z", CultureInfo.InvariantCulture), lastOfAugust.ResumesAt);

        var december = SpendRule.Judge(
            new SpendLedger([Spent("2026-12-03T09:00:00Z", "50.00")]),
            SpendCaps.Default,
            DateTimeOffset.Parse("2026-12-20T09:00:00Z", CultureInfo.InvariantCulture));

        Assert.Equal(DateTimeOffset.Parse("2027-01-01T00:00:00Z", CultureInfo.InvariantCulture), december.ResumesAt);

        // And a month is a month of a year: last September's fifty is not this
        // September's. Every read that fills a ledger starts at the month's first instant,
        // so no shipped path hands it a row a year old, and the ledger holds the year
        // itself rather than leaning on those reads.
        var yearEarlier = new SpendLedger([Spent("2025-09-10T12:00:00Z", "50.00")]);

        Assert.Equal(0m, yearEarlier.SpentIn(2026, 9));
        Assert.False(SpendRule.Judge(yearEarlier, SpendCaps.Default, DateTimeOffset.Parse("2026-09-15T20:00:00Z", CultureInfo.InvariantCulture)).Paused);
    }

    [Fact]
    public void WhereBothCapsAreReachedTheMonthIsNamedBecauseItResumesLater()
    {
        var ledger = new SpendLedger(
        [
            Spent("2026-09-15T10:00:00Z", "10.00"),
            Spent("2026-09-02T10:00:00Z", "45.00"),
        ]);

        var verdict = SpendRule.Judge(ledger, SpendCaps.Default, DateTimeOffset.Parse("2026-09-15T11:00:00Z", CultureInfo.InvariantCulture));

        Assert.Equal(SpendVerdict.MonthCap, verdict.Cap);
        Assert.Equal(DateTimeOffset.Parse("2026-10-01T00:00:00Z", CultureInfo.InvariantCulture), verdict.ResumesAt);
    }

    [Fact]
    public void ACallThatCouldTakeSpendPastACapIsRefusedBeforeTheCapIsReached()
    {
        // Nine dollars ninety spent today. A call that could cost twenty cents would
        // take the day past its cap, so it is refused although the cap has not been
        // reached; one that could cost ten cents lands on the cap exactly and is let
        // through, and the next call after it meets a cap that has been reached.
        var ledger = new SpendLedger([Spent("2026-09-15T14:00:00Z", "9.90")]);
        var now = DateTimeOffset.Parse("2026-09-15T20:00:00Z", CultureInfo.InvariantCulture);

        var tooDear = SpendRule.Judge(ledger, SpendCaps.Default, now, ceiling: 0.20m);

        Assert.True(tooDear.Paused);
        Assert.False(tooDear.Reached);
        Assert.Equal(SpendVerdict.DayCap, tooDear.Cap);
        Assert.Equal(
            "research is paused: a call that could cost $0.20 would take today's spend of $9.90 past the $10.00 day cap, and it resumes at 2026-09-16 00:00 UTC",
            tooDear.Line);

        Assert.False(SpendRule.Judge(ledger, SpendCaps.Default, now, ceiling: 0.10m).Paused);

        var after = new SpendLedger([Spent("2026-09-15T14:00:00Z", "9.90"), Spent("2026-09-15T20:00:00Z", "0.10")]);

        Assert.True(SpendRule.Judge(after, SpendCaps.Default, now, ceiling: 0.0001m).Reached);

        // The month refuses a call on its own terms the same way.
        var month = SpendRule.Judge(
            new SpendLedger([Spent("2026-09-02T10:00:00Z", "49.95")]),
            SpendCaps.Default,
            now,
            ceiling: 0.06m);

        Assert.Equal(SpendVerdict.MonthCap, month.Cap);
        Assert.False(month.Reached);

        Assert.Throws<ArgumentOutOfRangeException>(() => SpendRule.Judge(ledger, SpendCaps.Default, now, ceiling: -0.01m));
    }

    [Fact]
    public void ACapIsReadAsConfigurationWritesItAndRefusedWhereItIsNotMoney()
    {
        Assert.Equal(SpendCaps.Default, SpendCaps.From(null, " "));
        Assert.Equal(new SpendCaps(12.5m, 80m), SpendCaps.From("12.5", "80"));

        Assert.Contains($"'{SpendCaps.DayKey}' is '10 dollars'", Assert.Throws<InvalidOperationException>(() => SpendCaps.From("10 dollars", null)).Message, StringComparison.Ordinal);
        Assert.Throws<InvalidOperationException>(() => SpendCaps.From("0", null));
        Assert.Throws<InvalidOperationException>(() => new SpendCaps(10m, -1m));

        // A fraction of a cent is drawn as what it is rather than as nothing.
        Assert.Equal("$0.0001", SpendVerdict.Money(0.00006m));
        Assert.Equal("$0.00", SpendVerdict.Money(0m));
        Assert.Equal("$12.35", SpendVerdict.Money(12.345m));
    }

    // ---- the spend cap, the one component that makes a paid call ----

    // The shipped configuration's research model, with a key no test sends.
    static ResearchModelSettings Research(string? options = null) => Providers.ResearchModelFeedTests.Shipped(options);

    static IClock SpendClock(string instant) =>
        FixedClock.At(DateTimeOffset.Parse(instant, CultureInfo.InvariantCulture), SessionZones.UnitedStates);

    // A row some earlier call or stage wrote, as the store would hold it.
    static void SpentBefore(TemporaryStore store, string runId, string stage, string startedAt, string spend) =>
        store.Execute(
            "INSERT INTO run_log (run_id, stage, started_at, ended_at, outcome, rows_written, model_calls, network_requests, spend, detail) " +
            $"VALUES ('{runId}', '{stage}', '{startedAt}', '{startedAt}', 'ok', 0, 1, 1, '{spend}', '{{}}');");

    static IReadOnlyList<string> CallRows(TemporaryStore store, string runId) =>
        Query(store, $"SELECT stage, outcome, model_calls, network_requests, spend FROM run_log WHERE run_id = '{runId}' ORDER BY rowid;");

    [Fact]
    public async Task APaidCallBelowBothCapsIsMadeOnceAndWhatItCostIsWrittenOnARowOfItsOwn()
    {
        using var store = new TemporaryStore().Migrated();

        var settings = Research();
        var feed = new RecordedResearchModelFeed(Folder(), settings);
        var cap = new SpendCap(feed, SpendCaps.Default, SpendClock("2026-09-15T20:00:00Z"), store.DatabaseFile);
        var request = Providers.ResearchModelFeedTests.Recorded(settings);

        var call = await cap.AskAsync(request, "pass-below");

        Assert.True(call.Answered);
        Assert.False(call.Paused);
        Assert.Equal(1, feed.Requests);

        // The price is the recording's own counts at the configured rates, which the
        // feed tests work out by hand, written as money in TEXT and never rounded.
        Assert.Equal(0.0001731m, call.Price);
        Assert.Equal(["research call: The two cases|ok|1|1|0.0001731"], CallRows(store, "pass-below"));

        // And the ledger the next call is judged by is the store's.
        await using var connection = store.Open();

        var ledger = await SpendCap.LedgerAsync(connection, DateTimeOffset.Parse("2026-09-15T20:00:00Z", CultureInfo.InvariantCulture));

        Assert.Equal(0.0001731m, ledger.SpentOn(new DateOnly(2026, 9, 15)));
    }

    [Fact]
    public async Task APassAtTheDayCapIsRefusedBeforeItMakesACall()
    {
        using var store = new TemporaryStore().Migrated();

        // Ten dollars today from two earlier calls, and the month well below its cap.
        SpentBefore(store, "earlier", "research call: The short version", "2026-09-15T14:00:00Z", "6.00");
        SpentBefore(store, "earlier", "research call: The two cases", "2026-09-15T18:30:00Z", "4.00");

        var settings = Research();
        var feed = new RecordedResearchModelFeed(Folder(), settings);
        var call = await new SpendCap(feed, SpendCaps.Default, SpendClock("2026-09-15T20:00:00Z"), store.DatabaseFile)
            .AskAsync(Providers.ResearchModelFeedTests.Recorded(settings), "pass-at-day-cap");

        Assert.True(call.Paused);
        Assert.False(call.Answered);
        Assert.Equal(0, feed.Requests);
        Assert.Equal(SpendVerdict.DayCap, call.Verdict.Cap);

        // A row saying the pass was stopped, carrying no call and no spend.
        Assert.Equal(["research call: The two cases|paused|0|0|0"], CallRows(store, "pass-at-day-cap"));

        using var detail = JsonDocument.Parse(Query(store, "SELECT detail FROM run_log WHERE run_id = 'pass-at-day-cap';").Single());

        Assert.Equal("2026-09-16T00:00:00Z", detail.RootElement.GetProperty("resumesAt").GetString());
        Assert.Equal(SpendVerdict.DayCap, detail.RootElement.GetProperty("cap").GetString());
    }

    [Fact]
    public async Task APassAtTheMonthCapIsRefusedOnItsOwnWithNothingSpentToday()
    {
        using var store = new TemporaryStore().Migrated();

        SpentBefore(store, "first", "research call: The two cases", "2026-09-02T10:00:00Z", "30.00");
        SpentBefore(store, "second", "research call: The two cases", "2026-09-09T10:00:00Z", "20.00");

        var settings = Research();
        var feed = new RecordedResearchModelFeed(Folder(), settings);
        var call = await new SpendCap(feed, SpendCaps.Default, SpendClock("2026-09-15T20:00:00Z"), store.DatabaseFile)
            .AskAsync(Providers.ResearchModelFeedTests.Recorded(settings), "pass-at-month-cap");

        Assert.Equal(0, feed.Requests);
        Assert.Equal(SpendVerdict.MonthCap, call.Verdict.Cap);
        Assert.Equal(0m, call.Verdict.SpentToday);
        Assert.Equal(DateTimeOffset.Parse("2026-10-01T00:00:00Z", CultureInfo.InvariantCulture), call.Verdict.ResumesAt);
    }

    [Fact]
    public async Task ACallThatCouldPassTheCapIsRefusedAndOneThatFitsIsMadeWithTheLedgerReadOffTheStore()
    {
        var settings = Research();
        var request = Providers.ResearchModelFeedTests.Recorded(settings);
        var ceiling = settings.Pricing.Ceiling(request, settings.AnswerTokens);

        // Spend a stage other than the cap wrote counts, because the cap reads the store
        // rather than a figure of its own. Just below the cap by less than the ceiling:
        // refused, although the cap has not been reached.
        using (var close = new TemporaryStore().Migrated())
        {
            SpentBefore(close, "elsewhere", "another stage", "2026-09-15T12:00:00Z", (10m - ceiling / 2m).ToString(CultureInfo.InvariantCulture));

            var feed = new RecordedResearchModelFeed(Folder(), settings);
            var call = await new SpendCap(feed, SpendCaps.Default, SpendClock("2026-09-15T20:00:00Z"), close.DatabaseFile).AskAsync(request, "pass-too-dear");

            Assert.True(call.Paused);
            Assert.False(call.Verdict.Reached);
            Assert.Equal(0, feed.Requests);
        }

        // Below by more than the ceiling: made.
        using var room = new TemporaryStore().Migrated();

        SpentBefore(room, "elsewhere", "another stage", "2026-09-15T12:00:00Z", (10m - ceiling * 2m).ToString(CultureInfo.InvariantCulture));

        var fits = new RecordedResearchModelFeed(Folder(), settings);

        Assert.True((await new SpendCap(fits, SpendCaps.Default, SpendClock("2026-09-15T20:00:00Z"), room.DatabaseFile).AskAsync(request, "pass-fits")).Answered);
        Assert.Equal(1, fits.Requests);
    }

    [Fact]
    public async Task ACallThatFailsRecordsTheAttemptAndSpendsNothing()
    {
        using var store = new TemporaryStore().Migrated();

        var settings = Research();

        using var client = new HttpClient(new NothingListening()) { BaseAddress = new Uri(settings.BaseAddress) };

        var call = await new SpendCap(new OpenAiCompatibleResearchFeed(client, settings), SpendCaps.Default, SpendClock("2026-09-15T20:00:00Z"), store.DatabaseFile)
            .AskAsync(Providers.ResearchModelFeedTests.Recorded(settings), "pass-unreachable");

        Assert.False(call.Answered);
        Assert.Contains("could not be reached", call.Failure, StringComparison.Ordinal);
        Assert.Equal(["research call: The two cases|unavailable|1|1|0"], CallRows(store, "pass-unreachable"));
    }

    [Fact]
    public void TheSpendExpectationIsWhatTheConfiguredRatesAndTheRuleProduce()
    {
        var expected = Expected("spend");
        var pricing = Research().Pricing;

        // The rates, against what the shipped configuration hands the pricing, for the
        // model it names.
        var rates = expected.GetProperty("rates");

        decimal Rate(string name) => decimal.Parse(rates.GetProperty(name).GetString()!, CultureInfo.InvariantCulture);

        Assert.Equal(rates.GetProperty("model").GetString(), Research().Model);
        Assert.Equal(Rate("cacheHit"), pricing.CacheHit);
        Assert.Equal(Rate("cacheMiss"), pricing.CacheMiss);
        Assert.Equal(Rate("output"), pricing.Output);
        Assert.Equal(decimal.Parse(expected.GetProperty("peakMultiple").GetString()!, CultureInfo.InvariantCulture), pricing.PeakMultiple);

        // Peak, hour by hour over one week, against the windows and days the page states.
        var days = expected.GetProperty("peakDays").EnumerateArray().Select(day => Enum.Parse<DayOfWeek>(day.GetString()!)).ToHashSet();
        var windows = expected.GetProperty("peakWindows").EnumerateArray()
            .Select(window => (From: window.GetProperty("from").GetInt32(), To: window.GetProperty("to").GetInt32()))
            .ToArray();

        Assert.Equal(windows, pricing.PeakHours);
        Assert.Equal(days.Order(), pricing.PeakDays.Order());

        var hours = 0;

        for (var instant = DateTimeOffset.Parse("2026-09-14T00:30:00Z", CultureInfo.InvariantCulture); instant < DateTimeOffset.Parse("2026-09-21T00:00:00Z", CultureInfo.InvariantCulture); instant = instant.AddHours(1))
        {
            var byHand = days.Contains(instant.UtcDateTime.DayOfWeek) && windows.Any(window => instant.UtcDateTime.Hour >= window.From && instant.UtcDateTime.Hour < window.To);

            Assert.Equal(byHand, pricing.IsPeak(instant));
            hours++;
        }

        Assert.Equal(168, hours);

        // The two recorded calls, priced by hand in the file and by the configured rates here.
        foreach (var call in expected.GetProperty("recordedCalls").EnumerateArray())
        {
            var settings = Research(call.GetProperty("options").ValueKind == JsonValueKind.Null ? null : call.GetProperty("options").GetString());
            var request = Providers.ResearchModelFeedTests.Recorded(settings);
            var answer = OpenAiCompatibleResearchFeed.Parse(File.ReadAllText(Path.Combine(Folder(), RecordedResearchModelFeed.FileFor(request))), request.Section);

            Assert.Equal(call.GetProperty("cacheHitTokens").GetInt32(), answer.CacheHitTokens);
            Assert.Equal(call.GetProperty("cacheMissTokens").GetInt32(), answer.CacheMissTokens);
            Assert.Equal(call.GetProperty("completionTokens").GetInt32(), answer.CompletionTokens);
            Assert.Equal(call.GetProperty("created").GetString(), answer.Created.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
            Assert.Equal(call.GetProperty("peak").GetBoolean(), settings.Pricing.IsPeak(answer.Created));
            Assert.Equal(decimal.Parse(call.GetProperty("price").GetString()!, CultureInfo.InvariantCulture), settings.Pricing.Price(answer));
            Assert.False(string.IsNullOrWhiteSpace(call.GetProperty("priceWorked").GetString()));
        }

        // The verdicts, each worked by hand from its rows and its instant.
        var verdicts = expected.GetProperty("verdicts").EnumerateArray().ToArray();

        Assert.Equal(3, verdicts.Length);

        foreach (var verdict in verdicts)
        {
            var ledger = new SpendLedger(verdict.GetProperty("rows").EnumerateArray().Select(row => Spent(row[0].GetString()!, row[1].GetString()!)));
            var judged = SpendRule.Judge(ledger, SpendCaps.Default, DateTimeOffset.Parse(verdict.GetProperty("now").GetString()!, CultureInfo.InvariantCulture));

            Assert.True(verdict.GetProperty("paused").GetBoolean() == judged.Paused, verdict.GetProperty("case").GetString());
            Assert.Equal(verdict.GetProperty("cap").ValueKind == JsonValueKind.Null ? null : verdict.GetProperty("cap").GetString(), judged.Cap);
            Assert.Equal(
                verdict.GetProperty("resumesAt").ValueKind == JsonValueKind.Null ? null : verdict.GetProperty("resumesAt").GetString(),
                judged.ResumesAt?.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        }
    }

    [Fact]
    public void SectionSeventeensSpendCapRowStatesTheFiguresTheCapHolds()
    {
        // Read off the row rather than restated beside the constants.
        var row = ClaimAdmissibility.Row(Scope.LimitsTable, "Spend cap");
        var figures = Regex.Match(string.Join(" ", row), @"proposed at (\d+) and (\d+) dollars");

        Assert.True(figures.Success, "section 17's spend cap row states no proposed figures in the form this reads");
        Assert.Equal(SpendCaps.DefaultDay, decimal.Parse(figures.Groups[1].Value, CultureInfo.InvariantCulture));
        Assert.Equal(SpendCaps.DefaultMonth, decimal.Parse(figures.Groups[2].Value, CultureInfo.InvariantCulture));

        // And the row says the cap is money per day and per month, which is what the
        // two constants are.
        Assert.Contains("per day and per month", string.Join(" ", row), StringComparison.Ordinal);
    }
}
