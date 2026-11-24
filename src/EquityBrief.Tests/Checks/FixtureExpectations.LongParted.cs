using System.Globalization;
using EquityBrief.Core.History;
using EquityBrief.Core.Sweep;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.History;
using Microsoft.Data.Sqlite;
using Xunit;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, the 24.19 correction on the operator's answers of 2026-11-23 at 16:50: a code's provider spans under
// two names part into two companies only on evidence, never on the names alone, Fox's old rows left to Twenty-First Century
// Fox's codes and Alphabet's rename read as one span; and DDR reads SITC's prices from SITE Centers' 2018 rename by hand where
// SITC's close there continues DDR's, each worked by hand.
// see: A code's provider spans under two names part into two companies only on a different filer or the marks' price test, never on the names alone
// see: DDR reads SITC's prices from SITE Centers' 2018 rename by hand where SITC's close there continues DDR's
public partial class FixtureExpectations
{
    static readonly DateOnly FoxCorp = new(2019, 3, 19);

    static readonly DateOnly Alphabet = new(2015, 10, 5);

    static ProviderSpan[] FoxAndAlphabetRows() =>
    [
        new("FOXA", new DateOnly(2013, 7, 1), FoxCorp, "21st Century Fox"),
        new("FOXA", FoxCorp, null, "Fox Corp Class A"),
        new("TFCFA", null, new DateOnly(2019, 3, 20), "Twenty-First Century Fox Inc"),
        new("GOOGL", new DateOnly(2014, 4, 3), Alphabet, "Google Inc Class A"),
        new("GOOGL", Alphabet, null, "Alphabet Inc Class A"),
        new("REUS", new DateOnly(2013, 1, 2), new DateOnly(2016, 6, 1), "Old Co"),
        new("REUS", new DateOnly(2016, 6, 1), null, "New Co"),
    ];

    // With no evidence a code's rows under two names read as one span, as before. With FOXA's prices beginning at Fox Corp's
    // row, FOXA starts on 2019-03-19 and its old row is left out, Twenty-First Century Fox's own code holding those sessions;
    // GOOGL, one filer with prices running on through the rename, stays one span; and REUS, parted by a different filer with
    // prices on both sides, reads as two spans meeting at the second's start.
    [Fact]
    public void ACodesRowsUnderTwoNamesPartOnlyOnEvidence()
    {
        var from = new DateOnly(2012, 1, 1);
        var merged = LongSpans.Of(new Dictionary<string, IReadOnlyDictionary<DateOnly, IReadOnlySet<string>>>(), FoxAndAlphabetRows(), providerFrom: from);

        Assert.Equal([(new DateOnly(2013, 7, 1), (DateOnly?)null)], merged.Where(span => span.Ticker == "FOXA").Select(span => (span.Joined, span.Left)));

        var parted = new List<LongParted>();
        var spans = LongSpans.Of(
            new Dictionary<string, IReadOnlyDictionary<DateOnly, IReadOnlySet<string>>>(),
            FoxAndAlphabetRows(),
            providerFrom: from,
            partsThem: (earlier, _) => earlier.Ticker switch { "FOXA" => LongSpans.NoPriceBefore, "REUS" => LongSpans.DifferentFiler, _ => null },
            parted: parted);

        Assert.Equal([(FoxCorp, (DateOnly?)null)], spans.Where(span => span.Ticker == "FOXA").Select(span => (span.Joined, span.Left)));
        Assert.Equal([(new DateOnly(2014, 4, 3), (DateOnly?)null)], spans.Where(span => span.Ticker == "GOOGL").Select(span => (span.Joined, span.Left)));
        Assert.Equal([(new DateOnly(2013, 1, 2), (DateOnly?)new DateOnly(2016, 6, 1)), (new DateOnly(2016, 6, 1), null)], spans.Where(span => span.Ticker == "REUS").Select(span => (span.Joined, span.Left)));
        Assert.Equal(
            [
                new LongParted("FOXA", new DateOnly(2013, 7, 1), FoxCorp, "21st Century Fox", "Fox Corp Class A", LongSpans.NoPriceBefore, true),
                new LongParted("REUS", new DateOnly(2013, 1, 2), new DateOnly(2016, 6, 1), "Old Co", "New Co", LongSpans.DifferentFiler, false),
            ],
            parted.OrderBy(one => one.Ticker, StringComparer.Ordinal));
    }

