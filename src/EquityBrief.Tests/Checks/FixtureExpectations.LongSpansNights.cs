using System.Globalization;
using EquityBrief.Core.History;
using EquityBrief.Core.Sweep;
using EquityBrief.Core.Time;
using EquityBrief.Tests.Harness;
using EquityBrief.Worker.History;
using Xunit;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, the 24.19 correction's spans and renames: two codes of one filer priced on the same days join where
// their closes and volumes stand in one ratio and drift apart as share classes do never; a code's own provider spans that
// overlap read as one and two classes of shares stay two codes; the S&P 400's and 600's spans are the nights' own from their
// first night; and a current member whose long history ends more than two sessions before its newest bar is named, each
// worked by hand.
// see: Two codes of one filer join as one history where they are never priced on the same day or trade in one ratio on the days they share
// see: The long history's spans of the S&P 400 and 600 are the nights' own from the first night that read each index
public partial class FixtureExpectations
{
    static SweepBar Paired(int day, decimal close, long volume) =>
        new(new DateOnly(2011, 1, 3).AddDays(day), close, close, close, volume, close, close);

    // A hundred shared days at twice the price and three times the volume join and the other's days are put on the code's own
    // scale; one price a hundredth off on one day still joins, on two it does not; volumes three tenths off on twenty days
    // join and on twenty-one do not; prices drifting a twentieth of a per cent a day, as a share class's do, never join;
    // nineteen shared days fit one ratio and are too few.
    [Fact]
    public void TwoCodesPricedTogetherJoinInOneRatioAndShareClassesNever()
    {
        SweepBar[] own = [.. Enumerable.Range(0, 100).Select(day => Paired(day, 10m + (day % 7), 1000 + (day * 10)))];

        SweepBar[] Other(Func<int, decimal> closeOff, Func<int, decimal> volumeOff) =>
        [
            .. own.Select((bar, day) => bar with { High = bar.Close * 2m * closeOff(day), Low = bar.Close * 2m * closeOff(day), Close = bar.Close * 2m * closeOff(day), Open = bar.Close * 2m * closeOff(day), RawClose = bar.Close * 2m * closeOff(day), Volume = (long)(bar.Volume * 3m * volumeOff(day)) }),
            Paired(100, 40m, 6000),
        ];

        var whole = Other(_ => 1m, _ => 1m);

        Assert.Equal(LongRenames.ConstantRatio, LongRenames.Test(own, whole));
        Assert.Equal(LongRenames.ConstantRatio, LongRenames.Test(own, Other(day => day == 50 ? 1.01m : 1m, _ => 1m)));
        Assert.Null(LongRenames.Test(own, Other(day => day is 50 or 60 ? 1.01m : 1m, _ => 1m)));
        Assert.Equal(LongRenames.ConstantRatio, LongRenames.Test(own, Other(_ => 1m, day => day < 20 ? 1.3m : 1m)));
        Assert.Null(LongRenames.Test(own, Other(_ => 1m, day => day < 21 ? 1.3m : 1m)));
        Assert.Null(LongRenames.Test(own, Other(day => 1m + (day * 0.0005m), _ => 1m)));
        Assert.Null(LongRenames.Test(own[..19], Other(_ => 1m, _ => 1m)[..19].Select(bar => bar with { Close = bar.Close * 1.0001m }).ToArray()));

        // The day only the other code holds reads at half its close and a third of its volume.
        var (joined, taken) = LongRenames.Joined(own, whole);
        var added = Assert.Single(joined, bar => bar.Session == Paired(100, 0m, 0).Session);

        Assert.Equal(20m, added.Close);
        Assert.Equal(2000, added.Volume);
        Assert.Equal([Paired(100, 0m, 0).Session], taken);
        Assert.Equal(own.Select(bar => bar.Close), joined[..100].Select(bar => bar.Close));
    }

