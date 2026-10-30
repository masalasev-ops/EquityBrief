using System.Globalization;
using System.Net;
using System.Net.Http;
using EquityBrief.Core.Analysts;
using EquityBrief.Core.Bars;
using EquityBrief.Core.Ledger;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Readings;
using EquityBrief.Core.Sweep;
using EquityBrief.Core.Time;
using EquityBrief.Data;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.Analysts;
using EquityBrief.Worker.Filter;
using EquityBrief.Worker.Indices;
using EquityBrief.Worker.Ledger;
using EquityBrief.Worker.Shorts;
using EquityBrief.Worker.Sweep;
using Microsoft.Data.Sqlite;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 21.6: the new readings' sources. The provider's trends answer and FINRA's answers are read as sent
// from their captures; FINRA's published day is worked by hand on both sides of a closure and before the closure table's
// reach; the night's trends fetcher asks every member holding no trend in pages, stores each current year as first
// read, stops at the day's allowance, asks past a refused page and asks nothing on a night run again; the short interest
// reader keeps each settlement's reports with their published day, asks the newest held again and stores only what it
// lacked, and stops at a refusal with what it stored kept; the live feeds ask as the captures were asked, refuse an answer
// at FINRA's most records and read FINRA's empty answer as none; the ratings refresh asks the members never asked first
// and then the oldest, stops at its cap with one under it asking, and stops after three failed asks in a row; and the
// ledger reads the night's trend above a fetch's on its session and none before the first, and a short interest report
// from the session after its published day, a setup's readings rebuilt from the history cut at its session the same.
// see: A new reading joins the ledger with its history where its source keeps one, and from the night it is first stored where it keeps none
// see: The night asks every member's estimate trend once a night in requests of a page of symbols
// see: FINRA's short interest is read once a settlement after the labeller, and its history once by hand
// see: The ratings refresh asks Yahoo for the members whose stored answer is oldest or none after the labeller, under the chart page's daily cap less the page's reserve
public partial class FixtureExpectations
{
    // The rows of sections 17 and 18 21.6 adds that this check reaches.
    internal static readonly string[] NewReadingClaims =
    [
        CheckReach.Key(Scope.LimitsTable, "The ratings refresh"),
        CheckReach.Key(Scope.LimitsTable, "The night's estimate trends"),
        CheckReach.Key(Scope.LimitsTable, "FINRA's short interest"),
        CheckReach.Key(Scope.FailureTable, "A new reading's source refuses"),
        CheckReach.Key(Scope.FailureTable, "A member a new reading's source answers nothing for"),
    ];

    // The night after the close of 2026-10-30, in New York's evening.
    static readonly DateTimeOffset TrendsAsked = new(2026, 10, 30, 23, 40, 0, TimeSpan.Zero);

    [Fact]
    public void TheTrendsAnswerAndFinrasAnswersAreReadAsSentFromTheirCaptures()
    {
        // The trends answer: each symbol's current fiscal year is the 0y record with the newest end, so AAPL's +1y of a
        // later end and its 0y of the year before are passed over, and a count the answer files none of reads none.
        var trends = EstimateTrends.Parse(Captured("trends-2026-10-29.json"));

        Assert.Equal(["AAPL", "MSFT", "KEYS", "NFLX", "ROP"], trends.Select(trend => trend.Ticker));
        Assert.Equal(
            new NightTrend("AAPL", new DateOnly(2026, 9, 30), 8.8348m, 8.8279m, 8.8242m, 8.8177m, 8.7615m, 0, 2, null, 2),
            trends[0]);
        Assert.Equal(
            new NightTrend("MSFT", new DateOnly(2027, 6, 30), 19.7599m, 19.7625m, 19.7597m, 19.7531m, 19.3788m, 4, 5, null, 0),
            trends[1]);
        Assert.Equal((new DateOnly(2026, 10, 31), 11.4740m, 11.4740m, 11, 0), (trends[2].PeriodEnd, trends[2].Now!.Value, trends[2].MonthBefore!.Value, trends[2].UpMonth!.Value, trends[2].DownMonth!.Value));
        Assert.All(trends, trend => Assert.Null(trend.DownWeek));
        Assert.Throws<FormatException>(() => EstimateTrends.Parse("[]"));
        Assert.Throws<FormatException>(() => EstimateTrends.Parse("{\"type\":\"Trends\"}"));

        // FINRA's answer for AAPL over a span, every field as sent, and its settlement dates once each.
        var aapl = ShortInterests.Parse(Captured("short-interest-AAPL.json"));

        Assert.Equal(
            [new DateOnly(2026, 9, 15), new DateOnly(2026, 9, 30), new DateOnly(2026, 10, 15)],
            ShortInterests.SettlementsIn(Captured("short-interest-AAPL.json")));
        Assert.Equal(new ShortInterestReport("AAPL", new DateOnly(2026, 10, 15), 131_591_865, 35_498_663, 3.71, null), aapl[2]);
        Assert.Equal((128_753_092L, 45_135_477L, 2.85), (aapl[0].SharesShort, aapl[0].AverageVolume!.Value, aapl[0].DaysToCover!.Value));

        // A settlement's five symbols asked for some fields, the ones not asked absent and read as none, the same
        // settlement's AAPL figure as the span's; and a share class FINRA files with no separator, as its symbol for
        // the ticker joined.
        var five = ShortInterests.Parse(Captured("short-interest-2026-10-15.json"));

        Assert.Equal(["AAPL", "KEYS", "MSFT", "NFLX", "ROP"], five.Select(report => report.Symbol));
        Assert.Equal(aapl[2].SharesShort, five[0].SharesShort);
        Assert.All(five, report => Assert.Equal((new DateOnly(2026, 10, 15), (long?)null, (double?)null), (report.Settlement, report.AverageVolume, report.DaysToCover)));

        var classes = Assert.Single(ShortInterests.Parse(Captured("short-interest-classes-2026-10-15.json")));

        Assert.Equal(("BRKB", 13_769_255L), (classes.Symbol, classes.SharesShort));
        Assert.Equal("BRKB", ShortInterests.SymbolOf("BRK-B"));
        Assert.Equal("BFB", ShortInterests.SymbolOf("BF.B"));
        Assert.Throws<FormatException>(() => ShortInterests.Parse("{}"));
        Assert.Throws<FormatException>(() => ShortInterests.Parse("[{\"symbolCode\":\"AAPL\",\"settlementDate\":\"2026-10-15\"}]"));
    }

