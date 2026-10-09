using EquityBrief.Core.Families;
using EquityBrief.Core.Ledger;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Readings;
using EquityBrief.Core.Sweep;
using EquityBrief.Core.Time;
using EquityBrief.Worker.Indices;
using EquityBrief.Worker.Ledger;
using EquityBrief.Worker.Sweep;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 17.3: the ledger's point-in-time check over a constructed history. A setup's readings read off
// the whole history are the readings rebuilt from the history cut at its session, so nothing past the session reached
// them; and a stored reading taken from the session after is named as differing, by its column, with both figures. From
// the 17.3 correction of 2026-10-09 a swing setup's own readings are rebuilt by its family's own gates over the cut, and
// the check reads the history through the session the build read it through, since the pulled years are priced by the
// median ratio over the sessions the pull and the store both hold to that end.
// see: A setup is every member-session a family's loose gates pass, and its readings are defined once and read as they stood
public partial class FixtureExpectations
{
    // Three members over 80 weekdays, each drifting at its own pace with a weekly wobble, and the index and the VIX
    // moving every session, so every reading that reads past its session would read a different figure. AAA rises half a
    // point a session, its high a point over its close, on volume rising a thousand shares a session.
    static (List<DateOnly> Calendar, SweepHistoryInputs Inputs, LedgerMarket Market) PointInTimeHistory()
    {
        var calendar = new List<DateOnly>();

        for (var day = new DateOnly(2026, 1, 5); calendar.Count < 80; day = day.AddDays(1))
        {
            if (day.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday)
            {
                calendar.Add(day);
            }
        }

        SweepName Member(string ticker, decimal start, decimal step) =>
            new(
                ticker,
                [.. calendar.Select((day, at) => start + (step * at) + ((at % 5) - 2)).Select((close, at) => new SweepBar(calendar[at], close + 1, close - 1, close, 1_000_000 + (at * 1_000), close, close))],
                [(new DateOnly(2020, 1, 2), null)],
                []);

        var inputs = new SweepHistoryInputs(calendar[^1], [.. calendar], [Member("AAA", 100, 0.5m), Member("BBB", 80, -0.2m), Member("CCC", 50, 0.1m)], 0, 0, 0, 0, "constructed");
        var market = new LedgerMarket(new Dictionary<string, IReadOnlyList<(DateOnly Session, double Close)>>(StringComparer.Ordinal)
        {
            [LedgerMarket.Index] = [.. calendar.Select((day, at) => (day, 4000.0 + (at * 3)))],
            [LedgerMarket.Vix] = [.. calendar.Select((day, at) => (day, 15.0 + (at % 7)))],
        });

        return (calendar, inputs, market);
    }

    static readonly Dictionary<string, IReadOnlyList<FiledIncome>> NoIncome = new(StringComparer.Ordinal);

    static readonly Dictionary<string, string?> NoSectors = new(StringComparer.Ordinal);

    static readonly Dictionary<string, IReadOnlyList<FiledFactRow>> NoFacts = new(StringComparer.Ordinal);

    [Fact]
    public void ASetupsReadingsRebuiltFromTheHistoryCutAtItsSessionAreTheStoredOnesAndOneReadASessionAheadIsNamed()
    {
        var (calendar, inputs, market) = PointInTimeHistory();

        // The readings as the build reads them, off the whole history, on the sixtieth session.
        var sessionAt = calendar.Select((day, at) => (day, at)).ToDictionary(pair => pair.day, pair => pair.at);
        var series = inputs.Names.Select(name => SweepColumns.Series(name, sessionAt)).ToArray();
        var columns = SweepColumns.Sessions(series, inputs.Sessions);
        var members = SweepBenchmark.On(series, calendar.Count);
        var (highs, lows) = SweepIdeas.HighsAndLows(series, members, calendar.Count);
        var at = 60;
        var whole = LedgerSetups.Readings(
            IndexFamilies.LargeIndex,
            0,
            series[0],
            [.. series[0].Bars.Select(bar => Statistic.FromPrice(bar.Close))],
            EquityBrief.Worker.Indices.IndexNightRead.BarOf(series[0], at),
            columns[at],
            members,
            calendar,
            at,
            market,
            new LedgerContext(NoIncome, NoSectors, highs, lows, NoFacts));

        // Rebuilt from the history cut at that session: the same readings, the VIX's and the index's among them.
        var rebuilt = LedgerPointInTime.Rebuilt(
            IndexFamilies.LargeIndex,
            LedgerPointInTime.Cut(inputs, calendar[at]),
            LedgerPointInTime.Cut(market, calendar[at]),
            NoIncome,
            NoSectors,
            NoFacts,
            "AAA")!;

        Assert.Equal(calendar[at], LedgerPointInTime.Cut(inputs, calendar[at]).Sessions[^1]);
        Assert.NotNull(rebuilt[LedgerReadings.IndexOf("vix")]);
        Assert.NotNull(rebuilt[LedgerReadings.IndexOf("close_over_twenty")]);
        Assert.Empty(LedgerPointInTime.Differences(whole, rebuilt));

        // A stored VIX reading taken from the session after, 15 plus 61 sessions' wobble against 15 plus 60's, is
        // named by its column with both figures, and nothing else is.
        var ahead = (double?[])whole.Clone();

        ahead[LedgerReadings.IndexOf("vix")] = 15.0 + (61 % 7);

        Assert.Equal([("vix", (double?)(15.0 + (61 % 7)), (double?)(15.0 + (60 % 7)))], LedgerPointInTime.Differences(ahead, rebuilt));

        // A stock the cut holds no bar of reads none.
        Assert.Null(LedgerPointInTime.Rebuilt(IndexFamilies.LargeIndex, LedgerPointInTime.Cut(inputs, calendar[at]), market, NoIncome, NoSectors, NoFacts, "ZZZ"));
        Assert.Equal(25, SetupLedger.CheckPerYear);
    }

