using System.Globalization;
using EquityBrief.Core.History;
using EquityBrief.Core.Providers;
using EquityBrief.Core.Sweep;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.History;
using Microsoft.Data.Sqlite;
using Xunit;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, the 24.19 closing plan of 2026-11-27: a hole inside a code's own prices is asked of Yahoo, then Stooq,
// then Tiingo, and kept from the first source whose closes hold one ratio to the store's within 0.1% over the twenty sessions
// the store holds on each side and whose volumes are equal or in one proportion, at the source's prices times that ratio; a
// hole no source fills stays as it was; every ask is written, Tiingo's within its limits; and the long history's reader reads
// a kept session only where the code holds no price, each worked by hand over constructed series and a constructed store.
// see: A hole inside a code's own prices is filled from a second source's traded prices where they hold one ratio to the store's on each side, never interpolated
public partial class FixtureExpectations
{
    // The store's view of the second sources' fill: the stores it writes and section 17's row of its bands and limits.
    internal static string[] SecondSourceRows => [.. ComponentAccess.SecondSourceFillRows, .. SchemaColumns.SecondSourceStoreRows, .. SecondSourceClaims];

    internal static readonly string[] SecondSourceClaims =
    [
        CheckReach.Key(Scope.LimitsTable, "Second sources' fill"),
    ];

