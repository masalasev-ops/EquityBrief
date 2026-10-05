using System.Globalization;
using EquityBrief.Api.Reading;
using EquityBrief.Core.Bars;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Checks;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker;
using EquityBrief.Worker.Bars;

namespace EquityBrief.Tests.Bars;

// The history pull: which names it asks for and over which span, what it stores and how each row is
// marked, what it names rather than stores, what a purge removes, what it refuses, and that no night
// reads what it stores.
//
// The population is stated where a figure is. The store's membership holds four names: AAA a member
// throughout, BBB leaving on 2026-08-03, CCC joining on 2026-08-17, and DDD leaving on 2026-06-30,
// before any span below begins, so it is never asked for. Every bar and print is constructed here,
// and every count is derived from the exchange's calendar over the span rather than read back.
// see: The history pulled before the store's year sits apart from its bars, marked by the pull that wrote it, read by no night and removed whole by that pull
public class HistoryPullTests
{
    const string Index = "GSPC";

    // A Friday evening in New York, so tonight's session, where every span ends, is 2026-09-04.
    static readonly DateTimeOffset Instant = new(2026, 9, 4, 21, 10, 0, TimeSpan.Zero);

    static readonly DateOnly Tonight = new(2026, 9, 4);

    static readonly DateOnly From = new(2026, 7, 1);

    // The one weekday between the span's ends the exchange did not trade: Independence Day, observed
    // on the Friday.
    static readonly DateOnly Closure = new(2026, 7, 3);

    // 48 weekdays from 2026-07-01 to 2026-09-04, less the closure: July's 23 less 1, August's 21 and
    // September's 4. Stated so the derivation reads as a number without running anything.
    const int SessionsInTheSpan = 47;

    static IClock Clock() => FixedClock.At(Instant, SessionZones.UnitedStates);

    static IReadOnlyList<DateOnly> Sessions(DateOnly from, DateOnly to)
    {
        var sessions = new List<DateOnly>();

        for (var day = from; day <= to; day = day.AddDays(1))
        {
            if (day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday) && day != Closure)
            {
                sessions.Add(day);
            }
        }

