using System.Globalization;
using System.Text.Json;
using EquityBrief.Core.Candidates;
using EquityBrief.Core.Chart;
using EquityBrief.Core.History;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.History;
using Microsoft.Data.Sqlite;
using Xunit;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 23.4: the statement before the first request over a constructed feed, and the stop before any request; a
// code priced by nothing listed and left out; the span's floor worked by hand on both sides and a quarter end under the stop
// writing nothing; a filer whose names are not the company's refused and listed; Merrill's end marked at its merger's completion
// from its captured filings, with a departed code no filing settles listed at its last session with volume; the overlap with the
// pull worked by hand at the store's scale; the S&P 500's count from 2012 worked on both sides of its margin, two codes of one
// series counted once; the operator's word on the counts worked on both sides; no row of a bar, a pulled bar or a mark the write
// does not own moves, no night reads the new tables, and no source a rule's version pins among those 23.4 writes.
// see: The long history's prices are asked once a code from 2004-01-02 and kept apart from the bars, the pull's tables and every night
public partial class FixtureExpectations
{
    // A row the bar reader refuses, LTRPA's of 2015-02-10 adjusting to a low above its open as the rehearsal met it, is skipped and
    // named with its code and session, the rows either side kept, and stops nothing.
    // see: A row of the long prices the bar reader refuses is skipped and listed, and stops no write
    [Fact]
    public void ALongPricesRowTheReaderRefusesIsSkippedAndListedAndTheRowsBesideItKept()
    {
        const string answer = """
            [
              {"date":"2015-02-09","open":25.30,"high":25.60,"low":25.10,"close":25.40,"adjusted_close":25.40,"volume":1000},
              {"date":"2015-02-10","open":25.12,"high":25.51,"low":25.26,"close":25.26,"adjusted_close":25.26,"volume":1000},
              {"date":"2015-02-11","open":25.20,"high":25.70,"low":25.00,"close":25.50,"adjusted_close":25.50,"volume":1000}
            ]
            """;
        var unreadable = new List<string>();

        var bars = LongPricePull.BarsOf(answer, "LTRPA", unreadable);

        Assert.Equal([new DateOnly(2015, 2, 9), new DateOnly(2015, 2, 11)], bars.Select(bar => bar.SessionDate));
        Assert.Equal(["LTRPA on 2015-02-10 adjusts to low 25.26, open 25.12, high 25.51, close 25.26, which is a session that could not have traded."], unreadable);
        Assert.Equal(2, LongPricePull.BarsOf(answer, "LTRPA").Count);
    }

    internal static string[] LongPricesClaims =>
    [
        CheckReach.Key(Scope.LimitsTable, "The long history's prices"),
        CheckReach.Key(Scope.FailureTable, "A code the provider prices by nothing"),
        CheckReach.Key(Scope.FailureTable, "A filer whose names are not the company's"),
    ];

    internal static string[] LongPricesRows => [.. ComponentAccess.LongPricesRows, .. SchemaColumns.LongPriceStoreRows, .. LongPricesClaims, .. Reading.ReadSurface.LongPricesPageRows];

    // A provider answering every ask with nothing, counting what it was asked.
    sealed class SilentLongFeed : ILongHistoryFeed
    {
        public int Requests { get; private set; }

        Task<string> Answer()
        {
            Requests++;

            return Task.FromResult("[]");
        }

        public Task<string> DayFileAsync(string exchange, DateOnly session, CancellationToken cancellation = default) => Answer();

        public Task<string> SymbolsAsync(string exchange, bool delisted, CancellationToken cancellation = default) => Answer();

        public Task<string> PricesAsync(string code, DateOnly from, DateOnly through, CancellationToken cancellation = default) => Answer();

        public Task<string> SplitsAsync(string code, DateOnly from, CancellationToken cancellation = default) => Answer();

        public Task<string> DividendsAsync(string code, DateOnly from, CancellationToken cancellation = default) => Answer();

        public Task<string> CalendarAsync(DateOnly from, DateOnly through, CancellationToken cancellation = default) => Answer();

        public Task<string> CompanyAsync(string code, CancellationToken cancellation = default) => Answer();
    }

