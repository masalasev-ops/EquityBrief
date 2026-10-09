using EquityBrief.Core.Ledger;
using EquityBrief.Core.Prices;
using EquityBrief.Core.Readings;
using EquityBrief.Core.Sweep;
using EquityBrief.Worker.Indices;
using EquityBrief.Worker.Ledger;
using EquityBrief.Worker.Sweep;

namespace EquityBrief.Tests.Checks;

// fixture-expectations, 17.3: the ledger's point-in-time check over a constructed history. A setup's readings read off
// the whole history are the readings rebuilt from the history cut at its session, so nothing past the session reached
// them; and a stored reading taken from the session after is named as differing, by its column, with both figures.
// see: A setup is every member-session a family's loose gates pass, and its readings are defined once and read as they stood
public partial class FixtureExpectations
{
    [Fact]
    public void ASetupsReadingsRebuiltFromTheHistoryCutAtItsSessionAreTheStoredOnesAndOneReadASessionAheadIsNamed()
    {
        var calendar = new List<DateOnly>();

        for (var day = new DateOnly(2026, 1, 5); calendar.Count < 80; day = day.AddDays(1))
        {
            if (day.DayOfWeek is not DayOfWeek.Saturday and not DayOfWeek.Sunday)
            {
                calendar.Add(day);
            }
        }

        // Three members, each drifting at its own pace with a weekly wobble, and the index and the VIX moving every
        // session, so every reading that reads past its session would read a different figure.
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
        var income = new Dictionary<string, IReadOnlyList<FiledIncome>>(StringComparer.Ordinal);
        var sectors = new Dictionary<string, string?>(StringComparer.Ordinal);
        var facts = new Dictionary<string, IReadOnlyList<FiledFactRow>>(StringComparer.Ordinal);

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
            new LedgerContext(income, sectors, highs, lows, facts));

        // Rebuilt from the history cut at that session: the same readings, the VIX's and the index's among them.
        var rebuilt = LedgerPointInTime.Rebuilt(
            IndexFamilies.LargeIndex,
            LedgerPointInTime.Cut(inputs, calendar[at]),
            LedgerPointInTime.Cut(market, calendar[at]),
            income,
            sectors,
            facts,
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
        Assert.Null(LedgerPointInTime.Rebuilt(IndexFamilies.LargeIndex, LedgerPointInTime.Cut(inputs, calendar[at]), market, income, sectors, facts, "ZZZ"));
        Assert.Equal(25, SetupLedger.CheckPerYear);
    }
}
