using EquityBrief.Core.Providers;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Bars;
using EquityBrief.Worker.Membership;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Tests.Bars;

// 1.2's done condition, run through the backfill's own surface.
//
// The population is stated wherever a figure is: the fixture carries five
// constituents of which three are current members and two have left, and every
// count below is over those. A claim about roughly five hundred live names is
// not something this suite can assert, and saying so is the point.
//
// Every expectation here is derived from the captured series or from the
// exchange calendar. The fixture held a seeded random walk until 1.2 replaced
// it, and each of these figures was different against that walk: a generator
// that emits a bar for every weekday produces a year the market never trades.
// An expectation carried across from generated data is an expectation about the
// generator.
public class BackfillTests
{
    const string Index = "GSPC";
    const string Fixture = "membership-2026-09-05";

    static string FixtureFolder() => Path.Combine(Repository.Root, "fixtures", Fixture);

    // The fixture date, so the year the backfill asks for ends where the
    // captured series ends rather than wherever this machine is today.
    static readonly DateTimeOffset Instant = new(2026, 9, 5, 21, 10, 0, TimeSpan.Zero);

    // The window the backfill asks for at that instant: the year ending on the
    // session date. The capture reaches one session further back, so the window
    // filter has something to exclude and is exercised rather than assumed.
    static readonly DateOnly WindowFrom = new(2025, 9, 5);
    static readonly DateOnly WindowTo = new(2026, 9, 4);

    // The days the exchange did not trade inside that window.
    //
    // This is the independently derived expectation 1.2 owes. Counting the rows
    // the capture happens to hold and asserting that number back is regression
    // detection wearing verification's clothes; naming the closures and deriving
    // the session count from them is a statement about the market that the
    // capture is then checked against. If the provider silently drops a session,
    // a frozen count of 252 still passes and this does not.
    //
    // It is also the fact 1.5 turns on. A gap is a session the exchange traded
    // and the store does not hold, so the detection rule cannot be "a weekday
    // with no bar": these nine weekdays are missing from every clean series
    // there is, and a rule that counted weekdays would have raised nine false
    // gaps on the first real night while passing over the generated fixture,
    // which had no holidays in it at all
    // (see: A gap is a session the exchange traded and the store does not hold).
    static readonly DateOnly[] Closures =
    [
        new(2025, 11, 27), // Thanksgiving
        new(2025, 12, 25), // Christmas Day
        new(2026, 1, 1),   // New Year's Day
        new(2026, 1, 19),  // Martin Luther King Jr Day
        new(2026, 2, 16),  // Washington's Birthday
        new(2026, 4, 3),   // Good Friday
        new(2026, 5, 25),  // Memorial Day
        new(2026, 6, 19),  // Juneteenth
        new(2026, 7, 3),   // Independence Day, observed on the Friday
    ];

    // Weekdays in the window, less the closures. Derived here rather than
    // written down, so the two halves of the arithmetic cannot drift apart.
    static IReadOnlyList<string> TradingSessions()
    {
        var sessions = new List<string>();

        for (var day = WindowFrom; day <= WindowTo; day = day.AddDays(1))
        {
            if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday || Closures.Contains(day))
            {
                continue;
            }

            sessions.Add(day.ToString("yyyy-MM-dd"));
        }