    // From the 17.3 correction of 2026-10-09: on its 65th session AAA closes at 134, above the 133.5 high of the 63
    // sessions before, on volume above its average and an even range, a breakout setup. Its own readings, the volume
    // multiple and the range ratio, are rebuilt by the breakout's own gates over the history cut there, where the check
    // read every setup's readings alone and named both as none on every swing setup it sampled.
    [Fact]
    public void ASwingSetupsOwnReadingsAreRebuiltByItsFamilysGatesOverTheHistoryCutAtItsSession()
    {
        var (calendar, inputs, market) = PointInTimeHistory();
        var sessionAt = calendar.Select((day, at) => (day, at)).ToDictionary(pair => pair.day, pair => pair.at);
        var series = inputs.Names.Select(name => SweepColumns.Series(name, sessionAt)).ToArray();
        var columns = SweepColumns.Sessions(series, inputs.Sessions);
        var members = SweepBenchmark.On(series, calendar.Count);
        var (highs, lows) = SweepIdeas.HighsAndLows(series, members, calendar.Count);
        var at = 64;
        var stored = LedgerSetups.On(IndexFamilies.LargeIndex, series, columns, members, calendar, at, market, new LedgerContext(NoIncome, NoSectors, highs, lows, NoFacts))
            .Single(setups => setups.Family == BreakoutRule.Name)
            .Rows
            .Single(row => row.Ticker == "AAA");
        var cut = LedgerPointInTime.Cut(inputs, calendar[at]);
        var cutMarket = LedgerPointInTime.Cut(market, calendar[at]);

        Assert.Equal(134m, inputs.Names[0].Bars[at].Close);
        Assert.Equal(133.5m, inputs.Names[0].Bars.Skip(at - 63).Take(63).Max(bar => bar.High));

        var (held, rebuilt) = LedgerPointInTime.Setup(IndexFamilies.LargeIndex, cut, cutMarket, NoIncome, NoSectors, NoFacts, BreakoutRule.Name, "AAA");

        Assert.True(held);
        Assert.NotNull(rebuilt![LedgerReadings.IndexOf("volume_multiple")]);
        Assert.NotNull(rebuilt[LedgerReadings.IndexOf("range_ratio")]);
        Assert.Empty(LedgerPointInTime.Differences(stored.Readings, rebuilt));

        // The readings every setup carries leave the family's own as none, which the check read before the correction.
        var shared = LedgerPointInTime.Rebuilt(IndexFamilies.LargeIndex, cut, cutMarket, NoIncome, NoSectors, NoFacts, "AAA")!;

        Assert.Equal(
            [("volume_multiple", stored.Reading("volume_multiple"), (double?)null), ("range_ratio", stored.Reading("range_ratio"), (double?)null)],
            LedgerPointInTime.Differences(stored.Readings, shared));

        // A stored range ratio a hundredth over the one the gates read is named with both figures, and nothing else is.
        var off = (double?[])stored.Readings.Clone();

        off[LedgerReadings.IndexOf("range_ratio")] = stored.Reading("range_ratio") + 0.01;

        Assert.Equal([("range_ratio", off[LedgerReadings.IndexOf("range_ratio")], rebuilt[LedgerReadings.IndexOf("range_ratio")])], LedgerPointInTime.Differences(off, rebuilt));

        // No report reaches AAA, so the drift's gates pass no setup of it there; and a stock the cut holds no bar of is
        // held by none.
        Assert.Equal((true, (double?[]?)null), LedgerPointInTime.Setup(IndexFamilies.LargeIndex, cut, cutMarket, NoIncome, NoSectors, NoFacts, DriftRule.Name, "AAA"));
        Assert.False(LedgerPointInTime.Setup(IndexFamilies.LargeIndex, cut, cutMarket, NoIncome, NoSectors, NoFacts, BreakoutRule.Name, "ZZZ").Held);
    }

