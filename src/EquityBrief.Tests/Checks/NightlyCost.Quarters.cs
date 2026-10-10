using System.Globalization;
using EquityBrief.Core.Bars;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Quarters;

namespace EquityBrief.Tests.Checks;

// nightly-cost, the fourth carve-out: the quarters fetch after the close, counted night by night over
// constructed nights with feeds the test holds, so what is asked on each night is read off the asks and
// the requests rather than off what the step says it did.
// see: A member's reported quarters are fetched on the night after it reports, and asked for again on the five nights after and weekly after that until the quarter is posted
public partial class NightlyCost
{
    // A company's fundamentals as the test has the provider file them: the eight quarters ending on or
    // before the newest one posted for the member, none where nothing is posted, and a refusal where the
    // test says the provider refuses the member.
    sealed class PostedFundamentals : IFundamentalsFeed
    {
        readonly Dictionary<string, DateOnly> posted = new(StringComparer.Ordinal);
        readonly HashSet<string> refused = new(StringComparer.Ordinal);

        public int Requests { get; private set; }

        public void Post(string ticker, DateOnly newest) => posted[ticker] = newest;

        public void Refuse(string ticker) => refused.Add(ticker);

        public Task<CompanyFundamentals> FundamentalsAsync(string ticker, CancellationToken cancellation = default)
        {
            Requests++;

            if (refused.Contains(ticker))
            {
                throw new ProviderRefusal($"The provider refused {ticker}.", transient: false);
            }

            IReadOnlyList<FiledQuarter> filed = posted.TryGetValue(ticker, out var newest)
                ? [.. Enumerable.Range(0, 8).Select(back => Filed(MonthEnd(newest.AddMonths(-3 * back))))]
                : [];

            return Task.FromResult(new CompanyFundamentals(
                ticker, "USD", "0000000000", filed, null, new EpsBases(null, null, null), new ValuationRatios(null, null),
                new MarketValue(null), new AnalystRatings(null, null, null, null, null, null, null), [], 0));
        }

        static DateOnly MonthEnd(DateOnly day) => new DateOnly(day.Year, day.Month, 1).AddMonths(1).AddDays(-1);

        static FiledQuarter Filed(DateOnly end) => new(
            end,
            end.AddDays(30),
            new QuarterFigures(1000m, 400m, 100m, 300m, 110m),
            new BalanceSheet(null, null, null, null, null),
            new ReportedEarnings(end.AddDays(25), EventTiming.After, 1.00m, 1.00m));
    }

    // One close a night for any member, which is all a stored ask's valuation needs to be asked for.
    sealed class OneClose : IHistoricalBarFeed
    {
        public int Requests { get; private set; }

        public Task<IReadOnlyList<ProviderBar>> BarsAsync(string ticker, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
        {
            Requests++;

            return Task.FromResult<IReadOnlyList<ProviderBar>>([new ProviderBar(to, 50m, 51m, 49m, 50m, 50m, 1000)]);
        }
    }

    static TemporaryStore QuarterMembers(params string[] tickers)
    {
        var store = new TemporaryStore().Migrated();

        foreach (var ticker in tickers)
        {
            store.Execute($"INSERT INTO membership (index_code, ticker, joined, \"left\", observed_at) VALUES ('{Index}', '{ticker}', '2020-01-02', NULL, '2026-09-01T23:30:00Z');");
        }

        return store;
    }

    static void Reports(TemporaryStore store, string ticker, string on, string period) =>
        store.Execute(
            "INSERT INTO calendar (ticker, event_date, kind, timing, detail, observed_at) VALUES " +
            $"('{ticker}', '{on}', 'earnings', 'after', '{{\"periodEnd\":\"{period}\",\"estimate\":null,\"actual\":null,\"surprise\":null}}', '2026-09-01T23:30:00Z');");

