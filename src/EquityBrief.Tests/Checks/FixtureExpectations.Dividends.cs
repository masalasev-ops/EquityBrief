using System.Globalization;
using EquityBrief.Core.Providers;
using EquityBrief.Worker.Calendar;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 16.3: the night's dividend calendar ask, one request for each of the 21 sessions after the night
// whatever the index's size, each answer read for the index's own exchange alone, stored under a kind of its own for the
// members of every index the night reads, and replacing what the window held; a calendar that refuses names itself and
// stores nothing, and the earnings already stored stand.
// see: The night asks the dividend calendar for each of the next 21 sessions, one request a session
public partial class FixtureExpectations
{
    // A constructed answer in the captured answers' shape: a meta envelope and rows of a symbol and a date, every market
    // together.
    static string DividendAnswer(string session, params string[] symbols) =>
        $"{{\"meta\":{{\"total\":{symbols.Length},\"offset\":0,\"limit\":5000,\"date_eq\":\"{session}\"}},\"data\":[" +
        string.Join(",", symbols.Select(symbol => $"{{\"date\":\"{session}\",\"symbol\":\"{symbol}\"}}")) + "]}";

    [Fact]
    public void TheDividendWindowIsTheTwentyOneSessionsAfterTheNight()
    {
        // From the night of 2026-10-06: seven sessions from 10-07 to 10-15, five a week to 10-30, and 11-02 to 11-04, the
        // window 16.0's check asked for.
        var window = CalendarFetcher.DividendWindow(new DateOnly(2026, 10, 6));

        Assert.Equal(21, window.Count);
        Assert.Equal(21, CalendarFetcher.DividendSessions);
        Assert.Equal((new DateOnly(2026, 10, 7), new DateOnly(2026, 11, 4)), (window[0], window[^1]));
        Assert.DoesNotContain(new DateOnly(2026, 10, 10), window);
        Assert.Contains(new DateOnly(2026, 10, 12), window);
    }

    [Fact]
    public void ADividendAnswerIsReadForTheIndexsOwnExchangeOnTheSessionAskedFor()
    {
        var (rows, total) = RecordedDividendCalendarFeed.Parse(DividendAnswer("2026-10-07", "EIX.US", "1339.HK", "LEN-B.US", "DIOS.ST"), new DateOnly(2026, 10, 7));

        Assert.Equal(["EIX 2026-10-07", "LEN-B 2026-10-07"], rows.Select(row => FormattableString.Invariant($"{row.Ticker} {row.Date:yyyy-MM-dd}")));
        Assert.Equal(4, total);

        // A row dated another session is not this one's, and an answer with no rows under data cannot be read.
        Assert.Empty(RecordedDividendCalendarFeed.Parse(DividendAnswer("2026-10-08", "EIX.US"), new DateOnly(2026, 10, 7)).Rows);
        Assert.Throws<FormatException>(() => RecordedDividendCalendarFeed.Parse("{\"meta\":{}}", new DateOnly(2026, 10, 7)));
    }