        return sessions;
    }

    // A bar whose every price says which session it is, so a row read back names the bar it came from:
    // the n-th session of the span opens at 100 + n, trades a point either side, closes half a point
    // above its open, and its unadjusted close is twice its close.
    static ProviderBar Bar(DateOnly session)
    {
        var n = Sessions(From, Tonight).ToList().IndexOf(session) + (session < From ? -1000 : 0);
        var open = 100m + n;

        return new ProviderBar(session, open, open + 1m, open - 1m, open + 0.5m, (open + 0.5m) * 2m, 1_000 + n);
    }

    static TemporaryStore Seeded(params string[] alsoHeld)
    {
        var store = new TemporaryStore().Migrated();

        store.Execute(@"
            INSERT INTO membership (index_code, ticker, joined, ""left"", observed_at) VALUES
                ('GSPC', 'AAA', NULL, NULL, '2026-09-04T21:10:00Z'),
                ('GSPC', 'BBB', NULL, '2026-08-03', '2026-09-04T21:10:00Z'),
                ('GSPC', 'CCC', '2026-08-17', NULL, '2026-09-04T21:10:00Z'),
                ('GSPC', 'DDD', NULL, '2026-06-30', '2026-09-04T21:10:00Z');

            INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at, raw_close) VALUES
                ('AAA', '2026-09-03', '10', '11', '9', '10.5', 500, 'bulk', '2026-09-03T21:10:00Z', '10.5'),
                ('AAA', '2026-09-04', '11', '12', '10', '11.5', 600, 'bulk', '2026-09-04T21:10:00Z', '11.5');

            INSERT INTO calendar (ticker, event_date, kind, timing, detail, observed_at) VALUES
                ('AAA', '2026-10-29', 'earnings', 'after', '{}', '2026-09-04T21:10:00Z');");

        foreach (var ticker in alsoHeld)
        {
            store.Execute($"INSERT INTO membership (index_code, ticker, joined, \"left\", observed_at) VALUES ('GSPC', '{ticker}', NULL, NULL, '2026-09-04T21:10:00Z');");
        }

        return store;
    }

    static IReadOnlyList<string> Rows(TemporaryStore store, string sql)
    {
        using var connection = store.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;

        var rows = new List<string>();

        using var reader = command.ExecuteReader();

        while (reader.Read())
        {
            rows.Add(string.Join("|", Enumerable.Range(0, reader.FieldCount).Select(at => reader.IsDBNull(at) ? "null" : Convert.ToString(reader.GetValue(at), CultureInfo.InvariantCulture))));
        }

        return rows;
    }

    static string Day(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    static readonly IReadOnlyList<CalendarEvent> Prints =
    [
        new("AAA", new DateOnly(2026, 7, 30), EventTiming.After, null, null, null),
        new("BBB", new DateOnly(2026, 8, 5), EventTiming.Before, null, null, null),
        new("AAA", new DateOnly(2026, 8, 20), EventTiming.Unstated, null, null, null),
        // A print for a name the index never held, which the calendar answers with and no pull keeps.
        new("ZZZ", new DateOnly(2026, 7, 15), EventTiming.Before, null, null, null),
    ];

    [Fact]
    public async Task APullAsksEveryNameTheIndexHeldOverTheSpanOnceAndStoresEachRowMarkedByItsRunAndNothingElse()
    {
        using var store = Seeded();

        var bars = new ConstructedBars(new Dictionary<string, IReadOnlyList<DateOnly>>
        {
            ["AAA"] = Sessions(From, Tonight),
            ["BBB"] = Sessions(From, Tonight),
            ["CCC"] = Sessions(From, Tonight),
        });
        var prints = new ConstructedPrints(Prints);

        var barsBefore = Rows(store, "SELECT * FROM bar ORDER BY ticker, session_date;");
        var calendarBefore = Rows(store, "SELECT * FROM calendar ORDER BY ticker, event_date;");

        var outcome = await new HistoryPull(bars, prints, Clock(), store.DatabaseFile).PullAsync(Index, From, "history-pull-one");

        // AAA throughout, BBB until it left and CCC from when it joined, each asked once for the whole
        // span however much of it the name was a member for; DDD left before the span and is not asked.
        Assert.Equal(
            ["AAA 2026-07-01 2026-09-04", "BBB 2026-07-01 2026-09-04", "CCC 2026-07-01 2026-09-04"],
            bars.Asked);

        // Each calendar month the span touches, cut to it at both ends.
        Assert.Equal(["2026-07-01 2026-07-31", "2026-08-01 2026-08-31", "2026-09-01 2026-09-04"], prints.Windows);

        Assert.Equal(47, SessionsInTheSpan);
        Assert.Equal(SessionsInTheSpan, Sessions(From, Tonight).Count);
        Assert.Equal(3 * SessionsInTheSpan, outcome.BarsWritten);
        Assert.Equal(3, outcome.EarningsWritten);
        Assert.Equal(3 + 3, outcome.Requests);
        Assert.Equal((From, Tonight, 3, 3), (outcome.From, outcome.Through, outcome.Names, outcome.Stored));
        Assert.Empty(outcome.Unanswered);
        Assert.Empty(outcome.Holes);
        Assert.Empty(outcome.Strays);

        Assert.Equal(["history-pull-one"], Rows(store, "SELECT DISTINCT pull FROM pulled_bar UNION SELECT DISTINCT pull FROM pulled_earnings;"));
        Assert.Equal(
            [$"AAA|{SessionsInTheSpan}", $"BBB|{SessionsInTheSpan}", $"CCC|{SessionsInTheSpan}"],
            Rows(store, "SELECT ticker, COUNT(*) FROM pulled_bar GROUP BY ticker ORDER BY ticker;"));

        // The first and the last session of the span as they were sent, the unadjusted close beside them.
        Assert.Equal(
            ["2026-07-01|100|101|99|100.5|201.0|1000", "2026-09-04|146|147|145|146.5|293.0|1046"],
            Rows(store, "SELECT session_date, open, high, low, close, raw_close, volume FROM pulled_bar WHERE ticker = 'AAA' AND session_date IN ('2026-07-01', '2026-09-04') ORDER BY session_date;"));

        // The index's own names only, in the words the calendar stores its timing in.
        Assert.Equal(
            ["AAA|2026-07-30|after", "AAA|2026-08-20|unstated", "BBB|2026-08-05|before"],
            Rows(store, "SELECT ticker, event_date, timing FROM pulled_earnings ORDER BY ticker, event_date;"));

        // Nothing a night reads was touched.
        Assert.Equal(barsBefore, Rows(store, "SELECT * FROM bar ORDER BY ticker, session_date;"));
        Assert.Equal(calendarBefore, Rows(store, "SELECT * FROM calendar ORDER BY ticker, event_date;"));

        Assert.Equal(
            [$"history-pull|ok|{(3 * SessionsInTheSpan) + 3}|6"],
            Rows(store, "SELECT stage, outcome, rows_written, network_requests FROM run_log WHERE run_id = 'history-pull-one';"));

        // The run page draws a pull and a purge as runs by hand.
        Assert.Contains(HistoryPull.RunPrefix, RunScreen.RunsByHand);
        Assert.Contains(HistoryPull.PurgePrefix, RunScreen.RunsByHand);
    }

    [Fact]
    public async Task ANameTheProviderDoesNotAnswerAndASessionOneNameMissesAreNamedAndEverythingElseIsStoredAsSent()
    {
        using var store = Seeded("EEE");

        var missing = new DateOnly(2026, 8, 12);
        var bars = new ConstructedBars(
            new Dictionary<string, IReadOnlyList<DateOnly>>
            {
                ["AAA"] = Sessions(From, Tonight),
                ["CCC"] = [.. Sessions(From, Tonight).Where(session => session != missing)],
                ["EEE"] = [],
            },
            refused: ["BBB"]);

        var outcome = await new HistoryPull(bars, new ConstructedPrints([]), Clock(), store.DatabaseFile).PullAsync(Index, From, "history-pull-two");

        Assert.Equal(["BBB: the provider answered 404", "EEE: the provider sent no session"], outcome.Unanswered);
        Assert.Equal((4, 2), (outcome.Names, outcome.Stored));

        // CCC's hole is a session AAA holds, stored as sent rather than refused or filled: one of the two
        // names spanning it holds it, which is half.
        Assert.Equal([new Gap("CCC", missing)], outcome.Holes);
        Assert.Empty(outcome.Strays);
        Assert.Equal(
            [$"AAA|{SessionsInTheSpan}", $"CCC|{SessionsInTheSpan - 1}"],
            Rows(store, "SELECT ticker, COUNT(*) FROM pulled_bar GROUP BY ticker ORDER BY ticker;"));

        var logged = Assert.Single(Rows(store, "SELECT outcome || '|' || detail FROM run_log WHERE run_id = 'history-pull-two';"));

        Assert.StartsWith(HistoryPull.Partial + "|", logged, StringComparison.Ordinal);
        Assert.Contains("BBB: the provider answered 404", logged, StringComparison.Ordinal);
        Assert.Contains("EEE: the provider sent no session", logged, StringComparison.Ordinal);
        Assert.Contains("CCC is missing 1 session(s), the first 2026-08-12", logged, StringComparison.Ordinal);

        var said = HistoryPull.Detail(outcome);

        Assert.Contains("2 of 4 name(s) answered", said, StringComparison.Ordinal);
        Assert.Contains("1 name(s) with a missing session, 0 day(s) held by fewer than half the names spanning them", said, StringComparison.Ordinal);
        Assert.Contains("unanswered: BBB: the provider answered 404", said, StringComparison.Ordinal);
    }

    // A day is a session where at least half the names whose series span it hold it. Five names answer,
    // FFF's series starting on 2026-08-17, so four span every day before it: EEE alone holds a bar on
    // Independence Day observed, 2026-07-03, one of the four, which is a stray and nobody's missing
    // session; AAA and EEE alone hold 2026-08-12, two of the four, exactly half, which is a session BBB
    // and CCC each miss. Against every date any name held, AAA, BBB and CCC would each miss 2026-07-03:
    // three names with a missing session where two miss a session the exchange traded.
    [Fact]
    public async Task ADayFewerThanHalfTheNamesSpanningItHoldIsNamedApartAndNoOtherNameReadsAsMissingIt()
    {
        using var store = Seeded("EEE", "FFF");

        var missing = new DateOnly(2026, 8, 12);
        var later = new DateOnly(2026, 8, 17);
        var bars = new ConstructedBars(new Dictionary<string, IReadOnlyList<DateOnly>>
        {
            ["AAA"] = Sessions(From, Tonight),
            ["BBB"] = [.. Sessions(From, Tonight).Where(session => session != missing)],
            ["CCC"] = [.. Sessions(From, Tonight).Where(session => session != missing)],
            ["EEE"] = [.. Sessions(From, Tonight), Closure],
            ["FFF"] = Sessions(later, Tonight),
        });

        var outcome = await new HistoryPull(bars, new ConstructedPrints([]), Clock(), store.DatabaseFile).PullAsync(Index, From, "history-pull-stray");

        Assert.Equal((5, 5), (outcome.Names, outcome.Stored));
        Assert.Equal([new Gap("BBB", missing), new Gap("CCC", missing)], outcome.Holes);

        var stray = Assert.Single(outcome.Strays);

        Assert.Equal((Closure, 4), (stray.Day, stray.Spanning));
        Assert.Equal(["EEE"], stray.Holders);

        // Every bar stored as it was sent, the stray among them: 47, 46, 46, 48 and FFF's 15.
        Assert.Equal(
            [$"AAA|{SessionsInTheSpan}", $"BBB|{SessionsInTheSpan - 1}", $"CCC|{SessionsInTheSpan - 1}", $"EEE|{SessionsInTheSpan + 1}", "FFF|15"],
            Rows(store, "SELECT ticker, COUNT(*) FROM pulled_bar GROUP BY ticker ORDER BY ticker;"));

        var logged = Assert.Single(Rows(store, "SELECT detail FROM run_log WHERE run_id = 'history-pull-stray';"));

        Assert.Contains("BBB is missing 1 session(s), the first 2026-08-12", logged, StringComparison.Ordinal);
        Assert.Contains("CCC is missing 1 session(s), the first 2026-08-12", logged, StringComparison.Ordinal);
        Assert.DoesNotContain("AAA is missing", logged, StringComparison.Ordinal);
        Assert.Contains("2026-07-03 held by 1 of the 4 name(s) spanning it: EEE", logged, StringComparison.Ordinal);
        Assert.Contains("2 name(s) with a missing session, 1 day(s) held by fewer than half the names spanning them", HistoryPull.Detail(outcome), StringComparison.Ordinal);
    }

    [Fact]
    public async Task ASecondPullAddsOnlyTheSessionsNoPullHoldsAndAPurgeRemovesItsOwnPullWholeAndNothingElse()
    {
        using var store = Seeded();

        var all = new Dictionary<string, IReadOnlyList<DateOnly>>
        {
            ["AAA"] = Sessions(From, Tonight),
            ["BBB"] = Sessions(From, Tonight),
            ["CCC"] = Sessions(From, Tonight),
        };

        // From 2026-08-17 the index held AAA and CCC, BBB having left: 15 sessions each, August's 11
        // from the 17th and September's 4.
        var later = new DateOnly(2026, 8, 17);
        var first = await new HistoryPull(new ConstructedBars(all), new ConstructedPrints(Prints), Clock(), store.DatabaseFile).PullAsync(Index, later, "history-pull-a");

        Assert.Equal(15, Sessions(later, Tonight).Count);
        Assert.Equal((2 * 15, 1), (first.BarsWritten, first.EarningsWritten));

        // From 2026-07-01 all three are asked for the whole span, and only what the first pull does not
        // hold is added: 47 less 15 for AAA and CCC, all 47 for BBB, and the two earlier prints.
        var second = await new HistoryPull(new ConstructedBars(all), new ConstructedPrints(Prints), Clock(), store.DatabaseFile).PullAsync(Index, From, "history-pull-b");

        Assert.Equal(((2 * (SessionsInTheSpan - 15)) + SessionsInTheSpan, 2), (second.BarsWritten, second.EarningsWritten));
        Assert.Equal(
            ["history-pull-a|30|1", $"history-pull-b|{(2 * (SessionsInTheSpan - 15)) + SessionsInTheSpan}|2"],
            Rows(store, "SELECT pull, (SELECT COUNT(*) FROM pulled_bar b WHERE b.pull = p.pull), (SELECT COUNT(*) FROM pulled_earnings e WHERE e.pull = p.pull) FROM (SELECT DISTINCT pull FROM pulled_bar) p ORDER BY pull;"));

        var barsBefore = Rows(store, "SELECT * FROM bar ORDER BY ticker, session_date;");
        var survivors = Rows(store, "SELECT ticker, session_date, pull FROM pulled_bar WHERE pull = 'history-pull-b' ORDER BY ticker, session_date;");

        var purged = await HistoryPull.PurgeAsync(Clock(), store.DatabaseFile, "history-pull-a", "history-purge-a");

        Assert.Equal(("history-pull-a", 30, 1), (purged.Pull, purged.Bars, purged.Earnings));
        Assert.Equal(survivors, Rows(store, "SELECT ticker, session_date, pull FROM pulled_bar ORDER BY ticker, session_date;"));
        Assert.Equal(["AAA|2026-07-30|history-pull-b", "BBB|2026-08-05|history-pull-b"], Rows(store, "SELECT ticker, event_date, pull FROM pulled_earnings ORDER BY ticker, event_date;"));
        Assert.Equal(barsBefore, Rows(store, "SELECT * FROM bar ORDER BY ticker, session_date;"));
        Assert.Equal(
            ["history-purge|ok|0|0|{\"pull\":\"history-pull-a\",\"bars\":30,\"earnings\":1,\"surprises\":0,\"market\":0,\"companies\":0,\"counts\":0,\"splits\":0,\"revenue\":0,\"members\":0,\"income\":0,\"snapshots\":0,\"holdings\":0}"],
            Rows(store, "SELECT stage, outcome, rows_written, network_requests, detail FROM run_log WHERE run_id = 'history-purge-a';"));

        // A pull no row carries any longer is refused, and the refusal writes nothing.
        var logged = Rows(store, "SELECT COUNT(*) FROM run_log;");

        await Assert.ThrowsAsync<ArgumentException>(() => HistoryPull.PurgeAsync(Clock(), store.DatabaseFile, "history-pull-a", "history-purge-again"));

        Assert.Equal(logged, Rows(store, "SELECT COUNT(*) FROM run_log;"));
        Assert.Equal(survivors, Rows(store, "SELECT ticker, session_date, pull FROM pulled_bar ORDER BY ticker, session_date;"));
    }

    [Fact]
    public async Task APullFromTonightOrLaterAnUnknownPullAndAMissingDateAreEachRefusedWithNothingAskedOrWritten()
    {
        using var store = Seeded();

        var bars = new ConstructedBars(new Dictionary<string, IReadOnlyList<DateOnly>> { ["AAA"] = Sessions(From, Tonight) });
        var prints = new ConstructedPrints(Prints);

        await Assert.ThrowsAsync<ArgumentException>(() => new HistoryPull(bars, prints, Clock(), store.DatabaseFile).PullAsync(Index, Tonight, "history-pull-late"));

        Assert.Equal((0, 0), (bars.Requests, prints.Requests));
        Assert.Equal(["0|0|0"], Rows(store, "SELECT (SELECT COUNT(*) FROM pulled_bar), (SELECT COUNT(*) FROM pulled_earnings), (SELECT COUNT(*) FROM run_log);"));

        // The verb a person runs, over the same refusals. A purge never asks for the feeds, so it needs
        // no key and reaches no provider.
        var asked = 0;

        NightFeeds Feeds()
        {
            asked++;

            return NightFeeds.FromFixture(Path.Combine(Repository.Root, "fixtures", "membership-2026-09-05")) with { Historical = bars, Calendar = prints };
        }

        async Task<(int Code, string Output, string Error)> Verb(params string[] args)
        {
            var output = new StringWriter();
            var error = new StringWriter();
            var code = await HistoryPull.RunAsync(["history-pull", .. args], Feeds, Clock(), store.DatabaseFile, output, error);

            return (code, output.ToString(), error.ToString());
        }

        var none = await Verb();

        Assert.Equal(2, none.Code);
        Assert.Contains("'--from <yyyy-MM-dd>'", none.Error, StringComparison.Ordinal);

        var late = await Verb("--from", Day(Tonight));

        Assert.Equal(1, late.Code);
        Assert.Contains("2026-09-04 is not before it", late.Error, StringComparison.Ordinal);

        var unknown = await Verb("--purge", "history-pull-none");

        Assert.Equal(1, unknown.Code);
        Assert.Contains("No pulled row carries the pull 'history-pull-none'", unknown.Error, StringComparison.Ordinal);

        Assert.Equal(1, asked);
        Assert.Equal((0, 0), (bars.Requests, prints.Requests));
        Assert.Equal(["0|0|0"], Rows(store, "SELECT (SELECT COUNT(*) FROM pulled_bar), (SELECT COUNT(*) FROM pulled_earnings), (SELECT COUNT(*) FROM run_log);"));

        // And a pull through the verb prints its run id first, then what it did: the feed answers AAA
        // alone of the three names the index held over the span.
        var pulled = await Verb("--from", Day(From));

        Assert.Equal(0, pulled.Code);
        Assert.Equal(2, asked);
        Assert.StartsWith("pull " + HistoryPull.RunPrefix, pulled.Output, StringComparison.Ordinal);
        Assert.Contains($"1 of 3 name(s) answered, {SessionsInTheSpan} bar(s) and 3 earnings print(s) stored", pulled.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void TheCalendarIsAskedForEachMonthTheSpanTouchesCutToTheSpanAtBothEnds()
    {
        Assert.Equal(
            [(new DateOnly(2025, 12, 15), new DateOnly(2025, 12, 31)), (new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31)), (new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 3))],
            HistoryPull.MonthsOf(new DateOnly(2025, 12, 15), new DateOnly(2026, 2, 3)));

        Assert.Equal([(new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 28))], HistoryPull.MonthsOf(new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 28)));
        Assert.Equal([(new DateOnly(2026, 2, 27), new DateOnly(2026, 2, 27))], HistoryPull.MonthsOf(new DateOnly(2026, 2, 27), new DateOnly(2026, 2, 27)));
    }

    // The ruling's own claim, asserted by behaviour: a night run over a store already holding pulled
    // history computes what it computes over a store holding none. The pulled rows are built to move
    // everything a reader of them would compute: the years before the fixture's at a price of one, every
    // session of its year again at a price no name trades at, and a print on every tenth weekday. The
    // two stores are compared table by table, every table a night writes and the run log's rows aside.
    [Fact]
    public async Task ANightOverAStoreHoldingPulledHistoryComputesExactlyWhatItComputesWithoutIt()
    {
        var tickers = Directory
            .GetFiles(Path.Combine(Repository.Root, "fixtures", "membership-2026-09-05"), "bars-*.json")
            .Select(path => Path.GetFileNameWithoutExtension(path)["bars-".Length..])
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.True(tickers.Length >= 4, $"The fixture captures {tickers.Length} names' bars, expected at least 4.");

        void Hostile(TemporaryStore store)
        {
            foreach (var ticker in tickers)
            {
                store.Execute($@"
                    WITH RECURSIVE day(d) AS (SELECT '2024-01-02' UNION ALL SELECT date(d, '+1 day') FROM day WHERE d < '2026-09-08')
                    INSERT INTO pulled_bar (ticker, session_date, open, high, low, close, raw_close, volume, pull)
                    SELECT '{ticker}', d,
                           CASE WHEN d < '2025-09-04' THEN '1' ELSE '99999' END,
                           CASE WHEN d < '2025-09-04' THEN '1.5' ELSE '99999.5' END,
                           CASE WHEN d < '2025-09-04' THEN '0.5' ELSE '99998.5' END,
                           CASE WHEN d < '2025-09-04' THEN '1' ELSE '99999' END,
                           '1', 999999999, 'history-pull-hostile'
                    FROM day WHERE strftime('%w', d) NOT IN ('0', '6');

                    WITH RECURSIVE day(d, n) AS (SELECT '2024-01-02', 0 UNION ALL SELECT date(d, '+1 day'), n + 1 FROM day WHERE d < '2026-09-08')
                    INSERT INTO pulled_earnings (ticker, event_date, timing, pull)
                    SELECT '{ticker}', d, 'before', 'history-pull-hostile' FROM day WHERE n % 10 = 0 AND strftime('%w', d) NOT IN ('0', '6');

                    INSERT INTO pulled_company (ticker, cik, sector, industry_group, industry, sub_industry, delisted_on, pull)
                    VALUES ('{ticker}', '9999999999', 'Utilities', 'Utilities', 'Water Utilities', 'Water Utilities', '2025-01-02', 'history-pull-hostile');

                    INSERT INTO pulled_shares (ticker, period_end, filing_date, shares, basis_session, pull)
                    VALUES ('{ticker}', '2026-06-30', '2026-07-31', '1', '2026-09-08', 'history-pull-hostile');

                    INSERT INTO pulled_split (ticker, ex_date, new_shares, old_shares, pull)
                    VALUES ('{ticker}', '2026-09-01', '100', '1', 'history-pull-hostile');

                    INSERT INTO pulled_revenue (cik, concept, period_start, period_end, accession, dollars, filed, form, pull)
                    VALUES ('9999999999', 'Revenues', '2026-04-01', '2026-06-30', 'hostile-{ticker}', '1', '2026-07-31', '10-Q', 'history-pull-hostile');

                    INSERT INTO pulled_member (index_code, ticker, exchange, name, sector, industry, pull)
                    VALUES ('MID', '{ticker}', 'US', 'Hostile', 'Utilities', 'Water Utilities', 'history-pull-hostile');

                    INSERT INTO pulled_income (ticker, period_end, filing_date, net_income, operating_income, interest_expense, pull)
                    VALUES ('{ticker}', '2026-06-30', '2026-07-31', '-99999999', '-99999999', '99999999', 'history-pull-hostile');

                    INSERT INTO pulled_snapshot (index_code, period, accession, filed, holdings, equity, pull)
                    VALUES ('SML', '2026-06-30', 'hostile-{ticker}', '2026-08-27', 1, 1, 'history-pull-hostile')
                    ON CONFLICT (index_code, period) DO NOTHING;

                    INSERT INTO pulled_holding (index_code, period, holding, name, cusip, isin, ticker, matched_by, pull)
                    VALUES ('SML', '2026-06-30', 'hostile-{ticker}', 'Hostile', NULL, NULL, '{ticker}', 'name', 'history-pull-hostile');");
            }

            store.Execute(@"
                INSERT INTO pulled_surprise (ticker, event_date, timing, eps_actual, eps_estimate, surprise_percent, pull)
                SELECT ticker, '2026-09-01', 'before', '99', '1', 9800, 'history-pull-hostile' FROM pulled_company;

                WITH RECURSIVE day(d) AS (SELECT '2024-01-02' UNION ALL SELECT date(d, '+1 day') FROM day WHERE d < '2026-09-08')
                INSERT INTO pulled_market_bar (series, session_date, open, high, low, close, pull)
                SELECT series, d, '1', '1', '1', '1', 'history-pull-hostile' FROM day, (SELECT 'GSPC' AS series UNION ALL SELECT 'VIX' UNION ALL SELECT 'XLK')
                WHERE strftime('%w', d) NOT IN ('0', '6');");
        }

        var (plain, plainCode, _, _) = await FixtureReplay.NightAsync();
        var (pulled, pulledCode, _, _) = await FixtureReplay.NightAsync(before: Hostile);

        using (plain)
        using (pulled)
        {
            Assert.Equal(plainCode, pulledCode);

            var held = Rows(pulled, "SELECT COUNT(*) FROM pulled_bar;");

            Assert.True(int.Parse(held[0], CultureInfo.InvariantCulture) > tickers.Length * 600, $"The hostile store holds {held[0]} pulled bars, expected more than {tickers.Length * 600}.");

            var tables = Rows(plain, "SELECT name FROM sqlite_master WHERE type = 'table' AND name NOT LIKE 'pulled\\_%' ESCAPE '\\' AND name NOT IN ('run_log', 'sqlite_sequence') ORDER BY name;");

            // Every one of the twelve pulled tables holds hostile rows, so no night can read one and compute the same.
            Assert.All(
                ["pulled_bar", "pulled_earnings", "pulled_surprise", "pulled_market_bar", "pulled_company", "pulled_shares", "pulled_split", "pulled_revenue", "pulled_member", "pulled_income", "pulled_snapshot", "pulled_holding"],
                table => Assert.True(int.Parse(Rows(pulled, $"SELECT COUNT(*) FROM {table};")[0], CultureInfo.InvariantCulture) > 0, $"The hostile store holds no row in {table}."));
            Assert.Equal(12, Rows(pulled, "SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name LIKE 'pulled\\_%' ESCAPE '\\';").Select(count => int.Parse(count, CultureInfo.InvariantCulture)).Single());

            Assert.True(tables.Count >= 30, $"Compared {tables.Count} tables, expected at least 30.");
            Assert.True(
                int.Parse(Rows(plain, "SELECT COUNT(*) FROM level;")[0], CultureInfo.InvariantCulture) > 0,
                "The fixture night stored no band, so a comparison of bands would pass on nothing.");

            foreach (var table in tables)
            {
                var sql = $"SELECT * FROM {table};";

                Assert.True(
                    Rows(plain, sql).Order(StringComparer.Ordinal).SequenceEqual(Rows(pulled, sql).Order(StringComparer.Ordinal)),
                    $"The night over the store holding pulled history wrote {table} differently.");
            }

            // And the night left the pulled rows as it found them.
            Assert.Equal(held, Rows(pulled, "SELECT COUNT(*) FROM pulled_bar;"));

            // The pages read nothing of them either: each name's page, its chart among it, and tonight's
            // list and the universe drawn over the two stores are the same pages.
            using var plainHost = new Reading.ReadSurface.Host(plain.Root);
            using var pulledHost = new Reading.ReadSurface.Host(pulled.Root);
            using var plainClient = plainHost.CreateClient();
            using var pulledClient = pulledHost.CreateClient();

            string[] routes = ["/screens/tonight", "/screens/universe", .. tickers.Select(ticker => $"/screens/name/{ticker}")];

            foreach (var route in routes)
            {
                var drawn = await plainClient.GetStringAsync(route);

                Assert.True(drawn.Length > 1000, $"{route} drew {drawn.Length} characters, too few to compare.");
                Assert.True(drawn == await pulledClient.GetStringAsync(route), $"{route} draws differently over the store holding pulled history.");
            }
        }
    }

    // What no night reads, no shipped source but the pull and the one measurement that reads it names: the
    // history pull, the migration that creates its two tables and the sweep's history, which reads them by
    // hand and never from a night, are the only files outside the suite that name either table or its store,
    // so no stage, score, record or page can read them without this failing first.
    // see: The bar store holds one year for every night's work, and the history pulled beside it is read by measurements alone
    [Fact]
    public void NoShippedSourceButThePullAndItsMigrationNamesThePulledTables()
    {
        var naming = new System.Text.RegularExpressions.Regex(@"pulled_(bar|earnings|surprise|market_bar|company|shares|split|revenue|member)\b|Store\.Pulled(Bar|Earnings|Surprise|MarketBar|Company|Shares|Split|Revenue|Member)\b");

        var files = Repository.SourceFiles()
            .Where(file => !file.Contains(Path.DirectorySeparatorChar + "EquityBrief.Tests" + Path.DirectorySeparatorChar, StringComparison.Ordinal))
            .ToArray();

        Assert.True(files.Length > 100, $"Read {files.Length} shipped source files, expected more than 100.");

        var found = files
            .Where(file => naming.IsMatch(File.ReadAllText(file)))
            .Select(file => Path.GetRelativePath(Repository.Root, file).Replace(Path.DirectorySeparatorChar, '/'))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(["src/EquityBrief.Data/Migrations/SchemaMigrations.cs", "src/EquityBrief.Worker/Bars/HistoryPull.cs", "src/EquityBrief.Worker/Sweep/SweepHistory.cs"], found);

        // The reader is shown to find what it looks for: a query, a declaration of either store, and not a
        // word that only begins the same way.
        Assert.Matches(naming, "SELECT close FROM pulled_bar WHERE ticker = $ticker;");
        Assert.Matches(naming, "new StoreTouch(Store.PulledEarnings, Touch.Read)");
        Assert.Matches(naming, "SELECT surprise_percent FROM pulled_surprise;");
        Assert.Matches(naming, "SELECT close FROM pulled_market_bar WHERE series = 'VIX';");
        Assert.Matches(naming, "new StoreTouch(Store.PulledMarketBar, Touch.Read)");
        Assert.Matches(naming, "SELECT sector FROM pulled_company WHERE ticker = $ticker;");
        Assert.Matches(naming, "SELECT shares FROM pulled_shares WHERE ticker = $ticker;");
        Assert.Matches(naming, "SELECT new_shares FROM pulled_split;");
        Assert.Matches(naming, "SELECT dollars FROM pulled_revenue WHERE cik = $cik;");
        Assert.Matches(naming, "new StoreTouch(Store.PulledRevenue, Touch.Read)");
        Assert.Matches(naming, "SELECT DISTINCT ticker FROM pulled_member ORDER BY ticker;");
        Assert.Matches(naming, "new StoreTouch(Store.PulledMember, Touch.Read)");
        Assert.DoesNotMatch(naming, "var pulled_barrier = 1; Store.PulledBarrier");
        Assert.DoesNotMatch(naming, "var pulled_companyish = 1; Store.PulledSharesOut");
    }

    // The surprise pull: the calendar asked once a calendar month of the span, each print of a held name stored
    // with the figures as filed and the provider's surprise where the answer carries an estimate and an actual,
    // marked by its pull, removed whole with it, and refused for a date on or after tonight.
    // see: The surprises pulled before the store's year sit beside the pulled prints and are read by no night
    [Fact]
    public async Task ASurprisePullStoresEachHeldNamesPrintsWithTheSurpriseAsFiledAndAPurgeRemovesThemWithThePull()
    {
        using var store = Seeded();

        var prints = new ConstructedPrints(
        [
            new("AAA", new DateOnly(2026, 7, 30), EventTiming.After, new DateOnly(2026, 6, 30), "1.20", "1.32", "10"),
            new("AAA", new DateOnly(2026, 8, 20), EventTiming.Unstated, null, "0.50", "0.45", "-10"),
            // A print with an estimate and no actual has not happened, and one with neither carries no surprise.
            new("BBB", new DateOnly(2026, 8, 5), EventTiming.Before, null, "2.00", null, null),
            new("CCC", new DateOnly(2026, 8, 25), EventTiming.After, null, null, null, null),
            new("ZZZ", new DateOnly(2026, 7, 15), EventTiming.Before, null, "1", "2", "100"),
        ]);
        var pull = new HistoryPull(new ConstructedBars(new Dictionary<string, IReadOnlyList<DateOnly>>()), prints, Clock(), store.DatabaseFile);
        var outcome = await pull.PullSurprisesAsync(Index, From, "history-pull-surprises");

        Assert.Equal(["2026-07-01 2026-07-31", "2026-08-01 2026-08-31", "2026-09-01 2026-09-04"], prints.Windows);
        Assert.Equal((3, 3, 4, 2, 4, 3), (outcome.Names, outcome.Months, outcome.Prints, outcome.WithASurprise, outcome.Written, outcome.Requests));
        Assert.Equal(
            ["AAA|2026-07-30|after|1.32|1.20|10|history-pull-surprises", "AAA|2026-08-20|unstated|0.45|0.50|-10|history-pull-surprises", "BBB|2026-08-05|before|null|2.00|null|history-pull-surprises", "CCC|2026-08-25|after|null|null|null|history-pull-surprises"],
            Rows(store, "SELECT ticker, event_date, timing, eps_actual, eps_estimate, surprise_percent, pull FROM pulled_surprise ORDER BY ticker, event_date;"));
        Assert.Equal(["history-pull-surprises|ok|4|3"], Rows(store, "SELECT stage, outcome, rows_written, network_requests FROM run_log WHERE run_id = 'history-pull-surprises';"));

        // A second pull adds only the prints no earlier pull holds.
        var again = await pull.PullSurprisesAsync(Index, From, "history-pull-surprises-again");

        Assert.Equal(0, again.Written);
        Assert.Equal(["history-pull-surprises"], Rows(store, "SELECT DISTINCT pull FROM pulled_surprise;"));

        // The provider's surprise is read as the statistic it is, and none without an estimate and an actual.
        Assert.Equal(10, HistoryPull.SurprisePercent(new CalendarEvent("AAA", new DateOnly(2026, 7, 30), EventTiming.After, null, "1.20", "1.32", "10")));
        Assert.Null(HistoryPull.SurprisePercent(new CalendarEvent("AAA", new DateOnly(2026, 7, 30), EventTiming.After, null, null, "1.32", "10")));
        Assert.Null(HistoryPull.SurprisePercent(new CalendarEvent("AAA", new DateOnly(2026, 7, 30), EventTiming.After, null, "1.20", "1.32", null)));

        // A purge removes the pull's surprises with its bars and prints, and says how many.
        var purged = await HistoryPull.PurgeAsync(Clock(), store.DatabaseFile, "history-pull-surprises", "history-purge-surprises");

        Assert.Equal((0, 0, 4), (purged.Bars, purged.Earnings, purged.Surprises));
        Assert.Empty(Rows(store, "SELECT * FROM pulled_surprise;"));

        // A date on or after tonight is refused before any request: the two pulls' six requests stand.
        await Assert.ThrowsAsync<ArgumentException>(() => pull.PullSurprisesAsync(Index, Tonight, "history-pull-refused"));
        Assert.Equal(6, prints.Requests);
    }

    // The market pull: the index and the VIX asked for once each over the span, every session stored as sent
    // and marked by its pull, removed whole by its purge, refused before any request for a date on or after
    // tonight, and a series the provider refuses storing nothing and failing the command.
    // see: The index's and the VIX's daily series are pulled beside the pulled bars, marked by their pull and read by no night
    [Fact]
    public async Task AMarketPullAsksForTheIndexAndTheVixOnceEachAndStoresEverySessionMarkedByItsRunAndNothingElse()
    {
        using var store = Seeded();

        var market = new ConstructedSeries(new Dictionary<string, IReadOnlyList<DateOnly>>
        {
            ["GSPC"] = Sessions(From, Tonight),
            ["VIX"] = Sessions(From, Tonight),
        });
        var barsBefore = Rows(store, "SELECT * FROM bar ORDER BY ticker, session_date;");

        var outcome = await HistoryPull.PullMarketAsync(market, Clock(), store.DatabaseFile, From, "history-pull-market");

        // One request a series over the whole span, the index first.
        Assert.Equal(["GSPC 2026-07-01 2026-09-04", "VIX 2026-07-01 2026-09-04"], market.Asked);
        Assert.Equal((2, 2 * SessionsInTheSpan, 0), (outcome.Requests, outcome.Written, outcome.Refused.Count));
        Assert.Equal(
            [new MarketSeriesStored("GSPC", SessionsInTheSpan, From, Tonight), new MarketSeriesStored("VIX", SessionsInTheSpan, From, Tonight)],
            outcome.Stored);

        // Each session as sent: the first and the last of each series, the 47th being the 46th after the first.
        Assert.Equal(
            [
                "GSPC|2026-07-01|6000|6002|5998|6001|history-pull-market",
                "GSPC|2026-09-04|6046|6048|6044|6047|history-pull-market",
                "VIX|2026-07-01|15.25|17.25|13.25|16.25|history-pull-market",
                "VIX|2026-09-04|61.25|63.25|59.25|62.25|history-pull-market",
            ],
            Rows(store, "SELECT series, session_date, open, high, low, close, pull FROM pulled_market_bar WHERE session_date IN ('2026-07-01', '2026-09-04') ORDER BY series, session_date;"));
        Assert.Equal([$"GSPC|{SessionsInTheSpan}", $"VIX|{SessionsInTheSpan}"], Rows(store, "SELECT series, COUNT(*) FROM pulled_market_bar GROUP BY series ORDER BY series;"));

        // Its own row on the run log, and no other table moved.
        Assert.Equal(
            [$"history-pull-market|ok|{2 * SessionsInTheSpan}|2"],
            Rows(store, "SELECT stage, outcome, rows_written, network_requests FROM run_log WHERE run_id = 'history-pull-market';"));
        Assert.Equal(["0|0|0"], Rows(store, "SELECT (SELECT COUNT(*) FROM pulled_bar), (SELECT COUNT(*) FROM pulled_earnings), (SELECT COUNT(*) FROM pulled_surprise);"));
        Assert.Equal(barsBefore, Rows(store, "SELECT * FROM bar ORDER BY ticker, session_date;"));

        // A second pull adds only the sessions no pull holds, which here is none.
        var again = await HistoryPull.PullMarketAsync(market, Clock(), store.DatabaseFile, From, "history-pull-market-again");

        Assert.Equal(0, again.Written);
        Assert.Equal(["history-pull-market"], Rows(store, "SELECT DISTINCT pull FROM pulled_market_bar;"));
    }

    [Fact]
    public async Task APurgeRemovesAMarketPullsSessionsWholeSaysHowManyAndLeavesEveryOtherPullsRows()
    {
        using var store = Seeded();

        var all = Sessions(From, Tonight);
        var bars = new ConstructedBars(new Dictionary<string, IReadOnlyList<DateOnly>> { ["AAA"] = all, ["BBB"] = all, ["CCC"] = all });
        var market = new ConstructedSeries(new Dictionary<string, IReadOnlyList<DateOnly>> { ["GSPC"] = all, ["VIX"] = all });

        await new HistoryPull(bars, new ConstructedPrints(Prints), Clock(), store.DatabaseFile).PullAsync(Index, From, "history-pull-bars");
        await HistoryPull.PullMarketAsync(market, Clock(), store.DatabaseFile, From, "history-pull-market");

        var barsKept = Rows(store, "SELECT ticker, session_date, pull FROM pulled_bar ORDER BY ticker, session_date;");
        var purged = await HistoryPull.PurgeAsync(Clock(), store.DatabaseFile, "history-pull-market", "history-purge-market");

        Assert.Equal(("history-pull-market", 0, 0, 0, 2 * SessionsInTheSpan), (purged.Pull, purged.Bars, purged.Earnings, purged.Surprises, purged.MarketBars));
        Assert.Empty(Rows(store, "SELECT * FROM pulled_market_bar;"));
        Assert.Equal(barsKept, Rows(store, "SELECT ticker, session_date, pull FROM pulled_bar ORDER BY ticker, session_date;"));
        Assert.Equal(
            [$"history-purge|ok|{{\"pull\":\"history-pull-market\",\"bars\":0,\"earnings\":0,\"surprises\":0,\"market\":{2 * SessionsInTheSpan},\"companies\":0,\"counts\":0,\"splits\":0,\"revenue\":0,\"members\":0,\"income\":0,\"snapshots\":0,\"holdings\":0}}"],
            Rows(store, "SELECT stage, outcome, detail FROM run_log WHERE run_id = 'history-purge-market';"));

        // And a purge of the bars' pull leaves the market series it did not write.
        await HistoryPull.PullMarketAsync(market, Clock(), store.DatabaseFile, From, "history-pull-market-again");
        await HistoryPull.PurgeAsync(Clock(), store.DatabaseFile, "history-pull-bars", "history-purge-bars");

        Assert.Equal([$"history-pull-market-again|{2 * SessionsInTheSpan}"], Rows(store, "SELECT pull, COUNT(*) FROM pulled_market_bar GROUP BY pull;"));
    }

    [Fact]
    public async Task AMarketPullFromTonightOrLaterIsRefusedBeforeAnyRequestAndWritesNothing()
    {
        using var store = Seeded();

        var market = new ConstructedSeries(new Dictionary<string, IReadOnlyList<DateOnly>> { ["GSPC"] = Sessions(From, Tonight), ["VIX"] = Sessions(From, Tonight) });

        await Assert.ThrowsAsync<ArgumentException>(() => HistoryPull.PullMarketAsync(market, Clock(), store.DatabaseFile, Tonight, "history-pull-late"));
        await Assert.ThrowsAsync<ArgumentException>(() => HistoryPull.PullMarketAsync(market, Clock(), store.DatabaseFile, Tonight.AddDays(3), "history-pull-later"));

        // Through the verb a person runs, with the night's feeds never resolved, since a market pull asks none of them.
        var output = new StringWriter();
        var error = new StringWriter();
        var code = await HistoryPull.RunAsync(
            ["history-pull", "--market", "--from", Day(Tonight)],
            () => throw new InvalidOperationException("a market pull resolves none of the night's feeds"),
            Clock(),
            store.DatabaseFile,
            output,
            error,
            () => market);

        Assert.Equal(1, code);
        Assert.Contains("2026-09-04 is not before it", error.ToString(), StringComparison.Ordinal);
        Assert.Equal(0, market.Requests);
        Assert.Equal(["0|0"], Rows(store, "SELECT (SELECT COUNT(*) FROM pulled_market_bar), (SELECT COUNT(*) FROM run_log);"));
    }

    [Fact]
    public async Task ASeriesTheProviderRefusesStoresNothingIsNamedAndTheCommandFails()
    {
        using var store = Seeded();

        var market = new ConstructedSeries(
            new Dictionary<string, IReadOnlyList<DateOnly>> { ["GSPC"] = Sessions(From, Tonight), ["VIX"] = Sessions(From, Tonight) },
            refused: ["VIX"]);
        var output = new StringWriter();
        var error = new StringWriter();

        var code = await HistoryPull.RunAsync(
            ["history-pull", "--market", "--from", Day(From), "--live"],
            () => throw new InvalidOperationException("a market pull resolves none of the night's feeds"),
            Clock(),
            store.DatabaseFile,
            output,
            error,
            () => market);

        Assert.Equal(1, code);
        Assert.Equal(["GSPC 2026-07-01 2026-09-04", "VIX 2026-07-01 2026-09-04"], market.Asked);
        Assert.Equal([$"GSPC|{SessionsInTheSpan}"], Rows(store, "SELECT series, COUNT(*) FROM pulled_market_bar GROUP BY series;"));
        Assert.StartsWith("pull " + HistoryPull.RunPrefix, output.ToString(), StringComparison.Ordinal);
        Assert.Contains("1 of 2 series answered", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("refused: VIX: the provider answered 404", output.ToString(), StringComparison.Ordinal);
        Assert.Equal(
            [$"history-pull-market|partial|{SessionsInTheSpan}|2"],
            Rows(store, "SELECT stage, outcome, rows_written, network_requests FROM run_log WHERE stage = 'history-pull-market';"));

        // A series the provider answers with no session is refused the same way.
        var silent = new ConstructedSeries(new Dictionary<string, IReadOnlyList<DateOnly>> { ["GSPC"] = Sessions(From, Tonight) });
        var outcome = await HistoryPull.PullMarketAsync(silent, Clock(), store.DatabaseFile, From, "history-pull-market-silent");

        Assert.Equal(["VIX: the provider sent no session"], outcome.Refused);
    }

    // A series answered in a form that cannot be read, a page or an array of anything but sessions read by the
    // feeds' own reader, or not answered in time on any try, is named and stores nothing, the other series is
    // stored and the pull returns with its row partial, where any of the three ended the pull.
    // see: The index's and the VIX's daily series are pulled beside the pulled bars, marked by their pull and read by no night
    [Fact]
    public async Task ASeriesAnsweredInAFormThatCannotBeReadOrNotInTimeIsNamedAndTheOtherSeriesStored()
    {
        const string Served = """
            [
              {"date":"2026-09-02","open":6040,"high":6042,"low":6038,"close":6041,"adjusted_close":6041,"volume":0},
              {"date":"2026-09-03","open":6041,"high":6043,"low":6039,"close":6042,"adjusted_close":6042,"volume":0},
              {"date":"2026-09-04","open":6042,"high":6044,"low":6040,"close":6043,"adjusted_close":6043,"volume":0}
            ]
            """;

        var feeds = new (IMarketSeriesFeed Feed, string Said, string Run)[]
        {
            (new RecordedMarketSeriesFeed(new Dictionary<string, string> { ["GSPC"] = Served, ["VIX"] = "<html><body>Service busy</body></html>" }), "VIX: its answer could not be read: ", "history-pull-market-page"),
            (new RecordedMarketSeriesFeed(new Dictionary<string, string> { ["GSPC"] = Served, ["VIX"] = "[\"busy\"]" }), "VIX: its answer could not be read: ", "history-pull-market-array"),
            (new TimingOut("VIX", new RecordedMarketSeriesFeed(new Dictionary<string, string> { ["GSPC"] = Served })), "VIX: the provider did not answer in time on any try", "history-pull-market-slow"),
        };

        foreach (var (feed, said, run) in feeds)
        {
            using var store = Seeded();

            var outcome = await HistoryPull.PullMarketAsync(feed, Clock(), store.DatabaseFile, From, run);

            Assert.StartsWith(said, Assert.Single(outcome.Refused), StringComparison.Ordinal);
            Assert.Equal((2, 3), (outcome.Requests, outcome.Written));
            Assert.Equal(["GSPC|3"], Rows(store, "SELECT series, COUNT(*) FROM pulled_market_bar GROUP BY series;"));
            Assert.Equal(["history-pull-market|partial|3|2"], Rows(store, $"SELECT stage, outcome, rows_written, network_requests FROM run_log WHERE run_id = '{run}';"));
        }
    }

    // A market series feed timing out on every try for one series, as the provider's request does once its time
    // limit passes, and answering the others from the feed it is handed.
    sealed class TimingOut(string slow, IMarketSeriesFeed served) : IMarketSeriesFeed
    {
        public int Requests { get; private set; }

        public Task<IReadOnlyList<ProviderBar>> SeriesAsync(string name, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
        {
            Requests++;

            return name == slow
                ? throw new TaskCanceledException("The request was canceled due to the configured HttpClient.Timeout.")
                : served.SeriesAsync(name, from, to, cancellationToken);
        }
    }

    // A market series feed answering from constructed sessions, the index around 6,000 and the VIX around 15,
    // each price saying which session it is, refusing the series it is told to and recording each series asked
    // for with its span, in the order asked.
    sealed class ConstructedSeries(IReadOnlyDictionary<string, IReadOnlyList<DateOnly>> series, IReadOnlyList<string>? refused = null) : IMarketSeriesFeed
    {
        public int Requests { get; private set; }

        public List<string> Asked { get; } = [];

        public Task<IReadOnlyList<ProviderBar>> SeriesAsync(string name, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
        {
            Requests++;
            Asked.Add($"{name} {Day(from)} {Day(to)}");

            if (refused?.Contains(name) == true)
            {
                throw new ProviderRefusal("the provider answered 404", transient: false);
            }

            return Task.FromResult<IReadOnlyList<ProviderBar>>(
                series.TryGetValue(name, out var sessions)
                    ? [.. sessions.Where(session => session >= from && session <= to).Select(session => Level(name, session))]
                    : []);
        }

        // The n-th session of the span opens at the series' base plus n, trades two either side and closes one above.
        static ProviderBar Level(string name, DateOnly session)
        {
            var open = (name == "VIX" ? 15.25m : 6000m) + Sessions(From, Tonight).ToList().IndexOf(session);

            return new ProviderBar(session, open, open + 2m, open - 2m, open + 1m, open + 1m, 0);
        }
    }

    // A historical feed answering from constructed sessions, refusing the names it is told to, and
    // recording what it was asked for in the order asked.
    sealed class ConstructedBars(IReadOnlyDictionary<string, IReadOnlyList<DateOnly>> series, IReadOnlyList<string>? refused = null) : IHistoricalBarFeed
    {
        public int Requests { get; private set; }

        public List<string> Asked { get; } = [];

        public Task<IReadOnlyList<ProviderBar>> BarsAsync(string ticker, DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
        {
            Requests++;
            Asked.Add($"{ticker} {Day(from)} {Day(to)}");

            if (refused?.Contains(ticker) == true)
            {
                throw new ProviderRefusal("the provider answered 404", transient: false);
            }

            return Task.FromResult<IReadOnlyList<ProviderBar>>(
                series.TryGetValue(ticker, out var sessions)
                    ? [.. sessions.Where(session => session >= from && session <= to).Select(Bar)]
                    : []);
        }
    }

    // An earnings calendar answering every window with the prints it holds inside it, as the provider
    // answers for every market at once, and recording each window asked.
    sealed class ConstructedPrints(IReadOnlyList<CalendarEvent> prints) : IEarningsCalendarFeed
    {
        public int Requests { get; private set; }

        public List<string> Windows { get; } = [];

        public Task<IReadOnlyList<CalendarEvent>> EventsAsync(DateOnly from, DateOnly to, CancellationToken cancellation = default)
        {
            Requests++;
            Windows.Add($"{Day(from)} {Day(to)}");

            return Task.FromResult<IReadOnlyList<CalendarEvent>>([.. prints.Where(print => print.EventDate >= from && print.EventDate <= to)]);
        }
    }
}