    // One night's quarters step. The night's weighted calls are what it had spent before the step and what
    // the step's own asks have spent since, as the night's feeds count them.
    static Task<QuartersOutcome> QuartersNight(
        TemporaryStore store,
        PostedFundamentals fundamentals,
        OneClose closes,
        DateOnly session,
        int weightedSoFar = 0,
        TimeSpan? limit = null)
    {
        var (asked, priced) = (fundamentals.Requests, closes.Requests);

        return new QuarterFetcher(
            fundamentals,
            closes,
            () => weightedSoFar
                + ((fundamentals.Requests - asked) * ProviderWeights.Fundamentals)
                + ((closes.Requests - priced) * ProviderWeights.HistoricalPerTicker),
            FixedClock.At(new DateTimeOffset(session.ToDateTime(new TimeOnly(21, 10)), TimeSpan.Zero), SessionZones.UnitedStates),
            store.DatabaseFile,
            limit).RunAsync(Index, FormattableString.Invariant($"quarters-{session:yyyyMMdd}"));
    }

    // The exchange's sessions from a day on, the nights a scheduled night runs.
    static DateOnly[] SessionsFrom(DateOnly first, int count)
    {
        var sessions = new List<DateOnly>();

        for (var day = first; sessions.Count < count; day = day.AddDays(1))
        {
            if (ExchangeClosures.IsSession(day))
            {
                sessions.Add(day);
            }
        }

        return [.. sessions];
    }

    static IReadOnlyList<string[]> AskRows(TemporaryStore store, string ticker)
    {
        using var connection = store.Open();
        using var command = connection.CreateCommand();

        command.CommandText = "SELECT session_date, reason, awaited, outcome, quarters, weighted, nights, next_ask FROM quarter_ask WHERE ticker = $t ORDER BY session_date;";
        command.Parameters.AddWithValue("$t", ticker);

        var rows = new List<string[]>();

        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            rows.Add([.. Enumerable.Range(0, reader.FieldCount).Select(field => reader.IsDBNull(field) ? "null" : Convert.ToString(reader.GetValue(field), CultureInfo.InvariantCulture)!)]);
        }

        return rows;
    }