    [Fact]
    public void FinrasPublishedDayIsTheSeventhBusinessDayAfterTheSettlementWorkedByHand()
    {
        // 2026-10-15, a Thursday: the 16th, 19th, 20th, 21st, 22nd, 23rd and the 26th.
        Assert.Equal(new DateOnly(2026, 10, 26), ShortInterests.PublishedOn(new DateOnly(2026, 10, 15)));

        // 2026-11-20, a Friday: the 23rd, 24th, 25th, then Thanksgiving on the 26th, which the closure table holds, and the
        // 27th, 30th, 1 December and the 2nd; a day later than a count of weekdays alone.
        Assert.Equal(new DateOnly(2026, 12, 2), ShortInterests.PublishedOn(new DateOnly(2026, 11, 20)));

        // 2017-12-29, FINRA's first settlement, before the table covers a day: weekdays alone, the 1st of January counted, to
        // the 9th, and from the 21.6 correction of 2026-10-30 one business day later, the 10th. 2024-12-31's count starts on
        // the table's first day and is read off the table: the 2nd, 3rd, 6th, 7th, 8th, then the 9th closed, the 10th and
        // the 13th.
        Assert.Equal(new DateOnly(2018, 1, 10), ShortInterests.PublishedOn(ShortInterests.FirstSettlement));
        Assert.Equal(new DateOnly(2025, 1, 13), ShortInterests.PublishedOn(new DateOnly(2024, 12, 31)));
        Assert.Equal(new DateOnly(2024, 12, 25), ShortInterests.PublishedOn(new DateOnly(2024, 12, 13)));
    }

    // The 21.6 correction of 2026-10-30: over every settlement of the closure table's reach as FINRA dates them, the 15th or
    // the business day before it and the month's last business day, a count of weekdays alone misses the table's day by
    // at most the business days an estimate is moved later by, and by that many on at least one.
    // see: A FINRA publication day counted before the closure table's reach is moved later by the largest miss counting weekdays alone makes where the table reaches
    [Fact]
    public void AnEstimatedFinraDayIsMovedLaterByTheLargestMissCountingWeekdaysAloneMakesWhereTheTableReaches()
    {
        static bool Weekday(DateOnly day) => day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday);

        static bool Business(DateOnly day) => Weekday(day) && !ExchangeClosures.All.Contains(day);

        static DateOnly OnOrBefore(DateOnly day)
        {
            while (!Business(day))
            {
                day = day.AddDays(-1);
            }

            return day;
        }

        static DateOnly WeekdaysAfter(DateOnly from, int count)
        {
            var day = from;

            for (var counted = 0; counted < count;)
            {
                day = day.AddDays(1);
                counted += Weekday(day) ? 1 : 0;
            }

            return day;
        }

        var misses = new List<int>();

        for (var month = new DateOnly(2025, 1, 1); month.Year <= 2027; month = month.AddMonths(1))
        {
            foreach (var settlement in new[] { OnOrBefore(month.AddDays(14)), OnOrBefore(month.AddMonths(1).AddDays(-1)) })
            {
                var table = ShortInterests.PublishedOn(settlement);

                if (table > ExchangeClosures.CoveredThrough)
                {
                    continue;
                }

                var alone = WeekdaysAfter(settlement, ShortInterests.PublishedAfter);
                var miss = 0;

                for (var day = alone.AddDays(1); day <= table; day = day.AddDays(1))
                {
                    miss += Weekday(day) ? 1 : 0;
                }

                misses.Add(miss);
            }
        }

        // 72 settlements over three years, December 2027's month end published past the table's reach and left out.
        Assert.Equal(71, misses.Count);
        Assert.Equal(ShortInterests.EstimatedLate, misses.Max());

        // A settlement before the reach is counted on weekdays alone and moved that many weekdays later.
        var before = new DateOnly(2024, 11, 15);