    [Fact]
    public async Task TheLongPricesStateTheirAsksBeforeTheFirstRequestAndAStatementPastTheStopAsksNothing()
    {
        using var store = new TemporaryStore();
        var folder = Path.Combine(store.Root, "capture");
        var feed = new SilentLongFeed();
        var said = new List<(string Line, int Requests)>();
        LongCode[] codes = [new("AAA", ["Alpha Co"], "1"), new("BBB", ["Beta Co"], null)];

        var asked = await LongPricePull.CaptureAsync(folder, codes, feed, new DateOnly(2026, 11, 6), false, line => said.Add((line, feed.Requests)));

        // Two codes at three asks each, one company record, the calendar's 141 months and the 17 series: 165 requests, weighing
        // 6 + 10 + 141 + 17 = 174 calls.
        Assert.Equal(141, LongPrices.Months().Count);
        Assert.Equal((new DateOnly(2004, 1, 1), new DateOnly(2004, 1, 31)), LongPrices.Months()[0]);
        Assert.Equal(new DateOnly(2015, 9, 1), LongPrices.Months()[^1].First);
        Assert.Equal(17, LongPricePull.MarketSeries.Count);
        Assert.Equal(165, asked);
        Assert.Equal(0, said[0].Requests);
        Assert.Equal("asks about 174 weighted call(s) of the provider's 100,000 a day: 2 code(s), each its prices from 2004-01-02, splits and dividends; 1 company record(s) for a filer; 141 month(s) of the earnings calendar; 17 market series: 165 request(s), about 174 weighted call(s), about 0.1 hour(s)", said[0].Line);

        // Everything kept is asked nothing again.
        Assert.Equal(0, await LongPricePull.CaptureAsync(folder, codes, feed, new DateOnly(2026, 11, 6), false, _ => { }));

        // 2,000 codes with no filer's number weigh 2,000 x 3 + 2,000 x 10 = 26,000, past the stop of 25,000: refused before any ask.
        var many = Enumerable.Range(0, 2000).Select(at => new LongCode("C" + at.ToString(CultureInfo.InvariantCulture), [], null)).ToArray();
        var fresh = new SilentLongFeed();

        Assert.Throws<ArgumentException>(() => LongPricePull.CaptureAsync(Path.Combine(store.Root, "other"), many, fresh, new DateOnly(2026, 11, 6), false, _ => { }).GetAwaiter().GetResult());
        Assert.Equal(0, fresh.Requests);
    }

    [Fact]
    public void ASpansFloorIsWorkedByHandOnBothSidesAndAQuarterEndUnderTheStopStopsTheRun()
    {
        var start = new DateOnly(2008, 1, 2);
        DateOnly[] sessions = [.. Enumerable.Range(0, 50).Select(at => start.AddDays(at))];

        // 49 of 50 is 98 in every 100 and priced; 48 of 50 is 96 and not.
        Assert.True(LongPrices.PricedOver(sessions, sessions.Skip(1).ToHashSet(), start, null, sessions[^1]));
        Assert.False(LongPrices.PricedOver(sessions, sessions.Skip(2).ToHashSet(), start, null, sessions[^1]));

        // A code whose closes stop within its span is read to its last close: 30 sessions held of the 30 to it.
        Assert.True(LongPrices.PricedOver(sessions, sessions.Take(30).ToHashSet(), start, null, sessions[29]));

        // One whose closes start late misses the span's first sessions, and one that holds none is never priced.
        Assert.False(LongPrices.PricedOver(sessions, sessions.Skip(10).ToHashSet(), start, null, sessions[^1]));
        Assert.False(LongPrices.PricedOver(sessions, new HashSet<DateOnly>(), start, null, null));

        var period = start.AddDays(20);
        LongSpan[] spans = [new("MID", "AAA", start, null, LongSpans.FromFund), new("MID", "BBB", start, null, LongSpans.FromFund)];
        var held = new Dictionary<string, IReadOnlySet<DateOnly>> { ["AAA"] = sessions.ToHashSet(), ["BBB"] = sessions.Skip(5).ToHashSet() };
        (string, DateOnly, string?, decimal)[] nine = [.. Enumerable.Range(0, 9).Select(_ => ("MID", period, (string?)"AAA", 10m)), ("MID", period, "BBB", 10m)];

        // Nine of ten priced is 90 in every 100 and stands; with one more holding matched to none it is 9 of 11 and stops.
        var standing = LongPrices.Coverage(nine, spans, sessions, ticker => held[ticker], _ => sessions[^1]);

        Assert.Equal(9, standing.Single().Priced);
        Assert.Empty(LongPrices.Stops(standing));

        var under = LongPrices.Coverage([.. nine, ("MID", period, null, 10m)], spans, sessions, ticker => held[ticker], _ => sessions[^1]);

        Assert.Equal(["MID 2008-01-22: 9 of 11 holdings priced over their spans, 81.8 % and 81.8 % of their value, under the stop of 90 % of either"], LongPrices.Stops(under));
    }