    [Fact]
    public async Task AReportingMemberIsAskedOnTheNightAfterItsReportAndOnTheFiveAfterThenWeeklyUntilItsQuarterIsPosted()
    {
        Assert.Equal((5, 7), (QuarterFetcher.RetryNights, QuarterFetcher.WeeklyRetryDays));

        using var store = QuarterMembers("AAAA");

        var fundamentals = new PostedFundamentals();
        var closes = new OneClose();

        // The fill, on the step's first night: the member holds its quarters to 2026-06-30.
        fundamentals.Post("AAAA", new DateOnly(2026, 6, 30));

        var fill = await QuartersNight(store, fundamentals, closes, new DateOnly(2026, 10, 1));

        Assert.Equal((QuarterFetcher.Fill, QuarterFetcher.Stored), (Assert.Single(fill.Asked).Reason, fill.Asked[0].Outcome));

        // It reports on 2026-10-20 for the quarter to 2026-09-30, which the provider posts only on
        // 2026-11-09. Worked by hand over the sessions from the night after the report: asked on that
        // night and each of the five after while the answer lacks the quarter, not again until the first
        // night seven days after the sixth ask, asked then, and asked a week after that, when the quarter
        // is there and is stored, and never after.
        Reports(store, "AAAA", "2026-10-20", "2026-09-30");

        var sessions = SessionsFrom(new DateOnly(2026, 10, 21), 18);
        var asks = new List<int>();

        foreach (var session in sessions)
        {
            if (session == new DateOnly(2026, 11, 9))
            {
                fundamentals.Post("AAAA", new DateOnly(2026, 9, 30));
            }

            asks.Add((await QuartersNight(store, fundamentals, closes, session)).Asked.Count);
        }

        int[] expected = [1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0];

        Assert.Equal(expected, asks);
        Assert.Equal(
            ["2026-10-21", "2026-10-22", "2026-10-23", "2026-10-26", "2026-10-27", "2026-10-28", "2026-11-04", "2026-11-11"],
            sessions.Where((_, at) => expected[at] == 1).Select(session => session.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));

        // What each ask recorded: the first night after the report, then asks again, each awaiting the
        // quarter, the nights counted and the next ask stated once the five are spent, and the weighted
        // calls, the fundamentals alone where nothing was stored and the closes beside them where it was.
        var rows = AskRows(store, "AAAA").Skip(1).ToArray();

        Assert.Equal(
            [
                ["2026-10-21", QuarterFetcher.Report, "2026-09-30", QuarterFetcher.NotYetPosted, "0", "10", "1", "null"],
                ["2026-10-22", QuarterFetcher.Waiting, "2026-09-30", QuarterFetcher.NotYetPosted, "0", "10", "2", "null"],
                ["2026-10-23", QuarterFetcher.Waiting, "2026-09-30", QuarterFetcher.NotYetPosted, "0", "10", "3", "null"],
                ["2026-10-26", QuarterFetcher.Waiting, "2026-09-30", QuarterFetcher.NotYetPosted, "0", "10", "4", "null"],
                ["2026-10-27", QuarterFetcher.Waiting, "2026-09-30", QuarterFetcher.NotYetPosted, "0", "10", "5", "null"],
                ["2026-10-28", QuarterFetcher.Waiting, "2026-09-30", QuarterFetcher.NotYetPosted, "0", "10", "6", "2026-11-04"],
                ["2026-11-04", QuarterFetcher.Waiting, "2026-09-30", QuarterFetcher.NotYetPosted, "0", "10", "7", "2026-11-11"],
                ["2026-11-11", QuarterFetcher.Waiting, "2026-09-30", QuarterFetcher.Stored, "8", "11", "8", "null"],
            ],
            rows);

        Assert.Equal(ProviderWeights.Fundamentals, int.Parse(rows[0][5], CultureInfo.InvariantCulture));
        Assert.Equal(ProviderWeights.Fundamentals + ProviderWeights.HistoricalPerTicker, QuarterFetcher.WeightOfAnAsk);

        // And the requests the feeds counted are the asks and the one request for closes a stored ask
        // makes, the fill's and the last night's.
        Assert.Equal((1 + 8, 2), (fundamentals.Requests, closes.Requests));

        // The weights are the ones RUNBOOK states.
        var runbook = Corpus.Read("docs/RUNBOOK.md");

        Assert.Contains($"the quarters step's ask costs {QuarterFetcher.WeightOfAnAsk} where it stores a quarter", runbook, StringComparison.Ordinal);
        Assert.Contains($"and {ProviderWeights.Fundamentals} where the answer does not yet carry the quarter awaited", runbook, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheFillAsksAtMostItsCountANightInTickerOrderAfterAJoinerAndTheRestOnTheNextNight()
    {
        var tickers = Enumerable.Range(0, 300).Select(at => FormattableString.Invariant($"M{at:D3}")).ToArray();

        using var store = QuarterMembers(tickers);

        var fundamentals = new PostedFundamentals();
        var closes = new OneClose();

        foreach (var ticker in tickers)
        {
            fundamentals.Post(ticker, new DateOnly(2026, 6, 30));
        }

        // The first night asks the fill's count, the first by ticker.
        var first = await QuartersNight(store, fundamentals, closes, new DateOnly(2026, 10, 1));

        Assert.Equal(260, QuarterFetcher.FillPerNight);
        Assert.Equal(tickers.Take(QuarterFetcher.FillPerNight), first.Asked.Select(ask => ask.Ticker));
        Assert.All(first.Asked, ask => Assert.Equal(QuarterFetcher.Fill, ask.Reason));
        Assert.Equal(tickers.Length - QuarterFetcher.FillPerNight, first.FillOwed);

        // A member joining the index the next night is asked first, as a joiner, and then the rest of
        // the fill.
        store.Execute($"INSERT INTO membership (index_code, ticker, joined, \"left\", observed_at) VALUES ('{Index}', 'JOIN', '2026-10-02', NULL, '2026-10-01T23:30:00Z');");
        fundamentals.Post("JOIN", new DateOnly(2026, 6, 30));

        var second = await QuartersNight(store, fundamentals, closes, new DateOnly(2026, 10, 2));

        Assert.Equal(["JOIN", .. tickers.Skip(QuarterFetcher.FillPerNight)], second.Asked.Select(ask => ask.Ticker));
        Assert.Equal(QuarterFetcher.Joined, second.Asked[0].Reason);
        Assert.Equal(0, second.FillOwed);

        // And a night with no report due and no fill owed asks nothing and makes no request.
        var requests = fundamentals.Requests + closes.Requests;
        var quiet = await QuartersNight(store, fundamentals, closes, new DateOnly(2026, 10, 5));

        Assert.Empty(quiet.Asked);
        Assert.Equal((0, requests), (quiet.Requests, fundamentals.Requests + closes.Requests));
    }

    [Fact]
    public async Task TheQuartersVerbRunsTheStepByHandAndARunAgainTheSameDayAsksTheNextOfTheFill()
    {
        // The verb the operator runs, over feeds the test holds: the first run asks the fill's count by
        // ticker, a second the same day asks none the first asked and the rest of the fill, a third asks
        // nothing, and each run's row is under the prefix the run page reads as by hand.
        var tickers = Enumerable.Range(0, 300).Select(at => FormattableString.Invariant($"M{at:D3}")).ToArray();

        using var store = QuarterMembers(tickers);

        var fundamentals = new PostedFundamentals();
        var closes = new OneClose();

        foreach (var ticker in tickers)
        {
            fundamentals.Post(ticker, new DateOnly(2026, 6, 30));
        }

        var feeds = EquityBrief.Worker.NightFeeds.FromFixture(FixtureFolder()) with { Fundamentals = fundamentals, Historical = closes };

        async Task<string> ByHand(int minute)
        {
            var output = new StringWriter();
            var error = new StringWriter();
            var code = await QuarterFetcher.RunAsync(
                [],
                () => feeds,
                FixedClock.At(new DateTimeOffset(2026, 9, 27, 17, minute, 0, TimeSpan.Zero), SessionZones.UnitedStates),
                store.DatabaseFile,
                output,
                error);

            Assert.True(code == 0, error.ToString());

            return output.ToString();
        }

        IReadOnlyList<string> Asked()
        {
            using var connection = store.Open();
            using var command = connection.CreateCommand();

            command.CommandText = "SELECT ticker || '|' || session_date || '|' || reason FROM quarter_ask ORDER BY ticker;";

            var rows = new List<string>();
            using var reader = command.ExecuteReader();

            while (reader.Read())
            {
                rows.Add(reader.GetString(0));
            }

            return rows;
        }

        Assert.StartsWith($"quarters: {QuarterFetcher.FillPerNight} of 300 member(s) due asked", await ByHand(30), StringComparison.Ordinal);
        Assert.Equal(tickers.Take(QuarterFetcher.FillPerNight).Select(ticker => $"{ticker}|2026-09-27|{QuarterFetcher.Fill}"), Asked());

        Assert.StartsWith("quarters: 40 of 40 member(s) due asked", await ByHand(45), StringComparison.Ordinal);
        Assert.Equal(tickers.Select(ticker => $"{ticker}|2026-09-27|{QuarterFetcher.Fill}"), Asked());
        Assert.Equal(300, fundamentals.Requests);

        Assert.StartsWith("quarters: 0 of 0 member(s) due asked", await ByHand(50), StringComparison.Ordinal);
        Assert.Equal(300, fundamentals.Requests);

        // Every run's row is the step's, under the by-hand prefix the run page leaves out of its nights.
        using (var connection = store.Open())
        using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT run_id, stage FROM run_log ORDER BY rowid;";

            using var reader = command.ExecuteReader();
            var runs = 0;

            while (reader.Read())
            {
                runs++;
                Assert.StartsWith(QuarterFetcher.ByHandPrefix, reader.GetString(0), StringComparison.Ordinal);
                Assert.Equal(QuarterFetcher.Stage, reader.GetString(1));
            }

            Assert.Equal(3, runs);
        }

        Assert.Contains(QuarterFetcher.ByHandPrefix, EquityBrief.Api.Reading.RunScreen.RunsByHand);
        Assert.True(EquityBrief.Api.Reading.RunScreen.IsByHand(QuarterFetcher.ByHandPrefix + "20260927T173000.0000000Z"));

        // A source that cannot be resolved is refused by name and writes nothing.
        var refusedOutput = new StringWriter();
        var refusedError = new StringWriter();
        var refused = await QuarterFetcher.RunAsync(
            [],
            () => throw new InvalidOperationException("no key is configured"),
            FixedClock.At(new DateTimeOffset(2026, 9, 27, 18, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates),
            store.DatabaseFile,
            refusedOutput,
            refusedError);

        Assert.Equal(1, refused);
        Assert.Equal("quarters: no key is configured", refusedError.ToString().Trim());
        Assert.Equal(300, Asked().Count);

        // The runbook names the verb as a person types it.
        Assert.Contains("dotnet run --project src/EquityBrief.Worker -- quarters --live", Corpus.Read("docs/RUNBOOK.md"), StringComparison.Ordinal);
    }

    // A feed noting what a run had written by the time of its first request.
    sealed class FirstRequestNoted(IFundamentalsFeed inner, Func<string> written) : IFundamentalsFeed
    {
        public string? AtTheFirst { get; private set; }

        public int Requests => inner.Requests;

        public Task<CompanyFundamentals> FundamentalsAsync(string ticker, CancellationToken cancellation = default)
        {
            AtTheFirst ??= written();

            return inner.FundamentalsAsync(ticker, cancellation);
        }
    }

    // A fetch's quarter as the store holds it, read for its interest expense or not.
    static void Fetched(TemporaryStore store, string ticker, string fetchedAt, string periodEnd, int interestRead) =>
        store.Execute(
            "INSERT INTO reported_quarter (ticker, fetched_at, session_date, period_end, filing_date, net_income, operating_income, interest_expense, interest_read) VALUES " +
            $"('{ticker}', '{fetchedAt}', '{fetchedAt[..10]}', '{periodEnd}', '2026-07-30', '100', '150', {(interestRead == 1 ? "'10'" : "NULL")}, {interestRead});");

    // see: A member's reported quarters are fetched on the night after it reports, and asked for again on the five nights after and weekly after that until the quarter is posted
    [Fact]
    public async Task TheRefetchAsksEachMemberWhoseNewestFetchReadNoInterestExpenseStatingItsAsksBeforeTheFirstRequest()
    {
        // UA and UB fetched before the interest expense was stored; RA's newest fetch read it though an older one did not;
        // RB read it; NQ holds no quarter at all; and AS read none but was asked already on the session the runs fall on.
        using var store = QuarterMembers("AS", "NQ", "RA", "RB", "UA", "UB");

        Fetched(store, "UA", "2026-10-04T04:33:00Z", "2026-06-30", 0);
        Fetched(store, "UB", "2026-10-04T04:34:00Z", "2026-06-30", 0);
        Fetched(store, "RA", "2026-10-04T04:35:00Z", "2026-06-30", 0);
        Fetched(store, "RA", "2026-10-07T23:56:00Z", "2026-06-30", 1);
        Fetched(store, "RB", "2026-10-07T23:57:00Z", "2026-06-30", 1);
        Fetched(store, "AS", "2026-10-04T04:36:00Z", "2026-06-30", 0);
        store.Execute(
            "INSERT INTO quarter_ask (ticker, session_date, asked_at, reason, awaited, outcome, quarters, weighted, nights, next_ask, detail) VALUES " +
            "('AS', '2026-10-10', '2026-10-10T13:00:00Z', 'fill', NULL, 'refused', 0, 10, 1, NULL, 'The provider refused AS.');");

        var posted = new PostedFundamentals();
        var closes = new OneClose();

        foreach (var ticker in new[] { "AS", "NQ", "RA", "RB", "UA", "UB" })
        {
            posted.Post(ticker, new DateOnly(2026, 6, 30));
        }

        var output = new StringWriter();
        var fundamentals = new FirstRequestNoted(posted, output.ToString);
        var feeds = EquityBrief.Worker.NightFeeds.FromFixture(FixtureFolder()) with { Fundamentals = fundamentals, Historical = closes };

        async Task<int> ByHand(int minute, params string[] args)
        {
            var error = new StringWriter();

            return await QuarterFetcher.RunAsync(
                ["quarters", .. args],
                () => feeds,
                FixedClock.At(new DateTimeOffset(2026, 10, 10, 14, minute, 0, TimeSpan.Zero), SessionZones.UnitedStates),
                store.DatabaseFile,
                output,
                error) is var code && code == 0 ? 0 : throw new InvalidOperationException(error.ToString());
        }

        IReadOnlyList<string> AskedOn(string session)
        {
            using var connection = store.Open();
            using var command = connection.CreateCommand();

            command.CommandText = "SELECT ticker || '|' || reason || '|' || outcome || '|' || weighted FROM quarter_ask WHERE session_date = $s ORDER BY ticker;";
            command.Parameters.AddWithValue("$s", session);

            var rows = new List<string>();
            using var reader = command.ExecuteReader();

            while (reader.Read())
            {
                rows.Add(reader.GetString(0));
            }

            return rows;
        }

        // Capped at one ask, the run asks UA alone, the first of the two read none by ticker, after saying so: the line was
        // written before the first request, with the eleven weighted calls the ask may spend.
        await ByHand(0, "--refetch-unread", "--most", "1", "--live");

        var stated = $"quarters: 1 ask(s) of the 2 member(s) whose newest fetch read no interest expense, at most {QuarterFetcher.WeightOfAnAsk} weighted call(s)";

        Assert.Equal(stated, fundamentals.AtTheFirst?.Trim());
        Assert.Equal($"UA|{QuarterFetcher.Fill}|{QuarterFetcher.Stored}|{QuarterFetcher.WeightOfAnAsk}", Assert.Single(AskedOn("2026-10-10"), row => !row.StartsWith("AS|", StringComparison.Ordinal)));
        Assert.Equal((1, 1), (posted.Requests, closes.Requests));

        // The refetch stores the answer's quarters read for their interest expense, so UA is not asked again; the next
        // run asks UB alone, and one after that asks nothing and makes no request.
        await ByHand(10, "--refetch-unread", "--live");

        Assert.Equal(
            ["AS|fill|refused|10", $"UA|fill|stored|{QuarterFetcher.WeightOfAnAsk}", $"UB|fill|stored|{QuarterFetcher.WeightOfAnAsk}"],
            AskedOn("2026-10-10"));

        await ByHand(20, "--refetch-unread", "--live");

        Assert.Equal((2, 2), (posted.Requests, closes.Requests));
        Assert.EndsWith(
            $"quarters: 0 ask(s) of the 0 member(s) whose newest fetch read no interest expense, at most 0 weighted call(s){Environment.NewLine}quarters: 0 of 0 member(s) due asked: 0 reporting, 0 waiting, 0 joining, 0 filled; 0 stored, 0 not yet posted, 0 returning nothing, 0 refused; 0 quarter row(s), 0 weighted call(s); 0 member(s) of the fill still owed{Environment.NewLine}",
            output.ToString(),
            StringComparison.Ordinal);

        // A cap that is no whole number above nothing, and the two runs asked together, are refused with nothing asked.
        foreach (var refused in new[] { new[] { "--refetch-unread", "--most", "0" }, ["--refetch-unread", "--most", "x"], ["--refetch-unread", "--companies"] })
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => ByHand(30, refused));
        }

        Assert.Equal((2, 2), (posted.Requests, closes.Requests));

        // The runbook names the run as a person types it.
        Assert.Contains("dotnet run --project src/EquityBrief.Worker -- quarters --refetch-unread --most 454 --live", Corpus.Read("docs/RUNBOOK.md"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AMemberTheProviderReturnsNoQuarterForIsMarkedAbsentAndAskedOnTheSameSchedule()
    {
        using var store = QuarterMembers("ZZZZ");

        var fundamentals = new PostedFundamentals();
        var closes = new OneClose();
        var sessions = SessionsFrom(new DateOnly(2026, 10, 1), 12);
        var asks = new List<int>();

        foreach (var session in sessions)
        {
            asks.Add((await QuartersNight(store, fundamentals, closes, session)).Asked.Count);
        }

        // The fill's ask and the five after it, then the first night seven days after the sixth.
        Assert.Equal([1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 1, 0], asks);
        Assert.All(AskRows(store, "ZZZZ"), row => Assert.Equal(QuarterFetcher.NothingReturned, row[3]));
        Assert.Equal(0, closes.Requests);

        // A report since its last ask asks it on the night after, whatever the week says.
        Reports(store, "ZZZZ", "2026-10-20", "2026-09-30");

        var reported = await QuartersNight(store, fundamentals, closes, new DateOnly(2026, 10, 21));

        Assert.Equal(QuarterFetcher.Report, Assert.Single(reported.Asked).Reason);
    }

    [Fact]
    public async Task ARefusedAskIsRecordedWithItsReasonAndTheNextMemberIsStillAsked()
    {
        using var store = QuarterMembers("AAAA", "BBBB");

        var fundamentals = new PostedFundamentals();
        var closes = new OneClose();

        fundamentals.Refuse("AAAA");
        fundamentals.Post("BBBB", new DateOnly(2026, 6, 30));

        var night = await QuartersNight(store, fundamentals, closes, new DateOnly(2026, 10, 1));

        Assert.Equal(
            [("AAAA", QuarterFetcher.Refused), ("BBBB", QuarterFetcher.Stored)],
            night.Asked.Select(ask => (ask.Ticker, ask.Outcome)));
        Assert.Equal("The provider refused AAAA.", night.Asked[0].Detail);
        Assert.Contains("1 refused", QuarterFetcher.Detail(night), StringComparison.Ordinal);

        // The refused member is asked again on the next night, and the stored one is not.
        var next = await QuartersNight(store, fundamentals, closes, new DateOnly(2026, 10, 2));

        Assert.Equal(("AAAA", QuarterFetcher.Waiting), (Assert.Single(next.Asked).Ticker, next.Asked[0].Reason));
    }

    [Fact]
    public async Task AStepAtItsLimitStartsNoAskAndOneAtTheAllowanceMakesNoneAndTheNextNightAsksThem()
    {
        Assert.Equal(TimeSpan.FromMinutes(15), QuarterFetcher.Limit);

        using var store = QuarterMembers("AAAA", "BBBB");

        var fundamentals = new PostedFundamentals();
        var closes = new OneClose();

        fundamentals.Post("AAAA", new DateOnly(2026, 6, 30));
        fundamentals.Post("BBBB", new DateOnly(2026, 6, 30));

        // A limit already spent: no ask starts, and both are counted as left at it.
        var limited = await QuartersNight(store, fundamentals, closes, new DateOnly(2026, 10, 1), limit: TimeSpan.Zero);

        Assert.Empty(limited.Asked);
        Assert.Equal(2, limited.LeftAtTheLimit);
        Assert.EndsWith("2 left at the step's limit", QuarterFetcher.Detail(limited), StringComparison.Ordinal);

        // A night one weighted call short of an ask's cost: no ask is made, and both are left at it.
        var spent = await QuartersNight(store, fundamentals, closes, new DateOnly(2026, 10, 2), weightedSoFar: ProviderWeights.DailyAllowance - QuarterFetcher.WeightOfAnAsk + 1);

        Assert.Empty(spent.Asked);
        Assert.Equal(2, spent.LeftAtTheAllowance);
        Assert.EndsWith("2 left at the day's allowance", QuarterFetcher.Detail(spent), StringComparison.Ordinal);
        Assert.Equal(0, fundamentals.Requests);

        // Exactly an ask's cost below the allowance leaves room for one, and the ask it made takes the night
        // to the allowance, so the second waits for the next night.
        var room = await QuartersNight(store, fundamentals, closes, new DateOnly(2026, 10, 5), weightedSoFar: ProviderWeights.DailyAllowance - QuarterFetcher.WeightOfAnAsk);

        Assert.Equal(["AAAA"], room.Asked.Select(ask => ask.Ticker));
        Assert.Equal((1, QuarterFetcher.WeightOfAnAsk), (room.LeftAtTheAllowance, room.Weighted));

        var next = await QuartersNight(store, fundamentals, closes, new DateOnly(2026, 10, 6));

        Assert.Equal(["BBBB"], next.Asked.Select(ask => ask.Ticker));
    }
}