    // From the 17.3 correction of 2026-10-09: a member pulled at 100 on each of 30 sessions and stored from the 11th at
    // 200 and a point more a session, so the stored close over the pulled one rises from 2.00 to 2.19. Read through the
    // 20th session the pulled years are priced at the median of 2.00 to 2.09, 204.5; read through the 30th, at that of 2.00
    // to 2.19, 209.5. The check reads the history through the session the build read it through, the newest its history
    // rows reach, and not through the newest setup it sampled.
    [Fact]
    public async Task TheCheckReadsTheHistoryThroughTheSessionTheBuildReadItThroughWhereThePulledYearsArePriced()
    {
        using var store = new TemporaryStore().Migrated();
        var days = new List<DateOnly>();

        for (var day = new DateOnly(2026, 1, 5); days.Count < 30; day = day.AddDays(1))
        {
            if (day.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday)
            {
                days.Add(day);
            }
        }

        string Stamp(DateOnly day) => day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);

        store.Execute("INSERT INTO membership (index_code, ticker, joined, \"left\", observed_at) VALUES ('GSPC', 'AAA', NULL, NULL, '2026-02-13T21:10:00Z');");

        for (var at = 0; at < days.Count; at++)
        {
            store.Execute($"INSERT INTO pulled_bar (ticker, session_date, open, high, low, close, raw_close, volume, pull) VALUES ('AAA', '{Stamp(days[at])}', '100', '101', '99', '100', '100', 1000, 'history-pull-test');");

            if (at >= 10)
            {
                var close = 200 + (at - 10);

                store.Execute($"INSERT INTO bar (ticker, session_date, open, high, low, close, volume, source, observed_at, raw_close) VALUES ('AAA', '{Stamp(days[at])}', '{close}', '{close + 1}', '{close - 1}', '{close}', 1000, 'bulk', '2026-02-13T21:10:00Z', '{close}');");
            }
        }

        var history = new SweepHistory(store.DatabaseFile);

        Assert.Equal(204.5m, (await history.ReadAsync(days[19])).Names.Single().Bars[0].Close);
        Assert.Equal(209.5m, (await history.ReadAsync(days[29])).Names.Single().Bars[0].Close);

        // The build's rows reach the 30th session, and the one setup sampled stands on the 16th.
        foreach (var at in new[] { 15, 29 })
        {
            store.Execute($"INSERT INTO setup_night (index_code, family, session_date, members, setups, live_passes, source) VALUES ('GSPC', 'breakout', '{Stamp(days[at])}', 1, {(at == 15 ? 1 : 0)}, 0, 'history');");
        }

        store.Execute(
            "INSERT INTO setup (index_code, family, ticker, session_date, rule, live_pass, entry, stop, cap, end, settled, source, pin) "
            + $"VALUES ('GSPC', 'breakout', 'AAA', '{Stamp(days[15])}', 'the breakout', 0, '205', '201', 63, 'open', 0, 'history', '{LedgerReadings.Version}');");

        Assert.Equal(days[29], SetupLedger.HistoryThrough(Stamp(days[29]), days[15]));
        Assert.Equal(days[15], SetupLedger.HistoryThrough(null, days[15]));
        Assert.Equal(days[15], SetupLedger.HistoryThrough(Stamp(days[10]), days[15]));

        using var output = new StringWriter();

        await new SetupLedger(FixedClock.At(new DateTimeOffset(2026, 2, 16, 15, 0, 0, TimeSpan.Zero), SessionZones.UnitedStates), store.DatabaseFile).CheckAsync(IndexFamilies.LargeIndex, output);

        SweepRelease(store);

        Assert.Contains($"rebuilt from the history read through {Stamp(days[29])} and cut at each one's session", output.ToString(), StringComparison.Ordinal);
    }
}