    [Fact]
    public void TheSandP500sCountFrom2012IsWorkedOnBothSidesOfItsMarginAndTwoCodesOfOneSeriesCountOnce()
    {
        var period = new DateOnly(2014, 3, 31);
        var days = Enumerable.Range(0, 60).Select(at => period.AddDays(-at)).ToArray();
        var series = days.ToDictionary(day => day, day => 10m + day.DayNumber % 7);
        var other = days.ToDictionary(day => day, day => 50m + day.DayNumber % 5);
        var closes = new Dictionary<string, IReadOnlyDictionary<DateOnly, decimal>>(StringComparer.Ordinal) { ["OLD"] = series, ["NEW"] = series, ["ELSE"] = other };

        // OLD and NEW carry one series at the cent on every session both hold, and ELSE another.
        Assert.True(LongPrices.OneSeries(series, series, period.AddDays(-91), period));
        Assert.False(LongPrices.OneSeries(series, other, period.AddDays(-91), period));
        Assert.False(LongPrices.OneSeries(series.Take(19).ToDictionary(), series, period.AddDays(-91), period));

        ProviderSpan[] provider = [new("OLD", new DateOnly(2010, 1, 4), null), new("NEW", new DateOnly(2013, 6, 3), null), new("ELSE", null, null), new("LEFT", null, new DateOnly(2014, 1, 2))];
        IReadOnlyDictionary<DateOnly, decimal> Closes(string code) => closes.TryGetValue(code, out var held) ? held : new Dictionary<DateOnly, decimal>();

        // Three codes in the spans on the day, two once OLD and NEW count once; IVV's 7 holdings leave them 5 apart, within the margin.
        var within = LongPrices.Counted(provider, new Dictionary<DateOnly, int> { [period] = 7, [new DateOnly(2011, 12, 31)] = 1 }, Closes);

        Assert.Equal((3, 2, -5), (within.Single().Codes, within.Single().Companies, within.Single().Apart));
        Assert.Equal(["NEW and OLD"], within.Single().CountedOnce);
        Assert.Equal(LongSpans.ProviderFrom, LongPrices.Cutover(within));

        // At 8, six apart, the S&P 500 reads IVV's schedules to their last and the provider's dates from the day after.
        var beyond = LongPrices.Counted(provider, new Dictionary<DateOnly, int> { [period] = 8 }, Closes);

        Assert.Equal(new DateOnly(2018, 10, 1), LongPrices.Cutover(beyond));
    }