    [Fact]
    public async Task TheNightStoresEachMembersExDividendDateOverTheWindowAndReplacesWhatTheWindowHeld()
    {
        using var store = new TemporaryStore().Migrated();

        // EIX on the S&P 500 and NYT on the S&P 400; LEN-B is a member of neither.
        store.Execute(
            "INSERT INTO membership (index_code, ticker, joined, \"left\", observed_at) VALUES " +
            "('GSPC', 'EIX', NULL, NULL, '2026-10-01T21:00:00Z'), ('MID', 'NYT', NULL, NULL, '2026-10-01T21:00:00Z');");

        // EIX's date stored on 10-09, inside the window and no longer filed, and on 12-01, past it.
        store.Execute(
            "INSERT INTO calendar (ticker, event_date, kind, timing, detail, observed_at) VALUES " +
            "('EIX', '2026-10-09', 'ex-dividend', 'unstated', '{}', '2026-10-05T23:40:00Z'), ('EIX', '2026-12-01', 'ex-dividend', 'unstated', '{}', '2026-10-05T23:40:00Z');");

        var dividends = RecordedDividendCalendarFeed.Of(new Dictionary<DateOnly, string>
        {
            [new DateOnly(2026, 10, 7)] = DividendAnswer("2026-10-07", "EIX.US", "NYT.US", "LEN-B.US", "1339.HK"),
            [new DateOnly(2026, 10, 8)] = DividendAnswer("2026-10-08", "EIX.US"),
        });
        var earnings = new RecordedEarningsCalendarFeed(string.Empty);

        var outcome = await new CalendarFetcher(earnings, new SweepClock(new DateTimeOffset(2026, 10, 6, 23, 40, 0, TimeSpan.Zero)), store.DatabaseFile, dividends)
            .RunAsync("GSPC", new DateOnly(2026, 10, 6), "night-dividends", wider: ["MID"]);

        Assert.Equal((21, 22, 3), (dividends.Requests, outcome.Requests, outcome.ExDividends));
        Assert.Null(outcome.DividendsFault);
        Assert.Equal(
            ["EIX 2026-10-07", "EIX 2026-10-08", "EIX 2026-12-01", "NYT 2026-10-07"],
            Rows(store, "SELECT ticker || ' ' || event_date FROM calendar WHERE kind = 'ex-dividend' ORDER BY ticker, event_date;"));
        Assert.Contains("3 ex-dividend date(s) over the next 21 sessions", Rows(store, "SELECT detail FROM run_log WHERE stage = 'calendar';").Single(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheDividendAskCostsTwentyOneRequestsWhateverTheIndexsSize()
    {
        async Task<int> Asked(int members)
        {
            using var store = new TemporaryStore().Migrated();

            store.Execute(
                "INSERT INTO membership (index_code, ticker, joined, \"left\", observed_at) VALUES " +
                string.Join(", ", Enumerable.Range(0, members).Select(at => FormattableString.Invariant($"('GSPC', 'M{at}', NULL, NULL, '2026-10-01T21:00:00Z')"))) + ";");

            var dividends = RecordedDividendCalendarFeed.Of(new Dictionary<DateOnly, string>
            {
                [new DateOnly(2026, 10, 7)] = DividendAnswer("2026-10-07", [.. Enumerable.Range(0, members).Select(at => FormattableString.Invariant($"M{at}.US"))]),
            });

            await new CalendarFetcher(new RecordedEarningsCalendarFeed(string.Empty), new SweepClock(new DateTimeOffset(2026, 10, 6, 23, 40, 0, TimeSpan.Zero)), store.DatabaseFile, dividends)
                .RunAsync("GSPC", new DateOnly(2026, 10, 6), "night-cost");

            return dividends.Requests;
        }

        Assert.Equal((21, 21), (await Asked(50), await Asked(500)));
    }

    [Fact]
    public async Task ADividendCalendarThatRefusesStoresNothingAndIsNamedWhileTheEarningsStand()
    {
        using var store = new TemporaryStore().Migrated();

        store.Execute("INSERT INTO membership (index_code, ticker, joined, \"left\", observed_at) VALUES ('GSPC', 'EIX', NULL, NULL, '2026-10-01T21:00:00Z');");
        store.Execute("INSERT INTO calendar (ticker, event_date, kind, timing, detail, observed_at) VALUES ('EIX', '2026-10-09', 'ex-dividend', 'unstated', '{}', '2026-10-05T23:40:00Z');");

        var outcome = await new CalendarFetcher(new RecordedEarningsCalendarFeed(string.Empty), new SweepClock(new DateTimeOffset(2026, 10, 6, 23, 40, 0, TimeSpan.Zero)), store.DatabaseFile, new RefusingDividends())
            .RunAsync("GSPC", new DateOnly(2026, 10, 6), "night-refused");

        Assert.NotNull(outcome.DividendsFault);
        Assert.Equal(0, outcome.ExDividends);
        Assert.Equal(["EIX 2026-10-09"], Rows(store, "SELECT ticker || ' ' || event_date FROM calendar WHERE kind = 'ex-dividend';"));
        Assert.Contains("the dividend calendar not read: ", Rows(store, "SELECT detail FROM run_log WHERE stage = 'calendar';").Single(), StringComparison.Ordinal);
    }

    sealed class RefusingDividends : IDividendCalendarFeed
    {
        public int Requests { get; private set; }

        public Task<IReadOnlyList<ExDividend>> ExDividendsAsync(DateOnly session, CancellationToken cancellation = default)
        {
            Requests++;

            throw EodhdQuery.Refused(403, "dividend calendar feed");
        }
    }

    static string[] Rows(TemporaryStore store, string sql)
    {
        using var connection = store.Open();
        using var command = connection.CreateCommand();

        command.CommandText = sql;

        using var reader = command.ExecuteReader();
        var rows = new List<string>();

        while (reader.Read())
        {
            rows.Add(Convert.ToString(reader.GetValue(0), CultureInfo.InvariantCulture)!);
        }

        return [.. rows];
    }
}