    static readonly DateOnly[] FillDays = [.. Enumerable.Range(0, 90).Select(at => new DateOnly(2010, 1, 4).AddDays(at)).Where(day => day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)).Take(60)];

    // The store's close on the session at a place: 10 and a tenth for each session after the first.
    static decimal StoredClose(int at) => 10m + (at * 0.1m);

    static SweepBar[] StoredAround(params int[] missing) =>
        [.. FillDays.Select((day, at) => (day, at)).Where(pair => !missing.Contains(pair.at)).Select(pair => new SweepBar(pair.day, StoredClose(pair.at), StoredClose(pair.at), StoredClose(pair.at), 1000, StoredClose(pair.at), StoredClose(pair.at)))];

    // A source at half the store's scale with half its volume: the closes hold the ratio 2 and the volumes the proportion 2.
    static SourceBar[] HalfScale(Func<int, decimal>? close = null) =>
        [.. FillDays.Select((day, at) => new SourceBar(day, StoredClose(at) / 2, StoredClose(at) / 2 + 0.05m, StoredClose(at) / 2 - 0.05m, close?.Invoke(at) ?? StoredClose(at) / 2, 500, null))];

    // Worked by hand: sessions 30 and 31 missing, the source at half scale; each kept at twice the source, the hole's close of
    // 13 and 13.1 at the source's 6.5 and 6.55, its high a tenth above and its low a tenth below, its traded close the same,
    // and its volume 1,000.
    [Fact]
    public void AHoleIsKeptAtTheSourcesPricesTimesTheOneRatioItsClosesHoldToTheStores()
    {
        var judgement = SecondSources.Judge(StoredAround(30, 31), [FillDays[30], FillDays[31]], HalfScale());

        Assert.Equal(2m, judgement.Ratio);
        Assert.Equal(2m, judgement.VolumeRatio);
        Assert.Equal(
            [new KeptBar(FillDays[30], 13m, 13.1m, 12.9m, 13m, 13m, 1000), new KeptBar(FillDays[31], 13.1m, 13.2m, 13m, 13.1m, 13.1m, 1000)],
            judgement.Kept);
        Assert.Contains("kept at 2 the store's over 40 session(s)", judgement.Why, StringComparison.Ordinal);
    }

    // Each refusal on both sides of its line: one close around the hole 0.09% off the ratio keeps it and 0.11% refuses it; a
    // volume off its proportion by 0.2% refuses it; a source missing one of the hole's sessions or one around it refuses it;
    // and a hole at the store's first session has no side to hold to.
    [Fact]
    public void AHoleIsRefusedWhereTheClosesHoldNoOneRatioTheVolumesNoOneProportionOrTheSourceLacksASession()
    {
        var stored = StoredAround(30, 31);
        DateOnly[] hole = [FillDays[30], FillDays[31]];

        Assert.Equal(2, SecondSources.Judge(stored, hole, HalfScale(at => at == 12 ? StoredClose(at) / 2 * 1.0009m : StoredClose(at) / 2)).Kept.Count);
        Assert.Contains("past the 0.1% that makes one ratio", SecondSources.Judge(stored, hole, HalfScale(at => at == 12 ? StoredClose(at) / 2 * 1.0011m : StoredClose(at) / 2)).Why, StringComparison.Ordinal);

        var offVolume = HalfScale().Select((bar, at) => at == 40 ? bar with { Volume = 501 } : bar).ToArray();

        Assert.Contains("apart in proportion", SecondSources.Judge(stored, hole, offVolume).Why, StringComparison.Ordinal);
        Assert.Contains("holds 1 of the hole's 2", SecondSources.Judge(stored, hole, [.. HalfScale().Where(bar => bar.Session != FillDays[31])]).Why, StringComparison.Ordinal);
        Assert.Contains("holds 39 of the store's 40", SecondSources.Judge(stored, hole, [.. HalfScale().Where(bar => bar.Session != FillDays[50])]).Why, StringComparison.Ordinal);
        Assert.Contains("no session on one side", SecondSources.Judge(StoredAround(0), [FillDays[0]], HalfScale()).Why, StringComparison.Ordinal);
        Assert.Empty(SecondSources.Holes(StoredAround(59), [FillDays[59]]));
        Assert.Equal([FillDays[30]], SecondSources.Holes(StoredAround(30), [FillDays[30], FillDays[59]]));
    }

    // A source whose close is the traded one and whose adjusted close is held to the store's: adjusted at 0.9 of its close and
    // at half the store's scale, the kept close is its adjusted close times 2 and its traded close its own close times the
    // ratio its closes hold to the store's traded closes.
    [Fact]
    public void ASourcesAdjustedCloseIsHeldToTheStoresAndItsTradedCloseToTheStoresTraded()
    {
        var stored = FillDays.Select((day, at) => (day, at)).Where(pair => pair.at != 30)
            .Select(pair => new SweepBar(pair.day, StoredClose(pair.at), StoredClose(pair.at), StoredClose(pair.at), 1000, StoredClose(pair.at), StoredClose(pair.at) / 0.9m)).ToArray();
        SourceBar[] source = [.. FillDays.Select((day, at) => new SourceBar(day, StoredClose(at) / 1.8m, StoredClose(at) / 1.8m, StoredClose(at) / 1.8m, StoredClose(at) / 1.8m, 1000, StoredClose(at) / 2))];
        var kept = Assert.Single(SecondSources.Judge(stored, [FillDays[30]], source).Kept);

        Assert.Equal(13m, kept.Close);
        Assert.Equal(PriceForm(13m / 0.9m), kept.RawClose);
    }

    static decimal PriceForm(decimal value) => EquityBrief.Core.Prices.PriceForm.Round(value);

    // Tiingo's limits by hand: 49 asks in the hour leave one, the 50th waits until the hour's oldest is an hour old; 1,000 on
    // the day wait for the next UTC day; 500 codes in the month refuse a 501st code and not one already asked.
    [Fact]
    public void TiingoIsAskedWithinFiftyAnHourAThousandADayAndFiveHundredCodesAMonth()
    {
        var now = new DateTimeOffset(2026, 11, 28, 10, 0, 0, TimeSpan.Zero);
        var hour = Enumerable.Range(0, 49).Select(at => (now.AddMinutes(-50).AddSeconds(at), "A")).ToList();

        Assert.Equal(TimeSpan.Zero, TiingoAllowance.Wait(hour, now, "A"));

        hour.Add((now.AddMinutes(-1), "A"));

        Assert.Equal(TimeSpan.FromMinutes(10), TiingoAllowance.Wait(hour, now, "A"));

        var day = Enumerable.Range(0, 1000).Select(at => (now.AddHours(-9).AddSeconds(at), "A")).ToList();

        Assert.Equal(TimeSpan.FromHours(14), TiingoAllowance.Wait(day, now, "A"));

        var month = Enumerable.Range(0, 500).Select(at => (now.AddDays(-20).AddMinutes(at), "C" + at.ToString(CultureInfo.InvariantCulture))).ToList();

        Assert.Null(TiingoAllowance.Wait(month, now, "NEW"));
        Assert.Equal(TimeSpan.Zero, TiingoAllowance.Wait(month, now, "C7"));
        Assert.Equal(TimeSpan.Zero, TiingoAllowance.Wait(month, now.AddDays(5), "NEW"));
    }

    // Each source's answer as it sends it, and a code's symbol at each.
    [Fact]
    public void EachSecondSourcesAnswerIsReadAsItSendsIt()
    {
        var yahoo = SecondSources.YahooChart("""{"chart":{"result":[{"meta":{"gmtoffset":-18000},"timestamp":[1680789600,1680876000],"indicators":{"quote":[{"open":[40.1,null],"high":[41.0,null],"low":[39.9,null],"close":[40.5,null],"volume":[123456,null]}],"adjclose":[{"adjclose":[38.25,null]}]}}],"error":null}}""");

        Assert.Equal([new SourceBar(new DateOnly(2023, 4, 6), 40.1m, 41.0m, 39.9m, 40.5m, 123456, 38.25m)], yahoo);
        Assert.Empty(SecondSources.YahooChart("""{"chart":{"result":null,"error":{"code":"Not Found"}}}"""));
        Assert.Equal([new SourceBar(new DateOnly(2023, 4, 6), 40.1m, 41m, 39.9m, 40.5m, 123456, null)], SecondSources.StooqDaily("Date,Open,High,Low,Close,Volume\n2023-04-06,40.1,41,39.9,40.5,123456\n"));
        Assert.Empty(SecondSources.StooqDaily("No data"));
        Assert.Null(SecondSources.StooqRefusal("Date,Open,High,Low,Close,Volume\n2023-04-06,40.1,41,39.9,40.5,123456\n"));
        Assert.Equal("Stooq asked for its API key and sent no daily file", SecondSources.StooqRefusal("<html><body>Get your apikey: open https://stooq.com/q/d/?s=aapl.us&get_apikey</body></html>"));
        Assert.Equal("Stooq's daily limit is spent and it sent no daily file", SecondSources.StooqRefusal("Exceeded the daily hits limit"));
        Assert.Equal("Stooq holds no data for the symbol", SecondSources.StooqRefusal("No data"));
        Assert.Equal("Stooq sent no daily file", SecondSources.StooqRefusal("<html></html>"));
        Assert.Equal(
            [new SourceBar(new DateOnly(2023, 4, 6), 40.1m, 41m, 39.9m, 40.5m, 123456, 38.25m)],
            SecondSources.TiingoPrices("""[{"date":"2023-04-06T00:00:00.000Z","close":40.5,"high":41.0,"low":39.9,"open":40.1,"volume":123456,"adjClose":38.25}]"""));
        Assert.Empty(SecondSources.TiingoPrices("""{"detail":"Not found."}"""));
        Assert.Equal((new DateOnly(2005, 1, 3), new DateOnly(2012, 6, 29)), SecondSources.TiingoMeta("""{"ticker":"abc","startDate":"2005-01-03","endDate":"2012-06-29"}"""));
        Assert.Null(SecondSources.TiingoMeta("""{"detail":"Not found."}"""));
        Assert.Equal(["DOC", "brk-b.us", "wolf"], new[] { SecondSources.Symbol("DOC_old1", SecondSources.Yahoo), SecondSources.Symbol("BRK.B", SecondSources.Stooq), SecondSources.Symbol("WOLF_old2", SecondSources.Tiingo) });
    }

    // The reader's merge by hand: a hole's session read only where the series holds none, a correction's in place of the
    // stored one, and a session kept twice reading the newer.
    [Fact]
    public void AKeptSessionIsReadOnlyWhereTheCodeHoldsNoPriceAndACorrectionInPlaceOfTheStoredOne()
    {
        var series = StoredAround(30);
        var hole = new KeptBar(FillDays[30], 13m, 13m, 13m, 13m, 13m, 1000);
        var overStored = new KeptBar(FillDays[29], 99m, 99m, 99m, 99m, 99m, 1);
        var newer = hole with { Close = 13.05m, High = 13.05m };

        var read = SecondSources.Read(series, [(hole, SecondSources.HoleKind), (overStored, SecondSources.HoleKind)]);

        Assert.Equal(60, read.Length);
        Assert.Equal(13m, read[30].Close);
        Assert.Equal(StoredClose(29), read[29].Close);
        Assert.Equal(99m, SecondSources.Read(series, [(overStored, SecondSources.CorrectionKind)])[29].Close);
        Assert.Equal(13.05m, SecondSources.Read(series, [(hole, SecondSources.HoleKind), (newer, SecondSources.HoleKind)])[30].Close);
    }

    // Over a constructed store: HOL on the S&P 400 misses sessions 30 and 31 inside its own prices. Yahoo holds nothing,
    // Stooq's closes part from the store's on one session, and Tiingo, its meta covering the hole, holds them at half scale:
    // the two are kept from Tiingo at twice its prices, every ask is written, the reader now reads HOL's two sessions, the
    // lists name no session of HOL's without a price, a second run asks nothing, and the tables refuse an update and a
    // delete. Without a key Tiingo is asked nothing, the hole is written open with each source's reason, and a run after it
    // asks nothing again for that hole.
    [Fact]
    public async Task TheFillAsksEachSourceInTurnKeepsTheFirstThatHoldsTheHoleAndWritesEveryAsk()
    {
        using var store = FillStore();
        var clock = FixedClock.At(new DateTimeOffset(2026, 11, 28, 4, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates);
        var feed = new RecordedSecondSources(
            [],
            [.. HalfScale(at => at == 25 ? StoredClose(at) / 2 * 1.01m : StoredClose(at) / 2)],
            (FillDays[0], FillDays[^1]),
            [.. HalfScale()]);
        var output = new StringWriter();
        var outcomes = await new SecondSourceFill(store.DatabaseFile, clock, feed, tiingo: true, wait: (_, _) => Task.CompletedTask).RunAsync(true, output);

        var outcome = Assert.Single(outcomes);

        Assert.Equal(("HOL", FillDays[30], FillDays[31], 2, (string?)SecondSources.Tiingo), (outcome.Ticker, outcome.First, outcome.Last, outcome.Sessions, outcome.Source));
        Assert.Equal(["yahoo prices HOL", "stooq prices hol.us", "tiingo meta hol", "tiingo prices hol"], feed.Asked);
        Assert.Equal(4L, (long)One(store, "SELECT COUNT(*) FROM source_ask;")!);
        Assert.Equal("13 13.1", (string)One(store, "SELECT group_concat(close, ' ') FROM (SELECT close FROM long_fill_bar ORDER BY session_date);")!);
        Assert.Equal("tiingo 2 1000", (string)One(store, "SELECT source || ' ' || ratio || ' ' || MIN(volume) FROM long_fill_bar;")!);

        var (series, _) = await new LongHistoryReader(store.DatabaseFile).CodeSeriesAsync("HOL", FillDays[^1]);

        Assert.Equal(60, series.Length);

        var (missing, _) = await new LongHistoryReader(store.DatabaseFile).MissingAsync("MID", FillDays[^1]);

        Assert.False(missing.ContainsKey("HOL"));
        Assert.Empty(await new SecondSourceFill(store.DatabaseFile, clock, feed, tiingo: true, wait: (_, _) => Task.CompletedTask).RunAsync(true, TextWriter.Null));
        Assert.Equal(4, feed.Asked.Count);
        Assert.Throws<SqliteException>(() => store.Execute("UPDATE long_fill_bar SET close = '1';"));
        Assert.Throws<SqliteException>(() => store.Execute("DELETE FROM source_ask;"));
        Assert.Throws<SqliteException>(() => store.Execute("DELETE FROM long_fill_hole;"));

        using var keyless = FillStore();
        var none = new RecordedSecondSources([], [], null, []);
        var open = Assert.Single(await new SecondSourceFill(keyless.DatabaseFile, clock, none, tiingo: false).RunAsync(true, TextWriter.Null));

        Assert.Null(open.Source);
        Assert.Contains("yahoo: Yahoo sent no session", open.Why, StringComparison.Ordinal);
        Assert.Contains("tiingo: not asked: no key is set", open.Why, StringComparison.Ordinal);
        Assert.Equal(["yahoo prices HOL", "stooq prices hol.us"], none.Asked);
        Assert.Equal(0L, (long)One(keyless, "SELECT COUNT(*) FROM long_fill_bar;")!);
        Assert.Empty(await new SecondSourceFill(keyless.DatabaseFile, clock, none, tiingo: true).RunAsync(true, TextWriter.Null));
        Assert.Equal(2, none.Asked.Count);

        var said = new StringWriter();

        await new SecondSourceFill(store.DatabaseFile, clock, feed, tiingo: true).RunAsync(false, said);

        Assert.Contains("nothing asked", said.ToString(), StringComparison.Ordinal);
    }

    // HOL and ALL on the S&P 400 over sixty sessions, HOL missing its thirtieth and thirty-first.
    static TemporaryStore FillStore()
    {
        var store = new TemporaryStore().Migrated();
        var days = FillDays.Select(day => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)).ToArray();

        store.Execute("INSERT INTO long_market_bar (series, session_date, open, high, low, close, run_id) VALUES " + string.Join(", ", days.Select(day => $"('GSPC', '{day}', '1000', '1000', '1000', '1000', 'r')")) + ";");
        store.Execute($"INSERT INTO long_span (index_code, ticker, joined, \"left\", read_from, run_id) VALUES ('MID', 'HOL', '{days[0]}', NULL, 'fund', 'r'), ('MID', 'ALL', '{days[0]}', NULL, 'fund', 'r');");
        store.Execute("INSERT INTO long_bar (ticker, session_date, open, high, low, close, raw_close, volume, run_id) VALUES "
            + string.Join(", ", days.Select((day, at) => (day, at)).Where(pair => pair.at is not (30 or 31)).Select(pair => LongBarRow("HOL", pair.day, StoredClose(pair.at)))
                .Concat(days.Select((day, at) => LongBarRow("ALL", day, 20m + at)))) + ";");

        return store;
    }

    // The three sources as recorded answers, each ask named as it is made.
    sealed class RecordedSecondSources(SourceBar[] yahoo, SourceBar[] stooq, (DateOnly First, DateOnly Last)? meta, SourceBar[] tiingo) : ISecondSourceFeed
    {
        public List<string> Asked { get; } = [];

        public Task<SourceAnswer> YahooAsync(string symbol, DateOnly from, DateOnly to, CancellationToken cancellation = default) => Answer("yahoo prices " + symbol, yahoo, "Yahoo", from, to);

        public Task<SourceAnswer> StooqAsync(string symbol, DateOnly from, DateOnly to, CancellationToken cancellation = default) => Answer("stooq prices " + symbol, stooq, "Stooq", from, to);

        public Task<(DateOnly First, DateOnly Last)?> TiingoMetaAsync(string symbol, CancellationToken cancellation = default)
        {
            Asked.Add("tiingo meta " + symbol);

            return Task.FromResult(meta);
        }

        public Task<SourceAnswer> TiingoAsync(string symbol, DateOnly from, DateOnly to, CancellationToken cancellation = default) => Answer("tiingo prices " + symbol, tiingo, "Tiingo", from, to);

        Task<SourceAnswer> Answer(string ask, SourceBar[] bars, string named, DateOnly from, DateOnly to)
        {
            Asked.Add(ask);

            var held = bars.Where(bar => bar.Session >= from && bar.Session <= to).ToArray();

            return Task.FromResult(new SourceAnswer(held, held.Length == 0 ? named + " sent no session" : FormattableString.Invariant($"{named} sent {held.Length} session(s)")));
        }
    }
}