    [Fact]
    public void AFilerWhoseNamesAreNotTheCompanysIsRefusedAndTheOverlapWithThePullIsWorkedByHand()
    {
        Assert.True(LongPrices.IsTheCompany(["MERRILL LYNCH & CO INC"], ["Merrill Lynch & Co., Inc."]));
        Assert.True(LongPrices.IsTheCompany(["NEW NAME CORP", "OLD NAME INC"], ["Old Name, Inc."]));
        Assert.False(LongPrices.IsTheCompany(["BANK OF AMERICA CORP"], ["Merrill Lynch & Co., Inc."]));

        Assert.True(LongPrices.Departed("BSC_old", new DateOnly(2026, 11, 6), new DateOnly(2026, 11, 6)));
        Assert.True(LongPrices.Departed("GONE", new DateOnly(2026, 10, 27), new DateOnly(2026, 11, 6)));
        Assert.False(LongPrices.Departed("KEPT", new DateOnly(2026, 10, 28), new DateOnly(2026, 11, 6)));

        var (a, b, c) = (new DateOnly(2016, 1, 4), new DateOnly(2016, 1, 5), new DateOnly(2016, 1, 6));

        // The long answer is at half the pull's scale; the store's first close of 30 on the first day brings each side to it, the
        // long by 30 / 10 = 3 and the pull by 30 / 20 = 1.5. The third day stands 2 apart at 60 against 62, past a cent and one per
        // cent, and its close as traded 0.50 apart.
        var longer = new Dictionary<DateOnly, (decimal, decimal?)> { [a] = (10m, 10m), [b] = (11m, 11m), [c] = (20m, 20m) };
        var pulled = new Dictionary<DateOnly, (decimal, decimal?)> { [a] = (20m, 10m), [b] = (22m, 11m), [c] = (41.3333m, 20.5m) };
        var overlap = LongPricePull.Overlap("AAA", longer, pulled, (a, 30m));

        Assert.Equal(("AAA", 3, 1, 1, 1, (DateOnly?)c), (overlap.Ticker, overlap.Shared, overlap.ApartByACent, overlap.ApartByOnePerCent, overlap.TradedApart, overlap.FirstApart));
        Assert.Equal(0.0323m, Math.Round(overlap.Largest, 4));

        // With no stored close the long side is brought to the pull by the median of their ratios, 2, and the third day stands
        // 40 against 41.33, again apart.
        Assert.Equal(1, LongPricePull.Overlap("AAA", longer, pulled, null).ApartByOnePerCent);
    }

    // A weekday calendar from a day through a day, which the constructed answers are written over.
    static DateOnly[] LongWeekdays(DateOnly from, DateOnly through) =>
        [.. Enumerable.Range(0, through.DayNumber - from.DayNumber + 1).Select(from.AddDays).Where(day => day.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday && day != new DateOnly(2009, 1, 1) && day != new DateOnly(2008, 12, 25))];

    static string Answer(IEnumerable<DateOnly> days, decimal close, Func<DateOnly, long>? volume = null) =>
        JsonSerializer.Serialize(days.Select(day => new Dictionary<string, object>
        {
            ["date"] = day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            ["open"] = close,
            ["high"] = close,
            ["low"] = close,
            ["close"] = close,
            ["adjusted_close"] = close,
            ["volume"] = volume?.Invoke(day) ?? 1000L,
        }));

    // A capture folder over constructed answers: the index's sessions from December 2008 to March 2009; MER priced past its
    // merger's completion with Merrill Lynch's filings as captured; KEPT priced to the newest session, the answer given; GONE
    // stopping in January with no filer and no volume on its last session; ZZZ priced by nothing.
    static string Capture(string root, string keptAnswer)
    {
        var folder = Path.Combine(root, "capture");

        foreach (var kind in new[] { LongPricePull.PricesFolder, LongPricePull.MarketFolder, LongPricePull.SplitsFolder, LongPricePull.DividendsFolder })
        {
            Directory.CreateDirectory(Path.Combine(folder, kind));
        }

        var all = LongWeekdays(new DateOnly(2008, 12, 1), new DateOnly(2009, 3, 31));

        File.WriteAllText(Path.Combine(folder, LongPricePull.MarketFolder, "GSPC.json"), Answer(all, 900m));
        File.WriteAllText(Path.Combine(folder, LongPricePull.PricesFolder,"MER.json"), Answer(all.Where(day => day <= new DateOnly(2009, 2, 27)), 12m));
        File.WriteAllText(Path.Combine(folder, LongPricePull.PricesFolder,"KEPT.json"), keptAnswer);
        File.WriteAllText(Path.Combine(folder, LongPricePull.PricesFolder,"GONE.json"), Answer(all.Where(day => day <= new DateOnly(2009, 1, 15)), 3m, day => day == new DateOnly(2009, 1, 15) ? 0L : 500L));
        File.WriteAllText(Path.Combine(folder, LongPricePull.PricesFolder,"ZZZ.json"), "[]");
        File.WriteAllText(Path.Combine(folder, LongPricePull.SplitsFolder, "KEPT.json"), "[{\"date\":\"2009-02-02\",\"split\":\"2.000000/1.000000\"}]");
        File.WriteAllText(Path.Combine(folder, LongPricePull.DividendsFolder, "KEPT.json"), "[{\"date\":\"2009-03-02\",\"value\":0.25,\"unadjustedValue\":0.5}]");

        var merrill = Path.Combine(folder, LongPricePull.FilersFolder, "65100");

        foreach (var file in Directory.EnumerateFiles(HistoryEndsFixture + "/mer", "*", SearchOption.AllDirectories))
        {
            var to = Path.Combine(merrill, Path.GetRelativePath(Path.Combine(HistoryEndsFixture, "mer"), file));

            Directory.CreateDirectory(Path.GetDirectoryName(to)!);
            File.Copy(file, to);
        }

        LongCode[] codes = [new("GONE", ["Gone Co."], null), new("KEPT", ["Kept Co."], "1"), new("MER", ["Merrill Lynch & Co., Inc."], "65100"), new("ZZZ", ["Zed Co."], null)];
        LongFiler[] filers = [new("GONE", null, [], false, "no filer found"), new("KEPT", "1", ["SOMEONE ELSE INC"], false, "the pull's filer"), new("MER", "65100", ["MERRILL LYNCH & CO INC"], true, "the pull's filer"), new("ZZZ", null, [], false, "no filer found")];

        File.WriteAllText(Path.Combine(folder, LongPricePull.CodesFile), JsonSerializer.Serialize(codes));
        File.WriteAllText(Path.Combine(folder, LongPricePull.FilersFile), JsonSerializer.Serialize(filers));

        return folder;
    }