    // A code the provider dates twice, once to 2026-10-14 and once from 2017 with no end, reads as one span with no end; two
    // classes of one company's shares named by their class stay two codes, and two codes under one name as written and the
    // same dates read as the first. Before, the two rows of one code met at the day the provider's dates are read from and
    // the one that ended was kept, and the classes read as one.
    [Fact]
    public void AProvidersTwoRowsOfOneCodeReadAsOneSpanAndShareClassesStayTwo()
    {
        var from = new DateOnly(2018, 10, 1);
        var spans = LongSpans.Of(
            new Dictionary<string, IReadOnlyDictionary<DateOnly, IReadOnlySet<string>>>(),
            [
                new ProviderSpan("APT", new DateOnly(2012, 12, 24), new DateOnly(2026, 10, 14), "Apt Plc"),
                new ProviderSpan("APT", new DateOnly(2017, 12, 5), null, "Apt Plc"),
                new ProviderSpan("FOX", new DateOnly(2019, 3, 19), null, "Fox Corp Class B"),
                new ProviderSpan("FOXA", new DateOnly(2019, 3, 19), null, "Fox Corp Class A"),
                new ProviderSpan("TWO", new DateOnly(2021, 8, 3), new DateOnly(2021, 8, 4), "Two Co"),
                new ProviderSpan("TWOW", new DateOnly(2021, 8, 3), new DateOnly(2021, 8, 4), "Two Co"),
                new ProviderSpan("GAP", null, new DateOnly(2018, 11, 13), "Gap Co"),
                new ProviderSpan("GAP", new DateOnly(2022, 10, 3), null, "Gap Co"),
            ],
            providerFrom: from);

        Assert.Equal([new LongSpan("GSPC", "APT", from, null, LongSpans.FromProvider)], spans.Where(span => span.Ticker == "APT"));
        Assert.Contains(new LongSpan("GSPC", "FOX", new DateOnly(2019, 3, 19), null, LongSpans.FromProvider), spans);
        Assert.Contains(new LongSpan("GSPC", "FOXA", new DateOnly(2019, 3, 19), null, LongSpans.FromProvider), spans);
        Assert.Single(spans, span => span.Ticker is "TWO" or "TWOW");
        Assert.Equal([(new DateOnly(2022, 10, 3), (DateOnly?)null)], spans.Where(span => span.Ticker == "GAP").Select(span => (span.Joined, span.Left)));
    }

    // The S&P 400's fund holds AAA, BBB and CCC at its last quarter end; the nights from 2026-10-02 hold AAA throughout, CCC to
    // 2026-10-20, DDD from the first night and EEE from 2026-10-19. AAA runs on, BBB ends on the first night, CCC where the
    // nights end it, and DDD and EEE are the nights' own. Over a store: before the spans are written again DDD and EEE, the
    // two S&P 500 classes and the code dated twice are behind; after, none is, and a member two sessions behind is not named.
    [Fact]
    public async Task TheSpansOfTheSmallerIndicesAreTheNightsOwnFromTheirFirstNightAndAMemberBehindIsNamed()
    {
        var first = new DateOnly(2026, 10, 2);
        var spans = LongSpans.Of(
            new Dictionary<string, IReadOnlyDictionary<DateOnly, IReadOnlySet<string>>>
            {
                ["MID"] = new Dictionary<DateOnly, IReadOnlySet<string>>
                {
                    [new DateOnly(2026, 3, 31)] = new HashSet<string> { "AAA", "BBB", "CCC" },
                    [new DateOnly(2026, 6, 30)] = new HashSet<string> { "AAA", "BBB", "CCC" },
                },
            },
            [],
            nights: new Dictionary<string, IReadOnlyList<ProviderSpan>>
            {
                ["MID"] =
                [
                    new ProviderSpan("AAA", first, null),
                    new ProviderSpan("CCC", first, new DateOnly(2026, 10, 20)),
                    new ProviderSpan("DDD", first, null),
                    new ProviderSpan("EEE", new DateOnly(2026, 10, 19), null),
                ],
            });

        Assert.Equal(
            [
                new LongSpan("MID", "AAA", new DateOnly(2026, 3, 31), null, LongSpans.FromFund),
                new LongSpan("MID", "BBB", new DateOnly(2026, 3, 31), first, LongSpans.FromFund),
                new LongSpan("MID", "CCC", new DateOnly(2026, 3, 31), new DateOnly(2026, 10, 20), LongSpans.FromFund),
                new LongSpan("MID", "DDD", first, null, LongSpans.FromNight),
                new LongSpan("MID", "EEE", new DateOnly(2026, 10, 19), null, LongSpans.FromNight),
            ],
            spans);

        // Three sessions behind is named, two is not, and a member holding no span is named.
        DateOnly[] calendar = [.. Enumerable.Range(0, 10).Select(day => new DateOnly(2026, 11, 2).AddDays(day)).Where(day => day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))];
        LongSpan[] ending = [new("MID", "TWO", new DateOnly(2020, 1, 1), calendar[^2], "fund"), new("MID", "THR", new DateOnly(2020, 1, 1), calendar[^3], "fund")];

        Assert.Equal(
            [("MID", "NON", null, calendar.Length), ("MID", "THR", calendar[^3], 3)],
            LongSpans.Behind(ending, [("MID", "TWO", calendar[^1]), ("MID", "THR", calendar[^1]), ("MID", "NON", calendar[^1])], calendar));