        return sessions;
    }

    // 261 weekdays less 9 closures. Stated so the derivation above is legible
    // as a number and a reader can check it without running anything.
    const int SessionsInTheYear = 252;
    const int CurrentMembers = 3;
    const int RowsPerRun = SessionsInTheYear * CurrentMembers;

    static IClock Clock() => FixedClock.At(Instant, SessionZones.UnitedStates);

    static async Task<TemporaryStore> WithMembership()
    {
        var store = new TemporaryStore().Migrated();
        var feed = RecordedIndexMembershipFeed.FromFile(Path.Combine(FixtureFolder(), "index-constituents.json"));

        await new MembershipLoader(feed, Clock(), store.DatabaseFile).LoadAsync(Index, "run-0");

        return store;
    }

    static Backfill Loader(TemporaryStore store, out RecordedHistoricalBarFeed feed)
    {
        feed = RecordedHistoricalBarFeed.FromFolder(FixtureFolder());

        return new Backfill(feed, Clock(), store.DatabaseFile);
    }

    [Fact]
    public void TheDerivedYearIsTheOneTheArithmeticSaysItIs()
    {
        // The derivation checked against its own stated total before any of it
        // is used as an expectation, so a mistyped closure fails here with the
        // arithmetic in view rather than downstream as a row count nobody can
        // trace back.
        var weekdays = 0;

        for (var day = WindowFrom; day <= WindowTo; day = day.AddDays(1))
        {
            if (day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            {
                weekdays++;
            }
        }

        Assert.Equal(261, weekdays);
        Assert.Equal(9, Closures.Length);
        Assert.All(Closures, closure => Assert.True(
            closure.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday),
            $"{closure:yyyy-MM-dd} is a weekend, so naming it as a closure removes nothing."));

        Assert.Equal(SessionsInTheYear, weekdays - Closures.Length);
        Assert.Equal(SessionsInTheYear, TradingSessions().Count);
    }

    [Fact]
    public async Task EveryCurrentMemberHoldsAFullYearAndTheNamesThatLeftAreNotFetched()
    {
        using var store = await WithMembership();
        var backfill = Loader(store, out var feed);

        var outcome = await backfill.RunAsync(Index, "run-1");

        // Three current members and two departed names. The departed ones keep
        // the history they have, which here is none, and are not fetched: a
        // backfill is owed for names in the index, not for every name ever in
        // it.
        Assert.Equal(CurrentMembers, outcome.Members);
        Assert.Equal(CurrentMembers, outcome.Owed);
        Assert.Equal(CurrentMembers, outcome.Requests);
        Assert.Equal(CurrentMembers, feed.Requests);

        var tickers = Column(store, "SELECT DISTINCT ticker FROM bar ORDER BY ticker;");

        Assert.Equal(["AAPL", "KEYS", "MSFT"], tickers);

        // Both departed names, named rather than covered by the equality above.
        // The equality already fails if either appears, but naming them says
        // which absence is the property: a backfill is owed for names in the
        // index, and neither of these is in it.
        Assert.DoesNotContain("XRAY", tickers);
        Assert.DoesNotContain("AAL", tickers);

        // Per name rather than in total, so a name short of its year is visible
        // instead of being covered by another name's surplus, and against the
        // derived session list rather than against a count, so a series holding
        // the right number of the wrong days fails.
        var expected = TradingSessions();

        foreach (var ticker in tickers)
        {
            var sessions = Column(store, $"SELECT session_date FROM bar WHERE ticker = '{ticker}' ORDER BY session_date;");

            Assert.Equal(expected, sessions);
        }
    }

    [Fact]
    public async Task TheCaptureReachesFurtherBackThanTheWindowAndTheSurplusIsNotStored()
    {
        // The window filter, exercised rather than assumed. The capture starts
        // one session before the window opens, so a backfill that stored what
        // the provider sent rather than what it asked for would hold that row.
        var captured = RecordedHistoricalBarFeed.Parse(
            await File.ReadAllTextAsync(Path.Combine(FixtureFolder(), "bars-AAPL.json")), "AAPL");

        Assert.Equal(SessionsInTheYear + 1, captured.Count);
        Assert.Equal(new DateOnly(2025, 9, 4), captured[0].SessionDate);

        using var store = await WithMembership();
        var backfill = Loader(store, out _);

        await backfill.RunAsync(Index, "run-1");

        var held = Column(store, "SELECT session_date FROM bar WHERE ticker = 'AAPL' ORDER BY session_date;");

        Assert.DoesNotContain("2025-09-04", held);
        Assert.Equal(WindowFrom.ToString("yyyy-MM-dd"), held[0]);
        Assert.Equal(WindowTo.ToString("yyyy-MM-dd"), held[^1]);
    }

    [Fact]
    public void TheThreeNamesShareOneSessionSet()
    {
        // A provider-side property the store cannot show. Three names on one
        // exchange trade on the same days, so a name whose captured series is
        // thin is a fixture fault rather than a market fact, and a fixture meant
        // to be clean is the wrong place to discover one.
        var sets = new[] { "AAPL", "MSFT", "KEYS" }
            .Select(ticker => RecordedHistoricalBarFeed
                .Parse(File.ReadAllText(Path.Combine(FixtureFolder(), $"bars-{ticker}.json")), ticker)
                .Select(bar => bar.SessionDate.ToString("yyyy-MM-dd"))
                .ToArray())
            .ToArray();

        Assert.Equal(sets[0], sets[1]);
        Assert.Equal(sets[0], sets[2]);
    }

    [Fact]
    public async Task ASecondRunBackfillsNothingAndSpendsNoRequest()
    {
        using var store = await WithMembership();
        var backfill = Loader(store, out var feed);

        var first = await backfill.RunAsync(Index, "run-1");
        var before = Column(store, "SELECT ticker || ' ' || session_date FROM bar ORDER BY ticker, session_date;");

        var second = await backfill.RunAsync(Index, "run-2");
        var after = Column(store, "SELECT ticker || ' ' || session_date FROM bar ORDER BY ticker, session_date;");

        // Never repeated for a name that already holds its year. Not merely
        // harmless when repeated: no request is made at all, which is the whole
        // reason the rule exists rather than relying on the insert conflicting.
        Assert.Equal(CurrentMembers, first.Requests);
        Assert.Equal(0, second.Owed);
        Assert.Equal(0, second.Requests);
        Assert.Equal(0, second.RowsWritten);
        Assert.Equal(CurrentMembers, feed.Requests);

        Assert.Equal(before, after);
        Assert.Equal(RowsPerRun, before.Count);
    }

    [Fact]
    public async Task TheRequestCountInTheRunLogEqualsTheNamesLackingHistory()
    {
        using var store = await WithMembership();
        var backfill = Loader(store, out _);

        await backfill.RunAsync(Index, "run-1");
        await backfill.RunAsync(Index, "run-2");

        // The done condition, read off the store rather than off the return
        // value, because the run log is the surface the operator reads it on.
        var logged = Rows(store, "SELECT run_id, CAST(network_requests AS TEXT) FROM run_log WHERE stage = 'backfill' ORDER BY run_id;");

        Assert.Equal([("run-1", $"{CurrentMembers}"), ("run-2", "0")], logged);

        var written = Rows(store, "SELECT run_id, CAST(rows_written AS TEXT) FROM run_log WHERE stage = 'backfill' ORDER BY run_id;");

        Assert.Equal([("run-1", $"{RowsPerRun}"), ("run-2", "0")], written);

        // And the backfill costs no model call, like everything on the nightly
        // path.
        var free = Column(store, "SELECT DISTINCT CAST(model_calls AS TEXT) FROM run_log;");

        Assert.Equal("0", Assert.Single(free));
    }

    [Fact]
    public async Task TheStoredCloseIsTheAdjustedOneAndTheCapturedSeriesShowsTheDifference()
    {
        // Until 1.2 this could only be asserted over a payload written for the
        // test, because the generated fixture set adjusted_close equal to close
        // on every row. A parser reading the wrong field passed the fixture and
        // failed only the one synthetic case.
        //
        // The captured series carries the difference on most rows of two names
        // and on none of the third, so the claim is now load-bearing where it
        // matters and the fixture also holds the case where the two agree.
        using var store = await WithMembership();
        var backfill = Loader(store, out _);

        await backfill.RunAsync(Index, "run-1");

        // AAPL's first stored session. The capture reads
        //   "open":240, "close":239.69, "adjusted_close":238.8078
        // so a parser taking close would store 239.69 here.
        var first = Column(store, "SELECT close FROM bar WHERE ticker = 'AAPL' AND session_date = '2025-09-05';");

        Assert.Equal("238.8078", Assert.Single(first));

        // Counted over the window, per name, because the population differs by
        // name and a total would hide the one that is zero. KEYS pays no
        // dividend and split in the window, so its adjusted series is its close
        // series, which is the case a fixture of only adjusted names would miss.
        var divergent = new Dictionary<string, int>
        {
            ["AAPL"] = 232,
            ["MSFT"] = 240,
            ["KEYS"] = 0,
        };

        foreach (var (ticker, expected) in divergent)
        {
            var bars = RecordedHistoricalBarFeed
                .Parse(File.ReadAllText(Path.Combine(FixtureFolder(), $"bars-{ticker}.json")), ticker)
                .Where(bar => bar.SessionDate >= WindowFrom && bar.SessionDate <= WindowTo)
                .ToArray();

            var raw = RawCloses(ticker);
            var differs = bars.Count(bar => raw[bar.SessionDate] != bar.Close);

            Assert.Equal(SessionsInTheYear, bars.Length);
            Assert.Equal(expected, differs);
        }
    }

    [Fact]
    public async Task PricesAreStoredAsTextAndReadBackAsDecimal()
    {
        using var store = await WithMembership();
        var backfill = Loader(store, out _);

        await backfill.RunAsync(Index, "run-1");

        var types = Column(store, "SELECT DISTINCT typeof(open) || ' ' || typeof(close) || ' ' || typeof(volume) || ' ' || typeof(raw_close) FROM bar;");

        // TEXT for the money columns and INTEGER for the count, which is the
        // storage half of the money rule holding in a populated store rather
        // than only in the migration text price-storage-form reads.
        Assert.Equal("text text integer text", Assert.Single(types));

        // Two shapes the generated fixture never produced, because it wrote
        // every price as a two-decimal string. The provider sends JSON numbers,
        // renders a whole number with no decimal part at all, and carries four
        // decimal places on an adjusted close. All three reach storage through
        // the money helper, and a route through double would round the last of
        // them away.
        var row = Rows(store, "SELECT open, close FROM bar WHERE ticker = 'AAPL' AND session_date = '2025-09-05';");
        var (open, close) = Assert.Single(row);

        // The capture reads open 240, close 239.69, adjusted_close 238.8078.
        // All four prices are adjusted by the same factor, so the stored open is
        // 240 * 238.8078 / 239.69 and not the 240 the payload carries: three raw
        // prices beside one adjusted one is a bar that could not have traded.
        Assert.Equal("239.1167", open);
        Assert.Equal("238.8078", close);
        Assert.Equal(239.1167m, EquityBrief.Data.Money.FromStorage(open));
        Assert.Equal(238.8078m, EquityBrief.Data.Money.FromStorage(close));

        // And the fourth decimal is genuinely held rather than rendered back by
        // chance: truncating to two would give a different value here.
        Assert.NotEqual(238.81m, EquityBrief.Data.Money.FromStorage(close));

        // The raw close is kept beside the adjusted set, because it is the input
        // to the factor and a store holding only the output cannot audit it.
        var raw = Column(store, "SELECT raw_close FROM bar WHERE ticker = 'AAPL' AND session_date = '2025-09-05';");

        Assert.Equal("239.69", Assert.Single(raw));
    }

    [Fact]
    public async Task ANameTheFeedHasNoCaptureForFailsRatherThanReadingAsDone()
    {
        // A recorded feed that answered an unknown ticker with an empty series
        // would look exactly like a name the provider has no history for, and
        // the backfill would record it as done and never ask again.
        using var store = await WithMembership();

        using var connection = new SqliteConnection($"Data Source={store.DatabaseFile}");
        connection.Open();
        using var insert = connection.CreateCommand();
        insert.CommandText =
            @"INSERT INTO membership (index_code, ticker, joined, ""left"", observed_at)
              VALUES ($i, 'NOPE', '2020-01-02', NULL, '2026-09-05T21:10:00Z');";
        insert.Parameters.AddWithValue("$i", Index);
        insert.ExecuteNonQuery();

        var backfill = Loader(store, out _);

        var refusal = await Assert.ThrowsAsync<InvalidOperationException>(
            () => backfill.RunAsync(Index, "run-1"));

        Assert.Contains("NOPE", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheRecordedFeedTakesTheAdjustedCloseAndRefusesWhatItCannotRead()
    {
        // Written with JSON numbers, which is the shape the provider sends. It
        // read strings until 1.2, matching a fixture the generator had written
        // as strings, so the number path was never exercised anywhere.
        Assert.Equal(5.25m, Assert.Single(RecordedHistoricalBarFeed.Parse(
            """[{"date":"2026-09-04","open":10,"high":11.00,"low":9,"close":10.50,"adjusted_close":5.25,"volume":1000}]""",
            "TEST")).Close);

        // A string is still accepted, because a provider that quotes its numbers
        // is sending a price and not a fault.
        Assert.Equal(5.25m, Assert.Single(RecordedHistoricalBarFeed.Parse(
            """[{"date":"2026-09-04","open":"10.00","high":"11.00","low":"9.00","close":"10.50","adjusted_close":"5.25","volume":1000}]""",
            "TEST")).Close);

        Assert.Throws<FormatException>(() => RecordedHistoricalBarFeed.Parse("{}", "TEST"));
        Assert.Throws<FormatException>(() => RecordedHistoricalBarFeed.Parse(
            """[{"date":"04/09/2026","open":1,"high":1,"low":1,"close":1,"adjusted_close":1,"volume":1}]""",
            "TEST"));
        Assert.Throws<FormatException>(() => RecordedHistoricalBarFeed.Parse(
            """[{"date":"2026-09-04","open":1,"high":1,"low":1,"close":1,"volume":1}]""",
            "TEST"));

        // A null where a number is expected, which a live payload produces for a
        // session the provider holds no price for and a generator never emits.
        Assert.Throws<FormatException>(() => RecordedHistoricalBarFeed.Parse(
            """[{"date":"2026-09-04","open":1,"high":1,"low":1,"close":1,"adjusted_close":null,"volume":1}]""",
            "TEST"));
    }

    // The unadjusted closes, read straight from the captured file. The feed
    // deliberately does not expose them, so the comparison above reads the
    // payload rather than asking the parser for the field it is being tested
    // for not taking.
    static Dictionary<DateOnly, decimal> RawCloses(string ticker)
    {
        using var document = System.Text.Json.JsonDocument.Parse(
            File.ReadAllText(Path.Combine(FixtureFolder(), $"bars-{ticker}.json")));

        return document.RootElement.EnumerateArray().ToDictionary(
            entry => DateOnly.ParseExact(
                entry.GetProperty("date").GetString()!, "yyyy-MM-dd",
                System.Globalization.CultureInfo.InvariantCulture),
            entry => entry.GetProperty("close").GetDecimal());
    }

    static IReadOnlyList<string> Column(TemporaryStore store, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={store.DatabaseFile}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;

        var values = new List<string>();
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }

    static IReadOnlyList<(string, string)> Rows(TemporaryStore store, string sql)
    {
        using var connection = new SqliteConnection($"Data Source={store.DatabaseFile}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;

        var rows = new List<(string, string)>();
        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            rows.Add((reader.GetString(0), reader.GetString(1)));
        }

        return rows;
    }
}