    static TemporaryStore LongPricesStore()
    {
        var store = new TemporaryStore().Migrated();

        store.Execute(@"
            INSERT INTO long_snapshot (index_code, period, accession, form, holdings, value_usd, run_id) VALUES ('MID', '2008-12-31', 'acc-1', 'N-Q', 3, '100', 'long-members-1');
            INSERT INTO long_holding (index_code, period, holding, name, value_usd, ticker, matched_by, run_id) VALUES ('MID', '2008-12-31', 0, 'Merrill Lynch & Co., Inc.', '50', 'MER', 'name', 'long-members-1');
            INSERT INTO long_holding (index_code, period, holding, name, value_usd, ticker, matched_by, run_id) VALUES ('MID', '2008-12-31', 1, 'Kept Co.', '45', 'KEPT', 'name', 'long-members-1');
            INSERT INTO long_holding (index_code, period, holding, name, value_usd, ticker, matched_by, run_id) VALUES ('MID', '2008-12-31', 2, 'Gone Co.', '5', 'GONE', 'name', 'long-members-1');
            INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at, raw_close) VALUES ('KEPT', '2009-03-02', '7', '7', '7', '7', 100, 'bulk', '2026-11-06T21:10:00Z', '7');
            INSERT INTO pulled_bar (ticker, session_date, open, high, low, close, raw_close, volume, pull) VALUES ('KEPT', '2009-03-02', '7', '7', '7', '7', '7', 100, 'history-pull-1');
            INSERT INTO session_mark (ticker, session_date, kind, says, ruled, run_id) VALUES ('KEPT', '2009-01-05', 'checked and kept', 'Kept.', '2026-11-01T00:00:00Z', 'mark-1');");

        return store;
    }

    static string Standing(TemporaryStore store)
    {
        using var connection = store.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT group_concat(ticker || session_date || close, '|') FROM bar UNION ALL SELECT group_concat(ticker || session_date || close || pull, '|') FROM pulled_bar UNION ALL SELECT group_concat(ticker || session_date || kind, '|') FROM session_mark WHERE kind <> 'history ends';";
        using var reader = command.ExecuteReader();
        var rows = new List<string>();

        while (reader.Read())
        {
            rows.Add(reader.IsDBNull(0) ? string.Empty : reader.GetString(0));
        }

        return string.Join("/", rows);
    }