    // Over a constructed store: FOXA priced only from 2019-03-12, five sessions before Fox Corp's row, GOOGL priced every
    // session through its rename moving a quarter of a per cent a day, and JMPS priced every session with a jump of a fifth at
    // its second name's start. The evidence reads FOXA's prices beginning at the later name, JMPS jumping, and GOOGL as one
    // company; written again, the spans start FOXA on 2019-03-19, keep GOOGL's one span, and the run's row names the parting.
    [Fact]
    public async Task TheEvidenceIsReadOffTheCodesPricesAndTheSpansWrittenAgainFollowIt()
    {
        using var store = new TemporaryStore().Migrated();
        DateOnly[] calendar = [.. Enumerable.Range(0, 1100).Select(day => new DateOnly(2014, 1, 2).AddDays(day)).Where(day => day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))];
        calendar = [.. calendar, .. Enumerable.Range(0, 120).Select(day => new DateOnly(2019, 1, 2).AddDays(day)).Where(day => day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))];
        static string Text(DateOnly day) => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        static string Row(string code, DateOnly day, decimal close) => FormattableString.Invariant($"('{code}', '{Text(day)}', '{close}', '{close}', '{close}', '{close}', '{close}', 1000, 'r')");

        var jump = new DateOnly(2015, 6, 1);

        store.Execute("INSERT INTO long_market_bar (series, session_date, open, high, low, close, run_id) VALUES " + string.Join(", ", calendar.Select(day => $"('GSPC', '{Text(day)}', '1000', '1000', '1000', '1000', 'r')")) + ";");
        store.Execute("INSERT INTO membership (index_code, ticker, joined, \"left\", observed_at, name) VALUES "
            + $"('GSPC', 'FOXA', '2013-07-01', '{Text(FoxCorp)}', 'o', '21st Century Fox'), ('GSPC', 'FOXA', '{Text(FoxCorp)}', NULL, 'o', 'Fox Corp Class A'), "
            + $"('GSPC', 'GOOGL', '2014-04-03', '{Text(Alphabet)}', 'o', 'Google Inc Class A'), ('GSPC', 'GOOGL', '{Text(Alphabet)}', NULL, 'o', 'Alphabet Inc Class A'), "
            + $"('GSPC', 'JMPS', '2014-04-03', '{Text(jump)}', 'o', 'Jump Old Co'), ('GSPC', 'JMPS', '{Text(jump)}', NULL, 'o', 'Jump New Co');");
        store.Execute("INSERT INTO long_bar (ticker, session_date, open, high, low, close, raw_close, volume, run_id) VALUES "
            + string.Join(", ", calendar.Where(day => day >= new DateOnly(2019, 3, 12)).Select(day => Row("FOXA", day, 30m))
                .Concat(calendar.Select((day, at) => Row("GOOGL", day, 500m + (at % 2 == 0 ? 0m : 1.25m))))
                .Concat(calendar.Select((day, at) => Row("JMPS", day, (day >= jump ? 60m : 50m) + (at % 2 == 0 ? 0m : 0.125m))))) + ";");

        await using (var connection = new SqliteConnection(EquityBrief.Data.StoreConnection.For(store.DatabaseFile)))
        {
            await connection.OpenAsync();

            var parts = await LongPricePull.PartsAsync(connection, null);

            Assert.Equal(LongSpans.NoPriceBefore, parts(new ProviderSpan("FOXA", new DateOnly(2013, 7, 1), FoxCorp, "21st Century Fox"), new ProviderSpan("FOXA", FoxCorp, null, "Fox Corp Class A")));
            Assert.Null(parts(new ProviderSpan("GOOGL", new DateOnly(2014, 4, 3), Alphabet, "Google Inc Class A"), new ProviderSpan("GOOGL", Alphabet, null, "Alphabet Inc Class A")));
            Assert.Equal(LongMarks.Jump, parts(new ProviderSpan("JMPS", new DateOnly(2014, 4, 3), jump, "Jump Old Co"), new ProviderSpan("JMPS", jump, null, "Jump New Co")));
        }

        await LongPricePull.SpansAsync(store.DatabaseFile, "long-spans-parted", FixedClock.At(new DateTimeOffset(2026, 11, 24, 2, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates));

        Assert.Equal(Text(FoxCorp), (string)One(store, "SELECT joined FROM long_span WHERE ticker = 'FOXA';")!);
        Assert.Equal(1L, (long)One(store, "SELECT COUNT(*) FROM long_span WHERE ticker = 'GOOGL';")!);
        Assert.Equal(2L, (long)One(store, "SELECT COUNT(*) FROM long_span WHERE ticker = 'JMPS';")!);
        Assert.Contains(
            "parted: FOXA at 2019-03-19 from 21st Century Fox to Fox Corp Class A, its prices begin at the later name, the earlier left out; JMPS at 2015-06-01 from Jump Old Co to Jump New Co, jumps",
            (string)One(store, $"SELECT detail FROM run_log WHERE stage = '{LongPricePull.SpansStage}';")!,
            StringComparison.Ordinal);
    }

    // DDR priced at 12 to 13 through 2018, and SITC at 0.16 of DDR's close on the same days to the rename and from it on SITC
    // alone. From 2018-10-12 the code reads SITC's closes over 0.16, DDR's own later bars not read; SITC's first close there,
    // 2.0832 over 0.16 = 13.02, continues DDR's last of 13.00. Where SITC opens the rename a fifth higher, the link is refused.
    [Fact]
    public void DdrReadsSitcFromTheRenameByHandWhereSitcsCloseContinuesDdrs()
    {
        var from = new DateOnly(2018, 10, 12);

        Assert.Contains(("DDR", "SITC", from), LongRenames.ByHand);

        DateOnly[] calendar = [.. Enumerable.Range(0, 200).Select(day => new DateOnly(2018, 6, 1).AddDays(day)).Where(day => day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))];
        static SweepBar Bar(DateOnly day, decimal close) => new(day, close, close, close, 1000, close, close);
        decimal Ddr(int at) => 12.5m + (at % 2 == 0 ? 0.5m : 0m);

        SweepBar[] ddr = [.. calendar.Select((day, at) => Bar(day, day < from ? Ddr(at) : 99m))];
        SweepBar[] sitc = [.. calendar.Select((day, at) => Bar(day, day < from ? Ddr(at) * 0.16m : 2.0832m))];
        var linked = LongRenames.ByHandJoined(ddr, sitc, from, calendar);

        Assert.NotNull(linked);
        Assert.Equal("linked by hand from 2018-10-12, its first close there continuing the last before it", linked.Value.Test);
        Assert.Equal(13.02m, linked.Value.Bars.Single(bar => bar.Session == from).Close);
        Assert.DoesNotContain(linked.Value.Bars, bar => bar.Close == 99m);
        Assert.Equal(calendar.Count(day => day >= from), linked.Value.Joined.Count);

        SweepBar[] jumped = [.. sitc.Select(bar => bar.Session >= from ? Bar(bar.Session, 2.0832m * 1.2m) : bar)];

        Assert.Null(LongRenames.ByHandJoined(ddr, jumped, from, calendar));
    }
}