        Assert.Equal(WeekdaysAfter(before, ShortInterests.PublishedAfter + ShortInterests.EstimatedLate), ShortInterests.PublishedOn(before));
        Assert.Equal(new DateOnly(2024, 11, 27), ShortInterests.PublishedOn(before));
    }

    // A trends feed answering from a capture for the symbols it holds and a constructed current year for the rest but each
    // tenth, which it answers nothing for, refusing the page of the request numbers given.
    sealed class ConstructedTrendsFeed(IReadOnlyList<NightTrend> captured, params int[] refused) : IEstimateTrendsFeed
    {
        public int Requests { get; private set; }

        public int Symbols { get; private set; }

        public List<IReadOnlyList<string>> Pages { get; } = [];

        public Task<IReadOnlyList<NightTrend>> TrendsAsync(IReadOnlyList<string> tickers, CancellationToken cancellation = default)
        {
            Requests++;
            Symbols += tickers.Count;
            Pages.Add(tickers);

            if (refused.Contains(Requests))
            {
                throw new ProviderRefusal("The provider answered 500 for the page.", transient: true);
            }

            IReadOnlyList<NightTrend> answer =
            [
                .. tickers
                    .Select(ticker => captured.FirstOrDefault(trend => trend.Ticker == ticker)
                        ?? (ticker.StartsWith('T') && int.Parse(ticker[1..], CultureInfo.InvariantCulture) % 10 != 0
                            ? new NightTrend(ticker, new DateOnly(2026, 12, 31), 2.10m, 2.08m, 2.00m, 1.95m, 1.90m, 1, 3, 0, 1)
                            : null))
                    .OfType<NightTrend>(),
            ];

            return Task.FromResult(answer);
        }
    }

    // A store holding a session's bar and the three indices' members on it: the five captured stocks and T000 to T099,
    // a member that left the day before the night and one joining the day after, neither asked.
    static TemporaryStore TrendsStore()
    {
        var store = new TemporaryStore().Migrated();
        var members = new[] { "AAPL", "MSFT", "KEYS", "NFLX", "ROP" }.Concat(Enumerable.Range(0, 100).Select(at => FormattableString.Invariant($"T{at:000}"))).ToArray();
        var indices = new[] { "GSPC", "MID", "SML" };

        store.Execute(
            "INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at, raw_close) VALUES ('AAPL', '2026-10-30', '270', '272', '268', '271', 1000, 'bulk', '2026-10-30T21:10:00Z', '271');"
            + "INSERT INTO membership (index_code, ticker, joined, \"left\", observed_at) VALUES "
            + string.Join(", ", members.Select((ticker, at) => $"('{indices[at % 3]}', '{ticker}', NULL, NULL, '2026-10-30T21:10:00Z')"))
            + ", ('GSPC', 'GONE', NULL, '2026-10-29', '2026-10-30T21:10:00Z'), ('SML', 'SOON', '2026-11-02', NULL, '2026-10-30T21:10:00Z');");

        return store;
    }

    [Fact]
    public async Task TheTrendsFetcherAsksEveryMemberHoldingNoTrendInPagesAndStoresEachCurrentYearAsFirstRead()
    {
        using var store = TrendsStore();
        var clock = FixedClock.At(TrendsAsked, SessionZones.UnitedStates);
        var captured = EstimateTrends.Parse(Captured("trends-2026-10-29.json"));

        // T000 holds the night's trend already, so 104 of the 105 members are asked, in pages of 50, 50 and 4.
        store.Execute("INSERT INTO estimate_trend_night (ticker, session_date, period_end, eps_now, run_id) VALUES ('T000', '2026-10-30', '2026-12-31', '9.99', 'night-earlier');");

        var feed = new ConstructedTrendsFeed(captured);
        var outcome = await new TrendsFetcher(feed, () => feed.Symbols * ProviderWeights.EstimateTrends, clock, store.DatabaseFile).RunAsync("night-a");

        Assert.Equal([EstimateTrends.PageSymbols, EstimateTrends.PageSymbols, 4], feed.Pages.Select(page => page.Count));
        Assert.DoesNotContain(feed.Pages.SelectMany(page => page), ticker => ticker is "T000" or "GONE" or "SOON");

        // Of the 104 asked, T010 to T090 by tens answer nothing: 95 stored, 9 counted unanswered.
        Assert.Equal(new TrendsOutcome(105, 104, 95, 9, 0, 3, 104, null), outcome);
        Assert.Equal(["8.8348|8.8242|2026-09-30|2|2|null"], Query(store, "SELECT eps_now, eps_thirty_days_ago, period_end, up_last_thirty_days, down_last_thirty_days, down_last_seven_days FROM estimate_trend_night WHERE ticker = 'AAPL';"));
        Assert.Equal(["9.99"], Query(store, "SELECT eps_now FROM estimate_trend_night WHERE ticker = 'T000';"));
        Assert.Equal(["96"], Query(store, "SELECT COUNT(*) FROM estimate_trend_night WHERE session_date = '2026-10-30';"));
        Assert.Equal(["trends|ok|95|3"], Query(store, "SELECT stage, outcome, rows_written, network_requests FROM run_log WHERE run_id = 'night-a';"));

        // Asked again the same night, every member holding a trend asks nothing and the nine unanswered are asked again.
        var again = new ConstructedTrendsFeed(captured);
        var second = await new TrendsFetcher(again, () => again.Symbols, clock, store.DatabaseFile).RunAsync("night-b");

        Assert.Equal((9, 0, 9, 1), (second.Asked, second.Stored, second.Unanswered, second.Requests));
    }

    [Fact]
    public async Task TheTrendsFetcherStopsAtTheDaysAllowanceAsksPastARefusedPageAndAsksNothingOnANightRunAgain()
    {
        var clock = FixedClock.At(TrendsAsked, SessionZones.UnitedStates);
        var captured = EstimateTrends.Parse(Captured("trends-2026-10-29.json"));

        // The night at 99,900 weighted calls: the first page reaches 99,950 and the second the allowance itself, both asked,
        // and the third, 5 more, would pass it and is not asked.
        using (var store = TrendsStore())
        {
            var feed = new ConstructedTrendsFeed(captured);
            var outcome = await new TrendsFetcher(feed, () => 99_900 + feed.Symbols, clock, store.DatabaseFile).RunAsync("night-a");

            Assert.Equal((2, 100, 5), (outcome.Requests, outcome.Asked, outcome.NotAsked));
            Assert.Contains("5 not asked since their ask would take the night past the day's allowance of 100000", Query(store, "SELECT detail FROM run_log WHERE run_id = 'night-a';")[0], StringComparison.Ordinal);
        }

        // A refused second page, T045 to T094, stores nothing for its 50, counted unanswered beside the first page's T000 to
        // T040 by tens, and the third is still asked.
        using (var store = TrendsStore())
        {
            var feed = new ConstructedTrendsFeed(captured, 2);
            var outcome = await new TrendsFetcher(feed, () => feed.Symbols, clock, store.DatabaseFile).RunAsync("night-a");

            Assert.Equal((3, 105, 50 + 5), (outcome.Requests, outcome.Asked, outcome.Unanswered));
            Assert.NotNull(outcome.Refused);
            Assert.Equal(["trends|partial"], Query(store, "SELECT stage, outcome FROM run_log WHERE run_id = 'night-a';"));
        }

        // A night run again for an earlier session is handed no feed and asks for none.
        using (var store = TrendsStore())
        {
            var outcome = await new TrendsFetcher(null, () => 0, clock, store.DatabaseFile).RunAsync("night-a");

            Assert.Equal((105, 0, 105, 0), (outcome.Members, outcome.Asked, outcome.NotAsked, outcome.Requests));
            Assert.Equal(["0"], Query(store, "SELECT COUNT(*) FROM estimate_trend_night;"));
        }
    }

    // FINRA as the captures answered: AAPL's settlements of the span its reference asks, and each settlement's records for
    // the symbols asked, AAPL's from its span and the five's and the class's at 2026-10-15, refusing the settlement given.
    sealed class ConstructedShortInterestFeed(DateOnly? refusedAt = null) : IShortInterestFeed
    {
        public int Requests { get; private set; }

        public List<(DateOnly From, DateOnly Through)> Spans { get; } = [];

        readonly IReadOnlyList<ShortInterestReport> held =
        [
            .. ShortInterests.Parse(Captured("short-interest-AAPL.json")),
            .. ShortInterests.Parse(Captured("short-interest-2026-10-15.json")).Where(report => report.Symbol != "AAPL"),
            .. ShortInterests.Parse(Captured("short-interest-classes-2026-10-15.json")),
        ];

        public Task<IReadOnlyList<DateOnly>> SettlementsAsync(string symbol, DateOnly from, DateOnly through, CancellationToken cancellation = default)
        {
            Requests++;
            Spans.Add((from, through));

            IReadOnlyList<DateOnly> settlements = [.. held.Where(report => report.Symbol == symbol && report.Settlement >= from && report.Settlement <= through).Select(report => report.Settlement).Distinct().Order()];

            return Task.FromResult(settlements);
        }

        public Task<IReadOnlyList<ShortInterestReport>> SettlementAsync(DateOnly settlement, IReadOnlyList<string> symbols, CancellationToken cancellation = default)
        {
            Requests++;

            if (settlement == refusedAt)
            {
                throw new ProviderRefusal("FINRA's short interest answered 503 for the settlement.", transient: true);
            }

            IReadOnlyList<ShortInterestReport> answer = [.. held.Where(report => report.Settlement == settlement && symbols.Contains(report.Symbol))];

            return Task.FromResult(answer);
        }
    }

    // A store holding AAPL and BRK-B in the S&P 500, KEYS in the 400, and XYZ, which left the 500 on 2026-09-20 and FINRA
    // answers nothing for.
    static TemporaryStore ShortInterestStore()
    {
        var store = new TemporaryStore().Migrated();

        store.Execute(
            "INSERT INTO membership (index_code, ticker, joined, \"left\", observed_at) VALUES "
            + "('GSPC', 'AAPL', NULL, NULL, '2026-10-30T21:10:00Z'), ('GSPC', 'BRK-B', NULL, NULL, '2026-10-30T21:10:00Z'), "
            + "('MID', 'KEYS', NULL, NULL, '2026-10-30T21:10:00Z'), ('GSPC', 'XYZ', NULL, '2026-09-20', '2026-10-30T21:10:00Z');");

        return store;
    }

    // The off-peak run's first slot of 2026-10-30 in New York.
    static readonly DateTimeOffset ShortInterestRead = new(2026, 10, 30, 4, 20, 0, TimeSpan.Zero);

    // The 21.6 correction of 2026-10-30: a run first moves every stored settlement to the day the rule gives, a report
    // stored on a weekday count alone before the closure table's reach moved one business day later, and one stored on the
    // rule's day left alone.
    [Fact]
    public async Task TheShortInterestReaderMovesAStoredSettlementToTheDayTheRuleGivesBeforeItReads()
    {
        using var store = ShortInterestStore();

        store.Execute(
            "INSERT INTO short_interest (ticker, settlement_date, published_on, shares_short, average_volume, days_to_cover, revision, observed_at, run_id) VALUES "
            + "('AAPL', '2017-12-29', '2018-01-09', 100, NULL, NULL, NULL, '2026-10-30T13:00:00Z', 'short-interest-history-20261030T130000Z'), "
            + "('KEYS', '2017-12-29', '2018-01-09', 200, NULL, NULL, NULL, '2026-10-30T13:00:00Z', 'short-interest-history-20261030T130000Z'), "
            + "('AAPL', '2026-09-30', '2026-10-09', 300, NULL, NULL, NULL, '2026-10-30T13:00:00Z', 'short-interest-20261030T130000Z');");

        var outcome = await new ShortInterestReader(FixedClock.At(ShortInterestRead, SessionZones.UnitedStates), store.DatabaseFile).RunAsync(new ConstructedShortInterestFeed(), history: false);

        Assert.Equal(2, outcome.Restated);
        Assert.Equal(
            ["AAPL|2017-12-29|2018-01-10|100", "AAPL|2026-09-30|2026-10-09|300", "KEYS|2017-12-29|2018-01-10|200"],
            Query(store, "SELECT ticker, settlement_date, published_on, shares_short FROM short_interest WHERE settlement_date IN ('2017-12-29', '2026-09-30') ORDER BY ticker, settlement_date;"));
        Assert.Contains("; 2 stored report(s) moved to the day FINRA's publication is counted on", Query(store, "SELECT detail FROM run_log WHERE stage = 'short-interest';").Single(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheShortInterestReaderKeepsEachSettlementsReportsWithTheDayFinraPublishedThemAndAReportAsFirstRead()
    {
        using var store = ShortInterestStore();
        var clock = FixedClock.At(ShortInterestRead, SessionZones.UnitedStates);
        var feed = new ConstructedShortInterestFeed();

        // Nothing stored: the settlements of the 45 days to 2026-10-30, from 2026-09-15, each asking every stock held on or
        // after it, XYZ at 2026-09-15 alone; AAPL answered at all three, KEYS and BRK-B at 2026-10-15 alone, XYZ never.
        var first = await new ShortInterestReader(clock, store.DatabaseFile).RunAsync(feed, history: false);

        Assert.Equal([(new DateOnly(2026, 9, 15), new DateOnly(2026, 10, 30))], feed.Spans);
        Assert.Equal(new ShortInterestOutcome(3, 4 + 3 + 3, 5, 5, 4, null), first);
        Assert.Equal(
            [
                "AAPL|2026-09-15|2026-09-24|128753092|45135477|2.85",
                "AAPL|2026-09-30|2026-10-09|120763539|39858260|3.03",
                "AAPL|2026-10-15|2026-10-26|131591865|35498663|3.71",
                "BRK-B|2026-10-15|2026-10-26|13769255|null|null",
                "KEYS|2026-10-15|2026-10-26|2164006|null|null",
            ],
            Query(store, "SELECT ticker, settlement_date, published_on, shares_short, average_volume, days_to_cover FROM short_interest ORDER BY ticker, settlement_date;").Select(row => row.Replace(',', '.')));

        // Run again: the newest settlement held is asked again and stores nothing it held already.
        var again = new ConstructedShortInterestFeed();
        var second = await new ShortInterestReader(FixedClock.At(ShortInterestRead.AddHours(6), SessionZones.UnitedStates), store.DatabaseFile).RunAsync(again, history: false);

        Assert.Equal([(new DateOnly(2026, 10, 15), new DateOnly(2026, 10, 30))], again.Spans);
        Assert.Equal((1, 0), (second.Settlements, second.Stored));
        Assert.Equal(["5"], Query(store, "SELECT COUNT(*) FROM short_interest;"));

        // The history from FINRA's first settlement.
        var history = new ConstructedShortInterestFeed();

        await new ShortInterestReader(clock, store.DatabaseFile).RunAsync(history, history: true);

        Assert.Equal(ShortInterests.FirstSettlement, Assert.Single(history.Spans).From);
        Assert.Equal(
            ["short-interest-20261030T042000Z|5|4", "short-interest-history-20261030T042000Z|0|4", "short-interest-20261030T102000Z|0|2"],
            Query(store, "SELECT run_id, rows_written, network_requests FROM run_log WHERE stage = 'short-interest' AND outcome = 'ok' ORDER BY started_at, run_id;"));
    }

    [Fact]
    public async Task AFinraRefusalStopsTheReadWithWhatItStoredKeptAndSaysWhyOnItsRow()
    {
        using var store = ShortInterestStore();
        var outcome = await new ShortInterestReader(FixedClock.At(ShortInterestRead, SessionZones.UnitedStates), store.DatabaseFile)
            .RunAsync(new ConstructedShortInterestFeed(new DateOnly(2026, 9, 30)), history: false);

        Assert.Equal((1, 1), (outcome.Settlements, outcome.Stored));
        Assert.Equal("FINRA's short interest answered 503 for the settlement.", outcome.Refused);
        Assert.Equal(["AAPL|2026-09-15"], Query(store, "SELECT ticker, settlement_date FROM short_interest;"));

        var row = Assert.Single(Query(store, "SELECT outcome, detail FROM run_log;"));

        Assert.StartsWith("partial|the newest settlements from 2026-09-15 through 2026-10-30: 1 settlement(s) read", row, StringComparison.Ordinal);
        Assert.EndsWith("; stopped, FINRA's short interest answered 503 for the settlement.", row, StringComparison.Ordinal);
    }

    // A server answering every request with one status and body, keeping each request's address and body.
    sealed class AnsweringHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public List<(string Address, string? Body)> Asked { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Asked.Add((request.RequestUri!.ToString(), request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken)));

            return new HttpResponseMessage(status) { Content = new StringContent(body) };
        }
    }

    [Fact]
    public async Task TheLiveFeedsAskAsTheCapturesWereAskedReadFinrasEmptyAnswerAsNoneAndRefuseAnAnswerAtItsMostRecords()
    {
        // The provider's trends: one request for the page, each symbol with the exchange's suffix, answered from the capture.
        var trendsServer = new AnsweringHandler(HttpStatusCode.OK, Captured("trends-2026-10-29.json"));
        var trends = new EodhdEstimateTrendsFeed(new HttpClient(trendsServer) { BaseAddress = new Uri("https://eodhd.example/api/") }, new ProviderCredentials("constructed-key"));

        Assert.Equal(5, (await trends.TrendsAsync(["AAPL", "BRK-B"])).Count);
        Assert.Equal((1, 2), (trends.Requests, trends.Symbols));
        Assert.StartsWith("https://eodhd.example/api/calendar/trends?symbols=AAPL.US,BRK-B.US&fmt=json", Assert.Single(trendsServer.Asked).Address, StringComparison.Ordinal);

        // FINRA: a settlement's symbols asked in the body as the capture's were, the settlement compared equal.
        var finraServer = new AnsweringHandler(HttpStatusCode.OK, Captured("short-interest-2026-10-15.json"));
        var finra = new FinraShortInterestFeed(new HttpClient(finraServer) { BaseAddress = new Uri(FinraShortInterestFeed.BaseAddress) });

        Assert.Equal(5, (await finra.SettlementAsync(new DateOnly(2026, 10, 15), ["AAPL", "MSFT", "KEYS", "NFLX", "ROP"])).Count);

        var (address, body) = Assert.Single(finraServer.Asked);

        Assert.Equal(FinraShortInterestFeed.BaseAddress + FinraShortInterestFeed.Endpoint, address);
        Assert.Contains("\"domainFilters\":[{\"fieldName\":\"symbolCode\",\"values\":[\"AAPL\",\"MSFT\",\"KEYS\",\"NFLX\",\"ROP\"]}]", body, StringComparison.Ordinal);
        Assert.Contains("\"compareFilters\":[{\"compareType\":\"EQUAL\",\"fieldName\":\"settlementDate\",\"fieldValue\":\"2026-10-15\"}]", body, StringComparison.Ordinal);

        // A query matching nothing is answered 204 with no body, read as none.
        var empty = new FinraShortInterestFeed(new HttpClient(new AnsweringHandler(HttpStatusCode.NoContent, string.Empty)) { BaseAddress = new Uri(FinraShortInterestFeed.BaseAddress) });

        Assert.Empty(await empty.SettlementAsync(new DateOnly(2026, 10, 15), ["ZZZZZQ"]));

        // An answer holding FINRA's most records is refused, since it may hold fewer than were asked for.
        var full = "[" + string.Join(",", Enumerable.Range(0, ShortInterests.RecordsAnAnswer).Select(at => FormattableString.Invariant($"{{\"symbolCode\":\"S{at}\",\"settlementDate\":\"2026-10-15\",\"currentShortPositionQuantity\":1}}"))) + "]";
        var capped = new FinraShortInterestFeed(new HttpClient(new AnsweringHandler(HttpStatusCode.OK, full)) { BaseAddress = new Uri(FinraShortInterestFeed.BaseAddress) });
        var refused = await Assert.ThrowsAsync<ProviderRefusal>(() => capped.SettlementAsync(new DateOnly(2026, 10, 15), ["AAPL"]));

        Assert.Contains("held 5000 records, its most an answer", refused.Message, StringComparison.Ordinal);
        await Assert.ThrowsAsync<ArgumentException>(() => capped.SettlementAsync(new DateOnly(2026, 10, 15), [.. Enumerable.Range(0, ShortInterests.SymbolsARequest + 1).Select(at => FormattableString.Invariant($"S{at}"))]));
    }

    // A store holding a session's bar and four members, BBB and DDD asked of Yahoo before and CCC holding an answer older
    // than any ask the run log keeps, and the day's asks of other stocks given.
    static TemporaryStore RefreshStore(int askedToday)
    {
        var store = new TemporaryStore().Migrated();

        store.Execute(
            "INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at, raw_close) VALUES ('AAA', '2026-10-30', '10', '10', '10', '10', 1000, 'bulk', '2026-10-30T21:10:00Z', '10');"
            + "INSERT INTO membership (index_code, ticker, joined, \"left\", observed_at) VALUES "
            + "('GSPC', 'AAA', NULL, NULL, '2026-10-30T21:10:00Z'), ('MID', 'BBB', NULL, NULL, '2026-10-30T21:10:00Z'), ('SML', 'CCC', NULL, NULL, '2026-10-30T21:10:00Z'), ('GSPC', 'DDD', NULL, NULL, '2026-10-30T21:10:00Z'), ('GSPC', 'EEE', NULL, '2026-10-01', '2026-10-30T21:10:00Z');"
            + "INSERT INTO run_log (run_id, stage, started_at, ended_at, outcome, rows_written, model_calls, network_requests, spend, detail) VALUES "
            + "('analysts-20261001T150000Z-BBB', 'analysts', '2026-10-01T15:00:00Z', '2026-10-01T15:00:02Z', 'answered', 1, 0, 3, '0', '{\"ticker\":\"BBB\"}'),"
            + "('analysts-20261020T150000Z-DDD', 'analysts', '2026-10-20T15:00:00Z', '2026-10-20T15:00:02Z', 'no analysts', 0, 0, 3, '0', '{\"ticker\":\"DDD\"}');"
            + "INSERT INTO analyst_view (ticker, asked_at, answered_at, months, actions) VALUES ('CCC', '2026-10-25T15:00:00Z', '2026-10-25T15:00:02Z', '[]', '[]');");

        for (var at = 0; at < askedToday; at++)
        {
            store.Execute(FormattableString.Invariant($"INSERT INTO run_log (run_id, stage, started_at, ended_at, outcome, rows_written, model_calls, network_requests, spend, detail) VALUES ('analysts-page-{at}', 'analysts', '2026-10-31T04:0{at % 5}:00Z', '2026-10-31T04:05:00Z', 'answered', 1, 0, 3, '0', '{{\"ticker\":\"Z{at:00}\"}}');"));
        }

        return store;
    }

    // The off-peak run's first slot of 2026-10-31, six minutes into the New York day.
    static readonly DateTimeOffset RefreshAt = new(2026, 10, 31, 4, 6, 0, TimeSpan.Zero);

    static readonly YahooAnalystAnswer RefreshAnswer = new("USD", 10m, 12m, 12m, 14m, 9, "buy", [], []);

    [Fact]
    public async Task TheRatingsRefreshAsksTheMembersNeverAskedFirstThenTheOldestAndStopsAtItsCapWithOneUnderItAsking()
    {
        // The turn worked by hand: AAA never asked, then BBB asked on 1 October, DDD on the 20th and CCC answered on the 25th.
        Assert.Equal(
            ["AAA", "BBB", "DDD", "CCC"],
            RatingsRefresh.InTurn(["AAA", "BBB", "CCC", "DDD"], new Dictionary<string, string>(StringComparer.Ordinal) { ["BBB"] = "2026-10-01T15:00:00Z", ["DDD"] = "2026-10-20T15:00:02Z", ["CCC"] = "2026-10-25T15:00:00Z" }));

        // Under the cap by four: the four members asked in turn, EEE, which left the index, not, with a pause between asks.
        using (var store = RefreshStore(AnalystLimits.RefreshCap - 4))
        {
            var pauses = 0;
            var outcome = await new RatingsRefresh(FixedClock.At(RefreshAt, SessionZones.UnitedStates), store.DatabaseFile, () => new ConstructedAnalystFeed(RefreshAnswer), (_, _) => { pauses++; return Task.CompletedTask; }).RunAsync();

            Assert.Equal(new RefreshOutcome(4, 4, 4, 0, 0, RatingsRefresh.AllAsked, 4), outcome);
            Assert.Equal(3, pauses);
            Assert.Equal(["AAA", "BBB", "DDD", "CCC"], Query(store, "SELECT ticker FROM analyst_view WHERE asked_at >= '2026-10-31' ORDER BY rowid;"));
        }

        // One under the cap: one ask, AAA's, and the run stops at the cap.
        using (var store = RefreshStore(AnalystLimits.RefreshCap - 1))
        {
            var outcome = await new RatingsRefresh(FixedClock.At(RefreshAt, SessionZones.UnitedStates), store.DatabaseFile, () => new ConstructedAnalystFeed(RefreshAnswer), (_, _) => Task.CompletedTask).RunAsync();

            Assert.Equal((1, RatingsRefresh.AtTheCap), (outcome.Asked, outcome.Stopped));
            Assert.Equal(["AAA"], Query(store, "SELECT ticker FROM analyst_view WHERE asked_at >= '2026-10-31';"));

            var row = Assert.Single(Query(store, "SELECT outcome, rows_written, network_requests FROM run_log WHERE stage = 'analysts-refresh';"));

            Assert.Equal("ok|1|1", row);
        }

        // At the cap: nothing asked.
        using (var store = RefreshStore(AnalystLimits.RefreshCap))
        {
            var outcome = await new RatingsRefresh(FixedClock.At(RefreshAt, SessionZones.UnitedStates), store.DatabaseFile, () => throw new InvalidOperationException("no feed is built at the cap"), (_, _) => Task.CompletedTask).RunAsync();

            Assert.Equal((4, 0, RatingsRefresh.AtTheCap), (outcome.Members, outcome.Asked, outcome.Stopped));
        }
    }

    [Fact]
    public async Task TheRatingsRefreshStopsAfterThreeFailedAsksInARowAndAMemberWithNoAnalystsWaitsItsTurn()
    {
        using var store = RefreshStore(0);
        var refusing = new ProviderRefusal("Yahoo's crumb answered 429.", transient: true);
        var outcome = await new RatingsRefresh(FixedClock.At(RefreshAt, SessionZones.UnitedStates), store.DatabaseFile, () => new ConstructedAnalystFeed(null, refusing), (_, _) => Task.CompletedTask).RunAsync();

        Assert.Equal((3, 3, RatingsRefresh.Refused), (outcome.Asked, outcome.Failed, outcome.Stopped));
        Assert.Equal(["stopped"], Query(store, "SELECT outcome FROM run_log WHERE stage = 'analysts-refresh';"));

        // The next run asks first CCC, which the refusals did not reach, then the three they did, whose newest ask is now
        // the failed one of the morning, by ticker; Yahoo holding no analysts for any, each stores nothing and waits its
        // next turn.
        var stocks = await new RatingsRefresh(FixedClock.At(RefreshAt.AddHours(6), SessionZones.UnitedStates), store.DatabaseFile, () => new ConstructedAnalystFeed(null), (_, _) => Task.CompletedTask).RunAsync();

        Assert.Equal((4, 0, 4, RatingsRefresh.AllAsked), (stocks.Asked, stocks.Answered, stocks.NoAnalysts, stocks.Stopped));
        Assert.Equal(
            ["CCC", "AAA", "BBB", "DDD"],
            Query(store, "SELECT json_extract(detail, '$.ticker') FROM run_log WHERE stage = 'analysts' AND started_at >= '2026-10-31T10' ORDER BY rowid;"));
        Assert.Equal(["0"], Query(store, "SELECT COUNT(*) FROM analyst_view WHERE asked_at >= '2026-10-31';"));
    }

    [Fact]
    public async Task TheLedgerReadsTheNightsTrendAboveAFetchsOnItsSessionNoneBeforeTheFirstAndShortInterestFromTheSessionAfterItsPublishedDay()
    {
        using var store = new TemporaryStore().Migrated();

        // A quarters fetch on the evening of 6 October filing AAA's year at 1.05 against 1.00 thirty days before, the
        // night's own ask that night at 2.20 against 2.00 and on the 8th at 2.40 against 2.00; and FINRA's settlements of
        // 30 September and 15 October, published on 9 and 26 October.
        store.Execute(
            "INSERT INTO estimate_trend (ticker, fetched_at, period, period_end, eps_now, eps_thirty_days_ago) VALUES ('AAA', '2026-10-06T22:00:00Z', '0y', '2026-12-31', '1.05', '1.00'), ('AAA', '2026-10-01T22:00:00Z', '0q', '2026-12-31', '5.00', '1.00');"
            + "INSERT INTO estimate_trend_night (ticker, session_date, period_end, eps_now, eps_thirty_days_ago, run_id) VALUES ('AAA', '2026-10-06', '2026-12-31', '2.20', '2.00', 'night-a'), ('AAA', '2026-10-08', '2026-12-31', '2.40', '2.00', 'night-b');"
            + "INSERT INTO short_interest (ticker, settlement_date, published_on, shares_short, observed_at, run_id) VALUES "
            + "('AAA', '2026-09-30', '2026-10-09', 1000000, '2026-10-30T12:00:00Z', 'short-interest-history-x'), ('AAA', '2026-10-15', '2026-10-26', 1500000, '2026-10-30T12:00:00Z', 'short-interest-history-x');");

        await using var connection = new SqliteConnection(StoreConnection.For(store.DatabaseFile));
        await connection.OpenAsync();

        var trends = await SetupLedger.TrendsAsync(connection, ["AAA", "BBB"], CancellationToken.None);
        var shortInterest = await SetupLedger.ShortInterestAsync(connection, ["AAA", "BBB"], CancellationToken.None);

        Assert.Equal(
            [new LedgerTrend(new DateOnly(2026, 10, 6), 1.05m, 1.00m), new LedgerTrend(new DateOnly(2026, 10, 6), 2.20m, 2.00m), new LedgerTrend(new DateOnly(2026, 10, 8), 2.40m, 2.00m)],
            trends["AAA"]);
        Assert.False(trends.ContainsKey("BBB"));
        Assert.Equal([new LedgerShortInterest(new DateOnly(2026, 10, 9), 1_000_000m), new LedgerShortInterest(new DateOnly(2026, 10, 26), 1_500_000m)], shortInterest["AAA"]);

        // The revision: none before the first, the night's on the session holding both, and on through the next night's.
        var context = new LedgerContext(NoIncome, NoSectors, [], [], new Dictionary<string, IReadOnlyList<FiledFactRow>>(StringComparer.Ordinal)
        {
            ["AAA"] = [new FiledFactRow(FiledFacts.SharesOutstanding[0], new DateOnly(2026, 6, 30), new DateOnly(2026, 6, 30), 10_000_000m, new DateOnly(2026, 8, 1), "10-Q", "q-1")],
        })
        {
            Trends = trends,
            ShortInterest = shortInterest,
        };

        double? Revision(int day) => context.EstimateRevision("AAA", new DateOnly(2026, 10, day)) is { } value ? Math.Round(value, 12) : null;

        Assert.Equal([null, 0.1, 0.1, 0.2], new[] { 5, 6, 7, 8 }.Select(Revision));
        Assert.Null(context.EstimateRevision("BBB", new DateOnly(2026, 10, 8)));

        // The short interest over the covers' ten million: none on the day the first was published, a tenth from the
        // session after it to the day the second was published, and fifteen hundredths after that.
        var sessions = new[] { new DateOnly(2026, 10, 9), new DateOnly(2026, 10, 12), new DateOnly(2026, 10, 26), new DateOnly(2026, 10, 27) };
        var sessionAt = sessions.Select((day, at) => (day, at)).ToDictionary(pair => pair.day, pair => pair.at);
        var series = SweepColumns.Series(new SweepName("AAA", [.. sessions.Select(day => new SweepBar(day, 10m, 10m, 10m, 1000, 10m, 10m))], [(null, null)], []), sessionAt);

        Assert.Equal(
            [null, 0.1, 0.1, 0.15],
            sessions.Select(day => context.ShortInterestShare(series, day) is { } share ? Math.Round(share, 12) : (double?)null));
    }

    // The shipped sources 21.6 writes outside their comments. The ledger's readings file it touches gains comment lines
    // alone, which no pin reads.
    // see: A comment or a blank line moves no pin, and every other change to a pinned source does
    static readonly string[] NewReadingSources =
    [
        "src/EquityBrief.Api/Program.cs",
        "src/EquityBrief.Api/Reading/ReadApi.Ledger.cs",
        "src/EquityBrief.Api/Reading/ReadApi.cs",
        "src/EquityBrief.Api/Reading/RunScreen.cs",
        "src/EquityBrief.Core/Analysts/AnalystLimits.cs",
        "src/EquityBrief.Core/Components/ComponentAccess.cs",
        "src/EquityBrief.Core/Providers/EodhdEstimateTrendsFeed.cs",
        "src/EquityBrief.Core/Providers/EodhdQuery.cs",
        "src/EquityBrief.Core/Providers/FinraShortInterestFeed.cs",
        "src/EquityBrief.Core/Providers/IEstimateTrendsFeed.cs",
        "src/EquityBrief.Core/Providers/IShortInterestFeed.cs",
        "src/EquityBrief.Data/Migrations/SchemaMigrations.cs",
        "src/EquityBrief.Web/App/SinglePageApp.cs",
        "src/EquityBrief.Web/Marks/MarkRenderer.Ledger.cs",
        "src/EquityBrief.Worker/Analysts/RatingsRefresh.cs",
        "src/EquityBrief.Worker/Filter/TrendsFetcher.cs",
        "src/EquityBrief.Worker/Ledger/LedgerPointInTime.cs",
        "src/EquityBrief.Worker/Ledger/SetupLedger.cs",
        "src/EquityBrief.Worker/NightFeeds.cs",
        "src/EquityBrief.Worker/Nightly.cs",
        "src/EquityBrief.Worker/Program.cs",
        "src/EquityBrief.Worker/Shorts/ShortInterestReader.cs",
    ];

    // No source a rule's version pins is one 21.6 writes outside its comments, so no rule's version moves, the reader shown
    // to find a source a rule pins.
    [Fact]
    public void NoSourceARulesVersionPinsIsOneTheNewReadingsSourcesWrite()
    {
        var pinned = Core.Candidates.CandidateEvaluators.All
            .SelectMany(evaluator => evaluator.OwnSources)
            .Concat(Core.Candidates.CandidateEvaluator.EvaluationSources)
            .Concat(Core.Loop.RuleHooks.Sources)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.True(pinned.Length >= 30, $"Read {pinned.Length} pinned source(s), expected at least 30.");
        Assert.All(NewReadingSources, path => Assert.True(File.Exists(Path.Combine(Repository.Root, path)), path));
        Assert.Empty(NewReadingSources.Intersect(pinned, StringComparer.Ordinal));
        Assert.Contains("src/EquityBrief.Core/Ledger/LedgerReadings.cs", pinned);
    }

    [Fact]
    public void ASetupsTrendAndShortInterestReadingsRebuiltFromTheHistoryCutAtItsSessionAreTheStoredOnes()
    {
        var (calendar, inputs, market) = PointInTimeHistory();
        var at = 60;
        var session = calendar[at];

        // The night's trends on the session before and on the session after, and a report published the session before
        // the setup's and one published on it, with the covers' count filed long before.
        var trends = new Dictionary<string, IReadOnlyList<LedgerTrend>>(StringComparer.Ordinal)
        {
            ["AAA"] = [new(calendar[at - 1], 3.30m, 3.00m), new(calendar[at + 1], 9.90m, 3.00m)],
        };
        var shortInterest = new Dictionary<string, IReadOnlyList<LedgerShortInterest>>(StringComparer.Ordinal)
        {
            ["AAA"] = [new(calendar[at - 1], 2_000_000m), new(session, 4_000_000m)],
        };
        var facts = new Dictionary<string, IReadOnlyList<FiledFactRow>>(StringComparer.Ordinal)
        {
            ["AAA"] = [new FiledFactRow(FiledFacts.SharesOutstanding[0], calendar[5], calendar[5], 20_000_000m, calendar[10], "10-Q", "q-1")],
        };
        var sessionAt = calendar.Select((day, index) => (day, index)).ToDictionary(pair => pair.day, pair => pair.index);
        var series = inputs.Names.Select(name => SweepColumns.Series(name, sessionAt)).ToArray();
        var columns = SweepColumns.Sessions(series, inputs.Sessions);
        var members = SweepBenchmark.On(series, calendar.Count);
        var (highs, lows) = SweepIdeas.HighsAndLows(series, members, calendar.Count);
        var whole = LedgerSetups.Readings(
            IndexFamilies.LargeIndex,
            0,
            series[0],
            [.. series[0].Bars.Select(bar => Statistic.FromPrice(bar.Close))],
            IndexNightRead.BarOf(series[0], at),
            columns[at],
            members,
            calendar,
            at,
            market,
            new LedgerContext(NoIncome, NoSectors, highs, lows, facts) { Ranks = new FactorRanks(series, members), Trends = trends, ShortInterest = shortInterest });
        var rebuilt = LedgerPointInTime.Rebuilt(
            IndexFamilies.LargeIndex,
            LedgerPointInTime.Cut(inputs, session),
            LedgerPointInTime.Cut(market, session),
            NoIncome,
            NoSectors,
            facts,
            "AAA",
            trends: trends,
            shortInterest: shortInterest)!;

        // The revision is the session before's, 3.30 over 3.00, and the short interest the report published before the
        // session, two million over twenty: neither reads the next night's trend nor the report published on the session.
        Assert.Equal(0.1, Math.Round(whole[LedgerReadings.IndexOf("estimate_revision_month")]!.Value, 12));
        Assert.Equal(0.1, Math.Round(whole[LedgerReadings.IndexOf("short_interest_share")]!.Value, 12));
        Assert.Empty(LedgerPointInTime.Differences(whole, rebuilt));
    }
}