    [Fact]
    public async Task TheWriteMarksMerrillsEndAtItsMergersCompletionListsWhatNoFilingSettlesAndMovesNoStandingRow()
    {
        using var store = LongPricesStore();
        var all = LongWeekdays(new DateOnly(2008, 12, 1), new DateOnly(2009, 3, 31));
        var folder = Capture(store.Root, Answer(all, 7m));
        var before = Standing(store);
        var at = new DateTimeOffset(2026, 11, 7, 12, 0, 0, TimeSpan.Zero);

        var outcome = await LongPricePull.WriteAsync(store.DatabaseFile, folder, "long-prices-1", FixedClock.At(at, SessionZones.UnitedStates), true, _ => { });

        Assert.True(outcome.Written);
        Assert.Empty(outcome.Stops);
        Assert.Equal(3, outcome.Priced);
        Assert.Equal(["ZZZ (Zed Co.)"], outcome.Nothing);

        // KEPT's filer files under another company's names and is refused and listed; its filings are not kept.
        Assert.Equal(["KEPT"], outcome.Refused.Select(filer => filer.Ticker));

        // Merrill's answer runs on to 2009-02-27 past its end; the 8-K of 2009-01-02 ends it on 2008-12-31, marked.
        var merrill = outcome.Ends.Single(end => end.Ticker == "MER");

        Assert.Equal((new DateOnly(2008, 12, 31), "0000950123-09-000005", true), (merrill.Session!.Value, merrill.Accession, merrill.Marked));

        // GONE has no filer, so it reads to its last session with volume, 2009-01-14, and goes to the operator.
        var gone = outcome.Ends.Single(end => end.Ticker == "GONE");

        Assert.Equal((null, new DateOnly(2009, 1, 14), false), (gone.Session, gone.LastTraded, gone.Marked));

        using (var connection = store.Open())
        {
            Assert.Equal(1L, LongCount(connection, "SELECT COUNT(*) FROM session_mark WHERE ticker = 'MER' AND session_date = '2008-12-31' AND kind = 'history ends' AND says LIKE '%0000950123-09-000005%' AND run_id = 'long-prices-1';"));
            Assert.Equal(0L, LongCount(connection, "SELECT COUNT(*) FROM long_bar WHERE ticker = 'ZZZ';"));
            Assert.Equal((long)all.Length, LongCount(connection, "SELECT COUNT(*) FROM long_bar WHERE ticker = 'KEPT';"));
            Assert.Equal(1L, LongCount(connection, "SELECT COUNT(*) FROM long_split WHERE ticker = 'KEPT' AND ex_date = '2009-02-02' AND new_shares = '2.000000' AND old_shares = '1.000000';"));
            Assert.Equal(1L, LongCount(connection, "SELECT COUNT(*) FROM long_dividend WHERE ticker = 'KEPT' AND amount = '0.25' AND raw_amount = '0.5';"));
            Assert.Equal((long)all.Length, LongCount(connection, "SELECT COUNT(*) FROM long_market_bar WHERE series = 'GSPC';"));
            Assert.True(LongCount(connection, "SELECT COUNT(*) FROM long_filing WHERE ticker = 'MER' AND accession = '0000950123-09-000005' AND form = '8-K' AND items LIKE '%5.01%';") == 1L);
            Assert.Equal(0L, LongCount(connection, "SELECT COUNT(*) FROM long_filing WHERE ticker = 'KEPT';"));
            Assert.Equal(1L, LongCount(connection, "SELECT COUNT(*) FROM run_log WHERE run_id = 'long-prices-1' AND stage = 'long-prices' AND detail LIKE '%\"unsettled\":[\"GONE 2009-01-14\"]%';"));
        }

        Assert.Equal(before, Standing(store));
    }