        using var store = new TemporaryStore().Migrated();
        string[] sessions = [.. Enumerable.Range(0, 22).Select(day => new DateOnly(2026, 10, 1).AddDays(day)).Where(day => day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)).Select(day => day.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture))];

        store.Execute("INSERT INTO pulled_holding (index_code, period, holding, name, ticker, pull) VALUES "
            + string.Join(", ", new[] { "2026-03-31", "2026-06-30" }.SelectMany(period => new[] { "AAA", "BBB", "CCC" }.Select(code => $"('MID', '{period}', '{code}', '{code} Co', '{code}', 'p')"))) + ";");
        store.Execute("INSERT INTO membership (index_code, ticker, joined, \"left\", observed_at, name) VALUES "
            + "('MID', 'AAA', '2026-10-02', NULL, 'o', NULL), ('MID', 'CCC', '2026-10-02', '2026-10-20', 'o', NULL), "
            + "('MID', 'DDD', '2026-10-02', NULL, 'o', NULL), ('MID', 'EEE', '2026-10-19', NULL, 'o', NULL), "
            + "('GSPC', 'APT', '2012-12-24', '2026-10-14', 'o', 'Apt Plc'), ('GSPC', 'APT', '2017-12-05', NULL, 'o', 'Apt Plc'), "
            + "('GSPC', 'FOX', '2019-03-19', NULL, 'o', 'Fox Corp Class B'), ('GSPC', 'FOXA', '2019-03-19', NULL, 'o', 'Fox Corp Class A');");
        store.Execute("INSERT INTO long_span (index_code, ticker, joined, \"left\", read_from, run_id) VALUES "
            + "('MID', 'AAA', '2026-03-31', NULL, 'fund', 'r'), ('MID', 'BBB', '2026-03-31', NULL, 'fund', 'r'), ('MID', 'CCC', '2026-03-31', NULL, 'fund', 'r'), "
            + "('GSPC', 'APT', '2018-10-01', '2026-10-14', 'provider', 'r'), ('GSPC', 'FOX', '2019-03-19', NULL, 'provider', 'r');");
        store.Execute("INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at, raw_close) VALUES "
            + string.Join(", ", new[] { "AAA", "DDD", "APT", "FOX", "FOXA" }.SelectMany(code => sessions.Select(day => $"('{code}', '{day}', '10', '10', '10', '10', 1000, 'bulk', 'o', '10')"))
                .Concat(sessions.Where(day => string.CompareOrdinal(day, "2026-10-19") >= 0).Select(day => $"('EEE', '{day}', '10', '10', '10', '10', 1000, 'bulk', 'o', '10')"))) + ";");

        var before = await LongMembers.BehindAsync(store.DatabaseFile);

        Assert.Equal([("GSPC", "APT"), ("GSPC", "FOXA"), ("MID", "DDD"), ("MID", "EEE")], before.Select(one => (one.Index, one.Ticker)));

        var (was, now) = await LongPricePull.SpansAsync(store.DatabaseFile, "long-spans-test", FixedClock.At(new DateTimeOffset(2026, 11, 23, 17, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates));

        Assert.Equal((5, 8), (was, now));
        Assert.Empty(await LongMembers.BehindAsync(store.DatabaseFile));
        Assert.Equal(1L, (long)One(store, $"SELECT COUNT(*) FROM run_log WHERE stage = '{LongPricePull.SpansStage}' AND rows_written = 8;")!);
        Assert.Equal("2026-10-02", (string)One(store, "SELECT \"left\" FROM long_span WHERE ticker = 'BBB';")!);
        Assert.Equal(LongSpans.FromNight, (string)One(store, "SELECT read_from FROM long_span WHERE ticker = 'EEE';")!);
    }

    // A span the schedules ended 63 sessions after its code's last traded price ends on the first session after it; one 64
    // sessions after ends there only where the filer stops reporting; a span still open, or one starting after the price,
    // keeps its sessions.
    [Fact]
    public void ASpanPastItsLastPriceEndsThereWithinAQuarterOrWhereItsFilerStops()
    {
        var start = new DateOnly(2012, 1, 2);
        DateOnly[] calendar = [.. Enumerable.Range(0, 200).Select(day => start.AddDays(day))];
        var last = calendar[100];

        Assert.Equal([(calendar[0], calendar[101])], LongMarks.EndedAtLastPrice([(calendar[0], calendar[164])], last, calendar, _ => false));
        Assert.Equal([(calendar[0], calendar[165])], LongMarks.EndedAtLastPrice([(calendar[0], calendar[165])], last, calendar, _ => false));
        Assert.Equal([(calendar[0], calendar[101])], LongMarks.EndedAtLastPrice([(calendar[0], calendar[165])], last, calendar, day => day == last));
        Assert.Equal([(calendar[0], (DateOnly?)null)], LongMarks.EndedAtLastPrice([(calendar[0], null)], last, calendar, _ => true));
        Assert.Equal([(calendar[120], calendar[150])], LongMarks.EndedAtLastPrice([(calendar[120], calendar[150])], last, calendar, _ => true));
        Assert.Equal([(calendar[0], calendar[90])], LongMarks.EndedAtLastPrice([(calendar[0], calendar[90])], last, calendar, _ => true));
    }

    static object? One(TemporaryStore store, string sql)
    {
        using var connection = store.Open();
        using var command = connection.CreateCommand();
        command.CommandText = sql;

        return command.ExecuteScalar();
    }
}