    [Fact]
    public async Task AQuarterEndUnderTheFloorOverItsSpansWritesNothing()
    {
        using var store = LongPricesStore();
        var all = LongWeekdays(new DateOnly(2008, 12, 1), new DateOnly(2009, 3, 31));

        // KEPT's answer holds every other session, so its span is not priced and the quarter end stands at 2 of 3 holdings.
        var folder = Capture(store.Root, Answer(all.Where((_, at) => at % 2 == 0), 7m));
        var before = Standing(store);

        var outcome = await LongPricePull.WriteAsync(store.DatabaseFile, folder, "long-prices-1", FixedClock.At(new DateTimeOffset(2026, 11, 7, 12, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates), true, _ => { });

        Assert.False(outcome.Written);
        Assert.Equal(["MID 2008-12-31: 2 of 3 holdings priced over their spans, 66.7 % and 55.0 % of their value, under the stop of 90 % of either"], outcome.Stops);

        using (var connection = store.Open())
        {
            Assert.Equal(0L, LongCount(connection, "SELECT COUNT(*) FROM long_bar;"));
            Assert.Equal(0L, LongCount(connection, "SELECT COUNT(*) FROM session_mark WHERE kind = 'history ends';"));
            Assert.Equal(0L, LongCount(connection, "SELECT COUNT(*) FROM run_log WHERE stage = 'long-prices';"));
        }

        Assert.Equal(before, Standing(store));
    }

    [Fact]
    public void AnEndAFilingGivesIsMarkedOnlyWhereTheProvidersPricesRunOnPastIt()
    {
        using var store = new TemporaryStore();
        var folder = Capture(store.Root, "[]");
        var all = LongWeekdays(new DateOnly(2008, 12, 1), new DateOnly(2009, 3, 31));
        var calendar = new Core.Bars.PulledCalendar(all.ToDictionary(day => day, _ => 1), [(all[0], all[^1])]);
        var merrill = new LongCode("MER", ["Merrill Lynch & Co., Inc."], "65100");
        var filer = new LongFiler("MER", "65100", ["MERRILL LYNCH & CO INC"], true, "the pull's filer");
        ProviderBar[] Series(DateOnly through) => [.. all.Where(day => day <= through).Select(day => new ProviderBar(day, 12m, 12m, 12m, 12m, 12m, 1000))];

        // The answer stopping on 2008-12-15 stops before the filing's end of 2008-12-31: the end stands and no mark is written.
        var stopped = LongPricePull.EndOf(folder, merrill, filer, Series(new DateOnly(2008, 12, 15)), calendar);

        Assert.Equal((new DateOnly(2008, 12, 31), false), (stopped.Session!.Value, stopped.Marked));

        // Running on to 2009-01-02 it is marked, and with its filer's names another company's no end is read at all.
        Assert.True(LongPricePull.EndOf(folder, merrill, filer, Series(new DateOnly(2009, 1, 2)), calendar).Marked);
        Assert.Null(LongPricePull.EndOf(folder, merrill, filer with { IsTheCompany = false }, Series(new DateOnly(2009, 1, 2)), calendar).Session);
    }

    [Fact]
    public async Task TheLongDataCheckReadsTheLongPricesAndWritesItsFindingsFrom2005UnderItsOwnStage()
    {
        using var store = new TemporaryStore().Migrated();
        var days = LongWeekdays(new DateOnly(2004, 1, 2), new DateOnly(2006, 12, 29));

        using (var connection = store.Open())
        {
            using var transaction = connection.BeginTransaction();
            using var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = "INSERT INTO long_bar (ticker, session_date, open, high, low, close, raw_close, volume, run_id) VALUES ('AAA', $day, $close, $close, $close, $close, $close, 1000000, 'long-prices-1');";

            var (day, close) = (insert.Parameters.Add("$day", SqliteType.Text), insert.Parameters.Add("$close", SqliteType.Text));

            // A move of 40 in every 100 on 2004-06-01 and another on 2006-06-01, each standing, on usual volume.
            foreach (var session in days)
            {
                day.Value = session.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                close.Value = session < new DateOnly(2004, 6, 1) ? "10" : session < new DateOnly(2006, 6, 1) ? "14" : "19.6";
                insert.ExecuteNonQuery();
            }

            transaction.Commit();
        }

        // A pulled bar the long check does not read.
        store.Execute("INSERT INTO pulled_bar (ticker, session_date, open, high, low, close, raw_close, volume, pull) VALUES ('BBB', '2016-01-04', '5', '5', '5', '5', '5', 0, 'history-pull-1');");

        var clock = FixedClock.At(new DateTimeOffset(2026, 11, 7, 13, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates);
        var outcome = await new DataChecker(clock, store.DatabaseFile, longHistory: true).RunAsync("data-check-long-1");

        Assert.Equal(1, outcome.Stocks);

        using var read = store.Open();

        Assert.Equal(1L, LongCount(read, "SELECT COUNT(*) FROM data_check;"));
        Assert.Equal(1L, LongCount(read, "SELECT COUNT(*) FROM data_check WHERE ticker = 'AAA' AND session_date = '2006-06-01' AND class = 'jump' AND kind = 'unexplained' AND era = '2005 to 2009';"));
        Assert.Equal(1L, LongCount(read, "SELECT COUNT(*) FROM run_log WHERE run_id = 'data-check-long-1' AND stage = 'data-check-long';"));
    }

    [Fact]
    public async Task TheOperatorsWordOnTheLongCountsStandsOnlyAfterTheNewestLongCheck()
    {
        using var store = new TemporaryStore().Migrated();
        var clock = FixedClock.At(new DateTimeOffset(2026, 11, 7, 12, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates);

        void Checked(string at) => store.Execute($"INSERT INTO run_log (run_id, stage, started_at, ended_at, outcome, rows_written, model_calls, network_requests, spend, detail) VALUES ('data-check-long-{at}', 'data-check-long', '{at}', '{at}', 'ok', 0, 0, 0, '0', '');");

        Assert.False(await LongPricePull.CountsWordStandsAsync(store.DatabaseFile));

        Checked("2026-11-07T11:00:00Z");
        Assert.False(await LongPricePull.CountsWordStandsAsync(store.DatabaseFile));

        Assert.Equal(1, await LongPricePull.CountsReadAsync(store.DatabaseFile, "long-counts-read-1", clock));
        Assert.True(await LongPricePull.CountsWordStandsAsync(store.DatabaseFile));

        // A check run after the word asks for it again.
        Checked("2026-11-07T13:00:00Z");
        Assert.False(await LongPricePull.CountsWordStandsAsync(store.DatabaseFile));
    }

    // The new tables' names, which no source but the long prices', the long check's and the History page's reads.
    static readonly string[] LongPriceTables = ["long_bar", "long_market_bar", "long_print", "long_split", "long_dividend", "long_filing"];

    static readonly string[] LongPricesSources =
    [
        "src/EquityBrief.Core/History/LongPrices.cs",
        "src/EquityBrief.Core/History/LongSpans.cs",
        "src/EquityBrief.Core/Providers/EodhdLongHistoryFeed.cs",
        "src/EquityBrief.Core/Components/ComponentAccess.cs",
        "src/EquityBrief.Data/Migrations/SchemaMigrations.cs",
        "src/EquityBrief.Worker/History/LongPricePull.cs",
        "src/EquityBrief.Worker/History/LongMembers.cs",
        "src/EquityBrief.Worker/History/DataChecker.cs",
        "src/EquityBrief.Worker/Charts/SessionMarks.cs",
        "src/EquityBrief.Worker/Program.cs",
        "src/EquityBrief.Api/Reading/ReadApi.LongPrices.cs",
        "src/EquityBrief.Api/Program.cs",
        "src/EquityBrief.Web/App/SinglePageApp.LongPrices.cs",
        "src/EquityBrief.Web/App/SinglePageApp.History.cs",
    ];

    [Fact]
    public void NoNightReadsTheLongPricesAndNoSourceARulesVersionPinsIsOne23Point4Writes()
    {
        var readers = Directory.EnumerateFiles(Path.Combine(Repository.Root, "src"), "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}EquityBrief.Tests{Path.DirectorySeparatorChar}", StringComparison.Ordinal) && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => LongPriceTables.Any(table => System.Text.RegularExpressions.Regex.IsMatch(File.ReadAllText(path), $@"\b{table}\b")))
            .Select(path => Path.GetRelativePath(Repository.Root, path).Replace('\\', '/'))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(
            ["src/EquityBrief.Data/Migrations/SchemaMigrations.cs", "src/EquityBrief.Worker/History/DataChecker.cs", "src/EquityBrief.Worker/History/LongFactPull.cs", "src/EquityBrief.Worker/History/LongFirstYearReader.cs", "src/EquityBrief.Worker/History/LongPricePull.cs"],
            readers);

        var pinned = CandidateEvaluators.All
            .SelectMany(evaluator => evaluator.OwnSources)
            .Concat(CandidateEvaluator.EvaluationSources)
            .Concat(Core.Loop.RuleHooks.Sources)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        Assert.True(pinned.Length >= 30, $"Read {pinned.Length} pinned source(s), expected at least 30.");
        Assert.All(LongPricesSources, path => Assert.True(File.Exists(Path.Combine(Repository.Root, path)), path));
        Assert.Empty(LongPricesSources.Intersect(pinned, StringComparer.Ordinal));

        string[] readings = [.. LedgerSources.Select(path => Path.GetRelativePath(Repository.Root, path).Replace('\\', '/'))];

        Assert.Empty(LongPricesSources.Intersect(readings, StringComparer.Ordinal));
        Assert.Contains(SessionMarking.HistoryEnds, SessionMarking.Kinds);
    }
}
